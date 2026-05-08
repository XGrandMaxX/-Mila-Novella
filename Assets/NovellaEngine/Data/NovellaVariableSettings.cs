using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NovellaEngine.Data
{
    // ВАЖНО: порядок сериализуется. Только добавляй новые в КОНЕЦ, иначе
    // существующие ассеты сломаются (старые int-ы попадут в чужой тип).
    public enum EVarType { Integer, Boolean, String, Float, Choice, List }

    public enum EVarScope
    {
        Local,
        Global
    }

    [System.Serializable]
    public class VariableDefinition
    {
        public string Name = "NEW_VARIABLE";
        public string Category = "Default";
        public string Description = "";

        public EVarType Type = EVarType.Integer;
        public EVarScope Scope = EVarScope.Local;

        // Limits применимы к Integer / Float (Min/MaxValue для int,
        // Min/MaxFloat для float).
        public bool HasLimits = false;
        public int MinValue = 0;
        public int MaxValue = 100;
        public float MinFloat = 0f;
        public float MaxFloat = 100f;

        public int DefaultInt = 0;
        public bool DefaultBool = false;
        public string DefaultString = "";
        public float DefaultFloat = 0f;

        // Choice: список допустимых значений + дефолт (один из них).
        public List<string> Choices = new List<string>();
        public string DefaultChoice = "";

        // List: стартовое содержимое списка (строки).
        public List<string> DefaultList = new List<string>();

        public bool IsPremiumCurrency = false;
        public Sprite Icon;
    }

    public class NovellaVariableSettings : ScriptableObject
    {
        public List<VariableDefinition> Variables = new List<VariableDefinition>();

        // Список кастомных категорий проекта (до 5 штук). Появляются как
        // дополнительные chip-пресеты рядом с 6 встроенными. Юзер может удалять
        // через ✕ на чипе. Используется проектом целиком, а не на переменную.
        public List<string> CustomCategories = new List<string>();
        public const int MAX_CUSTOM_CATEGORIES = 5;

        private static NovellaVariableSettings _instance;
        public static NovellaVariableSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");

#if UNITY_EDITOR
                    if (_instance == null)
                    {
                        _instance = CreateInstance<NovellaVariableSettings>();

                        if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine"))
                            AssetDatabase.CreateFolder("Assets", "NovellaEngine");

                        if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Resources"))
                            AssetDatabase.CreateFolder("Assets/NovellaEngine", "Resources");

                        if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Resources/NovellaEngine"))
                            AssetDatabase.CreateFolder("Assets/NovellaEngine/Resources", "NovellaEngine");

                        AssetDatabase.CreateAsset(_instance, "Assets/NovellaEngine/Resources/NovellaEngine/NovellaVariableSettings.asset");
                        AssetDatabase.SaveAssets();
                    }
#endif
                }
                return _instance;
            }
        }
    }
}