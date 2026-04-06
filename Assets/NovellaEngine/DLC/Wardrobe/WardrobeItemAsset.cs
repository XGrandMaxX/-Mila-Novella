using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.DLC.Wardrobe
{
    public enum EItemGender
    {
        Unisex,
        Male,
        Female
    }

    [CreateAssetMenu(fileName = "New Wardrobe Item", menuName = "Novella Engine/DLC/Wardrobe Item")]
    public class WardrobeItemAsset : ScriptableObject
    {
        public string ItemID;
        public string DisplayName;

        [Header("Equip Settings")]
        public EItemGender Gender = EItemGender.Unisex;

        public ECharacterLayer BaseLayer = ECharacterLayer.Clothes;

        [Tooltip("Используется только если BaseLayer установлен в 'Extra'")]
        public string CustomLayerName = "";

        [Header("Visuals")]
        public Sprite ItemSprite;
        public Sprite UIIcon;

        [HideInInspector]
        public bool IsFavorite;

        public string GetLayerName()
        {
            return BaseLayer == ECharacterLayer.Extra ? CustomLayerName : BaseLayer.ToString();
        }
    }
}