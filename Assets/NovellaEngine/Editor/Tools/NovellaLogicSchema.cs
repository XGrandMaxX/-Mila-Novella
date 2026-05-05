// ════════════════════════════════════════════════════════════════════════════
// NovellaLogicSchema — «прослойка» между сырыми именами полей в коде
// (StoryLauncher.MainCharacterAsset) и понятным юзеру UI («Главный персонаж»
// + подсказка «Перетащи NovellaCharacter сюда…»).
//
// Используется в табе «⚙ Логика» Кузницы UI: для каждого MonoBehaviour
// смотрим есть ли схема. Если да — рисуем красивые секции с лейблами,
// подсказками и группировкой. Если нет — fallback в сырой PropertyField.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public static class NovellaLogicSchema
    {
        // Описание одного поля в схеме.
        public class FieldEntry
        {
            // Имя SerializedProperty (= имя сериализуемого поля в C#).
            public string PropertyName;
            // Лейбл который видит юзер.
            public string LabelEN;
            public string LabelRU;
            // Подсказка под полем (показывается когда включены 💡 Подсказки).
            public string HintEN;
            public string HintRU;
            // Если true — поле необязательное, прячем под «Дополнительно».
            public bool Advanced;
        }

        // Описание схемы скрипта целиком.
        public class ComponentSchema
        {
            public string TypeFullName;
            public string TitleEN;
            public string TitleRU;
            public string IntroEN;
            public string IntroRU;
            public List<FieldEntry> Fields = new List<FieldEntry>();
        }

        private static readonly Dictionary<Type, ComponentSchema> _registry = new Dictionary<Type, ComponentSchema>();

        static NovellaLogicSchema()
        {
            Register(typeof(NovellaEngine.Runtime.StoryLauncher), new ComponentSchema
            {
                TitleEN = "Main Menu — Story Launcher",
                TitleRU = "Главное меню — запуск историй",
                IntroEN = "This script controls how the player picks a story and creates their character on the main menu screen.",
                IntroRU = "Этот скрипт управляет тем, как игрок выбирает историю и создаёт персонажа на главном меню.",
                Fields = new List<FieldEntry>
                {
                    new FieldEntry
                    {
                        PropertyName = "MainCharacterAsset",
                        LabelEN = "Main character",
                        LabelRU = "Главный персонаж",
                        HintEN = "Drag a NovellaCharacter asset here. Without it the look picker (← →) and avatar in 'Create character' panel won't work — they'll be hidden.",
                        HintRU = "Перетащи сюда NovellaCharacter. Без него стрелки выбора внешности и аватар в панели создания персонажа не работают и автоматически скрываются."
                    },
                    new FieldEntry
                    {
                        PropertyName = "StoryButtonPrefab",
                        LabelEN = "Story card prefab",
                        LabelRU = "Префаб карточки истории",
                        HintEN = "The button prefab spawned for each story in the menu. Leave default for the built-in look, or drag your own.",
                        HintRU = "Префаб кнопки, который спавнится для каждой истории в меню. Можно оставить дефолтный или сделать свой и перетащить сюда."
                    },
                    new FieldEntry
                    {
                        PropertyName = "SpecificStories",
                        LabelEN = "Specific stories",
                        LabelRU = "Конкретные истории",
                        HintEN = "Empty = auto-load all stories from Resources/Stories. Add stories manually here only if you want to filter the list.",
                        HintRU = "Пусто = автозагрузка всех историй из Resources/Stories. Добавляй сюда вручную только если хочешь показывать конкретный поднабор."
                    },
                    new FieldEntry
                    {
                        PropertyName = "GameSceneName",
                        LabelEN = "Fallback gameplay scene",
                        LabelRU = "Запасная игровая сцена",
                        HintEN = "Used only if the picked story has no GameSceneName of its own. Modern flow assigns a scene per-story in Story Settings.",
                        HintRU = "Используется только если у выбранной истории нет своей игровой сцены. Современный путь — назначать сцену в настройках истории.",
                        Advanced = true
                    },
                    // Auto-discoverable refs — прячем под Advanced (StoryLauncher.AutoFindPanels их сам находит).
                    new FieldEntry { PropertyName = "MainMenuPanel",   LabelRU = "Главное меню (панель)",          LabelEN = "Main menu panel",          HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "StoriesPanel",    LabelRU = "Панель выбора истории",          LabelEN = "Stories panel",            HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCCreationPanel", LabelRU = "Панель создания персонажа",      LabelEN = "Character creation panel", HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCNameInput",     LabelRU = "Поле имени персонажа",           LabelEN = "Name input field",         HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCConfirmButton", LabelRU = "Кнопка «Готово»",                LabelEN = "Confirm button",           HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCAvatarPreview", LabelRU = "Превью аватара",                 LabelEN = "Avatar preview",           HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCPrevLookButton", LabelRU = "Кнопка ← (предыдущий вид)",     LabelEN = "Prev look button",         HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "MCNextLookButton", LabelRU = "Кнопка → (следующий вид)",      LabelEN = "Next look button",         HintRU = "Авто-обнаружение по имени.", HintEN = "Auto-detected by name.", Advanced = true },
                    new FieldEntry { PropertyName = "StoriesContainer", LabelRU = "Контейнер для карточек историй", LabelEN = "Stories container",        HintRU = "Сюда складываются спавненные карточки.", HintEN = "Spawned story cards go here.", Advanced = true },
                }
            });

            Register(typeof(NovellaEngine.Runtime.NovellaPresetMarker), new ComponentSchema
            {
                TitleEN = "Preset Marker",
                TitleRU = "Маркер пресета",
                IntroEN = "Internal tag — marks this object as auto-created by a preset. Don't edit unless you know what you're doing.",
                IntroRU = "Технический маркер — обозначает что объект создан пресетом. Лучше не трогать.",
                Fields = new List<FieldEntry>
                {
                    new FieldEntry { PropertyName = "PresetName", LabelRU = "Имя пресета", LabelEN = "Preset name", HintRU = "Какой пресет создал этот объект.", HintEN = "Which preset created this object.", Advanced = true },
                }
            });

            // Сюда в будущих заходах можно добавлять схемы для NovellaPlayer,
            // NovellaUIBinding (если хочется заменить его инспектор), кастомных
            // юзерских скриптов и т.п.
        }

        private static void Register(Type t, ComponentSchema schema)
        {
            if (t == null || schema == null) return;
            schema.TypeFullName = t.FullName;
            _registry[t] = schema;
        }

        public static ComponentSchema Get(Type t)
        {
            if (t == null) return null;
            return _registry.TryGetValue(t, out var s) ? s : null;
        }

        public static FieldEntry FindField(ComponentSchema schema, string propertyName)
        {
            if (schema == null || schema.Fields == null) return null;
            for (int i = 0; i < schema.Fields.Count; i++)
            {
                if (schema.Fields[i].PropertyName == propertyName) return schema.Fields[i];
            }
            return null;
        }
    }
}
