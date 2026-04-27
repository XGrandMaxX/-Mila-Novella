using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Находит координаты целевого элемента в окне по описанию шага туториала.
    /// Все четыре стратегии возвращают Rect в координатной системе window.position
    /// (т.е. локально для GUI-отрисовки в overlay).
    /// </summary>
    public static class NovellaTutorialResolver
    {
        // Глобальный реестр имён controlName → Rect, заполняемый IMGUI-модулями
        // вручную через RegisterControlRect. Сбрасывается каждый OnGUI.
        private static readonly Dictionary<string, Rect> _controlRects = new Dictionary<string, Rect>();

        /// <summary>
        /// Вызывается из IMGUI-модуля сразу после рисования контрола, чтобы зарегистрировать его координаты.
        /// Использование: <code>GUI.SetNextControlName("MySaveBtn"); GUILayout.Button(...);
        /// NovellaTutorialResolver.RegisterControlRect("MySaveBtn", GUILayoutUtility.GetLastRect());</code>
        /// </summary>
        public static void RegisterControlRect(string controlName, Rect rect)
        {
            if (string.IsNullOrEmpty(controlName)) return;
            _controlRects[controlName] = rect;
        }

        /// <summary>
        /// Возвращает Rect цели в локальных координатах окна.
        /// Если цель не найдена — возвращает Rect.zero.
        /// </summary>
        public static Rect Resolve(NovellaTutorialStep step, EditorWindow window)
        {
            if (window == null || step == null) return Rect.zero;

            float w = window.position.width;
            float h = window.position.height;

            switch (step.TargetMode)
            {
                case ETutorialTargetMode.None:
                    return Rect.zero;

                case ETutorialTargetMode.WholeWindow:
                    return new Rect(0, 0, w, h);

                case ETutorialTargetMode.ManualRect:
                    if (step.ManualRectUsePercent)
                    {
                        return new Rect(
                            step.ManualRect.x * w,
                            step.ManualRect.y * h,
                            step.ManualRect.width * w,
                            step.ManualRect.height * h
                        );
                    }
                    return step.ManualRect;

                case ETutorialTargetMode.ByVisualElementName:
                    return ResolveByVisualElementName(window, step.TargetName, step.TargetPadding);

                case ETutorialTargetMode.ByControlName:
                    return ResolveByControlName(step.TargetName, step.TargetPadding);

                case ETutorialTargetMode.ByReflectionField:
                    return ResolveByReflectionField(window, step.ReflectionFieldName, step.TargetPadding);
            }

            return Rect.zero;
        }

        // ─────────────── ByVisualElementName: для UI Toolkit окон ───────────────

        private static Rect ResolveByVisualElementName(EditorWindow window, string elementName, float padding)
        {
            if (string.IsNullOrEmpty(elementName)) return Rect.zero;

            VisualElement root = window.rootVisualElement;
            if (root == null) return Rect.zero;

            VisualElement found = root.Q(name: elementName);
            if (found == null) return Rect.zero;

            // worldBound у VisualElement — это координаты относительно root.
            // Это и есть локальные координаты окна для GUI overlay.
            Rect r = found.worldBound;
            if (float.IsNaN(r.x) || float.IsNaN(r.y) || r.width < 1 || r.height < 1) return Rect.zero;

            return Inflate(r, padding);
        }

        // ─────────────── ByControlName: для IMGUI окон ───────────────

        private static Rect ResolveByControlName(string controlName, float padding)
        {
            if (string.IsNullOrEmpty(controlName)) return Rect.zero;
            if (!_controlRects.TryGetValue(controlName, out Rect r)) return Rect.zero;
            return Inflate(r, padding);
        }

        // ─────────────── ByReflectionField: для случаев, когда поле приватное ───────────────

        private static Rect ResolveByReflectionField(EditorWindow window, string fieldName, float padding)
        {
            if (string.IsNullOrEmpty(fieldName)) return Rect.zero;

            FieldInfo field = window.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return Rect.zero;

            object val = field.GetValue(window);

            if (val is VisualElement ve)
            {
                Rect r = ve.worldBound;
                if (float.IsNaN(r.x) || r.width < 1 || r.height < 1) return Rect.zero;
                return Inflate(r, padding);
            }

            if (val is Rect rect)
            {
                return Inflate(rect, padding);
            }

            return Rect.zero;
        }

        private static Rect Inflate(Rect r, float p)
        {
            return new Rect(r.x - p, r.y - p, r.width + p * 2f, r.height + p * 2f);
        }
    }
}
