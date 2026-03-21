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

        // === ХЕЛПЕР: ОТРИСОВКА ВЫПАДАЮЩЕГО СПИСКА С ПРЕМИУМ-ИКОНКАМИ ===
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

            // Массив чистых имен для логики
            var realNames = settings.Variables.Select(v => v.Name).ToArray();
            // Массив для отображения (добавляем 💎 если премиум)
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

        // === ХЕЛПЕР: СЕРАЯ ПОДСКАЗКА ТИПА ДАННЫХ ===
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
                nodeData.Variables.Add(new VariableUpdate());
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

            if (nodeData.NodeType == ENodeType.Note)
            {
                DrawSectionHeader("📌", ToolLang.Get("Note Settings", "Настройки Заметки"));

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginVertical(EditorStyles.helpBox);
                nodeData.NoteWidth = EditorGUILayout.Slider(ToolLang.Get("Node Max Width", "Ширина ноды"), nodeData.NoteWidth, 200f, 1000f);
                GUILayout.Space(5);
                nodeData.NoteTitleColor = EditorGUILayout.ColorField(ToolLang.Get("Title Color", "Цвет названия"), nodeData.NoteTitleColor);
                nodeData.NoteTitleFontSize = EditorGUILayout.IntSlider(ToolLang.Get("Title Font Size", "Размер названия"), nodeData.NoteTitleFontSize, 10, 60);
                GUILayout.EndVertical();

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                nodeData.ShowBackground = EditorGUILayout.Toggle(ToolLang.Get("Show Background", "Показывать фон"), nodeData.ShowBackground);
                if (nodeData.ShowBackground)
                {
                    nodeData.NodeCustomColor = EditorGUILayout.ColorField(ToolLang.Get("Background Color", "Цвет фона"), nodeData.NodeCustomColor);
                }
                GUILayout.EndVertical();

                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(ToolLang.Get("Text Content:", "Текст заметки:"), EditorStyles.boldLabel);
                nodeData.NoteText = EditorGUILayout.TextArea(nodeData.NoteText, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(120));

                GUILayout.Space(5);
                nodeData.NoteTextColor = EditorGUILayout.ColorField(ToolLang.Get("Text Color", "Цвет текста"), nodeData.NoteTextColor);
                nodeData.FontSize = EditorGUILayout.IntSlider(ToolLang.Get("Text Font Size", "Размер текста"), nodeData.FontSize > 0 ? nodeData.FontSize : 14, 10, 60);
                GUILayout.EndVertical();

                GUILayout.Space(10);
                DrawSectionHeader("🖼", ToolLang.Get("Attached Images", "Изображения на Заметке"));

                for (int i = 0; i < nodeData.NoteImages.Count; i++)
                {
                    var img = nodeData.NoteImages[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get($"Image {i + 1}", $"Картинка {i + 1}"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { nodeData.NoteImages.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
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

                if (nodeData.NoteImages.Count < 3)
                {
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Image", "Добавить картинку"), EditorStyles.miniButton))
                    {
                        nodeData.NoteImages.Add(new NoteImageData());
                    }
                }

                GUILayout.Space(10);
                DrawSectionHeader("🔗", ToolLang.Get("Attached Links", "Прикрепленные ссылки"));

                for (int i = 0; i < nodeData.NoteLinks.Count; i++)
                {
                    var lnk = nodeData.NoteLinks[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                    lnk.DisplayName = EditorGUILayout.TextField(lnk.DisplayName, GUILayout.Width(100));
                    lnk.URL = EditorGUILayout.TextField(lnk.URL, GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { nodeData.NoteLinks.RemoveAt(i); GUI.backgroundColor = Color.white; break; }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                if (nodeData.NoteLinks.Count < 3)
                {
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Link", "Добавить ссылку"), EditorStyles.miniButton))
                    {
                        nodeData.NoteLinks.Add(new NoteLinkData());
                    }
                }

                if (EditorGUI.EndChangeCheck() || GUI.changed)
                {
                    Undo.RecordObject(_currentTree, "Edit Note");
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

            if (nodeData.NodeType == ENodeType.End)
            {
                DrawSectionHeader("🛑", ToolLang.Get("End Scene Settings", "Настройки конца сцены"));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                string[] actionNames = {
                    ToolLang.Get("Return to Main Menu", "Вернуться в гл. меню"),
                    ToolLang.Get("Load Next Chapter", "Загрузить след. главу"),
                    ToolLang.Get("Load Specific Scene", "Загрузить конкретную сцену"),
                    ToolLang.Get("Quit Game", "Выйти из игры")
                };

                nodeData.EndAction = (EEndAction)EditorGUILayout.Popup(ToolLang.Get("Action:", "Действие:"), (int)nodeData.EndAction, actionNames);

                if (nodeData.EndAction == EEndAction.LoadNextChapter)
                {
                    nodeData.NextChapter = (NovellaTree)EditorGUILayout.ObjectField(ToolLang.Get("Next Chapter:", "След. глава:"), nodeData.NextChapter, typeof(NovellaTree), false);
                }
                else if (nodeData.EndAction == EEndAction.LoadSpecificScene)
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

                        int selectedIndex = Mathf.Max(0, sceneNames.IndexOf(nodeData.TargetSceneName));
                        selectedIndex = EditorGUILayout.Popup(ToolLang.Get("Target Scene:", "Целевая сцена:"), selectedIndex, sceneNames.ToArray());
                        nodeData.TargetSceneName = sceneNames[selectedIndex];

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

            if (nodeData.NodeType == ENodeType.Audio)
            {
                DrawSectionHeader("🎵", ToolLang.Get("Audio Settings", "Настройки звука"));
                EditorGUIUtility.labelWidth = 130; EditorGUI.BeginChangeCheck();

                NovellaNodeData syncedDialogue = _currentTree.Nodes.FirstOrDefault(n => n.AudioSyncNodeID == nodeData.NodeID);
                nodeData.SyncWithDialogue = (syncedDialogue != null);

                if (!nodeData.SyncWithDialogue)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Standard sequential mode. Connect to Dialogue 'Audio Sync' port for advanced features.", "Обычный режим. Подключите к порту 'Audio Sync' Диалога для продвинутых фишек."), MessageType.Info);

                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    nodeData.AudioChannel = (EAudioChannel)EditorGUILayout.EnumPopup(ToolLang.Get("Channel", "Канал"), nodeData.AudioChannel);

                    string channelInfo = "";
                    if (nodeData.AudioChannel == EAudioChannel.BGM) channelInfo = ToolLang.Get("BGM - Background Music (Loops usually).", "BGM - Фоновая музыка (музыка сцены).");
                    else if (nodeData.AudioChannel == EAudioChannel.SFX) channelInfo = ToolLang.Get("SFX - Sound Effects (Steps, clicks, etc).", "SFX - Звуковые эффекты (шаги, удары и т.д).");
                    else if (nodeData.AudioChannel == EAudioChannel.Voice) channelInfo = ToolLang.Get("Voice - Character Voiceover.", "Voice - Озвучка персонажей.");
                    EditorGUILayout.HelpBox(channelInfo, MessageType.None);
                    GUILayout.EndVertical();

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
                    int requiredEvents = linesCount + 1;

                    while (nodeData.AudioEvents.Count < requiredEvents)
                    {
                        nodeData.AudioEvents.Add(new DialogueAudioEvent());
                    }
                    while (nodeData.AudioEvents.Count > requiredEvents)
                    {
                        nodeData.AudioEvents.RemoveAt(nodeData.AudioEvents.Count - 1);
                    }

                    for (int i = 0; i <= linesCount; i++)
                    {
                        var ev = nodeData.AudioEvents[i];
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
                    nodeData.Variables.Add(new VariableUpdate());
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                }

                EndLayout(); return;
            }

            if (nodeData.NodeType == ENodeType.Condition)
            {
                DrawSectionHeader("❓", ToolLang.Get("Condition Logic", "Логика Условия (If/Else)"));

                if (nodeData.Conditions.Count == 0) nodeData.Conditions.Add(new ChoiceCondition());

                for (int c = 0; c < nodeData.Conditions.Count; c++)
                {
                    var cond = nodeData.Conditions[c];
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

                    bool canDeleteCond = nodeData.Conditions.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDeleteCond);
                    GUI.backgroundColor = canDeleteCond ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_currentTree, "Remove Condition"); nodeData.Conditions.RemoveAt(c); GUI.backgroundColor = Color.white; break; }
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
                    nodeData.Conditions.Add(new ChoiceCondition());
                }

                GUILayout.Space(10);
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "If ALL conditions above are met, the story goes to the 'True' port. Otherwise, it goes to 'False'.",
                    "Если ВСЕ условия выше выполняются, сюжет пойдет по ветке 'Истина' (True). Иначе — по ветке 'Ложь' (False)."
                ), MessageType.Info);

                EndLayout(); return;
            }

            if (nodeData.NodeType == ENodeType.Random)
            {
                DrawSectionHeader("🎲", ToolLang.Get("Random Chances", "Случайные события (Шанс)"));

                int totalMaxWeight = nodeData.Choices.Sum(c => c.ChanceWeight + c.ChanceModifiers.Sum(m => m.BonusWeight));

                for (int i = 0; i < nodeData.Choices.Count; i++)
                {
                    var choice = nodeData.Choices[i];
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);

                    int choiceMaxWeight = choice.ChanceWeight + choice.ChanceModifiers.Sum(m => m.BonusWeight);
                    float percentage = totalMaxWeight > 0 ? ((float)choiceMaxWeight / totalMaxWeight) * 100f : 0f;

                    GUILayout.Label(ToolLang.Get($"Max Chance {i + 1} ({percentage:F1}%)", $"Макс. Шанс {i + 1} ({percentage:F1}%)"), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    bool canDelete = nodeData.Choices.Count > 1;
                    EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(65))) { Undo.RecordObject(_currentTree, "Remove Choice"); nodeData.Choices.RemoveAt(i); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); break; }
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

                if (nodeData.Choices.Count >= 4 && !nodeData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выхода)."), MessageType.Info);
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); nodeData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Chance", "Добавить шанс")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); nodeData.Choices.Add(new NovellaChoice() { ChanceWeight = 50 }); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); } }
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

                if (nodeData.Choices.Count >= 4 && !nodeData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выбора)."), MessageType.Info);
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); nodeData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Choice", "Добавить выбор")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); nodeData.Choices.Add(new NovellaChoice()); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); } }
                EndLayout(); return;
            }

            DrawSectionHeader("👥", ToolLang.Get("Scene Layout", "Расстановка на сцене"));
            EditorGUILayout.HelpBox(ToolLang.Get(
                "Note: Speakers are auto-spawned. Use this section ONLY for background characters or to set default positions for speakers.",
                "Примечание: Спикеры из реплик появляются автоматически. Используйте этот раздел ТОЛЬКО для настройки массовки или задания позиции спикеров по умолчанию."
            ), MessageType.None);

            if (GUILayout.Button($"🛠 {ToolLang.Get("Character Editor", "Редактор Персонажей")}", EditorStyles.miniButton, GUILayout.Height(25))) NovellaCharacterEditor.OpenWindow();
            GUILayout.Space(15);

            for (int i = 0; i < nodeData.ActiveCharacters.Count; i++)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                var activeChar = nodeData.ActiveCharacters[i];
                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                activeChar.IsExpanded = GUILayout.Toggle(activeChar.IsExpanded, activeChar.IsExpanded ? "▼" : "▶", EditorStyles.toolbarButton, GUILayout.Width(25));

                string charName = activeChar.CharacterAsset != null ? activeChar.CharacterAsset.name : ToolLang.Get("Select...", "Выбрать...");
                if (GUILayout.Button($"👤 {charName}", EditorStyles.toolbarDropDown, GUILayout.ExpandWidth(true)))
                {
                    NovellaCharacterSelectorWindow.ShowWindow((selectedChar) => { Undo.RecordObject(_currentTree, "Change Character Asset"); activeChar.CharacterAsset = selectedChar; selectedNodeView.RefreshVisuals(); SyncSceneWithDialogueNode(nodeData); _onMarkUnsaved?.Invoke(); _window.Repaint(); });
                }

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(30))) { Undo.RecordObject(_currentTree, "Remove Character"); nodeData.ActiveCharacters.RemoveAt(i); GUI.changed = true; GUI.backgroundColor = Color.white; break; }
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
                    activeChar.Scale = EditorGUILayout.Slider(ToolLang.Get("Scale:", "Масштаб:"), activeChar.Scale, 0.1f, 3f); GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    activeChar.PosX = EditorGUILayout.FloatField(ToolLang.Get("Pos X:", "Поз. X:"), activeChar.PosX); GUILayout.Space(10);
                    activeChar.PosY = EditorGUILayout.FloatField(ToolLang.Get("Pos Y:", "Поз. Y:"), activeChar.PosY); GUILayout.EndHorizontal();
                    if (EditorGUI.EndChangeCheck()) Undo.RecordObject(_currentTree, "Change Data");
                }
                EditorGUIUtility.labelWidth = originalLabelWidth; GUILayout.EndVertical(); GUILayout.Space(5);
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

                GUIStyle numStyle = new GUIStyle(EditorStyles.boldLabel);
                numStyle.normal.textColor = new Color(1f, 0.6f, 0f);
                GUILayout.Label($"#{i + 1}", numStyle, GUILayout.Width(25));

                if (GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    if (i > 0)
                    {
                        Undo.RecordObject(_currentTree, "Move Line Up");
                        var temp = nodeData.DialogueLines[i];
                        nodeData.DialogueLines[i] = nodeData.DialogueLines[i - 1];
                        nodeData.DialogueLines[i - 1] = temp;
                        if (_activePreviewLineIndex == i) _activePreviewLineIndex = i - 1;
                        else if (_activePreviewLineIndex == i - 1) _activePreviewLineIndex = i;
                        SyncSceneWithDialogueNode(nodeData);
                        _onMarkUnsaved?.Invoke();
                        break;
                    }
                }
                if (GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    if (i < nodeData.DialogueLines.Count - 1)
                    {
                        Undo.RecordObject(_currentTree, "Move Line Down");
                        var temp = nodeData.DialogueLines[i];
                        nodeData.DialogueLines[i] = nodeData.DialogueLines[i + 1];
                        nodeData.DialogueLines[i + 1] = temp;
                        if (_activePreviewLineIndex == i) _activePreviewLineIndex = i + 1;
                        else if (_activePreviewLineIndex == i + 1) _activePreviewLineIndex = i;
                        SyncSceneWithDialogueNode(nodeData);
                        _onMarkUnsaved?.Invoke();
                        break;
                    }
                }

                GUILayout.Space(5);

                string speakerName = line.Speaker != null ? line.Speaker.name : ToolLang.Get("No Speaker", "Без спикера");
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
                    line.Mood = moodList[EditorGUILayout.Popup(ToolLang.Get("Mood:", "Эмоция:"), moodIdx, moodList.ToArray(), GUILayout.Width(130))];

                    GUILayout.Space(5);
                    GUILayout.BeginVertical();
                    line.HideSpeakerName = GUILayout.Toggle(line.HideSpeakerName, " 👁 " + ToolLang.Get("Override Name", "Заменить/Скрыть Имя"));
                    line.HideSpeakerSprite = GUILayout.Toggle(line.HideSpeakerSprite, " 👻 " + ToolLang.Get("Hide Sprite", "Скрыть Спрайт"));
                    line.CustomizeSpeakerLayout = GUILayout.Toggle(line.CustomizeSpeakerLayout, " ⚙ " + ToolLang.Get("Override Pos", "Позиция"));
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();

                EditorGUIUtility.labelWidth = 45;
                line.DelayBefore = EditorGUILayout.FloatField(new GUIContent(ToolLang.Get("Wait:", "Пауза:"), ToolLang.Get("Delay in seconds before showing this line.", "Задержка в секундах перед появлением этой реплики.")), line.DelayBefore, GUILayout.Width(85));
                GUILayout.Label(ToolLang.Get("s.", "с."), GUILayout.Width(15));

                if (line.FontSize <= 0) line.FontSize = nodeData.FontSize > 0 ? nodeData.FontSize : 32;
                EditorGUIUtility.labelWidth = 40;
                line.FontSize = EditorGUILayout.IntField(new GUIContent(ToolLang.Get("Size:", "Разм:"), ToolLang.Get("Font size for this specific line.", "Размер шрифта только для этой реплики.")), line.FontSize, GUILayout.Width(75));
                EditorGUIUtility.labelWidth = originalLabelWidth;
                GUILayout.EndHorizontal();

                if (line.Speaker != null && line.HideSpeakerName)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "If text is empty, name window will be hidden. If filled, it overrides the speaker's name.",
                        "Если поле пустое, окно имени скроется. Если заполнить - заменит оригинальное имя."
                    ), MessageType.None);

                    GUILayout.BeginHorizontal();
                    float oldLw = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = ToolLang.IsRU ? 80 : 85;
                    line.CustomName = EditorGUILayout.TextField(ToolLang.Get("Custom Name:", "Новое Имя:"), line.CustomName, GUILayout.ExpandWidth(true));

                    GUILayout.Space(10);
                    EditorGUIUtility.labelWidth = 40;
                    line.CustomNameColor = EditorGUILayout.ColorField(ToolLang.Get("Color:", "Цвет:"), line.CustomNameColor, GUILayout.Width(100));
                    EditorGUIUtility.labelWidth = oldLw;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                if (line.Speaker != null && line.CustomizeSpeakerLayout)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "Overrides the default position from 'Scene Layout' for this line only.",
                        "Заменяет базовые настройки из 'Расстановки на сцене' только для этой реплики."
                    ), MessageType.None);

                    GUILayout.BeginHorizontal();
                    float oldLw = EditorGUIUtility.labelWidth;

                    EditorGUIUtility.labelWidth = 15;
                    line.SpeakerPosX = EditorGUILayout.FloatField("X", line.SpeakerPosX, GUILayout.Width(65));
                    GUILayout.Space(5);
                    line.SpeakerPosY = EditorGUILayout.FloatField("Y", line.SpeakerPosY, GUILayout.Width(65));

                    GUILayout.FlexibleSpace();

                    EditorGUIUtility.labelWidth = ToolLang.IsRU ? 55 : 45;
                    line.SpeakerScale = EditorGUILayout.FloatField(ToolLang.Get("Scale", "Размер"), line.SpeakerScale, GUILayout.Width(100));

                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    EditorGUIUtility.labelWidth = 45;
                    line.SpeakerPlane = (ECharacterPlane)EditorGUILayout.EnumPopup(ToolLang.Get("Plane", "Слой"), line.SpeakerPlane);
                    GUILayout.EndHorizontal();

                    EditorGUIUtility.labelWidth = oldLw;
                    GUILayout.EndVertical();
                }

                GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                string currentPhrase = line.LocalizedPhrase.GetText(_window.PreviewLanguage);
                string newPhrase = EditorGUILayout.TextArea(currentPhrase, textAreaStyle, GUILayout.Height(45));
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Phrase"); line.LocalizedPhrase.SetText(_window.PreviewLanguage, newPhrase); _activePreviewLineIndex = i; SyncSceneWithDialogueNode(nodeData); selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();

                line.UseTypewriter = GUILayout.Toggle(line.UseTypewriter, new GUIContent(" ⌨ " + ToolLang.Get("Typewriter", "Печать"), ToolLang.Get("Enable character-by-character typewriter effect.", "Включить эффект посимвольной печати (Печатная машинка).")), GUILayout.Width(85));

                if (line.UseTypewriter)
                {
                    EditorGUIUtility.labelWidth = 35;
                    line.BaseSpeed = EditorGUILayout.FloatField(new GUIContent(ToolLang.Get("Spd:", "Скор:"), ToolLang.Get("Speed in characters per second.", "Базовая скорость: символов в секунду.")), line.BaseSpeed, GUILayout.Width(75));
                    GUILayout.FlexibleSpace();
                    line.UseCustomPacing = GUILayout.Toggle(line.UseCustomPacing, new GUIContent("📈 " + ToolLang.Get("Curve", "Кривая"), ToolLang.Get("Use an animation curve to dynamically change typing speed.", "Использовать кривую для динамического изменения скорости печати.")), GUILayout.Width(80));
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }

                if (GUILayout.Button(new GUIContent($"✎ {ToolLang.Get("Editor", "Редакт.")}", ToolLang.Get("Open full text editor window.", "Открыть полноценный редактор текста.")), EditorStyles.miniButton, GUILayout.Width(65)))
                    NovellaTextEditorWindow.OpenWindow(line, fontSizeProp, _currentTree, () => { selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); });
                GUILayout.EndHorizontal();

                if (line.UseTypewriter && line.UseCustomPacing)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(new GUIContent(ToolLang.Get("Speed Multiplier:", "Множитель:"), ToolLang.Get("Left side is start of text, right side is end.", "Левая часть — начало текста, правая — конец.")), EditorStyles.miniLabel, GUILayout.Width(90));
                    line.PacingCurve = EditorGUILayout.CurveField(line.PacingCurve, Color.green, new Rect(0, 0, 1, 2), GUILayout.Height(20));
                    GUILayout.EndHorizontal();
                }

                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change Typewriter Settings"); _onMarkUnsaved?.Invoke(); }
                GUILayout.EndVertical();

                GUILayout.EndVertical();

                GUILayout.Space(15);
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

        private void SyncSceneWithDialogueNode(NovellaNodeData nodeData)
        {
            if (nodeData.NodeType != ENodeType.Dialogue && nodeData.NodeType != ENodeType.Event) { ClearScene(); return; }

            Transform charsContainer = null;
            var sceneManagerPlayer = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
            if (sceneManagerPlayer != null) charsContainer = sceneManagerPlayer.CharactersContainer;

            List<NovellaSceneEntity> entities = new List<NovellaSceneEntity>();
            if (charsContainer != null)
            {
                entities = charsContainer.GetComponentsInChildren<NovellaSceneEntity>(true).ToList();
            }
            else
            {
                entities = UnityEngine.Object.FindObjectsByType<NovellaSceneEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
            }

            string currentSpeakerID = "";
            DialogueLine activeLine = null;
            if (nodeData.DialogueLines.Count > 0 && _activePreviewLineIndex >= 0 && _activePreviewLineIndex < nodeData.DialogueLines.Count)
            {
                activeLine = nodeData.DialogueLines[_activePreviewLineIndex];
                if (activeLine.Speaker != null) currentSpeakerID = activeLine.Speaker.CharacterID;
            }

            Dictionary<string, CharacterInDialogue> charConfigs = new Dictionary<string, CharacterInDialogue>();
            foreach (var ac in nodeData.ActiveCharacters)
            {
                if (ac.CharacterAsset != null) charConfigs[ac.CharacterAsset.CharacterID] = ac;
            }

            foreach (var line in nodeData.DialogueLines)
            {
                if (line.Speaker != null && !charConfigs.ContainsKey(line.Speaker.CharacterID))
                {
                    charConfigs[line.Speaker.CharacterID] = new CharacterInDialogue
                    {
                        CharacterAsset = line.Speaker,
                        Plane = ECharacterPlane.Speaker,
                        Scale = 1f,
                        Emotion = "Default",
                        PosX = 0f,
                        PosY = 0f
                    };
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
                }

                var renderer = entity.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Sprite targetSprite = config.CharacterAsset.DefaultSprite;
                    string emotionToSet = config.Emotion;

                    if (activeLine != null && activeLine.Speaker != null && config.CharacterAsset.CharacterID == activeLine.Speaker.CharacterID)
                    {
                        emotionToSet = activeLine.Mood;
                        if (activeLine.CustomizeSpeakerLayout)
                        {
                            renderer.sortingOrder = (int)activeLine.SpeakerPlane;
                            entity.transform.localScale = Vector3.one * activeLine.SpeakerScale;
                            entity.transform.localPosition = new Vector3(activeLine.SpeakerPosX, activeLine.SpeakerPosY, 0);
                        }
                        else
                        {
                            renderer.sortingOrder = (int)ECharacterPlane.Speaker;
                            entity.transform.localScale = Vector3.one * config.Scale;
                            entity.transform.localPosition = new Vector3(config.PosX, config.PosY, 0);
                        }
                    }
                    else
                    {
                        renderer.sortingOrder = (int)config.Plane;
                        entity.transform.localScale = Vector3.one * config.Scale;
                        entity.transform.localPosition = new Vector3(config.PosX, config.PosY, 0);
                    }

                    if (emotionToSet != "Default")
                    {
                        var emotionData = config.CharacterAsset.Emotions.FirstOrDefault(e => e.EmotionName == emotionToSet);
                        if (emotionData.EmotionSprite != null) targetSprite = emotionData.EmotionSprite;
                    }

                    renderer.sprite = targetSprite;
                }

                bool shouldHide = (activeLine != null && activeLine.HideSpeakerSprite && config.CharacterAsset.CharacterID == currentSpeakerID);
                entity.gameObject.SetActive(!shouldHide);
            }

            bool sceneChanged = false;
            foreach (var entity in entities)
            {
                if (!charConfigs.ContainsKey(entity.LinkedNodeID))
                {
                    Undo.DestroyObjectImmediate(entity.gameObject);
                    sceneChanged = true;
                }
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