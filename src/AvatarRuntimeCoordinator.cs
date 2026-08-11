using System.Collections.Generic;
using UnityEngine;
using LLMUnitySamples;

namespace MateEngine.AIVoiceMod
{
    [DefaultExecutionOrder(31000)]
    public sealed class AvatarRuntimeCoordinator : MonoBehaviour
    {
        private static readonly int IsTalking = Animator.StringToHash("isTalking");
        private GeneratedSpeechLipSyncDriver driver;
        private GameObject avatarRoot;
        private Animator animator;
        private Animator petTalkingPatchedAnimator;
        private RuntimeAnimatorController petTalkingPatchedController;
        private AnimationClip silentPetTalkingClip;
        private UniversalBlendshapes[] blendshapes = new UniversalBlendshapes[0];
        private ChatBot[] chatBots = new ChatBot[0];
        private PetVoiceReactionHandler[] petVoices = new PetVoiceReactionHandler[0];
        private float nextBind;
        private bool speaking;

        public void Initialize(GeneratedSpeechLipSyncDriver value) { driver = value; Rebind(true); }
        public void BeginSpeech() { speaking = true; Rebind(true); driver.Begin(); SuppressLegacy(); }
        public void EndSpeech() { speaking = false; driver.Stop(); ClearMouthInputs(); }

        private void Update()
        {
            if (Time.unscaledTime >= nextBind) Rebind(false);
            SuppressLegacy();
            if (speaking) SuppressPetVoice();
        }
        private void LateUpdate() { SuppressLegacy(); ClearMouthInputs(); }

        private void Rebind(bool force)
        {
            nextBind = Time.unscaledTime + 0.5f;
            chatBots = FindObjectsByType<ChatBot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            petVoices = FindObjectsByType<PetVoiceReactionHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var found = AvatarLocator.FindAvatarRoot();
            if (!force && found == avatarRoot) return;
            avatarRoot = found;
            animator = avatarRoot != null ? avatarRoot.GetComponentInChildren<Animator>(true) : null;
            DisablePetTalkingClip();
            blendshapes = avatarRoot != null ? avatarRoot.GetComponentsInChildren<UniversalBlendshapes>(true) : new UniversalBlendshapes[0];
            if (driver != null) driver.BindAvatar(avatarRoot);
        }

        private void DisablePetTalkingClip()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (animator == petTalkingPatchedAnimator && animator.runtimeAnimatorController == petTalkingPatchedController) return;

            if (silentPetTalkingClip == null)
            {
                silentPetTalkingClip = new AnimationClip { name = "PET_TALKING_DISABLED", hideFlags = HideFlags.HideAndDontSave };
            }

            var controller = new AnimatorOverrideController(animator.runtimeAnimatorController);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            bool changed = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip original = overrides[i].Key;
                AnimationClip current = overrides[i].Value;
                if ((original != null && original.name == "PET_TALKING") || (current != null && current.name == "PET_TALKING"))
                {
                    controller[original] = silentPetTalkingClip;
                    changed = true;
                }
            }
            if (!changed) return;

            animator.runtimeAnimatorController = controller;
            petTalkingPatchedAnimator = animator;
            petTalkingPatchedController = controller;
            Debug.Log("[MateEngineAIVoice] PET_TALKING mouth animation disabled; uLipSync owns A/I/U/E/O.");
        }

        private void SuppressLegacy()
        {
            if (animator != null)
            {
                foreach (var parameter in animator.parameters) if (parameter.nameHash == IsTalking && parameter.type == AnimatorControllerParameterType.Bool) { animator.SetBool(IsTalking, false); break; }
            }
            foreach (var bot in chatBots) if (bot != null && bot.streamAudioSource != null && bot.streamAudioSource.isPlaying) bot.streamAudioSource.Stop();
        }

        private void SuppressPetVoice()
        {
            foreach (var handler in petVoices)
            {
                if (handler == null) continue;
                if (handler.voiceAudioSource != null && handler.voiceAudioSource.isPlaying) handler.voiceAudioSource.Stop();
                if (handler.layeredAudioSource != null && handler.layeredAudioSource.isPlaying) handler.layeredAudioSource.Stop();
            }
        }

        private void ClearMouthInputs()
        {
            foreach (var value in blendshapes) if (value != null) value.A = value.I = value.U = value.E = value.O = 0f;
        }

        private void OnDestroy()
        {
            if (silentPetTalkingClip != null) Destroy(silentPetTalkingClip);
        }
    }
}
