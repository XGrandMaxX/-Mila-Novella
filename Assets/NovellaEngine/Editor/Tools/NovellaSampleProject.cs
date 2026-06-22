using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using NovellaEngine.Runtime;

namespace NovellaEngine.Editor
{
    // Собирает играбельный стартовый проект для только что созданной истории:
    // применяет Gameplay-пресет к сцене, привязывает NovellaPlayer к главе новой
    // истории, создаёт пример персонажа и граф Dialogue → End.
    //
    // Вызывается из Hub («Новый проект» в DashboardModule). Отдельного окна НЕТ —
    // всё живёт внутри Hub. Логика переехала сюда из бывшего standalone-визарда.
    public static class NovellaSampleProject
    {
        // Полная настройка для свежесозданной истории. story уже на диске,
        // story.StartingChapter — её (пустой) граф.
        public static void SetUpSampleProject(NovellaStory story)
        {
            if (story == null) return;
            var tree = story.StartingChapter;
            if (tree == null) return;

            try
            {
                // 1. Сцена: Canvas + NovellaPlayer + окно диалога (со ScrollView)
                //    + контейнер персонажей. Тестированный пресет.
                NovellaSceneManagerModule.ApplyGameplayPresetToActiveScene();

                var player = UnityEngine.Object.FindFirstObjectByType<NovellaPlayer>();
                if (player != null)
                {
                    // Явно привязываем игрока к ЭТОЙ истории (пресет мог
                    // привязать другой/старый стартовый граф).
                    Undo.RecordObject(player, "Wire StoryTree");
                    player.StoryTree = tree;
                    EditorUtility.SetDirty(player);
                }

                // 2. Пример персонажа (без спрайта → голос за кадром).
                var hero = CreateSampleCharacter();

                // 3. Граф-сэмпл — только если глава пустая (не затираем работу).
                if (tree.Nodes != null && tree.Nodes.Count == 0)
                    PopulateSampleGraph(tree, hero);

                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (player != null)
                {
                    Selection.activeGameObject = player.gameObject;
                    EditorGUIUtility.PingObject(player.gameObject);
                }

                EditorUtility.DisplayDialog(
                    ToolLang.Get("Project ready!", "Проект готов!"),
                    ToolLang.Get(
                        "Your starter novella is set up. Press Play ▶ to see it run.\n\nThen open the story graph to write your story, and the Character Editor to give your character a sprite.",
                        "Стартовая новелла настроена. Жми Play ▶ чтобы увидеть как работает.\n\nПотом открой граф истории чтобы писать сюжет, и Редактор персонажей чтобы дать персонажу спрайт."),
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Setup error", "Ошибка настройки"), e.Message, "OK");
            }
        }

        private static NovellaCharacter CreateSampleCharacter()
        {
            string dir = "Assets/NovellaEngine/Generated";
            EnsureFolder(dir);

            var ch = ScriptableObject.CreateInstance<NovellaCharacter>();
            ch.CharacterID    = "Hero_" + Guid.NewGuid().ToString().Substring(0, 5);
            ch.DisplayName_EN = "Hero";
            ch.DisplayName_RU = "Герой";
            ch.ThemeColor     = NovellaSettingsModule.GetAccentColor();
            ch.BaseLayers     = new List<CharacterLayer>();

            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/Hero.asset");
            AssetDatabase.CreateAsset(ch, path);
            return ch;
        }

        // Минимальный играбельный граф: Dialogue (3 реплики, EN+RU) → End.
        private static void PopulateSampleGraph(NovellaTree tree, NovellaCharacter hero)
        {
            var dlg = new DialogueNodeData
            {
                NodeID = "Dialogue_" + Guid.NewGuid().ToString().Substring(0, 5),
                NodeTitle = ToolLang.Get("Intro", "Вступление"),
                GraphPosition = new Vector2(360, 220),
            };
            dlg.ActiveCharacters.Add(new CharacterInDialogue
            {
                CharacterAsset = hero,
                Plane = ECharacterPlane.Speaker,
                PositionPreset = ECharacterPosition.Center,
                Scale = 1f,
                Emotion = "Default",
            });

            dlg.DialogueLines.Add(MakeLine(hero,
                en: "Hi! I'm Hero. Welcome to your very first visual novel.",
                ru: "Привет! Я Герой. Добро пожаловать в твою первую новеллу."));
            dlg.DialogueLines.Add(MakeLine(hero,
                en: "Everything here was set up for you — the scene, me, and this dialogue.",
                ru: "Всё здесь настроено за тебя — сцена, я и этот диалог."));
            dlg.DialogueLines.Add(MakeLine(hero,
                en: "Open the graph to write your story, and the Character Editor to give me a sprite. Have fun!",
                ru: "Открой граф чтобы писать свою историю, и Редактор персонажей чтобы дать мне спрайт. Удачи!"));

            var end = new EndNodeData
            {
                NodeID = "End_" + Guid.NewGuid().ToString().Substring(0, 5),
                NodeTitle = ToolLang.Get("The End", "Конец"),
                GraphPosition = new Vector2(700, 220),
                EndAction = EEndAction.ReturnToMainMenu,
            };

            dlg.NextNodeID = end.NodeID;

            tree.Nodes.Add(dlg);
            tree.Nodes.Add(end);
            tree.RootNodeID = dlg.NodeID;
            EditorUtility.SetDirty(tree);
        }

        private static DialogueLine MakeLine(NovellaCharacter speaker, string en, string ru)
        {
            var line = new DialogueLine { Speaker = speaker, Mood = "Default" };
            line.LocalizedPhrase.SetText("EN", en);
            line.LocalizedPhrase.SetText("RU", ru);
            return line;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
