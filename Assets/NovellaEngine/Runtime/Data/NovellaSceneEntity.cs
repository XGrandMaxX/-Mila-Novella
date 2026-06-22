using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using System.Collections.Generic;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    // Сценный entity персонажа. Поддерживает два режима в зависимости от
    // того, где висит CharactersContainer:
    //
    //   • WORLD-mode (Transform parent): дочерние SpriteRenderer'ы по
    //     одному на каждый BaseLayer, сортировка через SortingGroup.
    //     Поддерживает wardrobe (для PlayerCharacter) и многослойных
    //     персонажей.
    //
    //   • UI-mode (RectTransform parent в Canvas): один Image на самом
    //     GameObject — выглядит в Hierarchy как обычная картинка, а не
    //     папка. Поддерживается Кузницей UI (drag/scale/выделение).
    //     Слои и wardrobe в UI-режиме упрощены до одного итогового
    //     спрайта (берётся первый BaseLayer + эмоция override).
    //
    // Режим определяется по типу transform на момент Initialize().
    [ExecuteAlways]
    public class NovellaSceneEntity : MonoBehaviour
    {
        public string LinkedNodeID;

        // ─── World-mode state ───
        private Dictionary<string, SpriteRenderer> _layerRenderers = new Dictionary<string, SpriteRenderer>();
        private SortingGroup _sortingGroup;

        // ─── UI-mode state ───
        private Image _uiImage;
        private RectTransform _uiRT;

        private bool _isUI;

        public void Initialize(string nodeID)
        {
            LinkedNodeID = nodeID;
            _isUI = transform is RectTransform;

            if (_isUI)
            {
                _uiRT = (RectTransform)transform;
                // Image на самом GameObject — Кузница UI работает с Image
                // как со «своим» элементом (можно тащить, скейлить, выделять).
                _uiImage = GetComponent<Image>();
                if (_uiImage == null) _uiImage = gameObject.AddComponent<Image>();
                _uiImage.raycastTarget = true; // для пика мышью в Кузнице
                _uiImage.preserveAspect = true;
            }
            else
            {
                // World-mode: SortingGroup нужен для управления порядком
                // отрисовки дочерних SpriteRenderer'ов.
                _sortingGroup = GetComponent<SortingGroup>();
                if (_sortingGroup == null) _sortingGroup = gameObject.AddComponent<SortingGroup>();
            }
        }

        public void ApplyAppearance(NovellaCharacter characterAsset, string emotionName)
        {
            if (characterAsset == null) return;
            if (_isUI) ApplyAppearanceUI(characterAsset, emotionName);
            else      ApplyAppearanceWorld(characterAsset, emotionName);
        }

        // ─── UI-mode: один Image, итоговый спрайт ───
        // BaseLayers и wardrobe не разворачиваются — берём первый
        // ненулевой DefaultSprite и подменяем его эмоцией если есть.
        // Достаточно для большинства персонажей; сложные многослойные
        // случаи лучше делать в World-mode (без Canvas).
        private void ApplyAppearanceUI(NovellaCharacter ch, string emotionName)
        {
            if (_uiImage == null) return;

            Sprite resolved = null;
            if (ch.BaseLayers != null)
            {
                for (int i = 0; i < ch.BaseLayers.Count; i++)
                {
                    if (ch.BaseLayers[i].DefaultSprite != null)
                    { resolved = ch.BaseLayers[i].DefaultSprite; break; }
                }
            }
            // Эмоция переопределяет первый non-null layer.
            if (!string.IsNullOrEmpty(emotionName) && emotionName != "Default"
                && ch.Emotions != null)
            {
                var emo = ch.Emotions.Find(e => e.EmotionName == emotionName);
                if (emo.EmotionName == emotionName && emo.LayerOverrides != null)
                {
                    foreach (var ov in emo.LayerOverrides)
                    {
                        if (ov.OverrideSprite != null)
                        { resolved = ov.OverrideSprite; break; }
                    }
                }
            }

            _uiImage.sprite = resolved;
            // sizeDelta = реальный размер спрайта в пикселях, чтобы
            // Кузница UI могла обводить рамкой и масштабировать.
            if (resolved != null)
                _uiRT.sizeDelta = new Vector2(resolved.rect.width, resolved.rect.height);
        }

        // ─── World-mode: оригинальная многослойная логика ───
        private void ApplyAppearanceWorld(NovellaCharacter characterAsset, string emotionName)
        {
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
            if (_isUI)
            {
                // В UI порядок отрисовки = sibling index. Не дёргаем
                // SetSiblingIndex автоматически — это сломало бы ручное
                // упорядочивание в Кузнице. Игнорируем silently.
                return;
            }
            if (_sortingGroup == null) _sortingGroup = GetComponent<SortingGroup>();
            if (_sortingGroup != null) _sortingGroup.sortingOrder = order;
        }

        public void SetFlip(bool flipX, bool flipY)
        {
            if (_isUI)
            {
                if (_uiRT == null) return;
                var s = _uiRT.localScale;
                s.x = flipX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
                s.y = flipY ? -Mathf.Abs(s.y) : Mathf.Abs(s.y);
                _uiRT.localScale = s;
                return;
            }
            foreach (var sr in _layerRenderers.Values)
            {
                sr.flipX = flipX;
                sr.flipY = flipY;
            }
        }
    }
}
