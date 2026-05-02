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
        // Когда добавляешь новое действие сюда: 1) расширь enum; 2) обработай
        // в HandleClick(); 3) дай ему имя/иконку в Forge-инспекторе.
        public enum BindingAction
        {
            None,
            GoToNode,        // перейти к ноде графа (использует OnClickGotoNodeId)
            StartNewGame,    // запустить историю с начала (StoryToStart)
            LoadLastSave,    // загрузить последнее сохранение (SelectedStoryID из PlayerPrefs)
            QuitGame,        // выйти из игры / выйти из Play-режима в editor'е
            ShowPanel,       // показать UI-элемент (TargetBindingId)
            HidePanel,       // скрыть UI-элемент (TargetBindingId)
            TogglePanel,     // переключить видимость UI-элемента (TargetBindingId)
            ChangeLanguage,  // сменить язык интерфейса (LanguageCode)
            OpenURL,         // открыть ссылку в браузере (URL)
        }

        [Tooltip("Что произойдёт при клике по кнопке. Настраивается в Кузнице UI.")]
        public BindingAction ClickAction = BindingAction.None;

        [Tooltip("Для GoToNode — id ноды графа куда прыгнуть.")]
        public string OnClickGotoNodeId;

        [Tooltip("Для StartNewGame — какую историю запускать.")]
        public NovellaEngine.Data.NovellaStory StoryToStart;

        [Tooltip("Для ShowPanel/HidePanel/TogglePanel — id целевого binding'а UI-элемента.")]
        public string TargetBindingId;

        [Tooltip("Для ChangeLanguage — код языка (RU, EN, ES, ...).")]
        public string LanguageCode = "EN";

        [Tooltip("Для OpenURL — адрес ссылки.")]
        public string URL = "https://";

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

            // Back-compat: старые binding'и без явного ClickAction, но с заполненным
            // OnClickGotoNodeId — автоматически считаем GoToNode.
            if (ClickAction == BindingAction.None && !string.IsNullOrEmpty(OnClickGotoNodeId))
                ClickAction = BindingAction.GoToNode;

            if (!_subscribedLocale)
            {
                NovellaLocalizationManager.OnLanguageChanged += Refresh;
                _subscribedLocale = true;
            }

            // Подписка на клик — только для статических кнопок с заданным действием.
            // NovellaChoice сам подключает свой обработчик отдельно (см. NovellaPlayer).
            bool hasAction = ClickAction != BindingAction.None
                          && !(ClickAction == BindingAction.GoToNode && string.IsNullOrEmpty(OnClickGotoNodeId));
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

        // Диспетчер клика — выбирает что делать по `ClickAction`. Каждое действие
        // изолировано: если что-то требует runtime-сервиса (Player, Launcher),
        // мягко падает, если его нет в сцене.
        private void HandleClick()
        {
            switch (ClickAction)
            {
                case BindingAction.GoToNode:
                    if (NovellaPlayer.Instance != null && !string.IsNullOrEmpty(OnClickGotoNodeId))
                        NovellaPlayer.Instance.JumpToNode(OnClickGotoNodeId);
                    break;

                case BindingAction.StartNewGame:
                    if (StoryToStart == null)
                    {
                        Debug.LogWarning("[NovellaUIBinding] StartNewGame: история не выбрана.");
                        break;
                    }
                    var launcherStart = UnityEngine.Object.FindFirstObjectByType<StoryLauncher>();
                    if (launcherStart != null)
                    {
                        // Свежий старт — стираем сохранение этой истории, чтобы не «продолжалось».
                        PlayerPrefs.DeleteKey($"NovellaSave_{StoryToStart.name}_Node");
                        launcherStart.TryLaunchStory(StoryToStart);
                    }
                    else Debug.LogWarning("[NovellaUIBinding] StartNewGame: StoryLauncher не найден в сцене.");
                    break;

                case BindingAction.LoadLastSave:
                {
                    string lastStoryName = PlayerPrefs.GetString("SelectedStoryID", "");
                    if (string.IsNullOrEmpty(lastStoryName))
                    {
                        Debug.LogWarning("[NovellaUIBinding] LoadLastSave: нет последнего сохранения.");
                        break;
                    }
                    var stories = Resources.LoadAll<NovellaEngine.Data.NovellaStory>("");
                    NovellaEngine.Data.NovellaStory match = null;
                    foreach (var s in stories) if (s != null && s.name == lastStoryName) { match = s; break; }
                    var launcherLoad = UnityEngine.Object.FindFirstObjectByType<StoryLauncher>();
                    if (match != null && launcherLoad != null) launcherLoad.TryLaunchStory(match);
                    else Debug.LogWarning($"[NovellaUIBinding] LoadLastSave: история '{lastStoryName}' или StoryLauncher не найдены.");
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
                    var target = NovellaUIBinding.Find(TargetBindingId);
                    if (target == null) break;
                    bool show = ClickAction == BindingAction.ShowPanel ? true
                              : ClickAction == BindingAction.HidePanel ? false
                              : !target.gameObject.activeSelf;
                    target.gameObject.SetActive(show);
                    break;
                }

                case BindingAction.ChangeLanguage:
                    if (!string.IsNullOrEmpty(LanguageCode))
                        NovellaLocalizationManager.SetLanguage(LanguageCode);
                    break;

                case BindingAction.OpenURL:
                    if (!string.IsNullOrEmpty(URL) && URL != "https://") Application.OpenURL(URL);
                    break;
            }
        }

        // ─── Refresh text content ───────────────────────────────────────────────

        // Перетягивает локализованную строку (если задан ключ) и подставляет
        // значение переменной (если задана). Вызывается при смене языка и в
        // OnEnable; ноды графа могут вызывать вручную после SetVariable.
        public void Refresh()
        {
            if (_tmp == null) _tmp = GetComponent<TMP_Text>();
            if (_tmp == null) return;

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

        private static string ResolveVariable(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            // Пытаемся int → bool → string в порядке частоты использования.
            if (NovellaVariables.IntVars != null && NovellaVariables.IntVars.TryGetValue(name, out var i)) return i.ToString();
            if (NovellaVariables.BoolVars != null && NovellaVariables.BoolVars.TryGetValue(name, out var b)) return b ? "true" : "false";
            if (NovellaVariables.StringVars != null && NovellaVariables.StringVars.TryGetValue(name, out var s)) return s ?? "";
            return "";
        }

        // ─── ID generation ──────────────────────────────────────────────────────

        private void EnsureId()
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
