// ════════════════════════════════════════════════════════════════════════════
// NovellaGraphMiniMap — кастомная мини-карта графа в Hub-стиле.
//
// Заменяет стоковый UnityEditor.Experimental.GraphView.MiniMap, который имеет
// неподдающуюся стилизации Unity-2019-default-look.
//
// Что умеет:
//   • Рисует все ноды как цветные прямоугольники (цвет = accent-strip типа)
//   • Подсвечивает выделенные ноды cyan-outline'ом
//   • Показывает текущий viewport как акцентная рамка
//   • Пин-индикатор у запиненных нод (📌-чернильная точка)
//   • Click — панорамирует viewport графа на эту точку
//   • Double-click на ноду — центрирует + выделяет её
//   • Resize handle в нижнем-правом углу (опционально, скрыт)
//   • Header «MAP · N nodes» сверху
//
// Использует IMGUIContainer для самого рисования карты (низкоуровневое
// EditorGUI.DrawRect), и UI Toolkit для контейнера/header'а/handle'а.
// ════════════════════════════════════════════════════════════════════════════

using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    public class NovellaGraphMiniMap : VisualElement
    {
        private readonly NovellaGraphView _graphView;
        private readonly IMGUIContainer _canvas;
        private readonly Label _headerLbl;

        // Кэш bounds-расчёта чтобы не пересчитывать на каждый кадр.
        private Rect _cachedNodesBounds;
        private double _lastBoundsCalcTime;

        public NovellaGraphMiniMap(NovellaGraphView graphView)
        {
            _graphView = graphView;
            name = "ns-graph-minimap";

            // ─── Контейнер ───
            style.position = Position.Absolute;
            style.bottom = 16;
            style.right = 16;
            style.width = 260;
            style.height = 180;
            style.backgroundColor = new Color(
                NovellaGraphTheme.BgSide.r,
                NovellaGraphTheme.BgSide.g,
                NovellaGraphTheme.BgSide.b, 0.94f);
            style.borderTopLeftRadius = 6;
            style.borderTopRightRadius = 6;
            style.borderBottomLeftRadius = 6;
            style.borderBottomRightRadius = 6;
            Color borderC = new Color(
                NovellaGraphTheme.Accent.r,
                NovellaGraphTheme.Accent.g,
                NovellaGraphTheme.Accent.b, 0.40f);
            style.borderTopColor = borderC;
            style.borderBottomColor = borderC;
            style.borderLeftColor = borderC;
            style.borderRightColor = borderC;
            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;

            // ─── Header «MAP · N nodes» ───
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.height = 22;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            header.style.backgroundColor = NovellaGraphTheme.BgRaised;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = NovellaGraphTheme.Border;
            Add(header);

            var titleLbl = new Label("🗺  MAP");
            titleLbl.pickingMode = PickingMode.Ignore;
            titleLbl.style.fontSize = 9;
            titleLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLbl.style.color = NovellaGraphTheme.Text3;
            titleLbl.style.letterSpacing = new StyleLength(new Length(1));
            header.Add(titleLbl);

            _headerLbl = new Label("");
            _headerLbl.pickingMode = PickingMode.Ignore;
            _headerLbl.style.fontSize = 9;
            _headerLbl.style.color = NovellaGraphTheme.Text4;
            header.Add(_headerLbl);

            // ─── Canvas (IMGUI для рисования) ───
            _canvas = new IMGUIContainer(DrawMap);
            _canvas.style.flexGrow = 1;
            Add(_canvas);

            // Mouse-handlers повешены на сам контейнер чтобы IMGUIContainer
            // не съел события первым.
            _canvas.RegisterCallback<MouseDownEvent>(OnMouseDown);

            // Регулярный repaint — состояние графа (выделение, позиции) меняется
            // постоянно; обновляем минимап 30 раз в секунду.
            schedule.Execute(() => _canvas?.MarkDirtyRepaint()).Every(33);
        }

        // ─── Основной рендер ───
        private void DrawMap()
        {
            if (_graphView == null) return;
            var nodes = _graphView.nodes.ToList();
            if (nodes.Count == 0)
            {
                var emptySt = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 10,
                    normal = { textColor = NovellaGraphTheme.Text4 }
                };
                Rect canvasR = new Rect(0, 0, _canvas.contentRect.width, _canvas.contentRect.height);
                GUI.Label(canvasR, ToolLang.Get("No nodes yet", "Нод пока нет"), emptySt);
                return;
            }

            // Считаем bounding box всех нод (с кэшем 100ms).
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastBoundsCalcTime > 0.1)
            {
                _cachedNodesBounds = ComputeNodesBounds(nodes);
                _lastBoundsCalcTime = now;
            }
            Rect worldBounds = _cachedNodesBounds;

            // Padding для красоты — чтобы ноды не упирались в края minimap'а.
            const float PAD = 8f;
            Rect canvasRect = new Rect(0, 0, _canvas.contentRect.width, _canvas.contentRect.height);
            Rect drawRect = new Rect(
                canvasRect.x + PAD, canvasRect.y + PAD,
                canvasRect.width - PAD * 2, canvasRect.height - PAD * 2);

            // Масштаб: вписываем worldBounds в drawRect с сохранением aspect ratio.
            float scaleX = drawRect.width / Mathf.Max(1, worldBounds.width);
            float scaleY = drawRect.height / Mathf.Max(1, worldBounds.height);
            float scale = Mathf.Min(scaleX, scaleY);

            // Центрируем содержимое в drawRect.
            float scaledW = worldBounds.width * scale;
            float scaledH = worldBounds.height * scale;
            float offX = drawRect.x + (drawRect.width - scaledW) * 0.5f;
            float offY = drawRect.y + (drawRect.height - scaledH) * 0.5f;

            System.Func<Vector2, Vector2> worldToMap = (worldPos) => new Vector2(
                offX + (worldPos.x - worldBounds.x) * scale,
                offY + (worldPos.y - worldBounds.y) * scale);

            // ─── Рисуем ноды ───
            foreach (var n in nodes)
            {
                Rect nodeRect = n.GetPosition();
                Vector2 tl = worldToMap(nodeRect.position);
                Vector2 br = worldToMap(nodeRect.position + nodeRect.size);
                Rect mapRect = new Rect(tl, br - tl);

                // Цвет = accent-strip ноды (через type → NovellaColorSettingsWindow).
                Color nodeColor = GetNodeAccentColor(n);
                EditorGUI.DrawRect(mapRect, new Color(nodeColor.r, nodeColor.g, nodeColor.b, 0.85f));

                // Selected — cyan-outline 2px.
                if (n.selected)
                {
                    DrawRectBorder(mapRect, NovellaGraphTheme.Accent, 2);
                }

                // Pinned — маленькая точка-индикатор в правом-верхнем углу ноды.
                if (n is NovellaNodeView nnv && nnv.Data != null && nnv.Data.IsPinned)
                {
                    Rect pinDot = new Rect(mapRect.xMax - 4, mapRect.y, 4, 4);
                    EditorGUI.DrawRect(pinDot, new Color(0.96f, 0.76f, 0.43f)); // янтарный
                }
            }

            // ─── Рисуем edges (тонкие линии между нодами) ───
            // Через _graphView.edges собираем все edge'и и тянем линии от
            // input до output через worldToMap. Унитёвского способа отрисовать
            // линию в IMGUI «из коробки» нет, поэтому используем DrawRect под
            // углом — приближение через тонкий прямоугольник.
            // Для simplicity рисуем edges как прямые линии 1px цвета Border.
            foreach (var e in _graphView.edges.ToList())
            {
                if (e.input == null || e.output == null) continue;
                if (e.input.node == null || e.output.node == null) continue;
                Rect outR = e.output.node.GetPosition();
                Rect inR  = e.input.node.GetPosition();
                Vector2 a = worldToMap(new Vector2(outR.xMax, outR.center.y));
                Vector2 b = worldToMap(new Vector2(inR.x, inR.center.y));
                DrawLine(a, b, new Color(NovellaGraphTheme.Accent.r, NovellaGraphTheme.Accent.g, NovellaGraphTheme.Accent.b, 0.40f));
            }

            // ─── Рисуем viewport rect (текущая видимая область) ───
            Rect viewportWorld = GetCurrentViewportInWorld();
            Vector2 vTL = worldToMap(viewportWorld.position);
            Vector2 vBR = worldToMap(viewportWorld.position + viewportWorld.size);
            Rect viewportRect = new Rect(vTL, vBR - vTL);
            // Полупрозрачная заливка + cyan-обводка.
            EditorGUI.DrawRect(viewportRect, new Color(NovellaGraphTheme.Accent.r, NovellaGraphTheme.Accent.g, NovellaGraphTheme.Accent.b, 0.08f));
            DrawRectBorder(viewportRect, NovellaGraphTheme.Accent, 1);

            // Header-каунтер обновляем (на каждый repaint — дёшево).
            _headerLbl.text = string.Format(ToolLang.Get("{0} nodes", "{0} нод"), nodes.Count);
        }

        // ─── Mouse interactions ───
        private void OnMouseDown(MouseDownEvent evt)
        {
            if (_graphView == null) return;
            if (evt.button != 0) return;

            Vector2 localMouse = evt.localMousePosition;

            // Перевод обратно: minimap-coord → world-coord.
            // Reverse worldToMap. Используем те же значения offX/offY/scale.
            // Чтобы не пересчитывать всё — делегируем GetWorldPosFromLocal.
            Vector2 worldTarget = MapPosToWorld(localMouse);

            // Double-click на ноде → focus + select.
            if (evt.clickCount >= 2)
            {
                Node hit = HitTestNode(worldTarget);
                if (hit != null)
                {
                    _graphView.ClearSelection();
                    _graphView.AddToSelection(hit);
                    _graphView.FrameSelection();
                    evt.StopPropagation();
                    return;
                }
            }

            // Single-click → центруем viewport на этой точке.
            CenterViewportAt(worldTarget);
            evt.StopPropagation();
        }

        // Найти ноду чьи bounds содержат worldPoint.
        private Node HitTestNode(Vector2 worldPoint)
        {
            foreach (var n in _graphView.nodes.ToList())
            {
                if (n.GetPosition().Contains(worldPoint)) return n;
            }
            return null;
        }

        // Перевод minimap-локальной координаты в world-координату графа.
        private Vector2 MapPosToWorld(Vector2 localMouse)
        {
            var nodes = _graphView.nodes.ToList();
            if (nodes.Count == 0) return Vector2.zero;
            Rect worldBounds = _cachedNodesBounds;

            const float PAD = 8f;
            float canvasW = _canvas.contentRect.width;
            float canvasH = _canvas.contentRect.height;
            // Учитываем что localMouse — относительно minimap'а целиком,
            // canvas начинается ниже header'а (height 22 + 1px border).
            const float HEADER_OFFSET = 23f;
            float relX = localMouse.x;
            float relY = localMouse.y - HEADER_OFFSET;
            if (relY < 0) return new Vector2(worldBounds.center.x, worldBounds.center.y);

            float drawW = canvasW - PAD * 2;
            float drawH = canvasH - PAD * 2;
            float scaleX = drawW / Mathf.Max(1, worldBounds.width);
            float scaleY = drawH / Mathf.Max(1, worldBounds.height);
            float scale = Mathf.Min(scaleX, scaleY);

            float scaledW = worldBounds.width * scale;
            float scaledH = worldBounds.height * scale;
            float offX = PAD + (drawW - scaledW) * 0.5f;
            float offY = PAD + (drawH - scaledH) * 0.5f;

            return new Vector2(
                worldBounds.x + (relX - offX) / scale,
                worldBounds.y + (relY - offY) / scale);
        }

        // Центруем viewport графа на worldPoint. Используем стандартный
        // GraphView.viewTransform — sets scale + position для контейнера контента.
        private void CenterViewportAt(Vector2 worldPoint)
        {
            float zoom = _graphView.scale;
            float halfW = _graphView.layout.width * 0.5f / zoom;
            float halfH = _graphView.layout.height * 0.5f / zoom;
            // Position в GraphView'shном transform — это offset content'а.
            // Для центрирования: position = -(worldPoint - half) * zoom.
            Vector3 newPos = new Vector3(
                -(worldPoint.x - halfW) * zoom,
                -(worldPoint.y - halfH) * zoom,
                0);
            _graphView.UpdateViewTransform(newPos, _graphView.viewTransform.scale);
        }

        // ─── Helpers ───
        private static Rect ComputeNodesBounds(System.Collections.Generic.List<Node> nodes)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in nodes)
            {
                Rect r = n.GetPosition();
                if (r.x < minX) minX = r.x;
                if (r.y < minY) minY = r.y;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMax > maxY) maxY = r.yMax;
            }
            // Минимум 100×100 чтобы при единственной ноде не было zero-size bounds.
            float w = Mathf.Max(100, maxX - minX);
            float h = Mathf.Max(100, maxY - minY);
            return new Rect(minX, minY, w, h);
        }

        // Возвращает bounding box того, что сейчас видит юзер в _graphView.
        private Rect GetCurrentViewportInWorld()
        {
            float zoom = _graphView.scale;
            Vector3 pos = _graphView.viewTransform.position;
            float w = _graphView.layout.width / zoom;
            float h = _graphView.layout.height / zoom;
            // pos = -(viewportTL_world) * zoom  =>  viewportTL_world = -pos / zoom
            float worldX = -pos.x / zoom;
            float worldY = -pos.y / zoom;
            return new Rect(worldX, worldY, w, h);
        }

        // Достаёт цвет accent-strip для типа ноды.
        private static Color GetNodeAccentColor(Node n)
        {
            if (n is NovellaNodeView nnv && nnv.Data != null)
            {
                if (nnv.Data.NodeType == ENodeType.Dialogue ||
                    nnv.Data.NodeType == ENodeType.Note ||
                    nnv.Data.NodeType == ENodeType.CustomDLC)
                {
                    return nnv.Data.NodeCustomColor;
                }
                return NovellaColorSettingsWindow.GetNodeColor(nnv.Data.NodeType);
            }
            // Start-нода — зелёная.
            if (n is NovellaStartNodeView)
                return new Color(0.30f, 0.78f, 0.42f);
            return Color.grey;
        }

        // Рисуем линию между двумя точками как тонкий повёрнутый rect.
        // EditorGUI не умеет линии, поэтому через GUIUtility.RotateAroundPivot.
        private static void DrawLine(Vector2 a, Vector2 b, Color color)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            EditorGUI.DrawRect(new Rect(a.x, a.y - 0.5f, len, 1f), color);
            GUI.matrix = oldMatrix;
        }

        // Рамка через 4 DrawRect.
        private static void DrawRectBorder(Rect r, Color c, int thickness)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
        }
    }
}
