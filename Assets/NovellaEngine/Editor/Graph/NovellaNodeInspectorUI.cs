/// <summary>
/// ОТВЕЧАЕТ ЗА:
/// 1. Отрисовку всех интерфейсов в правой панели (Инспекторе) при клике на любую ноду.
/// 2. Live-превью сцены: синхронизирует изменения в инспекторе с реальной игровой сценой.
/// 3. Автоматическую генерацию интерфейса для Custom DLC нод.
/// </summary>
using NovellaEngine.Data;
using NovellaEngine.Runtime;
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

        public NovellaNodeInspectorUI(NovellaTree tree, NovellaGraphView graphView, Action onMarkUnsaved, NovellaGraphWindow window)
        {
            _currentTree = tree; _graphView = graphView; _onMarkUnsaved = onMarkUnsaved; _window = window;
            if (_currentTree != null) _serializedObject = new SerializedObject(_currentTree);
        }

        public void SetGraphView(NovellaGraphView gv) => _graphView = gv;

        private string DrawVariableDropdown(string currentVar, params GUILayoutOption[] options)
        {
            GUILayout.BeginHorizontal();

            var settings = NovellaVariableSettings.Instance;
            if (settings.Variables.Count == 0)
            {
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button(ToolLang.Get("Create Var!", "Создать!"), EditorStyles.popup, options))
                {
                    NovellaVariableEditorWindow.ShowWindow();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                return currentVar;
            }

            var realNames = settings.Variables.Select(v => v.Name).ToArray();
            var displayNames = settings.Variables.Select(v => (v.Type == EVarType.Integer && v.IsPremiumCurrency ? "💎 " : "") + v.Name).ToArray();

            int idx = Array.IndexOf(realNames, currentVar);
            if (idx == -1) idx = 0;

            int newIdx = EditorGUILayout.Popup(idx, displayNames, options);

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(25), GUILayout.Height(18)))
            {
                NovellaVariableEditorWindow.ShowWindow(realNames[newIdx]);
            }

            GUILayout.EndHorizontal();
            return realNames[newIdx];
        }

        private EVarType GetVarType(string varName)
        {
            var settings = NovellaVariableSettings.Instance;
            var def = settings.Variables.FirstOrDefault(v => v.Name == varName);
            return def != null ? def.Type : EVarType.Integer;
        }

        private void DrawTypeHint(EVarType type)
        {
            string hint = type switch
            {
                EVarType.Integer => ToolLang.Get("Type: Number (Integer)", "Тип: Число (Целое)"),
                EVarType.Boolean => ToolLang.Get("Type: Boolean (True/False)", "Тип: Переключатель (Да/Нет)"),
                EVarType.String => ToolLang.Get("Type: String (Text)", "Тип: Строка (Текст)"),
                _ => ""
            };
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(hint, EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndHorizontal();
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
                    EditorGUILayout.HelpBox(ToolLang.Get("Characters use custom scene positions (posX, posY) from Dialogue Node. This tween overrides it.", "Персонажи используют позы из ноды Диалога. Этот твин временно перекроет их."), MessageType.Info);
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

            // === ФИКС 3: ПРИНУДИТЕЛЬНОЕ ОБНОВЛЕНИЕ ПАМЯТИ ДО РАБОТЫ ===
            _serializedObject.Update();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            GUILayout.BeginHorizontal(); GUILayout.Space(15); GUILayout.BeginVertical(); GUILayout.Space(15);

            if (isStartNodeSelected)
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScenePreview(); }
                DrawStartNodeHelp(); EndLayout(); return;
            }

            DrawSectionHeader("📝", ToolLang.Get("NODE INSPECTOR", "ИНСПЕКТОР НОДЫ"));

            if (selectedNodeView == null || selectedNodeView.Data == null)
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScenePreview(); }
                EditorGUILayout.HelpBox(ToolLang.Get("Select a Node.", "Выберите Ноду."), MessageType.Info); EndLayout(); return;
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

            GUILayout.Label(ToolLang.Get("General Data", "Основные данные"), EditorStyles.boldLabel);
            EditorGUIUtility.labelWidth = 120;
            EditorGUI.BeginDisabledGroup(true); EditorGUILayout.TextField(ToolLang.Get("Internal ID", "Внутренний ID"), nodeData.NodeID); EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            string newNodeTitle = EditorGUILayout.TextField(ToolLang.Get("Node Name", "Имя Ноды"), nodeData.NodeTitle);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change Node Name"); nodeData.NodeTitle = newNodeTitle; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

            GUILayout.BeginHorizontal();
            if (nodeData.NodeType == ENodeType.Dialogue || nodeData.NodeType == ENodeType.Event || nodeData.NodeType == ENodeType.Note)
            {
                EditorGUI.BeginChangeCheck(); Color newColor = EditorGUILayout.ColorField(GUIContent.none, nodeData.NodeCustomColor, false, true, false, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Color"); nodeData.NodeCustomColor = newColor; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
            }
            else GUILayout.Label(ToolLang.Get("🎨 Color is locked (Global Settings)", "🎨 Цвет зарезервирован (Общие настройки)"), EditorStyles.centeredGreyMiniLabel);

            EditorGUI.BeginChangeCheck(); bool isPinnedRequest = GUILayout.Toggle(nodeData.IsPinned, $"📌 {ToolLang.Get("Pin Node", "Закрепить")}", EditorStyles.miniButton, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Pin"); foreach (var n in _currentTree.Nodes) if (n != null) n.IsPinned = false; nodeData.IsPinned = isPinnedRequest; EditorApplication.delayCall += () => _graphView?.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals()); _onMarkUnsaved?.Invoke(); }
            GUILayout.EndHorizontal();

            // ==========================================
            // === НОДА ОЖИДАНИЯ (WAIT) ===
            // ==========================================
            if (nodeData is WaitNodeData waitData)
            {
                DrawSectionHeader("⏳", ToolLang.Get("Wait Settings", "Настройки Ожидания"));
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
            // === НОДА ЗАМЕТКИ (NOTE) ===
            // ==========================================
            if (nodeData is NoteNodeData noteData)
            {
                DrawSectionHeader("📌", ToolLang.Get("Note Settings", "Настройки Заметки"));

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
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "💡 Tip: Use 'Background' preset to place image under text. Use offsets to adjust spacing.",
                    "💡 Подсказка: Пресет 'Background' кладет картинку под текст как водяной знак. Используйте 'Смещение' для точной подгонки."
                ), MessageType.Info);

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
                        EditorGUILayout.HelpBox(ToolLang.Get("Add scenes to File -> Build Settings first!", "Сначала добавьте сцены в File -> Build Settings!"), MessageType.Warning);

                        if (GUILayout.Button("🛠 " + ToolLang.Get("Open Scene Manager", "Открыть Менеджер Сцен"), EditorStyles.miniButton, GUILayout.Height(25)))
                        {
                            NovellaSceneManagerWindow.ShowWindow();
                        }
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();

                        int selectedIndex = Mathf.Max(0, sceneNames.IndexOf(endData.TargetSceneName));
                        selectedIndex = EditorGUILayout.Popup(ToolLang.Get("Target Scene:", "Целевая сцена:"), selectedIndex, sceneNames.ToArray());
                        endData.TargetSceneName = sceneNames[selectedIndex];

                        if (GUILayout.Button(new GUIContent("🛠", ToolLang.Get("Open Scene Manager", "Открыть Менеджер Сцен")), EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            NovellaSceneManagerWindow.ShowWindow();
                        }

                        GUILayout.EndHorizontal();
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
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.AudioSyncNodeID == audData.NodeID);
                audData.SyncWithDialogue = (syncedDialogue != null);

                if (!audData.SyncWithDialogue)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Standard sequential mode. Connect to Dialogue 'Audio Sync' port for advanced features.", "Обычный режим. Подключите к порту 'Audio Sync' Диалога для продвинутых фишек."), MessageType.Info);

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    audData.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), audData.AudioChannel);

                    string channelInfo = "";
                    if (audData.AudioChannel == EAudioChannel.BGM) channelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                    else if (audData.AudioChannel == EAudioChannel.SFX) channelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                    else if (audData.AudioChannel == EAudioChannel.Voice) channelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                    EditorGUILayout.HelpBox(channelInfo, MessageType.None);
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
                        EditorGUILayout.HelpBox(evChannelInfo, MessageType.None);
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
                    v.VariableName = DrawVariableDropdown(v.VariableName, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();

                    EVarType type = GetVarType(v.VariableName);

                    GUILayout.BeginHorizontal();
                    if (type == EVarType.Integer)
                    {
                        string[] opNames = { ToolLang.Get("Set (=)", "Установить (=)"), ToolLang.Get("Add (+)", "Добавить (+)") };
                        v.VarOperation = (EVarOperation)EditorGUILayout.Popup((int)v.VarOperation, opNames, GUILayout.Width(100));
                        v.VarValue = EditorGUILayout.IntField(v.VarValue, GUILayout.ExpandWidth(true));
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
                        v.VarString = EditorGUILayout.TextField(v.VarString, GUILayout.ExpandWidth(true));
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

                if (condData.Conditions.Count == 0) condData.Conditions.Add(new ChoiceCondition());

                for (int c = 0; c < condData.Conditions.Count; c++)
                {
                    var cond = condData.Conditions[c];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();

                    GUILayout.Label(ToolLang.Get("IF", "ЕСЛИ"), EditorStyles.boldLabel, GUILayout.Width(40));
                    cond.Variable = DrawVariableDropdown(cond.Variable, GUILayout.ExpandWidth(true));

                    EVarType type = GetVarType(cond.Variable);

                    if (type == EVarType.Integer)
                    {
                        string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                        cond.Operator = (EConditionOperator)EditorGUILayout.Popup((int)cond.Operator, opNames, GUILayout.Width(50));
                        cond.Value = EditorGUILayout.IntField(cond.Value, GUILayout.Width(50));
                    }
                    else if (type == EVarType.Boolean)
                    {
                        string[] eqNames = { "==", "!=" };
                        EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                        int idx = Array.IndexOf(eqOps, cond.Operator);
                        if (idx == -1) idx = 0;
                        cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(50))];
                        cond.ValueBool = EditorGUILayout.Toggle(cond.ValueBool, GUILayout.Width(50));
                    }
                    else if (type == EVarType.String)
                    {
                        string[] eqNames = { "==", "!=" };
                        EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                        int idx = Array.IndexOf(eqOps, cond.Operator);
                        if (idx == -1) idx = 0;
                        cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(50))];
                        cond.ValueString = EditorGUILayout.TextField(cond.ValueString, GUILayout.Width(80));
                    }

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
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "If ALL conditions above are met, the story goes to the 'True' port. Otherwise, it goes to 'False'.",
                    "Если ВСЕ условия выше выполняются, сюжет пойдет по ветке 'Истина' (True). Иначе — по ветке 'Ложь' (False)."
                ), MessageType.Info);

                EndLayout(); return;
            }

            // ==========================================
            // === НОДА РАНДОМА (RANDOM) ===
            // ==========================================
            if (nodeData is RandomNodeData rndData)
            {
                DrawSectionHeader("🎲", ToolLang.Get("Random Chances", "Случайные события (Шанс)"));

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

                        mod.Variable = DrawVariableDropdown(mod.Variable, GUILayout.ExpandWidth(true), GUILayout.MinWidth(80));

                        EVarType type = GetVarType(mod.Variable);

                        if (type == EVarType.Integer)
                        {
                            string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                            mod.Operator = (EConditionOperator)EditorGUILayout.Popup((int)mod.Operator, opNames, GUILayout.Width(45));
                            mod.Value = EditorGUILayout.IntField(mod.Value, GUILayout.Width(35));
                        }
                        else if (type == EVarType.Boolean)
                        {
                            string[] eqNames = { "==", "!=" };
                            EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                            int idx = Array.IndexOf(eqOps, mod.Operator);
                            if (idx == -1) idx = 0;
                            mod.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(45))];
                            mod.ValueBool = EditorGUILayout.Toggle(mod.ValueBool, GUILayout.Width(35));
                        }
                        else if (type == EVarType.String)
                        {
                            string[] eqNames = { "==", "!=" };
                            EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                            int idx = Array.IndexOf(eqOps, mod.Operator);
                            if (idx == -1) idx = 0;
                            mod.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(45))];
                            mod.ValueString = EditorGUILayout.TextField(mod.ValueString, GUILayout.Width(50));
                        }

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
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "💡 How it works:\n• Base Weight: Default probability.\n• Modifiers: Add EXTRA weight if true.\n• Chance = (Weight / Total Weight) * 100%\n\n% shown above calculates the MAX chance if all conditions apply.",
                    "💡 Как это работает:\n• Базовый Вес: Стандартная доля вероятности.\n• Модификаторы: Дают ЭКСТРА вес, если условие истинно.\n• Шанс = (Вес / Сумма всех весов) * 100%\n\nПроценты (%) выше показывают МАКСИМАЛЬНЫЙ шанс, если все условия сработают."
                ), MessageType.Info);

                if (rndData.Choices.Count >= 4 && !rndData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выхода)."), MessageType.Info);
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
                    string newText = EditorGUILayout.TextField("Text", currentText);

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
                            cond.Variable = DrawVariableDropdown(cond.Variable, GUILayout.ExpandWidth(true));

                            EVarType type = GetVarType(cond.Variable);

                            if (type == EVarType.Integer)
                            {
                                string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                                cond.Operator = (EConditionOperator)EditorGUILayout.Popup((int)cond.Operator, opNames, GUILayout.Width(50));
                                cond.Value = EditorGUILayout.IntField(cond.Value, GUILayout.Width(50));
                            }
                            else if (type == EVarType.Boolean)
                            {
                                string[] eqNames = { "==", "!=" };
                                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                                int idx = Array.IndexOf(eqOps, cond.Operator);
                                if (idx == -1) idx = 0;
                                cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(50))];
                                cond.ValueBool = EditorGUILayout.Toggle(cond.ValueBool, GUILayout.Width(50));
                            }
                            else if (type == EVarType.String)
                            {
                                string[] eqNames = { "==", "!=" };
                                EConditionOperator[] eqOps = { EConditionOperator.Equal, EConditionOperator.NotEqual };
                                int idx = Array.IndexOf(eqOps, cond.Operator);
                                if (idx == -1) idx = 0;
                                cond.Operator = eqOps[EditorGUILayout.Popup(idx, eqNames, GUILayout.Width(50))];
                                cond.ValueString = EditorGUILayout.TextField(cond.ValueString, GUILayout.Width(80));
                            }

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

                    GUILayout.Space(5); GUILayout.EndVertical(); GUILayout.Space(5);
                }

                GUILayout.Space(10);
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "💡 Info: If a choice has multiple conditions, ALL of them must be met (AND logic).",
                    "💡 Инфо: Если у выбора несколько условий, они все должны быть выполнены (логика И)."
                ), MessageType.Info);

                if (branchData.Choices.Count >= 4 && !branchData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выбора)."), MessageType.Info);
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
                EditorGUIUtility.labelWidth = 130;
                EditorGUI.BeginChangeCheck();

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.SceneSyncNodeID == sceneData.NodeID);
                sceneData.SyncWithDialogue = (syncedDialogue != null);

                if (!sceneData.SyncWithDialogue)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "Standalone mode. Changes the visual backdrop before moving to the next node.",
                        "Одиночный режим. Меняет фон перед переходом к следующей ноде."
                    ), MessageType.Info);
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
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "Synced with Dialogue. Tie scene changes (backgrounds, hiding characters) to specific lines.",
                        "Синхронизировано с Диалогом. Привяжите изменения сцены к конкретным репликам."
                    ), MessageType.Info);
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
                            ToolLang.Get("Show Character", "Показать персонажа")
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

                            string[] transNames = { ToolLang.Get("None", "Нет"), ToolLang.Get("Fade", "Растворение"), ToolLang.Get("Slide Left", "Сдвиг Влево"), ToolLang.Get("Slide Right", "Сдвиг Вправо"), ToolLang.Get("Flash White", "Вспышка (Белая)"), ToolLang.Get("Flash Black", "Вспышка (Черная)") };
                            ev.BgTransition = (EBgTransition)EditorGUILayout.Popup(ToolLang.Get("Transition", "Переход"), (int)ev.BgTransition, transNames);
                            if (ev.BgTransition != EBgTransition.None) ev.BgTransitionTime = Mathf.Max(0.1f, EditorGUILayout.FloatField(ToolLang.Get("Duration", "Время"), ev.BgTransitionTime));
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
                        EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 events reached.", "Достигнут базовый лимит (4 события)."), MessageType.Warning);
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

                DialogueNodeData syncedDialogue = _currentTree.Nodes.OfType<DialogueNodeData>().FirstOrDefault(n => n.AnimSyncNodeID == animData.NodeID);
                animData.SyncWithDialogue = (syncedDialogue != null);

                if (animData.SyncWithDialogue)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Synced with Dialogue. Tie animations to specific lines.", "Синхронизировано с Диалогом. Привяжите анимации к конкретным репликам."), MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Standalone mode. Animations will play sequentially or by time delay.", "Обычный режим. Анимации проиграются по очереди или по задержке времени."), MessageType.Info);
                }

                if (animData.AnimEvents.Count == 0)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("No animations added.", "Нет добавленных анимаций."), MessageType.Info);
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
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 animations reached.", "Достигнут базовый лимит (4 анимации)."), MessageType.Warning);
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
                DrawSectionHeader("👥", ToolLang.Get("Scene Layout", "Массовка (Scene Layout)"));
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "Info: Characters added here will appear in the scene. You MUST add characters here to use them as speakers in Dialogue.",
                    "Инфо: Добавленные сюда персонажи появятся на сцене. Спикеров для реплик можно выбирать ТОЛЬКО из добавленной массовки."
                ), MessageType.Info);

                if (GUILayout.Button($"🛠 {ToolLang.Get("Character Editor", "Редактор Персонажей")}", EditorStyles.miniButton, GUILayout.Height(25))) NovellaCharacterEditor.OpenWindow();
                GUILayout.Space(15);

                for (int i = 0; i < dialData.ActiveCharacters.Count; i++)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    var activeChar = dialData.ActiveCharacters[i];
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);

                    activeChar.IsExpanded = GUILayout.Toggle(activeChar.IsExpanded, activeChar.IsExpanded ? "▼" : "▶", EditorStyles.toolbarButton, GUILayout.Width(25));

                    string charName = activeChar.CharacterAsset != null ? activeChar.CharacterAsset.name : ToolLang.Get("Select...", "Выбрать...");
                    if (GUILayout.Button($"👤 {charName}", EditorStyles.toolbarDropDown, GUILayout.ExpandWidth(true)))
                    {
                        NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => {
                            Undo.RecordObject(_currentTree, "Change Character Asset");
                            activeChar.CharacterAsset = selectedChar;
                            GUI.changed = true;
                        });
                    }

                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(30)))
                    {
                        Undo.RecordObject(_currentTree, "Remove Character");
                        dialData.ActiveCharacters.RemoveAt(i);
                        GUI.changed = true;
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white; GUILayout.EndHorizontal();

                    if (activeChar.IsExpanded && activeChar.CharacterAsset != null)
                    {
                        GUILayout.Space(5); EditorGUI.BeginChangeCheck(); GUILayout.BeginHorizontal();

                        string[] planeNames = { "BackSlot1", "BackSlot2", "BackSlot3" };
                        int[] planeValues = { 0, 1, 2 };
                        activeChar.Plane = (ECharacterPlane)EditorGUILayout.IntPopup(ToolLang.Get("Plane:", "План:"), (int)activeChar.Plane, planeNames, planeValues);

                        GUILayout.Space(10);
                        List<string> emotions = new List<string> { "Default" }; if (activeChar.CharacterAsset.Emotions != null) emotions.AddRange(activeChar.CharacterAsset.Emotions.Select(e => e.EmotionName));
                        int emIndex = emotions.IndexOf(activeChar.Emotion); if (emIndex == -1) emIndex = 0;
                        emIndex = EditorGUILayout.Popup(ToolLang.Get("Emotion:", "Эмоция:"), emIndex, emotions.ToArray()); activeChar.Emotion = emotions[emIndex];
                        GUILayout.EndHorizontal(); GUILayout.Space(2);

                        if (activeChar.Scale <= 0f) activeChar.Scale = 1f;
                        activeChar.Scale = EditorGUILayout.Slider(ToolLang.Get("Scale:", "Масштаб:"), activeChar.Scale, 0.1f, 3f); GUILayout.Space(2);

                        GUILayout.BeginHorizontal();
                        activeChar.PositionPreset = (ECharacterPosition)EditorGUILayout.EnumPopup(ToolLang.Get("Pos Preset:", "Позиция:"), activeChar.PositionPreset, GUILayout.Width(180));

                        if (activeChar.PositionPreset == ECharacterPosition.Custom)
                        {
                            GUILayout.Space(10);
                            activeChar.PosX = EditorGUILayout.FloatField("X", activeChar.PosX);
                            GUILayout.Space(10);
                            activeChar.PosY = EditorGUILayout.FloatField("Y", activeChar.PosY);
                        }
                        GUILayout.EndHorizontal();

                        if (EditorGUI.EndChangeCheck()) Undo.RecordObject(_currentTree, "Change Data");
                    }
                    EditorGUIUtility.labelWidth = originalLabelWidth; GUILayout.EndVertical(); GUILayout.Space(5);
                }

                if (GUILayout.Button($"+ {ToolLang.Get("Add Character to Scene", "Добавить персонажа на сцену")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Character"); dialData.ActiveCharacters.Add(new CharacterInDialogue()); GUI.changed = true; }

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

                DrawSectionHeader("💬", $"{ToolLang.Get("Dialogue Lines", "Реплики (Диалоги)")} ({_window.PreviewLanguage})");

                EditorGUILayout.HelpBox(ToolLang.Get(
                    $"This node contains {dialData.DialogueLines.Count} dialogue lines. Open the dedicated Dialogue Editor window to manage speakers, text, timing, and custom UI frames.",
                    $"В этой ноде содержится реплик: {dialData.DialogueLines.Count}. Откройте специализированное окно редактора диалогов для работы с текстом и спикерами."
                ), MessageType.None);

                GUILayout.Space(10);

                GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(new GUIContent("💬 " + ToolLang.Get("OPEN DIALOGUE EDITOR", "ОТКРЫТЬ РЕДАКТОР ДИАЛОГОВ")), new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(45)))
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
                GUI.backgroundColor = Color.white;

                if (!string.IsNullOrEmpty(dialData.AnimSyncNodeID))
                {
                    GUILayout.Space(10);
                    EditorGUILayout.HelpBox(ToolLang.Get("✨ Animation Node is synced. Select the Animation Node to configure events.", "✨ Подключена нода Анимаций. Выделите её, чтобы настроить эффекты для этих реплик."), MessageType.Info);
                }

                GUILayout.Space(15);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("🎨 " + ToolLang.Get("Open UI Editor (Base Frame)", "Открыть редактор UI (Базовая рамка)"), EditorStyles.miniButton, GUILayout.Height(25), GUILayout.Width(250)))
                    NovellaUIEditorWindow.ShowWindow();

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (GUI.changed) { _serializedObject.ApplyModifiedProperties(); if (_graphView != null) { _graphView.SyncGraphToData(); _onMarkUnsaved?.Invoke(); } }

                EndLayout(); return;

            }

            // ==========================================
            // === КАСТОМНЫЕ НОДЫ ИЗ DLC (АВТО-ОТРИСОВКА) ===
            // ==========================================
            if (nodeData.NodeType == ENodeType.CustomDLC)
            {
                string dlcName = "DLC Module";
                var attr = DLCCache.GetNodeAttribute(nodeData.GetType());
                if (attr != null) dlcName = attr.MenuName;

                DrawSectionHeader("🧩", dlcName);
                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();

                if (_cachedNodeProp != null)
                {
                    SerializedProperty iterator = _cachedNodeProp.Copy();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.name == "NodeID" || iterator.name == "NodeTitle" ||
                            iterator.name == "GraphPosition" || iterator.name == "NodeCustomColor" ||
                            iterator.name == "IsPinned") continue;

                        EditorGUILayout.PropertyField(iterator, true);
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

        private void DrawSectionHeader(string icon, string title) { GUILayout.Space(20); var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 }; if (EditorGUIUtility.isProSkin) headerStyle.normal.textColor = new Color(0.6f, 0.8f, 1f); else headerStyle.normal.textColor = new Color(0.2f, 0.4f, 0.6f); GUILayout.Label($"{icon} {title}", headerStyle); GUILayout.Space(5); }

        private void DrawStartNodeHelp()
        {
            bool hasPlayer = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>() != null;

            if (hasPlayer)
            {
                DrawSectionHeader("🚀", ToolLang.Get("SCENE READY", "СЦЕНА ГОТОВА К РАБОТЕ"));
                EditorGUILayout.HelpBox(ToolLang.Get("NovellaPlayer detected. Scene is fully linked.", "NovellaPlayer найден. Сцена подключена."), MessageType.Info);

                GUILayout.Space(15);
                GUI.backgroundColor = new Color(0.8f, 0.4f, 0.8f);
                if (GUILayout.Button("🎨 " + ToolLang.Get("UI Editor", "Редактор UI"), new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(35)))
                    NovellaUIEditorWindow.ShowWindow();
                GUI.backgroundColor = Color.white;
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
                    NovellaSceneManagerWindow.ShowWindow();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        private void EndLayout() { GUILayout.EndVertical(); GUILayout.Space(20); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); }

        private float GetCharXOffset(ECharacterPosition preset, float customX)
        {
            if (preset == ECharacterPosition.Left) return -5.5f;
            if (preset == ECharacterPosition.Right) return 5.5f;
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
                    var sr = go.AddComponent<SpriteRenderer>();
                    entity = go.AddComponent<NovellaSceneEntity>();
                    entity.Initialize(config.CharacterAsset.CharacterID);
                    Undo.RegisterCreatedObjectUndo(go, "Spawn Character");
                    entities.Add(entity);
                }

                var renderer = entity.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Sprite targetSprite = config.CharacterAsset.DefaultSprite;
                    string emotionToSet = config.Emotion;

                    float baseX = GetCharXOffset(config.PositionPreset, config.PosX);

                    if (activeLine != null && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID)
                    {
                        emotionToSet = activeLine.Mood;
                        if (activeLine.CustomizeSpeakerLayout)
                        {
                            renderer.sortingOrder = (int)activeLine.SpeakerPlane;

                            entity.transform.localScale = Vector3.one * (config.Scale * activeLine.SpeakerScale);
                            entity.transform.localPosition = new Vector3(baseX + activeLine.SpeakerPosX, config.PosY + activeLine.SpeakerPosY, 0);
                        }
                        else
                        {
                            renderer.sortingOrder = (int)ECharacterPlane.Speaker;
                            entity.transform.localScale = Vector3.one * config.Scale;
                            entity.transform.localPosition = new Vector3(baseX, config.PosY, 0);
                        }
                    }
                    else
                    {
                        renderer.sortingOrder = (int)config.Plane;
                        entity.transform.localScale = Vector3.one * config.Scale;
                        entity.transform.localPosition = new Vector3(baseX, config.PosY, 0);
                    }

                    if (emotionToSet != "Default")
                    {
                        var emotionData = config.CharacterAsset.Emotions.FirstOrDefault(e => e.EmotionName == emotionToSet);
                        if (emotionData.EmotionSprite != null) targetSprite = emotionData.EmotionSprite;
                    }
                    renderer.sprite = targetSprite;

                    if (activeLine != null && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID)
                    {
                        renderer.flipX = activeLine.FlipX;
                        renderer.flipY = activeLine.FlipY;
                    }
                    else
                    {
                        renderer.flipX = false;
                        renderer.flipY = false;
                    }
                }

                bool shouldHide = false;
                if (activeLine != null && activeLine.HideSpeakerSprite && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID)
                {
                    shouldHide = true;
                }

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