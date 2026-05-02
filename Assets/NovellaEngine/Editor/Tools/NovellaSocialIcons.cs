// ════════════════════════════════════════════════════════════════════════════
// NovellaSocialIcons — иконки Telegram и Discord для окна жалобы.
//
// Стратегия загрузки:
//   1. Если в Assets/NovellaEngine/Resources/SocialIcons/ лежит
//      telegram.png и/или discord.png — используем их (PNG с альфа-каналом
//      и без фона). Это идеальный путь — реальные брендовые лого.
//   2. Если файлов нет — fallback на программно нарисованные иконки.
//      Они узнаваемы, но не идеальны — лучше положить настоящие PNG.
//
// Программные иконки кэшируются в HideAndDontSave-текстурах (не попадают
// в проект, не плодят .meta).
// ════════════════════════════════════════════════════════════════════════════

using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public static class NovellaSocialIcons
    {
        private const int SIZE = 64;

        // Папка где юзер кладёт настоящие PNG-иконки.
        // Лежит в Resources/, а не Editor/ — чтобы их можно было использовать
        // и из других мест проекта при необходимости.
        private const string ICON_DIR = "Assets/NovellaEngine/Resources/SocialIcons/";

        // Бренд-цвета (используются программными fallback-иконками).
        public static readonly Color TelegramBlue = new Color(0.13f, 0.62f, 0.85f); // #229ED9
        public static readonly Color DiscordBlurple = new Color(0.34f, 0.40f, 0.95f); // #5865F2

        private static Texture2D _telegram, _discord;

        public static Texture2D Telegram
        {
            get
            {
                if (_telegram != null) return _telegram;
                _telegram = LoadIcon("telegram") ?? BuildTelegram();
                return _telegram;
            }
        }

        public static Texture2D Discord
        {
            get
            {
                if (_discord != null) return _discord;
                _discord = LoadIcon("discord") ?? BuildDiscord();
                return _discord;
            }
        }

        // Сброс кэша — позвать после того как юзер положит PNG в папку,
        // чтобы новая иконка подхватилась без перезапуска Studio.
        public static void Reload()
        {
            _telegram = null;
            _discord  = null;
        }

        // Пытается загрузить PNG/JPG-файл иконки.
        // 1. AssetDatabase.LoadAssetAtPath — стандартный способ.
        // 2. EnsureCorrectImportSettings правит настройки через TextureImporter
        //    если они не наши, и при правке вызывает SaveAndReimport().
        // 3. После SaveAndReimport старая ссылка Texture2D становится
        //    «destroyed companion» — поэтому, если настройки были изменены,
        //    мы СНОВА вызываем LoadAssetAtPath чтобы получить свежий валидный
        //    объект. Иначе DrawTexture рисует пустоту, и юзер видит fallback.
        private static Texture2D LoadIcon(string baseName)
        {
            string[] extensions = { ".png", ".jpg", ".jpeg" };
            foreach (var ext in extensions)
            {
                string path = ICON_DIR + baseName + ext;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                bool changed = EnsureCorrectImportSettings(path);
                if (changed)
                {
                    // Старый tex может быть инвалидирован после SaveAndReimport.
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                return tex;
            }
            return null;
        }

        // Чинит настройки импорта на конкретном файле. Возвращает true если
        // настройки реально были изменены и SaveAndReimport отработал
        // (тогда вызывающему стоит перечитать ассет).
        private static bool EnsureCorrectImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;

            bool dirty = false;
            if (importer.textureType        != TextureImporterType.Default)         { importer.textureType        = TextureImporterType.Default;         dirty = true; }
            if (importer.alphaSource        != TextureImporterAlphaSource.FromInput){ importer.alphaSource        = TextureImporterAlphaSource.FromInput; dirty = true; }
            if (!importer.alphaIsTransparency)                                       { importer.alphaIsTransparency = true;                                dirty = true; }
            if (importer.mipmapEnabled)                                              { importer.mipmapEnabled    = false;                                  dirty = true; }
            if (importer.npotScale          != TextureImporterNPOTScale.None)        { importer.npotScale        = TextureImporterNPOTScale.None;          dirty = true; }
            if (importer.wrapMode           != TextureWrapMode.Clamp)                { importer.wrapMode         = TextureWrapMode.Clamp;                  dirty = true; }
            if (importer.filterMode         != FilterMode.Bilinear)                  { importer.filterMode       = FilterMode.Bilinear;                    dirty = true; }
            if (importer.maxTextureSize     > 256)                                   { importer.maxTextureSize   = 256;                                    dirty = true; }

            var settings = importer.GetDefaultPlatformTextureSettings();
            if (settings.format             != TextureImporterFormat.RGBA32 ||
                settings.textureCompression != TextureImporterCompression.Uncompressed)
            {
                settings.format             = TextureImporterFormat.RGBA32;
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(settings);
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
            return dirty;
        }

        // ─── Telegram ──────────────────────────────────────────────────
        // Голубой круг + белый бумажный самолётик. По сравнению с прошлой
        // версией стрелка чуть уже (горизонтальный размах ~30px вместо 37px).
        private static Texture2D BuildTelegram()
        {
            var t = MakeTex();
            FillCircle(t, 32, 32, 29, TelegramBlue);

            // Самолётик. Координаты сужены: x от 18 до 48 (раньше 15–52).
            //   Хвост:        (18, 36)  — задний верх-левый
            //   Нос:          (48, 42)  — острый передний-правый
            //   Сложка крыла: (32, 33)  — «корешок» бумажного сложения
            //   Подвес:       (30, 20)  — нижний излом крыла
            FillTriangle(t,
                new Vector2(18, 36),
                new Vector2(48, 42),
                new Vector2(32, 33),
                Color.white);
            FillTriangle(t,
                new Vector2(32, 33),
                new Vector2(48, 42),
                new Vector2(30, 20),
                Color.white);

            t.Apply(false);
            return t;
        }

        // ─── Discord ───────────────────────────────────────────────────
        // Возврат к простому варианту: фиолетовый «squircle»-фон + белая
        // закруглённая «маска» внутри + два овальных глаза цветом фона.
        // Без «ножек»-геймпада — программно они получались криво,
        // лучше потом заменить на настоящий PNG из папки SocialIcons.
        private static Texture2D BuildDiscord()
        {
            var t = MakeTex();

            // Фон — фиолетовый закруглённый квадрат.
            FillRoundedRect(t, 4, 4, 60, 60, 14, DiscordBlurple);

            // Внутренняя белая маска.
            FillRoundedRect(t, 14, 18, 50, 46, 11, Color.white);

            // Глаза — две вертикально-вытянутые капельки цветом фона.
            FillCircle(t, 24, 30, 3.2f, DiscordBlurple);
            FillCircle(t, 24, 32, 3.2f, DiscordBlurple);
            FillCircle(t, 40, 30, 3.2f, DiscordBlurple);
            FillCircle(t, 40, 32, 3.2f, DiscordBlurple);

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
