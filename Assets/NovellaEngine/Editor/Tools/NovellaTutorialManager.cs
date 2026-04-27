using UnityEditor;
using UnityEngine;
using NovellaEngine.Editor.Tutorials;

namespace NovellaEngine.Editor
{
    /// <summary>
    /// Фасад над новой data-driven туториал-системой (NovellaTutorialManagerV2).
    /// Сохраняет старый публичный API, чтобы NovellaGraphWindow / NovellaHubWindow /
    /// все модули продолжали работать без правок их кода.
    ///
    /// Внутри:  NovellaTutorialManager.StartTutorial("CharacterEditor")
    ///       → NovellaTutorialManagerV2.StartTutorial(asset_with_TutorialKey="CharacterEditor")
    ///       → ассет загружается из Assets/NovellaEngine/Tutorials/Lessons/02_CharacterEditor.asset
    ///
    /// Если ассет не найден (юзер ещё не сделал миграцию) — выводим warning и ничего не ломаем.
    /// </summary>
    public static class NovellaTutorialManager
    {
        public static bool IsTutorialActive => NovellaTutorialManagerV2.IsTutorialActive;

        public static void StartTutorial(string tutorialKey)
        {
            EnsureMigrated();
            NovellaTutorialManagerV2.StartTutorial(tutorialKey);
        }

        public static void ForceStopTutorial() => NovellaTutorialManagerV2.ForceStopTutorial();

        public static void CompleteTutorial(string key, EditorWindow windowToClose = null)
        {
            // V2.CompleteTutorial сам переоткрывает Welcome через delayCall — не дублируем.
            if (NovellaTutorialManagerV2.IsTutorialActive)
            {
                NovellaTutorialManagerV2.CompleteTutorial();
            }

            if (windowToClose != null && windowToClose.GetType() != typeof(NovellaHubWindow))
            {
                EditorApplication.delayCall += () => { if (windowToClose != null) windowToClose.Close(); };
            }
        }

        public static void BlockBackgroundEvents(EditorWindow window, bool isHubGlobal = false)
            => NovellaTutorialManagerV2.BlockBackgroundEvents(window, isHubGlobal);

        public static void DrawOverlay(EditorWindow window, bool isHubGlobal = false)
            => NovellaTutorialManagerV2.DrawOverlay(window, isHubGlobal);

        // ─────────────── автомиграция ───────────────

        private static bool _migrationChecked;
        private static void EnsureMigrated()
        {
            if (_migrationChecked) return;
            _migrationChecked = true;

            string[] guids = AssetDatabase.FindAssets("t:NovellaTutorialAsset");
            if (guids == null || guids.Length == 0)
            {
                Debug.Log("[Novella Engine] Первый запуск: создаю ассеты туториалов в Assets/NovellaEngine/Tutorials/Lessons/...");
                NovellaTutorialMigrator.MigrateIfNeeded(force: false);
            }
        }
    }
}
