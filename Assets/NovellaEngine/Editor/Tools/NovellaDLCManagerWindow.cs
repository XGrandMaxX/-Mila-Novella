using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System.Linq;
using System.Collections.Generic;

namespace NovellaEngine.Editor
{
    public class NovellaDLCManagerWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private int _currentTab = 0;
        private string[] _tabs;

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
            var win = GetWindow<NovellaDLCManagerWindow>(ToolLang.Get("DLC Manager", "Менеджер DLC"));
            win.minSize = new Vector2(450, 450);

            win.ShowUtility();
            win.Focus();
        }

        private void OnEnable()
        {
            _tabs = new string[] { "🧩 " + ToolLang.Get("Active Modules", "Активные модули"), "🗑 " + ToolLang.Get("Trash Bin", "Корзина") };
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            _currentTab = GUILayout.Toolbar(_currentTab, _tabs, GUILayout.Height(30));
            GUILayout.Space(10);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            List<DLCItem> allDLCs = GetAllDLCs();

            if (_currentTab == 0) DrawActiveDLCs(allDLCs.Where(d => !d.IsTrashed).ToList());
            else DrawTrashBin(allDLCs.Where(d => d.IsTrashed).ToList());

            GUILayout.EndScrollView();
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
            NovellaTree currentTree = activeGraph != null ? activeGraph.GetType().GetField("_currentTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(activeGraph) as NovellaTree : null;

            foreach (var item in activeList)
            {
                bool isEnabled = settings.IsDLCEnabled(item.SystemName);

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();

                ColorUtility.TryParseHtmlString(item.HexColor, out Color dlcColor);
                GUI.color = isEnabled ? dlcColor : Color.gray;
                GUILayout.Label(isEnabled ? "✔" : "✖", EditorStyles.boldLabel, GUILayout.Width(20));
                GUI.color = Color.white;

                bool hasDescription = !string.IsNullOrWhiteSpace(item.Description);
                bool isExpanded = hasDescription && _expandedStates.TryGetValue(item.SystemName, out bool exp) && exp;

                GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = isEnabled ? Color.white : Color.gray } };

                if (hasDescription)
                {
                    nameStyle.hover.textColor = Color.white;
                    string arrow = isExpanded ? "▼ " : "▶ ";
                    if (GUILayout.Button(arrow + item.MenuName, nameStyle, GUILayout.ExpandWidth(false)))
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
                    if (count > 0) GUILayout.Label($"({count} {ToolLang.Get("nodes", "нод")})", EditorStyles.miniBoldLabel);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"v. {item.Version}", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                bool newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.SetDLCState(item.SystemName, newEnabled);
                    RefreshGraphs();
                }

                if (GUILayout.Button("🗑", EditorStyles.miniButton, GUILayout.Width(30)))
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
                GUILayout.BeginHorizontal();

                GUI.color = Color.gray;
                GUILayout.Label("✖", EditorStyles.boldLabel, GUILayout.Width(20));
                GUI.color = Color.white;

                bool hasDescription = !string.IsNullOrWhiteSpace(item.Description);
                bool isExpanded = hasDescription && _expandedStates.TryGetValue(item.SystemName, out bool exp) && exp;

                GUIStyle trashNameStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } };

                if (hasDescription)
                {
                    trashNameStyle.hover.textColor = Color.white;
                    string arrow = isExpanded ? "▼ " : "▶ ";
                    if (GUILayout.Button(arrow + item.MenuName, trashNameStyle, GUILayout.ExpandWidth(false)))
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
                if (GUILayout.Button("♻ " + ToolLang.Get("Restore", "Восстановить"), EditorStyles.miniButtonLeft, GUILayout.Width(100)))
                {
                    settings.SetDLCTrashed(item.SystemName, false);
                    RefreshGraphs();
                }

                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑 " + ToolLang.Get("Delete", "Удалить"), EditorStyles.miniButtonRight, GUILayout.Width(80)))
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
                        AssetDatabase.DeleteAsset(sPath);
                        AssetDatabase.Refresh();
                    };
                    GUIUtility.ExitGUI();
                    break;
                }
            }
        }
    }
}