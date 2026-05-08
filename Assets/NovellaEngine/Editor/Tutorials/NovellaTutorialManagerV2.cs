using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Новая туториал-система v2: data-driven, на ScriptableObject-ассетах.
    /// Полностью совместима по публичному API со старым NovellaEngine.Editor.NovellaTutorialManager.
    /// </summary>
    public static class NovellaTutorialManagerV2
    {
        // ─────────────────────────── СОСТОЯНИЕ ───────────────────────────

        private static NovellaTutorialAsset _activeAsset;
        private static int _currentStep;
        private static double _stepStartTime;
        private static double _tutorialStartTime;

        private static EventType _savedEventType = EventType.Ignore;
        private static Vector2 _textScrollPos;
        private static bool _initialized;

        // Lookup по TutorialKey, чтобы вызовы по строковому ключу (старый API) находили ассет.
        private static Dictionary<string, NovellaTutorialAsset> _byKey;

        public static bool IsTutorialActive => _activeAsset != null;
        public static NovellaTutorialAsset ActiveAsset => _activeAsset;
        public static int CurrentStepIndex => _currentStep;

        // События (для будущей интеграции с Welcome-окном и аналитикой)
        public static event Action<NovellaTutorialAsset> OnTutorialStarted;
        public static event Action<NovellaTutorialAsset> OnTutorialCompleted;
        public static event Action<NovellaTutorialAsset, int> OnStepChanged;

        // ─────────────────────────── INIT ───────────────────────────

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            RebuildLookup();
            EditorApplication.update += UpdateLoop;
            EditorApplication.projectChanged += RebuildLookup;
        }

        private static void RebuildLookup()
        {
            _byKey = new Dictionary<string, NovellaTutorialAsset>(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:NovellaTutorialAsset");
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<NovellaTutorialAsset>(p);
                if (a != null && !string.IsNullOrEmpty(a.TutorialKey))
                    _byKey[a.TutorialKey] = a;
            }
        }

        public static IReadOnlyList<NovellaTutorialAsset> GetAllOrdered()
        {
            Init();
            return _byKey.Values.OrderBy(a => a.OrderIndex).ToList();
        }

        public static NovellaTutorialAsset FindByKey(string key)
        {
            Init();
            return _byKey.TryGetValue(key ?? "", out var a) ? a : null;
        }

        // ─────────────────────────── ПУБЛИЧНЫЙ API ───────────────────────────

        public static void StartTutorial(string tutorialKey)
        {
            Init();
            var asset = FindByKey(tutorialKey);
            if (asset == null)
            {
                Debug.LogWarning($"[Novella Tutorial] Туториал с ключом '{tutorialKey}' не найден. " +
                                 $"Запусти миграцию через меню Tools → Novella Engine → Tutorials → Migrate Legacy Tutorials.");
                return;
            }
            StartTutorial(asset);
        }

        public static void StartTutorial(NovellaTutorialAsset asset)
        {
            StartTutorialAtStep(asset, 0);
        }

        /// <summary>
        /// Запустить туториал начиная с конкретного шага (0-based).
        /// Полезно для editor-кнопки «Test this step» — не надо листать весь
        /// туториал чтобы проверить как выглядит шаг N.
        /// </summary>
        public static void StartTutorialAtStep(NovellaTutorialAsset asset, int startStepIndex)
        {
            Init();
            if (asset == null || asset.Steps == null || asset.Steps.Count == 0) return;

            int idx = Mathf.Clamp(startStepIndex, 0, asset.Steps.Count - 1);

            _activeAsset = asset;
            _currentStep = idx;
            _tutorialStartTime = EditorApplication.timeSinceStartup;
            _stepStartTime = _tutorialStartTime;
            _textScrollPos = Vector2.zero;

            // Сбрасываем состояние плавного перехода — иначе первый шаг
            // нового туториала «приедет» с прошлой позиции.
            NovellaTutorialOverlay.ResetTransitionState();

            OnTutorialStarted?.Invoke(asset);
            OnStepChanged?.Invoke(asset, idx);
            RepaintAll();
        }

        public static void ForceStopTutorial()
        {
            if (_activeAsset == null) return;
            var asset = _activeAsset;
            _activeAsset = null;
            _currentStep = 0;
            NovellaTutorialOverlay.DisposeVideo();
            NovellaTutorialOverlay.ResetTransitionState();
            OnTutorialCompleted?.Invoke(asset);
            RepaintAll();
        }

        public static void NextStep()
        {
            if (_activeAsset == null) return;
            if (_currentStep < _activeAsset.Steps.Count - 1)
            {
                _currentStep++;
                _stepStartTime = EditorApplication.timeSinceStartup;
                _textScrollPos = Vector2.zero;
                NovellaTutorialOverlay.DisposeVideo();
                OnStepChanged?.Invoke(_activeAsset, _currentStep);
            }
            else
            {
                CompleteTutorial();
            }
        }

        public static void PrevStep()
        {
            if (_activeAsset == null || _currentStep == 0) return;
            _currentStep--;
            _stepStartTime = EditorApplication.timeSinceStartup;
            _textScrollPos = Vector2.zero;
            NovellaTutorialOverlay.DisposeVideo();
            OnStepChanged?.Invoke(_activeAsset, _currentStep);
        }

        public static void CompleteTutorial()
        {
            if (_activeAsset == null) return;

            string finishedKey = _activeAsset.TutorialKey;
            int finishedOrder = _activeAsset.OrderIndex;

            EditorPrefs.SetBool("Novella_Tut_" + finishedKey, true);

            int currentProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);
            if (finishedOrder >= currentProgress)
                EditorPrefs.SetInt("Novella_TutorialProgress", finishedOrder + 1);

            ForceStopTutorial();

            // Восстановление UX-флоу: после завершения возвращаемся в Welcome.
            // Откладываем — чтобы успели завершиться все текущие OnGUI/IMGUI операции
            // во всех окнах, иначе можно поймать "Invalid GUILayout state".
            EditorApplication.delayCall += () =>
            {
                // Закрываем все окна графа, открытые с туториал-ассетами (interactive lesson и т.д.)
                CloseTutorialGraphWindows();

                // И открываем Welcome поверх
                NovellaEngine.Editor.NovellaWelcomeWindow.ShowWindow();
            };
        }

        /// <summary>
        /// «Закрыть» — пользователь выходит из туториала, но мы НЕ помечаем урок
        /// пройденным и НЕ продвигаем общий прогресс. Welcome-окно после этого
        /// откроется в том же состоянии (тот же selected, тот же locked).
        /// Используется кнопкой "✕ Закрыть" в overlay-панели туториала.
        /// </summary>
        public static void CancelTutorial()
        {
            if (_activeAsset == null) return;

            ForceStopTutorial();

            // Возвращаемся в Welcome — но без отметки прогресса.
            // (Если юзер просто хотел уйти — Welcome даст ему перепройти позже.)
            EditorApplication.delayCall += () =>
            {
                CloseTutorialGraphWindows();
                NovellaEngine.Editor.NovellaWelcomeWindow.ShowWindow();
            };
        }

        private static void CloseTutorialGraphWindows()
        {
            // Закрываем NovellaGraphWindow, если он был открыт под туториал.
            // Простая эвристика: тот, у которого _isTutorialMode = true.
            var graphWindows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w.GetType().Name == "NovellaGraphWindow").ToList();

            foreach (var w in graphWindows)
            {
                var f = w.GetType().GetField("_isTutorialMode",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && (bool)f.GetValue(w))
                {
                    w.Close();
                }
            }
        }

        // ─────────────────────────── UPDATE: автопродвижение по таймеру ───────────────────────────

        private static void UpdateLoop()
        {
            if (_activeAsset == null) return;

            var step = CurrentStep();
            if (step == null) return;

            // Автотаймер
            if ((step.AdvanceMode == ETutorialAdvanceMode.AutoTimer || step.AdvanceMode == ETutorialAdvanceMode.Any)
                && step.AutoAdvanceSeconds > 0f)
            {
                double elapsed = EditorApplication.timeSinceStartup - _stepStartTime;
                if (elapsed >= step.AutoAdvanceSeconds + step.MinHoldSeconds)
                {
                    NextStep();
                    return;
                }
            }

            // Принудительно репеинтим все окна — анимации (smooth target,
            // breathing spotlight, multi-layer glow) ломаются если кадры
            // не идут. RepaintAll дороговат, но активен только во время
            // туториала (~короткое обучение).
            RepaintAll();
        }

        private static NovellaTutorialStep CurrentStep()
        {
            if (_activeAsset == null) return null;
            if (_currentStep < 0 || _currentStep >= _activeAsset.Steps.Count) return null;
            return _activeAsset.Steps[_currentStep];
        }

        private static void RepaintAll()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>()) w.Repaint();
        }

        // ─────────────────────────── СОВМЕСТИМЫЙ API ДЛЯ ВЫЗОВОВ ИЗ ОКОН ───────────────────────────

        /// <summary>
        /// Блокирует мышь/клавиатуру/скролл фоновых окон, пока туториал активен.
        /// Совместимо со старым NovellaTutorialManager.BlockBackgroundEvents.
        /// </summary>
        public static void BlockBackgroundEvents(EditorWindow window, bool isHubGlobal = false)
        {
            if (_activeAsset == null) return;
            if (window == null) return;

            // Hub передаёт isHubGlobal=true один раз. Для модулей внутри Hub'а вызов придёт
            // снова с isHubGlobal=false (они не знают что они в Hub'е) — игнорируем второй вызов.
            if (window.GetType().Name == "NovellaHubWindow" && !isHubGlobal) return;

            _savedEventType = Event.current.type;

            // Шаг с ClickTarget/ClickAnywhere — НЕ блокируем клики, пропускаем юзеру.
            var step = CurrentStep();
            if (step != null && (step.AdvanceMode == ETutorialAdvanceMode.OnUserAction || step.AdvanceMode == ETutorialAdvanceMode.Any))
            {
                if (step.ActionTrigger == ETutorialActionTrigger.ClickAnywhere ||
                    step.ActionTrigger == ETutorialActionTrigger.ClickTarget ||
                    step.ActionTrigger == ETutorialActionTrigger.TextInput ||
                    step.ActionTrigger == ETutorialActionTrigger.AnyKey)
                {
                    // Не блокируем — позволяем юзеру действовать.
                    return;
                }
            }

            if (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)
            {
                Event.current.type = EventType.Ignore;
            }
        }

        /// <summary>
        /// Главная точка отрисовки оверлея. Вызывается из IMGUIContainer / OnGUI.
        /// Совместимо со старым NovellaTutorialManager.DrawOverlay.
        /// </summary>
        public static void DrawOverlay(EditorWindow window, bool isHubGlobal = false)
        {
            if (_activeAsset == null) return;
            if (window == null) return;
            if (window.GetType().Name == "NovellaHubWindow" && !isHubGlobal) return;

            var step = CurrentStep();
            if (step == null) return;

            // Восстанавливаем event type, который мы погасили в BlockBackgroundEvents
            if (Event.current.type == EventType.Ignore && _savedEventType != EventType.Ignore)
            {
                Event.current.type = _savedEventType;
            }

            float stepTime = (float)(EditorApplication.timeSinceStartup - _stepStartTime);
            float introAnim = Mathf.Clamp01(stepTime / 0.35f);
            introAnim = 1f - Mathf.Pow(1f - introAnim, 3f); // ease-out cubic

            // 1) Резолвим целевой Rect
            Rect target = NovellaTutorialResolver.Resolve(step, window);

            // 2) Проверяем триггеры действия юзера ДО отрисовки (иначе клики могут уйти в overlay)
            HandleUserActionTriggers(step, target);

            // 3) Рисуем визуальную подсказку
            NovellaTutorialOverlay.Draw(window, step, target, introAnim, _stepStartTime);

            // 4) Рисуем текстовую панель
            DrawTextPanel(window, step, target, introAnim);

            // 5) Поглощаем оставшиеся события — если шаг ждёт Next (а не действия)
            if (step.AdvanceMode == ETutorialAdvanceMode.OnNextButton || step.AdvanceMode == ETutorialAdvanceMode.AutoTimer)
            {
                if (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)
                    Event.current.Use();
            }
        }

        // ─────────────────────────── ОТРИСОВКА ТЕКСТОВОЙ ПАНЕЛИ ───────────────────────────

        private static void DrawTextPanel(EditorWindow window, NovellaTutorialStep step, Rect target, float intro)
        {
            float w = window.position.width;
            float h = window.position.height;

            bool hasMedia = step.Video != null || step.Image != null;
            float boxW = hasMedia ? 580f : 540f;
            float boxH = hasMedia ? 420f : 200f;

            // Используем smooth-target от overlay чтобы стрелка/якорь панели
            // тоже плавно следовали за интерполированным rect'ом, а не за raw.
            Rect smoothTarget = NovellaTutorialOverlay.GetCurrentSmoothTarget(window, target);
            Rect panel = ComputePanelRect(smoothTarget, w, h, boxW, boxH, step.PanelAnchor);

            // Стрелка от панели к цели (для Hint=Arrow)
            if (step.HintStyle == ETutorialHintStyle.Arrow && smoothTarget.width > 1)
            {
                NovellaTutorialOverlay.DrawArrowFromPanelToTarget(panel, smoothTarget, step.AccentColor, intro);
            }

            // Appearance-анимация: scale-from-95% + slide-up + opacity.
            // Панель появляется не «плоско», а с лёгким зумом — как в нативных
            // современных UI (iOS popover / VS Code popups).
            float scale = 0.94f + 0.06f * intro;
            float invScale = 1f / scale;
            float scaledW = panel.width * scale;
            float scaledH = panel.height * scale;
            float scaledX = panel.x + (panel.width - scaledW) * 0.5f;
            float scaledY = panel.y + (panel.height - scaledH) * 0.5f + (1f - intro) * 14f;
            Rect drawRect = new Rect(scaledX, scaledY, scaledW, scaledH);

            // ─── Многослойная тень для глубины ───
            for (int s = 0; s < 4; s++)
            {
                float so = 2f + s * 5f;
                float sa = (0.38f - s * 0.08f) * intro;
                EditorGUI.DrawRect(new Rect(drawRect.x + so * 0.5f, drawRect.y + so, drawRect.width, drawRect.height),
                    new Color(0, 0, 0, sa));
            }

            // ─── Основной фон с градиентом сверху-вниз ───
            // Top — чуть светлее (acc-tinted), bottom — глубокий тёмный.
            // Эффект «глянца» как у iOS-карточек.
            // gradSteps = высота в px → 1px на шаг, banding невидим.
            int gradSteps = Mathf.Max(40, Mathf.CeilToInt(drawRect.height));
            float gradStepH = drawRect.height / gradSteps + 1f;
            for (int gi = 0; gi < gradSteps; gi++)
            {
                float t = gi / (float)(gradSteps - 1);
                Color top = new Color(0.16f, 0.17f, 0.22f, 0.97f * intro);
                Color bot = new Color(0.09f, 0.10f, 0.14f, 0.97f * intro);
                Color row = Color.Lerp(top, bot, t);
                float gy = drawRect.y + drawRect.height * t;
                EditorGUI.DrawRect(new Rect(drawRect.x, gy, drawRect.width, gradStepH), row);
            }

            // ─── HEADER bar (30px) с акцентной заливкой и иконкой 🎓 ───
            const float headerH = 30f;
            Rect header = new Rect(drawRect.x, drawRect.y, drawRect.width, headerH);
            // Градиент акцентного цвета: ярче слева, темнее справа.
            // hSteps масштабируется по ширине (1px на шаг) — banding не виден.
            int hSteps = Mathf.Max(80, Mathf.CeilToInt(header.width));
            float hStepW = header.width / hSteps + 1f;
            for (int hi = 0; hi < hSteps; hi++)
            {
                float t = hi / (float)(hSteps - 1);
                Color leftC = new Color(step.AccentColor.r, step.AccentColor.g, step.AccentColor.b, 0.95f * intro);
                Color rightC = new Color(step.AccentColor.r * 0.55f, step.AccentColor.g * 0.55f, step.AccentColor.b * 0.55f, 0.85f * intro);
                Color col = Color.Lerp(leftC, rightC, t);
                float gx = header.x + header.width * t;
                EditorGUI.DrawRect(new Rect(gx, header.y, hStepW, header.height), col);
            }
            // Тонкая линия под header'ом.
            EditorGUI.DrawRect(new Rect(header.x, header.yMax, header.width, 1),
                new Color(0, 0, 0, 0.45f * intro));

            // 🎓 иконка слева в header'е.
            var hdrIconSt = new GUIStyle(EditorStyles.label) {
                fontSize = 16, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1, 1, 1, intro) }
            };
            GUI.Label(new Rect(header.x + 12, header.y, 30, header.height), "🎓", hdrIconSt);

            // Tutorial title в header'е (имя ассета или дефолт).
            string tutTitle = _activeAsset != null
                ? (ToolLang.IsRU ? _activeAsset.TitleRU : _activeAsset.TitleEN)
                : ToolLang.Get("Tutorial", "Туториал");
            if (string.IsNullOrEmpty(tutTitle)) tutTitle = ToolLang.Get("Tutorial", "Туториал");
            var hdrTitleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1, 1, 1, intro) }
            };
            GUI.Label(new Rect(header.x + 42, header.y, header.width - 200, header.height), tutTitle, hdrTitleSt);

            // ─── Step counter pill справа в header'е ───
            // Большая, читаемая, на тёмной плашке поверх акцентного header'а.
            if (_activeAsset != null && _activeAsset.Steps != null)
            {
                int totalSteps = _activeAsset.Steps.Count;
                string counter = (_currentStep + 1) + " / " + totalSteps;
                var pillSt = new GUIStyle(EditorStyles.boldLabel) {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1, 1, 1, intro) }
                };
                Vector2 sz = pillSt.CalcSize(new GUIContent(counter));
                float pillW = sz.x + 16;
                Rect pill = new Rect(header.xMax - pillW - 12, header.y + 5, pillW, header.height - 10);
                EditorGUI.DrawRect(pill, new Color(0, 0, 0, 0.40f * intro));
                GUI.Label(pill, counter, pillSt);
            }

            // ─── Прогресс-бар (тонкий, под header'ом) ───
            if (_activeAsset != null && _activeAsset.Steps != null && _activeAsset.Steps.Count > 1)
            {
                Rect track = new Rect(drawRect.x, header.yMax + 1, drawRect.width, 3);
                EditorGUI.DrawRect(track, new Color(0, 0, 0, 0.30f * intro));
                float p = (_currentStep + 1) / (float)_activeAsset.Steps.Count;
                Rect fill = new Rect(track.x, track.y, track.width * p, track.height);
                EditorGUI.DrawRect(fill, new Color(step.AccentColor.r, step.AccentColor.g, step.AccentColor.b, intro));
            }

            // ─── Внешний glow-обод вокруг всей панели ───
            for (int g = 0; g < 3; g++)
            {
                float a = (0.55f - g * 0.18f) * intro;
                Color border = new Color(step.AccentColor.r, step.AccentColor.g, step.AccentColor.b, a);
                Rect br = new Rect(drawRect.x - g, drawRect.y - g, drawRect.width + g * 2, drawRect.height + g * 2);
                Handles.color = border;
                Handles.DrawSolidRectangleWithOutline(br, Color.clear, border);
            }
            Handles.color = Color.white;

            // Inner area начинается ПОД header'ом + progress bar (~38px),
            // иначе текст шага наезжает на title-полоску.
            const float headerOffset = 42f;
            float padX = 22f, padY = 16f;
            Rect inner = new Rect(
                drawRect.x + padX,
                drawRect.y + headerOffset + padY,
                drawRect.width - padX * 2f,
                drawRect.height - headerOffset - padY - 50f);

            Color oldGui = GUI.color;
            GUI.color = new Color(1, 1, 1, intro);

            float yCursor = inner.y;

            // Заголовок
            string title = ToolLang.IsRU ? step.TitleRU : step.TitleEN;
            if (!string.IsNullOrEmpty(title))
            {
                var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 17,
                    richText = true,
                    normal = { textColor = step.AccentColor }
                };
                Rect titleRect = new Rect(inner.x, yCursor, inner.width, 24);
                GUI.Label(titleRect, title, titleStyle);
                yCursor += 28;
            }

            // Видео/картинка
            if (hasMedia)
            {
                float mediaH = 200f;
                Rect media = new Rect(inner.x, yCursor, inner.width, mediaH);
                NovellaTutorialOverlay.DrawVideoOrImage(media, step);
                yCursor += mediaH + 12;
            }

            // Текст
            string body = ToolLang.IsRU ? step.BodyRU : step.BodyEN;
            var bodyStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.92f, 0.92f, 0.95f) }
            };

            float bodyHeight = drawRect.yMax - yCursor - 50;
            Rect bodyArea = new Rect(inner.x, yCursor, inner.width, Mathf.Max(40, bodyHeight));

            GUILayout.BeginArea(bodyArea);
            _textScrollPos = GUILayout.BeginScrollView(_textScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label(body, bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Кнопки внизу
            DrawPanelButtons(step, drawRect);

            GUI.color = oldGui;
        }

        private static void DrawPanelButtons(NovellaTutorialStep step, Rect panel)
        {
            float btnY = panel.yMax - 42;
            Rect btnArea = new Rect(panel.x + 18, btnY, panel.width - 36, 32);

            GUILayout.BeginArea(btnArea);
            GUILayout.BeginHorizontal();

            double timeOnStep = EditorApplication.timeSinceStartup - _stepStartTime;
            int unlockIn = Mathf.CeilToInt((float)(step.MinHoldSeconds - timeOnStep));

            // Close — выйти из туториала, НЕ отмечая прогресс. Раньше эта
            // кнопка называлась "Skip" и красным цветом дёргала CompleteTutorial,
            // что фактически награждало пользователя за пропуск (открывался
            // следующий урок). Теперь это нейтрально-серая «Закрыть»:
            //   • прогресс не двигается
            //   • следующий урок остаётся залоченным
            //   • Welcome-окно открывается с тем же selected
            // Награда за пройденный урок (анимация разблокировки) теперь
            // случается ТОЛЬКО при честном Done на последнем шаге.
            //
            // На ПОСЛЕДНЕМ шаге кнопку Close скрываем — иначе пользователь
            // случайно нажмёт её вместо «Завершить», и прогресс не зачтётся.
            // Выйти из последнего шага можно только через Back или Finish.
            bool isLastStep = (_currentStep == _activeAsset.Steps.Count - 1);
            if (step.AllowSkip && !isLastStep)
            {
                GUI.backgroundColor = new Color(0.30f, 0.32f, 0.38f);
                if (GUILayout.Button("✕ " + ToolLang.Get("Close", "Закрыть"), GUILayout.Width(110), GUILayout.Height(30)))
                {
                    // Откладываем — иначе _activeAsset = null прямо в середине этого OnGUI-кадра,
                    // и доступ к нему через 10 строк ниже падает с NRE.
                    EditorApplication.delayCall += CancelTutorial;
                }
                GUI.backgroundColor = Color.white;
            }

            // Подсказ "что нужно сделать" — для шагов с действием
            if (step.AdvanceMode == ETutorialAdvanceMode.OnUserAction || step.AdvanceMode == ETutorialAdvanceMode.Any)
            {
                GUILayout.Space(10);
                string hint = GetActionHintText(step);
                if (!string.IsNullOrEmpty(hint))
                {
                    var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, normal = { textColor = new Color(0.75f, 0.85f, 1f) } };
                    GUILayout.Label(hint, st, GUILayout.Height(30));
                }
            }

            GUILayout.FlexibleSpace();

            // Back
            if (_currentStep > 0)
            {
                GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
                if (GUILayout.Button("◄ " + ToolLang.Get("Back", "Назад"), GUILayout.Width(80), GUILayout.Height(30)))
                {
                    EditorApplication.delayCall += PrevStep;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(6);
            }

            // Next / Finish — _activeAsset МОГ стать null если delayCall сработал между begin и здесь;
            // null-guard защищает от крайних случаев и обеспечивает корректное закрытие GUILayout-блоков ниже.
            if (!step.HideNextButton && _activeAsset != null)
            {
                bool isLast = _currentStep == _activeAsset.Steps.Count - 1;
                bool locked = unlockIn > 0;

                EditorGUI.BeginDisabledGroup(locked);
                GUI.backgroundColor = locked ? new Color(0.3f, 0.5f, 0.3f) : new Color(0.2f, 0.78f, 0.45f);
                string label = locked ? $"⏳ {unlockIn}" : (isLast ? ToolLang.Get("Finish ✔", "Завершить ✔") : ToolLang.Get("Next ➔", "Далее ➔"));
                if (GUILayout.Button(label, GUILayout.Width(120), GUILayout.Height(30)))
                {
                    EditorApplication.delayCall += NextStep;
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static string GetActionHintText(NovellaTutorialStep step)
        {
            switch (step.ActionTrigger)
            {
                case ETutorialActionTrigger.ClickTarget:
                    return ToolLang.Get("👆 Click the highlighted item", "👆 Нажми на подсвеченный элемент");
                case ETutorialActionTrigger.ClickAnywhere:
                    return ToolLang.Get("👆 Click anywhere to continue", "👆 Нажми в любом месте чтобы продолжить");
                case ETutorialActionTrigger.TextInput:
                    return ToolLang.Get("✏ Type something in the highlighted field", "✏ Введи текст в подсвеченное поле");
                case ETutorialActionTrigger.AnyKey:
                    return ToolLang.Get("⌨ Press any key", "⌨ Нажми любую клавишу");
            }
            return "";
        }

        private static Rect ComputePanelRect(Rect target, float w, float h, float boxW, float boxH, ETutorialPanelAnchor anchor)
        {
            // Auto: ставим панель снизу окна по центру, или сверху если цель снизу
            if (anchor == ETutorialPanelAnchor.Auto)
            {
                if (target.width < 1 || target.height < 1)
                    return new Rect((w - boxW) / 2f, (h - boxH) / 2f, boxW, boxH);

                // Если цель в нижней половине — панель сверху, иначе снизу
                anchor = target.y > h * 0.55f ? ETutorialPanelAnchor.Top : ETutorialPanelAnchor.Bottom;
            }

            float x, y;
            switch (anchor)
            {
                case ETutorialPanelAnchor.Top:
                    x = (w - boxW) / 2f; y = 30;
                    break;
                case ETutorialPanelAnchor.Bottom:
                    x = (w - boxW) / 2f; y = h - boxH - 30;
                    break;
                case ETutorialPanelAnchor.Left:
                    x = 30; y = (h - boxH) / 2f;
                    break;
                case ETutorialPanelAnchor.Right:
                    x = w - boxW - 30; y = (h - boxH) / 2f;
                    break;
                default:
                    x = (w - boxW) / 2f; y = (h - boxH) / 2f;
                    break;
            }

            // Защита от выхода за границы
            x = Mathf.Clamp(x, 10, w - boxW - 10);
            y = Mathf.Clamp(y, 10, h - boxH - 10);

            return new Rect(x, y, boxW, boxH);
        }

        // ─────────────────────────── ТРИГГЕРЫ ДЕЙСТВИЯ ЮЗЕРА ───────────────────────────

        private static void HandleUserActionTriggers(NovellaTutorialStep step, Rect target)
        {
            if (step.AdvanceMode != ETutorialAdvanceMode.OnUserAction && step.AdvanceMode != ETutorialAdvanceMode.Any)
                return;

            // Проверка MinHoldSeconds — даём юзеру прочитать
            double timeOnStep = EditorApplication.timeSinceStartup - _stepStartTime;
            if (timeOnStep < step.MinHoldSeconds) return;

            Event e = Event.current;
            if (e == null) return;

            switch (step.ActionTrigger)
            {
                case ETutorialActionTrigger.ClickTarget:
                    if (e.type == EventType.MouseDown && target.Contains(e.mousePosition))
                    {
                        // Не Use'аем — позволяем клику дойти до реального контрола
                        EditorApplication.delayCall += NextStep;
                    }
                    break;

                case ETutorialActionTrigger.ClickAnywhere:
                    if (e.type == EventType.MouseDown)
                    {
                        EditorApplication.delayCall += NextStep;
                    }
                    break;

                case ETutorialActionTrigger.AnyKey:
                    if (e.type == EventType.KeyDown)
                    {
                        EditorApplication.delayCall += NextStep;
                    }
                    break;

                case ETutorialActionTrigger.TextInput:
                    // Текстовый ввод проверяем через TextField focus — отслеживаем GUI.GetNameOfFocusedControl()
                    if (e.type == EventType.KeyUp && !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
                    {
                        string focused = GUI.GetNameOfFocusedControl();
                        if (focused == step.TargetName)
                        {
                            // Проверяем регекс если задан. Текст самого поля мы получить не можем напрямую,
                            // но регекс у нас опциональный — обычно достаточно факта ввода в нужное поле.
                            if (string.IsNullOrEmpty(step.ActionTextRegex))
                            {
                                EditorApplication.delayCall += NextStep;
                            }
                        }
                    }
                    break;
            }
        }
    }
}
