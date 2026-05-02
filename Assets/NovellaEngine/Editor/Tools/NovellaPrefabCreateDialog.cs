// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabCreateDialog — модальное окно создания нового префаба.
// Юзер выбирает тип (Button / Panel / Image / Text) и вводит имя.
// При подтверждении генерируется минимальный prefab в Gallery/Prefabs/
// и сохраняется в файле истории.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor
{
    public class NovellaPrefabCreateDialog : EditorWindow
    {
        public enum PrefabType { Button, Panel, Image, Text }

        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private PrefabType _type = PrefabType.Button;
        private string _name = "";
        private Action<GameObject> _onCreated;

        // Имя «Open», а не «Show» — иначе скрывает базовый EditorWindow.Show().
        public static void Open(Action<GameObject> onCreated)
        {
            var win = CreateInstance<NovellaPrefabCreateDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Create prefab", "Создать префаб"));
            win._onCreated = onCreated;

            var size = new Vector2(440, 280);
            var main = EditorGUIUtility.GetMainWindowPosition();
            win.position = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x, size.y);
            win.minSize = size; win.maxSize = size;
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("✨ " + ToolLang.Get("New prefab", "Новый префаб"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(12);

            // Type selector — 4 кнопки рядом.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            DrawTypeButton(PrefabType.Button, "🔘", ToolLang.Get("Button", "Кнопка"));
            GUILayout.Space(6);
            DrawTypeButton(PrefabType.Panel,  "▣",  ToolLang.Get("Panel",  "Панель"));
            GUILayout.Space(6);
            DrawTypeButton(PrefabType.Image,  "🖼",  ToolLang.Get("Image",  "Картинка"));
            GUILayout.Space(6);
            DrawTypeButton(PrefabType.Text,   "📝", ToolLang.Get("Text",   "Текст"));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(14);

            // Name field.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var lblSt = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            lblSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get("Prefab name:", "Имя префаба:"), lblSt, GUILayout.Width(100));
            _name = EditorGUILayout.TextField(_name, GUILayout.Height(22));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            hintSt.normal.textColor = NovellaSettingsModule.GetTextDisabled();
            GUILayout.Label(string.Format(ToolLang.Get(
                "Saved to {0}",
                "Сохранится в {0}"), NovellaPrefabHistory.PREFABS_DIR + "/<name>.prefab"), hintSt);
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

            bool nameOk = !string.IsNullOrWhiteSpace(_name);
            using (new EditorGUI.DisabledScope(!nameOk))
            {
                var createSt = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 11, fixedHeight = 26, padding = new RectOffset(16, 16, 2, 2),
                    fontStyle = FontStyle.Bold,
                };
                createSt.normal.textColor = Color.white;
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = C_ACCENT;
                if (GUILayout.Button("✨ " + ToolLang.Get("Create", "Создать"), createSt, GUILayout.Width(140)))
                {
                    DoCreate();
                }
                GUI.backgroundColor = prevBg;
            }
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }

        private void DrawTypeButton(PrefabType type, string icon, string label)
        {
            const float W = 88f, H = 64f;
            Rect r = GUILayoutUtility.GetRect(W, H, GUILayout.Width(W), GUILayout.Height(H));
            bool selected = (_type == type);
            bool hover = r.Contains(Event.current.mousePosition);

            Color bg = selected
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f)
                : (hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.12f) : C_BG_RAISED);
            EditorGUI.DrawRect(r, bg);

            Color border = selected ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.7f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = selected ? C_ACCENT : C_TEXT_1;
            GUI.Label(new Rect(r.x, r.y + 6, r.width, 28), icon, iconSt);

            var labelSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            labelSt.normal.textColor = selected ? C_ACCENT : C_TEXT_3;
            GUI.Label(new Rect(r.x, r.y + 38, r.width, 18), label, labelSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                _type = type;
                Repaint();
                Event.current.Use();
            }
        }

        private void DoCreate()
        {
            // Sanitize имя: убираем недопустимые символы.
            string safeName = string.Concat(_name.Trim().Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safeName))
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Invalid name", "Некорректное имя"),
                    ToolLang.Get("Prefab name contains only invalid characters.",
                                 "Имя префаба содержит только недопустимые символы."),
                    "OK");
                return;
            }

            string folder = NovellaPrefabHistory.PREFABS_DIR;
            EnsureFolder(folder);

            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + ".prefab");

            // Создаём контент-объект в памяти и сохраняем как prefab.
            var go = BuildPrefabContent(_type, safeName);
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                if (prefab != null)
                {
                    NovellaPrefabHistory.Log("create", safeName, _type.ToString(), path);
                    Debug.Log($"[Novella] Prefab created: {path}");
                    _onCreated?.Invoke(prefab);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            Close();
        }

        // Минимальное содержимое для каждого типа prefab'а. Универсальная форма
        // — RectTransform-based, чтобы можно было класть на любой Canvas сцены.
        private static GameObject BuildPrefabContent(PrefabType type, string name)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            switch (type)
            {
                case PrefabType.Button:
                    rt.sizeDelta = new Vector2(200, 50);
                    var btnImg = go.AddComponent<Image>();
                    btnImg.color = new Color(0.36f, 0.75f, 0.92f);
                    go.AddComponent<Button>();
                    var btnLabelGo = new GameObject("Label");
                    btnLabelGo.transform.SetParent(go.transform, false);
                    var btnLabelRt = btnLabelGo.AddComponent<RectTransform>();
                    btnLabelRt.anchorMin = Vector2.zero; btnLabelRt.anchorMax = Vector2.one;
                    btnLabelRt.offsetMin = Vector2.zero; btnLabelRt.offsetMax = Vector2.zero;
                    var btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
                    btnLabel.text = name;
                    btnLabel.alignment = TextAlignmentOptions.Center;
                    btnLabel.color = new Color(0.07f, 0.08f, 0.10f);
                    btnLabel.fontSize = 18;
                    btnLabel.fontStyle = FontStyles.Bold;
                    break;

                case PrefabType.Panel:
                    rt.sizeDelta = new Vector2(420, 220);
                    var panImg = go.AddComponent<Image>();
                    panImg.color = new Color(0.10f, 0.11f, 0.15f, 0.85f);
                    break;

                case PrefabType.Image:
                    rt.sizeDelta = new Vector2(120, 120);
                    var img = go.AddComponent<Image>();
                    img.color = Color.white;
                    break;

                case PrefabType.Text:
                    rt.sizeDelta = new Vector2(220, 60);
                    var tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.text = name;
                    tmp.fontSize = 32;
                    tmp.color = Color.white;
                    tmp.alignment = TextAlignmentOptions.Center;
                    break;
            }
            return go;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string acc = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = acc + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(acc, parts[i]);
                acc = next;
            }
        }
    }
}
