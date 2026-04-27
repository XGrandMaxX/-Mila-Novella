using System;
using UnityEngine;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Один шаг туториала. Полностью data-driven — все параметры в инспекторе.
    /// </summary>
    [Serializable]
    public class NovellaTutorialStep
    {
        // ──────────────────────────── ТЕКСТ ────────────────────────────

        [Tooltip("Заголовок шага (необязательно). Если пусто — рисуется только тело сообщения.")]
        public string TitleEN = "";
        public string TitleRU = "";

        [TextArea(3, 10), Tooltip("Основной текст шага (EN). Поддерживается rich-text: <b>, <i>, <color=...>.")]
        public string BodyEN = "Step description...";

        [TextArea(3, 10), Tooltip("Основной текст шага (RU).")]
        public string BodyRU = "Описание шага...";

        // ──────────────────────────── ЦЕЛЬ ─────────────────────────────

        [Tooltip("Как находить элемент UI, на который указывает подсказка. " +
                 "ByVisualElementName — самое надёжное (UI Toolkit-окна типа графа). " +
                 "ByControlName — для IMGUI-окон через GUI.SetNextControlName(). " +
                 "ByReflectionField — рефлексия по приватному полю окна (escape-hatch). " +
                 "ManualRect — полностью ручные координаты (как в старой системе). " +
                 "WholeWindow — выделить всё окно. None — без подсветки, просто текст по центру.")]
        public ETutorialTargetMode TargetMode = ETutorialTargetMode.WholeWindow;

        [Tooltip("Имя VisualElement или GUI ControlName (зависит от TargetMode).")]
        public string TargetName = "";

        [Tooltip("Имя приватного поля типа VisualElement в hostWindow. Используется при TargetMode = ByReflectionField.")]
        public string ReflectionFieldName = "";

        [Tooltip("Ручной прямоугольник в координатах окна (используется при TargetMode = ManualRect).")]
        public Rect ManualRect = new Rect(0, 0, 200, 100);

        [Tooltip("Использовать процентные координаты (0..1) вместо пикселей. Делает шаги адаптивными к размеру окна.")]
        public bool ManualRectUsePercent = false;

        [Tooltip("Дополнительный отступ вокруг найденной цели в пикселях. Положительный — шире, отрицательный — уже.")]
        public float TargetPadding = 6f;

        // ──────────────────────── ТИП ПОДСКАЗКИ ────────────────────────

        [Tooltip("Стиль визуальной подсказки.")]
        public ETutorialHintStyle HintStyle = ETutorialHintStyle.Spotlight;

        [Tooltip("Цвет акцента подсказки (обводка/палец/стрелка).")]
        public Color AccentColor = new Color(0.36f, 0.75f, 0.92f, 1f); // novella-cyan

        [Tooltip("Где разместить текстовую панель относительно цели.")]
        public ETutorialPanelAnchor PanelAnchor = ETutorialPanelAnchor.Auto;

        [Tooltip("Опциональный VideoClip для проигрывания внутри панели (показывается над текстом).")]
        public UnityEngine.Video.VideoClip Video;

        [Tooltip("Опциональная статичная картинка / GIF-таймлайн (Texture2D с кадрами по горизонтали). Используется если Video не задан.")]
        public Texture2D Image;

        [Tooltip("FPS для проигрывания спрайт-листа (Image). Игнорируется если Image — обычная картинка.")]
        public int ImageFPS = 12;

        [Tooltip("Сколько кадров в спрайт-листе по горизонтали. 1 = просто статичная картинка.")]
        public int ImageFrameCount = 1;

        // ──────────────────── ПРОДВИЖЕНИЕ ────────────────────

        [Tooltip("Когда шаг считается выполненным и автоматически переходит на следующий. " +
                 "OnNextButton — только по клику Next. " +
                 "OnUserAction — по действию юзера (тип задаётся в ActionTrigger). " +
                 "AutoTimer — через AutoAdvanceSeconds сек. " +
                 "Any — любое из перечисленных, что наступит первым.")]
        public ETutorialAdvanceMode AdvanceMode = ETutorialAdvanceMode.OnNextButton;

        [Tooltip("Тип действия юзера для автопродвижения (используется при AdvanceMode = OnUserAction или Any).")]
        public ETutorialActionTrigger ActionTrigger = ETutorialActionTrigger.ClickTarget;

        [Tooltip("Опциональный regex-валидатор для TextInput-триггера. Пусто = любой ввод считается достаточным.")]
        public string ActionTextRegex = "";

        [Tooltip("Через сколько секунд автоматически перейти на следующий шаг (AdvanceMode = AutoTimer или Any). 0 = выкл.")]
        public float AutoAdvanceSeconds = 0f;

        [Tooltip("Минимальное время удержания шага перед тем как кнопка Next разблокируется. Защита от случайного прокликивания.")]
        public float MinHoldSeconds = 1f;

        [Tooltip("Можно ли пропустить весь туториал кнопкой Skip с этого шага.")]
        public bool AllowSkip = true;

        [Tooltip("Скрыть кнопку Next (полезно для шагов, где надо ждать действия юзера).")]
        public bool HideNextButton = false;
    }

    public enum ETutorialTargetMode
    {
        None = 0,
        WholeWindow = 1,
        ByVisualElementName = 2,
        ByControlName = 3,
        ByReflectionField = 4,
        ManualRect = 5,
    }

    public enum ETutorialHintStyle
    {
        /// <summary>Затемнение всего окна, кроме цели — классический "прожектор" с обводкой.</summary>
        Spotlight = 0,

        /// <summary>Без затемнения, только пульсирующая обводка вокруг цели.</summary>
        Outline = 1,

        /// <summary>Большой палец 👆 рядом с целью + текстовая панель.</summary>
        PointingFinger = 2,

        /// <summary>Стрелка от текстовой панели к цели.</summary>
        Arrow = 3,

        /// <summary>Всплывающий tooltip-баблик, привязанный к цели.</summary>
        Tooltip = 4,
    }

    public enum ETutorialPanelAnchor
    {
        Auto = 0,
        Top = 1,
        Bottom = 2,
        Left = 3,
        Right = 4,
        Center = 5,
    }

    public enum ETutorialAdvanceMode
    {
        OnNextButton = 0,
        OnUserAction = 1,
        AutoTimer = 2,
        Any = 3,
    }

    public enum ETutorialActionTrigger
    {
        /// <summary>Клик мыши по target-элементу.</summary>
        ClickTarget = 0,

        /// <summary>Любой клик в окне.</summary>
        ClickAnywhere = 1,

        /// <summary>Ввод текста в target-поле (с опциональным regex-валидатором).</summary>
        TextInput = 2,

        /// <summary>Любое нажатие клавиши.</summary>
        AnyKey = 3,
    }
}
