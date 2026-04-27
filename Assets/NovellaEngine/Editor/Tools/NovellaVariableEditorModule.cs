using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    public class NovellaVariableEditorModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Variables", "Переменные");
        public string ModuleIcon => "📊";

        private EditorWindow _window;
        private Vector2 _leftScrollPos;
        private Vector2 _rightScrollPos;

        private string _searchQuery = "";
        private int _selectedIndex = -1;

        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
        }

        public void OnDisable() { }

        /// <summary>
        /// Используется внешними окнами (например, NovellaGlobalVariablesWindow),
        /// чтобы выделить переменную сразу при открытии.
        /// </summary>
        public void SelectByName(string variableName)
        {
            if (string.IsNullOrEmpty(variableName)) { _selectedIndex = -1; return; }
            var settings = NovellaVariableSettings.Instance;
            if (settings == null) { _selectedIndex = -1; return; }
            int idx = settings.Variables.FindIndex(v => v.Name == variableName);
            _selectedIndex = idx;
        }

        /// <summary>
        /// Универсальная точка входа.
        /// Из графа открывается standalone-окно (не выкидывает из NovellaGraphWindow).
        /// Открыть прямо вкладку в Hub можно через ShowInHub().
        /// </summary>
        public static void ShowWindow(string variableToSelect = null)
        {
            // Если Hub уже открыт и активен — просто переключим вкладку (поведение как раньше).
            // Иначе открываем standalone-окно поверх графа, чтобы не закрывать его.
            bool hubAlreadyOpen = NovellaHubWindow.Instance != null
                                  && EditorWindow.focusedWindow is NovellaHubWindow;

            if (hubAlreadyOpen)
            {
                ShowInHub(variableToSelect);
            }
            else
            {
                NovellaGlobalVariablesWindow.ShowStandalone(variableToSelect);
            }
        }

        /// <summary>
        /// Открывает вкладку Variables в NovellaHubWindow.
        /// </summary>
        public static void ShowInHub(string variableToSelect = null)
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null)
            {
                NovellaHubWindow.Instance.SwitchToModule(4);
                var mod = NovellaHubWindow.Instance.GetModule(4) as NovellaVariableEditorModule;
                if (mod != null && !string.IsNullOrEmpty(variableToSelect))
                {
                    mod.SelectByName(variableToSelect);
                }
            }
        }

        public void DrawGUI(Rect position)
        {
            var settings = NovellaVariableSettings.Instance;
            if (settings == null) return;

            DrawHeader();

            GUILayout.BeginHorizontal();
            DrawLeftPanel(settings);
            DrawRightPanel(settings);
            GUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("📋 " + ToolLang.Get("GLOBAL VARIABLES DATABASE", "БАЗА ПЕРЕМЕННЫХ ПРОЕКТА"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, normal = { textColor = new Color(0.2f, 0.8f, 0.5f) } });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawLeftPanel(NovellaVariableSettings settings)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(280), GUILayout.ExpandHeight(true));

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("✖", EditorStyles.toolbarButton, GUILayout.Width(25)))
            {
                _searchQuery = "";
                GUI.FocusControl(null);
            }
            GUILayout.EndHorizontal();

            _leftScrollPos = GUILayout.BeginScrollView(_leftScrollPos);

            var groupedVars = settings.Variables
                .Select((v, index) => new { Var = v, Index = index })
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Var.Category) ? "Uncategorized" : x.Var.Category)
                .OrderBy(g => g.Key);

            foreach (var group in groupedVars)
            {
                if (!_categoryFoldouts.ContainsKey(group.Key)) _categoryFoldouts[group.Key] = true;

                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                _categoryFoldouts[group.Key] = GUILayout.Toggle(_categoryFoldouts[group.Key], _categoryFoldouts[group.Key] ? "▼ " + group.Key : "▶ " + group.Key, EditorStyles.toolbarButton, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                GUI.backgroundColor = Color.white;

                if (_categoryFoldouts[group.Key])
                {
                    foreach (var item in group)
                    {
                        var v = item.Var;
                        int originalIndex = item.Index;

                        if (!string.IsNullOrEmpty(_searchQuery) && !v.Name.Contains(_searchQuery, System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        bool isSelected = _selectedIndex == originalIndex;

                        if (isSelected) GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f, 1f);
                        else if (v.IsPremiumCurrency) GUI.backgroundColor = new Color(0.25f, 0.15f, 0.35f, 1f);
                        else GUI.backgroundColor = Color.white;

                        GUIStyle btnStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, fontSize = 13, fixedHeight = 30 };
                        if (isSelected) btnStyle.normal.textColor = Color.white;
                        else if (v.IsPremiumCurrency) btnStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

                        string displayName = string.IsNullOrWhiteSpace(v.Name) ? ToolLang.Get("[ Unnamed ]", "[ Без имени ]") : v.Name;

                        string icon = "💠 ";
                        if (v.Type == EVarType.Boolean) icon = "✓ ";
                        else if (v.Type == EVarType.String) icon = "📝 ";
                        if (v.Type == EVarType.Integer && v.IsPremiumCurrency) icon = "💎 ";

                        bool hasError = HasNamingError(v.Name, settings, originalIndex);
                        if (hasError) icon = "⚠️ ";

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(15);
                        if (GUILayout.Button(icon + displayName, btnStyle))
                        {
                            _selectedIndex = originalIndex;
                            GUI.FocusControl(null);
                        }
                        GUILayout.EndHorizontal();
                        GUI.backgroundColor = Color.white;
                    }
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(5);
            if (GUILayout.Button("+ " + ToolLang.Get("Create Variable", "Создать Переменную"), GUILayout.Height(35)))
            {
                Undo.RecordObject(settings, "Add Variable");
                settings.Variables.Add(new VariableDefinition { Name = "NEW_VARIABLE" });
                _selectedIndex = settings.Variables.Count - 1;
                _searchQuery = "";
                EditorUtility.SetDirty(settings);
            }

            GUILayout.EndVertical();
        }

        private void DrawRightPanel(NovellaVariableSettings settings)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (_selectedIndex >= 0 && _selectedIndex < settings.Variables.Count)
            {
                var v = settings.Variables[_selectedIndex];
                _rightScrollPos = GUILayout.BeginScrollView(_rightScrollPos);

                if (v.Type == EVarType.Integer && v.IsPremiumCurrency)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.1f, 0.3f);
                    GUILayout.BeginVertical(new GUIStyle(GUI.skin.box));
                    GUILayout.Label("💎 " + ToolLang.Get("PREMIUM CURRENCY", "ДОНАТ ВАЛЮТА"), new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.4f) }, fontSize = 16 }, GUILayout.Height(30));
                    GUILayout.EndVertical();
                    GUI.backgroundColor = Color.white;
                    GUILayout.Space(10);
                }
                else
                {
                    GUILayout.Space(10);
                    GUILayout.Label("⚙ " + ToolLang.Get("Variable Setup", "Настройка Переменной"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
                    GUILayout.Space(15);
                }

                EditorGUI.BeginChangeCheck();

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                GUILayout.Label(ToolLang.Get("Variable Key (Code Name)", "Ключ переменной (Имя)"), EditorStyles.boldLabel);
                GUIStyle largeTextField = new GUIStyle(EditorStyles.textField) { fontSize = 16, fontStyle = FontStyle.Bold, fixedHeight = 30 };
                v.Name = EditorGUILayout.TextField(v.Name, largeTextField);
                GUILayout.EndVertical();

                GUILayout.Space(10);

                GUILayout.BeginVertical(GUILayout.Width(150));
                GUILayout.Label(ToolLang.Get("Category (Folder)", "Категория (Папка)"), EditorStyles.boldLabel);
                v.Category = EditorGUILayout.TextField(v.Category, new GUIStyle(EditorStyles.textField) { fixedHeight = 30 });
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                bool hasSpaces = v.Name.Contains(" ");
                bool isLowerCase = v.Name.Any(char.IsLower);
                bool isDuplicate = settings.Variables.Count(varDef => varDef.Name == v.Name) > 1;
                bool isEmpty = string.IsNullOrWhiteSpace(v.Name);

                if (isEmpty) EditorGUILayout.HelpBox(ToolLang.Get("Name cannot be empty!", "Имя не может быть пустым!"), MessageType.Error);
                else if (isDuplicate) EditorGUILayout.HelpBox(ToolLang.Get("Duplicate key exists!", "Дубликат ключа! Это сломает игру."), MessageType.Error);
                else if (hasSpaces || isLowerCase)
                {
                    EditorGUILayout.HelpBox(ToolLang.Get("Use UPPERCASE and underscores (e.g., LAST_SPOKEN).", "Используйте ЗАГЛАВНЫЕ БУКВЫ и нижние_подчеркивания."), MessageType.Warning);
                    if (GUILayout.Button("✨ " + ToolLang.Get("Auto-Fix Name", "Исправить автоматически"), GUILayout.Height(25)))
                    {
                        Undo.RecordObject(settings, "Fix Name");
                        v.Name = Regex.Replace(v.Name.Replace(" ", "_").ToUpper(), @"[^A-ZА-ЯЁ0-9_]", "");
                        GUI.FocusControl(null);
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(ToolLang.Get("Core Settings", "Базовые Настройки"), EditorStyles.boldLabel);
                GUILayout.Space(5);

                GUILayout.BeginHorizontal();
                float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 80;

                string[] displayNames = ToolLang.IsRU
                    ? new string[] { "Число (Number)", "Флаг / Да-Нет (Boolean)", "Текст (String)" }
                    : new string[] { "Number (Integer)", "True/False (Boolean)", "Text (String)" };

                v.Type = (EVarType)EditorGUILayout.Popup(
                    ToolLang.Get("Variable Type", "Тип переменной"),
                    (int)v.Type,
                    displayNames
                ); GUILayout.Space(20);

                GUI.backgroundColor = v.Scope == EVarScope.Global ? new Color(0.6f, 1f, 0.6f) : Color.white;
                v.Scope = (EVarScope)EditorGUILayout.EnumPopup(ToolLang.Get("Scope:", "Жизнь:"), v.Scope, GUILayout.Height(25));
                GUI.backgroundColor = Color.white;

                EditorGUIUtility.labelWidth = lw;
                GUILayout.EndHorizontal();
                GUILayout.Space(5);

                if (v.Scope == EVarScope.Local) EditorGUILayout.HelpBox(ToolLang.Get("Local: Resets to 'Default Value' every time you launch a Chapter. Ideal for chapter-specific logic.", "Локальная (Local): Сбрасывается до 'Стартового значения' при каждом запуске Главы. Идеально для сюжета внутри одной сцены."), MessageType.None);
                else EditorGUILayout.HelpBox(ToolLang.Get("Global: Saved FOREVER in PlayerPrefs. Persists between chapters. Ideal for Gold, Stats, or major story flags.", "Глобальная (Global): Сохраняется НАВСЕГДА в PlayerPrefs. Идеально для Золота, Статов игрока или важных концовок."), MessageType.Info);

                if (v.Type == EVarType.Integer)
                {
                    GUILayout.Space(5);
                    v.IsPremiumCurrency = EditorGUILayout.ToggleLeft(new GUIContent(" 💎 " + ToolLang.Get("Premium Currency (Donation)", "Донат-валюта (Премиум)"), ToolLang.Get("Highlights this variable in the editor.", "Выделяет переменную в редакторе.")), v.IsPremiumCurrency);
                }
                else v.IsPremiumCurrency = false;

                GUILayout.EndVertical();
                GUILayout.Space(10);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(ToolLang.Get("Values & Constraints", "Значения и Лимиты"), EditorStyles.boldLabel);
                GUILayout.Space(5);

                if (v.Type == EVarType.Integer)
                {
                    v.DefaultInt = EditorGUILayout.IntField(ToolLang.Get("Default (Start) Value", "Стартовое Значение"), v.DefaultInt);
                    GUILayout.Space(5);
                    v.HasLimits = EditorGUILayout.ToggleLeft(ToolLang.Get("Enforce Min/Max Limits (Clamp)", "Ограничить (Min/Max лимиты)"), v.HasLimits);
                    if (v.HasLimits)
                    {
                        EditorGUILayout.HelpBox(ToolLang.Get("Limits prevent the value from going out of bounds (e.g. Gold cannot be < 0).", "Лимиты не дадут значению выйти за рамки (Например, Золото не упадет ниже 0)."), MessageType.None);
                        GUILayout.BeginHorizontal();
                        v.MinValue = EditorGUILayout.IntField("Min:", v.MinValue);
                        v.MaxValue = EditorGUILayout.IntField("Max:", v.MaxValue);
                        GUILayout.EndHorizontal();
                        if (v.MinValue > v.MaxValue) v.MaxValue = v.MinValue;
                    }
                }
                else if (v.Type == EVarType.Boolean)
                {
                    v.DefaultBool = EditorGUILayout.ToggleLeft(ToolLang.Get(" Default Value (True/False)", " Начальное Значение (Истина/Ложь)"), v.DefaultBool);
                }
                else if (v.Type == EVarType.String)
                {
                    v.DefaultString = EditorGUILayout.TextField(ToolLang.Get("Default Text", "Стартовый Текст"), v.DefaultString);
                    EditorGUILayout.HelpBox(ToolLang.Get("String variables are perfect for storing names (e.g. 'Count Dracula'), states, or passwords.", "Строковые переменные идеальны для сохранения имен (Например 'Граф Дракула'), состояний или паролей."), MessageType.None);
                }

                GUILayout.EndVertical();
                GUILayout.Space(10);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(ToolLang.Get("UI Presentation", "Отображение в UI"), EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(ToolLang.Get("Optional: Attach an icon if you plan to show this in the game UI (e.g., Inventory).", "Опционально: Прикрепите иконку, если планируете выводить это в инвентарь."), MessageType.None);
                GUILayout.Space(5);
                v.Icon = (Sprite)EditorGUILayout.ObjectField(ToolLang.Get("Icon Sprite:", "Иконка:"), v.Icon, typeof(Sprite), false, GUILayout.Height(64), GUILayout.Width(250));
                GUILayout.EndVertical();
                GUILayout.Space(10);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(ToolLang.Get("Developer Notes", "Заметки разработчика"), EditorStyles.boldLabel);

                GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 };
                string newDesc = EditorGUILayout.TextArea(v.Description, textAreaStyle, GUILayout.Height(60));
                if (newDesc.Length > 200) newDesc = newDesc.Substring(0, 200);
                v.Description = newDesc;

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUIStyle counterStyle = new GUIStyle(EditorStyles.miniLabel);
                if (v.Description.Length >= 200) counterStyle.normal.textColor = new Color(0.9f, 0.3f, 0.3f);
                GUILayout.Label($"{v.Description.Length}/200", counterStyle);
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(settings);
                }

                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("🔍 " + ToolLang.Get("Find References", "Найти Зависимости"), GUILayout.Height(35), GUILayout.Width(200)))
                {
                    NovellaReferenceFinderWindow.ShowWindow(v.Name);
                }

                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("🗑 " + ToolLang.Get("Delete", "Удалить"), GUILayout.Height(35), GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog(ToolLang.Get("Delete?", "Удалить?"), ToolLang.Get($"Are you sure you want to delete '{v.Name}'?", $"Точно удалить '{v.Name}'?"), "Yes", "No"))
                    {
                        Undo.RecordObject(settings, "Remove Variable");
                        settings.Variables.RemoveAt(_selectedIndex);
                        _selectedIndex = -1;
                        EditorUtility.SetDirty(settings);
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a variable from the list to edit its details.", "Выберите переменную из списка слева для настройки."), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }

            GUILayout.EndVertical();
        }

        private bool HasNamingError(string name, NovellaVariableSettings settings, int currentIndex)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.Contains(" ") || name.Any(char.IsLower)) return true;
            for (int i = 0; i < settings.Variables.Count; i++)
            {
                if (i != currentIndex && settings.Variables[i].Name == name) return true;
            }
            return false;
        }
    }
}