using NovellaEngine.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Менеджер графов: просмотр всех NovellaTree (глав) в проекте, открытие в Graph Window,
    /// удаление неиспользуемых, batch-удаление всех. Открывается из Home → Library tools.
    /// Не модуль Hub'а — отдельное окно, чтобы не загромождать Home постоянно видимым списком.
    ///
    /// Туториал-графы (лежат в /Tutorials/) показываются в самом низу списка как read-only:
    /// другая иконка (🎓), приглушённый цвет, бейдж TUTORIAL, кнопка удаления заблокирована.
    /// Они не учитываются в счётчике "unused" и не попадают в batch-удаление.
    /// </summary>
    public class NovellaGraphsBrowserWindow : EditorWindow
    {
        // Палитра — та же что в Hub
        private static readonly Color C_BG_PRIMARY = new Color(0.075f, 0.078f, 0.106f);
        private static readonly Color C_BG_SIDE    = new Color(0.102f, 0.106f, 0.149f);
        private static readonly Color C_BG_RAISED  = new Color(0.122f, 0.129f, 0.184f);
        private static readonly Color C_BORDER     = new Color(0.165f, 0.176f, 0.243f);
        private static readonly Color C_ACCENT     = new Color(0.36f, 0.75f, 0.92f);
        private static readonly Color C_TEXT_1     = new Color(0.925f, 0.925f, 0.957f);
        private static readonly Color C_TEXT_2     = new Color(0.710f, 0.718f, 0.784f);
        private static readonly Color C_TEXT_3     = new Color(0.616f, 0.624f, 0.690f);
        private static readonly Color C_TEXT_4     = new Color(0.427f, 0.435f, 0.502f);
        private static readonly Color C_DANGER     = new Color(0.85f, 0.32f, 0.32f);
        private static readonly Color C_OK         = new Color(0.48f, 0.81f, 0.62f);
        private static readonly Color C_WARN       = new Color(0.96f, 0.76f, 0.43f);

        private struct GraphInfo
        {
            public NovellaTree Tree;
            public string Path;
            public int NodeCount;
            public bool IsUsedAsStarting;     // используется как Starting Chapter в какой-то истории
            public string UsedByStory;         // имя истории, если есть
            public bool IsTutorial;            // лежит в /Tutorials/ — read-only для пользователя
        }

        private List<GraphInfo> _graphs = new List<GraphInfo>();
        private string _search = "";
        private Vector2 _scroll;
        private EditorWindow _hubToRefresh; // вернуться к Hub'у на refresh

        public static void ShowWindow(EditorWindow hubToRefresh = null)
        {
            var win = GetWindow<NovellaGraphsBrowserWindow>(true,
                ToolLang.Get("Story Graphs", "Графы историй"), true);
            win.minSize = new Vector2(640, 480);
            win._hubToRefresh = hubToRefresh;
            win.RefreshData();
            win.ShowUtility();
        }

        private void OnEnable() => RefreshData();

        private void RefreshData()
        {
            _graphs.Clear();

            // Все NovellaTree-ассеты в проекте
            string[] guids = AssetDatabase.FindAssets("t:NovellaTree");

            // Сначала собираем словарь: какой граф используется как стартовая глава какой историей
            var startingMap = new Dictionary<NovellaTree, string>();
            foreach (var sg in AssetDatabase.FindAssets("t:NovellaStory"))
            {
                var st = AssetDatabase.LoadAssetAtPath<NovellaStory>(AssetDatabase.GUIDToAssetPath(sg));
                if (st != null && st.StartingChapter != null && !startingMap.ContainsKey(st.StartingChapter))
                    startingMap[st.StartingChapter] = string.IsNullOrEmpty(st.Title) ? st.name : st.Title;
            }

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path)) continue;

                var tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(path);
                if (tree == null) continue;

                // Туториал-графы НЕ скрываем — показываем как read-only с бейджем,
                // чтобы пользователь видел что это за ассеты и не удивлялся им в Project View
                bool isTutorial = path.Contains("/Tutorials/");

                var info = new GraphInfo
                {
                    Tree = tree,
                    Path = path,
                    NodeCount = tree.Nodes != null ? tree.Nodes.Count : 0,
                    IsUsedAsStarting = startingMap.ContainsKey(tree),
                    UsedByStory = startingMap.TryGetValue(tree, out var s) ? s : null,
                    IsTutorial = isTutorial,
                };
                _graphs.Add(info);
            }

            // Сортировка: пользовательские графы сначала (используемые → неиспользуемые),
            // туториал-графы в самый низ — они read-only и не должны мозолить глаза.
            _graphs = _graphs
                .OrderBy(g => g.IsTutorial)             // false (0) идёт раньше true (1)
                .ThenByDescending(g => g.IsUsedAsStarting)
                .ThenBy(g => g.Tree.name)
                .ToList();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            DrawHeader();
            DrawSearchAndActions();
            DrawList();
        }

        private void DrawHeader()
        {
            Rect head = new Rect(0, 0, position.width, 64);
            EditorGUI.DrawRect(head, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(0, head.yMax - 1, position.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(20, 12, position.width - 40, 22),
                "🗺  " + ToolLang.Get("Story Graphs", "Графы историй"), t);

            var s = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            s.normal.textColor = C_TEXT_3;

            // Считаем только пользовательские графы — туториалы это системный контент.
            // Их вынесли в отдельный отрезок счётчика, чтобы не путать "unused" статистику.
            var userGraphs = _graphs.Where(g => !g.IsTutorial).ToList();
            int unused = userGraphs.Count(g => !g.IsUsedAsStarting);
            int tutorialCount = _graphs.Count - userGraphs.Count;

            string subtext = string.Format(
                ToolLang.Get("{0} total · {1} used as starting chapter · {2} unused",
                              "{0} всего · {1} используется как стартовая глава · {2} неиспользуемых"),
                userGraphs.Count, userGraphs.Count - unused, unused);

            if (tutorialCount > 0)
            {
                subtext += string.Format(
                    ToolLang.Get("  ·  +{0} tutorial", "  ·  +{0} туториал"),
                    tutorialCount);
            }

            GUI.Label(new Rect(20, 36, position.width - 40, 16), subtext, s);
        }

        private void DrawSearchAndActions()
        {
            Rect bar = new Rect(0, 64, position.width, 50);
            EditorGUI.DrawRect(bar, new Color(0.087f, 0.090f, 0.129f));
            EditorGUI.DrawRect(new Rect(0, bar.yMax - 1, position.width, 1), C_BORDER);

            // Поиск
            Rect searchRect = new Rect(20, 72, position.width - 220, 30);
            EditorGUI.DrawRect(searchRect, C_BG_PRIMARY);
            DrawRectBorder(searchRect, C_BORDER);

            var st = new GUIStyle(EditorStyles.textField);
            st.normal.background = null; st.focused.background = null;
            st.normal.textColor = C_TEXT_1; st.focused.textColor = C_TEXT_1;
            st.fontSize = 12; st.padding = new RectOffset(28, 8, 7, 7);
            _search = EditorGUI.TextField(searchRect, _search, st);

            if (string.IsNullOrEmpty(_search))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 8, searchRect.y, searchRect.width, searchRect.height),
                    "🔍  " + ToolLang.Get("Search graphs by name…", "Поиск графов по имени…"), ph);
            }

            // Кнопка "Удалить все неиспользуемые" — туториал-графы исключены из выборки,
            // они не "unused", они системные и read-only.
            int unusedCount = _graphs.Count(g => !g.IsUsedAsStarting && !g.IsTutorial);
            bool canCleanup = unusedCount > 0;

            Rect cleanupBtn = new Rect(position.width - 190, 72, 170, 30);
            DrawDangerButton(cleanupBtn,
                string.Format(ToolLang.Get("🗑 Delete unused ({0})", "🗑 Удалить неиспольз. ({0})"), unusedCount),
                canCleanup,
                () => DeleteAllUnusedWithDoubleConfirm());
        }

        private void DrawList()
        {
            Rect listArea = new Rect(0, 114, position.width, position.height - 114);
            GUILayout.BeginArea(listArea);
            GUILayout.Space(12);

            _scroll = GUILayout.BeginScrollView(_scroll);

            string searchLow = (_search ?? "").ToLowerInvariant();
            var visible = _graphs.Where(g =>
                string.IsNullOrEmpty(searchLow) ||
                (g.Tree.name ?? "").ToLowerInvariant().Contains(searchLow) ||
                (g.Path ?? "").ToLowerInvariant().Contains(searchLow)
            ).ToList();

            if (visible.Count == 0)
            {
                GUILayout.Space(40);
                var st = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(_graphs.Count == 0
                    ? ToolLang.Get("No graphs in your project yet.\nCreate a story to make one!",
                                    "В проекте ещё нет графов.\nСоздай историю чтобы появился первый!")
                    : ToolLang.Get("No graphs match your search.", "Ничего не найдено."),
                    st);
            }
            else
            {
                foreach (var info in visible) DrawGraphRow(info);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawGraphRow(GraphInfo info)
        {
            Rect r = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true), GUILayout.Height(64));
            r.x += 16; r.width -= 32;

            bool isTutorial = info.IsTutorial;
            bool hover = !isTutorial && r.Contains(Event.current.mousePosition);

            Color rowBg = isTutorial
                ? new Color(C_BG_PRIMARY.r, C_BG_PRIMARY.g, C_BG_PRIMARY.b, 0.6f)
                : (hover ? C_BG_RAISED : C_BG_SIDE);
            Color rowBorder = isTutorial
                ? new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f)
                : (hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f) : C_BORDER);

            EditorGUI.DrawRect(r, rowBg);
            DrawRectBorder(r, rowBorder);
            if (Event.current.type == EventType.MouseMove && hover) Repaint();

            Rect iconRect = new Rect(r.x + 12, r.y + 14, 36, 36);
            Color iconAccent = isTutorial ? C_WARN : C_ACCENT;
            EditorGUI.DrawRect(iconRect, new Color(iconAccent.r, iconAccent.g, iconAccent.b, isTutorial ? 0.08f : 0.13f));
            DrawRectBorder(iconRect, new Color(iconAccent.r, iconAccent.g, iconAccent.b, isTutorial ? 0.3f : 0.4f));
            var iconStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            iconStyle.normal.textColor = isTutorial
                ? new Color(iconAccent.r, iconAccent.g, iconAccent.b, 0.7f)
                : iconAccent;
            GUI.Label(iconRect, isTutorial ? "🎓" : "🗺", iconStyle);

            var nameStyle = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            nameStyle.normal.textColor = isTutorial ? C_TEXT_3 : C_TEXT_1;
            GUI.Label(new Rect(r.x + 60, r.y + 8, r.width - 280, 18), info.Tree.name, nameStyle);

            if (isTutorial)
            {
                var tutBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
                { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                tutBadgeStyle.normal.textColor = C_WARN;
                string badgeText = "🔒 " + ToolLang.Get("TUTORIAL", "ТУТОРИАЛ");
                float badgeW = tutBadgeStyle.CalcSize(new GUIContent(badgeText)).x + 12;
                float nameW = nameStyle.CalcSize(new GUIContent(info.Tree.name)).x;
                Rect tutBadge = new Rect(r.x + 60 + nameW + 8, r.y + 10, badgeW, 14);
                EditorGUI.DrawRect(tutBadge, new Color(C_WARN.r, C_WARN.g, C_WARN.b, 0.15f));
                DrawRectBorder(tutBadge, new Color(C_WARN.r, C_WARN.g, C_WARN.b, 0.5f));
                GUI.Label(tutBadge, badgeText, tutBadgeStyle);
            }

            var metaStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            metaStyle.normal.textColor = isTutorial ? C_TEXT_4 : C_TEXT_3;
            string shortPath = info.Path.Replace("Assets/", "").Replace("NovellaEngine/", "…/");
            string nodesText = string.Format(ToolLang.Get("{0} nodes", "{0} нод"), info.NodeCount);
            GUI.Label(new Rect(r.x + 60, r.y + 26, r.width - 280, 14),
                $"{shortPath}  ·  {nodesText}", metaStyle);

            if (isTutorial)
            {
                var bs = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
                bs.normal.textColor = new Color(C_WARN.r, C_WARN.g, C_WARN.b, 0.85f);
                GUI.Label(new Rect(r.x + 60, r.y + 42, r.width - 280, 14),
                    "ⓘ " + ToolLang.Get("Read-only — used by the in-app tutorial",
                                        "Только для чтения — используется встроенным обучением"), bs);
            }
            else if (info.IsUsedAsStarting)
            {
                Rect badge = new Rect(r.x + 60, r.y + 42, r.width - 280, 14);
                var bs = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
                bs.normal.textColor = C_OK;
                GUI.Label(badge, "✓ " + string.Format(ToolLang.Get("Used as starting chapter in '{0}'",
                                                                    "Используется как стартовая глава в «{0}»"),
                                                       info.UsedByStory), bs);
            }
            else
            {
                Rect badge = new Rect(r.x + 60, r.y + 42, r.width - 280, 14);
                var bs = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
                bs.normal.textColor = C_WARN;
                GUI.Label(badge, "○ " + ToolLang.Get("Unused — safe to delete", "Не используется — можно удалить"), bs);
            }

            float btnSize = 28;
            float btnY = r.y + (r.height - btnSize) / 2;

            Rect openBtn = new Rect(r.xMax - 200, btnY, 90, btnSize);
            if (DrawAccentButton(openBtn, "▶ " + ToolLang.Get("Open", "Открыть")))
            {
                NovellaGraphWindow.OpenGraphWindow(info.Tree);
            }

            Rect pingBtn = new Rect(r.xMax - 102, btnY, 36, btnSize);
            if (DrawSquareIconBtn(pingBtn, "📍", C_TEXT_2, ToolLang.Get("Show in Project view", "Показать файл в окне Project")))
            {
                EditorGUIUtility.PingObject(info.Tree);
            }

            Rect deleteBtn = new Rect(r.xMax - 60, btnY, 36, btnSize);
            if (isTutorial)
            {
                EditorGUI.DrawRect(deleteBtn, new Color(C_BG_PRIMARY.r, C_BG_PRIMARY.g, C_BG_PRIMARY.b, 0.6f));
                DrawRectBorder(deleteBtn, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));
                var ds = new GUIStyle(EditorStyles.label)
                { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
                ds.normal.textColor = new Color(C_TEXT_4.r, C_TEXT_4.g, C_TEXT_4.b, 0.6f);
                GUI.Label(deleteBtn, new GUIContent("🔒", ToolLang.Get("Tutorial graphs are protected from deletion", "Туториал-графы защищены от удаления")), ds);
            }
            else
            {
                if (DrawSquareIconBtn(deleteBtn, "🗑", C_DANGER, ToolLang.Get("Delete graph", "Удалить граф")))
                {
                    var capturedInfo = info;
                    EditorApplication.delayCall += () => DeleteOneWithConfirm(capturedInfo);
                }
            }

            GUILayout.Space(6);
        }
        private bool DrawSquareIconBtn(Rect r, string icon, Color color, string tooltip = "")
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? C_BG_RAISED : C_BG_PRIMARY);
            DrawRectBorder(r, C_BORDER);

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            st.normal.textColor = hover ? color : new Color(color.r, color.g, color.b, 0.7f);

            GUI.Label(r, new GUIContent(icon, tooltip), st);

            if (hover && Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
        // ─────────────────────────────────────────────
        // Удаление с подтверждениями
        // ─────────────────────────────────────────────

        private void DeleteOneWithConfirm(GraphInfo info)
        {
            // Defense-in-depth: даже если каким-то образом сюда дошёл туториал-граф
            // (например через будущую горячую клавишу или внешний вызов) — отказываемся явно.
            if (info.IsTutorial)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot delete tutorial graph", "Нельзя удалить туториал-граф"),
                    ToolLang.Get(
                        "This graph is part of the built-in tutorial and is protected from deletion.",
                        "Этот граф относится к встроенному обучению и защищён от удаления."),
                    "OK");
                return;
            }

            string warningExtra = info.IsUsedAsStarting
                ? "\n\n" + string.Format(ToolLang.Get(
                    "⚠ This graph is used as starting chapter in '{0}'. Deleting it will leave the story without a starting chapter.",
                    "⚠ Этот граф используется как стартовая глава в «{0}». После удаления у истории не будет стартовой главы."),
                    info.UsedByStory)
                : "";

            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete graph?", "Удалить граф?"),
                string.Format(ToolLang.Get("Delete '{0}' permanently?{1}", "Удалить «{0}» безвозвратно?{1}"),
                              info.Tree.name, warningExtra),
                ToolLang.Get("Yes, delete", "Да, удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            AssetDatabase.DeleteAsset(info.Path);
            AssetDatabase.SaveAssets();
            RefreshData();
            Repaint();
            if (_hubToRefresh != null) _hubToRefresh.Repaint();
        }

        private void DeleteAllUnusedWithDoubleConfirm()
        {
            // Туториал-графы read-only и не должны попадать в batch-удаление,
            // даже если они формально не используются как Starting Chapter.
            var unused = _graphs.Where(g => !g.IsUsedAsStarting && !g.IsTutorial).ToList();
            if (unused.Count == 0) return;

            // Шаг 1: общее предупреждение
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete unused graphs?", "Удалить неиспользуемые графы?"),
                string.Format(ToolLang.Get(
                    "Found {0} unused graphs. They are not assigned as a starting chapter to any story.\n\nContinue to confirmation?",
                    "Найдено {0} неиспользуемых графов. Они не назначены стартовой главой ни одной истории.\n\nПродолжить к подтверждению?"),
                    unused.Count),
                ToolLang.Get("Continue", "Продолжить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            // Шаг 2: финальное подтверждение
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("⚠ Final confirmation", "⚠ Финальное подтверждение"),
                ToolLang.Get(
                    "Last chance — these graphs will be deleted right now.\n\nThis cannot be undone.",
                    "Последний шанс — графы будут удалены прямо сейчас.\n\nЭто действие нельзя отменить."),
                ToolLang.Get("Yes, delete all", "Да, удалить все"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            int deleted = 0;
            foreach (var info in unused)
            {
                if (string.IsNullOrEmpty(info.Path)) continue;
                if (AssetDatabase.DeleteAsset(info.Path)) deleted++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshData();
            Repaint();
            if (_hubToRefresh != null) _hubToRefresh.Repaint();

            EditorUtility.DisplayDialog(
                ToolLang.Get("Done", "Готово"),
                string.Format(ToolLang.Get("Deleted {0} graphs.", "Удалено графов: {0}"), deleted),
                "OK");
        }

        // ─────────────────────────────────────────────
        // UI helpers (своя копия — окно standalone)
        // ─────────────────────────────────────────────

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private bool DrawAccentButton(Rect r, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.25f)
                : new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.15f));
            DrawRectBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.6f));
            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_ACCENT;
            GUI.Label(r, label, st);
            if (hover && Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawDangerButton(Rect r, string label, bool enabled, System.Action onClick)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);

            EditorGUI.DrawRect(r, hover
                ? new Color(C_DANGER.r, C_DANGER.g, C_DANGER.b, 0.20f)
                : (enabled ? new Color(C_DANGER.r, C_DANGER.g, C_DANGER.b, 0.10f) : C_BG_PRIMARY));
            DrawRectBorder(r, enabled
                ? new Color(C_DANGER.r, C_DANGER.g, C_DANGER.b, 0.5f)
                : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            st.normal.textColor = enabled ? C_DANGER : C_TEXT_4;
            GUI.Label(r, label, st);

            if (hover && Event.current.type == EventType.MouseMove) Repaint();
            if (enabled && Event.current.type == EventType.MouseDown && hover)
            {
                onClick?.Invoke();
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawSquareIconBtn(Rect r, string icon, Color color)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? C_BG_RAISED : C_BG_PRIMARY);
            DrawRectBorder(r, C_BORDER);
            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
            st.normal.textColor = hover ? color : new Color(color.r, color.g, color.b, 0.7f);
            GUI.Label(r, icon, st);
            if (hover && Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
    }
}
