$ErrorActionPreference = 'Stop'

$installDir = 'C:\Program Files\ADS Gamepad Service'
$serviceName = 'AdsGamepadService'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Removing the service registration failed, sc.exe code $LASTEXITCODE."
    }
    Write-Host "$serviceName was removed."
}

# Program files are removed but an edited appsettings.json is kept so a
# later reinstall finds the configuration again
if (Test-Path $installDir) {
    Get-ChildItem $installDir -Force | Where-Object Name -ne 'appsettings.json' | Remove-Item -Recurse -Force
    Write-Host "Program files were removed from $installDir. appsettings.json was kept."
}
