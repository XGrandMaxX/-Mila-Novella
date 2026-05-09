// ════════════════════════════════════════════════════════════════════════════
// NovellaGraphTheme — единый источник палитры и UI-helper'ов для редактора
// графа. До этого по графу было ~50 inline `Color(0.22, 0.22, 0.22)` без
// названий, разные кнопки/панели не совпадали по стилю с Hub'ом.
//
// Цвета подтягиваются из NovellaSettingsModule (центральная Hub-палитра),
// поэтому если юзер сменит акцентный цвет в настройках — граф тоже обновится.
//
// Содержит:
//   • Color tokens (BgPrimary/BgSide/BgRaised/Border/Accent/Text*) — 1:1 как
//     в NovellaVariableEditorModule, чтобы Studio выглядела единым продуктом
//   • UI Toolkit стайлеры: ApplySlimButton, ApplyAccentButton, ApplySidebarBtn
//   • Дискретные семантические цвета: Success/Danger/Warning
// ════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UIElements;

namespace NovellaEngine.Editor
{
    public static class NovellaGraphTheme
    {
        // ─── Color tokens ───────────────────────────────────────────────────
        // 1:1 с Variables / Hub-модулями.
        public static Color BgPrimary => NovellaSettingsModule.GetInterfaceColor();
        public static Color BgSide    => NovellaSettingsModule.GetBgSideColor();
        public static Color BgRaised  => NovellaSettingsModule.GetBgRaisedColor();
        public static Color Border    => NovellaSettingsModule.GetBorderColor();
        public static Color Accent    => NovellaSettingsModule.GetAccentColor();
        public static Color Text1     => NovellaSettingsModule.GetTextColor();
        public static Color Text2     => NovellaSettingsModule.GetTextSecondary();
        public static Color Text3     => NovellaSettingsModule.GetTextMuted();
        public static Color Text4     => NovellaSettingsModule.GetTextDisabled();

        // Семантические цвета — не зависят от темы пользователя.
        public static readonly Color Success = new Color(0.30f, 0.85f, 0.45f);
        public static readonly Color Danger  = new Color(0.85f, 0.32f, 0.32f);
        public static readonly Color Warning = new Color(0.96f, 0.76f, 0.43f);

        // ─── UI Toolkit стайлеры ────────────────────────────────────────────

        /// <summary>
        /// Hub-style slim button: тонкая обводка, hover-fill, без сплошной
        /// заливки в idle. Подходит для большинства тулбар-кнопок и
        /// сайдбар-навигации.
        /// </summary>
        public static void ApplySlimButton(Button btn, int height = 26, int paddingX = 12)
        {
            btn.style.height = height;
            btn.style.paddingLeft = paddingX;
            btn.style.paddingRight = paddingX;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.marginLeft = 4;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.borderTopLeftRadius = 4;
            btn.style.borderTopRightRadius = 4;
            btn.style.borderBottomLeftRadius = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.borderTopWidth = 1;
            btn.style.borderBottomWidth = 1;
            btn.style.borderLeftWidth = 1;
            btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = Border;
            btn.style.borderBottomColor = Border;
            btn.style.borderLeftColor = Border;
            btn.style.borderRightColor = Border;
            btn.style.backgroundColor = new Color(0, 0, 0, 0); // прозрачный
            btn.style.color = Text2;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.fontSize = 11;

            // Hover — лёгкий fill в сторону акцента + текст ярче.
            Color hoverBg = new Color(
                Mathf.Lerp(BgRaised.r, Accent.r, 0.10f),
                Mathf.Lerp(BgRaised.g, Accent.g, 0.10f),
                Mathf.Lerp(BgRaised.b, Accent.b, 0.10f),
                0.6f);
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = hoverBg;
                btn.style.color = Text1;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = new Color(0, 0, 0, 0);
                btn.style.color = Text2;
            });
        }

        /// <summary>
        /// Сильная accent-кнопка: сплошной акцент-fill, белый текст, для
        /// главных CTA (Save, Apply, Confirm).
        /// </summary>
        public static void ApplyAccentButton(Button btn, int height = 26)
        {
            btn.style.height = height;
            btn.style.paddingLeft = 14;
            btn.style.paddingRight = 14;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.marginLeft = 4;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.style.borderTopLeftRadius = 4;
            btn.style.borderTopRightRadius = 4;
            btn.style.borderBottomLeftRadius = 4;
            btn.style.borderBottomRightRadius = 4;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.backgroundColor = Accent;
            btn.style.color = NovellaSettingsModule.GetContrastingText(Accent);
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.fontSize = 11;
        }

        /// <summary>
        /// Stateful Save button: показывает enabled-зелёный когда есть изменения,
        /// disabled-серый когда сохранять нечего. Цвета меняются вызовом
        /// SetSaveButtonState(button, dirty).
        /// </summary>
        public static void ApplySaveButton(Button btn)
        {
            ApplyAccentButton(btn);
            btn.style.minWidth = 110;
        }

        public static void SetSaveButtonState(Button btn, bool dirty)
        {
            if (btn == null) return;
            btn.SetEnabled(dirty);
            // Зелёный когда dirty — сильный visual cue «есть что сохранить».
            // Серый-приглушённый когда чисто — кнопка явно неактивна.
            if (dirty)
            {
                btn.style.backgroundColor = Success;
                btn.style.color = new Color(0.05f, 0.18f, 0.08f);
                btn.style.opacity = 1f;
            }
            else
            {
                btn.style.backgroundColor = BgRaised;
                btn.style.color = Text4;
                btn.style.opacity = 0.7f;
            }
        }

        /// <summary>
        /// Sidebar-кнопка в стиле левой панели Hub'а: иконка слева + label,
        /// hover-fill, no-border idle.
        /// </summary>
        public static void ApplySidebarButton(Button btn)
        {
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.alignItems = Align.Center;
            btn.style.justifyContent = Justify.FlexStart;
            btn.style.height = 36;
            btn.style.marginTop = 2;
            btn.style.marginBottom = 2;
            btn.style.marginLeft = 8;
            btn.style.marginRight = 8;
            btn.style.paddingLeft = 12;
            btn.style.paddingRight = 12;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.borderTopLeftRadius = 5;
            btn.style.borderTopRightRadius = 5;
            btn.style.borderBottomLeftRadius = 5;
            btn.style.borderBottomRightRadius = 5;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.backgroundColor = new Color(0, 0, 0, 0);
            btn.style.color = Text2;
            btn.style.unityFontStyleAndWeight = FontStyle.Normal;
            btn.style.fontSize = 12;

            Color hoverBg = new Color(BgRaised.r, BgRaised.g, BgRaised.b, 0.6f);
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                btn.style.backgroundColor = hoverBg;
                btn.style.color = Text1;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                btn.style.backgroundColor = new Color(0, 0, 0, 0);
                btn.style.color = Text2;
            });
        }

        /// <summary>
        /// Маленькая icon-only кнопка 26×26 для toolbar (например toggle map,
        /// minimize). Идентична slim-стилю но квадратная и без paddingX.
        /// </summary>
        public static void ApplyIconButton(Button btn, int size = 26)
        {
            ApplySlimButton(btn, size, paddingX: 0);
            btn.style.width = size;
            btn.style.height = size;
            btn.style.minWidth = size;
        }

        /// <summary>
        /// Toggle-кнопка: slim-стиль в OFF-состоянии, accent-fill в ON.
        /// Используется вместо Unity'shных Toggle'ов которые плохо стилизуются.
        /// Возвращает callback который надо вызывать при смене состояния
        /// (например после клика — переключить bool и обновить визуал).
        /// </summary>
        public static System.Action<bool> ApplyToggleButton(Button btn, bool initial,
                                                              int height = 26, int paddingX = 12)
        {
            ApplySlimButton(btn, height, paddingX);
            // Отписываем дефолтные hover-callback'и slim'а — у toggle своя
            // логика (hover поверх ON/OFF, не должен сбрасывать заливку).
            // К сожалению UI Toolkit не даёт напрямую снять callback'и,
            // поэтому переопределяем их новыми которые учитывают _isOn.

            bool[] state = { initial };

            System.Action redraw = () =>
            {
                bool on = state[0];
                if (on)
                {
                    btn.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.22f);
                    btn.style.borderTopColor = Accent;
                    btn.style.borderBottomColor = Accent;
                    btn.style.borderLeftColor = Accent;
                    btn.style.borderRightColor = Accent;
                    btn.style.color = Text1;
                }
                else
                {
                    btn.style.backgroundColor = new Color(0, 0, 0, 0);
                    btn.style.borderTopColor = Border;
                    btn.style.borderBottomColor = Border;
                    btn.style.borderLeftColor = Border;
                    btn.style.borderRightColor = Border;
                    btn.style.color = Text2;
                }
            };

            btn.RegisterCallback<MouseEnterEvent>(_ => {
                if (state[0])
                {
                    btn.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.30f);
                }
                else
                {
                    Color hoverBg = new Color(
                        Mathf.Lerp(BgRaised.r, Accent.r, 0.10f),
                        Mathf.Lerp(BgRaised.g, Accent.g, 0.10f),
                        Mathf.Lerp(BgRaised.b, Accent.b, 0.10f),
                        0.6f);
                    btn.style.backgroundColor = hoverBg;
                    btn.style.color = Text1;
                }
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => redraw());

            redraw(); // начальная отрисовка

            // Вернуть «setter» — caller ставит новое состояние и виж обновится.
            return (newOn) => { state[0] = newOn; redraw(); };
        }

        /// <summary>
        /// Popup-кнопка: slim-стиль с шевроном-стрелкой ▾ справа от текста.
        /// Кликом открывает GenericMenu (caller сам формирует меню).
        /// Используется вместо DropdownField который не стилизуется.
        /// </summary>
        public static void ApplyPopupButton(Button btn, int height = 26, int paddingX = 10)
        {
            ApplySlimButton(btn, height, paddingX);
            // Шеврон добавим отдельным Label'ом — ApplySlimButton оставляет
            // text как есть, caller сам формирует "Label  ▾".
        }

        /// <summary>
        /// Тонкая вертикальная разделительная линия для toolbar'а — между
        /// группами действий.
        /// </summary>
        public static VisualElement CreateVerticalSeparator(int verticalMargin = 4)
        {
            var sep = new VisualElement();
            sep.style.width = 1;
            sep.style.marginLeft = 8;
            sep.style.marginRight = 4;
            sep.style.marginTop = verticalMargin;
            sep.style.marginBottom = verticalMargin;
            sep.style.backgroundColor = Border;
            return sep;
        }

        /// <summary>
        /// Тонкая горизонтальная разделительная линия для sidebar'а — между
        /// группами навигации.
        /// </summary>
        public static VisualElement CreateHorizontalSeparator()
        {
            var sep = new VisualElement();
            sep.style.height = 1;
            sep.style.marginTop = 10;
            sep.style.marginBottom = 6;
            sep.style.marginLeft = 14;
            sep.style.marginRight = 14;
            sep.style.backgroundColor = Border;
            return sep;
        }

        /// <summary>
        /// Section-header лейбл в стиле «UPPERCASE 9pt mini bold» — как в
        /// Hub-сайдбаре и Variables-модуле. Используется для группировок.
        /// </summary>
        public static Label CreateSectionHeader(string text)
        {
            var lbl = new Label(text.ToUpperInvariant());
            lbl.style.color = Text3;
            lbl.style.fontSize = 9;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = new StyleLength(new Length(1f));
            lbl.style.marginLeft = 14;
            lbl.style.marginRight = 14;
            lbl.style.marginTop = 14;
            lbl.style.marginBottom = 4;
            return lbl;
        }
    }
}
