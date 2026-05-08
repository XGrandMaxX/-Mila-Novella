// ════════════════════════════════════════════════════════════════════════════
// NovellaToolbarBeacon — большая всплывающая плашка под тулбар-кнопкой
// «🚀 Novella», которая помогает юзеру, случайно закрывшему Studio, найти
// эту маленькую кнопку обратно.
//
// Всплывает как frameless EditorWindow popup сразу после закрытия Hub'а:
// большая стрелка ↑ вверх, текст «Novella Studio открыть здесь», пульсация,
// автозакрытие через ~5 секунд или клик по плашке (клик заодно открывает Hub).
//
// Почему отдельный EditorWindow popup, а не in-toolbar VisualElement:
// тулбар Unity клипует overflow по вертикали, и любой большой callout-элемент
// внутри тулбар-панели обрезается. Popup живёт на screen-coords и виден всем.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaToolbarBeacon : EditorWindow
    {
        private const float DURATION = 5f;
        private const float W = 360f;
        private const float H = 96f;

        private double _t0;
        private static NovellaToolbarBeacon _activeBeacon;

        public static void Show(Rect targetButtonScreenRect)
        {
            // Single-instance: если уже есть открытый beacon — закрываем старый.
            DismissAll();

            var win = CreateInstance<NovellaToolbarBeacon>();
            win.titleContent = new GUIContent("");

            // Позиционируем плашку прямо под кнопкой (центр по X, +12px по Y).
            float x = targetButtonScreenRect.center.x - W / 2f;
            float y = targetButtonScreenRect.yMax + 12f;
            // Прижимаем по краям главного editor-окна (не по Screen, т.к. при
            // мульти-мониторной настройке Screen.currentResolution даёт только
            // один экран — и на втором мониторе beacon уехал бы не туда).
            Rect mainWnd = EditorGUIUtility.GetMainWindowPosition();
            float minX = mainWnd.x + 4;
            float maxX = mainWnd.xMax - W - 4;
            if (x < minX) x = minX;
            if (x > maxX) x = maxX;

            win.position = new Rect(x, y, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(W, H);
            win._t0 = EditorApplication.timeSinceStartup;
            win.ShowPopup();
            _activeBeacon = win;

            EditorApplication.update -= win.Tick;
            EditorApplication.update += win.Tick;
        }

        public static void DismissAll()
        {
            if (_activeBeacon != null)
            {
                EditorApplication.update -= _activeBeacon.Tick;
                _activeBeacon.Close();
                _activeBeacon = null;
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (_activeBeacon == this) _activeBeacon = null;
        }

        private void Tick()
        {
            if (this == null) { EditorApplication.update -= Tick; return; }
            double elapsed = EditorApplication.timeSinceStartup - _t0;
            if (elapsed > DURATION)
            {
                EditorApplication.update -= Tick;
                Close();
                return;
            }
            Repaint();
        }

        private void OnGUI()
        {
            float t = (float)(EditorApplication.timeSinceStartup - _t0);

            // Палитра — берём из Settings чтобы цвет совпадал с тулбар-кнопкой.
            Color accent = NovellaSettingsModule.GetAccentColor();
            Color textCol = NovellaSettingsModule.GetContrastingText(accent);
            Color bgDark = new Color(0.075f, 0.082f, 0.11f, 1f);

            // Fade-in 0..200ms, fade-out последние 600ms.
            float opacity;
            if (t < 0.2f) opacity = t / 0.2f;
            else if (t > DURATION - 0.6f) opacity = Mathf.Max(0f, (DURATION - t) / 0.6f);
            else opacity = 1f;

            // Bounce-эффект: вертикальное покачивание первые 1с — ловим взгляд.
            float bounceY = 0f;
            if (t < 1f)
            {
                float decay = 1f - t / 1f;
                bounceY = Mathf.Sin(t * 14f) * 6f * decay;
            }

            // ─── Фон плашки (тёмный с акцентной рамкой) ───
            Rect full = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(full, new Color(bgDark.r, bgDark.g, bgDark.b, opacity * 0.97f));
            // Рамка 2px акцентного цвета.
            DrawBorder(full, new Color(accent.r, accent.g, accent.b, opacity), 2);

            // Пульсирующая внутренняя «свечение»-полоса слева.
            float pulse = (Mathf.Sin(t * 5f) * 0.5f + 0.5f);
            EditorGUI.DrawRect(new Rect(0, 0, 4, full.height),
                new Color(accent.r, accent.g, accent.b, opacity * Mathf.Lerp(0.4f, 1f, pulse)));

            // ─── Большая стрелка ↑ слева ───
            Rect arrowRect = new Rect(16, 16 + bounceY, 56, 56);
            // Светящийся круг под стрелкой.
            EditorGUI.DrawRect(arrowRect,
                new Color(accent.r, accent.g, accent.b, opacity * Mathf.Lerp(0.18f, 0.30f, pulse)));
            DrawBorder(arrowRect, new Color(accent.r, accent.g, accent.b, opacity * 0.7f), 1);

            var arrowSt = new GUIStyle(EditorStyles.label) {
                fontSize = 32, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(accent.r, accent.g, accent.b, opacity) }
            };
            GUI.Label(arrowRect, "↑", arrowSt);

            // ─── Текст справа от стрелки ───
            // Заголовок жирный, акцентным цветом.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 14, alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(accent.r, accent.g, accent.b, opacity) }
            };
            GUI.Label(new Rect(82, 14 + bounceY, position.width - 90, 22),
                ToolLang.Get("Novella Studio is here", "Студия — здесь!"), titleSt);

            // Подзаголовок объясняет КАК открыть.
            var subSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true,
                normal = { textColor = new Color(0.92f, 0.95f, 0.98f, opacity * 0.88f) }
            };
            GUI.Label(new Rect(82, 38 + bounceY, position.width - 90, 50),
                ToolLang.Get(
                    "Click the «🚀 Novella» button above\nor click here to reopen.",
                    "Нажми кнопку «🚀 Novella» сверху\nили кликни сюда чтобы открыть."), subSt);

            // ─── Клик по любому месту плашки — открыть Studio ───
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && full.Contains(Event.current.mousePosition))
            {
                Close();
                EditorApplication.delayCall += () => NovellaHubWindow.ShowWindow();
                Event.current.Use();
            }

            // Cursor-link на всю плашку чтобы юзер сразу видел кликабельность.
            EditorGUIUtility.AddCursorRect(full, MouseCursor.Link);
        }

        private static void DrawBorder(Rect r, Color c, int thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
        }
    }
}
