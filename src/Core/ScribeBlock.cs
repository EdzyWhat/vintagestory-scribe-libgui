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
}

/// <summary>
/// One element of a <see cref="ScribeDocument"/>. A document is an ordered sequence of
/// these, so tasks and free-text sections can be interspersed and reordered freely.
///
/// A block is one of four kinds (see <see cref="ScribeBlockKind"/>): a Task (checkbox + text),
/// a Text section (text only), a Tracker (a "gather N of item X" task), or a Link (a reference to
/// an item's Handbook page). <see cref="Done"/> is meaningful for Task, Tracker, and Link (all
/// completable); it is unused for Text. <see cref="Depth"/> is reserved for a future sub-item
/// hierarchy (0 = top level today); it is carried through persistence now so enabling nesting
/// later needs no format change.
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

    /// <summary>Indent/nesting level. Reserved for future hierarchy; always 0 for now.</summary>
    public int Depth { get; set; }

    /// <summary>Stable identifier for this block. Assigned once at construction (a fresh
    /// <see cref="Guid"/> when not supplied) and never changed by any mutation, so an external
    /// reference to this block survives reorder/insert/delete of its siblings. Serialized as 16
    /// raw bytes by <see cref="ScribeDocumentCodec"/>.</summary>
    public Guid TaskId { get; }

    /// <summary>Reserved for a future assignment capability (player/group UID). Unset by
    /// default; no mutation method exists yet and nothing in this codebase reads it.</summary>
    public string? AssignedToUid { get; set; }

    /// <summary>For a <see cref="ScribeBlockKind.Tracker"/>: the item to count, as a plain code
    /// string (e.g. <c>"game:ingot-copper"</c>). Null for other kinds. Stored as a string, never a
    /// parsed AssetLocation, to keep Core API-free (see the class remarks).</summary>
    public string? TargetItemCode { get; set; }

    /// <summary>For a Tracker: how many of <see cref="TargetItemCode"/> to gather. Clamped to ≥ 1
    /// on set (a target of 0 or negative is meaningless). Lowering the target also re-clamps
    /// <see cref="CurrentQuantity"/> down to the new ceiling. Defaults to 1; meaningless for other
    /// kinds (kept at 1).</summary>
    public int TargetQuantity
    {
        get => _targetQuantity;
        set
        {
            _targetQuantity = value < 1 ? 1 : value;
            if (_currentQuantity > _targetQuantity) _currentQuantity = _targetQuantity;
        }
    }
    private int _targetQuantity = 1;

    /// <summary>For a Tracker: how many are currently carried (the live have/need count). Clamped
    /// into <c>[0, <see cref="TargetQuantity"/>]</c> on set. Defaults to 0.</summary>
    public int CurrentQuantity
    {
        get => _currentQuantity;
        set => _currentQuantity = value < 0 ? 0 : (value > _targetQuantity ? _targetQuantity : value);
    }
    private int _currentQuantity;

    /// <summary>For a <see cref="ScribeBlockKind.Link"/>: the Handbook target this task references,
    /// as a plain code string. Null for other kinds. Stored as a string, never a parsed
    /// AssetLocation, to keep Core API-free (see the class remarks).</summary>
    public string? LinkTarget { get; set; }

    public ScribeBlock(ScribeBlockKind kind, string text, bool done = false, int depth = 0, string? assignedToUid = null, Guid? taskId = null,
        string? targetItemCode = null, int targetQuantity = 1, int currentQuantity = 0, string? linkTarget = null)
    {
        Kind = kind;
        Text = text;
        Done = done;
        Depth = depth;
        AssignedToUid = assignedToUid;
        TaskId = taskId ?? Guid.NewGuid();
        TargetItemCode = targetItemCode;
        // Set target BEFORE current so CurrentQuantity clamps against the intended ceiling.
        TargetQuantity = targetQuantity;
        CurrentQuantity = currentQuantity;
        LinkTarget = linkTarget;
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
}
