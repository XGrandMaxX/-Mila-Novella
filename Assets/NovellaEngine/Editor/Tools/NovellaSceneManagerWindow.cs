using NovellaEngine.Data;
using NovellaEngine.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NovellaEngine.Editor
{
    public class NovellaSceneManagerWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private Texture2D _sceneIcon;
        private Dictionary<string, string> _sceneTagsCache = new Dictionary<string, string>();

        [MenuItem("Tools/Novella Engine/🛠 Scene Manager", false, 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<NovellaSceneManagerWindow>(false, "Scene Manager", true);
            window.minSize = new Vector2(600, 650);
            window.Show();
        }

        private void OnEnable()
        {
            _sceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon").image as Texture2D;
            _sceneTagsCache.Clear();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSceneList();
            GUILayout.Space(15);
            DrawCurrentSceneContext();
        }

        private void DrawHeader()
        {
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("🛠 " + ToolLang.Get("NOVELLA SCENE MANAGER", "МЕНЕДЖЕР СЦЕН NOVELLA"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.2f, 0.7f, 1f) } });
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(ToolLang.IsRU ? "RU" : "EN", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                ToolLang.Toggle();
                _sceneTagsCache.Clear();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(15);
        }

        private string GetSceneTag(string path)
        {
            if (_sceneTagsCache.TryGetValue(path, out string tag)) return tag;

            tag = ToolLang.Get("[ Empty ]", "[ Пустая ]");
            if (File.Exists(path))
            {
                try
                {
                    string content = File.ReadAllText(path);
                    if (content.Contains("NovellaPlayer")) tag = ToolLang.Get("[ Gameplay ]", "[ Игра ]");
                    else if (content.Contains("StoryLauncher")) tag = ToolLang.Get("[ Main Menu ]", "[ Меню ]");
                }
                catch { }
            }
            _sceneTagsCache[path] = tag;
            return tag;
        }

        private void DrawSceneList()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Label("📂 " + ToolLang.Get("Project Scenes (Build Settings)", "Сцены проекта (Build Settings)"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });

            EditorGUILayout.HelpBox(ToolLang.Get(
                "💡 Make a scene 'First' so it loads automatically when the game starts. Usually, this should be your Main Menu scene!",
                "💡 Сделайте сцену стартовой, чтобы она загружалась первой при входе в игру (обычно это Главное Меню)."
            ), MessageType.Info);

            GUILayout.Space(5);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(250));

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Count == 0)
            {
                GUILayout.Label(ToolLang.Get("No scenes in Build Settings.", "Нет сцен в Build Settings."), EditorStyles.centeredGreyMiniLabel);
            }

            for (int i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                if (string.IsNullOrEmpty(scene.path)) continue;

                string sceneName = Path.GetFileNameWithoutExtension(scene.path);
                bool isActive = EditorSceneManager.GetActiveScene().path == scene.path;
                string sceneTag = GetSceneTag(scene.path);

                GUILayout.BeginHorizontal(EditorStyles.helpBox);

                GUILayout.Label(_sceneIcon, GUILayout.Width(20), GUILayout.Height(20));

                if (isActive)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
                    GUILayout.Button($"▶ {sceneName} (Active)  {sceneTag}", new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(25), GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    if (GUILayout.Button($"{sceneName}  {sceneTag}", new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(25), GUILayout.ExpandWidth(true)))
                    {
                        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            EditorSceneManager.OpenScene(scene.path);
                    }
                }

                if (i > 0)
                {
                    GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
                    if (GUILayout.Button("★ " + ToolLang.Get("Make First", "Сделать первой"), EditorStyles.miniButtonMid, GUILayout.Width(125), GUILayout.Height(25)))
                    {
                        MakeSceneFirst(i);
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    GUILayout.Label(ToolLang.Get("★ Start Scene", "★ Стартовая"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.7f, 0.1f) } }, GUILayout.Width(125), GUILayout.Height(25));
                }

                GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
                if (GUILayout.Button("R", EditorStyles.miniButtonMid, GUILayout.Width(30), GUILayout.Height(25)))
                {
                    string p = scene.path;
                    RenamePopup.ShowPopup(sceneName, newName => RenameScene(p, newName));
                }

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("✖", EditorStyles.miniButtonRight, GUILayout.Width(30), GUILayout.Height(25)))
                {
                    string p = scene.path;
                    EditorApplication.delayCall += () => DeleteSceneSafely(p);
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                if (i == 0)
                {
                    var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(2), GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(rect, new Color(1f, 0.7f, 0.1f, 0.8f));
                    GUILayout.Space(2);
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
            if (GUILayout.Button(ToolLang.Get("+ Create New Scene", "+ Создать новую сцену"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(35)))
            {
                RenamePopup.ShowPopup("NewScene", CreateAndRegisterNewScene);
            }
            GUI.backgroundColor = Color.white;

            Scene currentScene = EditorSceneManager.GetActiveScene();
            bool inBuild = scenes.Any(s => s.path == currentScene.path);

            if (!string.IsNullOrEmpty(currentScene.path) && !inBuild)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
                if (GUILayout.Button(ToolLang.Get("Add Current To Build", "Добавить текущую в Build"), GUILayout.Height(35)))
                {
                    scenes.Add(new EditorBuildSettingsScene(currentScene.path, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void MakeSceneFirst(int index)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (index < 0 || index >= scenes.Count) return;

            var targetScene = scenes[index];
            scenes.RemoveAt(index);
            scenes.Insert(0, targetScene);

            EditorBuildSettings.scenes = scenes.ToArray();
            Repaint();
        }

        private void DrawCurrentSceneContext()
        {
            Scene currentScene = EditorSceneManager.GetActiveScene();
            string sceneName = string.IsNullOrEmpty(currentScene.name) ? "Unsaved Scene" : currentScene.name;

            GUILayout.BeginVertical(GUI.skin.box);

            string tag = GetSceneTag(currentScene.path);
            GUILayout.Label($"🎯 {sceneName} {tag}", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
            GUILayout.Space(5);

            if (string.IsNullOrEmpty(currentScene.path))
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Please SAVE the scene before setting it up! (Ctrl+S)", "Пожалуйста, СОХРАНИТЕ сцену (Ctrl+S) перед настройкой!"), MessageType.Warning);
                GUILayout.EndVertical();
                return;
            }

            string noteKey = "NovellaNote_" + currentScene.path;
            string currentNote = EditorPrefs.GetString(noteKey, "");
            GUILayout.Label(ToolLang.Get("📝 Scene Notes:", "📝 Заметки по сцене:"), EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            string newNote = EditorGUILayout.TextArea(currentNote, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.Height(50));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(noteKey, newNote);

            GUILayout.Space(10);

            bool hasPlayer = FindFirstObjectByType<NovellaPlayer>() != null;
            bool hasLauncher = FindFirstObjectByType<StoryLauncher>() != null;

            if (hasPlayer || hasLauncher)
            {
                GUILayout.BeginHorizontal();

                GUI.backgroundColor = new Color(0.8f, 0.4f, 0.8f);
                if (GUILayout.Button("🎨 " + ToolLang.Get("Open UI Editor", "Редактор UI"), new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(40)))
                {
                    NovellaUIEditorWindow.ShowWindow();
                }

                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("🗑 " + ToolLang.Get("Remove Setup", "Очистить сцену от сетапа"), new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(40), GUILayout.Width(200)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Warning", "Внимание"), ToolLang.Get("This will completely remove Novella Engine logic from this scene. Continue?", "Это полностью удалит логику Novella Engine с этой сцены. Продолжить?"), "OK", "Cancel"))
                    {
                        DestroyEngineRoot();
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(ToolLang.Get("Configure this empty scene:", "Настройте эту пустую сцену:"), EditorStyles.miniBoldLabel);
                GUILayout.BeginHorizontal();

                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                if (GUILayout.Button("🎮 " + ToolLang.Get("Make Gameplay", "Сделать Игровой"), new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(50)))
                {
                    PerformGameplaySetup();
                }

                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                if (GUILayout.Button("📱 " + ToolLang.Get("Make Main Menu", "Сделать Меню"), new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }, GUILayout.Height(50)))
                {
                    PerformMainMenuSetup();
                }

                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void CreateAndRegisterNewScene(string newName)
        {
            string dir = "Assets/NovellaEngine/Scenes";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Scenes");
                AssetDatabase.Refresh();
            }

            string path = $"{dir}/{newName}.unity";
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog(ToolLang.Get("Error", "Ошибка"), ToolLang.Get("A scene with this name already exists!", "Сцена с таким именем уже существует!"), "OK");
                return;
            }

            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, path);

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            _sceneTagsCache.Clear();
        }

        private void RenameScene(string oldPath, string newName)
        {
            AssetDatabase.RenameAsset(oldPath, newName);
            AssetDatabase.Refresh();
            _sceneTagsCache.Clear();
        }

        private void DeleteSceneSafely(string path)
        {
            bool isActive = (EditorSceneManager.GetActiveScene().path == path);
            string tag = GetSceneTag(path);
            bool hasSetup = tag.Contains("Gameplay") || tag.Contains("Menu") || tag.Contains("Игра") || tag.Contains("Меню");
            string sceneName = Path.GetFileNameWithoutExtension(path);

            if (hasSetup)
            {
                if (!EditorUtility.DisplayDialog(ToolLang.Get("Warning!", "Внимание!"), ToolLang.Get($"Scene '{sceneName}' contains a Novella Setup! Are you sure you want to delete it?", $"Сцена '{sceneName}' содержит логику NovellaEngine! Вы уверены, что хотите её удалить?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена"))) return;
                if (!EditorUtility.DisplayDialog(ToolLang.Get("DOUBLE WARNING!", "ДВОЙНОЕ ПРЕДУПРЕЖДЕНИЕ!"), ToolLang.Get("This action cannot be undone. All UI and Logic in this scene will be lost forever. DELETE?", "Это действие необратимо. Весь UI и настройки будут удалены НАВСЕГДА. УДАЛИТЬ?"), ToolLang.Get("DESTROY", "УНИЧТОЖИТЬ"), ToolLang.Get("Cancel", "Отмена"))) return;
            }
            else
            {
                if (!EditorUtility.DisplayDialog(ToolLang.Get("Delete Scene", "Удалить сцену"), ToolLang.Get($"Are you sure you want to delete '{sceneName}'?", $"Вы уверены, что хотите удалить пустую сцену '{sceneName}'?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена"))) return;
            }

            if (isActive)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            AssetDatabase.DeleteAsset(path);
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == path);
            EditorBuildSettings.scenes = scenes.ToArray();
            _sceneTagsCache.Clear();
        }

        private void DestroyEngineRoot()
        {
            GameObject rootGO = GameObject.Find("[NovellaEngine]");
            if (rootGO != null) Undo.DestroyObjectImmediate(rootGO);
            _sceneTagsCache.Clear();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private void SetupInputSystem(Transform uiTransform)
        {
            UnityEngine.Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/PlayerSettings.asset");
            if (settingsAssets != null && settingsAssets.Length > 0)
            {
                SerializedObject ps = new SerializedObject(settingsAssets[0]);
                SerializedProperty activeInput = ps.FindProperty("activeInputHandler");
                if (activeInput != null && activeInput.intValue != 2) { activeInput.intValue = 2; ps.ApplyModifiedProperties(); }
            }

            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.transform.SetParent(uiTransform, false);
                eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
                Type newInputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (newInputModuleType != null) eventSystem.AddComponent(newInputModuleType);
                else eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
            }
        }

        private Canvas SetupCoreCanvas()
        {
            GameObject rootGO = GameObject.Find("[NovellaEngine]");
            if (rootGO == null) rootGO = new GameObject("[NovellaEngine]");

            Transform uiTransform = rootGO.transform.Find("[UI]");
            if (uiTransform == null) { GameObject uiGO = new GameObject("[UI]"); uiGO.transform.SetParent(rootGO.transform, false); uiTransform = uiGO.transform; }

            Camera mainCam = Camera.main;
            if (mainCam == null) mainCam = FindFirstObjectByType<Camera>();
            if (mainCam == null)
            {
                GameObject camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
                camGO.transform.position = new Vector3(0, 0, -10f);
                mainCam = camGO.AddComponent<Camera>(); mainCam.orthographic = true; mainCam.backgroundColor = Color.black;
            }

            SetupInputSystem(uiTransform);

            Canvas canvas = uiTransform.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("Novella Canvas");
                canvasGO.transform.SetParent(uiTransform, false);
                canvas = canvasGO.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCam;
            canvas.planeDistance = 5f;
            canvas.sortingOrder = -10;

            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;

            if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            if (canvas.transform.Find("Background") == null)
            {
                GameObject bgObj = new GameObject("Background");
                bgObj.transform.SetParent(canvas.transform, false); bgObj.transform.SetAsFirstSibling();
                var bgImg = bgObj.AddComponent<UnityEngine.UI.Image>(); bgImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                var bgRect = bgObj.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            }

            return canvas;
        }

        private void PerformMainMenuSetup()
        {
            Canvas canvas = SetupCoreCanvas();
            GameObject rootGO = canvas.transform.parent.parent.gameObject;

            StoryLauncher launcher = rootGO.GetComponent<StoryLauncher>();
            if (launcher == null) launcher = rootGO.AddComponent<StoryLauncher>();

            Transform listTransform = canvas.transform.Find("Stories List");
            if (listTransform == null)
            {
                GameObject listObj = new GameObject("Stories List");
                listObj.transform.SetParent(canvas.transform, false);
                var listRect = listObj.AddComponent<RectTransform>();
                listRect.anchorMin = new Vector2(0.1f, 0.1f); listRect.anchorMax = new Vector2(0.9f, 0.9f);
                listRect.offsetMin = Vector2.zero; listRect.offsetMax = Vector2.zero;

                var hlg = listObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hlg.spacing = 30; hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlHeight = false; hlg.childControlWidth = false;

                var csf = listObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                csf.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

                listTransform = listObj.transform;
            }
            launcher.StoriesContainer = listTransform;

            Canvas listCanvas = listTransform.GetComponent<Canvas>();
            if (listCanvas == null) listCanvas = listTransform.gameObject.AddComponent<Canvas>();
            listCanvas.overrideSorting = true;
            listCanvas.sortingOrder = 50;
            if (listTransform.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) listTransform.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            string prefabPath = "Assets/NovellaEngine/Runtime/Prefabs/StoryButton.prefab";
            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (savedPrefab == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Runtime/Prefabs"))
                    System.IO.Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Runtime/Prefabs");

                GameObject buttonObj = new GameObject("StoryButtonTemplate");
                var btnRect = buttonObj.AddComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(350, 500);
                buttonObj.AddComponent<UnityEngine.UI.Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
                buttonObj.AddComponent<UnityEngine.UI.Button>();

                GameObject coverObj = new GameObject("CoverImage");
                coverObj.transform.SetParent(buttonObj.transform, false);
                var coverRect = coverObj.AddComponent<RectTransform>();
                coverRect.anchorMin = new Vector2(0.05f, 0.3f); coverRect.anchorMax = new Vector2(0.95f, 0.95f);
                coverRect.offsetMin = Vector2.zero; coverRect.offsetMax = Vector2.zero;
                coverObj.AddComponent<UnityEngine.UI.Image>().color = Color.gray;

                GameObject titleObj = new GameObject("TitleText");
                titleObj.transform.SetParent(buttonObj.transform, false);
                var titleRect = titleObj.AddComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0.05f, 0.15f); titleRect.anchorMax = new Vector2(0.95f, 0.25f);
                titleRect.offsetMin = Vector2.zero; titleRect.offsetMax = Vector2.zero;
                var tText = titleObj.AddComponent<TMPro.TextMeshProUGUI>();
                tText.text = "Story Title"; tText.alignment = TMPro.TextAlignmentOptions.Center; tText.fontSize = 28; tText.fontStyle = TMPro.FontStyles.Bold;

                GameObject descObj = new GameObject("DescText");
                descObj.transform.SetParent(buttonObj.transform, false);
                var descRect = descObj.AddComponent<RectTransform>();
                descRect.anchorMin = new Vector2(0.05f, 0.05f); descRect.anchorMax = new Vector2(0.95f, 0.15f);
                descRect.offsetMin = Vector2.zero; descRect.offsetMax = Vector2.zero;
                var dText = descObj.AddComponent<TMPro.TextMeshProUGUI>();
                dText.text = "Description..."; dText.alignment = TMPro.TextAlignmentOptions.Center; dText.fontSize = 18; dText.color = Color.gray;

                savedPrefab = PrefabUtility.SaveAsPrefabAsset(buttonObj, prefabPath);
                DestroyImmediate(buttonObj);
                AssetDatabase.Refresh();
            }
            launcher.StoryButtonPrefab = savedPrefab;

            EditorUtility.SetDirty(launcher);
            _sceneTagsCache.Clear();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private void PerformGameplaySetup()
        {
            Canvas canvas = SetupCoreCanvas();
            GameObject rootGO = canvas.transform.parent.parent.gameObject;

            NovellaPlayer player = rootGO.GetComponent<NovellaPlayer>();
            if (player == null) player = rootGO.AddComponent<NovellaPlayer>();

            Transform dpTransform = canvas.transform.Find("Dialogue Panel");
            if (dpTransform == null)
            {
                GameObject dialoguePanel = new GameObject("Dialogue Panel");
                dialoguePanel.transform.SetParent(canvas.transform, false);
                var bg = dialoguePanel.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(0, 0, 0, 0.8f);
                var panelRect = dialoguePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.1f, 0.05f); panelRect.anchorMax = new Vector2(0.9f, 0.3f);
                panelRect.offsetMin = Vector2.zero; panelRect.offsetMax = Vector2.zero;
                dpTransform = dialoguePanel.transform;
            }
            player.DialoguePanel = dpTransform.gameObject;

            Canvas dpCanvas = dpTransform.GetComponent<Canvas>();
            if (dpCanvas == null) dpCanvas = dpTransform.gameObject.AddComponent<Canvas>();
            dpCanvas.overrideSorting = true;
            dpCanvas.sortingOrder = 50;
            if (dpTransform.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) dpTransform.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Transform nameTransform = dpTransform.Find("Speaker Name");
            if (nameTransform == null)
            {
                GameObject nameObj = new GameObject("Speaker Name");
                nameObj.transform.SetParent(dpTransform, false);
                var nameText = nameObj.AddComponent<TMPro.TextMeshProUGUI>();
                nameText.text = "Speaker Name"; nameText.fontSize = 40; nameText.fontStyle = TMPro.FontStyles.Bold; nameText.color = new Color(1f, 0.8f, 0.4f); nameText.raycastTarget = false;
                var nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0.05f, 0.75f); nameRect.anchorMax = new Vector2(0.95f, 0.95f);
                nameRect.offsetMin = Vector2.zero; nameRect.offsetMax = Vector2.zero;
                nameTransform = nameObj.transform;
            }
            player.SpeakerNameText = nameTransform.GetComponent<TMPro.TextMeshProUGUI>();

            Transform bodyTransform = dpTransform.Find("Dialogue Body");
            if (bodyTransform == null)
            {
                GameObject bodyObj = new GameObject("Dialogue Body");
                bodyObj.transform.SetParent(dpTransform, false);
                var bodyText = bodyObj.AddComponent<TMPro.TextMeshProUGUI>();

                // === ИСПРАВЛЕНО ПРЕДУПРЕЖДЕНИЕ enableWordWrapping ===
                bodyText.textWrappingMode = TMPro.TextWrappingModes.Normal;

                bodyText.text = "Dialogue text goes here..."; bodyText.fontSize = 32; bodyText.color = Color.white; bodyText.alignment = TMPro.TextAlignmentOptions.TopLeft; bodyText.richText = true; bodyText.raycastTarget = false;
                var bodyRect = bodyObj.GetComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0.05f, 0.1f); bodyRect.anchorMax = new Vector2(0.95f, 0.7f);
                bodyRect.offsetMin = Vector2.zero; bodyRect.offsetMax = Vector2.zero;
                bodyTransform = bodyObj.transform;
            }
            player.DialogueBodyText = bodyTransform.GetComponent<TMPro.TextMeshProUGUI>();

            Transform choicesTransform = canvas.transform.Find("Choices Container");
            if (choicesTransform == null)
            {
                GameObject choicesObj = new GameObject("Choices Container");
                choicesObj.transform.SetParent(canvas.transform, false);
                var choicesRect = choicesObj.AddComponent<RectTransform>();
                choicesRect.anchorMin = new Vector2(0.3f, 0.4f); choicesRect.anchorMax = new Vector2(0.7f, 0.9f);
                choicesRect.offsetMin = Vector2.zero; choicesRect.offsetMax = Vector2.zero;
                choicesTransform = choicesObj.transform;
            }

            Canvas chCanvas = choicesTransform.GetComponent<Canvas>();
            if (chCanvas == null) chCanvas = choicesTransform.gameObject.AddComponent<Canvas>();
            chCanvas.overrideSorting = true;
            chCanvas.sortingOrder = 60;
            if (choicesTransform.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) choicesTransform.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var vlg = choicesTransform.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            if (vlg == null) vlg = choicesTransform.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.spacing = 15; vlg.childAlignment = TextAnchor.UpperCenter; vlg.childControlHeight = false; vlg.childControlWidth = false; vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = false;

            var csf = choicesTransform.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (csf == null) csf = choicesTransform.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            csf.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize; csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            player.ChoiceContainer = choicesTransform;

            if (player.ChoiceButtonPrefab == null)
            {
                string prefabPath = "Assets/NovellaEngine/Runtime/Prefabs/ChoiceButton.prefab";
                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                if (savedPrefab == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Runtime/Prefabs")) System.IO.Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Runtime/Prefabs");
                    GameObject buttonObj = new GameObject("ChoiceButtonTemplate");
                    var btnRect = buttonObj.AddComponent<RectTransform>(); btnRect.sizeDelta = new Vector2(250, 80);
                    buttonObj.AddComponent<UnityEngine.UI.Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
                    buttonObj.AddComponent<UnityEngine.UI.Button>();
                    GameObject btnTextObj = new GameObject("Text"); btnTextObj.transform.SetParent(buttonObj.transform, false);
                    var btnText = btnTextObj.AddComponent<TMPro.TextMeshProUGUI>(); btnText.text = "Choice Text"; btnText.alignment = TMPro.TextAlignmentOptions.Center; btnText.raycastTarget = false;
                    var btnTextRect = btnTextObj.GetComponent<RectTransform>(); btnTextRect.anchorMin = Vector2.zero; btnTextRect.anchorMax = Vector2.one; btnTextRect.offsetMin = Vector2.zero; btnTextRect.offsetMax = Vector2.zero;
                    savedPrefab = PrefabUtility.SaveAsPrefabAsset(buttonObj, prefabPath); DestroyImmediate(buttonObj);
                    AssetDatabase.Refresh();
                }
                player.ChoiceButtonPrefab = savedPrefab;
            }

            Transform charsHolder = rootGO.transform.Find("Characters Holder");
            if (charsHolder == null)
            {
                GameObject charsObj = new GameObject("Characters Holder");
                charsObj.transform.SetParent(rootGO.transform, false);
                charsHolder = charsObj.transform;
            }
            player.CharactersContainer = charsHolder;

            EditorUtility.SetDirty(player);
            _sceneTagsCache.Clear();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}