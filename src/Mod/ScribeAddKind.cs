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
/// <c>CanAddTaskUnderPolicy()</c> (tasks). False for notes, which are uncapped (design D4).</param>
/// <param name="Add">Appends a block of this kind to the scratch document; returns whether a block was
/// added (mirrors <see cref="ScribeDocument.AddTask"/> / <see cref="ScribeDocument.AddTextSection"/>).</param>
internal sealed record ScribeAddKind(
    string Id,
    string LabelLangKey,
    bool CountsAgainstTaskCap,
    Func<ScribeDocument, bool> Add);

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
    /// cap (scribe-document-policy); this is the DEFAULT add so one click still adds a task.</summary>
    public static readonly ScribeAddKind Task = new(
        Id: "task",
        LabelLangKey: "scribe:scribe-gui-addtask",
        CountsAgainstTaskCap: true,
        Add: doc => doc.AddTask(""));

    /// <summary>A freeform note — a <see cref="ScribeBlockKind.Text"/> block with no checkbox and no
    /// completion state. Uncapped: the tablet cap is task-scoped, so a note never trips "tablet full"
    /// (design D4). Reuses the existing <c>scribe-gui-addtext</c> label ("Add Note").</summary>
    public static readonly ScribeAddKind Note = new(
        Id: "note",
        LabelLangKey: "scribe:scribe-gui-addtext",
        CountsAgainstTaskCap: false,
        Add: doc => doc.AddTextSection(""));

    /// <summary>The kinds this release offers, in the order they appear in the picker's inline list. The
    /// first entry is the initial default for the primary button.</summary>
    public static readonly IReadOnlyList<ScribeAddKind> Live = new[] { Task, Note };
}
