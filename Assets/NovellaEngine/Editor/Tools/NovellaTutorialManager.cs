using NovellaEngine.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public static class NovellaTutorialManager
    {
        private class TutorialStep
        {
            public string Text;
            public Func<EditorWindow, Rect> HighlightRect;
        }

        private static string _activeTutorialKey = null;
        private static int _currentStep = 0;
        private static double _tutorialStartTime = 0;
        private static double _stepStartTime = 0;
        private static bool _initialized = false;

        private static EventType _savedEventType = EventType.Ignore;
        private static NovellaCharacter _tempTutorialChar;
        private static Vector2 _textScrollPos;

        public static bool IsTutorialActive => !string.IsNullOrEmpty(_activeTutorialKey);

        private static Dictionary<string, List<TutorialStep>> _tutorials = new Dictionary<string, List<TutorialStep>>();

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            EditorApplication.update += () => {
                if (!string.IsNullOrEmpty(_activeTutorialKey))
                {
                    var win = EditorWindow.focusedWindow;
                    if (win != null) win.Repaint();
                }
            };

            _tutorials["SceneManager"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the Scene Manager! Here you will connect your Unity scenes with the Novella Engine logic.", "Добро пожаловать в Менеджер Сцен!\nЗдесь вы связываете ваши сцены Unity с логикой Novella Engine."),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 60)
                },
                new TutorialStep {
                    Text = ToolLang.Get("This is your Project Scenes List. It automatically loads scenes from your Build Settings. Use the '+ Create New Scene' button below it to make new ones.", "Это список сцен проекта.\nОн автоматически загружает сцены из Build Settings. Вы можете создавать новые прямо отсюда."),
                    HighlightRect = w => new Rect(10, 70, w.position.width - 20, w.position.height - 120)
                },
                new TutorialStep {
                    Text = ToolLang.Get("When you select an empty scene, buttons will appear here to instantly generate the Game UI or Main Menu UI. Try it after the tour!", "Когда вы выберете пустую сцену, здесь появятся кнопки для мгновенной генерации интерфейса Игры или Главного Меню. Попробуйте после завершения экскурсии!"),
                    HighlightRect = w => new Rect(10, w.position.height - 250, w.position.width - 20, 200)
                }
            };

            _tutorials["CharacterEditor"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the Character Editor! Here you will create your actors.", "Добро пожаловать в Редактор Персонажей!\nЗдесь создаются актеры для вашей истории."),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 50)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Left Panel: Your characters list. Main Characters and Favorites are grouped for easy access. Create your first character here!", "Левая панель: Ваш список персонажей.\nГлавные герои и Избранные вынесены в отдельные группы для удобства."),
                    HighlightRect = w => new Rect(0, 0, 250, w.position.height)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Center Panel: Paper Doll System! Build your characters using layers. Example: Layer 1 is Body, Layer 2 is Clothes, Layer 3 is Face.", "Центральная панель: Система Слоев (Кукла)!\nСобирайте персонажей по частям. Например: Тело -> Одежда -> Лицо -> Волосы."),
                    HighlightRect = w => new Rect(250, 0, 400, w.position.height)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Emotions are Presets! Create a 'Smile' preset and tell it to replace ONLY the Face layer. The body and clothes will remain untouched during the game.", "Эмоции работают как Пресеты!\nСоздайте пресет 'Улыбка' и укажите ему подменять ТОЛЬКО слой лица. Тело и одежда останутся нетронутыми в игре."),
                    HighlightRect = w => new Rect(260, 260, 380, 250)
                }
            };

            _tutorials["GraphEditor"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the Graph Editor! This is where your story branches and dialogue flow are built.", "Добро пожаловать в Редактор Графа!\nЗдесь строится логика и ветвления вашей истории."),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 60)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Each node is unique! Left-click on any node to open its Inspector. There you can edit the dialogue text, characters, add audio, and configure logic.", "Каждая нода уникальна! Нажмите ЛКМ по ноде, чтобы открыть её Инспектор справа. Там настраивается текст реплик, выбираются персонажи, добавляется звук и логика."),
                    HighlightRect = w => new Rect(w.position.width - 550, 40, 550, w.position.height - 40)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Look at the animation: Connect nodes by dragging wires from ports! Start -> Dialogue -> Branch.", "Обратите внимание на анимацию:\nСоздавайте ноды и протягивайте связи от портов! Например: Старт -> Диалог -> Развилка."),
                    HighlightRect = w => new Rect(0, 60, w.position.width, w.position.height - 60)
                }
            };

            _tutorials["DLCManager"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the DLC Modules! DLCs (Downloadable Content) are add-ons that seamlessly expand Novella Engine with new features, mini-games, and mechanics.", "Добро пожаловать в Модули DLC!\nDLC — это дополнения, которые легко расширяют базовый функционал Novella Engine, добавляя новые мини-игры, системы и ноды."),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 60)
                },
                new TutorialStep {
                    Text = ToolLang.Get("In the Graph Editor's left panel, you will find the 'DLC Modules' button. Clicking it opens the manager to enable or disable installed add-ons.", "В левой панели Редактора Графа находится кнопка 'Модули DLC'.\nНажав на неё, вы откроете менеджер управления установленными дополнениями."),
                    HighlightRect = w => new Rect(10, 170, 240, 45)
                },
                new TutorialStep {
                    Text = ToolLang.Get("This is the DLC Manager! Here you can easily toggle downloaded modules on and off. The engine automatically reconfigures itself for the active DLCs.", "А вот и Менеджер DLC!\nЗдесь вы можете легко включать и выключать загруженные модули. Движок автоматически подстроит свой функционал под активные DLC."),
                    HighlightRect = w => new Rect(w.position.width / 2f - 325, w.position.height / 2f - 210, 650, 420)
                }
            };

            _tutorials["UIEditor"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the UI Forge! Here you design every visual aspect of your game.\n\nNOTE: If you haven't set up presets in Step 1 (Scenes & Menu), this window might be empty!",
                                        "Добро пожаловать в Кузницу UI!\nЗдесь настраивается весь визуальный стиль.\n\nВАЖНО: Пока вы не настроите пресеты в Шаге 1 (Сцены и Меню), здесь может быть пусто!"),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 100)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Left Panel: Settings. Here you bind your Story Graph, customize Dialogue frames, Menus, and the Character Creation screen (DLC).", "Левая панель: Настройки.\nЗдесь вы привязываете Граф истории, настраиваете рамки диалогов, Меню и Гардероб героя (DLC)."),
                    HighlightRect = w => new Rect(0, 0, 650, w.position.height)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Right Panel: Live Preview! Instantly check how your UI looks on PC and Mobile devices without starting the game.", "Правая панель: Живой предпросмотр!\nМгновенно проверяйте, как выглядит ваш UI на ПК и Телефонах без запуска самой игры."),
                    HighlightRect = w => new Rect(650, 0, w.position.width - 650, w.position.height)
                },
                new TutorialStep {
                Text = ToolLang.Get(
                    "Important: The UI Forge is a great starting point if you're not familiar with Unity's interface. However, its current implementation is basic. To create truly complex and unique interfaces, we highly recommend learning the fundamentals of the Unity Canvas system!",
                    "Важно: Кузница UI (UI Editor) — это отличный инструмент для старта, если вы не разбираетесь в интерфейсе Unity. Однако текущая реализация является базовой. Для создания по-настоящему сложных и уникальных интерфейсов мы настоятельно рекомендуем изучить систему Unity Canvas хотя бы на базовом уровне!"
                ),
                    HighlightRect = w => new Rect(0, 0, w.position.width, w.position.height)
                }
            };

            _tutorials["InteractiveLesson"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the Interactive Lesson! Take a look at this graph and carefully study the nodes.",
                                        "Добро пожаловать в Интерактивный Урок!\nОзнакомьтесь с этим графом и внимательно изучите, как соединены ноды."),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 80)
                },
                new TutorialStep {
                    Text = ToolLang.Get("You can freely delete, create, and modify nodes as you like! Don't worry, the graph will restore to its original state when you restart the tutorial.",
                                        "Вы можете смело удалять, создавать и менять ноды как хотите!\nНе бойтесь ничего сломать — этот обучающий граф всё равно восстановится к изначальному состоянию при перезапуске."),
                    HighlightRect = w => new Rect(0, 80, w.position.width, w.position.height - 80)
                }
            };
        }

        public static void StartTutorial(string tutorialKey)
        {
            Init();
            _activeTutorialKey = tutorialKey;
            _currentStep = 0;
            _tutorialStartTime = EditorApplication.timeSinceStartup;
            _stepStartTime = EditorApplication.timeSinceStartup;
            _textScrollPos = Vector2.zero;
        }

        public static void ForceStopTutorial()
        {
            _activeTutorialKey = null;
            CleanupTempData();
        }

        public static void CompleteTutorial(string key, EditorWindow windowToClose = null)
        {
            EditorPrefs.SetBool("Novella_Tut_" + key, true);

            int currentProgress = EditorPrefs.GetInt("Novella_TutorialProgress", 1);
            if (key == "SceneManager" && currentProgress < 2) EditorPrefs.SetInt("Novella_TutorialProgress", 2);
            if (key == "CharacterEditor" && currentProgress < 3) EditorPrefs.SetInt("Novella_TutorialProgress", 3);
            if (key == "GraphEditor" && currentProgress < 4) EditorPrefs.SetInt("Novella_TutorialProgress", 4);
            if (key == "DLCManager" && currentProgress < 5) EditorPrefs.SetInt("Novella_TutorialProgress", 5);
            if (key == "UIEditor" && currentProgress < 6) EditorPrefs.SetInt("Novella_TutorialProgress", 6);

            _activeTutorialKey = null;
            CleanupTempData();

            EditorApplication.delayCall += () => {
                if (windowToClose != null) windowToClose.Close();
                if (key != "InteractiveLesson")
                {
                    NovellaWelcomeWindow.ShowWindow();
                }
            };
        }

        private static void CleanupTempData()
        {
            if (_tempTutorialChar != null)
            {
                UnityEngine.Object.DestroyImmediate(_tempTutorialChar);
                _tempTutorialChar = null;
                if (EditorWindow.HasOpenInstances<NovellaCharacterEditor>())
                {
                    NovellaCharacterEditor.OpenWithCharacter(null);
                }
            }
        }

        private static Rect GetTutorialPanelRect(EditorWindow window, Rect highlightRect, string tutorialKey, int step)
        {
            float w = window.position.width;
            float h = window.position.height;
            float boxW = 550;
            float boxH = 170;
            float boxX = (w - boxW) / 2f;
            float boxY = h - boxH - 30;

            if (tutorialKey == "SceneManager" && step == 1)
            {
                boxY = 70;
            }
            else if (tutorialKey == "DLCManager" && step == 1)
            {
                boxX = 280;
                boxY = h / 2f - boxH / 2f;
            }
            else if (new Rect(boxX, boxY, boxW, boxH).Overlaps(highlightRect))
            {
                if (highlightRect.yMax < h - boxH - 60) boxY = highlightRect.yMax + 30;
                else boxY = 30;
            }

            return new Rect(boxX, boxY, boxW, boxH);
        }

        private static void DrawPointingFinger(Vector2 targetPos, float alpha)
        {
            if (alpha <= 0) return;
            float t = (float)(EditorApplication.timeSinceStartup % 2.0);
            Vector2 fingerPos = new Vector2(targetPos.x - 20, targetPos.y + 10 + Mathf.Sin(t * Mathf.PI * 2) * 10);

            Color oldG = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.Label(new Rect(fingerPos.x, fingerPos.y, 50, 50), "👆", new GUIStyle() { fontSize = 45 });
            GUI.color = oldG;
        }

        // === ИДЕАЛЬНОЕ СГЛАЖИВАНИЕ (ANTI-ALIASING) ===
        private static void DrawAACapsule(Rect rect, Color color)
        {
            Handles.color = color;
            float radius = Mathf.Min(rect.width / 2f, rect.height / 2f);
            int segments = 18;
            Vector3[] points = new Vector3[segments * 2];

            Vector2 leftCenter = new Vector2(rect.x + radius, rect.y + radius);
            Vector2 rightCenter = new Vector2(rect.xMax - radius, rect.y + radius);

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 0.5f + (i * Mathf.PI / (segments - 1));
                points[i] = new Vector3(leftCenter.x + Mathf.Cos(angle) * radius, leftCenter.y + Mathf.Sin(angle) * radius, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 1.5f + (i * Mathf.PI / (segments - 1));
                points[i + segments] = new Vector3(rightCenter.x + Mathf.Cos(angle) * radius, rightCenter.y + Mathf.Sin(angle) * radius, 0f);
            }

            Handles.DrawAAConvexPolygon(points);
            Handles.color = Color.white;
        }

        private static bool DrawToggleSwitch(Rect rect, bool value, float alpha = 1f)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color bgColor = value ? new Color(0.2f, 0.7f, 0.3f, alpha) : new Color(0.35f, 0.35f, 0.35f, alpha);
                DrawAACapsule(rect, bgColor);

                Color knobColor = new Color(1, 1, 1, alpha);
                float radius = Mathf.Min(rect.width / 2f, rect.height / 2f);
                float knobX = value ? rect.xMax - radius : rect.x + radius;
                Rect knobRect = new Rect(knobX - radius + 2f, rect.y + 2f, radius * 2f - 4f, radius * 2f - 4f);
                DrawAACapsule(knobRect, knobColor);
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        private static void DrawFakeDLCItem(Rect rect, string name, string sysName, string version, bool isEnabled, float alpha)
        {
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.22f, 0.22f, alpha));
            Handles.color = new Color(0, 0, 0, 0.5f * alpha);
            Handles.DrawSolidRectangleWithOutline(rect, Color.clear, Handles.color);
            Handles.color = Color.white;

            GUI.color = isEnabled ? new Color(0.2f, 0.8f, 0.3f, alpha) : new Color(0.5f, 0.5f, 0.5f, alpha);
            GUI.Label(new Rect(rect.x + 5, rect.y + 10, 20, 20), isEnabled ? "✔" : "✖", EditorStyles.boldLabel);
            GUI.color = Color.white;

            GUI.Label(new Rect(rect.x + 25, rect.y + 10, 300, 20), "▶ " + name, new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.9f, 0.9f, 0.9f, alpha) } });
            GUI.Label(new Rect(rect.x + 25, rect.y + 30, 400, 20), "Системное имя: " + sysName, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.5f, 0.5f, 0.5f, alpha) } });

            GUI.Label(new Rect(rect.xMax - 140, rect.y + 10, 50, 20), version, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f, alpha) } });

            DrawToggleSwitch(new Rect(rect.xMax - 80, rect.y + 10, 40, 20), isEnabled, alpha);

            Color oldC = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.Button(new Rect(rect.xMax - 30, rect.y + 7, 25, 25), "🗑", EditorStyles.miniButton);
            GUI.color = oldC;
        }

        private static void DrawPort(Vector2 center, string label, Color color, bool isRight, bool isConnected, float alpha)
        {
            if (alpha <= 0) return;
            Color c = color; c.a *= alpha;

            GUIStyle lblStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.8f, 0.8f, 0.8f, alpha) } };
            lblStyle.alignment = isRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            Rect lblRect = isRight ? new Rect(center.x - 105, center.y - 10, 95, 20) : new Rect(center.x + 10, center.y - 10, 95, 20);
            GUI.Label(lblRect, label, lblStyle);

            // ИСПРАВЛЕНО: Идеально ровные порты без пикселей
            DrawAACapsule(new Rect(center.x - 4.5f, center.y - 4.5f, 9f, 9f), c);
            DrawAACapsule(new Rect(center.x - 3f, center.y - 3f, 6f, 6f), new Color(0.15f, 0.15f, 0.15f, alpha));

            if (isConnected)
            {
                DrawAACapsule(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), c);
            }
        }

        private static void DrawBezier(Vector2 start, Vector2 end, Color color, float alpha)
        {
            if (alpha <= 0) return;
            Color c = color; c.a *= alpha;
            Handles.DrawBezier(start, end, start + Vector2.right * 40, end + Vector2.left * 40, c, null, 3f);
        }

        public static void BlockBackgroundEvents(EditorWindow window)
        {
            if (string.IsNullOrEmpty(_activeTutorialKey) || !_tutorials.ContainsKey(_activeTutorialKey)) return;
            var steps = _tutorials[_activeTutorialKey];
            if (_currentStep >= steps.Count || _currentStep < 0) return;

            _savedEventType = Event.current.type;

            if (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)
            {
                Event.current.type = EventType.Ignore;
            }
        }

        public static void DrawOverlay(EditorWindow window)
        {
            if (string.IsNullOrEmpty(_activeTutorialKey) || !_tutorials.ContainsKey(_activeTutorialKey)) return;

            var steps = _tutorials[_activeTutorialKey];
            if (_currentStep >= steps.Count || _currentStep < 0) return;

            float stepTime = (float)(EditorApplication.timeSinceStartup - _stepStartTime);
            float introAnim = Mathf.Clamp01(stepTime / 0.3f);
            introAnim = 1f - Mathf.Pow(1f - introAnim, 3f);

            if (Event.current.type == EventType.Ignore && _savedEventType != EventType.Ignore)
            {
                Event.current.type = _savedEventType;
            }

            if (_activeTutorialKey == "GraphEditor" && window.GetType().Name == "NovellaGraphWindow")
            {
                var isInspField = window.GetType().GetField("_isInspectorOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var rightPanelField = window.GetType().GetField("_rightPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var gvField = window.GetType().GetField("_graphView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                bool shouldBeOpen = (_currentStep == 1);

                if (isInspField != null && rightPanelField != null)
                {
                    bool isOpen = (bool)isInspField.GetValue(window);
                    if (isOpen != shouldBeOpen)
                    {
                        isInspField.SetValue(window, shouldBeOpen);
                        var rightPanel = rightPanelField.GetValue(window) as UnityEngine.UIElements.VisualElement;
                        if (rightPanel != null) rightPanel.style.width = shouldBeOpen ? 550 : 0;
                    }
                }

                if (shouldBeOpen && gvField != null)
                {
                    var gv = gvField.GetValue(window) as UnityEditor.Experimental.GraphView.GraphView;
                    if (gv != null && gv.selection.Count == 0)
                    {
                        var dialNode = gv.nodes.ToList().FirstOrDefault(n => n is NovellaNodeView nv && nv.Data is DialogueNodeData);
                        if (dialNode != null) gv.AddToSelection(dialNode);
                    }
                }
            }

            if (_activeTutorialKey == "CharacterEditor")
            {
                if (_currentStep >= 2 && _tempTutorialChar == null)
                {
                    _tempTutorialChar = ScriptableObject.CreateInstance<NovellaCharacter>();
                    _tempTutorialChar.name = "Tutorial_MC";
                    _tempTutorialChar.IsPlayerCharacter = true;
                    NovellaCharacterEditor.OpenWithCharacter(_tempTutorialChar);
                }
                else if (_currentStep < 2 && _tempTutorialChar != null)
                {
                    CleanupTempData();
                }
            }

            var step = steps[_currentStep];
            Rect hRect = step.HighlightRect(window);
            float w = window.position.width;
            float h = window.position.height;

            Color dimColor = new Color(0, 0, 0, 0.8f * introAnim);
            EditorGUI.DrawRect(new Rect(0, 0, w, hRect.y), dimColor);
            EditorGUI.DrawRect(new Rect(0, hRect.yMax, w, h - hRect.yMax), dimColor);
            EditorGUI.DrawRect(new Rect(0, hRect.y, hRect.x, hRect.height), dimColor);
            EditorGUI.DrawRect(new Rect(hRect.xMax, hRect.y, w - hRect.xMax, hRect.height), dimColor);

            if (_activeTutorialKey == "SceneManager" && _currentStep == 1)
            {
                DrawPointingFinger(new Vector2(hRect.center.x + 20, hRect.yMax - 25), introAnim);
            }

            if (_activeTutorialKey == "SceneManager" && _currentStep == 2)
            {
                Rect fakeUIRect = new Rect(hRect.x + 20, hRect.y + 60, hRect.width - 40, 100);

                Color oldGuiCol = GUI.color;
                GUI.color = new Color(1, 1, 1, introAnim);

                GUILayout.BeginArea(fakeUIRect);
                GUIStyle helpStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("💡 " + ToolLang.Get("Empty Scene Selected. Quick Setup:", "Выбрана пустая сцена. Быстрая настройка:"), helpStyle);
                GUILayout.Space(10);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.3f);
                GUILayout.Button("🎮 " + ToolLang.Get("Generate Game UI", "Сгенерировать UI Игры"), GUILayout.Height(40), GUILayout.Width(300));
                GUILayout.Space(20);
                GUI.backgroundColor = new Color(0.2f, 0.4f, 0.8f);
                GUILayout.Button("🏠 " + ToolLang.Get("Generate Main Menu UI", "Сгенерировать UI Главного Меню"), GUILayout.Height(40), GUILayout.Width(300));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUI.backgroundColor = Color.white;
                GUILayout.EndArea();

                GUI.color = oldGuiCol;
            }

            if (_activeTutorialKey == "CharacterEditor" && _currentStep == 1)
            {
                DrawPointingFinger(new Vector2(100, 100), introAnim);
            }

            if (_activeTutorialKey == "UIEditor" && _currentStep == 1)
            {
                DrawPointingFinger(new Vector2(300, 200), introAnim);
            }

            if (_activeTutorialKey == "DLCManager")
            {
                if (_currentStep == 1)
                {
                    float leftPanelWidth = 260f;
                    Rect leftPanel = new Rect(0, 0, leftPanelWidth, h);

                    EditorGUI.DrawRect(leftPanel, new Color(0.15f, 0.15f, 0.15f, introAnim));
                    EditorGUI.DrawRect(new Rect(leftPanelWidth - 1, 0, 1, h), new Color(0, 0, 0, introAnim));

                    GUI.Label(new Rect(0, 15, leftPanelWidth, 30), "🛠 " + ToolLang.Get("Workspace Tools", "Инструменты"), new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.6f, 0.8f, 1f, introAnim) } });
                    EditorGUI.DrawRect(new Rect(15, 55, leftPanelWidth - 30, 1), new Color(0.25f, 0.25f, 0.25f, introAnim));

                    Color btnColor = new Color(0.2f, 0.2f, 0.2f, introAnim);

                    EditorGUI.DrawRect(new Rect(10, 70, leftPanelWidth - 20, 45), btnColor);
                    GUI.Label(new Rect(20, 70, 35, 45), "🎨", new GUIStyle(EditorStyles.label) { fontSize = 20 });
                    GUI.Label(new Rect(55, 70, 180, 45), ToolLang.Get("Node Colors", "Цвета Нод"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.9f, 0.9f, introAnim) } });

                    EditorGUI.DrawRect(new Rect(10, 120, leftPanelWidth - 20, 45), btnColor);
                    GUI.Label(new Rect(20, 120, 35, 45), "📋", new GUIStyle(EditorStyles.label) { fontSize = 20 });
                    GUI.Label(new Rect(55, 120, 180, 45), ToolLang.Get("Global Variables", "База Переменных"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.9f, 0.9f, introAnim) } });

                    Rect dlcBtnRect = new Rect(10, 170, leftPanelWidth - 20, 45);
                    EditorGUI.DrawRect(dlcBtnRect, new Color(0.25f, 0.35f, 0.25f, introAnim));
                    GUI.Label(new Rect(20, 170, 35, 45), "🧩", new GUIStyle(EditorStyles.label) { fontSize = 20 });
                    GUI.Label(new Rect(55, 170, 180, 45), ToolLang.Get("DLC Modules", "Модули DLC"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = new Color(0.4f, 1f, 0.4f, introAnim) } });

                    DrawPointingFinger(dlcBtnRect.center, introAnim);
                }
                else if (_currentStep == 2)
                {
                    float winW = 650;
                    float winH = 380;
                    Rect dlcWinRect = new Rect(w / 2f - winW / 2f, h / 2f - winH / 2f - 20, winW, winH);

                    EditorGUI.DrawRect(dlcWinRect, new Color(0.16f, 0.16f, 0.16f, introAnim));

                    Color oldG = GUI.color;
                    GUI.color = new Color(1, 1, 1, introAnim);

                    GUI.Label(new Rect(dlcWinRect.x, dlcWinRect.y - 25, dlcWinRect.width, 25), ToolLang.Get("DLC Manager", "Менеджер DLC"), EditorStyles.boldLabel);

                    GUI.Label(new Rect(dlcWinRect.x, dlcWinRect.y + 10, dlcWinRect.width, 25), "🔌 " + ToolLang.Get("DLC Modules Manager", "Менеджер модулей DLC"), new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 16, normal = { textColor = new Color(1, 1, 1, introAnim) } });

                    Rect leftPane = new Rect(dlcWinRect.x + 10, dlcWinRect.y + 45, 150, dlcWinRect.height - 55);

                    EditorGUI.DrawRect(new Rect(leftPane.x, leftPane.y, leftPane.width, 30), new Color(0.15f, 0.3f, 0.45f, introAnim));
                    GUI.Label(new Rect(leftPane.x + 5, leftPane.y + 5, leftPane.width, 20), "🧩 " + ToolLang.Get("Active Modules", "Активные модули"), new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, 1, 1, introAnim) } });

                    EditorGUI.DrawRect(new Rect(leftPane.x, leftPane.y + 35, leftPane.width, 30), new Color(0.1f, 0.1f, 0.1f, introAnim));
                    GUI.Label(new Rect(leftPane.x + 5, leftPane.y + 40, leftPane.width, 20), "🗑 " + ToolLang.Get("Trash Bin", "Корзина"), new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.7f, 0.7f, 0.7f, introAnim) } });

                    Rect rightPane = new Rect(dlcWinRect.x + 170, dlcWinRect.y + 45, dlcWinRect.width - 180, dlcWinRect.height - 55);
                    EditorGUI.DrawRect(rightPane, new Color(0.2f, 0.2f, 0.2f, introAnim));

                    Rect helpBox = new Rect(rightPane.x + 5, rightPane.y + 5, rightPane.width - 10, 50);
                    EditorGUI.DrawRect(helpBox, new Color(0.25f, 0.25f, 0.25f, introAnim));
                    GUI.Label(helpBox, ToolLang.Get("<b>Graceful Degradation (Pass-through)</b>\nIf disabled, the player will simply skip these nodes in the game!\nClick on a module name to see its description.", "<b>Умный пропуск (Pass-through)</b>\nПри выключении, в игре плеер проскочит эти ноды насквозь!\nНажмите на имя модуля, чтобы прочитать описание."), new GUIStyle(EditorStyles.miniLabel) { richText = true, normal = { textColor = new Color(0.8f, 0.8f, 0.8f, introAnim) } });

                    float btnWidth = (rightPane.width - 10) / 2f - 2;
                    EditorGUI.DrawRect(new Rect(rightPane.x + 5, rightPane.y + 60, btnWidth, 20), new Color(0.3f, 0.3f, 0.3f, introAnim));
                    GUI.Label(new Rect(rightPane.x + 5, rightPane.y + 60, btnWidth, 20), ToolLang.Get("Enable All", "Включить всё"), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, introAnim) } });

                    EditorGUI.DrawRect(new Rect(rightPane.x + 5 + btnWidth + 4, rightPane.y + 60, btnWidth, 20), new Color(0.3f, 0.3f, 0.3f, introAnim));
                    GUI.Label(new Rect(rightPane.x + 5 + btnWidth + 4, rightPane.y + 60, btnWidth, 20), ToolLang.Get("Disable All", "Выключить всё"), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1, 1, 1, introAnim) } });

                    DrawFakeDLCItem(new Rect(rightPane.x + 5, rightPane.y + 90, rightPane.width - 10, 55), "Check Inventory Item", "NovellaEngine.DLC.Inventory.ItemCheckNodeData", "v. 1.0", true, introAnim);
                    DrawFakeDLCItem(new Rect(rightPane.x + 5, rightPane.y + 150, rightPane.width - 10, 55), "Character Wardrobe DLC", "NovellaEngine.DLC.Wardrobe.WardrobeNodeData", "v. 1.1.2", true, introAnim);

                    Rect questDlcRect = new Rect(rightPane.x + 5, rightPane.y + 210, rightPane.width - 10, 55);
                    DrawFakeDLCItem(questDlcRect, "Quest & Map Extension", "NovellaEngine.DLC.Quests.QuestNodeData", "v. 1.0.5", false, introAnim);

                    GUI.color = oldG;

                    DrawPointingFinger(new Vector2(questDlcRect.xMax - 60, questDlcRect.center.y - 10), introAnim);
                }
            }

            if (_activeTutorialKey == "GraphEditor" && _currentStep == 2)
            {
                float t = (float)(EditorApplication.timeSinceStartup % 6.0);

                Rect realStartNodeRect = new Rect(w / 2 - 300, h / 2 - 50, 160, 80);
                Vector2 pStartOut = new Vector2(realStartNodeRect.xMax - 10, realStartNodeRect.y + 36);

                if (window.GetType().Name == "NovellaGraphWindow")
                {
                    var field = window.GetType().GetField("_graphView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var gv = field.GetValue(window) as UnityEngine.UIElements.VisualElement;
                        if (gv != null)
                        {
                            var startNode = gv.Query<UnityEditor.Experimental.GraphView.Node>().Build().FirstOrDefault(n => n.GetType().Name == "NovellaStartNodeView");
                            if (startNode != null)
                            {
                                realStartNodeRect = startNode.worldBound;
                                pStartOut = new Vector2(realStartNodeRect.xMax - 10, realStartNodeRect.y + 36);
                            }
                        }
                    }
                }

                Rect rDial = new Rect(realStartNodeRect.xMax + 60, realStartNodeRect.y - 15, 200, 130);
                Rect rBranch = new Rect(rDial.xMax + 60, realStartNodeRect.y, 180, 85);

                Vector2 pDialIn = new Vector2(rDial.x, rDial.y + 45);
                Vector2 pDialOut = new Vector2(rDial.xMax, rDial.y + 115);
                Vector2 pBranchIn = new Vector2(rBranch.x, rBranch.y + 45);

                Color cyanCol = new Color(0.4f, 0.8f, 0.8f);
                Color audioCol = new Color(0.16f, 0.5f, 0.44f);
                Color animCol = new Color(0.58f, 0.24f, 0.33f);
                Color sceneCol = new Color(0.2f, 0.6f, 0.8f);

                float alphaDial = Mathf.Clamp01((t - 1f) * 4f) * introAnim;
                if (alphaDial > 0.01f)
                {
                    EditorGUI.DrawRect(rDial, new Color(0.18f, 0.18f, 0.18f, alphaDial));
                    EditorGUI.DrawRect(new Rect(rDial.x, rDial.y + 35, 60, rDial.height - 35), new Color(0.22f, 0.22f, 0.22f, alphaDial));
                    EditorGUI.DrawRect(new Rect(rDial.x, rDial.y, rDial.width, 35), new Color(0.25f, 0.25f, 0.25f, alphaDial));

                    Handles.color = new Color(0, 0, 0, 0.5f * alphaDial);
                    Handles.DrawSolidRectangleWithOutline(rDial, Color.clear, Handles.color);

                    GUI.Label(new Rect(rDial.x, rDial.y, rDial.width, 35), "Dialogue 1", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.8f, alphaDial) } });

                    DrawPort(pDialIn, ToolLang.Get("Вход", "Вход"), cyanCol, false, t > 1.5f, alphaDial);
                    DrawPort(new Vector2(rDial.xMax, rDial.y + 55), ToolLang.Get("🎵 Audio Sync", "🎵 Аудио Синхр."), audioCol, true, false, alphaDial);
                    DrawPort(new Vector2(rDial.xMax, rDial.y + 75), ToolLang.Get("✨ Anim Sync", "✨ Аним Синхр."), animCol, true, false, alphaDial);
                    DrawPort(new Vector2(rDial.xMax, rDial.y + 95), ToolLang.Get("🎬 Scene Sync", "🎬 Сцена Синхр."), sceneCol, true, false, alphaDial);
                    DrawPort(pDialOut, ToolLang.Get("Next ➡", "Далее ➡"), cyanCol, true, t > 4.0f, alphaDial);
                }

                float alphaBranch = Mathf.Clamp01((t - 3.5f) * 4f) * introAnim;
                if (alphaBranch > 0.01f)
                {
                    EditorGUI.DrawRect(rBranch, new Color(0.18f, 0.18f, 0.18f, alphaBranch));
                    EditorGUI.DrawRect(new Rect(rBranch.x, rBranch.y + 35, 60, rBranch.height - 35), new Color(0.22f, 0.22f, 0.22f, alphaBranch));
                    EditorGUI.DrawRect(new Rect(rBranch.x, rBranch.y, rBranch.width, 35), new Color(0.7f, 0.4f, 0.15f, alphaBranch));

                    Handles.color = new Color(0, 0, 0, 0.5f * alphaBranch);
                    Handles.DrawSolidRectangleWithOutline(rBranch, Color.clear, Handles.color);

                    GUI.Label(new Rect(rBranch.x, rBranch.y, rBranch.width, 35), "Branch 1", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.9f, 0.9f, alphaBranch) } });

                    DrawPort(pBranchIn, ToolLang.Get("Вход", "Вход"), cyanCol, false, t > 4.0f, alphaBranch);
                    DrawPort(new Vector2(rBranch.xMax, rBranch.y + 55), ToolLang.Get("Choice 1", "Выбор 1"), cyanCol, true, false, alphaBranch);
                    DrawPort(new Vector2(rBranch.xMax, rBranch.y + 75), ToolLang.Get("Choice 2", "Выбор 2"), cyanCol, true, false, alphaBranch);
                }

                if (t > 0.5f)
                {
                    Vector2 endPos = pDialIn;
                    if (t < 1.5f) endPos = Vector2.Lerp(pStartOut, pDialIn, (t - 0.5f) / 1.0f);
                    DrawBezier(pStartOut, endPos, cyanCol, introAnim);
                }
                if (t > 3.0f)
                {
                    Vector2 endPos2 = pBranchIn;
                    if (t < 4.0f) endPos2 = Vector2.Lerp(pDialOut, pBranchIn, (t - 3.0f) / 1.0f);
                    DrawBezier(pDialOut, endPos2, cyanCol, alphaDial);
                }

                Vector2 fingerPos = pStartOut;
                if (t < 0.5f) fingerPos = Vector2.Lerp(pStartOut + new Vector2(-40, 60), pStartOut, t / 0.5f);
                else if (t < 1.5f) fingerPos = Vector2.Lerp(pStartOut, pDialIn, (t - 0.5f) / 1.0f);
                else if (t < 2.0f) fingerPos = pDialIn;
                else if (t < 2.5f) fingerPos = Vector2.Lerp(pDialIn, pDialOut, (t - 2.0f) / 0.5f);
                else if (t < 3.0f) fingerPos = pDialOut;
                else if (t < 4.0f) fingerPos = Vector2.Lerp(pDialOut, pBranchIn, (t - 3.0f) / 1.0f);
                else if (t < 4.5f) fingerPos = pBranchIn;
                else if (t < 5.0f) fingerPos = Vector2.Lerp(pBranchIn, pBranchIn + new Vector2(40, 60), (t - 4.5f) / 0.5f);
                else fingerPos = new Vector2(-1000, -1000);

                Color oldG = GUI.color;
                GUI.color = new Color(1, 1, 1, introAnim);
                GUI.Label(new Rect(fingerPos.x, fingerPos.y, 50, 50), "👆", new GUIStyle() { fontSize = 45 });
                GUI.color = oldG;
            }

            float blink = (Mathf.Sin((float)EditorApplication.timeSinceStartup * 6f) + 1f) / 2f;
            Handles.color = new Color(1f, 0.8f, 0f, (0.4f + blink * 0.6f) * introAnim);
            Handles.DrawSolidRectangleWithOutline(hRect, new Color(0, 0, 0, 0), Handles.color);
            Handles.color = Color.white;

            Rect textRect = GetTutorialPanelRect(window, hRect, _activeTutorialKey, _currentStep);

            textRect.y += (1f - introAnim) * 15f;

            Color oldGuiColor = GUI.color;
            GUI.color = new Color(1, 1, 1, introAnim);

            EditorGUI.DrawRect(textRect, new Color(0.1f, 0.1f, 0.15f, 0.95f));
            Handles.color = new Color(0.2f, 0.6f, 1f, introAnim);
            Handles.DrawSolidRectangleWithOutline(textRect, new Color(0, 0, 0, 0), Handles.color);
            Handles.color = Color.white;

            GUIStyle textStyle = new GUIStyle(EditorStyles.label) { fontSize = 16, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = Color.white } };

            GUILayout.BeginArea(new Rect(textRect.x + 20, textRect.y + 20, textRect.width - 40, textRect.height - 70));
            _textScrollPos = GUILayout.BeginScrollView(_textScrollPos);
            GUILayout.Label(step.Text, textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            double timeElapsed = EditorApplication.timeSinceStartup - _tutorialStartTime;

            GUILayout.BeginArea(new Rect(textRect.x + 20, textRect.y + textRect.height - 45, textRect.width - 40, 30));
            GUILayout.BeginHorizontal();

            int secondsLeft = Mathf.CeilToInt(2f - (float)timeElapsed);
            EditorGUI.BeginDisabledGroup(secondsLeft > 0);
            GUI.backgroundColor = new Color(0.7f, 0.4f, 0.4f);

            string skipText = secondsLeft > 0 ? $"🔒 {ToolLang.Get("Skip", "Пропустить")} ({secondsLeft})" : ToolLang.Get("Skip Tour", "Пропустить");
            if (GUILayout.Button(skipText, GUILayout.Width(140), GUILayout.Height(30)))
            {
                if (_activeTutorialKey == "InteractiveLesson" || _activeTutorialKey == "GraphEditor" || _activeTutorialKey == "DLCManager") CompleteTutorial(_activeTutorialKey, null);
                else CompleteTutorial(_activeTutorialKey, window);
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            if (_currentStep > 0)
            {
                GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
                if (GUILayout.Button("◄ " + ToolLang.Get("Back", "Назад"), GUILayout.Width(90), GUILayout.Height(30)))
                {
                    _currentStep--;
                    _stepStartTime = EditorApplication.timeSinceStartup;
                    _textScrollPos = Vector2.zero;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10);
            }

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            string nextText = (_currentStep == steps.Count - 1) ? ToolLang.Get("Finish ✔", "Завершить ✔") : ToolLang.Get("Next ➔", "Далее ➔");
            if (GUILayout.Button(nextText, new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold }, GUILayout.Width(120), GUILayout.Height(30)))
            {
                if (_currentStep == steps.Count - 1)
                {
                    if (_activeTutorialKey == "InteractiveLesson" || _activeTutorialKey == "GraphEditor" || _activeTutorialKey == "DLCManager") CompleteTutorial(_activeTutorialKey, null);
                    else CompleteTutorial(_activeTutorialKey, window);
                }
                else
                {
                    _currentStep++;
                    _stepStartTime = EditorApplication.timeSinceStartup;
                    _textScrollPos = Vector2.zero;
                }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.color = oldGuiColor;

            if (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }
        }
    }
}