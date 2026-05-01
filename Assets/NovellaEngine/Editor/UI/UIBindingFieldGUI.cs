// ════════════════════════════════════════════════════════════════════════════
// UIBindingFieldGUI
//
// IMGUI-хелпер для рендера ссылки на NovellaUIBinding из любых редакторских
// окон (Graph Node Inspector, Dialogue Editor, …). Аналог property-drawer'а
// для атрибута [UIBindingTarget], но вызывается явно из ручного IMGUI кода.
//
// Принимает текущую строку-id и lambda которая запишет новое значение —
// это позволяет работать с произвольными data-типами без SerializedProperty.
// ════════════════════════════════════════════════════════════════════════════

using System;
using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    public static class UIBindingFieldGUI
    {
        public static void Draw(string label, string currentId, UIBindingKind kind, Action<string> onChanged)
        {
            NovellaUIBinding currentBinding = !string.IsNullOrEmpty(currentId)
                ? NovellaUIBinding.FindInScene(currentId)
                : null;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            GameObject newGo = (GameObject)EditorGUILayout.ObjectField(
                label,
                currentBinding != null ? currentBinding.gameObject : null,
                typeof(GameObject),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                if (newGo == null)
                {
                    onChanged?.Invoke("");
                }
                else if (!IsKindCompatible(newGo, kind))
                {
                    EditorUtility.DisplayDialog(
                        "UI Binding",
                        kind == UIBindingKind.Text
                            ? "Этому полю нужен текстовый элемент (TMP_Text). На выбранном объекте его нет."
                            : kind == UIBindingKind.Button
                                ? "Этому полю нужна кнопка (UnityEngine.UI.Button). На выбранном объекте её нет."
                                : "Объект не подходит.",
                        "OK");
                }
                else
                {
                    var b = NovellaUIBinding.GetOrAdd(newGo);
                    if (b != null) onChanged?.Invoke(b.Id);
                }
            }

            EditorGUILayout.EndHorizontal();

            // Лёгкий статусный текст под полем — короткий путь / предупреждение.
            var hintStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            hintStyle.normal.textColor = new Color(0.62f, 0.63f, 0.69f);

            string hint;
            if (string.IsNullOrEmpty(currentId)) hint = "↪ перетащи UI элемент из сцены сюда";
            else if (currentBinding == null) hint = $"⚠ binding '{currentId.Substring(0, Math.Min(6, currentId.Length))}…' не найден в сцене";
            else hint = $"✓ {GetPathOf(currentBinding.gameObject)}";

            EditorGUILayout.LabelField("    " + hint, hintStyle);
        }

        private static bool IsKindCompatible(GameObject go, UIBindingKind kind)
        {
            switch (kind)
            {
                case UIBindingKind.Text:   return go.GetComponent<TMP_Text>() != null;
                case UIBindingKind.Button: return go.GetComponent<Button>() != null;
                default:                   return true;
            }
        }

        private static string GetPathOf(GameObject go)
        {
            if (go == null) return "(null)";
            var t = go.transform;
            string p = t.name;
            if (t.parent != null) p = t.parent.name + "/" + p;
            if (t.parent != null && t.parent.parent != null) p = t.parent.parent.name + "/" + p;
            return p;
        }
    }
}
