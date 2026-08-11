[CmdletBinding()]
param(
    [string]$MateEngineProject = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Mate-Engine'),
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe',
    [string]$MSBuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$MateEngineProject = (Resolve-Path -LiteralPath $MateEngineProject).Path
foreach ($path in @($UnityEditor, $MSBuild, (Join-Path $MateEngineProject 'Assets'), (Join-Path $MateEngineProject 'Packages\manifest.json'))) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required build path is missing: $path" }
}

$projectLipSync = Join-Path $MateEngineProject 'Library\ScriptAssemblies\uLipSync.Runtime.dll'
$lipSyncReference = if (Test-Path -LiteralPath $projectLipSync -PathType Leaf) { $projectLipSync } else { Join-Path $projectRoot 'dist\uLipSync.Runtime.dll' }
$playerAssemblies = Join-Path $MateEngineProject 'Library\Bee\PlayerScriptAssemblies'
$collectionsSource = Join-Path $playerAssemblies 'Unity.Collections.dll'
$mathematicsSource = Join-Path $playerAssemblies 'Unity.Mathematics.dll'
$collectionsIlRoot = Join-Path $MateEngineProject 'Library\PackageCache\com.unity.collections@9c2353fb96f6\Unity.Collections.LowLevel.ILSupport'
$collectionsIlSource = Join-Path $collectionsIlRoot 'Unity.Collections.LowLevel.ILSupport.dll'
$winMmSource = Join-Path $MateEngineProject 'Assets\Packages\NAudio.WinMM.2.2.1\lib\netstandard2.0\NAudio.WinMM.dll'
foreach ($required in @($winMmSource, $collectionsSource, $mathematicsSource, $collectionsIlSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required runtime dependency is missing: $required" }
}
& $MSBuild (Join-Path $projectRoot 'MateEngineAIVoiceMod.csproj') /t:Rebuild /p:Configuration=Release "/p:MateEngineProjectDir=$MateEngineProject" "/p:ULipSyncAssembly=$lipSyncReference" /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Mod DLL build failed with exit code $LASTEXITCODE." }

$assetRoot = Join-Path $MateEngineProject 'Assets\AIVoiceMod'
$templateRoot = Join-Path $MateEngineProject 'Assets\AIVoiceModTemplate'
$editorRoot = Join-Path $MateEngineProject 'Assets\Editor'
New-Item -ItemType Directory -Force -Path $assetRoot, $templateRoot, $editorRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\MateEngineAIVoiceMod.dll') -Destination (Join-Path $assetRoot 'MateEngineAIVoiceMod.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'unity\Editor\MateEngineAIVoiceMenuBuilder.cs') -Destination (Join-Path $editorRoot 'MateEngineAIVoiceMenuBuilder.cs') -Force
foreach ($name in @('Button.prefab', 'Dropdown.prefab', 'Input.prefab', 'Toggle.prefab')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "unity\AIVoiceModTemplate\$name") -Destination (Join-Path $templateRoot $name) -Force
}

$packageManifest = Get-Content -LiteralPath (Join-Path $MateEngineProject 'Packages\manifest.json') -Raw
if ($packageManifest -notmatch 'com\.hecomi\.ulipsync') {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\uLipSync.Runtime.dll') -Destination (Join-Path $assetRoot 'uLipSync.Runtime.dll') -Force
}

$env:MATEENGINE_AI_VOICE_MOD_ROOT = $projectRoot
$logPath = Join-Path $projectRoot 'unity-menu-build.log'
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $MateEngineProject, '-executeMethod', 'MateEngineAIVoiceMenuBuilder.Build', '-logFile', $logPath)
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unity menu build failed with exit code $($process.ExitCode). See $logPath" }

$mePath = Join-Path $projectRoot 'dist\MateEngine AI Voice.me'
if (-not (Test-Path -LiteralPath $mePath -PathType Leaf)) { throw "Unity exited successfully but did not produce $mePath" }
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\MateEngineAIVoiceMod.dll') -Destination (Join-Path $projectRoot 'dist\MateEngineAIVoiceMod.dll') -Force
$retarget = Join-Path $projectRoot 'tools\Retarget-NetstandardAssembly.ps1'
$managedFallback = Join-Path $projectRoot 'tools\Use-ManagedBurstFallback.ps1'
$unityEngineManaged = Join-Path (Split-Path -Parent $UnityEditor) 'Data\Managed\UnityEngine'
$retargetSearch = @($playerAssemblies, $collectionsIlRoot, (Join-Path $MateEngineProject 'Library\ScriptAssemblies'), $unityEngineManaged)
$retargetedLipSync = Join-Path $projectRoot 'obj\uLipSync.Runtime.retargeted.dll'
& $retarget -InputPath $lipSyncReference -OutputPath $retargetedLipSync -SearchDirectories $retargetSearch
& $managedFallback -InputPath $retargetedLipSync -OutputPath (Join-Path $projectRoot 'dist\uLipSync.Runtime.dll') -SearchDirectories $retargetSearch
& $retarget -InputPath $collectionsSource -OutputPath (Join-Path $projectRoot 'dist\Unity.Collections.dll') -SearchDirectories $retargetSearch
& $retarget -InputPath $mathematicsSource -OutputPath (Join-Path $projectRoot 'dist\Unity.Mathematics.dll') -SearchDirectories $retargetSearch
& $retarget -InputPath $collectionsIlSource -OutputPath (Join-Path $projectRoot 'dist\Unity.Collections.LowLevel.ILSupport.dll') -SearchDirectories $retargetSearch
& $retarget -InputPath $winMmSource -OutputPath (Join-Path $projectRoot 'dist\NAudio.WinMM.dll') -SearchDirectories $retargetSearch
Write-Host "Built $mePath" -ForegroundColor Green
