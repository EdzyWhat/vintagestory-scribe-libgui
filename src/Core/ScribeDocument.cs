namespace Scribe.Core;

/// <summary>
/// The game-agnostic model of a Scribe document: an ordered sequence of <see cref="ScribeBlock"/>s.
/// Each block is either a checkbox task or a freeform text section, so tasks and text can be
/// interspersed and reordered freely. All mutation methods return <c>true</c> on success and
/// <c>false</c> for invalid input (blank task text, out-of-range index), never throwing to the
/// caller. This type has no dependency on the Vintage Story API.
/// </summary>
public sealed class ScribeDocument
{
    private readonly List<ScribeBlock> _blocks = new();

    /// <summary>The blocks, in order. Read-only to callers; mutate via the methods below.</summary>
    public IReadOnlyList<ScribeBlock> Blocks => _blocks;

    /// <summary>Adds a checkbox task to the end. Blank/whitespace-only text is rejected; otherwise
    /// the text is stored verbatim. Whitespace normalization (trimming) is the editing layer's
    /// responsibility, not the model's -- see <see cref="SetBlockText"/>.</summary>
    public bool AddTask(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Task, text));
        return true;
    }

    /// <summary>Adds a freeform text section to the end. Blank/empty text is allowed.</summary>
    public bool AddTextSection(string? text)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Text, text ?? ""));
        return true;
    }

    /// <summary>
    /// Changes a block's text. For Task blocks blank/whitespace-only text is rejected; Text sections
    /// may be set to empty. Otherwise the text is stored verbatim -- the model does NOT trim
    /// surrounding whitespace. Whitespace normalization (e.g. stripping a trailing blank line from a
    /// committed edit) is the editing layer's job (the lectern dialog's NormalizeRowOnCommit), so
    /// the live in-place editor can keep a just-typed trailing newline long enough for the row to
    /// grow. The Done flag and kind are unchanged.
    /// </summary>
    public bool SetBlockText(int index, string? text)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (block.IsTask)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            block.Text = text;
        }
        else
        {
            block.Text = text ?? "";
        }
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

    /// <summary>Flips the pinned flag of a Task block. Fails on a Text section or bad index.</summary>
    public bool TogglePinned(int index)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (!block.IsTask) return false;
        block.Pinned = !block.Pinned;
        return true;
    }

    /// <summary>Removes the block at <paramref name="index"/>, preserving the order of the rest.</summary>
    public bool DeleteBlock(int index)
    {
        if (!IsValidIndex(index)) return false;
        _blocks.RemoveAt(index);
        return true;
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
