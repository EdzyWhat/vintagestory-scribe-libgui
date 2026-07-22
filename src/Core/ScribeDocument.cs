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

    /// <summary>Adds a checkbox task to the end. Text is trimmed; blank text is rejected.</summary>
    public bool AddTask(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Task, text.Trim()));
        return true;
    }

    /// <summary>Adds a freeform text section to the end. Blank/empty text is allowed.</summary>
    public bool AddTextSection(string? text)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Text, text ?? ""));
        return true;
    }

    /// <summary>
    /// Changes a block's text. For Task blocks blank text is rejected; Text sections may be
    /// set to empty. The Done flag and kind are unchanged.
    /// </summary>
    /// <param name="trimTask">When true (the default, for committed edits), a task's text is trimmed
    /// of surrounding whitespace. The live in-place editor passes <c>false</c> so a trailing newline
    /// the player just typed (Shift+Enter at the end of a line) survives long enough for the row to
    /// grow to fit it; that path trims only on commit (GuiDialogScribeLectern.NormalizeRowOnCommit).
    /// A blank/whitespace-only task is still rejected either way.</param>
    public bool SetBlockText(int index, string? text, bool trimTask = true)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (block.IsTask)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            block.Text = trimTask ? text.Trim() : text;
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
