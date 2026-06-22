/// <summary>
/// ОТВЕЧАЕТ ЗА:
/// 1. Отрисовку всех интерфейсов в правой панели (Инспекторе) при клике на любую ноду.
/// 2. Live-превью сцены: синхронизирует изменения в инспекторе с реальной игровой сценой.
/// 3. Автоматическую генерацию интерфейса для Custom DLC нод.
/// </summary>
using NovellaEngine.Data;
using NovellaEngine.Runtime;
using NovellaEngine.DLC.Wardrobe;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public class NovellaNodeInspectorUI
    {
        private NovellaTree _currentTree;
        private SerializedObject _serializedObject;
        private NovellaGraphView _graphView;
        private NovellaGraphWindow _window;
        private Vector2 _scrollPos;
        private Action _onMarkUnsaved;
        private NovellaNodeBase _lastSyncedNode;
        private int _activePreviewLineIndex = 0;
        private SerializedProperty _cachedNodeProp;
        // Скролл для списка активных персонажей. Когда > SCROLL_THRESHOLD
        // персонажей, список оборачивается в ScrollView фиксированной высоты,
        // чтобы инспектор не превращался в бесконечно растущую простыню.
        private Vector2 _activeCharsScroll;
        private const int  ACTIVE_CHARS_SCROLL_THRESHOLD = 7;
        // Высота карточки персонажа (примерно): 28px контент + 4px gap.
        private const float ACTIVE_CHAR_CARD_HEIGHT = 36f;

        public NovellaNodeInspectorUI(NovellaTree tree, NovellaGraphView graphView, Action onMarkUnsaved, NovellaGraphWindow window)
        {
            _currentTree = tree; _graphView = graphView; _onMarkUnsaved = onMarkUnsaved; _window = window;
            if (_currentTree != null) _serializedObject = new SerializedObject(_currentTree);
        }

        public void SetGraphView(NovellaGraphView gv) => _graphView = gv;

        // Кнопка выбора переменной → открывает ВИЗУАЛЬНОЕ окно
        // NovellaVariablePickerWindow (группировка по типам, поиск, иконки)
        // вместо плоского enum-popup. Значение приходит через callback.
        private void DrawVariablePicker(string current, Action<string> onPicked, params GUILayoutOption[] options)
        {
            GUILayout.BeginHorizontal();

            var settings = NovellaVariableSettings.Instance;
            if (settings == null || settings.Variables.Count == 0)
            {
                if (NovellaInspectorChrome.DrawSlimBtn(ToolLang.Get("+ Create a variable first", "+ Сначала создай переменную"), options))
                    NovellaVariableEditorModule.ShowWindow();
                GUILayout.EndHorizontal();
                return;
            }

            bool known  = !string.IsNullOrEmpty(current) && settings.Variables.Any(v => v.Name == current);
            bool orphan = !string.IsNullOrEmpty(current) && !known;

            string label;
            if (orphan)
                label = ToolLang.Get($"⚠ missing: {current}", $"⚠ нет: {current}");
            else if (string.IsNullOrEmpty(current))
                label = ToolLang.Get("📊  — pick variable —", "📊  — выбери переменную —");
            else
            {
                var def = settings.Variables.FirstOrDefault(v => v.Name == current);
                string gem = (def != null && def.Type == EVarType.Integer && def.IsPremiumCurrency) ? "💎 " : "";
                label = "📊  " + gem + current;
            }

            Color prevBg = GUI.backgroundColor;
            if (orphan) GUI.backgroundColor = new Color(0.95f, 0.55f, 0.40f);
            if (GUILayout.Button(label, EditorStyles.popup, options))
            {
                NovellaEngine.Editor.UIBindings.NovellaVariablePickerWindow.Open(current, picked =>
                {
                    Undo.RecordObject(_currentTree, "Pick Variable");
                    onPicked?.Invoke(picked);
                    _onMarkUnsaved?.Invoke();
                    _window?.Repaint();
                });
            }
            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(25), GUILayout.Height(18)))
                NovellaVariableEditorModule.ShowWindow(orphan ? null : current);

            GUILayout.EndHorizontal();
        }

        private EVarType GetVarType(string varName)
        {
            var settings = NovellaVariableSettings.Instance;
            var def = settings.Variables.FirstOrDefault(v => v.Name == varName);
            return def != null ? def.Type : EVarType.Integer;
        }

        // Возвращает массив значений для Choice-переменной (если она Choice).
        // Иначе пустой массив. Используется при отрисовке Popup в Variable/Condition нодах.
        private string[] GetVarChoices(string varName)
        {
            var settings = NovellaVariableSettings.Instance;
            var def = settings.Variables.FirstOrDefault(v => v.Name == varName);
            if (def == null || def.Type != EVarType.Choice || def.Choices == null) return new string[0];
            // Подменяем '/' на '-' — Unity Popup трактует слэш как подменю.
            return def.Choices.Select(c => (c ?? "").Replace("/", "-")).ToArray();
        }

        private void DrawTypeHint(EVarType type)
        {
            string hint = type switch
            {
                EVarType.Integer => ToolLang.Get("Type: Number (Integer)", "Тип: Число (Целое)"),
                EVarType.Boolean => ToolLang.Get("Type: Boolean (True or False)", "Тип: Переключатель (Да или Нет)"),
                EVarType.String => ToolLang.Get("Type: String (Text)", "Тип: Строка (Текст)"),
                EVarType.Float => ToolLang.Get("Type: Decimal (Float)", "Тип: Дробное число (Float)"),
                EVarType.Choice => ToolLang.Get("Type: One of values (Choice)", "Тип: Из списка (Choice)"),
                EVarType.List => ToolLang.Get("Type: List (Collection)", "Тип: Список"),
                _ => ""
            };
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(hint, EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndHorizontal();
        }

        // ─── ВЫБОР ОПЕРАТОРА + ЗНАЧЕНИЯ ДЛЯ CONDITION/MODIFIER ─────────────
        // Едиая точка отрисовки части «оператор + значение» для условий разных
        // типов переменной. Используется в Condition node + Random ChanceModifier.
        // Возвращает true если что-то поменялось (для outer ChangeCheck).
        private void DrawConditionOperatorAndValue(string varName, ChoiceCondition cond,
                                                    float opW, float valW)
        {
            EVarType type = GetVarType(varName);

            if (type == EVarType.Integer)
            {
                string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                cond.Operator = (EConditionOperator)EditorGUILayout.Popup((int)cond.Operator, opNames, GUILayout.Width(opW));
                cond.Value = EditorGUILayout.IntField(cond.Value, GUILayout.Width(valW));
            }
            else if (type == EVarType.Float)
            {
                string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                cond.Operator = (EConditionOperator)EditorGUILayout.Popup((int)cond.Operator, opNames, GUILayout.Width(opW));
                cond.ValueFloat = EditorGUILayout.FloatField(cond.ValueFloat, GUILayout.Width(valW));
            }
            else if (type == EVarType.Boolean)
            {
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, cond.Operator);
                if (idx == -1) idx = 0;
                cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];
                cond.ValueBool = EditorGUILayout.Toggle(cond.ValueBool, GUILayout.Width(valW));
            }
            else if (type == EVarType.String)
            {
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, cond.Operator);
                if (idx == -1) idx = 0;
                cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];
                cond.ValueString = EditorGUILayout.TextField(cond.ValueString ?? "", GUILayout.Width(valW));
            }
            else if (type == EVarType.Choice)
            {
                // Оператор: == / !=. Значение — popup из allowed values.
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, cond.Operator);
                if (idx == -1) idx = 0;
                cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];

                string[] choices = GetVarChoices(varName);
                if (choices.Length == 0)
                {
                    GUILayout.Label(ToolLang.Get("(no values)", "(нет значений)"),
                        EditorStyles.miniLabel, GUILayout.Width(valW));
                }
                else
                {
                    int sel = Array.IndexOf(choices, (cond.ValueString ?? "").Replace("/", "-"));
                    if (sel < 0) sel = 0;
                    int newSel = EditorGUILayout.Popup(sel, choices, GUILayout.Width(valW));
                    if (newSel != sel) cond.ValueString = choices[newSel];
                    else if (sel >= 0 && sel < choices.Length && string.IsNullOrEmpty(cond.ValueString))
                        cond.ValueString = choices[0];
                }
            }
            else if (type == EVarType.List)
            {
                // Список операторов для List: contains/not contains + count comparisons.
                // Index в массиве = индекс в opNames.
                string[] opNames = ToolLang.IsRU
                    ? new[] { "содержит", "не содержит", "кол. ==", "кол. !=", "кол. >", "кол. <", "кол. >=", "кол. <=" }
                    : new[] { "contains", "not contains", "count ==", "count !=", "count >", "count <", "count >=", "count <=" };
                EConditionOperator[] opMap = {
                    EConditionOperator.Contains, EConditionOperator.NotContains,
                    EConditionOperator.Equal, EConditionOperator.NotEqual,
                    EConditionOperator.Greater, EConditionOperator.Less,
                    EConditionOperator.GreaterOrEqual, EConditionOperator.LessOrEqual
                };
                int idx = Array.IndexOf(opMap, cond.Operator);
                if (idx == -1) idx = 0;
                int newIdx = EditorGUILayout.Popup(idx, opNames, GUILayout.Width(opW + 30));
                cond.Operator = opMap[newIdx];

                // Под Contains/NotContains — текстовое поле (имя элемента).
                // Под count comparisons — int.
                if (cond.Operator == EConditionOperator.Contains || cond.Operator == EConditionOperator.NotContains)
                {
                    cond.ValueString = EditorGUILayout.TextField(cond.ValueString ?? "", GUILayout.Width(valW));
                }
                else
                {
                    cond.Value = EditorGUILayout.IntField(cond.Value, GUILayout.Width(valW));
                }
            }
        }

        // Перегрузка для ChanceModifier (тот же UI, что у ChoiceCondition).
        private void DrawConditionOperatorAndValue(string varName, ChanceModifier mod,
                                                    float opW, float valW)
        {
            // Делегируем через временный ChoiceCondition? — нет, прямее повторить
            // ту же логику; но чтобы не дублировать, маппим поля.
            // ChanceModifier поля идентичны: Operator/Value/ValueBool/ValueString/ValueFloat.
            EVarType type = GetVarType(varName);

            if (type == EVarType.Integer)
            {
                string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                mod.Operator = (EConditionOperator)EditorGUILayout.Popup((int)mod.Operator, opNames, GUILayout.Width(opW));
                mod.Value = EditorGUILayout.IntField(mod.Value, GUILayout.Width(valW));
            }
            else if (type == EVarType.Float)
            {
                string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                mod.Operator = (EConditionOperator)EditorGUILayout.Popup((int)mod.Operator, opNames, GUILayout.Width(opW));
                mod.ValueFloat = EditorGUILayout.FloatField(mod.ValueFloat, GUILayout.Width(valW));
            }
            else if (type == EVarType.Boolean)
            {
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, mod.Operator);
                if (idx == -1) idx = 0;
                mod.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];
                mod.ValueBool = EditorGUILayout.Toggle(mod.ValueBool, GUILayout.Width(valW));
            }
            else if (type == EVarType.String)
            {
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, mod.Operator);
                if (idx == -1) idx = 0;
                mod.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];
                mod.ValueString = EditorGUILayout.TextField(mod.ValueString ?? "", GUILayout.Width(valW));
            }
            else if (type == EVarType.Choice)
            {
                string[] eqNames = { "==", "!=" };
                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                int idx = Array.IndexOf(eqOps, mod.Operator);
                if (idx == -1) idx = 0;
                mod.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(opW))];

                string[] choices = GetVarChoices(varName);
                if (choices.Length == 0)
                {
                    GUILayout.Label(ToolLang.Get("(no values)", "(нет значений)"),
                        EditorStyles.miniLabel, GUILayout.Width(valW));
                }
                else
                {
                    int sel = Array.IndexOf(choices, (mod.ValueString ?? "").Replace("/", "-"));
                    if (sel < 0) sel = 0;
                    int newSel = EditorGUILayout.Popup(sel, choices, GUILayout.Width(valW));
                    if (newSel != sel) mod.ValueString = choices[newSel];
                    else if (string.IsNullOrEmpty(mod.ValueString)) mod.ValueString = choices[0];
                }
            }
            else if (type == EVarType.List)
            {
                string[] opNames = ToolLang.IsRU
                    ? new[] { "содержит", "не содержит", "кол. ==", "кол. !=", "кол. >", "кол. <", "кол. >=", "кол. <=" }
                    : new[] { "contains", "not contains", "count ==", "count !=", "count >", "count <", "count >=", "count <=" };
                EConditionOperator[] opMap = {
                    EConditionOperator.Contains, EConditionOperator.NotContains,
                    EConditionOperator.Equal, EConditionOperator.NotEqual,
                    EConditionOperator.Greater, EConditionOperator.Less,
                    EConditionOperator.GreaterOrEqual, EConditionOperator.LessOrEqual
                };
                int idx = Array.IndexOf(opMap, mod.Operator);
                if (idx == -1) idx = 0;
                int newIdx = EditorGUILayout.Popup(idx, opNames, GUILayout.Width(opW + 30));
                mod.Operator = opMap[newIdx];

                if (mod.Operator == EConditionOperator.Contains || mod.Operator == EConditionOperator.NotContains)
                {
                    mod.ValueString = EditorGUILayout.TextField(mod.ValueString ?? "", GUILayout.Width(valW));
                }
                else
                {
                    mod.Value = EditorGUILayout.IntField(mod.Value, GUILayout.Width(valW));
                }
            }
        }

        private void DrawLine(Color color, int thickness = 1, int padding = 10)
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
            r.height = thickness;
            r.y += padding / 2;
            r.x -= 2;
            r.width += 6;
            EditorGUI.DrawRect(r, color);
        }

        private void DrawAnimEventConfig(NovellaAnimEvent ev, int idx, NovellaTree tree, AnimationNodeData animData, bool isSynced, DialogueNodeData syncedDialogue)
        {
            Color baseCol = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = baseCol;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"#{idx + 1}", EditorStyles.boldLabel, GUILayout.Width(30));
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                Undo.RecordObject(tree, "Remove Anim Event");
                animData.AnimEvents.Remove(ev);
                EditorUtility.SetDirty(tree);
                GUIUtility.ExitGUI();
            }
            GUI.backgroundColor = baseCol;
            GUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();

            if (isSynced && syncedDialogue != null && syncedDialogue.DialogueLines.Count > 0)
            {
                string[] lineOptions = new string[syncedDialogue.DialogueLines.Count];
                for (int l = 0; l < syncedDialogue.DialogueLines.Count; l++)
                {
                    string spk = syncedDialogue.DialogueLines[l].Speaker != null ? syncedDialogue.DialogueLines[l].Speaker.name : ToolLang.Get("Narrator", "Автор");

                    string textPreview = syncedDialogue.DialogueLines[l].LocalizedPhrase.GetText(_window.PreviewLanguage);
                    if (textPreview.Length > 15) textPreview = textPreview.Substring(0, 15) + "...";

                    lineOptions[l] = $"#{l + 1} ({spk}): {textPreview}";
                }
                ev.LineIndex = EditorGUILayout.Popup(ToolLang.Get("Line", "Репл."), ev.LineIndex, lineOptions, GUILayout.ExpandWidth(true));
            }

            string[] triggerNames = { ToolLang.Get("Start", "Старт"), ToolLang.Get("End", "Конец"), ToolLang.Get("Delay", "Время") };
            ev.TriggerType = (EAudioTriggerType)EditorGUILayout.Popup((int)ev.TriggerType, triggerNames, GUILayout.Width(80));

            if (ev.TriggerType == EAudioTriggerType.TimeDelay)
            {
                ev.TimeDelay = EditorGUILayout.FloatField(ev.TimeDelay, GUILayout.Width(50));
                ev.TimeDelay = Mathf.Max(0, ev.TimeDelay);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            DrawLine(new Color(1, 1, 1, 0.1f));
            GUILayout.Space(5);

            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 90;

            string[] targetNames = {
                ToolLang.Get("Camera", "Камера"),
                ToolLang.Get("Background", "Фон"),
                ToolLang.Get("Dialogue Frame", "Рамка Диалога"),
                ToolLang.Get("Character", "Персонаж")
            };
            ev.Target = (EAnimTarget)EditorGUILayout.Popup(ToolLang.Get("Target", "Объект"), (int)ev.Target, targetNames);

            if (ev.Target == EAnimTarget.Character)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Which Char?", "Какой перс?"), GUILayout.Width(90));

                if (isSynced && syncedDialogue != null && syncedDialogue.ActiveCharacters.Count > 0)
                {
                    var validChars = syncedDialogue.ActiveCharacters.Where(c => c.CharacterAsset != null).Select(c => c.CharacterAsset).ToList();
                    if (validChars.Count > 0)
                    {
                        string[] charNames = validChars.Select(c => c.name).ToArray();
                        int currentIndex = validChars.IndexOf(ev.TargetCharacter);
                        if (currentIndex == -1) currentIndex = 0;

                        int newIndex = EditorGUILayout.Popup(currentIndex, charNames);
                        ev.TargetCharacter = validChars[newIndex];
                    }
                    else
                    {
                        GUILayout.Label(ToolLang.Get("No chars in Layout", "Массовка пуста"), EditorStyles.centeredGreyMiniLabel);
                        ev.TargetCharacter = null;
                    }
                }
                else
                {
                    string charName = ev.TargetCharacter != null ? ev.TargetCharacter.name : ToolLang.Get("Select...", "Выбрать...");
                    if (GUILayout.Button($"👤 {charName}", EditorStyles.popup))
                    {
                        NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => {
                            Undo.RecordObject(tree, "Change Anim Target");
                            ev.TargetCharacter = selectedChar;
                            EditorUtility.SetDirty(tree);
                        });
                    }
                    if (ev.TargetCharacter != null)
                    {
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { ev.TargetCharacter = null; }
                        GUI.backgroundColor = baseCol;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);

            string[] animNames = {
                ToolLang.Get("Shake", "Тряска"),
                ToolLang.Get("Punch", "Пульсация"),
                ToolLang.Get("Fade In", "Появление"),
                ToolLang.Get("Fade Out", "Исчезновение"),
                ToolLang.Get("Move To", "Движение в"),
                ToolLang.Get("Scale", "Масштаб")
            };
            ev.AnimType = (EAnimType)EditorGUILayout.Popup(ToolLang.Get("Animation", "Анимация"), (int)ev.AnimType, animNames);

            GUILayout.Space(5);

            float durLw = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = ToolLang.IsRU ? 120 : 100;
            ev.Duration = EditorGUILayout.FloatField(ToolLang.Get("Duration (sec)", "Длительность (с)"), ev.Duration);
            ev.Duration = Mathf.Max(0.01f, ev.Duration);
            EditorGUIUtility.labelWidth = durLw;

            if (ev.AnimType == EAnimType.Shake || ev.AnimType == EAnimType.Punch)
            {
                ev.Strength = EditorGUILayout.FloatField(ToolLang.Get("Strength", "Сила"), ev.Strength);
            }
            else if (ev.AnimType == EAnimType.MoveTo)
            {
                if (ev.Target == EAnimTarget.Character)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Characters use custom scene positions (posX, posY) from Dialogue Node. This tween overrides it.", "Персонажи используют позы из ноды Диалога. Этот твин временно перекроет их."));
                }
                ev.EndVector = EditorGUILayout.Vector2Field(ToolLang.Get("End Position", "Конечная Позиция"), ev.EndVector);
            }
            else if (ev.AnimType == EAnimType.Scale)
            {
                ev.EndVector = EditorGUILayout.Vector2Field(ToolLang.Get("End Scale", "Конечный Масштаб"), ev.EndVector);
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(tree);
            }

            EditorGUIUtility.labelWidth = lw;
            GUILayout.EndVertical();
            GUILayout.Space(5);
        }
        private string[] GetAvailableLayersAcrossProject()
        {
            List<string> layers = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (var guid in guids)
            {
                var ch = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(AssetDatabase.GUIDToAssetPath(guid));
                if (ch != null)
                {
                    foreach (var layer in ch.BaseLayers)
                    {
                        if (!layers.Contains(layer.LayerName)) layers.Add(layer.LayerName);
                    }
                }
            }
            if (layers.Count == 0) layers.Add("Clothes");
            return layers.ToArray();
        }
        public void DrawGroupInspector(NovellaGroupView groupView)
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            GUILayout.BeginHorizontal(); GUILayout.Space(15); GUILayout.BeginVertical(); GUILayout.Space(15);

            DrawSectionHeader("📦", ToolLang.Get("GROUP SETTINGS", "НАСТРОЙКИ ГРУППЫ"));
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Group Title", "Название группы"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{groupView.Data.Title.Length}/50", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();

            string newTitle = EditorGUILayout.TextField(groupView.Data.Title);
            if (newTitle.Length > 50) newTitle = newTitle.Substring(0, 50);
            groupView.Data.Title = newTitle;

            GUILayout.Space(10);
            groupView.Data.TitleColor = EditorGUILayout.ColorField(ToolLang.Get("Title Color", "Цвет названия"), groupView.Data.TitleColor);
            groupView.Data.TitleFontSize = EditorGUILayout.IntSlider(ToolLang.Get("Title Font Size", "Размер названия"), groupView.Data.TitleFontSize, 10, 60);

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Border Color", "Цвет рамки"), EditorStyles.boldLabel);
            groupView.Data.BorderColor = EditorGUILayout.ColorField(groupView.Data.BorderColor);

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Background Color", "Цвет фона"), EditorStyles.boldLabel);
            groupView.Data.BackgroundColor = EditorGUILayout.ColorField(groupView.Data.BackgroundColor);

            if (EditorGUI.EndChangeCheck())
            {
                groupView.title = groupView.Data.Title;
                groupView.style.borderTopColor = groupView.Data.BorderColor;
                groupView.style.borderBottomColor = groupView.Data.BorderColor;
                groupView.style.borderLeftColor = groupView.Data.BorderColor;
                groupView.style.borderRightColor = groupView.Data.BorderColor;
                groupView.RefreshVisuals();
                _onMarkUnsaved?.Invoke();
            }

            GUILayout.EndVertical(); GUILayout.Space(15); GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        public void DrawInspector(NovellaNodeView selectedNodeView, bool isStartNodeSelected)
        {
            if (_serializedObject == null || _currentTree == null || _window == null) return;

            _serializedObject.Update();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            GUILayout.BeginHorizontal(); GUILayout.Space(15); GUILayout.BeginVertical(); GUILayout.Space(15);

            if (isStartNodeSelected)
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScenePreview(); }
                DrawStartNodeHelp(); EndLayout(); return;
            }

            DrawSectionHeader("📝", ToolLang.Get("Node Inspector", "Инспектор ноды"));

            if (selectedNodeView == null || selectedNodeView.Data == null || !_currentTree.Nodes.Contains(selectedNodeView.Data))
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScenePreview(); }
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Select a node in the graph to edit its properties here.",
                    "Выбери ноду в графе чтобы редактировать её свойства тут."));
                EndLayout();
                return;
            }

            NovellaNodeBase nodeData = selectedNodeView.Data;

            if (_lastSyncedNode != nodeData)
            {
                _lastSyncedNode = nodeData;
                _activePreviewLineIndex = 0;
                SyncScenePreview(nodeData);

                int idx = _currentTree.Nodes.FindIndex(n => n != null && n.NodeID == nodeData.NodeID);
                if (idx != -1) _cachedNodeProp = _serializedObject.FindProperty("Nodes").GetArrayElementAtIndex(idx);
                else _cachedNodeProp = null;
            }

            if (nodeData is DialogueNodeData initDnd && initDnd.DialogueLines.Count == 0)
            {
                initDnd.DialogueLines.Add(new DialogueLine());
            }

            if (nodeData is VariableNodeData initVnd && initVnd.Variables.Count == 0)
            {
                initVnd.Variables.Add(new VariableUpdate());
            }

            float originalLabelWidth = EditorGUIUtility.labelWidth;

            DrawSectionHeader("🏷", ToolLang.Get("General", "Общие"));

            // ─── Node Name (главное поле) — DarkTextField в Hub-стиле ───
            NovellaInspectorChrome.DrawFieldLabel(ToolLang.Get("Name", "Имя"));
            EditorGUI.BeginChangeCheck();
            string newNodeTitle = NovellaInspectorChrome.DrawDarkTextField(nodeData.NodeTitle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_currentTree, "Change Node Name");
                nodeData.NodeTitle = newNodeTitle;
                selectedNodeView.RefreshVisuals();
                _onMarkUnsaved?.Invoke();
            }

            GUILayout.Space(8);

            // ─── Pin / Color / ID — компактный row ───
            GUILayout.BeginHorizontal();

            // Pin button — наша toggle-style кнопка.
            bool wasPinned = nodeData.IsPinned;
            string pinLabel = wasPinned
                ? "📌  " + ToolLang.Get("Pinned", "Закреплена")
                : "📌  " + ToolLang.Get("Pin node", "Закрепить");
            // Используем accent-fill для активной (прижата), slim для неактивной.
            bool pinClicked = wasPinned
                ? NovellaInspectorChrome.DrawAccentBtn(pinLabel,
                    GUILayout.Height(28), GUILayout.MinWidth(140))
                : NovellaInspectorChrome.DrawSlimBtn(pinLabel,
                    GUILayout.Height(28), GUILayout.MinWidth(140));
            if (pinClicked)
            {
                Undo.RecordObject(_currentTree, "Pin");
                bool wantPin = !wasPinned;
                foreach (var n in _currentTree.Nodes)
                    if (n != null) n.IsPinned = false;
                nodeData.IsPinned = wantPin;
                EditorApplication.delayCall += () => _graphView?.Query<NovellaNodeView>()
                    .ForEach(nv => nv.RefreshVisuals());
                _onMarkUnsaved?.Invoke();
            }

            GUILayout.Space(6);

            // Color picker — только для Dialogue/Event/Note (остальные используют
            // глобальные настройки). Слева label «Цвет», справа — color field.
            if (nodeData.NodeType == ENodeType.Dialogue || nodeData.NodeType == ENodeType.Note)
            {
                var colLblSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = NovellaGraphTheme.Text3 }
                };
                GUILayout.Label(ToolLang.Get("Color", "Цвет"), colLblSt,
                    GUILayout.Width(40), GUILayout.Height(28));
                EditorGUI.BeginChangeCheck();
                Color newColor = EditorGUILayout.ColorField(GUIContent.none, nodeData.NodeCustomColor,
                    false, true, false, GUILayout.Width(60), GUILayout.Height(22));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_currentTree, "Color");
                    nodeData.NodeCustomColor = newColor;
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }
            }
            else
            {
                var lockSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = NovellaGraphTheme.Text4 }
                };
                GUILayout.Label("🎨 " + ToolLang.Get("Type-locked color", "Цвет от типа"), lockSt,
                    GUILayout.Height(28));
            }
            GUILayout.EndHorizontal();

            // ─── Internal ID (мелкий read-only collapsed под GENERAL) ───
            // Раньше был полноразмерный disabled TextField — занимал много места.
            // Теперь — мелкая строка «ID: dialogue_a3f2b» + копировать-кнопка.
            // Кнопка стоит ВПЛОТНУЮ к значению (не на правом краю), иконка 📋
            // (узнаваемее чем абстрактный ⎘) + toast «ID скопирован».
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            var idLblSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9,
                normal = { textColor = NovellaGraphTheme.Text4 }
            };
            GUILayout.Label(ToolLang.Get("Internal ID:", "Внутренний ID:"), idLblSt,
                GUILayout.Width(ToolLang.IsRU ? 90 : 80), GUILayout.Height(20));

            var idValueSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9, fontStyle = FontStyle.Italic,
                normal = { textColor = NovellaGraphTheme.Text3 }
            };
            // Width = ровно по ширине текста — кнопка прижмётся вплотную.
            string idVal = nodeData.NodeID ?? "";
            float idValW = idValueSt.CalcSize(new GUIContent(idVal)).x + 4;
            GUILayout.Label(idVal, idValueSt, GUILayout.Width(idValW), GUILayout.Height(20));

            // Маленький отступ + кнопка-копия. Иконка 📋 — клипборд, понятнее.
            GUILayout.Space(4);
            if (NovellaInspectorChrome.DrawIconBtn("📋",
                ToolLang.Get("Copy ID to clipboard", "Скопировать ID в буфер обмена"),
                danger: false, size: 20))
            {
                EditorGUIUtility.systemCopyBuffer = idVal;
                // Уведомление: фирменный тост + Unity-нотификация (на случай
                // если где-то DrawOverlay'а нет — нотификация это built-in
                // оверлей самого EditorWindow).
                NovellaToast.Success(string.Format(
                    ToolLang.Get("ID copied: {0}", "ID скопирован: {0}"), idVal));
                if (_window != null)
                    _window.ShowNotification(new GUIContent(
                        ToolLang.Get("✔  ID copied", "✔  ID скопирован")), 1.2);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ==========================================
            // === НОДА ОЖИДАНИЯ (WAIT) ===
            // ==========================================
            if (nodeData is WaitNodeData waitData)
            {
                DrawSectionHeader("⏳", ToolLang.Get("Wait Settings", "Настройки Ожидания"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Pauses the story. 'Time' auto-continues after N seconds; 'User Click' waits for a click/tap. Use it for dramatic beats or 'press to continue' moments.",
                    "Ставит историю на паузу. «Время» — авто-продолжение через N секунд; «По клику игрока» — ждёт клик/тап. Удобно для драматических пауз или «нажми чтобы продолжить»."));
                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();

                string[] waitModeNames = { ToolLang.Get("Time (Delay)", "Время (Задержка)"), ToolLang.Get("User Click", "По клику игрока") };
                waitData.WaitMode = (EWaitMode)EditorGUILayout.Popup(ToolLang.Get("Wait Mode", "Режим"), (int)waitData.WaitMode, waitModeNames);

                if (waitData.WaitMode == EWaitMode.Time)
                {
                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Time (sec):", "Время (сек):"), GUILayout.Width(ToolLang.IsRU ? 80 : 100));
                    waitData.WaitTime = EditorGUILayout.FloatField(waitData.WaitTime, GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                    if (waitData.WaitTime < 0.1f) waitData.WaitTime = 0.1f;

                    GUILayout.Space(5);
                    waitData.WaitIsSkippable = EditorGUILayout.ToggleLeft(ToolLang.Get(" Is Skippable on Click", " Пропускаемое по клику"), waitData.WaitIsSkippable);
                }
                else
                {
                    GUILayout.Space(10);
                    GUILayout.Label("✨ " + ToolLang.Get("Click Indicator Settings", "Настройки Индикатора Клика"), EditorStyles.boldLabel);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(60));
                    string sprName = waitData.WaitIndicatorSprite != null ? waitData.WaitIndicatorSprite.name : ToolLang.Get("Default Rhombus", "Базовый Ромб");
                    if (GUILayout.Button("🖼 " + sprName, EditorStyles.popup))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Undo.RecordObject(_currentTree, "Change Indicator");
                            waitData.WaitIndicatorSprite = obj as Sprite;
                            if (waitData.WaitIndicatorSprite == null && obj is Texture2D tex)
                            {
                                string path = AssetDatabase.GetAssetPath(tex);
                                waitData.WaitIndicatorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                            }
                            _onMarkUnsaved?.Invoke();
                            SyncScenePreview(waitData);
                            _window.Repaint();
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }
                    if (waitData.WaitIndicatorSprite != null)
                    {
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.RecordObject(_currentTree, "Clear Sprite");
                            waitData.WaitIndicatorSprite = null;
                            _onMarkUnsaved?.Invoke();
                            SyncScenePreview(waitData);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    GUILayout.EndHorizontal();

                    float oldLw = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = ToolLang.IsRU ? 145 : 100;
                    waitData.WaitIndicatorColor = EditorGUILayout.ColorField(ToolLang.Get("Color", "Цвет"), waitData.WaitIndicatorColor);
                    waitData.WaitIndicatorSize = EditorGUILayout.Slider(ToolLang.Get("Size", "Размер"), waitData.WaitIndicatorSize, 10f, 100f);
                    waitData.WaitIndicatorAnimSpeed = EditorGUILayout.Slider(ToolLang.Get("Anim Speed", "Скорость анимации"), waitData.WaitIndicatorAnimSpeed, 0f, 10f);
                    waitData.WaitIndicatorAmplitude = EditorGUILayout.Slider(ToolLang.Get("Amplitude", "Амплитуда левитации"), waitData.WaitIndicatorAmplitude, 0f, 50f);
                    EditorGUIUtility.labelWidth = oldLw;
                }

                GUILayout.Space(10);
                waitData.WaitClearText = EditorGUILayout.ToggleLeft(ToolLang.Get(" Clear Dialogue Text", " Очистить текст диалога"), waitData.WaitClearText);
                GUILayout.Space(5);
                waitData.WaitHideFrame = EditorGUILayout.ToggleLeft(ToolLang.Get(" Hide Dialogue Frame", " Скрыть рамку диалога"), waitData.WaitHideFrame);

                GUILayout.Space(10);
                GUILayout.Label("📝 " + ToolLang.Get("Indicator Text (Optional)", "Текст под индикатором (Необязательно)"), EditorStyles.boldLabel);
                waitData.WaitText = EditorGUILayout.TextField(waitData.WaitText);

                // UI binding для текста — если задан, текст пишется не в встроенный
                // индикатор а в произвольный TMP-элемент сцены.
                NovellaEngine.Editor.UIBindings.UIBindingFieldGUI.Draw(
                    ToolLang.Get("📝 Text target (optional)", "📝 Поле для текста (опц.)"),
                    waitData.UITextTargetId,
                    NovellaEngine.Runtime.UI.UIBindingKind.Text,
                    newId => { waitData.UITextTargetId = newId; _onMarkUnsaved?.Invoke(); });

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(60));
                waitData.WaitTextColor = EditorGUILayout.ColorField(waitData.WaitTextColor, GUILayout.Width(60));
                GUILayout.Space(10);
                GUILayout.Label(ToolLang.Get("Size", "Размер"), GUILayout.Width(50));
                waitData.WaitTextSize = EditorGUILayout.IntField(waitData.WaitTextSize, GUILayout.Width(40));
                GUILayout.EndHorizontal();

                float lw2 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = ToolLang.IsRU ? 110 : 80;
                waitData.WaitTextBlinkSpeed = EditorGUILayout.Slider(ToolLang.Get("Blink Spd", "Скор. Мигания"), waitData.WaitTextBlinkSpeed, 0f, 10f);
                EditorGUIUtility.labelWidth = lw2;

                GUILayout.Space(10);
                GUILayout.Label("📍 " + ToolLang.Get("Position on Screen (Live Sync)", "Позиция на экране (Live Sync)"), EditorStyles.boldLabel);

                waitData.WaitIndicatorPreset = (EFramePosition)EditorGUILayout.EnumPopup(ToolLang.Get("Preset", "Пресет"), waitData.WaitIndicatorPreset);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Offset X:", GUILayout.Width(55));
                waitData.WaitIndicatorPosX = EditorGUILayout.FloatField(waitData.WaitIndicatorPosX, GUILayout.Width(50));
                GUILayout.Space(10);
                GUILayout.Label("Offset Y:", GUILayout.Width(55));
                waitData.WaitIndicatorPosY = EditorGUILayout.FloatField(waitData.WaitIndicatorPosY, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Text X:", GUILayout.Width(55));
                waitData.WaitTextPosX = EditorGUILayout.FloatField(waitData.WaitTextPosX, GUILayout.Width(50));
                GUILayout.Space(10);
                GUILayout.Label("Text Y:", GUILayout.Width(55));
                waitData.WaitTextPosY = EditorGUILayout.FloatField(waitData.WaitTextPosY, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_currentTree, "Change Wait Settings");
                    selectedNodeView.RefreshVisuals();
                    SyncScenePreview(waitData);
                    _onMarkUnsaved?.Invoke();
                }

                GUILayout.EndVertical();
                EndLayout();
                return;
            }
            // ==========================================
            // === НОДА СОХРАНЕНИЯ (SAVE) ===
            // ==========================================
            if (nodeData is SaveNodeData saveData)
            {
                DrawSectionHeader("💾", ToolLang.Get("Save Checkpoint", "Сохранение (Чекпоинт)"));
                GUILayout.BeginVertical(EditorStyles.helpBox);

                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Acts as a checkpoint: when the player reaches this node the game saves immediately (ignoring the auto-save timer). Great to place before major branching choices.",
                    "Работает как чекпоинт: когда игрок достигает этой ноды, игра сразу сохраняется (игнорируя таймер автосохранения). Удобно ставить перед важными выборами."));

                GUILayout.Space(10);
                GUILayout.Label("✅ " + ToolLang.Get("No configuration needed.", "Настройка не требуется."), EditorStyles.centeredGreyMiniLabel);
                GUILayout.Space(10);

                GUILayout.EndVertical();
                EndLayout(); return;
            }
            // ==========================================
            // === НОДА ЗАМЕТКИ (NOTE) ===
            // ==========================================
            if (nodeData is NoteNodeData noteData)
            {
                string currentNoteTextEN = noteData.LocalizedNoteText.GetText("EN");
                string currentNoteTextRU = noteData.LocalizedNoteText.GetText("RU");

                if ((currentNoteTextEN != null && currentNoteTextEN.Contains("[TUTORIAL_END]")) ||
                    (currentNoteTextRU != null && currentNoteTextRU.Contains("[TUTORIAL_END]")))
                {
                    DrawSectionHeader("🎓", ToolLang.Get("Tutorial Completed!", "Обучение Завершено!"));
                    GUILayout.BeginVertical(EditorStyles.helpBox);

                    GUIStyle tutStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
                    GUILayout.Label(ToolLang.Get(
                        "You've learned the basics of <b>Novella Engine</b>!\nNow it's time to create your own Visual Novel.",
                        "Вы изучили основы <b>Novella Engine</b>!\nТеперь пришло время создать свою собственную визуальную новеллу."
                    ), tutStyle);

                    GUILayout.Space(20);

                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                    if (GUILayout.Button(ToolLang.Get("🚀 GO TO CREATE MY VISUAL NOVEL", "🚀 ПЕРЕЙТИ К СОЗДАНИЮ СВОЕЙ НОВЕЛЛЫ"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(50)))
                    {
                        EditorPrefs.SetBool("Novella_TutorialMode", true);

                        Type type = Type.GetType("NovellaEngine.Editor.NovellaSceneManagerWindow");
                        if (type != null)
                        {
                            var method = type.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (method != null) method.Invoke(null, null);
                        }
                    }
                    GUI.backgroundColor = Color.white;

                    GUILayout.Space(10);
                    GUILayout.EndVertical();

                    EndLayout(); return;
                }

                DrawSectionHeader("📌", ToolLang.Get("Note Settings", "Настройки Заметки"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "A sticky note for YOU — it organizes the graph and never appears in the game. Use it for comments, to-dos or section labels.",
                    "Заметка для ТЕБЯ — помогает организовать граф и НЕ появляется в игре. Используй для комментариев, to-do или подписей разделов."));

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginVertical(EditorStyles.helpBox);
                noteData.NoteWidth = EditorGUILayout.Slider(ToolLang.Get("Node Max Width", "Ширина ноды"), noteData.NoteWidth, 200f, 1000f);
                GUILayout.Space(5);
                noteData.NoteTitleColor = EditorGUILayout.ColorField(ToolLang.Get("Title Color", "Цвет названия"), noteData.NoteTitleColor);
                noteData.NoteTitleFontSize = EditorGUILayout.IntSlider(ToolLang.Get("Title Font Size", "Размер названия"), noteData.NoteTitleFontSize, 10, 60);
                GUILayout.EndVertical();

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                noteData.ShowBackground = EditorGUILayout.Toggle(ToolLang.Get("Show Background", "Показывать фон"), noteData.ShowBackground);
                if (noteData.ShowBackground)
                {
                    noteData.NodeCustomColor = EditorGUILayout.ColorField(ToolLang.Get("Background Color", "Цвет фона"), noteData.NodeCustomColor);
                }
                GUILayout.EndVertical();

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(ToolLang.Get("Text Content:", "Текст заметки:") + $" [{_window.PreviewLanguage}]", EditorStyles.boldLabel);

                string currentNoteText = noteData.LocalizedNoteText.GetText(_window.PreviewLanguage);
                string newNoteText = EditorGUILayout.TextArea(currentNoteText, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(120));

                GUILayout.Space(5);
                noteData.NoteTextColor = EditorGUILayout.ColorField(ToolLang.Get("Text Color", "Цвет текста"), noteData.NoteTextColor);
                noteData.FontSize = EditorGUILayout.IntSlider(ToolLang.Get("Text Font Size", "Размер текста"), noteData.FontSize > 0 ? noteData.FontSize : 14, 10, 60);
                GUILayout.EndVertical();

                GUILayout.Space(10);
                DrawSectionHeader("🖼", ToolLang.Get("Attached Images", "Изображения на Заметке"));

                for (int i = 0; i < noteData.NoteImages.Count; i++)
                {
                    var img = noteData.NoteImages[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get($"Image {i + 1}", $"Картинка {i + 1}"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { noteData.NoteImages.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Texture", "Текстура"), GUILayout.Width(100));
                    string imgName = img.Image != null ? img.Image.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");
                    if (GUILayout.Button("🖼 " + imgName, EditorStyles.popup))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Undo.RecordObject(_currentTree, "Change Note Image");
                            img.Image = obj as Texture2D;
                            if (img.Image == null && obj is Sprite sp) img.Image = sp.texture;
                            selectedNodeView.RefreshVisuals();
                            _onMarkUnsaved?.Invoke();
                            _window.Repaint();
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }
                    GUILayout.EndHorizontal();

                    img.Shape = (ENoteImageShape)EditorGUILayout.EnumPopup(ToolLang.Get("Shape", "Форма"), img.Shape);
                    img.Alignment = (ENoteImageAlignment)EditorGUILayout.EnumPopup(ToolLang.Get("Position Preset", "Пресет позиции"), img.Alignment);

                    GUILayout.BeginHorizontal();
                    img.Offset = EditorGUILayout.Vector2Field(ToolLang.Get("Custom Offset (X,Y)", "Смещение (X,Y)"), img.Offset);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    img.Size = EditorGUILayout.Vector2Field(ToolLang.Get("Size (W,H)", "Размер (Ш,В)"), img.Size);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    img.Alpha = EditorGUILayout.Slider(ToolLang.Get("Alpha", "Прозрачность"), img.Alpha, 0f, 1f);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                if (noteData.NoteImages.Count < 3)
                {
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Image", "Добавить картинку"), EditorStyles.miniButton))
                    {
                        noteData.NoteImages.Add(new NoteImageData());
                    }
                }

                GUILayout.Space(10);
                DrawSectionHeader("🔗", ToolLang.Get("Attached Links", "Прикрепленные ссылки"));

                for (int i = 0; i < noteData.NoteLinks.Count; i++)
                {
                    var lnk = noteData.NoteLinks[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    lnk.DisplayName = EditorGUILayout.TextField(lnk.DisplayName, GUILayout.Width(100));
                    lnk.URL = EditorGUILayout.TextField(lnk.URL, GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { noteData.NoteLinks.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                if (noteData.NoteLinks.Count < 3)
                {
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Link", "Добавить ссылку"), EditorStyles.miniButton))
                    {
                        noteData.NoteLinks.Add(new NoteLinkData());
                    }
                }

                if (EditorGUI.EndChangeCheck() || GUI.changed)
                {
                    Undo.RecordObject(_currentTree, "Edit Note");
                    noteData.LocalizedNoteText.SetText(_window.PreviewLanguage, newNoteText);
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                    _window.Repaint();
                }

                GUILayout.Space(15);
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Tip: Use the 'Background' preset to place the image under the text. Use offsets to fine-tune spacing.",
                    "Подсказка: Пресет 'Background' кладёт картинку под текст как водяной знак. Смещения — для точной подгонки."));

                EndLayout(); return;
            }

            // ==========================================
            // === НОДА КОНЦА СЦЕНЫ (END) ===
            // ==========================================
            if (nodeData is EndNodeData endData)
            {
                DrawSectionHeader("🛑", ToolLang.Get("End Scene Settings", "Настройки конца сцены"));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                string[] actionNames = {
                    ToolLang.Get("Return to Main Menu", "Вернуться в гл. меню"),
                    ToolLang.Get("Load Next Chapter", "Загрузить след. главу"),
                    ToolLang.Get("Load Specific Scene", "Загрузить конкретную сцену"),
                    ToolLang.Get("Quit Game", "Выйти из игры")
                };

                endData.EndAction = (EEndAction)EditorGUILayout.Popup(ToolLang.Get("Action:", "Действие:"), (int)endData.EndAction, actionNames);

                if (endData.EndAction == EEndAction.LoadNextChapter)
                {
                    endData.NextChapter = (NovellaTree)EditorGUILayout.ObjectField(ToolLang.Get("Next Chapter:", "След. глава:"), endData.NextChapter, typeof(NovellaTree), false);
                    if (endData.NextChapter == null)
                        NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                            "⚠ No next chapter assigned — the game will stop here at runtime. Assign a chapter, or switch to 'Return to Main Menu'.",
                            "⚠ Следующая глава не назначена — игра остановится здесь. Назначь главу или переключи на «Вернуться в главное меню»."), true);
                }
                else if (endData.EndAction == EEndAction.LoadSpecificScene)
                {
                    var buildScenes = UnityEditor.EditorBuildSettings.scenes;
                    List<string> sceneNames = new List<string>();

                    foreach (var scene in buildScenes)
                    {
                        if (scene.enabled)
                        {
                            string name = System.IO.Path.GetFileNameWithoutExtension(scene.path);
                            sceneNames.Add(name);
                        }
                    }

                    if (sceneNames.Count == 0)
                    {
                        NovellaInspectorChrome.DrawWarn(ToolLang.Get("Add scenes to File -> Build Settings first!", "Сначала добавьте сцены в File -> Build Settings!"), true);

                        if (GUILayout.Button("🛠 " + ToolLang.Get("Open Scene Manager", "Открыть Менеджер Сцен"), EditorStyles.miniButton, GUILayout.Height(25)))
                        {
                            Type type = Type.GetType("NovellaEngine.Editor.NovellaSceneManagerWindow");
                            if (type != null)
                            {
                                var method = type.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                if (method != null) method.Invoke(null, null);
                            }
                        }
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();

                        // Если ранее выбранная сцена больше не в Build Settings —
                        // НЕ перезаписываем её на первую молча. Показываем warning
                        // с placeholder-вариантом «(missing: имя)» в начале списка,
                        // чтобы юзер увидел проблему и решил сам.
                        bool orphan = !string.IsNullOrEmpty(endData.TargetSceneName)
                                       && !sceneNames.Contains(endData.TargetSceneName);
                        List<string> dropdownNames = sceneNames;
                        int selectedIndex = sceneNames.IndexOf(endData.TargetSceneName);
                        if (orphan)
                        {
                            dropdownNames = new List<string>(sceneNames);
                            dropdownNames.Insert(0, ToolLang.Get($"⚠ (missing: {endData.TargetSceneName})",
                                                                $"⚠ (отсутствует: {endData.TargetSceneName})"));
                            selectedIndex = 0;
                        }
                        else if (selectedIndex < 0) selectedIndex = 0;

                        int newIndex = EditorGUILayout.Popup(ToolLang.Get("Target Scene:", "Целевая сцена:"),
                                                             selectedIndex, dropdownNames.ToArray());
                        // Записываем выбор только если юзер РЕАЛЬНО выбрал валидную
                        // строку (т.е. не остался на orphan-плашке).
                        if (orphan)
                        {
                            if (newIndex > 0) endData.TargetSceneName = sceneNames[newIndex - 1];
                        }
                        else
                        {
                            endData.TargetSceneName = sceneNames[newIndex];
                        }

                        if (GUILayout.Button(new GUIContent("🛠", ToolLang.Get("Open Scene Manager", "Открыть Менеджер Сцен")), EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            Type type = Type.GetType("NovellaEngine.Editor.NovellaSceneManagerWindow");
                            if (type != null)
                            {
                                var method = type.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                if (method != null) method.Invoke(null, null);
                            }
                        }

                        GUILayout.EndHorizontal();

                        if (orphan)
                        {
                            NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                                "The previously selected scene is no longer in Build Settings. The end node will fail at runtime — pick a valid scene above.",
                                "Ранее выбранная сцена больше не в Build Settings. Нода End сломается в рантайме — выбери валидную сцену выше."), true);
                        }
                    }
                }

                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change End Action"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА АУДИО (AUDIO) ===
            // ==========================================
            if (nodeData is AudioNodeData audData)
            {
                DrawSectionHeader("🎵", ToolLang.Get("Audio Settings", "Настройки звука"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Plays or stops music / sound. Pick a clip, the channel (Music or SFX) and the action. Turn on 'Sync with dialogue' to fire sounds on specific lines.",
                    "Проигрывает или останавливает музыку / звук. Выбери клип, канал (Музыка или SFX) и действие. Включи «Синхр. с диалогом» чтобы запускать звуки на конкретных репликах."));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.AudioSyncNodeID == audData.NodeID);
                audData.SyncWithDialogue = (syncedDialogue != null);

                if (!audData.SyncWithDialogue)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Standard sequential mode. Connect to Dialogue 'Audio Sync' port for advanced features.", "Обычный режим. Подключите к порту 'Audio Sync' Диалога для продвинутых фишек."));

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    audData.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), audData.AudioChannel);

                    string channelInfo = "";
                    if (audData.AudioChannel == EAudioChannel.BGM) channelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                    else if (audData.AudioChannel == EAudioChannel.SFX) channelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                    else if (audData.AudioChannel == EAudioChannel.Voice) channelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                    NovellaInspectorChrome.DrawHint(channelInfo);
                    GUILayout.EndVertical();

                    GUILayout.Space(5);

                    audData.AudioAction = (EAudioAction)EditorGUILayout.EnumPopup(ToolLang.Get("Action", "Действие"), audData.AudioAction);

                    if (audData.AudioAction == EAudioAction.Play)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get("Audio Clip", "Аудиофайл"), GUILayout.Width(126));
                        string audioName = audData.AudioAsset != null ? audData.AudioAsset.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");

                        if (GUILayout.Button("🎵 " + audioName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                        {
                            NovellaGalleryWindow.ShowWindow(obj => {
                                Undo.RecordObject(_currentTree, "Change Audio");
                                audData.AudioAsset = obj as AudioClip;
                                selectedNodeView.RefreshVisuals();
                                _onMarkUnsaved?.Invoke();
                            }, NovellaGalleryWindow.EGalleryFilter.Audio);
                        }

                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear Audio"); audData.AudioAsset = null; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        if (audData.AudioAsset == null)
                            NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                                "⚠ No audio clip selected — nothing will play at this node.",
                                "⚠ Аудиоклип не выбран — на этой ноде ничего не проиграется."), true);

                        audData.AudioVolume = EditorGUILayout.Slider(ToolLang.Get("Volume", "Громкость"), audData.AudioVolume, 0f, 1f);
                    }
                }
                else
                {
                    int linesCount = syncedDialogue.DialogueLines.Count;
                    int requiredEvents = linesCount + 1;

                    while (audData.AudioEvents.Count < requiredEvents)
                    {
                        audData.AudioEvents.Add(new DialogueAudioEvent());
                    }
                    while (audData.AudioEvents.Count > requiredEvents)
                    {
                        audData.AudioEvents.RemoveAt(audData.AudioEvents.Count - 1);
                    }

                    for (int i = 0; i <= linesCount; i++)
                    {
                        var ev = audData.AudioEvents[i];
                        bool isAfterDialogue = (i == linesCount);

                        ev.LineIndex = isAfterDialogue ? -1 : i;

                        if (isAfterDialogue) ev.TriggerType = EAudioTriggerType.OnDialogueEnd;
                        else if (ev.TriggerType == EAudioTriggerType.OnDialogueEnd) ev.TriggerType = EAudioTriggerType.OnStart;

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();

                        if (isAfterDialogue)
                        {
                            GUILayout.Label("🏁 " + ToolLang.Get("After Dialogue", "После диалога"), EditorStyles.boldLabel);
                        }
                        else
                        {
                            string spkName = syncedDialogue.DialogueLines[i].Speaker != null ? syncedDialogue.DialogueLines[i].Speaker.name : ToolLang.Get("Narrator", "Автор");
                            GUILayout.Label($"💬 {ToolLang.Get("Line", "Реплика")} #{i + 1} ({spkName})", EditorStyles.boldLabel);

                            GUILayout.FlexibleSpace();
                            string[] triggerNames = { ToolLang.Get("On Start", "В начале"), ToolLang.Get("Custom Delay", "Задержка") };
                            int tIndex = ev.TriggerType == EAudioTriggerType.TimeDelay ? 1 : 0;
                            tIndex = EditorGUILayout.Popup(tIndex, triggerNames, GUILayout.Width(100));
                            ev.TriggerType = tIndex == 1 ? EAudioTriggerType.TimeDelay : EAudioTriggerType.OnStart;

                            if (ev.TriggerType == EAudioTriggerType.TimeDelay)
                                ev.TimeDelay = EditorGUILayout.FloatField(ev.TimeDelay, GUILayout.Width(40));
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(5);

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        ev.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), ev.AudioChannel);
                        string evChannelInfo = "";
                        if (ev.AudioChannel == EAudioChannel.BGM) evChannelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                        else if (ev.AudioChannel == EAudioChannel.SFX) evChannelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                        else if (ev.AudioChannel == EAudioChannel.Voice) evChannelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                        NovellaInspectorChrome.DrawHint(evChannelInfo);
                        GUILayout.EndVertical();

                        ev.AudioAction = (EAudioAction)EditorGUILayout.EnumPopup(ToolLang.Get("Action", "Действие"), ev.AudioAction);

                        if (ev.AudioAction == EAudioAction.Play)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(ToolLang.Get("Audio Clip", "Аудиофайл"), GUILayout.Width(126));
                            string evAudioName = ev.AudioAsset != null ? ev.AudioAsset.name : ToolLang.Get("None (Disabled)", "Пусто (Отключено)");

                            if (GUILayout.Button("🎵 " + evAudioName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                            {
                                NovellaGalleryWindow.ShowWindow(obj => {
                                    Undo.RecordObject(_currentTree, "Change Audio");
                                    ev.AudioAsset = obj as AudioClip;
                                    selectedNodeView.RefreshVisuals();
                                    _onMarkUnsaved?.Invoke();
                                }, NovellaGalleryWindow.EGalleryFilter.Audio);
                            }

                            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                            if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear Audio"); ev.AudioAsset = null; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();

                            ev.Volume = EditorGUILayout.Slider(ToolLang.Get("Volume", "Громкость"), ev.Volume, 0f, 1f);
                        }
                        GUILayout.EndVertical();
                        GUILayout.Space(5);
                    }
                }

                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Audio Edit"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА ПЕРЕМЕННЫХ (VARIABLE) ===
            // ==========================================
            if (nodeData is VariableNodeData varData)
            {
                DrawSectionHeader("📊", ToolLang.Get("Variables List", "Список переменных"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Changes story variables (money, reputation, flags) the moment the story reaches this node. The player sees nothing — it runs instantly and moves on.",
                    "Меняет переменные истории (деньги, репутацию, флаги) как только сюжет доходит до этой ноды. Игрок ничего не видит — срабатывает мгновенно и идёт дальше."));

                if (varData.Variables.Count == 0) varData.Variables.Add(new VariableUpdate());

                for (int i = 0; i < varData.Variables.Count; i++)
                {
                    var v = varData.Variables[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get($"Variable #{i + 1}", $"Переменная #{i + 1}"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    bool canDelete = varData.Variables.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Var"); varData.Variables.RemoveAt(i); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();

                    EditorGUI.BeginChangeCheck();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Name", "Имя"), GUILayout.Width(50));
                    DrawVariablePicker(v.VariableName, picked => v.VariableName = picked, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();

                    EVarType type = GetVarType(v.VariableName);

                    // Предупреждение: переменная выбрана, но её нет в Менеджере
                    // переменных (переименована/удалена) → в игре изменение
                    // молча пропустится.
                    if (!string.IsNullOrEmpty(v.VariableName)
                        && NovellaVariableSettings.Instance != null
                        && !NovellaVariableSettings.Instance.Variables.Any(d => d.Name == v.VariableName))
                    {
                        NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                            "⚠ This variable no longer exists in the Variable Manager — the change will be silently skipped in game. Pick an existing variable.",
                            "⚠ Этой переменной больше нет в Менеджере переменных — изменение молча пропустится в игре. Выбери существующую."), true);
                    }

                    GUILayout.BeginHorizontal();
                    if (type == EVarType.Integer)
                    {
                        string[] opNames = { ToolLang.Get("Set (=)", "Установить (=)"), ToolLang.Get("Add (+)", "Добавить (+)") };
                        // Безопасный clamp на случай если Operation = ListAdd/Remove/Clear
                        // от старого List-варианта переменной.
                        int opIdx = (int)v.VarOperation; if (opIdx > 1) opIdx = 0;
                        v.VarOperation = (EVarOperation)EditorGUILayout.Popup(opIdx, opNames, GUILayout.Width(100));
                        v.VarValue = EditorGUILayout.IntField(v.VarValue, GUILayout.ExpandWidth(true));
                    }
                    else if (type == EVarType.Float)
                    {
                        string[] opNames = { ToolLang.Get("Set (=)", "Установить (=)"), ToolLang.Get("Add (+)", "Добавить (+)") };
                        int opIdx = (int)v.VarOperation; if (opIdx > 1) opIdx = 0;
                        v.VarOperation = (EVarOperation)EditorGUILayout.Popup(opIdx, opNames, GUILayout.Width(100));
                        v.VarFloat = EditorGUILayout.FloatField(v.VarFloat, GUILayout.ExpandWidth(true));
                    }
                    else if (type == EVarType.Boolean)
                    {
                        v.VarOperation = EVarOperation.Set;
                        GUILayout.Label(ToolLang.Get("Set to", "Задать как"), GUILayout.Width(80));
                        v.VarBool = EditorGUILayout.Toggle(v.VarBool, GUILayout.ExpandWidth(true));
                    }
                    else if (type == EVarType.String)
                    {
                        v.VarOperation = EVarOperation.Set;
                        GUILayout.Label(ToolLang.Get("Set to", "Задать как"), GUILayout.Width(80));
                        v.VarString = EditorGUILayout.TextField(v.VarString ?? "", GUILayout.ExpandWidth(true));
                    }
                    else if (type == EVarType.Choice)
                    {
                        v.VarOperation = EVarOperation.Set;
                        GUILayout.Label(ToolLang.Get("Set to", "Задать как"), GUILayout.Width(80));
                        string[] choices = GetVarChoices(v.VariableName);
                        if (choices.Length == 0)
                        {
                            GUILayout.Label(ToolLang.Get("(no values defined)", "(значения не заданы)"),
                                EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                        }
                        else
                        {
                            int sel = Array.IndexOf(choices, (v.VarString ?? "").Replace("/", "-"));
                            if (sel < 0) sel = 0;
                            int newSel = EditorGUILayout.Popup(sel, choices, GUILayout.ExpandWidth(true));
                            if (newSel != sel) v.VarString = choices[newSel];
                            else if (string.IsNullOrEmpty(v.VarString)) v.VarString = choices[0];
                        }
                    }
                    else if (type == EVarType.List)
                    {
                        // Список операций: Add item / Remove item / Clear all.
                        string[] opNames = ToolLang.IsRU
                            ? new[] { "Добавить", "Убрать", "Очистить" }
                            : new[] { "Add item", "Remove item", "Clear all" };
                        EVarOperation[] opMap = { EVarOperation.ListAdd, EVarOperation.ListRemove, EVarOperation.ListClear };
                        int idx = Array.IndexOf(opMap, v.VarOperation);
                        if (idx == -1) idx = 0;
                        int newIdx = EditorGUILayout.Popup(idx, opNames, GUILayout.Width(100));
                        v.VarOperation = opMap[newIdx];

                        if (v.VarOperation != EVarOperation.ListClear)
                        {
                            v.VarString = EditorGUILayout.TextField(v.VarString ?? "", GUILayout.ExpandWidth(true));
                        }
                        else
                        {
                            GUILayout.Label(ToolLang.Get("(removes all items)", "(удалит все элементы)"),
                                EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                        }
                    }
                    GUILayout.EndHorizontal();

                    DrawTypeHint(type);

                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Var Edit"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                if (GUILayout.Button("+ " + ToolLang.Get("Add Variable", "Добавить переменную"), EditorStyles.miniButton, GUILayout.Height(25)))
                {
                    Undo.RecordObject(_currentTree, "Add Var");
                    varData.Variables.Add(new VariableUpdate());
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                EndLayout(); return;
            }

            // ==========================================
            // === НОДА УСЛОВИЯ (CONDITION) ===
            // ==========================================
            if (nodeData is ConditionNodeData condData)
            {
                DrawSectionHeader("❓", ToolLang.Get("Condition Logic", "Логика Условия (If/Else)"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Automatically sends the story down the True or False branch — no player choice. Use it for things like \"if the player has the key, open the door\".",
                    "Автоматически направляет историю по ветке Истина или Ложь — без выбора игрока. Например: «если у игрока есть ключ — открыть дверь»."));

                if (condData.Conditions.Count == 0) condData.Conditions.Add(new ChoiceCondition());

                for (int c = 0; c < condData.Conditions.Count; c++)
                {
                    var cond = condData.Conditions[c];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();

                    GUILayout.Label(ToolLang.Get("IF", "ЕСЛИ"), EditorStyles.boldLabel, GUILayout.Width(40));
                    DrawVariablePicker(cond.Variable, picked => cond.Variable = picked, GUILayout.ExpandWidth(true));

                    EVarType type = GetVarType(cond.Variable);
                    DrawConditionOperatorAndValue(cond.Variable, cond, opW: 50, valW: 80);

                    bool canDeleteCond = condData.Conditions.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDeleteCond);
                    GUI.backgroundColor = canDeleteCond ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Condition"); condData.Conditions.RemoveAt(c); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();

                    DrawTypeHint(type);

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                if (GUILayout.Button(ToolLang.Get("+ Add Condition (AND)", "+ Добавить условие (И)"), EditorStyles.miniButton, GUILayout.Height(25)))
                {
                    Undo.RecordObject(_currentTree, "Add Condition");
                    condData.Conditions.Add(new ChoiceCondition());
                }

                GUILayout.Space(10);
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "If ALL conditions above are met, the story goes to the 'True' port. Otherwise — the 'False' port.",
                    "Если ВСЕ условия выше выполняются — сюжет идёт по ветке 'Истина' (True). Иначе — по ветке 'Ложь' (False)."));

                // Предупреждение: неподключённая ветка = молчаливый тупик в игре.
                bool trueWired  = condData.Choices != null && condData.Choices.Count > 0 && !string.IsNullOrEmpty(condData.Choices[0].NextNodeID);
                bool falseWired = condData.Choices != null && condData.Choices.Count > 1 && !string.IsNullOrEmpty(condData.Choices[1].NextNodeID);
                if (!trueWired || !falseWired)
                {
                    NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                        "⚠ A branch is not connected. If the story takes an unconnected branch, it will STOP. Connect both the True and False ports in the graph.",
                        "⚠ Ветка не подключена. Если сюжет пойдёт по неподключённой ветке — он ОСТАНОВИТСЯ. Подключи оба порта (Истина и Ложь) в графе."), true);
                }

                EndLayout(); return;
            }

            // ==========================================
            // === НОДА РАНДОМА (RANDOM) ===
            // ==========================================
            if (nodeData is RandomNodeData rndData)
            {
                DrawSectionHeader("🎲", ToolLang.Get("Random Chances", "Случайные события (Шанс)"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Randomly picks ONE outcome, like a weighted dice roll. Higher weight = more likely. Each outcome connects to its own branch. Great for random events.",
                    "Случайно выбирает ОДИН исход, как взвешенный бросок кубика. Больше вес = выше шанс. Каждый исход ведёт в свою ветку. Удобно для случайных событий."));

                int totalMaxWeight = rndData.Choices.Sum(c => c.ChanceWeight + c.ChanceModifiers.Sum(m => m.BonusWeight));

                for (int i = 0; i < rndData.Choices.Count; i++)
                {
                    var choice = rndData.Choices[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);

                    int choiceMaxWeight = choice.ChanceWeight + choice.ChanceModifiers.Sum(m => m.BonusWeight);
                    float percentage = totalMaxWeight > 0 ? ((float)choiceMaxWeight / totalMaxWeight) * 100f : 0f;

                    GUILayout.Label(ToolLang.Get($"Max Chance {i + 1} ({percentage:F1}%)", $"Макс. Шанс {i + 1} ({percentage:F1}%)"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    bool canDelete = rndData.Choices.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65))) { Undo.RecordObject(_currentTree, "Remove Choice"); rndData.Choices.RemoveAt(i); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); break; }
                    GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); GUILayout.EndHorizontal();

                    GUILayout.Space(5);

                    EditorGUI.BeginChangeCheck();
                    int newWeight = EditorGUILayout.IntSlider(ToolLang.Get("Base Weight", "Базовый Вес"), choice.ChanceWeight, 1, 100);

                    GUILayout.Space(10);
                    GUILayout.Label(ToolLang.Get("Dynamic Modifiers (Bonus Weight):", "Динамические модификаторы (Доп. Вес):"), EditorStyles.miniBoldLabel);

                    for (int m = 0; m < choice.ChanceModifiers.Count; m++)
                    {
                        var mod = choice.ChanceModifiers[m];
                        GUILayout.BeginVertical();
                        GUILayout.BeginHorizontal();

                        GUILayout.Label(ToolLang.Get("IF", "ЕСЛИ"), GUILayout.Width(35));

                        DrawVariablePicker(mod.Variable, picked => mod.Variable = picked, GUILayout.ExpandWidth(true), GUILayout.MinWidth(80));

                        EVarType type = GetVarType(mod.Variable);
                        DrawConditionOperatorAndValue(mod.Variable, mod, opW: 45, valW: 50);

                        GUILayout.Label("➔", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(20));
                        GUILayout.Label("+", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight }, GUILayout.Width(12));
                        mod.BonusWeight = EditorGUILayout.IntField(mod.BonusWeight, GUILayout.Width(35));
                        GUILayout.Label(ToolLang.Get("Wgt", "Вес"), GUILayout.Width(30));

                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20))) { choice.ChanceModifiers.RemoveAt(m); GUI.backgroundColor = Color.white; break; }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        DrawTypeHint(type);
                        GUILayout.EndVertical();
                    }

                    if (GUILayout.Button("+ " + ToolLang.Get("Add Modifier", "Добавить модификатор"), EditorStyles.miniButton))
                    {
                        choice.ChanceModifiers.Add(new ChanceModifier());
                    }

                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Weight"); choice.ChanceWeight = newWeight; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

                    GUILayout.Space(5); GUILayout.EndVertical(); GUILayout.Space(5);
                }

                GUILayout.Space(10);
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "How it works: Base Weight = default share. Modifiers add EXTRA weight when their condition is true. Chance = Weight / Total Weight. The % shown above is the MAX chance if all modifiers apply.",
                    "Как это работает: Базовый Вес — стандартная доля. Модификаторы дают ЭКСТРА вес если их условие истинно. Шанс = Вес / Сумма всех весов. Процент выше — МАКСИМАЛЬНЫЙ шанс если все модификаторы сработают."));

                if (rndData.Choices.Count >= 4 && !rndData.UnlockChoiceLimit)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Default limit of 4 outcomes reached.", "Достигнут базовый лимит (4 исхода)."));
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); rndData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Chance", "Добавить шанс")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); rndData.Choices.Add(new NovellaChoice() { ChanceWeight = 50 }); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); } }
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА ВЕТВЛЕНИЯ (BRANCH) ===
            // ==========================================
            if (nodeData is BranchNodeData branchData)
            {
                DrawSectionHeader("🔀", $"{ToolLang.Get("Branch Choices", "Варианты выбора")} ({_window.PreviewLanguage})");
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "The player picks ONE option. Each choice connects to its own branch — if a choice's output isn't connected, the story ends there. Add conditions to show a choice only when a variable matches.",
                    "Игрок выбирает ОДИН вариант. Каждый выбор ведёт в свою ветку — если выход выбора ни к чему не подключён, на нём история закончится. Добавь условия чтобы показывать выбор только когда переменная подходит."));

                GUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(ToolLang.Get("Custom Button Prefab:", "Кастомный префаб кнопок:"), EditorStyles.miniBoldLabel, GUILayout.Width(170));
                string btnUiName = branchData.OverrideChoiceButtonPrefab != null ? branchData.OverrideChoiceButtonPrefab.name : ToolLang.Get("Default System Button", "Базовая системная кнопка");
                if (GUILayout.Button("🔘 " + btnUiName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                {
                    NovellaGalleryWindow.ShowWindow(obj => {
                        Undo.RecordObject(_currentTree, "Override Button UI");
                        branchData.OverrideChoiceButtonPrefab = obj as GameObject;
                        _onMarkUnsaved?.Invoke();
                        _window.Repaint();
                    }, NovellaGalleryWindow.EGalleryFilter.Prefab);
                }
                if (branchData.OverrideChoiceButtonPrefab != null)
                {
                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear Button"); branchData.OverrideChoiceButtonPrefab = null; _onMarkUnsaved?.Invoke(); }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);

                for (int i = 0; i < branchData.Choices.Count; i++)
                {
                    var choice = branchData.Choices[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);
                    GUILayout.Label(ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}"), EditorStyles.boldLabel); GUILayout.FlexibleSpace();

                    bool canDelete = branchData.Choices.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65))) { Undo.RecordObject(_currentTree, "Remove Choice"); branchData.Choices.RemoveAt(i); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); break; }
                    GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); GUILayout.EndHorizontal();
                    GUILayout.Space(5);

                    EditorGUI.BeginChangeCheck();
                    string currentText = choice.LocalizedText.GetText(_window.PreviewLanguage);
                    string newText = EditorGUILayout.TextField(ToolLang.Get("Button text", "Текст кнопки"), currentText);

                    GUILayout.Space(5);

                    if (choice.HasCondition)
                    {
                        choice.Conditions.Add(new ChoiceCondition { Variable = choice.ConditionVariable, Operator = EConditionOperator.GreaterOrEqual, Value = choice.ConditionValue });
                        choice.HasCondition = false;
                    }

                    if (choice.Conditions.Count > 0)
                    {
                        GUILayout.Label(ToolLang.Get("Conditions (AND):", "Условия (И):"), new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.8f, 0.5f, 0.2f) } });
                        for (int c = 0; c < choice.Conditions.Count; c++)
                        {
                            var cond = choice.Conditions[c];
                            GUILayout.BeginVertical();
                            GUILayout.BeginHorizontal();

                            GUILayout.Label(ToolLang.Get("IF", "ЕСЛИ"), GUILayout.Width(35));
                            DrawVariablePicker(cond.Variable, picked => cond.Variable = picked, GUILayout.ExpandWidth(true));

                            EVarType type = GetVarType(cond.Variable);
                            DrawConditionOperatorAndValue(cond.Variable, cond, opW: 50, valW: 80);

                            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Condition"); choice.Conditions.RemoveAt(c); GUI.backgroundColor = Color.white; break; }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();
                            DrawTypeHint(type);
                            GUILayout.EndVertical();
                        }
                    }

                    if (GUILayout.Button(ToolLang.Get("+ Add Condition", "+ Добавить условие"), EditorStyles.miniButton, GUILayout.Height(20)))
                    {
                        Undo.RecordObject(_currentTree, "Add Condition");
                        choice.Conditions.Add(new ChoiceCondition());
                    }

                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Choice"); choice.LocalizedText.SetText(_window.PreviewLanguage, newText); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

                    // Если задана — Player НЕ спавнит свою кнопку, а кликает на эту scene-кнопку.
                    {
                        var choiceLocal = choice;
                        NovellaEngine.Editor.UIBindings.UIBindingFieldGUI.Draw(
                            ToolLang.Get("🔘 Scene button (optional)", "🔘 Кнопка из сцены (опц.)"),
                            choiceLocal.UIButtonTargetId,
                            NovellaEngine.Runtime.UI.UIBindingKind.Button,
                            newId => { Undo.RecordObject(_currentTree, "Bind Choice Button"); choiceLocal.UIButtonTargetId = newId; _onMarkUnsaved?.Invoke(); });
                    }

                    GUILayout.Space(5); GUILayout.EndVertical(); GUILayout.Space(5);
                }

                GUILayout.Space(10);
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "If a choice has multiple conditions, ALL of them must be met (AND logic).",
                    "Если у выбора несколько условий, все они должны быть выполнены (логика И)."));

                if (branchData.Choices.Count >= 4 && !branchData.UnlockChoiceLimit)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выбора)."));
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); branchData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Choice", "Добавить выбор")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); branchData.Choices.Add(new NovellaChoice()); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); } }
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА СМЕНЫ ФОНА (BACKGROUND / SCENE SETTINGS) ===
            // ==========================================
            if (nodeData is SceneSettingsNodeData sceneData)
            {
                DrawSectionHeader("🎬", ToolLang.Get("Scene Settings", "Настройки Сцены"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Sets the background and arranges characters on stage. Turn on 'Sync with dialogue' to apply changes on specific lines.",
                    "Задаёт фон и расставляет персонажей на сцене. Включи «Синхр. с диалогом» чтобы применять изменения на конкретных репликах."));
                EditorGUIUtility.labelWidth = 130;
                EditorGUI.BeginChangeCheck();

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.SceneSyncNodeID == sceneData.NodeID);
                sceneData.SyncWithDialogue = (syncedDialogue != null);

                if (!sceneData.SyncWithDialogue)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get(
                        "Standalone mode. Changes the visual backdrop before moving to the next node.",
                        "Одиночный режим. Меняет фон перед переходом к следующей ноде."));
                    GUILayout.Space(10);

                    GUILayout.BeginVertical(EditorStyles.helpBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("New Sprite", "Новый Спрайт"), GUILayout.Width(126));
                    string sprName = sceneData.BgSprite != null ? sceneData.BgSprite.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");

                    if (GUILayout.Button("🖼 " + sprName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Undo.RecordObject(_currentTree, "Change BG");
                            sceneData.BgSprite = obj as Sprite;
                            if (sceneData.BgSprite == null && obj is Texture2D tex)
                            {
                                string path = AssetDatabase.GetAssetPath(tex);
                                sceneData.BgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                            }
                            selectedNodeView.RefreshVisuals();
                            _onMarkUnsaved?.Invoke();
                            _window.Repaint();
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }

                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                    if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear BG"); sceneData.BgSprite = null; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    sceneData.BgColor = EditorGUILayout.ColorField(ToolLang.Get("Tint Color", "Цвет / Оттенок"), sceneData.BgColor);

                    GUILayout.Space(5);
                    DrawLine(new Color(1, 1, 1, 0.1f));
                    GUILayout.Space(5);

                    string[] transNames = { ToolLang.Get("None (Instant)", "Нет (Мгновенно)"), ToolLang.Get("Fade", "Растворение"), ToolLang.Get("Slide Left", "Сдвиг Влево"), ToolLang.Get("Slide Right", "Сдвиг Вправо"), ToolLang.Get("Flash White", "Вспышка (Белая)"), ToolLang.Get("Flash Black", "Вспышка (Черная)") };
                    sceneData.BgTransition = (EBgTransition)EditorGUILayout.Popup(ToolLang.Get("Transition", "Эффект перехода"), (int)sceneData.BgTransition, transNames);

                    if (sceneData.BgTransition != EBgTransition.None)
                    {
                        sceneData.BgTransitionTime = EditorGUILayout.FloatField(new GUIContent(ToolLang.Get("Duration (sec)", "Длительность (с)")), sceneData.BgTransitionTime);
                        sceneData.BgTransitionTime = Mathf.Max(0.1f, sceneData.BgTransitionTime);
                    }

                    GUILayout.Space(5);
                    sceneData.BgClearCharacters = EditorGUILayout.ToggleLeft(new GUIContent(" " + ToolLang.Get("Clear All Characters", "Очистить массовку"), ToolLang.Get("Removes all current characters from the screen.", "Удаляет всех текущих персонажей с экрана при смене фона.")), sceneData.BgClearCharacters, EditorStyles.boldLabel);
                    GUILayout.EndVertical();
                }
                else
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get(
                        "Synced with Dialogue. Tie scene changes (backgrounds, hiding characters) to specific lines.",
                        "Синхронизировано с Диалогом. Привяжите изменения сцены к конкретным репликам."));
                    GUILayout.Space(5);

                    for (int i = 0; i < sceneData.SceneEvents.Count; i++)
                    {
                        var ev = sceneData.SceneEvents[i];

                        Color baseCol = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.15f, 0.25f, 0.3f, 0.5f);
                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUI.backgroundColor = baseCol;

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"#{i + 1}", EditorStyles.boldLabel, GUILayout.Width(30));
                        GUILayout.FlexibleSpace();
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { sceneData.SceneEvents.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        if (syncedDialogue != null && syncedDialogue.DialogueLines.Count > 0)
                        {
                            string[] lineOptions = new string[syncedDialogue.DialogueLines.Count];
                            for (int l = 0; l < syncedDialogue.DialogueLines.Count; l++)
                            {
                                string spk = syncedDialogue.DialogueLines[l].Speaker != null ? syncedDialogue.DialogueLines[l].Speaker.name : ToolLang.Get("Narrator", "Автор");
                                string textPreview = syncedDialogue.DialogueLines[l].LocalizedPhrase.GetText(_window.PreviewLanguage);
                                if (textPreview.Length > 15) textPreview = textPreview.Substring(0, 15) + "...";
                                lineOptions[l] = $"#{l + 1} ({spk}): {textPreview}";
                            }

                            GUILayout.Label(ToolLang.Get("Line", "Репл."), GUILayout.Width(50));
                            ev.LineIndex = EditorGUILayout.Popup(ev.LineIndex, lineOptions, GUILayout.ExpandWidth(true));
                        }

                        GUILayout.Space(10);
                        string[] triggerNames = { ToolLang.Get("Start", "Старт"), ToolLang.Get("End", "Конец"), ToolLang.Get("Delay", "Время") };
                        ev.TriggerType = (EAudioTriggerType)EditorGUILayout.Popup((int)ev.TriggerType, triggerNames, GUILayout.Width(80));

                        if (ev.TriggerType == EAudioTriggerType.TimeDelay)
                        {
                            ev.TimeDelay = EditorGUILayout.FloatField(ev.TimeDelay, GUILayout.Width(50));
                            ev.TimeDelay = Mathf.Max(0, ev.TimeDelay);
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.Space(5);
                        DrawLine(new Color(1, 1, 1, 0.1f));
                        GUILayout.Space(5);

                        string[] actionNames = {
                            ToolLang.Get("Change Background", "Сменить фон"),
                            ToolLang.Get("Clear All Characters", "Очистить массовку"),
                            ToolLang.Get("Hide Character", "Скрыть персонажа"),
                            ToolLang.Get("Show Character", "Показать персонажа"),
                            ToolLang.Get("Show UI element", "Показать UI элемент"),
                            ToolLang.Get("Hide UI element", "Скрыть UI элемент"),
                            ToolLang.Get("Set UI text", "Задать UI текст")
                        };
                        ev.ActionType = (ESceneActionType)EditorGUILayout.Popup(ToolLang.Get("Action", "Действие"), (int)ev.ActionType, actionNames);

                        GUILayout.Space(5);

                        if (ev.ActionType == ESceneActionType.ChangeBackground)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(126));
                            string evSprName = ev.BgSprite != null ? ev.BgSprite.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");

                            if (GUILayout.Button("🖼 " + evSprName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                            {
                                NovellaGalleryWindow.ShowWindow(obj => {
                                    Undo.RecordObject(_currentTree, "Change Event BG");
                                    ev.BgSprite = obj as Sprite;
                                    if (ev.BgSprite == null && obj is Texture2D tex)
                                    {
                                        string path = AssetDatabase.GetAssetPath(tex);
                                        ev.BgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                                    }
                                    _onMarkUnsaved?.Invoke();
                                    _window.Repaint();
                                }, NovellaGalleryWindow.EGalleryFilter.Image);
                            }

                            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                            if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear Event BG"); ev.BgSprite = null; _onMarkUnsaved?.Invoke(); }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();

                            ev.BgColor = EditorGUILayout.ColorField(ToolLang.Get("Color", "Цвет"), ev.BgColor);

                            // Переход применяется ТОЛЬКО для несинхронизированной
                            // ноды. При «Синхр. с диалогом» рантайм меняет фон
                            // мгновенно и BgTransition игнорирует — поэтому
                            // прячем контрол, чтобы не вводить в заблуждение.
                            if (!sceneData.SyncWithDialogue)
                            {
                                string[] transNames = { ToolLang.Get("None", "Нет"), ToolLang.Get("Fade", "Растворение"), ToolLang.Get("Slide Left", "Сдвиг Влево"), ToolLang.Get("Slide Right", "Сдвиг Вправо"), ToolLang.Get("Flash White", "Вспышка (Белая)"), ToolLang.Get("Flash Black", "Вспышка (Черная)") };
                                ev.BgTransition = (EBgTransition)EditorGUILayout.Popup(ToolLang.Get("Transition", "Переход"), (int)ev.BgTransition, transNames);
                                if (ev.BgTransition != EBgTransition.None) ev.BgTransitionTime = Mathf.Max(0.1f, EditorGUILayout.FloatField(ToolLang.Get("Duration", "Время"), ev.BgTransitionTime));
                            }
                            else
                            {
                                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                                    "Synced with dialogue: the background changes instantly — transition effects aren't applied here.",
                                    "Синхронизировано с диалогом: фон меняется мгновенно — эффекты перехода здесь не применяются."));
                            }
                        }
                        else if (ev.ActionType == ESceneActionType.ShowUI ||
                                 ev.ActionType == ESceneActionType.HideUI ||
                                 ev.ActionType == ESceneActionType.SetUIText)
                        {
                            // Drag&drop UI-элемента из сцены — Drawer ставит/находит NovellaUIBinding.
                            NovellaEngine.Editor.UIBindings.UIBindingFieldGUI.Draw(ToolLang.Get("UI element", "UI элемент"),
                                ev.UITargetId,
                                NovellaEngine.Runtime.UI.UIBindingKind.Any,
                                newId => { ev.UITargetId = newId; _onMarkUnsaved?.Invoke(); });

                            if (ev.ActionType == ESceneActionType.SetUIText)
                            {
                                ev.UITextValue = EditorGUILayout.TextField(
                                    ToolLang.Get("Text", "Текст"), ev.UITextValue);
                                ev.UITextIsLocalizationKey = EditorGUILayout.ToggleLeft(
                                    ToolLang.Get("Treat as localization key", "Это ключ локализации"),
                                    ev.UITextIsLocalizationKey);
                            }
                        }
                        else if (ev.ActionType == ESceneActionType.HideCharacter || ev.ActionType == ESceneActionType.ShowCharacter)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(ToolLang.Get("Character", "Персонаж"), GUILayout.Width(126));

                            if (syncedDialogue != null && syncedDialogue.ActiveCharacters.Count > 0)
                            {
                                var validChars = syncedDialogue.ActiveCharacters.Where(c => c.CharacterAsset != null).Select(c => c.CharacterAsset).ToList();
                                if (validChars.Count > 0)
                                {
                                    string[] charNames = validChars.Select(c => c.name).ToArray();
                                    int currentIndex = validChars.IndexOf(ev.TargetCharacter);
                                    if (currentIndex == -1) currentIndex = 0;
                                    int newIndex = EditorGUILayout.Popup(currentIndex, charNames);
                                    ev.TargetCharacter = validChars[newIndex];
                                }
                                else
                                {
                                    GUILayout.Label(ToolLang.Get("No chars in Layout", "Массовка пуста"), EditorStyles.centeredGreyMiniLabel);
                                    ev.TargetCharacter = null;
                                }
                            }
                            else
                            {
                                GUILayout.Label(ToolLang.Get("Not synced with Layout", "Нет массовки"), EditorStyles.centeredGreyMiniLabel);
                            }
                            GUILayout.EndHorizontal();
                        }

                        GUILayout.EndVertical();
                        GUILayout.Space(5);
                    }

                    if (sceneData.SceneEvents.Count >= 4 && !sceneData.UnlockLimit)
                    {
                        NovellaInspectorChrome.DrawWarn(ToolLang.Get("Default limit of 4 events reached.", "Достигнут базовый лимит (4 события)."), true);
                        if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25)))
                        {
                            Undo.RecordObject(_currentTree, "Unlock Scene Events");
                            sceneData.UnlockLimit = true;
                            _onMarkUnsaved?.Invoke();
                        }
                    }
                    else
                    {
                        GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                        if (GUILayout.Button("+ " + ToolLang.Get("Add Scene Event", "Добавить событие сцены"), GUILayout.Height(30)))
                        {
                            Undo.RecordObject(_currentTree, "Add Scene Event");
                            sceneData.SceneEvents.Add(new SceneSettingsEvent());
                            EditorUtility.SetDirty(_currentTree);
                            selectedNodeView.RefreshVisuals();
                            _onMarkUnsaved?.Invoke();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_currentTree);
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                EndLayout(); return;
            }

            // ==========================================
            // === НОДА АНИМАЦИЙ (STANDALONE ANIMATION) ===
            // ==========================================
            if (nodeData is AnimationNodeData animData)
            {
                DrawSectionHeader("✨", ToolLang.Get("Animation Sequence", "Настройки Анимаций"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Plays visual effects (shake, fade, move, scale…) on the camera, background or a character. Turn on 'Sync with dialogue' to fire effects on specific lines.",
                    "Проигрывает визуальные эффекты (тряска, затухание, движение, масштаб…) на камере, фоне или персонаже. Включи «Синхр. с диалогом» чтобы запускать эффекты на конкретных репликах."));

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.AnimSyncNodeID == animData.NodeID);
                animData.SyncWithDialogue = (syncedDialogue != null);

                if (animData.SyncWithDialogue)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Synced with Dialogue. Tie animations to specific lines.", "Синхронизировано с Диалогом. Привяжите анимации к конкретным репликам."));
                }
                else
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Standalone mode. Animations will play sequentially or by time delay.", "Обычный режим. Анимации проиграются по очереди или по задержке времени."));
                }

                if (animData.AnimEvents.Count == 0)
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("No animations added.", "Нет добавленных анимаций."));
                }
                else
                {
                    for (int i = 0; i < animData.AnimEvents.Count; i++)
                    {
                        DrawAnimEventConfig(animData.AnimEvents[i], i, _currentTree, animData, animData.SyncWithDialogue, syncedDialogue);
                    }
                }

                int animLimit = 4;
                if (!animData.SyncWithDialogue && animData.AnimEvents.Count >= animLimit && !animData.UnlockAnimLimit)
                {
                    NovellaInspectorChrome.DrawWarn(ToolLang.Get("Default limit of 4 animations reached.", "Достигнут базовый лимит (4 анимации)."), true);
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25)))
                    {
                        Undo.RecordObject(_currentTree, "Unlock Anim");
                        animData.UnlockAnimLimit = true;
                        _onMarkUnsaved?.Invoke();
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.8f);
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Animation Event", "Добавить анимацию"), GUILayout.Height(30)))
                    {
                        Undo.RecordObject(_currentTree, "Add Anim Event");
                        animData.AnimEvents.Add(new NovellaAnimEvent());
                        EditorUtility.SetDirty(_currentTree);
                        selectedNodeView.RefreshVisuals();
                        _onMarkUnsaved?.Invoke();
                    }
                    GUI.backgroundColor = Color.white;
                }
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА ВЫЗОВА СОБЫТИЯ (EVENT BROADCAST) ===
            // ==========================================
            if (nodeData is EventBroadcastNodeData ebData)
            {
                DrawSectionHeader("⚡", ToolLang.Get("Event Broadcast", "Вызов Внешнего События"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Sends a signal to your game's C# code — e.g. give an item, start a minigame, unlock something. Just type a unique Event ID; the code recipe below is for programmers (optional).",
                    "Посылает сигнал в C#-код твоей игры — например выдать предмет, запустить миниигру, что-то разблокировать. Просто впиши уникальный ID события; рецепт кода ниже — для программистов (опционально)."));
                GUILayout.BeginVertical(EditorStyles.helpBox);

                GUIStyle codeStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 12 };
                GUILayout.Label(ToolLang.Get(
                    "<b>How to use (C# Code):</b>\n" +
                    "1. Open any of your game scripts.\n" +
                    "2. Subscribe to the event in <b>Start()</b> or <b>OnEnable()</b>:\n" +
                    "<color=#4da6ff>NovellaPlayer.OnNovellaEvent += HandleEvent;</color>\n\n" +
                    "3. Write the handler method:\n" +
                    "<color=#4da6ff>private void HandleEvent(string id, string param) {\n" +
                    "    if (id == \"MyEvent\") Debug.Log(param);\n" +
                    "}</color>\n\n" +
                    "4. Unsubscribe in <b>OnDestroy()</b>:\n" +
                    "<color=#4da6ff>NovellaPlayer.OnNovellaEvent -= HandleEvent;</color>",

                    "<b>Как использовать (C# Код):</b>\n" +
                    "1. Откройте любой ваш скрипт в игре.\n" +
                    "2. Подпишитесь на событие в <b>Start()</b> или <b>OnEnable()</b>:\n" +
                    "<color=#4da6ff>NovellaPlayer.OnNovellaEvent += HandleEvent;</color>\n\n" +
                    "3. Напишите метод-обработчик:\n" +
                    "<color=#4da6ff>private void HandleEvent(string id, string param) {\n" +
                    "    if (id == \"GiveItem\") GiveItem(param);\n" +
                    "}</color>\n\n" +
                    "4. Обязательно отпишитесь в <b>OnDestroy()</b>:\n" +
                    "<color=#4da6ff>NovellaPlayer.OnNovellaEvent -= HandleEvent;</color>"
                ), codeStyle);

                GUILayout.Space(10);
                EditorGUI.BeginChangeCheck();

                float lwEv = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = ToolLang.IsRU ? 170 : 140;
                ebData.BroadcastEventName = EditorGUILayout.TextField(new GUIContent(ToolLang.Get("Event ID (String)", "ID События (Строка)"), ToolLang.Get("Exact match with 'id' in your C# code", "Точное совпадение с параметром id в вашем C# коде")), ebData.BroadcastEventName);
                ebData.BroadcastEventParam = EditorGUILayout.TextField(new GUIContent(ToolLang.Get("Parameter (Optional)", "Параметр (Опционально)"), ToolLang.Get("Passed as 'string param'. Parse it to int/bool if needed.", "Передается как string param. В коде можно конвертировать через int.Parse() и т.д.")), ebData.BroadcastEventParam);
                EditorGUIUtility.labelWidth = lwEv;

                if (string.IsNullOrWhiteSpace(ebData.BroadcastEventName))
                    NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                        "⚠ Event ID is empty — your code won't be able to catch this event. Give it a unique name.",
                        "⚠ ID события пустой — твой код не сможет поймать это событие. Задай уникальное имя."), true);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_currentTree);
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                GUILayout.EndVertical();
                EndLayout(); return;
            }

            // ==========================================
            // === НОДА ДИАЛОГА И ИВЕНТА (DIALOGUE / EVENT) ===
            // ==========================================
            if (nodeData is DialogueNodeData dialData)
            {
                DrawSectionHeader("👥", ToolLang.Get("Scene Layout", "Массовка"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Characters added here will appear in the scene. Speakers in dialogue lines can only be picked from this list.",
                    "Персонажи добавленные сюда появятся в сцене. Спикеров для реплик можно выбирать ТОЛЬКО из этого списка."));

                if (NovellaInspectorChrome.DrawSlimBtn(
                    "🛠  " + ToolLang.Get("Character Editor", "Редактор персонажей")))
                {
                    NovellaCharacterEditorModule.OpenWindow();
                }
                GUILayout.Space(10);

                // ─── ScrollView для большого списка ───
                // Когда персонажей > ACTIVE_CHARS_SCROLL_THRESHOLD (7),
                // ограничиваем высоту списка и включаем вертикальный скролл.
                // Высота = THRESHOLD * card-height. Так инспектор остаётся
                // компактным при массовых сценах (например 12+ персонажей).
                bool useScrollForActive = dialData.ActiveCharacters.Count > ACTIVE_CHARS_SCROLL_THRESHOLD;
                if (useScrollForActive)
                {
                    float scrollH = ACTIVE_CHARS_SCROLL_THRESHOLD * ACTIVE_CHAR_CARD_HEIGHT + 4;
                    _activeCharsScroll = GUILayout.BeginScrollView(_activeCharsScroll,
                        GUILayout.Height(scrollH));
                }

                for (int i = 0; i < dialData.ActiveCharacters.Count; i++)
                {
                    var activeChar = dialData.ActiveCharacters[i];

                    // ─── Card-обёртка персонажа ───
                    // Тёмная плашка BgRaised + 1px border. Простая структура:
                    // dot + имя + remove. Все persistence-настройки (Plane,
                    // Emotion, Scale, Flip, Position) теперь редактируются
                    // в Dialogue Editor — там для этого есть scene preview.
                    Rect cardRect = EditorGUILayout.BeginVertical();
                    GUILayout.Space(2);

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(10);

                    // Color-dot 12×12 = ThemeColor персонажа.
                    Rect dotR = GUILayoutUtility.GetRect(12, 12,
                        GUILayout.Width(12), GUILayout.Height(28));
                    Rect dotInner = new Rect(dotR.x, dotR.y + 8, 12, 12);
                    bool hasChar = activeChar.CharacterAsset != null;
                    Color dotCol = hasChar
                        ? activeChar.CharacterAsset.ThemeColor
                        : new Color(NovellaGraphTheme.Text3.r, NovellaGraphTheme.Text3.g, NovellaGraphTheme.Text3.b, 0.5f);
                    EditorGUI.DrawRect(dotInner, dotCol);
                    NovellaInspectorChrome.DrawBorder(dotInner,
                        new Color(dotCol.r, dotCol.g, dotCol.b, 0.85f));

                    GUILayout.Space(10);

                    // Имя — кликабельное, открывает CharacterSelector.
                    string charName = hasChar
                        ? activeChar.CharacterAsset.name
                        : ToolLang.Get("Pick a character…", "Выбрать персонажа…");
                    var nameSt = new GUIStyle(EditorStyles.label) {
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        // «Не выбран» показываем Text2 (читаемо), не Text4 (мерт)
                        // + italic — стандартное «placeholder» обозначение.
                        normal = { textColor = hasChar
                            ? NovellaGraphTheme.Text1
                            : NovellaGraphTheme.Text2 }
                    };
                    if (!hasChar) nameSt.fontStyle = FontStyle.Italic;
                    if (GUILayout.Button(charName, nameSt,
                        GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                    {
                        NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => {
                            Undo.RecordObject(_currentTree, "Change Character Asset");
                            activeChar.CharacterAsset = selectedChar;
                            GUI.changed = true;
                            SyncScenePreview(dialData);
                            _window.Repaint();
                            _onMarkUnsaved?.Invoke();
                        });
                    }

                    // ✕ — удалить из сцены.
                    if (NovellaInspectorChrome.DrawIconBtn("✕",
                        ToolLang.Get("Remove character from scene", "Убрать персонажа со сцены"),
                        danger: true, size: 22))
                    {
                        Undo.RecordObject(_currentTree, "Remove Character");
                        dialData.ActiveCharacters.RemoveAt(i);
                        GUI.changed = true;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.Space(10);
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);
                    EditorGUILayout.EndVertical();

                    // Рисуем фон + border плашки ПОСЛЕ контента (Repaint).
                    if (Event.current.type == EventType.Repaint)
                    {
                        EditorGUI.DrawRect(cardRect, new Color(
                            NovellaGraphTheme.BgRaised.r, NovellaGraphTheme.BgRaised.g,
                            NovellaGraphTheme.BgRaised.b, 0.6f));
                        NovellaInspectorChrome.DrawBorder(cardRect, NovellaGraphTheme.Border);
                    }

                    EditorGUIUtility.labelWidth = originalLabelWidth;
                    GUILayout.Space(4);
                }

                if (useScrollForActive)
                {
                    GUILayout.EndScrollView();
                    // Подсказка-счётчик что список свёрнут в скролл.
                    var miniSt = new GUIStyle(EditorStyles.miniLabel) {
                        fontSize = 10, fontStyle = FontStyle.Italic,
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = NovellaGraphTheme.Text3 }
                    };
                    GUILayout.Label(string.Format(
                        ToolLang.Get("{0} characters in scene · scroll to see all",
                                     "{0} персонажей в сцене · скролль чтобы увидеть всех"),
                        dialData.ActiveCharacters.Count), miniSt);
                }

                GUILayout.Space(2);
                if (NovellaInspectorChrome.DrawSlimBtn(
                    "+  " + ToolLang.Get("Add Character to Scene", "Добавить персонажа на сцену"),
                    GUILayout.Height(28)))
                {
                    Undo.RecordObject(_currentTree, "Add Character");
                    dialData.ActiveCharacters.Add(new CharacterInDialogue());
                    GUI.changed = true;
                }

                if (GUI.changed)
                {
                    var activeAssets = dialData.ActiveCharacters.Where(ac => ac.CharacterAsset != null).Select(ac => ac.CharacterAsset).ToList();
                    bool needGraphRefresh = false;

                    foreach (var line in dialData.DialogueLines)
                    {
                        if (line.Speaker != null && !activeAssets.Contains(line.Speaker))
                        {
                            line.Speaker = null;
                            line.Mood = "Default";
                            needGraphRefresh = true;
                        }
                    }

                    if (needGraphRefresh) selectedNodeView.RefreshVisuals();

                    selectedNodeView.RefreshVisuals();
                    SyncScenePreview(dialData);
                    _onMarkUnsaved?.Invoke();
                    _window.Repaint();
                }

                DrawSectionHeader("💬", $"{ToolLang.Get("Dialogue Lines", "Реплики")} · {_window.PreviewLanguage}");

                // Подсказка-объяснение что делать.
                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "Click any line to open it in the Dialogue Editor. Use ✕ to remove. New lines are added inside the editor.",
                    "Кликни по реплике чтобы открыть её в редакторе диалогов. ✕ — удалить. Новые реплики добавляются внутри редактора."));

                GUILayout.Space(6);

                // ─── Компактный список реплик ───
                // Каждая строка: color-dot спикера + имя · превью-текст + ✕.
                // Hover — cyan-tint фон. Click row — открывает редактор на этой строке.
                // При большом количестве реплик показываем только первые
                // MAX_PREVIEW_LINES — остальные доступны через редактор диалогов
                // (полный список в инспекторе раздувает панель и всё равно
                // редактируется только в Dialogue Editor).
                const int MAX_PREVIEW_LINES = 5;
                int totalLines  = dialData.DialogueLines.Count;
                int previewLimit = System.Math.Min(totalLines, MAX_PREVIEW_LINES);
                int lineToRemove = -1;
                for (int li = 0; li < previewLimit; li++)
                {
                    var line = dialData.DialogueLines[li];
                    Rect rowR = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
                    bool rowHover = rowR.Contains(Event.current.mousePosition);

                    // Background hover.
                    if (rowHover)
                    {
                        EditorGUI.DrawRect(rowR, new Color(NovellaGraphTheme.Accent.r,
                            NovellaGraphTheme.Accent.g, NovellaGraphTheme.Accent.b, 0.06f));
                    }
                    // Цветная полоска слева 2px = ThemeColor спикера (или Border если нет).
                    Color spkColor = line.Speaker != null
                        ? line.Speaker.ThemeColor
                        : new Color(NovellaGraphTheme.Border.r, NovellaGraphTheme.Border.g, NovellaGraphTheme.Border.b, 0.6f);
                    EditorGUI.DrawRect(new Rect(rowR.x, rowR.y, 2, rowR.height), spkColor);

                    // Number pill убран по просьбе пользователя — нумерация реплик
                    // в инспекторе мешает (видно в редакторе диалогов и так).
                    // Левый отступ съёжился до 10px чтобы не пустовало.

                    // Speaker color-dot.
                    Rect dotR = new Rect(rowR.x + 10, rowR.y + 10, 8, 8);
                    EditorGUI.DrawRect(dotR, spkColor);
                    NovellaInspectorChrome.DrawBorder(dotR,
                        new Color(spkColor.r, spkColor.g, spkColor.b, 0.85f));

                    // Speaker name + text preview.
                    string spkName = line.Speaker != null ? line.Speaker.name
                        : ToolLang.Get("(no speaker)", "(без спикера)");
                    string previewText = line.LocalizedPhrase != null
                        ? line.LocalizedPhrase.GetText(_window.PreviewLanguage) : "";
                    string trimmed;
                    if (string.IsNullOrEmpty(previewText))
                        trimmed = ToolLang.Get("(empty line)", "(пустая реплика)");
                    else if (previewText.Length > 60)
                        trimmed = "«" + previewText.Substring(0, 58).TrimEnd() + "…»";
                    else
                        trimmed = "«" + previewText + "»";

                    var spkSt = new GUIStyle(EditorStyles.label) {
                        fontSize = 11, fontStyle = FontStyle.Bold,
                        normal = { textColor = line.Speaker != null
                            ? NovellaGraphTheme.Text1 : NovellaGraphTheme.Text4 }
                    };
                    var textSt = new GUIStyle(EditorStyles.label) {
                        fontSize = 11,
                        fontStyle = string.IsNullOrEmpty(previewText) ? FontStyle.Italic : FontStyle.Normal,
                        normal = { textColor = string.IsNullOrEmpty(previewText)
                            ? NovellaGraphTheme.Text4 : NovellaGraphTheme.Text2 }
                    };
                    float spkLabelW = spkSt.CalcSize(new GUIContent(spkName)).x;
                    Rect spkR = new Rect(dotR.xMax + 6, rowR.y, spkLabelW, rowR.height);
                    GUI.Label(spkR, spkName, spkSt);
                    Rect textR = new Rect(spkR.xMax + 6, rowR.y, rowR.width - spkR.xMax - 36, rowR.height);
                    var textSt2 = new GUIStyle(textSt) { clipping = TextClipping.Clip };
                    GUI.Label(textR, trimmed, textSt2);

                    // ✕ remove (24×20).
                    Rect xR = new Rect(rowR.xMax - 26, rowR.y + 4, 22, 20);
                    bool xHover = xR.Contains(Event.current.mousePosition);
                    if (xHover)
                    {
                        EditorGUI.DrawRect(xR, new Color(NovellaGraphTheme.Danger.r,
                            NovellaGraphTheme.Danger.g, NovellaGraphTheme.Danger.b, 0.18f));
                    }
                    var xSt = new GUIStyle(EditorStyles.miniLabel) {
                        fontSize = 11, fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = xHover
                            ? NovellaGraphTheme.Danger
                            : new Color(NovellaGraphTheme.Danger.r, NovellaGraphTheme.Danger.g, NovellaGraphTheme.Danger.b, 0.55f) }
                    };
                    GUI.Label(xR, "✕", xSt);
                    EditorGUIUtility.AddCursorRect(xR, MouseCursor.Link);

                    // Click handler — приоритет ✕ выше чем row.
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        if (xHover)
                        {
                            lineToRemove = li;
                            Event.current.Use();
                        }
                        else if (rowHover)
                        {
                            // Открываем редактор диалогов; параметр-callback'ом
                            // выставляем preview-line на эту реплику чтобы она
                            // была сразу в фокусе.
                            int capIdx = li;
                            var capDial = dialData;
                            NovellaDialogueEditorWindow.OpenWindow(_currentTree, capDial, _window.PreviewLanguage,
                                (lineIndex) =>
                                {
                                    _activePreviewLineIndex = lineIndex;
                                    SyncScenePreview(capDial);
                                    _window.Repaint();
                                },
                                _onMarkUnsaved,
                                () =>
                                {
                                    selectedNodeView.RefreshVisuals();
                                    _window.Repaint();
                                });
                            // Подкидываем редактору индекс открытой строки.
                            _activePreviewLineIndex = capIdx;
                            SyncScenePreview(capDial);
                            Event.current.Use();
                        }
                    }
                    if (rowHover && Event.current.type == EventType.MouseMove) _window.Repaint();
                    if (rowHover) EditorGUIUtility.AddCursorRect(rowR, MouseCursor.Link);
                }

                // ─── «+ ещё N · открыть в редакторе» ───
                // Когда реплик > MAX_PREVIEW_LINES — рисуем компактную
                // плашку-ссылку. Клик — открывает Dialogue Editor (тот же
                // флоу что и при клике по конкретной строке выше).
                if (totalLines > MAX_PREVIEW_LINES)
                {
                    int remaining = totalLines - MAX_PREVIEW_LINES;
                    Rect moreR = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
                    bool moreHover = moreR.Contains(Event.current.mousePosition);

                    // Лёгкий cyan-tint фон на hover (как на обычной строке).
                    if (moreHover)
                    {
                        EditorGUI.DrawRect(moreR, new Color(NovellaGraphTheme.Accent.r,
                            NovellaGraphTheme.Accent.g, NovellaGraphTheme.Accent.b, 0.06f));
                    }
                    // 1px пунктир-разделитель сверху — визуально отделяет от
                    // последней превью-строки.
                    EditorGUI.DrawRect(new Rect(moreR.x + 4, moreR.y, moreR.width - 8, 1),
                        new Color(NovellaGraphTheme.Border.r, NovellaGraphTheme.Border.g,
                                  NovellaGraphTheme.Border.b, 0.45f));

                    // NovellaPlurals.Lines(N) сразу даёт «3 реплики» / «5 реплик» /
                    // «3 lines» — без ручной плюрализации в каждом string.Format.
                    string label = ToolLang.IsRU
                        ? "+ ещё " + NovellaPlurals.Lines(remaining) + " · смотри в редакторе диалогов"
                        : "+ " + NovellaPlurals.Lines(remaining) + " more · view in Dialogue Editor";

                    var moreSt = new GUIStyle(EditorStyles.miniLabel) {
                        fontSize = 11, fontStyle = FontStyle.Italic,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = moreHover
                            ? NovellaGraphTheme.Accent
                            : NovellaGraphTheme.Text3 }
                    };
                    GUI.Label(moreR, label, moreSt);
                    EditorGUIUtility.AddCursorRect(moreR, MouseCursor.Link);
                    if (moreHover && Event.current.type == EventType.MouseMove) _window.Repaint();

                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                        && moreHover)
                    {
                        // Открываем редактор на первой «скрытой» реплике.
                        int capIdx = MAX_PREVIEW_LINES;
                        var capDial = dialData;
                        NovellaDialogueEditorWindow.OpenWindow(_currentTree, capDial, _window.PreviewLanguage,
                            (lineIndex) =>
                            {
                                _activePreviewLineIndex = lineIndex;
                                SyncScenePreview(capDial);
                                _window.Repaint();
                            },
                            _onMarkUnsaved,
                            () =>
                            {
                                selectedNodeView.RefreshVisuals();
                                _window.Repaint();
                            });
                        _activePreviewLineIndex = capIdx;
                        SyncScenePreview(capDial);
                        Event.current.Use();
                    }
                }

                // Removal — после цикла, чтобы не сдвинуть индексы.
                if (lineToRemove >= 0 && lineToRemove < dialData.DialogueLines.Count)
                {
                    Undo.RecordObject(_currentTree, "Remove Dialogue Line");
                    dialData.DialogueLines.RemoveAt(lineToRemove);
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                GUILayout.Space(8);

                // ─── Accent CTA — полнофункциональный редактор для глубокой правки ───
                if (NovellaInspectorChrome.DrawAccentBtn(
                    "💬  " + ToolLang.Get("Open Dialogue Editor", "Открыть редактор диалогов"),
                    GUILayout.Height(40)))
                {
                    NovellaDialogueEditorWindow.OpenWindow(_currentTree, dialData, _window.PreviewLanguage, (lineIndex) =>
                    {
                        _activePreviewLineIndex = lineIndex;
                        SyncScenePreview(dialData);
                        _window.Repaint();
                    }, _onMarkUnsaved, () =>
                    {
                        selectedNodeView.RefreshVisuals();
                        _window.Repaint();
                    });
                }

                // Подсказка про подключённую Animation-ноду убрана —
                // у Audio Sync / Scene Sync такого хинта нет, выглядело
                // непоследовательно. Edge'а между нодами достаточно как
                // визуальной индикации «подключено».

                // Кнопка «Открыть Кузницу UI» убрана из инспектора Dialogue —
                // у юзера она открывалась криво, плюс к ноде диалога UI Forge
                // прямого отношения не имеет (диалог — это контент, рамка
                // диалога — это уже задача оформления). Кузницу UI юзер
                // открывает из Tools / Novella Studio когда она реально нужна.

                if (GUI.changed) { _serializedObject.ApplyModifiedProperties(); if (_graphView != null) { _graphView.SyncGraphToData(); _onMarkUnsaved?.Invoke(); } }

                EndLayout(); return;

            }

            // ==========================================
            // === НОДА ГАРДЕРОБА (WARDROBE DLC) ===
            // ==========================================
            if (nodeData is WardrobeNodeData wardrobeData)
            {
                DrawSectionHeader("👗", ToolLang.Get("Wardrobe Settings", "Настройки Гардероба"));
                GUILayout.BeginVertical(EditorStyles.helpBox);

                NovellaInspectorChrome.DrawHint(ToolLang.Get(
                    "This node opens the Wardrobe UI in-game. \n\n• STANDARD MODE: Opens full wardrobe for the player to mix and match.\n• GIFT MODE: Gives the player a choice of specific items (like a lootbox).",
                    "Эта нода открывает интерфейс Гардероба в игре. \n\n• СТАНДАРТНЫЙ РЕЖИМ: Открывает полный гардероб для переодевания.\n• РЕЖИМ ПОДАРКА: Дает игроку выбор из конкретных предметов (как сундук с лутом)."));
                GUILayout.Space(10);

                EditorGUI.BeginChangeCheck();

                wardrobeData.UseMainCharacter = EditorGUILayout.ToggleLeft(" " + ToolLang.Get("Use Main Character (From Menu)", "Использовать Главного Героя (Созданного в меню)"), wardrobeData.UseMainCharacter, EditorStyles.boldLabel);

                if (!wardrobeData.UseMainCharacter)
                {
                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Target Character", "Персонаж"), GUILayout.Width(130));
                    string charName = wardrobeData.TargetCharacter != null ? wardrobeData.TargetCharacter.name : ToolLang.Get("Select...", "Выбрать...");
                    if (GUILayout.Button($"👤 {charName}", EditorStyles.popup))
                    {
                        NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => {
                            Undo.RecordObject(_currentTree, "Change Wardrobe Character");
                            wardrobeData.TargetCharacter = selectedChar;
                            EditorUtility.SetDirty(_currentTree);
                            _onMarkUnsaved?.Invoke();
                            _window.Repaint();
                        });
                    }
                    if (wardrobeData.TargetCharacter != null)
                    {
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.RecordObject(_currentTree, "Clear Character");
                            wardrobeData.TargetCharacter = null;
                            _onMarkUnsaved?.Invoke();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(10);
                wardrobeData.IsGiftMode = EditorGUILayout.ToggleLeft(ToolLang.Get(" Gift Mode (Choose 1 item)", " Режим Подарка (Выбор 1 предмета)"), wardrobeData.IsGiftMode, EditorStyles.boldLabel);

                if (wardrobeData.IsGiftMode)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("🎁 " + ToolLang.Get("Items to Choose From", "Предметы на выбор"), EditorStyles.boldLabel);

                    for (int i = 0; i < wardrobeData.ItemsToChoose.Count; i++)
                    {
                        var item = wardrobeData.ItemsToChoose[i];
                        GUILayout.BeginVertical(EditorStyles.helpBox);

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{i + 1}.", EditorStyles.miniLabel, GUILayout.Width(15));
                        item.ItemName = EditorGUILayout.TextField(item.ItemName, GUILayout.ExpandWidth(true));

                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.RecordObject(_currentTree, "Remove Item");
                            wardrobeData.ItemsToChoose.RemoveAt(i);
                            _onMarkUnsaved?.Invoke();
                            break;
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get("Layer:", "Слой:"), GUILayout.Width(45));
                        string[] allLayers = GetAvailableLayersAcrossProject();
                        int lIdx = System.Array.IndexOf(allLayers, item.TargetLayer);
                        if (lIdx == -1) lIdx = 0;
                        lIdx = EditorGUILayout.Popup(lIdx, allLayers, GUILayout.Width(100));
                        item.TargetLayer = allLayers[lIdx];

                        string sprName = item.ItemSprite != null ? item.ItemSprite.name : ToolLang.Get("Select Sprite...", "Выбрать спрайт...");
                        if (GUILayout.Button("🖼 " + sprName, EditorStyles.popup))
                        {
                            int index = i;
                            NovellaGalleryWindow.ShowWindow(obj => {
                                Undo.RecordObject(_currentTree, "Change Wardrobe Item");
                                wardrobeData.ItemsToChoose[index].ItemSprite = obj as Sprite;
                                if (wardrobeData.ItemsToChoose[index].ItemSprite == null && obj is Texture2D tex)
                                {
                                    string path = AssetDatabase.GetAssetPath(tex);
                                    wardrobeData.ItemsToChoose[index].ItemSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                                }
                                _onMarkUnsaved?.Invoke();
                                _window.Repaint();
                            }, NovellaGalleryWindow.EGalleryFilter.Image);
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                    }

                    if (GUILayout.Button("+ " + ToolLang.Get("Add Item Choice", "Добавить вариант"), EditorStyles.miniButton))
                    {
                        Undo.RecordObject(_currentTree, "Add Wardrobe Item");
                        wardrobeData.ItemsToChoose.Add(new WardrobeItemChoice());
                        _onMarkUnsaved?.Invoke();
                    }
                }
                else
                {
                    GUILayout.Space(5);
                    NovellaInspectorChrome.DrawHint(ToolLang.Get("Standard Mode: Opens the wardrobe screen with all currently unlocked items for the selected character.", "Стандартный режим: Открывает экран гардероба со всеми доступными вещами для выбранного персонажа."));
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_currentTree);
                    selectedNodeView.RefreshVisuals();
                }

                GUILayout.EndVertical();
                EndLayout(); return;
            }

            // ОСТАЛЬНЫЕ DLC
            if (nodeData.NodeType == ENodeType.CustomDLC)
            {
                string dlcName = "DLC Module";
                string dlcDesc = "";
                var attr = DLCCache.GetNodeAttribute(nodeData.GetType());
                if (attr != null)
                {
                    dlcName = attr.MenuName;
                    dlcDesc = attr.Description ?? "";
                }

                DrawSectionHeader("🧩", dlcName);

                // ─── Описание DLC-ноды от автора модуля ───
                // Если DLC-разработчик прописал [NovellaDLCNode(Description="...")]
                // в коде — показываем как вступительную плашку с акцентом.
                // Раньше юзер видел голые поля без объяснения что это вообще за нода.
                if (!string.IsNullOrEmpty(dlcDesc))
                {
                    NovellaInspectorChrome.DrawHint(dlcDesc);
                }
                else
                {
                    NovellaInspectorChrome.DrawHint(ToolLang.Get(
                        "This is a DLC node — its fields come from a third-party module. " +
                        "Hover over each field name to see its description (if the DLC author provided one).",
                        "Это DLC-нода — её поля приходят из стороннего модуля. " +
                        "Наведи мышь на название поля чтобы увидеть подсказку (если автор DLC её написал)."));
                }

                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();

                int nodeIndex = _currentTree.Nodes.FindIndex(n => n != null && n.NodeID == nodeData.NodeID);
                SerializedProperty freshNodeProp = nodeIndex != -1 ? _serializedObject.FindProperty("Nodes").GetArrayElementAtIndex(nodeIndex) : null;

                if (freshNodeProp != null)
                {
                    SerializedProperty iterator = freshNodeProp.Copy();
                    SerializedProperty endProperty = iterator.GetEndProperty();

                    bool enterChildren = true;
                    int fieldCount = 0;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (SerializedProperty.EqualContents(iterator, endProperty)) break;

                        if (iterator.name == "NodeID" || iterator.name == "NodeTitle" ||
                            iterator.name == "GraphPosition" || iterator.name == "NodeCustomColor" ||
                            iterator.name == "IsPinned") continue;

                        // PropertyField сам берёт displayName (auto-nicified) и [Tooltip]-атрибут
                        // от поля — даёт hover-подсказки если DLC-автор их прописал.
                        // Дополнительно подсвечиваем NextNodeID-подобные поля как «выходной порт».
                        bool isPortLike = iterator.name == "NextNodeID" ||
                                          (iterator.name.Length > 6 && iterator.name.EndsWith("NodeID"));
                        if (isPortLike)
                        {
                            // Output-порты: отдельная плашка с pin-иконкой, чтобы юзер
                            // понимал «это куда пойдёт сюжет» а не «настройка внутри ноды».
                            var portSt = new GUIStyle(EditorStyles.miniBoldLabel) {
                                fontSize = 10,
                                normal = { textColor = new Color(0.36f, 0.75f, 0.92f) }
                            };
                            GUILayout.Label("🔗 " + ToolLang.Get("Output port", "Выходной порт"), portSt);
                        }
                        EditorGUILayout.PropertyField(iterator, true);
                        fieldCount++;
                    }

                    if (fieldCount == 0)
                    {
                        EditorGUILayout.LabelField(ToolLang.Get(
                            "(this DLC node has no editable fields)",
                            "(у этой DLC-ноды нет редактируемых полей)"),
                            EditorStyles.centeredGreyMiniLabel);
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    _serializedObject.ApplyModifiedProperties();
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                GUILayout.EndVertical();
                EndLayout(); return;
            }
            EndLayout();
        }

        // Block 4A: единый источник стиля section-header'а — теперь через
        // NovellaInspectorChrome (Hub-style: cyan UPPERCASE 10pt + 1px Border
        // снизу + spacing). Раньше был 14pt boldLabel с разноцветным текстом
        // в зависимости от Pro/Personal-скина.
        private void DrawSectionHeader(string icon, string title)
            => NovellaInspectorChrome.DrawSectionHeader(icon, title);

        private void DrawStartNodeHelp()
        {
            bool hasPlayer = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>() != null;

            if (hasPlayer)
            {
                DrawSectionHeader("🚀", ToolLang.Get("SCENE READY", "СЦЕНА ГОТОВА К РАБОТЕ"));
                NovellaInspectorChrome.DrawHint(ToolLang.Get("NovellaPlayer detected. Scene is fully linked.", "NovellaPlayer найден. Сцена подключена."));
                // Раньше тут была фиолетовая кнопка «Редактор UI» — убрана,
                // т.к. Кузница UI открывалась криво из этого контекста.
                // Юзер открывает её из Tools / Novella Studio.
            }
            else
            {
                DrawSectionHeader("🗂️", ToolLang.Get("DATA MODE", "РЕЖИМ ДАННЫХ"));

                GUIStyle warningStyle = new GUIStyle(EditorStyles.helpBox);
                warningStyle.fontSize = 12; warningStyle.richText = true;

                EditorGUILayout.LabelField(ToolLang.Get(
                    "⚠️ <b>NovellaPlayer</b> is missing.\n\nUse the <b>Scene Manager</b> to generate the necessary UI objects for Gameplay or Main Menu.",
                    "⚠️ <b>NovellaPlayer</b> не найден.\n\nОткройте <b>Менеджер Сцен</b>, чтобы сгенерировать правильный UI для Игры или Главного меню."
                ), warningStyle);

                GUILayout.Space(15);
                GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
                if (GUILayout.Button("🛠 " + ToolLang.Get("Open Scene Manager", "Открыть Менеджер Сцен"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(40)))
                {
                    NovellaSceneManagerModule.ShowWindow();
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(20);
            DrawSectionHeader("💾", ToolLang.Get("AUTO-SAVE", "АВТОСОХРАНЕНИЕ"));
            GUILayout.BeginVertical(EditorStyles.helpBox);

            NovellaInspectorChrome.DrawHint(ToolLang.Get(
                "Auto-Save silently records the player's progress in the background every X seconds. When they click 'Continue' in the Main Menu, the story resumes from the last saved node.",
                "Автосохранение незаметно записывает прогресс игрока каждые X секунд. При нажатии на карточку истории в Главном меню игра продолжится с этого места."));

            EditorGUI.BeginChangeCheck();
            _currentTree.EnableAutoSave = EditorGUILayout.ToggleLeft(ToolLang.Get(" Enable Auto-Save (Timer)", " Включить автосохранение по таймеру"), _currentTree.EnableAutoSave, EditorStyles.boldLabel);

            if (_currentTree.EnableAutoSave)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Interval (sec):", "Интервал (сек):"), GUILayout.Width(100));
                _currentTree.AutoSaveInterval = EditorGUILayout.FloatField(_currentTree.AutoSaveInterval, GUILayout.Width(60));
                GUILayout.EndHorizontal();

                if (_currentTree.AutoSaveInterval < 5f) _currentTree.AutoSaveInterval = 5f;

                if (_currentTree.AutoSaveInterval < 10f)
                {
                    GUILayout.Space(5);
                    NovellaInspectorChrome.DrawWarn(ToolLang.Get(
                        "An interval below 10 seconds may cause micro-stutters on mobile devices due to frequent file writing. 15-30 seconds is recommended.",
                        "Интервал менее 10 секунд может вызывать микро-фризы на слабых мобильных устройствах из-за частой записи в память. Рекомендуется 15-30 секунд."), true);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_currentTree);
                _onMarkUnsaved?.Invoke();
            }
            GUILayout.EndVertical();
        }

        private void EndLayout() { GUILayout.EndVertical(); GUILayout.Space(20); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); }

        private float GetCharXOffset(ECharacterPosition preset, float customX)
        {
            if (preset == ECharacterPosition.Left) return -3.5f;
            if (preset == ECharacterPosition.Right) return 3.5f;
            if (preset == ECharacterPosition.FarLeft) return -6.5f;
            if (preset == ECharacterPosition.FarRight) return 6.5f;
            if (preset == ECharacterPosition.Custom) return customX;
            return 0f;
        }

        private Vector2? _originalPanelPos = null;
        private Vector3? _originalPanelScale = null;

        private void SyncScenePreview(NovellaNodeBase nodeData)
        {
            if (!(nodeData is DialogueNodeData) && !(nodeData is WaitNodeData) && !(nodeData is SceneSettingsNodeData) && !(nodeData is AnimationNodeData) && !(nodeData is EventBroadcastNodeData))
            {
                ClearScenePreview();
                return;
            }

            if (_lastSyncedNode != nodeData)
            {
                _lastSyncedNode = nodeData;
                _originalPanelPos = null;
                _originalPanelScale = null;
            }

            var sceneManagerPlayer = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
            if (sceneManagerPlayer == null) return;

            Transform charsContainer = sceneManagerPlayer.CharactersContainer;
            RectTransform dialoguePanelRect = sceneManagerPlayer.DialoguePanel != null ? sceneManagerPlayer.DialoguePanel.GetComponent<RectTransform>() : null;
            Transform canvasTransform = dialoguePanelRect != null ? dialoguePanelRect.parent : null;

            if (canvasTransform != null)
            {
                var oldPreview = canvasTransform.Find("TempWaitIndicator_Preview");
                if (oldPreview != null) Undo.DestroyObjectImmediate(oldPreview.gameObject);
            }

            if (nodeData is WaitNodeData waitData)
            {
                if (dialoguePanelRect != null)
                {
                    Undo.RecordObject(dialoguePanelRect.gameObject, "Toggle Wait Frame");
                    dialoguePanelRect.gameObject.SetActive(!waitData.WaitHideFrame);
                }

                if (canvasTransform != null)
                {
                    GameObject go = new GameObject("TempWaitIndicator_Preview");
                    go.transform.SetParent(canvasTransform, false);
                    Undo.RegisterCreatedObjectUndo(go, "Wait Preview");

                    var rt = go.AddComponent<RectTransform>();

                    if (waitData.WaitIndicatorPreset == EFramePosition.Top) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); }
                    else if (waitData.WaitIndicatorPreset == EFramePosition.Center) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); }
                    else if (waitData.WaitIndicatorPreset == EFramePosition.Bottom || waitData.WaitIndicatorPreset == EFramePosition.Default) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); }
                    else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); }

                    rt.anchoredPosition = new Vector2(waitData.WaitIndicatorPosX, waitData.WaitIndicatorPosY);
                    rt.sizeDelta = new Vector2(waitData.WaitIndicatorSize, waitData.WaitIndicatorSize);

                    if (waitData.WaitIndicatorSprite == null) rt.localRotation = Quaternion.Euler(0, 0, 45);

                    var img = go.AddComponent<UnityEngine.UI.Image>();
                    img.sprite = waitData.WaitIndicatorSprite;
                    img.color = waitData.WaitIndicatorColor;

                    if (!string.IsNullOrEmpty(waitData.WaitText))
                    {
                        GameObject txtGo = new GameObject("Text");
                        txtGo.transform.SetParent(go.transform, false);
                        var trt = txtGo.AddComponent<RectTransform>();
                        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                        if (waitData.WaitIndicatorSprite == null) trt.localRotation = Quaternion.Euler(0, 0, -45);
                        trt.anchoredPosition = new Vector2(waitData.WaitTextPosX, waitData.WaitTextPosY);
                        trt.sizeDelta = new Vector2(500, 100);

                        var txt = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
                        txt.text = waitData.WaitText;
                        txt.color = waitData.WaitTextColor;
                        txt.fontSize = waitData.WaitTextSize;
                        txt.alignment = TMPro.TextAlignmentOptions.Center;
                    }
                }

                if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                return;
            }

            if (nodeData is SceneSettingsNodeData || nodeData is AnimationNodeData || nodeData is EventBroadcastNodeData)
            {
                if (dialoguePanelRect != null)
                {
                    Undo.RecordObject(dialoguePanelRect.gameObject, "Hide UI For Node");
                    dialoguePanelRect.gameObject.SetActive(false);
                }
                if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                return;
            }

            if (dialoguePanelRect != null)
            {
                Undo.RecordObject(dialoguePanelRect.gameObject, "Restore UI Panel");
                dialoguePanelRect.gameObject.SetActive(true);
            }

            List<NovellaSceneEntity> entities = charsContainer != null ? charsContainer.GetComponentsInChildren<NovellaSceneEntity>(true).ToList() : new List<NovellaSceneEntity>();

            string currentSpeakerID = "";
            DialogueLine activeLine = null;

            Dictionary<string, CharacterInDialogue> charConfigs = new Dictionary<string, CharacterInDialogue>();

            if (nodeData is DialogueNodeData dialDataForPreview)
            {
                if (dialDataForPreview.DialogueLines.Count > 0 && _activePreviewLineIndex >= 0 && _activePreviewLineIndex < dialDataForPreview.DialogueLines.Count)
                {
                    activeLine = dialDataForPreview.DialogueLines[_activePreviewLineIndex];
                    if (activeLine.Speaker != null) currentSpeakerID = activeLine.Speaker.CharacterID;

                    if (sceneManagerPlayer != null)
                    {
                        if (sceneManagerPlayer.SpeakerNameText != null)
                        {
                            Undo.RecordObject(sceneManagerPlayer.SpeakerNameText, "Preview Speaker Name");
                            string dName = activeLine.Speaker != null ? activeLine.Speaker.name : "";
                            if (activeLine.HideSpeakerName && !string.IsNullOrEmpty(activeLine.CustomName)) dName = activeLine.CustomName;
                            sceneManagerPlayer.SpeakerNameText.text = dName;
                        }

                        if (sceneManagerPlayer.DialogueBodyText != null)
                        {
                            Undo.RecordObject(sceneManagerPlayer.DialogueBodyText, "Preview Body Text");
                            sceneManagerPlayer.DialogueBodyText.text = activeLine.LocalizedPhrase.GetText(_window.PreviewLanguage);
                        }
                    }

                    if (dialoguePanelRect != null)
                    {
                        if (_originalPanelPos == null)
                        {
                            _originalPanelPos = dialoguePanelRect.anchoredPosition;
                            _originalPanelScale = dialoguePanelRect.localScale;
                        }

                        Undo.RecordObject(dialoguePanelRect, "Preview UI Offset");
                        if (activeLine.CustomizeFrameLayout)
                        {
                            float targetY = 0f;
                            float targetX = 0f;

                            if (activeLine.FramePositionPreset == EFramePosition.Top) targetY = 600f;
                            else if (activeLine.FramePositionPreset == EFramePosition.Center) targetY = 300f;

                            targetX += activeLine.FramePosX;
                            targetY += activeLine.FramePosY;

                            dialoguePanelRect.anchoredPosition = new Vector2(targetX, targetY);
                            dialoguePanelRect.localScale = Vector3.one * activeLine.FrameScale;
                        }
                        else
                        {
                            dialoguePanelRect.anchoredPosition = Vector2.zero;
                            dialoguePanelRect.localScale = Vector3.one;
                        }
                    }
                }

                foreach (var ac in dialDataForPreview.ActiveCharacters) if (ac.CharacterAsset != null) charConfigs[ac.CharacterAsset.CharacterID] = ac;

                foreach (var line in dialDataForPreview.DialogueLines)
                {
                    if (line.Speaker != null && !charConfigs.ContainsKey(line.Speaker.CharacterID))
                    {
                        charConfigs[line.Speaker.CharacterID] = new CharacterInDialogue { CharacterAsset = line.Speaker, Plane = ECharacterPlane.Speaker, Scale = 1f, Emotion = "Default", PosX = 0f, PosY = 0f, PositionPreset = ECharacterPosition.Center };
                    }
                }
            }

            foreach (var config in charConfigs.Values)
            {
                var entity = entities.FirstOrDefault(e => e.LinkedNodeID == config.CharacterAsset.CharacterID);
                if (entity == null)
                {
                    GameObject go = new GameObject("Char_" + config.CharacterAsset.name);
                    if (charsContainer != null) go.transform.SetParent(charsContainer, false);

                    entity = go.AddComponent<NovellaSceneEntity>();
                    entity.Initialize(config.CharacterAsset.CharacterID);
                    Undo.RegisterCreatedObjectUndo(go, "Spawn Character");
                    entities.Add(entity);
                }

                string emotionToSet = config.Emotion;
                float baseX = GetCharXOffset(config.PositionPreset, config.PosX);

                int targetPlane = (int)config.Plane;
                float targetScale = config.Scale;
                Vector3 targetPos = new Vector3(baseX, config.PosY, 0);
                bool targetFlipX = config.FlipX;
                bool targetFlipY = config.FlipY;

                if (activeLine != null && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID)
                {
                    emotionToSet = activeLine.Mood;
                    targetFlipX = activeLine.FlipX;
                    targetFlipY = activeLine.FlipY;

                    if (activeLine.CustomizeSpeakerLayout)
                    {
                        targetPlane = (int)activeLine.SpeakerPlane;
                        targetScale = config.Scale * activeLine.SpeakerScale;
                        targetPos = new Vector3(baseX + activeLine.SpeakerPosX, config.PosY + activeLine.SpeakerPosY, 0);
                    }
                    else
                    {
                        targetPlane = (int)ECharacterPlane.Speaker;
                    }
                }

                entity.transform.localScale = Vector3.one * targetScale;
                entity.transform.localPosition = targetPos;

                entity.ApplyAppearance(config.CharacterAsset, emotionToSet);
                entity.SetSortingOrder(targetPlane);
                entity.SetFlip(targetFlipX, targetFlipY);

                bool shouldHide = false;
                if (activeLine != null && activeLine.HideSpeakerSprite && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID) shouldHide = true;
                entity.gameObject.SetActive(!shouldHide);
            }

            bool sceneChanged = false;
            foreach (var entity in entities)
            {
                if (!charConfigs.ContainsKey(entity.LinkedNodeID)) { Undo.DestroyObjectImmediate(entity.gameObject); sceneChanged = true; }
            }
            if (sceneChanged && !Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private void ClearScenePreview()
        {
            var entities = UnityEngine.Object.FindObjectsByType<NovellaSceneEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool sceneChanged = false;
            foreach (var entity in entities)
            {
                Undo.DestroyObjectImmediate(entity.gameObject);
                sceneChanged = true;
            }

            var player = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
            if (player != null && player.DialoguePanel != null)
            {
                var oldPreview = player.DialoguePanel.transform.parent.Find("TempWaitIndicator_Preview");
                if (oldPreview != null)
                {
                    Undo.DestroyObjectImmediate(oldPreview.gameObject);
                    sceneChanged = true;
                }
            }

            if (sceneChanged && !Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}