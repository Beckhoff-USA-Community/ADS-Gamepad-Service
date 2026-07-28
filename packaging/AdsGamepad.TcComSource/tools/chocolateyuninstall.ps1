$ErrorActionPreference = 'Stop'

$installDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\TcCOM'
$productDir = Split-Path $installDir -Parent

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}
if ((Test-Path $productDir) -and -not (Get-ChildItem $productDir -Force)) {
    Remove-Item $productDir -Force
}
Write-Host 'The TcCOM module source was removed.'
