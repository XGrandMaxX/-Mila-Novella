// ════════════════════════════════════════════════════════════════════════════
// NovellaLocKeyPickerWindow
//
// Окно выбора ключа локализации. Показывает все ключи NovellaUILocalizationTable,
// сгруппированные по Category (или плоско если категорий нет). Поддерживает
// поиск по имени и фильтр по категории.
//
// Превью на каждой карточке: значение перевода для текущего языка инструмента.
// Выбранная карточка раскрывается и показывает все языки.
//
// Снизу — кнопка «Открыть редактор переводов» (открывает NovellaUILocalizationEditor
// сразу на выбранном ключе).
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor.UIBindings
{
    public class NovellaLocKeyPickerWindow : EditorWindow
    {
        private static Action<string> _callback;
        private string _selected;
        private string _search = "";
        private string _categoryFilter = ""; // "" = все
        private Vector2 _scroll;
        private NovellaUILocalizationTable _table;

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
            var win = GetWindow<NovellaLocKeyPickerWindow>(true, "Novella · Pick localization key", true);
            win._selected = current ?? "";
            win._search = "";
            win._categoryFilter = "";
            _callback = onPick;
            win.minSize = new Vector2(720, 640);
            win.maxSize = new Vector2(1200, 1000);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(360, 320),
                new Vector2(720, 640));
            win._table = NovellaEngine.Editor.NovellaUILocalizationEditor.GetOrCreateTable();
            win.ShowUtility();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            if (_table == null) _table = NovellaEngine.Editor.NovellaUILocalizationEditor.GetOrCreateTable();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            DrawHeader(new Rect(0, 0, position.width, 90));

            // Footer состоит из двух строк (создание ключа + кнопки подтверждения),
            // каждая 36px + 8px отступ — всё помещается без обрезки.
            float footerH = 84;
            DrawBody(new Rect(0, 90, position.width, position.height - 90 - footerH));
            DrawFooter(new Rect(0, position.height - footerH, position.width, footerH));

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.Return && !string.IsNullOrEmpty(_selected)) { Confirm(); Event.current.Use(); }
            }
        }

        // ─── Header ─────────────────────────────────────────────────────────────

        private void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 200, 20), "🔑  Выбор ключа локализации", t);

            // Поиск справа (вверху).
            float searchW = 220;
            var searchRect = new Rect(r.xMax - searchW - 14, r.y + 8, searchW, 22);
            _search = EditorGUI.TextField(searchRect, _search);
            if (string.IsNullOrEmpty(_search))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y + 2, searchRect.width, searchRect.height), "🔍 поиск по ключу или тексту…", ph);
            }

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            sub.normal.textColor = C_TEXT_3;
            string subText = string.IsNullOrEmpty(_selected)
                ? "Пиши новый ключ или выбери существующий ниже."
                : "Выбрано: " + _selected;
            GUI.Label(new Rect(r.x + 14, r.y + 32, r.width - 28, 18), subText, sub);

            // Фильтр по категориям — chip-row.
            DrawCategoryFilter(new Rect(r.x + 14, r.y + 56, r.width - 28, 28));
        }

        private void DrawCategoryFilter(Rect r)
        {
            if (_table == null || _table.Entries == null) return;
            var cats = new HashSet<string>();
            foreach (var e in _table.Entries)
            {
                if (e == null) continue;
                cats.Add(string.IsNullOrEmpty(e.Category) ? "" : e.Category);
            }
            if (cats.Count == 0) return;

            float x = r.x;
            // «Все» chip первым.
            DrawCategoryChip(ref x, r.y, "Все", "", cats.Count);
            foreach (var c in cats)
            {
                if (c == "") continue; // (без категории) рисуем последним
                int count = CountByCategory(c);
                DrawCategoryChip(ref x, r.y, c, c, count);
            }
            // (без категории) — если такие есть.
            int noCat = CountByCategory("");
            if (noCat > 0) DrawCategoryChip(ref x, r.y, "(без категории)", "__none__", noCat);
        }

        private int CountByCategory(string cat)
        {
            int n = 0;
            foreach (var e in _table.Entries)
            {
                if (e == null) continue;
                string ec = e.Category ?? "";
                if (cat == "__none__" ? ec == "" : ec == cat) n++;
            }
            return n;
        }

        private void DrawCategoryChip(ref float x, float y, string label, string value, int count)
        {
            string text = label + "  " + count;
            var chipSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            float w = chipSt.CalcSize(new GUIContent(text)).x + 18;
            Rect r = new Rect(x, y, w, 22);
            bool active = _categoryFilter == value;
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.40f)
                              : hover ? new Color(1f, 1f, 1f, 0.06f) : C_BG_RAISED;
            EditorGUI.DrawRect(r, bg);
            DrawBorder(r, active ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            chipSt.normal.textColor = active ? C_ACCENT : C_TEXT_2;
            GUI.Label(r, text, chipSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                _categoryFilter = active ? "" : value;
                Event.current.Use();
                Repaint();
            }
            x += w + 4;
        }

        // ─── Body ───────────────────────────────────────────────────────────────

        private void DrawBody(Rect r)
        {
            if (_table == null || _table.Entries == null || _table.Entries.Count == 0)
            {
                DrawEmptyState(r);
                return;
            }

            GUILayout.BeginArea(r);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Space(6);

            // Группируем по Category.
            var byCat = new Dictionary<string, List<NovellaUILocalizationTable.Entry>>();
            foreach (var e in _table.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                if (!Matches(e)) continue;
                string c = string.IsNullOrEmpty(e.Category) ? "" : e.Category;
                if (!byCat.TryGetValue(c, out var list)) byCat[c] = list = new List<NovellaUILocalizationTable.Entry>();
                list.Add(e);
            }

            if (byCat.Count == 0)
            {
                var st = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Space(40);
                GUILayout.Label("Под фильтр ничего не попало.", st);
            }
            else
            {
                // Сортируем категории: сначала with-name по алфавиту, в конце пустая.
                var keys = new List<string>(byCat.Keys);
                keys.Sort((a, b) =>
                {
                    if (a == "" && b != "") return 1;
                    if (b == "" && a != "") return -1;
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });
                foreach (var c in keys) DrawGroup(string.IsNullOrEmpty(c) ? "БЕЗ КАТЕГОРИИ" : c.ToUpperInvariant(), byCat[c]);
            }

            GUILayout.Space(8);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool Matches(NovellaUILocalizationTable.Entry e)
        {
            if (e == null) return false;
            // Filter by category.
            if (!string.IsNullOrEmpty(_categoryFilter))
            {
                string ec = e.Category ?? "";
                if (_categoryFilter == "__none__") { if (ec != "") return false; }
                else if (ec != _categoryFilter) return false;
            }
            // Search by key OR translated value (any language).
            if (!string.IsNullOrEmpty(_search))
            {
                string s = _search;
                if (e.Key.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                foreach (var v in e.Values)
                {
                    if (v != null && !string.IsNullOrEmpty(v.Text) &&
                        v.Text.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
            return true;
        }

        private void DrawEmptyState(Rect r)
        {
            var ts = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            ts.normal.textColor = C_TEXT_2;
            var bs = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bs.normal.textColor = C_TEXT_3;

            float cx = r.x + r.width * 0.5f;
            float cy = r.y + r.height * 0.5f;
            GUI.Label(new Rect(cx - 240, cy - 32, 480, 22), "🔑  В таблице нет ключей", ts);
            GUI.Label(new Rect(cx - 240, cy, 480, 60), "Открой Settings → Локализация и заведи хотя бы один перевод, либо нажми «Создать ключ» внизу — будет добавлен новый ключ с указанным именем.", bs);
        }

        private void DrawGroup(string title, List<NovellaUILocalizationTable.Entry> entries)
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var hSt = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 10 };
            hSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(title + "  ·  " + entries.Count, hSt);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            entries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < entries.Count; i++)
            {
                DrawEntryCard(entries[i]);
                GUILayout.Space(2);
            }
        }

        private void DrawEntryCard(NovellaUILocalizationTable.Entry e)
        {
            bool selected = _selected == e.Key;
            string previewLang = NovellaLocalizationManager.CurrentLanguage;
            string preview = e.Get(previewLang);
            if (string.IsNullOrEmpty(preview) && previewLang != _table.DefaultLanguage) preview = e.Get(_table.DefaultLanguage);
            if (string.IsNullOrEmpty(preview)) preview = "(нет перевода)";

            // Высота: 38px компакт; в выбранном — раскрываем чтобы показать все языки.
            int extraLines = selected ? Math.Max(0, e.Values.Count - 1) : 0;
            float h = selected ? 50f + extraLines * 18f : 36f;

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect row = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true), GUILayout.Height(h));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            bool hover = row.Contains(Event.current.mousePosition);

            Color bg = selected ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                                : hover ? new Color(1f, 1f, 1f, 0.04f) : C_BG_RAISED;
            EditorGUI.DrawRect(row, bg);
            DrawBorder(row, selected ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));
            if (selected) EditorGUI.DrawRect(new Rect(row.x, row.y, 4, row.height), C_ACCENT);

            // Ключ + превью первой линии.
            var keySt = new GUIStyle(selected ? EditorStyles.boldLabel : EditorStyles.label) { fontSize = 12 };
            keySt.normal.textColor = selected ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(row.x + 12, row.y + (selected ? 6 : 8), row.width - 60, 18), "🔑  " + e.Key, keySt);

            var prevSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            prevSt.normal.textColor = C_TEXT_3;
            if (!selected)
            {
                GUI.Label(new Rect(row.x + 12, row.y + 20, row.width - 24, 14), "(" + previewLang + ")  " + preview, prevSt);
            }
            else
            {
                // Все языки компактно одной таблицей.
                int yBase = (int)(row.y + 26);
                foreach (var v in e.Values)
                {
                    if (v == null) continue;
                    var lt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                    lt.normal.textColor = string.IsNullOrEmpty(v.Text) ? new Color(0.95f, 0.66f, 0.30f) : C_TEXT_3;
                    string val = string.IsNullOrEmpty(v.Text) ? "(нет перевода)" : v.Text;
                    GUI.Label(new Rect(row.x + 14, yBase, 50, 16), v.LanguageID, lt);
                    GUI.Label(new Rect(row.x + 60, yBase, row.width - 70, 16), val, lt);
                    yBase += 18;
                }
            }

            // Маркер ●/○.
            var markSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            markSt.normal.textColor = selected ? C_ACCENT : new Color(1, 1, 1, 0.18f);
            GUI.Label(new Rect(row.xMax - 30, row.y, 22, 32), selected ? "●" : "○", markSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                _selected = e.Key;
                if (Event.current.clickCount >= 2) Confirm();
                Event.current.Use();
                Repaint();
            }
        }

        // ─── Footer ─────────────────────────────────────────────────────────────

        private void DrawFooter(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            // Row 1: новый ключ + создать + в редактор.
            Rect row1 = new Rect(r.x + 12, r.y + 10, r.width - 24, 28);
            DrawNewKeyRow(row1);

            // Row 2: cancel / confirm справа.
            Rect row2 = new Rect(r.x + 12, r.y + 46, r.width - 24, 28);
            DrawConfirmRow(row2);
        }

        private void DrawNewKeyRow(Rect row)
        {
            // TextField (с placeholder'ом).
            float textW = row.width - 280;
            Rect textRect = new Rect(row.x, row.y, textW, row.height);
            string newSel = EditorGUI.TextField(textRect, _selected ?? "");
            if (newSel != (_selected ?? "")) _selected = newSel;
            if (string.IsNullOrEmpty(_selected))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(textRect.x + 6, textRect.y + 4, textRect.width - 12, textRect.height), "название ключа (например ui.menu.play)", ph);
            }

            // Создать ключ.
            Rect createRect = new Rect(row.x + textW + 6, row.y, 140, row.height);
            bool canCreate = !string.IsNullOrEmpty(_selected) && _table != null && _table.FindEntry(_selected) == null;
            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUI.Button(createRect, "➕  Создать ключ"))
                {
                    if (_table != null) { _table.AddKey(_selected); UnityEditor.EditorUtility.SetDirty(_table); }
                    Repaint();
                }
            }

            // В редактор.
            Rect editRect = new Rect(row.x + textW + 6 + 140 + 6, row.y, 122, row.height);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selected)))
            {
                if (GUI.Button(editRect, "✏  В редактор"))
                {
                    NovellaEngine.Editor.NovellaUILocalizationEditor.ShowAtKey(_selected);
                }
            }
        }

        private void DrawConfirmRow(Rect row)
        {
            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            hint.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(row.x, row.y + 6, row.width - 220, 18),
                "⏎ — выбрать  ·  ESC — отмена", hint);

            float btnW = 100f;
            float spacing = 6f;
            Rect cancelRect = new Rect(row.xMax - btnW * 2 - spacing, row.y, btnW, row.height);
            Rect okRect     = new Rect(row.xMax - btnW, row.y, btnW, row.height);

            // Cancel
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = NovellaSettingsModule.GetButtonNeutralBg();
            if (GUI.Button(cancelRect, NovellaSettingsModule.ButtonStyleFor(GUI.skin.button, GUI.backgroundColor) != null
                ? new GUIContent("Отмена") : new GUIContent("Отмена")))
            {
                Close();
            }
            GUI.backgroundColor = prevBg;

            // Confirm
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selected)))
            {
                GUI.backgroundColor = C_ACCENT;
                if (GUI.Button(okRect, "Выбрать")) Confirm();
                GUI.backgroundColor = prevBg;
            }
        }

        private void Confirm() { _callback?.Invoke(_selected); Close(); }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
