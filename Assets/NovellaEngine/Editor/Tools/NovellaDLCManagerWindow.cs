using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaDLCManagerWindow : EditorWindow
    {
        private Vector2 _scrollPos;

        public static void ShowWindow()
        {
            var win = GetWindow<NovellaDLCManagerWindow>(ToolLang.Get("DLC Manager", "Менеджер DLC"));
            win.minSize = new Vector2(450, 400);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🧩 " + ToolLang.Get("Installed DLC Modules", "Установленные модули DLC"), new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);

            var settings = NovellaDLCSettings.Instance;
            var dlcTypes = TypeCache.GetTypesDerivedFrom<NovellaNodeBase>()
                .Where(t => t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).Length > 0)
                .ToList();

            if (dlcTypes.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolLang.Get(
                    "No DLC modules found.\n\nTo install a DLC, simply import its .unitypackage into the project. The engine will detect it automatically!",
                    "Модули DLC не найдены.\n\nЧтобы установить DLC, просто импортируйте его .unitypackage в проект. Движок найдет его автоматически!"
                ), MessageType.Info);
                return;
            }

            GUIStyle helpStyle = new GUIStyle(EditorStyles.helpBox) { richText = true, fontSize = 12 };
            GUILayout.Label(ToolLang.Get(
                "<b>Graceful Degradation (Pass-through)</b>\nIf you disable a DLC here, its nodes will be hidden from the Creation Menu, grayed out on the Graph, and locked in the Inspector. In the game, the player will simply <b>skip</b> these nodes!",
                "<b>Умный пропуск (Pass-through)</b>\nПри выключении DLC, его ноды пропадут из меню, заблокируются в Инспекторе и станут прозрачными. В игре плеер <b>проскочит</b> эти ноды насквозь!"
            ), helpStyle);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ToolLang.Get("Enable All", "Включить всё"), EditorStyles.miniButtonLeft, GUILayout.Height(25))) ToggleAll(dlcTypes, settings, true);
            if (GUILayout.Button(ToolLang.Get("Disable All", "Выключить всё"), EditorStyles.miniButtonRight, GUILayout.Height(25))) ToggleAll(dlcTypes, settings, false);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            foreach (var type in dlcTypes)
            {
                var attr = (NovellaDLCNodeAttribute)type.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).First();
                string dlcID = type.FullName;
                bool isEnabled = settings.IsDLCEnabled(dlcID);

                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();

                ColorUtility.TryParseHtmlString(attr.HexColor, out Color dlcColor);
                GUI.color = isEnabled ? dlcColor : Color.gray;
                string checkIcon = isEnabled ? "✔" : "✖";
                GUILayout.Label(checkIcon, EditorStyles.boldLabel, GUILayout.Width(20));
                GUI.color = Color.white;

                GUILayout.Label(attr.MenuName, new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = isEnabled ? Color.white : Color.gray } });
                GUILayout.FlexibleSpace();
                GUILayout.Label($"v. {attr.Version}", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                bool newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.SetDLCState(dlcID, newEnabled);
                    RefreshGraphs();
                }

                if (GUILayout.Button("🗑", EditorStyles.miniButton, GUILayout.Width(30))) DeleteDLC(type, attr);

                GUILayout.EndHorizontal();

                GUILayout.Space(5);
                GUILayout.Label(ToolLang.Get("System Name: ", "Системное имя: ") + type.Name, EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();
        }

        private void ToggleAll(System.Collections.Generic.List<System.Type> dlcTypes, NovellaDLCSettings settings, bool state)
        {
            foreach (var type in dlcTypes) settings.SetDLCState(type.FullName, state);
            RefreshGraphs();
        }

        private void RefreshGraphs()
        {
            var graphWindows = Resources.FindObjectsOfTypeAll<NovellaGraphWindow>();
            foreach (var gw in graphWindows) { gw.RefreshAllNodes(); gw.Repaint(); }
        }

        private void DeleteDLC(System.Type type, NovellaDLCNodeAttribute attr)
        {
            if (EditorUtility.DisplayDialog(
                ToolLang.Get("Delete DLC?", "Удалить DLC?"),
                ToolLang.Get(
                    $"Are you sure you want to permanently delete the '{attr.MenuName}' DLC?\n\nWARNING: All nodes of this type will be REMOVED from all graphs to prevent corruption. Connections will be broken. Backup recommended!",
                    $"Вы уверены, что хотите навсегда удалить DLC '{attr.MenuName}'?\n\nВНИМАНИЕ: Все ноды этого типа будут УДАЛЕНЫ из всех графов, чтобы не сломать проект. Связи к ним оборвутся. Сделайте бэкап!"
                ), ToolLang.Get("Yes, Delete", "Да, Удалить"), ToolLang.Get("Cancel", "Отмена")))
            {
                string[] treeGuids = AssetDatabase.FindAssets("t:NovellaTree");
                foreach (var guid in treeGuids)
                {
                    var tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(AssetDatabase.GUIDToAssetPath(guid));
                    if (tree != null)
                    {
                        int removed = tree.Nodes.RemoveAll(n => n.GetType() == type);
                        if (removed > 0) { EditorUtility.SetDirty(tree); AssetDatabase.SaveAssets(); }
                    }
                }

                string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript " + type.Name);
                foreach (var sGuid in scriptGuids)
                {
                    string sPath = AssetDatabase.GUIDToAssetPath(sGuid);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(sPath);
                    if (script != null && script.GetClass() == type)
                    {
                        AssetDatabase.DeleteAsset(sPath);
                        RefreshGraphs();
                        GUIUtility.ExitGUI();
                        break;
                    }
                }
            }
        }
    }
}