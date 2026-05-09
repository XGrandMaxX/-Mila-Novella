using NovellaEngine.Data;
using System;
using UnityEditor;
using UnityEngine;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Хост-окно для INovellaStudioModule в режиме «отдельного окна».
    /// Используется когда модуль (Character Editor, UI Forge и т.п.) нужно
    /// открыть НЕ переключая Novella Studio Hub — например из инспектора
    /// графа, чтобы не выкидывать пользователя из редактора графа.
    ///
    /// Окно создаёт собственный экземпляр модуля, проксирует ему OnEnable
    /// (передавая себя как hostWindow), вызывает DrawGUI(rect) в своём
    /// OnGUI() и аккуратно делает OnDisable при закрытии.
    ///
    /// Поведение по фокусу/Repaint и ScriptReload:
    /// • Если хочешь чтобы DrawGUI пересчитывался активно — используй
    ///   wantsMouseMove = true (модуль сам зовёт Repaint через _window).
    /// • При domain reload Unity пересоздаст EditorWindow, но _module
    ///   (не [SerializeField]) обнулится. В этом случае мы аккуратно
    ///   закрываем окно — пусть юзер откроет заново.
    /// </summary>
    public class NovellaStandaloneModuleWindow : EditorWindow
    {
        // Не [SerializeField] — модуль не сериализуется. После domain
        // reload это поле обнуляется, и мы закрываем окно (см. OnGUI).
        private INovellaStudioModule _module;
        private bool _initialized;

        /// <summary>Текущий хостируемый модуль (может быть null после reload).</summary>
        public INovellaStudioModule HostedModule => _module;

        // ─────────────── Public API ───────────────

        /// <summary>
        /// Открывает модуль в собственном окне.
        /// Если такое окно уже открыто — выводит его на передний план
        /// и возвращает существующий модуль (если совпадает по типу).
        /// </summary>
        /// <param name="module">Свежесозданный экземпляр модуля</param>
        /// <param name="minSize">Минимальный размер окна</param>
        /// <param name="defaultSize">Желаемый стартовый размер. null → 80% от main window.</param>
        /// <param name="titleOverride">Кастомный заголовок. null → ModuleIcon + ModuleName.</param>
        public static NovellaStandaloneModuleWindow Open(
            INovellaStudioModule module,
            Vector2 minSize,
            Vector2? defaultSize = null,
            string titleOverride = null)
        {
            if (module == null) return null;

            // Ищем уже открытое standalone-окно того же типа модуля —
            // не плодим дубли, фокусируем существующее.
            var existing = FindExisting(module.GetType());
            if (existing != null)
            {
                existing.Show();
                existing.Focus();
                return existing;
            }

            var win = CreateInstance<NovellaStandaloneModuleWindow>();
            win._module = module;
            win.minSize = minSize;
            win.titleContent = new GUIContent(BuildTitle(module, titleOverride));
            win.wantsMouseMove = true;

            ApplyPosition(win, defaultSize);

            // OnEnable модуля должен вызываться ПОСЛЕ присвоения _module,
            // потому что некоторые модули в OnEnable планируют delayCall
            // с _window.Repaint() — ему нужен валидный host.
            module.OnEnable(win);
            win._initialized = true;

            win.Show();
            win.Focus();
            return win;
        }

        /// <summary>Закрывает (если открыто) standalone-окно для модуля данного типа.</summary>
        public static void CloseIfOpen<T>() where T : INovellaStudioModule
        {
            var existing = FindExisting(typeof(T));
            if (existing != null) existing.Close();
        }

        // ─────────────── Hooks ───────────────

        private void OnDisable()
        {
            try { _module?.OnDisable(); }
            catch (Exception e) { Debug.LogException(e); }
            _module = null;
        }

        private void OnGUI()
        {
            // После domain reload модуль не сериализуется — окно остаётся,
            // но контента нет. Аккуратно закрываем, не падая.
            if (_module == null)
            {
                if (_initialized) { Close(); return; }
                EditorGUI.LabelField(new Rect(20, 20, position.width - 40, 24),
                    ToolLang.Get("Reopen this window from menu / inspector.",
                                 "Открой это окно заново из меню или инспектора."));
                return;
            }

            // Фон окна — чтобы при первом кадре не мерцал стандартный серый
            // (модули рисуют свой фон сами, но на разных версиях Unity
            // edge-cases отличаются, страховка не вредит).
            var bg = NovellaSettingsModule.GetInterfaceColor();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), bg);

            _module.DrawGUI(new Rect(0, 0, position.width, position.height));
        }

        // ─────────────── Helpers ───────────────

        private static string BuildTitle(INovellaStudioModule module, string overrideTitle)
        {
            if (!string.IsNullOrEmpty(overrideTitle)) return overrideTitle;
            string icon = module.ModuleIcon ?? "";
            string name = module.ModuleName ?? module.GetType().Name;
            return string.IsNullOrEmpty(icon) ? name : (icon + "  " + name);
        }

        private static NovellaStandaloneModuleWindow FindExisting(Type moduleType)
        {
            var all = Resources.FindObjectsOfTypeAll<NovellaStandaloneModuleWindow>();
            foreach (var w in all)
            {
                if (w == null) continue;
                if (w._module != null && w._module.GetType() == moduleType) return w;
            }
            return null;
        }

        private static void ApplyPosition(EditorWindow win, Vector2? defaultSize)
        {
            // Anchor: где сейчас «работает» юзер. Берём focused → mouseOver →
            // main window. Иначе при мульти-мониторной раскладке (Unity на
            // моник 2, граф на моник 1) standalone-окно появлялось не там,
            // где работал пользователь — приходилось его искать.
            Rect anchor = ResolveAnchorRect();

            float w = defaultSize?.x ?? Mathf.Min(anchor.width * 0.8f, 1400f);
            float h = defaultSize?.y ?? Mathf.Min(anchor.height * 0.85f, 900f);
            w = Mathf.Max(w, win.minSize.x);
            h = Mathf.Max(h, win.minSize.y);

            float x = anchor.x + (anchor.width - w) * 0.5f;
            float y = anchor.y + (anchor.height - h) * 0.5f;
            win.position = new Rect(x, y, w, h);
        }

        /// <summary>
        /// Возвращает Rect монитора/окна, на котором юзер сейчас работает.
        /// Приоритет: focusedWindow → mouseOverWindow → main Unity window →
        /// fallback (1200×800 от 120,120).
        /// </summary>
        private static Rect ResolveAnchorRect()
        {
            EditorWindow anchor = EditorWindow.focusedWindow;
            // Не якоримся к самим себе (на втором открытии Hub/standalone).
            if (anchor is NovellaStandaloneModuleWindow) anchor = null;

            if (anchor == null) anchor = EditorWindow.mouseOverWindow;
            if (anchor is NovellaStandaloneModuleWindow) anchor = null;

            if (anchor != null && anchor.position.width > 200 && anchor.position.height > 200)
                return anchor.position;

            Rect main = EditorGUIUtility.GetMainWindowPosition();
            if (main.width > 200 && main.height > 200) return main;

            return new Rect(120, 120, 1200, 800);
        }
    }
}
