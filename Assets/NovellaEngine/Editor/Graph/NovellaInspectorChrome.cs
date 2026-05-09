// ════════════════════════════════════════════════════════════════════════════
// NovellaInspectorChrome — переиспользуемые IMGUI-helper'ы для инспектора
// графа в Hub-стиле. Вытащены из NovellaVariableEditorModule (там они
// были private), здесь стали public static — теперь и graph-инспектор
// рисуется единым визуальным языком с Variables, Characters, Hub и т.д.
//
// Используется в NovellaNodeInspectorUI.cs:
//   • Card / SectionHeader / Hint / Warn — для секций и подсказок
//   • SlimBtn / AccentBtn / DangerBtn / IconBtn — кнопки в Hub-стиле
//   • DarkTextField / DarkTextArea — тёмные поля без Unity'shного chrome
//
// Все цвета берутся из NovellaGraphTheme (центральная палитра проекта).
// ════════════════════════════════════════════════════════════════════════════

using System;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public static class NovellaInspectorChrome
    {
        // ─── Цвета (мост к graph-теме для удобства call-site'а) ───
        private static Color BgPrimary => NovellaGraphTheme.BgPrimary;
        private static Color BgSide    => NovellaGraphTheme.BgSide;
        private static Color BgRaised  => NovellaGraphTheme.BgRaised;
        private static Color Border    => NovellaGraphTheme.Border;
        private static Color Accent    => NovellaGraphTheme.Accent;
        private static Color Text1     => NovellaGraphTheme.Text1;
        private static Color Text2     => NovellaGraphTheme.Text2;
        private static Color Text3     => NovellaGraphTheme.Text3;
        private static Color Text4     => NovellaGraphTheme.Text4;
        private static Color Success   => NovellaGraphTheme.Success;
        private static Color Danger    => NovellaGraphTheme.Danger;
        private static Color Warning   => NovellaGraphTheme.Warning;

        // ─── Section header ─────────────────────────────────────────────────
        // Hub-style: cyan miniBoldLabel UPPERCASE + 1px Border снизу + spacing.
        // Заменяет старый `EditorStyles.boldLabel 14pt` с разноцветным текстом.
        public static void DrawSectionHeader(string icon, string title)
        {
            GUILayout.Space(14);
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10, fontStyle = FontStyle.Bold,
                normal = { textColor = Accent }
            };
            string label = string.IsNullOrEmpty(icon)
                ? title.ToUpperInvariant()
                : icon + "  " + title.ToUpperInvariant();
            GUILayout.Label(label, st);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, Border);
            GUILayout.Space(8);
        }

        // ─── Card ──────────────────────────────────────────────────────────
        // Заменяет `EditorGUILayout.BeginVertical(EditorStyles.helpBox)`.
        // Содержимое рисуется внутри callback'а — caller не парится с
        // BeginVertical/EndVertical парами.
        public static void DrawCard(Action drawContent, int paddingX = 10, int paddingY = 8)
        {
            // Резервируем место и измеряем содержимое по факту.
            var cardStyle = new GUIStyle {
                padding = new RectOffset(paddingX, paddingX, paddingY, paddingY),
                margin = new RectOffset(0, 0, 0, 4),
            };
            GUILayout.BeginVertical(cardStyle);
            // Рисуем фон+рамку через DrawRect ПОСЛЕ контента — Layout-система
            // даст нам прямоугольник на Repaint phase.
            Rect bg = new Rect();
            if (Event.current.type != EventType.Layout)
            {
                bg = GUILayoutUtility.GetLastRect();
            }
            // Контент сверху.
            drawContent?.Invoke();
            GUILayout.EndVertical();

            // На Repaint — рисуем bg/рамку поверх текущего rect'а.
            if (Event.current.type == EventType.Repaint)
            {
                Rect last = GUILayoutUtility.GetLastRect();
                EditorGUI.DrawRect(last, new Color(BgRaised.r, BgRaised.g, BgRaised.b, 0.6f));
                DrawBorder(last, Border);
            }
        }

        // ─── Hint (синяя подсказка с акцент-полосой слева) ─────────────────
        // Заменяет `EditorGUILayout.HelpBox(text, MessageType.Info)`.
        public static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                fontSize = 10,
                normal = { textColor = Text2 },
                padding = new RectOffset(10, 8, 6, 6),
            };
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(Accent.r, Accent.g, Accent.b, 0.06f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 2, r.height), new Color(Accent.r, Accent.g, Accent.b, 0.55f));
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(4);
        }

        // ─── Warn (оранжевая/красная плашка) ────────────────────────────────
        // Заменяет `EditorGUILayout.HelpBox(text, MessageType.Warning)`.
        public static void DrawWarn(string text, bool danger = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color accent = danger ? Danger : Warning;
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                fontSize = 11, fontStyle = FontStyle.Bold,
                normal = { textColor = accent },
                padding = new RectOffset(10, 8, 6, 6),
            };
            string prefix = danger ? "⚠ " : "ⓘ ";
            Rect r = GUILayoutUtility.GetRect(new GUIContent(prefix + text), st, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(accent.r, accent.g, accent.b, 0.10f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), accent);
            GUI.Label(r, prefix + text, st);
            GUILayout.Space(4);
        }

        // ─── Field label ────────────────────────────────────────────────────
        // Маленький bold-заголовок 9pt над полем ввода.
        public static void DrawFieldLabel(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 9, fontStyle = FontStyle.Bold,
                normal = { textColor = Text3 }
            };
            GUILayout.Label(text, st);
            GUILayout.Space(2);
        }

        // ─── Slim button (тонкая обводка + hover-fill) ─────────────────────
        // Замена `EditorStyles.miniButton` для большинства тулбар-кнопок.
        public static bool DrawSlimBtn(string label, params GUILayoutOption[] options)
        {
            return DrawColoredBtn(label, fill: false, danger: false, options);
        }

        public static bool DrawAccentBtn(string label, params GUILayoutOption[] options)
        {
            return DrawColoredBtn(label, fill: true, danger: false, options);
        }

        public static bool DrawDangerBtn(string label, params GUILayoutOption[] options)
        {
            return DrawColoredBtn(label, fill: false, danger: true, options);
        }

        private static bool DrawColoredBtn(string label, bool fill, bool danger, params GUILayoutOption[] options)
        {
            float h = 26;
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button,
                AppendDefaultHeight(options, h));

            bool hovered = r.Contains(Event.current.mousePosition);

            Color textCol;
            Color borderCol;

            if (fill)
            {
                // ─── Accent ghost-CTA ───
                // Soft accent-fill (22%/32% on hover) + accent-border + accent-text.
                // Раньше был сплошной cyan что слепило при крупном размере;
                // теперь приглушённо но всё ещё «primary».
                EditorGUI.DrawRect(r, hovered
                    ? new Color(Accent.r, Accent.g, Accent.b, 0.32f)
                    : new Color(Accent.r, Accent.g, Accent.b, 0.18f));
                borderCol = Accent;
                textCol   = Accent;
                // Двойная толщина рамки для CTA — визуально «primary».
                DrawBorder(r, borderCol);
                EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, 1), borderCol);
            }
            else if (danger)
            {
                // ─── Danger button ───
                // Idle: прозрачный фон, danger-rim. Hover: tinted danger fill.
                if (hovered)
                {
                    EditorGUI.DrawRect(r, new Color(Danger.r, Danger.g, Danger.b, 0.16f));
                }
                borderCol = new Color(Danger.r, Danger.g, Danger.b, hovered ? 0.85f : 0.55f);
                textCol   = new Color(Danger.r, Danger.g, Danger.b, hovered ? 1.0f  : 0.85f);
                DrawBorder(r, borderCol);
            }
            else
            {
                // ─── Regular slim button ───
                // Idle: прозрачный фон, gray-border, muted Text2.
                // Hover: cyan-tinted bg (Lerp Accent 12%) + Accent-tinted border
                // + bright Text1. Раньше hover был просто Text1*6% что давало
                // bw-ощущение — теперь видно что «эта кнопка живая, ведёт куда-то».
                if (hovered)
                {
                    Color hoverBg = new Color(
                        Mathf.Lerp(BgRaised.r, Accent.r, 0.14f),
                        Mathf.Lerp(BgRaised.g, Accent.g, 0.14f),
                        Mathf.Lerp(BgRaised.b, Accent.b, 0.14f),
                        0.55f);
                    EditorGUI.DrawRect(r, hoverBg);
                    borderCol = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
                    textCol = Text1;
                }
                else
                {
                    borderCol = Border;
                    textCol = Text2;
                }
                DrawBorder(r, borderCol);
            }

            var st = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = textCol }
            };
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && hovered && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        // ─── Icon button (квадратная 26×26) ─────────────────────────────────
        public static bool DrawIconBtn(string icon, string tooltip = "", bool danger = false,
                                        float size = 26)
        {
            return DrawColoredBtn(icon, fill: false, danger: danger,
                GUILayout.Width(size), GUILayout.Height(size));
        }

        // ─── Dark text field ────────────────────────────────────────────────
        // Стилизованный TextField: тёмный bg, тонкая рамка Border, 28h.
        public static string DrawDarkTextField(string value, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField,
                AppendDefaultHeight(options, 28));
            EditorGUI.DrawRect(r, BgPrimary);
            DrawBorder(r, Border);

            var st = new GUIStyle(EditorStyles.textField) {
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6),
                normal  = { background = null, textColor = Text1 },
                focused = { background = null, textColor = Text1 },
                hover   = { background = null, textColor = Text1 },
                active  = { background = null, textColor = Text1 },
            };
            Rect inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            return EditorGUI.TextField(inner, value ?? "", st);
        }

        public static string DrawDarkTextArea(string value, float height = 60, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textArea,
                AppendDefaultHeight(options, height));
            EditorGUI.DrawRect(r, BgPrimary);
            DrawBorder(r, Border);

            var st = new GUIStyle(EditorStyles.textArea) {
                fontSize = 12, wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6),
                normal  = { background = null, textColor = Text1 },
                focused = { background = null, textColor = Text1 },
            };
            Rect inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            return EditorGUI.TextArea(inner, value ?? "", st);
        }

        // ─── Separator ──────────────────────────────────────────────────────
        public static void DrawSeparator(int spacingTop = 8, int spacingBottom = 8)
        {
            GUILayout.Space(spacingTop);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(Border.r, Border.g, Border.b, 0.5f));
            GUILayout.Space(spacingBottom);
        }

        // ─── Helpers ────────────────────────────────────────────────────────
        public static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        // Добавляет default Height-option если его не передали.
        // Чтобы caller'у не приходилось каждый раз писать GUILayout.Height(...).
        private static GUILayoutOption[] AppendDefaultHeight(GUILayoutOption[] opts, float defaultH)
        {
            if (opts == null) return new[] { GUILayout.Height(defaultH) };
            // Поверхностный hint: добавляем только если высота не задана —
            // но GUILayoutOption не разбирается читабельно, поэтому всегда
            // добавляем; Unity берёт первое подходящее.
            var combined = new GUILayoutOption[opts.Length + 1];
            opts.CopyTo(combined, 0);
            combined[opts.Length] = GUILayout.Height(defaultH);
            return combined;
        }
    }
}
