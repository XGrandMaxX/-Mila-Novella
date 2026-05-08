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
using System.Collections.Generic;     // List<> для фильтрации actions по поиску
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
        private string _searchQuery = "";

        public static void Open(NovellaUIBinding.BindingAction current, Action<NovellaUIBinding.BindingAction> onPick)
        {
            var win = GetWindow<NovellaActionPickerWindow>(true, "Novella · Choose action", true);
            win._selected = current;
            win._searchQuery = "";
            _callback = onPick;
            // Шире чтобы 3 колонки grid'а помещались БЕЗ горизонтального scroll'а
            // (с учётом scrollbar'а) + выше на ~25% чем раньше — комфортнее видеть
            // несколько секций без скролла.
            const float W = 820, H = 720;
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(1300, 1100);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(W * 0.5f, H * 0.5f),
                new Vector2(W, H));
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
        // Grid-layout вместо вертикального списка: на 720px ширине окна
        // получается 3 колонки по ~210px — все действия видны без долгого скролла.
        // Поверх грида — поле поиска (фильтрует все секции в реальном времени).
        // Под гридом — info-плашка с описанием выбранного действия.

        private struct GroupSpec
        {
            public string Title;
            public bool IsCoders;
            public NovellaUIBinding.BindingAction[] Actions;
        }

        private static readonly GroupSpec[] GROUPS = new[]
        {
            new GroupSpec { Title = "🧭 НАВИГАЦИЯ",       IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.GoToNode,
                    NovellaUIBinding.BindingAction.StartNewGame,
                    NovellaUIBinding.BindingAction.LoadLastSave,
                    NovellaUIBinding.BindingAction.RestartChapter,
                    NovellaUIBinding.BindingAction.ReturnToMainMenu,
                } },
            new GroupSpec { Title = "💾 СОХРАНЕНИЯ",       IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.SaveGameSlot,
                    NovellaUIBinding.BindingAction.LoadGameSlot,
                } },
            new GroupSpec { Title = "📋 ОКНА UI",          IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.ShowPanel,
                    NovellaUIBinding.BindingAction.HidePanel,
                    NovellaUIBinding.BindingAction.TogglePanel,
                } },
            new GroupSpec { Title = "📊 ИГРОВАЯ ЛОГИКА",   IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.SetVariable,
                } },
            new GroupSpec { Title = "🔊 ЭФФЕКТЫ",          IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.PlaySFX,
                } },
            new GroupSpec { Title = "⚙ СИСТЕМА",          IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.ChangeLanguage,
                    NovellaUIBinding.BindingAction.OpenURL,
                    NovellaUIBinding.BindingAction.PauseGame,
                    NovellaUIBinding.BindingAction.ResumeGame,
                    NovellaUIBinding.BindingAction.QuitGame,
                } },
            new GroupSpec { Title = "👨‍💻 ДЛЯ КОДЕРОВ — требуют C#-интеграции", IsCoders = true,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.TriggerEvent,
                    NovellaUIBinding.BindingAction.UnlockAchievement,
                } },
            new GroupSpec { Title = "📦 ПРОЧЕЕ",            IsCoders = false,
                Actions = new[] {
                    NovellaUIBinding.BindingAction.None,
                } },
        };

        private void DrawBody(Rect r)
        {
            // Search bar над body — фильтрует по имени и категории.
            const float searchH = 36f;
            Rect search = new Rect(r.x + 14, r.y + 8, r.width - 28, 22);
            DrawSearchBar(search);

            // Info-плашка снизу body (description выбранного action'а) фиксированная.
            const float infoH = 70f;
            Rect info = new Rect(r.x + 14, r.yMax - infoH - 6, r.width - 28, infoH);
            DrawInfoCard(info);

            // Grid посередине. Внутрь BeginScrollView передаём ScrollView,
            // вертикальный scrollbar съест ~14px справа — учитываем при расчёте
            // ширины ячеек (иначе появляется паразитный горизонтальный scroll).
            Rect grid = new Rect(r.x, r.y + searchH, r.width, r.height - searchH - infoH - 12);

            GUILayout.BeginArea(grid);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false);
            GUILayout.Space(4);

            // Эффективная ширина для grid-расчёта = область скроллвью минус scrollbar.
            float gridInnerW = grid.width - 16f;

            string q = (_searchQuery ?? "").Trim().ToLowerInvariant();
            bool anyShown = false;
            foreach (var g in GROUPS)
            {
                // Фильтр: оставляем только действия, чьё имя/описание/категория содержат query.
                var visible = new List<NovellaUIBinding.BindingAction>();
                foreach (var a in g.Actions)
                {
                    if (string.IsNullOrEmpty(q) || ActionMatches(a, g.Title, q))
                        visible.Add(a);
                }
                if (visible.Count == 0) continue;
                anyShown = true;

                DrawGroupGrid(g, visible.ToArray(), gridInnerW);
            }
            if (!anyShown && !string.IsNullOrEmpty(q))
            {
                var emptySt = new GUIStyle(EditorStyles.label) {
                    fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                    normal = { textColor = C_TEXT_3 }
                };
                GUILayout.Space(40);
                GUILayout.Label($"Ничего не найдено по запросу «{_searchQuery}»", emptySt);
            }

            GUILayout.Space(8);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSearchBar(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_RAISED);
            DrawBorder(r, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));

            GUI.Label(new Rect(r.x + 8, r.y + 1, 20, r.height), "🔍",
                new GUIStyle(EditorStyles.label) {
                    fontSize = 13, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_TEXT_3 }
                });

            var fieldSt = new GUIStyle(EditorStyles.textField) {
                fontSize = 12,
                normal = { background = null, textColor = C_TEXT_1 },
                focused = { background = null, textColor = C_TEXT_1 },
                hover = { background = null, textColor = C_TEXT_1 },
                active = { background = null, textColor = C_TEXT_1 },
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(2, 4, 2, 2),
            };
            GUI.SetNextControlName("ActionSearch");
            string newQ = EditorGUI.TextField(
                new Rect(r.x + 28, r.y + 2, r.width - 36, r.height - 4),
                _searchQuery ?? "", fieldSt);
            if (newQ != _searchQuery) { _searchQuery = newQ; Repaint(); }

            // Placeholder
            if (string.IsNullOrEmpty(_searchQuery) && GUI.GetNameOfFocusedControl() != "ActionSearch")
            {
                GUI.Label(new Rect(r.x + 30, r.y + 2, r.width - 38, r.height - 4),
                    "Поиск действия — название или категория…",
                    new GUIStyle(EditorStyles.label) {
                        fontSize = 12, fontStyle = FontStyle.Italic,
                        normal = { textColor = C_TEXT_4 }
                    });
            }
        }

        private void DrawInfoCard(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f));
            DrawBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.40f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            (string icon, string name) = ActionIconAndName(_selected);
            string desc = ActionDescription(_selected);
            if (string.IsNullOrEmpty(desc)) desc = "Кнопка ничего не делает по клику.";

            var nameSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 13,
                normal = { textColor = C_TEXT_1 }
            };
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 20, 20), icon + "  " + name, nameSt);

            var descSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, wordWrap = true,
                normal = { textColor = C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 12, r.y + 28, r.width - 20, r.height - 32), desc, descSt);
        }

        private void DrawGroupGrid(GroupSpec g, NovellaUIBinding.BindingAction[] actions, float bodyW)
        {
            // Заголовок группы.
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var hSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 10 };
            hSt.normal.textColor = g.IsCoders ? new Color(0.95f, 0.66f, 0.30f) : C_TEXT_3;
            GUILayout.Label(g.Title, hSt);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Grid из карточек: 3 колонки.
            const int cols = 3;
            const float pad = 14f;
            const float gap = 8f;
            float cellW = (bodyW - pad * 2 - gap * (cols - 1)) / cols;
            float cellH = 64f;

            int rows = (actions.Length + cols - 1) / cols;
            Rect grid = GUILayoutUtility.GetRect(bodyW, rows * cellH + (rows - 1) * gap);

            for (int i = 0; i < actions.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                Rect cell = new Rect(
                    grid.x + pad + col * (cellW + gap),
                    grid.y + row * (cellH + gap),
                    cellW, cellH);
                DrawActionTile(cell, actions[i], g.IsCoders);
            }
        }

        private void DrawActionTile(Rect r, NovellaUIBinding.BindingAction action, bool isCoderTile)
        {
            bool selected = _selected == action;
            Event e = Event.current;
            bool hover = r.Contains(e.mousePosition);

            // Фон.
            Color bg = selected
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                : (hover ? new Color(1f, 1f, 1f, 0.05f) : C_BG_RAISED);
            EditorGUI.DrawRect(r, bg);

            // Border.
            Color border;
            if (selected) border = C_ACCENT;
            else if (isCoderTile) border = new Color(0.95f, 0.66f, 0.30f, 0.40f);
            else border = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f);
            DrawBorder(r, border);

            // Cyan-stripe слева у selected.
            if (selected)
                EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            // Иконка крупная вверху.
            (string icon, string name) = ActionIconAndName(action);
            var iconSt = new GUIStyle(EditorStyles.label) {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = selected ? C_ACCENT : C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 6, r.y + 4, 36, 36), icon, iconSt);

            // Название справа от иконки.
            var nameSt = new GUIStyle(selected ? EditorStyles.boldLabel : EditorStyles.label) {
                fontSize = 11,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = selected ? C_TEXT_1 : C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 44, r.y + 4, r.width - 50, r.height - 8), name, nameSt);

            // Маркер «●» в правом верхнем углу для selected.
            if (selected)
            {
                var dotSt = new GUIStyle(EditorStyles.boldLabel) {
                    fontSize = 12, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_ACCENT }
                };
                GUI.Label(new Rect(r.xMax - 18, r.y + 2, 14, 14), "●", dotSt);
            }

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            // Клик: один — выбор; двойной — выбор + закрытие.
            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                _selected = action;
                if (e.clickCount >= 2) Confirm();
                e.Use();
                Repaint();
            }
        }

        private static bool ActionMatches(NovellaUIBinding.BindingAction a, string groupTitle, string q)
        {
            (string icon, string name) = ActionIconAndName(a);
            if (name.ToLowerInvariant().Contains(q)) return true;
            if (groupTitle.ToLowerInvariant().Contains(q)) return true;
            string desc = ActionDescription(a);
            if (!string.IsNullOrEmpty(desc) && desc.ToLowerInvariant().Contains(q)) return true;
            return false;
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
                case NovellaUIBinding.BindingAction.None:              return ("—",  "Без действия");
                case NovellaUIBinding.BindingAction.GoToNode:          return ("🎯", "Перейти к ноде графа");
                case NovellaUIBinding.BindingAction.StartNewGame:      return ("▶",  "Начать новую игру");
                case NovellaUIBinding.BindingAction.LoadLastSave:      return ("📥", "Загрузить сохранение");
                case NovellaUIBinding.BindingAction.RestartChapter:    return ("↻",  "Перезапустить главу");
                case NovellaUIBinding.BindingAction.QuitGame:          return ("🚪", "Выйти из игры");
                case NovellaUIBinding.BindingAction.ShowPanel:         return ("👁", "Показать UI элемент");
                case NovellaUIBinding.BindingAction.HidePanel:         return ("🚫", "Скрыть UI элемент");
                case NovellaUIBinding.BindingAction.TogglePanel:       return ("🔁", "Переключить UI элемент");
                case NovellaUIBinding.BindingAction.SetVariable:       return ("🔧", "Установить переменную");
                case NovellaUIBinding.BindingAction.TriggerEvent:      return ("📡", "Послать событие");
                case NovellaUIBinding.BindingAction.UnlockAchievement: return ("🏆", "Разблокировать ачивку");
                case NovellaUIBinding.BindingAction.PlaySFX:           return ("🎵", "Проиграть звук");
                case NovellaUIBinding.BindingAction.ChangeLanguage:    return ("🌐", "Сменить язык");
                case NovellaUIBinding.BindingAction.OpenURL:           return ("🔗", "Открыть ссылку");
                case NovellaUIBinding.BindingAction.PauseGame:         return ("⏸",  "Пауза игры");
                case NovellaUIBinding.BindingAction.ResumeGame:        return ("▶",  "Снять паузу");
                case NovellaUIBinding.BindingAction.SaveGameSlot:      return ("💾", "Сохранить в слот");
                case NovellaUIBinding.BindingAction.LoadGameSlot:      return ("📂", "Загрузить из слота");
                case NovellaUIBinding.BindingAction.ReturnToMainMenu:  return ("🏠", "Вернуться в главное меню");
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
                case NovellaUIBinding.BindingAction.None:              return "Кнопка ничего не делает по клику.";
                case NovellaUIBinding.BindingAction.GoToNode:          return "Player переходит на выбранную ноду графа — для диалоговых выборов и переходов между сценами истории.";
                case NovellaUIBinding.BindingAction.StartNewGame:      return "Стирает сохранение выбранной истории и запускает её с самого начала.";
                case NovellaUIBinding.BindingAction.LoadLastSave:      return "Открывает последнюю запущенную историю с её сохранения. Идеально для кнопки «Продолжить».";
                case NovellaUIBinding.BindingAction.RestartChapter:    return "Перезапускает текущую главу с нуля — стирает её сохранение и сбрасывает локальные переменные.";
                case NovellaUIBinding.BindingAction.QuitGame:          return "Закрывает игру (или останавливает Play Mode в редакторе).";
                case NovellaUIBinding.BindingAction.ShowPanel:         return "Включает (показывает) выбранный UI-элемент сцены. Например — панель настроек.";
                case NovellaUIBinding.BindingAction.HidePanel:         return "Выключает (скрывает) выбранный UI-элемент. Например — закрыть текущую панель.";
                case NovellaUIBinding.BindingAction.TogglePanel:       return "Переключает видимость UI-элемента: показывает если скрыт, скрывает если виден.";
                case NovellaUIBinding.BindingAction.SetVariable:       return "Устанавливает значение игровой переменной (NovellaVariables). Удобно для кнопок «Лайкнуть», «Выбрать имя», ...";
                case NovellaUIBinding.BindingAction.TriggerEvent:      return "👨‍💻 Для кодеров: посылает именованное событие в NovellaPlayer.OnNovellaEvent — игровая логика подписывается через C#.";
                case NovellaUIBinding.BindingAction.UnlockAchievement: return "👨‍💻 Для кодеров: посылает событие 'Achievement.Unlock' — подключи свою платформу (Steam / Google Play) через NovellaPlayer.OnNovellaEvent.";
                case NovellaUIBinding.BindingAction.PlaySFX:           return "Проигрывает звуковой эффект (одноразово, без повторов). Полезно для click-feedback кнопок и переходов.";
                case NovellaUIBinding.BindingAction.ChangeLanguage:    return "Меняет язык игры. Все тексты NovellaUIBinding и диалоги обновятся автоматически.";
                case NovellaUIBinding.BindingAction.OpenURL:           return "Открывает указанную ссылку в системном браузере.";
                case NovellaUIBinding.BindingAction.PauseGame:         return "⚠ Ставит Time.timeScale = 0 — игра «замораживается». Обязательно где-то добавь кнопку «Снять паузу» (ResumeGame), иначе игра выглядит зависшей.";
                case NovellaUIBinding.BindingAction.ResumeGame:        return "Снимает паузу — Time.timeScale = 1. Обычно вешается на кнопку «Продолжить» в pause-меню или на крестик закрытия паузы.";
                case NovellaUIBinding.BindingAction.SaveGameSlot:      return "Сохраняет текущее состояние истории в именованный слот. Открывает панель выбора слота с превью существующих сохранений.";
                case NovellaUIBinding.BindingAction.LoadGameSlot:      return "Загружает игру из выбранного слота. Открывает панель выбора слота с превью существующих сохранений.";
                case NovellaUIBinding.BindingAction.ReturnToMainMenu:  return "Загружает сцену главного меню. Если имя сцены не задано — берётся первая сцена из Build Settings (типичная раскладка проектов Novella). Time.timeScale возвращается в 1.";
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
