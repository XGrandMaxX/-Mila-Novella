using UnityEngine;
using UnityEditor;
using NovellaEngine.Data;
using System.Collections.Generic;
using System.Linq;

namespace NovellaEngine.Editor
{
    public class NovellaReferenceFinderWindow : EditorWindow
    {
        private string _targetVariable;
        private Vector2 _scrollPos;

        private class RefEntry
        {
            public NovellaTree Tree;
            public string NodeID;
            public string NodeTitle;
            public string Context;
        }

        private List<RefEntry> _references = new List<RefEntry>();

        public static void ShowWindow(string variableName)
        {
            var win = GetWindow<NovellaReferenceFinderWindow>(ToolLang.Get("References", "Зависимости"));
            win.minSize = new Vector2(400, 500);
            win._targetVariable = variableName;
            win.PerformSearch();
            win.ShowUtility();
        }

        private void PerformSearch()
        {
            _references.Clear();
            string[] guids = AssetDatabase.FindAssets("t:NovellaTree");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NovellaTree tree = AssetDatabase.LoadAssetAtPath<NovellaTree>(path);
                if (tree != null)
                {
                    foreach (var node in tree.Nodes)
                    {
                        bool isUsed = false;
                        string context = "";

                        // Проверка ноды Variable (Изменение переменной)
                        if (node is VariableNodeData varData && varData.Variables.Any(v => v.VariableName == _targetVariable))
                        {
                            isUsed = true; context = ToolLang.Get("Variable Update", "Обновление значения");
                        }
                        // Проверка ноды Condition (If/Else Условие)
                        else if (node is ConditionNodeData condData && condData.Conditions.Any(c => c.Variable == _targetVariable))
                        {
                            isUsed = true; context = ToolLang.Get("Condition Check", "Проверка условия");
                        }
                        // Проверка ветвлений (Branch) на блокировку вариантов ответа
                        else if (node is BranchNodeData branchData)
                        {
                            foreach (var choice in branchData.Choices)
                            {
                                if (choice.Conditions.Any(c => c.Variable == _targetVariable))
                                {
                                    isUsed = true; context = ToolLang.Get("Choice Lock", "Блокировка выбора");
                                    break;
                                }
                            }
                        }
                        // Проверка рандома (Random) на модификаторы шанса
                        else if (node is RandomNodeData rndData)
                        {
                            foreach (var choice in rndData.Choices)
                            {
                                if (choice.ChanceModifiers.Any(m => m.Variable == _targetVariable))
                                {
                                    isUsed = true; context = ToolLang.Get("Chance Modifier", "Модификатор шанса");
                                    break;
                                }
                            }
                        }

                        if (isUsed)
                        {
                            _references.Add(new RefEntry { Tree = tree, NodeID = node.NodeID, NodeTitle = node.NodeTitle, Context = context });
                        }
                    }
                }
            }
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label($"🔍 {ToolLang.Get("References for:", "Зависимости для:")} {_targetVariable}", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.2f, 0.8f, 0.5f) } });
            GUILayout.Space(10);

            if (_references.Count == 0)
            {
                EditorGUILayout.HelpBox(ToolLang.Get("No references found in any scenes.", "Не найдено ни одного использования в сценах."), MessageType.Info);
                return;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, EditorStyles.helpBox);

            var grouped = _references.GroupBy(r => r.Tree);

            foreach (var group in grouped)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
                GUILayout.Label($"📂 {group.Key.name}", new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold });
                GUI.backgroundColor = Color.white;
                GUILayout.Space(5);

                foreach (var r in group)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(15);
                    GUILayout.Label("• " + r.Context, EditorStyles.miniLabel, GUILayout.Width(130));

                    GUI.backgroundColor = new Color(0.2f, 0.6f, 0.8f);
                    if (GUILayout.Button(ToolLang.Get($"Open Node: {r.NodeTitle}", $"Перейти к: {r.NodeTitle}"), EditorStyles.miniButton, GUILayout.Height(22)))
                    {
                        OpenAndFocus(r.Tree, r.NodeID);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();
        }

        private void OpenAndFocus(NovellaTree tree, string nodeID)
        {
            NovellaGraphWindow.OpenGraphWindow(tree);
            var win = GetWindow<NovellaGraphWindow>("Novella Editor");
            win.FocusAndHighlightNode(nodeID);
        }
    }
}