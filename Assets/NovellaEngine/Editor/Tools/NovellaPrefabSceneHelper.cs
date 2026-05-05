// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabSceneHelper — управляет mock-сценой для редактирования
// префабов в Кузнице. Создаёт NovellaTestScene при первом обращении,
// переключает её, инстанцирует выбранный prefab внутри (с временным Canvas).
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NovellaEngine.Editor
{
    public static class NovellaPrefabSceneHelper
    {
        public const string MOCK_SCENE_PATH = "Assets/NovellaEngine/Scenes/NovellaTestScene.unity";
        public const string MARKER_NAME     = "[Novella]_PrefabEditor_Marker";
        public const string CANVAS_NAME     = "[Novella]_PrefabEditor_Canvas";
        public const string PREFAB_HOLDER   = "[Novella]_PrefabHolder";

        // Системная сцена движка — её нельзя удалять / добавлять в Build /
        // открывать из Сцен и Меню. Используется только Кузницей UI как
        // песочница для редактора префабов.
        public static bool IsSystemScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            return scenePath.Replace('\\', '/') == MOCK_SCENE_PATH;
        }

        // Возвращает путь к mock-сцене. Если её нет — создаёт.
        public static string EnsureMockScene()
        {
            if (!File.Exists(MOCK_SCENE_PATH))
            {
                EnsureFolder("Assets/NovellaEngine/Scenes");
                // Создаём пустую сцену и сохраняем по нужному пути.
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(newScene, MOCK_SCENE_PATH);
                EditorSceneManager.CloseScene(newScene, true);
                AssetDatabase.Refresh();
                Debug.Log($"[Novella] Created mock prefab-editor scene at {MOCK_SCENE_PATH}.");
            }
            return MOCK_SCENE_PATH;
        }

        // Проверка что текущая активная сцена — это mock-сцена.
        public static bool IsMockSceneActive()
        {
            var s = EditorSceneManager.GetActiveScene();
            return s.IsValid() && s.path == MOCK_SCENE_PATH;
        }

        // Открывает mock-сцену (если предыдущая dirty — спросит юзера).
        // Возвращает true при успехе. Используется только в legacy-сценариях;
        // основной путь — OpenMockSceneAdditive (см. ниже).
        public static bool OpenMockScene()
        {
            EnsureMockScene();
            if (IsMockSceneActive()) return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(MOCK_SCENE_PATH, OpenSceneMode.Single);
            return true;
        }

        // Подгружает mock-сцену поверх текущих (additive) и делает её активной —
        // как «DontDestroyOnLoad»-слой для prefab-редактора. Gameplay-сцена
        // остаётся в памяти и не теряет правок. Save current не требуется.
        public static bool OpenMockSceneAdditive()
        {
            EnsureMockScene();
            var loaded = EditorSceneManager.GetSceneByPath(MOCK_SCENE_PATH);
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                loaded = EditorSceneManager.OpenScene(MOCK_SCENE_PATH, OpenSceneMode.Additive);
            }
            if (!loaded.IsValid()) return false;
            if (EditorSceneManager.GetActiveScene() != loaded)
            {
                EditorSceneManager.SetActiveScene(loaded);
            }
            return true;
        }

        // Снимает mock-сцену с additive-загрузки. Перед закрытием передаёт
        // active-флаг другой загруженной сцене (gameplay), иначе Unity не
        // даёт закрыть active-сцену. Если mock — ЕДИНСТВЕННАЯ загруженная
        // сцена, не закрываем (Unity упадёт), просто логгируем.
        public static bool CloseMockSceneAdditive()
        {
            var mockScene = EditorSceneManager.GetSceneByPath(MOCK_SCENE_PATH);
            if (!mockScene.IsValid() || !mockScene.isLoaded) return false;

            UnityEngine.SceneManagement.Scene? another = null;
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s != mockScene && s.isLoaded) { another = s; break; }
            }

            if (another == null)
            {
                Debug.LogWarning(
                    "[Novella] Cannot close PrefabEditor mock scene additively — it's the only loaded scene. " +
                    "Open your gameplay scene first, then switch back to Prefabs/Scene tabs in UI Forge.");
                return false;
            }

            if (EditorSceneManager.GetActiveScene() == mockScene)
                EditorSceneManager.SetActiveScene(another.Value);

            return EditorSceneManager.CloseScene(mockScene, true);
        }

        // Создаёт Holder/Canvas/Camera/EventSystem в mock-сцене если их ещё нет.
        // Все объекты помечаются NovellaPresetMarker(PresetName="PrefabEditor"),
        // чтобы CleanupMockScene их сохранял. Возвращает Canvas.
        public static Canvas EnsureMockSceneChrome()
        {
            if (!IsMockSceneActive()) return null;

            // Holder.
            var holder = GameObject.Find(PREFAB_HOLDER);
            if (holder == null)
            {
                holder = new GameObject(PREFAB_HOLDER);
                holder.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";
            }

            // Canvas + Camera.
            var canvasGo = GameObject.Find(CANVAS_NAME);
            Canvas canvas = canvasGo != null ? canvasGo.GetComponent<Canvas>() : null;
            if (canvas == null)
            {
                canvasGo = new GameObject(CANVAS_NAME);
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = 5f;
                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                canvasGo.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";

                var camGo = new GameObject("[Novella]_PrefabEditor_Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
                canvas.worldCamera = cam;
                camGo.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";
            }

            // EventSystem.
            var es = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
            if (es == null)
            {
                var esGo = new GameObject("[Novella]_PrefabEditor_EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                esGo.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return canvas;
        }

        // Очищает mock-сцену от старого prefab-instance и инстанцирует новый.
        public static GameObject InstantiatePrefabInMockScene(GameObject prefabAsset)
        {
            if (prefabAsset == null) return null;

            // Сначала вычищаем всё, что не относится к редактору префабов
            // (старые инстансы, случайно оставшиеся пресеты типа MainMenu и т.п.).
            CleanupMockScene();

            var canvas = EnsureMockSceneChrome();
            if (canvas == null) return null;

            // Инстанцируем под Canvas (а не holder), чтобы UI-prefab видел
            // canvas как родителя.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, canvas.transform);
            if (instance != null)
            {
                var rt = instance.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot     = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return instance;
        }

        // Полная очистка mock-сцены: удаляем все root-объекты, не помеченные
        // как «PrefabEditor», а внутри editor-canvas — все prefab-instances
        // (которые могли остаться от прошлых сессий).
        // Используется и при переключении в Prefabs-режим, и перед каждым
        // открытием нового префаба.
        public static void CleanupMockScene()
        {
            if (!IsMockSceneActive()) return;
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            bool changed = false;
            foreach (var root in roots)
            {
                if (root == null) continue;
                var marker = root.GetComponent<NovellaEngine.Runtime.NovellaPresetMarker>();
                if (marker != null && marker.PresetName == "PrefabEditor")
                {
                    // Это наш editor-объект (Canvas/Camera/EventSystem/Holder).
                    // Внутри Canvas могут болтаться чужие prefab-instances —
                    // вычищаем их (но сами markers оставляем).
                    if (root.name == CANVAS_NAME || root.name == PREFAB_HOLDER)
                    {
                        for (int i = root.transform.childCount - 1; i >= 0; i--)
                        {
                            var child = root.transform.GetChild(i).gameObject;
                            var childMarker = child.GetComponent<NovellaEngine.Runtime.NovellaPresetMarker>();
                            if (childMarker == null || childMarker.PresetName != "PrefabEditor")
                            {
                                Object.DestroyImmediate(child);
                                changed = true;
                            }
                        }
                    }
                    continue;
                }

                // Чужой объект (MainMenu, оставшийся пресет и т.п.) — удаляем.
                Object.DestroyImmediate(root);
                changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                // Тихое авто-сохранение — чтобы Unity не просил подтверждать
                // изменения mock-сцены при каждом следующем переключении.
                EditorSceneManager.SaveScene(scene);
            }
        }

        // Сохраняет текущий instance prefab'а обратно в asset через
        // PrefabUtility.ApplyPrefabInstance.
        public static bool SaveCurrentPrefab()
        {
            var canvasGo = GameObject.Find(CANVAS_NAME);
            if (canvasGo == null) return false;
            for (int i = 0; i < canvasGo.transform.childCount; i++)
            {
                var child = canvasGo.transform.GetChild(i).gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(child))
                {
                    var asset = PrefabUtility.GetCorrespondingObjectFromSource(child) as GameObject;
                    PrefabUtility.ApplyPrefabInstance(child, InteractionMode.AutomatedAction);
                    if (asset != null)
                    {
                        string p = AssetDatabase.GetAssetPath(asset);
                        NovellaPrefabHistory.Log("save", asset.name, "", p);
                    }
                    return true;
                }
            }
            return false;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string acc = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = acc + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(acc, parts[i]);
                acc = next;
            }
        }
    }
}
