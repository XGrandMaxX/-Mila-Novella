using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaNodeView : Node
    {
        public NovellaNodeData Data;
        public Port InputPort;
        public Port OutputPort;
        public Port AudioSyncPort;
        public Port AnimSyncPort;

        private readonly NovellaGraphView _graphView;
        private readonly Label _pinLabel;

        public NovellaNodeView(NovellaNodeData data, NovellaGraphView graphView)
        {
            Data = data;
            _graphView = graphView;

            if (Data.NodeType != ENodeType.Character && Data.NodeType != ENodeType.Note)
            {
                InputPort = _graphView.GeneratePort(this, Direction.Input, Port.Capacity.Multi);
                InputPort.portName = Data.NodeType == ENodeType.End ? ToolLang.Get("Close", "Конец") : ToolLang.Get("Input", "Вход");
                inputContainer.Add(InputPort);

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event)
                {
                    AudioSyncPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    AudioSyncPort.portName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
                    AudioSyncPort.portColor = new Color(0.8f, 0.4f, 0.8f);
                    outputContainer.Add(AudioSyncPort);

                    AnimSyncPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    AnimSyncPort.portName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");
                    AnimSyncPort.portColor = new Color(1f, 0.6f, 0.2f);
                    outputContainer.Add(AnimSyncPort);
                }

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Audio || Data.NodeType == ENodeType.Variable || Data.NodeType == ENodeType.Wait || Data.NodeType == ENodeType.Background || Data.NodeType == ENodeType.Animation || Data.NodeType == ENodeType.EventBroadcast)
                {
                    OutputPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    OutputPort.portName = ToolLang.Get("Next ➡", "Далее ➡");
                    outputContainer.Add(OutputPort);
                }
                else if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition || Data.NodeType == ENodeType.Random)
                {
                    DrawBranchPorts();
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
            if (Data.NodeType == ENodeType.Audio && OutputPort != null)
            {
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshExpandedState();
                this.MarkDirtyRepaint();
            }
        }

        public void ToggleAnimNextPort(bool isSynced)
        {
            if (Data.NodeType == ENodeType.Animation && OutputPort != null)
            {
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
                RefreshExpandedState();
                this.MarkDirtyRepaint();
            }
        }

        public void DrawBranchPorts()
        {
            if (Data.NodeType != ENodeType.Branch && Data.NodeType != ENodeType.Condition && Data.NodeType != ENodeType.Random) return;

            var existingPorts = outputContainer.Query<Port>().ToList();
            foreach (var port in existingPorts) { if (port.connected) _graphView.DeleteElements(port.connections); outputContainer.Remove(port); }

            for (int i = 0; i < Data.Choices.Count; i++)
            {
                var port = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);

                if (Data.NodeType == ENodeType.Branch) port.portName = ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}");
                else if (Data.NodeType == ENodeType.Random) port.portName = ToolLang.Get($"Chance {i + 1}", $"Шанс {i + 1}");
                else port.portName = Data.Choices[i].LocalizedText.GetText(ToolLang.IsRU ? "RU" : "EN");

                outputContainer.Add(port);
            }
            RefreshExpandedState(); RefreshPorts();
        }

        public void RefreshVisuals()
        {
            if (Data == null || _pinLabel == null) return;

            bool hasText = false;
            if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event) hasText = Data.DialogueLines.Any(l => l.LocalizedPhrase.Translations.Any(t => !string.IsNullOrEmpty(t.Text)));
            else if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition) hasText = Data.Choices.Any(c => c.LocalizedText.Translations.Any(t => !string.IsNullOrEmpty(t.Text)));

            string titlePrefix = hasText ? "💬 " : "";
            if (Data.NodeType == ENodeType.Wait) titlePrefix = "⏳ ";
            else if (Data.NodeType == ENodeType.Background) titlePrefix = "🖼 ";
            else if (Data.NodeType == ENodeType.Animation) titlePrefix = "✨ ";
            else if (Data.NodeType == ENodeType.EventBroadcast) titlePrefix = "⚡ ";

            title = titlePrefix + (string.IsNullOrEmpty(Data.NodeTitle) ? Data.NodeID : Data.NodeTitle);

            _pinLabel.style.display = Data.IsPinned ? DisplayStyle.Flex : DisplayStyle.None;

            titleContainer.style.borderBottomWidth = 0;

            if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Note)
            {
                titleContainer.style.backgroundColor = new StyleColor(Data.NodeCustomColor);
            }
            else if (Data.NodeType == ENodeType.Wait || Data.NodeType == ENodeType.Background || Data.NodeType == ENodeType.Animation || Data.NodeType == ENodeType.EventBroadcast)
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

            extensionContainer.Clear();
            bool hasExtensionData = false;

            if (Data.NodeType == ENodeType.Wait)
            {
                string secStr = ToolLang.Get("sec", "сек");
                string waitText = Data.WaitMode == EWaitMode.Time ? $"{Data.WaitTime} {secStr}" : ToolLang.Get("Click to continue", "Ожидание клика");
                var waitBlock = new Label($"⏳ {waitText}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter } };
                extensionContainer.Add(waitBlock);
                hasExtensionData = true;
            }
            else if (Data.NodeType == ENodeType.Background)
            {
                string bgName = Data.BgSprite != null ? Data.BgSprite.name : ToolLang.Get("Color", "Цвет");
                var bgBlock = new Label($"🖼 {bgName}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter } };
                extensionContainer.Add(bgBlock);
                hasExtensionData = true;
            }
            else if (Data.NodeType == ENodeType.EventBroadcast)
            {
                var evBlock = new Label($"⚡ {Data.BroadcastEventName}") { style = { color = new Color(1f, 0.8f, 0.4f), paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, unityTextAlign = TextAnchor.MiddleCenter, unityFontStyleAndWeight = FontStyle.Bold } };
                extensionContainer.Add(evBlock);
                hasExtensionData = true;
            }
            else if (Data.NodeType == ENodeType.Note)
            {
                this.style.width = Data.NoteWidth;

                var oldBgs = this.Query<VisualElement>("note-bg-container").ToList();
                foreach (var bg in oldBgs) this.Remove(bg);

                titleContainer.style.display = Data.ShowBackground ? DisplayStyle.Flex : DisplayStyle.None;

                var tLabel = titleContainer.Q<Label>();
                if (tLabel != null)
                {
                    tLabel.style.color = Data.NoteTitleColor;
                    tLabel.style.fontSize = Data.NoteTitleFontSize > 0 ? Data.NoteTitleFontSize : 14;
                    tLabel.style.whiteSpace = WhiteSpace.Normal;
                    titleContainer.style.height = StyleKeyword.Auto;
                    titleContainer.style.paddingTop = 8;
                    titleContainer.style.paddingBottom = 8;
                }

                var border = this.Q("node-border");
                if (border != null)
                {
                    border.style.backgroundColor = Data.ShowBackground ? new StyleColor(Data.NodeCustomColor) : new StyleColor(Color.clear);
                    border.style.borderTopWidth = Data.ShowBackground ? 1 : 0;
                    border.style.borderBottomWidth = Data.ShowBackground ? 1 : 0;
                    border.style.borderLeftWidth = Data.ShowBackground ? 1 : 0;
                    border.style.borderRightWidth = Data.ShowBackground ? 1 : 0;
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

                foreach (var imgData in Data.NoteImages)
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

                string previewNoteText = Data.LocalizedNoteText.GetText(_graphView.Window.PreviewLanguage);
                if (string.IsNullOrEmpty(previewNoteText)) previewNoteText = "...";

                var textLabel = new Label(previewNoteText)
                {
                    style = {
                        whiteSpace = WhiteSpace.Normal,
                        color = Data.NoteTextColor,
                        fontSize = Data.FontSize > 0 ? Data.FontSize : 14,
                        paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10,
                        maxWidth = Data.NoteWidth
                    }
                };
                textCol.Add(textLabel);

                if (!string.IsNullOrEmpty(Data.NoteURL))
                {
                    var linkBtn = new Button(() => Application.OpenURL(Data.NoteURL)) { text = "🔗 " + ToolLang.Get("Open Link", "Открыть ссылку") };
                    linkBtn.style.marginTop = 5; linkBtn.style.marginBottom = 5; linkBtn.style.width = Data.NoteWidth - 30; linkBtn.style.alignSelf = Align.Center;
                    botRow.Add(linkBtn);
                }

                foreach (var link in Data.NoteLinks)
                {
                    if (string.IsNullOrEmpty(link.URL)) continue;
                    var linkBtn = new Button(() => Application.OpenURL(link.URL)) { text = $"🔗 {link.DisplayName}" };
                    linkBtn.style.marginTop = 2; linkBtn.style.marginBottom = 2; linkBtn.style.width = Data.NoteWidth - 30; linkBtn.style.alignSelf = Align.Center;
                    botRow.Add(linkBtn);
                }

                hasExtensionData = true;
            }
            else if (Data.NodeType == ENodeType.End)
            {
                string endText = "";
                if (Data.EndAction == EEndAction.ReturnToMainMenu) endText = ToolLang.Get("Return to Main Menu", "В гл. меню");
                else if (Data.EndAction == EEndAction.LoadNextChapter) endText = ToolLang.Get("Load Next Chapter", "След. глава");
                else if (Data.EndAction == EEndAction.LoadSpecificScene) endText = ToolLang.Get("Load Scene", "Загрузить сцену");
                else if (Data.EndAction == EEndAction.QuitGame) endText = ToolLang.Get("Quit Game", "Выход");

                if (Data.EndAction == EEndAction.LoadNextChapter && Data.NextChapter != null)
                    endText += $" -> {Data.NextChapter.name}";
                else if (Data.EndAction == EEndAction.LoadSpecificScene && !string.IsNullOrEmpty(Data.TargetSceneName))
                    endText += $" -> {Data.TargetSceneName}";

                var endBlock = new Label($"🛑 {endText}") { style = { backgroundColor = new StyleColor(new Color(0.6f, 0.1f, 0.1f)), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 12 } };
                extensionContainer.Add(endBlock); hasExtensionData = true;
            }
            else if (Data.NodeType == ENodeType.Audio)
            {
                bool isSynced = false;
                if (InputPort != null && InputPort.connected)
                {
                    foreach (var edge in InputPort.connections)
                    {
                        if (edge.output.portName == ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр."))
                        {
                            isSynced = true;
                            break;
                        }
                    }
                }

                Data.SyncWithDialogue = isSynced;

                if (!isSynced)
                {
                    string audioName = Data.AudioAsset != null ? Data.AudioAsset.name : ToolLang.Get("None", "Пусто");
                    string act = Data.AudioAction == EAudioAction.Play ? "▶" : "⏸";
                    var audioBlock = new Label($"{act} [{Data.AudioChannel}] {audioName}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8 } };
                    extensionContainer.Add(audioBlock); hasExtensionData = true;
                }
            }
            else if (Data.NodeType == ENodeType.Animation)
            {
                bool isSynced = false;
                if (InputPort != null && InputPort.connected)
                {
                    foreach (var edge in InputPort.connections)
                    {
                        if (edge.output.portName == ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр."))
                        {
                            isSynced = true;
                            break;
                        }
                    }
                }

                Data.SyncWithDialogue = isSynced;

                if (!isSynced)
                {
                    var animBlock = new Label($"✨ {Data.AnimEvents.Count} Animations") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8 } };
                    extensionContainer.Add(animBlock); hasExtensionData = true;
                }
            }
            else if (Data.NodeType == ENodeType.Variable)
            {
                if (Data.Variables != null && Data.Variables.Count > 0)
                {
                    int displayCount = Mathf.Min(3, Data.Variables.Count);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var v = Data.Variables[i];
                        string op = v.VarOperation == EVarOperation.Set ? "=" : "+=";
                        var varBlock = new Label($"📊 {v.VariableName} {op} {v.VarValue}") { style = { color = Color.white, paddingTop = 2, paddingBottom = 2, paddingLeft = 8, paddingRight = 8 } };
                        extensionContainer.Add(varBlock);
                    }

                    if (Data.Variables.Count > 3)
                    {
                        int remain = Data.Variables.Count - 3;
                        var moreBlock = new Label(ToolLang.Get($"... and {remain} more", $"... и еще {remain}")) { style = { color = new Color(0.7f, 0.7f, 0.7f), paddingTop = 2, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic } };
                        extensionContainer.Add(moreBlock);
                    }
                    hasExtensionData = true;
                }
            }
            else if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event)
            {
                if (Data.DialogueLines.Count > 0)
                {
                    var distinctSpeakers = Data.DialogueLines.Where(l => l.Speaker != null).Select(l => l.Speaker).Distinct().ToList();

                    foreach (var spk in distinctSpeakers)
                    {
                        int linesCount = Data.DialogueLines.Count(l => l.Speaker == spk);
                        var speakerBlock = new Label($"🗣 {spk.name} ({linesCount})") { style = { backgroundColor = new StyleColor(spk.ThemeColor), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2, borderTopColor = new Color(1f, 1f, 1f, 0.8f), borderBottomColor = new Color(1f, 1f, 1f, 0.8f), borderLeftColor = new Color(1f, 1f, 1f, 0.8f), borderRightColor = new Color(1f, 1f, 1f, 0.8f), unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
                        extensionContainer.Add(speakerBlock);
                    }
                    hasExtensionData = true;
                }
            }

            if (hasExtensionData)
            {
                extensionContainer.style.display = DisplayStyle.Flex;
                extensionContainer.style.backgroundColor = Data.NodeType == ENodeType.Note && !Data.ShowBackground ? new StyleColor(Color.clear) : new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.8f));
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

            if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition || Data.NodeType == ENodeType.Random)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < Data.Choices.Count; i++)
                {
                    if (i < ports.Count && ports[i].connected && ports[i].connections.Any()) Data.Choices[i].NextNodeID = ports[i].connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    else Data.Choices[i].NextNodeID = "";
                }
            }
            else
            {
                if (AudioSyncPort != null && AudioSyncPort.connected && AudioSyncPort.connections.Any())
                    Data.AudioSyncNodeID = (AudioSyncPort.connections.First().input.node as NovellaNodeView).Data.NodeID;
                else
                    Data.AudioSyncNodeID = "";

                if (AnimSyncPort != null && AnimSyncPort.connected && AnimSyncPort.connections.Any())
                    Data.AnimSyncNodeID = (AnimSyncPort.connections.First().input.node as NovellaNodeView).Data.NodeID;
                else
                    Data.AnimSyncNodeID = "";

                if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    Data.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                else Data.NextNodeID = "";
            }
        }
    }
}