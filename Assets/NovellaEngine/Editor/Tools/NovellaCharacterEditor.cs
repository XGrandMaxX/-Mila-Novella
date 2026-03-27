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
            NovellaTutorialManager.BlockBackgroundEvents(this);

            Event evt = Event.current;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Delete && _selectedCharacter != null)
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

            NovellaTutorialManager.DrawOverlay(this);
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

            if (_selectedCharacter != null && _selectedCharacter.BaseLayers.Count > 0 && _selectedCharacter.BaseLayers[0].DefaultSprite != null)
                _editingSpritePath = AssetDatabase.GetAssetPath(_selectedCharacter.BaseLayers[0].DefaultSprite);
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

            var mainChars = filteredList.Where(c => c.IsPlayerCharacter).ToList();
            if (mainChars.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("🦸 MAIN CHARACTERS", "🦸 ГЛАВНЫЕ ГЕРОИ"), EditorStyles.boldLabel);
                DrawCharacterCollection(mainChars);
                GUILayout.Space(10);
            }

            var favorites = filteredList.Where(c => c.IsFavorite && !c.IsPlayerCharacter).ToList();
            if (favorites.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("⭐ FAVORITES", "⭐ ИЗБРАННЫЕ"), EditorStyles.boldLabel);
                DrawCharacterCollection(favorites);
                GUILayout.Space(10);
            }

            var others = filteredList.Where(c => !c.IsFavorite && !c.IsPlayerCharacter).ToList();
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
            // ИСПРАВЛЕНИЕ: Мы definitively фиксируем ширину панели! Спрайты её больше не сдавят.
            float fixedWidth = 400f;
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(fixedWidth), GUILayout.ExpandHeight(true));

            if (_selectedCharacter == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a character to edit", "Выберите персонажа"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{ToolLang.Get("Editing:", "Редактируем:")} {_selectedCharacter.name}", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();

                bool showHints = EditorPrefs.GetBool("NovellaCharHints", true);
                bool newHints = GUILayout.Toggle(showHints, ToolLang.Get(" Tips", " Подсказки"), EditorStyles.toolbarButton);
                if (newHints != showHints) EditorPrefs.SetBool("NovellaCharHints", newHints);

                GUILayout.EndHorizontal();
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
                GUILayout.BeginVertical(EditorStyles.helpBox);

                var isPlayerProp = _serializedObject.FindProperty("IsPlayerCharacter");
                isPlayerProp.boolValue = EditorGUILayout.ToggleLeft(ToolLang.Get("Is Main Character (Player)", "Это Главный Герой (Игрок)"), isPlayerProp.boolValue, EditorStyles.boldLabel);

                if (isPlayerProp.boolValue && showHints)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "This character is marked as the Player. They will be available for selection in the UI Forge.",
                        "Персонаж отмечен как Игрок. Вы сможете использовать его в Кузнице UI для меню создания персонажа."
                    ), MessageType.Info);
                }
                GUILayout.EndVertical();

                GUILayout.Space(15);
                GUILayout.Label("📚 " + ToolLang.Get("Base Layers (Paper Doll)", "Базовые слои (Кукла)"), EditorStyles.boldLabel);

                if (showHints)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "HOW IT WORKS:\nLayers are drawn from top to bottom. Example:\nLayer 1: Body (Bottom)\nLayer 2: Clothes\nLayer 3: Face\nLayer 4: Hair (Top)",
                        "КАК ЭТО РАБОТАЕТ:\nСлои рисуются сверху вниз. Пример правильного порядка:\nСлой 1: Базовое тело (Позади всех)\nСлой 2: Одежда\nСлой 3: Лицо (Глаза, рот)\nСлой 4: Прическа (Спереди всех)"
                    ), MessageType.Info);
                }

                SerializedProperty baseLayersProp = _serializedObject.FindProperty("BaseLayers");
                List<string> layerNames = new List<string>();

                for (int i = 0; i < baseLayersProp.arraySize; i++)
                {
                    var layerProp = baseLayersProp.GetArrayElementAtIndex(i);
                    var nameP = layerProp.FindPropertyRelative("LayerName");
                    var sprP = layerProp.FindPropertyRelative("DefaultSprite");
                    var optionsP = layerProp.FindPropertyRelative("WardrobeOptions");

                    layerNames.Add(nameP.stringValue);

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    nameP.stringValue = EditorGUILayout.TextField(nameP.stringValue, GUILayout.Width(100));

                    int layerIndex = i;

                    // ИСПРАВЛЕНИЕ: Автоматически усекаем длинное название спрайта, чтобы оно не выдавливало панель.
                    string sprFullName = sprP.objectReferenceValue != null ? sprP.objectReferenceValue.name : ToolLang.Get("Gallery...", "Галерея...");
                    string sprDisplayName = sprFullName;
                    if (sprFullName.Length > 20) sprDisplayName = sprFullName.Substring(0, 17) + "..."; // Оставляем только начало

                    if (GUILayout.Button("🖼 " + sprDisplayName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Undo.RecordObject(_selectedCharacter, "Change Base Layer Sprite");
                            Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                            _selectedCharacter.BaseLayers[layerIndex].DefaultSprite = spr;
                            EditorUtility.SetDirty(_selectedCharacter);
                            if (spr != null) SelectSpriteForEdit(spr);
                            Repaint();
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }

                    if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(25)))
                        SelectSpriteForEdit(sprP.objectReferenceValue as Sprite);

                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                    {
                        baseLayersProp.DeleteArrayElementAtIndex(i);
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                if (GUILayout.Button("+ " + ToolLang.Get("Add Layer", "Добавить слой"), EditorStyles.miniButton))
                {
                    baseLayersProp.arraySize++;
                    var newLayer = baseLayersProp.GetArrayElementAtIndex(baseLayersProp.arraySize - 1);
                    newLayer.FindPropertyRelative("LayerName").stringValue = "New Layer";
                    newLayer.FindPropertyRelative("DefaultSprite").objectReferenceValue = null;
                }

                GUILayout.Space(15);
                SerializedProperty emotionsProp = _serializedObject.FindProperty("Emotions");
                _showEmotions = EditorGUILayout.Foldout(_showEmotions, "😊 " + ToolLang.Get("Emotions (Overrides)", "Эмоции (Переопределение)"), true, EditorStyles.foldoutHeader);

                if (_showEmotions)
                {
                    if (showHints)
                    {
                        EditorGUILayout.HelpBox(ToolLang.Get(
                            "Emotions are PRESETS. Instead of redrawing the whole character, you just replace specific layers.\nExample: Emotion 'Smile' -> overrides layer 'Face'.",
                            "Эмоции работают как ПРЕСЕТЫ. Вам не нужно перерисовывать всего персонажа!\nПример: Эмоция 'Smile' -> переопределяет только слой 'Лицо'. Тело и одежда остаются нетронутыми."
                        ), MessageType.Info);
                        GUILayout.Space(5);
                    }

                    for (int i = 0; i < emotionsProp.arraySize; i++)
                    {
                        var emProp = emotionsProp.GetArrayElementAtIndex(i);
                        var emName = emProp.FindPropertyRelative("EmotionName");
                        var overridesProp = emProp.FindPropertyRelative("LayerOverrides");

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get("Preset:", "Пресет:"), EditorStyles.boldLabel, GUILayout.Width(60));
                        emName.stringValue = EditorGUILayout.TextField(emName.stringValue);

                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("DEL", GUILayout.Width(45)))
                        {
                            emotionsProp.DeleteArrayElementAtIndex(i);
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        if (overridesProp.arraySize > 0) GUILayout.Space(5);

                        for (int j = 0; j < overridesProp.arraySize; j++)
                        {
                            var overProp = overridesProp.GetArrayElementAtIndex(j);
                            var lName = overProp.FindPropertyRelative("LayerName");
                            var lSpr = overProp.FindPropertyRelative("OverrideSprite");

                            GUILayout.BeginHorizontal();
                            GUILayout.Space(15);
                            GUILayout.Label("↳", GUILayout.Width(15));

                            int lIdx = layerNames.IndexOf(lName.stringValue);
                            if (lIdx == -1) lIdx = 0;

                            if (layerNames.Count > 0)
                            {
                                lIdx = EditorGUILayout.Popup(lIdx, layerNames.ToArray(), GUILayout.Width(100));
                                lName.stringValue = layerNames[lIdx];
                            }
                            else
                            {
                                lName.stringValue = EditorGUILayout.TextField(lName.stringValue, GUILayout.Width(100));
                            }

                            int eIdx = i;
                            int oIdx = j;

                            // ИСПРАВЛЕНИЕ: Автоматически усекаем длинное название спрайта эмоции.
                            string overSprFullName = lSpr.objectReferenceValue != null ? lSpr.objectReferenceValue.name : ToolLang.Get("Gallery...", "Галерея...");
                            string overSprDisplayName = overSprFullName;
                            if (overSprFullName.Length > 20) overSprDisplayName = overSprFullName.Substring(0, 17) + "..."; // Оставляем начало

                            if (GUILayout.Button("🖼 " + overSprDisplayName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                            {
                                NovellaGalleryWindow.ShowWindow(obj => {
                                    Undo.RecordObject(_selectedCharacter, "Change Override Sprite");
                                    Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                                    _selectedCharacter.Emotions[eIdx].LayerOverrides[oIdx].OverrideSprite = spr;
                                    EditorUtility.SetDirty(_selectedCharacter);
                                    if (spr != null) SelectSpriteForEdit(spr);
                                    Repaint();
                                }, NovellaGalleryWindow.EGalleryFilter.Image);
                            }

                            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(25)))
                                SelectSpriteForEdit(lSpr.objectReferenceValue as Sprite);

                            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                            {
                                overridesProp.DeleteArrayElementAtIndex(j);
                                GUI.backgroundColor = Color.white;
                                GUILayout.EndHorizontal();
                                break;
                            }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(35);
                        if (GUILayout.Button("+ " + ToolLang.Get("Add Layer Override", "Переопределить слой"), EditorStyles.miniButton))
                        {
                            overridesProp.arraySize++;
                            var newOver = overridesProp.GetArrayElementAtIndex(overridesProp.arraySize - 1);
                            newOver.FindPropertyRelative("LayerName").stringValue = layerNames.Count > 0 ? layerNames[0] : "Base";
                            newOver.FindPropertyRelative("OverrideSprite").objectReferenceValue = null;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                    }

                    if (GUILayout.Button("+ " + ToolLang.Get("Add Emotion Preset", "Добавить пресет эмоции")))
                    {
                        emotionsProp.arraySize++;
                        var newEm = emotionsProp.GetArrayElementAtIndex(emotionsProp.arraySize - 1);
                        newEm.FindPropertyRelative("EmotionName").stringValue = "NewEmotion";
                        newEm.FindPropertyRelative("LayerOverrides").arraySize = 0;
                    }
                }

                GUILayout.Space(15);

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