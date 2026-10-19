using System;
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, GlobalKey

namespace Scribe;

/// <summary>
/// <summary>
/// The dialog's single persistent-root body. LibGUI calls a dialog's <c>Build()</c> once per open, so wrapping the whole body in
/// this <see cref="StatefulWidget"/> and reconciling via <see cref="BodyState.Rebuild"/> (a no-op <c>SetState</c>) reuses the live
/// element tree (rows, each <c>ScribeMultilineField</c> State, the scroll controller) instead of destroying it. <c>ForceRebuild()</c>
/// is reserved for genuinely-new-tree cases (view switches, fresh editor seed, lost-lock recovery).
/// <para>The content is a <see cref="Func{Widget}"/> (<c>ScribeDialogBase.BuildBodyTree</c>) re-invoked on every build so it re-reads
/// live dialog state. The dialog reaches this State through a <see cref="GlobalKey"/> held as a field (never allocated in <c>Build</c>);
/// the key re-registers on mount, so it survives <c>ForceRebuild</c>.</para>
/// </summary>
/// </summary>
internal sealed class ScribeDialogBody : StatefulWidget
{
    public ScribeDialogBody(GlobalKey key, Func<Widget> buildBody) : base(key)
    {
        BuildBody = buildBody;
    }

    /// <summary>Produces the dialog body subtree, re-invoked on every reconcile; owned by the dialog (a method group).</summary>
    public Func<Widget> BuildBody { get; }

    public override State CreateState() => new BodyState();

    internal sealed class BodyState : State<ScribeDialogBody>
    {
        public override Widget Build(BuildContext context) => Widget.BuildBody();

        /// <summary>Reconciles the body in place: an empty <c>SetState</c> re-runs <see cref="Build"/> without the full unmount of a <c>ForceRebuild</c>.</summary>
        public void Rebuild() => SetState(() => { });
    }
}
