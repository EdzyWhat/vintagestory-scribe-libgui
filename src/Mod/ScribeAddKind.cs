using System;
using System.Collections.Generic;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// One kind the editor footer's add control can create. The picker builds its kind list and button label from
/// <see cref="ScribeAddKinds"/>, and <see cref="ScribeDialogBase.OnClickAdd"/> dispatches to <see cref="Add"/>,
/// so a new kind is one registry entry. Lives in <c>src/Mod/</c>, not Core, because <see cref="Add"/>
/// references mod editing intent.
/// </summary>
/// <param name="Id">Stable identifier (not player-facing).</param>
/// <param name="LabelLangKey">Fully-qualified lang key for the button/list label, e.g. <c>"scribe:scribe-gui-addtask"</c>.</param>
/// <param name="Add">Inserts a block at (document, item code, insert index); returns whether one was added.
/// Item-bound kinds (Tracker/Link) return false on a null item code; Task and Note ignore it.</param>
/// <param name="RequiresItemContext">True for kinds creatable only from a Handbook "Add to Scribe" link that
/// supplies the item code; a bare footer click dispatches a non-mutating guide action instead.</param>
internal sealed record ScribeAddKind(
    string Id,
    string LabelLangKey,
    Func<ScribeDocument, string?, int, bool> Add,
    bool RequiresItemContext = false);

/// <summary>The registry of kinds the add picker offers, in menu order.</summary>
internal static class ScribeAddKinds
{
    /// <summary>A checkbox task; the DEFAULT add. Ignores the item code.</summary>
    public static readonly ScribeAddKind Task = new(
        Id: "task",
        LabelLangKey: "scribe:scribe-gui-addtask",
        Add: (doc, _, at) => doc.InsertTask(at, ""));

    /// <summary>A freeform note (<see cref="ScribeBlockKind.Text"/>, no checkbox). Counts against a finite
    /// tier's cap like every kind. Ignores the item code.</summary>
    public static readonly ScribeAddKind Note = new(
        Id: "note",
        LabelLangKey: "scribe:scribe-gui-addtext",
        Add: (doc, _, at) => doc.InsertTextSection(at, ""));

    /// <summary>An item Tracker bound to an item code; <c>have/need</c> follows the viewer's carried inventory.
    /// Requires item context; target quantity defaults to 1.</summary>
    public static readonly ScribeAddKind Tracker = new(
        Id: "tracker",
        LabelLangKey: "scribe:scribe-gui-addtracker",
        Add: (doc, code, at) => code is not null && doc.InsertTracker(at, code, 1),
        RequiresItemContext: true);

    /// <summary>An item Link whose label opens the item's Handbook page. Requires item context, like
    /// <see cref="Tracker"/>.</summary>
    public static readonly ScribeAddKind Link = new(
        Id: "link",
        LabelLangKey: "scribe:scribe-gui-addlink",
        Add: (doc, code, at) => code is not null && doc.InsertLink(at, code),
        RequiresItemContext: true);

    /// <summary>A Crafting Task bound to a recipe. Requires item context: the recipe SIGNATURE comes from the
    /// Handbook link via <c>ScribeDialogBase.TryAddCraftFromHandbook</c>, so <see cref="ScribeAddKind.Add"/>
    /// always returns false and the footer dispatches the Handbook guide.</summary>
    public static readonly ScribeAddKind Craft = new(
        Id: "craft",
        LabelLangKey: "scribe:scribe-gui-addcraft",
        Add: (_, _, _) => false,
        RequiresItemContext: true);

    /// <summary>The kinds in picker order; the first (<see cref="Task"/>) is the initial primary-button default.</summary>
    public static readonly IReadOnlyList<ScribeAddKind> Live = new[] { Task, Tracker, Craft, Link, Note };
}
