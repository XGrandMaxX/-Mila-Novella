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

        private static List<CharacterInDialogue> _sceneClipboard = new List<CharacterInDialogue>();

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
                SyncSceneWithDialogueNode(nodeData);
            }

            _serializedObject.Update();
            int index = _currentTree.Nodes.FindIndex(n => n != null && n.NodeID == nodeData.NodeID);
            if (index == -1) { EndLayout(); return; }
            SerializedProperty nodeProp = _serializedObject.FindProperty("Nodes").GetArrayElementAtIndex(index);
            float originalLabelWidth = EditorGUIUtility.labelWidth;

            GUILayout.Label(ToolLang.Get("General Data", "Основные данные"), EditorStyles.boldLabel);
            EditorGUIUtility.labelWidth = 120;
            EditorGUI.BeginDisabledGroup(true); EditorGUILayout.TextField(ToolLang.Get("Internal ID", "Внутренний ID"), nodeData.NodeID); EditorGUI.EndDisabledGroup();

            EditorGUI.BeginChangeCheck();
            string newTitle = EditorGUILayout.TextField(ToolLang.Get("Node Name", "Имя Ноды"), nodeData.NodeTitle);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Change Node Name"); nodeData.NodeTitle = newTitle; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }

            GUILayout.BeginHorizontal();
            if (nodeData.NodeType != ENodeType.End && nodeData.NodeType != ENodeType.Character && nodeData.NodeType != ENodeType.Branch)
            {
                EditorGUI.BeginChangeCheck(); Color newColor = EditorGUILayout.ColorField(GUIContent.none, nodeData.NodeCustomColor, false, true, false, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Color"); nodeData.NodeCustomColor = newColor; selectedNodeView.RefreshVisuals(); _onMarkUnsaved?.Invoke(); }
            }
            if (nodeData.NodeType == ENodeType.Branch) GUILayout.Label(ToolLang.Get("🎨 Color is locked for this type", "🎨 Цвет заблокирован для этого типа"), EditorStyles.centeredGreyMiniLabel);

            EditorGUI.BeginChangeCheck(); bool isPinnedRequest = GUILayout.Toggle(nodeData.IsPinned, $"📌 {ToolLang.Get("Pin Node", "Закрепить")}", EditorStyles.miniButton, GUILayout.Height(20));
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Pin"); foreach (var n in _currentTree.Nodes) if (n != null) n.IsPinned = false; nodeData.IsPinned = isPinnedRequest; EditorApplication.delayCall += () => _graphView?.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals()); _onMarkUnsaved?.Invoke(); }
            GUILayout.EndHorizontal();

            if (nodeData.NodeType == ENodeType.End) { EndLayout(); return; }

            if (nodeData.NodeType == ENodeType.Dialogue)
            {
                DrawSectionHeader("⚡", ToolLang.Get("AUTOMATION", "АВТОМАТИЗАЦИЯ"));
                GUILayout.BeginHorizontal();

                GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
                if (GUILayout.Button(ToolLang.Get("Push to Next Nodes", "Протянуть до конца"), GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Propagate Layout", "Синхронизация цепочки"),
                        ToolLang.Get("This will overwrite character setups in all following nodes until a branch. Proceed?",
                                     "Это перезапишет расстановку персонажей во всех следующих нодах до первой развилки. Продолжить?"),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        _graphView.PropagateScene(nodeData);
                    }
                }

                GUI.backgroundColor = Color.white;
                if (GUILayout.Button(ToolLang.Get("Copy Layout", "Копир. визуал"), GUILayout.Height(25)))
                {
                    _sceneClipboard = nodeData.ActiveCharacters.Select(c => new CharacterInDialogue
                    {
                        CharacterAsset = c.CharacterAsset,
                        Plane = c.Plane,
                        Scale = c.Scale,
                        Emotion = c.Emotion,
                        PosX = c.PosX,
                        PosY = c.PosY
                    }).ToList();
                    _window.ShowNotification(new GUIContent(ToolLang.Get("Characters layout copied!", "Расстановка персонажей скопирована!")));
                }

                EditorGUI.BeginDisabledGroup(_sceneClipboard.Count == 0);
                if (GUILayout.Button(ToolLang.Get("Paste Layout", "Вставить визуал"), GUILayout.Height(25)))
                {
                    Undo.RecordObject(_currentTree, "Paste Character Layout");
                    nodeData.ActiveCharacters.Clear();
                    foreach (var c in _sceneClipboard) nodeData.ActiveCharacters.Add(new CharacterInDialogue
                    {
                        CharacterAsset = c.CharacterAsset,
                        Plane = c.Plane,
                        Scale = c.Scale,
                        Emotion = c.Emotion,
                        PosX = c.PosX,
                        PosY = c.PosY
                    });
                    GUI.changed = true;
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();
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
                    bool canDelete = nodeData.Choices.Count > 2; EditorGUI.BeginDisabledGroup(!canDelete);
                    GUI.backgroundColor = canDelete ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                    if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(60))) { Undo.RecordObject(_currentTree, "Remove Choice"); nodeData.Choices.RemoveAt(i); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); _onMarkUnsaved?.Invoke(); GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); break; }
                    GUI.backgroundColor = Color.white; EditorGUI.EndDisabledGroup(); GUILayout.EndHorizontal();
                    GUILayout.Space(5);

                    EditorGUI.BeginChangeCheck();
                    string currentText = choice.LocalizedText.GetText(_window.PreviewLanguage);
                    string newText = EditorGUILayout.TextField("Text", currentText);
                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Choice Text"); choice.LocalizedText.SetText(_window.PreviewLanguage, newText); _onMarkUnsaved?.Invoke(); }

                    GUILayout.Space(5); GUILayout.EndVertical(); GUILayout.Space(5);
                }

                if (nodeData.Choices.Count >= 4 && !nodeData.UnlockChoiceLimit)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Default limit of 4 choices reached.", "Достигнут базовый лимит (4 выбора)."), MessageType.Info);
                    if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять ограничение"), EditorStyles.miniButton, GUILayout.Height(25))) { Undo.RecordObject(_currentTree, "Unlock Choice"); nodeData.UnlockChoiceLimit = true; _onMarkUnsaved?.Invoke(); }
                }
                else { if (GUILayout.Button($"+ {ToolLang.Get("Add Choice", "Добавить выбор")}", EditorStyles.miniButton, GUILayout.Height(30))) { Undo.RecordObject(_currentTree, "Add Choice"); nodeData.Choices.Add(new NovellaChoice()); _serializedObject.Update(); selectedNodeView.DrawBranchPorts(); _onMarkUnsaved?.Invoke(); } }
                EndLayout(); return;
            }

            DrawSectionHeader("👥", ToolLang.Get("Scene Characters", "Персонажи сцены"));
            if (GUILayout.Button($"🛠️ {ToolLang.Get("Character Editor", "Редактор Персонажей")}", EditorStyles.miniButton, GUILayout.Height(25))) NovellaCharacterEditor.OpenWindow();
            GUILayout.Space(15);

            for (int i = 0; i < nodeData.ActiveCharacters.Count; i++)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                var activeChar = nodeData.ActiveCharacters[i];
                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                string charName = activeChar.CharacterAsset != null ? activeChar.CharacterAsset.name : ToolLang.Get("Select...", "Выбрать...");
                if (GUILayout.Button($"👤 {charName}", EditorStyles.toolbarDropDown, GUILayout.Width(150)))
                {
                    NovellaCharacterSelectorWindow.ShowWindow((selectedChar) =>
                    {
                        Undo.RecordObject(_currentTree, "Change Character Asset");
                        activeChar.CharacterAsset = selectedChar;
                        selectedNodeView.RefreshVisuals();
                        SyncSceneWithDialogueNode(nodeData);
                        _onMarkUnsaved?.Invoke();
                        _window.Repaint();
                    });
                }

                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button(ToolLang.Get("Delete", "Удалить"), EditorStyles.toolbarButton, GUILayout.Width(60))) { Undo.RecordObject(_currentTree, "Remove Character"); nodeData.ActiveCharacters.RemoveAt(i); GUI.changed = true; GUI.backgroundColor = Color.white; break; }
                GUI.backgroundColor = Color.white; GUILayout.EndHorizontal(); GUILayout.Space(8); EditorGUIUtility.labelWidth = 65;

                if (activeChar.CharacterAsset != null)
                {
                    GUILayout.Space(5); EditorGUI.BeginChangeCheck(); GUILayout.BeginHorizontal();

                    activeChar.Plane = (ECharacterPlane)EditorGUILayout.EnumPopup(ToolLang.Get("Plane:", "План:"), activeChar.Plane); GUILayout.Space(10);
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

            if (GUILayout.Button($"+ {ToolLang.Get("Add Character", "Добавить персонажа")}", EditorStyles.miniButton, GUILayout.Height(30)))
            { Undo.RecordObject(_currentTree, "Add Character"); nodeData.ActiveCharacters.Add(new CharacterInDialogue()); GUI.changed = true; }

            if (GUI.changed)
            {
                selectedNodeView.RefreshVisuals();
                SyncSceneWithDialogueNode(nodeData);
                _onMarkUnsaved?.Invoke();
            }

            DrawSectionHeader("🎭", ToolLang.Get("Speaker Settings", "Настройки Спикера"));
            EditorGUIUtility.labelWidth = 80; EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Speaker:", "Спикер:"), GUILayout.Width(80));
            string speakerName = nodeData.Speaker != null ? nodeData.Speaker.name : ToolLang.Get("None", "Нет");
            if (GUILayout.Button($"🗣 {speakerName}", EditorStyles.popup))
            {
                NovellaCharacterSelectorWindow.ShowWindow((selectedChar) =>
                {
                    Undo.RecordObject(_currentTree, "Change Speaker");
                    nodeData.Speaker = selectedChar;
                    selectedNodeView.RefreshVisuals();
                    _onMarkUnsaved?.Invoke();
                    _window.Repaint();
                });
            }
            GUILayout.EndHorizontal();

            if (nodeData.Speaker != null) { List<string> moodList = new List<string> { "Default" }; if (nodeData.Speaker.Emotions != null) moodList.AddRange(nodeData.Speaker.Emotions.Select(e => e.EmotionName)); int moodIdx = moodList.IndexOf(nodeData.Mood); if (moodIdx == -1) moodIdx = 0; nodeData.Mood = moodList[EditorGUILayout.Popup(ToolLang.Get("Mood:", "Эмоция:"), moodIdx, moodList.ToArray())]; }
            if (EditorGUI.EndChangeCheck()) Undo.RecordObject(_currentTree, "Set Speaker");
            EditorGUIUtility.labelWidth = originalLabelWidth;

            DrawSectionHeader("💬", $"{ToolLang.Get("Dialogue Line", "Текст Реплики")} ({_window.PreviewLanguage})");
            SerializedProperty fontSizeProp = nodeProp.FindPropertyRelative("FontSize");

            EditorGUI.BeginChangeCheck();
            GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            string currentPhrase = nodeData.LocalizedPhrase.GetText(_window.PreviewLanguage);
            string newPhrase = EditorGUILayout.TextArea(currentPhrase, textAreaStyle, GUILayout.Height(80));
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_currentTree, "Edit Phrase"); nodeData.LocalizedPhrase.SetText(_window.PreviewLanguage, newPhrase); _onMarkUnsaved?.Invoke(); }

            GUILayout.Space(15);
            if (GUILayout.Button($"✎ {ToolLang.Get("OPEN TEXT EDITOR", "ОТКРЫТЬ РЕДАКТОР ТЕКСТА")}", EditorStyles.miniButton, GUILayout.Height(35)))
                NovellaTextEditorWindow.OpenWindow(nodeData.LocalizedPhrase, fontSizeProp, _currentTree, () => _onMarkUnsaved?.Invoke());

            if (GUI.changed) { _serializedObject.ApplyModifiedProperties(); if (_graphView != null) { _graphView.SyncGraphToData(); _onMarkUnsaved?.Invoke(); } }
            EndLayout();
        }

        private void DrawSectionHeader(string icon, string title) { GUILayout.Space(20); var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 }; if (EditorGUIUtility.isProSkin) headerStyle.normal.textColor = new Color(0.6f, 0.8f, 1f); else headerStyle.normal.textColor = new Color(0.2f, 0.4f, 0.6f); GUILayout.Label($"{icon} {title}", headerStyle); GUILayout.Space(5); }
        private void DrawStartNodeHelp() { GUILayout.Label(ToolLang.Get("🟢 START NODE", "🟢 СТАРТОВАЯ НОДА"), EditorStyles.boldLabel); EditorGUILayout.HelpBox(ToolLang.Get("Scene starting point.", "Стартовая точка сцены."), MessageType.Info); }
        private void EndLayout() { GUILayout.EndVertical(); GUILayout.Space(20); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); }

        private void SyncSceneWithDialogueNode(NovellaNodeData nodeData)
        {
            if (nodeData.NodeType != ENodeType.Dialogue && nodeData.NodeType != ENodeType.Event) { ClearScene(); return; }

            var entities = UnityEngine.Object.FindObjectsByType<NovellaSceneEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);

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
                    if (activeChar.Emotion != "Default")
                    {
                        var emotionData = activeChar.CharacterAsset.Emotions.FirstOrDefault(e => e.EmotionName == activeChar.Emotion);
                        if (emotionData.EmotionSprite != null) targetSprite = emotionData.EmotionSprite;
                    }
                    renderer.sprite = targetSprite;
                    renderer.sortingOrder = activeChar.Plane == ECharacterPlane.Front ? 10 : (int)activeChar.Plane;
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
            if (sceneChanged && !Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}