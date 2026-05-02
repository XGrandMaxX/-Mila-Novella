// ════════════════════════════════════════════════════════════════════════════
// NovellaUIPickerWindow
//
// Маленькое окно «как кусочек Кузницы UI» для выбора привязываемого элемента
// сцены прямо из ноды графа / Dialogue Editor / SceneSettings event и т.п.
//
// Слева — иерархия (с фильтром по типу: Text / Button / Any), справа —
// визуальное превью canvas'а: каждый элемент нарисован прямоугольником в
// правильных пропорциях. Hover в иерархии подсвечивает в превью и наоборот.
// Двойной клик / клик на «Выбрать» — назначает binding на поле и закрывает
// окно. Если на выбранном GameObject ещё нет NovellaUIBinding — добавится
// автоматически (через GetOrAdd).
//
// Цель: пользователь никогда не открывает Unity, а навигирует UI визуально.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using NovellaEngine.Runtime.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace NovellaEngine.Editor.UIBindings
{
    public class NovellaUIPickerWindow : EditorWindow
    {
        private static Action<string> _callback;
        private string _label;
        private UIBindingKind _kind;
        private string _initialId;

        private Canvas _canvas;
        private List<RectTransform> _allRects = new List<RectTransform>();
        private RectTransform _selected;
        private RectTransform _hovered;
        private Vector2 _treeScroll;

        // Палитра — динамическая, как везде в Studio.
        private static Color C_BG_PRIMARY => NovellaSettingsModule.GetInterfaceColor();
        private static Color C_BG_SIDE    => NovellaSettingsModule.GetBgSideColor();
        private static Color C_BG_RAISED  => NovellaSettingsModule.GetBgRaisedColor();
        private static Color C_BORDER     => NovellaSettingsModule.GetBorderColor();
        private static Color C_ACCENT     => NovellaSettingsModule.GetAccentColor();
        private static Color C_TEXT_1     => NovellaSettingsModule.GetTextColor();
        private static Color C_TEXT_2     => NovellaSettingsModule.GetTextSecondary();
        private static Color C_TEXT_3     => NovellaSettingsModule.GetTextMuted();
        private static Color C_TEXT_4     => NovellaSettingsModule.GetTextDisabled();

        // ─── Open API ───────────────────────────────────────────────────────────

        public static void Open(string label, UIBindingKind kind, string currentId, Action<string> onPick)
        {
            var win = GetWindow<NovellaUIPickerWindow>(true, "Novella · Pick UI", true);
            win._label = label;
            win._kind = kind;
            win._initialId = currentId;
            _callback = onPick;
            win.minSize = new Vector2(760, 520);
            win.maxSize = new Vector2(1400, 900);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(380, 260),
                new Vector2(760, 520));
            win.RefreshElements();
            win.PreselectFromInitial();
            win.ShowUtility();
        }

        private void RefreshElements()
        {
            _allRects.Clear();
            _canvas = FindCanvas();
            if (_canvas == null) return;
            _canvas.GetComponentsInChildren(true, _allRects);
            // Корневой Canvas-RectTransform сам по себе нам тоже не нужен в иерархии
            // как «выбираемый», но он будет нашим масштабом. Уберём его из списка.
            _allRects.Remove(_canvas.GetComponent<RectTransform>());
        }

        private void PreselectFromInitial()
        {
            if (string.IsNullOrEmpty(_initialId)) return;
            var b = NovellaUIBinding.FindInScene(_initialId);
            if (b != null) _selected = b.GetComponent<RectTransform>();
        }

        private static Canvas FindCanvas()
        {
            // Сначала root canvas, чтобы превью было максимально полным.
            var cs = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cs == null || cs.Length == 0) return null;
            foreach (var c in cs) if (c.isRootCanvas) return c;
            return cs[0];
        }

        // ─── Lifecycle ──────────────────────────────────────────────────────────

        private void OnEnable()
        {
            wantsMouseMove = true;
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG_PRIMARY);

            DrawHeader(new Rect(0, 0, position.width, 56));

            float footerH = 50;
            float bodyY = 56;
            float bodyH = position.height - bodyY - footerH;

            float treeW = 260;
            DrawTree(new Rect(0, bodyY, treeW, bodyH));
            DrawPreview(new Rect(treeW, bodyY, position.width - treeW, bodyH));

            DrawFooter(new Rect(0, position.height - footerH, position.width, footerH));

            if (Event.current.type == EventType.MouseMove) Repaint();
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); }
                else if (Event.current.keyCode == KeyCode.Return && _selected != null) { Confirm(); Event.current.Use(); }
            }
        }

        // ─── Header ─────────────────────────────────────────────────────────────

        private void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), C_BORDER);

            var t = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            t.normal.textColor = C_TEXT_1;
            string kindIcon = _kind == UIBindingKind.Text ? "📝" : _kind == UIBindingKind.Button ? "🔘" : "🎯";
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, 18),
                $"{kindIcon}  Выбор UI элемента для: «{_label}»", t);

            // Имя текущего выбранного элемента — крупно и читаемо.
            var sub = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            sub.normal.textColor = C_TEXT_3;
            string subText;
            if (_selected != null)
            {
                var b = _selected.GetComponent<NovellaUIBinding>();
                string display = b != null ? b.DisplayName : _selected.gameObject.name;
                subText = "Выбрано: " + display + (b == null ? "  (новый binding будет создан)" : "");
            }
            else
            {
                subText = "Выбери элемент в дереве слева или прямо на превью →";
            }
            GUI.Label(new Rect(r.x + 14, r.y + 30, r.width - 28, 18), subText, sub);
        }

        // ─── Tree ───────────────────────────────────────────────────────────────

        private void DrawTree(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), C_BORDER);

            GUILayout.BeginArea(r);
            _treeScroll = GUILayout.BeginScrollView(_treeScroll);

            if (_canvas == null)
            {
                var st = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11, padding = new RectOffset(10, 10, 14, 14) };
                st.normal.textColor = C_TEXT_3;
                GUILayout.Label("В сцене нет Canvas. Открой Кузницу UI и создай его, потом возвращайся сюда.", st);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            // Рекурсивный обход — Canvas → дети по дереву, отступ через depth.
            var rootRt = _canvas.GetComponent<RectTransform>();
            DrawTreeNode(rootRt, 0);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTreeNode(RectTransform rt, int depth)
        {
            if (rt == null) return;
            bool isCanvas = rt.GetComponent<Canvas>() != null && rt == _canvas.GetComponent<RectTransform>();

            if (!isCanvas)
            {
                bool compatible = IsCompatible(rt);
                bool selected = _selected == rt;

                Rect row = GUILayoutUtility.GetRect(GUIContent.none,
                    GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.Height(20));

                bool hovered = row.Contains(Event.current.mousePosition);

                if (selected) EditorGUI.DrawRect(row, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.30f));
                else if (hovered) EditorGUI.DrawRect(row, C_BG_RAISED);

                var st = new GUIStyle(EditorStyles.label) { fontSize = 11 };
                st.normal.textColor = compatible ? C_TEXT_1 : C_TEXT_4;

                string icon = IconFor(rt);
                var b = rt.GetComponent<NovellaUIBinding>();
                string name = b != null ? b.DisplayName : rt.gameObject.name;
                string suffix = b != null ? "" : "  ·";
                string text = new string(' ', depth * 2) + icon + "  " + name + suffix;

                GUI.Label(new Rect(row.x + 6, row.y + 2, row.width - 12, 18), text, st);

                // hover-state для превью.
                if (hovered) _hovered = rt;

                if (Event.current.type == EventType.MouseDown && hovered && compatible)
                {
                    if (Event.current.clickCount >= 2) { _selected = rt; Confirm(); return; }
                    _selected = rt;
                    Repaint();
                    Event.current.Use();
                }
            }

            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch != null) DrawTreeNode(ch, depth + 1);
            }
        }

        // ─── Preview ────────────────────────────────────────────────────────────

        private void DrawPreview(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_PRIMARY);

            if (_canvas == null)
            {
                var st = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 };
                GUI.Label(r, "(нет canvas в сцене)", st);
                return;
            }

            // Размер canvas'а в его собственных координатах (referenceResolution
            // если есть CanvasScaler, иначе rect canvas'а).
            Vector2 canvasSize = GetCanvasSize();
            if (canvasSize.x <= 0 || canvasSize.y <= 0) canvasSize = new Vector2(1920, 1080);

            // Вписываем в r с отступом.
            Rect view = FitRect(r, canvasSize, 14);
            EditorGUI.DrawRect(view, C_BG_SIDE);
            DrawBorder(view, C_BORDER);

            float scale = view.width / canvasSize.x;

            // Отрисовка по порядку дерева — родители раньше детей.
            DrawElementRectsRecursive(_canvas.GetComponent<RectTransform>(), canvasSize, view, scale);

            // Подсветка hovered + selected — поверх.
            DrawHighlight(_hovered, canvasSize, view, scale, new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.45f));
            DrawHighlight(_selected, canvasSize, view, scale, C_ACCENT);

            // Клик внутри превью — пытаемся попасть в самый «глубокий» rect.
            if (Event.current.type == EventType.MouseDown && view.Contains(Event.current.mousePosition))
            {
                var hit = HitTestDeepest(_canvas.GetComponent<RectTransform>(), canvasSize, view, scale, Event.current.mousePosition);
                if (hit != null && IsCompatible(hit))
                {
                    if (Event.current.clickCount >= 2) { _selected = hit; Confirm(); return; }
                    _selected = hit;
                }
                Repaint();
                Event.current.Use();
            }
        }

        private void DrawElementRectsRecursive(RectTransform rt, Vector2 canvasSize, Rect view, float scale)
        {
            if (rt == null) return;
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch == null) continue;
                if (!ch.gameObject.activeInHierarchy) continue;

                Rect screenRect = MapRect(ch, canvasSize, view, scale);
                Color fill = TintFor(ch);
                EditorGUI.DrawRect(screenRect, fill);
                DrawBorder(screenRect, new Color(fill.r * 1.4f, fill.g * 1.4f, fill.b * 1.4f, 0.65f));

                // Подпись если влезает.
                if (screenRect.width > 50 && screenRect.height > 14)
                {
                    var st = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, alignment = TextAnchor.UpperLeft };
                    st.normal.textColor = NovellaSettingsModule.GetContrastingText(fill);
                    var b = ch.GetComponent<NovellaUIBinding>();
                    string nm = b != null ? b.DisplayName : ch.gameObject.name;
                    GUI.Label(new Rect(screenRect.x + 3, screenRect.y + 1, screenRect.width - 6, 14), nm, st);
                }

                DrawElementRectsRecursive(ch, canvasSize, view, scale);
            }
        }

        private void DrawHighlight(RectTransform rt, Vector2 canvasSize, Rect view, float scale, Color c)
        {
            if (rt == null) return;
            Rect r = MapRect(rt, canvasSize, view, scale);
            Color outer = new Color(c.r, c.g, c.b, 1f);
            // Толстая обводка 2px.
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, r.width + 4, 2), outer);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.yMax,  r.width + 4, 2), outer);
            EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 2, 2, r.height + 4), outer);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y - 2, 2, r.height + 4), outer);
        }

        private RectTransform HitTestDeepest(RectTransform parent, Vector2 canvasSize, Rect view, float scale, Vector2 mouse)
        {
            // DFS с обратным порядком — последние нарисованные дети обычно ближе к
            // взгляду; но для UI достаточно DFS «глубже всего что попадает».
            RectTransform best = null;
            HitTestRecursive(parent, canvasSize, view, scale, mouse, ref best);
            return best;
        }

        private void HitTestRecursive(RectTransform rt, Vector2 canvasSize, Rect view, float scale, Vector2 mouse, ref RectTransform best)
        {
            for (int i = 0; i < rt.childCount; i++)
            {
                var ch = rt.GetChild(i) as RectTransform;
                if (ch == null) continue;
                if (!ch.gameObject.activeInHierarchy) continue;

                Rect r = MapRect(ch, canvasSize, view, scale);
                if (r.Contains(mouse)) best = ch;
                HitTestRecursive(ch, canvasSize, view, scale, mouse, ref best);
            }
        }

        // ─── Footer ─────────────────────────────────────────────────────────────

        private void DrawFooter(Rect r)
        {
            EditorGUI.DrawRect(r, C_BG_SIDE);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), C_BORDER);

            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            GUILayout.BeginHorizontal();
            // Слева — фильтр-подсказка о Kind.
            var hint = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            hint.normal.textColor = C_TEXT_3;
            string filterText = _kind == UIBindingKind.Text ? "Фильтр: только 📝 Тексты"
                            : _kind == UIBindingKind.Button ? "Фильтр: только 🔘 Кнопки"
                            : "Фильтр: любой UI элемент";
            GUILayout.Label(filterText, hint);

            GUILayout.FlexibleSpace();

            if (NovellaSettingsModule.NeutralButton("Отмена", GUILayout.Width(100), GUILayout.Height(28)))
            {
                Close();
            }
            GUILayout.Space(8);

            using (new EditorGUI.DisabledScope(_selected == null || !IsCompatible(_selected)))
            {
                if (NovellaSettingsModule.AccentButton("Выбрать", GUILayout.Width(120), GUILayout.Height(28)))
                {
                    Confirm();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void Confirm()
        {
            if (_selected == null) { Close(); return; }
            var b = NovellaUIBinding.GetOrAdd(_selected.gameObject);
            if (b != null) _callback?.Invoke(b.Id);
            Close();
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private bool IsCompatible(RectTransform rt)
        {
            if (rt == null) return false;
            switch (_kind)
            {
                case UIBindingKind.Text:   return rt.GetComponent<TMP_Text>() != null;
                case UIBindingKind.Button: return rt.GetComponent<Button>()   != null;
                default:                   return true;
            }
        }

        private static string IconFor(RectTransform rt)
        {
            if (rt.GetComponent<TMP_Text>() != null) return "📝";
            if (rt.GetComponent<Button>()   != null) return "🔘";
            if (rt.GetComponent<Image>()    != null) return "🖼";
            return "▣";
        }

        // Цвет заливки прямоугольника на превью — по типу элемента.
        private static Color TintFor(RectTransform rt)
        {
            if (rt.GetComponent<Button>()  != null) return new Color(0.30f, 0.55f, 0.85f, 0.55f);
            if (rt.GetComponent<TMP_Text>()!= null) return new Color(0.30f, 0.75f, 0.55f, 0.45f);
            if (rt.GetComponent<Image>()   != null) return new Color(0.65f, 0.50f, 0.30f, 0.40f);
            return new Color(0.55f, 0.55f, 0.55f, 0.30f);
        }

        // Размер canvas'а в его внутренних единицах. Если есть CanvasScaler с
        // ScaleWithScreenSize — используем его referenceResolution. Иначе —
        // размер RectTransform canvas'а.
        private Vector2 GetCanvasSize()
        {
            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                return scaler.referenceResolution;
            var rt = _canvas.GetComponent<RectTransform>();
            return rt != null ? rt.rect.size : new Vector2(1920, 1080);
        }

        // Вписывает прямоугольник заданного aspect ratio в r с отступом.
        private static Rect FitRect(Rect r, Vector2 size, float padding)
        {
            float availW = r.width - padding * 2;
            float availH = r.height - padding * 2;
            float aspect = size.x / size.y;
            float w, h;
            if (availW / availH > aspect) { h = availH; w = h * aspect; }
            else                          { w = availW; h = w / aspect; }
            return new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h);
        }

        // Преобразует RectTransform в координаты GUI (внутри view) на основании
        // его положения относительно родителя-canvas. Используем cornersBuf в
        // world-space, потом нормализуем по rect canvas'а.
        private static readonly Vector3[] _cornersBuf = new Vector3[4];
        private Rect MapRect(RectTransform rt, Vector2 canvasSize, Rect view, float scale)
        {
            // Получаем углы в world-space, преобразуем в локальные относительно canvas RT.
            rt.GetWorldCorners(_cornersBuf);
            var canvasRt = _canvas.GetComponent<RectTransform>();
            Vector2 canvasOrigin = (Vector2)canvasRt.position - canvasRt.rect.size * 0.5f * canvasRt.lossyScale;

            Vector2 lossy = canvasRt.lossyScale;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float lx = (_cornersBuf[i].x - canvasOrigin.x) / Mathf.Max(0.0001f, lossy.x);
                float ly = (_cornersBuf[i].y - canvasOrigin.y) / Mathf.Max(0.0001f, lossy.y);
                if (lx < minX) minX = lx; if (lx > maxX) maxX = lx;
                if (ly < minY) minY = ly; if (ly > maxY) maxY = ly;
            }
            // Превратим в нормализованные [0..1] относительно canvasSize.
            float nx = minX / canvasSize.x;
            float nW = (maxX - minX) / canvasSize.x;
            float nY = 1f - (maxY / canvasSize.y); // GUI y-axis вниз
            float nH = (maxY - minY) / canvasSize.y;

            return new Rect(view.x + nx * view.width,
                            view.y + nY * view.height,
                            nW * view.width,
                            nH * view.height);
        }

        private static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
