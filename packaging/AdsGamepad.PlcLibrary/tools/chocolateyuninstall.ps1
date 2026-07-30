$ErrorActionPreference = 'Stop'

$installDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\Library'
$productDir = Split-Path $installDir -Parent

# The identity format is "Name, Version (Company)". Version and company must
# match the installed library, so keep this line in step with the package.
$libraryIdentity = 'AdsGamepad, 2.2.1 (Beckhoff Community)'

$repTools = @(Join-Path 'C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Components\Plc\Build_4026.*\' 'Common\RepTool.exe' -Resolve)
if ($repTools.Length -gt 0) {
    # Wildcard resolution sorts alphabetically, which puts Build_4026.9 after
    # Build_4026.14, so the build number is compared numerically instead
    $repTool = $repTools | Sort-Object { [version]((Split-Path (Split-Path (Split-Path $_ -Parent) -Parent) -Leaf) -replace '^Build_', '') } | Select-Object -Last 1
    $buildName = Split-Path (Split-Path (Split-Path $repTool -Parent) -Parent) -Leaf
    $repToolArgs = "--profile=`"TwinCAT PLC Control_$buildName`"", "--uninstallLib `"$libraryIdentity`""
    Start-Process $repTool -ArgumentList $repToolArgs -Wait -WindowStyle Hidden
}

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}
if ((Test-Path $productDir) -and -not (Get-ChildItem $productDir -Force)) {
    Remove-Item $productDir -Force
}
Write-Host 'The AdsGamepad library was removed.'
