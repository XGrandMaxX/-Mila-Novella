using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Программно сгенерированные outline-иконки 32x32 (white) для нового Hub UI.
    /// Окрашиваются через USS-свойство -unity-background-image-tint-color.
    ///
    /// Почему не SVG: UI Toolkit не поддерживает data: URLs и не имеет рантайм-парсера
    /// SVG. Стандартный путь — VectorImageAsset (.svg импортируется как TextAsset),
    /// но это требует raw-файлов в проекте.
    ///
    /// Текстуры кэшируются в HideAndDontSave — не попадают в проект, не сериализуются,
    /// не плодят .meta-файлы.
    /// </summary>
    public static class NovellaHubIcons
    {
        public enum Icon { Home, Characters, Scenes, UIEditor, Variables, Graph, Dialogues, Gallery, Search, Menu, Story, Plus, Lightbulb }

        public const Icon Home = Icon.Home;
        public const Icon Characters = Icon.Characters;
        public const Icon Scenes = Icon.Scenes;
        public const Icon UIEditor = Icon.UIEditor;
        public const Icon Variables = Icon.Variables;
        public const Icon Graph = Icon.Graph;
        public const Icon Dialogues = Icon.Dialogues;
        public const Icon Gallery = Icon.Gallery;
        public const Icon Search = Icon.Search;
        public const Icon Menu = Icon.Menu;
        public const Icon Story = Icon.Story;
        public const Icon Plus = Icon.Plus;
        public const Icon Lightbulb = Icon.Lightbulb;

        private const int SIZE = 32;
        private const int STROKE = 2;

        private static readonly Dictionary<Icon, Texture2D> _cache = new Dictionary<Icon, Texture2D>();

        public static Texture2D GetTexture(Icon icon)
        {
            if (_cache.TryGetValue(icon, out var t) && t != null) return t;

            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            ClearTransparent(tex);
            DrawIcon(tex, icon);
            tex.Apply(false);
            _cache[icon] = tex;
            return tex;
        }

        private static void ClearTransparent(Texture2D tex)
        {
            var clear = new Color[SIZE * SIZE];
            for (int i = 0; i < clear.Length; i++) clear[i] = new Color(0, 0, 0, 0);
            tex.SetPixels(clear);
        }

        private static void DrawIcon(Texture2D tex, Icon icon)
        {
            // Берём 32×32 канвас, рисуем в координатах 0..32 (0,0 = верхний-левый).
            // SetPx инвертирует Y под Unity.
            switch (icon)
            {
                case Icon.Home:
                    // Домик: треугольная крыша + квадратное основание
                    DrawLine(tex, 4, 14, 16, 4);
                    DrawLine(tex, 16, 4, 28, 14);
                    DrawLine(tex, 6, 12, 6, 28);
                    DrawLine(tex, 26, 12, 26, 28);
                    DrawLine(tex, 6, 28, 26, 28);
                    break;
                case Icon.Characters:
                    // Голова + плечи
                    DrawCircle(tex, 16, 11, 5);
                    DrawArc(tex, 6, 28, 16, 22, 26, 28);
                    break;
                case Icon.Scenes:
                    // Прямоугольник сцены + треугольник play
                    DrawRect(tex, 4, 7, 24, 18);
                    DrawTriangleFilled(tex, 13, 12, 13, 22, 22, 17);
                    break;
                case Icon.UIEditor:
                    // Куб (как иконка пакета)
                    DrawLine(tex, 16, 4, 4, 10);
                    DrawLine(tex, 16, 4, 28, 10);
                    DrawLine(tex, 4, 10, 4, 22);
                    DrawLine(tex, 28, 10, 28, 22);
                    DrawLine(tex, 4, 22, 16, 28);
                    DrawLine(tex, 16, 28, 28, 22);
                    DrawLine(tex, 4, 10, 16, 16);
                    DrawLine(tex, 28, 10, 16, 16);
                    DrawLine(tex, 16, 16, 16, 28);
                    break;
                case Icon.Variables:
                    // Прямоугольник + 2 строчки
                    DrawRect(tex, 4, 6, 24, 20);
                    DrawLine(tex, 9, 13, 23, 13);
                    DrawLine(tex, 9, 19, 19, 19);
                    break;
                case Icon.Graph:
                    // 3 узла соединённых линиями
                    DrawCircle(tex, 8, 8, 2);
                    DrawCircle(tex, 24, 8, 2);
                    DrawCircle(tex, 16, 24, 2);
                    DrawLine(tex, 9, 9, 15, 22);
                    DrawLine(tex, 23, 9, 17, 22);
                    DrawLine(tex, 10, 8, 22, 8);
                    break;
                case Icon.Dialogues:
                    // Speech bubble
                    DrawLine(tex, 6, 7, 26, 7);
                    DrawLine(tex, 26, 7, 26, 19);
                    DrawLine(tex, 26, 19, 14, 19);
                    DrawLine(tex, 14, 19, 9, 25);
                    DrawLine(tex, 9, 25, 11, 19);
                    DrawLine(tex, 11, 19, 6, 19);
                    DrawLine(tex, 6, 19, 6, 7);
                    break;
                case Icon.Gallery:
                    // Картинка с горой и солнцем
                    DrawRect(tex, 4, 7, 24, 18);
                    DrawCircle(tex, 11, 13, 1);
                    DrawLine(tex, 6, 22, 14, 14);
                    DrawLine(tex, 14, 14, 19, 19);
                    DrawLine(tex, 19, 19, 26, 12);
                    break;
                case Icon.Search:
                    DrawCircle(tex, 13, 13, 7);
                    DrawLine(tex, 18, 18, 26, 26);
                    break;
                case Icon.Menu:
                    DrawLine(tex, 6, 9, 26, 9);
                    DrawLine(tex, 6, 16, 26, 16);
                    DrawLine(tex, 6, 23, 26, 23);
                    break;
                case Icon.Story:
                    // Книжная полка / два открытых блока
                    DrawRect(tex, 4, 7, 10, 18);
                    DrawRect(tex, 18, 7, 10, 18);
                    break;
                case Icon.Plus:
                    DrawLine(tex, 16, 6, 16, 26);
                    DrawLine(tex, 6, 16, 26, 16);
                    break;
                case Icon.Lightbulb:
                    DrawArc(tex, 9, 16, 16, 4, 23, 16);
                    DrawLine(tex, 11, 22, 21, 22);
                    DrawLine(tex, 13, 26, 19, 26);
                    DrawLine(tex, 13, 16, 13, 22);
                    DrawLine(tex, 19, 16, 19, 22);
                    break;
            }
        }

        // ────────── Примитивы рисования (anti-aliased через мягкий blend) ──────────

        private static void SetPx(Texture2D tex, int x, int y, float a)
        {
            if (x < 0 || x >= SIZE || y < 0 || y >= SIZE || a <= 0) return;
            int yi = SIZE - 1 - y; // инверт Y
            Color existing = tex.GetPixel(x, yi);
            float na = Mathf.Clamp01(existing.a + a);
            tex.SetPixel(x, yi, new Color(1, 1, 1, na));
        }

        // Алгоритм Wu (anti-aliased line)
        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1)
        {
            float fx0 = x0, fy0 = y0, fx1 = x1, fy1 = y1;
            bool steep = Mathf.Abs(fy1 - fy0) > Mathf.Abs(fx1 - fx0);
            if (steep) { (fx0, fy0) = (fy0, fx0); (fx1, fy1) = (fy1, fx1); }
            if (fx0 > fx1) { (fx0, fx1) = (fx1, fx0); (fy0, fy1) = (fy1, fy0); }

            float dx = fx1 - fx0, dy = fy1 - fy0;
            float gradient = (dx == 0) ? 1f : dy / dx;
            float intery = fy0 + gradient * (Mathf.Round(fx0) - fx0);

            // Сделаем линию толщиной ~2px — рисуем ту же линию ещё раз с оффсетом
            for (int passOffset = 0; passOffset < STROKE; passOffset++)
            {
                float yPass = intery + passOffset - (STROKE - 1) * 0.5f;
                for (int x = (int)fx0; x <= (int)fx1; x++)
                {
                    int iy = (int)yPass;
                    float frac = yPass - iy;
                    if (steep)
                    {
                        SetPx(tex, iy,     x, 1f - frac);
                        SetPx(tex, iy + 1, x, frac);
                    }
                    else
                    {
                        SetPx(tex, x, iy,     1f - frac);
                        SetPx(tex, x, iy + 1, frac);
                    }
                    yPass += gradient;
                }
            }
        }

        private static void DrawRect(Texture2D tex, int x, int y, int w, int h)
        {
            DrawLine(tex, x, y, x + w, y);
            DrawLine(tex, x, y + h, x + w, y + h);
            DrawLine(tex, x, y, x, y + h);
            DrawLine(tex, x + w, y, x + w, y + h);
        }

        private static void DrawCircle(Texture2D tex, int cx, int cy, int r)
        {
            int segments = 64;
            float prevX = cx + r, prevY = cy;
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(a) * r;
                float y = cy + Mathf.Sin(a) * r;
                DrawLine(tex, Mathf.RoundToInt(prevX), Mathf.RoundToInt(prevY), Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                prevX = x; prevY = y;
            }
        }

        // Дугу аппроксимируем тремя точками (не идеально, но узнаваемо для иконок)
        private static void DrawArc(Texture2D tex, int x1, int y1, int xc, int yc, int x2, int y2)
        {
            // Bezier-2 через xc/yc, упрощённо ломаной
            int segments = 24;
            float prevX = x1, prevY = y1;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float u = 1 - t;
                float x = u * u * x1 + 2 * u * t * xc + t * t * x2;
                float y = u * u * y1 + 2 * u * t * yc + t * t * y2;
                DrawLine(tex, Mathf.RoundToInt(prevX), Mathf.RoundToInt(prevY), Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                prevX = x; prevY = y;
            }
        }

        private static void DrawTriangleFilled(Texture2D tex, int x1, int y1, int x2, int y2, int x3, int y3)
        {
            int minX = Mathf.Max(0, Mathf.Min(x1, Mathf.Min(x2, x3)));
            int maxX = Mathf.Min(SIZE - 1, Mathf.Max(x1, Mathf.Max(x2, x3)));
            int minY = Mathf.Max(0, Mathf.Min(y1, Mathf.Min(y2, y3)));
            int maxY = Mathf.Min(SIZE - 1, Mathf.Max(y1, Mathf.Max(y2, y3)));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float w1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / (float)((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
                    float w2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / (float)((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3));
                    float w3 = 1 - w1 - w2;
                    if (w1 >= 0 && w2 >= 0 && w3 >= 0) SetPx(tex, x, y, 1f);
                }
        }
    }

    /// <summary>
    /// Поиск USS-темы по имени файла. Hub нужно знать путь, не привязываясь к
    /// абсолютному (Asset Store-юзеры могут переместить движок в произвольную папку).
    /// </summary>
    public static class NovellaHubResources
    {
        private static string _cachedThemePath;

        public static string GetThemePath()
        {
            if (!string.IsNullOrEmpty(_cachedThemePath) && File.Exists(_cachedThemePath))
                return _cachedThemePath;

            var guids = AssetDatabase.FindAssets("NovellaHubTheme t:StyleSheet");
            if (guids != null && guids.Length > 0)
            {
                _cachedThemePath = AssetDatabase.GUIDToAssetPath(guids[0]);
                return _cachedThemePath;
            }
            return "Assets/NovellaEngine/Editor/Tools/UI/NovellaHubTheme.uss";
        }
    }
}
