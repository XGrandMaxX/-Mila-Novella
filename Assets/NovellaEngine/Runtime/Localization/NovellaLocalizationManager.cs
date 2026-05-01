// ════════════════════════════════════════════════════════════════════════════
// NovellaLocalizationManager
//
// Статический фасад для UI-локализации. Хранит ссылку на активную таблицу,
// текущий язык, выдаёт текст по ключу и оповещает компоненты NovellaLocalizedText
// о смене языка через событие OnLanguageChanged.
//
// В runtime язык подхватывается из PlayerPrefs (ключ "Novella_UILang").
// В editor — из EditorPrefs (через ToolLang.IsRU соответствие, но также можно
// явно назначить любой язык через SetLanguage).
// ════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace NovellaEngine.Data
{
    public static class NovellaLocalizationManager
    {
        private const string PREF_LANG = "Novella_UILang";
        private const string DEFAULT_LANG = "EN";

        private static NovellaUILocalizationTable _table;
        private static string _currentLanguage;

        public static event Action OnLanguageChanged;

        // ─── Active table ───────────────────────────────────────────────────────

        public static NovellaUILocalizationTable Table
        {
            get
            {
                if (_table == null) _table = LoadTableFromResources();
                return _table;
            }
            set { _table = value; }
        }

        public static void RegisterTable(NovellaUILocalizationTable table)
        {
            _table = table;
            OnLanguageChanged?.Invoke();
        }

        // ─── Current language ───────────────────────────────────────────────────

        public static string CurrentLanguage
        {
            get
            {
                if (string.IsNullOrEmpty(_currentLanguage)) _currentLanguage = LoadLanguage();
                return _currentLanguage;
            }
        }

        public static void SetLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return;
            if (_currentLanguage == lang) return;
            _currentLanguage = lang;
            try { PlayerPrefs.SetString(PREF_LANG, lang); PlayerPrefs.Save(); }
            catch { /* PlayerPrefs может быть недоступен в некоторых билд-режимах */ }
            OnLanguageChanged?.Invoke();
        }

        // ─── Lookup ─────────────────────────────────────────────────────────────

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var t = Table;
            if (t == null) return key;
            return t.Get(key, CurrentLanguage);
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private static string LoadLanguage()
        {
            try
            {
                var s = PlayerPrefs.GetString(PREF_LANG, "");
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return DEFAULT_LANG;
        }

        // Ищет таблицу в Resources/Localization/UILocalizationTable.asset.
        // Если таблицы нет — возвращает null (компоненты используют key as-is).
        private static NovellaUILocalizationTable LoadTableFromResources()
        {
            var t = Resources.Load<NovellaUILocalizationTable>("UILocalizationTable");
            if (t == null) t = Resources.Load<NovellaUILocalizationTable>("Localization/UILocalizationTable");
            return t;
        }
    }
}
