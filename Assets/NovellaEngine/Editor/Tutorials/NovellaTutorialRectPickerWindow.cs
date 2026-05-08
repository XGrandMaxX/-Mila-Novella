using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;     // ToolLang.Get / ToolLang.IsRU

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Picker для туториал-шагов в режиме ManualRect.
    ///
    /// Архитектура: picker — это маленькое тул-окно (340×280) рядом с host'ом,
    /// которое инжектит UIElements-overlay прямо в `host.rootVisualElement`.
    /// Overlay показывает cyan-rect с угловыми handle'ами поверх host'a, но
    /// сам не блокирует клики по host'у (PickingMode.Ignore на внешнем
    /// контейнере) — кроме самого rect'а и handle'ов.
    ///
    /// Преимущество: юзер ВИДИТ host полностью и может с ним взаимодействовать
    /// (скроллить, кликать), пока подгоняет rect туториал-шага.
    ///
    /// Drag-mode:
    ///   • тащишь центр rect'а → двигаешь
    ///   • тащишь угол → ресайз
    ///   • вне rect'а → host получает клик нормально
    /// Numeric inputs в picker'е дают точное позиционирование без мыши.
    ///
    /// Сохранение: respect'ит текущую настройку ManualRectUsePercent — если
    /// false, пишет в пикселях (как у пользователя), если true — в процентах.
    /// </summary>
    public class NovellaTutorialRectPickerWindow : EditorWindow
    {
        private NovellaTutorialAsset _asset;
        private int _stepIdx;
        private EditorWindow _hostWindow;
        private Vector2 _hostSize;
        private Rect _rect;          // pixel rect в локальных координатах host'a
        private bool _saveAsPercent; // toggle — в каком формате писать в asset

        // Overlay-структура на host'е
        private VisualElement _overlayRoot;
        private VisualElement _rectVe;
        private VisualElement[] _handles = new VisualElement[4]; // NW, NE, SW, SE

        private const float HANDLE_SIZE = 16f;

        private enum DragMode { None, Move, ResizeNW, ResizeNE, ResizeSW, ResizeSE }
        private DragMode _drag = DragMode.None;
        private Vector2 _dragStart;
        private Rect _dragStartRect;

        public static void Open(NovellaTutorialAsset asset, int stepIndex)
        {
            if (asset == null || asset.Steps == null || stepIndex < 0 || stepIndex >= asset.Steps.Count)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Cannot open picker", "Не получилось открыть picker"),
                    ToolLang.Get("Invalid step index.", "Неверный индекс шага."),
                    "OK");
                return;
            }

            var host = NovellaTutorialResolver.ResolveHostWindow(asset.HostWindow);
            if (host == null)
            {
                EditorUtility.DisplayDialog(
                    ToolLang.Get("Host window not open", "Хост-окно не открыто"),
                    ToolLang.Get(
                        "Open the target window first (Hub / Graph / Welcome) so the picker can position itself relative to it.",
                        "Сначала открой целевое окно (Hub / Graph / Welcome) — picker привязывается к нему."),
                    "OK");
                return;
            }

            host.Focus();

            // Закрываем предыдущий picker если есть.
            foreach (var p in Resources.FindObjectsOfTypeAll<NovellaTutorialRectPickerWindow>())
                p.Close();

            var w = CreateInstance<NovellaTutorialRectPickerWindow>();
            w._asset = asset;
            w._stepIdx = stepIndex;
            w._hostWindow = host;
            w._hostSize = new Vector2(host.position.width, host.position.height);

            var step = asset.Steps[stepIndex];
            w._saveAsPercent = step.ManualRectUsePercent;

            // Загружаем существующий rect → пиксели (для отображения).
            if (step.ManualRectUsePercent)
            {
                w._rect = new Rect(
                    step.ManualRect.x * w._hostSize.x,
                    step.ManualRect.y * w._hostSize.y,
                    step.ManualRect.width * w._hostSize.x,
                    step.ManualRect.height * w._hostSize.y);
            }
            else
            {
                w._rect = step.ManualRect;
            }

            // Если rect вырожденный — стартуем с центрированного 40%.
            if (w._rect.width < 10f || w._rect.height < 10f)
            {
                w._rect = new Rect(
                    w._hostSize.x * 0.30f, w._hostSize.y * 0.30f,
                    w._hostSize.x * 0.40f, w._hostSize.y * 0.40f);
            }

            // Picker-окошко — маленькое, ставим РЯДОМ с host'ом справа.
            // Если справа места нет (host вплотную к краю экрана) — слева.
            float pickerW = 340f, pickerH = 290f;
            float px = host.position.xMax + 8f;
            float py = host.position.y + 40f;
            // Простая эвристика: если за hostMax не остаётся места до 2560 (ширина типового
            // монитора) — пробуем слева. Это не идеально, но работает в 95% случаев.
            if (px + pickerW > 2560f) px = host.position.x - pickerW - 8f;
            if (px < 0f) px = host.position.x + 20f; // last resort — внутри host'a сверху

            w.position = new Rect(px, py, pickerW, pickerH);
            w.titleContent = new GUIContent("🎯 Rect Picker");
            w.minSize = new Vector2(pickerW, pickerH);
            w.ShowUtility();

            w.AttachOverlay();
        }

        // ─────────────── Overlay на host'е ───────────────

        private void AttachOverlay()
        {
            if (_hostWindow == null) return;
            var root = _hostWindow.rootVisualElement;
            if (root == null) return;

            // Внешний контейнер — IGNORE для кликов (host остаётся интерактивным).
            _overlayRoot = new VisualElement { name = "novellaTutPickerOverlay" };
            _overlayRoot.style.position = Position.Absolute;
            _overlayRoot.style.left = 0; _overlayRoot.style.top = 0;
            _overlayRoot.style.right = 0; _overlayRoot.style.bottom = 0;
            _overlayRoot.pickingMode = PickingMode.Ignore;

            // Сам rect — POSITION (ловит клики на нём для drag).
            _rectVe = new VisualElement();
            _rectVe.style.position = Position.Absolute;
            ApplyRectStyle(_rectVe);
            _rectVe.pickingMode = PickingMode.Position;
            _rectVe.RegisterCallback<PointerDownEvent>(e => StartDrag(e, DragMode.Move));
            _rectVe.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _rectVe.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _rectVe.RegisterCallback<PointerCaptureOutEvent>(_ => _drag = DragMode.None);
            _overlayRoot.Add(_rectVe);

            // 4 угловых handle'а.
            _handles[0] = MakeHandle(DragMode.ResizeNW);
            _handles[1] = MakeHandle(DragMode.ResizeNE);
            _handles[2] = MakeHandle(DragMode.ResizeSW);
            _handles[3] = MakeHandle(DragMode.ResizeSE);
            foreach (var h in _handles) _overlayRoot.Add(h);

            root.Add(_overlayRoot);
            UpdateOverlay();
        }

        private void DetachOverlay()
        {
            if (_overlayRoot != null && _overlayRoot.parent != null)
                _overlayRoot.parent.Remove(_overlayRoot);
            _overlayRoot = null;
            _rectVe = null;
            _handles = new VisualElement[4];
        }

        private void ApplyRectStyle(VisualElement ve)
        {
            // Полупрозрачная cyan-заливка + 2px cyan border.
            ve.style.backgroundColor = new Color(0.36f, 0.75f, 0.92f, 0.12f);
            var c = new StyleColor(new Color(0.36f, 0.75f, 0.92f, 1f));
            ve.style.borderTopColor = c; ve.style.borderBottomColor = c;
            ve.style.borderLeftColor = c; ve.style.borderRightColor = c;
            ve.style.borderTopWidth = 2; ve.style.borderBottomWidth = 2;
            ve.style.borderLeftWidth = 2; ve.style.borderRightWidth = 2;
        }

        private VisualElement MakeHandle(DragMode mode)
        {
            var h = new VisualElement();
            h.style.position = Position.Absolute;
            h.style.width = HANDLE_SIZE;
            h.style.height = HANDLE_SIZE;
            h.style.backgroundColor = new Color(0.36f, 0.75f, 0.92f, 1f);
            var bc = new StyleColor(Color.white);
            h.style.borderTopColor = bc; h.style.borderBottomColor = bc;
            h.style.borderLeftColor = bc; h.style.borderRightColor = bc;
            h.style.borderTopWidth = 2; h.style.borderBottomWidth = 2;
            h.style.borderLeftWidth = 2; h.style.borderRightWidth = 2;
            h.pickingMode = PickingMode.Position;

            // Курсоры: для угловых hint'ов изменения размера. UIElements не имеет
            // прямого MouseCursor.Resize* — оставим default cursor.

            h.RegisterCallback<PointerDownEvent>(e => StartDrag(e, mode));
            h.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            h.RegisterCallback<PointerUpEvent>(OnPointerUp);
            h.RegisterCallback<PointerCaptureOutEvent>(_ => _drag = DragMode.None);
            return h;
        }

        private void UpdateOverlay()
        {
            if (_rectVe == null) return;
            _rectVe.style.left = _rect.x;
            _rectVe.style.top = _rect.y;
            _rectVe.style.width = _rect.width;
            _rectVe.style.height = _rect.height;

            // Handles по углам (центр handle совпадает с углом rect'а).
            var positions = new[]
            {
                new Vector2(_rect.xMin, _rect.yMin),
                new Vector2(_rect.xMax, _rect.yMin),
                new Vector2(_rect.xMin, _rect.yMax),
                new Vector2(_rect.xMax, _rect.yMax),
            };
            for (int i = 0; i < 4; i++)
            {
                if (_handles[i] == null) continue;
                _handles[i].style.left = positions[i].x - HANDLE_SIZE / 2f;
                _handles[i].style.top  = positions[i].y - HANDLE_SIZE / 2f;
            }
        }

        // ─────────────── Pointer events на overlay'е ───────────────

        private void StartDrag(PointerDownEvent e, DragMode mode)
        {
            if (e.button != 0) return;
            _drag = mode;
            // localPosition в координатах _rectVe или handle'a — нам нужны координаты host'a.
            // PointerEvent.position уже в host-локальных координатах (relative к rootVisualElement).
            _dragStart = (Vector2)e.position;
            _dragStartRect = _rect;
            (e.target as VisualElement)?.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (_drag == DragMode.None) return;
            Vector2 m = (Vector2)e.position;
            Vector2 d = m - _dragStart;
            ApplyDrag(d);
            ClampRect();
            UpdateOverlay();
            Repaint();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (_drag == DragMode.None) return;
            _drag = DragMode.None;
            (e.target as VisualElement)?.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        private void ApplyDrag(Vector2 d)
        {
            switch (_drag)
            {
                case DragMode.Move:
                    _rect.position = _dragStartRect.position + d;
                    break;
                case DragMode.ResizeNW:
                    _rect = new Rect(
                        _dragStartRect.x + d.x, _dragStartRect.y + d.y,
                        _dragStartRect.width - d.x, _dragStartRect.height - d.y);
                    break;
                case DragMode.ResizeNE:
                    _rect = new Rect(
                        _dragStartRect.x, _dragStartRect.y + d.y,
                        _dragStartRect.width + d.x, _dragStartRect.height - d.y);
                    break;
                case DragMode.ResizeSW:
                    _rect = new Rect(
                        _dragStartRect.x + d.x, _dragStartRect.y,
                        _dragStartRect.width - d.x, _dragStartRect.height + d.y);
                    break;
                case DragMode.ResizeSE:
                    _rect = new Rect(
                        _dragStartRect.x, _dragStartRect.y,
                        _dragStartRect.width + d.x, _dragStartRect.height + d.y);
                    break;
            }
            if (_rect.width < 4f) _rect.width = 4f;
            if (_rect.height < 4f) _rect.height = 4f;
        }

        private void ClampRect()
        {
            if (_hostWindow == null) return;
            float W = _hostWindow.position.width;
            float H = _hostWindow.position.height;
            _rect.width = Mathf.Max(4f, Mathf.Min(_rect.width, W));
            _rect.height = Mathf.Max(4f, Mathf.Min(_rect.height, H));
            if (_rect.x < 0f) _rect.x = 0f;
            if (_rect.y < 0f) _rect.y = 0f;
            if (_rect.xMax > W) _rect.x = W - _rect.width;
            if (_rect.yMax > H) _rect.y = H - _rect.height;
        }

        // ─────────────── Picker GUI: numeric inputs + buttons ───────────────

        private void OnGUI()
        {
            if (_hostWindow == null) { Close(); return; }

            // Если host подвинулся — обновляем _hostSize (rect не пересчитываем,
            // юзер сам решит что делать в новом размере).
            if (Mathf.Abs(_hostWindow.position.width - _hostSize.x) > 1f ||
                Mathf.Abs(_hostWindow.position.height - _hostSize.y) > 1f)
            {
                _hostSize = new Vector2(_hostWindow.position.width, _hostWindow.position.height);
                ClampRect();
                UpdateOverlay();
            }

            DrawHeader();

            EditorGUILayout.Space(8);
            DrawNumericInputs();

            EditorGUILayout.Space(8);
            DrawModeToggle();

            GUILayout.FlexibleSpace();
            DrawHints();

            EditorGUILayout.Space(6);
            DrawActions();
        }

        private void DrawHeader()
        {
            var st = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            string name = _asset != null ? _asset.name : "?";
            int total = (_asset != null && _asset.Steps != null) ? _asset.Steps.Count : 0;
            EditorGUILayout.LabelField($"🎯 {name} — Step {_stepIdx + 1} / {total}", st);

            // Тонкая подложка под заголовок.
            var r = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(r.x, r.yMax + 2, r.width, 1),
                new Color(0.36f, 0.75f, 0.92f, 0.55f));
        }

        private void DrawNumericInputs()
        {
            EditorGUILayout.LabelField(ToolLang.Get("Rect (pixels)", "Rect (пиксели)"), EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.FloatField("X", _rect.x);
            float y = EditorGUILayout.FloatField("Y", _rect.y);
            float w = EditorGUILayout.FloatField(ToolLang.Get("Width",  "Ширина"), _rect.width);
            float h = EditorGUILayout.FloatField(ToolLang.Get("Height", "Высота"), _rect.height);
            if (EditorGUI.EndChangeCheck())
            {
                _rect = new Rect(x, y, w, h);
                ClampRect();
                UpdateOverlay();
            }

            // Live readout в percent — справочно.
            if (_hostSize.x > 0 && _hostSize.y > 0)
            {
                var grey = new GUIStyle(EditorStyles.miniLabel) {
                    normal = { textColor = new Color(0.6f, 0.65f, 0.72f, 1f) }
                };
                EditorGUILayout.LabelField(string.Format("%: {0:F3}  {1:F3}  {2:F3} × {3:F3}",
                    _rect.x / _hostSize.x, _rect.y / _hostSize.y,
                    _rect.width / _hostSize.x, _rect.height / _hostSize.y), grey);
            }
        }

        private void DrawModeToggle()
        {
            EditorGUILayout.LabelField(ToolLang.Get("Save format", "Формат сохранения"), EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();

            bool prev = _saveAsPercent;
            // Две radio-like кнопки.
            GUI.backgroundColor = !_saveAsPercent ? new Color(0.36f, 0.75f, 0.92f) : Color.white;
            if (GUILayout.Toggle(!_saveAsPercent, "📐 " + ToolLang.Get("Pixels", "Пиксели"),
                    "Button", GUILayout.Height(22)))
                _saveAsPercent = false;
            GUI.backgroundColor = _saveAsPercent ? new Color(0.36f, 0.75f, 0.92f) : Color.white;
            if (GUILayout.Toggle(_saveAsPercent, "% " + ToolLang.Get("Percent", "Проценты"),
                    "Button", GUILayout.Height(22)))
                _saveAsPercent = true;
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawHints()
        {
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) {
                normal = { textColor = new Color(0.65f, 0.72f, 0.80f, 1f) }
            };
            EditorGUILayout.LabelField(ToolLang.Get(
                "🖱 Drag the cyan rect on the host to move • Drag corners to resize • Numbers above for precision.",
                "🖱 Тащи cyan-rect на host'е чтобы двигать • Углы — ресайз • Числа выше — для точности."),
                st);
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.30f, 0.85f, 0.45f);
            if (GUILayout.Button("✓ " + ToolLang.Get("Save & Close", "Сохранить и закрыть"),
                GUILayout.Height(28)))
            {
                Save();
                Close();
            }
            GUI.backgroundColor = new Color(0.55f, 0.32f, 0.32f);
            if (GUILayout.Button("✕ " + ToolLang.Get("Cancel", "Отмена"),
                GUILayout.Height(28), GUILayout.Width(110)))
            {
                Close();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void Save()
        {
            if (_asset == null) return;
            if (_stepIdx < 0 || _stepIdx >= _asset.Steps.Count) return;

            var step = _asset.Steps[_stepIdx];
            if (_saveAsPercent && _hostSize.x > 0 && _hostSize.y > 0)
            {
                step.ManualRect = new Rect(
                    _rect.x / _hostSize.x,
                    _rect.y / _hostSize.y,
                    _rect.width / _hostSize.x,
                    _rect.height / _hostSize.y);
                step.ManualRectUsePercent = true;
            }
            else
            {
                step.ManualRect = _rect;
                step.ManualRectUsePercent = false;
            }
            step.TargetMode = ETutorialTargetMode.ManualRect;

            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssetIfDirty(_asset);

            // Будим инспектор чтобы новые значения отрисовались.
            var insp = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(w => w != null && w.GetType().Name == "InspectorWindow");
            if (insp != null) insp.Repaint();
        }

        private void OnDestroy()
        {
            DetachOverlay();
        }

        private void OnDisable()
        {
            DetachOverlay();
        }
    }
}
