// ════════════════════════════════════════════════════════════════════════════
// NovellaSocialIcons — программно сгенерированные иконки Telegram и Discord.
// Аналогично NovellaHubIcons, но в 2 цветах (бренд + белый/тёмный) — чтобы
// получился узнаваемый логотип без ручного импорта PNG.
//
// Кэшируются в HideAndDontSave-текстурах: не попадают в проект, не плодят
// .meta-файлов, ребилдятся при первом обращении после reload скриптов.
// ════════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public static class NovellaSocialIcons
    {
        private const int SIZE = 64;

        // Бренд-цвета. Telegram — фирменный голубой, Discord — Blurple.
        public static readonly Color TelegramBlue = new Color(0.13f, 0.62f, 0.85f); // #229ED9
        public static readonly Color DiscordBlurple = new Color(0.34f, 0.40f, 0.95f); // #5865F2

        private static Texture2D _telegram, _discord;

        public static Texture2D Telegram => _telegram != null ? _telegram : (_telegram = BuildTelegram());
        public static Texture2D Discord  => _discord  != null ? _discord  : (_discord  = BuildDiscord());

        // ─── Telegram ──────────────────────────────────────────────────
        // Голубой круг + белый бумажный самолётик. Силуэт центрирован
        // относительно круга. Y в Texture2D идёт снизу-вверх.
        private static Texture2D BuildTelegram()
        {
            var t = MakeTex();
            FillCircle(t, 32, 32, 29, TelegramBlue);

            // Бумажный самолётик. Параметры подобраны под центр (32,32),
            // силуэт занимает ~36 пикселей в ширину и центрирован.
            // Координаты в системе «Y вверх».
            //   Хвост:        (15, 35)  — задний верх-левый
            //   Подвес:       (28, 18)  — нижний излом крыла
            //   Нос:          (52, 42)  — острый передний-правый
            //   Сложка крыла: (32, 33)  — «корешок» бумажного сложения
            //
            // Левый большой треугольник — основное тело крыла.
            FillTriangle(t,
                new Vector2(15, 35),
                new Vector2(52, 42),
                new Vector2(32, 33),
                Color.white);
            // Правый малый — нижняя часть крыла («складка»).
            FillTriangle(t,
                new Vector2(32, 33),
                new Vector2(52, 42),
                new Vector2(28, 18),
                Color.white);

            t.Apply(false);
            return t;
        }

        // ─── Discord ───────────────────────────────────────────────────
        // Фиолетовая «маска» — горизонтально-вытянутая капсула с двумя
        // нижними «ушками». Внутри два глаза-капельки. Узнаваемо.
        private static Texture2D BuildDiscord()
        {
            var t = MakeTex();

            // Тело — закругленный прямоугольник 52×30 в центре.
            FillRoundedRect(t, 6, 18, 58, 50, 9, DiscordBlurple);

            // Два «ушка» — небольшие круги под телом, создают характерный силуэт.
            FillCircle(t, 18, 14, 6, DiscordBlurple);
            FillCircle(t, 46, 14, 6, DiscordBlurple);

            // Глаза — две большие белые точки. У оригинала глаза немного
            // вытянуты, но круглые тоже узнаваемо.
            FillCircle(t, 24, 36, 5, Color.white);
            FillCircle(t, 40, 36, 5, Color.white);

            t.Apply(false);
            return t;
        }

        // ─── Примитивы рисования ──────────────────────────────────────
        private static Texture2D MakeTex()
        {
            var t = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                hideFlags  = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
            var pixels = new Color[SIZE * SIZE];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0, 0, 0, 0);
            t.SetPixels(pixels);
            return t;
        }

        // Закрашенный круг с антиалиасингом в один пиксель шириной.
        private static void FillCircle(Texture2D t, int cx, int cy, float r, Color c)
        {
            int x0 = Mathf.Max(0, (int)Mathf.Floor(cx - r - 1));
            int x1 = Mathf.Min(SIZE - 1, (int)Mathf.Ceil(cx + r + 1));
            int y0 = Mathf.Max(0, (int)Mathf.Floor(cy - r - 1));
            int y1 = Mathf.Min(SIZE - 1, (int)Mathf.Ceil(cy + r + 1));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                float a;
                if (d <= r - 0.5f)      a = 1f;
                else if (d >= r + 0.5f) continue;
                else                    a = (r + 0.5f) - d; // soft edge

                Color existing = t.GetPixel(x, y);
                Color blend = Color.Lerp(existing, c, a * c.a);
                t.SetPixel(x, y, blend);
            }
        }

        // Закрашенный треугольник по barycentric. Без AA — для крупных форм.
        private static void FillTriangle(Texture2D t, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int minX = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(SIZE - 1, (int)Mathf.Ceil(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(SIZE - 1, (int)Mathf.Ceil(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            float denom = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));
            if (Mathf.Abs(denom) < 0.0001f) return;

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float w0 = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / denom;
                float w1 = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / denom;
                float w2 = 1f - w0 - w1;
                if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                {
                    Color existing = t.GetPixel(x, y);
                    t.SetPixel(x, y, Color.Lerp(existing, color, color.a));
                }
            }
        }

        // Закрашенный прямоугольник со скруглёнными углами.
        // (x,y)-(x+w,y+h), радиус скругления r.
        private static void FillRoundedRect(Texture2D t, int x, int y, int xMax, int yMax, int r, Color c)
        {
            // Центральная часть — обычный прямоугольник.
            for (int yy = y + r; yy <= yMax - r; yy++)
            for (int xx = x; xx <= xMax; xx++)
                if (xx >= 0 && xx < SIZE && yy >= 0 && yy < SIZE)
                    t.SetPixel(xx, yy, c);
            // Левая/правая полоски без углов.
            for (int yy = y; yy <= yMax; yy++)
            for (int xx = x + r; xx <= xMax - r; xx++)
                if (xx >= 0 && xx < SIZE && yy >= 0 && yy < SIZE)
                    t.SetPixel(xx, yy, c);
            // 4 угла — четверть-круги.
            FillCircle(t, x    + r, y    + r, r, c);
            FillCircle(t, xMax - r, y    + r, r, c);
            FillCircle(t, x    + r, yMax - r, r, c);
            FillCircle(t, xMax - r, yMax - r, r, c);
        }
    }
}
