$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $PSScriptRoot 'publish'
$outputDir = Join-Path $PSScriptRoot 'output'
$wix = Join-Path $projectRoot '.codex-tools\wix\wix.exe'
$msiPath = Join-Path $outputDir 'KsyxisTweaks-1.0.0-x64.msi'

if (-not (Test-Path $wix)) {
    dotnet tool install wix --tool-path (Join-Path $projectRoot '.codex-tools\wix')
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir, $outputDir | Out-Null

dotnet publish (Join-Path $projectRoot 'AestheticOptimizer\AxeOptimizer.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -p:EnableWindowsTargeting=true

& $wix --acceptEula wix7 build (Join-Path $PSScriptRoot 'KsyxisTweaks.wxs') `
    -d "PublishDir=$publishDir" `
    -o $msiPath

if ($LASTEXITCODE -ne 0) { throw "WiX zakończył pracę kodem $LASTEXITCODE." }
Write-Host "Utworzono: $msiPath"
