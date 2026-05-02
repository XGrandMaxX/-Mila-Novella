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

        // Контактные ссылки автора. Дублируются с константами в ConsoleModule
        // намеренно — модуль и диалог должны жить независимо. Если поменяешь
        // адреса — поменяй в обоих местах.
        private const string AUTHOR_TELEGRAM_USERNAME = "PBGJ241";
        private const string AUTHOR_DISCORD_USER_ID   = "384220331188944896";

        public static void Show(string reportText, int errorCount)
        {
            var win = GetWindow<NovellaReportDialog>(true,
                ToolLang.Get("Send a report", "Отправить отчёт"), true);
            win._reportText = reportText;
            win._errorCount = errorCount;
            win.minSize = new Vector2(520, 460);
            win.maxSize = new Vector2(520, 460);
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
                "We collected {0} error(s) from this session. Don't be shy — sending the report takes 5 seconds and really helps fix bugs.",
                "Мы собрали {0} ошибок за сессию. Не стесняйся — отправка отчёта займёт 5 секунд и реально помогает чинить баги."),
                _errorCount), subSt);
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
                "✈",
                "Telegram",
                "@" + AUTHOR_TELEGRAM_USERNAME,
                C_TELEGRAM,
                ToolLang.Get(
                    "Opens directly in Telegram. One Ctrl+V — and the report is in.",
                    "Откроется прямо в Telegram. Одно нажатие Ctrl+V — и отчёт у автора."),
                () =>
                {
                    EditorGUIUtility.systemCopyBuffer = _reportText;
                    Application.OpenURL("https://t.me/" + AUTHOR_TELEGRAM_USERNAME);
                    ShowSentToast("Telegram");
                });

            GUILayout.Space(16);

            DrawChannelCard(
                "💬",
                "Discord",
                ToolLang.Get("DM the author", "Личка автора"),
                C_DISCORD,
                ToolLang.Get(
                    "Opens the author's profile. Click «Send Message», then Ctrl+V.",
                    "Откроется профиль автора. Жми «Send Message», потом Ctrl+V."),
                () =>
                {
                    EditorGUIUtility.systemCopyBuffer = _reportText;
                    Application.OpenURL("https://discord.com/users/" + AUTHOR_DISCORD_USER_ID);
                    ShowSentToast("Discord");
                });

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
                    EditorUtility.DisplayDialog(
                        ToolLang.Get("Copied", "Скопировано"),
                        ToolLang.Get("Report is in the clipboard. Press Ctrl+V to paste.",
                                     "Отчёт в буфере обмена. Нажми Ctrl+V чтобы вставить."),
                        "OK");
                    Close();
                });

            GUILayout.Space(8);

            DrawSecondaryButton(
                "💾  " + ToolLang.Get("Save as .txt", "Сохранить .txt"),
                ToolLang.Get(
                    "Save report as a text file on disk.",
                    "Сохранить отчёт как текстовый файл на диск."),
                () =>
                {
                    string defaultName = "novella-error-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                    string path = EditorUtility.SaveFilePanel(
                        ToolLang.Get("Save error report", "Сохранить отчёт об ошибках"),
                        "", defaultName, "txt");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            System.IO.File.WriteAllText(path, _reportText);
                            EditorUtility.RevealInFinder(path);
                            Close();
                        }
                        catch (Exception ex)
                        {
                            EditorUtility.DisplayDialog(
                                ToolLang.Get("Save failed", "Не удалось сохранить"),
                                ex.Message, "OK");
                        }
                    }
                });

            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            // ─── Кнопка закрыть внизу ───────────────────────────────────
            GUILayout.FlexibleSpace();
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

        // Большая «карточка-кнопка» канала. Цветной фон бренда + крупная
        // иконка-эмодзи + название + сабтайтл + всё это кликабельно.
        private void DrawChannelCard(string icon, string title, string subtitle, Color brandColor, string tooltip, Action onClick)
        {
            const float cardW = 220f, cardH = 130f;
            Rect r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));
            bool hover = r.Contains(Event.current.mousePosition);

            // Тень / приподнятость на hover.
            Color bg = hover ? new Color(brandColor.r, brandColor.g, brandColor.b, 0.26f)
                             : new Color(brandColor.r, brandColor.g, brandColor.b, 0.16f);
            EditorGUI.DrawRect(r, bg);

            // Бордер.
            DrawBorder(r, hover ? brandColor : new Color(brandColor.r, brandColor.g, brandColor.b, 0.55f));

            // Цветная полоса слева — акцент.
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), brandColor);

            // Иконка.
            var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
            iconSt.normal.textColor = brandColor;
            GUI.Label(new Rect(r.x + 12, r.y + 16, 56, 48), new GUIContent(icon, tooltip), iconSt);

            // Title.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            titleSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 76, r.y + 24, r.width - 84, 22), new GUIContent(title, tooltip), titleSt);

            // Subtitle.
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            subSt.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x + 76, r.y + 46, r.width - 84, 16), new GUIContent(subtitle, tooltip), subSt);

            // Tooltip-подпись внизу карточки.
            var tipSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, wordWrap = true };
            tipSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(r.x + 12, r.y + 78, r.width - 24, 44), tooltip, tipSt);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
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

        private void ShowSentToast(string channelName)
        {
            EditorUtility.DisplayDialog(
                ToolLang.Get("Report copied", "Отчёт скопирован"),
                string.Format(ToolLang.Get(
                    "Report is in your clipboard and {0} should open shortly. Press Ctrl+V in the chat.\n\nThank you for the feedback ❤",
                    "Отчёт в буфере обмена и {0} должен скоро открыться. Жми Ctrl+V в чате.\n\nСпасибо за фидбек ❤"),
                    channelName),
                "OK");
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
