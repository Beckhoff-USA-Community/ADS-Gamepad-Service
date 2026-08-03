$ErrorActionPreference = 'Stop'

$moduleDir = 'C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository\Beckhoff Community\ADS_Gamepad'
$versionDir = Join-Path $moduleDir '0.0.0.12'
$vendorDir = Split-Path $moduleDir -Parent

if (Test-Path $versionDir) {
    Remove-Item $versionDir -Recurse -Force
}
if ((Test-Path $moduleDir) -and -not (Get-ChildItem $moduleDir -Force)) {
    Remove-Item $moduleDir -Force
}
if ((Test-Path $vendorDir) -and -not (Get-ChildItem $vendorDir -Force)) {
    Remove-Item $vendorDir -Force
}
Write-Host 'The Gamepad TcCOM module was removed from the TwinCAT module repository.'
