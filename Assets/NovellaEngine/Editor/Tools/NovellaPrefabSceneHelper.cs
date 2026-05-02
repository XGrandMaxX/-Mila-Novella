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
        // Возвращает true при успехе.
        public static bool OpenMockScene()
        {
            EnsureMockScene();
            if (IsMockSceneActive()) return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(MOCK_SCENE_PATH, OpenSceneMode.Single);
            return true;
        }

        // Очищает mock-сцену от старого prefab-instance и инстанцирует новый.
        // Создаёт временный Canvas если его ещё нет.
        public static GameObject InstantiatePrefabInMockScene(GameObject prefabAsset)
        {
            if (prefabAsset == null) return null;

            // Получаем или создаём корневой holder.
            var holder = GameObject.Find(PREFAB_HOLDER);
            if (holder == null)
            {
                holder = new GameObject(PREFAB_HOLDER);
                holder.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";
            }

            // Удаляем все предыдущие instances.
            for (int i = holder.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(holder.transform.GetChild(i).gameObject);
            }

            // Получаем или создаём Canvas.
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

                // Камеру для канваса.
                var camGo = new GameObject("[Novella]_PrefabEditor_Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
                canvas.worldCamera = cam;
                camGo.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";

                // EventSystem нужен иначе UI не отвечает в рантайме.
                var es = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
                if (es == null)
                {
                    var esGo = new GameObject("[Novella]_PrefabEditor_EventSystem");
                    esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                    esGo.AddComponent<NovellaEngine.Runtime.NovellaPresetMarker>().PresetName = "PrefabEditor";
                }
            }

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
