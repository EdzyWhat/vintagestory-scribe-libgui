namespace Scribe.Core;

/// <summary>
/// The game-agnostic model of a Scribe document: an ordered sequence of <see cref="ScribeBlock"/>s.
/// Each block is one of five kinds — a checkbox task, a freeform text section, a Tracker
/// ("gather N of item X"), a Link (a reference to an item's Handbook page), or a Craft (a
/// recipe-bound "craft N of item X" task that generates ingredient subtasks) — so they can be
/// interspersed and reordered freely. All mutation methods return <c>true</c> on success and
/// <c>false</c> for invalid input (out-of-range index), never throwing to the caller. Task text
/// is stored verbatim, including empty/whitespace-only text — the model enforces no non-blank
/// content invariant; removing an abandoned or cleared empty task is the editing layer's job.
/// This type has no dependency on the Vintage Story API.
/// </summary>
public sealed class ScribeDocument
{
    /// <summary>Default title shown for a Lectern that has never had a title set.</summary>
    public const string DefaultTitle = "Untitled";

    /// <summary>Maximum number of characters in a Lectern title.</summary>
    public const int MaxTitleLength = 50;

    private readonly List<ScribeBlock> _blocks = new();

    /// <summary>The display title of this document. Defaults to <see cref="DefaultTitle"/>;
    /// the editing layer normalizes empty/whitespace titles back to the default before saving.</summary>
    public string Title { get; set; } = DefaultTitle;

    /// <summary>The blocks, in order. Read-only to callers; mutate via the methods below.</summary>
    public IReadOnlyList<ScribeBlock> Blocks => _blocks;

    /// <summary>The total number of blocks of ANY kind — tasks, notes, trackers, links, and craft parents
    /// all count equally. This is the measure the tier cap counts (<see cref="ScribeDocumentPolicy.MaxBlocks"/>,
    /// the editor's add gate, and the Transcribe capacity check): a capped surface holds "N of anything", so
    /// adding any kind advances this count and is refused once it reaches the cap. Pure data.</summary>
    public int BlockCount => _blocks.Count;

    /// <summary>The number of completable blocks — Task, Tracker, and Link (everything except a
    /// free-text section, matching <see cref="ScribeBlock.IsCompletable"/>). This is the "N tasks"
    /// the Transcribe overwrite confirm reports before replacing a target document. Pure data.</summary>
    public int CompletableCount
    {
        get
        {
            int count = 0;
            foreach (var block in _blocks)
                if (block.IsCompletable) count++;
            return count;
        }
    }

    /// <summary>The number of TASK-kind blocks only (excluding Trackers, Links, and free-text sections).
    /// NOTE: this is NOT the tier-cap measure — the cap counts blocks of ANY kind via
    /// <see cref="BlockCount"/> (refine-chalkboard §12, "N of anything"). This property remains for
    /// diagnostics/tests that want the task-only tally; it is distinct from <see cref="CompletableCount"/>
    /// (which also counts Trackers/Links) and from <see cref="BlockCount"/> (the total). Pure data.</summary>
    public int TaskCount
    {
        get
        {
            int count = 0;
            foreach (var block in _blocks)
                if (block.IsTask) count++;
            return count;
        }
    }

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

    /// <summary>Assigns this document a brand-new identity (a fresh <see cref="Guid"/>). Used ONLY for
    /// the creative "clone a placed block" case: a middle-click pick carries the source block's
    /// serialized DocId onto the copy, but the copy must not share identity with the still-live original —
    /// the DocId keys the mod's host and pin registries, so two live blocks under one id collide (the
    /// copy's dialog/lock/pin traffic resolves to the original). This is deliberately distinct from the
    /// load-path <see cref="SetDocId"/>, which RESTORES a persisted id; here we intentionally break the
    /// carried-over identity to forge a unique one.</summary>
    public void ReassignNewDocId() => DocId = Guid.NewGuid();

    /// <summary>
    /// Produces a deep, independent copy of this document with a brand-new identity: a fresh
    /// <see cref="DocId"/> and a fresh <see cref="ScribeBlock.TaskId"/> for every block. Every other
    /// field — title, and each block's kind, text, done flag, depth, assignment, and Tracker/Link
    /// references — is copied verbatim. The source document is NOT modified.
    ///
    /// <para>This is the Transcribe copy primitive. A verbatim byte-copy of a serialized document would
    /// duplicate its identity, so two live items would collide on the mod's per-player pin store and
    /// block-doc resolution (which key off <see cref="DocId"/> + <see cref="ScribeBlock.TaskId"/> — see
    /// <see cref="ReassignNewDocId"/>). Regenerating all ids here keeps the copy fully independent: pins,
    /// completion, and later edits on one item never touch the other. Pure data; no VS API.</para>
    /// </summary>
    public ScribeDocument CloneWithNewIdentity()
    {
        var copy = new ScribeDocument { Title = Title };
        var clonedBlocks = new List<ScribeBlock>(_blocks.Count);
        foreach (var block in _blocks)
            clonedBlocks.Add(CloneBlockWithNewTaskId(block));
        copy.SetBlocks(clonedBlocks);
        return copy;
    }

    /// <summary>
    /// Appends fresh-identity copies of every block in <paramref name="other"/> onto the END of this
    /// document, leaving this document's existing blocks (and its <see cref="DocId"/>/title) untouched — the
    /// Transcribe "append" copy mode (add-transcribe-copy-paste). Each appended block gets a NEW
    /// <see cref="ScribeBlock.TaskId"/> (via <see cref="CloneBlockWithNewTaskId"/>), exactly like
    /// <see cref="CloneWithNewIdentity"/>, so the appended tasks never collide with either the source's or
    /// this document's existing ids on pins/completion. Pure data; no VS API. The caller is responsible for
    /// any capacity check (this method does not enforce a block cap).
    /// </summary>
    public void AppendClonedBlocksFrom(ScribeDocument other)
    {
        if (other is null) return;
        foreach (var block in other._blocks)
            _blocks.Add(CloneBlockWithNewTaskId(block));
    }

    /// <summary>Deep-copy one block, minting a FRESH <see cref="ScribeBlock.TaskId"/> (the ctor's default when
    /// no taskId is supplied) so the copy is independent of the original on pins/completion resolution. Shared
    /// by <see cref="CloneWithNewIdentity"/> and <see cref="AppendClonedBlocksFrom"/>.</summary>
    private static ScribeBlock CloneBlockWithNewTaskId(ScribeBlock block) => new(
        block.Kind,
        block.Text,
        done: block.Done,
        depth: block.Depth,
        assignedToUid: block.AssignedToUid,
        targetItemCode: block.TargetItemCode,
        targetQuantity: block.TargetQuantity,
        currentQuantity: block.CurrentQuantity,
        linkTarget: block.LinkTarget,
        linkLabel: block.LinkLabel,
        recipeSignature: block.RecipeSignature);

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

    /// <summary>Adds a Tracker task ("gather N of item X") to the end and gives it a fresh stable
    /// <see cref="ScribeBlock.TaskId"/>. <paramref name="itemCode"/> is the plain item code to count
    /// (may be null and set later); <paramref name="targetQuantity"/> is clamped to ≥ 1 by the block.
    /// The row's display label is derived by the Mod layer from the code, so Text starts empty.</summary>
    public bool AddTracker(string? itemCode, int targetQuantity)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Tracker, "", targetItemCode: itemCode, targetQuantity: targetQuantity));
        return true;
    }

    /// <summary>Adds a Craft task ("craft N of item X") to the end and gives it a fresh stable
    /// <see cref="ScribeBlock.TaskId"/>, returning that id so the caller can immediately reconcile its
    /// ingredient subtasks (see <see cref="ReconcileCraftIngredients"/>). <paramref name="outputItemCode"/>
    /// is the recipe's output item code (the count target, like a Tracker's); <paramref name="targetQuantity"/>
    /// is clamped to ≥ 1 by the block; <paramref name="recipeSignature"/> binds the grid recipe variant the
    /// ingredients are generated from (empty when unresolved). The row's display label is derived by the Mod
    /// layer from the code, so Text starts empty.</summary>
    public Guid AddCraft(string? outputItemCode, int targetQuantity, string recipeSignature)
    {
        var block = new ScribeBlock(ScribeBlockKind.Craft, "",
            targetItemCode: outputItemCode, targetQuantity: targetQuantity, recipeSignature: recipeSignature);
        _blocks.Add(block);
        return block.TaskId;
    }

    /// <summary>Adds an <b>item</b> Link task (a reference to an item's Handbook page) to the end and gives
    /// it a fresh stable <see cref="ScribeBlock.TaskId"/>. <paramref name="target"/> is the plain collectible
    /// code (may be null). The row's display label is derived live by the Mod layer from the item, so Text
    /// and <see cref="ScribeBlock.LinkLabel"/> start empty.</summary>
    public bool AddLink(string? target)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Link, "", linkTarget: target));
        return true;
    }

    /// <summary>Adds a <b>guide-page</b> Link task (a reference to a non-item Handbook guide/explainer page)
    /// to the end. <paramref name="pageCode"/> is the bare Handbook page code (stored <c>"page:"</c>-prefixed
    /// via <see cref="ScribeLinkTarget.ForPage"/>); <paramref name="label"/> is the guide's title, captured at
    /// creation because a guide page has no item to resolve a name from later (add-tracker-link-tasks 7.6).</summary>
    public bool AddGuideLink(string pageCode, string? label)
    {
        _blocks.Add(new ScribeBlock(ScribeBlockKind.Link, "",
            linkTarget: ScribeLinkTarget.ForPage(pageCode), linkLabel: label));
        return true;
    }

    /// <summary>
    /// The contiguous depth-1 run owned by the block at <paramref name="parentIndex"/>: half-open
    /// <c>[start, end)</c> starting at the row after the parent and stopping at the first row that is
    /// not depth 1 (a depth-0 gap, a later parent, or the document end). Kind-agnostic — any depth-0
    /// row can own the run, not only Craft. An invalid index or a parent with no following depth-1
    /// rows yields an empty run (<c>start == end</c>). Completing a depth-1 row is a leaf; callers that
    /// treat only depth-0 as a parent should check <see cref="ScribeBlock.Depth"/> first.
    /// </summary>
    public (int Start, int End) OwnedRun(int parentIndex)
    {
        if (!IsValidIndex(parentIndex)) return (0, 0);
        int start = parentIndex + 1;
        int end = start;
        while (end < _blocks.Count && _blocks[end].Depth == 1) end++;
        return (start, end);
    }

    /// <summary>The index of the block with <paramref name="taskId"/>, or -1 if none matches.</summary>
    public int IndexOf(Guid taskId)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId) return i;
        }
        return -1;
    }

    /// <summary>
    /// Walks backward from a depth-1 row to the depth-0 parent that owns its contiguous run. Returns
    /// -1 when the index is invalid, the row is not depth 1, or no depth-0 row sits above it.
    /// Parent identity is this walk-back, never "any depth-0 from the same document."
    /// </summary>
    public int FindParentIndex(int childIndex)
    {
        if (!IsValidIndex(childIndex) || _blocks[childIndex].Depth != 1) return -1;
        for (int i = childIndex - 1; i >= 0; i--)
        {
            if (_blocks[i].Depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Moves the half-open slice <c>[startInclusive, endExclusive)</c> so its first row lands
    /// at <paramref name="destIndex"/>, preserving relative order inside the slice. <paramref name="destIndex"/>
    /// uses the same convention as <see cref="MoveBlock"/> (a row index in the list before the move).
    /// Dropping onto a row inside the slice is a successful no-op. Returns false when the range or dest
    /// is invalid.</summary>
    public bool MoveRange(int startInclusive, int endExclusive, int destIndex)
    {
        if (startInclusive < 0 || endExclusive > _blocks.Count || startInclusive >= endExclusive)
            return false;
        if (!IsValidIndex(destIndex)) return false;
        if (destIndex >= startInclusive && destIndex < endExclusive) return true;

        int len = endExclusive - startInclusive;
        var slice = _blocks.GetRange(startInclusive, len);
        _blocks.RemoveRange(startInclusive, len);
        int insertAt = destIndex < startInclusive ? destIndex : destIndex - len + 1;
        insertAt = Math.Clamp(insertAt, 0, _blocks.Count);
        _blocks.InsertRange(insertAt, slice);
        return true;
    }

    /// <summary>
    /// Moves the half-open slice <c>[startInclusive, endExclusive)</c> to the end of the document as
    /// one block, preserving relative order inside the slice. Returns false (unchanged) when the
    /// range is empty/invalid or already at the bottom. Used for Bound Sink so parent+children stay
    /// contiguous (parent first) rather than N independent <see cref="MoveTaskToBottom"/> calls.
    /// </summary>
    public bool MoveRangeToBottom(int startInclusive, int endExclusive)
    {
        if (startInclusive < 0 || endExclusive > _blocks.Count || startInclusive >= endExclusive)
            return false;
        if (endExclusive == _blocks.Count) return false; // already last
        int len = endExclusive - startInclusive;
        var slice = _blocks.GetRange(startInclusive, len);
        _blocks.RemoveRange(startInclusive, len);
        _blocks.AddRange(slice);
        return true;
    }

    /// <summary>
    /// Removes the half-open slice <c>[startInclusive, endExclusive)</c>. Returns false when the
    /// range is empty or out of bounds.
    /// </summary>
    public bool DeleteRange(int startInclusive, int endExclusive)
    {
        if (startInclusive < 0 || endExclusive > _blocks.Count || startInclusive >= endExclusive)
            return false;
        _blocks.RemoveRange(startInclusive, endExclusive - startInclusive);
        return true;
    }

    /// <summary>
    /// Loosely self-heals the ingredient subtasks of the Craft task identified by <paramref name="craftTaskId"/>
    /// (craft-task capability). Reconciles ONLY the contiguous run of <see cref="ScribeBlock.Depth"/>-1 rows
    /// directly below the parent — the rows it owns — against the freshly derived ingredient list:
    /// <list type="bullet">
    /// <item>For each counting <paramref name="ingredients"/> entry, sets the matched child Tracker's
    /// <see cref="ScribeBlock.TargetQuantity"/> to <c>PerCraftQuantity × craftsNeeded</c> (matching by item
    /// code, preserving the child's id and live progress). When <paramref name="createMissing"/> is true
    /// (Handbook create-once), a Tracker child is inserted at depth 1 when none matches; when false
    /// (the parent-target stepper) a missing ingredient is left missing — never inserted.</item>
    /// <item>For each non-counting <paramref name="notes"/> entry (e.g. a liquid ingredient in v1), ensures a
    /// depth-1 Text note with that exact text exists, creating it when missing <b>only</b> when
    /// <paramref name="createMissing"/> is true.</item>
    /// </list>
    /// It NEVER deletes a row (a player who removed or edited a subtask keeps their choice) and NEVER touches
    /// anything below depth 1 (one level only). New children (create-once only) append to the END of the owned
    /// run, keeping the group contiguous. Returns false (document unchanged) when no Craft block has that id.
    ///
    /// <para>Expects DISTINCT item codes across <paramref name="ingredients"/> (the Mod layer merges a recipe's
    /// duplicate ingredients before calling); a repeated code would resolve to the first unclaimed match.
    /// <paramref name="craftsNeeded"/> is the batch multiplier from <see cref="ScribeCraftMath.CraftsNeeded"/>
    /// (clamped to ≥ 1 here). Pure data; no VS API.</para>
    /// </summary>
    public bool ReconcileCraftIngredients(Guid craftTaskId,
        IReadOnlyList<ScribeCraftIngredient> ingredients, IReadOnlyList<string> notes, int craftsNeeded,
        bool createMissing = true)
    {
        int parentIndex = -1;
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == craftTaskId && _blocks[i].IsCraft) { parentIndex = i; break; }
        }
        if (parentIndex < 0) return false;
        if (craftsNeeded < 1) craftsNeeded = 1;

        // The owned run: the contiguous depth-1 rows immediately below the parent. runEnd is exclusive and
        // grows as we append new children so they stay inside (and at the end of) the run.
        var (runStart, runEnd) = OwnedRun(parentIndex);

        var claimed = new HashSet<int>(); // rows already matched this pass, so duplicate codes don't double-claim

        foreach (var ing in ingredients)
        {
            // A liquid ingredient counts in litres, so its target bypasses the integer per-craft multiply and
            // ceils the batch litre total instead (add-liquid-ingredient-tracker D3). A solid stays
            // PerCraftQuantity × craftsNeeded.
            int target = ing.IsLiquid
                ? ScribeCraftMath.LitreTarget(ing.LitresPerCraft, craftsNeeded)
                : (ing.PerCraftQuantity < 1 ? 1 : ing.PerCraftQuantity) * craftsNeeded;

            int matchIdx = -1;
            for (int i = runStart; i < runEnd; i++)
            {
                if (claimed.Contains(i)) continue;
                if (_blocks[i].IsTracker
                    && string.Equals(_blocks[i].TargetItemCode, ing.ItemCode, StringComparison.Ordinal))
                {
                    matchIdx = i;
                    break;
                }
            }

            if (matchIdx >= 0)
            {
                claimed.Add(matchIdx);
                _blocks[matchIdx].TargetQuantity = target; // rescale in place; keep id + CurrentQuantity
            }
            else if (createMissing)
            {
                _blocks.Insert(runEnd, new ScribeBlock(ScribeBlockKind.Tracker, "",
                    depth: 1, targetItemCode: ing.ItemCode, targetQuantity: target));
                runEnd++;
            }
        }

        foreach (var note in notes)
        {
            if (string.IsNullOrEmpty(note)) continue;
            bool exists = false;
            for (int i = runStart; i < runEnd; i++)
            {
                if (_blocks[i].Kind == ScribeBlockKind.Text
                    && string.Equals(_blocks[i].Text, note, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists && createMissing)
            {
                _blocks.Insert(runEnd, new ScribeBlock(ScribeBlockKind.Text, note, depth: 1));
                runEnd++;
            }
        }

        return true;
    }

    /// <summary>
    /// Stepper-only reconcile: update <see cref="ScribeBlock.TargetQuantity"/> on item-code matches
    /// inside the owned run. Never inserts, never deletes. Handbook create still uses
    /// <see cref="ReconcileCraftIngredients"/> with <c>createMissing: true</c> (the default).
    /// </summary>
    public bool RescaleCraftIngredients(Guid craftTaskId,
        IReadOnlyList<ScribeCraftIngredient> ingredients, IReadOnlyList<string> notes, int craftsNeeded)
        => ReconcileCraftIngredients(craftTaskId, ingredients, notes, craftsNeeded, createMissing: false);

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

    /// <summary>Flips the completed flag of a completable block (Task, Tracker, or Link). Fails on a
    /// Text section (which has no Done flag) or a bad index.</summary>
    public bool ToggleTask(int index)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (!block.IsCompletable) return false;
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

    /// <summary>
    /// Sets the live carried count (<see cref="ScribeBlock.CurrentQuantity"/>) of the carried-count-tracked
    /// block (a Tracker or a Craft parent) with the given stable <see cref="ScribeBlock.TaskId"/> — the
    /// identity-addressed op the count engine uses to push an updated have-count without knowing the block's
    /// index. The value is clamped only to ≥ 0 by the block's setter (NOT capped at <c>TargetQuantity</c> —
    /// overflow is meaningful, 7.14). Returns false (document unchanged) when no block has that id or the id
    /// belongs to a block whose count is not carried-driven (see <see cref="ScribeBlock.IsCarriedCountTracked"/>).
    /// Pure data; no VS API.
    /// </summary>
    public bool SetTrackerCurrentQuantity(Guid taskId, int currentQuantity)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsCarriedCountTracked)
            {
                _blocks[i].CurrentQuantity = currentQuantity; // clamped to ≥ 0 in the setter (may exceed target, 7.14)
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
    /// false (document unchanged) when no block has that id, the id belongs to a non-completable (Text)
    /// block, or the task is already last. Pure data; no VS API.
    /// </summary>
    public bool MoveTaskToBottom(Guid taskId)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsCompletable)
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

    /// <summary>Replaces this document's blocks wholesale with <paramref name="blocks"/> (order preserved),
    /// leaving <see cref="Title"/> and <see cref="DocId"/> untouched. This is the import validator's degrade
    /// primitive (add-scriptorium-import-export D5): after deserialization the client rewrites any block whose
    /// Tracker/Link reference doesn't resolve to a real item in THIS game as a plain Task, then installs the
    /// reconciled sequence here. Exposed publicly (unlike the codec-only internal <see cref="SetBlocks"/>)
    /// because that validator lives in the mod assembly, across the Core boundary. Pure data; no VS API.</summary>
    public void ReplaceBlocks(IEnumerable<ScribeBlock> blocks) => SetBlocks(blocks);

    private bool IsValidIndex(int index) => index >= 0 && index < _blocks.Count;
}
