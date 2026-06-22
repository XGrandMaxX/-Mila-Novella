// ════════════════════════════════════════════════════════════════════════════
// NovellaSaveSlotsUI — runtime-компонент для панели сохранений/загрузок.
//
// Привязывается на пустой GameObject в UI. В инспекторе указываешь:
//   • Slot Container — Transform (например VerticalLayoutGroup), куда
//     будут спавниться карточки слотов.
//   • Slot Card Prefab — префаб карточки слота. Должен иметь два дочерних
//     объекта с компонентами TextMeshProUGUI («Title» и «Preview») и кнопку
//     с ребёнком-Image (Save/Load action). Подходит prefab из NovellaPrefabs.
//   • Mode — Save или Load (определяет что делает клик по слоту).
//   • Story — какой историй принадлежат слоты (по умолчанию активная).
//
// При появлении на сцене авто-генерирует слот-карточки с метой из
// NovellaSaveManager.GetAllSlots(). Клик по слоту → SaveGameToSlot или
// LoadVariablesFromSlot + перезагрузка сцены.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NovellaEngine.Runtime.UI
{
    public enum NovellaSlotMode { Save, Load }

    public class NovellaSaveSlotsUI : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Container where slot cards will be spawned (usually a VerticalLayoutGroup).")]
        public Transform SlotContainer;

        [Tooltip("Prefab for one slot card. Must contain a Button + TMP_Text(s) for title/preview/timestamp.")]
        public GameObject SlotCardPrefab;

        [Header("Mode")]
        [Tooltip("Save = click to save into that slot. Load = click to load that slot.")]
        public NovellaSlotMode Mode = NovellaSlotMode.Load;

        [Tooltip("If true — show the auto-save slot (slot 0) as the first card. Useful for Load panel; usually false for Save panel.")]
        public bool ShowAutoSlot = true;

        [Header("Story")]
        [Tooltip("Story name used for save keys. Empty = read 'SelectedStoryID' (set by StoryLauncher).")]
        public string StoryNameOverride = "";

        [Header("Empty slot text (optional)")]
        public string EmptyTitle = "Empty slot";
        public string EmptyPreview = "—";

        [Header("Behaviour")]
        [Tooltip("After saving from in-game UI, optionally close this panel (deactivate).")]
        public bool CloseOnSave = true;
        [Tooltip("After loading, scene to load. Empty = current scene reload.")]
        public string LoadTargetScene = "";

        private void OnEnable() { Refresh(); }

        public void Refresh()
        {
            if (SlotContainer == null || SlotCardPrefab == null) return;

            string storyName = ResolveStoryName();
            if (string.IsNullOrEmpty(storyName)) return;

            // Чистим старые карточки.
            for (int i = SlotContainer.childCount - 1; i >= 0; i--)
            {
                var child = SlotContainer.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            var slots = NovellaSaveManager.GetAllSlots(storyName);
            for (int i = 0; i < slots.Count; i++)
            {
                if (i == NovellaSaveManager.AUTO_SLOT && !ShowAutoSlot) continue;
                SpawnCard(slots[i], storyName);
            }
        }

        private string ResolveStoryName()
        {
            if (!string.IsNullOrEmpty(StoryNameOverride)) return StoryNameOverride;
            return PlayerPrefs.GetString("SelectedStoryID", "");
        }

        private void SpawnCard(NovellaSlotInfo info, string storyName)
        {
            var go = Instantiate(SlotCardPrefab, SlotContainer);
            go.name = $"Slot_{info.Slot}";

            // Заполняем TMP_Text-поля по имени дочерних объектов.
            var titleTmp = FindTmp(go.transform, "Title");
            var previewTmp = FindTmp(go.transform, "Preview");
            var timestampTmp = FindTmp(go.transform, "Timestamp");

            string slotLabel = info.Slot == NovellaSaveManager.AUTO_SLOT
                ? "★ Auto"
                : $"Slot {info.Slot}";

            if (titleTmp != null)
            {
                titleTmp.text = info.IsEmpty ? EmptyTitle + $" ({slotLabel})" : slotLabel;
            }
            if (previewTmp != null)
            {
                previewTmp.text = info.IsEmpty ? EmptyPreview : info.PreviewText;
            }
            if (timestampTmp != null)
            {
                timestampTmp.text = info.IsEmpty ? "" : info.Timestamp;
            }

            // Кнопка-карточка.
            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                int captured = info.Slot;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnSlotClicked(storyName, captured, info.IsEmpty));
            }

            // Кнопка удаления (если есть дочерний с именем Delete).
            var delTr = go.transform.Find("Delete");
            if (delTr != null)
            {
                var delBtn = delTr.GetComponent<Button>();
                if (delBtn != null)
                {
                    int capturedSlot = info.Slot;
                    delBtn.gameObject.SetActive(!info.IsEmpty);
                    delBtn.onClick.RemoveAllListeners();
                    delBtn.onClick.AddListener(() =>
                    {
                        NovellaSaveManager.DeleteSlot(storyName, capturedSlot);
                        Refresh();
                    });
                }
            }
        }

        private static TMP_Text FindTmp(Transform root, string name)
        {
            var tr = root.Find(name);
            if (tr == null)
            {
                // Поиск в любом потомке.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) { tr = t; break; }
            }
            return tr != null ? tr.GetComponent<TMP_Text>() : null;
        }

        private void OnSlotClicked(string storyName, int slot, bool isEmpty)
        {
            if (Mode == NovellaSlotMode.Save)
            {
                // Save-режим: ищем NovellaPlayer и сохраняемся через него.
                var player = FindNovellaPlayer();
                if (player != null)
                {
                    string preview = ExtractPlayerPreview(player);
                    string nodeId = ExtractPlayerNodeID(player);
                    int lineIdx = ExtractPlayerLineIndex(player);
                    NovellaSaveManager.SaveGameToSlot(storyName, slot, nodeId, lineIdx, preview);
                    Refresh();
                    if (CloseOnSave) gameObject.SetActive(false);
                }
                else
                {
                    Debug.LogWarning("[NovellaSaveSlotsUI] No NovellaPlayer in scene — can't save.");
                }
            }
            else // Load
            {
                if (isEmpty) return; // пустой слот — нечего грузить
                PlayerPrefs.SetInt("LoadFromSlot", slot);
                PlayerPrefs.Save();

                // Если мы сейчас в gameplay-сцене — грузим переменные сразу
                // и перезагружаем сцену через NovellaPlayer.
                var player = FindNovellaPlayer();
                if (player != null)
                {
                    NovellaSaveManager.LoadVariablesFromSlot(storyName, slot);
                    string sceneToLoad = string.IsNullOrEmpty(LoadTargetScene)
                        ? SceneManager.GetActiveScene().name
                        : LoadTargetScene;
                    var info = NovellaSaveManager.GetSlotInfo(storyName, slot);
                    PlayerPrefs.SetString("LoadTargetNodeID", info.NodeID ?? "");
                    PlayerPrefs.Save();
                    SceneManager.LoadScene(sceneToLoad);
                }
                else
                {
                    // Из главного меню: LoadFromSlot уже выставлен выше.
                    // Запускаем историю через StoryLauncher — ProceedToGameScene
                    // подхватит LoadFromSlot, выставит ноду из слота и возобновит
                    // игру. Раньше тут был только Debug.Log → панель сейвов в меню
                    // молча не работала.
                    var launcher = FindFirstObjectByType<StoryLauncher>();
                    if (launcher != null)
                    {
                        launcher.LaunchStoryByName(storyName);
                    }
                    else
                    {
                        Debug.LogWarning("[NovellaSaveSlotsUI] No StoryLauncher in scene — can't load a save from the menu.");
                    }
                }
            }
        }

        // ─── Reflection-based access к NovellaPlayer (избегаем жёсткой связи) ──

        private static MonoBehaviour FindNovellaPlayer()
        {
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb != null && mb.GetType().Name == "NovellaPlayer") return mb;
            }
            return null;
        }

        private static string ExtractPlayerNodeID(MonoBehaviour player)
        {
            var f = player.GetType().GetField("_currentNodeBase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var node = f?.GetValue(player);
            if (node == null) return "";
            var idField = node.GetType().GetField("NodeID");
            return (idField?.GetValue(node) as string) ?? "";
        }

        private static int ExtractPlayerLineIndex(MonoBehaviour player)
        {
            var f = player.GetType().GetField("_currentLineIndex",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var v = f?.GetValue(player);
            return v is int i ? i : 0;
        }

        private static string ExtractPlayerPreview(MonoBehaviour player)
        {
            var m = player.GetType().GetMethod("ExtractPreviewText",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (m?.Invoke(player, null) as string) ?? "";
        }
    }
}
