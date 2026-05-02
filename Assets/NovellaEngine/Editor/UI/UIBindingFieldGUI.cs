// ════════════════════════════════════════════════════════════════════════════
// UIBindingFieldGUI
//
// IMGUI-хелпер для выбора NovellaUIBinding в нодах графа. Вместо drag&drop из
// Unity-иерархии (что заставляло пользователя лезть в инспектор Unity) тут
// отдельный пикер: одна кнопка с именем выбранного binding'а, по клику —
// GenericMenu со всеми подходящими элементами активной сцены, сгруппированными
// по типу (📝 Текст / 🔘 Кнопка / 🖼 Картинка / Другое).
//
// Имена берутся из NovellaUIBinding.DisplayName (Name либо имя GameObject).
// Так пользователь работает с понятными именами «Diary/PageBody», а не с
// абстрактными ссылками на сцену — и ему не нужен Unity вообще.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
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
                ShowPicker(currentId, kind, onChanged);
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

        // Выпадающее меню со всеми binding'ами активной сцены, отфильтрованными
        // по типу. Если пусто — disabled-пункт с подсказкой как создать.
        private static void ShowPicker(string currentId, UIBindingKind kind, Action<string> onChanged)
        {
            var menu = new GenericMenu();
            var all = NovellaUIBinding.FindAllInScene();

            // Фильтр по kind.
            var filtered = new List<NovellaUIBinding>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (IsCompatible(all[i], kind)) filtered.Add(all[i]);
            }

            if (filtered.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(KindHint(kind) + ": в сцене нет привязываемых элементов"));
                menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent("Открой Кузницу UI → выбери элемент → «➕ Сделать привязываемым»"));
                menu.ShowAsContext();
                return;
            }

            // Сортируем по группе (Text/Button/Image/Other) затем по имени.
            filtered.Sort((a, b) =>
            {
                int ga = (int)a.DetectKind(), gb = (int)b.DetectKind();
                if (ga != gb) return ga.CompareTo(gb);
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var b in filtered)
            {
                var bLocal = b;
                string group = KindFolder(b.DetectKind());
                string item = $"{group}/{b.DisplayName}";
                bool selected = b.Id == currentId;
                menu.AddItem(new GUIContent(item), selected, () =>
                {
                    onChanged?.Invoke(bLocal.Id);
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("(очистить)"), false, () => onChanged?.Invoke(""));

            menu.ShowAsContext();
        }

        private static bool IsCompatible(NovellaUIBinding b, UIBindingKind kind)
        {
            if (b == null) return false;
            switch (kind)
            {
                case UIBindingKind.Text:   return b.GetComponent<TMP_Text>() != null;
                case UIBindingKind.Button: return b.GetComponent<Button>()   != null;
                default:                   return true;
            }
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

        private static string KindFolder(NovellaUIBinding.BindingKind k)
        {
            switch (k)
            {
                case NovellaUIBinding.BindingKind.Text:   return "📝 Тексты";
                case NovellaUIBinding.BindingKind.Button: return "🔘 Кнопки";
                case NovellaUIBinding.BindingKind.Image:  return "🖼 Картинки";
                default: return "▣ Прочее";
            }
        }

        private static string KindHint(UIBindingKind kind)
        {
            switch (kind)
            {
                case UIBindingKind.Text:   return "📝 Тексты";
                case UIBindingKind.Button: return "🔘 Кнопки";
                default: return "Элементы";
            }
        }
    }
}
