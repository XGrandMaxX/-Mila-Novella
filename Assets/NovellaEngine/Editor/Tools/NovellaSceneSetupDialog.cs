// ════════════════════════════════════════════════════════════════════════════
// NovellaSceneSetupDialog — модальное окно «➕ Сцена» для Кузницы UI.
// Альтернатива GenericMenu: показывает большие карточки с описанием для
// NovellaPlayer и StoryLauncher, плюс пресеты. Юзер видит что выбирает.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaSceneSetupDialog : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();
        private static readonly Color C_PURPLE = new Color(0.65f, 0.45f, 0.95f);
        private static readonly Color C_AMBER  = new Color(0.95f, 0.66f, 0.30f);

        // Колбэки которые юзер вешает при показе. Внутри они ходят в Forge —
        // диалог сам про Forge ничего не знает.
        private Action _onAddPlayer;
        private Action _onAddLauncher;
        private Action _onApplyMenu;
        private Action _onApplyGameplay;
        private bool _playerExists;
        private bool _launcherExists;

        // Кэш hover-состояний для каждой карточки — чтобы Repaint вызывался
        // только при реальной смене hover, а не на каждое MouseMove.
        private bool _hoverPlayer, _hoverLauncher, _hoverMenuPreset, _hoverGameplayPreset;

        public static void Show(
            bool playerExists, bool launcherExists,
            Action onAddPlayer, Action onAddLauncher,
            Action onApplyMenu, Action onApplyGameplay)
        {
            var win = CreateInstance<NovellaSceneSetupDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Add scene component", "Добавить компонент сцены"));
            win._playerExists    = playerExists;
            win._launcherExists  = launcherExists;
            win._onAddPlayer     = onAddPlayer;
            win._onAddLauncher   = onAddLauncher;
            win._onApplyMenu     = onApplyMenu;
            win._onApplyGameplay = onApplyGameplay;

            // Центруем относительно главного окна Unity.
            var size = new Vector2(640, 540);
            var main = EditorGUIUtility.GetMainWindowPosition();
            win.position = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x, size.y);
            win.minSize = size; win.maxSize = size;
            win.ShowUtility();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(20);

            // Header.
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🛠 " + ToolLang.Get("Set up the scene", "Сборка сцены"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Add a runtime component to your scene, or apply a full preset.",
                "Добавь runtime-компонент в сцену, или применить полный пресет."),
                subSt, GUILayout.Width(580));
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(14);

            // ── Section 1: Components ───────────────────────────────────
            DrawSectionLabel(ToolLang.Get("RUNTIME COMPONENTS", "RUNTIME-КОМПОНЕНТЫ"));

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);

            DrawComponentCard(
                "🎮", ToolLang.Get("NovellaPlayer", "NovellaPlayer"), C_ACCENT,
                ToolLang.Get(
                    "Plays the story graph at runtime: prints dialogue, spawns choice buttons, switches characters and backgrounds.",
                    "Воспроизводит граф истории в рантайме: печатает реплики, спавнит кнопки выбора, переключает персонажей и фоны."),
                ToolLang.Get("Required for gameplay scenes", "Нужен для игровых сцен"),
                _playerExists,
                () => { _onAddPlayer?.Invoke(); Close(); },
                ref _hoverPlayer);

            GUILayout.Space(14);

            DrawComponentCard(
                "📱", ToolLang.Get("StoryLauncher", "StoryLauncher"), C_PURPLE,
                ToolLang.Get(
                    "Wires up Main Menu buttons (Start / Settings / Exit), spawns story cards, drives MC Creation panel.",
                    "Привязывает кнопки меню (Старт / Настройки / Выход), спавнит карточки историй, рулит панелью создания персонажа."),
                ToolLang.Get("Required for menu scenes", "Нужен для меню"),
                _launcherExists,
                () => { _onAddLauncher?.Invoke(); Close(); },
                ref _hoverLauncher);

            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(14);

            // ── Section 2: Full presets ─────────────────────────────────
            DrawSectionLabel(ToolLang.Get("FULL SCENE PRESETS", "ПОЛНЫЕ ПРЕСЕТЫ СЦЕНЫ"));

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);

            DrawPresetMiniCard(
                "📱", ToolLang.Get("Main Menu", "Главное Меню"), C_PURPLE,
                ToolLang.Get(
                    "Canvas + StoryLauncher + Stories panel + MC Creation, all wired up.",
                    "Canvas + StoryLauncher + панель историй + создание персонажа, всё привязано."),
                () => { _onApplyMenu?.Invoke(); Close(); },
                ref _hoverMenuPreset);

            GUILayout.Space(14);

            DrawPresetMiniCard(
                "🎮", ToolLang.Get("Gameplay", "Игровая сцена"), C_ACCENT,
                ToolLang.Get(
                    "Canvas + DialogueBox + Character_Layer + ChoiceContainer + NovellaPlayer.",
                    "Canvas + DialogueBox + Character_Layer + ChoiceContainer + NovellaPlayer."),
                () => { _onApplyGameplay?.Invoke(); Close(); },
                ref _hoverGameplayPreset);

            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            // ── Footer: close ──
            GUILayout.FlexibleSpace();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var closeSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 24, padding = new RectOffset(16, 16, 2, 2) };
            closeSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), closeSt, GUILayout.Width(100)))
            {
                Close();
            }
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }

        // Большая карточка для NovellaPlayer / StoryLauncher с описанием,
        // ярлыком когда нужен, и CTA-кнопкой. Если компонент уже в сцене —
        // карточка disabled с галочкой.
        private void DrawComponentCard(string icon, string title, Color brand,
            string description, string usage, bool exists,
            Action onClick, ref bool hover)
        {
            const float cardW = 290f, cardH = 230f;
            Rect r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));

            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove)
            {
                bool h = !exists && r.Contains(Event.current.mousePosition);
                if (h != hover)
                {
                    hover = h;
                    if (Event.current.type == EventType.MouseMove) Repaint();
                }
            }

            // Фон. Серый если уже добавлено.
            Color bg = exists
                ? new Color(0.5f, 0.5f, 0.5f, 0.10f)
                : (hover ? new Color(brand.r, brand.g, brand.b, 0.24f)
                         : new Color(brand.r, brand.g, brand.b, 0.12f));
            EditorGUI.DrawRect(r, bg);

            Color border = exists
                ? new Color(0.5f, 0.5f, 0.5f, 0.4f)
                : (hover ? brand : new Color(brand.r, brand.g, brand.b, 0.55f));
            DrawBorder(r, border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), exists ? new Color(0.5f, 0.5f, 0.5f, 0.5f) : brand);

            // Иконка слева сверху.
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = exists ? C_TEXT_4 : brand;
            GUI.Label(new Rect(r.x + 12, r.y + 16, 50, 40), icon, iconSt);

            // Title.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            titleSt.normal.textColor = exists ? C_TEXT_3 : C_TEXT_1;
            GUI.Label(new Rect(r.x + 70, r.y + 18, r.width - 80, 22), title, titleSt);

            // Usage hint.
            var usageSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            usageSt.normal.textColor = exists ? C_TEXT_4 : new Color(brand.r, brand.g, brand.b, 0.85f);
            GUI.Label(new Rect(r.x + 70, r.y + 38, r.width - 80, 14), usage, usageSt);

            // Description.
            var descSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            descSt.normal.textColor = exists ? C_TEXT_4 : C_TEXT_2;
            GUI.Label(new Rect(r.x + 14, r.y + 70, r.width - 28, 100), description, descSt);

            // CTA / status.
            Rect ctaR = new Rect(r.x + 14, r.yMax - 42, r.width - 28, 30);
            if (exists)
            {
                EditorGUI.DrawRect(ctaR, new Color(0.40f, 0.78f, 0.45f, 0.20f));
                var okSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                okSt.normal.textColor = new Color(0.40f, 0.78f, 0.45f);
                GUI.Label(ctaR, "✓ " + ToolLang.Get("Already in scene", "Уже в сцене"), okSt);
            }
            else
            {
                EditorGUI.DrawRect(ctaR, hover ? brand : new Color(brand.r, brand.g, brand.b, 0.75f));
                var ctaSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                ctaSt.normal.textColor = Color.white;
                GUI.Label(ctaR, ToolLang.Get("➕ Add to scene", "➕ Добавить в сцену"), ctaSt);

                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    onClick?.Invoke();
                    Event.current.Use();
                }
            }
        }

        // Маленькая карточка пресета снизу: иконка + название + описание + CTA.
        private void DrawPresetMiniCard(string icon, string title, Color brand,
            string description, Action onClick, ref bool hover)
        {
            const float cardW = 290f, cardH = 110f;
            Rect r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));

            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove)
            {
                bool h = r.Contains(Event.current.mousePosition);
                if (h != hover)
                {
                    hover = h;
                    if (Event.current.type == EventType.MouseMove) Repaint();
                }
            }

            Color bg = hover
                ? new Color(brand.r, brand.g, brand.b, 0.20f)
                : new Color(brand.r, brand.g, brand.b, 0.10f);
            EditorGUI.DrawRect(r, bg);
            DrawBorder(r, hover ? brand : new Color(brand.r, brand.g, brand.b, 0.45f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), brand);

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = brand;
            GUI.Label(new Rect(r.x + 12, r.y + 12, 40, 30), icon, iconSt);

            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            titleSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 56, r.y + 14, r.width - 64, 22), title + "  ▸", titleSt);

            var descSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            descSt.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x + 14, r.y + 42, r.width - 28, 60), description, descSt);

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                Event.current.Use();
            }
        }

        private void DrawSectionLabel(string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_3;
            GUILayout.Label(text, st);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
