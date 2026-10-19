namespace Scribe.Core;

/// <summary>
/// The kind of a <see cref="ScribeBlock"/>. Persisted as a byte: values are explicit and MUST stay stable
/// (append new kinds; never renumber).
/// </summary>
public enum ScribeBlockKind : byte
{
    /// <summary>A checkbox to-do item (has a Done flag).</summary>
    Task = 0,

    /// <summary>A freeform text section with no checkbox.</summary>
    Text = 1,

    /// <summary>A "gather N of item X" task with a live have/need counter driven by carried inventory
    /// (<see cref="ScribeBlock.TargetItemCode"/>, <see cref="ScribeBlock.TargetQuantity"/>,
    /// <see cref="ScribeBlock.CurrentQuantity"/>). Has a Done flag.</summary>
    Tracker = 2,

    /// <summary>A reference task pointing at an item's Handbook page via <see cref="ScribeBlock.LinkTarget"/>.
    /// Acts as a hyperlink from every surface; opening the page never toggles its independent Done flag.</summary>
    Link = 3,

    /// <summary>A "craft N of item X" task: a recipe-bound composite generator. Counts the carried output
    /// like a <see cref="Tracker"/>, remembers its grid recipe variant
    /// (<see cref="ScribeBlock.RecipeSignature"/>), and maintains one ingredient Tracker subtask (depth 1)
    /// per recipe ingredient. Has a Done flag.</summary>
    Craft = 4,

    /// <summary>A depth-1 subtask for one Progression Framework quest objective, generated under its parent
    /// Quest Link and reconciled in place as the backend reports progress. Field reuse:
    /// <see cref="ScribeBlock.LinkTarget"/> is the objective's stable code (the reconcile match key, never a
    /// clickable target); <see cref="ScribeBlock.LinkLabel"/> is a captured display label used only when no
    /// item resolves; <see cref="ScribeBlock.TargetItemCode"/> is set only when the objective resolves to
    /// exactly one concrete item. Quantities are backend-reported, so it is excluded from
    /// <see cref="ScribeBlock.IsCarriedCountTracked"/> and <see cref="ScribeBlock.IsCompletable"/> (no
    /// checkbox).</summary>
    QuestObjective = 5,
}

/// <summary>
/// One element of a <see cref="ScribeDocument"/>: an ordered sequence of these lets tasks and free-text
/// sections be interspersed and reordered. <see cref="Done"/> is meaningful for every kind except Text.
/// <see cref="Depth"/> is the indentation level (0 = top-level, 1 = subtask) and is carried through every codec.
///
/// Tracker/Link item references are PLAIN STRINGS (<see cref="TargetItemCode"/> / <see cref="LinkTarget"/>),
/// never <c>AssetLocation</c>/<c>ItemStack</c>, keeping this type free of the Vintage Story API; the Mod
/// layer parses them. <see cref="TargetQuantity"/> and <see cref="CurrentQuantity"/> clamp themselves in
/// their setters.
///
/// <see cref="TaskId"/> is a stable per-block id preserved across every mutation and serialization.
/// External references (the per-player pin store) name a block by its document's
/// <see cref="ScribeDocument.DocId"/> plus this id, never by list position. Pinning is not stored on the block.
/// </summary>
public sealed class ScribeBlock
{
    public ScribeBlockKind Kind { get; set; }
    public string Text { get; set; }

    /// <summary>Completed flag. Meaningless for <see cref="ScribeBlockKind.Text"/>.</summary>
    public bool Done { get; set; }

    /// <summary>Row indentation level, clamped to <c>[0, 1]</c> on set (exactly one level of nesting).
    /// Kind-agnostic; a Craft task's generated ingredient rows live at depth 1.</summary>
    public int Depth
    {
        get => _depth;
        set => _depth = value < 0 ? 0 : value > 1 ? 1 : value;
    }
    private int _depth;

    /// <summary>Stable identifier, assigned once at construction (a fresh <see cref="Guid"/> when not
    /// supplied) and never changed, so external references survive sibling reorder/insert/delete. Serialized
    /// as 16 raw bytes by <see cref="ScribeDocumentCodec"/>.</summary>
    public Guid TaskId { get; }

    /// <summary>The optional player assignment carried by this block.</summary>
    public ScribeAssignment? Assignment { get; set; }

    /// <summary>Compatibility shim for pre-assignment callers. New code must use
    /// <see cref="Assignment"/>; this legacy UID cannot represent assignment state.</summary>
    [Obsolete("Use Assignment instead.")]
    public string? AssignedToUid
    {
        get => Assignment?.AssignerUid;
        set => Assignment = value is null ? null : new ScribeAssignment(value, "");
    }

    /// <summary>For a <see cref="ScribeBlockKind.Tracker"/>: the item to count, as a plain code string
    /// (e.g. <c>"game:ingot-copper"</c>). Null for other kinds.</summary>
    public string? TargetItemCode { get; set; }

    /// <summary>For a Tracker: how many of <see cref="TargetItemCode"/> to gather. Clamped to ≥ 1 on set.
    /// Does NOT re-clamp <see cref="CurrentQuantity"/>.</summary>
    public int TargetQuantity
    {
        get => _targetQuantity;
        set => _targetQuantity = value < 1 ? 1 : value;
    }
    private int _targetQuantity = 1;

    /// <summary>For a Tracker: how many of <see cref="TargetItemCode"/> are currently carried. Clamped only
    /// to ≥ 0, NOT capped at <see cref="TargetQuantity"/>, so overflow reads truthfully (<c>100 / 8</c>).
    /// "Satisfied" is <c>CurrentQuantity &gt;= TargetQuantity</c> everywhere.</summary>
    public int CurrentQuantity
    {
        get => _currentQuantity;
        set => _currentQuantity = value < 0 ? 0 : value;
    }
    private int _currentQuantity;

    /// <summary>For a <see cref="ScribeBlockKind.Link"/>: the Handbook target, as a plain code string. Null
    /// for other kinds.
    ///
    /// <para>Two flavors, distinguished by <see cref="ScribeLinkTarget"/>: an <b>item</b> Link stores a bare
    /// collectible code (e.g. <c>"game:ingot-copper"</c>) and derives icon+name from the resolved item; a
    /// <b>guide-page</b> Link stores a <c>"page:"</c>-prefixed Handbook page code, has no item, takes its
    /// name from <see cref="LinkLabel"/>, and shows a generic book icon.</para></summary>
    public string? LinkTarget { get; set; }

    /// <summary>For a guide-page or quest Link (a <c>"page:"</c>- or <c>"quest:"</c>-prefixed
    /// <see cref="LinkTarget"/>): the referenced page/quest's display title, captured at creation (neither
    /// has an <c>ItemStack</c> to resolve a name from). Null for item Links and non-Link kinds.</summary>
    public string? LinkLabel { get; set; }

    /// <summary>For a quest Link (a <c>"quest:"</c>-prefixed <see cref="LinkTarget"/>): the quest's
    /// description, captured at creation from the installed quest mod's catalog and never re-derived, so a
    /// since-uninstalled quest mod still renders it. Null for every other kind.</summary>
    public string? LinkDescription { get; set; }

    /// <summary>For a <see cref="ScribeBlockKind.Craft"/>: a stable string naming the grid recipe variant its
    /// ingredient subtasks come from (composition <c>outputCode|pattern|WxH</c>). The Mod layer re-resolves
    /// the live recipe from it, so documents stay small and survive recipe-data updates. Empty for non-Craft
    /// kinds and for a Craft whose recipe could not be resolved (which degrades to a plain output tracker).</summary>
    public string RecipeSignature { get; set; } = "";

    /// <summary>Opaque free-text detail attached by an external mod via <c>TryCreateExternalTask</c>.
    /// Kind-agnostic; never interpreted by Core or the Mod layer and shown verbatim as hover text. Null when
    /// nothing was attached.</summary>
    public string? ExtraInfo { get; set; }

    public ScribeBlock(ScribeBlockKind kind, string text, bool done = false, int depth = 0, string? assignedToUid = null, Guid? taskId = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0, string? linkTarget = null, string? linkLabel = null,
        string? recipeSignature = null, ScribeAssignment? assignment = null, string? linkDescription = null, string? extraInfo = null)
    {
        Kind = kind;
        Text = text;
        Done = done;
        Depth = depth;
        Assignment = assignment ?? (assignedToUid is null ? null : new ScribeAssignment(assignedToUid, ""));
        TaskId = taskId ?? Guid.NewGuid();
        TargetItemCode = targetItemCode;
        TargetQuantity = targetQuantity;
        CurrentQuantity = currentQuantity; // ≥0 only; may exceed the target (raw carried count)
        LinkTarget = linkTarget;
        LinkLabel = linkLabel;
        RecipeSignature = recipeSignature ?? "";
        LinkDescription = linkDescription;
        ExtraInfo = extraInfo;
    }

    public bool IsTask => Kind == ScribeBlockKind.Task;

    /// <summary>True for any block with a meaningful <see cref="Done"/> flag: everything except Text and
    /// <see cref="ScribeBlockKind.QuestObjective"/>. The single predicate every completion, pin, sink, and
    /// delete-from-reader path gates on. A QuestObjective is backend-driven and renders with no checkbox, but
    /// its row delete still works (it does not gate on this). Text-editing paths gate on <see cref="IsTask"/>
    /// instead, since a Tracker/Link has no player-editable text.</summary>
    public bool IsCompletable => Kind is not ScribeBlockKind.Text and not ScribeBlockKind.QuestObjective;

    /// <summary>True for a <see cref="ScribeBlockKind.QuestObjective"/> block.</summary>
    public bool IsQuestObjective => Kind == ScribeBlockKind.QuestObjective;

    /// <summary>True for a <see cref="ScribeBlockKind.Tracker"/> block.</summary>
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;

    /// <summary>True for a <see cref="ScribeBlockKind.Link"/> block.</summary>
    public bool IsLink => Kind == ScribeBlockKind.Link;

    /// <summary>True for a <see cref="ScribeBlockKind.Craft"/> block.</summary>
    public bool IsCraft => Kind == ScribeBlockKind.Craft;

    /// <summary>True for any block whose <see cref="CurrentQuantity"/> is driven by the viewer's carried
    /// inventory: a Tracker or a Craft. The carried-inventory scan gates on this so a Craft parent updates
    /// with its ingredient Trackers; <see cref="IsTracker"/> is the narrow check.</summary>
    public bool IsCarriedCountTracked => Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft;
}
