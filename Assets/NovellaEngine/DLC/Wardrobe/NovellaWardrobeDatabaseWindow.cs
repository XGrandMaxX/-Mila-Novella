using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;

namespace NovellaEngine.DLC.Wardrobe
{
    public class NovellaWardrobeDatabaseWindow : EditorWindow
    {
        private List<WardrobeItemAsset> _items = new List<WardrobeItemAsset>();
        private WardrobeItemAsset _selectedItem;

        private Vector2 _scrollPos;
        private string _searchQuery = "";
        private bool _isGridView = false;

        public static void ShowWindow()
        {
            var window = GetWindow<NovellaWardrobeDatabaseWindow>(ToolLang.Get("Wardrobe DB", "База Гардероба"));
            window.minSize = new Vector2(750, 500);
            window.Show();
        }

        private void OnEnable() { RefreshItems(); }

        private void RefreshItems()
        {
            _items.Clear();
            string[] guids = AssetDatabase.FindAssets("t:WardrobeItemAsset");
            foreach (string guid in guids)
            {
                var item = AssetDatabase.LoadAssetAtPath<WardrobeItemAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null) _items.Add(item);
            }
            _items = _items.OrderBy(i => i.GetLayerName()).ThenBy(i => i.name).ToList();
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            GUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300), GUILayout.ExpandHeight(true));

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("➕ " + ToolLang.Get("Create New Item", "Создать новую вещь"), new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(35)))
                CreateNewItem();
            GUI.backgroundColor = Color.white;

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(20));
            _searchQuery = EditorGUILayout.TextField(_searchQuery);
            if (GUILayout.Button("X", GUILayout.Width(25))) { _searchQuery = ""; GUI.FocusControl(null); }
            if (GUILayout.Button(_isGridView ? "🔲" : "📄", GUILayout.Width(30))) { _isGridView = !_isGridView; }
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            var filteredItems = _items.Where(i => string.IsNullOrEmpty(_searchQuery) ||
                                                  i.DisplayName.ToLower().Contains(_searchQuery.ToLower()) ||
                                                  i.name.ToLower().Contains(_searchQuery.ToLower())).ToList();

            var favorites = filteredItems.Where(i => i.IsFavorite).ToList();
            if (favorites.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("⭐ FAVORITES", "⭐ ИЗБРАННЫЕ"), EditorStyles.boldLabel);
                DrawItemCollection(favorites);
                GUILayout.Space(10);
            }

            var others = filteredItems.Where(i => !i.IsFavorite).ToList();
            if (others.Count > 0)
            {
                var grouped = others.GroupBy(i => i.GetLayerName()).OrderBy(g => g.Key);
                foreach (var group in grouped)
                {
                    GUILayout.Label($"📁 {group.Key}", EditorStyles.boldLabel);
                    DrawItemCollection(group.ToList());
                    GUILayout.Space(10);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawItemCollection(List<WardrobeItemAsset> list)
        {
            if (_isGridView)
            {
                int cols = 3; int current = 0;
                GUILayout.BeginVertical(); GUILayout.BeginHorizontal();
                foreach (var item in list)
                {
                    if (item == null) continue;
                    DrawItemGrid(item);
                    current++;
                    if (current >= cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); current = 0; }
                }
                GUILayout.EndHorizontal(); GUILayout.EndVertical();
            }
            else
            {
                foreach (var item in list) { if (item == null) continue; DrawItemList(item); }
            }
        }

        private void DrawItemList(WardrobeItemAsset item)
        {
            GUI.backgroundColor = _selectedItem == item ? new Color(0.3f, 0.5f, 0.8f) : Color.white;
            GUILayout.BeginHorizontal(GUI.skin.button);

            Texture2D icon = item.UIIcon != null ? item.UIIcon.texture : (item.ItemSprite != null ? item.ItemSprite.texture : null);
            if (icon != null) GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            else GUILayout.Label("👕", GUILayout.Width(20));

            if (GUILayout.Button(item.DisplayName, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(24)))
            {
                _selectedItem = item;
                GUI.FocusControl(null);
            }

            GUI.backgroundColor = Color.white;
            string starIcon = item.IsFavorite ? "★" : "☆";
            GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = item.IsFavorite ? Color.yellow : Color.gray } };
            if (GUILayout.Button(starIcon, starStyle, GUILayout.Width(25), GUILayout.Height(25)))
            {
                item.IsFavorite = !item.IsFavorite;
                EditorUtility.SetDirty(item);
            }

            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
        }

        private void DrawItemGrid(WardrobeItemAsset item)
        {
            float size = 84f;
            Rect rect = GUILayoutUtility.GetRect(size, size);

            GUI.backgroundColor = _selectedItem == item ? new Color(0.3f, 0.5f, 0.8f) : Color.white;
            if (GUI.Button(rect, GUIContent.none, GUI.skin.button)) { _selectedItem = item; GUI.FocusControl(null); }
            GUI.backgroundColor = Color.white;

            Texture2D icon = item.UIIcon != null ? item.UIIcon.texture : (item.ItemSprite != null ? item.ItemSprite.texture : null);
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 12, rect.y + 5, 60, 60), icon, ScaleMode.ScaleToFit);
            }

            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 10 };
            GUI.Label(new Rect(rect.x + 2, rect.yMax - 20, rect.width - 4, 20), item.DisplayName, nameStyle);

            GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = item.IsFavorite ? Color.yellow : Color.gray } };
            GUI.backgroundColor = Color.clear;
            if (GUI.Button(new Rect(rect.xMax - 24, rect.y + 2, 24, 24), item.IsFavorite ? "★" : "☆", starStyle))
            {
                item.IsFavorite = !item.IsFavorite;
                EditorUtility.SetDirty(item);
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawRightPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            if (_selectedItem == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select an item to edit.", "Выберите вещь для настройки."), EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"⚙ {ToolLang.Get("Editing:", "Настройка:")} {_selectedItem.name}", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUILayout.Button("💾 " + ToolLang.Get("Apply & Rename File", "Применить ID и переименовать"), GUILayout.Height(30)))
                {
                    if (_selectedItem.name != _selectedItem.ItemID && !string.IsNullOrEmpty(_selectedItem.ItemID))
                    {
                        AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(_selectedItem), _selectedItem.ItemID);
                        AssetDatabase.SaveAssets();
                        RefreshItems();
                        GUI.FocusControl(null);
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(15);

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginVertical(EditorStyles.helpBox);
                _selectedItem.ItemID = EditorGUILayout.TextField(new GUIContent(ToolLang.Get("Unique Item ID", "Уникальный ID вещи"), ToolLang.Get("Use this ID in code or variables.", "Используйте этот ID в базе переменных.")), _selectedItem.ItemID);
                _selectedItem.DisplayName = EditorGUILayout.TextField(ToolLang.Get("Display Name (UI)", "Имя для интерфейса"), _selectedItem.DisplayName);
                GUILayout.EndVertical();

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);

                // ИСПРАВЛЕНО: Используем EItemGender для одежды
                _selectedItem.Gender = (EItemGender)EditorGUILayout.EnumPopup(ToolLang.Get("Gender", "Пол одежды"), _selectedItem.Gender);
                GUILayout.Space(5);

                _selectedItem.BaseLayer = (ECharacterLayer)EditorGUILayout.EnumPopup(ToolLang.Get("Layer Category", "Категория / Слой"), _selectedItem.BaseLayer);

                if (_selectedItem.BaseLayer == ECharacterLayer.Extra)
                {
                    _selectedItem.CustomLayerName = EditorGUILayout.TextField(ToolLang.Get("Custom Layer Name", "Свой слой (название)"), _selectedItem.CustomLayerName);
                }

                EditorGUILayout.HelpBox(ToolLang.Get("Items will only fit characters with matching gender and layer.", "Вещи подойдут только персонажам с совпадающим полом и слоем."), MessageType.Info);
                GUILayout.EndVertical();

                GUILayout.Space(15);
                GUILayout.BeginHorizontal();

                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(160));
                GUILayout.Label("🎭 " + ToolLang.Get("Sprite on Character:", "Спрайт на персонаже:"), EditorStyles.miniBoldLabel);
                GUILayout.Space(5);
                _selectedItem.ItemSprite = (Sprite)EditorGUILayout.ObjectField(_selectedItem.ItemSprite, typeof(Sprite), false, GUILayout.Width(120), GUILayout.Height(120));
                GUILayout.EndVertical();

                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(160));
                GUILayout.Label("🖼 " + ToolLang.Get("Icon in Wardrobe UI:", "Иконка в меню:"), EditorStyles.miniBoldLabel);
                GUILayout.Space(5);
                _selectedItem.UIIcon = (Sprite)EditorGUILayout.ObjectField(_selectedItem.UIIcon, typeof(Sprite), false, GUILayout.Width(120), GUILayout.Height(120));
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selectedItem);
                }

                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑 " + ToolLang.Get("Delete Item", "Удалить предмет"), GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete?", "Удалить?"), ToolLang.Get($"Delete '{_selectedItem.name}'?", $"Удалить '{_selectedItem.name}'?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("No", "Нет")))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_selectedItem));
                        _selectedItem = null;
                        RefreshItems();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndVertical();
        }

        private void CreateNewItem()
        {
            string dir = "Assets/NovellaEngine/DLC/Wardrobe/Resources/Wardrobe";
            if (!System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var newItem = ScriptableObject.CreateInstance<WardrobeItemAsset>();
            newItem.ItemID = "Item_" + System.Guid.NewGuid().ToString().Substring(0, 5);
            newItem.DisplayName = "New Item";

            string path = $"{dir}/{newItem.ItemID}.asset";
            AssetDatabase.CreateAsset(newItem, path);
            AssetDatabase.SaveAssets();

            RefreshItems();
            _selectedItem = newItem;
        }
    }
}