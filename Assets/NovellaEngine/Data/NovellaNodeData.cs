using System;
using System.Collections.Generic;
using System.Linq;
using NovellaEngine.Runtime.UI;
using UnityEngine;

namespace NovellaEngine.Data
{
    public enum ENodeType { Dialogue, Branch, Event, End, Character, Audio, Variable, Condition, Note, Random, Wait, SceneSettings, Animation, EventBroadcast, Save, CustomDLC }
    public enum EBgTransition { None, Fade, SlideLeft, SlideRight, FlashWhite, FlashBlack }
    public enum EAnimTarget { Camera, Background, DialogueFrame, Character }
    public enum EAnimType { Shake, Punch, FadeIn, FadeOut, MoveTo, Scale }
    public enum ECharacterPlane { BackSlot1 = 0, BackSlot2 = 1, BackSlot3 = 2, Speaker = 20 }
    public enum EEndAction { ReturnToMainMenu, LoadNextChapter, LoadSpecificScene, QuitGame }
    public enum EAudioAction { Play, Stop }
    public enum EAudioChannel { BGM, SFX, Voice }
    public enum EVarOperation { Set, Add }
    public enum EAudioTriggerType { OnStart, OnEnd, TimeDelay, OnDialogueEnd }
    public enum EConditionOperator { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }
    public enum ENoteImageShape { Normal, Square, Circle }
    public enum ENoteImageAlignment { Background, TopLeft, TopCenter, TopRight, Left, Right, BottomLeft, BottomCenter, BottomRight }
    public enum ECharacterPosition { Center, Left, Right, FarLeft, FarRight, Custom }
    public enum EFramePosition { Default, Top, Center, Bottom, Custom }
    public enum EWaitMode { Time, UserClick }

    public enum ESceneActionType { ChangeBackground, ClearAllCharacters, HideCharacter, ShowCharacter, ShowUI, HideUI, SetUIText }

    [AttributeUsage(AttributeTargets.Class)]
    public class NovellaDLCNodeAttribute : Attribute
    {
        public string MenuName;
        public string NodeTitle;
        public string HexColor;
        public string Version;
        public string Description;

        public NovellaDLCNodeAttribute(string menuName, string nodeTitle, string hexColor, string description = "", string version = "1.0")
        {
            MenuName = menuName;
            NodeTitle = nodeTitle;
            HexColor = hexColor;
            Description = description;
            Version = version;
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class NovellaDLCOutputAttribute : Attribute
    {
        public string PortName;
        public NovellaDLCOutputAttribute(string portName) { PortName = portName; }
    }

    public static class DLCCache
    {
        private static Dictionary<Type, NovellaDLCNodeAttribute> _nodeAttrs = new Dictionary<Type, NovellaDLCNodeAttribute>();
        private static Dictionary<Type, List<System.Reflection.FieldInfo>> _outputFields = new Dictionary<Type, List<System.Reflection.FieldInfo>>();

        public static NovellaDLCNodeAttribute GetNodeAttribute(Type t)
        {
            if (_nodeAttrs.TryGetValue(t, out var attr)) return attr;
            attr = t.GetCustomAttributes(typeof(NovellaDLCNodeAttribute), false).FirstOrDefault() as NovellaDLCNodeAttribute;
            _nodeAttrs[t] = attr; return attr;
        }

        public static List<System.Reflection.FieldInfo> GetOutputFields(Type t)
        {
            if (_outputFields.TryGetValue(t, out var list)) return list;
            list = t.GetFields().Where(f => f.GetCustomAttributes(typeof(NovellaDLCOutputAttribute), false).Length > 0).ToList();
            _outputFields[t] = list; return list;
        }
    }

    [Serializable] public class NoteImageData { public Texture2D Image; public ENoteImageShape Shape = ENoteImageShape.Normal; public ENoteImageAlignment Alignment = ENoteImageAlignment.TopCenter; public Vector2 Offset = Vector2.zero; public Vector2 Size = new Vector2(100, 100); [Range(0f, 1f)] public float Alpha = 1f; }
    [Serializable] public class NoteLinkData { public string DisplayName = "Link"; public string URL = "https://"; }
    [Serializable] public class ChoiceCondition { public string Variable = "Reputation"; public EConditionOperator Operator = EConditionOperator.GreaterOrEqual; public int Value = 10; public bool ValueBool = true; public string ValueString = ""; }
    [Serializable] public class ChanceModifier { public string Variable = "Diamond"; public EConditionOperator Operator = EConditionOperator.GreaterOrEqual; public int Value = 10; public bool ValueBool = true; public string ValueString = ""; public int BonusWeight = 10; }
    [Serializable] public class DialogueAudioEvent { public int LineIndex = 0; public EAudioTriggerType TriggerType = EAudioTriggerType.OnStart; public float TimeDelay = 0f; public AudioClip AudioAsset; public EAudioAction AudioAction = EAudioAction.Play; public EAudioChannel AudioChannel = EAudioChannel.SFX; [Range(0f, 1f)] public float Volume = 1f; }
    [Serializable] public class NovellaAnimEvent { public int LineIndex = 0; public EAudioTriggerType TriggerType = EAudioTriggerType.OnStart; public float TimeDelay = 0f; public EAnimTarget Target = EAnimTarget.Camera; public NovellaCharacter TargetCharacter; public EAnimType AnimType = EAnimType.Shake; public float Duration = 0.5f; public float Strength = 10f; public Vector2 EndVector = Vector2.one; }
    [Serializable] public class CharacterInDialogue { public NovellaCharacter CharacterAsset; public ECharacterPlane Plane = ECharacterPlane.BackSlot1; public ECharacterPosition PositionPreset = ECharacterPosition.Center; public float Scale = 1.0f; public string Emotion = "Default"; public float PosX = 0f; public float PosY = 0f; public bool IsExpanded = true; public bool FlipX = false; public bool FlipY = false; }

    [Serializable]
    public class SceneSettingsEvent
    {
        public int LineIndex = 0;
        public EAudioTriggerType TriggerType = EAudioTriggerType.OnStart;
        public float TimeDelay = 0f;

        public ESceneActionType ActionType = ESceneActionType.ChangeBackground;

        public Sprite BgSprite;
        public Color BgColor = Color.white;
        public EBgTransition BgTransition = EBgTransition.Fade;
        public float BgTransitionTime = 1f;

        public NovellaCharacter TargetCharacter;

        // UI-actions: ShowUI / HideUI / SetUIText адресуют UI-элемент в сцене
        // через [UIBindingTarget] (drag&drop GameObject в инспекторе).
        // Для SetUIText TextValue = либо обычная строка, либо ключ локализации
        // если SetUITextIsLocalizationKey = true.
        [UIBindingTarget(UIBindingKind.Any)] public string UITargetId = "";
        public string UITextValue = "";
        public bool UITextIsLocalizationKey = false;
    }

    [Serializable]
    public class DialogueLine
    {
        public NovellaCharacter Speaker; public string Mood = "Default";
        public bool HideSpeakerName = false; public string CustomName = ""; public Color CustomNameColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        public bool HideSpeakerSprite = false; public bool CustomizeSpeakerLayout = false;
        public bool FlipX = false; public bool FlipY = false;
        public ECharacterPosition SpeakerPositionPreset = ECharacterPosition.Center; public float SpeakerPosX = 0f; public float SpeakerPosY = 0f; public float SpeakerScale = 1f; public ECharacterPlane SpeakerPlane = ECharacterPlane.Speaker;
        public GameObject OverrideDialogueFrame; public float DelayBefore = 0f; public int FontSize = 32; public LocalizedString LocalizedPhrase = new LocalizedString();
        public bool UseTypewriter = true; public float BaseSpeed = 40f; public bool UseCustomPacing = false; public AnimationCurve PacingCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        public bool CustomizeFrameLayout = false; public EFramePosition FramePositionPreset = EFramePosition.Default; public float FramePosX = 0f; public float FramePosY = 0f; public float FrameScale = 1f;

        // Опциональные UI-цели: drag&drop UI-элемента из сцены — Player пишет
        // фразу/имя спикера в этот binding вместо дефолтной DialoguePanel.
        // Пусто = используется DialoguePanel как раньше.
        [UIBindingTarget(UIBindingKind.Text)] public string UITextTargetId = "";
        [UIBindingTarget(UIBindingKind.Text)] public string UISpeakerTargetId = "";
    }

    [Serializable] public class NovellaChoice {
        public string PortID;
        public LocalizedString LocalizedText = new LocalizedString();
        public string NextNodeID;
        public List<ChoiceCondition> Conditions = new List<ChoiceCondition>();
        public bool HasCondition; public string ConditionVariable; public int ConditionValue;
        public int ChanceWeight = 50;
        public List<ChanceModifier> ChanceModifiers = new List<ChanceModifier>();
        // Если задан — Player не спавнит свою кнопку, а использует существующую
        // в сцене: пишет в её Text локализованный LocalizedText и навешивает
        // onClick на переход к NextNodeID. Так делается главное меню/инвентарь
        // без спавна префабов.
        [UIBindingTarget(UIBindingKind.Button)] public string UIButtonTargetId = "";
        public NovellaChoice() { PortID = "Choice_" + Guid.NewGuid().ToString().Substring(0, 5); }
    }
    [Serializable] public class VariableUpdate { public string VariableName = "MyVariable"; public EVarOperation VarOperation = EVarOperation.Set; public int VarValue = 1; public bool VarBool = true; public string VarString = ""; }
    [Serializable] public class NovellaGroupData { public string GroupID; public string Title = "New Group"; public Color TitleColor = Color.white; public Color BorderColor = new Color(0.6f, 0.6f, 0.6f, 1f); public int TitleFontSize = 24; public Color BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.5f); public List<string> ContainedNodeIDs = new List<string>(); public Rect Position; }

    [Serializable]
    public abstract class NovellaNodeBase
    {
        public string NodeID;
        public string NodeTitle;
        public Vector2 GraphPosition;
        public Color NodeCustomColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        public bool IsPinned;
        public abstract ENodeType NodeType { get; }
    }

    [Serializable]
    public class DialogueNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Dialogue;
        public List<CharacterInDialogue> ActiveCharacters = new List<CharacterInDialogue>();
        public List<DialogueLine> DialogueLines = new List<DialogueLine>();
        public bool UnlockDialogueLimit = false;
        public string NextNodeID;
        public string AudioSyncNodeID;
        public string AnimSyncNodeID;
        public string SceneSyncNodeID;
        [HideInInspector] public int FontSize = 32;
    }

    [Serializable]
    public class BranchNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Branch;
        public List<NovellaChoice> Choices = new List<NovellaChoice>();
        public bool UnlockChoiceLimit = false;
        public GameObject OverrideChoiceButtonPrefab;
    }

    [Serializable]
    public class ConditionNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Condition;
        public List<ChoiceCondition> Conditions = new List<ChoiceCondition>();
        public List<NovellaChoice> Choices = new List<NovellaChoice>();
    }

    [Serializable]
    public class RandomNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Random;
        public List<NovellaChoice> Choices = new List<NovellaChoice>();
        public bool UnlockChoiceLimit = false;
    }

    [Serializable]
    public class VariableNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Variable;
        public List<VariableUpdate> Variables = new List<VariableUpdate>();
        public string NextNodeID;
    }

    [Serializable]
    public class AudioNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Audio;
        public AudioClip AudioAsset;
        public EAudioAction AudioAction = EAudioAction.Play;
        public EAudioChannel AudioChannel = EAudioChannel.BGM;
        [Range(0f, 1f)] public float AudioVolume = 1f;
        public bool SyncWithDialogue = false;
        public List<DialogueAudioEvent> AudioEvents = new List<DialogueAudioEvent>();
        public string NextNodeID;
    }

    [Serializable]
    public class AnimationNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Animation;
        public bool SyncWithDialogue = false;
        public bool UnlockAnimLimit = false;
        public List<NovellaAnimEvent> AnimEvents = new List<NovellaAnimEvent>();
        public string NextNodeID;
    }

    [Serializable]
    public class SceneSettingsNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.SceneSettings;
        public bool SyncWithDialogue = false;
        public bool UnlockLimit = false;

        public Sprite BgSprite;
        public Color BgColor = Color.white;
        public EBgTransition BgTransition = EBgTransition.Fade;
        public float BgTransitionTime = 1f;
        public bool BgClearCharacters = true;

        public List<SceneSettingsEvent> SceneEvents = new List<SceneSettingsEvent>();
        public string NextNodeID;
    }

    [Serializable]
    public class EventBroadcastNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.EventBroadcast;
        public string BroadcastEventName = "MyCustomEvent";
        public string BroadcastEventParam = "";
        public string NextNodeID;
    }

    [Serializable]
    public class WaitNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Wait;
        public EWaitMode WaitMode = EWaitMode.Time;
        public float WaitTime = 1f;
        public bool WaitIsSkippable = true;
        public bool WaitClearText = false;
        public bool WaitHideFrame = false;
        public EFramePosition WaitIndicatorPreset = EFramePosition.Bottom;
        public float WaitIndicatorPosX = 0f;
        public float WaitIndicatorPosY = 80f;
        public Sprite WaitIndicatorSprite;
        public Color WaitIndicatorColor = Color.white;
        public float WaitIndicatorSize = 25f;
        public float WaitIndicatorAnimSpeed = 4f;
        public float WaitIndicatorAmplitude = 10f;
        public string WaitText = "";
        public Color WaitTextColor = new Color(1f, 1f, 1f, 0.7f);
        public int WaitTextSize = 24;
        public float WaitTextBlinkSpeed = 2f;
        public float WaitTextPosX = 0f;
        public float WaitTextPosY = -35f;
        // Если задан — Player пишет WaitText в этот UI-элемент вместо
        // дефолтного индикатора. Подходит для кастомных «press any key» панелей.
        [UIBindingTarget(UIBindingKind.Text)] public string UITextTargetId = "";
        public string NextNodeID;
    }

    [Serializable]
    public class SaveNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Save;
        public string NextNodeID;
    }

    [Serializable]
    public class NoteNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.Note;
        public float NoteWidth = 300f;
        public LocalizedString LocalizedNoteText = new LocalizedString();
        public bool ShowBackground = true;
        public string NoteURL = "";
        public Color NoteTitleColor = new Color(1f, 0.8f, 0.4f, 1f);
        public Color NoteTextColor = Color.white;
        public int NoteTitleFontSize = 18;
        public int FontSize = 14;
        public List<NoteImageData> NoteImages = new List<NoteImageData>();
        public List<NoteLinkData> NoteLinks = new List<NoteLinkData>();
    }

    [Serializable]
    public class EndNodeData : NovellaNodeBase
    {
        public override ENodeType NodeType => ENodeType.End;
        public EEndAction EndAction = EEndAction.ReturnToMainMenu;
        public NovellaTree NextChapter;
        public string TargetSceneName;
    }
}