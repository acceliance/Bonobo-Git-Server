<#
.SYNOPSIS
    Builds Bonobo.Git.Server.SshShell here and installs it on the server, under C:\ProgramData\Bonobo.

.DESCRIPTION
    Run on the BUILD machine, from the checkout. In one go it:

      1. builds the shell in Release and zips the output (through Deploy-SshShell.ps1);
      2. copies the zip to the server with scp, over the SSH access you already have;
      3. on the server, optionally stops running shell processes (they lock the exe), then runs
         Setup-PublishedSsh.ps1 from the site folder with the new build as -ShellSource.

    Setup-PublishedSsh.ps1 is what puts the shell in C:\ProgramData\Bonobo\ssh-shell and writes its
    .exe.config from the site's web.config, so a rebuilt shell never keeps a stale configuration. It
    also repairs the ACLs, makes sure the Match User block is in sshd_config, restarts sshd and
    recycles the site's application pool. Everything on the server is left as the setup script
    leaves it; only a temporary folder in C:\Windows\Temp is created, and removed again.

    Prerequisites: ssh.exe and scp.exe on this machine (Windows OpenSSH client), an SSH login to the
    server that is an administrator, and a site that was already set up once with
    Setup-PublishedSsh.ps1 (or follow the manual steps in the readme first).

.PARAMETER Server
    The SSH target, as you would type it after "ssh": for example administrator@webhosting.example.com.

.PARAMETER SshHostName
    The host name clients use to clone, passed on to Setup-PublishedSsh.ps1 (-SshHostName).

.PARAMETER SiteRoot
    The published site's folder ON THE SERVER, for example C:\inetpub\git.example.com. It must hold
    Setup-PublishedSsh.ps1, which ships with the site.

.PARAMETER GitAccount
    Passed on to Setup-PublishedSsh.ps1 when the SSH account is not the default 'git'.

.PARAMETER StopRunning
    Stop Bonobo.Git.Server.SshShell processes that are running from C:\ProgramData\Bonobo before
    installing. A running shell locks its exe, so the copy fails otherwise. This ends any clone,
    fetch or push in progress, so leave it off while people are working, or accept that they retry.

.PARAMETER SkipBuild
    Reuse the existing Bonobo.Git.Server.SshShell\bin\<Configuration> instead of building first.

.PARAMETER Configuration
    Build configuration. Release by default.

.EXAMPLE
    .\Publish-SshShell.ps1 -Server administrator@webhosting.example.com -SiteRoot 'C:\inetpub\git.example.com' -SshHostName git.example.com
    .\Publish-SshShell.ps1 -Server administrator@webhosting.example.com -SiteRoot 'C:\inetpub\git.example.com' -SshHostName git.example.com -StopRunning
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Server,
    [Parameter(Mandatory = $true)]
    [string]$SshHostName,
    [Parameter(Mandatory = $true)]
    [string]$SiteRoot,
    [string]$GitAccount = '',
    [switch]$StopRunning,
    [switch]$SkipBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor DarkGray }

# $PSScriptRoot is still empty under 'powershell -File' while param defaults are evaluated, so it is
# resolved here, as Deploy-SshShell.ps1 does.
$RepoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$DeployScript = Join-Path $RepoRoot 'Deploy-SshShell.ps1'
if (-not (Test-Path $DeployScript)) {
    throw "Deploy-SshShell.ps1 not found next to this script ($RepoRoot). Run Publish-SshShell.ps1 from a Bonobo-Git-Server checkout."
}
foreach ($tool in 'ssh.exe', 'scp.exe') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool not found. Install the Windows OpenSSH client (Settings > Optional features) and run again."
    }
}
if ($Server -match '[\s"''`;&|<>]') { throw "-Server '$Server' does not look like a plain [user@]host SSH target." }

# Values are embedded in a script that runs on the server, so every one of them is passed through a
# single-quoted literal with its quotes doubled: nothing in them can be interpreted as code.
function ConvertTo-Literal([string]$value) { "'" + $value.Replace("'", "''") + "'" }

# Runs PowerShell code on the server. The code travels base64-encoded (-EncodedCommand), so no quote,
# pipe or space in it is ever seen by the server's default SSH shell, whichever one that is (cmd or
# PowerShell) and however Windows PowerShell 5.1 would have mangled embedded quotes.
function Invoke-Remote([string]$code) {
    # -OutputFormat Text: without it, errors come back as serialized CLIXML instead of readable text.
    # The preference line keeps "Expand-Archive" and friends from writing progress records.
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("`$ProgressPreference = 'SilentlyContinue'`n" + $code))
    & ssh.exe $Server "powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand $encoded"
}

# A short random suffix keeps two runs, or a run left half-finished, from stepping on each other.
$stamp      = [guid]::NewGuid().ToString('N').Substring(0, 8)
$remoteDir  = "C:/Windows/Temp/bonobo-ssh-shell-$stamp"          # scp wants forward slashes
$remoteDirW = $remoteDir.Replace('/', '\')
$localTemp  = Join-Path ([IO.Path]::GetTempPath()) "bonobo-ssh-shell-$stamp"
New-Item -ItemType Directory -Force -Path $localTemp | Out-Null
$zipPath     = Join-Path $localTemp 'ssh-shell.zip'
$remoteScript = Join-Path $localTemp 'install-on-server.ps1'

try {
    # -----------------------------------------------------------------------
    Write-Step "Build and package the shell ($Configuration)"
    $deployArgs = @{ ZipPath = $zipPath; Configuration = $Configuration }
    if (-not $SkipBuild) { $deployArgs['Build'] = $true }
    & $DeployScript @deployArgs
    if (-not (Test-Path $zipPath)) { throw "Packaging failed: $zipPath was not created." }

    # -----------------------------------------------------------------------
    $setupArgs = "-SshHostName $(ConvertTo-Literal $SshHostName)"
    if ($GitAccount) { $setupArgs += " -GitAccount $(ConvertTo-Literal $GitAccount)" }

    $stopBlock = ''
    if ($StopRunning) {
        $stopBlock = @'
$running = @(Get-Process -Name 'Bonobo.Git.Server.SshShell' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like 'C:\ProgramData\Bonobo\*' })
if ($running.Count -gt 0) {
    Write-Host "Stopping $($running.Count) running shell process(es): $(($running | ForEach-Object Id) -join ', ')"
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}
'@
    }

    @"
`$ErrorActionPreference = 'Stop'
`$work = $(ConvertTo-Literal $remoteDirW)
`$site = $(ConvertTo-Literal $SiteRoot)
try {
    if (-not (Test-Path (Join-Path `$site 'Setup-PublishedSsh.ps1'))) {
        throw "Setup-PublishedSsh.ps1 not found in `$site. -SiteRoot must be the published site's folder on the server."
    }
    `$src = Join-Path `$work 'shell'
    Expand-Archive -Path (Join-Path `$work 'ssh-shell.zip') -DestinationPath `$src -Force
    if (-not (Test-Path (Join-Path `$src 'Bonobo.Git.Server.SshShell.exe'))) { throw 'The package holds no Bonobo.Git.Server.SshShell.exe.' }
$stopBlock
    Set-Location `$site
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 $setupArgs -ShellSource `$src
    if (`$LASTEXITCODE -ne 0) { throw "Setup-PublishedSsh.ps1 failed (exit `$LASTEXITCODE)." }
}
finally {
    Remove-Item `$work -Recurse -Force -ErrorAction SilentlyContinue
}
"@ | Set-Content -Path $remoteScript -Encoding UTF8

    # -----------------------------------------------------------------------
    Write-Step "Copy to $Server"
    Invoke-Remote "New-Item -ItemType Directory -Force -Path $(ConvertTo-Literal $remoteDirW) | Out-Null"
    if ($LASTEXITCODE -ne 0) { throw "Could not reach $Server over SSH (exit $LASTEXITCODE)." }
    & scp.exe $zipPath "${Server}:$remoteDir/ssh-shell.zip"
    if ($LASTEXITCODE -ne 0) { throw "scp of the package failed (exit $LASTEXITCODE)." }
    & scp.exe $remoteScript "${Server}:$remoteDir/install-on-server.ps1"
    if ($LASTEXITCODE -ne 0) { throw "scp of the install script failed (exit $LASTEXITCODE)." }

    # -----------------------------------------------------------------------
    Write-Step "Install on the server (C:\ProgramData\Bonobo)"
    Invoke-Remote "& $(ConvertTo-Literal "$remoteDirW\install-on-server.ps1"); exit `$LASTEXITCODE"
    if ($LASTEXITCODE -ne 0) {
        throw "The install on the server failed (exit $LASTEXITCODE). If the message says a file is being used by another process, run again with -StopRunning."
    }

    Write-Host @"

Done. Check it from a client:
  ssh -o IdentitiesOnly=yes -T git@$SshHostName
being refused with "This account is only for git access" or "No command was supplied" means key
authentication and the forced command both work. Then try: git ls-remote git@${SshHostName}:YourRepo.git
"@
}
finally {
    # The remote folder removes itself at the end of the install script; this covers a failure earlier.
    Invoke-Remote "Remove-Item $(ConvertTo-Literal $remoteDirW) -Recurse -Force -ErrorAction SilentlyContinue" 2>$null
    Remove-Item $localTemp -Recurse -Force -ErrorAction SilentlyContinue
}
