using NovellaEngine.Data;
using NovellaEngine.Runtime;
using NovellaEngine.Runtime.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.Events;

namespace NovellaEngine.Editor
{
    public class NovellaUIEditorModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("UI Forge", "Кузница UI");
        public string ModuleIcon => "🎨";

        private EditorWindow _window;

        private NovellaPlayer _player;
        private StoryLauncher _launcher;
        private Camera _camera;
        private RenderTexture _previewTexture;
        private Vector2 _scrollPos;

        private int _currentTab = 0;
        private string[] _tabs;

        private bool _isMobileMode = false;
        private GameObject _stylerPrefab;

        private RectTransform _activeEditRect;
        private RectTransform _activeDecoRect;

        private GameObject _tempChoicePreview;
        private GameObject _tempStoryPreview;
        private List<GameObject> _tempDummyButtons = new List<GameObject>();

        private bool _showBounds = true;
        private float _uiZoom = 1f;

        private string _customPrefabsDir = "Assets/NovellaEngine/Runtime/Prefabs/CustomUI";
        private GameObject _selectedCustomPrefab;
        private GameObject _tempCustomPreview;
        private int _customPrefabIndex = 0;

        private int _newPrefabTypeIndex = 0;
        private string _newPrefabName = "NewCustomFrame";
        private bool _customPrefabFoldout = true;

        private RectTransform _renamingDecoRect;
        private string _renamingDecoName;

        private int _editingGraphIndex = -1;
        private string _tempGraphName = "";

        private int _editingStoryIndex = -1;
        private string _tempStoryName = "";

        [MenuItem("Tools/Novella Engine/🎨 UI Master Forge", false, 2)]
        public static void ShowWindow()
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.SwitchToModule(3);
        }

        public static void OpenWithCustomPrefab(GameObject targetPrefab)
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null)
            {
                NovellaHubWindow.Instance.SwitchToModule(3);
                var win = NovellaHubWindow.Instance.GetModule(3) as NovellaUIEditorModule;
                if (win != null)
                {
                    win._currentTab = 3;
                    win._selectedCustomPrefab = targetPrefab;
                    win.CleanTempPrefabs();
                    win._activeEditRect = null;
                    win._activeDecoRect = null;
                    win.FindReferences();
                }
            }
        }

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
            _tabs = new string[] { "🎮 " + ToolLang.Get("Game UI", "Игровой UI"), "📱 " + ToolLang.Get("Menu UI", "Меню UI"), "🎨 " + ToolLang.Get("Prefab Styler", "Стилизация"), "🛠 " + ToolLang.Get("Creator", "Создание") };

            if (!AssetDatabase.IsValidFolder(_customPrefabsDir)) Directory.CreateDirectory(_customPrefabsDir);

            EditorApplication.delayCall += () => {
                FindReferences();
                _window?.Repaint();
            };

            EditorApplication.hierarchyChanged += OnHierarchyChange;
            EditorApplication.update += OnEditorUpdate;
        }

        public void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChange;
            EditorApplication.update -= OnEditorUpdate;
            CleanTempPrefabs();
            ClearDummyButtons();
            if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); }
        }

        private void OnHierarchyChange()
        {
            FindReferences();
            _window?.Repaint();
        }

        private void OnEditorUpdate() { _window?.Repaint(); }

        private void FindReferences()
        {
            _camera = Camera.main;
            if (_camera == null) _camera = UnityEngine.Object.FindFirstObjectByType<Camera>();

            _player = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
            _launcher = UnityEngine.Object.FindFirstObjectByType<StoryLauncher>();

            Canvas c = null;
            if (_player != null && _player.DialoguePanel != null) c = _player.DialoguePanel.GetComponentInParent<Canvas>(true);
            else if (_launcher != null && _launcher.StoriesContainer != null) c = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);

            if (c != null && _camera != null)
            {
                Canvas rCanvas = c.rootCanvas != null ? c.rootCanvas : c;
                rCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                rCanvas.worldCamera = _camera;
                rCanvas.planeDistance = 5f;
                rCanvas.sortingOrder = 100;

                Transform bgTransform = rCanvas.transform.Find("Background");
                if (bgTransform != null)
                {
                    Canvas bgCanvas = bgTransform.GetComponent<Canvas>();
                    if (bgCanvas == null)
                    {
                        bgCanvas = bgTransform.gameObject.AddComponent<Canvas>();
                        bgTransform.gameObject.AddComponent<GraphicRaycaster>();
                    }
                    bgCanvas.overrideSorting = true;
                    bgCanvas.sortingOrder = -100;
                }

                EditorUtility.SetDirty(rCanvas);
            }

            if (_launcher != null && _launcher.StoriesContainer != null)
            {
                bool isDirty = false;
                Canvas contCanvas = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);

                if (contCanvas != null)
                {
                    Canvas rootCanvas = contCanvas.rootCanvas != null ? contCanvas.rootCanvas : contCanvas;
                    var allTransforms = rootCanvas.GetComponentsInChildren<Transform>(true);

                    if (_launcher.MainMenuPanel == null)
                    {
                        var p = allTransforms.FirstOrDefault(t => t.name.Contains("MainMenu") || t.name.Contains("ButtonsList"));
                        if (p != null) { _launcher.MainMenuPanel = p.gameObject; isDirty = true; }
                    }

                    if (_launcher.StoriesPanel == null)
                    {
                        _launcher.StoriesPanel = _launcher.StoriesContainer.gameObject;
                        isDirty = true;
                    }

                    if (_launcher.MCCreationPanel == null)
                    {
                        var mcPanel = allTransforms.FirstOrDefault(t => t.name == "MCCreationPanel");
                        if (mcPanel != null)
                        {
                            _launcher.MCCreationPanel = mcPanel.gameObject;
                            _launcher.MCNameInput = mcPanel.GetComponentInChildren<TMP_InputField>(true);
                            var btns = mcPanel.GetComponentsInChildren<Button>(true);
                            _launcher.MCConfirmButton = btns.FirstOrDefault(b => b.name.Contains("Confirm") || b.name.Contains("Готово"));

                            _launcher.MCAvatarPreview = mcPanel.Find("AvatarPreview")?.GetComponent<Image>();
                            isDirty = true;
                        }
                    }
                }
                if (isDirty) EditorUtility.SetDirty(_launcher);
            }
        }

        public void DrawGUI(Rect position)
        {
            NovellaTutorialManager.BlockBackgroundEvents(_window);

            if (_player == null && _launcher == null)
            {
                GUILayout.Space(20);
                EditorGUILayout.HelpBox(ToolLang.Get("UI Elements not found! Please use Scene Manager to setup the scene first.", "Элементы интерфейса не найдены! Используйте Менеджер Сцен для настройки."), MessageType.Warning);
                GUILayout.Space(10);
                if (GUILayout.Button(ToolLang.Get("Find References Again", "Найти ссылки заново"), GUILayout.Height(30))) FindReferences();
                return;
            }

            ManageTempPreviews();

            GUILayout.BeginHorizontal();
            DrawSettingsPanel();
            DrawPreviewPanel();
            GUILayout.EndHorizontal();

            NovellaTutorialManager.DrawOverlay(_window);
        }

        private string TranslateWardrobeElementName(string name)
        {
            if (name == "WardrobePanel") return ToolLang.Get("Wardrobe Base", "Подложка Гардероба");
            if (name == "TitleText") return ToolLang.Get("Title Text", "Текст Заголовка");
            if (name == "Btn_CloseWardrobe") return ToolLang.Get("Close Button", "Кнопка 'Закрыть'");
            if (name == "AvatarMaskContainer") return ToolLang.Get("Avatar Container", "Контейнер Аватара");
            if (name == "CharacterNameText") return ToolLang.Get("Character Name", "Имя Персонажа");
            if (name == "BottomWardrobePanel") return ToolLang.Get("Bottom Panel", "Нижняя Панель (Слоты)");
            if (name == "TabsPanel") return ToolLang.Get("Categories Tabs", "Вкладки Категорий");
            if (name == "ItemsScroll") return ToolLang.Get("Items Scroll View", "Прокрутка Предметов");
            if (name == "Viewport") return ToolLang.Get("Viewport", "Область видимости");
            if (name == "Content") return ToolLang.Get("Content (Grid)", "Сетка предметов (Content)");
            return name;
        }

        private void ManageTempPreviews()
        {
            if (_currentTab != 0 && _currentTab != 2) { if (_tempChoicePreview) UnityEngine.Object.DestroyImmediate(_tempChoicePreview); }
            if (_currentTab != 1 && _currentTab != 2) { if (_tempStoryPreview) UnityEngine.Object.DestroyImmediate(_tempStoryPreview); }
            if (_currentTab != 3) { if (_tempCustomPreview) UnityEngine.Object.DestroyImmediate(_tempCustomPreview); }

            bool isEditingChoiceContainer = (_currentTab == 0 && _player != null && _activeEditRect == _player.ChoiceContainer?.GetComponent<RectTransform>());

            if (isEditingChoiceContainer)
            {
                if (_tempDummyButtons.Count == 0 && _player.ChoiceButtonPrefab != null && _player.ChoiceContainer != null)
                {
                    for (int i = 0; i < 3; i++) _tempDummyButtons.Add(PrefabUtility.InstantiatePrefab(_player.ChoiceButtonPrefab, _player.ChoiceContainer) as GameObject);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_player.ChoiceContainer.GetComponent<RectTransform>());
                    Canvas.ForceUpdateCanvases();
                }
            }
            else ClearDummyButtons();
        }

        private void CleanTempPrefabs()
        {
            if (_tempChoicePreview != null) UnityEngine.Object.DestroyImmediate(_tempChoicePreview);
            if (_tempStoryPreview != null) UnityEngine.Object.DestroyImmediate(_tempStoryPreview);
            if (_tempCustomPreview != null) UnityEngine.Object.DestroyImmediate(_tempCustomPreview);
        }

        private void ClearDummyButtons()
        {
            foreach (var btn in _tempDummyButtons) if (btn != null) UnityEngine.Object.DestroyImmediate(btn);
            _tempDummyButtons.Clear();
        }

        private void DrawSectionHeader(string icon, string title)
        {
            GUILayout.Space(20);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            if (EditorGUIUtility.isProSkin) headerStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            else headerStyle.normal.textColor = new Color(0.2f, 0.4f, 0.6f);
            GUILayout.Label($"{icon} {title}", headerStyle);
            GUILayout.Space(5);
        }

        private void DrawSettingsPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(650), GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            _currentTab = GUILayout.Toolbar(_currentTab, _tabs, GUILayout.Height(35));
            if (EditorGUI.EndChangeCheck()) { _activeEditRect = null; _activeDecoRect = null; }

            GUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            if (_currentTab == 0) DrawGameplayUITab();
            else if (_currentTab == 1) DrawMenuUITab();
            else if (_currentTab == 2) DrawPrefabsTab();
            else if (_currentTab == 3) DrawCustomUITab();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawMCCreationSection(Canvas canvas)
        {
            if (canvas == null) return;

            Transform rootT = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            GUILayout.Space(15);
            DrawSectionHeader("👤", ToolLang.Get("Main Character Creator", "Создание Главного Героя (Меню)"));

            Transform mcPanelTransform = rootT.Find("MCCreationPanel");

            if (mcPanelTransform == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Create a panel to allow players to interact with the MC before starting the game.", "Создайте панель, чтобы игроки могли выбрать и назвать ГГ перед началом игры."), MessageType.Info);
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                if (GUILayout.Button("🛠 " + ToolLang.Get("Generate MC Panel", "Сгенерировать панель создания"), GUILayout.Height(35)))
                {
                    GenerateMCCreationPanel(rootT);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                var mcUI = mcPanelTransform.GetComponent<NovellaMCCreationUI>();
                if (mcUI != null)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label("👥 " + ToolLang.Get("Available Characters:", "Доступные для выбора герои:"), EditorStyles.boldLabel);

                    for (int i = 0; i < mcUI.AvailableCharacters.Count; i++)
                    {
                        GUILayout.BeginHorizontal();
                        string mcName = mcUI.AvailableCharacters[i] != null ? mcUI.AvailableCharacters[i].name : ToolLang.Get("None (Click to select)", "Не выбран (Нажмите)");
                        string charDir = "Assets/NovellaEngine/Runtime/Data/Characters";

                        if (GUILayout.Button(mcName, EditorStyles.popup))
                        {
                            int idx = i;
                            if (!System.IO.Directory.Exists(charDir)) System.IO.Directory.CreateDirectory(charDir);
                            NovellaGalleryWindow.ShowWindow(obj => {
                                NovellaCharacter mc = obj as NovellaCharacter;
                                if (mc != null)
                                {
                                    if (!mc.IsPlayerCharacter)
                                    {
                                        EditorUtility.DisplayDialog(ToolLang.Get("Error", "Ошибка"), ToolLang.Get("This character must have 'Is Main Character' enabled!", "У этого персонажа должна быть включена галочка 'Это Главный Герой (Игрок)'!"), "OK");
                                        return;
                                    }
                                    Undo.RecordObject(mcUI, "Assign MC");
                                    mcUI.AvailableCharacters[idx] = mc;
                                    EditorUtility.SetDirty(mcUI);
                                    _window?.Repaint();
                                }
                            }, NovellaGalleryWindow.EGalleryFilter.Character, charDir);
                        }

                        if (mcUI.AvailableCharacters[i] != null)
                        {
                            GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                            if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(25)))
                            {
                                NovellaCharacterEditorModule.OpenWithCharacter(mcUI.AvailableCharacters[i]);
                            }
                            GUI.backgroundColor = Color.white;
                        }

                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("✖", GUILayout.Width(25)))
                        {
                            Undo.RecordObject(mcUI, "Remove MC Asset");
                            mcUI.AvailableCharacters.RemoveAt(i);
                            EditorUtility.SetDirty(mcUI);
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.Space(5);
                    GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                    if (GUILayout.Button("+ " + ToolLang.Get("Add Character Slot", "Добавить слот персонажа"), EditorStyles.miniButton))
                    {
                        Undo.RecordObject(mcUI, "Add Slot");
                        mcUI.AvailableCharacters.Add(null);
                        EditorUtility.SetDirty(mcUI);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndVertical();
                    GUILayout.Space(10);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("👁 " + ToolLang.Get("Panel Visibility", "Видимость панели"), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                bool isPanelActive = GUILayout.Toggle(mcPanelTransform.gameObject.activeSelf, ToolLang.Get("Show in Scene", "Показать на сцене"), EditorStyles.toolbarButton, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mcPanelTransform.gameObject, "Toggle MC Panel");
                    mcPanelTransform.gameObject.SetActive(isPanelActive);
                    EditorUtility.SetDirty(mcPanelTransform.gameObject);
                    if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);

                DrawRectTransformEditor(ToolLang.Get("Main Panel Base", "Главная подложка панели"), mcPanelTransform.GetComponent<RectTransform>());

                bool isEditingMC = _activeEditRect != null && (_activeEditRect == mcPanelTransform.GetComponent<RectTransform>() || _activeEditRect.IsChildOf(mcPanelTransform));

                if (isEditingMC)
                {
                    foreach (Transform child in mcPanelTransform)
                    {
                        var rt = child.GetComponent<RectTransform>();
                        var txt = child.GetComponent<TMP_Text>();

                        if (txt != null)
                        {
                            DrawTextEditor("↳ 📝 " + child.name, txt);
                        }
                        else if (rt != null)
                        {
                            DrawRectTransformEditor("↳ 🔲 " + child.name, rt);

                            bool isEditingChild = _activeEditRect != null && (_activeEditRect == rt || _activeEditRect.IsChildOf(rt));
                            if (isEditingChild)
                            {
                                var deepTexts = child.GetComponentsInChildren<TMP_Text>(true);
                                foreach (var dTxt in deepTexts)
                                {
                                    if (dTxt.gameObject != child.gameObject)
                                        DrawTextEditor("   ↳ 📝 " + dTxt.name, dTxt);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void GenerateMCCreationPanel(Transform rootTransform)
        {
            Undo.RegisterFullObjectHierarchyUndo(rootTransform.gameObject, "Generate MC Panel");

            GameObject panel = new GameObject("MCCreationPanel");
            panel.transform.SetParent(rootTransform, false);
            panel.transform.SetAsLastSibling();

            var canvas = panel.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
            panel.AddComponent<GraphicRaycaster>();

            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            GameObject avatarMaskObj = new GameObject("AvatarMaskContainer");
            avatarMaskObj.transform.SetParent(panel.transform, false);
            var maskRt = avatarMaskObj.AddComponent<RectTransform>();
            maskRt.anchorMin = new Vector2(0.5f, 0f); maskRt.anchorMax = new Vector2(0.5f, 0f);
            maskRt.pivot = new Vector2(0.5f, 0f);
            maskRt.anchoredPosition = new Vector2(0, 50);
            maskRt.sizeDelta = new Vector2(600, 850);

            var maskImg = avatarMaskObj.AddComponent<Image>();
            maskImg.color = new Color(1, 1, 1, 0.05f);
            avatarMaskObj.AddComponent<RectMask2D>();

            GameObject leftArrow = new GameObject("Btn_PrevChar");
            leftArrow.transform.SetParent(panel.transform, false);
            var laRt = leftArrow.AddComponent<RectTransform>();
            laRt.anchorMin = new Vector2(0.5f, 0f); laRt.anchorMax = new Vector2(0.5f, 0f);
            laRt.pivot = new Vector2(0.5f, 0.5f);
            laRt.anchoredPosition = new Vector2(-380, 50 + (850 / 2f));
            laRt.sizeDelta = new Vector2(80, 80);
            leftArrow.AddComponent<Image>().color = new Color(1f, 0.6f, 0f, 1f);
            var btnPrev = leftArrow.AddComponent<Button>();
            var laTxt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            laTxt.transform.SetParent(leftArrow.transform, false);
            laTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero; laTxt.GetComponent<RectTransform>().anchorMax = Vector2.one; laTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero; laTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            laTxt.text = "<"; laTxt.fontSize = 50; laTxt.alignment = TextAlignmentOptions.Center;

            GameObject rightArrow = new GameObject("Btn_NextChar");
            rightArrow.transform.SetParent(panel.transform, false);
            var raRt = rightArrow.AddComponent<RectTransform>();
            raRt.anchorMin = new Vector2(0.5f, 0f); raRt.anchorMax = new Vector2(0.5f, 0f);
            raRt.pivot = new Vector2(0.5f, 0.5f);
            raRt.anchoredPosition = new Vector2(380, 50 + (850 / 2f));
            raRt.sizeDelta = new Vector2(80, 80);
            rightArrow.AddComponent<Image>().color = new Color(1f, 0.6f, 0f, 1f);
            var btnNext = rightArrow.AddComponent<Button>();
            var raTxt = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            raTxt.transform.SetParent(rightArrow.transform, false);
            raTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero; raTxt.GetComponent<RectTransform>().anchorMax = Vector2.one; raTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero; raTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            raTxt.text = ">"; raTxt.fontSize = 50; raTxt.alignment = TextAlignmentOptions.Center;

            GameObject inputBg = new GameObject("NameInput");
            inputBg.transform.SetParent(panel.transform, false);
            var inRt = inputBg.AddComponent<RectTransform>();
            inRt.anchorMin = new Vector2(0f, 0f); inRt.anchorMax = new Vector2(0f, 0f);
            inRt.pivot = new Vector2(0f, 0f);
            inRt.anchoredPosition = new Vector2(50, 50);
            inRt.sizeDelta = new Vector2(400, 80);
            inputBg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

            GameObject textAreaObj = new GameObject("TextArea"); textAreaObj.transform.SetParent(inputBg.transform, false);
            var taRt = textAreaObj.AddComponent<RectTransform>(); taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one; taRt.offsetMin = new Vector2(15, 0); taRt.offsetMax = new Vector2(-15, 0); textAreaObj.AddComponent<RectMask2D>();
            GameObject inputText = new GameObject("Text"); inputText.transform.SetParent(textAreaObj.transform, false);
            var txtRt = inputText.AddComponent<RectTransform>(); txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one; txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
            var inputTMP = inputText.AddComponent<TextMeshProUGUI>(); inputTMP.fontSize = 40; inputTMP.alignment = TextAlignmentOptions.Center; inputTMP.textWrappingMode = TextWrappingModes.NoWrap;
            GameObject placeholderText = new GameObject("Placeholder"); placeholderText.transform.SetParent(textAreaObj.transform, false);
            var phRt = placeholderText.AddComponent<RectTransform>(); phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one; phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;
            var phTMP = placeholderText.AddComponent<TextMeshProUGUI>(); phTMP.text = ToolLang.Get("Alex...", "Алекс..."); phTMP.fontSize = 40; phTMP.alignment = TextAlignmentOptions.Center; phTMP.color = new Color(1, 1, 1, 0.3f); phTMP.textWrappingMode = TextWrappingModes.NoWrap;
            var inputField = inputBg.AddComponent<TMP_InputField>(); inputField.textComponent = inputTMP; inputField.placeholder = phTMP; inputField.characterLimit = 25;

            GameObject hintObj = new GameObject("HintText");
            hintObj.transform.SetParent(panel.transform, false);
            var hintRt = hintObj.AddComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f); hintRt.anchorMax = new Vector2(0f, 0f);
            hintRt.pivot = new Vector2(0f, 0f);
            hintRt.anchoredPosition = new Vector2(50, 140);
            hintRt.sizeDelta = new Vector2(400, 50);
            var hintTxt = hintObj.AddComponent<TextMeshProUGUI>();
            hintTxt.text = ToolLang.Get("Enter character name:", "Введите имя персонажа:");
            hintTxt.fontSize = 32; hintTxt.alignment = TextAlignmentOptions.Center;
            hintTxt.color = new Color(0.7f, 0.7f, 0.7f);

            GameObject btnObj = new GameObject("Btn_ConfirmMC");
            btnObj.transform.SetParent(panel.transform, false);
            var brt = btnObj.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(1f, 0f); brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.anchoredPosition = new Vector2(-50, 50);
            brt.sizeDelta = new Vector2(350, 80);
            btnObj.AddComponent<Image>().color = new Color(0.2f, 0.8f, 0.4f, 1f);
            btnObj.AddComponent<Button>();

            GameObject btnTxt = new GameObject("Text"); btnTxt.transform.SetParent(btnObj.transform, false);
            var bTrt = btnTxt.AddComponent<RectTransform>(); bTrt.anchorMin = Vector2.zero; bTrt.anchorMax = Vector2.one; bTrt.offsetMin = Vector2.zero; bTrt.offsetMax = Vector2.zero;
            var bTmp = btnTxt.AddComponent<TextMeshProUGUI>();
            bTmp.text = ToolLang.Get("Start Story", "Начать Историю");
            bTmp.fontSize = 40; bTmp.alignment = TextAlignmentOptions.Center;

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(panel.transform, false);
            var titleRt = titleObj.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f); titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -50);
            titleRt.sizeDelta = new Vector2(800, 100);
            var titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
            titleTxt.text = ToolLang.Get("Create Your Character", "Создание Персонажа");
            titleTxt.fontSize = 60; titleTxt.alignment = TextAlignmentOptions.Center;

            var mcUI = panel.AddComponent<NovellaMCCreationUI>();
            mcUI.AvatarMaskContainer = avatarMaskObj.transform;
            mcUI.PrevCharBtn = btnPrev;
            mcUI.NextCharBtn = btnNext;

            if (_launcher != null && _launcher.MainCharacterAsset != null)
            {
                mcUI.AvailableCharacters.Add(_launcher.MainCharacterAsset);
            }

            panel.SetActive(false);
            EditorUtility.SetDirty(rootTransform.gameObject);
            FindReferences();
            GUIUtility.ExitGUI();
        }

        private void DrawWardrobeSection(Canvas canvas)
        {
            if (canvas == null) return;

            var settings = NovellaEngine.Data.NovellaDLCSettings.Instance;
            if (settings == null || !settings.IsDLCEnabled("NovellaEngine.DLC.Wardrobe.WardrobeNodeData"))
            {
                return;
            }

            Transform rootT = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            GUILayout.Space(15);
            DrawSectionHeader("👗", ToolLang.Get("Wardrobe UI (DLC)", "Интерфейс Гардероба (DLC)"));

            Transform wardrobeTransform = rootT.Find("WardrobePanel");

            if (wardrobeTransform == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Create a Wardrobe Panel to allow players to change outfits during the game.", "Создайте панель Гардероба, чтобы игроки могли менять внешний вид персонажа во время игры."), MessageType.Info);
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                if (GUILayout.Button("🛠 " + ToolLang.Get("Generate Wardrobe Panel", "Сгенерировать панель Гардероба"), GUILayout.Height(35)))
                {
                    GenerateWardrobePanel(rootT);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("👁 " + ToolLang.Get("Panel Visibility", "Видимость панели"), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                bool isPanelActive = GUILayout.Toggle(wardrobeTransform.gameObject.activeSelf, ToolLang.Get("Show in Scene", "Показать на сцене"), EditorStyles.toolbarButton, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wardrobeTransform.gameObject, "Toggle Wardrobe Panel");
                    wardrobeTransform.gameObject.SetActive(isPanelActive);
                    EditorUtility.SetDirty(wardrobeTransform.gameObject);
                    if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);

                DrawMinimalBackgroundEditor(wardrobeTransform.gameObject, ToolLang.Get("Wardrobe Background", "Фон Гардероба"));
                DrawRectTransformEditor(ToolLang.Get("Wardrobe Base", "Подложка Гардероба (Контейнер)"), wardrobeTransform.GetComponent<RectTransform>());

                var uiScript = wardrobeTransform.GetComponent<NovellaEngine.Runtime.UI.NovellaWardrobeUI>();
                if (uiScript != null)
                {
                    GUILayout.Space(10);
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label("📦 " + ToolLang.Get("Dynamic Prefabs", "Динамические префабы"), EditorStyles.boldLabel);

                    DrawPrefabManager(ToolLang.Get("Item Slot", "Слот предмета"), uiScript.ItemSlotPrefab, obj => uiScript.ItemSlotPrefab = obj, uiScript, "Assets/NovellaEngine/DLC/Wardrobe/Prefabs");
                    GUILayout.EndVertical();

                    GUILayout.Space(10);
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label("🏷 " + ToolLang.Get("Free Category Tabs", "Свободные Вкладки (Категории)"), EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(ToolLang.Get("Any button named 'Tab_LayerName' (e.g. 'Tab_Hair') acts as a tab. You can place them ANYWHERE on the screen!", "Любая кнопка с именем 'Tab_ИмяСлоя' работает как вкладка. Ставьте их в ЛЮБОЕ место на экране!"), MessageType.Info);

                    var tabButtons = wardrobeTransform.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Tab_")).ToList();
                    foreach (var tb in tabButtons)
                    {
                        DrawRectTransformEditor("↳ " + tb.name, tb.GetComponent<RectTransform>(), false, true);
                        if (_activeEditRect == tb.GetComponent<RectTransform>())
                        {
                            var txt = tb.GetComponentInChildren<TMP_Text>(true);
                            if (txt != null) DrawTextEditor("   ↳ 📝 " + txt.name, txt);
                        }
                    }

                    GUILayout.Space(5);
                    GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                    if (GUILayout.Button("+ " + ToolLang.Get("Create New Tab", "Создать новую вкладку"), EditorStyles.miniButton, GUILayout.Height(25)))
                    {
                        GameObject newTab = new GameObject("Tab_NewLayer");
                        Transform parent = uiScript.CategoriesPanel != null ? uiScript.CategoriesPanel : wardrobeTransform;
                        newTab.transform.SetParent(parent, false);
                        var rt = newTab.AddComponent<RectTransform>();
                        rt.sizeDelta = new Vector2(250, 70);
                        newTab.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                        newTab.AddComponent<Button>();

                        GameObject tText = new GameObject("Text");
                        tText.transform.SetParent(newTab.transform, false);
                        var ttxtRt = tText.AddComponent<RectTransform>();
                        ttxtRt.anchorMin = Vector2.zero; ttxtRt.anchorMax = Vector2.one;
                        ttxtRt.offsetMin = Vector2.zero; ttxtRt.offsetMax = Vector2.zero;
                        var tTxtComp = tText.AddComponent<TextMeshProUGUI>();
                        tTxtComp.text = "NewLayer";
                        tTxtComp.fontSize = 32;
                        tTxtComp.alignment = TextAlignmentOptions.Center;

                        Undo.RegisterCreatedObjectUndo(newTab, "Add Tab");
                        _activeEditRect = rt;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndVertical();
                }

                bool isEditingWardrobe = _activeEditRect != null && (_activeEditRect == wardrobeTransform.GetComponent<RectTransform>() || _activeEditRect.IsChildOf(wardrobeTransform));

                if (isEditingWardrobe)
                {
                    foreach (Transform child in wardrobeTransform)
                    {
                        var rt = child.GetComponent<RectTransform>();
                        var txt = child.GetComponent<TMP_Text>();

                        if (txt != null)
                        {
                            DrawTextEditor("↳ 📝 " + TranslateWardrobeElementName(child.name), txt);
                        }
                        else if (rt != null && !rt.name.StartsWith("Tab_"))
                        {
                            DrawRectTransformEditor("↳ 🔲 " + TranslateWardrobeElementName(child.name), rt);

                            bool isEditingChild = _activeEditRect != null && (_activeEditRect == rt || _activeEditRect.IsChildOf(rt));
                            if (isEditingChild)
                            {
                                var deepTexts = child.GetComponentsInChildren<TMP_Text>(true);
                                foreach (var dTxt in deepTexts)
                                {
                                    if (dTxt.gameObject != child.gameObject)
                                        DrawTextEditor("   ↳ 📝 " + TranslateWardrobeElementName(dTxt.name), dTxt);
                                }
                            }
                        }
                    }
                }

                GUILayout.Space(15);
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUILayout.Button("🗃 " + ToolLang.Get("Open Wardrobe Database", "Открыть Базу Предметов Гардероба"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold }, GUILayout.Height(45)))
                {
                    System.Type type = System.AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.FullName == "NovellaEngine.DLC.Wardrobe.NovellaWardrobeDatabaseWindow");

                    if (type != null)
                    {
                        var method = type.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (method != null) { method.Invoke(null, null); }
                        else { Debug.LogError("[Novella DLC] Метод ShowWindow не найден в NovellaWardrobeDatabaseWindow!"); }
                    }
                    else { Debug.LogError("[Novella DLC] Скрипт NovellaWardrobeDatabaseWindow не найден в проекте."); }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10);
            }
        }
        private void GenerateWardrobePanel(Transform rootTransform)
        {
            Undo.RegisterFullObjectHierarchyUndo(rootTransform.gameObject, "Generate Wardrobe Panel");

            GameObject panel = new GameObject("WardrobePanel");
            panel.transform.SetParent(rootTransform, false);
            panel.transform.SetAsLastSibling();

            var canvas = panel.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
            panel.AddComponent<GraphicRaycaster>();

            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.98f);

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(panel.transform, false);
            var titleRt = titleObj.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(0f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(50, -50);
            titleRt.sizeDelta = new Vector2(400, 80);
            var titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
            titleTxt.text = ToolLang.Get("Wardrobe", "Гардероб");
            titleTxt.fontSize = 55; titleTxt.fontStyle = FontStyles.Bold; titleTxt.alignment = TextAlignmentOptions.Left;

            GameObject closeBtnObj = new GameObject("Btn_CloseWardrobe");
            closeBtnObj.transform.SetParent(panel.transform, false);
            var closeRt = closeBtnObj.AddComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f); closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-50, -50);
            closeRt.sizeDelta = new Vector2(80, 80);
            closeBtnObj.AddComponent<Image>().color = new Color(0.9f, 0.3f, 0.3f, 1f);
            var closeBtn = closeBtnObj.AddComponent<Button>();
            var closeTxtObj = new GameObject("Text"); closeTxtObj.transform.SetParent(closeBtnObj.transform, false);
            var ctRt = closeTxtObj.AddComponent<RectTransform>(); ctRt.anchorMin = Vector2.zero; ctRt.anchorMax = Vector2.one; ctRt.offsetMin = Vector2.zero; ctRt.offsetMax = Vector2.zero;
            var cTxt = closeTxtObj.AddComponent<TextMeshProUGUI>(); cTxt.text = "X"; cTxt.fontSize = 50; cTxt.alignment = TextAlignmentOptions.Center; cTxt.color = Color.white;

            GameObject bottomPanel = new GameObject("BottomWardrobePanel");
            bottomPanel.transform.SetParent(panel.transform, false);
            var botRt = bottomPanel.AddComponent<RectTransform>();
            botRt.anchorMin = new Vector2(0f, 0f); botRt.anchorMax = new Vector2(1f, 0f);
            botRt.pivot = new Vector2(0.5f, 0f);
            botRt.anchoredPosition = Vector2.zero;
            botRt.sizeDelta = new Vector2(0, 350);
            bottomPanel.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);

            GameObject catPanel = new GameObject("TabsPanel");
            catPanel.transform.SetParent(bottomPanel.transform, false);
            var catRt = catPanel.AddComponent<RectTransform>();
            catRt.anchorMin = new Vector2(0f, 1f); catRt.anchorMax = new Vector2(1f, 1f);
            catRt.pivot = new Vector2(0.5f, 1f);
            catRt.anchoredPosition = Vector2.zero;
            catRt.sizeDelta = new Vector2(0, 70);
            catPanel.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 1f);
            var hlg = catPanel.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter; hlg.spacing = 15;
            hlg.childControlHeight = false; hlg.childControlWidth = false;

            GameObject scrollObj = new GameObject("ItemsScroll");
            scrollObj.transform.SetParent(bottomPanel.transform, false);
            var scrollRt = scrollObj.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f); scrollRt.anchorMax = new Vector2(1f, 1f);
            scrollRt.offsetMin = new Vector2(20, 20); scrollRt.offsetMax = new Vector2(-20, -70);

            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollObj.transform, false);
            var vpRt = viewportObj.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            viewportObj.AddComponent<RectMask2D>();

            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform, false);
            var contentRt = contentObj.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 300);

            var glg = contentObj.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(200, 200);
            glg.spacing = new Vector2(20, 20);
            glg.padding = new RectOffset(20, 20, 20, 20);
            glg.childAlignment = TextAnchor.UpperCenter;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.MinSize;

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = vpRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;

            string prefabDir = "Assets/NovellaEngine/DLC/Wardrobe/Prefabs";
            if (!System.IO.Directory.Exists(prefabDir)) System.IO.Directory.CreateDirectory(prefabDir);

            GameObject tempSlot = new GameObject("WardrobeItemSlot");
            var tsRt = tempSlot.AddComponent<RectTransform>();
            tsRt.sizeDelta = new Vector2(200, 200);
            tempSlot.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            tempSlot.AddComponent<Button>();
            GameObject tIcon = new GameObject("Icon");
            tIcon.transform.SetParent(tempSlot.transform, false);
            var tiRt = tIcon.AddComponent<RectTransform>();
            tiRt.anchorMin = Vector2.zero; tiRt.anchorMax = Vector2.one;
            tiRt.offsetMin = new Vector2(10, 10); tiRt.offsetMax = new Vector2(-10, -10);
            tIcon.AddComponent<Image>().color = new Color(1, 1, 1, 0.5f);
            GameObject slotPrefab = PrefabUtility.SaveAsPrefabAsset(tempSlot, prefabDir + "/WardrobeItemSlot.prefab");
            UnityEngine.Object.DestroyImmediate(tempSlot);

            string[] cats = { "Hair", "Clothes", "Accessories" };
            foreach (var cat in cats)
            {
                GameObject newTab = new GameObject("Tab_" + cat);
                newTab.transform.SetParent(catPanel.transform, false);
                var rtTab = newTab.AddComponent<RectTransform>();
                rtTab.sizeDelta = new Vector2(250, 70);
                newTab.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                newTab.AddComponent<Button>();

                GameObject tText = new GameObject("Text");
                tText.transform.SetParent(newTab.transform, false);
                var ttxtRt = tText.AddComponent<RectTransform>();
                ttxtRt.anchorMin = Vector2.zero; ttxtRt.anchorMax = Vector2.one;
                ttxtRt.offsetMin = Vector2.zero; ttxtRt.offsetMax = Vector2.zero;
                var tTxtComp = tText.AddComponent<TextMeshProUGUI>();
                tTxtComp.text = cat;
                tTxtComp.fontSize = 32;
                tTxtComp.alignment = TextAlignmentOptions.Center;
            }

            GameObject avatarMaskObj = new GameObject("AvatarMaskContainer");
            avatarMaskObj.transform.SetParent(panel.transform, false);
            var maskRt = avatarMaskObj.AddComponent<RectTransform>();
            maskRt.anchorMin = new Vector2(0.5f, 0f); maskRt.anchorMax = new Vector2(0.5f, 1f);
            maskRt.pivot = new Vector2(0.5f, 0.5f);
            maskRt.offsetMin = new Vector2(-250, 370);
            maskRt.offsetMax = new Vector2(250, -50);
            var maskImg = avatarMaskObj.AddComponent<Image>();
            maskImg.color = new Color(1, 1, 1, 0.05f);
            avatarMaskObj.AddComponent<RectMask2D>();

            GameObject nameObj = new GameObject("CharacterNameText");
            nameObj.transform.SetParent(panel.transform, false);
            var nameRt = nameObj.AddComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0.5f, 0f); nameRt.anchorMax = new Vector2(0.5f, 0f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0, 360);
            nameRt.sizeDelta = new Vector2(500, 60);
            var nameFieldTxt = nameObj.AddComponent<TextMeshProUGUI>();
            nameFieldTxt.text = "Character Name";
            nameFieldTxt.fontSize = 45; nameFieldTxt.alignment = TextAlignmentOptions.Center;

            var uiScript = panel.AddComponent<NovellaEngine.Runtime.UI.NovellaWardrobeUI>();
            uiScript.AvatarMaskContainer = avatarMaskObj.transform;
            uiScript.CharacterNameText = nameFieldTxt;
            uiScript.CloseButton = closeBtn;
            uiScript.CategoriesPanel = catPanel.transform;
            uiScript.ItemsGrid = contentObj.transform;
            uiScript.ItemSlotPrefab = slotPrefab;

            panel.SetActive(true);

            EditorUtility.SetDirty(rootTransform.gameObject);
            FindReferences();
            GUIUtility.ExitGUI();
        }

        private void DrawLayoutContainerEditor(string title, RectTransform rect)
        {
            if (rect == null) return;

            DrawRectTransformEditor(title, rect);

            if (_activeEditRect == rect)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(ToolLang.Get("Layout Settings", "Настройки Расположения"), EditorStyles.miniBoldLabel);

                var vlg = rect.GetComponent<VerticalLayoutGroup>();
                var hlg = rect.GetComponent<HorizontalLayoutGroup>();
                int currentType = vlg != null ? 0 : (hlg != null ? 1 : -1);

                EditorGUI.BeginChangeCheck();
                int newType = EditorGUILayout.Popup(ToolLang.Get("Type", "Тип"), currentType, new string[] { ToolLang.Get("Vertical", "Вертикальный (Столбик)"), ToolLang.Get("Horizontal", "Горизонтальный (Строка)") });

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(rect.gameObject, "Change Layout Group");
                    float spacing = 20f; TextAnchor align = TextAnchor.MiddleCenter;

                    if (vlg != null) { spacing = vlg.spacing; align = vlg.childAlignment; UnityEngine.Object.DestroyImmediate(vlg); }
                    if (hlg != null) { spacing = hlg.spacing; align = hlg.childAlignment; UnityEngine.Object.DestroyImmediate(hlg); }

                    if (newType == 0)
                    {
                        vlg = rect.gameObject.AddComponent<VerticalLayoutGroup>();
                        vlg.spacing = spacing; vlg.childAlignment = align;
                        vlg.childControlHeight = false; vlg.childControlWidth = false;
                        vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = false;
                    }
                    else if (newType == 1)
                    {
                        hlg = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                        hlg.spacing = spacing; hlg.childAlignment = align;
                        hlg.childControlHeight = false; hlg.childControlWidth = false;
                        hlg.childForceExpandHeight = false; hlg.childForceExpandWidth = false;
                    }

                    var csf = rect.GetComponent<ContentSizeFitter>();
                    if (csf == null) csf = rect.gameObject.AddComponent<ContentSizeFitter>();
                    csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                    EditorUtility.SetDirty(rect.gameObject);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    Canvas.ForceUpdateCanvases();
                }

                LayoutGroup lg = rect.GetComponent<LayoutGroup>();
                if (lg != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float s = 0;
                    if (lg is VerticalLayoutGroup v) s = v.spacing;
                    if (lg is HorizontalLayoutGroup h) s = h.spacing;

                    s = EditorGUILayout.FloatField(ToolLang.Get("Spacing", "Отступ между элементами"), s);
                    TextAnchor al = (TextAnchor)EditorGUILayout.EnumPopup(ToolLang.Get("Alignment", "Выравнивание"), lg.childAlignment);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(lg, "Change Settings");
                        if (lg is VerticalLayoutGroup vv) { vv.spacing = s; vv.childAlignment = al; }
                        if (lg is HorizontalLayoutGroup hh) { hh.spacing = s; hh.childAlignment = al; }
                        EditorUtility.SetDirty(lg);

                        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                        Canvas.ForceUpdateCanvases();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }
        }

        private void DrawTextEditor(string title, TMP_Text txt, bool isPrefabMode = false, bool showDeleteBtn = false)
        {
            if (txt == null) return;
            DrawRectTransformEditor(title, txt.GetComponent<RectTransform>(), isPrefabMode, showDeleteBtn);

            if (_activeEditRect == txt.GetComponent<RectTransform>())
            {
                EditorGUI.BeginChangeCheck();
                DrawTMPTextSettingsBlock(txt, ToolLang.Get("Typography Settings", "Настройки Типографики"));
                if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(txt.gameObject); Canvas.ForceUpdateCanvases(); }
            }
        }

        private void DrawGameplayUITab()
        {
            if (_player == null) { EditorGUILayout.HelpBox(ToolLang.Get("This is not a Gameplay Scene.", "Это не игровая сцена."), MessageType.Info); return; }

            Canvas canvas = _player.DialoguePanel.GetComponentInParent<Canvas>(true);
            Canvas rootCanvas = (canvas != null && canvas.rootCanvas != null) ? canvas.rootCanvas : canvas;

            if (!EditorPrefs.GetBool("Novella_HideUIGuide_Game", false))
            {
                GUILayout.BeginVertical(new GUIStyle(EditorStyles.helpBox) { border = new RectOffset(4, 4, 4, 4) });
                GUILayout.Label("🎓 " + ToolLang.Get("How does Gameplay UI work?", "Как работает Игровой UI?"), EditorStyles.boldLabel);
                GUILayout.Label(ToolLang.Get(
                    "This tab configures the visual part of your story: Dialogue boxes, choices, and character positioning. You must link a 'Story Tree' (Graph) here so the game knows what story to play when this scene loads.",
                    "Эта вкладка настраивает визуал самой игры: окна диалогов, кнопки выборов и т.д. Главное — назначить 'Граф Истории' (Story Tree), чтобы движок знал, какой сюжет запускать на этой сцене."),
                    new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11, richText = true });

                if (GUILayout.Button(ToolLang.Get("Got it, hide guide", "Понятно, скрыть подсказку"), EditorStyles.miniButtonRight, GUILayout.Width(180)))
                    EditorPrefs.SetBool("Novella_HideUIGuide_Game", true);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            DrawSectionHeader("📖", ToolLang.Get("Active Story (Graph)", "Активная История (Граф)"));
            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.HelpBox(ToolLang.Get(
                "NOTE: This graph is only active if the game is launched directly from this scene (for testing).\nIf the player starts from the Main Menu, the Menu will OVERRIDE this and load the chosen story.",
                "ВАЖНО: Этот граф запустится ТОЛЬКО если вы тестируете игру прямо с этой сцены.\nЕсли игрок заходит через Главное Меню, то Меню ПЕРЕОПРЕДЕЛИТ этот граф и загрузит ту историю, которую игрок выбрал в меню."
            ), MessageType.Warning);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Linked Story Tree:", "Подключенный Граф:"), EditorStyles.boldLabel, GUILayout.Width(140));

            string treeName = _player.StoryTree != null ? _player.StoryTree.name : ToolLang.Get("None (Click to select)", "Пусто (Нажмите для выбора)");
            if (GUILayout.Button("📄 " + treeName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    NovellaTree selectedTree = obj as NovellaTree;
                    if (selectedTree != null)
                    {
                        Undo.RecordObject(_player, "Change Story Tree");
                        _player.StoryTree = selectedTree;
                        EditorUtility.SetDirty(_player);
                        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                        _window?.Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Graph, "Assets/NovellaEngine/Resources/Chapters");
            }

            if (_player.StoryTree != null)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    Undo.RecordObject(_player, "Clear Story Tree");
                    _player.StoryTree = null;
                    EditorUtility.SetDirty(_player);
                    if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            if (_player.StoryTree == null)
            {
                GUILayout.Space(5);
                EditorGUILayout.HelpBox(ToolLang.Get("Assign a NovellaTree (Graph) to start the game!", "Назначьте граф (NovellaTree), чтобы игра могла запуститься!"), MessageType.Warning);
            }
            else
            {
                GUILayout.Space(5);
                GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button(ToolLang.Get("Open Graph Editor", "Открыть Редактор Графа"), EditorStyles.miniButton, GUILayout.Height(30)))
                {
                    NovellaGraphWindow.OpenGraphWindow(_player.StoryTree);
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndVertical();
            GUILayout.Space(15);

            if (canvas != null)
            {
                Transform bgTransform = canvas.transform.Find("Background");
                if (bgTransform != null) DrawRectTransformEditor(ToolLang.Get("Background (Canvas)", "Общий Фон (Canvas)"), bgTransform.GetComponent<RectTransform>());
            }

            GUILayout.Space(15);
            DrawPrefabManager(ToolLang.Get("Choice Button Asset", "Ассет Кнопки Выбора"), _player.ChoiceButtonPrefab, obj => _player.ChoiceButtonPrefab = obj, _player, "Assets/NovellaEngine/Runtime/Prefabs");
            if (_player.ChoiceButtonPrefab != null) DrawChoiceButtonPrefabEditor();
            GUILayout.Space(15);

            DrawRectTransformEditor(ToolLang.Get("Dialogue Panel", "Панель Диалога"), _player.DialoguePanel.GetComponent<RectTransform>());
            DrawTextEditor(ToolLang.Get("Speaker Name", "Имя Спикера"), _player.SpeakerNameText);
            DrawTextEditor(ToolLang.Get("Dialogue Text", "Текст Диалога"), _player.DialogueBodyText);
            DrawLayoutContainerEditor(ToolLang.Get("Choices Container", "Контейнер Кнопок"), _player.ChoiceContainer.GetComponent<RectTransform>());

            if (rootCanvas != null)
            {
                DrawSaveLoadPanel(rootCanvas.transform, true);
                DrawWardrobeSection(rootCanvas);
            }

            if (_player.SaveNotification != null)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();

                GUILayout.Label("💾 " + ToolLang.Get("Save Notification", "Уведомление о сохранении"), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                EditorGUI.BeginChangeCheck();
                bool isSaveNotifActive = GUILayout.Toggle(_player.SaveNotification.activeSelf, ToolLang.Get("Show in Scene", "Показать на сцене"), EditorStyles.toolbarButton, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_player.SaveNotification, "Toggle Save Notification");
                    _player.SaveNotification.SetActive(isSaveNotifActive);

                    if (isSaveNotifActive)
                    {
                        var cg = _player.SaveNotification.GetComponent<CanvasGroup>();
                        if (cg != null) { Undo.RecordObject(cg, "Force Alpha"); cg.alpha = 1f; }
                    }

                    EditorUtility.SetDirty(_player.SaveNotification);
                    if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }

                GUILayout.EndHorizontal();

                DrawRectTransformEditor(ToolLang.Get("Notification Panel", "Панель Уведомления"), _player.SaveNotification.GetComponent<RectTransform>());

                if (_activeEditRect == _player.SaveNotification.GetComponent<RectTransform>())
                {
                    var images = _player.SaveNotification.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                    foreach (var img in images)
                    {
                        if (img.gameObject != _player.SaveNotification)
                        {
                            GUILayout.Space(5);
                            EditorGUI.BeginChangeCheck();
                            DrawImageSettingsBlock(img);
                            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(img.gameObject);
                        }
                    }
                }

                TMP_Text notifText = _player.SaveNotification.GetComponentInChildren<TMP_Text>(true);
                if (notifText != null)
                {
                    DrawTextEditor(ToolLang.Get("Notification Text", "Текст уведомления"), notifText);
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(15);
            DrawSectionHeader("✨", ToolLang.Get("Custom Scene Elements", "Кастомные элементы сцены"));

            var customElements = _player.DialoguePanel.transform.parent.GetComponentsInChildren<NovellaCustomUI>(true);
            if (customElements.Length == 0)
            {
                GUILayout.Label(ToolLang.Get("No custom UI added to this scene.", "На этой сцене нет добавленного кастомного UI."), EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                for (int i = 0; i < customElements.Length; i++)
                {
                    var el = customElements[i];
                    if (el == null) continue;

                    DrawRectTransformEditor("🎨 " + el.name, el.GetComponent<RectTransform>(), false, true);

                    if (_activeEditRect == el.GetComponent<RectTransform>())
                    {
                        var texts = el.GetComponentsInChildren<TMP_Text>(true);
                        foreach (var txt in texts)
                        {
                            DrawTextEditor("↳ 📝 " + txt.name, txt);
                        }
                    }
                }
            }

            GUILayout.Space(10);
            if (GUILayout.Button("+ " + ToolLang.Get("Add Custom Prefab to Scene", "Добавить Кастомный Префаб на сцену"), GUILayout.Height(30)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    GameObject go = obj as GameObject;
                    if (go != null && go.GetComponent<NovellaCustomUI>() != null)
                    {
                        GameObject instance = PrefabUtility.InstantiatePrefab(go, _player.DialoguePanel.transform.parent) as GameObject;
                        Undo.RegisterCreatedObjectUndo(instance, "Add Custom UI");
                        instance.transform.SetAsLastSibling();
                        _activeEditRect = instance.GetComponent<RectTransform>();
                        Selection.activeGameObject = instance;
                        _window?.Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.CustomUI, _customPrefabsDir);
            }
        }

        private void DrawMenuFullscreenBackground(Canvas canvas)
        {
            DrawSectionHeader("🖼", ToolLang.Get("Main Background", "Главный Фон Сцены"));

            Transform bgTransform = null;
            Transform parentToUse = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            GameObject bgCanvasObj = GameObject.Find("Background Canvas");
            Transform bgCanvas = bgCanvasObj != null ? bgCanvasObj.transform : null;

            if (bgCanvas == null && parentToUse.parent != null)
            {
                bgCanvas = parentToUse.parent.Find("Background Canvas");
            }

            if (bgCanvas != null)
            {
                bgTransform = bgCanvas.Find("Background");
                parentToUse = bgCanvas;
            }

            if (bgTransform == null)
            {
                var globalBg = GameObject.Find("Background");
                if (globalBg != null && globalBg.GetComponent<Image>() != null)
                {
                    bgTransform = globalBg.transform;
                    parentToUse = bgTransform.parent;
                }
            }

            if (bgTransform == null)
            {
                if (GUILayout.Button("+ " + ToolLang.Get("Create Background", "Создать Фон"), EditorStyles.miniButton, GUILayout.Height(30)))
                {
                    GameObject bg = new GameObject("Background");
                    bg.transform.SetParent(parentToUse, false);
                    bg.transform.SetAsFirstSibling();

                    var newRt = bg.AddComponent<RectTransform>();
                    newRt.anchorMin = Vector2.zero;
                    newRt.anchorMax = Vector2.one;
                    newRt.offsetMin = Vector2.zero;
                    newRt.offsetMax = Vector2.zero;
                    newRt.localScale = Vector3.one;
                    newRt.anchoredPosition = Vector2.zero;
                    newRt.sizeDelta = Vector2.zero;

                    bg.AddComponent<Image>().color = Color.white;
                    Undo.RegisterCreatedObjectUndo(bg, "Create Fullscreen BG");

                    EditorUtility.SetDirty(parentToUse.gameObject);
                    Canvas.ForceUpdateCanvases();
                    GUIUtility.ExitGUI();
                }
                return;
            }

            GUILayout.BeginVertical(EditorStyles.helpBox);
            Image img = bgTransform.GetComponent<Image>();
            RectTransform rt = bgTransform.GetComponent<RectTransform>();

            GUILayout.BeginHorizontal();
            GUILayout.Label("🎨 " + ToolLang.Get("Background Settings", "Настройки Фона"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("[ ] " + ToolLang.Get("Stretch Fullscreen", "Растянуть на весь экран"), EditorStyles.miniButton))
            {
                Undo.RecordObject(rt, "Stretch BG");
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
                EditorUtility.SetDirty(rt);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(55));
            string spriteName = img.sprite != null ? img.sprite.name : ToolLang.Get("None", "Пусто");

            if (GUILayout.Button(spriteName, EditorStyles.popup))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    Sprite sp = obj as Sprite;
                    if (sp == null && obj is Texture2D tex)
                    {
                        string path = AssetDatabase.GetAssetPath(tex);
                        sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                    if (sp != null)
                    {
                        Undo.RecordObject(img, "Change Sprite"); img.sprite = sp;
                        if (img.color == new Color(0.1f, 0.1f, 0.1f, 1f)) img.color = Color.white;
                        EditorUtility.SetDirty(img.gameObject); Canvas.ForceUpdateCanvases(); _window?.Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("✖", GUILayout.Width(25)))
            {
                Undo.RecordObject(img, "Clear Sprite");
                img.sprite = null;
                EditorUtility.SetDirty(img.gameObject);
                Canvas.ForceUpdateCanvases();
                _window?.Repaint();
            }
            GUI.backgroundColor = Color.white;

            img.color = EditorGUILayout.ColorField(GUIContent.none, img.color, false, true, false, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(img.gameObject);
                Canvas.ForceUpdateCanvases();
            }

            GUILayout.EndVertical();
        }

        private void CreateNewGraphForStory(NovellaStory story)
        {
            string chaptersDir = "Assets/NovellaEngine/Resources/Chapters";
            if (!System.IO.Directory.Exists(chaptersDir)) System.IO.Directory.CreateDirectory(chaptersDir);

            NovellaTree newTree = ScriptableObject.CreateInstance<NovellaTree>();
            newTree.StartPosition = new Vector2(400, 300);

            string safeFileName = string.Join("_", story.name.Split(System.IO.Path.GetInvalidFileNameChars()));
            string treePath = AssetDatabase.GenerateUniqueAssetPath($"{chaptersDir}/{safeFileName}_Chapter1.asset");

            AssetDatabase.CreateAsset(newTree, treePath);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(story, "Link Graph");
            story.StartingChapter = newTree;
            EditorUtility.SetDirty(story);

            NovellaGraphWindow.OpenGraphWindow(newTree);
        }

        private void DrawMenuUITab()
        {
            if (_launcher == null) { EditorGUILayout.HelpBox(ToolLang.Get("This is not a Main Menu Scene.", "Это не сцена главного меню."), MessageType.Info); return; }

            Canvas canvas = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);
            Canvas rootCanvas = (canvas != null && canvas.rootCanvas != null) ? canvas.rootCanvas : canvas;

            DrawSectionHeader("⚙", ToolLang.Get("General Menu Settings", "Общие настройки меню"));
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            GUILayout.Label("🎮 " + ToolLang.Get("Target Game Scene:", "Игровая Сцена:"), EditorStyles.boldLabel, GUILayout.Width(140));

            var scenesInBuild = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))
                .ToList();

            if (scenesInBuild.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("No scenes found in Build Settings!", "Нет сцен в Build Settings!"), MessageType.Warning);
            }
            else
            {
                int currentIndex = Mathf.Max(0, scenesInBuild.IndexOf(_launcher.GameSceneName));
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(currentIndex, scenesInBuild.ToArray());
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_launcher, "Change Target Scene");
                    _launcher.GameSceneName = scenesInBuild[newIndex];
                    EditorUtility.SetDirty(_launcher);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);

            DrawSectionHeader("📚", ToolLang.Get("Stories to Show", "Истории для показа"));
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(ToolLang.Get("If empty, ALL stories from Resources/Stories will be loaded automatically.", "Если список пуст, автоматически загрузятся ВСЕ истории из папки Resources/Stories."), MessageType.Info);

            for (int i = 0; i < _launcher.SpecificStories.Count; i++)
            {
                NovellaStory currentStory = _launcher.SpecificStories[i];

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                string sName = currentStory != null ? currentStory.name : ToolLang.Get("Empty (Click to set)", "Пусто (Нажмите для выбора)");

                if (GUILayout.Button("📖 " + sName, EditorStyles.popup))
                {
                    int index = i;
                    NovellaGalleryWindow.ShowWindow(obj => {
                        Undo.RecordObject(_launcher, "Change Story");
                        _launcher.SpecificStories[index] = obj as NovellaStory;
                        EditorUtility.SetDirty(_launcher);
                        _window?.Repaint();
                    }, NovellaGalleryWindow.EGalleryFilter.Story, "Assets/NovellaEngine/Resources/Stories");
                }

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    Undo.RecordObject(_launcher, "Remove Story");
                    _launcher.SpecificStories.RemoveAt(i);
                    EditorUtility.SetDirty(_launcher);
                    GUI.backgroundColor = Color.white;
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                if (currentStory != null)
                {
                    GUILayout.Space(5);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("↳ " + ToolLang.Get("Story File:", "Файл истории:"), EditorStyles.miniLabel, GUILayout.Width(130));

                    if (_editingStoryIndex == i)
                    {
                        _tempStoryName = EditorGUILayout.TextField(_tempStoryName);
                        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                        if (GUILayout.Button("✔", EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            if (!string.IsNullOrEmpty(_tempStoryName) && _tempStoryName != currentStory.name)
                            {
                                string path = AssetDatabase.GetAssetPath(currentStory);
                                AssetDatabase.RenameAsset(path, _tempStoryName);
                                currentStory.Title = _tempStoryName;
                                EditorUtility.SetDirty(currentStory);
                                AssetDatabase.SaveAssets();
                            }
                            _editingStoryIndex = -1;
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        EditorGUILayout.LabelField(currentStory.name, EditorStyles.boldLabel);
                        if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            _editingStoryIndex = i;
                            _tempStoryName = currentStory.name;
                        }
                    }
                    GUILayout.EndHorizontal();

                    if (currentStory.StartingChapter != null)
                    {
                        GUILayout.Space(5);

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("↳ " + ToolLang.Get("Card Prefab:", "Шаблон карточки:"), EditorStyles.miniLabel, GUILayout.Width(130));
                        string spName = currentStory.CustomStoryCardPrefab != null ? currentStory.CustomStoryCardPrefab.name : ToolLang.Get("Default (Global)", "По умолчанию");

                        if (GUILayout.Button("📦 " + spName, EditorStyles.popup, GUILayout.Width(150)))
                        {
                            NovellaGalleryWindow.ShowWindow(obj => {
                                Undo.RecordObject(currentStory, "Set Custom Prefab");
                                currentStory.CustomStoryCardPrefab = obj as GameObject;
                                EditorUtility.SetDirty(currentStory);
                                AssetDatabase.SaveAssets();
                            }, NovellaGalleryWindow.EGalleryFilter.Prefab, "Assets/NovellaEngine/Runtime/Prefabs");
                        }

                        if (currentStory.CustomStoryCardPrefab != null)
                        {
                            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                            {
                                Undo.RecordObject(currentStory, "Clear Prefab");
                                currentStory.CustomStoryCardPrefab = null;
                                EditorUtility.SetDirty(currentStory);
                            }
                            GUI.backgroundColor = Color.white;
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.Space(5);

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("↳ " + ToolLang.Get("Linked Graph:", "Подключенный Граф:"), EditorStyles.miniLabel, GUILayout.Width(130));

                        if (_editingGraphIndex == i)
                        {
                            _tempGraphName = EditorGUILayout.TextField(_tempGraphName);
                            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                            if (GUILayout.Button("✔", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                if (!string.IsNullOrEmpty(_tempGraphName) && _tempGraphName != currentStory.StartingChapter.name)
                                {
                                    string path = AssetDatabase.GetAssetPath(currentStory.StartingChapter);
                                    AssetDatabase.RenameAsset(path, _tempGraphName);
                                    AssetDatabase.SaveAssets();
                                }
                                _editingGraphIndex = -1;
                                GUIUtility.ExitGUI();
                            }
                            GUI.backgroundColor = Color.white;
                        }
                        else
                        {
                            EditorGUILayout.LabelField(currentStory.StartingChapter.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                _editingGraphIndex = i;
                                _tempGraphName = currentStory.StartingChapter.name;
                            }
                        }

                        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                        if (GUILayout.Button(ToolLang.Get("Open", "Открыть"), EditorStyles.miniButton, GUILayout.Width(70)))
                        {
                            NovellaGraphWindow.OpenGraphWindow(currentStory.StartingChapter);
                        }

                        GameObject existingBtn = null;
                        if (_launcher.StoriesContainer != null)
                        {
                            Transform t = _launcher.StoriesContainer.Find("StoryBtn_" + currentStory.name);
                            if (t != null) existingBtn = t.gameObject;
                        }

                        if (existingBtn != null)
                        {
                            bool isEditingThisUI = _activeEditRect != null && (_activeEditRect.gameObject == existingBtn || _activeEditRect.IsChildOf(existingBtn.transform));
                            GUI.backgroundColor = isEditingThisUI ? new Color(0.85f, 1f, 0.85f) : new Color(0.5f, 0.9f, 0.5f);

                            if (GUILayout.Button("✏ " + ToolLang.Get("Edit UI", "Настроить UI"), EditorStyles.miniButton, GUILayout.Width(110)))
                            {
                                _activeEditRect = existingBtn.GetComponent<RectTransform>();
                            }

                            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                            if (GUILayout.Button("🗑", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                if (isEditingThisUI) _activeEditRect = null;
                                Undo.DestroyObjectImmediate(existingBtn);
                                GUIUtility.ExitGUI();
                            }
                        }
                        else
                        {
                            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                            if (GUILayout.Button("➕ " + ToolLang.Get("Spawn", "На сцену"), EditorStyles.miniButton, GUILayout.Width(100)))
                            {
                                SpawnStoryButtonToScene(currentStory);
                            }
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        if (existingBtn != null && _activeEditRect != null && (_activeEditRect.gameObject == existingBtn || _activeEditRect.IsChildOf(existingBtn.transform)))
                        {
                            GUILayout.Space(5);
                            GUILayout.BeginVertical(GUI.skin.box);
                            DrawRectTransformEditor("↳ " + ToolLang.Get("Card Base", "Основа Карточки"), existingBtn.GetComponent<RectTransform>());

                            var titleTxt = existingBtn.transform.Find("TitleText")?.GetComponent<TMP_Text>();
                            var descTxt = existingBtn.transform.Find("DescText")?.GetComponent<TMP_Text>();
                            if (titleTxt != null) DrawTextEditor("   ↳ " + ToolLang.Get("Title Text", "Заголовок"), titleTxt);
                            if (descTxt != null) DrawTextEditor("   ↳ " + ToolLang.Get("Description", "Описание"), descTxt);
                            GUILayout.EndVertical();
                        }
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("↳ " + ToolLang.Get("Linked Graph:", "Подключенный Граф:"), EditorStyles.miniLabel, GUILayout.Width(130));
                        EditorGUILayout.HelpBox(ToolLang.Get("Missing!", "Отсутствует!"), MessageType.Error);

                        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                        if (GUILayout.Button("✨ " + ToolLang.Get("Create", "Создать"), EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(38)))
                        {
                            CreateNewGraphForStory(currentStory);
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;

                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("+ " + ToolLang.Get("Add Existing Story", "Добавить готовую историю"), EditorStyles.miniButton, GUILayout.Height(25)))
            {
                Undo.RecordObject(_launcher, "Add Story");
                _launcher.SpecificStories.Add(null);
                EditorUtility.SetDirty(_launcher);
            }

            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("✨ " + ToolLang.Get("Create NEW Story", "Создать НОВУЮ историю"), EditorStyles.miniButton, GUILayout.Height(25)))
            {
                CreateStoryPopup.ShowPopup(CreateNewStoryAsset);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            if (rootCanvas != null)
            {
                DrawMenuFullscreenBackground(rootCanvas);

                DrawSectionHeader("🎠", ToolLang.Get("Stories Carousel", "Контейнер Историй (Карусель)"));

                DrawLayoutContainerEditor(ToolLang.Get("Carousel Layout", "Настройки расположения"), _launcher.StoriesContainer.GetComponent<RectTransform>());
                DrawMinimalBackgroundEditor(_launcher.StoriesContainer.gameObject, ToolLang.Get("Carousel Background", "Фон Контейнера"));

                DrawSaveLoadPanel(rootCanvas.transform, false);

                DrawMCCreationSection(rootCanvas);
            }
        }
        private void SpawnStoryButtonToScene(NovellaStory story)
        {
            if (_launcher == null || _launcher.StoriesContainer == null) return;

            GameObject prefab = story.CustomStoryCardPrefab;
            if (prefab == null) prefab = _launcher.StoryButtonPrefab;

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Error", "No StoryButtonPrefab found! Please assign it in SceneManager.", "OK");
                return;
            }

            GameObject btnGO = PrefabUtility.InstantiatePrefab(prefab, _launcher.StoriesContainer) as GameObject;
            Undo.RegisterCreatedObjectUndo(btnGO, "Spawn Story Button");
            btnGO.name = "StoryBtn_" + story.name;

            var titleTxt = btnGO.transform.Find("TitleText")?.GetComponent<TMP_Text>();
            if (titleTxt != null) titleTxt.text = story.Title;

            var descTxt = btnGO.transform.Find("DescText")?.GetComponent<TMP_Text>();
            if (descTxt != null) descTxt.text = "Chapter 1";

            var btnComp = btnGO.GetComponent<Button>();
            if (btnComp != null)
            {
                UnityEventTools.AddObjectPersistentListener(btnComp.onClick, new UnityEngine.Events.UnityAction<NovellaStory>(_launcher.TryLaunchStory), story);
            }

            EditorUtility.SetDirty(_launcher.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            _activeEditRect = btnGO.GetComponent<RectTransform>();
        }
        private void DrawSaveLoadPanel(Transform canvasRoot, bool isGameScene)
        {
            if (canvasRoot == null) return;

            var allButtons = canvasRoot.GetComponentsInChildren<Button>(true);

            var saveLoadButtons = allButtons.Where(b =>
                b.name.ToLower().Contains("start") || b.name.ToLower().Contains("начать") || b.name.ToLower().Contains("play") ||
                b.name.ToLower().Contains("settings") || b.name.ToLower().Contains("настройки") ||
                b.name.ToLower().Contains("exit") || b.name.ToLower().Contains("выход")
            ).ToList();

            if (saveLoadButtons.Count == 0)
            {
                if (!isGameScene)
                {
                    GUILayout.Space(15);
                    EditorGUILayout.HelpBox(ToolLang.Get("No Main Menu buttons found on Canvas. Create them manually (e.g. 'Btn_Start', 'Btn_Exit') to edit them here.", "Базовые кнопки меню не найдены. Создайте их вручную на Canvas (например, 'Btn_Start', 'Btn_Exit'), и они появятся здесь для настройки."), MessageType.Info);
                }
                return;
            }

            GUILayout.Space(15);
            DrawSectionHeader("🕹️", ToolLang.Get("Menu Navigation Buttons", "Кнопки Навигации Меню"));

            foreach (var btn in saveLoadButtons)
            {
                string lowerName = btn.name.ToLower();
                string btnTitle = btn.name;

                if (lowerName.Contains("start") || lowerName.Contains("play") || lowerName.Contains("начать")) btnTitle = ToolLang.Get("Start / Play Button", "Кнопка Начать Игру");
                else if (lowerName.Contains("settings") || lowerName.Contains("настройки")) btnTitle = ToolLang.Get("Settings Button", "Кнопка Настройки");
                else if (lowerName.Contains("exit") || lowerName.Contains("выход")) btnTitle = ToolLang.Get("Exit Button", "Кнопка Выход");

                DrawRectTransformEditor($"▶ {btnTitle} ({btn.gameObject.name})", btn.GetComponent<RectTransform>());

                bool isEditingBtn = _activeEditRect != null && (_activeEditRect == btn.GetComponent<RectTransform>() || _activeEditRect.IsChildOf(btn.transform));

                if (isEditingBtn)
                {
                    var txt = btn.GetComponentInChildren<TMP_Text>();
                    if (txt != null)
                    {
                        if (txt.rectTransform.anchorMin == Vector2.zero && txt.rectTransform.anchorMax == Vector2.one)
                        {
                            Undo.RecordObject(txt.rectTransform, "Auto-fix Text Anchors");
                            txt.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                            txt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                            txt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                            txt.rectTransform.sizeDelta = btn.GetComponent<RectTransform>().rect.size;
                            txt.rectTransform.anchoredPosition = Vector2.zero;
                            EditorUtility.SetDirty(txt.rectTransform);
                            Canvas.ForceUpdateCanvases();
                        }

                        DrawTextEditor("↳ 📝 " + ToolLang.Get("Button Text", "Текст на кнопке"), txt);
                    }

                    GUILayout.Space(5);
                    EditorGUI.BeginChangeCheck();
                    DrawButtonSettingsBlock(btn);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(btn.gameObject);
                }
            }
        }

        private void DrawMinimalBackgroundEditor(GameObject targetObj, string title)
        {
            if (targetObj == null) return;

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🎨 " + title, EditorStyles.boldLabel);

            Image img = targetObj.GetComponent<Image>();
            if (img == null)
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ " + ToolLang.Get("Add Background", "Добавить Фон"), EditorStyles.miniButton))
                {
                    Undo.AddComponent<Image>(targetObj);
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    Undo.DestroyObjectImmediate(img);
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(55));
                string spriteName = img.sprite != null ? img.sprite.name : ToolLang.Get("None", "Пусто");

                if (GUILayout.Button(spriteName, EditorStyles.popup))
                {
                    NovellaGalleryWindow.ShowWindow(obj => {
                        Sprite sp = obj as Sprite;
                        if (sp == null && obj is Texture2D) sp = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                        if (sp != null)
                        {
                            Undo.RecordObject(img, "Change Sprite"); img.sprite = sp;
                            EditorUtility.SetDirty(img.gameObject); img.SetAllDirty(); Canvas.ForceUpdateCanvases(); _window?.Repaint();
                        }
                    }, NovellaGalleryWindow.EGalleryFilter.Image);
                }

                img.color = EditorGUILayout.ColorField(GUIContent.none, img.color, false, true, false, GUILayout.Width(60));
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(img.gameObject);
                    Canvas.ForceUpdateCanvases();
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawPrefabsTab()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🎨 " + ToolLang.Get("Prefab Styler", "Изолированная Стилизация"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(ToolLang.Get(
                "Select any UI prefab here to safely edit its visuals. This will NOT assign it to the current scene.",
                "Выберите любой UI префаб, чтобы безопасно настроить его визуал. Это НЕ привязывает его к текущей сцене."
            ), MessageType.Info);

            GUILayout.BeginHorizontal();
            string pName = _stylerPrefab != null ? _stylerPrefab.name : ToolLang.Get("Select Prefab...", "Выбрать Префаб...");
            if (GUILayout.Button("📦 " + pName, EditorStyles.popup))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    GameObject go = obj as GameObject;
                    if (go != null)
                    {
                        _stylerPrefab = go;
                        CleanTempPrefabs();
                        _activeEditRect = null;
                        _activeDecoRect = null;
                        _window?.Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Prefab, "Assets/NovellaEngine/Runtime/Prefabs");
            }

            if (_stylerPrefab != null)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("✖", GUILayout.Width(30)))
                {
                    _stylerPrefab = null;
                    CleanTempPrefabs();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);

            if (_stylerPrefab != null) DrawCustomPrefabEditor(_stylerPrefab);
        }

        private void DrawChoiceButtonPrefabEditor()
        {
            if (_tempChoicePreview == null && _player.ChoiceContainer != null)
            {
                _tempChoicePreview = PrefabUtility.InstantiatePrefab(_player.ChoiceButtonPrefab, _player.ChoiceContainer) as GameObject;
            }

            if (_tempChoicePreview == null) return;

            DrawPrefabSaveHeader(_tempChoicePreview, _player.ChoiceButtonPrefab);
            DrawRectTransformEditor(ToolLang.Get("Base Button", "Базовая Кнопка"), _tempChoicePreview.GetComponent<RectTransform>(), true);

            TMP_Text txt = _tempChoicePreview.GetComponentInChildren<TMP_Text>();
            if (txt != null) DrawTextEditor(ToolLang.Get("Button Text", "Текст на кнопке"), txt, true);
        }

        private void DrawCustomUITab()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🛠 " + ToolLang.Get("Create New Custom Prefab", "Создать новый кастомный префаб"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(ToolLang.Get(
                "Create unique frames, popups, or sliders. You can later select them in your Dialogue Nodes to swap the UI dynamically!",
                "Создавайте уникальные рамки диалогов, попапы или слайдеры. Позже вы сможете выбирать их в нодах Диалога для динамической смены!"
            ), MessageType.Info);

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            EditorGUIUtility.labelWidth = 50;
            _newPrefabName = EditorGUILayout.TextField(ToolLang.Get("Name", "Имя"), _newPrefabName, GUILayout.ExpandWidth(true));

            string[] typeNames = { ToolLang.Get("Image", "Картинка (Image)"), ToolLang.Get("Text", "Текст (TMP_Text)"), ToolLang.Get("Slider", "Ползунок (Slider)"), ToolLang.Get("Button", "Кнопка (Button)") };
            _newPrefabTypeIndex = EditorGUILayout.Popup(_newPrefabTypeIndex, typeNames, GUILayout.Width(130));

            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("+ " + ToolLang.Get("Create", "Создать"), GUILayout.Width(80)))
            {
                CreateCustomPrefab();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(15);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { _customPrefabsDir });
            List<GameObject> customPrefabs = guids.Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                                                  .Where(p => p != null && p.GetComponent<NovellaCustomUI>() != null).ToList();

            if (customPrefabs.Count == 0)
            {
                GUILayout.Label(ToolLang.Get("No custom prefabs found.", "Кастомные префабы не найдены."), EditorStyles.centeredGreyMiniLabel);
                return;
            }

            GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            _customPrefabFoldout = GUILayout.Toggle(_customPrefabFoldout, _customPrefabFoldout ? "▼ " + ToolLang.Get("Custom Prefabs List", "Список Кастомных Префабов") : "▶ " + ToolLang.Get("Custom Prefabs List", "Список Кастомных Префабов"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            if (_customPrefabFoldout)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                if (_selectedCustomPrefab != null && customPrefabs.Contains(_selectedCustomPrefab))
                {
                    _customPrefabIndex = customPrefabs.IndexOf(_selectedCustomPrefab);
                }

                string[] names = customPrefabs.Select(p => p.name).ToArray();
                _customPrefabIndex = Mathf.Clamp(_customPrefabIndex, 0, names.Length - 1);

                EditorGUI.BeginChangeCheck();
                _customPrefabIndex = EditorGUILayout.Popup(ToolLang.Get("Select to Edit:", "Выбрать для настройки:"), _customPrefabIndex, names);
                if (EditorGUI.EndChangeCheck() || _selectedCustomPrefab != customPrefabs[_customPrefabIndex])
                {
                    _selectedCustomPrefab = customPrefabs[_customPrefabIndex];
                    CleanTempPrefabs();
                    _activeEditRect = null;
                    _activeDecoRect = null;
                }

                GUILayout.Space(10);
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("✏ " + ToolLang.Get("Rename", "Переименовать"), EditorStyles.miniButton, GUILayout.Width(130), GUILayout.Height(25)))
                {
                    string oldName = _selectedCustomPrefab.name;
                    string path = AssetDatabase.GetAssetPath(_selectedCustomPrefab);
                    RenamePopup.ShowPopup(oldName, newName => {
                        AssetDatabase.RenameAsset(path, newName);
                        AssetDatabase.Refresh();
                        _selectedCustomPrefab = null;
                        CleanTempPrefabs();
                    });
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("🗑 " + ToolLang.Get("Delete Selected", "Удалить выбранный"), EditorStyles.miniButton, GUILayout.Width(150), GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete?", "Удалить?"), ToolLang.Get($"Delete '{_selectedCustomPrefab.name}' permanently?", $"Удалить '{_selectedCustomPrefab.name}' навсегда?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        string path = AssetDatabase.GetAssetPath(_selectedCustomPrefab);
                        AssetDatabase.DeleteAsset(path);
                        _selectedCustomPrefab = null;
                        CleanTempPrefabs();
                        GUIUtility.ExitGUI();
                    }
                }

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("☢ " + ToolLang.Get("Delete ALL", "Удалить ВСЕ"), EditorStyles.miniButton, GUILayout.Width(100), GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog("Warning", ToolLang.Get("Delete ALL custom prefabs?", "Удалить ВСЕ кастомные префабы?"), "Yes", "No"))
                    {
                        if (EditorUtility.DisplayDialog("Are you sure?", ToolLang.Get("This will break any scenes or nodes using them!", "Это сломает сцены и ноды, которые их используют!"), "Yes", "No"))
                        {
                            if (EditorUtility.DisplayDialog("FINAL WARNING", ToolLang.Get("ALL CUSTOM PREFABS WILL BE OBLITERATED!", "ВСЕ КАСТОМНЫЕ ПРЕФАБЫ БУДУТ УНИЧТОЖЕНЫ!"), "DESTROY", "Cancel"))
                            {
                                AssetDatabase.DeleteAsset(_customPrefabsDir);
                                Directory.CreateDirectory(_customPrefabsDir);
                                AssetDatabase.Refresh();
                                _selectedCustomPrefab = null;
                                CleanTempPrefabs();
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }

            if (_selectedCustomPrefab != null)
            {
                DrawCustomPrefabEditor(_selectedCustomPrefab);
            }
        }

        private void CreateCustomPrefab()
        {
            if (!AssetDatabase.IsValidFolder(_customPrefabsDir)) Directory.CreateDirectory(_customPrefabsDir);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{_customPrefabsDir}/{_newPrefabName}.prefab");

            GameObject root = new GameObject(_newPrefabName);
            var rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800, 250);

            var customUiComp = root.AddComponent<NovellaCustomUI>();

            if (_newPrefabTypeIndex == 0)
            {
                var img = root.AddComponent<Image>();
                img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            }
            else if (_newPrefabTypeIndex == 1)
            {
                var txt = root.AddComponent<TextMeshProUGUI>();
                txt.text = "Custom Text";
                txt.fontSize = 32;
                txt.alignment = TextAlignmentOptions.Center;
                customUiComp.OverrideDialogueText = txt;
            }
            else if (_newPrefabTypeIndex == 2)
            {
                GameObject sliderTemplate = DefaultControls.CreateSlider(new DefaultControls.Resources());
                sliderTemplate.transform.SetParent(root.transform, false);
                var sRt = sliderTemplate.GetComponent<RectTransform>();
                sRt.anchorMin = Vector2.zero; sRt.anchorMax = Vector2.one;
                sRt.offsetMin = Vector2.zero; sRt.offsetMax = Vector2.zero;

                var handleArea = sliderTemplate.transform.Find("Handle Slide Area");
                if (handleArea != null) UnityEngine.Object.DestroyImmediate(handleArea.gameObject);

                var bg = sliderTemplate.transform.Find("Background")?.GetComponent<Image>();
                if (bg != null) { bg.sprite = null; bg.color = new Color(0.1f, 0.1f, 0.1f, 0.5f); }

                var fill = sliderTemplate.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
                if (fill != null) { fill.sprite = null; fill.color = new Color(0.2f, 0.8f, 0.4f, 1f); }

                var comp = sliderTemplate.GetComponent<Slider>();
                if (comp != null) comp.interactable = false;
            }
            else if (_newPrefabTypeIndex == 3)
            {
                var img = root.AddComponent<Image>();
                img.color = new Color(0.2f, 0.6f, 0.8f, 1f);
                var btn = root.AddComponent<Button>();

                GameObject txtObj = new GameObject("Text");
                txtObj.transform.SetParent(root.transform, false);
                var trt = txtObj.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

                var txt = txtObj.AddComponent<TextMeshProUGUI>();
                txt.text = "Button";
                txt.fontSize = 28;
                txt.alignment = TextAlignmentOptions.Center;
                txt.color = Color.white;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.Refresh();

            _selectedCustomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            _newPrefabName = "NewCustomFrame";
        }

        private void CreateNewStoryAsset(string storyName)
        {
            string storiesDir = "Assets/NovellaEngine/Resources/Stories";
            string chaptersDir = "Assets/NovellaEngine/Resources/Chapters";

            if (!Directory.Exists(storiesDir)) Directory.CreateDirectory(storiesDir);
            if (!Directory.Exists(chaptersDir)) Directory.CreateDirectory(chaptersDir);
            AssetDatabase.Refresh();

            NovellaTree newTree = ScriptableObject.CreateInstance<NovellaTree>();
            newTree.StartPosition = new Vector2(400, 300);

            string safeFileName = string.Join("_", storyName.Split(Path.GetInvalidFileNameChars()));

            string treePath = AssetDatabase.GenerateUniqueAssetPath($"{chaptersDir}/{safeFileName}_Chapter1.asset");
            AssetDatabase.CreateAsset(newTree, treePath);

            NovellaStory newStory = ScriptableObject.CreateInstance<NovellaStory>();
            newStory.Title = storyName;
            newStory.StartingChapter = newTree;

            string storyPath = AssetDatabase.GenerateUniqueAssetPath($"{storiesDir}/{safeFileName}.asset");
            AssetDatabase.CreateAsset(newStory, storyPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (_launcher != null)
            {
                Undo.RecordObject(_launcher, "Add New Story");
                _launcher.SpecificStories.Add(newStory);
                EditorUtility.SetDirty(_launcher);
            }

            EditorUtility.DisplayDialog(
                ToolLang.Get("Success", "Успех"),
                ToolLang.Get($"Story '{storyName}' created successfully!\nGraph for Chapter 1 is linked.", $"История '{storyName}' успешно создана!\nГраф для Главы 1 автоматически подключен."),
                "OK"
            );

            NovellaGraphWindow.OpenGraphWindow(newTree);
            _window?.Repaint();
        }

        private void DrawCustomPrefabEditor(GameObject prefab)
        {
            Transform parentCanvas = _player != null && _player.DialoguePanel != null ? _player.DialoguePanel.transform.parent : null;
            if (parentCanvas == null && _launcher != null && _launcher.StoriesContainer != null) parentCanvas = _launcher.StoriesContainer.parent;

            if (parentCanvas == null) return;

            if (_tempCustomPreview == null)
            {
                _tempCustomPreview = PrefabUtility.InstantiatePrefab(prefab, parentCanvas) as GameObject;
                _activeEditRect = _tempCustomPreview.GetComponent<RectTransform>();
            }

            DrawPrefabSaveHeader(_tempCustomPreview, prefab);

            DrawRectTransformEditor(ToolLang.Get("Base Object", "Основной Объект"), _tempCustomPreview.GetComponent<RectTransform>(), true);

            var customComp = _tempCustomPreview.GetComponent<NovellaCustomUI>();
            if (customComp != null)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("🤖 " + ToolLang.Get("Smart Text Bindings", "Умные привязки текста"), EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();

                customComp.OverrideSpeakerName = (TMP_Text)EditorGUILayout.ObjectField(ToolLang.Get("Auto Speaker Name", "Авто Имя Спикера"), customComp.OverrideSpeakerName, typeof(TMP_Text), true);
                customComp.OverrideDialogueText = (TMP_Text)EditorGUILayout.ObjectField(ToolLang.Get("Auto Dialogue Text", "Авто Текст Диалога"), customComp.OverrideDialogueText, typeof(TMP_Text), true);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_tempCustomPreview);
                }
                GUILayout.EndVertical();
            }

            var elements = _tempCustomPreview.GetComponentsInChildren<RectTransform>(true);
            foreach (var rt in elements)
            {
                if (rt.gameObject == _tempCustomPreview) continue;

                var txt = rt.GetComponent<TMP_Text>();
                if (txt != null) DrawTextEditor("↳ 📝 " + rt.name, txt, true, true);
                else DrawRectTransformEditor("↳ 🖼 " + rt.name, rt, true, true);
            }

            GUILayout.Space(10);
            if (GUILayout.Button("+ " + ToolLang.Get("Add Child Image", "Добавить Картинку"), EditorStyles.miniButton))
            {
                Undo.RecordObject(_tempCustomPreview, "Add Image");
                GameObject imgGo = new GameObject("NewImage");
                imgGo.transform.SetParent(_tempCustomPreview.transform, false);
                var irt = imgGo.AddComponent<RectTransform>();
                irt.sizeDelta = new Vector2(100, 100);
                imgGo.AddComponent<Image>().color = new Color(0.8f, 0.8f, 0.8f, 1f);
                _activeEditRect = irt;
            }
            if (GUILayout.Button("+ " + ToolLang.Get("Add Child Text", "Добавить Текст"), EditorStyles.miniButton))
            {
                Undo.RecordObject(_tempCustomPreview, "Add Text");
                GameObject txtGo = new GameObject("NewText");
                txtGo.transform.SetParent(_tempCustomPreview.transform, false);
                var trt = txtGo.AddComponent<RectTransform>();
                trt.sizeDelta = new Vector2(200, 50);
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = "Sample Text";
                tmp.fontSize = 24;
                tmp.alignment = TextAlignmentOptions.Center;
                _activeEditRect = trt;
            }
        }

        private void DrawPrefabSaveHeader(GameObject instance, GameObject originalPrefab)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("🛠 " + ToolLang.Get("Editing Prefab:", "Редактирование: ") + originalPrefab.name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("💾 " + ToolLang.Get("Apply & Save", "Применить и Сохранить"), EditorStyles.toolbarButton, GUILayout.Width(180)))
            {
                PrefabUtility.SaveAsPrefabAsset(instance, AssetDatabase.GetAssetPath(originalPrefab));
                EditorUtility.DisplayDialog(ToolLang.Get("Success", "Успех"), ToolLang.Get("Prefab saved successfully!", "Префаб успешно сохранен!"), "OK");
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawPrefabManager(string label, GameObject currentPrefab, Action<GameObject> onSetPrefab, UnityEngine.Object undoTarget, string createPath)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔘 " + label, EditorStyles.boldLabel, GUILayout.Width(180));

            string pName = currentPrefab != null ? currentPrefab.name : ToolLang.Get("Default (System)", "Базовая системная");
            if (GUILayout.Button(pName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    Undo.RecordObject(undoTarget, "Change Prefab");
                    onSetPrefab(obj as GameObject);
                    EditorUtility.SetDirty(undoTarget);
                    CleanTempPrefabs();
                    _activeEditRect = null;
                    _activeDecoRect = null;
                    _window?.Repaint();
                }, NovellaGalleryWindow.EGalleryFilter.Prefab, createPath);
            }

            if (currentPrefab != null)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    Undo.RecordObject(undoTarget, "Clear Prefab");
                    onSetPrefab(null);
                    CleanTempPrefabs();
                    _activeEditRect = null;
                    _activeDecoRect = null;
                    EditorUtility.SetDirty(undoTarget);
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawActiveRectBounds(Rect localDrawRect)
        {
            if (_activeEditRect == null || _camera == null || _previewTexture == null) return;

            Vector3[] worldCorners = new Vector3[4];
            _activeEditRect.GetWorldCorners(worldCorners);

            Vector2[] screenPts = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 vp = _camera.WorldToViewportPoint(worldCorners[i]);
                screenPts[i] = new Vector2(
                    localDrawRect.x + vp.x * localDrawRect.width,
                    localDrawRect.y + (1f - vp.y) * localDrawRect.height
                );
            }

            Handles.color = Color.green;
            Handles.DrawLine(screenPts[0], screenPts[1]);
            Handles.DrawLine(screenPts[1], screenPts[2]);
            Handles.DrawLine(screenPts[2], screenPts[3]);
            Handles.DrawLine(screenPts[3], screenPts[0]);
            Handles.color = Color.white;
        }

        private void DrawPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("👁 " + ToolLang.Get("Live Scene Preview", "Живой Предпросмотр Сцены"), EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();

            _showBounds = GUILayout.Toggle(_showBounds, ToolLang.Get("Show Bounds", "Показывать рамки"), EditorStyles.toolbarButton);
            GUILayout.Space(10);
            GUILayout.Label("Zoom:", EditorStyles.miniLabel);
            _uiZoom = GUILayout.HorizontalSlider(_uiZoom, 0.5f, 2f, GUILayout.Width(100));

            bool newMobileMode = GUILayout.Toggle(_isMobileMode, "📱 Mobile Mode", EditorStyles.toolbarButton);
            if (newMobileMode != _isMobileMode)
            {
                _isMobileMode = newMobileMode;
                if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); _previewTexture = null; }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            if (_camera == null)
            {
                GUILayout.Label("Camera not found in Scene.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                return;
            }

            Rect previewRect = GUILayoutUtility.GetRect(400, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);

            if (Event.current.type == EventType.Repaint)
            {
                int targetW = _isMobileMode ? 1080 : 1920;
                int targetH = _isMobileMode ? 1920 : 1080;

                if (_previewTexture == null || _previewTexture.width != targetW || _previewTexture.height != targetH)
                {
                    if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); }
                    _previewTexture = new RenderTexture(targetW, targetH, 24);
                }

                var oldTarget = _camera.targetTexture;
                _camera.targetTexture = _previewTexture;
                _camera.Render();
                _camera.targetTexture = oldTarget;

                float aspect = (float)targetW / targetH;
                float w = previewRect.width * _uiZoom;
                float h = w / aspect;

                if (h > previewRect.height * _uiZoom)
                {
                    h = previewRect.height * _uiZoom;
                    w = h * aspect;
                }

                Rect drawRect = new Rect(
                    previewRect.x + (previewRect.width - w) / 2f,
                    previewRect.y + (previewRect.height - h) / 2f,
                    w, h
                );

                GUI.DrawTexture(drawRect, _previewTexture, ScaleMode.ScaleToFit, false);
                if (_showBounds) DrawActiveRectBounds(drawRect);
            }

            GUILayout.EndVertical();
        }

        private void DrawRectTransformEditor(string title, RectTransform rt, bool isPrefabMode = false, bool showDeleteBtn = false)
        {
            if (rt == null) return;
            bool isExpanded = _activeEditRect == rt;

            GUI.backgroundColor = isExpanded ? new Color(0.8f, 0.9f, 1f) : Color.white;
            GUILayout.BeginHorizontal(GUI.skin.button);

            string arrow = isExpanded ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} {title}", EditorStyles.label, GUILayout.ExpandWidth(true)))
            {
                _activeEditRect = isExpanded ? null : rt;
                _activeDecoRect = null;
                Selection.activeGameObject = rt.gameObject;
            }

            if (showDeleteBtn)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    if (isExpanded) _activeEditRect = null;
                    Undo.DestroyObjectImmediate(rt.gameObject);
                    GUIUtility.ExitGUI();
                }
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            if (isExpanded)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Anchors (Min/Max):", "Якоря (Min/Max):"), GUILayout.Width(120));
                Vector2 aMin = EditorGUILayout.Vector2Field("", rt.anchorMin, GUILayout.Width(100));
                Vector2 aMax = EditorGUILayout.Vector2Field("", rt.anchorMax, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Pivot:", "Пивот (Ось):"), GUILayout.Width(120));
                Vector2 pivot = EditorGUILayout.Vector2Field("", rt.pivot, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Position (X,Y):", "Позиция (X,Y):"), GUILayout.Width(120));
                Vector2 pos = EditorGUILayout.Vector2Field("", rt.anchoredPosition, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Size (W,H):", "Размер (Ш,В):"), GUILayout.Width(120));
                Vector2 size = EditorGUILayout.Vector2Field("", rt.sizeDelta, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(rt, "RectTransform Change");
                    rt.anchorMin = aMin; rt.anchorMax = aMax;
                    rt.pivot = pivot;
                    rt.anchoredPosition = pos;
                    rt.sizeDelta = size;
                    EditorUtility.SetDirty(rt.gameObject);

                    if (isPrefabMode) Canvas.ForceUpdateCanvases();
                    else if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }

                Image img = rt.GetComponent<Image>();
                if (img != null)
                {
                    GUILayout.Space(10);
                    EditorGUI.BeginChangeCheck();
                    DrawImageSettingsBlock(img);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(img.gameObject);
                }

                if (GUILayout.Button("+ " + ToolLang.Get("Add Deco Component", "Добавить Декоративный элемент"), EditorStyles.miniButton))
                {
                    GameObject child = new GameObject("Deco");
                    child.transform.SetParent(rt, false);
                    var crt = child.AddComponent<RectTransform>();
                    crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
                    crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                    child.AddComponent<Image>().color = new Color(1, 1, 1, 0.5f);
                    Undo.RegisterCreatedObjectUndo(child, "Add Deco Component");
                }

                DrawDecoratorsList(rt);

                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawDecoratorsList(RectTransform parentRt)
        {
            var children = new List<RectTransform>();
            foreach (Transform t in parentRt)
            {
                if (t.GetComponent<TMP_Text>() == null && t.GetComponent<Button>() == null && t.GetComponent<LayoutGroup>() == null && t.GetComponent<NovellaCustomUI>() == null)
                    children.Add(t.GetComponent<RectTransform>());
            }

            if (children.Count > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("✨ " + ToolLang.Get("Decorative Components", "Декоративные элементы"), EditorStyles.miniBoldLabel);

                for (int i = 0; i < children.Count; i++)
                {
                    var childRt = children[i];
                    bool isDecoExpanded = _activeDecoRect == childRt;

                    GUILayout.BeginHorizontal();

                    if (_renamingDecoRect == childRt)
                    {
                        _renamingDecoName = EditorGUILayout.TextField(_renamingDecoName, GUILayout.ExpandWidth(true));
                        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                        if (GUILayout.Button("✔", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.RecordObject(childRt.gameObject, "Rename Deco");
                            childRt.gameObject.name = _renamingDecoName;
                            _renamingDecoRect = null;
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        GUI.backgroundColor = isDecoExpanded ? new Color(0.8f, 0.8f, 0.8f) : Color.white;
                        if (GUILayout.Button((isDecoExpanded ? "▼ " : "▶ ") + childRt.name, EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(true)))
                        {
                            _activeDecoRect = isDecoExpanded ? null : childRt;
                        }

                        if (GUILayout.Button("✏", EditorStyles.miniButtonMid, GUILayout.Width(25)))
                        {
                            _renamingDecoRect = childRt;
                            _renamingDecoName = childRt.name;
                        }

                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(25)))
                        {
                            Undo.DestroyObjectImmediate(childRt.gameObject);
                            _activeDecoRect = null;
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;
                    }

                    GUILayout.EndHorizontal();

                    if (isDecoExpanded)
                    {
                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(40))) { childRt.SetSiblingIndex(Mathf.Max(0, childRt.GetSiblingIndex() - 1)); }
                        if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(40))) { childRt.SetSiblingIndex(childRt.GetSiblingIndex() + 1); }
                        GUILayout.EndHorizontal();

                        EditorGUI.BeginChangeCheck();
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Offset Min:", EditorStyles.miniLabel, GUILayout.Width(60));
                        Vector2 oMin = EditorGUILayout.Vector2Field("", childRt.offsetMin, GUILayout.Width(80));
                        GUILayout.Space(5);
                        GUILayout.Label("Offset Max:", EditorStyles.miniLabel, GUILayout.Width(60));
                        Vector2 oMax = EditorGUILayout.Vector2Field("", childRt.offsetMax, GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(childRt, "Change Deco Offset");
                            childRt.offsetMin = oMin; childRt.offsetMax = oMax;
                            EditorUtility.SetDirty(childRt.gameObject);
                        }

                        Image img = childRt.GetComponent<Image>();
                        if (img != null)
                        {
                            GUILayout.Space(5);
                            EditorGUI.BeginChangeCheck();
                            DrawImageSettingsBlock(img);
                            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(img.gameObject);
                        }
                        GUILayout.EndVertical();
                    }
                }
            }
        }

        private void DrawImageSettingsBlock(Image img)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(55));
            string spriteName = img.sprite != null ? img.sprite.name : ToolLang.Get("None", "Пусто");

            if (GUILayout.Button(spriteName, EditorStyles.popup))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    Sprite sp = obj as Sprite;
                    if (sp == null && obj is Texture2D tex)
                    {
                        string path = AssetDatabase.GetAssetPath(tex);
                        sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                    if (sp != null)
                    {
                        Undo.RecordObject(img, "Change Sprite"); img.sprite = sp;
                        if (img.color == new Color(0.1f, 0.1f, 0.1f, 1f)) img.color = Color.white;
                        EditorUtility.SetDirty(img.gameObject); img.SetAllDirty(); Canvas.ForceUpdateCanvases(); _window?.Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("✖", GUILayout.Width(25)))
            {
                Undo.RecordObject(img, "Clear Sprite");
                img.sprite = null;
                EditorUtility.SetDirty(img.gameObject);
                Canvas.ForceUpdateCanvases();
                _window?.Repaint();
            }
            GUI.backgroundColor = Color.white;

            img.color = EditorGUILayout.ColorField(GUIContent.none, img.color, false, true, false, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (img.sprite != null)
            {
                img.type = (Image.Type)EditorGUILayout.EnumPopup(ToolLang.Get("Image Type", "Тип Заливки"), img.type);
                if (img.type == Image.Type.Filled)
                {
                    img.fillMethod = (Image.FillMethod)EditorGUILayout.EnumPopup("Fill Method", img.fillMethod);
                    img.fillAmount = EditorGUILayout.Slider("Fill Amount", img.fillAmount, 0f, 1f);
                }
            }
        }

        private void DrawTMPTextSettingsBlock(TMP_Text txt, string title)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("📝 " + title, EditorStyles.miniBoldLabel);

            txt.font = (TMP_FontAsset)EditorGUILayout.ObjectField(ToolLang.Get("Font", "Шрифт"), txt.font, typeof(TMP_FontAsset), false);

            txt.color = EditorGUILayout.ColorField(ToolLang.Get("Color", "Цвет"), txt.color);
            txt.fontSize = EditorGUILayout.FloatField(ToolLang.Get("Font Size", "Размер"), txt.fontSize);
            txt.alignment = (TextAlignmentOptions)EditorGUILayout.EnumPopup(ToolLang.Get("Alignment", "Выравнивание"), txt.alignment);

            GUILayout.BeginHorizontal();
            txt.enableWordWrapping = EditorGUILayout.ToggleLeft(ToolLang.Get(" Word Wrap", " Перенос строк"), txt.enableWordWrapping, GUILayout.Width(100));
            txt.enableAutoSizing = EditorGUILayout.ToggleLeft(ToolLang.Get(" Auto Size", " Авто размер"), txt.enableAutoSizing);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawButtonSettingsBlock(Button btn)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🔘 " + ToolLang.Get("Button Interactivity", "Интерактивность"), EditorStyles.miniBoldLabel);

            btn.transition = (Selectable.Transition)EditorGUILayout.EnumPopup(ToolLang.Get("Transition", "Эффект"), btn.transition);
            if (btn.transition == Selectable.Transition.ColorTint)
            {
                var cb = btn.colors;
                cb.normalColor = EditorGUILayout.ColorField("Normal", cb.normalColor);
                cb.highlightedColor = EditorGUILayout.ColorField("Hover", cb.highlightedColor);
                cb.pressedColor = EditorGUILayout.ColorField("Pressed", cb.pressedColor);
                cb.disabledColor = EditorGUILayout.ColorField("Disabled", cb.disabledColor);
                btn.colors = cb;
            }
            else if (btn.transition == Selectable.Transition.SpriteSwap)
            {
                var sb = btn.spriteState;
                sb.highlightedSprite = (Sprite)EditorGUILayout.ObjectField("Hover Sprite", sb.highlightedSprite, typeof(Sprite), false);
                sb.pressedSprite = (Sprite)EditorGUILayout.ObjectField("Pressed Sprite", sb.pressedSprite, typeof(Sprite), false);
                sb.disabledSprite = (Sprite)EditorGUILayout.ObjectField("Disabled Sprite", sb.disabledSprite, typeof(Sprite), false);
                btn.spriteState = sb;
            }
            GUILayout.EndVertical();
        }
    }
}