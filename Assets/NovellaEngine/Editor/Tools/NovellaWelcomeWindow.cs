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

        static NovellaWelcomeWindow() { EditorApplication.delayCall += ShowWindowOnFirstLaunch; }

        private static void ShowWindowOnFirstLaunch()
        {
            if (!EditorPrefs.GetBool("Novella_HasShownWelcome", false)) ShowWindow();
        }

        [MenuItem("Window/Novella Engine/📖 Welcome Tutorial", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<NovellaWelcomeWindow>(true, "Novella Engine", true);
            window.minSize = new Vector2(850, 850);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            _tutorialProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout && EditorWindow.focusedWindow == this)
            {
                if (NovellaTutorialManager.IsTutorialActive)
                {
                    NovellaTutorialManager.ForceStopTutorial();
                }
            }

            _tutorialProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);

            InitStyles();

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            // ИСПРАВЛЕНО: Текст кнопки изменен на "Перепройти туториал"
            if (GUILayout.Button("↺ " + ToolLang.Get("Restart Tutorial", "Перепройти туториал"), EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                _tutorialProgress = 1;
                EditorPrefs.SetInt("Novella_TutorialProgress", 1);
                EditorPrefs.SetBool("Novella_HasShownWelcome", false);
                EditorPrefs.SetBool("Novella_Tut_SceneManager", false);
                EditorPrefs.SetBool("Novella_Tut_CharacterEditor", false);
                EditorPrefs.SetBool("Novella_Tut_UIEditor", false);
                EditorPrefs.SetBool("Novella_Tut_InteractiveLesson", false);
                EditorPrefs.SetBool("Novella_Tut_GraphEditor", false);
                NovellaTutorialManager.ForceStopTutorial();
            }

            GUILayout.FlexibleSpace();

            string langBtnText = ToolLang.IsRU ? "EN" : "RU";
            if (GUILayout.Button(langBtnText, EditorStyles.toolbarButton, GUILayout.Width(40))) { ToolLang.Toggle(); }
            GUILayout.EndHorizontal();

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Welcome to Novella Engine! 🚀", "Добро пожаловать в Novella Engine! 🚀"), new GUIStyle(EditorStyles.largeLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 28, fontStyle = FontStyle.Bold });
            GUILayout.Label(ToolLang.Get("Your visual novel journey starts here. Click on the blocks!", "Ваш путь в создании визуальных новелл начинается здесь. Кликайте по блокам!"), new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = Color.gray } });
            GUILayout.Space(15);

            DrawVisualWorkflow();

            GUILayout.FlexibleSpace();

            if (_tutorialProgress < 5)
            {
                GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
                if (GUILayout.Button("⏭ " + ToolLang.Get("SKIP TUTORIAL (UNLOCK ALL)", "ПРОПУСТИТЬ ОБУЧЕНИЕ (ОТКРЫТЬ ВСЕ)"), new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold }, GUILayout.Height(50), GUILayout.Width(450)))
                {
                    _tutorialProgress = 5;
                    EditorPrefs.SetInt("Novella_TutorialProgress", 5);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            bool dontShowAgain = EditorPrefs.GetBool("Novella_HasShownWelcome", false);
            dontShowAgain = GUILayout.Toggle(dontShowAgain, ToolLang.Get(" Do not show this window on startup", " Больше не показывать это окно при запуске Unity"), new GUIStyle(EditorStyles.toggle) { fontSize = 14 });
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool("Novella_HasShownWelcome", dontShowAgain);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(25);
            GUILayout.EndScrollView();
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

            float totalH = 20f + titleH + 15f + descH + 35f;
            return Mathf.Max(150f, totalH);
        }

        private void DrawVisualWorkflow()
        {
            string t1Title = ToolLang.Get("1. Scenes & Menu", "1. Сцены и Меню");
            string t1Desc = ToolLang.Get("Manage Unity Scenes for Gameplay and Main Menu.", "Управление Unity сценами для геймплея и меню.");

            string t2Title = ToolLang.Get("2. Actors & Variables", "2. Персонажи и Переменные");
            string t2Desc = ToolLang.Get("Define Actors, multi-layered Paper Dolls, and global variables.", "Актеры, многослойные Paper Dolls (одежда/эмоции) и глобальные переменные.");

            string t3Title = ToolLang.Get("3. Graph Editor", "3. Редактор Графа");
            string t3Desc = ToolLang.Get(
                "Write your story! Connect Dialogue, Choices, Audio, Logic, and many other interesting nodes that allow you to create the most detailed and engaging novel!",
                "Пишите историю! Соединяйте ноды Диалогов, Выборов, Аудио, Логики и множество других интересных нод, которые позволят вам создать максимально детализированную и захватывающую новеллу!"
            );

            string t4Title = ToolLang.Get("4. UI Forge", "4. UI Кузница");
            string t4Desc = ToolLang.Get("Style Dialogue frames, customize Main Menu, and configure Character Wardrobe flow.", "Стилизация диалогов, Главное Меню и Гардероб.");

            string t5Title = ToolLang.Get("5. Interactive Tutorial", "5. Интерактивный Урок");
            string t5Desc = ToolLang.Get("Launch the learning graph to see how all systems work together in a real scene!", "Запустите обучающий граф, чтобы своими глазами увидеть, как все системы работают вместе на реальной сцене!");

            float tileW = 340f;
            float gapX = 60f;

            float h1 = CalculateTileHeight(t1Title, t1Desc, tileW);
            float h2 = CalculateTileHeight(t2Title, t2Desc, tileW);
            float h3 = CalculateTileHeight(t3Title, t3Desc, tileW);
            float h4 = CalculateTileHeight(t4Title, t4Desc, tileW);
            float h5 = CalculateTileHeight(t5Title, t5Desc, tileW);

            float t1Y = 20f;
            float t2Y = t1Y + 80f;
            float t4Y = t2Y + h2 + 60f;
            float t3Y = t4Y + 80f;
            float t5Y = t3Y + h3 + 60f;

            float totalHeight = t5Y + h5 + 20f;
            Rect workArea = GUILayoutUtility.GetRect(850, totalHeight);

            float col1X = workArea.x + 50f;
            float col2X = col1X + tileW + gapX;

            Rect t1 = new Rect(col1X, workArea.y + t1Y, tileW, h1);
            Rect t2 = new Rect(col2X, workArea.y + t2Y, tileW, h2);
            Rect t3 = new Rect(col1X, workArea.y + t3Y, tileW, h3);
            Rect t4 = new Rect(col2X, workArea.y + t4Y, tileW, h4);
            Rect t5 = new Rect(workArea.x + (workArea.width - tileW) / 2f, workArea.y + t5Y, tileW, h5);

            DrawArrow(new Vector2(t1.xMax, t1.y + h1 / 2), new Vector2(t2.xMin, t2.y + h2 / 2), Vector2.right, Vector2.left, 120f, _tutorialProgress >= 2);
            DrawArrow(new Vector2(t2.center.x, t2.yMax), new Vector2(t3.center.x, t3.yMin), Vector2.down, Vector2.up, 120f, _tutorialProgress >= 3);
            DrawArrow(new Vector2(t3.xMax, t3.y + h3 / 2), new Vector2(t4.xMin, t4.y + h4 / 2), Vector2.right, Vector2.left, 120f, _tutorialProgress >= 4);
            DrawArrow(new Vector2(t4.center.x, t4.yMax), new Vector2(t5.center.x, t5.yMin), Vector2.down, Vector2.up, 120f, _tutorialProgress >= 5);

            DrawTileInteractive(t1, 1, "🛠", t1Title, t1Desc, () => {
                NovellaSceneManagerWindow.ShowWindow();
                NovellaTutorialManager.StartTutorial("SceneManager");
            });

            DrawTileInteractive(t2, 2, "🦸", t2Title, t2Desc, () => {
                NovellaCharacterEditor.OpenWindow();
                NovellaTutorialManager.StartTutorial("CharacterEditor");
            });

            DrawTileInteractive(t3, 3, "🗺️", t3Title, t3Desc, () => {
                var graphAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>("Assets/NovellaEngine/Tutorials/01_TutorialGraph.asset");
                if (graphAsset != null)
                {
                    NovellaGraphWindow.OpenGraphWindow(graphAsset);
                    GetWindow<NovellaGraphWindow>("Novella Graph").Focus();
                    EditorApplication.delayCall += () => NovellaTutorialManager.StartTutorial("GraphEditor");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Asset '01_TutorialGraph.asset' not found in Tutorials folder!", "OK");
                }
            });

            DrawTileInteractive(t4, 4, "🎨", t4Title, t4Desc, () => {
                NovellaUIEditorWindow.ShowWindow();
                NovellaTutorialManager.StartTutorial("UIEditor");
            });

            DrawTileInteractive(t5, 5, "▶️", t5Title, t5Desc, () => {
                var lessonAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>("Assets/NovellaEngine/Tutorials/02_InteractiveLesson.asset");
                if (lessonAsset != null)
                {
                    NovellaGraphWindow.OpenGraphWindow(lessonAsset);
                    GetWindow<NovellaGraphWindow>("Novella Graph").Focus();
                    EditorApplication.delayCall += () => NovellaTutorialManager.StartTutorial("InteractiveLesson");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Asset '02_InteractiveLesson.asset' not found in Tutorials folder!", "OK");
                }
            }, true);
        }

        private void DrawTileInteractive(Rect rect, int stepIndex, string icon, string title, string desc, Action onClick, bool isFinalStep = false)
        {
            bool isUnlocked = _tutorialProgress >= stepIndex;
            Event e = Event.current;
            bool isHovered = rect.Contains(e.mousePosition);

            Rect drawRect = new Rect(rect);
            if (isHovered && isUnlocked && !NovellaTutorialManager.IsTutorialActive)
            {
                float shakeX = Mathf.Sin((float)EditorApplication.timeSinceStartup * 45f) * 1.5f;
                float shakeY = Mathf.Cos((float)EditorApplication.timeSinceStartup * 50f) * 1.5f;
                drawRect.x += shakeX; drawRect.y += shakeY;

                Color glowColor = isFinalStep ? new Color(0.2f, 0.8f, 0.4f, 0.35f) : new Color(0.2f, 0.6f, 1f, 0.25f);
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

            if (!isUnlocked)
            {
                EditorGUI.DrawRect(drawRect, new Color(0.1f, 0.1f, 0.1f, 0.6f));
                Rect lockRect = new Rect(drawRect.center.x - 20, drawRect.center.y - 20, 40, 40);
                GUI.Label(lockRect, "🔒", new GUIStyle(EditorStyles.label) { fontSize = 36 });
            }

            if (GUI.Button(drawRect, GUIContent.none, GUIStyle.none))
            {
                if (NovellaTutorialManager.IsTutorialActive)
                {
                    EditorUtility.DisplayDialog(ToolLang.Get("Finish Current Tour!", "Завершите текущий урок!"), ToolLang.Get("Please finish or skip the active tutorial window first.", "Пожалуйста, завершите или пропустите текущую активную экскурсию перед открытием следующей."), "OK");
                    return;
                }

                if (isUnlocked)
                {
                    onClick?.Invoke();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        ToolLang.Get("Step Locked!", "Шаг заблокирован!"),
                        ToolLang.Get($"Please complete step {_tutorialProgress} first, or click 'Skip Tutorial' to unlock everything.", $"Пожалуйста, сначала изучите шаг {_tutorialProgress}, или нажмите кнопку 'Пропустить обучение', чтобы открыть все окна."),
                        "OK"
                    );
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