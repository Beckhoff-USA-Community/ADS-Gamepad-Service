# Removes the ADS Gamepad Service Windows service.
# Run from an elevated PowerShell. Program files are kept unless you pass
# the RemoveFiles switch, and appsettings.json is always kept so a later
# reinstall finds your configuration.

param(
    [switch]$RemoveFiles,

    [string]$InstallDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\Service'
)

$ErrorActionPreference = 'Stop'
$serviceName = 'AdsGamepadService'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must run from an elevated PowerShell.'
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Removing the service registration failed, sc.exe code $LASTEXITCODE. Close services.msc if it is open and try again."
    }
    Write-Host "$serviceName was removed."
}
else {
    Write-Host "$serviceName is not installed."
}

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Get-ChildItem $InstallDir -Force | Where-Object Name -ne 'appsettings.json' | Remove-Item -Recurse -Force
    Write-Host "Program files were removed from $InstallDir. appsettings.json was kept."
}
