# Steam installation

This package targets the native Windows Steam release of MateEngine, AppID `3625270`. It runs entirely inside the desktop application and does not require Node, a browser, or a companion process.

## Install

1. Install MateEngine in Steam and launch it once.
2. Close MateEngine completely.
3. Extract the release ZIP and run `Install-AI-Voice-Mod.cmd`.
4. Start MateEngine from Steam.
5. Press `J` to open **MateEngine AI + Voice**.
6. Enter the applicable OpenRouter or Vercel AI Gateway key and, for speech, a Fish Audio key.
7. Choose the model, route, voice, Fish transport/pacing, personality, and lip-sync settings, then select **Save**.

The installer reads Steam's `libraryfolders.vdf` and AppID `3625270`, so registered libraries are discovered automatically. To select a specific installation:

```powershell
& '.\Install-AI-Voice-Mod.ps1' -MateEnginePath 'D:\SteamLibrary\steamapps\common\MateEngine'
```

## What the installer changes

The installer copies and registers these files in `MateEngineX_Data\Managed`:

- `MateEngineAIVoiceMod.dll`
- `uLipSync.Runtime.dll`
- `NAudio.WinMM.dll`
- `Unity.Collections.dll`
- `Unity.Collections.LowLevel.ILSupport.dll`
- `Unity.Mathematics.dll`

It copies `MateEngine AI Voice.me` to:

```text
%USERPROFILE%\AppData\LocalLow\Shinymoon\MateEngineX\Mods
```

Before the first change, the installer saves the original scripting manifest and any file it may replace under:

```text
<MateEngine folder>\.mateengine-ai-voice-mod\original
```

Installation ownership is recorded in `.mateengine-ai-voice-mod\install-state.json`. Re-running the installer is the supported upgrade path. It replaces this mod's DLL and `.me` package without duplicating manifest entries, while preserving compatible assemblies that were present before the mod.

## Settings and API keys

Settings, BYOK values, routing preferences, personalities, and per-character voices are stored as local plaintext JSON at:

```text
%USERPROFILE%\AppData\LocalLow\Shinymoon\MateEngineX\MateEngineAIVoiceSettings.json
```

The active LLM provider determines which gateway key is used. Fish speech requires the Fish key, a selected or manual voice ID, and automatic speech enabled. Select **Save** after changing the panel.

## Verify installation

With MateEngine closed:

```powershell
$game = 'D:\SteamLibrary\steamapps\common\MateEngine'
$required = @(
  'MateEngineAIVoiceMod.dll',
  'uLipSync.Runtime.dll',
  'NAudio.WinMM.dll',
  'Unity.Collections.dll',
  'Unity.Collections.LowLevel.ILSupport.dll',
  'Unity.Mathematics.dll'
)
$required | ForEach-Object { Get-Item (Join-Path "$game\MateEngineX_Data\Managed" $_) }
Select-String -Path "$game\MateEngineX_Data\ScriptingAssemblies.json" -Pattern ($required -join '|')
```

The Unity player log is normally:

```text
%USERPROFILE%\AppData\LocalLow\Shinymoon\MateEngineX\Player.log
```

A healthy startup includes:

```text
[MateEngineAIVoice] Runtime ready. Proxy=127.0.0.1:<port>
```

Port `13333` is preferred. If another process owns it, the mod selects the next available port. A WASAPI format error followed by `using WaveOut` is also expected on devices that reject Fish's PCM format.

## Upgrade and Steam verification

Close MateEngine and run `Install-AI-Voice-Mod.cmd` from the newer extracted release.

Steam's **Verify integrity of game files** or a game update may restore `ScriptingAssemblies.json` and remove injected DLLs. Close MateEngine and rerun the installer afterward. The `.me` file and saved settings are outside the Steam game directory and normally remain in place.

## Validated runtime

Release `0.1.0` was validated on August 10, 2026 against Steam BuildID `21557579`. The installed player, BYOK settings, and a custom VRM confirmed:

- `.me` bootstrap and injected desktop runtime initialization;
- custom VRM discovery and binding;
- a streamed Vercel response with terminal completion;
- Fish realtime WebSocket PCM delivery;
- WASAPI-to-WaveOut fallback playback;
- speech-driven uLipSync mouth movement;
- no targeted provider, WebSocket, missing-assembly, or native-entry-point errors.

## Uninstall

Close MateEngine, then run `Uninstall-AI-Voice-Mod.cmd`, or specify the installation:

```powershell
& '.\Uninstall-AI-Voice-Mod.ps1' -MateEnginePath 'D:\SteamLibrary\steamapps\common\MateEngine'
```

The uninstaller removes only files and manifest registrations owned by this installation and restores backed-up pre-existing files. Recovery backups remain under `.mateengine-ai-voice-mod\original`. Local settings are intentionally retained.

## If chat stays on Loading

- Press `J`, confirm the selected provider has its matching key, and select **Save**.
- Refresh/select a model supported by the provider. Vercel IDs include creator and model names.
- Check `Player.log` for the terminal provider error.
- Confirm all six managed DLLs are present and registered, then rerun the installer after a Steam update or verification.
