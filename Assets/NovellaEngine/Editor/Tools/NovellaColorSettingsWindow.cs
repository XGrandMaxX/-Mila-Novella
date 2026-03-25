using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using System.Linq;
using System.Collections.Generic;
using System;

namespace NovellaEngine.Editor
{
    public class NovellaColorSettingsWindow : EditorWindow
    {
        private ENodeType _selectedType = ENodeType.End;
        private string _selectedDLC_ID = "";
        private Vector2 _scrollPos;

        private List<Type> _dlcTypes = new List<Type>();

        public static void ShowWindow()
        {
            var window = GetWindow<NovellaColorSettingsWindow>("Node Colors");
            window.minSize = new Vector2(500, 450);
            window.Show();
        }

        private void OnEnable()
        {
            _dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0)
                .ToList();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🎨 " + ToolLang.Get("Reserved Colors Setting", "Настройка системных цветов"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(220), GUILayout.ExpandHeight(true));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            DrawTabBtn(ENodeType.End, "🛑", ToolLang.Get("End Scene", "Конец Сцены"));
            DrawTabBtn(ENodeType.Branch, "🔀", ToolLang.Get("Branch", "Развилка"));
            DrawTabBtn(ENodeType.Condition, "❓", ToolLang.Get("Condition", "Условие (If/Else)"));
            DrawTabBtn(ENodeType.Random, "🎲", ToolLang.Get("Random", "Случайность"));
            DrawTabBtn(ENodeType.Variable, "📊", ToolLang.Get("Variable", "Логика / Перем."));

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Cinematography", "Режиссура"), EditorStyles.miniBoldLabel);
            DrawTabBtn(ENodeType.SceneSettings, "🖼", ToolLang.Get("Background", "Фон / Сцена"));
            DrawTabBtn(ENodeType.Audio, "🎵", ToolLang.Get("Audio", "Аудио"));
            DrawTabBtn(ENodeType.Animation, "✨", ToolLang.Get("Animation", "Анимация"));
            DrawTabBtn(ENodeType.Wait, "⏳", ToolLang.Get("Wait (Delay)", "Ожидание (Пауза)"));

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("System", "Система"), EditorStyles.miniBoldLabel);
            DrawTabBtn(ENodeType.EventBroadcast, "⚡", ToolLang.Get("Event Broadcast", "Вызов События"));

            DrawTabBtn(ENodeType.Save, "💾", ToolLang.Get("Save Checkpoint", "Сохранение"));

            if (_dlcTypes.Count > 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(ToolLang.Get("🧩 DLC Modules", "🧩 Модули DLC"), EditorStyles.centeredGreyMiniLabel);

                foreach (var dlc in _dlcTypes)
                {
                    var attr = DLCCache.GetNodeAttribute(dlc);
                    if (attr != null)
                    {
                        DrawDLCTabBtn(dlc.FullName, "🧩", attr.MenuName);
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.Label(ToolLang.Get("Node Preview", "Предпросмотр Ноды"), EditorStyles.boldLabel);
            GUILayout.Space(20);

            Color currentColor = string.IsNullOrEmpty(_selectedDLC_ID)
                ? GetNodeColor(_selectedType)
                : GetDLCNodeColor(_selectedDLC_ID);

            GUILayout.BeginHorizontal(GUILayout.Height(40));
            GUILayout.FlexibleSpace();

            GUIStyle nodeStyle = new GUIStyle(GUI.skin.box);
            nodeStyle.normal.background = Texture2D.whiteTexture;

            GUI.backgroundColor = currentColor;
            GUILayout.BeginVertical(nodeStyle, GUILayout.Height(40), GUILayout.Width(200));
            GUI.backgroundColor = Color.white;

            string title = "";
            if (string.IsNullOrEmpty(_selectedDLC_ID))
            {
                title = _selectedType.ToString().ToUpper();
                if (_selectedType == ENodeType.Condition) title = "CONDITION (IF)";
                else if (_selectedType == ENodeType.EventBroadcast) title = "EVENT BROADCAST";
            }
            else
            {
                var dlcType = _dlcTypes.FirstOrDefault(t => t.FullName == _selectedDLC_ID);
                if (dlcType != null)
                {
                    var attr = DLCCache.GetNodeAttribute(dlcType);
                    title = attr != null ? attr.MenuName.ToUpper() : "DLC NODE";
                }
            }

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, fontSize = 14 }, GUILayout.ExpandHeight(true));

            GUILayout.EndVertical();

            if (string.IsNullOrEmpty(_selectedDLC_ID) && _selectedType == ENodeType.Condition)
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
                if (string.IsNullOrEmpty(_selectedDLC_ID)) SetNodeColor(_selectedType, newColor);
                else SetDLCNodeColor(_selectedDLC_ID, newColor);

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
            bool isSelected = string.IsNullOrEmpty(_selectedDLC_ID) && _selectedType == type;
            GUI.backgroundColor = isSelected ? new Color(0.4f, 0.6f, 1f) : Color.white;

            GUIStyle btnStyle = new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontSize = 13, fixedHeight = 25 };
            if (isSelected) btnStyle.normal.textColor = Color.white;

            if (GUILayout.Button($"{icon} {label}", btnStyle))
            {
                _selectedType = type;
                _selectedDLC_ID = "";
                GUI.FocusControl(null);
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawDLCTabBtn(string dlcID, string icon, string label)
        {
            bool isSelected = _selectedDLC_ID == dlcID;
            GUI.backgroundColor = isSelected ? new Color(0.4f, 0.6f, 1f) : Color.white;

            GUIStyle btnStyle = new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontSize = 13, fixedHeight = 25 };
            if (isSelected) btnStyle.normal.textColor = Color.white;

            if (GUILayout.Button($"{icon} {label}", btnStyle))
            {
                _selectedDLC_ID = dlcID;
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
                case ENodeType.Wait: defaultHex = "#455A64"; break;
                case ENodeType.SceneSettings: defaultHex = "#3A5C74"; break;
                case ENodeType.Animation: defaultHex = "#963E56"; break;
                case ENodeType.EventBroadcast: defaultHex = "#A88522"; break;
                case ENodeType.Save: defaultHex = "#2E7D32"; break;
            }

            string hex = EditorPrefs.GetString("NovellaColor_" + type.ToString(), defaultHex);
            if (ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
            return Color.gray;
        }

        public static Color GetDLCNodeColor(string dlcFullName)
        {
            string defaultHex = "#4A4A4A";
            var dlcType = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>().FirstOrDefault(t => t.FullName == dlcFullName);
            if (dlcType != null)
            {
                var attr = DLCCache.GetNodeAttribute(dlcType);
                if (attr != null && !string.IsNullOrEmpty(attr.HexColor)) defaultHex = attr.HexColor;
            }

            string hex = EditorPrefs.GetString("NovellaColor_" + dlcFullName, defaultHex);
            if (ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
            return Color.gray;
        }

        private void SetNodeColor(ENodeType type, Color color)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGBA(color);
            EditorPrefs.SetString("NovellaColor_" + type.ToString(), hex);
        }

        private void SetDLCNodeColor(string dlcFullName, Color color)
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGBA(color);
            EditorPrefs.SetString("NovellaColor_" + dlcFullName, hex);
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
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Wait.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.SceneSettings.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Animation.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.EventBroadcast.ToString());
            EditorPrefs.DeleteKey("NovellaColor_" + ENodeType.Save.ToString());


            if (_dlcTypes != null)
            {
                foreach (var dlc in _dlcTypes)
                {
                    EditorPrefs.DeleteKey("NovellaColor_" + dlc.FullName);
                }
            }

            UpdateGraphSafely();
        }
    }
}