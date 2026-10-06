#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Unattended local setup of SSH access for Bonobo Git Server, running the shell
    from the web project's own bin\ folder (see chat: "run the shell from the bin folder").

.DESCRIPTION
    Idempotent: safe to re-run after a rebuild. It will:
      1. Install & start Windows OpenSSH Server (falling back to a manual Win32-OpenSSH
         install if the Windows Feature has no Feature-on-Demand payload for this build).
      2. Create the local 'git' account used as the sshd forced-command identity.
      3. Build Bonobo.Git.Server.SshShell (Debug) if msbuild is available.
      4. Copy the shell exe/pdb into Bonobo.Git.Server\bin\ and regenerate its
         .exe.config with absolute paths into this checkout's App_Data.
      5. Create C:\ProgramData\Bonobo\ssh for authorized_keys, ACL it.
      6. Grant the 'git' account traverse/read rights on the repo, bin\, App_Data\.
      7. Patch web.config's SshAuthorizedKeysPath / SshShellPath / SshServiceAccount.
      8. Patch config.xml: absolute Repositories path, SshEnabled, SshHost.
      9. Insert a "Match User git" block into sshd_config (before "Match Group
         administrators" if present) and restart sshd.

    Run this yourself, elevated. It was generated for you to review before running -
    it creates a local Windows account, changes sshd_config, edits ACLs, and (only if
    the Windows Feature install fails) downloads Microsoft's Win32-OpenSSH release from
    https://github.com/PowerShell/Win32-OpenSSH/releases.

.PARAMETER RepoRoot
    Path to the Bonobo-Git-Server checkout.

.PARAMETER GitAccount
    Local Windows account sshd will authenticate the forced command as.

.PARAMETER SshHostName
    Value written to config.xml's SshHost (what Bonobo shows in clone URLs).

.PARAMETER SkipBuild
    Skip building the SshShell project; use whatever is already in its bin\Debug.

.EXAMPLE
    .\Setup-LocalSsh.ps1
    .\Setup-LocalSsh.ps1 -RepoRoot 'D:\src\Bonobo-Git-Server' -GitAccount gitssh
#>
[CmdletBinding()]
param(
    [string]$RepoRoot    = (Join-Path $env:USERPROFILE 'source\repos\Bonobo-Git-Server'),
    [string]$GitAccount  = 'git',
    [string]$SshHostName = 'localhost',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor DarkGray }

# ---------------------------------------------------------------------------
$WebRoot       = Join-Path $RepoRoot 'Bonobo.Git.Server'
$AppData       = Join-Path $WebRoot 'App_Data'
$WebBin        = Join-Path $WebRoot 'bin'
$ShellProj     = Join-Path $RepoRoot 'Bonobo.Git.Server.SshShell'
$ShellCsproj   = Join-Path $ShellProj 'Bonobo.Git.Server.SshShell.csproj'
$ShellAppCfg   = Join-Path $ShellProj 'App.config'
$ShellBinDebug = Join-Path $ShellProj 'bin\Debug'
$SshKeysDir    = 'C:\ProgramData\Bonobo\ssh'
$AuthorizedKeysPath = Join-Path $SshKeysDir 'authorized_keys'
$ShellExePath  = Join-Path $WebBin 'Bonobo.Git.Server.SshShell.exe'
$SshdConfigPath = "$env:ProgramData\ssh\sshd_config"
$WebConfigPath  = Join-Path $WebRoot 'web.config'
$ConfigXmlPath  = Join-Path $AppData 'config.xml'
$InvokingUser   = "$env:USERDOMAIN\$env:USERNAME"

foreach ($p in @($RepoRoot, $WebRoot, $AppData, $ShellProj)) {
    if (-not (Test-Path $p)) { throw "Expected path not found: $p" }
}

# ---------------------------------------------------------------------------
Write-Step "1. Windows OpenSSH Server"
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
        # Manual install of Microsoft's Win32-OpenSSH port. Same sshd/ssh-agent binaries as the
        # Windows Feature, but fetched as a release asset instead of through Windows Update, so it
        # is not gated by Feature-on-Demand availability for this build.
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
        Push-Location $installDir
        try {
            & "$installDir\install-sshd.ps1"
        } finally {
            Pop-Location
        }
        # install-sshd.ps1 registers sshd/ssh-agent but doesn't add sshd.exe's folder to PATH or
        # open the firewall for anything beyond what it sets up itself; add the inbound rule if
        # it didn't already exist.
        if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' `
                -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
        }
    }
}

if (-not (Get-Service sshd -ErrorAction SilentlyContinue)) {
    $rebootPending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
                     (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
    if ($rebootPending) {
        throw "The sshd service is not registered yet and a Windows restart is pending. Restart the machine, then run this script again."
    }

    # The binaries are there but nothing registered the service (seen when the Windows feature and
    # a manual Win32-OpenSSH install have both been laid down). The manual install ships its own
    # registration script, so use it to repair the service entries.
    $manualDir = Join-Path $env:ProgramFiles 'OpenSSH'
    if (Test-Path (Join-Path $manualDir 'install-sshd.ps1')) {
        Write-Info "sshd binaries are present but the service is not registered; running install-sshd.ps1 from $manualDir..."
        Push-Location $manualDir
        try {
            & (Join-Path $manualDir 'install-sshd.ps1')
        } finally {
            Pop-Location
        }
        if (-not (Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH Server (sshd)' `
                -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 | Out-Null
        }
    }

    if (-not (Get-Service sshd -ErrorAction SilentlyContinue)) {
        throw "The sshd service is still not registered. Check C:\Windows\Logs\CBS\CBS.log, or remove the Windows feature / C:\Program Files\OpenSSH and re-run."
    }
}

Set-Service -Name sshd -StartupType Automatic
if ((Get-Service sshd).Status -ne 'Running') { Start-Service sshd }
Write-Info "sshd service: $((Get-Service sshd).Status)"

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
Write-Step "3. Build Bonobo.Git.Server.SshShell"
if ($SkipBuild) {
    Write-Info "Skipped (-SkipBuild)."
} else {
    $msbuildExe = $null
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { $msbuildExe = $cmd.Source }
    if (-not $msbuildExe) {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
            if ($vsPath) { $msbuildExe = $vsPath }
        }
    }
    if (-not $msbuildExe) {
        Write-Info "msbuild.exe not found on PATH; skipping build. Using whatever is already in bin\Debug."
    } else {
        Write-Info "Building with $msbuildExe..."
        & $msbuildExe $ShellCsproj /nologo /m /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Build of $ShellCsproj failed (exit $LASTEXITCODE)." }
    }
}
if (-not (Test-Path (Join-Path $ShellBinDebug 'Bonobo.Git.Server.SshShell.exe'))) {
    throw "Shell exe not found in $ShellBinDebug. Build it first, or remove -SkipBuild."
}

# ---------------------------------------------------------------------------
Write-Step "4. Deploy shell into web bin\ and rewrite its config"
Copy-Item (Join-Path $ShellBinDebug 'Bonobo.Git.Server.SshShell.exe') $WebBin -Force
Copy-Item (Join-Path $ShellBinDebug 'Bonobo.Git.Server.SshShell.pdb') $WebBin -Force -ErrorAction SilentlyContinue

$cfg = Get-Content $ShellAppCfg -Raw -Encoding UTF8
$cfg = $cfg.Replace('C:\inetpub\wwwroot\Bonobo.Git.Server\App_Data', $AppData)
$destCfg = Join-Path $WebBin 'Bonobo.Git.Server.SshShell.exe.config'
[IO.File]::WriteAllText($destCfg, $cfg, (New-Object System.Text.UTF8Encoding($false)))
Write-Info "Wrote $destCfg with absolute App_Data paths."

# ---------------------------------------------------------------------------
Write-Step "5. authorized_keys folder"
New-Item -ItemType Directory -Force -Path $SshKeysDir | Out-Null
# The web app (running as you / the app pool identity) must be able to rewrite this file;
# 'git' only needs to read it.
icacls $SshKeysDir /inheritance:r | Out-Null
icacls $SshKeysDir /grant "${InvokingUser}:(OI)(CI)M" | Out-Null
icacls $SshKeysDir /grant "${GitAccount}:(OI)(CI)RX" | Out-Null
# Well-known SIDs (SYSTEM, BUILTIN\Administrators): the group names are localized.
icacls $SshKeysDir /grant "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
Write-Info "ACL'd $SshKeysDir for $InvokingUser (write) and $GitAccount (read)."

# ---------------------------------------------------------------------------
Write-Step "6. Grant '$GitAccount' access to the checkout"
# Traverse-only on every parent directory so git can reach bin\ and App_Data\ without
# being able to list the rest of the profile/checkout.
$chain = New-Object System.Collections.Generic.List[string]
$current = Split-Path $RepoRoot -Parent
while ($current) {
    $chain.Add($current)
    $up = Split-Path $current -Parent
    if ([string]::IsNullOrEmpty($up) -or $up -eq $current) { break }
    $current = $up
}
$chain.Add($RepoRoot)
foreach ($p in $chain) {
    if (Test-Path $p) { icacls $p /grant "${GitAccount}:(X)" | Out-Null }
}
icacls $WebRoot /grant "${GitAccount}:(X)" | Out-Null
icacls $WebBin  /grant "${GitAccount}:(OI)(CI)RX" | Out-Null
icacls $AppData /grant "${GitAccount}:(OI)(CI)M" | Out-Null
Write-Info "Granted traverse on parents, RX on bin\, Modify on App_Data\."

# ---------------------------------------------------------------------------
Write-Step "7. Patch web.config"
# PreserveWhitespace keeps the file's existing layout so the git diff stays minimal.
$webConfigXml = New-Object System.Xml.XmlDocument
$webConfigXml.PreserveWhitespace = $true
$webConfigXml.Load($WebConfigPath)
$appSettings = $webConfigXml.SelectSingleNode('/configuration/appSettings')
function Set-AppSetting($node, $key, $value) {
    # SetAttribute, not '$entry.value =': PowerShell resolves .value to XmlNode.Value, not the attribute.
    $entry = $node.SelectSingleNode("add[@key='$key']")
    if ($entry) { $entry.SetAttribute('value', [string]$value) }
    else {
        $new = $node.OwnerDocument.CreateElement('add')
        $new.SetAttribute('key', $key)
        $new.SetAttribute('value', [string]$value)
        $node.AppendChild($new) | Out-Null
    }
}
Set-AppSetting $appSettings 'SshAuthorizedKeysPath' $AuthorizedKeysPath
Set-AppSetting $appSettings 'SshShellPath' $ShellExePath
Set-AppSetting $appSettings 'SshServiceAccount' $GitAccount
$webConfigXml.Save($WebConfigPath)
Write-Info "SshAuthorizedKeysPath = $AuthorizedKeysPath"
Write-Info "SshShellPath          = $ShellExePath"
Write-Info "SshServiceAccount     = $GitAccount"

# ---------------------------------------------------------------------------
Write-Step "8. Patch App_Data\config.xml"
$appCfgXml = New-Object System.Xml.XmlDocument
$appCfgXml.PreserveWhitespace = $true
$appCfgXml.Load($ConfigXmlPath)
function Set-ConfigNode($doc, $name, $value) {
    $node = $doc.SelectSingleNode("/Configuration/$name")
    if ($null -eq $node) {
        $node = $doc.CreateElement($name)
        $doc.DocumentElement.AppendChild($node) | Out-Null
    }
    $node.InnerText = [string]$value
}
Set-ConfigNode $appCfgXml 'Repositories' (Join-Path $AppData 'Repositories')
Set-ConfigNode $appCfgXml 'SshEnabled' 'true'
Set-ConfigNode $appCfgXml 'SshHost' $SshHostName
$appCfgXml.Save($ConfigXmlPath)
Write-Info "Repositories = $(Join-Path $AppData 'Repositories')"
Write-Info "SshEnabled = true, SshHost = $SshHostName"
Write-Info "Restart the site (IIS Express / VS) to pick this up and regenerate authorized_keys."

# ---------------------------------------------------------------------------
Write-Step "9. sshd_config"
$sshdLines = Get-Content $SshdConfigPath
$alreadyThere = $sshdLines -match '^\s*Match\s+User\s+git\b' | Select-Object -First 1
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

$sshdTest = & "$env:WINDIR\System32\OpenSSH\sshd.exe" -t -f $SshdConfigPath 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Warning "sshd -t reported a problem with sshd_config:`n$sshdTest"
    Write-Warning "Not restarting sshd. Fix $SshdConfigPath (backup at $SshdConfigPath.bak) and run Restart-Service sshd yourself."
} else {
    Restart-Service sshd
    Write-Info "sshd_config OK, sshd restarted."
}

# ---------------------------------------------------------------------------
Write-Step "Done"
Write-Host @"

Next steps:
  1. Restart the web app (stop/start in Visual Studio or IIS Express) so it
     rewrites $AuthorizedKeysPath with the current keys.
  2. Add a public key under Account -> SSH Keys in the running site.
  3. Test:
       ssh -o IdentitiesOnly=yes -T $GitAccount@$SshHostName
     Being refused with "This account is only for git access" is success -
     it means key auth worked and the forced command ran.
  4. Clone/fetch:
       git ls-remote $GitAccount@${SshHostName}:YourRepo.git

If it fails, check:
  - Event Viewer > Applications and Services Logs > OpenSSH > Operational
  - $AppData\Logs\ssh-*.txt (written by the shell itself)
"@
