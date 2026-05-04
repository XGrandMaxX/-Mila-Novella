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
        private string _lastReport = "";
        private bool _lastReportError;

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

            // Заголовок.
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📦 " + ToolLang.Get("Build the game", "Собрать игру"), titleSt);
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
                enabledScenes == 0 ? ToolLang.Get("Open Scenes module → add at least one scene.", "Открой модуль Сцены → добавь хотя бы одну.") : null);

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
                        : ToolLang.Get("Gameplay scene is NOT in Build Settings", "Игровая сцены НЕТ в Build Settings"),
                    !gameSceneInBuild
                        ? ToolLang.Get("Open story settings → click «➕ Add to Build».", "Открой настройки истории → нажми «➕ В сборку».")
                        : null);
            }
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
                DrawPlatformCard(BuildTarget.StandaloneWindows64, "🪟", "Windows (.exe)",
                    ToolLang.Get("Standalone build for Windows. The most common option for Steam / itch.io.",
                                 "Standalone-сборка под Windows. Самый частый вариант для Steam / itch.io."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.StandaloneOSX, "🍎", "macOS (.app)",
                    ToolLang.Get("Standalone build for Mac (Intel + Apple Silicon).",
                                 "Standalone-сборка под Mac (Intel + Apple Silicon)."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.WebGL, "🌐", "WebGL",
                    ToolLang.Get("Browser build. Slow loading but plays in any browser without install.",
                                 "Сборка под браузер. Медленнее грузится, но играется в любом браузере без установки."), canBuild);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                DrawPlatformCard(BuildTarget.StandaloneWindows64, "🪟", "Windows (.exe)",
                    ToolLang.Get("Standalone build for Windows. The most common option for Steam / itch.io.",
                                 "Standalone-сборка под Windows. Самый частый вариант для Steam / itch.io."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.StandaloneOSX, "🍎", "macOS (.app)",
                    ToolLang.Get("Standalone build for Mac (Intel + Apple Silicon).",
                                 "Standalone-сборка под Mac (Intel + Apple Silicon)."), canBuild);
                GUILayout.Space(8);
                DrawPlatformCard(BuildTarget.WebGL, "🌐", "WebGL",
                    ToolLang.Get("Browser build. Slow loading but plays in any browser without install.",
                                 "Сборка под браузер. Медленнее грузится, но играется в любом браузере без установки."), canBuild);
                GUILayout.Space(24);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPlatformCard(BuildTarget target, string icon, string label, string desc, bool enabled)
        {
            var cardSt = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(14, 14, 14, 14) };
            GUILayout.BeginVertical(cardSt, GUILayout.MinWidth(220));

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 28, alignment = TextAnchor.MiddleLeft };
            iconSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(icon, iconSt);

            var labelSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            labelSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(label, labelSt);

            var descSt = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontSize = 10 };
            descSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(desc, descSt);

            GUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!enabled))
            {
                GUI.backgroundColor = C_ACCENT;
                if (GUILayout.Button("📦 " + ToolLang.Get("Build", "Собрать"), GUILayout.Height(34)))
                {
                    StartBuild(target);
                }
                GUI.backgroundColor = Color.white;
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

            GUILayout.BeginHorizontal();
            GUILayout.Space(28);
            Rect r = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.MinHeight(50));
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            Color bg = _lastReportError
                ? new Color(0.92f, 0.36f, 0.36f, 0.10f)
                : new Color(0.40f, 0.78f, 0.45f, 0.10f);
            Color border = _lastReportError
                ? new Color(0.92f, 0.36f, 0.36f, 0.5f)
                : new Color(0.40f, 0.78f, 0.45f, 0.5f);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, border);

            var st = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11, padding = new RectOffset(10, 10, 8, 8) };
            st.normal.textColor = _lastReportError ? new Color(1f, 0.65f, 0.5f) : C_TEXT_1;
            GUI.Label(r, _lastReport, st);
        }

        // ─── Build pipeline ──────────────────────────────────────────────────

        private bool CanBuild(out string reason)
        {
            var (story, _) = ResolveActiveStory();
            if (story == null)
            {
                reason = ToolLang.Get("No active story.", "Нет активной истории.");
                return false;
            }
            if (story.StartingChapter == null)
            {
                reason = ToolLang.Get("Starting chapter not set.", "Нет стартовой главы.");
                return false;
            }
            if (string.IsNullOrEmpty(story.GameSceneName))
            {
                reason = ToolLang.Get("Gameplay scene not assigned.", "Нет игровой сцены.");
                return false;
            }
            int enabled = 0;
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) enabled++;
            if (enabled == 0)
            {
                reason = ToolLang.Get("No scenes in Build Settings.", "Нет сцен в Build Settings.");
                return false;
            }
            reason = "";
            return true;
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
            if (!CanBuild(out string reason))
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot build", "Нельзя собрать"),
                    reason, "OK");
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

            // Собираем список сцен из Build Settings.
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path)) scenes.Add(s.path);
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
                _window?.Repaint();
                return;
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
                _lastReport = string.Format(ToolLang.Get(
                    "❌ Build failed\nResult: {0}\nErrors: {1}\n\nCheck the console for details.",
                    "❌ Сборка не удалась\nРезультат: {0}\nОшибок: {1}\n\nДетали смотри в консоли."),
                    summary.result, summary.totalErrors);
                _lastReportError = true;
                NovellaToast.Error(ToolLang.Get("Build failed — see console", "Сборка не удалась — смотри консоль"));
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
