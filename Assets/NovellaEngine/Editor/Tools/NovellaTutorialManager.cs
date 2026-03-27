using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using NovellaEngine.Data;

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
        private static bool _initialized = false;

        private static EventType _savedEventType = EventType.Ignore;
        private static NovellaCharacter _tempTutorialChar;

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
                    HighlightRect = w => new Rect(0, 0, w.position.width, 100)
                },
                new TutorialStep {
                    Text = ToolLang.Get("This is your Project Scenes List. It automatically loads scenes from your Build Settings. Use the '+ Create New Scene' button below it to make new ones.", "Это список сцен проекта.\nОн автоматически загружает сцены из Build Settings. Вы можете создавать новые прямо отсюда."),
                    HighlightRect = w => new Rect(10, 100, w.position.width - 20, 300)
                },
                new TutorialStep {
                    Text = ToolLang.Get("When you select an empty scene, buttons will appear here to instantly generate the Game UI or Main Menu UI. Try it after the tour!", "Когда вы выберете пустую сцену, здесь появятся кнопки для мгновенной генерации интерфейса Игры или Главного Меню. Попробуйте после завершения экскурсии!"),
                    HighlightRect = w => new Rect(10, 420, w.position.width - 20, w.position.height - 430)
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
                    // ИСПРАВЛЕНО: Теперь рамка жестко привязана к верху (на высоте 260) и захватывает блок эмоций
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
                    Text = ToolLang.Get("Look at the animation: Right-click to create a Dialogue node, then drag the port to connect it to an End node!", "Обратите внимание на анимацию на фоне:\nСоздайте ноду Диалога (Правый клик мыши), а затем протяните связь от неё к ноде 'Конец'!"),
                    HighlightRect = w => new Rect(0, 60, w.position.width, w.position.height - 60)
                }
            };

            _tutorials["UIEditor"] = new List<TutorialStep>
            {
                new TutorialStep {
                    Text = ToolLang.Get("Welcome to the UI Forge! Here you design every visual aspect of your game.\n\nNOTE: If you haven't set up presets in Step 1 (Scenes & Menu), this window might be empty! UI Forge allows you to customize the scene and create objects without any Unity knowledge!",
                                        "Добро пожаловать в Кузницу UI!\nЗдесь настраивается весь визуальный стиль.\n\nВАЖНО: Пока вы не настроите пресеты в Шаге 1 (Сцены и Меню), здесь может быть пусто! Кузница позволяет настраивать сцену и создавать объекты без знаний Unity!"),
                    HighlightRect = w => new Rect(0, 0, w.position.width, 100)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Left Panel: Settings. Here you bind your Story Graph, customize Dialogue frames, Menus, and the Character Creation screen.", "Левая панель: Настройки.\nЗдесь вы привязываете Граф истории, настраиваете рамки диалогов, Меню и Гардероб героя."),
                    HighlightRect = w => new Rect(0, 0, 650, w.position.height)
                },
                new TutorialStep {
                    Text = ToolLang.Get("Right Panel: Live Preview! Instantly check how your UI looks on PC and Mobile devices without starting the game.", "Правая панель: Живой предпросмотр!\nМгновенно проверяйте, как выглядит ваш UI на ПК и Телефонах без запуска самой игры."),
                    HighlightRect = w => new Rect(650, 0, w.position.width - 650, w.position.height)
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
            if (!EditorPrefs.GetBool("Novella_Tut_" + tutorialKey, false))
            {
                _activeTutorialKey = tutorialKey;
                _currentStep = 0;
                _tutorialStartTime = EditorApplication.timeSinceStartup;
            }
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
            if (key == "UIEditor" && currentProgress < 5) EditorPrefs.SetInt("Novella_TutorialProgress", 5);

            _activeTutorialKey = null;
            CleanupTempData();

            EditorApplication.delayCall += () => {
                if (windowToClose != null) windowToClose.Close();
                NovellaWelcomeWindow.ShowWindow();
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

        private static Rect GetTutorialPanelRect(EditorWindow window)
        {
            float w = window.position.width;
            float h = window.position.height;
            float boxW = 550;
            float boxH = 170;
            float boxX = (w - boxW) / 2f;
            float boxY = h - boxH - 30;
            return new Rect(boxX, boxY, boxW, boxH);
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

            if (Event.current.type == EventType.Ignore && _savedEventType != EventType.Ignore)
            {
                Event.current.type = _savedEventType;
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

            Color dimColor = new Color(0, 0, 0, 0.8f);
            EditorGUI.DrawRect(new Rect(0, 0, w, hRect.y), dimColor);
            EditorGUI.DrawRect(new Rect(0, hRect.yMax, w, h - hRect.yMax), dimColor);
            EditorGUI.DrawRect(new Rect(0, hRect.y, hRect.x, hRect.height), dimColor);
            EditorGUI.DrawRect(new Rect(hRect.xMax, hRect.y, w - hRect.xMax, hRect.height), dimColor);

            // === АНИМАЦИЯ ПАЛЬЦА ДЛЯ ГРАФА (ШАГ 2) ===
            if (_activeTutorialKey == "GraphEditor" && _currentStep == 1)
            {
                float t = (float)(EditorApplication.timeSinceStartup % 4.0);
                Vector2 center = new Vector2(w / 2, h / 2 - 50);
                Vector2 startNode = center + new Vector2(-150, -50);
                Vector2 endNode = center + new Vector2(150, 50);

                GUIStyle nodeStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

                if (t > 0.5f)
                {
                    GUI.backgroundColor = new Color(0.3f, 0.6f, 0.4f);
                    GUI.Box(new Rect(startNode.x - 75, startNode.y - 25, 150, 50), ToolLang.Get("Dialogue", "Диалог"), nodeStyle);
                    GUI.backgroundColor = Color.white;
                }

                if (t > 1.5f)
                {
                    GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                    GUI.Box(new Rect(endNode.x - 60, endNode.y - 25, 120, 50), ToolLang.Get("End", "Конец"), nodeStyle);
                    GUI.backgroundColor = Color.white;
                }

                if (t > 2.5f)
                {
                    Handles.DrawBezier(startNode + new Vector2(75, 0), endNode - new Vector2(60, 0), startNode + new Vector2(150, 0), endNode - new Vector2(120, 0), Color.yellow, null, 5f);
                }

                Vector2 fingerPos = startNode;
                if (t < 0.5f) fingerPos = Vector2.Lerp(startNode + new Vector2(0, 150), startNode, t / 0.5f);
                else if (t < 1.0f) fingerPos = startNode;
                else if (t < 1.5f) fingerPos = Vector2.Lerp(startNode, endNode, (t - 1.0f) / 0.5f);
                else if (t < 2.0f) fingerPos = endNode;
                else if (t < 2.5f) fingerPos = Vector2.Lerp(endNode, startNode, (t - 2.0f) / 0.5f);
                else if (t < 3.5f) fingerPos = Vector2.Lerp(startNode, endNode, (t - 2.5f) / 1.0f);
                else fingerPos = endNode;

                GUI.Label(new Rect(fingerPos.x, fingerPos.y, 50, 50), "👆", new GUIStyle() { fontSize = 45 });
            }

            float blink = (Mathf.Sin((float)EditorApplication.timeSinceStartup * 6f) + 1f) / 2f;
            Handles.color = new Color(1f, 0.8f, 0f, 0.4f + blink * 0.6f);
            Handles.DrawSolidRectangleWithOutline(hRect, new Color(0, 0, 0, 0), Handles.color);
            Handles.color = Color.white;

            Rect textRect = GetTutorialPanelRect(window);

            EditorGUI.DrawRect(textRect, new Color(0.1f, 0.1f, 0.15f, 0.95f));
            Handles.color = new Color(0.2f, 0.6f, 1f, 1f);
            Handles.DrawSolidRectangleWithOutline(textRect, new Color(0, 0, 0, 0), Handles.color);
            Handles.color = Color.white;

            GUIStyle textStyle = new GUIStyle(EditorStyles.label) { fontSize = 16, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = Color.white } };
            GUI.Label(new Rect(textRect.x + 20, textRect.y + 20, textRect.width - 40, textRect.height - 70), step.Text, textStyle);

            double timeElapsed = EditorApplication.timeSinceStartup - _tutorialStartTime;

            GUILayout.BeginArea(new Rect(textRect.x + 20, textRect.y + textRect.height - 45, textRect.width - 40, 30));
            GUILayout.BeginHorizontal();

            int secondsLeft = Mathf.CeilToInt(2f - (float)timeElapsed);
            EditorGUI.BeginDisabledGroup(secondsLeft > 0);
            GUI.backgroundColor = new Color(0.7f, 0.4f, 0.4f);

            string skipText = secondsLeft > 0 ? $"🔒 {ToolLang.Get("Skip", "Пропустить")} ({secondsLeft})" : ToolLang.Get("Skip Tour", "Пропустить");
            if (GUILayout.Button(skipText, GUILayout.Width(140), GUILayout.Height(30)))
            {
                if (_activeTutorialKey == "InteractiveLesson" || _activeTutorialKey == "GraphEditor") CompleteTutorial(_activeTutorialKey, null);
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
                    if (_activeTutorialKey == "InteractiveLesson" || _activeTutorialKey == "GraphEditor") CompleteTutorial(_activeTutorialKey, null);
                    else CompleteTutorial(_activeTutorialKey, window);
                }
                else
                {
                    _currentStep++;
                }
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (Event.current.isMouse || Event.current.isKey || Event.current.type == EventType.ScrollWheel)
            {
                Event.current.Use();
            }
        }
    }
}