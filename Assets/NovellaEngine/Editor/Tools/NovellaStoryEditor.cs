using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    [CustomEditor(typeof(NovellaStory))]
    public class NovellaStoryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            NovellaStory story = (NovellaStory)target;

            serializedObject.Update();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("📖 " + ToolLang.Get("STORY BOOK", "КНИГА ИСТОРИИ"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = new Color(0.3f, 0.7f, 1f) } });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(15);

            if (story.CoverImage != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                Rect texRect = GUILayoutUtility.GetRect(200, 300, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(texRect, story.CoverImage.texture, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            else
            {
                EditorGUILayout.HelpBox(ToolLang.Get("No Cover Image assigned.", "Обложка не назначена."), MessageType.Info);
            }

            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CoverImage"), new GUIContent(ToolLang.Get("Cover Image", "Обложка")));
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.Label(ToolLang.Get("Story Details", "Детали истории"), EditorStyles.boldLabel);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"), new GUIContent(ToolLang.Get("Title", "Название")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Description"), new GUIContent(ToolLang.Get("Description", "Описание")));
            GUILayout.EndVertical();

            GUILayout.Space(15);
            GUILayout.Label(ToolLang.Get("Game Flow", "Логика Игры"), EditorStyles.boldLabel);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StartingChapter"), new GUIContent(ToolLang.Get("Starting Chapter", "Стартовая глава")));
            GUILayout.EndVertical();

            GUILayout.Space(20);

            if (story.StartingChapter != null)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
                if (GUILayout.Button(ToolLang.Get("▶ OPEN STARTING CHAPTER", "▶ ОТКРЫТЬ СТАРТОВУЮ ГЛАВУ"), GUILayout.Height(40)))
                {
                    NovellaGraphWindow.OpenGraphWindow(story.StartingChapter);
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Assign a Starting Chapter to begin.", "Назначьте стартовую главу, чтобы начать."), MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}