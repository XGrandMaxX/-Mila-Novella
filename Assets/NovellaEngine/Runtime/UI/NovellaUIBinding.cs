// ════════════════════════════════════════════════════════════════════════════
// NovellaUIBinding
//
// Компонент-мост между UI-элементом в сцене и Novella runtime/графом.
// Висит на любом UI GameObject (TMP_Text, Button, Image, Panel) и даёт три
// независимые возможности:
//
//   1. Локализация — `LocalizationKey` подключает текст к
//      NovellaUILocalizationTable. При смене языка обновляется автоматически.
//
//   2. Переменные — `BoundVariable` подставляет значение из NovellaVariables
//      в плейсхолдер `{var}` внутри локализованной строки.
//      Пример: "HP: {var}" + BoundVariable=PlayerHP → "HP: 42".
//
//   3. Клик-навигация — если на этом же GO есть UnityEngine.UI.Button и
//      `OnClickGotoNodeId` непустой, при клике Player перепрыгивает на эту
//      ноду. Так делаются меню/инвентарь/кастомные кнопки прямо в сцене.
//
// Граф-ноды (DialogueLine, NovellaChoice, WaitNodeData, SceneSettingsEvent)
// ссылаются на конкретный binding по `Id` — это стабильный GUID, который
// проставляется один раз при добавлении компонента.
//
// Player находит binding через статический реестр `Find(id)` — он наполняется
// при OnEnable каждого компонента, что работает и в Edit, и в Play Mode.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;          // FirstOrDefault для поиска NovellaSaveSlotsUI в сцене
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NovellaEngine.Data;

namespace NovellaEngine.Runtime.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Novella/UI Binding")]
    public class NovellaUIBinding : MonoBehaviour
    {
        // Стабильный идентификатор. Проставляется при первом добавлении
        // компонента (через Reset/OnValidate в editor) и НЕ должен меняться,
        // иначе все ссылки на этот binding из графа потеряются.
        [SerializeField, HideInInspector] private string _id;
        public string Id => _id;

        [Tooltip("Дружелюбное имя для пикера в Novella Studio. Видно везде где этот элемент выбирается из ноды или Forge.")]
        public string Name;

        [Tooltip("Опционально: ключ из таблицы локализации. Текст обновится при смене языка.\nПример: ui.button.start")]
        public string LocalizationKey;

        [Tooltip("Опционально: имя переменной из NovellaVariables. Подставится вместо {var} в локализованной строке.\nПример текста: \"HP: {var}\"")]
        public string BoundVariable;

        // ─── Click-action ────────────────────────────────────────────────────
        // Что делает кнопка по клику. Все действия настраиваются полностью из
        // Кузницы UI — без захода в Unity-инспектор и без UnityEvent'ов.
        // Когда добавляешь новое действие: 1) расширь enum; 2) обработай в
        // ExecuteStep(); 3) дай ему имя/иконку/описание в Forge.
        public enum BindingAction
        {
            None,
            GoToNode,            // перейти к ноде графа
            StartNewGame,        // запустить историю с начала
            LoadLastSave,        // загрузить последнее сохранение
            QuitGame,            // выйти из игры
            ShowPanel,           // показать UI-элемент
            HidePanel,           // скрыть UI-элемент
            TogglePanel,         // переключить видимость UI-элемента
            ChangeLanguage,      // сменить язык интерфейса
            OpenURL,             // открыть ссылку в браузере
            SetVariable,         // установить значение NovellaVariables
            TriggerEvent,        // послать NovellaPlayer.OnNovellaEvent
            PlaySFX,             // проиграть AudioClip как SFX
            RestartChapter,      // перезапустить активную главу с нуля
            UnlockAchievement,   // выдать ачивку (хук, требует интеграции платформы)
            PauseGame,           // Time.timeScale = 0 (заморозка игрового времени)
            ResumeGame,          // Time.timeScale = 1 (снять паузу)
            // ─── Save Slot system ───────────────────────────────────────────
            SaveGameSlot,        // сохранить текущее состояние в слот N (1..N)
            LoadGameSlot,        // загрузить состояние из слота N (1..N)
            // (раньше тут был OpenSaveSlotsPanel — убран, потому что у нас уже
            // есть ShowPanel для UI элементов. Если нужно открыть панель сейвов —
            // юзер выбирает её через обычный ShowPanel.)
            // ─── Navigation ─────────────────────────────────────────────────
            ReturnToMainMenu,    // выйти в главное меню (загрузить menu-сцену)
        }

        // Один шаг последовательности кликов. Список таких шагов на кнопке
        // выполняется сверху вниз; каждый шаг ждёт DelayBefore перед собой.
        // Параметры разные у разных Action — лежат в одной структуре, читается
        // только то что нужно конкретному действию (зависимости explicit
        // прописаны в ExecuteStep).
        [System.Serializable]
        public class ClickActionStep
        {
            public BindingAction Action = BindingAction.None;
            [Tooltip("Сколько секунд ждать перед выполнением этого шага. Полезно чтобы дать SFX доиграть до загрузки сцены.")]
            public float DelayBefore = 0f;

            // GoToNode
            public string OnClickGotoNodeId;
            // StartNewGame
            public NovellaEngine.Data.NovellaStory StoryToStart;
            // ShowPanel / HidePanel / TogglePanel
            public string TargetBindingId;
            // ChangeLanguage
            public string LanguageCode = "EN";
            // OpenURL
            public string URL = "https://";
            // SetVariable
            public string VariableName;
            public int VariableInt;
            public bool VariableBool;
            public string VariableString = "";
            public float VariableFloat;
            // Для List: операция (Add/Remove/Clear) — используется только когда
            // целевая переменная типа List. Для остальных типов игнорируется.
            public NovellaEngine.Data.EVarOperation VariableListOp = NovellaEngine.Data.EVarOperation.ListAdd;
            // TriggerEvent
            public string EventName = "MyEvent";
            public string EventParam = "";
            // PlaySFX
            public AudioClip SfxClip;
            [Range(0f, 1f)] public float SfxVolume = 1f;
            // UnlockAchievement
            public string AchievementId;
            // SaveGameSlot / LoadGameSlot
            [Tooltip("Номер слота сохранения (1..N). 0 = автосейв.")]
            public int SaveSlotIndex = 1;
            // ReturnToMainMenu
            [Tooltip("Имя сцены главного меню в Build Settings. Если пусто — берётся первая сцена с MainMenuPanel или первая сцена из Build Settings.")]
            public string MainMenuSceneName = "";
        }

        [Tooltip("Последовательность действий по клику. Выполняются сверху вниз с задержкой DelayBefore у каждого.")]
        public System.Collections.Generic.List<ClickActionStep> ClickSequence = new System.Collections.Generic.List<ClickActionStep>();

        // ─── Legacy fields (back-compat — мигрируются в ClickSequence) ───────
        [HideInInspector] public BindingAction ClickAction = BindingAction.None;
        [HideInInspector] public string OnClickGotoNodeId;
        [HideInInspector] public NovellaEngine.Data.NovellaStory StoryToStart;
        [HideInInspector] public string TargetBindingId;
        [HideInInspector] public string LanguageCode = "EN";
        [HideInInspector] public string URL = "https://";

        // Какие действия «терминальные» (после них дальше не выполнится — они
        // меняют сцену, выходят из игры и т.п.). Используется и в инспекторе
        // (warning на следующих шагах), и при логировании в рантайме.
        public static bool IsTerminalAction(BindingAction a) =>
            a == BindingAction.StartNewGame ||
            a == BindingAction.LoadLastSave ||
            a == BindingAction.LoadGameSlot ||
            a == BindingAction.RestartChapter ||
            a == BindingAction.ReturnToMainMenu ||
            a == BindingAction.QuitGame;

        // Категория компонента — для группировки в пикерах (Text / Button / Other).
        public enum BindingKind { Other, Text, Button, Image }
        public BindingKind DetectKind()
        {
            if (GetComponent<TMP_Text>() != null) return BindingKind.Text;
            if (GetComponent<Button>()  != null) return BindingKind.Button;
            if (GetComponent<Image>()   != null) return BindingKind.Image;
            return BindingKind.Other;
        }

        // Удобное имя для отображения. Если Name пустое — используем имя GameObject.
        public string DisplayName => string.IsNullOrEmpty(Name) ? gameObject.name : Name;

        // ─── Static registry ────────────────────────────────────────────────────

        private static readonly Dictionary<string, NovellaUIBinding> _registry = new Dictionary<string, NovellaUIBinding>();

        public static NovellaUIBinding Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _registry.TryGetValue(id, out var b);
            // Если objект был уничтожен между сценами — Unity вернёт «null
            // объект-компаньон», который сравнивается с null правильно.
            if (b == null) { _registry.Remove(id); return null; }
            return b;
        }

        // Удобный поиск по типу: текстовый компонент привязанного binding'а.
        public static TMP_Text FindText(string id)
        {
            var b = Find(id);
            return b != null ? b.GetComponent<TMP_Text>() : null;
        }

        public static Button FindButton(string id)
        {
            var b = Find(id);
            return b != null ? b.GetComponent<Button>() : null;
        }

        // ─── Lifecycle ──────────────────────────────────────────────────────────

        private TMP_Text _tmp;
        private Button _button;
        private Action _clickHandler;
        private bool _subscribedLocale;

        private void Reset()
        {
            EnsureId();
            if (string.IsNullOrEmpty(Name)) Name = gameObject != null ? gameObject.name : "Element";
        }

        private void OnValidate()
        {
            EnsureId();
        }

        private void Awake()
        {
            EnsureId();
            _tmp = GetComponent<TMP_Text>();
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_id)) EnsureId();
            if (!string.IsNullOrEmpty(_id)) _registry[_id] = this;

            // Миграция legacy single-action → ClickSequence. Если list пустой и
            // на binding'е был выставлен ClickAction (или OnClickGotoNodeId) —
            // создаём первый шаг из legacy-полей и обнуляем legacy. Так старые
            // ассеты конвертируются в новый формат при первой загрузке.
            if (ClickSequence == null) ClickSequence = new System.Collections.Generic.List<ClickActionStep>();
            if (ClickSequence.Count == 0)
            {
                BindingAction legacy = ClickAction;
                if (legacy == BindingAction.None && !string.IsNullOrEmpty(OnClickGotoNodeId))
                    legacy = BindingAction.GoToNode;
                if (legacy != BindingAction.None)
                {
                    ClickSequence.Add(new ClickActionStep
                    {
                        Action = legacy,
                        OnClickGotoNodeId = OnClickGotoNodeId,
                        StoryToStart = StoryToStart,
                        TargetBindingId = TargetBindingId,
                        LanguageCode = LanguageCode,
                        URL = URL,
                    });
                    ClickAction = BindingAction.None;
                    OnClickGotoNodeId = null;
                    StoryToStart = null;
                    TargetBindingId = null;
                }
            }

            if (!_subscribedLocale)
            {
                NovellaLocalizationManager.OnLanguageChanged += Refresh;
                _subscribedLocale = true;
            }

            // Подписка на клик — для статических кнопок c непустой
            // ClickSequence. NovellaChoice сам подключает свой обработчик.
            bool hasAction = ClickSequence != null && ClickSequence.Count > 0;
            if (_button != null && hasAction)
            {
                _clickHandler = HandleClick;
                _button.onClick.AddListener(InvokeClickHandler);
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (_subscribedLocale)
            {
                NovellaLocalizationManager.OnLanguageChanged -= Refresh;
                _subscribedLocale = false;
            }

            if (_button != null && _clickHandler != null)
            {
                _button.onClick.RemoveListener(InvokeClickHandler);
                _clickHandler = null;
            }

            if (!string.IsNullOrEmpty(_id) && _registry.TryGetValue(_id, out var cur) && cur == this)
                _registry.Remove(_id);
        }

        private void InvokeClickHandler() => _clickHandler?.Invoke();

        // Запускает последовательность шагов как корутину.
        private void HandleClick()
        {
            if (ClickSequence == null || ClickSequence.Count == 0) return;
            if (!isActiveAndEnabled) return;
            StartCoroutine(RunSequence());
        }

        private System.Collections.IEnumerator RunSequence()
        {
            // Снимок списка — пользователь может нажать кнопку повторно во
            // время задержки и пересобрать sequence; мы выполняем именно ту
            // версию что была на момент клика.
            var snapshot = new System.Collections.Generic.List<ClickActionStep>(ClickSequence);
            for (int i = 0; i < snapshot.Count; i++)
            {
                var step = snapshot[i];
                if (step == null) continue;
                if (step.DelayBefore > 0f) yield return new WaitForSeconds(step.DelayBefore);
                ExecuteStep(step);
            }
        }

        // Выполняет один шаг. Изолирован от остальной последовательности —
        // если runtime-сервис (Player/Launcher) отсутствует, шаг тихо
        // пропускается с warning'ом, остальные продолжают выполнение.
        private void ExecuteStep(ClickActionStep step)
        {
            switch (step.Action)
            {
                case BindingAction.GoToNode:
                    if (NovellaPlayer.Instance != null && !string.IsNullOrEmpty(step.OnClickGotoNodeId))
                        NovellaPlayer.Instance.JumpToNode(step.OnClickGotoNodeId);
                    break;

                case BindingAction.StartNewGame:
                {
                    if (step.StoryToStart == null) { Debug.LogWarning("[NovellaUIBinding] StartNewGame: история не выбрана."); break; }
                    var launcher = UnityEngine.Object.FindFirstObjectByType<StoryLauncher>();
                    if (launcher == null) { Debug.LogWarning("[NovellaUIBinding] StartNewGame: StoryLauncher не найден."); break; }
                    PlayerPrefs.DeleteKey($"NovellaSave_{step.StoryToStart.name}_Node");
                    launcher.TryLaunchStory(step.StoryToStart);
                    break;
                }

                case BindingAction.LoadLastSave:
                {
                    var launcher = UnityEngine.Object.FindFirstObjectByType<StoryLauncher>();
                    if (launcher == null) { Debug.LogWarning("[NovellaUIBinding] LoadLastSave: StoryLauncher не найден в сцене."); break; }

                    string lastStoryName = PlayerPrefs.GetString("SelectedStoryID", "");
                    if (string.IsNullOrEmpty(lastStoryName))
                    {
                        // Сейва ещё нет — мягко перенаправляем на стартовый
                        // экран выбора истории вместо ругательного warning.
                        Debug.Log("[NovellaUIBinding] LoadLastSave: ещё ничего не сохранено — открываем выбор истории.");
                        launcher.ShowPanel(launcher.StoriesPanel);
                        break;
                    }

                    NovellaEngine.Data.NovellaStory match = null;
                    var stories = Resources.LoadAll<NovellaEngine.Data.NovellaStory>("");
                    foreach (var s in stories) if (s != null && s.name == lastStoryName) { match = s; break; }
#if UNITY_EDITOR
                    // Fallback в Editor: история могла не оказаться в Resources/
                    // (старый ассет не мигрировал). Ищем через AssetDatabase.
                    if (match == null)
                    {
                        var guids = UnityEditor.AssetDatabase.FindAssets("t:NovellaStory");
                        foreach (var g in guids)
                        {
                            var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<NovellaEngine.Data.NovellaStory>(p);
                            if (s != null && s.name == lastStoryName) { match = s; break; }
                        }
                    }
#endif

                    if (match != null)
                    {
                        launcher.TryLaunchStory(match);
                    }
                    else
                    {
                        Debug.LogWarning($"[NovellaUIBinding] LoadLastSave: история '{lastStoryName}' не найдена (возможно ассет удалён). Сбрасываем сейв и открываем выбор истории.");
                        PlayerPrefs.DeleteKey("SelectedStoryID");
                        launcher.ShowPanel(launcher.StoriesPanel);
                    }
                    break;
                }

                case BindingAction.QuitGame:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;

                case BindingAction.ShowPanel:
                case BindingAction.HidePanel:
                case BindingAction.TogglePanel:
                {
                    var target = NovellaUIBinding.Find(step.TargetBindingId);
                    if (target == null) break;
                    bool show = step.Action == BindingAction.ShowPanel ? true
                              : step.Action == BindingAction.HidePanel ? false
                              : !target.gameObject.activeSelf;
                    target.gameObject.SetActive(show);
                    break;
                }

                case BindingAction.ChangeLanguage:
                    if (!string.IsNullOrEmpty(step.LanguageCode))
                        NovellaLocalizationManager.SetLanguage(step.LanguageCode);
                    break;

                case BindingAction.OpenURL:
                    if (!string.IsNullOrEmpty(step.URL) && step.URL != "https://")
                        Application.OpenURL(step.URL);
                    break;

                case BindingAction.SetVariable:
                {
                    if (string.IsNullOrEmpty(step.VariableName)) break;
                    var settings = NovellaEngine.Data.NovellaVariableSettings.Instance;
                    var def = settings != null ? settings.Variables.Find(v => v.Name == step.VariableName) : null;
                    // Тип переменной берём из настроек (если есть), иначе пишем во все три словаря — runtime разберётся.
                    if (def == null || def.Type == NovellaEngine.Data.EVarType.Integer) NovellaVariables.SetInt(step.VariableName, step.VariableInt);
                    else if (def.Type == NovellaEngine.Data.EVarType.Boolean) NovellaVariables.SetBool(step.VariableName, step.VariableBool);
                    else if (def.Type == NovellaEngine.Data.EVarType.String)  NovellaVariables.SetString(step.VariableName, step.VariableString ?? "");
                    else if (def.Type == NovellaEngine.Data.EVarType.Float)   NovellaVariables.SetFloat(step.VariableName, step.VariableFloat);
                    else if (def.Type == NovellaEngine.Data.EVarType.Choice)  NovellaVariables.SetChoice(step.VariableName, step.VariableString ?? "");
                    else if (def.Type == NovellaEngine.Data.EVarType.List)
                    {
                        if (step.VariableListOp == NovellaEngine.Data.EVarOperation.ListAdd) NovellaVariables.ListAdd(step.VariableName, step.VariableString ?? "");
                        else if (step.VariableListOp == NovellaEngine.Data.EVarOperation.ListRemove) NovellaVariables.ListRemove(step.VariableName, step.VariableString ?? "");
                        else if (step.VariableListOp == NovellaEngine.Data.EVarOperation.ListClear) NovellaVariables.ListClear(step.VariableName);
                    }
                    break;
                }

                case BindingAction.TriggerEvent:
                    NovellaPlayer.RaiseNovellaEvent(step.EventName ?? "", step.EventParam ?? "");
                    break;

                case BindingAction.PlaySFX:
                    if (step.SfxClip != null)
                    {
                        var src = NovellaPlayer.Instance != null ? NovellaPlayer.Instance.gameObject : gameObject;
                        AudioSource.PlayClipAtPoint(step.SfxClip, src.transform.position, step.SfxVolume);
                    }
                    break;

                case BindingAction.RestartChapter:
                {
                    var p = NovellaPlayer.Instance;
                    if (p == null || p.StoryTree == null) { Debug.LogWarning("[NovellaUIBinding] RestartChapter: Player или StoryTree не найдены."); break; }
                    PlayerPrefs.DeleteKey($"NovellaSave_{p.StoryTree.name}_Node");
                    NovellaVariables.ResetLocalVariables();
                    p.PlayNode(p.StoryTree.RootNodeID);
                    break;
                }

                case BindingAction.UnlockAchievement:
                    NovellaPlayer.RaiseNovellaEvent("Achievement.Unlock", step.AchievementId ?? "");
                    Debug.Log($"[NovellaUIBinding] Achievement unlock requested: '{step.AchievementId}'. Подключи свою платформенную интеграцию через NovellaPlayer.OnNovellaEvent (event = 'Achievement.Unlock').");
                    break;

                case BindingAction.PauseGame:
                    // Time.timeScale = 0 замораживает физику, корутины с
                    // WaitForSeconds (но не WaitForSecondsRealtime), Animator,
                    // ParticleSystem (если используется simulation space). НЕ
                    // влияет на UI-input и Update. Если забыть вызвать ResumeGame —
                    // юзер увидит «зависшую» игру, поэтому громкий warning.
                    Time.timeScale = 0f;
                    Debug.LogWarning("[NovellaUIBinding] Game PAUSED (Time.timeScale=0). Не забудь вызвать ResumeGame чтобы снять паузу — иначе игра выглядит зависшей.");
                    break;

                case BindingAction.ResumeGame:
                    // Возврат к нормальной скорости. Использует ровно 1f
                    // (не сохраняем «прошлый timeScale»), потому что пауза в
                    // VN-играх обычно бинарная.
                    Time.timeScale = 1f;
                    Debug.Log("[NovellaUIBinding] Game RESUMED (Time.timeScale=1).");
                    break;

                // ─── Save Slot system — используем NovellaPlayer.SaveToSlot/LoadFromSlot ─
                case BindingAction.SaveGameSlot:
                {
                    var p = NovellaPlayer.Instance;
                    if (p == null) { Debug.LogWarning("[NovellaUIBinding] SaveGameSlot: Player не найден."); break; }
                    p.SaveToSlot(Mathf.Max(0, step.SaveSlotIndex));
                    break;
                }

                case BindingAction.LoadGameSlot:
                {
                    var p = NovellaPlayer.Instance;
                    if (p == null) { Debug.LogWarning("[NovellaUIBinding] LoadGameSlot: Player не найден."); break; }
                    p.LoadFromSlot(Mathf.Max(0, step.SaveSlotIndex));
                    break;
                }

                // ─── Navigation ───────────────────────────────────────────────
                case BindingAction.ReturnToMainMenu:
                {
                    string sceneName = step.MainMenuSceneName;
                    // Auto-detect: если имя не задано — берём первую сцену Build Settings.
                    // Это разумная heuristic, потому что в типовом проекте Novella Engine
                    // первой в Build Settings ставится сцена меню.
                    if (string.IsNullOrEmpty(sceneName))
                    {
                        if (UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings > 0)
                        {
                            string firstPath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(0);
                            sceneName = System.IO.Path.GetFileNameWithoutExtension(firstPath);
                        }
                    }
                    if (string.IsNullOrEmpty(sceneName))
                    {
                        Debug.LogError("[NovellaUIBinding] ReturnToMainMenu: имя сцены меню не задано и в Build Settings нет ни одной сцены.");
                        break;
                    }
                    // Снимаем паузу перед загрузкой — иначе следующая сцена откроется с timeScale=0.
                    Time.timeScale = 1f;
                    UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
                    break;
                }
            }
        }

        // ─── Refresh text content ───────────────────────────────────────────────

        // Перетягивает локализованную строку (если задан ключ) и подставляет
        // значение переменной (если задана). Вызывается при смене языка и в
        // OnEnable; ноды графа могут вызывать вручную после SetVariable.
        // Также авто-применяет Icon переменной к UI.Image на этом GO, если
        // компонент есть и у переменной задана иконка.
        public void Refresh()
        {
            // ─── Текст: TMP / Image-вариант обрабатываем независимо ───
            if (_tmp == null) _tmp = GetComponent<TMP_Text>();
            if (_tmp != null)
            {
                string text = null;

                if (!string.IsNullOrEmpty(LocalizationKey))
                {
                    text = NovellaLocalizationManager.Get(LocalizationKey);
                    if (string.IsNullOrEmpty(text)) text = LocalizationKey;
                }
                else
                {
                    // Без ключа — оставляем как есть (текст редактируется напрямую).
                    text = _tmp.text;
                }

                if (!string.IsNullOrEmpty(BoundVariable))
                {
                    text = text?.Replace("{var}", ResolveVariable(BoundVariable)) ?? "";
                }

                _tmp.text = text ?? "";
            }

            // ─── Иконка: если у переменной есть Icon И на GO висит Image —
            // подставляем спрайт. Так юзер может в Кузнице UI создать Image,
            // привязать к нему BoundVariable, и игра сама проставит картинку.
            if (!string.IsNullOrEmpty(BoundVariable))
            {
                var img = GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    var icon = ResolveVariableIcon(BoundVariable);
                    if (icon != null) img.sprite = icon;
                }
            }
        }

        private static string ResolveVariable(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // Порядок: Int → Float → Bool → String → Choice → List (для List
            // показываем счётчик элементов через запятую).
            if (NovellaVariables.IntVars != null && NovellaVariables.IntVars.TryGetValue(name, out var i)) return i.ToString();
            if (NovellaVariables.FloatVars != null && NovellaVariables.FloatVars.TryGetValue(name, out var f)) return f.ToString("0.##");
            if (NovellaVariables.BoolVars != null && NovellaVariables.BoolVars.TryGetValue(name, out var b)) return b ? "true" : "false";
            if (NovellaVariables.StringVars != null && NovellaVariables.StringVars.TryGetValue(name, out var s)) return s ?? "";
            if (NovellaVariables.ChoiceVars != null && NovellaVariables.ChoiceVars.TryGetValue(name, out var ch)) return ch ?? "";
            if (NovellaVariables.ListVars != null && NovellaVariables.ListVars.TryGetValue(name, out var lst)) return lst != null ? string.Join(", ", lst) : "";
            return "";
        }

        // Достаёт Icon-спрайт переменной из NovellaVariableSettings.
        // null — если переменной нет, или у неё нет иконки.
        private static Sprite ResolveVariableIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null || settings.Variables == null) return null;
            for (int i = 0; i < settings.Variables.Count; i++)
            {
                if (settings.Variables[i].Name == name) return settings.Variables[i].Icon;
            }
            return null;
        }

        // ─── ID generation ──────────────────────────────────────────────────────

        // Public, чтобы editor-скрипты (например пресеты сцен) могли явно
        // гарантировать наличие _id сразу после AddComponent — иначе придётся
        // надеяться на отложенный OnValidate.
        public void EnsureId()
        {
            if (string.IsNullOrEmpty(_id))
            {
                _id = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(this);
                }
#endif
            }
        }

        // ─── Editor helpers ─────────────────────────────────────────────────────

#if UNITY_EDITOR
        // Используется property-drawer'ом UIBindingTargetAttribute: при drag&drop
        // GameObject в поле инспектора ноды мы находим/создаём binding и берём Id.
        public static NovellaUIBinding GetOrAdd(GameObject go)
        {
            if (go == null) return null;
            var b = go.GetComponent<NovellaUIBinding>();
            if (b == null)
            {
                b = UnityEditor.Undo.AddComponent<NovellaUIBinding>(go);
                b.EnsureId();
                if (string.IsNullOrEmpty(b.Name)) b.Name = go.name;
                UnityEditor.EditorUtility.SetDirty(b);
            }
            else
            {
                bool dirty = false;
                if (string.IsNullOrEmpty(b._id)) { b.EnsureId(); dirty = true; }
                if (string.IsNullOrEmpty(b.Name)) { b.Name = go.name; dirty = true; }
                if (dirty) UnityEditor.EditorUtility.SetDirty(b);
            }
            return b;
        }

        // Все binding'и в открытых сценах. Используется пикерами в Forge/нодах.
        public static NovellaUIBinding[] FindAllInScene()
        {
            return UnityEngine.Object.FindObjectsByType<NovellaUIBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        // Найти binding по id среди ВСЕХ объектов сцены (registry в edit-mode
        // может быть пустым если объект ни разу не активировался).
        public static NovellaUIBinding FindInScene(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var b = Find(id);
            if (b != null) return b;
            var all = UnityEngine.Object.FindObjectsByType<NovellaUIBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) if (all[i]._id == id) return all[i];
            return null;
        }
#endif
    }
}
