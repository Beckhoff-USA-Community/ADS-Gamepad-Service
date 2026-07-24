# Builds the TwinCAT package for the service.
# Requires the .NET SDK. Packs with tcpkg when it is installed, which is the
# case on any machine with the TwinCAT Package Manager, and falls back to
# nuget otherwise. The package version comes from the nuspec file.
#
#   .\packaging\Build-Package.ps1
#
# The finished package lands in packaging\bin\release.

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'bin\release')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$packageDir = Join-Path $PSScriptRoot 'AdsGamepadService.Service'
$stageBin = Join-Path $packageDir 'bin'
$nuspecs = @(
    (Join-Path $packageDir 'AdsGamepadService.Service.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.XAR\AdsGamepad.XAR.Workload.nuspec')
)

if (Test-Path $stageBin) {
    Remove-Item $stageBin -Recurse -Force
}

dotnet publish (Join-Path $repoRoot 'src\AdsGamepadService') -c Release -r win-x64 --self-contained -o $stageBin
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the service failed.'
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$tcpkg = Get-Command tcpkg -ErrorAction SilentlyContinue
$nuget = Get-Command nuget -ErrorAction SilentlyContinue
if (-not $tcpkg -and -not $nuget) {
    throw 'Neither tcpkg nor nuget was found. Install the TwinCAT Package Manager or the nuget CLI.'
}

foreach ($nuspec in $nuspecs) {
    if ($tcpkg) {
        & tcpkg pack $nuspec -o $OutputDir
        if ($LASTEXITCODE -ne 0) {
            throw "tcpkg pack failed for $nuspec with code $LASTEXITCODE."
        }
    }
    else {
        & nuget pack $nuspec -OutputDirectory $OutputDir -NoPackageAnalysis
        if ($LASTEXITCODE -ne 0) {
            throw "nuget pack failed for $nuspec with code $LASTEXITCODE."
        }
    }

    [xml]$nuspecXml = Get-Content $nuspec
    $packageId = $nuspecXml.package.metadata.id
    $packageVersion = $nuspecXml.package.metadata.version
    $built = Join-Path $OutputDir "$packageId.$packageVersion.nupkg"
    if (-not (Test-Path $built)) {
        throw "The expected package $built was not produced."
    }
    Write-Host "Built $built"
}
