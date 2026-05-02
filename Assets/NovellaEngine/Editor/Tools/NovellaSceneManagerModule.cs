using NovellaEngine.Data;
using NovellaEngine.Runtime;
using NovellaEngine.Runtime.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NovellaEngine.Editor
{
    // ─── ВСПЛЫВАЮЩЕЕ ОКНО ПЕРЕИМЕНОВАНИЯ ───
    public class NovellaRenamePopup : EditorWindow
    {
        public string NewName = "";
        public System.Action<string> OnConfirm;

        public static void ShowWindow(string currentName, System.Action<string> onConfirm)
        {
            var w = GetWindow<NovellaRenamePopup>(true, ToolLang.Get("Rename Scene", "Переименовать сцену"), true);
            w.NewName = currentName;
            w.OnConfirm = onConfirm;
            w.minSize = new Vector2(300, 130);
            w.maxSize = new Vector2(300, 130);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.102f, 0.106f, 0.149f));

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.BeginVertical();

            var st = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = new Color(0.925f, 0.925f, 0.957f);
            GUILayout.Label(ToolLang.Get("New scene name:", "Новое имя сцены:"), st);
            GUILayout.Space(8);

            GUI.SetNextControlName("RenameField");

            Rect r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.075f, 0.078f, 0.106f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), new Color(0.165f, 0.176f, 0.243f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), new Color(0.165f, 0.176f, 0.243f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), new Color(0.165f, 0.176f, 0.243f));
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), new Color(0.165f, 0.176f, 0.243f));

            var tfStyle = new GUIStyle(EditorStyles.textField) { fontSize = 12, padding = new RectOffset(8, 8, 6, 6) };
            tfStyle.normal.background = null; tfStyle.focused.background = null;
            tfStyle.normal.textColor = Color.white; tfStyle.focused.textColor = Color.white;

            NewName = EditorGUI.TextField(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), NewName, tfStyle);

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(ToolLang.Get("Cancel", "Отмена"), GUILayout.Height(26))) { Close(); }
            GUILayout.Space(8);

            var oldCol = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.36f, 0.75f, 0.92f);
            if (GUILayout.Button(ToolLang.Get("Rename", "Переименовать"), GUILayout.Height(26)))
            {
                OnConfirm?.Invoke(NewName);
                Close();
            }
            GUI.backgroundColor = oldCol;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(16);
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl("RenameField");
            }

            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                OnConfirm?.Invoke(NewName);
                Close();
                Event.current.Use();
            }
        }
    }

    /// <summary>
    /// Scene Manager — переписан под единый стиль Novella Studio.
    /// </summary>
    public class NovellaSceneManagerModule : INovellaStudioModule
    {
        public string ModuleName => ToolLang.Get("Scenes & Menu", "Сцены и Меню");
        public string ModuleIcon => "🎬";

        [MenuItem("Tools/Novella Engine/Scene Manager")]
        public static void ShowWindow()
        {
            NovellaHubWindow.ShowWindow();
            if (NovellaHubWindow.Instance != null)
            {
                NovellaHubWindow.Instance.SwitchToModule(2);
            }
        }

        // ─── Палитра — все основные цвета динамические, из NovellaSettingsModule ───
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();
        private static readonly Color C_OK = new Color(0.48f, 0.81f, 0.62f);
        private static readonly Color C_DANGER = new Color(0.85f, 0.32f, 0.32f);
        private static readonly Color C_WARN = new Color(0.96f, 0.76f, 0.43f);
        private static readonly Color C_PURPLE = new Color(0.63f, 0.49f, 1f);
        private static readonly Color C_MINT = new Color(0.48f, 0.81f, 0.62f);

        private const int MAX_DROPDOWN_SCENES = 12;

        private const string MARKER_MAINMENU = "[Novella]_MainMenuPanel";
        private const string MARKER_GAMEPLAY = "[Novella]_GameplayPanel";

        private enum AppliedPreset { None, MainMenu, Gameplay }

        private struct SceneRow
        {
            public string Path;
            public string Name;
            public bool InBuild;
            public bool BuildEnabled;
            public int BuildIndex;
            public bool IsOpen;
            public long FileSize;
            public System.DateTime LastEdited;
        }

        private EditorWindow _window;
        private List<SceneRow> _allScenes = new List<SceneRow>();

        private List<string> _selectedPaths = new List<string>();
        private string _lastClickedPath;
        private List<string> _currentVisualList = new List<string>();

        private string _searchQuery = "";
        private Vector2 _listScroll;
        private Vector2 _detailsScroll;

        private float _sidebarWidth = 380f;
        private bool _initialized;

        private int _stagedBuildIndex = -1;
        private string _editingIndexScenePath = null;

        private string _highlightScenePath = null;
        private double _highlightTime = 0;
        private const double SCENE_HIGHLIGHT_DURATION = 0.8;


        private bool _isEditingName = false;
        private string _editingNamePath = null;
        private string _stagedName = "";
        private bool _focusInlineEdit = false;

        // Кэш скриншотов сцен
        private Dictionary<string, Texture2D> _thumbnails = new Dictionary<string, Texture2D>();

        public void OnEnable(EditorWindow hostWindow)
        {
            _window = hostWindow;
            EditorSceneManager.sceneOpened += OnSceneEvent;
            EditorSceneManager.sceneClosed += OnSceneClosedEvent;
            RefreshScenes();
        }

        public void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneEvent;
            EditorSceneManager.sceneClosed -= OnSceneClosedEvent;
        }

        private void OnSceneEvent(Scene s, OpenSceneMode m) { RefreshScenes(); _window?.Repaint(); }
        private void OnSceneClosedEvent(Scene s) { RefreshScenes(); _window?.Repaint(); }

        private void HandleSceneClick(SceneRow sc, Event e)
        {
            if (e.shift && !string.IsNullOrEmpty(_lastClickedPath) && _currentVisualList.Contains(_lastClickedPath))
            {
                int startIdx = _currentVisualList.IndexOf(_lastClickedPath);
                int endIdx = _currentVisualList.IndexOf(sc.Path);
                int min = Mathf.Min(startIdx, endIdx);
                int max = Mathf.Max(startIdx, endIdx);

                _selectedPaths.Clear();
                for (int i = min; i <= max; i++)
                {
                    _selectedPaths.Add(_currentVisualList[i]);
                }
            }
            else if (e.control || e.command)
            {
                if (_selectedPaths.Contains(sc.Path)) _selectedPaths.Remove(sc.Path);
                else _selectedPaths.Add(sc.Path);
                _lastClickedPath = sc.Path;
            }
            else
            {
                _selectedPaths.Clear();
                _selectedPaths.Add(sc.Path);
                _lastClickedPath = sc.Path;
            }

            GUI.FocusControl(null);
            _window?.Repaint();
        }

        public void DrawGUI(Rect position)
        {
            if (!_initialized) { RefreshScenes(); _initialized = true; }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            Rect sideRect = new Rect(0, 0, _sidebarWidth, position.height);
            Rect detailsRect = new Rect(_sidebarWidth, 0, position.width - _sidebarWidth, position.height);

            DrawSidebar(sideRect);
            EditorGUI.DrawRect(new Rect(sideRect.xMax - 1, 0, 1, position.height), new Color(0, 0, 0, 0.5f));

            if (_selectedPaths.Count == 1)
            {
                DrawDetails(detailsRect);
            }
            else if (_selectedPaths.Count > 1)
            {
                DrawMultiSelectState(detailsRect);
            }
            else
            {
                DrawEmptyState(detailsRect);
            }
        }

        private void RefreshScenes()
        {
            _allScenes.Clear();

            string[] guids = AssetDatabase.FindAssets("t:Scene");
            var buildScenes = EditorBuildSettings.scenes;
            string activeScenePath = EditorSceneManager.GetActiveScene().path;

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.StartsWith("Assets/")) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                int buildIdx = -1;
                bool inBuild = false, enabled = false;
                for (int i = 0; i < buildScenes.Length; i++)
                {
                    if (buildScenes[i].path == path)
                    {
                        inBuild = true;
                        enabled = buildScenes[i].enabled;
                        buildIdx = i;
                        break;
                    }
                }

                long size = 0;
                System.DateTime edited = System.DateTime.MinValue;
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists) { size = fi.Length; edited = fi.LastWriteTime; }
                }
                catch { }

                _allScenes.Add(new SceneRow
                {
                    Path = path,
                    Name = name,
                    InBuild = inBuild,
                    BuildEnabled = enabled,
                    BuildIndex = buildIdx,
                    IsOpen = activeScenePath == path,
                    FileSize = size,
                    LastEdited = edited,
                });
            }

            _allScenes = _allScenes
                .OrderByDescending(s => s.IsOpen)
                .ThenByDescending(s => s.InBuild)
                .ThenBy(s => s.InBuild ? s.BuildIndex : int.MaxValue)
                .ThenBy(s => s.Name)
                .ToList();

            _selectedPaths.RemoveAll(p => !_allScenes.Any(s => s.Path == p));
            if (_selectedPaths.Count == 0 && _allScenes.Count > 0)
            {
                _selectedPaths.Add(_allScenes[0].Path);
                _lastClickedPath = _allScenes[0].Path;
            }
        }

        private void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_SIDE);

            Rect head = new Rect(rect.x, rect.y, rect.width, 56);
            EditorGUI.DrawRect(new Rect(rect.x, head.yMax - 1, rect.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            t.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(rect.x + 16, rect.y + 12, rect.width - 32, 18), ToolLang.Get("Scenes", "Сцены"), t);

            var s = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            s.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(rect.x + 16, rect.y + 30, rect.width - 32, 14),
                string.Format(ToolLang.Get("{0} in your project", "{0} в проекте"), _allScenes.Count), s);

            Rect newBtn = new Rect(rect.x + 14, rect.y + 64, rect.width - 28, 32);
            GUI.backgroundColor = C_ACCENT;
            if (GUI.Button(newBtn, "＋ " + ToolLang.Get("New scene", "Новая сцена")))
            {
                EditorApplication.delayCall += CreateNewScene;
            }
            GUI.backgroundColor = Color.white;

            Rect searchRect = new Rect(rect.x + 14, rect.y + 102, rect.width - 28, 28);
            EditorGUI.DrawRect(searchRect, C_BG_PRIMARY);
            DrawRectBorder(searchRect, C_BORDER);

            var st = new GUIStyle(EditorStyles.textField);
            st.normal.background = null; st.focused.background = null;
            st.normal.textColor = C_TEXT_1; st.focused.textColor = C_TEXT_1;
            st.fontSize = 11; st.padding = new RectOffset(26, 8, 6, 6);
            _searchQuery = GUI.TextField(searchRect, _searchQuery, st);

            if (string.IsNullOrEmpty(_searchQuery))
            {
                var ph = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                ph.normal.textColor = C_TEXT_4;
                GUI.Label(new Rect(searchRect.x + 6, searchRect.y, searchRect.width, searchRect.height),
                    "🔍  " + ToolLang.Get("Search…", "Поиск…"), ph);
            }

            Rect listRect = new Rect(rect.x, rect.y + 140, rect.width, rect.height - 140);
            GUILayout.BeginArea(listRect);

            string sq = (_searchQuery ?? "").ToLowerInvariant();
            var filtered = _allScenes.Where(sc =>
                string.IsNullOrEmpty(sq) ||
                sc.Name.ToLowerInvariant().Contains(sq) ||
                sc.Path.ToLowerInvariant().Contains(sq)).ToList();

            var inBuild = filtered.Where(sc => sc.InBuild).ToList();
            var notInBuild = filtered.Where(sc => !sc.InBuild).ToList();

            _listScroll = GUILayout.BeginScrollView(_listScroll);
            _currentVisualList.Clear();

            if (inBuild.Count > 0)
            {
                DrawCategoryLabel(ToolLang.Get("IN BUILD SETTINGS", "В НАСТРОЙКАХ СБОРКИ"));
                foreach (var sc in inBuild)
                {
                    _currentVisualList.Add(sc.Path);
                    DrawSceneRow(sc);
                }
            }
            if (notInBuild.Count > 0)
            {
                GUILayout.Space(8);
                DrawCategoryLabel(ToolLang.Get("NOT IN BUILD", "НЕ В СБОРКЕ"));
                foreach (var sc in notInBuild)
                {
                    _currentVisualList.Add(sc.Path);
                    DrawSceneRow(sc);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCategoryLabel(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold, padding = new RectOffset(14, 0, 8, 4) };
            st.normal.textColor = C_TEXT_4;
            GUILayout.Label(text, st);
        }

        private void DrawSceneRow(SceneRow sc)
        {
            bool active = _selectedPaths.Contains(sc.Path);

            Rect r = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            r.x += 8; r.width -= 16;

            bool hover = r.Contains(Event.current.mousePosition);
            if (active) EditorGUI.DrawRect(r, C_BG_RAISED);
            else if (hover) EditorGUI.DrawRect(r, new Color(C_BG_RAISED.r, C_BG_RAISED.g, C_BG_RAISED.b, 0.6f));
            if (active) EditorGUI.DrawRect(new Rect(r.x, r.y + 4, 2, r.height - 8), C_ACCENT);

            if (_highlightScenePath == sc.Path)
            {
                double elapsed = EditorApplication.timeSinceStartup - _highlightTime;
                if (elapsed < SCENE_HIGHLIGHT_DURATION)
                {
                    float alpha = 1f - (float)(elapsed / SCENE_HIGHLIGHT_DURATION);
                    EditorGUI.DrawRect(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, alpha * 0.35f));
                    _window?.Repaint();
                }
                else
                {
                    _highlightScenePath = null;
                }
            }

            Rect iconR = new Rect(r.x + 10, r.y + 11, 28, 28);
            EditorGUI.DrawRect(iconR, C_BG_PRIMARY);
            DrawRectBorder(iconR, C_BORDER);
            string statusIcon = sc.IsOpen ? "▶" : (sc.InBuild ? "●" : "○");
            Color iconCol = sc.IsOpen ? C_ACCENT : (sc.InBuild ? C_OK : C_TEXT_4);
            var iconStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            iconStyle.normal.textColor = iconCol;
            GUI.Label(iconR, statusIcon, iconStyle);

            string pillText; Color pillBg, pillFg;
            if (sc.IsOpen)
            {
                pillText = ToolLang.Get("OPEN", "ОТКРЫТА") + (sc.InBuild ? $" [{sc.BuildIndex}]" : "");
                pillBg = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.13f);
                pillFg = C_ACCENT;
            }
            else if (sc.InBuild)
            {
                pillText = ToolLang.Get("BUILD", "В СБОРКЕ") + $" [{sc.BuildIndex}]";
                pillBg = new Color(C_OK.r, C_OK.g, C_OK.b, 0.13f);
                pillFg = C_OK;
            }
            else
            {
                pillText = ToolLang.Get("OUT", "ВНЕ СБОРКИ");
                pillBg = new Color(C_DANGER.r, C_DANGER.g, C_DANGER.b, 0.13f);
                pillFg = C_DANGER;
            }

            var pillStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold };
            pillStyle.normal.textColor = pillFg;
            float pw = pillStyle.CalcSize(new GUIContent(pillText)).x + 12;
            Rect pillR = new Rect(r.xMax - pw - 6, r.y + 17, pw, 16);
            EditorGUI.DrawRect(pillR, pillBg);
            GUI.Label(pillR, pillText, pillStyle);

            float textStartX = iconR.xMax + 8;
            float maxTextWidth = r.width - iconR.width - pw - 24;

            var nameStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            string displayName = sc.Name;

            if (sc.InBuild && sc.BuildIndex == 0)
            {
                displayName = "★ " + displayName;
                nameStyle.normal.textColor = active ? new Color(0.96f, 0.86f, 0.53f) : new Color(0.96f, 0.76f, 0.43f);
            }
            else
            {
                nameStyle.normal.textColor = active ? C_TEXT_1 : C_TEXT_2;
            }

            string truncatedName = TruncateForWidth(displayName, nameStyle, maxTextWidth);
            GUI.Label(new Rect(textStartX, r.y + 7, maxTextWidth, 16), truncatedName, nameStyle);

            var pathStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            pathStyle.normal.textColor = C_TEXT_4;
            string shortP = sc.Path.Replace("Assets/", "");
            string truncatedPath = TruncateForWidth(shortP, pathStyle, maxTextWidth);
            GUI.Label(new Rect(textStartX, r.y + 24, maxTextWidth, 14), truncatedPath, pathStyle);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0 && Event.current.clickCount == 2)
                {
                    var capturedSc = sc;
                    EditorApplication.delayCall += () => OpenScene(capturedSc);
                }
                else
                {
                    HandleSceneClick(sc, Event.current);
                }
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && hover) _window?.Repaint();
        }

        // ═════════════════════════════════════════════════════════
        // ПРАВАЯ КОЛОНКА — детали сцены
        // ═════════════════════════════════════════════════════════

        private void DrawDetails(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);

            if (_selectedPaths.Count != 1) return;
            string currentPath = _selectedPaths[0];

            SceneRow? selectedNullable = null;
            foreach (var sc in _allScenes) if (sc.Path == currentPath) { selectedNullable = sc; break; }

            if (selectedNullable == null)
            {
                DrawEmptyState(rect);
                return;
            }

            SceneRow selected = selectedNullable.Value;

            Rect topR = new Rect(rect.x, rect.y, rect.width, 48);
            EditorGUI.DrawRect(new Rect(rect.x, topR.yMax - 1, rect.width, 1), C_BORDER);
            DrawTopbar(topR, selected);

            float bodyTop = topR.yMax;
            float actionH = 56;
            Rect bodyR = new Rect(rect.x, bodyTop, rect.width, rect.height - (bodyTop - rect.y) - actionH);
            DrawBody(bodyR, selected);

            Rect actR = new Rect(rect.x, rect.yMax - actionH, rect.width, actionH);
            DrawActionBar(actR, selected);
        }

        private void DrawTopbar(Rect r, SceneRow sc)
        {
            var bsRoot = new GUIStyle(EditorStyles.label) { fontSize = 12 };
            bsRoot.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x + 18, r.y, 80, r.height), ToolLang.Get("Scenes", "Сцены") + "  /", bsRoot);

            float bx = r.x + 18 + bsRoot.CalcSize(new GUIContent(ToolLang.Get("Scenes", "Сцены") + "  /")).x + 6;
            var bsCur = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            bsCur.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(bx, r.y, r.width - bx - 160, r.height), sc.Name, bsCur);

            Rect toggleR = new Rect(r.xMax - 160, r.y + 13, 110, 22);
            DrawHintsToggleButton(toggleR);

            Rect more = new Rect(r.xMax - 38, r.y + 9, 30, 30);
            if (DrawSquareIconBtn(more, "⋯", C_TEXT_2))
            {
                ShowMoreMenu(sc);
            }
        }

        private static bool ShowHints
        {
            get => EditorPrefs.GetBool("NovellaSceneHints_v1", true);
            set => EditorPrefs.SetBool("NovellaSceneHints_v1", value);
        }

        private static bool ShowThumbnail
        {
            get => EditorPrefs.GetBool("NovellaSceneThumbnails_v1", false);
            set => EditorPrefs.SetBool("NovellaSceneThumbnails_v1", value);
        }

        private void DrawHintsToggleButton(Rect r)
        {
            string label = ShowHints
                ? "💡 " + ToolLang.Get("Hints ON", "Подсказки ВКЛ")
                : "💡 " + ToolLang.Get("Hints OFF", "Подсказки ВЫКЛ");

            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = ShowHints ? C_ACCENT : C_TEXT_3;

            EditorGUI.DrawRect(r, ShowHints ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.13f) : C_BG_PRIMARY);
            DrawRectBorder(r, ShowHints ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f) : C_BORDER);
            GUI.Label(r, label, st);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                ShowHints = !ShowHints;
                Event.current.Use();
                _window?.Repaint();
            }
        }

        private void DrawHint(string richTextRu, float width)
        {
            if (!ShowHints) return;

            var st = new GUIStyle(EditorStyles.label) { fontSize = 10, wordWrap = true, richText = true, padding = new RectOffset(10, 10, 6, 6) };
            st.normal.textColor = NovellaSettingsModule.GetHintColor();

            var content = new GUIContent("💡  " + richTextRu);

            float h = st.CalcHeight(content, width);

            Rect r = GUILayoutUtility.GetRect(width, h);
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            DrawRectBorder(r, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.3f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), C_ACCENT);
            GUI.Label(r, content, st);
            GUILayout.Space(4);
        }

        private void ShowMoreMenu(SceneRow sc)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(ToolLang.Get("Rename…", "Переименовать…")), false, () => RenameScene(sc));
            menu.AddItem(new GUIContent(ToolLang.Get("Open in folder", "Открыть в папке")), false, () =>
            {
                EditorUtility.RevealInFinder(sc.Path);
            });
            menu.AddItem(new GUIContent(ToolLang.Get("Ping in Project", "Показать в Project")), false, () =>
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(sc.Path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            });
            menu.AddItem(new GUIContent(ToolLang.Get("Duplicate", "Дублировать")), false, () => DuplicateScene(sc));
            menu.ShowAsContext();
        }

        private void DrawBody(Rect rect, SceneRow sc)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(18);
            GUILayout.BeginHorizontal();
            GUILayout.Space(24);

            float contentWidth = rect.width - 48f - 16f;
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));

            _detailsScroll = GUILayout.BeginScrollView(_detailsScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            DrawHint(ToolLang.Get(
                "<b>What is a Scene?</b>\nThink of a scene as a specific level, a menu, or a room in your game. For example, 1 story you create = 1 scene. It contains all the characters, UI, cameras, and settings for that exact part of the story. The player moves between scenes as they progress.",
                "<b>Что такое Сцена?</b>\nСцена — это уровень, меню или отдельная локация в игре. Например, 1 история, которую вы создаете = 1 сцена. Она хранит в себе всех персонажей, интерфейс, камеры и настройки для конкретной части истории. Игрок перемещается между сценами по мере прохождения."
            ), contentWidth);

            GUILayout.BeginHorizontal();
            if (_isEditingName && _editingNamePath == sc.Path)
            {
                var tfStyle = new GUIStyle(EditorStyles.textField) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                tfStyle.normal.textColor = C_TEXT_1; tfStyle.focused.textColor = C_TEXT_1;

                GUI.SetNextControlName("SceneNameInlineEdit");
                _stagedName = EditorGUILayout.TextField(_stagedName, tfStyle, GUILayout.Width(250), GUILayout.Height(28));

                if (_focusInlineEdit && Event.current.type == EventType.Repaint)
                {
                    GUI.FocusControl("SceneNameInlineEdit");
                    _focusInlineEdit = false;
                }

                bool isChanged = _stagedName != sc.Name;
                bool isValid = !string.IsNullOrWhiteSpace(_stagedName) && !_allScenes.Any(s => s.Name == _stagedName && s.Path != sc.Path);

                if (Event.current.isKey && Event.current.type == EventType.KeyDown)
                {
                    if ((Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && isChanged && isValid)
                    {
                        RenameSceneInline(sc, _stagedName);
                        Event.current.Use();
                    }
                    else if (Event.current.keyCode == KeyCode.Escape)
                    {
                        _isEditingName = false;
                        GUI.FocusControl(null);
                        Event.current.Use();
                    }
                }

                if (isChanged && isValid)
                {
                    var okStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 12 };
                    var oldBg = GUI.backgroundColor; GUI.backgroundColor = C_OK;
                    if (GUILayout.Button("✓", okStyle, GUILayout.Width(30), GUILayout.Height(28)))
                    {
                        RenameSceneInline(sc, _stagedName);
                    }
                    GUI.backgroundColor = oldBg;
                }
                else if (isChanged && !isValid)
                {
                    var errStyle = new GUIStyle(EditorStyles.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                    errStyle.normal.textColor = C_DANGER;
                    GUILayout.Label(new GUIContent("⚠", ToolLang.Get("Invalid or duplicate name", "Некорректное имя или уже существует")), errStyle, GUILayout.Width(30), GUILayout.Height(28));
                }

                var cancelStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 12 };
                if (GUILayout.Button("✖", cancelStyle, GUILayout.Width(30), GUILayout.Height(28)))
                {
                    _isEditingName = false;
                    GUI.FocusControl(null);
                }
            }
            else
            {
                var h1 = new GUIStyle(EditorStyles.label) { fontSize = 20, fontStyle = FontStyle.Bold };
                h1.normal.textColor = C_TEXT_1;

                float nameW = h1.CalcSize(new GUIContent(sc.Name)).x;
                GUILayout.Label(sc.Name, h1, GUILayout.Width(nameW));

                GUILayout.Space(12);

                Rect editR = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                if (DrawInlineHeaderBtn(editR, "✎", C_TEXT_1, ToolLang.Get("Rename scene", "Переименовать сцену")))
                {
                    _isEditingName = true;
                    _editingNamePath = sc.Path;
                    _stagedName = sc.Name;
                    _focusInlineEdit = true;
                }

                if (sc.InBuild && sc.BuildIndex != 0)
                {
                    GUILayout.Space(6);
                    Rect starR = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));

                    if (DrawInlineHeaderBtn(starR, "★", new Color(0.96f, 0.86f, 0.53f), ToolLang.Get("Make this scene first (Index 0)", "Сделать эту сцену первой (Индекс 0)")))
                    {
                        EditorApplication.delayCall += () => MakeFirstInBuild(sc);
                    }
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var sub = new GUIStyle(EditorStyles.label) { fontSize = 12 };
            sub.normal.textColor = C_TEXT_3;
            string size = FormatSize(sc.FileSize);
            string when = FormatRelative(sc.LastEdited);
            GUILayout.Label($"{sc.Path}  ·  {size}  ·  {when}", sub);

            GUILayout.Space(16);

            Rect thumbHeader = GUILayoutUtility.GetRect(0, 24, GUILayout.Width(contentWidth));
            var thStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            thStyle.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(thumbHeader.x, thumbHeader.y + 4, 200, 20), "🖼 " + ToolLang.Get("SCENE PREVIEW", "ПРЕВЬЮ СЦЕНЫ"), thStyle);

            Rect toggleBtnR = new Rect(thumbHeader.xMax - 80, thumbHeader.y + 2, 80, 18);
            string toggleLabel = ShowThumbnail ? ToolLang.Get("Hide", "Скрыть") : ToolLang.Get("Show", "Показать");

            if (GUI.Button(toggleBtnR, toggleLabel, EditorStyles.miniButton))
            {
                ShowThumbnail = !ShowThumbnail;
            }

            EditorGUI.DrawRect(new Rect(thumbHeader.x, thumbHeader.yMax - 1, thumbHeader.width, 1), C_BORDER);
            GUILayout.Space(8);

            if (ShowThumbnail)
            {
                DrawHint(ToolLang.Get(
                    "This preview helps you visually distinguish your scenes. Open the scene and click the camera icon to capture the current Scene view.",
                    "Превью помогает визуально отличать сцены друг от друга. Открой сцену и нажми на иконку камеры (📸), чтобы сделать снимок текущего вида окна Scene."
                ), contentWidth);

                Rect thumbRect = GUILayoutUtility.GetRect(0, 320, GUILayout.Width(contentWidth));
                EditorGUI.DrawRect(thumbRect, C_BG_SIDE);
                DrawRectBorder(thumbRect, C_BORDER);

                Texture2D thumb = GetThumbnail(sc.Path);
                if (thumb != null)
                {
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleAndCrop);
                }
                else
                {
                    var plStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
                    plStyle.normal.textColor = C_TEXT_4;
                    GUI.Label(thumbRect, "📸 " + ToolLang.Get("No thumbnail. Open scene and click capture.", "Нет превью. Открой сцену и нажми кнопку снимка."), plStyle);
                }

                Rect btnRect = new Rect(thumbRect.xMax - 36, thumbRect.y + 8, 28, 28);
                Rect btnBg = new Rect(btnRect.x - 4, btnRect.y - 4, btnRect.width + 8, btnRect.height + 8);
                EditorGUI.DrawRect(btnBg, new Color(0, 0, 0, 0.5f));

                if (DrawSquareIconBtn(btnRect, "📸", C_TEXT_1))
                {
                    if (sc.IsOpen)
                    {
                        EditorApplication.delayCall += () => CaptureThumbnail(sc.Path);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            ToolLang.Get("Notice", "Внимание"),
                            ToolLang.Get("Please switch to this scene first to take a correct screenshot.",
                                         "Сначала переключитесь на эту сцену (кнопка внизу), чтобы сделать правильный снимок именно её, а не текущей."),
                            "OK"
                        );
                    }
                }
                GUILayout.Space(16);
            }

            DrawSectionHeader("📋 " + ToolLang.Get("SCENE INFO", "ИНФО О СЦЕНЕ"));
            DrawHint(ToolLang.Get(
                "Scenes added to <b>Build Settings</b> are included in your final game. The scene at <b>index 0</b> is the one that loads first (e.g. Main Menu).",
                "Сцены, добавленные в <b>Сборку (Build)</b>, попадают в финальную игру. Сцена с <b>индексом 0</b> запустится первой (обычно это Главное Меню)."
            ), contentWidth);
            DrawSceneInfoBox(sc);

            AppliedPreset applied = sc.IsOpen ? DetectAppliedPreset() : AppliedPreset.None;
            // Карточки «применить пресет» переехали в Кузницу UI (welcome-экран).
            // Здесь оставляем только tools-блок: показывает какой пресет
            // применён + кнопка «Очистить» + «Открыть в Кузнице UI».
            DrawToolsBlock(sc, applied, contentWidth);

            GUILayout.Space(20);

            // Закрываем В ОБРАТНОМ порядке от открытий:
            //   Begin: Area → Horizontal → Vertical → ScrollView
            //   End:   ScrollView → Vertical → Horizontal → Area
            // Раньше End-вызовы стояли в порядке открытий (Vertical → Horizontal
            // → ScrollView → Area), и при возникновении exception где-нибудь
            // выше IMGUI-стек разваливался: «EndLayoutGroup must be called
            // first» / «Stack empty».
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(24);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
        // ─── THUMBNAIL LOGIC ───
        // Специальная кнопка для заголовка: всегда есть фон и обводка, при наведении подсвечивается
        private bool DrawInlineHeaderBtn(Rect r, string icon, Color iconColor, string tooltip)
        {
            bool hover = r.Contains(Event.current.mousePosition);

            // Легкий полупрозрачный фон
            EditorGUI.DrawRect(r, hover ? C_BG_RAISED : new Color(0, 0, 0, 0.2f));

            // Рамка загорается цветом иконки при наведении
            DrawRectBorder(r, hover ? new Color(iconColor.r, iconColor.g, iconColor.b, 0.6f) : C_BORDER);

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            st.normal.textColor = hover ? iconColor : new Color(iconColor.r, iconColor.g, iconColor.b, 0.7f);

            GUI.Label(r, new GUIContent(icon, tooltip), st);

            if (hover && Event.current.type == EventType.MouseMove) _window?.Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
        private Texture2D GetThumbnail(string scenePath)
        {
            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (_thumbnails.ContainsKey(guid) && _thumbnails[guid] != null) return _thumbnails[guid];

            string dir = "Assets/NovellaEngine/EditorData/Thumbnails";
            string path = $"{dir}/{guid}.png";
            Texture2D tex = null;
            if (File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.filterMode = FilterMode.Bilinear; // Делает картинку гладкой
            }
            _thumbnails[guid] = tex;
            return tex;
        }

        private void CaptureThumbnail(string scenePath)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                Camera cam = sv.camera;
                // Снимаем в HD качестве (1280x720) вместо низкого
                int w = 1280; int h = 720;

                float oldAspect = cam.aspect;
                cam.aspect = (float)w / h;

                RenderTexture rt = new RenderTexture(w, h, 24);
                var prevTarget = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                cam.targetTexture = prevTarget;
                cam.aspect = oldAspect;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);

                string guid = AssetDatabase.AssetPathToGUID(scenePath);
                string dir = "Assets/NovellaEngine/EditorData/Thumbnails";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = $"{dir}/{guid}.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.Refresh();

                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.filterMode = FilterMode.Bilinear;
                _thumbnails[guid] = tex;
                _window?.Repaint();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", ToolLang.Get("Open Scene view to capture a thumbnail.", "Открой окно Scene (Сцена) чтобы сделать снимок."), "OK");
            }
        }

        private void DrawSceneInfoBox(SceneRow sc)
        {
            int rows = 3;
            float rowH = 26;
            float boxH = rows * rowH + 8;
            Rect box = GUILayoutUtility.GetRect(0, boxH, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(box, C_BG_SIDE);
            DrawRectBorder(box, C_BORDER);

            DrawInfoRow(new Rect(box.x + 14, box.y + 4, box.width - 28, rowH),
                ToolLang.Get("STATUS", "СТАТУС"),
                sc.IsOpen ? "● " + ToolLang.Get("Currently open", "Сейчас открыта") : ToolLang.Get("Not open", "Не открыта"),
                sc.IsOpen ? C_OK : C_TEXT_3,
                drawBottomBorder: true);

            Rect rBuild = new Rect(box.x + 14, box.y + 4 + rowH, box.width - 28, rowH);
            var keyStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(rBuild.x, rBuild.y, 200, rBuild.height), ToolLang.Get("IN BUILD SETTINGS", "В НАСТРОЙКАХ СБОРКИ"), keyStyle);

            if (!sc.InBuild)
            {
                var valStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
                valStyle.normal.textColor = C_TEXT_3;
                GUI.Label(new Rect(rBuild.x, rBuild.y, rBuild.width, rBuild.height), ToolLang.Get("No", "Нет"), valStyle);
            }
            else
            {
                var valStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
                valStyle.normal.textColor = C_OK;

                string prefix = ToolLang.Get("Yes (index", "Да (индекс");
                float pxW = valStyle.CalcSize(new GUIContent(prefix)).x;
                float sxW = valStyle.CalcSize(new GUIContent(")")).x;

                int totalBuildScenes = EditorBuildSettings.scenes.Length;
                bool isZero = (sc.BuildIndex == 0);

                bool useDropdown = totalBuildScenes <= MAX_DROPDOWN_SCENES;

                float fldW = useDropdown ? 40 : 32;
                bool isModified = !isZero && !useDropdown && (_editingIndexScenePath == sc.Path && _stagedBuildIndex != sc.BuildIndex);

                float dynamicExtraW = isModified ? 28 : 0;
                float startX = rBuild.xMax - sxW - fldW - pxW - 6 - dynamicExtraW;

                GUI.Label(new Rect(startX, rBuild.y, pxW, rBuild.height), prefix, valStyle);

                Rect fldRect = new Rect(startX + pxW + 2, rBuild.y + 4, fldW, rBuild.height - 8);

                if (!useDropdown)
                {
                    EditorGUI.DrawRect(fldRect, C_BG_PRIMARY);
                    DrawRectBorder(fldRect, C_BORDER);
                }

                var tfStyle = new GUIStyle(useDropdown ? EditorStyles.popup : EditorStyles.numberField);
                tfStyle.fontSize = 11;
                if (!useDropdown)
                {
                    tfStyle.normal.background = null; tfStyle.focused.background = null;
                    tfStyle.alignment = TextAnchor.MiddleCenter;
                }
                tfStyle.normal.textColor = C_TEXT_1; tfStyle.focused.textColor = C_TEXT_1;

                if (isZero)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    if (useDropdown) EditorGUI.Popup(fldRect, 0, new[] { "0" }, tfStyle);
                    else EditorGUI.IntField(fldRect, 0, tfStyle);
                    EditorGUI.EndDisabledGroup();
                    GUI.Label(new Rect(fldRect.xMax + 4, rBuild.y, sxW, rBuild.height), ")", valStyle);
                }
                else
                {
                    if (useDropdown)
                    {
                        GUIContent[] options = new GUIContent[totalBuildScenes - 1];
                        int[] values = new int[totalBuildScenes - 1];
                        for (int i = 1; i < totalBuildScenes; i++)
                        {
                            options[i - 1] = new GUIContent(i.ToString());
                            values[i - 1] = i;
                        }

                        EditorGUI.BeginChangeCheck();
                        var oldCol = GUI.contentColor;
                        GUI.contentColor = C_TEXT_1;
                        int newIdx = EditorGUI.IntPopup(fldRect, sc.BuildIndex, options, values, tfStyle);
                        GUI.contentColor = oldCol;

                        if (EditorGUI.EndChangeCheck())
                        {
                            ChangeBuildIndex(sc, newIdx);
                        }
                        GUI.Label(new Rect(fldRect.xMax + 4, rBuild.y, sxW, rBuild.height), ")", valStyle);
                    }
                    else
                    {
                        if (_editingIndexScenePath != sc.Path)
                        {
                            _editingIndexScenePath = sc.Path;
                            _stagedBuildIndex = sc.BuildIndex;
                        }

                        var oldCol = GUI.contentColor;
                        GUI.contentColor = C_TEXT_1;

                        EditorGUI.BeginChangeCheck();
                        int rawInput = EditorGUI.IntField(fldRect, _stagedBuildIndex, tfStyle);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _stagedBuildIndex = Mathf.Clamp(rawInput, 1, totalBuildScenes - 1);
                        }
                        GUI.contentColor = oldCol;

                        if (isModified)
                        {
                            Rect btnRect = new Rect(fldRect.xMax + 4, rBuild.y + 4, 24, rBuild.height - 8);

                            var btnStyle = new GUIStyle(EditorStyles.miniButton);
                            btnStyle.fontSize = 10;
                            btnStyle.padding = new RectOffset(0, 0, 0, 0);

                            var oldBg = GUI.backgroundColor;
                            GUI.backgroundColor = C_OK;
                            if (GUI.Button(btnRect, "✓", btnStyle))
                            {
                                ChangeBuildIndex(sc, _stagedBuildIndex);
                                GUI.FocusControl(null);
                            }
                            GUI.backgroundColor = oldBg;

                            GUI.Label(new Rect(btnRect.xMax + 4, rBuild.y, sxW, rBuild.height), ")", valStyle);
                        }
                        else
                        {
                            GUI.Label(new Rect(fldRect.xMax + 4, rBuild.y, sxW, rBuild.height), ")", valStyle);
                        }
                    }
                }
            }
            EditorGUI.DrawRect(new Rect(rBuild.x, rBuild.yMax - 1, rBuild.width, 1), C_BORDER);

            string presetVal;
            Color presetColor;
            if (!sc.IsOpen)
            {
                presetVal = ToolLang.Get("Open scene to detect", "Открой сцену чтобы узнать");
                presetColor = C_TEXT_4;
            }
            else
            {
                AppliedPreset ap = DetectAppliedPreset();
                if (ap == AppliedPreset.MainMenu) { presetVal = "📱 " + ToolLang.Get("Main Menu", "Главное Меню"); presetColor = C_PURPLE; }
                else if (ap == AppliedPreset.Gameplay) { presetVal = "🎮 " + ToolLang.Get("Gameplay", "Игровая"); presetColor = C_ACCENT; }
                else { presetVal = "—"; presetColor = C_TEXT_3; }
            }
            DrawInfoRow(new Rect(box.x + 14, box.y + 4 + rowH * 2, box.width - 28, rowH),
                ToolLang.Get("APPLIED PRESET", "ПРИМЕНЁННЫЙ ПРЕСЕТ"),
                presetVal,
                presetColor,
                drawBottomBorder: false);

            GUILayout.Space(14);
        }

        private void ChangeBuildIndex(SceneRow sc, int newIdx)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            int oldIdx = sc.BuildIndex;
            if (oldIdx < 0 || oldIdx >= scenes.Count) return;

            if (newIdx <= 0)
            {
                if (oldIdx == 0) return;
                newIdx = 1;
            }

            if (newIdx >= scenes.Count) newIdx = scenes.Count - 1;
            if (newIdx == oldIdx) return;

            var item = scenes[oldIdx];
            scenes.RemoveAt(oldIdx);
            scenes.Insert(newIdx, item);

            EditorBuildSettings.scenes = scenes.ToArray();
            _stagedBuildIndex = newIdx;

            _highlightScenePath = sc.Path;
            _highlightTime = EditorApplication.timeSinceStartup;

            RefreshScenes();
            _window?.Repaint();
        }

        private void DrawInfoRow(Rect r, string key, string value, Color valueColor, bool drawBottomBorder)
        {
            var keyStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = C_TEXT_3;
            GUI.Label(new Rect(r.x, r.y, 200, r.height), key, keyStyle);

            var valStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
            valStyle.normal.textColor = valueColor;
            GUI.Label(new Rect(r.x, r.y, r.width, r.height), value, valStyle);

            if (drawBottomBorder)
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);
        }

        // ─── Presets ───

        private void DrawPresetsBlock(SceneRow sc, AppliedPreset applied, float contentWidth)
        {
            DrawSectionHeader("🎬 " + ToolLang.Get("SCENE PRESETS", "ПРЕСЕТЫ СЦЕНЫ"));
            DrawHint(ToolLang.Get(
                "Presets <b>fill an empty scene</b> with a Canvas, dialogue box, cameras, and basic UI. Pick one to set up the scene quickly. After applying, customise the look in <b>UI Forge</b>.",
                "Пресеты <b>заполняют пустую сцену</b> Canvas'ом, диалоговым окном, камерами и базовым UI. Выбери один, чтобы быстро настроить сцену. После применения настрой внешний вид в <b>Кузнице UI</b>."
            ), contentWidth);

            float headH = 32;
            float gridH = 100;
            float blockH = headH + gridH + 16;

            Rect block = GUILayoutUtility.GetRect(0, blockH, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(block, C_BG_SIDE);
            DrawRectBorder(block, C_BORDER);

            var ic = new GUIStyle(EditorStyles.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            ic.normal.textColor = C_ACCENT;
            GUI.Label(new Rect(block.x + 14, block.y + 8, 22, 20), "🎬", ic);

            var ti = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            ti.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(block.x + 40, block.y + 8, block.width - 200, 20), ToolLang.Get("Scene presets", "Пресеты сцены"), ti);

            if (applied != AppliedPreset.None)
            {
                Rect pill = new Rect(block.xMax - 130, block.y + 11, 116, 16);
                EditorGUI.DrawRect(pill, new Color(C_OK.r, C_OK.g, C_OK.b, 0.13f));
                var ps = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, fontStyle = FontStyle.Bold };
                ps.normal.textColor = C_OK;
                GUI.Label(pill, ToolLang.Get("PRESET APPLIED", "ПРЕСЕТ ПРИМЕНЁН"), ps);
            }

            float gridY = block.y + headH + 6;
            float gridGap = 10;
            float cardW = (block.width - 28 - gridGap) / 2;

            Rect menuCard = new Rect(block.x + 14, gridY, cardW, gridH - 10);
            Rect gpCard = new Rect(block.x + 14 + cardW + gridGap, gridY, cardW, gridH - 10);

            // ApplyPreset надо вызывать ЧЕРЕЗ delayCall — он делает много
            // тяжёлых операций со сценой (создание GameObjects, AssetDatabase
            // CreateAsset/SaveAssets, EditorSceneManager.MarkSceneDirty/SaveScene),
            // и если выполнять прямо в onClick посреди OnGUI — IMGUI-стек
            // разваливается, и End-вызовы DrawBody падают «EndLayoutGroup
            // must be called first / Stack empty». ClearPreset рядом уже
            // обёрнут в delayCall — теперь и ApplyPreset тоже.
            DrawPresetCard(menuCard, "📱", ToolLang.Get("Main Menu", "Главное Меню"),
                ToolLang.Get("Menu with character editor and story selection.", "Меню с редактором персонажа и выбором истории."),
                C_PURPLE, applied == AppliedPreset.MainMenu, applied == AppliedPreset.Gameplay, AppliedPreset.Gameplay,
                () => EditorApplication.delayCall += () => ApplyPreset(sc, AppliedPreset.MainMenu));

            DrawPresetCard(gpCard, "🎮", ToolLang.Get("Gameplay", "Игровая сцена"),
                ToolLang.Get("Canvas with dialogue box, character displays and CG layer.", "Canvas с диалоговым окном, персонажами и слоем CG."),
                C_ACCENT, applied == AppliedPreset.Gameplay, applied == AppliedPreset.MainMenu, AppliedPreset.MainMenu,
                () => EditorApplication.delayCall += () => ApplyPreset(sc, AppliedPreset.Gameplay));

            GUILayout.Space(14);
        }

        private void DrawPresetCard(Rect r, string emoji, string title, string desc, Color accent,
                                    bool isApplied, bool isLocked, AppliedPreset blockingPreset, System.Action onClick)
        {
            bool disabled = isLocked;
            bool hover = !disabled && r.Contains(Event.current.mousePosition);

            Color bg;
            if (isApplied) bg = new Color(C_OK.r, C_OK.g, C_OK.b, 0.05f);
            else if (disabled) bg = C_BG_PRIMARY;
            else if (hover) bg = C_BG_RAISED;
            else bg = C_BG_PRIMARY;
            EditorGUI.DrawRect(r, bg);

            Color borderC;
            if (isApplied) borderC = new Color(C_OK.r, C_OK.g, C_OK.b, 0.5f);
            else if (hover) borderC = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f);
            else borderC = C_BORDER;
            DrawRectBorder(r, borderC);

            Rect iconR = new Rect(r.x + 12, r.y + 12, 38, 38);
            Color iconBg = new Color(accent.r, accent.g, accent.b, disabled ? 0.06f : 0.15f);
            EditorGUI.DrawRect(iconR, iconBg);
            DrawRectBorder(iconR, new Color(accent.r, accent.g, accent.b, disabled ? 0.25f : 0.5f));
            var iconStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
            iconStyle.normal.textColor = disabled ? new Color(accent.r, accent.g, accent.b, 0.5f) : accent;
            GUI.Label(iconR, emoji, iconStyle);

            var ts = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            ts.normal.textColor = disabled ? C_TEXT_3 : C_TEXT_1;
            GUI.Label(new Rect(r.x + 60, r.y + 12, r.width - 80, 18), title, ts);

            var ds = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            ds.normal.textColor = disabled ? C_TEXT_4 : C_TEXT_3;
            GUI.Label(new Rect(r.x + 60, r.y + 32, r.width - 72, 30), desc, ds);

            if (disabled)
            {
                var ls = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold, wordWrap = true, richText = true };
                ls.normal.textColor = C_WARN;
                string blockingName = blockingPreset == AppliedPreset.MainMenu
                    ? ToolLang.Get("Main Menu", "Главное Меню")
                    : ToolLang.Get("Gameplay", "Игровая");
                string hint = string.Format(ToolLang.Get("🔒 Already set up as <b>{0}</b> — clear it first to switch",
                                                          "🔒 Уже настроено как <b>{0}</b> — сначала удали чтобы заменить"), blockingName);
                GUI.Label(new Rect(r.x + 60, r.y + 64, r.width - 72, 24), hint, ls);
            }

            if (isApplied)
            {
                var ms = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, fontStyle = FontStyle.Bold };
                ms.normal.textColor = C_OK;
                GUI.Label(new Rect(r.xMax - 80, r.y + 12, 70, 14), "✓ ACTIVE", ms);
            }

            if (!disabled && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && r.Contains(Event.current.mousePosition)) _window?.Repaint();
        }

        // ─── Tools ───

        private void DrawToolsBlock(SceneRow sc, AppliedPreset applied, float contentWidth)
        {
            DrawSectionHeader("🛠 " + ToolLang.Get("TOOLS", "ИНСТРУМЕНТЫ"));
            DrawHint(ToolLang.Get(
                "Open the scene in <b>UI Forge</b> to customise fonts, colors, and frames. Or remove the preset to start from scratch.",
                "Открой сцену в <b>Кузнице UI</b>, чтобы настроить шрифты, цвета и рамки. Или удали пресет, чтобы начать заново."
            ), contentWidth);

            float headH = 32;
            float gridH = 60;
            float blockH = headH + gridH + 16;

            Rect block = GUILayoutUtility.GetRect(0, blockH, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(block, C_BG_SIDE);
            DrawRectBorder(block, C_BORDER);

            var ic = new GUIStyle(EditorStyles.label) { fontSize = 14 };
            ic.normal.textColor = C_MINT;
            GUI.Label(new Rect(block.x + 14, block.y + 8, 22, 20), "🛠", ic);

            var ti = new GUIStyle(EditorStyles.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            ti.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(block.x + 40, block.y + 8, block.width - 200, 20), ToolLang.Get("Tools", "Инструменты"), ti);

            float gridY = block.y + headH + 4;
            float gridGap = 10;
            float cardW = (block.width - 28 - gridGap) / 2;
            Rect t1 = new Rect(block.x + 14, gridY, cardW, gridH);
            Rect t2 = new Rect(block.x + 14 + cardW + gridGap, gridY, cardW, gridH);

            DrawToolCard(t1, "🎨", ToolLang.Get("Open in UI Forge", "Открыть в Кузнице UI"),
                ToolLang.Get("Customise dialogue, menu, fonts, wardrobe.", "Настрой диалоги, меню, шрифты, гардероб."),
                C_MINT, false, () => { if (NovellaHubWindow.Instance != null) NovellaHubWindow.Instance.SwitchToModule(3); });

            bool canClear = sc.IsOpen && applied != AppliedPreset.None;
            DrawToolCard(t2, "🗑", ToolLang.Get("Clear preset", "Удалить пресет"),
                canClear
                    ? ToolLang.Get("Remove the Canvas — back to a blank scene.", "Удалить Canvas — снова пустая сцена.")
                    : (sc.IsOpen
                        ? ToolLang.Get("No preset to clear in this scene.", "В этой сцене нет пресета для удаления.")
                        : ToolLang.Get("Open the scene first.", "Сначала открой сцену.")),
                C_DANGER, !canClear, () => EditorApplication.delayCall += () => ClearPreset());

            GUILayout.Space(14);
        }

        private void DrawToolCard(Rect r, string emoji, string title, string desc, Color accent, bool disabled, System.Action onClick)
        {
            bool hover = !disabled && r.Contains(Event.current.mousePosition);

            EditorGUI.DrawRect(r, disabled ? C_BG_PRIMARY : (hover ? C_BG_RAISED : C_BG_SIDE));
            DrawRectBorder(r, hover && accent == C_DANGER
                ? new Color(C_DANGER.r, C_DANGER.g, C_DANGER.b, 0.5f)
                : (hover ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.5f) : C_BORDER));

            Rect iconR = new Rect(r.x + 10, r.y + 14, 32, 32);
            EditorGUI.DrawRect(iconR, new Color(accent.r, accent.g, accent.b, disabled ? 0.06f : 0.13f));
            DrawRectBorder(iconR, new Color(accent.r, accent.g, accent.b, disabled ? 0.25f : 0.5f));
            var iconStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            iconStyle.normal.textColor = disabled ? new Color(accent.r, accent.g, accent.b, 0.5f) : accent;
            GUI.Label(iconR, emoji, iconStyle);

            var ts = new GUIStyle(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            ts.normal.textColor = disabled ? C_TEXT_3 : C_TEXT_1;
            GUI.Label(new Rect(r.x + 52, r.y + 10, r.width - 60, 16), title, ts);

            var ds = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, wordWrap = true };
            ds.normal.textColor = disabled ? C_TEXT_4 : C_TEXT_3;
            GUI.Label(new Rect(r.x + 52, r.y + 28, r.width - 60, 28), desc, ds);

            if (!disabled && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                onClick?.Invoke();
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseMove && r.Contains(Event.current.mousePosition)) _window?.Repaint();
        }

        // ─── Action bar ───

        private void DrawActionBar(Rect r, SceneRow sc)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            float bx = r.x + 16;
            float by = r.y + 12;
            float bh = 32;

            string openLabel = sc.IsOpen
                ? "✓ " + ToolLang.Get("Already open", "Уже открыта")
                : "▶ " + ToolLang.Get("Switch to scene", "Переключиться на сцену");
            float w1 = 180;
            if (DrawActionButton(new Rect(bx, by, w1, bh), openLabel, fill: false, danger: false, disabled: sc.IsOpen))
                EditorApplication.delayCall += () => OpenScene(sc);

            float rightX = r.xMax - 16;
            float wDel = 100;
            rightX -= wDel;
            if (DrawActionButton(new Rect(rightX, by, wDel, bh), "🗑 " + ToolLang.Get("Delete", "Удалить"), fill: false, danger: true))
                EditorApplication.delayCall += () => DeleteSceneWithConfirm(sc);
            rightX -= 8;

            float wBuild = 130;
            rightX -= wBuild;
            if (sc.InBuild)
            {
                if (DrawActionButton(new Rect(rightX, by, wBuild, bh), "− " + ToolLang.Get("Remove from build", "Убрать из сборки"), fill: false, danger: false))
                    EditorApplication.delayCall += () => RemoveFromBuild(sc);
            }
            else
            {
                if (DrawActionButton(new Rect(rightX, by, wBuild, bh), "✓ " + ToolLang.Get("Add to build", "В сборку"), fill: false, danger: false))
                    EditorApplication.delayCall += () => AddToBuild(sc);
            }
        }
        private void RenameSceneInline(SceneRow sc, string newName)
        {
            string err = AssetDatabase.RenameAsset(sc.Path, newName);
            if (!string.IsNullOrEmpty(err))
            {
                EditorUtility.DisplayDialog(ToolLang.Get("Error", "Ошибка"), err, "OK");
                return;
            }
            AssetDatabase.SaveAssets();
            string newPath = Path.Combine(Path.GetDirectoryName(sc.Path), newName + ".unity").Replace("\\", "/");

            _selectedPaths.Clear();
            _selectedPaths.Add(newPath);
            _lastClickedPath = newPath;

            _isEditingName = false;
            _editingIndexScenePath = null;

            _highlightScenePath = newPath;
            _highlightTime = EditorApplication.timeSinceStartup;
            RefreshScenes();
            _window?.Repaint();
        }
        // ═════════════════════════════════════════════════════════
        // ОПЕРАЦИИ
        // ═════════════════════════════════════════════════════════

        private bool PromptSaveIfDirty()
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.isDirty)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    ToolLang.Get("Unsaved changes", "Несохраненные изменения"),
                    string.Format(ToolLang.Get("Save changes to the scene '{0}' before switching?", "Сохранить изменения на сцене «{0}» перед переходом?"), activeScene.name),
                    ToolLang.Get("Save", "Сохранить"),
                    ToolLang.Get("Cancel", "Отмена"),
                    ToolLang.Get("Don't Save", "Не сохранять")
                );

                if (choice == 0) return EditorSceneManager.SaveScene(activeScene);
                else if (choice == 1) return false;
                else if (choice == 2) return true;
            }
            return true;
        }

        private void CreateNewScene()
        {
            string baseDir = "Assets/NovellaEngine/Scenes";
            if (!Directory.Exists(baseDir)) { Directory.CreateDirectory(baseDir); AssetDatabase.Refresh(); }

            string defaultName = "NewScene";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{baseDir}/{defaultName}.unity");

            if (!PromptSaveIfDirty()) return;

            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(s, path);
            AssetDatabase.Refresh();
            RefreshScenes();

            _selectedPaths.Clear();
            _selectedPaths.Add(path);
            _lastClickedPath = path;

            _highlightScenePath = path;
            _highlightTime = EditorApplication.timeSinceStartup;
            _window?.Repaint();
        }

        private void OpenScene(SceneRow sc)
        {
            if (sc.IsOpen) return;
            if (!PromptSaveIfDirty()) return;

            EditorSceneManager.OpenScene(sc.Path);
            _highlightScenePath = sc.Path;
            _highlightTime = EditorApplication.timeSinceStartup;
            RefreshScenes();
        }

        private void AddToBuild(SceneRow sc)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == sc.Path)) return;
            list.Add(new EditorBuildSettingsScene(sc.Path, true));
            EditorBuildSettings.scenes = list.ToArray();
            RefreshScenes();
        }

        private void RemoveFromBuild(SceneRow sc)
        {
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == sc.Path);
            EditorBuildSettings.scenes = list.ToArray();
            RefreshScenes();
        }

        private void AddSelectedToBuild()
        {
            var list = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            foreach (var path in _selectedPaths)
            {
                if (!list.Any(s => s.path == path))
                {
                    list.Add(new EditorBuildSettingsScene(path, true));
                    changed = true;
                }
            }
            if (changed)
            {
                EditorBuildSettings.scenes = list.ToArray();
                RefreshScenes();
            }
        }

        private void RemoveSelectedFromBuild()
        {
            var list = EditorBuildSettings.scenes.ToList();
            int removed = list.RemoveAll(s => _selectedPaths.Contains(s.path));
            if (removed > 0)
            {
                EditorBuildSettings.scenes = list.ToArray();
                RefreshScenes();
            }
        }

        private void DeleteSelectedScenesWithConfirm()
        {
            string activePath = EditorSceneManager.GetActiveScene().path;
            if (_selectedPaths.Contains(activePath))
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot delete open scene", "Нельзя удалить открытую сцену"),
                    ToolLang.Get("One of the selected scenes is currently open. Switch to another scene first.",
                                  "Одна из выбранных сцен сейчас открыта. Переключись на другую сцену сначала."),
                    "OK");
                return;
            }

            int count = _selectedPaths.Count;
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete scenes?", "Удалить сцены?"),
                string.Format(ToolLang.Get("Permanently delete {0} scenes?", "Безвозвратно удалить {0} сцен(ы)?"), count),
                ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена"))) return;

            if (count > 1)
            {
                if (!EditorUtility.DisplayDialog(
                    ToolLang.Get("Are you absolutely sure?", "Вы абсолютно уверены?"),
                    string.Format(ToolLang.Get("You are about to delete {0} scenes forever. This CANNOT be undone. Proceed?", "Вы собираетесь удалить {0} сцен навсегда. Это действие НЕЛЬЗЯ отменить. Продолжить?"), count),
                    ToolLang.Get("DELETE ALL", "УДАЛИТЬ ВСЕ"), ToolLang.Get("Cancel", "Отмена"))) return;
            }

            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => _selectedPaths.Contains(s.path));
            EditorBuildSettings.scenes = list.ToArray();

            foreach (var path in _selectedPaths)
            {
                // Очистка скриншота при удалении сцены
                string guid = AssetDatabase.AssetPathToGUID(path);
                string thumbPath = $"Assets/NovellaEngine/EditorData/Thumbnails/{guid}.png";
                if (File.Exists(thumbPath)) File.Delete(thumbPath);

                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.SaveAssets();
            _selectedPaths.Clear();
            RefreshScenes();
            _window?.Repaint();
        }

        private void MakeFirstInBuild(SceneRow sc)
        {
            var list = EditorBuildSettings.scenes.ToList();
            int idx = list.FindIndex(s => s.path == sc.Path);
            if (idx <= 0) return;
            var item = list[idx];
            list.RemoveAt(idx);
            list.Insert(0, item);
            EditorBuildSettings.scenes = list.ToArray();

            _highlightScenePath = sc.Path;
            _highlightTime = EditorApplication.timeSinceStartup;

            RefreshScenes();
        }

        private void RenameScene(SceneRow sc)
        {
            NovellaRenamePopup.ShowWindow(sc.Name, (newName) => {
                if (string.IsNullOrEmpty(newName) || newName == sc.Name) return;

                string err = AssetDatabase.RenameAsset(sc.Path, newName);
                if (!string.IsNullOrEmpty(err))
                {
                    EditorUtility.DisplayDialog(ToolLang.Get("Error", "Ошибка"), err, "OK");
                    return;
                }
                AssetDatabase.SaveAssets();
                string newPath = Path.Combine(Path.GetDirectoryName(sc.Path), newName + ".unity").Replace("\\", "/");

                _selectedPaths.Clear();
                _selectedPaths.Add(newPath);
                _lastClickedPath = newPath;
                RefreshScenes();
                _window?.Repaint();
            });
        }

        private void DuplicateScene(SceneRow sc)
        {
            string newPath = AssetDatabase.GenerateUniqueAssetPath(sc.Path);
            if (AssetDatabase.CopyAsset(sc.Path, newPath))
            {
                AssetDatabase.Refresh();

                _selectedPaths.Clear();
                _selectedPaths.Add(newPath);
                _lastClickedPath = newPath;
                RefreshScenes();
            }
        }

        private void DeleteSceneWithConfirm(SceneRow sc)
        {
            if (sc.IsOpen)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot delete open scene", "Нельзя удалить открытую сцену"),
                    ToolLang.Get("Switch to another scene before deleting this one.",
                                  "Переключись на другую сцену прежде чем удалять эту."),
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                ToolLang.Get("Delete scene?", "Удалить сцену?"),
                string.Format(ToolLang.Get("Permanently delete '{0}'?\nThis cannot be undone.",
                                            "Удалить «{0}» безвозвратно?\nДействие нельзя отменить."), sc.Name),
                ToolLang.Get("Yes, delete", "Да, удалить"),
                ToolLang.Get("Cancel", "Отмена"))) return;

            RemoveFromBuild(sc);

            // Очистка скриншота при удалении сцены
            string guid = AssetDatabase.AssetPathToGUID(sc.Path);
            string thumbPath = $"Assets/NovellaEngine/EditorData/Thumbnails/{guid}.png";
            if (File.Exists(thumbPath)) File.Delete(thumbPath);

            AssetDatabase.DeleteAsset(sc.Path);
            AssetDatabase.SaveAssets();
            _selectedPaths.Clear();
            RefreshScenes();
            _window?.Repaint();
        }

        // ─── Пресеты — apply/clear/detect ───

        private AppliedPreset DetectAppliedPreset()
        {
            if (GameObject.Find(MARKER_MAINMENU) != null) return AppliedPreset.MainMenu;
            if (GameObject.Find(MARKER_GAMEPLAY) != null) return AppliedPreset.Gameplay;
            return AppliedPreset.None;
        }

        private void ApplyPreset(SceneRow sc, AppliedPreset preset)
        {
            if (!sc.IsOpen)
            {
                if (!PromptSaveIfDirty()) return;
                EditorSceneManager.OpenScene(sc.Path);
                RefreshScenes();
            }

            AppliedPreset existing = DetectAppliedPreset();
            if (existing != AppliedPreset.None && existing != preset)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Preset already applied", "Пресет уже применён"),
                    ToolLang.Get("This scene already has a preset. Click 'Clear preset' to remove it before applying another.",
                                  "В этой сцене уже есть пресет. Нажми «Удалить пресет» прежде чем применять другой."),
                    "OK");
                return;
            }
            if (existing == preset) return;

            if (preset == AppliedPreset.MainMenu) PerformMainMenuSetup();
            else if (preset == AppliedPreset.Gameplay) PerformGameplaySetup();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            RefreshScenes();
            _window?.Repaint();
        }

        private void ClearPreset()
        {
            // Старая версия искала через GameObject.Find и StartsWith("[Novella]") —
            // это пропускало (а) отключенные объекты, (б) холсты которые юзер
            // переименовал, (в) канвасы добавленные позже через UI Forge.
            // Теперь делаем «полный nuke» под подтверждением: всё лучше чем
            // оставить дубликаты после повторного применения пресета.

            bool ok = EditorUtility.DisplayDialog(
                ToolLang.Get("Clear preset", "Очистить пресет"),
                ToolLang.Get(
                    "This will remove ALL Canvases, EventSystems and Novella-managed objects (StoryLauncher / NovellaPlayer) from the active scene, including disabled ones. Custom non-Novella scene content (3D objects, lights, etc.) is left untouched.\n\nProceed?",
                    "Будут удалены ВСЕ Canvas, EventSystem и связанные объекты Novella (StoryLauncher / NovellaPlayer) в активной сцене — включая отключенные. Прочие 3D-объекты и свет в сцене не трогаются.\n\nПродолжить?"),
                ToolLang.Get("Clear", "Очистить"),
                ToolLang.Get("Cancel", "Отмена"));
            if (!ok) return;

            // Все root-канвасы (с учётом отключенных).
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas) continue;
                Undo.DestroyObjectImmediate(c.gameObject);
            }
            // EventSystem'ы.
            var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var e in eventSystems)
            {
                if (e != null) Undo.DestroyObjectImmediate(e.gameObject);
            }
            // Player / StoryLauncher как отдельные GameObject (на случай если
            // юзер вынес их из канваса).
            var players = Object.FindObjectsByType<NovellaPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var p in players) { if (p != null) Undo.DestroyObjectImmediate(p.gameObject); }
            var launchers = Object.FindObjectsByType<StoryLauncher>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var l in launchers) { if (l != null) Undo.DestroyObjectImmediate(l.gameObject); }

            // Камеры пресета — удаляем по имени (юзерскую `Main Camera` не
            // трогаем чтобы не убить пользовательский setup).
            DestroyByName("[Novella]_Camera");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            _window?.Repaint();
        }

        private void DestroyByName(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }

        // ═════════════════════════════════════════════════════════
        // SETUP — Main Menu
        // ═════════════════════════════════════════════════════════

        // Публичный фасад для вызова пресетов из других модулей (например
        // из Кузницы UI welcome-экрана). Внутри тот же поток что и при клике
        // на карточку пресета в «Сценах и Меню»: проверка существующего пресета,
        // PerformXxxSetup, MarkSceneDirty + SaveScene.
        public static void ApplyMainMenuPresetToActiveScene()
        {
            // Используем экземпляр модуля — без него методы Perform не работают.
            // Если Hub открыт, у нас точно есть модуль. Если нет — создаём
            // временный (он бесстейтовый для целей этой операции).
            var mod = FindModuleInstance() ?? new NovellaSceneManagerModule();
            mod.ApplyPresetToActiveScene(AppliedPreset.MainMenu);
        }

        public static void ApplyGameplayPresetToActiveScene()
        {
            var mod = FindModuleInstance() ?? new NovellaSceneManagerModule();
            mod.ApplyPresetToActiveScene(AppliedPreset.Gameplay);
        }

        private static NovellaSceneManagerModule FindModuleInstance()
        {
            if (NovellaHubWindow.Instance == null) return null;
            for (int i = 0; i < 10; i++)
            {
                var m = NovellaHubWindow.Instance.GetModule(i) as NovellaSceneManagerModule;
                if (m != null) return m;
            }
            return null;
        }

        // Внутренний метод — повторяет логику ApplyPreset(SceneRow, AppliedPreset)
        // но для активной сцены без SceneRow (она нужна только для
        // переключения сцены, что здесь не нужно — мы и так в активной).
        private void ApplyPresetToActiveScene(AppliedPreset preset)
        {
            AppliedPreset existing = DetectAppliedPreset();
            if (existing != AppliedPreset.None && existing != preset)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Preset already applied", "Пресет уже применён"),
                    ToolLang.Get(
                        "This scene already has a preset. Click 'Clear preset' in «Scenes & Menu» before applying another.",
                        "В этой сцене уже есть пресет. Нажми «Удалить пресет» во вкладке «Сцены и Меню» прежде чем применять другой."),
                    "OK");
                return;
            }
            if (existing == preset) return;

            if (preset == AppliedPreset.MainMenu) PerformMainMenuSetup();
            else if (preset == AppliedPreset.Gameplay) PerformGameplaySetup();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }

        private void PerformMainMenuSetup()
        {
            EnsureMainCamera();
            EnsureEventSystem();
            var canvas = CreateCanvas("[Novella]_Canvas");

            // ─── Main menu panel (root) ──────────────────────────────────────
            var marker = new GameObject(MARKER_MAINMENU);
            marker.transform.SetParent(canvas.transform, false);
            var markerRT = marker.AddComponent<RectTransform>();
            markerRT.anchorMin = Vector2.zero; markerRT.anchorMax = Vector2.one;
            markerRT.offsetMin = Vector2.zero; markerRT.offsetMax = Vector2.zero;
            AddPresetMarker(marker, "MainMenu");
            AddPresetMarker(canvas.gameObject, "MainMenu");

            var title = CreateUIText(marker, "Title", "MY GAME", 64, FontStyle.Bold);
            var titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.5f, 1f);
            titleRT.anchorMax = new Vector2(0.5f, 1f);
            titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.anchoredPosition = new Vector2(0, -120);
            titleRT.sizeDelta = new Vector2(800, 100);

            // Имена кнопок жёстко зашиты под StoryLauncher.AutoWireButtons:
            //   Btn_StartPlay → ShowPanel(StoriesPanel)
            //   Btn_Settings  → OpenSettings
            //   Btn_Exit      → ExitGame
            // Btn_Continue не auto-wired (загрузка сейва живёт в NovellaPlayer);
            // ему вешаем NovellaUIBinding с действием LoadLastSave.
            //
            // Якоря — center-center (0.5, 0.5), блок кнопок центрирован по
            // вертикали относительно центра экрана. Шаг между кнопками 80px,
            // 4 кнопки → высота 320, верхняя на +120, нижняя на -120.
            var btnStart    = CreateUIButton(marker, "Btn_StartPlay", ToolLang.Get("New game",   "Новая игра"),  Vector2.zero);
            SetCenterAnchorPos(btnStart,    new Vector2(0,  120));
            var btnContinue = CreateUIButton(marker, "Btn_Continue",  ToolLang.Get("Continue",   "Продолжить"),  Vector2.zero);
            SetCenterAnchorPos(btnContinue, new Vector2(0,   40));
            var btnSettings = CreateUIButton(marker, "Btn_Settings",  ToolLang.Get("Settings",   "Настройки"),   Vector2.zero);
            SetCenterAnchorPos(btnSettings, new Vector2(0,  -40));
            var btnExit     = CreateUIButton(marker, "Btn_Exit",      ToolLang.Get("Quit",       "Выход"),       Vector2.zero);
            SetCenterAnchorPos(btnExit,     new Vector2(0, -120));

            // Привязки действий через NovellaUIBinding (вместо AutoWireButtons
            // по имени). Это делает связь кнопка ↔ действие устойчивой к
            // переименованию: юзер может назвать «Btn_StartPlay» в «Поехали» —
            // действие останется. AutoWireButtons теперь пропускает кнопки
            // у которых есть NovellaUIBinding (см. StoryLauncher.AutoWireButtons).
            AddLoadLastSaveBinding(btnContinue);
            AddQuitGameBinding(btnExit);
            // Btn_StartPlay = ShowPanel(StoriesPanel). Для этого StoriesPanel
            // тоже получит binding (id-based привязка). Делаем после создания
            // StoriesPanel ниже.

            // ─── Stories panel — список историй из Resources ─────────────────
            // StoryLauncher.LoadStoriesFromResources() сам инстанцирует
            // StoryButtonPrefab под каждый ассет в Resources/Stories.
            var storiesPanel = new GameObject("StoriesPanel");
            storiesPanel.transform.SetParent(canvas.transform, false);
            var spRT = storiesPanel.AddComponent<RectTransform>();
            spRT.anchorMin = Vector2.zero; spRT.anchorMax = Vector2.one;
            spRT.offsetMin = Vector2.zero; spRT.offsetMax = Vector2.zero;
            AddPresetMarker(storiesPanel, "MainMenu");

            var storiesContainer = new GameObject("StoriesContainer");
            storiesContainer.transform.SetParent(storiesPanel.transform, false);
            var scRT = storiesContainer.AddComponent<RectTransform>();
            scRT.anchorMin = new Vector2(0.5f, 0.5f);
            scRT.anchorMax = new Vector2(0.5f, 0.5f);
            scRT.pivot = new Vector2(0.5f, 0.5f);
            scRT.sizeDelta = new Vector2(900, 600);
            scRT.anchoredPosition = Vector2.zero;
            var vlg = storiesContainer.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 12;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            storiesPanel.SetActive(false);

            // Биндинг на StoriesPanel — нужен чтобы Btn_StartPlay мог
            // ссылаться на него по ID через ShowPanel.
            var storiesBinding = storiesPanel.AddComponent<NovellaUIBinding>();
            storiesBinding.EnsureId();
            storiesBinding.Name = "StoriesPanel";

            // Btn_StartPlay → ShowPanel(StoriesPanel). Связь по ID — устойчива
            // к переименованию StoriesPanel и/или самой кнопки.
            AddShowPanelBinding(btnStart, storiesBinding.Id);

            // ─── MC Creation panel — гардероб / редактор персонажа ───────────
            var mcPanel = BuildMCCreationPanel(canvas, active: false);

            // ─── StoryLauncher с привязанными ссылками ───────────────────────
            // Без StoryLauncher кнопки в главном меню были бы декоративными —
            // его наличие в сцене обязательно для пресета MainMenu.
            var slGO = new GameObject("[Novella]_StoryLauncher");
            AddPresetMarker(slGO, "MainMenu");
            var sl   = slGO.AddComponent<StoryLauncher>();
            sl.MainMenuPanel    = marker;
            sl.StoriesPanel     = storiesPanel;
            sl.StoriesContainer = storiesContainer.transform;
            sl.MCCreationPanel  = mcPanel.gameObject;
            sl.MCAvatarPreview  = mcPanel.AvatarPreview;
            sl.MCPrevLookButton = mcPanel.PrevLook;
            sl.MCNextLookButton = mcPanel.NextLook;
            sl.MCConfirmButton  = mcPanel.Confirm;
            sl.MCNameInput      = mcPanel.NameInput;

            // Авто-привязка истории и префаба кнопки. Без них список историй
            // в StoriesPanel был бы пустым и при клике на «Новая игра»
            // пользователь видел бы пустой экран.
            var starterTree   = EnsureStarterTree();
            var starterStory  = EnsureStarterStory(starterTree);
            var storyBtnPref  = EnsureStoryButtonPrefab();
            sl.StoryButtonPrefab = storyBtnPref;
            if (sl.SpecificStories == null) sl.SpecificStories = new List<NovellaStory>();
            if (!sl.SpecificStories.Contains(starterStory)) sl.SpecificStories.Add(starterStory);

            Selection.activeGameObject = marker;
        }

        // Помечает GameObject как «созданный пресетом». Studio дерево использует
        // эту метку, чтобы запретить ручное переключение active у структурных
        // объектов (MCCreationPanel должен оставаться выключенным; Player/Launcher
        // должны быть активны и т.д.). Любой потомок помеченного — тоже preset-managed.
        private static void AddPresetMarker(GameObject go, string presetName)
        {
            if (go == null) return;
            var m = go.GetComponent<NovellaPresetMarker>();
            if (m == null) m = go.AddComponent<NovellaPresetMarker>();
            m.PresetName = presetName;
        }

        // ─── Авто-привязка ассетов: NovellaTree / NovellaStory / StoryButtonPrefab ───
        // Цель: чтобы пресет работал «прямо из коробки» — не пришлось ничего
        // привязывать руками. Все три helper’а сначала ищут существующее в проекте
        // через AssetDatabase, и только если не нашли — создают минимальный
        // дефолт. Дубликаты не плодятся, повторный вызов вернёт тот же ассет.

        private const string GENERATED_DIR = "Assets/NovellaEngine/Generated";
        private const string GENERATED_TREE = "Assets/NovellaEngine/Generated/Starter_Chapter.asset";
        private const string GENERATED_STORY_DIR = "Assets/NovellaEngine/Resources/Stories";
        private const string GENERATED_STORY = "Assets/NovellaEngine/Resources/Stories/Starter_Story.asset";
        private const string GENERATED_STORY_BTN = "Assets/NovellaEngine/Generated/Starter_StoryButton.prefab";

        private static void EnsureFolder(string path)
        {
            // path в формате Assets/Foo/Bar — рекурсивно создаём недостающие папки.
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string acc = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = acc + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(acc, parts[i]);
                acc = next;
            }
        }

        // Возвращает любой имеющийся NovellaTree в проекте (первый по AssetDatabase),
        // или создаёт пустой Starter_Chapter.asset если в проекте нет ни одного.
        private NovellaTree EnsureStarterTree()
        {
            var guids = AssetDatabase.FindAssets("t:NovellaTree");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var existing = AssetDatabase.LoadAssetAtPath<NovellaTree>(path);
                if (existing != null) return existing;
            }

            EnsureFolder(GENERATED_DIR);
            var tree = ScriptableObject.CreateInstance<NovellaTree>();
            // Пустой tree без нодов — Player не упадёт, просто покажет лог
            // «нет стартового узла», и юзер откроет граф чтобы добавить контент.
            // Создавать здесь полноценный DialogueNode с фразой не стоит:
            // это требует валидного NovellaCharacter и LocalizedString с RU/EN —
            // юзер потом всё равно перепишет под свою историю.
            AssetDatabase.CreateAsset(tree, GENERATED_TREE);
            AssetDatabase.SaveAssets();
            return tree;
        }

        // Возвращает первую NovellaStory в проекте, или создаёт Starter_Story
        // в Resources/Stories со ссылкой на переданный chapter (стартовая глава).
        private NovellaStory EnsureStarterStory(NovellaTree chapter)
        {
            var guids = AssetDatabase.FindAssets("t:NovellaStory");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var existing = AssetDatabase.LoadAssetAtPath<NovellaStory>(path);
                if (existing != null)
                {
                    // Если у существующей истории не назначена стартовая глава,
                    // пристёгиваем переданную — иначе StoryLauncher отфильтрует
                    // её в LoadStoriesFromResources (там есть `if StartingChapter == null continue`).
                    if (existing.StartingChapter == null && chapter != null)
                    {
                        existing.StartingChapter = chapter;
                        EditorUtility.SetDirty(existing);
                        AssetDatabase.SaveAssets();
                    }
                    return existing;
                }
            }

            EnsureFolder(GENERATED_STORY_DIR);
            var story = ScriptableObject.CreateInstance<NovellaStory>();
            story.Title = "Starter Story";
            story.Description = "Auto-generated stub story. Replace with your own.";
            story.StartingChapter = chapter;
            AssetDatabase.CreateAsset(story, GENERATED_STORY);
            AssetDatabase.SaveAssets();
            return story;
        }

        // Создаёт минимальный prefab кнопки выбора истории в Generated/.
        // Один Button c Image-фоном и двумя TMP_Text-ами (Title + Description) —
        // это формат, которого ждёт StoryLauncher.LoadStoriesFromResources.
        private GameObject EnsureStoryButtonPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GENERATED_STORY_BTN);
            if (existing != null) return existing;

            EnsureFolder(GENERATED_DIR);

            var go = new GameObject("StoryButton");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(800, 120);

            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.14f, 0.16f, 0.20f, 0.95f);
            go.AddComponent<UnityEngine.UI.Button>();

            // Title
            var titleGo = CreateUIText(go, "Title", "Story Title", 28, FontStyle.Bold);
            var tRT = titleGo.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 0.55f);
            tRT.anchorMax = new Vector2(1, 1f);
            tRT.offsetMin = new Vector2(20, 0);
            tRT.offsetMax = new Vector2(-20, -8);
            var tT = titleGo.GetComponent<TMPro.TextMeshProUGUI>();
            tT.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

            // Description
            var descGo = CreateUIText(go, "Description", "Short description here…", 16, FontStyle.Normal);
            var dRT = descGo.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0, 0);
            dRT.anchorMax = new Vector2(1, 0.55f);
            dRT.offsetMin = new Vector2(20, 8);
            dRT.offsetMax = new Vector2(-20, 0);
            var dT = descGo.GetComponent<TMPro.TextMeshProUGUI>();
            dT.alignment = TMPro.TextAlignmentOptions.TopLeft;
            dT.color = new Color(0.78f, 0.80f, 0.86f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, GENERATED_STORY_BTN);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // Helper: вешает на GameObject (с Button-ом) NovellaUIBinding с одним
        // шагом LoadLastSave. Используется для Btn_Continue в Main Menu.
        private static void AddLoadLastSaveBinding(GameObject btnGo)
        {
            AddSimpleBinding(btnGo, new NovellaUIBinding.ClickActionStep
            {
                Action = NovellaUIBinding.BindingAction.LoadLastSave
            });
        }

        private static void AddQuitGameBinding(GameObject btnGo)
        {
            AddSimpleBinding(btnGo, new NovellaUIBinding.ClickActionStep
            {
                Action = NovellaUIBinding.BindingAction.QuitGame
            });
        }

        private static void AddShowPanelBinding(GameObject btnGo, string targetBindingId)
        {
            AddSimpleBinding(btnGo, new NovellaUIBinding.ClickActionStep
            {
                Action = NovellaUIBinding.BindingAction.ShowPanel,
                TargetBindingId = targetBindingId,
            });
        }

        // Универсальный helper: вешает NovellaUIBinding на кнопку и ставит
        // ровно один шаг ClickSequence. Если binding уже был — переиспользуем
        // его (это редко, но не дублируем компонент).
        private static void AddSimpleBinding(GameObject btnGo, NovellaUIBinding.ClickActionStep step)
        {
            if (btnGo == null) return;
            var b = btnGo.GetComponent<NovellaUIBinding>();
            if (b == null) b = btnGo.AddComponent<NovellaUIBinding>();
            b.EnsureId();
            b.Name = btnGo.name;
            if (b.ClickSequence == null) b.ClickSequence = new List<NovellaUIBinding.ClickActionStep>();
            b.ClickSequence.Clear();
            b.ClickSequence.Add(step);
        }

        // Контейнер ссылок на дочерние элементы MC Creation Panel — чтобы
        // снаружи можно было одной строкой собрать всё необходимое.
        private struct MCPanelRefs
        {
            public GameObject gameObject;
            public UnityEngine.UI.Image  AvatarPreview;
            public UnityEngine.UI.Button PrevLook;
            public UnityEngine.UI.Button NextLook;
            public UnityEngine.UI.Button Confirm;
            public TMPro.TMP_InputField  NameInput;
        }

        // Собирает «гардероб»: AvatarPreview + кнопки переключения внешности +
        // поле ввода имени + кнопка подтверждения. Имена строго совпадают с
        // тем что StoryLauncher.AutoFindPanels() ищет рекурсивно.
        private MCPanelRefs BuildMCCreationPanel(Canvas canvas, bool active)
        {
            var panel = new GameObject("MCCreationPanel");
            panel.transform.SetParent(canvas.transform, false);
            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            AddPresetMarker(panel, "Wardrobe");

            // Полупрозрачный фон, чтобы был «модальный» вид.
            var bgGo = CreateUIImage(panel, "Background", new Color(0.07f, 0.08f, 0.10f, 0.92f));
            var bgRT = bgGo.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            // AvatarPreview (Image) — посередине.
            var avatarGo = CreateUIImage(panel, "AvatarPreview", new Color(0.30f, 0.32f, 0.40f));
            var avRT = avatarGo.GetComponent<RectTransform>();
            avRT.anchorMin = new Vector2(0.5f, 0.5f);
            avRT.anchorMax = new Vector2(0.5f, 0.5f);
            avRT.pivot     = new Vector2(0.5f, 0.5f);
            avRT.sizeDelta = new Vector2(500, 700);
            avRT.anchoredPosition = new Vector2(0, 40);

            // Btn_PrevLook / Btn_NextLook — слева/справа от аватара.
            // Якоря center-center, чтобы координаты были интуитивные (от центра
            // экрана), а не относительно top-edge как у CreateUIButton по умолчанию.
            // Раньше якорь был top-center → Y=40 значило 40px НИЖЕ верхнего края,
            // но при кастомных размерах кнопок 80×80 + позиции -360 это выкидывало
            // их «за экран». Теперь center-anchor: x=-420/+360, y=40 — кнопки
            // ровно по бокам от аватара.
            var prev = CreateUIButton(panel, "Btn_PrevLook", "←", Vector2.zero);
            SetCenterAnchorPos(prev, new Vector2(-420, 40));
            prev.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);

            var next = CreateUIButton(panel, "Btn_NextLook", "→", Vector2.zero);
            SetCenterAnchorPos(next, new Vector2( 360, 40));
            next.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);

            // MCNameInput (TMP_InputField).
            var nameGo = CreateUIInputField(panel, "MCNameInput",
                ToolLang.Get("Your name", "Ваше имя"), new Vector2(0, -380));

            // Btn_Confirm — нижняя кнопка «Готово». Center-anchor чтобы
            // кнопка не «уплывала» при разных разрешениях.
            var confirm = CreateUIButton(panel, "Btn_Confirm",
                ToolLang.Get("Confirm", "Готово"), Vector2.zero);
            SetCenterAnchorPos(confirm, new Vector2(0, -440));

            panel.SetActive(active);

            return new MCPanelRefs
            {
                gameObject    = panel,
                AvatarPreview = avatarGo.GetComponent<UnityEngine.UI.Image>(),
                PrevLook      = prev.GetComponent<UnityEngine.UI.Button>(),
                NextLook      = next.GetComponent<UnityEngine.UI.Button>(),
                Confirm       = confirm.GetComponent<UnityEngine.UI.Button>(),
                NameInput     = nameGo.GetComponent<TMPro.TMP_InputField>(),
            };
        }

        // TMP_InputField с placeholder'ом. По умолчанию белый фон, тёмный текст.
        private GameObject CreateUIInputField(GameObject parent, string name, string placeholder, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(360, 56);

            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0.95f, 0.96f, 0.98f, 1f);

            var input = go.AddComponent<TMPro.TMP_InputField>();

            // Text-area child
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(go.transform, false);
            var taRT = textArea.AddComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero; taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(12, 6); taRT.offsetMax = new Vector2(-12, -6);
            var maskComp = textArea.AddComponent<UnityEngine.UI.RectMask2D>();

            // Placeholder
            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(textArea.transform, false);
            var phRT = phGo.AddComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
            var phT = phGo.AddComponent<TMPro.TextMeshProUGUI>();
            phT.text = placeholder;
            phT.fontStyle = TMPro.FontStyles.Italic;
            phT.color = new Color(0.55f, 0.58f, 0.62f);
            phT.fontSize = 22;
            phT.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

            // Real text
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(textArea.transform, false);
            var txtRT = txtGo.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            var txtT = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
            txtT.text = "";
            txtT.color = new Color(0.10f, 0.12f, 0.16f);
            txtT.fontSize = 22;
            txtT.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

            input.textViewport = taRT;
            input.textComponent = txtT;
            input.placeholder   = phT;

            return go;
        }

        // ═════════════════════════════════════════════════════════
        // SETUP — Gameplay
        // ═════════════════════════════════════════════════════════

        private void PerformGameplaySetup()
        {
            EnsureMainCamera();
            EnsureEventSystem();
            var canvas = CreateCanvas("[Novella]_Canvas");

            var marker = new GameObject(MARKER_GAMEPLAY);
            marker.transform.SetParent(canvas.transform, false);
            var markerRT = marker.AddComponent<RectTransform>();
            markerRT.anchorMin = Vector2.zero; markerRT.anchorMax = Vector2.one;
            markerRT.offsetMin = Vector2.zero; markerRT.offsetMax = Vector2.zero;
            AddPresetMarker(marker, "Gameplay");
            AddPresetMarker(canvas.gameObject, "Gameplay");

            var bg = CreateUIImage(marker, "Background_CG", new Color(0.1f, 0.1f, 0.15f));
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

            var charLayer = new GameObject("Character_Layer");
            charLayer.transform.SetParent(marker.transform, false);
            var clRT = charLayer.AddComponent<RectTransform>();
            clRT.anchorMin = Vector2.zero; clRT.anchorMax = Vector2.one;
            clRT.offsetMin = Vector2.zero; clRT.offsetMax = Vector2.zero;

            var dialogueBox = CreateUIImage(marker, "Dialogue_Box", new Color(0, 0, 0, 0.78f));
            var dRT = dialogueBox.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0, 0); dRT.anchorMax = new Vector2(1, 0);
            dRT.pivot = new Vector2(0.5f, 0);
            dRT.anchoredPosition = new Vector2(0, 40);
            dRT.sizeDelta = new Vector2(-160, 220);

            var speakerName = CreateUIText(dialogueBox, "Speaker_Name", "Speaker", 28, FontStyle.Bold);
            var snRT = speakerName.GetComponent<RectTransform>();
            snRT.anchorMin = new Vector2(0, 1); snRT.anchorMax = new Vector2(0, 1);
            snRT.pivot = new Vector2(0, 1);
            snRT.anchoredPosition = new Vector2(40, -20);
            snRT.sizeDelta = new Vector2(400, 40);

            var dialogueText = CreateUIText(dialogueBox, "Dialogue_Text",
                ToolLang.Get("Dialogue text appears here…", "Здесь появляется текст диалога…"), 22, FontStyle.Normal);
            var dtRT = dialogueText.GetComponent<RectTransform>();
            dtRT.anchorMin = Vector2.zero; dtRT.anchorMax = Vector2.one;
            dtRT.offsetMin = new Vector2(40, 30); dtRT.offsetMax = new Vector2(-40, -65);

            // ChoiceContainer — сюда NovellaPlayer спавнит кнопки выбора.
            // Layout по вертикали с интервалами, чтобы читалось из коробки.
            var choiceContainer = new GameObject("ChoiceContainer");
            choiceContainer.transform.SetParent(marker.transform, false);
            var ccRT = choiceContainer.AddComponent<RectTransform>();
            ccRT.anchorMin = new Vector2(0.5f, 0.5f);
            ccRT.anchorMax = new Vector2(0.5f, 0.5f);
            ccRT.pivot     = new Vector2(0.5f, 0.5f);
            ccRT.sizeDelta = new Vector2(800, 600);
            ccRT.anchoredPosition = new Vector2(0, 60);
            var ccVlg = choiceContainer.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            ccVlg.spacing = 12;
            ccVlg.childAlignment = TextAnchor.MiddleCenter;
            ccVlg.childForceExpandHeight = false;
            ccVlg.childForceExpandWidth  = true;
            // ChoiceButtonPrefab — НЕ создаём в сцене (это prefab, не runtime).
            // Если у юзера нет своего prefab'а, NovellaPlayer выводит warning при
            // попытке показать выборы; решает юзер сам, пресет prefab’ы не плодит.

            // ─── NovellaPlayer с ВСЕМИ привязками ──────────────────────────
            // Без него пресет был «мёртвым» — диалоги, выборы, персонажи не
            // работали. Теперь хватит назначить StoryTree в инспекторе плеера —
            // и можно сразу запускать сцену.
            var pGO = new GameObject("[Novella]_Player");
            AddPresetMarker(pGO, "Gameplay");
            var p = pGO.AddComponent<NovellaPlayer>();
            p.DialoguePanel       = dialogueBox;
            p.SpeakerNameText     = speakerName.GetComponent<TMPro.TMP_Text>();
            p.DialogueBodyText    = dialogueText.GetComponent<TMPro.TMP_Text>();
            p.ChoiceContainer     = choiceContainer.transform;
            p.CharactersContainer = charLayer.transform;
            // Авто-привязка StoryTree: ищем существующий NovellaTree в проекте
            // или создаём пустой Starter_Chapter. Без StoryTree NovellaPlayer
            // не запускается вообще.
            p.StoryTree           = EnsureStarterTree();

            // ─── Гардероб (MC Creation panel) — изначально скрыта ─────────
            // Юзер может показать её через NovellaUIBinding.ShowPanel из любой
            // кнопки HUD. Имена и компоненты совпадают с auto-find в StoryLauncher.
            BuildMCCreationPanel(canvas, active: false);

            Selection.activeGameObject = marker;
        }

        // ─── Helpers для setup ───

        private Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            // ScreenSpaceCamera + matchWidthOrHeight=0.5 + planeDistance —
            // те же настройки что и в UI Forge.
            // ВАЖНО: Camera.main может вернуть null сразу после создания
            // только что добавленной камеры (Unity кэширует тэг-поиск
            // до следующего тика). Поэтому ищем камеру руками — через
            // FindAnyObjectByType, а если ничего нет, оставляем worldCamera=null
            // и Forge сам её подставит при FindReferences.
            canvas.renderMode   = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera  = ResolveActiveCamera();
            canvas.planeDistance = 5f;

            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            return canvas;
        }

        // Возвращает активную камеру для канваса. Сначала Camera.main, если он
        // null — ищем любую через FindAnyObjectByType (Camera.main не успел
        // обновиться после AddComponent + tag).
        private static Camera ResolveActiveCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                cam = Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            return cam;
        }

        private void EnsureMainCamera()
        {
            if (ResolveActiveCamera() == null)
            {
                var camGO = new GameObject("[Novella]_Camera");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
                camGO.tag = "MainCamera";
                camGO.AddComponent<AudioListener>();
                AttachUrpAdditionalCameraData(camGO);
            }
        }

        // Если в проекте используется URP, у каждой Camera должен быть
        // компонент UniversalAdditionalCameraData. Без него URP сыпет в
        // консоль warning «Camera ... does not contain an additional camera
        // data component».
        // Используем рефлексию, чтобы код корректно собирался и в проектах
        // на Built-in pipeline (там этого типа нет).
        private static void AttachUrpAdditionalCameraData(GameObject camGO)
        {
            if (camGO == null) return;
            var urpType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpType != null && camGO.GetComponent(urpType) == null)
            {
                camGO.AddComponent(urpType);
            }
        }

        private void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("[Novella]_EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private GameObject CreateUIText(GameObject parent, string name, string text, int fontSize, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            // Создаём СРАЗУ TextMeshPro — legacy UnityEngine.UI.Text больше не используется,
            // плохо рендерится на больших экранах. Конвертация ручная больше не нужна.
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            // Маппим UnityEngine.FontStyle (Bold/Italic) в TMP-флаги.
            TMPro.FontStyles tmpStyle = TMPro.FontStyles.Normal;
            if ((style & FontStyle.Bold)   != 0) tmpStyle |= TMPro.FontStyles.Bold;
            if ((style & FontStyle.Italic) != 0) tmpStyle |= TMPro.FontStyles.Italic;
            t.fontStyle = tmpStyle;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.color = Color.white;
            return go;
        }

        // Перепривязывает якоря объекта на center-center и ставит anchoredPos.
        // Используется после CreateUIButton чтобы превратить top-anchored
        // кнопку в центрированную (для блока кнопок MainMenu).
        private static void SetCenterAnchorPos(GameObject go, Vector2 pos)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        private GameObject CreateUIButton(GameObject parent, string name, string label, Vector2 anchoredPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(360, 60);

            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);
            var btn = go.AddComponent<UnityEngine.UI.Button>();

            var labelGO = CreateUIText(go, "Label", label, 24, FontStyle.Bold);
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            return go;
        }

        private GameObject CreateUIImage(GameObject parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            return go;
        }

        // ═════════════════════════════════════════════════════════
        // EMPTY & MULTI-SELECT STATES
        // ═════════════════════════════════════════════════════════

        private void DrawEmptyState(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_PRIMARY);
            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            st.normal.textColor = C_TEXT_3;
            GUI.Label(r, ToolLang.Get("No scenes yet.\n← Click '＋ New scene' to create one.",
                                       "Сцен пока нет.\n← Нажми «＋ Новая сцена» чтобы создать."), st);
        }

        private void DrawMultiSelectState(Rect rect)
        {
            EditorGUI.DrawRect(rect, C_BG_PRIMARY);
            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(400));

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_1;
            GUILayout.Label(string.Format(ToolLang.Get("{0} scenes selected", "Выбрано сцен: {0}"), _selectedPaths.Count), st);

            int inBuildCount = _allScenes.Count(s => _selectedPaths.Contains(s.Path) && s.InBuild);
            int outBuildCount = _selectedPaths.Count - inBuildCount;

            GUILayout.Space(30);

            if (outBuildCount > 0)
            {
                if (DrawActionButton(GUILayoutUtility.GetRect(0, 36), "✓ " + ToolLang.Get("Add all to build", "Добавить все в сборку"), fill: false, danger: false))
                {
                    EditorApplication.delayCall += AddSelectedToBuild;
                }
                GUILayout.Space(10);
            }

            if (inBuildCount > 0)
            {
                if (DrawActionButton(GUILayoutUtility.GetRect(0, 36), "− " + ToolLang.Get("Remove all from build", "Убрать все из сборки"), fill: false, danger: false))
                {
                    EditorApplication.delayCall += RemoveSelectedFromBuild;
                }
                GUILayout.Space(10);
            }

            GUILayout.Space(20);

            Rect delR = GUILayoutUtility.GetRect(0, 40);
            bool delHover = delR.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(delR, delHover ? new Color(0.9f, 0.4f, 0.4f) : C_DANGER);
            var delSt = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            delSt.normal.textColor = Color.white;
            GUI.Label(delR, "🗑 " + ToolLang.Get("Delete Selected", "Удалить выбранные"), delSt);

            if (delHover && Event.current.type == EventType.MouseDown)
            {
                Event.current.Use();
                EditorApplication.delayCall += DeleteSelectedScenesWithConfirm;
            }
            if (delHover && Event.current.type == EventType.MouseMove) _window?.Repaint();

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        // ═════════════════════════════════════════════════════════
        // TRUNCATE ХЕЛПЕР ДЛЯ ОГРАНИЧЕНИЯ ДЛИНЫ СТРОКИ
        // ═════════════════════════════════════════════════════════

        private static string TruncateForWidth(string text, GUIStyle style, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (style.CalcSize(new GUIContent(text)).x <= maxWidth) return text;

            int low = 0, high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = text.Substring(0, mid) + "…";
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return text.Substring(0, Mathf.Max(1, low)) + "…";
        }

        // ═════════════════════════════════════════════════════════
        // FORMATTING / UI HELPERS
        // ═════════════════════════════════════════════════════════

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0f).ToString("F1") + " KB";
            return (bytes / 1024.0f / 1024.0f).ToString("F1") + " MB";
        }

        private static string FormatRelative(System.DateTime dt)
        {
            if (dt == System.DateTime.MinValue) return "—";
            var diff = System.DateTime.Now - dt;
            if (diff.TotalSeconds < 60) return ToolLang.Get("just now", "только что");
            if (diff.TotalMinutes < 60) return string.Format(ToolLang.Get("{0} min ago", "{0} мин назад"), (int)diff.TotalMinutes);
            if (diff.TotalHours < 24) return string.Format(ToolLang.Get("{0}h ago", "{0} ч назад"), (int)diff.TotalHours);
            if (diff.TotalDays < 7) return string.Format(ToolLang.Get("{0}d ago", "{0} д назад"), (int)diff.TotalDays);
            return dt.ToString("dd MMM yyyy");
        }

        private void DrawSectionHeader(string text)
        {
            var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_TEXT_3;
            GUILayout.Label(text, st);
            Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, C_BORDER);
            GUILayout.Space(8);
        }

        private bool DrawCreateButton(Rect r, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? C_BG_RAISED : C_BG_PRIMARY);
            DrawRectBorderDashed(r, hover ? C_ACCENT : C_BORDER);

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = hover ? C_ACCENT : C_TEXT_3;
            GUI.Label(r, label, st);

            if (hover && Event.current.type == EventType.MouseMove) _window?.Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawAccentButton(Rect r, string label)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? new Color(0.45f, 0.80f, 0.95f) : C_ACCENT);

            DrawRectBorder(r, new Color(0, 0, 0, 0.6f));

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = C_BG_PRIMARY;
            GUI.Label(r, label, st);

            if (hover && Event.current.type == EventType.MouseMove) _window?.Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawSquareIconBtn(Rect r, string icon, Color color)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(r, hover ? C_BG_RAISED : new Color(0, 0, 0, 0));
            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            st.normal.textColor = hover ? color : new Color(color.r, color.g, color.b, 0.7f);
            GUI.Label(r, icon, st);
            if (hover && Event.current.type == EventType.MouseMove) _window?.Repaint();
            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private bool DrawActionButton(Rect r, string label, bool fill, bool danger, bool disabled = false)
        {
            bool hover = !disabled && r.Contains(Event.current.mousePosition);

            Color borderC = danger ? new Color(0.65f, 0.18f, 0.18f) : C_TEXT_1;
            Color textCol = danger ? new Color(0.88f, 0.30f, 0.30f) : C_TEXT_1;

            if (disabled)
            {
                borderC = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f);
                textCol = C_TEXT_4;
            }
            else if (fill)
            {
                EditorGUI.DrawRect(r, hover ? Color.white : C_TEXT_1);
                textCol = C_BG_PRIMARY;
            }
            else
            {
                if (hover) EditorGUI.DrawRect(r, danger ? new Color(0.65f, 0.18f, 0.18f, 0.13f) : new Color(C_TEXT_1.r, C_TEXT_1.g, C_TEXT_1.b, 0.06f));
                DrawRectBorder(r, borderC);
            }

            if (fill && (hover || !disabled)) { /* already drawn above */ }
            else if (!fill) { /* border drawn */ }

            var st = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
            st.normal.textColor = textCol;
            GUI.Label(r, label, st);

            if (hover && Event.current.type == EventType.MouseMove) _window?.Repaint();
            if (!disabled && Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        private static void DrawRectBorderDashed(Rect r, Color c)
        {
            int dash = 4, gap = 3;
            for (int x = 0; x < r.width; x += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x + x, r.y, Mathf.Min(dash, r.width - x), 1), c);
                EditorGUI.DrawRect(new Rect(r.x + x, r.yMax - 1, Mathf.Min(dash, r.width - x), 1), c);
            }
            for (int y = 0; y < r.height; y += dash + gap)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y + y, 1, Mathf.Min(dash, r.height - y)), c);
            }
        }
    }
}