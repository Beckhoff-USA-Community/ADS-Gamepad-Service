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
# The documentation site pages ship as plain markdown under site
robocopy (Join-Path $repoRoot 'Documentation\docs') (Join-Path $docsBin 'site') *.md /S | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Staging the documentation site failed, robocopy code $LASTEXITCODE."
}
cmd /c exit 0

# The TcCOM source payload is the project tree without build output, user
# settings and licensing files
$sourceBin = Join-Path $PSScriptRoot 'AdsGamepad.TcComSource\bin'
if (Test-Path $sourceBin) {
    Remove-Item $sourceBin -Recurse -Force
}
robocopy (Join-Path $repoRoot 'tccom\Gamepad_TcCOM') $sourceBin /E /XD _Repository _Boot _products _Deployment .vs /XF *.bak *.user *.tclrs *.suo TcSignLog.txt | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Staging the TcCOM source failed, robocopy code $LASTEXITCODE."
}
cmd /c exit 0

# The module readme travels with the source it describes
Copy-Item (Join-Path $repoRoot 'tccom\README.md') (Join-Path $sourceBin 'README.md')

# The packaged project must not point at anyone's hardware
if (Select-String -Path (Join-Path $sourceBin 'Gamepad_TcCOM.tsproj') -Pattern 'TargetNetId="[^"]' -Quiet) {
    throw 'The staged tsproj still carries a TargetNetId. Set the project target to Local before packing.'
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$nuspecs = @(
    (Join-Path $PSScriptRoot 'AdsGamepadService.Service\AdsGamepadService.Service.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.PlcLibrary\AdsGamepad.PlcLibrary.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.Documentation\AdsGamepad.Documentation.nuspec'),
    (Join-Path $PSScriptRoot 'AdsGamepad.TcComSource\AdsGamepad.TcComSource.nuspec'),
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
