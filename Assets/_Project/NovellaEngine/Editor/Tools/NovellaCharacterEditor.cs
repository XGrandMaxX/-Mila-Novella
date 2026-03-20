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

        [MenuItem("Tools/Novella Engine/Character Editor")]
        public static void OpenWindow()
        {
            var win = GetWindow<NovellaCharacterEditor>(ToolLang.Get("Character Editor", "Редактор Персонажей"));
            win.minSize = new Vector2(600, 400);
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
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Delete)
            {
                if (_selectedCharacter != null)
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete Character?", "Удалить персонажа?"),
                        ToolLang.Get($"Are you sure you want to delete '{_selectedCharacter.name}'?", $"Вы уверены, что хотите удалить '{_selectedCharacter.name}'?"),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        string path = AssetDatabase.GetAssetPath(_selectedCharacter);
                        AssetDatabase.DeleteAsset(path);
                        _selectedCharacter = null;
                        _needsRefresh = true;
                        Event.current.Use();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            GUILayout.EndHorizontal();
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
                int cols = 3;
                int current = 0;
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                foreach (var character in list)
                {
                    if (character == null) continue;
                    DrawCharacterGridItem(character);
                    current++;
                    if (current >= cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); current = 0; }
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
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

            if (GUILayout.Button(character.name, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(24))) { _selectedCharacter = character; GUI.FocusControl(null); }

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
            if (GUI.Button(rect, GUIContent.none, GUI.skin.button)) { _selectedCharacter = character; GUI.FocusControl(null); }
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

        private void DrawRightPanel()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

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

                EditorGUILayout.PropertyField(_serializedObject.FindProperty("CharacterID"), new GUIContent("Character ID"));
                GUILayout.Space(5);

                EditorGUILayout.PropertyField(_serializedObject.FindProperty("DisplayName_EN"), new GUIContent(ToolLang.Get("Display Name (EN)", "Имя (EN)")));
                EditorGUILayout.PropertyField(_serializedObject.FindProperty("DisplayName_RU"), new GUIContent(ToolLang.Get("Display Name (RU)", "Имя (RU)")));
                EditorGUILayout.PropertyField(_serializedObject.FindProperty("ThemeColor"), new GUIContent(ToolLang.Get("Theme Color", "Цвет темы")));

                GUILayout.Space(10);
                GUILayout.Label(ToolLang.Get("Visuals", "Визуализация"), EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_serializedObject.FindProperty("DefaultSprite"), new GUIContent(ToolLang.Get("Default Sprite", "Стандартный спрайт")));
                EditorGUILayout.PropertyField(_serializedObject.FindProperty("Emotions"), new GUIContent(ToolLang.Get("Emotions", "Эмоции")), true);

                GUILayout.Space(10);
                EditorGUILayout.PropertyField(_serializedObject.FindProperty("InternalNotes"), new GUIContent(ToolLang.Get("Internal Notes", "Внутренние заметки")));

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
                        _selectedCharacter = null;
                        _needsRefresh = true;
                        GUIUtility.ExitGUI();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndVertical();
        }

        private void CreateNewCharacter()
        {
            NovellaCharacter newChar = ScriptableObject.CreateInstance<NovellaCharacter>();
            string newId = "NewCharacter_" + System.Guid.NewGuid().ToString().Substring(0, 4);
            while (_characters.Any(c => c.CharacterID == newId)) newId = "NewCharacter_" + System.Guid.NewGuid().ToString().Substring(0, 4);
            newChar.CharacterID = newId;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/NovellaEngine/Runtime/Data/Characters"))
            {
                System.IO.Directory.CreateDirectory(Application.dataPath + "/_Project/NovellaEngine/Runtime/Data/Characters");
                AssetDatabase.Refresh();
            }

            string path = $"Assets/_Project/NovellaEngine/Runtime/Data/Characters/{newChar.CharacterID}.asset";
            AssetDatabase.CreateAsset(newChar, path);
            AssetDatabase.SaveAssets();

            _selectedCharacter = newChar;
            _searchQuery = "";
            _needsRefresh = true;
        }
    }

    [CustomPropertyDrawer(typeof(CharacterEmotion))]
    public class CharacterEmotionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var nameProp = property.FindPropertyRelative("EmotionName");
            var spriteProp = property.FindPropertyRelative("EmotionSprite");

            var nameRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var spriteRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2, position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(nameRect, nameProp, new GUIContent(ToolLang.Get("Emotion Name", "Название эмоции")));
            EditorGUI.PropertyField(spriteRect, spriteProp, new GUIContent(ToolLang.Get("Emotion Sprite", "Спрайт эмоции")));

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) { return EditorGUIUtility.singleLineHeight * 2 + 4; }
    }
}