using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    [InitializeOnLoad]
    public class NovellaWelcomeWindow : EditorWindow
    {
        private const string TUTORIAL_FILE_PATH = "Assets/NovellaEngine/Tutorials/01_StartHere.asset";

        static NovellaWelcomeWindow() { EditorApplication.delayCall += ShowWindowOnFirstLaunch; }

        private static void ShowWindowOnFirstLaunch()
        {
            if (!EditorPrefs.GetBool("Novella_HasShownWelcome", false)) ShowWindow();
        }

        [MenuItem("Window/Novella Engine/📖 Welcome Tutorial", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<NovellaWelcomeWindow>(true, "Novella Engine", true);
            window.minSize = new Vector2(450, 320); window.maxSize = new Vector2(450, 320);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();

            string langBtnText = ToolLang.IsRU ? "EN" : "RU";
            if (GUILayout.Button(langBtnText, EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                ToolLang.Toggle();
                this.titleContent.text = ToolLang.Get("Novella Engine - Start", "Novella Engine - Старт");
                GUI.FocusControl(null);
                Repaint();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(10);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.2f, 0.6f, 1f) } };
            GUILayout.Label(ToolLang.Get("Welcome to Novella Engine!", "Добро пожаловать в Novella Engine!"), titleStyle);
            GUILayout.Space(15);

            GUIStyle descStyle = new GUIStyle(EditorStyles.label) { fontSize = 14, wordWrap = true, richText = true, alignment = TextAnchor.UpperCenter };
            string descEn = "Creating visual novels has never been easier. \n\nClick the button below to <b>instantly open</b> the interactive tutorial graph and see how it works!";
            string descRu = "Создание визуальных новелл еще никогда не было таким простым. \n\nНажмите кнопку ниже, чтобы <b>сразу открыть</b> интерактивный учебный граф и увидеть всё в деле!";
            GUILayout.Label(ToolLang.Get(descEn, descRu), descStyle);
            GUILayout.Space(30);

            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button(ToolLang.Get("🚀 START TUTORIAL", "🚀 НАЧАТЬ ОБУЧЕНИЕ"), new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold }, GUILayout.Height(60)))
            {
                StartTutorial();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            bool dontShowAgain = EditorPrefs.GetBool("Novella_HasShownWelcome", false);
            dontShowAgain = GUILayout.Toggle(dontShowAgain, ToolLang.Get(" Do not show again on startup", " Больше не показывать при старте"));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool("Novella_HasShownWelcome", dontShowAgain);

            GUILayout.Space(10);
            GUILayout.EndVertical();
        }

        public static void StartTutorial()
        {
            var tutorialAsset = AssetDatabase.LoadAssetAtPath<NovellaTree>(TUTORIAL_FILE_PATH);
            if (tutorialAsset != null)
            {
                NovellaGraphWindow.OpenGraphWindow(tutorialAsset);
                EditorGUIUtility.PingObject(tutorialAsset);
                EditorPrefs.SetBool("Novella_HasShownWelcome", true);
                if (HasOpenInstances<NovellaWelcomeWindow>()) GetWindow<NovellaWelcomeWindow>().Close();
            }
            else
            {
                EditorUtility.DisplayDialog(ToolLang.Get("File not found!", "Файл не найден!"), ToolLang.Get("Tutorial asset '01_StartHere' not found in Tutorials folder.", "Файл '01_StartHere' не найден в папке Tutorials."), "OK");
            }
        }
    }
}