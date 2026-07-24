$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceDir = Join-Path $toolsDir 'bin'
$installDir = 'C:\Program Files\ADS Gamepad Service'
$serviceName = 'AdsGamepadService'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# The publish output includes subdirectories the application needs, so the
# copy is recursive. An existing appsettings.json is kept so an upgrade
# never overwrites the configuration.
$keepConfig = Test-Path (Join-Path $installDir 'appsettings.json')
$robocopyArgs = @($sourceDir, $installDir, '/E')
if ($keepConfig) {
    Write-Host 'Keeping the existing appsettings.json.'
    $robocopyArgs += @('/XF', 'appsettings.json')
}
robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Copying the application files failed, robocopy code $LASTEXITCODE."
}
cmd /c exit 0

$exePath = Join-Path $installDir 'AdsGamepadService.exe'
$quotedExe = '"' + $exePath + '"'
$registryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"

if ($existing) {
    $imagePath = (Get-ItemProperty $registryKey).ImagePath
    if ($imagePath -ne $quotedExe) {
        Write-Host 'The install location changed, recreating the service registration.'

        # Carry an edited configuration over from the old location so the
        # takeover of a manual install in another directory keeps its settings
        $oldDir = Split-Path ($imagePath.Trim('"')) -Parent
        $oldConfig = Join-Path $oldDir 'appsettings.json'
        if ((Test-Path $oldConfig) -and -not $keepConfig) {
            Copy-Item $oldConfig (Join-Path $installDir 'appsettings.json') -Force
            Write-Host "Carried the configuration over from $oldDir."
        }

        & sc.exe delete $serviceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Removing the old service registration failed, sc.exe code $LASTEXITCODE."
        }
        Start-Sleep -Seconds 2
        $existing = $null
    }
}

if (-not $existing) {
    New-Service -Name $serviceName `
        -BinaryPathName $quotedExe `
        -DisplayName 'ADS Gamepad Service' `
        -StartupType Automatic `
        -Description 'Bridges Xbox gamepads to TwinCAT PLCs over ADS.' | Out-Null
}

# Delayed start keeps the service from racing the TwinCAT router at boot
& sc.exe config $serviceName start=delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Configuring the service start type failed, sc.exe code $LASTEXITCODE."
}

# Restart automatically on failure: twice after five seconds, then after thirty
& sc.exe failure $serviceName reset=86400 actions=restart/5000/restart/5000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Configuring service recovery failed, sc.exe code $LASTEXITCODE."
}

Start-Service -Name $serviceName
Write-Host "ADS Gamepad Service is installed and running from $installDir."
Write-Host "Settings live in $installDir\appsettings.json. Restart the service after editing them."
