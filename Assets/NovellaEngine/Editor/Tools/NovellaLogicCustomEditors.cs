// ════════════════════════════════════════════════════════════════════════════
// NovellaLogicCustomEditors — кастомные редакторы для сложных типов внутри
// Logic-таба Кузницы UI. Не каждый класс хорошо рисуется через стандартный
// PropertyField (List<NovellaUIBinding.ClickActionStep> — это пример боли:
// массив enum-дропдаунов, ассет-пикеров, raw-полей).
//
// Тут живут «дружелюбные» рисователи. Подключаются из NovellaUIForge через
// type-check в DrawLogicComponentCard.
// ════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using NovellaEngine.Runtime.UI;
using NovellaEngine.Editor.UIBindings;

namespace NovellaEngine.Editor
{
    public static class NovellaLogicCustomEditors
    {
        // Категория действия — для группировки в выпадающем меню «выбрать действие».
        private enum ActionCategory { Game, Navigation, System, Data, Audio, Other }

        private struct ActionMeta
        {
            public NovellaUIBinding.BindingAction Action;
            public ActionCategory Category;
            public string Icon;
            public string LabelEN, LabelRU;
            public string HintEN, HintRU;
        }

        // Каталог всех известных действий. Описание = что юзер видит вместо
        // голого имени enum. Hint показывается под выбранным действием когда
        // включены 💡 Подсказки.
        private static readonly List<ActionMeta> _actions = new List<ActionMeta>
        {
            new ActionMeta { Action = NovellaUIBinding.BindingAction.None,
                Category = ActionCategory.Other, Icon = "❌",
                LabelEN = "(none)", LabelRU = "(не выбрано)",
                HintEN = "No action — placeholder.", HintRU = "Действие не выбрано — заглушка." },

            // ─── Game flow ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.StartNewGame,
                Category = ActionCategory.Game, Icon = "▶",
                LabelEN = "Start new game", LabelRU = "Начать новую игру",
                HintEN = "Picks a story and launches it from chapter 1. If no story is selected — uses the first one in Resources/Stories.",
                HintRU = "Выбирает историю и запускает её с первой главы. Если не указана — берёт первую из Resources/Stories." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.LoadLastSave,
                Category = ActionCategory.Game, Icon = "💾",
                LabelEN = "Continue (last save)", LabelRU = "Продолжить (последний сейв)",
                HintEN = "Loads the last auto-save of the most recently played story.",
                HintRU = "Грузит автосейв последней проигранной истории." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.RestartChapter,
                Category = ActionCategory.Game, Icon = "🔁",
                LabelEN = "Restart chapter", LabelRU = "Перезапустить главу",
                HintEN = "Restarts the currently active chapter from its first node.",
                HintRU = "Перезапускает активную главу с первой ноды." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.QuitGame,
                Category = ActionCategory.Game, Icon = "🚪",
                LabelEN = "Quit game", LabelRU = "Выйти из игры",
                HintEN = "Closes the game (or stops Play Mode in editor).",
                HintRU = "Закрывает игру (или останавливает Play Mode в редакторе)." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.PauseGame,
                Category = ActionCategory.Game, Icon = "⏸",
                LabelEN = "Pause game", LabelRU = "Поставить паузу",
                HintEN = "Sets Time.timeScale = 0. Animations and sounds freeze, UI keeps working.",
                HintRU = "Time.timeScale = 0. Анимации и звук замирают, UI работает." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.ResumeGame,
                Category = ActionCategory.Game, Icon = "▶",
                LabelEN = "Resume game", LabelRU = "Снять паузу",
                HintEN = "Sets Time.timeScale = 1.",
                HintRU = "Time.timeScale = 1." },

            // ─── Navigation ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.GoToNode,
                Category = ActionCategory.Navigation, Icon = "📍",
                LabelEN = "Go to story node", LabelRU = "Перейти к ноде графа",
                HintEN = "Jumps to the specified node in the active story graph. Useful for shortcuts inside the story.",
                HintRU = "Прыгает к указанной ноде в активной истории. Удобно для шорткатов внутри игры." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.ShowPanel,
                Category = ActionCategory.Navigation, Icon = "👁",
                LabelEN = "Show UI panel", LabelRU = "Показать панель",
                HintEN = "Activates a UI element by binding ID. Doesn't deactivate other panels — chain Hide actions if you need exclusivity.",
                HintRU = "Включает UI-элемент по binding ID. Другие панели не выключает — добавь шаги Hide если нужно." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.HidePanel,
                Category = ActionCategory.Navigation, Icon = "🙈",
                LabelEN = "Hide UI panel", LabelRU = "Скрыть панель",
                HintEN = "Deactivates a UI element by binding ID.",
                HintRU = "Выключает UI-элемент по binding ID." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.TogglePanel,
                Category = ActionCategory.Navigation, Icon = "🔄",
                LabelEN = "Toggle UI panel", LabelRU = "Переключить панель",
                HintEN = "Inverts the active state of the UI element. Useful for «open/close menu» on one button.",
                HintRU = "Инвертирует состояние UI-элемента. Удобно для «открыть/закрыть меню» на одной кнопке." },

            // ─── System ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.ChangeLanguage,
                Category = ActionCategory.System, Icon = "🌐",
                LabelEN = "Change language", LabelRU = "Сменить язык",
                HintEN = "Switches the current UI language. Code must match one in your Localization Settings.",
                HintRU = "Меняет текущий язык UI. Код должен совпадать с одним из в Localization Settings." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.OpenURL,
                Category = ActionCategory.System, Icon = "🔗",
                LabelEN = "Open URL", LabelRU = "Открыть ссылку",
                HintEN = "Opens a link in the player's default browser.",
                HintRU = "Открывает ссылку в браузере игрока." },

            // ─── Data ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.SetVariable,
                Category = ActionCategory.Data, Icon = "📊",
                LabelEN = "Set variable", LabelRU = "Изменить переменную",
                HintEN = "Writes a value into a NovellaVariables variable. The graph can read it via Condition / Branch nodes.",
                HintRU = "Пишет значение в переменную NovellaVariables. Граф может читать её в нодах Condition / Branch." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.TriggerEvent,
                Category = ActionCategory.Data, Icon = "📣",
                LabelEN = "Trigger event", LabelRU = "Послать событие",
                HintEN = "Fires NovellaPlayer.OnNovellaEvent — your scripts can subscribe.",
                HintRU = "Стреляет NovellaPlayer.OnNovellaEvent — твои скрипты могут подписаться." },

            // ─── Audio ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.PlaySFX,
                Category = ActionCategory.Audio, Icon = "🔊",
                LabelEN = "Play sound (SFX)", LabelRU = "Проиграть звук (SFX)",
                HintEN = "Plays an AudioClip once as a UI sound effect (click sound, etc).",
                HintRU = "Проигрывает AudioClip один раз как UI-звук (клик и т.п.)." },

            // ─── Achievements ───
            new ActionMeta { Action = NovellaUIBinding.BindingAction.UnlockAchievement,
                Category = ActionCategory.Other, Icon = "🏆",
                LabelEN = "Unlock achievement", LabelRU = "Выдать достижение",
                HintEN = "Hook for platform integration (Steam, GOG, etc). Sends achievement ID to the platform layer.",
                HintRU = "Хук для интеграции с платформой (Steam, GOG). Шлёт ID ачивки в платформенный слой." },
        };

        private static ActionMeta GetMeta(NovellaUIBinding.BindingAction a)
        {
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Action == a) return _actions[i];
            return new ActionMeta { Action = a, Icon = "?", LabelRU = a.ToString(), LabelEN = a.ToString() };
        }

        private static string CategoryNameRU(ActionCategory c) => c switch
        {
            ActionCategory.Game       => "🎮 Игра",
            ActionCategory.Navigation => "🧭 Навигация",
            ActionCategory.System     => "⚙ Система",
            ActionCategory.Data       => "📊 Данные",
            ActionCategory.Audio      => "🔊 Звук",
            _                         => "📦 Прочее",
        };
        private static string CategoryNameEN(ActionCategory c) => c switch
        {
            ActionCategory.Game       => "🎮 Game",
            ActionCategory.Navigation => "🧭 Navigation",
            ActionCategory.System     => "⚙ System",
            ActionCategory.Data       => "📊 Data",
            ActionCategory.Audio      => "🔊 Audio",
            _                         => "📦 Other",
        };

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC: главная точка входа из Logic-таба.
        // ════════════════════════════════════════════════════════════════════
        public static void DrawNovellaUIBinding(NovellaUIBinding binding, bool showHints)
        {
            if (binding == null) return;

            // ─── Header / описание ──────────────────────────────────────────
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            titleSt.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUILayout.Label("📜 " + ToolLang.Get("UI Binding — what happens on click", "UI Binding — что происходит по клику"), titleSt);

            if (showHints)
            {
                var introSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                introSt.normal.textColor = NovellaSettingsModule.GetTextSecondary();
                GUILayout.Label(ToolLang.Get(
                    "Each step runs top-to-bottom when this button is clicked. Steps after a terminal action (Quit / Start / etc) won't run.",
                    "Каждый шаг выполняется сверху вниз когда нажимают эту кнопку. Шаги после терминального действия (Выход / Старт / и т.п.) не выполнятся."), introSt);
            }
            GUILayout.Space(8);

            // ─── Имя binding'а (для адресации) ─────────────────────────────
            GUILayout.Label(ToolLang.Get("Display name", "Имя для пикера"), EditorStyles.miniBoldLabel);
            string newName = EditorGUILayout.TextField(binding.Name);
            if (newName != binding.Name) { Undo.RecordObject(binding, "Rename binding"); binding.Name = newName; EditorUtility.SetDirty(binding); }
            if (showHints)
                DrawHint(ToolLang.Get(
                    "Friendly name shown in graph node pickers and the Bindings overview. Doesn't affect the game — only how YOU find this element later.",
                    "Дружелюбное имя — видно везде где этот элемент выбирается из ноды графа или таблицы Связей. На игру не влияет, нужно только тебе чтобы потом узнавать этот элемент в списке."));

            // ─── Localization key ──────────────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label(ToolLang.Get("Localization key (optional)", "Ключ локализации (опц.)"), EditorStyles.miniBoldLabel);
            GUILayout.BeginHorizontal();
            string newKey = EditorGUILayout.TextField(binding.LocalizationKey ?? "");
            if (newKey != binding.LocalizationKey)
            {
                Undo.RecordObject(binding, "Edit loc key");
                binding.LocalizationKey = newKey;
                EditorUtility.SetDirty(binding);
                binding.Refresh();
            }
            if (GUILayout.Button("📂", GUILayout.Width(28), GUILayout.Height(18)))
            {
                var capBinding = binding;
                NovellaLocKeyPickerWindow.Open(binding.LocalizationKey, picked =>
                {
                    Undo.RecordObject(capBinding, "Pick loc key");
                    capBinding.LocalizationKey = picked;
                    EditorUtility.SetDirty(capBinding);
                    capBinding.Refresh();
                });
            }
            GUILayout.EndHorizontal();
            if (showHints)
                DrawHint(ToolLang.Get(
                    "Connects this text to the Localization Table. When the player switches language, the text auto-updates. Leave empty if no translation is needed.",
                    "Связывает этот текст с таблицей локализации. При смене языка игроком текст обновится сам. Оставь пустым если переводить не нужно."));

            // ─── Bound variable ────────────────────────────────────────────
            GUILayout.Space(8);
            GUILayout.Label(ToolLang.Get("Variable (substitutes {var})", "Переменная (вместо {var})"), EditorStyles.miniBoldLabel);
            GUILayout.BeginHorizontal();
            string newVar = EditorGUILayout.TextField(binding.BoundVariable ?? "");
            if (newVar != binding.BoundVariable)
            {
                Undo.RecordObject(binding, "Edit bound var");
                binding.BoundVariable = newVar;
                EditorUtility.SetDirty(binding);
                binding.Refresh();
            }
            if (GUILayout.Button("📂", GUILayout.Width(28), GUILayout.Height(18)))
            {
                var capBinding = binding;
                NovellaVariablePickerWindow.Open(binding.BoundVariable, picked =>
                {
                    Undo.RecordObject(capBinding, "Pick variable");
                    capBinding.BoundVariable = picked;
                    EditorUtility.SetDirty(capBinding);
                    capBinding.Refresh();
                });
            }
            GUILayout.EndHorizontal();
            if (showHints)
                DrawHint(ToolLang.Get(
                    "Inserts a variable's value into the text. Write \"{var}\" inside the localized string and pick a variable here — at runtime \"{var}\" gets replaced. Example: \"HP: {var}\" + \"PlayerHP\" → \"HP: 42\".",
                    "Подставляет значение переменной в текст. Вставь «{var}» внутрь локализованной строки и выбери переменную здесь — в рантайме «{var}» заменится. Пример: «HP: {var}» + «PlayerHP» → «HP: 42»."));

            GUILayout.Space(12);

            // ─── Click sequence — заголовок ───────────────────────────────
            bool hasButton = binding.GetComponent<UnityEngine.UI.Button>() != null;
            if (hasButton)
            {
                GUILayout.Label(ToolLang.Get("On click — sequence of actions", "По клику — последовательность действий"), EditorStyles.miniBoldLabel);
                if (showHints)
                    DrawHint(ToolLang.Get(
                        "Steps run top-to-bottom when player clicks this button. Each step can have a delay before it runs.",
                        "Шаги выполняются сверху вниз когда игрок нажимает на кнопку. У каждого шага может быть задержка перед выполнением."));
            }
            else
            {
                // Не кнопка — секция шагов скрыта.
                GUILayout.Space(0);
            }
            GUILayout.Space(4);

            // ─── Список шагов + «Добавить действие» — только для Button ───
            if (hasButton)
            {
                if (binding.ClickSequence == null) binding.ClickSequence = new List<NovellaUIBinding.ClickActionStep>();

                int removeIndex = -1;
                int moveUp = -1, moveDown = -1;

                for (int i = 0; i < binding.ClickSequence.Count; i++)
                {
                    bool removed = DrawStepCard(binding, i, showHints, out bool reqUp, out bool reqDown);
                    if (removed) removeIndex = i;
                    if (reqUp) moveUp = i;
                    if (reqDown) moveDown = i;
                }

                if (removeIndex >= 0)
                {
                    Undo.RecordObject(binding, "Remove binding step");
                    binding.ClickSequence.RemoveAt(removeIndex);
                    EditorUtility.SetDirty(binding);
                }
                if (moveUp > 0)
                {
                    Undo.RecordObject(binding, "Move step up");
                    var s = binding.ClickSequence[moveUp];
                    binding.ClickSequence.RemoveAt(moveUp);
                    binding.ClickSequence.Insert(moveUp - 1, s);
                    EditorUtility.SetDirty(binding);
                }
                if (moveDown >= 0 && moveDown < binding.ClickSequence.Count - 1)
                {
                    Undo.RecordObject(binding, "Move step down");
                    var s = binding.ClickSequence[moveDown];
                    binding.ClickSequence.RemoveAt(moveDown);
                    binding.ClickSequence.Insert(moveDown + 1, s);
                    EditorUtility.SetDirty(binding);
                }

                GUILayout.Space(6);

                var addBtn = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, padding = new RectOffset(8, 8, 6, 6) };
                if (GUILayout.Button("➕ " + ToolLang.Get("Add action", "Добавить действие"), addBtn, GUILayout.Height(28)))
                {
                    var capBinding = binding;
                    NovellaActionPickerWindow.Open(NovellaUIBinding.BindingAction.None, picked =>
                    {
                        Undo.RecordObject(capBinding, "Add binding step");
                        capBinding.ClickSequence.Add(new NovellaUIBinding.ClickActionStep { Action = picked });
                        EditorUtility.SetDirty(capBinding);
                    });
                }
            }

            // ─── Remove link ─────────────────────────────────────────────
            GUILayout.Space(14);
            var prevC = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f);
            if (GUILayout.Button("🗑  " + ToolLang.Get("Remove link from this element", "Убрать связь с этого элемента"), GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("Remove binding?", "Убрать связь?"),
                    ToolLang.Get("All graph nodes referring to this element will lose the link. Proceed?",
                                 "Все ноды графа ссылающиеся на этот элемент потеряют связь. Продолжить?"),
                    ToolLang.Get("Remove", "Убрать"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    Undo.DestroyObjectImmediate(binding);
                }
            }
            GUI.backgroundColor = prevC;
        }

        // ─── Карточка одного шага ───────────────────────────────────────────
        // Возвращает true если юзер кликнул «удалить» — outer-цикл удалит.
        private static bool DrawStepCard(NovellaUIBinding binding, int index, bool showHints,
                                         out bool moveUp, out bool moveDown)
        {
            moveUp = false; moveDown = false;
            var step = binding.ClickSequence[index];
            var meta = GetMeta(step.Action);
            bool isTerminal = NovellaUIBinding.IsTerminalAction(step.Action);

            // Подсветка терминальных шагов — те после которых дальше ничего
            // не выполнится.
            Color bg = isTerminal ? new Color(0.22f, 0.18f, 0.10f, 0.6f) : new Color(0f, 0f, 0f, 0.20f);

            GUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: # шага + кнопка-выбор действия (на всю строку, с clip).
            // Раньше всё было в одной строке с ▲▼✕ → длинные имена действий
            // выезжали за инспектор.
            GUILayout.BeginHorizontal();
            var numSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            numSt.normal.textColor = NovellaSettingsModule.GetTextSecondary();
            GUILayout.Label($"#{index + 1}", numSt, GUILayout.Width(28));

            // Кнопка-пикер действия — открывает наш красивый
            // NovellaActionPickerWindow (а не GenericMenu).
            string actLabel = meta.Icon + "  " + ToolLang.Get(meta.LabelEN, meta.LabelRU);
            var pickSt = new GUIStyle(EditorStyles.popup) {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(8, 22, 2, 2)
            };
            if (GUILayout.Button(actLabel, pickSt, GUILayout.Height(22), GUILayout.ExpandWidth(true)))
            {
                var capBinding = binding;
                int capIndex = index;
                NovellaActionPickerWindow.Open(step.Action, picked =>
                {
                    Undo.RecordObject(capBinding, "Change action");
                    capBinding.ClickSequence[capIndex].Action = picked;
                    EditorUtility.SetDirty(capBinding);
                });
            }
            GUILayout.EndHorizontal();

            // Row 2: контролы перемещения/удаления — отдельной строкой справа,
            // не сжимают action picker.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(28), GUILayout.Height(20))) moveUp = true;
            }
            using (new EditorGUI.DisabledScope(index >= binding.ClickSequence.Count - 1))
            {
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(28), GUILayout.Height(20))) moveDown = true;
            }
            var prevC = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f);
            bool delete = GUILayout.Button("✕ " + ToolLang.Get("Remove", "Удалить"), EditorStyles.miniButtonRight, GUILayout.Width(90), GUILayout.Height(20));
            GUI.backgroundColor = prevC;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Hint про действие.
            if (showHints && (!string.IsNullOrEmpty(meta.HintEN) || !string.IsNullOrEmpty(meta.HintRU)))
            {
                DrawHint(ToolLang.Get(meta.HintEN, meta.HintRU));
            }

            // Action-specific поля + предупреждение для шагов после терминального.
            if (index > 0 && NovellaUIBinding.IsTerminalAction(binding.ClickSequence[index - 1].Action))
            {
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "Previous step is terminal — this step won't run. Move it before the terminal step.",
                    "Предыдущий шаг терминальный — этот не выполнится. Переставь выше терминального."),
                    MessageType.Warning);
            }

            DrawActionFields(binding, step, showHints);

            // Delay before — для всех, но скрываем если 0 для чистоты.
            DrawDelayField(binding, step, showHints);

            GUILayout.EndVertical();
            GUILayout.Space(4);

            return delete;
        }

        // Для каждого Action рисуем ТОЛЬКО релевантные ему поля.
        private static void DrawActionFields(NovellaUIBinding binding, NovellaUIBinding.ClickActionStep step, bool showHints)
        {
            switch (step.Action)
            {
                case NovellaUIBinding.BindingAction.GoToNode:
                {
                    string n = TextRow(ToolLang.Get("Node ID:", "ID ноды:"), step.OnClickGotoNodeId, showHints,
                        ToolLang.Get("Internal ID of the target node in the story graph (e.g. 'Chapter1_Branch_3').",
                                     "Внутренний ID ноды в графе истории (например «Chapter1_Branch_3»)."));
                    if (n != step.OnClickGotoNodeId) { Undo.RecordObject(binding, "Edit step"); step.OnClickGotoNodeId = n; EditorUtility.SetDirty(binding); }
                    break;
                }

                case NovellaUIBinding.BindingAction.StartNewGame:
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Story:", "История:"), GUILayout.Width(150));
                    string label = step.StoryToStart != null ? "📖 " + step.StoryToStart.name : ToolLang.Get("(any first available)", "(любая первая)");
                    if (GUILayout.Button(label, EditorStyles.popup, GUILayout.Height(22)))
                    {
                        var captured = binding;
                        var capStep = step;
                        NovellaGalleryWindow.ShowWindow(asset =>
                        {
                            var s = asset as NovellaStory;
                            Undo.RecordObject(captured, "Pick story");
                            capStep.StoryToStart = s;
                            EditorUtility.SetDirty(captured);
                        }, NovellaGalleryWindow.EGalleryFilter.Story);
                    }
                    if (step.StoryToStart != null && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        Undo.RecordObject(binding, "Clear story");
                        step.StoryToStart = null;
                        EditorUtility.SetDirty(binding);
                    }
                    GUILayout.EndHorizontal();
                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Leave empty to launch the first valid story automatically. Pick one only if you want this button to launch a specific story.",
                            "Оставь пустым чтобы запустить первую валидную историю автоматически. Выбирай конкретную только если эта кнопка должна запускать именно её."));
                    break;
                }

                case NovellaUIBinding.BindingAction.ShowPanel:
                case NovellaUIBinding.BindingAction.HidePanel:
                case NovellaUIBinding.BindingAction.TogglePanel:
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Target panel ID:", "ID целевой панели:"), GUILayout.Width(150));
                    string current = string.IsNullOrEmpty(step.TargetBindingId) ? ToolLang.Get("(not set)", "(не задано)") : step.TargetBindingId;
                    if (GUILayout.Button(current, EditorStyles.popup, GUILayout.Height(22)))
                    {
                        ShowAllBindingsMenu(binding, step);
                    }
                    GUILayout.EndHorizontal();
                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Pick another NovellaUIBinding in the scene by its ID. The action will activate / deactivate / toggle that target's GameObject.",
                            "Выбери другой NovellaUIBinding в сцене по его ID. Действие включит / выключит / переключит GameObject цели."));
                    break;
                }

                case NovellaUIBinding.BindingAction.ChangeLanguage:
                {
                    var settings = NovellaLocalizationSettings.GetOrCreateSettings();
                    var langs = settings != null && settings.Languages != null ? settings.Languages : new List<string> { "EN", "RU" };
                    int idx = langs.IndexOf(step.LanguageCode);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Switch to:", "Переключить на:"), GUILayout.Width(150));
                    int newIdx = EditorGUILayout.Popup(idx, langs.ToArray(), GUILayout.Height(22));
                    if (newIdx != idx && newIdx >= 0 && newIdx < langs.Count)
                    {
                        Undo.RecordObject(binding, "Set language");
                        step.LanguageCode = langs[newIdx];
                        EditorUtility.SetDirty(binding);
                    }
                    GUILayout.EndHorizontal();
                    break;
                }

                case NovellaUIBinding.BindingAction.OpenURL:
                {
                    string n = TextRow(ToolLang.Get("URL:", "Ссылка:"), step.URL, showHints,
                        ToolLang.Get("Full URL — must start with https:// or http://.",
                                     "Полная ссылка — должна начинаться с https:// или http://."));
                    if (n != step.URL) { Undo.RecordObject(binding, "Edit URL"); step.URL = n; EditorUtility.SetDirty(binding); }
                    break;
                }

                case NovellaUIBinding.BindingAction.SetVariable:
                {
                    string n = TextRow(ToolLang.Get("Variable name:", "Имя переменной:"), step.VariableName, false, null);
                    if (n != step.VariableName) { Undo.RecordObject(binding, "Edit var"); step.VariableName = n; EditorUtility.SetDirty(binding); }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Int value:", "Значение (число):"), GUILayout.Width(150));
                    int ni = EditorGUILayout.IntField(step.VariableInt);
                    if (ni != step.VariableInt) { Undo.RecordObject(binding, "Edit var"); step.VariableInt = ni; EditorUtility.SetDirty(binding); }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Bool value:", "Значение (да/нет):"), GUILayout.Width(150));
                    bool nb = EditorGUILayout.Toggle(step.VariableBool);
                    if (nb != step.VariableBool) { Undo.RecordObject(binding, "Edit var"); step.VariableBool = nb; EditorUtility.SetDirty(binding); }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("String value:", "Значение (текст):"), GUILayout.Width(150));
                    string ns = EditorGUILayout.TextField(step.VariableString ?? "");
                    if (ns != step.VariableString) { Undo.RecordObject(binding, "Edit var"); step.VariableString = ns; EditorUtility.SetDirty(binding); }
                    GUILayout.EndHorizontal();

                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Only the value matching the variable's type (set in Variables module) is used at runtime — others are ignored.",
                            "В рантайме используется только значение нужного типа (тип задан в модуле Переменные) — остальные игнорируются."));
                    break;
                }

                case NovellaUIBinding.BindingAction.TriggerEvent:
                {
                    string n = TextRow(ToolLang.Get("Event name:", "Имя события:"), step.EventName, false, null);
                    if (n != step.EventName) { Undo.RecordObject(binding, "Edit event"); step.EventName = n; EditorUtility.SetDirty(binding); }
                    string p = TextRow(ToolLang.Get("Param (string):", "Параметр (строка):"), step.EventParam, showHints,
                        ToolLang.Get("Optional string passed to event listeners.",
                                     "Опциональная строка, передаётся подписчикам события."));
                    if (p != step.EventParam) { Undo.RecordObject(binding, "Edit event"); step.EventParam = p; EditorUtility.SetDirty(binding); }
                    break;
                }

                case NovellaUIBinding.BindingAction.PlaySFX:
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Sound clip:", "Звук:"), GUILayout.Width(150));
                    string label = step.SfxClip != null ? "🔊 " + step.SfxClip.name : ToolLang.Get("📂 Pick from Gallery…", "📂 Выбрать в Галерее…");
                    if (GUILayout.Button(label, EditorStyles.popup, GUILayout.Height(22)))
                    {
                        var captured = binding;
                        var capStep = step;
                        NovellaGalleryWindow.ShowWindow(asset =>
                        {
                            var clip = asset as AudioClip;
                            Undo.RecordObject(captured, "Pick SFX");
                            capStep.SfxClip = clip;
                            EditorUtility.SetDirty(captured);
                        }, NovellaGalleryWindow.EGalleryFilter.Audio);
                    }
                    if (step.SfxClip != null && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        Undo.RecordObject(binding, "Clear SFX");
                        step.SfxClip = null;
                        EditorUtility.SetDirty(binding);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Volume:", "Громкость:"), GUILayout.Width(150));
                    float nv = EditorGUILayout.Slider(step.SfxVolume, 0f, 1f);
                    if (!Mathf.Approximately(nv, step.SfxVolume)) { Undo.RecordObject(binding, "SFX volume"); step.SfxVolume = nv; EditorUtility.SetDirty(binding); }
                    GUILayout.EndHorizontal();

                    if (showHints)
                        DrawHint(ToolLang.Get("UI sound — fires once when this step runs.",
                                              "UI-звук — играется один раз когда выполняется этот шаг."));
                    break;
                }

                case NovellaUIBinding.BindingAction.UnlockAchievement:
                {
                    string n = TextRow(ToolLang.Get("Achievement ID:", "ID достижения:"), step.AchievementId, showHints,
                        ToolLang.Get("ID matching the platform achievement (Steam ID, etc).",
                                     "ID, совпадающий с платформенной ачивкой (Steam ID и т.п.)."));
                    if (n != step.AchievementId) { Undo.RecordObject(binding, "Edit achievement"); step.AchievementId = n; EditorUtility.SetDirty(binding); }
                    break;
                }

                // Действия без параметров (LoadLastSave, RestartChapter, QuitGame, PauseGame, ResumeGame, None).
                default: break;
            }
        }

        private static void DrawDelayField(NovellaUIBinding binding, NovellaUIBinding.ClickActionStep step, bool showHints)
        {
            // Показываем delay только если он >0 (чтобы не засорять интерфейс),
            // плюс маленькую кнопку «+ Добавить задержку» когда =0.
            if (step.DelayBefore > 0f)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Delay before (s):", "Задержка перед (с):"), GUILayout.Width(150));
                float nd = EditorGUILayout.FloatField(step.DelayBefore);
                if (!Mathf.Approximately(nd, step.DelayBefore))
                {
                    Undo.RecordObject(binding, "Edit delay");
                    step.DelayBefore = Mathf.Max(0f, nd);
                    EditorUtility.SetDirty(binding);
                }
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    Undo.RecordObject(binding, "Clear delay");
                    step.DelayBefore = 0f;
                    EditorUtility.SetDirty(binding);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
                st.normal.textColor = NovellaSettingsModule.GetTextDisabled();
                if (GUILayout.Button("⏱ " + ToolLang.Get("+ add delay before", "+ добавить задержку"), st, GUILayout.Height(14)))
                {
                    Undo.RecordObject(binding, "Add delay");
                    step.DelayBefore = 0.5f;
                    EditorUtility.SetDirty(binding);
                }
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────
        private static string TextRow(string label, string value, bool showHint, string hint)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150));
            string n = EditorGUILayout.TextField(value ?? "");
            GUILayout.EndHorizontal();
            if (showHint && !string.IsNullOrEmpty(hint)) DrawHint(hint);
            return n;
        }

        // Стиль идентичен DrawFieldHint в NovellaUIForge — единая визуальная
        // семья по всему инструменту: тонкая полоска акцента слева + мягкий
        // акцентный фон + hint-цвет текста + 💡 префикс.
        private static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st);
            Color acc = NovellaSettingsModule.GetAccentColor();
            EditorGUI.DrawRect(r, new Color(acc.r, acc.g, acc.b, 0.07f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), acc);
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(2);
        }

        private static void ShowAllBindingsMenu(NovellaUIBinding source, NovellaUIBinding.ClickActionStep step)
        {
            var menu = new GenericMenu();
            var all = UnityEngine.Object.FindObjectsByType<NovellaUIBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            menu.AddItem(new GUIContent(ToolLang.Get("(none)", "(не задано)")), string.IsNullOrEmpty(step.TargetBindingId), () =>
            {
                Undo.RecordObject(source, "Clear target");
                step.TargetBindingId = "";
                EditorUtility.SetDirty(source);
            });
            menu.AddSeparator("");
            foreach (var b in all)
            {
                if (b == null || b == source) continue;
                string id = b.Id;
                string display = string.IsNullOrEmpty(b.Name) ? b.gameObject.name : b.Name;
                var capId = id;
                menu.AddItem(new GUIContent(display + "    [" + id + "]"),
                    step.TargetBindingId == capId,
                    () =>
                    {
                        Undo.RecordObject(source, "Pick target");
                        step.TargetBindingId = capId;
                        EditorUtility.SetDirty(source);
                    });
            }
            menu.ShowAsContext();
        }
    }
}
