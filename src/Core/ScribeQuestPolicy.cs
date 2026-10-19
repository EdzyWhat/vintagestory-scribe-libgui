namespace Scribe.Core;

/// <summary>Controls whether newly detected quests are linked automatically. <see cref="PromptHud"/> and
/// <see cref="PromptPopup"/> queue prompts identically; they differ only in how it renders.</summary>
public enum ScribeQuestAcceptPolicy : byte
{
    Always = 0,
    Never = 1,
    /// <summary>Prompt via the pinned-task HUD's banner. Shares the byte value of the original
    /// <c>Prompt</c> member, so saved preferences resolve unchanged.</summary>
    PromptHud = 2,
    /// <summary>Prompt via a center-screen modal, gated on <c>ScribeVanillaDialogGuard</c> so it never
    /// contests a click against a vanilla dialog it can't out-paint.</summary>
    PromptPopup = 3,
}

/// <summary>Controls whether detected quest completion marks a linked task done.</summary>
public enum ScribeQuestCompletionPolicy : byte
{
    Always = 0,
    Never = 1,
    Prompt = 2,
}
