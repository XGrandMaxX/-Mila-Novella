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

            GUILayout.Space(10);
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            GUILayout.Label("🕸️ NOVELLA GRAPH DATA", headerStyle);
            GUILayout.Space(10);

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button(ToolLang.Get("🛠️ OPEN GRAPH EDITOR", "🛠️ ОТКРЫТЬ РЕДАКТОР ГРАФА"), GUILayout.Height(40)))
            {
                NovellaGraphWindow.OpenGraphWindow(tree);
            }

            GUI.backgroundColor = Color.white;
            GUILayout.Space(10);
        }
    }
}