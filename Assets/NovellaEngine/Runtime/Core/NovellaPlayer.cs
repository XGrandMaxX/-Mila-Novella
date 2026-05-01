using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;
using NovellaEngine.Runtime.UI;
using System.Linq;
using System;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovellaEngine.Runtime
{
    public class NovellaPlayer : MonoBehaviour
    {
        public static event Action<string, string> OnNovellaEvent;
        public static event Action<NovellaPlayer, NovellaNodeBase> OnExecuteDLCNode;

        // Глобальный доступ для кнопок/UI binding'ов которые должны
        // дёрнуть JumpToNode по клику. Заполняется в OnEnable.
        public static NovellaPlayer Instance { get; private set; }

        // Хук для NovellaUIBinding и любых внешних триггеров — перейти на ноду.
        public void JumpToNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            // Чистим спавнённые choice-кнопки (если были) — пользователь уже
            // решил куда идти через свою UI.
            if (ChoiceContainer != null)
            {
                for (int i = ChoiceContainer.childCount - 1; i >= 0; i--) Destroy(ChoiceContainer.GetChild(i).gameObject);
            }
            // И снимаем listener'ы которые мы навешивали на binding-кнопки в ShowChoices.
            ClearChoiceBindingListeners();
            PlayNode(nodeId);
        }

        [Header("Story Data")]
        public NovellaTree StoryTree;
        public string CurrentLanguage = "RU";

        [Header("UI Elements")]
        public GameObject DialoguePanel;
        public TMP_Text SpeakerNameText;
        public TMP_Text DialogueBodyText;
        public GameObject SaveNotification;

        [Header("Choices UI")]
        public Transform ChoiceContainer;
        public GameObject ChoiceButtonPrefab;

        [Header("Scene Elements")]
        public Transform CharactersContainer;

        private NovellaPoolManager _poolManager;

        private NovellaNodeBase _currentNodeBase;
        private DialogueNodeData _currentDialogue;

        private int _currentLineIndex = 0;
        private bool _isWaitingForClick = false;
        private bool _isTyping = false;
        private Coroutine _typewriterCoroutine;

        private GameObject _defaultDialoguePanel;
        private TMP_Text _defaultSpeakerNameText;
        private TMP_Text _defaultDialogueBodyText;
        private GameObject _currentCustomFrame;

        private bool _isWaitNodeActive = false;
        private WaitNodeData _currentWaitNode;
        private Coroutine _waitNodeCoroutine;
        private GameObject _clickIndicator;
        private Coroutine _clickIndicatorCoroutine;
        private Coroutine _saveNotifCoroutine;

        private bool _isFastForwarding = false;
        private float _autoSaveTimer = 0f;

        // Listener'ы, которые ShowChoices навесил на binding-кнопки в сцене.
        // Снимаются при следующем ShowChoices/JumpToNode чтобы не накапливаться.
        private readonly List<(Button btn, UnityEngine.Events.UnityAction handler)> _choiceBindingListeners = new List<(Button, UnityEngine.Events.UnityAction)>();

        private void OnEnable()  { Instance = this; }
        private void OnDisable() { if (Instance == this) Instance = null; }

        private void Start()
        {
            if (FindFirstObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();

            _poolManager = gameObject.GetComponent<NovellaPoolManager>();
            if (_poolManager == null) _poolManager = gameObject.AddComponent<NovellaPoolManager>();
            _poolManager.InitializePools();

            _defaultDialoguePanel = DialoguePanel;
            _defaultSpeakerNameText = SpeakerNameText;
            _defaultDialogueBodyText = DialogueBodyText;

            if (DialoguePanel != null)
            {
                Transform tempWait = DialoguePanel.transform.parent.Find("TempWaitIndicator_Preview");
                if (tempWait != null) Destroy(tempWait.gameObject);
            }

            NovellaVariables.Initialize();

            string selectedStoryID = PlayerPrefs.GetString("SelectedStoryID", "");
            if (!string.IsNullOrEmpty(selectedStoryID))
            {
                var allStories = Resources.LoadAll<NovellaStory>("Stories");
                var activeStory = allStories.FirstOrDefault(s => s.name == selectedStoryID);
                if (activeStory != null && activeStory.StartingChapter != null)
                {
                    StoryTree = activeStory.StartingChapter;
                }
                else
                {
                    Debug.LogWarning($"[NovellaPlayer] Не удалось найти историю '{selectedStoryID}' в Resources/Stories. Будет использован граф из инспектора (если он назначен).");
                }
            }

            SetupMCProfile();

            string targetNodeID = PlayerPrefs.GetString("LoadTargetNodeID", "");

            if (!string.IsNullOrEmpty(targetNodeID) && StoryTree != null)
            {
                Debug.Log($"[NovellaPlayer] Загрузка сохраненной игры. Нода: {targetNodeID}");

                var saveData = NovellaSaveManager.LoadVariables(StoryTree.name);
                if (saveData != null)
                {
                    _currentLineIndex = saveData.CurrentLineIndex;
                }

                ClearCharacters();

                _isFastForwarding = true;
                PlayNode(targetNodeID);
                _isFastForwarding = false;
            }
            else if (StoryTree != null)
            {
                Debug.Log($"[NovellaPlayer] Старт новой игры. Граф: {StoryTree.name}");
                PlayTree(StoryTree);
            }
            else
            {
                Debug.LogError("[NovellaPlayer] Story Tree is not assigned! Пожалуйста, выберите историю в меню или назначьте граф вручную в инспекторе NovellaPlayer.");
            }
        }
        private void SetupMCProfile()
        {
            if (DialoguePanel == null || StoryTree == null) return;

            Canvas rootCanvas = DialoguePanel.GetComponentInParent<Canvas>(true);
            if (rootCanvas != null && rootCanvas.rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;

            Transform mcPanel = rootCanvas.transform.Find("MCCreationPanel");
            if (mcPanel != null)
            {
                mcPanel.gameObject.SetActive(false);

                string mcName = PlayerPrefs.GetString($"NovellaSave_{StoryTree.name}_MCName", "Alex");
                int bodyId = PlayerPrefs.GetInt($"NovellaSave_{StoryTree.name}_MCBodyID", 0);

                var inputField = mcPanel.GetComponentInChildren<TMP_InputField>(true);
                if (inputField != null) inputField.text = mcName;

                var avatarPreview = mcPanel.Find("AvatarPreview")?.GetComponent<Image>();
                if (avatarPreview != null)
                {
                    NovellaCharacter mcAsset = null;

                    foreach (var node in StoryTree.Nodes)
                    {
                        if (node is DialogueNodeData dialData)
                        {
                            var mc = dialData.ActiveCharacters.FirstOrDefault(c => c.CharacterAsset != null && c.CharacterAsset.IsPlayerCharacter);
                            if (mc != null)
                            {
                                mcAsset = mc.CharacterAsset;
                                break;
                            }
                        }
                    }

                    if (mcAsset != null && bodyId >= 0 && bodyId < mcAsset.AvailableBaseBodies.Count)
                    {
                        avatarPreview.sprite = mcAsset.AvailableBaseBodies[bodyId];
                        avatarPreview.color = Color.white;
                    }
                    else
                    {
                        avatarPreview.sprite = null;
                        avatarPreview.color = new Color(0.2f, 0.2f, 0.2f, 0f);
                    }
                }
            }
        }
        private void SaveProgress()
        {
            if (_currentNodeBase == null || StoryTree == null) return;

            if (_currentNodeBase.NodeType == ENodeType.End) return;

            NovellaSaveManager.SaveGame(StoryTree.name, _currentNodeBase.NodeID, _currentLineIndex);

            if (SaveNotification != null && !_isFastForwarding)
            {
                if (_saveNotifCoroutine != null) StopCoroutine(_saveNotifCoroutine);
                _saveNotifCoroutine = StartCoroutine(ShowSaveNotification());
            }
        }

        private IEnumerator ShowSaveNotification()
        {
            SaveNotification.SetActive(true);
            var cg = SaveNotification.GetComponent<CanvasGroup>();
            if (cg == null) cg = SaveNotification.AddComponent<CanvasGroup>();

            for (float t = 0; t < 0.3f; t += Time.deltaTime)
            {
                cg.alpha = t / 0.3f;
                yield return null;
            }
            cg.alpha = 1f;

            yield return new WaitForSeconds(2f);

            for (float t = 0; t < 0.5f; t += Time.deltaTime)
            {
                cg.alpha = 1f - (t / 0.5f);
                yield return null;
            }
            cg.alpha = 0f;
            SaveNotification.SetActive(false);
        }


        public void PlayTree(NovellaTree tree)
        {
            StoryTree = tree;
            NovellaVariables.ResetLocalVariables();
            ClearCharacters();
            PlayNode(tree.RootNodeID);
        }

        public void PlayNode(string nodeID)
        {
            if (string.IsNullOrEmpty(nodeID))
            {
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
                if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);
                return;
            }

            _currentNodeBase = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == nodeID);
            if (_currentNodeBase == null) return;

            if (_currentNodeBase.NodeType == ENodeType.CustomDLC)
            {
                var settings = NovellaDLCSettings.Instance;
                if (settings != null && !settings.IsDLCEnabled(_currentNodeBase.GetType().FullName))
                {
                    var outFields = DLCCache.GetOutputFields(_currentNodeBase.GetType());
                    string nextNodeID = "";
                    if (outFields.Count > 0) nextNodeID = (string)outFields.First().GetValue(_currentNodeBase);
                    else
                    {
                        var fallbackField = _currentNodeBase.GetType().GetField("NextNodeID");
                        if (fallbackField != null) nextNodeID = (string)fallbackField.GetValue(_currentNodeBase);
                    }
                    PlayNode(nextNodeID);
                    return;
                }
            }

            if (_currentNodeBase is DialogueNodeData dialData)
            {
                _currentDialogue = dialData;
                if (!_isFastForwarding) _currentLineIndex = 0;
                ProcessDialogueLine();
            }
            else if (_currentNodeBase is BranchNodeData branchData) ShowChoices(branchData);
            else if (_currentNodeBase is ConditionNodeData condData) ProcessConditionNode(condData);
            else if (_currentNodeBase is RandomNodeData rndData) ProcessRandomNode(rndData);
            else if (_currentNodeBase is AudioNodeData audData) ProcessStandaloneAudio(audData);
            else if (_currentNodeBase is VariableNodeData varData) ProcessVariables(varData);
            else if (_currentNodeBase is WaitNodeData waitData) ProcessWaitNode(waitData);
            else if (_currentNodeBase is SceneSettingsNodeData bgData) ProcessSceneSettingsNode(bgData);
            else if (_currentNodeBase is AnimationNodeData animData) ProcessAnimationNode(animData);
            else if (_currentNodeBase is EventBroadcastNodeData ebData) ProcessEventBroadcastNode(ebData);
            else if (_currentNodeBase is SaveNodeData saveData) ProcessSaveNode(saveData);
            else if (_currentNodeBase is EndNodeData endData) ProcessEndNode(endData);
            else if (_currentNodeBase.NodeType == ENodeType.CustomDLC)
            {
                if (_currentNodeBase is INovellaDLCExecutable executableNode)
                {
                    string nextNodeID = executableNode.Execute(this);
                    PlayNode(nextNodeID);
                }
                else
                {
                    OnExecuteDLCNode?.Invoke(this, _currentNodeBase);
                }
            }
        }

        private void ProcessSaveNode(SaveNodeData saveData)
        {
            if (!_isFastForwarding)
            {
                _autoSaveTimer = 0f;
                SaveProgress();
            }
            PlayNode(saveData.NextNodeID);
        }

        private void ProcessEventBroadcastNode(EventBroadcastNodeData ebData)
        {
            if (!_isFastForwarding) OnNovellaEvent?.Invoke(ebData.BroadcastEventName, ebData.BroadcastEventParam);
            PlayNode(ebData.NextNodeID);
        }

        private void ProcessSceneSettingsNode(SceneSettingsNodeData bgData)
        {
            if (bgData.SyncWithDialogue) { PlayNode(bgData.NextNodeID); return; }

            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            if (bgData.BgClearCharacters) ClearCharacters();

            if (_isFastForwarding || bgData.BgTransition == EBgTransition.None)
            {
                GameObject bgObj = GameObject.Find("Background");
                if (bgObj != null)
                {
                    var img = bgObj.GetComponent<Image>();
                    if (img != null) { img.sprite = bgData.BgSprite; img.color = bgData.BgColor; }
                }
                PlayNode(bgData.NextNodeID);
            }
            else
            {
                StartCoroutine(BackgroundTransitionRoutine(bgData));
            }
        }

        private IEnumerator BackgroundTransitionRoutine(SceneSettingsNodeData bgData)
        {
            GameObject bgObj = GameObject.Find("Background");
            Image bgImg = bgObj != null ? bgObj.GetComponent<Image>() : null;

            if (bgImg != null)
            {
                GameObject overlayObj = new GameObject("BgOverlayTransition");
                overlayObj.transform.SetParent(bgImg.transform.parent, false);
                overlayObj.transform.SetSiblingIndex(bgImg.transform.GetSiblingIndex() + 1);

                var rt = overlayObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                var overlayImg = overlayObj.AddComponent<Image>();

                float t = 0;

                if (bgData.BgTransition == EBgTransition.FlashWhite || bgData.BgTransition == EBgTransition.FlashBlack)
                {
                    Color flashCol = bgData.BgTransition == EBgTransition.FlashWhite ? Color.white : Color.black;

                    while (t < bgData.BgTransitionTime / 2f)
                    {
                        t += Time.deltaTime;
                        overlayImg.color = new Color(flashCol.r, flashCol.g, flashCol.b, Mathf.Lerp(0, 1, t / (bgData.BgTransitionTime / 2f)));
                        yield return null;
                    }

                    bgImg.sprite = bgData.BgSprite;
                    bgImg.color = bgData.BgColor;

                    t = 0;
                    while (t < bgData.BgTransitionTime / 2f)
                    {
                        t += Time.deltaTime;
                        overlayImg.color = new Color(flashCol.r, flashCol.g, flashCol.b, Mathf.Lerp(1, 0, t / (bgData.BgTransitionTime / 2f)));
                        yield return null;
                    }
                }
                else if (bgData.BgTransition == EBgTransition.Fade)
                {
                    overlayImg.sprite = bgData.BgSprite;
                    overlayImg.color = new Color(bgData.BgColor.r, bgData.BgColor.g, bgData.BgColor.b, 0);

                    while (t < bgData.BgTransitionTime)
                    {
                        t += Time.deltaTime;
                        overlayImg.color = new Color(bgData.BgColor.r, bgData.BgColor.g, bgData.BgColor.b, Mathf.Lerp(0, 1, t / bgData.BgTransitionTime));
                        yield return null;
                    }

                    bgImg.sprite = bgData.BgSprite;
                    bgImg.color = bgData.BgColor;
                }
                else
                {
                    bgImg.sprite = bgData.BgSprite;
                    bgImg.color = bgData.BgColor;
                    yield return new WaitForSeconds(bgData.BgTransitionTime);
                }

                Destroy(overlayObj);
            }

            PlayNode(bgData.NextNodeID);
        }

        private void ProcessAnimationNode(AnimationNodeData animData)
        {
            if (animData.SyncWithDialogue) { PlayNode(animData.NextNodeID); return; }

            if (!_isFastForwarding)
            {
                foreach (var ev in animData.AnimEvents)
                {
                    StartCoroutine(RunAnimEventCoroutine(ev));
                }
            }
            PlayNode(animData.NextNodeID);
        }

        private void ProcessSyncedAnim(EAudioTriggerType trigger, int lineIndex)
        {
            if (_isFastForwarding || _currentDialogue == null || string.IsNullOrEmpty(_currentDialogue.AnimSyncNodeID)) return;
            var animNode = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == _currentDialogue.AnimSyncNodeID) as AnimationNodeData;
            if (animNode == null) return;

            foreach (var ev in animNode.AnimEvents)
            {
                if (ev.TriggerType == trigger && (trigger == EAudioTriggerType.OnDialogueEnd || ev.LineIndex == lineIndex))
                {
                    StartCoroutine(RunAnimEventCoroutine(ev));
                }
            }
        }

        private void ProcessSyncedSceneSettings(EAudioTriggerType trigger, int lineIndex)
        {
            if (_currentDialogue == null || string.IsNullOrEmpty(_currentDialogue.SceneSyncNodeID)) return;
            var sceneNode = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == _currentDialogue.SceneSyncNodeID) as SceneSettingsNodeData;
            if (sceneNode == null) return;

            foreach (var ev in sceneNode.SceneEvents)
            {
                if (ev.TriggerType == trigger && (trigger == EAudioTriggerType.OnDialogueEnd || ev.LineIndex == lineIndex))
                {
                    if (ev.ActionType == ESceneActionType.ChangeBackground)
                    {
                        GameObject bgObj = GameObject.Find("Background");
                        if (bgObj != null)
                        {
                            var img = bgObj.GetComponent<Image>();
                            if (img != null) { img.sprite = ev.BgSprite; img.color = ev.BgColor; }
                        }
                    }
                    else if (ev.ActionType == ESceneActionType.ClearAllCharacters)
                    {
                        ClearCharacters();
                    }
                    else if (ev.ActionType == ESceneActionType.HideCharacter && ev.TargetCharacter != null)
                    {
                        var charGo = CharactersContainer.Find("Char_" + ev.TargetCharacter.name);
                        if (charGo != null) charGo.gameObject.SetActive(false);
                    }
                    else if (ev.ActionType == ESceneActionType.ShowCharacter && ev.TargetCharacter != null)
                    {
                        var charGo = CharactersContainer.Find("Char_" + ev.TargetCharacter.name);
                        if (charGo != null) charGo.gameObject.SetActive(true);
                    }
                    else if (ev.ActionType == ESceneActionType.ShowUI && !string.IsNullOrEmpty(ev.UITargetId))
                    {
                        var b = NovellaUIBinding.Find(ev.UITargetId);
                        if (b != null) b.gameObject.SetActive(true);
                    }
                    else if (ev.ActionType == ESceneActionType.HideUI && !string.IsNullOrEmpty(ev.UITargetId))
                    {
                        var b = NovellaUIBinding.Find(ev.UITargetId);
                        if (b != null) b.gameObject.SetActive(false);
                    }
                    else if (ev.ActionType == ESceneActionType.SetUIText && !string.IsNullOrEmpty(ev.UITargetId))
                    {
                        var t = NovellaUIBinding.FindText(ev.UITargetId);
                        if (t != null)
                        {
                            string raw = ev.UITextValue ?? "";
                            if (ev.UITextIsLocalizationKey) raw = NovellaLocalizationManager.Get(raw);
                            t.text = raw;
                        }
                    }
                }
            }
        }

        private IEnumerator RunAnimEventCoroutine(NovellaAnimEvent ev)
        {
            if (ev.TriggerType == EAudioTriggerType.TimeDelay)
                yield return new WaitForSeconds(ev.TimeDelay);

            Transform targetTr = null;
            SpriteRenderer sr = null;
            CanvasGroup cg = null;
            Image img = null;

            if (ev.Target == EAnimTarget.Camera && Camera.main != null) targetTr = Camera.main.transform;
            else if (ev.Target == EAnimTarget.Background)
            {
                var bg = GameObject.Find("Background");
                if (bg) { targetTr = bg.transform; img = bg.GetComponent<Image>(); }
            }
            else if (ev.Target == EAnimTarget.DialogueFrame && DialoguePanel != null)
            {
                targetTr = DialoguePanel.transform;
                cg = DialoguePanel.GetComponent<CanvasGroup>();
                if (!cg) cg = DialoguePanel.gameObject.AddComponent<CanvasGroup>();
            }
            else if (ev.Target == EAnimTarget.Character && ev.TargetCharacter != null)
            {
                var charGo = CharactersContainer.Find("Char_" + ev.TargetCharacter.name);
                if (charGo) { targetTr = charGo; sr = charGo.GetComponent<SpriteRenderer>(); }
            }

            if (targetTr == null) yield break;

            float t = 0;
            Vector3 startPos = targetTr.localPosition;
            Vector3 startScale = targetTr.localScale;

            float startAlpha = 1f;
            if (sr) startAlpha = sr.color.a; else if (cg) startAlpha = cg.alpha; else if (img) startAlpha = img.color.a;

            while (t < ev.Duration)
            {
                t += Time.deltaTime;
                float norm = Mathf.Clamp01(t / ev.Duration);

                if (ev.AnimType == EAnimType.Shake)
                {
                    targetTr.localPosition = startPos + (Vector3)UnityEngine.Random.insideUnitCircle * ev.Strength * (1f - norm);
                }
                else if (ev.AnimType == EAnimType.Punch)
                {
                    targetTr.localScale = startScale + Vector3.one * (Mathf.Sin(norm * Mathf.PI) * ev.Strength * 0.1f);
                }
                else if (ev.AnimType == EAnimType.FadeIn)
                {
                    float a = Mathf.Lerp(0f, 1f, norm);
                    if (sr) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, a);
                    if (cg) cg.alpha = a;
                    if (img) img.color = new Color(img.color.r, img.color.g, img.color.b, a);
                }
                else if (ev.AnimType == EAnimType.FadeOut)
                {
                    float a = Mathf.Lerp(1f, 0f, norm);
                    if (sr) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, a);
                    if (cg) cg.alpha = a;
                    if (img) img.color = new Color(img.color.r, img.color.g, img.color.b, a);
                }
                else if (ev.AnimType == EAnimType.MoveTo)
                {
                    targetTr.localPosition = Vector3.Lerp(startPos, new Vector3(ev.EndVector.x, ev.EndVector.y, startPos.z), norm);
                }
                else if (ev.AnimType == EAnimType.Scale)
                {
                    targetTr.localScale = Vector3.Lerp(startScale, new Vector3(ev.EndVector.x, ev.EndVector.y, startScale.z), norm);
                }

                yield return null;
            }

            if (ev.AnimType == EAnimType.Shake) targetTr.localPosition = startPos;
            if (ev.AnimType == EAnimType.Punch) targetTr.localScale = startScale;
            if (ev.AnimType == EAnimType.MoveTo) targetTr.localPosition = new Vector3(ev.EndVector.x, ev.EndVector.y, startPos.z);
            if (ev.AnimType == EAnimType.Scale) targetTr.localScale = new Vector3(ev.EndVector.x, ev.EndVector.y, startScale.z);
        }

        private void ProcessWaitNode(WaitNodeData waitData)
        {
            if (_isFastForwarding) { PlayNode(waitData.NextNodeID); return; }

            _currentWaitNode = waitData;
            if (waitData.WaitClearText && DialogueBodyText != null)
            {
                DialogueBodyText.text = "";
                if (SpeakerNameText != null) SpeakerNameText.text = "";
            }

            if (waitData.WaitHideFrame)
            {
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
                if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);
            }

            ShowClickIndicator();
            _isWaitNodeActive = true;

            if (waitData.WaitMode == EWaitMode.Time)
            {
                if (_waitNodeCoroutine != null) StopCoroutine(_waitNodeCoroutine);
                _waitNodeCoroutine = StartCoroutine(WaitTimeRoutine(waitData.WaitTime, waitData.WaitIsSkippable));
            }
        }

        private IEnumerator WaitTimeRoutine(float time, bool skippable)
        {
            float t = 0;
            while (t < time)
            {
                t += Time.deltaTime;
                if (!_isWaitNodeActive && skippable) break;
                yield return null;
            }
            _isWaitNodeActive = false;
            HideClickIndicator();
            PlayNode(_currentWaitNode.NextNodeID);
        }

        private void ShowClickIndicator()
        {
            if (_clickIndicator == null) CreateClickIndicator();

            _clickIndicator.SetActive(true);
            _clickIndicator.transform.SetAsLastSibling();

            var img = _clickIndicator.GetComponent<Image>();
            var rt = _clickIndicator.GetComponent<RectTransform>();
            var txt = _clickIndicator.GetComponentInChildren<TextMeshProUGUI>(true);
            var txtRt = txt.GetComponent<RectTransform>();

            if (_currentWaitNode.WaitIndicatorPreset == EFramePosition.Top) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); }
            else if (_currentWaitNode.WaitIndicatorPreset == EFramePosition.Center) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); }
            else if (_currentWaitNode.WaitIndicatorPreset == EFramePosition.Bottom || _currentWaitNode.WaitIndicatorPreset == EFramePosition.Default) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); }
            else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); }

            Vector2 startBasePos = new Vector2(_currentWaitNode.WaitIndicatorPosX, _currentWaitNode.WaitIndicatorPosY);
            rt.anchoredPosition = startBasePos;
            rt.sizeDelta = new Vector2(_currentWaitNode.WaitIndicatorSize, _currentWaitNode.WaitIndicatorSize);

            if (_currentWaitNode.WaitIndicatorSprite != null)
            {
                img.sprite = _currentWaitNode.WaitIndicatorSprite;
                rt.localRotation = Quaternion.identity;
                txtRt.localRotation = Quaternion.identity;
            }
            else
            {
                img.sprite = null;
                rt.localRotation = Quaternion.Euler(0, 0, 45);
                txtRt.localRotation = Quaternion.Euler(0, 0, -45);
            }
            img.color = _currentWaitNode.WaitIndicatorColor;

            // Если задан UI binding — пишем WaitText туда вместо встроенного индикатора.
            // Сам индикатор-стрелочка остаётся пульсировать как обычно.
            if (!string.IsNullOrEmpty(_currentWaitNode.UITextTargetId))
            {
                var bound = NovellaUIBinding.FindText(_currentWaitNode.UITextTargetId);
                if (bound != null)
                {
                    bound.text = _currentWaitNode.WaitText ?? "";
                    txt.gameObject.SetActive(false); // встроенный текст-индикатор отключаем
                }
                else
                {
                    // Binding не нашёлся — показываем встроенный как fallback.
                    if (!string.IsNullOrWhiteSpace(_currentWaitNode.WaitText))
                    {
                        txt.gameObject.SetActive(true);
                        txt.text = _currentWaitNode.WaitText;
                        txt.color = _currentWaitNode.WaitTextColor;
                        txt.fontSize = _currentWaitNode.WaitTextSize;
                        txtRt.anchoredPosition = new Vector2(_currentWaitNode.WaitTextPosX, _currentWaitNode.WaitTextPosY);
                    }
                    else txt.gameObject.SetActive(false);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_currentWaitNode.WaitText))
            {
                txt.gameObject.SetActive(true);
                txt.text = _currentWaitNode.WaitText;
                txt.color = _currentWaitNode.WaitTextColor;
                txt.fontSize = _currentWaitNode.WaitTextSize;
                txtRt.anchoredPosition = new Vector2(_currentWaitNode.WaitTextPosX, _currentWaitNode.WaitTextPosY);
            }
            else
            {
                txt.gameObject.SetActive(false);
            }

            if (_clickIndicatorCoroutine != null) StopCoroutine(_clickIndicatorCoroutine);
            _clickIndicatorCoroutine = StartCoroutine(ClickIndicatorPulse(_currentWaitNode.WaitIndicatorAnimSpeed, _currentWaitNode.WaitIndicatorAmplitude, _currentWaitNode.WaitIndicatorColor, startBasePos, txt));
        }

        private void HideClickIndicator()
        {
            if (_clickIndicator != null) _clickIndicator.SetActive(false);
            if (_clickIndicatorCoroutine != null) { StopCoroutine(_clickIndicatorCoroutine); _clickIndicatorCoroutine = null; }
        }

        private void CreateClickIndicator()
        {
            GameObject go = new GameObject("NovellaClickIndicator");
            Canvas canvas = DialoguePanel != null ? DialoguePanel.GetComponentInParent<Canvas>() : FindFirstObjectByType<Canvas>();
            if (canvas != null) go.transform.SetParent(canvas.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            go.AddComponent<Image>();

            GameObject txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(800, 100);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            txt.alignment = TextAlignmentOptions.Center;

            _clickIndicator = go;
        }

        private IEnumerator ClickIndicatorPulse(float speed, float amplitude, Color baseColor, Vector2 basePos, TextMeshProUGUI txt)
        {
            if (_clickIndicator == null) yield break;
            RectTransform rt = _clickIndicator.GetComponent<RectTransform>();
            Image img = _clickIndicator.GetComponent<Image>();
            Color textBaseColor = _currentWaitNode.WaitTextColor;

            float t = 0;
            while (true)
            {
                t += Time.deltaTime;
                float offset = Mathf.Sin(t * speed) * amplitude;
                float alpha = 0.4f + (Mathf.Sin(t * speed * 0.8f) + 1f) * 0.3f;
                float textAlpha = 0.3f + (Mathf.Sin(t * _currentWaitNode.WaitTextBlinkSpeed) + 1f) * 0.35f;

                rt.anchoredPosition = basePos + new Vector2(0, offset);
                img.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha * baseColor.a);

                if (txt != null && txt.gameObject.activeSelf)
                {
                    txt.color = new Color(textBaseColor.r, textBaseColor.g, textBaseColor.b, textAlpha * textBaseColor.a);
                }

                yield return null;
            }
        }

        private void ProcessConditionNode(ConditionNodeData condData)
        {
            bool isTrue = CheckConditions(condData.Conditions);
            if (isTrue && condData.Choices.Count > 0) PlayNode(condData.Choices[0].NextNodeID);
            else if (!isTrue && condData.Choices.Count > 1) PlayNode(condData.Choices[1].NextNodeID);
            else PlayNode("");
        }

        private void ProcessRandomNode(RandomNodeData rndData)
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            if (rndData.Choices == null || rndData.Choices.Count == 0) { PlayNode(""); return; }

            int totalWeight = 0;
            List<int> finalWeights = new List<int>();

            foreach (var choice in rndData.Choices)
            {
                int w = choice.ChanceWeight;
                foreach (var mod in choice.ChanceModifiers)
                {
                    if (CheckSingleCondition(mod.Variable, mod.Operator, mod.Value, mod.ValueBool, mod.ValueString)) w += mod.BonusWeight;
                }
                w = Mathf.Max(0, w);
                finalWeights.Add(w);
                totalWeight += w;
            }

            if (totalWeight <= 0) { PlayNode(rndData.Choices[0].NextNodeID); return; }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int currentSum = 0;

            for (int i = 0; i < rndData.Choices.Count; i++)
            {
                currentSum += finalWeights[i];
                if (roll < currentSum) { PlayNode(rndData.Choices[i].NextNodeID); return; }
            }

            PlayNode(rndData.Choices.Last().NextNodeID);
        }

        private void ProcessVariables(VariableNodeData varData)
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");

            foreach (var v in varData.Variables)
            {
                var def = settings?.Variables.FirstOrDefault(x => x.Name == v.VariableName);
                if (def == null) continue;

                if (v.VarOperation == EVarOperation.Set)
                {
                    if (def.Type == EVarType.Integer) NovellaVariables.SetInt(v.VariableName, v.VarValue);
                    else if (def.Type == EVarType.Boolean) NovellaVariables.SetBool(v.VariableName, v.VarBool);
                    else if (def.Type == EVarType.String) NovellaVariables.SetString(v.VariableName, v.VarString);
                }
                else if (v.VarOperation == EVarOperation.Add && def.Type == EVarType.Integer)
                {
                    int current = NovellaVariables.GetInt(v.VariableName);
                    NovellaVariables.SetInt(v.VariableName, current + v.VarValue);
                }
            }
            PlayNode(varData.NextNodeID);
        }

        private void ProcessEndNode(EndNodeData endData)
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            if (StoryTree != null)
            {
                PlayerPrefs.SetInt($"NovellaSave_{StoryTree.name}_Completed", 1);
                PlayerPrefs.Save();
                Debug.Log($"[NovellaPlayer] История {StoryTree.name} завершена!");
            }

            if (endData.EndAction == EEndAction.QuitGame) Application.Quit();
            else if (endData.EndAction == EEndAction.ReturnToMainMenu)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
            }
            else if (endData.EndAction == EEndAction.LoadNextChapter && endData.NextChapter != null) PlayTree(endData.NextChapter);
            else if (endData.EndAction == EEndAction.LoadSpecificScene && !string.IsNullOrEmpty(endData.TargetSceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(endData.TargetSceneName);
            }
        }

        private void ProcessDialogueLine()
        {
            if (_currentLineIndex >= _currentDialogue.DialogueLines.Count)
            {
                ProcessSyncedAudio(EAudioTriggerType.OnDialogueEnd, -1);
                ProcessSyncedAnim(EAudioTriggerType.OnDialogueEnd, -1);
                ProcessSyncedSceneSettings(EAudioTriggerType.OnDialogueEnd, -1);
                PlayNode(_currentDialogue.NextNodeID);
                return;
            }

            var line = _currentDialogue.DialogueLines[_currentLineIndex];

            SyncCharactersInScene(_currentDialogue, line);

            ProcessSyncedAudio(EAudioTriggerType.OnStart, _currentLineIndex);
            ProcessSyncedAudio(EAudioTriggerType.TimeDelay, _currentLineIndex);

            ProcessSyncedAnim(EAudioTriggerType.OnStart, _currentLineIndex);
            ProcessSyncedAnim(EAudioTriggerType.TimeDelay, _currentLineIndex);

            ProcessSyncedSceneSettings(EAudioTriggerType.OnStart, _currentLineIndex);
            ProcessSyncedSceneSettings(EAudioTriggerType.TimeDelay, _currentLineIndex);

            if (_isFastForwarding)
            {
                ShowLineData(line);
            }
            else if (line.DelayBefore > 0f) StartCoroutine(WaitAndShowLine(line));
            else ShowLineData(line);
        }

        private void ProcessStandaloneAudio(AudioNodeData audData)
        {
            if (!audData.SyncWithDialogue && !_isFastForwarding)
            {
                if (audData.AudioAction == EAudioAction.Play && audData.AudioAsset != null) _poolManager.PlayAudio(audData.AudioAsset, audData.AudioVolume, audData.AudioChannel, audData.AudioChannel == EAudioChannel.BGM);
                else if (audData.AudioAction == EAudioAction.Stop) _poolManager.StopAudio(audData.AudioChannel);
            }
            PlayNode(audData.NextNodeID);
        }

        private void ProcessSyncedAudio(EAudioTriggerType trigger, int lineIndex)
        {
            if (_isFastForwarding || _currentDialogue == null || string.IsNullOrEmpty(_currentDialogue.AudioSyncNodeID)) return;
            var audioNode = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == _currentDialogue.AudioSyncNodeID) as AudioNodeData;
            if (audioNode == null) return;

            foreach (var ev in audioNode.AudioEvents)
            {
                if (ev.TriggerType == trigger && (trigger == EAudioTriggerType.OnDialogueEnd || ev.LineIndex == lineIndex))
                {
                    if (trigger == EAudioTriggerType.TimeDelay) StartCoroutine(PlayAudioDelayed(ev));
                    else
                    {
                        if (ev.AudioAction == EAudioAction.Play) _poolManager.PlayAudio(ev.AudioAsset, ev.Volume, ev.AudioChannel, ev.AudioChannel == EAudioChannel.BGM);
                        else if (ev.AudioAction == EAudioAction.Stop) _poolManager.StopAudio(ev.AudioChannel);
                    }
                }
            }
        }

        private void Update()
        {
            if (StoryTree != null && StoryTree.EnableAutoSave && !_isFastForwarding)
            {
                _autoSaveTimer += Time.deltaTime;
                if (_autoSaveTimer >= StoryTree.AutoSaveInterval)
                {
                    _autoSaveTimer = 0f;
                    SaveProgress();
                }
            }

            bool advance = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) advance = true;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) advance = true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) advance = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                SaveProgress(); Debug.Log("[NovellaEngine] QuickSave!");
            }
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                if (PlayerPrefs.HasKey($"NovellaSave_{StoryTree.name}_Node"))
                {
                    PlayerPrefs.SetString("LoadTargetNodeID", PlayerPrefs.GetString($"NovellaSave_{StoryTree.name}_Node"));
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    Debug.Log("[NovellaEngine] QuickLoad!");
                }
            }
#endif

#else
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) advance = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F5)) 
            { 
                SaveProgress(); Debug.Log("[NovellaEngine] QuickSave!"); 
            }
            if (Input.GetKeyDown(KeyCode.F9)) 
            { 
                if (PlayerPrefs.HasKey($"NovellaSave_{StoryTree.name}_Node")) {
                    PlayerPrefs.SetString("LoadTargetNodeID", PlayerPrefs.GetString($"NovellaSave_{StoryTree.name}_Node"));
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    Debug.Log("[NovellaEngine] QuickLoad!");
                }
            }
#endif

#endif

            if (advance)
            {
                if (_isWaitNodeActive)
                {
                    if (_currentWaitNode != null)
                    {
                        if (_currentWaitNode.WaitMode == EWaitMode.UserClick)
                        {
                            _isWaitNodeActive = false; HideClickIndicator(); PlayNode(_currentWaitNode.NextNodeID);
                        }
                        else if (_currentWaitNode.WaitMode == EWaitMode.Time && _currentWaitNode.WaitIsSkippable)
                        {
                            _isWaitNodeActive = false; HideClickIndicator();
                        }
                    }
                }
                else if (_isTyping)
                {
                    if (_typewriterCoroutine != null) StopCoroutine(_typewriterCoroutine);
                    if (DialogueBodyText != null) DialogueBodyText.maxVisibleCharacters = int.MaxValue;
                    _isTyping = false; _isWaitingForClick = true;
                }
                else if (_isWaitingForClick)
                {
                    _isWaitingForClick = false;
                    ProcessSyncedAudio(EAudioTriggerType.OnEnd, _currentLineIndex);
                    ProcessSyncedAnim(EAudioTriggerType.OnEnd, _currentLineIndex);
                    ProcessSyncedSceneSettings(EAudioTriggerType.OnEnd, _currentLineIndex);
                    _currentLineIndex++;
                    ProcessDialogueLine();
                }
            }
        }

        private IEnumerator WaitAndShowLine(DialogueLine line)
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);
            yield return new WaitForSeconds(line.DelayBefore);
            ShowLineData(line);
        }

        private void ShowLineData(DialogueLine line)
        {
            if (line.OverrideDialogueFrame != null)
            {
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
                Transform parent = _defaultDialoguePanel != null ? _defaultDialoguePanel.transform.parent : transform;
                GameObject requestedFrame = _poolManager.GetCustomUIFrame(line.OverrideDialogueFrame, parent);

                if (_currentCustomFrame != null && _currentCustomFrame != requestedFrame) _currentCustomFrame.SetActive(false);

                _currentCustomFrame = requestedFrame; _currentCustomFrame.SetActive(true);

                var customUI = _currentCustomFrame.GetComponent<NovellaCustomUI>();
                if (customUI != null) { SpeakerNameText = customUI.OverrideSpeakerName; DialogueBodyText = customUI.OverrideDialogueText; }
            }
            else
            {
                if (_currentCustomFrame != null) { _currentCustomFrame.SetActive(false); _currentCustomFrame = null; }
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(true);
                SpeakerNameText = _defaultSpeakerNameText; DialogueBodyText = _defaultDialogueBodyText;
            }

            // UI Binding overrides — drag&drop'нутые в нодe сцены-цели имеют
            // приоритет над DialoguePanel/CustomFrame: текст пишется прямо туда.
            if (!string.IsNullOrEmpty(line.UITextTargetId))
            {
                var t = NovellaUIBinding.FindText(line.UITextTargetId);
                if (t != null) DialogueBodyText = t;
            }
            if (!string.IsNullOrEmpty(line.UISpeakerTargetId))
            {
                var t = NovellaUIBinding.FindText(line.UISpeakerTargetId);
                if (t != null) SpeakerNameText = t;
            }

            RectTransform activeRect = null;
            if (line.OverrideDialogueFrame != null && _currentCustomFrame != null) activeRect = _currentCustomFrame.GetComponent<RectTransform>();
            else if (_defaultDialoguePanel != null) activeRect = _defaultDialoguePanel.GetComponent<RectTransform>();

            if (activeRect != null)
            {
                if (line.CustomizeFrameLayout)
                {
                    float targetY = 0f; float targetX = 0f;
                    if (line.FramePositionPreset == EFramePosition.Top) targetY = 600f;
                    else if (line.FramePositionPreset == EFramePosition.Center) targetY = 300f;
                    else if (line.FramePositionPreset == EFramePosition.Custom) { targetX = line.FramePosX; targetY = line.FramePosY; }

                    activeRect.anchoredPosition = new Vector2(targetX, targetY);
                    activeRect.localScale = Vector3.one * line.FrameScale;
                }
                else { activeRect.anchoredPosition = Vector2.zero; activeRect.localScale = Vector3.one; }
            }

            if (SpeakerNameText != null)
            {
                if (line.Speaker != null)
                {
                    if (!line.HideSpeakerName)
                    {
                        SpeakerNameText.gameObject.SetActive(true);

                        string displayName = "";
                        if (line.Speaker.IsPlayerCharacter && StoryTree != null)
                        {
                            displayName = PlayerPrefs.GetString($"NovellaSave_{StoryTree.name}_MCName", "Alex");
                        }
                        else
                        {
                            displayName = CurrentLanguage == "RU" ? line.Speaker.DisplayName_RU : line.Speaker.DisplayName_EN;
                            if (string.IsNullOrEmpty(displayName)) displayName = line.Speaker.name;
                        }

                        SpeakerNameText.text = displayName;
                        SpeakerNameText.color = line.Speaker.ThemeColor;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(line.CustomName)) { SpeakerNameText.text = ""; SpeakerNameText.gameObject.SetActive(false); }
                        else { SpeakerNameText.gameObject.SetActive(true); SpeakerNameText.text = line.CustomName; SpeakerNameText.color = line.CustomNameColor; }
                    }
                }
                else { SpeakerNameText.text = ""; SpeakerNameText.gameObject.SetActive(false); }
            }

            if (DialogueBodyText != null)
            {
                DialogueBodyText.fontSize = line.FontSize > 0 ? line.FontSize : 32;
                string localizedText = line.LocalizedPhrase.GetText(CurrentLanguage);

                if (line.UseTypewriter && !_isFastForwarding)
                {
                    if (_typewriterCoroutine != null) StopCoroutine(_typewriterCoroutine);
                    _typewriterCoroutine = StartCoroutine(TypewriterRoutine(line, localizedText));
                }
                else
                {
                    DialogueBodyText.text = localizedText; DialogueBodyText.maxVisibleCharacters = 99999;
                    _isTyping = false; _isWaitingForClick = true;
                }
            }
            else { _isTyping = false; _isWaitingForClick = true; }
        }

        private IEnumerator TypewriterRoutine(DialogueLine line, string fullText)
        {
            _isTyping = true; _isWaitingForClick = false;
            DialogueBodyText.text = fullText; DialogueBodyText.maxVisibleCharacters = 0;
            DialogueBodyText.ForceMeshUpdate();
            int totalVisibleChars = DialogueBodyText.textInfo.characterCount;
            float timer = 0f; int currentVisibleCount = 0;

            while (currentVisibleCount < totalVisibleChars)
            {
                float currentSpeed = line.BaseSpeed;
                if (line.UseCustomPacing && totalVisibleChars > 1)
                {
                    float progress = (float)currentVisibleCount / (totalVisibleChars - 1);
                    float multiplier = line.PacingCurve.Evaluate(progress);
                    currentSpeed *= Mathf.Max(0.1f, multiplier);
                }

                timer += Time.deltaTime;
                float timePerChar = 1f / currentSpeed;

                while (timer >= timePerChar && currentVisibleCount < totalVisibleChars) { currentVisibleCount++; timer -= timePerChar; }
                DialogueBodyText.maxVisibleCharacters = currentVisibleCount;
                yield return null;
            }

            DialogueBodyText.maxVisibleCharacters = int.MaxValue;
            _isTyping = false; _isWaitingForClick = true;
        }

        private void SyncCharactersInScene(DialogueNodeData dialData, DialogueLine currentLine)
        {
            if (CharactersContainer == null) return;
            var entities = CharactersContainer.GetComponentsInChildren<NovellaSceneEntity>(true).ToList();
            Dictionary<string, CharacterInDialogue> charConfigs = new Dictionary<string, CharacterInDialogue>();

            foreach (var ac in dialData.ActiveCharacters) if (ac.CharacterAsset != null) charConfigs[ac.CharacterAsset.CharacterID] = ac;

            foreach (var line in dialData.DialogueLines)
            {
                if (line.Speaker != null && !charConfigs.ContainsKey(line.Speaker.CharacterID))
                {
                    charConfigs[line.Speaker.CharacterID] = new CharacterInDialogue { CharacterAsset = line.Speaker, Plane = ECharacterPlane.Speaker, Scale = 1.0f, Emotion = "Default", PosX = 0f, PosY = 0f, PositionPreset = ECharacterPosition.Center, FlipX = false, FlipY = false };
                }
            }

            foreach (var config in charConfigs.Values)
            {
                var entity = entities.FirstOrDefault(e => e.LinkedNodeID == config.CharacterAsset.CharacterID);
                if (entity == null)
                {
                    GameObject go = new GameObject("Char_" + config.CharacterAsset.name);
                    go.transform.SetParent(CharactersContainer, false);
                    entity = go.AddComponent<NovellaSceneEntity>();
                    entity.Initialize(config.CharacterAsset.CharacterID);
                    entities.Add(entity);
                }

                string emotionToSet = config.Emotion;
                float baseX = 0f;
                if (config.PositionPreset == ECharacterPosition.Left) baseX = -3.5f;
                else if (config.PositionPreset == ECharacterPosition.Right) baseX = 3.5f;
                else if (config.PositionPreset == ECharacterPosition.FarLeft) baseX = -6.5f;
                else if (config.PositionPreset == ECharacterPosition.FarRight) baseX = 6.5f;
                else if (config.PositionPreset == ECharacterPosition.Custom) baseX = config.PosX;

                int targetPlane = (int)config.Plane;
                float targetScale = config.Scale;
                Vector3 targetPos = new Vector3(baseX, config.PosY, 0);
                bool targetFlipX = config.FlipX;
                bool targetFlipY = config.FlipY;

                if (currentLine != null && currentLine.Speaker != null && config.CharacterAsset.CharacterID == currentLine.Speaker.CharacterID)
                {
                    emotionToSet = currentLine.Mood;
                    targetFlipX = config.FlipX ^ currentLine.FlipX;
                    targetFlipY = config.FlipY ^ currentLine.FlipY;

                    if (currentLine.CustomizeSpeakerLayout)
                    {
                        targetPlane = (int)currentLine.SpeakerPlane;
                        targetScale = config.Scale * currentLine.SpeakerScale;
                        float activeX = 0f;
                        if (currentLine.SpeakerPositionPreset == ECharacterPosition.Left) activeX = -3.5f;
                        else if (currentLine.SpeakerPositionPreset == ECharacterPosition.Right) activeX = 3.5f;
                        else if (currentLine.SpeakerPositionPreset == ECharacterPosition.FarLeft) activeX = -6.5f;
                        else if (currentLine.SpeakerPositionPreset == ECharacterPosition.FarRight) activeX = 6.5f;
                        else if (currentLine.SpeakerPositionPreset == ECharacterPosition.Custom) activeX = currentLine.SpeakerPosX;

                        targetPos = new Vector3(baseX + activeX, config.PosY + currentLine.SpeakerPosY, 0);
                    }
                    else
                    {
                        targetPlane = (int)ECharacterPlane.Speaker;
                    }
                }

                entity.transform.localScale = Vector3.one * targetScale;
                entity.transform.localPosition = targetPos;

                entity.ApplyAppearance(config.CharacterAsset, emotionToSet);
                entity.SetSortingOrder(targetPlane);
                entity.SetFlip(targetFlipX, targetFlipY);

                bool shouldHide = false;
                if (currentLine != null && currentLine.HideSpeakerSprite && currentLine.Speaker != null && config.CharacterAsset.CharacterID == currentLine.Speaker.CharacterID) shouldHide = true;
                entity.gameObject.SetActive(!shouldHide);
            }

            foreach (var entity in entities) if (!charConfigs.ContainsKey(entity.LinkedNodeID)) entity.gameObject.SetActive(false);
        }

        private void ShowChoices(BranchNodeData branchData)
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            // Сначала чистим всё что могло остаться от предыдущего Branch:
            // спавнённые кнопки в ChoiceContainer + listener'ы на binding-кнопках.
            if (ChoiceContainer != null)
                foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);
            ClearChoiceBindingListeners();

            GameObject prefabToUse = branchData.OverrideChoiceButtonPrefab != null ? branchData.OverrideChoiceButtonPrefab : ChoiceButtonPrefab;

            foreach (var choice in branchData.Choices)
            {
                if (!CheckConditions(choice.Conditions)) continue;

                // Если на choice есть UIButtonTargetId — используем существующую
                // в сцене кнопку (главное меню/инвентарь). Префаб не спавним.
                if (!string.IsNullOrEmpty(choice.UIButtonTargetId))
                {
                    var sceneBtn = NovellaUIBinding.FindButton(choice.UIButtonTargetId);
                    if (sceneBtn != null)
                    {
                        var lblTmp = sceneBtn.GetComponentInChildren<TMP_Text>();
                        if (lblTmp != null) lblTmp.text = choice.LocalizedText.GetText(CurrentLanguage);

                        string nextId = choice.NextNodeID;
                        UnityEngine.Events.UnityAction handler = () =>
                        {
                            ClearChoiceBindingListeners();
                            if (ChoiceContainer != null)
                                foreach (Transform c in ChoiceContainer) Destroy(c.gameObject);
                            PlayNode(nextId);
                        };
                        sceneBtn.onClick.AddListener(handler);
                        _choiceBindingListeners.Add((sceneBtn, handler));
                        continue;
                    }
                    // Если binding не найден — fallback на обычный спавн.
                }

                if (prefabToUse == null || ChoiceContainer == null) continue;

                GameObject btnGO = Instantiate(prefabToUse, ChoiceContainer);
                var tmpText = btnGO.GetComponentInChildren<TMP_Text>();
                if (tmpText != null) tmpText.text = choice.LocalizedText.GetText(CurrentLanguage);

                var button = btnGO.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.AddListener(() => {
                        if (ChoiceContainer != null)
                            foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);
                        ClearChoiceBindingListeners();
                        PlayNode(choice.NextNodeID);
                    });
                }
            }
        }

        private void ClearChoiceBindingListeners()
        {
            foreach (var pair in _choiceBindingListeners)
            {
                if (pair.btn != null) pair.btn.onClick.RemoveListener(pair.handler);
            }
            _choiceBindingListeners.Clear();
        }

        private void ClearCharacters()
        {
            if (CharactersContainer != null)
            {
                for (int i = CharactersContainer.childCount - 1; i >= 0; i--) Destroy(CharactersContainer.GetChild(i).gameObject);
                CharactersContainer.DetachChildren();
            }
        }

        private bool CheckConditions(List<ChoiceCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var cond in conditions) if (!CheckSingleCondition(cond.Variable, cond.Operator, cond.Value, cond.ValueBool, cond.ValueString)) return false;
            return true;
        }

        private bool CheckSingleCondition(string varName, EConditionOperator op, int targetInt, bool targetBool, string targetString)
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            var def = settings?.Variables.FirstOrDefault(x => x.Name == varName);
            if (def == null) return false;

            if (def.Type == EVarType.Integer)
            {
                int current = NovellaVariables.GetInt(varName);
                switch (op)
                {
                    case EConditionOperator.Equal: return current == targetInt;
                    case EConditionOperator.NotEqual: return current != targetInt;
                    case EConditionOperator.Greater: return current > targetInt;
                    case EConditionOperator.Less: return current < targetInt;
                    case EConditionOperator.GreaterOrEqual: return current >= targetInt;
                    case EConditionOperator.LessOrEqual: return current <= targetInt;
                }
            }
            else if (def.Type == EVarType.Boolean)
            {
                bool current = NovellaVariables.GetBool(varName);
                if (op == EConditionOperator.Equal) return current == targetBool;
                if (op == EConditionOperator.NotEqual) return current != targetBool;
            }
            else if (def.Type == EVarType.String)
            {
                string current = NovellaVariables.GetString(varName);
                if (op == EConditionOperator.Equal) return current == targetString;
                if (op == EConditionOperator.NotEqual) return current != targetString;
            }
            return false;
        }

        private IEnumerator PlayAudioDelayed(DialogueAudioEvent ev)
        {
            yield return new WaitForSeconds(ev.TimeDelay);
            if (ev.AudioAction == EAudioAction.Play) _poolManager.PlayAudio(ev.AudioAsset, ev.Volume, ev.AudioChannel, ev.AudioChannel == EAudioChannel.BGM);
            else if (ev.AudioAction == EAudioAction.Stop) _poolManager.StopAudio(ev.AudioChannel);
        }
    }
}