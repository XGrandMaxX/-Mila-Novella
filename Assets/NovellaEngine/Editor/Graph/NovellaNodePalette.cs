// ════════════════════════════════════════════════════════════════════════════
// NovellaNodePalette — нижняя панель графа со списком всех типов нод,
// которые можно создать. Решает UX-проблему: ПКМ-меню новички не находят,
// а Drag&Drop из палитры — интуитивный способ.
//
// Block 3A: только UI палитры (категории + поиск + draggable cards).
// Block 3B (следующий шаг): интеграция Drop в граф (на пустоту / на ноду /
// на edge), popup для выбора output-порта.
//
// Структура:
//   Header (38h): tab'ы категорий (📖 Story / 🔀 Logic / ...) + поиск
//   Body (88h):   горизонтальный scroll с item-карточками 100×72 каждая
//
// Каждый item:
//   - Иконка (emoji) сверху
//   - Имя ноды (две строки если длинное)
//   - Курсор: grab
//   - Hover: акцентная рамка
// ════════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public class NovellaNodePalette : VisualElement
    {
        // Идентификатор drag-payload'а — Block 3B читает его через
        // DragAndDrop.GetGenericData(KEY) при drop'е на canvas графа.
        public const string DRAG_DATA_KEY = "NovellaNodePalette.NodeKind";

        private readonly NovellaGraphView _graphView;
        private VisualElement _tabsRow;
        private TextField _searchField;
        private VisualElement _searchPlaceholder;
        private VisualElement _searchWrap;
        private ScrollView _itemsScroll;
        private Button _viewModeBtn;
        private string _activeCategory = "all";
        private string _searchQuery = "";

        // Block 3A.1: режим отображения — compact (квадратики) или detailed
        // (длинные плашки с описанием). Сохраняется в EditorPrefs.
        private enum ViewMode { Compact, Detailed }
        private ViewMode _viewMode = ViewMode.Compact;
        private const string PREF_VIEW_MODE = "NovellaGraph_PaletteViewMode";

        // ─── Определения категорий и нод (один источник правды) ───
        // Используется и для tabs, и для рендера item-карточек.
        private struct ItemDef
        {
            public string Category;        // "story" / "logic" / "cinema" / "system"
            public ENodeType NodeType;
            public string Icon;            // emoji
            public string LabelEN;
            public string LabelRU;
            public string TooltipEN;
            public string TooltipRU;
            // Если задан Type — это DLC-нода (создаётся через CreateDLCNode).
            public System.Type DlcType;
        }

        private static readonly (string id, string emoji, string en, string ru)[] CategoryDefs =
        {
            ("all",    "✦",   "All",        "Все"),
            ("story",  "📖",  "Story",      "Сюжет"),
            ("logic",  "🔀",  "Logic",      "Логика"),
            ("cinema", "🎬",  "Cinema",     "Режиссура"),
            ("system", "⚙",   "System",     "Система"),
            ("dlc",    "🧩",  "DLC",        "DLC"),
        };

        private static List<ItemDef> BuildItemDefs()
        {
            var list = new List<ItemDef>
            {
                // ─── Story ───
                new ItemDef { Category="story", NodeType=ENodeType.Dialogue, Icon="💬",
                    LabelEN="Dialogue", LabelRU="Диалог",
                    TooltipEN="Dialogue with one or more speakers and lines.",
                    TooltipRU="Диалог с одним или несколькими спикерами и репликами." },
                new ItemDef { Category="story", NodeType=ENodeType.Note, Icon="📝",
                    LabelEN="Note", LabelRU="Заметка",
                    TooltipEN="Sticky note for comments. Doesn't affect the game.",
                    TooltipRU="Стикер с заметкой. На игру не влияет." },
                // ─── Logic ───
                new ItemDef { Category="logic", NodeType=ENodeType.Branch, Icon="🔀",
                    LabelEN="Branch", LabelRU="Развилка",
                    TooltipEN="Player choice — multiple options to pick.",
                    TooltipRU="Выбор игрока — несколько вариантов." },
                new ItemDef { Category="logic", NodeType=ENodeType.Condition, Icon="❓",
                    LabelEN="Condition", LabelRU="Условие",
                    TooltipEN="If/Else split based on a variable value.",
                    TooltipRU="Развилка If/Else по значению переменной." },
                new ItemDef { Category="logic", NodeType=ENodeType.Random, Icon="🎲",
                    LabelEN="Random", LabelRU="Случай",
                    TooltipEN="Engine picks one path by chance weights.",
                    TooltipRU="Движок выбирает путь по весам шансов." },
                new ItemDef { Category="logic", NodeType=ENodeType.Variable, Icon="📊",
                    LabelEN="Variable", LabelRU="Переменная",
                    TooltipEN="Set or update a variable's value.",
                    TooltipRU="Изменить значение переменной." },
                // ─── Cinema ───
                new ItemDef { Category="cinema", NodeType=ENodeType.SceneSettings, Icon="🖼",
                    LabelEN="Scene", LabelRU="Сцена",
                    TooltipEN="Background, characters placement, UI show/hide.",
                    TooltipRU="Фон, расстановка персонажей, показ/скрытие UI." },
                new ItemDef { Category="cinema", NodeType=ENodeType.Audio, Icon="🎵",
                    LabelEN="Audio", LabelRU="Аудио",
                    TooltipEN="Play / stop a music track or sound effect.",
                    TooltipRU="Воспроизвести / остановить трек или звук." },
                new ItemDef { Category="cinema", NodeType=ENodeType.Animation, Icon="✨",
                    LabelEN="Animation", LabelRU="Анимация",
                    TooltipEN="Camera shake, fades, character movement, scaling.",
                    TooltipRU="Тряска камеры, fade, движение персонажа, масштаб." },
                new ItemDef { Category="cinema", NodeType=ENodeType.Wait, Icon="⏳",
                    LabelEN="Wait", LabelRU="Пауза",
                    TooltipEN="Pause for N seconds or until player click.",
                    TooltipRU="Пауза на N секунд или до клика игрока." },
                // ─── System ───
                new ItemDef { Category="system", NodeType=ENodeType.EventBroadcast, Icon="⚡",
                    LabelEN="Event", LabelRU="Событие",
                    TooltipEN="Broadcast a custom event for code listeners.",
                    TooltipRU="Послать кастомное событие для подписчиков в коде." },
                new ItemDef { Category="system", NodeType=ENodeType.Save, Icon="💾",
                    LabelEN="Save", LabelRU="Сейв",
                    TooltipEN="Auto-save checkpoint at this point in story.",
                    TooltipRU="Чекпоинт автосохранения в этом месте истории." },
                new ItemDef { Category="system", NodeType=ENodeType.End, Icon="🛑",
                    LabelEN="End", LabelRU="Конец",
                    TooltipEN="End of story branch — to main menu / next chapter / quit.",
                    TooltipRU="Конец ветки — в гл. меню / след. глава / выход." },
            };

            // ─── DLC nodes (через reflection) ───
            var dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0);
            var settings = NovellaDLCSettings.GetOrCreateSettings();
            foreach (var t in dlcTypes)
            {
                if (settings != null && !settings.IsDLCEnabled(t.FullName)) continue;
                var attr = (NovellaDLCNodeAttribute)t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).First();
                list.Add(new ItemDef
                {
                    Category = "dlc",
                    NodeType = ENodeType.CustomDLC,
                    Icon = "🧩",
                    LabelEN = attr.MenuName,
                    LabelRU = attr.MenuName,
                    TooltipEN = attr.Description ?? "",
                    TooltipRU = attr.Description ?? "",
                    DlcType = t,
                });
            }
            return list;
        }

        private List<ItemDef> _items;

        public NovellaNodePalette(NovellaGraphView graphView)
        {
            _graphView = graphView;
            _items = BuildItemDefs();

            // Восстанавливаем режим отображения.
            int savedMode = EditorPrefs.GetInt(PREF_VIEW_MODE, 0);
            _viewMode = savedMode == 1 ? ViewMode.Detailed : ViewMode.Compact;

            name = "ns-node-palette";
            // Контейнер: высота меняется в зависимости от режима.
            // Compact = 132h, Detailed = 200h (помещается ~3 ряда плашек).
            style.flexDirection = FlexDirection.Column;
            style.height = _viewMode == ViewMode.Detailed ? 200 : 132;
            style.flexShrink = 0;
            style.backgroundColor = NovellaGraphTheme.BgSide;
            style.borderTopWidth = 1;
            style.borderTopColor = NovellaGraphTheme.Border;
            // Анимация изменения высоты — для smooth-toggle режимов.
            style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("height"),
            });
            style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.20f, TimeUnit.Second),
            });
            style.transitionTimingFunction = new StyleList<EasingFunction>(new List<EasingFunction> {
                new EasingFunction(EasingMode.EaseOutCubic),
            });
            style.overflow = Overflow.Hidden;

            BuildHeader();
            BuildBody();
            RebuildItems();
        }

        private void BuildHeader()
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = 38;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = NovellaGraphTheme.Border;
            Add(header);

            // ─── Title ───
            var titleLbl = new Label("🧰  " + ToolLang.Get("Nodes", "Ноды"));
            titleLbl.style.fontSize = 11;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = NovellaGraphTheme.Text2;
            titleLbl.style.marginRight = 12;
            titleLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(titleLbl);

            // ─── Category tabs ───
            _tabsRow = new VisualElement();
            _tabsRow.style.flexDirection = FlexDirection.Row;
            _tabsRow.style.flexGrow = 0;
            header.Add(_tabsRow);

            for (int i = 0; i < CategoryDefs.Length; i++)
            {
                var def = CategoryDefs[i];
                // DLC tab показываем только если есть включённые DLC ноды.
                if (def.id == "dlc" && !_items.Any(x => x.Category == "dlc")) continue;

                var tabBtn = new Button(() => {
                    _activeCategory = def.id;
                    RebuildItems();
                    RefreshTabStates();
                })
                {
                    text = def.emoji + "  " + (ToolLang.IsRU ? def.ru : def.en),
                    name = "ns-pal-tab-" + def.id,
                };
                NovellaGraphTheme.ApplySlimButton(tabBtn, height: 24, paddingX: 10);
                tabBtn.style.fontSize = 11;
                tabBtn.style.marginLeft = i == 0 ? 0 : 4;
                _tabsRow.Add(tabBtn);
            }

            // ─── Spacer ───
            var spacer = new VisualElement { style = { flexGrow = 1 } };
            header.Add(spacer);

            // ─── Search field в Hub-стиле ───
            // Unity TextField очень упрямо держится за свой default chrome.
            // Боремся: глубоко override'им child input + textInput, ставим
            // borderRadius/colors на wrap-container, плюс свой placeholder
            // (Label который скрывается при фокусе/вводе).
            _searchWrap = new VisualElement();
            _searchWrap.style.flexDirection = FlexDirection.Row;
            _searchWrap.style.alignItems = Align.Center;
            _searchWrap.style.width = 200;
            _searchWrap.style.height = 26;
            _searchWrap.style.borderTopWidth = 1;
            _searchWrap.style.borderBottomWidth = 1;
            _searchWrap.style.borderLeftWidth = 1;
            _searchWrap.style.borderRightWidth = 1;
            _searchWrap.style.borderTopColor = NovellaGraphTheme.Border;
            _searchWrap.style.borderBottomColor = NovellaGraphTheme.Border;
            _searchWrap.style.borderLeftColor = NovellaGraphTheme.Border;
            _searchWrap.style.borderRightColor = NovellaGraphTheme.Border;
            _searchWrap.style.borderTopLeftRadius = 4;
            _searchWrap.style.borderTopRightRadius = 4;
            _searchWrap.style.borderBottomLeftRadius = 4;
            _searchWrap.style.borderBottomRightRadius = 4;
            _searchWrap.style.paddingLeft = 8;
            _searchWrap.style.paddingRight = 6;
            _searchWrap.style.backgroundColor = NovellaGraphTheme.BgPrimary;
            // Анимация бордера на focus.
            _searchWrap.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("border-top-color"),
                new StylePropertyName("border-bottom-color"),
                new StylePropertyName("border-left-color"),
                new StylePropertyName("border-right-color"),
            });
            _searchWrap.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
            });
            header.Add(_searchWrap);

            var searchIcon = new Label("🔍");
            searchIcon.pickingMode = PickingMode.Ignore;
            searchIcon.style.color = NovellaGraphTheme.Text3;
            searchIcon.style.fontSize = 11;
            searchIcon.style.marginRight = 6;
            _searchWrap.Add(searchIcon);

            // TextField + хитрый layered placeholder.
            var inputWrap = new VisualElement();
            inputWrap.style.flexGrow = 1;
            inputWrap.style.height = 24;
            inputWrap.style.position = Position.Relative;
            _searchWrap.Add(inputWrap);

            _searchField = new TextField();
            _searchField.style.flexGrow = 1;
            _searchField.style.marginTop = 0;
            _searchField.style.marginBottom = 0;
            _searchField.style.marginLeft = 0;
            _searchField.style.marginRight = 0;
            _searchField.style.height = 24;

            // Унти-стайл TextField: убираем рамки/фон у root и input.
            _searchField.style.backgroundColor = new Color(0, 0, 0, 0);
            _searchField.style.borderTopWidth = 0;
            _searchField.style.borderBottomWidth = 0;
            _searchField.style.borderLeftWidth = 0;
            _searchField.style.borderRightWidth = 0;

            // Текстовый element внутри TextField.
            var input = _searchField.Q(className: "unity-text-field__input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(0, 0, 0, 0);
                input.style.borderTopWidth = 0;
                input.style.borderBottomWidth = 0;
                input.style.borderLeftWidth = 0;
                input.style.borderRightWidth = 0;
                input.style.color = NovellaGraphTheme.Text1;
                input.style.fontSize = 11;
                input.style.paddingLeft = 0;
                input.style.paddingRight = 0;
                input.style.paddingTop = 0;
                input.style.paddingBottom = 0;
                input.style.marginLeft = 0;
                input.style.marginRight = 0;
            }
            // У некоторых версий Unity внутри ещё один TextElement.
            var textEl = _searchField.Q<TextElement>();
            if (textEl != null)
            {
                textEl.style.backgroundColor = new Color(0, 0, 0, 0);
                textEl.style.color = NovellaGraphTheme.Text1;
                textEl.style.fontSize = 11;
            }
            inputWrap.Add(_searchField);

            // ─── Свой placeholder ───
            // Label поверх TextField, скрывается когда есть текст.
            _searchPlaceholder = new Label(ToolLang.Get("Search nodes…", "Поиск нод…"));
            _searchPlaceholder.pickingMode = PickingMode.Ignore;
            _searchPlaceholder.style.position = Position.Absolute;
            _searchPlaceholder.style.left = 1;
            _searchPlaceholder.style.top = 0;
            _searchPlaceholder.style.bottom = 0;
            _searchPlaceholder.style.color = NovellaGraphTheme.Text4;
            _searchPlaceholder.style.fontSize = 11;
            _searchPlaceholder.style.unityTextAlign = TextAnchor.MiddleLeft;
            inputWrap.Add(_searchPlaceholder);

            // Focus / blur — подсветка border'а wrap-контейнера.
            _searchField.RegisterCallback<FocusInEvent>(_ => {
                _searchWrap.style.borderTopColor = NovellaGraphTheme.Accent;
                _searchWrap.style.borderBottomColor = NovellaGraphTheme.Accent;
                _searchWrap.style.borderLeftColor = NovellaGraphTheme.Accent;
                _searchWrap.style.borderRightColor = NovellaGraphTheme.Accent;
            });
            _searchField.RegisterCallback<FocusOutEvent>(_ => {
                _searchWrap.style.borderTopColor = NovellaGraphTheme.Border;
                _searchWrap.style.borderBottomColor = NovellaGraphTheme.Border;
                _searchWrap.style.borderLeftColor = NovellaGraphTheme.Border;
                _searchWrap.style.borderRightColor = NovellaGraphTheme.Border;
            });

            _searchField.RegisterValueChangedCallback(evt => {
                _searchQuery = (evt.newValue ?? "").Trim().ToLowerInvariant();
                _searchPlaceholder.style.display = string.IsNullOrEmpty(evt.newValue)
                    ? DisplayStyle.Flex : DisplayStyle.None;
                RebuildItems();
            });

            // ─── View mode toggle (compact ▦ / detailed ≡) ───
            _viewModeBtn = new Button(() => {
                _viewMode = _viewMode == ViewMode.Compact ? ViewMode.Detailed : ViewMode.Compact;
                EditorPrefs.SetInt(PREF_VIEW_MODE, _viewMode == ViewMode.Detailed ? 1 : 0);
                style.height = _viewMode == ViewMode.Detailed ? 200 : 132;
                _viewModeBtn.text = _viewMode == ViewMode.Detailed ? "▦" : "≡";
                _viewModeBtn.tooltip = _viewMode == ViewMode.Detailed
                    ? ToolLang.Get("Switch to compact view", "Компактный вид")
                    : ToolLang.Get("Switch to detailed view", "Подробный вид");
                RebuildItems();
            });
            _viewModeBtn.text = _viewMode == ViewMode.Detailed ? "▦" : "≡";
            _viewModeBtn.tooltip = _viewMode == ViewMode.Detailed
                ? ToolLang.Get("Switch to compact view", "Компактный вид")
                : ToolLang.Get("Switch to detailed view", "Подробный вид");
            NovellaGraphTheme.ApplyIconButton(_viewModeBtn, 26);
            _viewModeBtn.style.marginLeft = 6;
            _viewModeBtn.style.fontSize = 14;
            _viewModeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _viewModeBtn.style.color = NovellaGraphTheme.Text2;
            header.Add(_viewModeBtn);
        }

        private void BuildBody()
        {
            // ScrollView создаём один раз; направление (horizontal/vertical)
            // и layout содержимого настраиваем при каждом RebuildItems().
            _itemsScroll = new ScrollView();
            _itemsScroll.style.flexGrow = 1;
            Add(_itemsScroll);
        }

        private void ApplyScrollMode()
        {
            // Compact: horizontal scroll, row-flex.
            // Detailed: vertical scroll, wrap-row (3 ряда плашек).
            if (_viewMode == ViewMode.Compact)
            {
                _itemsScroll.mode = ScrollViewMode.Horizontal;
                _itemsScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
                _itemsScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                _itemsScroll.contentContainer.style.flexDirection = FlexDirection.Row;
                _itemsScroll.contentContainer.style.flexWrap = Wrap.NoWrap;
                _itemsScroll.contentContainer.style.alignItems = Align.Center;
            }
            else
            {
                _itemsScroll.mode = ScrollViewMode.Vertical;
                _itemsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _itemsScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
                _itemsScroll.contentContainer.style.flexDirection = FlexDirection.Row;
                _itemsScroll.contentContainer.style.flexWrap = Wrap.Wrap;
                _itemsScroll.contentContainer.style.alignItems = Align.FlexStart;
            }
            _itemsScroll.contentContainer.style.paddingLeft = 10;
            _itemsScroll.contentContainer.style.paddingRight = 10;
            _itemsScroll.contentContainer.style.paddingTop = 8;
            _itemsScroll.contentContainer.style.paddingBottom = 8;
        }

        private void RebuildItems()
        {
            ApplyScrollMode();
            _itemsScroll.contentContainer.Clear();

            var filtered = _items.Where(it => {
                if (_activeCategory != "all" && it.Category != _activeCategory) return false;
                if (string.IsNullOrEmpty(_searchQuery)) return true;
                string lbl = (ToolLang.IsRU ? it.LabelRU : it.LabelEN).ToLowerInvariant();
                string desc = (ToolLang.IsRU ? it.TooltipRU : it.TooltipEN).ToLowerInvariant();
                return lbl.Contains(_searchQuery) || desc.Contains(_searchQuery);
            }).ToList();

            if (filtered.Count == 0)
            {
                var empty = new Label(_searchQuery.Length > 0
                    ? ToolLang.Get("No matches.", "Ничего не найдено.")
                    : ToolLang.Get("No nodes in this category.", "В этой категории пусто."));
                empty.style.color = NovellaGraphTheme.Text4;
                empty.style.fontSize = 11;
                empty.style.unityFontStyleAndWeight = FontStyle.Italic;
                empty.style.marginLeft = 12;
                empty.style.marginTop = 16;
                _itemsScroll.Add(empty);
            }
            else
            {
                foreach (var it in filtered)
                {
                    _itemsScroll.contentContainer.Add(_viewMode == ViewMode.Compact
                        ? BuildItemCompactCard(it)
                        : BuildItemDetailedCard(it));
                }
            }
            RefreshTabStates();
        }

        // Compact card: 110×80 квадратик с 3px accent strip слева,
        // crew иконкой + name. Дизайн зеркалит ноды графа из Block 2A.
        private VisualElement BuildItemCompactCard(ItemDef def)
        {
            string labelText = ToolLang.IsRU ? def.LabelRU : def.LabelEN;
            string tipText   = ToolLang.IsRU ? def.TooltipRU : def.TooltipEN;
            Color accentC = GetItemAccentColor(def);

            var card = MakeCardBase(def, tipText);
            card.style.width = 110;
            card.style.height = 80;
            card.style.marginRight = 8;
            card.style.flexDirection = FlexDirection.Row;

            // Accent-strip слева — точно как у нод в графе.
            var strip = new VisualElement();
            strip.pickingMode = PickingMode.Ignore;
            strip.style.width = 3;
            strip.style.backgroundColor = accentC;
            strip.style.borderTopLeftRadius = 6;
            strip.style.borderBottomLeftRadius = 6;
            card.Add(strip);

            var content = new VisualElement();
            content.pickingMode = PickingMode.Ignore;
            content.style.flexGrow = 1;
            content.style.flexDirection = FlexDirection.Column;
            content.style.alignItems = Align.Center;
            content.style.justifyContent = Justify.Center;
            content.style.paddingTop = 2;
            content.style.paddingBottom = 2;
            card.Add(content);

            var iconLbl = new Label(def.Icon);
            iconLbl.pickingMode = PickingMode.Ignore;
            iconLbl.style.fontSize = 24;
            iconLbl.style.color = accentC;
            iconLbl.style.marginBottom = 2;
            content.Add(iconLbl);

            var nameLbl = new Label(labelText);
            nameLbl.pickingMode = PickingMode.Ignore;
            nameLbl.name = "name";
            nameLbl.style.fontSize = 10;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color = NovellaGraphTheme.Text2;
            nameLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
            content.Add(nameLbl);

            AttachCardInteractions(card, def, accentC);
            return card;
        }

        // Detailed card: широкая плашка 280×56 с accent-strip + иконка +
        // имя жирным + описание двумя строками.
        private VisualElement BuildItemDetailedCard(ItemDef def)
        {
            string labelText = ToolLang.IsRU ? def.LabelRU : def.LabelEN;
            string tipText   = ToolLang.IsRU ? def.TooltipRU : def.TooltipEN;
            Color accentC = GetItemAccentColor(def);

            var card = MakeCardBase(def, tipText);
            card.style.width = 280;
            card.style.height = 60;
            card.style.marginRight = 8;
            card.style.marginBottom = 6;
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Stretch;

            // Accent-strip слева.
            var strip = new VisualElement();
            strip.pickingMode = PickingMode.Ignore;
            strip.style.width = 3;
            strip.style.backgroundColor = accentC;
            strip.style.borderTopLeftRadius = 6;
            strip.style.borderBottomLeftRadius = 6;
            card.Add(strip);

            // Иконка большая по центру слева.
            var iconBox = new VisualElement();
            iconBox.pickingMode = PickingMode.Ignore;
            iconBox.style.width = 44;
            iconBox.style.alignItems = Align.Center;
            iconBox.style.justifyContent = Justify.Center;
            card.Add(iconBox);

            var iconLbl = new Label(def.Icon);
            iconLbl.pickingMode = PickingMode.Ignore;
            iconLbl.style.fontSize = 22;
            iconLbl.style.color = accentC;
            iconBox.Add(iconLbl);

            // Текст: title + description.
            var textCol = new VisualElement();
            textCol.pickingMode = PickingMode.Ignore;
            textCol.style.flexGrow = 1;
            textCol.style.flexDirection = FlexDirection.Column;
            textCol.style.justifyContent = Justify.Center;
            textCol.style.paddingTop = 6;
            textCol.style.paddingBottom = 6;
            textCol.style.paddingRight = 8;
            card.Add(textCol);

            var nameLbl = new Label(labelText);
            nameLbl.pickingMode = PickingMode.Ignore;
            nameLbl.name = "name";
            nameLbl.style.fontSize = 12;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.color = NovellaGraphTheme.Text1;
            nameLbl.style.marginBottom = 1;
            textCol.Add(nameLbl);

            var descLbl = new Label(tipText);
            descLbl.pickingMode = PickingMode.Ignore;
            descLbl.style.fontSize = 9;
            descLbl.style.color = NovellaGraphTheme.Text3;
            descLbl.style.whiteSpace = WhiteSpace.Normal;
            textCol.Add(descLbl);

            AttachCardInteractions(card, def, accentC);
            return card;
        }

        // ─── Общая база карточки (фон, рамка, скругление) ───
        private static VisualElement MakeCardBase(ItemDef def, string tooltip)
        {
            var card = new VisualElement();
            card.name = "ns-pal-item";
            card.userData = def;
            card.tooltip = tooltip;
            card.style.flexShrink = 0;
            card.style.backgroundColor = NovellaGraphTheme.BgRaised;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = NovellaGraphTheme.Border;
            card.style.borderBottomColor = NovellaGraphTheme.Border;
            card.style.borderLeftColor = NovellaGraphTheme.Border;
            card.style.borderRightColor = NovellaGraphTheme.Border;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.overflow = Overflow.Hidden;
            // Hover/focus border анимируется (12ms).
            card.style.transitionProperty = new StyleList<StylePropertyName>(new List<StylePropertyName> {
                new StylePropertyName("border-top-color"),
                new StylePropertyName("border-bottom-color"),
                new StylePropertyName("border-left-color"),
                new StylePropertyName("border-right-color"),
            });
            card.style.transitionDuration = new StyleList<TimeValue>(new List<TimeValue> {
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
                new TimeValue(0.12f, TimeUnit.Second),
            });
            return card;
        }

        // Hover / drag — общая логика для compact и detailed.
        private static void AttachCardInteractions(VisualElement card, ItemDef def, Color accentC)
        {
            var nameLbl = card.Q<Label>("name");
            card.RegisterCallback<MouseEnterEvent>(_ => {
                card.style.borderTopColor = accentC;
                card.style.borderBottomColor = accentC;
                card.style.borderLeftColor = accentC;
                card.style.borderRightColor = accentC;
                if (nameLbl != null) nameLbl.style.color = NovellaGraphTheme.Text1;
            });
            card.RegisterCallback<MouseLeaveEvent>(_ => {
                card.style.borderTopColor = NovellaGraphTheme.Border;
                card.style.borderBottomColor = NovellaGraphTheme.Border;
                card.style.borderLeftColor = NovellaGraphTheme.Border;
                card.style.borderRightColor = NovellaGraphTheme.Border;
                if (nameLbl != null) nameLbl.style.color = NovellaGraphTheme.Text2;
            });

            card.RegisterCallback<MouseDownEvent>(evt => {
                if (evt.button != 0) return;
                var capturedDef = def;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(DRAG_DATA_KEY, new PaletteDragPayload {
                    NodeType = capturedDef.NodeType,
                    DlcType  = capturedDef.DlcType,
                    Label    = ToolLang.IsRU ? capturedDef.LabelRU : capturedDef.LabelEN,
                });
                DragAndDrop.objectReferences = new Object[0];
                DragAndDrop.StartDrag(ToolLang.IsRU ? capturedDef.LabelRU : capturedDef.LabelEN);
                evt.StopPropagation();
            });
        }

        private static Color GetItemAccentColor(ItemDef def)
        {
            if (def.DlcType != null) return NovellaColorSettingsWindow.GetDLCNodeColor(def.DlcType.FullName);
            return NovellaColorSettingsWindow.GetNodeColor(def.NodeType);
        }

        // Плавное сворачивание/раскрытие палитры через анимацию height.
        // display:None не анимируется, height — да; на полном свернутом
        // height=0 палитра визуально исчезает но остаётся в дереве.
        public void SetCollapsed(bool collapsed)
        {
            int target = collapsed ? 0 : (_viewMode == ViewMode.Detailed ? 200 : 132);
            style.height = target;
        }

        private void RefreshTabStates()
        {
            // Подкрашиваем активный tab — accent-fill, остальные slim.
            for (int i = 0; i < CategoryDefs.Length; i++)
            {
                var def = CategoryDefs[i];
                var btn = _tabsRow.Q<Button>("ns-pal-tab-" + def.id);
                if (btn == null) continue;
                bool active = def.id == _activeCategory;
                if (active)
                {
                    btn.style.backgroundColor = new Color(NovellaGraphTheme.Accent.r, NovellaGraphTheme.Accent.g, NovellaGraphTheme.Accent.b, 0.22f);
                    btn.style.color = NovellaGraphTheme.Text1;
                    btn.style.borderTopColor = NovellaGraphTheme.Accent;
                    btn.style.borderBottomColor = NovellaGraphTheme.Accent;
                    btn.style.borderLeftColor = NovellaGraphTheme.Accent;
                    btn.style.borderRightColor = NovellaGraphTheme.Accent;
                }
                else
                {
                    btn.style.backgroundColor = new Color(0, 0, 0, 0);
                    btn.style.color = NovellaGraphTheme.Text2;
                    btn.style.borderTopColor = NovellaGraphTheme.Border;
                    btn.style.borderBottomColor = NovellaGraphTheme.Border;
                    btn.style.borderLeftColor = NovellaGraphTheme.Border;
                    btn.style.borderRightColor = NovellaGraphTheme.Border;
                }
            }
        }

        // Payload для DragAndDrop: тип + опциональный DlcType + label
        // (для drag-preview labels). Block 3B читает это в OnDragPerform.
        public class PaletteDragPayload
        {
            public ENodeType NodeType;
            public System.Type DlcType;
            public string Label;
        }
    }
}
