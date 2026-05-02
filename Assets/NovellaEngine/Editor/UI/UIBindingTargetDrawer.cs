// ════════════════════════════════════════════════════════════════════════════
// UIBindingTargetDrawer
//
// Если строковое поле помечено [UIBindingTarget(Kind)] и его всё-таки рисует
// дефолт-инспектор Unity (например при выборе ScriptableObject NovellaTree
// напрямую), мы показываем тот же пикер по именам что и в редакторах Forge /
// Dialogue Editor / Graph Inspector. Никаких ObjectField'ов — пользователь
// никогда не должен «таскать» элементы из иерархии.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Runtime.UI;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor.UIBindings
{
    [CustomPropertyDrawer(typeof(UIBindingTargetAttribute))]
    public class UIBindingTargetDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + 2;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[UIBindingTarget] поддерживает только string");
                return;
            }

            var attr = (UIBindingTargetAttribute)attribute;

            // Используем общий хелпер, чтобы UX был один и тот же везде.
            // Drawer работает поверх Layout-системы — поэтому обернёмся в
            // временный layout с прицельной шириной.
            EditorGUI.BeginChangeCheck();
            var oldValue = property.stringValue;

            // Простой подход: рисуем prefix-label + кнопку ниже через стандартный API.
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                                      position.width - EditorGUIUtility.labelWidth, position.height);

            EditorGUI.LabelField(labelRect, label);

            string current = property.stringValue;
            NovellaUIBinding currentBinding = !string.IsNullOrEmpty(current) ? NovellaUIBinding.FindInScene(current) : null;
            string btnLabel = currentBinding != null
                ? "📌  " + currentBinding.DisplayName
                : (string.IsNullOrEmpty(current) ? "— выбрать UI элемент —" : "⚠ binding не найден");

            if (GUI.Button(fieldRect, btnLabel, EditorStyles.popup))
            {
                ShowPickerMenu(current, attr.Kind, newId =>
                {
                    property.stringValue = newId;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
        }

        private static void ShowPickerMenu(string currentId, UIBindingKind kind, System.Action<string> onPick)
        {
            var menu = new GenericMenu();
            var all = NovellaUIBinding.FindAllInScene();

            int compatibleCount = 0;
            foreach (var b in all)
            {
                if (b == null) continue;
                bool ok = kind == UIBindingKind.Any
                    || (kind == UIBindingKind.Text   && b.GetComponent<TMPro.TMP_Text>()           != null)
                    || (kind == UIBindingKind.Button && b.GetComponent<UnityEngine.UI.Button>()    != null);
                if (!ok) continue;

                compatibleCount++;
                string folder = FolderFor(b.DetectKind());
                string item = $"{folder}/{b.DisplayName}";
                var bRef = b;
                menu.AddItem(new GUIContent(item), b.Id == currentId, () => onPick(bRef.Id));
            }

            if (compatibleCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("В сцене нет привязываемых элементов"));
                menu.AddDisabledItem(new GUIContent("Открой Кузницу UI и нажми «➕ Сделать привязываемым»"));
            }
            else
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("(очистить)"), false, () => onPick(""));
            }

            menu.ShowAsContext();
        }

        private static string FolderFor(NovellaUIBinding.BindingKind k)
        {
            switch (k)
            {
                case NovellaUIBinding.BindingKind.Text:   return "📝 Тексты";
                case NovellaUIBinding.BindingKind.Button: return "🔘 Кнопки";
                case NovellaUIBinding.BindingKind.Image:  return "🖼 Картинки";
                default: return "▣ Прочее";
            }
        }
    }
}
