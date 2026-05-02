// ════════════════════════════════════════════════════════════════════════════
// NovellaVariablePickerWindow
//
// Окно выбора переменной из NovellaVariableSettings. Группирует по типу
// (Integer / Boolean / String) — пользователь видит все доступные переменные
// в одном месте с подсказкой типа и иконкой.
//
// Возвращает имя переменной через callback. Если в проекте переменных нет —
// показывает понятное сообщение и кнопку открыть Hub → Variables.
// ════════════════════════════════════════════════════════════════════════════

using System;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor.UIBindings
{
    public class NovellaVariablePickerWindow : EditorWindow
    {
        private static Action<string> _callback;
        private string _selected;
        private string _search = "";
        private Vector2 _scroll;

        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();

        public static void Open(string current, Action<string> onPick)
        {
            var win = GetWindow<NovellaVariablePickerWindow>(true, "Novella · Pick variable", true);
            win._selected = current ?? "";
            win._search = "";
            _callback = onPick;
            win.minSize = new Vector2(520, 500);
            win.maxSize = new Vector2(900, 900);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(260, 250),
                new Vector2(520, 500));
            win.ShowUtility();
        }

        private void OnEnable() { wantsMouseMove = true; }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            DrawHeader(new Rect(0, 0, position.width, 64));

            float footerH = 50;
            DrawBody(new Rect(0, 64, position.width, position.height - 64 - footerH));
            DrawFooter(new Rect(0, position.height - footerH, position.width, footerH));

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.Return && !string.IsNullOrEmpty(_selected)) { Confirm(); Event.current.Use(); }
            }
        }

        private void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 200, 20), "🔧  Выбор переменной", t);

            // Поиск справа.
            float searchW = 220;
            var searchRect = new Rect(r.xMax - searchW - 14, r.y + 8, searchW, 22);
            _search = EditorGUI.TextField(searchRect, _search);
            if (string.IsNullOrEmpty(_search))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y + 2, searchRect.width, searchRect.height), "🔍 поиск по имени…", ph);
            }

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            sub.normal.textColor = C_TEXT_3;
            string subText = string.IsNullOrEmpty(_selected)
                ? "Переменные настраиваются в Hub → Переменные. Здесь только выбираешь нужную."
                : "Выбрано: " + _selected;
            GUI.Label(new Rect(r.x + 14, r.y + 32, r.width - 28, 18), subText, sub);
        }

        private void DrawBody(Rect r)
        {
            var settings = NovellaVariableSettings.Instance;
            if (settings == null || settings.Variables == null || settings.Variables.Count == 0)
            {
                DrawEmptyState(r);
                return;
            }

            GUILayout.BeginArea(r);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Space(6);

            // Группируем по Type.
            var byType = new System.Collections.Generic.Dictionary<EVarType, System.Collections.Generic.List<VariableDefinition>>();
            foreach (var v in settings.Variables)
            {
                if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                if (!string.IsNullOrEmpty(_search) && v.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!byType.TryGetValue(v.Type, out var list)) byType[v.Type] = list = new System.Collections.Generic.List<VariableDefinition>();
                list.Add(v);
            }

            if (byType.Count == 0)
            {
                var st = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Space(40);
                GUILayout.Label("Под фильтр ничего не попало.", st);
            }
            else
            {
                if (byType.TryGetValue(EVarType.Integer, out var ints) && ints.Count > 0) DrawGroup("ЧИСЛОВЫЕ (Integer)", "🔢", ints);
                if (byType.TryGetValue(EVarType.Boolean, out var bools) && bools.Count > 0) DrawGroup("ЛОГИЧЕСКИЕ (Boolean)", "🔘", bools);
                if (byType.TryGetValue(EVarType.String, out var strs) && strs.Count > 0) DrawGroup("СТРОКОВЫЕ (String)", "🔤", strs);
            }

            GUILayout.Space(8);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEmptyState(Rect r)
        {
            var ts = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            ts.normal.textColor = C_TEXT_2;
            var bs = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bs.normal.textColor = C_TEXT_3;

            float cx = r.x + r.width * 0.5f;
            float cy = r.y + r.height * 0.5f;
            GUI.Label(new Rect(cx - 220, cy - 28, 440, 22), "🪝  В проекте нет переменных", ts);
            GUI.Label(new Rect(cx - 240, cy + 0, 480, 50), "Открой Hub → Переменные и заведи хотя бы одну, потом возвращайся сюда.", bs);
        }

        private void DrawGroup(string title, string icon, System.Collections.Generic.List<VariableDefinition> vars)
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var hSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9 };
            hSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(title, hSt);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            for (int i = 0; i < vars.Count; i++)
            {
                DrawVariableCard(icon, vars[i]);
                GUILayout.Space(2);
            }
        }

        private void DrawVariableCard(string typeIcon, VariableDefinition v)
        {
            bool selected = _selected == v.Name;
            float h = selected ? 50f : 32f;

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect row = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            bool hover = row.Contains(Event.current.mousePosition);

            Color bg = selected ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                                : hover ? new Color(1f, 1f, 1f, 0.04f)
                                        : C_BG_RAISED;
            EditorGUI.DrawRect(row, bg);
            DrawBorder(row, selected ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));
            if (selected) EditorGUI.DrawRect(new Rect(row.x, row.y, 4, row.height), C_ACCENT);

            // Иконка типа + имя.
            var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = selected ? C_ACCENT : C_TEXT_2;
            GUI.Label(new Rect(row.x + 10, row.y, 28, row.height), typeIcon, iconSt);

            var nameSt = new GUIStyle(selected ? EditorStyles.boldLabel : EditorStyles.label) { fontSize = 12 };
            nameSt.normal.textColor = selected ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(row.x + 44, row.y + (selected ? 6 : 8), row.width - 90, 18), v.Name, nameSt);

            // Описание (категория или description). И значение по умолчанию у выбранного.
            if (selected)
            {
                var descSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
                descSt.normal.textColor = C_TEXT_3;
                string desc = !string.IsNullOrEmpty(v.Description) ? v.Description : ("Категория: " + (v.Category ?? "Default"));
                string defaultVal = v.Type switch
                {
                    EVarType.Integer => "по умолчанию: " + v.DefaultInt,
                    EVarType.Boolean => "по умолчанию: " + (v.DefaultBool ? "true" : "false"),
                    EVarType.String  => "по умолчанию: \"" + (v.DefaultString ?? "") + "\"",
                    _ => "",
                };
                GUI.Label(new Rect(row.x + 44, row.y + 26, row.width - 90, row.height - 26), desc + "  ·  " + defaultVal, descSt);
            }

            // Маркер «●»/«○».
            var markSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            markSt.normal.textColor = selected ? C_ACCENT : new Color(1, 1, 1, 0.18f);
            GUI.Label(new Rect(row.xMax - 30, row.y, 22, row.height), selected ? "●" : "○", markSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                _selected = v.Name;
                if (Event.current.clickCount >= 2) Confirm();
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawFooter(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            GUILayout.BeginHorizontal();

            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            hint.normal.textColor = C_TEXT_3;
            GUILayout.Label("⏎ — выбрать · ESC — отмена", hint);

            GUILayout.FlexibleSpace();

            if (NovellaSettingsModule.NeutralButton("Отмена", GUILayout.Width(100), GUILayout.Height(28)))
            {
                Close();
            }
            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selected)))
            {
                if (NovellaSettingsModule.AccentButton("Выбрать", GUILayout.Width(120), GUILayout.Height(28)))
                {
                    Confirm();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void Confirm()
        {
            _callback?.Invoke(_selected);
            Close();
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
