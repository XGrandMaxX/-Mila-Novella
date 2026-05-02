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
    /// Cmd+K command palette для Novella Studio. Один поиск по всему проекту:
    /// персонажи, переменные, истории, действия. Fuzzy-match по подстроке.
    /// </summary>
    public class NovellaCommandPalette
    {
        public VisualElement Root { get; private set; }

        private readonly NovellaHubWindow _hub;
        private readonly TextField _input;
        private readonly ScrollView _list;
        private readonly List<Item> _allItems = new List<Item>();
        private List<Item> _filtered = new List<Item>();
        private int _selectedIndex;

        private struct Item
        {
            public string Label;
            public string Hint;          // Open / Edit / Action / Help
            public string IconLetter;    // 1 буква в круге
            public Color IconColor;
            public Action OnInvoke;
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

            _input = new TextField();
            _input.AddToClassList("ns-cmd-input");
            _input.RegisterValueChangedCallback(ev => Refilter(ev.newValue));
            _input.RegisterCallback<KeyDownEvent>(OnInputKey);
            box.Add(_input);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("ns-cmd-list");
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            box.Add(_list);
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
                    Label = ToolLang.Get("Character: ", "Персонаж: ") + (string.IsNullOrEmpty(c.CharacterID) ? c.name : c.CharacterID),
                    Hint = ToolLang.Get("Open", "Открыть"),
                    IconLetter = "C",
                    IconColor = pal[idx++ % pal.Length],
                    OnInvoke = () =>
                    {
                        _hub.SwitchToModule(1);
                        var mod = _hub.GetModule(1) as NovellaCharacterEditorModule;
                        mod?.SelectCharacter(captured);
                        Close();
                    }
                });
            }

            // Переменные
            var varSettings = NovellaVariableSettings.Instance;
            if (varSettings != null && varSettings.Variables != null)
            {
                foreach (var v in varSettings.Variables)
                {
                    if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                    var capName = v.Name;
                    _allItems.Add(new Item
                    {
                        Label = ToolLang.Get("Variable: ", "Переменная: ") + v.Name,
                        Hint = ToolLang.Get("Edit", "Изменить"),
                        IconLetter = "V",
                        IconColor = pal[2],
                        OnInvoke = () =>
                        {
                            NovellaVariableEditorModule.ShowInHub(capName);
                            Close();
                        }
                    });
                }
            }

            // Истории
            foreach (var g in AssetDatabase.FindAssets("t:NovellaStory"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var s = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                if (s == null) continue;
                var captured = s;
                var capGuid = g;
                _allItems.Add(new Item
                {
                    Label = ToolLang.Get("Story: ", "История: ") + (string.IsNullOrEmpty(s.Title) ? "(untitled)" : s.Title),
                    Hint = ToolLang.Get("Open graph", "Открыть граф"),
                    IconLetter = "S",
                    IconColor = pal[1],
                    OnInvoke = () =>
                    {
                        EditorPrefs.SetString("Novella_ActiveStoryGuid", capGuid);
                        if (captured.StartingChapter != null)
                            NovellaGraphWindow.OpenGraphWindow(captured.StartingChapter);
                        Close();
                    }
                });
            }

            // Действия
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Create new character", "Создать персонажа"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "+",
                IconColor = pal[3],
                OnInvoke = () =>
                {
                    _hub.SwitchToModule(1);
                    Close();
                }
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Create new variable", "Создать переменную"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "+",
                IconColor = pal[3],
                OnInvoke = () =>
                {
                    _hub.SwitchToModule(4);
                    Close();
                }
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Insert prefab into scene", "Вставить префаб в сцену"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "P",
                IconColor = pal[3],
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
                OnInvoke = () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        NovellaWelcomeWindow.ShowWindow();
                        if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.Close();
                    };
                    Close();
                }
            });
            _allItems.Add(new Item
            {
                Label = ToolLang.Get("Toggle language (RU/EN)", "Сменить язык"),
                Hint = ToolLang.Get("Action", "Действие"),
                IconLetter = "L",
                IconColor = pal[5],
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
            if (string.IsNullOrEmpty(query))
            {
                _filtered = _allItems.Take(40).ToList();
            }
            else
            {
                var q = query.ToLowerInvariant();
                _filtered = _allItems
                    .Where(i => i.Label.ToLowerInvariant().Contains(q))
                    .Take(40)
                    .ToList();
            }
            _selectedIndex = 0;
            RebuildList();
        }

        private void RebuildList()
        {
            _list.Clear();
            for (int i = 0; i < _filtered.Count; i++)
            {
                int captured = i;
                var item = _filtered[i];
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
            int i = 0;
            foreach (var row in _list.Children())
            {
                if (i == _selectedIndex) row.AddToClassList("ns-cmd-row--selected");
                else row.RemoveFromClassList("ns-cmd-row--selected");
                i++;
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
