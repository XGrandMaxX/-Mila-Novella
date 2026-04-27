using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System.Linq;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    public class NovellaDLCManagerModule : EditorWindow
    {
        private Vector2 _scrollPos;
        private int _currentTab = 0;

        private NovellaTabState _tabState = new NovellaTabState();
        private Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();

        private class DLCItem
        {
            public string SystemName;
            public string MenuName;
            public string Version;
            public string HexColor;
            public string Description;
            public bool IsTrashed;
            public System.Type ClassType;
        }

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaDLCManagerModule>(ToolLang.Get("DLC Manager", "Менеджер DLC"));
            win.minSize = new Vector2(650, 500);

            win.ShowUtility();
            win.Focus();
        }

        private void OnEnable()
        {
            _tabState.Initialize(Repaint);
            _tabState.SetActive(_currentTab.ToString());
            EditorApplication.update += _tabState.Update;
        }

        private void OnDisable()
        {
            EditorApplication.update -= _tabState.Update;
        }

        private void DrawAACapsule(Rect rect, Color color)
        {
            Handles.color = color;
            float radius = Mathf.Min(rect.width / 2f, rect.height / 2f);
            int segments = 18;
            Vector3[] points = new Vector3[segments * 2];

            Vector2 leftCenter = new Vector2(rect.x + radius, rect.y + radius);
            Vector2 rightCenter = new Vector2(rect.xMax - radius, rect.y + radius);

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 0.5f + (i * Mathf.PI / (segments - 1));
                points[i] = new Vector3(leftCenter.x + Mathf.Cos(angle) * radius, leftCenter.y + Mathf.Sin(angle) * radius, 0f);
            }

            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 1.5f + (i * Mathf.PI / (segments - 1));
                points[i + segments] = new Vector3(rightCenter.x + Mathf.Cos(angle) * radius, rightCenter.y + Mathf.Sin(angle) * radius, 0f);
            }

            Handles.DrawAAConvexPolygon(points);
            Handles.color = Color.white;
        }

        private bool DrawToggleSwitch(Rect rect, bool value)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color bgColor = value ? new Color(0.2f, 0.7f, 0.3f) : new Color(0.35f, 0.35f, 0.35f);
                DrawAACapsule(rect, bgColor);

                Color knobColor = Color.white;
                float radius = Mathf.Min(rect.width / 2f, rect.height / 2f);
                float knobX = value ? rect.xMax - radius : rect.x + radius;
                Rect knobRect = new Rect(knobX - radius + 2f, rect.y + 2f, radius * 2f - 4f, radius * 2f - 4f);
                DrawAACapsule(knobRect, knobColor);
            }
            return GUI.Button(rect, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🔌 " + ToolLang.Get("DLC Modules Manager", "Менеджер модулей DLC"),
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(225));

            if (NovellaEditorLayout.DrawAnimatedTab("0", "🧩", ToolLang.Get("Active Modules", "Активные модули"),
                _tabState, new Color(0.15f, 0.5f, 0.75f), 180f, 220f))
            {
                _currentTab = 0;
                _tabState.SetActive("0");
                GUI.FocusControl(null);
            }

            GUILayout.Space(5);

            if (NovellaEditorLayout.DrawAnimatedTab("1", "🗑", ToolLang.Get("Trash Bin", "Корзина"),
                _tabState, new Color(0.8f, 0.3f, 0.3f), 180f, 220f))
            {
                _currentTab = 1;
                _tabState.SetActive("1");
                GUI.FocusControl(null);
            }

            GUILayout.EndVertical();

            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            List<DLCItem> allDLCs = GetAllDLCs();

            if (_currentTab == 0) DrawActiveDLCs(allDLCs.Where(d => !d.IsTrashed).ToList());
            else DrawTrashBin(allDLCs.Where(d => d.IsTrashed).ToList());

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private List<DLCItem> GetAllDLCs()
        {
            List<DLCItem> list = new List<DLCItem>();
            var settings = NovellaDLCSettings.Instance;

            var dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0);

            foreach (var type in dlcTypes)
            {
                var attr = (NovellaDLCNodeAttribute)type.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).First();
                bool trashed = settings.IsDLCTrashed(type.FullName);

                list.Add(new DLCItem
                {
                    SystemName = type.FullName,
                    MenuName = attr.MenuName,
                    Version = attr.Version,
                    HexColor = attr.HexColor,
                    Description = attr.Description,
                    IsTrashed = trashed,
                    ClassType = type
                });
            }

            return list.OrderBy(d => d.MenuName).ToList();
        }

        private void DrawActiveDLCs(List<DLCItem> activeList)
        {
            var settings = NovellaDLCSettings.Instance;

            if (activeList.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("No active DLC modules found.", "Активные модули DLC не найдены."), MessageType.Info);
                return;
            }

            GUIStyle helpStyle = new GUIStyle(EditorStyles.helpBox) { richText = true, fontSize = 12 };
            GUILayout.Label(ToolLang.Get(
                "<b>Graceful Degradation (Pass-through)</b>\nIf disabled, the player will simply <b>skip</b> these nodes in the game!\n<i>Click on a module name to see its description.</i>",
                "<b>Умный пропуск (Pass-through)</b>\nПри выключении, в игре плеер <b>проскочит</b> эти ноды насквозь!\n<i>Нажмите на имя модуля, чтобы прочитать описание.</i>"
            ), helpStyle);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ToolLang.Get("Enable All", "Включить всё"), EditorStyles.miniButtonLeft, GUILayout.Height(25))) { foreach (var d in activeList) settings.SetDLCState(d.SystemName, true); RefreshGraphs(); }
            if (GUILayout.Button(ToolLang.Get("Disable All", "Выключить всё"), EditorStyles.miniButtonRight, GUILayout.Height(25))) { foreach (var d in activeList) settings.SetDLCState(d.SystemName, false); RefreshGraphs(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            var activeGraph = Resources.FindObjectsOfTypeAll<NovellaGraphWindow>().FirstOrDefault();
            NovellaTree currentTree = null;
            if (activeGraph != null)
            {
                var field = activeGraph.GetType().GetField("_currentTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentTree = field?.GetValue(activeGraph) as NovellaTree;
            }

            foreach (var item in activeList)
            {
                bool isEnabled = settings.IsDLCEnabled(item.SystemName);

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal(GUILayout.Height(25));

                ColorUtility.TryParseHtmlString(item.HexColor, out Color dlcColor);
                GUI.color = isEnabled ? dlcColor : Color.gray;
                GUILayout.Label(isEnabled ? "✔" : "✖", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft }, GUILayout.Width(20));
                GUI.color = Color.white;

                bool hasDescription = !string.IsNullOrWhiteSpace(item.Description);
                bool isExpanded = hasDescription && _expandedStates.TryGetValue(item.SystemName, out bool exp) && exp;

                GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = isEnabled ? Color.white : Color.gray } };

                if (hasDescription)
                {
                    nameStyle.hover.textColor = Color.white;
                    string arrow = isExpanded ? "▼ " : "▶ ";
                    if (GUILayout.Button(arrow + item.MenuName, nameStyle, GUILayout.ExpandWidth(false), GUILayout.Height(25)))
                    {
                        _expandedStates[item.SystemName] = !isExpanded;
                    }
                }
                else
                {
                    GUILayout.Label(item.MenuName, nameStyle, GUILayout.ExpandWidth(false));
                }

                if (currentTree != null && item.ClassType != null)
                {
                    int count = currentTree.Nodes.Count(n => n.GetType() == item.ClassType);
                    if (count > 0) GUILayout.Label($"({count} {ToolLang.Get("nodes", "нод")})", new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft }, GUILayout.Height(25));
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"v. {item.Version}", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight }, GUILayout.Height(25));

                GUILayout.Space(10);

                Rect toggleRect = GUILayoutUtility.GetRect(40, 20);
                toggleRect.y += 2;
                bool newEnabled = DrawToggleSwitch(toggleRect, isEnabled);
                if (newEnabled != isEnabled)
                {
                    settings.SetDLCState(item.SystemName, newEnabled);
                    RefreshGraphs();
                }

                GUILayout.Space(10);

                if (GUILayout.Button("🗑", EditorStyles.miniButton, GUILayout.Width(30), GUILayout.Height(25)))
                {
                    settings.SetDLCTrashed(item.SystemName, true);
                    RefreshGraphs();
                }

                GUILayout.EndHorizontal();

                if (isExpanded)
                {
                    GUILayout.Space(2);
                    GUILayout.Label(item.Description, new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontSize = 11, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } });
                }

                GUILayout.Space(5);
                GUILayout.Label(ToolLang.Get("System Name: ", "Системное имя: ") + item.SystemName, EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void DrawTrashBin(List<DLCItem> trashedList)
        {
            var settings = NovellaDLCSettings.Instance;

            if (trashedList.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("Trash bin is empty.", "Корзина пуста."), MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(ToolLang.Get("These modules are virtually deleted and hidden from your project. Click 'Restore' to bring them back or 'Delete' to permanently remove their scripts.", "Эти модули виртуально удалены и скрыты из графов. Нажмите 'Восстановить', чтобы вернуть их, или 'Удалить', чтобы навсегда стереть их скрипты."), MessageType.Warning);
            GUILayout.Space(10);

            foreach (var item in trashedList)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal(GUILayout.Height(25));

                GUI.color = Color.gray;
                GUILayout.Label("✖", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft }, GUILayout.Width(20));
                GUI.color = Color.white;

                bool hasDescription = !string.IsNullOrWhiteSpace(item.Description);
                bool isExpanded = hasDescription && _expandedStates.TryGetValue(item.SystemName, out bool exp) && exp;

                GUIStyle trashNameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } };

                if (hasDescription)
                {
                    trashNameStyle.hover.textColor = Color.white;
                    string arrow = isExpanded ? "▼ " : "▶ ";
                    if (GUILayout.Button(arrow + item.MenuName, trashNameStyle, GUILayout.ExpandWidth(false), GUILayout.Height(25)))
                    {
                        _expandedStates[item.SystemName] = !isExpanded;
                    }
                }
                else
                {
                    GUILayout.Label(item.MenuName, trashNameStyle, GUILayout.ExpandWidth(false));
                }

                GUILayout.FlexibleSpace();

                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
                if (GUILayout.Button("♻ " + ToolLang.Get("Restore", "Восстановить"), EditorStyles.miniButtonLeft, GUILayout.Width(100), GUILayout.Height(25)))
                {
                    settings.SetDLCTrashed(item.SystemName, false);
                    RefreshGraphs();
                }

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑 " + ToolLang.Get("Delete", "Удалить"), EditorStyles.miniButtonRight, GUILayout.Width(80), GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog("Delete", ToolLang.Get($"Delete '{item.MenuName}' permanently?", $"Навсегда удалить скрипт '{item.MenuName}'?"), ToolLang.Get("Yes", "Да"), ToolLang.Get("No", "Нет")))
                    {
                        DeletePermanently(item);
                    }
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();

                if (isExpanded)
                {
                    GUILayout.Space(2);
                    GUILayout.Label(item.Description, new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontSize = 11, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } });
                }

                GUILayout.Space(5);
                GUILayout.Label(ToolLang.Get("System Name: ", "Системное имя: ") + item.SystemName, EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }
        }

        private void RefreshGraphs()
        {
            var graphWindows = Resources.FindObjectsOfTypeAll<NovellaGraphWindow>();
            foreach (var gw in graphWindows) { gw.RefreshAllNodes(); gw.Repaint(); }
        }

        // =========================================================
        // ПУЛЕНЕПРОБИВАЕМОЕ УДАЛЕНИЕ ПАПОК DLC
        // =========================================================
        private void DeletePermanently(DLCItem item)
        {
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript " + item.ClassType.Name);
            foreach (var sGuid in scriptGuids)
            {
                string sPath = AssetDatabase.GUIDToAssetPath(sGuid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(sPath);

                if (script != null && script.GetClass() == item.ClassType)
                {
                    NovellaDLCSettings.Instance.RemoveDLCRecord(item.SystemName);

                    EditorApplication.delayCall += () => {
                        string normalizedPath = sPath.Replace("\\", "/");
                        string dlcMarker = "/DLC/";
                        int markerIdx = normalizedPath.IndexOf(dlcMarker);

                        // Если скрипт находится внутри какой-то папки DLC
                        if (markerIdx != -1)
                        {
                            int startOfFolder = markerIdx + dlcMarker.Length;
                            int endOfFolder = normalizedPath.IndexOf('/', startOfFolder);

                            if (endOfFolder != -1)
                            {
                                // Формируем путь к коренной папке модуля (например "Assets/NovellaEngine/DLC/Wardrobe")
                                string rootDlcPath = normalizedPath.Substring(0, endOfFolder);
                                AssetDatabase.DeleteAsset(rootDlcPath);
                            }
                            else
                            {
                                // Скрипт лежит прямо в корне DLC, удаляем только его
                                AssetDatabase.DeleteAsset(sPath);
                            }
                        }
                        else
                        {
                            // Если пользователь вытащил скрипт из папки DLC в Runtime/Scripts, удаляем только сам скрипт!
                            AssetDatabase.DeleteAsset(sPath);
                        }

                        AssetDatabase.Refresh();
                    };

                    GUIUtility.ExitGUI();
                    break;
                }
            }
        }
    }
}