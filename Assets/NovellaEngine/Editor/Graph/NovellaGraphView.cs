using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaClipboard : ScriptableObject
    {
        public List<NovellaNodeData> Nodes = new List<NovellaNodeData>();
        public List<NovellaGroupData> Groups = new List<NovellaGroupData>();
    }

    public class NovellaStartNodeView : Node
    {
        public System.Action OnSelectedAction;

        public NovellaStartNodeView()
        {
            this.capabilities &= ~Capabilities.Deletable;
            this.capabilities &= ~Capabilities.Copiable;
            this.capabilities &= ~Capabilities.Groupable;
        }

        public void SetupView()
        {
            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null) { titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter; titleLabel.style.flexGrow = 1; }
            RefreshState();
        }

        public override void OnSelected() { base.OnSelected(); OnSelectedAction?.Invoke(); }

        public void RefreshState()
        {
            inputContainer.style.display = DisplayStyle.None;
            extensionContainer.style.display = DisplayStyle.None;

            style.width = 160;
            style.height = StyleKeyword.Auto;

            titleContainer.style.backgroundColor = new StyleColor(new Color(0.2f, 0.7f, 0.3f, 1f));
            titleContainer.style.height = 40;
            titleContainer.style.justifyContent = Justify.Center;

            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null)
            {
                titleLabel.style.fontSize = 18;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.color = Color.white;
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                titleLabel.style.marginTop = 0;
                titleLabel.style.marginBottom = 0;
            }

            title = ToolLang.Get("▶ START", "▶ СТАРТ");

            var nodeBorder = this.Q("node-border");
            if (nodeBorder != null)
            {
                nodeBorder.style.backgroundColor = new StyleColor(new Color(0.15f, 0.25f, 0.15f, 1f));
                Color borderColor = new Color(0.4f, 1f, 0.5f);
                nodeBorder.style.borderTopColor = new StyleColor(borderColor);
                nodeBorder.style.borderBottomColor = new StyleColor(borderColor);
                nodeBorder.style.borderLeftColor = new StyleColor(borderColor);
                nodeBorder.style.borderRightColor = new StyleColor(borderColor);
                nodeBorder.style.borderTopWidth = 2; nodeBorder.style.borderBottomWidth = 2;
                nodeBorder.style.borderLeftWidth = 2; nodeBorder.style.borderRightWidth = 2;
                nodeBorder.style.borderTopLeftRadius = 15; nodeBorder.style.borderTopRightRadius = 15;
                nodeBorder.style.borderBottomLeftRadius = 15; nodeBorder.style.borderBottomRightRadius = 15;
            }
        }
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

            string audioPortName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
            string animPortName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");

            if (port.portName == audioPortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Audio / BGM Node", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
            }
            else if (port.portName == animPortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Animation / Tween", "Анимация / Эффект")), false, () => _graphView.CreateNode(localPos, ENodeType.Animation, port));
            }
            else
            {
                string storyCat = ToolLang.Get("📖 Story/", "📖 Сюжет/");
                string logicCat = ToolLang.Get("🔀 Logic/", "🔀 Логика/");
                string cineCat = ToolLang.Get("🎬 Cinematography/", "🎬 Режиссура/");
                string sysCat = ToolLang.Get("⚙️ System/", "⚙️ Система/");

                menu.AddItem(new GUIContent(storyCat + ToolLang.Get("Dialogue Block", "Блок Диалогов")), false, () => _graphView.CreateNode(localPos, ENodeType.Dialogue, port));
                menu.AddItem(new GUIContent(storyCat + ToolLang.Get("Sticky Note", "Текстовая Заметка")), false, () => _graphView.CreateNode(localPos, ENodeType.Note, port));

                menu.AddItem(new GUIContent(logicCat + ToolLang.Get("Branch Node", "Нода Развилки")), false, () => _graphView.CreateNode(localPos, ENodeType.Branch, port));
                menu.AddItem(new GUIContent(logicCat + ToolLang.Get("Condition (If-Else)", "Условие (If-Else)")), false, () => _graphView.CreateNode(localPos, ENodeType.Condition, port));
                menu.AddItem(new GUIContent(logicCat + ToolLang.Get("Random (Chance)", "Случайность (Шанс)")), false, () => _graphView.CreateNode(localPos, ENodeType.Random, port));
                menu.AddItem(new GUIContent(logicCat + ToolLang.Get("Variable / Logic", "Переменная / Логика")), false, () => _graphView.CreateNode(localPos, ENodeType.Variable, port));

                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Background / CG", "Фон / Сцена")), false, () => _graphView.CreateNode(localPos, ENodeType.Background, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Audio / BGM", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Animation / Tween", "Анимация / Эффект")), false, () => _graphView.CreateNode(localPos, ENodeType.Animation, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Wait (Delay)", "Ожидание (Пауза)")), false, () => _graphView.CreateNode(localPos, ENodeType.Wait, port));

                menu.AddItem(new GUIContent(sysCat + ToolLang.Get("Event Broadcast", "Вызов События")), false, () => _graphView.CreateNode(localPos, ENodeType.EventBroadcast, port));

                if (!(port.node is NovellaStartNodeView))
                    menu.AddItem(new GUIContent(sysCat + ToolLang.Get("END Node", "Конец Сцены")), false, () => _graphView.CreateNode(localPos, ENodeType.End, port));
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
        public System.Action<NovellaGroupView> OnGroupSelected;

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

            serializeGraphElements = OnSerializeGraphElements;
            unserializeAndPaste = OnUnserializeAndPaste;
            canPasteSerializedData = (data) => !string.IsNullOrEmpty(data);
        }

        private string OnSerializeGraphElements(IEnumerable<GraphElement> elements)
        {
            if (elements == null) return "";

            var nodesToCopy = elements.OfType<NovellaNodeView>().Select(n => n.Data).ToList();
            var groupsToCopy = elements.OfType<NovellaGroupView>().Select(g => g.Data).ToList();

            if (nodesToCopy.Count == 0 && groupsToCopy.Count == 0) return "";

            var clipboard = ScriptableObject.CreateInstance<NovellaClipboard>();
            clipboard.Nodes = nodesToCopy;
            clipboard.Groups = groupsToCopy;

            string json = EditorJsonUtility.ToJson(clipboard);
            Object.DestroyImmediate(clipboard);
            return json;
        }

        private void OnUnserializeAndPaste(string operationName, string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            var clipboard = ScriptableObject.CreateInstance<NovellaClipboard>();
            try { EditorJsonUtility.FromJsonOverwrite(data, clipboard); }
            catch { Object.DestroyImmediate(clipboard); return; }

            if (clipboard.Nodes == null || clipboard.Groups == null || (clipboard.Nodes.Count == 0 && clipboard.Groups.Count == 0))
            {
                Object.DestroyImmediate(clipboard); return;
            }

            Undo.RegisterCompleteObjectUndo(Tree, "Paste Elements");
            ClearSelection();

            var idMap = new Dictionary<string, string>();

            foreach (var node in clipboard.Nodes)
            {
                string oldId = node.NodeID;
                string newId = node.NodeType.ToString() + "_" + System.Guid.NewGuid().ToString().Substring(0, 5);
                node.NodeID = newId; idMap[oldId] = newId;
                node.GraphPosition += new Vector2(50, 50);

                foreach (var choice in node.Choices) choice.PortID = "Choice_" + System.Guid.NewGuid().ToString().Substring(0, 5);
            }

            foreach (var node in clipboard.Nodes)
            {
                if (idMap.ContainsKey(node.NextNodeID)) node.NextNodeID = idMap[node.NextNodeID];
                else node.NextNodeID = "";

                if (node.AudioSyncNodeID != null && idMap.ContainsKey(node.AudioSyncNodeID)) node.AudioSyncNodeID = idMap[node.AudioSyncNodeID];
                else node.AudioSyncNodeID = "";

                if (node.AnimSyncNodeID != null && idMap.ContainsKey(node.AnimSyncNodeID)) node.AnimSyncNodeID = idMap[node.AnimSyncNodeID];
                else node.AnimSyncNodeID = "";

                foreach (var choice in node.Choices)
                {
                    if (idMap.ContainsKey(choice.NextNodeID)) choice.NextNodeID = idMap[choice.NextNodeID];
                    else choice.NextNodeID = "";
                }
                Tree.Nodes.Add(node);
            }

            var newGroupIds = new List<string>();
            foreach (var group in clipboard.Groups)
            {
                string oldId = group.GroupID; string newId = "Group_" + System.Guid.NewGuid().ToString().Substring(0, 5);
                group.GroupID = newId; group.Position.position += new Vector2(50, 50);

                var validNodes = new List<string>();
                foreach (var oldNodeId in group.ContainedNodeIDs) if (idMap.TryGetValue(oldNodeId, out string newNodeId)) validNodes.Add(newNodeId);
                group.ContainedNodeIDs = validNodes; Tree.Groups.Add(group); newGroupIds.Add(newId);
            }

            var newIds = idMap.Values.ToList();

            EditorApplication.delayCall += () =>
            {
                if (this == null || Window == null) return;
                LoadGraph(); ClearSelection();
                foreach (var nv in nodes.ToList().OfType<NovellaNodeView>()) if (newIds.Contains(nv.Data.NodeID)) AddToSelection(nv);
                foreach (var gv in graphElements.ToList().OfType<NovellaGroupView>()) if (newGroupIds.Contains(gv.Data.GroupID)) AddToSelection(gv);
                Window.MarkUnsaved();
            };

            Object.DestroyImmediate(clipboard);
        }

        private void OnUndoRedo()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null || Window == null) return;
                LoadGraph();
                foreach (var nv in nodes.ToList().OfType<NovellaNodeView>()) nv.RefreshVisuals();
                Window.Repaint();
            };
        }

        private Edge ConnectPorts(Port output, Port input)
        {
            var edge = new Edge { output = output, input = input };
            edge.input.Connect(edge); edge.output.Connect(edge);
            return edge;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (change.elementsToRemove != null || change.edgesToCreate != null || change.movedElements != null)
            {
                Undo.RegisterCompleteObjectUndo(Tree, "Graph Modification");

                if (change.elementsToRemove != null)
                {
                    foreach (var element in change.elementsToRemove)
                    {
                        if (element is NovellaNodeView nodeView) Tree.Nodes.Remove(nodeView.Data);
                        if (element is NovellaGroupView groupView) Tree.Groups.Remove(groupView.Data);

                        if (element is Edge edge)
                        {
                            var outNode = edge.output?.node as NovellaNodeView;
                            var inNode = edge.input?.node as NovellaNodeView;
                            if (outNode != null && inNode != null)
                            {
                                if (outNode.AudioSyncPort != null && edge.output == outNode.AudioSyncPort)
                                {
                                    outNode.Data.AudioSyncNodeID = "";
                                    inNode.Data.SyncWithDialogue = false;
                                    inNode.ToggleAudioNextPort(false);
                                    inNode.RefreshVisuals();
                                    EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                                }
                                else if (outNode.AnimSyncPort != null && edge.output == outNode.AnimSyncPort)
                                {
                                    outNode.Data.AnimSyncNodeID = "";
                                    inNode.Data.SyncWithDialogue = false;
                                    inNode.ToggleAnimNextPort(false);
                                    inNode.RefreshVisuals();
                                    EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                                }
                                else
                                {
                                    if (outNode.Data.NodeType == ENodeType.Dialogue || outNode.Data.NodeType == ENodeType.Event || outNode.Data.NodeType == ENodeType.Audio || outNode.Data.NodeType == ENodeType.Variable || outNode.Data.NodeType == ENodeType.Wait || outNode.Data.NodeType == ENodeType.Background || outNode.Data.NodeType == ENodeType.Animation || outNode.Data.NodeType == ENodeType.EventBroadcast)
                                        outNode.Data.NextNodeID = "";
                                    else if (outNode.Data.NodeType == ENodeType.Branch || outNode.Data.NodeType == ENodeType.Condition || outNode.Data.NodeType == ENodeType.Random)
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
                                inNode.Data.SyncWithDialogue = true;
                                inNode.ToggleAudioNextPort(true);

                                if (inNode.OutputPort != null && inNode.OutputPort.connected)
                                {
                                    var connections = inNode.OutputPort.connections.ToList();
                                    EditorApplication.delayCall += () => { DeleteElements(connections); };
                                }
                                EditorApplication.delayCall += () => { if (inNode != null) inNode.RefreshVisuals(); };
                                EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                            }
                            else if (outNode.AnimSyncPort != null && edge.output == outNode.AnimSyncPort)
                            {
                                outNode.Data.AnimSyncNodeID = inNode.Data.NodeID;
                                inNode.Data.SyncWithDialogue = true;
                                inNode.ToggleAnimNextPort(true);

                                if (inNode.OutputPort != null && inNode.OutputPort.connected)
                                {
                                    var connections = inNode.OutputPort.connections.ToList();
                                    EditorApplication.delayCall += () => { DeleteElements(connections); };
                                }
                                EditorApplication.delayCall += () => { if (inNode != null) inNode.RefreshVisuals(); };
                                EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                            }
                            else
                            {
                                if (outNode.Data.NodeType == ENodeType.Dialogue || outNode.Data.NodeType == ENodeType.Event || outNode.Data.NodeType == ENodeType.Audio || outNode.Data.NodeType == ENodeType.Variable || outNode.Data.NodeType == ENodeType.Wait || outNode.Data.NodeType == ENodeType.Background || outNode.Data.NodeType == ENodeType.Animation || outNode.Data.NodeType == ENodeType.EventBroadcast)
                                    outNode.Data.NextNodeID = inNode.Data.NodeID;
                                else if (outNode.Data.NodeType == ENodeType.Branch || outNode.Data.NodeType == ENodeType.Condition || outNode.Data.NodeType == ENodeType.Random)
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
                        if (element is NovellaGroupView g) g.Data.Position = g.GetPosition();
                    }
                }
                else
                {
                    EditorApplication.delayCall += () =>
                    {
                        if (this != null && Tree != null)
                        {
                            SyncGraphToData();
                            Window.MarkUnsaved();
                            foreach (var nv in nodes.ToList().OfType<NovellaNodeView>()) nv.RefreshVisuals();
                            Window.Repaint();
                        }
                    };
                }
            }
            return change;
        }

        private Node GenerateEntryPointNode()
        {
            var node = new NovellaStartNodeView { name = "START_NODE" };
            node.OnSelectedAction = () => { OnStartNodeSelected?.Invoke(); };

            var port = GeneratePort(node, Direction.Output); port.portName = ToolLang.Get("Start", "Старт");
            node.outputContainer.Add(port); node.SetupView();

            node.RegisterCallback<GeometryChangedEvent>(evt => { var collapseBtn = node.titleButtonContainer.Q("collapse-button"); if (collapseBtn != null) collapseBtn.style.display = DisplayStyle.None; });
            node.RefreshExpandedState(); node.RefreshPorts();
            node.SetPosition(new Rect(Tree.StartPosition, new Vector2(180, 80)));
            StartNodeView = node; return node;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 pos = viewTransform.matrix.inverse.MultiplyPoint(evt.localMousePosition);

            var groupableNodes = selection.OfType<NovellaNodeView>().Where(n => n.GetContainingScope() == null).ToList();

            if (groupableNodes.Count > 1)
            {
                evt.menu.AppendAction(ToolLang.Get("📦 Group Selection", "📦 Сгруппировать выделенное"), (a) => GroupSelection());
                evt.menu.AppendSeparator("");
            }

            var selectedNodes = selection.OfType<NovellaNodeView>().ToList();
            var nodesInGroup = selectedNodes.Where(n => n.GetContainingScope() is NovellaGroupView).ToList();
            if (nodesInGroup.Count > 0)
            {
                evt.menu.AppendAction(ToolLang.Get("📤 Remove from Group", "📤 Убрать из группы"), (a) => RemoveNodesFromGroup(nodesInGroup));
                evt.menu.AppendSeparator("");
            }

            if (selection.OfType<NovellaGroupView>().Any())
            {
                evt.menu.AppendAction(ToolLang.Get("🔓 Delete Group (Keep Nodes)", "🔓 Разгруппировать (Удалить рамку)"), (a) => UngroupSelection());
                evt.menu.AppendSeparator("");
            }

            string storyCat = ToolLang.Get("📖 Story/", "📖 Сюжет/");
            string logicCat = ToolLang.Get("🔀 Logic/", "🔀 Логика/");
            string cineCat = ToolLang.Get("🎬 Cinematography/", "🎬 Режиссура/");
            string sysCat = ToolLang.Get("⚙️ System/", "⚙️ Система/");

            evt.menu.AppendAction(storyCat + ToolLang.Get("Dialogue Block", "Блок Диалогов"), (a) => CreateNode(pos, ENodeType.Dialogue));
            evt.menu.AppendAction(storyCat + ToolLang.Get("Sticky Note", "Текстовая Заметка"), (a) => CreateNode(pos, ENodeType.Note));

            evt.menu.AppendAction(logicCat + ToolLang.Get("Branch Node", "Нода Развилки"), (a) => CreateNode(pos, ENodeType.Branch));
            evt.menu.AppendAction(logicCat + ToolLang.Get("Condition (If-Else)", "Условие (If-Else)"), (a) => CreateNode(pos, ENodeType.Condition));
            evt.menu.AppendAction(logicCat + ToolLang.Get("Random (Chance)", "Случайность (Шанс)"), (a) => CreateNode(pos, ENodeType.Random));
            evt.menu.AppendAction(logicCat + ToolLang.Get("Variable / Logic", "Переменная / Логика"), (a) => CreateNode(pos, ENodeType.Variable));

            evt.menu.AppendAction(cineCat + ToolLang.Get("Background / CG", "Фон / Сцена"), (a) => CreateNode(pos, ENodeType.Background));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Audio / BGM", "Аудио / Музыка"), (a) => CreateNode(pos, ENodeType.Audio));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Animation / Tween", "Анимация / Эффект"), (a) => CreateNode(pos, ENodeType.Animation));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Wait (Delay)", "Ожидание (Пауза)"), (a) => CreateNode(pos, ENodeType.Wait));

            evt.menu.AppendAction(sysCat + ToolLang.Get("Event Broadcast", "Вызов События"), (a) => CreateNode(pos, ENodeType.EventBroadcast));

            if (!selection.OfType<NovellaStartNodeView>().Any())
                evt.menu.AppendAction(sysCat + ToolLang.Get("END Node", "Конец Сцены"), (a) => CreateNode(pos, ENodeType.End));

            base.BuildContextualMenu(evt);
        }

        private void RemoveNodesFromGroup(List<NovellaNodeView> nodes)
        {
            Undo.RegisterCompleteObjectUndo(Tree, "Remove From Group");
            foreach (var n in nodes) { var group = n.GetContainingScope() as NovellaGroupView; group?.RemoveElement(n); }
            Window.MarkUnsaved();
        }

        private void UngroupSelection()
        {
            var selectedGroups = selection.OfType<NovellaGroupView>().ToList();
            if (selectedGroups.Count == 0) return;

            int totalNodesInGroups = selectedGroups.Sum(g => g.containedElements.Count());
            if (totalNodesInGroups > 3)
            {
                if (!EditorUtility.DisplayDialog(ToolLang.Get("Ungroup Warning", "Разгруппировка"), ToolLang.Get($"You are about to ungroup {totalNodesInGroups} nodes. Are you sure you want to delete this group?", $"В этой рамке находится {totalNodesInGroups} нод. Вы уверены, что хотите удалить рамку (ноды останутся)?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена"))) return;
            }

            Undo.RegisterCompleteObjectUndo(Tree, "Ungroup");
            foreach (var group in selectedGroups)
            {
                var elements = group.containedElements.ToList();
                foreach (var el in elements) group.RemoveElement(el);
                Tree.Groups.Remove(group.Data); RemoveElement(group);
            }
            Window.MarkUnsaved();
        }

        private void GroupSelection()
        {
            var selectedNodes = selection.OfType<NovellaNodeView>().Where(n => n.GetContainingScope() == null).ToList();
            if (selectedNodes.Count < 2) return;

            Undo.RegisterCompleteObjectUndo(Tree, "Create Group");
            var groupData = new NovellaGroupData { GroupID = "Group_" + System.Guid.NewGuid().ToString().Substring(0, 5), Title = ToolLang.Get("New Group", "Новая Группа") };
            foreach (var n in selectedNodes) groupData.ContainedNodeIDs.Add(n.Data.NodeID);
            Tree.Groups.Add(groupData);

            var groupView = new NovellaGroupView(groupData, this); AddElement(groupView);
            foreach (var n in selectedNodes) groupView.AddElement(n);
            Window.MarkUnsaved();
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
                foreach (var charInPrev in parentNode.Data.ActiveCharacters)
                {
                    nodeData.ActiveCharacters.Add(new CharacterInDialogue { CharacterAsset = charInPrev.CharacterAsset, Plane = charInPrev.Plane, Scale = charInPrev.Scale, Emotion = charInPrev.Emotion, PosX = charInPrev.PosX, PosY = charInPrev.PosY });
                }
            }

            if (type == ENodeType.Branch) { nodeData.Choices.Add(new NovellaChoice()); nodeData.Choices.Add(new NovellaChoice()); }
            else if (type == ENodeType.Condition)
            {
                var trueChoice = new NovellaChoice(); trueChoice.LocalizedText.SetText("EN", "True"); trueChoice.LocalizedText.SetText("RU", "Истина");
                var falseChoice = new NovellaChoice(); falseChoice.LocalizedText.SetText("EN", "False"); falseChoice.LocalizedText.SetText("RU", "Ложь");
                nodeData.Choices.Add(trueChoice); nodeData.Choices.Add(falseChoice);
            }
            else if (type == ENodeType.Random)
            {
                nodeData.Choices.Add(new NovellaChoice() { ChanceWeight = 50 }); nodeData.Choices.Add(new NovellaChoice() { ChanceWeight = 50 });
            }

            Tree.Nodes.Add(nodeData);
            var view = new NovellaNodeView(nodeData, this);
            view.SetPosition(new Rect(position, new Vector2(200, 150))); AddElement(view);

            if (autoConnectPort != null && type != ENodeType.Character && type != ENodeType.Note)
            {
                string audioSyncName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
                string animSyncName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");

                if (autoConnectPort.direction == Direction.Output)
                {
                    if (autoConnectPort.portName == audioSyncName && type == ENodeType.Audio)
                    {
                        if (autoConnectPort.connected)
                        {
                            var oldEdges = autoConnectPort.connections.ToList();
                            foreach (var e in oldEdges) { if (e.input.node is NovellaNodeView oldIn) { oldIn.Data.SyncWithDialogue = false; oldIn.ToggleAudioNextPort(false); oldIn.RefreshVisuals(); } }
                            DeleteElements(oldEdges);
                        }
                        AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                        if (autoConnectPort.node is NovellaNodeView sourceNode) sourceNode.Data.AudioSyncNodeID = view.Data.NodeID;
                        view.Data.SyncWithDialogue = true; view.ToggleAudioNextPort(true); view.RefreshVisuals();
                    }
                    else if (autoConnectPort.portName == animSyncName && type == ENodeType.Animation)
                    {
                        if (autoConnectPort.connected)
                        {
                            var oldEdges = autoConnectPort.connections.ToList();
                            foreach (var e in oldEdges) { if (e.input.node is NovellaNodeView oldIn) { oldIn.Data.SyncWithDialogue = false; oldIn.ToggleAnimNextPort(false); oldIn.RefreshVisuals(); } }
                            DeleteElements(oldEdges);
                        }
                        AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                        if (autoConnectPort.node is NovellaNodeView sourceNode) sourceNode.Data.AnimSyncNodeID = view.Data.NodeID;
                        view.Data.SyncWithDialogue = true; view.ToggleAnimNextPort(true); view.RefreshVisuals();
                    }
                    else if (view.InputPort != null && autoConnectPort.portName != audioSyncName && autoConnectPort.portName != animSyncName)
                    {
                        if (autoConnectPort.capacity == Port.Capacity.Single && autoConnectPort.connected) DeleteElements(autoConnectPort.connections);
                        AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                    }
                }
                else if (autoConnectPort.direction == Direction.Input)
                {
                    if (view.OutputPort != null) { if (autoConnectPort.capacity == Port.Capacity.Single && autoConnectPort.connected) DeleteElements(autoConnectPort.connections); AddElement(ConnectPorts(view.OutputPort, autoConnectPort)); }
                }
            }
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
            string audioSyncName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
            string animSyncName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");

            ports.ForEach(port => {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    bool isStartAudioOut = (startPort.portName == audioSyncName && startPort.direction == Direction.Output);
                    bool isPortAudioOut = (port.portName == audioSyncName && port.direction == Direction.Output);

                    bool isStartAnimOut = (startPort.portName == animSyncName && startPort.direction == Direction.Output);
                    bool isPortAnimOut = (port.portName == animSyncName && port.direction == Direction.Output);

                    if (isStartAudioOut)
                    {
                        if (port.node is NovellaNodeView targetNode && targetNode.Data.NodeType == ENodeType.Audio)
                            if (!port.connections.Any(e => e.output.portName == audioSyncName)) compPorts.Add(port);
                    }
                    else if (isPortAudioOut)
                    {
                        if (startPort.node is NovellaNodeView targetNode && targetNode.Data.NodeType == ENodeType.Audio)
                            if (!startPort.connections.Any(e => e.output.portName == audioSyncName)) compPorts.Add(port);
                    }
                    else if (isStartAnimOut)
                    {
                        if (port.node is NovellaNodeView targetNode && targetNode.Data.NodeType == ENodeType.Animation)
                            if (!port.connections.Any(e => e.output.portName == animSyncName)) compPorts.Add(port);
                    }
                    else if (isPortAnimOut)
                    {
                        if (startPort.node is NovellaNodeView targetNode && targetNode.Data.NodeType == ENodeType.Animation)
                            if (!startPort.connections.Any(e => e.output.portName == animSyncName)) compPorts.Add(port);
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

            var updatedGroups = new List<NovellaGroupData>();
            foreach (var groupView in graphElements.ToList().OfType<NovellaGroupView>()) { groupView.SaveGroupData(); updatedGroups.Add(groupView.Data); }
            Tree.Groups = updatedGroups;
        }

        public void LoadGraph()
        {
            if (Tree == null) return;
            var existingNodes = nodes.ToList().OfType<NovellaNodeView>().ToList();
            foreach (var n in existingNodes) RemoveElement(n);
            foreach (var e in edges.ToList()) RemoveElement(e);
            foreach (var g in graphElements.ToList().OfType<NovellaGroupView>()) RemoveElement(g);

            var nodeDict = new Dictionary<string, NovellaNodeView>();
            foreach (var data in Tree.Nodes)
            {
                var view = new NovellaNodeView(data, this);
                view.SetPosition(new Rect(data.GraphPosition, new Vector2(200, 150))); AddElement(view); nodeDict.Add(data.NodeID, view);
            }

            foreach (var groupData in Tree.Groups)
            {
                var groupView = new NovellaGroupView(groupData, this); groupView.SetPosition(groupData.Position); AddElement(groupView);
                foreach (var nodeID in groupData.ContainedNodeIDs) { if (nodeDict.TryGetValue(nodeID, out var nodeView)) groupView.AddElement(nodeView); }
            }

            if (!string.IsNullOrEmpty(Tree.RootNodeID) && nodeDict.TryGetValue(Tree.RootNodeID, out var rootNodeView))
            {
                var startP = StartNodeView.outputContainer.Q<Port>();
                if (startP != null && rootNodeView.InputPort != null) AddElement(ConnectPorts(startP, rootNodeView.InputPort));
            }

            foreach (var nodeView in nodeDict.Values)
            {
                if (nodeView.Data.NodeType == ENodeType.End || nodeView.Data.NodeType == ENodeType.Character || nodeView.Data.NodeType == ENodeType.Note) continue;

                if ((nodeView.Data.NodeType == ENodeType.Dialogue || nodeView.Data.NodeType == ENodeType.Event) && !string.IsNullOrEmpty(nodeView.Data.AudioSyncNodeID))
                {
                    if (nodeDict.TryGetValue(nodeView.Data.AudioSyncNodeID, out var audioNode))
                    {
                        if (nodeView.AudioSyncPort != null && audioNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(nodeView.AudioSyncPort, audioNode.InputPort));
                            audioNode.Data.SyncWithDialogue = true; audioNode.ToggleAudioNextPort(true); audioNode.RefreshVisuals();
                        }
                    }
                }

                if ((nodeView.Data.NodeType == ENodeType.Dialogue || nodeView.Data.NodeType == ENodeType.Event) && !string.IsNullOrEmpty(nodeView.Data.AnimSyncNodeID))
                {
                    if (nodeDict.TryGetValue(nodeView.Data.AnimSyncNodeID, out var animNode))
                    {
                        if (nodeView.AnimSyncPort != null && animNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(nodeView.AnimSyncPort, animNode.InputPort));
                            animNode.Data.SyncWithDialogue = true; animNode.ToggleAnimNextPort(true); animNode.RefreshVisuals();
                        }
                    }
                }

                if (nodeView.Data.NodeType == ENodeType.Branch || nodeView.Data.NodeType == ENodeType.Condition || nodeView.Data.NodeType == ENodeType.Random)
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