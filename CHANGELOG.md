# Changelog

## 0.1.2 - 2026-08-12

- Removed the letter-key `J` shortcut so ordinary typing can never open or close the plugin menu.
- Added an **AI + Voice Settings** command to MateEngine's system-tray context menu.
- Left `F8` under MateEngine's ownership for its native **ME Value Changer (Runtime)** menu.

## 0.1.1 - 2026-08-12

- Fixed tiny AI + Voice menu text by targeting a 1920x1080 Unity canvas reference resolution.
- Added persistent 80%-160% panel scaling controls in the menu footer.
- Made the Unity Collections package lookup resilient to changing Unity package-cache hashes.

## 0.1.0 - 2026-08-10

Initial public release.

- Added native OpenRouter and Vercel AI Gateway chat with model catalogs and routing controls.
- Added Fish Audio WebSocket and timestamp SSE transports with four pacing modes.
- Added saved character personalities and per-character Fish voice binding.
- Added NAudio PCM playback with WASAPI and WaveOut fallback.
- Added uLipSync-backed VRM0/VRM1 mouth movement and suppression of MateEngine's fake talking layer.
- Added compact native settings UI, reversible Steam installer, and clean uninstaller.
