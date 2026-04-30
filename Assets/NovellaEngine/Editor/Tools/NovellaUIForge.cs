// ════════════════════════════════════════════════════════════════════════════
// Novella UI Forge — новый редактор интерфейса.
//
// Заменяет старый NovellaUIEditorModule. Основные принципы:
//
//   1. Не показываем "GameObject", "RectTransform", "Canvas" — пользователю
//      это не нужно. Вместо этого — дружелюбные названия ("Текст диалога",
//      "Имя персонажа", "Кнопка выбора").
//   2. Левая колонка — дерево элементов, видимых в текущей сцене.
//   3. Центр — интерактивный превью. Клик в элемент = выделить. Drag = двигать.
//   4. Правая колонка — контекстный инспектор. Свойства сгруппированы:
//      положение/размер, якорь, стиль (для Image / Text / Button — свои блоки).
//
// Сцена редактируется напрямую (через Undo). Превью — RenderTexture от камеры.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using NovellaEngine.Runtime;
using NovellaEngine.Runtime.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor
{
    public class NovellaUIForge : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("UI Forge", "Кузница UI");
        public string ModuleIcon => "🎨";

        // ─── Public entry-points (back-compat со старыми вызовами) ─────────────

        [MenuItem("Tools/Novella Engine/🎨 UI Master Forge", false, 2)]
        public static void ShowWindow()
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.SwitchToModule(3);
        }

        public static void OpenWithCustomPrefab(GameObject targetPrefab)
        {
            ShowWindow();
            // В новой версии вкладки кастом-префабов пока нет — упоминание в TODO для этапа 4.
            if (targetPrefab != null) Debug.Log($"[Novella] OpenWithCustomPrefab: префаб '{targetPrefab.name}' будет поддерживаться в следующей версии.");
        }

        // ─── Цвета (берём те же, что в остальных модулях) ──────────────────────
        private static readonly Color C_BG_PRIMARY = new Color(0.075f, 0.078f, 0.106f);
        private static readonly Color C_BG_SIDE    = new Color(0.102f, 0.106f, 0.149f);
        private static readonly Color C_BG_RAISED  = new Color(0.13f,  0.14f,  0.18f);
        private static readonly Color C_BORDER     = new Color(0.165f, 0.176f, 0.243f);
        private static readonly Color C_TEXT_1     = new Color(0.93f,  0.93f,  0.96f);
        private static readonly Color C_TEXT_2     = new Color(0.78f,  0.80f,  0.86f);
        private static readonly Color C_TEXT_3     = new Color(0.62f,  0.63f,  0.69f);
        private static readonly Color C_TEXT_4     = new Color(0.42f,  0.43f,  0.49f);
        private static readonly Color C_ACCENT     = new Color(0.36f,  0.75f,  0.92f);
        private static readonly Color C_DANGER     = new Color(0.85f,  0.32f,  0.32f);
        private static readonly Color C_SUCCESS    = new Color(0.40f,  0.78f,  0.45f);

        // ─── Поля ──────────────────────────────────────────────────────────────
        private EditorWindow _window;

        // Сцена
        private NovellaPlayer _player;
        private StoryLauncher _launcher;
        private Camera _camera;
        private Canvas _canvas;
        private RenderTexture _previewTexture;

        // Каталог элементов сцены — кэшируется при изменении hierarchy
        private List<RectTransform> _allRects = new();
        private readonly Dictionary<GameObject, string> _friendlyNames = new();
        private readonly Dictionary<GameObject, string> _friendlyIcons = new();

        // Выделение
        private RectTransform _selected;

        // Превью
        private bool _isMobileMode;
        private float _previewZoom = 1f;
        private bool _showSafeArea;
        private bool _showGrid = true;
        private float _gridStep = 20f;

        // Разрешение превью (заменяет тогл Desktop/Mobile в части ширины-высоты).
        // _resolutionPresetIndex < presets.Length — пресет, == presets.Length — Custom.
        private int _resolutionPresetIndex = 0;
        private int _customW = 1920;
        private int _customH = 1080;
        private static readonly (string label, int w, int h, bool isMobile)[] RESOLUTION_PRESETS = new (string, int, int, bool)[]
        {
            ("🖥 1920×1080 (Full HD)",      1920, 1080, false),
            ("🖥 2560×1440 (QHD)",           2560, 1440, false),
            ("🖥 3840×2160 (4K UHD)",        3840, 2160, false),
            ("📱 1080×1920 (Phone Portrait)",1080, 1920, true),
            ("📱 1170×2532 (iPhone 14)",     1170, 2532, true),
            ("📱 1290×2796 (iPhone 14 Pro Max)", 1290, 2796, true),
            ("📱 1668×2388 (iPad Pro 11)",   1668, 2388, true),
            ("🖥 1366×768 (Laptop HD)",      1366, 768,  false),
        };

        // Drag
        private bool _dragging;
        private Vector2 _dragMouseStart;     // координата мыши в превью при начале drag
        private Vector2 _dragAnchorStart;    // anchoredPosition при начале drag

        // Resize handles (8 штук + центр)
        private int _resizeHandle = -1;      // 0..7 = handle index, -1 = нет
        private Vector2 _resizeMouseStart;
        private Vector2 _resizeSizeStart;
        private Vector2 _resizeAnchorStart;
        private const float HANDLE_SIZE_PX = 8f;

        // Поиск по дереву
        private string _treeFilter = "";
        private Vector2 _treeScroll;
        private Vector2 _inspectorScroll;

        // ─── Lifecycle ─────────────────────────────────────────────────────────

        public void OnEnable(EditorWindow w)
        {
            _window = w;
            EditorApplication.hierarchyChanged += OnHierarchyChange;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.delayCall += () =>
            {
                FindReferences();
                _window?.Repaint();
            };
        }

        public void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChange;
            EditorApplication.update -= OnEditorUpdate;
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }
        }

        private void OnHierarchyChange()
        {
            FindReferences();
            RefreshRectsCache();
            _window?.Repaint();
        }

        private void OnEditorUpdate()
        {
            // Repaint только когда модуль активный + видимое окно — иначе перегруз
            if (_window != null && _window == EditorWindow.focusedWindow) _window.Repaint();
            else if (_dragging || _resizeHandle >= 0) _window?.Repaint();
        }

        // ─── Главный draw ──────────────────────────────────────────────────────

        public void DrawGUI(Rect position)
        {
            if (_camera == null) FindReferences();

            // Глобальный Del — работает над всем модулем, не требует курсора над холстом.
            // Перехватываем здесь, чтобы Unity не унёс событие в Hierarchy.
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Delete && _selected != null)
            {
                DeleteSelected();
                ev.Use();
                return;
            }

            if (_camera == null || _canvas == null)
            {
                DrawNoCanvasState(position);
                return;
            }

            if (_allRects.Count == 0) RefreshRectsCache();

            // Layout: 240 (tree) | flex (canvas) | 320 (inspector)
            const float treeW = 260f;
            const float inspectorW = 340f;
            float canvasW = position.width - treeW - inspectorW;
            if (canvasW < 200f) canvasW = 200f;

            Rect treeRect = new Rect(position.x, position.y, treeW, position.height);
            Rect canvasRect = new Rect(position.x + treeW, position.y, canvasW, position.height);
            Rect inspRect = new Rect(position.x + treeW + canvasW, position.y, inspectorW, position.height);

            DrawElementsTree(treeRect);
            DrawCenterCanvas(canvasRect);
            DrawInspector(inspRect);
        }

        private void DrawNoCanvasState(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG_PRIMARY);
            GUILayout.BeginArea(position);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(520));

            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🎨 " + ToolLang.Get("UI Forge", "Кузница UI"), titleSt);
            GUILayout.Space(6);

            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;

            string statusText;
            bool canProceed = _camera != null && _canvas != null;

            if (canProceed)
            {
                statusText = ToolLang.Get("Loading…", "Загрузка…");
            }
            else if (_camera == null && _canvas == null)
            {
                statusText = ToolLang.Get(
                    "Scene seems to be empty. Open the Scene Manager and load a scene that has a Camera and a Canvas.",
                    "В сцене ничего не найдено. Открой «Менеджер Сцен» и загрузи сцену с камерой и UI Canvas.");
            }
            else if (_camera == null)
            {
                statusText = ToolLang.Get(
                    "Canvas found, but no Camera in scene. Add a Camera or load a scene preset.",
                    "Canvas нашёлся, но в сцене нет камеры. Добавь камеру или загрузи пресет сцены.");
            }
            else
            {
                statusText = ToolLang.Get(
                    "Camera found, but no UI Canvas. Add a Canvas to the scene or load a scene preset.",
                    "Камера нашлась, но в сцене нет UI Canvas. Добавь Canvas или загрузи пресет сцены.");
            }
            GUILayout.Label(statusText, subSt);

            GUILayout.Space(10);

            // Диагностический блок — что найдено
            Rect diagRect = GUILayoutUtility.GetRect(0, 92, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(diagRect, C_BG_RAISED);
            DrawRectBorder(diagRect, C_BORDER);

            var diagSt = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            diagSt.normal.textColor = C_TEXT_2;
            float y = diagRect.y + 8;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18),
                (_camera != null ? "✅" : "❌") + "  " + ToolLang.Get("Camera", "Камера") +
                (_camera != null ? "  ·  " + _camera.gameObject.name : ""), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18),
                (_canvas != null ? "✅" : "❌") + "  " + ToolLang.Get("UI Canvas", "UI Canvas") +
                (_canvas != null ? "  ·  " + _canvas.gameObject.name : ""), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18),
                (_player != null ? "✅" : "⚪") + "  NovellaPlayer" +
                (_player != null ? "  ·  " + _player.gameObject.name : "  " + ToolLang.Get("(optional)", "(необязательно)")), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18),
                (_launcher != null ? "✅" : "⚪") + "  StoryLauncher" +
                (_launcher != null ? "  ·  " + _launcher.gameObject.name : "  " + ToolLang.Get("(optional)", "(необязательно)")), diagSt);

            GUILayout.Space(14);

            // На пустой сцене — кнопка создать всё с нуля
            if (_canvas == null)
            {
                GUI.backgroundColor = C_SUCCESS;
                if (GUILayout.Button(ToolLang.Get("✨ Create UI Canvas in scene", "✨ Создать UI Canvas в сцене"), GUILayout.Height(34)))
                {
                    CreateCanvasInScene();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(8);
            }

            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button(ToolLang.Get("🔄 Refresh", "🔄 Обновить"), GUILayout.Height(34)))
            {
                FindReferences();
                RefreshRectsCache();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void CreateCanvasInScene()
        {
            // 1. Camera (если нет)
            if (_camera == null && Camera.main == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
                cam.orthographic = false;
                camGo.AddComponent<AudioListener>();
                Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
                _camera = cam;
            }

            // 2. Canvas
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = 5f;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

            // 3. EventSystem (если нет)
            var es = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
            }

            FindReferences();
            RefreshRectsCache();
        }

        // ─── Find references / scene fixup ─────────────────────────────────────

        private void FindReferences()
        {
            // Камера: main → любая в сцене (включая неактивные)
            _camera = Camera.main;
            if (_camera == null) _camera = UnityEngine.Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

            // Player / Launcher (включая неактивные — некоторые сцены имеют их выключенными)
            _player = UnityEngine.Object.FindAnyObjectByType<NovellaPlayer>(FindObjectsInactive.Include);
            _launcher = UnityEngine.Object.FindAnyObjectByType<StoryLauncher>(FindObjectsInactive.Include);

            // Канвас: пробуем по цепочке fallback'ов от наиболее точного к наименее.
            _canvas = null;

            // 1. По полям компонентов
            if (_player != null && _player.DialoguePanel != null)
                _canvas = _player.DialoguePanel.GetComponentInParent<Canvas>(true);
            else if (_launcher != null && _launcher.StoriesContainer != null)
                _canvas = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);

            // 2. Через сам gameObject компонента
            if (_canvas == null)
            {
                if (_player != null)
                    _canvas = _player.GetComponentInParent<Canvas>(true)
                           ?? _player.GetComponentInChildren<Canvas>(true);
                else if (_launcher != null)
                    _canvas = _launcher.GetComponentInParent<Canvas>(true)
                           ?? _launcher.GetComponentInChildren<Canvas>(true);
            }

            // 3. Любой Canvas в сцене (включая неактивные)
            if (_canvas == null)
            {
                var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                // Предпочитаем root canvas (без вложенных)
                _canvas = allCanvases.FirstOrDefault(c => c.isRootCanvas) ?? allCanvases.FirstOrDefault();
            }

            // Настраиваем render mode только если у нас есть И канвас И камера.
            if (_canvas != null && _camera != null)
            {
                Canvas rc = _canvas.rootCanvas != null ? _canvas.rootCanvas : _canvas;
                if (rc.renderMode != RenderMode.ScreenSpaceCamera || rc.worldCamera != _camera)
                {
                    rc.renderMode = RenderMode.ScreenSpaceCamera;
                    rc.worldCamera = _camera;
                    if (rc.planeDistance < 1f) rc.planeDistance = 5f;
                    EditorUtility.SetDirty(rc);
                }
                _canvas = rc;
            }

            BuildFriendlyNamesMap();
        }

        private void BuildFriendlyNamesMap()
        {
            _friendlyNames.Clear();
            _friendlyIcons.Clear();
            AddFromComponent(_player);
            AddFromComponent(_launcher);
        }

        private void AddFromComponent(Component c)
        {
            if (c == null) return;
            var fields = c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var f in fields)
            {
                object val = f.GetValue(c);
                GameObject go = null;
                if (val is GameObject g) go = g;
                else if (val is Component comp) go = comp.gameObject;
                if (go != null && !_friendlyNames.ContainsKey(go))
                {
                    _friendlyNames[go] = LocalizeFieldName(f.Name);
                    _friendlyIcons[go] = IconFor(f.Name, go);
                }
            }
        }

        private static string LocalizeFieldName(string fieldName)
        {
            return fieldName switch
            {
                "DialoguePanel"          => ToolLang.Get("Dialogue Panel",      "Панель диалога"),
                "SpeakerNameText"        => ToolLang.Get("Speaker Name",        "Имя говорящего"),
                "DialogueBodyText"       => ToolLang.Get("Dialogue Text",       "Текст диалога"),
                "SaveNotification"       => ToolLang.Get("Save Notification",   "Уведомление сохранения"),
                "ChoiceContainer"        => ToolLang.Get("Choices",              "Кнопки выбора"),
                "ChoiceButtonPrefab"     => ToolLang.Get("Choice Button (Prefab)","Кнопка выбора (префаб)"),
                "CharactersContainer"    => ToolLang.Get("Characters",           "Персонажи"),
                "MainMenuPanel"          => ToolLang.Get("Main Menu",            "Главное меню"),
                "StoriesPanel"           => ToolLang.Get("Stories",              "Список историй"),
                "MCCreationPanel"        => ToolLang.Get("Character Creation",   "Создание персонажа"),
                "MCConfirmButton"        => ToolLang.Get("Confirm Button",       "Кнопка «Готово»"),
                "MCAvatarPreview"        => ToolLang.Get("Avatar",               "Аватар"),
                "MCPrevLookButton"       => ToolLang.Get("Prev Look",            "← Предыдущий вид"),
                "MCNextLookButton"       => ToolLang.Get("Next Look",            "Следующий вид →"),
                "StoriesContainer"       => ToolLang.Get("Stories List",         "Лента историй"),
                "StoryButtonPrefab"      => ToolLang.Get("Story Card (Prefab)",  "Карточка истории (префаб)"),
                _ => fieldName
            };
        }

        private static string IconFor(string fieldName, GameObject go)
        {
            if (go.GetComponent<Button>() != null) return "🔘";
            if (go.GetComponent<TMP_Text>() != null || go.GetComponent<UnityEngine.UI.Text>() != null) return "𝐓";
            if (go.GetComponent<Image>() != null) return "🖼";
            if (fieldName.Contains("Container") || fieldName.Contains("Panel")) return "▣";
            return "◆";
        }

        // ─── Сборка списка прямоугольников ─────────────────────────────────────

        private void RefreshRectsCache()
        {
            _allRects.Clear();
            if (_canvas == null) return;
            var rt = _canvas.GetComponent<RectTransform>();
            if (rt != null) CollectRectsRecursive(rt, _allRects, 0);
        }

        private static void CollectRectsRecursive(RectTransform rt, List<RectTransform> list, int depth)
        {
            if (rt == null || depth > 16) return;
            // Прячем технические TMP-подмеши (создаются автоматически от fallback-шрифтов)
            if (IsHiddenInternal(rt)) return;
            list.Add(rt);
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null) CollectRectsRecursive(ch, list, depth + 1);
            }
        }

        // Возвращает true для GameObject'ов, которые юзеру видеть/редактировать НЕ нужно:
        // например TMP_SubMeshUI, скрытые DontSave и т.п.
        private static bool IsHiddenInternal(RectTransform rt)
        {
            if (rt == null) return true;
            var go = rt.gameObject;
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) return true;
            // TMP_SubMeshUI создаёт дочерние объекты для fallback-шрифтов
            if (go.GetComponent<TMPro.TMP_SubMeshUI>() != null) return true;
            if (go.GetComponent<TMPro.TMP_SubMesh>() != null) return true;
            return false;
        }

        private string GetDisplayName(RectTransform rt)
        {
            if (rt == null) return "?";
            if (_friendlyNames.TryGetValue(rt.gameObject, out var n)) return n;
            return rt.gameObject.name;
        }

        private string GetDisplayIcon(RectTransform rt)
        {
            if (rt == null) return "◆";
            if (_friendlyIcons.TryGetValue(rt.gameObject, out var i)) return i;
            return IconFor("", rt.gameObject);
        }

        // ─── Левая панель: дерево элементов ────────────────────────────────────

        private void DrawElementsTree(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            DrawRectBorder(new Rect(rect.xMax - 1, rect.y, 1, rect.height), C_BORDER);

            GUILayout.BeginArea(rect);

            // Header
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🗂  " + ToolLang.Get("Scene Elements", "Элементы сцены"), titleSt);
            GUILayout.EndHorizontal();

            // Counter
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(string.Format(ToolLang.Get("{0} elements found", "{0} элементов"), _allRects.Count), subSt);
            GUILayout.EndHorizontal();

            // Search
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            Rect searchRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            EditorGUI.DrawRect(searchRect, C_BG_PRIMARY);
            DrawRectBorder(searchRect, C_BORDER);
            var sst = new GUIStyle(EditorStyles.textField) { fontSize = 11, padding = new RectOffset(6, 6, 4, 4) };
            sst.normal.background = null; sst.focused.background = null;
            sst.normal.textColor = C_TEXT_1; sst.focused.textColor = C_TEXT_1;
            _treeFilter = GUI.TextField(searchRect, _treeFilter, sst);
            if (string.IsNullOrEmpty(_treeFilter))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y, searchRect.width, searchRect.height),
                    "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }

            GUILayout.Space(8);

            // Items
            _treeScroll = GUILayout.BeginScrollView(_treeScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            string filter = _treeFilter?.Trim().ToLowerInvariant() ?? "";

            for (int i = 0; i < _allRects.Count; i++)
            {
                var rt = _allRects[i];
                if (rt == null) continue;
                string name = GetDisplayName(rt);
                if (filter.Length > 0 && !name.ToLowerInvariant().Contains(filter)) continue;

                int depth = ComputeDepth(rt);
                DrawTreeRow(rt, name, GetDisplayIcon(rt), depth, i);
            }

            GUILayout.EndScrollView();

            // Подсказка про правый клик
            GUILayout.Space(8);
            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = C_TEXT_4;
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("💡 " + ToolLang.Get(
                "Right-click on canvas or tree to add elements",
                "ПКМ по холсту или дереву — добавить элемент"), hint);
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.EndArea();
        }

        // ─── Создание новых элементов (через ПКМ) ──────────────────────────────

        private Transform GetCreationParent()
        {
            // Создаём под выбранный элемент, если он есть и это контейнер; иначе под root canvas.
            if (_selected != null && _selected != _canvas.GetComponent<RectTransform>())
                return _selected.transform;
            return _canvas != null ? _canvas.transform : null;
        }

        private RectTransform PlaceUnderParent(GameObject go, Vector2 size, string undoName)
        {
            var parent = GetCreationParent();
            if (parent == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(go, undoName);
            _selected = rt;
            RefreshRectsCache();
            EditorUtility.SetDirty(go);
            return rt;
        }

        private void CreateText()
        {
            var go = new GameObject("Text");
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "Текст";
            tmp.fontSize = 32;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            PlaceUnderParent(go, new Vector2(220, 60), "Create Text");
        }

        private void CreateButton()
        {
            var go = new GameObject("Button");
            var img = go.AddComponent<Image>();
            img.color = new Color(0.36f, 0.75f, 0.92f);
            go.AddComponent<Button>();
            var rt = PlaceUnderParent(go, new Vector2(180, 48), "Create Button");
            if (rt == null) return;

            // Дочерний TMP-текст внутри кнопки
            var textGo = new GameObject("Text");
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "Button";
            tmp.color = new Color(0.07f, 0.08f, 0.10f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 18;
            tmp.fontStyle = FontStyles.Bold;
            var trt = textGo.GetComponent<RectTransform>();
            trt.SetParent(go.transform, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            trt.localScale = Vector3.one;
        }

        private void CreateImage()
        {
            var go = new GameObject("Image");
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            PlaceUnderParent(go, new Vector2(120, 120), "Create Image");
        }

        private void CreatePanel()
        {
            var go = new GameObject("Panel");
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.11f, 0.15f, 0.85f);
            PlaceUnderParent(go, new Vector2(420, 220), "Create Panel");
        }

        private int ComputeDepth(RectTransform rt)
        {
            if (rt == null || _canvas == null) return 0;
            int d = 0;
            var t = rt.parent;
            while (t != null && t != _canvas.transform.parent)
            {
                d++;
                if (d > 16) break;
                t = t.parent;
            }
            return Mathf.Max(0, d - 1);
        }

        private void DrawTreeRow(RectTransform rt, string name, string icon, int depth, int index)
        {
            bool isSel = rt == _selected;

            GUILayout.BeginHorizontal();
            GUILayout.Space(0);
            Rect row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUILayout.Space(0);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, isSel ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f) : Color.clear);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            // Tree-line indent: вертикальные направляющие для каждого уровня + L-коннектор.
            const float STEP = 14f;
            float baseX = row.x + 12f;
            Color lineCol = new Color(0.30f, 0.32f, 0.40f, 0.55f);

            if (depth > 0)
            {
                // Вертикальные направляющие для всех родительских уровней (кроме корня)
                for (int d = 1; d <= depth; d++)
                {
                    float lx = baseX + (d - 1) * STEP + STEP * 0.5f;
                    EditorGUI.DrawRect(new Rect(lx, row.y, 1, row.height), lineCol);
                }
                // L-коннектор для текущей строки (горизонтальная палочка)
                float branchX = baseX + (depth - 1) * STEP + STEP * 0.5f;
                EditorGUI.DrawRect(new Rect(branchX, row.y + row.height * 0.5f, STEP * 0.5f, 1), lineCol);
            }

            float iconX = baseX + depth * STEP;

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = isSel ? C_ACCENT : C_TEXT_3;
            GUI.Label(new Rect(iconX, row.y, 18, row.height), icon, iconSt);

            var nameSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            nameSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(iconX + 20, row.y, row.width - (iconX - row.x) - 28, row.height), name, nameSt);

            // Левый клик — выбрать. Правый — контекстное меню создания / удаления.
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _selected = rt;
                if (Event.current.button == 1)
                {
                    ShowTreeContextMenu(rt);
                }
                _window?.Repaint();
                Event.current.Use();
            }
        }

        private void ShowTreeContextMenu(RectTransform rt)
        {
            var menu = new GenericMenu();
            string parentName = GetDisplayName(rt);

            menu.AddDisabledItem(new GUIContent(string.Format(ToolLang.Get("Create inside: {0}", "Создать внутри: {0}"), parentName)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("📝 Text", "📝 Текст")),     false, () => { _selected = rt; CreateText();   });
            menu.AddItem(new GUIContent(ToolLang.Get("🔘 Button", "🔘 Кнопка")),  false, () => { _selected = rt; CreateButton(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🖼 Image", "🖼 Картинка")),  false, () => { _selected = rt; CreateImage();  });
            menu.AddItem(new GUIContent(ToolLang.Get("▣ Panel", "▣ Панель")),    false, () => { _selected = rt; CreatePanel();  });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("📋 Duplicate", "📋 Дублировать")), false, () => { _selected = rt; DuplicateSelected(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🗑 Delete", "🗑 Удалить")), false, () => { _selected = rt; DeleteSelected(); });
            menu.ShowAsContext();
        }

        // ─── Центр: интерактивный canvas ───────────────────────────────────────

        private void DrawCenterCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);

            // Topbar для канваса
            const float topH = 48f;
            Rect topbar = new Rect(rect.x, rect.y, rect.width, topH);
            EditorGUI.DrawRect(topbar, C_BG_SIDE);
            DrawRectBorder(new Rect(topbar.x, topbar.yMax - 1, topbar.width, 1), C_BORDER);

            DrawCanvasTopbar(topbar);

            Rect viewport = new Rect(rect.x, rect.y + topH, rect.width, rect.height - topH);

            if (_camera == null) return;

            // Целевое разрешение — из пресета или из custom-полей
            int targetW, targetH;
            if (_resolutionPresetIndex >= 0 && _resolutionPresetIndex < RESOLUTION_PRESETS.Length)
            {
                var p = RESOLUTION_PRESETS[_resolutionPresetIndex];
                targetW = p.w;
                targetH = p.h;
                _isMobileMode = p.isMobile;
            }
            else
            {
                targetW = Mathf.Max(64, _customW);
                targetH = Mathf.Max(64, _customH);
                _isMobileMode = targetH > targetW;
            }

            // Recreate texture if needed
            if (Event.current.type == EventType.Repaint)
            {
                if (_previewTexture == null || _previewTexture.width != targetW || _previewTexture.height != targetH)
                {
                    if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); }
                    _previewTexture = new RenderTexture(targetW, targetH, 24)
                    {
                        antiAliasing = 4,
                        filterMode = FilterMode.Bilinear,
                    };
                }

                var oldRT = _camera.targetTexture;
                _camera.targetTexture = _previewTexture;
                _camera.Render();
                _camera.targetTexture = oldRT;
            }

            // Рассчитать draw rect В ЛОКАЛЬНЫХ КООРДИНАТАХ КЛИПА (origin = viewport.x/y).
            // Зум >1 разрешён — лишнее обрежется по границам clip'а.
            float aspect = (float)targetW / targetH;
            float maxW = (viewport.width - 32f) * _previewZoom;
            float maxH = (viewport.height - 32f) * _previewZoom;
            float w = maxW;
            float h = w / aspect;
            if (h > maxH) { h = maxH; w = h * aspect; }

            // drawRect в LOCAL координатах (внутри clip)
            Rect drawRectLocal = new Rect(
                (viewport.width - w) * 0.5f,
                (viewport.height - h) * 0.5f,
                w, h);

            // Всё рисование внутри clip'а — координаты локальные относительно viewport.
            GUI.BeginClip(viewport);
            try
            {
                // Подложка viewport
                EditorGUI.DrawRect(new Rect(0, 0, viewport.width, viewport.height), new Color(0.05f, 0.05f, 0.07f));

                // Тень-кадр вокруг превью
                var frame = new Rect(drawRectLocal.x - 2, drawRectLocal.y - 2, drawRectLocal.width + 4, drawRectLocal.height + 4);
                EditorGUI.DrawRect(frame, C_BORDER);

                if (Event.current.type == EventType.Repaint && _previewTexture != null)
                {
                    GUI.DrawTexture(drawRectLocal, _previewTexture, ScaleMode.ScaleToFit, false);
                }

                // Сетка
                if (_showGrid) DrawGridOverlay(drawRectLocal, targetW, targetH);

                // Safe area
                if (_isMobileMode && _showSafeArea) DrawSafeAreaOverlay(drawRectLocal);

                // Селекшен + handles
                if (_selected != null)
                {
                    Rect selRect = ComputeRectScreenForRT(_selected, drawRectLocal, targetW, targetH);
                    DrawSelectionOverlay(selRect);
                }

                // Input — Event.current.mousePosition внутри BeginClip уже локальный,
                // и drawRectLocal тоже локальный → проверки совпадают.
                HandleCanvasInput(drawRectLocal, targetW, targetH);
            }
            finally
            {
                GUI.EndClip();
            }
        }

        private void DrawCanvasTopbar(Rect topbar)
        {
            // Делим топбар на 3 группы, рисуем каждую как "карточку".
            // Группы отделяем тонкой вертикальной чертой для визуального разделения.

            const float pad = 14f;
            const float groupH = 32f;
            float yMid = topbar.y + (topbar.height - groupH) * 0.5f;

            // ── ГРУППА 1: разрешение (слева) ──
            float g1W = (_resolutionPresetIndex == RESOLUTION_PRESETS.Length) ? 360f : 220f;
            Rect g1 = new Rect(topbar.x + pad, yMid, g1W, groupH);
            DrawTopbarGroupBg(g1);
            DrawResolutionGroup(g1);

            // ── ГРУППА 3: zoom + help (справа) ──
            const float g3W = 290f;
            Rect g3 = new Rect(topbar.xMax - pad - g3W, yMid, g3W, groupH);
            DrawTopbarGroupBg(g3);
            DrawZoomHelpGroup(g3);

            // ── ГРУППА 2: тогглы (по центру между g1 и g3) ──
            float g2W = _isMobileMode ? 230f : 92f;
            float availStart = g1.xMax + 12f;
            float availEnd = g3.x - 12f;
            float g2X = (availStart + availEnd) * 0.5f - g2W * 0.5f;
            // если не помещается между группами — прижимаем к правой границе g1
            if (g2X < availStart) g2X = availStart;
            if (g2X + g2W > availEnd) g2X = availEnd - g2W;
            Rect g2 = new Rect(g2X, yMid, g2W, groupH);
            DrawTopbarGroupBg(g2);
            DrawTogglesGroup(g2);
        }

        private void DrawTopbarGroupBg(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_RAISED);
            DrawRectBorder(r, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.7f));
        }

        private void DrawResolutionGroup(Rect r)
        {
            float monitorIconW = 28f;
            // Иконка-индикатор устройства (десктоп/мобила) слева
            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = C_TEXT_2;
            GUI.Label(new Rect(r.x, r.y, monitorIconW, r.height), _isMobileMode ? "📱" : "🖥", iconSt);

            // Разделитель
            EditorGUI.DrawRect(new Rect(r.x + monitorIconW, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.06f));

            string[] options = new string[RESOLUTION_PRESETS.Length + 1];
            for (int i = 0; i < RESOLUTION_PRESETS.Length; i++) options[i] = RESOLUTION_PRESETS[i].label;
            options[RESOLUTION_PRESETS.Length] = ToolLang.Get("⚙ Custom…", "⚙ Своё…");

            // Dropdown растягивается на оставшееся место (минус custom-поля если они есть)
            float ddX = r.x + monitorIconW + 6;
            float ddW = (_resolutionPresetIndex == RESOLUTION_PRESETS.Length) ? 200f : (r.width - monitorIconW - 12);
            Rect ddRect = new Rect(ddX, r.y + 5, ddW, 22);

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUI.Popup(ddRect, _resolutionPresetIndex, options);
            if (EditorGUI.EndChangeCheck())
            {
                _resolutionPresetIndex = newIdx;
                ResetPreviewTexture();
            }

            // Custom W/H поля
            if (_resolutionPresetIndex == RESOLUTION_PRESETS.Length)
            {
                float fx = ddRect.xMax + 6;
                var labelSt = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_3 }, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(fx, r.y, 14, r.height), "W", labelSt);
                EditorGUI.BeginChangeCheck();
                int nw = EditorGUI.IntField(new Rect(fx + 14, r.y + 5, 50, 22), _customW);
                GUI.Label(new Rect(fx + 70, r.y, 14, r.height), "H", labelSt);
                int nh = EditorGUI.IntField(new Rect(fx + 84, r.y + 5, 50, 22), _customH);
                if (EditorGUI.EndChangeCheck())
                {
                    _customW = Mathf.Clamp(nw, 64, 8192);
                    _customH = Mathf.Clamp(nh, 64, 8192);
                    ResetPreviewTexture();
                }
            }
        }

        private void DrawTogglesGroup(Rect r)
        {
            float btnW = 86f;
            float bh = 22f;
            float bx = r.x + 4;
            float by = r.y + (r.height - bh) * 0.5f;

            DrawIconToggle(new Rect(bx, by, btnW, bh), "▦", ToolLang.Get("Grid", "Сетка"), ref _showGrid);
            bx += btnW + 4;

            if (_isMobileMode)
            {
                DrawIconToggle(new Rect(bx, by, 130, bh), "📱", ToolLang.Get("Safe Area", "Безоп. зона"), ref _showSafeArea);
            }
            else
            {
                _showSafeArea = false;
            }
        }

        // Кастомный toggle: иконка + текст, бирюзовая обводка/подложка когда активен.
        private void DrawIconToggle(Rect r, string icon, string label, ref bool value)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg;
            if (value) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, hover ? 0.30f : 0.22f);
            else bg = hover ? new Color(1, 1, 1, 0.05f) : Color.clear;
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, value ? C_ACCENT : new Color(1, 1, 1, 0.07f));

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = value ? C_ACCENT : C_TEXT_2;
            GUI.Label(new Rect(r.x + 6, r.y, 20, r.height), icon, iconSt);

            var labelSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            labelSt.normal.textColor = value ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(r.x + 26, r.y, r.width - 32, r.height), label, labelSt);

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                value = !value;
                Event.current.Use();
            }
        }

        private void DrawZoomHelpGroup(Rect r)
        {
            // Layout: [—] [-▭slider▭-] [+]   |   [Zoom 100%]   |   [❓]
            float bx = r.x + 6;
            float by = r.y + (r.height - 22) * 0.5f;

            // Минус
            if (DrawIconBtn(new Rect(bx, by, 22, 22), "−"))
            {
                _previewZoom = Mathf.Max(0.25f, _previewZoom - 0.1f);
            }
            bx += 22 + 2;

            // Слайдер
            float sliderW = 90f;
            _previewZoom = GUI.HorizontalSlider(new Rect(bx, by + 8, sliderW, 6), _previewZoom, 0.25f, 1.5f);
            bx += sliderW + 2;

            // Плюс
            if (DrawIconBtn(new Rect(bx, by, 22, 22), "+"))
            {
                _previewZoom = Mathf.Min(1.5f, _previewZoom + 0.1f);
            }
            bx += 22 + 6;

            // Разделитель
            EditorGUI.DrawRect(new Rect(bx, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.06f));
            bx += 6;

            // Процент
            var pctSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            pctSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(bx, r.y, 56, r.height), (_previewZoom * 100f).ToString("F0") + "%", pctSt);
            bx += 56 + 4;

            // Разделитель
            EditorGUI.DrawRect(new Rect(bx, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.06f));
            bx += 6;

            // Help
            if (DrawIconBtn(new Rect(bx, by, 26, 22), "❓"))
            {
                NovellaUIForgeHelpWindow.ShowHelp();
            }
        }

        private bool DrawIconBtn(Rect r, string icon)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? new Color(1, 1, 1, 0.08f) : Color.clear);
            DrawRectBorder(r, new Color(1, 1, 1, hover ? 0.12f : 0.04f));

            var st = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            st.normal.textColor = hover ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(r, icon, st);

            bool clicked = Event.current.type == EventType.MouseDown && hover && Event.current.button == 0;
            if (clicked) Event.current.Use();
            return clicked;
        }

        private void ResetPreviewTexture()
        {
            if (_previewTexture != null)
            {
                _previewTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewTexture);
                _previewTexture = null;
            }
        }

        private void DrawGridOverlay(Rect drawRect, int targetW, int targetH)
        {
            if (Event.current.type != EventType.Repaint) return;
            float pxPerUnit = drawRect.width / targetW;
            float step = _gridStep * pxPerUnit;
            if (step < 6f) step = 6f;
            Color lineCol = new Color(1f, 1f, 1f, 0.04f);

            for (float x = drawRect.x + step; x < drawRect.xMax; x += step)
                EditorGUI.DrawRect(new Rect(x, drawRect.y, 1, drawRect.height), lineCol);
            for (float y = drawRect.y + step; y < drawRect.yMax; y += step)
                EditorGUI.DrawRect(new Rect(drawRect.x, y, drawRect.width, 1), lineCol);
        }

        private void DrawSafeAreaOverlay(Rect drawRect)
        {
            // Условная мобильная safe area: ~5% по бокам, ~10% сверху, ~5% снизу
            float t = drawRect.height * 0.10f;
            float b = drawRect.height * 0.05f;
            float s = drawRect.width * 0.05f;
            Rect safe = new Rect(drawRect.x + s, drawRect.y + t, drawRect.width - s * 2f, drawRect.height - t - b);
            Color c = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.7f);
            EditorGUI.DrawRect(new Rect(safe.x, safe.y, safe.width, 1), c);
            EditorGUI.DrawRect(new Rect(safe.x, safe.yMax - 1, safe.width, 1), c);
            EditorGUI.DrawRect(new Rect(safe.x, safe.y, 1, safe.height), c);
            EditorGUI.DrawRect(new Rect(safe.xMax - 1, safe.y, 1, safe.height), c);
        }

        // Считаем экранный прямоугольник выделенного RT.
        // Делегируем в ComputeRectScreenForRT (одна формула для всех).
        private Rect ComputeSelectedScreenRect(Rect drawRect, int targetW, int targetH)
        {
            return ComputeRectScreenForRT(_selected, drawRect, targetW, targetH);
        }

        private void DrawSelectionOverlay(Rect selRect)
        {
            if (selRect.width < 1f && selRect.height < 1f) return;
            Color c = C_ACCENT;

            // Border 2px
            EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.y - 1, selRect.width + 2, 2), c);
            EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.yMax - 1, selRect.width + 2, 2), c);
            EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.y - 1, 2, selRect.height + 2), c);
            EditorGUI.DrawRect(new Rect(selRect.xMax - 1, selRect.y - 1, 2, selRect.height + 2), c);

            // Resize handles
            for (int i = 0; i < 8; i++)
            {
                Rect h = HandleRectByIndex(i, selRect);
                EditorGUI.DrawRect(h, Color.white);
                EditorGUI.DrawRect(new Rect(h.x + 1, h.y + 1, h.width - 2, h.height - 2), c);
            }
        }

        private static Rect HandleRectByIndex(int idx, Rect r)
        {
            float s = HANDLE_SIZE_PX;
            float midX = r.x + r.width / 2f - s / 2f;
            float midY = r.y + r.height / 2f - s / 2f;
            return idx switch
            {
                0 => new Rect(r.x - s / 2f, r.y - s / 2f, s, s),                 // tl
                1 => new Rect(midX, r.y - s / 2f, s, s),                          // t
                2 => new Rect(r.xMax - s / 2f, r.y - s / 2f, s, s),               // tr
                3 => new Rect(r.xMax - s / 2f, midY, s, s),                       // r
                4 => new Rect(r.xMax - s / 2f, r.yMax - s / 2f, s, s),            // br
                5 => new Rect(midX, r.yMax - s / 2f, s, s),                       // b
                6 => new Rect(r.x - s / 2f, r.yMax - s / 2f, s, s),               // bl
                7 => new Rect(r.x - s / 2f, midY, s, s),                          // l
                _ => Rect.zero
            };
        }

        private void HandleCanvasInput(Rect drawRect, int targetW, int targetH)
        {
            Event e = Event.current;
            if (e == null) return;

            // Del обрабатывается на уровне DrawGUI (над всем модулем).

            // Pre-pass: если уже идёт drag/resize — обрабатываем независимо от позиции
            if (_dragging || _resizeHandle >= 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (_dragging) ProcessDragMove(e, drawRect, targetW, targetH);
                    else if (_resizeHandle >= 0) ProcessResize(e, drawRect, targetW, targetH);
                    e.Use();
                    return;
                }
                if (e.type == EventType.MouseUp)
                {
                    _dragging = false;
                    _resizeHandle = -1;
                    e.Use();
                    return;
                }
            }

            // Правый клик — контекстное меню создания
            if (e.type == EventType.ContextClick && drawRect.Contains(e.mousePosition))
            {
                RectTransform under = PickRectAt(e.mousePosition, drawRect, targetW, targetH);
                ShowCanvasCreateMenu(under);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && drawRect.Contains(e.mousePosition))
            {
                // Resize handles имеют приоритет над всем (даже если кликнули внутри родителя)
                if (_selected != null)
                {
                    Rect selRect = ComputeSelectedScreenRect(drawRect, targetW, targetH);
                    for (int i = 0; i < 8; i++)
                    {
                        Rect h = HandleRectByIndex(i, selRect);
                        if (h.Contains(e.mousePosition))
                        {
                            BeginResize(i, e.mousePosition);
                            e.Use();
                            return;
                        }
                    }
                }

                // ВСЕГДА переподбираем элемент под курсором.
                // Раньше: если ребёнок был внутри selection rect родителя, мы тащили родителя.
                // Теперь: pick всегда возвращает самый ГЛУБОКИЙ элемент (PickRectAt),
                // и драг применяется именно к нему.
                RectTransform picked = PickRectAt(e.mousePosition, drawRect, targetW, targetH);
                if (picked != null)
                {
                    _selected = picked;
                    BeginDrag(e.mousePosition);
                }
                else
                {
                    _selected = null;
                }
                e.Use();
            }
        }

        private void ShowCanvasCreateMenu(RectTransform under)
        {
            var menu = new GenericMenu();

            Transform creationParent;
            string parentName;
            if (under != null && under != _canvas.GetComponent<RectTransform>())
            {
                creationParent = under.transform;
                parentName = GetDisplayName(under);
                _selected = under; // визуально выделяем родителя для подсказки
            }
            else
            {
                creationParent = _canvas.transform;
                parentName = "Canvas";
            }

            menu.AddDisabledItem(new GUIContent(string.Format(ToolLang.Get("Create inside: {0}", "Создать внутри: {0}"), parentName)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("📝 Text", "📝 Текст")),     false, () => { _selected = creationParent.GetComponent<RectTransform>(); CreateText();   });
            menu.AddItem(new GUIContent(ToolLang.Get("🔘 Button", "🔘 Кнопка")),  false, () => { _selected = creationParent.GetComponent<RectTransform>(); CreateButton(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🖼 Image", "🖼 Картинка")),  false, () => { _selected = creationParent.GetComponent<RectTransform>(); CreateImage();  });
            menu.AddItem(new GUIContent(ToolLang.Get("▣ Panel", "▣ Панель")),    false, () => { _selected = creationParent.GetComponent<RectTransform>(); CreatePanel();  });
            menu.ShowAsContext();
        }

        // Возвращает самый глубокий RT, чей world rect содержит экранную точку.
        private RectTransform PickRectAt(Vector2 mousePosScreen, Rect drawRect, int targetW, int targetH)
        {
            if (_canvas == null || _camera == null) return null;
            // Идём с конца (потомки рисуются позже = выше)
            for (int i = _allRects.Count - 1; i >= 0; i--)
            {
                var rt = _allRects[i];
                if (rt == null) continue;
                if (rt == _canvas.GetComponent<RectTransform>()) continue; // не пикаем сам канвас
                if (!rt.gameObject.activeInHierarchy) continue;
                Rect rs = ComputeRectScreenForRT(rt, drawRect, targetW, targetH);
                if (rs.width < 4f || rs.height < 4f) continue;
                if (rs.Contains(mousePosScreen)) return rt;
            }
            return null;
        }

        // Преобразуем мировые координаты RT в пиксели окна редактора.
        // Подход: canvas (в режиме ScreenSpaceCamera) занимает строго такую же область
        // мира, что и кадр камеры → берём мировой rect канваса как 0..1 и нормируем
        // мировой rect элемента в эти координаты. Не зависит от Camera.pixelWidth.
        private Rect ComputeRectScreenForRT(RectTransform rt, Rect drawRect, int targetW, int targetH)
        {
            if (rt == null || _canvas == null) return Rect.zero;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return Rect.zero;

            Vector3[] cw = new Vector3[4]; // 0=BL, 1=TL, 2=TR, 3=BR
            canvasRT.GetWorldCorners(cw);
            float canvasMinX = cw[0].x, canvasMinY = cw[0].y;
            float canvasMaxX = cw[2].x, canvasMaxY = cw[2].y;
            float canvasW = canvasMaxX - canvasMinX;
            float canvasH = canvasMaxY - canvasMinY;
            if (canvasW < 0.0001f || canvasH < 0.0001f) return Rect.zero;

            Vector3[] worldCorners = new Vector3[4];
            rt.GetWorldCorners(worldCorners);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var w in worldCorners)
            {
                float u = (w.x - canvasMinX) / canvasW;
                float v = (w.y - canvasMinY) / canvasH;
                float px = drawRect.x + u * drawRect.width;
                float py = drawRect.y + (1f - v) * drawRect.height; // GUI: y вниз
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // ─── Drag ──────────────────────────────────────────────────────────────

        private void BeginDrag(Vector2 mousePos)
        {
            if (_selected == null) return;
            _dragging = true;
            _dragMouseStart = mousePos;
            _dragAnchorStart = _selected.anchoredPosition;
            Undo.RecordObject(_selected, "Move UI Element");
        }

        private void ProcessDragMove(Event e, Rect drawRect, int targetW, int targetH)
        {
            if (_selected == null) return;
            Vector2 deltaPreview = e.mousePosition - _dragMouseStart;
            Vector2 deltaCanvas = PreviewDeltaToCanvasDelta(deltaPreview, drawRect);

            Vector2 newAnchor = _dragAnchorStart + deltaCanvas;
            if (_showGrid && _gridStep > 0.5f)
            {
                newAnchor.x = Mathf.Round(newAnchor.x / _gridStep) * _gridStep;
                newAnchor.y = Mathf.Round(newAnchor.y / _gridStep) * _gridStep;
            }

            _selected.anchoredPosition = newAnchor;
            EditorUtility.SetDirty(_selected);
        }

        // Перевод дельты курсора в превью-пикселях → в локальные пиксели канваса.
        // Используем реальный размер RectTransform канваса (учитывает CanvasScaler).
        private Vector2 PreviewDeltaToCanvasDelta(Vector2 deltaPreview, Rect drawRect)
        {
            if (_canvas == null || drawRect.width < 1f) return Vector2.zero;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return Vector2.zero;
            float scaleX = canvasRT.rect.width / drawRect.width;
            float scaleY = canvasRT.rect.height / drawRect.height;
            // GUI Y вниз — компенсируем
            return new Vector2(deltaPreview.x * scaleX, -deltaPreview.y * scaleY);
        }

        private void BeginResize(int handleIdx, Vector2 mousePos)
        {
            if (_selected == null) return;
            _resizeHandle = handleIdx;
            _resizeMouseStart = mousePos;
            _resizeSizeStart = _selected.sizeDelta;
            _resizeAnchorStart = _selected.anchoredPosition;
            Undo.RecordObject(_selected, "Resize UI Element");
        }

        private void ProcessResize(Event e, Rect drawRect, int targetW, int targetH)
        {
            if (_selected == null) return;
            Vector2 deltaPreview = e.mousePosition - _resizeMouseStart;
            Vector2 deltaCanvas = PreviewDeltaToCanvasDelta(deltaPreview, drawRect);

            Vector2 newSize = _resizeSizeStart;
            Vector2 newAnchor = _resizeAnchorStart;
            // Координаты в canvas: Y вверх. deltaCanvas.y > 0 = курсор пошёл вверх.
            // Логика: edge_moved_by_delta. Если ребро TOP двинулось вверх (Δy > 0) — высота увеличилась.
            // Если ребро BOTTOM двинулось вверх (Δy > 0) — высота уменьшилась.
            // Центр обоих случаев двигается на Δ/2 в сторону сдвига ребра.
            //
            // 0=tl 1=t 2=tr 3=r 4=br 5=b 6=bl 7=l
            float dx = deltaCanvas.x;
            float dy = deltaCanvas.y;
            switch (_resizeHandle)
            {
                case 0: newSize.x -= dx; newSize.y += dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break; // TL
                case 1:                  newSize.y += dy;                              newAnchor.y += dy * 0.5f; break; // T
                case 2: newSize.x += dx; newSize.y += dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break; // TR
                case 3: newSize.x += dx;                  newAnchor.x += dx * 0.5f;                              break; // R
                case 4: newSize.x += dx; newSize.y -= dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break; // BR
                case 5:                  newSize.y -= dy;                              newAnchor.y += dy * 0.5f; break; // B
                case 6: newSize.x -= dx; newSize.y -= dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break; // BL
                case 7: newSize.x -= dx;                  newAnchor.x += dx * 0.5f;                              break; // L
            }

            // Snap
            if (_showGrid && _gridStep > 0.5f)
            {
                newSize.x = Mathf.Round(newSize.x / _gridStep) * _gridStep;
                newSize.y = Mathf.Round(newSize.y / _gridStep) * _gridStep;
            }
            newSize.x = Mathf.Max(8f, newSize.x);
            newSize.y = Mathf.Max(8f, newSize.y);

            _selected.sizeDelta = newSize;
            _selected.anchoredPosition = newAnchor;
            EditorUtility.SetDirty(_selected);
        }

        // ─── Правая панель: контекстный инспектор ──────────────────────────────

        private void DrawInspector(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            DrawRectBorder(new Rect(rect.x, rect.y, 1, rect.height), C_BORDER);

            if (_selected == null)
            {
                GUILayout.BeginArea(rect);
                GUILayout.Space(20);
                var st = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get(
                    "Select an element on the canvas or in the tree to edit it.",
                    "Выбери элемент на холсте или в дереве слева, чтобы редактировать его."), st);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginArea(rect);
            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            DrawInspectorHeader();
            DrawInspectorPositionSection();
            DrawInspectorAnchorSection();
            DrawInspectorTypeSpecificSections();
            DrawInspectorActionsSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawInspectorHeader()
        {
            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            string icon = GetDisplayIcon(_selected);
            string name = TruncateForHeader(GetDisplayName(_selected), 28);
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            iconSt.normal.textColor = C_ACCENT;
            GUILayout.Label(icon, iconSt, GUILayout.Width(22));
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, clipping = TextClipping.Clip };
            nameSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(name, nameSt);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, clipping = TextClipping.Clip };
            subSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(TruncateForHeader(_selected.gameObject.name, 36), subSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private static string TruncateForHeader(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 1) + "…";
        }

        private void DrawInspectorPositionSection()
        {
            DrawSectionLabel(ToolLang.Get("POSITION & SIZE", "ПОЛОЖЕНИЕ И РАЗМЕР"), "position");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(18));
            EditorGUI.BeginChangeCheck();
            float nx = EditorGUILayout.FloatField(_selected.anchoredPosition.x);
            GUILayout.Space(8);
            GUILayout.Label("Y", GUILayout.Width(18));
            float ny = EditorGUILayout.FloatField(_selected.anchoredPosition.y);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "Position");
                _selected.anchoredPosition = new Vector2(nx, ny);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("W", GUILayout.Width(18));
            EditorGUI.BeginChangeCheck();
            float nw = EditorGUILayout.FloatField(_selected.sizeDelta.x);
            GUILayout.Space(8);
            GUILayout.Label("H", GUILayout.Width(18));
            float nh = EditorGUILayout.FloatField(_selected.sizeDelta.y);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "Size");
                _selected.sizeDelta = new Vector2(nw, nh);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Rotation", "Поворот"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float rot = EditorGUILayout.FloatField(_selected.localEulerAngles.z);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "Rotation");
                var e = _selected.localEulerAngles;
                e.z = rot;
                _selected.localEulerAngles = e;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Scale", "Масштаб"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float sx = EditorGUILayout.FloatField(_selected.localScale.x);
            GUILayout.Space(8);
            float sy = EditorGUILayout.FloatField(_selected.localScale.y);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_selected, "Scale");
                _selected.localScale = new Vector3(sx, sy, _selected.localScale.z);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawInspectorAnchorSection()
        {
            DrawSectionLabel(ToolLang.Get("ANCHOR", "ЯКОРЬ"), "anchor");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            // 3×3 grid of preset buttons + center
            const float cell = 30f;
            Rect grid = GUILayoutUtility.GetRect(cell * 3 + 8, cell * 3 + 8, GUILayout.Width(cell * 3 + 8));
            EditorGUI.DrawRect(grid, C_BG_RAISED);
            DrawRectBorder(grid, C_BORDER);

            (string name, Vector2 min, Vector2 max)[] presets = new (string, Vector2, Vector2)[]
            {
                ("↖", new Vector2(0,1), new Vector2(0,1)), ("↑", new Vector2(0.5f,1), new Vector2(0.5f,1)), ("↗", new Vector2(1,1), new Vector2(1,1)),
                ("←", new Vector2(0,0.5f), new Vector2(0,0.5f)), ("●", new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f)), ("→", new Vector2(1,0.5f), new Vector2(1,0.5f)),
                ("↙", new Vector2(0,0), new Vector2(0,0)), ("↓", new Vector2(0.5f,0), new Vector2(0.5f,0)), ("↘", new Vector2(1,0), new Vector2(1,0)),
            };

            for (int i = 0; i < 9; i++)
            {
                int col = i % 3;
                int row = i / 3;
                Rect cellRect = new Rect(grid.x + 4 + col * cell, grid.y + 4 + row * cell, cell - 2, cell - 2);
                bool isCurrent = Mathf.Approximately(_selected.anchorMin.x, presets[i].min.x)
                              && Mathf.Approximately(_selected.anchorMin.y, presets[i].min.y)
                              && Mathf.Approximately(_selected.anchorMax.x, presets[i].max.x)
                              && Mathf.Approximately(_selected.anchorMax.y, presets[i].max.y);

                Color bg = isCurrent ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f) : C_BG_PRIMARY;
                if (cellRect.Contains(Event.current.mousePosition)) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
                EditorGUI.DrawRect(cellRect, bg);
                DrawRectBorder(cellRect, isCurrent ? C_ACCENT : C_BORDER);

                var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = isCurrent ? C_ACCENT : C_TEXT_2;
                GUI.Label(cellRect, presets[i].name, st);

                if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                {
                    SnapAnchorToCorner(presets[i].min, presets[i].max);
                    Event.current.Use();
                }
            }

            // 3 stretch presets
            GUILayout.Space(8);
            GUILayout.BeginVertical();

            DrawAnchorStretch(ToolLang.Get("Stretch H", "Растянуть по гор."), new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            DrawAnchorStretch(ToolLang.Get("Stretch V", "Растянуть по верт."), new Vector2(0.5f, 0), new Vector2(0.5f, 1));
            DrawAnchorStretch(ToolLang.Get("Fill", "Заполнить"), new Vector2(0, 0), new Vector2(1, 1));

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawAnchorStretch(string label, Vector2 min, Vector2 max)
        {
            bool isCurrent = _selected != null
                && Mathf.Approximately(_selected.anchorMin.x, min.x) && Mathf.Approximately(_selected.anchorMin.y, min.y)
                && Mathf.Approximately(_selected.anchorMax.x, max.x) && Mathf.Approximately(_selected.anchorMax.y, max.y);
            GUI.backgroundColor = isCurrent ? C_ACCENT : Color.white;
            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(140), GUILayout.Height(22)))
            {
                Undo.RecordObject(_selected, "Stretch Anchor");
                _selected.anchorMin = min;
                _selected.anchorMax = max;
                _selected.offsetMin = Vector2.zero;
                _selected.offsetMax = Vector2.zero;
            }
            GUI.backgroundColor = Color.white;
        }

        // "Прилипить к углу": ставим якорь и pivot в один угол, обнуляем anchoredPosition.
        // Так выбранный элемент визуально снэпится к указанному углу/центру родителя.
        // Важно: сохраняем актуальный размер (rect.size), чтобы stretch-элементы
        // (например, Dialogue_Text с anchor (0,0)..(1,1)) не схлопывались в точку.
        private void SnapAnchorToCorner(Vector2 cornerMin, Vector2 cornerMax)
        {
            if (_selected == null) return;
            Undo.RecordObject(_selected, "Set Anchor");

            // Запомним реальный размер, т.к. при смене anchor с stretch на corner
            // sizeDelta теряет смысл и нужно восстановить размер вручную.
            Vector2 actualSize = _selected.rect.size;

            _selected.anchorMin = cornerMin;
            _selected.anchorMax = cornerMax;

            if (Mathf.Approximately(cornerMin.x, cornerMax.x) && Mathf.Approximately(cornerMin.y, cornerMax.y))
            {
                _selected.pivot = cornerMin;
                _selected.anchoredPosition = Vector2.zero;
                _selected.sizeDelta = actualSize;
            }
            EditorUtility.SetDirty(_selected);
        }

        private void DrawInspectorTypeSpecificSections()
        {
            // Image
            var img = _selected.GetComponent<Image>();
            if (img != null) DrawImageSection(img);

            // Raw Image (тоже встречается — поддержим базово через Image-блок если есть)
            // (опускаем, чтобы не путать)

            // Text (TMP) — приоритет, т.к. у Novella по умолчанию TMP
            var tmp = _selected.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                DrawTextSection(tmp);
            }
            else
            {
                // Legacy UnityEngine.UI.Text
                var legacy = _selected.GetComponent<UnityEngine.UI.Text>();
                if (legacy != null) DrawLegacyTextSection(legacy);
            }

            // Button
            var btn = _selected.GetComponent<Button>();
            if (btn != null) DrawButtonSection(btn);
        }

        private void DrawImageSection(Image img)
        {
            DrawSectionLabel(ToolLang.Get("IMAGE", "ИЗОБРАЖЕНИЕ"), "image");
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(72));
            string spName = img.sprite != null ? img.sprite.name : ToolLang.Get("(none)", "(не выбрано)");
            if (GUILayout.Button(spName, EditorStyles.popup, GUILayout.Height(20)))
            {
                var captured = img;
                NovellaGalleryWindow.ShowWindow((obj) =>
                {
                    if (captured == null) return;
                    Sprite picked = null;
                    if (obj is Sprite sp) picked = sp;
                    else if (obj is Texture2D tex)
                    {
                        string p = AssetDatabase.GetAssetPath(tex);
                        picked = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    }
                    if (picked != null)
                    {
                        Undo.RecordObject(captured, "Sprite");
                        captured.sprite = picked;
                        EditorUtility.SetDirty(captured);
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image, "");
            }
            if (img.sprite != null && GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(20)))
            {
                Undo.RecordObject(img, "Clear Sprite");
                img.sprite = null;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var col = EditorGUILayout.ColorField(img.color);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(img, "Color"); img.color = col; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Type", "Тип"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var t = (Image.Type)EditorGUILayout.EnumPopup(img.type);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(img, "Image Type"); img.type = t; }
            GUILayout.EndHorizontal();

            if (img.type == Image.Type.Filled)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Fill", "Заполн."), GUILayout.Width(72));
                EditorGUI.BeginChangeCheck();
                float fill = EditorGUILayout.Slider(img.fillAmount, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(img, "Fill"); img.fillAmount = fill; }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawTextSection(TMP_Text txt)
        {
            DrawSectionLabel(ToolLang.Get("TEXT STYLE (TMP)", "СТИЛЬ ТЕКСТА (TMP)"), "text");

            // Подсказка про локализацию
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            hintSt.normal.textColor = C_TEXT_4;
            GUILayout.Label("ℹ " + ToolLang.Get(
                "Actual text comes from localization (RU/EN). Edit it in the Localization tab.",
                "Сам текст приходит из локализации (RU/EN). Редактируй его на вкладке локализации."), hintSt);
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font", "Шрифт"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var newFont = (TMP_FontAsset)EditorGUILayout.ObjectField(txt.font, typeof(TMP_FontAsset), false);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Font"); txt.font = newFont; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font Size", "Размер"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float fs = EditorGUILayout.FloatField(txt.fontSize);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Font Size"); txt.fontSize = fs; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var col = EditorGUILayout.ColorField(txt.color);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Text Color"); txt.color = col; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Align", "Выравн."), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var al = (TextAlignmentOptions)EditorGUILayout.EnumPopup(txt.alignment);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Align"); txt.alignment = al; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Bold", "Жирный"), GUILayout.Width(72));
            bool isBold = (txt.fontStyle & FontStyles.Bold) != 0;
            EditorGUI.BeginChangeCheck();
            bool nb = EditorGUILayout.Toggle(isBold);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(txt, "Bold");
                if (nb) txt.fontStyle |= FontStyles.Bold;
                else txt.fontStyle &= ~FontStyles.Bold;
            }
            GUILayout.EndHorizontal();

            // Конвертация Legacy → TMP, если рядом нет TMP но юзер хочет
            // (опускаем — у нас здесь и так TMP).

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawLegacyTextSection(UnityEngine.UI.Text txt)
        {
            DrawSectionLabel(ToolLang.Get("TEXT STYLE (Legacy)", "СТИЛЬ ТЕКСТА (Legacy)"), "text");

            // Предупреждение про legacy + подсказка локализации
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var warnSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            warnSt.normal.textColor = new Color(1f, 0.78f, 0.20f);
            GUILayout.Label("⚠ " + ToolLang.Get(
                "Legacy UI Text. Recommended: convert to TextMeshPro for sharper rendering.",
                "Устаревший UI Text. Рекомендуется конвертировать в TextMeshPro — чёткость лучше."), warnSt);
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            hintSt.normal.textColor = C_TEXT_4;
            GUILayout.Label("ℹ " + ToolLang.Get(
                "Actual text comes from localization (RU/EN).",
                "Сам текст приходит из локализации (RU/EN)."), hintSt);
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font", "Шрифт"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var newFont = (Font)EditorGUILayout.ObjectField(txt.font, typeof(Font), false);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Font"); txt.font = newFont; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font Size", "Размер"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            int fs = EditorGUILayout.IntField(txt.fontSize);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Font Size"); txt.fontSize = fs; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var col = EditorGUILayout.ColorField(txt.color);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Text Color"); txt.color = col; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Align", "Выравн."), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var al = (TextAnchor)EditorGUILayout.EnumPopup(txt.alignment);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Align"); txt.alignment = al; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Style", "Стиль"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var st = (FontStyle)EditorGUILayout.EnumPopup(txt.fontStyle);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(txt, "Style"); txt.fontStyle = st; }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawButtonSection(Button btn)
        {
            DrawSectionLabel(ToolLang.Get("BUTTON", "КНОПКА"), "button");
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Interactable", "Активна"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            bool ni = EditorGUILayout.Toggle(btn.interactable);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(btn, "Interactable"); btn.interactable = ni; }
            GUILayout.EndHorizontal();

            var c = btn.colors;
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Normal", "Обычная"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var nc = EditorGUILayout.ColorField(c.normalColor);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(btn, "Btn Color"); c.normalColor = nc; btn.colors = c; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Highlight", "Hover"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var hc = EditorGUILayout.ColorField(c.highlightedColor);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(btn, "Btn Hover"); c.highlightedColor = hc; btn.colors = c; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Pressed", "Нажата"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var pc = EditorGUILayout.ColorField(c.pressedColor);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(btn, "Btn Pressed"); c.pressedColor = pc; btn.colors = c; }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawInspectorActionsSection()
        {
            DrawSectionLabel(ToolLang.Get("ACTIONS", "ДЕЙСТВИЯ"));
            if (_selected == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            if (GUILayout.Button(ToolLang.Get("📋 Duplicate", "📋 Дублировать"), GUILayout.Height(28), GUILayout.ExpandWidth(true)))
            {
                DuplicateSelected();
            }

            GUILayout.Space(4);

            GUI.backgroundColor = C_DANGER;
            if (GUILayout.Button(ToolLang.Get("🗑 Delete", "🗑 Удалить"), GUILayout.Height(28), GUILayout.ExpandWidth(true)))
            {
                DeleteSelected();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
        }

        private void DuplicateSelected()
        {
            if (_selected == null) return;
            var dup = UnityEngine.Object.Instantiate(_selected.gameObject, _selected.parent);
            dup.name = _selected.gameObject.name + " (Copy)";
            Undo.RegisterCreatedObjectUndo(dup, "Duplicate");
            _selected = dup.GetComponent<RectTransform>();
            RefreshRectsCache();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete element?", "Удалить элемент?"),
                string.Format(ToolLang.Get("Delete '{0}'? This cannot be undone easily.", "Удалить «{0}»? Отменить будет непросто."),
                    GetDisplayName(_selected)),
                ToolLang.Get("Delete", "Удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            Undo.DestroyObjectImmediate(_selected.gameObject);
            _selected = null;
            RefreshRectsCache();
        }

        // ─── Утилиты ───────────────────────────────────────────────────────────

        private static void DrawSectionLabel(string text, string helpKey = null)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = new Color(0.62f, 0.63f, 0.69f);
            GUILayout.Label(text, st, GUILayout.Width(180));

            if (!string.IsNullOrEmpty(helpKey))
            {
                Rect btnRect = GUILayoutUtility.GetRect(18, 14, GUILayout.Width(18), GUILayout.Height(14));
                bool hover = btnRect.Contains(Event.current.mousePosition);
                var qSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                qSt.normal.textColor = hover ? new Color(0.36f, 0.75f, 0.92f) : new Color(0.62f, 0.63f, 0.69f);
                GUI.Label(btnRect, "?", qSt);
                if (Event.current.type == EventType.MouseDown && hover)
                {
                    NovellaUIForgeTipPopup.Show(GUIUtility.GUIToScreenRect(btnRect), helpKey);
                    Event.current.Use();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Help: словарь подсказок + два UI-варианта — компактный поповер у "?" иконки
    // и большое окно (запасной вариант через тулбар-кнопку).
    // ════════════════════════════════════════════════════════════════════════
    internal static class NovellaUIForgeHelpDB
    {
        public static (string title, string body) Get(string key)
        {
            switch (key)
            {
                case "position":
                    return (
                        "📐 " + ToolLang.Get("Position & Size", "Положение и размер"),
                        ToolLang.Get(
                            "X/Y — offset from the anchor. W/H — width and height in pixels.\n\nYou rarely need to type these: just drag the element on the canvas, and drag the white square handles to resize.",
                            "X/Y — смещение от якоря. W/H — ширина и высота в пикселях.\n\nЧасто их вводить вручную не нужно: просто тяни элемент мышкой по холсту, а белые квадратики по углам — меняют размер.")
                    );
                case "anchor":
                    return (
                        "🎯 " + ToolLang.Get("Anchor", "Якорь"),
                        ToolLang.Get(
                            "The anchor is the point of the parent that your element 'sticks to'.\n\nClick a corner to snap the element there: it will hold its distance from that corner when the screen size changes.\n\n'Stretch H / V / Fill' — make the element grow together with the parent.",
                            "Якорь — это точка родителя, к которой «прилипает» элемент.\n\nКликни в один из 9 углов — элемент прыгнет туда. При смене разрешения экрана он будет держать расстояние от этого угла.\n\n«Растянуть гор. / верт. / Заполнить» — заставит элемент расти вместе с родителем.")
                    );
                case "image":
                    return (
                        "🖼 " + ToolLang.Get("Image", "Изображение"),
                        ToolLang.Get(
                            "Sprite — what is drawn. Click the field and pick from the project gallery.\nColor — tints the image (full white = no tint).\nType → Filled — useful for HP bars and progress fills (control via 'Fill amount').",
                            "Спрайт — что нарисовано. Кликни поле и выбери из галереи проекта.\nЦвет — окрашивает картинку (полностью белый = без тонировки).\nТип → Filled — удобно для HP-баров и шкал прогресса (управляется через «Заполн.»).")
                    );
                case "text":
                    return (
                        "𝐓 " + ToolLang.Get("Text", "Текст"),
                        ToolLang.Get(
                            "Here you only set the look: font, size, color, alignment, bold.\n\nThe actual text content comes from localization (RU/EN tables) and is edited in the Localization tab — not here. So translations stay consistent for all languages.",
                            "Здесь только внешний вид: шрифт, размер, цвет, выравнивание, жирный.\n\nСам текст берётся из локализации (таблицы RU/EN) и редактируется на вкладке Локализации — не здесь. Так перевод остаётся консистентным на всех языках.")
                    );
                case "button":
                    return (
                        "🔘 " + ToolLang.Get("Button", "Кнопка"),
                        ToolLang.Get(
                            "A clickable element with three colors:\n• Normal — resting\n• Highlight — mouse hovers over\n• Pressed — being clicked\n\nThe button text usually lives as a child element inside the button.",
                            "Кликабельный элемент с тремя цветами:\n• Обычная — состояние покоя\n• Hover — мышь над кнопкой\n• Нажата — в момент клика\n\nТекст кнопки обычно лежит дочерним элементом внутри кнопки.")
                    );
                case "canvas":
                    return (
                        "🧱 Canvas",
                        ToolLang.Get(
                            "The 'sheet' on top of which all UI lives. The editor configures it automatically — you usually don't touch it directly. One Canvas = one screen of your novel.",
                            "«Лист», поверх которого живёт весь интерфейс. Редактор настраивает его автоматически — обычно сам ты в него не лазишь. Один Canvas = один экран новеллы.")
                    );
                default:
                    return ("", "");
            }
        }
    }

    // Поповер у "?" иконки. Появляется как dropdown ровно у курсора, исчезает по клику вне.
    public class NovellaUIForgeTipPopup : EditorWindow
    {
        private string _title;
        private string _body;

        public static void Show(Rect screenAnchor, string helpKey)
        {
            var (title, body) = NovellaUIForgeHelpDB.Get(helpKey);
            if (string.IsNullOrEmpty(title)) return;

            var win = CreateInstance<NovellaUIForgeTipPopup>();
            win._title = title;
            win._body = body;
            win.ShowAsDropDown(screenAnchor, new Vector2(360, 180));
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.13f, 0.14f, 0.18f));

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            var ttl = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            ttl.normal.textColor = new Color(0.36f, 0.75f, 0.92f);
            GUILayout.Label(_title, ttl);
            GUILayout.Space(4);

            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), new Color(0.165f, 0.176f, 0.243f));
            GUILayout.Space(6);

            var bd = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            bd.normal.textColor = new Color(0.85f, 0.87f, 0.93f);
            GUILayout.Label(_body, bd);

            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
        }
    }

    // Большое окно — оставляем как fallback из тулбара (полный гид).
    public class NovellaUIForgeHelpWindow : EditorWindow
    {
        private Vector2 _scroll;

        public static void ShowHelp()
        {
            var win = GetWindow<NovellaUIForgeHelpWindow>(true, ToolLang.Get("UI Forge — Help", "Кузница UI — Помощь"), true);
            win.minSize = new Vector2(520, 460);
            win.maxSize = new Vector2(820, 900);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.075f, 0.078f, 0.106f));

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUILayout.BeginVertical();

            var h1 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            h1.normal.textColor = new Color(0.93f, 0.93f, 0.96f);
            GUILayout.Label("❓ " + ToolLang.Get("UI Forge — quick guide", "Кузница UI — краткий гид"), h1);

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            sub.normal.textColor = new Color(0.78f, 0.80f, 0.86f);
            GUILayout.Label(ToolLang.Get(
                "Tip: each section in the inspector has a small '?' next to its name — click it for a short explanation.",
                "Подсказка: у каждой секции инспектора рядом с названием есть «?» — кликни и появится короткое объяснение."), sub);

            GUILayout.Space(12);
            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var key in new[] { "canvas", "anchor", "position", "image", "text", "button" })
            {
                var (t, b) = NovellaUIForgeHelpDB.Get(key);
                if (string.IsNullOrEmpty(t)) continue;
                var ttl = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
                ttl.normal.textColor = new Color(0.36f, 0.75f, 0.92f);
                GUILayout.Label(t, ttl);
                GUILayout.Space(2);
                var bd = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
                bd.normal.textColor = new Color(0.85f, 0.87f, 0.93f);
                GUILayout.Label(b, bd);
                GUILayout.Space(10);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), new Color(0.165f, 0.176f, 0.243f));
                GUILayout.Space(8);
            }

            GUILayout.Space(10);

            // Дополнительные подсказки про управление
            var ttl2 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            ttl2.normal.textColor = new Color(0.36f, 0.75f, 0.92f);
            GUILayout.Label("⌨ " + ToolLang.Get("Hotkeys & gestures", "Горячие клавиши и жесты"), ttl2);
            GUILayout.Space(2);
            var bd2 = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            bd2.normal.textColor = new Color(0.85f, 0.87f, 0.93f);
            GUILayout.Label(ToolLang.Get(
                "• Left click on the canvas — select element\n• Right click on canvas/tree — create new element inside\n• Drag — move; drag handles — resize\n• Del — delete selected\n• Ctrl+Z / Ctrl+Y — undo/redo",
                "• ЛКМ по холсту — выбрать элемент\n• ПКМ по холсту/дереву — создать новый элемент внутри\n• Drag — двигать; тяни за квадратики — менять размер\n• Del — удалить выделенный\n• Ctrl+Z / Ctrl+Y — отменить/вернуть"), bd2);

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            if (GUILayout.Button(ToolLang.Get("Got it", "Понятно"), GUILayout.Height(32)))
                Close();

            GUILayout.EndVertical();
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }
    }
}
