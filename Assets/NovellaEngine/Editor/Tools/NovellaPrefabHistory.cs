// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabHistory — append-only журнал создания/сохранения префабов
// в Gallery/Prefabs. JSON в той же папке. Очистить через UI нельзя
// (по дизайн-требованию).
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public static class NovellaPrefabHistory
    {
        // Папка с префабами и сам файл истории.
        public const string PREFABS_DIR  = "Assets/NovellaEngine/Gallery/Prefabs";
        public const string HISTORY_FILE = "Assets/NovellaEngine/Gallery/Prefabs/__history.json";

        [Serializable]
        public class Entry
        {
            public string  Action;     // "create" | "save" | "rename" | "delete"
            public string  PrefabName;
            public string  PrefabType; // Button / Panel / Image / Text
            public string  Path;       // относительный путь к prefab-ассету
            public string  Timestamp;  // ISO-8601 в локальной таймзоне
        }

        [Serializable]
        private class HistoryFile
        {
            public List<Entry> Entries = new List<Entry>();
        }

        // Append-запись. Никогда не удаляет старое.
        public static void Log(string action, string prefabName, string prefabType, string path)
        {
            EnsureFolder();
            var hist = LoadInternal();
            hist.Entries.Add(new Entry
            {
                Action     = action ?? "",
                PrefabName = prefabName ?? "",
                PrefabType = prefabType ?? "",
                Path       = path ?? "",
                Timestamp  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
            SaveInternal(hist);
        }

        // Только чтение — для отображения в UI.
        public static List<Entry> ReadAll()
        {
            return LoadInternal().Entries;
        }

        private static HistoryFile LoadInternal()
        {
            if (!File.Exists(HISTORY_FILE)) return new HistoryFile();
            try
            {
                string json = File.ReadAllText(HISTORY_FILE);
                if (string.IsNullOrEmpty(json)) return new HistoryFile();
                var hist = JsonUtility.FromJson<HistoryFile>(json);
                return hist ?? new HistoryFile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NovellaPrefabHistory] Failed to read history file: {ex.Message}. Will create a fresh file.");
                return new HistoryFile();
            }
        }

        private static void SaveInternal(HistoryFile hist)
        {
            try
            {
                string json = JsonUtility.ToJson(hist, true);
                File.WriteAllText(HISTORY_FILE, json);
                AssetDatabase.ImportAsset(HISTORY_FILE);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NovellaPrefabHistory] Failed to write history file: {ex.Message}");
            }
        }

        private static void EnsureFolder()
        {
            // Рекурсивно создаём папки если их нет.
            string[] segments = PREFABS_DIR.Split('/');
            string acc = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = acc + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(acc, segments[i]);
                }
                acc = next;
            }
        }
    }
}
