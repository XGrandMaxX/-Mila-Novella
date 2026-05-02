// ════════════════════════════════════════════════════════════════════════════
// NovellaUIBindingEditor
//
// Минималистичный инспектор для NovellaUIBinding в Unity. Все настройки
// (Display name, Localization key, Variable, On-click) редактируются в
// «Кузнице UI» (Novella Studio). Этот инспектор только показывает сводку и
// предлагает быстро открыть Forge на нужном элементе.
//
// Идея: пользователь не должен заходить в Unity-инспектор вообще. Если он туда
// попал случайно — мы аккуратно отправляем его обратно в Studio.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    [CustomEditor(typeof(NovellaUIBinding))]
    public class NovellaUIBindingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var b = (NovellaUIBinding)target;

            // Заголовок-плашка
            var rect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.36f, 0.75f, 0.92f, 0.10f));
            DrawBorder(rect, new Color(0.36f, 0.75f, 0.92f, 0.45f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3, rect.height), new Color(0.36f, 0.75f, 0.92f));

            var ttlSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 18), "🎨 " + (string.IsNullOrEmpty(b.Name) ? b.gameObject.name : b.Name));

            var subSt = new GUIStyle(EditorStyles.miniLabel);
            subSt.normal.textColor = new Color(0.62f, 0.63f, 0.69f);
            string kindLbl = KindLabel(b);
            GUI.Label(new Rect(rect.x + 12, rect.y + 20, rect.width - 24, 14), kindLbl, subSt);

            EditorGUILayout.Space(8);

            // Сводка (read-only) — настраивать здесь нельзя, чтобы не было двух
            // мест редактирования. Источник истины — Кузница UI.
            var sumSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            sumSt.normal.textColor = new Color(0.71f, 0.72f, 0.78f);
            EditorGUILayout.LabelField("🔑 " + Lang("Localization key", "Ключ локализации") + ": " + Or(b.LocalizationKey), sumSt);
            EditorGUILayout.LabelField("📊 " + Lang("Variable", "Переменная") + ": " + Or(b.BoundVariable), sumSt);
            EditorGUILayout.LabelField("➡  " + Lang("On click", "По клику") + ": " + Or(b.OnClickGotoNodeId), sumSt);

            EditorGUILayout.Space(8);

            var infoSt = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = true };
            EditorGUILayout.LabelField(Lang(
                "Configure this binding in the UI Forge — that's the single source of truth. Avoid editing values from Unity inspector to prevent confusion.",
                "Настраивай эту связь в Кузнице UI — там единственный источник правды. Не редактируй значения из инспектора Unity, чтобы не путаться."),
                infoSt);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("🎨  " + Lang("Open in UI Forge", "Открыть в Кузнице UI"), GUILayout.Height(28)))
            {
                Selection.activeGameObject = b.gameObject;
                // ShowWindow на NovellaUIForge через рефлексию, чтобы избежать
                // прямой ссылки из Editor.UI на Editor.Tools (ассембли одинаковые,
                // но логически держим разделение чистым).
                var t = System.Type.GetType("NovellaEngine.Editor.NovellaUIForge,Assembly-CSharp-Editor");
                if (t != null)
                {
                    var m = t.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    m?.Invoke(null, null);
                }
            }
        }

        private static string KindLabel(NovellaUIBinding b)
        {
            if (b.GetComponent<TMP_Text>() != null) return "📝 " + Lang("Text element", "Текстовый элемент");
            if (b.GetComponent<Button>()   != null) return "🔘 " + Lang("Button", "Кнопка");
            if (b.GetComponent<Image>()    != null) return "🖼 " + Lang("Image", "Картинка");
            return "▣ " + Lang("UI element", "UI элемент");
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "—" : s;

        // Локально дублируем ToolLang.Get чтобы не тянуть зависимость на тулзы
        // из узкоспециализированного редактора компонента.
        private static string Lang(string en, string ru)
        {
            try
            {
                var t = System.Type.GetType("NovellaEngine.Editor.ToolLang,Assembly-CSharp-Editor");
                if (t != null)
                {
                    var m = t.GetMethod("Get", new[] { typeof(string), typeof(string) });
                    if (m != null) return (string)m.Invoke(null, new object[] { en, ru });
                }
            }
            catch { }
            return en;
        }
    }
}
