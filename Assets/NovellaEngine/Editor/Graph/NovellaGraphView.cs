using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaStartNodeView : Node
    {
        public System.Action OnSelectedAction;
        public void SetupView() { var titleLabel = titleContainer.Q<Label>(); if (titleLabel != null) { titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter; titleLabel.style.flexGrow = 1; } }
        public override void OnSelected() { base.OnSelected(); OnSelectedAction?.Invoke(); }
    }

    public class NovellaEdgeConnectorListener : IEdgeConnectorListener
    {
        private NovellaGraphView _graphView;
        public NovellaEdgeConnectorListener(NovellaGraphView graphView) => _graphView = graphView;

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            var port = edge.output != null ? edge.output : edge.input;
            if (port == null) return;
            Vector2 localPos = _graphView.contentViewContainer.WorldToLocal(Event.current.mousePosition);
            var menu = new GenericMenu();

            string syncPortName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
            if (port.portName == syncPortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Audio / BGM Node", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
            }
            else
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Dialogue Block", "Блок Диалогов")), false, () => _graphView.CreateNode(localPos, ENodeType.Dialogue, port));
                menu.AddItem(new GUIContent(ToolLang.Get("Branch Node", "Нода Развилки")), false, () => _graphView.CreateNode(localPos, ENodeType.Branch, port));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent(ToolLang.Get("Audio / BGM", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
                menu.AddItem(new GUIContent(ToolLang.Get("Variable / Logic", "Переменная / Логика")), false, () => _graphView.CreateNode(localPos, ENodeType.Variable, port));
                if (!(port.node is NovellaStartNodeView)) { menu.AddSeparator(""); menu.AddItem(new GUIContent(ToolLang.Get("END Node", "Конец Сцены")), false, () => _graphView.CreateNode(localPos, ENodeType.End, port)); }
            }

            menu.ShowAsContext();
        }
        public void OnDrop(GraphView graphView, Edge edge) { }
    }

    public class NovellaGraphView : GraphView
    {
        public readonly NovellaTree Tree;
        public NovellaGraphWindow Window { get; private set; }
        public System.Action<NovellaNodeView> OnNodeSelected;
        public System.Action OnStartNodeSelected;

        public NovellaStartNodeView StartNodeView { get; private set; }
        private NovellaEdgeConnectorListener _edgeListener;

        public NovellaGraphView(NovellaGraphWindow window, NovellaTree tree)
        {
            Window = window; Tree = tree;
            _edgeListener = new NovellaEdgeConnectorListener(this);
            SetupZoom(0.5f, 5.0f);
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger()); this.AddManipulator(new RectangleSelector());
            var grid = new GridBackground(); Insert(0, grid); grid.StretchToParentSize();
            AddElement(GenerateEntryPointNode());
            graphViewChanged += OnGraphViewChanged;

            RegisterCallback<AttachToPanelEvent>(e => Undo.undoRedoPerformed += OnUndoRedo);
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= OnUndoRedo);
        }

        private void OnUndoRedo() => LoadGraph();

        private Edge ConnectPorts(Port output, Port input)
        {
            var edge = new Edge { output = output, input = input };
            edge.input.Connect(edge);
            edge.output.Connect(edge);
            return edge;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.elementsToRemove != null || change.edgesToCreate != null || change.movedElements != null)
            {
                Undo.RegisterCompleteObjectUndo(Tree, "Graph Modification");
                string syncPortName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");

                if (change.elementsToRemove != null)
                {
                    foreach (var element in change.elementsToRemove)
                    {
                        if (element is NovellaNodeView nodeView) Tree.Nodes.Remove(nodeView.Data);
                        if (element is Edge edge)
                        {
                            var outNode = edge.output?.node as NovellaNodeView;
                            var inNode = edge.input?.node as NovellaNodeView;
                            if (outNode != null && inNode != null)
                            {
                                if (outNode.AudioSyncPort != null && edge.output == outNode.AudioSyncPort)
                                {
                                    outNode.Data.AudioSyncNodeID = "";
                                    inNode.ToggleAudioNextPort(false);
                                }
                                else
                                {
                                    if (outNode.Data.NodeType == ENodeType.Dialogue || outNode.Data.NodeType == ENodeType.Event || outNode.Data.NodeType == ENodeType.Audio || outNode.Data.NodeType == ENodeType.Variable)
                                        outNode.Data.NextNodeID = "";
                                    else if (outNode.Data.NodeType == ENodeType.Branch)
                                    {
                                        int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                        if (portIdx >= 0 && portIdx < outNode.Data.Choices.Count) outNode.Data.Choices[portIdx].NextNodeID = "";
                                    }
                                }
                            }
                        }
                    }
                }

                if (change.edgesToCreate != null)
                {
                    foreach (var edge in change.edgesToCreate)
                    {
                        var outNode = edge.output?.node as NovellaNodeView;
                        var inNode = edge.input?.node as NovellaNodeView;
                        if (outNode != null && inNode != null)
                        {
                            if (outNode.AudioSyncPort != null && edge.output == outNode.AudioSyncPort)
                            {
                                outNode.Data.AudioSyncNodeID = inNode.Data.NodeID;
                                inNode.ToggleAudioNextPort(true);

                                if (inNode.OutputPort != null && inNode.OutputPort.connected)
                                {
                                    var connections = inNode.OutputPort.connections.ToList();
                                    EditorApplication.delayCall += () => { DeleteElements(connections); };
                                }
                            }
                            else
                            {
                                if (outNode.Data.NodeType == ENodeType.Dialogue || outNode.Data.NodeType == ENodeType.Event || outNode.Data.NodeType == ENodeType.Audio || outNode.Data.NodeType == ENodeType.Variable)
                                    outNode.Data.NextNodeID = inNode.Data.NodeID;
                                else if (outNode.Data.NodeType == ENodeType.Branch)
                                {
                                    int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                    if (portIdx >= 0 && portIdx < outNode.Data.Choices.Count) outNode.Data.Choices[portIdx].NextNodeID = inNode.Data.NodeID;
                                }
                            }
                        }
                    }
                }

                if (change.movedElements != null)
                {
                    foreach (var element in change.movedElements)
                    {
                        if (element is NovellaNodeView n) n.Data.GraphPosition = n.GetPosition().position;
                        if (element is NovellaStartNodeView s) Tree.StartPosition = s.GetPosition().position;
                    }
                }

                EditorApplication.delayCall += () => { if (this != null && Tree != null) { SyncGraphToData(); Window.MarkUnsaved(); Window.rootVisualElement.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals()); Window.Repaint(); } };
            }
            return change;
        }

        private Node GenerateEntryPointNode()
        {
            var node = new NovellaStartNodeView { title = "🟢 " + ToolLang.Get("START", "СТАРТ"), name = "START_NODE" };
            node.OnSelectedAction = () => { OnStartNodeSelected?.Invoke(); };
            node.style.backgroundColor = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
            node.style.width = 140; node.style.height = 95;
            var port = GeneratePort(node, Direction.Output); port.portName = ToolLang.Get("Start", "Старт");
            node.outputContainer.Add(port); node.SetupView();
            node.RegisterCallback<GeometryChangedEvent>(evt => { var collapseBtn = node.titleButtonContainer.Q("collapse-button"); if (collapseBtn != null) collapseBtn.style.display = DisplayStyle.None; });
            node.RefreshExpandedState(); node.RefreshPorts();
            node.SetPosition(new Rect(Tree.StartPosition, new Vector2(140, 95)));
            StartNodeView = node; return node;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 pos = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);
            evt.menu.AppendAction($"Node/{ToolLang.Get("Dialogue Block", "Блок Диалогов")}", (a) => CreateNode(pos, ENodeType.Dialogue));
            evt.menu.AppendAction($"Node/{ToolLang.Get("Branch Node", "Нода Развилки")}", (a) => CreateNode(pos, ENodeType.Branch));
            evt.menu.AppendSeparator("Node/");
            evt.menu.AppendAction($"Node/{ToolLang.Get("Audio / BGM", "Аудио / Музыка")}", (a) => CreateNode(pos, ENodeType.Audio));
            evt.menu.AppendAction($"Node/{ToolLang.Get("Variable / Logic", "Переменная / Логика")}", (a) => CreateNode(pos, ENodeType.Variable));
            evt.menu.AppendSeparator("Node/");
            evt.menu.AppendAction($"Node/{ToolLang.Get("END Node", "Конец Сцены")}", (a) => CreateNode(pos, ENodeType.End));
            base.BuildContextualMenu(evt);
        }

        public void CreateNode(Vector2 position, ENodeType type, Port autoConnectPort = null)
        {
            Undo.RegisterCompleteObjectUndo(Tree, "Create Node");
            int count = Tree.Nodes.Count(n => n.NodeType == type) + 1;

            var nodeData = new NovellaNodeData
            {
                NodeID = type.ToString() + "_" + System.Guid.NewGuid().ToString().Substring(0, 5),
                NodeTitle = $"{type} {count}",
                NodeType = type,
                GraphPosition = position
            };

            if (autoConnectPort != null && autoConnectPort.node is NovellaNodeView parentNode)
            {
                var pData = parentNode.Data;
                foreach (var charInPrev in pData.ActiveCharacters)
                {
                    nodeData.ActiveCharacters.Add(new CharacterInDialogue
                    {
                        CharacterAsset = charInPrev.CharacterAsset,
                        Plane = charInPrev.Plane,
                        Scale = charInPrev.Scale,
                        Emotion = charInPrev.Emotion,
                        PosX = charInPrev.PosX,
                        PosY = charInPrev.PosY
                    });
                }
            }

            if (type == ENodeType.Branch)
            {
                nodeData.Choices.Add(new NovellaChoice());
                nodeData.Choices.Add(new NovellaChoice());
            }

            Tree.Nodes.Add(nodeData);
            var view = new NovellaNodeView(nodeData, this);
            view.SetPosition(new Rect(position, new Vector2(200, 150))); AddElement(view);

            if (autoConnectPort != null && type != ENodeType.Character)
            {
                string syncPortName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
                if (autoConnectPort.direction == Direction.Output)
                {
                    if (autoConnectPort.portName == syncPortName && type == ENodeType.Audio)
                    {
                        AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                        view.ToggleAudioNextPort(true);
                    }
                    else if (view.InputPort != null && autoConnectPort.portName != syncPortName)
                    {
                        if (autoConnectPort.capacity == Port.Capacity.Single && autoConnectPort.connected) DeleteElements(autoConnectPort.connections);
                        AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                    }
                }
                else if (autoConnectPort.direction == Direction.Input)
                {
                    if (view.OutputPort != null)
                    {
                        if (autoConnectPort.capacity == Port.Capacity.Single && autoConnectPort.connected) DeleteElements(autoConnectPort.connections);
                        AddElement(ConnectPorts(view.OutputPort, autoConnectPort));
                    }
                }
            }
            Window.MarkUnsaved();
        }

        public void PropagateScene(NovellaNodeData source)
        {
            Undo.RegisterCompleteObjectUndo(Tree, "Propagate Scene");
            HashSet<string> processedNodes = new HashSet<string> { source.NodeID };
            Queue<string> toProcess = new Queue<string>();

            if (!string.IsNullOrEmpty(source.NextNodeID)) toProcess.Enqueue(source.NextNodeID);

            while (toProcess.Count > 0)
            {
                string currentID = toProcess.Dequeue();
                if (processedNodes.Contains(currentID)) continue;

                var node = Tree.Nodes.FirstOrDefault(n => n.NodeID == currentID);
                if (node == null || node.NodeType == ENodeType.End) continue;

                node.ActiveCharacters.Clear();
                foreach (var c in source.ActiveCharacters)
                {
                    node.ActiveCharacters.Add(new CharacterInDialogue
                    {
                        CharacterAsset = c.CharacterAsset,
                        Plane = c.Plane,
                        Scale = c.Scale,
                        Emotion = c.Emotion,
                        PosX = c.PosX,
                        PosY = c.PosY
                    });
                }

                processedNodes.Add(currentID);

                if (node.NodeType == ENodeType.Branch) continue;

                if (!string.IsNullOrEmpty(node.NextNodeID)) toProcess.Enqueue(node.NextNodeID);
            }

            this.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals());
            Window.MarkUnsaved();
        }

        public Port GeneratePort(Node node, Direction portDirection, Port.Capacity capacity = Port.Capacity.Single)
        {
            var port = node.InstantiatePort(Orientation.Horizontal, portDirection, capacity, typeof(float));
            port.AddManipulator(new EdgeConnector<Edge>(_edgeListener)); return port;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compPorts = new List<Port>();
            string syncPortName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");

            ports.ForEach(port => {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    bool isAudioSyncOut = (startPort.portName == syncPortName && startPort.direction == Direction.Output) ||
                                          (port.portName == syncPortName && port.direction == Direction.Output);

                    if (isAudioSyncOut)
                    {
                        Port inPort = startPort.direction == Direction.Input ? startPort : port;
                        if (inPort.node is NovellaNodeView targetNode && targetNode.Data.NodeType == ENodeType.Audio)
                        {
                            compPorts.Add(port);
                        }
                    }
                    else
                    {
                        compPorts.Add(port);
                    }
                }
            });
            return compPorts;
        }

        public void SyncGraphToData()
        {
            if (Tree == null) return;
            if (StartNodeView != null)
            {
                Tree.StartPosition = StartNodeView.GetPosition().position;
                var startPort = StartNodeView.outputContainer.Q<Port>();
                if (startPort != null && startPort.connected && startPort.connections.Any())
                {
                    var targetNode = startPort.connections.First().input.node as NovellaNodeView;
                    Tree.RootNodeID = targetNode != null ? targetNode.Data.NodeID : "";
                }
                else Tree.RootNodeID = "";
            }
            var updatedNodes = new List<NovellaNodeData>();
            foreach (var nodeView in nodes.OfType<NovellaNodeView>()) { nodeView.SaveNodeData(); updatedNodes.Add(nodeView.Data); }
            Tree.Nodes = updatedNodes;
        }

        public void LoadGraph()
        {
            if (Tree == null) return;
            var existingNodes = nodes.ToList().OfType<NovellaNodeView>().ToList();
            foreach (var n in existingNodes) RemoveElement(n);
            foreach (var e in edges.ToList()) RemoveElement(e);

            var nodeDict = new Dictionary<string, NovellaNodeView>();
            foreach (var data in Tree.Nodes)
            {
                var view = new NovellaNodeView(data, this);
                view.SetPosition(new Rect(data.GraphPosition, new Vector2(200, 150))); AddElement(view); nodeDict.Add(data.NodeID, view);
            }

            if (!string.IsNullOrEmpty(Tree.RootNodeID) && nodeDict.TryGetValue(Tree.RootNodeID, out var rootNodeView))
            {
                var startP = StartNodeView.outputContainer.Q<Port>();
                if (startP != null && rootNodeView.InputPort != null) AddElement(ConnectPorts(startP, rootNodeView.InputPort));
            }

            foreach (var nodeView in nodeDict.Values)
            {
                if (nodeView.Data.NodeType == ENodeType.End || nodeView.Data.NodeType == ENodeType.Character) continue;

                if ((nodeView.Data.NodeType == ENodeType.Dialogue || nodeView.Data.NodeType == ENodeType.Event) && !string.IsNullOrEmpty(nodeView.Data.AudioSyncNodeID))
                {
                    if (nodeDict.TryGetValue(nodeView.Data.AudioSyncNodeID, out var audioNode))
                    {
                        if (nodeView.AudioSyncPort != null && audioNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(nodeView.AudioSyncPort, audioNode.InputPort));
                            audioNode.ToggleAudioNextPort(true);
                        }
                    }
                }

                if (nodeView.Data.NodeType == ENodeType.Branch)
                {
                    var ports = nodeView.outputContainer.Query<Port>().ToList();
                    for (int i = 0; i < nodeView.Data.Choices.Count; i++)
                    {
                        if (i < ports.Count && !string.IsNullOrEmpty(nodeView.Data.Choices[i].NextNodeID) && nodeDict.TryGetValue(nodeView.Data.Choices[i].NextNodeID, out var targetNode))
                        {
                            if (targetNode.InputPort != null) AddElement(ConnectPorts(ports[i], targetNode.InputPort));
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(nodeView.Data.NextNodeID) && nodeDict.TryGetValue(nodeView.Data.NextNodeID, out var targetNode))
                        if (nodeView.OutputPort != null && targetNode.InputPort != null) AddElement(ConnectPorts(nodeView.OutputPort, targetNode.InputPort));
                }
            }
        }
    }
}