namespace Scribe.Core;

/// <summary>
/// The game-agnostic model of a Scribe document: an ordered sequence of <see cref="ScribeBlock"/>s.
/// Each block is either a checkbox task or a freeform text section, so tasks and text can be
/// interspersed and reordered freely. All mutation methods return <c>true</c> on success and
/// <c>false</c> for invalid input (out-of-range index), never throwing to the caller. Task text
/// is stored verbatim, including empty/whitespace-only text — the model enforces no non-blank
/// content invariant; removing an abandoned or cleared empty task is the editing layer's job.
/// This type has no dependency on the Vintage Story API.
/// </summary>
public sealed class ScribeDocument
{
    private readonly List<ScribeBlock> _blocks = new();

    /// <summary>The blocks, in order. Read-only to callers; mutate via the methods below.</summary>
    public IReadOnlyList<ScribeBlock> Blocks => _blocks;

    /// <summary>Stable identifier for this document. Assigned once when the document is created
    /// (a fresh <see cref="Guid"/>) and preserved through serialization, so a reference to a task
    /// in this document — a per-player pin — keeps resolving even after the document's owning
    /// block is broken and re-placed elsewhere (the id rides inside the serialized bytes). Set by
    /// the codec on the deserialization path via <see cref="SetDocId"/>.</summary>
    public Guid DocId { get; private set; } = Guid.NewGuid();

    /// <summary>Overwrites the document id with a persisted one. Used only by the codec when
    /// rebuilding a document from bytes that carry an id (the current format); callers never
    /// reassign a document's identity.</summary>
    internal void SetDocId(Guid docId) => DocId = docId;

    /// <summary>Adds a checkbox task to the end. Any text is accepted and stored verbatim,
    /// including empty/whitespace-only text (a new task starts empty and the player types into it).
    /// Whitespace normalization (trimming) and removal of an abandoned empty task are the editing
    /// layer's responsibility, not the model's -- see <see cref="SetBlockText"/>.</summary>
    public bool AddTask(string text)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Task, text));
        return true;
    }

    /// <summary>
    /// Inserts a checkbox task at <paramref name="index"/>, shifting later blocks down (so passing
    /// <c>currentIndex + 1</c> puts the new task directly under the current one — the editor's
    /// Enter=new-task gesture). <paramref name="index"/> may equal <see cref="Blocks"/>.Count to append.
    /// Any text is accepted and stored verbatim, including empty/whitespace-only text; an
    /// out-of-range index fails safely (see <see cref="AddTask"/>).
    /// </summary>
    public bool InsertTask(int index, string text)
    {
        if (index < 0 || index > _blocks.Count) return false;
        _blocks.Insert(index, new ScribeBlock(ScribeBlockKind.Task, text));
        return true;
    }

    /// <summary>Adds a freeform text section to the end. Blank/empty text is allowed.</summary>
    public bool AddTextSection(string? text)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Text, text ?? ""));
        return true;
    }

    /// <summary>
    /// Changes a block's text. Both Task and Text blocks may be set to any value, including
    /// empty/whitespace-only — the text is stored verbatim and the model does NOT trim surrounding
    /// whitespace or reject blank task text. This lets a task go transiently empty while the player
    /// clears it (the field writes through on every keystroke); removing a task left empty is the
    /// editing layer's job. Whitespace normalization (e.g. stripping a trailing blank line from a
    /// committed edit) is likewise the editing layer's job (the lectern dialog's NormalizeRowOnCommit),
    /// so the live in-place editor can keep a just-typed trailing newline long enough for the row to
    /// grow. The Done flag and kind are unchanged.
    /// </summary>
    public bool SetBlockText(int index, string? text)
    {
        if (!IsValidIndex(index)) return false;
        _blocks[index].Text = text ?? "";
        return true;
    }

    /// <summary>Flips the completed flag of a Task block. Fails on a Text section or bad index.</summary>
    public bool ToggleTask(int index)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (!block.IsTask) return false;
        block.Done = !block.Done;
        return true;
    }

    /// <summary>Removes the block at <paramref name="index"/>, preserving the order of the rest,
    /// and reports the removed block's <see cref="ScribeBlock.TaskId"/> so a caller can react to
    /// that specific task's removal (e.g. orphan a pin referencing it). On an invalid index the
    /// document is unchanged and <paramref name="deletedTaskId"/> is null.</summary>
    public bool DeleteBlock(int index, out Guid? deletedTaskId)
    {
        if (!IsValidIndex(index))
        {
            deletedTaskId = null;
            return false;
        }
        deletedTaskId = _blocks[index].TaskId;
        _blocks.RemoveAt(index);
        return true;
    }

    /// <summary>Removes the block at <paramref name="index"/>, preserving the order of the rest.
    /// Thin overload for callers that don't need the removed task's id.</summary>
    public bool DeleteBlock(int index) => DeleteBlock(index, out _);

    /// <summary>
    /// Changes the text of the task with the given stable <see cref="ScribeBlock.TaskId"/> — the
    /// identity-addressed convenience the pin editor uses to edit a task without knowing its index or
    /// block position (over <see cref="FindByTaskId"/> + <see cref="SetBlockText"/>). Unlike the raw
    /// <see cref="SetBlockText"/>, this HONORS the editing-layer content invariant: blank or
    /// whitespace-only <paramref name="text"/> is REJECTED (returns false, document unchanged) so a pin
    /// edit can never blank a task out. Also returns false when no task with that id exists or the id
    /// belongs to a non-task block. Text is otherwise stored verbatim (no trimming — same as
    /// <see cref="SetBlockText"/>). Pure data; no VS API.
    /// </summary>
    public bool SetTaskText(Guid taskId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsTask)
            {
                _blocks[i].Text = text;
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns the block with the given <see cref="ScribeBlock.TaskId"/>, or null if no
    /// block in this document has that id.</summary>
    public ScribeBlock? FindByTaskId(Guid taskId)
    {
        foreach (var block in _blocks)
        {
            if (block.TaskId == taskId) return block;
        }
        return null;
    }

    /// <summary>
    /// Moves the task with the given stable <see cref="ScribeBlock.TaskId"/> to the END of the block
    /// list, preserving the relative order of every other block — the identity-addressed "sink to the
    /// bottom" a completion under the Sink policy performs (scribe-lectern-view-consistency). Returns
    /// false (document unchanged) when no block has that id, the id belongs to a non-task block, or the
    /// task is already last. Pure data; no VS API.
    /// </summary>
    public bool MoveTaskToBottom(Guid taskId)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsTask)
            {
                if (i == _blocks.Count - 1) return false; // already last — nothing to do
                var block = _blocks[i];
                _blocks.RemoveAt(i);
                _blocks.Add(block);
                return true;
            }
        }
        return false;
    }

    /// <summary>Moves the block at <paramref name="from"/> to position <paramref name="to"/>.</summary>
    public bool MoveBlock(int from, int to)
    {
        if (!IsValidIndex(from) || !IsValidIndex(to)) return false;
        if (from == to) return true;
        var block = _blocks[from];
        _blocks.RemoveAt(from);
        _blocks.Insert(to, block);
        return true;
    }

    /// <summary>
    /// Replaces all blocks in one shot (used by the codec when rebuilding a document).
    /// </summary>
    internal void SetBlocks(IEnumerable<ScribeBlock> blocks)
    {
        _blocks.Clear();
        _blocks.AddRange(blocks);
    }

    private bool IsValidIndex(int index) => index >= 0 && index < _blocks.Count;
}
