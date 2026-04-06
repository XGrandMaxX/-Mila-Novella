using System;
using System.Collections.Generic;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.DLC.Wardrobe
{
    [Serializable]
    public class WardrobeItemChoice
    {
        public string ItemName = "New Item";
        public Sprite ItemSprite;
        public string TargetLayer = "Clothes";
    }

    [Serializable]
    [NovellaDLCNode("Character Wardrobe", "👗 Wardrobe", "#E040FB", "Opens the wardrobe UI. The player can change clothes, or you can give them a forced choice (Gift Mode).", "1.0")]
    public class WardrobeNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.CustomDLC;

        [NovellaDLCOutput("Next ➡")]
        public string NextNodeID;

        // ИСПРАВЛЕНО: Флаг для использования Главного Героя (по умолчанию включен)
        public bool UseMainCharacter = true;
        public NovellaCharacter TargetCharacter;

        public bool IsGiftMode = false;

        public List<WardrobeItemChoice> ItemsToChoose = new List<WardrobeItemChoice>();
    }
}