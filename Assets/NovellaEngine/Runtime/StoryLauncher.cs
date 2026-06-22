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
        // Editor может подписаться: при misconfig'е (геймплей-сцена = сцене меню)
        // показать клик-диалог «остановить Play и открыть настройки истории».
        // В runtime-сборке не используется — там просто Debug.LogError.
        public static System.Action<NovellaStory> OnSameSceneAsMenuError;

        // Editor подписывается: gameplay-сцена назначена, но не в Build Settings.
        // Параметр — имя сцены, его передадим в Editor чтобы тот сам
        // нашёл .unity и добавил в Build Settings (auto-fix без юзерских
        // действий).
        public static System.Action<string, NovellaStory> OnGameSceneNotInBuildError;

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

        [Tooltip("Fallback scene name used if the picked NovellaStory has no GameSceneName of its own. " +
                 "Modern flow: each story carries its own GameSceneAsset — this field stays for legacy.")]
        public string GameSceneName = "GameScene";

        [Tooltip("Stories shown in the menu. Empty = auto-load all from Resources/Stories.")]
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

            // Сначала показываем главное меню (если оно есть). Stories panel
            // открывается только по «Новая игра» / Btn_StartPlay. Так юзер
            // видит свою сборку меню (с кнопками Continue/Settings/Exit), а
            // не сразу падает в выбор истории.
            ShowPanel(MainMenuPanel != null ? MainMenuPanel : StoriesPanel);
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
                    string btnName = btn.name;
                    var binding = btn.GetComponent<NovellaEngine.Runtime.UI.NovellaUIBinding>();

                    // Btn_StartPlay: ВСЕГДА перехватываем (даже если есть binding)
                    // и решаем умно: 1 история → запускаем, много → показываем выбор.
                    // Существующий binding ShowPanel(StoriesPanel) был не очень
                    // полезен (ничего не происходит с одной историей).
                    if (btnName == "Btn_StartPlay")
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(SmartStartGame);
                        continue;
                    }

                    // Остальные кнопки с NovellaUIBinding не трогаем — действие
                    // живёт в ClickSequence, binding сам цепляется к onClick.
                    if (binding != null) continue;

                    btn.onClick.RemoveAllListeners();
                    // Fallback по именам — для пользователей без NovellaUIBinding.
                    if (btnName == "Btn_Continue") btn.onClick.AddListener(ContinueLastSave);
                    else if (btnName == "Btn_Exit") btn.onClick.AddListener(ExitGame);
                    // Btn_Settings — оставляем юзеру (свой UI настроек).
                }
            }

            if (MCCreationPanel != null)
            {
                if (MCConfirmButton == null)
                {
                    var allBtns = MCCreationPanel.GetComponentsInChildren<Button>(true);
                    MCConfirmButton = allBtns.FirstOrDefault(b => b.name.Contains("Confirm") || b.name.Contains("������"));
                }

                if (MCConfirmButton != null)
                {
                    MCConfirmButton.onClick.RemoveAllListeners();
                    MCConfirmButton.onClick.AddListener(ConfirmMCCreation);
                }

                // Picker внешности (стрелки + аватар) имеет смысл только если
                // у MainCharacterAsset задан хотя бы один body-sprite. Если нет —
                // прячем стрелки и аватар, иначе юзер видит тёмный квадрат и
                // непонятные ←→ кнопки. Вернётся всё как только NovellaCharacter
                // получит спрайты в AvailableBaseBodies.
                bool hasLooks = MainCharacterAsset != null && MainCharacterAsset.AvailableBaseBodies != null && MainCharacterAsset.AvailableBaseBodies.Count > 0;

                if (MCPrevLookButton != null)
                {
                    MCPrevLookButton.gameObject.SetActive(hasLooks);
                    if (hasLooks)
                    {
                        MCPrevLookButton.onClick.RemoveAllListeners();
                        MCPrevLookButton.onClick.AddListener(SelectPrevLook);
                    }
                }

                if (MCNextLookButton != null)
                {
                    MCNextLookButton.gameObject.SetActive(hasLooks);
                    if (hasLooks)
                    {
                        MCNextLookButton.onClick.RemoveAllListeners();
                        MCNextLookButton.onClick.AddListener(SelectNextLook);
                    }
                }

                if (MCAvatarPreview != null)
                {
                    MCAvatarPreview.gameObject.SetActive(hasLooks);
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

        private void ExitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Btn_StartPlay: умный запуск.
        //   • 1 валидная история → запускаем её сразу (TryLaunchStory).
        //   • >1                 → показываем StoriesPanel для выбора.
        //   • 0                  → log + остаёмся на меню.
        // «Валидная» = есть StartingChapter.
        private void SmartStartGame()
        {
            var pool = (SpecificStories != null && SpecificStories.Count > 0)
                ? SpecificStories
                : Resources.LoadAll<NovellaStory>("Stories").ToList();
            // Fallback на "" — если истории не в Resources/Stories.
            if (pool == null || pool.Count == 0)
                pool = Resources.LoadAll<NovellaStory>("").ToList();

            var valid = pool.Where(s => s != null && s.StartingChapter != null).ToList();

            if (valid.Count == 0)
            {
                Debug.LogWarning("[Novella Engine] SmartStartGame: историй нет (или нет с StartingChapter). " +
                                 "Открой Novella Studio → Главная → создай историю и назначь стартовую главу.");
                return;
            }
            if (valid.Count == 1)
            {
                TryLaunchStory(valid[0]);
                return;
            }
            // Больше одной — даём юзеру выбрать.
            ShowPanel(StoriesPanel);
        }

        // Btn_Continue: грузим последнюю историю из её AUTO_SLOT-сейва.
        // ID последней проигранной истории лежит в PlayerPrefs("SelectedStoryID")
        // — устанавливается ProceedToGameScene при предыдущем запуске.
        private void ContinueLastSave()
        {
            string lastStoryId = PlayerPrefs.GetString("SelectedStoryID", "");
            if (string.IsNullOrEmpty(lastStoryId))
            {
                Debug.LogWarning("[Novella Engine] Continue: ещё ни одной истории не запускалось — нечего продолжать.");
                ShowPanel(StoriesPanel);
                return;
            }

            // Найти историю по name из загруженного списка / Resources.
            NovellaStory found = null;
            if (SpecificStories != null)
                foreach (var s in SpecificStories)
                    if (s != null && s.name == lastStoryId) { found = s; break; }
            if (found == null)
            {
                var all = Resources.LoadAll<NovellaStory>("Stories");
                foreach (var s in all)
                    if (s != null && s.name == lastStoryId) { found = s; break; }
            }
            if (found == null)
            {
                Debug.LogWarning($"[Novella Engine] Continue: история '{lastStoryId}' не найдена. Возможно ассет удалён или вне Resources/Stories.");
                ShowPanel(StoriesPanel);
                return;
            }

            if (!NovellaSaveManager.HasSave(found.name, NovellaSaveManager.AUTO_SLOT))
            {
                Debug.LogWarning($"[Novella Engine] Continue: сейва нет у '{found.name}' — стартуй через «Новая игра».");
                ShowPanel(StoriesPanel);
                return;
            }

            ProceedToGameScene(found);
        }

        // Публичный запуск истории по имени (= NovellaStory.name / SelectedStoryID).
        // Панель сейвов в главном меню ставит PlayerPrefs("LoadFromSlot") и зовёт
        // этот метод — ProceedToGameScene подхватит слот, выставит LoadTargetNodeID
        // и возобновит игру с нужной ноды. Резолв истории — как в ContinueLastSave.
        public void LaunchStoryByName(string storyName)
        {
            if (string.IsNullOrEmpty(storyName))
            {
                Debug.LogWarning("[Novella Engine] LaunchStoryByName: пустое имя истории.");
                return;
            }
            NovellaStory found = null;
            if (SpecificStories != null)
                foreach (var s in SpecificStories)
                    if (s != null && s.name == storyName) { found = s; break; }
            if (found == null)
            {
                var all = Resources.LoadAll<NovellaStory>("Stories");
                foreach (var s in all)
                    if (s != null && s.name == storyName) { found = s; break; }
            }
            if (found == null)
            {
                Debug.LogWarning($"[Novella Engine] LaunchStoryByName: история '{storyName}' не найдена (ассет вне Resources/Stories?).");
                return;
            }
            TryLaunchStory(found);
        }

        private void LoadStoriesFromResources()
        {
            if (StoriesContainer != null && StoriesContainer.childCount > 0)
            {
                var existingBtns = StoriesContainer.GetComponentsInChildren<Button>(true);
                if (existingBtns.Length > 0) return;
            }

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

                GameObject prefabToSpawn = story.CustomStoryCardPrefab != null ? story.CustomStoryCardPrefab : StoryButtonPrefab;
                if (prefabToSpawn == null) continue;

                GameObject btnGO = Instantiate(prefabToSpawn, StoriesContainer);
                btnGO.name = "StoryBtn_" + story.name;

                TMP_Text[] texts = btnGO.GetComponentsInChildren<TMP_Text>();
                if (texts.Length > 0) texts[0].text = story.Title;
                if (texts.Length > 1) texts[1].text = story.Description;

                Image[] images = btnGO.GetComponentsInChildren<Image>();
                if (images.Length > 1 && story.CoverImage != null) images[1].sprite = story.CoverImage;

                Button btn = btnGO.GetComponent<Button>();
                if (btn == null) btn = btnGO.GetComponentInChildren<Button>(true);
                if (btn != null) btn.onClick.AddListener(() => TryLaunchStory(story));
            }
        }

        public void TryLaunchStory(NovellaStory story)
        {
            if (story == null)
            {
                Debug.LogError("[Novella Engine] TryLaunchStory: story == null. Кнопка истории привязана к удалённому ассету?");
                return;
            }
            Debug.Log($"[Novella Engine] TryLaunchStory: '{story.name}' | StartingChapter={(story.StartingChapter != null ? story.StartingChapter.name : "NULL")} | GameSceneName='{story.GameSceneName}' | MCCreationPanel={(MCCreationPanel != null ? "set" : "NULL")}");

            _pendingStory = story;
            string mcCreatedKey = $"NovellaSave_{story.name}_MCCreated";
            int mcCreated = PlayerPrefs.GetInt(mcCreatedKey, 0);

            if (MCCreationPanel != null && mcCreated == 0)
            {
                Debug.Log($"[Novella Engine] → Showing MC creation panel (mcCreated={mcCreated}).");
                ShowPanel(MCCreationPanel);
                if (MCNameInput != null) MCNameInput.text = "";
                _currentLookIndex = 0;
                UpdateAvatarPreview();
                return;
            }

            Debug.Log($"[Novella Engine] → Proceeding to game scene (MCCreationPanel={(MCCreationPanel != null ? "set" : "NULL")}, mcCreated={mcCreated}).");
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

            // Валидируем целевую сцену ДО любых side-effects: иначе MC-панель
            // скрывается, ProceedToGameScene тихо падает в Debug.LogError, и
            // юзер видит панель историй — кажется что «нажал имя и ничего не
            // произошло».
            string targetScene = !string.IsNullOrEmpty(_pendingStory.GameSceneName)
                ? _pendingStory.GameSceneName
                : GameSceneName;
            if (string.IsNullOrEmpty(targetScene))
            {
                Debug.LogError($"[Novella Engine] Story '{_pendingStory.name}' has no gameplay scene assigned. " +
                               "Open Novella Studio → Главная → Settings of this story → 'Игровая сцена' and pick one.");
                return;
            }

            string finalName = MCNameInput != null && !string.IsNullOrWhiteSpace(MCNameInput.text) ? MCNameInput.text : "Alex";

            PlayerPrefs.SetString($"NovellaSave_{_pendingStory.name}_MCName", finalName);
            PlayerPrefs.SetInt($"NovellaSave_{_pendingStory.name}_MCBodyID", _currentLookIndex);
            PlayerPrefs.SetInt($"NovellaSave_{_pendingStory.name}_MCCreated", 1);
            PlayerPrefs.Save();

            // Сначала прячем панель — иначе она «маячит» во время загрузки сцены.
            if (MCCreationPanel != null) MCCreationPanel.SetActive(false);

            ProceedToGameScene(_pendingStory);
        }

        private void ProceedToGameScene(NovellaStory story)
        {
            string completeKey = $"NovellaSave_{story.name}_Completed";
            bool isCompleted = PlayerPrefs.GetInt(completeKey, 0) == 1;

            PlayerPrefs.SetString("SelectedStoryID", story.name);
            PlayerPrefs.SetString("SelectedChapterPath", story.StartingChapter.name);

            // Слот по умолчанию для «Продолжить» — авто-слот (0).
            // Если хотим грузить ручной слот, его указали через
            // PlayerPrefs.SetInt("LoadFromSlot", N) до вызова.
            int targetSlot = PlayerPrefs.GetInt("LoadFromSlot", NovellaSaveManager.AUTO_SLOT);

            if (isCompleted)
            {
                NovellaSaveManager.DeleteSlot(story.name, NovellaSaveManager.AUTO_SLOT);
                PlayerPrefs.SetInt(completeKey, 0);
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }
            else if (NovellaSaveManager.HasSave(story.name, targetSlot))
            {
                var info = NovellaSaveManager.GetSlotInfo(story.name, targetSlot);
                PlayerPrefs.SetString("LoadTargetNodeID", info.NodeID ?? "");
            }
            else
            {
                PlayerPrefs.SetString("LoadTargetNodeID", "");
            }

            // Сбрасываем индикатор после использования.
            PlayerPrefs.DeleteKey("LoadFromSlot");
            PlayerPrefs.Save();

            // Приоритет: per-story GameSceneName из самой NovellaStory
            // (новый поток через GameSceneAsset). Если пусто — fallback на
            // launcher-уровень GameSceneName (старая совместимость).
            string targetScene = !string.IsNullOrEmpty(story.GameSceneName)
                ? story.GameSceneName
                : GameSceneName;

            if (string.IsNullOrEmpty(targetScene))
            {
                Debug.LogError($"[Novella Engine] ProceedToGameScene: GameSceneName пустое у '{story.name}'. Открой Novella Studio → Главная → settings истории → «Игровая сцена» → выбери в Галерее.");
                return;
            }

            // Игровая сцена не должна совпадать с текущей (которая меню) —
            // иначе LoadScene просто перезагрузит ту же сцену и игрок увидит
            // снова меню. Эффект «нажал но ничего не произошло».
            string currentSceneName = SceneManager.GetActiveScene().name;
            if (currentSceneName == targetScene)
            {
                Debug.LogError(
                    $"[Novella Engine] Игровая сцена ('{targetScene}') и текущая сцена меню — одна и та же. " +
                    $"После LoadScene игрока вернёт в меню вместо геймплея.\n" +
                    $"Открой Novella Studio → Сцены и Меню → создай НОВУЮ сцену → примени пресет «Игровая сцена» → " +
                    $"добавь в Build Settings → Главная → settings истории → «Игровая сцена» → назначь её.");
                // Editor-only: показывает кликабельный диалог с переходом в
                // настройки истории. В build игра просто остановится здесь.
                OnSameSceneAsMenuError?.Invoke(story);
                return;
            }

            // Проверяем что сцена реально включена в Build Settings — иначе
            // SceneManager.LoadScene упадёт молча в рантайме / в build.
            if (UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(targetScene) < 0)
            {
                // targetScene может быть просто именем (без .unity и пути) —
                // SceneUtility требует путь. Попробуем найти по имени среди
                // сцен в build settings.
                bool found = false;
                int total = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < total; i++)
                {
                    var path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == targetScene) { found = true; break; }
                }
                if (!found)
                {
                    Debug.LogError($"[Novella Engine] ProceedToGameScene: сцена '{targetScene}' не в Build Settings.");
                    // Editor-only: предложит auto-add сцену в Build, остановит
                    // Play и перезапустит запуск истории. В build игра здесь
                    // просто остановится с ошибкой в логе.
                    OnGameSceneNotInBuildError?.Invoke(targetScene, story);
                    return;
                }
            }

            Debug.Log($"[Novella Engine] → SceneManager.LoadScene('{targetScene}')");
            SceneManager.LoadScene(targetScene);
        }
    }
}