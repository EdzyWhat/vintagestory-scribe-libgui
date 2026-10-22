using Gui.Rendering;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Painting;
using OpenTK.Mathematics;

namespace Scribe;

/// <summary>The HUD header's settings gear: an icon button that brightens +10 HSV Value on hover and opens
/// settings on tap. Stateful so hover survives rebuilds (like <see cref="ScribeFadeText"/>). The nudge is in em
/// units off <see cref="FontSize"/>; the y-offset is a <c>Transform.Translate</c> around the GestureDetector so
/// the clickable region moves with the paint.</summary>
internal sealed class ScribeHudGearButton : StatefulWidget
{
    public ScribeHudGearButton(Vector4 baseColor, float fontSize, Action onTap, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        BaseColor = baseColor;
        FontSize = fontSize;
        OnTap = onTap;
    }

    public Vector4 BaseColor { get; }
    public float FontSize { get; }
    public Action OnTap { get; }

    public override State CreateState() => new ScribeHudGearButtonState();
}

internal sealed class ScribeHudGearButtonState : State<ScribeHudGearButton>
{
    private bool hovered;

    public override Widget Build(BuildContext context)
    {
        // Hover: +10 HSV Value, matching the lectern nav/row buttons.
        var color = hovered
            ? ScribeRowConstants.ShiftBrightness(Widget.BaseColor, +10f)
            : Widget.BaseColor;

        float em = Widget.FontSize;
        float iconSize = 0.75f * em;    // 12px at the tuned 16px baseline
        float leftPad = 0.0625f * em;   // 1px at baseline
        float offsetX = -0.0625f * em;  // 1px at baseline
        // Down nudge of 0.0625em (1px at baseline).
        float offsetY = 0.0625f * em;

        // VsIcon has no text glow, so a Container BoxShadow (black, zero-offset, blurred, as on the lectern nav buttons) halos the icon's box.
        var haloed = new Container(
            new BoxStyle
            {
                BoxShadows = new[]
                {
                    new BoxShadow(
                        Color: new Vector4(0f, 0f, 0f, 0.3f),
                        Offset: new Vector2(0f, 0f),
                        BlurRadius: 2.25f),
                },
            },
            new VsIcon("scribegearhud", iconSize, color));   // HUD-only gear-ring (dialogs keep the solid "scribegear")

        return Transform.Translate(
            new MouseRegion(
                onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
                onExit: _ => { if (hovered) SetState(() => hovered = false); },
                child: new GestureDetector(
                    onTap: _ => Widget.OnTap(),
                    child: new Padding(
                        EdgeInsets.Only(left: leftPad),
                        child: haloed))),
            new Vector2(offsetX, offsetY));
    }
}
