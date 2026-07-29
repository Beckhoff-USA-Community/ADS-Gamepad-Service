# Installs ADS Gamepad Service as a Windows service.
# Run from an elevated PowerShell after publishing the application:
#   dotnet publish src\AdsGamepadService -c Release -o publish
#   .\deploy\windows\install-service.ps1 -SourceDir .\publish
# An existing appsettings.json in the install directory is kept, so an
# upgrade does not overwrite your configuration. Upgrades copy over the old
# files without deleting extras; for a fully clean slate run the uninstall
# script with RemoveFiles first.

param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [string]$InstallDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\Service'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'AdsGamepadService'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must run from an elevated PowerShell.'
}

if (-not (Test-Path (Join-Path $SourceDir 'AdsGamepadService.exe'))) {
    throw "AdsGamepadService.exe was not found in $SourceDir. Publish the application first."
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping the existing $serviceName service."
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# The publish output includes subdirectories such as runtimes, and the
# application cannot start without them, so the copy must be recursive.
$keepConfig = Test-Path (Join-Path $InstallDir 'appsettings.json')
$robocopyArgs = @($SourceDir, $InstallDir, '/E')
if ($keepConfig) {
    Write-Host 'Keeping the existing appsettings.json.'
    $robocopyArgs += @('/XF', 'appsettings.json')
}
robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Copying the application files failed, robocopy code $LASTEXITCODE."
}

$exePath = Join-Path $InstallDir 'AdsGamepadService.exe'
$quotedExe = '"' + $exePath + '"'
$registryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"

if ($existing) {
    $imagePath = (Get-ItemProperty $registryKey).ImagePath
    if ($imagePath -ne $quotedExe) {
        Write-Host 'The install directory changed, recreating the service registration.'
        & sc.exe delete $serviceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Removing the old service registration failed, sc.exe code $LASTEXITCODE."
        }
        Start-Sleep -Seconds 2
        $existing = $null
    }
}

if (-not $existing) {
    # New-Service handles the quoting of spaced paths the same way in every
    # PowerShell version, unlike passing quotes through to sc.exe create
    New-Service -Name $serviceName `
        -BinaryPathName $quotedExe `
        -DisplayName 'ADS Gamepad Service' `
        -StartupType Automatic `
        -Description 'Bridges Xbox and PlayStation gamepads to TwinCAT PLCs over ADS.' | Out-Null

    $imagePath = (Get-ItemProperty $registryKey).ImagePath
    if ($imagePath -ne $quotedExe) {
        throw "The service registration stored an unexpected path: $imagePath"
    }
}

# Delayed start keeps the service from racing the TwinCAT router at boot
& sc.exe config $serviceName start=delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Configuring the service start type failed, sc.exe code $LASTEXITCODE."
}

# Restart automatically on failure: twice after five seconds, then after thirty
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Configuring service recovery failed, sc.exe code $LASTEXITCODE."
}

Start-Service -Name $serviceName
Write-Host "$serviceName is installed and running from $InstallDir."
Write-Host "Settings live in $InstallDir\appsettings.json. Restart the service after editing them."
