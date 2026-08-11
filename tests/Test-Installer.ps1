$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $projectRoot 'dist'
$install = Join-Path $dist 'Install-AI-Voice-Mod.ps1'
$uninstall = Join-Path $dist 'Uninstall-AI-Voice-Mod.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('MateEngineAIVoiceInstallerTest-' + [Guid]::NewGuid().ToString('N'))

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-Game([string]$Name, [bool]$WithLipSync) {
    $game = Join-Path $testRoot $Name
    $managed = Join-Path $game 'MateEngineX_Data\Managed'
    New-Item -ItemType Directory -Force -Path $managed | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $game 'MateEngineX.exe'), [byte[]]@(77, 90))
    $names = @('Assembly-CSharp.dll')
    $types = @(16)
    if ($WithLipSync) {
        $names += 'uLipSync.Runtime.dll'; $types += 16
        [IO.File]::WriteAllBytes((Join-Path $managed 'uLipSync.Runtime.dll'), [byte[]]@(1, 2, 3, 4))
    }
    $manifest = [ordered]@{ names = $names; types = $types }
    [IO.File]::WriteAllText((Join-Path $game 'MateEngineX_Data\ScriptingAssemblies.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    return $game
}

function Read-Manifest([string]$Game) {
    return Get-Content -LiteralPath (Join-Path $Game 'MateEngineX_Data\ScriptingAssemblies.json') -Raw | ConvertFrom-Json
}

try {
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

    $clean = New-Game 'clean' $false
    $cleanMods = Join-Path $testRoot 'clean-mods'
    & $install -MateEnginePath $clean -ModsPath $cleanMods | Out-Null
    & $install -MateEnginePath $clean -ModsPath $cleanMods | Out-Null
    $manifest = Read-Manifest $clean
    Assert (@($manifest.names | Where-Object { $_ -eq 'MateEngineAIVoiceMod.dll' }).Count -eq 1) 'Clean install duplicated the mod assembly.'
    Assert (@($manifest.names | Where-Object { $_ -eq 'uLipSync.Runtime.dll' }).Count -eq 1) 'Clean install duplicated the uLipSync dependency.'
    Assert (@($manifest.names | Where-Object { $_ -eq 'NAudio.WinMM.dll' }).Count -eq 1) 'Clean install duplicated the WaveOut dependency.'
    foreach ($name in @('Unity.Collections.dll', 'Unity.Mathematics.dll', 'Unity.Collections.LowLevel.ILSupport.dll')) {
        Assert (@($manifest.names | Where-Object { $_ -eq $name }).Count -eq 1) "Clean install did not register $name exactly once."
        Assert (Test-Path -LiteralPath (Join-Path $clean "MateEngineX_Data\Managed\$name")) "Clean install did not copy $name."
    }
    Assert (Test-Path -LiteralPath (Join-Path $clean 'MateEngineX_Data\Managed\MateEngineAIVoiceMod.dll')) 'Clean install did not copy the mod DLL.'
    Assert (Test-Path -LiteralPath (Join-Path $cleanMods 'MateEngine AI Voice.me')) 'Clean install did not copy the .me file.'
    & $uninstall -MateEnginePath $clean -ModsPath $cleanMods | Out-Null
    $manifest = Read-Manifest $clean
    Assert (-not (@($manifest.names) -contains 'MateEngineAIVoiceMod.dll')) 'Clean uninstall left the mod assembly registered.'
    Assert (-not (@($manifest.names) -contains 'uLipSync.Runtime.dll')) 'Clean uninstall left its dependency registered.'
    Assert (-not (@($manifest.names) -contains 'NAudio.WinMM.dll')) 'Clean uninstall left the WaveOut dependency registered.'
    foreach ($name in @('Unity.Collections.dll', 'Unity.Mathematics.dll', 'Unity.Collections.LowLevel.ILSupport.dll')) {
        Assert (-not (@($manifest.names) -contains $name)) "Clean uninstall left $name registered."
        Assert (-not (Test-Path -LiteralPath (Join-Path $clean "MateEngineX_Data\Managed\$name"))) "Clean uninstall left $name."
    }
    Assert (-not (Test-Path -LiteralPath (Join-Path $clean 'MateEngineX_Data\Managed\NAudio.WinMM.dll'))) 'Clean uninstall left the WaveOut dependency DLL.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $clean 'MateEngineX_Data\Managed\MateEngineAIVoiceMod.dll'))) 'Clean uninstall left the mod DLL.'

    $existing = New-Game 'existing-ulipsync' $true
    $existingMods = Join-Path $testRoot 'existing-mods'
    $lipPath = Join-Path $existing 'MateEngineX_Data\Managed\uLipSync.Runtime.dll'
    $before = Get-FileHash -LiteralPath $lipPath -Algorithm SHA256
    & $install -MateEnginePath $existing -ModsPath $existingMods | Out-Null
    $after = Get-FileHash -LiteralPath $lipPath -Algorithm SHA256
    Assert ($before.Hash -eq $after.Hash) 'Installer overwrote Mate Engine''s existing uLipSync DLL.'
    & $uninstall -MateEnginePath $existing -ModsPath $existingMods | Out-Null
    $manifest = Read-Manifest $existing
    Assert (@($manifest.names) -contains 'uLipSync.Runtime.dll') 'Uninstall removed Mate Engine''s existing uLipSync registration.'
    Assert (Test-Path -LiteralPath $lipPath) 'Uninstall removed Mate Engine''s existing uLipSync DLL.'

    'PASS clean install and idempotent reinstall'
    'PASS clean uninstall'
    'PASS existing uLipSync preservation'
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTest = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTest)) {
        Remove-Item -LiteralPath $resolvedTest -Recurse -Force
    }
}
