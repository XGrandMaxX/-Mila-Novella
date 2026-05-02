// ════════════════════════════════════════════════════════════════════════════
// UIBindingFieldGUI
//
// IMGUI-хелпер для выбора NovellaUIBinding в нодах графа. Кнопка-popup которая
// открывает NovellaUIPickerWindow — окно «как кусочек Кузницы UI» с превью
// сцены и иерархией. Пользователь выбирает элемент визуально, не залезая в
// Unity-инспектор и не тыкая в плоский enum.
// ════════════════════════════════════════════════════════════════════════════

using System;
using NovellaEngine.Runtime.UI;
using UnityEditor;
using UnityEngine;

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

            // Label слева — стандартной шириной как у других editor-полей.
            if (!string.IsNullOrEmpty(label))
            {
                GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            }

            // Кнопка-пикер: показывает имя выбранного binding'а или плейсхолдер.
            string btnLabel;
            if (currentBinding != null)
            {
                btnLabel = $"{KindIcon(currentBinding.DetectKind())}  {currentBinding.DisplayName}";
            }
            else if (!string.IsNullOrEmpty(currentId))
            {
                btnLabel = "⚠ binding не найден в сцене";
            }
            else
            {
                btnLabel = "— выбрать UI элемент —";
            }

            if (GUILayout.Button(btnLabel, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                NovellaUIPickerWindow.Open(label, kind, currentId, onChanged);
            }

            // X-кнопка справа — очистить.
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(currentId)))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22)))
                {
                    onChanged?.Invoke("");
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string KindIcon(NovellaUIBinding.BindingKind k)
        {
            switch (k)
            {
                case NovellaUIBinding.BindingKind.Text:   return "📝";
                case NovellaUIBinding.BindingKind.Button: return "🔘";
                case NovellaUIBinding.BindingKind.Image:  return "🖼";
                default: return "▣";
            }
        }
    }
}
