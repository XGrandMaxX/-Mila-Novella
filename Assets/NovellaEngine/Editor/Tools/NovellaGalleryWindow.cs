/// <summary>
/// ОТВЕЧАЕТ ЗА:
/// 1. Отображение кастомного файлового менеджера (Галереи) поверх Unity Project.
/// 2. Фильтрацию файлов (только картинки, аудио, UI префабы и т.д.).
/// 3. Превью файлов (прослушивание аудио, просмотр текстур) перед выбором.
/// 4. Безопасное удаление в локальную "Корзину" и систему Undo (Ctrl+Z).
/// 5. Навигацию "Хлебные крошки" (интерактивный путь).
/// </summary>
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NovellaEngine.Data;
using NovellaEngine.Runtime;

namespace NovellaEngine.Editor
{
    public class NovellaGalleryWindow : EditorWindow
    {
        public enum EGalleryFilter { All, Image, Audio, Video, Prefab, CustomUI }

        private enum UndoType { Delete, Move, Copy }

        private struct UndoAction
        {
            public UndoType Type;
            public List<string> OriginalPaths;
            public List<string> TargetPaths;
            public int FileCount;
            public long SizeBytes;
            public string DisplayName;
        }

        private class CachedItem
        {
            public string Path;
            public string Name;
            public bool IsFolder;
            public bool IsEmptyFolder;
            public Texture2D Icon;
            public Sprite SpriteAsset;
            public string SubText;
        }

        private Action<UnityEngine.Object> _onSelect;
        private EGalleryFilter _filterMode = EGalleryFilter.All;

        private string _galleryDir = "Assets/NovellaEngine/Gallery";
        private string _projectDir = "Assets";
        private string _trashDir = "Assets/NovellaEngine/TrashBin";

        private bool _isProjectMode = false;
        private string RootDir => _isProjectMode ? _projectDir : _galleryDir;

        private string _currentDir;
        private Vector2 _scroll;
        private Vector2 _trashScroll;

        private Texture2D _folderIcon;
        private Texture2D _fileIcon;
        private Texture2D _scriptIcon;
        private Texture2D _audioIcon;
        private Texture2D _videoIcon;
        private Texture2D _prefabIcon;

        private List<string> _selectedPaths = new List<string>();
        private int _lastSelectedIndex = -1;

        private List<string> _clipboard = new List<string>();
        private bool _isCut = false;
        private string _searchQuery = "";

        private static Stack<UndoAction> _undoStack = new Stack<UndoAction>();
        private bool _showTrashPanel = false;

        private bool _needsRefresh = true;
        private int _cols = 1;
        private List<CachedItem> _cachedItems = new List<CachedItem>();

        private GameObject _mediaPlayerGO;
        private AudioSource _audioPreviewer;

        [MenuItem("Tools/Novella Engine/Gallery")]
        public static void OpenStandalone()
        {
            ShowWindow(null);
        }

        public static void ShowWindow(Action<UnityEngine.Object> onSelect, EGalleryFilter filter = EGalleryFilter.All, string startDir = "")
        {
            var window = GetWindow<NovellaGalleryWindow>(false, ToolLang.Get("Art Gallery", "Галерея Изображений"), true);
            window._onSelect = onSelect;
            window._filterMode = onSelect != null ? filter : EGalleryFilter.All;

            if (filter == EGalleryFilter.Prefab || filter == EGalleryFilter.CustomUI)
            {
                window._isProjectMode = true;

                if (filter == EGalleryFilter.CustomUI)
                {
                    string targetFolder = "Assets/NovellaEngine/Runtime/Prefabs/CustomUI";
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                    window._currentDir = targetFolder;
                }
            }

            window.minSize = new Vector2(900, 500);
            window.Init();

            if (!string.IsNullOrEmpty(startDir))
            {
                if (Directory.Exists(startDir)) window._currentDir = startDir;
                else window._currentDir = "Assets/NovellaEngine";
            }

            window.Show();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_currentDir)) Init();
            EditorApplication.quitting -= EmptyTrashOnQuit;
            EditorApplication.quitting += EmptyTrashOnQuit;
        }

        private static void EmptyTrashOnQuit()
        {
            var trashItems = _undoStack.Where(u => u.Type == UndoType.Delete).ToList();
            foreach (var action in trashItems)
                foreach (var tp in action.TargetPaths)
                    if (File.Exists(tp) || Directory.Exists(tp)) AssetDatabase.DeleteAsset(tp);

            _undoStack.Clear();
        }

        private void Init()
        {
            if (!AssetDatabase.IsValidFolder(_galleryDir))
            {
                Directory.CreateDirectory(_galleryDir);
                AssetDatabase.Refresh();
            }
            if (!AssetDatabase.IsValidFolder(_trashDir))
            {
                AssetDatabase.CreateFolder("Assets/NovellaEngine", "TrashBin");
            }

            if (string.IsNullOrEmpty(_currentDir)) _currentDir = RootDir;

            _folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            _fileIcon = EditorGUIUtility.IconContent("DefaultAsset Icon").image as Texture2D;
            _scriptIcon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;
            _audioIcon = EditorGUIUtility.IconContent("AudioClip Icon").image as Texture2D;
            _videoIcon = EditorGUIUtility.IconContent("VideoClip Icon").image as Texture2D;
            _prefabIcon = EditorGUIUtility.IconContent("Prefab Icon").image as Texture2D;

            InitMediaPlayers();
            RequestRefresh();
        }

        private void InitMediaPlayers()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_mediaPlayerGO == null)
            {
                _mediaPlayerGO = EditorUtility.CreateGameObjectWithHideFlags("NovellaGalleryMediaPlayer", HideFlags.HideAndDontSave);
                _audioPreviewer = _mediaPlayerGO.AddComponent<AudioSource>();
            }
            EditorApplication.update += OnEditorUpdate;
        }

        private void CleanMediaPlayers()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_audioPreviewer != null) _audioPreviewer.Stop();
            if (_mediaPlayerGO != null) DestroyImmediate(_mediaPlayerGO);
        }

        private void OnEditorUpdate()
        {
            if (_audioPreviewer != null && _audioPreviewer.isPlaying) Repaint();
        }

        private void OnDestroy()
        {
            CleanMediaPlayers();
        }

        private void OnProjectChange() { RequestRefresh(); }
        public void RequestRefresh() { _needsRefresh = true; Repaint(); }

        private bool IsProtected(string path)
        {
            string p = path.Replace("\\", "/").TrimEnd('/');
            if (p == "Assets" || p == "Assets/NovellaEngine" ||
                p == "Assets/NovellaEngine/Gallery" || p == "Assets/NovellaEngine/TrashBin") return true;
            return false;
        }

        private void RefreshDirectoryCache()
        {
            _cachedItems.Clear();

            bool isSearching = !string.IsNullOrEmpty(_searchQuery);

            if (isSearching)
            {
                string[] guids = AssetDatabase.FindAssets(_searchQuery, new[] { RootDir });
                var paths = guids.Select(g => AssetDatabase.GUIDToAssetPath(g)).Distinct().ToList();

                foreach (string path in paths)
                {
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    if (path.Replace("\\", "/").StartsWith(_trashDir)) continue;

                    ProcessFileForCache(path);
                }
            }
            else
            {
                if (!Directory.Exists(_currentDir)) return;

                string[] directories = Directory.GetDirectories(_currentDir);
                foreach (string dir in directories)
                {
                    string formattedDir = dir.Replace("\\", "/");
                    if (formattedDir == _trashDir) continue;

                    long sizeBytes = 0;
                    var allFiles = Directory.GetFiles(formattedDir, "*.*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")).ToArray();
                    foreach (var f in allFiles) sizeBytes += new FileInfo(f).Length;

                    bool isEmpty = allFiles.Length == 0;
                    float mb = sizeBytes / 1048576f;
                    string sizeStr = mb >= 1f ? $"{mb:F1} MB" : $"{sizeBytes / 1024} KB";
                    string subText = isEmpty ? ToolLang.Get("Empty", "Пустая") : $"{allFiles.Length} f. ({sizeStr})";

                    _cachedItems.Add(new CachedItem { Path = formattedDir, Name = Path.GetFileName(formattedDir), IsFolder = true, IsEmptyFolder = isEmpty, Icon = _folderIcon, SubText = subText });
                }

                string[] files = Directory.GetFiles(_currentDir, "*.*").Where(f => !f.EndsWith(".meta")).ToArray();
                foreach (string file in files)
                {
                    ProcessFileForCache(file);
                }
            }
        }

        private void ProcessFileForCache(string file)
        {
            string formattedFile = file.Replace("\\", "/");
            string ext = Path.GetExtension(formattedFile).ToLower();

            bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg";
            bool isScript = ext == ".cs";
            bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".ogg";
            bool isVideo = ext == ".mp4" || ext == ".mov" || ext == ".webm" || ext == ".avi" || ext == ".gif";
            bool isPrefab = ext == ".prefab";
            bool isCustomUI = false;

            if (isPrefab && _filterMode == EGalleryFilter.CustomUI)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(formattedFile);
                if (go != null && go.GetComponent<NovellaCustomUI>() != null) isCustomUI = true;
            }

            if (!_isProjectMode || _onSelect != null)
            {
                if (_filterMode == EGalleryFilter.Image && !isImage) return;
                if (_filterMode == EGalleryFilter.Audio && !isAudio) return;
                if (_filterMode == EGalleryFilter.Video && !isVideo) return;
                if (_filterMode == EGalleryFilter.Prefab && !isPrefab) return;
                if (_filterMode == EGalleryFilter.CustomUI && !isCustomUI) return;
                if (_filterMode == EGalleryFilter.All && !(isImage || isScript || isAudio || isVideo || isPrefab)) return;
            }

            Texture2D tex = null;
            Sprite spr = null;

            if (isImage)
            {
                spr = AssetDatabase.LoadAssetAtPath<Sprite>(formattedFile);
                if (spr != null) tex = spr.texture;
                else tex = AssetDatabase.GetCachedIcon(formattedFile) as Texture2D;
            }
            else if (isScript) tex = _scriptIcon;
            else if (isAudio) tex = _audioIcon;
            else if (isVideo) tex = _videoIcon;
            else if (isPrefab)
            {
                GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(formattedFile);
                if (prefabObj != null)
                {
                    var firstImg = prefabObj.GetComponentInChildren<UnityEngine.UI.Image>();
                    if (firstImg != null && firstImg.sprite != null) tex = firstImg.sprite.texture;
                }
                if (tex == null) tex = _prefabIcon ?? _fileIcon;
            }
            else tex = AssetDatabase.GetCachedIcon(formattedFile) as Texture2D ?? _fileIcon;

            _cachedItems.Add(new CachedItem { Path = formattedFile, Name = Path.GetFileNameWithoutExtension(formattedFile), IsFolder = false, Icon = tex, SpriteAsset = spr, SubText = "" });
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                if (_needsRefresh) { RefreshDirectoryCache(); _needsRefresh = false; }

                float availableWidth = position.width - 300 - (_showTrashPanel ? 250 : 0) - 20;
                _cols = Mathf.Max(1, Mathf.FloorToInt(availableWidth / 95f));
            }

            ProcessHotkeys();
            DrawTopBar();

            GUILayout.BeginHorizontal();
            DrawGrid();
            DrawPreviewPanel();
            if (_showTrashPanel) DrawTrashPanel();
            GUILayout.EndHorizontal();
        }

        private void ProcessHotkeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            bool isCtrl = e.control || e.command;

            if (isCtrl && e.keyCode == KeyCode.Z)
            {
                if (_undoStack.Count > 0) { EditorApplication.delayCall += () => ExecuteUndo(); }
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete && _selectedPaths.Count > 0)
            {
                var pathsToDelete = _selectedPaths.ToList();
                EditorApplication.delayCall += () => DeleteItems(pathsToDelete);
                e.Use();
            }
            else if (e.keyCode == KeyCode.F2 && _selectedPaths.Count == 1)
            {
                string pathToRename = _selectedPaths[0];
                if (!IsProtected(pathToRename)) EditorApplication.delayCall += () => RenameItem(pathToRename);
                e.Use();
            }
            else if (isCtrl && e.keyCode == KeyCode.C && _selectedPaths.Count > 0)
            {
                _clipboard = _selectedPaths.ToList(); _isCut = false;
                ShowNotification(new GUIContent(ToolLang.Get("Copied", "Скопировано")));
                e.Use();
            }
            else if (isCtrl && e.keyCode == KeyCode.X && _selectedPaths.Count > 0)
            {
                _clipboard = _selectedPaths.Where(p => !IsProtected(p)).ToList();
                if (_clipboard.Count > 0) { _isCut = true; ShowNotification(new GUIContent(ToolLang.Get("Cut", "Вырезано"))); }
                e.Use();
            }
            else if (isCtrl && e.keyCode == KeyCode.V && _clipboard.Count > 0)
            {
                EditorApplication.delayCall += () => PasteClipboard();
                e.Use();
            }
            else if (isCtrl && e.keyCode == KeyCode.D && _selectedPaths.Count > 0)
            {
                var pathsToDup = _selectedPaths.ToList();
                EditorApplication.delayCall += () => DuplicateItems(pathsToDup);
                e.Use();
            }
        }

        private void DrawTopBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.backgroundColor = _isProjectMode ? new Color(0.7f, 0.8f, 1f) : new Color(1f, 0.9f, 0.6f);
            if (GUILayout.Button(_isProjectMode ? "🗂 Unity Project" : "🖼 Novella Gallery", EditorStyles.toolbarButton, GUILayout.Width(130)))
            {
                EditorApplication.delayCall += () => {
                    _isProjectMode = !_isProjectMode; _currentDir = RootDir;
                    _selectedPaths.Clear(); StopMedia(); RequestRefresh();
                };
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginDisabledGroup(_currentDir == RootDir || !string.IsNullOrEmpty(_searchQuery));
            if (GUILayout.Button("◀ " + ToolLang.Get("Back", "Назад"), EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                string targetDir = Path.GetDirectoryName(_currentDir).Replace("\\", "/");
                EditorApplication.delayCall += () => { _currentDir = targetDir; _selectedPaths.Clear(); StopMedia(); RequestRefresh(); };
            }
            EditorGUI.EndDisabledGroup();

            Rect backBtnRect = GUILayoutUtility.GetLastRect();
            Event e = Event.current;
            if (_currentDir != RootDir && backBtnRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Move; e.Use(); }
                else if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    string parentDir = Path.GetDirectoryName(_currentDir).Replace("\\", "/");
                    var paths = DragAndDrop.paths.ToList();
                    EditorApplication.delayCall += () => { PerformMoveMultiple(paths, parentDir); };
                    e.Use();
                }
            }

            GUILayout.Space(10);
            EditorGUI.BeginChangeCheck();
            _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
            {
                RequestRefresh();
            }
            if (GUILayout.Button("✖", EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                _searchQuery = "";
                GUI.FocusControl(null);
                RequestRefresh();
            }

            if (_filterMode != EGalleryFilter.All)
            {
                GUILayout.Space(15);
                GUIStyle filterStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.4f, 0.8f, 1f) }
                };
                GUILayout.Label($"[ {ToolLang.Get("TARGET", "ЦЕЛЬ")}: {_filterMode.ToString().ToUpper()} ]", filterStyle);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("📁 " + ToolLang.Get("New Folder", "Новая папка"), EditorStyles.toolbarButton, GUILayout.Width(100)))
                EditorApplication.delayCall += () => CreateFolder();

            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("📥 " + ToolLang.Get("Import", "Импорт"), EditorStyles.toolbarButton, GUILayout.Width(70)))
                EditorApplication.delayCall += () => ImportFile();
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = _showTrashPanel ? new Color(0.8f, 0.8f, 0.8f) : Color.white;
            int trashCount = _undoStack.Count(u => u.Type == UndoType.Delete);
            if (GUILayout.Button($"🗑 ({trashCount})", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                EditorApplication.delayCall += () => { ToggleTrashPanel(); };
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            // === ФИКС ХЛЕБНЫХ КРОШЕК: ИНТЕРАКТИВНАЯ НАВИГАЦИЯ ПО ПУТИ ===
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                GUILayout.Label("🔍 " + ToolLang.Get("Global Search...", "Глобальный поиск..."), new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 0.8f, 1f) } });
            }
            else
            {
                GUILayout.Label("📍 ", new GUIStyle(EditorStyles.toolbarButton) { alignment = TextAnchor.MiddleLeft });

                string[] pathParts = _currentDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                string currentBuiltPath = "";

                for (int i = 0; i < pathParts.Length; i++)
                {
                    currentBuiltPath += (i == 0 ? "" : "/") + pathParts[i];

                    GUIStyle breadcrumbStyle = new GUIStyle(EditorStyles.toolbarButton);
                    breadcrumbStyle.fontStyle = (i == pathParts.Length - 1) ? FontStyle.Bold : FontStyle.Normal;
                    breadcrumbStyle.normal.textColor = (i == pathParts.Length - 1) ? new Color(0.6f, 0.8f, 1f) : Color.white;

                    string targetDir = currentBuiltPath;
                    if (GUILayout.Button(pathParts[i], breadcrumbStyle))
                    {
                        EditorApplication.delayCall += () => {
                            _currentDir = targetDir;
                            _selectedPaths.Clear();
                            StopMedia();
                            RequestRefresh();
                        };
                    }

                    if (i < pathParts.Length - 1)
                    {
                        GUILayout.Label("▸", new GUIStyle(EditorStyles.toolbarButton) { normal = { textColor = Color.gray } }, GUILayout.Width(15));
                    }
                }
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
        }

        private void ToggleTrashPanel()
        {
            _showTrashPanel = !_showTrashPanel;
            Rect pos = position;
            pos.width += _showTrashPanel ? 250 : -250;
            position = pos;
            Repaint();
        }

        private void DrawGrid()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            int current = 0;
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            for (int i = 0; i < _cachedItems.Count; i++)
            {
                DrawItem(_cachedItems[i], i);
                current++;
                if (current >= _cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); current = 0; }
            }

            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            Rect gridRect = GUILayoutUtility.GetLastRect();
            Event e = Event.current;

            if (gridRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.ContextClick)
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent(ToolLang.Get("New Folder", "Новая папка")), false, () => { EditorApplication.delayCall += () => CreateFolder(); });
                    menu.AddItem(new GUIContent(ToolLang.Get("Import File", "Импортировать")), false, () => { EditorApplication.delayCall += () => ImportFile(); });
                    if (_clipboard.Count > 0)
                    {
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent(ToolLang.Get("Paste (Ctrl+V)", "Вставить (Ctrl+V)")), false, () => { EditorApplication.delayCall += () => PasteClipboard(); });
                    }
                    menu.ShowAsContext();
                    e.Use();
                }

                if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        var paths = DragAndDrop.paths.ToList();
                        string targetDir = _currentDir;
                        EditorApplication.delayCall += () => {
                            foreach (string path in paths)
                            {
                                if (Path.IsPathRooted(path) && !path.StartsWith("Assets")) ImportFromOS(path, targetDir);
                            }
                            PerformMoveMultiple(paths.Where(p => p.StartsWith("Assets")).ToList(), targetDir);
                            AssetDatabase.Refresh(); RequestRefresh();
                        };
                        e.Use();
                    }
                }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    _selectedPaths.Clear(); StopMedia(); GUI.FocusControl(null); Repaint();
                }
            }
        }

        private void DrawItem(CachedItem item, int index)
        {
            Rect slotRect = GUILayoutUtility.GetRect(90, 110, GUILayout.Width(90), GUILayout.Height(110));
            Rect rect = new Rect(slotRect.x + 5, slotRect.y + 5, 80, 100);

            Event e = Event.current;
            bool isSelected = _selectedPaths.Contains(item.Path);

            if (!item.IsFolder && rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; e.Use(); }
                else if (e.type == EventType.DragPerform) e.Use();
            }

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);

                if (e.shift && _lastSelectedIndex != -1)
                {
                    int min = Mathf.Min(index, _lastSelectedIndex);
                    int max = Mathf.Max(index, _lastSelectedIndex);
                    _selectedPaths.Clear();
                    for (int i = min; i <= max; i++) _selectedPaths.Add(_cachedItems[i].Path);
                }
                else if (e.control || e.command)
                {
                    if (isSelected) _selectedPaths.Remove(item.Path);
                    else _selectedPaths.Add(item.Path);
                    _lastSelectedIndex = index;
                }
                else
                {
                    if (!isSelected) { _selectedPaths.Clear(); _selectedPaths.Add(item.Path); StopMedia(); }
                    _lastSelectedIndex = index;
                }

                if (e.clickCount == 2)
                {
                    if (item.IsFolder)
                    {
                        string targetDir = item.Path;
                        EditorApplication.delayCall += () => { _currentDir = targetDir; _searchQuery = ""; _selectedPaths.Clear(); StopMedia(); RequestRefresh(); };
                    }
                    else
                    {
                        string path = item.Path;
                        if (_onSelect != null)
                        {
                            EditorApplication.delayCall += () => {
                                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                                _onSelect?.Invoke(asset);
                                Close();
                            };
                        }
                        else
                        {
                            EditorApplication.delayCall += () => EditorUtility.OpenWithDefaultApp(path);
                        }
                    }
                }
                e.Use(); Repaint();
            }

            if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
            {
                if (!isSelected) { _selectedPaths.Clear(); _selectedPaths.Add(item.Path); _lastSelectedIndex = index; }

                GenericMenu menu = new GenericMenu();

                if (_selectedPaths.All(p => !IsProtected(p)))
                {
                    menu.AddItem(new GUIContent(ToolLang.Get("Cut (Ctrl+X)", "Вырезать (Ctrl+X)")), false, () => { _clipboard = _selectedPaths.ToList(); _isCut = true; });
                    menu.AddItem(new GUIContent(ToolLang.Get("Delete (Del)", "Удалить (Del)")), false, () => { var p = _selectedPaths.ToList(); EditorApplication.delayCall += () => DeleteItems(p); });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent(ToolLang.Get("Protected Folder", "Защищенная папка")));
                }

                menu.AddItem(new GUIContent(ToolLang.Get("Copy (Ctrl+C)", "Копировать (Ctrl+C)")), false, () => { _clipboard = _selectedPaths.ToList(); _isCut = false; });

                if (_selectedPaths.Count == 1 && !IsProtected(item.Path))
                {
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent(ToolLang.Get("Rename (F2)", "Переименовать (F2)")), false, () => { string p = item.Path; EditorApplication.delayCall += () => RenameItem(p); });
                }

                menu.ShowAsContext(); e.Use(); Repaint();
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && rect.Contains(e.mousePosition) && isSelected)
            {
                DragAndDrop.PrepareStartDrag(); DragAndDrop.paths = _selectedPaths.ToArray(); DragAndDrop.StartDrag("Move Items"); e.Use();
            }

            if (item.IsFolder && DragAndDrop.paths != null && DragAndDrop.paths.Length > 0 && rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Move; e.Use(); }
                else if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var paths = DragAndDrop.paths.ToList();
                    string targetFolder = item.Path;
                    EditorApplication.delayCall += () => { PerformMoveMultiple(paths, targetFolder); };
                    e.Use();
                }
            }

            bool isCutItem = _isCut && _clipboard.Contains(item.Path);
            if (isCutItem) GUI.color = new Color(1f, 1f, 1f, 0.4f);

            GUI.backgroundColor = isSelected ? new Color(0.3f, 0.5f, 0.8f) : Color.white;
            GUI.Box(rect, GUIContent.none, GUI.skin.box);
            GUI.backgroundColor = Color.white;

            if (item.Icon != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 5, 60, 60), item.Icon, ScaleMode.ScaleToFit);
            }

            if (item.IsFolder && DragAndDrop.paths != null && DragAndDrop.paths.Length > 0 && rect.Contains(e.mousePosition))
            {
                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f, 0.3f);
                GUI.Box(rect, GUIContent.none, GUI.skin.box);
                GUI.backgroundColor = Color.white;
            }

            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = 10 };
            GUI.Label(new Rect(rect.x, rect.y + 65, 80, 20), item.Name, nameStyle);

            if (item.IsFolder)
            {
                if (item.IsEmptyFolder)
                {
                    GUIStyle emptyStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.UpperCenter, fontSize = 10, normal = { textColor = Color.white } };
                    GUI.Label(new Rect(rect.x, rect.y + 80, 80, 20), item.SubText, emptyStyle);
                }
                else
                {
                    GUIStyle subStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9 };
                    GUI.Label(new Rect(rect.x, rect.y + 82, 80, 15), item.SubText, subStyle);
                }
            }

            GUI.color = Color.white;
        }

        private void DrawPreviewPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300), GUILayout.ExpandHeight(true));
            GUILayout.Label(ToolLang.Get("Preview", "Превью"), EditorStyles.boldLabel);
            GUILayout.Space(10);

            if (_selectedPaths.Count == 1)
            {
                string path = _selectedPaths[0];
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                string ext = Path.GetExtension(path).ToLower();

                GUILayout.Label(Path.GetFileName(path), EditorStyles.wordWrappedLabel);
                if (IsProtected(path)) GUILayout.Label($"[ {ToolLang.Get("Protected System Folder", "Защищенная папка")} ]", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } });
                GUILayout.Space(10);

                bool isVideoOrGif = ext == ".mp4" || ext == ".mov" || ext == ".webm" || ext == ".avi" || ext == ".gif";

                if (isVideoOrGif)
                {
                    Rect r = GUILayoutUtility.GetRect(280, 280);
                    GUI.Box(r, GUIContent.none, EditorStyles.helpBox);
                    GUI.Label(r, "🎬\n" + ToolLang.Get("Video / GIF Format", "Формат Видео / GIF"), new GUIStyle(EditorStyles.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter });

                    GUILayout.Space(10);
                    if (GUILayout.Button(ToolLang.Get("🎬 Open in OS Player", "🎬 Открыть в плеере ОС"), GUILayout.Height(40)))
                    {
                        EditorUtility.OpenWithDefaultApp(path);
                    }
                }
                else if (asset is Texture2D tex)
                {
                    Rect r = GUILayoutUtility.GetRect(280, 280);
                    GUI.Box(r, GUIContent.none, EditorStyles.helpBox);
                    GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                    GUILayout.Label($"{tex.width} x {tex.height} px", EditorStyles.centeredGreyMiniLabel);
                }
                else if (asset is AudioClip clip)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label("🎵 Audio Player", EditorStyles.boldLabel);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("▶ Play", GUILayout.Height(30))) { _audioPreviewer.clip = clip; _audioPreviewer.Play(); }
                    if (GUILayout.Button("⏸ Stop", GUILayout.Height(30))) { _audioPreviewer.Stop(); }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5);
                    _audioPreviewer.volume = EditorGUILayout.Slider("Volume", _audioPreviewer.volume, 0f, 1f);
                    GUILayout.EndVertical();
                }
                else if (asset != null && ext == ".prefab")
                {
                    Texture2D prefabPreview = null;
                    GameObject go = asset as GameObject;
                    if (go != null)
                    {
                        var img = go.GetComponentInChildren<UnityEngine.UI.Image>();
                        if (img != null && img.sprite != null) prefabPreview = img.sprite.texture;
                    }
                    if (prefabPreview == null) prefabPreview = AssetPreview.GetAssetPreview(asset);
                    if (prefabPreview == null) prefabPreview = AssetPreview.GetMiniThumbnail(asset);

                    if (prefabPreview != null)
                    {
                        Rect r = GUILayoutUtility.GetRect(280, 280);
                        GUI.Box(r, GUIContent.none, EditorStyles.helpBox);
                        GUI.DrawTexture(r, prefabPreview, ScaleMode.ScaleToFit);

                        if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID())) Repaint();
                    }
                }
                else if (asset is DefaultAsset && !AssetDatabase.IsValidFolder(path))
                {
                    Texture2D icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
                    if (icon) { Rect r = GUILayoutUtility.GetRect(128, 128); GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit); }
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"Path: {path}", EditorStyles.wordWrappedMiniLabel);

                if (File.Exists(path))
                {
                    long bytes = new FileInfo(path).Length;
                    float kb = bytes / 1024f;
                    float mb = kb / 1024f;
                    GUILayout.Label($"Size: {kb:F2} KB ({mb:F2} MB)", EditorStyles.miniLabel);
                }

                if (_onSelect != null && !IsProtected(path) && !AssetDatabase.IsValidFolder(path))
                {
                    GUILayout.Space(10);
                    GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
                    if (GUILayout.Button("✔ " + ToolLang.Get("Select Asset", "Выбрать Ассет"), GUILayout.Height(40)))
                    {
                        EditorApplication.delayCall += () => {
                            _onSelect?.Invoke(asset);
                            Close();
                        };
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            else if (_selectedPaths.Count > 1)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_selectedPaths.Count} items selected", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Select a file to preview", "Выберите файл для превью"), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                GUILayout.FlexibleSpace();
            }

            GUILayout.EndVertical();
        }

        private void StopMedia()
        {
            if (_audioPreviewer != null) _audioPreviewer.Stop();
        }

        private void DrawTrashPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(250), GUILayout.ExpandHeight(true));

            GUILayout.Label("🗑 " + ToolLang.Get("Trash Bin", "Корзина"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(ToolLang.Get("Items here will be PERMANENTLY deleted when you close Unity!", "Файлы будут УДАЛЕНЫ НАВСЕГДА при закрытии Unity!"), MessageType.Warning);

            _trashScroll = GUILayout.BeginScrollView(_trashScroll);

            var trashItems = _undoStack.Where(u => u.Type == UndoType.Delete).ToList();

            if (trashItems.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(ToolLang.Get("Trash is empty.", "Корзина пуста."), new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                GUILayout.FlexibleSpace();
            }
            else
            {
                foreach (var action in trashItems)
                {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label(action.DisplayName + (action.OriginalPaths.Count > 1 ? $" (+{action.OriginalPaths.Count - 1})" : ""), EditorStyles.boldLabel);

                    float mb = action.SizeBytes / 1048576f;
                    string sizeStr = mb >= 1f ? $"{mb:F1} MB" : $"{action.SizeBytes / 1024} KB";
                    string info = action.FileCount > 1 ? $"{action.FileCount} files ({sizeStr})" : (action.FileCount == 0 ? "Empty folder" : $"1 file ({sizeStr})");
                    GUILayout.Label(info, EditorStyles.miniLabel);

                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                    if (GUILayout.Button(ToolLang.Get("Restore", "Восстановить"), EditorStyles.miniButtonLeft))
                    {
                        EditorApplication.delayCall += () => RestoreDeletedAction(action);
                    }
                    GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                    if (GUILayout.Button("X", EditorStyles.miniButtonRight, GUILayout.Width(30)))
                    {
                        EditorApplication.delayCall += () => {
                            foreach (var tp in action.TargetPaths) AssetDatabase.DeleteAsset(tp);
                            RemoveFromStack(action);
                            RequestRefresh();
                        };
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }

            GUILayout.EndScrollView();

            if (trashItems.Count > 0)
            {
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button(ToolLang.Get("Empty Trash", "Очистить корзину"), GUILayout.Height(30)))
                {
                    EditorApplication.delayCall += () => EmptyTrash();
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndVertical();
        }

        private void ExecuteUndo()
        {
            var action = _undoStack.Pop();

            if (action.Type == UndoType.Delete) { RestoreDeletedAction(action, false); }
            else if (action.Type == UndoType.Move)
            {
                for (int i = 0; i < action.OriginalPaths.Count; i++) AssetDatabase.MoveAsset(action.TargetPaths[i], action.OriginalPaths[i]);
                ShowNotification(new GUIContent(ToolLang.Get("Move undone", "Перемещение отменено")));
            }
            else if (action.Type == UndoType.Copy)
            {
                foreach (var tp in action.TargetPaths) AssetDatabase.DeleteAsset(tp);
                ShowNotification(new GUIContent(ToolLang.Get("Copy undone", "Копирование отменено")));
            }
            AssetDatabase.Refresh();
            RequestRefresh();
        }

        private void RestoreDeletedAction(UndoAction action, bool removeFromStack = true)
        {
            long mb = action.SizeBytes / (1024 * 1024);
            if (action.FileCount > 100 || mb > 100)
            {
                if (!EditorUtility.DisplayDialog(ToolLang.Get("Restore Large Items", "Восстановление крупных объектов"),
                    ToolLang.Get($"Restoring {action.FileCount} files ({mb} MB) will take some time. Proceed?", $"Восстановление {action.FileCount} файлов ({mb} МБ) займет время. Продолжить?"),
                    "Yes", "Cancel")) return;
            }

            for (int i = 0; i < action.OriginalPaths.Count; i++) AssetDatabase.MoveAsset(action.TargetPaths[i], action.OriginalPaths[i]);
            if (removeFromStack) RemoveFromStack(action);
            AssetDatabase.Refresh(); RequestRefresh();
        }

        private void RemoveFromStack(UndoAction target)
        {
            var tempList = _undoStack.ToList();
            tempList.Remove(target); tempList.Reverse();
            _undoStack = new Stack<UndoAction>(tempList);
        }

        private void EmptyTrash()
        {
            var trashItems = _undoStack.Where(u => u.Type == UndoType.Delete).ToList();
            foreach (var action in trashItems) foreach (var tp in action.TargetPaths) AssetDatabase.DeleteAsset(tp);

            var otherItems = _undoStack.Where(u => u.Type != UndoType.Delete).ToList();
            otherItems.Reverse();
            _undoStack = new Stack<UndoAction>(otherItems);

            AssetDatabase.Refresh(); RequestRefresh();
        }

        private void PerformMoveMultiple(List<string> sourcePaths, string destDir)
        {
            List<string> originals = new List<string>();
            List<string> targets = new List<string>();

            foreach (string src in sourcePaths)
            {
                if (IsProtected(src)) continue;
                if (Path.GetDirectoryName(src).Replace("\\", "/") == destDir) continue;

                string destPath = GetUniquePath(destDir, src);
                string error = AssetDatabase.MoveAsset(src, destPath);
                if (string.IsNullOrEmpty(error)) { originals.Add(src); targets.Add(destPath); }
            }

            if (originals.Count > 0)
            {
                _undoStack.Push(new UndoAction { Type = UndoType.Move, OriginalPaths = originals, TargetPaths = targets, DisplayName = Path.GetFileName(originals[0]) });
                AssetDatabase.Refresh(); RequestRefresh();
            }
        }

        private void PasteClipboard()
        {
            List<string> originals = new List<string>();
            List<string> targets = new List<string>();

            foreach (string path in _clipboard)
            {
                if (Path.GetDirectoryName(path).Replace("\\", "/") == _currentDir && _isCut) continue;
                if (_isCut && IsProtected(path)) continue;

                string destPath = GetUniquePath(_currentDir, path);

                if (_isCut)
                {
                    string error = AssetDatabase.MoveAsset(path, destPath);
                    if (string.IsNullOrEmpty(error)) { originals.Add(path); targets.Add(destPath); }
                }
                else
                {
                    if (AssetDatabase.CopyAsset(path, destPath)) { originals.Add(path); targets.Add(destPath); }
                }
            }

            if (originals.Count > 0)
            {
                _undoStack.Push(new UndoAction { Type = _isCut ? UndoType.Move : UndoType.Copy, OriginalPaths = originals, TargetPaths = targets, DisplayName = Path.GetFileName(originals[0]) });
                if (_isCut) _clipboard.Clear();
                AssetDatabase.Refresh(); RequestRefresh();
            }
        }

        private void DuplicateItems(List<string> sourcePaths)
        {
            List<string> originals = new List<string>();
            List<string> targets = new List<string>();

            foreach (var src in sourcePaths)
            {
                string destPath = GetUniquePath(Path.GetDirectoryName(src).Replace("\\", "/"), src);
                if (AssetDatabase.CopyAsset(src, destPath)) { originals.Add(src); targets.Add(destPath); }
            }

            if (originals.Count > 0)
            {
                _undoStack.Push(new UndoAction { Type = UndoType.Copy, OriginalPaths = originals, TargetPaths = targets, DisplayName = Path.GetFileName(originals[0]) });
                AssetDatabase.Refresh(); RequestRefresh();
            }
        }

        private string GetUniquePath(string targetDir, string originalPath)
        {
            string name = Path.GetFileNameWithoutExtension(originalPath);
            string ext = Path.GetExtension(originalPath);
            string dest = targetDir + "/" + name + ext;
            int count = 1;
            while (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dest) != null)
            {
                dest = targetDir + "/" + name + " " + count + ext;
                count++;
            }
            return dest;
        }

        private void CreateFolder()
        {
            string dest = GetUniquePath(_currentDir, _currentDir + "/NewFolder");
            AssetDatabase.CreateFolder(_currentDir, Path.GetFileName(dest));
            _undoStack.Push(new UndoAction { Type = UndoType.Copy, OriginalPaths = new List<string>(), TargetPaths = new List<string> { dest }, DisplayName = Path.GetFileName(dest) });
            AssetDatabase.Refresh(); RequestRefresh();
        }

        private void ImportFromOS(string sourcePath, string destDir)
        {
            if (File.Exists(sourcePath))
            {
                string destFile = destDir + "/" + Path.GetFileName(sourcePath);
                string ext = Path.GetExtension(sourcePath).ToLower();

                if (File.Exists(destFile))
                {
                    if (ext == ".cs")
                    {
                        if (EditorUtility.DisplayDialog(ToolLang.Get("Warning", "Внимание"),
                            ToolLang.Get("Script already exists! Overwrite? (Duplicates cause compilation errors)", "Скрипт уже существует! Перезаписать? (Дубликаты вызовут ошибки компиляции)"),
                            ToolLang.Get("Overwrite", "Заменить"), ToolLang.Get("Cancel", "Отмена")))
                        {
                            File.Copy(sourcePath, destFile, true);
                        }
                        else return;
                    }
                    else
                    {
                        destFile = GetUniquePath(destDir, sourcePath);
                        File.Copy(sourcePath, destFile);
                    }
                }
                else
                {
                    if (ext == ".cs")
                    {
                        string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(sourcePath) + " t:MonoScript");
                        if (guids.Length > 0 && !EditorUtility.DisplayDialog(ToolLang.Get("Warning", "Внимание"),
                            ToolLang.Get("A script with this name already exists in the project! Importing it might cause duplicate class errors. Continue?", "Скрипт с таким именем уже есть в проекте! Импорт может сломать компиляцию. Продолжить?"),
                            ToolLang.Get("Yes", "Да"), ToolLang.Get("Cancel", "Отмена"))) return;
                    }
                    File.Copy(sourcePath, destFile);
                }

                AssetDatabase.ImportAsset(destFile);

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    TextureImporter importer = AssetImporter.GetAtPath(destFile) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.filterMode = FilterMode.Bilinear;
                        importer.maxTextureSize = 4096;
                        importer.SaveAndReimport();
                    }
                }
                _undoStack.Push(new UndoAction { Type = UndoType.Copy, OriginalPaths = new List<string>(), TargetPaths = new List<string> { destFile }, DisplayName = Path.GetFileName(destFile) });
            }
            else if (Directory.Exists(sourcePath))
            {
                string newDir = GetUniquePath(destDir, sourcePath);
                Directory.CreateDirectory(newDir);
                _undoStack.Push(new UndoAction { Type = UndoType.Copy, OriginalPaths = new List<string>(), TargetPaths = new List<string> { newDir }, DisplayName = Path.GetFileName(newDir) });

                string[] files = Directory.GetFiles(sourcePath);
                foreach (string f in files) ImportFromOS(f, newDir);
                string[] dirs = Directory.GetDirectories(sourcePath);
                foreach (string d in dirs) ImportFromOS(d, newDir);
            }
        }

        private void ImportFile()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(ToolLang.Get("Select File", "Выберите файл"), "", new string[] { "All Files", "*" });
            if (!string.IsNullOrEmpty(path)) { ImportFromOS(path, _currentDir); AssetDatabase.Refresh(); RequestRefresh(); }
        }

        private void RenameItem(string fullPath)
        {
            if (IsProtected(fullPath)) return;
            string currentName = Path.GetFileNameWithoutExtension(fullPath);
            RenamePopup.ShowPopup(currentName, newName => {
                AssetDatabase.RenameAsset(fullPath, newName);
                AssetDatabase.Refresh(); RequestRefresh();
            });
        }

        private void DeleteItems(List<string> fullPaths)
        {
            long totalSizeBytes = 0;
            int totalFilesCount = 0;
            List<string> targets = new List<string>();
            List<string> originals = new List<string>();
            bool anyEmptyDeleted = false;

            foreach (var fullPath in fullPaths)
            {
                if (IsProtected(fullPath)) { Debug.LogWarning($"[Novella Engine] Protected folder blocked from deletion: {fullPath}"); continue; }

                bool isFolder = AssetDatabase.IsValidFolder(fullPath);
                if (isFolder)
                {
                    var allFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")).ToArray();
                    if (allFiles.Length == 0)
                    {
                        AssetDatabase.DeleteAsset(fullPath);
                        anyEmptyDeleted = true;
                        continue;
                    }
                    totalFilesCount += allFiles.Length;
                    foreach (var f in allFiles) totalSizeBytes += new FileInfo(f).Length;
                }
                else
                {
                    totalFilesCount += 1;
                    totalSizeBytes += new FileInfo(fullPath).Length;
                }

                string trashName = Guid.NewGuid().ToString().Substring(0, 8) + "_" + Path.GetFileName(fullPath);
                string trashPath = _trashDir + "/" + trashName;

                string error = AssetDatabase.MoveAsset(fullPath, trashPath);
                if (string.IsNullOrEmpty(error)) { targets.Add(trashPath); originals.Add(fullPath); }
            }

            if (targets.Count > 0)
            {
                _undoStack.Push(new UndoAction
                {
                    Type = UndoType.Delete,
                    OriginalPaths = originals,
                    TargetPaths = targets,
                    FileCount = totalFilesCount,
                    SizeBytes = totalSizeBytes,
                    DisplayName = Path.GetFileName(originals[0])
                });
                _selectedPaths.Clear();

                if (!_showTrashPanel)
                {
                    _showTrashPanel = true;
                    Rect pos = position; pos.width += 250; position = pos;
                }
            }
            else if (anyEmptyDeleted) _selectedPaths.Clear();

            AssetDatabase.Refresh();
            RequestRefresh();
        }
    }

    public class RenamePopup : EditorWindow
    {
        private string _newName;
        private Action<string> _onRename;

        public static void ShowPopup(string currentName, Action<string> onRename)
        {
            var window = GetWindow<RenamePopup>(true, ToolLang.Get("Rename", "Переименовать"), true);
            window._newName = currentName;
            window._onRename = onRename;
            window.minSize = window.maxSize = new Vector2(250, 80);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            _newName = EditorGUILayout.TextField(_newName);
            GUILayout.Space(10);
            if (GUILayout.Button("OK", GUILayout.Height(30)))
            {
                if (!string.IsNullOrEmpty(_newName)) _onRename?.Invoke(_newName);
                Close();
            }
        }
    }
}