using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using System;
using System.Linq;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    public class NovellaDialogueEditorWindow : EditorWindow
    {
        private NovellaTree _tree;
        private NovellaNodeData _nodeData;
        private Action<int> _onLineSelected;
        private Action _onMarkUnsaved;
        private Action _onRepaintRequest;

        private Vector2 _storyboardScrollPos;
        private Vector2 _settingsScrollPos;
        private int _activeLineIndex = 0;
        private NovellaLocalizationSettings _locSettings;
        private string _previewLanguage = "EN";

        private static DialogueLine _clipboardLine = null;

        public static void OpenWindow(NovellaTree tree, NovellaNodeData nodeData, string lang, Action<int> onLineSelected, Action onMarkUnsaved, Action onRepaintRequest)
        {
            var win = GetWindow<NovellaDialogueEditorWindow>(ToolLang.Get("Dialogue Storyboard", "Раскадровка Диалогов"));
            win._tree = tree;
            win._nodeData = nodeData;
            win._previewLanguage = lang;
            win._onLineSelected = onLineSelected;
            win._onMarkUnsaved = onMarkUnsaved;
            win._onRepaintRequest = onRepaintRequest;

            win._locSettings = NovellaLocalizationSettings.GetOrCreateSettings();
            win.minSize = new Vector2(1100, 750);
            win.ShowUtility();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            GUI.FocusControl(null);
            _onMarkUnsaved?.Invoke();
            _onRepaintRequest?.Invoke();
            _onLineSelected?.Invoke(_activeLineIndex);
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnGUI()
        {
            if (_tree == null || _nodeData == null) { Close(); return; }

            ProcessKeyboardNavigation();
            DrawHeader();

            _storyboardScrollPos = GUILayout.BeginScrollView(_storyboardScrollPos, GUILayout.ExpandHeight(true));
            DrawStoryboard();
            GUILayout.EndScrollView();

            Rect divider = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(3));
            EditorGUI.DrawRect(divider, new Color(0.15f, 0.15f, 0.15f, 1f));

            _settingsScrollPos = GUILayout.BeginScrollView(_settingsScrollPos, GUILayout.Height(350));
            DrawBottomSettingsPanel();
            GUILayout.EndScrollView();
        }

        private void ProcessKeyboardNavigation()
        {
            if (Event.current.type == EventType.KeyDown)
            {
                bool isActionKey = Event.current.control || Event.current.command;

                if (Event.current.keyCode == KeyCode.LeftArrow)
                {
                    if (_activeLineIndex > 0)
                    {
                        _activeLineIndex--;
                        TriggerLiveSync(true);
                    }
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.RightArrow)
                {
                    if (_activeLineIndex < _nodeData.DialogueLines.Count - 1)
                    {
                        _activeLineIndex++;
                        TriggerLiveSync(true);
                    }
                    Event.current.Use();
                }
                // === КОПИРОВАНИЕ (CTRL+C) ===
                else if (Event.current.keyCode == KeyCode.C && isActionKey)
                {
                    if (_activeLineIndex >= 0 && _activeLineIndex < _nodeData.DialogueLines.Count)
                    {
                        // Глубокое копирование через JSON
                        string json = JsonUtility.ToJson(_nodeData.DialogueLines[_activeLineIndex]);
                        _clipboardLine = JsonUtility.FromJson<DialogueLine>(json);

                        this.ShowNotification(new GUIContent(ToolLang.Get("Line Copied!", "Реплика скопирована!")));
                    }
                    Event.current.Use();
                }
                // === ВСТАВКА (CTRL+V) ===
                else if (Event.current.keyCode == KeyCode.V && isActionKey)
                {
                    if (_clipboardLine != null && _activeLineIndex >= 0 && _activeLineIndex < _nodeData.DialogueLines.Count)
                    {
                        Undo.RecordObject(_tree, "Paste Dialogue Line");

                        // Вставляем данные поверх текущей реплики
                        string json = JsonUtility.ToJson(_clipboardLine);
                        JsonUtility.FromJsonOverwrite(json, _nodeData.DialogueLines[_activeLineIndex]);

                        EditorUtility.SetDirty(_tree);
                        _onMarkUnsaved?.Invoke();
                        TriggerLiveSync(true);

                        this.ShowNotification(new GUIContent(ToolLang.Get("Line Pasted!", "Реплика вставлена!")));
                    }
                    Event.current.Use();
                }
            }
        }

        private void TriggerLiveSync(bool clearFocus = false)
        {
            if (clearFocus) GUI.FocusControl(null);
            _onLineSelected?.Invoke(_activeLineIndex);
            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"🎞 {ToolLang.Get("Storyboard Node:", "Раскадровка Ноды:")} {(_nodeData.NodeTitle == "" ? _nodeData.NodeID : _nodeData.NodeTitle)}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(ToolLang.Get("Use Left/Right Arrows to navigate", "Стрелки Влево/Вправо для быстрой навигации"), EditorStyles.miniLabel);
            GUILayout.Space(20);
            GUILayout.Label($"{ToolLang.Get("Lang:", "Язык:")} {_previewLanguage}", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            if (GetAvailableSpeakers().Count == 0)
                EditorGUILayout.HelpBox(ToolLang.Get("No characters in Scene Layout! Assign characters in the Inspector to use speakers.", "Массовка пуста! Добавьте персонажей в 'Scene Layout' в инспекторе, чтобы использовать их здесь."), MessageType.Warning);
        }

        private List<NovellaCharacter> GetAvailableSpeakers()
        {
            if (_nodeData.ActiveCharacters == null) return new List<NovellaCharacter>();
            return _nodeData.ActiveCharacters.Where(ac => ac.CharacterAsset != null).Select(ac => ac.CharacterAsset).Distinct().ToList();
        }

        private void DrawStoryboard()
        {
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);

            var activeAssets = GetAvailableSpeakers();
            List<string> speakerOptions = new List<string> { ToolLang.Get("-- No Speaker --", "-- Без спикера --") };
            speakerOptions.AddRange(activeAssets.Select(a => a.name));

            SerializedObject treeObj = new SerializedObject(_tree);
            int nodeIndex = _tree.Nodes.IndexOf(_nodeData);
            SerializedProperty fontSizeProp = nodeIndex != -1 ? treeObj.FindProperty("Nodes").GetArrayElementAtIndex(nodeIndex).FindPropertyRelative("FontSize") : null;

            int actionDelete = -1, actionMoveLeft = -1, actionMoveRight = -1;

            for (int i = 0; i < _nodeData.DialogueLines.Count; i++)
            {
                var line = _nodeData.DialogueLines[i];

                if (line.Speaker != null && !activeAssets.Contains(line.Speaker))
                {
                    line.Speaker = null;
                    line.Mood = "Default";
                    TriggerLiveSync(true);
                }

                bool isSelected = _activeLineIndex == i;

                GUI.backgroundColor = isSelected ? new Color(1f, 0.95f, 0.8f) : new Color(0.85f, 0.85f, 0.85f);
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(220), GUILayout.ExpandHeight(true));
                GUI.backgroundColor = Color.white;

                Rect cardRect = EditorGUILayout.BeginVertical();

                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUIStyle numStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                numStyle.normal.textColor = isSelected ? new Color(0.9f, 0.5f, 0f) : new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label($"#{i + 1}", numStyle, GUILayout.Width(30));

                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(25)) && i > 0) actionMoveLeft = i;
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(25)) && i < _nodeData.DialogueLines.Count - 1) actionMoveRight = i;

                GUILayout.FlexibleSpace();
                bool canDeleteLine = _nodeData.DialogueLines.Count > 1;
                EditorGUI.BeginDisabledGroup(!canDeleteLine);
                GUI.backgroundColor = canDeleteLine ? new Color(0.9f, 0.4f, 0.4f) : Color.grey;
                if (GUILayout.Button("✖", EditorStyles.toolbarButton, GUILayout.Width(30))) actionDelete = i;
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                // Имя Спикера с поддержкой Rich Text
                GUILayout.Space(5);
                string displayName = line.Speaker != null ? line.Speaker.name : ToolLang.Get("No Speaker", "Без спикера");
                if (line.HideSpeakerName && !string.IsNullOrEmpty(line.CustomName)) displayName = line.CustomName;

                GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16, richText = true };
                nameStyle.normal.textColor = isSelected ? (line.Speaker != null ? line.Speaker.ThemeColor : Color.black) : new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Label($"{ToolLang.Get("Speaker:", "Спикер:")} {displayName}", nameStyle);

                if (isSelected)
                {
                    GUIStyle activeHintStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.2f, 0.6f, 0.2f) } };
                    GUILayout.Label(ToolLang.Get($"Editing line #{i + 1}", $"Настраивается реплика #{i + 1}"), activeHintStyle);
                }
                else GUILayout.Space(14);

                GUILayout.Space(5);

                // Отрисовка Спрайта с золотой рамкой
                Rect imgRect = GUILayoutUtility.GetRect(200, 230, GUILayout.ExpandHeight(true));

                if (isSelected)
                {
                    Rect goldBorder = new Rect(imgRect.x - 3, imgRect.y - 3, imgRect.width + 6, imgRect.height + 6);
                    EditorGUI.DrawRect(goldBorder, new Color(1f, 0.85f, 0f, 1f));
                }

                EditorGUI.DrawRect(imgRect, new Color(0.15f, 0.15f, 0.15f, 1f));

                Sprite targetSprite = null;
                if (line.Speaker != null && !line.HideSpeakerSprite)
                {
                    targetSprite = line.Speaker.DefaultSprite;
                    if (line.Mood != "Default" && line.Speaker.Emotions != null)
                    {
                        int emotionIndex = line.Speaker.Emotions.FindIndex(e => e.EmotionName == line.Mood);
                        if (emotionIndex != -1 && line.Speaker.Emotions[emotionIndex].EmotionSprite != null)
                            targetSprite = line.Speaker.Emotions[emotionIndex].EmotionSprite;
                    }
                }

                if (targetSprite != null && targetSprite.texture != null)
                {
                    if (!isSelected) GUI.color = new Color(0.6f, 0.6f, 0.6f, 1f);
                    GUI.DrawTexture(imgRect, targetSprite.texture, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                    GUIStyle centeredStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleCenter };
                    GUI.Label(imgRect, line.HideSpeakerSprite ? ToolLang.Get("👻 Hidden", "👻 Скрыт") : ToolLang.Get("No Sprite", "Нет спрайта"), centeredStyle);
                    GUI.color = Color.white;
                }

                // Текст
                GUILayout.Space(10);
                GUILayout.Label($"✏ {ToolLang.Get("Text", "Текст")} [{_previewLanguage}]:", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                string newPhrase = EditorGUILayout.TextArea(line.LocalizedPhrase.GetText(_previewLanguage), new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 }, GUILayout.Height(75));

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_tree, "Edit Phrase");
                    line.LocalizedPhrase.SetText(_previewLanguage, newPhrase);
                    EditorUtility.SetDirty(_tree);
                    TriggerLiveSync(false); // Сохраняем фокус в текстовом поле
                }

                GUILayout.Space(5);
                if (GUILayout.Button(new GUIContent($"📝 {ToolLang.Get("Full Text Editor", "Полный редактор текста")}"), EditorStyles.miniButton))
                {
                    NovellaTextEditorWindow.OpenWindow(line, fontSizeProp, _tree, () => { _onMarkUnsaved?.Invoke(); _onRepaintRequest?.Invoke(); });
                }

                EditorGUILayout.EndVertical();

                if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
                {
                    _activeLineIndex = i;
                    TriggerLiveSync(true); // Сброс фокуса при клике
                    Event.current.Use();
                }

                GUILayout.Space(5);
                GUILayout.EndVertical();

                GUILayout.Space(10);
            }

            ProcessDeferredActions(actionDelete, actionMoveLeft, actionMoveRight);

            GUILayout.BeginVertical(GUILayout.Width(130), GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();

            if (_nodeData.DialogueLines.Count >= 10 && !_nodeData.UnlockDialogueLimit)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Limit reached (10 lines). We recommend splitting dialogues into multiple nodes.", "Достигнут лимит (10 реплик). Рекомендуем разбивать длинные диалоги на несколько нод."), MessageType.Warning);
                if (GUILayout.Button(ToolLang.Get("🔓 Remove Limit", "🔓 Снять лимит"), EditorStyles.miniButton, GUILayout.Height(30)))
                {
                    Undo.RecordObject(_tree, "Unlock Limit");
                    _nodeData.UnlockDialogueLimit = true;
                    EditorUtility.SetDirty(_tree);
                    _onMarkUnsaved?.Invoke();
                }
            }
            else
            {
                if (GUILayout.Button("+\n" + ToolLang.Get("Add Line", "Добавить\nреплику"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold }, GUILayout.Height(80)))
                {
                    Undo.RecordObject(_tree, "Add Line");
                    var newLine = new DialogueLine();
                    if (_nodeData.DialogueLines.Count > 0) newLine.Speaker = _nodeData.DialogueLines.Last().Speaker;
                    if (newLine.Speaker != null && !activeAssets.Contains(newLine.Speaker)) newLine.Speaker = null;
                    newLine.FrameScale = 1f; newLine.SpeakerScale = 1f;

                    _nodeData.DialogueLines.Add(newLine);
                    _activeLineIndex = _nodeData.DialogueLines.Count - 1;
                    EditorUtility.SetDirty(_tree);
                    TriggerLiveSync(true);
                    _storyboardScrollPos.x = float.MaxValue;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.Space(20);
            GUILayout.EndHorizontal();
        }

        private void ProcessDeferredActions(int delete, int moveLeft, int moveRight)
        {
            if (delete != -1)
            {
                Undo.RecordObject(_tree, "Remove Line");
                _nodeData.DialogueLines.RemoveAt(delete);
                _activeLineIndex = Mathf.Clamp(_activeLineIndex, 0, _nodeData.DialogueLines.Count - 1);
                EditorUtility.SetDirty(_tree);
                TriggerLiveSync(true);
            }
            else if (moveLeft != -1)
            {
                Undo.RecordObject(_tree, "Move Line Left");
                var temp = _nodeData.DialogueLines[moveLeft];
                _nodeData.DialogueLines[moveLeft] = _nodeData.DialogueLines[moveLeft - 1];
                _nodeData.DialogueLines[moveLeft - 1] = temp;
                if (_activeLineIndex == moveLeft) _activeLineIndex = moveLeft - 1;
                else if (_activeLineIndex == moveLeft - 1) _activeLineIndex = moveLeft;
                EditorUtility.SetDirty(_tree);
                TriggerLiveSync(true);
            }
            else if (moveRight != -1)
            {
                Undo.RecordObject(_tree, "Move Line Right");
                var temp = _nodeData.DialogueLines[moveRight];
                _nodeData.DialogueLines[moveRight] = _nodeData.DialogueLines[moveRight + 1];
                _nodeData.DialogueLines[moveRight + 1] = temp;
                if (_activeLineIndex == moveRight) _activeLineIndex = moveRight + 1;
                else if (_activeLineIndex == moveRight + 1) _activeLineIndex = moveRight;
                EditorUtility.SetDirty(_tree);
                TriggerLiveSync(true);
            }
        }

        private void DrawBottomSettingsPanel()
        {
            if (_activeLineIndex < 0 || _activeLineIndex >= _nodeData.DialogueLines.Count) return;
            var line = _nodeData.DialogueLines[_activeLineIndex];

            GUILayout.BeginHorizontal();
            GUILayout.Space(10);

            // ================= КОЛОНКА 1: НАСТРОЙКИ СПИКЕРА И ЭМОЦИИ =================
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(310));

            GUILayout.BeginHorizontal();
            GUILayout.Label("🎭 " + ToolLang.Get("Speaker Settings", "Настройки спикера"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↺ " + ToolLang.Get("Reset", "Сброс"), EditorStyles.miniButton))
            {
                Undo.RecordObject(_tree, "Reset Line Settings");
                line.HideSpeakerName = false; line.HideSpeakerSprite = false; line.CustomizeSpeakerLayout = false;
                line.CustomizeFrameLayout = false; line.OverrideDialogueFrame = null;
                line.FrameScale = 1f; line.SpeakerScale = 1f;
                line.FramePosX = 0f; line.FramePosY = 0f; line.SpeakerPosX = 0f; line.SpeakerPosY = 0f;
                line.DelayBefore = 0; line.FontSize = 0; line.UseTypewriter = true; line.UseCustomPacing = false;
                EditorUtility.SetDirty(_tree);
                TriggerLiveSync(true);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            var activeAssets = GetAvailableSpeakers();
            List<string> speakerOptions = new List<string> { ToolLang.Get("-- No Speaker --", "-- Без спикера --") };
            speakerOptions.AddRange(activeAssets.Select(a => a.name));

            EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal();
            GUILayout.Label("👤 " + ToolLang.Get("Speaker:", "Спикер:"), GUILayout.Width(70));

            int currentSpeakerIdx = (line.Speaker != null && activeAssets.Contains(line.Speaker)) ? activeAssets.IndexOf(line.Speaker) + 1 : 0;
            int newSpeakerIdx = EditorGUILayout.Popup(currentSpeakerIdx, speakerOptions.ToArray());

            if (line.Speaker != null)
            {
                if (GUILayout.Button("🛠", EditorStyles.miniButton, GUILayout.Width(25)))
                    NovellaCharacterEditor.OpenWithCharacter(line.Speaker);
            }
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_tree, "Change Speaker");
                line.Speaker = newSpeakerIdx == 0 ? null : activeAssets[newSpeakerIdx - 1];
                line.Mood = "Default";
                EditorUtility.SetDirty(_tree);
                TriggerLiveSync(true);
            }

            if (line.Speaker != null)
            {
                EditorGUI.BeginChangeCheck();
                List<string> moodList = new List<string> { "Default" };
                if (line.Speaker.Emotions != null) moodList.AddRange(line.Speaker.Emotions.Select(e => e.EmotionName));

                int moodIdx = moodList.IndexOf(line.Mood);
                if (moodIdx == -1) moodIdx = 0;

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("😃 " + ToolLang.Get("Emotion:", "Эмоция:"), GUILayout.Width(70));
                line.Mood = moodList[EditorGUILayout.Popup(moodIdx, moodList.ToArray())];
                GUILayout.EndHorizontal();

                if (line.Speaker.Emotions == null || line.Speaker.Emotions.Count < 1)
                    GUILayout.Label(ToolLang.Get("💡 Add emotions in Character Editor.", "💡 Эмоции добавляются в редакторе персонажа."), EditorStyles.centeredGreyMiniLabel);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_tree, "Change Mood");
                    EditorUtility.SetDirty(_tree);
                    TriggerLiveSync(false);
                }

                GUILayout.Space(10);
                GUILayout.Label(ToolLang.Get("Visual Overrides", "Переопределение визуала на сцене"), EditorStyles.miniBoldLabel);

                GUILayout.BeginHorizontal();
                line.HideSpeakerName = GUILayout.Toggle(line.HideSpeakerName, new GUIContent(" 👁 " + ToolLang.Get("Fake Name", "Фейк Имя"), "Заменяет имя в окне UI."), EditorStyles.miniButton);
                line.HideSpeakerSprite = GUILayout.Toggle(line.HideSpeakerSprite, new GUIContent(" 👻 " + ToolLang.Get("Hide Sprite", "Скрыть Спрайт"), "Убирает персонажа со сцены."), EditorStyles.miniButton);
                line.CustomizeSpeakerLayout = GUILayout.Toggle(line.CustomizeSpeakerLayout, new GUIContent(" ⚙ " + ToolLang.Get("Override Pos", "Сдвиг Поз."), "Изменить координаты/размер."), EditorStyles.miniButton);
                GUILayout.EndHorizontal();

                if (line.HideSpeakerName)
                {
                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Name:", "Имя:"), GUILayout.Width(40));
                    line.CustomName = EditorGUILayout.TextField(line.CustomName, GUILayout.ExpandWidth(true));
                    GUILayout.Label(ToolLang.Get("Color:", "Цвет:"), GUILayout.Width(40));
                    line.CustomNameColor = EditorGUILayout.ColorField(line.CustomNameColor, GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                    GUILayout.Label(ToolLang.Get("💡 Replaces name. Rich Text (e.g. <b>) supported.", "💡 Заменяет имя. Поддерживает Rich Text теги."), EditorStyles.miniLabel);
                }

                if (line.HideSpeakerSprite)
                {
                    GUILayout.Space(5);
                    GUILayout.Label(ToolLang.Get("💡 Character will be hidden from the scene.", "💡 Спрайт персонажа временно исчезнет со сцены."), EditorStyles.miniLabel);
                }

                if (line.CustomizeSpeakerLayout)
                {
                    GUILayout.Space(5);
                    EditorGUI.BeginChangeCheck();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("X:", GUILayout.Width(20));
                    line.SpeakerPosX = EditorGUILayout.FloatField(line.SpeakerPosX, GUILayout.Width(50));
                    GUILayout.Space(5);
                    GUILayout.Label("Y:", GUILayout.Width(20));
                    line.SpeakerPosY = EditorGUILayout.FloatField(line.SpeakerPosY, GUILayout.Width(50));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Scale:", "Масшт:"), GUILayout.Width(45));
                    if (line.SpeakerScale <= 0f) line.SpeakerScale = 1f; // Защита от скейла <= 0
                    line.SpeakerScale = EditorGUILayout.FloatField(line.SpeakerScale, GUILayout.Width(50));
                    line.SpeakerScale = Mathf.Max(0.1f, line.SpeakerScale); // Жесткий лимит

                    GUILayout.Space(5);
                    GUILayout.Label(ToolLang.Get("Layer:", "Слой:"), GUILayout.Width(40));
                    line.SpeakerPlane = (ECharacterPlane)EditorGUILayout.EnumPopup(line.SpeakerPlane, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();

                    GUILayout.Label(ToolLang.Get("💡 X/Y values act as OFFSETS from the base scene pos.", "💡 Значения X и Y применяются как СДВИГ от базовой позиции в массовке."), EditorStyles.miniLabel);

                    if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_tree, "Change Pos"); EditorUtility.SetDirty(_tree); TriggerLiveSync(false); }
                }
            }
            GUILayout.EndVertical();

            // ================= КОЛОНКА 2: РАМКА ДИАЛОГА (CUSTOM FRAME) =================
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(350));
            GUILayout.Label("🖼 " + ToolLang.Get("Dialogue UI Frame", "Настройки рамки диалога"), EditorStyles.boldLabel);
            GUILayout.Space(5);

            EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Prefab:", "Префаб:"), GUILayout.Width(55));
            string uiName = line.OverrideDialogueFrame != null ? line.OverrideDialogueFrame.name : ToolLang.Get("Default (Global)", "Базовая (Глобальная)");

            if (GUILayout.Button(uiName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    Undo.RecordObject(_tree, "Override UI");
                    line.OverrideDialogueFrame = obj as GameObject;
                    EditorUtility.SetDirty(_tree);
                    TriggerLiveSync(true);
                }, NovellaGalleryWindow.EGalleryFilter.CustomUI);
            }

            if (line.OverrideDialogueFrame != null)
            {
                if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(25)))
                    NovellaUIEditorWindow.OpenWithCustomPrefab(line.OverrideDialogueFrame);

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25))) { Undo.RecordObject(_tree, "Clear UI"); line.OverrideDialogueFrame = null; EditorUtility.SetDirty(_tree); TriggerLiveSync(true); }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(25)))
                    NovellaUIEditorWindow.ShowWindow();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(ToolLang.Get("💡 Swaps dialogue window style for this specific line.", "💡 Полностью меняет дизайн окна под эту реплику."), EditorStyles.miniLabel);

            // СДВИГ РАМКИ
            GUILayout.Space(10);
            line.CustomizeFrameLayout = EditorGUILayout.ToggleLeft(new GUIContent("📍 " + ToolLang.Get("Override Frame Position", "Сдвиг рамки по экрану (Live Sync)"), "Позволяет сместить окно диалога (Например наверх)"), line.CustomizeFrameLayout, EditorStyles.boldLabel);

            if (line.CustomizeFrameLayout)
            {
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("X:", GUILayout.Width(20));
                line.FramePosX = EditorGUILayout.FloatField(line.FramePosX, GUILayout.Width(60));
                GUILayout.Space(10);
                GUILayout.Label("Y:", GUILayout.Width(20));
                line.FramePosY = EditorGUILayout.FloatField(line.FramePosY, GUILayout.Width(60));
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Scale:", "Масшт:"), GUILayout.Width(45));
                if (line.FrameScale <= 0f) line.FrameScale = 1f; // Защита
                line.FrameScale = EditorGUILayout.FloatField(line.FrameScale, GUILayout.Width(60));
                line.FrameScale = Mathf.Max(0.1f, line.FrameScale); // Жесткий лимит
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                EditorGUILayout.HelpBox(ToolLang.Get("Look at the Game Window or UI Forge! Frame moves in real-time.", "Посмотрите в Game Window или Кузницу UI! Вы двигаете рамку в реальном времени (Offset)."), MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_tree, "Change Frame Settings"); EditorUtility.SetDirty(_tree); TriggerLiveSync(false); }
            GUILayout.EndVertical();


            // ================= КОЛОНКА 3: ТАЙМИНГИ И ТАЙПРАЙТЕР =================
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            GUILayout.Label("⏱ " + ToolLang.Get("Timing & Text Animation", "Тайминги и Анимация"), EditorStyles.boldLabel);
            GUILayout.Space(5);

            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("⏳ " + ToolLang.Get("Wait Before:", "Пауза перед:")), GUILayout.Width(100));
            line.DelayBefore = EditorGUILayout.FloatField(line.DelayBefore, GUILayout.Width(50));
            GUILayout.Label(ToolLang.Get("sec", "сек"), EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            GUILayout.Label(ToolLang.Get("💡 Adds a silent delay before printing starts.", "💡 Добавляет немую паузу перед появлением текста."), EditorStyles.miniLabel);

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            if (line.FontSize <= 0) line.FontSize = _nodeData.FontSize > 0 ? _nodeData.FontSize : 32;
            EditorGUIUtility.labelWidth = ToolLang.IsRU ? 120 : 100;
            line.FontSize = EditorGUILayout.IntField(new GUIContent("🔠 " + ToolLang.Get("Override Font Size:", "Размер шрифта:"), "Переопределить размер текста конкретно для этой реплики."), line.FontSize, GUILayout.Width(170));
            EditorGUIUtility.labelWidth = 0;
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            line.UseTypewriter = EditorGUILayout.ToggleLeft(new GUIContent("⌨ " + ToolLang.Get("Enable Typewriter Effect", "Включить печатную машинку"), "Печатать текст по буквам."), line.UseTypewriter, EditorStyles.boldLabel);

            if (line.UseTypewriter)
            {
                GUILayout.Label(ToolLang.Get("💡 Text appears letter by letter.", "💡 Текст будет печататься по буквам."), EditorStyles.miniLabel);
                GUILayout.Space(5);
                GUILayout.BeginHorizontal();
                GUILayout.Label("⚡ " + ToolLang.Get("Speed:", "Скорость:"), GUILayout.Width(70));
                line.BaseSpeed = EditorGUILayout.FloatField(line.BaseSpeed, GUILayout.Width(50));
                GUILayout.Label(ToolLang.Get("chars/s", "симв/с"), EditorStyles.miniLabel);
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                line.UseCustomPacing = EditorGUILayout.ToggleLeft(new GUIContent("📈 " + ToolLang.Get("Use Custom Pacing Curve", "Использовать кривую скорости"), "Динамическое изменение скорости во время вывода текста (например, в конце медленнее)."), line.UseCustomPacing);

                if (line.UseCustomPacing)
                {
                    GUILayout.Label(ToolLang.Get("💡 Speeds up or slows down text printing dynamically.", "💡 Кривая позволяет ускорять или замедлять печать текста."), EditorStyles.miniLabel);
                    GUILayout.Space(5);
                    line.PacingCurve = EditorGUILayout.CurveField(line.PacingCurve, Color.green, new Rect(0, 0, 1, 2), GUILayout.Height(25));
                }
            }

            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_tree, "Change Timings"); EditorUtility.SetDirty(_tree); TriggerLiveSync(false); }
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.EndHorizontal();
        }
    }
}