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
        private const string PREF_GUIDE_MODE = "Novella_SettingsGuideMode";

        // Событие — стреляет когда юзер меняет внешний вид. Hub/UI Forge/etc.
        // подписываются и применяют новые значения.
        public static event System.Action OnAppearanceChanged;

        // Кэш значений — чтобы не дёргать EditorPrefs на каждом фрейме при Repaint.
        // Без кэша ColorField лагал, потому что каждое движение мыши делало
        // EditorPrefs.GetString + парсинг hex.
        private static Color? _cachedAccent;
        private static Texture2D _cachedBg;
        private static string _cachedBgPath;
        private static float _cachedBgOpacity = -1f;
        private static bool _showGuide;
        private static bool _guideLoaded;

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
            if (_cachedAccent.HasValue) return _cachedAccent.Value;
            string s = EditorPrefs.GetString(PREF_ACCENT, "");
            if (!string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString("#" + s, out var c))
                _cachedAccent = c;
            else
                _cachedAccent = C_ACCENT_DEFAULT;
            return _cachedAccent.Value;
        }

        public static void SetAccentColor(Color c)
        {
            _cachedAccent = c;
            EditorPrefs.SetString(PREF_ACCENT, ColorUtility.ToHtmlStringRGB(c));
            OnAppearanceChanged?.Invoke();
        }

        public static Texture2D GetBackgroundImage()
        {
            string p = EditorPrefs.GetString(PREF_BG_IMAGE, "");
            if (p == _cachedBgPath && _cachedBg != null) return _cachedBg;
            _cachedBgPath = p;
            if (string.IsNullOrEmpty(p)) { _cachedBg = null; return null; }
            _cachedBg = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            return _cachedBg;
        }

        public static void SetBackgroundImage(string path)
        {
            if (string.IsNullOrEmpty(path)) EditorPrefs.DeleteKey(PREF_BG_IMAGE);
            else EditorPrefs.SetString(PREF_BG_IMAGE, path);
            _cachedBg = null;
            _cachedBgPath = null;
            OnAppearanceChanged?.Invoke();
        }

        public static float GetBackgroundOpacity()
        {
            if (_cachedBgOpacity < 0) _cachedBgOpacity = Mathf.Clamp01(EditorPrefs.GetFloat(PREF_BG_OPACITY, 0.15f));
            return _cachedBgOpacity;
        }

        public static void SetBackgroundOpacity(float v)
        {
            _cachedBgOpacity = Mathf.Clamp01(v);
            EditorPrefs.SetFloat(PREF_BG_OPACITY, _cachedBgOpacity);
            OnAppearanceChanged?.Invoke();
        }

        public static bool ShowGuide
        {
            get
            {
                if (!_guideLoaded) { _showGuide = EditorPrefs.GetBool(PREF_GUIDE_MODE, true); _guideLoaded = true; }
                return _showGuide;
            }
            set
            {
                _showGuide = value;
                _guideLoaded = true;
                EditorPrefs.SetBool(PREF_GUIDE_MODE, value);
            }
        }

        public static void ResetAppearance()
        {
            EditorPrefs.DeleteKey(PREF_ACCENT);
            EditorPrefs.DeleteKey(PREF_BG_IMAGE);
            EditorPrefs.DeleteKey(PREF_BG_OPACITY);
            _cachedAccent = null;
            _cachedBg = null;
            _cachedBgPath = null;
            _cachedBgOpacity = -1f;
            OnAppearanceChanged?.Invoke();
        }

        // ─── Главный draw ───────────────────────────────────────────────────────

        public void DrawGUI(Rect position)
        {
            EditorGUI.DrawRect(position, C_BG_PRIMARY);
            // Фоновую картинку рисует Hub под всем — здесь повторно НЕ рисуем,
            // чтобы не было двойного слоя.

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

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("⚙  " + ToolLang.Get("Settings", "Настройки"), titleSt);
            GUILayout.FlexibleSpace();

            // Toggle подсказок — единая кнопка, как во всех модулях.
            bool guide = ShowGuide;
            GUI.backgroundColor = guide ? GetAccentColor() : Color.white;
            string lbl = (guide ? "💡  " : "💡  ") + ToolLang.Get(guide ? "Hints: ON" : "Hints: OFF",
                                                                  guide ? "Подсказки: вкл" : "Подсказки: выкл");
            if (GUILayout.Button(lbl, GUILayout.Width(170), GUILayout.Height(28)))
            {
                ShowGuide = !guide;
                _window?.Repaint();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            var subSt = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Customize the toolkit appearance and configure project localization.",
                "Настрой внешний вид инструмента и локализацию проекта."), subSt);
        }

        // Подсказка: серая плашка под заголовком секции (если ShowGuide включён).
        private static void DrawGuideTip(string text)
        {
            if (!ShowGuide) return;
            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };
            st.normal.textColor = new Color(0.85f, 0.87f, 0.93f);
            Rect r = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), st);
            Color acc = GetAccentColor();
            EditorGUI.DrawRect(r, new Color(acc.r, acc.g, acc.b, 0.10f));
            DrawRectBorder(r, new Color(acc.r, acc.g, acc.b, 0.4f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), acc);
            GUI.Label(r, "💡 " + text, st);
            GUILayout.Space(8);
        }

        // ─── Section: Appearance ────────────────────────────────────────────────

        private void DrawAppearanceSection()
        {
            DrawSectionCard("🎨 " + ToolLang.Get("Appearance", "Внешний вид"), () =>
            {
                DrawGuideTip(ToolLang.Get(
                    "These settings affect the look of the WHOLE toolkit — accent color is used for selections, buttons and highlights across modules; the background image shows behind everything.",
                    "Эти настройки влияют на ВЕСЬ инструмент: акцентный цвет используется для выделений, кнопок и подсветок во всех модулях; фоновая картинка отображается за всем содержимым."));

                var lbl = new GUIStyle(EditorStyles.label) { fontSize = 12 };
                lbl.normal.textColor = C_TEXT_2;

                // Accent color
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Accent color", "Акцентный цвет"), lbl, GUILayout.Width(180));

                Color acc = GetAccentColor();
                EditorGUI.BeginChangeCheck();
                Color newAcc = EditorGUILayout.ColorField(GUIContent.none, acc, true, false, false, GUILayout.Width(80), GUILayout.Height(22));
                if (EditorGUI.EndChangeCheck() && newAcc != acc)
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

                GUILayout.Space(14);

                // Background image
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Background image", "Фоновая картинка"), lbl, GUILayout.Width(180));

                var bg = GetBackgroundImage();
                string bgName = bg != null ? bg.name : ToolLang.Get("(none — pick from gallery)", "(нет — выбрать из галереи)");
                if (GUILayout.Button(bgName, EditorStyles.popup, GUILayout.Height(22)))
                {
                    NovellaGalleryWindow.ShowWindow((obj) =>
                    {
                        string path = null;
                        if (obj is Texture2D tex) path = AssetDatabase.GetAssetPath(tex);
                        else if (obj is Sprite sp) path = AssetDatabase.GetAssetPath(sp);
                        if (!string.IsNullOrEmpty(path))
                        {
                            SetBackgroundImage(path);
                            _window?.Repaint();
                        }
                    }, NovellaGalleryWindow.EGalleryFilter.Image, "");
                }
                if (bg != null && GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(22)))
                {
                    SetBackgroundImage(null);
                    _window?.Repaint();
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Background opacity", "Прозрачность фона"), lbl, GUILayout.Width(180));
                EditorGUI.BeginChangeCheck();
                float op = GUILayout.HorizontalSlider(GetBackgroundOpacity(), 0f, 1f, GUILayout.Width(180));
                if (EditorGUI.EndChangeCheck()) SetBackgroundOpacity(op);
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

        private NovellaUILocalizationTable _activeTable;

        private void DrawLanguageSection()
        {
            DrawSectionCard("🌐 " + ToolLang.Get("Language", "Язык"), () =>
            {
                DrawGuideTip(ToolLang.Get(
                    "Two separate things here: 'Toolkit language' = the editor itself in RU/EN. 'Project UI localization' = translations for YOUR game's UI (button labels, screens, etc.) which players see at runtime.",
                    "Здесь две разные вещи: «Язык инструмента» — сам редактор на RU/EN. «Локализация интерфейса проекта» — переводы UI ТВОЕЙ игры (надписи кнопок, экраны), которые увидит игрок в рантайме."));

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

                GUILayout.Space(14);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
                GUILayout.Space(14);

                var subTtl = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                subTtl.normal.textColor = C_TEXT_1;
                GUILayout.Label(ToolLang.Get("Project UI localization", "Локализация интерфейса проекта"), subTtl);
                GUILayout.Space(8);

                if (_activeTable == null) _activeTable = NovellaUILocalizationEditor.GetOrCreateTable();
                var settings = NovellaLocalizationSettings.GetOrCreateSettings();

                // Кастомный picker таблицы (через GenericMenu, без unity-проводника)
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Table", "Таблица"), lbl, GUILayout.Width(180));
                string tName = _activeTable != null ? _activeTable.name : ToolLang.Get("(none)", "(нет)");
                if (GUILayout.Button(tName, EditorStyles.popup, GUILayout.Height(22)))
                {
                    ShowTablePickerMenu();
                }
                if (_activeTable != null)
                {
                    if (GUILayout.Button("👁", GUILayout.Width(28), GUILayout.Height(22)))
                    {
                        EditorGUIUtility.PingObject(_activeTable);
                    }
                }
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
                        if (_activeTable.ImportJson(txt))
                        {
                            EditorUtility.SetDirty(_activeTable);
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
                        File.WriteAllText(p, _activeTable.ExportJson());
                        EditorUtility.RevealInFinder(p);
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            });
        }

        private void ShowTablePickerMenu()
        {
            var menu = new GenericMenu();
            var guids = AssetDatabase.FindAssets("t:NovellaUILocalizationTable");
            if (guids.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent(ToolLang.Get("No tables found", "Таблицы не найдены")));
            }
            else
            {
                foreach (var g in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var t = AssetDatabase.LoadAssetAtPath<NovellaUILocalizationTable>(p);
                    if (t == null) continue;
                    bool isActive = _activeTable == t;
                    menu.AddItem(new GUIContent($"{t.name}    ({p.Replace("Assets/", "")})"), isActive, () =>
                    {
                        _activeTable = t;
                        NovellaLocalizationManager.RegisterTable(t);
                    });
                }
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("➕ " + ToolLang.Get("Create new table…", "Создать новую таблицу…")), false, () =>
            {
                string p = EditorUtility.SaveFilePanelInProject(
                    ToolLang.Get("Create UI Localization Table", "Создать UI Localization Table"),
                    "UILocalizationTable", "asset",
                    ToolLang.Get("Choose where to save the new table", "Выбери куда сохранить новую таблицу"),
                    "Assets/NovellaEngine/Runtime/Resources");
                if (!string.IsNullOrEmpty(p))
                {
                    var nt = ScriptableObject.CreateInstance<NovellaUILocalizationTable>();
                    AssetDatabase.CreateAsset(nt, p);
                    AssetDatabase.SaveAssets();
                    _activeTable = nt;
                    NovellaLocalizationManager.RegisterTable(nt);
                }
            });
            menu.ShowAsContext();
        }

        // ─── Section: Misc ──────────────────────────────────────────────────────

        private void DrawMiscSection()
        {
            DrawSectionCard("🛠 " + ToolLang.Get("Other", "Прочее"), () =>
            {
                DrawGuideTip(ToolLang.Get(
                    "Reset clears your accent color, background image and opacity. Settings are stored in EditorPrefs (per-machine), so this won't affect anyone else on the team.",
                    "Сброс удалит акцентный цвет, фоновую картинку и прозрачность. Настройки хранятся в EditorPrefs (на твоей машине), на других участников команды это не повлияет."));

                GUI.backgroundColor = C_DANGER;
                if (GUILayout.Button(ToolLang.Get("Reset all appearance settings", "Сбросить все настройки внешнего вида"), GUILayout.Height(28), GUILayout.MaxWidth(360)))
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Reset", "Сбросить"),
                        ToolLang.Get("Reset all appearance settings to defaults?", "Сбросить все настройки внешнего вида к значениям по умолчанию?"),
                        ToolLang.Get("Reset", "Сбросить"),
                        ToolLang.Get("Cancel", "Отмена")))
                    {
                        ResetAppearance();
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
