// ════════════════════════════════════════════════════════════════════════════
// NovellaAssetImportDialog — drag-and-drop импорт картинок и аудио в Gallery.
//
// Юзер кидает файл в окно (или с диска, или из Project window) — диалог
// определяет категорию по имени/размерам/формату и предлагает переложить
// в правильную папку Gallery/<Category>. Юзер может переопределить категорию
// дропдауном перед подтверждением.
//
// Зачем: чтобы не лазить в Project window и Asset import settings руками.
// ════════════════════════════════════════════════════════════════════════════

using NovellaEngine.Data;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    public class NovellaAssetImportDialog : EditorWindow
    {
        public enum AssetCategory
        {
            Background,    // фоны сцен (широкие)
            CG,            // event-CG / иллюстрации
            Character,     // вертикальные спрайты персонажей
            UIIcon,        // иконки UI (~64-256, квадратные)
            Audio,         // музыка / звуки
            Other,         // всё прочее
        }

        // Корневая папка галереи. Категории — подпапки внутри.
        private const string GALLERY_ROOT = "Assets/NovellaEngine/Gallery";

        private static Color C_BG       => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_RAISED=> NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER   => NovellaSettingsModule.GetBorderColor();
        private static Color C_TEXT_1   => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_3   => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4   => NovellaSettingsModule.GetTextDisabled();
        private static Color C_ACCENT   => NovellaSettingsModule.GetAccentColor();

        // Pending список — файлы которые юзер набросал, ещё не подтвердил.
        private List<PendingItem> _items = new List<PendingItem>();
        private Vector2 _scroll;

        private class PendingItem
        {
            public string SourcePath;     // абсолютный путь на диске или asset path
            public string FileName;
            public Texture2D Preview;
            public AssetCategory Category;
            public long FileSizeBytes;
            public Vector2Int ImageSize;
            public bool IsImage;
            public bool IsAudio;
        }

        public static void Open()
        {
            // Single-instance.
            foreach (var existing in Resources.FindObjectsOfTypeAll<NovellaAssetImportDialog>())
            {
                if (existing != null) existing.Close();
            }

            var win = CreateInstance<NovellaAssetImportDialog>();
            win.titleContent = new GUIContent(ToolLang.Get("Import assets", "Импорт ассетов"));
            var size = new Vector2(560, 460);
            var main = EditorGUIUtility.GetMainWindowPosition();
            win.position = new Rect(
                main.x + (main.width - size.x) * 0.5f,
                main.y + (main.height - size.y) * 0.5f,
                size.x, size.y);
            win.minSize = size;
            win.ShowUtility();
            win.Focus();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var titleSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            titleSt.normal.textColor = C_TEXT_1;
            GUILayout.Label("📥  " + ToolLang.Get("Import assets", "Импорт ассетов"), titleSt);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            var subSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, wordWrap = true };
            subSt.normal.textColor = C_TEXT_3;
            GUILayout.Label(ToolLang.Get(
                "Drag files (or files from Project) into the area below. We'll guess what each is and put it in the right folder of the Gallery.",
                "Перетащи файлы (с диска или из Project) в область ниже. Мы угадаем что это и положим в нужную папку Галереи."), subSt);
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Drop-zone.
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect dropRect = GUILayoutUtility.GetRect(0, 80, GUILayout.ExpandWidth(true));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            DrawDropZone(dropRect);
            HandleDrop(dropRect);

            GUILayout.Space(10);

            // Список pending-итемов.
            if (_items.Count == 0)
            {
                GUILayout.Space(20);
                var emptySt = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                emptySt.normal.textColor = C_TEXT_4;
                GUILayout.Label(ToolLang.Get("No files yet. Drop something above to start.",
                                              "Пока пусто. Перетащи файлы в область выше."), emptySt);
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
                for (int i = 0; i < _items.Count; i++)
                {
                    DrawItemRow(_items[i], i);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();

            // Footer.
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true)), C_BORDER);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);

            var cancelSt = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 28, padding = new RectOffset(16, 16, 4, 4) };
            cancelSt.normal.textColor = C_TEXT_3;
            if (GUILayout.Button(ToolLang.Get("Close", "Закрыть"), cancelSt, GUILayout.Width(100)))
            {
                Close();
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_items.Count == 0))
            {
                GUI.backgroundColor = C_ACCENT;
                if (GUILayout.Button("📥 " + string.Format(ToolLang.Get("Import {0} files", "Импортировать {0} файла(ов)"), _items.Count),
                        GUILayout.Width(220), GUILayout.Height(28)))
                {
                    DoImport();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.Space(20);
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
        }

        private void DrawDropZone(Rect r)
        {
            bool dragActive = DragAndDrop.objectReferences.Length > 0 || DragAndDrop.paths.Length > 0;
            bool hover = r.Contains(Event.current.mousePosition);

            Color bg = (dragActive && hover)
                ? new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.20f)
                : new Color(1, 1, 1, 0.04f);
            EditorGUI.DrawRect(r, bg);

            // Dashed-style border.
            Color border = dragActive && hover ? C_ACCENT : new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.7f);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);

            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = dragActive && hover ? C_ACCENT : C_TEXT_3;
            GUI.Label(r, "📥  " + ToolLang.Get("Drop files here", "Перетащи файлы сюда"), st);
        }

        private void HandleDrop(Rect dropRect)
        {
            var e = Event.current;
            if (!dropRect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.DragUpdated:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    e.Use();
                    Repaint();
                    break;
                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    foreach (string path in DragAndDrop.paths)
                    {
                        AddPath(path);
                    }
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj == null) continue;
                        var p = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(p)) AddPath(p);
                    }
                    e.Use();
                    Repaint();
                    break;
            }
        }

        private void AddPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (Directory.Exists(path))
            {
                // Папка — рекурсивно её содержимое (только файлы первого уровня).
                foreach (var f in Directory.GetFiles(path))
                {
                    AddPath(f);
                }
                return;
            }
            if (!File.Exists(path)) return;

            // Дубль? Skip.
            foreach (var it in _items)
            {
                if (string.Equals(it.SourcePath, path, StringComparison.OrdinalIgnoreCase)) return;
            }

            var item = ClassifyFile(path);
            if (item != null) _items.Add(item);
        }

        private PendingItem ClassifyFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string fname = Path.GetFileNameWithoutExtension(path);
            string fnameLower = fname.ToLowerInvariant();

            var item = new PendingItem
            {
                SourcePath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = new FileInfo(path).Length,
            };

            // Image vs Audio.
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".tga")
            {
                item.IsImage = true;
                // Загружаем превью и размеры.
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(bytes);
                item.Preview = tex;
                item.ImageSize = new Vector2Int(tex.width, tex.height);

                // Категория по эвристике.
                item.Category = GuessImageCategory(fnameLower, item.ImageSize);
            }
            else if (ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aiff")
            {
                item.IsAudio = true;
                item.Category = AssetCategory.Audio;
            }
            else
            {
                // Не картинка и не аудио — пропускаем.
                return null;
            }

            return item;
        }

        private static AssetCategory GuessImageCategory(string fnameLower, Vector2Int size)
        {
            // 1. Эвристика по имени.
            if (fnameLower.StartsWith("bg_") || fnameLower.StartsWith("background")
                || fnameLower.Contains("scene")) return AssetCategory.Background;
            if (fnameLower.StartsWith("cg_") || fnameLower.Contains("event")) return AssetCategory.CG;
            if (fnameLower.StartsWith("char_") || fnameLower.StartsWith("character")
                || fnameLower.Contains("sprite")) return AssetCategory.Character;
            if (fnameLower.StartsWith("icon") || fnameLower.StartsWith("ui_")) return AssetCategory.UIIcon;

            // 2. Эвристика по размеру / соотношению сторон.
            float aspect = (float)size.x / Mathf.Max(1, size.y);
            int maxDim = Mathf.Max(size.x, size.y);

            if (maxDim <= 256 && Mathf.Abs(aspect - 1f) < 0.2f)
                return AssetCategory.UIIcon;
            if (aspect > 1.5f && maxDim >= 1024)
                return AssetCategory.Background;
            if (aspect < 0.8f && maxDim >= 800)
                return AssetCategory.Character;
            if (maxDim >= 1280)
                return AssetCategory.CG;

            return AssetCategory.Other;
        }

        private void DrawItemRow(PendingItem item, int index)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            Rect row = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
            GUILayout.Space(20);
            GUILayout.EndHorizontal();

            EditorGUI.DrawRect(row, index % 2 == 0 ? new Color(1, 1, 1, 0.03f) : Color.clear);

            // Превью / иконка.
            Rect thumb = new Rect(row.x + 4, row.y + 4, 52, 52);
            EditorGUI.DrawRect(thumb, C_BG_RAISED);
            if (item.Preview != null)
            {
                GUI.DrawTexture(thumb, item.Preview, ScaleMode.ScaleToFit, true);
            }
            else
            {
                var iconSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                iconSt.normal.textColor = C_TEXT_3;
                GUI.Label(thumb, item.IsAudio ? "🎵" : "📄", iconSt);
            }

            // Имя файла + информация.
            var nameSt = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.LowerLeft };
            nameSt.normal.textColor = C_TEXT_1;
            GUI.Label(new Rect(row.x + 60, row.y + 4, row.width - 240, 18), item.FileName, nameSt);

            string detail = item.IsImage
                ? $"{item.ImageSize.x}×{item.ImageSize.y} · {(item.FileSizeBytes / 1024.0):F0} KB"
                : $"{(item.FileSizeBytes / 1024.0):F0} KB";
            var detSt = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            detSt.normal.textColor = C_TEXT_4;
            GUI.Label(new Rect(row.x + 60, row.y + 22, row.width - 240, 14), detail, detSt);

            // Dropdown категории.
            string[] options = new[]
            {
                ToolLang.Get("Background", "Фон"),
                ToolLang.Get("CG", "CG"),
                ToolLang.Get("Character", "Персонаж"),
                ToolLang.Get("UI Icon", "UI-иконка"),
                ToolLang.Get("Audio", "Аудио"),
                ToolLang.Get("Other", "Прочее"),
            };
            int idx = (int)item.Category;
            int newIdx = EditorGUI.Popup(new Rect(row.xMax - 180, row.y + 18, 130, 22),
                idx, options);
            if (newIdx != idx) item.Category = (AssetCategory)newIdx;

            // Кнопка удалить из списка.
            if (GUI.Button(new Rect(row.xMax - 40, row.y + 18, 30, 22), "✕"))
            {
                _items.RemoveAt(index);
                Repaint();
            }
        }

        private void DoImport()
        {
            int success = 0;
            int failed = 0;

            foreach (var item in _items)
            {
                string targetFolder = ResolveTargetFolder(item.Category);
                EnsureFolder(targetFolder);
                string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                    targetFolder + "/" + item.FileName);

                try
                {
                    if (item.SourcePath.StartsWith("Assets/"))
                    {
                        // Из проекта — двигаем через AssetDatabase.MoveAsset.
                        string err = AssetDatabase.MoveAsset(item.SourcePath, targetPath);
                        if (!string.IsNullOrEmpty(err)) { failed++; Debug.LogError($"[NovellaImport] {err}"); continue; }
                    }
                    else
                    {
                        // С диска — File.Copy + AssetDatabase.ImportAsset.
                        File.Copy(item.SourcePath, targetPath, true);
                        AssetDatabase.ImportAsset(targetPath);
                    }
                    success++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogError($"[NovellaImport] {ex.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failed == 0)
            {
                NovellaToast.Success(string.Format(ToolLang.Get(
                    "Imported {0} files", "Импортировано {0} файла(ов)"), success));
            }
            else
            {
                NovellaToast.Warning(string.Format(ToolLang.Get(
                    "Imported {0}, failed {1} (see console)",
                    "Импортировано {0}, ошибок {1} (смотри консоль)"), success, failed));
            }

            _items.Clear();
            Close();
        }

        private static string ResolveTargetFolder(AssetCategory cat)
        {
            switch (cat)
            {
                case AssetCategory.Background: return GALLERY_ROOT + "/Backgrounds";
                case AssetCategory.CG:         return GALLERY_ROOT + "/CG";
                case AssetCategory.Character:  return GALLERY_ROOT + "/Characters";
                case AssetCategory.UIIcon:     return GALLERY_ROOT + "/UIIcons";
                case AssetCategory.Audio:      return GALLERY_ROOT + "/Audio";
                default:                       return GALLERY_ROOT + "/Misc";
            }
        }

        private static void EnsureFolder(string path)
        {
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
    }
}
