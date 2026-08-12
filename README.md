# MateEngine AI + Voice

A native desktop mod that adds remote LLM chat, Fish Audio speech, character profiles, and real audio-driven lip sync to the Steam version of MateEngine.

The complete runtime is a .NET Framework 4.7.2 assembly loaded directly by MateEngine's Unity Mono player. Users do not need a browser, JavaScript, Node, or a companion process.

## Features

- OpenRouter and Vercel AI Gateway with BYOK credentials
- provider model catalogs, endpoint selection, cost/latency/throughput routing, and provider pinning
- Fish Audio voice catalogs and per-character voice assignment
- Fish realtime WebSocket and timestamped HTTP SSE speech transports
- live bridge, stable stream, early-chunk, and sentence-chunk pacing modes
- character/personality profiles stored with the local settings
- native uLipSync analysis plus a final VRM0/VRM1 mouth writer that is not overwritten by MateEngine's animations
- suppression of MateEngine's fake `PET_TALKING` mouth path only while generated speech is active
- a loopback llama.cpp-compatible bridge, allowing MateEngine to retain its native chat/history flow

## Install

Download the binary release ZIP, extract it, close MateEngine, and run `Install-AI-Voice-Mod.cmd`. Start MateEngine and press `J` to open the compact AI + voice panel.

Enter the key for the selected LLM gateway and, for speech, a Fish Audio key. Choose the model, voice, routing, transport, and lip-sync settings, then select **Save**.

Use the **− 100% +** controls in the panel footer to resize the entire interface from 80% to 160%. The selected UI scale is saved locally and restored the next time MateEngine starts. Windows desktop scaling does not control this Unity overlay.

For an explicit installation path:

```powershell
.\Install-AI-Voice-Mod.ps1 -MateEnginePath 'D:\SteamLibrary\steamapps\common\MateEngine'
```

See [STEAM-INSTALLATION.md](STEAM-INSTALLATION.md) for upgrades, exact file changes, verification, and uninstall instructions.

## Local settings

Settings, personalities, routing preferences, voice assignments, and BYOK values are stored locally as plaintext JSON at:

```text
%USERPROFILE%\AppData\LocalLow\Shinymoon\MateEngineX\MateEngineAIVoiceSettings.json
```

They are never included in this source tree or the release archives.

## Build from source

Requirements:

- the MateEngine Unity source project
- Unity `6000.4.4f1`
- Visual Studio 2022 with MSBuild and .NET Framework 4.7.2 targeting support
- the MateEngine project's resolved uLipSync, Unity Collections, Unity Mathematics, and NAudio packages

Build the DLL and `.me` package:

```powershell
.\Build-Mod.ps1 -MateEngineProject 'D:\path\to\Mate-Engine'
```

Create tested versioned binary and source archives:

```powershell
.\Release-Mod.ps1 -Version 0.1.1 -MateEngineProject 'D:\path\to\Mate-Engine'
```

Use `-SkipBuild` only when `dist` already contains a freshly validated build.

## Verified for v0.1.1

- Release assembly builds deterministically as version `0.1.1.0`.
- All 20 protocol, routing, speech, playback, lip-sync, and settings tests pass.
- Unity batch validation confirms all 71 serialized menu controls are assigned before packaging.
- Installer tests cover clean install, idempotent reinstall, uninstall, runtime dependencies, and preservation of an existing MateEngine uLipSync installation.
- Steam AppID `3625270`, BuildID `21557579`, completed a real Vercel streamed response and Fish WebSocket TTS run with PCM playback and native mouth movement.

## Distribution and attribution

This project contains adapted MateEngine UI prefab assets and is distributed non-commercially under the full MateEngine Pro License v2.1. Public distribution requires the complete corresponding source and the original license/attribution. It is not affiliated with or endorsed by the MateEngine developers.

See [LICENSE.md](LICENSE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before redistributing the mod.
