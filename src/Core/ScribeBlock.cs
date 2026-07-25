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
}

/// <summary>
/// One element of a <see cref="ScribeDocument"/>. A document is an ordered sequence of
/// these, so tasks and free-text sections can be interspersed and reordered freely.
///
/// A block is either a Task (checkbox + text) or a Text section (text only). <see cref="Done"/>
/// is only meaningful for Task blocks. <see cref="Depth"/> is reserved for a future
/// sub-item hierarchy (0 = top level today); it is carried through persistence now so
/// enabling nesting later needs no format change.
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

    public ScribeBlock(ScribeBlockKind kind, string text, bool done = false, int depth = 0, string? assignedToUid = null, Guid? taskId = null)
    {
        Kind = kind;
        Text = text;
        Done = done;
        Depth = depth;
        AssignedToUid = assignedToUid;
        TaskId = taskId ?? Guid.NewGuid();
    }

    public bool IsTask => Kind == ScribeBlockKind.Task;
}
