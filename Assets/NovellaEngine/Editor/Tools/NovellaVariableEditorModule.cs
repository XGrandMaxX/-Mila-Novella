// ════════════════════════════════════════════════════════════════════════════
// NovellaVariableEditorModule — редактор глобальных переменных проекта.
// Дизайн повторяет 1:1 паттерн NovellaCharacterEditorModule (sidebar 220px +
// settings flex, помощники DrawDarkTextField / DrawDarkToggle / DrawSectionHeader,
// мульти-выбор Shift/Ctrl, кнопка «💡 Подсказки: Вкл/Выкл»).
//
// Функциональные фичи (ВСЕ сохранены):
//   • Поиск + группировка по категориям с foldout
//   • Множественный выбор (Shift = диапазон, Ctrl/Cmd = добавить/убрать)
//   • Создание / удаление (одиночное + массовое)
//   • Все поля: Name, Category, Type (Int/Bool/String), Scope (Local/Global),
//     IsPremiumCurrency, Default*, HasLimits + Min/Max, Icon, Description (200)
//   • Валидация имени: пустое / дубль / lowercase / пробелы + Auto-Fix
//   • Кнопка «🔍 Find references» (NovellaReferenceFinderWindow)
//   • Premium-currency акцент золотом
// ════════════════════════════════════════════════════════════════════════════

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

        // ─── Hub-палитра (точно как в Characters) ───
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();
        // Семантические — не зависят от темы.
        private static readonly Color C_PREMIUM = new Color(0.96f, 0.76f, 0.43f);
        private static readonly Color C_ERROR   = new Color(0.85f, 0.32f, 0.32f);
        private static readonly Color C_SUCCESS = new Color(0.30f, 0.85f, 0.45f);
        // Цвета для Scope-badge'й (Local = тёплый янтарный «временно»,
        // Global = холодный циан «постоянно/везде»).
        private static readonly Color C_LOCAL   = new Color(0.96f, 0.65f, 0.22f);
        private static readonly Color C_GLOBAL  = new Color(0.30f, 0.72f, 0.95f);

        private EditorWindow _window;
        // 280px (с 220) — чтобы 20-символьные UPPERCASE-имена комфортно влезали
        // в карточку списка слева без обрезания.
        private float _sidebarWidth = 280f;
        // Максимальная длина имени переменной — UPPERCASE_WITH_UNDERSCORES
        // в коде графа должно быть короткое и читаемое.
        private const int MAX_VAR_NAME_LEN = 25;

        // ─── Multi-select state (как в Characters) ───
        private List<int> _selectedIndices = new List<int>();
        private int _lastClickedIndex = -1;
        private List<int> _currentVisualOrder = new List<int>();

        private string _searchQuery = "";
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();
        // Скроллы для списков значений (Choice / List). Ключ = controlPrefix
        // — он уникален для каждого вида списка и переменной.
        private Dictionary<string, Vector2> _stringListScrolls = new Dictionary<string, Vector2>();

        // Состояние inline-редактирования «Своей» категории. Активно только
        // для одной переменной за раз — сбрасывается при смене выделения.
        private bool _categoryEditingCustom = false;
        private string _categoryEditingDraft = "";

        public void OnEnable(EditorWindow hostWindow) { _window = hostWindow; }
        public void OnDisable() { }

        /// <summary>
        /// Внешние окна (NovellaGlobalVariablesWindow) могут открыть модуль с
        /// заранее выделенной переменной.
        /// </summary>
        public void SelectByName(string variableName)
        {
            _selectedIndices.Clear();
            if (string.IsNullOrEmpty(variableName)) return;
            var settings = NovellaVariableSettings.Instance;
            if (settings == null) return;
            int idx = settings.Variables.FindIndex(v => v.Name == variableName);
            if (idx >= 0) { _selectedIndices.Add(idx); _lastClickedIndex = idx; }
        }

        public static void ShowWindow(string variableToSelect = null)
        {
            bool hubAlreadyOpen = NovellaHubWindow.Instance != null
                                  && EditorWindow.focusedWindow is NovellaHubWindow;
            if (hubAlreadyOpen) ShowInHub(variableToSelect);
            else NovellaGlobalVariablesWindow.ShowStandalone(variableToSelect);
        }

        public static void ShowInHub(string variableToSelect = null)
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null)
            {
                NovellaHubWindow.Instance.SwitchToModule(4);
                var mod = NovellaHubWindow.Instance.GetModule(4) as NovellaVariableEditorModule;
                if (mod != null && !string.IsNullOrEmpty(variableToSelect))
                    mod.SelectByName(variableToSelect);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MAIN DRAW
        // ═══════════════════════════════════════════════════════════════════════

        public void DrawGUI(Rect position)
        {
            var settings = NovellaVariableSettings.Instance;
            if (settings == null) return;

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            Rect sideRect = new Rect(0, 0, _sidebarWidth, position.height);
            Rect detailRect = new Rect(sideRect.xMax, 0,
                position.width - sideRect.xMax, position.height);

            DrawVariablesSidebar(sideRect, settings);
            DrawDivider(new Rect(sideRect.xMax - 1, 0, 1, position.height));

            // Single / Multi / Empty ветки — точно как в Characters.
            if (_selectedIndices.Count == 1)
            {
                int idx = _selectedIndices[0];
                if (idx >= 0 && idx < settings.Variables.Count)
                {
                    DrawDetailPanel(detailRect, settings, idx);
                }
                else
                {
                    DrawEmptyState(detailRect);
                }
            }
            else if (_selectedIndices.Count > 1)
            {
                DrawMultiSelectState(detailRect, settings);
            }
            else
            {
                DrawEmptyState(detailRect);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // SIDEBAR (left)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawVariablesSidebar(Rect rect, NovellaVariableSettings settings)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            GUILayout.BeginArea(rect);

            GUILayout.Space(14);
            DrawIndentedLabel(ToolLang.Get("Variables", "Переменные"),
                14, fontSize: 14, bold: true, color: C_TEXT_1);
            DrawIndentedLabel(string.Format(
                ToolLang.Get("{0} in this project", "{0} в проекте"),
                settings.Variables.Count),
                14, fontSize: 11, color: C_TEXT_3);
            GUILayout.Space(10);

            // ─── Кнопка «+ Новая переменная» (cтиль = Characters' «+ Новый персонаж») ───
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("＋ " + ToolLang.Get("New variable", "Новая переменная"),
                GUILayout.Height(32)))
            {
                EditorApplication.delayCall += () => CreateNewVariable(settings);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // ─── Search field (стиль = Characters search) ───
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            Rect searchRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(searchRect, C_BG_PRIMARY);
            DrawRectBorder(searchRect, C_BORDER);
            GUI.SetNextControlName("VarSearch");
            _searchQuery = GUI.TextField(
                new Rect(searchRect.x + 8, searchRect.y + 6, searchRect.width - 16, 16),
                _searchQuery, GUIStyle.none);
            if (string.IsNullOrEmpty(_searchQuery))
            {
                GUI.color = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 8, searchRect.y + 4, searchRect.width - 16, 20),
                    "🔍  " + ToolLang.Get("Search…", "Поиск…"));
                GUI.color = Color.white;
            }
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── List (scroll) ───
            float listHeight = rect.height - GUILayoutUtility.GetLastRect().yMax - 4;
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(listHeight));

            _currentVisualOrder.Clear();

            if (settings.Variables.Count == 0)
            {
                GUILayout.Space(20);
                var emptySt = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                    fontSize = 11, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_TEXT_3 }
                };
                GUILayout.Label(ToolLang.Get(
                    "No variables yet.\nClick «+ New variable» above.",
                    "Переменных пока нет.\nНажми «+ Новая переменная» выше."), emptySt);
            }
            else
            {
                DrawCategorizedList(settings);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCategorizedList(NovellaVariableSettings settings)
        {
            string q = (_searchQuery ?? "").Trim();
            bool hasQuery = !string.IsNullOrEmpty(q);

            // Группируем переменные по категории, сохраняя original-индексы.
            var groupedVars = settings.Variables
                .Select((v, index) => new { Var = v, Index = index })
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Var.Category)
                    ? ToolLang.Get("Uncategorized", "Без категории")
                    : x.Var.Category)
                .OrderBy(g => g.Key);

            int totalShown = 0;
            foreach (var group in groupedVars)
            {
                if (!_categoryFoldouts.ContainsKey(group.Key)) _categoryFoldouts[group.Key] = true;

                // Фильтр.
                var filtered = group.Where(x => !hasQuery
                    || x.Var.Name.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                    || (x.Var.Category ?? "").Contains(q, System.StringComparison.OrdinalIgnoreCase)
                ).ToList();
                if (filtered.Count == 0) continue;

                DrawIndentedLabel(group.Key.ToUpper(),
                    14, fontSize: 9, bold: true, color: C_TEXT_3);

                // При активном поиске разворачиваем все группы.
                bool expanded = _categoryFoldouts[group.Key] || hasQuery;
                if (expanded)
                {
                    foreach (var item in filtered)
                    {
                        _currentVisualOrder.Add(item.Index);
                        DrawVariableRow(item.Var, item.Index, settings);
                        totalShown++;
                    }
                }
                GUILayout.Space(6);
            }

            if (totalShown == 0 && hasQuery)
            {
                GUILayout.Space(20);
                var emptySt = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                    fontSize = 11, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_TEXT_3 }
                };
                GUILayout.Label(string.Format(ToolLang.Get(
                    "No variables match «{0}»",
                    "Под «{0}» ничего не найдено"), q), emptySt);
            }
        }

        private void DrawVariableRow(VariableDefinition v, int originalIndex, NovellaVariableSettings settings)
        {
            bool active = _selectedIndices.Contains(originalIndex);
            bool isPremium = v.Type == EVarType.Integer && v.IsPremiumCurrency;
            bool hasError = HasNamingError(v.Name, settings, originalIndex);

            Rect r = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
            r.x += 8; r.width -= 16;

            // Background — точно как у Characters.
            if (active) EditorGUI.DrawRect(r, C_BG_RAISED);
            else if (r.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(r,
                    new Color(C_BG_RAISED.r, C_BG_RAISED.g, C_BG_RAISED.b, 0.6f));
                if (Event.current.type == EventType.MouseMove) _window?.Repaint();
            }

            // Cyan-полоса слева у выделенного.
            if (active)
                EditorGUI.DrawRect(new Rect(r.x, r.y + 3, 2, r.height - 6), C_ACCENT);

            // Иконка типа в круглой подложке (как «аватарка» персонажа).
            float avSize = 26;
            Rect avRect = new Rect(r.x + 8, r.y + (r.height - avSize) / 2, avSize, avSize);
            Color avColor = hasError ? C_ERROR
                          : isPremium ? C_PREMIUM
                          : C_ACCENT;
            EditorGUI.DrawRect(avRect, new Color(avColor.r, avColor.g, avColor.b, 0.18f));
            DrawRectBorder(avRect, avColor);
            string icon = hasError ? "⚠"
                        : isPremium ? "💎"
                        : v.Type == EVarType.Boolean ? "✓"
                        : v.Type == EVarType.String  ? "📝"
                        : v.Type == EVarType.Float   ? "≈"
                        : v.Type == EVarType.Choice  ? "🎯"
                        : v.Type == EVarType.List    ? "📋"
                        : "💠";
            var iconSt = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13, fontStyle = FontStyle.Bold,
                normal = { textColor = avColor }
            };
            GUI.Label(avRect, icon, iconSt);

            // Имя.
            Rect nameRect = new Rect(avRect.xMax + 8, r.y + 5, r.width - avSize - 30, 16);
            var nameStyle = new GUIStyle(EditorStyles.label) {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = active ? C_TEXT_1 : C_TEXT_2 }
            };
            string displayName = string.IsNullOrWhiteSpace(v.Name)
                ? ToolLang.Get("[ Unnamed ]", "[ Без имени ]") : v.Name;
            GUI.Label(nameRect, displayName, nameStyle);

            // Subtitle — мини-badge Scope + значение по умолчанию.
            Rect subRect = new Rect(avRect.xMax + 8, r.y + 22, r.width - avSize - 30, 14);
            // Сначала Scope-badge (16x12, цветной с буквой).
            Rect badgeR = new Rect(subRect.x, subRect.y + 1, 18, 12);
            DrawScopeBadge(badgeR, v.Scope, mini: true);

            // Дефолт-значение справа от badge'а.
            string defaultStr;
            switch (v.Type)
            {
                case EVarType.Integer: defaultStr = "= " + v.DefaultInt; break;
                case EVarType.Boolean: defaultStr = v.DefaultBool ? "= true" : "= false"; break;
                case EVarType.String:  defaultStr = "= \"" + Truncate(v.DefaultString ?? "", 10) + "\""; break;
                case EVarType.Float:   defaultStr = "= " + v.DefaultFloat.ToString("0.##"); break;
                case EVarType.Choice:
                    int cnt = v.Choices?.Count ?? 0;
                    defaultStr = cnt == 0
                        ? ToolLang.Get("(no values)", "(нет значений)")
                        : "= " + Truncate(v.DefaultChoice ?? "", 8) + " · " + cnt + (ToolLang.IsRU ? " вар." : " opts");
                    break;
                case EVarType.List:
                    int lc = v.DefaultList?.Count ?? 0;
                    defaultStr = lc == 0
                        ? ToolLang.Get("[empty]", "[пусто]")
                        : "[" + lc + (ToolLang.IsRU ? " элем.]" : " items]");
                    break;
                default: defaultStr = ""; break;
            }
            var subStyle = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9,
                normal = { textColor = C_TEXT_4 }
            };
            GUI.Label(new Rect(badgeR.xMax + 4, subRect.y, subRect.width - 22, 14),
                defaultStr, subStyle);

            // Premium-звезда справа.
            if (isPremium)
            {
                var starSt = new GUIStyle(EditorStyles.label) {
                    alignment = TextAnchor.MiddleRight, fontSize = 11,
                    normal = { textColor = C_PREMIUM }
                };
                GUI.Label(new Rect(r.xMax - 22, r.y, 16, r.height), "★", starSt);
            }

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                HandleVariableClick(originalIndex, Event.current);
                Event.current.Use();
            }
        }

        // Multi-select клик: повторяет логику Characters HandleCharacterClick.
        private void HandleVariableClick(int idx, Event e)
        {
            // Сбрасываем edit-mode при смене выделения чтобы не «утек» в чужую переменную.
            _categoryEditingCustom = false;
            _categoryEditingDraft = "";

            if (e.shift && _lastClickedIndex >= 0 && _currentVisualOrder.Contains(_lastClickedIndex))
            {
                int startVis = _currentVisualOrder.IndexOf(_lastClickedIndex);
                int endVis = _currentVisualOrder.IndexOf(idx);
                int min = Mathf.Min(startVis, endVis);
                int max = Mathf.Max(startVis, endVis);

                _selectedIndices.Clear();
                for (int i = min; i <= max; i++)
                    _selectedIndices.Add(_currentVisualOrder[i]);
            }
            else if (e.control || e.command)
            {
                if (_selectedIndices.Contains(idx)) _selectedIndices.Remove(idx);
                else _selectedIndices.Add(idx);
                _lastClickedIndex = idx;
            }
            else
            {
                _selectedIndices.Clear();
                _selectedIndices.Add(idx);
                _lastClickedIndex = idx;
            }
            GUI.FocusControl(null);
            _window?.Repaint();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // EMPTY / MULTI states (стиль точно как у Characters)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawEmptyState(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            var st = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 14,
                normal = { textColor = C_TEXT_3 }
            };
            GUI.Label(rect, ToolLang.Get(
                "← Pick a variable on the left\nor create a new one",
                "← Выбери переменную слева\nили создай новую"), st);
        }

        private void DrawMultiSelectState(Rect rect, NovellaVariableSettings settings)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(400));

            var st = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold,
                normal = { textColor = C_TEXT_1 }
            };
            GUILayout.Label(string.Format(
                ToolLang.Get("{0} variables selected", "Выбрано переменных: {0}"),
                _selectedIndices.Count), st);

            // Подсчёт premium-валюты в выборке.
            int premiums = _selectedIndices.Count(i =>
                i >= 0 && i < settings.Variables.Count
                && settings.Variables[i].Type == EVarType.Integer
                && settings.Variables[i].IsPremiumCurrency);
            if (premiums > 0)
            {
                GUILayout.Space(10);
                var hst = new GUIStyle(EditorStyles.label) {
                    alignment = TextAnchor.MiddleCenter, fontSize = 14,
                    normal = { textColor = C_PREMIUM }
                };
                GUILayout.Label(string.Format(
                    ToolLang.Get("Includes {0} premium currency variable(s)!",
                                 "Внимание: включает {0} премиум-валюту/валют!"),
                    premiums), hst);
            }

            GUILayout.Space(30);

            GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f);
            if (GUILayout.Button("🗑 " + ToolLang.Get("Delete Selected", "Удалить выбранные"),
                GUILayout.Height(40)))
            {
                EditorApplication.delayCall += () => DeleteSelectedVariables(settings);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void DeleteSelectedVariables(NovellaVariableSettings settings)
        {
            int total = _selectedIndices.Count;
            int premiums = _selectedIndices.Count(i =>
                i >= 0 && i < settings.Variables.Count
                && settings.Variables[i].IsPremiumCurrency);

            string title = ToolLang.Get("Delete variables", "Удалить переменные");
            string msg = string.Format(
                ToolLang.Get("Delete {0} selected variables? Nodes that reference them will break.",
                             "Удалить {0} выбранные переменные? Ноды графа, ссылающиеся на них, сломаются."),
                total);
            if (premiums > 0)
                msg += "\n\n" + string.Format(
                    ToolLang.Get("Includes {0} premium currency variable(s)!",
                                 "Включает {0} премиум-валюту/валют!"), premiums);

            if (!EditorUtility.DisplayDialog(title, msg,
                ToolLang.Get("Delete", "Удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            Undo.RecordObject(settings, "Delete Variables");
            // Удаляем по убыванию индексов чтобы не сместить нижестоящие.
            var sorted = _selectedIndices.OrderByDescending(x => x).ToList();
            foreach (var i in sorted)
                if (i >= 0 && i < settings.Variables.Count)
                    settings.Variables.RemoveAt(i);

            _selectedIndices.Clear();
            _lastClickedIndex = -1;
            EditorUtility.SetDirty(settings);
            _window?.Repaint();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // DETAIL PANEL — single-variable editor
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawDetailPanel(Rect rect, NovellaVariableSettings settings, int idx)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            var v = settings.Variables[idx];

            GUILayout.BeginArea(rect);

            // ═══ STICKY TOP BAR — title + hints + actions (всегда видны, не скроллятся) ═══
            DrawTopActionBar(v, settings, idx);

            // ═══ Premium banner ═══
            if (v.Type == EVarType.Integer && v.IsPremiumCurrency)
            {
                DrawPremiumBanner(rect.width - 40);
            }

            // ═══ Scrollable form ═══
            _detailScroll = GUILayout.BeginScrollView(_detailScroll);

            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUILayout.BeginVertical();

            // Подсказка-объяснение «что такое переменная» (только при ShowGuide).
            DrawWhatIsAVariableHint();

            // === 1. Имя ===
            DrawSectionHeader("🏷  " + ToolLang.Get("NAME", "ИМЯ"));
            // Подсказка СНАЧАЛА — перед полем.
            DrawHint(ToolLang.Get(
                "Name should be in BIG LETTERS WITH UNDERSCORES (like PLAYER_GOLD), up to 25 characters. This is how nodes will reference the variable in your story.",
                "Имя пиши БОЛЬШИМИ БУКВАМИ ЧЕРЕЗ ПОДЧЁРКИВАНИЯ (например PLAYER_GOLD), до 25 символов. Так на переменную будут ссылаться ноды в истории."));
            GUILayout.Space(4);
            // Счётчик «N / 20» справа от лейбла + clamp ввода до 20 символов.
            DrawFieldLabel(ToolLang.Get("Name (key)", "Имя (ключ)"),
                v.Name?.Length ?? 0, MAX_VAR_NAME_LEN);
            string newName = DrawDarkTextField(v.Name ?? "", "VarName_" + idx);
            if (newName.Length > MAX_VAR_NAME_LEN) newName = newName.Substring(0, MAX_VAR_NAME_LEN);
            v.Name = newName;

            GUILayout.Space(6);
            DrawValidation(v, settings, idx);

            GUILayout.Space(14);

            // === Категория — 6 пресетов + «Своя» (с textfield ниже) ===
            DrawSectionHeader("📁  " + ToolLang.Get("CATEGORY", "КАТЕГОРИЯ"));
            DrawHint(ToolLang.Get(
                "Category just groups variables in the list on the left — it doesn't change anything in the game. Pick a preset or write your own.",
                "Категория просто группирует переменные в списке слева — на игру это никак не влияет. Выбери пресет или впиши свою."));
            GUILayout.Space(6);
            DrawCategoryPresets(v);

            GUILayout.Space(14);

            // === 2. Тип ===
            DrawSectionHeader("⚙  " + ToolLang.Get("TYPE", "ТИП"));
            DrawHint(ToolLang.Get(
                "Type tells the engine what kind of value this variable holds — a number, a yes/no flag, a piece of text, etc. Pick the one that matches what you'll store.",
                "Тип говорит движку, что именно хранит переменная — число, флаг да-нет, кусок текста и т.д. Выбери тот, который подходит под то что будешь записывать."));
            GUILayout.Space(4);
            DrawTypeChips(v);
            // Premium currency имеет смысл только для числовых (Integer/Float).
            if (v.Type != EVarType.Integer && v.Type != EVarType.Float) v.IsPremiumCurrency = false;

            // Premium toggle — сразу под type-chips для числовых.
            if (v.Type == EVarType.Integer || v.Type == EVarType.Float)
            {
                GUILayout.Space(8);
                if (v.IsPremiumCurrency)
                    DrawHint(ToolLang.Get(
                        "A gold mark — just so you and your team see at a glance that this is real-money currency (gems, premium coins). It doesn't change how the variable works.",
                        "Золотая пометка — чтобы ты и команда сразу видели: это валюта за реальные деньги (кристаллы, премиум-монеты). На работу переменной никак не влияет."));
                DrawPremiumToggle(v);
            }

            GUILayout.Space(14);

            // === 3. Стартовое значение (СРАЗУ ПОД ТИПОМ — логика «выбрал тип → задал старт») ===
            DrawSectionHeader("🎯  " + ToolLang.Get("DEFAULT VALUE", "СТАРТОВОЕ ЗНАЧЕНИЕ"));
            DrawDefaultSection(v);

            GUILayout.Space(14);

            // === 4. Время жизни — после старта, отдельной секцией с своей подсказкой ===
            DrawSectionHeader("⏳  " + ToolLang.Get("LIFETIME", "ВРЕМЯ ЖИЗНИ"));
            // Динамическая подсказка под текущий выбор Local/Global.
            DrawHint(v.Scope == EVarScope.Local
                ? ToolLang.Get(
                    "🕒 Chapter only — the value lives only inside one chapter. The moment the player leaves the chapter, it resets to the starting one. Good for short flags like «met Anna today».",
                    "🕒 Только в главе — значение живёт только внутри одной главы. Как только игрок выходит из главы, всё сбрасывается на стартовое. Подходит для коротких флагов вроде «встретил Анну сегодня».")
                : ToolLang.Get(
                    "💾 Whole game — the value is remembered forever, even after the player closes the game and opens it again. Use it for things that should live through the whole story: gold, stats, big plot flags.",
                    "💾 На всю игру — значение запоминается навсегда, даже если игрок закроет игру и откроет заново. Используй для того, что должно жить через всю историю: золото, статы, важные флаги сюжета."));
            GUILayout.Space(4);
            DrawScopeSegments(v);

            GUILayout.Space(14);

            // === 4. Иконка — компактно: thumbnail + кнопка Галереи + ✕ в одной строке ===
            DrawSectionHeader("🎨  " + ToolLang.Get("ICON (OPTIONAL)", "ИКОНКА (ОПЦИОНАЛЬНО)"));
            DrawHint(ToolLang.Get(
                "Optional picture for this variable. To show it in the game: in UI Forge add an Image element, bind this variable to it (Variable field) — the engine will auto-pick this icon. For value text, bind the same variable to a Text element with «{var}» placeholder (e.g. text «Gold: {var}» → «Gold: 42»).",
                "Необязательная картинка для переменной. Чтобы показать её в игре: в Кузнице UI добавь Image-элемент, привяжи к нему эту переменную (поле «Переменная») — движок сам подставит картинку. Для текста значения — привяжи ту же переменную к Text-элементу с плейсхолдером «{var}» (напр. текст «Золото: {var}» → «Золото: 42»)."));
            GUILayout.Space(4);
            DrawIconPicker(v);

            GUILayout.Space(14);

            // === 5. Заметки ===
            DrawSectionHeader("📝  " + ToolLang.Get("DEVELOPER NOTES", "ЗАМЕТКИ ДЛЯ СЕБЯ"));
            DrawHint(ToolLang.Get(
                "Notes are just for you and your team — write down what this variable means, where it changes, what to watch out for. Future-you will thank you when you come back to this story in a month.",
                "Заметки только для тебя и команды — запиши, что значит эта переменная, где она меняется, на что обратить внимание. Через месяц, когда вернёшься к истории, ты сам себе скажешь спасибо."));
            GUILayout.Space(4);
            DrawFieldLabel(ToolLang.Get("Notes (max 200 chars)", "Заметки (макс. 200 симв.)"),
                v.Description?.Length ?? 0, 200);
            var taSt = new GUIStyle(EditorStyles.textArea) {
                wordWrap = true, fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6)
            };
            string newDesc = EditorGUILayout.TextArea(v.Description ?? "", taSt, GUILayout.Height(60));
            if (newDesc.Length > 200) newDesc = newDesc.Substring(0, 200);
            v.Description = newDesc;

            GUILayout.Space(20);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(settings);

            GUILayout.EndVertical();
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ─── Sticky top bar — заголовок + hints toggle + Find references + Delete ───
        private void DrawTopActionBar(VariableDefinition v, NovellaVariableSettings settings, int idx)
        {
            GUILayout.Space(14);

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);

            // Title + meta стопкой слева.
            GUILayout.BeginVertical();
            DrawHeader(string.IsNullOrWhiteSpace(v.Name)
                ? ToolLang.Get("[ Unnamed variable ]", "[ Безымянная переменная ]")
                : v.Name);
            DrawMeta(string.Format(
                ToolLang.Get("Category: {0}", "Категория: {0}"),
                string.IsNullOrWhiteSpace(v.Category)
                    ? ToolLang.Get("Uncategorized", "Без категории") : v.Category));
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Action buttons + hints toggle справа.
            // Используем Layout для hints toggle (он рисуется через GUILayout),
            // а тяжёлые кнопки — DrawSlimActionBtn в стиле Characters Live Preview.
            GUILayout.BeginVertical();
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            DrawHintsToggle();
            GUILayout.Space(8);

            // Резервируем место под две action-кнопки и рисуем их вручную поверх.
            Rect findRect   = GUILayoutUtility.GetRect(150, 26, GUILayout.Width(150), GUILayout.Height(26));
            GUILayout.Space(8);
            Rect deleteRect = GUILayoutUtility.GetRect(110, 26, GUILayout.Width(110), GUILayout.Height(26));

            if (DrawSlimActionBtn(findRect,
                "🔍  " + ToolLang.Get("Where used", "Где используется"), false))
            {
                NovellaReferenceFinderWindow.ShowWindow(v.Name);
            }
            if (DrawSlimActionBtn(deleteRect,
                "🗑  " + ToolLang.Get("Delete", "Удалить"), false, danger: true))
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("Delete variable?", "Удалить переменную?"),
                    string.Format(ToolLang.Get(
                        "Permanently delete «{0}»?\n\nNodes that use it in any chapter will break.",
                        "Безвозвратно удалить «{0}»?\n\nНоды в главах, которые её используют, сломаются."), v.Name),
                    ToolLang.Get("Delete", "Удалить"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    Undo.RecordObject(settings, "Remove Variable");
                    settings.Variables.RemoveAt(idx);
                    _selectedIndices.Clear();
                    _lastClickedIndex = -1;
                    EditorUtility.SetDirty(settings);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            // Тонкий разделитель под top-bar'ом.
            Rect div = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(div, C_BORDER);
            GUILayout.Space(8);
        }

        // ─── Иконка: drag-zone в стиле Character Layers ───
        // Большой квадрат 80×80 с пунктирным бордером (когда пусто) —
        // принимает Drag&Drop из Project / клик открывает Галерею.
        private void DrawIconPicker(VariableDefinition v)
        {
            const float thumbSize = 80f;
            Rect row = GUILayoutUtility.GetRect(0, thumbSize, GUILayout.ExpandWidth(true));

            // Thumbnail слева 80×80.
            Rect thumb = new Rect(row.x, row.y, thumbSize, thumbSize);
            bool hasIcon = v.Icon != null;
            EditorGUI.DrawRect(thumb, C_BG_PRIMARY);

            bool dragOver = thumb.Contains(Event.current.mousePosition)
                            && DragAndDrop.objectReferences != null
                            && DragAndDrop.objectReferences.Length > 0
                            && (DragAndDrop.objectReferences[0] is Sprite
                                || DragAndDrop.objectReferences[0] is Texture2D);

            if (hasIcon)
            {
                DrawRectBorder(thumb, dragOver ? C_ACCENT
                                               : new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f));
                var tex = v.Icon.texture;
                if (tex != null)
                {
                    var sRect = v.Icon.rect;
                    var uv = new Rect(sRect.x / tex.width, sRect.y / tex.height,
                                      sRect.width / tex.width, sRect.height / tex.height);
                    float scale = Mathf.Min((thumb.width - 6) / sRect.width, (thumb.height - 6) / sRect.height);
                    float w = sRect.width * scale, h = sRect.height * scale;
                    Rect dst = new Rect(thumb.x + (thumb.width - w) * 0.5f,
                                        thumb.y + (thumb.height - h) * 0.5f, w, h);
                    GUI.DrawTextureWithTexCoords(dst, tex, uv);
                }
            }
            else
            {
                DrawRectBorderDashed(thumb, dragOver ? C_ACCENT : C_BORDER);
                var hintSt = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleCenter, fontSize = 9, wordWrap = true,
                    normal = { textColor = dragOver ? C_ACCENT : C_TEXT_4 }
                };
                GUI.Label(thumb, ToolLang.Get(
                    "drag\nimage\nhere",
                    "перетащи\nкартинку\nсюда"), hintSt);
            }

            // Drag&Drop обработка.
            if (Event.current.type == EventType.DragUpdated && thumb.Contains(Event.current.mousePosition))
            {
                DragAndDrop.visualMode = dragOver ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                Event.current.Use();
            }
            else if (Event.current.type == EventType.DragPerform && thumb.Contains(Event.current.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                if (DragAndDrop.objectReferences.Length > 0)
                {
                    var settings = NovellaVariableSettings.Instance;
                    Undo.RecordObject(settings, "Drop Icon");
                    var obj = DragAndDrop.objectReferences[0];
                    if (obj is Sprite sp) v.Icon = sp;
                    else if (obj is Texture2D t)
                    {
                        string p = AssetDatabase.GetAssetPath(t);
                        var spr = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                        if (spr != null) v.Icon = spr;
                    }
                    EditorUtility.SetDirty(settings);
                }
                Event.current.Use();
            }

            // Клик по thumbnail → открывает Галерею.
            if (Event.current.type == EventType.MouseDown && thumb.Contains(Event.current.mousePosition))
            {
                var capV = v;
                NovellaGalleryWindow.ShowWindow(asset =>
                {
                    var settings = NovellaVariableSettings.Instance;
                    Undo.RecordObject(settings, "Pick Icon");
                    if (asset is Sprite s) capV.Icon = s;
                    else if (asset is Texture2D t)
                    {
                        string p = AssetDatabase.GetAssetPath(t);
                        var spr = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                        capV.Icon = spr;
                    }
                    EditorUtility.SetDirty(settings);
                }, NovellaGalleryWindow.EGalleryFilter.Image);
                Event.current.Use();
            }

            // Справа — статус + кнопки управления (компактно, без длинного дропдауна).
            float infoX = thumb.xMax + 12;
            float infoW = row.width - thumb.width - 12;

            if (hasIcon)
            {
                // Имя выбранного спрайта (одна строка, обрезается).
                var nameSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_TEXT_1 }, clipping = TextClipping.Clip
                };
                GUI.Label(new Rect(infoX, row.y + 6, infoW, 18), v.Icon.name, nameSt);

                var subSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, normal = { textColor = C_TEXT_3 }
                };
                GUI.Label(new Rect(infoX, row.y + 24, infoW, 14),
                    ToolLang.Get("Picture is set", "Картинка задана"), subSt);

                // Кнопки: Replace + Clear.
                Rect replaceBtn = new Rect(infoX, row.y + 44, 110, 26);
                if (DrawSlimActionBtn(replaceBtn, "🔄  " + ToolLang.Get("Replace", "Заменить"), false))
                {
                    var capV = v;
                    NovellaGalleryWindow.ShowWindow(asset =>
                    {
                        var settings = NovellaVariableSettings.Instance;
                        Undo.RecordObject(settings, "Replace Icon");
                        if (asset is Sprite s) capV.Icon = s;
                        else if (asset is Texture2D t)
                        {
                            string p = AssetDatabase.GetAssetPath(t);
                            var spr = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                            capV.Icon = spr;
                        }
                        EditorUtility.SetDirty(settings);
                    }, NovellaGalleryWindow.EGalleryFilter.Image);
                }
                Rect clearBtn = new Rect(infoX + 116, row.y + 44, 90, 26);
                if (DrawSlimActionBtn(clearBtn, "✕  " + ToolLang.Get("Clear", "Убрать"), false, danger: true))
                {
                    var settings = NovellaVariableSettings.Instance;
                    Undo.RecordObject(settings, "Clear Icon");
                    v.Icon = null;
                    EditorUtility.SetDirty(settings);
                }
            }
            else
            {
                // Подсказка + одна кнопка «Из галереи».
                var hSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 10, wordWrap = true, normal = { textColor = C_TEXT_3 }
                };
                GUI.Label(new Rect(infoX, row.y + 6, infoW, 32),
                    ToolLang.Get(
                        "Drop a picture from Project,\nor pick one from Gallery.",
                        "Перетащи картинку из Project\nили выбери из Галереи."),
                    hSt);

                Rect pickBtn = new Rect(infoX, row.y + 44, 160, 26);
                if (DrawSlimActionBtn(pickBtn, "🖼  " + ToolLang.Get("From Gallery…", "Из Галереи…"), false))
                {
                    var capV = v;
                    NovellaGalleryWindow.ShowWindow(asset =>
                    {
                        var settings = NovellaVariableSettings.Instance;
                        Undo.RecordObject(settings, "Pick Icon");
                        if (asset is Sprite s) capV.Icon = s;
                        else if (asset is Texture2D t)
                        {
                            string p = AssetDatabase.GetAssetPath(t);
                            var spr = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                            capV.Icon = spr;
                        }
                        EditorUtility.SetDirty(settings);
                    }, NovellaGalleryWindow.EGalleryFilter.Image);
                }
            }
        }

        private void DrawValidation(VariableDefinition v, NovellaVariableSettings settings, int idx)
        {
            string n = v.Name ?? "";
            bool isEmpty = string.IsNullOrWhiteSpace(n);
            bool isDuplicate = !isEmpty && settings.Variables.Count(x => x.Name == n) > 1;
            bool hasSpaces = n.Contains(" ");
            bool isLowerCase = n.Any(char.IsLower);

            if (isEmpty)
                DrawWarn(ToolLang.Get("Name cannot be empty.", "Имя не может быть пустым."), C_ERROR);
            else if (isDuplicate)
                DrawWarn(ToolLang.Get("Duplicate name — this will break the game!",
                                      "Дубликат имени — это сломает игру!"), C_ERROR);
            else if (hasSpaces || isLowerCase)
            {
                DrawWarn(ToolLang.Get("Use UPPERCASE_WITH_UNDERSCORES (e.g. PLAYER_GOLD).",
                                      "Используй ЗАГЛАВНЫЕ_БУКВЫ_С_ПОДЧЁРКИВАНИЯМИ (напр. PLAYER_GOLD)."),
                          C_PREMIUM);
                if (GUILayout.Button("✨ " + ToolLang.Get("Auto-fix name", "Исправить автоматически"),
                    GUILayout.Height(24)))
                {
                    Undo.RecordObject(settings, "Fix Name");
                    string fixedN = Regex.Replace(n.Replace(" ", "_").ToUpper(), @"[^A-ZА-ЯЁ0-9_]", "");
                    if (fixedN.Length > MAX_VAR_NAME_LEN) fixedN = fixedN.Substring(0, MAX_VAR_NAME_LEN);
                    v.Name = fixedN;
                    GUI.FocusControl(null);
                }
            }
        }

        // ─── Type chips — все 6 типов как chip-pills ───
        // Если когда-нибудь типов станет >10 — стоит вернуть Popup или
        // ввести группировку. Пока 6 — комфортно умещаются в 2 ряда.
        private static (EVarType type, string emoji, string en, string ru)[] TypeChipDefs => new[]
        {
            (EVarType.Integer, "💠", "Number",       "Число"),
            (EVarType.Float,   "≈",  "Decimal",      "Дробное"),
            (EVarType.Boolean, "✓",  "True or False","Флаг да-нет"),
            (EVarType.String,  "📝", "Text",         "Текст"),
            (EVarType.Choice,  "🎯", "One of values","Из списка"),
            (EVarType.List,    "📋", "List",         "Список"),
        };

        private void DrawTypeChips(VariableDefinition v)
        {
            float availW = EditorGUIUtility.currentViewWidth - _sidebarWidth - 60;
            if (availW < 200) availW = 200;

            float gap = 8f;
            float chipH = 32f;
            float curX = 0, curY = 0;

            var stChip = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 0, 0)
            };

            // Precompute geometry чтобы понять сколько строк нужно.
            int totalRows = 1;
            float probeX = 0;
            for (int i = 0; i < TypeChipDefs.Length; i++)
            {
                var d = TypeChipDefs[i];
                string lbl = d.emoji + "  " + (ToolLang.IsRU ? d.ru : d.en);
                float w = stChip.CalcSize(new GUIContent(lbl)).x + 14;
                if (probeX + w > availW) { totalRows++; probeX = 0; }
                probeX += w + gap;
            }
            float blockH = totalRows * chipH + (totalRows - 1) * gap;
            Rect block = GUILayoutUtility.GetRect(0, blockH, GUILayout.ExpandWidth(true));

            for (int i = 0; i < TypeChipDefs.Length; i++)
            {
                int captured = i;
                var d = TypeChipDefs[i];
                string label = d.emoji + "  " + (ToolLang.IsRU ? d.ru : d.en);
                float w = stChip.CalcSize(new GUIContent(label)).x + 14;
                if (curX + w > availW)
                {
                    curX = 0;
                    curY += chipH + gap;
                }
                Rect r = new Rect(block.x + curX, block.y + curY, w, chipH);
                bool active = v.Type == d.type;
                DrawChip(r, label, active, () =>
                {
                    var s = NovellaVariableSettings.Instance;
                    Undo.RecordObject(s, "Set Variable Type");
                    v.Type = TypeChipDefs[captured].type;
                    EditorUtility.SetDirty(s);
                });
                curX += w + gap;
            }
        }

        // ─── Category presets — 6 готовых + «Своя» ───
        // Подобраны под визуальную новеллу: что РЕАЛЬНО хранят писатели в
        // переменных. Сюжет, романтика, статы (charisma/intelligence), концовки,
        // валюта (золото/кристаллы) и настройки игры. Inventory/Player убраны —
        // первое неактуально (нет инвентарной системы), второе слишком общее.
        private static (string en, string ru, string emoji)[] CategoryPresets => new[]
        {
            ("Story",     "Сюжет",     "📖"),  // флаги «встретил», «знает тайну»
            ("Romance",   "Романтика", "💗"),  // отношения с персонажами
            ("Stats",     "Статы",     "📊"),  // харизма, ум, удача
            ("Endings",   "Концовки",  "🏆"),  // флаги для разных финалов
            ("Currency",  "Валюта",    "💰"),  // золото, кристаллы
            ("Settings",  "Настройки", "⚙"),  // громкость, язык, скорость
        };

        private void DrawCategoryPresets(VariableDefinition v)
        {
            var settings = NovellaVariableSettings.Instance;
            string current = v.Category ?? "";

            // Сравнение текущей категории с встроенными пресетами.
            int activePreset = -1;
            for (int i = 0; i < CategoryPresets.Length; i++)
            {
                var p = CategoryPresets[i];
                if (string.Equals(current, p.en, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current, p.ru, System.StringComparison.OrdinalIgnoreCase))
                {
                    activePreset = i;
                    break;
                }
            }
            // Сравнение со списком custom-категорий проекта.
            int activeCustom = -1;
            if (activePreset == -1 && !string.IsNullOrWhiteSpace(current)
                && settings.CustomCategories != null)
            {
                for (int i = 0; i < settings.CustomCategories.Count; i++)
                {
                    if (string.Equals(current, settings.CustomCategories[i],
                                      System.StringComparison.OrdinalIgnoreCase))
                    {
                        activeCustom = i;
                        break;
                    }
                }
            }

            // ─── Ряд: 6 пресетов + custom-чипы + «+ Своя» chip / edit-row ───
            float availW = EditorGUIUtility.currentViewWidth - _sidebarWidth - 60;
            if (availW < 200) availW = 200;

            float gap = 8f;
            float chipH = 32f;
            float curX = 0, curY = 0;

            var stChip = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 0, 0)
            };

            // Edit-row фиксированной ширины.
            const float EDIT_ROW_W = 260f;

            int customCount = settings.CustomCategories?.Count ?? 0;
            int maxCustom = NovellaVariableSettings.MAX_CUSTOM_CATEGORIES;
            // Add-chip показываем только пока не достигли лимита.
            bool canAddMore = customCount < maxCustom;
            string addLabel = "✏  " + ToolLang.Get("Custom…", "Своя…");

            // Геометрия: считаем построчно.
            int totalRows = 1;
            float probeX = 0;
            // Предполагаем удаление - просто чтобы оценить размеры.
            System.Action<float> probeAdd = (w) => {
                if (probeX + w > availW) { totalRows++; probeX = 0; }
                probeX += w + gap;
            };
            for (int i = 0; i < CategoryPresets.Length; i++)
            {
                var p = CategoryPresets[i];
                string lbl = p.emoji + "  " + (ToolLang.IsRU ? p.ru : p.en);
                probeAdd(stChip.CalcSize(new GUIContent(lbl)).x + 14);
            }
            for (int i = 0; i < customCount; i++)
            {
                string lbl = "✏  " + Truncate(settings.CustomCategories[i] ?? "", 16);
                // +24 на ✕-кнопку справа от чипа.
                probeAdd(stChip.CalcSize(new GUIContent(lbl)).x + 14 + 24);
            }
            if (canAddMore)
            {
                probeAdd(_categoryEditingCustom
                    ? EDIT_ROW_W
                    : stChip.CalcSize(new GUIContent(addLabel)).x + 14);
            }

            float blockH = totalRows * chipH + (totalRows - 1) * gap;
            Rect block = GUILayoutUtility.GetRect(0, blockH, GUILayout.ExpandWidth(true));

            // Helper для расположения следующего chip с учётом переноса строки.
            System.Func<float, Rect> nextRect = (w) => {
                if (curX + w > availW)
                {
                    curX = 0;
                    curY += chipH + gap;
                }
                Rect r = new Rect(block.x + curX, block.y + curY, w, chipH);
                curX += w + gap;
                return r;
            };

            // ─── 6 встроенных пресетов ───
            for (int i = 0; i < CategoryPresets.Length; i++)
            {
                int captured = i;
                var p = CategoryPresets[i];
                string label = p.emoji + "  " + (ToolLang.IsRU ? p.ru : p.en);
                float w = stChip.CalcSize(new GUIContent(label)).x + 14;
                Rect r = nextRect(w);
                bool active = activePreset == captured;
                DrawChip(r, label, active, () =>
                {
                    var s = NovellaVariableSettings.Instance;
                    Undo.RecordObject(s, "Set Category");
                    v.Category = active
                        ? "" // toggle-off
                        : (ToolLang.IsRU ? CategoryPresets[captured].ru : CategoryPresets[captured].en);
                    EditorUtility.SetDirty(s);
                    _categoryEditingCustom = false;
                    _categoryEditingDraft = "";
                    GUI.FocusControl(null);
                });
            }

            // ─── Custom-категории (динамические, с ✕ удалением) ───
            // Удаление откладываем до конца цикла. DisplayDialog блокирует
            // OnGUI и мутация списка прямо в loop'е приводит к
            // ArgumentOutOfRangeException на следующей итерации (stale count).
            int customToRemove = -1;
            for (int i = 0; i < customCount; i++)
            {
                int captured = i;
                // Защитная проверка — на случай если список сократился
                // снаружи между фреймами (Undo / другой инспектор).
                if (i >= settings.CustomCategories.Count) break;

                string custom = settings.CustomCategories[i] ?? "";
                string label = "✏  " + Truncate(custom, 16);
                float w = stChip.CalcSize(new GUIContent(label)).x + 14;
                // Для custom-чипа резервируем 24px справа на ✕-кнопку.
                Rect totalR = nextRect(w + 24);
                Rect chipR = new Rect(totalR.x, totalR.y, w, totalR.height);
                Rect xR = new Rect(chipR.xMax + 0, totalR.y + 6, 24, 20);

                bool active = activeCustom == captured;
                DrawChip(chipR, label, active, () =>
                {
                    var s = NovellaVariableSettings.Instance;
                    Undo.RecordObject(s, "Set Custom Category");
                    if (captured >= 0 && captured < s.CustomCategories.Count)
                    {
                        v.Category = active ? "" : (s.CustomCategories[captured] ?? "");
                    }
                    EditorUtility.SetDirty(s);
                    _categoryEditingCustom = false;
                    _categoryEditingDraft = "";
                    GUI.FocusControl(null);
                });

                // Маленькая ✕-кнопка для удаления custom-категории из проекта.
                bool xHover = xR.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(xR, xHover
                    ? new Color(C_ERROR.r, C_ERROR.g, C_ERROR.b, 0.25f)
                    : new Color(0, 0, 0, 0));
                var xSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = xHover ? C_ERROR : C_TEXT_4 }
                };
                GUI.Label(xR, "✕", xSt);
                EditorGUIUtility.AddCursorRect(xR, MouseCursor.Link);
                if (Event.current.type == EventType.MouseDown && xHover)
                {
                    customToRemove = captured;
                    Event.current.Use();
                }
            }

            // Удаляем после цикла — список уже не трогается итератором.
            if (customToRemove >= 0 && customToRemove < settings.CustomCategories.Count)
            {
                string toRemove = settings.CustomCategories[customToRemove];
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("Remove custom category?", "Удалить свою категорию?"),
                    string.Format(ToolLang.Get(
                        "Remove «{0}» from the project's custom categories?\n\nVariables already using this category will keep the value as text — you can re-add the same category later.",
                        "Удалить «{0}» из списка своих категорий проекта?\n\nПеременные, которые её используют, сохранят значение текстом — потом всегда можно добавить такую же категорию заново."),
                        toRemove),
                    ToolLang.Get("Remove", "Удалить"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    Undo.RecordObject(settings, "Remove Custom Category");
                    settings.CustomCategories.RemoveAt(customToRemove);
                    EditorUtility.SetDirty(settings);
                    // Перерисовать с новым layout'ом сразу.
                    _window?.Repaint();
                }
            }

            // ─── «+ Своя» chip / edit-row (если ещё есть слот) ───
            if (canAddMore)
            {
                float lastW = _categoryEditingCustom
                    ? EDIT_ROW_W
                    : stChip.CalcSize(new GUIContent(addLabel)).x + 14;
                Rect lastR = nextRect(lastW);

                if (_categoryEditingCustom)
                {
                    DrawCustomCategoryEditRow(lastR, v, current);
                }
                else
                {
                    DrawChip(lastR, addLabel, false, () =>
                    {
                        _categoryEditingCustom = true;
                        _categoryEditingDraft = "";
                        EditorGUI.FocusTextInControl("VarCat_customEdit");
                    });
                }
            }
            else if (!_categoryEditingCustom)
            {
                // Достигнут лимит — мягкая подсказка под чипами.
                GUILayout.Space(4);
                var capSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, fontStyle = FontStyle.Italic,
                    normal = { textColor = C_TEXT_4 }
                };
                GUILayout.Label(string.Format(ToolLang.Get(
                    "Reached limit of {0} custom categories. Remove one with ✕ to add a new one.",
                    "Достигнут лимит — {0} своих категорий. Удали одну через ✕ чтобы добавить новую."),
                    maxCustom), capSt);
            }
        }

        // Inline edit-row: [textfield] [✓ save] [✕ cancel].
        // Появляется на месте «Своя» chip когда _categoryEditingCustom == true.
        private void DrawCustomCategoryEditRow(Rect r, VariableDefinition v, string current)
        {
            // Контейнер с акцентной рамкой — визуально отличается от чипов.
            EditorGUI.DrawRect(r, new Color(C_BG_RAISED.r, C_BG_RAISED.g, C_BG_RAISED.b, 0.7f));
            DrawRectBorder(r, C_ACCENT);

            const float BTN_W = 32f;
            const float PAD = 4f;

            // Textfield слева.
            Rect fldR = new Rect(r.x + PAD, r.y + 3, r.width - BTN_W * 2 - PAD * 4, r.height - 6);
            EditorGUI.DrawRect(fldR, C_BG_PRIMARY);

            GUI.SetNextControlName("VarCat_customEdit");
            var tfSt = new GUIStyle(EditorStyles.textField) {
                fontSize = 12, padding = new RectOffset(8, 8, 5, 5),
                normal  = { background = null, textColor = C_TEXT_1 },
                focused = { background = null, textColor = C_TEXT_1 },
                hover   = { background = null, textColor = C_TEXT_1 },
                active  = { background = null, textColor = C_TEXT_1 },
            };
            string newDraft = EditorGUI.TextField(fldR, _categoryEditingDraft ?? "", tfSt);
            if (newDraft != _categoryEditingDraft) _categoryEditingDraft = newDraft;

            // Placeholder когда драфт пуст и поле не в фокусе (редко — мы фокусим сразу).
            if (string.IsNullOrEmpty(_categoryEditingDraft) &&
                GUI.GetNameOfFocusedControl() != "VarCat_customEdit")
            {
                var phSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, normal = { textColor = C_TEXT_4 }
                };
                GUI.Label(new Rect(fldR.x + 8, fldR.y - 1, fldR.width, fldR.height),
                    ToolLang.Get("name…", "название…"), phSt);
            }

            // Enter — подтверждение, Esc — отмена.
            if (Event.current.type == EventType.KeyDown
                && GUI.GetNameOfFocusedControl() == "VarCat_customEdit")
            {
                if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    CommitCustomCategory(v);
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.Escape)
                {
                    _categoryEditingCustom = false;
                    _categoryEditingDraft = "";
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }

            // ✓ кнопка — подтвердить.
            Rect okR = new Rect(fldR.xMax + PAD, r.y + 3, BTN_W, r.height - 6);
            // Зелёная подсветка для visual-cue «save».
            bool okHover = okR.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(okR, okHover
                ? new Color(C_SUCCESS.r, C_SUCCESS.g, C_SUCCESS.b, 0.30f)
                : new Color(C_SUCCESS.r, C_SUCCESS.g, C_SUCCESS.b, 0.18f));
            DrawRectBorder(okR, C_SUCCESS);
            var okSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_SUCCESS }
            };
            GUI.Label(okR, "✓", okSt);
            EditorGUIUtility.AddCursorRect(okR, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && okHover)
            {
                CommitCustomCategory(v);
                Event.current.Use();
            }

            // ✕ кнопка — отмена.
            Rect cancelR = new Rect(okR.xMax + PAD, r.y + 3, BTN_W, r.height - 6);
            bool cancelHover = cancelR.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(cancelR, cancelHover
                ? new Color(C_ERROR.r, C_ERROR.g, C_ERROR.b, 0.25f)
                : new Color(1, 1, 1, 0.04f));
            DrawRectBorder(cancelR, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            var cancelSt = new GUIStyle(EditorStyles.label) {
                fontSize = 12, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = cancelHover ? C_ERROR : C_TEXT_3 }
            };
            GUI.Label(cancelR, "✕", cancelSt);
            EditorGUIUtility.AddCursorRect(cancelR, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && cancelHover)
            {
                _categoryEditingCustom = false;
                _categoryEditingDraft = "";
                GUI.FocusControl(null);
                Event.current.Use();
            }
        }

        private void CommitCustomCategory(VariableDefinition v)
        {
            var s = NovellaVariableSettings.Instance;
            string draft = (_categoryEditingDraft ?? "").Trim();

            // Пустой драфт — просто закрываем edit-mode без сохранения.
            if (string.IsNullOrEmpty(draft))
            {
                _categoryEditingCustom = false;
                _categoryEditingDraft = "";
                GUI.FocusControl(null);
                return;
            }

            // Если значение совпадает с встроенным пресетом (en/ru) — не плодим
            // дубль в CustomCategories, просто ставим как Category.
            bool isBuiltinPreset = false;
            for (int i = 0; i < CategoryPresets.Length; i++)
            {
                var p = CategoryPresets[i];
                if (string.Equals(draft, p.en, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(draft, p.ru, System.StringComparison.OrdinalIgnoreCase))
                {
                    isBuiltinPreset = true;
                    break;
                }
            }

            Undo.RecordObject(s, "Add Custom Category");

            // Если уже есть такая custom — повторно не добавляем.
            if (!isBuiltinPreset)
            {
                if (s.CustomCategories == null) s.CustomCategories = new List<string>();
                bool already = s.CustomCategories.Any(c =>
                    string.Equals(c, draft, System.StringComparison.OrdinalIgnoreCase));
                if (!already && s.CustomCategories.Count < NovellaVariableSettings.MAX_CUSTOM_CATEGORIES)
                {
                    s.CustomCategories.Add(draft);
                }
            }

            v.Category = draft;
            EditorUtility.SetDirty(s);

            _categoryEditingCustom = false;
            _categoryEditingDraft = "";
            GUI.FocusControl(null);
        }

        // Modern chip:
        //   • Inactive — едва заметный bg + 1px очень бледный border
        //   • Hover — мягкая подсветка bg-raised + cursor-link
        //   • Active — сплошной акцент-fill + белый текст + 2px сильная рамка
        //              + тонкая «свечение»-полоса сверху для эффекта объёма
        private void DrawChip(Rect r, string label, bool active, System.Action onClick)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            // Background — чёткая разница между состояниями.
            Color bg;
            if (active)
            {
                // Сплошной акцент-fill (вместо 22% alpha).
                bg = new Color(C_ACCENT.r * 0.92f, C_ACCENT.g * 0.92f, C_ACCENT.b * 0.92f);
            }
            else if (hover)
            {
                // Hover — слегка ярче чем bg, чуть-чуть в сторону акцента.
                bg = new Color(
                    Mathf.Lerp(C_BG_RAISED.r, C_ACCENT.r, 0.12f),
                    Mathf.Lerp(C_BG_RAISED.g, C_ACCENT.g, 0.12f),
                    Mathf.Lerp(C_BG_RAISED.b, C_ACCENT.b, 0.12f));
            }
            else
            {
                bg = new Color(C_BG_RAISED.r, C_BG_RAISED.g, C_BG_RAISED.b, 0.6f);
            }
            EditorGUI.DrawRect(r, bg);

            // Border — для inactive почти невидимый, для active — толстая акцентная.
            if (active)
            {
                // 2px рамка через две DrawRect.
                Color brd = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), brd);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), brd);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), brd);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), brd);
                // Внутренний highlight (1px белая полоса сверху для «объёма»).
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, 1),
                    new Color(1, 1, 1, 0.18f));
            }
            else
            {
                Color brd = hover
                    ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.45f)
                    : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.4f);
                DrawRectBorder(r, brd);
            }

            // Текст. Для активной — белый, для inactive — приглушённый/обычный.
            var st = new GUIStyle(EditorStyles.label) {
                fontSize = 11,
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = {
                    textColor = active
                        ? NovellaSettingsModule.GetContrastingText(C_ACCENT)
                        : (hover ? C_TEXT_1 : C_TEXT_2)
                }
            };
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && hover) _window?.Repaint();
        }

        // Большие кард-кнопки Local/Global — каждая с иконкой, заголовком,
        // описанием. Активная подсвечивается нативным цветом scope'а.
        private void DrawScopeSegments(VariableDefinition v)
        {
            Rect r = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            float halfW = (r.width - 6) * 0.5f;

            DrawScopeCard(
                new Rect(r.x, r.y, halfW, r.height),
                v.Scope == EVarScope.Local,
                C_LOCAL,
                "🕒",
                ToolLang.Get("Chapter only", "Только в главе"),
                ToolLang.Get("resets every entry", "сбрасывается при входе"),
                () => v.Scope = EVarScope.Local);

            DrawScopeCard(
                new Rect(r.x + halfW + 6, r.y, halfW, r.height),
                v.Scope == EVarScope.Global,
                C_GLOBAL,
                "💾",
                ToolLang.Get("Whole game", "На всю игру"),
                ToolLang.Get("saved forever", "сохраняется навсегда"),
                () => v.Scope = EVarScope.Global);
        }

        // Карточка-сегмент Scope с иконкой, заголовком и описанием.
        private void DrawScopeCard(Rect r, bool active, Color tint,
                                    string emoji, string title, string desc,
                                    System.Action onClick)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            Color bg = active ? new Color(tint.r, tint.g, tint.b, 0.18f)
                              : (hover ? new Color(1, 1, 1, 0.04f) : C_BG_PRIMARY);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, active ? tint : C_BORDER);
            // Цветной акцент-бар слева когда активна.
            if (active) EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), tint);

            // Эмодзи-иконка слева.
            var emSt = new GUIStyle(EditorStyles.label) {
                fontSize = 22, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = active ? tint : C_TEXT_3 }
            };
            GUI.Label(new Rect(r.x + 4, r.y, 36, r.height), emoji, emSt);

            // Заголовок (жирный).
            var titleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, alignment = TextAnchor.LowerLeft,
                normal = { textColor = active ? Color.white : C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 40, r.y + 6, r.width - 44, 18), title, titleSt);

            // Описание (мелкое).
            var descSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9, alignment = TextAnchor.UpperLeft,
                normal = { textColor = active ? new Color(tint.r, tint.g, tint.b, 0.95f) : C_TEXT_4 }
            };
            GUI.Label(new Rect(r.x + 40, r.y + 26, r.width - 44, 18), desc, descSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && hover) _window?.Repaint();
        }

        // Маленький badge для sidebar-строки. Показывает букву (L/G/Л/Г)
        // в цветной круглой подложке. Tooltip объясняет что это.
        private void DrawScopeBadge(Rect r, EVarScope scope, bool mini)
        {
            Color tint = scope == EVarScope.Local ? C_LOCAL : C_GLOBAL;
            string letter = scope == EVarScope.Local
                ? (ToolLang.IsRU ? "Л" : "L")
                : (ToolLang.IsRU ? "Г" : "G");

            EditorGUI.DrawRect(r, new Color(tint.r, tint.g, tint.b, 0.22f));
            DrawRectBorder(r, new Color(tint.r, tint.g, tint.b, 0.85f));

            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = mini ? 8 : 10, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tint }
            };
            GUI.Label(r, letter, st);

            // Tooltip — Unity автоматически показывает по hover для GUIContent.
            string tip = scope == EVarScope.Local
                ? ToolLang.Get("Local — chapter only, resets each entry",
                               "Локальная — только в главе, сбрасывается при каждом входе")
                : ToolLang.Get("Global — saved forever, persists between chapters",
                               "Глобальная — сохраняется навсегда, переживает смену глав");
            // Невидимый Box поверх для tooltip-hit области.
            GUI.Label(r, new GUIContent("", tip), GUIStyle.none);
        }

        private void DrawSegment(Rect r, string label, bool active, System.Action onClick)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.20f)
                              : (hover ? C_BG_RAISED : C_BG_PRIMARY);
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, active ? C_ACCENT : C_BORDER);

            var st = new GUIStyle(active ? EditorStyles.boldLabel : EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = active ? Color.white : C_TEXT_2 }
            };
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
        }

        private void DrawPremiumToggle(VariableDefinition v)
        {
            Rect r = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, v.IsPremiumCurrency ? C_PREMIUM : C_BORDER);

            Rect chk = new Rect(r.x + 10, r.y + 9, 14, 14);
            EditorGUI.DrawRect(chk, v.IsPremiumCurrency ? C_PREMIUM : C_BG_PRIMARY);
            DrawRectBorder(chk, v.IsPremiumCurrency ? C_PREMIUM : C_BORDER);
            if (v.IsPremiumCurrency)
            {
                var ck = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_BG_PRIMARY }
                };
                GUI.Label(chk, "✓", ck);
            }

            var labelSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = v.IsPremiumCurrency ? C_PREMIUM : C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 32, r.y, r.width - 32, r.height),
                "💎  " + ToolLang.Get("Premium currency (donation)", "Донат-валюта (премиум)"),
                labelSt);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                v.IsPremiumCurrency = !v.IsPremiumCurrency;
                Event.current.Use();
            }
        }

        private void DrawDefaultSection(VariableDefinition v)
        {
            if (v.Type == EVarType.Integer)
            {
                DrawFieldLabel(ToolLang.Get("Default (start) value", "Стартовое значение"), 0, 0);
                v.DefaultInt = EditorGUILayout.IntField(v.DefaultInt, GUILayout.Height(22));

                GUILayout.Space(8);

                // Подсказка про лимиты СНАЧАЛА — без слова «clamp», простыми словами.
                DrawHint(ToolLang.Get(
                    "Limits stop the number from going too low or too high — e.g. gold can't go below 0, health can't go above 100. The engine keeps the value inside the range automatically.",
                    "Лимиты не дают числу уйти слишком низко или слишком высоко — например, золото не уйдёт ниже 0, здоровье не превысит 100. Движок сам держит значение в этих рамках."));
                GUILayout.Space(4);
                DrawHasLimitsToggle(v);

                if (v.HasLimits)
                {
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    DrawFieldLabel(ToolLang.Get("Min", "Min"), 0, 0);
                    v.MinValue = EditorGUILayout.IntField(v.MinValue, GUILayout.Height(22));
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    DrawFieldLabel(ToolLang.Get("Max", "Max"), 0, 0);
                    v.MaxValue = EditorGUILayout.IntField(v.MaxValue, GUILayout.Height(22));
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    if (v.MinValue > v.MaxValue) v.MaxValue = v.MinValue;
                }
            }
            else if (v.Type == EVarType.Boolean)
            {
                DrawFieldLabel(ToolLang.Get("Default state", "Начальное состояние"), 0, 0);
                Rect r = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, C_BG_PRIMARY);
                DrawRectBorder(r, v.DefaultBool ? C_SUCCESS : C_BORDER);

                Rect chk = new Rect(r.x + 10, r.y + 9, 14, 14);
                EditorGUI.DrawRect(chk, v.DefaultBool ? C_SUCCESS : C_BG_PRIMARY);
                DrawRectBorder(chk, v.DefaultBool ? C_SUCCESS : C_BORDER);
                if (v.DefaultBool)
                {
                    var ck = new GUIStyle(EditorStyles.miniLabel) {
                        alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold,
                        normal = { textColor = C_BG_PRIMARY }
                    };
                    GUI.Label(chk, "✓", ck);
                }
                var labelSt = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = v.DefaultBool ? C_SUCCESS : C_TEXT_2 }
                };
                GUI.Label(new Rect(r.x + 32, r.y, r.width - 32, r.height),
                    v.DefaultBool ? ToolLang.Get("True (on)", "Истина (вкл)")
                                  : ToolLang.Get("False (off)", "Ложь (выкл)"),
                    labelSt);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    v.DefaultBool = !v.DefaultBool;
                    Event.current.Use();
                }
            }
            else if (v.Type == EVarType.String)
            {
                // Подсказка СНАЧАЛА — перед полем ввода.
                DrawHint(ToolLang.Get(
                    "Text is great for things the player types in (like a hero name) or short pieces of text shown in dialogues.",
                    "Текст удобен для того, что игрок вводит сам (например, имя героя) или для коротких кусочков текста, которые показываются в диалогах."));
                GUILayout.Space(4);
                DrawFieldLabel(ToolLang.Get("Default text", "Стартовый текст"), 0, 0);
                v.DefaultString = DrawDarkTextField(v.DefaultString ?? "", "VarDefStr");
            }
            else if (v.Type == EVarType.Float)
            {
                DrawHint(ToolLang.Get(
                    "Float is for numbers with decimals — like 0.75 luck multiplier or 1.5x XP rate. If you only need whole numbers, pick Integer instead.",
                    "Дробное число — для значений с долями: 0.75 множитель удачи, 1.5x опыта. Если нужны только целые — бери Integer."));
                GUILayout.Space(4);
                DrawFieldLabel(ToolLang.Get("Default (start) value", "Стартовое значение"), 0, 0);
                v.DefaultFloat = EditorGUILayout.FloatField(v.DefaultFloat, GUILayout.Height(22));

                GUILayout.Space(8);
                DrawHint(ToolLang.Get(
                    "Limits stop the number from going too low or too high — e.g. luck can't go below 0.0 or above 1.0. The engine keeps the value inside the range automatically.",
                    "Лимиты не дают числу уйти слишком низко или слишком высоко — напр. удача не уйдёт ниже 0.0 или выше 1.0. Движок сам держит значение в этих рамках."));
                GUILayout.Space(4);
                DrawHasLimitsToggle(v);

                if (v.HasLimits)
                {
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    DrawFieldLabel(ToolLang.Get("Min", "Min"), 0, 0);
                    v.MinFloat = EditorGUILayout.FloatField(v.MinFloat, GUILayout.Height(22));
                    GUILayout.EndVertical();
                    GUILayout.Space(8);
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    DrawFieldLabel(ToolLang.Get("Max", "Max"), 0, 0);
                    v.MaxFloat = EditorGUILayout.FloatField(v.MaxFloat, GUILayout.Height(22));
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    if (v.MinFloat > v.MaxFloat) v.MaxFloat = v.MinFloat;
                }
            }
            else if (v.Type == EVarType.Choice)
            {
                DrawHint(ToolLang.Get(
                    "Choice means the variable can hold ONE value out of a fixed list you define here. Great for relationship status, quest state, character class — clean alternative to magic numbers or strings.",
                    "Из списка — переменная хранит ОДНО значение из заранее заданного списка. Идеально для статуса отношений, состояния квеста, класса персонажа — чистая альтернатива магическим числам и строкам."));
                GUILayout.Space(6);

                DrawSubsectionLabel(ToolLang.Get("Allowed values", "Допустимые значения"));
                DrawStringListEditor(v.Choices, ToolLang.Get("e.g. friends, dating, married…",
                                                              "напр. друзья, влюблены, расстались…"),
                    "VarChoiceItem_", "Edit Choice", null);

                if (v.Choices == null || v.Choices.Count == 0)
                {
                    GUILayout.Space(4);
                    DrawWarn(ToolLang.Get(
                        "Add at least one value above — otherwise the variable can't hold anything.",
                        "Добавь хотя бы одно значение выше — иначе переменная не сможет хранить ничего."), C_ERROR);
                }
                else
                {
                    GUILayout.Space(10);
                    DrawSubsectionLabel(ToolLang.Get("Default value", "Стартовое значение"));

                    // Если default не из списка — выбираем первое.
                    int defIdx = v.Choices.IndexOf(v.DefaultChoice ?? "");
                    if (defIdx < 0) { defIdx = 0; v.DefaultChoice = v.Choices[0]; }
                    // Подменяем '/' на '-' (Popup не любит).
                    string[] safeChoices = v.Choices.Select(c => (c ?? "").Replace("/", "-")).ToArray();
                    int newIdx = EditorGUILayout.Popup(defIdx, safeChoices, GUILayout.Height(22));
                    if (newIdx != defIdx) v.DefaultChoice = v.Choices[newIdx];
                }
            }
            else if (v.Type == EVarType.List)
            {
                DrawHint(ToolLang.Get(
                    "List holds many items at once — great for inventory, visited cities, met characters. Nodes can add/remove items and check if something is inside.",
                    "Список хранит сразу много элементов — удобно для инвентаря, посещённых городов, встреченных персонажей. Ноды могут добавлять/убирать элементы и проверять есть ли что-то внутри."));
                GUILayout.Space(6);

                DrawSubsectionLabel(ToolLang.Get(
                    "Starting items (the list begins with these)",
                    "Стартовые элементы (с этого начинается список)"));
                DrawStringListEditor(v.DefaultList, ToolLang.Get("e.g. starter sword, healing potion…",
                                                                  "напр. стартовый меч, зелье лечения…"),
                    "VarListItem_", "Edit Default List", null);
            }
        }

        private void DrawSubsectionLabel(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9, fontStyle = FontStyle.Bold,
                normal = { textColor = C_TEXT_3 }
            };
            GUILayout.Label(text.ToUpperInvariant(), st);
            GUILayout.Space(4);
        }

        // Универсальный редактор List<string> — добавление / удаление / правка
        // строк. Используется для Choice.Choices и для List.DefaultList.
        // Когда элементов > 5 — содержимое идёт в scroll-view фиксированной
        // высоты, чтобы не растягивать панель.
        private void DrawStringListEditor(List<string> items, string placeholder,
                                          string controlPrefix, string undoLabel,
                                          System.Action<int> onItemChanged)
        {
            if (items == null) return;
            int toRemove = -1;
            const int MAX_VISIBLE = 5;
            const float ROW_H = 28f;
            const float ROW_GAP = 2f;

            // Если элементов больше 5 — оборачиваем в scroll-view.
            bool useScroll = items.Count > MAX_VISIBLE;
            if (useScroll)
            {
                if (!_stringListScrolls.TryGetValue(controlPrefix, out Vector2 scroll))
                    scroll = Vector2.zero;
                float scrollH = MAX_VISIBLE * (ROW_H + ROW_GAP) + 6;
                scroll = GUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar,
                    GUILayout.Height(scrollH));
                _stringListScrolls[controlPrefix] = scroll;

                // Счётчик «1 — 5 из 8» сверху (информативный).
                var cntSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, alignment = TextAnchor.MiddleRight,
                    normal = { textColor = C_TEXT_4 }
                };
                // Не рисуем здесь — счётчик логичнее показать ПОД scroll-view.
            }

            for (int i = 0; i < items.Count; i++)
            {
                Rect rowR = GUILayoutUtility.GetRect(0, ROW_H, GUILayout.ExpandWidth(true));

                // Order chip (1, 2, 3…) слева.
                Rect ordR = new Rect(rowR.x, rowR.y + 3, 22, 22);
                EditorGUI.DrawRect(ordR, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.15f));
                DrawRectBorder(ordR, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f));
                var ordSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 10, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_ACCENT }
                };
                GUI.Label(ordR, (i + 1).ToString(), ordSt);

                // Field. Резервируем правый край под scrollbar (если есть).
                float scrollPad = useScroll ? 16 : 0;
                float xBtn = rowR.xMax - 26 - scrollPad;
                Rect fldR = new Rect(ordR.xMax + 6, rowR.y, xBtn - ordR.xMax - 12, ROW_H);
                EditorGUI.DrawRect(fldR, C_BG_PRIMARY);
                DrawRectBorder(fldR, C_BORDER);

                GUI.SetNextControlName(controlPrefix + i);
                var tfSt = new GUIStyle(EditorStyles.textField) {
                    fontSize = 12, padding = new RectOffset(8, 8, 6, 6),
                    normal  = { background = null, textColor = C_TEXT_1 },
                    focused = { background = null, textColor = C_TEXT_1 }
                };
                Rect fldInner = new Rect(fldR.x + 2, fldR.y + 2, fldR.width - 4, fldR.height - 4);
                string newV = EditorGUI.TextField(fldInner, items[i] ?? "", tfSt);
                if (newV != items[i])
                {
                    var s = NovellaVariableSettings.Instance;
                    Undo.RecordObject(s, undoLabel);
                    items[i] = newV;
                    EditorUtility.SetDirty(s);
                    onItemChanged?.Invoke(i);
                }

                // Remove button.
                Rect rmR = new Rect(xBtn, rowR.y, 24, ROW_H);
                if (DrawSlimActionBtn(rmR, "✕", false, danger: true))
                {
                    toRemove = i;
                }

                GUILayout.Space(ROW_GAP);
            }

            if (useScroll)
            {
                GUILayout.EndScrollView();

                // Счётчик внизу — даёт юзеру уверенность что он не потерял элементы.
                var cntSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, alignment = TextAnchor.MiddleRight,
                    normal = { textColor = C_TEXT_4 }
                };
                GUILayout.Label(string.Format(
                    ToolLang.Get("{0} items · scroll to see all", "{0} элементов · прокрути чтобы увидеть все"),
                    items.Count), cntSt);
            }

            if (toRemove >= 0)
            {
                var s = NovellaVariableSettings.Instance;
                Undo.RecordObject(s, undoLabel);
                items.RemoveAt(toRemove);
                EditorUtility.SetDirty(s);
            }

            GUILayout.Space(6);

            // «+ Добавить» — пунктирная кнопка во всю ширину (как Add Layer у Characters).
            Rect addR = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(addR, C_BG_PRIMARY);
            DrawRectBorderDashed(addR, C_BORDER);
            var addSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_TEXT_3 }
            };
            string addLbl = items.Count == 0
                ? "+ " + ToolLang.Get("Add first value", "Добавить первое значение")
                : "+ " + ToolLang.Get("Add value", "Добавить значение");
            GUI.Label(addR, addLbl, addSt);
            if (Event.current.type == EventType.MouseDown && addR.Contains(Event.current.mousePosition))
            {
                var s = NovellaVariableSettings.Instance;
                Undo.RecordObject(s, undoLabel);
                items.Add("");
                EditorUtility.SetDirty(s);
                Event.current.Use();
                GUI.FocusControl(controlPrefix + (items.Count - 1));
                // Прокрутить scroll вниз чтобы увидеть свежедобавленный.
                if (items.Count > MAX_VISIBLE)
                {
                    _stringListScrolls[controlPrefix] = new Vector2(0, float.MaxValue);
                }
            }

            // Placeholder подсказка под полем когда список пуст.
            if (items.Count == 0)
            {
                var phSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9, alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = C_TEXT_4 }
                };
                GUILayout.Space(2);
                GUILayout.Label(placeholder, phSt);
            }
        }

        // Простой truncate: обрезает длинные строки до maxLen с многоточием.
        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen - 1) + "…";
        }

        private void DrawHasLimitsToggle(VariableDefinition v)
        {
            Rect r = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, v.HasLimits ? C_ACCENT : C_BORDER);

            Rect chk = new Rect(r.x + 10, r.y + 9, 14, 14);
            EditorGUI.DrawRect(chk, v.HasLimits ? C_ACCENT : C_BG_PRIMARY);
            DrawRectBorder(chk, v.HasLimits ? C_ACCENT : C_BORDER);
            if (v.HasLimits)
            {
                var ck = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold,
                    normal = { textColor = C_BG_PRIMARY }
                };
                GUI.Label(chk, "✓", ck);
            }
            var labelSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = v.HasLimits ? C_TEXT_1 : C_TEXT_2 }
            };
            GUI.Label(new Rect(r.x + 32, r.y, r.width - 32, r.height),
                ToolLang.Get("Keep value between Min and Max", "Удерживать значение между Min и Max"),
                labelSt);
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                v.HasLimits = !v.HasLimits;
                Event.current.Use();
            }
        }

        private void DrawPremiumBanner(float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect r = GUILayoutUtility.GetRect(width, 40, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(C_PREMIUM.r, C_PREMIUM.g, C_PREMIUM.b, 0.10f));
            DrawRectBorder(r, new Color(C_PREMIUM.r, C_PREMIUM.g, C_PREMIUM.b, 0.55f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_PREMIUM);

            var st = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 13, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_PREMIUM }
            };
            GUI.Label(r, "💎  " + ToolLang.Get("PREMIUM CURRENCY", "ДОНАТ-ВАЛЮТА"), st);
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Hints (показываются только когда NovellaSettingsModule.ShowGuide == true)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawHintsToggle()
        {
            bool guide = NovellaSettingsModule.ShowGuide;
            string text = "💡  " + (guide
                ? ToolLang.Get("Hints: On", "Подсказки: Вкл")
                : ToolLang.Get("Hints: Off", "Подсказки: Выкл"));

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = guide ? C_ACCENT : new Color(1, 1, 1, 0.05f);
            var st = new GUIStyle(EditorStyles.miniButton) {
                fontSize = 11, padding = new RectOffset(8, 8, 4, 4)
            };
            st.normal.textColor = guide
                ? NovellaSettingsModule.GetContrastingText(C_ACCENT)
                : C_TEXT_2;
            if (GUILayout.Button(text, st, GUILayout.Width(140), GUILayout.Height(22)))
            {
                NovellaSettingsModule.ShowGuide = !guide;
                _window?.Repaint();
            }
            GUI.backgroundColor = prevBg;
        }

        private void DrawWhatIsAVariableHint()
        {
            if (!NovellaSettingsModule.ShowGuide) return;

            string text = ToolLang.Get(
                "Variables are little memory cells the game uses to remember things — player money, choices, names. " +
                "Use them in your story: nodes can change them, check them, or pick a path based on them. " +
                "Pick Local for things that matter only inside one chapter, Global for things that should live through the whole game.",
                "Переменные — это маленькие ячейки памяти, в которых игра запоминает: деньги игрока, его выборы, имена. " +
                "Используй их в истории: ноды могут менять переменные, проверять их и выбирать путь в зависимости от значения. " +
                "Локальная — для вещей, важных только внутри одной главы. Глобальная — для всего, что должно жить через всю игру.");

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var st = new GUIStyle(EditorStyles.label) {
                fontSize = 11, wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8),
                normal = { textColor = NovellaSettingsModule.GetHintColor() }
            };
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.07f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
        }

        private void DrawHint(string text)
        {
            if (!NovellaSettingsModule.ShowGuide) return;
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                fontSize = 10,
                normal = { textColor = NovellaSettingsModule.GetHintColor() },
                padding = new RectOffset(8, 8, 5, 5)
            };
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.06f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.55f));
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(2);
        }

        private void DrawWarn(string text, Color accent)
        {
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = accent },
                padding = new RectOffset(10, 8, 6, 6)
            };
            Rect r = GUILayoutUtility.GetRect(new GUIContent("⚠ " + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(accent.r, accent.g, accent.b, 0.10f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), accent);
            GUI.Label(r, "⚠ " + text, st);
            GUILayout.Space(2);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Reusable UI bits (повторяют Characters helpers)
        // ═══════════════════════════════════════════════════════════════════════

        private void DrawHeader(string title)
        {
            var st = new GUIStyle(EditorStyles.label) {
                fontSize = 18, fontStyle = FontStyle.Bold,
                normal = { textColor = C_TEXT_1 }
            };
            GUILayout.Label(title, st);
        }

        private void DrawMeta(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10,
                normal = { textColor = C_TEXT_3 }
            };
            GUILayout.Label(text, st);
        }

        private void DrawSectionHeader(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = C_ACCENT }
            };
            GUILayout.Label(text, st);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BORDER);
            GUILayout.Space(8);
        }

        private void DrawFieldLabel(string text, int curLen, int maxLen)
        {
            GUILayout.BeginHorizontal();
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9, fontStyle = FontStyle.Bold,
                normal = { textColor = C_TEXT_3 }
            };
            GUILayout.Label(text, st);
            GUILayout.FlexibleSpace();
            if (maxLen > 0)
            {
                var cnt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9,
                    normal = {
                        textColor = curLen >= maxLen ? new Color(1f, 0.4f, 0.4f) : C_TEXT_4
                    }
                };
                GUILayout.Label($"{curLen} / {maxLen}", cnt);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private string DrawDarkTextField(string value, string controlName)
        {
            Rect r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, C_BORDER);

            GUI.SetNextControlName(controlName);
            var st = new GUIStyle(EditorStyles.textField) {
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6),
                normal  = { background = null, textColor = C_TEXT_1 },
                focused = { background = null, textColor = C_TEXT_1 },
                hover   = { background = null, textColor = C_TEXT_1 },
                active  = { background = null, textColor = C_TEXT_1 },
            };
            Rect inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            return EditorGUI.TextField(inner, value, st);
        }

        private void DrawIndentedLabel(string text, int indent, int fontSize, bool bold = false, Color color = default)
        {
            if (color == default) color = C_TEXT_2;
            var st = new GUIStyle(EditorStyles.label) {
                fontSize = fontSize,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                padding = new RectOffset(indent, 0, 0, 0),
                normal = { textColor = color }
            };
            GUILayout.Label(text, st);
        }

        private static void DrawDivider(Rect r) => EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.5f));

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        // Пунктирный border — дублирует Characters DrawRectBorderDashed.
        private static void DrawRectBorderDashed(Rect r, Color c)
        {
            int dash = 4, gap = 3;
            for (int x = 0; x < r.width; x += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x + x, r.y, Mathf.Min(dash, r.width - x), 1), c);
                EditorGUI.DrawRect(new Rect(r.x + x, r.yMax - 1, Mathf.Min(dash, r.width - x), 1), c);
            }
            for (int y = 0; y < r.height; y += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
            }
        }

        // Кнопка в стиле Characters Live-Preview action-bar (DrawActionBtn).
        // Тонкая обводка + hover-фон, без backgroundColor-хака.
        private bool DrawSlimActionBtn(Rect r, string label, bool fill, bool danger = false)
        {
            bool hovered = r.Contains(Event.current.mousePosition);
            Color border  = danger ? new Color(0.65f, 0.18f, 0.18f) : C_TEXT_1;
            Color textCol = danger ? new Color(0.88f, 0.30f, 0.30f) : C_TEXT_1;

            if (fill)
            {
                EditorGUI.DrawRect(r, hovered ? Color.white : C_TEXT_1);
                textCol = C_BG_PRIMARY;
            }
            else
            {
                if (hovered)
                {
                    EditorGUI.DrawRect(r, danger
                        ? new Color(0.65f, 0.18f, 0.18f, 0.13f)
                        : new Color(C_TEXT_1.r, C_TEXT_1.g, C_TEXT_1.b, 0.06f));
                }
                DrawRectBorder(r, border);
            }

            var st = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = textCol }
            };
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && hovered)
            {
                Event.current.Use();
                return true;
            }
            if (Event.current.type == EventType.MouseMove && hovered) _window?.Repaint();
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Logic helpers
        // ═══════════════════════════════════════════════════════════════════════

        private void CreateNewVariable(NovellaVariableSettings settings)
        {
            Undo.RecordObject(settings, "Add Variable");
            settings.Variables.Add(new VariableDefinition { Name = "NEW_VARIABLE" });
            int newIdx = settings.Variables.Count - 1;
            _selectedIndices.Clear();
            _selectedIndices.Add(newIdx);
            _lastClickedIndex = newIdx;
            _searchQuery = "";
            EditorUtility.SetDirty(settings);
            _window?.Repaint();
        }

        private bool HasNamingError(string name, NovellaVariableSettings settings, int currentIndex)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.Contains(" ") || name.Any(char.IsLower)) return true;
            for (int i = 0; i < settings.Variables.Count; i++)
                if (i != currentIndex && settings.Variables[i].Name == name) return true;
            return false;
        }
    }
}
