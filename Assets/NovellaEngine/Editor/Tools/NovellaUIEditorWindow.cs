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
        private RectTransform _activeDecoRect; // ФИКС: Отдельная переменная для слоев-декораций

        private GameObject _tempChoicePreview;
        private GameObject _tempStoryPreview;
        private List<GameObject> _tempDummyButtons = new List<GameObject>();

        private bool _showBounds = true;
        private float _uiZoom = 1f;

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaUIEditorWindow>(ToolLang.Get("UI Master Forge", "Кузница UI"));
            win.minSize = new Vector2(1200, 700);
            win.Show();
        }

        private void OnEnable()
        {
            _tabs = new string[] { "🎮 " + ToolLang.Get("Game UI", "Игровой UI"), "📱 " + ToolLang.Get("Menu UI", "Меню UI"), "💠 " + ToolLang.Get("Prefabs", "Префабы") };
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
            if (_camera == null) _camera = FindFirstObjectByType<Camera>();

            _player = FindFirstObjectByType<NovellaPlayer>();
            _launcher = FindFirstObjectByType<StoryLauncher>();

            if (_launcher != null && _player == null) _currentTab = 1;
            else if (_player != null) _currentTab = 0;

            Canvas c = null;
            if (_player != null && _player.DialoguePanel != null) c = _player.DialoguePanel.GetComponentInParent<Canvas>();
            else if (_launcher != null && _launcher.StoriesContainer != null) c = _launcher.StoriesContainer.GetComponentInParent<Canvas>();

            if (c != null && _camera != null)
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
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(520), GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            _currentTab = GUILayout.Toolbar(_currentTab, _tabs, GUILayout.Height(35));
            if (EditorGUI.EndChangeCheck()) { _activeEditRect = null; _activeDecoRect = null; }

            GUILayout.Space(10);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            if (_currentTab == 0) DrawGameplayUITab();
            else if (_currentTab == 1) DrawMenuUITab();
            else if (_currentTab == 2) DrawPrefabsTab();

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

            EditorGUILayout.HelpBox(ToolLang.Get(
                "Customize the visual style of your story here. Add decorations for unique frames!",
                "Настройте визуальный стиль истории. Добавляйте декорации для создания уникальных рамок!"
            ), MessageType.Info);
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

            Transform bgTransform = _launcher.StoriesContainer.parent.Find("Background");
            if (bgTransform != null) DrawRectTransformEditor(ToolLang.Get("Menu Background", "Фон Меню"), bgTransform.GetComponent<RectTransform>());

            DrawLayoutContainerEditor(ToolLang.Get("Stories Carousel", "Контейнер Историй"), _launcher.StoriesContainer.GetComponent<RectTransform>());
        }

        private void DrawPrefabsTab()
        {
            if (_player != null)
            {
                DrawPrefabManager(ToolLang.Get("Choice Button Asset", "Ассет Кнопки Выбора"), ref _player.ChoiceButtonPrefab, _player);
                if (_player.ChoiceButtonPrefab != null) DrawChoiceButtonPrefabEditor();
                GUILayout.Space(20);
            }

            if (_launcher != null)
            {
                DrawPrefabManager(ToolLang.Get("Story Cover Asset", "Ассет Карточки Истории"), ref _launcher.StoryButtonPrefab, _launcher);
                if (_launcher.StoryButtonPrefab != null) DrawStoryButtonPrefabEditor();
            }
        }

        private void DrawPrefabManager(string title, ref GameObject prefabRef, UnityEngine.Object dirtyTarget)
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
                }, NovellaGalleryWindow.EGalleryFilter.Prefab);
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

                    EditorUtility.DisplayDialog(ToolLang.Get("Success", "Успех"), ToolLang.Get($"Prefab duplicated to:\n{newPath}\n\nYou can select it from the Gallery.", $"Префаб скопирован в:\n{newPath}\n\nВы можете выбрать его через Галерею."), "OK");
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);
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
            DrawRectTransformEditor(ToolLang.Get("Base Button", "Базовая Кнопка"), _tempChoicePreview.GetComponent<RectTransform>());

            TMP_Text txt = _tempChoicePreview.GetComponentInChildren<TMP_Text>();
            if (txt != null) DrawTextEditor(ToolLang.Get("Button Text", "Текст на кнопке"), txt);
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
            DrawRectTransformEditor(ToolLang.Get("Base Card", "Базовая Карточка"), _tempStoryPreview.GetComponent<RectTransform>());

            var titleTxt = _tempStoryPreview.transform.Find("TitleText")?.GetComponent<TMP_Text>();
            var descTxt = _tempStoryPreview.transform.Find("DescText")?.GetComponent<TMP_Text>();

            if (titleTxt != null) DrawTextEditor(ToolLang.Get("Title Text", "Текст Заголовка"), titleTxt);
            if (descTxt != null) DrawTextEditor(ToolLang.Get("Description Text", "Текст Описания"), descTxt);
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

        // ==========================================
        // БЛОК: СЛОИ-ДЕКОРАЦИИ (DECORATIONS)
        // ==========================================
        private void DrawDecorationsBlock(RectTransform parentRect)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("✨ " + ToolLang.Get("Decoration Layers", "Декорации (Слои)"), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            int decCount = 0;
            foreach (Transform t in parentRect) if (t.name.StartsWith("Deco_")) decCount++;

            EditorGUI.BeginDisabledGroup(decCount >= 5);
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
                img.color = new Color(1f, 1f, 1f, 0.2f); // ФИКС: Прозрачный слой по умолчанию

                _activeDecoRect = rt;
                EditorUtility.SetDirty(parentRect.gameObject);
                GUIUtility.ExitGUI(); // ФИКС: Мгновенный выход, чтобы не сломать отрисовку
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            List<RectTransform> decorations = new List<RectTransform>();
            foreach (Transform child in parentRect)
            {
                if (child.name.StartsWith("Deco_")) decorations.Add(child as RectTransform);
            }

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
                        GUILayout.Label("↳ 🖼 " + dec.name, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                        if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), EditorStyles.miniButton, GUILayout.Width(70))) _activeDecoRect = null;
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
                        if (GUILayout.Button("🖼 " + dec.name, EditorStyles.miniButton))
                        {
                            _activeDecoRect = dec;
                        }
                        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                        if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(25)))
                        {
                            Undo.DestroyObjectImmediate(dec.gameObject);
                            if (_activeDecoRect == dec) _activeDecoRect = null;
                            GUIUtility.ExitGUI(); // ФИКС: Выход, чтобы UI не сломался
                        }
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                GUILayout.Label(ToolLang.Get("No extra layers. Add images to make complex borders!", "Нет слоев. Добавьте картинки для создания сложных рамок!"), EditorStyles.centeredGreyMiniLabel);
            }
            GUILayout.EndVertical();
        }

        // ==========================================
        // БЛОК: НАСТРОЙКИ ИЗОБРАЖЕНИЯ (IMAGE)
        // ==========================================
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
                        img.SetAllDirty();
                        Canvas.ForceUpdateCanvases();
                        Repaint();
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image);
            }

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("✖", GUILayout.Width(25))) { Undo.RecordObject(img, "Clear Sprite"); img.sprite = null; EditorUtility.SetDirty(img.gameObject); }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            img.color = EditorGUILayout.ColorField(ToolLang.Get("Color / Tint", "Цвет / Оттенок"), img.color);

            EditorGUI.BeginChangeCheck();
            img.type = (Image.Type)EditorGUILayout.EnumPopup(ToolLang.Get("Draw Type", "Режим отрисовки"), img.type);
            if (EditorGUI.EndChangeCheck() && (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled))
            {
                if (img.sprite != null && img.sprite.border == Vector4.zero)
                {
                    Debug.LogWarning("[Novella] This sprite does not have 9-slice borders set up in its Import Settings!");
                }
            }

            if (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled)
            {
                img.pixelsPerUnitMultiplier = EditorGUILayout.Slider(ToolLang.Get("Border Multiplier", "Масштаб рамок"), img.pixelsPerUnitMultiplier, 0.1f, 10f);
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

            btn.transition = (Selectable.Transition)EditorGUILayout.EnumPopup(ToolLang.Get("Transition Mode", "Режим реакции"), btn.transition);

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

        // ==========================================
        // БЛОК: НАСТРОЙКИ ТЕКСТА (9-WAY И ОТСТУПЫ)
        // ==========================================
        private void DrawTMPTextSettingsBlock(TMP_Text txt, string headerLabel)
        {
            if (txt == null) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("📝 " + headerLabel, EditorStyles.miniBoldLabel);
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

        // ==========================================
        // РЕДАКТОР СЦЕНЫ (ОБЩИЙ УЗЕЛ RECT TRANSFORM)
        // ==========================================
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

                if (rect.GetComponent<TMP_Text>() == null)
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

            if (!skipAnchorAndOffsets)
            {
                GUILayout.BeginHorizontal();
                DrawAnchorGrid(rect);
                GUILayout.BeginVertical();
            }

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

            if (!skipAnchorAndOffsets)
            {
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTextEditor(string title, TMP_Text txt, bool forceActive = false)
        {
            if (txt == null) return;

            DrawRectTransformEditor(title, txt.GetComponent<RectTransform>());

            if (_activeEditRect == txt.GetComponent<RectTransform>())
            {
                EditorGUI.BeginChangeCheck();
                DrawTMPTextSettingsBlock(txt, ToolLang.Get("Typography Settings", "Настройки Типографики"));

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(txt.gameObject);
                    Canvas.ForceUpdateCanvases();
                }
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

        // ==========================================
        // ЖИВОЙ ПРЕДПРОСМОТР (С ЗУМОМ И ГАЛОЧКАМИ)
        // ==========================================
        private void DrawPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("🎥 " + ToolLang.Get("Live Preview", "Живой Предпросмотр"), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            _showBounds = GUILayout.Toggle(_showBounds, ToolLang.Get(" Show Green Bounds", " Зеленые рамки"), EditorStyles.toolbarButton);
            GUILayout.Space(15);

            GUILayout.Label("🔍 " + ToolLang.Get("Zoom:", "Лупа:"), EditorStyles.miniLabel);
            _uiZoom = GUILayout.HorizontalSlider(_uiZoom, 0.5f, 3f, GUILayout.Width(100));
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

                // Высчитываем масштабированный Rect для зума
                float texAspect = 1920f / 1080f;
                float rectAspect = previewRect.width / previewRect.height;
                float drawW = previewRect.width;
                float drawH = previewRect.height;

                if (texAspect > rectAspect) drawH = drawW / texAspect;
                else drawW = drawH * texAspect;

                drawW *= _uiZoom;
                drawH *= _uiZoom;

                Rect drawRect = new Rect(
                    previewRect.center.x - drawW / 2f,
                    previewRect.center.y - drawH / 2f,
                    drawW, drawH
                );

                GUI.DrawTexture(drawRect, _previewTexture, ScaleMode.StretchToFill, false);

                if (_showBounds && _activeEditRect != null) DrawActiveRectBounds(drawRect);
            }
            else if (_camera == null)
            {
                GUI.Box(previewRect, ToolLang.Get("Camera not found.", "Камера не найдена."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
            }

            GUILayout.EndVertical();
        }

        private void DrawActiveRectBounds(Rect drawRect)
        {
            if (_activeEditRect == null || _camera == null || _previewTexture == null) return;

            Vector3[] worldCorners = new Vector3[4];
            _activeEditRect.GetWorldCorners(worldCorners);

            Vector2[] screenPts = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 vp = _camera.WorldToViewportPoint(worldCorners[i]);
                screenPts[i] = new Vector2(
                    drawRect.x + vp.x * drawRect.width,
                    drawRect.y + (1f - vp.y) * drawRect.height
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