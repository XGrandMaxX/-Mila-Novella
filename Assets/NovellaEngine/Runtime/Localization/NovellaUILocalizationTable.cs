// ════════════════════════════════════════════════════════════════════════════
// NovellaUILocalizationTable
//
// Глобальная таблица локализации UI: ключ → словарь {язык → строка}.
// Используется компонентом NovellaLocalizedText, чтобы UI-элементы (кнопки,
// заголовки, лейблы) автоматически подтягивали текст по текущему языку.
//
// Существующий LocalizedString из NovellaLocalizationSettings используется
// для текста узлов графа (диалоги/выборы) — UI-таблица намеренно отдельна,
// потому что у неё другой паттерн использования: один общий словарь для всего
// проекта, а не per-line storage в каждом узле.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NovellaEngine.Data
{
    [CreateAssetMenu(fileName = "UILocalizationTable", menuName = "Novella Engine/UI Localization Table")]
    public class NovellaUILocalizationTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string Key;
            // Категория для группировки и фильтрации (например «menu», «dialog», «hud»).
            // Пользователь задаёт сам в редакторе локализации; используется picker'ом
            // чтобы быстро находить нужные ключи в больших проектах.
            public string Category = "";
            public List<TranslationEntry> Values = new List<TranslationEntry>();

            public string Get(string lang)
            {
                for (int i = 0; i < Values.Count; i++)
                {
                    if (Values[i] != null && Values[i].LanguageID == lang) return Values[i].Text;
                }
                return null;
            }

            public void Set(string lang, string text)
            {
                for (int i = 0; i < Values.Count; i++)
                {
                    if (Values[i] != null && Values[i].LanguageID == lang)
                    {
                        Values[i].Text = text;
                        return;
                    }
                }
                Values.Add(new TranslationEntry { LanguageID = lang, Text = text });
            }
        }

        [Tooltip("Default language used as fallback when current language has no translation.")]
        public string DefaultLanguage = "EN";

        [Tooltip("All UI translation keys.")]
        public List<Entry> Entries = new List<Entry>();

        [NonSerialized] private Dictionary<string, Entry> _cache;

        // ─── Lookup ─────────────────────────────────────────────────────────────

        public string Get(string key, string lang)
        {
            if (string.IsNullOrEmpty(key)) return "";
            BuildCache();
            if (!_cache.TryGetValue(key, out var entry)) return key;
            string text = entry.Get(lang);
            if (string.IsNullOrEmpty(text) && lang != DefaultLanguage) text = entry.Get(DefaultLanguage);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            BuildCache();
            return _cache.ContainsKey(key);
        }

        public Entry FindEntry(string key)
        {
            BuildCache();
            return _cache.TryGetValue(key, out var e) ? e : null;
        }

        public IEnumerable<string> AllKeys()
        {
            BuildCache();
            return _cache.Keys;
        }

        // ─── Mutation ───────────────────────────────────────────────────────────

        public Entry AddKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            BuildCache();
            if (_cache.TryGetValue(key, out var existing)) return existing;
            var e = new Entry { Key = key };
            Entries.Add(e);
            _cache = null;
            return e;
        }

        public bool RemoveKey(string key)
        {
            int idx = Entries.FindIndex(e => e.Key == key);
            if (idx < 0) return false;
            Entries.RemoveAt(idx);
            _cache = null;
            return true;
        }

        public bool RenameKey(string oldKey, string newKey)
        {
            if (string.IsNullOrEmpty(newKey) || oldKey == newKey) return false;
            BuildCache();
            if (!_cache.TryGetValue(oldKey, out var entry)) return false;
            if (_cache.ContainsKey(newKey)) return false; // конфликт
            entry.Key = newKey;
            _cache = null;
            return true;
        }

        public void SetValue(string key, string lang, string text)
        {
            var entry = AddKey(key);
            if (entry == null) return;
            entry.Set(lang, text);
        }

        public void InvalidateCache() { _cache = null; }

        private void BuildCache()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, Entry>();
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                if (!_cache.ContainsKey(e.Key)) _cache[e.Key] = e;
            }
        }

        // ─── JSON import / export ───────────────────────────────────────────────
        // Формат человекочитаемый:
        // {
        //   "default": "EN",
        //   "entries": {
        //     "btn_play": { "EN": "Play", "RU": "Играть" },
        //     "title_main": { "EN": "Main Menu", "RU": "Главное меню" }
        //   }
        // }

        public string ExportJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"default\": \"{Escape(DefaultLanguage)}\",");
            sb.AppendLine("  \"entries\": {");

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                sb.Append($"    \"{Escape(e.Key)}\": {{");
                for (int v = 0; v < e.Values.Count; v++)
                {
                    if (v > 0) sb.Append(", ");
                    sb.Append($"\"{Escape(e.Values[v].LanguageID)}\": \"{Escape(e.Values[v].Text)}\"");
                }
                sb.Append("}");
                if (i < Entries.Count - 1) sb.AppendLine(","); else sb.AppendLine();
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ─── CSV import / export ────────────────────────────────────────────────
        // Формат: первая строка-заголовок «Key,Category,LANG1,LANG2,...».
        // Каждая последующая строка — один ключ. Пустые ячейки = «нет перевода».
        // Подходит для редактирования в Excel / Google Sheets и обмена с
        // внешними переводчиками. Языки в шапке должны совпадать с теми,
        // которые есть в LocalizationSettings — но импортёр толерантный, добавит
        // отсутствующие языки в таблицу как новые TranslationEntry.

        public string ExportCsv(List<string> langs)
        {
            if (langs == null || langs.Count == 0)
                langs = new List<string> { DefaultLanguage };

            var sb = new StringBuilder();
            sb.Append("Key,Category");
            foreach (var l in langs) { sb.Append(','); sb.Append(EscapeCsv(l)); }
            sb.Append('\n');

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                sb.Append(EscapeCsv(e.Key));
                sb.Append(',');
                sb.Append(EscapeCsv(e.Category ?? ""));
                foreach (var l in langs)
                {
                    sb.Append(',');
                    sb.Append(EscapeCsv(e.Get(l) ?? ""));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public bool ImportCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return false;
            try
            {
                var rows = ParseCsv(csv);
                if (rows.Count < 2) return false;

                var headers = rows[0];
                if (headers.Count < 3) return false;
                if (headers[0].Trim() != "Key" || headers[1].Trim() != "Category") return false;

                var langs = new List<string>();
                for (int i = 2; i < headers.Count; i++) langs.Add(headers[i].Trim());

                Entries.Clear();
                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0])) continue;
                    var entry = new Entry { Key = row[0], Category = row.Count > 1 ? row[1] : "" };
                    for (int c = 0; c < langs.Count; c++)
                    {
                        int colIdx = c + 2;
                        string val = colIdx < row.Count ? row[colIdx] : "";
                        if (!string.IsNullOrEmpty(val))
                            entry.Values.Add(new TranslationEntry { LanguageID = langs[c], Text = val });
                    }
                    Entries.Add(entry);
                }
                _cache = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NovellaUILocalization] CSV import failed: {ex.Message}");
                return false;
            }
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            bool needs = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needs) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static List<List<string>> ParseCsv(string csv)
        {
            var rows = new List<List<string>>();
            var current = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else
                {
                    if (c == '"') { inQuotes = true; }
                    else if (c == ',') { current.Add(field.ToString()); field.Clear(); }
                    else if (c == '\n' || c == '\r')
                    {
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                        current.Add(field.ToString()); field.Clear();
                        if (current.Count > 0 && !(current.Count == 1 && string.IsNullOrEmpty(current[0])))
                            rows.Add(current);
                        current = new List<string>();
                    }
                    else field.Append(c);
                }
            }
            if (field.Length > 0 || current.Count > 0)
            {
                current.Add(field.ToString());
                if (current.Count > 0 && !(current.Count == 1 && string.IsNullOrEmpty(current[0])))
                    rows.Add(current);
            }
            return rows;
        }

        public bool ImportJson(string json)
        {
            try
            {
                var parsed = MiniJson.Parse(json) as Dictionary<string, object>;
                if (parsed == null) return false;

                if (parsed.TryGetValue("default", out var def) && def is string defStr) DefaultLanguage = defStr;

                if (parsed.TryGetValue("entries", out var entriesObj) && entriesObj is Dictionary<string, object> entriesDict)
                {
                    Entries.Clear();
                    foreach (var kv in entriesDict)
                    {
                        var entry = new Entry { Key = kv.Key };
                        if (kv.Value is Dictionary<string, object> langs)
                        {
                            foreach (var lkv in langs)
                            {
                                entry.Values.Add(new TranslationEntry
                                {
                                    LanguageID = lkv.Key,
                                    Text = lkv.Value?.ToString() ?? ""
                                });
                            }
                        }
                        Entries.Add(entry);
                    }
                    _cache = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NovellaUILocalization] JSON import failed: {ex.Message}");
            }
            return false;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // MiniJson — минимальный JSON-парсер (без внешних зависимостей).
    // Достаточен для нашего формата (object/string/number/bool/null/array).
    // Адаптация публичного домена (https://gist.github.com/darktable/1411710),
    // переписана для чистоты и совместимости с нашим code-style.
    // ════════════════════════════════════════════════════════════════════════
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            return ParseValue(json, ref i);
        }

        private static void SkipWS(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object ParseValue(string s, ref int i)
        {
            SkipWS(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' || c == 'f') return ParseBool(s, ref i);
            if (c == 'n') { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // {
            SkipWS(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (i < s.Length)
            {
                SkipWS(s, ref i);
                string key = ParseString(s, ref i);
                SkipWS(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                var val = ParseValue(s, ref i);
                d[key] = val;
                SkipWS(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
            }
            return d;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++; // [
            SkipWS(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (i < s.Length)
            {
                l.Add(ParseValue(s, ref i));
                SkipWS(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
            }
            return l;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return "";
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length && int.TryParse(s.Substring(i, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool ParseBool(string s, ref int i)
        {
            if (i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
            return false;
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string num = s.Substring(start, i - start);
            double.TryParse(num, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d);
            return d;
        }
    }
}
