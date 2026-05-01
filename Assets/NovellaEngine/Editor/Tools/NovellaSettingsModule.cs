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
        private const string PREF_INTERFACE = "Novella_InterfaceColor";
        private const string PREF_TEXT = "Novella_TextColor";
        private const string PREF_GUIDE_MODE = "Novella_SettingsGuideMode";

        // Событие — стреляет когда юзер меняет внешний вид. Hub/UI Forge/etc.
        // подписываются и применяют новые значения.
        public static event System.Action OnAppearanceChanged;

        private static Color? _cachedAccent;
        private static Color? _cachedInterface;
        private static Color? _cachedText;
        private static bool _showGuide;
        private static bool _guideLoaded;

        // Дефолты — соответствуют исходному тёмному стилю инструмента.
        public static readonly Color C_INTERFACE_DEFAULT = new Color(0.075f, 0.078f, 0.106f);
        public static readonly Color C_TEXT_DEFAULT      = new Color(0.93f,  0.93f,  0.96f);

        // ─── Цвета (все динамические — из настроек) ─────────────────────────────
        private static Color C_BG_PRIMARY => GetInterfaceColor();
        private static Color C_BG_SIDE    => GetBgSideColor();
        private static Color C_BG_RAISED  => GetBgRaisedColor();
        private static Color C_BORDER     => GetBorderColor();
        private static Color C_TEXT_1     => GetTextColor();
        private static Color C_TEXT_2     => GetTextSecondary();
        private static Color C_TEXT_3     => GetTextMuted();
        private static Color C_TEXT_4     => GetTextDisabled();
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

        // Цвет основного фона панелей. Дефолт — тёмно-сине-серый.
        public static Color GetInterfaceColor()
        {
            if (_cachedInterface.HasValue) return _cachedInterface.Value;
            string s = EditorPrefs.GetString(PREF_INTERFACE, "");
            if (!string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString("#" + s, out var c))
                _cachedInterface = c;
            else
                _cachedInterface = C_INTERFACE_DEFAULT;
            return _cachedInterface.Value;
        }

        public static void SetInterfaceColor(Color c)
        {
            _cachedInterface = c;
            EditorPrefs.SetString(PREF_INTERFACE, ColorUtility.ToHtmlStringRGB(c));
            OnAppearanceChanged?.Invoke();
        }

        // Цвет основного текста (заголовки и т.п.). Все производные оттенки
        // (вторичный, серый, выключенный) получаются миксом с фоном.
        public static Color GetTextColor()
        {
            if (_cachedText.HasValue) return _cachedText.Value;
            string s = EditorPrefs.GetString(PREF_TEXT, "");
            if (!string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString("#" + s, out var c))
                _cachedText = c;
            else
                _cachedText = C_TEXT_DEFAULT;
            return _cachedText.Value;
        }

        public static void SetTextColor(Color c)
        {
            _cachedText = c;
            EditorPrefs.SetString(PREF_TEXT, ColorUtility.ToHtmlStringRGB(c));
            OnAppearanceChanged?.Invoke();
        }

        // ─── Производные оттенки ───────────────────────────────────────────────
        //
        // Идея: чтобы пользователь не выбирал 7-8 цветов вручную, основные —
        // только Interface и Text. Остальные оттенки (sidebar, raised, border,
        // вторичный текст) автоматически рассчитываются:
        //   • для тёмной темы (V<0.5) — поднимаем V (становится светлее)
        //   • для светлой темы (V>=0.5) — опускаем V (становится темнее)
        // Так логика "поверхность + 1 уровень" работает для любого цвета.

        private static Color Shade(Color c, float amount)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            v = (v < 0.5f) ? Mathf.Min(1f, v + amount) : Mathf.Max(0f, v - amount);
            var r = Color.HSVToRGB(h, s, v);
            r.a = c.a;
            return r;
        }

        public static Color GetBgSideColor()   => Shade(GetInterfaceColor(), 0.04f);
        public static Color GetBgRaisedColor() => Shade(GetInterfaceColor(), 0.07f);
        public static Color GetBorderColor()   => Shade(GetInterfaceColor(), 0.13f);

        // Вторичные градации текста — линейный микс между основным текстом и фоном.
        public static Color GetTextSecondary() => Color.Lerp(GetTextColor(), GetInterfaceColor(), 0.18f);
        public static Color GetTextMuted()     => Color.Lerp(GetTextColor(), GetInterfaceColor(), 0.40f);
        public static Color GetTextDisabled()  => Color.Lerp(GetTextColor(), GetInterfaceColor(), 0.62f);

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
            EditorPrefs.DeleteKey(PREF_INTERFACE);
            EditorPrefs.DeleteKey(PREF_TEXT);
            _cachedAccent = null;
            _cachedInterface = null;
            _cachedText = null;
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
                    "Two colors:\n• Accent — used for hints, highlights and primary buttons.\n• Interface — the main dark background of all panels and inspectors.",
                    "Два цвета:\n• Акцентный — используется для подсветок, подсказок и основных кнопок.\n• Интерфейса — основной тёмный фон всех панелей и инспекторов."));

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

                // Interface color (dark panel bg)
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Interface color", "Цвет интерфейса"), lbl, GUILayout.Width(180));

                Color ifc = GetInterfaceColor();
                EditorGUI.BeginChangeCheck();
                Color newIfc = EditorGUILayout.ColorField(GUIContent.none, ifc, true, false, false, GUILayout.Width(80), GUILayout.Height(22));
                if (EditorGUI.EndChangeCheck() && newIfc != ifc)
                {
                    SetInterfaceColor(newIfc);
                    _window?.Repaint();
                }

                GUILayout.Space(10);
                if (GUILayout.Button(ToolLang.Get("Reset", "Сброс"), GUILayout.Width(80), GUILayout.Height(22)))
                {
                    SetInterfaceColor(C_INTERFACE_DEFAULT);
                    _window?.Repaint();
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(14);

                // Text color
                GUILayout.BeginHorizontal();
                GUILayout.Label(ToolLang.Get("Text color", "Цвет текста"), lbl, GUILayout.Width(180));

                Color txt = GetTextColor();
                EditorGUI.BeginChangeCheck();
                Color newTxt = EditorGUILayout.ColorField(GUIContent.none, txt, true, false, false, GUILayout.Width(80), GUILayout.Height(22));
                if (EditorGUI.EndChangeCheck() && newTxt != txt)
                {
                    SetTextColor(newTxt);
                    _window?.Repaint();
                }

                GUILayout.Space(10);
                if (GUILayout.Button(ToolLang.Get("Reset", "Сброс"), GUILayout.Width(80), GUILayout.Height(22)))
                {
                    SetTextColor(C_TEXT_DEFAULT);
                    _window?.Repaint();
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
                hint.normal.textColor = C_TEXT_4;
                GUILayout.Label(ToolLang.Get(
                    "Affects headings and labels. Secondary/muted/disabled tones are derived automatically by mixing with the interface color.",
                    "Влияет на заголовки и подписи. Вторичный/серый/выключенный текст рассчитываются автоматически — миксом с цветом интерфейса."), hint);

                GUILayout.Space(10);

                // Live preview palette
                Rect pal = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(pal, GetInterfaceColor());
                DrawRectBorder(pal, GetBorderColor());
                float colW = pal.width / 4f;
                DrawSwatch(new Rect(pal.x,            pal.y, colW, pal.height), GetInterfaceColor(),    GetTextColor(),     "Aa  H1");
                DrawSwatch(new Rect(pal.x + colW,     pal.y, colW, pal.height), GetBgSideColor(),       GetTextSecondary(), "Aa  H2");
                DrawSwatch(new Rect(pal.x + colW * 2, pal.y, colW, pal.height), GetBgRaisedColor(),     GetTextMuted(),     "Aa  hint");
                DrawSwatch(new Rect(pal.x + colW * 3, pal.y, colW, pal.height), GetBgRaisedColor(),     GetTextDisabled(),  "Aa  off");
            });
        }

        private static void DrawSwatch(Rect r, Color bg, Color textColor, string label)
        {
            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(new Rect(r.x, r.y, 1, r.height), GetBorderColor());
            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = textColor;
            GUI.Label(r, label, st);
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
