namespace Scribe.Core;

/// <summary>
/// The game-agnostic model of a Scribe document: an ordered sequence of <see cref="ScribeBlock"/>s of any
/// kind, interspersed and reorderable. Mutation methods return <c>true</c> on success and <c>false</c> for
/// invalid input (out-of-range index), never throwing. Text is stored verbatim, including empty text; the
/// model enforces no non-blank invariant (removing an abandoned empty task is the editing layer's job).
/// No Vintage Story API dependency.
/// </summary>
public sealed class ScribeDocument
{
    /// <summary>Default title for a Lectern that has never had one set.</summary>
    public const string DefaultTitle = "Untitled";

    /// <summary>Maximum characters in a Lectern title.</summary>
    public const int MaxTitleLength = 50;

    private readonly List<ScribeBlock> _blocks = new();

    /// <summary>The display title. The editing layer normalizes empty/whitespace titles back to
    /// <see cref="DefaultTitle"/> before saving.</summary>
    public string Title { get; set; } = DefaultTitle;

    /// <summary>The blocks, in order. Read-only; mutate via the methods below.</summary>
    public IReadOnlyList<ScribeBlock> Blocks => _blocks;

    /// <summary>The number of blocks of ANY kind. This is the measure the tier cap counts
    /// (<see cref="ScribeDocumentPolicy.MaxBlocks"/>, the editor's add gate, the Transcribe capacity check).</summary>
    public int BlockCount => _blocks.Count;

    /// <summary>The number of completable blocks (see <see cref="ScribeBlock.IsCompletable"/>), the "N tasks"
    /// the Transcribe overwrite confirm reports.</summary>
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

    /// <summary>The number of TASK-kind blocks only. NOT the tier-cap measure (see <see cref="BlockCount"/>);
    /// kept for diagnostics and tests.</summary>
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

    /// <summary>Stable identifier, assigned once at creation and preserved through serialization, so a
    /// per-player pin keeps resolving after the owning block is broken and re-placed. Set by the codec via
    /// <see cref="SetDocId"/>.</summary>
    public Guid DocId { get; private set; } = Guid.NewGuid();

    /// <summary>Overwrites the document id with a persisted one. Used only by the codec.</summary>
    internal void SetDocId(Guid docId) => DocId = docId;

    /// <summary>Assigns a brand-new identity. Used ONLY for the creative "clone a placed block" case: a
    /// middle-click pick carries the source's DocId onto the copy, but DocId keys the mod's host and pin
    /// registries, so two live blocks under one id would collide. Distinct from <see cref="SetDocId"/>,
    /// which RESTORES a persisted id.</summary>
    public void ReassignNewDocId() => DocId = Guid.NewGuid();

    /// <summary>
    /// Deep, independent copy with a fresh <see cref="DocId"/> and a fresh <see cref="ScribeBlock.TaskId"/>
    /// for every block; all other fields are copied verbatim. The source is not modified. This is the
    /// Transcribe copy primitive: a byte-copy would duplicate identity and collide on the pin store and
    /// block-doc resolution.
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
    /// Appends fresh-identity copies of every block in <paramref name="other"/> to the END (the Transcribe
    /// "append" mode), leaving existing blocks, <see cref="DocId"/> and title untouched. The caller owns any
    /// capacity check.
    /// </summary>
    public void AppendClonedBlocksFrom(ScribeDocument other)
    {
        if (other is null) return;
        foreach (var block in other._blocks)
            _blocks.Add(CloneBlockWithNewTaskId(block));
    }

    /// <summary>Appends a fully-formed block VERBATIM, preserving its <see cref="ScribeBlock.TaskId"/>
    /// (contrast <see cref="AppendClonedBlocksFrom"/>). Used for Accept-time assignment placement: the block
    /// is a MOVE of one canonical assignment and must keep the record's TaskId. The caller owns any
    /// capacity check.</summary>
    public void AppendAssignedBlock(ScribeBlock block) => _blocks.Add(block);

    /// <summary>Same contract as <see cref="AppendAssignedBlock"/> but at an explicit position (the
    /// accepting player's New Task Insert preference). <paramref name="index"/> is clamped into
    /// <c>[0, Blocks.Count]</c> so a stale index degrades rather than throws.</summary>
    public void InsertAssignedBlock(int index, ScribeBlock block) =>
        _blocks.Insert(Math.Clamp(index, 0, _blocks.Count), block);

    /// <summary>Appends a fully-formed block verbatim, preserving its freshly-minted TaskId; the placement
    /// primitive for <see cref="ScribeExternalTaskMapper"/> output. Separate from
    /// <see cref="AppendAssignedBlock"/> because that contract is an assignment MOVE of a pre-existing,
    /// correlated TaskId. The caller owns any capacity check.</summary>
    public void AppendExternalBlock(ScribeBlock block) => _blocks.Add(block);

    /// <summary>Deep-copies one block with a FRESH <see cref="ScribeBlock.TaskId"/>. Shared by
    /// <see cref="CloneWithNewIdentity"/> and <see cref="AppendClonedBlocksFrom"/>.</summary>
    private static ScribeBlock CloneBlockWithNewTaskId(ScribeBlock block) => new(
        block.Kind,
        block.Text,
        done: block.Done,
        depth: block.Depth,
        assignedToUid: null,
        targetItemCode: block.TargetItemCode,
        targetQuantity: block.TargetQuantity,
        currentQuantity: block.CurrentQuantity,
        linkTarget: block.LinkTarget,
        linkLabel: block.LinkLabel,
        recipeSignature: block.RecipeSignature,
        assignment: block.Assignment?.Clone(),
        linkDescription: block.LinkDescription);

    /// <summary>The index a document-level create uses for <paramref name="pos"/>: 0 for Top,
    /// <see cref="Blocks"/>.Count for Bottom. Unknown values follow
    /// <see cref="ScribePlayerSettings.NormalizeNewTaskInsert"/> (Top).</summary>
    public int InsertIndex(ScribeNewTaskInsert pos) =>
        ScribePlayerSettings.NormalizeNewTaskInsert(pos) == ScribeNewTaskInsert.Bottom
            ? _blocks.Count
            : 0;

    /// <summary>The index Accept-time placement uses for a row carrying <paramref name="batchId"/>: directly
    /// after the cluster (its owned run, for a depth-0 parent) of a sibling from the same batch already placed,
    /// so a batch stays contiguous whatever order its rows are Accepted in. Falls back to
    /// <paramref name="fallback"/>'s <see cref="InsertIndex"/> when no sibling is placed yet or
    /// <paramref name="batchId"/> is <see cref="Guid.Empty"/> (see <see cref="ScribeAssignment.BatchId"/>);
    /// gluing unrelated rows together would be wrong.
    ///
    /// <para>Known limitation: a subtask Accepted BEFORE its parent falls back to Top/Bottom, and the parent
    /// then lands next to it rather than ahead of it.</para></summary>
    public int InsertIndexForBatch(Guid batchId, ScribeNewTaskInsert fallback)
    {
        if (batchId != Guid.Empty)
        {
            for (int i = _blocks.Count - 1; i >= 0; i--)
            {
                if (_blocks[i].Assignment?.BatchId != batchId) continue;
                return _blocks[i].Depth == 0 ? OwnedRun(i).End : i + 1;
            }
        }
        return InsertIndex(fallback);
    }

    /// <summary>Adds a checkbox task to the end. Any text is accepted verbatim, including empty (a new task
    /// starts empty); trimming and removal of an abandoned empty task belong to the editing layer, see
    /// <see cref="SetBlockText"/>.</summary>
    public bool AddTask(string text) => InsertTask(_blocks.Count, text);

    /// <summary>
    /// Inserts a checkbox task at <paramref name="index"/>, shifting later blocks down (<c>currentIndex + 1</c>
    /// puts it under the current one). <paramref name="index"/> may equal <see cref="Blocks"/>.Count to
    /// append. Text is stored verbatim; an out-of-range index fails safely.
    /// </summary>
    public bool InsertTask(int index, string text) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Task, text));

    /// <summary>Adds a freeform text section to the end. Blank/empty text is allowed.</summary>
    public bool AddTextSection(string? text) => InsertTextSection(_blocks.Count, text);

    /// <summary>Inserts a freeform text section at <paramref name="index"/>. Out-of-range fails safely.</summary>
    public bool InsertTextSection(int index, string? text) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Text, text ?? ""));

    /// <summary>Adds a Tracker ("gather N of item X") to the end. <paramref name="itemCode"/> may be null and
    /// set later; <paramref name="targetQuantity"/> is clamped to ≥ 1. Text starts empty (the Mod layer derives
    /// the label from the code).</summary>
    public bool AddTracker(string? itemCode, int targetQuantity) =>
        InsertTracker(_blocks.Count, itemCode, targetQuantity);

    /// <summary>Inserts a Tracker at <paramref name="index"/>. Out-of-range fails safely.</summary>
    public bool InsertTracker(int index, string? itemCode, int targetQuantity) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Tracker, "",
            targetItemCode: itemCode, targetQuantity: targetQuantity));

    /// <summary>Adds a Craft task ("craft N of item X") to the end and returns its
    /// <see cref="ScribeBlock.TaskId"/> so the caller can reconcile ingredient subtasks (see
    /// <see cref="ReconcileCraftIngredients"/>). <paramref name="outputItemCode"/> is the recipe's output;
    /// <paramref name="recipeSignature"/> binds the grid recipe variant (empty when unresolved). Text starts
    /// empty.</summary>
    public Guid AddCraft(string? outputItemCode, int targetQuantity, string recipeSignature) =>
        InsertCraft(_blocks.Count, outputItemCode, targetQuantity, recipeSignature);

    /// <summary>Inserts a Craft parent at <paramref name="index"/> and returns its TaskId. Out-of-range
    /// returns <see cref="Guid.Empty"/> and inserts nothing.</summary>
    public Guid InsertCraft(int index, string? outputItemCode, int targetQuantity, string recipeSignature)
    {
        var block = new ScribeBlock(ScribeBlockKind.Craft, "",
            targetItemCode: outputItemCode, targetQuantity: targetQuantity, recipeSignature: recipeSignature);
        return TryInsert(index, block) ? block.TaskId : Guid.Empty;
    }

    /// <summary>Adds an <b>item</b> Link (a reference to an item's Handbook page) to the end.
    /// <paramref name="target"/> is the plain collectible code (may be null). Text and
    /// <see cref="ScribeBlock.LinkLabel"/> start empty; the label is derived live from the item.</summary>
    public bool AddLink(string? target) => InsertLink(_blocks.Count, target);

    /// <summary>Inserts an item Link at <paramref name="index"/>. Out-of-range fails safely.</summary>
    public bool InsertLink(int index, string? target) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Link, "", linkTarget: target));

    /// <summary>Adds a <b>guide-page</b> Link to the end. <paramref name="pageCode"/> is the bare Handbook
    /// page code (stored <c>"page:"</c>-prefixed via <see cref="ScribeLinkTarget.ForPage"/>);
    /// <paramref name="label"/> is the guide's title, captured at creation because a page has no item to
    /// resolve a name from.</summary>
    public bool AddGuideLink(string pageCode, string? label) =>
        InsertGuideLink(_blocks.Count, pageCode, label);

    /// <summary>Inserts a guide-page Link at <paramref name="index"/>. Out-of-range fails safely.</summary>
    public bool InsertGuideLink(int index, string pageCode, string? label) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Link, "",
            linkTarget: ScribeLinkTarget.ForPage(pageCode), linkLabel: label));

    /// <summary>Adds a <b>quest</b> Link to the end. <paramref name="source"/> is the owning backend mod
    /// (e.g. <see cref="ScribeQuestSource.VsQuest"/>/<see cref="ScribeQuestSource.ProgressionFramework"/>);
    /// <paramref name="questCode"/> is the quest's domain-qualified id, stored <c>"quest:"</c>-prefixed via
    /// <see cref="ScribeLinkTarget.ForQuest"/>. <paramref name="title"/> and <paramref name="description"/>
    /// are captured at creation and never re-derived, so the Link renders even if the quest mod is later
    /// uninstalled.</summary>
    public bool AddQuestLink(string source, string questCode, string? title, string? description) =>
        InsertQuestLink(_blocks.Count, source, questCode, title, description);

    /// <summary>Inserts a quest Link at <paramref name="index"/>. Out-of-range fails safely.</summary>
    public bool InsertQuestLink(int index, string source, string questCode, string? title, string? description) =>
        TryInsert(index, new ScribeBlock(ScribeBlockKind.Link, "",
            linkTarget: ScribeLinkTarget.ForQuest(source, questCode), linkLabel: title, linkDescription: description));

    bool TryInsert(int index, ScribeBlock block)
    {
        if (index < 0 || index > _blocks.Count) return false;
        _blocks.Insert(index, block);
        return true;
    }

    /// <summary>
    /// The contiguous depth-1 run owned by the block at <paramref name="parentIndex"/>: half-open
    /// <c>[start, end)</c> from the row after the parent to the first non-depth-1 row (or the document end).
    /// Kind-agnostic. An invalid index or a parent with no depth-1 rows yields an empty run
    /// (<c>start == end</c>). A depth-1 row is a leaf; check <see cref="ScribeBlock.Depth"/> first.
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
    /// Walks backward from a depth-1 row to the depth-0 parent owning its contiguous run. Returns -1 when
    /// the index is invalid, the row is not depth 1, or no depth-0 row sits above it.
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

    /// <summary>Moves the half-open slice <c>[startInclusive, endExclusive)</c> so its first row lands at
    /// <paramref name="destIndex"/> (same convention as <see cref="MoveBlock"/>: an index before the move),
    /// preserving order within the slice. Dropping onto a row inside the slice is a successful no-op.
    /// Returns false for an invalid range or dest.</summary>
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
    /// Moves the half-open slice <c>[startInclusive, endExclusive)</c> to the end as one block, preserving
    /// order. Returns false (unchanged) for an empty/invalid range or one already at the bottom. Keeps
    /// parent and children contiguous for Bound Sink.
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
    /// Removes the half-open slice <c>[startInclusive, endExclusive)</c>. False when empty or out of bounds.
    /// </summary>
    public bool DeleteRange(int startInclusive, int endExclusive)
    {
        if (startInclusive < 0 || endExclusive > _blocks.Count || startInclusive >= endExclusive)
            return false;
        _blocks.RemoveRange(startInclusive, endExclusive - startInclusive);
        return true;
    }

    /// <summary>
    /// Loosely self-heals the ingredient subtasks of the Craft task <paramref name="craftTaskId"/>,
    /// reconciling ONLY its owned depth-1 run against the derived ingredient list:
    /// <list type="bullet">
    /// <item>For each counting <paramref name="ingredients"/> entry, sets the matched child Tracker's
    /// <see cref="ScribeBlock.TargetQuantity"/> to <c>PerCraftQuantity × craftsNeeded</c> (matched by item
    /// code, preserving id and progress). A missing child is inserted at depth 1 only when
    /// <paramref name="createMissing"/> is true (Handbook create-once); the parent-target stepper passes false.</item>
    /// <item>For each non-counting <paramref name="notes"/> entry (e.g. a liquid), ensures a depth-1 Text
    /// note with that exact text exists, creating it only when <paramref name="createMissing"/>.</item>
    /// </list>
    /// It NEVER deletes a row (a player's removal or edit is kept) and never touches anything below depth 1.
    /// New children append to the END of the owned run. Returns false (unchanged) when no Craft block has
    /// that id.
    ///
    /// <para>Expects DISTINCT item codes in <paramref name="ingredients"/> (the Mod layer merges duplicates);
    /// a repeated code would resolve to the first unclaimed match. <paramref name="craftsNeeded"/> comes
    /// from <see cref="ScribeCraftMath.CraftsNeeded"/> and is clamped to ≥ 1 here.</para>
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

        // runEnd is exclusive and grows as children are appended, so they stay at the end of the run.
        var (runStart, runEnd) = OwnedRun(parentIndex);

        var claimed = new HashSet<int>(); // rows matched this pass, so duplicate codes don't double-claim

        foreach (var ing in ingredients)
        {
            // A liquid counts in litres: ceil the batch litre total instead of the integer per-craft multiply.
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
    /// Stepper-only reconcile: rescales <see cref="ScribeBlock.TargetQuantity"/> on item-code matches inside
    /// the owned run. Never inserts, never deletes.
    /// </summary>
    public bool RescaleCraftIngredients(Guid craftTaskId,
        IReadOnlyList<ScribeCraftIngredient> ingredients, IReadOnlyList<string> notes, int craftsNeeded)
        => ReconcileCraftIngredients(craftTaskId, ingredients, notes, craftsNeeded, createMissing: false);

    /// <summary>
    /// Loosely self-heals the <see cref="ScribeBlockKind.QuestObjective"/> subtasks of the quest Link
    /// <paramref name="questLinkTaskId"/>, mirroring <see cref="ReconcileCraftIngredients"/>'s owned-run
    /// shape but matching by the objective's stable <c>Code</c> (via <see cref="ScribeBlock.LinkTarget"/>),
    /// since two objectives could share an item. The parent must be a quest Link
    /// (<see cref="ScribeLinkTarget.IsQuest"/>). A matched child has its TargetQuantity rescaled to
    /// <c>Required</c> (id and progress preserved); when <paramref name="createMissing"/> and no match
    /// exists, a child is appended to the run carrying <c>ItemCode</c> (item icon) and <c>Label</c>
    /// (fallback text). Never deletes a row and never touches anything below depth 1. Returns false
    /// (unchanged) when no matching quest Link exists.
    /// </summary>
    public bool ReconcileQuestObjectives(Guid questLinkTaskId,
        IReadOnlyList<(string Code, string? ItemCode, string? Label, int Required)> objectives,
        bool createMissing = true)
    {
        int parentIndex = -1;
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == questLinkTaskId && _blocks[i].IsLink
                && ScribeLinkTarget.IsQuest(_blocks[i].LinkTarget))
            {
                parentIndex = i;
                break;
            }
        }
        if (parentIndex < 0) return false;

        var (runStart, runEnd) = OwnedRun(parentIndex);
        var claimed = new HashSet<int>(); // rows matched this pass, so a duplicate code can't double-claim

        foreach (var obj in objectives)
        {
            int required = obj.Required < 1 ? 1 : obj.Required;

            int matchIdx = -1;
            for (int i = runStart; i < runEnd; i++)
            {
                if (claimed.Contains(i)) continue;
                if (_blocks[i].IsQuestObjective
                    && string.Equals(_blocks[i].LinkTarget, obj.Code, StringComparison.Ordinal))
                {
                    matchIdx = i;
                    break;
                }
            }

            if (matchIdx >= 0)
            {
                claimed.Add(matchIdx);
                _blocks[matchIdx].TargetQuantity = required; // rescale in place; keep id + CurrentQuantity
            }
            else if (createMissing)
            {
                _blocks.Insert(runEnd, new ScribeBlock(ScribeBlockKind.QuestObjective, "",
                    depth: 1, targetItemCode: obj.ItemCode, targetQuantity: required,
                    linkTarget: obj.Code, linkLabel: obj.Label));
                runEnd++;
            }
        }

        return true;
    }

    /// <summary>
    /// Sets the backend-reported progress (<see cref="ScribeBlock.CurrentQuantity"/>) of the
    /// <see cref="ScribeBlockKind.QuestObjective"/> with the given TaskId. Gated on
    /// <see cref="ScribeBlock.IsQuestObjective"/>, not <see cref="SetTrackerCurrentQuantity"/>'s carried-count
    /// gate, since an objective is never inventory-driven. Progress is clamped into <c>[0, TargetQuantity]</c>
    /// (tighter than the block's ≥ 0 setter) because it cannot exceed the requirement. Returns false
    /// (unchanged) for an unknown id or a non-QuestObjective block.
    /// </summary>
    public bool SetQuestObjectiveProgress(Guid taskId, int currentQuantity)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsQuestObjective)
            {
                _blocks[i].CurrentQuantity = Math.Clamp(currentQuantity, 0, _blocks[i].TargetQuantity);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Changes a block's text, stored verbatim (no trimming; blank allowed). This lets a task go
    /// transiently empty while the player clears it (the field writes through per keystroke), and lets the
    /// live editor keep a just-typed trailing newline long enough for the row to grow. Removing an empty
    /// task and whitespace normalization (the lectern dialog's NormalizeRowOnCommit) are the editing
    /// layer's job. Done flag and kind are unchanged.
    /// </summary>
    public bool SetBlockText(int index, string? text)
    {
        if (!IsValidIndex(index)) return false;
        _blocks[index].Text = text ?? "";
        return true;
    }

    /// <summary>Flips the completed flag of a completable block. Fails on a Text section or a bad index.</summary>
    public bool ToggleTask(int index)
    {
        if (!IsValidIndex(index)) return false;
        var block = _blocks[index];
        if (!block.IsCompletable) return false;
        block.Done = !block.Done;
        if (block.Done && block.Assignment is { } assignment)
            ScribeAssignmentTransitions.TryMarkCompleted(assignment, taskDone: true);
        return true;
    }

    /// <summary>Removes the block at <paramref name="index"/>, preserving order, and reports its
    /// <see cref="ScribeBlock.TaskId"/> so the caller can react (e.g. orphan a pin). On an invalid index
    /// the document is unchanged and <paramref name="deletedTaskId"/> is null.</summary>
    public bool DeleteBlock(int index, out Guid? deletedTaskId)
    {
        if (!IsValidIndex(index))
        {
            deletedTaskId = null;
            return false;
        }
        deletedTaskId = _blocks[index].TaskId;
        if (_blocks[index].Assignment is { State: ScribeAssignmentState.Accepted } assignment)
            ScribeAssignmentTransitions.TryApply(assignment, ScribeAssignmentActor.Assignee,
                ScribeAssignmentAction.Discard);
        _blocks.RemoveAt(index);
        return true;
    }

    /// <summary>Removes the block at <paramref name="index"/>; overload for callers that don't need the id.</summary>
    public bool DeleteBlock(int index) => DeleteBlock(index, out _);

    /// <summary>
    /// Changes the text of the task with the given TaskId (the pin editor's identity-addressed edit).
    /// Unlike <see cref="SetBlockText"/>, blank or whitespace-only <paramref name="text"/> is REJECTED
    /// (returns false, unchanged) so a pin edit can never blank a task. Also false for an unknown id or a
    /// non-task block. Otherwise stored verbatim.
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
    /// Sets the carried count (<see cref="ScribeBlock.CurrentQuantity"/>) of the carried-count-tracked block
    /// (Tracker or Craft parent) with the given TaskId. Clamped only to ≥ 0 by the setter, NOT capped at
    /// <c>TargetQuantity</c> (overflow is meaningful). Returns false (unchanged) for an unknown id or a
    /// block that is not <see cref="ScribeBlock.IsCarriedCountTracked"/>.
    /// </summary>
    public bool SetTrackerCurrentQuantity(Guid taskId, int currentQuantity)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsCarriedCountTracked)
            {
                _blocks[i].CurrentQuantity = currentQuantity; // clamped to ≥ 0 in the setter
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns the block with the given <see cref="ScribeBlock.TaskId"/>, or null.</summary>
    public ScribeBlock? FindByTaskId(Guid taskId)
    {
        foreach (var block in _blocks)
        {
            if (block.TaskId == taskId) return block;
        }
        return null;
    }

    /// <summary>
    /// Moves the task with the given TaskId to the END, preserving the order of all others (the "sink to the
    /// bottom" of the Sink policy). Returns false (unchanged) for an unknown id, a non-completable (Text)
    /// block, or a task already last.
    /// </summary>
    public bool MoveTaskToBottom(Guid taskId)
    {
        for (int i = 0; i < _blocks.Count; i++)
        {
            if (_blocks[i].TaskId == taskId && _blocks[i].IsCompletable)
            {
                if (i == _blocks.Count - 1) return false; // already last
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

    /// <summary>Replaces all blocks in one shot (used by the codec).</summary>
    internal void SetBlocks(IEnumerable<ScribeBlock> blocks)
    {
        _blocks.Clear();
        _blocks.AddRange(blocks);
    }

    /// <summary>Replaces this document's blocks wholesale (order preserved), leaving <see cref="Title"/> and
    /// <see cref="DocId"/> untouched. The import validator's degrade primitive: it rewrites any block whose
    /// Tracker/Link reference doesn't resolve in THIS game as a plain Task, then installs the result here.
    /// Public (unlike the codec-only <see cref="SetBlocks"/>) because the validator lives in the mod assembly.</summary>
    public void ReplaceBlocks(IEnumerable<ScribeBlock> blocks) => SetBlocks(blocks);

    private bool IsValidIndex(int index) => index >= 0 && index < _blocks.Count;
}
