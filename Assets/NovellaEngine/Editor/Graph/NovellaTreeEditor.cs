using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    [CustomEditor(typeof(NovellaTree))]
    public class NovellaTreeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NovellaTree tree = (NovellaTree)target;

            GUILayout.Space(12);

            // Заголовок: «📖 Глава истории» (более понятное чем «GRAPH DATA»).
            var titleSt = new GUIStyle(EditorStyles.boldLabel) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                normal = { textColor = NovellaSettingsModule.GetTextColor() }
            };
            GUILayout.Label("📖  " + ToolLang.Get("Story chapter", "Глава истории"), titleSt);

            // Подзаголовок с количеством нод (приземляет «что это вообще»).
            int nodes = tree.Nodes != null ? tree.Nodes.Count : 0;
            var subSt = new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = NovellaSettingsModule.GetTextMuted() }
            };
            GUILayout.Label(string.Format(ToolLang.Get("{0} nodes", "{0} нод"), nodes), subSt);

            GUILayout.Space(10);

            // Accent-кнопка в стиле Hub: cyan-fill (а не зелёный backgroundColor-хак).
            // Высота 36 — подгоняем под Hub-стиль (раньше было 40 + GUI.backgroundColor).
            if (NovellaSettingsModule.AccentButton(
                "🛠  " + ToolLang.Get("Open graph editor", "Открыть редактор графа"),
                GUILayout.Height(36)))
            {
                NovellaGraphWindow.OpenGraphWindow(tree);
            }

            GUILayout.Space(10);
        }
    }
}