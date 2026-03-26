using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NovellaEngine.Data
{
    public enum EVarType { Integer, Boolean, String }

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

        public bool HasLimits = false;
        public int MinValue = 0;
        public int MaxValue = 100;

        public int DefaultInt = 0;
        public bool DefaultBool = false;
        public string DefaultString = "";

        public bool IsPremiumCurrency = false;
        public Sprite Icon;
    }

    public class NovellaVariableSettings : ScriptableObject
    {
        public List<VariableDefinition> Variables = new List<VariableDefinition>();

        private static NovellaVariableSettings _instance;
        public static NovellaVariableSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<NovellaVariableSettings>("NovellaVariableSettings");

#if UNITY_EDITOR
                    if (_instance == null)
                    {
                        _instance = CreateInstance<NovellaVariableSettings>();

                        if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine"))
                            AssetDatabase.CreateFolder("Assets", "NovellaEngine");

                        if (!AssetDatabase.IsValidFolder("Assets/NovellaEngine/Resources"))
                            AssetDatabase.CreateFolder("Assets/NovellaEngine", "Resources");

                        AssetDatabase.CreateAsset(_instance, "Assets/NovellaEngine/Resources/NovellaVariableSettings.asset");
                        AssetDatabase.SaveAssets();
                    }
#endif
                }
                return _instance;
            }
        }
    }
}