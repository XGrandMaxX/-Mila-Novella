// ════════════════════════════════════════════════════════════════════════════
// NovellaReportDialog — модальное окно «Поделись ошибкой».
// Альтернатива GenericMenu: вместо безликого выпадающего списка показываем
// красивую панель с большими кнопками каналов и поощрительным сообщением.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaReportDialog : EditorWindow
    {
        // Цвета берём из Settings — окно подхватывает текущую тему Studio.
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BG_SIDE  => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        // Бренд-цвета каналов — общеизвестные значения, не считаются секретами.
        private static readonly Color C_TELEGRAM = new Color(0.13f, 0.62f, 0.85f); // #229ED9
        private static readonly Color C_DISCORD  = new Color(0.34f, 0.40f, 0.95f); // #5865F2
        private static readonly Color C_SUCCESS  = new Color(0.40f, 0.78f, 0.45f);

        private string _reportText;
        private int _errorCount;
        // Inline-статус последнего действия (вместо DisplayDialog-тостов
        // которые перебивали фокус и закрывали окно). Обнуляется не нужно —
        // юзер сам закрывает окно по кнопке Close.
        private string _statusMessage;
        private double _statusSetAt;
        // Кэш hover-состояния карточек — чтобы Repaint вызывался только
        // когда курсор пересёк границу, а не каждый MouseMove.
        private bool _hoverTelegram, _hoverDiscord;

        private void OnEnable()
        {
            // Без wantsMouseMove IMGUI не получает MouseMove события и
            // hover-эффекты «застывают» — visually это и был «лаг».
            wantsMouseMove = true;
        }

        // Контактные ссылки автора. Дублируются с константами в ConsoleModule
        // намеренно — модуль и диалог должны жить независимо. Если поменяешь
        // адреса — поменяй в обоих местах.
        private const string AUTHOR_TELEGRAM_USERNAME = "PBGJ241";
        private const string AUTHOR_DISCORD_USER_ID   = "384220331188944896";

        public static void Show(string reportText, int errorCount)
        {
            // Single-instance: повторный клик переоткрывает, не плодит стек.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaReportDialog>())
            {
                if (existing != null) existing.Close();
            }

            var win = CreateInstance<NovellaReportDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Send a report", "Отправить отчёт"));
            win._reportText  = reportText;
            win._errorCount  = errorCount;

            // Центруем относительно главного окна Unity. CreateInstance +
            // ручная позиция надёжнее чем GetWindow (который мог поставить
            // окно «куда захочет», часто далеко от центра).
            var size = new Vector2(520, 460);
            var main = EditorGUIUtility.GetMainWindowPosition();
            float x = main.x + (main.width  - size.x) * 0.5f;
            float y = main.y + (main.height - size.y) * 0.5f;
            win.position = new Rect(x, y, size.x, size.y);
            win.minSize  = size;
            win.maxSize  = size;
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(20);

            // ─── Header ─────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var heartSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            heartSt.normal.textColor = new Color(0.92f, 0.36f, 0.36f);
            GUILayout.Label("❤", heartSt, GUILayout.Width(40), GUILayout.Height(40));

            GUILayout.Space(8);
            GUILayout.BeginVertical();
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(ToolLang.Get(
                "Help us make Novella Studio better",
                "Помоги сделать Novella Studio лучше"), titleSt);

            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(string.Format(ToolLang.Get(
                "We collected {0} from this session. Don't be shy — sending the report takes 5 seconds.",
                "Мы собрали {0} за сессию. Не стесняйся — отправка отчёта займёт 5 секунд."),
                NovellaPlurals.Errors(_errorCount)), subSt);

            GUILayout.Space(4);
            // Молния отдельным label-ом, без italic и без wordWrap — иначе при
            // переносе строки glyph «⚡» вертикально сжимался и выглядел плоским.
            GUILayout.BeginHorizontal();
            var lightSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            lightSt.normal.textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.95f);
            GUILayout.Label("⚡", lightSt, GUILayout.Width(18), GUILayout.Height(20));

            var promiseSt = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, fontStyle = FontStyle.Italic };
            promiseSt.normal.textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.95f);
            GUILayout.Label(ToolLang.Get(
                "Bugs are fixed as fast as possible — your feedback is never wasted, it makes the toolkit better for everyone.",
                "Баги исправляются максимально быстро — твой фидбек не лишний, он делает инструмент лучше для всех."),
                promiseSt);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(20);

            // ─── Канал-кнопки (Telegram / Discord) ──────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);

            DrawChannelCard(
                NovellaSocialIcons.Telegram,
                "Telegram",
                "@" + AUTHOR_TELEGRAM_USERNAME,
                C_TELEGRAM,
                ToolLang.Get(
                    "Opens Telegram. Long reports are auto-saved as .txt — drag the file from File Explorer into the chat.",
                    "Откроется Telegram. Длинные отчёты автоматически сохраняются в .txt — перетащи файл из проводника в чат."),
                () => OpenChannel("Telegram", "https://t.me/" + AUTHOR_TELEGRAM_USERNAME),
                ref _hoverTelegram);

            GUILayout.Space(16);

            DrawChannelCard(
                NovellaSocialIcons.Discord,
                "Discord",
                ToolLang.Get("DM the author", "Личка автора"),
                C_DISCORD,
                ToolLang.Get(
                    "Opens Discord. Long reports are auto-saved as .txt — drag the file from File Explorer into the DM.",
                    "Откроется Discord. Длинные отчёты автоматически сохраняются в .txt — перетащи файл из проводника в DM."),
                () => OpenChannel("Discord", "https://discord.com/users/" + AUTHOR_DISCORD_USER_ID),
                ref _hoverDiscord);

            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(16);

            // ─── Альтернативные каналы (компактно) ──────────────────────
            var altHdrSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            altHdrSt.normal.textColor = C_TEXT_4;
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            GUILayout.Label(ToolLang.Get("OR SAVE / COPY MANUALLY",
                                         "ИЛИ СОХРАНИТЬ / СКОПИРОВАТЬ ВРУЧНУЮ"), altHdrSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);

            DrawSecondaryButton(
                "📋  " + ToolLang.Get("Copy to clipboard", "В буфер обмена"),
                ToolLang.Get(
                    "Copy the full report. Paste anywhere you need.",
                    "Скопировать весь отчёт. Вставить можно куда угодно."),
                () =>
                {
                    EditorGUIUtility.systemCopyBuffer = _reportText;
                    SetStatus(ToolLang.Get(
                        "✓ Report copied to clipboard. Press Ctrl+V where you need it.",
                        "✓ Отчёт скопирован в буфер обмена. Нажми Ctrl+V где нужно."));
                });

            GUILayout.Space(8);

            DrawSecondaryButton(
                "💾  " + ToolLang.Get("Save as .txt", "Сохранить .txt"),
                ToolLang.Get(
                    "Save report as a text file on disk.",
                    "Сохранить отчёт как текстовый файл на диск."),
                () =>
                {
                    // Имя файла — «официальное», с подчёркиваниями и
                    // удобной для сортировки датой ISO-формата.
                    string defaultName = "Novella_Studio_Error_Report_" +
                        DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt";
                    string path = EditorUtility.SaveFilePanel(
                        ToolLang.Get("Save error report", "Сохранить отчёт об ошибках"),
                        "", defaultName, "txt");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            System.IO.File.WriteAllText(path, _reportText);
                            EditorUtility.RevealInFinder(path);
                            SetStatus(string.Format(ToolLang.Get(
                                "✓ Saved to {0}",
                                "✓ Сохранено: {0}"), System.IO.Path.GetFileName(path)));
                        }
                        catch (Exception ex)
                        {
                            SetStatus(ToolLang.Get("✗ Save failed: ", "✗ Не удалось сохранить: ") + ex.Message);
                        }
                    }
                });

            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            // ─── Inline-статус ───────────────────────────────────────────
            // Показывает результат последнего действия — без всплывающих
            // диалогов, которые перетягивали фокус и закрывали окно.
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                bool isError = _statusMessage.StartsWith("✗");
                var bannerCol = isError ? new Color(0.92f, 0.36f, 0.36f, 0.16f)
                                        : new Color(C_SUCCESS.r, C_SUCCESS.g, C_SUCCESS.b, 0.16f);
                Rect bannerRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(bannerRect, bannerCol);

                var bannerSt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false };
                bannerSt.normal.textColor = isError ? new Color(0.92f, 0.36f, 0.36f) : C_SUCCESS;
                GUI.Label(new Rect(bannerRect.x + 24, bannerRect.y, bannerRect.width - 48, bannerRect.height), _statusMessage, bannerSt);
            }

            // ─── Кнопка закрыть внизу ───────────────────────────────────
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var closeSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 24, padding = new RectOffset(16, 16, 2, 2) };
            closeSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), closeSt, GUILayout.Width(100)))
            {
                Close();
            }
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }

        // Большая «карточка-кнопка» канала. Cached-hover bool снаружи —
        // чтобы вызывать Repaint только при реальной смене hover, а не на
        // каждое MouseMove (раньше это было причиной «лагающего» интерфейса).
        private void DrawChannelCard(Texture2D icon, string title, string subtitle, Color brandColor,
                                     string tooltip, System.Action onClick, ref bool cachedHover)
        {
            const float cardW = 220f, cardH = 130f;
            Rect r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));

            // Hover пересчитываем только в Repaint/MouseMove чтобы не пересоздавать
            // GUI-стили на каждый event-проход (Layout, KeyDown, etc.).
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove)
            {
                bool h = r.Contains(Event.current.mousePosition);
                if (h != cachedHover)
                {
                    cachedHover = h;
                    if (Event.current.type == EventType.MouseMove) Repaint();
                }
            }

            // Тень / приподнятость на hover.
            Color bg = cachedHover ? new Color(brandColor.r, brandColor.g, brandColor.b, 0.26f)
                                   : new Color(brandColor.r, brandColor.g, brandColor.b, 0.16f);
            EditorGUI.DrawRect(r, bg);
            DrawBorder(r, cachedHover ? brandColor : new Color(brandColor.r, brandColor.g, brandColor.b, 0.55f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), brandColor);

            // Логотип. ScaleToFit — для 64→56.
            if (icon != null)
            {
                Rect iconRect = new Rect(r.x + 14, r.y + 18, 56, 56);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                // Tooltip перенесён ниже на сам label-блок — не накладываем
                // лишний GUI.Label поверх Texture (это лишний хит-тест).
            }

            // Title (ленивая инициализация стиля — раньше создавали GUIStyle на
            // каждый кадр, что и могло слегка тормозить на hover).
            var titleSt = LazyTitleStyle();
            GUI.Label(new Rect(r.x + 80, r.y + 24, r.width - 88, 22), new GUIContent(title, tooltip), titleSt);

            var subSt = LazySubtitleStyle();
            GUI.Label(new Rect(r.x + 80, r.y + 46, r.width - 88, 16), subtitle, subSt);

            var tipSt = LazyTipStyle();
            GUI.Label(new Rect(r.x + 14, r.y + 82, r.width - 28, 40), tooltip, tipSt);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                Event.current.Use();
            }
        }

        // ─── Кэш стилей ───────────────────────────────────────────────
        private GUIStyle _titleStyle, _subtitleStyle, _tipStyle;
        private GUIStyle LazyTitleStyle()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            }
            _titleStyle.normal.textColor = C_TEXT_1; // тема могла поменяться
            return _titleStyle;
        }
        private GUIStyle LazySubtitleStyle()
        {
            if (_subtitleStyle == null)
            {
                _subtitleStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            }
            _subtitleStyle.normal.textColor = C_TEXT_3;
            return _subtitleStyle;
        }
        private GUIStyle LazyTipStyle()
        {
            if (_tipStyle == null)
            {
                _tipStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
            }
            _tipStyle.normal.textColor = C_TEXT_4;
            return _tipStyle;
        }

        // Серая «второстепенная» кнопка под основными — для clipboard / save.
        private void DrawSecondaryButton(string label, string tooltip, Action onClick)
        {
            var st = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 30,
                padding = new RectOffset(14, 14, 2, 2),
            };
            st.normal.textColor = C_TEXT_2;
            st.hover.textColor = C_TEXT_1;
            if (GUILayout.Button(new GUIContent(label, tooltip), st, GUILayout.MinWidth(220)))
            {
                onClick?.Invoke();
            }
        }

        // Inline-статус — показывается узкой полосой над кнопкой Закрыть.
        // Не трогает фокус окна (в отличие от EditorUtility.DisplayDialog,
        // который отбирал фокус и при закрытии автоматически закрывал и нас).
        private void SetStatus(string text)
        {
            _statusMessage = text;
            _statusSetAt = EditorApplication.timeSinceStartup;
            Repaint();
        }

        // Открывает мессенджер с разной стратегией передачи отчёта в зависимости
        // от длины: короткий текст (≤ TG_MESSAGE_LIMIT) кладём в буфер обмена,
        // длинный — сохраняем .txt во временную папку и открываем проводник
        // на этом файле, чтобы юзер мог перетащить его в чат drag-n-drop'ом.
        // Telegram-мессадж имеет лимит 4096 символов, Discord — 2000. Берём
        // более жёсткий 1900 как общий порог чтобы был запас.
        private const int CHAT_INLINE_LIMIT = 1900;

        private void OpenChannel(string channelName, string url)
        {
            EditorGUIUtility.systemCopyBuffer = _reportText;

            bool tooLong = _reportText != null && _reportText.Length > CHAT_INLINE_LIMIT;
            string savedPath = null;
            if (tooLong)
            {
                try
                {
                    string fname = "Novella_Studio_Error_Report_" +
                        DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt";
                    savedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fname);
                    System.IO.File.WriteAllText(savedPath, _reportText);
                    EditorUtility.RevealInFinder(savedPath);
                }
                catch (Exception ex)
                {
                    SetStatus(ToolLang.Get("✗ Failed to save temp file: ", "✗ Не удалось сохранить временный файл: ") + ex.Message);
                    return;
                }
            }

            Application.OpenURL(url);

            if (tooLong)
            {
                SetStatus(string.Format(ToolLang.Get(
                    "✓ {0} opened. Report is long — drag {1} from File Explorer into the chat. Thanks ❤",
                    "✓ {0} открыт. Отчёт длинный — перетащи {1} из проводника в чат. Спасибо ❤"),
                    channelName, System.IO.Path.GetFileName(savedPath)));
            }
            else
            {
                SetStatus(string.Format(ToolLang.Get(
                    "✓ {0} opened — paste the report (Ctrl+V) into the chat. Thanks ❤",
                    "✓ {0} открыт — вставь отчёт (Ctrl+V) в чат. Спасибо ❤"),
                    channelName));
            }
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
