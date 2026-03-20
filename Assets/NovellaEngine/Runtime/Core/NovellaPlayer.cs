using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NovellaEngine.Data;
using System.Linq;

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

        private AudioSource _bgmSource;
        private AudioSource _sfxSource;

        private NovellaNodeData _currentNode;
        private int _currentLineIndex = 0;
        private bool _isWaitingForClick = false;

        private void Start()
        {
            _bgmSource = gameObject.AddComponent<AudioSource>(); _bgmSource.loop = true;
            _sfxSource = gameObject.AddComponent<AudioSource>();

            if (StoryTree != null)
            {
                PlayTree(StoryTree);
            }
            else
            {
                Debug.LogWarning("[NovellaPlayer] Story Tree is not assigned!");
            }
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
                Debug.Log("[NovellaPlayer] Reached end of the line (No next node connected).");
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

        private void Update()
        {
            if (_isWaitingForClick && (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
            {
                _isWaitingForClick = false;
                _currentLineIndex++;
                ProcessDialogueLine();
            }
        }

        private void ProcessDialogueLine()
        {
            DialoguePanel.SetActive(true);

            if (_currentLineIndex >= _currentNode.DialogueLines.Count)
            {
                PlayNode(_currentNode.NextNodeID);
                return;
            }

            var line = _currentNode.DialogueLines[_currentLineIndex];

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

            DialogueBodyText.text = line.LocalizedPhrase.GetText(CurrentLanguage);
            DialogueBodyText.fontSize = _currentNode.FontSize;

            _isWaitingForClick = true;
        }

        private void ShowChoices()
        {
            DialoguePanel.SetActive(false);

            foreach (Transform child in ChoiceContainer)
            {
                Destroy(child.gameObject);
            }

            foreach (var choice in _currentNode.Choices)
            {
                if (!CheckConditions(choice.Conditions)) continue;

                GameObject btnGO = Instantiate(ChoiceButtonPrefab, ChoiceContainer);
                var tmpText = btnGO.GetComponentInChildren<TMP_Text>();
                if (tmpText != null)
                {
                    tmpText.text = choice.LocalizedText.GetText(CurrentLanguage);
                }

                var button = btnGO.GetComponent<Button>();
                button.onClick.AddListener(() => {
                    foreach (Transform child in ChoiceContainer) Destroy(child.gameObject);
                    PlayNode(choice.NextNodeID);
                });
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

                Debug.Log($"[Variable] {v.VariableName} = {Variables[v.VariableName]}");
            }

            PlayNode(_currentNode.NextNodeID);
        }

        private void ProcessStandaloneAudio()
        {
            if (!_currentNode.SyncWithDialogue)
            {
                AudioSource targetSource = _currentNode.AudioChannel == EAudioChannel.BGM ? _bgmSource : _sfxSource;

                if (_currentNode.AudioAction == EAudioAction.Play && _currentNode.AudioAsset != null)
                {
                    targetSource.clip = _currentNode.AudioAsset;
                    targetSource.volume = _currentNode.AudioVolume;
                    targetSource.Play();
                }
                else if (_currentNode.AudioAction == EAudioAction.Stop)
                {
                    targetSource.Stop();
                }
            }

            PlayNode(_currentNode.NextNodeID);
        }

        private void ProcessEndNode()
        {
            DialoguePanel.SetActive(false);

            if (_currentNode.EndAction == EEndAction.QuitGame)
            {
                Debug.Log("QUIT GAME");
                Application.Quit();
            }
            else if (_currentNode.EndAction == EEndAction.LoadNextChapter && _currentNode.NextChapter != null)
            {
                Debug.Log($"Loading next chapter: {_currentNode.NextChapter.name}");
                PlayTree(_currentNode.NextChapter);
            }
            else
            {
                Debug.Log("RETURN TO MAIN MENU");
                // —юда можно добавить SceneManager.LoadScene("MainMenu");
            }
        }

        private void ClearCharacters()
        {
            if (CharactersContainer != null)
            {
                foreach (Transform child in CharactersContainer) Destroy(child.gameObject);
            }
        }
    }
}