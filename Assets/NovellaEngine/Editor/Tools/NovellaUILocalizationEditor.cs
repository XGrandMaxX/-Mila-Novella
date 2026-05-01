// ════════════════════════════════════════════════════════════════════════════
// NovellaUILocalizationEditor — окно редактирования таблицы UI-локализации.
//
// Левая колонка — список ключей (с поиском, добавлением, удалением).
// Правая — редактирование выбранного ключа: на каждом языке поле для перевода.
// Сверху — управление набором языков (взято из NovellaLocalizationSettings).
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaUILocalizationEditor : EditorWindow
    {
        private const string TABLE_FOLDER = "Assets/NovellaEngine/Runtime/Resources";
        private const string TABLE_PATH = TABLE_FOLDER + "/UILocalizationTable.asset";

        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        // Из Settings (см. NovellaSettingsModule).
        private static Color C_BG_SIDE   => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER    => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1    => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2    => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3    => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4    => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT => NovellaSettingsModule.GetAccentColor();
        private static readonly Color C_DANGER     = new Color(0.85f,  0.32f,  0.32f);
        private static readonly Color C_WARN       = new Color(1.00f,  0.78f,  0.20f);

        private NovellaUILocalizationTable _table;
        private NovellaLocalizationSettings _settings;
        private string _selectedKey;
        private string _newKey = "";
        private string _filter = "";
        private Vector2 _keysScroll;
        private Vector2 _editScroll;
        private bool _addLangMode = false;
        private string _newLang = "";

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaUILocalizationEditor>(false,
                ToolLang.Get("UI Localization", "Локализация UI"), true);
            win.minSize = new Vector2(820, 480);
            // Заменяем стандартный house-icon Unity-окна на наш «🌐» (через emoji-text title).
            win.titleContent = new GUIContent("  " + ToolLang.Get("UI Localization", "Локализация UI"),
                EditorGUIUtility.IconContent("d_BuildSettings.Web.Small").image);
            win.Show();
        }

        public static NovellaUILocalizationTable GetOrCreateTable()
        {
            var guids = AssetDatabase.FindAssets("t:NovellaUILocalizationTable");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<NovellaUILocalizationTable>(AssetDatabase.GUIDToAssetPath(guids[0]));

            // Resources-папка — чтобы менеджер мог подгружать таблицу в рантайме.
            if (!AssetDatabase.IsValidFolder(TABLE_FOLDER))
            {
                Directory.CreateDirectory(Application.dataPath.Replace("Assets", "") + TABLE_FOLDER);
                AssetDatabase.Refresh();
            }
            var t = ScriptableObject.CreateInstance<NovellaUILocalizationTable>();
            AssetDatabase.CreateAsset(t, TABLE_PATH);
            AssetDatabase.SaveAssets();
            return t;
        }

        private void OnEnable()
        {
            _table = GetOrCreateTable();
            _settings = NovellaLocalizationSettings.GetOrCreateSettings();
            if (_table.Entries.Count > 0) _selectedKey = _table.Entries[0].Key;
        }

        private void OnGUI()
        {
            if (_table == null) _table = GetOrCreateTable();
            if (_settings == null) _settings = NovellaLocalizationSettings.GetOrCreateSettings();

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            const float topbarH = 44f;
            Rect topbar = new Rect(0, 0, position.width, topbarH);
            EditorGUI.DrawRect(topbar, C_BG_SIDE);
            DrawRectBorder(new Rect(0, topbar.yMax - 1, topbar.width, 1), C_BORDER);
            DrawTopbar(topbar);

            const float leftW = 280f;
            Rect leftRect = new Rect(0, topbarH, leftW, position.height - topbarH);
            EditorGUI.DrawRect(leftRect, C_BG_SIDE);
            DrawRectBorder(new Rect(leftRect.xMax - 1, leftRect.y, 1, leftRect.height), C_BORDER);
            DrawKeysList(leftRect);

            Rect rightRect = new Rect(leftW, topbarH, position.width - leftW, position.height - topbarH);
            DrawKeyEditor(rightRect);
        }

        // ─── Topbar ─────────────────────────────────────────────────────────────

        private void DrawTopbar(Rect r)
        {
            GUILayout.BeginArea(r);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            var ttl = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            ttl.normal.textColor = C_TEXT_1;
            GUILayout.Label("🌐  " + ToolLang.Get("UI Localization", "Локализация UI"), ttl, GUILayout.Height(28));

            GUILayout.FlexibleSpace();

            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            st.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get("Default lang:", "Язык по умолч.:"), st, GUILayout.Height(28));

            int curIdx = Mathf.Max(0, _settings.Languages.IndexOf(_table.DefaultLanguage));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(curIdx, _settings.Languages.ToArray(), GUILayout.Width(80), GUILayout.Height(22));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_table, "Default Lang");
                _table.DefaultLanguage = _settings.Languages[newIdx];
                EditorUtility.SetDirty(_table);
            }

            GUILayout.Space(8);

            if (GUILayout.Button("📥 " + ToolLang.Get("Import", "Импорт"), GUILayout.Height(22), GUILayout.Width(96)))
            {
                ImportJson();
            }
            if (GUILayout.Button("📤 " + ToolLang.Get("Export", "Экспорт"), GUILayout.Height(22), GUILayout.Width(96)))
            {
                ExportJson();
            }

            GUILayout.Space(8);

            // Toggle подсказок (общий с Settings-модулем — общий EditorPref)
            bool guide = NovellaSettingsModule.ShowGuide;
            GUI.backgroundColor = guide ? C_ACCENT : Color.white;
            if (GUILayout.Button("💡  " + (guide ? ToolLang.Get("Hints: ON", "Подск.: вкл") : ToolLang.Get("Hints: OFF", "Подск.: выкл")),
                GUILayout.Height(22), GUILayout.Width(120)))
            {
                NovellaSettingsModule.ShowGuide = !guide;
                Repaint();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawGuideTip(string text)
        {
            if (!NovellaSettingsModule.ShowGuide) return;
            var sty = new GUIStyle(EditorStyles.label) { fontSize = 11, wordWrap = true, padding = new RectOffset(10, 10, 8, 8) };
            sty.normal.textColor = NovellaSettingsModule.GetHintColor();
            Rect rec = GUILayoutUtility.GetRect(new GUIContent("💡 " + text), sty);
            Color a = C_ACCENT;
            EditorGUI.DrawRect(rec, new Color(a.r, a.g, a.b, 0.10f));
            DrawRectBorder(rec, new Color(a.r, a.g, a.b, 0.4f));
            EditorGUI.DrawRect(new Rect(rec.x, rec.y, 3, rec.height), a);
            GUI.Label(rec, "💡 " + text, sty);
            GUILayout.Space(8);
        }

        // ─── Keys list (left) ───────────────────────────────────────────────────

        private void DrawKeysList(Rect r)
        {
            GUILayout.BeginArea(r);

            // Add new key row
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            _newKey = EditorGUILayout.TextField(_newKey, GUILayout.Height(22));
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("+", GUILayout.Width(28), GUILayout.Height(22)))
            {
                if (!string.IsNullOrWhiteSpace(_newKey))
                {
                    Undo.RecordObject(_table, "Add Key");
                    var entry = _table.AddKey(_newKey.Trim());
                    if (entry != null) _selectedKey = entry.Key;
                    _newKey = "";
                    EditorUtility.SetDirty(_table);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(10);
            GUILayout.EndHorizontal();

            // Filter
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            Rect filterRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            EditorGUI.DrawRect(filterRect, C_BG_RAISED);
            DrawRectBorder(filterRect, C_BORDER);
            var sst = new GUIStyle(EditorStyles.textField) { fontSize = 11, padding = new RectOffset(8, 8, 4, 4) };
            sst.normal.background = null; sst.focused.background = null;
            sst.normal.textColor = C_TEXT_1; sst.focused.textColor = C_TEXT_1;
            _filter = GUI.TextField(filterRect, _filter, sst);
            if (string.IsNullOrEmpty(_filter))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(filterRect.x + 6, filterRect.y, filterRect.width, filterRect.height), "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }

            GUILayout.Space(8);

            // Counter
            var cnt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            cnt.normal.textColor = C_TEXT_3;
            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(string.Format(ToolLang.Get("{0} keys", "{0} ключей"), _table.Entries.Count), cnt);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            string filter = _filter?.Trim().ToLowerInvariant() ?? "";

            _keysScroll = GUILayout.BeginScrollView(_keysScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            for (int i = 0; i < _table.Entries.Count; i++)
            {
                var e = _table.Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Key)) continue;
                if (filter.Length > 0 && !e.Key.ToLowerInvariant().Contains(filter)) continue;
                DrawKeyRow(e);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawKeyRow(NovellaUILocalizationTable.Entry e)
        {
            bool isSel = e.Key == _selectedKey;

            GUILayout.BeginHorizontal();
            GUILayout.Space(0);
            Rect row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUILayout.Space(0);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, isSel ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f) : Color.clear);
            if (isSel) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), C_ACCENT);

            // Кружок-индикатор: заполнен ли default-язык
            string defText = e.Get(_table.DefaultLanguage);
            Color dotCol = string.IsNullOrEmpty(defText) ? C_WARN : new Color(0.40f, 0.78f, 0.45f);
            EditorGUI.DrawRect(new Rect(row.x + 12, row.y + row.height * 0.5f - 3, 6, 6), dotCol);

            var st = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            st.normal.textColor = isSel ? C_TEXT_1 : C_TEXT_2;
            GUI.Label(new Rect(row.x + 26, row.y, row.width - 32, row.height), e.Key, st);

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _selectedKey = e.Key;
                if (Event.current.button == 1)
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(ToolLang.Get("Rename", "Переименовать")), false, () => StartRenameKey(e.Key));
                    menu.AddItem(new GUIContent(ToolLang.Get("Delete", "Удалить")), false, () =>
                    {
                        if (EditorUtility.DisplayDialog(
                            ToolLang.Get("Delete key", "Удалить ключ"),
                            string.Format(ToolLang.Get("Delete '{0}' and all its translations?", "Удалить «{0}» и все его переводы?"), e.Key),
                            ToolLang.Get("Delete", "Удалить"), ToolLang.Get("Cancel", "Отмена")))
                        {
                            Undo.RecordObject(_table, "Delete Key");
                            _table.RemoveKey(e.Key);
                            if (_selectedKey == e.Key) _selectedKey = null;
                            EditorUtility.SetDirty(_table);
                        }
                    });
                    menu.ShowAsContext();
                }
                Event.current.Use();
                Repaint();
            }
        }

        private void StartRenameKey(string oldKey)
        {
            string newKey = oldKey;
            // Простой синхронный inline через диалог — UX skeleton, дорабатывать в будущем
            // (сейчас Unity не даёт inline-редактирование IMGUI Label простым способом)
            // Используем модальный popup с TextField.
            NovellaPopupRename.Show(oldKey, (entered) =>
            {
                if (string.IsNullOrWhiteSpace(entered) || entered == oldKey) return;
                Undo.RecordObject(_table, "Rename Key");
                if (_table.RenameKey(oldKey, entered.Trim()))
                {
                    if (_selectedKey == oldKey) _selectedKey = entered.Trim();
                    EditorUtility.SetDirty(_table);
                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog(ToolLang.Get("Rename failed", "Не получилось переименовать"),
                        ToolLang.Get("Key already exists or invalid name.", "Ключ уже существует или некорректное имя."), "OK");
                }
            });
        }

        // ─── Right pane: editor for selected key ────────────────────────────────

        private void DrawKeyEditor(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 14, r.y, r.width - 28, r.height));

            if (string.IsNullOrEmpty(_selectedKey) || _table.FindEntry(_selectedKey) == null)
            {
                GUILayout.Space(20);
                DrawGuideTip(ToolLang.Get(
                    "Localization keys are short ID strings (e.g. 'btn_play', 'title_main') that your UI elements reference. Each key has translations for every language. Press '+' on the left to add a new key.",
                    "Ключи локализации — короткие ID-строки (например, 'btn_play', 'title_main'), на которые ссылаются UI-элементы. У каждого ключа есть перевод на каждом языке. Нажми «+» слева, чтобы добавить новый ключ."));
                GUILayout.Space(20);
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                emptySt.normal.textColor = C_TEXT_3;
                GUILayout.Label(ToolLang.Get(
                    "Select a key on the left, or add a new one to start editing translations.",
                    "Выбери ключ слева или добавь новый, чтобы начать редактировать переводы."), emptySt);
                GUILayout.EndArea();
                return;
            }

            var entry = _table.FindEntry(_selectedKey);

            GUILayout.Space(16);

            // Header
            GUILayout.BeginHorizontal();
            var keySt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            keySt.normal.textColor = C_TEXT_1;
            GUILayout.Label("🔑  " + entry.Key, keySt);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(ToolLang.Get("✎ Rename", "✎ Переименовать"), GUILayout.Width(120), GUILayout.Height(22)))
                StartRenameKey(entry.Key);
            GUI.backgroundColor = C_DANGER;
            if (GUILayout.Button(ToolLang.Get("🗑 Delete", "🗑 Удалить"), GUILayout.Width(96), GUILayout.Height(22)))
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("Delete key", "Удалить ключ"),
                    string.Format(ToolLang.Get("Delete '{0}'?", "Удалить «{0}»?"), entry.Key),
                    ToolLang.Get("Delete", "Удалить"), ToolLang.Get("Cancel", "Отмена")))
                {
                    Undo.RecordObject(_table, "Delete Key");
                    _table.RemoveKey(entry.Key);
                    _selectedKey = null;
                    EditorUtility.SetDirty(_table);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(12);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(12);

            DrawGuideTip(ToolLang.Get(
                "Fill in a translation for each language. The default-language field (marked ⭐) is the fallback when other languages are empty. The ✕ button next to a language removes it from the project entirely.",
                "Заполни перевод для каждого языка. Поле языка по умолчанию (⭐) используется как fallback, если другие языки пусты. Кнопка ✕ рядом с языком полностью удаляет его из проекта."));

            // Translations per language
            _editScroll = GUILayout.BeginScrollView(_editScroll);

            foreach (var lang in _settings.Languages)
            {
                DrawLangField(entry, lang);
                GUILayout.Space(10);
            }

            GUILayout.Space(20);

            // Add language form
            if (_addLangMode)
            {
                GUILayout.BeginHorizontal();
                _newLang = EditorGUILayout.TextField(_newLang, GUILayout.Width(120), GUILayout.Height(22));
                GUI.backgroundColor = C_ACCENT;
                if (GUILayout.Button(ToolLang.Get("Add", "Добавить"), GUILayout.Width(80), GUILayout.Height(22)))
                {
                    if (!string.IsNullOrWhiteSpace(_newLang) && !_settings.Languages.Contains(_newLang.Trim().ToUpperInvariant()))
                    {
                        Undo.RecordObject(_settings, "Add Language");
                        _settings.Languages.Add(_newLang.Trim().ToUpperInvariant());
                        EditorUtility.SetDirty(_settings);
                    }
                    _newLang = "";
                    _addLangMode = false;
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button(ToolLang.Get("Cancel", "Отмена"), GUILayout.Width(80), GUILayout.Height(22)))
                {
                    _newLang = "";
                    _addLangMode = false;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("➕ " + ToolLang.Get("Add language", "Добавить язык"), GUILayout.Width(180), GUILayout.Height(22)))
                {
                    _addLangMode = true;
                }
            }

            GUILayout.Space(20);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLangField(NovellaUILocalizationTable.Entry entry, string lang)
        {
            // Label с языком + индикатор default
            GUILayout.BeginHorizontal();
            var langSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            langSt.normal.textColor = (lang == _table.DefaultLanguage) ? C_ACCENT : C_TEXT_2;
            string suffix = (lang == _table.DefaultLanguage) ? "  ⭐ " + ToolLang.Get("default", "по умолч.") : "";
            GUILayout.Label(lang + suffix, langSt, GUILayout.Width(140));

            GUILayout.FlexibleSpace();

            // Кнопка удаления языка (только если не default)
            if (lang != _table.DefaultLanguage && _settings.Languages.Count > 1)
            {
                GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Remove language", "Удалить язык"),
                        string.Format(ToolLang.Get("Remove '{0}' from project? All translations for it across all keys will be lost.", "Удалить «{0}» из проекта? Все переводы на этом языке будут утеряны."), lang),
                        ToolLang.Get("Remove", "Удалить"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        Undo.RecordObject(_settings, "Remove Lang");
                        _settings.Languages.Remove(lang);
                        EditorUtility.SetDirty(_settings);

                        // Also remove from table entries
                        Undo.RecordObject(_table, "Remove Lang");
                        foreach (var e in _table.Entries)
                            e.Values.RemoveAll(v => v.LanguageID == lang);
                        EditorUtility.SetDirty(_table);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            string current = entry.Get(lang) ?? "";
            EditorGUI.BeginChangeCheck();
            string updated = EditorGUILayout.TextArea(current, GUILayout.MinHeight(40), GUILayout.MaxHeight(120));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_table, "Edit Translation");
                entry.Set(lang, updated);
                _table.InvalidateCache();
                EditorUtility.SetDirty(_table);
            }
        }

        // ─── JSON ───────────────────────────────────────────────────────────────

        private void ImportJson()
        {
            string p = EditorUtility.OpenFilePanel(ToolLang.Get("Import JSON", "Импорт JSON"), "", "json");
            if (string.IsNullOrEmpty(p)) return;
            string txt = File.ReadAllText(p);
            Undo.RecordObject(_table, "Import JSON");
            if (_table.ImportJson(txt))
            {
                EditorUtility.SetDirty(_table);
                AssetDatabase.SaveAssets();
                Repaint();
            }
            else
            {
                EditorUtility.DisplayDialog(ToolLang.Get("Failed", "Ошибка"),
                    ToolLang.Get("JSON import failed.", "Импорт не удался."), "OK");
            }
        }

        private void ExportJson()
        {
            string p = EditorUtility.SaveFilePanel(ToolLang.Get("Export JSON", "Экспорт JSON"), "", "novella_ui_loc", "json");
            if (string.IsNullOrEmpty(p)) return;
            File.WriteAllText(p, _table.ExportJson());
            EditorUtility.RevealInFinder(p);
        }

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Простой модальный popup для переименования.
    // ════════════════════════════════════════════════════════════════════════
    internal class NovellaPopupRename : EditorWindow
    {
        private string _value;
        private System.Action<string> _onConfirm;

        public static void Show(string current, System.Action<string> onConfirm)
        {
            var win = CreateInstance<NovellaPopupRename>();
            win._value = current;
            win._onConfirm = onConfirm;
            win.titleContent = new GUIContent(ToolLang.Get("Rename", "Переименование"));
            win.minSize = new Vector2(380, 110);
            win.maxSize = new Vector2(380, 110);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            GUILayout.Label(ToolLang.Get("New name:", "Новое имя:"));
            _value = EditorGUILayout.TextField(_value);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(ToolLang.Get("Cancel", "Отмена"), GUILayout.Width(100)))
            {
                Close();
            }
            GUI.backgroundColor = NovellaSettingsModule.GetAccentColor();
            if (GUILayout.Button(ToolLang.Get("Rename", "Переименовать"), GUILayout.Width(140)))
            {
                _onConfirm?.Invoke(_value);
                Close();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
        }
    }
}
