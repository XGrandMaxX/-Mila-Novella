using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using System;

namespace NovellaEngine.Editor
{
    [InitializeOnLoad]
    public class NovellaWelcomeWindow : EditorWindow
    {
        private GUIStyle _tileStyle;
        private GUIStyle _tileTitleStyle;
        private GUIStyle _tileDescStyle;

        private int _tutorialProgress = 1;

        private int _lastSeenProgress = -1;
        private float _unlockAnimStartTime = -99f;
        private int _unlockedStepIndex = -1;

        private int _selectedTutorialIndex = 1;

        public bool _canClose = false;
        public bool _isLaunchingTutorial = false;

        static NovellaWelcomeWindow() { EditorApplication.delayCall += ShowWindowOnFirstLaunch; }

        private static void ShowWindowOnFirstLaunch()
        {
            // Автомиграция при первом запуске Unity: создаём ассеты туториалов если их нет.
            NovellaEngine.Editor.Tutorials.NovellaTutorialMigrator.MigrateIfNeeded(force: false);

            if (!EditorPrefs.GetBool("Novella_HasShownWelcome", false))
            {
                ShowWindow();
            }
            else
            {
                EditorApplication.delayCall += NovellaHubWindow.ShowWindow;
            }
        }

        [MenuItem("Window/Novella Engine/📖 Welcome Tutorial", false, 0)]
        public static void ShowWindow()
        {
            var existing = Resources.FindObjectsOfTypeAll<NovellaWelcomeWindow>();
            foreach (var w in existing) { w._canClose = true; w.Close(); }

            var window = CreateInstance<NovellaWelcomeWindow>();
            window._canClose = false;
            window._isLaunchingTutorial = false;

            // minSize увеличен: 6 урок-плиток + правая панель + hero должны
            // помещаться без скролла. Меньше — съедет правая панель или плитки сожмутся в нечитаемое.
            window.minSize = new Vector2(960, 720);
            window.maxSize = new Vector2(10000, 10000);

            // Вычисляем целевой прямоугольник ОДИН РАЗ при показе окна.
            Rect target = ResolveTutorialTargetRect();

            // ShowUtility вместо ShowPopup: окно МОЖНО перетаскивать, у него есть рамка
            // и заголовок. ShowPopup делает окно бескаркасным и неперемещаемым (что было причиной
            // жалобы "появилось на другом мониторе и не могу перетащить").
            window.position = target;
            window.ShowUtility();
            window.titleContent = new GUIContent(ToolLang.Get("Novella Engine — Tutorial", "Novella Engine — Обучение"));
            window.position = target; // повторяем после Show — Unity иногда сбрасывает позицию.
            window.Focus();
        }

        /// <summary>
        /// Находит "правильный" монитор и возвращает позицию для окна туториала.
        /// Приоритет:
        /// 1) Монитор, на котором сейчас находится главное окно Unity.
        /// 2) Если оно по координатам не валидно (дисплей отключён) — используется
        ///    основной системный дисплей (Display.displays[0] / Screen.currentResolution).
        /// </summary>
        private static Rect ResolveTutorialTargetRect()
        {
            Rect mainPos = EditorGUIUtility.GetMainWindowPosition();

            // Если main window валидный и не нулевой — берём его монитор.
            // Окно туториала будет показано на ТОМ ЖЕ мониторе, что и Unity.
            if (mainPos.width > 200 && mainPos.height > 200)
            {
                // Возвращаем рект с небольшим отступом по краям, чтобы не клипалось taskbar/dock'ом.
                // НЕ вылезаем за границы ректа главного окна.
                return mainPos;
            }

            // Fallback: первый системный дисплей.
            int w = Screen.currentResolution.width;
            int h = Screen.currentResolution.height;
            return new Rect(0, 0, Mathf.Max(800, w), Mathf.Max(600, h));
        }

        private void OnEnable()
        {
            _tutorialProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);

            // ─── Триггер разблок-анимации после CompleteTutorial ───
            // Раньше OnEnable брал _lastSeenProgress = _tutorialProgress, и анимация
            // никогда не срабатывала после возврата из туториала (окно создавалось заново).
            // Теперь храним последний «увиденный пользователем» прогресс в EditorPrefs.
            // Если он меньше текущего — значит пока окна не было, юзер прошёл урок.
            // Запускаем красивую разблок-анимацию для нового шага.
            int persistedLastSeen = EditorPrefs.GetInt("Novella_LastSeenProgress", _tutorialProgress);

            if (_tutorialProgress > persistedLastSeen)
            {
                // Новый урок открылся! Запускаем анимацию через 0.4с после отрисовки —
                // даёт юзеру долю секунды на ориентацию в окне перед эффектом.
                _unlockedStepIndex = _tutorialProgress;
                _unlockAnimStartTime = (float)EditorApplication.timeSinceStartup + 0.4f;
                _selectedTutorialIndex = _tutorialProgress;
            }
            else
            {
                _selectedTutorialIndex = Mathf.Clamp(_tutorialProgress, 1, 6);
            }
            // Всегда выставляем равным current, чтобы ветка в OnGUI
            // (currentProgress > _lastSeenProgress) не клобила _unlockAnimStartTime
            // и не теряла наш delay в 0.4с.
            _lastSeenProgress = _tutorialProgress;

            // Включаем приём mouseMove-событий: без этого hover-анимация плиток
            // срабатывает только при кликах/нажатиях клавиш, а не при простом наведении.
            // Также подписываемся на update — для непрерывной плавной анимации (shake/glow).
            wantsMouseMove = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private double _lastRepaintTime;
        private void OnEditorUpdate()
        {
            // Постоянный 30-FPS Repaint — нужен для:
            //   • плавной cyan-волны в hero (всегда работает)
            //   • shake-анимации плиток при hover
            //   • разблок-анимации (lock pop + cyan rings)
            // Throttle 33ms (~30fps) — комфортно для глаза, не грузит CPU.
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaintTime < 0.033) return;
            _lastRepaintTime = now;
            Repaint();
        }

        private bool _isMouseOverAnyTile;

        private void OnDestroy()
        {
            NovellaTutorialManager.ForceStopTutorial();

            if (_isLaunchingTutorial) return;

            if (!_canClose)
            {
                EditorApplication.delayCall += ShowWindow;
            }
            else if (!EditorPrefs.GetBool("Novella_HasShownWelcome", false))
            {
                EditorApplication.delayCall += NovellaHubWindow.ShowWindow;
            }
        }

        private void OnGUI()
        {
            // Position-lock убран намеренно: пользователь должен иметь возможность
            // перетащить окно на другой монитор / в другой угол экрана. Раньше окно
            // снапилось обратно на main-window каждый кадр — это бесило.
            // Стартовая позиция всё ещё ResolveTutorialTargetRect, выставляется
            // один раз при ShowWindow().

            // На каждый Layout-кадр сбрасываем флаг "над плиткой" — DrawTileInteractive установит его,
            // если курсор реально над одной из плиток.
            if (Event.current.type == EventType.Layout) _isMouseOverAnyTile = false;

            if (Event.current.type == EventType.Layout && EditorWindow.focusedWindow == this)
            {
                if (NovellaTutorialManager.IsTutorialActive) NovellaTutorialManager.ForceStopTutorial();
            }

            int currentProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);
            if (_lastSeenProgress == -1) _lastSeenProgress = currentProgress;

            if (currentProgress > _lastSeenProgress)
            {
                _unlockedStepIndex = currentProgress;
                _unlockAnimStartTime = (float)EditorApplication.timeSinceStartup;
                _lastSeenProgress = currentProgress;
                _selectedTutorialIndex = currentProgress;
            }

            _tutorialProgress = currentProgress;

            float animT = (float)EditorApplication.timeSinceStartup - _unlockAnimStartTime;
            if (_unlockedStepIndex != -1)
            {
                if (animT >= 2.5f)
                {
                    // Анимация закончилась — фиксируем «увиденный» прогресс,
                    // чтобы при следующем открытии окна анимация не повторилась.
                    EditorPrefs.SetInt("Novella_LastSeenProgress", _tutorialProgress);
                    _unlockedStepIndex = -1;
                    _lastSeenProgress = _tutorialProgress;
                }
            }

            InitStyles();

            // ─── Базовый фон окна — глубокий almost-black как у NovellaHubWindow ───
            // Сначала заливаем всю площадь, потом сверху рисуем toolbar/hero/панели.
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height),
                new Color(0.075f, 0.078f, 0.106f, 1f));

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("↺ " + ToolLang.Get("Restart Tutorial", "Перепройти туториал"), EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                _tutorialProgress = 1; _lastSeenProgress = 1; _unlockedStepIndex = -1; _selectedTutorialIndex = 1;
                EditorPrefs.SetInt("Novella_TutorialProgress", 1);
                // Сбрасываем «увиденный» прогресс синхронно с реальным,
                // чтобы при следующем разблоке анимация сработала корректно.
                EditorPrefs.SetInt("Novella_LastSeenProgress", 1);
                EditorPrefs.SetBool("Novella_HasShownWelcome", false);
                EditorPrefs.SetBool("Novella_Tut_SceneManager", false);
                EditorPrefs.SetBool("Novella_Tut_CharacterEditor", false);
                EditorPrefs.SetBool("Novella_Tut_GraphEditor", false);
                EditorPrefs.SetBool("Novella_Tut_DLCManager", false);
                EditorPrefs.SetBool("Novella_Tut_UIEditor", false);
                EditorPrefs.SetBool("Novella_Tut_InteractiveLesson", false);
                NovellaTutorialManager.ForceStopTutorial();
            }

            GUILayout.FlexibleSpace();

            string langBtnText = ToolLang.IsRU ? "EN" : "RU";
            if (GUILayout.Button(langBtnText, EditorStyles.toolbarButton, GUILayout.Width(40))) { ToolLang.Toggle(); }

            // Exit Welcome — даёт пользователю явный путь закрыть окно в любой
            // момент, не дожидаясь конца обучения. Прогресс при этом не теряется
            // (EditorPrefs живут до Restart Tutorial). Перепройти окно можно через
            // меню Window → Novella Engine → 📖 Welcome Tutorial.
            if (GUILayout.Button("✕ " + ToolLang.Get("Exit", "Закрыть"),
                EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                _canClose = true;
                Close();
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            // ─── Hero section — Novella Hub-style banner ───
            // Тёмная навигационная плашка с тонкой cyan-подсветкой снизу
            // (как разделитель top-bar'а в NovellaHubWindow). Палитра 1:1 с Hub'ом.
            const float heroH = 110f;
            Rect heroRect = GUILayoutUtility.GetRect(0, heroH, GUILayout.ExpandWidth(true));

            // Базовая заливка — точный card-tone из Hub'а (0.10, 0.106, 0.149).
            EditorGUI.DrawRect(heroRect, new Color(0.10f, 0.106f, 0.149f, 1f));

            // Лёгкий вертикальный shading сверху — придаёт глубину без отрыва от Hub-палитры.
            // hSteps = высота в px → каждый шаг ровно 1px, banding не виден.
            int hSteps = Mathf.Max(40, Mathf.CeilToInt(heroRect.height));
            for (int hi = 0; hi < hSteps; hi++)
            {
                float t = hi / (float)(hSteps - 1);
                // 18% затемнение сверху → 0% снизу (Hub-base уже тёмный, не пересвечиваем)
                float a = (1f - t) * 0.18f;
                EditorGUI.DrawRect(new Rect(heroRect.x, heroRect.y + heroRect.height * t,
                    heroRect.width, heroRect.height / hSteps + 1),
                    new Color(0f, 0f, 0f, a));
            }

            // ─── Анимированная cyan-волна (бесшовный seamless loop) ───
            // Пятно начинается ОФФ-СКРИНОМ слева (waveX = -0.4 от ширины hero),
            // плавно проходит весь hero и уходит ОФФ-СКРИНОМ вправо (1.4).
            // Когда phase оборачивается с 1.0 → 0.0, waveX скачком возвращается с
            // 1.4 → -0.4 — оба значения за пределами видимой области, поэтому
            // никакого визуального «дёрга» loop'а.
            // Banding убран: количество полосок теперь = ширине в пикселях / 2,
            // т.е. полоса = 2px и не видна глазу.
            float now = (float)EditorApplication.timeSinceStartup;
            const float wavePeriod = 9f;
            float wavePhase = (now % wavePeriod) / wavePeriod;
            // Ease-in-out cubic — мягкий старт и конец, бóльшая часть времени плавный мид-движение.
            float ease = wavePhase < 0.5f
                ? 4f * wavePhase * wavePhase * wavePhase
                : 1f - Mathf.Pow(-2f * wavePhase + 2f, 3f) / 2f;
            float waveX = -0.4f + 1.8f * ease;
            float waveCenterX = heroRect.x + heroRect.width * waveX;
            float waveRadius = heroRect.width * 0.32f;

            // Рисуем пятно горизонтально — каждая полоска 2px шириной, banding
            // не виден. Cosine-shaped intensity (плавное пятно с мягкими краями).
            int waveStrips = Mathf.Max(120, Mathf.CeilToInt(heroRect.width / 2f));
            float stripW = heroRect.width / waveStrips + 1f;
            for (int ws = 0; ws < waveStrips; ws++)
            {
                float st = ws / (float)(waveStrips - 1);
                float stripX = heroRect.x + heroRect.width * st;
                float dist = Mathf.Abs(stripX - waveCenterX) / waveRadius;
                if (dist > 1f) continue;
                float intensity = 0.5f * (Mathf.Cos(dist * Mathf.PI) + 1f);
                float a = intensity * 0.20f;
                EditorGUI.DrawRect(new Rect(stripX, heroRect.y, stripW, heroRect.height),
                    new Color(0.36f, 0.75f, 0.92f, a));
            }

            // Постоянный лёгкий cyan-glow в самом левом углу — фирменное
            // «свечение логотипа» Hub'а (не зависит от анимации).
            // Высокий glowSteps убирает видимое стрипование.
            int glowSteps = 30;
            for (int g = 0; g < glowSteps; g++)
            {
                float t = g / (float)(glowSteps - 1);
                float a = (1f - t) * 0.04f;
                EditorGUI.DrawRect(new Rect(heroRect.x, heroRect.y,
                    heroRect.width * (0.35f - t * 0.25f), heroRect.height),
                    new Color(0.36f, 0.75f, 0.92f, a));
            }

            // Постоянный repaint пока окно фокусировано — анимация работает.
            if (EditorWindow.focusedWindow == this) Repaint();

            // Border снизу — точно как у top-bar'а Hub'а (тонкая навигационная линия).
            EditorGUI.DrawRect(new Rect(heroRect.x, heroRect.yMax - 1, heroRect.width, 1),
                new Color(0.165f, 0.176f, 0.243f, 1f));
            // Cyan-акцент тонкой полосой над border'ом — фирменная фишка.
            EditorGUI.DrawRect(new Rect(heroRect.x, heroRect.yMax - 3, heroRect.width, 2),
                new Color(0.36f, 0.75f, 0.92f, 0.55f));

            // Заголовок — крупный, белый, чуть левее центра.
            var heroTitleSt = new GUIStyle(EditorStyles.label) {
                fontSize = 26, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(heroRect.x, heroRect.y + 18, heroRect.width, 36),
                ToolLang.Get("Welcome to Novella Engine 🚀", "Добро пожаловать в Novella Engine 🚀"), heroTitleSt);

            // Подзаголовок — Hub-muted (тот же тон что текст-описания в hub-карточках).
            var heroSubSt = new GUIStyle(EditorStyles.label) {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = new Color(0.62f, 0.70f, 0.78f, 1f) }
            };
            GUI.Label(new Rect(heroRect.x + 40, heroRect.y + 56, heroRect.width - 80, 40),
                ToolLang.Get("Your visual novel journey starts here. Click any unlocked block to begin a lesson.",
                             "Ваш путь в создании визуальных новелл начинается здесь. Кликни по любому открытому блоку чтобы начать урок."), heroSubSt);

            // ─── Абсолютная компоновка двух колонок под hero ───
            // Раньше использовался BeginHorizontal+вложенные BeginVertical+BeginArea —
            // это ломало координаты правой колонки (она наследовала огромную высоту
            // скроллвью слева, и BeginArea рисовался поверх левой части). Теперь
            // каждая колонка — отдельный BeginArea с явным rect'ом.
            // topY с увеличенным gap (28px) — даёт «воздух» между hero и плитками.
            float topY = heroRect.yMax + 28f;
            float bottomPad = 12f;
            float remainingH = position.height - topY - bottomPad;
            if (remainingH < 200f) remainingH = 200f;

            float rightW = Mathf.Clamp(position.width * 0.36f, 300f, 480f);
            float gap = 18f;
            float sidePad = 18f;  // увеличено с 10 чтобы glow плиток не упирался в край окна
            float leftW = position.width - rightW - gap - sidePad * 2f;
            if (leftW < 300f) { leftW = 300f; rightW = position.width - leftW - gap - sidePad * 2f; }

            Rect leftRect = new Rect(sidePad, topY, leftW, remainingH);
            Rect rightRect = new Rect(leftRect.xMax + gap, topY, rightW, remainingH);

            // Сначала рисуем фон правой панели (чтобы он был ПОД содержимым).
            DrawRightPanelChrome(rightRect);

            // Левая колонка — workflow tiles, БЕЗ scrollview.
            // Все 6 плиток обязаны помещаться в доступную высоту, иначе урок
            // невозможно «увидеть целиком», что ломает UX обучения. Высота плиток
            // адаптивно ужимается под leftRect.height в DrawVisualWorkflow.
            GUILayout.BeginArea(leftRect);
            DrawVisualWorkflow(animT, leftRect.width, leftRect.height);
            GUILayout.EndArea();

            // Правая колонка — содержимое поверх chrome.
            // Внутренний отступ 18px со всех сторон. Передаём ширину явно,
            // чтобы расчёт высоты hint-карточки не сломался во время Layout-pass.
            float innerPadding = 18f;
            float innerW = rightRect.width - innerPadding * 2f;
            GUILayout.BeginArea(new Rect(rightRect.x + innerPadding, rightRect.y + innerPadding,
                innerW, rightRect.height - innerPadding * 2f));
            DrawRightSidePanel(innerW);
            GUILayout.EndArea();
        }

        /// <summary>
        /// Helper для тонкого 1px бордюра по периметру rect'а — точно как
        /// DrawRectBorder в NovellaHubWindow, но локально, без cross-class зависимостей.
        /// </summary>
        private static void DrawCardBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y,        1, r.height), c);
        }

        // Рисует правую панель в стиле Hub-карточки: тёмный navy-фон + 1px border.
        // Раньше была 3px cyan вертикальная полоса слева на всю высоту — это
        // визуально конфликтовало с cyan-stripe внутри hint-карточки и
        // выглядело как «полоска вылезает за плашку». Теперь — только тонкая
        // cyan-акцентная плашка в самом верху панели (как у hero accent line).
        private void DrawRightPanelChrome(Rect r)
        {
            // Базовый Hub-card-fill (0.10, 0.106, 0.149).
            EditorGUI.DrawRect(r, new Color(0.10f, 0.106f, 0.149f, 1f));
            // Hub-border (0.165, 0.176, 0.243).
            DrawCardBorder(r, new Color(0.165f, 0.176f, 0.243f, 1f));
            // Cyan-акцент — короткая горизонтальная полоса в верхней части,
            // не на всю высоту. Согласован с hero-accent.
            EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, 2),
                new Color(0.36f, 0.75f, 0.92f, 0.85f));
        }

        private void DrawRightSidePanel(float panelW)
        {
            string title = GetTutTitle(_selectedTutorialIndex);
            string desc = GetTutDesc(_selectedTutorialIndex);
            string extraInfo = GetTutExtraInfo(_selectedTutorialIndex);

            // Top padding — раньше тут был STEP-badge (22px). После его удаления
            // заголовок «1. Сцены и Меню» съехал слишком вверх и упирался в cyan
            // accent line на крышке панели. 14px воздуха возвращают визуальный баланс.
            GUILayout.Space(14);

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 20, wordWrap = true,
                normal = { textColor = Color.white }
            });

            // Тонкий cyan-разделитель под заголовком (fade слева→справа).
            Rect divRect = GUILayoutUtility.GetRect(panelW, 2, GUILayout.Width(panelW));
            for (int i = 0; i < 24; i++)
            {
                float t = i / 23f;
                float fade = 1f - Mathf.Pow(t, 1.5f);
                Color c = new Color(0.36f, 0.75f, 0.92f, 0.85f * fade);
                EditorGUI.DrawRect(new Rect(divRect.x + divRect.width * t, divRect.y,
                    divRect.width / 24f + 1, 2), c);
            }

            GUILayout.Space(10);

            // Description — Hub-text-color (не белый, мягче).
            var descSt = new GUIStyle(EditorStyles.wordWrappedLabel) {
                fontSize = 13,
                normal = { textColor = new Color(0.78f, 0.82f, 0.88f, 1f) }
            };
            GUILayout.Label(desc, descSt);
            GUILayout.Space(10);

            // ─── Hint-карточка в Hub-tip-стиле — увеличенная и с воздухом ───
            // Большой внутренний padding по всем сторонам (16px x 14px),
            // высота +35% относительно текста для воздуха. Cyan-stripe слева
            // c вертикальным insets чтобы не казалось «полоска вылезает за плашку».
            const float hintPadLR = 16f;   // лев/прав inner padding
            const float hintPadTB = 14f;   // верх/низ inner padding
            const float stripeW   = 4f;    // ширина левой cyan-полосы
            const float stripeInsetTB = 8f; // отступ полосы сверху/снизу — чтобы не упиралась в углы

            var hintTextSt = new GUIStyle(EditorStyles.wordWrappedLabel) {
                fontSize = 13,                    // чуть крупнее (было 12)
                richText = true,
                normal = { textColor = new Color(0.82f, 0.86f, 0.92f, 1f) }
            };
            float hintInnerW = panelW - hintPadLR * 2f - stripeW;
            float textH = hintTextSt.CalcHeight(new GUIContent(extraInfo), hintInnerW);
            float hintH = textH + hintPadTB * 2f;

            Rect hintRect = GUILayoutUtility.GetRect(panelW, hintH, GUILayout.Width(panelW));

            // Карточка фон.
            EditorGUI.DrawRect(hintRect, new Color(0.075f, 0.082f, 0.11f, 1f));
            // 1px cyan-tinted border по периметру.
            DrawCardBorder(hintRect, new Color(0.36f, 0.75f, 0.92f, 0.40f));
            // Cyan stripe слева — НЕ во всю высоту, с insets, чтобы выглядело аккуратно.
            EditorGUI.DrawRect(new Rect(
                hintRect.x + 1, hintRect.y + stripeInsetTB,
                stripeW, hintRect.height - stripeInsetTB * 2f),
                new Color(0.36f, 0.75f, 0.92f, 1f));

            // Текст внутри карточки с большим внутренним отступом.
            GUI.Label(new Rect(
                hintRect.x + stripeW + hintPadLR,
                hintRect.y + hintPadTB,
                hintInnerW,
                textH),
                extraInfo, hintTextSt);

            GUILayout.Space(20);

            bool isUnlocked = _tutorialProgress >= _selectedTutorialIndex;

            // ─── Главная кнопка PLAY TUTORIAL — Hub Play-green ───
            // Цвет 1:1 с test/play кнопкой Hub: (0.30, 0.85, 0.45).
            string playLabel = "▶  " + ToolLang.Get("PLAY TUTORIAL", "НАЧАТЬ УРОК");
            if (DrawBigGradientButton(playLabel, 56f, isUnlocked,
                new Color(0.32f, 0.86f, 0.46f), new Color(0.18f, 0.55f, 0.28f), true))
            {
                PlayTutorial(_selectedTutorialIndex);
            }

            if (!isUnlocked)
            {
                GUILayout.Space(6);
                GUILayout.Label(ToolLang.Get("🔒 Complete previous steps to unlock", "🔒 Пройдите предыдущие шаги для разблокировки"),
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 11,
                        normal = { textColor = new Color(0.62f, 0.70f, 0.78f, 1f) } });
            }

            GUILayout.FlexibleSpace();

            bool hasCompletedAll = EditorPrefs.GetBool("Novella_Tut_InteractiveLesson", false);

            // ─── «Открыть Novella Hub» — доступна только когда туториал реально пройден ───
            // (Раньше блок показывался тоже только после прохождения. Теперь логика
            // «прошёл всё»→Hub оставлена, а отдельный «Пропустить всё» — ниже,
            // доступен в любой момент с подтверждением.)
            if (hasCompletedAll)
            {
                if (DrawBigGradientButton("🚀  " + ToolLang.Get("OPEN NOVELLA HUB", "ОТКРЫТЬ NOVELLA HUB"), 46f, true,
                    new Color(0.36f, 0.75f, 0.92f), new Color(0.18f, 0.45f, 0.62f), true))
                {
                    _canClose = true;
                    Close();
                    NovellaHubWindow.ShowWindow();
                    GUIUtility.ExitGUI();
                }

                GUILayout.Space(10);
                EditorGUI.BeginChangeCheck();
                bool dontShowAgain = EditorPrefs.GetBool("Novella_HasShownWelcome", false);
                dontShowAgain = GUILayout.Toggle(dontShowAgain,
                    ToolLang.Get(" Do not show this window on startup", " Больше не показывать это окно при запуске Unity"),
                    new GUIStyle(EditorStyles.toggle) { fontSize = 11,
                        normal = { textColor = new Color(0.62f, 0.70f, 0.78f, 1f) },
                        onNormal = { textColor = new Color(0.62f, 0.70f, 0.78f, 1f) } });
                if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool("Novella_HasShownWelcome", dontShowAgain);
            }
            else
            {
                // ─── «Пропустить всё обучение» — для опытных юзеров ───
                // 6 уроков для абсолютного новичка — много. Если пользователь
                // уже знаком с движком (например, переустановил Unity), даём
                // выйти. С двойным подтверждением, чтобы случайно не нажали.
                if (DrawBigGradientButton("⏭  " + ToolLang.Get("SKIP ALL TUTORIALS", "ПРОПУСТИТЬ ВСЁ ОБУЧЕНИЕ"), 38f, true,
                    new Color(0.30f, 0.32f, 0.38f), new Color(0.18f, 0.20f, 0.24f), false))
                {
                    bool ok = EditorUtility.DisplayDialog(
                        ToolLang.Get("Skip all tutorials?", "Пропустить всё обучение?"),
                        ToolLang.Get(
                            "You'll go straight to Novella Studio without learning the basics. " +
                            "If you're new — please reconsider; the tutorials take ~10 minutes total " +
                            "and save hours of confusion later.\n\n" +
                            "You can return any time — there's a 📖 button in the left sidebar of Novella Studio.",
                            "Ты сразу попадёшь в Novella Studio, минуя обучение. " +
                            "Если ты новичок — подумай ещё раз; вся обучалка занимает ~10 минут " +
                            "и экономит часы блужданий потом.\n\n" +
                            "Вернуться можно в любой момент — в левом сайдбаре Novella Studio есть кнопка 📖."),
                        ToolLang.Get("Yes, skip", "Да, пропустить"),
                        ToolLang.Get("Cancel", "Отмена"));
                    if (ok)
                    {
                        // Помечаем все уроки как пройденные + ставим прогресс на финал.
                        EditorPrefs.SetBool("Novella_Tut_SceneManager", true);
                        EditorPrefs.SetBool("Novella_Tut_CharacterEditor", true);
                        EditorPrefs.SetBool("Novella_Tut_GraphEditor", true);
                        EditorPrefs.SetBool("Novella_Tut_DLCManager", true);
                        EditorPrefs.SetBool("Novella_Tut_UIEditor", true);
                        EditorPrefs.SetBool("Novella_Tut_InteractiveLesson", true);
                        EditorPrefs.SetInt("Novella_TutorialProgress", 6);
                        EditorPrefs.SetInt("Novella_LastSeenProgress", 6);
                        EditorPrefs.SetBool("Novella_HasShownWelcome", true);

                        _canClose = true;
                        Close();
                        NovellaHubWindow.ShowWindow();
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.Space(8);

                // Подсказка о progress-системе (мягкая, не запрещающая).
                var hintSt = new GUIStyle(EditorStyles.wordWrappedLabel) {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.55f, 0.60f, 0.68f, 1f) }
                };
                string hint = ToolLang.Get(
                    "Pass each lesson to unlock the next. Tutorials take ~10 min total.",
                    "Проходи уроки по очереди — следующий открывается после предыдущего.");
                GUILayout.Label(hint, hintSt);
            }

            GUILayout.Space(6);
        }

        // Кастомная кнопка с градиентом и hover/press состояниями.
        // glowOnHover = подсветка вокруг при наведении.
        private bool DrawBigGradientButton(string label, float h, bool enabled,
                                           Color top, Color bottom, bool glowOnHover)
        {
            Rect r = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true));
            Event e = Event.current;
            bool hover = enabled && r.Contains(e.mousePosition) && !NovellaTutorialManager.IsTutorialActive;
            bool pressed = hover && (e.type == EventType.MouseDown && e.button == 0);

            // Мягкая внешняя подсветка под кнопкой (только если enabled и hover).
            if (hover && glowOnHover)
            {
                _isMouseOverAnyTile = true; // чтобы Repaint крутился
                for (int g = 3; g >= 0; g--)
                {
                    float inflate = 1f + g * 3f;
                    float a = 0.32f * Mathf.Pow(0.55f, g);
                    Color c = top; c.a = a;
                    EditorGUI.DrawRect(new Rect(r.x - inflate, r.y - inflate,
                        r.width + inflate * 2f, r.height + inflate * 2f), c);
                }
            }

            // Лёгкое «вдавливание» на pressed.
            Rect bodyRect = pressed ? new Rect(r.x, r.y + 1, r.width, r.height - 1) : r;

            // Если выключен — приглушаем цвета.
            if (!enabled)
            {
                top = new Color(top.r * 0.35f, top.g * 0.35f, top.b * 0.35f, 1f);
                bottom = new Color(bottom.r * 0.35f, bottom.g * 0.35f, bottom.b * 0.35f, 1f);
            }
            else if (hover)
            {
                top = new Color(Mathf.Min(1, top.r * 1.12f), Mathf.Min(1, top.g * 1.12f), Mathf.Min(1, top.b * 1.12f), 1f);
                bottom = new Color(Mathf.Min(1, bottom.r * 1.12f), Mathf.Min(1, bottom.g * 1.12f), Mathf.Min(1, bottom.b * 1.12f), 1f);
            }

            // Вертикальный градиент.
            int steps = 18;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                Color row = Color.Lerp(top, bottom, t);
                float gy = bodyRect.y + bodyRect.height * t;
                float gh = bodyRect.height / steps + 1;
                EditorGUI.DrawRect(new Rect(bodyRect.x, gy, bodyRect.width, gh), row);
            }

            // Тонкая верхняя «глянцевая» полоска для объёма.
            EditorGUI.DrawRect(new Rect(bodyRect.x, bodyRect.y, bodyRect.width, 1),
                new Color(1f, 1f, 1f, enabled ? 0.18f : 0.06f));
            // И тонкая нижняя тень.
            EditorGUI.DrawRect(new Rect(bodyRect.x, bodyRect.yMax - 1, bodyRect.width, 1),
                new Color(0f, 0f, 0f, 0.45f));

            // Текст.
            var st = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = h >= 50f ? 16 : 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f) }
            };
            // Лёгкая «тень» текста для читаемости.
            var shadowSt = new GUIStyle(st) { normal = { textColor = new Color(0, 0, 0, enabled ? 0.55f : 0.25f) } };
            GUI.Label(new Rect(bodyRect.x + 1, bodyRect.y + 2, bodyRect.width, bodyRect.height), label, shadowSt);
            GUI.Label(bodyRect, label, st);

            // Невидимая кнопка-приёмник кликов.
            if (enabled && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                return true;
            }
            return false;
        }

        private string GetTutTitle(int index)
        {
            if (index == 1) return ToolLang.Get("1. Scenes & Menu", "1. Сцены и Меню");
            if (index == 2) return ToolLang.Get("2. Actors & Variables", "2. Персонажи и Переменные");
            if (index == 3) return ToolLang.Get("3. Graph Editor", "3. Редактор Графа");
            if (index == 4) return ToolLang.Get("4. DLC Modules", "4. Модули DLC");
            if (index == 5) return ToolLang.Get("5. UI Forge", "5. UI Кузница");
            if (index == 6) return ToolLang.Get("6. Interactive Lesson", "6. Интерактивный Урок");
            return "";
        }

        private string GetTutDesc(int index)
        {
            if (index == 1) return ToolLang.Get("Manage Unity Scenes for Gameplay and Main Menu.", "Управление Unity сценами для геймплея и меню.");
            if (index == 2) return ToolLang.Get("Define Actors, multi-layered Paper Dolls, and global variables.", "Актеры, многослойные Paper Dolls (одежда/эмоции) и глобальные переменные.");
            if (index == 3) return ToolLang.Get("Write your story! Connect Dialogue, Choices, Audio, Logic, and many other interesting nodes.", "Пишите историю! Соединяйте ноды Диалогов, Выборов, Аудио и Логики в единый сюжет.");
            if (index == 4) return ToolLang.Get("Expand your engine with Downloadable Content! Add new mechanics effortlessly.", "Расширяйте возможности движка с помощью DLC! Легко добавляйте новые механики.");
            if (index == 5) return ToolLang.Get("Style Dialogue frames, customize Main Menu, and configure Character Wardrobe flow.", "Стилизация диалогов, Главное Меню и элементы графического интерфейса.");
            if (index == 6) return ToolLang.Get("Launch the learning graph to see how all systems work together in a real scene!", "Запустите обучающий граф, чтобы своими глазами увидеть, как все системы работают вместе на реальной сцене!");
            return "";
        }

        private string GetTutExtraInfo(int index)
        {
            if (index == 1) return ToolLang.Get("💡 In this lesson, we will create the Game Canvas and prepare the Main Menu structure.", "💡 В этом уроке мы создадим игровой Canvas и подготовим структуру Главного Меню.");
            if (index == 2) return ToolLang.Get("💡 We will create our first character, set up their emotions, and add a global variable.", "💡 Мы создадим первого персонажа, настроим его эмоции и добавим global переменную.");
            if (index == 3) return ToolLang.Get("💡 You will learn how to connect nodes, use auto-layout, and create story branches.", "💡 Вы узнаете, как связывать ноды, использовать авто-выравнивание и делать сюжетные ветвления.");
            if (index == 4) return ToolLang.Get("💡 We will explore the DLC Manager, enable a module, and see how to safely delete it.", "💡 Мы изучим Менеджер DLC, включим модуль и узнаем, как безопасно его удалить.");
            if (index == 5) return ToolLang.Get("💡 Time to make things pretty! We'll tweak colors, fonts, and dialogue box styles.", "💡 Время навести красоту! Мы изменим цвета, шрифты и стиль окна диалогов.");
            if (index == 6) return ToolLang.Get("💡 The final step. A massive interactive example showing the engine in action.", "💡 Финальный шаг. Огромный интерактивный пример, показывающий движок в действии.");
            return "";
        }

        private void PlayTutorial(int index)
        {
            _isLaunchingTutorial = true;
            _canClose = true;
            Close();

            if (index == 1) { NovellaSceneManagerModule.ShowWindow(); NovellaTutorialManager.StartTutorial("SceneManager"); }
            else if (index == 2) { NovellaCharacterEditorModule.OpenWindow(); NovellaTutorialManager.StartTutorial("CharacterEditor"); }
            else if (index == 3)
            {
                var graphAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>("Assets/NovellaEngine/Tutorials/01_TutorialGraph.asset");
                if (graphAsset != null)
                {
                    NovellaGraphWindow.OpenGraphWindow(graphAsset);
                    var gw = GetWindow<NovellaGraphWindow>("Novella Editor");
                    gw.Focus();
                    EditorApplication.delayCall += () => {
                        var isInspOpenField = gw.GetType().GetField("_isInspectorOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (isInspOpenField != null) isInspOpenField.SetValue(gw, false);

                        var rightPanelField = gw.GetType().GetField("_rightPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (rightPanelField != null)
                        {
                            var rightPanel = rightPanelField.GetValue(gw) as UnityEngine.UIElements.VisualElement;
                            if (rightPanel != null) rightPanel.style.width = 0;
                        }
                        NovellaTutorialManager.StartTutorial("GraphEditor");
                    };
                }
                else EditorUtility.DisplayDialog("Error", "Tutorial Graph Asset not found!", "OK");
            }
            else if (index == 4)
            {
                var graphAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>("Assets/NovellaEngine/Tutorials/01_TutorialGraph.asset");
                if (graphAsset != null)
                {
                    NovellaGraphWindow.OpenGraphWindow(graphAsset);
                    GetWindow<NovellaGraphWindow>("Novella Editor").Focus();
                    EditorApplication.delayCall += () => NovellaTutorialManager.StartTutorial("DLCManager");
                }
                else EditorUtility.DisplayDialog("Error", "Tutorial Graph Asset not found!", "OK");
            }
            else if (index == 5) { NovellaUIForge.ShowWindow(); NovellaTutorialManager.StartTutorial("UIEditor"); }
            else if (index == 6)
            {
                var lessonAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>("Assets/NovellaEngine/Tutorials/02_InteractiveLesson.asset");
                if (lessonAsset != null)
                {
                    NovellaGraphWindow.OpenGraphWindow(lessonAsset);
                    GetWindow<NovellaGraphWindow>("Novella Editor").Focus();
                    EditorApplication.delayCall += () => NovellaTutorialManager.StartTutorial("InteractiveLesson");
                }
                else EditorUtility.DisplayDialog("Error", "Interactive Lesson Asset not found!", "OK");
            }
        }

        private void InitStyles()
        {
            if (_tileStyle == null)
            {
                _tileStyle = new GUIStyle(EditorStyles.helpBox);
                _tileTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, wordWrap = true };
                _tileDescStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 13, normal = { textColor = new Color(0.75f, 0.75f, 0.75f) } };
            }
        }

        private float CalculateTileHeight(string title, string desc, float width)
        {
            float titleWidth = width - 80f;
            float descWidth = width - 30f;

            return Mathf.Max(150f, 20f + _tileTitleStyle.CalcHeight(new GUIContent(title), titleWidth) + 15f + _tileDescStyle.CalcHeight(new GUIContent(desc), descWidth) + 35f);
        }

        private void DrawVisualWorkflow(float animT, float panelWidth, float availableH)
        {
            float tileW = (panelWidth - 100f) / 2f;
            if (tileW < 170f) tileW = 170f;
            float gapX = 30f;

            // Компактный zigzag-layout: 3 ряда по 2 плитки.
            // Из формулы totalH = topPad + bottomPad + 2*zigzagOffset + 2*rowGap + 3*tileH
            // решаем для tileH чтобы поместиться в availableH (без скролла).
            const float topPad      = 8f;
            const float bottomPad   = 8f;
            const float zigzagOffset = 40f;  // вертикальное смещение между t1↔t2 и т.п.
            const float rowGap       = 18f;  // вертикальный gap между рядами

            float tileH = (availableH - topPad - bottomPad - 2 * zigzagOffset - 2 * rowGap) / 3f;
            tileH = Mathf.Clamp(tileH, 78f, 170f);

            float t1Y = topPad;
            float t2Y = t1Y + zigzagOffset;
            float t3Y = t2Y + tileH + rowGap;
            float t4Y = t3Y + zigzagOffset;
            float t5Y = t4Y + tileH + rowGap;
            float t6Y = t5Y + zigzagOffset;

            float totalHeight = t6Y + tileH + bottomPad;
            Rect workArea = GUILayoutUtility.GetRect(panelWidth, totalHeight, GUILayout.Width(panelWidth));

            float col1X = workArea.x + 20f;
            float col2X = col1X + tileW + gapX;

            Rect t1 = new Rect(col1X, workArea.y + t1Y, tileW, tileH);
            Rect t2 = new Rect(col2X, workArea.y + t2Y, tileW, tileH);
            Rect t3 = new Rect(col1X, workArea.y + t3Y, tileW, tileH);
            Rect t4 = new Rect(col2X, workArea.y + t4Y, tileW, tileH);
            Rect t5 = new Rect(col1X, workArea.y + t5Y, tileW, tileH);
            Rect t6 = new Rect(col2X, workArea.y + t6Y, tileW, tileH);

            // Кривизна стрелок зависит от ширины — на узких окнах уменьшаем.
            float curve = Mathf.Clamp(tileW * 0.45f, 60f, 110f);

            // ─── Список всех 5 коннекторов ───
            // Сначала рисуем визуал (line + glow + arrowhead) для каждого по отдельности.
            // Потом — единый поток пульсаций через цепочку открытых стрелок.
            var arrows = new System.Collections.Generic.List<ArrowSegment>
            {
                MakeArrowSeg(new Vector2(t1.xMax, t1.y + tileH / 2), new Vector2(t2.xMin, t2.y + tileH / 2), Vector2.right, Vector2.left, curve, _tutorialProgress >= 2),
                MakeArrowSeg(new Vector2(t2.center.x, t2.yMax), new Vector2(t3.center.x, t3.yMin), Vector2.down,  Vector2.up,   curve, _tutorialProgress >= 3),
                MakeArrowSeg(new Vector2(t3.xMax, t3.y + tileH / 2), new Vector2(t4.xMin, t4.y + tileH / 2), Vector2.right, Vector2.left, curve, _tutorialProgress >= 4),
                MakeArrowSeg(new Vector2(t4.center.x, t4.yMax), new Vector2(t5.center.x, t5.yMin), Vector2.down,  Vector2.up,   curve, _tutorialProgress >= 5),
                MakeArrowSeg(new Vector2(t5.xMax, t5.y + tileH / 2), new Vector2(t6.xMin, t6.y + tileH / 2), Vector2.right, Vector2.left, curve, _tutorialProgress >= 6),
            };

            // Рисуем сам визуал каждой стрелки (БЕЗ пульсов).
            foreach (var seg in arrows) DrawArrowVisual(seg);

            // А теперь — поток пульсов через цепочку открытых стрелок.
            // 2 пульса с разнесённой фазой бегут от первого открытого до последнего.
            var unlockedChain = new System.Collections.Generic.List<ArrowSegment>();
            foreach (var s in arrows) if (s.IsUnlocked) unlockedChain.Add(s);
            DrawArrowChainPulses(unlockedChain);

            DrawTileInteractive(t1, 1, "🛠", GetTutTitle(1), GetTutDesc(1), animT);
            DrawTileInteractive(t2, 2, "🦸", GetTutTitle(2), GetTutDesc(2), animT);
            DrawTileInteractive(t3, 3, "🗺️", GetTutTitle(3), GetTutDesc(3), animT);
            DrawTileInteractive(t4, 4, "🧩", GetTutTitle(4), GetTutDesc(4), animT);
            DrawTileInteractive(t5, 5, "🎨", GetTutTitle(5), GetTutDesc(5), animT);
            DrawTileInteractive(t6, 6, "▶️", GetTutTitle(6), GetTutDesc(6), animT, true);
        }

        private void DrawTileInteractive(Rect rect, int stepIndex, string icon, string title, string desc, float animT, bool isFinalStep = false)
        {
            bool isUnlocked = _tutorialProgress >= stepIndex;
            bool isJustUnlocked = (_unlockedStepIndex == stepIndex);

            bool isAnimatingUnlock = isJustUnlocked && animT < 2.0f;
            bool showAsLocked = !isUnlocked || (isJustUnlocked && animT < 1.0f);

            Event e = Event.current;
            bool isHovered = rect.Contains(e.mousePosition);
            if (isHovered) _isMouseOverAnyTile = true; // триггер для постоянной перерисовки в OnEditorUpdate
            bool isSelected = _selectedTutorialIndex == stepIndex;

            Rect drawRect = new Rect(rect);

            if ((isHovered || isSelected) && isUnlocked && !NovellaTutorialManager.IsTutorialActive && !isAnimatingUnlock)
            {
                if (isHovered && !isSelected)
                {
                    float shakeX = Mathf.Sin((float)EditorApplication.timeSinceStartup * 45f) * 1.2f;
                    float shakeY = Mathf.Cos((float)EditorApplication.timeSinceStartup * 50f) * 1.2f;
                    drawRect.x += shakeX; drawRect.y += shakeY;
                }

                // ─── Многослойный glow в Hub-cyan ───
                // Selected — пульсирующий cyan ореол; hover — мягкий cyan-glow.
                // Цвет 1:1 с акцентом Novella Studio (никаких зелёных/синих сюрпризов).
                Color baseGlow = new Color(0.36f, 0.75f, 0.92f, 1f);
                float pulse = isSelected
                    ? (Mathf.Sin((float)EditorApplication.timeSinceStartup * 3.5f) * 0.5f + 0.5f) * 0.30f + 0.50f
                    : 0.32f;
                for (int g = 4; g >= 0; g--)
                {
                    float inflate = 2f + g * 4f;
                    float a = pulse * Mathf.Pow(0.45f, g);
                    Color c = baseGlow; c.a = a;
                    Rect glowRect = new Rect(drawRect.x - inflate, drawRect.y - inflate,
                                              drawRect.width + inflate * 2f, drawRect.height + inflate * 2f);
                    EditorGUI.DrawRect(glowRect, c);
                }
            }

            // ─── Hub-card-style фон плитки ───
            // Палитра 1:1 с дашбордом NovellaHubWindow:
            //   default     = (0.10, 0.106, 0.149)   ← база Hub-карточки
            //   hover       = (0.13, 0.137, 0.18)    ← Hub hover-tone
            //   selected    = (0.15, 0.20, 0.28)     ← Hub selected-tone
            //   locked      = чуть темнее default
            Color tileBg;
            if (!isUnlocked)
                tileBg = new Color(0.085f, 0.090f, 0.125f, 1f);
            else if (isSelected)
                tileBg = new Color(0.15f, 0.20f, 0.28f, 1f);
            else if (isHovered)
                tileBg = new Color(0.13f, 0.137f, 0.18f, 1f);
            else
                tileBg = new Color(0.10f, 0.106f, 0.149f, 1f);
            EditorGUI.DrawRect(drawRect, tileBg);

            // Hub-border — 1px тонкая рамка.
            Color borderC = isSelected
                ? new Color(0.36f, 0.75f, 0.92f, 1f)              // cyan для selected
                : (isUnlocked
                    ? new Color(0.165f, 0.176f, 0.243f, 1f)        // Hub default border
                    : new Color(0.135f, 0.145f, 0.20f, 1f));       // приглушённый для locked
            DrawCardBorder(drawRect, borderC);

            // Левая cyan-полоса у selected — фирменный «вы здесь» маркер.
            if (isSelected && isUnlocked)
            {
                EditorGUI.DrawRect(new Rect(drawRect.x, drawRect.y, 3, drawRect.height),
                    new Color(0.36f, 0.75f, 0.92f, 1f));
            }
            GUI.color = isUnlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);

            // ─── Адаптивный layout: иконка + title + (опционально) description ───
            // На компактных tileH (78–95) описание скрывается — только title рядом
            // с иконкой. На «нормальных» (>95) — title сверху, desc под ним.
            float th = drawRect.height;
            float iconSize = Mathf.Clamp(th - 24f, 32f, 46f);
            float iconY = drawRect.y + (th - iconSize) * 0.5f;

            Rect iconBg = new Rect(drawRect.x + 10, iconY, iconSize, iconSize);
            EditorGUI.DrawRect(iconBg, new Color(0.075f, 0.082f, 0.11f, 1f));
            DrawCardBorder(iconBg, isSelected
                ? new Color(0.36f, 0.75f, 0.92f, 0.55f)
                : new Color(0.165f, 0.176f, 0.243f, 1f));

            GUI.Label(iconBg, icon, new GUIStyle(EditorStyles.label) {
                fontSize = (int)(iconSize * 0.62f),
                alignment = TextAnchor.MiddleCenter
            });

            float textX = iconBg.xMax + 14f;
            float textW = drawRect.xMax - textX - 12f;

            // Title style.
            GUIStyle titleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = th < 100f ? 13 : 15,
                wordWrap = true,
                normal = { textColor = isUnlocked
                    ? Color.white
                    : new Color(0.55f, 0.60f, 0.68f, 1f) }
            };
            if (isFinalStep && isUnlocked)
                titleSt.normal.textColor = new Color(0.30f, 0.85f, 0.45f, 1f);

            bool showDesc = th >= 100f;

            if (showDesc)
            {
                // Title наверху, description снизу.
                Rect titleRect = new Rect(textX, drawRect.y + 12, textW, 26);
                GUI.Label(titleRect, title, titleSt);

                var descSt = new GUIStyle(EditorStyles.wordWrappedLabel) {
                    fontSize = 12,
                    normal = { textColor = isUnlocked
                        ? new Color(0.62f, 0.70f, 0.78f, 1f)
                        : new Color(0.42f, 0.46f, 0.52f, 1f) }
                };
                Rect descRect = new Rect(textX, drawRect.y + 38,
                    textW, drawRect.height - 46);
                GUI.Label(descRect, desc, descSt);
            }
            else
            {
                // Компактный режим — только title, вертикально по центру.
                titleSt.alignment = TextAnchor.MiddleLeft;
                Rect titleRect = new Rect(textX, drawRect.y + 4, textW, drawRect.height - 8);
                GUI.Label(titleRect, title, titleSt);
            }

            GUI.color = Color.white;

            if (showAsLocked)
            {
                float lockAlpha = 1f;
                float lockOffsetX = 0f;
                float lockOffsetY = 0f;
                float lockScale = 1f;
                float overlayAlpha = 0.55f;

                if (isJustUnlocked)
                {
                    // Phase 1 (0–0.45s): жёсткий shake — замок трясётся.
                    if (animT < 0.45f)
                    {
                        float shakeIntensity = 1f - animT / 0.45f * 0.3f; // нарастает к концу
                        lockOffsetX = Mathf.Sin(animT * 60f) * 7f * shakeIntensity;
                        lockOffsetY = Mathf.Cos(animT * 70f) * 4f * shakeIntensity;
                    }
                    else
                    {
                        // Phase 2 (0.45–0.95s): замок «лопается» — масштабируется и фейдится.
                        float popT = Mathf.Clamp01((animT - 0.45f) / 0.50f);
                        lockScale = 1f + popT * 1.8f;          // увеличивается до 2.8x
                        lockAlpha = 1f - popT;                  // фейд
                        overlayAlpha = 0.55f * (1f - popT);
                    }
                }

                EditorGUI.DrawRect(drawRect, new Color(0.075f, 0.078f, 0.106f, overlayAlpha));

                if (lockAlpha > 0)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1, 1, 1, lockAlpha);
                    int lockFontSize = (int)(36f * lockScale);
                    float lockSize = 40f * lockScale;
                    Rect lockRect = new Rect(
                        drawRect.center.x - lockSize / 2f + lockOffsetX,
                        drawRect.center.y - lockSize / 2f + lockOffsetY,
                        lockSize, lockSize);
                    GUI.Label(lockRect, "🔒", new GUIStyle(EditorStyles.label) {
                        fontSize = lockFontSize,
                        alignment = TextAnchor.MiddleCenter
                    });
                    GUI.color = oldColor;
                }
            }

            // ─── Cyan-burst после разблокировки (кольца расходятся от плитки) ───
            // Фаза 0.45–1.6s: 3 кольца расширяются и фейдятся.
            if (isJustUnlocked && animT >= 0.45f && animT < 1.6f)
            {
                float burstT = (animT - 0.45f) / 1.15f;  // 0..1
                for (int ring = 0; ring < 3; ring++)
                {
                    float ringDelay = ring * 0.18f;
                    float ringT = Mathf.Clamp01(burstT - ringDelay);
                    if (ringT <= 0f || ringT >= 1f) continue;

                    float expand = ringT * 38f;
                    float a = (1f - ringT) * 0.55f;
                    Color burstC = new Color(0.36f, 0.75f, 0.92f, a);

                    Rect br = new Rect(
                        drawRect.x - expand, drawRect.y - expand,
                        drawRect.width + expand * 2f, drawRect.height + expand * 2f);
                    // Рисуем «полое» кольцо: 4 тонкие полоски по периметру.
                    EditorGUI.DrawRect(new Rect(br.x, br.y, br.width, 2), burstC);
                    EditorGUI.DrawRect(new Rect(br.x, br.yMax - 2, br.width, 2), burstC);
                    EditorGUI.DrawRect(new Rect(br.x, br.y, 2, br.height), burstC);
                    EditorGUI.DrawRect(new Rect(br.xMax - 2, br.y, 2, br.height), burstC);
                }
            }

            if (GUI.Button(drawRect, GUIContent.none, GUIStyle.none))
            {
                if (NovellaTutorialManager.IsTutorialActive)
                {
                    EditorUtility.DisplayDialog(ToolLang.Get("Finish Current Tour!", "Завершите текущий урок!"), ToolLang.Get("Please finish or skip the active tutorial window first.", "Пожалуйста, завершите или пропустите текущую активную экскурсию перед открытием следующей."), "OK");
                    return;
                }

                if (isUnlocked && !isAnimatingUnlock)
                {
                    _selectedTutorialIndex = stepIndex;
                }
            }
        }

        /// <summary>
        /// Описание одного коннектора: точки + tangent'ы + статус разблокировки.
        /// Используется для (а) рисования визуала и (б) построения цепочки пульсов.
        /// </summary>
        private struct ArrowSegment
        {
            public Vector2 Start;
            public Vector2 End;
            public Vector2 StartTangent;
            public Vector2 EndTangent;
            public bool IsUnlocked;
        }

        private static ArrowSegment MakeArrowSeg(Vector2 start, Vector2 end, Vector2 startDir, Vector2 endDir, float curveStrength, bool isUnlocked)
        {
            return new ArrowSegment
            {
                Start = start,
                End = end,
                StartTangent = start + startDir * curveStrength,
                EndTangent = end + endDir * curveStrength,
                IsUnlocked = isUnlocked,
            };
        }

        /// <summary>
        /// Рисует один коннектор: glow + основная линия + наконечник.
        /// Без пульсов — пульсы идут единым потоком по цепочке (DrawArrowChainPulses).
        /// </summary>
        private void DrawArrowVisual(ArrowSegment seg)
        {
            Vector2 start = seg.Start, end = seg.End;
            Vector3 startTangent = seg.StartTangent;
            Vector3 endTangent = seg.EndTangent;

            if (!seg.IsUnlocked)
            {
                // Locked: приглушённая серая линия без glow и без пульсов.
                Color dim = new Color(0.30f, 0.34f, 0.42f, 0.55f);
                Handles.DrawBezier(start, end, startTangent, endTangent,
                    new Color(0, 0, 0, 0.30f), null, 3f);
                Handles.DrawBezier(start, end, startTangent, endTangent, dim, null, 2f);

                Vector2 dir2 = (end - (Vector2)endTangent).normalized;
                if (dir2 == Vector2.zero) dir2 = (end - start).normalized;
                Vector2 r2 = new Vector2(-dir2.y, dir2.x);
                Handles.color = dim;
                Handles.DrawAAConvexPolygon(end, end - dir2 * 12f + r2 * 6f, end - dir2 * 12f - r2 * 6f);
                Handles.color = Color.white;
                return;
            }

            // ─── Unlocked: glow + main line + наконечник ───
            Color cyan = new Color(0.36f, 0.75f, 0.92f, 1f);

            // Glow: 3 слоя с убывающей альфой, нарастающей толщиной.
            for (int g = 3; g > 0; g--)
            {
                float thickness = 2f + g * 2.5f;
                float a = 0.18f / g;
                Handles.DrawBezier(start, end, startTangent, endTangent,
                    new Color(cyan.r, cyan.g, cyan.b, a), null, thickness);
            }

            // Основная чистая линия.
            Handles.DrawBezier(start, end, startTangent, endTangent, cyan, null, 2.5f);

            // Стрелка-наконечник с glow.
            Vector2 dir = (end - (Vector2)endTangent).normalized;
            if (dir == Vector2.zero) dir = (end - start).normalized;
            Vector2 right = new Vector2(-dir.y, dir.x);

            Vector2 p1 = end - dir * 12f + right * 6f;
            Vector2 p2 = end - dir * 12f - right * 6f;

            Handles.color = new Color(cyan.r, cyan.g, cyan.b, 0.28f);
            Vector2 gP1 = end - dir * 14f + right * 8f;
            Vector2 gP2 = end - dir * 14f - right * 8f;
            Handles.DrawAAConvexPolygon(end + dir * 1.5f, gP1, gP2);

            Handles.color = cyan;
            Handles.DrawAAConvexPolygon(end, p1, p2);
            Handles.color = Color.white;
        }

        /// <summary>
        /// Гоняет 2 cyan-пульса единым потоком ЧЕРЕЗ ВСЮ цепочку открытых коннекторов.
        /// Один пульс «выходит» из tile N, входит в tile N+1, выходит из tile N+1, и т.д.
        /// до конца цепочки. Затем новый цикл.
        ///
        /// На границах между сегментами пульс плавно затухает (sin envelope) — это даёт
        /// эффект «энергия зашла в плитку, потом вышла с другой стороны», без визуального
        /// «телепорта» через сам tile.
        /// </summary>
        private void DrawArrowChainPulses(System.Collections.Generic.List<ArrowSegment> chain)
        {
            if (chain == null || chain.Count == 0) return;

            Color cyan = new Color(0.36f, 0.75f, 0.92f, 1f);
            float now = (float)EditorApplication.timeSinceStartup;

            // Длительность одного полного пробега цепочки = ~1.4с на сегмент.
            // Так короткая цепочка из 1-2 стрелок не «пролистывается» слишком быстро,
            // а длинная (5 стрелок) проходится за разумные ~7 секунд.
            float chainDuration = chain.Count * 1.4f;
            const int pulseCount = 2;

            for (int p = 0; p < pulseCount; p++)
            {
                float phaseOffset = p / (float)pulseCount;
                float globalT = ((now / chainDuration) + phaseOffset) % 1f;

                // Маппинг globalT (0..1) → segment index + local t внутри сегмента.
                float segPosition = globalT * chain.Count;
                int segIdx = Mathf.FloorToInt(segPosition);
                if (segIdx >= chain.Count) segIdx = chain.Count - 1;
                float localT = segPosition - segIdx;

                var seg = chain[segIdx];
                Vector2 pt = CubicBezier(seg.Start, seg.StartTangent, seg.EndTangent, seg.End, localT);

                // Sin envelope по локальному t — пульс невидим у границ сегмента,
                // максимум в центре. Это маскирует «прыжок» с конца N на начало N+1
                // через тело tile'а: пульс просто гаснет перед телом и зажигается после.
                float scale = Mathf.Sin(localT * Mathf.PI);
                if (scale <= 0.01f) continue;

                float pulseR = 2.5f + scale * 2.5f;

                // Glow-кольца вокруг пульса.
                for (int gp = 3; gp > 0; gp--)
                {
                    float ringR = pulseR + gp * 2f;
                    float ringA = 0.40f / gp * scale;
                    Handles.color = new Color(cyan.r, cyan.g, cyan.b, ringA);
                    Handles.DrawSolidDisc(pt, Vector3.forward, ringR);
                }
                // Яркая центральная точка.
                Handles.color = new Color(1f, 1f, 1f, 0.85f * scale);
                Handles.DrawSolidDisc(pt, Vector3.forward, pulseR * 0.5f);
            }
            Handles.color = Color.white;
        }

        /// <summary>
        /// Точка на cubic Bezier curve в параметре t (0..1).
        /// Используется для позиционирования traveling-pulses на коннекторе.
        /// </summary>
        private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * p0
                 + 3f * uu * t * p1
                 + 3f * u * tt * p2
                 + ttt * p3;
        }
    }
}