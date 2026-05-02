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
                NovellaUIPickerWindow.Open(label.text, attr.Kind, current, newId =>
                {
                    property.stringValue = newId;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
        }

    }
}
