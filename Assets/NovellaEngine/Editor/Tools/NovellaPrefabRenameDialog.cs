// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabRenameDialog — компактное окно переименования префаба.
// Стилистически — как NovellaPrefabCreateDialog (тот же фрейм, акцентная
// кнопка подтверждения, счётчик символов 30 max).
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaPrefabRenameDialog : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private const int MAX_NAME = 30;

        private string _name = "";
        private string _originalName = "";
        private Action<string> _onRename;
        private bool _focused;

        public static void Open(string assetPath, Action<string> onRename)
        {
            // Single-instance.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaPrefabRenameDialog>())
            {
                if (existing != null) existing.Close();
            }

            var win = CreateInstance<NovellaPrefabRenameDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Rename prefab", "Переименовать префаб"));
            win._originalName = Path.GetFileNameWithoutExtension(assetPath);
            win._name = win._originalName;
            win._onRename = onRename;

            var size = new Vector2(420, 180);
            var main = EditorGUIUtility.GetMainWindowPosition();
            win.position = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x, size.y);
            win.minSize = size; win.maxSize = size;
            win.ShowUtility();
            win.Focus();
        }

        private void OnGUI()
        {
            // Esc — всегда закрывает.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("✎ " + ToolLang.Get("Rename prefab", "Переименовать префаб"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(12);

            // Поле ввода.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var lblSt = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            lblSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get("New name:", "Новое имя:"), lblSt, GUILayout.Width(100));
            GUI.SetNextControlName("PrefabRenameField");
            _name = EditorGUILayout.TextField(_name, GUILayout.Height(22));
            if (_name != null && _name.Length > MAX_NAME) _name = _name.Substring(0, MAX_NAME);
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            if (!_focused)
            {
                EditorGUI.FocusTextInControl("PrefabRenameField");
                _focused = true;
            }

            // Счётчик.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            int cur = _name?.Length ?? 0;
            var counterSt = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 10 };
            counterSt.normal.textColor = cur >= MAX_NAME
                ? new Color(0.92f, 0.36f, 0.36f)
                : NovellaSettingsModule.GetTextDisabled();
            GUILayout.Label($"{cur}/{MAX_NAME}", counterSt, GUILayout.Width(50));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);

            // Footer.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);

            var cancelSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 26, padding = new RectOffset(16, 16, 2, 2) };
            cancelSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(ToolLang.Get("Cancel", "Отмена"), cancelSt, GUILayout.Width(100)))
            {
                Close();
            }

            GUILayout.FlexibleSpace();

            bool nameOk = !string.IsNullOrWhiteSpace(_name) && _name != _originalName;
            using (new EditorGUI.DisabledScope(!nameOk))
            {
                GUI.backgroundColor = C_ACCENT;
                if (GUILayout.Button("✎ " + ToolLang.Get("Rename", "Переименовать"),
                        GUILayout.Width(160), GUILayout.Height(26)))
                {
                    var cb = _onRename;
                    string n = _name;
                    Close();
                    cb?.Invoke(n);
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);

            // Enter подтверждает (если имя валидно).
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && nameOk)
            {
                var cb = _onRename;
                string n = _name;
                Close();
                cb?.Invoke(n);
                Event.current.Use();
            }
        }
    }
}
