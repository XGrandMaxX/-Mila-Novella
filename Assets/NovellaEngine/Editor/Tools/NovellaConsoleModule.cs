// ════════════════════════════════════════════════════════════════════════════
// Novella Console — встроенная консоль Studio. Отображает логи Unity
// (Debug.Log/Warning/Error/Exception) в фирменном стиле:
//   • Левая колонка: список лог-записей с фильтрами.
//   • Правая колонка: stack trace + полный текст выделенной записи.
//   • Тулбар: тоггл-фильтры по типу, очистка, авто-прокрутка, поиск, копировать.
// Источник данных — NovellaConsoleStore (статический буфер).
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaConsoleModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Console", "Консоль");
        public string ModuleIcon => "🖥";

        // ─── Цвета (читаем из Settings, не хардкодим) ──────────────────────
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static readonly Color C_LOG     = new Color(0.62f, 0.70f, 0.78f);
        private static readonly Color C_WARNING = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color C_ERROR   = new Color(0.92f, 0.36f, 0.36f);

        private EditorWindow _window;

        // Тоггл-фильтры. Сохраняем в EditorPrefs чтобы не сбрасывались между сессиями.
        private const string PrefShowLog   = "Novella.Console.ShowLog";
        private const string PrefShowWarn  = "Novella.Console.ShowWarn";
        private const string PrefShowErr   = "Novella.Console.ShowErr";
        private const string PrefAutoScroll= "Novella.Console.AutoScroll";
        private const string PrefCollapse  = "Novella.Console.Collapse";

        private bool _showLog, _showWarn, _showErr, _autoScroll, _collapse;
        private string _searchQuery = "";
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        // Индекс выделенной записи в ОТФИЛЬТРОВАННОМ списке
        // (для прыжка в детали). -1 = не выбрано.
        private int _selectedFiltered = -1;

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
            _showLog    = EditorPrefs.GetBool(PrefShowLog,    true);
            _showWarn   = EditorPrefs.GetBool(PrefShowWarn,   true);
            _showErr    = EditorPrefs.GetBool(PrefShowErr,    true);
            _autoScroll = EditorPrefs.GetBool(PrefAutoScroll, true);
            _collapse   = EditorPrefs.GetBool(PrefCollapse,   false);

            NovellaConsoleStore.OnChanged += RequestRepaint;
        }

        public void OnDisable()
        {
            NovellaConsoleStore.OnChanged -= RequestRepaint;
        }

        // OnChanged может прилететь не из главного потока, поэтому Repaint
        // запускаем через delayCall — он сам диспетчирует на main thread.
        private void RequestRepaint()
        {
            EditorApplication.delayCall += () =>
            {
                if (_window != null) _window.Repaint();
            };
        }

        public void DrawGUI(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG_PRIMARY);

            // ─── Toolbar ────────────────────────────────────────────────
            const float toolbarH = 40f;
            Rect toolbarRect = new Rect(position.x, position.y, position.width, toolbarH);
            EditorGUI.DrawRect(toolbarRect, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(toolbarRect.x, toolbarRect.yMax - 1, toolbarRect.width, 1), C_BORDER);
            DrawToolbar(toolbarRect);

            // ─── Главная область: список слева + детали справа ─────────
            const float minDetailW = 360f;
            float listW = Mathf.Max(420f, position.width * 0.55f);
            if (position.width - listW < minDetailW) listW = position.width - minDetailW;
            if (listW < 360f) listW = position.width; // совсем узкое окно — без правой панели

            Rect listRect   = new Rect(position.x, position.y + toolbarH, listW, position.height - toolbarH);
            Rect detailRect = new Rect(listRect.xMax, position.y + toolbarH, position.width - listW, position.height - toolbarH);

            DrawList(listRect);
            if (detailRect.width > 50)
            {
                EditorGUI.DrawRect(new Rect(detailRect.x, detailRect.y, 1, detailRect.height), C_BORDER);
                DrawDetails(detailRect);
            }
        }

        // ─── Toolbar ──────────────────────────────────────────────────────
        private void DrawToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);

            var counts = NovellaConsoleStore.CountByType();

            // Тоггл-кнопки фильтров. Иконки ⓘ ⚠ ✖ + счётчик.
            DrawFilterToggle("ⓘ", counts.log,   ref _showLog,  PrefShowLog,  C_LOG,
                ToolLang.Get("Show informational logs (Debug.Log).", "Показывать обычные логи (Debug.Log)."));
            GUILayout.Space(4);
            DrawFilterToggle("⚠", counts.warn,  ref _showWarn, PrefShowWarn, C_WARNING,
                ToolLang.Get("Show warnings.", "Показывать предупреждения."));
            GUILayout.Space(4);
            DrawFilterToggle("✖", counts.error, ref _showErr,  PrefShowErr,  C_ERROR,
                ToolLang.Get("Show errors and exceptions.", "Показывать ошибки и исключения."));

            GUILayout.Space(12);

            // Поле поиска.
            var searchSt = new GUIStyle(EditorStyles.toolbarSearchField) { fontSize = 11 };
            _searchQuery = EditorGUILayout.TextField(_searchQuery, searchSt, GUILayout.MinWidth(140), GUILayout.Height(22));

            GUILayout.Space(8);

            // Тоггл «Свернуть повторы».
            DrawCheckToggle(ToolLang.Get("Collapse",     "Свернуть"),   ref _collapse,   PrefCollapse,
                ToolLang.Get("Collapse identical messages into a single line with a counter.",
                             "Сворачивать одинаковые сообщения в одну строку со счётчиком."));
            GUILayout.Space(6);
            DrawCheckToggle(ToolLang.Get("Auto-scroll", "Авто-прокрутка"), ref _autoScroll, PrefAutoScroll,
                ToolLang.Get("Automatically scroll to the latest log entry.",
                             "Автоматически прокручивать список к последней записи."));

            GUILayout.FlexibleSpace();

            // Кнопка-меню «Жалоба» — видна только если есть хоть одна ошибка.
            if (counts.error > 0)
            {
                var rptSt = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize = 11,
                    fixedHeight = 22,
                    padding = new RectOffset(10, 10, 2, 2),
                    fontStyle = FontStyle.Bold,
                };
                rptSt.normal.textColor = new Color(0.92f, 0.36f, 0.36f);

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.92f, 0.36f, 0.36f, 0.22f);
                if (GUILayout.Button(new GUIContent(
                        string.Format("📤  " + ToolLang.Get("Report ({0})", "Жалоба ({0})"), counts.error),
                        ToolLang.Get(
                            "Send the error report to the toolkit author. Pick channel: Discord, Telegram, or save as a .txt file.",
                            "Отправить отчёт об ошибках автору инструмента. Выбери канал: Discord, Telegram или сохранить .txt.")),
                    rptSt))
                {
                    var report = BuildReportText();
                    NovellaReportDialog.Show(report, counts.error);
                }
                GUI.backgroundColor = prevBg;
                GUILayout.Space(6);
            }

            // Кнопка-меню «Тест» — кидает в консоль образцы каждого типа лога.
            // Удобно проверять что фильтры/счётчики/жалоба работают, без
            // необходимости запускать игру или искать живой Debug.Log в коде.
            var testSt = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 2, 2),
            };
            testSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(new GUIContent("🧪  " + ToolLang.Get("Test", "Тест") + "  ▾",
                ToolLang.Get("Generate sample log entries to verify filters, counters and the «Report» flow.",
                             "Сгенерировать тестовые записи в консоль, чтобы проверить фильтры, счётчики и кнопку «Жалоба».")),
                testSt))
            {
                ShowTestMenu();
            }
            GUILayout.Space(6);

            // Кнопка очистки.
            var clrSt = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 2, 2),
                fontStyle = FontStyle.Bold
            };
            clrSt.normal.textColor = C_TEXT_1;
            if (GUILayout.Button(new GUIContent("🗑  " + ToolLang.Get("Clear", "Очистить"),
                ToolLang.Get("Remove all log entries from the Studio console (Unity Console is not affected).",
                             "Удалить все записи из консоли Studio. Стандартная Unity Console не затрагивается.")),
                clrSt))
            {
                NovellaConsoleStore.Clear();
                _selectedFiltered = -1;
            }
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ─── Test logs ─────────────────────────────────────────────────────
        // Простые тестовые сообщения для проверки UI консоли. Все идут через
        // Debug.Log/Warning/LogError — благодаря этому они появляются и в
        // Studio-консоли (через Application.logMessageReceived), и в стандартной
        // Unity Console.
        private void ShowTestMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(ToolLang.Get("ⓘ  Add a test log",     "ⓘ  Добавить тестовый лог")),     false, () =>
                Debug.Log("[Novella Test] Hello from Studio console — this is an info log."));
            menu.AddItem(new GUIContent(ToolLang.Get("⚠  Add a test warning", "⚠  Добавить тестовое предупреждение")), false, () =>
                Debug.LogWarning("[Novella Test] This is a warning — useful to verify the ⚠ counter and pulse animation."));
            menu.AddItem(new GUIContent(ToolLang.Get("✖  Add a test error",   "✖  Добавить тестовую ошибку")), false, () =>
                Debug.LogError("[Novella Test] This is a fake error — try the «Report» button below to send it via Telegram/Discord."));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent(ToolLang.Get("Burst: 1 of each",  "Серия: по одному каждого")), false, () =>
            {
                Debug.Log     ("[Novella Test] Burst log");
                Debug.LogWarning("[Novella Test] Burst warning");
                Debug.LogError("[Novella Test] Burst error");
            });
            menu.AddItem(new GUIContent(ToolLang.Get("Burst: 1000 of each (stress test)",
                                                    "Серия: 1000 каждого (стресс-тест)")), false, () =>
            {
                bool ok = EditorUtility.DisplayDialog(
                    ToolLang.Get("Stress test", "Стресс-тест"),
                    ToolLang.Get(
                        "Spawn 3000 messages (1000 logs + 1000 warnings + 1000 errors) into the console. Useful to verify scrolling and pulse smoothness, but Unity's own console might briefly freeze.",
                        "Кинуть 3000 сообщений (1000 логов + 1000 предупреждений + 1000 ошибок). Полезно проверить плавность скролла и pulse-анимацию, но стандартная Unity-консоль может ненадолго залагать."),
                    ToolLang.Get("Run", "Запустить"),
                    ToolLang.Get("Cancel", "Отмена"));
                if (!ok) return;

                for (int i = 0; i < 1000; i++)
                {
                    Debug.Log     ("[Novella Test] Burst log #" + i);
                    Debug.LogWarning("[Novella Test] Burst warning #" + i);
                    Debug.LogError("[Novella Test] Burst error #" + i);
                }
            });
            menu.ShowAsContext();
        }

        // ─── Жалоба автору ─────────────────────────────────────────────────
        // Сборщик текста отчёта. Вызывается из NovellaReportDialog когда
        // юзер выбирает канал отправки — Telegram/Discord/Save/Copy.
        private string BuildReportText()
        {
            var snap = NovellaConsoleStore.Snapshot();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Novella Studio — error report ===");
            sb.Append("Generated: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("Unity:     ").AppendLine(Application.unityVersion);
            sb.Append("Platform:  ").AppendLine(Application.platform.ToString());
            sb.Append("OS:        ").AppendLine(SystemInfo.operatingSystem);
            sb.Append("Project:   ").AppendLine(Application.productName);
            sb.AppendLine();

            int errIdx = 0;
            for (int i = 0; i < snap.Count; i++)
            {
                var e = snap[i];
                bool isErr = e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert;
                if (!isErr) continue;
                errIdx++;
                sb.Append("--- #").Append(errIdx).Append("  [").Append(e.Type).Append("]  ").AppendLine(e.Time.ToString("HH:mm:ss"));
                sb.AppendLine(e.Message);
                if (!string.IsNullOrEmpty(e.StackTrace))
                {
                    sb.AppendLine();
                    sb.AppendLine(e.StackTrace);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // Собирает текстовый отчёт со всеми ошибками + системной инфой,
        // (Сохранение в .txt и копирование в буфер теперь живут в
        // NovellaReportDialog — там же выбор канала отправки.)

        private void DrawFilterToggle(string icon, int count, ref bool value, string prefKey, Color tint, string tooltip)
        {
            string label = string.Format("{0} {1}", icon, count);
            var st = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 2, 2),
                fontStyle = FontStyle.Bold
            };
            st.normal.textColor = value ? tint : C_TEXT_3;
            st.hover.textColor  = value ? tint : C_TEXT_2;

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = value
                ? new Color(tint.r, tint.g, tint.b, 0.22f)
                : new Color(0.5f, 0.5f, 0.5f, 0.10f);
            if (GUILayout.Button(new GUIContent(label, tooltip), st, GUILayout.MinWidth(64)))
            {
                value = !value;
                EditorPrefs.SetBool(prefKey, value);
            }
            GUI.backgroundColor = prevBg;
        }

        private void DrawCheckToggle(string label, ref bool value, string prefKey, string tooltip)
        {
            var st = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(8, 10, 2, 2)
            };
            st.normal.textColor = value ? C_ACCENT : C_TEXT_3;

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = value
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.22f)
                : new Color(0.5f, 0.5f, 0.5f, 0.10f);
            if (GUILayout.Button(new GUIContent((value ? "☑ " : "☐ ") + label, tooltip), st))
            {
                value = !value;
                EditorPrefs.SetBool(prefKey, value);
            }
            GUI.backgroundColor = prevBg;
        }

        // ─── Список записей ──────────────────────────────────────────────
        private struct VisibleEntry
        {
            public NovellaConsoleStore.LogEntry Entry;
            public int Count;          // для collapse: сколько раз встречается
            public int OriginalIndex;  // индекс в snapshot (для seek to источник)
        }

        private List<VisibleEntry> _visibleCache = new List<VisibleEntry>();
        private int _lastSnapshotCount = -1;
        // Сигнатура текущих фильтров — если она поменялась с прошлого ребилда,
        // кэш надо пересобрать даже если счётчик не изменился.
        private string _lastFilterSig = null;

        // Кэш GUIStyle для строк списка. Раньше каждая строка создавала по
        // 4-5 новых GUIStyle на каждый кадр — при тысяче логов это сотни
        // мс на кадр. Создаём один раз лениво и переиспользуем.
        private bool _listStylesReady;
        private GUIStyle _styleIcon, _styleMsg, _styleMsgSel, _styleCtx, _styleCtxSel,
                         _styleCounter, _styleTime;
        private void EnsureListStyles()
        {
            if (_listStylesReady) return;
            _styleIcon       = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _styleMsg        = new GUIStyle(EditorStyles.label)     { fontSize = 12, alignment = TextAnchor.LowerLeft, clipping = TextClipping.Clip };
            _styleMsgSel     = new GUIStyle(_styleMsg);
            _styleCtx        = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            _styleCtxSel     = new GUIStyle(_styleCtx);
            _styleCounter    = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
            _styleTime       = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.MiddleRight };
            _listStylesReady = true;
        }

        private void RebuildVisible(List<NovellaConsoleStore.LogEntry> snapshot)
        {
            _visibleCache.Clear();
            string q = _searchQuery == null ? "" : _searchQuery.Trim().ToLowerInvariant();

            // Если collapse — свернём по DedupKey, в остальном просто фильтруем.
            Dictionary<string, int> keyToIdx = _collapse ? new Dictionary<string, int>() : null;

            for (int i = 0; i < snapshot.Count; i++)
            {
                var e = snapshot[i];
                if (!PassesTypeFilter(e.Type)) continue;
                if (q.Length > 0 && !e.Message.ToLowerInvariant().Contains(q)) continue;

                if (keyToIdx != null)
                {
                    string k = e.DedupKey;
                    if (keyToIdx.TryGetValue(k, out int existIdx))
                    {
                        var ve = _visibleCache[existIdx];
                        ve.Count++;
                        // Берём свежий timestamp как «последний раз»
                        ve.Entry = e;
                        _visibleCache[existIdx] = ve;
                        continue;
                    }
                    keyToIdx[k] = _visibleCache.Count;
                }
                _visibleCache.Add(new VisibleEntry { Entry = e, Count = 1, OriginalIndex = i });
            }
        }

        private bool PassesTypeFilter(LogType t)
        {
            switch (t)
            {
                case LogType.Log:       return _showLog;
                case LogType.Warning:   return _showWarn;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:    return _showErr;
            }
            return true;
        }

        private const float ROW_HEIGHT = 44f;

        private void DrawList(Rect rect)
        {
            EnsureListStyles();

            // Пересобираем кэш ТОЛЬКО если поменялось количество логов или
            // фильтры. Раньше тут стояло «|| true» — кэш строился каждый кадр,
            // плюс Snapshot() копировал весь буфер под локом. На 2000 записях
            // это давало ощутимые лаги. Сейчас сначала проверяем дешёвый Count,
            // и только при изменении вызываем Snapshot.
            int curCount = NovellaConsoleStore.Count;
            string filterSig = (_showLog ? "L" : "") + (_showWarn ? "W" : "") + (_showErr ? "E" : "")
                               + "|" + (_collapse ? "1" : "0") + "|" + (_searchQuery ?? "");
            if (curCount != _lastSnapshotCount || filterSig != _lastFilterSig)
            {
                var snap = NovellaConsoleStore.Snapshot();
                RebuildVisible(snap);
                _lastSnapshotCount = curCount;
                _lastFilterSig = filterSig;
            }

            GUILayout.BeginArea(rect);

            if (_visibleCache.Count == 0)
            {
                GUILayout.Space(40);
                var st = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get(
                    "No log entries.\nWhen the game or editor scripts call Debug.Log/Warning/Error, messages will appear here.",
                    "Логов нет.\nКак только в игре или редакторных скриптах будет вызван Debug.Log/Warning/Error, сообщения появятся здесь."), st);
                GUILayout.EndArea();
                return;
            }

            float totalH = _visibleCache.Count * ROW_HEIGHT;

            // Авто-прокрутка к низу.
            if (_autoScroll) _listScroll.y = Mathf.Max(0, totalH - rect.height);

            // Ручной BeginScrollView с заранее известной общей высотой —
            // позволяет рендерить только видимый диапазон строк (виртуализация).
            // Без этого 2000 строк рисовались каждый кадр все.
            Rect scrollArea  = new Rect(0, 0, rect.width, rect.height);
            Rect contentArea = new Rect(0, 0, rect.width - 16, totalH);
            _listScroll = GUI.BeginScrollView(scrollArea, _listScroll, contentArea);

            int firstVisible = Mathf.Max(0, (int)(_listScroll.y / ROW_HEIGHT) - 2);
            int lastVisible  = Mathf.Min(_visibleCache.Count - 1,
                                         (int)((_listScroll.y + rect.height) / ROW_HEIGHT) + 2);

            for (int i = firstVisible; i <= lastVisible; i++)
            {
                Rect rowRect = new Rect(0, i * ROW_HEIGHT, contentArea.width, ROW_HEIGHT);
                DrawEntryRow(rowRect, _visibleCache[i], i);
            }

            GUI.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEntryRow(Rect row, VisibleEntry ve, int filteredIndex)
        {
            bool isSel = (filteredIndex == _selectedFiltered);

            // Тон фона: чередование + подсветка выделенного.
            Color bg = (filteredIndex % 2 == 0)
                ? new Color(1f, 1f, 1f, 0.02f)
                : Color.clear;
            if (isSel) bg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f);
            EditorGUI.DrawRect(row, bg);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            // Цветная полоска по типу — у левого края.
            Color typeCol = ColorForType(ve.Entry.Type);
            EditorGUI.DrawRect(new Rect(row.x + 4, row.y + 6, 3, row.height - 12), typeCol);

            // Иконка (стиль кэширован, цвет меняем).
            _styleIcon.normal.textColor = typeCol;
            GUI.Label(new Rect(row.x + 12, row.y, 22, row.height), IconForType(ve.Entry.Type), _styleIcon);

            // Сообщение — первая строка кратко.
            string firstLine = ExtractFirstLine(ve.Entry.Message);
            var msgSt = isSel ? _styleMsgSel : _styleMsg;
            msgSt.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;

            float textX = row.x + 38;
            float counterW = ve.Count > 1 ? 38 : 0;
            float timeW = 60;
            float textW = row.width - (textX - row.x) - timeW - counterW - 12;
            GUI.Label(new Rect(textX, row.y + 2, textW, 20), firstLine, msgSt);

            // Краткий контекст (первая строка стека или вторая строка сообщения).
            string ctx = ExtractContext(ve.Entry.Message, ve.Entry.StackTrace);
            if (!string.IsNullOrEmpty(ctx))
            {
                var ctxSt = isSel ? _styleCtxSel : _styleCtx;
                ctxSt.normal.textColor = isSel ? C_TEXT_3 : C_TEXT_4;
                GUI.Label(new Rect(textX, row.y + 22, textW, 18), ctx, ctxSt);
            }

            // Счётчик повторов (если включён collapse).
            if (ve.Count > 1)
            {
                Rect cntRect = new Rect(row.xMax - timeW - counterW - 6, row.y + 12, counterW, 20);
                EditorGUI.DrawRect(cntRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f));
                _styleCounter.normal.textColor = C_ACCENT;
                GUI.Label(cntRect, ve.Count.ToString(), _styleCounter);
            }

            // Время.
            Rect timeRect = new Rect(row.xMax - timeW - 6, row.y + 12, timeW, 20);
            _styleTime.normal.textColor = C_TEXT_4;
            GUI.Label(timeRect, ve.Entry.Time.ToString("HH:mm:ss"), _styleTime);

            // Разделитель снизу.
            EditorGUI.DrawRect(new Rect(row.x + 8, row.yMax - 1, row.width - 16, 1), new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.4f));

            // Клик — выделяем.
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && row.Contains(ev.mousePosition))
            {
                if (ev.button == 0)
                {
                    _selectedFiltered = filteredIndex;
                    _detailScroll = Vector2.zero;
                    _autoScroll = false; // юзер взаимодействует — отключаем авто-прокрутку
                    EditorPrefs.SetBool(PrefAutoScroll, false);
                    _window?.Repaint();
                    ev.Use();
                }
                else if (ev.button == 1)
                {
                    ShowEntryContextMenu(ve.Entry);
                    ev.Use();
                }
            }
        }

        private void ShowEntryContextMenu(NovellaConsoleStore.LogEntry e)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(ToolLang.Get("📋 Copy message",     "📋 Скопировать сообщение")),     false, () => { EditorGUIUtility.systemCopyBuffer = e.Message; });
            menu.AddItem(new GUIContent(ToolLang.Get("📋 Copy stack trace", "📋 Скопировать стек")),           false, () => { EditorGUIUtility.systemCopyBuffer = e.StackTrace; });
            menu.AddItem(new GUIContent(ToolLang.Get("📋 Copy all",         "📋 Скопировать всё")),            false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = string.Format("[{0}] {1}\n{2}\n{3}", e.Type, e.Time.ToString("HH:mm:ss"), e.Message, e.StackTrace);
            });
            menu.ShowAsContext();
        }

        // ─── Детали ─────────────────────────────────────────────────────
        private void DrawDetails(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            GUILayout.BeginArea(rect);

            if (_selectedFiltered < 0 || _selectedFiltered >= _visibleCache.Count)
            {
                GUILayout.Space(40);
                var st = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get("Select a log entry to see details.", "Выбери запись слева — здесь покажется текст и стек."), st);
                GUILayout.EndArea();
                return;
            }

            var ve = _visibleCache[_selectedFiltered];
            var e  = ve.Entry;
            Color typeCol = ColorForType(e.Type);

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            iconSt.normal.textColor = typeCol;
            GUILayout.Label(IconForType(e.Type), iconSt, GUILayout.Width(24));

            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            titleSt.normal.textColor = typeCol;
            GUILayout.Label(LabelForType(e.Type), titleSt, GUILayout.Width(110));

            var timeSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            timeSt.normal.textColor = C_TEXT_4;
            GUILayout.Label(e.Time.ToString("HH:mm:ss"), timeSt, GUILayout.Width(60));

            GUILayout.FlexibleSpace();

            var copySt = new GUIStyle(EditorStyles.miniButton) { fontSize = 10 };
            if (GUILayout.Button(new GUIContent("📋 " + ToolLang.Get("Copy", "Копировать"),
                ToolLang.Get("Copy full message and stack to clipboard.",
                             "Скопировать сообщение и стек в буфер обмена.")), copySt, GUILayout.Width(96), GUILayout.Height(20)))
            {
                EditorGUIUtility.systemCopyBuffer = e.Message + "\n" + e.StackTrace;
            }
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            // Сообщение.
            var msgSt = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, richText = false };
            msgSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(e.Message, msgSt);

            GUILayout.Space(10);

            // Stack trace — моноширинно.
            if (!string.IsNullOrEmpty(e.StackTrace))
            {
                var hdrSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
                hdrSt.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get("STACK TRACE", "СТЕК ВЫЗОВОВ"), hdrSt);

                var traceSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, richText = false };
                traceSt.font = EditorStyles.miniFont;
                traceSt.normal.textColor = C_TEXT_2;
                GUILayout.Label(e.StackTrace, traceSt);
            }

            GUILayout.Space(10);
            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ─── Утилиты ────────────────────────────────────────────────────
        private static Color ColorForType(LogType t)
        {
            switch (t)
            {
                case LogType.Log:       return C_LOG;
                case LogType.Warning:   return C_WARNING;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:    return C_ERROR;
            }
            return C_LOG;
        }

        private static string IconForType(LogType t)
        {
            switch (t)
            {
                case LogType.Log:       return "ⓘ";
                case LogType.Warning:   return "⚠";
                case LogType.Error:     return "✖";
                case LogType.Exception: return "✖";
                case LogType.Assert:    return "✖";
            }
            return "·";
        }

        private static string LabelForType(LogType t)
        {
            switch (t)
            {
                case LogType.Log:       return ToolLang.Get("Log",        "Лог");
                case LogType.Warning:   return ToolLang.Get("Warning",    "Предупреждение");
                case LogType.Error:     return ToolLang.Get("Error",      "Ошибка");
                case LogType.Exception: return ToolLang.Get("Exception",  "Исключение");
                case LogType.Assert:    return ToolLang.Get("Assertion",  "Assertion");
            }
            return t.ToString();
        }

        private static string ExtractFirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }

        // Контекст для двухстрочного отображения в списке: либо вторая строка
        // сообщения (если оно многострочное), либо первая строка стека —
        // обычно «UnityEngine.Debug:Log (object)». Чуть фильтруем шум.
        private static string ExtractContext(string message, string stackTrace)
        {
            if (!string.IsNullOrEmpty(message))
            {
                int nl = message.IndexOf('\n');
                if (nl >= 0 && nl < message.Length - 1)
                {
                    int nl2 = message.IndexOf('\n', nl + 1);
                    int end = nl2 < 0 ? message.Length : nl2;
                    return message.Substring(nl + 1, end - nl - 1);
                }
            }
            if (!string.IsNullOrEmpty(stackTrace))
            {
                int nl = stackTrace.IndexOf('\n');
                return nl < 0 ? stackTrace : stackTrace.Substring(0, nl);
            }
            return "";
        }
    }
}
