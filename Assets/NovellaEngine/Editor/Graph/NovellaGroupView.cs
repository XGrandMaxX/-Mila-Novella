using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaGroupView : Group
    {
        public NovellaGroupData Data;
        private NovellaGraphView _graphView;

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

            RefreshVisuals();
        }

        // ЖЕСТКАЯ ЗАЩИТА: Отторгаем любые элементы, кроме обычных нод (NovellaNodeView)
        protected override void OnElementsAdded(System.Collections.Generic.IEnumerable<GraphElement> elements)
        {
            base.OnElementsAdded(elements);
            bool hasInvalid = false;

            foreach (var el in elements.ToList())
            {
                if (!(el is NovellaNodeView))
                {
                    this.RemoveElement(el);
                    hasInvalid = true;
                }
            }

            if (hasInvalid && _graphView != null) _graphView.Window.MarkUnsaved();
        }

        public void RefreshVisuals()
        {
            title = Data.Title;

            var titleLabel = headerContainer.Q<Label>("titleLabel");
            if (titleLabel != null)
            {
                titleLabel.style.color = Data.TitleColor;
                titleLabel.style.fontSize = Data.TitleFontSize;
            }

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