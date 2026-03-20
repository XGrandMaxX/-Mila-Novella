using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaCharacterSelectorWindow : EditorWindow
    {
        private Action<NovellaCharacter> _onSelect;
        private List<NovellaCharacter> _characters;
        private Vector2 _scroll;
        private bool _isGridView = false;

        public static void ShowWindow(Action<NovellaCharacter> onSelect)
        {
            var window = GetWindow<NovellaCharacterSelectorWindow>(true, ToolLang.Get("Select Character", "Выберите персонажа"), true);
            window.titleContent = new GUIContent(ToolLang.Get("Select Character", "Выберите персонажа"));
            window._onSelect = onSelect;
            window.minSize = new Vector2(300, 400);
            window.LoadCharacters();
            window.ShowUtility();
        }

        private void LoadCharacters()
        {
            _characters = new List<NovellaCharacter>();
            string[] guids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ch = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(path);
                if (ch != null) _characters.Add(ch);
            }
            _characters = _characters.OrderBy(c => c.name).ToList();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ToolLang.Get("❌ None (Clear)", "❌ Убрать персонажа"), GUILayout.Height(30), GUILayout.ExpandWidth(true)))
            {
                _onSelect?.Invoke(null);
                Close();
            }
            if (GUILayout.Button(_isGridView ? "🔲" : "📄", GUILayout.Width(40), GUILayout.Height(30))) { _isGridView = !_isGridView; }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            _scroll = GUILayout.BeginScrollView(_scroll);

            var favorites = _characters.Where(c => c.IsFavorite).ToList();
            if (favorites.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("⭐ FAVORITES", "⭐ ИЗБРАННЫЕ"), EditorStyles.boldLabel);
                DrawCollection(favorites);
                GUILayout.Space(15);
            }

            var others = _characters.Where(c => !c.IsFavorite).ToList();
            if (others.Count > 0)
            {
                GUILayout.Label(ToolLang.Get("📝 ALL CHARACTERS", "📝 ВСЕ ПЕРСОНАЖИ"), EditorStyles.boldLabel);
                DrawCollection(others);
            }

            GUILayout.EndScrollView();
        }

        private void DrawCollection(List<NovellaCharacter> list)
        {
            if (_isGridView)
            {
                int cols = 3;
                int current = 0;
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();

                foreach (var character in list)
                {
                    float size = 85f;
                    Rect rect = GUILayoutUtility.GetRect(size, size);

                    if (GUI.Button(rect, GUIContent.none, GUI.skin.button)) { _onSelect?.Invoke(character); Close(); }

                    GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 12 };
                    GUI.Label(new Rect(rect.x + 2, rect.y, rect.width - 24, rect.height), character.name, nameStyle);

                    GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = character.IsFavorite ? Color.yellow : Color.gray } };
                    GUI.Label(new Rect(rect.x + rect.width - 26, rect.y + (rect.height / 2f) - 12f, 24, 24), character.IsFavorite ? "★" : "☆", starStyle);

                    current++;
                    if (current >= cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); current = 0; }
                }
                GUILayout.EndHorizontal(); GUILayout.EndVertical();
            }
            else
            {
                foreach (var character in list)
                {
                    GUILayout.BeginHorizontal(GUI.skin.button);
                    if (GUILayout.Button(character.name, EditorStyles.label, GUILayout.ExpandWidth(true), GUILayout.Height(24)))
                    { _onSelect?.Invoke(character); Close(); }

                    GUIStyle starStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = character.IsFavorite ? Color.yellow : Color.gray } };
                    GUILayout.Label(character.IsFavorite ? "★" : "☆", starStyle, GUILayout.Width(25), GUILayout.Height(25));

                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}