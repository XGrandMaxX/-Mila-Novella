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
            new ActionMeta { Action = NovellaUIBinding.BindingAction.SaveGameSlot,
                Category = ActionCategory.Game, Icon = "💾",
                LabelEN = "Save to slot", LabelRU = "Сохранить в слот",
                HintEN = "Saves current state to a named slot (1..N). Slot 0 = auto-save. Use with NovellaSaveSlotsUI for a slot-list UI.",
                HintRU = "Сохраняет состояние в именованный слот (1..N). Слот 0 = автосейв. Работает в паре с NovellaSaveSlotsUI для UI выбора слотов." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.LoadGameSlot,
                Category = ActionCategory.Game, Icon = "📂",
                LabelEN = "Load from slot", LabelRU = "Загрузить из слота",
                HintEN = "Loads game state from a named slot. If slot is empty — logs a warning and does nothing.",
                HintRU = "Загружает состояние из именованного слота. Если пусто — предупреждение в консоль и ничего не делает." },
            new ActionMeta { Action = NovellaUIBinding.BindingAction.ReturnToMainMenu,
                Category = ActionCategory.Game, Icon = "🏠",
                LabelEN = "Return to main menu", LabelRU = "Вернуться в главное меню",
                HintEN = "Loads the menu scene by name (or first scene in Build Settings if name is empty). Time.timeScale is restored to 1.",
                HintRU = "Грузит сцену меню по имени (или первую сцену из Build Settings если имя пустое). Time.timeScale возвращается в 1." },
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
                // Intro как карточка с булитами вместо одного слипшегося абзаца —
                // юзер за секунду видит 3 главных правила.
                var bulletSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 10, wordWrap = true,
                    normal = { textColor = NovellaSettingsModule.GetTextSecondary() },
                    padding = new RectOffset(10, 8, 4, 4)
                };
                string[] bullets = new[]
                {
                    ToolLang.Get(
                        "▸ Steps run top-to-bottom when the button is clicked.",
                        "▸ Шаги выполняются сверху вниз при нажатии на кнопку."),
                    ToolLang.Get(
                        "▸ Each step can have a delay (⏱) before it runs — useful for SFX → scene swap.",
                        "▸ У каждого шага можно задать задержку (⏱) перед запуском — удобно для SFX → смена сцены."),
                    ToolLang.Get(
                        "▸ After a terminal action (Quit / Start game / Return to menu) — steps below WON'T run.",
                        "▸ После терминального действия (Выход / Старт / Назад в меню) — шаги ниже НЕ выполнятся."),
                };

                Color acc = NovellaSettingsModule.GetAccentColor();
                foreach (var line in bullets)
                {
                    Rect lr = GUILayoutUtility.GetRect(new GUIContent(line), bulletSt);
                    EditorGUI.DrawRect(lr, new Color(acc.r, acc.g, acc.b, 0.05f));
                    EditorGUI.DrawRect(new Rect(lr.x, lr.y, 2, lr.height), new Color(acc.r, acc.g, acc.b, 0.7f));
                    GUI.Label(lr, line, bulletSt);
                    GUILayout.Space(2);
                }
            }
            GUILayout.Space(8);

            // ─── Hub-card field: имя binding'а ──────────────────────────────
            // Раньше тут был голый TextField с лейблом сверху — выглядело как
            // дефолтный Unity-инспектор. Сейчас — Hub-card с иконкой, тёмной
            // подложкой поля и слотом для кнопки-пикера справа.
            DrawHubField(
                icon: "🏷",
                label: ToolLang.Get("Display name", "Имя для пикера"),
                hint: ToolLang.Get(
                    "Friendly name shown in graph node pickers and the Bindings overview. Doesn't affect the game — only how YOU find this element later.",
                    "Дружелюбное имя — видно везде где этот элемент выбирается из ноды графа или таблицы Связей. На игру не влияет, нужно только тебе чтобы потом узнавать этот элемент в списке."),
                showHints: showHints,
                value: binding.Name ?? "",
                placeholder: ToolLang.Get("e.g. \"Save Button\"", "напр. «Кнопка сохранения»"),
                pickerIcon: null,
                onPickerClick: null,
                onValueChanged: v => {
                    if (v != binding.Name) { Undo.RecordObject(binding, "Rename binding"); binding.Name = v; EditorUtility.SetDirty(binding); }
                });

            GUILayout.Space(6);

            DrawHubField(
                icon: "🌐",
                label: ToolLang.Get("Localization key (optional)", "Ключ локализации (опц.)"),
                hint: ToolLang.Get(
                    "Connects this text to the Localization Table. When the player switches language, the text auto-updates. Leave empty if no translation is needed.",
                    "Связывает этот текст с таблицей локализации. При смене языка игроком текст обновится сам. Оставь пустым если переводить не нужно."),
                showHints: showHints,
                value: binding.LocalizationKey ?? "",
                placeholder: ToolLang.Get("e.g. \"menu.start\"", "напр. «menu.start»"),
                pickerIcon: "📂",
                onPickerClick: () => {
                    var capBinding = binding;
                    NovellaLocKeyPickerWindow.Open(binding.LocalizationKey, picked => {
                        Undo.RecordObject(capBinding, "Pick loc key");
                        capBinding.LocalizationKey = picked;
                        EditorUtility.SetDirty(capBinding);
                        capBinding.Refresh();
                    });
                },
                onValueChanged: v => {
                    if (v != binding.LocalizationKey) {
                        Undo.RecordObject(binding, "Edit loc key");
                        binding.LocalizationKey = v;
                        EditorUtility.SetDirty(binding);
                        binding.Refresh();
                    }
                });

            GUILayout.Space(6);

            DrawHubField(
                icon: "📊",
                label: ToolLang.Get("Variable (replaces {var} in text)", "Переменная (вместо {var} в тексте)"),
                hint: ToolLang.Get(
                    "Substitutes a NovellaVariables value into the {var} placeholder of the localized text.\nExample: text \"HP: {var}\" + variable PlayerHP → \"HP: 42\".",
                    "Подставляет значение NovellaVariables вместо плейсхолдера {var} в локализованном тексте.\nПример: текст «HP: {var}» + переменная PlayerHP → «HP: 42»."),
                showHints: showHints,
                value: binding.BoundVariable ?? "",
                placeholder: ToolLang.Get("e.g. \"PlayerHP\"", "напр. «PlayerHP»"),
                pickerIcon: "📂",
                onPickerClick: () => {
                    var capBinding = binding;
                    NovellaVariablePickerWindow.Open(binding.BoundVariable, picked => {
                        Undo.RecordObject(capBinding, "Pick variable");
                        capBinding.BoundVariable = picked;
                        EditorUtility.SetDirty(capBinding);
                        capBinding.Refresh();
                    });
                },
                onValueChanged: v => {
                    if (v != binding.BoundVariable) {
                        Undo.RecordObject(binding, "Edit bound var");
                        binding.BoundVariable = v;
                        EditorUtility.SetDirty(binding);
                        binding.Refresh();
                    }
                });

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

                // ─── Проверка «Pause без Resume» ────────────────────────────
                // Pause без парного Resume замораживает игру навсегда. Резумы могут
                // быть на ДРУГОЙ кнопке (типичный паттерн: «Пауза» открывает панель,
                // «Продолжить» внутри панели делает Resume), поэтому ищем по ВСЕЙ сцене.
                bool hasPauseHere = false;
                for (int i = 0; i < binding.ClickSequence.Count; i++)
                    if (binding.ClickSequence[i].Action == NovellaUIBinding.BindingAction.PauseGame) { hasPauseHere = true; break; }
                if (hasPauseHere)
                {
                    bool sceneHasResume = false;
                    // Используем кешированный массив (см. GetBindingsCached) —
                    // FindObjectsByType слишком дорогой на каждый OnGUI кадр.
                    var allBindings = GetBindingsCached();
                    for (int b = 0; b < allBindings.Length && !sceneHasResume; b++)
                    {
                        var bb = allBindings[b];
                        if (bb == null || bb.ClickSequence == null) continue;
                        for (int s = 0; s < bb.ClickSequence.Count; s++)
                            if (bb.ClickSequence[s].Action == NovellaUIBinding.BindingAction.ResumeGame) { sceneHasResume = true; break; }
                    }
                    if (!sceneHasResume)
                    {
                        GUILayout.Space(8);
                        Rect warnRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(warnRect, new Color(0.42f, 0.30f, 0.10f, 0.30f));
                        EditorGUI.DrawRect(new Rect(warnRect.x, warnRect.y, 3, warnRect.height),
                            new Color(0.95f, 0.78f, 0.30f, 1f));
                        var ws = new GUIStyle(EditorStyles.label) {
                            fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = true,
                            alignment = TextAnchor.MiddleLeft,
                            normal = { textColor = new Color(0.95f, 0.78f, 0.30f, 1f) },
                            padding = new RectOffset(11, 8, 0, 0)
                        };
                        GUI.Label(warnRect, ToolLang.Get(
                            "⚠ This sequence pauses the game but no other binding in the scene has a Resume action — game will freeze forever.",
                            "⚠ Эта последовательность ставит паузу, но нигде в сцене нет Resume — игра застрянет навсегда."), ws);
                    }
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
                    // NovellaStoryPickerPopup ищет истории через AssetDatabase.FindAssets
                    // ("t:NovellaStory") — находит ассеты ВЕЗДЕ в проекте, включая
                    // Resources/Stories. Старый вариант (Gallery + EGalleryFilter.Story)
                    // открывал галерею в папке Assets/NovellaEngine, которая не
                    // содержит истории — у юзера показывался пустой список.
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Story:", "История:"), GUILayout.Width(150));
                    string label = step.StoryToStart != null
                        ? "📖 " + step.StoryToStart.name
                        : ToolLang.Get("(any first available)", "(любая первая)");

                    Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(label), EditorStyles.popup, GUILayout.Height(22), GUILayout.ExpandWidth(true));
                    if (GUI.Button(btnRect, label, EditorStyles.popup))
                    {
                        var captured = binding;
                        var capStep = step;
                        string activeGuid = "";
                        if (capStep.StoryToStart != null)
                        {
                            activeGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(capStep.StoryToStart));
                        }
                        Vector2 popupPos = GUIUtility.GUIToScreenPoint(new Vector2(btnRect.x, btnRect.yMax + 2));
                        NovellaStoryPickerPopup.Open(popupPos, activeGuid,
                            pickedGuid =>
                            {
                                NovellaStory picked = null;
                                if (!string.IsNullOrEmpty(pickedGuid))
                                {
                                    string p = AssetDatabase.GUIDToAssetPath(pickedGuid);
                                    if (!string.IsNullOrEmpty(p))
                                        picked = AssetDatabase.LoadAssetAtPath<NovellaStory>(p);
                                }
                                Undo.RecordObject(captured, "Pick story");
                                capStep.StoryToStart = picked;
                                EditorUtility.SetDirty(captured);
                            },
                            null /* onCreateNew — пока не нужен из этого контекста */);
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
                    // Используем NovellaUIPickerWindow — визуальный выбор элемента
                    // в сцене с превью и подсветкой (раньше тут был сухой GenericMenu
                    // со списком ID-строк, в котором юзер не видел что выбирает).
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Target panel:", "Целевая панель:"), GUILayout.Width(150));
                    string current = string.IsNullOrEmpty(step.TargetBindingId)
                        ? ToolLang.Get("(pick a UI element)", "(выбрать элемент UI)")
                        : "🎯 " + ResolveBindingDisplay(step.TargetBindingId);
                    if (GUILayout.Button(current, EditorStyles.popup, GUILayout.Height(22)))
                    {
                        var capBinding = binding;
                        var capStep = step;
                        NovellaUIPickerWindow.Open(
                            ToolLang.Get("Pick UI panel", "Выбор UI-панели"),
                            UIBindingKind.Any,
                            capStep.TargetBindingId,
                            picked =>
                            {
                                Undo.RecordObject(capBinding, "Pick panel");
                                capStep.TargetBindingId = picked;
                                EditorUtility.SetDirty(capBinding);
                            });
                    }
                    if (!string.IsNullOrEmpty(step.TargetBindingId) && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        Undo.RecordObject(binding, "Clear panel");
                        step.TargetBindingId = "";
                        EditorUtility.SetDirty(binding);
                    }
                    GUILayout.EndHorizontal();

                    // ─── Sanity-warning: HidePanel над уже-выключенным target'ом ───
                    // Если кнопка прячет panel, которая И ТАК выключена в сцене —
                    // действие бесполезно. Юзеру наверняка нужен ShowPanel.
                    if (step.Action == NovellaUIBinding.BindingAction.HidePanel
                        && !string.IsNullOrEmpty(step.TargetBindingId))
                    {
                        var targetBinding = NovellaUIBinding.Find(step.TargetBindingId);
                        if (targetBinding != null && !targetBinding.gameObject.activeInHierarchy)
                        {
                            DrawWarn(ToolLang.Get(
                                "⚠ This panel is already disabled in the scene — Hide does nothing here. Did you mean Show?",
                                "⚠ Эта панель и так выключена в сцене — Hide здесь бесполезен. Может, имелось в виду Show?"));
                        }
                    }

                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Opens the visual UI Picker — click an element on the live scene preview to bind it. The action will activate / deactivate / toggle that element.",
                            "Открывает визуальный UI-пикер — кликни по элементу на превью сцены чтобы привязать его. Действие включит / выключит / переключит этот элемент."));
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
                    // Variable picker (раньше тут был голый TextField — юзер
                    // должен был помнить точное имя переменной, что приводило
                    // к молча сломанной логике из-за опечаток).
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Variable:", "Переменная:"), GUILayout.Width(150));
                    string varLabel = string.IsNullOrEmpty(step.VariableName)
                        ? ToolLang.Get("(pick a variable)", "(выбери переменную)")
                        : "📊 " + step.VariableName;
                    if (GUILayout.Button(varLabel, EditorStyles.popup, GUILayout.Height(22)))
                    {
                        var captured = binding;
                        var capStep = step;
                        NovellaVariablePickerWindow.Open(capStep.VariableName, picked =>
                        {
                            Undo.RecordObject(captured, "Pick variable");
                            capStep.VariableName = picked;
                            EditorUtility.SetDirty(captured);
                        });
                    }
                    if (!string.IsNullOrEmpty(step.VariableName) && GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        Undo.RecordObject(binding, "Clear variable");
                        step.VariableName = "";
                        EditorUtility.SetDirty(binding);
                    }
                    GUILayout.EndHorizontal();

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

                // ─── Save slot actions ────────────────────────────────────────
                case NovellaUIBinding.BindingAction.SaveGameSlot:
                case NovellaUIBinding.BindingAction.LoadGameSlot:
                {
                    bool isSave = step.Action == NovellaUIBinding.BindingAction.SaveGameSlot;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Slot:", "Слот:"), GUILayout.Width(150));
                    string slotLabel = step.SaveSlotIndex == 0
                        ? "⚡ " + ToolLang.Get("Auto", "Автосейв")
                        : "💾 " + ToolLang.Get($"Slot {step.SaveSlotIndex}", $"Слот {step.SaveSlotIndex}");
                    Rect slotBtn = GUILayoutUtility.GetRect(new GUIContent(slotLabel), EditorStyles.popup,
                        GUILayout.Height(22), GUILayout.ExpandWidth(true));
                    if (GUI.Button(slotBtn, slotLabel, EditorStyles.popup))
                    {
                        var capBinding = binding;
                        var capStep = step;
                        Vector2 popupPos = GUIUtility.GUIToScreenPoint(new Vector2(slotBtn.x, slotBtn.yMax + 2));
                        NovellaSaveSlotPickerWindow.Open(popupPos,
                            isSave ? NovellaSaveSlotPickerWindow.Mode.Save : NovellaSaveSlotPickerWindow.Mode.Load,
                            capStep.SaveSlotIndex,
                            picked =>
                            {
                                Undo.RecordObject(capBinding, "Pick slot");
                                capStep.SaveSlotIndex = picked;
                                EditorUtility.SetDirty(capBinding);
                            });
                    }
                    GUILayout.EndHorizontal();
                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Click to pick a slot — visual grid shows previews of existing saves.",
                            "Клик — откроется сетка слотов с превью существующих сохранений."));
                    break;
                }

                case NovellaUIBinding.BindingAction.ReturnToMainMenu:
                {
                    // Picker сцен из Build Settings — карточками с иконками,
                    // build-индексами и status-плашками. Раньше был EditorGUILayout.Popup,
                    // некрасивый и без контекста.
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Menu scene:", "Сцена меню:"), GUILayout.Width(150));
                    // Лейбл кнопки — короткий чтобы не разваливал layout инспектора.
                    // Раньше «Auto: первая Build-сцена» (28 символов) выезжал за поле.
                    string sceneLabel;
                    if (string.IsNullOrEmpty(step.MainMenuSceneName))
                        sceneLabel = "✨ " + ToolLang.Get("Auto", "Авто");
                    else
                        sceneLabel = "🎬 " + (step.MainMenuSceneName.Length > 22
                            ? step.MainMenuSceneName.Substring(0, 20) + "…"
                            : step.MainMenuSceneName);
                    var sceneBtnSt = new GUIStyle(EditorStyles.popup) {
                        clipping = TextClipping.Clip
                    };
                    Rect btnRect = GUILayoutUtility.GetRect(new GUIContent(sceneLabel,
                            string.IsNullOrEmpty(step.MainMenuSceneName)
                                ? ToolLang.Get("Auto: loads the first scene from Build Settings.",
                                               "Авто: грузит первую сцену из Build Settings.")
                                : step.MainMenuSceneName),
                        sceneBtnSt, GUILayout.Height(22), GUILayout.ExpandWidth(true));
                    if (GUI.Button(btnRect, sceneLabel, sceneBtnSt))
                    {
                        var capBinding = binding;
                        var capStep = step;
                        Vector2 popupPos = GUIUtility.GUIToScreenPoint(new Vector2(btnRect.x, btnRect.yMax + 2));
                        NovellaScenePickerPopup.Open(popupPos, capStep.MainMenuSceneName,
                            picked =>
                            {
                                Undo.RecordObject(capBinding, "Edit menu scene");
                                capStep.MainMenuSceneName = picked ?? "";
                                EditorUtility.SetDirty(capBinding);
                            });
                    }
                    GUILayout.EndHorizontal();
                    if (showHints)
                        DrawHint(ToolLang.Get(
                            "Click to pick a scene from Build Settings — visual cards with build indices.",
                            "Клик — откроется визуальный выбор сцены из Build Settings с карточками."));
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

        /// <summary>
        /// Hub-style карточка для одного поля ввода: иконка-чип + лейбл + тёмная
        /// подложка под TextField + опциональная кнопка-пикер справа + опциональный hint.
        /// Поддерживает: focus-highlight (cyan border при фокусе), filled-state-glow
        /// (тонкая cyan-полоса слева когда поле заполнено), hover-tone, ✓-индикатор.
        /// Заменяет старые «голые» Label + TextField — даёт связке полей современный вид.
        /// </summary>
        private static void DrawHubField(
            string icon, string label, string hint, bool showHints,
            string value, string placeholder,
            string pickerIcon, System.Action onPickerClick,
            System.Action<string> onValueChanged)
        {
            bool isFilled = !string.IsNullOrEmpty(value);

            // ─── Header row: иконка-чип + лейбл + (✓ если заполнено) ───
            Rect headerRow = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            float chipSize = 16f;
            Rect chipRect = new Rect(headerRow.x, headerRow.y + 1, chipSize, chipSize);
            // Чип под иконкой — Hub-tip-tone с cyan-tint бордером.
            Color tipBg = Color.Lerp(NovellaSettingsModule.GetInterfaceColor(),
                                     NovellaSettingsModule.GetAccentColor(), 0.10f);
            Color acc = NovellaSettingsModule.GetAccentColor();
            EditorGUI.DrawRect(chipRect, tipBg);
            EditorGUI.DrawRect(new Rect(chipRect.x, chipRect.y, chipRect.width, 1), new Color(acc.r, acc.g, acc.b, 0.4f));
            EditorGUI.DrawRect(new Rect(chipRect.x, chipRect.yMax - 1, chipRect.width, 1), new Color(acc.r, acc.g, acc.b, 0.4f));
            EditorGUI.DrawRect(new Rect(chipRect.x, chipRect.y, 1, chipRect.height), new Color(acc.r, acc.g, acc.b, 0.4f));
            EditorGUI.DrawRect(new Rect(chipRect.xMax - 1, chipRect.y, 1, chipRect.height), new Color(acc.r, acc.g, acc.b, 0.4f));

            var chipSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = NovellaSettingsModule.GetTextColor() }
            };
            GUI.Label(chipRect, icon ?? "", chipSt);

            var lblSt = new GUIStyle(EditorStyles.miniBoldLabel) {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = NovellaSettingsModule.GetTextSecondary() }
            };
            GUI.Label(new Rect(headerRow.x + chipSize + 6, headerRow.y, headerRow.width - chipSize - 6 - 18, 18),
                label, lblSt);

            // Маленький ✓ справа если поле заполнено — позитивный feedback.
            if (isFilled)
            {
                var checkSt = new GUIStyle(EditorStyles.miniBoldLabel) {
                    fontSize = 11, alignment = TextAnchor.MiddleRight,
                    normal = { textColor = new Color(0.30f, 0.85f, 0.45f, 1f) }
                };
                GUI.Label(new Rect(headerRow.xMax - 18, headerRow.y, 16, 18), "✓", checkSt);
            }

            GUILayout.Space(3);

            // ─── Field row: TextField + picker button ───
            float fieldH = 26f;
            float pickerW = string.IsNullOrEmpty(pickerIcon) ? 0f : 32f;
            Rect bg = GUILayoutUtility.GetRect(0, fieldH, GUILayout.ExpandWidth(true));

            // Уникальное имя контрола = label (для focus-tracking).
            string ctrlName = "HubField_" + label;
            bool isFocused = GUI.GetNameOfFocusedControl() == ctrlName;
            bool isHover = bg.Contains(Event.current.mousePosition);

            // Цвета: фон базовый, при фокусе — чуть светлее.
            Color cardBg = NovellaSettingsModule.GetBgRaisedColor();
            if (isFocused) cardBg = Color.Lerp(cardBg, acc, 0.06f);
            else if (isHover) cardBg = Color.Lerp(cardBg, NovellaSettingsModule.GetTextColor(), 0.03f);
            EditorGUI.DrawRect(bg, cardBg);

            // Border: при фокусе — cyan (1.5px эффектом — две линии), иначе стандартный.
            Color borderC = isFocused ? acc : NovellaSettingsModule.GetBorderColor();
            float borderW = isFocused ? 1.4f : 1f;
            // Аппроксимируем 1.4px двумя линиями: 1px чёткой + 1px полупрозрачной.
            void DrawSide(Rect side) { EditorGUI.DrawRect(side, borderC); }
            DrawSide(new Rect(bg.x, bg.y, bg.width, 1));
            DrawSide(new Rect(bg.x, bg.yMax - 1, bg.width, 1));
            DrawSide(new Rect(bg.x, bg.y, 1, bg.height));
            DrawSide(new Rect(bg.xMax - 1, bg.y, 1, bg.height));
            if (isFocused)
            {
                Color softer = new Color(borderC.r, borderC.g, borderC.b, 0.45f);
                EditorGUI.DrawRect(new Rect(bg.x + 1, bg.y + 1, bg.width - 2, 1), softer);
                EditorGUI.DrawRect(new Rect(bg.x + 1, bg.yMax - 2, bg.width - 2, 1), softer);
            }

            // Filled-state cyan-полоса слева — даёт «pop» когда поле заполнено,
            // визуально отделяет filled от empty без агрессии.
            if (isFilled)
            {
                EditorGUI.DrawRect(new Rect(bg.x, bg.y, 3, bg.height),
                    new Color(acc.r, acc.g, acc.b, isFocused ? 1f : 0.85f));
            }

            // Текст-филд.
            Rect fieldRect = new Rect(bg.x + (isFilled ? 10 : 8), bg.y + 4,
                bg.width - (isFilled ? 18 : 16) - pickerW, bg.height - 8);
            var fieldSt = new GUIStyle(EditorStyles.textField) {
                fontSize = 12,
                normal  = { background = null, textColor = NovellaSettingsModule.GetTextColor() },
                focused = { background = null, textColor = NovellaSettingsModule.GetTextColor() },
                hover   = { background = null, textColor = NovellaSettingsModule.GetTextColor() },
                active  = { background = null, textColor = NovellaSettingsModule.GetTextColor() },
                padding = new RectOffset(2, 2, 2, 2),
                border  = new RectOffset(0, 0, 0, 0),
            };
            GUI.SetNextControlName(ctrlName);
            string newValue = EditorGUI.TextField(fieldRect, value ?? "", fieldSt);
            if (newValue != value) onValueChanged?.Invoke(newValue);

            // Placeholder (только если поле пустое и не в фокусе).
            if (!isFilled && !string.IsNullOrEmpty(placeholder) && !isFocused)
            {
                var phSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 12, fontStyle = FontStyle.Italic,
                    normal = { textColor = NovellaSettingsModule.GetTextDisabled() },
                    padding = new RectOffset(2, 2, 2, 2),
                };
                Rect phRect = new Rect(fieldRect.x + 2, fieldRect.y, fieldRect.width - 4, fieldRect.height);
                GUI.Label(phRect, placeholder, phSt);
            }

            // Кнопка пикера справа.
            if (!string.IsNullOrEmpty(pickerIcon) && onPickerClick != null)
            {
                Rect pickerRect = new Rect(bg.xMax - pickerW + 1, bg.y + 1, pickerW - 2, bg.height - 2);
                bool pickerHover = pickerRect.Contains(Event.current.mousePosition);
                Color pickerBg = pickerHover
                    ? new Color(acc.r, acc.g, acc.b, 0.20f)
                    : new Color(acc.r, acc.g, acc.b, 0.08f);
                EditorGUI.DrawRect(pickerRect, pickerBg);
                EditorGUI.DrawRect(new Rect(pickerRect.x, pickerRect.y, 1, pickerRect.height),
                    new Color(acc.r, acc.g, acc.b, 0.40f));

                var pickerSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = pickerHover ? NovellaSettingsModule.GetTextColor() : acc }
                };
                EditorGUIUtility.AddCursorRect(pickerRect, MouseCursor.Link);
                if (GUI.Button(pickerRect, GUIContent.none, GUIStyle.none))
                {
                    onPickerClick.Invoke();
                }
                GUI.Label(pickerRect, pickerIcon, pickerSt);
            }

            if (showHints && !string.IsNullOrEmpty(hint))
            {
                GUILayout.Space(3);
                DrawHint(hint);
            }
        }

        // ─── Cached binding lookup ─────────────────────────────────────────────
        // Раньше ResolveBindingDisplay и Pause-Resume warning вызывали
        // Object.FindObjectsByType<NovellaUIBinding> прямо в OnGUI — это O(N всех
        // объектов сцены) на КАЖДЫЙ ShowPanel/HidePanel/TogglePanel шаг + 1 на
        // Pause-Resume проверку. С несколькими шагами на 60fps Кузница лагала.
        // Теперь массив кешируется на 0.5с — юзер не видит разницы, CPU отдыхает.
        private static NovellaUIBinding[] _bindingsCache;
        private static double _bindingsCacheTime = -1;
        private const double BINDINGS_CACHE_TTL = 0.5;

        private static NovellaUIBinding[] GetBindingsCached()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_bindingsCache == null || now - _bindingsCacheTime > BINDINGS_CACHE_TTL)
            {
                _bindingsCache = UnityEngine.Object.FindObjectsByType<NovellaUIBinding>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                _bindingsCacheTime = now;
            }
            return _bindingsCache;
        }

        /// <summary>
        /// Резолвит binding-id в человекочитаемое имя для показа на кнопках
        /// «Target panel». Сначала пробует быстрый static-реестр Awake-зарегистрированных
        /// bindings. Если ID нет в реестре (binding на отключённом GO или мок-сцена,
        /// где Awake не вызывался) — идёт в кешированный массив (~раз в 0.5с).
        /// </summary>
        private static string ResolveBindingDisplay(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";

            // Сначала пробуем static-реестр (O(1)).
            var fromRegistry = NovellaUIBinding.Find(id);
            if (fromRegistry != null)
            {
                string n = string.IsNullOrEmpty(fromRegistry.Name) ? fromRegistry.gameObject.name : fromRegistry.Name;
                return TruncateNice(n);
            }

            // Edit-режим: реестр пустой, идём через КЕШИРОВАННЫЙ массив.
            var all = GetBindingsCached();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].Id == id)
                {
                    string n = string.IsNullOrEmpty(all[i].Name) ? all[i].gameObject.name : all[i].Name;
                    return TruncateNice(n);
                }
            }

            // Stale ID — обрезаем до 12 символов с префиксом «?».
            return id.Length > 12 ? "?" + id.Substring(0, 10) + "…" : "?" + id;
        }

        // Усекает имя если оно слишком длинное (чтобы не разваливать layout).
        private static string TruncateNice(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > 28 ? s.Substring(0, 26) + "…" : s;
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

        // Жёлтое-оранжевое предупреждение в том же стиле что DrawHint, но
        // ярче и заметнее. Используется для sanity-warning'ов
        // (Pause без Resume, Hide уже выключенной панели и т.п.).
        private static void DrawWarn(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.label) {
                fontSize = 11, fontStyle = FontStyle.Bold,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };
            st.normal.textColor = new Color(0.95f, 0.78f, 0.30f, 1f);
            Rect r = GUILayoutUtility.GetRect(new GUIContent(text), st);
            EditorGUI.DrawRect(r, new Color(0.95f, 0.78f, 0.30f, 0.10f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), new Color(0.95f, 0.78f, 0.30f, 1f));
            GUI.Label(r, text, st);
            GUILayout.Space(2);
        }

    }
}
