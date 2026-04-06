using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;
using System;

namespace NovellaEngine.Editor
{
    public class NovellaHubWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private List<NovellaStory> _stories = new List<NovellaStory>();
        private List<NovellaCharacter> _characters = new List<NovellaCharacter>();

        [MenuItem("Novella Engine/🚀 Novella Hub", false, 0)]
        public static void ShowWindow()
        {
            var win = GetWindow<NovellaHubWindow>("Novella Hub");
            win.minSize = new Vector2(950, 750);
            win.Show();
        }

        private void OnEnable()
        {
            RefreshData();
        }

        private void RefreshData()
        {
            _stories.Clear();
            string[] storyGuids = AssetDatabase.FindAssets("t:NovellaStory");
            foreach (var guid in storyGuids)
            {
                var story = AssetDatabase.LoadAssetAtPath<NovellaStory>(AssetDatabase.GUIDToAssetPath(guid));
                if (story != null) _stories.Add(story);
            }

            _characters.Clear();
            string[] charGuids = AssetDatabase.FindAssets("t:NovellaCharacter");
            foreach (var guid in charGuids)
            {
                var ch = AssetDatabase.LoadAssetAtPath<NovellaCharacter>(AssetDatabase.GUIDToAssetPath(guid));
                if (ch != null) _characters.Add(ch);
            }
            _characters = _characters.OrderBy(c => c.name).ToList();
        }

        private void OnGUI()
        {
            GUILayout.Space(15);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("⬅ " + ToolLang.Get("Welcome Screen", "К Обучению"), EditorStyles.miniButton, GUILayout.Width(130), GUILayout.Height(25)))
            {
                EditorApplication.delayCall += () => {
                    NovellaWelcomeWindow.ShowWindow();
                    Close();
                };
                GUIUtility.ExitGUI();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("✨ NOVELLA ENGINE HUB ✨", new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, normal = { textColor = new Color(0.2f, 0.7f, 1f) } });
            GUILayout.FlexibleSpace();

            string langBtnText = ToolLang.IsRU ? "EN" : "RU";
            if (GUILayout.Button(langBtnText, EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(25)))
            {
                ToolLang.Toggle();
            }
            GUILayout.Space(10);
            GUILayout.EndHorizontal();
            GUILayout.Space(15);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            GUILayout.BeginHorizontal();

            // --- ЛЕВАЯ КОЛОНКА: ИСТОРИИ ---
            GUILayout.BeginVertical(GUILayout.Width(position.width * 0.45f));
            DrawSectionHeader("📖 " + ToolLang.Get("My Stories", "Мои Истории"));

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("➕ " + ToolLang.Get("Create New Story", "Создать новую историю"), GUILayout.Height(40)))
            {
                CreateNewStory();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(10);

            NovellaStory storyToDelete = null; // ФИКС ОШИБКИ GUILAYOUT

            foreach (var story in _stories)
            {
                if (story == null) continue;

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(story.Title, EditorStyles.boldLabel);
                GUILayout.Label(string.IsNullOrEmpty(story.Description) ? "..." : story.Description, EditorStyles.wordWrappedMiniLabel);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("⚙ " + ToolLang.Get("Settings", "Настройки"), GUILayout.Height(25)))
                {
                    NovellaStorySettingsPopup.ShowWindow(story, RefreshData);
                }

                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                if (GUILayout.Button(ToolLang.Get("Open Graph", "Редактор Графа"), GUILayout.Height(25)))
                {
                    if (story.StartingChapter != null) NovellaGraphWindow.OpenGraphWindow(story.StartingChapter);
                    else Debug.LogWarning("Сначала назначьте стартовую главу (Starting Chapter)!");
                }

                GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
                if (GUILayout.Button("🗑", GUILayout.Width(30), GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog(
                        ToolLang.Get("Delete Story", "Удалить историю"),
                        ToolLang.Get($"Are you sure you want to delete '{story.Title}'?", $"Вы уверены, что хотите удалить '{story.Title}'?"),
                        ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена")))
                    {
                        storyToDelete = story;
                    }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.Space(5);
            }

            if (storyToDelete != null)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(storyToDelete));
                RefreshData();
                GUIUtility.ExitGUI(); // Спасает от ошибки Invalid GUILayout State
            }

            GUILayout.EndVertical();

            GUILayout.Space(20);

            // --- ПРАВАЯ КОЛОНКА: ПЕРСОНАЖИ И ИНСТРУМЕНТЫ ---
            GUILayout.BeginVertical(GUILayout.Width(position.width * 0.45f));

            DrawSectionHeader("🎭 " + ToolLang.Get("Characters", "Персонажи"));

            GUI.backgroundColor = new Color(0.8f, 0.5f, 0.2f);
            if (GUILayout.Button("➕ " + ToolLang.Get("Character Editor", "Редактор персонажей"), GUILayout.Height(40)))
            {
                NovellaCharacterEditor.OpenWindow();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(5);

            GUILayout.Label(ToolLang.Get($"Total Characters: {_characters.Count}", $"Всего персонажей: {_characters.Count}"), EditorStyles.miniLabel);

            GUILayout.Space(20);

            DrawSectionHeader("🛠 " + ToolLang.Get("Engine Tools", "Инструменты движка"));

            DrawToolButton("🎬",
                ToolLang.Get("Scene & UI Manager", "Менеджер Сцен и UI"),
                ToolLang.Get("Create Main Menu, Gameplay Canvas, and Setup Scenes.", "Создание сцен для игры, главного меню и настройка Canvas."),
                NovellaSceneManagerWindow.ShowWindow);

            DrawToolButton("📊",
                ToolLang.Get("Global Variables", "Глобальные Переменные"),
                ToolLang.Get("Manage numeric, text, and boolean flags for story logic.", "Настройка числовых, текстовых и булевых флагов для сюжета."),
                () => NovellaVariableEditorWindow.ShowWindow(null));

            DrawToolButton("🎨",
                ToolLang.Get("Node Colors", "Настройка Цветов Нод"),
                ToolLang.Get("Customize the visual style of your graph editor.", "Настройте визуальный стиль карточек в редакторе графа."),
                NovellaColorSettingsWindow.ShowWindow);

            DrawToolButton("🧩",
                ToolLang.Get("DLC Manager", "Менеджер Модулей (DLC)"),
                ToolLang.Get("Enable, disable, or delete installed extensions.", "Включение, отключение и полное удаление установленных модулей."),
                NovellaDLCManagerWindow.ShowWindow);


            // --- ИСПРАВЛЕНИЕ: Выделили DLC в отдельную категорию ---
            GUILayout.Space(15);
            DrawSectionHeader("🧩 " + ToolLang.Get("Installed DLCs", "Установленные DLC"));

            var wardrobeWindowType = TypeCache.GetTypesDerivedFrom<EditorWindow>().FirstOrDefault(t => t.Name == "NovellaWardrobeDatabaseWindow");
            var wardrobeNodeType = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>().FirstOrDefault(t => t.Name == "WardrobeNodeData");

            if (wardrobeWindowType != null && wardrobeNodeType != null)
            {
                bool isWardrobeEnabled = NovellaDLCSettings.Instance.IsDLCEnabled(wardrobeNodeType.FullName);

                DrawToolButton("👗",
                    ToolLang.Get("Wardrobe Database", "База Гардероба"),
                    ToolLang.Get("Manage character outfits and accessories.", "Настройка одежды и переодевания персонажей."),
                    () => {
                        var method = wardrobeWindowType.GetMethod("ShowWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        method?.Invoke(null, null);
                    },
                    isWardrobeEnabled);
            }
            else
            {
                GUILayout.Label(ToolLang.Get("No DLCs active.", "Нет активных модулей DLC."), EditorStyles.centeredGreyMiniLabel);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(20);
            if (GUILayout.Button("🔄 " + ToolLang.Get("Refresh Hub", "Обновить Хаб"), GUILayout.Height(35))) RefreshData();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private void DrawToolButton(string icon, string title, string desc, Action onClick, bool isEnabled = true)
        {
            EditorGUI.BeginDisabledGroup(!isEnabled);

            Rect rect = GUILayoutUtility.GetRect(0, 65, GUILayout.ExpandWidth(true));

            if (GUI.Button(rect, GUIContent.none))
            {
                onClick?.Invoke();
            }

            GUI.Label(new Rect(rect.x + 10, rect.y + 10, 45, 45), icon, new GUIStyle(EditorStyles.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(rect.x + 65, rect.y + 8, rect.width - 70, 20), title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });

            GUIStyle descStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            descStyle.normal.textColor = isEnabled ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f);
            GUI.Label(new Rect(rect.x + 65, rect.y + 28, rect.width - 70, 35), desc, descStyle);

            EditorGUI.EndDisabledGroup();
            GUILayout.Space(5);
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
            Rect rect = GUILayoutUtility.GetRect(100, 2);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), new Color(0.4f, 0.4f, 0.4f, 0.5f));
            GUILayout.Space(10);
        }

        private void CreateNewStory()
        {
            string baseDir = "Assets/NovellaEngine/Runtime/Data/Stories";
            if (!System.IO.Directory.Exists(baseDir))
            {
                System.IO.Directory.CreateDirectory(baseDir);
                AssetDatabase.Refresh();
            }

            string guid = System.Guid.NewGuid().ToString().Substring(0, 5);

            NovellaTree newTree = ScriptableObject.CreateInstance<NovellaTree>();
            string treePath = $"{baseDir}/Chapter_1_{guid}.asset";
            AssetDatabase.CreateAsset(newTree, treePath);

            NovellaStory newStory = ScriptableObject.CreateInstance<NovellaStory>();
            newStory.Title = "New Story";
            newStory.StartingChapter = newTree;
            string storyPath = $"{baseDir}/Story_{guid}.asset";
            AssetDatabase.CreateAsset(newStory, storyPath);

            AssetDatabase.SaveAssets();
            RefreshData();

            NovellaStorySettingsPopup.ShowWindow(newStory, RefreshData);
        }
    }

    public class NovellaStorySettingsPopup : EditorWindow
    {
        public NovellaStory Story;
        public Action OnClose;

        public static void ShowWindow(NovellaStory story, Action onClose)
        {
            var win = GetWindow<NovellaStorySettingsPopup>(true, ToolLang.Get("Story Settings", "Настройки Истории"), true);
            win.Story = story;
            win.OnClose = onClose;
            win.minSize = new Vector2(400, 430);
            win.maxSize = new Vector2(400, 430);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            if (Story == null) { Close(); return; }

            GUILayout.Space(10);
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(Story, "Edit Story");

            GUILayout.Label(ToolLang.Get("Title", "Название"), EditorStyles.boldLabel);
            Story.Title = EditorGUILayout.TextField(Story.Title);

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Description", "Описание"), EditorStyles.boldLabel);
            Story.Description = EditorGUILayout.TextArea(Story.Description, GUILayout.Height(60));

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Cover Image", "Обложка"), EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            Story.CoverImage = (Sprite)EditorGUILayout.ObjectField(Story.CoverImage, typeof(Sprite), false, GUILayout.Height(60), GUILayout.Width(60));
            if (GUILayout.Button("🖼 " + ToolLang.Get("Select from Gallery", "Выбрать из Галереи"), GUILayout.Height(60)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    if (obj is Sprite s) Story.CoverImage = s;
                    else if (obj is Texture2D t)
                    {
                        string path = AssetDatabase.GetAssetPath(t);
                        Story.CoverImage = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                    Repaint();
                }, NovellaGalleryWindow.EGalleryFilter.Image);
            }
            GUILayout.EndHorizontal();

            // ИСПРАВЛЕНИЕ: Выбор графа через Галерею
            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Starting Chapter (Graph)", "Стартовая глава (Граф)"), EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            Story.StartingChapter = (NovellaTree)EditorGUILayout.ObjectField(Story.StartingChapter, typeof(NovellaTree), false, GUILayout.Height(30));
            if (GUILayout.Button("🕸 " + ToolLang.Get("Gallery", "Галерея"), GUILayout.Width(120), GUILayout.Height(30)))
            {
                NovellaGalleryWindow.ShowWindow(obj => {
                    if (obj is NovellaTree tree) Story.StartingChapter = tree;
                    Repaint();
                }, NovellaGalleryWindow.EGalleryFilter.Graph);
            }
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(Story);
            }

            GUILayout.FlexibleSpace();
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
            if (GUILayout.Button("✔ " + ToolLang.Get("Save & Close", "Сохранить и закрыть"), new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold }, GUILayout.Height(35)))
            {
                OnClose?.Invoke();
                EditorApplication.delayCall += Close; // ФИКС ОШИБКИ GUILAYOUT
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(10);
        }
    }
}