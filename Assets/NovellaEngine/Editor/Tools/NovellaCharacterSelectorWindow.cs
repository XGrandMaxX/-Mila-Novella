using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Окно выбора персонажа — переписано в Hub-стиль (тёмная палитра,
    /// карточки BgRaised с акцентным ободком на hover, поиск, режимы
    /// «список/сетка», избранное вверху).
    ///
    /// Открывается модально через ShowUtility() — пользователь обязан
    /// либо выбрать персонажа, либо очистить выбор.
    /// </summary>
    public class NovellaCharacterSelectorWindow : EditorWindow
    {
        private Action<NovellaCharacter> _onSelect;
        private List<NovellaCharacter> _characters;
        // Опциональный фильтр исключения — персонажи с этими InstanceID не
        // будут показаны в окне (используется для «Replace/Add character»
        // в раскадровке: тех кто уже в массовке скрываем).
        private HashSet<int> _excludeIds;
        private Vector2 _scroll;
        private bool _isGridView;
        private string _search = "";
        // Hover-индекс — рисуем accent-ободок на текущем элементе. Сбрасываем
        // при первом OnGUI/Layout чтобы не залипал, обновляем в Repaint.
        private int _hoverIndex = -1;
        // Уникальные id для каждого видимого элемента (favorites + others) —
        // нужен в Repaint для определения hover'а до клика.
        private int _nextRowId;

        private const float ROW_HEIGHT = 40f;
        private const float CARD_SIZE  = 110f;

        // ─── Палитра — динамическая, из Settings (как и весь Hub). ───
        private static Color BgPrimary => NovellaSettingsModule.GetInterfaceColor();
        private static Color BgSide    => NovellaSettingsModule.GetBgSideColor();
        private static Color BgRaised  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color Border    => NovellaSettingsModule.GetBorderColor();
        private static Color Accent    => NovellaSettingsModule.GetAccentColor();
        private static Color Text1     => NovellaSettingsModule.GetTextColor();
        private static Color Text2     => NovellaSettingsModule.GetTextSecondary();
        private static Color Text3     => NovellaSettingsModule.GetTextMuted();
        private static Color Text4     => NovellaSettingsModule.GetTextDisabled();
        private static readonly Color FAVORITE_GOLD = new Color(1f, 0.78f, 0.20f);

        // ─────────────── Public API ───────────────

        public static void ShowWindow(Action<NovellaCharacter> onSelect)
            => ShowWindow(onSelect, null);

        // Перегрузка с exclude-фильтром: персонажи из excludeIds в списке
        // НЕ показываются. Если все исключены — будет видна empty-state.
        public static void ShowWindow(Action<NovellaCharacter> onSelect,
            IEnumerable<NovellaCharacter> excludeCharacters)
        {
            var window = GetWindow<NovellaCharacterSelectorWindow>(true,
                ToolLang.Get("Select Character", "Выберите персонажа"), true);
            window.titleContent = new GUIContent(
                ToolLang.Get("Select Character", "Выберите персонажа"));
            window._onSelect = onSelect;
            window._excludeIds = null;
            if (excludeCharacters != null)
            {
                window._excludeIds = new HashSet<int>();
                foreach (var c in excludeCharacters)
                    if (c != null) window._excludeIds.Add(c.GetInstanceID());
            }
            window.minSize = new Vector2(360, 500);
            window.LoadCharacters();
            // wantsMouseMove нужен чтобы hover'ы перерисовывались плавно.
            window.wantsMouseMove = true;
            window.ShowUtility();
        }

        // ─────────────── Загрузка ───────────────

        private void LoadCharacters()
        {
            _characters = new List<NovellaCharacter>();
            string[] guids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ch = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(path);
                if (ch == null) continue;
                if (_excludeIds != null && _excludeIds.Contains(ch.GetInstanceID()))
                    continue; // фильтр-исключение
                _characters.Add(ch);
            }
            _characters = _characters.OrderBy(c => c.name).ToList();
        }

        // ─────────────── OnGUI ───────────────

        private void OnGUI()
        {
            // Сброс hover-id перед раскладкой; в Repaint hover назначается
            // строкой/карточкой когда мышь в её Rect.
            if (Event.current.type == EventType.Layout)
            {
                _hoverIndex = -1;
                _nextRowId  = 0;
            }

            // Фон окна.
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BgPrimary);

            DrawHeader();
            DrawSearchBar();
            DrawList();

            // Постоянная перерисовка для hover-эффекта (только когда мышь над окном).
            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        // ─────────────── Header (top bar) ───────────────

        private void DrawHeader()
        {
            const float HEADER_H = 56f;
            Rect headerR = new Rect(0, 0, position.width, HEADER_H);
            EditorGUI.DrawRect(headerR, BgSide);
            // Нижняя 1px-разделительная линия.
            EditorGUI.DrawRect(new Rect(0, HEADER_H - 1, position.width, 1), Border);

            // Title + counter.
            var titleSt = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14, normal = { textColor = Text1 }
            };
            GUI.Label(new Rect(14, 8, position.width - 28, 22),
                ToolLang.Get("Select Character", "Выбор персонажа"), titleSt);

            int total = _characters?.Count ?? 0;
            int favs  = _characters?.Count(c => c.IsFavorite) ?? 0;
            string counter = total == 0
                ? ToolLang.Get("No characters yet", "Нет персонажей")
                : favs > 0
                    ? string.Format(ToolLang.Get("{0} characters · {1} favorite",
                                                 "{0} персонажей · {1} в избранном"), total, favs)
                    : string.Format(ToolLang.Get("{0} characters", "{0} персонажей"), total);

            var counterSt = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Text3 }, fontSize = 11
            };
            GUI.Label(new Rect(14, 30, position.width - 28, 18), counter, counterSt);
        }

        // ─────────────── Search + actions ───────────────

        private void DrawSearchBar()
        {
            const float Y     = 64f;
            const float H     = 30f;
            const float PAD   = 12f;
            const float CLEAR_W = 110f;
            const float TOG_W = 36f;
            const float GAP   = 6f;

            float searchW = position.width - PAD * 2 - CLEAR_W - TOG_W - GAP * 2;

            // Search field.
            Rect searchR = new Rect(PAD, Y, searchW, H);
            DrawSearchField(searchR);

            // Clear-button.
            Rect clearR = new Rect(PAD + searchW + GAP, Y, CLEAR_W, H);
            DrawSlimButton(clearR, ToolLang.Get("✕ Clear", "✕ Убрать"), () =>
            {
                _onSelect?.Invoke(null);
                Close();
            });

            // View-toggle (List ↔ Grid).
            Rect togR = new Rect(PAD + searchW + GAP + CLEAR_W + GAP, Y, TOG_W, H);
            DrawSlimButton(togR, _isGridView ? "≡" : "▦", () => _isGridView = !_isGridView);
        }

        private void DrawSearchField(Rect r)
        {
            // Тёмное поле с 1px-обводкой, акцент на фокусе.
            EditorGUI.DrawRect(r, BgRaised);
            DrawBorder(r, Border);

            // 🔍 icon
            var iconSt = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13, normal = { textColor = Text3 },
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(r.x + 4, r.y, 22, r.height), "🔍", iconSt);

            // TextField без рамки.
            var fieldSt = new GUIStyle(GUI.skin.textField)
            {
                normal      = { background = null, textColor = Text1 },
                focused     = { background = null, textColor = Text1 },
                hover       = { background = null, textColor = Text1 },
                active      = { background = null, textColor = Text1 },
                fontSize    = 12,
                alignment   = TextAnchor.MiddleLeft,
                border      = new RectOffset(0, 0, 0, 0),
                padding     = new RectOffset(0, 4, 0, 0),
                margin      = new RectOffset(0, 0, 0, 0)
            };
            Rect inner = new Rect(r.x + 26, r.y, r.width - 30, r.height);
            string before = _search;
            string after = GUI.TextField(inner, before ?? "", fieldSt);
            if (after != before) { _search = after; Repaint(); }

            // Placeholder.
            if (string.IsNullOrEmpty(_search))
            {
                var ph = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 12, normal = { textColor = Text4 },
                    alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Italic
                };
                GUI.Label(new Rect(inner.x + 2, inner.y, inner.width, inner.height),
                    ToolLang.Get("Search by name…", "Поиск по имени…"), ph);
            }
        }

        // ─────────────── Список / Сетка ───────────────

        private void DrawList()
        {
            const float TOP = 64f + 30f + 12f; // header + search + gap
            Rect contentR = new Rect(0, TOP, position.width, position.height - TOP);

            if (_characters == null || _characters.Count == 0)
            {
                DrawEmptyState(contentR);
                return;
            }

            string q = (_search ?? "").Trim().ToLowerInvariant();
            bool Matches(NovellaCharacter c)
                => string.IsNullOrEmpty(q) || (c.name ?? "").ToLowerInvariant().Contains(q);

            var favorites = _characters.Where(c => c.IsFavorite && Matches(c)).ToList();
            var others    = _characters.Where(c => !c.IsFavorite && Matches(c)).ToList();

            if (favorites.Count == 0 && others.Count == 0)
            {
                DrawNoMatchesState(contentR);
                return;
            }

            GUILayout.BeginArea(contentR);
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            if (favorites.Count > 0)
            {
                DrawSectionHeader("⭐", ToolLang.Get("Favorites", "Избранное"), favorites.Count, FAVORITE_GOLD);
                if (_isGridView) DrawGrid(favorites); else DrawRows(favorites);
                GUILayout.Space(14);
            }

            if (others.Count > 0)
            {
                DrawSectionHeader("👥", ToolLang.Get("All characters", "Все персонажи"), others.Count, Text2);
                if (_isGridView) DrawGrid(others); else DrawRows(others);
            }

            GUILayout.Space(10);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEmptyState(Rect r)
        {
            var st = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Text3 },
                fontSize = 12, wordWrap = true
            };
            GUI.Label(new Rect(r.x + 30, r.y + 80, r.width - 60, 60),
                ToolLang.Get(
                    "No characters in project yet.\nOpen Character Editor to create one.",
                    "В проекте пока нет персонажей.\nОткрой Редактор персонажей чтобы создать."),
                st);
        }

        private void DrawNoMatchesState(Rect r)
        {
            var st = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Text3 },
                fontSize = 12, fontStyle = FontStyle.Italic
            };
            GUI.Label(new Rect(r.x + 30, r.y + 60, r.width - 60, 30),
                string.Format(
                    ToolLang.Get("No matches for «{0}»", "Нет совпадений по «{0}»"), _search),
                st);
        }

        private void DrawSectionHeader(string icon, string title, int count, Color iconCol)
        {
            GUILayout.Space(6);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(22));
            r.x += 12; r.width -= 24;

            var icSt = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13, normal = { textColor = iconCol }
            };
            GUI.Label(new Rect(r.x, r.y, 18, 22), icon, icSt);

            var titleSt = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11, normal = { textColor = Text2 }
            };
            // UPPERCASE feel — title как написал пользователь.
            GUI.Label(new Rect(r.x + 22, r.y + 2, r.width - 22, 18), title.ToUpperInvariant(), titleSt);

            // Counter pill справа.
            string countStr = count.ToString();
            var pillSt = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Text3 }
            };
            float pillW = pillSt.CalcSize(new GUIContent(countStr)).x + 14;
            Rect pill = new Rect(r.x + r.width - pillW, r.y + 3, pillW, 16);
            EditorGUI.DrawRect(pill, BgRaised);
            DrawBorder(pill, Border);
            GUI.Label(pill, countStr, pillSt);

            // Тонкая линия снизу.
            GUILayout.Space(2);
            Rect line = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(1));
            line.x += 12; line.width -= 24;
            EditorGUI.DrawRect(line, new Color(Border.r, Border.g, Border.b, 0.55f));
            GUILayout.Space(6);
        }

        // ─────────────── Rows (list mode) ───────────────

        private void DrawRows(List<NovellaCharacter> list)
        {
            foreach (var ch in list)
            {
                int id = _nextRowId++;
                Rect rowR = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.Height(ROW_HEIGHT + 6));
                rowR.x += 12; rowR.width -= 24;
                Rect cardR = new Rect(rowR.x, rowR.y + 3, rowR.width, ROW_HEIGHT);

                bool hover = cardR.Contains(Event.current.mousePosition);
                if (hover && Event.current.type == EventType.Repaint) _hoverIndex = id;

                // Card bg + border (accent on hover).
                EditorGUI.DrawRect(cardR, BgRaised);
                Color rim = hover
                    ? new Color(Accent.r, Accent.g, Accent.b, 0.85f)
                    : Border;
                DrawBorder(cardR, rim);

                // Color-dot (ThemeColor).
                Rect dotR = new Rect(cardR.x + 10, cardR.y + (cardR.height - 14) * 0.5f, 14, 14);
                Color themeCol = ch.ThemeColor.a > 0.05f ? ch.ThemeColor : Text4;
                EditorGUI.DrawRect(dotR, themeCol);
                DrawBorder(dotR, new Color(themeCol.r, themeCol.g, themeCol.b, 0.85f));

                // Name (bold) + filename (mini).
                string display = string.IsNullOrEmpty(ch.DisplayName_EN)
                    ? ch.name
                    : (ToolLang.IsRU ? ch.DisplayName_RU : ch.DisplayName_EN);
                if (string.IsNullOrEmpty(display)) display = ch.name;

                var nameSt = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12, normal = { textColor = Text1 }
                };
                GUI.Label(new Rect(dotR.xMax + 10, cardR.y + 4, cardR.width - 90, 18), display, nameSt);

                // sub-line: filename (если отличается от display) или DisplayName_EN/RU «другой».
                string sub = ch.name == display ? "" : ch.name;
                if (!string.IsNullOrEmpty(sub))
                {
                    var subSt = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 10, normal = { textColor = Text3 }
                    };
                    GUI.Label(new Rect(dotR.xMax + 10, cardR.y + 21, cardR.width - 90, 14), sub, subSt);
                }

                // Star button — toggles favorite (right side).
                Rect starR = new Rect(cardR.xMax - 36, cardR.y + (cardR.height - 26) * 0.5f, 26, 26);
                DrawStarButton(starR, ch);

                // Card click → select.
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                    && cardR.Contains(Event.current.mousePosition)
                    && !starR.Contains(Event.current.mousePosition))
                {
                    _onSelect?.Invoke(ch);
                    Close();
                    Event.current.Use();
                    return;
                }
            }
        }

        // ─────────────── Grid (cards) ───────────────

        private void DrawGrid(List<NovellaCharacter> list)
        {
            const float CARD_GAP = 8f;
            float availW = position.width - 24;
            int cols = Mathf.Max(1, (int)((availW + CARD_GAP) / (CARD_SIZE + CARD_GAP)));
            int rows = Mathf.CeilToInt(list.Count / (float)cols);

            for (int row = 0; row < rows; row++)
            {
                Rect rowR = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.Height(CARD_SIZE + 6));
                rowR.x += 12; rowR.width -= 24;

                for (int col = 0; col < cols; col++)
                {
                    int idx = row * cols + col;
                    if (idx >= list.Count) break;
                    var ch = list[idx];

                    int id = _nextRowId++;
                    Rect card = new Rect(
                        rowR.x + col * (CARD_SIZE + CARD_GAP),
                        rowR.y, CARD_SIZE, CARD_SIZE);

                    bool hover = card.Contains(Event.current.mousePosition);
                    if (hover && Event.current.type == EventType.Repaint) _hoverIndex = id;

                    EditorGUI.DrawRect(card, BgRaised);
                    DrawBorder(card, hover
                        ? new Color(Accent.r, Accent.g, Accent.b, 0.85f)
                        : Border);

                    // Sprite preview (top portion of card).
                    Rect previewR = new Rect(card.x + 1, card.y + 1, card.width - 2, card.height - 28);
                    Color themeCol = ch.ThemeColor.a > 0.05f ? ch.ThemeColor : Text4;
                    EditorGUI.DrawRect(previewR, new Color(themeCol.r * 0.30f, themeCol.g * 0.30f, themeCol.b * 0.30f, 1f));

                    if (ch.DefaultSprite != null && ch.DefaultSprite.texture != null)
                    {
                        // Маленькое превью: вписать в previewR keeping aspect.
                        var tex = AssetPreview.GetAssetPreview(ch.DefaultSprite);
                        if (tex == null) tex = ch.DefaultSprite.texture;
                        Rect fit = FitRect(previewR, new Vector2(tex.width, tex.height), 0.92f);
                        GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit, true);
                    }
                    else
                    {
                        // Заглушка: акцентная инициал-буква.
                        string initial = !string.IsNullOrEmpty(ch.name) ? ch.name.Substring(0, 1).ToUpperInvariant() : "?";
                        var initSt = new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = 28,
                            alignment = TextAnchor.MiddleCenter,
                            normal = { textColor = new Color(Text1.r, Text1.g, Text1.b, 0.85f) }
                        };
                        GUI.Label(previewR, initial, initSt);
                    }

                    // Name footer.
                    Rect footR = new Rect(card.x + 4, card.yMax - 24, card.width - 8, 20);
                    var nameSt = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 11, alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Text1 }, wordWrap = false, clipping = TextClipping.Clip
                    };
                    GUI.Label(footR, ch.name ?? "", nameSt);

                    // Star top-right corner.
                    Rect starR = new Rect(card.xMax - 24, card.y + 4, 20, 20);
                    DrawStarButton(starR, ch, mini: true);

                    // Card click.
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                        && card.Contains(Event.current.mousePosition)
                        && !starR.Contains(Event.current.mousePosition))
                    {
                        _onSelect?.Invoke(ch);
                        Close();
                        Event.current.Use();
                        return;
                    }
                }
            }
        }

        // ─────────────── Common controls ───────────────

        private void DrawSlimButton(Rect r, string label, Action onClick)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = hover
                ? Color.Lerp(BgRaised, Accent, 0.14f)
                : BgRaised;
            EditorGUI.DrawRect(r, bg);
            DrawBorder(r, hover ? new Color(Accent.r, Accent.g, Accent.b, 0.55f) : Border);

            var st = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = hover ? Accent : Text1 }
            };
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && r.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                Event.current.Use();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawStarButton(Rect r, NovellaCharacter ch, bool mini = false)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            // Mini в grid-режиме — без явного фона, только цветная иконка.
            if (!mini)
            {
                if (hover) EditorGUI.DrawRect(r, new Color(BgPrimary.r, BgPrimary.g, BgPrimary.b, 0.55f));
            }

            Color starCol = ch.IsFavorite ? FAVORITE_GOLD : (hover ? Text2 : Text4);
            var st = new GUIStyle(EditorStyles.label)
            {
                fontSize = mini ? 14 : 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = starCol }
            };
            GUI.Label(r, ch.IsFavorite ? "★" : "☆", st);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && r.Contains(Event.current.mousePosition))
            {
                Undo.RecordObject(ch, ch.IsFavorite ? "Unfavorite character" : "Favorite character");
                ch.IsFavorite = !ch.IsFavorite;
                EditorUtility.SetDirty(ch);
                Event.current.Use();
                Repaint();
            }
        }

        // ─────────────── Helpers ───────────────

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        // Вписывает контент с заданным аспектом в rect, оставляя padding.
        private static Rect FitRect(Rect bounds, Vector2 contentSize, float fillFactor = 1f)
        {
            float bw = bounds.width * fillFactor;
            float bh = bounds.height * fillFactor;
            float aspect = contentSize.x / Mathf.Max(0.001f, contentSize.y);
            float w, h;
            if (bw / bh > aspect) { h = bh; w = h * aspect; }
            else                   { w = bw; h = w / aspect; }
            return new Rect(bounds.x + (bounds.width - w) * 0.5f,
                            bounds.y + (bounds.height - h) * 0.5f, w, h);
        }
    }
}
