using UnityEngine;

namespace MateEngine.AIVoiceMod
{
    public static class AvatarLocator
    {
        public static GameObject FindAvatarRoot()
        {
            var receivers = Object.FindObjectsByType<AvatarAnimatorReceiver>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var receiver in receivers)
                if (receiver != null && receiver.isActiveAndEnabled && receiver.avatarAnimator != null && receiver.avatarAnimator.gameObject.activeInHierarchy)
                    return receiver.gameObject;
            var custom = GameObject.Find("CustomVRM(Clone)");
            if (custom != null) return custom;
            return GameObject.Find("VRMModel");
        }
    }
}

