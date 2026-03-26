using NovellaEngine.Data;
using NovellaEngine.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor
{
    public class NovellaUIEditorWindow : EditorWindow
    {
        private NovellaPlayer _player;
        private StoryLauncher _launcher;
        private Camera _camera;
        private RenderTexture _previewTexture;
        private Vector2 _scrollPos;

        private int _currentTab = 0;
        private string[] _tabs;

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

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaUIEditorWindow>(ToolLang.Get("UI Master Forge", "Кузница UI"));
            win.minSize = new Vector2(1200, 700);
            win.Show();
        }

        public static void OpenWithCustomPrefab(GameObject targetPrefab)
        {
            var win = GetWindow<NovellaUIEditorWindow>(ToolLang.Get("UI Master Forge", "Кузница UI"));
            win.minSize = new Vector2(1200, 700);
            win._currentTab = 3;
            win._selectedCustomPrefab = targetPrefab;
            win.CleanTempPrefabs();
            win._activeEditRect = null;
            win._activeDecoRect = null;
            win.FindReferences();
            win.Show();
            win.Focus();
        }

        private void OnEnable()
        {
            _tabs = new string[] { "🎮 " + ToolLang.Get("Game UI", "Игровой UI"), "📱 " + ToolLang.Get("Menu UI", "Меню UI"), "💠 " + ToolLang.Get("System Prefabs", "Системные Префабы"), "🛠 " + ToolLang.Get("Prefab Creator", "Создание Префабов") };
            if (!AssetDatabase.IsValidFolder(_customPrefabsDir)) Directory.CreateDirectory(_customPrefabsDir);

            EditorApplication.delayCall += () => {
                FindReferences();
                Repaint();
            };

            EditorApplication.update += OnEditorUpdate;
        }
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanTempPrefabs();
            ClearDummyButtons();
            if (_previewTexture != null) { _previewTexture.Release(); DestroyImmediate(_previewTexture); }
        }
        private void OnHierarchyChange()
        {
            FindReferences();
            Repaint();
        }
        private void OnEditorUpdate() { Repaint(); }

        private void FindReferences()
        {
            _camera = Camera.main;
            if (_camera == null) _camera = FindFirstObjectByType<Camera>();

            _player = FindFirstObjectByType<NovellaPlayer>();
            _launcher = FindFirstObjectByType<StoryLauncher>();

            if (_launcher != null && _player == null) _currentTab = 1;
            else if (_player != null && _currentTab == 1) _currentTab = 0;

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
                            _launcher.MCPrevLookButton = mcPanel.Find("Btn_PrevLook")?.GetComponent<Button>();
                            _launcher.MCNextLookButton = mcPanel.Find("Btn_NextLook")?.GetComponent<Button>();

                            isDirty = true;
                        }
                    }
                }
                if (isDirty) EditorUtility.SetDirty(_launcher);
            }
        }
        private void OnGUI()
        {
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
        }

        private void ManageTempPreviews()
        {
            if (_currentTab != 2) { if (_tempChoicePreview) DestroyImmediate(_tempChoicePreview); if (_tempStoryPreview) DestroyImmediate(_tempStoryPreview); }
            if (_currentTab != 3) { if (_tempCustomPreview) DestroyImmediate(_tempCustomPreview); }

            bool isEditingChoiceContainer = (_currentTab == 0 && _player != null && _activeEditRect == _player.ChoiceContainer?.GetComponent<RectTransform>());
            bool isEditingStoryContainer = (_currentTab == 1 && _launcher != null && _launcher.StoriesContainer != null);

            if (isEditingChoiceContainer || isEditingStoryContainer)
            {
                if (_tempDummyButtons.Count == 0)
                {
                    GameObject prefabToUse = isEditingChoiceContainer ? _player.ChoiceButtonPrefab : _launcher.StoryButtonPrefab;
                    Transform containerToUse = isEditingChoiceContainer ? _player.ChoiceContainer : _launcher.StoriesContainer;

                    if (prefabToUse != null && containerToUse != null)
                    {
                        for (int i = 0; i < 3; i++) _tempDummyButtons.Add(PrefabUtility.InstantiatePrefab(prefabToUse, containerToUse) as GameObject);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(containerToUse.GetComponent<RectTransform>());
                        Canvas.ForceUpdateCanvases();
                    }
                }
            }
            else ClearDummyButtons();
        }

        private void CleanTempPrefabs()
        {
            if (_tempChoicePreview != null) DestroyImmediate(_tempChoicePreview);
            if (_tempStoryPreview != null) DestroyImmediate(_tempStoryPreview);
            if (_tempCustomPreview != null) DestroyImmediate(_tempCustomPreview);
        }

        private void ClearDummyButtons()
        {
            foreach (var btn in _tempDummyButtons) if (btn != null) DestroyImmediate(btn);
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
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(520), GUILayout.ExpandHeight(true));

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

            bool isGameScene = _currentTab == 0;
            Transform rootT = canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;

            GUILayout.Space(15);
            string headerText = isGameScene ? ToolLang.Get("Character Profile (In-Game)", "Профиль персонажа (В игре)") : ToolLang.Get("Main Character Creator", "Создание Главного Героя");
            DrawSectionHeader("👤", headerText);

            Transform mcPanelTransform = rootT.Find("MCCreationPanel");

            if (mcPanelTransform == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Create a panel to allow players to interact with the MC.", "Создайте панель, чтобы игроки могли взаимодействовать с профилем ГГ."), MessageType.Info);
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                if (GUILayout.Button("🛠 " + ToolLang.Get("Generate MC Panel", "Сгенерировать панель ГГ"), GUILayout.Height(35)))
                {
                    GenerateMCCreationPanel(rootT, isGameScene);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                if (_launcher != null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("🦸 " + ToolLang.Get("Base MC Asset:", "SO Персонажа:"), EditorStyles.boldLabel, GUILayout.Width(130));

                    string mcName = _launcher.MainCharacterAsset != null ? _launcher.MainCharacterAsset.name : ToolLang.Get("None (Click to select)", "Не выбран (Нажмите)");
                    string charDir = "Assets/NovellaEngine/Runtime/Data/Characters";

                    if (GUILayout.Button(mcName, EditorStyles.popup))
                    {
                        if (!Directory.Exists(charDir)) Directory.CreateDirectory(charDir);

                        NovellaGalleryWindow.ShowWindow(obj => {
                            NovellaCharacter mc = obj as NovellaCharacter;
                            if (mc != null)
                            {
                                Undo.RecordObject(_launcher, "Assign MC");
                                _launcher.MainCharacterAsset = mc;
                                EditorUtility.SetDirty(_launcher);
                                Repaint();
                            }
                        }, NovellaGalleryWindow.EGalleryFilter.Character, charDir);
                    }

                    if (_launcher.MainCharacterAsset != null)
                    {
                        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                        if (GUILayout.Button("✖", GUILayout.Width(25)))
                        {
                            Undo.RecordObject(_launcher, "Clear MC Asset");
                            _launcher.MainCharacterAsset = null;
                            EditorUtility.SetDirty(_launcher);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                        if (GUILayout.Button("+ " + ToolLang.Get("Create", "Создать"), EditorStyles.miniButton, GUILayout.Width(70)))
                        {
                            if (!Directory.Exists(charDir)) Directory.CreateDirectory(charDir);

                            NovellaCharacter newMc = ScriptableObject.CreateInstance<NovellaCharacter>();
                            newMc.IsPlayerCharacter = true;
                            newMc.CharacterID = "Player";
                            newMc.DisplayName_EN = "Player";
                            newMc.DisplayName_RU = "Игрок";

                            string path = AssetDatabase.GenerateUniqueAssetPath($"{charDir}/MC_Player.asset");
                            AssetDatabase.CreateAsset(newMc, path);
                            AssetDatabase.SaveAssets();

                            Undo.RecordObject(_launcher, "Assign MC");
                            _launcher.MainCharacterAsset = newMc;
                            EditorUtility.SetDirty(_launcher);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    GUILayout.EndHorizontal();

                    if (_launcher.MainCharacterAsset != null)
                    {
                        GUILayout.Space(5);
                        GUILayout.BeginVertical(GUI.skin.box);

                        SerializedObject so = new SerializedObject(_launcher.MainCharacterAsset);
                        so.Update();

                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(so.FindProperty("IsPlayerCharacter"), new GUIContent(ToolLang.Get("Is Main Character", "Это Главный Герой")));
                        GUILayout.Space(5);
                        EditorGUILayout.PropertyField(so.FindProperty("AvailableBaseBodies"), new GUIContent(ToolLang.Get("Base Looks (Sprites)", "Базовые спрайты (Внешность)")), true);

                        if (EditorGUI.EndChangeCheck())
                        {
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(_launcher.MainCharacterAsset);
                        }
                        GUILayout.EndVertical();
                    }
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
                GUILayout.EndVertical();
            }
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

                    if (vlg != null) { spacing = vlg.spacing; align = vlg.childAlignment; DestroyImmediate(vlg); }
                    if (hlg != null) { spacing = hlg.spacing; align = hlg.childAlignment; DestroyImmediate(hlg); }

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

        private void DrawTextEditor(string title, TMP_Text txt, bool isPrefabMode = false)
        {
            if (txt == null) return;
            DrawRectTransformEditor(title, txt.GetComponent<RectTransform>(), isPrefabMode);

            if (_activeEditRect == txt.GetComponent<RectTransform>())
            {
                EditorGUI.BeginChangeCheck();
                DrawTMPTextSettingsBlock(txt, ToolLang.Get("Typography Settings", "Настройки Типографики"));
                if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(txt.gameObject); Canvas.ForceUpdateCanvases(); }
            }
        }
        private void GenerateMCCreationPanel(Transform rootTransform, bool isGameScene)
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

            // АВАТАР ПЕРСОНАЖА (1001 СЛОЙ)
            GameObject avatarObj = new GameObject("AvatarPreview");
            avatarObj.transform.SetParent(panel.transform, false);
            var avRt = avatarObj.AddComponent<RectTransform>();
            avRt.anchorMin = new Vector2(0.5f, 0.5f); avRt.anchorMax = new Vector2(0.5f, 0.5f);
            avRt.anchoredPosition = new Vector2(0, 30);
            avRt.sizeDelta = new Vector2(300, 450);
            var avImg = avatarObj.AddComponent<Image>();
            avImg.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            avImg.preserveAspect = true;

            var avCanvas = avatarObj.AddComponent<Canvas>();
            avCanvas.overrideSorting = true;
            avCanvas.sortingOrder = 1001;

            // СТРЕЛКИ ГЕНЕРИРУЮТСЯ ТОЛЬКО ЕСЛИ МЫ НЕ В ИГРЕ
            if (!isGameScene)
            {
                GameObject btnPrev = new GameObject("Btn_PrevLook");
                btnPrev.transform.SetParent(panel.transform, false);
                var bpRt = btnPrev.AddComponent<RectTransform>();
                bpRt.anchorMin = new Vector2(0.5f, 0.5f); bpRt.anchorMax = new Vector2(0.5f, 0.5f);
                bpRt.anchoredPosition = new Vector2(-300, 0);
                bpRt.sizeDelta = new Vector2(60, 60);
                btnPrev.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
                btnPrev.AddComponent<Button>();

                GameObject txtPrev = new GameObject("Text");
                txtPrev.transform.SetParent(btnPrev.transform, false);
                var tpRt = txtPrev.AddComponent<RectTransform>();
                tpRt.anchorMin = Vector2.zero; tpRt.anchorMax = Vector2.one;
                tpRt.offsetMin = Vector2.zero; tpRt.offsetMax = Vector2.zero;
                var tTmpP = txtPrev.AddComponent<TextMeshProUGUI>();
                tTmpP.text = "<"; tTmpP.fontSize = 40; tTmpP.alignment = TextAlignmentOptions.Center;

                GameObject btnNext = new GameObject("Btn_NextLook");
                btnNext.transform.SetParent(panel.transform, false);
                var bnRt = btnNext.AddComponent<RectTransform>();
                bnRt.anchorMin = new Vector2(0.5f, 0.5f); bnRt.anchorMax = new Vector2(0.5f, 0.5f);
                bnRt.anchoredPosition = new Vector2(300, 0);
                bnRt.sizeDelta = new Vector2(60, 60);
                btnNext.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
                btnNext.AddComponent<Button>();

                GameObject txtNext = new GameObject("Text");
                txtNext.transform.SetParent(btnNext.transform, false);
                var tnRt = txtNext.AddComponent<RectTransform>();
                tnRt.anchorMin = Vector2.zero; tnRt.anchorMax = Vector2.one;
                tnRt.offsetMin = Vector2.zero; tnRt.offsetMax = Vector2.zero;
                var tTmpN = txtNext.AddComponent<TextMeshProUGUI>();
                tTmpN.text = ">"; tTmpN.fontSize = 40; tTmpN.alignment = TextAlignmentOptions.Center;
            }

            // Заголовок
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(panel.transform, false);
            var titleRt = titleObj.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1f); titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0, -50);
            titleRt.sizeDelta = new Vector2(800, 100);
            var titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
            titleTxt.text = isGameScene ? ToolLang.Get("Character Profile", "Профиль персонажа") : ToolLang.Get("Create Your Character", "Создание Персонажа");
            titleTxt.fontSize = 60; titleTxt.alignment = TextAlignmentOptions.Center;

            // Подсказка (Хинт)
            GameObject hintObj = new GameObject("HintText");
            hintObj.transform.SetParent(panel.transform, false);
            var hintRt = hintObj.AddComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0.5f); hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.anchoredPosition = new Vector2(0, -230);
            hintRt.sizeDelta = new Vector2(500, 50);
            var hintTxt = hintObj.AddComponent<TextMeshProUGUI>();
            hintTxt.text = isGameScene ? ToolLang.Get("Current Name:", "Имя персонажа:") : ToolLang.Get("Enter character name:", "Введите имя персонажа:");
            hintTxt.fontSize = 32; hintTxt.alignment = TextAlignmentOptions.Center;
            hintTxt.color = new Color(0.7f, 0.7f, 0.7f);

            // Поле ввода имени (Input Field Base)
            GameObject inputBg = new GameObject("NameInput");
            inputBg.transform.SetParent(panel.transform, false);
            var inRt = inputBg.AddComponent<RectTransform>();
            inRt.anchorMin = new Vector2(0.5f, 0.5f); inRt.anchorMax = new Vector2(0.5f, 0.5f);
            inRt.anchoredPosition = new Vector2(0, -290);
            inRt.sizeDelta = new Vector2(500, 80);
            inputBg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // СОЗДАЕМ МАСКУ, ЧТОБЫ ТЕКСТ НЕ ВЫЛАЗИЛ
            GameObject textAreaObj = new GameObject("TextArea");
            textAreaObj.transform.SetParent(inputBg.transform, false);
            var taRt = textAreaObj.AddComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero; taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(15, 0); taRt.offsetMax = new Vector2(-15, 0);
            textAreaObj.AddComponent<RectMask2D>();

            // Текст внутри маски
            GameObject inputText = new GameObject("Text");
            inputText.transform.SetParent(textAreaObj.transform, false);
            var txtRt = inputText.AddComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero; txtRt.offsetMax = Vector2.zero;
            var inputTMP = inputText.AddComponent<TextMeshProUGUI>();
            inputTMP.fontSize = 40; inputTMP.alignment = TextAlignmentOptions.Center;
            inputTMP.enableWordWrapping = false; // Отключаем перенос строк

            // Плейсхолдер внутри маски
            GameObject placeholderText = new GameObject("Placeholder");
            placeholderText.transform.SetParent(textAreaObj.transform, false);
            var phRt = placeholderText.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero; phRt.offsetMax = Vector2.zero;
            var phTMP = placeholderText.AddComponent<TextMeshProUGUI>();
            phTMP.text = ToolLang.Get("Alex...", "Алекс...");
            phTMP.fontSize = 40; phTMP.alignment = TextAlignmentOptions.Center;
            phTMP.color = new Color(1, 1, 1, 0.3f);
            phTMP.enableWordWrapping = false;

            var inputField = inputBg.AddComponent<TMP_InputField>();
            inputField.textComponent = inputTMP;
            inputField.placeholder = phTMP;
            inputField.characterLimit = 25;
            if (isGameScene) inputField.interactable = false;

            GameObject btnObj = new GameObject("Btn_ConfirmMC");
            btnObj.transform.SetParent(panel.transform, false);
            var brt = btnObj.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(700, 30);
            brt.sizeDelta = new Vector2(400, 80);
            btnObj.AddComponent<Image>().color = new Color(0.2f, 0.8f, 0.4f, 1f);
            btnObj.AddComponent<Button>();

            GameObject btnTxt = new GameObject("Text");
            btnTxt.transform.SetParent(btnObj.transform, false);
            var bTrt = btnTxt.AddComponent<RectTransform>();
            bTrt.anchorMin = Vector2.zero; bTrt.anchorMax = Vector2.one;
            bTrt.offsetMin = Vector2.zero; bTrt.offsetMax = Vector2.zero;
            var bTmp = btnTxt.AddComponent<TextMeshProUGUI>();
            bTmp.text = isGameScene ? ToolLang.Get("Close Profile", "Закрыть профиль") : ToolLang.Get("Start Story", "Начать Историю");
            bTmp.fontSize = 40; bTmp.alignment = TextAlignmentOptions.Center;

            panel.SetActive(false);
            EditorUtility.SetDirty(rootTransform.gameObject);
            FindReferences();
            GUIUtility.ExitGUI();
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
                "Assign a NovellaTree (Graph) here to link this scene to a specific story. The UI will use this graph to load dialogues, characters, and choices.",
                "Назначьте здесь Граф (NovellaTree), чтобы привязать эту сцену к конкретной истории. UI будет брать оттуда диалоги, персонажей и выборы."
            ), MessageType.Info);
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
                        GetWindow<NovellaUIEditorWindow>().Repaint();
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

                GUILayout.Space(10);
                GUIStyle descStyle = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, richText = true, fontSize = 11 };
                GUILayout.Label(ToolLang.Get(
                    "<b>What is a Graph?</b>\nIt's a visual flowchart where you build your story. You saw this in the Tutorial! Connect nodes to create your game flow:\n\n" +
                    "• <b>Dialogue:</b> Displays text and shows characters.\n" +
                    "• <b>Branch:</b> Creates choices for the player.\n" +
                    "• <b>Audio/Scene:</b> Plays music or changes background.\n" +
                    "• <b>Save:</b> A checkpoint to save player's progress.\n" +
                    "и т.д...",

                    "<b>Что такое Граф?</b>\nЭто визуальная блок-схема, где вы строите сюжет (вы уже видели это в Обучении!). Соединяйте ноды (карточки) линиями, чтобы создать игру:\n\n" +
                    "• <b>Диалог:</b> Выводит текст на экран и показывает персонажей.\n" +
                    "• <b>Развилка:</b> Создает кнопки выбора для игрока.\n" +
                    "• <b>Аудио/Сцена:</b> Включает музыку или меняет фон.\n" +
                    "• <b>Чекпоинт:</b> Точка сохранения прогресса игрока.\n" +
                    "etc..."
                ), descStyle);
            }
            GUILayout.EndVertical();
            GUILayout.Space(15);

            if (canvas != null)
            {
                Transform bgTransform = canvas.transform.Find("Background");
                if (bgTransform != null) DrawRectTransformEditor(ToolLang.Get("Background (Canvas)", "Общий Фон (Canvas)"), bgTransform.GetComponent<RectTransform>());
            }

            DrawRectTransformEditor(ToolLang.Get("Dialogue Panel", "Панель Диалога"), _player.DialoguePanel.GetComponent<RectTransform>());
            DrawTextEditor(ToolLang.Get("Speaker Name", "Имя Спикера"), _player.SpeakerNameText);
            DrawTextEditor(ToolLang.Get("Dialogue Text", "Текст Диалога"), _player.DialogueBodyText);
            DrawLayoutContainerEditor(ToolLang.Get("Choices Container", "Контейнер Кнопок"), _player.ChoiceContainer.GetComponent<RectTransform>());

            if (rootCanvas != null)
            {
                DrawSaveLoadPanel(rootCanvas.transform, true);
                DrawMCCreationSection(rootCanvas);
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
                        GetWindow<NovellaUIEditorWindow>().Repaint();
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
                        EditorUtility.SetDirty(img.gameObject); Canvas.ForceUpdateCanvases(); Repaint();
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
                Repaint();
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

        private void DrawMenuUITab()
        {
            if (_launcher == null) { EditorGUILayout.HelpBox(ToolLang.Get("This is not a Main Menu Scene.", "Это не сцена главного меню."), MessageType.Info); return; }

            Canvas canvas = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);
            Canvas rootCanvas = (canvas != null && canvas.rootCanvas != null) ? canvas.rootCanvas : canvas;

            // --- БЛОК ОБЩИХ НАСТРОЕК (С ВЫБОРОМ СЦЕНЫ) ---
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
            GUILayout.EndHorizontal(); // <--- ВОТ ЗАКРЫТИЕ, КОТОРОЕ СПАСАЕТ ВЕСЬ UI
            GUILayout.EndVertical();
            GUILayout.Space(10);
            // ---------------------------------------------

            DrawSectionHeader("📚", ToolLang.Get("Stories to Show", "Истории для показа"));
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(ToolLang.Get("If empty, ALL stories from Resources/Stories will be loaded automatically.", "Если список пуст, автоматически загрузятся ВСЕ истории из папки Resources/Stories."), MessageType.Info);

            for (int i = 0; i < _launcher.SpecificStories.Count; i++)
            {
                var story = _launcher.SpecificStories[i];

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                string sName = story != null ? story.name : ToolLang.Get("Empty (Click to set)", "Пусто (Нажмите для выбора)");

                if (GUILayout.Button("📖 " + sName, EditorStyles.popup))
                {
                    int index = i;
                    NovellaGalleryWindow.ShowWindow(obj => {
                        Undo.RecordObject(_launcher, "Change Story");
                        _launcher.SpecificStories[index] = obj as NovellaStory;
                        EditorUtility.SetDirty(_launcher);
                        GetWindow<NovellaUIEditorWindow>().Repaint();
                    }, NovellaGalleryWindow.EGalleryFilter.Story, "Assets/NovellaEngine/Resources/Stories");
                }

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    Undo.RecordObject(_launcher, "Remove Story");
                    _launcher.SpecificStories.RemoveAt(i);
                    EditorUtility.SetDirty(_launcher);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                if (story != null)
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
                            if (!string.IsNullOrEmpty(_tempStoryName) && _tempStoryName != story.name)
                            {
                                string path = AssetDatabase.GetAssetPath(story);
                                AssetDatabase.RenameAsset(path, _tempStoryName);
                                story.Title = _tempStoryName;
                                EditorUtility.SetDirty(story);
                                AssetDatabase.SaveAssets();
                            }
                            _editingStoryIndex = -1;
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    else
                    {
                        EditorGUILayout.LabelField(story.name, EditorStyles.boldLabel);
                        if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            _editingStoryIndex = i;
                            _tempStoryName = story.name;
                        }
                    }
                    GUILayout.EndHorizontal();

                    if (story.StartingChapter != null)
                    {
                        GUILayout.Space(5);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("↳ " + ToolLang.Get("Linked Graph:", "Подключенный Граф:"), EditorStyles.miniLabel, GUILayout.Width(130));

                        if (_editingGraphIndex == i)
                        {
                            _tempGraphName = EditorGUILayout.TextField(_tempGraphName);
                            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                            if (GUILayout.Button("✔", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                if (!string.IsNullOrEmpty(_tempGraphName) && _tempGraphName != story.StartingChapter.name)
                                {
                                    string path = AssetDatabase.GetAssetPath(story.StartingChapter);
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
                            EditorGUILayout.LabelField(story.StartingChapter.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                _editingGraphIndex = i;
                                _tempGraphName = story.StartingChapter.name;
                            }
                        }

                        GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                        if (GUILayout.Button(ToolLang.Get("Open", "Открыть"), EditorStyles.miniButton, GUILayout.Width(70)))
                        {
                            NovellaGraphWindow.OpenGraphWindow(story.StartingChapter);
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("↳ " + ToolLang.Get("Linked Graph:", "Подключенный Граф:"), EditorStyles.miniLabel, GUILayout.Width(130));
                        EditorGUILayout.HelpBox(ToolLang.Get("Missing!", "Отсутствует!"), MessageType.Error);
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
                            EditorUtility.SetDirty(img.gameObject); img.SetAllDirty(); Canvas.ForceUpdateCanvases(); Repaint();
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
            if (_player != null)
            {
                DrawPrefabManager(ToolLang.Get("Choice Button Asset", "Ассет Кнопки Выбора"), ref _player.ChoiceButtonPrefab, _player, "Assets/NovellaEngine/Runtime/Prefabs");
                if (_player.ChoiceButtonPrefab != null) DrawChoiceButtonPrefabEditor();
            }
            if (_launcher != null)
            {
                GUILayout.Space(20);
                DrawPrefabManager(ToolLang.Get("Story Cover Asset", "Ассет Карточки Истории"), ref _launcher.StoryButtonPrefab, _launcher, "Assets/NovellaEngine/Runtime/Prefabs");
                if (_launcher.StoryButtonPrefab != null) DrawStoryButtonPrefabEditor();
            }
        }

        private void DrawChoiceButtonPrefabEditor()
        {
            if (_tempChoicePreview == null && _player.ChoiceContainer != null)
            {
                _tempChoicePreview = PrefabUtility.InstantiatePrefab(_player.ChoiceButtonPrefab, _player.ChoiceContainer) as GameObject;
                _activeEditRect = _tempChoicePreview.GetComponent<RectTransform>();
            }

            if (_tempChoicePreview == null) return;

            DrawPrefabSaveHeader(_tempChoicePreview, _player.ChoiceButtonPrefab);
            DrawRectTransformEditor(ToolLang.Get("Base Button", "Базовая Кнопка"), _tempChoicePreview.GetComponent<RectTransform>(), true);

            TMP_Text txt = _tempChoicePreview.GetComponentInChildren<TMP_Text>();
            if (txt != null) DrawTextEditor(ToolLang.Get("Button Text", "Текст на кнопке"), txt, true);
        }

        private void DrawStoryButtonPrefabEditor()
        {
            if (_tempStoryPreview == null && _launcher.StoriesContainer != null)
            {
                _tempStoryPreview = PrefabUtility.InstantiatePrefab(_launcher.StoryButtonPrefab, _launcher.StoriesContainer) as GameObject;
                _activeEditRect = _tempStoryPreview.GetComponent<RectTransform>();
            }

            if (_tempStoryPreview == null) return;

            DrawPrefabSaveHeader(_tempStoryPreview, _launcher.StoryButtonPrefab);
            DrawRectTransformEditor(ToolLang.Get("Base Card", "Базовая Карточка"), _tempStoryPreview.GetComponent<RectTransform>(), true);

            var titleTxt = _tempStoryPreview.transform.Find("TitleText")?.GetComponent<TMP_Text>();
            var descTxt = _tempStoryPreview.transform.Find("DescText")?.GetComponent<TMP_Text>();

            if (titleTxt != null) DrawTextEditor(ToolLang.Get("Title Text", "Текст Заголовка"), titleTxt, true);
            if (descTxt != null) DrawTextEditor(ToolLang.Get("Description Text", "Текст Описания"), descTxt, true);
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
                if (handleArea != null) DestroyImmediate(handleArea.gameObject);

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
            DestroyImmediate(root);
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
            Repaint();
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

            DrawRectTransformEditor(ToolLang.Get("Base Element", "Базовый Элемент"), _tempCustomPreview.GetComponent<RectTransform>(), true);

            var customComp = _tempCustomPreview.GetComponent<NovellaCustomUI>();
            if (customComp != null)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUI.BeginChangeCheck();
                customComp.IsDialogueFrame = EditorGUILayout.ToggleLeft(ToolLang.Get(" This is a Dialogue Frame", " Это Рамка Диалога"), customComp.IsDialogueFrame, EditorStyles.boldLabel);

                if (customComp.IsDialogueFrame)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.HelpBox(ToolLang.Get("Select which Text component corresponds to the Speaker Name and Dialogue Body.", "Выберите, какой текстовый компонент отвечает за Имя, а какой за сам Текст."), MessageType.Info);

                    var allTexts = _tempCustomPreview.GetComponentsInChildren<TMP_Text>(true);
                    string[] textNames = new string[allTexts.Length + 1];
                    textNames[0] = ToolLang.Get("[ None ]", "[ Не назначено ]");
                    for (int i = 0; i < allTexts.Length; i++) textNames[i + 1] = allTexts[i].name;

                    int currNameIdx = customComp.OverrideSpeakerName != null ? Array.IndexOf(allTexts, customComp.OverrideSpeakerName) + 1 : 0;
                    int newNameIdx = EditorGUILayout.Popup(ToolLang.Get("Speaker Name Text", "Текст для Имени"), currNameIdx, textNames);
                    if (newNameIdx != currNameIdx) customComp.OverrideSpeakerName = newNameIdx == 0 ? null : allTexts[newNameIdx - 1];

                    int currDialIdx = customComp.OverrideDialogueText != null ? Array.IndexOf(allTexts, customComp.OverrideDialogueText) + 1 : 0;
                    int newDialIdx = EditorGUILayout.Popup(ToolLang.Get("Dialogue Body Text", "Текст для Диалога"), currDialIdx, textNames);
                    if (newDialIdx != currDialIdx) customComp.OverrideDialogueText = newDialIdx == 0 ? null : allTexts[newDialIdx - 1];
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(customComp);
                    PrefabUtility.SaveAsPrefabAsset(_tempCustomPreview, AssetDatabase.GetAssetPath(prefab));
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            var texts = _tempCustomPreview.GetComponentsInChildren<TMP_Text>(true);
            foreach (var txt in texts)
            {
                if (txt.gameObject == _tempCustomPreview) continue;
                DrawTextEditor("↳ 📝 " + txt.name, txt, true);
            }
        }

        private void DrawPrefabManager(string title, ref GameObject prefabRef, UnityEngine.Object dirtyTarget, string defaultSearchDir)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("📦 " + title, EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(ToolLang.Get(
                "Each scene represents a story. Create or duplicate prefabs to give each story a unique style (Modern, Fantasy, etc.)!",
                "Каждая сцена = отдельная история. Копируйте префабы, чтобы задать каждой истории свой уникальный стиль (Сай-фай, Фэнтези и т.д.)!"
            ), MessageType.Info);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Selected Prefab:", "Выбранный префаб:"), GUILayout.Width(130));

            string pName = prefabRef != null ? prefabRef.name : ToolLang.Get("None", "Не выбран");
            if (GUILayout.Button("📦 " + pName, EditorStyles.popup))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    GameObject go = obj as GameObject;
                    if (go != null)
                    {
                        Undo.RecordObject(dirtyTarget, "Change Prefab");
                        if (dirtyTarget is NovellaPlayer p) p.ChoiceButtonPrefab = go;
                        else if (dirtyTarget is StoryLauncher l) l.StoryButtonPrefab = go;

                        EditorUtility.SetDirty(dirtyTarget);
                        CleanTempPrefabs();
                        _activeEditRect = null;
                        _activeDecoRect = null;
                        GetWindow<NovellaUIEditorWindow>().Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Prefab, defaultSearchDir);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("📄 " + ToolLang.Get("Duplicate Active (Clone)", "Сделать Копию (Дубликат)"), EditorStyles.miniButton, GUILayout.Height(25), GUILayout.Width(200)))
            {
                if (prefabRef != null)
                {
                    string path = AssetDatabase.GetAssetPath(prefabRef);
                    string newPath = AssetDatabase.GenerateUniqueAssetPath(path.Replace(".prefab", "_Custom.prefab"));
                    AssetDatabase.CopyAsset(path, newPath);
                    AssetDatabase.Refresh();

                    GameObject newPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
                    if (dirtyTarget is NovellaPlayer p) p.ChoiceButtonPrefab = newPrefab;
                    else if (dirtyTarget is StoryLauncher l) l.StoryButtonPrefab = newPrefab;

                    EditorUtility.SetDirty(dirtyTarget);
                    CleanTempPrefabs();
                    _activeEditRect = null;
                    EditorUtility.DisplayDialog(ToolLang.Get("Success", "Успех"), ToolLang.Get($"Prefab duplicated to:\n{newPath}\n\nYou can select it from the Gallery.", $"Префаб скопирован в:\n{newPath}\n\nВы можете выбрать его через Галерею."), "OK");
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawPrefabSaveHeader(GameObject tempInstance, GameObject originalPrefab)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🛠 " + ToolLang.Get("WYSIWYG Editing Mode", "Режим живого редактирования"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("💾 " + ToolLang.Get("Save Changes to Prefab", "Сохранить изменения в Префаб"), GUILayout.Height(30), GUILayout.Width(250)))
            {
                string path = AssetDatabase.GetAssetPath(originalPrefab);
                PrefabUtility.SaveAsPrefabAsset(tempInstance, path);
                EditorUtility.DisplayDialog(ToolLang.Get("Success", "Успех"), ToolLang.Get("Prefab saved successfully!", "Префаб успешно сохранен и обновлен!"), "OK");
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);
        }

        private void DrawDecorationsBlock(RectTransform parentRect)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("✨ " + ToolLang.Get("Decoration Layers", "Декорации (Слои)"), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            int decCount = 0;
            foreach (Transform t in parentRect) if (t.name.StartsWith("Deco_")) decCount++;

            EditorGUI.BeginDisabledGroup(decCount >= 10);
            if (GUILayout.Button("+ " + ToolLang.Get("Add Layer", "Добавить слой"), EditorStyles.miniButton, GUILayout.Width(120)))
            {
                Undo.RegisterFullObjectHierarchyUndo(parentRect.gameObject, "Add Decoration");
                GameObject dec = new GameObject("Deco_" + (decCount + 1));
                dec.transform.SetParent(parentRect, false);
                dec.transform.SetAsFirstSibling();
                var rt = dec.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

                var img = dec.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0.2f);

                _activeDecoRect = rt;
                EditorUtility.SetDirty(parentRect.gameObject);
                GUIUtility.ExitGUI();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(ToolLang.Get("Layer Z-Index: Use arrows to move layer behind or in front of others.", "Сортировка слоев: Используйте стрелки, чтобы переместить слой на задний или передний план."), MessageType.Info);

            if (parentRect.GetComponent<Image>() != null && !parentRect.name.StartsWith("Deco_"))
            {
                if (GUILayout.Button("⬇ " + ToolLang.Get("Move Base Image to Layer (To put decorations behind)", "Сделать базовый фон слоем (Позволит класть декорации позади)"), EditorStyles.miniButton))
                {
                    var baseImg = parentRect.GetComponent<Image>();
                    GameObject dec = new GameObject("Deco_BaseBackground");
                    dec.transform.SetParent(parentRect, false);
                    dec.transform.SetAsFirstSibling();
                    var rt = dec.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                    var newImg = dec.AddComponent<Image>();
                    EditorUtility.CopySerialized(baseImg, newImg);
                    DestroyImmediate(baseImg);
                    _activeDecoRect = rt;
                    GUIUtility.ExitGUI();
                }
            }

            List<RectTransform> decorations = new List<RectTransform>();
            foreach (Transform child in parentRect) if (child.name.StartsWith("Deco_")) decorations.Add(child as RectTransform);

            if (decorations.Count > 0)
            {
                GUILayout.Space(5);
                foreach (var dec in decorations)
                {
                    if (_activeDecoRect == dec)
                    {
                        GUI.backgroundColor = new Color(0.85f, 1f, 0.85f);
                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        GUI.backgroundColor = Color.white;

                        GUILayout.BeginHorizontal();

                        if (_renamingDecoRect == dec)
                        {
                            _renamingDecoName = EditorGUILayout.TextField(_renamingDecoName, GUILayout.Width(150));
                            if (GUILayout.Button("✔", EditorStyles.miniButton, GUILayout.Width(30)))
                            {
                                Undo.RecordObject(dec.gameObject, "Rename Layer");
                                dec.gameObject.name = _renamingDecoName;
                                _renamingDecoRect = null;
                                GUIUtility.ExitGUI();
                            }
                        }
                        else
                        {
                            GUILayout.Label("↳ 🖼 " + dec.name, EditorStyles.boldLabel);
                            if (GUILayout.Button("✏", EditorStyles.miniButton, GUILayout.Width(25)))
                            {
                                _renamingDecoRect = dec;
                                _renamingDecoName = dec.name;
                            }
                        }

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(25)))
                        {
                            Undo.SetTransformParent(dec, parentRect, "Move Layer Up");
                            int idx = dec.GetSiblingIndex();
                            if (idx > 0) dec.SetSiblingIndex(idx - 1);
                            EditorUtility.SetDirty(parentRect.gameObject);
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(25)))
                        {
                            Undo.SetTransformParent(dec, parentRect, "Move Layer Down");
                            int idx = dec.GetSiblingIndex();
                            dec.SetSiblingIndex(idx + 1);
                            EditorUtility.SetDirty(parentRect.gameObject);
                            GUIUtility.ExitGUI();
                        }

                        GUILayout.Space(10);
                        GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                        if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), EditorStyles.miniButtonRight, GUILayout.Width(70))) _activeDecoRect = null;
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();

                        DrawTransformSettings(dec, false);
                        Image img = dec.GetComponent<Image>();
                        if (img != null)
                        {
                            EditorGUI.BeginChangeCheck();
                            DrawImageSettingsBlock(img);
                            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(img.gameObject);
                        }
                        GUILayout.EndVertical();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("🖼 " + dec.name, EditorStyles.miniButton)) _activeDecoRect = dec;
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.DestroyObjectImmediate(dec.gameObject);
                            if (_activeDecoRect == dec) _activeDecoRect = null;
                            GUIUtility.ExitGUI();
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                    }
                }
            }
            else GUILayout.Label(ToolLang.Get("No extra layers.", "Нет слоев."), EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
        }

        private void DrawImageSettingsBlock(Image img)
        {
            if (img == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("🖼 " + ToolLang.Get("Image & Background", "Изображение и Фон"), EditorStyles.miniBoldLabel);
            GUILayout.Space(5);

            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 110;

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Source Sprite", "Спрайт"), GUILayout.Width(106));
            string spriteName = img.sprite != null ? img.sprite.name : ToolLang.Get("None (Solid Color)", "Пусто (Сплошной цвет)");

            if (GUILayout.Button(spriteName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    Sprite sp = obj as Sprite;
                    if (sp == null && obj is Texture2D) sp = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(obj));
                    if (sp != null)
                    {
                        Undo.RecordObject(img, "Change Sprite"); img.sprite = sp;
                        EditorUtility.SetDirty(img.gameObject); img.SetAllDirty(); Canvas.ForceUpdateCanvases(); Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(img, "Clear Sprite"); img.sprite = null; EditorUtility.SetDirty(img.gameObject); }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            img.color = EditorGUILayout.ColorField(ToolLang.Get("Color / Tint", "Цвет / Оттенок"), img.color);

            img.type = (Image.Type)EditorGUILayout.EnumPopup(ToolLang.Get("Draw Type", "Режим отрисовки"), img.type);

            if (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled)
            {
                img.pixelsPerUnitMultiplier = EditorGUILayout.Slider(ToolLang.Get("Border Multiplier", "Масштаб рамок"), img.pixelsPerUnitMultiplier, 0.1f, 10f);
            }
            else if (img.type == Image.Type.Filled)
            {
                img.fillMethod = (Image.FillMethod)EditorGUILayout.EnumPopup("Fill Method", img.fillMethod);

                string[] originNames = { "Bottom", "Right", "Top", "Left" };
                if (img.fillMethod == Image.FillMethod.Horizontal) originNames = new[] { "Left", "Right" };
                else if (img.fillMethod == Image.FillMethod.Vertical) originNames = new[] { "Bottom", "Top" };

                int oIdx = Mathf.Clamp(img.fillOrigin, 0, originNames.Length - 1);
                img.fillOrigin = EditorGUILayout.Popup("Fill Origin", oIdx, originNames);

                img.fillAmount = EditorGUILayout.Slider("Fill Amount", img.fillAmount, 0f, 1f);
                if (img.fillMethod != Image.FillMethod.Horizontal && img.fillMethod != Image.FillMethod.Vertical)
                    img.fillClockwise = EditorGUILayout.Toggle("Clockwise", img.fillClockwise);
                img.preserveAspect = EditorGUILayout.Toggle("Preserve Aspect", img.preserveAspect);
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(img.gameObject);
                img.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                Repaint();
            }

            EditorGUIUtility.labelWidth = lw;
            GUILayout.EndVertical();
        }

        private void DrawButtonSettingsBlock(Button btn)
        {
            if (btn == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("👆 " + ToolLang.Get("Button Interactions", "Поведение при клике/наведении"), EditorStyles.miniBoldLabel);
            GUILayout.Space(5);
            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 110;

            string[] transNames = { ToolLang.Get("None (Нет)", "Нет (None)"), ToolLang.Get("Color Tint (Смена цвета)", "Цвет (Color Tint)"), ToolLang.Get("Sprite Swap (Смена картинок)", "Спрайты (Sprite Swap)") };
            int currentTransIdx = btn.transition == Selectable.Transition.None ? 0 : btn.transition == Selectable.Transition.ColorTint ? 1 : 2;

            EditorGUI.BeginChangeCheck();
            int newTransIdx = EditorGUILayout.Popup(ToolLang.Get("Transition Mode", "Режим реакции"), currentTransIdx, transNames);
            if (EditorGUI.EndChangeCheck())
            {
                btn.transition = newTransIdx == 0 ? Selectable.Transition.None : newTransIdx == 1 ? Selectable.Transition.ColorTint : Selectable.Transition.SpriteSwap;
            }

            if (btn.transition != Selectable.Transition.None)
            {
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "Normal: Default state\nHighlighted: Mouse hover\nPressed: On click\nSelected: After click / focused\nDisabled: Uninteractable",
                    "Normal: Обычное состояние\nHighlighted: Наведение мыши\nPressed: В момент клика\nSelected: Выбрана (фокус)\nDisabled: Выключена"), MessageType.Info);
            }

            if (btn.transition == Selectable.Transition.ColorTint)
            {
                var cb = btn.colors;
                cb.normalColor = EditorGUILayout.ColorField("Normal Color", cb.normalColor);
                cb.highlightedColor = EditorGUILayout.ColorField("Highlight Color", cb.highlightedColor);
                cb.pressedColor = EditorGUILayout.ColorField("Pressed Color", cb.pressedColor);
                cb.selectedColor = EditorGUILayout.ColorField("Selected Color", cb.selectedColor);
                cb.disabledColor = EditorGUILayout.ColorField("Disabled Color", cb.disabledColor);
                cb.colorMultiplier = EditorGUILayout.FloatField("Multiplier", cb.colorMultiplier);
                cb.fadeDuration = EditorGUILayout.Slider("Fade Duration", cb.fadeDuration, 0f, 1f);
                btn.colors = cb;
            }
            else if (btn.transition == Selectable.Transition.SpriteSwap)
            {
                var ss = btn.spriteState;
                ss.highlightedSprite = (Sprite)EditorGUILayout.ObjectField("Highlight Sprite", ss.highlightedSprite, typeof(Sprite), false);
                ss.pressedSprite = (Sprite)EditorGUILayout.ObjectField("Pressed Sprite", ss.pressedSprite, typeof(Sprite), false);
                ss.selectedSprite = (Sprite)EditorGUILayout.ObjectField("Selected Sprite", ss.selectedSprite, typeof(Sprite), false);
                ss.disabledSprite = (Sprite)EditorGUILayout.ObjectField("Disabled Sprite", ss.disabledSprite, typeof(Sprite), false);
                btn.spriteState = ss;
            }
            EditorGUIUtility.labelWidth = lw;
            GUILayout.EndVertical();
        }

        private void DrawTMPTextSettingsBlock(TMP_Text txt, string headerLabel)
        {
            if (txt == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("📝 " + headerLabel, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("↻ " + ToolLang.Get("Reset", "Сбросить"), EditorStyles.miniButton, GUILayout.Width(70)))
            {
                Undo.RecordObject(txt, "Reset Text Settings");
                Undo.RecordObject(txt.rectTransform, "Reset Text Rect");

                txt.fontSize = 28;
                txt.color = Color.white;
                txt.alignment = TextAlignmentOptions.Center;
                txt.richText = true;
                txt.raycastTarget = false;

                txt.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                txt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                txt.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                txt.rectTransform.sizeDelta = new Vector2(200f, 50f);
                txt.rectTransform.anchoredPosition = Vector2.zero;
                txt.rectTransform.localRotation = Quaternion.identity;
                txt.rectTransform.localScale = Vector3.one;

                txt.ForceMeshUpdate();
                EditorUtility.SetDirty(txt.rectTransform);
                EditorUtility.SetDirty(txt.gameObject);
                Canvas.ForceUpdateCanvases();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 110;

            txt.font = (TMP_FontAsset)EditorGUILayout.ObjectField(ToolLang.Get("Font Asset", "Шрифт"), txt.font, typeof(TMP_FontAsset), false);

            GUILayout.BeginHorizontal();
            txt.fontSize = EditorGUILayout.FloatField(ToolLang.Get("Font Size", "Размер"), txt.fontSize, GUILayout.ExpandWidth(true));
            txt.color = EditorGUILayout.ColorField(GUIContent.none, txt.color, false, true, false, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            txt.richText = EditorGUILayout.ToggleLeft("Rich Text", txt.richText, GUILayout.Width(100));
            txt.raycastTarget = EditorGUILayout.ToggleLeft("Raycast Target", txt.raycastTarget);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Alignment", "Выравнивание"), GUILayout.Width(106));
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(130));
            DrawAlignRow(txt, TextAlignmentOptions.TopLeft, TextAlignmentOptions.Top, TextAlignmentOptions.TopRight, "↖", "↑", "↗");
            DrawAlignRow(txt, TextAlignmentOptions.Left, TextAlignmentOptions.Center, TextAlignmentOptions.Right, "←", "•", "→");
            DrawAlignRow(txt, TextAlignmentOptions.BottomLeft, TextAlignmentOptions.Bottom, TextAlignmentOptions.BottomRight, "↙", "↓", "↘");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("📏 " + ToolLang.Get("Text Margins (Padding)", "Отступы текста от краев"), EditorStyles.miniBoldLabel);

            RectTransform trt = txt.rectTransform;
            GUILayout.BeginHorizontal();
            float left = EditorGUILayout.FloatField("Left (Л)", trt.offsetMin.x);
            float right = EditorGUILayout.FloatField("Right (П)", -trt.offsetMax.x);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            float top = EditorGUILayout.FloatField("Top (В)", -trt.offsetMax.y);
            float bot = EditorGUILayout.FloatField("Bot (Н)", trt.offsetMin.y);
            GUILayout.EndHorizontal();

            trt.offsetMin = new Vector2(left, bot);
            trt.offsetMax = new Vector2(-right, -top);

            EditorGUIUtility.labelWidth = lw;
            GUILayout.EndVertical();
        }

        private void DrawAlignRow(TMP_Text txt, TextAlignmentOptions a1, TextAlignmentOptions a2, TextAlignmentOptions a3, string l1, string l2, string l3)
        {
            GUILayout.BeginHorizontal();
            if (DrawAlignBtn(txt.alignment == a1, l1)) { Undo.RecordObject(txt, "Align"); txt.alignment = a1; EditorUtility.SetDirty(txt); }
            if (DrawAlignBtn(txt.alignment == a2, l2)) { Undo.RecordObject(txt, "Align"); txt.alignment = a2; EditorUtility.SetDirty(txt); }
            if (DrawAlignBtn(txt.alignment == a3, l3)) { Undo.RecordObject(txt, "Align"); txt.alignment = a3; EditorUtility.SetDirty(txt); }
            GUILayout.EndHorizontal();
        }

        private bool DrawAlignBtn(bool active, string label)
        {
            GUI.backgroundColor = active ? new Color(0.4f, 0.8f, 1f) : Color.white;
            bool clicked = GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(35), GUILayout.Height(25));
            GUI.backgroundColor = Color.white;
            return clicked;
        }

        private void DrawRectTransformEditor(string title, RectTransform rect, bool isPrefabMode = false, bool showDeleteBtn = false)
        {
            if (rect == null) return;
            bool isActive = _activeEditRect == rect;
            bool isDecoration = rect.name.StartsWith("Deco_");
            if (isDecoration) title = "↳ 🖼 " + rect.name;

            GUI.backgroundColor = isActive ? new Color(0.85f, 1f, 0.85f) : Color.white;
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            GUILayout.BeginHorizontal();
            GUILayout.Label(title, isDecoration ? EditorStyles.label : EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (isActive)
            {
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), EditorStyles.miniButton, GUILayout.Width(70))) _activeEditRect = null;
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button(ToolLang.Get("✏ Edit", "✏ Настроить"), EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    _activeEditRect = rect;
                    _activeDecoRect = null;
                }
                GUI.backgroundColor = Color.white;
            }

            if (showDeleteBtn)
            {
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                {
                    if (_activeEditRect != null && _activeEditRect.gameObject == rect.gameObject) { _activeEditRect = null; _activeDecoRect = null; }
                    Undo.DestroyObjectImmediate(rect.gameObject);
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndHorizontal();

            if (isActive)
            {
                DrawTransformSettings(rect, false);

                Image img = rect.GetComponent<Image>();
                if (img != null)
                {
                    GUILayout.Space(10);
                    EditorGUI.BeginChangeCheck();
                    DrawImageSettingsBlock(img);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(img.gameObject);
                }

                Button btn = rect.GetComponent<Button>();
                if (btn != null)
                {
                    GUILayout.Space(10);
                    EditorGUI.BeginChangeCheck();
                    DrawButtonSettingsBlock(btn);
                    if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(btn.gameObject);
                }

                if (!isDecoration && rect.GetComponent<TMP_Text>() == null)
                {
                    GUILayout.Space(10);
                    DrawDecorationsBlock(rect);
                }
            }

            GUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawTransformSettings(RectTransform rect, bool skipAnchorAndOffsets = false)
        {
            GUILayout.Space(10);

            bool isControlledByLayout = rect.parent != null && rect.parent.GetComponent<LayoutGroup>() != null;

            if (!skipAnchorAndOffsets)
            {
                if (isControlledByLayout)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Anchors and Position are controlled by the parent Layout Group.", "Якоря и позиция жестко контролируются родительским Layout Group."), MessageType.Info);
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    DrawAnchorGrid(rect);
                    GUILayout.BeginVertical();
                }
            }

            EditorGUI.BeginChangeCheck();
            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 55;

            GUILayout.Label(ToolLang.Get("Position & Size", "Позиция и Размеры"), EditorStyles.miniBoldLabel);

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(isControlledByLayout);
            Vector2 pos = EditorGUILayout.Vector2Field("", rect.anchoredPosition);
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            var csf = rect.GetComponent<ContentSizeFitter>();
            bool isSizeDriven = csf != null && (csf.horizontalFit != ContentSizeFitter.FitMode.Unconstrained || csf.verticalFit != ContentSizeFitter.FitMode.Unconstrained);

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(isSizeDriven || isControlledByLayout);
            Vector2 size = EditorGUILayout.Vector2Field("", rect.sizeDelta);
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            if (isSizeDriven) GUILayout.Label(ToolLang.Get("Size is auto-driven by content.", "Размер подстраивается автоматически."), EditorStyles.centeredGreyMiniLabel);

            EditorGUIUtility.labelWidth = lw;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rect, "Change UI Layout");
                if (!isControlledByLayout) rect.anchoredPosition = pos;
                if (!isSizeDriven && !isControlledByLayout) rect.sizeDelta = size;
                EditorUtility.SetDirty(rect.gameObject);
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                Canvas.ForceUpdateCanvases();
            }

            if (!skipAnchorAndOffsets && !isControlledByLayout)
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawAnchorGrid(RectTransform rect)
        {
            GUILayout.BeginVertical(GUILayout.Width(110));
            GUILayout.Label(ToolLang.Get("Anchor Presets", "Пресеты Якорей"), EditorStyles.centeredGreyMiniLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↖", EditorStyles.miniButtonLeft, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0, 1, 0, 1);
            if (GUILayout.Button("↑", EditorStyles.miniButtonMid, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0.5f, 1, 0.5f, 1);
            if (GUILayout.Button("↗", EditorStyles.miniButtonRight, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 1, 1, 1, 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("←", EditorStyles.miniButtonLeft, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0, 0.5f, 0, 0.5f);
            if (GUILayout.Button("•", EditorStyles.miniButtonMid, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0.5f, 0.5f, 0.5f, 0.5f);
            if (GUILayout.Button("→", EditorStyles.miniButtonRight, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 1, 0.5f, 1, 0.5f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↙", EditorStyles.miniButtonLeft, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0, 0, 0, 0);
            if (GUILayout.Button("↓", EditorStyles.miniButtonMid, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 0.5f, 0, 0.5f, 0);
            if (GUILayout.Button("↘", EditorStyles.miniButtonRight, GUILayout.Width(35), GUILayout.Height(25))) SetAnchor(rect, 1, 0, 1, 0);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("[ ] " + ToolLang.Get("Stretch All", "Растянуть"), EditorStyles.miniButton, GUILayout.Width(109))) SetAnchor(rect, 0, 0, 1, 1);

            GUILayout.EndVertical();
        }

        private void SetAnchor(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            if (rect == null) return;
            Undo.RecordObject(rect, "Apply Anchor Preset");

            float w = Mathf.Abs(rect.rect.width);
            float h = Mathf.Abs(rect.rect.height);

            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);

            rect.pivot = new Vector2(
                minX == maxX ? minX : 0.5f,
                minY == maxY ? minY : 0.5f
            );

            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Vector2 newSize = Vector2.zero;
            if (minX == maxX) newSize.x = w;
            if (minY == maxY) newSize.y = h;
            rect.sizeDelta = newSize;

            rect.anchoredPosition = Vector2.zero;

            rect.localScale = new Vector3(Mathf.Abs(rect.localScale.x), Mathf.Abs(rect.localScale.y), Mathf.Abs(rect.localScale.z));

            EditorUtility.SetDirty(rect.gameObject);
            if (rect.parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect.parent as RectTransform);
            Canvas.ForceUpdateCanvases();
        }

        private void DrawPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("🎥 " + ToolLang.Get("Live Preview", "Живой Предпросмотр"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            _showBounds = GUILayout.Toggle(_showBounds, ToolLang.Get(" Show Green Bounds", " Зеленые рамки"), EditorStyles.toolbarButton);
            GUILayout.Space(15);

            GUILayout.Label("🔍 " + ToolLang.Get("Zoom:", "Лупа:"), EditorStyles.miniLabel);
            _uiZoom = GUILayout.HorizontalSlider(_uiZoom, 0.5f, 4f, GUILayout.Width(100));
            GUILayout.Label($"{(_uiZoom * 100):F0}%", EditorStyles.miniLabel, GUILayout.Width(40));

            GUILayout.Space(10);
            if (GUILayout.Button("↻ " + ToolLang.Get("Refresh", "Обновить"), EditorStyles.toolbarButton)) FindReferences();
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            Rect previewRect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint && _camera != null)
            {
                if (_previewTexture == null || _previewTexture.width != 1920 || _previewTexture.height != 1080)
                {
                    if (_previewTexture != null) _previewTexture.Release();
                    _previewTexture = new RenderTexture(1920, 1080, 24);
                    _previewTexture.antiAliasing = 4;
                }

                RenderTexture oldTarget = _camera.targetTexture;
                _camera.targetTexture = _previewTexture;
                _camera.Render();
                _camera.targetTexture = oldTarget;

                GUI.Box(previewRect, GUIContent.none, EditorStyles.helpBox);
                GUI.BeginClip(previewRect);

                float texAspect = 1920f / 1080f;
                float rectAspect = previewRect.width / previewRect.height;
                float drawW = previewRect.width;
                float drawH = previewRect.height;

                if (texAspect > rectAspect) drawH = drawW / texAspect;
                else drawW = drawH * texAspect;

                drawW *= _uiZoom;
                drawH *= _uiZoom;

                Rect localDrawRect = new Rect(
                    (previewRect.width - drawW) / 2f,
                    (previewRect.height - drawH) / 2f,
                    drawW, drawH
                );

                GUI.DrawTexture(localDrawRect, _previewTexture, ScaleMode.StretchToFill, false);

                if (_showBounds && _activeEditRect != null) DrawActiveRectBounds(localDrawRect);

                GUI.EndClip();
            }
            else if (_camera == null)
            {
                GUI.Box(previewRect, ToolLang.Get("Camera not found.", "Камера не найдена."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
            }

            GUILayout.EndVertical();
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
    }
}