// ════════════════════════════════════════════════════════════════════════════
// NovellaSettingsModule — модуль "Настройки" в Hub.
//
// Что внутри:
//   1. Внешний вид — кастомизация интерфейса инструмента (акцентный цвет,
//      фоновая картинка, скругления).
//   2. Язык — переключатель языка интерфейса инструмента (RU/EN), а также
//      настройки UI-локализации проекта (выбор языков, открыть редактор переводов).
//   3. Прочее — служебные настройки, сброс к дефолтам и т.п.
//
// Все значения хранятся в EditorPrefs, чтобы не засорять проект.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaSettingsModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Settings", "Настройки");
        public string ModuleIcon => "⚙";

        private EditorWindow _window;
        private Vector2 _scroll;

        // EditorPrefs keys
        private const string PREF_ACCENT = "Novella_AccentColor";
        private const string PREF_BG_IMAGE = "Novella_BackgroundImagePath";
        private const string PREF_BG_OPACITY = "Novella_BackgroundOpacity";

        // ─── Цвета ──────────────────────────────────────────────────────────────
        private static readonly Color C_BG_PRIMARY = new Color(0.075f, 0.078f, 0.106f);
        private static readonly Color C_BG_SIDE    = new Color(0.102f, 0.106f, 0.149f);
        private static readonly Color C_BG_RAISED  = new Color(0.13f,  0.14f,  0.18f);
        private static readonly Color C_BORDER     = new Color(0.165f, 0.176f, 0.243f);
        private static readonly Color C_TEXT_1     = new Color(0.93f,  0.93f,  0.96f);
        private static readonly Color C_TEXT_2     = new Color(0.78f,  0.80f,  0.86f);
        private static readonly Color C_TEXT_3     = new Color(0.62f,  0.63f,  0.69f);
        private static readonly Color C_TEXT_4     = new Color(0.42f,  0.43f,  0.49f);
        private static readonly Color C_ACCENT_DEFAULT = new Color(0.36f, 0.75f, 0.92f);
        private static readonly Color C_DANGER     = new Color(0.85f,  0.32f,  0.32f);

        // ─── Lifecycle ──────────────────────────────────────────────────────────

        public void OnEnable(EditorWindow w) { _window = w; }
        public void OnDisable() { }

        // ─── Public API: текущие значения для других модулей ───────────────────

        public static Color GetAccentColor()
        {
            string s = EditorPrefs.GetString(PREF_ACCENT, "");
            if (!string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString("#" + s, out var c)) return c;
            return C_ACCENT_DEFAULT;
        }

        public static void SetAccentColor(Color c)
        {
            EditorPrefs.SetString(PREF_ACCENT, ColorUtility.ToHtmlStringRGB(c));
        }

        public static Texture2D GetBackgroundImage()
        {
            string p = EditorPrefs.GetString(PREF_BG_IMAGE, "");
            if (string.IsNullOrEmpty(p)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        public static float GetBackgroundOpacity()
        {
            return Mathf.Clamp01(EditorPrefs.GetFloat(PREF_BG_OPACITY, 0.15f));
        }

        // ─── Главный draw ───────────────────────────────────────────────────────

        public void DrawGUI(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG_PRIMARY);
            DrawBackgroundOverlay(position);

            GUILayout.BeginArea(position);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Space(20);
            GUILayout.BeginHorizontal();
            GUILayout.Space(40);
            GUILayout.BeginVertical(GUILayout.MaxWidth(720));

            DrawHeader();
            GUILayout.Space(16);

            DrawAppearanceSection();
            GUILayout.Space(20);

            DrawLanguageSection();
            GUILayout.Space(20);

            DrawMiscSection();
            GUILayout.Space(40);

            GUILayout.EndVertical();
            GUILayout.Space(40);
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBackgroundOverlay(Rect rect)
        {
            var bg = GetBackgroundImage();
            if (bg == null) return;
            float o = GetBackgroundOpacity();
            Color prev = GUI.color;
            GUI.color = new Color(1, 1, 1, o);
            GUI.DrawTexture(rect, bg, ScaleMode.ScaleAndCrop);
            GUI.color = prev;
        }

        private void DrawHeader()
        {
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("⚙  " + ToolLang.Get("Settings", "Настройки"), titleSt);

            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Customize the toolkit appearance and configure project localization.",
                "Настрой внешний вид инструмента и локализацию проекта."), subSt);
        }

        // ─── Section: Appearance ────────────────────────────────────────────────

        private void DrawAppearanceSection()
        {
            DrawSectionCard("🎨 " + ToolLang.Get("Appearance", "Внешний вид"), () =>
            {
                // Accent color
                GUILayout.BeginHorizontal();
                var lbl = new GUIStyle(EditorStyles.label) { fontSize = 12 };
                lbl.normal.textColor = C_TEXT_2;
                GUILayout.Label(ToolLang.Get("Accent color", "Акцентный цвет"), lbl, GUILayout.Width(180));

                Color acc = GetAccentColor();
                EditorGUI.BeginChangeCheck();
                Color newAcc = EditorGUILayout.ColorField(GUIContent.none, acc, true, false, false, GUILayout.Width(80), GUILayout.Height(22));
                if (EditorGUI.EndChangeCheck())
                {
                    SetAccentColor(newAcc);
                    _window?.Repaint();
                }

                GUILayout.Space(10);
                if (GUILayout.Button(ToolLang.Get("Reset", "Сброс"), GUILayout.Width(80), GUILayout.Height(22)))
                {
                    SetAccentColor(C_ACCENT_DEFAULT);
                    _window?.Repaint();
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                hint.normal.textColor = C_TEXT_4;
                GUILayout.Label(ToolLang.Get(
                    "Used for highlights, selections and primary buttons across all modules.",
                    "Используется для подсветок, выделений и основных кнопок во всех модулях."), hint);

                GUILayout.Space(14);

                // Background image
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Background image", "Фоновая картинка"), lbl, GUILayout.Width(180));

                var bg = GetBackgroundImage();
                string bgName = bg != null ? bg.name : ToolLang.Get("(none)", "(нет)");
                if (GUILayout.Button(bgName, EditorStyles.popup, GUILayout.Height(22)))
                {
                    NovellaGalleryWindow.ShowWindow((obj) =>
                    {
                        if (obj is Texture2D tex)
                        {
                            string path = AssetDatabase.GetAssetPath(tex);
                            EditorPrefs.SetString(PREF_BG_IMAGE, path);
                            _window?.Repaint();
                        }
                        else if (obj is Sprite sp)
                        {
                            string path = AssetDatabase.GetAssetPath(sp);
                            EditorPrefs.SetString(PREF_BG_IMAGE, path);
                            _window?.Repaint();
                        }
                    }, NovellaGalleryWindow.EGalleryFilter.Image, "");
                }
                if (bg != null && GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(22)))
                {
                    EditorPrefs.DeleteKey(PREF_BG_IMAGE);
                    _window?.Repaint();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Background opacity", "Прозрачность фона"), lbl, GUILayout.Width(180));
                EditorGUI.BeginChangeCheck();
                float op = GUILayout.HorizontalSlider(GetBackgroundOpacity(), 0f, 1f, GUILayout.Width(180));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetFloat(PREF_BG_OPACITY, op);
                    _window?.Repaint();
                }
                GUILayout.Space(10);
                GUILayout.Label((GetBackgroundOpacity() * 100f).ToString("F0") + "%", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = C_TEXT_3 } }, GUILayout.Width(40));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (bg != null)
                {
                    GUILayout.Space(8);
                    Rect prevRect = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(prevRect, C_BG_RAISED);
                    DrawRectBorder(prevRect, C_BORDER);
                    GUI.DrawTexture(prevRect, bg, ScaleMode.ScaleAndCrop);
                }
            });
        }

        // ─── Section: Language ──────────────────────────────────────────────────

        private void DrawLanguageSection()
        {
            DrawSectionCard("🌐 " + ToolLang.Get("Language", "Язык"), () =>
            {
                var lbl = new GUIStyle(EditorStyles.label) { fontSize = 12 };
                lbl.normal.textColor = C_TEXT_2;

                // Tool language (RU/EN of the toolkit itself)
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Toolkit language", "Язык инструмента"), lbl, GUILayout.Width(180));
                bool isRU = ToolLang.IsRU;

                GUI.backgroundColor = !isRU ? GetAccentColor() : Color.white;
                if (GUILayout.Button("EN  English", EditorStyles.miniButtonLeft, GUILayout.Width(110), GUILayout.Height(22)))
                {
                    if (isRU) ToolLang.Toggle();
                    _window?.Repaint();
                }
                GUI.backgroundColor = isRU ? GetAccentColor() : Color.white;
                if (GUILayout.Button("RU  Русский", EditorStyles.miniButtonRight, GUILayout.Width(110), GUILayout.Height(22)))
                {
                    if (!isRU) ToolLang.Toggle();
                    _window?.Repaint();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                hint.normal.textColor = C_TEXT_4;
                GUILayout.Label(ToolLang.Get(
                    "Language of the editor UI itself (menus, labels, hints).",
                    "Язык интерфейса самого инструмента (меню, надписи, подсказки)."), hint);

                GUILayout.Space(14);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
                GUILayout.Space(14);

                // Project localization table
                var subTtl = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                subTtl.normal.textColor = C_TEXT_1;
                GUILayout.Label(ToolLang.Get("Project UI localization", "Локализация интерфейса проекта"), subTtl);
                GUILayout.Space(4);

                GUILayout.Label(ToolLang.Get(
                    "Stores translations for UI elements (button labels, panel titles…) keyed by short ID. Edit in the dedicated window.",
                    "Хранит переводы UI-элементов (надписи кнопок, заголовки панелей…) по коротким ID-ключам. Редактируется в отдельном окне."), hint);

                GUILayout.Space(8);

                var table = NovellaUILocalizationEditor.GetOrCreateTable();
                var settings = NovellaLocalizationSettings.GetOrCreateSettings();

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Table", "Таблица"), lbl, GUILayout.Width(180));
                EditorGUILayout.ObjectField(table, typeof(NovellaUILocalizationTable), false, GUILayout.Height(20));
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Available languages", "Доступные языки"), lbl, GUILayout.Width(180));
                GUILayout.Label(string.Join(", ", settings.Languages), new GUIStyle(EditorStyles.label) { fontSize = 11, normal = { textColor = C_TEXT_2 } });
                GUILayout.EndHorizontal();

                GUILayout.Space(10);

                GUILayout.BeginHorizontal();
                GUILayout.Space(180);
                GUI.backgroundColor = GetAccentColor();
                if (GUILayout.Button("📝 " + ToolLang.Get("Open Translation Editor", "Открыть редактор переводов"), GUILayout.Height(28), GUILayout.MaxWidth(280)))
                {
                    NovellaUILocalizationEditor.ShowWindow();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.Space(180);
                if (GUILayout.Button("📥 " + ToolLang.Get("Import JSON…", "Импорт из JSON…"), EditorStyles.miniButtonLeft, GUILayout.Width(140), GUILayout.Height(22)))
                {
                    string p = EditorUtility.OpenFilePanel(ToolLang.Get("Import JSON", "Импорт JSON"), "", "json");
                    if (!string.IsNullOrEmpty(p))
                    {
                        string txt = File.ReadAllText(p);
                        if (table.ImportJson(txt))
                        {
                            EditorUtility.SetDirty(table);
                            AssetDatabase.SaveAssets();
                            EditorUtility.DisplayDialog(ToolLang.Get("Done", "Готово"), ToolLang.Get("Localization imported.", "Локализация импортирована."), "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(ToolLang.Get("Failed", "Ошибка"), ToolLang.Get("JSON import failed. Check the format.", "Импорт не удался — проверь формат."), "OK");
                        }
                    }
                }
                if (GUILayout.Button("📤 " + ToolLang.Get("Export JSON…", "Экспорт в JSON…"), EditorStyles.miniButtonRight, GUILayout.Width(140), GUILayout.Height(22)))
                {
                    string p = EditorUtility.SaveFilePanel(ToolLang.Get("Export JSON", "Экспорт JSON"), "", "novella_ui_loc", "json");
                    if (!string.IsNullOrEmpty(p))
                    {
                        File.WriteAllText(p, table.ExportJson());
                        EditorUtility.RevealInFinder(p);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            });
        }

        // ─── Section: Misc ──────────────────────────────────────────────────────

        private void DrawMiscSection()
        {
            DrawSectionCard("🛠 " + ToolLang.Get("Other", "Прочее"), () =>
            {
                var lbl = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, wordWrap = true };
                lbl.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get(
                    "Reset toolkit appearance preferences to defaults.",
                    "Вернуть внешний вид инструмента к значениям по умолчанию."), lbl);

                GUILayout.Space(8);
                GUI.backgroundColor = C_DANGER;
                if (GUILayout.Button(ToolLang.Get("Reset all appearance settings", "Сбросить все настройки внешнего вида"), GUILayout.Height(28), GUILayout.MaxWidth(360)))
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Reset", "Сбросить"),
                        ToolLang.Get("Reset all appearance settings to defaults?", "Сбросить все настройки внешнего вида к значениям по умолчанию?"),
                        ToolLang.Get("Reset", "Сбросить"),
                        ToolLang.Get("Cancel", "Отмена")))
                    {
                        EditorPrefs.DeleteKey(PREF_ACCENT);
                        EditorPrefs.DeleteKey(PREF_BG_IMAGE);
                        EditorPrefs.DeleteKey(PREF_BG_OPACITY);
                        _window?.Repaint();
                    }
                }
                GUI.backgroundColor = Color.white;
            });
        }

        // ─── Утилиты рисования ──────────────────────────────────────────────────

        private void DrawSectionCard(string title, System.Action drawContent)
        {
            // Карточка с заголовком и контентом.
            GUILayout.BeginVertical();
            Rect cardStart = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.BeginVertical();

            var ttlSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            ttlSt.normal.textColor = C_TEXT_1;
            GUILayout.Label(title, ttlSt);
            GUILayout.Space(6);

            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);

            drawContent?.Invoke();

            GUILayout.Space(12);
            GUILayout.EndVertical();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            Rect cardEnd = GUILayoutUtility.GetLastRect();
            Rect cardRect = new Rect(cardStart.x, cardStart.y + 4, cardStart.width, cardEnd.yMax - cardStart.y);

            // Подложка карточки рисуется ПОД контентом, поэтому используем low-level rect compute.
            // (Контент уже отрисован сверху — фон будет под ним при следующем Repaint)
            // Чтобы фон рисовался ПОД контентом, нам нужен альтернативный подход — но в IMGUI
            // это делается через GUILayout.Box с GUI.skin.box. Здесь используем простой кадр.
            DrawRectBorder(cardRect, C_BORDER);
            GUILayout.EndVertical();
        }

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
