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
            list.Add(rt);
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null) CollectRectsRecursive(ch, list, depth + 1);
            }
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
                DrawTreeRow(rt, name, GetDisplayIcon(rt), depth);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
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

        private void DrawTreeRow(RectTransform rt, string name, string icon, int depth)
        {
            bool isSel = rt == _selected;

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            Rect row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUILayout.Space(8);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, isSel ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f) : Color.clear);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            float indent = 6f + depth * 12f;

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = isSel ? C_ACCENT : C_TEXT_3;
            GUI.Label(new Rect(row.x + indent, row.y, 18, row.height), icon, iconSt);

            var nameSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            nameSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(row.x + indent + 20, row.y, row.width - indent - 24, row.height), name, nameSt);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _selected = rt;
                _window?.Repaint();
                Event.current.Use();
            }
        }

        // ─── Центр: интерактивный canvas ───────────────────────────────────────

        private void DrawCenterCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);

            // Topbar для канваса
            const float topH = 38f;
            Rect topbar = new Rect(rect.x, rect.y, rect.width, topH);
            EditorGUI.DrawRect(topbar, C_BG_SIDE);
            DrawRectBorder(new Rect(topbar.x, topbar.yMax - 1, topbar.width, 1), C_BORDER);

            DrawCanvasTopbar(topbar);

            Rect viewport = new Rect(rect.x, rect.y + topH, rect.width, rect.height - topH);

            if (_camera == null) return;

            // Целевое разрешение
            int targetW = _isMobileMode ? 1080 : 1920;
            int targetH = _isMobileMode ? 1920 : 1080;

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
            GUILayout.BeginArea(topbar);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            GUILayout.Space(4);
            GUILayout.Label(_isMobileMode ? "📱 1080×1920" : "🖥 1920×1080",
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = C_TEXT_1 }, alignment = TextAnchor.MiddleLeft },
                GUILayout.Width(110), GUILayout.Height(38));

            // Toggle desktop / mobile
            GUI.backgroundColor = _isMobileMode ? Color.white : C_ACCENT;
            if (GUILayout.Button("🖥 Desktop", EditorStyles.toolbarButton, GUILayout.Width(90), GUILayout.Height(22)))
            {
                if (_isMobileMode) { _isMobileMode = false; ResetPreviewTexture(); }
            }
            GUI.backgroundColor = _isMobileMode ? C_ACCENT : Color.white;
            if (GUILayout.Button("📱 Mobile", EditorStyles.toolbarButton, GUILayout.Width(90), GUILayout.Height(22)))
            {
                if (!_isMobileMode) { _isMobileMode = true; ResetPreviewTexture(); }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(20);

            _showGrid = GUILayout.Toggle(_showGrid, ToolLang.Get(" Grid", " Сетка"), EditorStyles.toolbarButton, GUILayout.Height(22));
            _showSafeArea = GUILayout.Toggle(_showSafeArea, ToolLang.Get(" Safe", " Безопасная зона"), EditorStyles.toolbarButton, GUILayout.Height(22));

            GUILayout.FlexibleSpace();

            GUILayout.Label("Zoom", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_3 } }, GUILayout.Width(40));
            _previewZoom = GUILayout.HorizontalSlider(_previewZoom, 0.25f, 1.5f, GUILayout.Width(120));
            GUILayout.Label((_previewZoom * 100f).ToString("F0") + "%", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_3 } }, GUILayout.Width(40));

            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
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

            if (e.type == EventType.MouseDown && drawRect.Contains(e.mousePosition))
            {
                // Если кликнули по handle выбранного элемента — начать resize
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

                    // Внутри selection rect — начать drag
                    if (selRect.Contains(e.mousePosition))
                    {
                        BeginDrag(e.mousePosition);
                        e.Use();
                        return;
                    }
                }

                // Иначе — pick новый элемент под курсором
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
            string name = GetDisplayName(_selected);
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            iconSt.normal.textColor = C_ACCENT;
            GUILayout.Label(icon, iconSt, GUILayout.Width(22));
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            nameSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(name, nameSt);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            subSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(_selected.gameObject.name, subSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawInspectorPositionSection()
        {
            DrawSectionLabel(ToolLang.Get("POSITION & SIZE", "ПОЛОЖЕНИЕ И РАЗМЕР"));

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
            DrawSectionLabel(ToolLang.Get("ANCHOR", "ЯКОРЬ"));

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
        private void SnapAnchorToCorner(Vector2 cornerMin, Vector2 cornerMax)
        {
            if (_selected == null) return;
            Undo.RecordObject(_selected, "Set Anchor");
            _selected.anchorMin = cornerMin;
            _selected.anchorMax = cornerMax;
            // Если это пресет угла (anchorMin == anchorMax), синхронизируем pivot.
            if (Mathf.Approximately(cornerMin.x, cornerMax.x) && Mathf.Approximately(cornerMin.y, cornerMax.y))
            {
                _selected.pivot = cornerMin;
            }
            _selected.anchoredPosition = Vector2.zero;
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
            DrawSectionLabel(ToolLang.Get("IMAGE", "ИЗОБРАЖЕНИЕ"));
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var newSp = (Sprite)EditorGUILayout.ObjectField(img.sprite, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(img, "Sprite"); img.sprite = newSp; }
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
            DrawSectionLabel(ToolLang.Get("TEXT STYLE (TMP)", "СТИЛЬ ТЕКСТА (TMP)"));

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
            DrawSectionLabel(ToolLang.Get("TEXT STYLE (Legacy)", "СТИЛЬ ТЕКСТА (Legacy)"));

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
            DrawSectionLabel(ToolLang.Get("BUTTON", "КНОПКА"));
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
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            if (_selected != null)
            {
                if (GUILayout.Button(ToolLang.Get("📋 Duplicate", "📋 Дублировать"), GUILayout.Height(28)))
                {
                    DuplicateSelected();
                }

                GUI.backgroundColor = C_DANGER;
                if (GUILayout.Button(ToolLang.Get("🗑 Delete", "🗑 Удалить"), GUILayout.Height(28)))
                {
                    DeleteSelected();
                }
                GUI.backgroundColor = Color.white;
            }

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

        private static void DrawSectionLabel(string text)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = new Color(0.62f, 0.63f, 0.69f);
            GUILayout.Label(text, st);
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
}
