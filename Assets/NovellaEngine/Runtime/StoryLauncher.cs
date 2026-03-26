using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;
using System.Linq;

namespace NovellaEngine.Runtime
{
    public class StoryLauncher : MonoBehaviour
    {
        [Header("UI References - Panels")]
        public GameObject MainMenuPanel;
        public GameObject StoriesPanel;
        public GameObject MCCreationPanel;

        [Header("MC Creation UI Elements")]
        public NovellaCharacter MainCharacterAsset;
        public TMP_InputField MCNameInput;
        public Button MCConfirmButton;
        public Image MCAvatarPreview;
        public Button MCPrevLookButton;
        public Button MCNextLookButton;

        [Header("UI References - Stories")]
        public Transform StoriesContainer;
        public GameObject StoryButtonPrefab;
        [HideInInspector] public string GameSceneName = "GameScene";

        [Tooltip("Если список пуст, загрузятся ВСЕ истории из папки Resources/Stories")]
        public List<NovellaStory> SpecificStories = new List<NovellaStory>();

        private NovellaStory _pendingStory;
        private int _currentLookIndex = 0;

        private void Start()
        {
            AutoFindPanels();
            AutoWireButtons();
            LoadStoriesFromResources();

            if (StoriesContainer != null)
            {
                var canvas = StoriesContainer.GetComponentInParent<Canvas>(true);
                if (canvas != null && !canvas.gameObject.activeSelf)
                {
                    canvas.gameObject.SetActive(true);
                }
            }

            ShowPanel(StoriesPanel);
        }
        private void AutoFindPanels()
        {
            if (StoriesContainer != null)
            {
                Transform rootCanvas = StoriesContainer.root;
                var allTransforms = rootCanvas.GetComponentsInChildren<Transform>(true);

                if (MainMenuPanel == null)
                {
                    var p = allTransforms.FirstOrDefault(t => t.name.Contains("MainMenu") || t.name.Contains("ButtonsList"));
                    if (p != null) MainMenuPanel = p.gameObject;
                }

                if (StoriesPanel == null) StoriesPanel = StoriesContainer.gameObject;

                if (MCCreationPanel == null)
                {
                    var mcPanel = allTransforms.FirstOrDefault(t => t.name == "MCCreationPanel");
                    if (mcPanel != null)
                    {
                        MCCreationPanel = mcPanel.gameObject;
                        MCNameInput = MCCreationPanel.GetComponentInChildren<TMP_InputField>(true);

                        MCAvatarPreview = mcPanel.Find("AvatarPreview")?.GetComponent<Image>();
                        MCPrevLookButton = mcPanel.Find("Btn_PrevLook")?.GetComponent<Button>();
                        MCNextLookButton = mcPanel.Find("Btn_NextLook")?.GetComponent<Button>();
                    }
                }
            }
        }

        private void AutoWireButtons()
        {
            if (MainMenuPanel != null)
            {
                Button[] buttons = MainMenuPanel.GetComponentsInChildren<Button>(true);
                foreach (var btn in buttons)
                {
                    btn.onClick.RemoveAllListeners();
                    string btnName = btn.name;
                    if (btnName == "Btn_StartPlay") btn.onClick.AddListener(() => ShowPanel(StoriesPanel));
                    else if (btnName == "Btn_Settings") btn.onClick.AddListener(OpenSettings);
                    else if (btnName == "Btn_Exit") btn.onClick.AddListener(ExitGame);
                }
            }

            if (MCCreationPanel != null)
            {
                if (MCConfirmButton == null)
                {
                    var allBtns = MCCreationPanel.GetComponentsInChildren<Button>(true);
                    MCConfirmButton = allBtns.FirstOrDefault(b => b.name.Contains("Confirm") || b.name.Contains("Готово"));
                }

                if (MCConfirmButton != null)
                {
                    MCConfirmButton.onClick.RemoveAllListeners();
                    MCConfirmButton.onClick.AddListener(ConfirmMCCreation);
                }

                if (MCPrevLookButton != null)
                {
                    MCPrevLookButton.onClick.RemoveAllListeners();
                    MCPrevLookButton.onClick.AddListener(SelectPrevLook);
                }

                if (MCNextLookButton != null)
                {
                    MCNextLookButton.onClick.RemoveAllListeners();
                    MCNextLookButton.onClick.AddListener(SelectNextLook);
                }
            }
        }

        public void ShowPanel(GameObject panelToShow)
        {
            if (MainMenuPanel != null) MainMenuPanel.SetActive(false);
            if (StoriesPanel != null) StoriesPanel.SetActive(false);
            if (MCCreationPanel != null) MCCreationPanel.SetActive(false);

            if (panelToShow != null) panelToShow.SetActive(true);
        }

        private void OpenSettings() { Debug.Log("[Novella Engine] Открыты настройки."); }

        private void ExitGame()
        {
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
                foreach (var s in SpecificStories) { if (s != null) storiesToLoad.Add(s); }
            }
            else { storiesToLoad = Resources.LoadAll<NovellaStory>("Stories").ToList(); }

            if (storiesToLoad.Count == 0) return;

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
                if (images.Length > 1 && story.CoverImage != null) images[1].sprite = story.CoverImage;

                Button btn = btnGO.GetComponent<Button>();
                btn.onClick.AddListener(() => TryLaunchStory(story));
            }
        }

        public void TryLaunchStory(NovellaStory story)
        {
            _pendingStory = story;
            string mcCreatedKey = $"NovellaSave_{story.name}_MCCreated";

            if (MCCreationPanel != null && PlayerPrefs.GetInt(mcCreatedKey, 0) == 0)
            {
                ShowPanel(MCCreationPanel);
                if (MCNameInput != null) MCNameInput.text = "";
                _currentLookIndex = 0;
                UpdateAvatarPreview();
                return;
            }

            ProceedToGameScene(story);
        }

        private void SelectPrevLook()
        {
            if (MainCharacterAsset == null || MainCharacterAsset.AvailableBaseBodies.Count == 0) return;
            _currentLookIndex--;
            if (_currentLookIndex < 0) _currentLookIndex = MainCharacterAsset.AvailableBaseBodies.Count - 1;
            UpdateAvatarPreview();
        }

        private void SelectNextLook()
        {
            if (MainCharacterAsset == null || MainCharacterAsset.AvailableBaseBodies.Count == 0) return;
            _currentLookIndex++;
            if (_currentLookIndex >= MainCharacterAsset.AvailableBaseBodies.Count) _currentLookIndex = 0;
            UpdateAvatarPreview();
        }

        private void UpdateAvatarPreview()
        {
            if (MCAvatarPreview == null) return;

            if (MainCharacterAsset != null && MainCharacterAsset.AvailableBaseBodies.Count > 0)
            {
                MCAvatarPreview.sprite = MainCharacterAsset.AvailableBaseBodies[_currentLookIndex];
                MCAvatarPreview.color = Color.white;
            }
            else
            {
                MCAvatarPreview.sprite = null;
                MCAvatarPreview.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            }
        }

        private void ConfirmMCCreation()
        {
            if (_pendingStory == null) return;

            string finalName = MCNameInput != null && !string.IsNullOrWhiteSpace(MCNameInput.text) ? MCNameInput.text : "Alex";

            PlayerPrefs.SetString($"NovellaSave_{_pendingStory.name}_MCName", finalName);
            PlayerPrefs.SetInt($"NovellaSave_{_pendingStory.name}_MCBodyID", _currentLookIndex);
            PlayerPrefs.SetInt($"NovellaSave_{_pendingStory.name}_MCCreated", 1);
            PlayerPrefs.Save();

            // Выключаем панель перед загрузкой, чтобы она не "моргала" при возврате в меню
            if (MCCreationPanel != null) MCCreationPanel.SetActive(false);

            ProceedToGameScene(_pendingStory);
        }

        private void ProceedToGameScene(NovellaStory story)
        {
            string saveKey = $"NovellaSave_{story.name}_Node";
            string completeKey = $"NovellaSave_{story.name}_Completed";

            bool isCompleted = PlayerPrefs.GetInt(completeKey, 0) == 1;
            bool hasSave = PlayerPrefs.HasKey(saveKey);

            PlayerPrefs.SetString("SelectedStoryID", story.name);
            PlayerPrefs.SetString("SelectedChapterPath", story.StartingChapter.name);

            if (isCompleted)
            {
                PlayerPrefs.DeleteKey(saveKey);
                PlayerPrefs.SetInt(completeKey, 0);
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }
            else if (hasSave)
            {
                PlayerPrefs.SetString("LoadTargetNodeID", PlayerPrefs.GetString(saveKey));
            }
            else
            {
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }

            PlayerPrefs.Save();

            if (!string.IsNullOrEmpty(GameSceneName))
            {
                SceneManager.LoadScene(GameSceneName);
            }
            else
            {
                Debug.LogError("[Novella Engine] Игровая сцена не назначена в настройках меню!");
            }
        }
    }
}