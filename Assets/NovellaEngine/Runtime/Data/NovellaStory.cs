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

        [Header("Entry Point")]
        [Tooltip("The very first chapter that will be loaded when the player clicks Play.")]
        public NovellaTree StartingChapter;
    }
}