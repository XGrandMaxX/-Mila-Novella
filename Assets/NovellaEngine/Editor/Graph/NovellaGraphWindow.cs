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
        public const string DISCORD_LINK = "https://discord.com/users/384220331188944896";
        public const string TELEGRAM_LINK = "https://t.me/pbgj241";
        public const string ASSET_STORE_LINK = "https://assetstore.unity.com/";

        private NovellaTree _currentTree;
        private NovellaGraphView _graphView;
        private NovellaNodeInspectorUI _inspectorUI;
        private VisualElement _rightPanel;
        private IMGUIContainer _inspectorContainer;
        private IMGUIContainer _tutorialContainer;

        private VisualElement _leftPanel;
        private bool _isLeftPanelOpen = true;
        private Button _toggleLeftPanelBtn;
        private float _sidebarWidth = 260f;

        private bool _isInspectorOpen = true;
        private NovellaNodeView _selectedNodeView;
        private NovellaGroupView _selectedGroupView;
        private bool _isStartNodeSelected = false;
        private bool _hasUnsavedChanges = false;
        private Button _saveButton;
        private bool _autoSave = true;
        private double _lastChangeTime = 0;

        private bool _needsFocusFrame = false;
        private int _focusFramesDelay = 0;

        private bool _isTutorialMode = false;

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
            bool isTut = false;
            if (AssetDatabase.Contains(tree))
            {
                string path = AssetDatabase.GetAssetPath(tree).Replace("\\", "/");
                if (path.Contains("/NovellaEngine/Tutorials/"))
                {
                    isTut = true;
                }
            }

            if (isTut)
            {
                NovellaTree instance = Instantiate(tree);
                instance.name = tree.name;
                tree = instance;
            }

            var window = GetWindow<NovellaGraphWindow>("Novella Editor");
            window._currentTree = tree;
            window._isTutorialMode = isTut;
            window.minSize = new Vector2(1000, 600);
            window.ConstructGraph();
        }

        private void OnEnable()
        {
            _autoSave = EditorPrefs.GetBool("NovellaGraph_AutoSave", true);
            _isLeftPanelOpen = EditorPrefs.GetBool("NovellaGraph_SidebarOpen", true);
            _locSettings = NovellaLocalizationSettings.GetOrCreateSettings();

            string savedLang = EditorPrefs.GetString("NovellaGraph_PreviewLang", "");
            if (!string.IsNullOrEmpty(savedLang) && _locSettings.Languages.Contains(savedLang))
                PreviewLanguage = savedLang;
            else if (_locSettings.Languages.Count > 0)
                PreviewLanguage = _locSettings.Languages[0];

            if (_currentTree != null) ConstructGraph();
        }

        private void Update()
        {
            if (!_isTutorialMode && _autoSave && _hasUnsavedChanges)
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

            if (_tutorialContainer != null)
            {
                _tutorialContainer.pickingMode = NovellaTutorialManager.IsTutorialActive ? PickingMode.Position : PickingMode.Ignore;
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

            if (!_isTutorialMode)
            {
                _leftPanel = new VisualElement
                {
                    style = {
                        width = _isLeftPanelOpen ? _sidebarWidth : 0,
                        flexShrink = 0,
                        backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f)),
                        borderRightColor = new StyleColor(Color.black),
                        borderRightWidth = 1,
                        overflow = Overflow.Hidden,
                        flexDirection = FlexDirection.Column
                    }
                };

                var scrollContainer = new ScrollView(ScrollViewMode.Vertical);
                scrollContainer.style.flexGrow = 1;
                _leftPanel.Add(scrollContainer);

                var toolsLabel = new Label("🛠 " + ToolLang.Get("Workspace Tools", "Инструменты"))
                {
                    style = { color = new Color(0.6f, 0.8f, 1f), unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16, marginTop = 15, marginBottom = 10, alignSelf = Align.Center }
                };
                scrollContainer.Add(toolsLabel);

                AddSidebarButton("🎓", ToolLang.Get("Tutorial", "Обучение"), ToolLang.Get("Open interactive tutorials.", "Открыть интерактивные уроки."), () => NovellaWelcomeWindow.ShowWindow(), scrollContainer);

                var separator = new VisualElement { style = { height = 1, backgroundColor = new Color(0.25f, 0.25f, 0.25f), marginTop = 15, marginBottom = 10, marginLeft = 15, marginRight = 15 } };
                scrollContainer.Add(separator);

                AddSidebarButton("🎨", ToolLang.Get("Node Colors", "Цвета Нод"), ToolLang.Get("Change reserved node colors.", "Настроить системные цвета нод."), () => NovellaColorSettingsWindow.ShowWindow(), scrollContainer);

                AddSidebarButton("📋", ToolLang.Get("Global Variables", "База Переменных"), ToolLang.Get("Manage global string variables.", "Настройка всех переменных проекта."), () => NovellaVariableEditorWindow.ShowWindow(), scrollContainer);

                AddSidebarButton("👨‍💻", ToolLang.Get("C# API (Coders)", "C# API (Кодерам)"), ToolLang.Get("Open C# API cheat sheet with examples.", "Открыть шпаргалку с примерами C# кода."), () => NovellaAPIWindow.ShowWindow(), scrollContainer);

                AddSidebarButton("🧩", ToolLang.Get("DLC Modules", "Модули DLC"), ToolLang.Get("Manage installed DLCs.", "Управление установленными модулями DLC."), () => NovellaDLCManagerWindow.ShowWindow(), scrollContainer);

                var ioLabel = new Label(ToolLang.Get("CSV Localization (Text Data):", "CSV Локализация (Весь текст):"))
                {
                    style = { color = new Color(0.7f, 0.7f, 0.7f), fontSize = 11, marginLeft = 12, marginTop = 15, marginBottom = 5, unityFontStyleAndWeight = FontStyle.Bold }
                };
                scrollContainer.Add(ioLabel);

                var ioRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 10, marginRight = 10, justifyContent = Justify.SpaceBetween } };

                var btnExport = new Button(() => NovellaCSVUtility.ExportCSV(_currentTree, _locSettings.Languages))
                { text = "📤 " + ToolLang.Get("Export", "Экспорт"), tooltip = ToolLang.Get("Export all texts to CSV.", "Выгрузить все тексты графа в таблицу Excel/CSV.") };
                btnExport.style.flexGrow = 1; btnExport.style.height = 35; btnExport.style.marginRight = 2;

                var btnImport = new Button(() => { NovellaCSVUtility.ImportCSV(_currentTree, _locSettings.Languages); _inspectorContainer.MarkDirtyRepaint(); _graphView.LoadGraph(); })
                { text = "📥 " + ToolLang.Get("Import", "Импорт"), tooltip = ToolLang.Get("Import translated texts from CSV.", "Загрузить переводы из таблицы обратно в граф.") };
                btnImport.style.flexGrow = 1; btnImport.style.height = 35; btnImport.style.marginLeft = 2;

                ioRow.Add(btnExport);
                ioRow.Add(btnImport);
                scrollContainer.Add(ioRow);

                var jsonLabel = new Label(ToolLang.Get("JSON Backup (Graph Data):", "Бэкап JSON (Весь Граф):"))
                {
                    style = { color = new Color(0.7f, 0.7f, 0.7f), fontSize = 11, marginLeft = 12, marginTop = 15, marginBottom = 5, unityFontStyleAndWeight = FontStyle.Bold }
                };
                scrollContainer.Add(jsonLabel);

                var jsonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 10, marginRight = 10, justifyContent = Justify.SpaceBetween } };

                var btnExportJson = new Button(() => ExportGraphToJSON())
                { text = "📤 " + ToolLang.Get("Export", "Экспорт") + " JSON", tooltip = ToolLang.Get("Export entire graph to JSON backup.", "Сохранить весь граф в JSON.") };
                btnExportJson.style.flexGrow = 1; btnExportJson.style.height = 35; btnExportJson.style.marginRight = 2;

                var btnImportJson = new Button(() => ImportGraphFromJSON())
                { text = "📥 " + ToolLang.Get("Import", "Импорт") + " JSON", tooltip = ToolLang.Get("Restore graph from JSON backup.", "Восстановить граф из JSON.") };
                btnImportJson.style.flexGrow = 1; btnImportJson.style.height = 35; btnImportJson.style.marginLeft = 2;

                jsonRow.Add(btnExportJson);
                jsonRow.Add(btnImportJson);
                scrollContainer.Add(jsonRow);

                var supportContainer = new VisualElement { style = { backgroundColor = new Color(0.12f, 0.12f, 0.12f), paddingBottom = 10, paddingTop = 10, borderTopWidth = 1, borderTopColor = Color.black } };

                var suppTitle = new Label("💬 " + ToolLang.Get("Support & Community", "Поддержка и Комьюнити")) { style = { color = Color.white, unityFontStyleAndWeight = FontStyle.Bold, alignSelf = Align.Center, marginBottom = 5 } };
                supportContainer.Add(suppTitle);

                var linksRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center } };

                var btnDiscord = new Button(() => Application.OpenURL(DISCORD_LINK)) { text = "Discord" };
                btnDiscord.style.backgroundColor = new StyleColor(new Color(0.34f, 0.4f, 0.95f)); btnDiscord.style.color = Color.white; btnDiscord.style.unityFontStyleAndWeight = FontStyle.Bold; btnDiscord.style.height = 25; btnDiscord.style.width = 100;

                var btnTg = new Button(() => Application.OpenURL(TELEGRAM_LINK)) { text = "Telegram" };
                btnTg.style.backgroundColor = new StyleColor(new Color(0.13f, 0.62f, 0.85f)); btnTg.style.color = Color.white; btnTg.style.unityFontStyleAndWeight = FontStyle.Bold; btnTg.style.height = 25; btnTg.style.width = 100;

                linksRow.Add(btnDiscord); linksRow.Add(btnTg);
                supportContainer.Add(linksRow);

                var btnStore = new Button(() => Application.OpenURL(ASSET_STORE_LINK)) { text = "🛒 " + ToolLang.Get("More Assets", "Больше Ассетов") };
                btnStore.style.backgroundColor = new StyleColor(new Color(0.2f, 0.6f, 0.3f)); btnStore.style.color = Color.white; btnStore.style.unityFontStyleAndWeight = FontStyle.Bold; btnStore.style.height = 30; btnStore.style.marginLeft = 15; btnStore.style.marginRight = 15; btnStore.style.marginTop = 10;
                supportContainer.Add(btnStore);

                _leftPanel.Add(supportContainer);

                mainContainer.Add(_leftPanel);

                _toggleLeftPanelBtn = new Button(() => {
                    _isLeftPanelOpen = !_isLeftPanelOpen;
                    _leftPanel.style.width = _isLeftPanelOpen ? _sidebarWidth : 0;
                    _toggleLeftPanelBtn.text = _isLeftPanelOpen ? "◀" : "▶";
                    EditorPrefs.SetBool("NovellaGraph_SidebarOpen", _isLeftPanelOpen);
                })
                { text = _isLeftPanelOpen ? "◀" : "▶" };

                _toggleLeftPanelBtn.style.position = Position.Absolute;
                _toggleLeftPanelBtn.style.top = Length.Percent(45);
                _toggleLeftPanelBtn.style.left = -1f;
                _toggleLeftPanelBtn.style.width = 24;
                _toggleLeftPanelBtn.style.height = 100;
                _toggleLeftPanelBtn.style.borderTopRightRadius = 10;
                _toggleLeftPanelBtn.style.borderBottomRightRadius = 10;
                _toggleLeftPanelBtn.style.borderTopLeftRadius = 0;
                _toggleLeftPanelBtn.style.borderBottomLeftRadius = 0;
                _toggleLeftPanelBtn.style.backgroundColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f, 0.95f));

                graphContainer.Add(_toggleLeftPanelBtn);
            }

            _rightPanel = new VisualElement
            {
                style = {
                    width = _isInspectorOpen ? 550 : 0,
                    flexShrink = 0,
                    backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f)),
                    borderLeftColor = new StyleColor(Color.black),
                    borderLeftWidth = 1
                }
            };

            _inspectorContainer = new IMGUIContainer(DrawInspectorPanel);
            _inspectorContainer.StretchToParentSize();
            _rightPanel.Add(_inspectorContainer);
            mainContainer.Add(graphContainer);
            mainContainer.Add(_rightPanel);

            _graphView.OnNodeSelected = (selectedNode) => { _isStartNodeSelected = false; _selectedNodeView = selectedNode; _selectedGroupView = null; _inspectorUI.SetGraphView(_graphView); _inspectorContainer.MarkDirtyRepaint(); };
            _graphView.OnStartNodeSelected = () => { _selectedNodeView = null; _selectedGroupView = null; _isStartNodeSelected = true; _inspectorUI.SetGraphView(_graphView); _inspectorContainer.MarkDirtyRepaint(); };
            _graphView.OnGroupSelected = (selectedGroup) => { _selectedNodeView = null; _isStartNodeSelected = false; _selectedGroupView = selectedGroup; _inspectorUI.SetGraphView(_graphView); _inspectorContainer.MarkDirtyRepaint(); };

            _graphView.LoadGraph();
            _hasUnsavedChanges = false;
            UpdateButtonState();

            _needsFocusFrame = true;
            _focusFramesDelay = 3;

            _tutorialContainer = new IMGUIContainer(() =>
            {
                NovellaTutorialManager.BlockBackgroundEvents(this);
                NovellaTutorialManager.DrawOverlay(this);
            });

            _tutorialContainer.style.position = Position.Absolute;
            _tutorialContainer.style.left = 0;
            _tutorialContainer.style.right = 0;
            _tutorialContainer.style.top = 0;
            _tutorialContainer.style.bottom = 0;

            _tutorialContainer.pickingMode = NovellaTutorialManager.IsTutorialActive ? PickingMode.Position : PickingMode.Ignore;

            rootVisualElement.Add(_tutorialContainer);
        }

        private void ExportGraphToJSON()
        {
            string path = EditorUtility.SaveFilePanel("Export Novella Graph", "", _currentTree.name + "_Backup", "json");
            if (string.IsNullOrEmpty(path)) return;

            string json = EditorJsonUtility.ToJson(_currentTree, true);
            System.IO.File.WriteAllText(path, json);
            EditorUtility.DisplayDialog("Success", ToolLang.Get("Graph exported to JSON successfully!", "Граф успешно сохранен в JSON!"), "OK");
        }

        private void ImportGraphFromJSON()
        {
            string path = EditorUtility.OpenFilePanel("Import Novella Graph", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            if (EditorUtility.DisplayDialog(ToolLang.Get("Warning", "Внимание"), ToolLang.Get("This will overwrite the current graph. Are you sure?", "Это перезапишет текущий граф. Вы уверены?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
            {
                Undo.RecordObject(_currentTree, "Import JSON");
                string json = System.IO.File.ReadAllText(path);
                EditorJsonUtility.FromJsonOverwrite(json, _currentTree);
                EditorUtility.SetDirty(_currentTree);
                AssetDatabase.SaveAssets();
                ConstructGraph();
                EditorUtility.DisplayDialog("Success", ToolLang.Get("Graph imported successfully!", "Граф успешно загружен!"), "OK");
            }
        }

        private void AddSidebarButton(string icon, string text, string tooltip, System.Action onClick, VisualElement container)
        {
            var btn = new Button(onClick) { tooltip = tooltip };
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.FlexStart;
            btn.style.height = 45;
            btn.style.marginTop = 5;
            btn.style.marginLeft = 10;
            btn.style.marginRight = 10;
            btn.style.paddingLeft = 10;
            btn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
            btn.style.borderTopLeftRadius = 6; btn.style.borderBottomLeftRadius = 6;
            btn.style.borderTopRightRadius = 6; btn.style.borderBottomRightRadius = 6;

            var iconLabel = new Label(icon) { style = { fontSize = 20, width = 35 } };
            var textLabel = new Label(text) { style = { fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.9f, 0.9f, 0.9f) } };

            btn.Add(iconLabel);
            btn.Add(textLabel);
            container.Add(btn);
        }

        private void GenerateToolbar(VisualElement container)
        {
            var toolbarContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, backgroundColor = new Color(0.22f, 0.22f, 0.22f), paddingBottom = 4, paddingTop = 4, alignItems = Align.Center } };

            if (_isTutorialMode)
            {
                var tutLabel = new Label("🎓 " + ToolLang.Get("TUTORIAL MODE (READ-ONLY)", "РЕЖИМ ОБУЧЕНИЯ (ТОЛЬКО ЧТЕНИЕ)"))
                {
                    style = { color = new Color(1f, 0.8f, 0.2f), unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 15, marginRight = 15, alignSelf = Align.Center }
                };
                toolbarContainer.Add(tutLabel);
            }
            else
            {
                _saveButton = new Button(() => SaveGraph()) { text = ToolLang.Get("💾 SAVE", "💾 СОХРАНИТЬ") };
                _saveButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                toolbarContainer.Add(_saveButton);

                var autoSaveToggle = new Toggle(ToolLang.Get("Auto-Save", "Автосохр.")) { value = _autoSave };
                autoSaveToggle.style.marginLeft = 15;
                autoSaveToggle.RegisterValueChangedCallback(evt => { _autoSave = evt.newValue; EditorPrefs.SetBool("NovellaGraph_AutoSave", _autoSave); });
                var toggleLabel = autoSaveToggle.Q<Label>();
                if (toggleLabel != null) { toggleLabel.style.minWidth = StyleKeyword.Auto; toggleLabel.style.paddingRight = 5; }
                toolbarContainer.Add(autoSaveToggle);

                var autoLayoutBtn = new Button(() =>
                {
                    if (_graphView != null)
                    {
                        _graphView.AutoLayout();
                        MarkUnsaved();
                        _graphView.FrameSelection();
                    }
                })
                { text = "✨ " + ToolLang.Get("Auto-Layout", "Выровнять Граф") };
                autoLayoutBtn.style.marginLeft = 15;
                autoLayoutBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                autoLayoutBtn.style.backgroundColor = new StyleColor(new Color(0.2f, 0.4f, 0.6f));
                autoLayoutBtn.style.color = Color.white;
                toolbarContainer.Add(autoLayoutBtn);
            }

            var miniMapToggle = new Toggle("🗺️ " + ToolLang.Get("Map", "Карта")) { value = true };
            miniMapToggle.style.marginLeft = 15;
            miniMapToggle.style.alignSelf = Align.Center;
            var mapToggleLabel = miniMapToggle.Q<Label>();
            if (mapToggleLabel != null) { mapToggleLabel.style.minWidth = StyleKeyword.Auto; mapToggleLabel.style.paddingRight = 5; }
            miniMapToggle.RegisterValueChangedCallback(evt => {
                if (_graphView != null && _graphView.MiniMapInstance != null)
                {
                    _graphView.MiniMapInstance.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    _graphView.MiniMapInstance.visible = evt.newValue;
                }
            });
            toolbarContainer.Add(miniMapToggle);
            // --------------------------------------

            var langButton = new Button(() => { ToolLang.Toggle(); ConstructGraph(); }) { text = ToolLang.IsRU ? "UI: RU" : "UI: EN" };
            langButton.style.marginLeft = 15;

            var langLabel = new Label(ToolLang.Get("Preview Lang:", "Язык превью:")) { style = { marginLeft = 20, marginRight = 5, color = new Color(0.7f, 0.7f, 0.7f) } };
            var langDropdown = new DropdownField(_locSettings.Languages, _locSettings.Languages.IndexOf(PreviewLanguage));
            langDropdown.style.width = 60;

            langDropdown.RegisterValueChangedCallback(evt => {
                PreviewLanguage = evt.newValue;
                EditorPrefs.SetString("NovellaGraph_PreviewLang", PreviewLanguage);
                _inspectorContainer.MarkDirtyRepaint();
                if (_graphView != null) _graphView.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals());
            });

            toolbarContainer.Add(langButton);
            toolbarContainer.Add(langLabel);
            toolbarContainer.Add(langDropdown);

            var spacer = new VisualElement { style = { flexGrow = 1 } };
            toolbarContainer.Add(spacer);

            var toggleInspectorBtn = new Button(() => { _isInspectorOpen = !_isInspectorOpen; _rightPanel.style.width = _isInspectorOpen ? 550 : 0; }) { text = ToolLang.Get("Inspector", "Инспектор") };
            toolbarContainer.Add(toggleInspectorBtn);

            container.Add(toolbarContainer);
        }

        private void DrawInspectorPanel()
        {
            if (_inspectorUI != null)
            {
                if (_selectedGroupView != null)
                {
                    _inspectorUI.DrawGroupInspector(_selectedGroupView);
                }
                else
                {
                    bool isDisabledDLC = false;
                    if (_selectedNodeView != null && _selectedNodeView.Data != null && _selectedNodeView.Data.NodeType == ENodeType.CustomDLC)
                    {
                        // ФИКС ЛАГОВ: Используем кэш Instance вместо тяжелого поиска по AssetDatabase каждый кадр!
                        var settings = NovellaDLCSettings.Instance;
                        if (settings != null) isDisabledDLC = !settings.IsDLCEnabled(_selectedNodeView.Data.GetType().FullName);
                    }

                    if (isDisabledDLC) EditorGUI.BeginDisabledGroup(true);

                    _inspectorUI.DrawInspector(_selectedNodeView, _isStartNodeSelected);

                    if (isDisabledDLC) EditorGUI.EndDisabledGroup();
                }
            }
        }

        public void SaveGraph()
        {
            if (_isTutorialMode) return;
            if (_graphView != null) { _graphView.SyncGraphToData(); EditorUtility.SetDirty(_currentTree); AssetDatabase.SaveAssets(); }
            _hasUnsavedChanges = false; UpdateButtonState();
        }

        public void MarkUnsaved()
        {
            if (_isTutorialMode) return;
            _hasUnsavedChanges = true; _lastChangeTime = EditorApplication.timeSinceStartup; UpdateButtonState();
            if (_currentTree != null) EditorUtility.SetDirty(_currentTree);
        }

        public void RefreshAllNodes()
        {
            if (_graphView != null) _graphView.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals());
        }

        private void UpdateButtonState()
        {
            if (_saveButton == null) return;
            _saveButton.SetEnabled(_hasUnsavedChanges);
            _saveButton.style.backgroundColor = _hasUnsavedChanges ? new StyleColor(new Color(0.2f, 0.6f, 0.2f)) : new StyleColor(new Color(0.3f, 0.3f, 0.3f));
        }

        public void FocusAndHighlightNode(string nodeID)
        {
            EditorApplication.delayCall += () => {
                if (_graphView == null) return;
                var targetNode = _graphView.nodes.ToList().OfType<NovellaNodeView>().FirstOrDefault(n => n.Data.NodeID == nodeID);
                if (targetNode != null)
                {
                    _graphView.ClearSelection();
                    _graphView.AddToSelection(targetNode);
                    _graphView.FrameSelection();

                    var border = targetNode.Q("node-border");
                    if (border != null)
                    {
                        var oldColor = border.style.borderTopColor;
                        var oldW = border.style.borderTopWidth;

                        Color highlight = new Color(1f, 0.85f, 0f, 1f);
                        border.style.borderTopColor = highlight;
                        border.style.borderBottomColor = highlight;
                        border.style.borderLeftColor = highlight;
                        border.style.borderRightColor = highlight;
                        border.style.borderTopWidth = 4;
                        border.style.borderBottomWidth = 4;
                        border.style.borderLeftWidth = 4;
                        border.style.borderRightWidth = 4;

                        targetNode.schedule.Execute(() => {
                            if (border == null) return;
                            border.style.borderTopColor = oldColor;
                            border.style.borderBottomColor = oldColor;
                            border.style.borderLeftColor = oldColor;
                            border.style.borderRightColor = oldColor;
                            border.style.borderTopWidth = oldW;
                            border.style.borderBottomWidth = oldW;
                            border.style.borderLeftWidth = oldW;
                            border.style.borderRightWidth = oldW;
                        }).StartingIn(2000);
                    }
                }
            };
        }
    }
}