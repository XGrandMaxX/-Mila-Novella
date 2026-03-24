using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Data
{
    /// <summary>
    /// ћаленький помощник дл€ перевода самого интерфейса редактора
    /// </summary>
    public static class ToolLang
    {
        public static bool IsRU => EditorPrefs.GetBool("NovellaGraph_IsRU", true);

        public static void Toggle() => EditorPrefs.SetBool("NovellaGraph_IsRU", !IsRU);

        public static string Get(string en, string ru) => IsRU ? ru : en;
    }

    [Serializable]
    public class TranslationEntry
    {
        public string LanguageID;
        public string Text;
    }

    [Serializable]
    public class LocalizedString
    {
        public List<TranslationEntry> Translations = new List<TranslationEntry>();

        public string GetText(string langID)
        {
            var entry = Translations.FirstOrDefault(t => t.LanguageID == langID);
            return entry != null ? entry.Text : "";
        }

        public void SetText(string langID, string text)
        {
            var entry = Translations.FirstOrDefault(t => t.LanguageID == langID);
            if (entry != null) entry.Text = text;
            else Translations.Add(new TranslationEntry { LanguageID = langID, Text = text });
        }
    }

    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Novella Engine/Localization Settings")]
    public class NovellaLocalizationSettings : ScriptableObject
    {
        [Header("Available Languages")]
        public List<string> Languages = new List<string> { "EN", "RU", "ES", "FR", "DE", "ZH", "JA" };

#if UNITY_EDITOR
        public static NovellaLocalizationSettings GetOrCreateSettings()
        {
            var guids = AssetDatabase.FindAssets("t:NovellaLocalizationSettings");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<NovellaLocalizationSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var settings = ScriptableObject.CreateInstance<NovellaLocalizationSettings>();

            string folderPath = "Assets/NovellaEngine/Runtime/Data";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                Directory.CreateDirectory(Application.dataPath + "/NovellaEngine/Runtime/Data");
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(settings, folderPath + "/LocalizationSettings.asset");
            AssetDatabase.SaveAssets();
            return settings;
        }
#endif
    }

#if UNITY_EDITOR
    public static class NovellaCSVUtility
    {
        public static void ExportCSV(NovellaTree tree, List<string> langs)
        {
            string path = EditorUtility.SaveFilePanel("Export Localization CSV", "", $"{tree.name}_Localization", "csv");
            if (string.IsNullOrEmpty(path)) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NodeID,FieldType," + string.Join(",", langs));

            foreach (var node in tree.Nodes)
            {
                // Ёкспорт реплик диалога
                if (node is DialogueNodeData dialData)
                {
                    for (int i = 0; i < dialData.DialogueLines.Count; i++)
                    {
                        sb.AppendLine(CreateCSVRow(node.NodeID, $"Line_{i}", dialData.DialogueLines[i].LocalizedPhrase, langs));
                    }
                }
                // Ёкспорт вариантов выбора ветвлени€
                else if (node is BranchNodeData branchData)
                {
                    for (int i = 0; i < branchData.Choices.Count; i++)
                    {
                        sb.AppendLine(CreateCSVRow(node.NodeID, $"Choice_{i}", branchData.Choices[i].LocalizedText, langs));
                    }
                }
                // Ёкспорт вариантов (»стина/Ћожь) услови€
                else if (node is ConditionNodeData condData)
                {
                    for (int i = 0; i < condData.Choices.Count; i++)
                    {
                        sb.AppendLine(CreateCSVRow(node.NodeID, $"Choice_{i}", condData.Choices[i].LocalizedText, langs));
                    }
                }
                // Ёкспорт вариантов рандома
                else if (node is RandomNodeData rndData)
                {
                    for (int i = 0; i < rndData.Choices.Count; i++)
                    {
                        sb.AppendLine(CreateCSVRow(node.NodeID, $"Choice_{i}", rndData.Choices[i].LocalizedText, langs));
                    }
                }
                // Ёкспорт текста заметок
                else if (node is NoteNodeData noteData)
                {
                    sb.AppendLine(CreateCSVRow(node.NodeID, "NoteText", noteData.LocalizedNoteText, langs));
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Novella Engine] Exported CSV to: {path}");
        }

        public static void ImportCSV(NovellaTree tree, List<string> langs)
        {
            string path = EditorUtility.OpenFilePanel("Import Localization CSV", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            string[] lines = File.ReadAllLines(path);
            if (lines.Length <= 1) return;

            string[] headers = lines[0].Split(',');

            Undo.RecordObject(tree, "Import CSV Localization");

            for (int i = 1; i < lines.Length; i++)
            {
                string[] row = ParseCSVLine(lines[i]);
                if (row.Length < 3) continue;

                string nodeID = row[0];
                string fieldType = row[1];
                var node = tree.Nodes.FirstOrDefault(n => n.NodeID == nodeID);
                if (node == null) continue;

                LocalizedString targetString = null;

                if (node is DialogueNodeData dialData)
                {
                    if (fieldType.StartsWith("Line_"))
                    {
                        if (int.TryParse(fieldType.Split('_')[1], out int lineIdx) && lineIdx < dialData.DialogueLines.Count)
                            targetString = dialData.DialogueLines[lineIdx].LocalizedPhrase;
                    }
                    else if (fieldType == "Phrase") // ƒл€ совместимости со старыми файлами экспорта
                    {
                        if (dialData.DialogueLines.Count > 0) targetString = dialData.DialogueLines[0].LocalizedPhrase;
                    }
                }
                else if (node is BranchNodeData bnd && fieldType.StartsWith("Choice_"))
                {
                    if (int.TryParse(fieldType.Split('_')[1], out int choiceIdx) && choiceIdx < bnd.Choices.Count)
                        targetString = bnd.Choices[choiceIdx].LocalizedText;
                }
                else if (node is ConditionNodeData cnd && fieldType.StartsWith("Choice_"))
                {
                    if (int.TryParse(fieldType.Split('_')[1], out int choiceIdx) && choiceIdx < cnd.Choices.Count)
                        targetString = cnd.Choices[choiceIdx].LocalizedText;
                }
                else if (node is RandomNodeData rnd && fieldType.StartsWith("Choice_"))
                {
                    if (int.TryParse(fieldType.Split('_')[1], out int choiceIdx) && choiceIdx < rnd.Choices.Count)
                        targetString = rnd.Choices[choiceIdx].LocalizedText;
                }
                else if (node is NoteNodeData noteD && fieldType == "NoteText")
                {
                    targetString = noteD.LocalizedNoteText;
                }

                if (targetString != null)
                {
                    for (int h = 2; h < headers.Length; h++)
                    {
                        if (h < row.Length) targetString.SetText(headers[h], row[h]);
                    }
                }
            }

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            Debug.Log("[Novella Engine] Successfully Imported CSV!");
        }

        private static string CreateCSVRow(string id, string fieldType, LocalizedString locString, List<string> langs)
        {
            List<string> row = new List<string> { id, fieldType };
            foreach (var lang in langs)
                row.Add(EscapeCSV(locString.GetText(lang)));
            return string.Join(",", row);
        }

        private static string EscapeCSV(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            if (str.Contains(",") || str.Contains("\"") || str.Contains("\n"))
                return "\"" + str.Replace("\"", "\"\"") + "\"";
            return str;
        }

        private static string[] ParseCSVLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            StringBuilder currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"') { currentField.Append('\"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString()); currentField.Clear();
                }
                else currentField.Append(c);
            }
            result.Add(currentField.ToString());
            return result.ToArray();
        }
    }
#endif
}