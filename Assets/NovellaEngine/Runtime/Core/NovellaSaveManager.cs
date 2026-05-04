using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NovellaEngine.Runtime
{
    [Serializable]
    public class NovellaSaveData
    {
        public int CurrentLineIndex = 0;
        // Заголовок-превью для слота (последняя реплика). Пишется при сохранении.
        public string PreviewText = "";
        // Имя ноды, на которой сохранились (для отображения в слот-карточке).
        public string CurrentNodeID = "";
        // Дата/время сохранения (ISO-8601 в локальной таймзоне).
        public string Timestamp = "";

        public List<string> IntKeys = new List<string>();
        public List<int> IntValues = new List<int>();
        public List<string> BoolKeys = new List<string>();
        public List<bool> BoolValues = new List<bool>();
        public List<string> StringKeys = new List<string>();
        public List<string> StringValues = new List<string>();
    }

    /// <summary>
    /// Метаданные слота для UI-карточки. Не содержат сами переменные —
    /// только то что нужно показать игроку перед загрузкой.
    /// </summary>
    public class NovellaSlotInfo
    {
        public int Slot;
        public bool IsEmpty;
        public string NodeID;
        public string PreviewText;
        public string Timestamp;     // строкой как сохранили
        public DateTime TimeUtc;     // парсинг для сортировки
    }

    public static class NovellaSaveManager
    {
        // Слот 0 — автосохранение (используется кнопкой «Продолжить» в меню).
        // Слоты 1..MAX_SLOTS — ручные слоты игрока.
        public const int AUTO_SLOT = 0;
        public const int MAX_SLOTS = 9; // 9 ручных + 1 авто = 10 всего

        private static string SaveKey(string storyName, int slot, string suffix)
            => $"NovellaSave_{storyName}_S{slot}_{suffix}";

        // ─── Backward-compat API ───
        // Старый код (NovellaPlayer / StoryLauncher) вызывает SaveGame()/LoadVariables()
        // без слота — это попадает в AUTO_SLOT.
        public static void SaveGame(string storyName, string nodeID, int currentLineIndex)
            => SaveGameToSlot(storyName, AUTO_SLOT, nodeID, currentLineIndex, "");

        public static void SaveGame(string storyName, string nodeID, int currentLineIndex, string previewText)
            => SaveGameToSlot(storyName, AUTO_SLOT, nodeID, currentLineIndex, previewText);

        public static NovellaSaveData LoadVariables(string storyName)
            => LoadVariablesFromSlot(storyName, AUTO_SLOT);

        // ─── Slot-based API ───
        public static void SaveGameToSlot(string storyName, int slot, string nodeID, int currentLineIndex, string previewText)
        {
            var data = new NovellaSaveData
            {
                CurrentLineIndex = currentLineIndex,
                CurrentNodeID    = nodeID ?? "",
                PreviewText      = previewText ?? "",
                Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                IntKeys    = NovellaVariables.IntVars.Keys.ToList(),
                IntValues  = NovellaVariables.IntVars.Values.ToList(),
                BoolKeys   = NovellaVariables.BoolVars.Keys.ToList(),
                BoolValues = NovellaVariables.BoolVars.Values.ToList(),
                StringKeys = NovellaVariables.StringVars.Keys.ToList(),
                StringValues = NovellaVariables.StringVars.Values.ToList(),
            };

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(SaveKey(storyName, slot, "Vars"), json);
            // NodeID отдельным ключом — для backward-compat с StoryLauncher
            // который читает его без декодирования всего slot blob.
            PlayerPrefs.SetString(SaveKey(storyName, slot, "Node"), nodeID ?? "");
            PlayerPrefs.Save();
        }

        public static NovellaSaveData LoadVariablesFromSlot(string storyName, int slot)
        {
            string json = PlayerPrefs.GetString(SaveKey(storyName, slot, "Vars"), "");
            if (string.IsNullOrEmpty(json)) return null;

            NovellaSaveData data = JsonUtility.FromJson<NovellaSaveData>(json);
            if (data == null) return null;

            // Применяем переменные глобально.
            NovellaVariables.IntVars.Clear();
            NovellaVariables.BoolVars.Clear();
            NovellaVariables.StringVars.Clear();
            for (int i = 0; i < data.IntKeys.Count; i++)    NovellaVariables.IntVars[data.IntKeys[i]]    = data.IntValues[i];
            for (int i = 0; i < data.BoolKeys.Count; i++)   NovellaVariables.BoolVars[data.BoolKeys[i]]  = data.BoolValues[i];
            for (int i = 0; i < data.StringKeys.Count; i++) NovellaVariables.StringVars[data.StringKeys[i]] = data.StringValues[i];

            return data;
        }

        public static bool HasSave(string storyName, int slot)
        {
            return !string.IsNullOrEmpty(PlayerPrefs.GetString(SaveKey(storyName, slot, "Vars"), ""));
        }

        public static void DeleteSlot(string storyName, int slot)
        {
            PlayerPrefs.DeleteKey(SaveKey(storyName, slot, "Vars"));
            PlayerPrefs.DeleteKey(SaveKey(storyName, slot, "Node"));
            PlayerPrefs.Save();
        }

        // Возвращает мета-инфу для одного слота (без применения переменных).
        public static NovellaSlotInfo GetSlotInfo(string storyName, int slot)
        {
            var info = new NovellaSlotInfo { Slot = slot, IsEmpty = true };
            string json = PlayerPrefs.GetString(SaveKey(storyName, slot, "Vars"), "");
            if (string.IsNullOrEmpty(json)) return info;

            NovellaSaveData data;
            try { data = JsonUtility.FromJson<NovellaSaveData>(json); }
            catch { return info; }
            if (data == null) return info;

            info.IsEmpty     = false;
            info.NodeID      = data.CurrentNodeID;
            info.PreviewText = data.PreviewText;
            info.Timestamp   = data.Timestamp;
            DateTime.TryParse(data.Timestamp, out info.TimeUtc);
            return info;
        }

        public static List<NovellaSlotInfo> GetAllSlots(string storyName)
        {
            var list = new List<NovellaSlotInfo>(MAX_SLOTS + 1);
            for (int s = 0; s <= MAX_SLOTS; s++) list.Add(GetSlotInfo(storyName, s));
            return list;
        }
    }
}
