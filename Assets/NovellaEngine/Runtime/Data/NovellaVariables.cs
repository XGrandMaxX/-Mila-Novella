using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime
{
    /// <summary>
    /// Runtime API для переменных проекта. Поддерживает Integer / Boolean /
    /// String / Float / Choice / List. Local — живёт в памяти на главу,
    /// Global — пишется в PlayerPrefs.
    /// </summary>
    public static class NovellaVariables
    {
        public static Dictionary<string, int> IntVars = new Dictionary<string, int>();
        public static Dictionary<string, bool> BoolVars = new Dictionary<string, bool>();
        public static Dictionary<string, string> StringVars = new Dictionary<string, string>();
        public static Dictionary<string, float> FloatVars = new Dictionary<string, float>();
        public static Dictionary<string, string> ChoiceVars = new Dictionary<string, string>();
        public static Dictionary<string, List<string>> ListVars = new Dictionary<string, List<string>>();

        private const int SECURE_XOR_KEY = 777;
        // Разделитель для сериализации List в одну строку PlayerPrefs.
        // ASCII Record Separator (U+001E) — управляющий символ, который никогда
        // не встретится в обычной пользовательской строке.
        private const string LIST_SEP = "";

        #region Инициализация и сброс

        public static void Initialize()
        {
            IntVars.Clear(); BoolVars.Clear(); StringVars.Clear();
            FloatVars.Clear(); ChoiceVars.Clear(); ListVars.Clear();

            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null) return;

            foreach (var v in settings.Variables)
            {
                if (v.Scope == EVarScope.Global)
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = GetGlobalInt(v.Name, v.DefaultInt, v.IsPremiumCurrency);
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = PlayerPrefs.GetInt("NV_" + v.Name, v.DefaultBool ? 1 : 0) == 1;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = PlayerPrefs.GetString("NV_" + v.Name, v.DefaultString);
                    else if (v.Type == EVarType.Float) FloatVars[v.Name] = PlayerPrefs.GetFloat("NV_" + v.Name, v.DefaultFloat);
                    else if (v.Type == EVarType.Choice) ChoiceVars[v.Name] = PlayerPrefs.GetString("NV_" + v.Name, v.DefaultChoice ?? "");
                    else if (v.Type == EVarType.List)
                    {
                        // Если ключа нет — стартовый список из дефолтов.
                        if (PlayerPrefs.HasKey("NV_" + v.Name))
                        {
                            string raw = PlayerPrefs.GetString("NV_" + v.Name, "");
                            ListVars[v.Name] = DeserializeList(raw);
                        }
                        else
                        {
                            ListVars[v.Name] = new List<string>(v.DefaultList ?? new List<string>());
                        }
                    }
                }
                else
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = v.DefaultInt;
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = v.DefaultBool;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = v.DefaultString;
                    else if (v.Type == EVarType.Float) FloatVars[v.Name] = v.DefaultFloat;
                    else if (v.Type == EVarType.Choice) ChoiceVars[v.Name] = v.DefaultChoice ?? "";
                    else if (v.Type == EVarType.List) ListVars[v.Name] = new List<string>(v.DefaultList ?? new List<string>());
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
                    else if (v.Type == EVarType.Float) FloatVars[v.Name] = v.DefaultFloat;
                    else if (v.Type == EVarType.Choice) ChoiceVars[v.Name] = v.DefaultChoice ?? "";
                    else if (v.Type == EVarType.List) ListVars[v.Name] = new List<string>(v.DefaultList ?? new List<string>());
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

        private static string SerializeList(List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(LIST_SEP, list);
        }

        private static List<string> DeserializeList(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new List<string>();
            return new List<string>(raw.Split(new[] { LIST_SEP }, StringSplitOptions.None));
        }

        #endregion

        #region PUBLIC API (для использования в коде)

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

        // ─── Float ───
        public static float GetFloat(string key)
        {
            if (FloatVars.TryGetValue(key, out float val)) return val;
            var def = GetVariableDef(key);
            return def != null ? def.DefaultFloat : 0f;
        }

        public static void SetFloat(string key, float value)
        {
            var def = GetVariableDef(key);
            if (def != null && def.HasLimits)
            {
                value = Mathf.Clamp(value, def.MinFloat, def.MaxFloat);
            }
            FloatVars[key] = value;
            if (def != null && def.Scope == EVarScope.Global)
            {
                PlayerPrefs.SetFloat("NV_" + key, value);
                PlayerPrefs.Save();
            }
        }

        // ─── Choice (одно значение из списка) ───
        public static string GetChoice(string key)
        {
            if (ChoiceVars.TryGetValue(key, out string val)) return val;
            var def = GetVariableDef(key);
            return def != null ? (def.DefaultChoice ?? "") : "";
        }

        public static void SetChoice(string key, string value)
        {
            var def = GetVariableDef(key);
            // Защищаем от записи мусора: если список Choices непустой и значение
            // не из него — игнорируем (рантайм не должен ломать историю).
            if (def != null && def.Choices != null && def.Choices.Count > 0
                && !def.Choices.Contains(value))
            {
                Debug.LogWarning($"[NovellaVariables] Choice value «{value}» is not in allowed list of «{key}» — ignoring SetChoice.");
                return;
            }
            ChoiceVars[key] = value;
            if (def != null && def.Scope == EVarScope.Global)
            {
                PlayerPrefs.SetString("NV_" + key, value ?? "");
                PlayerPrefs.Save();
            }
        }

        // ─── List ───
        public static List<string> GetList(string key)
        {
            if (ListVars.TryGetValue(key, out var val)) return val;
            var def = GetVariableDef(key);
            var copy = new List<string>(def?.DefaultList ?? new List<string>());
            ListVars[key] = copy;
            return copy;
        }

        public static void ListAdd(string key, string item)
        {
            var list = GetList(key);
            list.Add(item ?? "");
            SaveListIfGlobal(key, list);
        }

        public static void ListRemove(string key, string item)
        {
            var list = GetList(key);
            list.Remove(item ?? "");
            SaveListIfGlobal(key, list);
        }

        public static void ListClear(string key)
        {
            var list = GetList(key);
            list.Clear();
            SaveListIfGlobal(key, list);
        }

        public static bool ListContains(string key, string item)
        {
            var list = GetList(key);
            return list.Contains(item ?? "");
        }

        public static int ListCount(string key)
        {
            return GetList(key).Count;
        }

        private static void SaveListIfGlobal(string key, List<string> list)
        {
            var def = GetVariableDef(key);
            if (def != null && def.Scope == EVarScope.Global)
            {
                PlayerPrefs.SetString("NV_" + key, SerializeList(list));
                PlayerPrefs.Save();
            }
        }

        #endregion
    }
}
