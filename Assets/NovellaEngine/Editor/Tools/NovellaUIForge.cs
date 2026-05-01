// ════════════════════════════════════════════════════════════════════════════
// Novella UI Forge — новый редактор интерфейса.
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

        [MenuItem("Tools/Novella Engine/🎨 UI Master Forge", false, 2)]
        public static void ShowWindow()
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.SwitchToModule(3);
        }

        public static void OpenWithCustomPrefab(GameObject targetPrefab)
        {
            ShowWindow();
            if (targetPrefab != null) Debug.Log($"[Novella] OpenWithCustomPrefab: префаб '{targetPrefab.name}' будет поддерживаться в следующей версии.");
        }

        // Dynamic — из Settings
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        // Все производные цвета — из Settings (см. NovellaSettingsModule).
        // Они рассчитываются автоматически от Interface/Text цветов, поэтому
        // достаточно указать в Settings 2 цвета — остальные подстроятся.
        private static Color C_BG_SIDE   => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER    => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1    => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2    => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3    => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4    => NovellaSettingsModule.GetTextDisabled();
        // Акцентный цвет — динамический, из Settings-модуля Hub'а.
        // Кэшируется внутри Settings, поэтому чтение дешёвое.
        private static Color C_ACCENT => NovellaSettingsModule.GetAccentColor();
        private static readonly Color C_DANGER = new Color(0.85f, 0.32f, 0.32f);
        private static readonly Color C_SUCCESS = new Color(0.40f, 0.78f, 0.45f);

        private EditorWindow _window;
        private NovellaPlayer _player;
        private StoryLauncher _launcher;
        private Camera _camera;
        private Canvas _canvas;
        private RenderTexture _previewTexture;

        private List<RectTransform> _allRects = new();
        private readonly Dictionary<GameObject, string> _friendlyNames = new();
        private readonly Dictionary<GameObject, string> _friendlyIcons = new();

        // ─── Multi-Select ───
        private List<RectTransform> _selectedList = new List<RectTransform>();
        private RectTransform FirstSelected => _selectedList.Count > 0 ? _selectedList[0] : null;
        private bool HasMultiple => _selectedList.Count > 1;
        private int _lastSelectedTreeIndex = -1;

        private string _pendingRename;
        private RectTransform _renameTarget;

        private bool _isMobileMode;
        private float _previewZoom = 1f;
        private bool _showSafeArea;
        private bool _showGrid = true;
        private bool _showGuideMode = false;
        private float _gridStep = 20f;

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

        // Drag & Resize
        private bool _dragging;
        private Vector2 _dragMouseStart;
        private Dictionary<RectTransform, Vector2> _dragAnchorStarts = new Dictionary<RectTransform, Vector2>();

        private int _resizeHandle = -1;
        private Vector2 _resizeMouseStart;
        private Vector2 _resizeSizeStart;
        private Vector2 _resizeAnchorStart;
        private const float HANDLE_SIZE_PX = 8f;
        private double _lastCanvasClickTime = 0;

        // Tree drag threshold: чтобы случайное микро-движение мыши не запускало DnD.
        // Настоящий drag начнётся только если курсор сместился больше чем на TREE_DRAG_THRESHOLD пикселей.
        private bool _treeDragArmed;     // мышь зажата над строкой и мы готовы начать drag по факту движения
        private Vector2 _treeMouseDownPos;
        private const float TREE_DRAG_THRESHOLD = 8f;

        private string _treeFilter = "";
        private Vector2 _treeScroll;
        private Vector2 _inspectorScroll;

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
            if (_window != null && _window == EditorWindow.focusedWindow) _window.Repaint();
            else if (_dragging || _resizeHandle >= 0) _window?.Repaint();
        }

        // Возвращает уникальное в пределах parent имя в формате:  Base, Base (1), Base (2)…
        // Снимает любые суффиксы вида "(Copy)", "(копия)", "(N)" перед поиском, чтобы не плодить
        // "(копия) (копия) (копия)" при многократном дублировании.
        private string EnforceUniqueName(Transform parent, string desiredName, GameObject ignoreObj)
        {
            if (parent == null) return desiredName;

            // Сначала чистим имя от хвостов
            string baseName = StripCopyOrIndexSuffix(desiredName);

            // Если такого имени ещё нет — отдаём чистый base
            if (!HasSibling(parent, baseName, ignoreObj)) return baseName;

            // Иначе подбираем номер
            for (int n = 1; n < 9999; n++)
            {
                string candidate = $"{baseName} ({n})";
                if (!HasSibling(parent, candidate, ignoreObj)) return candidate;
            }
            return baseName;
        }

        private static bool HasSibling(Transform parent, string name, GameObject ignoreObj)
        {
            foreach (Transform child in parent)
            {
                if (child.gameObject != ignoreObj && child.name == name) return true;
            }
            return false;
        }

        private static string StripCopyOrIndexSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r = s;
            // Снимаем по очереди в конце все стандартные хвосты:
            // " (1)", " (Copy)", " (Clone)", " (копия)", " (Копия)", " (клон)" и т.п.
            var rgx = new System.Text.RegularExpressions.Regex(@"\s*\((?:\d+|copy|clone|копия|клон)\)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            while (true)
            {
                var match = rgx.Match(r);
                if (!match.Success) break;
                r = r.Substring(0, match.Index);
            }
            return r.Length > 0 ? r : s;
        }

        public void DrawGUI(Rect position)
        {
            if (_camera == null) FindReferences();

            Event ev = Event.current;
            if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Delete && _selectedList.Count > 0)
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

            const float treeW = 280f; // Сделал чуть шире для кнопок вверх/вниз
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

            if (canProceed) statusText = ToolLang.Get("Loading…", "Загрузка…");
            else if (_camera == null && _canvas == null)
                statusText = ToolLang.Get("Scene seems to be empty. Open the Scene Manager and load a scene that has a Camera and a Canvas.", "В сцене ничего не найдено. Открой «Менеджер Сцен» и загрузи сцену с камерой и UI Canvas.");
            else if (_camera == null)
                statusText = ToolLang.Get("Canvas found, but no Camera in scene. Add a Camera or load a scene preset.", "Canvas нашёлся, но в сцене нет камеры. Добавь камеру или загрузи пресет сцены.");
            else
                statusText = ToolLang.Get("Camera found, but no UI Canvas. Add a Canvas to the scene or load a scene preset.", "Камера нашлась, но в сцене нет UI Canvas. Добавь Canvas или загрузи пресет сцены.");
            GUILayout.Label(statusText, subSt);

            GUILayout.Space(10);

            Rect diagRect = GUILayoutUtility.GetRect(0, 92, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(diagRect, C_BG_RAISED);
            DrawRectBorder(diagRect, C_BORDER);

            var diagSt = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            diagSt.normal.textColor = C_TEXT_2;
            float y = diagRect.y + 8;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18), (_camera != null ? "✅" : "❌") + "  " + ToolLang.Get("Camera", "Камера") + (_camera != null ? "  ·  " + _camera.gameObject.name : ""), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18), (_canvas != null ? "✅" : "❌") + "  " + ToolLang.Get("UI Canvas", "UI Canvas") + (_canvas != null ? "  ·  " + _canvas.gameObject.name : ""), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18), (_player != null ? "✅" : "⚪") + "  NovellaPlayer" + (_player != null ? "  ·  " + _player.gameObject.name : "  " + ToolLang.Get("(optional)", "(необязательно)")), diagSt);
            y += 18;
            GUI.Label(new Rect(diagRect.x + 12, y, diagRect.width - 24, 18), (_launcher != null ? "✅" : "⚪") + "  StoryLauncher" + (_launcher != null ? "  ·  " + _launcher.gameObject.name : "  " + ToolLang.Get("(optional)", "(необязательно)")), diagSt);

            GUILayout.Space(14);

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

            var canvasGo = new GameObject(ToolLang.Get("Canvas", "Холст"));
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

        private void FindReferences()
        {
            _camera = Camera.main;
            if (_camera == null) _camera = UnityEngine.Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

            _player = UnityEngine.Object.FindAnyObjectByType<NovellaPlayer>(FindObjectsInactive.Include);
            _launcher = UnityEngine.Object.FindAnyObjectByType<StoryLauncher>(FindObjectsInactive.Include);

            _canvas = null;

            if (_player != null && _player.DialoguePanel != null) _canvas = _player.DialoguePanel.GetComponentInParent<Canvas>(true);
            else if (_launcher != null && _launcher.StoriesContainer != null) _canvas = _launcher.StoriesContainer.GetComponentInParent<Canvas>(true);

            if (_canvas == null)
            {
                if (_player != null) _canvas = _player.GetComponentInParent<Canvas>(true) ?? _player.GetComponentInChildren<Canvas>(true);
                else if (_launcher != null) _canvas = _launcher.GetComponentInParent<Canvas>(true) ?? _launcher.GetComponentInChildren<Canvas>(true);
            }

            if (_canvas == null)
            {
                var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                _canvas = allCanvases.FirstOrDefault(c => c.isRootCanvas) ?? allCanvases.FirstOrDefault();
            }

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
                "DialoguePanel" => ToolLang.Get("Dialogue Panel", "Панель диалога"),
                "SpeakerNameText" => ToolLang.Get("Speaker Name", "Имя говорящего"),
                "DialogueBodyText" => ToolLang.Get("Dialogue Text", "Текст диалога"),
                "SaveNotification" => ToolLang.Get("Save Notification", "Уведомление сохранения"),
                "ChoiceContainer" => ToolLang.Get("Choices", "Кнопки выбора"),
                "ChoiceButtonPrefab" => ToolLang.Get("Choice Button (Prefab)", "Кнопка выбора (префаб)"),
                "CharactersContainer" => ToolLang.Get("Characters", "Персонажи"),
                "MainMenuPanel" => ToolLang.Get("Main Menu", "Главное меню"),
                "StoriesPanel" => ToolLang.Get("Stories", "Список историй"),
                "MCCreationPanel" => ToolLang.Get("Character Creation", "Создание персонажа"),
                "MCConfirmButton" => ToolLang.Get("Confirm Button", "Кнопка «Готово»"),
                "MCAvatarPreview" => ToolLang.Get("Avatar", "Аватар"),
                "MCPrevLookButton" => ToolLang.Get("Prev Look", "← Предыдущий вид"),
                "MCNextLookButton" => ToolLang.Get("Next Look", "Следующий вид →"),
                "StoriesContainer" => ToolLang.Get("Stories List", "Лента историй"),
                "StoryButtonPrefab" => ToolLang.Get("Story Card (Prefab)", "Карточка истории (префаб)"),
                _ => fieldName
            };
        }

        private static string IconFor(string fieldName, GameObject go)
        {
            if (go.GetComponent<Button>() != null) return "🔘";
            if (go.GetComponent<TextMeshProUGUI>() != null || go.GetComponent<UnityEngine.UI.Text>() != null) return "𝐓";
            if (go.GetComponent<Image>() != null) return "🖼";
            if (fieldName.Contains("Container") || fieldName.Contains("Panel")) return "▣";
            return "◆";
        }

        private void RefreshRectsCache()
        {
            _allRects.Clear();
            // Раньше: брали ТОЛЬКО _canvas. Если в сцене дубликат канваса
            // (например после Duplicate), его дочерние элементы не отображались
            // в дереве, хотя камера их рендерила. Теперь собираем все root-канвасы.
            var allCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in allCanvases)
            {
                if (c == null || !c.isRootCanvas) continue;
                var rt = c.GetComponent<RectTransform>();
                if (rt != null) CollectRectsRecursive(rt, _allRects, 0);
            }
        }

        private static void CollectRectsRecursive(RectTransform rt, List<RectTransform> list, int depth)
        {
            if (rt == null || depth > 16) return;
            if (IsHiddenInternal(rt)) return;
            list.Add(rt);
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null) CollectRectsRecursive(ch, list, depth + 1);
            }
        }

        private static bool IsHiddenInternal(RectTransform rt)
        {
            if (rt == null) return true;
            var go = rt.gameObject;
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) return true;
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

        private void DrawElementsTree(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            DrawRectBorder(new Rect(rect.xMax - 1, rect.y, 1, rect.height), C_BORDER);

            GUILayout.BeginArea(rect);

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🗂  " + ToolLang.Get("Scene Elements", "Элементы сцены"), titleSt);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(string.Format(ToolLang.Get("{0} elements found", "{0} элементов"), _allRects.Count), subSt);
            GUILayout.EndHorizontal();

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
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y, searchRect.width, searchRect.height), "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }

            GUILayout.Space(8);

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

            // Запасной "хвост" внизу — если бросить сюда, переносим в корень Canvas.
            // Это позволяет вытаскивать элемент из родителя (на верхний уровень).
            GUILayout.Space(4);
            Rect dropToRootRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            HandleDropToRoot(dropToRootRect);

            GUILayout.EndScrollView();

            GUILayout.Space(8);
            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            hint.normal.textColor = C_TEXT_4;
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("💡 " + ToolLang.Get(
                "Drag rows to reorder. Drop in middle = make child, drop on edge = sibling. Drop on bottom strip = move to root.",
                "Тяни строки. По центру — сделать дочерним, на край — поставить рядом. На нижнюю полосу — вытащить в корень."), hint);
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.EndArea();
        }

        // Зона для drop-on-root: визуальная подсказка + обработка.
        private void HandleDropToRoot(Rect r)
        {
            Event e = Event.current;
            bool dragActive = DragAndDrop.objectReferences.Length > 0;
            bool hover = r.Contains(e.mousePosition) && dragActive;

            if (e.type == EventType.Repaint)
            {
                if (dragActive)
                {
                    EditorGUI.DrawRect(r, hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.25f) : new Color(1, 1, 1, 0.04f));
                    DrawRectBorder(r, hover ? C_ACCENT : new Color(1, 1, 1, 0.1f));
                    var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                    st.normal.textColor = hover ? C_TEXT_1 : C_TEXT_3;
                    GUI.Label(r, ToolLang.Get("⤴ Drop here to move to root", "⤴ Бросить сюда — поднять в корень"), st);
                }
            }

            if (hover && _canvas != null)
            {
                if (e.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    e.Use();
                    _window?.Repaint();
                }
                else if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var canvasRT = _canvas.GetComponent<RectTransform>();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        var go = obj as GameObject;
                        if (go != null && go.transform is RectTransform dragRt && dragRt != canvasRT)
                        {
                            Undo.SetTransformParent(dragRt, canvasRT, "Drop to Root");
                            dragRt.SetAsLastSibling();
                        }
                    }
                    RefreshRectsCache();
                    e.Use();
                }
            }
        }

        private Transform GetCreationParent()
        {
            if (FirstSelected != null && FirstSelected != _canvas.GetComponent<RectTransform>()) return FirstSelected.transform;
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
            go.name = EnforceUniqueName(parent, go.name, go);

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

            _selectedList.Clear();
            _selectedList.Add(rt);

            RefreshRectsCache();
            EditorUtility.SetDirty(go);
            return rt;
        }

        private void CreateText()
        {
            string locName = ToolLang.Get("Text", "Текст");
            var go = new GameObject(locName);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = locName;
            tmp.fontSize = 32;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            PlaceUnderParent(go, new Vector2(220, 60), "Create Text");
        }

        private void CreateButton()
        {
            string locName = ToolLang.Get("Button", "Кнопка");
            var go = new GameObject(locName);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.36f, 0.75f, 0.92f);
            go.AddComponent<Button>();
            var rt = PlaceUnderParent(go, new Vector2(180, 48), "Create Button");
            if (rt == null) return;

            string txtName = ToolLang.Get("Text", "Текст");
            var textGo = new GameObject(txtName);
            textGo.name = EnforceUniqueName(go.transform, txtName, textGo);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = locName;
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
            string locName = ToolLang.Get("Image", "Изображение");
            var go = new GameObject(locName);
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            PlaceUnderParent(go, new Vector2(120, 120), "Create Image");
        }

        private void CreatePanel()
        {
            string locName = ToolLang.Get("Panel", "Панель");
            var go = new GameObject(locName);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.10f, 0.11f, 0.15f, 0.85f);
            PlaceUnderParent(go, new Vector2(420, 220), "Create Panel");
        }

        private int ComputeDepth(RectTransform rt)
        {
            if (rt == null) return 0;
            // Глубина = количество родителей до root-canvas элемента (любого).
            int d = 0;
            var t = rt.parent;
            while (t != null)
            {
                var c = t.GetComponent<Canvas>();
                if (c != null && c.isRootCanvas) break;
                d++;
                if (d > 16) break;
                t = t.parent;
            }
            return d;
        }

        private void DrawTreeRow(RectTransform rt, string name, string icon, int depth, int index)
        {
            bool isSel = _selectedList.Contains(rt);

            GUILayout.BeginHorizontal();
            GUILayout.Space(0);
            Rect row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUILayout.Space(0);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, isSel ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f) : Color.clear);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            const float STEP = 14f;
            float baseX = row.x + 12f;
            Color lineCol = new Color(0.30f, 0.32f, 0.40f, 0.55f);

            if (depth > 0)
            {
                for (int d = 1; d <= depth; d++)
                {
                    float lx = baseX + (d - 1) * STEP + STEP * 0.5f;
                    EditorGUI.DrawRect(new Rect(lx, row.y, 1, row.height), lineCol);
                }
                float branchX = baseX + (depth - 1) * STEP + STEP * 0.5f;
                EditorGUI.DrawRect(new Rect(branchX, row.y + row.height * 0.5f, STEP * 0.5f, 1), lineCol);
            }

            float iconX = baseX + depth * STEP;

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = isSel ? C_ACCENT : C_TEXT_3;
            GUI.Label(new Rect(iconX, row.y, 18, row.height), icon, iconSt);

            var nameSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            nameSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(iconX + 20, row.y, row.width - (iconX - row.x) - 60, row.height), name, nameSt);

            Event e = Event.current;

            // ─── DRAG & DROP с тремя режимами: Above / Inside / Below ─────────────
            // Ranges по высоте: верхние 25% — Above, нижние 25% — Below, центр — Inside.
            float thirdH = row.height * 0.25f;
            DropMode mode = DropMode.Inside;
            if (e.mousePosition.y < row.y + thirdH) mode = DropMode.Above;
            else if (e.mousePosition.y > row.yMax - thirdH) mode = DropMode.Below;

            // Drag начинается только если курсор сместился >= TREE_DRAG_THRESHOLD от точки нажатия.
            // Раньше любое микро-движение мыши после клика триггерило DnD — раздражало.
            if (e.type == EventType.MouseDrag && _treeDragArmed && isSel)
            {
                if (Vector2.Distance(e.mousePosition, _treeMouseDownPos) >= TREE_DRAG_THRESHOLD)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = _selectedList.Select(x => x.gameObject).ToArray();
                    DragAndDrop.StartDrag("Move UI Elements");
                    _treeDragArmed = false;
                    e.Use();
                }
            }
            if (e.type == EventType.MouseUp) _treeDragArmed = false;

            if (row.Contains(e.mousePosition) && DragAndDrop.objectReferences.Length > 0)
            {
                if (e.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    e.Use();
                    _window?.Repaint();
                }
                else if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    ApplyTreeDrop(rt, mode);
                    RefreshRectsCache();
                    e.Use();
                }

                // Визуальная индикация места вставки.
                if (e.type == EventType.Repaint)
                {
                    if (mode == DropMode.Inside)
                    {
                        EditorGUI.DrawRect(row, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f));
                        DrawRectBorder(row, C_ACCENT);
                    }
                    else
                    {
                        float lineY = mode == DropMode.Above ? row.y - 1 : row.yMax - 1;
                        EditorGUI.DrawRect(new Rect(row.x + 12, lineY, row.width - 24, 2), C_ACCENT);
                        // "Усики" вокруг линии — указывают на сиблинг-вставку
                        EditorGUI.DrawRect(new Rect(row.x + 8, lineY - 2, 4, 6), C_ACCENT);
                        EditorGUI.DrawRect(new Rect(row.xMax - 12, lineY - 2, 4, 6), C_ACCENT);
                    }
                }
            }

            // ─── КЛИКИ ПО СТРОКЕ ──────────────────────────────────────────────────
            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    if (e.control || e.command)
                    {
                        if (isSel) _selectedList.Remove(rt);
                        else _selectedList.Add(rt);
                        _lastSelectedTreeIndex = index;
                    }
                    else if (e.shift && _lastSelectedTreeIndex >= 0)
                    {
                        int min = Mathf.Min(index, _lastSelectedTreeIndex);
                        int max = Mathf.Max(index, _lastSelectedTreeIndex);
                        _selectedList.Clear();
                        for (int i = min; i <= max; i++)
                        {
                            if (i < _allRects.Count) _selectedList.Add(_allRects[i]);
                        }
                    }
                    else
                    {
                        if (!isSel)
                        {
                            _selectedList.Clear();
                            _selectedList.Add(rt);
                        }
                        _lastSelectedTreeIndex = index;
                    }

                    // Взводим drag — реальный DnD начнётся только после порога движения.
                    _treeDragArmed = true;
                    _treeMouseDownPos = e.mousePosition;
                }
                else if (e.button == 1)
                {
                    if (!isSel)
                    {
                        _selectedList.Clear();
                        _selectedList.Add(rt);
                        _lastSelectedTreeIndex = index;
                    }
                    ShowTreeContextMenu(rt);
                }
                _window?.Repaint();
                e.Use();
            }
        }

        private enum DropMode { Above, Inside, Below }

        private void ApplyTreeDrop(RectTransform target, DropMode mode)
        {
            if (target == null) return;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                var go = obj as GameObject;
                if (go == null) continue;
                if (!(go.transform is RectTransform dragRt)) continue;
                if (dragRt == target) continue;
                if (target.IsChildOf(dragRt)) continue; // нельзя сделать предка ребёнком потомка

                if (mode == DropMode.Inside)
                {
                    Undo.SetTransformParent(dragRt, target, "Drop Inside");
                    dragRt.SetAsLastSibling();
                }
                else
                {
                    var newParent = target.parent;
                    if (newParent == null) continue;
                    Undo.SetTransformParent(dragRt, newParent, "Drop Sibling");
                    int targetIdx = target.GetSiblingIndex();
                    int newIdx = mode == DropMode.Above ? targetIdx : targetIdx + 1;
                    // если drag из того же родителя и шёл сверху — компенсируем сдвиг индекса
                    dragRt.SetSiblingIndex(Mathf.Clamp(newIdx, 0, newParent.childCount - 1));
                }
            }
        }

        private void ShowTreeContextMenu(RectTransform rt)
        {
            var menu = new GenericMenu();
            string parentName = GetDisplayName(rt);

            menu.AddDisabledItem(new GUIContent(string.Format(ToolLang.Get("Create inside: {0}", "Создать внутри: {0}"), parentName)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("📝 Text", "📝 Текст")), false, () => { _selectedList.Clear(); _selectedList.Add(rt); CreateText(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🔘 Button", "🔘 Кнопка")), false, () => { _selectedList.Clear(); _selectedList.Add(rt); CreateButton(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🖼 Image", "🖼 Картинка")), false, () => { _selectedList.Clear(); _selectedList.Add(rt); CreateImage(); });
            menu.AddItem(new GUIContent(ToolLang.Get("▣ Panel", "▣ Панель")), false, () => { _selectedList.Clear(); _selectedList.Add(rt); CreatePanel(); });
            menu.AddSeparator("");

            bool isCanvas = rt == _canvas.GetComponent<RectTransform>();
            if (!isCanvas) menu.AddItem(new GUIContent(ToolLang.Get("📋 Duplicate", "📋 Дублировать")), false, () => { DuplicateSelected(); });

            menu.AddItem(new GUIContent(ToolLang.Get("🗑 Delete", "🗑 Удалить")), false, () => { DeleteSelected(); });
            menu.ShowAsContext();
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
                _selectedList.Clear();
                _selectedList.Add(under);
            }
            else
            {
                creationParent = _canvas.transform;
                parentName = "Canvas";
            }

            menu.AddDisabledItem(new GUIContent(string.Format(ToolLang.Get("Create inside: {0}", "Создать внутри: {0}"), parentName)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("📝 Text", "📝 Текст")), false, () => { _selectedList.Clear(); _selectedList.Add(creationParent.GetComponent<RectTransform>()); CreateText(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🔘 Button", "🔘 Кнопка")), false, () => { _selectedList.Clear(); _selectedList.Add(creationParent.GetComponent<RectTransform>()); CreateButton(); });
            menu.AddItem(new GUIContent(ToolLang.Get("🖼 Image", "🖼 Картинка")), false, () => { _selectedList.Clear(); _selectedList.Add(creationParent.GetComponent<RectTransform>()); CreateImage(); });
            menu.AddItem(new GUIContent(ToolLang.Get("▣ Panel", "▣ Панель")), false, () => { _selectedList.Clear(); _selectedList.Add(creationParent.GetComponent<RectTransform>()); CreatePanel(); });
            menu.ShowAsContext();
        }

        private void DrawCenterCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);

            const float topH = 48f;
            Rect topbar = new Rect(rect.x, rect.y, rect.width, topH);
            EditorGUI.DrawRect(topbar, C_BG_SIDE);
            DrawRectBorder(new Rect(topbar.x, topbar.yMax - 1, topbar.width, 1), C_BORDER);

            DrawCanvasTopbar(topbar);

            Rect viewport = new Rect(rect.x, rect.y + topH, rect.width, rect.height - topH);

            if (_camera == null) return;

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

            if (Event.current.type == EventType.Repaint)
            {
                if (_previewTexture == null || _previewTexture.width != targetW || _previewTexture.height != targetH)
                {
                    if (_previewTexture != null) { _previewTexture.Release(); UnityEngine.Object.DestroyImmediate(_previewTexture); }
                    _previewTexture = new RenderTexture(targetW, targetH, 24) { antiAliasing = 4, filterMode = FilterMode.Bilinear };
                }

                var oldRT = _camera.targetTexture;
                _camera.targetTexture = _previewTexture;
                _camera.Render();
                _camera.targetTexture = oldRT;
            }

            float aspect = (float)targetW / targetH;
            float maxW = (viewport.width - 32f) * _previewZoom;
            float maxH = (viewport.height - 32f) * _previewZoom;
            float w = maxW;
            float h = w / aspect;
            if (h > maxH) { h = maxH; w = h * aspect; }

            Rect drawRectLocal = new Rect((viewport.width - w) * 0.5f, (viewport.height - h) * 0.5f, w, h);

            GUI.BeginClip(viewport);
            try
            {
                EditorGUI.DrawRect(new Rect(0, 0, viewport.width, viewport.height), new Color(0.05f, 0.05f, 0.07f));
                var frame = new Rect(drawRectLocal.x - 2, drawRectLocal.y - 2, drawRectLocal.width + 4, drawRectLocal.height + 4);
                EditorGUI.DrawRect(frame, C_BORDER);

                if (Event.current.type == EventType.Repaint && _previewTexture != null)
                {
                    GUI.DrawTexture(drawRectLocal, _previewTexture, ScaleMode.ScaleToFit, false);
                }

                if (_showGrid) DrawGridOverlay(drawRectLocal, targetW, targetH);
                if (_isMobileMode && _showSafeArea) DrawSafeAreaOverlay(drawRectLocal);

                DrawSelectionOverlay(drawRectLocal, targetW, targetH);

                HandleCanvasInput(drawRectLocal, targetW, targetH);
            }
            finally
            {
                GUI.EndClip();
            }
        }

        private void DrawCanvasTopbar(Rect topbar)
        {
            const float pad = 14f;
            const float groupH = 32f;
            float yMid = topbar.y + (topbar.height - groupH) * 0.5f;

            float g1W = (_resolutionPresetIndex == RESOLUTION_PRESETS.Length) ? 360f : 220f;
            Rect g1 = new Rect(topbar.x + pad, yMid, g1W, groupH);
            DrawTopbarGroupBg(g1);
            DrawResolutionGroup(g1);

            const float g3W = 260f;
            Rect g3 = new Rect(topbar.xMax - pad - g3W, yMid, g3W, groupH);
            DrawTopbarGroupBg(g3);
            DrawZoomHelpGroup(g3);

            float g2W = _isMobileMode ? 310f : 210f;
            float availStart = g1.xMax + 12f;
            float availEnd = g3.x - 12f;
            float g2X = (availStart + availEnd) * 0.5f - g2W * 0.5f;
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
            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = C_TEXT_2;
            GUI.Label(new Rect(r.x, r.y, monitorIconW, r.height), _isMobileMode ? "📱" : "🖥", iconSt);
            EditorGUI.DrawRect(new Rect(r.x + monitorIconW, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.06f));

            string[] options = new string[RESOLUTION_PRESETS.Length + 1];
            for (int i = 0; i < RESOLUTION_PRESETS.Length; i++) options[i] = RESOLUTION_PRESETS[i].label;
            options[RESOLUTION_PRESETS.Length] = ToolLang.Get("⚙ Custom…", "⚙ Своё…");

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
            float bh = 22f;
            float bx = r.x + 4;
            float by = r.y + (r.height - bh) * 0.5f;

            float gridW = 66f;
            DrawIconToggle(new Rect(bx, by, gridW, bh), "▦", ToolLang.Get("Grid", "Сетка"), ref _showGrid);
            bx += gridW + 4;

            float guideW = 126f;
            string guideText = _showGuideMode ? ToolLang.Get("Hints: On", "Подсказки: Вкл") : ToolLang.Get("Hints: Off", "Подсказки: Выкл");
            DrawIconToggle(new Rect(bx, by, guideW, bh), "💡", guideText, ref _showGuideMode);
            bx += guideW + 4;

            if (_isMobileMode)
            {
                DrawIconToggle(new Rect(bx, by, 90f, bh), "📱", ToolLang.Get("Safe Area", "Безоп. зона"), ref _showSafeArea);
            }
            else _showSafeArea = false;
        }

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
            GUI.Label(new Rect(r.x + 4, r.y, 18, r.height), icon, iconSt);

            var labelSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            labelSt.normal.textColor = value ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(r.x + 22, r.y, r.width - 24, r.height), label, labelSt);

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                value = !value;
                Event.current.Use();
            }
        }

        private void DrawZoomHelpGroup(Rect r)
        {
            float bx = r.x + 6;
            float by = r.y + (r.height - 22) * 0.5f;

            string zoomStr = ToolLang.Get("Zoom", "Масштаб");
            var labelSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            labelSt.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(bx, r.y, 56, r.height), zoomStr, labelSt);
            bx += 56;

            if (DrawIconBtn(new Rect(bx, by, 22, 22), "−")) _previewZoom = Mathf.Max(0.25f, _previewZoom - 0.1f);
            bx += 22 + 4;

            float sliderW = 70f;
            float sliderY = r.y + (r.height - 16) * 0.5f + 1f;
            _previewZoom = GUI.HorizontalSlider(new Rect(bx, sliderY, sliderW, 16f), _previewZoom, 0.25f, 1.5f);
            bx += sliderW + 4;

            if (DrawIconBtn(new Rect(bx, by, 22, 22), "+")) _previewZoom = Mathf.Min(1.5f, _previewZoom + 0.1f);
            bx += 22 + 6;

            EditorGUI.DrawRect(new Rect(bx, r.y + 6, 1, r.height - 12), new Color(1, 1, 1, 0.06f));
            bx += 6;

            var pctSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            pctSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(bx, r.y, 40, r.height), (_previewZoom * 100f).ToString("F0") + "%", pctSt);
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

        private void DrawSelectionOverlay(Rect drawRectLocal, int targetW, int targetH)
        {
            if (_selectedList.Count == 0) return;

            Color c = C_ACCENT;
            foreach (var rt in _selectedList)
            {
                if (rt == null || (_canvas != null && rt == _canvas.GetComponent<RectTransform>())) continue;

                Rect selRect = ComputeRectScreenForRT(rt, drawRectLocal, targetW, targetH);
                if (selRect.width < 1f && selRect.height < 1f) continue;

                EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.y - 1, selRect.width + 2, 2), c);
                EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.yMax - 1, selRect.width + 2, 2), c);
                EditorGUI.DrawRect(new Rect(selRect.x - 1, selRect.y - 1, 2, selRect.height + 2), c);
                EditorGUI.DrawRect(new Rect(selRect.xMax - 1, selRect.y - 1, 2, selRect.height + 2), c);

                // Квадратики для ресайза рисуем ТОЛЬКО если выбран один объект
                if (_selectedList.Count == 1)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Rect h = HandleRectByIndex(i, selRect);
                        EditorGUI.DrawRect(h, Color.white);
                        EditorGUI.DrawRect(new Rect(h.x + 1, h.y + 1, h.width - 2, h.height - 2), c);
                    }
                }
            }
        }

        private static Rect HandleRectByIndex(int idx, Rect r)
        {
            float s = HANDLE_SIZE_PX;
            float midX = r.x + r.width / 2f - s / 2f;
            float midY = r.y + r.height / 2f - s / 2f;
            return idx switch
            {
                0 => new Rect(r.x - s / 2f, r.y - s / 2f, s, s),
                1 => new Rect(midX, r.y - s / 2f, s, s),
                2 => new Rect(r.xMax - s / 2f, r.y - s / 2f, s, s),
                3 => new Rect(r.xMax - s / 2f, midY, s, s),
                4 => new Rect(r.xMax - s / 2f, r.yMax - s / 2f, s, s),
                5 => new Rect(midX, r.yMax - s / 2f, s, s),
                6 => new Rect(r.x - s / 2f, r.yMax - s / 2f, s, s),
                7 => new Rect(r.x - s / 2f, midY, s, s),
                _ => Rect.zero
            };
        }

        private void HandleCanvasInput(Rect drawRect, int targetW, int targetH)
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.ScrollWheel && drawRect.Contains(e.mousePosition))
            {
                float scrollDelta = e.delta.y;
                if (scrollDelta > 0) _previewZoom = Mathf.Max(0.25f, _previewZoom - 0.05f);
                else if (scrollDelta < 0) _previewZoom = Mathf.Min(1.5f, _previewZoom + 0.05f);
                e.Use();
                return;
            }

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

            if (e.type == EventType.ContextClick && drawRect.Contains(e.mousePosition))
            {
                RectTransform under = PickDeepestRectAt(e.mousePosition, drawRect, targetW, targetH);
                ShowCanvasCreateMenu(under);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && drawRect.Contains(e.mousePosition))
            {
                if (_selectedList.Count == 1)
                {
                    RectTransform singleSel = _selectedList[0];
                    if (singleSel != null && singleSel != _canvas.GetComponent<RectTransform>())
                    {
                        Rect selRect = ComputeRectScreenForRT(singleSel, drawRect, targetW, targetH);
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
                }

                bool isDoubleClick = (EditorApplication.timeSinceStartup - _lastCanvasClickTime) < 0.3f;
                _lastCanvasClickTime = EditorApplication.timeSinceStartup;

                RectTransform pickedDeepest = PickDeepestRectAt(e.mousePosition, drawRect, targetW, targetH);
                if (pickedDeepest != null)
                {
                    RectTransform target = pickedDeepest;

                    if (!isDoubleClick)
                    {
                        bool alreadySelected = _selectedList.Contains(target);
                        if (!alreadySelected)
                        {
                            RectTransform curr = target;
                            while (curr != null && curr != _canvas.GetComponent<RectTransform>())
                            {
                                if (_selectedList.Contains(curr))
                                {
                                    alreadySelected = true;
                                    target = curr;
                                    break;
                                }
                                curr = curr.parent as RectTransform;
                            }
                        }

                        if (!alreadySelected)
                        {
                            RectTransform topLevel = GetTopLevelParent(pickedDeepest);
                            if (topLevel != null) target = topLevel;
                        }
                    }

                    if (e.control || e.command)
                    {
                        if (_selectedList.Contains(target)) _selectedList.Remove(target);
                        else _selectedList.Add(target);
                    }
                    else
                    {
                        if (!_selectedList.Contains(target))
                        {
                            _selectedList.Clear();
                            _selectedList.Add(target);
                        }
                    }

                    if (FirstSelected != _canvas.GetComponent<RectTransform>()) BeginDrag(e.mousePosition);
                }
                else
                {
                    if (!e.control && !e.command) _selectedList.Clear();
                }
                e.Use();
            }
        }

        private RectTransform PickDeepestRectAt(Vector2 mousePosScreen, Rect drawRect, int targetW, int targetH)
        {
            if (_canvas == null || _camera == null) return null;
            for (int i = _allRects.Count - 1; i >= 0; i--)
            {
                var rt = _allRects[i];
                if (rt == null) continue;
                if (rt == _canvas.GetComponent<RectTransform>()) continue;
                if (!rt.gameObject.activeInHierarchy) continue;
                Rect rs = ComputeRectScreenForRT(rt, drawRect, targetW, targetH);
                if (rs.width < 4f || rs.height < 4f) continue;
                if (rs.Contains(mousePosScreen)) return rt;
            }
            return null;
        }

        private RectTransform GetTopLevelParent(RectTransform deepest)
        {
            if (_canvas == null) return deepest;
            RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
            RectTransform curr = deepest;
            while (curr.parent != null && curr.parent != canvasRT && curr.parent != canvasRT.parent)
            {
                curr = curr.parent.GetComponent<RectTransform>();
            }
            return curr;
        }

        private Rect ComputeRectScreenForRT(RectTransform rt, Rect drawRect, int targetW, int targetH)
        {
            if (rt == null || _canvas == null) return Rect.zero;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return Rect.zero;

            Vector3[] cw = new Vector3[4];
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
                float py = drawRect.y + (1f - v) * drawRect.height;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void RecordUndoAll(string msg)
        {
            var objs = _selectedList.Select(x => (UnityEngine.Object)x.gameObject).ToArray();
            Undo.RecordObjects(objs, msg);
        }

        private void BeginDrag(Vector2 mousePos)
        {
            if (_selectedList.Count == 0) return;
            _dragging = true;
            _dragMouseStart = mousePos;
            _dragAnchorStarts.Clear();

            RecordUndoAll("Move UI Element");

            foreach (var rt in _selectedList)
            {
                _dragAnchorStarts[rt] = rt.anchoredPosition;
            }
        }

        private void ProcessDragMove(Event e, Rect drawRect, int targetW, int targetH)
        {
            if (_selectedList.Count == 0) return;
            Vector2 deltaPreview = e.mousePosition - _dragMouseStart;
            Vector2 deltaCanvas = PreviewDeltaToCanvasDelta(deltaPreview, drawRect);

            foreach (var rt in _selectedList)
            {
                if (!_dragAnchorStarts.TryGetValue(rt, out Vector2 startPos)) continue;

                Vector2 newAnchor = startPos + deltaCanvas;
                if (_showGrid && _gridStep > 0.5f)
                {
                    newAnchor.x = Mathf.Round(newAnchor.x / _gridStep) * _gridStep;
                    newAnchor.y = Mathf.Round(newAnchor.y / _gridStep) * _gridStep;
                }

                rt.anchoredPosition = newAnchor;
                EditorUtility.SetDirty(rt);
            }
        }

        private Vector2 PreviewDeltaToCanvasDelta(Vector2 deltaPreview, Rect drawRect)
        {
            if (_canvas == null || drawRect.width < 1f) return Vector2.zero;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return Vector2.zero;
            float scaleX = canvasRT.rect.width / drawRect.width;
            float scaleY = canvasRT.rect.height / drawRect.height;
            return new Vector2(deltaPreview.x * scaleX, -deltaPreview.y * scaleY);
        }

        private void BeginResize(int handleIdx, Vector2 mousePos)
        {
            if (_selectedList.Count != 1) return; // Ресайз только для одного объекта
            _resizeHandle = handleIdx;
            _resizeMouseStart = mousePos;
            _resizeSizeStart = FirstSelected.sizeDelta;
            _resizeAnchorStart = FirstSelected.anchoredPosition;
            Undo.RecordObject(FirstSelected, "Resize UI Element");
        }

        private void ProcessResize(Event e, Rect drawRect, int targetW, int targetH)
        {
            if (_selectedList.Count != 1) return;
            RectTransform sel = FirstSelected;

            Vector2 deltaPreview = e.mousePosition - _resizeMouseStart;
            Vector2 deltaCanvas = PreviewDeltaToCanvasDelta(deltaPreview, drawRect);

            Vector2 newSize = _resizeSizeStart;
            Vector2 newAnchor = _resizeAnchorStart;
            float dx = deltaCanvas.x;
            float dy = deltaCanvas.y;
            switch (_resizeHandle)
            {
                case 0: newSize.x -= dx; newSize.y += dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break;
                case 1: newSize.y += dy; newAnchor.y += dy * 0.5f; break;
                case 2: newSize.x += dx; newSize.y += dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break;
                case 3: newSize.x += dx; newAnchor.x += dx * 0.5f; break;
                case 4: newSize.x += dx; newSize.y -= dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break;
                case 5: newSize.y -= dy; newAnchor.y += dy * 0.5f; break;
                case 6: newSize.x -= dx; newSize.y -= dy; newAnchor.x += dx * 0.5f; newAnchor.y += dy * 0.5f; break;
                case 7: newSize.x -= dx; newAnchor.x += dx * 0.5f; break;
            }

            if (_showGrid && _gridStep > 0.5f)
            {
                newSize.x = Mathf.Round(newSize.x / _gridStep) * _gridStep;
                newSize.y = Mathf.Round(newSize.y / _gridStep) * _gridStep;
            }
            newSize.x = Mathf.Max(8f, newSize.x);
            newSize.y = Mathf.Max(8f, newSize.y);

            sel.sizeDelta = newSize;
            sel.anchoredPosition = newAnchor;
            EditorUtility.SetDirty(sel);
        }

        // ─── Проверка гомогенности для Мультивыбора ───
        private bool AreSelectedTypesHomogeneous()
        {
            if (_selectedList.Count <= 1) return true;

            bool hasImg = FirstSelected.GetComponent<Image>() != null;
            bool hasTxt = FirstSelected.GetComponent<TMP_Text>() != null;
            bool hasBtn = FirstSelected.GetComponent<Button>() != null;

            foreach (var rt in _selectedList)
            {
                if ((rt.GetComponent<Image>() != null) != hasImg) return false;
                if ((rt.GetComponent<TMP_Text>() != null) != hasTxt) return false;
                if ((rt.GetComponent<Button>() != null) != hasBtn) return false;
            }
            return true;
        }

        private void DrawInspector(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            DrawRectBorder(new Rect(rect.x, rect.y, 1, rect.height), C_BORDER);

            if (_selectedList.Count == 0)
            {
                GUILayout.BeginArea(rect);
                GUILayout.Space(20);
                var st = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get("Select an element on the canvas or in the tree to edit it.", "Выбери элемент на холсте или в дереве слева, чтобы редактировать его."), st);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginArea(rect);
            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            DrawInspectorHeader();

            bool isCanvas = FirstSelected.GetComponent<Canvas>() != null && _selectedList.Count == 1;
            bool isMixed = !AreSelectedTypesHomogeneous();

            if (isCanvas)
            {
                DrawSectionLabel(ToolLang.Get("CANVAS", "ХОЛСТ (CANVAS)"));
                DrawInlineGuide("canvas");
            }
            else if (isMixed)
            {
                DrawSectionLabel(ToolLang.Get("MIXED TYPES", "РАЗНЫЕ ТИПЫ"));
                GUILayout.BeginHorizontal();
                GUILayout.Space(14);
                var st = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, normal = { textColor = new Color(0.78f, 0.80f, 0.86f) } };
                GUILayout.Label(ToolLang.Get("Multiple elements of different types selected. Only basic actions are available.", "Выбраны элементы разных типов. Доступны только общие действия."), st);
                GUILayout.Space(14);
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                DrawInspectorPositionSection();
                DrawInspectorAnchorSection();
                DrawInspectorTypeSpecificSections();
            }

            DrawInspectorActionsSection(isCanvas);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawInspectorHeader()
        {
            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            if (HasMultiple)
            {
                var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                iconSt.normal.textColor = C_ACCENT;
                GUILayout.Label("❖", iconSt, GUILayout.Width(22));

                var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                titleSt.normal.textColor = C_TEXT_1;
                GUILayout.Label(string.Format(ToolLang.Get("Multiple Selected ({0})", "Выбрано элементов: {0}"), _selectedList.Count), titleSt);
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                RectTransform single = FirstSelected;
                string icon = GetDisplayIcon(single);
                var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                iconSt.normal.textColor = C_ACCENT;
                GUILayout.Label(icon, iconSt, GUILayout.Width(22));

                var tfStyle = new GUIStyle(EditorStyles.textField) { fontSize = 14, fontStyle = FontStyle.Bold };
                tfStyle.normal.background = null; tfStyle.focused.background = null;
                tfStyle.normal.textColor = C_TEXT_1; tfStyle.focused.textColor = Color.white;

                if (_renameTarget != single)
                {
                    _renameTarget = single;
                    _pendingRename = single.gameObject.name;
                }

                int maxChars = 30;
                _pendingRename = EditorGUILayout.TextField(_pendingRename, tfStyle, GUILayout.Height(20));
                if (_pendingRename.Length > maxChars) _pendingRename = _pendingRename.Substring(0, maxChars);

                if (_pendingRename != single.gameObject.name)
                {
                    GUI.backgroundColor = C_SUCCESS;
                    if (GUILayout.Button("✓", GUILayout.Width(24), GUILayout.Height(20)))
                    {
                        Undo.RecordObject(single.gameObject, "Rename Element");
                        single.gameObject.name = EnforceUniqueName(single.parent, _pendingRename, single.gameObject);
                        _pendingRename = single.gameObject.name;
                        RefreshRectsCache();
                        GUI.FocusControl(null);
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                int curChars = _pendingRename.Length;
                var counterStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                counterStyle.normal.textColor = curChars >= maxChars ? C_DANGER : C_TEXT_4;
                GUILayout.Label($"{curChars}/{maxChars}", counterStyle, GUILayout.Width(45));
                GUILayout.Space(14);
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
            }
        }

        private void DrawInspectorPositionSection()
        {
            DrawSectionLabel(ToolLang.Get("POSITION & SIZE", "ПОЛОЖЕНИЕ И РАЗМЕР"));
            DrawInlineGuide("position");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            RectTransform f = FirstSelected;

            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(18));
            EditorGUI.BeginChangeCheck();
            float nx = EditorGUILayout.FloatField(f.anchoredPosition.x);
            GUILayout.Space(8);
            GUILayout.Label("Y", GUILayout.Width(18));
            float ny = EditorGUILayout.FloatField(f.anchoredPosition.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    Undo.RecordObject(rt, "Position");
                    rt.anchoredPosition = new Vector2(nx, ny);
                    EditorUtility.SetDirty(rt);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("W", GUILayout.Width(18));
            EditorGUI.BeginChangeCheck();
            float nw = EditorGUILayout.FloatField(f.sizeDelta.x);
            GUILayout.Space(8);
            GUILayout.Label("H", GUILayout.Width(18));
            float nh = EditorGUILayout.FloatField(f.sizeDelta.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    Undo.RecordObject(rt, "Size");
                    rt.sizeDelta = new Vector2(nw, nh);
                    EditorUtility.SetDirty(rt);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Rotation", "Поворот"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float rot = EditorGUILayout.FloatField(f.localEulerAngles.z);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    Undo.RecordObject(rt, "Rotation");
                    var e = rt.localEulerAngles; e.z = rot;
                    rt.localEulerAngles = e;
                    EditorUtility.SetDirty(rt);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Scale", "Масштаб"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float sx = EditorGUILayout.FloatField(f.localScale.x);
            GUILayout.Space(8);
            float sy = EditorGUILayout.FloatField(f.localScale.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    Undo.RecordObject(rt, "Scale");
                    rt.localScale = new Vector3(sx, sy, rt.localScale.z);
                    EditorUtility.SetDirty(rt);
                }
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
            DrawInlineGuide("anchor");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

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

                bool isCurrent = Mathf.Approximately(FirstSelected.anchorMin.x, presets[i].min.x)
                              && Mathf.Approximately(FirstSelected.anchorMin.y, presets[i].min.y)
                              && Mathf.Approximately(FirstSelected.anchorMax.x, presets[i].max.x)
                              && Mathf.Approximately(FirstSelected.anchorMax.y, presets[i].max.y);

                Color bg = isCurrent ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f) : C_BG_PRIMARY;
                if (cellRect.Contains(Event.current.mousePosition)) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
                EditorGUI.DrawRect(cellRect, bg);
                DrawRectBorder(cellRect, isCurrent ? C_ACCENT : C_BORDER);

                var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = isCurrent ? C_ACCENT : C_TEXT_2;
                GUI.Label(cellRect, presets[i].name, st);

                if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                {
                    foreach (var rt in _selectedList) SnapAnchorToCorner(rt, presets[i].min, presets[i].max);
                    Event.current.Use();
                }
            }

            GUILayout.Space(8);
            GUILayout.BeginVertical();

            DrawAnchorStretch(ToolLang.Get("Stretch H", "Растянуть по гор."), new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            DrawAnchorStretch(ToolLang.Get("Stretch V", "Растянуть по верт."), new Vector2(0.5f, 0), new Vector2(0.5f, 1));
            DrawAnchorStretch(ToolLang.Get("Fill", "Заполнить"), new Vector2(0, 0), new Vector2(1, 1));

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // Smart Anchors — определяет зону экрана и расставляет якоря автоматически.
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUI.backgroundColor = C_ACCENT;
            string smartLabel = "✨ " + ToolLang.Get("Smart anchors (mobile-friendly)", "Умный якорь (под мобилку)");
            if (GUILayout.Button(new GUIContent(smartLabel, ToolLang.Get(
                    "Auto-detect element zone (corner/edge/center) and apply anchors that hold position correctly when screen size changes.",
                    "Определяет в какой зоне (угол/край/центр) находится элемент и ставит якоря, которые удержат позицию при смене разрешения.")),
                GUILayout.Height(26), GUILayout.ExpandWidth(true)))
            {
                ApplySmartAnchorsToSelection();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        // "Зональный" авто-якорь: смотрим где центр элемента в канвасе (3×3 зон), и ставим
        // соответствующие anchorMin/Max. Это упрощённый smart-anchor — для большинства элементов
        // (логотип в углу, низ-кнопка, центральный диалог) даёт правильное поведение при смене разрешения.
        private void ApplySmartAnchorsToSelection()
        {
            if (_selectedList.Count == 0 || _canvas == null) return;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Smart Anchors");

            foreach (var rt in _selectedList)
            {
                if (rt == null || rt == canvasRT) continue;
                Undo.RecordObject(rt, "Smart Anchors");

                // Получаем центр RT в мировом пространстве.
                Vector3[] worldCorners = new Vector3[4];
                rt.GetWorldCorners(worldCorners);
                Vector3 worldCenter = (worldCorners[0] + worldCorners[2]) * 0.5f;

                // И углы канваса.
                Vector3[] cw = new Vector3[4];
                canvasRT.GetWorldCorners(cw);
                float canvasW = cw[2].x - cw[0].x;
                float canvasH = cw[2].y - cw[0].y;
                if (canvasW < 0.0001f || canvasH < 0.0001f) continue;

                float u = (worldCenter.x - cw[0].x) / canvasW; // 0 = left, 1 = right
                float v = (worldCenter.y - cw[0].y) / canvasH; // 0 = bottom, 1 = top

                // Размер RT относительно канваса: если элемент крупный — растягиваем.
                Vector2 rtSize = rt.rect.size;
                bool stretchX = rtSize.x > canvasRT.rect.width * 0.6f;
                bool stretchY = rtSize.y > canvasRT.rect.height * 0.6f;

                // Определяем зону по центру.
                float ax, axMax;
                if (stretchX) { ax = 0f; axMax = 1f; }
                else if (u < 0.33f) { ax = 0f; axMax = 0f; }
                else if (u > 0.67f) { ax = 1f; axMax = 1f; }
                else { ax = 0.5f; axMax = 0.5f; }

                float ay, ayMax;
                if (stretchY) { ay = 0f; ayMax = 1f; }
                else if (v < 0.33f) { ay = 0f; ayMax = 0f; }
                else if (v > 0.67f) { ay = 1f; ayMax = 1f; }
                else { ay = 0.5f; ayMax = 0.5f; }

                Vector2 newMin = new Vector2(ax, ay);
                Vector2 newMax = new Vector2(axMax, ayMax);

                // Сохраняем визуальное положение и размер.
                Vector3 worldPos = rt.position;
                Vector2 oldSize = rt.rect.size;

                rt.anchorMin = newMin;
                rt.anchorMax = newMax;
                if (Mathf.Approximately(newMin.x, newMax.x) && Mathf.Approximately(newMin.y, newMax.y))
                {
                    rt.pivot = newMin;
                }
                rt.position = worldPos;

                // Для не-stretch — восстанавливаем sizeDelta как абсолютный размер.
                if (newMin.x == newMax.x) rt.sizeDelta = new Vector2(oldSize.x, rt.sizeDelta.y);
                else { /* оставляем offsetMin/Max как Unity рассчитала */ }
                if (newMin.y == newMax.y) rt.sizeDelta = new Vector2(rt.sizeDelta.x, oldSize.y);

                EditorUtility.SetDirty(rt);
            }

            Undo.CollapseUndoOperations(undoGroup);
            _window?.Repaint();
        }

        private void DrawAnchorStretch(string label, Vector2 min, Vector2 max)
        {
            bool isCurrent = Mathf.Approximately(FirstSelected.anchorMin.x, min.x) && Mathf.Approximately(FirstSelected.anchorMin.y, min.y)
                && Mathf.Approximately(FirstSelected.anchorMax.x, max.x) && Mathf.Approximately(FirstSelected.anchorMax.y, max.y);
            GUI.backgroundColor = isCurrent ? C_ACCENT : Color.white;
            if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(140), GUILayout.Height(22)))
            {
                foreach (var rt in _selectedList)
                {
                    Undo.RecordObject(rt, "Stretch Anchor");
                    rt.anchorMin = min; rt.anchorMax = max;
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                    EditorUtility.SetDirty(rt);
                }
            }
            GUI.backgroundColor = Color.white;
        }

        private void SnapAnchorToCorner(RectTransform rt, Vector2 cornerMin, Vector2 cornerMax)
        {
            if (rt == null) return;
            Undo.RecordObject(rt, "Set Anchor");
            Vector2 actualSize = rt.rect.size;
            rt.anchorMin = cornerMin;
            rt.anchorMax = cornerMax;

            if (Mathf.Approximately(cornerMin.x, cornerMax.x) && Mathf.Approximately(cornerMin.y, cornerMax.y))
            {
                rt.pivot = cornerMin;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = actualSize;
            }
            EditorUtility.SetDirty(rt);
        }

        private void DrawInspectorTypeSpecificSections()
        {
            var firstImg = FirstSelected.GetComponent<Image>();
            if (firstImg != null) DrawImageSection(firstImg);

            var firstTmp = FirstSelected.GetComponent<TMP_Text>();
            if (firstTmp != null)
            {
                DrawTextSection(firstTmp);
            }
            else
            {
                // Если на объекте легаси UnityEngine.UI.Text — показываем компактный
                // блок с кнопкой конвертации в TMP. Полный legacy-инспектор не делаем
                // намеренно, чтобы пользователь сразу мигрировал на современный текст.
                var firstLegacy = FirstSelected.GetComponent<UnityEngine.UI.Text>();
                if (firstLegacy != null) DrawLegacyTextConvertSection(firstLegacy);
            }

            var firstBtn = FirstSelected.GetComponent<Button>();
            if (firstBtn != null) DrawButtonSection(firstBtn);
        }

        private void DrawLegacyTextConvertSection(UnityEngine.UI.Text legacy)
        {
            DrawSectionLabel(ToolLang.Get("LEGACY TEXT", "СТАРЫЙ ТЕКСТ"), "text");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var warnSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            warnSt.normal.textColor = new Color(1f, 0.78f, 0.20f);
            GUILayout.Label("⚠ " + ToolLang.Get(
                "This is the old UI Text component. Convert to TextMeshPro for sharper rendering on big screens.",
                "Это старый компонент UI Text. Конвертируй в TextMeshPro — будет чётче на больших экранах."), warnSt);
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("✨ " + ToolLang.Get("Convert to TextMeshPro", "Преобразовать в TextMeshPro"), GUILayout.Height(28), GUILayout.ExpandWidth(true)))
            {
                ConvertLegacyToTMP(legacy);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void ConvertLegacyToTMP(UnityEngine.UI.Text legacy)
        {
            if (legacy == null) return;
            var go = legacy.gameObject;

            // Запоминаем релевантные свойства до удаления
            string text = legacy.text;
            Color color = legacy.color;
            int fontSize = legacy.fontSize;
            TextAnchor align = legacy.alignment;
            FontStyle style = legacy.fontStyle;
            bool isRich = legacy.supportRichText;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert to TextMeshPro");

            Undo.DestroyObjectImmediate(legacy);

            var tmp = Undo.AddComponent<TextMeshProUGUI>(go);
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = fontSize;
            tmp.alignment = MapTextAnchorToTMP(align);
            tmp.richText = isRich;
            FontStyles st = FontStyles.Normal;
            if ((style & FontStyle.Bold) != 0) st |= FontStyles.Bold;
            if ((style & FontStyle.Italic) != 0) st |= FontStyles.Italic;
            tmp.fontStyle = st;

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(go);
        }

        private static TextAlignmentOptions MapTextAnchorToTMP(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
                default:                      return TextAlignmentOptions.Center;
            }
        }

        private void DrawImageSection(Image firstImg)
        {
            DrawSectionLabel(ToolLang.Get("IMAGE", "ИЗОБРАЖЕНИЕ"));
            DrawInlineGuide("image");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Sprite", "Спрайт"), GUILayout.Width(72));
            string spName = firstImg.sprite != null ? firstImg.sprite.name : ToolLang.Get("(none)", "(не выбрано)");
            if (spName.Length > 20) spName = spName.Substring(0, 18) + "...";

            var btnStyle = new GUIStyle(EditorStyles.popup) { clipping = TextClipping.Clip };
            if (GUILayout.Button(spName, btnStyle, GUILayout.Height(20)))
            {
                NovellaGalleryWindow.ShowWindow((obj) =>
                {
                    Sprite picked = null;
                    if (obj is Sprite sp) picked = sp;
                    else if (obj is Texture2D tex) picked = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(tex));

                    if (picked != null)
                    {
                        foreach (var rt in _selectedList)
                        {
                            var img = rt.GetComponent<Image>();
                            if (img != null) { Undo.RecordObject(img, "Sprite"); img.sprite = picked; EditorUtility.SetDirty(img); }
                        }
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Image, "");
            }
            if (firstImg.sprite != null && GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(20)))
            {
                foreach (var rt in _selectedList)
                {
                    var img = rt.GetComponent<Image>();
                    if (img != null) { Undo.RecordObject(img, "Clear Sprite"); img.sprite = null; EditorUtility.SetDirty(img); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var col = EditorGUILayout.ColorField(firstImg.color);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var img = rt.GetComponent<Image>();
                    if (img != null) { Undo.RecordObject(img, "Color"); img.color = col; EditorUtility.SetDirty(img); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Type", "Тип"), GUILayout.Width(72));
            string[] typeLabels = { ToolLang.Get("Simple", "Обычный"), ToolLang.Get("Sliced", "Нарезанный"), ToolLang.Get("Tiled", "Замощенный"), ToolLang.Get("Filled", "Заполненный") };
            int typeIdx = (int)firstImg.type;
            EditorGUI.BeginChangeCheck();
            typeIdx = EditorGUILayout.Popup(typeIdx, typeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var img = rt.GetComponent<Image>();
                    if (img != null) { Undo.RecordObject(img, "Image Type"); img.type = (Image.Type)typeIdx; EditorUtility.SetDirty(img); }
                }
            }
            GUILayout.EndHorizontal();

            if (firstImg.type == Image.Type.Filled)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Fill Method", "Метод"), GUILayout.Width(72));
                string[] methodLabels = {
                    ToolLang.Get("Horizontal", "Горизонтально"),
                    ToolLang.Get("Vertical",   "Вертикально"),
                    ToolLang.Get("Radial 90",  "Радиально 90°"),
                    ToolLang.Get("Radial 180", "Радиально 180°"),
                    ToolLang.Get("Radial 360", "Радиально 360°"),
                };
                int methodIdx = (int)firstImg.fillMethod;
                EditorGUI.BeginChangeCheck();
                methodIdx = EditorGUILayout.Popup(methodIdx, methodLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var rt in _selectedList)
                    {
                        var img = rt.GetComponent<Image>();
                        if (img != null) { Undo.RecordObject(img, "Fill Method"); img.fillMethod = (Image.FillMethod)methodIdx; EditorUtility.SetDirty(img); }
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Fill", "Заполн."), GUILayout.Width(72));
                EditorGUI.BeginChangeCheck();
                float fill = EditorGUILayout.Slider(firstImg.fillAmount, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var rt in _selectedList)
                    {
                        var img = rt.GetComponent<Image>();
                        if (img != null) { Undo.RecordObject(img, "Fill"); img.fillAmount = fill; EditorUtility.SetDirty(img); }
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawTextSection(TMP_Text firstTxt)
        {
            DrawSectionLabel(ToolLang.Get("TEXT STYLE", "СТИЛЬ ТЕКСТА"));
            DrawInlineGuide("text");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font", "Шрифт"), GUILayout.Width(72));

            string fontName = firstTxt.font != null ? firstTxt.font.name : ToolLang.Get("Default", "По умолчанию");
            if (fontName.Length > 20) fontName = fontName.Substring(0, 18) + "...";
            var btnStyle = new GUIStyle(EditorStyles.popup) { clipping = TextClipping.Clip };

            if (GUILayout.Button(fontName, btnStyle, GUILayout.Height(20)))
            {
                NovellaGalleryWindow.ShowWindow((obj) =>
                {
                    TMP_FontAsset pickedFont = null;

                    if (obj is TMP_FontAsset tmpFont) { pickedFont = tmpFont; }
                    else if (obj is Font legacyFont)
                    {
                        string path = AssetDatabase.GetAssetPath(legacyFont);
                        string newPath = System.IO.Path.ChangeExtension(path, ".asset");
                        pickedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(newPath);
                        if (pickedFont == null)
                        {
                            pickedFont = TMP_FontAsset.CreateFontAsset(legacyFont);
                            if (pickedFont != null) { AssetDatabase.CreateAsset(pickedFont, newPath); AssetDatabase.SaveAssets(); }
                        }
                    }

                    if (pickedFont != null)
                    {
                        foreach (var rt in _selectedList)
                        {
                            var txt = rt.GetComponent<TMP_Text>();
                            if (txt != null) { Undo.RecordObject(txt, "Font"); txt.font = pickedFont; EditorUtility.SetDirty(txt); }
                        }
                    }
                }, NovellaGalleryWindow.EGalleryFilter.Font, "");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Font Size", "Размер"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            float fs = EditorGUILayout.FloatField(firstTxt.fontSize);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var txt = rt.GetComponent<TMP_Text>();
                    if (txt != null) { Undo.RecordObject(txt, "Font Size"); txt.fontSize = fs; EditorUtility.SetDirty(txt); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Color", "Цвет"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            var col = EditorGUILayout.ColorField(firstTxt.color);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var txt = rt.GetComponent<TMP_Text>();
                    if (txt != null) { Undo.RecordObject(txt, "Text Color"); txt.color = col; EditorUtility.SetDirty(txt); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Align", "Выравн."), GUILayout.Width(72));

            const float cell = 24f;
            Rect grid = GUILayoutUtility.GetRect(cell * 3 + 8, cell * 3 + 8, GUILayout.Width(cell * 3 + 8));
            EditorGUI.DrawRect(grid, C_BG_RAISED);
            DrawRectBorder(grid, C_BORDER);

            (string icon, TextAlignmentOptions align)[] aligns = new (string, TextAlignmentOptions)[]
            {
                ("↖", TextAlignmentOptions.TopLeft),    ("↑", TextAlignmentOptions.Top),    ("↗", TextAlignmentOptions.TopRight),
                ("←", TextAlignmentOptions.Left),       ("●", TextAlignmentOptions.Center), ("→", TextAlignmentOptions.Right),
                ("↙", TextAlignmentOptions.BottomLeft), ("↓", TextAlignmentOptions.Bottom), ("↘", TextAlignmentOptions.BottomRight)
            };

            for (int i = 0; i < 9; i++)
            {
                int colI = i % 3;
                int rowI = i / 3;
                Rect cellRect = new Rect(grid.x + 4 + colI * cell, grid.y + 4 + rowI * cell, cell - 2, cell - 2);
                bool isCurrent = firstTxt.alignment == aligns[i].align;

                Color bg = isCurrent ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f) : C_BG_PRIMARY;
                if (cellRect.Contains(Event.current.mousePosition)) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
                EditorGUI.DrawRect(cellRect, bg);
                DrawRectBorder(cellRect, isCurrent ? C_ACCENT : C_BORDER);

                var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = isCurrent ? C_ACCENT : C_TEXT_2;
                GUI.Label(cellRect, aligns[i].icon, st);

                if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                {
                    foreach (var rt in _selectedList)
                    {
                        var txt = rt.GetComponent<TMP_Text>();
                        if (txt != null) { Undo.RecordObject(txt, "Align"); txt.alignment = aligns[i].align; EditorUtility.SetDirty(txt); }
                    }
                    Event.current.Use();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Bold", "Жирный"), GUILayout.Width(72));
            bool isBold = (firstTxt.fontStyle & FontStyles.Bold) != 0;
            EditorGUI.BeginChangeCheck();
            bool nb = EditorGUILayout.Toggle(isBold);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var txt = rt.GetComponent<TMP_Text>();
                    if (txt != null)
                    {
                        Undo.RecordObject(txt, "Bold");
                        if (nb) txt.fontStyle |= FontStyles.Bold; else txt.fontStyle &= ~FontStyles.Bold;
                        EditorUtility.SetDirty(txt);
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawButtonSection(Button firstBtn)
        {
            DrawSectionLabel(ToolLang.Get("BUTTON", "КНОПКА"));
            DrawInlineGuide("button");

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Interactable", "Активна"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            bool ni = EditorGUILayout.Toggle(firstBtn.interactable);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var btn = rt.GetComponent<Button>();
                    if (btn != null) { Undo.RecordObject(btn, "Interactable"); btn.interactable = ni; EditorUtility.SetDirty(btn); }
                }
            }
            GUILayout.EndHorizontal();

            var c = firstBtn.colors;
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Normal", "Обычная"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var nc = EditorGUILayout.ColorField(c.normalColor);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var btn = rt.GetComponent<Button>();
                    if (btn != null) { var bc = btn.colors; bc.normalColor = nc; Undo.RecordObject(btn, "Btn Color"); btn.colors = bc; EditorUtility.SetDirty(btn); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Highlight", "Hover"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var hc = EditorGUILayout.ColorField(c.highlightedColor);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var btn = rt.GetComponent<Button>();
                    if (btn != null) { var bc = btn.colors; bc.highlightedColor = hc; Undo.RecordObject(btn, "Btn Hover"); btn.colors = bc; EditorUtility.SetDirty(btn); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Pressed", "Нажата"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var pc = EditorGUILayout.ColorField(c.pressedColor);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var btn = rt.GetComponent<Button>();
                    if (btn != null) { var bc = btn.colors; bc.pressedColor = pc; Undo.RecordObject(btn, "Btn Pressed"); btn.colors = bc; EditorUtility.SetDirty(btn); }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawInspectorActionsSection(bool isCanvas)
        {
            DrawSectionLabel(ToolLang.Get("ACTIONS", "ДЕЙСТВИЯ"));
            if (_selectedList.Count == 0) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            if (!isCanvas)
            {
                if (GUILayout.Button(ToolLang.Get("📋 Duplicate", "📋 Дублировать"), GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                {
                    DuplicateSelected();
                }
                GUILayout.Space(4);
            }

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
            if (_selectedList.Count == 0) return;
            List<RectTransform> newSel = new List<RectTransform>();

            foreach (var rt in _selectedList)
            {
                var dup = UnityEngine.Object.Instantiate(rt.gameObject, rt.parent);
                // Берём ИСХОДНОЕ имя оригинала (rt.gameObject.name), а не имя дубля
                // (которое Unity мог обогатить " (Clone)"). EnforceUniqueName сам снимет
                // любой хвост и подберёт первый свободный номер: Image → Image (1) → Image (2)…
                dup.name = EnforceUniqueName(rt.parent, rt.gameObject.name, dup);
                Undo.RegisterCreatedObjectUndo(dup, "Duplicate");
                newSel.Add(dup.GetComponent<RectTransform>());
            }

            _selectedList = newSel;
            RefreshRectsCache();
        }

        private void DeleteSelected()
        {
            if (_selectedList.Count == 0) return;
            string text = _selectedList.Count == 1
                ? string.Format(ToolLang.Get("Delete '{0}'?", "Удалить «{0}»?"), GetDisplayName(FirstSelected))
                : string.Format(ToolLang.Get("Delete {0} elements?", "Удалить {0} эл.?"), _selectedList.Count);

            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete element?", "Удалить элемент(ы)?"),
                text,
                ToolLang.Get("Delete", "Удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            foreach (var rt in _selectedList)
            {
                if (rt != null) Undo.DestroyObjectImmediate(rt.gameObject);
            }

            _selectedList.Clear();
            RefreshRectsCache();
        }

        private static void DrawSectionLabel(string text, string helpKey = "")
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label(text, st, GUILayout.Width(180));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawInlineGuide(string helpKey)
        {
            if (!_showGuideMode) return;
            var body = NovellaUIForgeHelpDB.Get(helpKey);
            if (string.IsNullOrEmpty(body)) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            var textStyle = new GUIStyle(EditorStyles.label);
            textStyle.fontSize = 11;
            textStyle.wordWrap = true;
            textStyle.normal.textColor = NovellaSettingsModule.GetHintColor();
            textStyle.padding = new RectOffset(10, 10, 8, 8);

            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + body), textStyle);

            Color bgColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.1f);
            Color borderColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f);

            EditorGUI.DrawRect(r, bgColor);
            DrawRectBorder(r, borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            GUI.Label(r, "💡 " + body, textStyle);

            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
        }
        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }

    internal static class NovellaUIForgeHelpDB
    {
        public static string Get(string key)
        {
            switch (key)
            {
                case "canvas":
                    return ToolLang.Get(
                        "The Canvas is the foundation of your UI. All interface elements live inside it.\n\nPosition, rotation, and anchors are hidden because the Engine automatically scales the Canvas to fit the screen. You cannot stretch or move it manually.",
                        "Холст (Canvas) — это фундамент всего интерфейса. На нём располагаются все элементы.\n\nПоложение, поворот и якоря отключены, так как движок сам масштабирует холст под размер экрана. Его не нужно растягивать или двигать руками.");
                case "position":
                    return ToolLang.Get(
                        "X/Y — offset from the anchor. W/H — width and height in pixels.\n\nYou rarely need to type these: just drag the element on the canvas, and drag the white square handles to resize.",
                        "X/Y — смещение от якоря. W/H — ширина и высота в пикселях.\n\nОбычно их не нужно вводить вручную: просто тяни элемент мышкой по холсту, а белые квадратики по углам меняют его размер.");
                case "anchor":
                    return ToolLang.Get(
                        "The anchor is the point of the parent that your element 'sticks to'.\n\nClick a corner to snap the element there: it will hold its distance from that corner when the screen size changes.\n\n'Stretch H / V / Fill' — make the element grow together with the parent.",
                        "Якорь — это точка родителя, к которой «прилипает» элемент.\n\nКликни в один из углов — элемент привяжется туда. При смене разрешения экрана он будет держать расстояние от этого угла.\n\n«Растянуть гор. / верт. / Заполнить» — заставит элемент расти вместе с родителем.");
                case "image":
                    return ToolLang.Get(
                        "Sprite — the picture itself. Click the field and pick from the project gallery.\nColor — tints the image (pure white means no tint).\n\nThe 'Type' dropdown controls how the sprite stretches:\n  • Simple — drawn 1:1 as-is\n  • Sliced — '9-slice' with fixed corners (perfect for buttons and panels)\n  • Tiled — repeats to fill the area\n  • Filled — partial fill (HP bar / progress; control via the Fill slider)",
                        "Спрайт — сама картинка. Кликни поле и выбери изображение из галереи проекта.\nЦвет — окрашивает картинку (полностью белый = без оттенка).\n\nВыпадающий список «Тип» решает как картинка будет растягиваться:\n  • Simple — рисует 1:1, как есть\n  • Sliced — «9-slice» с фиксированными углами (идеально для кнопок и панелей)\n  • Tiled — повторяет картинку, заполняя площадь\n  • Filled — частичное заполнение (HP-бар / прогресс; настраивается ползунком «Заполн.»)");
                case "text":
                    return ToolLang.Get(
                        "Here you only set the look: font, size, color, alignment, bold.\n\nThe actual text content comes from localization (RU/EN tables) and is edited in the Localization tab — not here. This keeps translations safe.",
                        "Здесь настраивается только внешний вид: шрифт, размер, цвет, выравнивание.\n\nСам текст берётся из системы локализации (таблицы RU/EN) и редактируется на отдельной вкладке — не здесь. Так переводы никогда не сломаются.");
                case "button":
                    return ToolLang.Get(
                        "A clickable element with three colors:\n• Normal — resting state\n• Highlight — mouse hovers over\n• Pressed — being clicked",
                        "Кликабельный элемент реагирующий на действия цветом:\n• Обычная — состояние покоя\n• Hover — мышь находится над кнопкой\n• Нажата — в момент клика мышкой");
                default:
                    return "";
            }
        }
    }
}