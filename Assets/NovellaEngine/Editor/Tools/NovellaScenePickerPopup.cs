// ════════════════════════════════════════════════════════════════════════════
// NovellaScenePickerPopup — выбор сцены из Build Settings в виде карточек.
//
// Используется action'ом ReturnToMainMenu (и любым другим, где надо выбрать
// сцену). Раньше там был EditorGUILayout.Popup со строкой имён — некрасиво
// и не даёт никакого контекста (кто эта сцена, есть ли там нужные пресеты).
//
// Карточка показывает:
//   • имя сцены (без .unity)
//   • build-индекс
//   • статус «Enabled» / «Disabled in Build»
//   • emoji-иконку по эвристике (🏠 для menu/main, 🎮 для game/play, иначе ⬜)
//
// Также есть карточка «(auto: first build scene)» сверху — для значения «по
// умолчанию», т.е. когда юзер не хочет хардкодить конкретную сцену.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Data;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaScenePickerPopup : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private struct Entry
        {
            public string Name;          // "MainMenu" (без .unity)
            public string Path;           // "Assets/.../MainMenu.unity"
            public int BuildIndex;        // -1 если disabled в build
            public bool IsAutoSentinel;   // первая запись «(auto: first build scene)»
        }

        private string _activeName = "";
        private List<Entry> _all = new List<Entry>();
        private Action<string> _onPick; // "" = auto, иначе имя сцены
        private Vector2 _scroll;

        public static void Open(Vector2 screenPos, string activeName, Action<string> onPick)
        {
            // Single-instance.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaScenePickerPopup>())
                if (existing != null) existing.Close();

            var win = CreateInstance<NovellaScenePickerPopup>();
            win._activeName = activeName ?? "";
            win._onPick = onPick;
            win.RefreshList();

            // Ширина авто-подбирается под длину самого длинного имени сцены и пути.
            // Минимум 460, максимум 760. Высота тоже адаптивна, но в разумных рамках.
            float maxNameW = 0f;
            var measureSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            for (int i = 0; i < win._all.Count; i++)
            {
                var e = win._all[i];
                string label = e.IsAutoSentinel ? "Auto: first build scene" : e.Name + " (disabled)";
                maxNameW = Mathf.Max(maxNameW, measureSt.CalcSize(new GUIContent(label)).x);
                if (!string.IsNullOrEmpty(e.Path))
                {
                    var pathSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
                    maxNameW = Mathf.Max(maxNameW, pathSt.CalcSize(new GUIContent(e.Path)).x);
                }
            }
            float W = Mathf.Clamp(maxNameW + 120f, 460f, 760f); // +120 для иконки/паддингов
            const float H = 460f;
            win.titleContent = new GUIContent(ToolLang.Get("Pick scene", "Выбор сцены"));
            win.position = new Rect(screenPos.x - W * 0.5f, screenPos.y, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(Mathf.Max(W, 760f), H);
            win.ShowUtility();
            win.Focus();
        }

        private void RefreshList()
        {
            _all.Clear();
            // Auto-sentinel — первая карточка «(auto)».
            _all.Add(new Entry { IsAutoSentinel = true, Name = "(auto)", BuildIndex = -1 });

            int enabledIdx = 0;
            foreach (var bs in EditorBuildSettings.scenes)
            {
                if (bs == null) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(bs.path);
                _all.Add(new Entry
                {
                    Name = name,
                    Path = bs.path,
                    BuildIndex = bs.enabled ? enabledIdx++ : -1,
                });
            }
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // ─── Header ───
            Rect header = new Rect(0, 0, position.width, 32);
            EditorGUI.DrawRect(header, C_BG_RAISED);
            EditorGUI.DrawRect(new Rect(0, header.yMax - 1, position.width, 1), C_BORDER);
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 2),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f));

            var headerSt = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_TEXT_3 }
            };
            GUI.Label(new Rect(14, 0, position.width - 28, 32),
                "🎬 " + ToolLang.Get("Scenes from Build Settings", "Сцены из Build Settings"), headerSt);

            // Кнопка «Open Build Settings» справа — открывает нативное окно Unity.
            // Tooltip объясняет что это для не-Unity-юзеров: «список сцен которые
            // попадут в финальную игру». Кастомную Build Settings панель НЕ делаем
            // (нативная управляет платформами/билд-профилями/подписями — это огромный
            // функционал, реплицировать не имеет смысла).
            Rect openBtn = new Rect(position.width - 174, 4, 160, 24);
            var btnContent = new GUIContent(
                "🛠 " + ToolLang.Get("Build Settings…", "Build Settings…"),
                ToolLang.Get(
                    "Opens Unity's Build Settings — the official list of scenes that will be included in your final game. Add a scene there to make it loadable in builds.",
                    "Открывает нативные Build Settings Unity — официальный список сцен, которые попадут в финальную игру. Добавь сцену туда, чтобы её можно было загружать в билде."));
            if (GUI.Button(openBtn, btnContent, EditorStyles.miniButton))
            {
                if (!EditorApplication.ExecuteMenuItem("File/Build Profiles"))
                {
                    if (!EditorApplication.ExecuteMenuItem("File/Build Settings..."))
                    {
                        // Final fallback — открываем Project Settings (там тоже есть build platforms).
                        SettingsService.OpenProjectSettings("Project/Player");
                    }
                }
            }

            // ─── List with scroll ───
            float listY = header.yMax + 6;
            float listH = position.height - listY - 6;
            Rect viewport = new Rect(0, listY, position.width, listH);
            const float cardH = 56f, cardGap = 4f;
            float contentH = _all.Count * (cardH + cardGap);

            Rect content = new Rect(0, 0, position.width - 16, contentH);
            _scroll = GUI.BeginScrollView(viewport, _scroll, content, false, contentH > listH);

            for (int i = 0; i < _all.Count; i++)
            {
                Rect cell = new Rect(8, i * (cardH + cardGap), content.width - 16, cardH);
                DrawCard(cell, _all[i]);
            }
            GUI.EndScrollView();
        }

        private void DrawCard(Rect r, Entry e)
        {
            bool isCurrent = e.IsAutoSentinel
                ? string.IsNullOrEmpty(_activeName)
                : e.Name == _activeName;
            bool disabled = !e.IsAutoSentinel && e.BuildIndex < 0;
            Event ev = Event.current;
            bool hover = r.Contains(ev.mousePosition) && !disabled;

            Color bg = isCurrent
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f)
                : (hover ? C_BG_RAISED : C_BG);
            EditorGUI.DrawRect(r, bg);

            Color border = isCurrent ? C_ACCENT : C_BORDER;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);

            if (isCurrent)
                EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);

            // Icon.
            string icon = ResolveIcon(e);
            var iconSt = new GUIStyle(EditorStyles.label) {
                fontSize = 22, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_TEXT_1 }
            };
            GUI.Label(new Rect(r.x + 8, r.y, 40, r.height), icon, iconSt);

            // Title — с TextClipping.Clip чтобы длинные имена не вытекали за карточку.
            var titleSt = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12,
                clipping = TextClipping.Clip,
                normal = { textColor = disabled ? C_TEXT_4 : (isCurrent ? Color.white : C_TEXT_1) }
            };
            string title;
            if (e.IsAutoSentinel)
                title = ToolLang.Get("Auto: first build scene", "Авто: первая Build-сцена");
            else
                title = e.Name + (disabled ? " " + ToolLang.Get("(disabled)", "(отключена)") : "");
            GUI.Label(new Rect(r.x + 52, r.y + 4, r.width - 60, 18), title, titleSt);

            // Subtitle.
            var subSt = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 10,
                normal = { textColor = C_TEXT_3 }
            };
            string sub;
            if (e.IsAutoSentinel)
                sub = ToolLang.Get("Picks scene at build index 0 at runtime", "В рантайме грузит первую сцену из Build Settings");
            else if (disabled)
                sub = ToolLang.Get("Not in build — enable in Build Settings to use", "Нет в Build — включи через Build Settings");
            else
                sub = "📦 " + ToolLang.Get($"Build index: {e.BuildIndex}", $"Build-индекс: {e.BuildIndex}");
            GUI.Label(new Rect(r.x + 52, r.y + 22, r.width - 60, 16), sub, subSt);

            // Path (path of asset, dim).
            if (!e.IsAutoSentinel && !string.IsNullOrEmpty(e.Path))
            {
                var pathSt = new GUIStyle(EditorStyles.miniLabel) {
                    fontSize = 9,
                    normal = { textColor = C_TEXT_4 },
                    clipping = TextClipping.Clip,
                };
                GUI.Label(new Rect(r.x + 52, r.y + 36, r.width - 60, 14), e.Path, pathSt);
            }

            if (disabled) return; // disabled-карточки не кликаются.

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (ev.type == EventType.MouseDown && ev.button == 0 && hover)
            {
                _onPick?.Invoke(e.IsAutoSentinel ? "" : e.Name);
                Close();
                ev.Use();
            }
        }

        private static string ResolveIcon(Entry e)
        {
            if (e.IsAutoSentinel) return "✨";
            string n = e.Name?.ToLowerInvariant() ?? "";
            if (n.Contains("menu") || n.Contains("main") || n.Contains("home"))   return "🏠";
            if (n.Contains("game") || n.Contains("play") || n.Contains("level")) return "🎮";
            if (n.Contains("settings") || n.Contains("config"))                  return "⚙";
            if (n.Contains("credit") || n.Contains("end"))                        return "🎬";
            return "🎬";
        }
    }
}
