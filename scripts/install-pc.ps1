<#
.SYNOPSIS
  Sets up a Windows PC for PocketPC: installs Sunshine (the streaming server), installs the
  PocketPC companion, and allows it through the firewall on PRIVATE (home) networks only.

.DESCRIPTION
  Run from PowerShell:
      powershell -ExecutionPolicy Bypass -File .\install-pc.ps1

  Put PocketPC.exe next to this script (it's in the "PocketPC-Windows" download from the
  project's GitHub Actions or Releases page), or pass -CompanionExe <path>. If neither exists and
  you have the .NET 10 SDK plus the source code, the script builds it.

  Nothing is opened to the internet: no router port forwarding, no UPnP.
#>
param(
    [string]$CompanionExe
)

$ErrorActionPreference = 'Stop'
$CompanionPort = 47880

function Write-Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok($text) { Write-Host "    $text" -ForegroundColor Green }
function Write-Warn($text) { Write-Host "    $text" -ForegroundColor Yellow }

# --- Re-launch as administrator (needed for Sunshine and firewall rules) ----------------------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($CompanionExe) { $argList += @('-CompanionExe', "`"$CompanionExe`"") }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argList
    exit
}

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Split-Path -Parent $scriptDir

# --- 1. Sunshine ------------------------------------------------------------------------------
Write-Step 'Sunshine (streaming server)'
$sunshineExe = Join-Path $env:ProgramFiles 'Sunshine\sunshine.exe'
if (Test-Path $sunshineExe) {
    Write-Ok 'Already installed.'
} else {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is missing. Install "App Installer" from the Microsoft Store, or install Sunshine from https://github.com/LizardByte/Sunshine/releases, then run this script again.'
    }
    winget install --id LizardByte.Sunshine --exact --accept-source-agreements --accept-package-agreements
    if (-not (Test-Path $sunshineExe)) { throw 'Sunshine did not install correctly.' }
    Write-Ok 'Installed. Sunshine runs as a Windows service and starts with Windows.'
}

# --- 2. PocketPC companion --------------------------------------------------------------------
Write-Step 'PocketPC companion'
if (-not $CompanionExe) {
    $candidate = Join-Path $scriptDir 'PocketPC.exe'
    if (Test-Path $candidate) { $CompanionExe = $candidate }
}
if (-not $CompanionExe) {
    $project = Join-Path $repoRoot 'pc\src\PocketPC.Companion\PocketPC.Companion.csproj'
    if ((Test-Path $project) -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Ok 'Building PocketPC.exe from source (takes a minute)...'
        $out = Join-Path $env:TEMP 'PocketPC-build'
        dotnet publish $project -c Release -o $out | Out-Host
        $CompanionExe = Join-Path $out 'PocketPC.exe'
    } else {
        throw 'PocketPC.exe not found. Download "PocketPC-Windows" from the GitHub Actions/Releases page, put PocketPC.exe next to this script and run it again.'
    }
}

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PocketPC'
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Get-Process PocketPC -ErrorAction SilentlyContinue | Stop-Process -Force
$installedExe = Join-Path $installDir 'PocketPC.exe'
Copy-Item $CompanionExe $installedExe -Force
Write-Ok "Installed to $installedExe"

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PocketPC.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $installedExe
$shortcut.Description = 'PocketPC companion: play your PC on your phone'
$shortcut.Save()
Write-Ok 'Start menu shortcut created.'

# --- 3. Firewall: home network only -----------------------------------------------------------
Write-Step 'Firewall (home/private networks only)'
Get-NetFirewallRule -DisplayName 'PocketPC companion' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'PocketPC companion' -Direction Inbound -Action Allow `
    -Program $installedExe -Protocol TCP -LocalPort $CompanionPort -Profile Private | Out-Null
Write-Ok "Allowed TCP $CompanionPort for PocketPC on Private networks. Public networks stay blocked."

$public = Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' }
foreach ($net in $public) {
    Write-Warn "Your network '$($net.Name)' is set to Public, so phones can't connect on it."
    $answer = Read-Host "    Is '$($net.Name)' your own home network? Set it to Private? (y/N)"
    if ($answer -match '^[yY]') {
        Set-NetConnectionProfile -InterfaceIndex $net.InterfaceIndex -NetworkCategory Private
        Write-Ok "'$($net.Name)' is now Private."
    }
}

# --- 4. Start the companion (as the normal user, not as admin) --------------------------------
Write-Step 'Starting PocketPC'
Start-Process explorer.exe -ArgumentList "`"$installedExe`""
Write-Ok 'PocketPC is in the system tray. In its window:'
Write-Ok '  1. Finish the Sunshine step (one click on a fresh install).'
Write-Ok '  2. Click "Pair new phone..." and scan the QR code with the PocketPC app.'
Write-Host ''
Read-Host 'Press Enter to close'
