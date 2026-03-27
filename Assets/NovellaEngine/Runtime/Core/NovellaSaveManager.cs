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

            NovellaSaveData data = new()
            {
                CurrentLineIndex = currentLineIndex,

                IntKeys = NovellaVariables.IntVars.Keys.ToList(),
                IntValues = NovellaVariables.IntVars.Values.ToList(),

                BoolKeys = NovellaVariables.BoolVars.Keys.ToList(),
                BoolValues = NovellaVariables.BoolVars.Values.ToList(),

                StringKeys = NovellaVariables.StringVars.Keys.ToList(),
                StringValues = NovellaVariables.StringVars.Values.ToList()
            };

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

            NovellaVariables.IntVars.Clear();
            NovellaVariables.BoolVars.Clear();
            NovellaVariables.StringVars.Clear();

            for (int i = 0; i < data.IntKeys.Count; i++) NovellaVariables.IntVars[data.IntKeys[i]] = data.IntValues[i];
            for (int i = 0; i < data.BoolKeys.Count; i++) NovellaVariables.BoolVars[data.BoolKeys[i]] = data.BoolValues[i];
            for (int i = 0; i < data.StringKeys.Count; i++) NovellaVariables.StringVars[data.StringKeys[i]] = data.StringValues[i];

            return data;
        }
    }
}