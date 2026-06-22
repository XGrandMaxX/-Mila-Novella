using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.EditorTools
{
    // Кастомный текстовый редактор — замена EditorGUI.TextArea, у которого
    // хрупкое selection-tracking (через GUIUtility.keyboardControl /
    // TextEditor state) и которое ломается каждый раз когда фокус уходит
    // на toolbar-кнопку. Здесь весь state — наш собственный, не зависит
    // от Unity focus / keyboardControl.
    //
    // Используется так:
    //   private NovellaTextEditor _editor = new NovellaTextEditor();
    //   ...
    //   string newText = _editor.Draw(rect, currentText, style);
    //   if (newText != currentText) { /* save */ }
    //   ...
    //   _editor.InsertAroundSelection("**", "**"); // для toolbar B
    //
    // Layout (word wrap, позиции символов) считаем через GUIStyle —
    // у него встроенные методы GetCursorPixelPosition / GetCursorStringIndex
    // которые знают про wordWrap. Это даёт нам корректное отображение
    // при минимуме своего layout-кода. Мы сами рисуем только selection
    // (несколько rect'ов) и caret.
    public class NovellaTextEditor
    {
        // ─── State ───
        public string Text   { get; private set; } = "";
        public int    Caret  { get; private set; } = 0;
        public int    Anchor { get; private set; } = 0;
        public Vector2 Scroll;

        public bool HasSelection => Caret != Anchor;
        public int  SelectionStart => Mathf.Min(Caret, Anchor);
        public int  SelectionEnd   => Mathf.Max(Caret, Anchor);
        public bool IsFocused      { get; private set; }

        // ─── Internal ───
        private double _lastInputTime;          // для blink курсора
        private bool   _mouseDown;              // активный drag-select
        private double _lastClickTime;          // для double-click word select
        private Vector2 _lastClickPos;          // double-click validity
        private const double DOUBLE_CLICK_TIME = 0.40;
        private const double CARET_BLINK_PERIOD = 1.06;

        // Цвета — берём дефолты, можно override через свойства.
        public Color CaretColor      = new Color(0.95f, 0.95f, 0.95f, 1f);
        public Color SelectionColor  = new Color(0.30f, 0.50f, 0.80f, 0.45f);

        public Action OnTextChanged;     // вызывается когда юзер изменил text
        public Action OnFocusChanged;    // вызывается при фокус/расфокус

        // ─── Public API ───

        // Принудительно установить текст и позицию курсора (например при
        // переключении реплик). Сбрасывает selection.
        public void SetText(string text, int caret = -1)
        {
            Text = text ?? "";
            int c = caret < 0 ? Text.Length : Mathf.Clamp(caret, 0, Text.Length);
            Caret = c;
            Anchor = c;
            _lastInputTime = EditorApplication.timeSinceStartup;
        }

        public void SelectAll()
        {
            Anchor = 0;
            Caret = Text.Length;
        }

        public void Deselect()
        {
            Anchor = Caret;
        }

        // Главная вставка: оборачивает выделенный фрагмент в before+after.
        // Если выделения нет — вставляет пару маркеров в позицию курсора
        // и ставит курсор МЕЖДУ ними. Без зависимости от фокуса / events —
        // работает всегда, потому что весь state наш.
        public void InsertAroundSelection(string before, string after)
        {
            int s = SelectionStart;
            int e = SelectionEnd;
            string newText;
            int newCaret;
            if (s != e)
            {
                newText = Text.Substring(0, s) + before
                        + Text.Substring(s, e - s) + after
                        + Text.Substring(e);
                newCaret = e + before.Length + after.Length;
            }
            else
            {
                int c = Mathf.Clamp(Caret, 0, Text.Length);
                newText = Text.Substring(0, c) + before + after + Text.Substring(c);
                newCaret = c + before.Length;
            }
            Text = newText;
            Caret = newCaret;
            Anchor = newCaret;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        public void InsertAtCaret(string s)
        {
            int c = Mathf.Clamp(Caret, 0, Text.Length);
            Text = Text.Substring(0, c) + s + Text.Substring(c);
            Caret = c + s.Length;
            Anchor = Caret;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        // Ставит фокус (юзер клавиши/мышь будет работать с этим редактором).
        public void Focus()
        {
            if (!IsFocused) { IsFocused = true; OnFocusChanged?.Invoke(); }
        }

        public void Unfocus()
        {
            if (IsFocused) { IsFocused = false; OnFocusChanged?.Invoke(); }
        }

        // ─── Главный Draw ───
        // rect — где рисовать поле (обычно это viewport ScrollView'а).
        // text — внешнее значение (если поменялось — синкаем).
        // style — GUIStyle для рендера (важно: wordWrap=true и шрифт).
        // Возвращает текущий Text (после возможных правок этим OnGUI).
        public string Draw(Rect rect, string text, GUIStyle style)
        {
            // Sync с внешним text — если поменялся снаружи (Undo, переключение
            // реплики). Сбрасываем выделение и ставим курсор в конец —
            // прежние Caret/Anchor могут указывать на позиции которых уже
            // нет, и MousePosToCaret потом «мажет» выделение в неожиданных
            // местах. Чистый state — самое надёжное.
            if (text != Text)
            {
                Text = text ?? "";
                Caret  = Text.Length;
                Anchor = Caret;
                _mouseDown = false; // прервать активный drag-select если был
            }

            HandleEvents(rect, style);

            DrawSelection(rect, style);
            DrawText(rect, style);
            DrawCaret(rect, style);

            return Text;
        }

        // ─── События ───
        private void HandleEvents(Rect rect, GUIStyle style)
        {
            Event ev = Event.current;
            if (ev == null) return;

            // ─── Mouse ───
            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                if (rect.Contains(ev.mousePosition))
                {
                    Focus();
                    // Забираем keyboardControl у других Unity-виджетов
                    // (например ColorField/IntField на toolbar). Иначе
                    // Space/Enter «активируют» тот виджет вместо печати
                    // в наш редактор.
                    GUIUtility.keyboardControl = 0;
                    int idx = MousePosToCaret(rect, style, ev.mousePosition);
                    double now = EditorApplication.timeSinceStartup;
                    bool isDoubleClick = (now - _lastClickTime) < DOUBLE_CLICK_TIME
                        && Vector2.Distance(_lastClickPos, ev.mousePosition) < 6f;
                    _lastClickTime = now;
                    _lastClickPos = ev.mousePosition;

                    if (isDoubleClick)
                    {
                        // Выделить слово вокруг курсора.
                        SelectWordAt(idx);
                    }
                    else if (ev.shift)
                    {
                        // Расширяем существующее выделение.
                        Caret = idx;
                    }
                    else
                    {
                        Caret = idx;
                        Anchor = idx;
                    }
                    _mouseDown = true;
                    _lastInputTime = EditorApplication.timeSinceStartup;
                    ev.Use();
                    GUI.changed = true;
                }
                // ВАЖНО: НЕ снимаем фокус при клике вне rect — иначе клик
                // на toolbar-кнопку (B/I/U) сбивает фокус и Backspace
                // потом удаляет реплику глобальным хоткеем. Фокус снимается
                // только когда сам редактор просит (через Unfocus()) или
                // когда сменилась реплика (внешний код вызывает SetText).
            }
            else if (ev.type == EventType.MouseDrag && _mouseDown && ev.button == 0)
            {
                int idx = MousePosToCaret(rect, style, ev.mousePosition);
                Caret = idx;
                _lastInputTime = EditorApplication.timeSinceStartup;
                ev.Use();
                GUI.changed = true;
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                if (_mouseDown) { _mouseDown = false; ev.Use(); }
            }

            // ─── Клавиатура (только если в фокусе) ───
            if (!IsFocused) return;
            if (ev.type != EventType.KeyDown) return;

            bool ctrl  = ev.control || ev.command;
            bool shift = ev.shift;

            if (ctrl)
            {
                // Ctrl+A — select all
                if (ev.keyCode == KeyCode.A) { SelectAll(); ev.Use(); GUI.changed = true; return; }
                // Ctrl+C — copy
                if (ev.keyCode == KeyCode.C) { CopySelection(); ev.Use(); return; }
                // Ctrl+X — cut
                if (ev.keyCode == KeyCode.X) { CopySelection(); DeleteSelection(); ev.Use(); GUI.changed = true; return; }
                // Ctrl+V — paste
                if (ev.keyCode == KeyCode.V) { PasteFromClipboard(); ev.Use(); GUI.changed = true; return; }
                // Ctrl+Backspace — delete word back
                if (ev.keyCode == KeyCode.Backspace)
                { DeleteWordBack(); ev.Use(); GUI.changed = true; return; }
                // Ctrl+Delete — delete word forward
                if (ev.keyCode == KeyCode.Delete)
                { DeleteWordForward(); ev.Use(); GUI.changed = true; return; }
                // Ctrl+Left/Right — jump word
                if (ev.keyCode == KeyCode.LeftArrow)
                { JumpWord(-1, shift); ev.Use(); GUI.changed = true; return; }
                if (ev.keyCode == KeyCode.RightArrow)
                { JumpWord(1, shift);  ev.Use(); GUI.changed = true; return; }
            }

            // Стрелки (без ctrl).
            switch (ev.keyCode)
            {
                case KeyCode.LeftArrow:
                    MoveCaret(Mathf.Max(0, Caret - 1), shift);
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.RightArrow:
                    MoveCaret(Mathf.Min(Text.Length, Caret + 1), shift);
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.UpArrow:
                {
                    int idx = MoveVertical(rect, style, -1);
                    MoveCaret(idx, shift);
                    ev.Use(); GUI.changed = true; return;
                }
                case KeyCode.DownArrow:
                {
                    int idx = MoveVertical(rect, style, 1);
                    MoveCaret(idx, shift);
                    ev.Use(); GUI.changed = true; return;
                }
                case KeyCode.Home:
                    MoveCaret(LineStart(Caret), shift);
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.End:
                    MoveCaret(LineEnd(Caret), shift);
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.Backspace:
                    if (HasSelection) DeleteSelection();
                    else if (Caret > 0)
                    {
                        Text = Text.Substring(0, Caret - 1) + Text.Substring(Caret);
                        Caret--;
                        Anchor = Caret;
                        OnTextChanged?.Invoke();
                    }
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.Delete:
                    if (HasSelection) DeleteSelection();
                    else if (Caret < Text.Length)
                    {
                        Text = Text.Substring(0, Caret) + Text.Substring(Caret + 1);
                        OnTextChanged?.Invoke();
                    }
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    InsertChar('\n');
                    ev.Use(); GUI.changed = true; return;
                case KeyCode.Tab:
                    InsertChar('\t');
                    ev.Use(); GUI.changed = true; return;
            }

            // Печать обычного символа. Берём ev.character — там уже учтён
            // регистр/локаль/композиция.
            char ch = ev.character;
            if (ch != '\0' && ch >= 32 && !ctrl)
            {
                InsertChar(ch);
                ev.Use(); GUI.changed = true;
            }
        }

        // ─── Вспомогательные операции ───

        private void MoveCaret(int newCaret, bool shift)
        {
            Caret = Mathf.Clamp(newCaret, 0, Text.Length);
            if (!shift) Anchor = Caret;
            _lastInputTime = EditorApplication.timeSinceStartup;
        }

        private void InsertChar(char c)
        {
            if (HasSelection) DeleteSelection();
            int p = Mathf.Clamp(Caret, 0, Text.Length);
            Text = Text.Substring(0, p) + c + Text.Substring(p);
            Caret = p + 1;
            Anchor = Caret;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        private void DeleteSelection()
        {
            int s = SelectionStart;
            int e = SelectionEnd;
            if (s == e) return;
            Text = Text.Substring(0, s) + Text.Substring(e);
            Caret = s;
            Anchor = s;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        private void CopySelection()
        {
            if (!HasSelection) return;
            EditorGUIUtility.systemCopyBuffer = Text.Substring(SelectionStart, SelectionEnd - SelectionStart);
        }

        private void PasteFromClipboard()
        {
            string s = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(s)) return;
            if (HasSelection) DeleteSelection();
            int p = Mathf.Clamp(Caret, 0, Text.Length);
            Text = Text.Substring(0, p) + s + Text.Substring(p);
            Caret = p + s.Length;
            Anchor = Caret;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        private void DeleteWordBack()
        {
            if (HasSelection) { DeleteSelection(); return; }
            int target = PrevWordBoundary(Caret);
            if (target == Caret) return;
            Text = Text.Substring(0, target) + Text.Substring(Caret);
            Caret = target; Anchor = target;
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        private void DeleteWordForward()
        {
            if (HasSelection) { DeleteSelection(); return; }
            int target = NextWordBoundary(Caret);
            if (target == Caret) return;
            Text = Text.Substring(0, Caret) + Text.Substring(target);
            _lastInputTime = EditorApplication.timeSinceStartup;
            OnTextChanged?.Invoke();
        }

        private void JumpWord(int dir, bool shift)
        {
            int target = (dir < 0) ? PrevWordBoundary(Caret) : NextWordBoundary(Caret);
            MoveCaret(target, shift);
        }

        private int PrevWordBoundary(int from)
        {
            int i = from - 1;
            // Скип whitespaces назад.
            while (i > 0 && char.IsWhiteSpace(Text[i])) i--;
            // Скип word chars назад.
            while (i > 0 && !char.IsWhiteSpace(Text[i - 1])) i--;
            return Mathf.Max(0, i);
        }

        private int NextWordBoundary(int from)
        {
            int i = from;
            // Скип word chars вперёд.
            while (i < Text.Length && !char.IsWhiteSpace(Text[i])) i++;
            // Скип whitespaces вперёд.
            while (i < Text.Length && char.IsWhiteSpace(Text[i])) i++;
            return Mathf.Min(Text.Length, i);
        }

        private void SelectWordAt(int idx)
        {
            if (Text.Length == 0) { Anchor = 0; Caret = 0; return; }
            int s = Mathf.Clamp(idx, 0, Text.Length - 1);
            int e = s;
            // Расширяем влево пока word char.
            while (s > 0 && !char.IsWhiteSpace(Text[s - 1])) s--;
            // Расширяем вправо.
            while (e < Text.Length && !char.IsWhiteSpace(Text[e])) e++;
            Anchor = s;
            Caret = e;
        }

        private int LineStart(int from)
        {
            int i = from - 1;
            while (i >= 0 && Text[i] != '\n') i--;
            return i + 1;
        }

        private int LineEnd(int from)
        {
            int i = from;
            while (i < Text.Length && Text[i] != '\n') i++;
            return i;
        }

        private int MoveVertical(Rect rect, GUIStyle style, int dir)
        {
            // Используем GUIStyle.GetCursorPixelPosition чтобы найти Y текущего
            // курсора, сместить на одну строку вверх/вниз и через
            // GetCursorStringIndex получить новый indx.
            var content = new GUIContent(Text);
            Vector2 cur = style.GetCursorPixelPosition(rect, content, Caret);
            float lineH = style.lineHeight > 0 ? style.lineHeight : style.fontSize + 4;
            cur.y += dir * lineH;
            // Защитим от выхода за rect.
            cur.y = Mathf.Clamp(cur.y, rect.yMin + 1, rect.yMax - 1);
            return style.GetCursorStringIndex(rect, content, cur);
        }

        private int MousePosToCaret(Rect rect, GUIStyle style, Vector2 mouse)
        {
            // GetCursorStringIndex знает word-wrap и многострочный layout.
            var content = new GUIContent(Text);
            // mouse уже в координатах окна, rect тоже — передаём как есть.
            return Mathf.Clamp(
                style.GetCursorStringIndex(rect, content, mouse),
                0, Text.Length);
        }

        // ─── Отрисовка ───

        private void DrawText(Rect rect, GUIStyle style)
        {
            // Обычный label с word-wrap. В IMGUI рендер дешёв — каждый
            // фрейм пересчитывается layout.
            GUI.Label(rect, Text, style);
        }

        private void DrawSelection(Rect rect, GUIStyle style)
        {
            if (!HasSelection) return;
            var content = new GUIContent(Text);
            int s = SelectionStart;
            int e = SelectionEnd;

            // Многострочное выделение: идём по символам и группируем
            // последовательные на одной Y в один Rect. Это даст
            // несколько прямоугольников для multi-line selection.
            float lineH = style.lineHeight > 0 ? style.lineHeight : style.fontSize + 4;
            Vector2 startPx = style.GetCursorPixelPosition(rect, content, s);
            Vector2 endPx   = style.GetCursorPixelPosition(rect, content, e);

            if (Mathf.Approximately(startPx.y, endPx.y))
            {
                // Single-line selection.
                Rect r = new Rect(startPx.x, startPx.y, endPx.x - startPx.x, lineH);
                EditorGUI.DrawRect(r, SelectionColor);
                return;
            }

            // Multi-line: первая строка — от startX до правого края rect.
            EditorGUI.DrawRect(new Rect(startPx.x, startPx.y,
                rect.xMax - startPx.x, lineH), SelectionColor);
            // Полные средние строки.
            float y = startPx.y + lineH;
            while (y + lineH * 0.5f < endPx.y)
            {
                EditorGUI.DrawRect(new Rect(rect.xMin, y, rect.width, lineH), SelectionColor);
                y += lineH;
            }
            // Последняя строка — от левого края до endX.
            EditorGUI.DrawRect(new Rect(rect.xMin, endPx.y,
                endPx.x - rect.xMin, lineH), SelectionColor);
        }

        private void DrawCaret(Rect rect, GUIStyle style)
        {
            if (!IsFocused) return;
            // Blink: курсор виден половину периода.
            double t = EditorApplication.timeSinceStartup - _lastInputTime;
            bool visible = (t < 0.4) || (((int)((t) / (CARET_BLINK_PERIOD * 0.5)) % 2) == 0);
            if (!visible) return;

            var content = new GUIContent(Text);
            Vector2 px = style.GetCursorPixelPosition(rect, content, Caret);
            float lineH = style.lineHeight > 0 ? style.lineHeight : style.fontSize + 4;
            EditorGUI.DrawRect(new Rect(px.x, px.y, 1.5f, lineH), CaretColor);
        }
    }
}
