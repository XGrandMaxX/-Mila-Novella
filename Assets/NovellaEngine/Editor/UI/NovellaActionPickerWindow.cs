// ════════════════════════════════════════════════════════════════════════════
// NovellaActionPickerWindow
//
// Окно выбора действия для NovellaUIBinding.ClickAction. Группирует действия
// по смыслу (Навигация / Окна UI / Система) — пользователь не видит enum, видит
// аккуратный список с иконками, названиями и описаниями.
//
// Открывается из инспектора Кузницы UI. Возвращает выбранное действие через
// callback и закрывается. Параметры действия (story, target binding, url, lang)
// настраиваются ОТДЕЛЬНО в инспекторе binding'а — это окно только выбирает
// «что делает кнопка», не «с чем».
// ════════════════════════════════════════════════════════════════════════════

using System;
using NovellaEngine.Runtime.UI;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor.UIBindings
{
    public class NovellaActionPickerWindow : EditorWindow
    {
        private static Action<NovellaUIBinding.BindingAction> _callback;
        private NovellaUIBinding.BindingAction _selected;

        // Палитра — динамическая.
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();

        private Vector2 _scroll;

        public static void Open(NovellaUIBinding.BindingAction current, Action<NovellaUIBinding.BindingAction> onPick)
        {
            var win = GetWindow<NovellaActionPickerWindow>(true, "Novella · Choose action", true);
            win._selected = current;
            _callback = onPick;
            win.minSize = new Vector2(560, 560);
            win.maxSize = new Vector2(900, 900);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(280, 280),
                new Vector2(560, 560));
            win.ShowUtility();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            DrawHeader(new Rect(0, 0, position.width, 56));

            float footerH = 50;
            float bodyY = 56;
            float bodyH = position.height - bodyY - footerH;

            DrawBody(new Rect(0, bodyY, position.width, bodyH));
            DrawFooter(new Rect(0, position.height - footerH, position.width, footerH));

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.Return) { Confirm(); Event.current.Use(); }
            }
        }

        // ─── Header ─────────────────────────────────────────────────────────────

        private void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, 20), "🎬  Что должно произойти при клике?", t);

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            sub.normal.textColor = C_TEXT_3;
            string subText = _selected == NovellaUIBinding.BindingAction.None
                ? "Выбери одно из действий слева"
                : "Выбрано: " + ActionFriendlyName(_selected);
            GUI.Label(new Rect(r.x + 14, r.y + 30, r.width - 28, 18), subText, sub);
        }

        // ─── Body ───────────────────────────────────────────────────────────────

        private void DrawBody(Rect r)
        {
            GUILayout.BeginArea(r);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Space(8);

            DrawGroup("НАВИГАЦИЯ", new[]
            {
                NovellaUIBinding.BindingAction.GoToNode,
                NovellaUIBinding.BindingAction.StartNewGame,
                NovellaUIBinding.BindingAction.LoadLastSave,
            });

            DrawGroup("ОКНА UI", new[]
            {
                NovellaUIBinding.BindingAction.ShowPanel,
                NovellaUIBinding.BindingAction.HidePanel,
                NovellaUIBinding.BindingAction.TogglePanel,
            });

            DrawGroup("СИСТЕМА", new[]
            {
                NovellaUIBinding.BindingAction.ChangeLanguage,
                NovellaUIBinding.BindingAction.OpenURL,
                NovellaUIBinding.BindingAction.QuitGame,
            });

            DrawGroup("ПРОЧЕЕ", new[]
            {
                NovellaUIBinding.BindingAction.None,
            });

            GUILayout.Space(8);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGroup(string title, NovellaUIBinding.BindingAction[] actions)
        {
            // Заголовок группы — caps-label с акцентом.
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var hSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9 };
            hSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(title, hSt);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            // Карточки группы.
            for (int i = 0; i < actions.Length; i++)
            {
                DrawActionCard(actions[i]);
                GUILayout.Space(2);
            }
        }

        private void DrawActionCard(NovellaUIBinding.BindingAction action)
        {
            bool selected = _selected == action;

            float h = selected ? 56f : 38f;
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect row = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            bool hover = row.Contains(Event.current.mousePosition);

            Color bg = selected ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                                : hover ? new Color(1f, 1f, 1f, 0.04f)
                                        : C_BG_RAISED;
            EditorGUI.DrawRect(row, bg);
            DrawBorder(row, selected ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));

            if (selected)
            {
                EditorGUI.DrawRect(new Rect(row.x, row.y, 4, row.height), C_ACCENT);
            }

            // Иконка + название.
            (string icon, string name) = ActionIconAndName(action);
            string desc = ActionDescription(action);

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = selected ? C_ACCENT : C_TEXT_2;
            GUI.Label(new Rect(row.x + 12, row.y, 36, row.height), icon, iconSt);

            var nameSt = new GUIStyle(selected ? EditorStyles.boldLabel : EditorStyles.label) { fontSize = 13 };
            nameSt.normal.textColor = selected ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(row.x + 54, row.y + (selected ? 8 : 10), row.width - 80, 20), name, nameSt);

            if (selected)
            {
                var descSt = new GUIStyle(EditorStyles.label) { fontSize = 10, wordWrap = true };
                descSt.normal.textColor = C_TEXT_3;
                GUI.Label(new Rect(row.x + 54, row.y + 28, row.width - 80, row.height - 28), desc, descSt);
            }

            // Маркер «●» / «○» справа.
            var markSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            markSt.normal.textColor = selected ? C_ACCENT : new Color(1, 1, 1, 0.18f);
            GUI.Label(new Rect(row.xMax - 36, row.y, 24, row.height), selected ? "●" : "○", markSt);

            // Клик: один — выбор; двойной — выбор + закрытие.
            if (Event.current.type == EventType.MouseDown && hover)
            {
                _selected = action;
                if (Event.current.clickCount >= 2) Confirm();
                Event.current.Use();
                Repaint();
            }
        }

        // ─── Footer ─────────────────────────────────────────────────────────────

        private void DrawFooter(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            GUILayout.BeginHorizontal();

            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            hint.normal.textColor = C_TEXT_3;
            GUILayout.Label("⏎ — выбрать · ESC — отмена · ▦▦ двойной клик — выбрать", hint);

            GUILayout.FlexibleSpace();

            if (NovellaSettingsModule.NeutralButton("Отмена", GUILayout.Width(100), GUILayout.Height(28)))
            {
                Close();
            }
            GUILayout.Space(8);
            if (NovellaSettingsModule.AccentButton("Выбрать", GUILayout.Width(120), GUILayout.Height(28)))
            {
                Confirm();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void Confirm()
        {
            _callback?.Invoke(_selected);
            Close();
        }

        // ─── Action metadata ────────────────────────────────────────────────────

        private static (string icon, string name) ActionIconAndName(NovellaUIBinding.BindingAction a)
        {
            switch (a)
            {
                case NovellaUIBinding.BindingAction.None:           return ("—",  "Без действия");
                case NovellaUIBinding.BindingAction.GoToNode:       return ("🎯", "Перейти к ноде графа");
                case NovellaUIBinding.BindingAction.StartNewGame:   return ("▶",  "Начать новую игру");
                case NovellaUIBinding.BindingAction.LoadLastSave:   return ("📥", "Загрузить сохранение");
                case NovellaUIBinding.BindingAction.QuitGame:       return ("🚪", "Выйти из игры");
                case NovellaUIBinding.BindingAction.ShowPanel:      return ("👁", "Показать UI элемент");
                case NovellaUIBinding.BindingAction.HidePanel:      return ("🚫", "Скрыть UI элемент");
                case NovellaUIBinding.BindingAction.TogglePanel:    return ("🔁", "Переключить UI элемент");
                case NovellaUIBinding.BindingAction.ChangeLanguage: return ("🌐", "Сменить язык");
                case NovellaUIBinding.BindingAction.OpenURL:        return ("🔗", "Открыть ссылку");
            }
            return ("?", a.ToString());
        }

        private static string ActionFriendlyName(NovellaUIBinding.BindingAction a)
        {
            var (icon, name) = ActionIconAndName(a);
            return $"{icon}  {name}";
        }

        private static string ActionDescription(NovellaUIBinding.BindingAction a)
        {
            switch (a)
            {
                case NovellaUIBinding.BindingAction.None:           return "Кнопка ничего не делает по клику.";
                case NovellaUIBinding.BindingAction.GoToNode:       return "Player переходит на выбранную ноду графа — для диалоговых выборов и переходов между сценами истории.";
                case NovellaUIBinding.BindingAction.StartNewGame:   return "Стирает сохранение выбранной истории и запускает её с самого начала.";
                case NovellaUIBinding.BindingAction.LoadLastSave:   return "Открывает последнюю запущенную историю с её сохранения. Идеально для кнопки «Продолжить».";
                case NovellaUIBinding.BindingAction.QuitGame:       return "Закрывает игру (или останавливает Play Mode в редакторе).";
                case NovellaUIBinding.BindingAction.ShowPanel:      return "Включает (показывает) выбранный UI-элемент сцены. Например — панель настроек.";
                case NovellaUIBinding.BindingAction.HidePanel:      return "Выключает (скрывает) выбранный UI-элемент. Например — закрыть текущую панель.";
                case NovellaUIBinding.BindingAction.TogglePanel:    return "Переключает видимость UI-элемента: показывает если скрыт, скрывает если виден.";
                case NovellaUIBinding.BindingAction.ChangeLanguage: return "Меняет язык игры. Все тексты NovellaUIBinding и диалоги обновятся автоматически.";
                case NovellaUIBinding.BindingAction.OpenURL:        return "Открывает указанную ссылку в системном браузере.";
            }
            return "";
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
