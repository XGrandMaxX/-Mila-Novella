using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;
using System.Collections.Generic;
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
        private bool _tutorialContainerInTree; // флаг: overlay сейчас в дереве?
        private double _lastTutorialCheck;     // время последней проверки (для throttle)

        private VisualElement _leftPanel;
        private bool _isLeftPanelOpen = true;
        private Button _toggleLeftPanelBtn;
        private float _sidebarWidth = 260f;

        // Block 3A: bottom node-palette (drag&drop источник создания нод).
        private NovellaNodePalette _nodePalette;
        private bool _isPaletteOpen = true;
        private Button _palToggleBtn;

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
            _isPaletteOpen = EditorPrefs.GetBool("NovellaGraph_PaletteOpen", true);
            _locSettings = NovellaLocalizationSettings.GetOrCreateSettings();

            string savedLang = EditorPrefs.GetString("NovellaGraph_PreviewLang", "");
            if (!string.IsNullOrEmpty(savedLang) && _locSettings.Languages.Contains(savedLang))
                PreviewLanguage = savedLang;
            else if (_locSettings.Languages.Count > 0)
                PreviewLanguage = _locSettings.Languages[0];

            if (_currentTree != null) ConstructGraph();
        }

        // Update вызывается ~100 раз/сек. Раньше каждый вызов сетил pickingMode на overlay
        // (даже если он не менялся), что триггерило relayout. Теперь:
        //   1) Сам tutorial overlay live в дереве только когда туториал активен (lazy attach).
        //   2) Проверку статуса делаем не чаще 5 Hz — пользователь не заметит задержки в 200мс
        //      когда туториал стартует/заканчивается, зато при перетаскивании ноды у нас не
        //      капает CPU на overlay-операции каждый Editor-тик.
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

            // Throttle: проверяем статус туториала максимум 5 раз/сек.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastTutorialCheck >= 0.2)
            {
                _lastTutorialCheck = now;
                SyncTutorialOverlay();
            }
        }

        private void SyncTutorialOverlay()
        {
            if (_tutorialContainer == null || rootVisualElement == null) return;
            bool needed = NovellaTutorialManager.IsTutorialActive;
            if (needed == _tutorialContainerInTree) return;

            if (needed)
            {
                if (_tutorialContainer.parent == null) rootVisualElement.Add(_tutorialContainer);
                _tutorialContainerInTree = true;
            }
            else
            {
                if (_tutorialContainer.parent != null) _tutorialContainer.RemoveFromHierarchy();
                _tutorialContainerInTree = false;
            }
        }

        public void ConstructGraph()
        {
            if (_currentTree == null) return;
            rootVisualElement.Clear();
            _tutorialContainerInTree = false; // дерево полностью пересобирается → overlay тоже

            _graphView = new NovellaGraphView(this, _currentTree);
            _graphView.StretchToParentSize();
            _inspectorUI = new NovellaNodeInspectorUI(_currentTree, _graphView, MarkUnsaved, this);
            GenerateToolbar(rootVisualElement);

            var mainContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            rootVisualElement.Add(mainContainer);

            // graphContainer теперь column-flex чтобы под graph-area можно было
            // положить bottom node-palette (Block 3A). graphArea — внутренний
            // контейнер с flexGrow=1, ему graphView растягивается на всю
            // площадь. Палитра сидит фиксированной высотой 132px ниже.
            var graphContainer = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            var graphArea = new VisualElement { style = { flexGrow = 1, position = Position.Relative } };
            graphArea.Add(_graphView);
            graphContainer.Add(graphArea);

            // Bottom node-palette — добавляем во всех модах (включая туториал;
            // в read-only туторе drag не сработает, но визуальное знакомство
            // с интерфейсом юзеру даём).
            _nodePalette = new NovellaNodePalette(_graphView);
            _nodePalette.SetCollapsed(!_isPaletteOpen);
            graphContainer.Add(_nodePalette);

            if (!_isTutorialMode)
            {
                BuildLeftPanel(mainContainer, graphContainer);
            }

            _rightPanel = new VisualElement
            {
                style = {
                    width = _isInspectorOpen ? 550 : 0,
                    flexShrink = 0,
                    backgroundColor = NovellaGraphTheme.BgPrimary,
                    borderLeftColor = NovellaGraphTheme.Border,
                    borderLeftWidth = 1,
                    overflow = Overflow.Hidden,
                }
            };
            // Анимация ширины: 0.20s ease-out-cubic — smooth toggle inspector'а.
            _rightPanel.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("width"),
            });
            _rightPanel.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.20f, TimeUnit.Second),
            });
            _rightPanel.style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction> {
                new EasingFunction(EasingMode.EaseOutCubic),
            });

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

            // Tutorial overlay создаём, но НЕ добавляем в дерево.
            // Добавлять/удалять будет SyncTutorialOverlay() в Update() — только когда
            // реально стартует/заканчивается туториал. Это убирает CPU-нагрузку на пустой
            // IMGUIContainer когда юзер просто двигает ноды (Graph Repaint'ит сам себя
            // при каждом mouse-move во время drag — раньше overlay тоже перерисовывался).
            _tutorialContainer = new IMGUIContainer(() =>
            {
                if (!NovellaTutorialManager.IsTutorialActive) return;
                NovellaTutorialManager.BlockBackgroundEvents(this);
                NovellaTutorialManager.DrawOverlay(this);
            });

            _tutorialContainer.style.position = Position.Absolute;
            _tutorialContainer.style.left = 0;
            _tutorialContainer.style.right = 0;
            _tutorialContainer.style.top = 0;
            _tutorialContainer.style.bottom = 0;
            _tutorialContainer.pickingMode = PickingMode.Position;
            // НЕ Add в rootVisualElement — это сделает SyncTutorialOverlay() при необходимости.
            // Но если туториал УЖЕ активен в момент пересборки графа — синканём сразу.
            SyncTutorialOverlay();
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

        // ─── Левая панель: инструменты графа в стиле Hub-сайдбара ───
        // Группы: Tools / Localization / Backup / Community. Каждая группа имеет
        // section-header «UPPERCASE 9pt» и slim-кнопки (no border idle, hover-fill).
        // Discord/Telegram/AssetStore-плашки заменены одной строкой Community с
        // три маленькими icon-only кнопками — больше не выпадают из общего стиля.
        private void BuildLeftPanel(VisualElement mainContainer, VisualElement graphContainer)
        {
            _sidebarWidth = 240f;
            _leftPanel = new VisualElement
            {
                style = {
                    width = _isLeftPanelOpen ? _sidebarWidth : 0,
                    flexShrink = 0,
                    backgroundColor = NovellaGraphTheme.BgSide,
                    borderRightColor = NovellaGraphTheme.Border,
                    borderRightWidth = 1,
                    overflow = Overflow.Hidden,
                    flexDirection = FlexDirection.Column,
                }
            };
            // Анимация ширины — smooth toggle левой панели.
            _leftPanel.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("width"),
            });
            _leftPanel.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.20f, TimeUnit.Second),
            });
            _leftPanel.style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction> {
                new EasingFunction(EasingMode.EaseOutCubic),
            });

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _leftPanel.Add(scroll);

            // Заголовок «Workspace tools».
            var brand = new Label("🛠  " + ToolLang.Get("Tools", "Инструменты"))
            {
                style = {
                    color = NovellaGraphTheme.Text1,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                    marginTop = 14, marginBottom = 8,
                    marginLeft = 14, marginRight = 14,
                }
            };
            scroll.Add(brand);

            // ─── TOOLS ───
            scroll.Add(NovellaGraphTheme.CreateSectionHeader(
                ToolLang.Get("Workspace", "Рабочее место")));
            scroll.Add(BuildSidebarBtn("🎓", ToolLang.Get("Tutorial", "Обучение"),
                ToolLang.Get("Open interactive tutorials.", "Открыть интерактивные уроки."),
                () => NovellaWelcomeWindow.ShowWindow()));
            scroll.Add(BuildSidebarBtn("🎨", ToolLang.Get("Node colors", "Цвета нод"),
                ToolLang.Get("Customize per-type node colors.", "Настроить цвета нод по типам."),
                () => NovellaColorSettingsWindow.ShowWindow()));
            scroll.Add(BuildSidebarBtn("📊", ToolLang.Get("Variables", "Переменные"),
                ToolLang.Get("Manage all project variables.", "Все переменные проекта."),
                () => NovellaVariableEditorModule.ShowWindow()));
            scroll.Add(BuildSidebarBtn("👨‍💻", ToolLang.Get("C# API", "C# API"),
                ToolLang.Get("Cheat sheet with code samples.", "Шпаргалка с примерами кода."),
                () => NovellaAPIWindow.ShowWindow()));
            scroll.Add(BuildSidebarBtn("🧩", ToolLang.Get("DLC modules", "DLC модули"),
                ToolLang.Get("Manage installed DLCs.", "Управление установленными модулями DLC."),
                () => NovellaDLCManagerModule.ShowWindow()));

            // ─── LOCALIZATION ───
            scroll.Add(NovellaGraphTheme.CreateSectionHeader(
                ToolLang.Get("Localization (CSV)", "Локализация (CSV)")));

            var ioRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8, marginRight = 8, marginTop = 2 } };
            var btnExport = new Button(() => NovellaCSVUtility.ExportCSV(_currentTree, _locSettings.Languages))
            { text = "📤 " + ToolLang.Get("Export", "Экспорт"),
              tooltip = ToolLang.Get("Export all graph texts to CSV.", "Выгрузить все тексты графа в CSV.") };
            NovellaGraphTheme.ApplySlimButton(btnExport, height: 28, paddingX: 8);
            btnExport.style.flexGrow = 1; btnExport.style.marginLeft = 0; btnExport.style.marginRight = 4;

            var btnImport = new Button(() => {
                NovellaCSVUtility.ImportCSV(_currentTree, _locSettings.Languages);
                _inspectorContainer.MarkDirtyRepaint();
                _graphView.LoadGraph();
            })
            { text = "📥 " + ToolLang.Get("Import", "Импорт"),
              tooltip = ToolLang.Get("Import translated texts from CSV.", "Загрузить переводы из CSV.") };
            NovellaGraphTheme.ApplySlimButton(btnImport, height: 28, paddingX: 8);
            btnImport.style.flexGrow = 1; btnImport.style.marginLeft = 0;

            ioRow.Add(btnExport);
            ioRow.Add(btnImport);
            scroll.Add(ioRow);

            // ─── BACKUP ───
            scroll.Add(NovellaGraphTheme.CreateSectionHeader(
                ToolLang.Get("Backup (JSON)", "Бэкап (JSON)")));

            var jsonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8, marginRight = 8, marginTop = 2 } };
            var btnExportJson = new Button(() => ExportGraphToJSON())
            { text = "📤 JSON",
              tooltip = ToolLang.Get("Save entire graph to JSON file.", "Сохранить весь граф в JSON-файл.") };
            NovellaGraphTheme.ApplySlimButton(btnExportJson, height: 28, paddingX: 8);
            btnExportJson.style.flexGrow = 1; btnExportJson.style.marginLeft = 0; btnExportJson.style.marginRight = 4;

            var btnImportJson = new Button(() => ImportGraphFromJSON())
            { text = "📥 JSON",
              tooltip = ToolLang.Get("Restore graph from JSON file.", "Восстановить граф из JSON.") };
            NovellaGraphTheme.ApplySlimButton(btnImportJson, height: 28, paddingX: 8);
            btnImportJson.style.flexGrow = 1; btnImportJson.style.marginLeft = 0;

            jsonRow.Add(btnExportJson);
            jsonRow.Add(btnImportJson);
            scroll.Add(jsonRow);

            // ─── COMMUNITY (одна компактная строка вместо разноцветных плашек) ───
            scroll.Add(NovellaGraphTheme.CreateSectionHeader(
                ToolLang.Get("Community", "Сообщество")));

            var commRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 8, marginRight = 8, marginTop = 2, marginBottom = 16 } };

            var btnDiscord = new Button(() => Application.OpenURL(DISCORD_LINK))
            { text = "💬 Discord", tooltip = "Discord" };
            NovellaGraphTheme.ApplySlimButton(btnDiscord, height: 28, paddingX: 6);
            btnDiscord.style.flexGrow = 1; btnDiscord.style.marginLeft = 0; btnDiscord.style.marginRight = 4;
            btnDiscord.style.fontSize = 10;

            var btnTg = new Button(() => Application.OpenURL(TELEGRAM_LINK))
            { text = "✈ Telegram", tooltip = "Telegram" };
            NovellaGraphTheme.ApplySlimButton(btnTg, height: 28, paddingX: 6);
            btnTg.style.flexGrow = 1; btnTg.style.marginLeft = 0; btnTg.style.marginRight = 4;
            btnTg.style.fontSize = 10;

            var btnStore = new Button(() => Application.OpenURL(ASSET_STORE_LINK))
            { text = "🛒 Store", tooltip = ToolLang.Get("More assets in the Store", "Больше ассетов в Store") };
            NovellaGraphTheme.ApplySlimButton(btnStore, height: 28, paddingX: 6);
            btnStore.style.flexGrow = 1; btnStore.style.marginLeft = 0;
            btnStore.style.fontSize = 10;

            commRow.Add(btnDiscord);
            commRow.Add(btnTg);
            commRow.Add(btnStore);
            scroll.Add(commRow);

            mainContainer.Add(_leftPanel);

            // ─── Toggle-кнопка свёртывания (узкий язычок справа от панели) ───
            _toggleLeftPanelBtn = new Button(() => {
                _isLeftPanelOpen = !_isLeftPanelOpen;
                _leftPanel.style.width = _isLeftPanelOpen ? _sidebarWidth : 0;
                _toggleLeftPanelBtn.text = _isLeftPanelOpen ? "◀" : "▶";
                EditorPrefs.SetBool("NovellaGraph_SidebarOpen", _isLeftPanelOpen);
            })
            { text = _isLeftPanelOpen ? "◀" : "▶",
              tooltip = ToolLang.Get("Toggle tools panel", "Свернуть/развернуть панель инструментов") };

            _toggleLeftPanelBtn.style.position = Position.Absolute;
            _toggleLeftPanelBtn.style.top = Length.Percent(45);
            _toggleLeftPanelBtn.style.left = -1f;
            _toggleLeftPanelBtn.style.width = 18;
            _toggleLeftPanelBtn.style.height = 64;
            _toggleLeftPanelBtn.style.paddingLeft = 0;
            _toggleLeftPanelBtn.style.paddingRight = 0;
            _toggleLeftPanelBtn.style.marginLeft = 0;
            _toggleLeftPanelBtn.style.borderTopLeftRadius = 0;
            _toggleLeftPanelBtn.style.borderBottomLeftRadius = 0;
            _toggleLeftPanelBtn.style.borderTopRightRadius = 8;
            _toggleLeftPanelBtn.style.borderBottomRightRadius = 8;
            _toggleLeftPanelBtn.style.borderTopWidth = 1;
            _toggleLeftPanelBtn.style.borderBottomWidth = 1;
            _toggleLeftPanelBtn.style.borderRightWidth = 1;
            _toggleLeftPanelBtn.style.borderLeftWidth = 0;
            _toggleLeftPanelBtn.style.borderTopColor = NovellaGraphTheme.Border;
            _toggleLeftPanelBtn.style.borderBottomColor = NovellaGraphTheme.Border;
            _toggleLeftPanelBtn.style.borderRightColor = NovellaGraphTheme.Border;
            _toggleLeftPanelBtn.style.backgroundColor = NovellaGraphTheme.BgSide;
            _toggleLeftPanelBtn.style.color = NovellaGraphTheme.Text3;
            _toggleLeftPanelBtn.style.fontSize = 10;

            graphContainer.Add(_toggleLeftPanelBtn);
        }

        // Sidebar-кнопка в стиле Hub: иконка слева + название, hover-fill.
        // Используется для пунктов меню «Tools / Workspace».
        private VisualElement BuildSidebarBtn(string icon, string text, string tooltip, System.Action onClick)
        {
            var btn = new Button(onClick) { tooltip = tooltip };
            NovellaGraphTheme.ApplySidebarButton(btn);

            var iconLbl = new Label(icon)
            {
                style = {
                    fontSize = 14,
                    width = 22,
                    marginRight = 8,
                    color = NovellaGraphTheme.Text2,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            iconLbl.pickingMode = PickingMode.Ignore;

            var textLbl = new Label(text)
            {
                style = {
                    fontSize = 12,
                    unityFontStyleAndWeight = FontStyle.Normal,
                    color = NovellaGraphTheme.Text2,
                    flexGrow = 1,
                }
            };
            textLbl.pickingMode = PickingMode.Ignore;

            btn.Add(iconLbl);
            btn.Add(textLbl);
            return btn;
        }

        private void GenerateToolbar(VisualElement container)
        {
            // Toolbar в стиле Hub: BgSide-фон, тонкая нижняя обводка, slim-кнопки
            // одной высоты, группы разделены вертикальными разделителями.
            var toolbarContainer = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    backgroundColor = NovellaGraphTheme.BgSide,
                    paddingTop = 5, paddingBottom = 5,
                    paddingLeft = 8, paddingRight = 8,
                    alignItems = Align.Center,
                    borderBottomWidth = 1,
                    borderBottomColor = NovellaGraphTheme.Border,
                    height = 40,
                    flexShrink = 0,
                }
            };

            if (_isTutorialMode)
            {
                var tutBadge = new Label("🎓 " + ToolLang.Get("TUTORIAL MODE — READ-ONLY", "РЕЖИМ ОБУЧЕНИЯ — ТОЛЬКО ЧТЕНИЕ"))
                {
                    style = {
                        color = NovellaGraphTheme.Warning,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginLeft = 12, marginRight = 12,
                        fontSize = 11,
                        alignSelf = Align.Center
                    }
                };
                toolbarContainer.Add(tutBadge);
            }
            else
            {
                // ─── Save (accent-CTA, активна только при dirty) ───
                _saveButton = new Button(() => SaveGraph())
                { text = "💾  " + ToolLang.Get("Save", "Сохранить") };
                NovellaGraphTheme.ApplySaveButton(_saveButton);
                toolbarContainer.Add(_saveButton);

                // ─── Auto-Save toggle-button (вместо Unity Toggle) ───
                bool autoSaveOn = _autoSave;
                Button autoSaveBtn = null;
                System.Action<bool> setAutoSave = null;
                autoSaveBtn = new Button(() => {
                    autoSaveOn = !autoSaveOn;
                    _autoSave = autoSaveOn;
                    EditorPrefs.SetBool("NovellaGraph_AutoSave", _autoSave);
                    autoSaveBtn.text = autoSaveOn
                        ? "✓  " + ToolLang.Get("Auto-save", "Автосохранение")
                        : "○  " + ToolLang.Get("Auto-save", "Автосохранение");
                    setAutoSave?.Invoke(autoSaveOn);
                })
                { text = (autoSaveOn ? "✓  " : "○  ") + ToolLang.Get("Auto-save", "Автосохранение"),
                  tooltip = ToolLang.Get(
                      "When ON — graph saves automatically 0.5s after the last change.",
                      "Когда ВКЛ — граф сохраняется автоматически через 0.5с после правки.") };
                setAutoSave = NovellaGraphTheme.ApplyToggleButton(autoSaveBtn, autoSaveOn);
                toolbarContainer.Add(autoSaveBtn);

                toolbarContainer.Add(NovellaGraphTheme.CreateVerticalSeparator());

                // ─── Auto-Layout (slim, не accent — это не CTA) ───
                var autoLayoutBtn = new Button(() => {
                    if (_graphView != null)
                    {
                        _graphView.AutoLayout();
                        MarkUnsaved();
                        _graphView.FrameSelection();
                    }
                })
                { text = "✨  " + ToolLang.Get("Auto-layout", "Выровнять") };
                NovellaGraphTheme.ApplySlimButton(autoLayoutBtn);
                toolbarContainer.Add(autoLayoutBtn);
            }

            // ─── Palette toggle-button — показать/скрыть нижнюю палитру нод ───
            toolbarContainer.Add(NovellaGraphTheme.CreateVerticalSeparator());
            bool palOn = _isPaletteOpen;
            System.Action<bool> setPal = null;
            _palToggleBtn = new Button(() => {
                palOn = !palOn;
                _isPaletteOpen = palOn;
                EditorPrefs.SetBool("NovellaGraph_PaletteOpen", palOn);
                if (_nodePalette != null)
                {
                    // Анимируем height вместо display:None — display не
                    // транзишит, а height даёт плавное «сворачивание».
                    _nodePalette.SetCollapsed(!palOn);
                }
                setPal?.Invoke(palOn);
            })
            { text = "🧰  " + ToolLang.Get("Nodes", "Ноды"),
              tooltip = ToolLang.Get(
                  "Show / hide the bottom node palette",
                  "Показать / скрыть нижнюю панель нод") };
            setPal = NovellaGraphTheme.ApplyToggleButton(_palToggleBtn, palOn);
            toolbarContainer.Add(_palToggleBtn);

            // ─── Map toggle-button (вместо Unity Toggle) ───
            bool mapOn = true;
            Button mapBtn = null;
            System.Action<bool> setMap = null;
            mapBtn = new Button(() => {
                mapOn = !mapOn;
                // Toggle нашей кастомной мини-карты (Block 2D).
                if (_graphView != null && _graphView.CustomMiniMap != null)
                {
                    _graphView.CustomMiniMap.style.display = mapOn ? DisplayStyle.Flex : DisplayStyle.None;
                }
                setMap?.Invoke(mapOn);
            })
            { text = "🗺  " + ToolLang.Get("Map", "Карта"),
              tooltip = ToolLang.Get("Show / hide minimap", "Показать / спрятать миникарту") };
            setMap = NovellaGraphTheme.ApplyToggleButton(mapBtn, mapOn);
            toolbarContainer.Add(mapBtn);

            // ─── Spacer ───
            var spacer = new VisualElement { style = { flexGrow = 1 } };
            toolbarContainer.Add(spacer);

            // ─── Word count (метаданные, компактно справа) ───
            (int words, double readingMinutes) stats = _currentTree != null
                ? _currentTree.GetWordStats()
                : (0, 0.0);
            string statsText = stats.words > 0
                ? string.Format(ToolLang.Get(
                    "📖 {0} words · ~{1} min", "📖 {0} слов · ~{1} мин"),
                    stats.words.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                    System.Math.Max(1, (int)System.Math.Round(stats.readingMinutes)))
                : ToolLang.Get("📖 (empty)", "📖 (пусто)");
            var statsLbl = new Label(statsText)
            {
                tooltip = ToolLang.Get(
                    "Word count and approximate reading time across this chapter (max across languages, ~200 wpm).",
                    "Слова и примерное время чтения по главе (берётся максимум среди языков, ~200 слов/мин)."),
                style = {
                    color = NovellaGraphTheme.Text3,
                    marginRight = 8, marginLeft = 6,
                    alignSelf = Align.Center,
                    fontSize = 11,
                }
            };
            toolbarContainer.Add(statsLbl);

            toolbarContainer.Add(NovellaGraphTheme.CreateVerticalSeparator());

            // ─── Preview language popup-button (вместо DropdownField) ───
            var previewLangLbl = new Label(ToolLang.Get("Preview:", "Превью:"))
            {
                style = {
                    marginLeft = 4, marginRight = 4,
                    color = NovellaGraphTheme.Text3,
                    fontSize = 11,
                    alignSelf = Align.Center,
                }
            };
            toolbarContainer.Add(previewLangLbl);

            Button previewLangBtn = null;
            previewLangBtn = new Button(() => {
                // GenericMenu в screen-coords под кнопкой.
                var menu = new GenericMenu();
                foreach (var lang in _locSettings.Languages)
                {
                    var capLang = lang;
                    menu.AddItem(new GUIContent(lang), lang == PreviewLanguage, () => {
                        PreviewLanguage = capLang;
                        EditorPrefs.SetString("NovellaGraph_PreviewLang", PreviewLanguage);
                        previewLangBtn.text = "🌐  " + capLang + "  ▾";
                        _inspectorContainer.MarkDirtyRepaint();
                        if (_graphView != null) _graphView.Query<NovellaNodeView>().ForEach(nv => nv.RefreshVisuals());
                    });
                }
                // worldBound даёт screen-coords относительно окна; DropDown(rect)
                // позиционирует меню под кнопкой.
                menu.DropDown(previewLangBtn.worldBound);
            })
            { text = "🌐  " + PreviewLanguage + "  ▾",
              tooltip = ToolLang.Get(
                  "Pick the language to preview dialogues in.",
                  "Выбрать язык для предпросмотра диалогов.") };
            NovellaGraphTheme.ApplyPopupButton(previewLangBtn, height: 26, paddingX: 10);
            previewLangBtn.style.minWidth = 70;
            toolbarContainer.Add(previewLangBtn);

            // ─── UI lang toggle (RU ↔ EN, без меню — он бинарный) ───
            // Эмодзи флагов 🇷🇺/🇬🇧 = composite regional indicator символы,
            // дефолтный Unity-шрифт их не рендерит и юзер видит «ru RU».
            // Поэтому идём чистым bold-текстом с увеличенным шрифтом.
            var langButton = new Button(() => { ToolLang.Toggle(); ConstructGraph(); })
            { text = ToolLang.IsRU ? "RU" : "EN",
              tooltip = ToolLang.Get(
                  "Toggle Studio interface language (Ctrl+L).",
                  "Переключить язык интерфейса Студии (Ctrl+L).") };
            NovellaGraphTheme.ApplySlimButton(langButton, height: 26, paddingX: 14);
            langButton.style.minWidth = 52;
            langButton.style.fontSize = 13;            // крупнее остальных slim-кнопок
            langButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            // Active-стиль: акцентная рамка чуть ярче, чтобы кнопка читалась
            // как «индикатор текущего языка», а не просто кнопка.
            langButton.style.borderTopColor = NovellaGraphTheme.Accent;
            langButton.style.borderBottomColor = NovellaGraphTheme.Accent;
            langButton.style.borderLeftColor = NovellaGraphTheme.Accent;
            langButton.style.borderRightColor = NovellaGraphTheme.Accent;
            langButton.style.color = NovellaGraphTheme.Accent;
            toolbarContainer.Add(langButton);

            toolbarContainer.Add(NovellaGraphTheme.CreateVerticalSeparator());

            // ─── Inspector toggle ───
            var toggleInspectorBtn = new Button(() => {
                _isInspectorOpen = !_isInspectorOpen;
                _rightPanel.style.width = _isInspectorOpen ? 550 : 0;
            })
            { text = "📋  " + ToolLang.Get("Inspector", "Инспектор") };
            NovellaGraphTheme.ApplySlimButton(toggleInspectorBtn);
            toolbarContainer.Add(toggleInspectorBtn);

            // ─── Minimize button (icon-only 26x26 in slim style) ───
            var minimize = new Button(() => MinimizeToLauncher());
            minimize.text = "─"; // не emoji, чтобы выглядел в стиле Hub'овского
            NovellaGraphTheme.ApplyIconButton(minimize);
            minimize.style.marginLeft = 6;
            minimize.style.color = NovellaGraphTheme.Text2;
            minimize.style.fontSize = 13;
            minimize.style.unityFontStyleAndWeight = FontStyle.Bold;
            minimize.tooltip = ToolLang.Get("Minimize window", "Свернуть окно");
            toolbarContainer.Add(minimize);

            container.Add(toolbarContainer);
        }

        private void MinimizeToLauncher()
        {
            Close();
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
            // Save: зелёная при dirty, серая-неактивная когда чисто.
            // Логика — в NovellaGraphTheme.SetSaveButtonState (единый источник).
            NovellaGraphTheme.SetSaveButtonState(_saveButton, _hasUnsavedChanges);
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
