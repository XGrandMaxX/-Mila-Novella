// ════════════════════════════════════════════════════════════════════════════
// NovellaBindingsOverviewWindow
//
// Таблица всех NovellaUIBinding в активной сцене + счётчик «сколько нод графа
// ссылается на этот binding». Помогает аудит-стайл вопросам:
//   • Какие привязки никем не используются (счётчик 0 = мёртвый код, можно
//     убирать)?
//   • У каких binding'ов пусто в Loc-key / Variable / OnClick — не дописал?
//   • Где какой элемент привязан и как он называется?
//
// Источники использования сканируются по всем NovellaTree-ассетам в проекте:
//   • DialogueLine.UITextTargetId / UISpeakerTargetId
//   • NovellaChoice.UIButtonTargetId
//   • WaitNodeData.UITextTargetId
//   • SceneSettingsEvent.UITargetId
//
// Двойной клик / Enter — открывает элемент в иерархии Unity, пингует его и
// готов к редактированию в Кузнице UI.
// ════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;
using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    public class NovellaBindingsOverviewWindow : EditorWindow
    {
        private struct Row
        {
            public NovellaUIBinding Binding;
            public string Name;
            public string Kind; // icon string
            public int Uses;
        }

        private List<Row> _rows = new List<Row>();
        private Vector2 _scroll;
        private string _search = "";
        private bool _onlyUnused;
        private enum SortBy { Name, Kind, Uses }
        private SortBy _sortBy = SortBy.Uses;
        private bool _sortDescending = true;

        // Палитра — динамическая.
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();

        [MenuItem("Tools/Novella Engine/📋 UI Bindings Overview", false, 7)]
        public static void Open()
        {
            var w = GetWindow<NovellaBindingsOverviewWindow>(false, "Novella · Bindings", true);
            w.minSize = new Vector2(720, 420);
            w.Refresh();
            w.Show();
        }

        // ─── Refresh ────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _rows.Clear();
            var bindings = NovellaUIBinding.FindAllInScene();

            // Подсчитаем использования по всем NovellaTree в проекте.
            var usageCounts = CountAllUsages();

            foreach (var b in bindings)
            {
                if (b == null) continue;
                int uses = usageCounts.TryGetValue(b.Id, out var c) ? c : 0;
                _rows.Add(new Row
                {
                    Binding = b,
                    Name = b.DisplayName,
                    Kind = KindIcon(b),
                    Uses = uses,
                });
            }
            ApplySort();
        }

        private static Dictionary<string, int> CountAllUsages()
        {
            var counts = new Dictionary<string, int>();
            string[] guids = AssetDatabase.FindAssets("t:NovellaTree");
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(p);
                if (tree == null || tree.Nodes == null) continue;
                foreach (var node in tree.Nodes)
                {
                    if (node == null) continue;
                    if (node is DialogueNodeData dlg)
                    {
                        foreach (var line in dlg.DialogueLines)
                        {
                            Bump(counts, line.UITextTargetId);
                            Bump(counts, line.UISpeakerTargetId);
                        }
                    }
                    else if (node is BranchNodeData br)
                    {
                        foreach (var ch in br.Choices) Bump(counts, ch.UIButtonTargetId);
                    }
                    else if (node is WaitNodeData wt)
                    {
                        Bump(counts, wt.UITextTargetId);
                    }
                    else if (node is SceneSettingsNodeData ss)
                    {
                        foreach (var ev in ss.SceneEvents) Bump(counts, ev.UITargetId);
                    }
                }
            }
            return counts;
        }

        private static void Bump(Dictionary<string, int> counts, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            counts[id] = counts.TryGetValue(id, out var v) ? v + 1 : 1;
        }

        private void ApplySort()
        {
            _rows.Sort((a, b) =>
            {
                int cmp = _sortBy switch
                {
                    SortBy.Name => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase),
                    SortBy.Kind => string.Compare(a.Kind, b.Kind, System.StringComparison.Ordinal),
                    SortBy.Uses => a.Uses.CompareTo(b.Uses),
                    _ => 0,
                };
                return _sortDescending ? -cmp : cmp;
            });
        }

        // ─── GUI ────────────────────────────────────────────────────────────────

        private void OnEnable() { Refresh(); }
        private void OnFocus()  { Refresh(); }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            float headerH = 56;
            float guideH = NovellaSettingsModule.ShowGuide ? 92 : 0;

            DrawHeader(new Rect(0, 0, position.width, headerH));

            if (guideH > 0) DrawGuideTip(new Rect(8, headerH + 4, position.width - 16, guideH - 8));

            float tableHeaderY = headerH + guideH;
            DrawTableHeader(new Rect(0, tableHeaderY, position.width, 24));

            float footerH = 36;
            float tableY = tableHeaderY + 24;
            DrawTable(new Rect(0, tableY, position.width, position.height - tableY - footerH));
            DrawFooter(new Rect(0, position.height - footerH, position.width, footerH));
        }

        // Объясняющая плашка зачем эта таблица и что в ней. Включается
        // глобальным тогглом «💡 Подсказки» в любом из модулей Studio.
        private void DrawGuideTip(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.10f));
            DrawBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();
            string text =
                "💡  Здесь видны все UI-элементы сцены, которые ты «привязал» в Кузнице UI " +
                "(значит ноды графа могут писать в них текст / ставить переходы по клику / показывать-скрывать). " +
                "Колонка «Использован» считает сколько нод сослалось на этот элемент: 0 = мёртвая привязка, " +
                "которую можно убрать кнопкой «🗑 Удалить неиспользуемые». " +
                "Клик по строке — открывает Кузницу UI и пульсирует рамкой вокруг элемента, чтобы сразу было видно где он на холсте.";
            GUI.Label(r, text, st);
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 200, 18), "📋  Связи UI с историей", t);

            // Поиск
            float searchW = 220;
            var searchRect = new Rect(r.xMax - searchW - 14, r.y + 6, searchW, 22);
            _search = EditorGUI.TextField(searchRect, _search);
            if (string.IsNullOrEmpty(_search))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y + 2, searchRect.width, searchRect.height), "🔍 поиск по имени…", ph);
            }

            // Toggle "только неиспользуемые"
            var toggleRect = new Rect(r.x + 14, r.y + 30, 220, 18);
            EditorGUI.BeginChangeCheck();
            _onlyUnused = GUI.Toggle(toggleRect, _onlyUnused, "  показать только неиспользуемые");
            if (EditorGUI.EndChangeCheck()) Repaint();

            // Refresh — увеличил ширину чтобы «Обновить» не обрезалось.
            if (GUI.Button(new Rect(r.xMax - 116, r.y + 30, 102, 20), "↻ Обновить", EditorStyles.miniButton))
            {
                Refresh();
            }
        }

        private void DrawTableHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_RAISED);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            float[] colsW = ColumnWidths(r.width);
            float x = r.x + 8;
            DrawHeaderCell(new Rect(x, r.y, colsW[0], r.height), "Имя",       SortBy.Name); x += colsW[0];
            DrawHeaderCell(new Rect(x, r.y, colsW[1], r.height), "Тип",       SortBy.Kind); x += colsW[1];
            DrawHeaderCell(new Rect(x, r.y, colsW[2], r.height), "Loc-key",   null);        x += colsW[2];
            DrawHeaderCell(new Rect(x, r.y, colsW[3], r.height), "Variable",  null);        x += colsW[3];
            DrawHeaderCell(new Rect(x, r.y, colsW[4], r.height), "OnClick→",  null);        x += colsW[4];
            DrawHeaderCell(new Rect(x, r.y, colsW[5], r.height), "Использован", SortBy.Uses);
        }

        private void DrawHeaderCell(Rect r, string text, SortBy? sort)
        {
            var st = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = C_TEXT_2;
            string suffix = "";
            if (sort.HasValue && _sortBy == sort.Value) suffix = _sortDescending ? "  ▼" : "  ▲";
            GUI.Label(new Rect(r.x + 4, r.y, r.width - 8, r.height), text + suffix, st);

            if (sort.HasValue && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                if (_sortBy == sort.Value) _sortDescending = !_sortDescending;
                else { _sortBy = sort.Value; _sortDescending = sort == SortBy.Uses; }
                ApplySort();
                Event.current.Use();
            }
        }

        private static float[] ColumnWidths(float total)
        {
            // Name | Kind | Loc | Var | OnClick | Uses
            // Kind вмещает '🔘 Button', 'Использован' — иконку/число — фиксы;
            // остальное растягивается пропорционально.
            float kindW = 110, usesW = 110;
            float remaining = total - kindW - usesW - 16;
            if (remaining < 240) remaining = 240;
            float nameW = remaining * 0.30f;
            float locW  = remaining * 0.24f;
            float varW  = remaining * 0.22f;
            float clkW  = remaining * 0.24f;
            return new[] { nameW, kindW, locW, varW, clkW, usesW };
        }

        private void DrawTable(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_PRIMARY);

            // Фильтр. «Unused» считается мёртвой если граф НЕ ссылается И клик-action пустой.
            var filtered = _rows.Where(row =>
            {
                if (_onlyUnused && !IsUnused(row)) return false;
                if (string.IsNullOrEmpty(_search)) return true;
                return row.Name != null && row.Name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            if (filtered.Count == 0)
            {
                var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = C_TEXT_3;
                if (_rows.Count == 0)
                    GUI.Label(r, "В сцене нет привязок. Открой Кузницу UI, выбери элемент, нажми «➕ Сделать привязываемым».", st);
                else
                    GUI.Label(r, "Под фильтр ничего не попало.", st);
                return;
            }

            GUILayout.BeginArea(r);
            _scroll = GUILayout.BeginScrollView(_scroll);

            float rowH = 26f;
            float[] colsW = ColumnWidths(r.width);

            for (int i = 0; i < filtered.Count; i++)
            {
                var row = filtered[i];
                Rect rowRect = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));

                // Зебра + предупреждение для неиспользуемых.
                Color bg = i % 2 == 0 ? C_BG_PRIMARY : C_BG_SIDE;
                if (IsUnused(row)) bg = new Color(0.85f, 0.55f, 0.20f, 0.10f);
                bool hovered = rowRect.Contains(Event.current.mousePosition);
                if (hovered) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.13f);
                EditorGUI.DrawRect(rowRect, bg);

                float x = rowRect.x + 8;
                DrawCell(new Rect(x, rowRect.y, colsW[0], rowRect.height), row.Name, C_TEXT_1, true); x += colsW[0];
                DrawCell(new Rect(x, rowRect.y, colsW[1], rowRect.height), row.Kind, C_TEXT_2, false); x += colsW[1];
                DrawCell(new Rect(x, rowRect.y, colsW[2], rowRect.height), Or(row.Binding.LocalizationKey), C_TEXT_3, true); x += colsW[2];
                DrawCell(new Rect(x, rowRect.y, colsW[3], rowRect.height), Or(row.Binding.BoundVariable), C_TEXT_3, true); x += colsW[3];
                DrawCell(new Rect(x, rowRect.y, colsW[4], rowRect.height), Or(ActionLabel(row.Binding)), C_TEXT_3, true); x += colsW[4];
                bool unused = IsUnused(row);
                DrawCell(new Rect(x, rowRect.y, colsW[5], rowRect.height),
                    unused ? "⚠ 0" : row.Uses.ToString(),
                    unused ? new Color(0.95f, 0.66f, 0.30f) : C_TEXT_2, false);

                // Клик — открывает Кузницу UI на этом элементе и подсвечивает его
                // пульсирующей рамкой (как при выделении в графе/сценах).
                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    if (row.Binding != null && row.Binding.gameObject != null)
                    {
                        NovellaEngine.Editor.NovellaUIForge.PingBinding(row.Binding);
                    }
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawCell(Rect r, string text, Color color, bool clip)
        {
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            st.normal.textColor = color;
            if (clip) st.clipping = TextClipping.Clip;
            GUI.Label(new Rect(r.x + 4, r.y, r.width - 8, r.height), text, st);
        }

        private static string Or(string s) => string.IsNullOrEmpty(s) ? "—" : s;

        // Текст для колонки OnClick→. Если на binding'е ОДИН шаг — показываем
        // его иконку + параметр; если несколько — сокращённо «🎯 ▶ 🎵 +2 шага»
        // чтобы пользователь видел и порядок и количество. Multi-step совершенно
        // нормальный кейс для меню-кнопок.
        private static string ActionLabel(NovellaUIBinding b)
        {
            if (b == null || b.ClickSequence == null || b.ClickSequence.Count == 0) return "";
            if (b.ClickSequence.Count == 1)
            {
                return StepShortLabel(b.ClickSequence[0]);
            }
            // Multi-step — иконки подряд.
            string s = "";
            int max = System.Math.Min(4, b.ClickSequence.Count);
            for (int i = 0; i < max; i++)
            {
                var step = b.ClickSequence[i];
                s += StepIcon(step.Action) + " ";
            }
            if (b.ClickSequence.Count > max) s += "+" + (b.ClickSequence.Count - max);
            return s.Trim();
        }

        private static string StepShortLabel(NovellaUIBinding.ClickActionStep step)
        {
            string ic = StepIcon(step.Action);
            switch (step.Action)
            {
                case NovellaUIBinding.BindingAction.None:              return "";
                case NovellaUIBinding.BindingAction.GoToNode:          return ic + "  " + (string.IsNullOrEmpty(step.OnClickGotoNodeId) ? "(?)" : NodeLabel(step.OnClickGotoNodeId));
                case NovellaUIBinding.BindingAction.StartNewGame:      return ic + "  " + (step.StoryToStart != null ? step.StoryToStart.name : "(?)");
                case NovellaUIBinding.BindingAction.LoadLastSave:      return ic + "  Last save";
                case NovellaUIBinding.BindingAction.RestartChapter:    return ic + "  Restart chapter";
                case NovellaUIBinding.BindingAction.QuitGame:          return ic + "  Quit";
                case NovellaUIBinding.BindingAction.ShowPanel:         return ic + "  " + TargetLabel(step.TargetBindingId);
                case NovellaUIBinding.BindingAction.HidePanel:         return ic + "  " + TargetLabel(step.TargetBindingId);
                case NovellaUIBinding.BindingAction.TogglePanel:       return ic + "  " + TargetLabel(step.TargetBindingId);
                case NovellaUIBinding.BindingAction.SetVariable:       return ic + "  " + (string.IsNullOrEmpty(step.VariableName) ? "(?)" : step.VariableName);
                case NovellaUIBinding.BindingAction.TriggerEvent:      return ic + "  " + (string.IsNullOrEmpty(step.EventName) ? "(?)" : step.EventName);
                case NovellaUIBinding.BindingAction.UnlockAchievement: return ic + "  " + (string.IsNullOrEmpty(step.AchievementId) ? "(?)" : step.AchievementId);
                case NovellaUIBinding.BindingAction.PlaySFX:           return ic + "  " + (step.SfxClip != null ? step.SfxClip.name : "(?)");
                case NovellaUIBinding.BindingAction.ChangeLanguage:    return ic + "  " + (string.IsNullOrEmpty(step.LanguageCode) ? "(?)" : step.LanguageCode);
                case NovellaUIBinding.BindingAction.OpenURL:           return ic + "  " + (string.IsNullOrEmpty(step.URL) ? "(?)" : step.URL);
            }
            return ic;
        }

        private static string StepIcon(NovellaUIBinding.BindingAction a)
        {
            switch (a)
            {
                case NovellaUIBinding.BindingAction.GoToNode:          return "🎯";
                case NovellaUIBinding.BindingAction.StartNewGame:      return "▶";
                case NovellaUIBinding.BindingAction.LoadLastSave:      return "📥";
                case NovellaUIBinding.BindingAction.RestartChapter:    return "↻";
                case NovellaUIBinding.BindingAction.QuitGame:          return "🚪";
                case NovellaUIBinding.BindingAction.ShowPanel:         return "👁";
                case NovellaUIBinding.BindingAction.HidePanel:         return "🚫";
                case NovellaUIBinding.BindingAction.TogglePanel:       return "🔁";
                case NovellaUIBinding.BindingAction.SetVariable:       return "🔧";
                case NovellaUIBinding.BindingAction.TriggerEvent:      return "📡";
                case NovellaUIBinding.BindingAction.UnlockAchievement: return "🏆";
                case NovellaUIBinding.BindingAction.PlaySFX:           return "🎵";
                case NovellaUIBinding.BindingAction.ChangeLanguage:    return "🌐";
                case NovellaUIBinding.BindingAction.OpenURL:           return "🔗";
            }
            return "—";
        }

        private static string TargetLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(?)";
            var t = NovellaUIBinding.FindInScene(id);
            return t != null ? t.DisplayName : id.Substring(0, System.Math.Min(6, id.Length)) + "…";
        }

        private static string NodeLabel(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return "";
            string activeStoryGuid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            if (string.IsNullOrEmpty(activeStoryGuid)) return nodeId.Substring(0, System.Math.Min(8, nodeId.Length));
            var p = AssetDatabase.GUIDToAssetPath(activeStoryGuid);
            var story = AssetDatabase.LoadAssetAtPath<NovellaStory>(p);
            if (story == null || story.StartingChapter == null) return nodeId.Substring(0, System.Math.Min(8, nodeId.Length));
            var n = story.StartingChapter.Nodes.Find(nn => nn != null && nn.NodeID == nodeId);
            if (n == null) return nodeId.Substring(0, System.Math.Min(8, nodeId.Length));
            return string.IsNullOrEmpty(n.NodeTitle) ? n.NodeType.ToString() : n.NodeTitle;
        }

        private static string KindIcon(NovellaUIBinding b)
        {
            if (b.GetComponent<TMP_Text>() != null) return "📝 Text";
            if (b.GetComponent<Button>()   != null) return "🔘 Button";
            if (b.GetComponent<Image>()    != null) return "🖼 Image";
            return "▣ Other";
        }

        private void DrawFooter(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            int total = _rows.Count;
            int unused = _rows.Count(IsUnused);

            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            st.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x + 14, r.y + 9, r.width - 200, 18),
                $"Всего: {total} · неиспользуемых: {unused}", st);

            using (new EditorGUI.DisabledScope(unused == 0))
            {
                if (GUI.Button(new Rect(r.xMax - 200, r.y + 6, 186, 22),
                    new GUIContent("🗑  Удалить неиспользуемые", "Удалить все binding-компоненты с 0 использований."),
                    EditorStyles.miniButton))
                {
                    if (EditorUtility.DisplayDialog(
                            "Удалить неиспользуемые",
                            $"Удалить {unused} компонентов NovellaUIBinding которые ни разу не используются нодами графа?\n\nСами UI элементы (Text/Button/Image) останутся — пропадёт только привязка.",
                            "Удалить", "Отмена"))
                    {
                        RemoveUnused();
                    }
                }
            }
        }

        private void RemoveUnused()
        {
            int removed = 0;
            foreach (var row in _rows.ToList())
            {
                if (!IsUnused(row)) continue;
                if (row.Binding == null) continue;
                Undo.DestroyObjectImmediate(row.Binding);
                removed++;
            }
            Refresh();
            ShowNotification(new GUIContent($"Убрано связей: {removed}"));
        }

        // Binding считается «мёртвым» если на него не ссылается ни одна нода
        // графа И на нём нет click-action / локализации / переменной — нечего
        // вообще делать. Чисто текстовые binding'и с LocalizationKey
        // правильно считаются используемыми.
        private static bool IsUnused(Row row)
        {
            if (row.Binding == null) return true;
            if (row.Uses != 0) return false;
            var b = row.Binding;
            // Если задана хотя бы одна step-action — binding активен.
            if (b.ClickSequence != null)
                foreach (var s in b.ClickSequence)
                    if (s != null && s.Action != NovellaUIBinding.BindingAction.None) return false;
            if (b.ClickAction != NovellaUIBinding.BindingAction.None) return false;
            if (!string.IsNullOrEmpty(b.LocalizationKey)) return false;
            if (!string.IsNullOrEmpty(b.BoundVariable)) return false;
            return true;
        }
    }
}
