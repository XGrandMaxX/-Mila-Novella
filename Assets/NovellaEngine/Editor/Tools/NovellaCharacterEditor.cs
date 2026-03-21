using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public class NovellaCharacterEditor : EditorWindow
    {
        private List<NovellaCharacter> _characters = new List<NovellaCharacter>();
        private NovellaCharacter _selectedCharacter;
        private SerializedObject _serializedObject;

        private string _editingSpritePath = "";
        private bool _isSpriteDirty = false;

        private Vector2 _listScrollPos;
        private Vector2 _inspectorScrollPos;
        private Vector2 _spriteSettingsScrollPos;

        private string _searchQuery = "";
        private bool _needsRefresh = false;
        private bool _isGridView = false;
        private bool _showEmotions = true;

        private float _previewZoom = 1f;
        private Vector2 _previewPan = Vector2.zero;

        [MenuItem("Tools/Novella Engine/Character Editor")]
        public static void OpenWindow()
        {
            var win = GetWindow<NovellaCharacterEditor>(ToolLang.Get("Character Editor", "Редактор Персонажей"));
            win.minSize = new Vector2(1050, 600);
            win.Show();
        }

        public static void OpenWithCharacter(NovellaCharacter charToSelect)
        {
            var win = GetWindow<NovellaCharacterEditor>(ToolLang.Get("Character Editor", "Редактор Персонажей"));
            win.minSize = new Vector2(1050, 600);
            win.RefreshCharacterList();
            win.SelectCharacter(charToSelect);
            win.Show();
        }

        private void OnEnable() => RefreshCharacterList();
        private void OnDisable() { if (_serializedObject != null) _serializedObject.Dispose(); }

        private void Update()
        {
            if (_needsRefresh) { RefreshCharacterList(); _needsRefresh = false; Repaint(); }
        }

        private void RefreshCharacterList()
        {
            _characters.Clear();
            string[] guids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NovellaCharacter character = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(path);
                if (character != null) _characters.Add(character);
            }
            _characters = _characters.OrderBy(c => c.name).ToList();
        }

        private void OnGUI()
        {
            Event evt = Event.current;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Delete)
            {
                if (_selectedCharacter != null)
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete Character?", "Удалить персонажа?"),
                        ToolLang.Get($"Are you sure you want to delete '{_selectedCharacter.name}'?", $"Вы уверены, что хотите удалить '{_selectedCharacter.name}'?"),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        string path = AssetDatabase.GetAssetPath(_selectedCharacter);
                        AssetDatabase.DeleteAsset(path);
                        SelectCharacter(null);
                        _needsRefresh = true;
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && _isSpriteDirty)
            {
                _isSpriteDirty = false;
                evt.Use();
            }

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawCenterPanel();
            DrawSpritePanel();
            GUILayout.EndHorizontal();
        }

        private void SelectCharacter(NovellaCharacter newChar)
        {
            if (_isSpriteDirty && _selectedCharacter != null && !string.IsNullOrEmpty(_editingSpritePath))
            {
                if (EditorUtility.DisplayDialog(ToolLang.Get("Unsaved Sprite Settings", "Несохраненные настройки спрайта"),
                    ToolLang.Get("You have unsaved import settings. Apply them before switching?", "У вас есть несохраненные настройки импорта. Применить их перед переключением?"),
                    ToolLang.Get("Apply", "Применить"), ToolLang.Get("Discard", "Сбросить")))
                {
                    TextureImporter imp = AssetImporter.GetAtPath(_editingSpritePath) as TextureImporter;
                    if (imp != null) imp.SaveAndReimport();
                }
            }

            _selectedCharacter = newChar;
            _isSpriteDirty = false;
            _previewZoom = 1f;
            _previewPan = Vector2.zero;

            if (_selectedCharacter != null && _selectedCharacter.DefaultSprite != null)
                _editingSpritePath = AssetDatabase.GetAssetPath(_selectedCharacter.DefaultSprite);
            else
                _editingSpritePath = "";

            GUI.FocusControl(null);
        }

        private void SelectSpriteForEdit(Sprite spr)
        {
            string newPath = spr != null ? AssetDatabase.GetAssetPath(spr) : "";

            if (_isSpriteDirty && !string.IsNullOrEmpty(_editingSpritePath) && _editingSpritePath != newPath)
            {
                if (EditorUtility.DisplayDialog(ToolLang.Get("Unsaved Sprite Settings", "Несохраненные настройки спрайта"),
                    ToolLang.Get("Apply changes before switching sprite?", "Применить изменения перед переключением спрайта?"),
                    ToolLang.Get("Apply", "Применить"), ToolLang.Get("Discard", "Сбросить")))
                {
                    TextureImporter imp = AssetImporter.GetAtPath(_editingSpritePath) as TextureImporter;
                    if (imp != null) imp.SaveAndReimport();
                }
            }

            _editingSpritePath = newPath;
            _isSpriteDirty = false;
            _previewZoom = 1f;
            _previewPan = Vector2.zero;
            GUI.FocusControl(null);
        }

        private void DrawLeftPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(250), GUILayout.ExpandHeight(true));

            if (GUILayout.Button(ToolLang.Get("➕ Create New Character", "➕ Создать персонажа"), GUILayout.Height(30))) CreateNewCharacter();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(20));
            _searchQuery = EditorGUILayout.TextField(_searchQuery);
            if (GUILayout.Button("X", GUILayout.Width(25))) { _searchQuery = ""; GUI.FocusControl(null); }
            if (GUILayout.Button(_isGridView ? "🔲" : "📄", GUILayout.Width(30))) { _isGridView = !_isGridView; }
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            _listScrollPos = GUILayout.BeginScrollView(_listScrollPos);

            var filteredList = _characters.Where(c => c != null && (string.IsNullOrEmpty(_searchQuery) ||
                                                      c.name.ToLower().Contains(_searchQuery.ToLower()) ||
                                                      c.CharacterID.ToLower().Contains(_searchQuery.ToLower()))).ToList();

            var favorites = filteredList.Where(c => c.IsFavorite).ToList();
            if (favorites.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("⭐ FAVORITES", "⭐ ИЗБРАННЫЕ"), EditorStyles.boldLabel);
                DrawCharacterCollection(favorites);
                GUILayout.Space(10);
            }

            var others = filteredList.Where(c => !c.IsFavorite).ToList();
            if (others.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("📝 ALL CHARACTERS", "📝 ВСЕ ПЕРСОНАЖИ"), EditorStyles.boldLabel);
                DrawCharacterCollection(others);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawCharacterCollection(List<NovellaCharacter> list)
        {
            if (_isGridView)
            {
                int cols = 3; int current = 0;
                GUILayout.BeginVertical(); GUILayout.BeginHorizontal();
                foreach (var character in list)
                {
                    if (character == null) continue;
                    DrawCharacterGridItem(character);
                    current++;
                    if (current >= cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); current = 0; }
                }
                GUILayout.EndHorizontal(); GUILayout.EndVertical();
            }
            else
            {
                foreach (var character in list) { if (character == null) continue; DrawCharacterListItem(character); }
            }
        }

        private void DrawCharacterListItem(NovellaCharacter character)
        {
            GUI.backgroundColor = _selectedCharacter == character ? new Color(0.3f, 0.5f, 0.8f) : Color.white;
            GUILayout.BeginHorizontal(GUI.skin.button);

            if (GUILayout.Button(character.name, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(24)))
                SelectCharacter(character);

            GUI.backgroundColor = Color.white;
            string starIcon = character.IsFavorite ? "★" : "☆";
            GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = character.IsFavorite ? Color.yellow : Color.gray } };
            if (GUILayout.Button(starIcon, starStyle, GUILayout.Width(25), GUILayout.Height(25))) { character.IsFavorite = !character.IsFavorite; EditorUtility.SetDirty(character); AssetDatabase.SaveAssets(); }

            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
        }

        private void DrawCharacterGridItem(NovellaCharacter character)
        {
            float size = 74f;
            Rect rect = GUILayoutUtility.GetRect(size, size);

            GUI.backgroundColor = _selectedCharacter == character ? new Color(0.3f, 0.5f, 0.8f) : Color.white;
            if (GUI.Button(rect, GUIContent.none, GUI.skin.button)) SelectCharacter(character);
            GUI.backgroundColor = Color.white;

            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 11 };
            GUI.Label(new Rect(rect.x + 2, rect.y, rect.width - 20, rect.height), character.name, nameStyle);

            GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = character.IsFavorite ? Color.yellow : Color.gray } };
            GUI.backgroundColor = Color.clear;
            if (GUI.Button(new Rect(rect.x + rect.width - 24, rect.y + (rect.height / 2f) - 12f, 24, 24), character.IsFavorite ? "★" : "☆", starStyle))
            {
                character.IsFavorite = !character.IsFavorite;
                EditorUtility.SetDirty(character);
                AssetDatabase.SaveAssets();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawLimitHeader(string title, int currentLength, int maxLength)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUI.contentColor = currentLength >= maxLength ? new Color(1f, 0.4f, 0.4f) : Color.gray;
            GUILayout.Label($"{currentLength} / {maxLength}", EditorStyles.miniLabel);
            GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawCenterPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(350), GUILayout.ExpandHeight(true));

            if (_selectedCharacter == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a character to edit", "Выберите персонажа"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.Label($"{ToolLang.Get("Editing:", "Редактируем:")} {_selectedCharacter.name}", EditorStyles.largeLabel);
                GUILayout.Space(10);

                _inspectorScrollPos = GUILayout.BeginScrollView(_inspectorScrollPos);

                if (_serializedObject == null || _serializedObject.targetObject != _selectedCharacter)
                    _serializedObject = new SerializedObject(_selectedCharacter);

                _serializedObject.Update();
                EditorGUI.BeginChangeCheck();

                var idProp = _serializedObject.FindProperty("CharacterID");
                DrawLimitHeader(ToolLang.Get("Character ID", "Внутренний ID"), idProp.stringValue.Length, NovellaCharacter.MAX_ID_LENGTH);
                idProp.stringValue = EditorGUILayout.TextField(idProp.stringValue);
                if (idProp.stringValue.Length > NovellaCharacter.MAX_ID_LENGTH) idProp.stringValue = idProp.stringValue.Substring(0, NovellaCharacter.MAX_ID_LENGTH);
                GUILayout.Space(5);

                var nameEnProp = _serializedObject.FindProperty("DisplayName_EN");
                DrawLimitHeader(ToolLang.Get("Display Name (EN)", "Имя (EN)"), nameEnProp.stringValue.Length, NovellaCharacter.MAX_NAME_LENGTH);
                nameEnProp.stringValue = EditorGUILayout.TextField(nameEnProp.stringValue);
                if (nameEnProp.stringValue.Length > NovellaCharacter.MAX_NAME_LENGTH) nameEnProp.stringValue = nameEnProp.stringValue.Substring(0, NovellaCharacter.MAX_NAME_LENGTH);

                var nameRuProp = _serializedObject.FindProperty("DisplayName_RU");
                DrawLimitHeader(ToolLang.Get("Display Name (RU)", "Имя (RU)"), nameRuProp.stringValue.Length, NovellaCharacter.MAX_NAME_LENGTH);
                nameRuProp.stringValue = EditorGUILayout.TextField(nameRuProp.stringValue);
                if (nameRuProp.stringValue.Length > NovellaCharacter.MAX_NAME_LENGTH) nameRuProp.stringValue = nameRuProp.stringValue.Substring(0, NovellaCharacter.MAX_NAME_LENGTH);

                EditorGUILayout.PropertyField(_serializedObject.FindProperty("ThemeColor"), new GUIContent(ToolLang.Get("Speaker Color", "Цвет спикера")));

                GUILayout.Space(10);
                GUILayout.Label(ToolLang.Get("Visuals", "Визуализация"), EditorStyles.boldLabel);

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Default Sprite", "Стандартный спрайт"), GUILayout.Width(140));

                SerializedProperty defSprProp = _serializedObject.FindProperty("DefaultSprite");
                bool hasDefaultSpr = defSprProp.objectReferenceValue != null;
                string defSprName = hasDefaultSpr ? defSprProp.objectReferenceValue.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");
                if (defSprName.Length > 16) defSprName = defSprName.Substring(0, 14) + "..";

                if (GUILayout.Button("🖼 " + defSprName, EditorStyles.popup, GUILayout.MinWidth(50), GUILayout.ExpandWidth(true)))
                {
                    NovellaGalleryWindow.ShowWindow(obj => {
                        _serializedObject.Update();
                        Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                        defSprProp.objectReferenceValue = spr;
                        _serializedObject.ApplyModifiedProperties();
                        GUI.changed = true;
                        if (spr != null) SelectSpriteForEdit(spr);
                    }, NovellaGalleryWindow.EGalleryFilter.Image);
                }

                EditorGUI.BeginDisabledGroup(!hasDefaultSpr);
                if (GUILayout.Button("⚙", GUILayout.Width(25))) SelectSpriteForEdit(defSprProp.objectReferenceValue as Sprite);
                EditorGUI.EndDisabledGroup();

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("✖", GUILayout.Width(25))) { defSprProp.objectReferenceValue = null; GUI.changed = true; }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                GUILayout.Space(10);

                SerializedProperty emotionsProp = _serializedObject.FindProperty("Emotions");
                _showEmotions = EditorGUILayout.Foldout(_showEmotions, ToolLang.Get("Emotions", "Эмоции"), true, EditorStyles.foldoutHeader);

                if (_showEmotions)
                {
                    for (int i = 0; i < emotionsProp.arraySize; i++)
                    {
                        var elem = emotionsProp.GetArrayElementAtIndex(i);
                        var nameProp = elem.FindPropertyRelative("EmotionName");
                        var sprProp = elem.FindPropertyRelative("EmotionSprite");
                        bool hasEmSpr = sprProp.objectReferenceValue != null;

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();

                        GUILayout.BeginVertical();
                        EditorGUILayout.PropertyField(nameProp, new GUIContent(ToolLang.Get("Name", "Имя")));

                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(120));
                        string emSprName = hasEmSpr ? sprProp.objectReferenceValue.name : ToolLang.Get("Select...", "Выбрать...");
                        if (emSprName.Length > 15) emSprName = emSprName.Substring(0, 13) + "..";

                        if (GUILayout.Button("🖼 " + emSprName, EditorStyles.popup, GUILayout.MinWidth(50), GUILayout.ExpandWidth(true)))
                        {
                            int indexToUpdate = i;
                            NovellaGalleryWindow.ShowWindow(obj => {
                                _serializedObject.Update();
                                Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                                emotionsProp.GetArrayElementAtIndex(indexToUpdate).FindPropertyRelative("EmotionSprite").objectReferenceValue = spr;
                                _serializedObject.ApplyModifiedProperties();
                                GUI.changed = true;
                                if (spr != null) SelectSpriteForEdit(spr);
                            }, NovellaGalleryWindow.EGalleryFilter.Image);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();

                        GUILayout.BeginVertical(GUILayout.Width(25));
                        EditorGUI.BeginDisabledGroup(!hasEmSpr);
                        if (GUILayout.Button("⚙", GUILayout.Width(25), GUILayout.Height(20))) SelectSpriteForEdit(sprProp.objectReferenceValue as Sprite);
                        EditorGUI.EndDisabledGroup();

                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("✖", GUILayout.Width(25), GUILayout.Height(20))) { sprProp.objectReferenceValue = null; GUI.changed = true; }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndVertical();

                        GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
                        if (GUILayout.Button("DEL", GUILayout.Width(45), GUILayout.ExpandHeight(true))) { emotionsProp.DeleteArrayElementAtIndex(i); i--; }
                        GUI.backgroundColor = Color.white;

                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                    }

                    if (GUILayout.Button($"+ {ToolLang.Get("Add Emotion", "Добавить эмоцию")}"))
                    {
                        emotionsProp.InsertArrayElementAtIndex(emotionsProp.arraySize);
                        var newElem = emotionsProp.GetArrayElementAtIndex(emotionsProp.arraySize - 1);
                        newElem.FindPropertyRelative("EmotionName").stringValue = "NewEmotion";
                        newElem.FindPropertyRelative("EmotionSprite").objectReferenceValue = null;
                    }
                }

                GUILayout.Space(10);

                var notesProp = _serializedObject.FindProperty("InternalNotes");
                DrawLimitHeader(ToolLang.Get("Internal Notes", "Внутренние заметки"), notesProp.stringValue.Length, NovellaCharacter.MAX_NOTES_LENGTH);
                notesProp.stringValue = EditorGUILayout.TextArea(notesProp.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(60));
                if (notesProp.stringValue.Length > NovellaCharacter.MAX_NOTES_LENGTH) notesProp.stringValue = notesProp.stringValue.Substring(0, NovellaCharacter.MAX_NOTES_LENGTH);


                if (EditorGUI.EndChangeCheck())
                {
                    _serializedObject.ApplyModifiedProperties();
                    string path = AssetDatabase.GetAssetPath(_selectedCharacter);

                    if (!string.IsNullOrEmpty(path) && _selectedCharacter.name != _selectedCharacter.CharacterID && !string.IsNullOrEmpty(_selectedCharacter.CharacterID))
                    {
                        bool isDuplicate = _characters.Any(c => c != _selectedCharacter && c.CharacterID == _selectedCharacter.CharacterID);
                        if (isDuplicate)
                        {
                            EditorUtility.DisplayDialog("Error", ToolLang.Get("A character with this ID already exists!", "Персонаж с таким ID уже существует!"), "OK");
                            _selectedCharacter.CharacterID = _selectedCharacter.name;
                        }
                        else
                        {
                            AssetDatabase.RenameAsset(path, _selectedCharacter.CharacterID);
                            AssetDatabase.SaveAssets();
                            _needsRefresh = true;
                        }
                    }

                    var graphWindows = Resources.FindObjectsOfTypeAll<NovellaGraphWindow>();
                    foreach (var gw in graphWindows)
                    {
                        if (gw != null && gw.rootVisualElement != null)
                        {
                            gw.rootVisualElement.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals());
                            gw.Repaint();
                        }
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button(ToolLang.Get("DELETE CHARACTER", "УДАЛИТЬ ПЕРСОНАЖА"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(40)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete Character?", "Удалить персонажа?"),
                        ToolLang.Get($"Are you sure you want to delete '{_selectedCharacter.name}'?", $"Вы уверены, что хотите удалить '{_selectedCharacter.name}'?"),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        string path = AssetDatabase.GetAssetPath(_selectedCharacter);
                        AssetDatabase.DeleteAsset(path);
                        SelectCharacter(null);
                        _needsRefresh = true;
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndVertical();
        }

        private void DrawSpritePanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedCharacter != null && !string.IsNullOrEmpty(_editingSpritePath))
            {
                TextureImporter importer = AssetImporter.GetAtPath(_editingSpritePath) as TextureImporter;

                if (importer != null)
                {
                    string filename = System.IO.Path.GetFileNameWithoutExtension(_editingSpritePath);
                    GUILayout.Label(ToolLang.Get($"Import Settings: {filename}", $"Импорт: {filename}"), EditorStyles.largeLabel);
                    GUILayout.Space(10);

                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(_editingSpritePath);
                    if (tex != null)
                    {
                        Rect previewRect = GUILayoutUtility.GetRect(200, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                        GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);

                        Event e = Event.current;

                        if (e.type == EventType.MouseDown && e.clickCount == 2 && previewRect.Contains(e.mousePosition))
                        {
                            _previewZoom = 1f; _previewPan = Vector2.zero; e.Use();
                        }
                        else if (e.type == EventType.MouseDrag && e.button == 1 && previewRect.Contains(e.mousePosition))
                        {
                            _previewPan += e.delta; e.Use(); Repaint();
                        }
                        else if (e.type == EventType.ScrollWheel && previewRect.Contains(e.mousePosition))
                        {
                            _previewZoom -= e.delta.y * 0.05f;
                            _previewZoom = Mathf.Clamp(_previewZoom, 0.1f, 5f);
                            e.Use(); Repaint();
                        }

                        GUI.BeginGroup(previewRect);
                        float aspect = (float)tex.width / tex.height;
                        float drawH = previewRect.height * _previewZoom;
                        float drawW = drawH * aspect;

                        if (drawW > previewRect.width * _previewZoom)
                        {
                            drawW = previewRect.width * _previewZoom;
                            drawH = drawW / aspect;
                        }

                        Rect texRect = new Rect((previewRect.width - drawW) / 2 + _previewPan.x, (previewRect.height - drawH) / 2 + _previewPan.y, drawW, drawH);
                        GUI.DrawTexture(texRect, tex);
                        GUI.EndGroup();
                    }

                    GUILayout.Space(15);
                    _spriteSettingsScrollPos = GUILayout.BeginScrollView(_spriteSettingsScrollPos, GUILayout.Height(200));

                    TextureImporterSettings texSettings = new TextureImporterSettings();
                    importer.ReadTextureSettings(texSettings);

                    EditorGUI.BeginChangeCheck();

                    texSettings.textureType = (TextureImporterType)EditorGUILayout.EnumPopup("Texture Type", texSettings.textureType);

                    if (texSettings.textureType == TextureImporterType.Sprite)
                    {
                        texSettings.spriteMode = (int)(SpriteImportMode)EditorGUILayout.EnumPopup("Sprite Mode", (SpriteImportMode)texSettings.spriteMode);
                        texSettings.spritePixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", texSettings.spritePixelsPerUnit);

                        texSettings.spriteMeshType = (SpriteMeshType)EditorGUILayout.EnumPopup("Mesh Type", texSettings.spriteMeshType);
                        texSettings.spriteExtrude = (uint)EditorGUILayout.IntSlider("Extrude Edges", (int)texSettings.spriteExtrude, 0, 32);
                        texSettings.spriteGenerateFallbackPhysicsShape = EditorGUILayout.Toggle("Generate Physics Shape", texSettings.spriteGenerateFallbackPhysicsShape);
                    }

                    GUILayout.Space(10);
                    int[] sizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
                    string[] sizeStrs = { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" };
                    importer.maxTextureSize = EditorGUILayout.IntPopup("Max Size", importer.maxTextureSize, sizeStrs, sizes);
                    importer.textureCompression = (TextureImporterCompression)EditorGUILayout.EnumPopup("Compression", importer.textureCompression);
                    texSettings.filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", texSettings.filterMode);

                    if (EditorGUI.EndChangeCheck())
                    {
                        importer.SetTextureSettings(texSettings);
                        _isSpriteDirty = true;
                    }

                    if (tex != null)
                    {
                        GUILayout.Space(10);
                        long memSize = Profiler.GetRuntimeMemorySizeLong(tex);
                        string memStr = (memSize / 1024f).ToString("0.00") + " KB";
                        if (memSize > 1024 * 1024) memStr = (memSize / 1024f / 1024f).ToString("0.00") + " MB";

                        GUIStyle orangeText = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.6f, 0f) } };
                        GUILayout.Label(ToolLang.Get($"VRAM Size: {memStr}", $"Размер в VRAM: {memStr}"), orangeText);

                        if (memSize > 100 * 1024 * 1024)
                        {
                            EditorGUILayout.HelpBox(ToolLang.Get(
                                "⚠️ VRAM usage is very high (>100MB)! Consider reducing 'Max Size' or changing 'Compression' to avoid memory leaks on target devices.",
                                "⚠️ Потребление VRAM очень высокое (>100МБ)! Рекомендуется уменьшить 'Max Size' или изменить 'Compression', чтобы избежать проблем с памятью в игре."),
                                MessageType.Warning);
                        }
                    }

                    GUILayout.EndScrollView();

                    GUILayout.Space(10);
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button(ToolLang.Get("Open Sprite Editor", "Открыть Sprite Editor"), GUILayout.Height(30)))
                    {
                        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(_editingSpritePath);
                        EditorApplication.ExecuteMenuItem("Window/2D/Sprite Editor");
                    }

                    EditorGUI.BeginDisabledGroup(!_isSpriteDirty);

                    if (GUILayout.Button(ToolLang.Get("Revert", "Отменить"), GUILayout.Height(30), GUILayout.Width(80)))
                    {
                        _isSpriteDirty = false;
                        GUI.FocusControl(null);
                    }

                    GUI.backgroundColor = _isSpriteDirty ? new Color(0.2f, 0.8f, 0.4f) : Color.white;
                    if (GUILayout.Button(ToolLang.Get("Apply", "Применить"), GUILayout.Height(30)))
                    {
                        importer.SaveAndReimport();
                        _isSpriteDirty = false;
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(ToolLang.Get("Failed to load Texture Importer.", "Не удалось загрузить настройки спрайта."), EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                }
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a sprite (⚙) to view settings.", "Нажмите (⚙) возле спрайта для настроек."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
            }

            GUILayout.EndVertical();
        }

        private void CreateNewCharacter()
        {
            NovellaCharacter newChar = ScriptableObject.CreateInstance<NovellaCharacter>();
            string newId = "NewCharacter_" + System.Guid.NewGuid().ToString().Substring(0, 4);
            while (_characters.Any(c => c.CharacterID == newId))
                newId = "NewCharacter_" + System.Guid.NewGuid().ToString().Substring(0, 4);

            newChar.CharacterID = newId;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/NovellaEngine/Runtime/Data/Characters"))
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/_Project/NovellaEngine/Runtime/Data/Characters");
                AssetDatabase.Refresh();
            }

            string path = $"Assets/_Project/NovellaEngine/Runtime/Data/Characters/{newChar.CharacterID}.asset";
            AssetDatabase.CreateAsset(newChar, path);
            AssetDatabase.SaveAssets();

            SelectCharacter(newChar);
            _searchQuery = "";
            _needsRefresh = true;
        }
    }
}