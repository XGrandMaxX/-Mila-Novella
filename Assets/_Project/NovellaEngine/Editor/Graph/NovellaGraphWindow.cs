using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaGraphWindow : EditorWindow
    {
        private NovellaTree _currentTree;
        private NovellaGraphView _graphView;
        private NovellaNodeInspectorUI _inspectorUI;
        private VisualElement _rightPanel;
        private IMGUIContainer _inspectorContainer;

        private bool _isInspectorOpen = true;
        private NovellaNodeView _selectedNodeView;
        private bool _isStartNodeSelected = false;
        private bool _hasUnsavedChanges = false;
        private Button _saveButton;
        private bool _autoSave = true;
        private double _lastChangeTime = 0;

        private bool _needsFocusFrame = false;
        private int _focusFramesDelay = 0;

        private NovellaLocalizationSettings _locSettings;
        public string PreviewLanguage { get; private set; } = "EN";

        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceID, int line)
        {
            NovellaTree tree = EditorUtility.InstanceIDToObject(instanceID) as NovellaTree;
            if (tree != null) { OpenGraphWindow(tree); return true; }
            return false;
        }

        public static void OpenGraphWindow(NovellaTree tree)
        {
            var window = GetWindow<NovellaGraphWindow>("Novella Editor");
            window._currentTree = tree;
            window.minSize = new Vector2(1000, 600);
            window.ConstructGraph();
        }

        private void OnEnable()
        {
            _autoSave = EditorPrefs.GetBool("NovellaGraph_AutoSave", true);
            _locSettings = NovellaLocalizationSettings.GetOrCreateSettings();
            if (_locSettings.Languages.Count > 0) PreviewLanguage = _locSettings.Languages[0];

            if (_currentTree != null) ConstructGraph();
        }

        private void Update()
        {
            if (_autoSave && _hasUnsavedChanges)
                if (EditorApplication.timeSinceStartup - _lastChangeTime > 0.5f) SaveGraph();

            if (_needsFocusFrame && _graphView != null)
            {
                _focusFramesDelay--;
                if (_focusFramesDelay <= 0)
                {
                    _needsFocusFrame = false;
                    _graphView.ClearSelection();

                    var pinnedNode = _graphView.nodes.ToList().OfType<NovellaNodeView>().FirstOrDefault(n => n.Data != null && n.Data.IsPinned);

                    if (pinnedNode != null)
                        _graphView.AddToSelection(pinnedNode);
                    else if (_graphView.StartNodeView != null)
                        _graphView.AddToSelection(_graphView.StartNodeView);

                    _graphView.FrameSelection();
                    _graphView.ClearSelection();
                }
            }
        }

        public void ConstructGraph()
        {
            if (_currentTree == null) return;
            rootVisualElement.Clear();

            _graphView = new NovellaGraphView(this, _currentTree);
            _graphView.StretchToParentSize();
            _inspectorUI = new NovellaNodeInspectorUI(_currentTree, _graphView, MarkUnsaved, this);
            GenerateToolbar(rootVisualElement);

            var mainContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            rootVisualElement.Add(mainContainer);
            var graphContainer = new VisualElement { style = { flexGrow = 1 } };
            graphContainer.Add(_graphView);

            _rightPanel = new VisualElement { style = { width = _isInspectorOpen ? 480 : 0, backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f)), borderLeftColor = new StyleColor(Color.black), borderLeftWidth = 1 } };

            _inspectorContainer = new IMGUIContainer(DrawInspectorPanel);
            _inspectorContainer.StretchToParentSize();
            _rightPanel.Add(_inspectorContainer);
            mainContainer.Add(graphContainer);
            mainContainer.Add(_rightPanel);

            _graphView.OnNodeSelected = (selectedNode) => { _isStartNodeSelected = false; _selectedNodeView = selectedNode; _inspectorUI.SetGraphView(_graphView); _inspectorContainer.MarkDirtyRepaint(); };
            _graphView.OnStartNodeSelected = () => { _selectedNodeView = null; _isStartNodeSelected = true; _inspectorUI.SetGraphView(_graphView); _inspectorContainer.MarkDirtyRepaint(); };

            _graphView.LoadGraph();
            _hasUnsavedChanges = false;
            UpdateButtonState();

            _needsFocusFrame = true;
            _focusFramesDelay = 3;
        }

        private void GenerateToolbar(VisualElement container)
        {
            var toolbarContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = new Color(0.22f, 0.22f, 0.22f), paddingBottom = 4, paddingTop = 4, alignItems = Align.Center } };

            _saveButton = new Button(() => SaveGraph()) { text = ToolLang.Get("💾 SAVE", "💾 СОХРАНИТЬ") };
            _saveButton.style.unityFontStyleAndWeight = FontStyle.Bold;

            var autoSaveToggle = new Toggle(ToolLang.Get("Auto-Save", "Автосохр.")) { value = _autoSave };
            autoSaveToggle.style.marginLeft = 15;
            autoSaveToggle.RegisterValueChangedCallback(evt => { _autoSave = evt.newValue; EditorPrefs.SetBool("NovellaGraph_AutoSave", _autoSave); });

            var toggleLabel = autoSaveToggle.Q<Label>();
            if (toggleLabel != null)
            {
                toggleLabel.style.minWidth = StyleKeyword.Auto;
                toggleLabel.style.paddingRight = 5;
            }

            var langButton = new Button(() => { ToolLang.Toggle(); ConstructGraph(); }) { text = ToolLang.IsRU ? "UI: RU" : "UI: EN" };
            langButton.style.marginLeft = 15;

            var langLabel = new Label(ToolLang.Get("Preview Lang:", "Язык превью:")) { style = { marginLeft = 20, marginRight = 5, color = new Color(0.7f, 0.7f, 0.7f) } };
            var langDropdown = new DropdownField(_locSettings.Languages, _locSettings.Languages.IndexOf(PreviewLanguage));
            langDropdown.style.width = 60;
            langDropdown.RegisterValueChangedCallback(evt => { PreviewLanguage = evt.newValue; _inspectorContainer.MarkDirtyRepaint(); _graphView.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals()); });

            toolbarContainer.Add(_saveButton);
            toolbarContainer.Add(autoSaveToggle);
            toolbarContainer.Add(langButton);
            toolbarContainer.Add(langLabel);
            toolbarContainer.Add(langDropdown);

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            toolbarContainer.Add(spacer);

            var exportBtn = new Button(() => NovellaCSVUtility.ExportCSV(_currentTree, _locSettings.Languages)) { text = ToolLang.Get("📤 Export CSV", "📤 Экспорт CSV") };
            var importBtn = new Button(() => { NovellaCSVUtility.ImportCSV(_currentTree, _locSettings.Languages); _inspectorContainer.MarkDirtyRepaint(); _graphView.LoadGraph(); }) { text = ToolLang.Get("📥 Import CSV", "📥 Импорт CSV") };
            exportBtn.style.marginRight = 5; importBtn.style.marginRight = 15;

            var toggleInspectorBtn = new Button(() => { _isInspectorOpen = !_isInspectorOpen; _rightPanel.style.width = _isInspectorOpen ? 480 : 0; }) { text = ToolLang.Get("Inspector", "Инспектор") };

            toolbarContainer.Add(exportBtn);
            toolbarContainer.Add(importBtn);
            toolbarContainer.Add(toggleInspectorBtn);
            container.Add(toolbarContainer);
        }

        private void DrawInspectorPanel() { if (_inspectorUI != null) _inspectorUI.DrawInspector(_selectedNodeView, _isStartNodeSelected); }
        public void SaveGraph() { if (_graphView != null) { _graphView.SyncGraphToData(); EditorUtility.SetDirty(_currentTree); AssetDatabase.SaveAssets(); } _hasUnsavedChanges = false; UpdateButtonState(); }
        public void MarkUnsaved() { _hasUnsavedChanges = true; _lastChangeTime = EditorApplication.timeSinceStartup; UpdateButtonState(); if (_currentTree != null) EditorUtility.SetDirty(_currentTree); }
        private void UpdateButtonState() { if (_saveButton == null) return; _saveButton.SetEnabled(_hasUnsavedChanges); _saveButton.style.backgroundColor = _hasUnsavedChanges ? new StyleColor(new Color(0.2f, 0.6f, 0.2f)) : new StyleColor(new Color(0.3f, 0.3f, 0.3f)); }
    }
}