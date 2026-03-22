using System;
using System.Collections.Generic;
using UnityEngine;

namespace NovellaEngine.Data
{
    public enum ENodeType { Dialogue, Branch, Event, End, Character, Audio, Variable, Condition, Note, Random }
    public enum ECharacterPlane { BackSlot1 = 0, BackSlot2 = 1, BackSlot3 = 2, Speaker = 20 }
    public enum EEndAction { ReturnToMainMenu, LoadNextChapter, LoadSpecificScene, QuitGame }

    public enum EAudioAction { Play, Stop }
    public enum EAudioChannel { BGM, SFX, Voice }
    public enum EVarOperation { Set, Add }
    public enum EAudioTriggerType { OnStart, OnEnd, TimeDelay, OnDialogueEnd }

    public enum EConditionOperator { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }

    public enum ENoteImageShape { Normal, Square, Circle }
    public enum ENoteImageAlignment { Background, TopLeft, TopCenter, TopRight, Left, Right, BottomLeft, BottomCenter, BottomRight }

    [Serializable]
    public class NoteImageData
    {
        public Texture2D Image;
        public ENoteImageShape Shape = ENoteImageShape.Normal;
        public ENoteImageAlignment Alignment = ENoteImageAlignment.TopCenter;
        public Vector2 Offset = Vector2.zero;
        public Vector2 Size = new Vector2(100, 100);
        [Range(0f, 1f)] public float Alpha = 1f;
    }

    [Serializable]
    public class NoteLinkData
    {
        public string DisplayName = "Link";
        public string URL = "https://";
    }

    [Serializable]
    public class ChoiceCondition
    {
        public string Variable = "Reputation";
        public EConditionOperator Operator = EConditionOperator.GreaterOrEqual;
        public int Value = 10;
        public bool ValueBool = true;
        public string ValueString = "";
    }

    [Serializable]
    public class ChanceModifier
    {
        public string Variable = "Diamond";
        public EConditionOperator Operator = EConditionOperator.GreaterOrEqual;
        public int Value = 10;
        public bool ValueBool = true;
        public string ValueString = "";
        public int BonusWeight = 10;
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
        public bool IsExpanded = true;
    }

    [Serializable]
    public class DialogueLine
    {
        public NovellaCharacter Speaker;
        public string Mood = "Default";

        public bool HideSpeakerName = false;
        public string CustomName = "";
        public Color CustomNameColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        public bool HideSpeakerSprite = false;

        public bool CustomizeSpeakerLayout = false;
        public float SpeakerPosX = 0f;
        public float SpeakerPosY = 0f;
        public float SpeakerScale = 1f;
        public ECharacterPlane SpeakerPlane = ECharacterPlane.Speaker;

        // === ÕŒ¬Œ≈:  ¿—“ŒÃÕ¿ﬂ –¿Ã ¿ ===
        public GameObject OverrideDialogueFrame;

        public float DelayBefore = 0f;
        public int FontSize = 32;
        public LocalizedString LocalizedPhrase = new LocalizedString();
        public bool UseTypewriter = true;
        public float BaseSpeed = 40f;
        public bool UseCustomPacing = false;
        public AnimationCurve PacingCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
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

        public int ChanceWeight = 50;
        public List<ChanceModifier> ChanceModifiers = new List<ChanceModifier>();

        public NovellaChoice() { PortID = "Choice_" + Guid.NewGuid().ToString().Substring(0, 5); }
    }

    [Serializable]
    public class VariableUpdate
    {
        public string VariableName = "MyVariable";
        public EVarOperation VarOperation = EVarOperation.Set;
        public int VarValue = 1;
        public bool VarBool = true;
        public string VarString = "";
    }

    [Serializable]
    public class NovellaGroupData
    {
        public string GroupID;
        public string Title = "New Group";

        public Color TitleColor = Color.white;
        public Color BorderColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        public int TitleFontSize = 24;
        public Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        public List<string> ContainedNodeIDs = new List<string>();
        public Rect Position;
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
        [HideInInspector] public int FontSize = 32;

        public List<NovellaChoice> Choices = new List<NovellaChoice>();
        public bool UnlockChoiceLimit = false;
        public List<ChoiceCondition> Conditions = new List<ChoiceCondition>();

        // === ÕŒ¬Œ≈:  ¿—“ŒÃÕ¿ﬂ  ÕŒœ ¿ (ƒÎˇ Branch) ===
        public GameObject OverrideChoiceButtonPrefab;

        public EEndAction EndAction = EEndAction.ReturnToMainMenu;
        public NovellaTree NextChapter;
        public string TargetSceneName;

        public AudioClip AudioAsset;
        public EAudioAction AudioAction = EAudioAction.Play;
        public EAudioChannel AudioChannel = EAudioChannel.BGM;
        [Range(0f, 1f)] public float AudioVolume = 1f;
        public bool SyncWithDialogue = false;
        public List<DialogueAudioEvent> AudioEvents = new List<DialogueAudioEvent>();

        public List<VariableUpdate> Variables = new List<VariableUpdate>();

        public float NoteWidth = 300f;
        public string NoteText = "“ÂÍÒÚ Á‡ÏÂÚÍË...";
        public bool ShowBackground = true;
        public string NoteURL = "";
        public Color NoteTitleColor = new Color(1f, 0.8f, 0.4f, 1f);
        public Color NoteTextColor = Color.white;
        public int NoteTitleFontSize = 18;
        public List<NoteImageData> NoteImages = new List<NoteImageData>();
        public List<NoteLinkData> NoteLinks = new List<NoteLinkData>();
    }
}