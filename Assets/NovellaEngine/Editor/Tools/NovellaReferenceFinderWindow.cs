// ════════════════════════════════════════════════════════════════════════════
// NovellaReferenceFinderWindow — поиск использований переменной во всех
// деревьях диалогов. Дизайн повторяет NovellaStoryPickerPopup: Hub-палитра,
// единый header, поиск, сгруппированный scroll-list с карточками-рядами.
//
// Использование: NovellaReferenceFinderWindow.ShowWindow("PLAYER_GOLD")
// показывает окно с найденными нодами; клик по ряду открывает Editor и
// фокусирует ноду в графе.
// ════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaReferenceFinderWindow : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BG_SIDE  => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private string _targetVariable;
        private string _filter = "";
        private Vector2 _scroll;
        private int _highlight;
        private bool _focusedSearch;

        private struct RefEntry
        {
            public NovellaTree Tree;
            public string NodeID;
            public string NodeTitle;
            public string Context;
            public string ContextEmoji;
        }

        private List<RefEntry> _references = new List<RefEntry>();
        // Ряды-кандидаты на отображение после фильтра. Содержат либо
        // header-строку (Tree != null && IsHeader), либо сам RefEntry.
        private struct VisualRow
        {
            public bool IsHeader;
            public NovellaTree HeaderTree;
            public RefEntry Entry;
        }
        private List<VisualRow> _visualRows = new List<VisualRow>();

        public static void ShowWindow(string variableName)
        {
            // Single-instance: не плодим стек одинаковых окон.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaReferenceFinderWindow>())
                if (existing != null) existing.Close();

            var win = CreateInstance<NovellaReferenceFinderWindow>();
            win.titleContent = new GUIContent(ToolLang.Get("References", "Зависимости"));
            win._targetVariable = variableName ?? "";
            win.PerformSearch();

            const float W = 460f, H = 520f;
            // По центру экрана главного редактора.
            var mouse = GUIUtility.GUIToScreenPoint(Event.current != null && Event.current.mousePosition != Vector2.zero
                ? Event.current.mousePosition : new Vector2(Screen.currentResolution.width / 2f,
                                                             Screen.currentResolution.height / 2f));
            win.position = new Rect(mouse.x - W * 0.5f, mouse.y - H * 0.3f, W, H);
            win.minSize = new Vector2(W, H);
            win.ShowUtility();
            win.Focus();
        }

        private void PerformSearch()
        {
            _references.Clear();
            string[] guids = AssetDatabase.FindAssets("t:NovellaTree");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NovellaTree tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(path);
                if (tree == null) continue;

                foreach (var node in tree.Nodes)
                {
                    bool isUsed = false;
                    string ctx = "";
                    string emoji = "•";

                    if (node is VariableNodeData varData &&
                        varData.Variables.Any(v => v.VariableName == _targetVariable))
                    {
                        isUsed = true;
                        ctx = ToolLang.Get("Variable update", "Изменение значения");
                        emoji = "✏";
                    }
                    else if (node is ConditionNodeData condData &&
                             condData.Conditions.Any(c => c.Variable == _targetVariable))
                    {
                        isUsed = true;
                        ctx = ToolLang.Get("Condition check", "Проверка условия");
                        emoji = "❓";
                    }
                    else if (node is BranchNodeData branchData)
                    {
                        foreach (var ch in branchData.Choices)
                        {
                            if (ch.Conditions.Any(c => c.Variable == _targetVariable))
                            {
                                isUsed = true;
                                ctx = ToolLang.Get("Choice lock", "Блокировка варианта");
                                emoji = "🔒";
                                break;
                            }
                        }
                    }
                    else if (node is RandomNodeData rndData)
                    {
                        foreach (var ch in rndData.Choices)
                        {
                            if (ch.ChanceModifiers.Any(m => m.Variable == _targetVariable))
                            {
                                isUsed = true;
                                ctx = ToolLang.Get("Chance modifier", "Модификатор шанса");
                                emoji = "🎲";
                                break;
                            }
                        }
                    }

                    if (isUsed)
                    {
                        _references.Add(new RefEntry
                        {
                            Tree = tree,
                            NodeID = node.NodeID,
                            NodeTitle = string.IsNullOrEmpty(node.NodeTitle) ? "(no title)" : node.NodeTitle,
                            Context = ctx,
                            ContextEmoji = emoji
                        });
                    }
                }
            }
        }

        private void OnGUI()
        {
            // Esc — закрыть.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close(); Event.current.Use(); return;
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // Бордер по периметру.
            var bord = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.85f);
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 1), bord);
            EditorGUI.DrawRect(new Rect(0, position.height - 1, position.width, 1), bord);
            EditorGUI.DrawRect(new Rect(0, 0, 1, position.height), bord);
            EditorGUI.DrawRect(new Rect(position.width - 1, 0, 1, position.height), bord);

            // ─── Header ───
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, normal = { textColor = C_TEXT_1 }
            };
            GUILayout.Label("🔍  " + ToolLang.Get("Where this variable is used", "Где используется переменная"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            // Имя переменной выделенно.
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var nameSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 14, normal = { textColor = C_ACCENT }
            };
            GUILayout.Label(_targetVariable, nameSt);
            GUILayout.FlexibleSpace();
            // Счётчик найденных.
            var cntSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, normal = { textColor = C_TEXT_3 }
            };
            int total = _references.Count;
            string cntText = total == 0
                ? ToolLang.Get("not used anywhere", "нигде не используется")
                : string.Format(
                    ToolLang.Get("{0} usage(s) found", "найдено: {0}"),
                    total);
            GUILayout.Label(cntText, cntSt);
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ─── Search ───
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            Rect sR = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            EditorGUI.DrawRect(sR, C_BG_RAISED);
            DrawBorder(sR, C_BORDER);
            var sSt = new GUIStyle(EditorStyles.textField) {
                fontSize = 11, padding = new RectOffset(8, 8, 4, 4),
                normal = { background = null, textColor = C_TEXT_1 },
                focused = { background = null, textColor = C_TEXT_1 }
            };
            GUI.SetNextControlName("RefSearch");
            string nf = GUI.TextField(sR, _filter, sSt);
            if (nf != _filter) { _filter = nf; _highlight = 0; }
            if (string.IsNullOrEmpty(_filter))
            {
                var ph = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, normal = { textColor = C_TEXT_4 }
                };
                GUI.Label(new Rect(sR.x + 6, sR.y, sR.width, sR.height),
                    "🔍  " + ToolLang.Get("Filter by tree or node…", "Фильтр по дереву или ноде…"), ph);
            }
            if (!_focusedSearch) { EditorGUI.FocusTextInControl("RefSearch"); _focusedSearch = true; }

            GUILayout.Space(8);

            // Подготовим visual rows из фильтра.
            BuildVisualRows();

            // ─── List ───
            float footerH = 36f;
            float headerY = sR.yMax + 8f;
            float listH = position.height - headerY - footerH;

            HandleHotkeys();

            Rect listRect = new Rect(0, headerY, position.width, listH);
            GUI.BeginGroup(listRect);
            _scroll = GUI.BeginScrollView(new Rect(0, 0, listRect.width, listRect.height),
                _scroll,
                new Rect(0, 0, listRect.width - 16, ComputeContentHeight()),
                false, true);

            if (_visualRows.Count == 0)
            {
                var emptySt = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                    normal = { textColor = C_TEXT_3 }
                };
                Rect emptyR = new Rect(20, 30, listRect.width - 56, 80);
                GUI.Label(emptyR, _references.Count == 0
                    ? ToolLang.Get(
                        "Looks unused.\nNo node in any chapter reads or writes this variable yet.",
                        "Пока не используется.\nНи одна нода ни в одной главе не читает и не меняет эту переменную.")
                    : ToolLang.Get("No matches.", "Ничего не найдено."), emptySt);
            }
            else
            {
                float y = 4;
                for (int i = 0; i < _visualRows.Count; i++)
                {
                    var row = _visualRows[i];
                    if (row.IsHeader)
                    {
                        DrawTreeHeader(new Rect(8, y, listRect.width - 32, 22), row.HeaderTree);
                        y += 26;
                    }
                    else
                    {
                        DrawRefRow(new Rect(8, y, listRect.width - 32, 44), row.Entry, i, i == _highlight);
                        y += 48;
                    }
                }
            }

            GUI.EndScrollView();
            GUI.EndGroup();

            // ─── Footer (хоткеи) ───
            EditorGUI.DrawRect(new Rect(0, position.height - footerH, position.width, 1), C_BORDER);
            var hintSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_TEXT_4 }
            };
            GUI.Label(new Rect(0, position.height - footerH + 8, position.width, 20),
                ToolLang.Get(
                    "↑↓ navigate  ·  Enter — open node  ·  Esc — close",
                    "↑↓ навигация  ·  Enter — открыть ноду  ·  Esc — закрыть"), hintSt);
        }

        private void BuildVisualRows()
        {
            _visualRows.Clear();
            string f = (_filter ?? "").Trim().ToLowerInvariant();

            var grouped = _references
                .Where(r => f.Length == 0
                    || (r.Tree != null && r.Tree.name.ToLowerInvariant().Contains(f))
                    || (r.NodeTitle ?? "").ToLowerInvariant().Contains(f)
                    || (r.Context ?? "").ToLowerInvariant().Contains(f))
                .GroupBy(r => r.Tree)
                .OrderBy(g => g.Key != null ? g.Key.name : "");

            foreach (var g in grouped)
            {
                _visualRows.Add(new VisualRow { IsHeader = true, HeaderTree = g.Key });
                foreach (var r in g)
                    _visualRows.Add(new VisualRow { IsHeader = false, Entry = r });
            }

            if (_highlight >= _visualRows.Count) _highlight = Mathf.Max(0, _visualRows.Count - 1);
            // Подсвечивать сразу первый non-header.
            while (_highlight < _visualRows.Count && _visualRows[_highlight].IsHeader) _highlight++;
            if (_highlight >= _visualRows.Count) _highlight = 0;
        }

        private float ComputeContentHeight()
        {
            float h = 4;
            for (int i = 0; i < _visualRows.Count; i++)
                h += _visualRows[i].IsHeader ? 26 : 48;
            return h + 4;
        }

        private void DrawTreeHeader(Rect r, NovellaTree tree)
        {
            string name = tree != null ? tree.name : "(missing)";
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 0, 4, 0),
                normal = { textColor = C_TEXT_3 }
            };
            GUI.Label(r, "📂  " + name.ToUpperInvariant(), st);
            // Тонкая линия снизу.
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1),
                new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));
        }

        private void DrawRefRow(Rect r, RefEntry entry, int rowIndex, bool highlighted)
        {
            bool hover = r.Contains(Event.current.mousePosition);

            Color bg;
            if (highlighted) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f);
            else if (hover)  bg = new Color(1, 1, 1, 0.05f);
            else             bg = (rowIndex % 2 == 0) ? new Color(1, 1, 1, 0.02f) : Color.clear;
            EditorGUI.DrawRect(r, bg);
            if (highlighted) EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            // Иконка контекста (✏ ❓ 🔒 🎲) в круглой подложке.
            float iconSize = 28;
            Rect ic = new Rect(r.x + 8, r.y + (r.height - iconSize) / 2, iconSize, iconSize);
            EditorGUI.DrawRect(ic, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.12f));
            DrawBorder(ic, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f));
            var icSt = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 14,
                normal = { textColor = C_ACCENT }
            };
            GUI.Label(ic, entry.ContextEmoji, icSt);

            // Title — заголовок ноды (жирный).
            var tSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12, alignment = TextAnchor.LowerLeft,
                normal = { textColor = C_TEXT_1 }
            };
            GUI.Label(new Rect(ic.xMax + 10, r.y + 4, r.width - iconSize - 24, 18),
                entry.NodeTitle, tSt);

            // Subtitle — контекст использования.
            var subSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, alignment = TextAnchor.UpperLeft,
                normal = { textColor = C_TEXT_3 }
            };
            GUI.Label(new Rect(ic.xMax + 10, r.y + 22, r.width - iconSize - 24, 16),
                entry.Context, subSt);

            // Стрелка-указатель «открыть» справа.
            var arrSt = new GUIStyle(EditorStyles.label) {
                fontSize = 16, alignment = TextAnchor.MiddleRight,
                normal = { textColor = highlighted || hover ? C_ACCENT : C_TEXT_4 }
            };
            GUI.Label(new Rect(r.xMax - 28, r.y, 22, r.height), "›", arrSt);

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                OpenAndFocus(entry.Tree, entry.NodeID);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseMove && hover)
            {
                _highlight = rowIndex; Repaint();
            }
        }

        private void HandleHotkeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    do { _highlight++; }
                    while (_highlight < _visualRows.Count && _visualRows[_highlight].IsHeader);
                    if (_highlight >= _visualRows.Count) _highlight = _visualRows.Count - 1;
                    e.Use(); Repaint(); break;

                case KeyCode.UpArrow:
                    do { _highlight--; }
                    while (_highlight >= 0 && _highlight < _visualRows.Count && _visualRows[_highlight].IsHeader);
                    if (_highlight < 0) _highlight = 0;
                    e.Use(); Repaint(); break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlight >= 0 && _highlight < _visualRows.Count && !_visualRows[_highlight].IsHeader)
                    {
                        OpenAndFocus(_visualRows[_highlight].Entry.Tree, _visualRows[_highlight].Entry.NodeID);
                    }
                    e.Use(); break;
            }
        }

        private void OpenAndFocus(NovellaTree tree, string nodeID)
        {
            if (tree == null) return;
            NovellaGraphWindow.OpenGraphWindow(tree);
            var win = GetWindow<NovellaGraphWindow>("Novella Editor");
            win.FocusAndHighlightNode(nodeID);
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
