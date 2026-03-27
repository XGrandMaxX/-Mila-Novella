using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    /// <summary>
    /// Глобальный API для управления переменными Новеллы.
    /// Безопасно сохраняет данные, применяет лимиты и шифрует донатную валюту.
    /// </summary>
    public static class NovellaVariables
    {
        public static Dictionary<string, int> IntVars = new Dictionary<string, int>();
        public static Dictionary<string, bool> BoolVars = new Dictionary<string, bool>();
        public static Dictionary<string, string> StringVars = new Dictionary<string, string>();

        private const int SECURE_XOR_KEY = 777;

        #region ИНИЦИАЛИЗАЦИЯ И СИСТЕМНЫЕ МЕТОДЫ

        public static void Initialize()
        {
            IntVars.Clear(); BoolVars.Clear(); StringVars.Clear();
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null) return;

            foreach (var v in settings.Variables)
            {
                if (v.Scope == EVarScope.Global)
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = GetGlobalInt(v.Name, v.DefaultInt, v.IsPremiumCurrency);
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = PlayerPrefs.GetInt("NV_" + v.Name, v.DefaultBool ? 1 : 0) == 1;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = PlayerPrefs.GetString("NV_" + v.Name, v.DefaultString);
                }
                else
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = v.DefaultInt;
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = v.DefaultBool;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = v.DefaultString;
                }
            }
        }

        public static void ResetLocalVariables()
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null) return;

            foreach (var v in settings.Variables)
            {
                if (v.Scope == EVarScope.Local)
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = v.DefaultInt;
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = v.DefaultBool;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = v.DefaultString;
                }
            }
        }

        private static int GetGlobalInt(string key, int defaultValue, bool isPremium)
        {
            if (isPremium)
            {
                string secureKey = "NV_SEC_" + key;
                if (!PlayerPrefs.HasKey(secureKey)) return defaultValue;
                try
                {
                    string base64 = PlayerPrefs.GetString(secureKey);
                    byte[] bytes = Convert.FromBase64String(base64);
                    string xoredString = System.Text.Encoding.UTF8.GetString(bytes);
                    if (int.TryParse(xoredString, out int xoredValue)) return xoredValue ^ SECURE_XOR_KEY;
                }
                catch { return defaultValue; }
                return defaultValue;
            }
            else return PlayerPrefs.GetInt("NV_" + key, defaultValue);
        }

        private static VariableDefinition GetVariableDef(string key)
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            return settings?.Variables.FirstOrDefault(v => v.Name == key);
        }

        #endregion

        #region PUBLIC API (ДЛЯ ТВОИХ СКРИПТОВ И ИГРЫ)

        public static int GetInt(string key)
        {
            if (IntVars.TryGetValue(key, out int val)) return val;
            var def = GetVariableDef(key);
            return def != null ? def.DefaultInt : 0;
        }

        public static void SetInt(string key, int value)
        {
            var def = GetVariableDef(key);
            if (def != null && def.HasLimits)
            {
                value = Mathf.Clamp(value, def.MinValue, def.MaxValue);
            }

            IntVars[key] = value;

            if (def != null && def.Scope == EVarScope.Global)
            {
                if (def.IsPremiumCurrency)
                {
                    string xoredString = (value ^ SECURE_XOR_KEY).ToString();
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(xoredString);
                    string base64 = Convert.ToBase64String(bytes);
                    PlayerPrefs.SetString("NV_SEC_" + key, base64);
                }
                else
                {
                    PlayerPrefs.SetInt("NV_" + key, value);
                }
                PlayerPrefs.Save();
            }
        }

        public static bool GetBool(string key)
        {
            if (BoolVars.TryGetValue(key, out bool val)) return val;
            var def = GetVariableDef(key);
            return def != null ? def.DefaultBool : false;
        }

        public static void SetBool(string key, bool value)
        {
            BoolVars[key] = value;
            var def = GetVariableDef(key);
            if (def != null && def.Scope == EVarScope.Global)
            {
                PlayerPrefs.SetInt("NV_" + key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static string GetString(string key)
        {
            if (StringVars.TryGetValue(key, out string val)) return val;
            var def = GetVariableDef(key);
            return def != null ? def.DefaultString : "";
        }

        public static void SetString(string key, string value)
        {
            StringVars[key] = value;
            var def = GetVariableDef(key);
            if (def != null && def.Scope == EVarScope.Global)
            {
                PlayerPrefs.SetString("NV_" + key, value);
                PlayerPrefs.Save();
            }
        }

        #endregion
    }
}