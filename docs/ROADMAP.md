# Roadmap

## Input and menu ownership

- `F8` remains MateEngine's native **ME Value Changer (Runtime)** shortcut.
- AI + Voice settings reopen from MateEngine's system-tray menu and do not own a typing key.
- `F9` is reserved for a compact AI composer once that composer is implemented. It must never toggle the full settings panel.
- The radial chat action remains unchanged. Messages submitted through MateEngine's radial chat may continue to use its native bubbles.

## Compact composer

Build a small floating Unity overlay containing a text area, microphone control, and send button. `F9` toggles the overlay; Escape dismisses it only while it is focused.

Composer submissions call `LLMCharacter.Chat` directly with history enabled. This keeps MateEngine's real conversation history and the existing loopback LLM, TTS, and lip-sync path, but bypasses `ChatBot.onInputFieldSubmit`, which is the code that creates player and AI bubbles.

The overlay should show only concise states such as **Listening**, **Transcribing**, **Thinking**, and an error. It should not recreate the full bubble transcript.

## Speech to text

The first STT implementation will use OpenRouter's dedicated `POST /api/v1/audio/transcriptions` endpoint with the user's existing OpenRouter key. Record mono PCM to an in-memory WAV with NAudio, send it after the user stops recording, then place the returned transcript into the composer for review. Auto-send should be optional and off by default.

Default model: `openai/whisper-large-v3`. Keep the model configurable so available transcription models can change without rebuilding the mod.

OpenRouter is the first implementation because it adds no local model download or additional native inference runtime to the Steam mod. A local **Whisper.NET** provider can follow as an optional offline mode, with explicit model download, CPU/GPU runtime selection, and disk-usage UI.

References:

- [OpenRouter audio transcription API](https://openrouter.ai/docs/api/api-reference/transcriptions/create-audio-transcriptions)
- [Whisper.NET](https://github.com/sandrohanea/whisper.net)

## Delivery order

1. Floating composer and `F9` toggle, with direct history-preserving chat and no bubbles.
2. OpenRouter push-to-talk transcription into the composer.
3. Composer/history persistence checks across restart and character changes.
4. Optional local Whisper.NET provider after the hosted path is stable.
