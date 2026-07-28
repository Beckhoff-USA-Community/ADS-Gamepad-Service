$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceDir = Join-Path $toolsDir 'docs'
$installDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\Documentation'

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item (Join-Path $sourceDir '*') $installDir -Recurse -Force

Write-Host "The documentation is installed in $installDir."
