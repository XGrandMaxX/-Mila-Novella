// ════════════════════════════════════════════════════════════════════════════
// NovellaLocalizedText
//
// Компонент-связка: вешается рядом с TMP_Text или UnityEngine.UI.Text.
// При старте берёт текст из таблицы по ключу и подставляет в текстовый компонент.
// Подписан на OnLanguageChanged — автоматически перезаписывает текст при смене языка.
// ════════════════════════════════════════════════════════════════════════════

using TMPro;
using UnityEngine;

namespace NovellaEngine.Data
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Novella/Localized Text")]
    public class NovellaLocalizedText : MonoBehaviour
    {
        [Tooltip("Localization key. Looked up in NovellaLocalizationManager.Table.")]
        public string Key;

        private TMP_Text _tmp;
        private UnityEngine.UI.Text _legacy;

        private void Awake()
        {
            CacheTargets();
        }

        private void OnEnable()
        {
            NovellaLocalizationManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            NovellaLocalizationManager.OnLanguageChanged -= Refresh;
        }

#if UNITY_EDITOR
        // Чтобы в редакторе тоже подхватывалось при смене ключа в инспекторе.
        private void OnValidate()
        {
            CacheTargets();
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            Refresh();
        }
#endif

        public void Refresh()
        {
            string text = NovellaLocalizationManager.Get(Key);
            if (string.IsNullOrEmpty(text)) text = Key;

            if (_tmp == null && _legacy == null) CacheTargets();

            if (_tmp != null) _tmp.text = text;
            else if (_legacy != null) _legacy.text = text;
        }

        private void CacheTargets()
        {
            if (_tmp == null) _tmp = GetComponent<TMP_Text>();
            if (_legacy == null) _legacy = GetComponent<UnityEngine.UI.Text>();
        }
    }
}
