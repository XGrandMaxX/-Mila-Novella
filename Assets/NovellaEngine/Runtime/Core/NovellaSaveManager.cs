using System;
using System.Collections.Generic;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    [Serializable]
    public class NovellaSaveData
    {
        public string SaveDate;
        public string ChapterName;
        public string CurrentNodeID;
        public int CurrentLineIndex;

        public List<string> IntKeys = new();
        public List<int> IntValues = new();

        public List<string> BoolKeys = new();
        public List<bool> BoolValues = new();

        public List<string> StringKeys = new();
        public List<string> StringValues = new();
    }

    public static class NovellaSaveManager
    {
        private const string SAVE_KEY_PREFIX = "NovellaSaveSlot_";

        public static void SaveGame(int slotIndex, NovellaTree currentTree, string currentNodeID, int currentLineIndex)
        {
            if (currentTree == null) return;

            NovellaSaveData data = new NovellaSaveData
            {
                SaveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ChapterName = currentTree.name,
                CurrentNodeID = currentNodeID,
                CurrentLineIndex = currentLineIndex
            };

            foreach (var kvp in NovellaPlayer.IntVars) { data.IntKeys.Add(kvp.Key); data.IntValues.Add(kvp.Value); }
            foreach (var kvp in NovellaPlayer.BoolVars) { data.BoolKeys.Add(kvp.Key); data.BoolValues.Add(kvp.Value); }
            foreach (var kvp in NovellaPlayer.StringVars) { data.StringKeys.Add(kvp.Key); data.StringValues.Add(kvp.Value); }

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(SAVE_KEY_PREFIX + slotIndex, json);
            PlayerPrefs.Save();

            Debug.Log($"[Novella Engine] Game saved in slot {slotIndex} at node {currentNodeID}");
        }

        public static bool HasSave(int slotIndex)
        {
            return PlayerPrefs.HasKey(SAVE_KEY_PREFIX + slotIndex);
        }

        public static NovellaSaveData GetSaveData(int slotIndex)
        {
            if (!HasSave(slotIndex)) return null;
            string json = PlayerPrefs.GetString(SAVE_KEY_PREFIX + slotIndex);
            return JsonUtility.FromJson<NovellaSaveData>(json);
        }

        public static void DeleteSave(int slotIndex)
        {
            if (HasSave(slotIndex))
            {
                PlayerPrefs.DeleteKey(SAVE_KEY_PREFIX + slotIndex);
                PlayerPrefs.Save();
            }
        }
    }
}