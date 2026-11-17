#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Unattended setup of SSH access for a PUBLISHED Bonobo Git Server site running under IIS.

.DESCRIPTION
    The counterpart of Setup-LocalSsh.ps1 for a server: it works from the published site folder
    (the one IIS points at), never from a source checkout, and builds nothing. Idempotent: safe to
    re-run after a new publish. It will:
      1. Install & start Windows OpenSSH Server (falling back to a manual Win32-OpenSSH install
         if the Windows Feature has no Feature-on-Demand payload for this build).
      2. Create the local 'git' account used as the sshd forced-command identity.
      3. Deploy Bonobo.Git.Server.SshShell outside the web root (default
         C:\ProgramData\Bonobo\ssh-shell) from App_Data\ssh-shell in the published output, or from
         the folder given with -ShellSource, and write its .exe.config from the site's web.config
         with every path made absolute.
      4. Create C:\ProgramData\Bonobo\ssh for authorized_keys and ACL it so the application pool
         identity can rewrite it and 'git' can read it.
      5. Grant the 'git' account traverse rights down to the site and Modify on App_Data (it
         reads git.exe and the database, writes the SQLite journal, pushes and logs there).
      6. Patch App_Data\config.xml: absolute Repositories path, SshEnabled, SshHost.
      7. Patch web.config's SshAuthorizedKeysPath / SshShellPath / SshServiceAccount.
      8. Insert a "Match User git" block into sshd_config (before "Match Group
         administrators" if present), validate it and restart sshd.
      9. Recycle the application pool so the site regenerates authorized_keys.

    The application pool identity is read from IIS by matching the site (or application) whose
    physical path is the published folder. Give -AppPoolIdentity when that lookup cannot work, for
    example when the folder is not the physical path of any IIS site yet.

    Run it from an elevated PowerShell. Windows Server's default execution policy refuses scripts
    that are not signed, so start it as:

        powershell -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 -SshHostName git.example.com

    Review it first: it creates a local Windows account, changes sshd_config, edits ACLs, and
    (only if the Windows Feature install fails) downloads Microsoft's Win32-OpenSSH release from
    https://github.com/PowerShell/Win32-OpenSSH/releases.

.PARAMETER SiteRoot
    The published site folder, i.e. the IIS physical path. Defaults to the folder this script is
    in, since it ships at the root of the published output.

.PARAMETER ShellSource
    Folder holding the Bonobo.Git.Server.SshShell build output (the exe, its .config and every
    dll beside it). Defaults to App_Data\ssh-shell under the site, which the publish step fills
    from the SshShell project's output. Copy that project's bin\Release here by hand if the
    published output does not contain it.

.PARAMETER ShellDir
    Where the shell is deployed to. Outside the web root, readable by 'git', not writable by the
    application pool identity.

.PARAMETER KeysDir
    Folder holding authorized_keys. The application pool identity gets Modify on it, 'git' gets
    read.

.PARAMETER GitAccount
    Local Windows account sshd will authenticate the forced command as.

.PARAMETER SshHostName
    Value written to config.xml's SshHost (what Bonobo shows in clone URLs). Defaults to the
    host name of the site's first binding, else the computer name.

.PARAMETER AppPoolIdentity
    Account the application pool runs as, e.g. 'IIS AppPool\git.example.com' or 'DOMAIN\svc'.
    Detected from IIS when omitted.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 -SshHostName git.example.com
    powershell -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 -SiteRoot 'D:\sites\git' -ShellSource 'D:\drop\SshShell' -GitAccount gitssh
#>
[CmdletBinding()]
param(
    [string]$SiteRoot        = '',
    [string]$ShellSource     = '',
    [string]$ShellDir        = 'C:\ProgramData\Bonobo\ssh-shell',
    [string]$KeysDir         = 'C:\ProgramData\Bonobo\ssh',
    [string]$GitAccount      = 'git',
    [string]$SshHostName     = '',
    [string]$AppPoolIdentity = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------------
# Resolved here rather than as the parameter's default: under 'powershell -File', $PSScriptRoot
# is still empty while the param block's defaults are evaluated.
if ([string]::IsNullOrWhiteSpace($SiteRoot)) {
    $SiteRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
}
$SiteRoot = (Resolve-Path $SiteRoot).Path.TrimEnd('\')
$AppData  = Join-Path $SiteRoot 'App_Data'
$ShellExeName       = 'Bonobo.Git.Server.SshShell.exe'
if ([string]::IsNullOrWhiteSpace($ShellSource)) {
    $ShellSource = Join-Path $AppData 'ssh-shell'
    if (-not (Test-Path (Join-Path $ShellSource $ShellExeName))) {
        throw @"
The published site has no shell at $ShellSource.
The publish step only includes it when Bonobo.Git.Server.SshShell was built in the same configuration
before publishing. Either rebuild the solution, publish again and copy the output here, or copy the
Bonobo.Git.Server.SshShell\bin\Release folder from the build machine to this server and run again with
    -ShellSource '<that folder>'
"@
    }
}
$ShellExePath       = Join-Path $ShellDir $ShellExeName
$AuthorizedKeysPath = Join-Path $KeysDir 'authorized_keys'
$SshdConfigPath     = "$env:ProgramData\ssh\sshd_config"
$WebConfigPath      = Join-Path $SiteRoot 'web.config'
$ConfigXmlPath      = Join-Path $AppData 'config.xml'

foreach ($p in @($SiteRoot, $AppData, $WebConfigPath, $ShellSource, (Join-Path $ShellSource $ShellExeName))) {
    if (-not (Test-Path $p)) { throw "Expected path not found: $p" }
}
if (-not (Test-Path (Join-Path $SiteRoot 'bin\Bonobo.Git.Server.dll'))) {
    throw "$SiteRoot does not look like a published Bonobo Git Server site (no bin\Bonobo.Git.Server.dll)."
}

# ---------------------------------------------------------------------------
Write-Step "IIS site and application pool"
$iisSiteName = $null
$iisAppPool  = $null
$iisHost     = $null
Import-Module WebAdministration -ErrorAction SilentlyContinue
if (Get-Module WebAdministration) {
    function Test-SamePath($a, $b) {
        $ea = [Environment]::ExpandEnvironmentVariables([string]$a).TrimEnd('\')
        return [string]::Equals($ea, $b, [StringComparison]::OrdinalIgnoreCase)
    }
    foreach ($site in Get-Website) {
        if (Test-SamePath $site.physicalPath $SiteRoot) {
            $iisSiteName = $site.name
            $iisAppPool  = $site.applicationPool
            $binding = @($site.bindings.Collection | Where-Object { $_.protocol -in 'http', 'https' }) |
                       Select-Object -First 1
            if ($binding) { $iisHost = ($binding.bindingInformation -split ':')[-1] }
            break
        }
        foreach ($app in @(Get-WebApplication -Site $site.name)) {
            if (Test-SamePath $app.PhysicalPath $SiteRoot) {
                $iisSiteName = "$($site.name)$($app.path)"
                $iisAppPool  = $app.applicationPool
                break
            }
        }
        if ($iisAppPool) { break }
    }
} else {
    Write-Warning "The WebAdministration module is not available; IIS cannot be queried."
}

if ($iisAppPool) {
    Write-Info "IIS: '$iisSiteName' runs in application pool '$iisAppPool'."
    if ([string]::IsNullOrWhiteSpace($AppPoolIdentity)) {
        $pool = Get-Item "IIS:\AppPools\$iisAppPool"
        switch ([string]$pool.processModel.identityType) {
            'ApplicationPoolIdentity' { $AppPoolIdentity = "IIS AppPool\$iisAppPool" }
            'SpecificUser'            { $AppPoolIdentity = $pool.processModel.userName }
            'NetworkService'          { $AppPoolIdentity = '*S-1-5-20' }
            'LocalService'            { $AppPoolIdentity = '*S-1-5-19' }
            'LocalSystem'             { $AppPoolIdentity = '*S-1-5-18' }
            default                   { throw "Unknown application pool identity type '$($pool.processModel.identityType)'. Pass -AppPoolIdentity." }
        }
    }
} elseif ([string]::IsNullOrWhiteSpace($AppPoolIdentity)) {
    throw "No IIS site or application has '$SiteRoot' as its physical path. Create the site first, or pass -AppPoolIdentity."
}
Write-Info "Application pool identity: $AppPoolIdentity"

if ([string]::IsNullOrWhiteSpace($SshHostName)) {
    if (-not [string]::IsNullOrWhiteSpace($iisHost) -and $iisHost -ne '*') { $SshHostName = $iisHost }
    else { $SshHostName = $env:COMPUTERNAME.ToLowerInvariant() }
    Write-Info "SSH host name not given; using '$SshHostName'."
}

# ---------------------------------------------------------------------------
Write-Step "1. Windows OpenSSH Server"
function Ensure-SshdFirewallRule {
    if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' `
            -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
        Write-Info "Opened TCP 22 inbound in Windows Firewall."
    }
}
function Invoke-ManualSshdInstall($dir) {
    Push-Location $dir
    try { & (Join-Path $dir 'install-sshd.ps1') } finally { Pop-Location }
}

$sshdSvc = Get-Service sshd -ErrorAction SilentlyContinue
if ($sshdSvc) {
    Write-Info "sshd service already present ($($sshdSvc.Status))."
} else {
    $cap = Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0'
    $viaFeature = $false
    if ($cap.State -eq 'Installed') {
        $viaFeature = $true
    } else {
        Write-Info "Installing OpenSSH.Server capability via Windows Update..."
        try {
            Add-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0' -ErrorAction Stop | Out-Null
            $viaFeature = $true
        } catch {
            Write-Warning "Add-WindowsCapability failed: $($_.Exception.Message)"
            Write-Info "Falling back to a manual Win32-OpenSSH install (this build likely has no"
            Write-Info "matching Feature-on-Demand payload published yet - CBS_E_NO_OPTIONAL_CONTENT_FOUND_FOR_BUILD)."
        }
    }

    if (-not $viaFeature) {
        # Microsoft's Win32-OpenSSH port: the same sshd binaries as the Windows Feature, fetched as
        # a release asset instead of through Windows Update.
        $installDir = Join-Path $env:ProgramFiles 'OpenSSH'
        if (-not (Test-Path (Join-Path $installDir 'sshd.exe'))) {
            Write-Info "Downloading latest Win32-OpenSSH release..."
            $release = Invoke-RestMethod 'https://api.github.com/repos/PowerShell/Win32-OpenSSH/releases/latest'
            $asset = $release.assets | Where-Object { $_.name -match 'win64\.zip$' } | Select-Object -First 1
            if (-not $asset) { throw "Could not find a win64.zip asset in the latest Win32-OpenSSH release." }
            $zipPath = Join-Path $env:TEMP $asset.name
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing
            $extractDir = Join-Path $env:TEMP ([IO.Path]::GetFileNameWithoutExtension($asset.name))
            Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
            $srcDir = Get-ChildItem $extractDir -Directory | Select-Object -First 1
            if (-not $srcDir) { $srcDir = Get-Item $extractDir }
            New-Item -ItemType Directory -Force -Path $installDir | Out-Null
            Copy-Item (Join-Path $srcDir.FullName '*') $installDir -Recurse -Force
            Remove-Item $zipPath, $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Info "Win32-OpenSSH already present in $installDir."
        }
        Invoke-ManualSshdInstall $installDir
    }
}

if (-not (Get-Service sshd -ErrorAction SilentlyContinue)) {
    $rebootPending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                     (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
    if ($rebootPending) {
        throw "The sshd service is not registered yet and a Windows restart is pending. Restart the machine, then run this script again."
    }
    $manualDir = Join-Path $env:ProgramFiles 'OpenSSH'
    if (Test-Path (Join-Path $manualDir 'install-sshd.ps1')) {
        Write-Info "sshd binaries are present but the service is not registered; running install-sshd.ps1 from $manualDir..."
        Invoke-ManualSshdInstall $manualDir
    }
    if (-not (Get-Service sshd -ErrorAction SilentlyContinue)) {
        throw "The sshd service is still not registered. Check C:\Windows\Logs\CBS\CBS.log, or remove the Windows feature / C:\Program Files\OpenSSH and re-run."
    }
}

Ensure-SshdFirewallRule
Set-Service -Name sshd -StartupType Automatic
if ((Get-Service sshd).Status -ne 'Running') { Start-Service sshd }
Write-Info "sshd service: $((Get-Service sshd).Status)"

# sshd.exe lives wherever the service was registered from (System32\OpenSSH for the Windows
# feature, Program Files\OpenSSH for the manual install).
$SshdExePath = ((Get-CimInstance Win32_Service -Filter "Name='sshd'").PathName -replace '^"([^"]+)".*$', '$1').Trim()
if (-not (Test-Path $SshdExePath)) { $SshdExePath = "$env:WINDIR\System32\OpenSSH\sshd.exe" }

# ---------------------------------------------------------------------------
Write-Step "2. Local '$GitAccount' account"
$existing = Get-LocalUser -Name $GitAccount -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Info "Creating local user '$GitAccount' (no password)..."
    New-LocalUser -Name $GitAccount -NoPassword -UserMayNotChangePassword | Out-Null
    Set-LocalUser -Name $GitAccount -PasswordNeverExpires $true
} else {
    Write-Info "Already exists."
}

# ---------------------------------------------------------------------------
Write-Step "3. Deploy the shell to $ShellDir"
New-Item -ItemType Directory -Force -Path $ShellDir | Out-Null
Copy-Item (Join-Path $ShellSource '*') $ShellDir -Recurse -Force
Write-Info "Copied the shell and its dependencies from $ShellSource."

# The shell shares the site's configuration but runs outside IIS, so every path has to be
# absolute. Take the shipped .exe.config as the template (it carries the binding redirects and the
# provider registrations) and overwrite the settings that describe the installation with the
# site's own web.config values, resolving '~\' against the site root and |DataDirectory| against
# App_Data, which is what ASP.NET would do for the web site.
function Resolve-SitePath([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $value }
    $v = $value.Replace('|DataDirectory|', ($AppData + '\'))
    if ($v -match '^~[\\/](.*)$') { $v = Join-Path $SiteRoot $Matches[1] }
    elseif ($v -eq '~') { $v = $SiteRoot }
    return $v
}
function Get-AppSettingNode($doc, $key) { $doc.SelectSingleNode("/configuration/appSettings/add[@key='$key']") }
function Set-AppSetting($doc, $key, $value) {
    # SetAttribute, not '$entry.value =': PowerShell resolves .value to XmlNode.Value, not the attribute.
    $node = $doc.SelectSingleNode('/configuration/appSettings')
    $entry = $node.SelectSingleNode("add[@key='$key']")
    if ($entry) { $entry.SetAttribute('value', [string]$value) }
    else {
        $new = $doc.CreateElement('add')
        $new.SetAttribute('key', $key)
        $new.SetAttribute('value', [string]$value)
        $node.AppendChild($new) | Out-Null
    }
}

$webConfigXml = New-Object System.Xml.XmlDocument
$webConfigXml.PreserveWhitespace = $true
$webConfigXml.Load($WebConfigPath)

$shellCfgPath = Join-Path $ShellDir ($ShellExeName + '.config')
$shellCfgXml = New-Object System.Xml.XmlDocument
$shellCfgXml.PreserveWhitespace = $true
$shellCfgXml.Load($shellCfgPath)

$pathKeys   = @('UserConfiguration', 'GitPath', 'GitHomePath', 'LogDirectory', 'ActiveDirectoryBackendPath')
$mirrorKeys = @('MembershipService', 'ActiveDirectoryDefaultDomain', 'ActiveDirectoryMemberGroupName',
                'ActiveDirectoryTeamMapping', 'ActiveDirectoryRoleMapping', 'serilog:minimum-level')
foreach ($key in $pathKeys + $mirrorKeys) {
    $src = Get-AppSettingNode $webConfigXml $key
    if ($null -eq $src) { continue }
    $value = $src.GetAttribute('value')
    if ($key -in $pathKeys) { $value = Resolve-SitePath $value }
    Set-AppSetting $shellCfgXml $key $value
    Write-Info "$key = $value"
}
$webConn   = $webConfigXml.SelectSingleNode("/configuration/connectionStrings/add[@name='BonoboGitServerContext']")
$shellConn = $shellCfgXml.SelectSingleNode("/configuration/connectionStrings/add[@name='BonoboGitServerContext']")
if ($null -eq $webConn)   { throw "web.config has no BonoboGitServerContext connection string." }
if ($null -eq $shellConn) { throw "$shellCfgPath has no BonoboGitServerContext connection string." }
$shellConn.SetAttribute('connectionString', (Resolve-SitePath $webConn.GetAttribute('connectionString')))
$shellConn.SetAttribute('providerName', $webConn.GetAttribute('providerName'))
Write-Info "connectionString = $($shellConn.GetAttribute('connectionString'))"
$shellCfgXml.Save($shellCfgPath)
Write-Info "Wrote $shellCfgPath"

# The shell runs as 'git'; nothing else needs to write here, and the application pool identity
# must not be able to replace what sshd executes.
icacls $ShellDir /inheritance:r | Out-Null
icacls $ShellDir /grant "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
icacls $ShellDir /grant "${GitAccount}:(OI)(CI)RX" | Out-Null
Write-Info "ACL'd $ShellDir for SYSTEM/Administrators (full) and $GitAccount (read)."

# ---------------------------------------------------------------------------
Write-Step "4. authorized_keys folder $KeysDir"
New-Item -ItemType Directory -Force -Path $KeysDir | Out-Null
# Bonobo (the application pool identity) rewrites the file; 'git' only needs to read it.
# Well-known SIDs for SYSTEM and BUILTIN\Administrators: the group names are localized.
icacls $KeysDir /inheritance:r | Out-Null
icacls $KeysDir /grant "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
icacls $KeysDir /grant "${AppPoolIdentity}:(OI)(CI)M" | Out-Null
icacls $KeysDir /grant "${GitAccount}:(OI)(CI)RX" | Out-Null
Write-Info "ACL'd $KeysDir for $AppPoolIdentity (modify) and $GitAccount (read)."

# ---------------------------------------------------------------------------
Write-Step "5. Grant '$GitAccount' access to the site"
# Traverse-only on every parent so git can reach App_Data without listing anything else.
$chain = New-Object System.Collections.Generic.List[string]
$current = Split-Path $SiteRoot -Parent
while ($current) {
    $chain.Add($current)
    $up = Split-Path $current -Parent
    if ([string]::IsNullOrEmpty($up) -or $up -eq $current) { break }
    $current = $up
}
$chain.Add($SiteRoot)
foreach ($p in $chain) {
    if (Test-Path $p) { icacls $p /grant "${GitAccount}:(X)" | Out-Null }
}
# Modify on App_Data as a whole: git.exe and the database are read from here, but a push writes
# the repository, every connection updates the key's last-used time (SQLite needs to create its
# journal beside the database for that) and the shell writes App_Data\Logs\ssh-*.txt.
icacls $AppData /grant "${GitAccount}:(OI)(CI)M" | Out-Null
Write-Info "Granted traverse on parents and Modify on App_Data\."

# ---------------------------------------------------------------------------
Write-Step "6. Patch App_Data\config.xml"
# Before web.config: saving web.config restarts the application, and it caches config.xml in
# memory, so config.xml has to be final by then.
$appCfgXml = New-Object System.Xml.XmlDocument
$appCfgXml.PreserveWhitespace = $true
if (Test-Path $ConfigXmlPath) {
    $appCfgXml.Load($ConfigXmlPath)
} else {
    # The site writes this file on first start. A partial document is fine: the site fills every
    # element it does not find with its default, exactly as it does on first start.
    Write-Info "config.xml does not exist yet (site never started?); creating a minimal one."
    $appCfgXml.LoadXml('<?xml version="1.0"?><Configuration xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"></Configuration>')
}
function Set-ConfigNode($doc, $name, $value) {
    $node = $doc.SelectSingleNode("/Configuration/$name")
    if ($null -eq $node) {
        $node = $doc.CreateElement($name)
        $doc.DocumentElement.AppendChild($node) | Out-Null
    }
    $node.InnerText = [string]$value
}
# Absolute, because the shell resolves '~\' against its own folder, not the site.
$repositoriesNode = $appCfgXml.SelectSingleNode('/Configuration/Repositories')
$repositories = if ($repositoriesNode) { Resolve-SitePath $repositoriesNode.InnerText } else { Join-Path $AppData 'Repositories' }
Set-ConfigNode $appCfgXml 'Repositories' $repositories
Set-ConfigNode $appCfgXml 'SshEnabled' 'true'
Set-ConfigNode $appCfgXml 'SshHost' $SshHostName
$appCfgXml.Save($ConfigXmlPath)
Write-Info "Repositories = $repositories"
Write-Info "SshEnabled = true, SshHost = $SshHostName"

# ---------------------------------------------------------------------------
Write-Step "7. Patch web.config"
Set-AppSetting $webConfigXml 'SshAuthorizedKeysPath' $AuthorizedKeysPath
Set-AppSetting $webConfigXml 'SshShellPath' $ShellExePath
Set-AppSetting $webConfigXml 'SshServiceAccount' $GitAccount
$webConfigXml.Save($WebConfigPath)
Write-Info "SshAuthorizedKeysPath = $AuthorizedKeysPath"
Write-Info "SshShellPath          = $ShellExePath"
Write-Info "SshServiceAccount     = $GitAccount"

# ---------------------------------------------------------------------------
Write-Step "8. sshd_config"
$sshdLines = Get-Content $SshdConfigPath
$alreadyThere = $sshdLines -match ('^\s*Match\s+User\s+' + [regex]::Escape($GitAccount) + '\b') | Select-Object -First 1
if ($alreadyThere) {
    Write-Info "'Match User $GitAccount' block already present; leaving sshd_config untouched."
} else {
    $block = @(
        "Match User $GitAccount"
        "    AuthorizedKeysFile $AuthorizedKeysPath"
        "    PubkeyAuthentication yes"
        "    PasswordAuthentication no"
        "    AllowTcpForwarding no"
        "    PermitTTY no"
        "    X11Forwarding no"
        ""
    )
    $adminMatchIndex = ($sshdLines | Select-String -Pattern '^\s*Match\s+Group\s+administrators\b' | Select-Object -First 1).LineNumber
    if ($adminMatchIndex) {
        $insertAt = $adminMatchIndex - 1  # LineNumber is 1-based
        $newLines = $sshdLines[0..($insertAt - 1)] + $block + $sshdLines[$insertAt..($sshdLines.Length - 1)]
        Write-Info "Inserted block before existing 'Match Group administrators'."
    } else {
        $newLines = $sshdLines + $block
        Write-Info "Appended block at end of file."
    }
    Copy-Item $SshdConfigPath "$SshdConfigPath.bak" -Force
    # No BOM: Windows PowerShell 5.1's 'Set-Content -Encoding UTF8' adds one, which sshd can reject.
    [IO.File]::WriteAllLines($SshdConfigPath, [string[]]$newLines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Info "Backed up previous file to $SshdConfigPath.bak"
}

# sshd -t writes its findings to stderr; with ErrorActionPreference=Stop, Windows PowerShell would
# turn that stderr into a terminating error instead of letting us report it.
$ErrorActionPreference = 'Continue'
$sshdTest = & $SshdExePath -t -f $SshdConfigPath 2>&1
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) {
    Write-Warning "sshd -t reported a problem with sshd_config:`n$sshdTest"
    Write-Warning "Not restarting sshd. Fix $SshdConfigPath (backup at $SshdConfigPath.bak) and run Restart-Service sshd yourself."
} else {
    Restart-Service sshd
    Write-Info "sshd_config OK, sshd restarted."
}

# ---------------------------------------------------------------------------
Write-Step "9. Recycle the application pool"
if ($iisAppPool) {
    Restart-WebAppPool -Name $iisAppPool
    Write-Info "Recycled '$iisAppPool'; the site rewrites $AuthorizedKeysPath on its next request."
} else {
    Write-Info "No IIS application pool detected; recycle the site's pool yourself."
}

# ---------------------------------------------------------------------------
Write-Step "Done"
Write-Host @"

Next steps:
  1. Open the site once so it starts and writes $AuthorizedKeysPath.
  2. Add a public key under Account -> SSH Keys.
  3. Test from a client machine:
       ssh -o IdentitiesOnly=yes -T $GitAccount@$SshHostName
     Being refused with "This account is only for git access" is success -
     it means key auth worked and the forced command ran.
  4. Clone/fetch:
       git ls-remote $GitAccount@${SshHostName}:YourRepo.git

Re-run this script after every publish: a publish overwrites web.config and the shell may
have changed. If it fails, check:
  - Event Viewer > Applications and Services Logs > OpenSSH > Operational
  - $AppData\Logs\ssh-*.txt (written by the shell itself)
"@
