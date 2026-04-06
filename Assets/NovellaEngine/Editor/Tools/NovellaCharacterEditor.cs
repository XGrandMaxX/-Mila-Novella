using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public class NovellaCharacterEditor : EditorWindow
    {
        private List<NovellaCharacter> _characters = new List<NovellaCharacter>();
        private NovellaCharacter _selectedCharacter;
        private SerializedObject _serializedObject;

        private Vector2 _listScrollPos;
        private Vector2 _inspectorScrollPos;

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

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawCenterPanel();
            DrawPaperDollPreviewPanel();
            GUILayout.EndHorizontal();

            NovellaTutorialManager.DrawOverlay(this);
        }

        private void SelectCharacter(NovellaCharacter newChar)
        {
            _selectedCharacter = newChar;
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
            float fixedWidth = 410f;
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

                var nameRuProp = _serializedObject.FindProperty("DisplayName_RU");
                DrawLimitHeader(ToolLang.Get("Display Name (RU)", "Имя (RU)"), nameRuProp.stringValue.Length, NovellaCharacter.MAX_NAME_LENGTH);
                nameRuProp.stringValue = EditorGUILayout.TextField(nameRuProp.stringValue);

                EditorGUILayout.PropertyField(_serializedObject.FindProperty("ThemeColor"), new GUIContent(ToolLang.Get("Speaker Color", "Цвет спикера")));

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);

                var isPlayerProp = _serializedObject.FindProperty("IsPlayerCharacter");
                isPlayerProp.boolValue = EditorGUILayout.ToggleLeft(ToolLang.Get("Is Main Character (Player)", "Это Главный Герой (Игрок)"), isPlayerProp.boolValue, EditorStyles.boldLabel);

                GUILayout.Space(5);
                var genderProp = _serializedObject.FindProperty("Gender");
                EditorGUILayout.PropertyField(genderProp, new GUIContent(ToolLang.Get("Gender", "Пол персонажа")));

                GUILayout.EndVertical();

                GUILayout.Space(15);
                GUILayout.Label("📚 " + ToolLang.Get("Base Layers (Paper Doll)", "Базовые слои (Кукла)"), EditorStyles.boldLabel);

                SerializedProperty baseLayersProp = _serializedObject.FindProperty("BaseLayers");
                List<string> layerNames = new List<string>();

                for (int i = 0; i < baseLayersProp.arraySize; i++)
                {
                    var layerProp = baseLayersProp.GetArrayElementAtIndex(i);
                    var layerTypeP = layerProp.FindPropertyRelative("LayerType");
                    var customNameP = layerProp.FindPropertyRelative("CustomLayerName");
                    var sprP = layerProp.FindPropertyRelative("DefaultSprite");
                    var offP = layerProp.FindPropertyRelative("Offset");
                    var sclP = layerProp.FindPropertyRelative("Scale");
                    var tintP = layerProp.FindPropertyRelative("Tint");

                    ECharacterLayer currentLayerType = (ECharacterLayer)layerTypeP.enumValueIndex;
                    string currentLayerName = currentLayerType == ECharacterLayer.Extra ? customNameP.stringValue : currentLayerType.ToString();
                    layerNames.Add(currentLayerName);

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();

                    if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22)))
                    {
                        if (i > 0)
                        {
                            baseLayersProp.MoveArrayElement(i, i - 1);
                            GUI.FocusControl(null);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }
                    if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        if (i < baseLayersProp.arraySize - 1)
                        {
                            baseLayersProp.MoveArrayElement(i, i + 1);
                            GUI.FocusControl(null);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }

                    EditorGUILayout.PropertyField(layerTypeP, GUIContent.none, GUILayout.Width(85));
                    if (currentLayerType == ECharacterLayer.Extra)
                    {
                        customNameP.stringValue = EditorGUILayout.TextField(customNameP.stringValue, GUILayout.Width(80));
                    }

                    int layerIndex = i;
                    string sprFullName = sprP.objectReferenceValue != null ? sprP.objectReferenceValue.name : ToolLang.Get("Gallery...", "Галерея...");
                    if (sprFullName.Length > 15) sprFullName = sprFullName.Substring(0, 12) + "...";

                    if (GUILayout.Button("🖼 " + sprFullName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Undo.RecordObject(_selectedCharacter, "Change Base Layer Sprite");
                            Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                            _selectedCharacter.BaseLayers[layerIndex].DefaultSprite = spr;
                            EditorUtility.SetDirty(_selectedCharacter);
                            Repaint();
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }

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

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Pos:", EditorStyles.miniLabel, GUILayout.Width(25));
                    offP.vector2Value = EditorGUILayout.Vector2Field("", offP.vector2Value, GUILayout.Width(100));
                    GUILayout.Space(5);
                    GUILayout.Label("Scl:", EditorStyles.miniLabel, GUILayout.Width(25));
                    sclP.vector2Value = EditorGUILayout.Vector2Field("", sclP.vector2Value, GUILayout.Width(100));
                    GUILayout.FlexibleSpace();
                    tintP.colorValue = EditorGUILayout.ColorField(GUIContent.none, tintP.colorValue, false, true, false, GUILayout.Width(45));
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }

                if (GUILayout.Button("+ " + ToolLang.Get("Add Layer", "Добавить слой"), EditorStyles.miniButton))
                {
                    baseLayersProp.arraySize++;
                    var newLayer = baseLayersProp.GetArrayElementAtIndex(baseLayersProp.arraySize - 1);
                    newLayer.FindPropertyRelative("LayerType").enumValueIndex = (int)ECharacterLayer.Extra;
                    newLayer.FindPropertyRelative("CustomLayerName").stringValue = "NewLayer";
                    newLayer.FindPropertyRelative("DefaultSprite").objectReferenceValue = null;
                    newLayer.FindPropertyRelative("Scale").vector2Value = Vector2.one;
                    newLayer.FindPropertyRelative("Offset").vector2Value = Vector2.zero;
                    newLayer.FindPropertyRelative("Tint").colorValue = Color.white;
                }

                GUILayout.Space(15);
                SerializedProperty emotionsProp = _serializedObject.FindProperty("Emotions");
                _showEmotions = EditorGUILayout.Foldout(_showEmotions, "😊 " + ToolLang.Get("Emotions (Overrides)", "Эмоции (Переопределение)"), true, EditorStyles.foldoutHeader);

                if (_showEmotions)
                {
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
                            var oOff = overProp.FindPropertyRelative("Offset");
                            var oScl = overProp.FindPropertyRelative("Scale");
                            var oTint = overProp.FindPropertyRelative("Tint");

                            GUILayout.BeginVertical(EditorStyles.helpBox);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("↳", GUILayout.Width(15));

                            int lIdx = layerNames.IndexOf(lName.stringValue);
                            if (lIdx == -1) lIdx = 0;

                            if (layerNames.Count > 0)
                            {
                                lIdx = EditorGUILayout.Popup(lIdx, layerNames.ToArray(), GUILayout.Width(90));
                                lName.stringValue = layerNames[lIdx];
                            }
                            else lName.stringValue = EditorGUILayout.TextField(lName.stringValue, GUILayout.Width(90));

                            int eIdx = i; int oIdx = j;
                            string overSprFullName = lSpr.objectReferenceValue != null ? lSpr.objectReferenceValue.name : ToolLang.Get("Gallery...", "Галерея...");
                            if (overSprFullName.Length > 20) overSprFullName = overSprFullName.Substring(0, 17) + "...";

                            if (GUILayout.Button("🖼 " + overSprFullName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                            {
                                NovellaGalleryWindow.ShowWindow(obj => {
                                    Undo.RecordObject(_selectedCharacter, "Change Override Sprite");
                                    Sprite spr = obj is Sprite ? (Sprite)obj : AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                                    _selectedCharacter.Emotions[eIdx].LayerOverrides[oIdx].OverrideSprite = spr;
                                    EditorUtility.SetDirty(_selectedCharacter);
                                    Repaint();
                                }, NovellaGalleryWindow.EGalleryFilter.Image);
                            }

                            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                            {
                                overridesProp.DeleteArrayElementAtIndex(j);
                                GUI.backgroundColor = Color.white;
                                GUILayout.EndHorizontal(); GUILayout.EndVertical();
                                break;
                            }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();

                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Pos:", EditorStyles.miniLabel, GUILayout.Width(25));
                            oOff.vector2Value = EditorGUILayout.Vector2Field("", oOff.vector2Value, GUILayout.Width(95));
                            GUILayout.Space(5);
                            GUILayout.Label("Scl:", EditorStyles.miniLabel, GUILayout.Width(25));
                            oScl.vector2Value = EditorGUILayout.Vector2Field("", oScl.vector2Value, GUILayout.Width(95));
                            GUILayout.FlexibleSpace();
                            oTint.colorValue = EditorGUILayout.ColorField(GUIContent.none, oTint.colorValue, false, true, false, GUILayout.Width(45));
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                        }

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(35);
                        if (GUILayout.Button("+ " + ToolLang.Get("Add Layer Override", "Переопределить слой"), EditorStyles.miniButton))
                        {
                            overridesProp.arraySize++;
                            var newOver = overridesProp.GetArrayElementAtIndex(overridesProp.arraySize - 1);
                            newOver.FindPropertyRelative("LayerName").stringValue = layerNames.Count > 0 ? layerNames[0] : "Base";
                            newOver.FindPropertyRelative("OverrideSprite").objectReferenceValue = null;
                            newOver.FindPropertyRelative("Scale").vector2Value = Vector2.one;
                            newOver.FindPropertyRelative("Offset").vector2Value = Vector2.zero;
                            newOver.FindPropertyRelative("Tint").colorValue = Color.white;
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

                if (EditorGUI.EndChangeCheck())
                {
                    _serializedObject.ApplyModifiedProperties();
                    string path = AssetDatabase.GetAssetPath(_selectedCharacter);

                    // === АВТО-ПЕРЕМЕЩЕНИЕ В RESOURCES/CHARACTERS ===
                    if (_selectedCharacter.IsPlayerCharacter)
                    {
                        string targetFolder = "Assets/NovellaEngine/Resources/Characters";
                        if (!AssetDatabase.IsValidFolder(targetFolder))
                        {
                            System.IO.Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Resources/Characters");
                            AssetDatabase.Refresh();
                        }

                        if (!string.IsNullOrEmpty(path) && !path.StartsWith(targetFolder))
                        {
                            string newPath = targetFolder + "/" + _selectedCharacter.name + ".asset";
                            string error = AssetDatabase.MoveAsset(path, newPath);

                            if (string.IsNullOrEmpty(error))
                            {
                                path = newPath;
                                Debug.Log($"[Novella Engine] Персонаж {_selectedCharacter.name} автоматически перемещен в папку Resources/Characters!");
                            }
                        }
                    }
                    // ===============================================

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

        private void DrawPaperDollPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedCharacter != null)
            {
                GUILayout.Label("🎭 " + ToolLang.Get("Live Paper Doll Preview", "Живой предпросмотр (Кукла)"), EditorStyles.largeLabel);
                GUILayout.Space(10);

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

                Color oldColor = GUI.color;
                for (int i = _selectedCharacter.BaseLayers.Count - 1; i >= 0; i--)
                {
                    var layer = _selectedCharacter.BaseLayers[i];
                    if (layer.DefaultSprite == null) continue;

                    Texture2D tex = layer.DefaultSprite.texture;
                    Rect sRect = layer.DefaultSprite.rect;

                    float drawW = sRect.width * _previewZoom * layer.Scale.x;
                    float drawH = sRect.height * _previewZoom * layer.Scale.y;

                    float centerX = previewRect.width / 2f + _previewPan.x + (layer.Offset.x * _previewZoom);
                    float centerY = previewRect.height / 2f + _previewPan.y - (layer.Offset.y * _previewZoom);

                    Rect destRect = new Rect(centerX - drawW / 2f, centerY - drawH / 2f, drawW, drawH);
                    Rect uvRect = new Rect(sRect.x / tex.width, sRect.y / tex.height, sRect.width / tex.width, sRect.height / tex.height);

                    GUI.color = layer.Tint;
                    GUI.DrawTextureWithTexCoords(destRect, tex, uvRect, true);
                }
                GUI.color = oldColor;

                GUI.EndGroup();

                GUILayout.Space(10);
                GUILayout.Label(ToolLang.Get("RMB - Pan | Scroll - Zoom | Double Click - Reset", "ПКМ - Двигать | Колесико - Зум | Двойной клик - Сброс камеры"), EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a character to view.", "Выберите персонажа для предпросмотра."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
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

            if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Runtime/Data/Characters"))
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Runtime/Data/Characters");
                AssetDatabase.Refresh();
            }

            string path = $"Assets/NovellaEngine/Runtime/Data/Characters/{newChar.CharacterID}.asset";
            AssetDatabase.CreateAsset(newChar, path);
            AssetDatabase.SaveAssets();

            SelectCharacter(newChar);
            _searchQuery = "";
            _needsRefresh = true;
        }
    }
}