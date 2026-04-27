using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Создаёт ScriptableObject-ассеты со всеми текстами туториалов из старого
    /// хардкод-NovellaTutorialManager. Запускается автоматически при первом
    /// открытии Welcome-окна, или вручную через меню.
    /// </summary>
    public static class NovellaTutorialMigrator
    {
        private const string TUTORIALS_DIR = "Assets/NovellaEngine/Tutorials/Lessons";

        [MenuItem("Tools/Novella Engine/Tutorials/Migrate Legacy Tutorials")]
        public static void MigrateMenu()
        {
            int created = MigrateIfNeeded(force: true);
            EditorUtility.DisplayDialog("Novella Tutorials",
                $"Создано/обновлено уроков: {created}\nПапка: {TUTORIALS_DIR}", "OK");
        }

        /// <summary>
        /// Возвращает количество созданных ассетов.
        /// Если force=false — создаёт только отсутствующие, не трогает существующие.
        /// </summary>
        public static int MigrateIfNeeded(bool force = false)
        {
            EnsureFolder(TUTORIALS_DIR);

            int created = 0;
            foreach (var (key, builder) in _builders)
            {
                string path = $"{TUTORIALS_DIR}/{key}.asset";
                bool exists = AssetDatabase.LoadAssetAtPath<NovellaTutorialAsset>(path) != null;
                if (exists && !force) continue;

                var asset = exists ? AssetDatabase.LoadAssetAtPath<NovellaTutorialAsset>(path) : ScriptableObject.CreateInstance<NovellaTutorialAsset>();
                builder(asset);
                if (!exists) AssetDatabase.CreateAsset(asset, path);
                else EditorUtility.SetDirty(asset);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created;
        }

        private static void EnsureFolder(string assetPath)
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                AssetDatabase.Refresh();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Билдеры всех 6 уроков. Тексты максимально близки к старой системе,
        // но переписаны более дружелюбно — для низкого порога входа.
        // Селекторы целей переведены на ByVisualElementName / ByControlName,
        // где это возможно. Где невозможно — оставлен ManualRect с процентами,
        // что устойчиво к изменению размера окна.
        // ─────────────────────────────────────────────────────────────────

        private static readonly List<(string key, System.Action<NovellaTutorialAsset> build)> _builders = new()
        {
            ("01_SceneManager", BuildSceneManager),
            ("02_CharacterEditor", BuildCharacterEditor),
            ("03_GraphEditor", BuildGraphEditor),
            ("04_DLCManager", BuildDLCManager),
            ("05_UIEditor", BuildUIEditor),
            ("06_InteractiveLesson", BuildInteractiveLesson),
        };

        private static void BuildSceneManager(NovellaTutorialAsset a)
        {
            a.TutorialKey = "SceneManager";
            a.OrderIndex = 1;
            a.Icon = "🛠";
            a.TitleEN = "Scenes & Menu";
            a.TitleRU = "Сцены и Меню";
            a.DescriptionEN = "Set up Unity scenes for your gameplay and main menu.";
            a.DescriptionRU = "Настроим Unity-сцены для игры и главного меню.";
            a.HostWindow = ETutorialHostWindow.NovellaHub_SceneManager;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "Welcome to Scene Manager",
                    titleRU: "Добро пожаловать в Менеджер Сцен",
                    bodyEN: "A <b>Scene</b> in Unity is like a separate level or screen. Novella Engine uses different scenes to keep your <b>Main Menu</b> and <b>Game</b> apart — this prevents UI overlap and keeps the project clean.",
                    bodyRU: "<b>Сцена</b> в Unity — это как отдельный уровень или экран. Novella Engine использует разные сцены, чтобы разделить <b>Главное Меню</b> и <b>Игру</b> — так интерфейсы не накладываются и проект остаётся чистым.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.12f)
                ),
                Step(
                    titleEN: "Project Scenes List",
                    titleRU: "Список сцен проекта",
                    bodyEN: "Here are <b>all scenes</b> from your Build Settings. Use the <b>+ Create New Scene</b> button below to add new ones — the engine will register them automatically.",
                    bodyRU: "Здесь все <b>сцены проекта</b> из Build Settings. Кнопка <b>+ Создать новую сцену</b> внизу добавит новую — движок зарегистрирует её автоматически.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0.20f, 0.10f, 0.78f, 0.78f)
                ),
                Step(
                    titleEN: "One-Click UI Generation",
                    titleRU: "UI в один клик",
                    bodyEN: "Pick an <b>empty scene</b> and you'll see <b>Quick Setup</b> buttons to instantly generate the Game Canvas or Main Menu structure. Try this after the tour!",
                    bodyRU: "Выберите <b>пустую сцену</b> и появятся кнопки <b>Быстрой настройки</b> — они моментально создадут Canvas игры или Главного Меню. Попробуйте после тура!",
                    hint: ETutorialHintStyle.PointingFinger,
                    target: ManualPercent(0.20f, 0.65f, 0.78f, 0.30f)
                ),
            };
        }

        private static void BuildCharacterEditor(NovellaTutorialAsset a)
        {
            a.TutorialKey = "CharacterEditor";
            a.OrderIndex = 2;
            a.Icon = "🦸";
            a.TitleEN = "Actors & Variables";
            a.TitleRU = "Персонажи и Переменные";
            a.DescriptionEN = "Define actors, multi-layered Paper Dolls, and global variables.";
            a.DescriptionRU = "Создадим персонажей с многослойной графикой и переменные.";
            a.HostWindow = ETutorialHostWindow.NovellaHub_CharacterEditor;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "Welcome to Characters Module",
                    titleRU: "Модуль персонажей",
                    bodyEN: "Create, manage and edit all your story actors here. <i>Main Characters</i> and <i>Favorites</i> are auto-grouped at the top so you can find them instantly.",
                    bodyRU: "Создавай, редактируй и управляй всеми персонажами здесь. <i>Главные герои</i> и <i>Избранные</i> автоматически группируются сверху списка.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.10f)
                ),
                Step(
                    titleEN: "Characters Database",
                    titleRU: "База персонажей",
                    bodyEN: "This is your <b>characters database</b>. Click <b>+ Create New Character</b> at the top to add your first actor.",
                    bodyRU: "Это <b>база персонажей</b>. Нажми <b>+ Создать персонажа</b> сверху, чтобы добавить первого актёра.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0, 0.05f, 0.22f, 0.92f)
                ),
                Step(
                    titleEN: "ID vs Display Name",
                    titleRU: "ID и Имя — в чём разница",
                    bodyEN: "<b>Character ID</b> is the internal identifier used in code and graph references — must be unique, no spaces.\n\n<b>Display Name</b> is what the <b>player sees</b> in dialogues. Set per language.\n\nTip: turn on <b>💡 Tips</b> at the top of the inspector to see field hints.",
                    bodyRU: "<b>Character ID</b> — внутренний идентификатор для кода и ссылок в графе, должен быть уникален и без пробелов.\n\n<b>Display Name</b> — это то, что <b>увидит игрок</b> в диалоге. Задаётся для каждого языка.\n\nПодсказка: включи <b>💡 Подсказки</b> сверху инспектора, чтобы увидеть пояснения к полям.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0.22f, 0.05f, 0.34f, 0.92f)
                ),
                Step(
                    titleEN: "Paper Doll System",
                    titleRU: "Система слоёв (Paper Doll)",
                    bodyEN: "Build characters <b>layer by layer</b>: Body → Clothes → Face → Hair → Accessories. Re-order with ▲▼ buttons. Each layer can have its own offset, scale and tint.",
                    bodyRU: "Собирай персонажей по <b>слоям</b>: Тело → Одежда → Лицо → Волосы → Аксессуары. Кнопки ▲▼ меняют порядок. У каждого слоя свой сдвиг, масштаб и оттенок.",
                    hint: ETutorialHintStyle.Outline,
                    target: ManualPercent(0.22f, 0.30f, 0.34f, 0.45f)
                ),
                Step(
                    titleEN: "Emotions are presets",
                    titleRU: "Эмоции — это пресеты",
                    bodyEN: "An <b>Emotion preset</b> overrides only the layers you choose. Example: \"Smile\" replaces just the <i>Face</i> layer — body and clothes stay untouched. Use <code>character.SetEmotion(\"Smile\")</code> in dialogue.",
                    bodyRU: "<b>Эмоция</b> — это пресет, который подменяет только указанные слои. Например, «Улыбка» подменяет только слой <i>Face</i>, тело и одежда остаются. В диалоге вызывай <code>character.SetEmotion(\"Smile\")</code>.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0.22f, 0.55f, 0.34f, 0.40f)
                ),
            };
        }

        private static void BuildGraphEditor(NovellaTutorialAsset a)
        {
            a.TutorialKey = "GraphEditor";
            a.OrderIndex = 3;
            a.Icon = "🗺";
            a.TitleEN = "Graph Editor";
            a.TitleRU = "Редактор Графа";
            a.DescriptionEN = "Connect dialogue, choices, audio and logic into your story.";
            a.DescriptionRU = "Соединяй диалоги, выборы, аудио и логику в одну историю.";
            a.HostWindow = ETutorialHostWindow.GraphWindow;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "Welcome to the Graph Editor",
                    titleRU: "Добро пожаловать в Редактор Графа",
                    bodyEN: "This is the <b>heart of Novella Engine</b>. Your story logic, branches and dialogues all live here. Each node is a step in your scenario.",
                    bodyRU: "Это <b>сердце Novella Engine</b>. Логика истории, ветвления и диалоги живут здесь. Каждая нода — шаг сценария.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.12f)
                ),
                Step(
                    titleEN: "Node Inspector",
                    titleRU: "Инспектор ноды",
                    bodyEN: "<b>Click any node</b> to open its <b>Inspector</b> on the right. There you edit text, pick characters, attach audio and configure logic.",
                    bodyRU: "<b>Нажми на любую ноду</b>, чтобы открыть её <b>Инспектор</b> справа. Там настраивается текст, выбор персонажей, звук и логика.",
                    hint: ETutorialHintStyle.Outline,
                    target: ManualPercent(0.65f, 0.08f, 0.34f, 0.90f)
                ),
                Step(
                    titleEN: "Minimap",
                    titleRU: "Миникарта",
                    bodyEN: "The <b>Minimap</b> in the bottom-left helps you navigate large story graphs. Click and drag inside it to jump around.",
                    bodyRU: "<b>Миникарта</b> в левом нижнем углу помогает ориентироваться в больших графах. Кликни и тяни внутри неё, чтобы прыгнуть в нужное место.",
                    hint: ETutorialHintStyle.PointingFinger,
                    target: ManualPercent(0.01f, 0.72f, 0.20f, 0.25f)
                ),
                Step(
                    titleEN: "Connect nodes by dragging",
                    titleRU: "Соединяй ноды, протягивая связи",
                    bodyEN: "Drag from a node's <b>output port</b> to another node's <b>input port</b>. Typical flow: <b>Start → Dialogue → Branch</b>. Right-click empty space to add new nodes.",
                    bodyRU: "Тяни от <b>выходного порта</b> ноды к <b>входному</b> другой. Типичный порядок: <b>Старт → Диалог → Развилка</b>. Правый клик в пустоте — добавить ноду.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0, 0.12f, 0.65f, 0.85f)
                ),
            };
        }

        private static void BuildDLCManager(NovellaTutorialAsset a)
        {
            a.TutorialKey = "DLCManager";
            a.OrderIndex = 4;
            a.Icon = "🧩";
            a.TitleEN = "DLC Modules";
            a.TitleRU = "Модули DLC";
            a.DescriptionEN = "Expand the engine with extra mechanics — wardrobe, quests, inventory.";
            a.DescriptionRU = "Расширь движок дополнительными механиками — гардероб, квесты, инвентарь.";
            a.HostWindow = ETutorialHostWindow.GraphWindow_DLC;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "DLC Modules Manager",
                    titleRU: "Менеджер модулей DLC",
                    bodyEN: "<b>DLCs</b> seamlessly add new node types to Novella Engine — wardrobes, mini-games, quest systems. Toggle them on or off here.",
                    bodyRU: "<b>DLC</b> добавляют новые типы нод — гардеробы, мини-игры, системы квестов. Включай и выключай их здесь.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.12f)
                ),
                Step(
                    titleEN: "Open the DLC tab in the sidebar",
                    titleRU: "Найди вкладку DLC в сайдбаре",
                    bodyEN: "Look in the <b>Workspace Tools</b> panel — the <b>🧩 DLC Modules</b> button is there. It's the control center for all installed extensions.",
                    bodyRU: "Смотри в панель <b>Инструменты</b> слева — там есть кнопка <b>🧩 Модули DLC</b>. Это центр управления всеми расширениями.",
                    hint: ETutorialHintStyle.PointingFinger,
                    target: ManualPercent(0.01f, 0.20f, 0.20f, 0.10f)
                ),
                Step(
                    titleEN: "Graceful Degradation",
                    titleRU: "Умный пропуск",
                    bodyEN: "If you <b>disable a DLC</b>, the player will just <b>skip those nodes</b> in-game — no errors. This makes it safe to share scenarios with people who haven't bought the DLC.",
                    bodyRU: "Если ты <b>отключишь DLC</b>, плеер <b>проскочит его ноды насквозь</b> в игре — никаких ошибок. Можно безопасно делиться сценариями с теми, у кого DLC не куплен.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0.30f, 0.30f, 0.40f, 0.40f)
                ),
            };
        }

        private static void BuildUIEditor(NovellaTutorialAsset a)
        {
            a.TutorialKey = "UIEditor";
            a.OrderIndex = 5;
            a.Icon = "🎨";
            a.TitleEN = "UI Forge";
            a.TitleRU = "Кузница UI";
            a.DescriptionEN = "Style dialogue frames, main menu and character wardrobe.";
            a.DescriptionRU = "Стилизация диалогов, меню и гардероба персонажа.";
            a.HostWindow = ETutorialHostWindow.NovellaHub_UIEditor;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "Welcome to UI Forge",
                    titleRU: "Кузница UI",
                    bodyEN: "Design every visual aspect of your game directly inside the Studio. Tip: for advanced custom UI, learn Unity Canvas — UI Forge is a great starting point but it's basic.",
                    bodyRU: "Здесь настраивается весь визуал игры прямо внутри Студии. Совет: для сложного кастомного UI изучи Unity Canvas — Кузница UI — отличный старт, но базовый инструмент.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.12f)
                ),
                Step(
                    titleEN: "Settings panel (left)",
                    titleRU: "Панель настроек (слева)",
                    bodyEN: "Bind your <b>Story Graph</b>, customize <b>Dialogue frames</b>, <b>Menus</b> and the <b>Character Wardrobe</b> flow.",
                    bodyRU: "Привязывай <b>Граф истории</b>, настраивай <b>рамки диалогов</b>, <b>Меню</b> и поток <b>Гардероба персонажа</b>.",
                    hint: ETutorialHintStyle.Outline,
                    target: ManualPercent(0, 0.10f, 0.50f, 0.88f)
                ),
                Step(
                    titleEN: "Live Preview (right)",
                    titleRU: "Живой превью (справа)",
                    bodyEN: "See how your UI looks on <b>PC and Mobile</b> instantly — no need to press Play.",
                    bodyRU: "Смотри как выглядит UI на <b>ПК и Мобильных</b> мгновенно — без нажатия Play.",
                    hint: ETutorialHintStyle.Outline,
                    target: ManualPercent(0.50f, 0.10f, 0.50f, 0.88f)
                ),
            };
        }

        private static void BuildInteractiveLesson(NovellaTutorialAsset a)
        {
            a.TutorialKey = "InteractiveLesson";
            a.OrderIndex = 6;
            a.Icon = "▶";
            a.TitleEN = "Interactive Lesson";
            a.TitleRU = "Интерактивный Урок";
            a.DescriptionEN = "Open a real example graph and see all systems working together.";
            a.DescriptionRU = "Откроем реальный граф-пример и посмотрим все системы вместе.";
            a.HostWindow = ETutorialHostWindow.GraphWindow_InteractiveLesson;
            a.Steps = new List<NovellaTutorialStep>
            {
                Step(
                    titleEN: "Welcome to the Interactive Lesson!",
                    titleRU: "Интерактивный Урок",
                    bodyEN: "Take a close look at this graph and see how nodes are connected. <b>Dialogue → Branch → Audio → End</b> — all working together.",
                    bodyRU: "Внимательно изучи граф и посмотри, как соединены ноды. <b>Диалог → Развилка → Аудио → Конец</b> — всё работает вместе.",
                    hint: ETutorialHintStyle.Tooltip,
                    target: ManualPercent(0, 0, 1f, 0.10f)
                ),
                Step(
                    titleEN: "Experiment freely",
                    titleRU: "Экспериментируй свободно",
                    bodyEN: "Feel free to <b>delete, create and modify</b> nodes. Don't worry about breaking things — this lesson graph auto-restores when you restart the tutorial.",
                    bodyRU: "Можешь смело <b>удалять, создавать и менять</b> ноды. Не бойся ничего сломать — обучающий граф восстановится при перезапуске урока.",
                    hint: ETutorialHintStyle.Spotlight,
                    target: ManualPercent(0, 0.10f, 1f, 0.90f)
                ),
            };
        }

        // ─────────── Хелперы ───────────

        private static NovellaTutorialStep Step(string titleEN, string titleRU,
            string bodyEN, string bodyRU,
            ETutorialHintStyle hint,
            (Rect rect, bool percent) target,
            ETutorialPanelAnchor anchor = ETutorialPanelAnchor.Auto,
            ETutorialAdvanceMode advance = ETutorialAdvanceMode.OnNextButton)
        {
            return new NovellaTutorialStep
            {
                TitleEN = titleEN,
                TitleRU = titleRU,
                BodyEN = bodyEN,
                BodyRU = bodyRU,
                HintStyle = hint,
                AccentColor = new Color(0.36f, 0.75f, 0.92f, 1f),
                PanelAnchor = anchor,
                AdvanceMode = advance,
                TargetMode = ETutorialTargetMode.ManualRect,
                ManualRect = target.rect,
                ManualRectUsePercent = target.percent,
                MinHoldSeconds = 1.5f,
                AllowSkip = true,
            };
        }

        private static (Rect rect, bool percent) ManualPercent(float x, float y, float w, float h)
        {
            return (new Rect(x, y, w, h), true);
        }
    }
}
