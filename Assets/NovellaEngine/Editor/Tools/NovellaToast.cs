// ════════════════════════════════════════════════════════════════════════════
// NovellaToast — лёгкая система всплывающих уведомлений в стиле проекта.
// Хосты-окна вызывают NovellaToast.DrawOverlay(rect) у себя в OnGUI, и тост
// рисуется поверх содержимого. Сами тосты ставятся через NovellaToast.Show*
// из любого editor-кода.
//
// Зачем: дублирует короткое сообщение из Debug.Log в виде ненавязчивого попапа,
// чтобы юзер видел подтверждение действия (сохранил префаб / создал объект /
// и т.п.), не отвлекаясь на консоль Unity.
// ════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public static class NovellaToast
    {
        public enum Kind { Info, Success, Warning, Error }

        private class Entry
        {
            public string Text;
            public Kind   Type;
            public double Spawn;
            public float  Lifetime;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private const float DEFAULT_LIFETIME = 2.0f;
        private const float FADE_DURATION   = 0.25f;
        private const float SLIDE_DURATION  = 0.20f;
        private const int   MAX_VISIBLE     = 4;
        private const int   MAX_LEN         = 110;

        // Базовые API.
        public static void Info(string msg)    => Push(msg, Kind.Info,    DEFAULT_LIFETIME);
        public static void Success(string msg) => Push(msg, Kind.Success, DEFAULT_LIFETIME);
        public static void Warning(string msg) => Push(msg, Kind.Warning, DEFAULT_LIFETIME + 0.5f);
        public static void Error(string msg)   => Push(msg, Kind.Error,   DEFAULT_LIFETIME + 1.0f);

        public static void Push(string text, Kind type, float lifetimeSec)
        {
            if (string.IsNullOrEmpty(text)) return;

            string trimmed = text.Trim().Replace("\r\n", " ").Replace('\n', ' ');
            if (trimmed.Length > MAX_LEN) trimmed = trimmed.Substring(0, MAX_LEN - 1) + "…";

            _entries.Add(new Entry
            {
                Text     = trimmed,
                Type     = type,
                Spawn    = EditorApplication.timeSinceStartup,
                Lifetime = Mathf.Max(0.5f, lifetimeSec),
            });

            // Жёсткий лимит видимости — самые старые отбрасываем.
            while (_entries.Count > MAX_VISIBLE) _entries.RemoveAt(0);

            EditorApplication.RepaintHierarchyWindow();
            // Подталкиваем редактор к репайнту чтобы анимация шла плавно.
            if (_lastHostWindow != null) _lastHostWindow.Repaint();
        }

        // Хост-окно вызывает это у себя в OnGUI, передавая свой клиентский Rect
        // (обычно rect центральной области, либо position-based).
        // hostWindow используется для авто-репайнта во время анимации.
        public static void DrawOverlay(Rect hostRect, EditorWindow hostWindow = null)
        {
            _lastHostWindow = hostWindow;

            // Удаляем устаревшие.
            double now = EditorApplication.timeSinceStartup;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (now - e.Spawn > e.Lifetime + FADE_DURATION) _entries.RemoveAt(i);
            }
            if (_entries.Count == 0) return;

            // Раскладываем снизу-справа, новые — выше старых.
            const float W = 320f;
            const float H = 44f;
            const float MARGIN = 14f;
            const float GAP = 8f;

            float baseY = hostRect.yMax - MARGIN - H;
            float x = hostRect.xMax - MARGIN - W;
            if (x < hostRect.x + 8) x = hostRect.x + 8;

            // Перерисовываем хост во время анимаций.
            bool needsRepaint = false;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                float age = (float)(now - e.Spawn);
                float remaining = e.Lifetime + FADE_DURATION - age;

                // Slide-in.
                float slideT = Mathf.Clamp01(age / SLIDE_DURATION);
                float slideEase = 1f - (1f - slideT) * (1f - slideT);
                float xOffset = (1f - slideEase) * 60f;

                // Fade-out на конце жизни.
                float fadeT = Mathf.Clamp01(remaining / FADE_DURATION);
                float alpha = Mathf.Min(slideEase, fadeT);

                if (slideT < 1f || fadeT < 1f) needsRepaint = true;

                int slot = (_entries.Count - 1) - i;
                float y = baseY - slot * (H + GAP);
                Rect r = new Rect(x + xOffset, y, W, H);

                DrawToast(r, e, alpha);
            }

            if (needsRepaint && hostWindow != null) hostWindow.Repaint();
        }

        private static EditorWindow _lastHostWindow;

        private static void DrawToast(Rect r, Entry e, float alpha)
        {
            if (alpha <= 0.01f) return;

            // Палитра — стиль остального интерфейса.
            Color accent;
            string icon;
            switch (e.Type)
            {
                case Kind.Success: accent = new Color(0.40f, 0.78f, 0.45f); icon = "✔"; break;
                case Kind.Warning: accent = new Color(0.96f, 0.70f, 0.30f); icon = "⚠"; break;
                case Kind.Error:   accent = new Color(0.92f, 0.36f, 0.36f); icon = "✕"; break;
                default:           accent = NovellaSettingsModule.GetAccentColor(); icon = "ℹ"; break;
            }

            Color bg     = NovellaSettingsModule.GetBgRaisedColor();
            Color border = NovellaSettingsModule.GetBorderColor();
            Color text1  = NovellaSettingsModule.GetTextColor();

            // Фон + тонкая обводка + акцентная полоса слева.
            EditorGUI.DrawRect(r, new Color(bg.r, bg.g, bg.b, bg.a * alpha));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1),               new Color(border.r, border.g, border.b, alpha));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1),         new Color(border.r, border.g, border.b, alpha));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height),               new Color(border.r, border.g, border.b, alpha));
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height),        new Color(border.r, border.g, border.b, alpha));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height),               new Color(accent.r, accent.g, accent.b, alpha));

            // Иконка типа.
            var iconSt = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
            };
            iconSt.normal.textColor = new Color(accent.r, accent.g, accent.b, alpha);
            GUI.Label(new Rect(r.x + 8, r.y, 28, r.height), icon, iconSt);

            // Текст сообщения.
            var textSt = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(0, 6, 4, 4),
            };
            textSt.normal.textColor = new Color(text1.r, text1.g, text1.b, alpha);
            GUI.Label(new Rect(r.x + 38, r.y, r.width - 44, r.height), e.Text, textSt);
        }

        // Для тестов / ручного сброса.
        public static void Clear() => _entries.Clear();
    }
}
