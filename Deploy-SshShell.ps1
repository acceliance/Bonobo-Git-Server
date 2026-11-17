<#
.SYNOPSIS
    Copies the Bonobo.Git.Server.SshShell build output to a published site's App_Data\ssh-shell
    folder, where Setup-PublishedSsh.ps1 expects to find it.

.DESCRIPTION
    Run on the build machine, from the checkout. It takes Bonobo.Git.Server.SshShell\bin\<Configuration>
    (the whole folder: the exe, its .config, every dll and the native subfolders) and copies it with
    robocopy to the destination, typically the server's App_Data\ssh-shell over an administrative
    share. When the server cannot be reached as a share, -ZipPath writes a zip instead, to be
    extracted into App_Data\ssh-shell on the server by hand.

    Use this when a site was published before the publish step started shipping the shell, or to
    push a newer shell to a site without republishing it. Afterwards, run Setup-PublishedSsh.ps1
    on the server (again, if it already ran) so the new shell is deployed outside the web root.

.PARAMETER Destination
    The site's App_Data\ssh-shell folder, local or UNC. Created when missing.

.PARAMETER Source
    A folder holding the shell's build output, to run the script somewhere other than the build
    machine: for example the checkout's Bonobo.Git.Server.SshShell\bin\Release reached over a
    share from the server. Defaults to that folder in the checkout this script is in.

.PARAMETER Configuration
    Which build output to take. Release by default. Ignored when -Source is given.

.PARAMETER Build
    Build Bonobo.Git.Server.SshShell in that configuration first, with the latest Visual Studio's
    MSBuild. That also builds the web project it references.

.PARAMETER Clean
    Mirror instead of merge: files in the destination that are not in the build output are deleted,
    so no stale dll stays behind. Refused when the destination holds anything other than a
    previous copy of the shell.

.PARAMETER ZipPath
    Write a zip of the build output to this path instead of copying to Destination.

.EXAMPLE
    .\Deploy-SshShell.ps1 -Destination '\\server\c$\inetpub\site\App_Data\ssh-shell'
    .\Deploy-SshShell.ps1 -Build -Clean -Destination '\\server\c$\inetpub\site\App_Data\ssh-shell'
    .\Deploy-SshShell.ps1 -ZipPath "$env:USERPROFILE\Desktop\ssh-shell.zip"
    # On the server, with the build machine's checkout shared:
    .\Deploy-SshShell.ps1 -Source '\\buildpc\src\Bonobo-Git-Server\Bonobo.Git.Server.SshShell\bin\Release' -Destination 'C:\inetpub\site\App_Data\ssh-shell'
#>
[CmdletBinding(DefaultParameterSetName = 'Copy')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Copy', Position = 0)]
    [string]$Destination,
    [string]$Source = '',
    [string]$Configuration = 'Release',
    [switch]$Build,
    [Parameter(ParameterSetName = 'Copy')]
    [switch]$Clean,
    [Parameter(Mandatory = $true, ParameterSetName = 'Zip')]
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor DarkGray }

# Resolved here rather than as a parameter default: under 'powershell -File', $PSScriptRoot is
# still empty while the param block's defaults are evaluated.
$RepoRoot   = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$ShellProj  = Join-Path $RepoRoot 'Bonobo.Git.Server.SshShell'
$ShellCsproj = Join-Path $ShellProj 'Bonobo.Git.Server.SshShell.csproj'
$ShellExeName = 'Bonobo.Git.Server.SshShell.exe'

if ([string]::IsNullOrWhiteSpace($Source)) {
    if (-not (Test-Path $ShellCsproj)) {
        throw @"
$RepoRoot is not a Bonobo-Git-Server checkout ($ShellCsproj not found).
This script runs on the BUILD machine, from the checkout, and copies the shell to the server:
    .\Deploy-SshShell.ps1 -Build -Destination '\\server\c$\inetpub\site\App_Data\ssh-shell'
To run it on the server instead, point -Source at the build output, for example the build machine's
Bonobo.Git.Server.SshShell\bin\Release over a share, or a copy of that folder.
"@
    }
    $Source = Join-Path $ShellProj "bin\$Configuration"
} elseif ($Build) {
    throw "-Build cannot be combined with -Source: the build output is taken from -Source as is."
}

# ---------------------------------------------------------------------------
if ($Build) {
    Write-Step "Build Bonobo.Git.Server.SshShell ($Configuration)"
    $msbuildExe = $null
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { $msbuildExe = $cmd.Source }
    if (-not $msbuildExe) {
        $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vswhere) {
            $msbuildExe = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        }
    }
    if (-not $msbuildExe) { throw "msbuild.exe not found. Open a Developer PowerShell, or build the solution in Visual Studio and run again without -Build." }
    Write-Info "Using $msbuildExe"
    & $msbuildExe $ShellCsproj /nologo /m /v:minimal /p:Configuration=$Configuration /p:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}

# ---------------------------------------------------------------------------
Write-Step "Source: $Source"
if (-not (Test-Path (Join-Path $Source $ShellExeName))) {
    throw "No $Configuration build output at $Source. Build the solution in $Configuration first, or run again with -Build."
}
$files = Get-ChildItem $Source -Recurse -File
$sizeMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Info "$($files.Count) files, $sizeMb MB, exe dated $((Get-Item (Join-Path $Source $ShellExeName)).LastWriteTime)"

# ---------------------------------------------------------------------------
if ($PSCmdlet.ParameterSetName -eq 'Zip') {
    Write-Step "Zip to $ZipPath"
    $zipDir = Split-Path -Parent $ZipPath
    if ($zipDir) { New-Item -ItemType Directory -Force -Path $zipDir | Out-Null }
    Compress-Archive -Path (Join-Path $Source '*') -DestinationPath $ZipPath -Force
    Write-Info "Wrote $ZipPath ($([math]::Round((Get-Item $ZipPath).Length / 1MB, 1)) MB)"
    Write-Host @"

Next, on the server:
  1. Extract the zip into <site>\App_Data\ssh-shell so that the exe sits directly in that folder.
  2. Run Setup-PublishedSsh.ps1 from the site folder (elevated):
       powershell -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 -SshHostName <host>
"@
    return
}

Write-Step "Copy to $Destination"
if ($Clean -and (Test-Path $Destination)) {
    $existing = @(Get-ChildItem $Destination -Force -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0 -and -not (Test-Path (Join-Path $Destination $ShellExeName))) {
        throw "-Clean would delete the contents of $Destination, which does not look like a previous copy of the shell (no $ShellExeName). Pick the App_Data\ssh-shell folder, or drop -Clean."
    }
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$mode = if ($Clean) { '/MIR' } else { '/E' }
# Exit codes below 8 mean success (bits 1/2/4 only report what was copied, skipped or removed).
& robocopy.exe $Source $Destination $mode /R:2 /W:3 /NP /NFL /NDL /NJH
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "robocopy failed with exit code $rc." }
if (-not (Test-Path (Join-Path $Destination $ShellExeName))) { throw "Copy finished but $ShellExeName is missing from $Destination." }
Write-Info "Done (robocopy exit $rc)."

Write-Host @"

Next, on the server, from the site folder (elevated):
  powershell -ExecutionPolicy Bypass -File .\Setup-PublishedSsh.ps1 -SshHostName <host>
"@
