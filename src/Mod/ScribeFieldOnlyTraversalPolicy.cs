using System;
using System.Collections.Generic;
using Gui.Widgets.Framework;    // Element
using Gui.Widgets.Input;        // FocusTraversalPolicy, FocusNode, FocusManager

namespace Scribe;

/// <summary>An allow-list <see cref="FocusTraversalPolicy"/> on the Lectern dialog's
/// <see cref="Gui.GuiBase.FocusManager"/> so Tab / Shift+Tab visit ONLY the dialog's editable text fields, in
/// row order, never a row's completion checkbox.
///
/// <para>In gui 3.1.0+ every mounted <c>Checkbox</c> owns a focus node, and the default
/// <c>ReadingOrderTraversalPolicy</c> collects them all, doubling the Tab presses between rows. Checkbox has no
/// public seam to mark its node non-traversable, so the policy returns the dialog's live row-ordered field
/// list (supplied by delegate, so it tracks add/remove/reorder) and nothing else. See <c>VSAPI-NOTES.md</c>
/// (LibGUI section).</para></summary>
internal sealed class ScribeFieldOnlyTraversalPolicy : FocusTraversalPolicy
{
    /// <summary>Supplies the current view's ordered, mounted field focus nodes. Called fresh on every Tab press,
    /// so it reflects the live row set and active view.</summary>
    private readonly Func<IReadOnlyList<FocusNode>> getFieldNodes;

    public ScribeFieldOnlyTraversalPolicy(Func<IReadOnlyList<FocusNode>> getFieldNodes)
    {
        this.getFieldNodes = getFieldNodes ?? throw new ArgumentNullException(nameof(getFieldNodes));
    }

    public override IReadOnlyList<FocusNode> GetTraversalOrder(Element root, FocusManager manager)
        => getFieldNodes();
}
