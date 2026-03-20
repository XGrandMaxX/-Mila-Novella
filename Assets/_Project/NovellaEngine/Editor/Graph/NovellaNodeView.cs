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

        private readonly NovellaGraphView _graphView;
        private readonly Label _pinLabel;

        public NovellaNodeView(NovellaNodeData data, NovellaGraphView graphView)
        {
            Data = data;
            _graphView = graphView;

            if (Data.NodeType != ENodeType.Character)
            {
                InputPort = _graphView.GeneratePort(this, Direction.Input, Port.Capacity.Multi);
                InputPort.portName = Data.NodeType == ENodeType.End ? ToolLang.Get("Close", "Конец") : ToolLang.Get("Input", "Вход");
                inputContainer.Add(InputPort);

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event)
                {
                    OutputPort = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                    OutputPort.portName = ToolLang.Get("Next", "Далее");
                    outputContainer.Add(OutputPort);
                }
                else if (Data.NodeType == ENodeType.Branch)
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
            if (titleLabel != null)
            {
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                titleLabel.style.flexGrow = 1;
                titleLabel.style.marginLeft = titleLabel.style.marginRight = 0;
            }
            titleContainer.style.justifyContent = Justify.Center;

            this.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var collapseBtn = this.titleButtonContainer.Q("collapse-button");
                if (collapseBtn != null) collapseBtn.style.display = DisplayStyle.None;
            });

            RefreshVisuals();
            RefreshExpandedState();
            RefreshPorts();
        }

        public void DrawBranchPorts()
        {
            if (Data.NodeType != ENodeType.Branch) return;

            var existingPorts = outputContainer.Query<Port>().ToList();
            foreach (var port in existingPorts)
            {
                if (port.connected) _graphView.DeleteElements(port.connections);
                outputContainer.Remove(port);
            }

            for (int i = 0; i < Data.Choices.Count; i++)
            {
                var port = _graphView.GeneratePort(this, Direction.Output, Port.Capacity.Single);
                port.portName = ToolLang.Get($"Choice {i + 1}", $"Выбор {i + 1}");
                outputContainer.Add(port);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshVisuals()
        {
            if (Data == null || _pinLabel == null) return;

            title = string.IsNullOrEmpty(Data.NodeTitle) ? Data.NodeID : Data.NodeTitle;

            _pinLabel.style.display = Data.IsPinned ? DisplayStyle.Flex : DisplayStyle.None;

            if (Data.NodeType == ENodeType.End)
                titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.1f, 0.1f));
            else if (Data.NodeType == ENodeType.Branch)
                titleContainer.style.backgroundColor = new StyleColor(new Color(0.6f, 0.4f, 0.1f));
            else if (Data.NodeType == ENodeType.Character)
                titleContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.4f, 0.6f));
            else
                titleContainer.style.backgroundColor = new StyleColor(Data.NodeCustomColor);

            extensionContainer.Clear();
            bool hasExtensionData = false;

            if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event)
            {
                if (Data.Speaker != null)
                {
                    string moodTxt = Data.Mood == "Default" ? "" : $" ({Data.Mood})";
                    var speakerBlock = new Label($"🗣 {Data.Speaker.name}{moodTxt}")
                    {
                        style = {
                            backgroundColor = new StyleColor(Data.Speaker.ThemeColor),
                            color = Color.white,
                            paddingTop = 4, paddingBottom = 4, paddingLeft = 8, paddingRight = 8,
                            marginTop = 4, marginBottom = 4, marginLeft = 5, marginRight = 5,
                            borderBottomLeftRadius = 5, borderBottomRightRadius = 5,
                            borderTopLeftRadius = 5, borderTopRightRadius = 5,

                            borderTopWidth = 2, borderBottomWidth = 2, borderLeftWidth = 2, borderRightWidth = 2,
                            borderTopColor = new Color(1f, 1f, 1f, 0.8f),
                            borderBottomColor = new Color(1f, 1f, 1f, 0.8f),
                            borderLeftColor = new Color(1f, 1f, 1f, 0.8f),
                            borderRightColor = new Color(1f, 1f, 1f, 0.8f),
                            unityFontStyleAndWeight = FontStyle.Bold,
                            fontSize = 12
                        }
                    };
                    extensionContainer.Add(speakerBlock);
                    hasExtensionData = true;
                }

                if (Data.Speaker != null && Data.ActiveCharacters != null && Data.ActiveCharacters.Count > 0)
                {
                    var separator = new VisualElement()
                    {
                        style = {
                            height = 1,
                            backgroundColor = new Color(0.3f, 0.3f, 0.3f),
                            marginTop = 2, marginBottom = 2, marginLeft = 10, marginRight = 10
                        }
                    };
                    extensionContainer.Add(separator);
                }

                if (Data.ActiveCharacters != null && Data.ActiveCharacters.Count > 0)
                {
                    foreach (var charData in Data.ActiveCharacters)
                    {
                        if (charData.CharacterAsset != null)
                        {
                            string moodTxt = charData.Emotion == "Default" ? "" : $" ({charData.Emotion})";
                            var block = new Label($"👤 {charData.CharacterAsset.name}{moodTxt}")
                            {
                                style = {
                                    backgroundColor = new StyleColor(charData.CharacterAsset.ThemeColor),
                                    color = Color.white,
                                    paddingTop = 3, paddingBottom = 3, paddingLeft = 8, paddingRight = 8,
                                    marginTop = 2, marginBottom = 2, marginLeft = 5, marginRight = 5,
                                    borderBottomLeftRadius = 5, borderBottomRightRadius = 5,
                                    borderTopLeftRadius = 5, borderTopRightRadius = 5,
                                    unityFontStyleAndWeight = FontStyle.Bold
                                }
                            };
                            extensionContainer.Add(block);
                        }
                    }
                    hasExtensionData = true;
                }
            }

            if (hasExtensionData)
            {
                extensionContainer.style.display = DisplayStyle.Flex;
                extensionContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
                extensionContainer.style.paddingTop = 5;
                extensionContainer.style.paddingBottom = 5;
            }
            else
            {
                extensionContainer.style.display = DisplayStyle.None;
            }

            this.MarkDirtyRepaint();
            RefreshExpandedState();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            _graphView?.OnNodeSelected?.Invoke(this);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            if (Data != null) Data.GraphPosition = newPos.position;
        }

        public void SaveNodeData()
        {
            if (Data == null) return;
            Data.GraphPosition = GetPosition().position;
            if (Data.NodeType == ENodeType.End || Data.NodeType == ENodeType.Character) return;

            if (Data.NodeType == ENodeType.Branch)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < Data.Choices.Count; i++)
                {
                    if (i < ports.Count && ports[i].connected && ports[i].connections.Any())
                    {
                        Data.Choices[i].NextNodeID = ports[i].connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                    }
                    else
                    {
                        Data.Choices[i].NextNodeID = "";
                    }
                }
            }
            else
            {
                if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    Data.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                else Data.NextNodeID = "";
            }
        }
    }
}