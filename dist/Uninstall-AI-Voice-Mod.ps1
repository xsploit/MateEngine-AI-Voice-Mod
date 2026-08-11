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
$legacyHttpDll = 'System.Net.Http.dll'
$meName = 'MateEngine AI Voice.me'

function Test-MateEnginePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return Test-Path -LiteralPath (Join-Path $Path 'MateEngineX_Data\ScriptingAssemblies.json') -PathType Leaf
}

function Find-MateEnginePath {
    if (Test-MateEnginePath $MateEnginePath) { return (Resolve-Path -LiteralPath $MateEnginePath).Path }
    $roots = [System.Collections.Generic.List[string]]::new()
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    if ($programFilesX86) { $roots.Add((Join-Path $programFilesX86 'Steam')) }
    try {
        $steamRegistry = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction Stop
        if ($steamRegistry.SteamPath) { $roots.Add([string]$steamRegistry.SteamPath) }
    } catch { }
    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($root in @($roots | Select-Object -Unique)) {
        $libraries = [System.Collections.Generic.List[string]]::new(); $libraries.Add($root)
        $vdfPath = Join-Path $root 'steamapps\libraryfolders.vdf'
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
            $candidates.Add((Join-Path $common 'MateEngine')); $candidates.Add((Join-Path $common 'Mate Engine'))
            $candidates.Add((Join-Path $common 'MateEngineX'))
        }
    }
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (Test-MateEnginePath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    throw 'Mate Engine was not found. Run with -MateEnginePath pointing at its installation folder.'
}

function Write-JsonAtomic([string]$Path, [object]$Value) {
    $tempPath = "$Path.ai-voice.tmp"
    [IO.File]::WriteAllText($tempPath, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tempPath -Destination $Path -Force
}

function Remove-Assembly([object]$Manifest, [string]$Name) {
    $names = @($Manifest.names); $types = @($Manifest.types)
    if ($names.Count -ne $types.Count) { throw 'ScriptingAssemblies.json is invalid: names/types lengths do not match.' }
    $newNames = [System.Collections.Generic.List[object]]::new(); $newTypes = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $names.Count; $i++) {
        if ([string]$names[$i] -eq $Name) { continue }
        $newNames.Add($names[$i]); $newTypes.Add($types[$i])
    }
    $Manifest.names = @($newNames); $Manifest.types = @($newTypes)
}

if (Get-Process -Name 'MateEngineX' -ErrorAction SilentlyContinue) {
    throw 'Mate Engine is running. Close it before uninstalling the mod.'
}

$gameRoot = Find-MateEnginePath
$dataRoot = Join-Path $gameRoot 'MateEngineX_Data'
$managedRoot = Join-Path $dataRoot 'Managed'
$assembliesPath = Join-Path $dataRoot 'ScriptingAssemblies.json'
$stateRoot = Join-Path $gameRoot '.mateengine-ai-voice-mod'
$backupRoot = Join-Path $stateRoot 'original'
$statePath = Join-Path $stateRoot 'install-state.json'
$state = if (Test-Path -LiteralPath $statePath -PathType Leaf) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
$defaultPersistentRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Shinymoon\MateEngineX'
if ([string]::IsNullOrWhiteSpace($ModsPath)) {
    if ($state -and $state.PSObject.Properties.Name -contains 'modsPath') { $ModsPath = [string]$state.modsPath }
    else { $ModsPath = Join-Path $defaultPersistentRoot 'Mods' }
}
$installedMePath = Join-Path $ModsPath $meName

$manifest = Get-Content -LiteralPath $assembliesPath -Raw | ConvertFrom-Json
Remove-Assembly $manifest $modDll
if ($state -and [bool]$state.dependencyManifestAdded) { Remove-Assembly $manifest $dependencyDll }
if ($state -and $state.PSObject.Properties.Name -contains 'audioFallbackManifestAdded' -and [bool]$state.audioFallbackManifestAdded) { Remove-Assembly $manifest $audioFallbackDll }
if ($state -and $state.PSObject.Properties.Name -contains 'collectionsManifestAdded' -and [bool]$state.collectionsManifestAdded) { Remove-Assembly $manifest $collectionsDll }
if ($state -and $state.PSObject.Properties.Name -contains 'mathematicsManifestAdded' -and [bool]$state.mathematicsManifestAdded) { Remove-Assembly $manifest $mathematicsDll }
if ($state -and $state.PSObject.Properties.Name -contains 'collectionsIlManifestAdded' -and [bool]$state.collectionsIlManifestAdded) { Remove-Assembly $manifest $collectionsIlDll }
if ($state -and $state.PSObject.Properties.Name -contains 'httpManifestAdded' -and [bool]$state.httpManifestAdded) { Remove-Assembly $manifest $legacyHttpDll }
Write-JsonAtomic $assembliesPath $manifest

$modDestination = Join-Path $managedRoot $modDll
if ($state -and [bool]$state.modDllExisted -and (Test-Path -LiteralPath (Join-Path $backupRoot $modDll) -PathType Leaf)) {
    Copy-Item -LiteralPath (Join-Path $backupRoot $modDll) -Destination $modDestination -Force
} elseif (Test-Path -LiteralPath $modDestination -PathType Leaf) {
    Remove-Item -LiteralPath $modDestination -Force
}

if ($state -and [bool]$state.dependencyFileInstalled) {
    $dependencyDestination = Join-Path $managedRoot $dependencyDll
    if (Test-Path -LiteralPath $dependencyDestination -PathType Leaf) { Remove-Item -LiteralPath $dependencyDestination -Force }
}
if ($state -and $state.PSObject.Properties.Name -contains 'audioFallbackFileInstalled' -and [bool]$state.audioFallbackFileInstalled) {
    $audioFallbackDestination = Join-Path $managedRoot $audioFallbackDll
    if (Test-Path -LiteralPath $audioFallbackDestination -PathType Leaf) { Remove-Item -LiteralPath $audioFallbackDestination -Force }
}
foreach ($entry in @(
    @('collectionsFileInstalled', $collectionsDll),
    @('mathematicsFileInstalled', $mathematicsDll),
    @('collectionsIlFileInstalled', $collectionsIlDll)
)) {
    $property = [string]$entry[0]; $name = [string]$entry[1]
    if ($state -and $state.PSObject.Properties.Name -contains $property -and [bool]$state.$property) {
        $destination = Join-Path $managedRoot $name
        if (Test-Path -LiteralPath $destination -PathType Leaf) { Remove-Item -LiteralPath $destination -Force }
    }
}
if ($state -and $state.PSObject.Properties.Name -contains 'httpFileInstalled' -and [bool]$state.httpFileInstalled) {
    $legacyHttpDestination = Join-Path $managedRoot $legacyHttpDll
    if (Test-Path -LiteralPath $legacyHttpDestination -PathType Leaf) { Remove-Item -LiteralPath $legacyHttpDestination -Force }
}

if ($state -and [bool]$state.meExisted -and (Test-Path -LiteralPath (Join-Path $backupRoot $meName) -PathType Leaf)) {
    New-Item -ItemType Directory -Force -Path $ModsPath | Out-Null
    Copy-Item -LiteralPath (Join-Path $backupRoot $meName) -Destination $installedMePath -Force
} elseif (Test-Path -LiteralPath $installedMePath -PathType Leaf) {
    Remove-Item -LiteralPath $installedMePath -Force
}
if (Test-Path -LiteralPath $statePath -PathType Leaf) { Remove-Item -LiteralPath $statePath -Force }

Write-Host ''
Write-Host 'MateEngine AI + Voice mod uninstalled.' -ForegroundColor Green
Write-Host "Recovery backups remain at $backupRoot"
