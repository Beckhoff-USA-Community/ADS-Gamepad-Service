$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceDir = Join-Path $toolsDir 'source'
$installDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\TcCOM'

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $sourceDir '*') $installDir -Recurse -Force

Write-Host "The TcCOM module source is installed in $installDir."
Write-Host 'Copy the project to a writable folder before building. The readme inside covers the build.'
