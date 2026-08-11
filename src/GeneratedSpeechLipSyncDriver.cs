using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UniVRM10;
using VRM;

namespace MateEngine.AIVoiceMod
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class GeneratedSpeechLipSyncDriver : MonoBehaviour
    {
        public enum ShapingMode { Hybrid, Direct }
        public ShapingMode mode = ShapingMode.Hybrid;
        [Range(0f, 0.95f)] public float smoothing = 0.44f;
        [Range(0f, 2f)] public float gain = 1f;
        [Range(0f, 2f)] public float volumeInfluence = 1f;
        public float minVolume = -2.5f;
        public float maxVolume = -1.5f;

        private const float PhonemeGain = 0.39f, VisemeDeadzone = 0.045f, CloseGateStart = 0.035f, CloseGateEnd = 0.16f, CloseSmoothingRatio = 0.36f, DirectGain = 0.9f;
        private static readonly string[] Phonemes = { "A", "I", "U", "E", "O" };
        private static readonly BlendShapeKey[] Vrm0Keys =
        {
            BlendShapeKey.CreateFromPreset(BlendShapePreset.A), BlendShapeKey.CreateFromPreset(BlendShapePreset.I),
            BlendShapeKey.CreateFromPreset(BlendShapePreset.U), BlendShapeKey.CreateFromPreset(BlendShapePreset.E),
            BlendShapeKey.CreateFromPreset(BlendShapePreset.O)
        };
        private static readonly ExpressionKey[] Vrm1Keys = { ExpressionKey.Aa, ExpressionKey.Ih, ExpressionKey.Ou, ExpressionKey.Ee, ExpressionKey.Oh };
        private readonly float[] targets = new float[5], ratios = new float[5], weights = new float[5];
        private VRMBlendShapeProxy vrm0;
        private Vrm10Instance vrm1;
        private NativeULipSyncAnalyzer analyzer;
        private uLipSync.Profile analyzerProfile;
        private bool hasSignal, speechActive;
        private float nextDiagnosticTime;
        private float peakAnalyzedWeight;

        public bool AnalyzerReady => analyzer != null && analyzerProfile != null;

        public void InitializeAnalyzer()
        {
            if (AnalyzerReady) return;

            try
            {
                analyzerProfile = uLipSync.Profile.Create();
                string profilePath = MaterializeProfile();
                if (!analyzerProfile.Import(profilePath)) throw new InvalidOperationException("uLipSync rejected the embedded speech profile.");
                analyzer = new NativeULipSyncAnalyzer(analyzerProfile, OnLipSyncUpdate);

                Debug.Log("[MateEngineAIVoice] Native uLipSync ready (" + analyzerProfile.mfccs.Count +
                          " calibrated samples, " + string.Join(",", analyzerProfile.GetPhonemeNames()) + ").");
            }
            catch (Exception ex)
            {
                Debug.LogError("[MateEngineAIVoice] Native uLipSync initialization failed: " + ex);
                DestroyAnalyzer();
            }
        }

        private static string MaterializeProfile()
        {
            const string resourceName = "MateEngine.AIVoiceMod.Resources.lipsync-profile.json";
            string directory = Path.Combine(Application.persistentDataPath, "MateEngineAIVoice");
            string path = Path.Combine(directory, "lipsync-profile.json");
            Directory.CreateDirectory(directory);

            byte[] embedded;
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new FileNotFoundException("Embedded uLipSync profile is missing.", resourceName);
                using (var memory = new MemoryStream())
                {
                    input.CopyTo(memory);
                    embedded = memory.ToArray();
                }
            }

            bool write = !File.Exists(path);
            if (!write)
            {
                byte[] existing = File.ReadAllBytes(path);
                if (existing.Length != embedded.Length) write = true;
                else
                {
                    for (int i = 0; i < embedded.Length; i++)
                    {
                        if (existing[i] == embedded[i]) continue;
                        write = true;
                        break;
                    }
                }
            }
            if (write) File.WriteAllBytes(path, embedded);
            return path;
        }

        public void BindAvatar(GameObject avatarRoot)
        {
            ResetMouth();
            vrm0 = avatarRoot != null ? avatarRoot.GetComponentInChildren<VRMBlendShapeProxy>(true) : null;
            vrm1 = avatarRoot != null ? avatarRoot.GetComponentInChildren<Vrm10Instance>(true) : null;
        }

        public void Apply(ModSettings settings)
        {
            mode = string.Equals(settings.lipSyncMode, "direct", System.StringComparison.OrdinalIgnoreCase) ? ShapingMode.Direct : ShapingMode.Hybrid;
            smoothing = Mathf.Clamp(settings.lipSyncSmoothing, 0f, 0.9f);
            gain = Mathf.Clamp(settings.lipSyncGain, 0.1f, 2f);
            volumeInfluence = Mathf.Clamp(settings.lipSyncVolumeInfluence, 0f, 2f);
        }

        public void OnLipSyncUpdate(uLipSync.LipSyncInfo info)
        {
            float amplitude = info.rawVolume > 0f ? Mathf.Clamp01((Mathf.Log10(info.rawVolume) - minVolume) / Mathf.Max(maxVolume - minVolume, 0.0001f)) : 0f;
            float total = 0f;
            for (int i = 0; i < ratios.Length; i++)
            {
                float ratio = 0f;
                if (info.phonemeRatios != null) info.phonemeRatios.TryGetValue(Phonemes[i], out ratio);
                else if (info.phoneme == Phonemes[i]) ratio = 1f;
                ratios[i] = Mathf.Max(0f, ratio); total += ratios[i];
            }
            for (int i = 0; i < ratios.Length; i++) peakAnalyzedWeight = Mathf.Max(peakAnalyzedWeight, ratios[i] * amplitude);
            UpdateTargets(amplitude, total);
        }

        public void FeedPcm16(byte[] pcm, int sampleRate)
        {
            if (pcm == null || pcm.Length < 2) return;
            if (!AnalyzerReady) InitializeAnalyzer();
            if (!AnalyzerReady) return;

            analyzer.AnalyzePcm16(pcm, sampleRate);
        }

        private void UpdateTargets(float amplitude, float total)
        {
            if (mode == ShapingMode.Direct)
            {
                float loudness = Volume(amplitude), scale = DirectGain * gain;
                for (int i = 0; i < targets.Length; i++) targets[i] = Mathf.Clamp01((total > 0f ? ratios[i] / total : 0f) * loudness * scale);
            }
            else
            {
                float loudness = Volume(amplitude), inv = total > 0.00001f ? 1f / total : 0f;
                float a = ratios[0] * inv, ii = ratios[1] * inv, u = ratios[2] * inv, e = ratios[3] * inv, o = ratios[4] * inv;
                targets[0] = a * loudness * 1.45f * PhonemeGain + loudness * 0.1f;
                targets[1] = ii * loudness * 1.2f * PhonemeGain;
                targets[2] = u * loudness * 1.15f * PhonemeGain;
                targets[3] = e * loudness * 1.25f * PhonemeGain;
                float rounded = Mathf.Pow(Mathf.Clamp01(o * loudness), 1.2f);
                targets[4] = rounded * 0.34f * PhonemeGain; targets[2] += rounded * 0.24f; targets[0] *= 1f - rounded * 0.16f;
                targets[0] = Mathf.Min(targets[0], 0.95f); targets[1] = Mathf.Min(targets[1], 0.72f); targets[2] = Mathf.Min(targets[2], 0.70f); targets[3] = Mathf.Min(targets[3], 0.75f); targets[4] = Mathf.Min(targets[4], 0.36f);
                if (targets[4] > 0f)
                {
                    targets[4] = Mathf.Min(Mathf.Pow(Mathf.Clamp01(targets[4]), 1.2f), 0.3f + loudness * 0.14f);
                    targets[2] = Mathf.Min(targets[2] + targets[4] * 0.34f, 0.74f); targets[0] *= 1f - targets[4] * 0.18f; targets[3] *= 1f - targets[4] * 0.45f;
                    float roundTotal = targets[4] + targets[2], cap = 0.62f + loudness * 0.12f;
                    if (roundTotal > cap) { float scale = cap / roundTotal; targets[4] *= scale; targets[2] *= scale; }
                }
                float master = Mathf.Clamp01((amplitude - CloseGateStart) / (CloseGateEnd - CloseGateStart)) * gain;
                for (int i = 0; i < targets.Length; i++) targets[i] = Mathf.Clamp01(targets[i] * master);
            }
            hasSignal = amplitude > 0.01f || total > 0.02f;
        }

        public void Begin() { speechActive = true; }
        public void Stop()
        {
            speechActive = false;
            hasSignal = false;
            peakAnalyzedWeight = 0f;
            for (int i = 0; i < targets.Length; i++) targets[i] = 0f;
        }

        private void LateUpdate()
        {
            bool moving = false;
            for (int i = 0; i < weights.Length; i++)
            {
                float applied = targets[i] < weights[i] ? smoothing * CloseSmoothingRatio : smoothing;
                weights[i] += (targets[i] - weights[i]) * (1f - applied);
                if (mode == ShapingMode.Hybrid) weights[i] = weights[i] <= VisemeDeadzone ? 0f : Mathf.Clamp01((weights[i] - VisemeDeadzone) / (1f - VisemeDeadzone));
                weights[i] = Mathf.Clamp01(weights[i]); moving |= weights[i] > 0.0001f || targets[i] > 0f;
            }
            if (speechActive || moving || hasSignal)
            {
                ApplyMouth();
                LogDiagnostics();
            }
        }

        private void ApplyMouth()
        {
            if (vrm0 != null) { for (int i = 0; i < weights.Length; i++) vrm0.AccumulateValue(Vrm0Keys[i], weights[i]); vrm0.Apply(); }
            if (vrm1 != null && vrm1.Runtime != null)
            {
                for (int i = 0; i < weights.Length; i++) vrm1.Runtime.Expression.SetWeight(Vrm1Keys[i], weights[i]);
                // Vrm10Instance normally processes at order 11000, before this final mouth pass.
                // Apply lip sync after animation, then commit the final VRM expression weights.
                vrm1.Runtime.Process();
            }
        }

        private void LogDiagnostics()
        {
            if (!speechActive || Time.unscaledTime < nextDiagnosticTime) return;
            nextDiagnosticTime = Time.unscaledTime + 0.75f;
            float requested = 0f;
            float applied = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                requested = Mathf.Max(requested, weights[i]);
                if (vrm0 != null) applied = Mathf.Max(applied, vrm0.GetValue(Vrm0Keys[i]));
                if (vrm1 != null && vrm1.Runtime != null && vrm1.Runtime.Expression.ActualWeights.TryGetValue(Vrm1Keys[i], out float value)) applied = Mathf.Max(applied, value);
            }
            Debug.Log("[MateEngineAIVoice] uLipSync mouth frame avatar=" + (vrm1 != null ? "VRM1" : vrm0 != null ? "VRM0" : "unbound") +
                      " analyzed=" + peakAnalyzedWeight.ToString("F3") + " requested=" + requested.ToString("F3") +
                      " applied=" + applied.ToString("F3"));
            peakAnalyzedWeight = 0f;
        }

        private void ResetMouth() { for (int i = 0; i < weights.Length; i++) targets[i] = weights[i] = 0f; ApplyMouth(); }
        private float Volume(float value) { value = Mathf.Clamp01(value); return value <= 0f ? 0f : Mathf.Pow(value, Mathf.Max(volumeInfluence, 0.05f)); }
        private void OnDisable() { ResetMouth(); }

        private void OnDestroy() { DestroyAnalyzer(); }

        private void DestroyAnalyzer()
        {
            if (analyzer != null) analyzer.Dispose();
            if (analyzerProfile != null) Destroy(analyzerProfile);
            analyzer = null;
            analyzerProfile = null;
        }
    }
}
