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
        private Vector2 _scrollPos;

        private int _lastSeenProgress = -1;
        private float _unlockAnimStartTime = -99f;
        private int _unlockedStepIndex = -1;
        private float _targetScrollY = 0f;

        private int _selectedTutorialIndex = 1;

        public bool _canClose = false;

        static NovellaWelcomeWindow() { EditorApplication.delayCall += ShowWindowOnFirstLaunch; }

        private static void ShowWindowOnFirstLaunch()
        {
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
            var window = GetWindow<NovellaWelcomeWindow>(true, "Novella Engine", true);
            window._canClose = false;

            Rect mainRect = EditorGUIUtility.GetMainWindowPosition();
            window.position = mainRect;
            window.minSize = new Vector2(mainRect.width, mainRect.height);
            window.maxSize = new Vector2(mainRect.width, mainRect.height);

            window.ShowPopup();
        }

        private void OnEnable()
        {
            _tutorialProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);
            _lastSeenProgress = _tutorialProgress;
            _selectedTutorialIndex = Mathf.Clamp(_tutorialProgress, 1, 6);
            EditorApplication.update += EnsureFullscreen;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EnsureFullscreen;
        }

        private void EnsureFullscreen()
        {
            Rect mainPos = EditorGUIUtility.GetMainWindowPosition();
            if (position.x != mainPos.x || position.y != mainPos.y || position.width != mainPos.width || position.height != mainPos.height)
            {
                position = mainPos;
            }
            Repaint();
        }

        private void OnDestroy()
        {
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
            if (animT < 1.5f && _unlockedStepIndex != -1)
            {
                _scrollPos.y = Mathf.Lerp(_scrollPos.y, _targetScrollY, 0.05f);
                Repaint();
            }

            InitStyles();

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("↺ " + ToolLang.Get("Restart Tutorial", "Перепройти туториал"), EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                _tutorialProgress = 1; _lastSeenProgress = 1; _unlockedStepIndex = -1; _selectedTutorialIndex = 1;
                EditorPrefs.SetInt("Novella_TutorialProgress", 1);
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
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Welcome to Novella Engine! 🚀", "Добро пожаловать в Novella Engine! 🚀"), new GUIStyle(EditorStyles.largeLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 28, fontStyle = FontStyle.Bold });
            GUILayout.Label(ToolLang.Get("Your visual novel journey starts here. Click on the blocks!", "Ваш путь в создании визуальных новелл начинается здесь. Кликайте по блокам!"), new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = Color.gray } });
            GUILayout.Space(15);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(position.width * 0.65f));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            DrawVisualWorkflow(animT);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(position.width * 0.35f - 10));
            DrawRightSidePanel();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawRightSidePanel()
        {
            GUILayout.Space(20);

            string title = GetTutTitle(_selectedTutorialIndex);
            string desc = GetTutDesc(_selectedTutorialIndex);
            string extraInfo = GetTutExtraInfo(_selectedTutorialIndex);

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, wordWrap = true });
            GUILayout.Space(10);
            GUILayout.Label(desc, new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 14 });
            GUILayout.Space(15);
            GUILayout.Label(extraInfo, new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 13, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } });

            GUILayout.Space(30);

            bool isUnlocked = _tutorialProgress >= _selectedTutorialIndex;

            EditorGUI.BeginDisabledGroup(!isUnlocked);
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("▶ " + ToolLang.Get("PLAY TUTORIAL", "НАЧАТЬ УРОК"), new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold }, GUILayout.Height(60)))
            {
                PlayTutorial(_selectedTutorialIndex);
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            if (!isUnlocked)
            {
                GUILayout.Space(5);
                GUILayout.Label(ToolLang.Get("🔒 Complete previous steps to unlock", "🔒 Пройдите предыдущие шаги для разблокировки"), EditorStyles.centeredGreyMiniLabel);
            }

            GUILayout.FlexibleSpace();

            if (_tutorialProgress < 6)
            {
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
                if (GUILayout.Button("⏭ " + ToolLang.Get("SKIP TUTORIAL", "ПРОПУСТИТЬ ОБУЧЕНИЕ"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold }, GUILayout.Height(40)))
                {
                    _tutorialProgress = 6;
                    EditorPrefs.SetInt("Novella_TutorialProgress", 6);
                    _selectedTutorialIndex = 6;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10);
            }

            GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
            if (GUILayout.Button("🚀 " + ToolLang.Get("OPEN NOVELLA HUB", "ОТКРЫТЬ NOVELLA HUB"), new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold }, GUILayout.Height(50)))
            {
                _canClose = true;
                EditorApplication.delayCall += () => {
                    NovellaHubWindow.ShowWindow();
                    Close();
                };
                GUIUtility.ExitGUI(); // ФИКС ОШИБКИ GUILAYOUT
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(15);
            EditorGUI.BeginChangeCheck();
            bool dontShowAgain = EditorPrefs.GetBool("Novella_HasShownWelcome", false);
            dontShowAgain = GUILayout.Toggle(dontShowAgain, ToolLang.Get(" Do not show this window on startup", " Больше не показывать это окно при запуске Unity"), new GUIStyle(EditorStyles.toggle) { fontSize = 13 });
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool("Novella_HasShownWelcome", dontShowAgain);

            if (dontShowAgain)
            {
                GUILayout.Space(10);
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button(ToolLang.Get("✖ CLOSE WINDOW", "✖ ЗАКРЫТЬ ОКНО"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold }, GUILayout.Height(40)))
                {
                    _canClose = true;
                    EditorApplication.delayCall += () => {
                        NovellaHubWindow.ShowWindow();
                        Close();
                    };
                    GUIUtility.ExitGUI(); // ФИКС ОШИБКИ GUILAYOUT
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.Space(20);
        }

        private string GetTutTitle(int index)
        {
            if (index == 1) return ToolLang.Get("1. Scenes & Menu", "1. Сцены и Меню");
            if (index == 2) return ToolLang.Get("2. Actors & Variables", "2. Персонажи и Переменные");
            if (index == 3) return ToolLang.Get("3. Graph Editor", "3. Редактор Графа");
            if (index == 4) return ToolLang.Get("4. DLC Modules", "4. Модули DLC");
            if (index == 5) return ToolLang.Get("5. UI Forge", "5. UI Кузница");
            if (index == 6) return ToolLang.Get("6. Interactive Tutorial", "6. Интерактивный Урок");
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
            if (index == 2) return ToolLang.Get("💡 We will create our first character, set up their emotions, and add a global variable.", "💡 Мы создадим первого персонажа, настроим его эмоции и добавим глобальную переменную.");
            if (index == 3) return ToolLang.Get("💡 You will learn how to connect nodes, use auto-layout, and create story branches.", "💡 Вы узнаете, как связывать ноды, использовать авто-выравнивание и делать сюжетные ветвления.");
            if (index == 4) return ToolLang.Get("💡 We will explore the DLC Manager, enable a module, and see how to safely delete it.", "💡 Мы изучим Менеджер DLC, включим модуль и узнаем, как безопасно его удалить.");
            if (index == 5) return ToolLang.Get("💡 Time to make things pretty! We'll tweak colors, fonts, and dialogue box styles.", "💡 Время навести красоту! Мы изменим цвета, шрифты и стиль окна диалогов.");
            if (index == 6) return ToolLang.Get("💡 The final step. A massive interactive example showing the engine in action.", "💡 Финальный шаг. Огромный интерактивный пример, показывающий движок в действии.");
            return "";
        }

        private void PlayTutorial(int index)
        {
            if (index == 1) { NovellaSceneManagerWindow.ShowWindow(); NovellaTutorialManager.StartTutorial("SceneManager"); }
            else if (index == 2) { NovellaCharacterEditor.OpenWindow(); NovellaTutorialManager.StartTutorial("CharacterEditor"); }
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
            else if (index == 5) { NovellaUIEditorWindow.ShowWindow(); NovellaTutorialManager.StartTutorial("UIEditor"); }
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

            float titleH = _tileTitleStyle.CalcHeight(new GUIContent(title), titleWidth);
            float descH = _tileDescStyle.CalcHeight(new GUIContent(desc), descWidth);

            return Mathf.Max(150f, 20f + titleH + 15f + descH + 35f);
        }

        private void DrawVisualWorkflow(float animT)
        {
            float panelWidth = position.width * 0.65f;
            float tileW = (panelWidth - 120f) / 2f;
            float gapX = 40f;

            float h1 = CalculateTileHeight(GetTutTitle(1), GetTutDesc(1), tileW);
            float h2 = CalculateTileHeight(GetTutTitle(2), GetTutDesc(2), tileW);
            float h3 = CalculateTileHeight(GetTutTitle(3), GetTutDesc(3), tileW);
            float h4 = CalculateTileHeight(GetTutTitle(4), GetTutDesc(4), tileW);
            float h5 = CalculateTileHeight(GetTutTitle(5), GetTutDesc(5), tileW);
            float h6 = CalculateTileHeight(GetTutTitle(6), GetTutDesc(6), tileW);

            float t1Y = 20f;
            float t2Y = t1Y + 80f;
            float t3Y = t2Y + h2 + 60f;
            float t4Y = t3Y + 80f;
            float t5Y = t4Y + h4 + 60f;
            float t6Y = t5Y + 80f;

            float totalHeight = t6Y + h6 + 20f;
            Rect workArea = GUILayoutUtility.GetRect(panelWidth, totalHeight);

            float col1X = workArea.x + 40f;
            float col2X = col1X + tileW + gapX;

            Rect t1 = new Rect(col1X, workArea.y + t1Y, tileW, h1);
            Rect t2 = new Rect(col2X, workArea.y + t2Y, tileW, h2);
            Rect t3 = new Rect(col1X, workArea.y + t3Y, tileW, h3);
            Rect t4 = new Rect(col2X, workArea.y + t4Y, tileW, h4);
            Rect t5 = new Rect(col1X, workArea.y + t5Y, tileW, h5);
            Rect t6 = new Rect(col2X, workArea.y + t6Y, tileW, h6);

            DrawArrow(new Vector2(t1.xMax, t1.y + h1 / 2), new Vector2(t2.xMin, t2.y + h2 / 2), Vector2.right, Vector2.left, 120f, _tutorialProgress >= 2);
            DrawArrow(new Vector2(t2.center.x, t2.yMax), new Vector2(t3.center.x, t3.yMin), Vector2.down, Vector2.up, 120f, _tutorialProgress >= 3);
            DrawArrow(new Vector2(t3.xMax, t3.y + h3 / 2), new Vector2(t4.xMin, t4.y + h4 / 2), Vector2.right, Vector2.left, 120f, _tutorialProgress >= 4);
            DrawArrow(new Vector2(t4.center.x, t4.yMax), new Vector2(t5.center.x, t5.yMin), Vector2.down, Vector2.up, 120f, _tutorialProgress >= 5);
            DrawArrow(new Vector2(t5.xMax, t5.y + h5 / 2), new Vector2(t6.xMin, t6.y + h6 / 2), Vector2.right, Vector2.left, 120f, _tutorialProgress >= 6);

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

            if (isJustUnlocked)
            {
                _targetScrollY = rect.y - 200f;
                if (_targetScrollY < 0) _targetScrollY = 0;
            }

            Event e = Event.current;
            bool isHovered = rect.Contains(e.mousePosition);
            bool isSelected = _selectedTutorialIndex == stepIndex;

            Rect drawRect = new Rect(rect);

            if ((isHovered || isSelected) && isUnlocked && !NovellaTutorialManager.IsTutorialActive && !isAnimatingUnlock)
            {
                if (isHovered && !isSelected)
                {
                    float shakeX = Mathf.Sin((float)EditorApplication.timeSinceStartup * 45f) * 1.5f;
                    float shakeY = Mathf.Cos((float)EditorApplication.timeSinceStartup * 50f) * 1.5f;
                    drawRect.x += shakeX; drawRect.y += shakeY;
                }

                Color glowColor = isSelected ? new Color(0.2f, 0.8f, 0.4f, 0.5f) : new Color(0.2f, 0.6f, 1f, 0.25f);
                Rect glowRect = new Rect(drawRect.x - 3, drawRect.y - 3, drawRect.width + 6, drawRect.height + 6);
                EditorGUI.DrawRect(glowRect, glowColor);
            }

            GUI.Box(drawRect, GUIContent.none, _tileStyle);
            GUI.color = isUnlocked ? Color.white : new Color(1f, 1f, 1f, 0.3f);

            Rect iconRect = new Rect(drawRect.x + 15, drawRect.y + 15, 40, 40);
            GUI.Label(iconRect, icon, new GUIStyle(EditorStyles.label) { fontSize = 32 });

            Rect titleRect = new Rect(drawRect.x + 65, drawRect.y + 20, drawRect.width - 80, 30);
            GUIStyle currentTitleStyle = new GUIStyle(_tileTitleStyle);
            if (isFinalStep && isUnlocked) currentTitleStyle.normal.textColor = new Color(0.2f, 0.8f, 0.4f);
            GUI.Label(titleRect, title, currentTitleStyle);

            Rect descRect = new Rect(drawRect.x + 15, drawRect.y + 65, drawRect.width - 30, drawRect.height - 75);
            GUI.Label(descRect, desc, _tileDescStyle);

            GUI.color = Color.white;

            if (showAsLocked)
            {
                float lockAlpha = 1f;
                float lockOffsetX = 0f;
                float lockOffsetY = 0f;
                float overlayAlpha = 0.6f;

                if (isJustUnlocked)
                {
                    if (animT < 0.5f) lockOffsetX = Mathf.Sin(animT * 50f) * 6f;
                    else
                    {
                        float dropT = (animT - 0.5f) * 2f;
                        lockOffsetY = dropT * dropT * 60f;
                        lockAlpha = 1f - dropT;
                        overlayAlpha = 0.6f * (1f - dropT);
                    }
                }

                EditorGUI.DrawRect(drawRect, new Color(0.1f, 0.1f, 0.1f, overlayAlpha));

                if (lockAlpha > 0)
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(1, 1, 1, lockAlpha);
                    Rect lockRect = new Rect(drawRect.center.x - 20 + lockOffsetX, drawRect.center.y - 20 + lockOffsetY, 40, 40);
                    GUI.Label(lockRect, "🔒", new GUIStyle(EditorStyles.label) { fontSize = 36 });
                    GUI.color = oldColor;
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

        private void DrawArrow(Vector2 start, Vector2 end, Vector2 startDir, Vector2 endDir, float curveStrength, bool isUnlocked)
        {
            Vector3 startTangent = start + startDir * curveStrength;
            Vector3 endTangent = end + endDir * curveStrength;
            Color arrowColor = isUnlocked ? new Color(0.2f, 0.6f, 1f, 1f) : new Color(0.4f, 0.4f, 0.4f, 0.5f);

            Handles.DrawBezier(start, end, startTangent, endTangent, new Color(0, 0, 0, 0.4f), null, 6f);
            Handles.DrawBezier(start, end, startTangent, endTangent, arrowColor, null, 4f);

            Vector2 dir = (end - (Vector2)endTangent).normalized;
            if (dir == Vector2.zero) dir = (end - start).normalized;
            Vector2 right = new Vector2(-dir.y, dir.x);

            Vector2 p1 = end - dir * 16f + right * 9f;
            Vector2 p2 = end - dir * 16f - right * 9f;

            Handles.color = arrowColor;
            Handles.DrawAAConvexPolygon(end, p1, p2);
            Handles.color = Color.white;
        }
    }
}