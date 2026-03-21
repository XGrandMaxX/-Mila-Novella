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
                }

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Audio || Data.NodeType == ENodeType.Variable)
                {
                    OutputPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    OutputPort.portName = ToolLang.Get("Next ➡", "Далее ➡");
                    outputContainer.Add(OutputPort);
                }
                else if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition)
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
                OutputPort.style.display = isSynced ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void DrawBranchPorts()
        {
            if (Data.NodeType != ENodeType.Branch && Data.NodeType != ENodeType.Condition) return;

            var existingPorts = outputContainer.Query<Port>().ToList();
            foreach (var port in existingPorts) { if (port.connected) _graphView.DeleteElements(port.connections); outputContainer.Remove(port); }

            for (int i = 0; i < Data.Choices.Count; i++)
            {
                var port = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);

                if (Data.NodeType == ENodeType.Branch) port.portName = ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}");
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
            title = titlePrefix + (string.IsNullOrEmpty(Data.NodeTitle) ? Data.NodeID : Data.NodeTitle);

            _pinLabel.style.display = Data.IsPinned ? DisplayStyle.Flex : DisplayStyle.None;

            titleContainer.style.borderBottomWidth = 0;

            if (Data.NodeType == ENodeType.End) titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.1f, 0.1f));
            else if (Data.NodeType == ENodeType.Branch) titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.4f, 0.1f));
            else if (Data.NodeType == ENodeType.Character) titleContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.4f, 0.6f));
            else if (Data.NodeType == ENodeType.Audio) titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.2f, 0.5f));
            else if (Data.NodeType == ENodeType.Variable) titleContainer.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.6f));
            else if (Data.NodeType == ENodeType.Condition)
            {
                titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.4f, 0.1f));
                titleContainer.style.borderBottomColor = new StyleColor(new Color(1f, 0.7f, 0f));
                titleContainer.style.borderBottomWidth = 3;
            }
            else titleContainer.style.backgroundColor = new StyleColor(Data.NodeCustomColor);

            extensionContainer.Clear();
            bool hasExtensionData = false;

            if (Data.NodeType == ENodeType.Note)
            {
                titleContainer.style.display = Data.ShowBackground ? DisplayStyle.Flex : DisplayStyle.None;

                var tLabel = titleContainer.Q<Label>();
                if (tLabel != null)
                {
                    tLabel.style.color = Data.NoteTitleColor;
                    tLabel.style.fontSize = Data.NoteTitleFontSize > 0 ? Data.NoteTitleFontSize : 14;
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

                var textLabel = new Label(Data.NoteText)
                {
                    style = {
                        whiteSpace = WhiteSpace.Normal,
                        color = Data.NoteTextColor,
                        fontSize = Data.FontSize > 0 ? Data.FontSize : 14,
                        paddingTop = 10, paddingBottom = 10, paddingLeft = 10, paddingRight = 10,
                        maxWidth = 400
                    }
                };
                extensionContainer.Add(textLabel);

                if (!string.IsNullOrEmpty(Data.NoteURL))
                {
                    var linkBtn = new Button(() => Application.OpenURL(Data.NoteURL)) { text = "🔗 " + ToolLang.Get("Open Link", "Открыть ссылку") };
                    linkBtn.style.marginTop = 10; linkBtn.style.marginBottom = 10; linkBtn.style.width = 150; linkBtn.style.alignSelf = Align.Center;
                    extensionContainer.Add(linkBtn);
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
                if (!Data.SyncWithDialogue)
                {
                    string audioName = Data.AudioAsset != null ? Data.AudioAsset.name : ToolLang.Get("None", "Пусто");
                    string act = Data.AudioAction == EAudioAction.Play ? "▶" : "⏸";
                    var audioBlock = new Label($"{act} [{Data.AudioChannel}] {audioName}") { style = { color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8 } };
                    extensionContainer.Add(audioBlock); hasExtensionData = true;
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
                    int noSpeakerCount = Data.DialogueLines.Count(l => l.Speaker == null);

                    foreach (var spk in distinctSpeakers)
                    {
                        int linesCount = Data.DialogueLines.Count(l => l.Speaker == spk);
                        var speakerBlock = new Label($"🗣 {spk.name} ({linesCount})") { style = { backgroundColor = new StyleColor(spk.ThemeColor), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2, borderTopColor = new Color(1f, 1f, 1f, 0.8f), borderBottomColor = new Color(1f, 1f, 1f, 0.8f), borderLeftColor = new Color(1f, 1f, 1f, 0.8f), borderRightColor = new Color(1f, 1f, 1f, 0.8f), unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
                        extensionContainer.Add(speakerBlock);
                    }

                    if (noSpeakerCount > 0)
                    {
                        var textOnlyBlock = new Label($"📝 {ToolLang.Get("Text Only", "Без спикера")} ({noSpeakerCount})") { style = { backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f)), color = Color.white, paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8, marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5, borderBottomLeftRadius = 5, borderBottomRightRadius = 5, borderTopLeftRadius = 5, borderTopRightRadius = 5, unityFontStyleAndWeight = FontStyle.Bold, fontSize = 11 } };
                        extensionContainer.Add(textOnlyBlock);
                    }

                    hasExtensionData = true;
                }
            }

                if (hasExtensionData)
            {
                extensionContainer.style.display = DisplayStyle.Flex;
                extensionContainer.style.backgroundColor = Data.NodeType == ENodeType.Note && !Data.ShowBackground ? new StyleColor(Color.clear) : new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.8f));
                extensionContainer.style.paddingTop = 5; extensionContainer.style.paddingBottom = 5;
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

            if (Data.NodeType == ENodeType.Branch || Data.NodeType == ENodeType.Condition)
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

                if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    Data.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                else Data.NextNodeID = "";
            }
        }
    }
}