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

            // Вертикальный layout: лейбл на отдельной строке, кнопка-пикер ниже.
            // Так ничего не обрезается даже в узких инспекторах (Branch / Wait),
            // и кнопка получает всю ширину.
            EditorGUILayout.BeginVertical();

            if (!string.IsNullOrEmpty(label))
            {
                var lblSt = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true };
                GUILayout.Label(label, lblSt);
            }

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

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(btnLabel, EditorStyles.popup, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2)))
            {
                NovellaUIPickerWindow.Open(label, kind, currentId, onChanged);
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(currentId)))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22), GUILayout.Height(EditorGUIUtility.singleLineHeight + 2)))
                {
                    onChanged?.Invoke("");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
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
