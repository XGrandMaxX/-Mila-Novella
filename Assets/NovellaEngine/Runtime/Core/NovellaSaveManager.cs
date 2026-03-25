using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace NovellaEngine.Runtime
{
    [System.Serializable]
    public class NovellaSaveData
    {
        public int CurrentLineIndex = 0;

        public List<string> IntKeys = new List<string>();
        public List<int> IntValues = new List<int>();
        public List<string> BoolKeys = new List<string>();
        public List<bool> BoolValues = new List<bool>();
        public List<string> StringKeys = new List<string>();
        public List<string> StringValues = new List<string>();
    }

    public static class NovellaSaveManager
    {
        public static void SaveGame(string storyName, string nodeID, int currentLineIndex)
        {
            PlayerPrefs.SetString($"NovellaSave_{storyName}_Node", nodeID);

            NovellaSaveData data = new NovellaSaveData();
            data.CurrentLineIndex = currentLineIndex;

            data.IntKeys = NovellaPlayer.IntVars.Keys.ToList();
            data.IntValues = NovellaPlayer.IntVars.Values.ToList();

            data.BoolKeys = NovellaPlayer.BoolVars.Keys.ToList();
            data.BoolValues = NovellaPlayer.BoolVars.Values.ToList();

            data.StringKeys = NovellaPlayer.StringVars.Keys.ToList();
            data.StringValues = NovellaPlayer.StringVars.Values.ToList();

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString($"NovellaSave_{storyName}_Vars", json);
            PlayerPrefs.Save();
        }

        public static NovellaSaveData LoadVariables(string storyName)
        {
            string json = PlayerPrefs.GetString($"NovellaSave_{storyName}_Vars", "");
            if (string.IsNullOrEmpty(json)) return null;

            NovellaSaveData data = JsonUtility.FromJson<NovellaSaveData>(json);
            if (data == null) return null;

            NovellaPlayer.IntVars.Clear();
            NovellaPlayer.BoolVars.Clear();
            NovellaPlayer.StringVars.Clear();

            for (int i = 0; i < data.IntKeys.Count; i++) NovellaPlayer.IntVars[data.IntKeys[i]] = data.IntValues[i];
            for (int i = 0; i < data.BoolKeys.Count; i++) NovellaPlayer.BoolVars[data.BoolKeys[i]] = data.BoolValues[i];
            for (int i = 0; i < data.StringKeys.Count; i++) NovellaPlayer.StringVars[data.StringKeys[i]] = data.StringValues[i];

            return data;
        }
    }
}