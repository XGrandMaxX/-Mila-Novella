/// <summary>
/// ОТВЕЧАЕТ ЗА:
/// 1. Отрисовку нод (карточек) на самом Графе (Canvas).
/// 2. Генерацию портов (гнезд) для подключения связей.
/// 3. Применение цветов к нодам (читает из настроек).
/// 4. Затемнение выключенных DLC-модулей (Graceful Degradation).
/// </summary>
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;
using System.Linq;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    public class NovellaNodeView : Node
    {
        public NovellaNodeBase Data;
        public Port InputPort;
        public Port OutputPort;
        public Port AudioSyncPort;
        public Port AnimSyncPort;
        public Port SceneSyncPort;

        private readonly NovellaGraphView _graphView;
        private readonly Label _pinLabel;

        public NovellaNodeView(NovellaNodeBase data, NovellaGraphView graphView)
        {
            Data = data;
            _graphView = graphView;

            if (Data == null) return;

            if (Data.NodeType != ENodeType.Character && Data.NodeType != ENodeType.Note)
            {
                InputPort = _graphView.GeneratePort(this, Direction.Input, Port.Capacity.Multi);
                InputPort.portName = Data.NodeType == ENodeType.End ? ToolLang.Get("Close", "Конец") : ToolLang.Get("Input", "Вход");
                inputContainer.Add(InputPort);

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event)
                {
                    AudioSyncPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    AudioSyncPort.portName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
                    AudioSyncPort.portColor = new Color(0.16f, 0.5f, 0.44f);
                    outputContainer.Add(AudioSyncPort);

                    AnimSyncPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    AnimSyncPort.portName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");
                    AnimSyncPort.portColor = new Color(0.58f, 0.24f, 0.33f);
                    outputContainer.Add(AnimSyncPort);

                    SceneSyncPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    SceneSyncPort.portName = ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.");
                    SceneSyncPort.portColor = new Color(0.2f, 0.6f, 0.8f);
                    outputContainer.Add(SceneSyncPort);
                }

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Audio || Data.NodeType == ENodeType.Variable || Data.NodeType == ENodeType.Wait || Data.NodeType == ENodeType.SceneSettings || Data.NodeType == ENodeType.Animation || Data.NodeType == ENodeType.EventBroadcast || Data.NodeType == ENodeType.CustomDLC)
                {
                    OutputPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    OutputPort.portName = ToolLang.Get("Next ➡", "Далее ➡");
                    outputContainer.Add(OutputPort);
                }
                else if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition || Data.NodeType == ENodeType.Random)
                {
                    DrawBranchPorts();
                }
                else if (Data.NodeType == ENodeType.CustomDLC)
                {
                    var outputFields = DLCCache.GetOutputFields(Data.GetType());

                    if (outputFields.Count > 0)
                    {
                        foreach (var field in outputFields)
                        {
                            var attr = (NovellaDLCOutputAttribute)field.GetCustomAttributes(typeof(NovellaDLCOutputAttribute), false).First();
                            var port = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                            port.portName = attr.PortName;
                            port.name = field.Name;
                            outputContainer.Add(port);
                        }
                    }
                    else
                    {
                        OutputPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                        OutputPort.portName = ToolLang.Get("Next ➡", "Далее ➡");
                        OutputPort.name = "NextNodeID";
                        outputContainer.Add(OutputPort);
                    }
                }
            }

            _pinLabel = new Label("📌") { name = "pin-icon", pickingMode = PickingMode.Ignore };
            _pinLabel.style.position = Position.Absolute;
            _pinLabel.style.left = -15;
            _pinLabel.style.top = -15;
            _pinLabel.style.fontSize = 20;
            _pinLabel.style.scale = new StyleScale(new Vector2(-1, 1));
            this.Insert(0, _pinLabel);

            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null) { titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter; titleLabel.style.flexGrow = 1; titleLabel.style.marginLeft = titleLabel.style.marginRight = 0; }
            titleContainer.style.justifyContent = Justify.Center;

            this.RegisterCallback<GeometryChangedEvent>(evt => { var collapseBtn = this.titleButtonContainer.Q("collapse-button"); if (collapseBtn != null) collapseBtn.style.display = DisplayStyle.None; });

            RefreshVisuals(); RefreshExpandedState(); RefreshPorts();
        }

        public void ToggleAudioNextPort(bool isSynced)
        {
            if (Data != null && Data.NodeType == ENodeType.Audio && OutputPort != null)
            {
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshExpandedState();
                this.MarkDirtyRepaint();
            }
        }

        public void ToggleAnimNextPort(bool isSynced)
        {
            if (Data != null && Data.NodeType == ENodeType.Animation && OutputPort != null)
            {
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshExpandedState();
                this.MarkDirtyRepaint();
            }
        }

        public void ToggleSceneNextPort(bool isSynced)
        {
            if (Data != null && Data.NodeType == ENodeType.SceneSettings && OutputPort != null)
            {
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshExpandedState();
                this.MarkDirtyRepaint();
            }
        }

        public void DrawBranchPorts()
        {
            if (Data == null || (Data.NodeType != ENodeType.Branch && Data.NodeType != ENodeType.Condition && Data.NodeType != ENodeType.Random)) return;

            var existingPorts = outputContainer.Query<Port>().ToList();
            foreach (var port in existingPorts) { if (port.connected) _graphView.DeleteElements(port.connections); outputContainer.Remove(port); }

            List<NovellaChoice> choices = null;
            if (Data is BranchNodeData b) choices = b.Choices;
            else if (Data is ConditionNodeData c) choices = c.Choices;
            else if (Data is RandomNodeData r) choices = r.Choices;

            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    var port = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);

                    if (Data.NodeType == ENodeType.Branch) port.portName = ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}");
                    else if (Data.NodeType == ENodeType.Random) port.portName = ToolLang.Get($"Chance {i + 1}", $"Шанс {i + 1}");
                    else port.portName = choices[i].LocalizedText.GetText(ToolLang.IsRU ? "RU" : "EN");

                    outputContainer.Add(port);
                }
            }
            RefreshExpandedState(); RefreshPorts();
        }

        public void RefreshVisuals()
        {
            if (Data == null || _pinLabel == null) return;

            bool hasText = false;
            if (Data is DialogueNodeData dnd) hasText = dnd.DialogueLines.Any(l => l.LocalizedPhrase.Translations.Any(t => !string.IsNullOrEmpty(t.Text)));
            else if (Data is BranchNodeData bnd) hasText = bnd.Choices.Any(c => c.LocalizedText.Translations.Any(t => !string.IsNullOrEmpty(t.Text)));
            else if (Data is ConditionNodeData cnd) hasText = cnd.Choices.Any(c => c.LocalizedText.Translations.Any(t => !string.IsNullOrEmpty(t.Text)));

            string titlePrefix = hasText ? "💬 " : "";
            if (Data.NodeType == ENodeType.Wait) titlePrefix = "⏳ ";
            else if (Data.NodeType == ENodeType.SceneSettings) titlePrefix = "🖼 ";
            else if (Data.NodeType == ENodeType.Animation) titlePrefix = "✨ ";
            else if (Data.NodeType == ENodeType.EventBroadcast) titlePrefix = "⚡ ";
            else if (Data.NodeType == ENodeType.Audio) titlePrefix = "🎵 ";
            else if (Data.NodeType == ENodeType.CustomDLC) titlePrefix = "🧩 ";

            title = titlePrefix + (string.IsNullOrEmpty(Data.NodeTitle) ? Data.NodeID : Data.NodeTitle);

            _pinLabel.style.display = Data.IsPinned ? DisplayStyle.Flex : DisplayStyle.None;

            titleContainer.style.borderBottomWidth = 0;

            if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Note || Data.NodeType == ENodeType.CustomDLC)
            {
                titleContainer.style.backgroundColor = new StyleColor(Data.NodeCustomColor);
            }
            else if (Data.NodeType == ENodeType.Wait || Data.NodeType == ENodeType.SceneSettings || Data.NodeType == ENodeType.Animation || Data.NodeType == ENodeType.EventBroadcast || Data.NodeType == ENodeType.Audio)
            {
                titleContainer.style.backgroundColor = new StyleColor(NovellaColorSettingsWindow.GetNodeColor(Data.NodeType));
            }
            else
            {
                titleContainer.style.backgroundColor = new StyleColor(NovellaColorSettingsWindow.GetNodeColor(Data.NodeType));
                if (Data.NodeType == ENodeType.Condition)
                {
                    titleContainer.style.borderBottomColor = new StyleColor(new Color(1f, 0.7f, 0f));
                    titleContainer.style.borderBottomWidth = 3;
                }
            }

            if (Data.NodeType == ENodeType.CustomDLC)
            {
                var settings = NovellaDLCSettings.Instance;
                bool isEnabled = settings.IsDLCEnabled(Data.GetType().FullName);

                this.style.opacity = isEnabled ? 1f : 0.45f;

                titleContainer.style.backgroundColor = new StyleColor(NovellaColorSettingsWindow.GetDLCNodeColor(Data.GetType().FullName));

                var disabledLabel = this.Q<Label>("dlc-disabled-label");
                if (!isEnabled)
                {
                    if (disabledLabel == null)
                    {
                        disabledLabel = new Label("DISABLED") { name = "dlc-disabled-label" };
                        disabledLabel.style.position = Position.Absolute;
                        disabledLabel.style.top = -18;
                        disabledLabel.style.left = 0;
                        disabledLabel.style.right = 0;
                        disabledLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.8f);
                        disabledLabel.style.color = new Color(1f, 0.3f, 0.3f);
                        disabledLabel.style.fontSize = 11;
                        disabledLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        disabledLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                        disabledLabel.style.borderTopLeftRadius = 4;
                        disabledLabel.style.borderTopRightRadius = 4;
                        disabledLabel.style.paddingTop = 2; disabledLabel.style.paddingBottom = 2;

                        this.Add(disabledLabel);
                    }
                }
                else
                {
                    if (disabledLabel != null) this.Remove(disabledLabel);
                }
            }

            extensionContainer.Clear();
            bool hasExtensionData = false;

            if (Data is WaitNodeData waitD)
            {
                string secStr = ToolLang.Get("sec", "сек");
                string waitText = waitD.WaitMode == EWaitMode.Time ? $"{waitD.WaitTime} {secStr}" : ToolLang.Get("Click to continue", "Ожидание клика");
                var waitBlock = new Label($"⏳ {waitText}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter } };
                extensionContainer.Add(waitBlock);
                hasExtensionData = true;
            }
            else if (Data is SceneSettingsNodeData bgD)
            {
                bool isSynced = false;
                if (InputPort != null && InputPort.connected)
                {
                    foreach (var edge in InputPort.connections) { if (edge.output.portName == ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.")) { isSynced = true; break; } }
                }
                bgD.SyncWithDialogue = isSynced;

                if (!isSynced)
                {
                    string bgName = bgD.BgSprite != null ? bgD.BgSprite.name : ToolLang.Get("Color", "Цвет");
                    var bgBlock = new Label($"🖼 {bgName}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter } };
                    extensionContainer.Add(bgBlock);
                    hasExtensionData = true;
                }
            }
            else if (Data is EventBroadcastNodeData evD)
            {
                var evBlock = new Label($"⚡ {evD.BroadcastEventName}") { style = { color = new Color(1f, 0.8f, 0.4f), paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter, unityFontStyleAndWeight = FontStyle.Bold } };
                extensionContainer.Add(evBlock);
                hasExtensionData = true;
            }
            else if (Data is NoteNodeData noteD)
            {
                this.style.width = noteD.NoteWidth;

                var oldBgs = this.Query<VisualElement>("note-bg-container").ToList();
                foreach (var bg in oldBgs) this.Remove(bg);

                titleContainer.style.display = noteD.ShowBackground ? DisplayStyle.Flex : DisplayStyle.None;

                var tLabel = titleContainer.Q<Label>();
                if (tLabel != null)
                {
                    tLabel.style.color = noteD.NoteTitleColor;
                    tLabel.style.fontSize = noteD.NoteTitleFontSize > 0 ? noteD.NoteTitleFontSize : 14;
                    tLabel.style.whiteSpace = WhiteSpace.Normal;
                    titleContainer.style.height = StyleKeyword.Auto;
                    titleContainer.style.paddingTop = 8;
                    titleContainer.style.paddingBottom = 8;
                }

                var border = this.Q("node-border");
                if (border != null)
                {
                    border.style.backgroundColor = noteD.ShowBackground ? new StyleColor(noteD.NodeCustomColor) : new StyleColor(Color.clear);
                    border.style.borderTopWidth = noteD.ShowBackground ? 1 : 0;
                    border.style.borderBottomWidth = noteD.ShowBackground ? 1 : 0;
                    border.style.borderLeftWidth = noteD.ShowBackground ? 1 : 0;
                    border.style.borderRightWidth = noteD.ShowBackground ? 1 : 0;
                }

                var bgContainer = new VisualElement { name = "note-bg-container" };
                bgContainer.style.position = Position.Absolute;
                bgContainer.style.left = bgContainer.style.top = bgContainer.style.right = bgContainer.style.bottom = 0;
                bgContainer.style.borderTopLeftRadius = bgContainer.style.borderTopRightRadius = 8;
                bgContainer.style.borderBottomLeftRadius = bgContainer.style.borderBottomRightRadius = 8;
                bgContainer.style.overflow = Overflow.Hidden;
                this.Insert(0, bgContainer);

                var topRow = new VisualElement { style = { flexDirection = FlexDirection.Column, width = Length.Percent(100) } };
                var midRow = new VisualElement { style = { flexDirection = FlexDirection.Row, width = Length.Percent(100) } };
                var leftCol = new VisualElement { style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center } };
                var textCol = new VisualElement { style = { flexDirection = FlexDirection.Column, flexGrow = 1 } };
                var rightCol = new VisualElement { style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center } };
                var botRow = new VisualElement { style = { flexDirection = FlexDirection.Column, width = Length.Percent(100) } };

                midRow.Add(leftCol); midRow.Add(textCol); midRow.Add(rightCol);
                extensionContainer.Add(topRow); extensionContainer.Add(midRow); extensionContainer.Add(botRow);

                foreach (var imgData in noteD.NoteImages)
                {
                    if (imgData.Image == null) continue;

                    var imgEl = new VisualElement();
                    imgEl.style.backgroundImage = new StyleBackground(imgData.Image);
                    imgEl.style.opacity = imgData.Alpha;

                    float w = imgData.Size.x;
                    float h = imgData.Shape == ENoteImageShape.Normal ? imgData.Size.y : imgData.Size.x;
                    imgEl.style.width = w;
                    imgEl.style.height = h;

                    imgEl.style.translate = new StyleTranslate(new Translate(new Length(imgData.Offset.x), new Length(imgData.Offset.y)));

                    if (imgData.Shape == ENoteImageShape.Circle)
                    {
                        imgEl.style.borderTopLeftRadius = imgEl.style.borderTopRightRadius = imgEl.style.borderBottomLeftRadius = imgEl.style.borderBottomRightRadius = new Length(50, LengthUnit.Percent);
                        imgEl.style.overflow = Overflow.Hidden;
                    }

                    switch (imgData.Alignment)
                    {
                        case ENoteImageAlignment.Background:
                            imgEl.style.position = Position.Absolute;
                            imgEl.style.left = imgData.Offset.x;
                            imgEl.style.top = imgData.Offset.y;
                            imgEl.style.translate = new StyleTranslate(new Translate(new Length(0), new Length(0)));
                            bgContainer.Add(imgEl);
                            break;
                        case ENoteImageAlignment.TopLeft: imgEl.style.alignSelf = Align.FlexStart; topRow.Add(imgEl); break;
                        case ENoteImageAlignment.TopCenter: imgEl.style.alignSelf = Align.Center; topRow.Add(imgEl); break;
                        case ENoteImageAlignment.TopRight: imgEl.style.alignSelf = Align.FlexEnd; topRow.Add(imgEl); break;
                        case ENoteImageAlignment.Left: imgEl.style.alignSelf = Align.Center; leftCol.Add(imgEl); break;
                        case ENoteImageAlignment.Right: imgEl.style.alignSelf = Align.Center; rightCol.Add(imgEl); break;
                        case ENoteImageAlignment.BottomLeft: imgEl.style.alignSelf = Align.FlexStart; botRow.Add(imgEl); break;
                        case ENoteImageAlignment.BottomCenter: imgEl.style.alignSelf = Align.Center; botRow.Add(imgEl); break;
                        case ENoteImageAlignment.BottomRight: imgEl.style.alignSelf = Align.FlexEnd; botRow.Add(imgEl); break;
                    }
                }

                string previewNoteText = noteD.LocalizedNoteText.GetText(_graphView.Window.PreviewLanguage);
                if (string.IsNullOrEmpty(previewNoteText)) previewNoteText = "...";

                var textLabel = new Label(previewNoteText)
                {
                    style = {
                        whiteSpace = WhiteSpace.Normal,
                        color = noteD.NoteTextColor,
                        fontSize = noteD.FontSize > 0 ? noteD.FontSize : 14,
                        paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10,
                        maxWidth = noteD.NoteWidth
                    }
                };
                textCol.Add(textLabel);

                if (!string.IsNullOrEmpty(noteD.NoteURL))
                {
                    var linkBtn = new Button(() => Application.OpenURL(noteD.NoteURL)) { text = "🔗 " + ToolLang.Get("Open Link", "Открыть ссылку") };
                    linkBtn.style.marginTop = 5; linkBtn.style.marginBottom = 5; linkBtn.style.width = noteD.NoteWidth - 30; linkBtn.style.alignSelf = Align.Center;
                    botRow.Add(linkBtn);
                }

                foreach (var link in noteD.NoteLinks)
                {
                    if (string.IsNullOrEmpty(link.URL)) continue;
                    var linkBtn = new Button(() => Application.OpenURL(link.URL)) { text = $"🔗 {link.DisplayName}" };
                    linkBtn.style.marginTop = 2; linkBtn.style.marginBottom = 2; linkBtn.style.width = noteD.NoteWidth - 30; linkBtn.style.alignSelf = Align.Center;
                    botRow.Add(linkBtn);
                }

                hasExtensionData = true;
            }
            else if (Data is EndNodeData endD)
            {
                string endText = "";
                if (endD.EndAction == EEndAction.ReturnToMainMenu) endText = ToolLang.Get("Return to Main Menu", "В гл. меню");
                else if (endD.EndAction == EEndAction.LoadNextChapter) endText = ToolLang.Get("Load Next Chapter", "След. глава");
                else if (endD.EndAction == EEndAction.LoadSpecificScene) endText = ToolLang.Get("Load Scene", "Загрузить сцену");
                else if (endD.EndAction == EEndAction.QuitGame) endText = ToolLang.Get("Quit Game", "Выход");

                if (endD.EndAction == EEndAction.LoadNextChapter && endD.NextChapter != null)
                    endText += $" -> {endD.NextChapter.name}";
                else if (endD.EndAction == EEndAction.LoadSpecificScene && !string.IsNullOrEmpty(endD.TargetSceneName))
                    endText += $" -> {endD.TargetSceneName}";

                var endBlock = new Label($"🛑 {endText}") { style = { backgroundColor = new StyleColor(new Color(0.6f, 0.1f, 0.1f)), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 12 } };
                extensionContainer.Add(endBlock); hasExtensionData = true;
            }
            else if (Data is AudioNodeData audD)
            {
                bool isSynced = false;
                if (InputPort != null && InputPort.connected)
                {
                    foreach (var edge in InputPort.connections) { if (edge.output.portName == ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.")) { isSynced = true; break; } }
                }

                audD.SyncWithDialogue = isSynced;

                if (!isSynced)
                {
                    string audioName = audD.AudioAsset != null ? audD.AudioAsset.name : ToolLang.Get("None", "Пусто");
                    string act = audD.AudioAction == EAudioAction.Play ? "▶" : "⏸";
                    var audioBlock = new Label($"{act} [{audD.AudioChannel}] {audioName}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8 } };
                    extensionContainer.Add(audioBlock); hasExtensionData = true;
                }
            }
            else if (Data is AnimationNodeData animD)
            {
                bool isSynced = false;
                if (InputPort != null && InputPort.connected)
                {
                    foreach (var edge in InputPort.connections) { if (edge.output.portName == ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.")) { isSynced = true; break; } }
                }

                animD.SyncWithDialogue = isSynced;

                if (!isSynced)
                {
                    var animBlock = new Label($"✨ {animD.AnimEvents.Count} Animations") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8 } };
                    extensionContainer.Add(animBlock); hasExtensionData = true;
                }
            }
            else if (Data is VariableNodeData varD)
            {
                if (varD.Variables != null && varD.Variables.Count > 0)
                {
                    int displayCount = Mathf.Min(3, varD.Variables.Count);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var v = varD.Variables[i];
                        string op = v.VarOperation == EVarOperation.Set ? "=" : "+=";
                        var varBlock = new Label($"📊 {v.VariableName} {op} {v.VarValue}") { style = { color = Color.white, paddingTop = 2, paddingBottom = 2, paddingLeft = 8, paddingRight = 8 } };
                        extensionContainer.Add(varBlock);
                    }

                    if (varD.Variables.Count > 3)
                    {
                        int remain = varD.Variables.Count - 3;
                        var moreBlock = new Label(ToolLang.Get($"... and {remain} more", $"... и еще {remain}")) { style = { color = new Color(0.7f, 0.7f, 0.7f), paddingTop = 2, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic } };
                        extensionContainer.Add(moreBlock);
                    }
                    hasExtensionData = true;
                }
            }
            else if (Data is DialogueNodeData dialD)
            {
                if (dialD.DialogueLines.Count > 0)
                {
                    var distinctSpeakers = dialD.DialogueLines.Where(l => l.Speaker != null).Select(l => l.Speaker).Distinct().ToList();

                    foreach (var spk in distinctSpeakers)
                    {
                        int linesCount = dialD.DialogueLines.Count(l => l.Speaker == spk);
                        var speakerBlock = new Label($"🗣 {spk.name} ({linesCount})") { style = { backgroundColor = new StyleColor(spk.ThemeColor), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2, borderTopColor = new Color(1f, 1f, 1f, 0.8f), borderBottomColor = new Color(1f, 1f, 1f, 0.8f), borderLeftColor = new Color(1f, 1f, 1f, 0.8f), borderRightColor = new Color(1f, 1f, 1f, 0.8f), unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
                        extensionContainer.Add(speakerBlock);
                    }
                    hasExtensionData = true;
                }
            }

            if (hasExtensionData)
            {
                extensionContainer.style.display = DisplayStyle.Flex;
                extensionContainer.style.backgroundColor = Data.NodeType == ENodeType.Note && !(Data is NoteNodeData nd && nd.ShowBackground) ? new StyleColor(Color.clear) : new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.8f));
                extensionContainer.style.paddingTop = 5; extensionContainer.style.paddingBottom = 5;
                extensionContainer.style.overflow = Overflow.Visible;
            }
            else { extensionContainer.style.display = DisplayStyle.None; }

            this.MarkDirtyRepaint(); RefreshExpandedState();
        }

        public override void OnSelected() { base.OnSelected(); _graphView?.OnNodeSelected?.Invoke(this); }
        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); if (Data != null) Data.GraphPosition = newPos.position; }

        public void SaveNodeData()
        {
            if (Data == null) return;
            Data.GraphPosition = GetPosition().position;
            if (Data.NodeType == ENodeType.End || Data.NodeType == ENodeType.Character || Data.NodeType == ENodeType.Note) return;

            if (Data is BranchNodeData bnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < bnd.Choices.Count; i++)
                {
                    if (i < ports.Count && ports[i].connected && ports[i].connections.Any()) bnd.Choices[i].NextNodeID = ports[i].connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else bnd.Choices[i].NextNodeID = "";
                }
            }
            else if (Data is ConditionNodeData cnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < cnd.Choices.Count; i++)
                {
                    if (i < ports.Count && ports[i].connected && ports[i].connections.Any()) cnd.Choices[i].NextNodeID = ports[i].connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else cnd.Choices[i].NextNodeID = "";
                }
            }
            else if (Data is RandomNodeData rnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < rnd.Choices.Count; i++)
                {
                    if (i < ports.Count && ports[i].connected && ports[i].connections.Any()) rnd.Choices[i].NextNodeID = ports[i].connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else rnd.Choices[i].NextNodeID = "";
                }
            }
            else
            {
                if (Data is DialogueNodeData dialData)
                {
                    if (AudioSyncPort != null && AudioSyncPort.connected && AudioSyncPort.connections.Any())
                        dialData.AudioSyncNodeID = (AudioSyncPort.connections.First().input.node as NovellaNodeView).Data.NodeID;
                    else dialData.AudioSyncNodeID = "";

                    if (AnimSyncPort != null && AnimSyncPort.connected && AnimSyncPort.connections.Any())
                        dialData.AnimSyncNodeID = (AnimSyncPort.connections.First().input.node as NovellaNodeView).Data.NodeID;
                    else dialData.AnimSyncNodeID = "";

                    if (SceneSyncPort != null && SceneSyncPort.connected && SceneSyncPort.connections.Any())
                        dialData.SceneSyncNodeID = (SceneSyncPort.connections.First().input.node as NovellaNodeView).Data.NodeID;
                    else dialData.SceneSyncNodeID = "";

                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        dialData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else dialData.NextNodeID = "";
                }
                else if (Data is WaitNodeData waitData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        waitData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else waitData.NextNodeID = "";
                }
                else if (Data is SceneSettingsNodeData bgData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        bgData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else bgData.NextNodeID = "";
                }
                else if (Data is EventBroadcastNodeData evntData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        evntData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else evntData.NextNodeID = "";
                }
                else if (Data is AudioNodeData audData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        audData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else audData.NextNodeID = "";
                }
                else if (Data is AnimationNodeData animData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        animData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else animData.NextNodeID = "";
                }
                else if (Data is VariableNodeData varData)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                        varData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else varData.NextNodeID = "";
                }
                else if (Data.NodeType == ENodeType.CustomDLC)
                {
                    var ports = outputContainer.Query<Port>().ToList();
                    foreach (var port in ports)
                    {
                        var field = Data.GetType().GetField(port.name);
                        if (field != null && field.FieldType == typeof(string))
                        {
                            string targetNodeID = port.connected && port.connections.Any()
                                ? (port.connections.FirstOrDefault()?.input?.node as NovellaNodeView)?.Data.NodeID ?? ""
                                : "";
                            field.SetValue(Data, targetNodeID);
                        }
                    }
                }
            }
        }
    }
}