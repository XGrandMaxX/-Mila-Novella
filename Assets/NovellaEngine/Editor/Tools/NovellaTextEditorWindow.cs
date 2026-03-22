using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Text.RegularExpressions;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaTextEditorWindow : EditorWindow
    {
        private DialogueLine _dialogueLine;
        private LocalizedString _targetString;
        private SerializedProperty _fontSizeProp;
        private NovellaTree _targetTree;
        private Action _onSavedCallback;

        private NovellaLocalizationSettings _locSettings;
        private Color _tagColor = Color.cyan;
        private string _selectedLang = "EN";
        private int _lastCursorIndex = 0;
        private int _lastSelectIndex = 0;
        private bool _isGuideExpanded = true;

        private TextField _textArea;
        private Label _previewLabel;

        public static void OpenWindow(DialogueLine line, SerializedProperty fontSizeProp, NovellaTree tree, Action onSaved)
        {
            var win = GetWindow<NovellaTextEditorWindow>(true, ToolLang.Get("📝 Novella Text Editor", "📝 Редактор текста"), true);
            win._dialogueLine = line;
            win._targetString = line.LocalizedPhrase;
            win._fontSizeProp = fontSizeProp;
            win._targetTree = tree;
            win._onSavedCallback = onSaved;

            win._locSettings = NovellaLocalizationSettings.GetOrCreateSettings();

            // ФИКС: Считываем язык превью, который выбрал пользователь в графе
            string savedLang = EditorPrefs.GetString("NovellaGraph_PreviewLang", "");
            if (!string.IsNullOrEmpty(savedLang) && win._locSettings.Languages.Contains(savedLang))
            {
                win._selectedLang = savedLang;
            }
            else if (win._locSettings.Languages.Count > 0)
            {
                win._selectedLang = win._locSettings.Languages[0];
            }

            win.minSize = new Vector2(1000, 500);
            win.ShowUtility(); win.BuildUI(); win.Focus();
        }

        private void OnEnable() { Undo.undoRedoPerformed += OnUndoRedo; if (_targetTree != null) BuildUI(); }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndoRedo; EditorUtility.SetDirty(_targetTree); AssetDatabase.SaveAssets(); _onSavedCallback?.Invoke(); }

        private void OnUndoRedo() { if (_targetTree == null || _textArea == null) return; _textArea.SetValueWithoutNotify(RichToMacro(_targetString.GetText(_selectedLang))); UpdatePreview(); Repaint(); }

        private void Update()
        {
            if (_targetTree == null) { Close(); return; }
            if (_textArea != null && _textArea.focusController != null && _textArea.focusController.focusedElement == _textArea)
            {
                _lastCursorIndex = _textArea.cursorIndex;
                _lastSelectIndex = _textArea.selectIndex;
            }
        }

        private string MacroToRich(string text) { if (string.IsNullOrEmpty(text)) return text; text = text.Replace("<cr>", "<color=red>").Replace("<cg>", "<color=green>").Replace("<cb>", "<color=#3399FF>").Replace("<cy>", "<color=yellow>").Replace("</c>", "</color>"); return Regex.Replace(text, @"<(#[0-9a-fA-F]{6})>", "<color=$1>"); }
        private string RichToMacro(string text) { if (string.IsNullOrEmpty(text)) return text; text = text.Replace("<color=red>", "<cr>").Replace("<color=green>", "<cg>").Replace("<color=#3399FF>", "<cb>").Replace("<color=yellow>", "<cy>").Replace("</color>", "</c>"); return Regex.Replace(text, @"<color=(#[0-9a-fA-F]{6})>", "<$1>"); }

        public void BuildUI()
        {
            if (_targetTree == null) return;
            rootVisualElement.Clear();

            string speakerName = _dialogueLine != null && _dialogueLine.Speaker != null ? _dialogueLine.Speaker.name : ToolLang.Get("Narrator", "Автор");
            var header = new Label($"📝 {ToolLang.Get("Editing Line for:", "Редактирование реплики:")} {speakerName}") { style = { backgroundColor = new Color(0.2f, 0.2f, 0.2f), color = Color.white, unityTextAlign = TextAnchor.MiddleCenter, paddingBottom = 5, paddingTop = 5, unityFontStyleAndWeight = FontStyle.Bold } };
            rootVisualElement.Add(header);

            var topBar = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = new Color(0.15f, 0.15f, 0.15f), paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5, alignItems = Align.Center } };

            foreach (var lang in _locSettings.Languages)
            {
                var btn = new Button(() => { _selectedLang = lang; BuildUI(); }) { text = lang };
                btn.style.backgroundColor = _selectedLang == lang ? new StyleColor(new Color(0.2f, 0.4f, 0.6f)) : new StyleColor(Color.clear);
                topBar.Add(btn);
            }

            topBar.Add(new VisualElement { style = { flexGrow = 1 } });

            if (_fontSizeProp != null)
            {
                var fontSizeSlider = new SliderInt(ToolLang.Get("Font Size:", "Размер шрифта:"), 10, 120) { value = _fontSizeProp.intValue, style = { width = 200 } };
                fontSizeSlider.RegisterValueChangedCallback(evt => { _fontSizeProp.intValue = evt.newValue; _fontSizeProp.serializedObject.ApplyModifiedProperties(); ApplyFontSize(); });
                topBar.Add(fontSizeSlider);
            }

            rootVisualElement.Add(topBar);

            var formatBar = new IMGUIContainer(DrawFormattingToolbar) { style = { height = 35 } };
            rootVisualElement.Add(formatBar);

            var workArea = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5 } };

            var guidePanel = new VisualElement { style = { backgroundColor = new Color(0.18f, 0.18f, 0.18f), marginRight = 5, borderRightColor = Color.black, borderRightWidth = 1 } };
            var guideHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, backgroundColor = new Color(0.15f, 0.15f, 0.15f), paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5 } };
            var toggleBtn = new Button() { text = _isGuideExpanded ? "◀" : "▶", style = { width = 20, height = 20, paddingTop = 0, paddingBottom = 0, paddingLeft = 0, paddingRight = 0, marginRight = 5 } };
            var guideTitle = new Label(ToolLang.Get("📖 Guide", "📖 Гайд")) { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.6f, 0.8f, 1f), display = _isGuideExpanded ? DisplayStyle.Flex : DisplayStyle.None } };
            guideHeader.Add(toggleBtn); guideHeader.Add(guideTitle); guidePanel.Add(guideHeader);

            var guideContent = new VisualElement { style = { paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10, display = _isGuideExpanded ? DisplayStyle.Flex : DisplayStyle.None } };
            string guideText = ToolLang.IsRU ?
                "<b><color=#FF5555><\u200bcr></color></b> = Красный\n<b><color=#55FF55><\u200bcg></color></b> = Зеленый\n<b><color=#3399FF><\u200bcb></color></b> = Голубой\n<b><color=#FFFF55><\u200bcy></color></b> = Желтый\n<b><color=#DDDDDD><\u200b#hex></color></b> = Свой цвет\n\n<b><\u200b/c></b> = Закрыть цвет\n\n<b>[ B ]</b> = Жирный (<b>текст</b>)\n<b>[ I ]</b> = Курсив (<i>текст</i>)" :
                "<b><color=#FF5555><\u200bcr></color></b> = Red\n<b><color=#55FF55><\u200bcg></color></b> = Green\n<b><color=#3399FF><\u200bcb></color></b> = Blue\n<b><color=#FFFF55><\u200bcy></color></b> = Yellow\n<b><color=#DDDDDD><\u200b#hex></color></b> = Custom Color\n\n<b><\u200b/c></b> = Close color\n\n<b>[ B ]</b> = Bold (<b>text</b>)\n<b>[ I ]</b> = Italic (<i>text</i>)";
            guideContent.Add(new Label(guideText) { enableRichText = true, style = { whiteSpace = WhiteSpace.Normal, fontSize = 13 } });
            guidePanel.Add(guideContent);
            guidePanel.style.width = _isGuideExpanded ? 220 : 35;
            toggleBtn.clicked += () => {
                _isGuideExpanded = !_isGuideExpanded; guidePanel.style.width = _isGuideExpanded ? 220 : 35;
                guideTitle.style.display = _isGuideExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                guideContent.style.display = _isGuideExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                toggleBtn.text = _isGuideExpanded ? "◀" : "▶";
            };
            workArea.Add(guidePanel);

            var editorsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

            var leftPanel = new VisualElement { style = { width = new Length(50, LengthUnit.Percent), paddingRight = 5 } };
            leftPanel.Add(new Label($"Editing: {_selectedLang} (Macro Code)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 5 } });

            var leftScroll = new ScrollView { style = { flexGrow = 1, backgroundColor = new Color(0.15f, 0.15f, 0.15f), borderTopWidth = 1 } };
            _textArea = new TextField { multiline = true, style = { flexGrow = 1, whiteSpace = WhiteSpace.Normal, minHeight = new Length(100, LengthUnit.Percent) } };

            _textArea.value = RichToMacro(_targetString.GetText(_selectedLang));

            _textArea.RegisterValueChangedCallback(evt => {
                Undo.RecordObject(_targetTree, "Type Text");
                _targetString.SetText(_selectedLang, MacroToRich(evt.newValue));
                EditorUtility.SetDirty(_targetTree); UpdatePreview();
            });
            leftScroll.Add(_textArea);
            leftPanel.Add(leftScroll);
            editorsContainer.Add(leftPanel);

            var rightPanel = new VisualElement { style = { width = new Length(50, LengthUnit.Percent), paddingLeft = 5 } };
            rightPanel.Add(new Label(ToolLang.Get("Live Preview:", "Предпросмотр:")) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 5 } });
            var previewScroll = new ScrollView { style = { flexGrow = 1, backgroundColor = new Color(0.15f, 0.15f, 0.15f), borderTopWidth = 1 } };

            _previewLabel = new Label { enableRichText = true, style = { whiteSpace = WhiteSpace.Normal, paddingTop = 5, paddingBottom = 5, paddingLeft = 5, paddingRight = 5, color = Color.white } };
            previewScroll.Add(_previewLabel);
            rightPanel.Add(previewScroll);
            editorsContainer.Add(rightPanel);

            workArea.Add(editorsContainer);
            rootVisualElement.Add(workArea);
            ApplyFontSize(); UpdatePreview();
        }

        private void ApplyFontSize()
        {
            if (_fontSizeProp == null) return;
            if (_previewLabel != null) _previewLabel.style.fontSize = new StyleLength(new Length(_fontSizeProp.intValue, LengthUnit.Pixel));
            if (_textArea != null) { var textInput = _textArea.Q(className: "unity-text-field__input"); if (textInput != null) textInput.style.fontSize = new StyleLength(new Length(_fontSizeProp.intValue, LengthUnit.Pixel)); }
        }

        private void UpdatePreview() { if (_previewLabel != null && _targetTree != null) _previewLabel.text = _targetString.GetText(_selectedLang); }

        private void DrawFormattingToolbar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (DrawSafeButton("<b>B</b>", FontStyle.Bold, 30)) InsertTag("<b>", "</b>");
            if (DrawSafeButton("<i>I</i>", FontStyle.Italic, 30)) InsertTag("<i>", "</i>");
            GUILayout.Space(20);
            _tagColor = EditorGUILayout.ColorField(GUIContent.none, _tagColor, false, false, false, GUILayout.Width(60));
            if (DrawSafeButton(ToolLang.Get("🎨 Apply Color", "🎨 Свой цвет"), FontStyle.Bold, 130)) { string hex = ColorUtility.ToHtmlStringRGB(_tagColor); InsertTag($"<#{hex}>", "</c>"); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private bool DrawSafeButton(string text, FontStyle style, float width)
        {
            GUIContent content = new GUIContent(text); GUIStyle btnStyle = new GUIStyle(EditorStyles.miniButton) { richText = true, fontStyle = style };
            Rect rect = GUILayoutUtility.GetRect(content, btnStyle, GUILayout.Width(width), GUILayout.Height(20));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition)) { Event.current.Use(); return true; }
            if (Event.current.type == EventType.Repaint) btnStyle.Draw(rect, content, false, false, false, false);
            return false;
        }

        private void InsertTag(string openTag, string closeTag)
        {
            if (_textArea == null) return; Undo.RecordObject(_targetTree, "Apply Tag");
            string text = _textArea.value; int start = Mathf.Min(_lastCursorIndex, _lastSelectIndex); int end = Mathf.Max(_lastCursorIndex, _lastSelectIndex);
            string newText = start != end ? text[..start] + openTag + text[start..end] + closeTag + text[end..] : text[..start] + openTag + "Text" + closeTag + text[start..];
            _textArea.value = newText; _targetString.SetText(_selectedLang, MacroToRich(newText)); EditorUtility.SetDirty(_targetTree); UpdatePreview(); _textArea.Focus();
            EditorApplication.delayCall += () => { if (_textArea != null) _textArea.SelectRange(start + openTag.Length, start != end ? end + openTag.Length + closeTag.Length : start + openTag.Length + 4); };
        }
    }
}