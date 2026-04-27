using UnityEditor;
using UnityEngine;
using NovellaEngine.Data;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Standalone-окно для редактора глобальных переменных.
    /// Открывается из графа без переключения активного окна на NovellaHubWindow.
    /// Внутри переиспользует тот же IMGUI-код, что и вкладка Hub'а
    /// (NovellaVariableEditorModule.DrawGUI), чтобы не дублировать логику.
    /// </summary>
    public class NovellaGlobalVariablesWindow : EditorWindow
    {
        private NovellaVariableEditorModule _module;
        private string _initialSelection;

        /// <summary>
        /// Открывает окно как самостоятельное (НЕ дёргает Hub).
        /// </summary>
        /// <param name="variableToSelect">Опциональный ключ переменной для авто-выделения.</param>
        public static NovellaGlobalVariablesWindow ShowStandalone(string variableToSelect = null)
        {
            var win = GetWindow<NovellaGlobalVariablesWindow>(false, "📋 " + ToolLang.Get("Global Variables", "База Переменных"), true);
            win.minSize = new Vector2(820, 520);
            win._initialSelection = variableToSelect;
            win.Show();
            win.Focus();
            return win;
        }

        private void OnEnable()
        {
            _module = new NovellaVariableEditorModule();
            _module.OnEnable(this);

            if (!string.IsNullOrEmpty(_initialSelection))
            {
                _module.SelectByName(_initialSelection);
                _initialSelection = null;
            }
        }

        private void OnDisable()
        {
            if (_module != null) { _module.OnDisable(); _module = null; }
        }

        private void OnGUI()
        {
            if (_module == null) { OnEnable(); }
            _module.DrawGUI(new Rect(0, 0, position.width, position.height));
        }
    }
}
