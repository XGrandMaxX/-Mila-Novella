// ════════════════════════════════════════════════════════════════════════════
// NovellaPrefabBrowserWindow — окно-браузер префабов из Gallery/Prefabs.
// Создание / открытие в mock-сцене / удаление / просмотр истории.
// MVP-инфраструктура для prefab-редактирования в Кузнице — пока без
// inline-режима внутри самой Кузницы.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaPrefabBrowserWindow : EditorWindow
    {
        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2   => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        private Vector2 _scroll;
        private string _filter = "";
        private List<GameObject> _prefabs = new List<GameObject>();
        private string _selectedPath;

        public static void Show()
        {
            var win = GetWindow<NovellaPrefabBrowserWindow>(false,
                ToolLang.Get("Prefabs", "Префабы"), true);
            win.titleContent = new GUIContent(ToolLang.Get("Prefabs", "Префабы"));
            win.minSize = new Vector2(560, 420);
            win.RefreshList();
            win.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            RefreshList();
        }

        private void RefreshList()
        {
            _prefabs.Clear();
            string folder = NovellaPrefabHistory.PREFABS_DIR;
            if (!AssetDatabase.IsValidFolder(folder)) return;

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) _prefabs.Add(go);
            }
            _prefabs.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            // ── Toolbar ──────────────────────────────────────────────────
            const float toolbarH = 44f;
            Rect tb = new Rect(0, 0, position.width, toolbarH);
            EditorGUI.DrawRect(tb, C_BG_RAISED);
            EditorGUI.DrawRect(new Rect(0, toolbarH - 1, position.width, 1), C_BORDER);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);

            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📦 " + ToolLang.Get("Prefabs library", "Библиотека префабов"), titleSt, GUILayout.Width(220));

            GUILayout.FlexibleSpace();

            // Search.
            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.Width(220), GUILayout.Height(22));

            GUILayout.Space(8);

            // Create button.
            var createSt = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11, fixedHeight = 22, padding = new RectOffset(10, 10, 2, 2),
                fontStyle = FontStyle.Bold,
            };
            createSt.normal.textColor = Color.white;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = C_ACCENT;
            if (GUILayout.Button("✨ " + ToolLang.Get("Create", "Создать"), createSt, GUILayout.Width(110)))
            {
                NovellaPrefabCreateDialog.Show(_ =>
                {
                    RefreshList();
                    Repaint();
                });
            }
            GUI.backgroundColor = prevBg;

            GUILayout.Space(6);

            // History button.
            var histSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 22, padding = new RectOffset(10, 10, 2, 2) };
            histSt.normal.textColor = C_TEXT_2;
            if (GUILayout.Button("📜 " + ToolLang.Get("History", "История"), histSt, GUILayout.Width(110)))
            {
                NovellaPrefabHistoryDialog.Show();
            }

            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // ── Body — список prefab'ов ───────────────────────────────────
            string filter = _filter == null ? "" : _filter.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(filter)
                ? _prefabs
                : _prefabs.Where(p => p != null && p.name.ToLowerInvariant().Contains(filter)).ToList();

            if (filtered.Count == 0)
            {
                GUILayout.Space(60);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                emptySt.normal.textColor = C_TEXT_3;
                if (string.IsNullOrEmpty(filter))
                    GUILayout.Label(ToolLang.Get("No prefabs yet. Click «Create» to make one.",
                                                  "Префабов пока нет. Нажми «Создать» чтобы сделать первый."), emptySt);
                else
                    GUILayout.Label(ToolLang.Get("No prefabs match the filter.",
                                                  "Ни один префаб не подходит под фильтр."), emptySt);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
            for (int i = 0; i < filtered.Count; i++)
            {
                DrawPrefabRow(filtered[i], i);
            }
            GUILayout.EndScrollView();
        }

        private void DrawPrefabRow(GameObject prefab, int index)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            bool selected = (_selectedPath == path);

            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            Rect row = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            GUILayout.Space(14);
            GUILayout.EndHorizontal();

            Color bg = selected
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.20f)
                : (index % 2 == 0 ? new Color(1, 1, 1, 0.02f) : Color.clear);
            EditorGUI.DrawRect(row, bg);

            // Превью-thumbnail.
            Rect thumbR = new Rect(row.x + 6, row.y + 4, 48, 48);
            EditorGUI.DrawRect(thumbR, C_BG_RAISED);
            var preview = AssetPreview.GetAssetPreview(prefab);
            if (preview != null)
            {
                GUI.DrawTexture(thumbR, preview, ScaleMode.ScaleToFit, true);
            }
            else
            {
                var iconSt = new GUIStyle(EditorStyles.label) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
                iconSt.normal.textColor = C_TEXT_3;
                GUI.Label(thumbR, GuessIcon(prefab), iconSt);
            }

            // Name + path.
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            nameSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(row.x + 64, row.y + 6, row.width - 200, 18), prefab.name, nameSt);

            var pathSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, clipping = TextClipping.Clip };
            pathSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(row.x + 64, row.y + 24, row.width - 200, 14), path, pathSt);

            // Actions: Open / Reveal / Delete.
            float bx = row.xMax - 130;
            var actSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 10, fixedHeight = 22, padding = new RectOffset(8, 8, 2, 2) };
            actSt.normal.textColor = C_TEXT_2;

            if (GUI.Button(new Rect(bx, row.y + 10, 60, 22), "📂 " + ToolLang.Get("Open", "Открыть"), actSt))
            {
                OpenPrefabInMockScene(prefab);
            }
            bx += 64;

            var delSt = new GUIStyle(actSt);
            delSt.normal.textColor = new Color(0.92f, 0.36f, 0.36f);
            if (GUI.Button(new Rect(bx, row.y + 10, 56, 22), "🗑 " + ToolLang.Get("Del", "Удал"), delSt))
            {
                if (EditorUtility.DisplayDialog(
                    ToolLang.Get("Delete prefab?", "Удалить префаб?"),
                    string.Format(ToolLang.Get("Delete '{0}'?\n\nThis cannot be undone via Ctrl+Z (asset is removed from disk).",
                                                "Удалить «{0}»?\n\nCtrl+Z это не откатит — ассет удалится с диска."),
                                  prefab.name),
                    ToolLang.Get("Delete", "Удалить"),
                    ToolLang.Get("Cancel", "Отмена")))
                {
                    NovellaPrefabHistory.Log("delete", prefab.name, "", path);
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.Refresh();
                    RefreshList();
                    Repaint();
                }
            }

            // Click on row → select.
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _selectedPath = path;
                EditorGUIUtility.PingObject(prefab);
                Repaint();
                if (Event.current.clickCount >= 2)
                {
                    OpenPrefabInMockScene(prefab);
                }
                Event.current.Use();
            }
        }

        private static void OpenPrefabInMockScene(GameObject prefab)
        {
            if (!NovellaPrefabSceneHelper.OpenMockScene()) return;
            var instance = NovellaPrefabSceneHelper.InstantiatePrefabInMockScene(prefab);
            if (instance != null)
            {
                Selection.activeGameObject = instance;
                EditorGUIUtility.PingObject(instance);
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static string GuessIcon(GameObject prefab)
        {
            if (prefab.GetComponent<UnityEngine.UI.Button>() != null) return "🔘";
            if (prefab.GetComponent<TMPro.TMP_Text>() != null) return "📝";
            if (prefab.GetComponent<UnityEngine.UI.Image>() != null) return "🖼";
            return "▣";
        }
    }
}
