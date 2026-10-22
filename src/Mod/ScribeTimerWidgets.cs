using Gui.Core.Layout;
using Gui.Rendering.Text;
using Gui.Widgets.Animations;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>Self-ticking countdown display, seeded from <paramref name="initialSeconds"/>. Decrements without a
/// parent ForceRebuild so siblings (the Stop button) are not remounted. InGame timers drain at the world's
/// in-game time rate (read live each tick), RealTime at one per real second, matching the server.</summary>
internal sealed class ScribeCountdownText : StatefulWidget
{
    public readonly double InitialSeconds;
    public readonly TextStyle Style;
    public readonly ICoreClientAPI Capi;
    public readonly TimerMode Mode;
    public readonly ScribeModSystem ModSystem;

    public ScribeCountdownText(double initialSeconds, TextStyle style, ICoreClientAPI capi, TimerMode mode, ScribeModSystem modSystem)
    { InitialSeconds = initialSeconds; Style = style; Capi = capi; Mode = mode; ModSystem = modSystem; }

    public override State CreateState() => new ScribeCountdownTextState();
}

internal sealed class ScribeCountdownTextState : State<ScribeCountdownText>
{
    private double _remaining;

    public override void InitState()
    {
        base.InitState();
        _remaining = Widget.InitialSeconds;
        // Decrement and repaint off the mod system's shared 1Hz tick (see ScribeModSystem.TimerDisplayTick), so
        // this countdown and the HUD timer row stay in the same dispatch.
        Widget.ModSystem.TimerDisplayTick += OnDisplayTick;
    }

    private void OnDisplayTick()
    {
        double rate = Widget.Mode == TimerMode.InGame ? ScribeTimeRate.InGamePerReal(Widget.Capi) : 1.0;
        SetState(() => _remaining = Math.Max(0, _remaining - rate));
    }

    public override void Dispose()
    {
        Widget.ModSystem.TimerDisplayTick -= OnDisplayTick;
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
    {
        int total = (int)Math.Max(0, _remaining);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        string text = h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
        return new Gui.Widgets.Basic.Text(text, Widget.Style);
    }
}

// ScribeTimerModeRow

/// <summary>
/// A self-stateful pair of RadioButtons for In-game / Real-time mode. Owns its SetState so toggling does not
/// redraw the sibling Button (remounting it would crash ButtonState.PlaySound).
/// </summary>
internal sealed class ScribeTimerModeRow : StatefulWidget
{
    public readonly TimerMode Selected;
    public readonly Action<TimerMode> OnSelect;
    public readonly TextStyle BodyStyle;
    public readonly float CheckboxSize;

    public ScribeTimerModeRow(TimerMode selected, Action<TimerMode> onSelect, TextStyle bodyStyle, float checkboxSize)
    { Selected = selected; OnSelect = onSelect; BodyStyle = bodyStyle; CheckboxSize = checkboxSize; }

    public override State CreateState() => new ScribeTimerModeRowState();
}

internal sealed class ScribeTimerModeRowState : State<ScribeTimerModeRow>
{
    private int _current;

    public override void InitState()
    {
        base.InitState();
        _current = (int)Widget.Selected;
    }

    private void Select(int mode)
    {
        if (_current == mode) return;
        SetState(() => _current = mode);
        Widget.OnSelect((TimerMode)mode);
    }

    public override Widget Build(BuildContext context)
    {
        // Theme radio style; labels use BodyStyle, which carries the player's task font.
        var radioStyle = Theme.Of(context).RadioButtonStyle with
        {
            LabelStyle = Widget.BodyStyle,
        };

        // Vertical stack: a horizontal row of both Caudex-Bold labels overflows the narrow page and pushes
        // "Real time" behind the Settings nav button, which swallows its clicks.
        return new Column(
            spacing: 8,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: new Widget[]
            {
                new RadioButton<int>(
                    value: (int)TimerMode.RealTime,
                    groupValue: _current,
                    onChanged: Select,
                    label: Lang.Get("scribe:scribe-gui-timer-mode-realtime"),
                    size: Widget.CheckboxSize,
                    style: radioStyle),
                new RadioButton<int>(
                    value: (int)TimerMode.InGame,
                    groupValue: _current,
                    onChanged: Select,
                    label: Lang.Get("scribe:scribe-gui-timer-mode-ingame"),
                    size: Widget.CheckboxSize,
                    style: radioStyle),
            });
    }
}

// ScribeBlinkText

/// <summary>
/// Blinks text at 500 ms via a VS game tick listener; avoids AnimationController + Element.Owner, whose
/// mount-order dependency can silently crash in the shipped LibGUI build.
/// </summary>
internal sealed class ScribeBlinkText : StatefulWidget
{
    public readonly string Text;
    public readonly TextStyle Style;
    public readonly ICoreClientAPI Capi;

    public ScribeBlinkText(string text, TextStyle style, ICoreClientAPI capi)
    { Text = text; Style = style; Capi = capi; }

    public override State CreateState() => new ScribeBlinkTextState();
}

internal sealed class ScribeBlinkTextState : State<ScribeBlinkText>
{
    private bool _visible = true;
    private long _tickId;

    public override void InitState()
    {
        base.InitState();
        _tickId = Widget.Capi.Event.RegisterGameTickListener(_ => SetState(() => _visible = !_visible), 500);
    }

    public override void Dispose()
    {
        if (_tickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_tickId); _tickId = 0; }
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
        => new AnimatedOpacity(
            opacity: _visible ? 1f : 0f,
            duration: TimeSpan.FromMilliseconds(50),
            child: new Gui.Widgets.Basic.Text(Widget.Text, Widget.Style));
}

// ScribeTimerIcon

/// <summary>
/// Oscillates the timer icon +-30 degrees via AnimatedRotation on a game tick listener. AnimatedRotation is used
/// instead of Transform.Rotate to avoid a Skia matrix NaN/GPU crash when the icon size is zero at first layout.
/// </summary>
internal sealed class ScribeTimerIcon : StatefulWidget
{
    public readonly float Size;
    public readonly Vector4 Color;
    public readonly ICoreClientAPI Capi;

    public ScribeTimerIcon(float size, Vector4 color, ICoreClientAPI capi)
    { Size = size; Color = color; Capi = capi; }

    public override State CreateState() => new ScribeTimerIconState();
}

internal sealed class ScribeTimerIconState : State<ScribeTimerIcon>
{
    private static readonly float AngleA =  MathF.PI / 6f;  // +30°
    private static readonly float AngleB = -MathF.PI / 6f;  // -30°
    private float _angle = MathF.PI / 6f;
    private long _tickId;

    public override void InitState()
    {
        base.InitState();
        // Toggle every 100ms between +30 and -30 degrees.
        _tickId = Widget.Capi.Event.RegisterGameTickListener(_ =>
            SetState(() => _angle = _angle > 0 ? AngleB : AngleA), 100);
    }

    public override void Dispose()
    {
        if (_tickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_tickId); _tickId = 0; }
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
        => new AnimatedRotation(
            angle: _angle,
            duration: TimeSpan.FromMilliseconds(100),
            curve: Curves.EaseInOutBack,
            child: new ScribeVsIconGlyph("scribetimer", Widget.Size, Widget.Color));
}
