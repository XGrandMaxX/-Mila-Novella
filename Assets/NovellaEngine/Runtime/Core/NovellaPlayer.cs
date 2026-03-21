using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;
using System.Linq;

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

        public static Dictionary<string, int> Variables = new Dictionary<string, int>();

        private NovellaPoolManager _poolManager;
        private NovellaNodeData _currentNode;
        private int _currentLineIndex = 0;

        private bool _isWaitingForClick = false;
        private bool _isTyping = false;
        private Coroutine _typewriterCoroutine;

        private void Start()
        {
            if (FindAnyObjectByType<AudioListener>() == null && Camera.main != null)
                Camera.main.gameObject.AddComponent<AudioListener>();

            _poolManager = gameObject.GetComponent<NovellaPoolManager>();
            if (_poolManager == null) _poolManager = gameObject.AddComponent<NovellaPoolManager>();
            _poolManager.InitializePools();

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

        public void PlayTree(NovellaTree tree)
        {
            StoryTree = tree;
            Variables.Clear();
            ClearCharacters();
            PlayNode(tree.RootNodeID);
        }

        private void PlayNode(string nodeID)
        {
            if (string.IsNullOrEmpty(nodeID))
            {
                DialoguePanel.SetActive(false);
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

                    DialogueBodyText.maxVisibleCharacters = int.MaxValue;
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
            DialoguePanel.SetActive(true);

            if (_currentLineIndex >= _currentNode.DialogueLines.Count)
            {
                ProcessSyncedAudio(EAudioTriggerType.OnDialogueEnd, -1);
                PlayNode(_currentNode.NextNodeID);
                return;
            }

            var line = _currentNode.DialogueLines[_currentLineIndex];

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
            DialoguePanel.SetActive(false);
            yield return new WaitForSeconds(line.DelayBefore);
            DialoguePanel.SetActive(true);
            ShowLineData(line);
        }

        private void ShowLineData(DialogueLine line)
        {
            if (line.Speaker != null)
            {
                SpeakerNameText.text = line.Speaker.name;
                SpeakerNameText.color = line.Speaker.ThemeColor;
                SpeakerNameText.gameObject.SetActive(true);
            }
            else
            {
                SpeakerNameText.gameObject.SetActive(false);
            }

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
            DialoguePanel.SetActive(false);
            foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);

            foreach (var choice in _currentNode.Choices)
            {
                if (!CheckConditions(choice.Conditions)) continue;

                GameObject btnGO = Instantiate(ChoiceButtonPrefab, ChoiceContainer);
                var tmpText = btnGO.GetComponentInChildren<TMP_Text>();
                if (tmpText != null) tmpText.text = choice.LocalizedText.GetText(CurrentLanguage);

                var button = btnGO.GetComponent<Button>();
                button.onClick.AddListener(() => { foreach (Transform child in ChoiceContainer) Destroy(child.gameObject); PlayNode(choice.NextNodeID); });
            }
        }

        private bool CheckConditions(List<ChoiceCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0) return true;
            foreach (var cond in conditions)
            {
                int varValue = Variables.ContainsKey(cond.Variable) ? Variables[cond.Variable] : 0;
                switch (cond.Operator)
                {
                    case EConditionOperator.Equal: if (varValue != cond.Value) return false; break;
                    case EConditionOperator.NotEqual: if (varValue == cond.Value) return false; break;
                    case EConditionOperator.Greater: if (varValue <= cond.Value) return false; break;
                    case EConditionOperator.Less: if (varValue >= cond.Value) return false; break;
                    case EConditionOperator.GreaterOrEqual: if (varValue < cond.Value) return false; break;
                    case EConditionOperator.LessOrEqual: if (varValue > cond.Value) return false; break;
                }
            }
            return true;
        }

        private void ProcessVariables()
        {
            foreach (var v in _currentNode.Variables)
            {
                if (!Variables.ContainsKey(v.VariableName)) Variables[v.VariableName] = 0;
                if (v.VarOperation == EVarOperation.Set) Variables[v.VariableName] = v.VarValue;
                else if (v.VarOperation == EVarOperation.Add) Variables[v.VariableName] += v.VarValue;
            }
            PlayNode(_currentNode.NextNodeID);
        }

        private void ProcessEndNode()
        {
            DialoguePanel.SetActive(false);
            if (_currentNode.EndAction == EEndAction.QuitGame) Application.Quit();
            else if (_currentNode.EndAction == EEndAction.LoadNextChapter && _currentNode.NextChapter != null) PlayTree(_currentNode.NextChapter);
        }

        private void ClearCharacters()
        {
            if (CharactersContainer != null) foreach (Transform child in CharactersContainer) Destroy(child.gameObject);
        }
    }
}