[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$MateEngineProject = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Mate-Engine'),
    [switch]$SkipBuild,
    [switch]$SkipInstallerTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$distRoot = Join-Path $projectRoot 'dist'
$releaseRoot = Join-Path $projectRoot 'release'
$product = "MateEngine-AI-Voice-Mod-v$Version"
$stageRoot = Join-Path $releaseRoot $product
$sourceStageRoot = Join-Path $releaseRoot "$product-source"
$binaryZip = Join-Path $releaseRoot "$product-windows.zip"
$sourceZip = Join-Path $releaseRoot "$product-source.zip"

$assemblyInfo = Get-Content -LiteralPath (Join-Path $projectRoot 'Properties\AssemblyInfo.cs') -Raw
if ($assemblyInfo -notmatch ('AssemblyVersion\("' + [Regex]::Escape($Version) + '\.0"\)')) {
    throw "Properties\AssemblyInfo.cs does not declare version $Version.0."
}

if (-not $SkipBuild) {
    & (Join-Path $projectRoot 'Build-Mod.ps1') -MateEngineProject $MateEngineProject
    if ($LASTEXITCODE -ne 0) { throw "Build-Mod.ps1 failed with exit code $LASTEXITCODE." }
}

& dotnet run --project (Join-Path $projectRoot 'tests\ProtocolTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Protocol tests failed with exit code $LASTEXITCODE." }

if (-not $SkipInstallerTests) {
    & (Join-Path $projectRoot 'tests\Test-Installer.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Installer tests failed with exit code $LASTEXITCODE." }
}

$packageFiles = @(
    'Install-AI-Voice-Mod.cmd',
    'Install-AI-Voice-Mod.ps1',
    'Uninstall-AI-Voice-Mod.cmd',
    'Uninstall-AI-Voice-Mod.ps1',
    'MateEngine AI Voice.me',
    'MateEngineAIVoiceMod.dll',
    'NAudio.WinMM.dll',
    'uLipSync.Runtime.dll',
    'Unity.Collections.dll',
    'Unity.Collections.LowLevel.ILSupport.dll',
    'Unity.Mathematics.dll'
)

foreach ($name in $packageFiles) {
    $path = Join-Path $distRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release input is missing: $path" }
}

foreach ($directory in @($stageRoot, $sourceStageRoot)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $stageRoot, $sourceStageRoot, $releaseRoot | Out-Null

foreach ($name in $packageFiles) {
    Copy-Item -LiteralPath (Join-Path $distRoot $name) -Destination (Join-Path $stageRoot $name) -Force
}
foreach ($name in @('README.md', 'STEAM-INSTALLATION.md', 'LICENSE.md', 'NOTICE.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $stageRoot $name) -Force
}

$innerChecksums = foreach ($file in Get-ChildItem -LiteralPath $stageRoot -File | Sort-Object Name) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $file.Name
}
[IO.File]::WriteAllLines((Join-Path $stageRoot 'SHA256SUMS.txt'), $innerChecksums, [Text.UTF8Encoding]::new($false))

foreach ($archive in @($binaryZip, $sourceZip)) {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}
Compress-Archive -LiteralPath $stageRoot -DestinationPath $binaryZip -CompressionLevel Optimal

$sourceItems = @(
    '.gitattributes', '.gitignore', 'Build-Mod.ps1', 'Release-Mod.ps1',
    'MateEngineAIVoiceMod.csproj', 'README.md', 'STEAM-INSTALLATION.md',
    'LICENSE.md', 'NOTICE.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md',
    'Properties', 'Resources', 'docs', 'src', 'tools', 'unity',
    'tests\ProtocolTests.cs', 'tests\ProtocolTests.csproj', 'tests\Test-Installer.ps1',
    'dist\Install-AI-Voice-Mod.cmd', 'dist\Install-AI-Voice-Mod.ps1',
    'dist\Uninstall-AI-Voice-Mod.cmd', 'dist\Uninstall-AI-Voice-Mod.ps1'
)
$sourcePaths = foreach ($item in $sourceItems) {
    $path = Join-Path $projectRoot $item
    if (-not (Test-Path -LiteralPath $path)) { throw "Source release input is missing: $path" }
    $destination = Join-Path $sourceStageRoot $item
    $destinationParent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    if (Test-Path -LiteralPath $path -PathType Container) {
        Copy-Item -LiteralPath $path -Destination $destinationParent -Recurse -Force
    } else {
        Copy-Item -LiteralPath $path -Destination $destination -Force
    }
}
Compress-Archive -LiteralPath $sourceStageRoot -DestinationPath $sourceZip -CompressionLevel Optimal

$outerChecksums = foreach ($archive in @($binaryZip, $sourceZip)) {
    $hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $archive)
}
[IO.File]::WriteAllLines((Join-Path $releaseRoot 'SHA256SUMS.txt'), $outerChecksums, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host "Release v$Version created." -ForegroundColor Green
Write-Host $binaryZip
Write-Host $sourceZip
Write-Host (Join-Path $releaseRoot 'SHA256SUMS.txt')
