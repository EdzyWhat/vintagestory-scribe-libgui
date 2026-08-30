namespace Scribe.Core;

/// <summary>Controls whether newly detected quests are linked automatically.</summary>
public enum ScribeQuestAcceptPolicy : byte
{
    Always = 0,
    Never = 1,
    Prompt = 2,
}

/// <summary>Controls whether detected quest completion marks a linked task done.</summary>
public enum ScribeQuestCompletionPolicy : byte
{
    Always = 0,
    Never = 1,
    Prompt = 2,
}
