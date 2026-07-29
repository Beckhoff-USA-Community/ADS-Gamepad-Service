$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$sourceDir = Join-Path $toolsDir 'bin'
$installDir = 'C:\Program Files\Beckhoff USA Community\ADS Gamepad\Service'
$serviceName = 'AdsGamepadService'

# The previous dedicated install location. Only this exact directory is
# removed after a successful move. A manual install somewhere else is
# left in place, its directory may hold files that are not ours.
$oldDefaultDir = 'C:\Program Files\ADS Gamepad Service'

$exePath = Join-Path $installDir 'AdsGamepadService.exe'
$quotedExe = '"' + $exePath + '"'
$registryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
}

# A move is detected before any file is copied, so the configuration can be
# carried over first and a failed attempt can simply be repeated
$relocating = $false
$oldInstallDir = $null
if ($existing) {
    $imagePath = (Get-ItemProperty $registryKey).ImagePath
    if ($imagePath -ne $quotedExe) {
        $relocating = $true
        $oldInstallDir = Split-Path ($imagePath.Trim('"')) -Parent
        Write-Host 'The install location changed, recreating the service registration.'
    }
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

# An edited configuration always wins over the packaged default: first the
# one already in the new directory, otherwise the one carried over from the
# old location during a move
if ($relocating -and -not (Test-Path (Join-Path $installDir 'appsettings.json'))) {
    $oldConfig = Join-Path $oldInstallDir 'appsettings.json'
    if (Test-Path $oldConfig) {
        Copy-Item $oldConfig (Join-Path $installDir 'appsettings.json') -Force
        Write-Host "Carried the configuration over from $oldInstallDir."
    }
}

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

if ($relocating) {
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Removing the old service registration failed, sc.exe code $LASTEXITCODE."
    }
    Start-Sleep -Seconds 2
    $existing = $null
}

if (-not $existing) {
    New-Service -Name $serviceName `
        -BinaryPathName $quotedExe `
        -DisplayName 'ADS Gamepad Service' `
        -StartupType Automatic `
        -Description 'Bridges Xbox and PlayStation gamepads to TwinCAT PLCs over ADS.' | Out-Null
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

# After a successful move the old default directory only holds stale binaries
if ($relocating -and $oldInstallDir -eq $oldDefaultDir -and (Test-Path $oldDefaultDir)) {
    Remove-Item $oldDefaultDir -Recurse -Force
    Write-Host "The old installation in $oldDefaultDir was removed."
}

Write-Host "ADS Gamepad Service is installed and running from $installDir."
Write-Host "Settings live in $installDir\appsettings.json. Restart the service after editing them."
