// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabHistoryDialog — окно просмотра истории создания/изменения
// префабов. Read-only, без возможности очистки (по дизайн-требованию).
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaPrefabHistoryDialog : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private List<NovellaPrefabHistory.Entry> _entries;
        private Vector2 _scroll;

        public static void Show()
        {
            var win = CreateInstance<NovellaPrefabHistoryDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Prefab history", "История префабов"));
            win._entries = NovellaPrefabHistory.ReadAll();

            var size = new Vector2(640, 480);
            var main = EditorGUIUtility.GetMainWindowPosition();
            win.position = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x, size.y);
            win.minSize = size;
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📜 " + ToolLang.Get("Prefab history", "История префабов"), titleSt);
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Append-only log of all prefab actions in Gallery/Prefabs. Cannot be cleared.",
                "Журнал всех действий с префабами в Gallery/Prefabs. Append-only, очистить нельзя."), subSt);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);

            if (_entries == null || _entries.Count == 0)
            {
                GUILayout.Space(40);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 12 };
                emptySt.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get("No history yet.", "Истории пока нет."), emptySt);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
                // Новые сверху.
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    DrawEntry(_entries[i], _entries.Count - i);
                }
                GUILayout.EndScrollView();
            }

            // Footer.
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var closeSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 24, padding = new RectOffset(16, 16, 2, 2) };
            closeSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), closeSt, GUILayout.Width(100))) Close();
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }

        private void DrawEntry(NovellaPrefabHistory.Entry e, int displayIdx)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect row = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, displayIdx % 2 == 0 ? new Color(1, 1, 1, 0.02f) : Color.clear);

            // Action icon + tag.
            (string icon, Color col) = e.Action switch
            {
                "create" => ("✨", new Color(0.40f, 0.78f, 0.45f)),
                "save"   => ("💾", new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b)),
                "rename" => ("✎",  new Color(0.96f, 0.86f, 0.53f)),
                "delete" => ("🗑", new Color(0.92f, 0.36f, 0.36f)),
                _        => ("·",  C_TEXT_3),
            };
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = col;
            GUI.Label(new Rect(row.x + 4, row.y, 22, row.height), icon, iconSt);

            // Action label + name.
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.LowerLeft };
            nameSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(row.x + 30, row.y + 2, 360, 18), e.PrefabName, nameSt);

            // Type + path.
            var sub = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            sub.normal.textColor = C_TEXT_4;
            string subLabel = string.IsNullOrEmpty(e.PrefabType) ? e.Path : (e.PrefabType + "  ·  " + e.Path);
            GUI.Label(new Rect(row.x + 30, row.y + 18, 460, 14), subLabel, sub);

            // Timestamp.
            var timeSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleRight };
            timeSt.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(row.xMax - 156, row.y, 150, row.height), e.Timestamp, timeSt);
        }
    }
}
