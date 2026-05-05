// ════════════════════════════════════════════════════════════════════════════
// NovellaBuildModule — модуль сборки игры в .exe / .app / WebGL.
// Юзеру не нужно знать про BuildPipeline.BuildPlayer и Build Settings —
// здесь три большие кнопки платформ, prebuild-проверки и понятные ошибки.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaBuildModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Build", "Сборка");
        public string ModuleIcon => "📦";

        private EditorWindow _window;
        private Vector2 _scroll;
        // Отчёт о последней сборке. Хранится в SessionState чтобы пережить
        // domain reload — иначе после фикса compile-ошибки и пересборки скриптов
        // карточка с описанием падения сразу пропадёт.
        private const string PREF_LAST_REPORT = "Novella_LastBuildReport";
        private const string PREF_LAST_REPORT_ERR = "Novella_LastBuildReportIsError";
        private string _lastReport
        {
            get => SessionState.GetString(PREF_LAST_REPORT, "");
            set => SessionState.SetString(PREF_LAST_REPORT, value ?? "");
        }
        private bool _lastReportError
        {
            get => SessionState.GetBool(PREF_LAST_REPORT_ERR, false);
            set => SessionState.SetBool(PREF_LAST_REPORT_ERR, value);
        }
        // Пока идёт BuildPipeline.BuildPlayer (синхронный), Editor зависает.
        // Этот флаг блокирует повторные клики по карточкам платформ и
        // используется для отображения «строится» в карточках.
        private bool _isBuilding;

        // Палитра — динамическая.
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE  => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        public void OnEnable(EditorWindow hostWindow) { _window = hostWindow; }
        public void OnDisable() { }

        public void DrawGUI(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG);

            GUILayout.BeginArea(position);
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.Space(20);

            // Заголовок + переключатель подсказок справа.
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📦 " + ToolLang.Get("Build the game", "Собрать игру"), titleSt);
            GUILayout.FlexibleSpace();
            DrawHintsToggle();
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 12, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "One click — and your novel becomes a game ready to share. Pick a platform below.",
                "Один клик — и твоя новелла превращается в готовую игру. Выбери платформу ниже."), subSt);
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawWhatIsBuildHint();

            GUILayout.Space(18);

            // Pre-build checklist.
            DrawChecklist();

            GUILayout.Space(20);

            // Платформенные кнопки.
            DrawPlatformCards(position);

            GUILayout.Space(20);

            // Последний отчёт.
            if (!string.IsNullOrEmpty(_lastReport))
            {
                DrawLastReport();
            }

            GUILayout.Space(40);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Переключатель глобального ShowGuide. Аналогично кнопке в Кузнице UI.
        private void DrawHintsToggle()
        {
            bool guide = NovellaSettingsModule.ShowGuide;
            string text = "💡  " + (guide
                ? ToolLang.Get("Hints: On", "Подсказки: Вкл")
                : ToolLang.Get("Hints: Off", "Подсказки: Выкл"));

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = guide ? C_ACCENT : new Color(1, 1, 1, 0.05f);
            var st = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, padding = new RectOffset(8, 8, 4, 4) };
            st.normal.textColor = guide
                ? NovellaSettingsModule.GetContrastingText(C_ACCENT)
                : C_TEXT_2;
            if (GUILayout.Button(text, st, GUILayout.Width(140), GUILayout.Height(22)))
            {
                NovellaSettingsModule.ShowGuide = !guide;
                _window?.Repaint();
            }
            GUI.backgroundColor = prevBg;
        }

        // Карточка-объяснение «Что такое сборка». Показывается только когда
        // включены Подсказки. Стиль идентичен DrawGuideTip из Settings.
        private void DrawWhatIsBuildHint()
        {
            if (!NovellaSettingsModule.ShowGuide) return;

            string text = ToolLang.Get(
                "A 'build' is a finished version of your game packaged into a single program (.exe / .app / web page). " +
                "Players don't need Unity — they just download and run it. Pick a platform card below to start; the engine collects all your scenes, scripts, and assets and writes them into one folder you can share.",
                "«Сборка» — это готовая версия игры, собранная в один запускаемый файл (.exe / .app / веб-страница). " +
                "Игроку Unity не нужен — он просто скачивает и запускает. Выбери платформу ниже — движок соберёт все твои сцены, скрипты и ассеты в одну папку, которую ты можешь раздавать.");

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.07f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(24);
            GUILayout.EndHorizontal();
        }

        private void DrawChecklist()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var hSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            hSt.normal.textColor = C_TEXT_2;
            GUILayout.Label(ToolLang.Get("PRE-BUILD CHECKS", "ПРОВЕРКИ ПЕРЕД СБОРКОЙ").ToUpperInvariant(), hSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // 1. Активная история есть и валидна.
            var (story, storyErr) = ResolveActiveStory();
            DrawCheckRow(story != null && string.IsNullOrEmpty(storyErr),
                story != null
                    ? string.Format(ToolLang.Get("Active story: {0}", "Активная история: {0}"), story.Title)
                    : ToolLang.Get("No active story selected", "Активная история не выбрана"),
                storyErr);

            // 2. История имеет стартовую главу.
            DrawCheckRow(story != null && story.StartingChapter != null,
                story != null && story.StartingChapter != null
                    ? string.Format(ToolLang.Get("Starting chapter: {0}", "Стартовая глава: {0}"), story.StartingChapter.name)
                    : ToolLang.Get("Starting chapter not set", "Стартовая глава не задана"),
                story != null && story.StartingChapter == null
                    ? ToolLang.Get("Open story settings → pick a chapter.", "Открой настройки истории → выбери главу.")
                    : null);

            // 3. У истории назначена gameplay-сцена.
            DrawCheckRow(story != null && !string.IsNullOrEmpty(story.GameSceneName),
                story != null && !string.IsNullOrEmpty(story.GameSceneName)
                    ? string.Format(ToolLang.Get("Gameplay scene: {0}", "Игровая сцена: {0}"), story.GameSceneName)
                    : ToolLang.Get("Gameplay scene not assigned", "Игровая сцена не назначена"),
                story != null && string.IsNullOrEmpty(story.GameSceneName)
                    ? ToolLang.Get("Open story settings → pick a Gameplay Scene.", "Открой настройки истории → выбери игровую сцену.")
                    : null);

            // 4. Build Settings: хотя бы одна включённая сцена.
            int enabledScenes = 0;
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) enabledScenes++;
            DrawCheckRow(enabledScenes > 0,
                string.Format(ToolLang.Get("Scenes in Build Settings: {0}", "Сцен в Build Settings: {0}"), enabledScenes),
                enabledScenes == 0 ? ToolLang.Get(
                    "No scenes added to Build. Open «Сцены и Меню» → find your scene → click «✓ В сборку» on its action bar. If you have no scenes yet, create one first via the same module.",
                    "Ни одной сцены не добавлено в сборку. Открой «Сцены и Меню» → найди свою сцену → нажми «✓ В сборку» на её панели действий. Если сцен ещё нет — сначала создай через тот же модуль.") : null);

            // 5. Game scene включена в Build Settings.
            if (story != null && !string.IsNullOrEmpty(story.GameSceneName))
            {
                bool gameSceneInBuild = false;
                foreach (var s in EditorBuildSettings.scenes)
                {
                    if (!s.enabled) continue;
                    if (Path.GetFileNameWithoutExtension(s.path) == story.GameSceneName)
                    {
                        gameSceneInBuild = true;
                        break;
                    }
                }
                DrawCheckRow(gameSceneInBuild,
                    gameSceneInBuild
                        ? ToolLang.Get("Gameplay scene is in Build Settings", "Игровая сцена в Build Settings")
                        : ToolLang.Get("Gameplay scene is NOT in Build Settings", "Игровой сцены НЕТ в Build Settings"),
                    !gameSceneInBuild
                        ? ToolLang.Get(
                            "If '" + story.GameSceneName + "' exists — Story Settings → «➕ В сборку». If you don't have a gameplay scene yet — «Сцены и Меню» → New scene → apply preset «Игровая сцена» → save → «✓ В сборку» → Story Settings → assign it as Игровая сцена.",
                            "Если сцена «" + story.GameSceneName + "» уже существует — Настройки истории → «➕ В сборку». Если игровой сцены ещё нет — «Сцены и Меню» → создай новую → примени пресет «Игровая сцена» → сохрани сцену → «✓ В сборку» → Настройки истории → назначь её как Игровую сцену.")
                        : null);
            }

            // 6. Menu-сцена со StoryLauncher есть в Build Settings — entry point.
            // Если сцена сейчас открыта — проверяем in-memory объекты, иначе
            // читаем файл с диска. Иначе после удаления StoryLauncher до Save
            // на диске ещё лежит старая ссылка и check ложно зеленится.
            string menuScenePath = FindMenuScenePath();
            bool menuSceneOk = !string.IsNullOrEmpty(menuScenePath);
            DrawCheckRow(menuSceneOk,
                menuSceneOk
                    ? ToolLang.Get("Menu scene with StoryLauncher present", "Сцена меню со StoryLauncher есть")
                    : ToolLang.Get("No menu scene with StoryLauncher in Build Settings", "В Build Settings нет сцены меню (со StoryLauncher)"),
                !menuSceneOk
                    ? ToolLang.Get("Open Сцены и Меню → New scene → apply «Main Menu» preset → save scene → add to Build Settings.", "Открой Сцены и Меню → создай сцену → примени пресет «Главное меню» → сохрани сцену → добавь в Build Settings.")
                    : null);

            // 7. Gameplay-сцена ≠ menu-сцена. Если совпадают — после старта
            // истории игрока кидает в то же меню вместо геймплея.
            if (story != null && !string.IsNullOrEmpty(story.GameSceneName) && menuSceneOk)
            {
                string menuName = Path.GetFileNameWithoutExtension(menuScenePath);
                bool different = menuName != story.GameSceneName;
                DrawCheckRow(different,
                    different
                        ? ToolLang.Get("Gameplay scene ≠ menu scene", "Игровая сцена ≠ сцена меню")
                        : ToolLang.Get("Gameplay scene IS the menu scene — game won't actually start", "Игровая сцена и сцена меню — одна и та же, игра не начнётся"),
                    !different
                        ? ToolLang.Get(
                            "Create a SEPARATE gameplay scene: open Сцены и Меню → New scene → apply «Gameplay» preset → save scene → add to Build → assign in Story Settings → Игровая сцена.",
                            "Создай ОТДЕЛЬНУЮ игровую сцену: Сцены и Меню → создай новую → примени пресет «Игровая сцена» → сохрани сцену → добавь в Build → назначь в Настройках истории → Игровая сцена.")
                        : null);
            }
        }

        // Возвращает путь к первой сцене из Build Settings, в которой есть
        // StoryLauncher. Для уже открытой в Editor сцены проверяем компоненты
        // напрямую (свежее, чем то что лежит на диске до Save). Для остальных —
        // читаем .unity текстом и ищем подстроку «StoryLauncher».
        private static string FindMenuScenePath()
        {
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled || string.IsNullOrEmpty(s.path) || !File.Exists(s.path)) continue;

                var loaded = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(s.path);
                if (loaded.IsValid() && loaded.isLoaded)
                {
                    foreach (var root in loaded.GetRootGameObjects())
                    {
                        if (root == null) continue;
                        if (root.GetComponentInChildren<NovellaEngine.Runtime.StoryLauncher>(true) != null)
                            return s.path;
                    }
                    continue; // загруженная сцена осмотрена полностью — на диск не лезем
                }

                try { if (File.ReadAllText(s.path).Contains("StoryLauncher")) return s.path; }
                catch { /* ignore */ }
            }
            return null;
        }

        private void DrawCheckRow(bool ok, string label, string hint)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(28);
            Rect r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = ok ? new Color(0.40f, 0.78f, 0.45f) : new Color(0.92f, 0.36f, 0.36f);
            GUI.Label(new Rect(r.x, r.y, 18, r.height), ok ? "✓" : "✕", iconSt);

            var labelSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            labelSt.normal.textColor = ok ? C_TEXT_1 : new Color(1f, 0.65f, 0.5f);
            GUI.Label(new Rect(r.x + 18, r.y, r.width - 18, r.height), label, labelSt);

            if (!ok && !string.IsNullOrEmpty(hint))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(46);
                var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                hintSt.normal.textColor = C_TEXT_3;
                GUILayout.Label(hint, hintSt);
                GUILayout.Space(24);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPlatformCards(Rect position)
        {
            bool canBuild = CanBuild(out _);

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var hSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            hSt.normal.textColor = C_TEXT_2;
            GUILayout.Label(ToolLang.Get("PLATFORMS", "ПЛАТФОРМЫ").ToUpperInvariant(), hSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Три карточки в ряд, при узком окне — в столбик.
            float availW = position.width - 48f;
            bool stack = availW < 720f;

            if (stack)
            {
                DrawPlatformCard(BuildTarget.StandaloneWindows64, "🖥", "Windows (.exe)",
                    ToolLang.Get("Standalone build for Windows (64-bit). Produces a folder with .exe + game data — share the whole folder; running the .exe alone will not work.",
                                 "Standalone-сборка под Windows (64-bit). Получается папка с .exe и данными игры — раздавать нужно всю папку целиком; запустить только .exe в отрыве от неё не получится."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.StandaloneOSX, "🍎", "macOS (.app)",
                    ToolLang.Get(
                        "Standalone build for Mac (Intel + Apple Silicon). Note: a real Mac is required to produce a working .app — Unity can build the file from Windows, but it won't run on macOS without code-signing on a Mac. Search 'Unity build for Mac on Windows' on YouTube for the full setup.",
                        "Standalone-сборка под Mac (Intel + Apple Silicon). Учти: чтобы получить рабочее .app, нужен сам Mac — Unity соберёт файл из-под Windows, но без подписи на Mac он не запустится. Подробный гайд — поиск «Unity build for Mac on Windows» на YouTube."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.WebGL, "🌐", "WebGL",
                    ToolLang.Get("Browser build. Plays in any browser without install. In development — not yet available.",
                                 "Сборка под браузер. Играется в любом браузере без установки. В разработке — пока недоступно."),
                    canBuild, comingSoon: true);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                DrawPlatformCard(BuildTarget.StandaloneWindows64, "🖥", "Windows (.exe)",
                    ToolLang.Get("Standalone build for Windows (64-bit). Produces a folder with .exe + game data — share the whole folder; running the .exe alone will not work.",
                                 "Standalone-сборка под Windows (64-bit). Получается папка с .exe и данными игры — раздавать нужно всю папку целиком; запустить только .exe в отрыве от неё не получится."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.StandaloneOSX, "🍎", "macOS (.app)",
                    ToolLang.Get(
                        "Standalone build for Mac (Intel + Apple Silicon). Note: a real Mac is required to produce a working .app — Unity can build the file from Windows, but it won't run on macOS without code-signing on a Mac. Search 'Unity build for Mac on Windows' on YouTube for the full setup.",
                        "Standalone-сборка под Mac (Intel + Apple Silicon). Учти: чтобы получить рабочее .app, нужен сам Mac — Unity соберёт файл из-под Windows, но без подписи на Mac он не запустится. Подробный гайд — поиск «Unity build for Mac on Windows» на YouTube."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.WebGL, "🌐", "WebGL",
                    ToolLang.Get("Browser build. Plays in any browser without install. In development — not yet available.",
                                 "Сборка под браузер. Играется в любом браузере без установки. В разработке — пока недоступно."),
                    canBuild, comingSoon: true);
                GUILayout.Space(24);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPlatformCard(BuildTarget target, string icon, string label, string desc, bool enabled, bool comingSoon = false)
        {
            var cardSt = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(14, 14, 14, 14) };
            GUILayout.BeginVertical(cardSt, GUILayout.MinWidth(220));

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 28, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = comingSoon ? C_TEXT_3 : C_TEXT_1;
            GUILayout.Label(icon, iconSt);

            var labelSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            labelSt.normal.textColor = comingSoon ? C_TEXT_3 : C_TEXT_1;
            GUILayout.Label(label, labelSt);

            var descSt = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 10 };
            descSt.normal.textColor = comingSoon ? C_TEXT_4 : C_TEXT_3;
            GUILayout.Label(desc, descSt);

            GUILayout.Space(10);

            if (comingSoon)
            {
                // Серая «Coming soon» — некликабельная заглушка.
                using (new EditorGUI.DisabledScope(true))
                {
                    GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Button("⏳ " + ToolLang.Get("Coming soon", "Скоро будет"), GUILayout.Height(34));
                    GUI.backgroundColor = Color.white;
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(!enabled || _isBuilding))
                {
                    GUI.backgroundColor = _isBuilding ? new Color(0.5f, 0.5f, 0.5f) : C_ACCENT;
                    string btnText = _isBuilding
                        ? "⏳ " + ToolLang.Get("Building…", "Сборка…")
                        : "📦 " + ToolLang.Get("Build", "Собрать");
                    if (GUILayout.Button(btnText, GUILayout.Height(34)))
                    {
                        // delayCall: модальные диалоги (SaveFolderPanel,
                        // DisplayDialog) внутри StartBuild ломают GUILayout-стек
                        // если их позвать прямо из обработчика клика. Откладываем
                        // на следующий тик — IMGUI к этому моменту уже закроется.
                        var captured = target;
                        EditorApplication.delayCall += () => StartBuild(captured);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawLastReport()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var hSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            hSt.normal.textColor = C_TEXT_2;
            GUILayout.Label(ToolLang.Get("LAST BUILD", "ПОСЛЕДНЯЯ СБОРКА").ToUpperInvariant(), hSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Стиль текста.
            var st = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11, padding = new RectOffset(10, 10, 8, 8) };
            st.normal.textColor = _lastReportError ? new Color(1f, 0.65f, 0.5f) : C_TEXT_1;

            // Считаем нужную высоту по реальному тексту + ширине viewport,
            // иначе многострочный отчёт обрезается на 50px.
            float availW = Mathf.Max(200f, EditorGUIUtility.currentViewWidth - 28f - 24f - 14f);
            float h = Mathf.Max(50f, st.CalcHeight(new GUIContent(_lastReport), availW));

            Color bg = _lastReportError
                ? new Color(0.92f, 0.36f, 0.36f, 0.10f)
                : new Color(0.40f, 0.78f, 0.45f, 0.10f);
            Color border = _lastReportError
                ? new Color(0.92f, 0.36f, 0.36f, 0.5f)
                : new Color(0.40f, 0.78f, 0.45f, 0.5f);

            GUILayout.BeginHorizontal();
            GUILayout.Space(28);
            Rect r = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, border);
            GUI.Label(r, _lastReport, st);

            // Кнопка «📋 Скопировать» — удобно отправить лог в issue/чат.
            if (_lastReportError)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Space(28);
                if (GUILayout.Button("📋 " + ToolLang.Get("Copy to clipboard", "Скопировать в буфер"),
                    GUILayout.Height(22), GUILayout.Width(220)))
                {
                    EditorGUIUtility.systemCopyBuffer = _lastReport;
                    NovellaToast.Info(ToolLang.Get("Copied to clipboard", "Скопировано в буфер"));
                }
                GUILayout.FlexibleSpace();
                GUILayout.Space(24);
                GUILayout.EndHorizontal();
            }
        }

        // ─── Build pipeline ──────────────────────────────────────────────────

        // CanBuild возвращает первую ошибку коротко (для DisabledScope на
        // карточках). Полный список см. CollectBuildIssues — он используется
        // в pre-build диалоге, чтобы показать ВСЁ что не так за один заход.
        private bool CanBuild(out string reason)
        {
            var issues = CollectBuildIssues();
            if (issues.Count == 0) { reason = ""; return true; }
            reason = issues[0];
            return false;
        }

        // Собирает все блокеры сборки. Каждый пункт — готовая строка для
        // показа юзеру, с указанием куда идти чтобы исправить.
        private List<string> CollectBuildIssues()
        {
            var issues = new List<string>();
            var (story, _) = ResolveActiveStory();

            if (story == null)
            {
                issues.Add(ToolLang.Get(
                    "• No active story selected.\n  → Open Novella Studio → Главная → pick a story or click '+ Новая история'.",
                    "• Нет активной истории.\n  → Открой Novella Studio → Главная → выбери историю или нажми «+ Новая история»."));
                return issues; // дальше нечего проверять
            }

            if (story.StartingChapter == null)
            {
                issues.Add(ToolLang.Get(
                    "• Story '" + story.Title + "' has no starting chapter.\n  → Open Story Settings → 'Стартовая глава' and pick one.",
                    "• У истории «" + story.Title + "» не задана стартовая глава.\n  → Настройки истории → «Стартовая глава» → выбери главу."));
            }

            if (string.IsNullOrEmpty(story.GameSceneName))
            {
                issues.Add(ToolLang.Get(
                    "• Story '" + story.Title + "' has no gameplay scene.\n  → Open Story Settings → 'Игровая сцена' → pick from Gallery. If you have no scenes — open UI Forge → Сцены и Меню → create one.",
                    "• У истории «" + story.Title + "» не задана игровая сцена.\n  → Настройки истории → «Игровая сцена» → выбери в Галерее. Если сцен нет — открой Сцены и Меню → создай новую."));
            }

            // Game scene file actually exists.
            if (story.GameSceneAsset == null && !string.IsNullOrEmpty(story.GameSceneName))
            {
                issues.Add(ToolLang.Get(
                    "• Gameplay scene '" + story.GameSceneName + "' file is missing on disk.\n  → Reassign in Story Settings → 'Игровая сцена'.",
                    "• Файл игровой сцены «" + story.GameSceneName + "» не найден на диске.\n  → Переназначь в Настройках истории → «Игровая сцена»."));
            }

            // Build Settings: at least one scene.
            int enabled = 0;
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path)) enabled++;
            if (enabled == 0)
            {
                issues.Add(ToolLang.Get(
                    "• No valid scenes in Build Settings.\n  → File → Build Profiles → drag your scenes into the Scene List.",
                    "• В Build Settings нет валидных сцен.\n  → File → Build Profiles → перетащи свои сцены в Scene List."));
            }
            else
            {
                // Game scene must be in Build Settings.
                if (!string.IsNullOrEmpty(story.GameSceneName))
                {
                    bool gameOk = false;
                    foreach (var s in EditorBuildSettings.scenes)
                    {
                        if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;
                        if (Path.GetFileNameWithoutExtension(s.path) == story.GameSceneName) { gameOk = true; break; }
                    }
                    if (!gameOk)
                    {
                        issues.Add(ToolLang.Get(
                            "• Gameplay scene '" + story.GameSceneName + "' is NOT in Build Settings.\n  → Story Settings → click '➕ В сборку' next to the scene picker.",
                            "• Игровая сцена «" + story.GameSceneName + "» не добавлена в Build Settings.\n  → Настройки истории → нажми «➕ В сборку» рядом с пикером сцены."));
                    }
                }

                // Menu scene with StoryLauncher must exist in Build Settings — это
                // entry point игры. Без неё сборка стартует «в пустоту».
                // Используем общий helper FindMenuScenePath — он умеет проверять
                // загруженные сцены через in-memory компоненты (не упирается в
                // несохранённый файл на диске).
                string menuPath = FindMenuScenePath();
                if (string.IsNullOrEmpty(menuPath))
                {
                    issues.Add(ToolLang.Get(
                        "• No menu scene found in Build Settings (a scene with StoryLauncher).\n  → Open Сцены и Меню → New scene → apply 'Main Menu' preset → save scene → add it to Build Settings.",
                        "• Не найдена сцена меню в Build Settings (сцена со StoryLauncher).\n  → Открой Сцены и Меню → создай новую сцену → примени пресет «Главное меню» → сохрани сцену → добавь в Build Settings."));
                }
                else if (!string.IsNullOrEmpty(story.GameSceneName))
                {
                    // Gameplay-сцена ≠ menu-сцена. Если совпадают — после
                    // старта истории игрока кидает обратно в меню.
                    string menuName = Path.GetFileNameWithoutExtension(menuPath);
                    if (menuName == story.GameSceneName)
                    {
                        issues.Add(ToolLang.Get(
                            "• Gameplay scene and menu scene are the SAME scene ('" + menuName + "'). Player will be sent back to the menu instead of starting the story.\n  → Create a separate gameplay scene: Сцены и Меню → New scene → apply 'Gameplay' preset → save scene → add to Build Settings → assign in Story Settings → 'Игровая сцена'.",
                            "• Игровая сцена и сцена меню — одна и та же сцена («" + menuName + "»). После старта истории игрока вернёт в меню вместо начала игры.\n  → Создай отдельную игровую сцену: Сцены и Меню → создай новую → примени пресет «Игровая сцена» → сохрани сцену → добавь в Build Settings → назначь в Настройках истории → «Игровая сцена»."));
                    }
                }
            }

            return issues;
        }

        private (NovellaStory, string) ResolveActiveStory()
        {
            string guid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            if (string.IsNullOrEmpty(guid))
                return (null, ToolLang.Get("Pick an active story in the sidebar.", "Выбери активную историю в боковой панели."));
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var st = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
            if (st == null)
                return (null, ToolLang.Get("Active story asset not found.", "Ассет активной истории не найден."));
            return (st, "");
        }

        private void StartBuild(BuildTarget target)
        {
            if (_isBuilding) return; // защита от двойного клика

            var issues = CollectBuildIssues();
            if (issues.Count > 0)
            {
                string body = ToolLang.Get(
                    "Cannot build — fix the items below first:\n\n",
                    "Сборка невозможна — сначала исправь:\n\n")
                    + string.Join("\n\n", issues);
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot build", "Нельзя собрать"),
                    body, "OK");
                return;
            }

            // Спрашиваем папку для билда.
            string defaultFolder = EditorPrefs.GetString("Novella_LastBuildFolder",
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop));
            string defaultName = Application.productName + " " + GetTargetSuffix(target);

            string outFolder = EditorUtility.SaveFolderPanel(
                ToolLang.Get("Pick output folder", "Выбери папку для сборки"),
                defaultFolder, defaultName);
            if (string.IsNullOrEmpty(outFolder)) return;
            EditorPrefs.SetString("Novella_LastBuildFolder", outFolder);

            // Подтверждение перед началом — это последний шанс отменить.
            // BuildPlayer синхронный и блокирует Editor на минуты, прервать
            // его чисто нельзя (Unity показывает свой прогресс, который не
            // отменяется пользовательской кнопкой).
            bool go = EditorUtility.DisplayDialog(
                ToolLang.Get("Start build?", "Начать сборку?"),
                string.Format(ToolLang.Get(
                    "About to build {0} into:\n{1}\n\nUnity will be unresponsive for a few minutes (typically 2–10). Don't close the editor — wait for the result.\n\nThis is the last chance to cancel.",
                    "Сейчас будет собрана платформа {0} в:\n{1}\n\nUnity зависнет на несколько минут (обычно 2–10). Не закрывай редактор — дождись результата.\n\nЭто последний шанс отменить."),
                    target.ToString(), outFolder),
                ToolLang.Get("Build now", "Собрать"),
                ToolLang.Get("Cancel", "Отмена"));
            if (!go) return;

            // Собираем список сцен из Build Settings.
            // Фильтруем удалённые/несуществующие — иначе BuildPlayer ругается
            // «'X.unity' is an incorrect path for a scene file».
            // Нормализуем backslashes → forward slashes (Unity иногда отдаёт
            // win-style пути из EditorBuildSettings).
            var scenes = new List<string>();
            var skippedScenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;
                string p = s.path.Replace('\\', '/');
                if (!System.IO.File.Exists(p))
                {
                    skippedScenes.Add(p);
                    continue;
                }
                scenes.Add(p);
            }
            if (skippedScenes.Count > 0)
            {
                Debug.LogWarning("[Novella Build] Skipping missing scenes from Build Settings:\n  " +
                    string.Join("\n  ", skippedScenes) +
                    "\n\nFix: open Unity → File → Build Profiles → remove these entries.");
                NovellaToast.Warning(string.Format(ToolLang.Get(
                    "Skipped {0} missing scene(s) — see Console.",
                    "Пропущено {0} удалённых сцен — смотри Console."), skippedScenes.Count));
            }
            if (scenes.Count == 0)
            {
                _lastReport = ToolLang.Get(
                    "❌ Build aborted: no valid scenes in Build Settings. Open Unity → File → Build Profiles and add at least one scene that exists on disk.",
                    "❌ Сборка отменена: в Build Settings нет валидных сцен. Открой Unity → File → Build Profiles и добавь хотя бы одну существующую сцену.");
                _lastReportError = true;
                NovellaToast.Error(ToolLang.Get("No valid scenes — see report", "Нет валидных сцен — смотри отчёт"));
                _window?.Repaint();
                return;
            }

            string locationPath = ResolveLocationPath(outFolder, target);

            var options = new BuildPlayerOptions
            {
                scenes        = scenes.ToArray(),
                locationPathName = locationPath,
                target        = target,
                targetGroup   = BuildPipeline.GetBuildTargetGroup(target),
                options       = BuildOptions.None,
            };

            // Если активная цель отличается — переключаем (Unity может попросить
            // подождать пока загрузит платформенные модули).
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(options.targetGroup, target);
                if (!switched)
                {
                    EditorUtility.DisplayDialog(
                        ToolLang.Get("Build target not installed", "Платформа не установлена"),
                        string.Format(ToolLang.Get(
                            "Cannot switch to {0}. The platform module may not be installed in Unity Hub.\n\n" +
                            "Open Unity Hub → Installs → click the gear on your Unity version → «Add modules» → tick the platform.",
                            "Не получилось переключиться на {0}. Возможно платформенный модуль не установлен в Unity Hub.\n\n" +
                            "Открой Unity Hub → Installs → шестерёнка на твоей версии Unity → «Add modules» → отметь платформу."),
                            target.ToString()),
                        "OK");
                    return;
                }
            }

            BuildReport report;
            _isBuilding = true;
            EditorUtility.DisplayProgressBar(
                ToolLang.Get("Building", "Сборка"),
                ToolLang.Get("Building " + target + " — Unity may be unresponsive…",
                             "Идёт сборка " + target + " — Unity может зависнуть, это нормально…"),
                0.5f);
            _window?.Repaint();
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (System.Exception ex)
            {
                _lastReport = string.Format(ToolLang.Get(
                    "Build crashed: {0}", "Сборка упала: {0}"), ex.Message);
                _lastReportError = true;
                NovellaToast.Error(_lastReport);
                _isBuilding = false;
                EditorUtility.ClearProgressBar();
                _window?.Repaint();
                return;
            }
            finally
            {
                _isBuilding = false;
                EditorUtility.ClearProgressBar();
            }

            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                double mb = summary.totalSize / 1024.0 / 1024.0;
                _lastReport = string.Format(ToolLang.Get(
                    "✅ Built successfully\nSize: {0:F1} MB · Time: {1:F1}s\nFolder: {2}",
                    "✅ Сборка прошла успешно\nРазмер: {0:F1} MB · Время: {1:F1}с\nПапка: {2}"),
                    mb, summary.totalTime.TotalSeconds, outFolder);
                _lastReportError = false;
                NovellaToast.Success(ToolLang.Get("Build succeeded", "Сборка прошла успешно"));
                EditorUtility.RevealInFinder(outFolder);
            }
            else
            {
                // Вытаскиваем первые N error/exception сообщений из BuildReport,
                // чтобы юзер видел причину прямо в карточке без захода в консоль.
                var sb = new System.Text.StringBuilder();
                sb.AppendFormat(ToolLang.Get(
                    "❌ Build failed\nResult: {0}\nErrors: {1}",
                    "❌ Сборка не удалась\nРезультат: {0}\nОшибок: {1}"),
                    summary.result, summary.totalErrors);

                int shown = 0;
                const int MAX_SHOWN = 6;
                if (report.steps != null)
                {
                    foreach (var step in report.steps)
                    {
                        if (step.messages == null) continue;
                        foreach (var msg in step.messages)
                        {
                            if (msg.type != LogType.Error && msg.type != LogType.Exception) continue;
                            // Дублируем в Novella Console — чтобы видно было
                            // и в нашем модуле «Консоль», а не только Unity-овском.
                            NovellaConsoleStore.Push(msg.type, "[Build] " + (msg.content ?? ""));
                            if (shown == 0) sb.Append("\n\n");
                            sb.Append("• ");
                            string text = msg.content ?? "";
                            if (text.Length > 220) text = text.Substring(0, 217) + "…";
                            sb.AppendLine(text);
                            shown++;
                            if (shown >= MAX_SHOWN) break;
                        }
                        if (shown >= MAX_SHOWN) break;
                    }
                }

                if (shown == 0)
                {
                    sb.Append("\n\n");
                    sb.Append(ToolLang.Get(
                        "(No error messages in the build report — open Console for details. Often this is a compile error blocking the build entirely.)",
                        "(В отчёте сборки нет сообщений об ошибках — открой Console. Часто это compile-ошибка, блокирующая сборку целиком.)"));
                }
                else if (summary.totalErrors > shown)
                {
                    sb.AppendFormat(ToolLang.Get(
                        "\n…and {0} more — see Console.", "\n…и ещё {0} — смотри Console."),
                        summary.totalErrors - shown);
                }

                _lastReport = sb.ToString();
                _lastReportError = true;
                // Длинная жизнь у тоста, чтобы не пропадал за 3с пока юзер
                // переключается между окнами.
                NovellaToast.Push(
                    ToolLang.Get("Build failed — details below", "Сборка не удалась — детали ниже"),
                    NovellaToast.Kind.Error, 8f);
            }
            _window?.Repaint();
        }

        private static string ResolveLocationPath(string folder, BuildTarget target)
        {
            string product = SanitizeFileName(Application.productName);
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(folder, product + ".exe");
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(folder, product + ".app");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(folder, product);
                case BuildTarget.WebGL:
                    return folder; // папка — это уже корень WebGL build'a
                default:
                    return Path.Combine(folder, product);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Game";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "");
            return string.IsNullOrEmpty(name) ? "Game" : name;
        }

        private static string GetTargetSuffix(BuildTarget t)
        {
            switch (t)
            {
                case BuildTarget.StandaloneWindows64: return "Windows";
                case BuildTarget.StandaloneOSX:       return "macOS";
                case BuildTarget.WebGL:               return "WebGL";
                default:                              return t.ToString();
            }
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
