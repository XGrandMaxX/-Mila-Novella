using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    [Serializable]
    public class CharacterLayer
    {
        public string LayerName = "Base";
        public Sprite DefaultSprite;

        public Vector2 Offset = Vector2.zero;
        public Vector2 Scale = Vector2.one;

        public List<Sprite> WardrobeOptions = new List<Sprite>();
    }

    [Serializable]
    public class CharacterLayerOverride
    {
        public string LayerName;
        public Sprite OverrideSprite;

        public Vector2 Offset = Vector2.zero;
        public Vector2 Scale = Vector2.one;
    }

    [Serializable]
    public struct CharacterEmotion
    {
        public string EmotionName;
        public List<CharacterLayerOverride> LayerOverrides;
    }

    [CreateAssetMenu(fileName = "NewCharacter", menuName = "Novella Engine/Character")]
    public class NovellaCharacter : ScriptableObject
    {
        public string CharacterID;

        [Header("Main Character (Player) Settings")]
        [Tooltip("Отметьте, если это Главный Герой (внешность будет настраиваться игроком)")]
        public bool IsPlayerCharacter = false;

        public List<Sprite> AvailableBaseBodies = new List<Sprite>();

        [Header("Standard Settings")]
        public string DisplayName_EN;
        public string DisplayName_RU;
        public Color ThemeColor = Color.black;

        [Header("Layer System (Paper Doll)")]
        public List<CharacterLayer> BaseLayers = new List<CharacterLayer>();
        public List<CharacterEmotion> Emotions = new List<CharacterEmotion>();

        public Sprite DefaultSprite => BaseLayers.Count > 0 ? BaseLayers[0].DefaultSprite : null;

        [HideInInspector]
        public bool IsFavorite;

        [TextArea(3, 5)]
        public string InternalNotes;

        public const int MAX_ID_LENGTH = 20;
        public const int MAX_NAME_LENGTH = 30;
        public const int MAX_NOTES_LENGTH = 200;

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(CharacterID) && CharacterID.Length > MAX_ID_LENGTH) CharacterID = CharacterID[..MAX_ID_LENGTH];
            if (!string.IsNullOrEmpty(DisplayName_EN) && DisplayName_EN.Length > MAX_NAME_LENGTH) DisplayName_EN = DisplayName_EN[..MAX_NAME_LENGTH];
            if (!string.IsNullOrEmpty(DisplayName_RU) && DisplayName_RU.Length > MAX_NAME_LENGTH) DisplayName_RU = DisplayName_RU[..MAX_NAME_LENGTH];
            if (!string.IsNullOrEmpty(InternalNotes) && InternalNotes.Length > MAX_NOTES_LENGTH) InternalNotes = InternalNotes[..MAX_NOTES_LENGTH];
        }
    }
}