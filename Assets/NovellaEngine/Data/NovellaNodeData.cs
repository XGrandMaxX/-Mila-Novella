using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    public enum ENodeType { Dialogue, Branch, Event, End, Character, Audio, Variable }
    public enum ECharacterPlane { BackSlot1 = 0, BackSlot2 = 1, BackSlot3 = 2, Speaker = 20 }
    public enum EEndAction { ReturnToMainMenu, LoadNextChapter, QuitGame }

    public enum EAudioAction { Play, Stop }
    public enum EAudioChannel { BGM, SFX, Voice }
    public enum EVarOperation { Set, Add }
    public enum EAudioTriggerType { OnStart, OnEnd, TimeDelay, OnDialogueEnd }

    public enum EConditionOperator { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }

    [Serializable]
    public class ChoiceCondition
    {
        public string Variable = "Reputation";
        public EConditionOperator Operator = EConditionOperator.GreaterOrEqual;
        public int Value = 10;
    }

    [Serializable]
    public class DialogueAudioEvent
    {
        public int LineIndex = 0;
        public EAudioTriggerType TriggerType = EAudioTriggerType.OnStart;
        public float TimeDelay = 0f;
        public AudioClip AudioAsset;
        public EAudioAction AudioAction = EAudioAction.Play;
        public EAudioChannel AudioChannel = EAudioChannel.SFX;
        [Range(0f, 1f)] public float Volume = 1f;
    }

    [Serializable]
    public class CharacterInDialogue
    {
        public NovellaCharacter CharacterAsset;
        public ECharacterPlane Plane = ECharacterPlane.BackSlot1;
        public float Scale = 1.0f;
        public string Emotion = "Default";
        public float PosX = 0f;
        public float PosY = 0f;
    }

    [Serializable]
    public class DialogueLine
    {
        public NovellaCharacter Speaker;
        public string Mood = "Default";
        public float DelayBefore = 0f;
        public LocalizedString LocalizedPhrase = new LocalizedString();
    }

    [Serializable]
    public class NovellaChoice
    {
        public string PortID;
        public LocalizedString LocalizedText = new LocalizedString();
        public string NextNodeID;

        public List<ChoiceCondition> Conditions = new List<ChoiceCondition>();

        public bool HasCondition;
        public string ConditionVariable;
        public int ConditionValue;

        public NovellaChoice() { PortID = "Choice_" + Guid.NewGuid().ToString().Substring(0, 5); }
    }

    [Serializable]
    public class VariableUpdate
    {
        public string VariableName = "MyVariable";
        public EVarOperation VarOperation = EVarOperation.Set;
        public int VarValue = 1;
    }

    [Serializable]
    public class NovellaNodeData
    {
        public string NodeID;
        public string NodeTitle;
        public ENodeType NodeType;
        public Vector2 GraphPosition;

        public Color NodeCustomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public bool IsPinned;

        public List<CharacterInDialogue> ActiveCharacters = new List<CharacterInDialogue>();
        public List<DialogueLine> DialogueLines = new List<DialogueLine>();

        public NovellaCharacter Speaker;
        public string Mood;
        public LocalizedString LocalizedPhrase = new LocalizedString();

        public string NextNodeID;
        public string AudioSyncNodeID;

        public int FontSize = 24;

        public List<NovellaChoice> Choices = new List<NovellaChoice>();
        public bool UnlockChoiceLimit = false;

        public EEndAction EndAction = EEndAction.ReturnToMainMenu;
        public NovellaTree NextChapter;

        public AudioClip AudioAsset;
        public EAudioAction AudioAction = EAudioAction.Play;
        public EAudioChannel AudioChannel = EAudioChannel.BGM;
        [Range(0f, 1f)] public float AudioVolume = 1f;

        public bool SyncWithDialogue = false;
        public List<DialogueAudioEvent> AudioEvents = new List<DialogueAudioEvent>();

        public List<VariableUpdate> Variables = new List<VariableUpdate>();
        public string VariableName = "MyVariable";
        public EVarOperation VarOperation = EVarOperation.Set;
        public int VarValue = 1;
    }
}