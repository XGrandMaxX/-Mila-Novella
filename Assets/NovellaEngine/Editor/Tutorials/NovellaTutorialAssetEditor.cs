using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor.Tutorials
{
    [CustomEditor(typeof(NovellaTutorialAsset))]
    public class NovellaTutorialAssetEditor : UnityEditor.Editor
    {
        private static readonly Color HEADER = new Color(0.36f, 0.75f, 0.92f);
        private static readonly Color CARD_BG = new Color(0.18f, 0.18f, 0.22f);
        private static readonly Color CARD_HEADER_BG = new Color(0.22f, 0.24f, 0.30f);

        private SerializedProperty _stepsProp;
        private List<bool> _foldouts = new List<bool>();
        private int _previewStepIndex = 0;

        private void OnEnable()
        {
            _stepsProp = serializedObject.FindProperty("Steps");
            SyncFoldouts();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var asset = (NovellaTutorialAsset)target;

            DrawHeader(asset);
            DrawIdentity(asset);
            DrawStepsList(asset);
            DrawActions(asset);

            serializedObject.ApplyModifiedProperties();
        }

        // ─────────────── HEADER ───────────────

        private void DrawHeader(NovellaTutorialAsset asset)
        {
            GUILayout.Space(8);
            Rect r = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, CARD_HEADER_BG);

            // Цветной акцент сверху
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 3), HEADER);

            // Иконка
            var iconStyle = new GUIStyle(EditorStyles.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(r.x + 12, r.y + 10, 50, 50), asset.Icon, iconStyle);

            // Title + key
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 18, normal = { textColor = Color.white } };
            string lang = ToolLang.IsRU ? asset.TitleRU : asset.TitleEN;
            GUI.Label(new Rect(r.x + 70, r.y + 10, r.width - 100, 22), lang, titleStyle);

            var subStyle = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.65f, 0.85f, 1f) } };
            GUI.Label(new Rect(r.x + 70, r.y + 32, r.width - 100, 18),
                $"key: \"{asset.TutorialKey}\"  •  step #{asset.OrderIndex}  •  {asset.Steps.Count} steps", subStyle);

            GUILayout.Space(6);
        }

        // ─────────────── IDENTITY ───────────────

        private void DrawIdentity(NovellaTutorialAsset asset)
        {
            EditorGUILayout.LabelField(ToolLang.Get("Identity", "Идентификация"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("TutorialKey"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Icon"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OrderIndex"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("HostWindow"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("EN", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TitleEN"), GUIContent.none);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DescriptionEN"), GUIContent.none);
            EditorGUILayout.LabelField("RU", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TitleRU"), GUIContent.none);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DescriptionRU"), GUIContent.none);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);
        }

        // ─────────────── STEPS LIST ───────────────

        private void DrawStepsList(NovellaTutorialAsset asset)
        {
            SyncFoldouts();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📜 {ToolLang.Get("Steps", "Шаги")} ({_stepsProp.arraySize})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
            if (GUILayout.Button("+ " + ToolLang.Get("Add Step", "Добавить шаг"), GUILayout.Width(140), GUILayout.Height(22)))
            {
                _stepsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
                SyncFoldouts();
                _foldouts[_foldouts.Count - 1] = true;
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            int toRemove = -1;
            int swapA = -1, swapB = -1;

            for (int i = 0; i < _stepsProp.arraySize; i++)
            {
                var stepProp = _stepsProp.GetArrayElementAtIndex(i);
                DrawStepCard(stepProp, i, ref toRemove, ref swapA, ref swapB);
            }

            if (toRemove >= 0)
            {
                _stepsProp.DeleteArrayElementAtIndex(toRemove);
                serializedObject.ApplyModifiedProperties();
                SyncFoldouts();
            }

            if (swapA >= 0 && swapB >= 0 && swapA < _stepsProp.arraySize && swapB < _stepsProp.arraySize)
            {
                _stepsProp.MoveArrayElement(swapA, swapB);
                serializedObject.ApplyModifiedProperties();
                bool tmp = _foldouts[swapA]; _foldouts[swapA] = _foldouts[swapB]; _foldouts[swapB] = tmp;
            }
        }

        private void DrawStepCard(SerializedProperty stepProp, int idx, ref int toRemove, ref int swapA, ref int swapB)
        {
            var titleENp = stepProp.FindPropertyRelative("TitleEN");
            var titleRUp = stepProp.FindPropertyRelative("TitleRU");
            var hintProp = stepProp.FindPropertyRelative("HintStyle");
            var advProp = stepProp.FindPropertyRelative("AdvanceMode");
            var targetMode = stepProp.FindPropertyRelative("TargetMode");

            string title = ToolLang.IsRU ? titleRUp.stringValue : titleENp.stringValue;
            if (string.IsNullOrEmpty(title)) title = $"Step {idx + 1}";

            // CARD
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row
            EditorGUILayout.BeginHorizontal();

            _foldouts[idx] = EditorGUILayout.Foldout(_foldouts[idx], $"  {idx + 1}.  {title}", true, new GUIStyle(EditorStyles.foldoutHeader) { fontSize = 12 });

            // Бейджи стиля и продвижения
            DrawBadge(GetHintLabel((ETutorialHintStyle)hintProp.enumValueIndex), HintColor((ETutorialHintStyle)hintProp.enumValueIndex));
            DrawBadge(GetAdvanceLabel((ETutorialAdvanceMode)advProp.enumValueIndex), new Color(0.3f, 0.5f, 0.7f));

            GUILayout.FlexibleSpace();

            // Reorder
            EditorGUI.BeginDisabledGroup(idx == 0);
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22), GUILayout.Height(18))) { swapA = idx; swapB = idx - 1; }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(idx == _stepsProp.arraySize - 1);
            if (GUILayout.Button("▼", EditorStyles.miniButtonRight, GUILayout.Width(22), GUILayout.Height(18))) { swapA = idx; swapB = idx + 1; }
            EditorGUI.EndDisabledGroup();

            GUI.backgroundColor = new Color(0.85f, 0.4f, 0.4f);
            if (GUILayout.Button("🗑", GUILayout.Width(26), GUILayout.Height(18)))
            {
                if (EditorUtility.DisplayDialog(ToolLang.Get("Delete Step?", "Удалить шаг?"),
                    ToolLang.Get($"Delete step '{title}'?", $"Удалить шаг «{title}»?"), "Yes", "No"))
                    toRemove = idx;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            if (_foldouts[idx])
            {
                EditorGUILayout.Space(4);

                // Tabs: Текст / Цель / Стиль / Продвижение / Медиа
                DrawSectionLabel("📝 " + ToolLang.Get("Text", "Текст"));
                EditorGUILayout.PropertyField(titleENp, new GUIContent("Title (EN)"));
                EditorGUILayout.PropertyField(titleRUp, new GUIContent("Title (RU)"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("BodyEN"), new GUIContent("Body (EN)"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("BodyRU"), new GUIContent("Body (RU)"));

                EditorGUILayout.Space(8);
                DrawSectionLabel("🎯 " + ToolLang.Get("Target", "Цель"));
                EditorGUILayout.PropertyField(targetMode);
                ETutorialTargetMode mode = (ETutorialTargetMode)targetMode.enumValueIndex;

                if (mode == ETutorialTargetMode.ByVisualElementName || mode == ETutorialTargetMode.ByControlName)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("TargetName"));
                    EditorGUILayout.HelpBox(
                        mode == ETutorialTargetMode.ByVisualElementName
                            ? ToolLang.Get("Set name on the VisualElement: ve.name = \"MySaveBtn\".", "Имя VisualElement в UI Toolkit: ve.name = \"MySaveBtn\".")
                            : ToolLang.Get("Use GUI.SetNextControlName(\"MySaveBtn\") in IMGUI, then RegisterControlRect after the button.", "В IMGUI: GUI.SetNextControlName(\"MySaveBtn\"), затем NovellaTutorialResolver.RegisterControlRect."),
                        MessageType.Info);
                }

                if (mode == ETutorialTargetMode.ByReflectionField)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ReflectionFieldName"));
                    EditorGUILayout.HelpBox(ToolLang.Get(
                        "Reflection escape-hatch — for accessing private VisualElement fields of the host window. Avoid if possible.",
                        "Escape-hatch через рефлексию — для приватных полей VisualElement. Избегай по возможности."), MessageType.Warning);
                }

                if (mode == ETutorialTargetMode.ManualRect)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ManualRect"));
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ManualRectUsePercent"));
                    if (stepProp.FindPropertyRelative("ManualRectUsePercent").boolValue)
                    {
                        EditorGUILayout.HelpBox(ToolLang.Get(
                            "Percent mode (0..1): values scale with window size. Recommended over absolute pixels.",
                            "Процентный режим (0..1): значения адаптируются к размеру окна. Рекомендуется вместо пикселей."), MessageType.Info);
                    }
                }

                if (mode != ETutorialTargetMode.None && mode != ETutorialTargetMode.WholeWindow)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("TargetPadding"));
                }

                EditorGUILayout.Space(8);
                DrawSectionLabel("✨ " + ToolLang.Get("Visual Style", "Визуальный стиль"));
                EditorGUILayout.PropertyField(hintProp);
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("AccentColor"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("PanelAnchor"));

                EditorGUILayout.Space(8);
                DrawSectionLabel("🎬 " + ToolLang.Get("Media", "Медиа"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("Video"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("Image"));
                if (stepProp.FindPropertyRelative("Image").objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ImageFrameCount"));
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ImageFPS"));
                }
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "Drop a VideoClip for full-motion preview, or use a horizontal sprite-sheet (Texture2D) with FrameCount > 1 for a lightweight GIF effect.",
                    "Подключи VideoClip для полноценного видео, или используй горизонтальный спрайт-лист (Texture2D) с FrameCount > 1 для лёгкого GIF-эффекта."), MessageType.None);

                EditorGUILayout.Space(8);
                DrawSectionLabel("⏭ " + ToolLang.Get("Advance", "Продвижение"));
                EditorGUILayout.PropertyField(advProp);
                ETutorialAdvanceMode adv = (ETutorialAdvanceMode)advProp.enumValueIndex;
                if (adv == ETutorialAdvanceMode.OnUserAction || adv == ETutorialAdvanceMode.Any)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ActionTrigger"));
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("ActionTextRegex"));
                }
                if (adv == ETutorialAdvanceMode.AutoTimer || adv == ETutorialAdvanceMode.Any)
                {
                    EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("AutoAdvanceSeconds"));
                }
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("MinHoldSeconds"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("AllowSkip"));
                EditorGUILayout.PropertyField(stepProp.FindPropertyRelative("HideNextButton"));
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ─────────────── ACTIONS ───────────────

        private void DrawActions(NovellaTutorialAsset asset)
        {
            EditorGUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button("▶  " + ToolLang.Get("Test in current window", "Тест в текущем окне"), GUILayout.Height(36)))
            {
                NovellaTutorialManagerV2.StartTutorial(asset);
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(0.85f, 0.5f, 0.3f);
            if (GUILayout.Button("⏹  " + ToolLang.Get("Stop", "Стоп"), GUILayout.Height(36), GUILayout.Width(100)))
            {
                NovellaTutorialManagerV2.ForceStopTutorial();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(ToolLang.Get(
                "Tip: open the host window first (Hub / Graph / Scene Manager etc), then click Test. " +
                "All step coordinates resolve relative to the active host window.",
                "Совет: сначала открой целевое окно (Hub / Graph / Менеджер сцен и т.д.), потом нажми Тест. " +
                "Координаты шагов вычисляются относительно активного окна."),
                MessageType.None);
        }

        // ─────────────── HELPERS ───────────────

        private void SyncFoldouts()
        {
            while (_foldouts.Count < _stepsProp.arraySize) _foldouts.Add(false);
            while (_foldouts.Count > _stepsProp.arraySize) _foldouts.RemoveAt(_foldouts.Count - 1);
        }

        private static void DrawSectionLabel(string text)
        {
            var st = new GUIStyle(EditorStyles.miniBoldLabel)
            { fontSize = 11, normal = { textColor = new Color(0.55f, 0.85f, 1f) } };
            EditorGUILayout.LabelField(text, st);
        }

        private static void DrawBadge(string text, Color bg)
        {
            var st = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            var sz = st.CalcSize(new GUIContent(text));
            Rect r = GUILayoutUtility.GetRect(sz.x + 14, 17, GUILayout.Width(sz.x + 14));
            r.y += 2; r.height = 15;
            EditorGUI.DrawRect(r, bg);
            GUI.Label(r, text, st);
        }

        private static string GetHintLabel(ETutorialHintStyle s) => s switch
        {
            ETutorialHintStyle.Spotlight => "SPOT",
            ETutorialHintStyle.Outline => "OUTLINE",
            ETutorialHintStyle.PointingFinger => "FINGER",
            ETutorialHintStyle.Arrow => "ARROW",
            ETutorialHintStyle.Tooltip => "TIP",
            _ => "?"
        };

        private static Color HintColor(ETutorialHintStyle s) => s switch
        {
            ETutorialHintStyle.Spotlight => new Color(0.36f, 0.55f, 0.85f),
            ETutorialHintStyle.Outline => new Color(0.3f, 0.65f, 0.5f),
            ETutorialHintStyle.PointingFinger => new Color(0.85f, 0.55f, 0.3f),
            ETutorialHintStyle.Arrow => new Color(0.7f, 0.4f, 0.7f),
            ETutorialHintStyle.Tooltip => new Color(0.4f, 0.7f, 0.7f),
            _ => Color.gray
        };

        private static string GetAdvanceLabel(ETutorialAdvanceMode m) => m switch
        {
            ETutorialAdvanceMode.OnNextButton => "NEXT",
            ETutorialAdvanceMode.OnUserAction => "ACTION",
            ETutorialAdvanceMode.AutoTimer => "TIMER",
            ETutorialAdvanceMode.Any => "ANY",
            _ => "?"
        };
    }
}
