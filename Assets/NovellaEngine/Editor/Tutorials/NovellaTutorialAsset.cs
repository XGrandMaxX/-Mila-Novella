using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Editor.Tutorials
{
    /// <summary>
    /// Один туториал = один ScriptableObject-ассет.
    /// Создаётся через меню Assets → Create → Novella Engine → Tutorial.
    /// Может быть создан и автоматически миграцией старых хардкод-туториалов.
    /// </summary>
    [CreateAssetMenu(fileName = "New Tutorial", menuName = "Novella Engine/Tutorial Asset", order = 100)]
    public class NovellaTutorialAsset : ScriptableObject
    {
        // ──────────────────────────── ИДЕНТИФИКАЦИЯ ────────────────────────────

        [Tooltip("Ключ туториала. Используется в коде через NovellaTutorialManager.StartTutorial(\"YourKey\"). " +
                 "Должен быть уникален в проекте. Латиница, без пробелов.")]
        public string TutorialKey = "MyTutorial";

        [Tooltip("Название урока в Welcome-окне (EN).")]
        public string TitleEN = "My Tutorial";

        [Tooltip("Название урока в Welcome-окне (RU).")]
        public string TitleRU = "Мой Туториал";

        [TextArea(2, 4)]
        [Tooltip("Краткое описание урока (EN).")]
        public string DescriptionEN = "What this lesson covers.";

        [TextArea(2, 4)]
        [Tooltip("Краткое описание урока (RU).")]
        public string DescriptionRU = "О чём этот урок.";

        [Tooltip("Эмодзи-иконка урока в карточке Welcome-окна (одно эмодзи).")]
        public string Icon = "📖";

        [Tooltip("Порядковый номер шага в общем флоу обучения (1..6). " +
                 "Нужен для прогрессии — Welcome-окно блокирует уроки 2..N пока не пройден предыдущий.")]
        public int OrderIndex = 1;

        [Tooltip("Тип целевого окна, в котором будет показан этот туториал. " +
                 "WelcomeWindow_NoTarget = шаги показываются прямо в Welcome-окне без открытия других окон. " +
                 "Используется для самого первого вводного урока.")]
        public ETutorialHostWindow HostWindow = ETutorialHostWindow.NovellaHub_CharacterEditor;

        // ──────────────────────────────── ШАГИ ─────────────────────────────────

        [SerializeField]
        public List<NovellaTutorialStep> Steps = new List<NovellaTutorialStep>();
    }

    /// <summary>
    /// Тип целевого окна, в котором будет проигрываться туториал.
    /// Welcome-окно перед стартом урока открывает соответствующее окно автоматически.
    /// </summary>
    public enum ETutorialHostWindow
    {
        NovellaHub_Dashboard = 0,
        NovellaHub_CharacterEditor = 1,
        NovellaHub_SceneManager = 2,
        NovellaHub_UIEditor = 3,
        NovellaHub_VariableEditor = 4,
        GraphWindow = 10,
        GraphWindow_DLC = 11,
        GraphWindow_InteractiveLesson = 12,
        WelcomeWindow_NoTarget = 99,
    }
}
