// ════════════════════════════════════════════════════════════════════════════
// UIBindingTargetDrawer
//
// Рисует поле помеченное [UIBindingTarget(...)] как ObjectField для GameObject:
//   • drag&drop GameObject из иерархии — Drawer добавляет NovellaUIBinding если
//     его не было и сохраняет в строку его стабильный Id;
//   • если поле пустое — отображает «Drag UI element here»;
//   • если выбран binding с подходящим компонентом (Text/Button по Kind) —
//     рисует мини-плашку с именем элемента и значком ✓.
//
// Так пользователь работает с UI как с обычными ассетами — никаких ручных
// строковых ID, никаких «магических» полей.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    [CustomPropertyDrawer(typeof(UIBindingTargetAttribute))]
    public class UIBindingTargetDrawer : PropertyDrawer
    {
        private const float ROW_H = 18f;
        private const float HINT_H = 14f;
        private const float SPACE = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ROW_H + SPACE + HINT_H;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[UIBindingTarget] поддерживает только string");
                return;
            }

            var attr = (UIBindingTargetAttribute)attribute;
            string currentId = property.stringValue;

            // Найдём текущий binding (если строка непустая) — для подсказки имени
            // элемента и проверки kind.
            NovellaUIBinding currentBinding = null;
            if (!string.IsNullOrEmpty(currentId)) currentBinding = NovellaUIBinding.FindInScene(currentId);

            Rect rowRect = new Rect(position.x, position.y, position.width, ROW_H);
            Rect hintRect = new Rect(position.x, position.y + ROW_H + SPACE, position.width, HINT_H);

            // Сам ObjectField — принимает GameObject.
            EditorGUI.BeginChangeCheck();
            GameObject newGo = (GameObject)EditorGUI.ObjectField(rowRect, label,
                currentBinding != null ? currentBinding.gameObject : null, typeof(GameObject), true);

            if (EditorGUI.EndChangeCheck())
            {
                if (newGo == null)
                {
                    property.stringValue = "";
                    property.serializedObject.ApplyModifiedProperties();
                }
                else
                {
                    // Проверяем что компонент нужного типа на нём есть.
                    if (!IsKindCompatible(newGo, attr.Kind))
                    {
                        EditorUtility.DisplayDialog(
                            "UI Binding",
                            attr.Kind == UIBindingKind.Text
                                ? "Этому полю нужен текстовый элемент (TMP_Text). На выбранном объекте его нет."
                                : attr.Kind == UIBindingKind.Button
                                    ? "Этому полю нужна кнопка (UnityEngine.UI.Button). На выбранном объекте её нет."
                                    : "Объект не подходит.",
                            "OK");
                        return;
                    }

                    var binding = NovellaUIBinding.GetOrAdd(newGo);
                    if (binding != null)
                    {
                        property.stringValue = binding.Id;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }
            }

            // Hint: показываем имя + статус.
            var hintStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            hintStyle.normal.textColor = new Color(0.62f, 0.63f, 0.69f);

            string hint;
            if (string.IsNullOrEmpty(currentId)) hint = "↪ перетащи UI элемент из сцены";
            else if (currentBinding == null) hint = $"⚠ binding '{currentId.Substring(0, 6)}…' не найден в сцене";
            else hint = $"✓ {GetPathOf(currentBinding.gameObject)}";

            EditorGUI.LabelField(hintRect, hint, hintStyle);
        }

        private static bool IsKindCompatible(GameObject go, UIBindingKind kind)
        {
            if (go == null) return false;
            switch (kind)
            {
                case UIBindingKind.Text:   return go.GetComponent<TMP_Text>() != null;
                case UIBindingKind.Button: return go.GetComponent<Button>() != null;
                case UIBindingKind.Any:
                default:                   return true;
            }
        }

        // "Canvas/Panel/HpLabel" — короткое читаемое имя для подсказки.
        private static string GetPathOf(GameObject go)
        {
            if (go == null) return "(null)";
            var t = go.transform;
            string p = t.name;
            // Идём максимум на 2 уровня вверх — чтобы не получить полный путь
            // через половину иерархии и не сломать ширину инспектора.
            if (t.parent != null) p = t.parent.name + "/" + p;
            if (t.parent != null && t.parent.parent != null) p = t.parent.parent.name + "/" + p;
            return p;
        }
    }
}
