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

        // ─── Block 2A: визуальные элементы общего design-language ───
        // Левая 3px полоса акцентного цвета (тип ноды), вешается на node-border.
        private VisualElement _accentStrip;
        // Selection-overlay — 2px цианиевый outline когда нода выделена.
        private VisualElement _selectionOutline;

        // Кэш рефлексии для устранения лагов DLC нод при перетаскивании
        private static Dictionary<System.Type, Dictionary<string, System.Reflection.FieldInfo>> _fieldCache = new Dictionary<System.Type, Dictionary<string, System.Reflection.FieldInfo>>();

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

                if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event || Data.NodeType == ENodeType.Audio || Data.NodeType == ENodeType.Variable || Data.NodeType == ENodeType.Wait || Data.NodeType == ENodeType.SceneSettings || Data.NodeType == ENodeType.Animation || Data.NodeType == ENodeType.EventBroadcast || Data.NodeType == ENodeType.CustomDLC || Data.NodeType == ENodeType.Save)
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

            // ─── Block 2A: 3px полоса акцентного цвета слева ───
            // Узкая вертикальная плашка ВНУТРИ node-border, ПОВЕРХ всех
            // остальных детей (title/inputContainer/extensionContainer).
            // Иначе title-bar с фоном BgRaised её перекрывает (стрип
            // оказывается под ним) — что и было видно в графе раньше.
            _accentStrip = new VisualElement { name = "ns-accent-strip" };
            _accentStrip.pickingMode = PickingMode.Ignore;
            _accentStrip.style.position = Position.Absolute;
            _accentStrip.style.left = 0;
            _accentStrip.style.top = 0;
            _accentStrip.style.bottom = 0;
            _accentStrip.style.width = 3;
            _accentStrip.style.borderTopLeftRadius = 6;
            _accentStrip.style.borderBottomLeftRadius = 6;
            var nodeBorderForStrip = this.Q("node-border");
            if (nodeBorderForStrip != null) nodeBorderForStrip.Add(_accentStrip); // Add → на верх z-order
            else this.Add(_accentStrip);

            // ─── Block 2A: 2px cyan outline на selection ───
            // Стоковый GraphView подсвечивает выделенную ноду белой 1px
            // линией (через USS .node:checked). Заменяем на акцент-cyan 2px,
            // включаемый в OnSelected/OnUnselected.
            _selectionOutline = new VisualElement { name = "ns-selection-outline" };
            _selectionOutline.pickingMode = PickingMode.Ignore;
            _selectionOutline.style.position = Position.Absolute;
            _selectionOutline.style.left = -2;
            _selectionOutline.style.right = -2;
            _selectionOutline.style.top = -2;
            _selectionOutline.style.bottom = -2;
            _selectionOutline.style.borderTopLeftRadius = 8;
            _selectionOutline.style.borderTopRightRadius = 8;
            _selectionOutline.style.borderBottomLeftRadius = 8;
            _selectionOutline.style.borderBottomRightRadius = 8;
            _selectionOutline.style.borderTopWidth = 2;
            _selectionOutline.style.borderBottomWidth = 2;
            _selectionOutline.style.borderLeftWidth = 2;
            _selectionOutline.style.borderRightWidth = 2;
            _selectionOutline.style.borderTopColor = NovellaGraphTheme.Accent;
            _selectionOutline.style.borderBottomColor = NovellaGraphTheme.Accent;
            _selectionOutline.style.borderLeftColor = NovellaGraphTheme.Accent;
            _selectionOutline.style.borderRightColor = NovellaGraphTheme.Accent;
            _selectionOutline.style.display = DisplayStyle.None;
            this.Insert(0, _selectionOutline);

            var titleLabel = titleContainer.Q<Label>();
            if (titleLabel != null) {
                titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                titleLabel.style.flexGrow = 1;
                // Сдвиг от accent-strip + emoji bullet → отступ 12 слева.
                titleLabel.style.marginLeft = 12;
                titleLabel.style.marginRight = 8;
                titleLabel.style.fontSize = 12;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.color = NovellaGraphTheme.Text1;
            }
            titleContainer.style.justifyContent = Justify.FlexStart;
            // Title-bar теперь приглушённый (BgRaised), с тонкой нижней
            // обводкой. Per-type цвет ушёл в accent-strip слева.
            titleContainer.style.backgroundColor = NovellaGraphTheme.BgRaised;
            titleContainer.style.borderBottomWidth = 1;
            titleContainer.style.borderBottomColor = NovellaGraphTheme.Border;
            titleContainer.style.paddingTop = 6;
            titleContainer.style.paddingBottom = 6;

            this.RegisterCallback<GeometryChangedEvent>(evt => { var collapseBtn = this.titleButtonContainer.Q("collapse-button"); if (collapseBtn != null) collapseBtn.style.display = DisplayStyle.None; });

            RefreshVisuals(); RefreshExpandedState(); RefreshPorts();
        }

        private System.Reflection.FieldInfo GetCachedField(System.Type type, string fieldName)
        {
            if (!_fieldCache.ContainsKey(type))
                _fieldCache[type] = new Dictionary<string, System.Reflection.FieldInfo>();

            if (!_fieldCache[type].ContainsKey(fieldName))
                _fieldCache[type][fieldName] = type.GetField(fieldName);

            return _fieldCache[type][fieldName];
        }

        public void FormatLongLabel(Label label)
        {
            if (label == null) return;
            label.style.maxWidth = 140;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
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
            else if (Data.NodeType == ENodeType.Save) titlePrefix = "💾 ";
            else if (Data.NodeType == ENodeType.CustomDLC) titlePrefix = "🧩 ";

            title = titlePrefix + (string.IsNullOrEmpty(Data.NodeTitle) ? Data.NodeID : Data.NodeTitle);

            _pinLabel.style.display = Data.IsPinned ? DisplayStyle.Flex : DisplayStyle.None;

            // Title-bar в Block 2A — всегда BgRaised (тёмный приглушённый),
            // с тонкой нижней обводкой. Per-type цвет идёт в accent-strip.
            titleContainer.style.backgroundColor = NovellaGraphTheme.BgRaised;
            titleContainer.style.borderBottomWidth = 1;
            titleContainer.style.borderBottomColor = NovellaGraphTheme.Border;

            // ─── Per-type цвет — выбираем источник ───
            Color nodeColor = Color.grey;
            if (Data.NodeType == ENodeType.Dialogue || Data.NodeType == ENodeType.Event ||
                Data.NodeType == ENodeType.Note || Data.NodeType == ENodeType.CustomDLC)
            {
                nodeColor = Data.NodeCustomColor;
            }
            else
            {
                nodeColor = NovellaColorSettingsWindow.GetNodeColor(Data.NodeType);
            }

            // Accent-strip (3px слева) получает per-type цвет на полную яркость —
            // это и есть теперь визуальный «маркер типа». Мини-карта тоже видит
            // фон node-border (15% alpha от того же цвета).
            if (_accentStrip != null)
            {
                _accentStrip.style.backgroundColor = nodeColor;
            }

            // node-border = body ноды. Тонировка 8% (раньше 15%) — почти не
            // мешает читать содержимое, но мини-карта по-прежнему различает
            // типы. Border — тонкий Border-цвет, чтобы edges были чёткими.
            var border = this.Q("node-border");
            if (border != null)
            {
                border.style.backgroundColor = new Color(nodeColor.r, nodeColor.g, nodeColor.b, 0.08f);
                border.style.borderTopColor = NovellaGraphTheme.Border;
                border.style.borderBottomColor = NovellaGraphTheme.Border;
                border.style.borderLeftColor = NovellaGraphTheme.Border;
                border.style.borderRightColor = NovellaGraphTheme.Border;
                border.style.borderTopWidth = 1;
                border.style.borderBottomWidth = 1;
                border.style.borderLeftWidth = 1;
                border.style.borderRightWidth = 1;
                border.style.borderTopLeftRadius = 6;
                border.style.borderTopRightRadius = 6;
                border.style.borderBottomLeftRadius = 6;
                border.style.borderBottomRightRadius = 6;
            }

            // Condition раньше имел оранжевый borderBottom 3px — оставим как
            // отдельный визуальный сигнал «if/else», но теперь как accent-strip
            // переключение цвета на оранжевый, не двойная обводка.
            if (Data.NodeType == ENodeType.Condition && _accentStrip != null)
            {
                _accentStrip.style.backgroundColor = new Color(0.96f, 0.65f, 0.22f); // тёплый янтарный
            }

            if (Data.NodeType == ENodeType.CustomDLC)
            {
                var settings = NovellaDLCSettings.Instance;
                bool isEnabled = settings.IsDLCEnabled(Data.GetType().FullName);

                // Disabled DLC: 45% opacity на всю ноду, accent-strip серый.
                this.style.opacity = isEnabled ? 1f : 0.45f;
                nodeColor = NovellaColorSettingsWindow.GetDLCNodeColor(Data.GetType().FullName);
                if (_accentStrip != null)
                    _accentStrip.style.backgroundColor = isEnabled ? nodeColor : NovellaGraphTheme.Text4;
                if (border != null)
                    border.style.backgroundColor = new Color(nodeColor.r, nodeColor.g, nodeColor.b, isEnabled ? 0.08f : 0.04f);

                // Disabled-плашка: чище, в стиле Hub'овских warning-badge.
                var disabledLabel = this.Q<Label>("dlc-disabled-label");
                if (!isEnabled)
                {
                    if (disabledLabel == null)
                    {
                        disabledLabel = new Label("🔒  DISABLED") { name = "dlc-disabled-label" };
                        disabledLabel.style.position = Position.Absolute;
                        disabledLabel.style.top = -22;
                        disabledLabel.style.left = 0;
                        disabledLabel.style.right = 0;
                        disabledLabel.style.backgroundColor = new Color(NovellaGraphTheme.Danger.r, NovellaGraphTheme.Danger.g, NovellaGraphTheme.Danger.b, 0.18f);
                        disabledLabel.style.color = NovellaGraphTheme.Danger;
                        disabledLabel.style.fontSize = 10;
                        disabledLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        disabledLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                        disabledLabel.style.borderTopWidth = 1;
                        disabledLabel.style.borderBottomWidth = 1;
                        disabledLabel.style.borderLeftWidth = 1;
                        disabledLabel.style.borderRightWidth = 1;
                        disabledLabel.style.borderTopColor = NovellaGraphTheme.Danger;
                        disabledLabel.style.borderBottomColor = NovellaGraphTheme.Danger;
                        disabledLabel.style.borderLeftColor = NovellaGraphTheme.Danger;
                        disabledLabel.style.borderRightColor = NovellaGraphTheme.Danger;
                        disabledLabel.style.borderTopLeftRadius = 6;
                        disabledLabel.style.borderTopRightRadius = 6;
                        disabledLabel.style.borderBottomLeftRadius = 6;
                        disabledLabel.style.borderBottomRightRadius = 6;
                        disabledLabel.style.paddingTop = 3; disabledLabel.style.paddingBottom = 3;
                        disabledLabel.pickingMode = PickingMode.Ignore;

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
                // Block 2C: компактная строка-info вместо центрированного label'а.
                string secStr = ToolLang.Get("sec", "сек");
                string waitText = waitD.WaitMode == EWaitMode.Time
                    ? $"⏳  {waitD.WaitTime} {secStr}"
                    : "🖱  " + ToolLang.Get("On click", "По клику");
                var waitBlock = new Label(waitText);
                waitBlock.pickingMode = PickingMode.Ignore;
                waitBlock.style.fontSize = 11;
                waitBlock.style.color = NovellaGraphTheme.Text2;
                waitBlock.style.paddingLeft = 6;
                waitBlock.style.paddingRight = 6;
                waitBlock.style.paddingTop = 2;
                waitBlock.style.paddingBottom = 2;
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
                    string bgName = bgD.BgSprite != null ? bgD.BgSprite.name : ToolLang.Get("(color only)", "(только цвет)");
                    var bgBlock = new Label("🖼  " + bgName);
                    bgBlock.pickingMode = PickingMode.Ignore;
                    bgBlock.style.fontSize = 11;
                    bgBlock.style.color = NovellaGraphTheme.Text2;
                    bgBlock.style.paddingLeft = 6;
                    bgBlock.style.paddingRight = 6;
                    bgBlock.style.paddingTop = 2;
                    bgBlock.style.paddingBottom = 2;
                    FormatLongLabel(bgBlock);
                    extensionContainer.Add(bgBlock);
                    hasExtensionData = true;
                }
            }
            else if (Data is EventBroadcastNodeData evD)
            {
                // Event-name выделяем bold-цветом акцента — это «триггер».
                var evBlock = new Label("⚡  " + evD.BroadcastEventName);
                evBlock.pickingMode = PickingMode.Ignore;
                evBlock.style.fontSize = 11;
                evBlock.style.unityFontStyleAndWeight = FontStyle.Bold;
                evBlock.style.color = new Color(0.96f, 0.76f, 0.43f); // тёплый янтарный
                evBlock.style.paddingLeft = 6;
                evBlock.style.paddingRight = 6;
                evBlock.style.paddingTop = 2;
                evBlock.style.paddingBottom = 2;
                extensionContainer.Add(evBlock);
                hasExtensionData = true;
            }
            else if (Data is SaveNodeData saveD)
            {
                var saveBlock = new Label("💾  " + ToolLang.Get("Checkpoint", "Чекпоинт"));
                saveBlock.pickingMode = PickingMode.Ignore;
                saveBlock.style.fontSize = 11;
                saveBlock.style.unityFontStyleAndWeight = FontStyle.Bold;
                saveBlock.style.color = new Color(0.30f, 0.85f, 0.45f); // зелёный success
                saveBlock.style.paddingLeft = 6;
                saveBlock.style.paddingRight = 6;
                saveBlock.style.paddingTop = 2;
                saveBlock.style.paddingBottom = 2;
                extensionContainer.Add(saveBlock);
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
                // Раньше: красная плашка-фон (агрессивный визуал). Теперь:
                // только красный заголовок + муторный subtitle с указанием куда.
                string endTitle = "";
                if (endD.EndAction == EEndAction.ReturnToMainMenu) endTitle = ToolLang.Get("To main menu", "В гл. меню");
                else if (endD.EndAction == EEndAction.LoadNextChapter) endTitle = ToolLang.Get("Next chapter", "След. глава");
                else if (endD.EndAction == EEndAction.LoadSpecificScene) endTitle = ToolLang.Get("Load scene", "Загрузить сцену");
                else if (endD.EndAction == EEndAction.QuitGame) endTitle = ToolLang.Get("Quit game", "Выход");

                var endActionLbl = new Label("🛑  " + endTitle);
                endActionLbl.pickingMode = PickingMode.Ignore;
                endActionLbl.style.fontSize = 11;
                endActionLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                endActionLbl.style.color = new Color(0.85f, 0.32f, 0.32f); // danger
                endActionLbl.style.paddingLeft = 6;
                endActionLbl.style.paddingRight = 6;
                endActionLbl.style.paddingTop = 2;
                endActionLbl.style.paddingBottom = 0;
                extensionContainer.Add(endActionLbl);

                // Указание куда именно (если есть).
                string targetText = "";
                if (endD.EndAction == EEndAction.LoadNextChapter && endD.NextChapter != null)
                    targetText = "→  " + endD.NextChapter.name;
                else if (endD.EndAction == EEndAction.LoadSpecificScene && !string.IsNullOrEmpty(endD.TargetSceneName))
                    targetText = "→  " + endD.TargetSceneName;
                if (!string.IsNullOrEmpty(targetText))
                {
                    var tgtLbl = new Label(targetText);
                    tgtLbl.pickingMode = PickingMode.Ignore;
                    tgtLbl.style.fontSize = 10;
                    tgtLbl.style.color = NovellaGraphTheme.Text3;
                    tgtLbl.style.paddingLeft = 18; // отступ под emoji
                    tgtLbl.style.paddingRight = 6;
                    tgtLbl.style.paddingBottom = 4;
                    FormatLongLabel(tgtLbl);
                    extensionContainer.Add(tgtLbl);
                }
                hasExtensionData = true;
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
                    string audioName = audD.AudioAsset != null ? audD.AudioAsset.name : ToolLang.Get("(empty)", "(пусто)");
                    string act = audD.AudioAction == EAudioAction.Play ? "▶" : "⏸";
                    // Канал в виде монохромного [BGM] / [SFX] / [Voice] перед именем.
                    var audioBlock = new Label($"{act}  [{audD.AudioChannel}]  {audioName}");
                    audioBlock.pickingMode = PickingMode.Ignore;
                    audioBlock.style.fontSize = 11;
                    audioBlock.style.color = NovellaGraphTheme.Text2;
                    audioBlock.style.paddingLeft = 6;
                    audioBlock.style.paddingRight = 6;
                    audioBlock.style.paddingTop = 2;
                    audioBlock.style.paddingBottom = 2;
                    FormatLongLabel(audioBlock);
                    extensionContainer.Add(audioBlock);
                    hasExtensionData = true;
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
                    string animText = animD.AnimEvents.Count == 1
                        ? "✨  " + ToolLang.Get("1 animation", "1 анимация")
                        : "✨  " + string.Format(ToolLang.Get("{0} animations", "{0} анимаций"), animD.AnimEvents.Count);
                    var animBlock = new Label(animText);
                    animBlock.pickingMode = PickingMode.Ignore;
                    animBlock.style.fontSize = 11;
                    animBlock.style.color = NovellaGraphTheme.Text2;
                    animBlock.style.paddingLeft = 6;
                    animBlock.style.paddingRight = 6;
                    animBlock.style.paddingTop = 2;
                    animBlock.style.paddingBottom = 2;
                    extensionContainer.Add(animBlock);
                    hasExtensionData = true;
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
                        // Показываем оператор как чёткий символ; для list-операций
                        // (ListAdd/Remove/Clear) — отдельные glyph'ы.
                        string op = v.VarOperation switch
                        {
                            EVarOperation.Set => "=",
                            EVarOperation.Add => "+=",
                            EVarOperation.ListAdd => "+ list",
                            EVarOperation.ListRemove => "− list",
                            EVarOperation.ListClear => "× clear",
                            _ => "=",
                        };
                        var varBlock = new Label($"📊  {v.VariableName}  {op}  {v.VarValue}");
                        varBlock.pickingMode = PickingMode.Ignore;
                        varBlock.style.fontSize = 10;
                        varBlock.style.color = NovellaGraphTheme.Text2;
                        varBlock.style.paddingLeft = 6;
                        varBlock.style.paddingRight = 6;
                        varBlock.style.paddingTop = 1;
                        varBlock.style.paddingBottom = 1;
                        FormatLongLabel(varBlock);
                        extensionContainer.Add(varBlock);
                    }

                    if (varD.Variables.Count > 3)
                    {
                        int remain = varD.Variables.Count - 3;
                        var moreBlock = new Label(string.Format(
                            ToolLang.Get("…and {0} more", "…и ещё {0}"), remain));
                        moreBlock.pickingMode = PickingMode.Ignore;
                        moreBlock.style.fontSize = 9;
                        moreBlock.style.color = NovellaGraphTheme.Text4;
                        moreBlock.style.unityFontStyleAndWeight = FontStyle.Italic;
                        moreBlock.style.paddingLeft = 6;
                        moreBlock.style.paddingRight = 6;
                        moreBlock.style.paddingTop = 2;
                        moreBlock.style.paddingBottom = 4;
                        extensionContainer.Add(moreBlock);
                    }
                    hasExtensionData = true;
                }
            }
            else if (Data is BranchNodeData branchD)
            {
                int n = branchD.Choices != null ? branchD.Choices.Count : 0;
                var bLbl = new Label(n == 1
                    ? "🔀  " + ToolLang.Get("1 choice", "1 выбор")
                    : "🔀  " + string.Format(ToolLang.Get("{0} choices", "{0} вариантов"), n));
                bLbl.pickingMode = PickingMode.Ignore;
                bLbl.style.fontSize = 11;
                bLbl.style.color = NovellaGraphTheme.Text2;
                bLbl.style.paddingLeft = 6;
                bLbl.style.paddingRight = 6;
                bLbl.style.paddingTop = 2;
                bLbl.style.paddingBottom = 2;
                extensionContainer.Add(bLbl);
                hasExtensionData = true;
            }
            else if (Data is ConditionNodeData condD)
            {
                int condCount = condD.Conditions != null ? condD.Conditions.Count : 0;
                if (condCount > 0)
                {
                    var first = condD.Conditions[0];
                    string opStr = first.Operator switch
                    {
                        EConditionOperator.Equal => "==",
                        EConditionOperator.NotEqual => "!=",
                        EConditionOperator.Greater => ">",
                        EConditionOperator.Less => "<",
                        EConditionOperator.GreaterOrEqual => ">=",
                        EConditionOperator.LessOrEqual => "<=",
                        EConditionOperator.Contains => "⊃",
                        EConditionOperator.NotContains => "⊅",
                        _ => "=="
                    };
                    string valStr = first.Value.ToString();
                    var lbl = new Label("❓  " + first.Variable + " " + opStr + " " + valStr);
                    lbl.pickingMode = PickingMode.Ignore;
                    lbl.style.fontSize = 11;
                    lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    lbl.style.color = new Color(0.96f, 0.65f, 0.22f); // тёплый янтарный, тот же что у accent-strip
                    lbl.style.paddingLeft = 6;
                    lbl.style.paddingRight = 6;
                    lbl.style.paddingTop = 2;
                    lbl.style.paddingBottom = 2;
                    FormatLongLabel(lbl);
                    extensionContainer.Add(lbl);

                    if (condCount > 1)
                    {
                        var more = new Label(string.Format(
                            ToolLang.Get("+{0} more conditions", "+{0} условия"), condCount - 1));
                        more.pickingMode = PickingMode.Ignore;
                        more.style.fontSize = 9;
                        more.style.color = NovellaGraphTheme.Text4;
                        more.style.unityFontStyleAndWeight = FontStyle.Italic;
                        more.style.paddingLeft = 6;
                        more.style.paddingRight = 6;
                        more.style.paddingBottom = 2;
                        extensionContainer.Add(more);
                    }
                    hasExtensionData = true;
                }
            }
            else if (Data is RandomNodeData rndD)
            {
                int n = rndD.Choices != null ? rndD.Choices.Count : 0;
                int totalW = 0;
                if (rndD.Choices != null)
                    foreach (var c in rndD.Choices) totalW += c.ChanceWeight;

                var rLbl = new Label("🎲  " + string.Format(
                    ToolLang.Get("{0} chances · weight {1}", "{0} шансов · вес {1}"), n, totalW));
                rLbl.pickingMode = PickingMode.Ignore;
                rLbl.style.fontSize = 11;
                rLbl.style.color = NovellaGraphTheme.Text2;
                rLbl.style.paddingLeft = 6;
                rLbl.style.paddingRight = 6;
                rLbl.style.paddingTop = 2;
                rLbl.style.paddingBottom = 2;
                extensionContainer.Add(rLbl);
                hasExtensionData = true;
            }
            else if (Data is DialogueNodeData dialD)
            {
                // Block 2C — Dialogue body: preview-line + speaker dots.
                // Раньше: цветастые блоки с белой рамкой, по одному на строку —
                // визуально шумно, занимает много места.
                // Теперь: компактная превью первой реплики (italic) +
                // одна строка-row с цветными dots для каждого speaker'а.
                if (dialD.DialogueLines.Count > 0)
                {
                    // ─── Preview первой реплики (на текущем preview-языке) ───
                    string previewLang = _graphView != null && _graphView.Window != null
                        ? _graphView.Window.PreviewLanguage : "EN";
                    string previewText = "";
                    foreach (var line in dialD.DialogueLines)
                    {
                        string t = line.LocalizedPhrase != null ? line.LocalizedPhrase.GetText(previewLang) : "";
                        if (!string.IsNullOrEmpty(t)) { previewText = t; break; }
                    }
                    if (!string.IsNullOrEmpty(previewText))
                    {
                        // Truncate до ~60 символов чтобы не растягивало ноду.
                        string trimmed = previewText.Length > 60
                            ? previewText.Substring(0, 58).TrimEnd() + "…"
                            : previewText;
                        var preview = new Label("«" + trimmed + "»");
                        preview.style.fontSize = 11;
                        preview.style.unityFontStyleAndWeight = FontStyle.Italic;
                        preview.style.color = NovellaGraphTheme.Text2;
                        preview.style.whiteSpace = WhiteSpace.Normal;
                        preview.style.paddingLeft = 4;
                        preview.style.paddingRight = 4;
                        preview.style.paddingTop = 2;
                        preview.style.paddingBottom = 4;
                        preview.style.maxWidth = 240;
                        extensionContainer.Add(preview);
                    }

                    // ─── Speaker dots — компактная строка ●<color> name · ●<color> name ───
                    var distinctSpeakers = dialD.DialogueLines
                        .Where(l => l.Speaker != null)
                        .Select(l => l.Speaker)
                        .Distinct()
                        .ToList();

                    if (distinctSpeakers.Count > 0)
                    {
                        var speakerRow = new VisualElement();
                        speakerRow.style.flexDirection = FlexDirection.Row;
                        speakerRow.style.flexWrap = Wrap.Wrap;
                        speakerRow.style.alignItems = Align.Center;
                        speakerRow.style.paddingLeft = 4;
                        speakerRow.style.paddingRight = 4;
                        speakerRow.style.paddingTop = 2;
                        speakerRow.style.paddingBottom = 2;

                        for (int i = 0; i < distinctSpeakers.Count; i++)
                        {
                            var spk = distinctSpeakers[i];
                            int linesCount = dialD.DialogueLines.Count(l => l.Speaker == spk);

                            // Колоr-dot 8×8 = ThemeColor персонажа.
                            var dot = new VisualElement();
                            dot.pickingMode = PickingMode.Ignore;
                            dot.style.width = 8;
                            dot.style.height = 8;
                            dot.style.borderTopLeftRadius = 4;
                            dot.style.borderTopRightRadius = 4;
                            dot.style.borderBottomLeftRadius = 4;
                            dot.style.borderBottomRightRadius = 4;
                            dot.style.backgroundColor = spk.ThemeColor;
                            dot.style.marginRight = 4;
                            dot.style.marginLeft = i == 0 ? 0 : 8;
                            speakerRow.Add(dot);

                            // Имя + count tiny — flat, без рамок.
                            var nameLbl = new Label(spk.name + " · " + linesCount);
                            nameLbl.pickingMode = PickingMode.Ignore;
                            nameLbl.style.fontSize = 10;
                            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                            nameLbl.style.color = NovellaGraphTheme.Text2;
                            speakerRow.Add(nameLbl);
                        }
                        extensionContainer.Add(speakerRow);
                    }

                    // Total line count внизу — мелкий футер.
                    var totalLbl = new Label(string.Format(
                        ToolLang.Get("{0} lines", "{0} реплик"),
                        dialD.DialogueLines.Count));
                    totalLbl.pickingMode = PickingMode.Ignore;
                    totalLbl.style.fontSize = 9;
                    totalLbl.style.color = NovellaGraphTheme.Text4;
                    totalLbl.style.paddingLeft = 4;
                    totalLbl.style.paddingTop = 4;
                    totalLbl.style.unityFontStyleAndWeight = FontStyle.Italic;
                    extensionContainer.Add(totalLbl);

                    hasExtensionData = true;
                }
            }

            if (hasExtensionData)
            {
                extensionContainer.style.display = DisplayStyle.Flex;
                // Фон extensionContainer: BgPrimary с лёгкой прозрачностью —
                // визуально отделяется от title-bar (BgRaised) тонкой ступенькой.
                // Note без фона остаётся прозрачным как раньше.
                bool noteWithoutBg = Data.NodeType == ENodeType.Note && !(Data is NoteNodeData nd && nd.ShowBackground);
                extensionContainer.style.backgroundColor = noteWithoutBg
                    ? new StyleColor(Color.clear)
                    : new StyleColor(new Color(NovellaGraphTheme.BgPrimary.r, NovellaGraphTheme.BgPrimary.g, NovellaGraphTheme.BgPrimary.b, 0.6f));
                extensionContainer.style.paddingTop = 6;
                extensionContainer.style.paddingBottom = 6;
                extensionContainer.style.paddingLeft = 4;
                extensionContainer.style.paddingRight = 4;
                extensionContainer.style.overflow = Overflow.Visible;
            }
            else { extensionContainer.style.display = DisplayStyle.None; }

            this.MarkDirtyRepaint();
            RefreshExpandedState();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            // Cyan-outline selection (Block 2A) — заменяет стоковую белую линию.
            if (_selectionOutline != null) _selectionOutline.style.display = DisplayStyle.Flex;
            _graphView?.OnNodeSelected?.Invoke(this);
        }
        public override void OnUnselected()
        {
            base.OnUnselected();
            if (_selectionOutline != null) _selectionOutline.style.display = DisplayStyle.None;
        }
        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); if (Data != null) Data.GraphPosition = newPos.position; }

        public void SaveNodeData()
        {
            if (Data == null) return;

            Data.GraphPosition = GetPosition().position;

            if (Data is DialogueNodeData || Data is AudioNodeData || Data is SceneSettingsNodeData || Data is AnimationNodeData || Data is EventBroadcastNodeData || Data is WaitNodeData)
            {
                var nextNodeField = Data.GetType().GetField("NextNodeID");
                if (nextNodeField != null)
                {
                    if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    {
                        nextNodeField.SetValue(Data, OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "");
                    }
                    else nextNodeField.SetValue(Data, "");
                }
            }
            else if (Data is BranchNodeData bnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < bnd.Choices.Count; i++)
                {
                    if (i < ports.Count)
                        bnd.Choices[i].NextNodeID = ports[i].connected && ports[i].connections.Any() ? (ports[i].connections.FirstOrDefault()?.input?.node as NovellaNodeView)?.Data.NodeID ?? "" : "";
                }
            }
            else if (Data is ConditionNodeData cnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < cnd.Choices.Count; i++)
                {
                    if (i < ports.Count)
                        cnd.Choices[i].NextNodeID = ports[i].connected && ports[i].connections.Any() ? (ports[i].connections.FirstOrDefault()?.input?.node as NovellaNodeView)?.Data.NodeID ?? "" : "";
                }
            }
            else if (Data is RandomNodeData rnd)
            {
                var ports = outputContainer.Query<Port>().ToList();
                for (int i = 0; i < rnd.Choices.Count; i++)
                {
                    if (i < ports.Count)
                        rnd.Choices[i].NextNodeID = ports[i].connected && ports[i].connections.Any() ? (ports[i].connections.FirstOrDefault()?.input?.node as NovellaNodeView)?.Data.NodeID ?? "" : "";
                }
            }
            else if (Data is VariableNodeData varData)
            {
                if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    varData.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                else varData.NextNodeID = "";
            }
            else if (Data is SaveNodeData saveD)
            {
                if (OutputPort != null && OutputPort.connected && OutputPort.connections.Any())
                    saveD.NextNodeID = OutputPort.connections.FirstOrDefault()?.input?.node is NovellaNodeView targetNode ? targetNode.Data.NodeID : "";
                else saveD.NextNodeID = "";
            }
            else if (Data.NodeType == ENodeType.CustomDLC)
            {
                var ports = outputContainer.Query<Port>().ToList();
                foreach (var port in ports)
                {
                    var field = GetCachedField(Data.GetType(), port.name);
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