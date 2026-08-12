using System;
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, GlobalKey

namespace Scribe;

/// <summary>
/// The dialog's single persistent-root body (reconcile-animating-surfaces §3.1). LibGUI's
/// <c>GuiBase</c> calls a dialog's <c>Build()</c> exactly ONCE per open (<c>GuiBase.BuildRootTree</c>);
/// the whole tree then persists until <c>ForceRebuild()</c> unmounts it. Wrapping the entire dialog
/// body in this one <see cref="StatefulWidget"/> and reconciling it in place via <see cref="BodyState.Rebuild"/>
/// (a no-op <c>SetState</c>) lets a repaint REUSE the live element tree — the central editor content, its
/// rows, and each row's <c>ScribeMultilineField</c> State — instead of destroying and rebuilding it. A
/// reused field keeps its caret + unsaved buffer; a reused row keeps its hover; the shared scroll
/// controller keeps its offset. This is the mechanism that replaces <c>ForceRebuild()</c> for every
/// in-place update (structural row edits AND chrome repaints), leaving <c>ForceRebuild()</c> only for the
/// genuinely-new-tree cases (view switches, fresh editor seed, lost-lock recovery — reconcile-animating-surfaces §3.3).
///
/// <para>The body content is supplied as a <see cref="Func{Widget}"/> the dialog owns
/// (<c>ScribeDialogBase.BuildBodyTree</c>), re-invoked on every <see cref="BodyState.Build"/> so it always
/// re-reads the dialog's live state (<c>scratch</c>, <c>viewMode</c>, the pin cache, title-edit flag). The
/// dialog reaches the live State through a <see cref="GlobalKey"/> held once as a field (never allocated in
/// <c>Build</c>), so <c>bodyKey.CurrentState&lt;BodyState&gt;()?.Rebuild()</c> triggers the in-place
/// reconcile from any mutation handler. The key re-registers itself on mount, so it stays valid across a
/// <c>ForceRebuild</c> too.</para>
/// </summary>
internal sealed class ScribeDialogBody : StatefulWidget
{
    public ScribeDialogBody(GlobalKey key, Func<Widget> buildBody) : base(key)
    {
        BuildBody = buildBody;
    }

    /// <summary>Produces the dialog body subtree, re-invoked on every reconcile so it reflects the
    /// dialog's current live state. Owned by the dialog (a method group), not this widget.</summary>
    public Func<Widget> BuildBody { get; }

    public override State CreateState() => new BodyState();

    internal sealed class BodyState : State<ScribeDialogBody>
    {
        public override Widget Build(BuildContext context) => Widget.BuildBody();

        /// <summary>Reconcile the body in place: an empty <c>SetState</c> marks this element dirty so the
        /// next <c>BuildOwner.BuildDirtyElements()</c> pass re-runs <see cref="Build"/> and reconciles the
        /// subtree (reusing matching elements/State), WITHOUT the full unmount a <c>ForceRebuild</c> does.</summary>
        public void Rebuild() => SetState(() => { });
    }
}
