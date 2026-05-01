using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Character Editor — полностью переписанный под новый стиль Novella Studio.
    /// Тёмная палитра #13141B / #5BC0EB, карточный layout, расширенное превью,
    /// drag-zone для спрайтов. Совместим со старым INovellaStudioModule API.
    /// </summary>
    public class NovellaCharacterEditorModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Characters", "Персонажи");
        public string ModuleIcon => "❖";

        [MenuItem("Tools/Novella Engine/Character Editor")]
        public static void OpenWindow()
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.SwitchToModule(1);
        }

        public static void OpenWithCharacter(NovellaCharacter charToSelect)
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null)
            {
                NovellaHubWindow.Instance.SwitchToModule(1);
                var module = NovellaHubWindow.Instance.GetModule(1) as NovellaCharacterEditorModule;
                module?.SelectCharacter(charToSelect);
            }
        }

        // Все цвета — динамические, читаются из NovellaSettingsModule (Hub → Settings).
        // Производные оттенки (sidebar/raised/border, secondary/muted/disabled text)
        // рассчитываются автоматически от двух базовых: Interface и Text.
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();

        private EditorWindow _window;
        private List<NovellaCharacter> _characters = new List<NovellaCharacter>();

        private List<NovellaCharacter> _selectedCharacters = new List<NovellaCharacter>();
        private NovellaCharacter _lastClickedCharacter;
        private List<NovellaCharacter> _currentVisualList = new List<NovellaCharacter>();

        private NovellaCharacter _selectedCharacter;
        private SerializedObject _serializedObject;

        private string _searchQuery = "";
        private Vector2 _listScroll;
        private Vector2 _settingsScroll;

        private float _previewZoom = 1f;
        private Vector2 _previewPan;
        private bool _previewAutoFit = true;
        private int _previewEmotionIndex = -1;
        private int _editingEmotionIndex = -1;

        private int _expandedLayerIndex = -1;
        private float _emotionScrollX;

        private int _highlightLayerIndex = -1;
        private double _highlightTime = 0;
        private const double HIGHLIGHT_DURATION = 0.5;

        private float _sidebarWidth = 220f;
        private float _settingsWidth = 340f;

        private bool _needsRefresh;
        private bool _hasInitialized;

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
            RefreshList();
        }

        public void OnDisable() { }

        public void SelectCharacter(NovellaCharacter c)
        {
            _selectedCharacters.Clear();
            if (c != null) _selectedCharacters.Add(c);
            _lastClickedCharacter = c;

            _selectedCharacter = c;
            _serializedObject = c != null ? new SerializedObject(c) : null;
            _previewAutoFit = true;
            _previewEmotionIndex = -1;
            _editingEmotionIndex = -1;
            _expandedLayerIndex = -1;
            GUI.FocusControl(null);
            _window?.Repaint();
        }

        private void HandleCharacterClick(NovellaCharacter c, Event e)
        {
            if (e.shift && _lastClickedCharacter != null && _currentVisualList.Contains(_lastClickedCharacter))
            {
                int startIdx = _currentVisualList.IndexOf(_lastClickedCharacter);
                int endIdx = _currentVisualList.IndexOf(c);
                int min = Mathf.Min(startIdx, endIdx);
                int max = Mathf.Max(startIdx, endIdx);

                _selectedCharacters.Clear();
                for (int i = min; i <= max; i++)
                {
                    _selectedCharacters.Add(_currentVisualList[i]);
                }
            }
            else if (e.control || e.command)
            {
                if (_selectedCharacters.Contains(c)) _selectedCharacters.Remove(c);
                else _selectedCharacters.Add(c);

                _lastClickedCharacter = c;
            }
            else
            {
                _selectedCharacters.Clear();
                _selectedCharacters.Add(c);
                _lastClickedCharacter = c;
            }

            if (_selectedCharacters.Count == 1)
            {
                _selectedCharacter = _selectedCharacters[0];
                _serializedObject = new SerializedObject(_selectedCharacter);
                _previewAutoFit = true;
                _previewEmotionIndex = -1;
                _editingEmotionIndex = -1;
                _expandedLayerIndex = -1;
            }
            else
            {
                _selectedCharacter = null;
                _serializedObject = null;
            }

            GUI.FocusControl(null);
            _window?.Repaint();
        }

        public void DrawGUI(Rect position)
        {
            if (!_hasInitialized) { RefreshList(); _hasInitialized = true; }
            if (_needsRefresh) { RefreshList(); _needsRefresh = false; }

            Rect bg = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(bg, C_BG_PRIMARY);

            float padding = 0;
            Rect sideRect = new Rect(padding, padding, _sidebarWidth, position.height - padding * 2);
            Rect settingsRect = new Rect(sideRect.xMax, padding, _settingsWidth, position.height - padding * 2);
            Rect previewRect = new Rect(settingsRect.xMax, padding, position.width - settingsRect.xMax - padding, position.height - padding * 2);

            DrawCharactersSidebar(sideRect);
            DrawDivider(new Rect(sideRect.xMax - 1, 0, 1, position.height));

            if (_selectedCharacters.Count == 1 && _selectedCharacter != null)
            {
                DrawSettingsPanel(settingsRect);
                DrawDivider(new Rect(settingsRect.xMax - 1, 0, 1, position.height));
                DrawPreviewPanel(previewRect);
            }
            else if (_selectedCharacters.Count > 1)
            {
                Rect multiRect = new Rect(sideRect.xMax, padding, position.width - sideRect.xMax - padding, position.height - padding * 2);
                DrawMultiSelectState(multiRect);
            }
            else
            {
                DrawEmptyState(new Rect(sideRect.xMax, 0, position.width - sideRect.xMax, position.height));
            }
        }

        // ───────────────────────────────────────────────
        // КОЛОНКА 1: Список персонажей
        // ───────────────────────────────────────────────

        private void DrawCharactersSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);
            GUILayout.BeginArea(rect);

            GUILayout.Space(14);
            DrawIndentedLabel(ToolLang.Get("Characters", "Персонажи"), 14, fontSize: 14, bold: true, color: C_TEXT_1);
            DrawIndentedLabel(string.Format(ToolLang.Get("{0} in this story", "{0} в этой истории"), _characters.Count), 14, fontSize: 11, color: C_TEXT_3);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("＋ " + ToolLang.Get("New character", "Новый персонаж"), GUILayout.Height(32)))
            {
                EditorApplication.delayCall += CreateNewCharacter;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            Rect searchRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(searchRect, C_BG_PRIMARY);
            DrawRectBorder(searchRect, C_BORDER);
            GUI.SetNextControlName("CharSearch");
            _searchQuery = GUI.TextField(new Rect(searchRect.x + 8, searchRect.y + 6, searchRect.width - 16, 16), _searchQuery, GUIStyle.none);
            if (string.IsNullOrEmpty(_searchQuery))
            {
                GUI.color = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 8, searchRect.y + 4, searchRect.width - 16, 20), "🔍  " + ToolLang.Get("Search…", "Поиск…"));
                GUI.color = Color.white;
            }
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            float listHeight = rect.height - GUILayoutUtility.GetLastRect().yMax - 4;
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Height(listHeight));

            var filtered = _characters.Where(c => c != null && Match(c)).ToList();

            var mains = filtered.Where(c => c.IsPlayerCharacter).ToList();
            var favs = filtered.Where(c => !c.IsPlayerCharacter && c.IsFavorite).ToList();
            var others = filtered.Where(c => !c.IsPlayerCharacter && !c.IsFavorite).ToList();

            _currentVisualList.Clear();

            if (mains.Count > 0)
            {
                DrawIndentedLabel("★ " + ToolLang.Get("MAIN HEROES", "ГЕРОИ").ToUpper(), 14, fontSize: 9, bold: true, color: C_TEXT_3);
                foreach (var c in mains) { _currentVisualList.Add(c); DrawCharRow(c); }
                GUILayout.Space(6);
            }
            if (favs.Count > 0)
            {
                DrawIndentedLabel("♥ " + ToolLang.Get("FAVORITES", "ИЗБРАННЫЕ").ToUpper(), 14, fontSize: 9, bold: true, color: C_TEXT_3);
                foreach (var c in favs) { _currentVisualList.Add(c); DrawCharRow(c); }
                GUILayout.Space(6);
            }
            if (others.Count > 0)
            {
                DrawIndentedLabel(ToolLang.Get("SUPPORTING", "ВТОРОСТЕПЕННЫЕ"), 14, fontSize: 9, bold: true, color: C_TEXT_3);
                foreach (var c in others) { _currentVisualList.Add(c); DrawCharRow(c); }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool Match(NovellaCharacter c)
        {
            if (string.IsNullOrEmpty(_searchQuery)) return true;
            string q = _searchQuery.ToLowerInvariant();
            return (c.name ?? "").ToLowerInvariant().Contains(q)
                || (c.CharacterID ?? "").ToLowerInvariant().Contains(q)
                || (c.DisplayName_EN ?? "").ToLowerInvariant().Contains(q)
                || (c.DisplayName_RU ?? "").ToLowerInvariant().Contains(q);
        }

        private void DrawCharRow(NovellaCharacter c)
        {
            bool active = _selectedCharacters.Contains(c);

            Rect r = GUILayoutUtility.GetRect(0, 42, GUILayout.ExpandWidth(true));
            r.x += 8; r.width -= 16;

            if (active) EditorGUI.DrawRect(r, C_BG_RAISED);
            else if (r.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(r, new Color(C_BG_RAISED.r, C_BG_RAISED.g, C_BG_RAISED.b, 0.6f));
                if (Event.current.type == EventType.MouseMove) _window?.Repaint();
            }

            if (active)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y + 3, 2, r.height - 6), C_ACCENT);
            }

            float avSize = 26;
            Rect avRect = new Rect(r.x + 8, r.y + (r.height - avSize) / 2, avSize, avSize);
            Color avColor = c.ThemeColor.maxColorComponent < 0.05f ? C_ACCENT : c.ThemeColor;
            EditorGUI.DrawRect(avRect, new Color(avColor.r, avColor.g, avColor.b, 0.18f));
            DrawRectBorder(avRect, avColor);
            string firstChar = ExtractAvatarLetter(c);
            var avStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            avStyle.normal.textColor = avColor;
            GUI.Label(avRect, firstChar, avStyle);

            Rect nameRect = new Rect(avRect.xMax + 8, r.y + 5, r.width - avSize - 30, 16);
            var nameStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            nameStyle.normal.textColor = active ? C_TEXT_1 : C_TEXT_2;
            string displayName = !string.IsNullOrEmpty(c.DisplayName_EN) ? c.DisplayName_EN : c.name;
            GUI.Label(nameRect, displayName, nameStyle);

            Rect idRect = new Rect(avRect.xMax + 8, r.y + 22, r.width - avSize - 30, 14);
            var idStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            idStyle.normal.textColor = C_TEXT_4;
            GUI.Label(idRect, !string.IsNullOrEmpty(c.CharacterID) ? c.CharacterID : c.name, idStyle);

            if (c.IsPlayerCharacter)
            {
                var starStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
                starStyle.normal.textColor = new Color(0.96f, 0.76f, 0.43f);
                GUI.Label(new Rect(r.xMax - 22, r.y, 16, r.height), "★", starStyle);
            }
            else if (c.IsFavorite)
            {
                var favStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, fontSize = 11 };
                favStyle.normal.textColor = new Color(0.9f, 0.4f, 0.45f);
                GUI.Label(new Rect(r.xMax - 22, r.y, 16, r.height), "♥", favStyle);
            }

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                HandleCharacterClick(c, Event.current);
                Event.current.Use();
            }
        }

        // ───────────────────────────────────────────────
        // КОЛОНКА 2: Настройки выбранного
        // ───────────────────────────────────────────────

        private void DrawSettingsPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            GUILayout.BeginArea(rect);

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.BeginVertical();

            _settingsScroll = GUILayout.BeginScrollView(_settingsScroll);

            if (_serializedObject == null || _serializedObject.targetObject != _selectedCharacter)
                _serializedObject = new SerializedObject(_selectedCharacter);

            _serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            string headerName = !string.IsNullOrEmpty(_selectedCharacter.DisplayName_EN) ? _selectedCharacter.DisplayName_EN : _selectedCharacter.name;

            var headStyle = new GUIStyle(EditorStyles.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            headStyle.normal.textColor = C_TEXT_1;
            string truncatedHeader = TruncateForWidth(headerName, headStyle, _settingsWidth - 100);

            GUILayout.Label(truncatedHeader, headStyle);

            GUILayout.FlexibleSpace();
            DrawHintsToggleButton();
            GUILayout.EndHorizontal();

            string metaFormat = ToolLang.Get("id: {0} · {1} emotions · {2} layers", "id: {0} · {1} эмоций · {2} слоёв");
            string meta = string.Format(metaFormat,
                _selectedCharacter.CharacterID ?? "?",
                _selectedCharacter.Emotions?.Count ?? 0,
                _selectedCharacter.BaseLayers?.Count ?? 0);
            DrawMeta(meta);

            GUILayout.Space(14);

            var idProp = _serializedObject.FindProperty("CharacterID");
            string oldId = idProp.stringValue;
            DrawFieldLabel(ToolLang.Get("INTERNAL ID", "ВНУТРЕННИЙ ID"), idProp.stringValue.Length, NovellaCharacter.MAX_ID_LENGTH);
            idProp.stringValue = DrawDarkTextField(idProp.stringValue, "char_id_field", true);
            if (idProp.stringValue.Length > NovellaCharacter.MAX_ID_LENGTH) idProp.stringValue = idProp.stringValue.Substring(0, NovellaCharacter.MAX_ID_LENGTH);
            DrawHint(ToolLang.Get(
                "A short <b>nickname</b> the engine uses to remember this character. Use English letters and digits, no spaces and no brackets — like <b>Mila</b> or <b>old_wizard</b>. <b>Players never see this</b> — it is only for you.",
                "Короткий <b>позывной</b> по которому движок узнаёт этого персонажа. Английские буквы и цифры, без пробелов и скобок — например <b>Mila</b> или <b>old_wizard</b>. <b>Игрок этого никогда не увидит</b> — это только для тебя."));

            GUILayout.Space(10);

            var nameRuProp = _serializedObject.FindProperty("DisplayName_RU");
            DrawFieldLabel(ToolLang.Get("DISPLAY NAME (RU)", "ИМЯ (RU)"), nameRuProp.stringValue.Length, NovellaCharacter.MAX_NAME_LENGTH);
            nameRuProp.stringValue = DrawDarkTextField(nameRuProp.stringValue, "char_namen_ru");

            GUILayout.Space(8);

            var nameEnProp = _serializedObject.FindProperty("DisplayName_EN");
            DrawFieldLabel(ToolLang.Get("DISPLAY NAME (EN)", "ИМЯ (EN)"), nameEnProp.stringValue.Length, NovellaCharacter.MAX_NAME_LENGTH);
            nameEnProp.stringValue = DrawDarkTextField(nameEnProp.stringValue, "char_name_en");

            DrawHint(ToolLang.Get(
                "The name that <b>appears in the dialogue box</b> when this character speaks. You can write it differently for each language — the player will see the version in their game language.",
                "Имя, которое <b>появится над репликой</b> когда этот персонаж говорит. Можно написать по-разному для каждого языка — игрок увидит версию для своего языка игры."));

            GUILayout.Space(10);

            DrawFieldLabel(ToolLang.Get("SPEAKER COLOR", "ЦВЕТ СПИКЕРА"), -1, -1);
            var colorProp = _serializedObject.FindProperty("ThemeColor");
            colorProp.colorValue = EditorGUILayout.ColorField(colorProp.colorValue);
            DrawHint(ToolLang.Get(
                "Color of this character's <b>name</b> in the dialogue. Helps players see who is speaking at a glance.",
                "Цвет <b>имени</b> этого персонажа в диалоге. Помогает игроку с одного взгляда понять кто говорит."));

            GUILayout.Space(10);

            var isPlayerProp = _serializedObject.FindProperty("IsPlayerCharacter");
            DrawDarkToggle(ToolLang.Get("Main hero (player)", "Главный герой (игрок)"), isPlayerProp);
            DrawHint(ToolLang.Get(
                "Turn this on if this character is <b>the one the player controls</b> — the hero whose name and look the player can change.",
                "Включи если это <b>персонаж за которого играет игрок</b> — герой, чьё имя и внешность игрок может менять."));

            GUILayout.Space(4);

            var isFavoriteProp = _serializedObject.FindProperty("IsFavorite");
            DrawDarkToggle("♥ " + ToolLang.Get("Favorite character", "В избранное"), isFavoriteProp);
            DrawHint(ToolLang.Get(
                "Pin this character to the separate list in the 'FAVORITES' section.",
                "Отправить персонажа в отдельный список в разделе 'ИЗБРАННЫЕ'."));

            GUILayout.Space(10);

            DrawFieldLabel(ToolLang.Get("GENDER", "ПОЛ"), -1, -1);
            var genderProp = _serializedObject.FindProperty("Gender");
            DrawLocalizedGenderField(genderProp);
            DrawHint(ToolLang.Get(
                "Useful for stories where <b>something depends on gender</b> — for example a different scene for a male and female hero. Doesn't change how the character looks.",
                "Пригодится в истории когда <b>что-то зависит от пола</b> — например разная сцена для героя-мужчины и героини-женщины. На внешний вид не влияет."));

            GUILayout.Space(18);

            DrawSectionHeader("❖ " + ToolLang.Get("LAYERS", "СЛОИ"));
            DrawHint(ToolLang.Get(
                "A <b>layer</b> is one picture stacked on the character — like Body, Hair, Clothes, Face. Pile them up like cut-outs from a magazine to build the look. The <b>top layer in the list is drawn first (behind)</b>, the <b>last layer is drawn on top (in front)</b>. Use ▲▼ to change the order.",
                "<b>Слой</b> — это одна картинка, наложенная на персонажа: Тело, Волосы, Одежда, Лицо. Складывай их как вырезки из журнала чтобы собрать образ. <b>Первый в списке = на заднем плане</b>, <b>последний = впереди всех</b>. Кнопками ▲▼ меняешь порядок."));

            var layersProp = _serializedObject.FindProperty("BaseLayers");
            int toRemove = -1;
            int swapA = -1, swapB = -1;

            for (int i = 0; i < layersProp.arraySize; i++)
            {
                var layer = _selectedCharacter.BaseLayers[i];
                DrawLayerCard(layer, i, layersProp.arraySize, ref toRemove, ref swapA, ref swapB);
                GUILayout.Space(6);
            }

            if (toRemove >= 0)
            {
                _selectedCharacter.BaseLayers.RemoveAt(toRemove);
                EditorUtility.SetDirty(_selectedCharacter);
            }
            if (swapA >= 0 && swapB >= 0)
            {
                var tmp = _selectedCharacter.BaseLayers[swapA];
                _selectedCharacter.BaseLayers[swapA] = _selectedCharacter.BaseLayers[swapB];
                _selectedCharacter.BaseLayers[swapB] = tmp;
                EditorUtility.SetDirty(_selectedCharacter);

                _highlightLayerIndex = swapB;
                _highlightTime = EditorApplication.timeSinceStartup;
                _window?.Repaint();
            }

            Rect addRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(addRect, C_BG_PRIMARY);
            DrawRectBorderDashed(addRect, C_BORDER);
            var addStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            addStyle.normal.textColor = C_TEXT_3;
            GUI.Label(addRect, "+ " + ToolLang.Get("Add layer", "Добавить слой"), addStyle);
            if (Event.current.type == EventType.MouseDown && addRect.Contains(Event.current.mousePosition))
            {
                _selectedCharacter.BaseLayers.Add(new CharacterLayer { LayerType = ECharacterLayer.Clothes, CustomLayerName = "New Layer" });
                EditorUtility.SetDirty(_selectedCharacter);
                Event.current.Use();
            }

            GUILayout.Space(20);

            if (EditorGUI.EndChangeCheck())
            {
                _serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_selectedCharacter);

                bool idActuallyChanged = oldId != _selectedCharacter.CharacterID;
                if (idActuallyChanged && !string.IsNullOrEmpty(_selectedCharacter.CharacterID))
                {
                    string path = AssetDatabase.GetAssetPath(_selectedCharacter);
                    bool isDuplicate = _characters.Any(c => c != _selectedCharacter && c.CharacterID == _selectedCharacter.CharacterID);
                    if (isDuplicate)
                    {
                        EditorUtility.DisplayDialog(
                            ToolLang.Get("Duplicate ID", "ID занят"),
                            ToolLang.Get("Another character already has this ID. Pick a different one!", "Другой персонаж уже имеет этот ID. Выбери другой!"),
                            "OK");
                        _selectedCharacter.CharacterID = oldId;
                    }
                    else if (_selectedCharacter.name != _selectedCharacter.CharacterID)
                    {
                        AssetDatabase.RenameAsset(path, _selectedCharacter.CharacterID);
                        AssetDatabase.SaveAssets();
                        _needsRefresh = true;
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ─────────── Layer card ───────────

        private void DrawLayerCard(CharacterLayer layer, int idx, int total, ref int toRemove, ref int swapA, ref int swapB)
        {
            bool isExpanded = _expandedLayerIndex == idx;
            float bodyH = isExpanded ? 80 : 0;
            float totalH = 38 + bodyH + (isExpanded ? 10 : 0);

            Rect cardRect = GUILayoutUtility.GetRect(0, totalH, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(cardRect, C_BG_SIDE);

            // Header
            Rect headRect = new Rect(cardRect.x, cardRect.y, cardRect.width, 38);
            EditorGUI.DrawRect(headRect, C_BG_RAISED);

            if (_highlightLayerIndex == idx)
            {
                double elapsed = EditorApplication.timeSinceStartup - _highlightTime;
                if (elapsed < HIGHLIGHT_DURATION)
                {
                    float alpha = 1f - (float)(elapsed / HIGHLIGHT_DURATION);
                    EditorGUI.DrawRect(cardRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, alpha * 0.35f));
                    _window?.Repaint();
                }
                else
                {
                    _highlightLayerIndex = -1;
                }
            }

            DrawRectBorder(cardRect, C_BORDER);

            // Order index
            var orderStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
            orderStyle.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(headRect.x + 4, headRect.y, 18, headRect.height), (idx + 1).ToString(), orderStyle);

            // Action buttons
            float btnSize = 22;
            float btnY = headRect.y + (headRect.height - btnSize) / 2;
            float curX = headRect.xMax - 8 - btnSize;

            if (DrawSquareIconBtn(new Rect(curX, btnY, btnSize, btnSize), "×", new Color(0.85f, 0.4f, 0.4f))) toRemove = idx;
            curX -= btnSize + 2;

            bool canMoveDown = idx < total - 1;
            bool canMoveUp = idx > 0;

            if (DrawSquareIconBtn(new Rect(curX, btnY, btnSize, btnSize), "▼", C_TEXT_2, disabled: !canMoveDown))
            { swapA = idx; swapB = idx + 1; }
            curX -= btnSize + 2;

            if (DrawSquareIconBtn(new Rect(curX, btnY, btnSize, btnSize), "▲", C_TEXT_2, disabled: !canMoveUp))
            { swapA = idx; swapB = idx - 1; }
            curX -= btnSize + 2;

            if (DrawSquareIconBtn(new Rect(curX, btnY, btnSize, btnSize), isExpanded ? "▾" : "▸", C_ACCENT)) _expandedLayerIndex = isExpanded ? -1 : idx;

            // Badge
            float badgeW = 86;
            float badgeX = curX - badgeW - 10;
            Rect badgeRect = new Rect(badgeX, headRect.y + 10, badgeW, 18);

            Color badgeCol = GetLayerTypeColor(layer.LayerType);
            bool badgeHover = badgeRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(badgeRect, new Color(badgeCol.r, badgeCol.g, badgeCol.b, badgeHover ? 0.30f : 0.18f));
            DrawRectBorder(badgeRect, new Color(badgeCol.r, badgeCol.g, badgeCol.b, 0.4f));

            var badgeStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold };
            badgeStyle.normal.textColor = badgeCol;

            string localizedTypeName = GetLocalizedLayerName(layer.LayerType);
            string badgeText = layer.LayerType == ECharacterLayer.Extra && !string.IsNullOrEmpty(layer.CustomLayerName)
                ? layer.CustomLayerName.ToUpper() + " ▾"
                : localizedTypeName.ToUpper() + " ▾";
            GUI.Label(badgeRect, badgeText, badgeStyle);

            // Layer name / Extra name Field
            float nameX = headRect.x + 26;
            float nameW = badgeX - nameX - 10;

            var nameStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, fontStyle = FontStyle.Bold };
            nameStyle.normal.textColor = C_TEXT_1;

            if (layer.LayerType == ECharacterLayer.Extra)
            {
                var fldStyle = new GUIStyle(EditorStyles.textField) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                fldStyle.normal.textColor = C_TEXT_1;
                fldStyle.normal.background = null;

                Rect fldRect = new Rect(nameX, headRect.y + 10, nameW, 18);
                EditorGUI.DrawRect(fldRect, C_BG_PRIMARY);
                DrawRectBorder(fldRect, C_BORDER);

                GUI.SetNextControlName("ExtraName" + idx);
                layer.CustomLayerName = EditorGUI.TextField(new Rect(fldRect.x + 4, fldRect.y, fldRect.width - 8, fldRect.height), layer.CustomLayerName ?? "", fldStyle);
            }
            else
            {
                GUI.Label(new Rect(nameX, headRect.y, nameW, headRect.height), localizedTypeName, nameStyle);
            }

            if (Event.current.type == EventType.MouseDown && badgeHover)
            {
                int capturedIdx = idx;
                var menu = new GenericMenu();
                foreach (ECharacterLayer t in System.Enum.GetValues(typeof(ECharacterLayer)))
                {
                    var captured = t;
                    bool current = layer.LayerType == t;
                    menu.AddItem(new GUIContent(GetLocalizedLayerName(t)), current, () =>
                    {
                        _selectedCharacter.BaseLayers[capturedIdx].LayerType = captured;
                        EditorUtility.SetDirty(_selectedCharacter);
                        _window?.Repaint();
                    });
                }
                menu.DropDown(badgeRect);
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && badgeHover) _window?.Repaint();

            // Expanded body — sprite + transforms
            if (isExpanded)
            {
                Rect bodyRect = new Rect(cardRect.x + 10, cardRect.y + 44, cardRect.width - 20, 70);

                // Thumbnail (left)
                Rect thumbRect = new Rect(bodyRect.x, bodyRect.y, 56, 56);
                bool hasSprite = layer.DefaultSprite != null;
                EditorGUI.DrawRect(thumbRect, C_BG_PRIMARY);
                if (hasSprite)
                {
                    DrawRectBorder(thumbRect, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.4f));
                    DrawSpriteThumbnail(layer.DefaultSprite, thumbRect);
                }
                else
                {
                    DrawRectBorderDashed(thumbRect, C_BORDER);
                    var hintStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, wordWrap = true };
                    hintStyle.normal.textColor = C_TEXT_4;
                    GUI.Label(thumbRect, ToolLang.Get("drag\nimage\nhere", "перетащи\nкартинку\nсюда"), hintStyle);
                }

                if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
                {
                    var capturedLayer = layer;
                    NovellaGalleryWindow.ShowWindow((picked) =>
                    {
                        if (picked is Sprite sp)
                        {
                            capturedLayer.DefaultSprite = sp;
                            EditorUtility.SetDirty(_selectedCharacter);
                            _previewAutoFit = true;
                            _window?.Repaint();
                        }
                        else if (picked is Texture2D tex)
                        {
                            string path = AssetDatabase.GetAssetPath(tex);
                            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                            if (s != null)
                            {
                                capturedLayer.DefaultSprite = s;
                                EditorUtility.SetDirty(_selectedCharacter);
                                _previewAutoFit = true;
                                _window?.Repaint();
                            }
                        }
                    }, NovellaGalleryWindow.EGalleryFilter.Image, "");
                    Event.current.Use();
                }

                // Transforms grid (right)
                float fieldsX = thumbRect.xMax + 10;
                float fieldsW = bodyRect.width - 56 - 10 - 26; // Место для кнопки сброса

                var oldContentColor = GUI.contentColor;
                GUI.contentColor = C_TEXT_1;

                EditorGUIUtility.labelWidth = 24;

                float fh = 16;
                float space = 2;

                layer.Offset.x = EditorGUI.FloatField(new Rect(fieldsX, bodyRect.y, fieldsW, fh), "X", layer.Offset.x);
                layer.Offset.y = EditorGUI.FloatField(new Rect(fieldsX, bodyRect.y + fh + space, fieldsW, fh), "Y", layer.Offset.y);

                layer.Scale.x = EditorGUI.Slider(new Rect(fieldsX, bodyRect.y + (fh + space) * 2, fieldsW, fh), "Sx", layer.Scale.x, 0.1f, 5f);
                layer.Scale.y = EditorGUI.Slider(new Rect(fieldsX, bodyRect.y + (fh + space) * 3, fieldsW, fh), "Sy", layer.Scale.y, 0.1f, 5f);

                EditorGUIUtility.labelWidth = 0;
                GUI.contentColor = oldContentColor;

                if (GUI.Button(new Rect(fieldsX + fieldsW + 4, bodyRect.y + (fh + space) * 2, 22, fh * 2 + space), new GUIContent("R", ToolLang.Get("Reset transforms", "Сброс X,Y,Scale"))))
                {
                    layer.Offset = Vector2.zero;
                    layer.Scale = Vector2.one;
                    GUI.FocusControl(null);
                }
            }

            _selectedCharacter.BaseLayers[idx] = layer;
        }

        private string GetLocalizedLayerName(ECharacterLayer t)
        {
            return t switch
            {
                ECharacterLayer.Body => ToolLang.Get("Body", "Тело"),
                ECharacterLayer.Face => ToolLang.Get("Face", "Лицо"),
                ECharacterLayer.Hair => ToolLang.Get("Hair", "Волосы"),
                ECharacterLayer.Clothes => ToolLang.Get("Clothes", "Одежда"),
                ECharacterLayer.Accessories => ToolLang.Get("Accessories", "Аксессуары"),
                ECharacterLayer.Extra => ToolLang.Get("Extra", "Экстра"),
                _ => t.ToString()
            };
        }

        private Color GetLayerTypeColor(ECharacterLayer t)
        {
            return t switch
            {
                ECharacterLayer.Body => new Color(0.36f, 0.75f, 0.92f),
                ECharacterLayer.Face => new Color(0.63f, 0.49f, 1f),
                ECharacterLayer.Hair => new Color(1f, 0.55f, 0.45f),
                ECharacterLayer.Clothes => new Color(0.48f, 0.81f, 0.62f),
                ECharacterLayer.Accessories => new Color(0.96f, 0.76f, 0.43f),
                _ => C_TEXT_3,
            };
        }

        // ───────────────────────────────────────────────
        // КОЛОНКА 3: Live Preview
        // ───────────────────────────────────────────────

        private void DrawPreviewPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            GUILayout.BeginArea(rect);

            Rect topRect = new Rect(0, 0, rect.width, 48);
            EditorGUI.DrawRect(topRect, C_BG_PRIMARY);
            EditorGUI.DrawRect(new Rect(0, topRect.yMax - 1, rect.width, 1), C_BORDER);

            var titleStyle = new GUIStyle(EditorStyles.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            titleStyle.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(18, 0, rect.width - 100, 48), ToolLang.Get("Live preview", "Живой предпросмотр"), titleStyle);

            Rect fitRect = new Rect(rect.width - 80, 9, 30, 30);
            if (DrawSquareIconBtn(fitRect, "🎯", C_TEXT_2))
            {
                _previewAutoFit = true;
                _window?.Repaint();
            }
            Rect zoomRect = new Rect(rect.width - 44, 9, 30, 30);
            if (DrawSquareIconBtn(zoomRect, "⊕", C_TEXT_2))
            {
                _previewZoom = 1f; _previewPan = Vector2.zero;
                _window?.Repaint();
            }

            Rect emotionBar = new Rect(0, topRect.yMax, rect.width, 42);
            EditorGUI.DrawRect(emotionBar, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(0, emotionBar.yMax - 1, rect.width, 1), C_BORDER);

            DrawEmotionsScroll(emotionBar);

            float bottomBarH = 48;
            Rect stageRect = new Rect(0, emotionBar.yMax, rect.width, rect.height - emotionBar.yMax - bottomBarH);
            DrawStageBackground(stageRect);
            DrawPreviewLayers(stageRect);
            DrawStageBadges(stageRect);

            Rect actionsRect = new Rect(0, rect.height - bottomBarH, rect.width, bottomBarH);
            EditorGUI.DrawRect(actionsRect, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(0, actionsRect.y, rect.width, 1), C_BORDER);

            float bx = 14;
            float by = actionsRect.y + 8;
            if (DrawActionBtn(new Rect(bx, by, 100, 32), "💾 " + ToolLang.Get("Save", "Сохранить"), false))
            {
                AssetDatabase.SaveAssets();
            }
            bx += 108;
            float deleteW = 130;
            if (DrawActionBtn(new Rect(rect.width - deleteW - 14, by, deleteW, 32), "🗑 " + ToolLang.Get("Delete", "Удалить"), false, danger: true))
            {
                DeleteSelectedCharacters();
            }

            int currentEmotionsCount = _selectedCharacter.Emotions?.Count ?? 0;
            if (_editingEmotionIndex >= 0 && _editingEmotionIndex < currentEmotionsCount)
            {
                DrawEmotionOverridesPanel(stageRect);
            }

            GUILayout.EndArea();
        }

        private void DrawStageBackground(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            Color grid = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.4f);
            int step = 40;
            for (int gx = 0; gx < r.width; gx += step) EditorGUI.DrawRect(new Rect(r.x + gx, r.y, 1, r.height), grid);
            for (int gy = 0; gy < r.height; gy += step) EditorGUI.DrawRect(new Rect(r.x, r.y + gy, r.width, 1), grid);
        }

        private void DrawPreviewLayers(Rect stage)
        {
            var renderLayers = BuildRenderLayers();

            if (_previewAutoFit && renderLayers.Count > 0)
            {
                Rect bbox = ComputeLayersBoundingBox(renderLayers);
                if (bbox.width > 1 && bbox.height > 1 && stage.width > 1 && stage.height > 1)
                {
                    float zX = (stage.width * 0.85f) / bbox.width;
                    float zY = (stage.height * 0.85f) / bbox.height;
                    _previewZoom = Mathf.Clamp(Mathf.Min(zX, zY), 0.05f, 5f);
                    _previewPan = new Vector2(-bbox.center.x * _previewZoom, bbox.center.y * _previewZoom);
                }
                _previewAutoFit = false;
            }

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.clickCount == 2 && stage.Contains(e.mousePosition))
            {
                _previewAutoFit = true; e.Use(); _window?.Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1 && stage.Contains(e.mousePosition))
            {
                _previewPan += e.delta; e.Use(); _window?.Repaint();
            }
            else if (e.type == EventType.ScrollWheel && stage.Contains(e.mousePosition))
            {
                _previewZoom = Mathf.Clamp(_previewZoom - e.delta.y * 0.05f, 0.05f, 5f);
                e.Use(); _window?.Repaint();
            }

            GUI.BeginClip(stage);
            for (int i = renderLayers.Count - 1; i >= 0; i--)
            {
                var layer = renderLayers[i];
                if (layer.Sprite == null) continue;

                Texture2D tex = layer.Sprite.texture;
                Rect sRect = layer.Sprite.rect;
                float dW = sRect.width * _previewZoom * layer.Scale.x;
                float dH = sRect.height * _previewZoom * layer.Scale.y;
                float cx = stage.width / 2f + _previewPan.x + (layer.Offset.x * _previewZoom);
                float cy = stage.height / 2f + _previewPan.y - (layer.Offset.y * _previewZoom);

                Rect dst = new Rect(cx - dW / 2, cy - dH / 2, dW, dH);
                Rect uv = new Rect(sRect.x / tex.width, sRect.y / tex.height, sRect.width / tex.width, sRect.height / tex.height);

                Color old = GUI.color;
                GUI.color = layer.Tint;
                GUI.DrawTextureWithTexCoords(dst, tex, uv, true);
                GUI.color = old;
            }
            GUI.EndClip();
        }

        private void DrawStageBadges(Rect stage)
        {
            DrawBadge(new Rect(stage.x + 12, stage.y + 12, 100, 20), $"x: {(int)_previewPan.x}  y: {(int)_previewPan.y}");
            DrawBadge(new Rect(stage.xMax - 90, stage.y + 12, 78, 20), $"zoom: {_previewZoom:F1}×");
            DrawBadge(new Rect(stage.x + 12, stage.yMax - 28, 80, 20), $"{_selectedCharacter.BaseLayers?.Count ?? 0} layers");
            DrawBadge(new Rect(stage.xMax - 220, stage.yMax - 28, 208, 20), ToolLang.Get("RMB drag · scroll zoom · dblclk fit", "ПКМ — сдвиг · колёсико — зум · 2клик — фит"));
        }

        private void DrawEmotionOverridesPanel(Rect stage)
        {
            float w = 280;
            Rect panel = new Rect(stage.xMax - w - 14, stage.y + 50, w, stage.height - 100);
            EditorGUI.DrawRect(panel, C_BG_SIDE);
            DrawRectBorder(panel, C_BORDER);

            GUILayout.BeginArea(panel);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginVertical();

            var em = _selectedCharacter.Emotions[_editingEmotionIndex];

            DrawIndentedLabel(ToolLang.Get("EDITING EMOTION", "РЕДАКТИРУЕМ ЭМОЦИЮ"), 0, fontSize: 9, bold: true, color: C_TEXT_3);
            GUILayout.Space(4);

            DrawFieldLabel(ToolLang.Get("NAME", "ИМЯ"), -1, -1);
            string newName = DrawDarkTextField(em.EmotionName ?? "", "em_name");
            if (newName != em.EmotionName)
            {
                em.EmotionName = newName;
                _selectedCharacter.Emotions[_editingEmotionIndex] = em;
                EditorUtility.SetDirty(_selectedCharacter);
            }

            GUILayout.Space(8);
            DrawIndentedLabel(ToolLang.Get("LAYER OVERRIDES", "ПЕРЕОПРЕДЕЛЕНИЯ СЛОЁВ"), 0, fontSize: 9, bold: true, color: C_TEXT_3);
            GUILayout.Space(4);

            DrawHint(ToolLang.Get(
                "Overrides allow you to change the image, position, and scale of a layer <b>specifically for this emotion</b>. Base layers remain unaffected.",
                "Переопределения меняют картинку, позицию и масштаб слоя <b>только для этой эмоции</b>. Базовые слои при этом не ломаются."));

            if (em.LayerOverrides == null) em.LayerOverrides = new List<CharacterLayerOverride>();

            for (int i = 0; i < em.LayerOverrides.Count; i++)
            {
                var ov = em.LayerOverrides[i];

                Rect cardR = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(cardR, C_BG_PRIMARY);
                DrawRectBorder(cardR, C_BORDER);

                Rect hRect = new Rect(cardR.x, cardR.y, cardR.width, 28);
                EditorGUI.DrawRect(hRect, C_BG_SIDE);
                EditorGUI.DrawRect(new Rect(hRect.x, hRect.yMax - 1, hRect.width, 1), C_BORDER);

                var lblStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, fontSize = 11 };
                lblStyle.normal.textColor = C_TEXT_1;

                string localOvName = "?";
                foreach (var bl in _selectedCharacter.BaseLayers)
                {
                    if (bl.LayerName == ov.LayerName)
                    {
                        localOvName = bl.LayerType == ECharacterLayer.Extra ? bl.LayerName : GetLocalizedLayerName(bl.LayerType);
                        break;
                    }
                }
                GUI.Label(new Rect(hRect.x + 8, hRect.y + 6, 80, 16), localOvName, lblStyle);

                string sprName = ov.OverrideSprite != null ? ov.OverrideSprite.name : ToolLang.Get("(no image)", "(нет картинки)");

                if (GUI.Button(new Rect(hRect.xMax - 146, hRect.y + 4, 96, 20), "🖼 " + TruncateForWidth(sprName, EditorStyles.label, 60)))
                {
                    int capturedI = i;
                    var capturedEm = em;
                    NovellaGalleryWindow.ShowWindow((picked) =>
                    {
                        Sprite s = picked as Sprite;
                        if (s == null && picked is Texture2D tex)
                        {
                            string path = AssetDatabase.GetAssetPath(tex);
                            s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        }
                        if (s != null)
                        {
                            var ovUpd = capturedEm.LayerOverrides[capturedI];
                            ovUpd.OverrideSprite = s;

                            if (ovUpd.Scale == Vector2.zero) ovUpd.Scale = Vector2.one;
                            if (ovUpd.Tint.a == 0) ovUpd.Tint = Color.white;

                            capturedEm.LayerOverrides[capturedI] = ovUpd;
                            _selectedCharacter.Emotions[_editingEmotionIndex] = capturedEm;
                            EditorUtility.SetDirty(_selectedCharacter);
                            _window?.Repaint();
                        }
                    }, NovellaGalleryWindow.EGalleryFilter.Image, "");
                }

                if (GUI.Button(new Rect(hRect.xMax - 46, hRect.y + 4, 20, 20), new GUIContent("R", ToolLang.Get("Reset transforms", "Сброс"))))
                {
                    ov.Offset = Vector2.zero;
                    ov.Scale = Vector2.one;
                    GUI.FocusControl(null);
                }

                if (GUI.Button(new Rect(hRect.xMax - 22, hRect.y + 4, 18, 20), "×"))
                {
                    em.LayerOverrides.RemoveAt(i);
                    EditorUtility.SetDirty(_selectedCharacter);
                    break;
                }

                float tx = cardR.x + 8;
                float ty = cardR.y + 36;
                float tW = (cardR.width - 16 - 12) / 4;

                var oldCol = GUI.contentColor;
                GUI.contentColor = C_TEXT_1;
                EditorGUIUtility.labelWidth = 14;

                ov.Offset.x = EditorGUI.FloatField(new Rect(tx, ty, tW, 18), "X", ov.Offset.x); tx += tW + 4;
                ov.Offset.y = EditorGUI.FloatField(new Rect(tx, ty, tW, 18), "Y", ov.Offset.y); tx += tW + 4;

                EditorGUIUtility.labelWidth = 20;
                ov.Scale.x = EditorGUI.FloatField(new Rect(tx, ty, tW, 18), "Sx", ov.Scale.x == 0 ? 1 : ov.Scale.x); tx += tW + 4;
                ov.Scale.y = EditorGUI.FloatField(new Rect(tx, ty, tW, 18), "Sy", ov.Scale.y == 0 ? 1 : ov.Scale.y);

                EditorGUIUtility.labelWidth = 0;
                GUI.contentColor = oldCol;

                em.LayerOverrides[i] = ov;
                GUILayout.Space(6);
            }

            GUILayout.Space(6);
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("+ " + ToolLang.Get("Override layer", "Добавить слой")))
            {
                var menu = new GenericMenu();
                foreach (var l in _selectedCharacter.BaseLayers)
                {
                    string lname = l.LayerName;

                    string menuName = l.LayerType == ECharacterLayer.Extra ? lname : GetLocalizedLayerName(l.LayerType);

                    menu.AddItem(new GUIContent(menuName), false, () =>
                    {
                        em.LayerOverrides.Add(new CharacterLayerOverride { LayerName = lname, Scale = Vector2.one, Tint = Color.white });
                        _selectedCharacter.Emotions[_editingEmotionIndex] = em;
                        EditorUtility.SetDirty(_selectedCharacter);
                    });
                }
                menu.ShowAsContext();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f);
            if (GUILayout.Button("🗑 " + ToolLang.Get("Delete emotion", "Удалить эмоцию")))
            {
                _selectedCharacter.Emotions.RemoveAt(_editingEmotionIndex);
                EditorUtility.SetDirty(_selectedCharacter);
                _editingEmotionIndex = -1;
                _previewEmotionIndex = -1;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ───────────────────────────────────────────────
        // Empty & Multi-Select states
        // ───────────────────────────────────────────────

        private void DrawEmptyState(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            st.normal.textColor = C_TEXT_3;
            GUI.Label(rect, ToolLang.Get("← Pick a character on the left\nor create a new one",
                                        "← Выбери персонажа слева\nили создай нового"), st);
        }

        private void DrawMultiSelectState(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(400));

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_1;
            GUILayout.Label(string.Format(ToolLang.Get("{0} characters selected", "Выбрано персонажей: {0}"), _selectedCharacters.Count), st);

            int heroes = _selectedCharacters.Count(c => c.IsPlayerCharacter);
            if (heroes > 0)
            {
                GUILayout.Space(10);
                var hst = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                hst.normal.textColor = new Color(0.96f, 0.76f, 0.43f);
                GUILayout.Label(string.Format(ToolLang.Get("Includes {0} main hero(es)!", "Внимание: Включает {0} главны(й/х) геро(й/ев/я)!"), heroes), hst);
            }

            GUILayout.Space(30);

            GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f);
            if (GUILayout.Button("🗑 " + ToolLang.Get("Delete Selected", "Удалить выбранных"), GUILayout.Height(40)))
            {
                EditorApplication.delayCall += DeleteSelectedCharacters;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        private void DeleteSelectedCharacters()
        {
            int total = _selectedCharacters.Count;
            int heroes = _selectedCharacters.Count(c => c.IsPlayerCharacter);

            string title = ToolLang.Get("Delete characters", "Удалить персонажей");
            string msg = string.Format(ToolLang.Get("Are you sure you want to delete {0} selected characters?", "Вы действительно хотите удалить {0} выбранных персонажей?"), total);

            if (heroes > 0)
            {
                msg += "\n\n" + string.Format(ToolLang.Get("Warning: This includes {0} main hero(es)!", "Критическое предупреждение: Среди них {0} главны(й/х) геро(й/ев/я)!"), heroes);
            }

            if (EditorUtility.DisplayDialog(title, msg, ToolLang.Get("Delete", "Удалить"), ToolLang.Get("Cancel", "Отмена")))
            {
                foreach (var sc in _selectedCharacters.ToList())
                {
                    if (sc != null)
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(sc));
                }

                _selectedCharacters.Clear();
                _selectedCharacter = null;
                _serializedObject = null;
                RefreshList();
                _window?.Repaint();
            }
        }

        // ───────────────────────────────────────────────
        // Render layers (с применением эмоции)
        // ───────────────────────────────────────────────

        private struct RenderLayer { public Sprite Sprite; public Vector2 Offset; public Vector2 Scale; public Color Tint; }

        private List<RenderLayer> BuildRenderLayers()
        {
            var result = new List<RenderLayer>();
            if (_selectedCharacter == null || _selectedCharacter.BaseLayers == null) return result;

            CharacterEmotion? activeEmotion = null;
            if (_previewEmotionIndex >= 0 && _previewEmotionIndex < (_selectedCharacter.Emotions?.Count ?? 0))
                activeEmotion = _selectedCharacter.Emotions[_previewEmotionIndex];

            foreach (var baseLayer in _selectedCharacter.BaseLayers)
            {
                Sprite spr = baseLayer.DefaultSprite;
                Vector2 off = baseLayer.Offset;
                Vector2 sca = baseLayer.Scale;
                Color tint = baseLayer.Tint;

                string baseLayerName = baseLayer.LayerName;
                if (activeEmotion.HasValue && activeEmotion.Value.LayerOverrides != null)
                {
                    foreach (var ov in activeEmotion.Value.LayerOverrides)
                    {
                        if (ov.LayerName == baseLayerName && ov.OverrideSprite != null)
                        {
                            spr = ov.OverrideSprite;
                            off = ov.Offset;
                            sca = ov.Scale == Vector2.zero ? Vector2.one : ov.Scale;
                            tint = ov.Tint.a == 0 ? Color.white : ov.Tint;
                            break;
                        }
                    }
                }

                result.Add(new RenderLayer { Sprite = spr, Offset = off, Scale = sca, Tint = tint });
            }
            return result;
        }

        private Rect ComputeLayersBoundingBox(List<RenderLayer> layers)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            bool any = false;
            foreach (var l in layers)
            {
                if (l.Sprite == null) continue;
                float w = l.Sprite.rect.width * l.Scale.x;
                float h = l.Sprite.rect.height * l.Scale.y;
                float cx = l.Offset.x, cy = -l.Offset.y;
                if (cx - w / 2 < minX) minX = cx - w / 2;
                if (cx + w / 2 > maxX) maxX = cx + w / 2;
                if (cy - h / 2 < minY) minY = cy - h / 2;
                if (cy + h / 2 > maxY) maxY = cy + h / 2;
                any = true;
            }
            return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.zero;
        }

        // ───────────────────────────────────────────────
        // CRUD
        // ───────────────────────────────────────────────

        private void RefreshList()
        {
            _characters.Clear();
            string[] guids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (var g in guids)
            {
                var c = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(AssetDatabase.GUIDToAssetPath(g));
                if (c != null) _characters.Add(c);
            }
            _characters = _characters.OrderByDescending(c => c.IsPlayerCharacter).ThenBy(c => c.name).ToList();
        }

        private void CreateNewCharacter()
        {
            string baseDir = "Assets/NovellaEngine/Runtime/Data/Characters";
            if (!System.IO.Directory.Exists(baseDir))
            {
                System.IO.Directory.CreateDirectory(baseDir);
                AssetDatabase.Refresh();
            }
            var ch = ScriptableObject.CreateInstance<NovellaCharacter>();
            ch.CharacterID = "NewCharacter";
            ch.DisplayName_EN = "New Character";
            ch.DisplayName_RU = "Новый Персонаж";
            ch.ThemeColor = C_ACCENT;
            ch.BaseLayers = new List<CharacterLayer>();

            string path = AssetDatabase.GenerateUniqueAssetPath($"{baseDir}/NewCharacter.asset");
            AssetDatabase.CreateAsset(ch, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshList();
            SelectCharacter(ch);
        }

        // ───────────────────────────────────────────────
        // Reusable UI bits
        // ───────────────────────────────────────────────

        private void DrawHeader(string title)
        {
            var st = new GUIStyle(EditorStyles.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_1;
            GUILayout.Label(title, st);
        }

        private void DrawMeta(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            st.normal.textColor = C_TEXT_3;
            GUILayout.Label(text, st);
        }

        private void DrawSectionHeader(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_ACCENT;
            GUILayout.Label(text, st);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BORDER);
            GUILayout.Space(8);
        }

        private void DrawFieldLabel(string text, int curLen, int maxLen)
        {
            GUILayout.BeginHorizontal();
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_3;
            GUILayout.Label(text, st);
            GUILayout.FlexibleSpace();
            if (maxLen > 0)
            {
                var cnt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
                cnt.normal.textColor = curLen >= maxLen ? new Color(1f, 0.4f, 0.4f) : C_TEXT_4;
                GUILayout.Label($"{curLen} / {maxLen}", cnt);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private string DrawDarkTextField(string value, string controlName, bool delayed = false)
        {
            Rect r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, C_BORDER);

            GUI.SetNextControlName(controlName);
            var st = new GUIStyle(EditorStyles.textField);
            st.normal.background = null;
            st.focused.background = null;
            st.normal.textColor = C_TEXT_1;
            st.focused.textColor = C_TEXT_1;
            st.fontSize = 12;
            st.padding = new RectOffset(8, 8, 6, 6);

            Rect inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            if (delayed)
                return EditorGUI.DelayedTextField(inner, value, st);
            return EditorGUI.TextField(inner, value, st);
        }

        private void DrawDarkToggle(string label, SerializedProperty prop)
        {
            Rect r = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, prop.boolValue ? C_ACCENT : C_BORDER);

            Rect chk = new Rect(r.x + 10, r.y + 9, 14, 14);
            EditorGUI.DrawRect(chk, prop.boolValue ? C_ACCENT : C_BG_PRIMARY);
            DrawRectBorder(chk, prop.boolValue ? C_ACCENT : C_BORDER);
            if (prop.boolValue)
            {
                var ck = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold };
                ck.normal.textColor = C_BG_PRIMARY;
                GUI.Label(chk, "✓", ck);
            }

            var lbl = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12, fontStyle = FontStyle.Bold };
            lbl.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x + 32, r.y, r.width - 32, r.height), label, lbl);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                prop.boolValue = !prop.boolValue;
                prop.serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_selectedCharacter);

                if (prop.name == "IsPlayerCharacter" || prop.name == "IsFavorite")
                {
                    _needsRefresh = true;
                }

                Event.current.Use();
                _window?.Repaint();
            }
        }

        private static bool ShowHints
        {
            get => EditorPrefs.GetBool("NovellaCharHints_v2", true);
            set => EditorPrefs.SetBool("NovellaCharHints_v2", value);
        }

        private void DrawHintsToggleButton()
        {
            string label = ShowHints
                ? "💡 " + ToolLang.Get("Hints ON", "Подсказки ВКЛ")
                : "💡 " + ToolLang.Get("Hints OFF", "Подсказки ВЫКЛ");

            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 4, 4) };
            st.normal.textColor = ShowHints ? C_ACCENT : C_TEXT_3;
            Vector2 size = st.CalcSize(new GUIContent(label));

            Rect r = GUILayoutUtility.GetRect(size.x + 14, 22, GUILayout.Width(size.x + 14), GUILayout.Height(22));

            EditorGUI.DrawRect(r, ShowHints ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.13f) : C_BG_PRIMARY);
            DrawRectBorder(r, ShowHints ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f) : C_BORDER);
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                ShowHints = !ShowHints;
                Event.current.Use();
                _window?.Repaint();
            }
        }

        private void DrawLocalizedGenderField(SerializedProperty genderProp)
        {
            string[] options = ToolLang.IsRU
                ? new[] { "Мужской", "Женский" }
                : new[] { "Male", "Female" };

            int currentValue = genderProp.enumValueIndex;
            currentValue = Mathf.Clamp(currentValue, 0, options.Length - 1);

            Rect r = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, C_BORDER);

            var labelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 0, 0, 0) };
            labelStyle.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(r.x, r.y, r.width - 20, r.height), options[currentValue], labelStyle);

            var arrStyle = new GUIStyle(EditorStyles.label) { fontSize = 10, alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 10, 0, 0) };
            arrStyle.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x, r.y, r.width, r.height), "▾", arrStyle);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < options.Length; i++)
                {
                    int captured = i;
                    menu.AddItem(new GUIContent(options[i]), currentValue == i, () =>
                    {
                        genderProp.enumValueIndex = captured;
                        genderProp.serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(_selectedCharacter);
                    });
                }
                menu.DropDown(r);
                Event.current.Use();
            }
        }

        private void DrawHint(string richTextRu)
        {
            if (!ShowHints) return;

            var st = new GUIStyle(EditorStyles.label) { fontSize = 10, wordWrap = true, richText = true, padding = new RectOffset(10, 10, 6, 6) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();

            float maxW = _settingsWidth - 32f - 20f;
            var content = new GUIContent("💡  " + richTextRu);
            float h = st.CalcHeight(content, maxW);

            Rect r = GUILayoutUtility.GetRect(0, h, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);
            GUI.Label(r, content, st);
            GUILayout.Space(4);
        }

        private bool DrawSquareIconBtn(Rect r, string icon, Color color, bool disabled = false)
        {
            if (disabled)
            {
                EditorGUI.DrawRect(r, C_BG_PRIMARY);
                DrawRectBorder(r, new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.5f));
                var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
                st.normal.textColor = new Color(color.r, color.g, color.b, 0.2f);
                GUI.Label(r, icon, st);
                return false;
            }
            else
            {
                bool hovered = r.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(r, hovered ? C_BG_RAISED : C_BG_PRIMARY);
                DrawRectBorder(r, C_BORDER);

                var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold };
                st.normal.textColor = hovered ? color : new Color(color.r, color.g, color.b, 0.7f);
                GUI.Label(r, icon, st);

                if (Event.current.type == EventType.MouseDown && hovered)
                {
                    Event.current.Use();
                    return true;
                }
                if (Event.current.type == EventType.MouseMove && hovered) _window?.Repaint();
                return false;
            }
        }

        private void DrawEmotionsScroll(Rect bar)
        {
            int emotionsCount = _selectedCharacter.Emotions?.Count ?? 0;

            float baseTabW = 90f;
            float emTabW = 100f;
            float editBtnW = 28f;
            float newTabW = 90f;
            float gap = 6f;

            float contentW = baseTabW + gap;
            for (int ei = 0; ei < emotionsCount; ei++)
            {
                contentW += emTabW + gap;
                if (_previewEmotionIndex == ei) contentW += editBtnW;
            }
            contentW += newTabW;

            bool needScroll = contentW > bar.width - 24;
            float arrowW = 26;
            float visibleStart = bar.x + (needScroll ? arrowW + 4 : 12);
            float visibleEnd = bar.xMax - (needScroll ? arrowW + 4 : 12);
            float visibleW = visibleEnd - visibleStart;

            float maxScroll = Mathf.Max(0, contentW - visibleW);
            _emotionScrollX = Mathf.Clamp(_emotionScrollX, 0, maxScroll);

            if (needScroll)
            {
                Rect leftArr = new Rect(bar.x + 4, bar.y + 7, arrowW, 28);
                Rect rightArr = new Rect(bar.xMax - arrowW - 4, bar.y + 7, arrowW, 28);

                if (DrawSquareIconBtn(leftArr, "◀", C_TEXT_2)) _emotionScrollX = Mathf.Max(0, _emotionScrollX - 120);
                if (DrawSquareIconBtn(rightArr, "▶", C_TEXT_2)) _emotionScrollX = Mathf.Min(maxScroll, _emotionScrollX + 120);

                if (Event.current.type == EventType.ScrollWheel && bar.Contains(Event.current.mousePosition))
                {
                    _emotionScrollX = Mathf.Clamp(_emotionScrollX + Event.current.delta.y * 18, 0, maxScroll);
                    Event.current.Use();
                    _window?.Repaint();
                }
            }
            else
            {
                _emotionScrollX = 0;
            }

            Rect clipRect = new Rect(visibleStart, bar.y, visibleW, bar.height);
            GUI.BeginClip(clipRect);

            float ex = -_emotionScrollX;
            float ey = 7;

            ex = DrawEmotionTab(new Rect(ex, ey, baseTabW, 28),
                "▪ " + ToolLang.Get("Base", "Базовый"),
                _previewEmotionIndex == -1,
                () => { _previewEmotionIndex = -1; _editingEmotionIndex = -1; },
                isBold: true) + gap;

            for (int ei = 0; ei < emotionsCount; ei++)
            {
                var em = _selectedCharacter.Emotions[ei];
                string emName = string.IsNullOrEmpty(em.EmotionName) ? $"Emotion {ei + 1}" : em.EmotionName;
                int captured = ei;

                ex = DrawEmotionTab(new Rect(ex, ey, emTabW, 28),
                    "• " + emName,
                    _previewEmotionIndex == ei,
                    () => { _previewEmotionIndex = captured; }) + 4;

                if (_previewEmotionIndex == ei)
                {
                    if (DrawSquareIconBtn(new Rect(ex, ey + 4, 22, 22), "✎", C_ACCENT))
                    {
                        _editingEmotionIndex = _editingEmotionIndex == ei ? -1 : ei;
                    }
                    ex += 28;
                }
            }

            DrawEmotionTab(new Rect(ex, ey, newTabW, 28),
                "+ " + ToolLang.Get("New", "Новая"),
                false,
                () =>
                {
                    if (_selectedCharacter.Emotions == null) _selectedCharacter.Emotions = new List<CharacterEmotion>();
                    _selectedCharacter.Emotions.Add(new CharacterEmotion { EmotionName = "NewEmotion", LayerOverrides = new List<CharacterLayerOverride>() });
                    EditorUtility.SetDirty(_selectedCharacter);
                    _previewEmotionIndex = _selectedCharacter.Emotions.Count - 1;
                });

            GUI.EndClip();
        }

        private float DrawEmotionTab(Rect r, string label, bool active, System.Action onClick, bool isBold = false)
        {
            bool hovered = r.Contains(Event.current.mousePosition);
            Color bg = active ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.13f) : (hovered ? C_BG_RAISED : new Color(0, 0, 0, 0));
            EditorGUI.DrawRect(r, bg);
            if (active) DrawRectBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f));

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, clipping = TextClipping.Clip };
            if (isBold) st.fontStyle = FontStyle.Bold;
            st.normal.textColor = active ? C_ACCENT : C_TEXT_2;

            string drawn = TruncateForWidth(label, st, r.width - 12);
            GUI.Label(r, drawn, st);

            if (Event.current.type == EventType.MouseDown && hovered)
            {
                onClick?.Invoke();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && hovered) _window?.Repaint();
            return r.xMax;
        }

        private static string TruncateForWidth(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;

            int low = 0, high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = text.Substring(0, mid) + "…";
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return text.Substring(0, Mathf.Max(1, low)) + "…";
        }

        private bool DrawActionBtn(Rect r, string label, bool fill, bool danger = false)
        {
            bool hovered = r.Contains(Event.current.mousePosition);
            Color border = danger ? new Color(0.65f, 0.18f, 0.18f) : C_TEXT_1;
            Color textCol = danger ? new Color(0.88f, 0.30f, 0.30f) : C_TEXT_1;

            if (fill)
            {
                EditorGUI.DrawRect(r, hovered ? Color.white : C_TEXT_1);
                textCol = C_BG_PRIMARY;
            }
            else
            {
                if (hovered) EditorGUI.DrawRect(r, danger ? new Color(0.65f, 0.18f, 0.18f, 0.13f) : new Color(C_TEXT_1.r, C_TEXT_1.g, C_TEXT_1.b, 0.06f));
                DrawRectBorder(r, border);
            }

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = textCol;
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && hovered)
            {
                Event.current.Use();
                return true;
            }
            if (Event.current.type == EventType.MouseMove && hovered) _window?.Repaint();
            return false;
        }

        private void DrawIndentedLabel(string text, int indent, int fontSize, bool bold = false, Color color = default)
        {
            if (color == default) color = C_TEXT_2;
            var st = new GUIStyle(EditorStyles.label) { fontSize = fontSize, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, padding = new RectOffset(indent, 0, 0, 0) };
            st.normal.textColor = color;
            GUILayout.Label(text, st);
        }

        private void DrawBadge(Rect r, string text)
        {
            EditorGUI.DrawRect(r, new Color(0.075f, 0.078f, 0.106f, 0.85f));
            DrawRectBorder(r, C_BORDER);
            var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
            st.normal.textColor = C_TEXT_3;
            GUI.Label(r, text, st);
        }

        private static void DrawDivider(Rect r) => EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.5f));

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private static void DrawRectBorderDashed(Rect r, Color c)
        {
            int dash = 4, gap = 3;
            for (int x = 0; x < r.width; x += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x + x, r.y, Mathf.Min(dash, r.width - x), 1), c);
                EditorGUI.DrawRect(new Rect(r.x + x, r.yMax - 1, Mathf.Min(dash, r.width - x), 1), c);
            }
            for (int y = 0; y < r.height; y += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
            }
        }

        // ФИКС: Зависимость приоритета от языка
        private static string ExtractAvatarLetter(NovellaCharacter c)
        {
            string[] candidates = ToolLang.IsRU
                ? new string[] { c.DisplayName_RU, c.CharacterID, c.DisplayName_EN, c.name }
                : new string[] { c.DisplayName_EN, c.CharacterID, c.DisplayName_RU, c.name };

            foreach (var s in candidates)
            {
                if (string.IsNullOrEmpty(s)) continue;

                foreach (var ch in s)
                {
                    if (char.IsLetterOrDigit(ch))
                        return char.ToUpperInvariant(ch).ToString();
                }
            }
            return "?";
        }

        private static void DrawSpriteThumbnail(Sprite sprite, Rect rect)
        {
            if (sprite == null) return;
            Texture2D tex = sprite.texture;
            Rect sRect = sprite.rect;
            Rect uv = new Rect(sRect.x / tex.width, sRect.y / tex.height, sRect.width / tex.width, sRect.height / tex.height);

            float scale = Mathf.Min((rect.width - 6) / sRect.width, (rect.height - 6) / sRect.height);
            float w = sRect.width * scale;
            float h = sRect.height * scale;
            Rect dst = new Rect(rect.x + (rect.width - w) / 2, rect.y + (rect.height - h) / 2, w, h);
            GUI.DrawTextureWithTexCoords(dst, tex, uv);
        }
    }
}