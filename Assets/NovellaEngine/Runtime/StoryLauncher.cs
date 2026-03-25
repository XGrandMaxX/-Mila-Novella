using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NovellaEngine.Runtime
{
    public class StoryLauncher : MonoBehaviour
    {
        [Header("UI References - Panels")]
        [Tooltip("Сюда можно перетащить панель MainMenu_ButtonsList. Если пусто - скрипт найдет её сам.")]
        public GameObject MainMenuPanel;

        [Tooltip("Панель/ScrollView с каруселью историй.")]
        public GameObject StoriesPanel;

        [Header("UI References - Stories")]
        public Transform StoriesContainer;
        public GameObject StoryButtonPrefab;
        public string GameSceneName = "GameScene";

        [Tooltip("Если список пуст, загрузятся ВСЕ истории из папки Resources/Stories")]
        public List<NovellaStory> SpecificStories = new List<NovellaStory>();

        private void Start()
        {
            AutoFindPanels();
            AutoWireButtons();
            LoadStoriesFromResources();
            ShowPanel(MainMenuPanel);
        }

        private void AutoFindPanels()
        {
            if (MainMenuPanel == null && StoriesContainer != null)
            {
                Transform canvas = StoriesContainer.root;
                Transform generatedPanel = canvas.Find("MainMenu_ButtonsList");
                if (generatedPanel != null) MainMenuPanel = generatedPanel.gameObject;
            }

            if (StoriesPanel == null && StoriesContainer != null)
            {
                StoriesPanel = StoriesContainer.parent.gameObject;
            }
        }

        private void AutoWireButtons()
        {
            if (MainMenuPanel == null) return;

            Button[] buttons = MainMenuPanel.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                btn.onClick.RemoveAllListeners();

                string btnName = btn.name;
                if (btnName == "Btn_StartPlay")
                {
                    btn.onClick.AddListener(() => ShowPanel(StoriesPanel));
                }
                else if (btnName == "Btn_Settings")
                {
                    btn.onClick.AddListener(OpenSettings);
                }
                else if (btnName == "Btn_Exit")
                {
                    btn.onClick.AddListener(ExitGame);
                }
            }
        }

        public void ShowPanel(GameObject panelToShow)
        {
            if (MainMenuPanel != null) MainMenuPanel.SetActive(false);
            if (StoriesPanel != null) StoriesPanel.SetActive(false);
            if (panelToShow != null) panelToShow.SetActive(true);
        }

        private void OpenSettings()
        {
            Debug.Log("[Novella Engine] Открыты настройки (Место для интеграции разработчиков).");
        }

        private void ExitGame()
        {
            Debug.Log("[Novella Engine] Выход из игры...");
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void LoadStoriesFromResources()
        {
            List<NovellaStory> storiesToLoad = new List<NovellaStory>();

            if (SpecificStories != null && SpecificStories.Count > 0)
            {
                foreach (var s in SpecificStories)
                {
                    if (s != null) storiesToLoad.Add(s);
                }
            }
            else
            {
                storiesToLoad = Resources.LoadAll<NovellaStory>("Stories").ToList();
            }

            if (storiesToLoad.Count == 0)
            {
                Debug.LogWarning("[Novella Engine] Истории не найдены в настройках и папке Resources/Stories!");
                return;
            }

            foreach (Transform child in StoriesContainer) Destroy(child.gameObject);

            foreach (var story in storiesToLoad)
            {
                if (story.StartingChapter == null) continue;

                GameObject btnGO = Instantiate(StoryButtonPrefab, StoriesContainer);
                btnGO.name = "StoryBtn_" + story.name;

                TMP_Text[] texts = btnGO.GetComponentsInChildren<TMP_Text>();
                if (texts.Length > 0) texts[0].text = story.Title;
                if (texts.Length > 1) texts[1].text = story.Description;

                Image[] images = btnGO.GetComponentsInChildren<Image>();
                if (images.Length > 1 && story.CoverImage != null)
                {
                    images[1].sprite = story.CoverImage;
                }

                Button btn = btnGO.GetComponent<Button>();
                btn.onClick.AddListener(() => LaunchStory(story));
            }
        }

        public void LaunchStory(NovellaStory story)
        {
            Debug.Log($"[Novella Engine] Запуск истории: {story.Title}");

            string saveKey = $"NovellaSave_{story.name}_Node";
            string completeKey = $"NovellaSave_{story.name}_Completed";

            bool isCompleted = PlayerPrefs.GetInt(completeKey, 0) == 1;
            bool hasSave = PlayerPrefs.HasKey(saveKey);

            PlayerPrefs.SetString("SelectedStoryID", story.name);
            PlayerPrefs.SetString("SelectedChapterPath", story.StartingChapter.name);

            if (isCompleted)
            {
                Debug.Log("[Novella Engine] История полностью пройдена. Начинаем с первой ноды!");
                PlayerPrefs.DeleteKey(saveKey);
                PlayerPrefs.SetInt(completeKey, 0);
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }
            else if (hasSave)
            {
                string targetNode = PlayerPrefs.GetString(saveKey);
                Debug.Log($"[Novella Engine] Найден сейв! Авто-загрузка ноды: {targetNode}");
                PlayerPrefs.SetString("LoadTargetNodeID", targetNode);
            }
            else
            {
                Debug.Log("[Novella Engine] Сохранений нет. Начинаем новую игру.");
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }

            PlayerPrefs.Save();
            SceneManager.LoadScene(GameSceneName);
        }
    }
}