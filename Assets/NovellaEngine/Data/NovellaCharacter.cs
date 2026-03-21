using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    [Serializable]
    public struct CharacterEmotion
    {
        public string EmotionName;
        public Sprite EmotionSprite;
    }

    [CreateAssetMenu(fileName = "NewCharacter", menuName = "Novella Engine/Character")]
    public class NovellaCharacter : ScriptableObject
    {
        public string CharacterID;
        public string DisplayName_EN;
        public string DisplayName_RU;
        public Color ThemeColor = Color.black;

        public Sprite DefaultSprite;
        public List<CharacterEmotion> Emotions = new List<CharacterEmotion>();

        [HideInInspector]
        public bool IsFavorite;

        [TextArea(3, 5)]
        public string InternalNotes;

        public const int MAX_ID_LENGTH = 20;
        public const int MAX_NAME_LENGTH = 30;
        public const int MAX_NOTES_LENGTH = 200;

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(CharacterID) && CharacterID.Length > MAX_ID_LENGTH)
                CharacterID = CharacterID[..MAX_ID_LENGTH];

            if (!string.IsNullOrEmpty(DisplayName_EN) && DisplayName_EN.Length > MAX_NAME_LENGTH)
                DisplayName_EN = DisplayName_EN[..MAX_NAME_LENGTH];

            if (!string.IsNullOrEmpty(DisplayName_RU) && DisplayName_RU.Length > MAX_NAME_LENGTH)
                DisplayName_RU = DisplayName_RU[..MAX_NAME_LENGTH];

            if (!string.IsNullOrEmpty(InternalNotes) && InternalNotes.Length > MAX_NOTES_LENGTH)
                InternalNotes = InternalNotes[..MAX_NOTES_LENGTH];
        }
    }
}