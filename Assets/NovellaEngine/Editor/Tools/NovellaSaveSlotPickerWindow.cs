// ════════════════════════════════════════════════════════════════════════════
// NovellaSaveSlotPickerWindow — popup-выбор слота сохранения для action'ов
// SaveGameSlot / LoadGameSlot.
//
// Раньше в инспекторе был голый IntField — юзер вводил «5» и не понимал
// какие слоты заняты, пустые, что в них лежит. Это окно показывает все слоты
// (auto + 1..9) как карточки в сетке 4 колонки, с превью текста, временем
// и кнопкой удаления для непустых слотов.
//
// Slot info берётся через NovellaSaveManager.GetAllSlots — то что игрок
// успел нагенерить во время последнего Play. Если активной истории нет —
// карточки идут без preview, только номера (юзер всё равно может выбрать).
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Data;
using NovellaEngine.Runtime;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaSaveSlotPickerWindow : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        public enum Mode { Save, Load }

        private Mode _mode;
        private int _currentSlot;
        private Action<int> _onPick;
        private string _activeStoryName;
        private List<NovellaSlotInfo> _slots = new List<NovellaSlotInfo>();
        private Vector2 _scroll;

        public static void Open(Vector2 screenPos, Mode mode, int currentSlot, Action<int> onPick)
        {
            // Single-instance — не плодим стек одинаковых окон.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaSaveSlotPickerWindow>())
                if (existing != null) existing.Close();

            var win = CreateInstance<NovellaSaveSlotPickerWindow>();
            win._mode = mode;
            win._currentSlot = currentSlot;
            win._onPick = onPick;
            win._activeStoryName = ResolveActiveStoryName();
            win.RefreshSlots();

            // Размер окна: 4×3 = 12 ячеек (1 авто + до 11 ручных), с запасом по высоте
            // на header + scroll если слотов будет больше.
            const float W = 480f, H = 460f;
            win.titleContent = new GUIContent(mode == Mode.Save
                ? ToolLang.Get("Pick slot to save", "Выбор слота для сохранения")
                : ToolLang.Get("Pick slot to load", "Выбор слота для загрузки"));
            win.position = new Rect(screenPos.x - W * 0.5f, screenPos.y, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(W, H);
            win.ShowUtility();
            win.Focus();
        }

        private static string ResolveActiveStoryName()
        {
            string guid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            if (string.IsNullOrEmpty(guid)) return "";
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return "";
            var s = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
            if (s == null) return "";
            // Слоты сохраняются по имени StoryTree (NovellaPlayer.StoryTree.name),
            // не по имени Story. Берём имя стартовой главы.
            return s.StartingChapter != null ? s.StartingChapter.name : "";
        }

        private void RefreshSlots()
        {
            _slots.Clear();
            if (string.IsNullOrEmpty(_activeStoryName))
            {
                // Без активной истории — генерим dummy-слоты (Empty), юзер всё
                // равно может выбрать номер.
                for (int s = 0; s <= NovellaSaveManager.MAX_SLOTS; s++)
                    _slots.Add(new NovellaSlotInfo { Slot = s, IsEmpty = true });
                return;
            }
            _slots = NovellaSaveManager.GetAllSlots(_activeStoryName) ?? new List<NovellaSlotInfo>();
            // Дополняем недостающие (если GetAllSlots возвращает только заполненные).
            var present = new HashSet<int>();
            foreach (var s in _slots) present.Add(s.Slot);
            for (int s = 0; s <= NovellaSaveManager.MAX_SLOTS; s++)
            {
                if (!present.Contains(s)) _slots.Add(new NovellaSlotInfo { Slot = s, IsEmpty = true });
            }
            _slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // ─── Header ─────────────────────────────────────────────
            // Заголовок window-bar'а уже содержит «Pick slot to save/load»,
            // поэтому в самом header'е не дублируем «Сохранить в слот».
            // Показываем только контекст (активная история) + короткий хинт справа.
            Rect header = new Rect(0, 0, position.width, 32);
            EditorGUI.DrawRect(header, C_BG_RAISED);
            EditorGUI.DrawRect(new Rect(0, header.yMax - 1, position.width, 1), C_BORDER);
            // cyan-акцент сверху
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 2),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f));

            string ctx = string.IsNullOrEmpty(_activeStoryName)
                ? "📚 " + ToolLang.Get("No active story — previews unavailable",
                                       "Нет активной истории — превью недоступно")
                : "📚 " + ToolLang.Get($"Story: {_activeStoryName}", $"История: {_activeStoryName}");
            var ctxSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_TEXT_3 }
            };
            GUI.Label(new Rect(14, 0, position.width - 200, 32), ctx, ctxSt);

            string actionHint = _mode == Mode.Save
                ? ToolLang.Get("Click slot to save", "Клик по слоту = сохранить")
                : ToolLang.Get("Click slot to load", "Клик по слоту = загрузить");
            var hintSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, alignment = TextAnchor.MiddleRight,
                normal = { textColor = C_TEXT_4 }
            };
            GUI.Label(new Rect(position.width - 220, 0, 200, 32), actionHint, hintSt);

            // ─── Slots grid (4 columns × N rows, скроллится если нужно) ──
            const int cols = 4;
            const float pad = 12f;
            const float gap = 8f;
            const float cellH = 88f;

            int rows = (_slots.Count + cols - 1) / cols;
            float gridY = header.yMax + pad;
            float gridAvailH = position.height - gridY - pad;
            // Учитываем scrollbar: cellW считаем от ширины ВНУТРИ scrollview
            // (минус scrollbar если нужен).
            bool needsScroll = (rows * cellH + (rows - 1) * gap) > gridAvailH;
            float scrollbarPad = needsScroll ? 14f : 0f;
            float cellW = (position.width - pad * 2 - gap * (cols - 1) - scrollbarPad) / cols;

            Rect viewport = new Rect(0, gridY, position.width, gridAvailH);
            Rect content = new Rect(0, 0, position.width - scrollbarPad,
                                    rows * cellH + Mathf.Max(0, rows - 1) * gap);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, needsScroll);

            for (int i = 0; i < _slots.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                Rect cell = new Rect(
                    pad + col * (cellW + gap),
                    row * (cellH + gap),
                    cellW, cellH);
                DrawSlotCard(cell, _slots[i]);
            }

            GUI.EndScrollView();
        }

        private void DrawSlotCard(Rect r, NovellaSlotInfo info)
        {
            bool isCurrent = info.Slot == _currentSlot;
            bool isAuto = info.Slot == 0;
            Event e = Event.current;
            bool hover = r.Contains(e.mousePosition);

            // Фон
            Color bg = isCurrent
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.20f)
                : (hover ? C_BG_RAISED : C_BG);
            EditorGUI.DrawRect(r, bg);
            // Border
            Color border = isCurrent ? C_ACCENT : C_BORDER;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);

            // Top header — название слота
            var nameSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isCurrent ? Color.white : C_TEXT_1 }
            };
            string slotName = isAuto
                ? "⚡ " + ToolLang.Get("Auto", "Автосейв")
                : "💾 " + ToolLang.Get($"Slot {info.Slot}", $"Слот {info.Slot}");
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 18), slotName, nameSt);

            // Preview text / Empty
            var prevSt = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                fontSize = 10,
                normal = { textColor = info.IsEmpty ? C_TEXT_4 : C_TEXT_3 },
                wordWrap = true
            };
            if (info.IsEmpty)
            {
                prevSt.fontStyle = FontStyle.Italic;
                GUI.Label(new Rect(r.x + 8, r.y + 24, r.width - 16, 38),
                    ToolLang.Get("Empty", "Пусто"), prevSt);
            }
            else
            {
                string preview = string.IsNullOrEmpty(info.PreviewText)
                    ? "(node: " + info.NodeID + ")"
                    : info.PreviewText;
                GUI.Label(new Rect(r.x + 8, r.y + 22, r.width - 16, 40), preview, prevSt);
            }

            // Timestamp
            if (!info.IsEmpty)
            {
                var tsSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9,
                    alignment = TextAnchor.LowerLeft,
                    normal = { textColor = C_TEXT_4 }
                };
                string ts = info.TimeUtc != default ? info.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : (info.Timestamp ?? "");
                GUI.Label(new Rect(r.x + 8, r.yMax - 16, r.width - 36, 14), "🕒 " + ts, tsSt);
            }

            // Delete button (только для non-empty)
            if (!info.IsEmpty)
            {
                Rect delRect = new Rect(r.xMax - 24, r.yMax - 22, 18, 18);
                bool delHover = delRect.Contains(e.mousePosition);
                EditorGUI.DrawRect(delRect, delHover
                    ? new Color(0.85f, 0.32f, 0.32f, 1f)
                    : new Color(0.50f, 0.20f, 0.20f, 0.6f));
                var dSt = new GUIStyle(EditorStyles.boldLabel) {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                GUI.Label(delRect, "✕", dSt);
                EditorGUIUtility.AddCursorRect(delRect, MouseCursor.Link);

                if (e.type == EventType.MouseDown && e.button == 0 && delHover)
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Delete slot?", "Удалить слот?"),
                        ToolLang.Get(
                            $"Permanently delete slot {info.Slot} of '{_activeStoryName}'?",
                            $"Безвозвратно удалить слот {info.Slot} истории «{_activeStoryName}»?"),
                        ToolLang.Get("Delete", "Удалить"),
                        ToolLang.Get("Cancel", "Отмена")))
                    {
                        NovellaSaveManager.DeleteSlot(_activeStoryName, info.Slot);
                        RefreshSlots();
                        Repaint();
                    }
                    e.Use();
                    return;
                }
            }

            // Click on card → pick.
            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                // Save в пустой слот / non-empty — оба нормально (overwrite).
                // Load из пустого слота не имеет смысла — блокируем с тостом.
                if (_mode == Mode.Load && info.IsEmpty)
                {
                    EditorUtility.DisplayDialog(
                        ToolLang.Get("Slot is empty", "Слот пустой"),
                        ToolLang.Get("Cannot load from an empty slot.", "Нельзя загрузить из пустого слота."),
                        "OK");
                    e.Use();
                    return;
                }

                _onPick?.Invoke(info.Slot);
                Close();
                e.Use();
                return;
            }
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
        }
    }
}
