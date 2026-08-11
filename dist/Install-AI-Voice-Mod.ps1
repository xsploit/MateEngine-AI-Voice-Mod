[CmdletBinding()]
param(
    [string]$MateEnginePath,
    [string]$ModsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modDll = 'MateEngineAIVoiceMod.dll'
$dependencyDll = 'uLipSync.Runtime.dll'
$audioFallbackDll = 'NAudio.WinMM.dll'
$collectionsDll = 'Unity.Collections.dll'
$mathematicsDll = 'Unity.Mathematics.dll'
$collectionsIlDll = 'Unity.Collections.LowLevel.ILSupport.dll'
$meName = 'MateEngine AI Voice.me'
$scriptRoot = $PSScriptRoot
$runtimeDlls = @($dependencyDll, $audioFallbackDll, $collectionsDll, $mathematicsDll, $collectionsIlDll)

function Test-MateEnginePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return (Test-Path -LiteralPath (Join-Path $Path 'MateEngineX.exe') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Path 'MateEngineX_Data\ScriptingAssemblies.json') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $Path 'MateEngineX_Data\Managed') -PathType Container)
}

function Find-MateEnginePath {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($MateEnginePath)) { $candidates.Add($MateEnginePath) }

    $steamRoots = [System.Collections.Generic.List[string]]::new()
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    if ($programFilesX86) { $steamRoots.Add((Join-Path $programFilesX86 'Steam')) }
    try {
        $steamRegistry = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction Stop
        if ($steamRegistry.SteamPath) { $steamRoots.Add([string]$steamRegistry.SteamPath) }
    } catch { }

    foreach ($steamRoot in @($steamRoots | Select-Object -Unique)) {
        $libraries = [System.Collections.Generic.List[string]]::new()
        $libraries.Add($steamRoot)
        $vdfPath = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdfPath) {
            foreach ($line in Get-Content -LiteralPath $vdfPath) {
                if ($line -match '^\s*"path"\s+"(.+)"') { $libraries.Add($Matches[1].Replace('\\', '\')) }
            }
        }
        foreach ($library in @($libraries | Select-Object -Unique)) {
            $common = Join-Path $library 'steamapps\common'
            $manifestPath = Join-Path $library 'steamapps\appmanifest_3625270.acf'
            if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
                $manifestText = Get-Content -LiteralPath $manifestPath -Raw
                if ($manifestText -match '"installdir"\s+"([^"]+)"') { $candidates.Add((Join-Path $common $Matches[1])) }
            }
            $candidates.Add((Join-Path $common 'MateEngine'))
            $candidates.Add((Join-Path $common 'Mate Engine'))
            $candidates.Add((Join-Path $common 'MateEngineX'))
        }
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-MateEnginePath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    throw 'Mate Engine was not found. Run with -MateEnginePath "D:\SteamLibrary\steamapps\common\MateEngine".'
}

function Write-JsonAtomic([string]$Path, [object]$Value) {
    $tempPath = "$Path.ai-voice.tmp"
    [IO.File]::WriteAllText($tempPath, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempPath -Destination $Path -Force
}

function Add-Assembly([object]$Manifest, [string]$Name) {
    $names = @($Manifest.names)
    $types = @($Manifest.types)
    if ($names.Count -ne $types.Count) { throw 'ScriptingAssemblies.json is invalid: names/types lengths do not match.' }
    if ($names -contains $Name) { return $false }
    $Manifest.names = @($names) + @($Name)
    $Manifest.types = @($types) + @(16)
    return $true
}

if (Get-Process -Name 'MateEngineX' -ErrorAction SilentlyContinue) {
    throw 'Mate Engine is running. Close it before installing the mod.'
}

foreach ($name in @($modDll) + $runtimeDlls + @($meName)) {
    if (-not (Test-Path -LiteralPath (Join-Path $scriptRoot $name) -PathType Leaf)) { throw "Package is missing $name." }
}

$gameRoot = Find-MateEnginePath
$dataRoot = Join-Path $gameRoot 'MateEngineX_Data'
$managedRoot = Join-Path $dataRoot 'Managed'
$assembliesPath = Join-Path $dataRoot 'ScriptingAssemblies.json'
$stateRoot = Join-Path $gameRoot '.mateengine-ai-voice-mod'
$backupRoot = Join-Path $stateRoot 'original'
$statePath = Join-Path $stateRoot 'install-state.json'
$defaultPersistentRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Shinymoon\MateEngineX'
$modsRoot = if ([string]::IsNullOrWhiteSpace($ModsPath)) { Join-Path $defaultPersistentRoot 'Mods' } else { $ModsPath }
$installedMePath = Join-Path $modsRoot $meName

New-Item -ItemType Directory -Force -Path $stateRoot, $backupRoot, $modsRoot | Out-Null
$manifest = Get-Content -LiteralPath $assembliesPath -Raw | ConvertFrom-Json

if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    Copy-Item -LiteralPath $assembliesPath -Destination (Join-Path $backupRoot 'ScriptingAssemblies.json') -Force
    $modDestination = Join-Path $managedRoot $modDll
    $dependencyDestination = Join-Path $managedRoot $dependencyDll
    $audioFallbackDestination = Join-Path $managedRoot $audioFallbackDll
    $modExisted = Test-Path -LiteralPath $modDestination -PathType Leaf
    $dependencyExisted = Test-Path -LiteralPath $dependencyDestination -PathType Leaf
    $dependencyManifestExisted = @($manifest.names) -contains $dependencyDll
    $audioFallbackExisted = Test-Path -LiteralPath $audioFallbackDestination -PathType Leaf
    $audioFallbackManifestExisted = @($manifest.names) -contains $audioFallbackDll
    $collectionsExisted = Test-Path -LiteralPath (Join-Path $managedRoot $collectionsDll) -PathType Leaf
    $collectionsManifestExisted = @($manifest.names) -contains $collectionsDll
    $mathematicsExisted = Test-Path -LiteralPath (Join-Path $managedRoot $mathematicsDll) -PathType Leaf
    $mathematicsManifestExisted = @($manifest.names) -contains $mathematicsDll
    $collectionsIlExisted = Test-Path -LiteralPath (Join-Path $managedRoot $collectionsIlDll) -PathType Leaf
    $collectionsIlManifestExisted = @($manifest.names) -contains $collectionsIlDll
    $meExisted = Test-Path -LiteralPath $installedMePath -PathType Leaf
    if ($modExisted) { Copy-Item -LiteralPath $modDestination -Destination (Join-Path $backupRoot $modDll) -Force }
    if ($meExisted) { Copy-Item -LiteralPath $installedMePath -Destination (Join-Path $backupRoot $meName) -Force }
    $state = [ordered]@{
        version = 1
        gameRoot = $gameRoot
        installedAt = [DateTime]::UtcNow.ToString('o')
        modsPath = $modsRoot
        modDllExisted = $modExisted
        dependencyFileInstalled = (-not $dependencyExisted)
        dependencyManifestAdded = (-not $dependencyManifestExisted)
        audioFallbackFileInstalled = (-not $audioFallbackExisted)
        audioFallbackManifestAdded = (-not $audioFallbackManifestExisted)
        collectionsFileInstalled = (-not $collectionsExisted)
        collectionsManifestAdded = (-not $collectionsManifestExisted)
        mathematicsFileInstalled = (-not $mathematicsExisted)
        mathematicsManifestAdded = (-not $mathematicsManifestExisted)
        collectionsIlFileInstalled = (-not $collectionsIlExisted)
        collectionsIlManifestAdded = (-not $collectionsIlManifestExisted)
        meExisted = $meExisted
    }
    Write-JsonAtomic $statePath $state
}
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json

$stateChanged = $false
foreach ($entry in @(
    @('collectionsFileInstalled', 'collectionsManifestAdded', $collectionsDll),
    @('mathematicsFileInstalled', 'mathematicsManifestAdded', $mathematicsDll),
    @('collectionsIlFileInstalled', 'collectionsIlManifestAdded', $collectionsIlDll)
)) {
    $fileProperty = [string]$entry[0]; $manifestProperty = [string]$entry[1]; $name = [string]$entry[2]
    if (-not ($state.PSObject.Properties.Name -contains $fileProperty)) {
        $state | Add-Member -NotePropertyName $fileProperty -NotePropertyValue (-not (Test-Path -LiteralPath (Join-Path $managedRoot $name) -PathType Leaf))
        $state | Add-Member -NotePropertyName $manifestProperty -NotePropertyValue (-not (@($manifest.names) -contains $name))
        $stateChanged = $true
    }
}
if ($stateChanged) { Write-JsonAtomic $statePath $state }

[void](Add-Assembly $manifest $modDll)
foreach ($name in $runtimeDlls) { [void](Add-Assembly $manifest $name) }
Write-JsonAtomic $assembliesPath $manifest
Copy-Item -LiteralPath (Join-Path $scriptRoot $modDll) -Destination (Join-Path $managedRoot $modDll) -Force
if ([bool]$state.dependencyFileInstalled -or -not (Test-Path -LiteralPath (Join-Path $managedRoot $dependencyDll) -PathType Leaf)) {
    Copy-Item -LiteralPath (Join-Path $scriptRoot $dependencyDll) -Destination (Join-Path $managedRoot $dependencyDll) -Force
}
if (($state.PSObject.Properties.Name -contains 'audioFallbackFileInstalled' -and [bool]$state.audioFallbackFileInstalled) -or
    -not (Test-Path -LiteralPath (Join-Path $managedRoot $audioFallbackDll) -PathType Leaf)) {
    Copy-Item -LiteralPath (Join-Path $scriptRoot $audioFallbackDll) -Destination (Join-Path $managedRoot $audioFallbackDll) -Force
}
foreach ($entry in @(
    @('collectionsFileInstalled', $collectionsDll),
    @('mathematicsFileInstalled', $mathematicsDll),
    @('collectionsIlFileInstalled', $collectionsIlDll)
)) {
    $property = [string]$entry[0]; $name = [string]$entry[1]; $destination = Join-Path $managedRoot $name
    if ([bool]$state.$property -or -not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        Copy-Item -LiteralPath (Join-Path $scriptRoot $name) -Destination $destination -Force
    }
}
Copy-Item -LiteralPath (Join-Path $scriptRoot $meName) -Destination $installedMePath -Force

Write-Host ''
Write-Host 'MateEngine AI + Voice mod installed.' -ForegroundColor Green
Write-Host "Game: $gameRoot"
Write-Host "Mod:  $installedMePath"
Write-Host 'Start Mate Engine and press J to open the native AI + voice settings.'
