// Путь: _Project/NovellaEngine/Editor/Tools/NovellaNodeInspectorUI.cs
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
        private NovellaNodeData _lastSyncedNode;
        private int _activePreviewLineIndex = 0;

        public NovellaNodeInspectorUI(NovellaTree tree, NovellaGraphView graphView, Action onMarkUnsaved, NovellaGraphWindow window)
        {
            _currentTree = tree; _graphView = graphView; _onMarkUnsaved = onMarkUnsaved; _window = window;
            if (_currentTree != null) _serializedObject = new SerializedObject(_currentTree);
        }

        public void SetGraphView(NovellaGraphView gv) => _graphView = gv;

        public void DrawInspector(NovellaNodeView selectedNodeView, bool isStartNodeSelected)
        {
            if (_serializedObject == null || _currentTree == null || _window == null) return;

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            GUILayout.BeginHorizontal(); GUILayout.Space(15); GUILayout.BeginVertical(); GUILayout.Space(15);

            if (isStartNodeSelected)
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScene(); }
                DrawStartNodeHelp(); EndLayout(); return;
            }

            DrawSectionHeader("📝", ToolLang.Get("NODE INSPECTOR", "ИНСПЕКТОР НОДЫ"));

            if (selectedNodeView == null || selectedNodeView.Data == null)
            {
                if (_lastSyncedNode != null) { _lastSyncedNode = null; ClearScene(); }
                EditorGUILayout.HelpBox(ToolLang.Get("Select a Node.", "Выберите Ноду."), MessageType.Info); EndLayout(); return;
            }

            NovellaNodeData nodeData = selectedNodeView.Data;

            if (_lastSyncedNode != nodeData)
            {
                _lastSyncedNode = nodeData;
                _activePreviewLineIndex = 0;
                SyncSceneWithDialogueNode(nodeData);
            }

            if (nodeData.NodeType == ENodeType.Dialogue && nodeData.DialogueLines.Count == 0)
            {
                bool hasOldText = !string.IsNullOrEmpty(nodeData.LocalizedPhrase.GetText("EN")) || !string.IsNullOrEmpty(nodeData.LocalizedPhrase.GetText("RU"));
                if (hasOldText || nodeData.Speaker != null)
                {
                    nodeData.DialogueLines.Add(new DialogueLine { Speaker = nodeData.Speaker, Mood = nodeData.Mood, LocalizedPhrase = nodeData.LocalizedPhrase });
                    nodeData.LocalizedPhrase = new LocalizedString();
                }
                else nodeData.DialogueLines.Add(new DialogueLine());
            }

            if (nodeData.NodeType == ENodeType.Variable && nodeData.Variables.Count == 0)
            {
                if (!string.IsNullOrEmpty(nodeData.VariableName)) nodeData.Variables.Add(new VariableUpdate { VariableName = nodeData.VariableName, VarOperation = nodeData.VarOperation, VarValue = nodeData.VarValue });
                else nodeData.Variables.Add(new VariableUpdate());
            }

            _serializedObject.Update();
            int index = _currentTree.Nodes.FindIndex(n => n != null && n.NodeID == nodeData.NodeID);
            if (index == -1) { EndLayout(); return; }
            SerializedProperty nodeProp = _serializedObject.FindProperty("Nodes").GetArrayElementAtIndex(index);
            SerializedProperty fontSizeProp = nodeProp.FindPropertyRelative("FontSize");
            float originalLabelWidth = EditorGUIUtility.labelWidth;

            GUILayout.Label(ToolLang.Get("General Data", "Основные данные"), EditorStyles.boldLabel);
            EditorGUIUtility.labelWidth = 120;
            EditorGUI.BeginDisabledGroup(true); EditorGUILayout.TextField(ToolLang.Get("Internal ID", "Внутренний ID"), nodeData.NodeID); EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            string newTitle = EditorGUILayout.TextField(ToolLang.Get("Node Name", "Имя Ноды"), nodeData.NodeTitle);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change Node Name"); nodeData.NodeTitle = newTitle; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

            GUILayout.BeginHorizontal();
            if (nodeData.NodeType != ENodeType.End && nodeData.NodeType != ENodeType.Character && nodeData.NodeType != ENodeType.Branch && nodeData.NodeType != ENodeType.Audio && nodeData.NodeType != ENodeType.Variable)
            {
                EditorGUI.BeginChangeCheck(); Color newColor = EditorGUILayout.ColorField(GUIContent.none, nodeData.NodeCustomColor, false, true, false, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Color"); nodeData.NodeCustomColor = newColor; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
            }
            else GUILayout.Label(ToolLang.Get("🎨 Color is locked for this type", "🎨 Цвет заблокирован для этого типа"), EditorStyles.centeredGreyMiniLabel);

            EditorGUI.BeginChangeCheck(); bool isPinnedRequest = GUILayout.Toggle(nodeData.IsPinned, $"📌 {ToolLang.Get("Pin Node", "Закрепить")}", EditorStyles.miniButton, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Pin"); foreach (var n in _currentTree.Nodes) if (n != null) n.IsPinned = false; nodeData.IsPinned = isPinnedRequest; EditorApplication.delayCall += () => _graphView?.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals()); _onMarkUnsaved?.Invoke(); }
            GUILayout.EndHorizontal();

            if (nodeData.NodeType == ENodeType.End)
            {
                DrawSectionHeader("🛑", ToolLang.Get("End Scene Settings", "Настройки конца сцены"));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();
                string[] actionNames = { ToolLang.Get("Return to Main Menu", "Вернуться в гл. меню"), ToolLang.Get("Load Next Chapter", "Загрузить след. главу"), ToolLang.Get("Quit Game", "Выйти из игры") };
                nodeData.EndAction = (EEndAction)EditorGUILayout.Popup(ToolLang.Get("Action:", "Действие:"), (int)nodeData.EndAction, actionNames);
                if (nodeData.EndAction == EEndAction.LoadNextChapter) nodeData.NextChapter = (NovellaTree)EditorGUILayout.ObjectField(ToolLang.Get("Next Chapter:", "След. глава:"), nodeData.NextChapter, typeof(NovellaTree), false);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change End Action"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                EndLayout(); return;
            }

            if (nodeData.NodeType == ENodeType.Audio)
            {
                DrawSectionHeader("🎵", ToolLang.Get("Audio Settings", "Настройки звука"));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                NovellaNodeData syncedDialogue = _currentTree.Nodes.FirstOrDefault(n => n.AudioSyncNodeID == nodeData.NodeID);
                nodeData.SyncWithDialogue = (syncedDialogue != null);

                if (!nodeData.SyncWithDialogue)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Standard sequential mode. Connect to Dialogue 'Audio Sync' port for advanced features.", "Обычный режим. Подключите к порту 'Audio Sync' Диалога для продвинутых фишек."), MessageType.Info);
                    nodeData.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), nodeData.AudioChannel);

                    string channelInfo = "";
                    if (nodeData.AudioChannel == EAudioChannel.BGM) channelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                    else if (nodeData.AudioChannel == EAudioChannel.SFX) channelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                    else if (nodeData.AudioChannel == EAudioChannel.Voice) channelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                    GUILayout.Label(channelInfo, new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = Color.gray } });
                    GUILayout.Space(5);

                    nodeData.AudioAction = (EAudioAction)EditorGUILayout.EnumPopup(ToolLang.Get("Action", "Действие"), nodeData.AudioAction);

                    if (nodeData.AudioAction == EAudioAction.Play)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get("Audio Clip", "Аудиофайл"), GUILayout.Width(126));
                        string audioName = nodeData.AudioAsset != null ? nodeData.AudioAsset.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");

                        if (GUILayout.Button("🎵 " + audioName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                        {
                            NovellaGalleryWindow.ShowWindow(obj => {
                                Undo.RecordObject(_currentTree, "Change Audio");
                                nodeData.AudioAsset = obj as AudioClip;
                                selectedNodeView.RefreshVisuals();
                                _onMarkUnsaved?.Invoke();
                            }, NovellaGalleryWindow.EGalleryFilter.Audio);
                        }

                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Clear Audio"); nodeData.AudioAsset = null; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        nodeData.AudioVolume = EditorGUILayout.Slider(ToolLang.Get("Volume", "Громкость"), nodeData.AudioVolume, 0f, 1f);
                    }
                }
                else
                {
                    int linesCount = syncedDialogue.DialogueLines.Count;
                    int maxEvents = linesCount + 1;

                    string countStr = $"<b><color=#55FF55>{linesCount}</color></b>";
                    string textEn = $"Synced with Dialogue: {countStr} lines available.";
                    string textRu = $"Синхронизировано с Диалогом: доступна {countStr} реплика.";

                    if (ToolLang.IsRU)
                    {
                        int mod10 = linesCount % 10; int mod100 = linesCount % 100;
                        if (mod10 == 1 && mod100 != 11) textRu = $"Синхронизировано с Диалогом: доступна {countStr} реплика.";
                        else if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) textRu = $"Синхронизировано с Диалогом: доступно {countStr} реплики.";
                        else textRu = $"Синхронизировано с Диалогом: доступно {countStr} реплик.";
                    }

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label(ToolLang.Get(textEn, textRu), new GUIStyle(EditorStyles.label) { richText = true });
                    GUILayout.EndVertical();
                    GUILayout.Space(5);

                    string[] lineOptions = new string[syncedDialogue.DialogueLines.Count];
                    for (int i = 0; i < syncedDialogue.DialogueLines.Count; i++)
                    {
                        string spkName = syncedDialogue.DialogueLines[i].Speaker != null ? syncedDialogue.DialogueLines[i].Speaker.name : ToolLang.Get("Narrator", "Автор");
                        lineOptions[i] = ToolLang.Get($"Line #{i + 1} ({spkName})", $"Реплика #{i + 1} ({spkName})");
                    }

                    for (int i = 0; i < nodeData.AudioEvents.Count; i++)
                    {
                        var ev = nodeData.AudioEvents[i];
                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(ToolLang.Get($"Audio Event {i + 1}", $"Событие {i + 1}"), EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Audio Event"); nodeData.AudioEvents.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        GUILayout.Space(5);

                        if (ev.LineIndex >= lineOptions.Length) ev.LineIndex = Mathf.Max(0, lineOptions.Length - 1);

                        string[] triggerNames = { ToolLang.Get("On Start", "В начале"), ToolLang.Get("On End", "В конце"), ToolLang.Get("Custom Delay", "Задержка"), ToolLang.Get("After Dialogue", "После диалога") };
                        ev.TriggerType = (EAudioTriggerType)EditorGUILayout.Popup(ToolLang.Get("Trigger At", "Когда:"), (int)ev.TriggerType, triggerNames);

                        if (ev.TriggerType != EAudioTriggerType.OnDialogueEnd)
                        {
                            ev.LineIndex = EditorGUILayout.Popup(ToolLang.Get("Target Line", "Целевая реплика"), ev.LineIndex, lineOptions);
                        }

                        if (ev.TriggerType == EAudioTriggerType.TimeDelay)
                            ev.TimeDelay = EditorGUILayout.FloatField(ToolLang.Get("Delay (sec)", "Задержка (сек)"), ev.TimeDelay);

                        GUILayout.Space(15);

                        ev.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), ev.AudioChannel);

                        string evChannelInfo = "";
                        if (ev.AudioChannel == EAudioChannel.BGM) evChannelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                        else if (ev.AudioChannel == EAudioChannel.SFX) evChannelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                        else if (ev.AudioChannel == EAudioChannel.Voice) evChannelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                        GUILayout.Label(evChannelInfo, new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = Color.gray } });
                        GUILayout.Space(5);

                        ev.AudioAction = (EAudioAction)EditorGUILayout.EnumPopup(ToolLang.Get("Action", "Действие"), ev.AudioAction);

                        if (ev.AudioAction == EAudioAction.Play)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label(ToolLang.Get("Audio Clip", "Аудиофайл"), GUILayout.Width(126));
                            string evAudioName = ev.AudioAsset != null ? ev.AudioAsset.name : ToolLang.Get("Select from Gallery...", "Выбрать из галереи...");

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

                    if (nodeData.AudioEvents.Count >= maxEvents)
                    {
                        string limitEn = $"Limit reached. Events available: {maxEvents - 1} (based on line count), +1 extra event is provided in case you need to play a sound after the dialogue!";
                        string limitRu = $"Достигнут лимит. Доступно событий: {maxEvents - 1} (по числу реплик), +1 событие дается если вам понадобится проиграть звук после диалога!";
                        EditorGUILayout.HelpBox(ToolLang.Get(limitEn, limitRu), MessageType.Info);
                    }
                    else
                    {
                        if (GUILayout.Button("+ " + ToolLang.Get("Add Audio Event", "Добавить аудио событие"), EditorStyles.miniButton, GUILayout.Height(25)))
                        {
                            Undo.RecordObject(_currentTree, "Add Audio Event");
                            nodeData.AudioEvents.Add(new DialogueAudioEvent());
                        }
                    }
                }

                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Audio Edit"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                EndLayout(); return;
            }

            if (nodeData.NodeType == ENodeType.Variable)
            {
                DrawSectionHeader("📊", ToolLang.Get("Variables List", "Список переменных"));

                if (nodeData.Variables.Count == 0) nodeData.Variables.Add(new VariableUpdate());

                for (int i = 0; i < nodeData.Variables.Count; i++)
                {
                    var v = nodeData.Variables[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get($"Variable #{i + 1}", $"Переменная #{i + 1}"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    bool canDelete = nodeData.Variables.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Var"); nodeData.Variables.RemoveAt(i); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();

                    EditorGUI.BeginChangeCheck();
                    EditorGUIUtility.labelWidth = 100;
                    v.VariableName = EditorGUILayout.TextField(ToolLang.Get("Name (Key)", "Имя (Ключ)"), v.VariableName);

                    GUILayout.BeginHorizontal();
                    string[] opNames = { ToolLang.Get("Set (=)", "Установить (=)"), ToolLang.Get("Add (+)", "Добавить (+)") };
                    v.VarOperation = (EVarOperation)EditorGUILayout.Popup(ToolLang.Get("Operation", "Операция"), (int)v.VarOperation, opNames);
                    v.VarValue = EditorGUILayout.IntField(ToolLang.Get("Value", "Значение"), v.VarValue);
                    GUILayout.EndHorizontal();

                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Var Edit"); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }

                if (GUILayout.Button("+ " + ToolLang.Get("Add Variable", "Добавить переменную"), EditorStyles.miniButton, GUILayout.Height(25)))
                {
                    Undo.RecordObject(_currentTree, "Add Var");
                    nodeData.Variables.Add(new VariableUpdate());
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                EndLayout(); return;
            }

            if (nodeData.NodeType == ENodeType.Branch)
            {
                DrawSectionHeader("🔀", $"{ToolLang.Get("Branch Choices", "Варианты выбора")} ({_window.PreviewLanguage})");
                for (int i = 0; i < nodeData.Choices.Count; i++)
                {
                    var choice = nodeData.Choices[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);
                    GUILayout.Label(ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}"), EditorStyles.boldLabel); GUILayout.FlexibleSpace();

                    bool canDelete = nodeData.Choices.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65))) { Undo.RecordObject(_currentTree, "Remove Choice"); nodeData.Choices.RemoveAt(i); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); break; }
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
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("IF", GUILayout.Width(20));
                            cond.Variable = EditorGUILayout.TextField(cond.Variable, GUILayout.ExpandWidth(true));

                            string[] opNames = { "==", "!=", ">", "<", ">=", "<=" };
                            cond.Operator = (EConditionOperator)EditorGUILayout.Popup((int)cond.Operator, opNames, GUILayout.Width(50));

                            cond.Value = EditorGUILayout.IntField(cond.Value, GUILayout.Width(50));

                            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Condition"); choice.Conditions.RemoveAt(c); GUI.backgroundColor = Color.white; break; }
                            GUI.backgroundColor = Color.white;
                            GUILayout.EndHorizontal();
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

                if (nodeData.Choices.Count >= 4 && !nodeData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выбора)."), MessageType.Info);
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); nodeData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Choice", "Добавить выбор")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); nodeData.Choices.Add(new NovellaChoice()); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); } }
                EndLayout(); return;
            }

            DrawSectionHeader("👥", ToolLang.Get("Scene Layout", "Расстановка на сцене"));
            if (GUILayout.Button($"🛠 {ToolLang.Get("Character Editor", "Редактор Персонажей")}", EditorStyles.miniButton, GUILayout.Height(25))) NovellaCharacterEditor.OpenWindow();
            GUILayout.Space(15);

            for (int i = 0; i < nodeData.ActiveCharacters.Count; i++)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                var activeChar = nodeData.ActiveCharacters[i];
                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                string charName = activeChar.CharacterAsset != null ? activeChar.CharacterAsset.name : ToolLang.Get("Select...", "Выбрать...");
                if (GUILayout.Button($"👤 {charName}", EditorStyles.toolbarDropDown, GUILayout.Width(150)))
                {
                    NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => { Undo.RecordObject(_currentTree, "Change Character Asset"); activeChar.CharacterAsset = selectedChar; selectedNodeView.RefreshVisuals(); SyncSceneWithDialogueNode(nodeData); _onMarkUnsaved?.Invoke(); _window.Repaint(); });
                }

                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65))) { Undo.RecordObject(_currentTree, "Remove Character"); nodeData.ActiveCharacters.RemoveAt(i); GUI.changed = true; GUI.backgroundColor = Color.white; break; }
                GUI.backgroundColor = Color.white; GUILayout.EndHorizontal(); GUILayout.Space(8); EditorGUIUtility.labelWidth = 65;

                if (activeChar.CharacterAsset != null)
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
                    activeChar.Scale = EditorGUILayout.Slider(ToolLang.Get("Scale:", "Масштаб:"), activeChar.Scale, 0.1f, 3f); GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    activeChar.PosX = EditorGUILayout.FloatField(ToolLang.Get("Pos X:", "Поз. X:"), activeChar.PosX); GUILayout.Space(10);
                    activeChar.PosY = EditorGUILayout.FloatField(ToolLang.Get("Pos Y:", "Поз. Y:"), activeChar.PosY); GUILayout.EndHorizontal();
                    if (EditorGUI.EndChangeCheck()) Undo.RecordObject(_currentTree, "Change Data");
                }
                EditorGUIUtility.labelWidth = originalLabelWidth; GUILayout.Space(8); GUILayout.EndVertical(); GUILayout.Space(10);
            }

            if (GUILayout.Button($"+ {ToolLang.Get("Add Character to Scene", "Добавить персонажа на сцену")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Character"); nodeData.ActiveCharacters.Add(new CharacterInDialogue()); GUI.changed = true; }

            if (GUI.changed) { selectedNodeView.RefreshVisuals(); SyncSceneWithDialogueNode(nodeData); _onMarkUnsaved?.Invoke(); }

            DrawSectionHeader("💬", $"{ToolLang.Get("Dialogue Lines", "Список реплик")} ({_window.PreviewLanguage})");

            for (int i = 0; i < nodeData.DialogueLines.Count; i++)
            {
                var line = nodeData.DialogueLines[i];

                GUI.backgroundColor = _activePreviewLineIndex == i ? new Color(0.85f, 0.95f, 1f) : Color.white;
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = Color.white;

                if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    _activePreviewLineIndex = i;
                    SyncSceneWithDialogueNode(nodeData);
                    _window.Repaint();
                }

                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label($"#{i + 1}", EditorStyles.boldLabel, GUILayout.Width(25));

                string speakerName = line.Speaker != null ? line.Speaker.name : ToolLang.Get("Narrator", "Автор");
                if (GUILayout.Button($"🗣 {speakerName}", EditorStyles.toolbarDropDown, GUILayout.Width(120)))
                {
                    NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => {
                        Undo.RecordObject(_currentTree, "Change Speaker");
                        line.Speaker = selectedChar;
                        selectedNodeView.RefreshVisuals();
                        _activePreviewLineIndex = i;
                        SyncSceneWithDialogueNode(nodeData);
                        _onMarkUnsaved?.Invoke();
                        _window.Repaint();
                    });
                }

                if (line.Speaker != null)
                {
                    if (GUILayout.Button("🛠", EditorStyles.toolbarButton, GUILayout.Width(25)))
                        NovellaCharacterEditor.OpenWithCharacter(line.Speaker);

                    GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                    if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(20))) { Undo.RecordObject(_currentTree, "Clear Speaker"); line.Speaker = null; selectedNodeView.RefreshVisuals(); _activePreviewLineIndex = i; SyncSceneWithDialogueNode(nodeData); _onMarkUnsaved?.Invoke(); }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.FlexibleSpace();

                bool canDeleteLine = nodeData.DialogueLines.Count > 1;
                EditorGUI.BeginDisabledGroup(!canDeleteLine);
                GUI.backgroundColor = canDeleteLine ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                if (GUILayout.Button(ToolLang.Get("Del", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    Undo.RecordObject(_currentTree, "Remove Line");
                    nodeData.DialogueLines.RemoveAt(i);
                    if (_activePreviewLineIndex >= nodeData.DialogueLines.Count) _activePreviewLineIndex = Mathf.Max(0, nodeData.DialogueLines.Count - 1);
                    selectedNodeView.RefreshVisuals();
                    SyncSceneWithDialogueNode(nodeData);
                    _onMarkUnsaved?.Invoke();
                    GUI.backgroundColor = Color.white;
                    EditorGUI.EndDisabledGroup();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                if (line.Speaker != null)
                {
                    List<string> moodList = new List<string> { "Default" }; if (line.Speaker.Emotions != null) moodList.AddRange(line.Speaker.Emotions.Select(e => e.EmotionName));
                    int moodIdx = moodList.IndexOf(line.Mood); if (moodIdx == -1) moodIdx = 0;
                    EditorGUIUtility.labelWidth = 55;
                    line.Mood = moodList[EditorGUILayout.Popup(ToolLang.Get("Mood:", "Эмоция:"), moodIdx, moodList.ToArray(), GUILayout.Width(150))];
                }
                GUILayout.FlexibleSpace();

                EditorGUIUtility.labelWidth = 40;
                GUIContent waitContent = new GUIContent(ToolLang.Get("Wait:", "Пауза:"), ToolLang.Get("Delay in seconds before this line is shown.", "Задержка в секундах перед появлением этой реплики."));
                line.DelayBefore = EditorGUILayout.FloatField(waitContent, line.DelayBefore, GUILayout.Width(80));
                GUILayout.Label(ToolLang.Get("s.", "с."), GUILayout.Width(15));
                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUILayout.EndHorizontal();

                GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                string currentPhrase = line.LocalizedPhrase.GetText(_window.PreviewLanguage);
                string newPhrase = EditorGUILayout.TextArea(currentPhrase, textAreaStyle, GUILayout.Height(45));
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Phrase"); line.LocalizedPhrase.SetText(_window.PreviewLanguage, newPhrase); _activePreviewLineIndex = i; SyncSceneWithDialogueNode(nodeData); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

                if (GUILayout.Button($"✎ {ToolLang.Get("Full Editor", "Редактор")}", EditorStyles.miniButton))
                    NovellaTextEditorWindow.OpenWindow(line, fontSizeProp, _currentTree, () => { selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); });

                GUILayout.EndVertical();
                GUILayout.Space(5);
            }

            if (GUILayout.Button($"+ {ToolLang.Get("Add Dialogue Line", "Добавить реплику")}", EditorStyles.miniButton, GUILayout.Height(30)))
            {
                Undo.RecordObject(_currentTree, "Add Line");
                var newLine = new DialogueLine();
                if (nodeData.DialogueLines.Count > 0) newLine.Speaker = nodeData.DialogueLines.Last().Speaker;
                nodeData.DialogueLines.Add(newLine);
                _activePreviewLineIndex = nodeData.DialogueLines.Count - 1;
                selectedNodeView.RefreshVisuals();
                SyncSceneWithDialogueNode(nodeData);
                _onMarkUnsaved?.Invoke();
            }

            if (GUI.changed) { _serializedObject.ApplyModifiedProperties(); if (_graphView != null) { _graphView.SyncGraphToData(); _onMarkUnsaved?.Invoke(); } }
            EndLayout();
        }

        private void DrawSectionHeader(string icon, string title) { GUILayout.Space(20); var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 }; if (EditorGUIUtility.isProSkin) headerStyle.normal.textColor = new Color(0.6f, 0.8f, 1f); else headerStyle.normal.textColor = new Color(0.2f, 0.4f, 0.6f); GUILayout.Label($"{icon} {title}", headerStyle); GUILayout.Space(5); }

        private void DrawStartNodeHelp()
        {
            GUILayout.Label(ToolLang.Get("🟢 START NODE", "🟢 СТАРТОВАЯ НОДА"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(ToolLang.Get("Scene starting point.", "Стартовая точка сцены."), MessageType.Info);

            GUILayout.Space(15);
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("🚀 " + ToolLang.Get("Smart Scene Setup", "Умная Настройка Сцены"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(40)))
            {
                PerformSmartSceneSetup();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);
            EditorGUILayout.HelpBox(ToolLang.Get(
                "Automatically creates Canvas, UI, EventSystem and NovellaPlayer on the current scene. If elements exist, it only adds the missing ones.",
                "Автоматически создает Canvas, UI, EventSystem и NovellaPlayer на текущей сцене. Если элементы уже есть, создадутся только недостающие."
            ), MessageType.None);
        }

        private void PerformSmartSceneSetup()
        {
            int createdCount = 0;

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camGO = new GameObject("Main Camera");
                camGO.tag = "MainCamera";
                mainCam = camGO.AddComponent<Camera>();
                mainCam.orthographic = true;
                mainCam.backgroundColor = Color.black;
                camGO.AddComponent<AudioListener>();
                Undo.RegisterCreatedObjectUndo(camGO, "Create Camera");
                createdCount++;
            }

            if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
                createdCount++;
            }

            NovellaPlayer player = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
            if (player == null)
            {
                GameObject managerGO = new GameObject("--- NOVELLA ENGINE ---");
                player = managerGO.AddComponent<NovellaPlayer>();
                Undo.RegisterCreatedObjectUndo(managerGO, "Create Novella Engine");
                createdCount++;
            }

            if (player.StoryTree == null)
            {
                player.StoryTree = _currentTree;
            }

            Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("Novella Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
                createdCount++;
            }

            if (player.DialoguePanel == null)
            {
                GameObject dialoguePanel = new GameObject("Dialogue Panel");
                dialoguePanel.transform.SetParent(canvas.transform, false);
                var bg = dialoguePanel.AddComponent<UnityEngine.UI.Image>();
                bg.color = new Color(0, 0, 0, 0.8f);
                var panelRect = dialoguePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.1f, 0.05f);
                panelRect.anchorMax = new Vector2(0.9f, 0.3f);
                panelRect.offsetMin = Vector2.zero; panelRect.offsetMax = Vector2.zero;
                player.DialoguePanel = dialoguePanel;
                Undo.RegisterCreatedObjectUndo(dialoguePanel, "Create Panel");
                createdCount++;

                GameObject nameObj = new GameObject("Speaker Name");
                nameObj.transform.SetParent(dialoguePanel.transform, false);
                var nameText = nameObj.AddComponent<TMPro.TextMeshProUGUI>();
                nameText.text = "Speaker Name";
                nameText.fontSize = 40; nameText.fontStyle = TMPro.FontStyles.Bold;
                nameText.color = new Color(1f, 0.8f, 0.4f);
                var nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0.05f, 0.75f); nameRect.anchorMax = new Vector2(0.95f, 0.95f);
                nameRect.offsetMin = Vector2.zero; nameRect.offsetMax = Vector2.zero;
                player.SpeakerNameText = nameText;

                GameObject bodyObj = new GameObject("Dialogue Body");
                bodyObj.transform.SetParent(dialoguePanel.transform, false);
                var bodyText = bodyObj.AddComponent<TMPro.TextMeshProUGUI>();
                bodyText.text = "Dialogue text goes here...";
                bodyText.fontSize = 32; bodyText.color = Color.white;
                bodyText.enableWordWrapping = true; bodyText.alignment = TMPro.TextAlignmentOptions.TopLeft;
                var bodyRect = bodyObj.GetComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0.05f, 0.1f); bodyRect.anchorMax = new Vector2(0.95f, 0.7f);
                bodyRect.offsetMin = Vector2.zero; bodyRect.offsetMax = Vector2.zero;
                player.DialogueBodyText = bodyText;
            }

            if (player.ChoiceContainer == null)
            {
                GameObject choicesObj = new GameObject("Choices Container");
                choicesObj.transform.SetParent(canvas.transform, false);
                var choicesRect = choicesObj.AddComponent<RectTransform>();
                choicesRect.anchorMin = new Vector2(0.3f, 0.4f); choicesRect.anchorMax = new Vector2(0.7f, 0.9f);
                choicesRect.offsetMin = Vector2.zero; choicesRect.offsetMax = Vector2.zero;
                var vlg = choicesObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                vlg.spacing = 15; vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childControlHeight = false; vlg.childControlWidth = true;
                player.ChoiceContainer = choicesObj.transform;
                Undo.RegisterCreatedObjectUndo(choicesObj, "Create Choices");
                createdCount++;
            }

            if (player.ChoiceButtonPrefab == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/NovellaEngine/Runtime/Prefabs"))
                {
                    System.IO.Directory.CreateDirectory(Application.dataPath + "/_Project/NovellaEngine/Runtime/Prefabs");
                    AssetDatabase.Refresh();
                }
                string prefabPath = "Assets/_Project/NovellaEngine/Runtime/Prefabs/ChoiceButton.prefab";
                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (savedPrefab == null)
                {
                    GameObject buttonObj = new GameObject("ChoiceButtonTemplate");
                    var btnRect = buttonObj.AddComponent<RectTransform>();
                    btnRect.sizeDelta = new Vector2(0, 60);
                    var btnImg = buttonObj.AddComponent<UnityEngine.UI.Image>();
                    btnImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
                    buttonObj.AddComponent<UnityEngine.UI.Button>();

                    GameObject btnTextObj = new GameObject("Text");
                    btnTextObj.transform.SetParent(buttonObj.transform, false);
                    var btnText = btnTextObj.AddComponent<TMPro.TextMeshProUGUI>();
                    btnText.text = "Choice Text";
                    btnText.fontSize = 30; btnText.color = Color.white; btnText.alignment = TMPro.TextAlignmentOptions.Center;
                    var btnTextRect = btnTextObj.GetComponent<RectTransform>();
                    btnTextRect.anchorMin = Vector2.zero; btnTextRect.anchorMax = Vector2.one;
                    btnTextRect.offsetMin = Vector2.zero; btnTextRect.offsetMax = Vector2.zero;

                    savedPrefab = PrefabUtility.SaveAsPrefabAsset(buttonObj, prefabPath);
                    UnityEngine.Object.DestroyImmediate(buttonObj);
                    createdCount++;
                }
                player.ChoiceButtonPrefab = savedPrefab;
            }

            if (player.CharactersContainer == null)
            {
                GameObject charsObj = new GameObject("Characters Holder");
                player.CharactersContainer = charsObj.transform;
                Undo.RegisterCreatedObjectUndo(charsObj, "Create Characters Holder");
                createdCount++;
            }

            EditorUtility.SetDirty(player);

            if (createdCount > 0)
                EditorUtility.DisplayDialog("Setup Complete", ToolLang.Get($"Scene configured! Created {createdCount} new elements.", $"Сцена настроена! Создано новых элементов: {createdCount}."), "OK");
            else
                EditorUtility.DisplayDialog("Setup Complete", ToolLang.Get("Everything is already set up perfectly!", "Всё уже настроено идеально! Недостающих элементов нет."), "OK");

            Selection.activeGameObject = player.gameObject;
        }

        private void EndLayout() { GUILayout.EndVertical(); GUILayout.Space(20); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); }

        private void SyncSceneWithDialogueNode(NovellaNodeData nodeData)
        {
            if (nodeData.NodeType != ENodeType.Dialogue && nodeData.NodeType != ENodeType.Event) { ClearScene(); return; }

            var entities = UnityEngine.Object.FindObjectsByType<NovellaSceneEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            string currentSpeakerID = "";
            if (nodeData.DialogueLines.Count > 0 && _activePreviewLineIndex >= 0 && _activePreviewLineIndex < nodeData.DialogueLines.Count)
            {
                var activeLine = nodeData.DialogueLines[_activePreviewLineIndex];
                if (activeLine.Speaker != null) currentSpeakerID = activeLine.Speaker.CharacterID;
            }

            foreach (var activeChar in nodeData.ActiveCharacters)
            {
                if (activeChar.CharacterAsset == null) continue;

                var entity = entities.FirstOrDefault(e => e.LinkedNodeID == activeChar.CharacterAsset.CharacterID);
                if (entity == null)
                {
                    GameObject go = new GameObject("Char_" + activeChar.CharacterAsset.name);
                    var sr = go.AddComponent<SpriteRenderer>();
                    entity = go.AddComponent<NovellaSceneEntity>();
                    entity.Initialize(activeChar.CharacterAsset.CharacterID);
                    Undo.RegisterCreatedObjectUndo(go, "Spawn Character");
                }

                var renderer = entity.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Sprite targetSprite = activeChar.CharacterAsset.DefaultSprite;

                    string emotionToSet = activeChar.Emotion;
                    if (activeChar.CharacterAsset.CharacterID == currentSpeakerID)
                    {
                        emotionToSet = nodeData.DialogueLines[_activePreviewLineIndex].Mood;
                    }

                    if (emotionToSet != "Default")
                    {
                        var emotionData = activeChar.CharacterAsset.Emotions.FirstOrDefault(e => e.EmotionName == emotionToSet);
                        if (emotionData.EmotionSprite != null) targetSprite = emotionData.EmotionSprite;
                    }

                    renderer.sprite = targetSprite;

                    if (activeChar.CharacterAsset.CharacterID == currentSpeakerID)
                        renderer.sortingOrder = (int)ECharacterPlane.Speaker;
                    else
                        renderer.sortingOrder = (int)activeChar.Plane;
                }
                entity.transform.localScale = Vector3.one * activeChar.Scale;
                entity.transform.position = new Vector3(activeChar.PosX, activeChar.PosY, 0);
                entity.gameObject.SetActive(true);
            }

            bool sceneChanged = false;
            foreach (var entity in entities)
            {
                bool isNeeded = nodeData.ActiveCharacters.Any(c => c.CharacterAsset != null && c.CharacterAsset.CharacterID == entity.LinkedNodeID);
                if (!isNeeded) { Undo.DestroyObjectImmediate(entity.gameObject); sceneChanged = true; }
            }
            if (sceneChanged && !Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private void ClearScene()
        {
            var entities = UnityEngine.Object.FindObjectsByType<NovellaSceneEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool sceneChanged = false;
            foreach (var entity in entities)
            {
                Undo.DestroyObjectImmediate(entity.gameObject);
                sceneChanged = true;
            }
            if (sceneChanged && !Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}