using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaAPIWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private GUIStyle _codeStyle;

        private int _currentTab = 0;
        private NovellaTabState _tabState = new NovellaTabState();

        public static void ShowWindow(int tabIndex = 0)
        {
            var win = GetWindow<NovellaAPIWindow>(true, ToolLang.Get("Novella API Cheat Sheet", "Шпаргалка C# API (Novella)"), true);
            win.minSize = new Vector2(750, 650);
            win._currentTab = tabIndex;
            win.ShowUtility();
        }

        private void OnEnable()
        {
            _tabState.Initialize(Repaint);
            _tabState.SetActive(_currentTab.ToString());
            EditorApplication.update += _tabState.Update;
        }

        private void OnDisable() => EditorApplication.update -= _tabState.Update;

        private void OnGUI()
        {
            if (_codeStyle == null) _codeStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 13, margin = new RectOffset(10, 10, 10, 10) };

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(220));

            if (NovellaEditorLayout.DrawAnimatedTab("0", "📊", ToolLang.Get("Variables API", "API Переменных"), _tabState, new Color(0.15f, 0.5f, 0.75f))) { _currentTab = 0; _tabState.SetActive("0"); }
            if (NovellaEditorLayout.DrawAnimatedTab("1", "⚡", ToolLang.Get("Events API", "API Событий"), _tabState, new Color(0.15f, 0.5f, 0.75f))) { _currentTab = 1; _tabState.SetActive("1"); }

            GUILayout.EndVertical();

            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            if (_currentTab == 0) DrawVariablesTab(); else DrawEventsTab();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawVariablesTab()
        {
            EditorGUILayout.HelpBox(ToolLang.Get(
                "Use the NovellaVariables class to safely interact with your global and local variables. It handles limits, saving, and premium currency encryption automatically.",
                "Используйте класс NovellaVariables для безопасного взаимодействия с вашими переменными. Он сам обрабатывает лимиты, сохранения и шифрование донатной валюты."
            ), MessageType.Info);
            GUILayout.Space(15);

            GUILayout.Label(ToolLang.Get("1. Reading Variables", "1. Чтение переменных"), EditorStyles.boldLabel);

            string commentRead = ToolLang.Get("// Returns variable value. If it doesn't exist - returns default value.", "// Возвращает значение переменной. Если её нет - вернет стартовое значение.");

            DrawCodeBlock(
                $"<color=#608b4e>{commentRead}</color>\n" +
                "<color=#569cd6>int</color> gold = <color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>GetInt</color>(<color=#ce9178>\"GOLD\"</color>);\n" +
                "<color=#569cd6>bool</color> isUnlocked = <color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>GetBool</color>(<color=#ce9178>\"DOOR_UNLOCKED\"</color>);\n" +
                "<color=#569cd6>string</color> pName = <color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>GetString</color>(<color=#ce9178>\"PLAYER_NAME\"</color>);"
            );

            GUILayout.Space(15);

            GUILayout.Label(ToolLang.Get("2. Writing Variables", "2. Изменение переменных"), EditorStyles.boldLabel);

            string commentWrite = ToolLang.Get("// Automatically saves global variables to PlayerPrefs!", "// Автоматически сохраняет глобальные переменные в PlayerPrefs!");

            DrawCodeBlock(
                $"<color=#608b4e>{commentWrite}</color>\n" +
                "<color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>SetInt</color>(<color=#ce9178>\"GOLD\"</color>, gold + <color=#b5cea8>100</color>);\n" +
                "<color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>SetBool</color>(<color=#ce9178>\"DOOR_UNLOCKED\"</color>, <color=#569cd6>true</color>);\n" +
                "<color=#4ec9b0>NovellaVariables</color>.<color=#dcdcaa>SetString</color>(<color=#ce9178>\"PLAYER_NAME\"</color>, <color=#ce9178>\"Alex\"</color>);"
            );
        }

        private void DrawEventsTab()
        {
            EditorGUILayout.HelpBox(ToolLang.Get(
                "You can trigger custom events directly from the visual graph using the 'Event Broadcast' node. Below is the code to catch those events in your game scripts.",
                "Вы можете вызывать кастомные события прямо из визуального графа с помощью ноды 'Event Broadcast'. Ниже представлен код для перехвата этих событий в ваших скриптах."
            ), MessageType.Info);
            GUILayout.Space(15);

            GUILayout.Label(ToolLang.Get("Catching Graph Events", "Перехват событий графа"), EditorStyles.boldLabel);

            string commentSub = ToolLang.Get("// Must subscribe and unsubscribe from the event", "// Обязательно подписываемся и отписываемся от события");
            string commentHandler = ToolLang.Get("// Handler method. eventId - event name from node, param - passed text", "// Метод-обработчик. eventId - имя события из ноды, param - переданный текст");
            string commentLog = ToolLang.Get("// Player received item", "// Игрок получил предмет");

            DrawCodeBlock(
                "<color=#569cd6>public class</color> <color=#4ec9b0>MyGameManager</color> : <color=#4ec9b0>MonoBehaviour</color>\n" +
                "{\n" +
                $"    <color=#608b4e>{commentSub}</color>\n" +
                "    <color=#569cd6>private void</color> <color=#dcdcaa>OnEnable</color>() {\n" +
                "        <color=#4ec9b0>NovellaPlayer</color>.OnNovellaEvent += <color=#dcdcaa>HandleGraphEvent</color>;\n" +
                "    }\n\n" +
                "    <color=#569cd6>private void</color> <color=#dcdcaa>OnDisable</color>() {\n" +
                "        <color=#4ec9b0>NovellaPlayer</color>.OnNovellaEvent -= <color=#dcdcaa>HandleGraphEvent</color>;\n" +
                "    }\n\n" +
                $"    <color=#608b4e>{commentHandler}</color>\n" +
                "    <color=#569cd6>private void</color> <color=#dcdcaa>HandleGraphEvent</color>(<color=#569cd6>string</color> eventId, <color=#569cd6>string</color> param) {\n" +
                "        <color=#c586c0>if</color> (eventId == <color=#ce9178>\"GiveItem\"</color>) {\n" +
                $"            <color=#608b4e>{commentLog}: </color>\n" +
                "            <color=#4ec9b0>Debug</color>.<color=#dcdcaa>Log</color>(<color=#ce9178>\"Item: \"</color> + param);\n" +
                "        }\n" +
                "    }\n" +
                "}"
            );
        }

        private void DrawCodeBlock(string codeText)
        {
            GUILayout.BeginVertical();

            GUIContent content = new GUIContent(codeText);
            float height = _codeStyle.CalcHeight(content, position.width - 230 - 40);

            Rect rect = GUILayoutUtility.GetRect(position.width - 230 - 30, height + 20);
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 1f));

            Rect textRect = new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20);
            GUI.Label(textRect, codeText, _codeStyle);

            GUILayout.EndVertical();
        }
    }
}