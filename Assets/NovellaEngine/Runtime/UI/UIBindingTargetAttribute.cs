// ════════════════════════════════════════════════════════════════════════════
// UIBindingTargetAttribute
//
// Маркирует строковое поле в данных ноды как «id binding'а UI-элемента в сцене».
// В инспекторе вместо обычного TextField рисуется ObjectField, в который можно
// drag&drop'нуть GameObject из иерархии сцены. Drawer добавит NovellaUIBinding
// если его ещё нет, и сохранит в строку его стабильный Id.
//
// Параметр Kind подсказывает drawer'у какой компонент должен быть на цели —
// чтобы пользователь не мог положить, скажем, Button туда где нужен Text.
// ════════════════════════════════════════════════════════════════════════════

using System;
using UnityEngine;

namespace NovellaEngine.Runtime.UI
{
    public enum UIBindingKind
    {
        Any,
        Text,    // TMP_Text/Text-элемент — для подстановки текста
        Button,  // UnityEngine.UI.Button — для клик-кнопок
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class UIBindingTargetAttribute : PropertyAttribute
    {
        public UIBindingKind Kind;

        public UIBindingTargetAttribute(UIBindingKind kind = UIBindingKind.Any)
        {
            Kind = kind;
        }
    }
}
