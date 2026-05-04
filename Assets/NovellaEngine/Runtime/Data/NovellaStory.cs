using UnityEngine;

namespace NovellaEngine.Data
{
    [CreateAssetMenu(fileName = "New Story Book", menuName = "Novella Engine/Story Book")]
    public class NovellaStory : ScriptableObject
    {
        [Header("Story Info")]
        public string Title = "My Epic Visual Novel";
        [TextArea(3, 10)]
        public string Description = "A story about choices, consequences, and debugging C# code.";

        [Header("Visuals")]
        public Sprite CoverImage;

        [Header("UI Override")]
        [Tooltip("Card prefab override for the menu (optional, used by StoryLauncher).")]
        public GameObject CustomStoryCardPrefab;

        [Header("Entry Point")]
        [Tooltip("The very first chapter that will be loaded when the player clicks Play")]
        public NovellaTree StartingChapter;

        // ─── Gameplay scene mapping ─────────────────────────────────────────
        // SceneAsset существует только в editor-сборке, поэтому держим пару:
        // ассет (для Studio-UI и инспектора) + строковый путь/имя (используется
        // в рантайме). OnValidate копирует имя из ассета в строку, чтобы юзеру
        // никогда не приходилось править строковое поле руками.
#if UNITY_EDITOR
        [Header("Gameplay scene")]
        [Tooltip("The Unity scene that plays the story. Pick from a dropdown — runtime field below stays in sync.")]
        public UnityEditor.SceneAsset GameSceneAsset;
#endif

        [Tooltip("Runtime scene name — auto-filled from GameSceneAsset above. Don't edit manually.")]
        public string GameSceneName = "";

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Синхронизируем строковое имя сцены с выбранным SceneAsset.
            if (GameSceneAsset != null)
            {
                string n = GameSceneAsset.name;
                if (GameSceneName != n) GameSceneName = n;
            }
        }
#endif
    }
}
