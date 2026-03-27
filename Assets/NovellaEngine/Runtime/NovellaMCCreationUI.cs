using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime.UI
{
    public class NovellaMCCreationUI : MonoBehaviour
    {
        [Header("Characters List")]
        [Tooltip("Массив доступных персонажей для выбора")]
        public List<NovellaCharacter> AvailableCharacters = new List<NovellaCharacter>();

        [Header("UI Containers")]
        [Tooltip("Контейнер, внутри которого будут ездить аватары (Маска)")]
        public Transform AvatarMaskContainer;
        [Tooltip("Контейнер для кнопок управления одеждой (Справа)")]
        public Transform ControlsContainer;

        [Header("Character Switch Buttons")]
        public Button PrevCharBtn;
        public Button NextCharBtn;

        [Header("Prefabs")]
        public GameObject ControlPrefab;

        private int _currentCharIndex = 0;
        private GameObject _activeAvatar;
        private GameObject _outgoingAvatar;

        private float _animProgress = 1f;
        private float _animDir = 1f; // 1 = едем влево, -1 = едем вправо

        private Dictionary<string, int> _currentIndices = new Dictionary<string, int>();
        private Dictionary<string, Image> _previewLayers = new Dictionary<string, Image>();

        void Start()
        {
            if (AvailableCharacters == null || AvailableCharacters.Count == 0) return;

            NovellaVariables.Initialize();

            // Пытаемся загрузить последнего выбранного персонажа
            string savedCharID = NovellaVariables.GetString("MC_SelectedCharacterID");
            int foundIdx = AvailableCharacters.FindIndex(c => c != null && c.CharacterID == savedCharID);
            if (foundIdx >= 0) _currentCharIndex = foundIdx;

            if (PrevCharBtn != null) PrevCharBtn.onClick.AddListener(() => ChangeCharacter(-1));
            if (NextCharBtn != null) NextCharBtn.onClick.AddListener(() => ChangeCharacter(1));

            // Обновляем видимость стрелок, если персонаж всего один
            if (AvailableCharacters.Count <= 1)
            {
                if (PrevCharBtn != null) PrevCharBtn.gameObject.SetActive(false);
                if (NextCharBtn != null) NextCharBtn.gameObject.SetActive(false);
            }

            BuildCurrentCharacter();
        }

        private void BuildCurrentCharacter()
        {
            NovellaVariables.SetString("MC_SelectedCharacterID", AvailableCharacters[_currentCharIndex].CharacterID);

            _activeAvatar = GenerateAvatarObject();
            _activeAvatar.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;

            GenerateWardrobeControls();
        }

        private void ChangeCharacter(int dir)
        {
            if (AvailableCharacters.Count <= 1 || _animProgress < 1f) return;

            _currentCharIndex = (_currentCharIndex + dir + AvailableCharacters.Count) % AvailableCharacters.Count;
            _animDir = dir;

            NovellaVariables.SetString("MC_SelectedCharacterID", AvailableCharacters[_currentCharIndex].CharacterID);

            _outgoingAvatar = _activeAvatar;
            _activeAvatar = GenerateAvatarObject();

            // Ставим нового персонажа за экраном для начала анимации
            _activeAvatar.GetComponent<RectTransform>().anchoredPosition = new Vector2(dir * 1000f, 0);
            _animProgress = 0f;

            GenerateWardrobeControls();
        }

        private void Update()
        {
            // Логика плавной анимации (Lerp)
            if (_animProgress < 1f)
            {
                _animProgress += Time.deltaTime * 4f; // Скорость анимации
                if (_animProgress >= 1f)
                {
                    _animProgress = 1f;
                    if (_outgoingAvatar != null) Destroy(_outgoingAvatar);
                }

                // Плавное смягчение движения
                float ease = Mathf.SmoothStep(0, 1, _animProgress);

                _activeAvatar.GetComponent<RectTransform>().anchoredPosition = new Vector2(Mathf.Lerp(_animDir * 1000f, 0, ease), 0);

                if (_outgoingAvatar != null)
                {
                    _outgoingAvatar.GetComponent<RectTransform>().anchoredPosition = new Vector2(Mathf.Lerp(0, -_animDir * 1000f, ease), 0);
                }
            }
        }

        private GameObject GenerateAvatarObject()
        {
            var charAsset = AvailableCharacters[_currentCharIndex];
            _previewLayers.Clear();

            GameObject avatarRoot = new GameObject("AvatarRoot_" + charAsset.name);
            avatarRoot.transform.SetParent(AvatarMaskContainer, false);
            var rt = avatarRoot.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            for (int i = 0; i < charAsset.BaseLayers.Count; i++)
            {
                var layer = charAsset.BaseLayers[i];

                GameObject layerObj = new GameObject("Layer_" + layer.LayerName);
                layerObj.transform.SetParent(avatarRoot.transform, false);
                Image img = layerObj.AddComponent<Image>();
                img.preserveAspect = true;

                var lRt = layerObj.GetComponent<RectTransform>();
                lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
                lRt.offsetMin = Vector2.zero; lRt.offsetMax = Vector2.zero;

                _previewLayers[layer.LayerName] = img;

                // Загружаем сохраненный индекс одежды
                int savedIdx = NovellaVariables.GetInt("MC_Wardrobe_" + layer.LayerName);
                _currentIndices[layer.LayerName] = savedIdx;

                UpdateLayerPreview(layer.LayerName, charAsset);
            }

            return avatarRoot;
        }

        private void GenerateWardrobeControls()
        {
            foreach (Transform child in ControlsContainer) Destroy(child.gameObject);

            var charAsset = AvailableCharacters[_currentCharIndex];

            for (int i = 0; i < charAsset.BaseLayers.Count; i++)
            {
                var layer = charAsset.BaseLayers[i];

                if (layer.WardrobeOptions != null && layer.WardrobeOptions.Count > 0)
                {
                    if (ControlPrefab != null)
                    {
                        GameObject control = Instantiate(ControlPrefab, ControlsContainer);
                        var txt = control.GetComponentInChildren<TMP_Text>();
                        if (txt != null) txt.text = layer.LayerName;

                        var btns = control.GetComponentsInChildren<Button>();
                        if (btns.Length >= 2)
                        {
                            btns[0].onClick.AddListener(() => ChangeLayerOption(layer.LayerName, -1, charAsset));
                            btns[1].onClick.AddListener(() => ChangeLayerOption(layer.LayerName, 1, charAsset));
                        }
                    }
                }
            }
        }

        private void ChangeLayerOption(string layerName, int dir, NovellaCharacter charAsset)
        {
            var layer = charAsset.BaseLayers.Find(l => l.LayerName == layerName);
            if (layer == null || layer.WardrobeOptions.Count == 0) return;

            int currentIdx = _currentIndices[layerName];
            currentIdx += dir;

            if (currentIdx < 0) currentIdx = layer.WardrobeOptions.Count - 1;
            if (currentIdx >= layer.WardrobeOptions.Count) currentIdx = 0;

            _currentIndices[layerName] = currentIdx;
            NovellaVariables.SetInt("MC_Wardrobe_" + layerName, currentIdx);

            UpdateLayerPreview(layerName, charAsset);
        }

        private void UpdateLayerPreview(string layerName, NovellaCharacter charAsset)
        {
            var layer = charAsset.BaseLayers.Find(l => l.LayerName == layerName);
            if (layer == null) return;

            Sprite targetSpr = layer.DefaultSprite;
            int idx = _currentIndices[layerName];

            if (layer.WardrobeOptions != null && layer.WardrobeOptions.Count > 0 && idx >= 0 && idx < layer.WardrobeOptions.Count)
            {
                targetSpr = layer.WardrobeOptions[idx];
            }

            if (_previewLayers.ContainsKey(layerName))
            {
                _previewLayers[layerName].sprite = targetSpr;
                _previewLayers[layerName].color = targetSpr == null ? Color.clear : Color.white;
            }
        }
    }
}