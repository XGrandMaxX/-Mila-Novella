// ════════════════════════════════════════════════════════════════════════════
// NovellaUIBindingEditor
//
// Кастомный инспектор для NovellaUIBinding. Делает работу с компонентом
// удобной без запоминания строк:
//   • плашка-бейдж в шапке: какой UI-компонент обнаружен (Text/Button/Image)
//     и его сокращённый ID;
//   • поле LocalizationKey + кнопка «🔑 Выбрать…» — выпадающее меню со всеми
//     ключами активной таблицы локализации;
//   • поле BoundVariable + кнопка «📊 Выбрать…» — переменные из
//     NovellaVariableSettings;
//   • поле OnClickGotoNodeId + кнопка «➡ Выбрать…» — ноды активной истории.
//
// Когда автокомплит-источник пуст (нет таблицы / нет переменных / нет графа),
// меню показывает disabled-пункт с подсказкой что сделать.
// ════════════════════════════════════════════════════════════════════════════

using System.IO;
using NovellaEngine.Data;
using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    [CustomEditor(typeof(NovellaUIBinding))]
    public class NovellaUIBindingEditor : UnityEditor.Editor
    {
        private SerializedProperty _localizationKey;
        private SerializedProperty _boundVariable;
        private SerializedProperty _onClickGoto;

        private void OnEnable()
        {
            _localizationKey = serializedObject.FindProperty(nameof(NovellaUIBinding.LocalizationKey));
            _boundVariable   = serializedObject.FindProperty(nameof(NovellaUIBinding.BoundVariable));
            _onClickGoto     = serializedObject.FindProperty(nameof(NovellaUIBinding.OnClickGotoNodeId));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var binding = (NovellaUIBinding)target;
            DrawHeaderBadge(binding);

            EditorGUILayout.Space(6);

            // Localization key + chooser
            DrawFieldWithPicker(
                _localizationKey,
                "Ключ локализации",
                "🔑 Выбрать…",
                () => ShowLocalizationKeyMenu());

            // Bound variable + chooser
            DrawFieldWithPicker(
                _boundVariable,
                "Переменная (для {var})",
                "📊 Выбрать…",
                () => ShowVariableMenu());

            // Click goto + chooser
            bool hasButton = binding.GetComponent<Button>() != null;
            using (new EditorGUI.DisabledScope(!hasButton))
            {
                DrawFieldWithPicker(
                    _onClickGoto,
                    hasButton ? "Перейти на ноду по клику" : "Перейти на ноду по клику (нужен Button)",
                    "➡ Выбрать…",
                    () => ShowNodeMenu());
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ─── Header badge ───────────────────────────────────────────────────────

        private static void DrawHeaderBadge(NovellaUIBinding b)
        {
            string kind = "—";
            if (b.GetComponent<TMP_Text>() != null) kind = "📝 Text";
            else if (b.GetComponent<Button>() != null) kind = "🔘 Button";
            else if (b.GetComponent<Image>() != null) kind = "🖼 Image";

            string idShort = string.IsNullOrEmpty(b.Id) ? "(no id)" : "id: " + b.Id.Substring(0, Mathf.Min(8, b.Id.Length));

            var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.18f, 0.24f, 0.45f));

            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            GUI.Label(new Rect(rect.x + 10, rect.y + 7, rect.width - 20, 18), kind);

            var idStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            idStyle.normal.textColor = new Color(0.62f, 0.63f, 0.69f);
            GUI.Label(new Rect(rect.x + 10, rect.y + 7, rect.width - 20, 18), idShort, idStyle);
        }

        // ─── Field helper ───────────────────────────────────────────────────────

        private static void DrawFieldWithPicker(SerializedProperty prop, string label, string btnLabel, System.Action onPick)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop, new GUIContent(label));
            if (GUILayout.Button(btnLabel, EditorStyles.miniButton, GUILayout.Width(110), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                onPick?.Invoke();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Picker menus ───────────────────────────────────────────────────────

        private void ShowLocalizationKeyMenu()
        {
            var menu = new GenericMenu();
            var table = NovellaLocalizationManager.Table;
            if (table == null || table.Entries == null || table.Entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Таблица пуста или не назначена"));
                menu.AddDisabledItem(new GUIContent("Открой Settings → Open Translation Editor"));
            }
            else
            {
                foreach (var entry in table.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    string key = entry.Key;
                    menu.AddItem(new GUIContent(key.Replace("/", "\\")), _localizationKey.stringValue == key, () =>
                    {
                        _localizationKey.stringValue = key;
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("(очистить)"), false, () =>
                {
                    _localizationKey.stringValue = "";
                    serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        private void ShowVariableMenu()
        {
            var menu = new GenericMenu();
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null || settings.Variables == null || settings.Variables.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Переменные не заданы"));
                menu.AddDisabledItem(new GUIContent("Открой Hub → Variables и добавь хотя бы одну"));
            }
            else
            {
                foreach (var v in settings.Variables)
                {
                    if (string.IsNullOrEmpty(v.Name)) continue;
                    string varName = v.Name;
                    menu.AddItem(new GUIContent($"{varName}    ({v.Type})"), _boundVariable.stringValue == varName, () =>
                    {
                        _boundVariable.stringValue = varName;
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("(очистить)"), false, () =>
                {
                    _boundVariable.stringValue = "";
                    serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }

        private void ShowNodeMenu()
        {
            var menu = new GenericMenu();

            // Берём активную историю из EditorPrefs (как Hub).
            string activeStoryGuid = EditorPrefs.GetString("Novella_ActiveStoryGuid", "");
            NovellaTree tree = null;
            if (!string.IsNullOrEmpty(activeStoryGuid))
            {
                var p = AssetDatabase.GUIDToAssetPath(activeStoryGuid);
                var story = AssetDatabase.LoadAssetAtPath<NovellaStory>(p);
                if (story != null) tree = story.StartingChapter;
            }

            if (tree == null || tree.Nodes == null || tree.Nodes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Активная история не выбрана / нет нод"));
                menu.AddDisabledItem(new GUIContent("Открой Hub → выбери историю"));
            }
            else
            {
                foreach (var n in tree.Nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.NodeID)) continue;
                    string title = string.IsNullOrEmpty(n.NodeTitle) ? n.NodeType.ToString() : n.NodeTitle;
                    string label = $"{n.NodeType}    ({title})";
                    string nodeId = n.NodeID;
                    menu.AddItem(new GUIContent(label), _onClickGoto.stringValue == nodeId, () =>
                    {
                        _onClickGoto.stringValue = nodeId;
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("(очистить)"), false, () =>
                {
                    _onClickGoto.stringValue = "";
                    serializedObject.ApplyModifiedProperties();
                });
            }
            menu.ShowAsContext();
        }
    }
}
