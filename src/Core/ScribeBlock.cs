namespace Scribe.Core;

/// <summary>
/// The kind of a <see cref="ScribeBlock"/>. Persisted as a byte, so values are explicit
/// and MUST remain stable across versions (append new kinds; never renumber).
/// </summary>
public enum ScribeBlockKind : byte
{
    /// <summary>A checkbox to-do item (has a Done flag).</summary>
    Task = 0,

    /// <summary>A freeform text section with no checkbox.</summary>
    Text = 1,

    /// <summary>A "gather N of item X" task with a live have/need counter driven by carried
    /// inventory. Uses <see cref="ScribeBlock.TargetItemCode"/>, <see cref="ScribeBlock.TargetQuantity"/>,
    /// and <see cref="ScribeBlock.CurrentQuantity"/>. Still a checkbox task (has a Done flag).</summary>
    Tracker = 2,

    /// <summary>A reference task pointing at an item's Handbook page via
    /// <see cref="ScribeBlock.LinkTarget"/>. Behaves as a hyperlink from every surface and still
    /// has an independent Done flag (opening the page never toggles completion).</summary>
    Link = 3,

    /// <summary>A "craft N of item X" task: a recipe-bound composite generator. Like a
    /// <see cref="Tracker"/>, it counts the carried output via <see cref="ScribeBlock.TargetItemCode"/>,
    /// <see cref="ScribeBlock.TargetQuantity"/>, and <see cref="ScribeBlock.CurrentQuantity"/>; in
    /// addition it remembers which grid recipe variant it generates from
    /// (<see cref="ScribeBlock.RecipeSignature"/>) and auto-generates/maintains one ingredient Tracker
    /// subtask (at <see cref="ScribeBlock.Depth"/> 1) per recipe ingredient. Still a checkbox task
    /// (has a Done flag).</summary>
    Craft = 4,
}

/// <summary>
/// One element of a <see cref="ScribeDocument"/>. A document is an ordered sequence of
/// these, so tasks and free-text sections can be interspersed and reordered freely.
///
/// A block is one of five kinds (see <see cref="ScribeBlockKind"/>): a Task (checkbox + text),
/// a Text section (text only), a Tracker (a "gather N of item X" task), a Link (a reference to
/// an item's Handbook page), or a Craft (a recipe-bound "craft N of item X" task). <see cref="Done"/>
/// is meaningful for Task, Tracker, Link, and Craft (all completable); it is unused for Text.
/// <see cref="Depth"/> is the row's indentation level, clamped to one level: 0 = top-level row,
/// 1 = subtask (an indented child, e.g. a Craft task's generated ingredient rows). It is carried
/// through persistence in every codec.
///
/// The Tracker/Link item references are stored as PLAIN STRINGS (<see cref="TargetItemCode"/> /
/// <see cref="LinkTarget"/>), never parsed <c>AssetLocation</c>/<c>ItemStack</c> — that keeps this
/// type free of any Vintage Story API reference; the Mod layer parses them when it needs the game.
/// <see cref="TargetQuantity"/> and <see cref="CurrentQuantity"/> clamp themselves (target ≥ 1;
/// current into [0, target]) so the invariant holds regardless of caller — see their setters.
///
/// <see cref="TaskId"/> is a stable per-block identifier assigned at creation and preserved
/// across every mutation and through serialization. Nothing in the document references a block
/// by list position durably (the index shifts on move/insert/delete); external references (the
/// per-player pin store) name a block by its owning document's <see cref="ScribeDocument.DocId"/>
/// plus this id. Pinning is no longer a field on the block — it moved to a per-player store.
/// </summary>
public sealed class ScribeBlock
{
    public ScribeBlockKind Kind { get; set; }
    public string Text { get; set; }

    /// <summary>Completed flag. Only meaningful when <see cref="Kind"/> is Task.</summary>
    public bool Done { get; set; }

    /// <summary>Row indentation level, clamped to one level on set: 0 = top-level row, 1 = subtask
    /// (an indented child rendered beneath the row above it). Any value outside <c>[0, 1]</c> is
    /// clamped — the system supports exactly one level of nesting (no depth-2). Kind-agnostic: any
    /// block kind may be a subtask (task-subtasks capability). A Craft task's generated ingredient
    /// rows live at depth 1.</summary>
    public int Depth
    {
        get => _depth;
        set => _depth = value < 0 ? 0 : value > 1 ? 1 : value;
    }
    private int _depth;

    /// <summary>Stable identifier for this block. Assigned once at construction (a fresh
    /// <see cref="Guid"/> when not supplied) and never changed by any mutation, so an external
    /// reference to this block survives reorder/insert/delete of its siblings. Serialized as 16
    /// raw bytes by <see cref="ScribeDocumentCodec"/>.</summary>
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

    /// <summary>For a <see cref="ScribeBlockKind.Tracker"/>: the item to count, as a plain code
    /// string (e.g. <c>"game:ingot-copper"</c>). Null for other kinds. Stored as a string, never a
    /// parsed AssetLocation, to keep Core API-free (see the class remarks).</summary>
    public string? TargetItemCode { get; set; }

    /// <summary>For a Tracker: how many of <see cref="TargetItemCode"/> to gather. Clamped to ≥ 1
    /// on set (a target of 0 or negative is meaningless). Defaults to 1; meaningless for other
    /// kinds (kept at 1). Does NOT re-clamp <see cref="CurrentQuantity"/>: the current count is the
    /// live raw carried amount and may legitimately exceed the target (see that property).</summary>
    public int TargetQuantity
    {
        get => _targetQuantity;
        set => _targetQuantity = value < 1 ? 1 : value;
    }
    private int _targetQuantity = 1;

    /// <summary>For a Tracker: how many of <see cref="TargetItemCode"/> are currently carried (the live
    /// have-count). Clamped only to ≥ 0 on set — it is NOT capped at <see cref="TargetQuantity"/>, so a
    /// player carrying more than the target reads the true overflow (e.g. <c>100 / 8</c>), not a clamped
    /// <c>8 / 8</c> (feedback 7.14). "Satisfied" is therefore <c>CurrentQuantity &gt;= TargetQuantity</c>
    /// everywhere it's tested. Defaults to 0.</summary>
    public int CurrentQuantity
    {
        get => _currentQuantity;
        set => _currentQuantity = value < 0 ? 0 : value;
    }
    private int _currentQuantity;

    /// <summary>For a <see cref="ScribeBlockKind.Link"/>: the Handbook target this task references,
    /// as a plain code string. Null for other kinds. Stored as a string, never a parsed
    /// AssetLocation, to keep Core API-free (see the class remarks).
    ///
    /// <para>Two flavors, distinguished by <see cref="ScribeLinkTarget"/>: an <b>item</b> Link stores a
    /// bare collectible code (e.g. <c>"game:ingot-copper"</c>) and derives its icon+name live from the
    /// resolved item; a <b>guide-page</b> Link stores a <c>"page:"</c>-prefixed Handbook page code (e.g.
    /// <c>"page:craftinginfo-knapping"</c>) — it has no item, so its display name lives in
    /// <see cref="LinkLabel"/> and its icon is a generic book (add-tracker-link-tasks 7.6).</para></summary>
    public string? LinkTarget { get; set; }

    /// <summary>For a guide-page <see cref="ScribeBlockKind.Link"/> (a <c>"page:"</c>-prefixed
    /// <see cref="LinkTarget"/>): the guide's display title, captured from the Handbook page at creation
    /// time (a guide page has no <c>ItemStack</c> to resolve a name from). Null for an item Link (whose
    /// name resolves live from the item) and for non-Link kinds (add-tracker-link-tasks 7.6).</summary>
    public string? LinkLabel { get; set; }

    /// <summary>For a <see cref="ScribeBlockKind.Craft"/>: a stable string identifying which grid recipe
    /// variant this task generates its ingredient subtasks from (the working composition is
    /// <c>outputCode|pattern|WxH</c>). The Mod layer re-resolves the live recipe from this signature to
    /// (re)generate/reconcile the ingredient rows, so documents stay small and survive recipe-data
    /// updates. Empty string for non-Craft kinds (and for a Craft whose recipe could not be resolved,
    /// which then degrades to a plain output tracker). Stored as a plain string to keep Core API-free.</summary>
    public string RecipeSignature { get; set; } = "";

    public ScribeBlock(ScribeBlockKind kind, string text, bool done = false, int depth = 0, string? assignedToUid = null, Guid? taskId = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0, string? linkTarget = null, string? linkLabel = null,
        string? recipeSignature = null, ScribeAssignment? assignment = null)
    {
        Kind = kind;
        Text = text;
        Done = done;
        Depth = depth;
        Assignment = assignment ?? (assignedToUid is null ? null : new ScribeAssignment(assignedToUid, ""));
        TaskId = taskId ?? Guid.NewGuid();
        TargetItemCode = targetItemCode;
        TargetQuantity = targetQuantity;
        CurrentQuantity = currentQuantity; // ≥0 only; may exceed the target (raw carried count, 7.14)
        LinkTarget = linkTarget;
        LinkLabel = linkLabel;
        RecipeSignature = recipeSignature ?? "";
    }

    public bool IsTask => Kind == ScribeBlockKind.Task;

    /// <summary>True for any block that carries a meaningful <see cref="Done"/> flag — Task, Tracker,
    /// and Link (everything except a free-text section). This is the single predicate every
    /// completion, pin, sink, and delete-from-reader path gates on, so a Tracker or Link completes and
    /// pins exactly like a plain Task. Text-EDITING paths still gate on <see cref="IsTask"/> instead
    /// (a Tracker/Link has no player-editable text — its label comes from the referenced item).</summary>
    public bool IsCompletable => Kind != ScribeBlockKind.Text;

    /// <summary>True for a <see cref="ScribeBlockKind.Tracker"/> block.</summary>
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;

    /// <summary>True for a <see cref="ScribeBlockKind.Link"/> block.</summary>
    public bool IsLink => Kind == ScribeBlockKind.Link;

    /// <summary>True for a <see cref="ScribeBlockKind.Craft"/> block.</summary>
    public bool IsCraft => Kind == ScribeBlockKind.Craft;

    /// <summary>True for any block whose <see cref="CurrentQuantity"/> is driven by the viewer's carried
    /// inventory — a <see cref="ScribeBlockKind.Tracker"/> (gather-count) or a
    /// <see cref="ScribeBlockKind.Craft"/> (output-count). This is the broader predicate the
    /// carried-inventory scan gates on, so a Craft parent updates alongside its ingredient Tracker
    /// children. (<see cref="IsTracker"/> remains the narrow "is exactly a Tracker" check.)</summary>
    public bool IsCarriedCountTracked => Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft;
}
