# Architecture

## Native desktop runtime

MateEngine is a Unity Mono desktop player. The mod runs entirely in its process as a .NET Framework 4.7.2 assembly; users do not need Node, JavaScript, or a companion server.

The runtime uses:

- `RawHttpClient`, a small TLS HTTP/SSE client compatible with MateEngine's shipped Mono surface, for catalogs and OpenAI-compatible streaming APIs;
- `FishSocketConnection`, a managed RFC 6455 client, for Fish Audio realtime MessagePack speech;
- `FishTimestampSession` for Fish's timestamped HTTP/SSE transport;
- `UnityPcmSpeechPlayer`, backed by NAudio WASAPI with WaveOut fallback, for PCM playback;
- uLipSync analysis and a late VRM expression writer for mouth movement.

## Chat compatibility hook

MateEngine's existing `LLMCharacter` remains the chat and history owner. A loopback server implements the llama.cpp-shaped endpoints that `LLMCharacter` already calls and translates only the remote completion request.

That is why llama.cpp names appear in this project. Remote mode does not launch llama.cpp or load a GGUF model; it exposes the compatibility surface MateEngine already expects and sends inference directly to OpenRouter or Vercel AI Gateway.

## Provider flow

The active request selects OpenRouter or Vercel from saved settings. `ModelCatalogClient` fetches available models and Vercel endpoints. `GatewayClient` applies the selected automatic or pinned-provider route and converts SSE deltas into MateEngine's loopback response stream.

## Speech flow

Assistant deltas enter `SpeechTextChunker`. The selected pacing mode controls whether text is sent as live, stable, early, or sentence chunks.

- WebSocket mode sends MessagePack commands and receives PCM frames as they arrive.
- Timestamp SSE mode submits the completed response and decodes the returned audio stream.

PCM is queued to `UnityPcmSpeechPlayer`. NAudio first attempts WASAPI and falls back to WaveOut if the endpoint rejects the source format.

## Face ownership

The same PCM used for playback drives the lip-sync coordinator. uLipSync supplies phoneme analysis while the mod's final VRM0/VRM1 writer applies mouth expressions after MateEngine's animation writers. During generated speech, the coordinator suppresses only `PET_TALKING`, fake vowel input, and competing mouth weights. Blink, gaze, body animation, and unrelated expressions remain active.

## UI and persistence

The settings interface is a MateSDK `.me` prefab. It is a new controller/menu, not the old CustomLLMAPI menu. The four primitive prefabs used by the builder originate from MateEngine and are covered by its license and attribution requirements.

Settings are atomically written to Unity's persistent-data directory as `MateEngineAIVoiceSettings.json`, with a replacement backup. BYOK values are local plaintext JSON and are never copied by the installer or release tooling.
