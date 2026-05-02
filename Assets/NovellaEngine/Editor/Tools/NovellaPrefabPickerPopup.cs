// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabPickerPopup — компактный выбиратель префаба из Gallery/Prefabs
// для вставки в сцену через ПКМ-меню Кузницы. Поиск + миниатюры + клавиатура.
// Скейлится на сотни префабов: список виртуально не нужен, фильтр режет.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaPrefabPickerPopup : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private List<GameObject> _all = new List<GameObject>();
        private string _filter = "";
        private Vector2 _scroll;
        private int _highlight = 0;
        private Action<GameObject> _onPick;
        private bool _focusedSearch;

        public static void Open(Vector2 screenPos, Action<GameObject> onPick)
        {
            // Single-instance: если уже открыт — закрываем старый и открываем
            // новый. Иначе при повторных кликах накапливался стек одинаковых
            // окон, который никто не закрывал.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaPrefabPickerPopup>())
            {
                if (existing != null) existing.Close();
            }

            var win = CreateInstance<NovellaPrefabPickerPopup>();
            win.titleContent = new GUIContent(ToolLang.Get("Pick prefab", "Выбрать префаб"));
            win._onPick = onPick;
            win.RefreshList();

            const float W = 360f, H = 380f;
            win.position = new Rect(screenPos.x - W * 0.5f, screenPos.y, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(W, H);
            // ShowUtility (а не ShowPopup) — попап-режим Unity самопроизвольно
            // закрывается при потере фокуса, что критически путает: юзер
            // случайно кликает мимо и окно пропадает. Utility держится пока
            // юзер сам не закроет его (Esc / выбор / крестик).
            win.ShowUtility();
            win.Focus();
        }

        private void RefreshList()
        {
            _all.Clear();
            string dir = NovellaPrefabHistory.PREFABS_DIR;
            if (!AssetDatabase.IsValidFolder(dir)) return;
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) _all.Add(go);
            }
            _all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void OnGUI()
        {
            // Esc должен закрывать окно ВСЕГДА — обрабатываем до того, как
            // TextField search'а съест keydown. SearchField забирает Esc как
            // «очистить поле», и наш HandleHotkeys ниже уже не получает event.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // Тонкая обводка вокруг попапа.
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
            GUILayout.Label("📦  " + ToolLang.Get("Insert prefab", "Вставить префаб"), titleSt);
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
            GUI.SetNextControlName("PickerSearch");
            string newFilter = GUI.TextField(sR, _filter, sSt);
            if (newFilter != _filter) { _filter = newFilter; _highlight = 0; }
            if (string.IsNullOrEmpty(_filter))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(sR.x + 6, sR.y, sR.width, sR.height),
                    "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }
            if (!_focusedSearch) { EditorGUI.FocusTextInControl("PickerSearch"); _focusedSearch = true; }

            GUILayout.Space(8);

            // Filtered list.
            string f = (_filter ?? "").Trim().ToLowerInvariant();
            var filtered = new List<GameObject>(_all.Count);
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i] == null) continue;
                if (f.Length == 0 || _all[i].name.ToLowerInvariant().Contains(f)) filtered.Add(_all[i]);
            }
            if (_highlight >= filtered.Count) _highlight = Mathf.Max(0, filtered.Count - 1);

            // Hotkeys.
            HandleHotkeys(filtered);

            // List.
            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (filtered.Count == 0)
            {
                GUILayout.Space(40);
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                emptySt.normal.textColor = C_TEXT_3;
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.Label(ToolLang.Get(
                    "No prefabs in Gallery/Prefabs.\nCreate one in the Forge first.",
                    "В Gallery/Prefabs пусто.\nСначала создай хоть один в Кузнице."), emptySt);
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

            // Footer hint.
            EditorGUI.DrawRect(new Rect(0, position.height - 24, position.width, 1), C_BORDER);
            var hintSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            hintSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(0, position.height - 22, position.width, 20),
                ToolLang.Get("↑↓ navigate · Enter — insert · Esc — close",
                             "↑↓ навигация · Enter — вставить · Esc — закрыть"), hintSt);
        }

        private void HandleHotkeys(List<GameObject> filtered)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    Close(); e.Use(); break;
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
            // Скролл к подсвеченному. Простая аппроксимация: 60px на ряд.
            const float ROW_H = 60f;
            float top = _highlight * ROW_H;
            float vh = position.height - 90f;
            if (top < _scroll.y) _scroll.y = top;
            else if (top + ROW_H > _scroll.y + vh) _scroll.y = top + ROW_H - vh;
        }

        private void DrawRow(GameObject prefab, int index, bool highlighted)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(6);
            Rect row = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            bool hover = row.Contains(Event.current.mousePosition);
            Color bg = highlighted
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f)
                : (hover ? new Color(1, 1, 1, 0.05f)
                         : (index % 2 == 0 ? new Color(1, 1, 1, 0.02f) : Color.clear));
            EditorGUI.DrawRect(row, bg);
            if (highlighted) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            // Thumb.
            Rect thumb = new Rect(row.x + 6, row.y + 4, 48, 48);
            EditorGUI.DrawRect(thumb, C_BG_RAISED);
            var preview = AssetPreview.GetAssetPreview(prefab);
            if (preview != null) GUI.DrawTexture(thumb, preview, ScaleMode.ScaleToFit, true);
            else
            {
                var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                iconSt.normal.textColor = C_TEXT_3;
                string ic = "▣";
                if (prefab.GetComponent<UnityEngine.UI.Button>() != null) ic = "🔘";
                else if (prefab.GetComponent<TMPro.TMP_Text>() != null) ic = "📝";
                else if (prefab.GetComponent<UnityEngine.UI.Image>() != null) ic = "🖼";
                GUI.Label(thumb, ic, iconSt);
            }

            // Name.
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.LowerLeft };
            nameSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(row.x + 60, row.y + 4, row.width - 70, 20), prefab.name, nameSt);

            string p = AssetDatabase.GetAssetPath(prefab);
            var pathSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            pathSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(row.x + 60, row.y + 26, row.width - 70, 14), p, pathSt);

            // Click.
            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                Pick(prefab);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseMove && hover)
            {
                _highlight = index;
                Repaint();
            }
        }

        private void Pick(GameObject prefab)
        {
            var cb = _onPick;
            Close();
            cb?.Invoke(prefab);
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
