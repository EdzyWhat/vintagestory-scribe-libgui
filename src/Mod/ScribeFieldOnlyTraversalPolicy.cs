using System;
using System.Collections.Generic;
using Gui.Widgets.Framework;    // Element
using Gui.Widgets.Input;        // FocusTraversalPolicy, FocusNode, FocusManager

namespace Scribe;

/// <summary>An allow-list <see cref="FocusTraversalPolicy"/> installed on the Lectern dialog's
/// <see cref="Gui.GuiBase.FocusManager"/> so Tab / Shift+Tab visit ONLY the dialog's own editable text
/// fields, in row order — never a row's completion checkbox.
///
/// <para><b>Why this exists (gui@3.1.0 regression):</b> before 3.1.0 LibGUI had no focus-traversal engine
/// at all, so the Scribe editor rolled its own Tab handling and row checkboxes were plain, non-focusable
/// widgets that Tab could never reach. 3.1.0 changed two things: <c>CheckboxState</c> now derives from
/// <c>FocusableState</c> and lazily owns a <see cref="FocusNode"/> (so every mounted checkbox is focusable),
/// and <c>GuiBase.OnKeyDown</c> now intercepts Tab globally and walks
/// <c>FocusManager.TraversalPolicy.GetTraversalOrder(...)</c>. The default
/// <c>ReadingOrderTraversalPolicy</c> collects every focus node in the tree — including the new checkbox
/// nodes — so Tab began stopping on each row's checkbox before its text field, doubling the keystrokes to
/// move between rows. The <c>Checkbox</c> widget exposes no public seam to mark its internal node
/// non-traversable, so per-checkbox exclusion isn't possible; an allow-list of the fields we DO want is.</para>
///
/// <para>The dialog already owns the complete, row-ordered set of field focus nodes it wants Tab to visit
/// (<c>editorFocusNodes</c> in the Editor view, the pin rows' nodes in the Pinned view). This policy simply
/// returns that live list via a delegate the dialog supplies, so it tracks row add/remove/reorder with no
/// second bookkeeping path. Anything not in the list — checkboxes now, and any future focusable control we
/// don't opt in — is never returned, so Tab can't land on it. This is strictly more robust than a
/// checkbox-specific deny-list. See <c>VSAPI-NOTES.md</c> (§LibGUI) and the
/// <c>exclude-checkboxes-from-tab-focus</c> change design for the full rationale.</para></summary>
internal sealed class ScribeFieldOnlyTraversalPolicy : FocusTraversalPolicy
{
    /// <summary>Supplies the current view's ordered, mounted field focus nodes. Called fresh on every Tab
    /// press (LibGUI re-reads the order each <c>FocusNext</c>/<c>FocusPrevious</c>), so it always reflects
    /// the live row set and the active view.</summary>
    private readonly Func<IReadOnlyList<FocusNode>> getFieldNodes;

    public ScribeFieldOnlyTraversalPolicy(Func<IReadOnlyList<FocusNode>> getFieldNodes)
    {
        this.getFieldNodes = getFieldNodes ?? throw new ArgumentNullException(nameof(getFieldNodes));
    }

    public override IReadOnlyList<FocusNode> GetTraversalOrder(Element root, FocusManager manager)
        => getFieldNodes();
}
