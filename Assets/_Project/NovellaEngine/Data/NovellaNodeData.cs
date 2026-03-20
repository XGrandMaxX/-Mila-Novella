using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    public enum ENodeType { Dialogue, Branch, Event, End, Character }
    public enum ECharacterPlane { BackSlot1, BackSlot2, BackSlot3, Front }

    [Serializable]
    public class CharacterInDialogue
    {
        public NovellaCharacter CharacterAsset;
        public ECharacterPlane Plane = ECharacterPlane.BackSlot1;
        public float Scale = 1.0f;
        public string Emotion = "Default";
        public float PosX = 0f;
        public float PosY = 0f;
    }

    [Serializable]
    public class NovellaChoice
    {
        public string PortID;
        public LocalizedString LocalizedText = new LocalizedString();
        public string NextNodeID;

        public NovellaChoice() { PortID = "Choice_" + Guid.NewGuid().ToString().Substring(0, 5); }
    }

    [Serializable]
    public class NovellaNodeData
    {
        public string NodeID;
        public string NodeTitle;
        public ENodeType NodeType;
        public Vector2 GraphPosition;

        public Color NodeCustomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public bool IsPinned;

        public NovellaCharacter CharacterAsset;
        public NovellaCharacter Speaker;
        public string Mood;
        public int FontSize = 24;

        public LocalizedString LocalizedPhrase = new LocalizedString();
        public string NextNodeID;

        public List<CharacterInDialogue> ActiveCharacters = new List<CharacterInDialogue>();
        public List<NovellaChoice> Choices = new List<NovellaChoice>();
        public bool UnlockChoiceLimit = false;
    }
}