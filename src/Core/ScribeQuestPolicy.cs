namespace Scribe.Core;

/// <summary>Controls whether newly detected quests are linked automatically. <see cref="PromptHud"/> and
/// <see cref="PromptPopup"/> both queue an accept-prompt identically (same detection/dedup/queuing) —
/// they differ only in which of the two presentation styles renders it (HUD banner vs. center-screen
/// modal), decided at render time (rework-quest-accept-notification-styles).</summary>
public enum ScribeQuestAcceptPolicy : byte
{
    Always = 0,
    Never = 1,
    /// <summary>Prompt via the pinned-task HUD's banner. Renamed from the original <c>Prompt</c> — same
    /// underlying byte value, so an existing saved preference resolves identically with no migration.</summary>
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
