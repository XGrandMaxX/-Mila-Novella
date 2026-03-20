using UnityEditor;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Маленький помощник для перевода самого интерфейса редактора
    /// </summary>
    public static class ToolLang
    {
        public static bool IsRU => EditorPrefs.GetBool("NovellaGraph_IsRU", true);

        public static void Toggle() => EditorPrefs.SetBool("NovellaGraph_IsRU", !IsRU);

        public static string Get(string en, string ru) => IsRU ? ru : en;
    }
}