using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using UnityEngine;

namespace MateEngine.AIVoiceMod
{
    [DisallowMultipleComponent]
    public sealed class UnityPcmSpeechPlayer : MonoBehaviour
    {
        private readonly Queue<byte[]> lipChunks = new Queue<byte[]>();
        private readonly object gate = new object();
        private byte[] activeLipChunk;
        private int activeLipOffset;
        private long lipSamplesFed;
        private float playbackStartTime;
        private int sampleRate = 24000;
        private bool inputFinished, playbackStarted;
        private bool receivedAudio;
        private float emptySince = -1f;
        private float playbackVolume = 1f;
        private BufferedWaveProvider buffer;
        private IWavePlayer output;
        private MediaFoundationResampler resampler;
        private MMDeviceEnumerator deviceEnumerator;
        private MMDevice outputDevice;
        private GeneratedSpeechLipSyncDriver driver;
        private AvatarRuntimeCoordinator coordinator;

        public void Initialize(ModSettings settings)
        {
            driver = GetComponent<GeneratedSpeechLipSyncDriver>() ?? gameObject.AddComponent<GeneratedSpeechLipSyncDriver>();
            driver.Apply(settings);
            driver.InitializeAnalyzer();
            coordinator = GetComponent<AvatarRuntimeCoordinator>() ?? gameObject.AddComponent<AvatarRuntimeCoordinator>();
            coordinator.Initialize(driver);
            Configure(settings.fishSampleRate, settings.ttsVolume);
        }

        public void Apply(ModSettings settings) { driver.Apply(settings); Configure(settings.fishSampleRate, settings.ttsVolume); }
        public void Configure(int rate, float volume)
        {
            sampleRate = Mathf.Clamp(rate, 16000, 48000);
            playbackVolume = Mathf.Clamp(volume, 0f, 2f);
            if (output != null) output.Volume = playbackVolume;
        }

        public void BeginInput()
        {
            StopPlayback();
            lock (gate)
            {
                lipChunks.Clear();
                activeLipChunk = null;
                activeLipOffset = 0;
            }
            lipSamplesFed = 0;
            playbackStartTime = -1f;
            buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1));
            buffer.BufferDuration = TimeSpan.FromSeconds(120);
            buffer.DiscardOnBufferOverflow = true;
            buffer.ReadFully = true;
            try
            {
                output = new WasapiOut(AudioClientShareMode.Shared, false, 50);
                output.Init(buffer);
                output.Volume = playbackVolume;
            }
            catch (COMException ex)
            {
                Debug.LogWarning("[MateEngineAIVoice] Direct WASAPI format rejected (0x" + ex.HResult.ToString("X8") + "); retrying with the Windows mix format.");
                try { if (output != null) output.Dispose(); } catch { }
                output = null;
                deviceEnumerator = new MMDeviceEnumerator();
                outputDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var mixFormat = outputDevice.AudioClient.MixFormat;
                resampler = new MediaFoundationResampler(buffer, mixFormat) { ResamplerQuality = 60 };
                output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 75);
                try { output.Init(resampler); output.Volume = playbackVolume; }
                catch (COMException fallback)
                {
                    Debug.LogWarning("[MateEngineAIVoice] Windows mix-format WASAPI failed (0x" + fallback.HResult.ToString("X8") + ", " + mixFormat + "); using WaveOut.");
                    try { if (output != null) output.Dispose(); } catch { }
                    try { if (resampler != null) resampler.Dispose(); } catch { }
                    try { if (outputDevice != null) outputDevice.Dispose(); } catch { }
                    try { if (deviceEnumerator != null) deviceEnumerator.Dispose(); } catch { }
                    resampler = null;
                    outputDevice = null;
                    deviceEnumerator = null;
                    var waveOut = new WaveOutEvent { DesiredLatency = 75, NumberOfBuffers = 3 };
                    waveOut.Init(buffer);
                    waveOut.Volume = playbackVolume;
                    output = waveOut;
                }
            }
            inputFinished = false;
            playbackStarted = false;
            receivedAudio = false;
            emptySince = -1f;
        }

        public void EnqueuePcm(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2 || buffer == null) return;
            if (!receivedAudio)
            {
                receivedAudio = true;
                Debug.Log("[MateEngineAIVoice] Fish PCM received (" + bytes.Length + " bytes in first frame).");
            }
            buffer.AddSamples(bytes, 0, bytes.Length);
            var copy = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
            lock (gate) lipChunks.Enqueue(copy);
        }

        public void EndInput() { inputFinished = true; }

        private void Update()
        {
            int count = buffer != null ? buffer.BufferedBytes : 0;
            if (!playbackStarted && count >= sampleRate * 2 / 10)
            {
                playbackStarted = true;
                output.Play();
                playbackStartTime = UnityEngine.Time.unscaledTime;
                coordinator.BeginSpeech();
                Debug.Log("[MateEngineAIVoice] Speech playback and lip sync started.");
            }
            if (playbackStarted) FeedLipSyncAtPlaybackRate();
            if (!inputFinished || !playbackStarted || count > 0) { emptySince = -1f; return; }
            if (emptySince < 0f) emptySince = UnityEngine.Time.unscaledTime;
            if (UnityEngine.Time.unscaledTime - emptySince >= 0.15f) StopPlayback();
        }

        private void FeedLipSyncAtPlaybackRate()
        {
            if (driver == null || playbackStartTime < 0f) return;
            long targetSamples = (long)((UnityEngine.Time.unscaledTime - playbackStartTime) * sampleRate);
            int due = (int)Math.Min(Math.Max(targetSamples - lipSamplesFed, 0L), sampleRate / 10);
            if (due <= 0) return;

            var pcm = new byte[due * 2];
            int written = 0;
            lock (gate)
            {
                while (written < pcm.Length)
                {
                    if (activeLipChunk == null || activeLipOffset >= activeLipChunk.Length)
                    {
                        if (lipChunks.Count == 0) break;
                        activeLipChunk = lipChunks.Dequeue();
                        activeLipOffset = 0;
                    }

                    int copy = Math.Min(pcm.Length - written, activeLipChunk.Length - activeLipOffset);
                    copy -= copy % 2;
                    if (copy <= 0) break;
                    Buffer.BlockCopy(activeLipChunk, activeLipOffset, pcm, written, copy);
                    activeLipOffset += copy;
                    written += copy;
                }
            }

            if (written <= 0) return;
            if (written != pcm.Length) Array.Resize(ref pcm, written);
            lipSamplesFed += written / 2;
            driver.FeedPcm16(pcm, sampleRate);
        }

        private void StopPlayback()
        {
            try { if (output != null) output.Stop(); } catch { }
            try { if (output != null) output.Dispose(); } catch { }
            try { if (resampler != null) resampler.Dispose(); } catch { }
            try { if (outputDevice != null) outputDevice.Dispose(); } catch { }
            try { if (deviceEnumerator != null) deviceEnumerator.Dispose(); } catch { }
            output = null;
            resampler = null;
            outputDevice = null;
            deviceEnumerator = null;
            buffer = null;
            if (playbackStarted && coordinator != null) coordinator.EndSpeech();
            playbackStarted = false;
            playbackStartTime = -1f;
            inputFinished = true;
        }

        private void OnDestroy() { StopPlayback(); }
    }
}
