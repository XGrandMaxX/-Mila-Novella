using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;
using System.Linq;
using System;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovellaEngine.Runtime
{
    public class NovellaPlayer : MonoBehaviour
    {
        [Header("Story Data")]
        public NovellaTree StoryTree;
        public string CurrentLanguage = "RU";

        [Header("UI Elements")]
        public GameObject DialoguePanel;
        public TMP_Text SpeakerNameText;
        public TMP_Text DialogueBodyText;

        [Header("Choices UI")]
        public Transform ChoiceContainer;
        public GameObject ChoiceButtonPrefab;

        [Header("Scene Elements")]
        public Transform CharactersContainer;

        public static Dictionary<string, int> IntVars = new Dictionary<string, int>();
        public static Dictionary<string, bool> BoolVars = new Dictionary<string, bool>();
        public static Dictionary<string, string> StringVars = new Dictionary<string, string>();

        private NovellaPoolManager _poolManager;
        private NovellaNodeData _currentNode;
        private int _currentLineIndex = 0;

        private bool _isWaitingForClick = false;
        private bool _isTyping = false;
        private Coroutine _typewriterCoroutine;

        private const int SECURE_XOR_KEY = 777;

        private GameObject _defaultDialoguePanel;
        private TMP_Text _defaultSpeakerNameText;
        private TMP_Text _defaultDialogueBodyText;

        private GameObject _currentCustomFrame; // Ссылка на активную в данный момент кастомную рамку

        private void Start()
        {
            if (FindAnyObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();

            _poolManager = gameObject.GetComponent<NovellaPoolManager>();
            if (_poolManager == null) _poolManager = gameObject.AddComponent<NovellaPoolManager>();
            _poolManager.InitializePools();

            // Сохраняем дефолтные элементы UI
            _defaultDialoguePanel = DialoguePanel;
            _defaultSpeakerNameText = SpeakerNameText;
            _defaultDialogueBodyText = DialogueBodyText;

            InitializeVariables();

            string chapterName = PlayerPrefs.GetString("SelectedChapterPath", "");
            if (!string.IsNullOrEmpty(chapterName))
            {
                PlayerPrefs.SetString("SelectedChapterPath", "");
                NovellaTree externalTree = Resources.Load<NovellaTree>("Chapters/" + chapterName);
                if (externalTree != null)
                {
                    PlayTree(externalTree);
                    return;
                }
                else Debug.LogError($"[Novella Engine] Chapter '{chapterName}' not found in Resources/Chapters/ !");
            }

            if (StoryTree != null) PlayTree(StoryTree);
            else Debug.LogWarning("[NovellaPlayer] Story Tree is not assigned!");
        }

        private void InitializeVariables()
        {
            IntVars.Clear();
            BoolVars.Clear();
            StringVars.Clear();

            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings == null) return;

            foreach (var v in settings.Variables)
            {
                if (v.Scope == EVarScope.Global)
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = GetGlobalInt(v.Name, v.DefaultInt, v.IsPremiumCurrency);
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = PlayerPrefs.GetInt("NV_" + v.Name, v.DefaultBool ? 1 : 0) == 1;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = PlayerPrefs.GetString("NV_" + v.Name, v.DefaultString);
                }
                else
                {
                    if (v.Type == EVarType.Integer) IntVars[v.Name] = v.DefaultInt;
                    else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = v.DefaultBool;
                    else if (v.Type == EVarType.String) StringVars[v.Name] = v.DefaultString;
                }
            }
        }

        private int GetGlobalInt(string key, int defaultValue, bool isPremium)
        {
            if (isPremium)
            {
                string secureKey = "NV_SEC_" + key;
                if (!PlayerPrefs.HasKey(secureKey)) return defaultValue;

                try
                {
                    string base64 = PlayerPrefs.GetString(secureKey);
                    byte[] bytes = Convert.FromBase64String(base64);
                    string xoredString = System.Text.Encoding.UTF8.GetString(bytes);
                    if (int.TryParse(xoredString, out int xoredValue))
                    {
                        return xoredValue ^ SECURE_XOR_KEY;
                    }
                }
                catch { return defaultValue; }
                return defaultValue;
            }
            else
            {
                return PlayerPrefs.GetInt("NV_" + key, defaultValue);
            }
        }

        private void SetGlobalInt(string key, int value, bool isPremium)
        {
            if (isPremium)
            {
                string xoredString = (value ^ SECURE_XOR_KEY).ToString();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(xoredString);
                string base64 = Convert.ToBase64String(bytes);
                PlayerPrefs.SetString("NV_SEC_" + key, base64);
            }
            else
            {
                PlayerPrefs.SetInt("NV_" + key, value);
            }
            PlayerPrefs.Save();
        }

        public void PlayTree(NovellaTree tree)
        {
            StoryTree = tree;

            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            if (settings != null)
            {
                foreach (var v in settings.Variables)
                {
                    if (v.Scope == EVarScope.Local)
                    {
                        if (v.Type == EVarType.Integer) IntVars[v.Name] = v.DefaultInt;
                        else if (v.Type == EVarType.Boolean) BoolVars[v.Name] = v.DefaultBool;
                        else if (v.Type == EVarType.String) StringVars[v.Name] = v.DefaultString;
                    }
                }
            }

            ClearCharacters();
            PlayNode(tree.RootNodeID);
        }

        private void PlayNode(string nodeID)
        {
            if (string.IsNullOrEmpty(nodeID))
            {
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
                if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);
                return;
            }

            _currentNode = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == nodeID);
            if (_currentNode == null) return;

            switch (_currentNode.NodeType)
            {
                case ENodeType.Dialogue:
                case ENodeType.Event:
                    _currentLineIndex = 0;
                    ProcessDialogueLine();
                    break;
                case ENodeType.Branch:
                    ShowChoices();
                    break;
                case ENodeType.Condition:
                    ProcessConditionNode();
                    break;
                case ENodeType.Random:
                    ProcessRandomNode();
                    break;
                case ENodeType.Audio:
                    ProcessStandaloneAudio();
                    break;
                case ENodeType.Variable:
                    ProcessVariables();
                    break;
                case ENodeType.End:
                    ProcessEndNode();
                    break;
            }
        }

        private void ProcessConditionNode()
        {
            bool isTrue = CheckConditions(_currentNode.Conditions);

            if (isTrue && _currentNode.Choices.Count > 0)
            {
                PlayNode(_currentNode.Choices[0].NextNodeID);
            }
            else if (!isTrue && _currentNode.Choices.Count > 1)
            {
                PlayNode(_currentNode.Choices[1].NextNodeID);
            }
            else
            {
                PlayNode("");
            }
        }

        private void ProcessRandomNode()
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            if (_currentNode.Choices == null || _currentNode.Choices.Count == 0)
            {
                PlayNode("");
                return;
            }

            int totalWeight = 0;
            List<int> finalWeights = new List<int>();

            foreach (var choice in _currentNode.Choices)
            {
                int w = choice.ChanceWeight;
                foreach (var mod in choice.ChanceModifiers)
                {
                    if (CheckSingleCondition(mod.Variable, mod.Operator, mod.Value, mod.ValueBool, mod.ValueString))
                    {
                        w += mod.BonusWeight;
                    }
                }

                w = Mathf.Max(0, w);
                finalWeights.Add(w);
                totalWeight += w;
            }

            if (totalWeight <= 0)
            {
                PlayNode(_currentNode.Choices[0].NextNodeID);
                return;
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int currentSum = 0;

            for (int i = 0; i < _currentNode.Choices.Count; i++)
            {
                currentSum += finalWeights[i];
                if (roll < currentSum)
                {
                    PlayNode(_currentNode.Choices[i].NextNodeID);
                    return;
                }
            }

            PlayNode(_currentNode.Choices.Last().NextNodeID);
        }

        private void Update()
        {
            bool advance = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) advance = true;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) advance = true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) advance = true;
#else
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) advance = true;
#endif

            if (advance)
            {
                if (_isTyping)
                {
                    if (_typewriterCoroutine != null) StopCoroutine(_typewriterCoroutine);

                    if (DialogueBodyText != null) DialogueBodyText.maxVisibleCharacters = int.MaxValue;
                    _isTyping = false;
                    _isWaitingForClick = true;
                }
                else if (_isWaitingForClick)
                {
                    _isWaitingForClick = false;
                    ProcessSyncedAudio(EAudioTriggerType.OnEnd, _currentLineIndex);
                    _currentLineIndex++;
                    ProcessDialogueLine();
                }
            }
        }

        private void ProcessDialogueLine()
        {
            if (_currentLineIndex >= _currentNode.DialogueLines.Count)
            {
                ProcessSyncedAudio(EAudioTriggerType.OnDialogueEnd, -1);
                PlayNode(_currentNode.NextNodeID);
                return;
            }

            var line = _currentNode.DialogueLines[_currentLineIndex];

            SyncCharactersInScene(_currentNode, line);

            ProcessSyncedAudio(EAudioTriggerType.OnStart, _currentLineIndex);
            ProcessSyncedAudio(EAudioTriggerType.TimeDelay, _currentLineIndex);

            if (line.DelayBefore > 0f)
            {
                StartCoroutine(WaitAndShowLine(line));
            }
            else
            {
                ShowLineData(line);
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
            // === ПОДМЕНА РАМКИ ЧЕРЕЗ POOL MANAGER ===
            if (line.OverrideDialogueFrame != null)
            {
                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);

                Transform parent = _defaultDialoguePanel != null ? _defaultDialoguePanel.transform.parent : transform;

                // Запрашиваем рамку из пула
                GameObject requestedFrame = _poolManager.GetCustomUIFrame(line.OverrideDialogueFrame, parent);

                // Если нужно сменить текущую кастомную на другую кастомную
                if (_currentCustomFrame != null && _currentCustomFrame != requestedFrame)
                {
                    _currentCustomFrame.SetActive(false);
                }

                _currentCustomFrame = requestedFrame;
                _currentCustomFrame.SetActive(true);

                var customUI = _currentCustomFrame.GetComponent<NovellaCustomUI>();
                if (customUI != null)
                {
                    SpeakerNameText = customUI.OverrideSpeakerName;
                    DialogueBodyText = customUI.OverrideDialogueText;
                }
            }
            else
            {
                if (_currentCustomFrame != null)
                {
                    _currentCustomFrame.SetActive(false);
                    _currentCustomFrame = null;
                }

                if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(true);

                SpeakerNameText = _defaultSpeakerNameText;
                DialogueBodyText = _defaultDialogueBodyText;
            }

            if (SpeakerNameText != null)
            {
                if (line.Speaker != null)
                {
                    if (!line.HideSpeakerName)
                    {
                        SpeakerNameText.gameObject.SetActive(true);
                        string displayName = CurrentLanguage == "RU" ? line.Speaker.DisplayName_RU : line.Speaker.DisplayName_EN;
                        if (string.IsNullOrEmpty(displayName)) displayName = line.Speaker.name;

                        SpeakerNameText.text = displayName;
                        SpeakerNameText.color = line.Speaker.ThemeColor;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(line.CustomName))
                        {
                            SpeakerNameText.text = "";
                            SpeakerNameText.gameObject.SetActive(false);
                        }
                        else
                        {
                            SpeakerNameText.gameObject.SetActive(true);
                            SpeakerNameText.text = line.CustomName;
                            SpeakerNameText.color = line.CustomNameColor;
                        }
                    }
                }
                else
                {
                    SpeakerNameText.text = "";
                    SpeakerNameText.gameObject.SetActive(false);
                }
            }

            if (DialogueBodyText != null)
            {
                DialogueBodyText.fontSize = line.FontSize > 0 ? line.FontSize : 32;
                string localizedText = line.LocalizedPhrase.GetText(CurrentLanguage);

                if (line.UseTypewriter)
                {
                    if (_typewriterCoroutine != null) StopCoroutine(_typewriterCoroutine);
                    _typewriterCoroutine = StartCoroutine(TypewriterRoutine(line, localizedText));
                }
                else
                {
                    DialogueBodyText.text = localizedText;
                    DialogueBodyText.maxVisibleCharacters = 99999;
                    _isTyping = false;
                    _isWaitingForClick = true;
                }
            }
            else
            {
                _isTyping = false;
                _isWaitingForClick = true;
            }
        }

        private void SyncCharactersInScene(NovellaNodeData nodeData, DialogueLine currentLine)
        {
            if (CharactersContainer == null) return;

            var entities = CharactersContainer.GetComponentsInChildren<NovellaSceneEntity>(true).ToList();

            Dictionary<string, CharacterInDialogue> charConfigs = new Dictionary<string, CharacterInDialogue>();

            foreach (var ac in nodeData.ActiveCharacters)
            {
                if (ac.CharacterAsset != null) charConfigs[ac.CharacterAsset.CharacterID] = ac;
            }

            foreach (var line in nodeData.DialogueLines)
            {
                if (line.Speaker != null && !charConfigs.ContainsKey(line.Speaker.CharacterID))
                {
                    charConfigs[line.Speaker.CharacterID] = new CharacterInDialogue
                    {
                        CharacterAsset = line.Speaker,
                        Plane = ECharacterPlane.Speaker,
                        Scale = 1.0f,
                        Emotion = "Default",
                        PosX = 0f,
                        PosY = 0f
                    };
                }
            }

            foreach (var config in charConfigs.Values)
            {
                var entity = entities.FirstOrDefault(e => e.LinkedNodeID == config.CharacterAsset.CharacterID);
                if (entity == null)
                {
                    GameObject go = new GameObject("Char_" + config.CharacterAsset.name);
                    go.transform.SetParent(CharactersContainer, false);
                    var sr = go.AddComponent<SpriteRenderer>();
                    entity = go.AddComponent<NovellaSceneEntity>();
                    entity.Initialize(config.CharacterAsset.CharacterID);
                }

                var renderer = entity.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Sprite targetSprite = config.CharacterAsset.DefaultSprite;
                    string emotionToSet = config.Emotion;

                    if (currentLine != null && currentLine.Speaker != null && config.CharacterAsset.CharacterID == currentLine.Speaker.CharacterID)
                    {
                        emotionToSet = currentLine.Mood;

                        if (currentLine.CustomizeSpeakerLayout)
                        {
                            renderer.sortingOrder = (int)currentLine.SpeakerPlane;
                            entity.transform.localScale = Vector3.one * currentLine.SpeakerScale;
                            entity.transform.localPosition = new Vector3(currentLine.SpeakerPosX, currentLine.SpeakerPosY, 0);
                        }
                        else
                        {
                            renderer.sortingOrder = (int)ECharacterPlane.Speaker;
                            entity.transform.localScale = Vector3.one * config.Scale;
                            entity.transform.localPosition = new Vector3(config.PosX, config.PosY, 0);
                        }
                    }
                    else
                    {
                        renderer.sortingOrder = (int)config.Plane;
                        entity.transform.localScale = Vector3.one * config.Scale;
                        entity.transform.localPosition = new Vector3(config.PosX, config.PosY, 0);
                    }

                    if (emotionToSet != "Default")
                    {
                        var emotionData = config.CharacterAsset.Emotions.FirstOrDefault(e => e.EmotionName == emotionToSet);
                        if (emotionData.EmotionSprite != null) targetSprite = emotionData.EmotionSprite;
                    }

                    renderer.sprite = targetSprite;
                }

                bool shouldHide = false;
                if (currentLine != null && currentLine.HideSpeakerSprite && currentLine.Speaker != null && config.CharacterAsset.CharacterID == currentLine.Speaker.CharacterID)
                {
                    shouldHide = true;
                }

                entity.gameObject.SetActive(!shouldHide);
            }

            foreach (var entity in entities)
            {
                if (!charConfigs.ContainsKey(entity.LinkedNodeID))
                {
                    entity.gameObject.SetActive(false);
                }
            }
        }

        private IEnumerator TypewriterRoutine(DialogueLine line, string fullText)
        {
            _isTyping = true;
            _isWaitingForClick = false;

            DialogueBodyText.text = fullText;
            DialogueBodyText.maxVisibleCharacters = 0;

            DialogueBodyText.ForceMeshUpdate();
            int totalVisibleChars = DialogueBodyText.textInfo.characterCount;

            float timer = 0f;
            int currentVisibleCount = 0;

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

                while (timer >= timePerChar && currentVisibleCount < totalVisibleChars)
                {
                    currentVisibleCount++;
                    timer -= timePerChar;
                }

                DialogueBodyText.maxVisibleCharacters = currentVisibleCount;
                yield return null;
            }

            DialogueBodyText.maxVisibleCharacters = int.MaxValue;
            _isTyping = false;
            _isWaitingForClick = true;
        }

        private void ProcessSyncedAudio(EAudioTriggerType trigger, int lineIndex)
        {
            if (string.IsNullOrEmpty(_currentNode.AudioSyncNodeID)) return;
            var audioNode = StoryTree.Nodes.FirstOrDefault(n => n.NodeID == _currentNode.AudioSyncNodeID);
            if (audioNode == null || audioNode.NodeType != ENodeType.Audio) return;

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

        private IEnumerator PlayAudioDelayed(DialogueAudioEvent ev)
        {
            yield return new WaitForSeconds(ev.TimeDelay);
            if (ev.AudioAction == EAudioAction.Play) _poolManager.PlayAudio(ev.AudioAsset, ev.Volume, ev.AudioChannel, ev.AudioChannel == EAudioChannel.BGM);
            else if (ev.AudioAction == EAudioAction.Stop) _poolManager.StopAudio(ev.AudioChannel);
        }

        private void ProcessStandaloneAudio()
        {
            if (!_currentNode.SyncWithDialogue)
            {
                if (_currentNode.AudioAction == EAudioAction.Play && _currentNode.AudioAsset != null) _poolManager.PlayAudio(_currentNode.AudioAsset, _currentNode.AudioVolume, _currentNode.AudioChannel, _currentNode.AudioChannel == EAudioChannel.BGM);
                else if (_currentNode.AudioAction == EAudioAction.Stop) _poolManager.StopAudio(_currentNode.AudioChannel);
            }
            PlayNode(_currentNode.NextNodeID);
        }

        private void ShowChoices()
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);

            GameObject prefabToUse = _currentNode.OverrideChoiceButtonPrefab != null ? _currentNode.OverrideChoiceButtonPrefab : ChoiceButtonPrefab;

            foreach (var choice in _currentNode.Choices)
            {
                if (!CheckConditions(choice.Conditions)) continue;

                GameObject btnGO = Instantiate(prefabToUse, ChoiceContainer);
                var tmpText = btnGO.GetComponentInChildren<TMP_Text>();
                if (tmpText != null) tmpText.text = choice.LocalizedText.GetText(CurrentLanguage);

                var button = btnGO.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.AddListener(() => {
                        foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);
                        PlayNode(choice.NextNodeID);
                    });
                }
            }
        }

        private bool CheckConditions(List<ChoiceCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var cond in conditions)
            {
                if (!CheckSingleCondition(cond.Variable, cond.Operator, cond.Value, cond.ValueBool, cond.ValueString)) return false;
            }
            return true;
        }

        private bool CheckSingleCondition(string varName, EConditionOperator op, int targetInt, bool targetBool, string targetString)
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");
            var def = settings?.Variables.FirstOrDefault(x => x.Name == varName);
            if (def == null) return false;

            if (def.Type == EVarType.Integer)
            {
                int current = IntVars.ContainsKey(varName) ? IntVars[varName] : def.DefaultInt;
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
                bool current = BoolVars.ContainsKey(varName) ? BoolVars[varName] : def.DefaultBool;
                if (op == EConditionOperator.Equal) return current == targetBool;
                if (op == EConditionOperator.NotEqual) return current != targetBool;
            }
            else if (def.Type == EVarType.String)
            {
                string current = StringVars.ContainsKey(varName) ? StringVars[varName] : def.DefaultString;
                if (op == EConditionOperator.Equal) return current == targetString;
                if (op == EConditionOperator.NotEqual) return current != targetString;
            }
            return false;
        }

        private void ProcessVariables()
        {
            var settings = Resources.Load<NovellaVariableSettings>("NovellaEngine/NovellaVariableSettings");

            foreach (var v in _currentNode.Variables)
            {
                var def = settings?.Variables.FirstOrDefault(x => x.Name == v.VariableName);
                if (def == null) continue;

                if (def.Type == EVarType.Integer)
                {
                    int current = IntVars.ContainsKey(v.VariableName) ? IntVars[v.VariableName] : def.DefaultInt;

                    if (v.VarOperation == EVarOperation.Set) current = v.VarValue;
                    else if (v.VarOperation == EVarOperation.Add) current += v.VarValue;

                    if (def.HasLimits) current = Mathf.Clamp(current, def.MinValue, def.MaxValue);

                    IntVars[v.VariableName] = current;
                    SetGlobalInt(v.VariableName, current, def.IsPremiumCurrency);
                }
                else if (def.Type == EVarType.Boolean)
                {
                    BoolVars[v.VariableName] = v.VarBool;
                    if (def.Scope == EVarScope.Global)
                    {
                        PlayerPrefs.SetInt("NV_" + v.VariableName, v.VarBool ? 1 : 0);
                        PlayerPrefs.Save();
                    }
                }
                else if (def.Type == EVarType.String)
                {
                    StringVars[v.VariableName] = v.VarString;
                    if (def.Scope == EVarScope.Global)
                    {
                        PlayerPrefs.SetString("NV_" + v.VariableName, v.VarString);
                        PlayerPrefs.Save();
                    }
                }
            }
            PlayNode(_currentNode.NextNodeID);
        }

        private void ProcessEndNode()
        {
            if (_defaultDialoguePanel != null) _defaultDialoguePanel.SetActive(false);
            if (_currentCustomFrame != null) _currentCustomFrame.SetActive(false);

            if (_currentNode.EndAction == EEndAction.QuitGame) Application.Quit();
            else if (_currentNode.EndAction == EEndAction.LoadNextChapter && _currentNode.NextChapter != null) PlayTree(_currentNode.NextChapter);
        }

        private void ClearCharacters()
        {
            if (CharactersContainer != null)
            {
                for (int i = CharactersContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(CharactersContainer.GetChild(i).gameObject);
                }
                CharactersContainer.DetachChildren();
            }
        }
    }
}