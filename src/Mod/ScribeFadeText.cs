using Gui.Rendering.Text;
using Gui.Widgets.Animations;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Painting;

namespace Scribe;

/// <summary>
/// Text that fades 1→0 over <paramref name="durationMs"/> when <c>fading</c> is set, via a self-owned
/// <see cref="AnimationController"/>.
///
/// <para>Not <see cref="AnimatedOpacity"/>: a freshly created implicitly-animated widget evaluates its tween
/// to the target instantly, so the text would snap to 0.</para>
///
/// <para><b>Reconcile-safety.</b> The HUD reuses row elements across rebuilds, so <see cref="InitState"/> does
/// not re-run when <see cref="Fading"/> flips <c>false→true</c>. The fade is therefore synced from
/// <c>SyncFadeController</c> in both <c>InitState</c> and <see cref="UpdateWidget"/>; it is idempotent, so a
/// repaint doesn't restart the ramp.</para>
/// </summary>
internal sealed class ScribeFadeText : StatefulWidget
{
    public ScribeFadeText(bool fading, int durationMs, string text, TextStyle style, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        Fading = fading;
        DurationMs = durationMs;
        TextContent = text;
        Style = style;
    }

    public bool Fading { get; }
    public int DurationMs { get; }
    public string TextContent { get; }
    public TextStyle Style { get; }

    public override State CreateState() => new ScribeFadeTextState();
}

internal sealed class ScribeFadeTextState : State<ScribeFadeText>
{
    private AnimationController? controller;

    public override void InitState()
    {
        base.InitState();
        // Fresh mount already fading (e.g. a newly added destructive-pending row).
        SyncFadeController();
    }

    public override void UpdateWidget(ScribeFadeText oldWidget)
    {
        base.UpdateWidget(oldWidget);
        // Reused element: InitState didn't re-run, so reconcile a Fading flip here (false→true starts, true→false clears on undo).
        SyncFadeController();
    }

    /// <summary>Syncs the fade controller with <see cref="ScribeFadeText.Fading"/> in both directions. Fading
    /// starts the ramp if not already running. Not fading DISPOSES any controller so <see cref="Build"/> returns to
    /// full opacity; without this, an undo late in the window would leave a reused element's stale controller
    /// holding the text at ~0 opacity (a "checkbox with no text").</summary>
    private void SyncFadeController()
    {
        if (Widget.Fading)
        {
            if (controller != null) return; // already fading

            // Ramp 0→1 over the window (opacity = 1 − value), repainting via MarkNeedsBuild.
            controller = new AnimationController(TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
            controller.OnValueChanged += _ => Element.MarkNeedsBuild();
            controller.Forward();
        }
        else
        {
            // Undo / un-fade: snap back to full opacity. Safe here: runs in the build phase, not inside the ticker callback.
            controller?.Dispose();
            controller = null;
        }
    }

    public override Widget Build(BuildContext context)
    {
        float opacity = controller == null ? 1f : 1f - (float)controller.Value;
        return new Opacity(opacity, new Text(Widget.TextContent, Widget.Style));
    }

    public override void Dispose()
    {
        controller?.Dispose();
        controller = null;
        base.Dispose();
    }
}
