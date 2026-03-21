using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    public class StoryLauncher : MonoBehaviour
    {
        [Header("UI References")]
        public Transform StoriesContainer;
        public GameObject StoryButtonPrefab;
        public string GameSceneName = "GameScene";

        private void Start()
        {
            LoadStoriesFromResources();
        }

        private void LoadStoriesFromResources()
        {
            NovellaStory[] allStories = Resources.LoadAll<NovellaStory>("Stories");

            if (allStories.Length == 0)
            {
                Debug.LogWarning("[Novella Engine] No stories found in Resources/Stories!");
                return;
            }

            foreach (Transform child in StoriesContainer) Destroy(child.gameObject);

            foreach (var story in allStories)
            {
                if (story.StartingChapter == null) continue;

                GameObject btnGO = Instantiate(StoryButtonPrefab, StoriesContainer);
                btnGO.name = "StoryBtn_" + story.Title;

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
            Debug.Log($"[Novella Engine] Launching Story: {story.Title}");

            PlayerPrefs.SetString("SelectedChapterPath", story.StartingChapter.name);
            PlayerPrefs.Save();

            SceneManager.LoadScene(GameSceneName);
        }
    }
}