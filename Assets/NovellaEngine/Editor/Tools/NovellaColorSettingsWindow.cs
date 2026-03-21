using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaColorSettingsWindow : EditorWindow
    {
        private ENodeType _selectedType = ENodeType.End;
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            // Убрали привязку typeof(NovellaGraphWindow)
            var window = GetWindow<NovellaColorSettingsWindow>("Node Colors");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🎨 " + ToolLang.Get("Reserved Colors Setting", "Настройка системных цветов"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            // === ЛЕВАЯ ПАНЕЛЬ ===
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(200), GUILayout.ExpandHeight(true));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            DrawTabBtn(ENodeType.End, "🛑", ToolLang.Get("End Scene", "Конец Сцены"));
            DrawTabBtn(ENodeType.Branch, "🔀", ToolLang.Get("Branch", "Развилка"));
            DrawTabBtn(ENodeType.Condition, "❓", ToolLang.Get("Condition", "Условие (If/Else)"));
            DrawTabBtn(ENodeType.Random, "🎲", ToolLang.Get("Random", "Случайность"));
            DrawTabBtn(ENodeType.Audio, "🎵", ToolLang.Get("Audio", "Аудио"));
            DrawTabBtn(ENodeType.Variable, "📊", ToolLang.Get("Variable", "Логика / Перем."));

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // === ПРАВАЯ ПАНЕЛЬ ===
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.Label(ToolLang.Get("Node Preview", "Предпросмотр Ноды"), EditorStyles.boldLabel);
            GUILayout.Space(20);

            Color currentColor = GetNodeColor(_selectedType);

            GUILayout.BeginHorizontal(GUILayout.Height(40));
            GUILayout.FlexibleSpace();

            GUIStyle nodeStyle = new GUIStyle(GUI.skin.box);
            nodeStyle.normal.background = Texture2D.whiteTexture;

            GUI.backgroundColor = currentColor;
            GUILayout.BeginVertical(nodeStyle, GUILayout.Height(40), GUILayout.Width(200));
            GUI.backgroundColor = Color.white;

            string title = _selectedType.ToString().ToUpper();
            if (_selectedType == ENodeType.Condition) title = "CONDITION (IF)";

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, fontSize = 14 }, GUILayout.ExpandHeight(true));

            GUILayout.EndVertical();

            if (_selectedType == ENodeType.Condition)
            {
                Rect lastRect = GUILayoutUtility.GetLastRect();
                Rect lineRect = new Rect(lastRect.x, lastRect.yMax - 3, lastRect.width, 3);
                EditorGUI.DrawRect(lineRect, new Color(1f, 0.7f, 0f));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(40);

            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField(new GUIContent(ToolLang.Get("Edit Color", "Изменить цвет")), currentColor, true, true, false);
            if (EditorGUI.EndChangeCheck())
            {
                SetNodeColor(_selectedType, newColor);
                UpdateGraphSafely();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ " + ToolLang.Get("Reset All to Defaults", "Сбросить все цвета по умолчанию"), GUILayout.Height(30)))
            {
                ResetToDefaults();
            }

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawTabBtn(ENodeType type, string icon, string label)
        {
            bool isSelected = _selectedType == type;
            GUI.backgroundColor = isSelected ? new Color(0.4f, 0.6f, 1f) : Color.white;

            GUIStyle btnStyle = new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontSize = 13, fixedHeight = 25 };
            if (isSelected) btnStyle.normal.textColor = Color.white;

            if (GUILayout.Button($"{icon} {label}", btnStyle))
            {
                _selectedType = type;
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = Color.white;
        }

        public static Color GetNodeColor(ENodeType type)
        {
            string defaultHex = "#333333";
            switch (type)
            {
                case ENodeType.End: defaultHex = "#8A2E2E"; break;
                case ENodeType.Branch: defaultHex = "#A86022"; break;
                case ENodeType.Condition: defaultHex = "#A86022"; break;
                case ENodeType.Random: defaultHex = "#623B8A"; break;
                case ENodeType.Audio: defaultHex = "#2A8272"; break;
                case ENodeType.Variable: defaultHex = "#307D50"; break;
            }

            string hex = EditorPrefs.GetString("NovellaColor_" + type.ToString(), defaultHex);
            if (ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
            return Color.gray;
        }

        private void SetNodeColor(ENodeType type, Color color)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGBA(color);
            EditorPrefs.SetString("NovellaColor_" + type.ToString(), hex);
        }

        private void UpdateGraphSafely()
        {
            var graphWindows = Resources.FindObjectsOfTypeAll<NovellaGraphWindow>();
            if (graphWindows != null && graphWindows.Length > 0)
            {
                graphWindows[0].RefreshAllNodes();
                graphWindows[0].Repaint();
            }
        }

        private void ResetToDefaults()
        {
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.End.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Branch.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Condition.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Random.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Audio.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Variable.ToString());

            UpdateGraphSafely();
        }
    }
}