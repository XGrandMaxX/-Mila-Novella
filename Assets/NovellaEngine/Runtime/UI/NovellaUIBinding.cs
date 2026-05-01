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

        [Tooltip("Опционально: ключ из таблицы локализации. Текст обновится при смене языка.\nПример: ui.button.start")]
        public string LocalizationKey;

        [Tooltip("Опционально: имя переменной из NovellaVariables. Подставится вместо {var} в локализованной строке.\nПример текста: \"HP: {var}\"")]
        public string BoundVariable;

        [Tooltip("Опционально: куда перейти при клике (если на этом GO есть UnityEngine.UI.Button).\nПодставь NodeID любой ноды графа — Player перейдёт на неё.")]
        public string OnClickGotoNodeId;

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

            if (!_subscribedLocale)
            {
                NovellaLocalizationManager.OnLanguageChanged += Refresh;
                _subscribedLocale = true;
            }

            // Подписка на клик-переход — только для статических кнопок
            // (NovellaChoice сам подключает и снимает свой обработчик отдельно).
            if (_button != null && !string.IsNullOrEmpty(OnClickGotoNodeId))
            {
                _clickHandler = HandleNavClick;
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

        private void HandleNavClick()
        {
            var p = NovellaPlayer.Instance;
            if (p != null && !string.IsNullOrEmpty(OnClickGotoNodeId)) p.JumpToNode(OnClickGotoNodeId);
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
                UnityEditor.EditorUtility.SetDirty(b);
            }
            else if (string.IsNullOrEmpty(b._id))
            {
                b.EnsureId();
                UnityEditor.EditorUtility.SetDirty(b);
            }
            return b;
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
