$ErrorActionPreference = 'Stop'
$serviceName = 'AdsGamepadService'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping $serviceName before the package changes."
    Stop-Service -Name $serviceName -Force
    Start-Sleep -Seconds 2
}
