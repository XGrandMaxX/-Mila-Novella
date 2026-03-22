using UnityEngine;

namespace NovellaEngine.Runtime
{
    public class NovellaCustomUI : MonoBehaviour
    {
        [Tooltip("Является ли этот префаб рамкой диалога?")]
        public bool IsDialogueFrame = false;

        [Tooltip("Если это рамка диалога, движок будет искать здесь текст.")]
        public TMPro.TMP_Text OverrideDialogueText;

        [Tooltip("Если это рамка диалога, движок будет искать здесь имя.")]
        public TMPro.TMP_Text OverrideSpeakerName;
    }
}