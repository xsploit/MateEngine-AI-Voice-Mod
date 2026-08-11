using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MateEngine.AIVoiceMod
{
    /// <summary>
    /// Drives uLipSync's public MFCC job directly from streamed PCM. MateEngine's
    /// shipped player strips the AudioSettings members used by the optional
    /// uLipSync MonoBehaviour, while the analyzer core remains fully available.
    /// </summary>
    internal sealed class NativeULipSyncAnalyzer : IDisposable
    {
        private readonly uLipSync.Profile profile;
        private readonly Action<uLipSync.LipSyncInfo> callback;
        private readonly Dictionary<string, float> ratios = new Dictionary<string, float>();
        private NativeArray<float> input;
        private NativeArray<float> means;
        private NativeArray<float> standardDeviations;
        private NativeArray<float> phonemes;
        private NativeArray<float> mfcc;
        private NativeArray<float> scores;
        private NativeArray<uLipSync.LipSyncJob.Info> info;
        private int inputRate;
        private int writeIndex;
        private bool disposed;

        public NativeULipSyncAnalyzer(uLipSync.Profile profile, Action<uLipSync.LipSyncInfo> callback)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.callback = callback ?? throw new ArgumentNullException(nameof(callback));
            AllocateFixedBuffers();
        }

        public void AnalyzePcm16(byte[] pcm, int sampleRate)
        {
            if (disposed || pcm == null || pcm.Length < 2) return;
            sampleRate = Mathf.Clamp(sampleRate, 8000, 96000);
            EnsureInputBuffer(sampleRate);

            for (int offset = 0; offset + 1 < pcm.Length; offset += 2)
            {
                short value = (short)(pcm[offset] | pcm[offset + 1] << 8);
                input[writeIndex] = value / 32768f;
                writeIndex = (writeIndex + 1) % input.Length;
            }

            var job = new uLipSync.LipSyncJob
            {
                input = input,
                startIndex = writeIndex,
                outputSampleRate = inputRate,
                targetSampleRate = profile.targetSampleRate,
                melFilterBankChannels = profile.melFilterBankChannels,
                compareMethod = profile.compareMethod,
                means = means,
                standardDeviations = standardDeviations,
                phonemes = phonemes,
                mfcc = mfcc,
                scores = scores,
                info = info,
            };
            job.Execute();
            PublishResult();
        }

        private void AllocateFixedBuffers()
        {
            int mfccCount = Mathf.Max(profile.mfccNum, 1);
            int phonemeCount = Mathf.Max(profile.mfccs.Count, 1);
            means = new NativeArray<float>(mfccCount, Allocator.Persistent);
            standardDeviations = new NativeArray<float>(mfccCount, Allocator.Persistent);
            phonemes = new NativeArray<float>(mfccCount * phonemeCount, Allocator.Persistent);
            mfcc = new NativeArray<float>(mfccCount, Allocator.Persistent);
            scores = new NativeArray<float>(phonemeCount, Allocator.Persistent);
            info = new NativeArray<uLipSync.LipSyncJob.Info>(1, Allocator.Persistent);

            CopyArray(profile.means, means);
            CopyArray(profile.standardDeviation, standardDeviations);
            int destination = 0;
            for (int phonemeIndex = 0; phonemeIndex < profile.mfccs.Count; phonemeIndex++)
            {
                NativeArray<float> source = profile.GetAverages(phonemeIndex);
                for (int coefficient = 0; coefficient < source.Length && destination < phonemes.Length; coefficient++)
                    phonemes[destination++] = source[coefficient];
            }
        }

        private void EnsureInputBuffer(int sampleRate)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(profile.sampleCount * (sampleRate / (float)profile.targetSampleRate)));
            if (input.IsCreated && inputRate == sampleRate && input.Length == count) return;
            if (input.IsCreated) input.Dispose();
            input = new NativeArray<float>(count, Allocator.Persistent);
            inputRate = sampleRate;
            writeIndex = 0;
        }

        private void PublishResult()
        {
            float sum = 0f;
            for (int i = 0; i < scores.Length; i++) sum += scores[i];

            ratios.Clear();
            for (int i = 0; i < scores.Length; i++)
            {
                string phoneme = profile.GetPhoneme(i);
                float ratio = sum > 0f ? scores[i] / sum : 0f;
                if (ratios.TryGetValue(phoneme, out float current)) ratios[phoneme] = current + ratio;
                else ratios[phoneme] = ratio;
            }

            uLipSync.LipSyncJob.Info result = info[0];
            float normalizedVolume = result.volume > 0f
                ? Mathf.Clamp01((Mathf.Log10(result.volume) - uLipSync.Common.DefaultMinVolume) /
                                (uLipSync.Common.DefaultMaxVolume - uLipSync.Common.DefaultMinVolume))
                : 0f;
            callback(new uLipSync.LipSyncInfo
            {
                phoneme = profile.GetPhoneme(result.mainPhonemeIndex),
                volume = normalizedVolume,
                rawVolume = result.volume,
                phonemeRatios = ratios,
            });
        }

        private static void CopyArray(float[] source, NativeArray<float> destination)
        {
            int count = Mathf.Min(source != null ? source.Length : 0, destination.Length);
            for (int i = 0; i < count; i++) destination[i] = source[i];
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (input.IsCreated) input.Dispose();
            if (means.IsCreated) means.Dispose();
            if (standardDeviations.IsCreated) standardDeviations.Dispose();
            if (phonemes.IsCreated) phonemes.Dispose();
            if (mfcc.IsCreated) mfcc.Dispose();
            if (scores.IsCreated) scores.Dispose();
            if (info.IsCreated) info.Dispose();
        }
    }
}
