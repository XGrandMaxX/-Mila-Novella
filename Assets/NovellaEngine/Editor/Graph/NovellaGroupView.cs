using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaGroupView : Group
    {
        public NovellaGroupData Data;
        private NovellaGraphView _graphView;
        private Label _descLabel;
        private Button _toggleDescBtn;
        private VisualElement _descContainer;

        public NovellaGroupView(NovellaGroupData data, NovellaGraphView graphView)
        {
            Data = data;
            _graphView = graphView;
            viewDataKey = data.GroupID;

            var header = this.Q("headerContainer");
            if (header != null)
            {
                header.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 0.9f));
                header.style.borderBottomWidth = 2;
                header.style.borderBottomColor = new StyleColor(Color.black);
            }

            var titleContainer = header.Q("titleContainer");

            _toggleDescBtn = new Button(ToggleDesc) { text = Data.IsDescExpanded ? "▼" : "▶" };
            _toggleDescBtn.style.width = 20;
            _toggleDescBtn.style.height = 20;
            _toggleDescBtn.style.backgroundColor = Color.clear;
            _toggleDescBtn.style.borderTopWidth = 0; _toggleDescBtn.style.borderBottomWidth = 0;
            _toggleDescBtn.style.borderLeftWidth = 0; _toggleDescBtn.style.borderRightWidth = 0;

            if (titleContainer != null) titleContainer.Insert(0, _toggleDescBtn);

            _descContainer = new VisualElement();
            _descContainer.style.display = Data.IsDescExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _descContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.95f));
            _descContainer.style.paddingLeft = 10; _descContainer.style.paddingRight = 10;
            _descContainer.style.paddingTop = 10; _descContainer.style.paddingBottom = 10;

            _descContainer.style.position = Position.Absolute;
            _descContainer.style.bottom = new Length(100, LengthUnit.Percent);
            _descContainer.style.left = 0;
            _descContainer.style.right = 0;

            _descContainer.style.borderBottomWidth = 1; _descContainer.style.borderBottomColor = Color.black;
            _descContainer.style.borderTopLeftRadius = 8; _descContainer.style.borderTopRightRadius = 8;

            _descLabel = new Label(Data.Description) { style = { whiteSpace = WhiteSpace.Normal } };
            _descContainer.Add(_descLabel);

            this.Add(_descContainer);

            RefreshVisuals();
        }

        private void ToggleDesc()
        {
            Data.IsDescExpanded = !Data.IsDescExpanded;
            _descContainer.style.display = Data.IsDescExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleDescBtn.text = Data.IsDescExpanded ? "▼" : "▶";
            _graphView.Window.MarkUnsaved();
        }

        public void RefreshVisuals()
        {
            title = Data.Title;
            _descLabel.text = Data.Description;

            var titleLabel = headerContainer.Q<Label>("titleLabel");
            if (titleLabel != null)
            {
                titleLabel.style.color = Data.TitleColor;
                titleLabel.style.fontSize = Data.TitleFontSize;
            }

            _descLabel.style.color = Data.DescColor;
            _descLabel.style.fontSize = Data.DescFontSize;

            this.style.backgroundColor = Data.BackgroundColor;
            var content = this.Q("contentContainer");
            if (content != null) content.style.backgroundColor = new StyleColor(Color.clear);

            this.style.borderTopWidth = 2; this.style.borderBottomWidth = 2;
            this.style.borderLeftWidth = 2; this.style.borderRightWidth = 2;
            this.style.borderTopColor = Data.BorderColor;
            this.style.borderBottomColor = Data.BorderColor;
            this.style.borderLeftColor = Data.BorderColor;
            this.style.borderRightColor = Data.BorderColor;
            this.style.borderTopLeftRadius = 8; this.style.borderTopRightRadius = 8;
            this.style.borderBottomLeftRadius = 8; this.style.borderBottomRightRadius = 8;

            _descContainer.style.borderTopWidth = 2; _descContainer.style.borderBottomWidth = 2;
            _descContainer.style.borderLeftWidth = 2; _descContainer.style.borderRightWidth = 2;
            _descContainer.style.borderTopColor = Data.BorderColor;
            _descContainer.style.borderBottomColor = Data.BorderColor;
            _descContainer.style.borderLeftColor = Data.BorderColor;
            _descContainer.style.borderRightColor = Data.BorderColor;
        }

        public override void OnSelected() { base.OnSelected(); _graphView?.OnGroupSelected?.Invoke(this); }

        public void SaveGroupData()
        {
            Data.Position = GetPosition();
            Data.ContainedNodeIDs.Clear();
            foreach (var el in containedElements)
            {
                if (el is NovellaNodeView nv) Data.ContainedNodeIDs.Add(nv.Data.NodeID);
            }
        }
    }
}