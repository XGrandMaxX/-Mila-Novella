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
        private bool _smartGuides = true;
        // Линии-индикаторы snap'а в координатах canvas. Очищаются на MouseUp.
        private List<float> _snapLinesX = new List<float>();
        private List<float> _snapLinesY = new List<float>();
        // Тоггл подсказок — общий по всему Studio (NovellaSettingsModule.ShowGuide,
        // хранится в EditorPrefs). Локальной копии больше нет — везде используем
        // NovellaSettingsModule.ShowGuide напрямую, чтобы один переключатель
        // в любом модуле/окне управлял всеми хинтами разом.
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
        // Стартовый world-rect элемента в момент BeginDrag — используется
        // smart-guides'ом как «база» при расчёте planned-позиции, чтобы каждый
        // фрейм не наслаивал предыдущие snap-коррекции.
        private Dictionary<RectTransform, Rect> _dragWorldStarts = new Dictionary<RectTransform, Rect>();
        // Зацепка магнитом по осям для hysteresis: если ось уже снаплена в эту
        // world-координату, держимся за неё с расширенным порогом.
        private float? _stickyWorldX;
        private float? _stickyWorldY;

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

        // Свёрнутые ветки в дереве сцены — храним InstanceID, чтобы не держать
        // ссылки на удалённые объекты. Сессионное состояние, пересоздание окна
        // сбрасывает (намеренно — логика «по дефолту всё развернуто»).
        private HashSet<int> _collapsedNodes = new HashSet<int>();
        // 🔒 Заблокированные объекты — нельзя удалять/дублировать/переключать
        // active/двигать в дереве. Только редактор-only флаг (не влияет на игру).
        private HashSet<int> _lockedNodes = new HashSet<int>();
        // ID объектов которые мы уже «видели» как preset-managed — нужно
        // чтобы автозамок ставился ОДИН РАЗ (при первом появлении в _allRects).
        // Если юзер сам снял замок — повторного восстановления не будет.
        private HashSet<int> _seenPresetIds = new HashSet<int>();
        // ✋ Объекты с заблокированным «click pickup»: PickDeepestRectAt их
        // пропускает. Удобно когда канвас A поверх канваса B мешает выбирать
        // элементы B — отключаешь pickup на A и работаешь с B. Сами объекты
        // остаются полностью функциональны в рантайме (флаг editor-only).
        private HashSet<int> _clickBlockedNodes = new HashSet<int>();

        // Активный экземпляр модуля — нужен внешним вызовам (Bindings overview
        // делает PingBinding и должен достучаться до текущего Forge).
        public static NovellaUIForge Instance { get; private set; }

        public void OnEnable(EditorWindow w)
        {
            _window = w;
            Instance = this;
            EditorApplication.hierarchyChanged += OnHierarchyChange;
            EditorApplication.update += OnEditorUpdate;
            // Реакция на выделение в Unity Hierarchy — чтобы дерево Кузницы
            // тоже раскрывало путь к объекту, выбранному снаружи (например в
            // стандартной Unity-иерархии).
            Selection.selectionChanged += OnUnitySelectionChanged;
            EditorApplication.delayCall += () =>
            {
                FindReferences();
                _window?.Repaint();
            };
        }

        private void OnUnitySelectionChanged()
        {
            // Просто триггерим Repaint — дальнейшая логика синка живёт в
            // PullSelectionFromUnityIfNeeded, она вызывается из DrawGUI.
            _window?.Repaint();
        }

        public void OnDisable()
        {
            if (Instance == this) Instance = null;
            EditorApplication.hierarchyChanged -= OnHierarchyChange;
            EditorApplication.update -= OnEditorUpdate;
            Selection.selectionChanged -= OnUnitySelectionChanged;
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

            // Prune мертвых ссылок ПЕРЕД любой отрисовкой инспектора/дерева.
            // После Undo (или внешнего Destroy) RectTransform-ы могут быть
            // «destroyed companion»: managed-обёртка ещё жива, но любой
            // GetComponent<> бросает MissingReferenceException — что в IMGUI
            // ломает GUIClip-стек («pushing more clips than popping»).
            PruneSelection();

            // Если юзер выделил объект СНАРУЖИ Кузницы (например в Unity
            // Hierarchy) — подхватываем это выделение в _selectedList и
            // раскрываем путь в нашем дереве. Без этого после клика в Unity
            // Hierarchy наше дерево не реагировало.
            PullSelectionFromUnityIfNeeded();

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

            const float treeW = 340f; // Расширено для иконок lock/pickup/eye слева + длинные имена
            const float inspectorW = 340f;
            float canvasW = position.width - treeW - inspectorW;
            if (canvasW < 200f) canvasW = 200f;

            Rect treeRect = new Rect(position.x, position.y, treeW, position.height);
            Rect canvasRect = new Rect(position.x + treeW, position.y, canvasW, position.height);
            Rect inspRect = new Rect(position.x + treeW + canvasW, position.y, inspectorW, position.height);

            DrawElementsTree(treeRect);
            DrawCenterCanvas(canvasRect);
            DrawInspector(inspRect);

            SyncSelectionToUnityIfNeeded();
        }

        // Убирает из _selectedList все null и «destroyed companion» ссылки.
        // Дополнительно подчищает кэш _allRects если в нём тоже завелись битые
        // элементы (после Undo Unity не всегда вызывает hierarchyChanged).
        private void PruneSelection()
        {
            // Unity-овский `==` сравнивает с native-частью; для destroyed
            // объектов он возвращает true даже если managed-обёртка не null.
            for (int i = _selectedList.Count - 1; i >= 0; i--)
            {
                var rt = _selectedList[i];
                if (rt == null) _selectedList.RemoveAt(i);
            }

            bool needsRebuild = false;
            for (int i = 0; i < _allRects.Count; i++)
            {
                if (_allRects[i] == null) { needsRebuild = true; break; }
            }
            if (needsRebuild) RefreshRectsCache();

            // Index подсветки тоже мог стать невалидным.
            if (_lastSelectedTreeIndex >= _allRects.Count) _lastSelectedTreeIndex = -1;

            // Кэш переименования.
            if (_renameTarget == null) { _renameTarget = null; _pendingRename = null; }
        }

        // Прокидываем _selectedList в Unity Selection — чтобы:
        //   • в стандартной Hierarchy выделение и раскрытие предков совпадали
        //     с тем что выделено в Forge,
        //   • стандартный инспектор показывал реальный компонент (для тех кому
        //     нужны сырые поля Unity).
        // Сравниваем с прошлой синкой по состоянию (а не каждый кадр пишем
        // Selection.objects), иначе Unity не сможет ловить клики в Hierarchy
        // как «пользовательские» — мы постоянно бы их перезатирали.
        private RectTransform[] _lastSyncedSelection;
        private void SyncSelectionToUnityIfNeeded()
        {
            bool changed = _lastSyncedSelection == null
                        || _lastSyncedSelection.Length != _selectedList.Count;
            if (!changed)
            {
                for (int i = 0; i < _selectedList.Count; i++)
                {
                    if (_lastSyncedSelection[i] != _selectedList[i]) { changed = true; break; }
                }
            }
            if (!changed) return;

            var objs = new List<UnityEngine.Object>(_selectedList.Count);
            foreach (var rt in _selectedList)
            {
                if (rt == null) continue;
                objs.Add(rt.gameObject);
            }
            Selection.objects = objs.ToArray();

            if (_selectedList.Count == 1 && _selectedList[0] != null)
            {
                var rt = _selectedList[0];
                // Раскрываем всех предков выделенного в НАШЕМ дереве слева,
                // сворачиваем посторонние root-канвасы (если их 2+),
                // прокручиваем дерево к выделенной строке.
                ExpandTreeToTarget(rt);

                // Стандартный ping в Unity Hierarchy — побочный бонус,
                // не обязательный для Кузницы.
                EditorGUIUtility.PingObject(rt.gameObject);
            }

            _lastSyncedSelection = _selectedList.ToArray();
        }

        // Если Unity Selection.objects содержит RectTransform'ы которые
        // ОТЛИЧАЮТСЯ от текущего _selectedList, переносим их в _selectedList
        // и раскрываем путь в нашем дереве. Срабатывает когда юзер кликает
        // в стандартной Unity Hierarchy на UI-объект — Кузница теперь
        // сразу подсвечивает его в своём списке.
        private void PullSelectionFromUnityIfNeeded()
        {
            var sel = Selection.objects;
            if (sel == null) return;

            var unityRects = new List<RectTransform>();
            for (int i = 0; i < sel.Length; i++)
            {
                var go = sel[i] as GameObject;
                if (go == null) continue;
                var rt = go.GetComponent<RectTransform>();
                if (rt != null) unityRects.Add(rt);
            }

            // Совпадает с тем что у нас уже выделено? — ничего не делаем.
            if (unityRects.Count == _selectedList.Count)
            {
                bool same = true;
                for (int i = 0; i < unityRects.Count; i++)
                {
                    if (unityRects[i] != _selectedList[i]) { same = false; break; }
                }
                if (same) return;
            }

            // Совпадает с тем что мы только что отправили в Unity? — это эхо
            // нашего же sync, не реагируем (иначе будет feedback loop).
            if (_lastSyncedSelection != null && _lastSyncedSelection.Length == unityRects.Count)
            {
                bool echo = true;
                for (int i = 0; i < unityRects.Count; i++)
                {
                    if (_lastSyncedSelection[i] != unityRects[i]) { echo = false; break; }
                }
                if (echo) return;
            }

            // Принимаем Unity Selection как наше — обновляем _selectedList и
            // раскрываем дерево к выделенному объекту.
            _selectedList.Clear();
            foreach (var rt in unityRects)
            {
                if (rt != null) _selectedList.Add(rt);
            }

            if (_selectedList.Count == 1 && _selectedList[0] != null)
            {
                ExpandTreeToTarget(_selectedList[0]);
            }
        }

        // Раскрывает в дереве UI Forge всех предков целевого объекта.
        // Если в сцене 2+ root-канвасов — сворачивает все остальные, оставляя
        // открытым только тот в котором сейчас выделение. Плюс прокручивает
        // tree-scroll к выделенной строке если она вне viewport.
        //
        // КРИТИЧНО: _collapsedNodes хранит InstanceID именно RectTransform-а
        // (так делает DrawElementsTree и шеврон-toggle: rt.GetInstanceID()).
        // GameObject и RectTransform имеют РАЗНЫЕ InstanceID. Если перепутать —
        // Remove ищет «свой» ID а в наборе лежит «чужой», и сворачивание
        // никогда не снимается.
        private void ExpandTreeToTarget(RectTransform target)
        {
            if (target == null) return;

            // 1. Раскрываем всех предков (убираем RectTransform.InstanceID).
            Transform t = target.parent;
            while (t != null)
            {
                var trt = t as RectTransform;
                if (trt != null)
                {
                    _collapsedNodes.Remove(trt.GetInstanceID());
                }
                // Останавливаемся на root-канвасе (он сам root, нет предков выше).
                var c = t.GetComponent<Canvas>();
                if (c != null && c.isRootCanvas) break;
                t = t.parent;
            }

            // 2. Сворачиваем другие root-канвасы (если их в сцене 2+).
            // Целевой root — определяем поднявшись до root-канваса.
            Transform rootT = target;
            while (rootT.parent != null) rootT = rootT.parent;
            int targetRootRtId = -1;
            if (rootT is RectTransform rrt) targetRootRtId = rrt.GetInstanceID();

            // Считаем сколько всего root-канвасов в _allRects.
            var rootCanvases = new List<RectTransform>();
            foreach (var rt2 in _allRects)
            {
                if (rt2 == null) continue;
                var c2 = rt2.GetComponent<Canvas>();
                if (c2 != null && c2.isRootCanvas) rootCanvases.Add(rt2);
            }

            if (rootCanvases.Count >= 2)
            {
                foreach (var rc in rootCanvases)
                {
                    int id = rc.GetInstanceID();           // ← InstanceID RectTransform
                    if (id == targetRootRtId) continue;
                    _collapsedNodes.Add(id);
                }
            }

            // 3. Прокручиваем tree к выделенной строке если она вне viewport.
            ScrollTreeToTarget(target);

            _window?.Repaint();
        }

        // Простой scroll-to-row для нашего tree. Считаем индекс target в
        // визуально-видимой последовательности (с учётом сворачивания) и
        // ставим _treeScroll.y чтобы строка попала в начало viewport.
        // Высота строки = ROW_H_TREE (см. DrawTreeRow — там GetRect(0, 36)).
        private const float ROW_H_TREE = 36f;
        private void ScrollTreeToTarget(RectTransform target)
        {
            if (target == null) return;

            int visibleIndex = -1;
            int curr = 0;
            RectTransform skipRoot = null;
            for (int i = 0; i < _allRects.Count; i++)
            {
                var rt = _allRects[i];
                if (rt == null) continue;
                if (skipRoot != null)
                {
                    if (rt != skipRoot && rt.IsChildOf(skipRoot)) continue;
                    skipRoot = null;
                }
                if (rt == target) { visibleIndex = curr; break; }
                bool hasChildren = HasVisibleChildren(rt);
                if (hasChildren && _collapsedNodes.Contains(rt.GetInstanceID()))
                {
                    skipRoot = rt;
                }
                curr++;
            }
            if (visibleIndex < 0) return;

            // Без точной высоты viewport ставим target в верхнюю четверть
            // прокрутки — это работает «достаточно хорошо» в большинстве
            // случаев. Если строка близко к началу, не дёргаем scroll.
            float targetY = visibleIndex * ROW_H_TREE;
            if (targetY < _treeScroll.y || targetY > _treeScroll.y + 200f)
            {
                _treeScroll.y = Mathf.Max(0f, targetY - 60f);
            }
        }

        private void DrawNoCanvasState(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG_PRIMARY);
            GUILayout.BeginArea(position);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(820));

            // Заголовок и подзаголовок.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🎨 " + ToolLang.Get("UI Forge", "Кузница UI"), titleSt);
            GUILayout.Space(8);

            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Pick a starting scene template — UI, camera and required components will be set up automatically.",
                "Выбери шаблон сцены с которого начнёшь — UI, камера и нужные компоненты соберутся автоматически."), subSt);

            GUILayout.Space(20);

            // Две карточки сценариев. «Пустой холст» убран — без Player/Launcher
            // граф не воспроизводится, юзеру нет смысла начинать с пустого UI.
            GUILayout.BeginHorizontal();
            DrawStartCard(
                "📱",
                ToolLang.Get("Main Menu",      "Главное Меню"),
                new Color(0.65f, 0.45f, 0.95f),
                ToolLang.Get(
                    "Menu with story selection, MC creation and Settings/Quit buttons. Includes Canvas + EventSystem + StoryLauncher with all bindings ready.",
                    "Меню с выбором истории, редактором персонажа и кнопками Настройки/Выход. Создаёт Canvas + EventSystem + StoryLauncher со всеми связями."),
                ToolLang.Get("Apply preset", "Применить шаблон"),
                () => ApplyMenuPresetFromForge());

            GUILayout.Space(14);

            DrawStartCard(
                "🎮",
                ToolLang.Get("Gameplay",      "Игровая сцена"),
                C_ACCENT,
                ToolLang.Get(
                    "Dialogue box, character layer, choice container. Includes NovellaPlayer with all bindings ready and a starter NovellaTree if you don't have one.",
                    "Диалоговое окно, слой персонажей, контейнер выборов. Создаёт NovellaPlayer со всеми связями и стартовый NovellaTree если у тебя его нет."),
                ToolLang.Get("Apply preset", "Применить шаблон"),
                () => ApplyGameplayPresetFromForge());

            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            // Тонкая мелкая ссылка-обновление — если юзер уже что-то насоздавал
            // через Unity Hierarchy и хочет чтобы Кузница это подхватила.
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var refreshSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            refreshSt.normal.textColor = C_TEXT_4;
            refreshSt.hover.textColor  = C_TEXT_2;
            if (GUILayout.Button(new GUIContent(
                    "🔄 " + ToolLang.Get("Already prepared the scene? Re-scan", "Уже подготовил сцену вручную? Пересканировать"),
                    ToolLang.Get(
                        "Re-detect Camera / Canvas / Player / Launcher in the scene. Useful if you set them up via Unity Hierarchy.",
                        "Перепроверить наличие Camera / Canvas / Player / Launcher в сцене. Нужно если ты собирал их через Unity Hierarchy.")),
                refreshSt))
            {
                FindReferences();
                RefreshRectsCache();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        // Кэш hover/animation-state для preset-карточек на welcome-экране.
        // Хранит текущее значение hover [0..1], плавно интерполируется
        // в OnEditorUpdate при смене hover. На карточку нужен ключ —
        // используем имя пресета.
        private readonly Dictionary<string, float> _cardHoverProgress = new Dictionary<string, float>();
        private bool _hadAnyCardHover;

        // Большая карточка-сценарий на welcome-экране Кузницы.
        // Hover-анимация: фон/border/иконка/CTA плавно «загораются» через
        // float-progress 0..1, который тянется к hover-цели каждый OnGUI.
        // Без этого выглядело статично — переключение шло мгновенно.
        private void DrawStartCard(string icon, string title, Color accent, string desc, string actionLabel, System.Action onClick)
        {
            const float cardW = 240f, cardH = 280f;
            Rect r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));
            bool hover = r.Contains(Event.current.mousePosition);

            // Анимация hover-progress: 0 нейтрально, 1 — полный hover.
            // Стремимся к target плавно, ~10 кадров на полный переход.
            string key = title;
            if (!_cardHoverProgress.TryGetValue(key, out float prog)) prog = 0f;
            float target = hover ? 1f : 0f;
            float speed = 0.18f;
            prog = Mathf.MoveTowards(prog, target, speed);
            _cardHoverProgress[key] = prog;
            // Если есть незавершённая анимация на любой карточке — Repaint
            // чтобы кадры продолжали идти. Without this, карточка «зависает»
            // на промежуточном progress'е после ухода мыши.
            if (Mathf.Abs(prog - target) > 0.001f) _window?.Repaint();
            else if (prog > 0.001f) _hadAnyCardHover = true;

            // Easing — smoothstep даёт более «упругое» наезжающее ощущение.
            float t = prog * prog * (3f - 2f * prog);

            // Лёгкий scale-up при hover. Pivot — центр карточки.
            float scale = Mathf.Lerp(1.0f, 1.03f, t);
            float dx = (cardW * (scale - 1f)) * 0.5f;
            float dy = (cardH * (scale - 1f)) * 0.5f;
            Rect rs = new Rect(r.x - dx, r.y - dy, cardW * scale, cardH * scale);

            // Фон. Прозрачность интерполируется.
            Color bg = new Color(accent.r, accent.g, accent.b, Mathf.Lerp(0.10f, 0.24f, t));
            EditorGUI.DrawRect(rs, bg);

            // Бордер.
            Color border = Color.Lerp(new Color(accent.r, accent.g, accent.b, 0.50f), accent, t);
            DrawRectBorder(rs, border);

            // Цветная полоса слева — толще на hover.
            float stripW = Mathf.Lerp(4f, 6f, t);
            EditorGUI.DrawRect(new Rect(rs.x, rs.y, stripW, rs.height), accent);

            // Иконка — слегка увеличивается.
            float iconFs = Mathf.Lerp(42f, 48f, t);
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = (int)iconFs, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = accent;
            GUI.Label(new Rect(rs.x, rs.y + 24, rs.width, 56), icon, iconSt);

            // Заголовок.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            titleSt.normal.textColor = Color.Lerp(C_TEXT_1, accent, t * 0.6f);
            GUI.Label(new Rect(rs.x, rs.y + 88, rs.width, 22), title, titleSt);

            // Описание.
            var descSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.UpperCenter, wordWrap = true };
            descSt.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(rs.x + 14, rs.y + 116, rs.width - 28, 110), desc, descSt);

            // CTA-кнопка снизу — насыщеннее цвет на hover.
            Rect btnR = new Rect(rs.x + 14, rs.yMax - 42, rs.width - 28, 30);
            EditorGUI.DrawRect(btnR, Color.Lerp(new Color(accent.r, accent.g, accent.b, 0.75f), accent, t));
            var btnSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            btnSt.normal.textColor = Color.white;
            GUI.Label(btnR, actionLabel, btnSt);

            EditorGUIUtility.AddCursorRect(rs, MouseCursor.Link);

            // Клик по любой части карточки. Используем оригинальный r для
            // hit-test'а — анимированный rs нестабилен.
            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
        }

        // Применяет пресет ПРЯМО в активной сцене через публичный фасад
        // NovellaSceneManagerModule. Всё тяжёлое делается через delayCall —
        // иначе IMGUI-стек разваливается посреди отрисовки приветственного экрана.
        private void ApplyMenuPresetFromForge()
        {
            EditorApplication.delayCall += () =>
            {
                NovellaSceneManagerModule.ApplyMainMenuPresetToActiveScene();
                FindReferences();
                RefreshRectsCache();
                _window?.Repaint();
            };
        }

        private void ApplyGameplayPresetFromForge()
        {
            EditorApplication.delayCall += () =>
            {
                NovellaSceneManagerModule.ApplyGameplayPresetToActiveScene();
                FindReferences();
                RefreshRectsCache();
                _window?.Repaint();
            };
        }

        // Лимит на количество корневых канвасов в сцене.
        // SOFT — после него подтверждение, HARD — после него вообще нельзя.
        private const int CANVAS_SOFT_LIMIT = 5;
        private const int CANVAS_HARD_LIMIT = 10;

        private static int CountRootCanvasesInScene()
        {
            int n = 0;
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all) { if (c != null && c.isRootCanvas) n++; }
            return n;
        }

        // Обёртка над CreateCanvasInScene с проверкой лимита.
        // count == 5 → ОДИН диалог про оптимизацию (рассказываем юзеру что
        //              много канвасов — это плохо для перфоманса).
        // count >= 10 → не создаём (но кнопка к этому моменту уже disabled,
        //              сюда приходим только если кто-то вызвал из кода).
        private void TryCreateAdditionalCanvas()
        {
            int count = CountRootCanvasesInScene();
            if (count >= CANVAS_HARD_LIMIT) return;

            if (count == CANVAS_SOFT_LIMIT)
            {
                bool ok = EditorUtility.DisplayDialog(
                    ToolLang.Get("Performance warning", "Предупреждение по производительности"),
                    ToolLang.Get(
                        "You already have 5 root Canvases. Each Canvas is a separate draw call batch and a full hierarchy rebuild on any UI change — too many of them hurts FPS, especially on mobile.\n\nUsually 1–3 canvases are enough: one for the game UI, one for modals, one for HUD. Reuse panels inside an existing canvas where possible.\n\nMaximum is 10. Create one more?",
                        "У тебя уже 5 корневых Холстов. Каждый Холст — это отдельный батч отрисовки и полная перестройка иерархии при любом изменении UI. Большое количество Холстов бьёт по FPS, особенно на мобилках.\n\nОбычно достаточно 1–3 Холстов: один на игровой UI, один на модалки, один на HUD. По возможности переиспользуй панели внутри существующего Холста.\n\nМаксимум — 10. Всё-таки создать ещё один?"),
                    ToolLang.Get("Create",  "Создать"),
                    ToolLang.Get("Cancel",  "Отмена"));
                if (!ok) return;
            }
            CreateCanvasInScene();
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
                // URP требует UniversalAdditionalCameraData на каждой камере,
                // иначе консоль засоряется warning'ами. Через рефлексию —
                // чтобы код собирался и на Built-in pipeline.
                var urpType = System.Type.GetType(
                    "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                if (urpType != null && camGo.GetComponent(urpType) == null) camGo.AddComponent(urpType);
                Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
                _camera = cam;
            }

            // Уникальное имя — чтобы повторное нажатие «+ Холст» не создавало
            // конфликтующие «Холст / Холст / Холст» с одинаковыми именами.
            string canvasBase = ToolLang.Get("Canvas", "Холст");
            string canvasName = canvasBase;
            var existingRoots = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int n = 1; n < 9999; n++)
            {
                bool taken = false;
                foreach (var c in existingRoots) { if (c != null && c.gameObject.name == canvasName) { taken = true; break; } }
                if (!taken) break;
                canvasName = $"{canvasBase} ({n})";
            }

            var canvasGo = new GameObject(canvasName);
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
            // FindReferences отдаёт приоритет канвасу с Player/Launcher, поэтому
            // только что созданный второй холст не выберется автоматически.
            // Принудительно ставим фокус на новый — чтобы новые элементы клали под него.
            _canvas = canvas;
            _selectedList.Clear();
            _selectedList.Add(canvas.GetComponent<RectTransform>());
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
                // Важно использовать Unity-овский `==` против null — это
                // отлавливает «destroyed companion» (managed-обёртка ещё жива,
                // а native-часть уже уничтожена). Если потянуться за .gameObject
                // у такого объекта — летит MissingReferenceException и
                // OnHierarchyChange падает на каждом изменении сцены.
                if (val is GameObject g)
                {
                    if (g == null) continue;
                    go = g;
                }
                else if (val is Component comp)
                {
                    if (comp == null) continue;
                    go = comp.gameObject;
                }
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
            if (go.GetComponent<TextMeshProUGUI>() != null || go.GetComponent<UnityEngine.UI.Text>() != null) return "📝";
            if (go.GetComponent<Image>() != null) return "🖼";
            if (go.GetComponent<Canvas>() != null) return "🖥";
            if (fieldName.Contains("Container") || fieldName.Contains("Panel")) return "▣";
            // RectTransform-контейнер без графики — теперь «папка», метафора
            // понятнее чем абстрактный «пустой квадрат».
            return "📁";
        }

        private void RefreshRectsCache()
        {
            _allRects.Clear();
            // Канвасы перебираем в порядке сцены (через GetRootGameObjects),
            // а не как FindObjectsByType вернёт — у того порядок не гарантирован
            // и канвасы прыгали в дереве при каждом рефреше.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var roots = scene.GetRootGameObjects();
            foreach (var rg in roots)
            {
                if (rg == null) continue;
                // Канвас может быть и сам root-объект и где-то глубже (редко, но бывает).
                // Берём всю иерархию, фильтруем только root-канвасы.
                var canvases = rg.GetComponentsInChildren<Canvas>(true);
                foreach (var c in canvases)
                {
                    if (c == null || !c.isRootCanvas) continue;
                    var rt = c.GetComponent<RectTransform>();
                    if (rt != null) CollectRectsRecursive(rt, _allRects, 0);
                }
            }

            // Автозамок для preset-managed объектов — ставится ОДИН РАЗ при
            // первом появлении объекта в _allRects. Если юзер сам снял замок —
            // мы уже зафиксировали его id в _seenPresetIds, повторно замок
            // ставить не будем. Это защита от случайного удаления preset-кнопок,
            // но юзер всё ещё может снять замок осознанно.
            foreach (var rt in _allRects)
            {
                if (rt == null) continue;
                int id = rt.GetInstanceID();
                if (_seenPresetIds.Contains(id)) continue;
                if (rt.GetComponent<NovellaEngine.Runtime.NovellaPresetMarker>() == null) continue;
                _seenPresetIds.Add(id);
                _lockedNodes.Add(id);
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

        // Учитывает только «видимых» детей — без TMP-сабмешей, которые TMP_Text
        // плодит автоматически при использовании fallback-шрифтов. Раньше у текстов
        // показывался шеврон ▶ хотя «настоящих» детей у них нет.
        private static bool HasVisibleChildren(RectTransform rt)
        {
            if (rt == null) return false;
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null && !IsHiddenInternal(ch)) return true;
            }
            return false;
        }

        // Рекурсивно сворачивает или разворачивает всё поддерево под rt.
        // Используется при Alt+клик на шевроне.
        // collapse = true  — добавляет в _collapsedNodes сам узел и всех потомков с детьми
        // collapse = false — удаляет всё поддерево (включая сам узел) из _collapsedNodes
        private void ToggleSubtreeCollapse(RectTransform rt, bool collapse)
        {
            if (rt == null) return;
            if (HasVisibleChildren(rt))
            {
                int id = rt.GetInstanceID();
                if (collapse) _collapsedNodes.Add(id);
                else          _collapsedNodes.Remove(id);
            }
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null && !IsHiddenInternal(ch))
                    ToggleSubtreeCollapse(ch, collapse);
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

        // Возвращает короткую подпись типа объекта для второй строки в дереве.
        // Помогает понять что это за элемент даже если имя кастомное —
        // например юзер назвал «Character_Layer» а под ним мы пишем «Пустая группа».
        private string GetElementSubtitle(RectTransform rt)
        {
            if (rt == null) return "";
            // Любой root Canvas — это «Холст». Раньше сравнивали только с _canvas
            // (тем что Forge выбрал «активным»), и все остальные канвасы
            // отображались как «Пустая группа».
            var canvas = rt.GetComponent<Canvas>();
            if (canvas != null && canvas.isRootCanvas)
                return ToolLang.Get("Canvas",        "Холст");
            var go = rt.gameObject;
            if (go.GetComponent<UnityEngine.UI.Button>() != null)
                return ToolLang.Get("Button",        "Кнопка");
            if (go.GetComponent<TMPro.TMP_Text>() != null)
                return ToolLang.Get("Text",          "Текст");
            if (go.GetComponent<UnityEngine.UI.Text>() != null)
                return ToolLang.Get("Text (legacy)", "Текст (устар.)");
            if (go.GetComponent<UnityEngine.UI.Image>() != null)
            {
                // Если у Image полупрозрачный/тёмный фон и есть дети — это «панель».
                var img = go.GetComponent<UnityEngine.UI.Image>();
                if (rt.childCount > 0 && img.color.a < 0.99f) return ToolLang.Get("Panel", "Панель");
                return ToolLang.Get("Image", "Картинка");
            }
            if (go.GetComponent<UnityEngine.UI.RawImage>() != null)
                return ToolLang.Get("Raw image", "Сырая картинка");
            // Без графики — пустышка-контейнер.
            // Без графики — это просто контейнер-«папка» без визуала.
            // Метафора «папка» более понятна новичкам, чем «пустая группа».
            return ToolLang.Get("Folder", "Папка");
        }

        // Возвращает текст предупреждения для элемента дерева (или null если ОК).
        // Используется для отрисовки «!» индикатора рядом с именем элемента
        // в дереве + tooltip объясняющий что не так.
        private string GetElementWarning(RectTransform rt)
        {
            if (rt == null) return null;
            var go = rt.gameObject;

            // Legacy UnityEngine.UI.Text без TMP — устаревший компонент,
            // плохо рендерится на больших экранах. Открой инспектор и
            // конвертируй в TextMeshPro.
            if (go.GetComponent<UnityEngine.UI.Text>() != null && go.GetComponent<TMPro.TMP_Text>() == null)
                return ToolLang.Get(
                    "Legacy UI Text — convert to TextMeshPro for sharper rendering. Open the inspector and click 'Convert to TextMeshPro'.",
                    "Устаревший UI Text — конвертируй в TextMeshPro для чёткого рендера. Открой инспектор и нажми «Преобразовать в TextMeshPro».");

            return null;
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

            // Кнопка «+ Холст» — добавить дополнительный канвас (например для
            // модалок или для отдельного слоя HUD). До этого в инструменте
            // подразумевался один канвас на сцену, но юзеры просили возможность
            // строить многослойную сцену.
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            var addCanvasBg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f);
            var addCanvasSt = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 2, 2)
            };
            addCanvasSt.normal.textColor = C_TEXT_1;
            addCanvasSt.hover.textColor = C_ACCENT;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = addCanvasBg;

            // На жёстком лимите (10) кнопка становится недоступной — подсказка
            // объясняет почему.
            int rootCanvasCount = CountRootCanvasesInScene();
            bool atHardLimit = rootCanvasCount >= CANVAS_HARD_LIMIT;
            string addTip = atHardLimit
                ? string.Format(ToolLang.Get(
                    "Hard limit {0} reached — delete or reorganize existing canvases.",
                    "Достигнут лимит {0} — удали или переорганизуй существующие холсты."), CANVAS_HARD_LIMIT)
                : ToolLang.Get(
                    "Add another root Canvas (e.g. modal layer, HUD on top of game).",
                    "Добавить ещё один root Canvas (например слой модалок или HUD над игровым).");

            using (new EditorGUI.DisabledScope(atHardLimit))
            {
                if (GUILayout.Button(new GUIContent("➕  " + ToolLang.Get("Canvas", "Холст"), addTip),
                    addCanvasSt, GUILayout.MinWidth(120)))
                {
                    TryCreateAdditionalCanvas();
                }
            }
            GUI.backgroundColor = prevBg;

            GUILayout.FlexibleSpace();
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            _treeScroll = GUILayout.BeginScrollView(_treeScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            // Подсказка над первым холстом про Alt+клик — чтобы юзер сам
            // догадался про массовое сворачивание/разворачивание поддерева.
            // Видна только когда:
            //   • глобальный тоггл подсказок включён (ShowGuide),
            //   • в дереве есть элементы,
            //   • поиск не активен (иначе занимает дефицитное место).
            if (NovellaSettingsModule.ShowGuide && _allRects.Count > 0 && string.IsNullOrEmpty(_treeFilter))
            {
                GUILayout.Space(4);
                Rect hintRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(hintRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.10f));
                DrawRectBorder(hintRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f));
                var hintTipSt = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
                hintTipSt.normal.textColor = C_TEXT_2;
                GUI.Label(hintRect, ToolLang.Get(
                    "💡 Alt + click on ▼/▶ — expand/collapse the whole subtree at once.",
                    "💡 Alt + клик по ▼/▶ — раскрыть или свернуть всё поддерево разом."), hintTipSt);
                GUILayout.Space(4);
            }

            string filter = _treeFilter?.Trim().ToLowerInvariant() ?? "";
            bool useCollapse = filter.Length == 0;
            // skipRoot — корень свёрнутой ветки. Все элементы которые являются
            // его потомками — пропускаем. Используем ссылку на трансформ а не
            // глубину, потому что ComputeDepth в этом проекте возвращает 0 и
            // для root-canvas и для его прямых детей (логика «канвас не считается»).
            // Из-за этого skipByDepth ломался — приходится сравнивать иерархию.
            RectTransform skipRoot = null;
            bool drewAnyCanvas = false;

            for (int i = 0; i < _allRects.Count; i++)
            {
                var rt = _allRects[i];
                if (rt == null) continue;

                // Внутри свёрнутой ветки — пропускаем всё что под skipRoot.
                // Как только встречаем элемент НЕ-потомок — снимаем skipRoot.
                if (useCollapse && skipRoot != null)
                {
                    if (rt != skipRoot && rt.IsChildOf(skipRoot)) continue;
                    skipRoot = null;
                }

                int depth = ComputeDepth(rt);
                string name = GetDisplayName(rt);
                if (filter.Length > 0 && !name.ToLowerInvariant().Contains(filter)) continue;

                bool isCanvasRoot = false;
                var rootCanvasComp = rt.GetComponent<Canvas>();
                if (rootCanvasComp != null && rootCanvasComp.isRootCanvas) isCanvasRoot = true;

                // Разделитель между канвасами — визуально отделяет каждый
                // следующий root Canvas, чтобы юзер видел границы слоёв.
                if (isCanvasRoot && drewAnyCanvas)
                {
                    GUILayout.Space(8);
                    Rect sep = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(sep, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.45f));
                    GUILayout.Space(8);
                }

                bool hasChildren = HasVisibleChildren(rt);
                bool collapsed = useCollapse && hasChildren && _collapsedNodes.Contains(rt.GetInstanceID());

                DrawTreeRow(rt, name, GetDisplayIcon(rt), depth, i, hasChildren, collapsed);

                if (isCanvasRoot) drewAnyCanvas = true;
                if (collapsed) skipRoot = rt;
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
                "Drag rows to reorder. Drop middle = child, edge = sibling, bottom strip = root.\nLock — protect from edits.  Hand — block click-pickup on the canvas preview.  ●/◌ — toggle active.",
                "Тяни строки. Центр — дочерний, край — рядом, нижняя полоса — в корень.\nЗамок — защита от правок.  Рука — клики проходят сквозь объект на превью.  ●/◌ — вкл/выкл объект."), hint);
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
                            // Корневые канвасы НЕ должны попадать в drop-to-root —
                            // иначе они становятся дочерними _canvas, теряют
                            // isRootCanvas и в дереве отображаются как «Папка».
                            // Канвасы реордерятся через Above/Below на другом канвасе.
                            var dragCanvas = dragRt.GetComponent<Canvas>();
                            if (dragCanvas != null && dragCanvas.isRootCanvas) continue;

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
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Button");

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
            // Регистрируем дочерний text как часть той же undo-группы, чтобы один Ctrl+Z откатывал и кнопку и её label.
            Undo.RegisterCreatedObjectUndo(textGo, "Create Button Label");
            Undo.CollapseUndoOperations(undoGroup);
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

        // Пустышка — RectTransform-контейнер без графики. Используется как
        // логическая группа (например Character_Layer, HUD_Top), внутрь
        // вкладывают другие элементы. По дефолту растягиваем на весь родитель
        // (full-stretch) — для слоя-контейнера это корректно: он покрывает всю
        // канвас-зону, и дочерние элементы анкорятся внутри как нужно.
        // Юзер может потом сжать вручную если нужен «локальный» контейнер.
        private void CreateEmpty()
        {
            string locName = ToolLang.Get("Folder", "Папка");
            var go = new GameObject(locName);
            var rt = PlaceUnderParent(go, new Vector2(200, 200), "Create Folder");
            if (rt != null)
            {
                // Перекрываем якоря на full-stretch и обнуляем offsets.
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                EditorUtility.SetDirty(rt);
            }
        }

        private int ComputeDepth(RectTransform rt)
        {
            if (rt == null) return 0;
            // Глубина считается ВКЛЮЧАЯ сам канвас как уровень-родитель.
            // Прямой ребёнок канваса → depth 1 (а не 0 как было раньше),
            // иначе ветка-линия от канваса к ребёнку не рисуется и визуально
            // непонятно что элемент закреплён за канвасом.
            int d = 0;
            var t = rt.parent;
            while (t != null)
            {
                d++;
                var c = t.GetComponent<Canvas>();
                if (c != null && c.isRootCanvas) break;
                if (d > 16) break;
                t = t.parent;
            }
            return d;
        }

        private void DrawTreeRow(RectTransform rt, string name, string icon, int depth, int index, bool hasChildren, bool collapsed)
        {
            bool isSel = _selectedList.Contains(rt);

            GUILayout.BeginHorizontal();
            GUILayout.Space(0);
            Rect row = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUILayout.Space(0);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, isSel ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f) : Color.clear);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            const float STEP = 12f;
            // На сильно вложенных деревьях индент перестаёт расти после
            // INDENT_DEPTH_CAP — иначе текст на 10+ уровне был бы 5px шириной.
            // Линии-«лесенка» рисуем только до cap; за ним — рисуем единственный
            // маркер «↪», что сигнализирует юзеру: «здесь ещё глубже, реальный
            // depth подсмотри через Unity Hierarchy».
            const int INDENT_DEPTH_CAP = 6;
            int visualDepth = Mathf.Min(depth, INDENT_DEPTH_CAP);
            float baseX = row.x + 12f;
            Color lineCol = new Color(0.30f, 0.32f, 0.40f, 0.55f);

            if (visualDepth > 0)
            {
                for (int d = 1; d <= visualDepth; d++)
                {
                    float lx = baseX + (d - 1) * STEP + STEP * 0.5f;
                    EditorGUI.DrawRect(new Rect(lx, row.y, 1, row.height), lineCol);
                }
                float branchX = baseX + (visualDepth - 1) * STEP + STEP * 0.5f;
                EditorGUI.DrawRect(new Rect(branchX, row.y + row.height * 0.5f, STEP * 0.5f, 1), lineCol);
            }

            float iconX = baseX + visualDepth * STEP;

            // Маркер «↪» для глубже cap'а — занимает 12px перед шевроном.
            if (depth > INDENT_DEPTH_CAP)
            {
                var deepSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                deepSt.normal.textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f);
                GUI.Label(new Rect(iconX, row.y, 12, row.height),
                    new GUIContent("↪", string.Format(ToolLang.Get(
                        "Nesting level {0} (visual indent capped at {1}).",
                        "Уровень вложенности {0} (визуальный отступ ограничен {1})."),
                        depth, INDENT_DEPTH_CAP)), deepSt);
                iconX += 12;
            }

            // Шеврон ▼/▶ — только у строк с детьми. Клик по нему сворачивает
            // ветку. Зона кликабельности 14×row, чтобы было удобно ткнуть.
            // Резервируем место под шеврон ВСЕГДА (даже если детей нет), иначе
            // при сворачивании/разворачивании текст «прыгает» на 14px.
            Rect chevronRect = new Rect(iconX, row.y, 14, row.height);
            if (hasChildren)
            {
                var chSt = new GUIStyle(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                chSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_3;
                GUI.Label(chevronRect, collapsed ? "▶" : "▼", chSt);
            }

            // Состояние locked / click-blocked — два редактор-only флага.
            int rtId = rt.GetInstanceID();
            bool isLocked       = _lockedNodes.Contains(rtId);
            bool isClickBlocked = _clickBlockedNodes.Contains(rtId);

            // Три кнопки СЛЕВА от иконки (после chevron'а): lock | pickup | eye.
            // Раньше они были справа и съедали место под имя — теперь имя
            // получает почти всю ширину строки до warning'а.
            float btnsStartX = iconX + 14; // после chevron
            Rect lockRect = new Rect(btnsStartX,      row.y, 20, row.height);
            Rect pickRect = new Rect(btnsStartX + 20, row.y, 20, row.height);
            Rect eyeRect  = new Rect(btnsStartX + 40, row.y, 20, row.height);
            float iconStartX = btnsStartX + 64; // место под три кнопки + 4px зазора

            // Активность объекта. Различаем три состояния:
            //  • selfActive=true,  visible=true   → норма
            //  • selfActive=false                  → юзер сам выключил, метка «выкл»
            //  • selfActive=true,  visible=false   → сам включён, но скрыт неактивным предком (метка «скрыт»)
            // Серим только первый и второй случай по-разному, чтобы было сразу
            // видно «это я выключил» vs «это родитель скрыл».
            bool selfActive       = rt.gameObject.activeSelf;
            bool actuallyVisible  = rt.gameObject.activeInHierarchy;
            bool selfDisabled     = !selfActive;
            bool hiddenByParent   = selfActive && !actuallyVisible;
            bool isInactive       = selfDisabled || hiddenByParent;
            // Создан ли пресетом — нельзя ли трогать активность вручную.
            bool isPresetManaged = rt.GetComponentInParent<NovellaEngine.Runtime.NovellaPresetMarker>(true) != null;

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            // Три уровня приглушённости иконки:
            //   selfDisabled    — самый приглушённый (C_TEXT_4),
            //   hiddenByParent  — слегка приглушён  (C_TEXT_3 — между обычным и selfDisabled),
            //   норма           — обычный.
            iconSt.normal.textColor = selfDisabled
                ? C_TEXT_4
                : (hiddenByParent ? C_TEXT_3
                                  : (isSel ? C_ACCENT : C_TEXT_3));
            GUI.Label(new Rect(iconStartX, row.y, 22, row.height), icon, iconSt);

            // Имя — верхняя строка ряда.
            var nameSt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.LowerLeft, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 2) };
            // selfDisabled  — italic + светлее (C_TEXT_4)
            // hiddenByParent — чуть темнее обычного (C_TEXT_3), без italic
            // норма         — стандартный (C_TEXT_2 / C_TEXT_1 при выделении)
            if (selfDisabled)
            {
                nameSt.fontStyle = FontStyle.Italic;
                nameSt.normal.textColor = C_TEXT_4;
            }
            else if (hiddenByParent)
            {
                nameSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_3;
            }
            else
            {
                nameSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;
            }
            // Справа теперь только warning ~28px (lock/pickup/eye переехали слева).
            float textX = iconStartX + 24;
            float textW = row.width - (textX - row.x) - 30;
            GUI.Label(new Rect(textX, row.y + 2, textW, 18), name, nameSt);

            // Подпись типа — нижняя строка, мелким серым шрифтом. К ней
            // дописываем КОРОТКИЙ индикатор статуса (длинный текст не влезает
            // в узкую панель дерева):
            //   • selfDisabled  → «· выкл»     (юзер сам выключил)
            //   • hiddenByParent→ «· скрыт»    (сам активен, но родитель выключен)
            // Полный смысл — в tooltip над подписью.
            string subtitle = GetElementSubtitle(rt);
            string subtitleTip = null;
            if (selfDisabled)
            {
                string offTag = ToolLang.Get("off", "выкл");
                subtitle = string.IsNullOrEmpty(subtitle) ? offTag : subtitle + " · " + offTag;
                subtitleTip = ToolLang.Get(
                    "Object is disabled (activeSelf = false). Click ● next to it to enable.",
                    "Объект выключен (activeSelf = false). Кликни ● справа чтобы включить.");
            }
            else if (hiddenByParent)
            {
                string hidTag = ToolLang.Get("hidden", "скрыт");
                subtitle = string.IsNullOrEmpty(subtitle) ? hidTag : subtitle + " · " + hidTag;
                subtitleTip = ToolLang.Get(
                    "This object is enabled but one of its parents is disabled, so it is not rendered.",
                    "Сам объект включён, но один из его родителей выключен — поэтому он не отображается.");
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
                subSt.normal.textColor = selfDisabled ? C_TEXT_4
                                                     : (isSel ? C_TEXT_3 : C_TEXT_4);
                GUI.Label(new Rect(textX, row.y + 18, textW, 14),
                          new GUIContent(subtitle, subtitleTip), subSt);
            }

            // Индикатор проблем «!» — теперь у самого правого края, потому
            // что lock/pickup/eye переехали слева.
            string warning = GetElementWarning(rt);
            if (!string.IsNullOrEmpty(warning))
            {
                Rect warnRect = new Rect(row.xMax - 26, row.y, 22, row.height);
                var warnSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
                warnSt.normal.textColor = new Color(0.95f, 0.66f, 0.30f);
                GUI.Label(warnRect, new GUIContent("!", warning), warnSt);
            }

            // ─── Lock ─────────────────────────────────────────────────────
            // Используем Unity built-in lock-иконку («IN LockButton» / «...on»)
            // вместо эмодзи 🔒 — шрифт IMGUI часто не имеет fallback на эмодзи,
            // и иконка получается «плоским квадратиком». Built-in рендерится
            // в стиле Unity-инспектора и одинакова по высоте с другими.
            string lockTip = isLocked
                ? ToolLang.Get(
                    "Locked: cannot delete, duplicate, drag or toggle active. Click to unlock.",
                    "Заблокирован: нельзя удалять, дублировать, перетаскивать, включать/выключать. Клик — снять.")
                : ToolLang.Get(
                    "Click to lock — protects from accidental delete / drag / disable. Editor-only flag, doesn't affect the game.",
                    "Клик — заблокировать. Защита от случайного удаления / перетаскивания / выключения. Только в редакторе, на игру не влияет.");
            var lockTex = EditorGUIUtility.IconContent(isLocked ? "IN LockButton on" : "IN LockButton").image as Texture;
            // Ярче чем раньше — locked насыщенно amber, unlocked видимый
            // C_TEXT_3 (раньше C_TEXT_4 был почти невидим).
            Color lockColor = isLocked
                ? new Color(0.98f, 0.72f, 0.30f)
                : new Color(C_TEXT_3.r, C_TEXT_3.g, C_TEXT_3.b, 0.85f);
            DrawSmallIconWithTint(lockRect, lockTex, lockColor, lockTip, 16f);

            // ─── Click-pickup ─────────────────────────────────────────────
            // Unity-style hand icon для click-pickup (как в Hierarchy
            // Pickable=on/off). Tooltip объясняет редактор-only смысл.
            string pickTip = isClickBlocked
                ? ToolLang.Get(
                    "Click-pickup blocked: this object is invisible to clicks on the canvas preview. Click to unblock.",
                    "Pickup заблокирован: этот объект игнорируется при клике на превью канваса. Клик — снять.")
                : ToolLang.Get(
                    "Click to block pickup — clicks on the canvas preview will go through this object. Useful when an upper canvas blocks selecting elements behind it. Editor-only.",
                    "Клик — заблокировать pickup. Клики на превью канваса будут проходить сквозь этот объект. Удобно когда верхний канвас мешает выделять элементы под ним. Только в редакторе.");
            var pickTex = EditorGUIUtility.IconContent(isClickBlocked ? "scenepicking_notpickable" : "scenepicking_pickable").image as Texture;
            Color pickColor = isClickBlocked
                ? new Color(0.95f, 0.40f, 0.40f)
                : new Color(C_TEXT_3.r, C_TEXT_3.g, C_TEXT_3.b, 0.85f);
            DrawSmallIconWithTint(pickRect, pickTex, pickColor, pickTip, 16f);

            // ─── Eye (active) ─────────────────────────────────────────────
            // Кнопка-«глаз» переключает activeSelf. Замок и preset-marker
            // НЕ блокируют этот тоггл — юзер может включать/выключать панели
            // (например показать MCCreationPanel в gameplay). Защита замка
            // только от удаления/дублирования/drag.
            string eyeIcon = selfActive ? "●" : "◌";
            string eyeTip = selfActive
                ? ToolLang.Get("Click to disable this object (and its children).",
                               "Клик — выключить объект (и его потомков).")
                : ToolLang.Get("Click to enable this object.",
                               "Клик — включить объект.");
            var eyeSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            eyeSt.normal.textColor = selfActive ? C_TEXT_2 : C_TEXT_4;
            GUI.Label(eyeRect, new GUIContent(eyeIcon, eyeTip), eyeSt);

            Event e = Event.current;

            // Клики по lock / pickup / eye. Каждая кнопка съедает event.
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (lockRect.Contains(e.mousePosition))
                {
                    if (isLocked) _lockedNodes.Remove(rtId);
                    else _lockedNodes.Add(rtId);
                    _window?.Repaint();
                    e.Use();
                    return;
                }
                if (pickRect.Contains(e.mousePosition))
                {
                    if (isClickBlocked) _clickBlockedNodes.Remove(rtId);
                    else _clickBlockedNodes.Add(rtId);
                    _window?.Repaint();
                    e.Use();
                    return;
                }
                if (eyeRect.Contains(e.mousePosition))
                {
                    // Замок и preset-marker НЕ блокируют переключение active.
                    // Юзеру нужно показывать/скрывать панели даже у preset-объектов.
                    Undo.RecordObject(rt.gameObject, selfActive ? "Disable Object" : "Enable Object");
                    rt.gameObject.SetActive(!selfActive);
                    EditorUtility.SetDirty(rt.gameObject);
                    _window?.Repaint();
                    e.Use();
                    return;
                }
            }

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
                    // Locked-объекты не таскаем — drag отключаем.
                    bool anyLocked = _selectedList.Any(x => x != null && IsLockedTransitive(x));
                    if (!anyLocked)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = _selectedList.Select(x => x.gameObject).ToArray();
                        DragAndDrop.StartDrag("Move UI Elements");
                    }
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
            // Клик по шеврону — сворачиваем/разворачиваем ветку и не пробрасываем
            // событие дальше (иначе строка ещё и выделится, что неудобно).
            if (hasChildren && e.type == EventType.MouseDown && e.button == 0 && chevronRect.Contains(e.mousePosition))
            {
                int id = rt.GetInstanceID();
                // Используем _collapsedNodes напрямую, а не `collapsed`, потому что
                // во время поиска collapse игнорируется (useCollapse=false), но
                // юзер всё равно может тыкать шевроны — чтобы заранее настроить
                // что будет свёрнуто после очистки поиска.
                bool currentlyCollapsed = _collapsedNodes.Contains(id);

                if (e.alt)
                {
                    // Alt+клик: рекурсивно ко всему поддереву.
                    // Поведение «как в IDE»:
                    //   - если узел свёрнут → разворачиваем всё под ним
                    //   - если узел развёрнут → сворачиваем всё под ним (включая его сам)
                    bool collapseAll = !currentlyCollapsed;
                    ToggleSubtreeCollapse(rt, collapseAll);
                }
                else
                {
                    if (currentlyCollapsed) _collapsedNodes.Remove(id);
                    else _collapsedNodes.Add(id);
                }
                _window?.Repaint();
                e.Use();
                return;
            }

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

                // Канвас можно реордерить только относительно ДРУГОГО root-канваса
                // на уровне сцены. Любой другой drop:
                //   • Inside на что угодно → канвас стал бы дочерним и потерял
                //     isRootCanvas (Unity автоматически снимает root-флаг у nested
                //     канвасов), после чего в дереве он рендерился бы как «Папка»
                //     и больше не рендерил UI.
                //   • Above/Below на не-канвас → тот же эффект (target.parent
                //     становится новым родителем).
                // Поэтому здесь сразу отбрасываем такие случаи.
                var dragCanvas = dragRt.GetComponent<Canvas>();
                bool dragIsCanvas = dragCanvas != null && dragCanvas.isRootCanvas;
                var targetCanvas = target.GetComponent<Canvas>();
                bool targetIsCanvas = targetCanvas != null && targetCanvas.isRootCanvas;

                if (dragIsCanvas)
                {
                    // Inside в любую цель — нельзя.
                    if (mode == DropMode.Inside) continue;
                    // Above/Below только если target — тоже корневой канвас.
                    if (!targetIsCanvas) continue;
                }

                if (mode == DropMode.Inside)
                {
                    Undo.SetTransformParent(dragRt, target, "Drop Inside");
                    Undo.RegisterCompleteObjectUndo(dragRt.transform, "Drop Inside Sibling Order");
                    dragRt.SetAsLastSibling();
                }
                else
                {
                    // Спец-кейс: оба элемента — корневые канвасы. Меняем порядок
                    // на уровне сцены через SetSiblingIndex (parent у обоих null).
                    bool dragIsRootCanvas = dragRt.parent == null
                        && dragRt.GetComponent<Canvas>() != null
                        && dragRt.GetComponent<Canvas>().isRootCanvas;
                    bool targetIsRootCanvas = target.parent == null
                        && target.GetComponent<Canvas>() != null
                        && target.GetComponent<Canvas>().isRootCanvas;

                    if (dragIsRootCanvas && targetIsRootCanvas)
                    {
                        Undo.RegisterCompleteObjectUndo(dragRt.transform, "Reorder Root Canvases");
                        int tIdx = target.GetSiblingIndex();
                        int newIdx = mode == DropMode.Above ? tIdx : tIdx + 1;
                        dragRt.SetSiblingIndex(newIdx);
                        continue;
                    }

                    var newParent = target.parent;
                    if (newParent == null) continue;
                    Undo.SetTransformParent(dragRt, newParent, "Drop Sibling");
                    Undo.RegisterCompleteObjectUndo(dragRt.transform, "Drop Sibling Order");
                    int targetIdx = target.GetSiblingIndex();
                    int dropIdx = mode == DropMode.Above ? targetIdx : targetIdx + 1;
                    // если drag из того же родителя и шёл сверху — компенсируем сдвиг индекса
                    dragRt.SetSiblingIndex(Mathf.Clamp(dropIdx, 0, newParent.childCount - 1));
                }
            }
        }

        private void ShowTreeContextMenu(RectTransform rt)
        {
            var menu = new GenericMenu();
            string parentName = GetDisplayName(rt);

            menu.AddDisabledItem(new GUIContent(string.Format(ToolLang.Get("Create inside: {0}", "Создать внутри: {0}"), parentName)));
            menu.AddSeparator("");
            // «Пустая группа» — особый кейс: это контейнер-слой (HUD_Top, Character_Layer и т.п.),
            // им юзер обычно структурирует сцену ДО того как добавляет контент.
            // Поэтому ставим её первой и отделяем от обычных UI-элементов.
            menu.AddItem(new GUIContent(ToolLang.Get("📁 Folder", "📁 Папка")), false, () => { _selectedList.Clear(); _selectedList.Add(rt); CreateEmpty(); });
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
            // «Пустая группа» — наверху и через сепаратор: это контейнер-слой,
            // им структурируют сцену до контента.
            menu.AddItem(new GUIContent(ToolLang.Get("📁 Folder", "📁 Папка")), false, () => { _selectedList.Clear(); _selectedList.Add(creationParent.GetComponent<RectTransform>()); CreateEmpty(); });
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
                DrawSnapGuides(drawRectLocal, targetW, targetH);

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

            // Содержимое DrawTogglesGroup (см. там расчёт ширин):
            // pad8 + Сетка70 + 10 + Магнит78 + 10 + Подсказки130 + 10 + [Safe 110+10] + Связи96 + pad8.
            float g2W = _isMobileMode ? 540f : 420f;
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

            const float gap = 10f; // комфортный зазор между кнопками тулбара

            float gridW = 70f;
            DrawIconToggle(new Rect(bx, by, gridW, bh), "▦", ToolLang.Get("Grid", "Сетка"), ref _showGrid);
            bx += gridW + gap;

            float snapW = 78f;
            DrawIconToggle(new Rect(bx, by, snapW, bh), "✦", ToolLang.Get("Snap", "Магнит"), ref _smartGuides);
            bx += snapW + gap;

            float guideW = 130f;
            // Хинты — общий тоггл NovellaSettingsModule.ShowGuide (нельзя передать
            // property по ref, поэтому копируем в локальную и пишем обратно).
            bool guide = NovellaSettingsModule.ShowGuide;
            string guideText = guide ? ToolLang.Get("Hints: On", "Подсказки: Вкл") : ToolLang.Get("Hints: Off", "Подсказки: Выкл");
            DrawIconToggle(new Rect(bx, by, guideW, bh), "💡", guideText, ref guide);
            if (guide != NovellaSettingsModule.ShowGuide) NovellaSettingsModule.ShowGuide = guide;
            bx += guideW + gap;

            if (_isMobileMode)
            {
                DrawIconToggle(new Rect(bx, by, 110f, bh), "📱", ToolLang.Get("Safe Area", "Безоп. зона"), ref _showSafeArea);
                bx += 110f + gap;
            }
            else _showSafeArea = false;

            // Открыть таблицу всех связей сцены — отдельное окно с подсчётом
            // использований по всем NovellaTree-ассетам.
            float overviewW = 96f;
            if (DrawIconButton(new Rect(bx, by, overviewW, bh), "📋", ToolLang.Get("Bindings", "Связи")))
            {
                NovellaEngine.Editor.UIBindings.NovellaBindingsOverviewWindow.Open();
            }
        }

        private bool DrawIconButton(Rect r, string icon, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = hover ? new Color(1, 1, 1, 0.07f) : Color.clear;
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, new Color(1, 1, 1, 0.07f));

            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = C_TEXT_2;
            GUI.Label(new Rect(r.x + 4, r.y, 18, r.height), icon, iconSt);

            var textSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            textSt.normal.textColor = C_TEXT_2;
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 26, r.height), label, textSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
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

        // Линии-индикаторы snap'а в WORLD-координатах. Конвертим к drawRect-пикселям
        // через размер canvas в world-пространстве — это надёжно работает для
        // элементов на любом уровне вложенности.
        private void DrawSnapGuides(Rect drawRect, int targetW, int targetH)
        {
            if (_snapLinesX.Count == 0 && _snapLinesY.Count == 0) return;
            if (_canvas == null) return;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return;

            Rect canvasWorld = GetWorldRect(canvasRT);
            if (canvasWorld.width <= 0.0001f || canvasWorld.height <= 0.0001f) return;

            Color c = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f);

            foreach (float worldX in _snapLinesX)
            {
                float u = (worldX - canvasWorld.xMin) / canvasWorld.width;
                if (u < -0.05f || u > 1.05f) continue;
                float px = drawRect.x + u * drawRect.width;
                EditorGUI.DrawRect(new Rect(px - 0.5f, drawRect.y, 1f, drawRect.height), c);
            }
            foreach (float worldY in _snapLinesY)
            {
                // canvas world y растёт вверх; в GUI — вниз. Инвертируем v.
                float v = 1f - (worldY - canvasWorld.yMin) / canvasWorld.height;
                if (v < -0.05f || v > 1.05f) continue;
                float py = drawRect.y + v * drawRect.height;
                EditorGUI.DrawRect(new Rect(drawRect.x, py - 0.5f, drawRect.width, 1f), c);
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

        // Ping: внешний триггер чтобы Forge выделил конкретный элемент и
        // нарисовал вокруг него пульсирующую рамку — используется например
        // окном «Связи» при клике по строке.
        private static RectTransform _pingTarget;
        private static double _pingStartTime;
        private const float PING_DURATION = 1.4f;

        public static void PingBinding(NovellaEngine.Runtime.UI.NovellaUIBinding b)
        {
            if (b == null) return;
            ShowWindow();
            if (Instance == null) return;
            var rt = b.GetComponent<RectTransform>();
            if (rt == null) return;
            Instance._selectedList.Clear();
            Instance._selectedList.Add(rt);
            _pingTarget = rt;
            _pingStartTime = EditorApplication.timeSinceStartup;
            Instance._window?.Repaint();
        }

        private void DrawPingOverlay(Rect drawRectLocal, int targetW, int targetH)
        {
            if (_pingTarget == null) return;
            double elapsed = EditorApplication.timeSinceStartup - _pingStartTime;
            if (elapsed > PING_DURATION) { _pingTarget = null; return; }

            Rect selRect = ComputeRectScreenForRT(_pingTarget, drawRectLocal, targetW, targetH);
            if (selRect.width < 1f && selRect.height < 1f) return;

            // Пульс: 0..1..0 за 0.5s, повторяется ~3 раза.
            float t = (float)elapsed / PING_DURATION;
            float pulse = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f));
            float thickness = 2f + pulse * 6f;
            float fade = 1f - t;

            Color c = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, fade);
            EditorGUI.DrawRect(new Rect(selRect.x - thickness, selRect.y - thickness, selRect.width + 2 * thickness, thickness), c);
            EditorGUI.DrawRect(new Rect(selRect.x - thickness, selRect.yMax,          selRect.width + 2 * thickness, thickness), c);
            EditorGUI.DrawRect(new Rect(selRect.x - thickness, selRect.y - thickness, thickness, selRect.height + 2 * thickness), c);
            EditorGUI.DrawRect(new Rect(selRect.xMax,           selRect.y - thickness, thickness, selRect.height + 2 * thickness), c);

            _window?.Repaint();
        }

        private void DrawSelectionOverlay(Rect drawRectLocal, int targetW, int targetH)
        {
            DrawPingOverlay(drawRectLocal, targetW, targetH);

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
                    _snapLinesX.Clear();
                    _snapLinesY.Clear();
                    _stickyWorldX = null;
                    _stickyWorldY = null;
                    _dragWorldStarts.Clear();
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
                    // Раньше при первом одиночном клике мы повышали target до
                    // GetTopLevelParent(pickedDeepest) — выделялся ВЕСЬ слой
                    // канваса, и только повторный клик «дрилл-даун»-ил до
                    // реального элемента. Это путало: клик на Текст НЕ
                    // выделял Текст. Сейчас всегда работаем с pickedDeepest —
                    // тем что юзер реально видит и в чём кликает.
                    RectTransform target = pickedDeepest;

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

                    // Раскрываем путь до выделенного объекта в дереве слева
                    // ВСЕГДА — даже если selection не изменился. Иначе если
                    // юзер свернул Холст руками, повторные клики на этот же
                    // объект уже не раскрывают (SyncSelectionToUnityIfNeeded
                    // считает «не менялся» и пропускает).
                    ExpandTreeToTarget(target);

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
                // Pickup-блокировка: если объект (или любой его предок) помечен
                // ✋, клик проходит сквозь него. Аналог Pickable=off в Unity.
                if (IsClickBlockedTransitive(rt)) continue;
                Rect rs = ComputeRectScreenForRT(rt, drawRect, targetW, targetH);
                if (rs.width < 4f || rs.height < 4f) continue;
                if (rs.Contains(mousePosScreen)) return rt;
            }
            return null;
        }

        // Рисует built-in Unity-иконку 16×16 в центре rect, с цветной
        // подкраской через GUI.color. После DrawTexture восстанавливаем
        // оригинальный GUI.color чтобы не подкрасить остальные элементы.
        // Tooltip пробрасываем через невидимый GUI.Label поверх иконки.
        private static void DrawSmallIconWithTint(Rect r, Texture tex, Color tint, string tooltip, float size = 14f)
        {
            if (tex == null) return;
            Rect center = new Rect(r.x + (r.width - size) * 0.5f, r.y + (r.height - size) * 0.5f, size, size);

            Color prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(center, tex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;

            // Tooltip — невидимый Label поверх области.
            if (!string.IsNullOrEmpty(tooltip))
                GUI.Label(r, new GUIContent("", tooltip));
        }

        // Транзитивная проверка click-block: сам объект или любой его предок
        // помечен как ✋. Удобно — заблокировал родительский холст и все его
        // потомки автоматически не кликабельны в превью.
        private bool IsClickBlockedTransitive(RectTransform rt)
        {
            Transform t = rt;
            while (t != null)
            {
                if (t is RectTransform trt && _clickBlockedNodes.Contains(trt.GetInstanceID()))
                    return true;
                t = t.parent;
            }
            return false;
        }

        // Транзитивная проверка замка: сам объект или любой его предок 🔒.
        // Замок на родителе автоматически защищает всех потомков.
        private bool IsLockedTransitive(RectTransform rt)
        {
            Transform t = rt;
            while (t != null)
            {
                if (t is RectTransform trt && _lockedNodes.Contains(trt.GetInstanceID()))
                    return true;
                t = t.parent;
            }
            return false;
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
            _dragWorldStarts.Clear();
            _stickyWorldX = null;
            _stickyWorldY = null;

            RecordUndoAll("Move UI Element");

            foreach (var rt in _selectedList)
            {
                _dragAnchorStarts[rt] = rt.anchoredPosition;
                _dragWorldStarts[rt] = GetWorldRect(rt);
            }
        }

        private void ProcessDragMove(Event e, Rect drawRect, int targetW, int targetH)
        {
            if (_selectedList.Count == 0) return;
            Vector2 deltaPreview = e.mousePosition - _dragMouseStart;
            Vector2 deltaCanvas = PreviewDeltaToCanvasDelta(deltaPreview, drawRect);

            // Smart-guides — корректируем deltaCanvas если можно прилипнуть к
            // соседям/краям/центру родителя. Только для одиночного перетаскивания.
            _snapLinesX.Clear(); _snapLinesY.Clear();
            bool snappedX = false, snappedY = false;
            if (_smartGuides && _selectedList.Count == 1)
            {
                deltaCanvas = ApplySmartSnap(_selectedList[0], deltaCanvas, drawRect, out snappedX, out snappedY);
            }

            foreach (var rt in _selectedList)
            {
                if (!_dragAnchorStarts.TryGetValue(rt, out Vector2 startPos)) continue;

                Vector2 newAnchor = startPos + deltaCanvas;
                if (_showGrid && _gridStep > 0.5f)
                {
                    // Grid применяется ПОСЛЕ snap'а, и только по тем осям где
                    // снапа нет — иначе magnet всегда проигрывает сетке.
                    if (!snappedX) newAnchor.x = Mathf.Round(newAnchor.x / _gridStep) * _gridStep;
                    if (!snappedY) newAnchor.y = Mathf.Round(newAnchor.y / _gridStep) * _gridStep;
                }

                rt.anchoredPosition = newAnchor;
                EditorUtility.SetDirty(rt);
            }
        }

        // Магнитное прилипание к соседним элементам сцены и краям/центру родителя.
        // Считаем всё в WORLD-координатах через RectTransform.GetWorldCorners — так
        // корректно работает для элементов на любом уровне вложенности (canvas → panel
        // → group → button и т.п.), и линии-индикаторы рендерятся в правильных местах.
        //
        // Возвращает скорректированную deltaCanvas (в parent-local единицах, как ожидает
        // anchoredPosition); out-параметры snappedX/snappedY говорят применилось ли
        // прилипание по соответствующей оси (нужно для grid-fallback).
        private Vector2 ApplySmartSnap(RectTransform rt, Vector2 deltaCanvas, Rect drawRect, out bool snappedX, out bool snappedY)
        {
            snappedX = false; snappedY = false;
            if (rt == null || rt.parent == null || _canvas == null) return deltaCanvas;
            if (!(rt.parent is RectTransform pRT)) return deltaCanvas;
            var canvasRT = _canvas.GetComponent<RectTransform>();
            if (canvasRT == null) return deltaCanvas;

            // Конвертим parent-local дельту в world-дельту через lossyScale родителя.
            Vector2 pLossy = pRT.lossyScale;
            if (Mathf.Abs(pLossy.x) < 0.0001f) pLossy.x = 1f;
            if (Mathf.Abs(pLossy.y) < 0.0001f) pLossy.y = 1f;
            Vector2 deltaWorld = new Vector2(deltaCanvas.x * pLossy.x, deltaCanvas.y * pLossy.y);

            // Порог в world-юнитах: 12 px превью, переведённые через текущий zoom.
            // Превью показывает canvas — поэтому px-per-world = drawRect.width / canvasWorldWidth.
            Rect canvasWorld = GetWorldRect(canvasRT);
            if (canvasWorld.width <= 0.0001f) return deltaCanvas;
            float pxPerWorld = drawRect.width / canvasWorld.width;
            float threshold = 12f / Mathf.Max(0.001f, pxPerWorld);

            // Запланированные world-bounds — берём СТАРТОВЫЙ rect элемента
            // (зафиксированный в BeginDrag) + текущая world-дельта от старта.
            // Это критично: использовать GetWorldRect(rt) каждый кадр нельзя,
            // так как rt уже сдвинут предыдущими фреймами snap-коррекции и
            // получится двойной учёт.
            if (!_dragWorldStarts.TryGetValue(rt, out Rect rtStartWorld))
                rtStartWorld = GetWorldRect(rt);
            Rect planned = new Rect(rtStartWorld.x + deltaWorld.x, rtStartWorld.y + deltaWorld.y, rtStartWorld.width, rtStartWorld.height);

            float bestX = float.MaxValue, bestY = float.MaxValue;
            float bestDx = 0f, bestDy = 0f;
            float? snapX = null, snapY = null;

            // Hysteresis: ось уже примагниченная к чему-то держится с 1.6× порогом —
            // даёт ощущение «прилипло, и теперь надо приложить усилие чтобы оторвать».
            float xThreshold = _stickyWorldX.HasValue ? threshold * 1.6f : threshold;
            float yThreshold = _stickyWorldY.HasValue ? threshold * 1.6f : threshold;

            // Цель 1: сиблинги под тем же родителем (исключая сам dragged).
            foreach (Transform sib in pRT)
            {
                if (sib == rt.transform) continue;
                if (!(sib is RectTransform sibRT)) continue;
                if (!sibRT.gameObject.activeInHierarchy) continue;
                Rect sw = GetWorldRect(sibRT);
                if (sw.width <= 0.0001f || sw.height <= 0.0001f) continue;

                TrySnapAxis(planned.xMin,    sw.xMin,    xThreshold, ref bestX, ref bestDx, ref snapX, sw.xMin);
                TrySnapAxis(planned.center.x, sw.center.x, xThreshold, ref bestX, ref bestDx, ref snapX, sw.center.x);
                TrySnapAxis(planned.xMax,    sw.xMax,    xThreshold, ref bestX, ref bestDx, ref snapX, sw.xMax);
                TrySnapAxis(planned.yMin,    sw.yMin,    yThreshold, ref bestY, ref bestDy, ref snapY, sw.yMin);
                TrySnapAxis(planned.center.y, sw.center.y, yThreshold, ref bestY, ref bestDy, ref snapY, sw.center.y);
                TrySnapAxis(planned.yMax,    sw.yMax,    yThreshold, ref bestY, ref bestDy, ref snapY, sw.yMax);
            }

            // Цель 2: границы и центр родителя (часто это canvas).
            Rect pW = GetWorldRect(pRT);
            TrySnapAxis(planned.xMin,    pW.xMin,    xThreshold, ref bestX, ref bestDx, ref snapX, pW.xMin);
            TrySnapAxis(planned.center.x, pW.center.x, xThreshold, ref bestX, ref bestDx, ref snapX, pW.center.x);
            TrySnapAxis(planned.xMax,    pW.xMax,    xThreshold, ref bestX, ref bestDx, ref snapX, pW.xMax);
            TrySnapAxis(planned.yMin,    pW.yMin,    yThreshold, ref bestY, ref bestDy, ref snapY, pW.yMin);
            TrySnapAxis(planned.center.y, pW.center.y, yThreshold, ref bestY, ref bestDy, ref snapY, pW.center.y);
            TrySnapAxis(planned.yMax,    pW.yMax,    yThreshold, ref bestY, ref bestDy, ref snapY, pW.yMax);

            if (bestX != float.MaxValue)
            {
                deltaCanvas.x += bestDx / pLossy.x;
                snappedX = true;
                _stickyWorldX = snapX;
                if (snapX.HasValue) _snapLinesX.Add(snapX.Value);
            }
            else _stickyWorldX = null;

            if (bestY != float.MaxValue)
            {
                deltaCanvas.y += bestDy / pLossy.y;
                snappedY = true;
                _stickyWorldY = snapY;
                if (snapY.HasValue) _snapLinesY.Add(snapY.Value);
            }
            else _stickyWorldY = null;

            return deltaCanvas;
        }

        private static void TrySnapAxis(float draggedAxis, float targetAxis, float threshold, ref float bestDist, ref float bestDelta, ref float? snapLine, float lineAt)
        {
            float d = targetAxis - draggedAxis;
            float ad = Mathf.Abs(d);
            if (ad < threshold && ad < bestDist)
            {
                bestDist = ad;
                bestDelta = d;
                snapLine = lineAt;
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

            // isCanvas теперь срабатывает и при множественном выделении —
            // если ВСЕ выбранные элементы это root-канвасы, мы прячем
            // position/якоря (которые для канваса не редактируются — Canvas сам
            // тянет размер от рендера) и оставляем только глобальный RT тоггл
            // и кнопки удаления/действий.
            bool isCanvas = _selectedList.Count > 0 && _selectedList.All(rt =>
                rt != null
                && rt.GetComponent<Canvas>() != null
                && rt.GetComponent<Canvas>().isRootCanvas);
            bool isMixed = !AreSelectedTypesHomogeneous();

            if (isCanvas)
            {
                DrawSectionLabel(ToolLang.Get("CANVAS", "ХОЛСТ (CANVAS)"));
                DrawInlineGuide("canvas");
                // RT-секция работает и при множественном выделении — кликом
                // переключаем сразу у всех. Если состояния разные, агрегатор
                // показывает «Mixed» и первый клик сводит всех к ВКЛ.
                var canvases = _selectedList.Select(rt => rt.GetComponent<Canvas>()).Where(c => c != null).ToList();
                DrawCanvasGlobalRTSection(canvases);
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

                // Raycast Target — компактный тоггл в заголовке инспектора.
                // Виден сразу при выделении, не ныряет в подвал. Показываем только
                // для элементов с Graphic-компонентом.
                var graphic = single.GetComponent<UnityEngine.UI.Graphic>();
                if (graphic != null)
                {
                    DrawHeaderRTToggle(graphic);
                    GUILayout.Space(4);
                }

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
            DrawSectionLabel(ToolLang.Get("ANCHOR & STRETCH", "ЯКОРЬ И РАСТЯНУТЬ"));
            DrawInlineGuide("anchor");

            // Две плашки 3×3 — Якорь и Растянуть — стоят бок-о-бок: пользователь
            // их визуально сравнивает и видит «как заякорено» + «куда тянется» в одном ряду.
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            DrawAnchorGrid();
            GUILayout.Space(10);
            DrawStretchGrid();

            GUILayout.FlexibleSpace();
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

        // 3×3 плашка ЯКОРЕЙ — пресеты anchorMin/Max для одной точки или растяжки.
        // Извлечена из DrawInspectorAnchorSection чтобы рисоваться рядом с
        // плашкой РАСТЯНУТЬ.
        private void DrawAnchorGrid()
        {
            const float cell = 30f;
            Rect grid = GUILayoutUtility.GetRect(cell * 3 + 8, cell * 3 + 8, GUILayout.Width(cell * 3 + 8));
            EditorGUI.DrawRect(grid, C_BG_RAISED);
            DrawRectBorder(grid, C_BORDER);

            (string name, Vector2 min, Vector2 max)[] presets = new (string, Vector2, Vector2)[]
            {
                ("↖", new Vector2(0,1),     new Vector2(0,1)),     ("↑", new Vector2(0.5f,1),    new Vector2(0.5f,1)),    ("↗", new Vector2(1,1),    new Vector2(1,1)),
                ("←", new Vector2(0,0.5f),  new Vector2(0,0.5f)),  ("●", new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f)), ("→", new Vector2(1,0.5f), new Vector2(1,0.5f)),
                ("↙", new Vector2(0,0),     new Vector2(0,0)),     ("↓", new Vector2(0.5f,0),    new Vector2(0.5f,0)),    ("↘", new Vector2(1,0),    new Vector2(1,0)),
            };

            for (int i = 0; i < 9; i++)
            {
                int col = i % 3, row = i / 3;
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
        }

        // 3×3 плашка РАСТЯНУТЬ — внешне идентична якорной. Тянет сторону/угол
        // элемента к соответствующей стороне/углу родителя. Активная ячейка
        // подсвечивается акцентом.
        //
        // Иконки выбраны из Arrows-блока U+21xx (хорошо поддержано Liberation Sans):
        //   ↖ ↥ ↗ — корнер-СЗ, стрелка-к-горизонтальной-планке, корнер-СВ
        //   ⇤ ▣ ⇥ — стрелка-к-вертикальной-планке, fill, стрелка-к-вертикальной-планке
        //   ↙ ↧ ↘ — корнер-ЮЗ, стрелка-к-горизонтальной-планке-снизу, корнер-ЮВ
        // ↥/↧ заменили ⤒/⤓ (U+29xx) — те не входят в стандартный Unity-шрифт.
        private void DrawStretchGrid()
        {
            const float cell = 30f;
            Rect grid = GUILayoutUtility.GetRect(cell * 3 + 8, cell * 3 + 8, GUILayout.Width(cell * 3 + 8));
            EditorGUI.DrawRect(grid, C_BG_RAISED);
            DrawRectBorder(grid, C_BORDER);

            (string icon, string tip, StretchKind kind)[] presets = new (string, string, StretchKind)[]
            {
                ("↖", ToolLang.Get("Stretch to top-left corner",     "Растянуть в верхний-левый угол"),  StretchKind.ToTopLeft),
                ("↥", ToolLang.Get("Stretch to top edge",            "Растянуть до верхнего края"),       StretchKind.ToTop),
                ("↗", ToolLang.Get("Stretch to top-right corner",    "Растянуть в верхний-правый угол"), StretchKind.ToTopRight),
                ("⇤", ToolLang.Get("Stretch to left edge",           "Растянуть до левого края"),         StretchKind.ToLeft),
                ("▣", ToolLang.Get("Fill — stretch to parent",       "Заполнить — растянуть на родителя"), StretchKind.FillAll),
                ("⇥", ToolLang.Get("Stretch to right edge",          "Растянуть до правого края"),        StretchKind.ToRight),
                ("↙", ToolLang.Get("Stretch to bottom-left corner",  "Растянуть в нижний-левый угол"),    StretchKind.ToBottomLeft),
                ("↧", ToolLang.Get("Stretch to bottom edge",         "Растянуть до нижнего края"),        StretchKind.ToBottom),
                ("↘", ToolLang.Get("Stretch to bottom-right corner", "Растянуть в нижний-правый угол"),   StretchKind.ToBottomRight),
            };

            for (int i = 0; i < 9; i++)
            {
                int col = i % 3, row = i / 3;
                Rect cellRect = new Rect(grid.x + 4 + col * cell, grid.y + 4 + row * cell, cell - 2, cell - 2);

                bool isCurrent = IsStretchActive(presets[i].kind);
                bool hover = cellRect.Contains(Event.current.mousePosition);

                Color bg = isCurrent ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f) : C_BG_PRIMARY;
                if (hover) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
                EditorGUI.DrawRect(cellRect, bg);
                DrawRectBorder(cellRect, isCurrent ? C_ACCENT : C_BORDER);

                var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = isCurrent ? C_ACCENT : C_TEXT_2;
                GUI.Label(cellRect, new GUIContent(presets[i].icon, presets[i].tip), st);

                if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                {
                    StretchEdge(presets[i].kind);
                    Event.current.Use();
                }
            }
        }

        // Активна ли ячейка stretch-плашки для текущего FirstSelected.
        // Логика: сторона считается «прибитой» к родителю если её anchor-ratio
        // = 0 или 1 И offset ≈ 0. Дальше сравниваем какие именно стороны прибиты
        // против предустановки.
        private bool IsStretchActive(StretchKind kind)
        {
            if (FirstSelected == null) return false;
            var rt = FirstSelected;
            const float epsR = 0.001f;
            const float epsO = 0.5f;

            bool L  = Mathf.Abs(rt.anchorMin.x - 0f) < epsR && Mathf.Abs(rt.offsetMin.x) < epsO;
            bool R  = Mathf.Abs(rt.anchorMax.x - 1f) < epsR && Mathf.Abs(rt.offsetMax.x) < epsO;
            bool T  = Mathf.Abs(rt.anchorMax.y - 1f) < epsR && Mathf.Abs(rt.offsetMax.y) < epsO;
            bool B  = Mathf.Abs(rt.anchorMin.y - 0f) < epsR && Mathf.Abs(rt.offsetMin.y) < epsO;

            switch (kind)
            {
                case StretchKind.FillAll:        return L && R && T && B;
                case StretchKind.ToLeft:         return L && !R;
                case StretchKind.ToRight:        return R && !L;
                case StretchKind.ToTop:          return T && !B;
                case StretchKind.ToBottom:       return B && !T;
                case StretchKind.ToTopLeft:      return L && T && !R && !B;
                case StretchKind.ToTopRight:     return R && T && !L && !B;
                case StretchKind.ToBottomLeft:   return L && B && !R && !T;
                case StretchKind.ToBottomRight:  return R && B && !L && !T;
            }
            return false;
        }

        // Заполнить родителя — anchor 0..1, нулевые offset. Работает на любом
        // количестве выделенных, включая один.
        private void FillStretch()
        {
            StretchEdge(StretchKind.FillAll);
        }

        // Все 9 направлений stretch-плашки.
        // Edges: ToLeft/Right/Top/Bottom — тянут одну сторону к родителю.
        // Corners: ToTopLeft/etc. — тянут две смежные стороны (две стороны прибиты).
        // FillAll — все 4 стороны прибиты к родителю (полное заполнение).
        private enum StretchKind {
            ToLeft, ToRight, ToTop, ToBottom,
            ToTopLeft, ToTopRight, ToBottomLeft, ToBottomRight,
            FillAll
        }

        // Растягивает элемент: указанный край дотягивается до края родителя,
        // противоположный край ОСТАЁТСЯ на текущей мировой позиции.
        // Реализация — через переустановку anchorMin/Max на новые ratio'ы +
        // обнуление offsetMin/Max. Работает с любого начального anchor-режима
        // (точечного / стретча / смешанного).
        private void StretchEdge(StretchKind kind)
        {
            if (_selectedList == null || _selectedList.Count == 0) return;

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Stretch " + kind);

            foreach (var rt in _selectedList)
            {
                if (rt == null) continue;
                if (!(rt.parent is RectTransform pRT)) continue;
                Undo.RecordObject(rt, "Stretch");

                Rect rtW = GetWorldRect(rt);
                Rect pW  = GetWorldRect(pRT);
                if (pW.width <= 0.0001f || pW.height <= 0.0001f) continue;

                // Новые мировые границы — стартуем с текущих, тянем нужный край.
                float left   = rtW.xMin;
                float right  = rtW.xMax;
                float bottom = rtW.yMin;
                float top    = rtW.yMax;

                switch (kind)
                {
                    case StretchKind.ToLeft:         left = pW.xMin; break;
                    case StretchKind.ToRight:        right = pW.xMax; break;
                    case StretchKind.ToTop:          top = pW.yMax; break;
                    case StretchKind.ToBottom:       bottom = pW.yMin; break;
                    case StretchKind.ToTopLeft:      left = pW.xMin; top = pW.yMax; break;
                    case StretchKind.ToTopRight:     right = pW.xMax; top = pW.yMax; break;
                    case StretchKind.ToBottomLeft:   left = pW.xMin; bottom = pW.yMin; break;
                    case StretchKind.ToBottomRight:  right = pW.xMax; bottom = pW.yMin; break;
                    case StretchKind.FillAll:        left = pW.xMin; right = pW.xMax; top = pW.yMax; bottom = pW.yMin; break;
                }

                // Конвертируем мировые границы в anchorMin/Max (доли от родителя)
                // и зануляем offset'ы — так получается точно нужный rect, и при
                // ресайзе родителя элемент будет масштабироваться правильно.
                float lx = (left   - pW.xMin) / pW.width;
                float rx = (right  - pW.xMin) / pW.width;
                float by = (bottom - pW.yMin) / pW.height;
                float ty = (top    - pW.yMin) / pW.height;

                rt.anchorMin = new Vector2(lx, by);
                rt.anchorMax = new Vector2(rx, ty);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                EditorUtility.SetDirty(rt);
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        // (AlignEdges/AlignKind убраны — все 7 кнопок плашки теперь делают
        //  растягивание через StretchEdge, а не выравнивание позиции.)

        private static readonly Vector3[] _bboxBuf = new Vector3[4];
        private static Rect GetWorldRect(RectTransform rt)
        {
            rt.GetWorldCorners(_bboxBuf);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                if (_bboxBuf[i].x < xMin) xMin = _bboxBuf[i].x;
                if (_bboxBuf[i].x > xMax) xMax = _bboxBuf[i].x;
                if (_bboxBuf[i].y < yMin) yMin = _bboxBuf[i].y;
                if (_bboxBuf[i].y > yMax) yMax = _bboxBuf[i].y;
            }
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
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

            // RT-тоггл вынесен в заголовок инспектора (см. DrawInspectorHeader)
            // — не дублируем здесь.

            // Bindings — связь UI-элемента с графом/локализацией/переменными.
            DrawBindingSection();
        }

        // Canvas-уровневый «глобальный RT». Реализован через CanvasGroup на
        // Canvas-объекте: blocksRaycasts + interactable одновременно, чтобы
        // отключение давало и raycast-блокировку, и серый-disabled-вид.
        // Индивидуальные RT-настройки элементов сохраняются — они активны
        // только когда canvas-RT включен. Так можно одной кнопкой «глушить»
        // весь UI (например когда показывается модалка).
        private void DrawCanvasGlobalRTSection(List<Canvas> canvases)
        {
            if (canvases == null || canvases.Count == 0) return;

            DrawSectionLabel(ToolLang.Get("GLOBAL CLICKS", "ГЛОБАЛЬНЫЕ КЛИКИ (RT)"));
            DrawFieldHint(ToolLang.Get(
                "Master switch for all UI clicks on this canvas. When OFF — every element is grayed out and won't receive clicks (whatever their own RT). When ON — elements use their individual RT toggles. Useful for «disable UI when modal is open».",
                "Главный выключатель кликов для всего канваса. Если ВЫКЛ — все элементы серые и не реагируют на клики (вне зависимости от их собственного RT). Если ВКЛ — элементы используют свои индивидуальные RT-настройки. Удобно когда нужно «заглушить» весь UI пока показана модалка."));

            // Агрегированное состояние:
            //   allActive     — все включены
            //   allInactive   — все выключены
            //   иначе         — смешанное (показываем как ВЫКЛ-стиль с пометкой Mixed)
            bool allActive = true, allInactive = true;
            foreach (var c in canvases)
            {
                if (c == null) continue;
                var g = c.GetComponent<CanvasGroup>();
                bool a = g == null ? true : g.blocksRaycasts;
                if (a) allInactive = false; else allActive = false;
            }
            bool mixed = !allActive && !allInactive;
            // Для отрисовки кнопки используем «активный» вид только если все ВКЛ.
            bool active = allActive;

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);

            const float cell = 30f;
            Rect r = GUILayoutUtility.GetRect(cell, cell, GUILayout.Width(cell), GUILayout.Height(cell));
            bool hover = r.Contains(Event.current.mousePosition);

            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, hover ? 0.45f : 0.30f)
                              : hover ? new Color(0.95f, 0.66f, 0.30f, 0.20f) : new Color(0.95f, 0.66f, 0.30f, 0.10f);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, active ? C_ACCENT : new Color(0.95f, 0.66f, 0.30f, 0.55f));

            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = active ? C_ACCENT : new Color(0.95f, 0.66f, 0.30f);
            GUI.Label(r, "RT", st);
            if (!active)
            {
                Color line = new Color(0.95f, 0.66f, 0.30f);
                EditorGUI.DrawRect(new Rect(r.x + 4, r.y + r.height * 0.5f - 1, r.width - 8, 2f), line);
            }

            GUILayout.Space(8);
            var stateSt = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            stateSt.normal.textColor = active ? C_TEXT_2 : new Color(0.95f, 0.66f, 0.30f);
            string label;
            if (mixed)
                label = ToolLang.Get("MIXED — click to enable all", "СМЕШАНО — клик включит все");
            else if (active)
                label = canvases.Count > 1
                    ? string.Format(ToolLang.Get("ON for {0} canvases", "ВКЛ на {0} холстах"), canvases.Count)
                    : ToolLang.Get("ON — UI is interactive", "ВКЛ — UI кликабелен");
            else
                label = canvases.Count > 1
                    ? string.Format(ToolLang.Get("OFF for {0} canvases", "ВЫКЛ на {0} холстах"), canvases.Count)
                    : ToolLang.Get("OFF — UI is frozen", "ВЫКЛ — UI заморожен");
            GUILayout.Label(label, stateSt);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                // Целевое состояние:
                //   mixed  → выравниваем всех в ВКЛ
                //   иначе  → инвертируем
                bool newActive = mixed ? true : !active;
                foreach (var c in canvases)
                {
                    if (c == null) continue;
                    var g = c.GetComponent<CanvasGroup>();
                    if (g == null) g = Undo.AddComponent<CanvasGroup>(c.gameObject);
                    Undo.RecordObject(g, "Toggle Canvas Global RT");
                    g.blocksRaycasts = newActive;
                    g.interactable   = newActive;
                    EditorUtility.SetDirty(g);
                }
                Event.current.Use();
            }
        }

        // Маленький RT-тоггл в шапке инспектора. 22×20 кнопка — еле занимает
        // место, но всегда на виду. Tooltip объясняет назначение.
        private void DrawHeaderRTToggle(UnityEngine.UI.Graphic graphic)
        {
            const float w = 26f, h = 20f;
            Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            bool hover = r.Contains(Event.current.mousePosition);
            bool active = graphic.raycastTarget;

            string tooltip = active
                ? ToolLang.Get("Raycast Target ON — element receives clicks. Click to disable (decorative panels above buttons should be OFF).",
                               "Raycast Target ВКЛ — элемент ловит клики. Кликни чтобы выключить (декоративные панели над кнопками должны быть ВЫКЛ — иначе они «съедят» клик).")
                : ToolLang.Get("Raycast Target OFF — clicks pass through this element. Click to enable.",
                               "Raycast Target ВЫКЛ — клики проходят сквозь элемент. Кликни чтобы включить.");

            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, hover ? 0.45f : 0.30f)
                              : hover ? new Color(0.95f, 0.66f, 0.30f, 0.20f) : new Color(0.95f, 0.66f, 0.30f, 0.10f);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, active ? C_ACCENT : new Color(0.95f, 0.66f, 0.30f, 0.55f));

            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = active ? C_ACCENT : new Color(0.95f, 0.66f, 0.30f);
            GUI.Label(r, new GUIContent("RT", tooltip), st);

            // Перечёркивание у выключенного — мгновенный «отключено» вижуал.
            if (!active)
            {
                Color line = new Color(0.95f, 0.66f, 0.30f);
                EditorGUI.DrawRect(new Rect(r.x + 4, r.y + r.height * 0.5f - 0.5f, r.width - 8, 1.5f), line);
            }

            if (Event.current.type == EventType.MouseDown && hover)
            {
                foreach (var rt in _selectedList)
                {
                    var g = rt.GetComponent<UnityEngine.UI.Graphic>();
                    if (g == null) continue;
                    Undo.RecordObject(g, "Toggle Raycast Target");
                    g.raycastTarget = !active;
                    EditorUtility.SetDirty(g);
                }
                Event.current.Use();
            }
        }

        // ─── BINDINGS section ───────────────────────────────────────────────────
        // Полный редактор привязки — здесь и только здесь пользователь настраивает
        // связь UI-элемента с историей. В Unity-инспектор лезть НЕ нужно: всё
        // что отсюда меняется, сразу видят ноды графа в их пикерах.

        private void DrawBindingSection()
        {
            DrawSectionLabel(ToolLang.Get("LINK TO STORY", "СВЯЗАТЬ С ИСТОРИЕЙ"), "bindings");
            DrawInlineGuide("bindings");

            var binding = FirstSelected.GetComponent<NovellaEngine.Runtime.UI.NovellaUIBinding>();

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            if (binding == null)
            {
                var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get(
                    "Make this element addressable from story nodes — give it a friendly name and link it to localization, variables or click navigation.",
                    "Сделай этот элемент доступным из нод истории — задай дружелюбное имя и привяжи к локализации, переменным или переходу по клику."), st);
                GUILayout.Space(6);

                if (NovellaSettingsModule.AccentButton(ToolLang.Get("➕  Make this linkable", "➕  Сделать привязываемым"), GUILayout.Height(26)))
                {
                    foreach (var rt in _selectedList)
                    {
                        if (rt == null) continue;
                        if (rt.GetComponent<NovellaEngine.Runtime.UI.NovellaUIBinding>() == null)
                            NovellaEngine.Runtime.UI.NovellaUIBinding.GetOrAdd(rt.gameObject);
                    }
                }
            }
            else
            {
                Undo.RecordObject(binding, "Edit UI Binding");

                // Friendly name — главное поле, видно везде в пикерах.
                GUILayout.Label(ToolLang.Get("Display name", "Имя для пикера"), EditorStyles.miniBoldLabel);
                DrawFieldHint(ToolLang.Get(
                    "Friendly name shown in graph node pickers and the Bindings overview. Doesn't affect the game — only how YOU find this element later.",
                    "Дружелюбное имя — видно везде где этот элемент выбирается из ноды графа или таблицы Связей. На игру не влияет, нужно только тебе чтобы потом узнавать этот элемент в списке."));
                string newName = EditorGUILayout.TextField(binding.Name);
                if (newName != binding.Name) { binding.Name = newName; EditorUtility.SetDirty(binding); }

                GUILayout.Space(8);
                GUILayout.Label(ToolLang.Get("Localization key (optional)", "Ключ локализации (опц.)"), EditorStyles.miniBoldLabel);
                DrawFieldHint(ToolLang.Get(
                    "Connect this text to the localization table. When the player switches language, the text auto-updates. Leave empty if the text doesn't need translation.",
                    "Связывает этот текст с таблицей локализации. При смене языка игроком текст обновится сам. Оставь пустым, если переводить не нужно."));
                DrawKeyPickerRow(
                    binding.LocalizationKey,
                    newKey => { binding.LocalizationKey = newKey; EditorUtility.SetDirty(binding); binding.Refresh(); });

                GUILayout.Space(6);
                GUILayout.Label(ToolLang.Get("Variable (substitutes {var})", "Переменная (вместо {var})"), EditorStyles.miniBoldLabel);
                DrawFieldHint(ToolLang.Get(
                    "Inserts a variable's value into the text. Write \"{var}\" inside the localized string and pick a variable here — at runtime \"{var}\" gets replaced with its current value. Example: \"HP: {var}\" + variable \"PlayerHP\" → \"HP: 42\".",
                    "Подставляет значение переменной в текст. Вставь «{var}» внутрь локализованной строки и выбери переменную здесь — в рантайме «{var}» заменится на её текущее значение. Пример: «HP: {var}» + переменная «PlayerHP» → «HP: 42»."));
                DrawVarPickerRow(
                    binding.BoundVariable,
                    newVar => { binding.BoundVariable = newVar; EditorUtility.SetDirty(binding); binding.Refresh(); });

                bool hasButton = binding.GetComponent<UnityEngine.UI.Button>() != null;
                if (hasButton)
                {
                    GUILayout.Space(8);
                    GUILayout.Label(ToolLang.Get("Click action", "По клику"), EditorStyles.miniBoldLabel);
                    DrawFieldHint(ToolLang.Get(
                        "What the button does when player clicks it. Can be a sequence of actions executed top-to-bottom with optional delays.",
                        "Что произойдёт когда игрок кликнет по кнопке. Может быть последовательность действий — выполняются сверху вниз с опциональными задержками."));
                    DrawClickActionEditor(binding);
                }

                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                if (NovellaSettingsModule.NeutralButton(ToolLang.Get("Remove link", "Убрать связь"), GUILayout.Height(22)))
                {
                    if (UnityEditor.EditorUtility.DisplayDialog(
                            ToolLang.Get("Remove binding?", "Убрать связь?"),
                            ToolLang.Get("All graph nodes referring to this element will lose the link. Proceed?",
                                         "Все ноды графа ссылающиеся на этот элемент потеряют связь. Продолжить?"),
                            ToolLang.Get("Remove", "Убрать"),
                            ToolLang.Get("Cancel", "Отмена")))
                    {
                        UnityEditor.Undo.DestroyObjectImmediate(binding);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
        }

        // Редактор «что делает кнопка по клику» — список шагов (ClickSequence).
        // Каждый шаг = карточка с заголовком (action picker), ↑↓✖ кнопками,
        // delay-полем и контекстным редактором параметров. Внизу — «+ Добавить шаг».
        // Все шаги выполняются сверху вниз с задержкой DelayBefore у каждого.
        private static void DrawClickActionEditor(NovellaEngine.Runtime.UI.NovellaUIBinding b)
        {
            if (b.ClickSequence == null) b.ClickSequence = new System.Collections.Generic.List<NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep>();

            // Если sequence пустая — миграция legacy single-action.
            if (b.ClickSequence.Count == 0 && b.ClickAction != NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None)
            {
                b.ClickSequence.Add(new NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep
                {
                    Action = b.ClickAction,
                    OnClickGotoNodeId = b.OnClickGotoNodeId,
                    StoryToStart = b.StoryToStart,
                    TargetBindingId = b.TargetBindingId,
                    LanguageCode = b.LanguageCode,
                    URL = b.URL,
                });
                b.ClickAction = NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None;
                EditorUtility.SetDirty(b);
            }

            // Знаем ли мы что предыдущий шаг — терминальный (сменит сцену и т.п.).
            bool sceneEnded = false;

            for (int i = 0; i < b.ClickSequence.Count; i++)
            {
                var step = b.ClickSequence[i];
                int stepIndex = i;
                DrawStepCard(b, step, stepIndex, sceneEnded);
                if (NovellaEngine.Runtime.UI.NovellaUIBinding.IsTerminalAction(step.Action)) sceneEnded = true;
                GUILayout.Space(6);
            }

            GUILayout.Space(2);
            // «+ Добавить действие»
            if (NovellaSettingsModule.NeutralButton("➕  " + ToolLang.Get("Add action", "Добавить действие"), GUILayout.Height(28)))
            {
                NovellaEngine.Editor.UIBindings.NovellaActionPickerWindow.Open(NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None, picked =>
                {
                    if (b == null) return;
                    if (picked == NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None) return;
                    b.ClickSequence.Add(new NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep { Action = picked });
                    EditorUtility.SetDirty(b);
                });
            }
        }

        private static void DrawStepCard(NovellaEngine.Runtime.UI.NovellaUIBinding b, NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep step, int idx, bool sceneEnded)
        {
            Rect cardStart = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginVertical();

            // Header: №, action picker (растягивается), ↑ ↓ ✖
            GUILayout.BeginHorizontal();

            var numSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 10 };
            numSt.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label("#" + (idx + 1), numSt, GUILayout.Width(26));

            // Action picker — кнопка с текущим действием.
            var (icon, name) = ActionIconAndName(step.Action);
            string label = step.Action == NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None
                ? "—  " + ToolLang.Get("(pick an action)", "(выбери действие)")
                : $"{icon}    {name}    ▾";

            Color prevBg = GUI.backgroundColor;
            if (step.Action != NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None)
                GUI.backgroundColor = new Color(NovellaSettingsModule.GetAccentColor().r, NovellaSettingsModule.GetAccentColor().g, NovellaSettingsModule.GetAccentColor().b, 0.45f);
            var btnSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4) };
            if (GUILayout.Button(label, btnSt, GUILayout.Height(26)))
            {
                NovellaEngine.Editor.UIBindings.NovellaActionPickerWindow.Open(step.Action, picked =>
                {
                    step.Action = picked;
                    EditorUtility.SetDirty(b);
                });
            }
            GUI.backgroundColor = prevBg;

            using (new EditorGUI.DisabledScope(idx == 0))
                if (GUILayout.Button("↑", GUILayout.Width(22), GUILayout.Height(26)))
                {
                    var t = b.ClickSequence[idx]; b.ClickSequence[idx] = b.ClickSequence[idx - 1]; b.ClickSequence[idx - 1] = t;
                    EditorUtility.SetDirty(b);
                }
            using (new EditorGUI.DisabledScope(idx == b.ClickSequence.Count - 1))
                if (GUILayout.Button("↓", GUILayout.Width(22), GUILayout.Height(26)))
                {
                    var t = b.ClickSequence[idx]; b.ClickSequence[idx] = b.ClickSequence[idx + 1]; b.ClickSequence[idx + 1] = t;
                    EditorUtility.SetDirty(b);
                }
            if (GUILayout.Button("✖", GUILayout.Width(22), GUILayout.Height(26)))
            {
                b.ClickSequence.RemoveAt(idx);
                EditorUtility.SetDirty(b);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(8);
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();

            // Описание действия + warning если предыдущий шаг закроет сцену.
            if (step.Action != NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None)
            {
                var descSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
                descSt.normal.textColor = NovellaSettingsModule.GetTextMuted();
                GUILayout.Label(ActionDescription(step.Action), descSt);
            }
            if (sceneEnded)
            {
                var wSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
                wSt.normal.textColor = new Color(0.95f, 0.66f, 0.30f);
                GUILayout.Label("⚠  " + ToolLang.Get("Previous step closes the scene — this action probably won't run.",
                                                     "Предыдущий шаг закрывает сцену — этот шаг скорее всего не выполнится."), wSt);
            }

            GUILayout.Space(4);

            // Параметр(ы) для конкретного действия — те же поля что были в legacy editor'е,
            // но теперь читаются/пишутся в step.X.
            DrawStepParameters(b, step);

            // Задержка — мини-плашка внизу шага с коротким лейблом и числовым полем.
            // Текст один-в-строку, без переносов, чтобы у двух соседних шагов
            // оставался одинаковый размер и ничего не съезжало за рамку.
            if (step.Action != NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None)
            {
                GUILayout.Space(4);
                Rect delayRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(delayRect, new Color(NovellaSettingsModule.GetAccentColor().r, NovellaSettingsModule.GetAccentColor().g, NovellaSettingsModule.GetAccentColor().b, 0.06f));

                string when = idx == 0
                    ? ToolLang.Get("⏱ Wait before start",        "⏱ Подождать перед запуском")
                    : ToolLang.Get("⏱ Wait after previous step", "⏱ Подождать после прошлого шага");
                string tooltip = idx == 0
                    ? ToolLang.Get("How many seconds to wait AFTER the click, before running this step.",
                                   "Сколько секунд ждать ПОСЛЕ клика, прежде чем выполнить этот шаг.")
                    : ToolLang.Get("How many seconds to wait AFTER the previous step finishes, before running this step.",
                                   "Сколько секунд ждать ПОСЛЕ окончания предыдущего шага, прежде чем выполнить этот шаг.");

                var lblSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                lblSt.normal.textColor = NovellaSettingsModule.GetTextMuted();
                GUI.Label(new Rect(delayRect.x + 8, delayRect.y, delayRect.width - 96, delayRect.height),
                          new GUIContent(when, tooltip), lblSt);

                Rect numRect = new Rect(delayRect.xMax - 86, delayRect.y + 4, 50, 20);
                float newDelay = EditorGUI.FloatField(numRect, step.DelayBefore);
                if (Mathf.Abs(newDelay - step.DelayBefore) > 0.0001f) { step.DelayBefore = Mathf.Max(0f, newDelay); EditorUtility.SetDirty(b); }
                var sSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
                sSt.normal.textColor = NovellaSettingsModule.GetTextMuted();
                GUI.Label(new Rect(delayRect.xMax - 32, delayRect.y, 30, delayRect.height), "сек", sSt);
            }

            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            Rect cardEnd = GUILayoutUtility.GetLastRect();
            Rect cardRect = new Rect(cardStart.x, cardStart.y, cardStart.width, cardEnd.yMax - cardStart.y);
            DrawRectBorder(cardRect, new Color(NovellaSettingsModule.GetAccentColor().r, NovellaSettingsModule.GetAccentColor().g, NovellaSettingsModule.GetAccentColor().b, 0.4f));
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 3, cardRect.height), NovellaSettingsModule.GetAccentColor());
        }

        private static void DrawStepParameters(NovellaEngine.Runtime.UI.NovellaUIBinding b, NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep step)
        {
            switch (step.Action)
            {
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.GoToNode:
                    DrawNodePickerRow(step.OnClickGotoNodeId,
                        v => { step.OnClickGotoNodeId = v; EditorUtility.SetDirty(b); });
                    break;

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.StartNewGame:
                    DrawStoryPickerRow(step.StoryToStart,
                        v => { step.StoryToStart = v; EditorUtility.SetDirty(b); });
                    break;

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ShowPanel:
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.HidePanel:
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TogglePanel:
                    NovellaEngine.Editor.UIBindings.UIBindingFieldGUI.Draw(
                        ToolLang.Get("Target UI element", "Целевой UI элемент"),
                        step.TargetBindingId,
                        NovellaEngine.Runtime.UI.UIBindingKind.Any,
                        v => { step.TargetBindingId = v; EditorUtility.SetDirty(b); });
                    break;

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ChangeLanguage:
                    DrawLanguagePickerRow(step.LanguageCode,
                        v => { step.LanguageCode = v; EditorUtility.SetDirty(b); });
                    break;

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.OpenURL:
                {
                    string newUrl = EditorGUILayout.TextField(step.URL ?? "");
                    if (newUrl != step.URL) { step.URL = newUrl; EditorUtility.SetDirty(b); }
                    break;
                }

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.SetVariable:
                    DrawSetVariableEditor(b, step);
                    break;

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TriggerEvent:
                {
                    var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
                    hint.normal.textColor = NovellaSettingsModule.GetTextMuted();
                    GUILayout.Label("👨‍💻 " + ToolLang.Get("For coders: subscribe to NovellaPlayer.OnNovellaEvent in C# to react.",
                                                            "Для кодеров: подпишись на NovellaPlayer.OnNovellaEvent в C# чтобы обработать."), hint);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Event name", "Имя"), GUILayout.Width(80));
                    string en = EditorGUILayout.TextField(step.EventName ?? "");
                    if (en != step.EventName) { step.EventName = en; EditorUtility.SetDirty(b); }
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Param", "Параметр"), GUILayout.Width(80));
                    string ep = EditorGUILayout.TextField(step.EventParam ?? "");
                    if (ep != step.EventParam) { step.EventParam = ep; EditorUtility.SetDirty(b); }
                    GUILayout.EndHorizontal();
                    break;
                }

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.PlaySFX:
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Sound", "Звук"), GUILayout.Width(80));
                    string clipName = step.SfxClip != null ? step.SfxClip.name : ToolLang.Get("— pick from gallery —", "— выбрать из галереи —");
                    if (GUILayout.Button("🎵  " + clipName, EditorStyles.popup))
                    {
                        NovellaGalleryWindow.ShowWindow(obj =>
                        {
                            if (obj is AudioClip clip) { step.SfxClip = clip; EditorUtility.SetDirty(b); }
                        }, NovellaGalleryWindow.EGalleryFilter.Audio);
                    }
                    using (new EditorGUI.DisabledScope(step.SfxClip == null))
                    {
                        if (GUILayout.Button("✖", GUILayout.Width(22))) { step.SfxClip = null; EditorUtility.SetDirty(b); }
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Volume", "Громкость"), GUILayout.Width(80));
                    float nv = EditorGUILayout.Slider(step.SfxVolume, 0f, 1f);
                    if (Mathf.Abs(nv - step.SfxVolume) > 0.0001f) { step.SfxVolume = nv; EditorUtility.SetDirty(b); }
                    GUILayout.EndHorizontal();
                    break;
                }

                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.UnlockAchievement:
                {
                    var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
                    hint.normal.textColor = NovellaSettingsModule.GetTextMuted();
                    GUILayout.Label("👨‍💻 " + ToolLang.Get("For coders: integrate Steam/Google Play via NovellaPlayer.OnNovellaEvent (event = 'Achievement.Unlock').",
                                                            "Для кодеров: подключи Steam/Google Play через NovellaPlayer.OnNovellaEvent (event = 'Achievement.Unlock')."), hint);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ToolLang.Get("Achievement ID", "ID ачивки"), GUILayout.Width(120));
                    string ai = EditorGUILayout.TextField(step.AchievementId ?? "");
                    if (ai != step.AchievementId) { step.AchievementId = ai; EditorUtility.SetDirty(b); }
                    GUILayout.EndHorizontal();
                    break;
                }
            }
        }

        // Редактор для SetVariable: пикер имени переменной → потом поле под её тип.
        private static void DrawSetVariableEditor(NovellaEngine.Runtime.UI.NovellaUIBinding b, NovellaEngine.Runtime.UI.NovellaUIBinding.ClickActionStep step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Variable", "Переменная"), GUILayout.Width(100));
            string current = step.VariableName ?? "";
            if (GUILayout.Button(string.IsNullOrEmpty(current) ? ToolLang.Get("— pick a variable —", "— выбери переменную —") : "🔧  " + current, EditorStyles.popup))
            {
                NovellaEngine.Editor.UIBindings.NovellaVariablePickerWindow.Open(current, picked =>
                {
                    if (b == null || step == null) return;
                    step.VariableName = picked;
                    EditorUtility.SetDirty(b);
                });
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(current)))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22))) { step.VariableName = ""; EditorUtility.SetDirty(b); }
            }
            GUILayout.EndHorizontal();

            // Тип переменной — показываем поле под её Type из настроек.
            if (string.IsNullOrEmpty(step.VariableName)) return;
            var def = GetVariableDef(step.VariableName);
            if (def == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Set to", "Значение"), GUILayout.Width(100));
            switch (def.Type)
            {
                case NovellaEngine.Data.EVarType.Integer:
                    int ni = EditorGUILayout.IntField(step.VariableInt);
                    if (ni != step.VariableInt) { step.VariableInt = ni; EditorUtility.SetDirty(b); }
                    break;
                case NovellaEngine.Data.EVarType.Boolean:
                    bool nb = EditorGUILayout.Toggle(step.VariableBool);
                    if (nb != step.VariableBool) { step.VariableBool = nb; EditorUtility.SetDirty(b); }
                    break;
                case NovellaEngine.Data.EVarType.String:
                    string ns = EditorGUILayout.TextField(step.VariableString ?? "");
                    if (ns != step.VariableString) { step.VariableString = ns; EditorUtility.SetDirty(b); }
                    break;
            }
            GUILayout.EndHorizontal();
        }

        private static NovellaEngine.Data.VariableDefinition GetVariableDef(string name)
        {
            var settings = Resources.Load<NovellaEngine.Data.NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null || settings.Variables == null) return null;
            return settings.Variables.Find(v => v.Name == name);
        }

        private static (string icon, string name) ActionIconAndName(NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction a)
        {
            switch (a)
            {
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None:              return ("—",  ToolLang.Get("No action",            "Без действия"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.GoToNode:          return ("🎯", ToolLang.Get("Go to graph node",     "Перейти к ноде графа"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.StartNewGame:      return ("▶",  ToolLang.Get("Start new game",       "Начать новую игру"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.LoadLastSave:      return ("📥", ToolLang.Get("Load last save",       "Загрузить сохранение"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.RestartChapter:    return ("↻",  ToolLang.Get("Restart chapter",      "Перезапустить главу"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.QuitGame:          return ("🚪", ToolLang.Get("Quit game",             "Выйти из игры"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ShowPanel:         return ("👁", ToolLang.Get("Show UI element",      "Показать UI элемент"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.HidePanel:         return ("🚫", ToolLang.Get("Hide UI element",      "Скрыть UI элемент"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TogglePanel:       return ("🔁", ToolLang.Get("Toggle UI element",    "Переключить UI элемент"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.SetVariable:       return ("🔧", ToolLang.Get("Set variable",         "Установить переменную"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TriggerEvent:      return ("📡", ToolLang.Get("Trigger event",        "Послать событие"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.UnlockAchievement: return ("🏆", ToolLang.Get("Unlock achievement",   "Разблокировать ачивку"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.PlaySFX:           return ("🎵", ToolLang.Get("Play SFX",             "Проиграть звук"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ChangeLanguage:    return ("🌐", ToolLang.Get("Change language",      "Сменить язык"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.OpenURL:           return ("🔗", ToolLang.Get("Open URL",             "Открыть ссылку"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.PauseGame:         return ("⏸",  ToolLang.Get("Pause game",           "Пауза игры"));
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ResumeGame:        return ("▶",  ToolLang.Get("Resume game",          "Снять паузу"));
            }
            return ("?", a.ToString());
        }

        private static string ActionDescription(NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction a)
        {
            switch (a)
            {
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.None:              return ToolLang.Get("Click does nothing.", "Клик ничего не делает.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.RestartChapter:    return ToolLang.Get("Wipes save of the current chapter and restarts it from the root node.", "Стирает сохранение текущей главы и перезапускает её с корневой ноды.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.SetVariable:       return ToolLang.Get("Sets a NovellaVariables value. Useful for like-buttons, name choosers, ...", "Устанавливает значение игровой переменной. Удобно для кнопок «Лайкнуть», «Выбрать имя» и т.п.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TriggerEvent:      return ToolLang.Get("(For coders) Sends a named event to NovellaPlayer.OnNovellaEvent.", "(Для кодеров) Посылает именованное событие в NovellaPlayer.OnNovellaEvent.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.UnlockAchievement: return ToolLang.Get("(For coders) Sends 'Achievement.Unlock' — wire your platform via NovellaPlayer.OnNovellaEvent.", "(Для кодеров) Посылает 'Achievement.Unlock' — подключи платформу через NovellaPlayer.OnNovellaEvent.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.PlaySFX:           return ToolLang.Get("Plays a one-shot sound effect.", "Проигрывает звуковой эффект (одноразово).");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.GoToNode:          return ToolLang.Get("Player jumps to a graph node — for in-dialogue choices and menus.", "Player переходит на ноду графа — для диалоговых выборов и меню.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.StartNewGame:   return ToolLang.Get("Wipes save of the chosen story and launches it from the start.", "Стирает сохранение выбранной истории и запускает её с начала.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.LoadLastSave:   return ToolLang.Get("Reopens the most recently played story from its save.", "Открывает последнюю запущенную историю с её сохранения.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.QuitGame:       return ToolLang.Get("Closes the game (or stops Play Mode in editor).", "Закрывает игру (или останавливает Play Mode в редакторе).");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ShowPanel:      return ToolLang.Get("Activates the chosen UI element.", "Включает (показывает) выбранный UI элемент.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.HidePanel:      return ToolLang.Get("Deactivates the chosen UI element.", "Выключает (скрывает) выбранный UI элемент.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.TogglePanel:    return ToolLang.Get("Toggles visibility of the chosen UI element.", "Переключает видимость выбранного UI элемента.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ChangeLanguage: return ToolLang.Get("Sets the in-game language. All NovellaUIBinding texts and dialogue refresh.", "Меняет язык игры. Все тексты NovellaUIBinding и диалоги обновятся.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.OpenURL:        return ToolLang.Get("Opens the URL in the system browser.", "Открывает URL в системном браузере.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.PauseGame:      return ToolLang.Get("⚠ Sets Time.timeScale = 0 — game freezes. Make sure you wire a «Resume game» button somewhere, otherwise the game looks frozen.", "⚠ Ставит Time.timeScale = 0 — игра «замораживается». Обязательно повесь где-то кнопку «Снять паузу», иначе игра выглядит зависшей.");
                case NovellaEngine.Runtime.UI.NovellaUIBinding.BindingAction.ResumeGame:     return ToolLang.Get("Sets Time.timeScale = 1 — unfreezes the game. Usually wired to «Continue» or close-pause buttons.", "Ставит Time.timeScale = 1 — снимает паузу. Обычно вешается на «Продолжить» или крестик закрытия паузы.");
            }
            return "";
        }

        private static void DrawStoryPickerRow(NovellaEngine.Data.NovellaStory current, System.Action<NovellaEngine.Data.NovellaStory> onChanged)
        {
            GUILayout.BeginHorizontal();
            string label = current != null ? current.name : ToolLang.Get("— pick a story —", "— выбери историю —");
            if (GUILayout.Button(label, EditorStyles.popup))
            {
                var menu = new GenericMenu();
                var guids = AssetDatabase.FindAssets("t:NovellaStory");
                if (guids.Length == 0) menu.AddDisabledItem(new GUIContent(ToolLang.Get("No stories in project", "Нет историй в проекте")));
                else
                {
                    foreach (var g in guids)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g);
                        var s = AssetDatabase.LoadAssetAtPath<NovellaEngine.Data.NovellaStory>(p);
                        if (s == null) continue;
                        var sRef = s;
                        menu.AddItem(new GUIContent(s.name), s == current, () => onChanged(sRef));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent(ToolLang.Get("(clear)", "(очистить)")), false, () => onChanged(null));
                }
                menu.ShowAsContext();
            }
            using (new EditorGUI.DisabledScope(current == null))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22))) onChanged(null);
            }
            GUILayout.EndHorizontal();
        }

        // Дружелюбный picker языка: показывает «Русский (RU)» в кнопке, по клику
        // открывается popup со всеми сконфигурированными языками + их именами.
        // Internally хранит код (RU, EN, ...).
        private static void DrawLanguagePickerRow(string current, System.Action<string> onChanged)
        {
            GUILayout.BeginHorizontal();

            string label = string.IsNullOrEmpty(current)
                ? ToolLang.Get("— pick a language —", "— выбери язык —")
                : LanguageDisplayName(current);

            if (GUILayout.Button(label, EditorStyles.popup))
            {
                var menu = new GenericMenu();
                var settings = NovellaEngine.Data.NovellaLocalizationSettings.GetOrCreateSettings();
                if (settings == null || settings.Languages == null || settings.Languages.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent(ToolLang.Get("No languages configured — open Settings → UI Localization", "Языки не настроены — открой Настройки → Локализация UI")));
                }
                else
                {
                    foreach (var lang in settings.Languages)
                    {
                        if (string.IsNullOrEmpty(lang)) continue;
                        string code = lang;
                        menu.AddItem(new GUIContent(LanguageDisplayName(code)), code == current, () => onChanged(code));
                    }
                }
                menu.ShowAsContext();
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(current)))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22))) onChanged("");
            }
            GUILayout.EndHorizontal();
        }

        // Имя языка для UI: «Русский (RU)». Код в скобках чтобы пользователь
        // видел и читаемое имя, и техническую сторону.
        private static string LanguageDisplayName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            string name = code switch
            {
                "EN" => "English",
                "RU" => "Русский",
                "ES" => "Español",
                "FR" => "Français",
                "DE" => "Deutsch",
                "ZH" => "中文",
                "JA" => "日本語",
                "PT" => "Português",
                "IT" => "Italiano",
                "PL" => "Polski",
                "TR" => "Türkçe",
                "KO" => "한국어",
                "AR" => "العربية",
                _ => null
            };
            return name != null ? $"{name} ({code})" : code;
        }

        // «💡»-плашка под лейблом поля — объясняет рядовому пользователю
        // зачем это поле и как им пользоваться. Показывается только когда
        // включены Подсказки. Шрифт fontSize=11 — читаемо, не «мелкий стир».
        private static void DrawFieldHint(string text)
        {
            if (!NovellaSettingsModule.ShowGuide) return;
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st);
            EditorGUI.DrawRect(r, new Color(NovellaSettingsModule.GetAccentColor().r, NovellaSettingsModule.GetAccentColor().g, NovellaSettingsModule.GetAccentColor().b, 0.07f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), NovellaSettingsModule.GetAccentColor());
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(2);
        }

        // Поле «ключ + кнопка пикера» для локализации. Кнопка открывает
        // визуальное окно NovellaLocKeyPickerWindow с группами по категориям.
        private static void DrawKeyPickerRow(string current, System.Action<string> onChanged)
        {
            GUILayout.BeginHorizontal();
            string newCurrent = EditorGUILayout.TextField(current);
            if (newCurrent != current) onChanged(newCurrent);

            if (GUILayout.Button("🔑", GUILayout.Width(28), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                NovellaEngine.Editor.UIBindings.NovellaLocKeyPickerWindow.Open(current, onChanged);
            }
            GUILayout.EndHorizontal();
        }

        // Поле «переменная + кнопка пикера». Текстовое поле остаётся на случай
        // если пользователь хочет ввести имя руками; кнопка 📊 открывает
        // визуальный Variable Picker Window вместо плоского popup-меню.
        private static void DrawVarPickerRow(string current, System.Action<string> onChanged)
        {
            GUILayout.BeginHorizontal();
            string newCurrent = EditorGUILayout.TextField(current);
            if (newCurrent != current) onChanged(newCurrent);

            if (GUILayout.Button("📊", GUILayout.Width(28), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                NovellaEngine.Editor.UIBindings.NovellaVariablePickerWindow.Open(current, onChanged);
            }
            GUILayout.EndHorizontal();
        }

        // Поле «нода + кнопка пикера» — для OnClickGoto.
        private static void DrawNodePickerRow(string current, System.Action<string> onChanged)
        {
            GUILayout.BeginHorizontal();

            // Покажем имя выбранной ноды если найдём.
            string label = current;
            string activeStoryGuid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            NovellaEngine.Data.NovellaTree tree = null;
            if (!string.IsNullOrEmpty(activeStoryGuid))
            {
                var p = AssetDatabase.GUIDToAssetPath(activeStoryGuid);
                var story = AssetDatabase.LoadAssetAtPath<NovellaEngine.Data.NovellaStory>(p);
                if (story != null) tree = story.StartingChapter;
            }
            if (tree != null && !string.IsNullOrEmpty(current))
            {
                var node = tree.Nodes.Find(n => n != null && n.NodeID == current);
                if (node != null) label = string.IsNullOrEmpty(node.NodeTitle) ? node.NodeType.ToString() : node.NodeTitle;
            }
            if (string.IsNullOrEmpty(label)) label = ToolLang.Get("— pick a node —", "— выбери ноду —");

            if (GUILayout.Button(label, EditorStyles.popup, GUILayout.ExpandWidth(true)))
            {
                var menu = new GenericMenu();
                if (tree == null || tree.Nodes == null || tree.Nodes.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent(ToolLang.Get("No active story / nodes", "Активная история не выбрана / нет нод")));
                }
                else
                {
                    foreach (var n in tree.Nodes)
                    {
                        if (n == null || string.IsNullOrEmpty(n.NodeID)) continue;
                        string title = string.IsNullOrEmpty(n.NodeTitle) ? n.NodeType.ToString() : n.NodeTitle;
                        string item = $"{n.NodeType}/{title}";
                        string nid = n.NodeID;
                        menu.AddItem(new GUIContent(item), nid == current, () => onChanged(nid));
                    }
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent(ToolLang.Get("(clear)", "(очистить)")), false, () => onChanged(""));
                }
                menu.ShowAsContext();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(current)))
            {
                if (GUILayout.Button("✖", GUILayout.Width(22))) onChanged("");
            }
            GUILayout.EndHorizontal();
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

            // Содержимое текста — пишется прямо в TMP_Text.text. Если на binding'е
            // у этого элемента стоит LocalizationKey, эта строка будет переписана
            // при старте сцены / смене языка. Предупреждение показываем явно.
            var binding = FirstSelected.GetComponent<NovellaEngine.Runtime.UI.NovellaUIBinding>();
            bool hasLocKey = binding != null && !string.IsNullOrEmpty(binding.LocalizationKey);

            GUILayout.Label(ToolLang.Get("Text", "Текст"), EditorStyles.miniBoldLabel);

            if (!hasLocKey)
            {
                DrawFieldHint(ToolLang.Get(
                    "What's actually written in the text element. Use this for static texts that don't need translation. For dialogues or multilingual UI use the localization key in 'LINK TO STORY' below.",
                    "Сам текст элемента. Используй это поле для статичных текстов, которые не нужно переводить на другие языки. Для диалогов или мультиязычного UI — задавай ключ локализации в секции «СВЯЗАТЬ С ИСТОРИЕЙ» ниже."));

                EditorGUI.BeginChangeCheck();
                string newText = EditorGUILayout.TextArea(firstTxt.text ?? "", GUILayout.MinHeight(48));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var rt in _selectedList)
                    {
                        var txt = rt.GetComponent<TMP_Text>();
                        if (txt != null) { Undo.RecordObject(txt, "Edit Text"); txt.text = newText; EditorUtility.SetDirty(txt); }
                    }
                }
            }
            else
            {
                // Loc-key стоит — текст НЕ редактируется отсюда. Вместо TextArea
                // показываем превью переводов на всех языках + кнопку «В редактор».
                DrawLocKeyPreview(binding.LocalizationKey);
            }

            GUILayout.Space(8);

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
            // Если включён Auto-Resize, фиксированный размер игнорируется TMP'ом —
            // делаем поле disabled чтобы юзер не путался.
            using (new EditorGUI.DisabledScope(firstTxt.enableAutoSizing))
            {
                float fs = EditorGUILayout.FloatField(firstTxt.fontSize);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var rt in _selectedList)
                    {
                        var txt = rt.GetComponent<TMP_Text>();
                        if (txt != null) { Undo.RecordObject(txt, "Font Size"); txt.fontSize = fs; EditorUtility.SetDirty(txt); }
                    }
                }
            }
            GUILayout.EndHorizontal();

            // Auto-Resize — TMP сам подбирает размер шрифта в диапазоне [min..max]
            // чтобы текст влез в RectTransform. Удобно для динамических подписей
            // (имя персонажа, локализация, переменные {var}).
            DrawFieldHint(ToolLang.Get(
                "Auto-Resize: TMP picks font size automatically between Min and Max so the text fits the box. Useful for dynamic content (character names, translations, {var}-substitutions). When ON, the «Font Size» field is ignored.",
                "Авто-размер: TMP сам подбирает размер шрифта в диапазоне Мин..Макс, чтобы текст помещался в рамку. Удобно для динамики (имя персонажа, переводы, {var}-подстановки). Когда ВКЛ, поле «Размер» игнорируется."));
            GUILayout.BeginHorizontal();
            // Подпись короче чем «Авто-размер» (70px не хватало под «Авто-разме…»),
            // полный смысл всё равно в подсказке выше.
            GUILayout.Label(ToolLang.Get("Auto", "Авто"), GUILayout.Width(72));
            EditorGUI.BeginChangeCheck();
            bool autoResize = EditorGUILayout.Toggle(firstTxt.enableAutoSizing, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var txt = rt.GetComponent<TMP_Text>();
                    if (txt != null) { Undo.RecordObject(txt, "Auto-Resize"); txt.enableAutoSizing = autoResize; EditorUtility.SetDirty(txt); }
                }
            }
            // При включённом auto-resize рисуем компактные поля Min/Max справа.
            if (firstTxt.enableAutoSizing)
            {
                GUILayout.Space(6);
                GUILayout.Label(ToolLang.Get("Min", "Мин"), GUILayout.Width(28));
                EditorGUI.BeginChangeCheck();
                float minFs = EditorGUILayout.FloatField(firstTxt.fontSizeMin, GUILayout.Width(46));
                if (EditorGUI.EndChangeCheck())
                {
                    if (minFs < 1f) minFs = 1f;
                    foreach (var rt in _selectedList)
                    {
                        var txt = rt.GetComponent<TMP_Text>();
                        if (txt != null) { Undo.RecordObject(txt, "Auto-Resize Min"); txt.fontSizeMin = minFs; EditorUtility.SetDirty(txt); }
                    }
                }
                GUILayout.Space(4);
                GUILayout.Label(ToolLang.Get("Max", "Макс"), GUILayout.Width(32));
                EditorGUI.BeginChangeCheck();
                float maxFs = EditorGUILayout.FloatField(firstTxt.fontSizeMax, GUILayout.Width(46));
                if (EditorGUI.EndChangeCheck())
                {
                    if (maxFs < firstTxt.fontSizeMin) maxFs = firstTxt.fontSizeMin;
                    foreach (var rt in _selectedList)
                    {
                        var txt = rt.GetComponent<TMP_Text>();
                        if (txt != null) { Undo.RecordObject(txt, "Auto-Resize Max"); txt.fontSizeMax = maxFs; EditorUtility.SetDirty(txt); }
                    }
                }
            }
            GUILayout.FlexibleSpace();
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

            GUILayout.Space(6);

            // Стилевые кнопки в стиле Word: B  I  R . Каждая — иконка-toggle с тултипом.
            // Заменяет три отдельных Toggle-строки одной компактной плашкой.
            DrawTextStyleToggles(firstTxt);

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        // Превью переводов на всех языках для заданного ключа локализации.
        // Заменяет TextArea когда у binding'а стоит LocalizationKey — текст
        // редактируется только в таблице локализации, не здесь.
        private void DrawLocKeyPreview(string locKey)
        {
            var table = NovellaEngine.Editor.NovellaUILocalizationEditor.GetOrCreateTable();
            var settings = NovellaEngine.Data.NovellaLocalizationSettings.GetOrCreateSettings();
            var entry = table != null ? table.FindEntry(locKey) : null;

            // «Замок» + объяснение.
            var lockSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(8, 8, 6, 6) };
            lockSt.normal.textColor = new Color(0.95f, 0.66f, 0.30f);
            string lockMsg = "🔒  " + ToolLang.Get(
                "Text is managed by localization key '{0}'. Edit translations in the localization table.",
                "Текст управляется ключом локализации «{0}». Редактируй переводы в таблице локализации.");
            lockMsg = lockMsg.Replace("{0}", locKey);
            Rect lr = GUILayoutUtility.GetRect(new GUIContent(lockMsg), lockSt);
            EditorGUI.DrawRect(lr, new Color(0.95f, 0.66f, 0.30f, 0.10f));
            EditorGUI.DrawRect(new Rect(lr.x, lr.y, 3, lr.height), new Color(0.95f, 0.66f, 0.30f, 0.85f));
            GUI.Label(lr, lockMsg, lockSt);
            GUILayout.Space(4);

            // Список языков с переводами.
            if (settings != null && settings.Languages != null)
            {
                foreach (var lang in settings.Languages)
                {
                    string val = entry != null ? entry.Get(lang) : null;
                    bool missing = string.IsNullOrEmpty(val);

                    GUILayout.BeginHorizontal();
                    var langSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 11 };
                    langSt.normal.textColor = NovellaSettingsModule.GetTextSecondary();
                    GUILayout.Label(lang, langSt, GUILayout.Width(40));

                    var valSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
                    valSt.normal.textColor = missing ? new Color(0.95f, 0.66f, 0.30f) : NovellaSettingsModule.GetTextColor();
                    GUILayout.Label(missing ? "(перевода нет)" : val, valSt, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            // Кнопка «открыть редактор переводов на этом ключе».
            if (NovellaSettingsModule.AccentButton("✏  " + ToolLang.Get("Edit translations", "Редактировать переводы"), GUILayout.Height(26)))
            {
                NovellaEngine.Editor.NovellaUILocalizationEditor.ShowAtKey(locKey);
            }
        }

        // Кнопки-toggle для текстовых стилей: жирный / курсив / Rich-теги.
        // Иконки B I R с активным акцентом — компактнее и понятнее чем три
        // строки checkbox'ов.
        private void DrawTextStyleToggles(TMP_Text firstTxt)
        {
            const float cell = 30f;
            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Style", "Стиль"), GUILayout.Width(72));

            // Bold
            bool isBold = (firstTxt.fontStyle & FontStyles.Bold) != 0;
            if (DrawTextStyleCell(cell, "B", isBold, true,
                ToolLang.Get("Bold — make text bold", "Жирный — выделить текст полужирным")))
            {
                ApplyTextStyleFlag(FontStyles.Bold, !isBold);
            }
            GUILayout.Space(2);

            // Italic
            bool isItalic = (firstTxt.fontStyle & FontStyles.Italic) != 0;
            if (DrawTextStyleCell(cell, "I", isItalic, false, true,
                ToolLang.Get("Italic — slant the text", "Курсив — наклонить текст")))
            {
                ApplyTextStyleFlag(FontStyles.Italic, !isItalic);
            }
            GUILayout.Space(2);

            // Underline
            bool isUnderline = (firstTxt.fontStyle & FontStyles.Underline) != 0;
            if (DrawTextStyleCell(cell, "U", isUnderline, false, false, true,
                ToolLang.Get("Underline — underline the text", "Подчёркнутый — подчеркнуть текст")))
            {
                ApplyTextStyleFlag(FontStyles.Underline, !isUnderline);
            }
            GUILayout.Space(8);

            // Rich Text — отдельная семантика, не fontStyle. Иконка ‹›.
            bool richOn = firstTxt.richText;
            if (DrawTextStyleCell(cell, "‹›", richOn, false, false, false,
                ToolLang.Get("Rich Text — allow <b><color><size> tags inside the text",
                             "Rich-теги — разрешить теги <b><color><size> внутри текста")))
            {
                foreach (var rtSel in _selectedList)
                {
                    var txt = rtSel.GetComponent<TMP_Text>();
                    if (txt != null) { Undo.RecordObject(txt, "Rich Text"); txt.richText = !richOn; EditorUtility.SetDirty(txt); }
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // Рисует одну style-toggle ячейку. Возвращает true если кликнули.
        private bool DrawTextStyleCell(float cell, string letter, bool active, bool bold, string tooltip)
            => DrawTextStyleCell(cell, letter, active, bold, false, false, tooltip);
        private bool DrawTextStyleCell(float cell, string letter, bool active, bool bold, bool italic, string tooltip)
            => DrawTextStyleCell(cell, letter, active, bold, italic, false, tooltip);
        private bool DrawTextStyleCell(float cell, string letter, bool active, bool bold, bool italic, bool underline, string tooltip)
        {
            Rect r = GUILayoutUtility.GetRect(cell, cell, GUILayout.Width(cell), GUILayout.Height(cell));
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f) : C_BG_PRIMARY;
            if (hover) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, active ? C_ACCENT : C_BORDER);

            FontStyle fs = FontStyle.Normal;
            if (bold) fs |= FontStyle.Bold;
            if (italic) fs |= FontStyle.Italic;
            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter, fontStyle = fs };
            st.normal.textColor = active ? C_ACCENT : C_TEXT_2;
            GUI.Label(r, new GUIContent(letter, tooltip), st);

            if (underline)
            {
                // Лёгкая подчёркивающая полоска под буквой — псевдо-underline.
                Color uc = active ? C_ACCENT : C_TEXT_2;
                EditorGUI.DrawRect(new Rect(r.x + 8, r.y + cell - 8, cell - 16, 1.5f), uc);
            }

            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private void ApplyTextStyleFlag(FontStyles flag, bool enable)
        {
            foreach (var rt in _selectedList)
            {
                var txt = rt.GetComponent<TMP_Text>();
                if (txt == null) continue;
                Undo.RecordObject(txt, "Text Style");
                if (enable) txt.fontStyle |= flag; else txt.fontStyle &= ~flag;
                EditorUtility.SetDirty(txt);
            }
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
            GUILayout.Label(ToolLang.Get("Highlight", "Наведена"), GUILayout.Width(80));
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

            GUILayout.BeginHorizontal();
            GUILayout.Label(ToolLang.Get("Disabled", "Неактивна"), GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            var dc = EditorGUILayout.ColorField(c.disabledColor);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var rt in _selectedList)
                {
                    var btn = rt.GetComponent<Button>();
                    if (btn != null) { var bc = btn.colors; bc.disabledColor = dc; Undo.RecordObject(btn, "Btn Disabled"); btn.colors = bc; EditorUtility.SetDirty(btn); }
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
            // Locked-объекты не дублируем. Если ВСЕ выделены locked — выходим
            // с предупреждением. Если часть locked — дублируем остальные.
            int lockedCount = _selectedList.Count(rt => rt != null && IsLockedTransitive(rt));
            if (lockedCount == _selectedList.Count)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Locked",  "Заблокировано"),
                    ToolLang.Get(
                        "Selected objects are locked (🔒) and cannot be duplicated. Unlock them first.",
                        "Выделенные объекты заблокированы (🔒) — дубль невозможен. Сними замок чтобы продолжить."),
                    "OK");
                return;
            }

            List<RectTransform> newSel = new List<RectTransform>();
            foreach (var rt in _selectedList)
            {
                if (rt == null) continue;
                if (IsLockedTransitive(rt)) continue; // защита замком
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

            // Если ВСЕ выделенные заблокированы — отказываем сразу.
            int lockedCount = _selectedList.Count(rt => rt != null && IsLockedTransitive(rt));
            if (lockedCount == _selectedList.Count)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Locked",  "Заблокировано"),
                    ToolLang.Get(
                        "Selected objects are locked (🔒) and cannot be deleted. Unlock them first.",
                        "Выделенные объекты заблокированы (🔒) — удалить нельзя. Сними замок чтобы продолжить."),
                    "OK");
                return;
            }

            string text = _selectedList.Count == 1
                ? string.Format(ToolLang.Get("Delete '{0}'?", "Удалить «{0}»?"), GetDisplayName(FirstSelected))
                : string.Format(ToolLang.Get("Delete {0} elements?", "Удалить {0} эл.?"), _selectedList.Count);

            // Если в удаляемых объектах есть NovellaPresetMarker — собираем
            // все имена пресетов и ищем «осиротевшие» объекты с теми же
            // именами вне выделения (например StoryLauncher / Player лежат
            // в корне сцены, не внутри Холста). Без этого удаление Холста
            // оставляло бы их пустые компонентами с null-ссылками.
            var presetNames = CollectPresetNamesInside(_selectedList);
            // MainMenu и Gameplay всегда тащат с собой Wardrobe-панель —
            // она логически часть пресета.
            if (presetNames.Contains("MainMenu") || presetNames.Contains("Gameplay"))
            {
                presetNames.Add("Wardrobe");
            }

            var orphaned = FindOrphanedPresetObjects(presetNames, _selectedList);

            if (orphaned.Count > 0)
            {
                var names = string.Join(", ", orphaned.Select(o => o.name));
                text += "\n\n" + string.Format(ToolLang.Get(
                    "Also removes related preset objects: {0}",
                    "Будут удалены связанные объекты пресета: {0}"), names);
            }

            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete element?", "Удалить элемент(ы)?"),
                text,
                ToolLang.Get("Delete", "Удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            // Сначала удаляем выделенное (locked-объекты пропускаем).
            foreach (var rt in _selectedList)
            {
                if (rt == null) continue;
                if (IsLockedTransitive(rt)) continue;
                Undo.DestroyObjectImmediate(rt.gameObject);
            }

            // Затем — связанные «осиротевшие» объекты пресета. Список
            // пересобираем заново на случай если предыдущие удаления
            // уже задели что-то транзитивно.
            if (presetNames.Count > 0)
            {
                var allMarkers = UnityEngine.Object.FindObjectsByType<NovellaEngine.Runtime.NovellaPresetMarker>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var m in allMarkers)
                {
                    if (m == null || string.IsNullOrEmpty(m.PresetName)) continue;
                    if (!presetNames.Contains(m.PresetName)) continue;
                    Undo.DestroyObjectImmediate(m.gameObject);
                }
            }

            _selectedList.Clear();
            RefreshRectsCache();
        }

        // Собирает уникальные имена пресетов из всех NovellaPresetMarker
        // внутри переданных объектов (включая потомков).
        private static HashSet<string> CollectPresetNamesInside(List<RectTransform> selection)
        {
            var result = new HashSet<string>();
            foreach (var rt in selection)
            {
                if (rt == null) continue;
                var markers = rt.GetComponentsInChildren<NovellaEngine.Runtime.NovellaPresetMarker>(true);
                foreach (var m in markers)
                {
                    if (m != null && !string.IsNullOrEmpty(m.PresetName))
                        result.Add(m.PresetName);
                }
            }
            return result;
        }

        // Возвращает GameObject'ы с NovellaPresetMarker'ом из заданных
        // пресет-имён, которые НЕ являются потомками выделенных объектов.
        // Это и есть «осиротевшие» элементы — их надо удалить вместе с канвасом.
        private static List<GameObject> FindOrphanedPresetObjects(HashSet<string> presetNames, List<RectTransform> selection)
        {
            var result = new List<GameObject>();
            if (presetNames == null || presetNames.Count == 0) return result;

            var allMarkers = UnityEngine.Object.FindObjectsByType<NovellaEngine.Runtime.NovellaPresetMarker>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var m in allMarkers)
            {
                if (m == null || string.IsNullOrEmpty(m.PresetName)) continue;
                if (!presetNames.Contains(m.PresetName)) continue;

                bool insideSelection = false;
                foreach (var rt in selection)
                {
                    if (rt == null) continue;
                    if (m.transform == rt || m.transform.IsChildOf(rt))
                    {
                        insideSelection = true; break;
                    }
                }
                if (!insideSelection) result.Add(m.gameObject);
            }
            return result;
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
            if (!NovellaSettingsModule.ShowGuide) return;
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
                        "Sprite — the picture itself. Click the field and pick from the project gallery.\nColor — tints the image (pure white means no tint).\n\nThe 'Type' dropdown controls how the sprite stretches:\n  • Simple — draws the picture 1:1, as-is\n  • Sliced — '9-slice' with fixed corners (perfect for buttons and panels — the corners stay sharp while the middle stretches)\n  • Tiled — repeats the picture to fill the area (good for textures / patterns)\n  • Filled — partial fill (HP bar / progress; control via the Fill slider)",
                        "Спрайт — сама картинка. Кликни поле и выбери изображение из галереи проекта.\nЦвет — окрашивает картинку (полностью белый = без оттенка).\n\nВыпадающий список «Тип» решает как картинка будет растягиваться:\n  • Обычный — рисует картинку 1:1, как есть\n  • Нарезанный — «9-slice» с фиксированными углами (идеально для кнопок и панелей — углы остаются чёткими, а середина тянется)\n  • Замощенный — повторяет картинку как плитку (хорошо для текстур / узоров)\n  • Заполненный — частичное заполнение (HP-бар / прогресс; настраивается ползунком «Заполн.»)");
                case "text":
                    return ToolLang.Get(
                        "Here you set only the LOOK: font, size, color, alignment, style (Bold / Italic / Underline / Rich-tags).\n\nThe actual TEXT CONTENT itself comes from one of two places:\n  • Localization key (in 'LINK TO STORY' below) — text auto-translates per language.\n  • Or a graph node — for example a Dialogue line writes into this text element if you target it via 'UI text target' in the dialogue editor.\n\nThe Style buttons (B / I / U / ‹›):\n  • B — Bold\n  • I — Italic\n  • U — Underline\n  • ‹› — Rich-tags (allow <b><color><size> markup inside the text).",
                        "Здесь настраивается только ВНЕШНИЙ ВИД: шрифт, размер, цвет, выравнивание, стиль (Жирный / Курсив / Подчёркивание / Rich-теги).\n\nСам ТЕКСТ берётся из одного из двух мест:\n  • Ключа локализации (см. «СВЯЗАТЬ С ИСТОРИЕЙ» ниже) — текст переводится сам при смене языка.\n  • Или из ноды графа — например реплика диалога пишется в этот текстовый элемент если ты указал его в «UI цель для текста» в редакторе диалогов.\n\nКнопки стиля (B / I / U / ‹›):\n  • B — Жирный\n  • I — Курсив\n  • U — Подчёркнутый\n  • ‹› — Rich-теги (разрешает разметку <b><color><size> внутри текста).");
                case "button":
                    return ToolLang.Get(
                        "A clickable element with four colors:\n• Normal — resting state\n• Highlight — when the mouse hovers over\n• Pressed — at the moment of click\n• Disabled — when the button is set to non-interactable\n\nUntick «Interactable» to make the button non-clickable (it'll be visually shown using the Disabled color).",
                        "Кликабельный элемент с четырьмя состояниями:\n• Обычная — состояние покоя\n• Наведена — когда мышь над кнопкой\n• Нажата — в момент клика мышкой\n• Неактивна — когда кнопка отключена («Активна» снята)\n\nСними галочку «Активна» чтобы кнопка стала неактивной (она будет показана цветом «Неактивна»).");
                case "raycast":
                    return ToolLang.Get(
                        "Decides whether THIS element catches mouse/touch events. Decorative images and panels typically should have it OFF — otherwise they steal clicks meant for buttons placed underneath them.\n\nIf you ever wonder \"why is my button not clickable?\" — likely there's another Graphic on top of it with Raycast Target ON, blocking the input.",
                        "Решает: ловит ли ЭТОТ элемент клики мышью/пальцем. У декоративных картинок и панелей обычно лучше ВЫКЛ — иначе они «съедят» клик предназначенный кнопке которая под ними.\n\nЕсли вдруг кажется «почему моя кнопка не нажимается?» — скорее всего сверху лежит другой Graphic с включённым Raycast Target, который перехватывает ввод.");
                case "bindings":
                    return ToolLang.Get(
                        "Bindings make this UI element controllable from the story graph.\n• Localization key — text auto-updates on language switch.\n• Variable — substitutes a value of a variable into '{var}' inside the text.\n• Click → node — for buttons: clicking jumps the Player to a chosen node.\n\nIn graph nodes (Dialogue, Branch, Wait, Scene Settings) you'll see fields where you can drag this element from scene to target it directly.",
                        "Bindings связывают UI-элемент с графом истории.\n• Ключ локализации — текст обновляется автоматически при смене языка.\n• Переменная — подставляет её значение вместо «{var}» в тексте.\n• Перейти к ноде — для кнопок: клик переводит Player на нужную ноду графа.\n\nВ нодах графа (Диалог, Развилка, Wait, Scene Settings) появятся поля куда этот элемент можно перетащить мышью прямо из сцены.");
                default:
                    return "";
            }
        }
    }
}