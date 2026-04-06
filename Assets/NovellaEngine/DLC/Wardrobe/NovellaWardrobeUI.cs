using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;
using NovellaEngine.DLC.Wardrobe;

namespace NovellaEngine.Runtime.UI
{
    public class NovellaWardrobeUI : MonoBehaviour
    {
        [Header("UI References")]
        public Transform AvatarMaskContainer;
        public TMP_Text CharacterNameText;
        public Button CloseButton;
        public Transform CategoriesPanel; // Оставлено как удобный родитель по умолчанию
        public Transform ItemsGrid;

        [Header("Prefabs")]
        public GameObject ItemSlotPrefab;
        // УДАЛЕНО: TabPrefab больше не нужен!

        private NovellaPlayer _player;
        private WardrobeNodeData _currentNode;
        private NovellaCharacter _targetCharacter;

        private List<WardrobeItemAsset> _allItems = new List<WardrobeItemAsset>();
        private Dictionary<string, Image> _avatarLayers = new Dictionary<string, Image>();
        private string _currentCategory = "Clothes";

        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private TMP_Text _warningText;

        private List<GameObject> _spawnedTabs = new List<GameObject>();

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<GraphicRaycaster>();
        }

        private void OnEnable() { NovellaPlayer.OnExecuteDLCNode += HandleDLCNode; }
        private void OnDisable() { NovellaPlayer.OnExecuteDLCNode -= HandleDLCNode; }

        private void Start()
        {
            if (CloseButton != null) CloseButton.onClick.AddListener(CloseWardrobe);
            if (_canvas != null) _canvas.enabled = false;
            if (_raycaster != null) _raycaster.enabled = false;
        }

        private void LoadAllItems()
        {
            _allItems.Clear();
            var items = Resources.LoadAll<WardrobeItemAsset>("Wardrobe");
            _allItems.AddRange(items);
        }

        private void HandleDLCNode(NovellaPlayer player, NovellaNodeBase node)
        {
            if (node is WardrobeNodeData wardrobeNode)
            {
                _player = player;
                _currentNode = wardrobeNode;

                if (_player != null && _player.DialoguePanel != null)
                    _player.DialoguePanel.SetActive(false);

                LoadAllItems();
                OpenWardrobe();
            }
        }

        public void OpenWardrobe()
        {
            if (_canvas != null) _canvas.enabled = true;
            if (_raycaster != null) _raycaster.enabled = true;

            if (_currentNode.UseMainCharacter)
            {
                string mcId = PlayerPrefs.GetString("MC_CharacterID", "");
                _targetCharacter = Resources.Load<NovellaCharacter>("Characters/" + mcId);

                if (_targetCharacter == null)
                {
                    _targetCharacter = Resources.LoadAll<NovellaCharacter>("Characters").FirstOrDefault(c => c.IsPlayerCharacter);
                    if (_targetCharacter == null) return;
                }
            }
            else _targetCharacter = _currentNode.TargetCharacter;

            if (_targetCharacter != null && CharacterNameText != null)
                CharacterNameText.text = _targetCharacter.DisplayName_RU;

            BuildAvatar();

            if (!_currentNode.IsGiftMode)
            {
                if (CategoriesPanel != null) CategoriesPanel.gameObject.SetActive(true);
                BuildTabs();
            }
            else
            {
                if (CategoriesPanel != null) CategoriesPanel.gameObject.SetActive(false);

                // В режиме подарка прячем все кастомные вкладки
                Button[] allTabs = GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab_")).ToArray();
                foreach (var t in allTabs) t.gameObject.SetActive(false);

                BuildGiftItems();
            }
        }

        private void BuildAvatar()
        {
            foreach (Transform child in AvatarMaskContainer) Destroy(child.gameObject);
            _avatarLayers.Clear();

            if (_targetCharacter == null) return;

            for (int i = _targetCharacter.BaseLayers.Count - 1; i >= 0; i--)
            {
                var layer = _targetCharacter.BaseLayers[i];

                GameObject layerObj = new GameObject("Layer_" + layer.LayerName);
                layerObj.transform.SetParent(AvatarMaskContainer, false);
                var rt = layerObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                var img = layerObj.AddComponent<Image>();
                img.preserveAspect = true;
                img.color = layer.Tint;

                string equippedItemId = NovellaWardrobeAPI.GetEquippedItemID(_targetCharacter.name, layer.LayerName);
                if (!string.IsNullOrEmpty(equippedItemId))
                {
                    var item = _allItems.Find(x => x.ItemID == equippedItemId);
                    if (item != null) img.sprite = item.ItemSprite;
                    else img.sprite = layer.DefaultSprite;
                }
                else img.sprite = layer.DefaultSprite;

                _avatarLayers[layer.LayerName] = img;
            }
        }

        private void BuildTabs()
        {
            _spawnedTabs.Clear();

            // Ищем ВСЕ кнопки в Гардеробе, которые начинаются на "Tab_"
            Button[] allTabs = GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab_")).ToArray();
            bool firstTabSet = false;

            foreach (Button btn in allTabs)
            {
                string catName = btn.name.Substring(4); // Убираем приставку "Tab_" (Например: Tab_Hair -> Hair)

                // Ищем все подходящие вещи по полу для этой категории
                var validItems = _allItems.Where(i => i.GetLayerName() == catName &&
                                            (i.Gender == EItemGender.Unisex ||
                                            (i.Gender == EItemGender.Male && _targetCharacter.Gender == ECharacterGender.Male) ||
                                            (i.Gender == EItemGender.Female && _targetCharacter.Gender == ECharacterGender.Female))).ToList();

                // ЕСЛИ ДЛЯ ЭТОЙ КАТЕГОРИИ НЕТ НИ ОДНОЙ ВЕЩИ -> ПРЯЧЕМ КНОПКУ ПОЛНОСТЬЮ!
                if (validItems.Count == 0)
                {
                    btn.gameObject.SetActive(false);
                    continue;
                }

                // Если вещи есть -> включаем кнопку и настраиваем
                btn.gameObject.SetActive(true);
                _spawnedTabs.Add(btn.gameObject);

                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => {
                    UpdateTabVisuals(btn.gameObject);
                    ShowCategory(catName);
                });

                if (!firstTabSet)
                {
                    UpdateTabVisuals(btn.gameObject);
                    ShowCategory(catName);
                    firstTabSet = true;
                }
            }

            if (!firstTabSet)
            {
                CreateWarningTextIfNeeded();
                _warningText.gameObject.SetActive(true);
                _warningText.text = ToolLang.Get("No items available for this character.", "Для этого персонажа нет доступных вещей.");
                foreach (Transform child in ItemsGrid) Destroy(child.gameObject);
            }
        }

        private void UpdateTabVisuals(GameObject activeTab)
        {
            foreach (var tab in _spawnedTabs)
            {
                if (tab == null) continue;
                Button btn = tab.GetComponent<Button>();
                Image img = tab.GetComponent<Image>();
                TMP_Text txt = tab.GetComponentInChildren<TMP_Text>();

                if (tab == activeTab)
                {
                    if (btn != null) btn.interactable = false; // Кнопку активной вкладки нажимать нельзя
                    if (img != null) img.color = new Color(0.3f, 0.5f, 0.8f, 1f); // Подсветка активной
                    if (txt != null) txt.color = Color.white;
                }
                else
                {
                    if (btn != null) btn.interactable = true;
                    if (img != null) img.color = new Color(0.15f, 0.15f, 0.15f, 1f); // Темный фон
                    if (txt != null) txt.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                }
            }
        }

        private void CreateWarningTextIfNeeded()
        {
            if (_warningText != null) return;

            Transform scrollRectT = ItemsGrid.parent.parent;
            GameObject wObj = new GameObject("WarningText");
            wObj.transform.SetParent(scrollRectT, false);
            var rt = wObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            _warningText = wObj.AddComponent<TextMeshProUGUI>();
            _warningText.alignment = TextAlignmentOptions.Center;
            _warningText.fontSize = 35;
            _warningText.color = new Color(1f, 0.4f, 0.4f, 1f);
            _warningText.fontStyle = FontStyles.Bold;
            _warningText.raycastTarget = false;
        }

        private void ShowCategory(string categoryName)
        {
            _currentCategory = categoryName;
            foreach (Transform child in ItemsGrid) Destroy(child.gameObject);

            if (_warningText != null) _warningText.gameObject.SetActive(false);

            var categoryItems = _allItems.Where(i => i.GetLayerName() == categoryName &&
                                            (i.Gender == EItemGender.Unisex ||
                                            (i.Gender == EItemGender.Male && _targetCharacter.Gender == ECharacterGender.Male) ||
                                            (i.Gender == EItemGender.Female && _targetCharacter.Gender == ECharacterGender.Female))).ToList();

            CreateItemSlot(null, "Снять", false);

            foreach (var item in categoryItems) CreateItemSlot(item, item.DisplayName, false);
        }

        private void BuildGiftItems()
        {
            foreach (Transform child in ItemsGrid) Destroy(child.gameObject);

            if (_warningText != null) _warningText.gameObject.SetActive(false);

            foreach (var gift in _currentNode.ItemsToChoose)
            {
                var dummyItem = ScriptableObject.CreateInstance<WardrobeItemAsset>();
                dummyItem.ItemID = gift.ItemName;
                dummyItem.BaseLayer = ECharacterLayer.Extra;
                dummyItem.CustomLayerName = gift.TargetLayer;
                dummyItem.ItemSprite = gift.ItemSprite;
                dummyItem.UIIcon = gift.ItemSprite;
                dummyItem.Gender = EItemGender.Unisex;

                CreateItemSlot(dummyItem, gift.ItemName, true);
            }
        }

        private void CreateItemSlot(WardrobeItemAsset item, string label, bool isGift = false)
        {
            GameObject slotObj = Instantiate(ItemSlotPrefab, ItemsGrid);
            slotObj.SetActive(true);

            Image iconImg = slotObj.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImg != null)
            {
                if (item != null && item.UIIcon != null)
                {
                    iconImg.sprite = item.UIIcon;
                    iconImg.color = Color.white;
                }
                else iconImg.color = new Color(0, 0, 0, 0);
            }

            Button btn = slotObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => {
                    if (_targetCharacter == null) return;

                    if (item == null) NovellaWardrobeAPI.UnequipItem(_targetCharacter.name, _currentCategory);
                    else
                    {
                        string targetLayerName = item.GetLayerName();

                        if (isGift)
                        {
                            NovellaWardrobeAPI.EquipItem(_targetCharacter.name, targetLayerName, item.ItemID);
                            if (_avatarLayers.ContainsKey(targetLayerName)) _avatarLayers[targetLayerName].sprite = item.ItemSprite;
                        }
                        else
                        {
                            NovellaWardrobeAPI.EquipItem(_targetCharacter.name, targetLayerName, item.ItemID);
                        }
                    }
                    BuildAvatar();
                });
            }
        }

        public void CloseWardrobe()
        {
            if (_canvas != null) _canvas.enabled = false;
            if (_raycaster != null) _raycaster.enabled = false;

            if (_player != null)
            {
                if (_player.DialoguePanel != null) _player.DialoguePanel.SetActive(true);
                _player.PlayNode(_currentNode.NextNodeID);
            }
        }
    }
}