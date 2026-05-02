// ════════════════════════════════════════════════════════════════════════════
// NovellaStoryPickerPopup — компактный popup-выбиратель активной истории
// с обложками и поиском. Заменяет старый GenericMenu в OpenStorySwitcher,
// который при 10+ историях превращался в трудно-читаемый длинный список.
// Стиль соответствует NovellaPrefabPickerPopup — единая визуальная семья.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaStoryPickerPopup : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private struct Entry
        {
            public string Guid;
            public NovellaStory Story;
        }

        private List<Entry> _all = new List<Entry>();
        private string _filter = "";
        private string _activeGuid = "";
        private Vector2 _scroll;
        private int _highlight;
        private Action<string> _onPick;
        private Action _onCreateNew;
        private bool _focusedSearch;

        public static void Open(Vector2 screenPos, string activeGuid,
                                Action<string> onPickGuid, Action onCreateNew)
        {
            // Single-instance: если попап уже открыт — переоткрываем (закрыть
            // старый, открыть новый). Не плодим стек одинаковых окон.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaStoryPickerPopup>())
            {
                if (existing != null) existing.Close();
            }

            var win = CreateInstance<NovellaStoryPickerPopup>();
            win.titleContent = new GUIContent(ToolLang.Get("Pick story", "Выбрать историю"));
            win._onPick = onPickGuid;
            win._onCreateNew = onCreateNew;
            win._activeGuid = activeGuid ?? "";
            win.RefreshList();

            const float W = 380f, H = 420f;
            win.position = new Rect(screenPos.x - W * 0.5f, screenPos.y, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(W, H);
            // ShowUtility — попап не закрывается при потере фокуса.
            win.ShowUtility();
            win.Focus();

            // Подсветить текущую историю по умолчанию.
            for (int i = 0; i < win._all.Count; i++)
            {
                if (win._all[i].Guid == win._activeGuid) { win._highlight = i; break; }
            }
        }

        private void RefreshList()
        {
            _all.Clear();
            string[] guids = AssetDatabase.FindAssets("t:NovellaStory");
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var st = AssetDatabase.LoadAssetAtPath<NovellaStory>(p);
                if (st == null) continue;
                _all.Add(new Entry { Guid = g, Story = st });
            }
            _all.Sort((a, b) =>
            {
                string at = string.IsNullOrEmpty(a.Story.Title) ? a.Story.name : a.Story.Title;
                string bt = string.IsNullOrEmpty(b.Story.Title) ? b.Story.name : b.Story.Title;
                return string.Compare(at, bt, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void OnGUI()
        {
            // Esc — всегда закрывает (до того как TextField съест keydown).
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // Тонкая обводка.
            var bord = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.85f);
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 1), bord);
            EditorGUI.DrawRect(new Rect(0, position.height - 1, position.width, 1), bord);
            EditorGUI.DrawRect(new Rect(0, 0, 1, position.height), bord);
            EditorGUI.DrawRect(new Rect(position.width - 1, 0, 1, position.height), bord);

            // Header.
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📖  " + ToolLang.Get("Switch story", "Переключить историю"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Search.
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            Rect sR = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            EditorGUI.DrawRect(sR, C_BG_RAISED);
            DrawBorder(sR, C_BORDER);
            var sSt = new GUIStyle(EditorStyles.textField) { fontSize = 11, padding = new RectOffset(8, 8, 4, 4) };
            sSt.normal.background = null; sSt.focused.background = null;
            sSt.normal.textColor = C_TEXT_1; sSt.focused.textColor = C_TEXT_1;
            GUI.SetNextControlName("StoryPickerSearch");
            string newFilter = GUI.TextField(sR, _filter, sSt);
            if (newFilter != _filter) { _filter = newFilter; _highlight = 0; }
            if (string.IsNullOrEmpty(_filter))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(sR.x + 6, sR.y, sR.width, sR.height),
                    "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }
            if (!_focusedSearch) { EditorGUI.FocusTextInControl("StoryPickerSearch"); _focusedSearch = true; }

            GUILayout.Space(8);

            // Filter.
            string f = (_filter ?? "").Trim().ToLowerInvariant();
            var filtered = new List<Entry>(_all.Count);
            for (int i = 0; i < _all.Count; i++)
            {
                var e = _all[i];
                if (e.Story == null) continue;
                string tt = string.IsNullOrEmpty(e.Story.Title) ? e.Story.name : e.Story.Title;
                if (f.Length == 0 || tt.ToLowerInvariant().Contains(f)) filtered.Add(e);
            }
            if (_highlight >= filtered.Count) _highlight = Mathf.Max(0, filtered.Count - 1);

            HandleHotkeys(filtered);

            // List.
            float footerH = 64f;
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar,
                                                GUILayout.Height(position.height - 80f - footerH));
            if (filtered.Count == 0)
            {
                GUILayout.Space(40);
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                emptySt.normal.textColor = C_TEXT_3;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.Label(_all.Count == 0
                    ? ToolLang.Get("No stories yet.\nCreate one below.",
                                   "Историй пока нет.\nСоздай первую кнопкой ниже.")
                    : ToolLang.Get("No matches.", "Ничего не найдено."), emptySt);
                GUILayout.Space(20);
                GUILayout.EndHorizontal();
            }
            else
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    DrawRow(filtered[i], i, i == _highlight);
                }
            }
            GUILayout.EndScrollView();

            // Footer: «+ Создать историю» + хоткеи.
            EditorGUI.DrawRect(new Rect(0, position.height - footerH, position.width, 1), C_BORDER);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("＋ " + ToolLang.Get("New story", "Новая история"), GUILayout.Height(28)))
            {
                var cb = _onCreateNew;
                Close();
                cb?.Invoke();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            hintSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(0, position.height - 22, position.width, 20),
                ToolLang.Get("↑↓ navigate · Enter — switch · Esc — close",
                             "↑↓ навигация · Enter — переключить · Esc — закрыть"), hintSt);
        }

        private void HandleHotkeys(List<Entry> filtered)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    _highlight = Mathf.Min(filtered.Count - 1, _highlight + 1);
                    EnsureVisible();
                    e.Use(); Repaint(); break;
                case KeyCode.UpArrow:
                    _highlight = Mathf.Max(0, _highlight - 1);
                    EnsureVisible();
                    e.Use(); Repaint(); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_highlight >= 0 && _highlight < filtered.Count) Pick(filtered[_highlight]);
                    e.Use(); break;
            }
        }

        private void EnsureVisible()
        {
            const float ROW_H = 64f;
            float top = _highlight * ROW_H;
            float vh = position.height - 144f;
            if (top < _scroll.y) _scroll.y = top;
            else if (top + ROW_H > _scroll.y + vh) _scroll.y = top + ROW_H - vh;
        }

        private void DrawRow(Entry entry, int index, bool highlighted)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(6);
            Rect row = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            bool isActive = entry.Guid == _activeGuid;
            bool hover = row.Contains(Event.current.mousePosition);

            Color bg;
            if (highlighted) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f);
            else if (isActive) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.10f);
            else if (hover) bg = new Color(1, 1, 1, 0.05f);
            else bg = (index % 2 == 0) ? new Color(1, 1, 1, 0.02f) : Color.clear;
            EditorGUI.DrawRect(row, bg);
            if (highlighted || isActive) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            // Cover thumbnail (либо иконка-плейсхолдер).
            Rect thumb = new Rect(row.x + 6, row.y + 4, 52, 52);
            EditorGUI.DrawRect(thumb, C_BG_RAISED);
            if (entry.Story.CoverImage != null && entry.Story.CoverImage.texture != null)
            {
                GUI.DrawTexture(thumb, entry.Story.CoverImage.texture, ScaleMode.ScaleAndCrop, true);
            }
            else
            {
                var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                iconSt.normal.textColor = C_TEXT_3;
                GUI.Label(thumb, "📖", iconSt);
            }

            // Title (жирный) + active-маркер.
            string title = string.IsNullOrEmpty(entry.Story.Title) ? entry.Story.name : entry.Story.Title;
            string suffix = isActive ? "  •  " + ToolLang.Get("active", "активная") : "";
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.LowerLeft };
            nameSt.normal.textColor = isActive ? C_ACCENT : C_TEXT_1;
            GUI.Label(new Rect(row.x + 64, row.y + 4, row.width - 70, 20), title + suffix, nameSt);

            // Description (1 строка, обрезается).
            string desc = entry.Story.Description ?? "";
            desc = desc.Replace("\r\n", " ").Replace('\n', ' ');
            if (desc.Length > 80) desc = desc.Substring(0, 79) + "…";
            var descSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            descSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(row.x + 64, row.y + 26, row.width - 70, 28), desc, descSt);

            // Click.
            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                Pick(entry);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseMove && hover)
            {
                _highlight = index;
                Repaint();
            }
        }

        private void Pick(Entry entry)
        {
            var cb = _onPick;
            string guid = entry.Guid;
            Close();
            cb?.Invoke(guid);
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
