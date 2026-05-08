using System;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Ctrl+F command palette для Novella Studio. Один поиск по всему
    /// проекту: персонажи, переменные, истории, действия. Fuzzy-match по подстроке.
    /// (Ctrl+K не используем — в Unity 2021+ он зарезервирован за встроенным
    /// Search Provider и перехватывается до UI Toolkit.)
    ///
    /// Layout: search-row (🔍 + input + placeholder) → list (с секционными
    /// заголовками когда нет фильтра) → footer (хоткеи).
    /// </summary>
    public class NovellaCommandPalette
    {
        public VisualElement Root { get; private set; }

        private readonly NovellaHubWindow _hub;
        private readonly TextField _input;
        private readonly Label _placeholder;
        private readonly ScrollView _list;
        private readonly List<Item> _allItems = new List<Item>();
        private List<Item> _filtered = new List<Item>();
        private int _selectedIndex;

        // Категория пункта определяет к какой section-группе он попадает.
        private enum Category { Navigation, Story, Character, Variable, Action }

        private struct Item
        {
            public string Label;
            public string Hint;          // Open / Edit / Action / Help
            public string IconLetter;    // 1 буква в круге
            public Color IconColor;
            public Category Category;
            public Action OnInvoke;
            // Если true — пункт скрыт когда EditorApplication.isPlaying.
            // Используется для всего что меняет проектное состояние или
            // переключает в модули, отключённые в Play (см. NovellaHubWindow).
            public bool DisabledInPlay;
        }

        public NovellaCommandPalette(NovellaHubWindow hub)
        {
            _hub = hub;

            Root = new VisualElement();
            Root.AddToClassList("ns-cmd-overlay");
            Root.AddToClassList("ns-cmd-overlay--hidden");
            // КРИТИЧНО: пока палитра скрыта — Ignore, иначе она перехватывает все клики
            // во всём окне (даже если визуально невидима через opacity:0).
            Root.pickingMode = PickingMode.Ignore;
            Root.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target == Root) Close();
            });

            var box = new VisualElement();
            box.AddToClassList("ns-cmd-box");
            box.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            Root.Add(box);

            // ─── Search row: 🔍 иконка + поле + placeholder ───
            var searchRow = new VisualElement();
            searchRow.AddToClassList("ns-cmd-search-row");
            box.Add(searchRow);

            var searchIcon = new Label("🔍");
            searchIcon.AddToClassList("ns-cmd-search-icon");
            searchRow.Add(searchIcon);

            _input = new TextField();
            _input.AddToClassList("ns-cmd-input");
            _input.RegisterValueChangedCallback(ev => Refilter(ev.newValue));
            _input.RegisterCallback<KeyDownEvent>(OnInputKey);
            searchRow.Add(_input);

            _placeholder = new Label(ToolLang.Get(
                "Search characters, stories, variables, actions…",
                "Поиск по персонажам, историям, переменным, действиям…"));
            _placeholder.AddToClassList("ns-cmd-placeholder");
            // Placeholder сидит ВНЕ search-row (как абсолютный overlay) чтобы
            // не сбивать раскладку TextField, но мы добавляем его в search-row
            // потому что у того position absolute класс и он растягивается
            // под весь box — лучше всё-таки overlay'ить относительно search-row.
            searchRow.Add(_placeholder);

            // ─── Список ───
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("ns-cmd-list");
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            box.Add(_list);

            // ─── Footer: подсказки хоткеев ───
            var footer = new VisualElement();
            footer.AddToClassList("ns-cmd-footer");
            box.Add(footer);

            footer.Add(MakeFooterHint("↑↓", ToolLang.Get("navigate", "навигация")));
            footer.Add(MakeFooterHint("Enter", ToolLang.Get("open", "открыть")));
            footer.Add(MakeFooterHint("Esc", ToolLang.Get("close", "закрыть")));
        }

        private VisualElement MakeFooterHint(string key, string action)
        {
            var hint = new VisualElement();
            hint.style.flexDirection = FlexDirection.Row;
            hint.style.alignItems = Align.Center;
            hint.style.marginLeft = 8; hint.style.marginRight = 8;

            var k = new Label(key);
            k.AddToClassList("ns-cmd-footer-key");
            hint.Add(k);

            var a = new Label(action);
            a.AddToClassList("ns-cmd-footer-hint");
            hint.Add(a);
            return hint;
        }

        public void Open()
        {
            BuildIndex();
            Refilter("");
            _selectedIndex = 0;
            UpdateSelection();

            Root.RemoveFromClassList("ns-cmd-overlay--hidden");
            Root.pickingMode = PickingMode.Position; // ловим клики только когда открыта
            // Фокусируем поле ввода в следующем кадре
            _input.schedule.Execute(() => _input.Focus()).StartingIn(10);
            _input.SetValueWithoutNotify("");
            UpdatePlaceholder();
        }

        public void Close()
        {
            Root.AddToClassList("ns-cmd-overlay--hidden");
            Root.pickingMode = PickingMode.Ignore; // НЕ глотаем клики когда закрыта
        }

        // ─────────── Индексация ───────────

        private void BuildIndex()
        {
            _allItems.Clear();

            var pal = new[]
            {
                new Color(0.36f, 0.75f, 0.92f), // cyan
                new Color(0.63f, 0.49f, 1f),    // purple
                new Color(1f, 0.55f, 0.45f),    // coral
                new Color(0.48f, 0.81f, 0.62f), // mint
                new Color(0.96f, 0.76f, 0.43f), // amber
                new Color(0.88f, 0.48f, 0.62f), // pink
            };

            // ─── Навигация по модулям Studio ───
            // (3 = UI Forge, 6 = Console — разрешены в Play; остальные скрываются.)
            void AddNav(int moduleIndex, string en, string ru, string letter, Color col, bool playOk)
            {
                int captured = moduleIndex;
                _allItems.Add(new Item
                {
                    Label = ToolLang.Get(en, ru),
                    Hint = ToolLang.Get("Open", "Открыть"),
                    IconLetter = letter,
                    IconColor = col,
                    Category = Category.Navigation,
                    OnInvoke = () => { _hub.SwitchToModule(captured); Close(); },
                    DisabledInPlay = !playOk,
                });
            }
            AddNav(0, "Open Home",          "Открыть Главную",          "🏠", pal[0], false);
            AddNav(3, "Open UI Forge",      "Открыть Кузницу UI",       "🔨", pal[1], true);
            AddNav(1, "Open Characters",    "Открыть Персонажей",       "👤", pal[2], false);
            AddNav(2, "Open Scenes & Menu", "Открыть Сцены и Меню",     "🎬", pal[3], false);
            AddNav(4, "Open Variables",     "Открыть Переменные",       "📊", pal[4], false);
            AddNav(5, "Open Build",         "Открыть Сборку",           "📦", pal[5], false);
            AddNav(6, "Open Console",       "Открыть Консоль",          "📟", pal[0], true);
            AddNav(7, "Open Settings",      "Открыть Настройки",        "⚙",  pal[1], false);

            // Персонажи
            int idx = 0;
            foreach (var g in AssetDatabase.FindAssets("t:NovellaCharacter"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(path);
                if (c == null) continue;
                var captured = c;
                _allItems.Add(new Item
                {
                    Label = string.IsNullOrEmpty(c.CharacterID) ? c.name : c.CharacterID,
                    Hint = ToolLang.Get("Open", "Открыть"),
                    IconLetter = "👤",
                    IconColor = pal[idx++ % pal.Length],
                    Category = Category.Character,
                    OnInvoke = () =>
                    {
                        _hub.SwitchToModule(1);
                        var mod = _hub.GetModule(1) as NovellaCharacterEditorModule;
                        mod?.SelectCharacter(captured);
                        Close();
                    },
                    DisabledInPlay = true,  // открывает Персонажей — модуль закрыт в Play
                });
            }

            // Переменные — иконка зависит от типа.
            var varSettings = NovellaVariableSettings.Instance;
            if (varSettings != null && varSettings.Variables != null)
            {
                foreach (var v in varSettings.Variables)
                {
                    if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                    var capName = v.Name;
                    string emoji = v.Type switch
                    {
                        EVarType.Boolean => "✓",
                        EVarType.String  => "📝",
                        EVarType.Float   => "≈",
                        EVarType.Choice  => "🎯",
                        EVarType.List    => "📋",
                        _ => "💠",
                    };
                    _allItems.Add(new Item
                    {
                        Label = v.Name,
                        Hint = ToolLang.Get("Edit", "Изменить"),
                        IconLetter = emoji,
                        IconColor = pal[2],
                        Category = Category.Variable,
                        OnInvoke = () =>
                        {
                            NovellaVariableEditorModule.ShowInHub(capName);
                            Close();
                        },
                        DisabledInPlay = true,  // открывает модуль Переменных
                    });
                }
            }

            // Истории — клик меняет активную историю и открывает граф.
            // В Play менять активную историю нельзя → пункт скрыт.
            foreach (var g in AssetDatabase.FindAssets("t:NovellaStory"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var s = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                if (s == null) continue;
                var captured = s;
                var capGuid = g;
                _allItems.Add(new Item
                {
                    Label = string.IsNullOrEmpty(s.Title) ? s.name : s.Title,
                    Hint = ToolLang.Get("Open", "Открыть"),
                    IconLetter = "📖",
                    IconColor = pal[1],
                    Category = Category.Story,
                    OnInvoke = () =>
                    {
                        EditorPrefs.SetString("Novella_ActiveStoryGuid", capGuid);
                        if (captured.StartingChapter != null)
                            NovellaGraphWindow.OpenGraphWindow(captured.StartingChapter);
                        Close();
                    },
                    DisabledInPlay = true,
                });
            }

            // ─── Действия ───
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Create new character", "Создать персонажа"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "+",
                IconColor = pal[3],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    _hub.SwitchToModule(1);
                    Close();
                },
                DisabledInPlay = true,
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Create new variable", "Создать переменную"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "+",
                IconColor = pal[3],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    _hub.SwitchToModule(4);
                    Close();
                },
                DisabledInPlay = true,
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Build the game", "Собрать игру"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "📦",
                IconColor = pal[3],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    // 5 = NovellaBuildModule
                    _hub.SwitchToModule(5);
                    Close();
                },
                DisabledInPlay = true,
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Import images & audio", "Импорт картинок и аудио"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "📥",
                IconColor = pal[3],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    Close();
                    EditorApplication.delayCall += () => NovellaAssetImportDialog.Open();
                }
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Insert prefab into scene", "Вставить префаб в сцену"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "📦",
                IconColor = pal[3],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    // 3 = NovellaUIForge в списке модулей хаба.
                    _hub.SwitchToModule(3);
                    Close();
                    // Дважды-задержка: первая чтобы UI хаба перерисовался и
                    // _activeInstance Кузницы успел зарегистрироваться,
                    // вторая чтобы попап открылся уже после.
                    EditorApplication.delayCall += () =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            NovellaUIForge.RequestInsertPrefabFromExternal();
                        };
                    };
                }
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Restart tutorial", "Перезапустить туториал"),
                Hint = ToolLang.Get("Help", "Помощь"),
                IconLetter = "?",
                IconColor = pal[4],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        NovellaWelcomeWindow.ShowWindow();
                        if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.Close();
                    };
                    Close();
                },
                DisabledInPlay = true,
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Toggle language (RU/EN)", "Сменить язык"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "🌐",
                IconColor = pal[5],
                Category = Category.Action,
                OnInvoke = () =>
                {
                    ToolLang.Toggle();
                    Close();
                }
            });
        }

        // ─────────── Фильтрация ───────────

        private void Refilter(string query)
        {
            // В Play отбрасываем пункты, ведущие в заблокированные модули
            // (Персонажи, Сцены, Сборка и т.п.) — чтобы юзер не видел того
            // что нажать всё равно не получится.
            bool inPlay = EditorApplication.isPlaying;
            IEnumerable<Item> source = inPlay
                ? _allItems.Where(i => !i.DisabledInPlay)
                : _allItems;

            if (string.IsNullOrEmpty(query))
            {
                _filtered = source.Take(60).ToList();
            }
            else
            {
                var q = query.ToLowerInvariant();
                _filtered = source
                    .Where(i => i.Label.ToLowerInvariant().Contains(q))
                    .Take(60)
                    .ToList();
            }
            _selectedIndex = 0;
            RebuildList();
            UpdatePlaceholder();
        }

        private void UpdatePlaceholder()
        {
            string text = _input.value ?? "";
            if (string.IsNullOrEmpty(text))
                _placeholder.RemoveFromClassList("ns-cmd-placeholder--hidden");
            else
                _placeholder.AddToClassList("ns-cmd-placeholder--hidden");
        }

        private static string SectionTitle(Category c)
        {
            return c switch
            {
                Category.Navigation => ToolLang.Get("NAVIGATE TO",   "ПЕРЕЙТИ В"),
                Category.Story      => ToolLang.Get("STORIES",       "ИСТОРИИ"),
                Category.Character  => ToolLang.Get("CHARACTERS",    "ПЕРСОНАЖИ"),
                Category.Variable   => ToolLang.Get("VARIABLES",     "ПЕРЕМЕННЫЕ"),
                Category.Action     => ToolLang.Get("ACTIONS",       "ДЕЙСТВИЯ"),
                _ => ""
            };
        }

        private void RebuildList()
        {
            _list.Clear();

            // Empty state — нет результатов под фильтр.
            if (_filtered.Count == 0)
            {
                var empty = new Label(ToolLang.Get(
                    "Nothing matches.\nTry a different keyword.",
                    "Под запрос ничего не нашлось.\nПопробуй другие слова."));
                empty.AddToClassList("ns-cmd-empty");
                _list.Add(empty);
                return;
            }

            // Группируем по Category и отрисовываем заголовки секций между группами.
            // Порядок групп — фиксированный (по enum-у), внутри группы — порядок
            // как был в _filtered (важно для подсветки/Enter-нав).
            Category? lastCat = null;
            for (int i = 0; i < _filtered.Count; i++)
            {
                int captured = i;
                var item = _filtered[i];

                if (lastCat != item.Category)
                {
                    var section = new Label(SectionTitle(item.Category));
                    section.AddToClassList("ns-cmd-section");
                    _list.Add(section);
                    lastCat = item.Category;
                }

                var row = new VisualElement();
                row.AddToClassList("ns-cmd-row");
                if (i == _selectedIndex) row.AddToClassList("ns-cmd-row--selected");

                var ic = new Label(item.IconLetter);
                ic.AddToClassList("ns-cmd-row__icon");
                ic.style.color = item.IconColor;
                row.Add(ic);

                var lbl = new Label(item.Label);
                lbl.AddToClassList("ns-cmd-row__label");
                row.Add(lbl);

                var hk = new Label(item.Hint.ToUpperInvariant());
                hk.AddToClassList("ns-cmd-row__hint");
                row.Add(hk);

                row.RegisterCallback<ClickEvent>(_ => Invoke(captured));
                row.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    _selectedIndex = captured;
                    UpdateSelection();
                });
                _list.Add(row);
            }
        }

        private void UpdateSelection()
        {
            // Учитываем что между rows есть section-headers — нумерация _selectedIndex
            // относится к items, а в _list.Children() строк больше (header'ы тоже).
            int rowIdx = 0;
            foreach (var ch in _list.Children())
            {
                if (!ch.ClassListContains("ns-cmd-row")) continue;
                if (rowIdx == _selectedIndex) ch.AddToClassList("ns-cmd-row--selected");
                else ch.RemoveFromClassList("ns-cmd-row--selected");
                rowIdx++;
            }
        }

        private void OnInputKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.DownArrow)
            {
                _selectedIndex = Mathf.Min(_selectedIndex + 1, _filtered.Count - 1);
                UpdateSelection();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                _selectedIndex = Mathf.Max(_selectedIndex - 1, 0);
                UpdateSelection();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Invoke(_selectedIndex);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                Close();
                e.StopPropagation();
            }
        }

        private void Invoke(int index)
        {
            if (index < 0 || index >= _filtered.Count) return;
            _filtered[index].OnInvoke?.Invoke();
        }
    }
}
