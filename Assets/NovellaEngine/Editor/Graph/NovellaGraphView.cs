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
        [SerializeReference]
        public List<NovellaNodeBase> Nodes = new List<NovellaNodeBase>();
        [SerializeReference]
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
            string scenePortName = ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.");

            if (port.portName == audioPortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Audio / BGM Node", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
            }
            else if (port.portName == animPortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Animation / Tween", "Анимация / Эффект")), false, () => _graphView.CreateNode(localPos, ENodeType.Animation, port));
            }
            else if (port.portName == scenePortName)
            {
                menu.AddItem(new GUIContent(ToolLang.Get("Scene Settings", "Настройки Сцены")), false, () => _graphView.CreateNode(localPos, ENodeType.SceneSettings, port));
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

                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Scene Settings (Bg & Chars)", "Настройки Сцены (Фон/Актеры)")), false, () => _graphView.CreateNode(localPos, ENodeType.SceneSettings, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Audio / BGM", "Аудио / Музыка")), false, () => _graphView.CreateNode(localPos, ENodeType.Audio, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Animation / Tween", "Анимация / Эффект")), false, () => _graphView.CreateNode(localPos, ENodeType.Animation, port));
                menu.AddItem(new GUIContent(cineCat + ToolLang.Get("Wait (Delay)", "Ожидание (Пауза)")), false, () => _graphView.CreateNode(localPos, ENodeType.Wait, port));

                menu.AddItem(new GUIContent(sysCat + ToolLang.Get("Event Broadcast", "Вызов События")), false, () => _graphView.CreateNode(localPos, ENodeType.EventBroadcast, port));

                if (!(port.node is NovellaStartNodeView))
                    menu.AddItem(new GUIContent(sysCat + ToolLang.Get("END Node", "Конец Сцены")), false, () => _graphView.CreateNode(localPos, ENodeType.End, port));

                var dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                    .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0);

                if (dlcTypes.Any())
                {
                    menu.AddSeparator("");
                    foreach (var type in dlcTypes)
                    {
                        var settings = NovellaDLCSettings.GetOrCreateSettings();
                        bool isEnabled = settings.IsDLCEnabled(type.FullName);
                        if (!isEnabled) continue;

                        var attr = (NovellaDLCNodeAttribute)type.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).First();
                        menu.AddItem(new GUIContent("🧩 DLC/ " + attr.MenuName), false, () => _graphView.CreateNode(localPos, ENodeType.CustomDLC, port, type));
                    }
                }
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
        public MiniMap MiniMapInstance { get; private set; }

        public NovellaGraphView(NovellaGraphWindow window, NovellaTree tree)
        {
            Window = window;
            Tree = tree;
            _edgeListener = new NovellaEdgeConnectorListener(this);

            SetupZoom(0.5f, 5.0f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            AddElement(GenerateEntryPointNode());

            MiniMapInstance = new MiniMap { anchored = true };
            MiniMapInstance.style.position = Position.Absolute;
            MiniMapInstance.style.bottom = 20;
            MiniMapInstance.style.right = 20;
            MiniMapInstance.style.width = 250;
            MiniMapInstance.style.height = 150;
            MiniMapInstance.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.9f));
            Add(MiniMapInstance);

            graphViewChanged += OnGraphViewChanged;

            RegisterCallback<AttachToPanelEvent>(e => Undo.undoRedoPerformed += OnUndoRedo);
            RegisterCallback<DetachFromPanelEvent>(e => Undo.undoRedoPerformed -= OnUndoRedo);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);

            serializeGraphElements = OnSerializeGraphElements;
            unserializeAndPaste = OnUnserializeAndPaste;
            canPasteSerializedData = (data) => !string.IsNullOrEmpty(data);
        }
        public void AutoLayout()
        {
            if (StartNodeView == null) return;
            Undo.RegisterCompleteObjectUndo(Tree, "Auto Layout Graph");

            var nodeDict = nodes.ToList().OfType<NovellaNodeView>().ToDictionary(n => n.Data.NodeID);
            var visited = new HashSet<string>();
            var depthMap = new Dictionary<string, int>();
            var depthLists = new Dictionary<int, List<NovellaNodeView>>();

            void Traverse(string nodeId, int depth)
            {
                if (string.IsNullOrEmpty(nodeId) || !nodeDict.ContainsKey(nodeId)) return;
                if (visited.Contains(nodeId))
                {
                    if (depth > depthMap[nodeId]) depthMap[nodeId] = depth;
                    return;
                }

                visited.Add(nodeId);
                depthMap[nodeId] = depth;

                var nodeView = nodeDict[nodeId];
                var ports = nodeView.outputContainer.Query<Port>().ToList();
                foreach (var port in ports)
                {
                    if (port.connected)
                    {
                        foreach (var edge in port.connections)
                        {
                            if (edge.input.node is NovellaNodeView targetNode) Traverse(targetNode.Data.NodeID, depth + 1);
                        }
                    }
                }
            }

            var startPort = StartNodeView.outputContainer.Q<Port>();
            if (startPort != null && startPort.connected)
            {
                foreach (var edge in startPort.connections)
                {
                    if (edge.input.node is NovellaNodeView targetNode) Traverse(targetNode.Data.NodeID, 1);
                }
            }

            foreach (var nv in nodeDict.Values)
            {
                if (!visited.Contains(nv.Data.NodeID)) Traverse(nv.Data.NodeID, 0);
            }

            foreach (var kvp in depthMap)
            {
                if (!depthLists.ContainsKey(kvp.Value)) depthLists[kvp.Value] = new List<NovellaNodeView>();
                depthLists[kvp.Value].Add(nodeDict[kvp.Key]);
            }

            float xOffset = 270f;
            float baseNodeHeight = 180f;
            float yGap = 20f;
            float yOffset = baseNodeHeight + yGap;

            Vector2 startPos = StartNodeView.GetPosition().position;

            foreach (var kvp in depthLists)
            {
                int currentDepth = kvp.Key;
                var columnNodes = kvp.Value;

                columnNodes = columnNodes.OrderBy(n => n.GetPosition().y).ToList();

                float totalHeight = (columnNodes.Count - 1) * yOffset;
                float startY = startPos.y - (totalHeight / 2f);

                float staggerOffset = (currentDepth % 2 != 0) ? (yOffset / 2f) : 0f;

                for (int i = 0; i < columnNodes.Count; i++)
                {
                    var nv = columnNodes[i];
                    float posX = startPos.x + (currentDepth * xOffset);
                    float posY = startY + (i * yOffset) + staggerOffset;

                    Vector2 newPos = new Vector2(posX, posY);
                    nv.SetPosition(new Rect(newPos, nv.GetPosition().size));
                    nv.Data.GraphPosition = newPos;
                }
            }

            SyncGraphToData();
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.objectReferences.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (DragAndDrop.objectReferences.Length == 0) return;

            Vector2 localPos = contentViewContainer.WorldToLocal(evt.mousePosition);
            bool handled = false;

            var characters = DragAndDrop.objectReferences.OfType<NovellaCharacter>().ToList();
            if (characters.Count > 0)
            {
                CreateNode(localPos, ENodeType.Dialogue);
                var newNode = nodes.ToList().OfType<NovellaNodeView>().LastOrDefault();
                if (newNode != null && newNode.Data is DialogueNodeData dnd)
                {
                    foreach (var character in characters)
                    {
                        dnd.ActiveCharacters.Add(new CharacterInDialogue { CharacterAsset = character });
                    }
                    newNode.RefreshVisuals();
                }
                localPos += new Vector2(30, 30);
                handled = true;
            }

            var clips = DragAndDrop.objectReferences.OfType<AudioClip>().ToList();
            foreach (var clip in clips)
            {
                CreateNode(localPos, ENodeType.Audio);
                var newNode = nodes.ToList().OfType<NovellaNodeView>().LastOrDefault();
                if (newNode != null && newNode.Data is AudioNodeData and)
                {
                    and.AudioAsset = clip;
                    and.AudioAction = EAudioAction.Play;
                    newNode.RefreshVisuals();
                }
                localPos += new Vector2(30, 30);
                handled = true;
            }

            var sprites = DragAndDrop.objectReferences.OfType<Sprite>().ToList();
            var textures = DragAndDrop.objectReferences.OfType<Texture2D>().ToList();
            foreach (var tex in textures)
            {
                string path = AssetDatabase.GetAssetPath(tex);
                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s != null && !sprites.Contains(s)) sprites.Add(s);
            }

            foreach (var sprite in sprites)
            {
                CreateNode(localPos, ENodeType.SceneSettings);
                var newNode = nodes.ToList().OfType<NovellaNodeView>().LastOrDefault();
                if (newNode != null && newNode.Data is SceneSettingsNodeData snd)
                {
                    snd.BgSprite = sprite;
                    newNode.RefreshVisuals();
                }
                localPos += new Vector2(30, 30);
                handled = true;
            }

            if (handled)
            {
                DragAndDrop.AcceptDrag();
                Window.MarkUnsaved();
                evt.StopPropagation();
            }
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
                if (node == null) continue;
                string oldId = node.NodeID;
                string newId = node.NodeType.ToString() + "_" + System.Guid.NewGuid().ToString().Substring(0, 5);
                node.NodeID = newId; idMap[oldId] = newId;
                node.GraphPosition += new Vector2(50, 50);

                if (node is BranchNodeData bnd)
                    foreach (var choice in bnd.Choices) choice.PortID = "Choice_" + System.Guid.NewGuid().ToString().Substring(0, 5);
                else if (node is ConditionNodeData cnd)
                    foreach (var choice in cnd.Choices) choice.PortID = "Choice_" + System.Guid.NewGuid().ToString().Substring(0, 5);
                else if (node is RandomNodeData rnd)
                    foreach (var choice in rnd.Choices) choice.PortID = "Choice_" + System.Guid.NewGuid().ToString().Substring(0, 5);
            }

            foreach (var node in clipboard.Nodes)
            {
                if (node == null) continue;
                if (node is DialogueNodeData dialData)
                {
                    if (idMap.ContainsKey(dialData.NextNodeID)) dialData.NextNodeID = idMap[dialData.NextNodeID];
                    else dialData.NextNodeID = "";

                    if (dialData.AudioSyncNodeID != null && idMap.ContainsKey(dialData.AudioSyncNodeID)) dialData.AudioSyncNodeID = idMap[dialData.AudioSyncNodeID];
                    else dialData.AudioSyncNodeID = "";

                    if (dialData.AnimSyncNodeID != null && idMap.ContainsKey(dialData.AnimSyncNodeID)) dialData.AnimSyncNodeID = idMap[dialData.AnimSyncNodeID];
                    else dialData.AnimSyncNodeID = "";

                    if (dialData.SceneSyncNodeID != null && idMap.ContainsKey(dialData.SceneSyncNodeID)) dialData.SceneSyncNodeID = idMap[dialData.SceneSyncNodeID];
                    else dialData.SceneSyncNodeID = "";
                }
                else if (node is BranchNodeData bnd)
                {
                    foreach (var choice in bnd.Choices) { if (idMap.ContainsKey(choice.NextNodeID)) choice.NextNodeID = idMap[choice.NextNodeID]; else choice.NextNodeID = ""; }
                }
                else if (node is ConditionNodeData cnd)
                {
                    foreach (var choice in cnd.Choices) { if (idMap.ContainsKey(choice.NextNodeID)) choice.NextNodeID = idMap[choice.NextNodeID]; else choice.NextNodeID = ""; }
                }
                else if (node is RandomNodeData rnd)
                {
                    foreach (var choice in rnd.Choices) { if (idMap.ContainsKey(choice.NextNodeID)) choice.NextNodeID = idMap[choice.NextNodeID]; else choice.NextNodeID = ""; }
                }
                else
                {
                    var nextNodeField = node.GetType().GetField("NextNodeID");
                    if (nextNodeField != null && nextNodeField.FieldType == typeof(string))
                    {
                        string currentNextId = (string)nextNodeField.GetValue(node);
                        if (!string.IsNullOrEmpty(currentNextId) && idMap.ContainsKey(currentNextId))
                        {
                            nextNodeField.SetValue(node, idMap[currentNextId]);
                        }
                        else
                        {
                            nextNodeField.SetValue(node, "");
                        }
                    }
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
                                    if (outNode.Data is DialogueNodeData odnd) odnd.AudioSyncNodeID = "";
                                    if (inNode.Data is AudioNodeData iand) iand.SyncWithDialogue = false;
                                    inNode.ToggleAudioNextPort(false);
                                    inNode.RefreshVisuals();
                                    EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                                }
                                else if (outNode.AnimSyncPort != null && edge.output == outNode.AnimSyncPort)
                                {
                                    if (outNode.Data is DialogueNodeData odnd) odnd.AnimSyncNodeID = "";
                                    if (inNode.Data is AnimationNodeData iand) iand.SyncWithDialogue = false;
                                    inNode.ToggleAnimNextPort(false);
                                    inNode.RefreshVisuals();
                                    EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                                }
                                else if (outNode.Data is DialogueNodeData dnd3 && outNode.outputContainer.IndexOf(edge.output) == 2)
                                {
                                    dnd3.SceneSyncNodeID = "";
                                    if (inNode.Data is SceneSettingsNodeData isnd) isnd.SyncWithDialogue = false;
                                    inNode.ToggleSceneNextPort(false);
                                    inNode.RefreshVisuals();
                                    EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                                }
                                else
                                {
                                    if (outNode.Data is BranchNodeData bnd)
                                    {
                                        int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                        if (portIdx >= 0 && portIdx < bnd.Choices.Count) bnd.Choices[portIdx].NextNodeID = "";
                                    }
                                    else if (outNode.Data is ConditionNodeData cnd)
                                    {
                                        int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                        if (portIdx >= 0 && portIdx < cnd.Choices.Count) cnd.Choices[portIdx].NextNodeID = "";
                                    }
                                    else if (outNode.Data is RandomNodeData rnd)
                                    {
                                        int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                        if (portIdx >= 0 && portIdx < rnd.Choices.Count) rnd.Choices[portIdx].NextNodeID = "";
                                    }
                                    else if (outNode.Data.NodeType == ENodeType.CustomDLC)
                                    {
                                        var outFields = DLCCache.GetOutputFields(outNode.Data.GetType());
                                        if (outFields.Count > 0)
                                        {
                                            var targetField = outFields.FirstOrDefault(f => ((NovellaDLCOutputAttribute)f.GetCustomAttributes(typeof(NovellaDLCOutputAttribute), false).First()).PortName == edge.output.portName) ?? outFields.First();
                                            targetField.SetValue(outNode.Data, "");
                                        }
                                        else
                                        {
                                            var nextNodeField = outNode.Data.GetType().GetField("NextNodeID");
                                            if (nextNodeField != null && nextNodeField.FieldType == typeof(string)) nextNodeField.SetValue(outNode.Data, "");
                                        }
                                    }
                                    else
                                    {
                                        var nextNodeField = outNode.Data.GetType().GetField("NextNodeID");
                                        if (nextNodeField != null && nextNodeField.FieldType == typeof(string))
                                        {
                                            nextNodeField.SetValue(outNode.Data, "");
                                        }
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
                            string sceneSyncName = ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.");

                            if (outNode.AudioSyncPort != null && edge.output == outNode.AudioSyncPort)
                            {
                                if (outNode.Data is DialogueNodeData odnd) odnd.AudioSyncNodeID = inNode.Data.NodeID;
                                if (inNode.Data is AudioNodeData iand) iand.SyncWithDialogue = true;
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
                                if (outNode.Data is DialogueNodeData odnd) odnd.AnimSyncNodeID = inNode.Data.NodeID;
                                if (inNode.Data is AnimationNodeData iand) iand.SyncWithDialogue = true;
                                inNode.ToggleAnimNextPort(true);

                                if (inNode.OutputPort != null && inNode.OutputPort.connected)
                                {
                                    var connections = inNode.OutputPort.connections.ToList();
                                    EditorApplication.delayCall += () => { DeleteElements(connections); };
                                }
                                EditorApplication.delayCall += () => { if (inNode != null) inNode.RefreshVisuals(); };
                                EditorApplication.delayCall += () => { if (Window != null) Window.Repaint(); };
                            }
                            else if (edge.output.portName == sceneSyncName)
                            {
                                if (outNode.Data is DialogueNodeData odnd) odnd.SceneSyncNodeID = inNode.Data.NodeID;
                                if (inNode.Data is SceneSettingsNodeData isnd) isnd.SyncWithDialogue = true;
                                inNode.ToggleSceneNextPort(true);

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
                                if (outNode.Data is BranchNodeData bnd)
                                {
                                    int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                    if (portIdx >= 0 && portIdx < bnd.Choices.Count) bnd.Choices[portIdx].NextNodeID = inNode.Data.NodeID;
                                }
                                else if (outNode.Data is ConditionNodeData cnd)
                                {
                                    int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                    if (portIdx >= 0 && portIdx < cnd.Choices.Count) cnd.Choices[portIdx].NextNodeID = inNode.Data.NodeID;
                                }
                                else if (outNode.Data is RandomNodeData rnd)
                                {
                                    int portIdx = outNode.outputContainer.IndexOf(edge.output);
                                    if (portIdx >= 0 && portIdx < rnd.Choices.Count) rnd.Choices[portIdx].NextNodeID = inNode.Data.NodeID;
                                }
                                else if (outNode.Data.NodeType == ENodeType.CustomDLC)
                                {
                                    var outFields = DLCCache.GetOutputFields(outNode.Data.GetType());
                                    if (outFields.Count > 0)
                                    {
                                        var targetField = outFields.FirstOrDefault(f => ((NovellaDLCOutputAttribute)f.GetCustomAttributes(typeof(NovellaDLCOutputAttribute), false).First()).PortName == edge.output.portName) ?? outFields.First();
                                        targetField.SetValue(outNode.Data, inNode.Data.NodeID);
                                    }
                                    else
                                    {
                                        var nextNodeField = outNode.Data.GetType().GetField("NextNodeID");
                                        if (nextNodeField != null && nextNodeField.FieldType == typeof(string)) nextNodeField.SetValue(outNode.Data, inNode.Data.NodeID);
                                    }
                                }
                                else
                                {
                                    var nextNodeField = outNode.Data.GetType().GetField("NextNodeID");
                                    if (nextNodeField != null && nextNodeField.FieldType == typeof(string))
                                    {
                                        nextNodeField.SetValue(outNode.Data, inNode.Data.NodeID);
                                    }
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

            evt.menu.AppendAction(cineCat + ToolLang.Get("Scene Settings (Bg & Chars)", "Настройки Сцены (Фон-Актеры)"), (a) => CreateNode(pos, ENodeType.SceneSettings));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Audio / BGM", "Аудио / Музыка"), (a) => CreateNode(pos, ENodeType.Audio));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Animation / Tween", "Анимация / Эффект"), (a) => CreateNode(pos, ENodeType.Animation));
            evt.menu.AppendAction(cineCat + ToolLang.Get("Wait (Delay)", "Ожидание (Пауза)"), (a) => CreateNode(pos, ENodeType.Wait));

            evt.menu.AppendAction(sysCat + ToolLang.Get("Event Broadcast", "Вызов События"), (a) => CreateNode(pos, ENodeType.EventBroadcast));
            evt.menu.AppendAction(sysCat + ToolLang.Get("Save Game (Auto-Save)", "Сохранить Игру (Автосейв)"), (a) => CreateNode(pos, ENodeType.Save));

            if (!selection.OfType<NovellaStartNodeView>().Any())
                evt.menu.AppendAction(sysCat + ToolLang.Get("END Node", "Конец Сцены"), (a) => CreateNode(pos, ENodeType.End));

            var dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0);

            if (dlcTypes.Any())
            {
                evt.menu.AppendSeparator("");
                foreach (var type in dlcTypes)
                {
                    var settings = NovellaDLCSettings.GetOrCreateSettings();
                    bool isEnabled = settings.IsDLCEnabled(type.FullName);

                    if (!isEnabled) continue;

                    var attr = (NovellaDLCNodeAttribute)type.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).First();
                    evt.menu.AppendAction("🧩 DLC/ " + attr.MenuName, (a) => CreateDLCNode(pos, type, attr));
                }
            }

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

        public void CreateNode(Vector2 position, ENodeType type, Port autoConnectPort = null, System.Type dlcType = null)
        {
            if (Tree == null || Tree.Nodes == null) return;

            Undo.RegisterCompleteObjectUndo(Tree, "Create Node");
            int count = Tree.Nodes.Count(n => n != null && n.NodeType == type) + 1;

            NovellaNodeBase nodeData = null;

            switch (type)
            {
                case ENodeType.Dialogue:
                case ENodeType.Event: nodeData = new DialogueNodeData(); break;
                case ENodeType.Branch: nodeData = new BranchNodeData(); break;
                case ENodeType.Condition: nodeData = new ConditionNodeData(); break;
                case ENodeType.Random: nodeData = new RandomNodeData(); break;
                case ENodeType.Variable: nodeData = new VariableNodeData(); break;
                case ENodeType.Audio: nodeData = new AudioNodeData(); break;
                case ENodeType.Wait: nodeData = new WaitNodeData(); break;
                case ENodeType.SceneSettings: nodeData = new SceneSettingsNodeData(); break;
                case ENodeType.Animation: nodeData = new AnimationNodeData(); break;
                case ENodeType.EventBroadcast: nodeData = new EventBroadcastNodeData(); break;
                case ENodeType.Save: nodeData = new SaveNodeData(); break;
                case ENodeType.Note: nodeData = new NoteNodeData(); break;
                case ENodeType.End: nodeData = new EndNodeData(); break;
                case ENodeType.CustomDLC:
                    if (dlcType != null)
                    {
                        nodeData = (NovellaNodeBase)System.Activator.CreateInstance(dlcType);
                        var attr = DLCCache.GetNodeAttribute(dlcType);
                        if (attr != null) ColorUtility.TryParseHtmlString(attr.HexColor, out nodeData.NodeCustomColor);
                    }
                    break;
                default: nodeData = new DialogueNodeData(); break;
            }

            if (nodeData == null) return;

            nodeData.NodeID = type.ToString() + "_" + System.Guid.NewGuid().ToString().Substring(0, 5);
            nodeData.NodeTitle = type == ENodeType.CustomDLC && dlcType != null ? DLCCache.GetNodeAttribute(dlcType)?.NodeTitle : $"{type} {count}";
            nodeData.GraphPosition = position;

            if (autoConnectPort != null && autoConnectPort.node is NovellaNodeView parentNode)
            {
                if (nodeData is DialogueNodeData dnd && parentNode.Data is DialogueNodeData pDnd)
                {
                    foreach (var charInPrev in pDnd.ActiveCharacters)
                    {
                        dnd.ActiveCharacters.Add(new CharacterInDialogue { CharacterAsset = charInPrev.CharacterAsset, Plane = charInPrev.Plane, Scale = charInPrev.Scale, Emotion = charInPrev.Emotion, PosX = charInPrev.PosX, PosY = charInPrev.PosY });
                    }
                }
            }

            if (nodeData is BranchNodeData bnd) { bnd.Choices.Add(new NovellaChoice()); bnd.Choices.Add(new NovellaChoice()); }
            else if (nodeData is ConditionNodeData cnd)
            {
                var trueChoice = new NovellaChoice(); trueChoice.LocalizedText.SetText("EN", "True"); trueChoice.LocalizedText.SetText("RU", "Истина");
                var falseChoice = new NovellaChoice(); falseChoice.LocalizedText.SetText("EN", "False"); falseChoice.LocalizedText.SetText("RU", "Ложь");
                cnd.Choices.Add(trueChoice); cnd.Choices.Add(falseChoice);
            }
            else if (nodeData is RandomNodeData rnd)
            {
                rnd.Choices.Add(new NovellaChoice() { ChanceWeight = 50 }); rnd.Choices.Add(new NovellaChoice() { ChanceWeight = 50 });
            }

            Tree.Nodes.Add(nodeData);
            var view = new NovellaNodeView(nodeData, this);
            view.SetPosition(new Rect(position, new Vector2(200, 150))); AddElement(view);

            HandleAutoConnect(view, autoConnectPort, type);
            Window.MarkUnsaved();
        }

        public void CreateDLCNode(Vector2 position, System.Type dlcType, NovellaDLCNodeAttribute attr)
        {
            Undo.RegisterCompleteObjectUndo(Tree, "Create DLC Node");

            NovellaNodeBase nodeData = (NovellaNodeBase)System.Activator.CreateInstance(dlcType);
            nodeData.NodeID = "DLC_" + System.Guid.NewGuid().ToString().Substring(0, 5);
            nodeData.NodeTitle = attr.NodeTitle;
            nodeData.GraphPosition = position;

            ColorUtility.TryParseHtmlString(attr.HexColor, out Color customCol);
            nodeData.NodeCustomColor = customCol;

            Tree.Nodes.Add(nodeData);
            var view = new NovellaNodeView(nodeData, this);
            view.SetPosition(new Rect(position, new Vector2(200, 150)));
            AddElement(view);

            Window.MarkUnsaved();
        }

        private void HandleAutoConnect(NovellaNodeView view, Port autoConnectPort, ENodeType type)
        {
            if (autoConnectPort == null || type == ENodeType.Character || type == ENodeType.Note) return;

            string audioSyncName = ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр.");
            string animSyncName = ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр.");
            string sceneSyncName = ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.");

            if (autoConnectPort.direction == Direction.Output)
            {
                if (autoConnectPort.portName == audioSyncName && type == ENodeType.Audio)
                {
                    if (autoConnectPort.connected)
                    {
                        var oldEdges = autoConnectPort.connections.ToList();
                        foreach (var e in oldEdges) { if (e.input.node is NovellaNodeView oldIn && oldIn.Data is AudioNodeData oauD) { oauD.SyncWithDialogue = false; oldIn.ToggleAudioNextPort(false); oldIn.RefreshVisuals(); } }
                        DeleteElements(oldEdges);
                    }
                    AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                    if (autoConnectPort.node is NovellaNodeView sourceNode && sourceNode.Data is DialogueNodeData sDnd) sDnd.AudioSyncNodeID = view.Data.NodeID;
                    if (view.Data is AudioNodeData vAuD) vAuD.SyncWithDialogue = true;
                    view.ToggleAudioNextPort(true); view.RefreshVisuals();
                }
                else if (autoConnectPort.portName == animSyncName && type == ENodeType.Animation)
                {
                    if (autoConnectPort.connected)
                    {
                        var oldEdges = autoConnectPort.connections.ToList();
                        foreach (var e in oldEdges) { if (e.input.node is NovellaNodeView oldIn && oldIn.Data is AnimationNodeData oAnD) { oAnD.SyncWithDialogue = false; oldIn.ToggleAnimNextPort(false); oldIn.RefreshVisuals(); } }
                        DeleteElements(oldEdges);
                    }
                    AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                    if (autoConnectPort.node is NovellaNodeView sourceNode && sourceNode.Data is DialogueNodeData sDnd) sDnd.AnimSyncNodeID = view.Data.NodeID;
                    if (view.Data is AnimationNodeData vAnD) vAnD.SyncWithDialogue = true;
                    view.ToggleAnimNextPort(true); view.RefreshVisuals();
                }
                else if (autoConnectPort.portName == sceneSyncName && type == ENodeType.SceneSettings)
                {
                    if (autoConnectPort.connected)
                    {
                        var oldEdges = autoConnectPort.connections.ToList();
                        foreach (var e in oldEdges) { if (e.input.node is NovellaNodeView oldIn && oldIn.Data is SceneSettingsNodeData oScD) { oScD.SyncWithDialogue = false; oldIn.RefreshVisuals(); } }
                        DeleteElements(oldEdges);
                    }
                    AddElement(ConnectPorts(autoConnectPort, view.InputPort));
                    if (autoConnectPort.node is NovellaNodeView sourceNode && sourceNode.Data is DialogueNodeData sDnd) sDnd.SceneSyncNodeID = view.Data.NodeID;
                    if (view.Data is SceneSettingsNodeData vScD) vScD.SyncWithDialogue = true;

                    if (view.OutputPort != null) view.OutputPort.style.display = DisplayStyle.None;
                    view.RefreshVisuals();
                }
                else if (view.InputPort != null && autoConnectPort.portName != audioSyncName && autoConnectPort.portName != animSyncName && autoConnectPort.portName != sceneSyncName)
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
            string sceneSyncName = ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр.");

            ports.ForEach(port => {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    bool isStartAudioOut = (startPort.portName == audioSyncName && startPort.direction == Direction.Output);
                    bool isPortAudioOut = (port.portName == audioSyncName && port.direction == Direction.Output);

                    bool isStartAnimOut = (startPort.portName == animSyncName && startPort.direction == Direction.Output);
                    bool isPortAnimOut = (port.portName == animSyncName && port.direction == Direction.Output);

                    bool isStartSceneOut = (startPort.portName == sceneSyncName && startPort.direction == Direction.Output);
                    bool isPortSceneOut = (port.portName == sceneSyncName && port.direction == Direction.Output);

                    if (isStartAudioOut)
                    {
                        if (port.node is NovellaNodeView targetNode && targetNode.Data is AudioNodeData)
                            if (!port.connections.Any(e => e.output.portName == audioSyncName)) compPorts.Add(port);
                    }
                    else if (isPortAudioOut)
                    {
                        if (startPort.node is NovellaNodeView targetNode && targetNode.Data is AudioNodeData)
                            if (!startPort.connections.Any(e => e.output.portName == audioSyncName)) compPorts.Add(port);
                    }
                    else if (isStartAnimOut)
                    {
                        if (port.node is NovellaNodeView targetNode && targetNode.Data is AnimationNodeData)
                            if (!port.connections.Any(e => e.output.portName == animSyncName)) compPorts.Add(port);
                    }
                    else if (isPortAnimOut)
                    {
                        if (startPort.node is NovellaNodeView targetNode && targetNode.Data is AnimationNodeData)
                            if (!startPort.connections.Any(e => e.output.portName == animSyncName)) compPorts.Add(port);
                    }
                    else if (isStartSceneOut)
                    {
                        if (port.node is NovellaNodeView targetNode && targetNode.Data is SceneSettingsNodeData)
                            if (!port.connections.Any(e => e.output.portName == sceneSyncName)) compPorts.Add(port);
                    }
                    else if (isPortSceneOut)
                    {
                        if (startPort.node is NovellaNodeView targetNode && targetNode.Data is SceneSettingsNodeData)
                            if (!startPort.connections.Any(e => e.output.portName == sceneSyncName)) compPorts.Add(port);
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

            var updatedNodes = new List<NovellaNodeBase>();
            foreach (var nodeView in nodes.OfType<NovellaNodeView>()) { nodeView.SaveNodeData(); updatedNodes.Add(nodeView.Data); }
            Tree.Nodes = updatedNodes;

            var updatedGroups = new List<NovellaGroupData>();
            foreach (var groupView in graphElements.ToList().OfType<NovellaGroupView>()) { groupView.SaveGroupData(); updatedGroups.Add(groupView.Data); }
            Tree.Groups = updatedGroups;
        }

        public void LoadGraph()
        {
            if (Tree == null || Tree.Nodes == null) return;
            var existingNodes = nodes.ToList().OfType<NovellaNodeView>().ToList();
            foreach (var n in existingNodes) RemoveElement(n);
            foreach (var e in edges.ToList()) RemoveElement(e);
            foreach (var g in graphElements.ToList().OfType<NovellaGroupView>()) RemoveElement(g);

            var nodeDict = new Dictionary<string, NovellaNodeView>();
            foreach (var data in Tree.Nodes)
            {
                if (data == null) continue;
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

                if (nodeView.Data is DialogueNodeData dnd)
                {
                    if (!string.IsNullOrEmpty(dnd.AudioSyncNodeID) && nodeDict.TryGetValue(dnd.AudioSyncNodeID, out var audioNode))
                    {
                        if (nodeView.AudioSyncPort != null && audioNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(nodeView.AudioSyncPort, audioNode.InputPort));
                            if (audioNode.Data is AudioNodeData audD) audD.SyncWithDialogue = true;
                            audioNode.ToggleAudioNextPort(true); audioNode.RefreshVisuals();
                        }
                    }
                    if (!string.IsNullOrEmpty(dnd.AnimSyncNodeID) && nodeDict.TryGetValue(dnd.AnimSyncNodeID, out var animNode))
                    {
                        if (nodeView.AnimSyncPort != null && animNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(nodeView.AnimSyncPort, animNode.InputPort));
                            if (animNode.Data is AnimationNodeData anD) anD.SyncWithDialogue = true;
                            animNode.ToggleAnimNextPort(true); animNode.RefreshVisuals();
                        }
                    }
                    if (!string.IsNullOrEmpty(dnd.SceneSyncNodeID) && nodeDict.TryGetValue(dnd.SceneSyncNodeID, out var sceneNode))
                    {
                        var scenePort = nodeView.outputContainer.Query<Port>().ToList().FirstOrDefault(p => p.portName == ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр."));
                        if (scenePort != null && sceneNode.InputPort != null)
                        {
                            AddElement(ConnectPorts(scenePort, sceneNode.InputPort));
                            if (sceneNode.Data is SceneSettingsNodeData scD) scD.SyncWithDialogue = true;
                            if (sceneNode.OutputPort != null) sceneNode.OutputPort.style.display = DisplayStyle.None;
                            sceneNode.RefreshVisuals();
                        }
                    }

                }

                if (nodeView.Data is BranchNodeData bnd)
                {
                    var ports = nodeView.outputContainer.Query<Port>().ToList();
                    for (int i = 0; i < bnd.Choices.Count; i++) { if (i < ports.Count && !string.IsNullOrEmpty(bnd.Choices[i].NextNodeID) && nodeDict.TryGetValue(bnd.Choices[i].NextNodeID, out var targetNode)) { if (targetNode.InputPort != null) AddElement(ConnectPorts(ports[i], targetNode.InputPort)); } }
                }
                else if (nodeView.Data is ConditionNodeData cnd)
                {
                    var ports = nodeView.outputContainer.Query<Port>().ToList();
                    for (int i = 0; i < cnd.Choices.Count; i++) { if (i < ports.Count && !string.IsNullOrEmpty(cnd.Choices[i].NextNodeID) && nodeDict.TryGetValue(cnd.Choices[i].NextNodeID, out var targetNode)) { if (targetNode.InputPort != null) AddElement(ConnectPorts(ports[i], targetNode.InputPort)); } }
                }
                else if (nodeView.Data is RandomNodeData rnd)
                {
                    var ports = nodeView.outputContainer.Query<Port>().ToList();
                    for (int i = 0; i < rnd.Choices.Count; i++) { if (i < ports.Count && !string.IsNullOrEmpty(rnd.Choices[i].NextNodeID) && nodeDict.TryGetValue(rnd.Choices[i].NextNodeID, out var targetNode)) { if (targetNode.InputPort != null) AddElement(ConnectPorts(ports[i], targetNode.InputPort)); } }
                }
                else if (nodeView.Data.NodeType == ENodeType.CustomDLC)
                {
                    var outFields = DLCCache.GetOutputFields(nodeView.Data.GetType());
                    if (outFields.Count > 0)
                    {
                        foreach (var field in outFields)
                        {
                            string targetId = (string)field.GetValue(nodeView.Data);
                            if (!string.IsNullOrEmpty(targetId) && nodeDict.TryGetValue(targetId, out var tNode))
                            {
                                var attr = (NovellaDLCOutputAttribute)field.GetCustomAttributes(typeof(NovellaDLCOutputAttribute), false).First();
                                var outPort = nodeView.outputContainer.Query<Port>().ToList().FirstOrDefault(p => p.portName == attr.PortName);

                                outPort ??= nodeView.OutputPort;

                                if (outPort != null && tNode.InputPort != null) AddElement(ConnectPorts(outPort, tNode.InputPort));
                            }
                        }
                    }
                    else
                    {
                        var nextNodeField = nodeView.Data.GetType().GetField("NextNodeID");
                        if (nextNodeField != null && nextNodeField.FieldType == typeof(string))
                        {
                            string currentNextId = (string)nextNodeField.GetValue(nodeView.Data);
                            if (!string.IsNullOrEmpty(currentNextId) && nodeDict.TryGetValue(currentNextId, out var tNode))
                            {
                                if (nodeView.OutputPort != null && tNode.InputPort != null) AddElement(ConnectPorts(nodeView.OutputPort, tNode.InputPort));
                            }
                        }
                    }
                }
                else
                {
                    var nextNodeField = nodeView.Data.GetType().GetField("NextNodeID");
                    if (nextNodeField != null && nextNodeField.FieldType == typeof(string))
                    {
                        string currentNextId = (string)nextNodeField.GetValue(nodeView.Data);
                        if (!string.IsNullOrEmpty(currentNextId) && nodeDict.TryGetValue(currentNextId, out var tNode))
                        {
                            if (nodeView.OutputPort != null && tNode.InputPort != null)
                            {
                                AddElement(ConnectPorts(nodeView.OutputPort, tNode.InputPort));
                            }
                        }
                    }
                }
            }
        }
    }
}