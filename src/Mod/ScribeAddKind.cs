using System;
using System.Collections.Generic;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// One kind the editor footer's add control can create (add-note-kind-picker, spec
/// <c>task-kind-picker</c>): its stable id, the lang key for its menu/button label, whether it counts
/// against the tablet's task cap (scribe-document-policy), and the delegate that appends it to the
/// scratch document. The picker builds its inline kind list and its primary-button label from the
/// registry (<see cref="ScribeAddKinds"/>), and <see cref="ScribeDialogBase.OnClickAdd"/> dispatches to
/// <see cref="Add"/> — so adding a future kind (Tracked/Linked) is one registry entry, with the footer
/// widget and the other kinds untouched (design D2).
///
/// <para>This lives in <c>src/Mod/</c>, not Core: <see cref="Add"/> references the mod's editing intent
/// (and future kinds reach VS-API item pickers). The Core model already stores both block kinds; this
/// is only the UI entry point that was missing.</para>
/// </summary>
/// <param name="Id">Stable identifier (not player-facing); used for equality/diagnostics.</param>
/// <param name="LabelLangKey">Fully-qualified lang key for the label shown on the primary button and in
/// the kind list, e.g. <c>"scribe:scribe-gui-addtask"</c>.</param>
/// <param name="CountsAgainstTaskCap">True if adding this kind is gated by
/// <c>CanAddTaskUnderPolicy()</c> (tasks + trackers). False for notes and links, which are uncapped
/// (design D4 — the cap is task-scoped).</param>
/// <param name="Add">Appends a block of this kind to the scratch document; returns whether a block was
/// added (mirrors <see cref="ScribeDocument.AddTask"/> / <see cref="ScribeDocument.AddTextSection"/>).
/// The second parameter is the target item code (e.g. <c>"game:ingot-copper"</c>) for the item-bound
/// kinds (Tracker/Link); Task and Note ignore it. Kinds with <see cref="RequiresItemContext"/> return
/// false when it is null (there is nothing to track/link), so a footer click without an item is a
/// safe no-op that the caller turns into a guide action instead.</param>
/// <param name="RequiresItemContext">True for kinds that only make sense against a specific item
/// (Tracker/Link): they cannot be created from a bare footer click, only from a Handbook page's
/// "Add to Scribe" link that supplies the item code. The editor footer detects this and dispatches a
/// non-mutating guide action (open the explainer / point at the Handbook link) instead of adding a row
/// (add-tracker-link-tasks 3.7). Task and Note are false — the ordinary immediate add.</param>
internal sealed record ScribeAddKind(
    string Id,
    string LabelLangKey,
    bool CountsAgainstTaskCap,
    Func<ScribeDocument, string?, bool> Add,
    bool RequiresItemContext = false);

/// <summary>
/// The live registry of kinds the add picker offers, in menu order (add-note-kind-picker D2). This
/// interim release registers exactly two — <see cref="Task"/> and <see cref="Note"/>; Tracked/Linked are
/// deliberately ABSENT (not stubbed as dead options), per spec <c>task-kind-picker</c> ("This release
/// offers exactly Task and Note"). A future kind is added by registering a new <see cref="ScribeAddKind"/>
/// in <see cref="Live"/>; the footer contract does not change.
/// </summary>
internal static class ScribeAddKinds
{
    /// <summary>A checkbox task — the historical "Add task" behavior. Counts against the tablet's 10-task
    /// cap (scribe-document-policy); this is the DEFAULT add so one click still adds a task. Ignores the
    /// item-code param (a task has no bound item).</summary>
    public static readonly ScribeAddKind Task = new(
        Id: "task",
        LabelLangKey: "scribe:scribe-gui-addtask",
        CountsAgainstTaskCap: true,
        Add: (doc, _) => doc.AddTask(""));

    /// <summary>A freeform note — a <see cref="ScribeBlockKind.Text"/> block with no checkbox and no
    /// completion state. Uncapped: the tablet cap is task-scoped, so a note never trips "tablet full"
    /// (design D4). Reuses the existing <c>scribe-gui-addtext</c> label ("Add Note"). Ignores the
    /// item-code param.</summary>
    public static readonly ScribeAddKind Note = new(
        Id: "note",
        LabelLangKey: "scribe:scribe-gui-addtext",
        CountsAgainstTaskCap: false,
        Add: (doc, _) => doc.AddTextSection(""));

    /// <summary>An item Tracker — a <see cref="ScribeBlockKind.Tracker"/> block bound to a specific item
    /// code, whose <c>have/need</c> counter follows the viewer's carried inventory (add-tracker-link-tasks).
    /// Counts against the task cap (it completes like a task). <see cref="ScribeAddKind.RequiresItemContext"/>
    /// is true: it can only be created from a Handbook page's "Add to Scribe" link (which supplies the item
    /// code), never from a bare footer click — so <see cref="ScribeAddKind.Add"/> is a no-op when the code is
    /// null. Target quantity defaults to 1 (the row's stepper edits it afterward).</summary>
    public static readonly ScribeAddKind Tracker = new(
        Id: "tracker",
        LabelLangKey: "scribe:scribe-gui-addtracker",
        CountsAgainstTaskCap: true,
        Add: (doc, code) => code is not null && doc.AddTracker(code, 1),
        RequiresItemContext: true);

    /// <summary>An item Link — a <see cref="ScribeBlockKind.Link"/> block whose label opens the referenced
    /// item's Handbook page on click (add-tracker-link-tasks). Uncapped (like a note, it is not a completable
    /// task under the cap). <see cref="ScribeAddKind.RequiresItemContext"/> is true for the same reason as
    /// <see cref="Tracker"/> — the item code comes from the Handbook link, so <see cref="ScribeAddKind.Add"/>
    /// no-ops on a null code.</summary>
    public static readonly ScribeAddKind Link = new(
        Id: "link",
        LabelLangKey: "scribe:scribe-gui-addlink",
        CountsAgainstTaskCap: false,
        Add: (doc, code) => code is not null && doc.AddLink(code),
        RequiresItemContext: true);

    /// <summary>The kinds this release offers, in the order they appear in the picker's inline list. The
    /// first entry is the initial default for the primary button. Tracker and Link appear after the two
    /// freeform kinds; because they <see cref="ScribeAddKind.RequiresItemContext"/>, a footer click on them
    /// dispatches a guide action (open the explainer / point at the Handbook link) rather than adding a row
    /// (add-tracker-link-tasks 3.7).</summary>
    public static readonly IReadOnlyList<ScribeAddKind> Live = new[] { Task, Note, Tracker, Link };
}
