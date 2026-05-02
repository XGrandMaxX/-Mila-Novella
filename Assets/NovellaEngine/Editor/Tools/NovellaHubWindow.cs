using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    [InitializeOnLoad]
    internal static class NovellaToolbarButton
    {
        private const string ELEMENT_NAME = "novella-studio-toolbar-btn";
        private const float PULSE_DURATION = 1.6f;
        private const int PULSE_COUNT = 3;

        // Базовый цвет кнопки в Unity-тулбаре — следует за акцентом из Settings.
        // Hover берёт тот же оттенок и слегка осветляет.
        private static Color BaseColor => NovellaSettingsModule.GetAccentColor();
        private static Color HoverColor
        {
            get { var c = BaseColor; return new Color(Mathf.Min(1f, c.r + 0.09f), Mathf.Min(1f, c.g + 0.05f), Mathf.Min(1f, c.b + 0.03f)); }
        }
        private static readonly Color RingColor = new Color(1f, 0.78f, 0.20f);

        private static VisualElement _btn;
        private static VisualElement _ring;
        private static double _flashStart;
        private static bool _hovered;
        private static int _retryCount;

        static NovellaToolbarButton()
        {
            _retryCount = 0;
            EditorApplication.delayCall += Attach;
            // На холодном старте Unity тулбар может пересоздаваться несколько раз —
            // ловим эти моменты и переподключаем кнопку первые ~3 секунды.
            EditorApplication.update += MonitorAttachment;
        }

        private static void MonitorAttachment()
        {
            _retryCount++;
            bool stillAttached = _btn != null && _btn.parent != null;
            if (!stillAttached) Attach();
            if (_retryCount > 180) // ~3s @ 60fps
            {
                EditorApplication.update -= MonitorAttachment;
            }
        }

        private static void Attach()
        {
            var toolbarType = typeof(EditorWindow).Assembly.GetType("UnityEditor.Toolbar");
            if (toolbarType == null) return;

            var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            if (toolbars == null || toolbars.Length == 0) return;

            var rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            if (rootField == null) return;

            var root = rootField.GetValue(toolbars[0]) as VisualElement;
            if (root == null) return;

            var rightZone = root.Q("ToolbarZoneRightAlign");
            if (rightZone == null) return;

            var existing = rightZone.Q(ELEMENT_NAME);
            if (existing != null) existing.RemoveFromHierarchy();

            _btn = new VisualElement { name = ELEMENT_NAME };
            _btn.style.flexDirection = FlexDirection.Row;
            _btn.style.alignItems = Align.Center;
            _btn.style.justifyContent = Justify.Center;
            _btn.style.height = 22;
            _btn.style.minWidth = 96;
            _btn.style.flexShrink = 0;
            _btn.style.flexGrow = 0;
            _btn.style.paddingLeft = 10;
            _btn.style.paddingRight = 10;
            _btn.style.marginLeft = 6;
            _btn.style.marginRight = 6;
            _btn.style.borderTopLeftRadius = 4;
            _btn.style.borderTopRightRadius = 4;
            _btn.style.borderBottomLeftRadius = 4;
            _btn.style.borderBottomRightRadius = 4;
            _btn.style.backgroundColor = BaseColor;
            _btn.tooltip = "Open Novella Studio";

            var label = new Label("🚀 Novella");
            label.style.color = NovellaSettingsModule.GetContrastingText(BaseColor);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            _btn.Add(label);

            // Ping-кольцо: расширяющаяся рамка с затуханием. Сидит ребёнком кнопки,
            // позиционируется абсолютно с отрицательными отступами — чтобы выходить
            // за границы кнопки.
            _ring = new VisualElement();
            _ring.pickingMode = PickingMode.Ignore;
            _ring.style.position = Position.Absolute;
            _ring.style.left = -2;
            _ring.style.right = -2;
            _ring.style.top = -2;
            _ring.style.bottom = -2;
            _ring.style.borderTopWidth = 2;
            _ring.style.borderRightWidth = 2;
            _ring.style.borderBottomWidth = 2;
            _ring.style.borderLeftWidth = 2;
            _ring.style.borderTopColor = RingColor;
            _ring.style.borderRightColor = RingColor;
            _ring.style.borderBottomColor = RingColor;
            _ring.style.borderLeftColor = RingColor;
            _ring.style.borderTopLeftRadius = 6;
            _ring.style.borderTopRightRadius = 6;
            _ring.style.borderBottomLeftRadius = 6;
            _ring.style.borderBottomRightRadius = 6;
            _ring.style.opacity = 0f;
            _btn.Add(_ring);

            _btn.RegisterCallback<MouseEnterEvent>(_ => { _hovered = true; ApplyIdleColor(); });
            _btn.RegisterCallback<MouseLeaveEvent>(_ => { _hovered = false; ApplyIdleColor(); });
            _btn.RegisterCallback<ClickEvent>(_ => NovellaHubWindow.ShowWindow());

            rightZone.Add(_btn);
            rightZone.MarkDirtyRepaint();
            _btn.MarkDirtyRepaint();
        }

        public static void Flash()
        {
            if (_ring == null) Attach();
            if (_ring == null) return;
            _flashStart = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Pulse;
            EditorApplication.update += Pulse;
        }

        private static void Pulse()
        {
            if (_ring == null)
            {
                EditorApplication.update -= Pulse;
                return;
            }
            double elapsed = EditorApplication.timeSinceStartup - _flashStart;
            if (elapsed > PULSE_DURATION)
            {
                _ring.style.opacity = 0f;
                _ring.style.scale = new StyleScale(new Scale(Vector3.one));
                EditorApplication.update -= Pulse;
                return;
            }

            // Цикл одной пульсации: scale 1.0 -> 1.55, opacity 1.0 -> 0.0.
            float globalT = (float)(elapsed / PULSE_DURATION);
            float pulseT = Mathf.Repeat(globalT * PULSE_COUNT, 1f);
            float ease = 1f - Mathf.Pow(1f - pulseT, 2f); // easeOutQuad
            float s = Mathf.Lerp(1f, 1.55f, ease);
            float o = Mathf.Lerp(0.95f, 0f, ease);

            _ring.style.opacity = o;
            _ring.style.scale = new StyleScale(new Scale(new Vector3(s, s, 1f)));
        }

        private static void ApplyIdleColor()
        {
            if (_btn == null) return;
            _btn.style.backgroundColor = _hovered ? HoverColor : BaseColor;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Контракт модулей — НЕ ИЗМЕНЯЕТСЯ. Старые модули (CharacterEditor,
    // VariableEditor, SceneManager, UIEditor) продолжают рисоваться через
    // IMGUI-метод DrawGUI(Rect). Hub оборачивает их в IMGUIContainer внутри
    // нового UI Toolkit-контейнера.
    // ════════════════════════════════════════════════════════════════════

    public interface INovellaStudioModule
    {
        string ModuleName { get; }
        string ModuleIcon { get; }
        void OnEnable(EditorWindow hostWindow);
        void OnDisable();
        void DrawGUI(Rect position);
    }

    /// <summary>
    /// Главное окно Novella Studio. Полностью на UI Toolkit + USS.
    /// Старые модули (IMGUI) продолжают работать через IMGUIContainer.
    /// </summary>
    public class NovellaHubWindow : EditorWindow
    {
        // ─────────────── Public API (для совместимости со старым кодом) ───────────────

        public static NovellaHubWindow Instance { get; private set; }

        // Флаг — анимировать открытие на ближайшем OnEnable.
        // Ставится из ShowWindow и потребляется один раз; на reload’е скриптов не срабатывает.
        private static bool _shouldAnimateOpen;

        public void SwitchToModule(int index)
        {
            if (_modules == null || index < 0 || index >= _modules.Count) return;
            _currentModuleIndex = index;
            SyncModuleStates();
        }

        public INovellaStudioModule GetModule(int index)
            => (index >= 0 && _modules != null && index < _modules.Count) ? _modules[index] : null;

        // ─────────────── Поля ───────────────

        private List<INovellaStudioModule> _modules;
        private int _currentModuleIndex;
        private bool _sidebarCollapsed;

        // UI Toolkit references
        private VisualElement _root;
        private VisualElement _sideEl;
        private VisualElement _mainEl; // правая основная область — нужна для перекраски при смене темы
        private List<VisualElement> _modButtons;
        // Бейджи на кнопке консоли в сайдбаре — три счётчика (logs / warnings / errors).
        // Обновляются по событию NovellaConsoleStore.OnChanged.
        private Label _consoleBadgeLog, _consoleBadgeWarn, _consoleBadgeErr;
        private VisualElement _consoleButton; // ссылка на саму кнопку (для pulse-анимации).
        // Прошлые значения счётчиков — нужны чтобы понять «появилось новое».
        private int _prevLogCount, _prevWarnCount, _prevErrCount;
        private IVisualElementScheduledItem _consolePulseTimer;
        private Label _crumbCurrent;
        private IMGUIContainer _moduleContainer;
        private IMGUIContainer _tutorialOverlay;
        private bool _tutorialOverlayInTree;          // флаг: overlay сейчас в дереве?
        private IVisualElementScheduledItem _tutorialPoll; // 2 Hz-планировщик статуса туториала
        private NovellaCommandPalette _commandPalette;
        private VisualElement _topActions; // Play / + New story — видимы только на Home

        // ─────────────── Menu / Show ───────────────

        [MenuItem("Novella Engine/🚀 Novella Studio (Hub)", false, 0)]
        public static void ShowWindow()
        {
            _shouldAnimateOpen = true;

            var win = GetWindow<NovellaHubWindow>("Novella Studio");
            win.minSize = new Vector2(1100, 700);

            ApplyFullscreenPosition(win);
            win.Show();
            win.Focus();

            // Дубль-страховка: иногда `GetMainWindowPosition()` на первом тике
            // отдаёт неактуальный размер (Unity ещё доинициализирует docking).
            // Повторно прижимаем окно к экрану на следующем кадре.
            EditorApplication.delayCall += () =>
            {
                if (win == null) return;
                ApplyFullscreenPosition(win);
                win.Show();
                win.Focus();
            };
        }

        private static void ApplyFullscreenPosition(NovellaHubWindow win)
        {
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            if (main.width > 200 && main.height > 200)
            {
                float pad = 20f;
                win.position = new Rect(main.x + pad, main.y + pad, main.width - pad * 2f, main.height - pad * 2f);
            }
        }
        // ─────────────── OnEnable / OnDisable ───────────────

        private void OnEnable()
        {
            Instance = this;
            titleContent = new GUIContent("Novella Studio");

            _modules = new List<INovellaStudioModule>
            {
                new DashboardModule(),
                new NovellaCharacterEditorModule(),
                new NovellaSceneManagerModule(),
                new NovellaUIForge(),
                new NovellaVariableEditorModule(),
                new NovellaConsoleModule(),
                new NovellaSettingsModule(),
            };
            foreach (var m in _modules) m.OnEnable(this);

            BuildUI();
            SwitchToModule(_currentModuleIndex);

            EditorApplication.projectChanged += OnProjectChanged;
            NovellaSettingsModule.OnAppearanceChanged += ApplyAppearance;
            ApplyAppearance();

            if (_shouldAnimateOpen)
            {
                _shouldAnimateOpen = false;
                PlayOpenAnimation();
            }
        }

        private void PlayOpenAnimation()
        {
            if (rootVisualElement == null) return;

            // Стартовое состояние: чуть сжато, прозрачно. Без transition — сразу.
            rootVisualElement.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });
            rootVisualElement.style.opacity = 0f;
            rootVisualElement.style.scale = new StyleScale(new Scale(new Vector3(0.94f, 0.94f, 1f)));
            rootVisualElement.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(50f), Length.Percent(50f)));

            // На следующем кадре — включаем transition и поднимаем значения, чтобы UIE плавно довёл.
            rootVisualElement.schedule.Execute(() =>
            {
                if (rootVisualElement == null) return;
                rootVisualElement.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                    new StylePropertyName("opacity"),
                    new StylePropertyName("scale"),
                });
                rootVisualElement.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                    new TimeValue(0.22f, TimeUnit.Second),
                    new TimeValue(0.22f, TimeUnit.Second),
                });
                rootVisualElement.style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction> {
                    new EasingFunction(EasingMode.EaseOutCubic),
                    new EasingFunction(EasingMode.EaseOutCubic),
                });
                rootVisualElement.style.opacity = 1f;
                rootVisualElement.style.scale = new StyleScale(new Scale(Vector3.one));
            }).StartingIn(20);
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
            NovellaSettingsModule.OnAppearanceChanged -= ApplyAppearance;
            NovellaConsoleStore.OnChanged -= OnConsoleStoreChanged;
            _consolePulseTimer?.Pause();
            _consolePulseTimer = null;
            _consoleBadgeLog = _consoleBadgeWarn = _consoleBadgeErr = null;
            _consoleBadgesBox = null;
            _consoleButton = null;
            if (_modules != null) foreach (var m in _modules) m.OnDisable();
            _tutorialPoll?.Pause();
            _tutorialPoll = null;
            if (Instance == this) Instance = null;
        }

        private void OnProjectChanged()
        {
            string activeGuid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            if (!string.IsNullOrEmpty(activeGuid))
            {
                string path = AssetDatabase.GUIDToAssetPath(activeGuid);
                if (string.IsNullOrEmpty(path) ||
                    AssetDatabase.LoadAssetAtPath<NovellaStory>(path) == null)
                {
                    EditorPrefs.DeleteKey("Novella_ActiveStoryGuid");
                }
            }
            RefreshActiveStoryLabel();
        }

        // Стреляет когда пользователь меняет настройки в Settings-модуле.
        // Применяет цвета ко всем UI Toolkit-элементам Hub'а (root, sidebar, main,
        // breadcrumb, кнопки модулей). Дальше — модули сами читают цвета через
        // NovellaSettingsModule на следующем Repaint.
        private void ApplyAppearance()
        {
            Color iface  = NovellaSettingsModule.GetInterfaceColor();
            Color side   = NovellaSettingsModule.GetBgSideColor();
            Color border = NovellaSettingsModule.GetBorderColor();

            if (_root != null)   _root.style.backgroundColor = iface;
            if (_sideEl != null) { _sideEl.style.backgroundColor = side; _sideEl.style.borderRightColor = border; }
            if (_mainEl != null) _mainEl.style.backgroundColor = iface;

            // USS-тема задаёт хардкод-фон у .ns-top / .ns-content / .ns-side__brand
            // (поверх _mainEl). Без inline-перекрытия пользовательский Interface
            // color не доходит до топбара и зоны контента.
            if (_root != null)
            {
                foreach (var e in _root.Query<VisualElement>(className: "ns-top").ToList())
                {
                    e.style.backgroundColor = iface;
                    e.style.borderBottomColor = border;
                }
                foreach (var e in _root.Query<VisualElement>(className: "ns-content").ToList())          e.style.backgroundColor = iface;
                foreach (var e in _root.Query<VisualElement>(className: "ns-side__brand").ToList())      e.style.borderBottomColor = border;
                foreach (var e in _root.Query<VisualElement>(className: "ns-top__collapse").ToList())    e.style.borderRightColor = border;

                // Кнопки топбара (Play / + New story). Outline = текст-как-text, fill = акцент.
                Color accent = NovellaSettingsModule.GetAccentColor();
                Color text   = NovellaSettingsModule.GetTextColor();
                Color onAcc  = NovellaSettingsModule.GetContrastingText(accent);
                foreach (var b in _root.Query<Button>(className: "ns-btn--out").ToList())
                {
                    b.style.color = text;
                    b.style.borderTopColor = text; b.style.borderBottomColor = text;
                    b.style.borderLeftColor = text; b.style.borderRightColor = text;
                }
                foreach (var b in _root.Query<Button>(className: "ns-btn--fill").ToList())
                {
                    b.style.backgroundColor = accent;
                    b.style.color = onAcc;
                    b.style.borderTopColor = accent; b.style.borderBottomColor = accent;
                    b.style.borderLeftColor = accent; b.style.borderRightColor = accent;
                }
                // Tutorial-кнопка в сайдбаре
                foreach (var b in _root.Query<Button>(className: "ns-tutorial-btn").ToList())
                {
                    b.style.backgroundColor = accent;
                    b.style.color = onAcc;
                }
            }

            // Текст в sidebar/topbar изначально окрашен через USS (статичные #ECECF4
            // и подобные). Чтобы он реагировал на смену "Text color" в Settings,
            // прокидываем inline .style.color по UQuery — inline всегда перекрывает USS.
            ApplyDynamicTextColors();

            // Полная перерисовка, чтобы IMGUI-модули и UI Toolkit-стили синхронизировались.
            if (rootVisualElement != null) rootVisualElement.MarkDirtyRepaint();
            Repaint();
        }

        // Прокидывает Text/Accent/Muted цвета на лейблы Hub'а по их USS-классам.
        // Вызывается из ApplyAppearance + после BuildUI, чтобы inline-стили
        // победили USS даже на первом кадре.
        private void ApplyDynamicTextColors()
        {
            if (_root == null) return;

            Color c1 = NovellaSettingsModule.GetTextColor();      // основной (#ECECF4 в USS)
            Color c2 = NovellaSettingsModule.GetTextSecondary();  // вторичный (#B5B7C8)
            Color c3 = NovellaSettingsModule.GetTextMuted();      // приглушённый (#9D9FB0)
            Color c4 = NovellaSettingsModule.GetTextDisabled();   // выключенный (#6D6F80)
            Color acc = NovellaSettingsModule.GetAccentColor();
            Color border = NovellaSettingsModule.GetBorderColor();

            // Sidebar — бренд
            foreach (var e in _root.Query<Label>(className: "ns-side__brand-name").ToList()) e.style.color = c1;
            foreach (var e in _root.Query<Label>(className: "ns-side__brand-sub").ToList())  e.style.color = c3;

            // Sidebar — story picker
            foreach (var e in _root.Query<Label>(className: "ns-story-pick__lbl").ToList())  e.style.color = c3;
            foreach (var e in _root.Query<Label>(className: "ns-story-pick__name").ToList()) e.style.color = c1;
            foreach (var e in _root.Query<Label>(className: "ns-story-pick__arr").ToList())  e.style.color = c3;

            // Sidebar — quick search
            foreach (var e in _root.Query<Label>(className: "ns-search__text").ToList()) e.style.color = c3;
            foreach (var e in _root.Query<Label>(className: "ns-search__kbd").ToList())  e.style.color = c4;

            // Sidebar — категории и кнопки модулей
            foreach (var e in _root.Query<Label>(className: "ns-cat").ToList())        e.style.color = c4;
            foreach (var e in _root.Query<Label>(className: "ns-mod__label").ToList()) e.style.color = c2;

            // Sidebar — help-блок (горячие клавиши)
            foreach (var e in _root.Query<Label>(className: "ns-help__title").ToList())     e.style.color = c3;
            foreach (var e in _root.Query<Label>(className: "ns-help__row-label").ToList()) e.style.color = c2;
            foreach (var e in _root.Query<Label>(className: "ns-help__kbd").ToList())       e.style.color = c4;

            // Topbar — крошки "Novella Studio / [Module]"
            foreach (var e in _root.Query<Label>(className: "ns-crumb__root").ToList()) e.style.color = c3;
            foreach (var e in _root.Query<Label>(className: "ns-crumb__sep").ToList())  e.style.color = border;
            foreach (var e in _root.Query<Label>(className: "ns-crumb__cur").ToList())  e.style.color = c1;

            // Иконки-тинты в sidebar (story-pick, search, mod-icon, crumb-icon, collapse-icon)
            foreach (var e in _root.Query<VisualElement>(className: "ns-story-pick__icon").ToList())   e.style.unityBackgroundImageTintColor = acc;
            foreach (var e in _root.Query<VisualElement>(className: "ns-crumb__icon").ToList())        e.style.unityBackgroundImageTintColor = acc;
            foreach (var e in _root.Query<VisualElement>(className: "ns-side__brand-logo").ToList())
            {
                e.style.borderLeftColor = acc; e.style.borderRightColor = acc;
                e.style.borderTopColor = acc;  e.style.borderBottomColor = acc;
                e.style.backgroundColor = new Color(acc.r, acc.g, acc.b, 0.09f);
            }
            foreach (var e in _root.Query<Label>(className: "ns-side__brand-logo-text").ToList()) e.style.color = acc;
        }

        // Когда юзер возвращается в Hub из Graph-окна, Dashboard должен перечитать времена правки
        // чтобы карточки показывали актуальное "когда последний раз заходили".
        private void OnFocus()
        {
            if (_modules != null && _modules.Count > 0 && _modules[0] is DashboardModule dash)
            {
                dash.RefreshExternal();
            }
            Repaint();
        }

        // ─────────────── BuildUI: вся UI Toolkit-разметка ───────────────

        private void BuildUI()
        {
            _root = rootVisualElement;
            _root.Clear();

            // ─── Загрузка USS-темы ───
            // Если файл не нашёлся (плохой meta, неверный путь и т.д.) — пишем
            // в Console и оставляем inline-fallback. Layout всё равно будет
            // работать благодаря inline-стилям ниже.
            var ussPath = NovellaHubResources.GetThemePath();
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet != null)
            {
                _root.styleSheets.Add(styleSheet);
            }
            else
            {
                Debug.LogWarning($"[Novella Studio] USS-тема не загрузилась по пути '{ussPath}'. " +
                                 "Окно покажется без декоративного оформления (только базовый layout). " +
                                 "Проверь что файл NovellaHubTheme.uss лежит в Assets/NovellaEngine/Editor/Tools/UI/ " +
                                 "и в Project View отображается как StyleSheet (не TextAsset).");
            }

            _root.AddToClassList("ns-root");
            // Делаем root способным принять фокус — нужно для глобальных горячих клавиш
            _root.focusable = true;
            _root.tabIndex = 0;

            // ───── INLINE-FALLBACK для критического layout ─────
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.flexGrow = 1;
            _root.style.backgroundColor = NovellaSettingsModule.GetInterfaceColor();

            // (Background image VE-слой убран — пользователь решил отказаться.
            //  Цвет интерфейса меняется через _root.style.backgroundColor в ApplyAppearance.)

            // ═══════════════════ SIDEBAR ═══════════════════
            _sideEl = new VisualElement();
            _sideEl.AddToClassList("ns-side");
            _sideEl.style.width = 240;
            _sideEl.style.flexDirection = FlexDirection.Column;
            _sideEl.style.backgroundColor = NovellaSettingsModule.GetBgSideColor();
            _sideEl.style.borderRightWidth = 1;
            _sideEl.style.borderRightColor = NovellaSettingsModule.GetBorderColor();
            _root.Add(_sideEl);

            BuildSidebarBrand(_sideEl);
            BuildSidebarStoryPick(_sideEl);
            BuildSidebarSearch(_sideEl);
            BuildSidebarModules(_sideEl);
            BuildSidebarHelp(_sideEl);

            // ═══════════════════ MAIN AREA ═══════════════════
            _mainEl = new VisualElement();
            _mainEl.AddToClassList("ns-main");
            _mainEl.style.flexGrow = 1;
            _mainEl.style.flexDirection = FlexDirection.Column;
            _mainEl.style.backgroundColor = NovellaSettingsModule.GetInterfaceColor();
            _root.Add(_mainEl);

            BuildTopbar(_mainEl);
            BuildContent(_mainEl);

            // ═══════════════════ COMMAND PALETTE OVERLAY ═══════════════════
            _commandPalette = new NovellaCommandPalette(this);
            _root.Add(_commandPalette.Root);

            // ═══════════════════ TUTORIAL OVERLAY (LAZY) ═══════════════════
            // КРИТИЧНАЯ ОПТИМИЗАЦИЯ:
            // Раньше IMGUIContainer всегда висел в дереве и его колбэк вызывался каждый
            // кадр (особенно когда соседнее окно — Graph — делало Repaint при перемещении
            // ноды; Unity при этом инвалидирует все открытые окна, и пустой overlay съедал
            // заметный кусок CPU + сетил pickingMode что триггерило relayout).
            //
            // Теперь overlay создаётся, но НЕ добавляется в дерево. 2 Hz-scheduler
            // проверяет статус туториала и добавляет/удаляет overlay по факту.
            // Когда туториал не активен — IMGUIContainer не существует в дереве и не
            // потребляет ресурсов вообще.
            CreateTutorialOverlay();

            _tutorialPoll = _root.schedule.Execute(SyncTutorialOverlay).Every(500);
            _tutorialPoll.StartingIn(0);

            // Перехватываем горячие клавиши на root С TrickleDown=true: событие приходит сюда
            // ДО того как достигнет любого VisualElement-инпута (TextField, IntegerField и т.д.).
            // Без этого Cmd+K и Cmd+L "проваливались" в TextField'ы (Unity открывал свой Quick Search).
            _root.RegisterCallback<KeyDownEvent>(OnGlobalKeyDown, TrickleDown.TrickleDown);
        }

        private void CreateTutorialOverlay()
        {
            _tutorialOverlay = new IMGUIContainer(() =>
            {
                // Колбэк выполняется только пока overlay в дереве. А overlay в дереве
                // только когда туториал активен — но guard оставим на случай рассинхрона.
                if (!NovellaTutorialManager.IsTutorialActive) return;
                NovellaTutorialManager.BlockBackgroundEvents(this, true);
                NovellaTutorialManager.DrawOverlay(this, true);
            });
            _tutorialOverlay.AddToClassList("ns-tutorial-overlay");
            _tutorialOverlay.style.position = Position.Absolute;
            _tutorialOverlay.style.left = 0;
            _tutorialOverlay.style.top = 0;
            _tutorialOverlay.style.right = 0;
            _tutorialOverlay.style.bottom = 0;
            _tutorialOverlay.pickingMode = PickingMode.Position;
        }

        private void SyncTutorialOverlay()
        {
            if (_tutorialOverlay == null || _root == null) return;
            bool needed = NovellaTutorialManager.IsTutorialActive;
            if (needed == _tutorialOverlayInTree) return;

            if (needed)
            {
                if (_tutorialOverlay.parent == null) _root.Add(_tutorialOverlay);
                _tutorialOverlayInTree = true;
            }
            else
            {
                if (_tutorialOverlay.parent != null) _tutorialOverlay.RemoveFromHierarchy();
                _tutorialOverlayInTree = false;
            }
        }

        private void OnGlobalKeyDown(KeyDownEvent evt)
        {
            bool ctrl = evt.ctrlKey || evt.commandKey;
            if (ctrl && evt.keyCode == KeyCode.K)
            {
                _commandPalette?.Open();
                evt.StopPropagation();
                evt.PreventDefault();
            }
            else if (ctrl && evt.keyCode == KeyCode.L)
            {
                ToolLang.Toggle();
                RefreshAllLabels();
                evt.StopPropagation();
                evt.PreventDefault();
            }
        }

        // ─────────── Sidebar: бренд ───────────

        private void BuildSidebarBrand(VisualElement parent)
        {
            var brand = new VisualElement();
            brand.AddToClassList("ns-side__brand");
            brand.style.flexDirection = FlexDirection.Row;
            brand.style.alignItems = Align.Center;
            brand.style.paddingLeft = 14; brand.style.paddingRight = 14;
            brand.style.paddingTop = 14; brand.style.paddingBottom = 12;
            brand.style.borderBottomWidth = 1;
            brand.style.borderBottomColor = new Color(0.165f, 0.176f, 0.243f);

            var logo = new VisualElement();
            logo.AddToClassList("ns-side__brand-logo");
            logo.style.width = 30; logo.style.height = 30;
            logo.style.marginRight = 10;
            logo.style.alignItems = Align.Center;
            logo.style.justifyContent = Justify.Center;
            logo.style.borderTopLeftRadius = 7; logo.style.borderTopRightRadius = 7;
            logo.style.borderBottomLeftRadius = 7; logo.style.borderBottomRightRadius = 7;
            logo.style.borderLeftWidth = 1; logo.style.borderRightWidth = 1;
            logo.style.borderTopWidth = 1; logo.style.borderBottomWidth = 1;
            logo.style.borderLeftColor = NovellaSettingsModule.GetAccentColor();
            logo.style.borderRightColor = NovellaSettingsModule.GetAccentColor();
            logo.style.borderTopColor = NovellaSettingsModule.GetAccentColor();
            logo.style.borderBottomColor = NovellaSettingsModule.GetAccentColor();
            logo.style.backgroundColor = new Color(0.36f, 0.75f, 0.92f, 0.09f);
            var logoLbl = new Label("N");
            logoLbl.AddToClassList("ns-side__brand-logo-text");
            logoLbl.style.color = NovellaSettingsModule.GetAccentColor();
            logoLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            logo.Add(logoLbl);
            brand.Add(logo);

            var text = new VisualElement();
            text.AddToClassList("ns-side__brand-text");
            text.style.flexDirection = FlexDirection.Column;

            var nameLbl = new Label("Novella Studio") { name = "brandName" };
            nameLbl.AddToClassList("ns-side__brand-name");
            nameLbl.style.fontSize = 14;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color = NovellaSettingsModule.GetTextColor();
            text.Add(nameLbl);

            var sub = new Label(ToolLang.Get("Story workshop", "Студия историй"));
            sub.AddToClassList("ns-side__brand-sub");
            sub.style.fontSize = 9;
            sub.style.color = NovellaSettingsModule.GetTextMuted();
            sub.style.marginTop = 2;
            text.Add(sub);
            brand.Add(text);

            parent.Add(brand);
        }

        // ─────────── Sidebar: active story picker ───────────

        private void BuildSidebarStoryPick(VisualElement parent)
        {
            var pick = new VisualElement();
            pick.AddToClassList("ns-story-pick");
            pick.RegisterCallback<ClickEvent>(_ => OpenStorySwitcher());

            var icon = new VisualElement();
            icon.AddToClassList("ns-story-pick__icon");
            icon.style.backgroundImage = NovellaHubIcons.GetTexture(NovellaHubIcons.Story);
            pick.Add(icon);

            var info = new VisualElement();
            info.AddToClassList("ns-story-pick__info");

            var lbl = new Label(ToolLang.Get("Active story", "Активная история"));
            lbl.AddToClassList("ns-story-pick__lbl");
            info.Add(lbl);

            var name = new Label(GetActiveStoryName()) { name = "activeStoryName" };
            name.AddToClassList("ns-story-pick__name");
            info.Add(name);

            pick.Add(info);

            var arr = new Label("▾");
            arr.AddToClassList("ns-story-pick__arr");
            pick.Add(arr);

            parent.Add(pick);
        }

        private string GetActiveStoryName()
        {
            string lastId = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            if (!string.IsNullOrEmpty(lastId))
            {
                var path = AssetDatabase.GUIDToAssetPath(lastId);
                if (!string.IsNullOrEmpty(path))
                {
                    var s = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                    if (s != null) return s.Title;
                }
            }
            return ToolLang.Get("Select a story!", "Выберите историю!");
        }

        public void RefreshActiveStoryLabel()
        {
            if (_root == null) return;
            var nameEl = _root.Q<Label>("activeStoryName");
            if (nameEl != null) nameEl.text = GetActiveStoryName();
        }

        private void OpenStorySwitcher()
        {
            var menu = new GenericMenu();
            string[] guids = AssetDatabase.FindAssets("t:NovellaStory");
            string activeGuid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var st = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                if (st == null) continue;
                bool isActive = g == activeGuid;
                string lbl = string.IsNullOrEmpty(st.Title) ? "(untitled)" : st.Title;
                menu.AddItem(new GUIContent(lbl), isActive, () =>
                {
                    EditorPrefs.SetString("Novella_ActiveStoryGuid", g);
                    var nameEl = _root.Q<Label>("activeStoryName");
                    if (nameEl != null) nameEl.text = st.Title;
                });
            }
            if (guids.Length == 0) menu.AddDisabledItem(new GUIContent(ToolLang.Get("No stories yet", "Истории не созданы")));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("➕ Create new story", "➕ Создать историю")), false, () =>
            {
                if (_modules.Count > 0 && _modules[0] is DashboardModule dash) dash.RequestCreateNewStory();
                SwitchToModule(0);
            });
            menu.ShowAsContext();
        }

        // ─────────── Sidebar: quick find ───────────

        private void BuildSidebarSearch(VisualElement parent)
        {
            var search = new VisualElement();
            search.AddToClassList("ns-search");
            search.RegisterCallback<ClickEvent>(_ => _commandPalette.Open());

            var icon = new VisualElement();
            icon.AddToClassList("ns-search__icon");
            icon.style.backgroundImage = NovellaHubIcons.GetTexture(NovellaHubIcons.Search);
            search.Add(icon);

            var text = new Label(ToolLang.Get("Quick find", "Быстрый поиск"));
            text.AddToClassList("ns-search__text");
            search.Add(text);

            var kbd = new Label("Ctrl K");
            kbd.AddToClassList("ns-search__kbd");
            search.Add(kbd);

            parent.Add(search);
        }

        // ─────────── Sidebar: список модулей ───────────

        private void BuildSidebarModules(VisualElement parent)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ns-side__scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            parent.Add(scroll);

            _modButtons = new List<VisualElement>();

            scroll.Add(MakeCategoryLabel(ToolLang.Get("Workshop", "Мастерская")));
            for (int i = 0; i < _modules.Count; i++)
            {
                int captured = i;
                var icon = i switch
                {
                    0 => NovellaHubIcons.Home,
                    1 => NovellaHubIcons.Characters,
                    2 => NovellaHubIcons.Scenes,
                    3 => NovellaHubIcons.UIEditor,
                    4 => NovellaHubIcons.Variables,
                    5 => NovellaHubIcons.Console,
                    6 => NovellaHubIcons.Settings,
                    _ => NovellaHubIcons.Home,
                };
                var label = GetModuleLabelLocalized(i);
                var btn = MakeModuleButton(label, icon, () => SwitchToModule(captured));
                // На кнопке консоли — три бейджа со счётчиками логов.
                if (icon == NovellaHubIcons.Console)
                {
                    AttachConsoleBadges(btn);
                }
                scroll.Add(btn);
                _modButtons.Add(btn);
            }

            scroll.Add(MakeCategoryLabel(ToolLang.Get("Story", "История")));

            scroll.Add(MakeModuleButton(
                ToolLang.Get("Story graph", "Граф истории"),
                NovellaHubIcons.Graph,
                OpenActiveStoryGraph));

            scroll.Add(MakeModuleButton(
                ToolLang.Get("CG gallery", "Галерея"),
                NovellaHubIcons.Gallery,
                () => NovellaGalleryWindow.OpenStandalone()));
        }

        private VisualElement MakeCategoryLabel(string text)
        {
            var l = new Label(text.ToUpperInvariant());
            l.AddToClassList("ns-cat");
            return l;
        }

        // ─── Бейджи консоли в сайдбаре ─────────────────────────────────
        // Три цветных пилюли «N ⓘ», «N ⚠», «N ✖» справа от названия.
        // Каждый показывается только если соответствующий счётчик > 0.
        private VisualElement _consoleBadgesBox;

        private void AttachConsoleBadges(VisualElement btn)
        {
            _consoleButton = btn;

            var box = new VisualElement();
            box.AddToClassList("ns-console-badges");
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.Center;
            _consoleBadgesBox = box;

            _consoleBadgeLog  = MakeConsoleBadge(new Color(0.62f, 0.70f, 0.78f));
            _consoleBadgeWarn = MakeConsoleBadge(new Color(0.95f, 0.78f, 0.30f));
            _consoleBadgeErr  = MakeConsoleBadge(new Color(0.92f, 0.36f, 0.36f));

            box.Add(_consoleBadgeLog);
            box.Add(_consoleBadgeWarn);
            box.Add(_consoleBadgeErr);
            btn.Add(box);

            // Запоминаем стартовые значения чтобы НЕ пульсировать сразу при
            // открытии Hub (там уже могут быть накопленные логи).
            var c0 = NovellaConsoleStore.CountByType();
            _prevLogCount = c0.log; _prevWarnCount = c0.warn; _prevErrCount = c0.error;

            // Подписываемся на изменения и сразу заполняем.
            NovellaConsoleStore.OnChanged -= OnConsoleStoreChanged;
            NovellaConsoleStore.OnChanged += OnConsoleStoreChanged;
            UpdateConsoleBadges();
        }

        private static Label MakeConsoleBadge(Color tint)
        {
            var l = new Label("0");
            l.style.fontSize = 9;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.paddingLeft = 6; l.style.paddingRight = 6;
            l.style.paddingTop = 1; l.style.paddingBottom = 1;
            l.style.marginLeft = 3;
            l.style.borderTopLeftRadius = 7;
            l.style.borderTopRightRadius = 7;
            l.style.borderBottomLeftRadius = 7;
            l.style.borderBottomRightRadius = 7;
            l.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.22f);
            l.style.color = tint;
            l.style.display = DisplayStyle.None; // по умолчанию скрыт
            return l;
        }

        private void OnConsoleStoreChanged()
        {
            // OnChanged может прилететь не из main thread — диспетчируем.
            EditorApplication.delayCall += UpdateConsoleBadges;
        }

        private void UpdateConsoleBadges()
        {
            if (_consoleBadgeLog == null) return;
            var c = NovellaConsoleStore.CountByType();
            SetBadge(_consoleBadgeLog,  c.log,   "ⓘ");
            SetBadge(_consoleBadgeWarn, c.warn,  "⚠");
            SetBadge(_consoleBadgeErr,  c.error, "✖");

            // Если хотя бы один счётчик увеличился — это «новое сообщение
            // прилетело», запускаем pulse-анимацию кнопки. Цвет вспышки берём
            // самый «срочный»: error > warn > log.
            bool gotNew = c.error > _prevErrCount || c.warn > _prevWarnCount || c.log > _prevLogCount;
            if (gotNew)
            {
                Color flash =
                    c.error > _prevErrCount  ? new Color(0.92f, 0.36f, 0.36f) :
                    c.warn  > _prevWarnCount ? new Color(0.95f, 0.78f, 0.30f) :
                                               new Color(0.62f, 0.70f, 0.78f);
                PulseConsoleButton(flash);
            }

            _prevLogCount  = c.log;
            _prevWarnCount = c.warn;
            _prevErrCount  = c.error;
        }

        // Pulse-анимация на кнопке консоли в сайдбаре — кратковременная
        // подсветка фоном цвета прилетевшего сообщения и лёгкое scale-up.
        // Через 600мс возвращаем всё к норме. Так юзер замечает что в
        // консоль что-то пришло, даже если Hub-вкладка скрыта на другом мониторе.
        private void PulseConsoleButton(Color flash)
        {
            if (_consoleButton == null) return;

            _consoleButton.style.backgroundColor = new StyleColor(new Color(flash.r, flash.g, flash.b, 0.30f));
            _consoleButton.style.scale = new StyleScale(new Scale(new Vector3(1.04f, 1.04f, 1f)));

            // Пауза прошлого таймера если был — иначе быстрая серия логов
            // оставила бы кнопку в «вспышке» навсегда.
            _consolePulseTimer?.Pause();
            _consolePulseTimer = _consoleButton.schedule.Execute(() =>
            {
                if (_consoleButton == null) return;
                _consoleButton.style.backgroundColor = StyleKeyword.Null;
                _consoleButton.style.scale = new StyleScale(new Scale(Vector3.one));
            }).StartingIn(600);
        }

        private static void SetBadge(Label l, int count, string icon)
        {
            if (l == null) return;
            if (count <= 0)
            {
                l.style.display = DisplayStyle.None;
            }
            else
            {
                // Сжимаем большие числа: 99+ — потолок.
                string num = count > 99 ? "99+" : count.ToString();
                l.text = num + " " + icon;
                l.style.display = DisplayStyle.Flex;
            }
        }

        private VisualElement MakeModuleButton(string label, NovellaHubIcons.Icon icon, Action onClick)
        {
            var btn = new VisualElement();
            btn.AddToClassList("ns-mod");
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.paddingLeft = 12; btn.style.paddingRight = 12;
            btn.style.paddingTop = 7; btn.style.paddingBottom = 7;
            btn.style.marginLeft = 8; btn.style.marginRight = 8;
            btn.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());

            var iconEl = new VisualElement();
            iconEl.AddToClassList("ns-mod__icon");
            iconEl.style.width = 16; iconEl.style.height = 16;
            iconEl.style.marginRight = 10;
            iconEl.style.backgroundImage = NovellaHubIcons.GetTexture(icon);
            btn.Add(iconEl);

            var lbl = new Label(label);
            lbl.AddToClassList("ns-mod__label");
            lbl.style.flexGrow = 1;
            lbl.style.fontSize = 12;
            btn.Add(lbl);

            return btn;
        }

        private string GetModuleLabelLocalized(int idx)
        {
            return idx switch
            {
                0 => ToolLang.Get("Home", "Главная"),
                1 => ToolLang.Get("Characters", "Персонажи"),
                2 => ToolLang.Get("Scenes & Menu", "Сцены и Меню"),
                3 => ToolLang.Get("UI Forge", "Кузница UI"),
                4 => ToolLang.Get("Variables", "Переменные"),
                _ => _modules[idx].ModuleName
            };
        }

        private void OpenActiveStoryGraph()
        {
            string guid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            NovellaStory st = null;

            if (!string.IsNullOrEmpty(guid))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                st = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
            }

            if (st == null)
            {
                var allGuids = AssetDatabase.FindAssets("t:NovellaStory");
                if (allGuids.Length > 0)
                {
                    var p = AssetDatabase.GUIDToAssetPath(allGuids[0]);
                    st = AssetDatabase.LoadAssetAtPath<NovellaStory>(p);
                    if (st != null) EditorPrefs.SetString("Novella_ActiveStoryGuid", allGuids[0]);
                }
            }

            if (st == null)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("No stories yet", "Историй пока нет"),
                    ToolLang.Get("Create a story first — click '+ New story' at the top.",
                                 "Сначала создай историю — кнопка «+ Новая история» сверху."),
                    "OK");
                return;
            }

            if (st.StartingChapter != null)
            {
                NovellaGraphWindow.OpenGraphWindow(st.StartingChapter);
            }
            else
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("No chapter set", "Нет стартовой главы"),
                    string.Format(ToolLang.Get("Story '{0}' has no starting chapter. Open settings now?",
                                              "У истории «{0}» не задана стартовая глава. Открыть настройки?"), st.Title),
                    ToolLang.Get("Open settings", "Открыть настройки"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    var capturedStory = st;
                    EditorApplication.delayCall += () => NovellaStorySettingsPopup.ShowWindow(capturedStory, null);
                }
            }
        }

        // ─────────── Sidebar: footer (tutorial button + quick keys) ───────────

        private void BuildSidebarHelp(VisualElement parent)
        {
            var tutBtn = new Button(() =>
            {
                EditorApplication.delayCall += () =>
                {
                    NovellaWelcomeWindow.ShowWindow();
                    Close();
                };
            })
            {
                text = "🎓 " + ToolLang.Get("Open tutorial", "Открыть обучение")
            };
            tutBtn.AddToClassList("ns-tutorial-btn");
            tutBtn.style.marginLeft = 12; tutBtn.style.marginRight = 12;
            tutBtn.style.marginTop = 8; tutBtn.style.marginBottom = 8;
            tutBtn.style.height = 34;
            tutBtn.style.backgroundColor = NovellaSettingsModule.GetAccentColor();
            tutBtn.style.color = NovellaSettingsModule.GetContrastingText(NovellaSettingsModule.GetAccentColor());
            tutBtn.style.borderTopLeftRadius = 6; tutBtn.style.borderTopRightRadius = 6;
            tutBtn.style.borderBottomLeftRadius = 6; tutBtn.style.borderBottomRightRadius = 6;
            tutBtn.style.borderLeftWidth = 0; tutBtn.style.borderRightWidth = 0;
            tutBtn.style.borderTopWidth = 0; tutBtn.style.borderBottomWidth = 0;
            tutBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            tutBtn.style.fontSize = 12;
            parent.Add(tutBtn);

            var help = new VisualElement();
            help.AddToClassList("ns-help");

            var title = new Label(ToolLang.Get("Quick keys", "Горячие клавиши"));
            title.AddToClassList("ns-help__title");
            help.Add(title);

            help.Add(MakeHelpRow(ToolLang.Get("Quick find", "Быстрый поиск"), "Ctrl K"));
            help.Add(MakeHelpRow(ToolLang.Get("Toggle language", "Сменить язык"), "Ctrl L"));

            parent.Add(help);
        }

        private VisualElement MakeHelpRow(string text, string kbd)
        {
            var row = new VisualElement();
            row.AddToClassList("ns-help__row");

            var nameLbl = new Label(text);
            nameLbl.AddToClassList("ns-help__row-label");
            row.Add(nameLbl);

            var k = new Label(kbd);
            k.AddToClassList("ns-help__kbd");
            row.Add(k);
            return row;
        }

        // ─────────── Topbar ───────────

        private void BuildTopbar(VisualElement parent)
        {
            var top = new VisualElement();
            top.AddToClassList("ns-top");
            top.style.height = 48;
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.style.borderBottomWidth = 1;
            top.style.borderBottomColor = new Color(0.165f, 0.176f, 0.243f);
            top.style.flexShrink = 0;
            parent.Add(top);

            BuildTopbarInto(top);
        }

        private void BuildTopbarInto(VisualElement top)
        {
            var collapse = new VisualElement();
            collapse.AddToClassList("ns-top__collapse");
            collapse.style.width = 48; collapse.style.height = 48;
            collapse.style.alignItems = Align.Center;
            collapse.style.justifyContent = Justify.Center;
            collapse.style.borderRightWidth = 1;
            collapse.style.borderRightColor = new Color(0.165f, 0.176f, 0.243f);
            collapse.RegisterCallback<ClickEvent>(_ => ToggleSidebar());

            var collapseIcon = new VisualElement();
            collapseIcon.AddToClassList("ns-top__collapse-icon");
            collapseIcon.style.width = 16; collapseIcon.style.height = 16;
            collapseIcon.style.backgroundImage = NovellaHubIcons.GetTexture(NovellaHubIcons.Menu);
            collapse.Add(collapseIcon);
            top.Add(collapse);

            // Breadcrumb: "Novella Studio / [Module]"
            var crumb = new VisualElement();
            crumb.AddToClassList("ns-crumb");

            var crumbIcon = new VisualElement();
            crumbIcon.AddToClassList("ns-crumb__icon");
            crumbIcon.style.backgroundImage = NovellaHubIcons.GetTexture(NovellaHubIcons.Story);
            crumb.Add(crumbIcon);

            var root = new Label("Novella Studio") { name = "crumbRoot" };
            root.AddToClassList("ns-crumb__root");
            crumb.Add(root);

            var sep = new Label("/");
            sep.AddToClassList("ns-crumb__sep");
            crumb.Add(sep);

            _crumbCurrent = new Label(GetModuleLabelLocalized(_currentModuleIndex));
            _crumbCurrent.AddToClassList("ns-crumb__cur");
            crumb.Add(_crumbCurrent);

            top.Add(crumb);

            // Действия справа — Play / + New story (только на Home)
            var actions = new VisualElement();
            actions.AddToClassList("ns-actions");

            var play = new Button(() => OpenActiveStoryGraph()) { text = "▶ " + ToolLang.Get("Play", "Запустить") };
            play.AddToClassList("ns-btn");
            play.AddToClassList("ns-btn--out");
            actions.Add(play);

            var newStory = new Button(() => {
                if (_modules.Count > 0 && _modules[0] is DashboardModule dash) dash.RequestCreateNewStory();
                SwitchToModule(0);
            }) { text = "＋ " + ToolLang.Get("New story", "Новая история") };
            newStory.AddToClassList("ns-btn");
            newStory.AddToClassList("ns-btn--fill");
            newStory.AddToClassList("ns-btn--last");
            actions.Add(newStory);

            top.Add(actions);

            _topActions = actions;
            _topActions.style.display = (_currentModuleIndex == 0) ? DisplayStyle.Flex : DisplayStyle.None;

            var minimize = new VisualElement();
            minimize.style.width = 32;
            minimize.style.height = 24;
            minimize.style.marginRight = 8;
            minimize.style.marginLeft = 6;
            minimize.style.alignItems = Align.Center;
            minimize.style.justifyContent = Justify.Center;
            minimize.style.borderTopLeftRadius = 4;
            minimize.style.borderTopRightRadius = 4;
            minimize.style.borderBottomLeftRadius = 4;
            minimize.style.borderBottomRightRadius = 4;
            minimize.tooltip = ToolLang.Get("Minimize", "Свернуть");
            minimize.RegisterCallback<MouseEnterEvent>(_ => minimize.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f));
            minimize.RegisterCallback<MouseLeaveEvent>(_ => minimize.style.backgroundColor = new StyleColor(StyleKeyword.Initial));
            minimize.RegisterCallback<ClickEvent>(_ => MinimizeToLauncher());

            var dash = new VisualElement();
            dash.style.width = 12;
            dash.style.height = 2;
            dash.style.backgroundColor = NovellaSettingsModule.GetTextColor();
            dash.style.marginTop = 1;
            minimize.Add(dash);
            top.Add(minimize);
        }

        private void MinimizeToLauncher()
        {
            if (rootVisualElement == null)
            {
                Close();
                EditorApplication.delayCall += () => NovellaToolbarButton.Flash();
                return;
            }

            rootVisualElement.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(50f), Length.Percent(50f)));
            rootVisualElement.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("opacity"),
                new StylePropertyName("scale"),
            });
            rootVisualElement.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.16f, TimeUnit.Second),
                new TimeValue(0.16f, TimeUnit.Second),
            });
            rootVisualElement.style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction> {
                new EasingFunction(EasingMode.EaseInCubic),
                new EasingFunction(EasingMode.EaseInCubic),
            });
            rootVisualElement.style.opacity = 0f;
            rootVisualElement.style.scale = new StyleScale(new Scale(new Vector3(0.92f, 0.92f, 1f)));

            rootVisualElement.schedule.Execute(() =>
            {
                Close();
                EditorApplication.delayCall += () => NovellaToolbarButton.Flash();
            }).StartingIn(170);
        }

        // ─────────── Content area ───────────

        private void BuildContent(VisualElement parent)
        {
            var content = new VisualElement();
            content.AddToClassList("ns-content");
            parent.Add(content);

            _moduleContainer = new IMGUIContainer(DrawCurrentModuleIMGUI);
            _moduleContainer.AddToClassList("ns-content__imgui");
            content.Add(_moduleContainer);
        }

        private void DrawCurrentModuleIMGUI()
        {
            if (_modules == null || _currentModuleIndex < 0 || _currentModuleIndex >= _modules.Count) return;
            var rect = _moduleContainer != null ? _moduleContainer.contentRect : new Rect(0, 0, position.width - 240, position.height - 48);
            _modules[_currentModuleIndex].DrawGUI(rect);
        }

        // ─────────── Sidebar collapse ───────────

        private void ToggleSidebar()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            if (_sidebarCollapsed)
            {
                _sideEl.AddToClassList("ns-side--collapsed");
                _sideEl.style.width = 64;
                ApplyCollapsedStyleToModButtons(true);
            }
            else
            {
                _sideEl.RemoveFromClassList("ns-side--collapsed");
                _sideEl.style.width = 240;
                ApplyCollapsedStyleToModButtons(false);
            }
        }

        private void ApplyCollapsedStyleToModButtons(bool collapsed)
        {
            if (_modButtons == null) return;
            foreach (var btn in _modButtons) ApplyCollapsedStyleToOne(btn, collapsed);

            var allMods = _sideEl.Query<VisualElement>(className: "ns-mod").ToList();
            foreach (var btn in allMods) ApplyCollapsedStyleToOne(btn, collapsed);
        }

        private void ApplyCollapsedStyleToOne(VisualElement btn, bool collapsed)
        {
            if (btn == null) return;
            if (collapsed)
            {
                btn.style.marginLeft = 4; btn.style.marginRight = 4;
                btn.style.paddingLeft = 0; btn.style.paddingRight = 0;
                btn.style.justifyContent = Justify.Center;
                var lbl = btn.Q<Label>(className: "ns-mod__label");
                if (lbl != null) lbl.style.display = DisplayStyle.None;
                var icon = btn.Q<VisualElement>(className: "ns-mod__icon");
                if (icon != null) icon.style.marginRight = 0;
                // Бейджи консоли тоже прячем — они с числами «99+» торчат
                // за свёрнутый сайдбар и выглядят неаккуратно.
                var badges = btn.Q<VisualElement>(className: "ns-console-badges");
                if (badges != null) badges.style.display = DisplayStyle.None;
            }
            else
            {
                btn.style.marginLeft = 8; btn.style.marginRight = 8;
                btn.style.paddingLeft = 12; btn.style.paddingRight = 12;
                btn.style.justifyContent = Justify.FlexStart;
                var lbl = btn.Q<Label>(className: "ns-mod__label");
                if (lbl != null) lbl.style.display = DisplayStyle.Flex;
                var icon = btn.Q<VisualElement>(className: "ns-mod__icon");
                if (icon != null) icon.style.marginRight = 10;
                var badges = btn.Q<VisualElement>(className: "ns-console-badges");
                if (badges != null) badges.style.display = DisplayStyle.Flex;
            }
        }

        // ─────────── Active module sync ───────────

        private void SyncModuleStates()
        {
            if (_modButtons == null) return;
            for (int i = 0; i < _modButtons.Count; i++)
            {
                if (i == _currentModuleIndex) _modButtons[i].AddToClassList("ns-mod--active");
                else _modButtons[i].RemoveFromClassList("ns-mod--active");
            }
            if (_crumbCurrent != null) _crumbCurrent.text = GetModuleLabelLocalized(_currentModuleIndex);

            if (_topActions != null)
                _topActions.style.display = (_currentModuleIndex == 0) ? DisplayStyle.Flex : DisplayStyle.None;

            if (_moduleContainer != null)
            {
                _moduleContainer.style.opacity = 0;
                _moduleContainer.style.translate = new StyleTranslate(new Translate(0, 6, 0));
                _moduleContainer.schedule.Execute(() =>
                {
                    _moduleContainer.style.opacity = 1f;
                    _moduleContainer.style.translate = new StyleTranslate(new Translate(0, 0, 0));
                }).StartingIn(10);
            }

            Repaint();
        }

        private void RefreshAllLabels()
        {
            if (_root == null || _sideEl == null) return;

            VisualElement mainEl = null;
            for (int i = 0; i < _root.childCount; i++)
            {
                var ch = _root[i];
                if (ch.ClassListContains("ns-main")) { mainEl = ch; break; }
                if (ch != _sideEl && ch != _commandPalette?.Root && ch != _tutorialOverlay) mainEl = ch;
            }

            _sideEl.Clear();
            BuildSidebarBrand(_sideEl);
            BuildSidebarStoryPick(_sideEl);
            BuildSidebarSearch(_sideEl);
            BuildSidebarModules(_sideEl);
            BuildSidebarHelp(_sideEl);

            if (_sidebarCollapsed)
            {
                _sideEl.AddToClassList("ns-side--collapsed");
                _sideEl.style.width = 64;
                ApplyCollapsedStyleToModButtons(true);
            }

            if (mainEl != null)
            {
                if (mainEl.childCount > 0 && mainEl[0].ClassListContains("ns-top"))
                {
                    var top = mainEl[0];
                    top.Clear();
                    BuildTopbarInto(top);
                }
            }

            SyncModuleStates();
            Repaint();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // DASHBOARD MODULE — IMGUI как раньше
    // ════════════════════════════════════════════════════════════════════

    public class DashboardModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Home", "Главная");
        public string ModuleIcon => "🏠";

        private EditorWindow _window;
        private Vector2 _scrollPos;
        private List<NovellaStory> _stories = new List<NovellaStory>();
        private Dictionary<NovellaStory, DateTime> _modifiedTimes = new Dictionary<NovellaStory, DateTime>();

        // Hover-tracking для library tile'ов: вместо Repaint на каждый MouseMove
        // храним последний hovered tile и Repaint только при смене состояния.
        private int _hoveredTileIdx = -1;
        private int _statCharacters = 0;
        private int _statTrees = 0;

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
            RefreshData();
        }

        public void OnDisable() { }

        public void RefreshExternal() => RefreshData();

        public void RequestCreateNewStory()
        {
            EditorApplication.delayCall += CreateNewStory;
        }

        private void RefreshData()
        {
            _stories.Clear();
            _modifiedTimes.Clear();
            string[] storyGuids = AssetDatabase.FindAssets("t:NovellaStory");
            foreach (var guid in storyGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var story = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                if (story == null) continue;

                _stories.Add(story);

                DateTime latest = DateTime.MinValue;

                try
                {
                    var fullPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
                    if (System.IO.File.Exists(fullPath))
                        latest = System.IO.File.GetLastWriteTime(fullPath);
                }
                catch { }

                if (story.StartingChapter != null)
                {
                    try
                    {
                        string chapterPath = AssetDatabase.GetAssetPath(story.StartingChapter);
                        if (!string.IsNullOrEmpty(chapterPath))
                        {
                            var fullPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), chapterPath);
                            if (System.IO.File.Exists(fullPath))
                            {
                                var cTime = System.IO.File.GetLastWriteTime(fullPath);
                                if (cTime > latest) latest = cTime;
                            }
                        }
                    }
                    catch { }
                }

                long openTicks = long.Parse(EditorPrefs.GetString("Novella_LastOpened_" + guid, "0"));
                if (openTicks > 0)
                {
                    var openTime = new DateTime(openTicks);
                    if (openTime > latest) latest = openTime;
                }

                if (latest > DateTime.MinValue) _modifiedTimes[story] = latest;
            }
            _stories = _stories.OrderByDescending(s => _modifiedTimes.TryGetValue(s, out var t) ? t : DateTime.MinValue).ToList();

            _statCharacters = AssetDatabase.FindAssets("t:NovellaCharacter").Length;
            _statTrees = AssetDatabase.FindAssets("t:NovellaTree")
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Count(p => !string.IsNullOrEmpty(p) && !p.Contains("/Tutorials/"));
        }

        public void DrawGUI(Rect position)
        {
            GUILayout.Space(24);
            GUILayout.BeginHorizontal();
            GUILayout.Space(28);
            GUILayout.BeginVertical();

            var h1 = new GUIStyle(EditorStyles.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            h1.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUILayout.Label(ToolLang.Get("Welcome back", "С возвращением"), h1);

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 13 };
            sub.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label($"{_stories.Count} " + ToolLang.Get("stories in your workshop", "историй в мастерской"), sub);

            GUILayout.Space(20);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            DrawStatRow();

            GUILayout.Space(18);
            DrawSectionHeader("📖 " + ToolLang.Get("Recent stories", "Последние истории"));

            if (NovellaSettingsModule.AccentButton("➕ " + ToolLang.Get("Create new story", "Создать новую историю"), GUILayout.Height(34), GUILayout.MaxWidth(360)))
            {
                EditorApplication.delayCall += CreateNewStory;
            }
            GUILayout.Space(12);

            float availW = position.width - 28 - 28;
            int columns = Mathf.Clamp(Mathf.FloorToInt(availW / 240f), 1, 4);
            DrawStoriesGrid(columns);

            GUILayout.Space(18);
            DrawSectionHeader("🛠 " + ToolLang.Get("Library tools", "Инструменты библиотеки"));
            DrawLibraryTools();

            GUILayout.Space(18);
            DrawSectionHeader("💡 " + ToolLang.Get("Today's tip", "Подсказка дня"));
            DrawTipCard();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(28);
            GUILayout.EndHorizontal();
        }

        private void DrawStatRow()
        {
            GUILayout.BeginHorizontal();

            DrawStat(ToolLang.Get("Stories", "Истории"), _stories.Count.ToString());
            GUILayout.Space(10);

            DrawStat(ToolLang.Get("Characters", "Персонажи"), _statCharacters.ToString());
            GUILayout.Space(10);

            DrawStat(ToolLang.Get("Story graphs", "Графы"), _statTrees.ToString());

            GUILayout.EndHorizontal();
        }

        private void DrawStat(string label, string value)
        {
            GUILayout.BeginVertical(GUILayout.Width(190));
            var lblStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            lblStyle.normal.textColor = NovellaSettingsModule.GetTextMuted();

            var valStyle = new GUIStyle(EditorStyles.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            valStyle.normal.textColor = NovellaSettingsModule.GetTextColor();

            Rect cardRect = GUILayoutUtility.GetRect(190, 70, GUILayout.Width(190), GUILayout.Height(70));
            EditorGUI.DrawRect(cardRect, new Color(0.10f, 0.106f, 0.149f, 1f));
            DrawRectBorder(cardRect, new Color(0.165f, 0.176f, 0.243f, 1f));

            GUI.Label(new Rect(cardRect.x + 14, cardRect.y + 12, cardRect.width - 28, 14), label.ToUpperInvariant(), lblStyle);
            GUI.Label(new Rect(cardRect.x + 14, cardRect.y + 30, cardRect.width - 28, 30), value, valStyle);

            GUILayout.EndVertical();
        }

        private void DrawSectionHeader(string title)
        {
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            st.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label(title.ToUpperInvariant(), st);
            Rect r = GUILayoutUtility.GetRect(100, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.165f, 0.176f, 0.243f, 1f));
            GUILayout.Space(10);
        }

        private void DrawStoriesGrid(int columns)
        {
            var snapshot = _stories.ToArray();

            int total = snapshot.Length;
            int row = 0;
            for (int i = 0; i < total; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    int idx = i + c;
                    if (idx >= total)
                    {
                        GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(1));
                    }
                    else
                    {
                        DrawStoryCard(snapshot[idx]);
                    }
                    if (c < columns - 1) GUILayout.Space(10);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                row++;
            }
        }

        private void DrawStoryCard(NovellaStory st)
        {
            if (st == null) return;

            var v = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(14, 14, 12, 12) };
            GUILayout.BeginVertical(v, GUILayout.ExpandWidth(true), GUILayout.MinWidth(220));

            var t = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            t.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUILayout.Label(string.IsNullOrEmpty(st.Title) ? "(untitled)" : st.Title, t);

            var meta = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            meta.normal.textColor = NovellaSettingsModule.GetTextMuted();
            string metaText = _modifiedTimes.TryGetValue(st, out var dt)
                ? FormatRelativeTime(dt)
                : ToolLang.Get("never opened", "не открывалось");
            GUILayout.Label(metaText, meta);

            var d = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fixedHeight = 28 };
            d.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label(string.IsNullOrEmpty(st.Description) ? ToolLang.Get("No description.", "Нет описания.") : st.Description, d);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("⚙", GUILayout.Width(26), GUILayout.Height(24)))
            {
                var capturedStory = st;
                EditorApplication.delayCall += () => NovellaStorySettingsPopup.ShowWindow(capturedStory, RefreshData);
            }

            if (NovellaSettingsModule.AccentButton(ToolLang.Get("Open", "Открыть"), GUILayout.Height(24)))
            {
                var capturedStory = st;
                EditorApplication.delayCall += () => OpenStoryFromCard(capturedStory);
            }

            if (NovellaSettingsModule.ColoredButton("🗑", new Color(0.85f, 0.32f, 0.32f), null, GUILayout.Width(26), GUILayout.Height(24)))
            {
                var capturedStory = st;
                EditorApplication.delayCall += () =>
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Delete story", "Удалить историю"),
                        string.Format(ToolLang.Get("Delete '{0}'?", "Удалить «{0}»?"), capturedStory.Title),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        string deletedPath = AssetDatabase.GetAssetPath(capturedStory);
                        string deletedGuid = AssetDatabase.AssetPathToGUID(deletedPath);
                        AssetDatabase.DeleteAsset(deletedPath);
                        if (!string.IsNullOrEmpty(deletedGuid) &&
                            EditorPrefs.GetString("Novella_ActiveStoryGuid", "") == deletedGuid)
                        {
                            EditorPrefs.DeleteKey("Novella_ActiveStoryGuid");
                        }
                        RefreshData();
                        if (_window is NovellaHubWindow hub) hub.RefreshActiveStoryLabel();
                        if (_window != null) _window.Repaint();
                    }
                };
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void OpenStoryFromCard(NovellaStory st)
        {
            if (st == null) return;
            string assetPath = AssetDatabase.GetAssetPath(st);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            EditorPrefs.SetString("Novella_ActiveStoryGuid", guid);

            EditorPrefs.SetString("Novella_LastOpened_" + guid, DateTime.Now.Ticks.ToString());

            if (_modifiedTimes.ContainsKey(st)) _modifiedTimes[st] = DateTime.Now;
            else _modifiedTimes.Add(st, DateTime.Now);

            if (st.StartingChapter != null)
            {
                NovellaGraphWindow.OpenGraphWindow(st.StartingChapter);
            }
            else
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("No chapter set", "Нет стартовой главы"),
                    ToolLang.Get("This story has no starting chapter yet. Open settings to assign one?",
                                 "У этой истории не задана стартовая глава. Открыть настройки чтобы выбрать?"),
                    ToolLang.Get("Open settings", "Открыть настройки"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    NovellaStorySettingsPopup.ShowWindow(st, RefreshData);
                }
            }
        }

        private static string FormatRelativeTime(DateTime dt)
        {
            TimeSpan diff = DateTime.Now - dt;
            if (diff.TotalSeconds < 60) return ToolLang.Get("just now", "только что");
            if (diff.TotalMinutes < 60) return string.Format(ToolLang.Get("{0} min ago", "{0} мин назад"), (int)diff.TotalMinutes);
            if (diff.TotalHours < 24) return string.Format(ToolLang.Get("{0}h ago", "{0} ч назад"), (int)diff.TotalHours);
            if (diff.TotalDays < 7) return string.Format(ToolLang.Get("{0}d ago", "{0} д назад"), (int)diff.TotalDays);
            return dt.ToString("dd MMM yyyy");
        }

        private void DrawLibraryTools()
        {
            GUILayout.BeginHorizontal();

            DrawLibraryTile(
                0,
                "🗺",
                ToolLang.Get("Browse all graphs", "Все графы"),
                ToolLang.Get("Open the graph manager — view, open, or clean up unused chapter graphs from your project.",
                             "Открыть менеджер графов — просмотри, открой или удали неиспользуемые графы глав из проекта."),
                new Color(0.63f, 0.49f, 1f),
                () => EditorApplication.delayCall += () => NovellaGraphsBrowserWindow.ShowWindow(_window)
            );

            GUILayout.Space(10);

            int storiesCount = _stories.Count;
            bool canDeleteAll = storiesCount >= 2;
            DrawLibraryTile(
                1,
                "🗑",
                ToolLang.Get("Delete all stories", "Удалить все истории"),
                canDeleteAll
                    ? string.Format(ToolLang.Get("Remove all {0} stories from your project. This action cannot be undone.",
                                                  "Удалить все истории ({0}) из проекта. Действие нельзя отменить."), storiesCount)
                    : ToolLang.Get("Available when you have 2 or more stories.",
                                    "Доступно когда у тебя 2 или больше историй."),
                new Color(0.85f, 0.32f, 0.32f),
                canDeleteAll
                    ? (() => EditorApplication.delayCall += DeleteAllStoriesWithDoubleConfirm)
                    : (System.Action)null
            );

            GUILayout.EndHorizontal();
        }

        private void DrawLibraryTile(int idx, string icon, string title, string description, Color accent, System.Action onClick)
        {
            bool enabled = onClick != null;
            float h = 78;
            Rect r = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true), GUILayout.Height(h));

            bool hover = enabled && r.Contains(Event.current.mousePosition);

            EditorGUI.DrawRect(r, hover
                ? new Color(0.13f, 0.137f, 0.18f)
                : new Color(0.10f, 0.106f, 0.149f));
            DrawRectBorder(r, hover
                ? new Color(accent.r, accent.g, accent.b, 0.55f)
                : new Color(0.165f, 0.176f, 0.243f));

            Rect iconRect = new Rect(r.x + 14, r.y + 16, 46, 46);
            EditorGUI.DrawRect(iconRect, new Color(accent.r, accent.g, accent.b, enabled ? 0.13f : 0.06f));
            DrawRectBorder(iconRect, new Color(accent.r, accent.g, accent.b, enabled ? 0.5f : 0.25f));
            var iconStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            iconStyle.normal.textColor = enabled ? accent : new Color(accent.r, accent.g, accent.b, 0.5f);
            GUI.Label(iconRect, icon, iconStyle);

            var tStyle = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            tStyle.normal.textColor = enabled ? NovellaSettingsModule.GetTextColor() : new Color(0.55f, 0.56f, 0.62f);
            GUI.Label(new Rect(r.x + 70, r.y + 14, r.width - 80, 18), title, tStyle);

            var dStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, wordWrap = true };
            dStyle.normal.textColor = enabled ? NovellaSettingsModule.GetTextMuted() : new Color(0.42f, 0.43f, 0.48f);
            GUI.Label(new Rect(r.x + 70, r.y + 32, r.width - 80, h - 36), description, dStyle);

            if (enabled && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                onClick();
                Event.current.Use();
            }

            // ОПТИМИЗАЦИЯ: раньше Repaint вызывался на каждый MouseMove над тайлом
            // (десятки раз в секунду пока юзер просто водит мышью по Hub'у — это
            // инвалидирует и соседнее окно Graph). Теперь Repaint только когда
            // hover-state РЕАЛЬНО меняется (мышь зашла/вышла).
            int newHovered = hover ? idx : (_hoveredTileIdx == idx ? -1 : _hoveredTileIdx);
            if (newHovered != _hoveredTileIdx)
            {
                _hoveredTileIdx = newHovered;
                if (_window != null) _window.Repaint();
            }
        }

        private void DeleteAllStoriesWithDoubleConfirm()
        {
            int count = _stories.Count;
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete all stories?", "Удалить все истории?"),
                string.Format(ToolLang.Get(
                    "This will permanently delete all {0} stories from your project.\n\nDo you want to continue to the confirmation step?",
                    "Это безвозвратно удалит все истории ({0}) из проекта.\n\nПродолжить к шагу подтверждения?"), count),
                ToolLang.Get("Continue", "Продолжить"),
                ToolLang.Get("Cancel", "Отмена")))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("⚠ Final confirmation", "⚠ Финальное подтверждение"),
                ToolLang.Get(
                    "Last chance — all stories will be deleted right now.\n\nThis cannot be undone.",
                    "Последний шанс — все истории будут удалены прямо сейчас.\n\nЭто действие нельзя отменить."),
                ToolLang.Get("Yes, delete everything", "Да, удалить всё"),
                ToolLang.Get("Cancel", "Отмена")))
            {
                return;
            }

            var snapshot = _stories.ToArray();
            int deleted = 0;
            foreach (var st in snapshot)
            {
                if (st == null) continue;
                string path = AssetDatabase.GetAssetPath(st);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.DeleteAsset(path)) deleted++;
            }
            EditorPrefs.DeleteKey("Novella_ActiveStoryGuid");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshData();
            if (_window is NovellaHubWindow hub) hub.RefreshActiveStoryLabel();
            if (_window != null) _window.Repaint();

            EditorUtility.DisplayDialog(
                ToolLang.Get("Done", "Готово"),
                string.Format(ToolLang.Get("Deleted {0} stories.", "Удалено историй: {0}"), deleted),
                "OK");
        }

        private void DrawTipCard()
        {
            string[] tips =
            {
                ToolLang.Get("Drag a sprite right onto a character layer slot — no need to click 'Browse' every time.", "Перетащи спрайт прямо в ячейку слоя персонажа — не нужно каждый раз жать «Выбрать»."),
                ToolLang.Get("Press Ctrl+K anywhere in the Studio to instantly find characters, stories or variables.", "Нажми Ctrl+K в Студии, чтобы мгновенно найти любого персонажа, историю или переменную."),
                ToolLang.Get("Right-click empty space in the Story Graph to add nodes — start with Dialogue and Branch.", "Правый клик в пустоте графа добавит ноду — начни с Диалога и Развилки."),
                ToolLang.Get("Press F1 anytime to restart the tutorial — it remembers where you left off.", "F1 в любой момент перезапустит туториал — он помнит на каком уроке ты остановился.")
            };
            int dayIdx = (int)((DateTime.Now.Ticks / TimeSpan.TicksPerDay) % tips.Length);

            Rect tipRect = GUILayoutUtility.GetRect(100, 56, GUILayout.ExpandWidth(true), GUILayout.Height(56));
            EditorGUI.DrawRect(tipRect, new Color(0.075f, 0.082f, 0.11f, 1f));
            DrawRectBorder(tipRect, new Color(0.36f, 0.75f, 0.92f, 0.34f));
            EditorGUI.DrawRect(new Rect(tipRect.x, tipRect.y, 3, tipRect.height), new Color(0.36f, 0.75f, 0.92f, 1f));

            var tt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
            tt.normal.textColor = NovellaSettingsModule.GetAccentColor();
            GUI.Label(new Rect(tipRect.x + 16, tipRect.y + 8, tipRect.width - 32, 14), ToolLang.Get("DID YOU KNOW", "А ВЫ ЗНАЛИ"), tt);

            var bt = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
            bt.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUI.Label(new Rect(tipRect.x + 16, tipRect.y + 22, tipRect.width - 32, 30), tips[dayIdx], bt);
        }

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private void CreateNewStory()
        {
            string baseDir = "Assets/NovellaEngine/Runtime/Data/Stories";
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
                AssetDatabase.Refresh();
            }

            string guid = Guid.NewGuid().ToString().Substring(0, 5);

            var newTree = ScriptableObject.CreateInstance<NovellaTree>();
            newTree.name = $"Chapter_1_{guid}";

            var newStory = ScriptableObject.CreateInstance<NovellaStory>();
            newStory.name = $"Story_{guid}";
            newStory.Title = "New Story";
            newStory.StartingChapter = newTree;

            string treePath = $"{baseDir}/Chapter_1_{guid}.asset";
            string storyPath = $"{baseDir}/Story_{guid}.asset";

            NovellaStorySettingsPopup.ShowWindowForNew(newStory, newTree, storyPath, treePath, () =>
            {
                RefreshData();
                if (_window is NovellaHubWindow hub) hub.RefreshActiveStoryLabel();
            });
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Story Settings popup
    // ════════════════════════════════════════════════════════════════════

    public class NovellaStorySettingsPopup : EditorWindow
    {
        public NovellaStory Story;
        public Action OnClose;
        private Vector2 _scroll;

        // Pending-mode: assets are not yet on disk; created only on Save & Close.
        private bool _isPending;
        private NovellaTree _pendingTree;
        private string _pendingStoryPath;
        private string _pendingTreePath;
        private bool _saved;

        public static void ShowWindow(NovellaStory story, Action onClose)
        {
            var win = GetWindow<NovellaStorySettingsPopup>(true, ToolLang.Get("Story Settings", "Настройки истории"), true);
            win.Story = story;
            win.OnClose = onClose;
            win._isPending = false;
            win._pendingTree = null;
            win._pendingStoryPath = null;
            win._pendingTreePath = null;
            win._saved = false;
            win.minSize = new Vector2(540, 420);
            win.maxSize = new Vector2(900, 800);
            win.ShowUtility();
        }

        public static void ShowWindowForNew(NovellaStory pendingStory, NovellaTree pendingTree,
                                            string storyPath, string treePath, Action onClose)
        {
            var win = GetWindow<NovellaStorySettingsPopup>(true, ToolLang.Get("Story Settings", "Настройки истории"), true);
            win.Story = pendingStory;
            win.OnClose = onClose;
            win._isPending = true;
            win._pendingTree = pendingTree;
            win._pendingStoryPath = storyPath;
            win._pendingTreePath = treePath;
            win._saved = false;
            win.minSize = new Vector2(540, 420);
            win.maxSize = new Vector2(900, 800);
            win.ShowUtility();
        }

        private void OnDestroy()
        {
            if (_isPending && !_saved)
            {
                if (_pendingTree != null) DestroyImmediate(_pendingTree);
                if (Story != null && !AssetDatabase.Contains(Story)) DestroyImmediate(Story);
            }
        }

        private void CommitPendingAssets()
        {
            if (!_isPending) return;

            bool useInitialTree = Story.StartingChapter == _pendingTree;
            if (useInitialTree && _pendingTree != null)
            {
                AssetDatabase.CreateAsset(_pendingTree, _pendingTreePath);
            }
            else if (_pendingTree != null)
            {
                DestroyImmediate(_pendingTree);
                _pendingTree = null;
            }

            AssetDatabase.CreateAsset(Story, _pendingStoryPath);
            AssetDatabase.SaveAssets();

            string storyGuid = AssetDatabase.AssetPathToGUID(_pendingStoryPath);
            if (!string.IsNullOrEmpty(storyGuid))
            {
                EditorPrefs.SetString("Novella_ActiveStoryGuid", storyGuid);
            }

            _saved = true;
            _isPending = false;
        }

        private void OnGUI()
        {
            if (Story == null) { Close(); return; }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.075f, 0.078f, 0.106f));

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.BeginVertical();

            var h1 = new GUIStyle(EditorStyles.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            h1.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUILayout.Label(ToolLang.Get("Story Settings", "Настройки истории"), h1);
            GUILayout.Space(10);

            DrawSectionLabel(ToolLang.Get("TITLE", "НАЗВАНИЕ"));
            Story.Title = EditorGUILayout.TextField(Story.Title);

            GUILayout.Space(10);
            DrawSectionLabel(ToolLang.Get("DESCRIPTION", "ОПИСАНИЕ"));
            Story.Description = EditorGUILayout.TextArea(Story.Description, GUILayout.Height(50));

            GUILayout.Space(14);
            DrawSectionLabel(ToolLang.Get("STARTING CHAPTER", "СТАРТОВАЯ ГЛАВА"));
            DrawHelpHint(ToolLang.Get(
                "Pick which chapter the story will play first. Click 'Create new chapter' to make one if there are none.",
                "Выбери главу, с которой история начнётся. Нажми «Создать главу» если их ещё нет."));

            GUILayout.BeginHorizontal();
            string currentName = Story.StartingChapter != null ? Story.StartingChapter.name : ToolLang.Get("Not selected", "Не выбрана");
            Rect currRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.Height(28));
            EditorGUI.DrawRect(currRect, Story.StartingChapter != null ? new Color(0.1f, 0.106f, 0.149f) : new Color(0.18f, 0.10f, 0.10f));
            DrawRectBorder(currRect, Story.StartingChapter != null ? new Color(0.36f, 0.75f, 0.92f, 0.5f) : new Color(0.7f, 0.3f, 0.3f, 0.5f));
            var currentStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 0, 0) };
            currentStyle.normal.textColor = Story.StartingChapter != null ? NovellaSettingsModule.GetTextColor() : new Color(1f, 0.65f, 0.5f);
            GUI.Label(currRect, "▶  " + currentName, currentStyle);

            if (NovellaSettingsModule.AccentButton("➕ " + ToolLang.Get("New chapter", "Создать главу"), GUILayout.Width(150), GUILayout.Height(28)))
            {
                CreateNewChapterAndAssign();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            DrawSectionLabel(ToolLang.Get("AVAILABLE CHAPTERS", "ДОСТУПНЫЕ ГЛАВЫ"));
            DrawChaptersGrid();

            GUILayout.FlexibleSpace();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (NovellaSettingsModule.NeutralButton(ToolLang.Get("Cancel", "Отмена"), GUILayout.Width(100), GUILayout.Height(32)))
            {
                Close();
            }
            GUILayout.Space(8);
            if (NovellaSettingsModule.AccentButton(ToolLang.Get("Save & Close", "Сохранить и закрыть"), GUILayout.Width(180), GUILayout.Height(32)))
            {
                if (_isPending)
                {
                    CommitPendingAssets();
                }
                else
                {
                    EditorUtility.SetDirty(Story);
                    AssetDatabase.SaveAssets();
                }
                OnClose?.Invoke();
                Close();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(14);
            GUILayout.EndVertical();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();
        }

        private struct CachedChapter { public NovellaTree Tree; public string Path; public int NodeCount; }
        private List<CachedChapter> _chapterCache;

        private void RefreshChaptersCache()
        {
            _chapterCache = new List<CachedChapter>();
            string[] guids = AssetDatabase.FindAssets("t:NovellaTree");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != null && path.Contains("/Tutorials/")) continue;
                var tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(path);
                if (tree == null) continue;
                _chapterCache.Add(new CachedChapter
                {
                    Tree = tree,
                    Path = path,
                    NodeCount = tree.Nodes != null ? tree.Nodes.Count : 0,
                });
            }
        }

        private void DrawChaptersGrid()
        {
            if (_chapterCache == null) RefreshChaptersCache();

            if (_chapterCache.Count == 0)
            {
                Rect empty = GUILayoutUtility.GetRect(0, 80, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(empty, new Color(0.1f, 0.106f, 0.149f));
                DrawRectBorder(empty, new Color(0.165f, 0.176f, 0.243f));
                var st = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 };
                st.normal.textColor = NovellaSettingsModule.GetTextMuted();
                GUI.Label(empty, ToolLang.Get("No chapters yet — click 'New chapter' above", "Глав ещё нет — нажми «Создать главу» выше"), st);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(280));

            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 50) / 220f));
            int idx = 0;
            GUILayout.BeginHorizontal();
            foreach (var cc in _chapterCache)
            {
                if (idx > 0 && idx % cols == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                }

                DrawChapterCard(cc.Tree, cc.Path, cc.NodeCount);
                if (idx % cols < cols - 1) GUILayout.Space(6);
                idx++;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private void DrawChapterCard(NovellaTree tree, string path, int nodeCount)
        {
            bool isSelected = Story.StartingChapter == tree;

            Rect cardRect = GUILayoutUtility.GetRect(200, 56, GUILayout.MinWidth(200), GUILayout.Height(56), GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(cardRect, isSelected ? new Color(0.15f, 0.20f, 0.28f) : new Color(0.1f, 0.106f, 0.149f));
            DrawRectBorder(cardRect, isSelected ? new Color(0.36f, 0.75f, 0.92f, 1f) : new Color(0.165f, 0.176f, 0.243f));

            if (isSelected)
            {
                EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 3, cardRect.height), NovellaSettingsModule.GetAccentColor());
            }

            var titleStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = NovellaSettingsModule.GetTextColor();
            GUI.Label(new Rect(cardRect.x + 12, cardRect.y + 8, cardRect.width - 24, 16), tree.name, titleStyle);

            var pathStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            pathStyle.normal.textColor = NovellaSettingsModule.GetTextMuted();
            string shortPath = path.Replace("Assets/", "").Replace("NovellaEngine/", "…/");
            GUI.Label(new Rect(cardRect.x + 12, cardRect.y + 26, cardRect.width - 24, 14), shortPath, pathStyle);

            var nodesStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            nodesStyle.normal.textColor = NovellaSettingsModule.GetAccentColor();
            GUI.Label(new Rect(cardRect.x + 12, cardRect.y + 40, cardRect.width - 24, 14), $"{nodeCount} nodes", nodesStyle);

            if (GUI.Button(cardRect, GUIContent.none, GUIStyle.none))
            {
                Story.StartingChapter = tree;
                EditorUtility.SetDirty(Story);
                Repaint();
            }
        }

        private void CreateNewChapterAndAssign()
        {
            string baseDir = "Assets/NovellaEngine/Runtime/Data/Stories";
            if (!System.IO.Directory.Exists(baseDir))
            {
                System.IO.Directory.CreateDirectory(baseDir);
                AssetDatabase.Refresh();
            }
            var newTree = ScriptableObject.CreateInstance<NovellaTree>();
            string desiredName = string.IsNullOrEmpty(Story.Title) ? "Chapter_New" : $"{Story.Title}_Chapter1";
            desiredName = System.Text.RegularExpressions.Regex.Replace(desiredName, @"[\\/:*?""<>|]", "_");
            string path = AssetDatabase.GenerateUniqueAssetPath($"{baseDir}/{desiredName}.asset");
            AssetDatabase.CreateAsset(newTree, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Story.StartingChapter = newTree;
            EditorUtility.SetDirty(Story);
            _chapterCache = null;
            Repaint();
        }

        private static void DrawSectionLabel(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = NovellaSettingsModule.GetTextMuted();
            GUILayout.Label(text, st);
        }

        private static void DrawHelpHint(string text)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.Height(36));
            EditorGUI.DrawRect(r, new Color(0.075f, 0.082f, 0.11f));
            DrawRectBorder(r, new Color(0.36f, 0.75f, 0.92f, 0.4f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), NovellaSettingsModule.GetAccentColor());

            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, r.height - 12), "💡  " + text, st);
            GUILayout.Space(4);
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
