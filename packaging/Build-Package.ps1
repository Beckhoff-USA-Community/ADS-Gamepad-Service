# Builds all TwinCAT packages for the project.
# Requires the .NET SDK. Packs with tcpkg when it is installed, which is the
# case on any machine with the TwinCAT Package Manager, and falls back to
# nuget otherwise. The package versions come from the nuspec files.
#
#   .\packaging\Build-Package.ps1
#
# The finished packages land in packaging\bin\release.

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'bin\release')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

# The service binaries are published fresh on every run
$serviceBin = Join-Path $PSScriptRoot 'AdsGamepadService.Service\bin'
if (Test-Path $serviceBin) {
    Remove-Item $serviceBin -Recurse -Force
}
dotnet publish (Join-Path $repoRoot 'src\AdsGamepadService') -c Release -r win-x64 --self-contained -o $serviceBin
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the service failed.'
}

# The documentation payload is staged from the repository, so the packaged
# documents always match the committed state
$docsBin = Join-Path $PSScriptRoot 'AdsGamepad.Documentation\bin'
if (Test-Path $docsBin) {
    Remove-Item $docsBin -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $docsBin | Out-Null
Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $docsBin 'README.md')
Copy-Item (Join-Path $repoRoot 'CONFIGURATION.md') (Join-Path $docsBin 'CONFIGURATION.md')
Copy-Item (Join-Path $repoRoot 'MIGRATION.md') (Join-Path $docsBin 'MIGRATION.md')
Copy-Item (Join-Path $repoRoot 'tccom\README.md') (Join-Path $docsBin 'TcComModule.md')
# The documentation site pages ship as plain markdown with their images
robocopy (Join-Path $repoRoot 'Documentation\docs') (Join-Path $docsBin 'site') *.md *.png *.svg /S | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Staging the documentation site failed, robocopy code $LASTEXITCODE."
}
cmd /c exit 0

# The committed project must not point at anyone's hardware. The compiled
# TcCOM payload is committed under AdsGamepad.TcCom\repository, so there is
# nothing to stage for it, but the source stays in the repository and this
# guard keeps a lab target id from ever reaching the public tree.
$tsprojFiles = Get-ChildItem (Join-Path $repoRoot 'tccom'), (Join-Path $repoRoot 'plc') -Recurse -Filter *.tsproj
foreach ($tsproj in $tsprojFiles) {
    if (Select-String -Path $tsproj.FullName -Pattern 'TargetNetId="[^"]' -Quiet) {
        throw "A committed project file carries a TargetNetId: $($tsproj.FullName). Set the project target to Local."
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$nuspecs = @(
    (Join-Path $PSScriptRoot 'AdsGamepadService.Service\AdsGamepadService.Service.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.PlcLibrary\AdsGamepad.PlcLibrary.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.Documentation\AdsGamepad.Documentation.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.TcCom\AdsGamepad.TcCom.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.XAR\AdsGamepad.XAR.Workload.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.XAE\AdsGamepad.XAE.Workload.nuspec')
)

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
