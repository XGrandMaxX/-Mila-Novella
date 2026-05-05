/// <summary>
/// ������� ScriptableObject ����� �������.
/// ������ ���������� ����������� ������������ [SerializeReference] ��� ��������� ����� ��������� ��� (DLC).
/// </summary>
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    [CreateAssetMenu(fileName = "NewNovellaTree", menuName = "Novella Engine/Novella Tree")]
    public class NovellaTree : ScriptableObject
    {
        public string RootNodeID;
        public Vector2 StartPosition = new Vector2(50, 200);
        public bool EnableAutoSave = false;
        public float AutoSaveInterval = 30f;

        [SerializeReference]
        public List<NovellaNodeBase> Nodes = new List<NovellaNodeBase>();

        [SerializeReference]
        public List<NovellaGroupData> Groups = new List<NovellaGroupData>();

        // Считает слова во ВСЕХ переводах глав: диалоги (по строкам),
        // выборы (Branch/Condition/Random), текст заметок. Берётся максимум
        // среди языков — это лучше отражает реальный объём в любом переводе.
        // Возвращает (words, readingMinutes). Скорость — 200 слов/мин (средний
        // взрослый темп чтения вслух).
        public (int words, double readingMinutes) GetWordStats()
        {
            int maxWords = 0;
            // Собираем все языки которые встречаются в LocalizedString.
            var langs = new HashSet<string>();
            void Collect(LocalizedString ls)
            {
                if (ls == null || ls.Translations == null) return;
                foreach (var t in ls.Translations)
                    if (t != null && !string.IsNullOrEmpty(t.LanguageID)) langs.Add(t.LanguageID);
            }
            foreach (var n in Nodes)
            {
                if (n is DialogueNodeData d)
                    foreach (var line in d.DialogueLines) Collect(line.LocalizedPhrase);
                else if (n is BranchNodeData b)
                    foreach (var c in b.Choices) Collect(c.LocalizedText);
                else if (n is ConditionNodeData cn)
                    foreach (var c in cn.Choices) Collect(c.LocalizedText);
                else if (n is RandomNodeData r)
                    foreach (var c in r.Choices) Collect(c.LocalizedText);
                else if (n is NoteNodeData nt)
                    Collect(nt.LocalizedNoteText);
            }
            if (langs.Count == 0) return (0, 0);

            foreach (var lang in langs)
            {
                int cur = 0;
                foreach (var n in Nodes)
                {
                    if (n is DialogueNodeData d)
                        foreach (var line in d.DialogueLines)
                            cur += CountWords(line.LocalizedPhrase != null ? line.LocalizedPhrase.GetText(lang) : "");
                    else if (n is BranchNodeData b)
                        foreach (var c in b.Choices)
                            cur += CountWords(c.LocalizedText != null ? c.LocalizedText.GetText(lang) : "");
                    else if (n is ConditionNodeData cn)
                        foreach (var c in cn.Choices)
                            cur += CountWords(c.LocalizedText != null ? c.LocalizedText.GetText(lang) : "");
                    else if (n is RandomNodeData r)
                        foreach (var c in r.Choices)
                            cur += CountWords(c.LocalizedText != null ? c.LocalizedText.GetText(lang) : "");
                    else if (n is NoteNodeData nt)
                        cur += CountWords(nt.LocalizedNoteText != null ? nt.LocalizedNoteText.GetText(lang) : "");
                }
                if (cur > maxWords) maxWords = cur;
            }
            const double WORDS_PER_MIN = 200.0;
            return (maxWords, maxWords / WORDS_PER_MIN);
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            // Whitespace-разделитель работает для большинства языков;
            // CJK (китайский/японский) — слово ≈ символ, считаем грубо как
            // длину в символах после удаления пробелов / 1.5.
            int cjk = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 0x4E00 && c <= 0x9FFF) cjk++;             // CJK Unified
                else if (c >= 0x3040 && c <= 0x30FF) cjk++;         // Hiragana/Katakana
            }
            if (cjk > text.Length / 3)
            {
                int total = 0;
                for (int i = 0; i < text.Length; i++) if (!char.IsWhiteSpace(text[i])) total++;
                return Mathf.Max(1, Mathf.RoundToInt(total / 1.5f));
            }
            int count = 0;
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                bool ws = char.IsWhiteSpace(text[i]);
                if (!ws && !inWord) { count++; inWord = true; }
                else if (ws) inWord = false;
            }
            return count;
        }
    }
}