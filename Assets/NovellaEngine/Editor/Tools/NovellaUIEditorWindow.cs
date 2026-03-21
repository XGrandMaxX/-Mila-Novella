using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Runtime;
using System.Collections.Generic;
using NovellaEngine.Data;

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

        private GameObject _tempChoicePreview;
        private GameObject _tempStoryPreview;
        private List<GameObject> _tempDummyButtons = new List<GameObject>();

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaUIEditorWindow>(ToolLang.Get("UI Editor", "Редактор UI"));
            win.minSize = new Vector2(1000, 600);
            win.Show();
        }

        private void OnEnable()
        {
            _tabs = new string[] { "🎮 " + ToolLang.Get("Game UI", "Игровой UI"), "📱 " + ToolLang.Get("Menu UI", "Меню UI"), "💠 " + ToolLang.Get("Prefabs", "Префабы"), "🎥 " + ToolLang.Get("Camera", "Камера") };
            FindReferences();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanTempPrefabs();
            ClearDummyButtons();
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                DestroyImmediate(_previewTexture);
            }
        }

        private void OnEditorUpdate() { Repaint(); }

        private void FindReferences()
        {
            _camera = Camera.main;
            if (_camera == null) return;

            _player = FindFirstObjectByType<NovellaPlayer>();
            _launcher = FindFirstObjectByType<StoryLauncher>();

            if (_launcher != null && _player == null) _currentTab = 1;
            else if (_player != null) _currentTab = 0;

            Canvas c = null;
            if (_player != null && _player.DialoguePanel != null) c = _player.DialoguePanel.GetComponentInParent<Canvas>();
            else if (_launcher != null && _launcher.StoriesContainer != null) c = _launcher.StoriesContainer.GetComponentInParent<Canvas>();

            if (c != null && c.renderMode != RenderMode.ScreenSpaceCamera)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = _camera;
                c.planeDistance = 5f;
                EditorUtility.SetDirty(c);
            }
        }

        private void OnGUI()
        {
            if (_player == null && _launcher == null)
            {
                GUILayout.Space(20);
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "UI Elements not found! Please use Scene Manager to setup the scene first.",
                    "Элементы интерфейса не найдены! Используйте Менеджер Сцен для настройки."
                ), MessageType.Warning);

                GUILayout.Space(10);
                if (GUILayout.Button(ToolLang.Get("Find References Again", "Найти ссылки заново"), GUILayout.Height(30)))
                    FindReferences();

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
            if (_currentTab != 2) CleanTempPrefabs();

            bool isEditingChoiceContainer = (_currentTab == 0 && _player != null && _activeEditRect == _player.ChoiceContainer?.GetComponent<RectTransform>());
            bool isEditingStoryContainer = (_currentTab == 1 && _launcher != null && _activeEditRect == _launcher.StoriesContainer?.GetComponent<RectTransform>());

            if (isEditingChoiceContainer || isEditingStoryContainer)
            {
                if (_tempDummyButtons.Count == 0)
                {
                    GameObject prefabToUse = isEditingChoiceContainer ? _player.ChoiceButtonPrefab : _launcher.StoryButtonPrefab;
                    Transform containerToUse = isEditingChoiceContainer ? _player.ChoiceContainer : _launcher.StoriesContainer;

                    if (prefabToUse != null && containerToUse != null)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            GameObject btn = PrefabUtility.InstantiatePrefab(prefabToUse, containerToUse) as GameObject;
                            _tempDummyButtons.Add(btn);
                        }
                        LayoutRebuilder.ForceRebuildLayoutImmediate(containerToUse.GetComponent<RectTransform>());
                        Canvas.ForceUpdateCanvases();
                    }
                }
            }
            else
            {
                ClearDummyButtons();
            }
        }

        private void CleanTempPrefabs()
        {
            if (_tempChoicePreview != null) { DestroyImmediate(_tempChoicePreview); _tempChoicePreview = null; }
            if (_tempStoryPreview != null) { DestroyImmediate(_tempStoryPreview); _tempStoryPreview = null; }
        }

        private void ClearDummyButtons()
        {
            foreach (var btn in _tempDummyButtons) if (btn != null) DestroyImmediate(btn);
            _tempDummyButtons.Clear();
        }

        private void DrawSettingsPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(420), GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            _currentTab = GUILayout.Toolbar(_currentTab, _tabs, GUILayout.Height(30));
            if (EditorGUI.EndChangeCheck()) _activeEditRect = null;

            GUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            if (_currentTab == 0) DrawGameplayUITab();
            else if (_currentTab == 1) DrawMenuUITab();
            else if (_currentTab == 2) DrawPrefabsTab();
            else if (_currentTab == 3) DrawCameraTab();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawGameplayUITab()
        {
            if (_player == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("This is not a Gameplay Scene.", "Это не игровая сцена."), MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(ToolLang.Get("Click 'Edit' to see object bounds and unlock its settings.", "Нажмите 'Редактировать', чтобы увидеть границы объекта и разблокировать его настройки."), MessageType.Info);
            GUILayout.Space(5);

            Transform bgTransform = _player.DialoguePanel.transform.parent.Find("Background");
            if (bgTransform != null) DrawRectTransformEditor(ToolLang.Get("Background (Canvas)", "Общий Фон (Canvas)"), bgTransform.GetComponent<RectTransform>());

            DrawRectTransformEditor(ToolLang.Get("Dialogue Panel", "Панель Диалога"), _player.DialoguePanel.GetComponent<RectTransform>());
            DrawTextEditor(ToolLang.Get("Speaker Name", "Имя Спикера"), _player.SpeakerNameText);
            DrawTextEditor(ToolLang.Get("Dialogue Text", "Текст Диалога"), _player.DialogueBodyText);
            DrawLayoutContainerEditor(ToolLang.Get("Choices Container", "Контейнер Кнопок"), _player.ChoiceContainer.GetComponent<RectTransform>());
        }

        private void DrawMenuUITab()
        {
            if (_launcher == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("This is not a Main Menu Scene.", "Это не сцена главного меню."), MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(ToolLang.Get("Click 'Edit' to set up Menu UI layout.", "Нажмите 'Редактировать' для настройки меню."), MessageType.Info);
            GUILayout.Space(5);

            Transform bgTransform = _launcher.StoriesContainer.parent.Find("Background");
            if (bgTransform != null) DrawRectTransformEditor(ToolLang.Get("Menu Background", "Фон Меню"), bgTransform.GetComponent<RectTransform>());

            DrawLayoutContainerEditor(ToolLang.Get("Stories Carousel", "Контейнер Историй"), _launcher.StoriesContainer.GetComponent<RectTransform>());
        }

        private void DrawPrefabsTab()
        {
            if (_player != null && _player.ChoiceButtonPrefab != null)
            {
                DrawChoiceButtonPrefabEditor();
                GUILayout.Space(20);
            }

            if (_launcher != null && _launcher.StoryButtonPrefab != null)
            {
                DrawStoryButtonPrefabEditor();
            }

            if (_player == null && _launcher == null)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("No prefabs to edit on this scene.", "На этой сцене нет префабов для редактирования."), MessageType.Info);
            }
        }

        private void DrawChoiceButtonPrefabEditor()
        {
            GameObject btnPrefab = _player.ChoiceButtonPrefab;
            if (_tempChoicePreview == null && _player.ChoiceContainer != null)
                _tempChoicePreview = PrefabUtility.InstantiatePrefab(btnPrefab, _player.ChoiceContainer) as GameObject;

            if (_tempChoicePreview != null) _activeEditRect = _tempChoicePreview.GetComponent<RectTransform>();

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔘 " + ToolLang.Get("Choice Button (Game)", "Кнопка Выбора (Игра)"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select Asset", EditorStyles.miniButton)) Selection.activeObject = btnPrefab;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            RectTransform rt = btnPrefab.GetComponent<RectTransform>();
            TMP_Text txt = btnPrefab.GetComponentInChildren<TMP_Text>();
            Image bgImg = btnPrefab.GetComponent<Image>();

            EditorGUI.BeginChangeCheck();

            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 140;

            GUILayout.Label(ToolLang.Get("Dimensions", "Размеры"), EditorStyles.miniBoldLabel);
            Vector2 size = EditorGUILayout.Vector2Field(ToolLang.Get("Width / Height", "Ширина / Высота"), rt.sizeDelta);
            Color bgColor = EditorGUILayout.ColorField(ToolLang.Get("Background Color", "Цвет фона кнопки"), bgImg.color);

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Text Content", "Текстовое наполнение"), EditorStyles.miniBoldLabel);
            float fontSize = EditorGUILayout.FloatField(ToolLang.Get("Font Size", "Размер шрифта"), txt.fontSize);
            Color txtColor = EditorGUILayout.ColorField(ToolLang.Get("Text Color", "Цвет текста"), txt.color);

            EditorGUIUtility.labelWidth = lw;

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rt, "Change Prefab Dimensions");
                Undo.RecordObject(txt, "Change Prefab Text");
                Undo.RecordObject(bgImg, "Change Prefab BG");

                rt.sizeDelta = size;
                bgImg.color = bgColor;
                txt.fontSize = fontSize; txt.color = txtColor;

                EditorUtility.SetDirty(btnPrefab);
                EditorUtility.SetDirty(txt);
                EditorUtility.SetDirty(bgImg);

                if (_tempChoicePreview != null)
                {
                    _tempChoicePreview.GetComponent<RectTransform>().sizeDelta = size;
                    _tempChoicePreview.GetComponent<Image>().color = bgColor;
                    var tempTxt = _tempChoicePreview.GetComponentInChildren<TMP_Text>();
                    tempTxt.fontSize = fontSize; tempTxt.color = txtColor;

                    LayoutRebuilder.ForceRebuildLayoutImmediate(_tempChoicePreview.GetComponent<RectTransform>());
                    Canvas.ForceUpdateCanvases();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawStoryButtonPrefabEditor()
        {
            GameObject btnPrefab = _launcher.StoryButtonPrefab;
            if (_tempStoryPreview == null && _launcher.StoriesContainer != null)
                _tempStoryPreview = PrefabUtility.InstantiatePrefab(btnPrefab, _launcher.StoriesContainer) as GameObject;

            if (_tempStoryPreview != null) _activeEditRect = _tempStoryPreview.GetComponent<RectTransform>();

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label("📖 " + ToolLang.Get("Story Cover Button (Menu)", "Кнопка Истории (Меню)"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select Asset", EditorStyles.miniButton)) Selection.activeObject = btnPrefab;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            RectTransform rt = btnPrefab.GetComponent<RectTransform>();
            Image bgImg = btnPrefab.GetComponent<Image>();

            Transform coverTr = btnPrefab.transform.Find("CoverImage");
            Transform titleTr = btnPrefab.transform.Find("TitleText");
            Transform descTr = btnPrefab.transform.Find("DescText");

            TMP_Text titleTxt = titleTr?.GetComponent<TMP_Text>();
            TMP_Text descTxt = descTr?.GetComponent<TMP_Text>();

            EditorGUI.BeginChangeCheck();
            float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 140;

            GUILayout.Label(ToolLang.Get("Card Dimensions", "Размеры Карточки"), EditorStyles.miniBoldLabel);
            Vector2 size = EditorGUILayout.Vector2Field(ToolLang.Get("Width / Height", "Ширина / Высота"), rt.sizeDelta);
            Color bgColor = EditorGUILayout.ColorField(ToolLang.Get("Card Color", "Цвет Карточки"), bgImg.color);

            float titleSize = titleTxt != null ? titleTxt.fontSize : 24f;
            Color titleColor = titleTxt != null ? titleTxt.color : Color.white;

            float descSize = descTxt != null ? descTxt.fontSize : 18f;
            Color descColor = descTxt != null ? descTxt.color : Color.gray;

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Typography", "Типографика"), EditorStyles.miniBoldLabel);
            if (titleTxt != null)
            {
                titleSize = EditorGUILayout.FloatField(ToolLang.Get("Title Font Size", "Размер Заголовка"), titleSize);
                titleColor = EditorGUILayout.ColorField(ToolLang.Get("Title Color", "Цвет Заголовка"), titleColor);
            }
            if (descTxt != null)
            {
                descSize = EditorGUILayout.FloatField(ToolLang.Get("Desc Font Size", "Размер Описания"), descSize);
                descColor = EditorGUILayout.ColorField(ToolLang.Get("Desc Color", "Цвет Описания"), descColor);
            }

            EditorGUIUtility.labelWidth = lw;

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rt, "Change Story Prefab");
                Undo.RecordObject(bgImg, "Change Story BG");
                if (titleTxt != null) Undo.RecordObject(titleTxt, "Change Title");
                if (descTxt != null) Undo.RecordObject(descTxt, "Change Desc");

                rt.sizeDelta = size;
                bgImg.color = bgColor;
                if (titleTxt != null) { titleTxt.fontSize = titleSize; titleTxt.color = titleColor; EditorUtility.SetDirty(titleTxt); }
                if (descTxt != null) { descTxt.fontSize = descSize; descTxt.color = descColor; EditorUtility.SetDirty(descTxt); }

                EditorUtility.SetDirty(btnPrefab);
                EditorUtility.SetDirty(bgImg);

                if (_tempStoryPreview != null)
                {
                    _tempStoryPreview.GetComponent<RectTransform>().sizeDelta = size;
                    _tempStoryPreview.GetComponent<Image>().color = bgColor;

                    var tTxt = _tempStoryPreview.transform.Find("TitleText")?.GetComponent<TMP_Text>();
                    var dTxt = _tempStoryPreview.transform.Find("DescText")?.GetComponent<TMP_Text>();

                    if (tTxt != null) { tTxt.fontSize = titleSize; tTxt.color = titleColor; }
                    if (dTxt != null) { dTxt.fontSize = descSize; dTxt.color = descColor; }

                    LayoutRebuilder.ForceRebuildLayoutImmediate(_tempStoryPreview.GetComponent<RectTransform>());
                    Canvas.ForceUpdateCanvases();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawCameraTab()
        {
            if (_camera == null) return;

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("🎥 " + ToolLang.Get("Camera Settings", "Настройки Камеры"), EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUI.BeginChangeCheck();
            float orthoSize = EditorGUILayout.Slider(ToolLang.Get("Zoom (Orthographic Size)", "Охват (Zoom)"), _camera.orthographicSize, 1f, 20f);
            Color bgColor = EditorGUILayout.ColorField(ToolLang.Get("Background Color", "Цвет Фона (За сценой)"), _camera.backgroundColor);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_camera, "Change Camera");
                _camera.orthographicSize = orthoSize;
                _camera.backgroundColor = bgColor;
                EditorUtility.SetDirty(_camera.gameObject);
                Repaint();
            }
            GUILayout.EndVertical();
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

        private void DrawRectTransformEditor(string title, RectTransform rect)
        {
            if (rect == null) return;

            bool isActive = _activeEditRect == rect;
            GUI.backgroundColor = isActive ? new Color(0.85f, 1f, 0.85f) : Color.white;
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            GUILayout.BeginHorizontal();
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (isActive)
            {
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                if (GUILayout.Button(ToolLang.Get("Done", "Готово"), EditorStyles.miniButton, GUILayout.Width(70))) _activeEditRect = null;
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button(ToolLang.Get("✏ Edit", "✏ Редактировать"), EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    _activeEditRect = rect;
                    Selection.activeGameObject = rect.gameObject;
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            if (isActive)
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                DrawAnchorGrid(rect);

                GUILayout.BeginVertical();
                EditorGUI.BeginChangeCheck();
                float lw = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 55;

                GUILayout.Label(ToolLang.Get("Position & Size", "Позиция и Размеры"), EditorStyles.miniBoldLabel);

                GUILayout.BeginHorizontal();
                Vector2 pos = EditorGUILayout.Vector2Field("", rect.anchoredPosition);
                GUILayout.EndHorizontal();

                var csf = rect.GetComponent<ContentSizeFitter>();
                bool isSizeDriven = csf != null && (csf.horizontalFit != ContentSizeFitter.FitMode.Unconstrained || csf.verticalFit != ContentSizeFitter.FitMode.Unconstrained);

                GUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(isSizeDriven);
                Vector2 size = EditorGUILayout.Vector2Field("", rect.sizeDelta);
                EditorGUI.EndDisabledGroup();
                GUILayout.EndHorizontal();

                if (isSizeDriven) GUILayout.Label(ToolLang.Get("Size is auto-driven by content.", "Размер подстраивается автоматически."), EditorStyles.centeredGreyMiniLabel);

                EditorGUIUtility.labelWidth = lw;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(rect, "Change UI Layout");
                    rect.anchoredPosition = pos;
                    if (!isSizeDriven) rect.sizeDelta = size;
                    EditorUtility.SetDirty(rect.gameObject);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    Canvas.ForceUpdateCanvases();
                }

                Image img = rect.GetComponent<Image>();
                if (img != null)
                {
                    GUILayout.Space(5);
                    GUILayout.Label(ToolLang.Get("Image Settings", "Настройки Изображения"), EditorStyles.miniBoldLabel);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(50));
                    string spriteName = img.sprite != null ? img.sprite.name : ToolLang.Get("None", "Пусто");

                    if (GUILayout.Button("🖼 " + spriteName, EditorStyles.popup, GUILayout.ExpandWidth(true)))
                    {
                        NovellaGalleryWindow.ShowWindow(obj => {
                            Sprite sp = obj as Sprite;
                            if (sp == null && obj is Texture2D)
                            {
                                string path = AssetDatabase.GetAssetPath(obj);
                                sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                            }

                            if (sp != null)
                            {
                                Undo.RecordObject(img, "Change Sprite");
                                img.sprite = sp;
                                EditorUtility.SetDirty(img.gameObject);
                            }
                        }, NovellaGalleryWindow.EGalleryFilter.Image);
                    }

                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                    if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(img, "Clear Sprite"); img.sprite = null; EditorUtility.SetDirty(img.gameObject); }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);

                    GUILayout.BeginHorizontal();
                    EditorGUIUtility.labelWidth = 50;
                    EditorGUI.BeginChangeCheck();
                    Color newCol = EditorGUILayout.ColorField(ToolLang.Get("Color", "Цвет"), img.color, GUILayout.ExpandWidth(true));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(img, "Change Color");
                        img.color = newCol;
                        EditorUtility.SetDirty(img.gameObject);
                    }
                    EditorGUIUtility.labelWidth = lw;
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(5);
        }

        private void DrawTextEditor(string title, TMP_Text txt)
        {
            if (txt == null) return;
            DrawRectTransformEditor(title, txt.GetComponent<RectTransform>());

            if (_activeEditRect == txt.GetComponent<RectTransform>())
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(ToolLang.Get("Text Settings", "Настройки Текста"), EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                float fontSize = EditorGUILayout.FloatField(ToolLang.Get("Font Size", "Размер Шрифта"), txt.fontSize);
                Color color = EditorGUILayout.ColorField(ToolLang.Get("Text Color", "Цвет"), txt.color);

                GUILayout.BeginHorizontal();
                bool rich = EditorGUILayout.ToggleLeft("Rich Text", txt.richText, GUILayout.Width(100));
                bool raycast = EditorGUILayout.ToggleLeft("Raycast Target", txt.raycastTarget);
                GUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(txt, "Change Text Settings");
                    txt.fontSize = fontSize; txt.color = color;
                    txt.raycastTarget = raycast; txt.richText = rich;
                    EditorUtility.SetDirty(txt.gameObject);
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
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
            Undo.RecordObject(rect, "Apply Anchor Preset");

            float currentWidth = rect.rect.width;
            float currentHeight = rect.rect.height;

            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);

            if (minX == maxX && minY == maxY)
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, currentWidth);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, currentHeight);
                rect.anchoredPosition = Vector2.zero;
            }
            else
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                if (minX == maxX)
                {
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, currentWidth);
                    rect.anchoredPosition = new Vector2(0, rect.anchoredPosition.y);
                }
                else if (minY == maxY)
                {
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, currentHeight);
                    rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0);
                }
            }

            EditorUtility.SetDirty(rect.gameObject);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Canvas.ForceUpdateCanvases();
        }

        private void DrawPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal();
            GUILayout.Label("🎥 " + ToolLang.Get("Live Camera Preview", "Живой Предпросмотр с Камеры"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ " + ToolLang.Get("Refresh", "Обновить"), EditorStyles.miniButton)) FindReferences();
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
                GUI.DrawTexture(previewRect, _previewTexture, ScaleMode.ScaleToFit, false);

                if (_activeEditRect != null) DrawActiveRectBounds(previewRect);
            }
            else if (_camera == null)
            {
                GUI.Box(previewRect, ToolLang.Get("Camera not found.", "Камера не найдена."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
            }

            GUILayout.EndVertical();
        }

        private void DrawActiveRectBounds(Rect previewRect)
        {
            if (_activeEditRect == null || _camera == null || _previewTexture == null) return;

            Vector3[] worldCorners = new Vector3[4];
            _activeEditRect.GetWorldCorners(worldCorners);

            float texAspect = 1920f / 1080f;
            float rectAspect = previewRect.width / previewRect.height;

            float drawW = previewRect.width;
            float drawH = previewRect.height;
            float offsetX = 0; float offsetY = 0;

            if (texAspect > rectAspect)
            {
                drawH = previewRect.width / texAspect;
                offsetY = (previewRect.height - drawH) / 2f;
            }
            else
            {
                drawW = previewRect.height * texAspect;
                offsetX = (previewRect.width - drawW) / 2f;
            }

            Vector2[] screenPts = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 vp = _camera.WorldToViewportPoint(worldCorners[i]);
                screenPts[i] = new Vector2(
                    previewRect.x + offsetX + vp.x * drawW,
                    previewRect.y + offsetY + (1f - vp.y) * drawH
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