$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceDir = Join-Path $toolsDir 'repository'
$repositoryDir = 'C:\ProgramData\Beckhoff\TwinCAT\3.1\Repository'
$moduleVersionDir = Join-Path $repositoryDir 'Beckhoff Community\ADS_Gamepad\0.0.0.11'

New-Item -ItemType Directory -Force -Path $repositoryDir | Out-Null
Copy-Item (Join-Path $sourceDir '*') $repositoryDir -Recurse -Force

if (-not (Test-Path (Join-Path $moduleVersionDir 'ADS_Gamepad.tmc'))) {
    throw "The module did not arrive in the TwinCAT repository at $moduleVersionDir."
}

Write-Host "The Gamepad TcCOM module is installed in the TwinCAT module repository."
Write-Host "Add an instance in your project under System, TcCOM Objects; the documentation walks through it."
