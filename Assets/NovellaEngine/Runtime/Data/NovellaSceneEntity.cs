using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    [ExecuteAlways]
    [RequireComponent(typeof(SortingGroup))]
    public class NovellaSceneEntity : MonoBehaviour
    {
        public string LinkedNodeID;

        private Dictionary<string, SpriteRenderer> _layerRenderers = new Dictionary<string, SpriteRenderer>();
        private SortingGroup _sortingGroup;

        public void Initialize(string nodeID)
        {
            LinkedNodeID = nodeID;
            gameObject.hideFlags = HideFlags.NotEditable;
            _sortingGroup = GetComponent<SortingGroup>();
        }

        public void ApplyAppearance(NovellaCharacter characterAsset, string emotionName)
        {
            if (characterAsset == null) return;

            for (int i = 0; i < characterAsset.BaseLayers.Count; i++)
            {
                var layerDef = characterAsset.BaseLayers[i];
                if (!_layerRenderers.ContainsKey(layerDef.LayerName))
                {
                    Transform existingLayer = transform.Find(layerDef.LayerName);
                    GameObject layerObj = existingLayer != null ? existingLayer.gameObject : new GameObject(layerDef.LayerName);
                    layerObj.transform.SetParent(this.transform, false);

                    var sr = layerObj.GetComponent<SpriteRenderer>();
                    if (sr == null) sr = layerObj.AddComponent<SpriteRenderer>();

                    sr.sortingOrder = i;
                    _layerRenderers[layerDef.LayerName] = sr;
                }
            }

            foreach (var layerDef in characterAsset.BaseLayers)
            {
                if (_layerRenderers.TryGetValue(layerDef.LayerName, out var sr))
                {
                    Sprite targetSprite = layerDef.DefaultSprite;

                    if (characterAsset.IsPlayerCharacter && layerDef.WardrobeOptions != null && layerDef.WardrobeOptions.Count > 0)
                    {
                        int savedIdx = NovellaVariables.GetInt("MC_Wardrobe_" + layerDef.LayerName);

                        if (savedIdx >= 0 && savedIdx < layerDef.WardrobeOptions.Count)
                        {
                            targetSprite = layerDef.WardrobeOptions[savedIdx];
                        }
                    }

                    sr.sprite = targetSprite;
                }
            }

            if (!string.IsNullOrEmpty(emotionName) && emotionName != "Default")
            {
                var emotionData = characterAsset.Emotions.Find(e => e.EmotionName == emotionName);
                if (emotionData.EmotionName == emotionName && emotionData.LayerOverrides != null)
                {
                    foreach (var over in emotionData.LayerOverrides)
                    {
                        if (_layerRenderers.TryGetValue(over.LayerName, out var sr))
                        {
                            if (over.OverrideSprite != null) sr.sprite = over.OverrideSprite;
                        }
                    }
                }
            }
        }

        public void SetSortingOrder(int order)
        {
            if (_sortingGroup == null) _sortingGroup = GetComponent<SortingGroup>();
            if (_sortingGroup != null) _sortingGroup.sortingOrder = order;
        }

        public void SetFlip(bool flipX, bool flipY)
        {
            foreach (var sr in _layerRenderers.Values)
            {
                sr.flipX = flipX;
                sr.flipY = flipY;
            }
        }
    }
}