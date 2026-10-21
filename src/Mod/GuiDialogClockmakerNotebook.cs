using System;
using System.Collections.Generic;
using Gui.Core.Framework;
using Gui.Core.Layout;
using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Animations;
using Gui.Widgets.Basic;
using Gui.Widgets.Events;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Overlay;
using Gui.Widgets.Painting;
using Gui.Widgets.Scroll;
using OpenTK.Mathematics;
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Alignment = Gui.Widgets.Layout.Alignment;

namespace Scribe;

/// <summary>
/// The Clockmaker's Notebook dialog: <see cref="GuiDialogScribeNotebook"/> plus a Timer tab; other tabs are inherited.
/// </summary>
public sealed class GuiDialogClockmakerNotebook : GuiDialogScribeNotebook
{
    // Timer tab state

    /// <summary>Client-interpolated remaining seconds, for smooth display between server pushes.</summary>
    private double _localRemaining;

    /// <summary>Reseed _localRemaining from MyTimer on the next rebuild (set when a server push arrives).</summary>
    private bool _resyncRemaining = true;

    // Seeded from the remembered PreferredTimerMode in the ctor body, not here: field initializers run before
    // base(...) assigns modSystem. Persisted back on every selection.
    private TimerMode _pendingMode;
    private int _pendingHours;
    private int _pendingMinutes;
    private int _pendingSecs;
    private string _pendingLabel = "";

    private readonly ScribeNumericFocusRegistry _timerFocus = new();

    /// <summary>Last-seen timer status; ForceRebuild runs only on a status transition, not on every server push.</summary>
    private TimerStatus _lastKnownStatus = TimerStatus.Idle;

    /// <summary>Monotonic-clock timestamps (ms, <c>capi.World.ElapsedMilliseconds</c>) of the last Idle-to-Running
    /// (engage) and to-Fired (lock) transitions; 0 = not yet. Held on the host because the gearworks State is torn
    /// down on every transition, which would reset the slide/lock.</summary>
    private long _engageStartMs;
    private long _fireLockMs;

    public GuiDialogClockmakerNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(host, capi)
    {
        // Seed the form's mode from the player's remembered choice.
        _pendingMode = modSystem.MySettings.PreferredTimerMode;
        modSystem.MyTimerChanged += OnTimerChanged;
    }

    public override void OnGuiClosed()
    {
        modSystem.MyTimerChanged -= OnTimerChanged;
        _timerFocus.Dispose();
        base.OnGuiClosed();
    }

    // Extra nav button

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        // History (inherited)
        foreach (var w in base.GetExtraNavButtons()) yield return w;

        // Timer
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribetimer",
            "scribe-gui-nav-timer",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToTimer,
            boxShadows: NavButtonShadow,
            activeColor: IsTimerView ? ScribeRowConstants.NavActiveTimer : null);
    }

    // Timer tab content

    protected override Widget BuildTimerContent()
    {
        var timer  = modSystem.MyTimer;
        var status = timer?.Status ?? TimerStatus.Idle;

        var colors    = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float scale   = ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float body    = ScribeRowConstants.BaseWindowFontSize * scale;
        float big     = body * 2.0f;
        float small   = body * 0.8f;
        string taskFont = ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily);

        // bodyStyle keeps its explicit FontFamily: it feeds the label TextField and numeric steppers, which do not
        // read the DefaultTextStyle ancestor. big/label/small go only into Text widgets and inherit the family.
        var bodyStyle  = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, body),  Color = colors.OnSurface, FontFamily = taskFont };
        var bigStyle   = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, big),   Color = colors.OnSurface };
        var labelStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, body),  Color = colors.OnSurface with { W = colors.OnSurface.W * 0.7f } };
        var smallStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, small), Color = colors.OnSurfaceVariant };

        if (_resyncRemaining && timer is not null)
        {
            _localRemaining = timer.RemainingSeconds;
            _resyncRemaining = false;
        }

        Widget content;

        if (status == TimerStatus.Idle)
        {
            content = BuildSetTimerForm(colors, bodyStyle, smallStyle, scale);
        }
        else
        {
            // Running or Fired: countdown (or 00:00) + Stop. ScribeCountdownText self-ticks, so no ForceRebuild.
            Widget timeWidget = status == TimerStatus.Fired
                ? new ScribeBlinkText("00:00", bigStyle, capi)
                : new ScribeCountdownText(
                    initialSeconds: _resyncRemaining ? (timer?.RemainingSeconds ?? _localRemaining) : _localRemaining,
                    style: bigStyle,
                    capi: capi,
                    mode: timer?.Mode ?? TimerMode.InGame,
                    modSystem: modSystem);

            string labelText = timer?.Label ?? "";

            var activeChildren = new List<Widget>();
            if (labelText.Length > 0)
                activeChildren.Add(new Text(labelText, labelStyle));
            activeChildren.Add(timeWidget);
            activeChildren.Add(new Button(
                child: new Text(Lang.Get("scribe:scribe-gui-timer-stop"), new TextStyle
                {
                    FontSize = body,
                    FontFamily = ScribeTaskFont.ButtonFamily,
                    Color = colors.OnPrimary,
                }),
                onTap: _ => SendClearTimer()));

            content = new Column(
                spacing: 12,
                crossAxisAlignment: CrossAxisAlignment.Center,
                mainAxisSize: MainAxisSize.Min,
                children: activeChildren);
        }

        // The ambient gearworks sits above the form/countdown, shown in every timer state and non-interactive.
        // A Row (not Center) centers it while consuming only its own height; a bare Center would expand and starve
        // the Expanded(content) below. Scale follows Pixel Art Size (540px = 100%), not the text scale.
        // ScribeResetPaintColor kills the SharedPaint DrawMaskedBox fade, as for the dialog backdrops.
        float gearScale = ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) / 540f;
        Widget gearworks = new ScribeResetPaintColor(new Row(
            mainAxisAlignment: MainAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { new ScribeGearworks(capi, modSystem, gearScale, status, _engageStartMs, _fireLockMs) }));

        // Shared Row 2 subtitle + divider; no Row 3, like the Editor/History tabs.
        Widget header = ScribeTabHeader.Build(colors, RowStyle, "scribe:scribe-gui-nav-timer", "scribe:scribe-gui-subtitle-timer",
            modSystem.VisualTuning.ShowSubtitleRow);

        // Root the tab in the Task Text Font + window-scaled size. Text widgets inherit the family; bodyStyle keeps
        // its explicit family for the non-inheriting inputs; buttons keep the Caudex button font. Top inset is 4
        // (tight gap under the title bar); the other sides are 10 like every tab.
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, body, new Padding(
            EdgeInsets.Ltrb(10, 4, 10, 10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { header, gearworks, new Expanded(new Center(child: content)) })));
    }

    private Widget BuildSetTimerForm(ColorScheme colors, TextStyle bodyStyle, TextStyle smallStyle, float scale)
    {
        float fieldH = 34f * scale;
        float fieldW = 70f * scale;

        // Label field.
        var labelController = new TextEditingController(_pendingLabel);

        var inputFieldStyle = new TextFieldStyle
        {
            FillColor       = colors.SurfaceHigh,
            BorderColor     = colors.Border,
            BorderThickness = 1,
            Height          = fieldH,
            TextStyle       = bodyStyle,
        };

        Widget labelRow = new Column(
            spacing: 4,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-gui-timer-label"), smallStyle),
                new TextField(
                    labelController,
                    null,
                    inputFieldStyle,
                    onChanged: text => _pendingLabel = text),
            });

        // H / M / S steppers.
        Widget timeRow = new Row(
            spacing: 8,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.End,
            children: new Widget[]
            {
                BuildTimeUnit("th", _pendingHours,   0, 23, 1,  "h", v => _pendingHours   = v, inputFieldStyle, smallStyle),
                BuildTimeUnit("tm", _pendingMinutes, 0, 59, 1,  "m", v => _pendingMinutes = v, inputFieldStyle, smallStyle),
                BuildTimeUnit("ts", _pendingSecs,    0, 55, 5,  "s", v => _pendingSecs    = v, inputFieldStyle, smallStyle),
            });

        // Mode toggle: one stateful widget owns the toggle state so it can SetState without remounting the Button.
        Widget modeRow = new ScribeTimerModeRow(
            selected: _pendingMode,
            // Persisted as PreferredTimerMode so the form pre-selects it next time.
            onSelect: m =>
            {
                _pendingMode = m;
                modSystem.UpdateMySettings(s => s.PreferredTimerMode = m);
            },
            bodyStyle: bodyStyle,
            checkboxSize: ScribeRowConstants.RowCheckboxSize * scale);

        var btnStyle = new TextStyle
        {
            FontSize   = ScribeRowConstants.BaseWindowFontSize * scale,
            FontFamily = ScribeTaskFont.ButtonFamily,
            Color      = colors.OnPrimary,
        };

        // Always pass a non-null onTap (guard inside SendStartTimer): a null onTap still mounts ButtonState,
        // which may crash on press-sound in the shipped Gui.dll 3.1.0.
        Widget startBtn = new Button(
            child: new Text(Lang.Get("scribe:scribe-gui-timer-start"), btnStyle),
            onTap: _ => SendStartTimer());

        return new Column(
            spacing: 16,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new SizedBox(width: 260, child: labelRow),
                timeRow,
                new SizedBox(width: 260, child: modeRow),
                startBtn,
            });
    }

    private Widget BuildTimeUnit(string id, int value, int min, int max, int step,
        string suffix, Action<int> onSet,
        TextFieldStyle inputFieldStyle, TextStyle smallStyle)
    {
        // BoxStyle for the numeric field's outer container, matching the input fill.
        var stepperBoxStyle = new BoxStyle
        {
            Color           = inputFieldStyle.FillColor,
            BorderColor     = inputFieldStyle.BorderColor,
            BorderThickness = inputFieldStyle.BorderThickness,
            Height          = inputFieldStyle.Height,
            Width           = 70,
        };

        return new Column(
            spacing: 4,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new SizedBox(
                    key: new ValueKey<int>(value),
                    child: new ScribeNumericField(
                        initialValue: value,
                        step: step,
                        onChanged: v =>
                        {
                            int clamped = Math.Clamp((int)MathF.Round(v / step) * step, min, max);
                            onSet(clamped);
                        },
                        style: stepperBoxStyle,
                        focusNode: _timerFocus.NodeFor(id),
                        autoFocus: _timerFocus.ShouldFocus(id),
                        onStepped: () => _timerFocus.ArmAutoFocus(id),
                        clamp: v => Math.Clamp((int)MathF.Round(v / step) * step, min, max),
                        textStyle: inputFieldStyle.TextStyle)),
                new Text(suffix, smallStyle),
            });
    }

    // Network sends

    private void SendStartTimer()
    {
        double duration = _pendingHours * 3600.0 + _pendingMinutes * 60.0 + _pendingSecs;
        if (duration <= 0) return;
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetTimerMessage
        {
            DurationSeconds = duration,
            Label           = _pendingLabel.Trim(),
            Mode            = _pendingMode,
        });
    }

    private void SendClearTimer()
    {
        modSystem.DismissAlarm();
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeClearTimerMessage());
    }

    // Tick & event

    /// <summary>Rebuilds only when the timer status changes; the countdown text self-ticks, so mid-run pushes
    /// need no rebuild.</summary>
    internal new void RefreshTimerView()
    {
        var newStatus = modSystem.MyTimer?.Status ?? TimerStatus.Idle;
        if (newStatus == _lastKnownStatus) return;

        // Stamp the transitions the gearworks animates off. They live on the host so they survive the ForceRebuild
        // below. Engage = anything to Running; lock = to Fired.
        long now = capi.World.ElapsedMilliseconds;
        if (newStatus == TimerStatus.Running && _lastKnownStatus != TimerStatus.Running) _engageStartMs = now;
        if (newStatus == TimerStatus.Fired) _fireLockMs = now;
        if (newStatus == TimerStatus.Idle) { _engageStartMs = 0; _fireLockMs = 0; }

        _lastKnownStatus = newStatus;
        _resyncRemaining = true;
        if (IsTimerView && IsOpened()) ForceRebuild();
    }

    private void OnTimerChanged()
    {
        _resyncRemaining = true;
    }

}

// ScribeCountdownText

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

// ScribeGearworks

/// <summary>
/// The Timer-tab ambient clockwork gear-train.
///
/// <para><b>Temporal pair (always moving).</b> A large and a small interlocking gear tick one tooth per second
/// whenever the tab is open, in every timer state; they never stop, not even on fire.</para>
///
/// <para><b>Escape wheel.</b> A big, mostly hidden wheel peeking up from behind the pair. It slides into view when
/// a timer starts, rotates while Running, and on Fired shudders then locks while the pair keeps turning. This is
/// the only coupling between timer state and the gears.</para>
///
/// <para><b>Rebuild-stable tick:</b> the phase is derived from a monotonic clock
/// (<see cref="IClientWorldAccessor.ElapsedMilliseconds"/>), <c>toothIndex = floor(elapsedMs / TickPeriodMs)</c>,
/// not accumulated in <see cref="State"/>, because the Timer tab's <c>ForceRebuild</c> on status transitions
/// remounts this subtree and a State-held phase would reset. The lock angle derives from the host-owned fire
/// timestamp.</para>
///
/// <para><b>Mesh:</b> each gear's angle = <c>direction x ratio x toothIndex x stepAngle</c>, with
/// <c>ratio = ReferenceTeeth / thisGearTeeth</c> and alternating direction. Positions are hand-tuned so painted
/// teeth interlock; this is faked, not a physics solver.</para>
///
/// <para>Gears are rasters from the asset locations below (the escape wheel is the procedural great wheel, with a
/// small-gear fallback). Non-interactive: paint-only Containers with no gesture handlers.</para>
/// </summary>
internal sealed class ScribeGearworks : StatefulWidget
{
    // Filed under textures/gui/ like the mod's other GUI rasters (VS scans "textures" but has no "gui" category).
    // The temporal gear is a 512px raster downsampled to largeSize in Build.
    public static readonly AssetLocation LargeGearAsset = new("scribe", "textures/gui/gear-temporal-large-512.png");
    public static readonly AssetLocation SmallGearAsset = new("scribe", "textures/gui/gear-temporal-small.png");
    // Fallback for the escape wheel when the procedural great wheel is unavailable.
    public static readonly AssetLocation EscapeGearAsset = SmallGearAsset;

    // Optional framing art for the trim box; if missing, the backdrop is omitted and the border falls back to a 1px
    // Container border. The logical box is TrimBoxWidth x TrimBoxHeight (252x138): the BACKDROP fills it exactly
    // (author at 2-3x, e.g. 756x414); the BORDER-TRIM frames it OUTSIDE with an even 8px margin (268x154 logical,
    // e.g. 804x462) with a transparent 252x138 centre hole.
    public static readonly AssetLocation TrimBackdropAsset = new("scribe", "textures/gui/gearworks-backdrop.png");
    public static readonly AssetLocation TrimBorderAsset   = new("scribe", "textures/gui/gearworks-border.png");
    /// <summary>Nearly transparent glass pane painted over the gears and backdrop (topmost child inside the
    /// HardEdge clip). Fills the trim box exactly; author at 2-3x (756x414). Optional.</summary>
    public static readonly AssetLocation TrimGlassAsset    = new("scribe", "textures/gui/gearworks-glass.png");
    /// <summary>Even border margin (logical px) the border art adds on each side of the trim box; must match how the
    /// border art is drawn.</summary>
    public const float TrimBorderMargin = 8f;

    public readonly ICoreClientAPI Capi;
    public readonly ScribeModSystem ModSystem;
    public readonly float Scale;
    public readonly TimerStatus Status;
    /// <summary>Monotonic-clock ms of the last Idle-to-Running transition, or 0. Host-owned so the engage slide
    /// survives the ForceRebuild remount.</summary>
    public readonly long EngageStartMs;
    /// <summary>Monotonic-clock ms of the last to-Fired transition, or 0. The wheel freezes at the angle it had then.</summary>
    public readonly long FireLockMs;

    public ScribeGearworks(ICoreClientAPI capi, ScribeModSystem modSystem, float scale, TimerStatus status,
        long engageStartMs, long fireLockMs)
    { Capi = capi; ModSystem = modSystem; Scale = scale; Status = status; EngageStartMs = engageStartMs; FireLockMs = fireLockMs; }

    public override State CreateState() => new ScribeGearworksState();
}

internal sealed class ScribeGearworksState : State<ScribeGearworks>
{
    // Tooth counts must match the art or the faked mesh drifts. The 12-tooth temporal gear steps 2pi/12 per tick
    // and the small gear 2pi/8 (see Ratio).
    private const int ReferenceTeeth = 12;   // large teal gear (counted from gear-temporal-large-512.png)
    private const int SmallTeeth     = 8;    // small gear
    // The escape wheel steps by its own tooth count, which is .geartune-tunable, so its step is derived in Build.

    // One tooth-step per real second, matching a RealTime countdown. InGame timers drain faster, an accepted
    // mismatch. The tween settles within SettleMs (< TickPeriodMs) so each step snaps and settles before the next.
    private const long TickPeriodMs = 1000;
    private const int  SettleMs     = 520;
    // Clock re-check interval; small so the snap begins within a frame of the second boundary.
    private const int  PollMs = 16;

    // Engage slide: on Idle-to-Running the wheel slides from its resting peek to its live peek over this duration,
    // derived from Widget.EngageStartMs so it does not snap on the remount.
    private const long EngageSlideMs = 420;
    // Fire retract: on Running-to-Fired the wheel slides back down to the resting peek (mirror of the engage
    // slide), derived from Widget.FireLockMs for the same rebuild-stability reason.
    private const long FireRetractMs = 420;

    private long _tickId;
    private long _toothIndex;

    // Fire, shudder, lock. The lock angle derives from the host-owned Widget.FireLockMs (the tooth index reached at
    // the fire instant), not State: State is remounted on the Running-to-Fired ForceRebuild and AnimatedRotation
    // seeds Begin==End on a fresh mount, so any other angle would snap, visible as a rewind. The shudder is a
    // decaying oscillation added on top via a raw Transform.Rotate (AnimatedRotation's tween would smooth it away).
    private float _shudderAngle;
    private int   _shudderStep;
    private long  _shudderTickId;
    // Decaying oscillation (radians), one value per ShudderStepMs, ending at 0 (locked).
    private static readonly float[] ShudderSequence = { 0.14f, -0.10f, 0.06f, -0.03f, 0f };
    private const int ShudderStepMs = 70;

    // Cast shadow via ScribeGearEffect (dark silhouette offset down-right beneath each gear), contained by the
    // HardEdge clip. Opacity is per gear: the temporal gear 0x70, the steel gears 2/3 of that so it stays the focus.
    private static readonly SKColor TealShadowTint  = new(0x00, 0x00, 0x00, 0x70);   // ~44% black (temporal gear)
    private static readonly SKColor SteelShadowTint = new(0x00, 0x00, 0x00, 0x4B);   // ~29% black (2/3 of 0x70)
    private const float ShadowOffset = 1.5f;   // px (×scale) down-right
    private const float ShadowBlur   = 1f;     // sigma (×scale)

    private static readonly float StepAngleLarge  = MathF.PI * 2f / ReferenceTeeth;   // one large tooth (2π/12 = 30°)
    private const float Ratio = (float)ReferenceTeeth / SmallTeeth;                    // small steps ratio× faster

    // The escape wheel is always visible; "engaged" only sets how far it peeks and whether it turns. A hidden-to-
    // visible latch would re-slide from nothing on each remount, since stock Animated* widgets snap on a fresh mount.
    private bool Engaged => Widget.Status != TimerStatus.Idle;   // wheel turns + peeks further while Running/Fired
    private bool Locked  => Widget.Status == TimerStatus.Fired;

    public override void InitState()
    {
        base.InitState();
        _toothIndex = CurrentToothIndex();
        // Self-tick: repaint when the tooth index advances or while a slide is in flight, without rebuilding the host.
        _tickId = Widget.Capi.Event.RegisterGameTickListener(_ =>
        {
            long next = CurrentToothIndex();
            bool sliding = EngageSlideProgress() < 1f || (Locked && FireRetractProgress() < 1f);
            if (next != _toothIndex)
            {
                PlayTickTock(next);            // one faint tick-tock per real second
                SetState(() => _toothIndex = next);
            }
            else if (sliding)
            {
                SetState(() => _toothIndex = next);
            }
        }, PollMs);

        // DEV: rebuild live when a .geartune knob changes.
        Widget.ModSystem.GearTuningChanged += OnGearTuningChanged;

        if (Locked)
        {
            // One-shot shudder on top of the frozen angle; InitState runs fresh at the Running-to-Fired remount,
            // so it fires once per fire.
            _shudderStep = 0;
            _shudderAngle = ShudderSequence[0];
            _shudderTickId = Widget.Capi.Event.RegisterGameTickListener(OnShudder, ShudderStepMs);
        }
    }

    /// <summary>Tooth index the running wheel had reached at a monotonic timestamp. Shared by the live angle and the
    /// frozen lock angle, so the lock equals the last running angle (no rewind).</summary>
    private static long ToothIndexAt(long ms) => ms / TickPeriodMs;

    /// <summary>0..1 progress of the engage slide from the host-owned EngageStartMs; 1 when never started or long past.</summary>
    private float EngageSlideProgress()
    {
        if (Widget.EngageStartMs <= 0) return 1f;
        long dt = Widget.Capi.World.ElapsedMilliseconds - Widget.EngageStartMs;
        if (dt >= EngageSlideMs) return 1f;
        if (dt <= 0) return 0f;
        return (float)dt / EngageSlideMs;
    }

    /// <summary>0..1 progress of the Fired retract slide from FireLockMs: 0 at the fire instant, 1 once withdrawn.</summary>
    private float FireRetractProgress()
    {
        if (Widget.FireLockMs <= 0) return 0f;
        long dt = Widget.Capi.World.ElapsedMilliseconds - Widget.FireLockMs;
        if (dt >= FireRetractMs) return 1f;
        if (dt <= 0) return 0f;
        return (float)dt / FireRetractMs;
    }

    private void OnShudder(float dt)
    {
        _shudderStep++;
        if (_shudderStep >= ShudderSequence.Length)
        {
            if (_shudderTickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_shudderTickId); _shudderTickId = 0; }
            SetState(() => _shudderAngle = 0f);
            return;
        }
        SetState(() => _shudderAngle = ShudderSequence[_shudderStep]);
    }

    /// <summary>DEV: a .geartune knob changed; rebuild so Build re-reads the tuning values.</summary>
    private void OnGearTuningChanged() => SetState(() => { });

    public override void Dispose()
    {
        if (_tickId != 0)        { Widget.Capi.Event.UnregisterGameTickListener(_tickId);        _tickId = 0; }
        if (_shudderTickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_shudderTickId); _shudderTickId = 0; }
        Widget.ModSystem.GearTuningChanged -= OnGearTuningChanged;
        base.Dispose();
    }

    private long CurrentToothIndex() => Widget.Capi.World.ElapsedMilliseconds / TickPeriodMs;

    // Tick-tock: a faint beat per real second alternating two pitches of vanilla game:sounds/tick.ogg (no new
    // audio). Plays on the Effect channel via EnumSoundType.Sound + RelativePosition; muted by "Mute Scribe UI sounds".
    private static readonly AssetLocation TickSound = new("game", "sounds/tick");
    private const float TickVolume = 0.49f;   // faint
    private const float TickPitch  = 1.15f;   // higher "tick"
    private const float TockPitch  = 0.85f;   // lower "tock"

    /// <summary>One tick-tock beat (even tooth = tick, odd = tock, stable across remounts). No-op when muted.
    /// Fire-and-forget: the sound self-disposes (SoundParams.DisposeOnFinish defaults true).</summary>
    private void PlayTickTock(long toothIndex)
    {
        if (Widget.ModSystem.MySettings.MuteUiSounds) return;
        var world = Widget.Capi.World;
        var sound = world.LoadSound(new SoundParams(TickSound)
        {
            SoundType = EnumSoundType.Sound,     // Effect channel (not Music)
            RelativePosition = true,             // non-positional, always at the listener
            Position = new Vec3f(0f, 0f, 0f),
            ShouldLoop = false,
            Volume = TickVolume,
            Pitch = (toothIndex % 2 == 0) ? TickPitch : TockPitch,
        });
        sound?.Start();
    }

    public override Widget Build(BuildContext context)
    {
        float scale = Widget.Scale;

        // Region + gear sizing. The region hosts the pair (upper band) plus the escape wheel's peek from below.
        // DEV: region dims and gear scales come from the .geartune window; fold into constants and delete
        // ScribeGearTuning once the layout is final.
        var tuning = Widget.ModSystem.GearTuning;
        float regionW   = tuning.TrimBoxWidth  * scale;   // DEV .geartune knob (default 200)
        float regionH   = tuning.TrimBoxHeight * scale;   // DEV .geartune knob (default 130)
        float largeSize = 97.5f  * scale * tuning.LargeGearScale;   // base size × scale knob (default 1)
        float smallSize = 65f    * scale * tuning.SmallGearScale;   // base size × scale knob (default 1)
        float escapeSize = 212.5f * scale;  // big wheel; only its top arc shows — rest clipped below

        // Symmetric train: the temporal gear is the centered driver, dropped low to overlap the escape wheel, flanked
        // by two small steel gears. toothKiss tucks their teeth into the temporal gear's. DEV: small-gear placement
        // and teal-gear Y come from .geartune.
        float toothKiss = tuning.SmallGearOverlapX * scale;   // how deep each small tucks under the teal gear
        float largeLeft = (regionW - largeSize) / 2f;         // centered horizontally
        float largeTop  = tuning.LargeGearY * scale;          // DEV .geartune knob (default 30) — LOW → overlaps wheel
        float smallTop  = tuning.SmallGearY * scale;          // midpoint sits just below the teal gear's
        float leftSmallLeft  = largeLeft - smallSize + toothKiss;
        float rightSmallLeft = largeLeft + largeSize - toothKiss;

        // Escape wheel: centered, peeking up from the bottom edge; the rest is clipped away. A small resting peek
        // when Idle, a larger live peek while running; it slides between them over EngageSlideMs (up) and
        // FireRetractMs (down on fire). Progress derives from host-owned timestamps so it glides, not snaps.
        float restingPeek = tuning.WheelIdlePeek * scale;    // DEV .geartune knob (default 34)
        float livePeek    = tuning.WheelActivePeek * scale;  // DEV .geartune knob (default 60)
        float escapePeek;
        if (Locked)
        {
            // Fired: retract from the live peek to the resting peek.
            float retractT = (float)Curves.EaseOutBack.Transform(FireRetractProgress());
            escapePeek = livePeek + (restingPeek - livePeek) * retractT;
        }
        else if (Engaged)
        {
            float engageT = (float)Curves.EaseOutBack.Transform(EngageSlideProgress());
            escapePeek = restingPeek + (livePeek - restingPeek) * engageT;
        }
        else
        {
            escapePeek = restingPeek;
        }
        float escapeLeft = (regionW - escapeSize) / 2f;
        float escapeTop  = regionH - escapePeek;

        // Monotonic-derived angles. The teal driver turns one way; both smalls counter-rotate, ratio x faster. The
        // smalls add a DEV .geartune resting-phase offset so their teeth mesh at rest; it is mirrored (+X left, -X
        // right) because they flank the teal gear symmetrically, and only shifts the starting phase.
        float largeAngle    =  _toothIndex * StepAngleLarge;
        float smallSpin     = -_toothIndex * StepAngleLarge * Ratio;
        float smallRestPhase = tuning.SmallGearAngle * (MathF.PI / 180f);
        float leftSmallAngle  = smallSpin + smallRestPhase;
        float rightSmallAngle = smallSpin - smallRestPhase;

        var children = new List<Widget>();

        // Textured BACKDROP first so it paints behind every gear, filling the trim box (inside the HardEdge clip).
        // Optional. ScribeResetPaintColor prevents the DrawMaskedBox SharedPaint-alpha leak making it see-through.
        var backdropBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimBackdropAsset);
        if (backdropBmp is not null)
            children.Add(new Positioned(left: 0f, top: 0f, width: regionW, height: regionH,
                child: new ScribeResetPaintColor(
                    new Container(style: new BoxStyle { Texture = backdropBmp, Width = regionW, Height = regionH }))));

        // Escape wheel first so it paints behind the pair. Always present; only its motion depends on state.
        //   Idle:    stationary at tooth 0.
        //   Running: one escape-tooth per tick off the monotonic clock.
        //   Fired:   frozen at the tooth index of the fire instant (Widget.FireLockMs), same formula as running,
        //            so it locks where it was; a brief shudder rides on top while it retracts to idle.
        // The wheel spins opposite the teal driver, hence +index.
        long escapeIndex = Locked ? ToothIndexAt(Widget.FireLockMs)
                         : Engaged ? _toothIndex
                         : 0L;
        // Per-tick step is one of the wheel's own teeth; the count is .geartune-tunable, so the step derives from the
        // live count. WheelAngle is a resting-phase offset so its teeth can mesh with the teal gear.
        float escapeStep = MathF.PI * 2f / (int)tuning.WheelTeeth;
        float escapeBase = escapeIndex * escapeStep + tuning.WheelAngle * (MathF.PI / 180f);
        // The great wheel is procedurally generated; falls back to the small-gear PNG if generation is unavailable (e.g. server-side).
        var escapeBmp = Widget.ModSystem.GetProceduralGreatWheel()
                        ?? Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.EscapeGearAsset);
        if (escapeBmp is not null)
        {
            // The shudder is an instant raw Transform.Rotate over the settling base rotation (AnimatedRotation's
            // 520ms tween would smooth it away). Safe from the zero-size Skia matrix crash (fixed child size).
            System.Func<Widget> maybeShudder = () =>
            {
                Widget w = MakeGear(escapeBmp, escapeSize, escapeBase);
                if (Locked && _shudderAngle != 0f)
                    w = Transform.Rotate(child: w, radians: _shudderAngle, alignment: Alignment.Center);
                return w;
            };
            // Shadow beneath, then the wheel in ScribeResetPaintColor so the shadow's SrcIn tint cannot make it see-through.
            AddShadow(children, maybeShudder(), escapeLeft, escapeTop, escapeSize, scale, SteelShadowTint);
            children.Add(new Positioned(left: escapeLeft, top: escapeTop, width: escapeSize, height: escapeSize,
                child: new ScribeResetPaintColor(maybeShudder())));
        }

        // Two flanking small steel gears with shadows, below the teal gear in paint order.
        var smallBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.SmallGearAsset);
        if (smallBmp is not null)
        {
            AddShadow(children, MakeGear(smallBmp, smallSize, leftSmallAngle), leftSmallLeft, smallTop, smallSize, scale, SteelShadowTint);
            children.Add(new Positioned(left: leftSmallLeft, top: smallTop, width: smallSize, height: smallSize,
                child: new ScribeResetPaintColor(MakeGear(smallBmp, smallSize, leftSmallAngle))));
            AddShadow(children, MakeGear(smallBmp, smallSize, rightSmallAngle), rightSmallLeft, smallTop, smallSize, scale, SteelShadowTint);
            children.Add(new Positioned(left: rightSmallLeft, top: smallTop, width: smallSize, height: smallSize,
                child: new ScribeResetPaintColor(MakeGear(smallBmp, smallSize, rightSmallAngle))));
        }

        // Teal temporal gear last (on top), with its darker shadow; ScribeResetPaintColor keeps it opaque.
        var largeBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.LargeGearAsset);
        if (largeBmp is not null)
        {
            AddShadow(children, MakeGear(largeBmp, largeSize, largeAngle), largeLeft, largeTop, largeSize, scale, TealShadowTint);
            children.Add(new Positioned(left: largeLeft, top: largeTop, width: largeSize, height: largeSize,
                child: new ScribeResetPaintColor(MakeGear(largeBmp, largeSize, largeAngle))));
        }

        // Glass pane last but inside the clip, so it covers gears and backdrop and is trimmed to the box.
        // Non-interactive. Optional. ScribeResetPaintColor for the same SharedPaint leak fix.
        var glassBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimGlassAsset);
        if (glassBmp is not null)
            children.Add(new Positioned(left: 0f, top: 0f, width: regionW, height: regionH,
                child: new ScribeResetPaintColor(
                    new Container(style: new BoxStyle { Texture = glassBmp, Width = regionW, Height = regionH }))));

        // Nothing loaded: render an empty region rather than crash.
        if (children.Count == 0)
            return new SizedBox(width: regionW, height: regionH);

        // Clip so the escape wheel's hidden body and every shadow never overrun the form below. Without border art,
        // a 1px parchment border keeps the box visible while tuning.
        var borderBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimBorderAsset);
        Widget clipped = new Container(
            style: new BoxStyle
            {
                Width           = regionW,
                Height          = regionH,
                // Fallback outline only when there is no border art.
                BorderThickness = borderBmp is null ? scale : 0f,
                BorderColor     = new Vector4(0.55f, 0.42f, 0.2f, 0.9f),   // parchment-brown outline
            },
            child: new Clip(
                clipBehavior: ClipBehavior.HardEdge,
                child: new SizedBox(width: regionW, height: regionH, child: new Stack(children))));

        // Border-trim frame: an authored PNG outside the trim box with an even TrimBorderMargin on every side
        // ((regionW + 2*margin) x (regionH + 2*margin)) and a transparent centre. Painted last so its inner edge
        // overlaps the box edge cleanly. Absent: just the clipped region with its fallback border.
        Widget region;
        if (borderBmp is not null)
        {
            float margin  = ScribeGearworks.TrimBorderMargin * scale;
            float borderW = regionW + 2f * margin;
            float borderH = regionH + 2f * margin;
            region = new SizedBox(width: borderW, height: borderH, child: new Stack(new List<Widget>
            {
                // Clipped gear region, inset by the margin.
                new Positioned(left: margin, top: margin, width: regionW, height: regionH, child: clipped),
                // The frame PNG over the full bordered area.
                new Positioned(left: 0f, top: 0f, width: borderW, height: borderH,
                    child: new ScribeResetPaintColor(
                        new Container(style: new BoxStyle { Texture = borderBmp, Width = borderW, Height = borderH }))),
            }));
        }
        else
        {
            region = clipped;
        }

        // DEV .geartune "trim box Y": a paint-only nudge (Transform.Translate), not a margin, so the form below
        // does not reflow. Positive = down; skipped when 0.
        float trimY = tuning.TrimBoxY * scale;
        return trimY == 0f ? region : Transform.Translate(child: region, offset: new Vector2(0f, trimY));
    }

    /// <summary>Adds a cast-shadow silhouette of <paramref name="gear"/>, offset down-right and tinted via
    /// <see cref="ScribeGearEffect"/> (<paramref name="tint"/> is per gear). The outer HardEdge clip contains it.</summary>
    private static void AddShadow(List<Widget> children, Widget gear, float left, float top, float size, float scale, SKColor tint)
    {
        children.Add(new Positioned(
            left: left + ShadowOffset * scale,
            top:  top  + ShadowOffset * scale,
            width: size, height: size,
            child: new ScribeGearEffect(gear, tint, ShadowBlur * scale)));
    }

    /// <summary>One gear: a raster in a fixed-size textured Container inside AnimatedRotation so per-tooth target
    /// changes spring-settle. Fresh instance per call, since a widget cannot appear twice in the tree and the
    /// shadow needs its own rotation widget.</summary>
    private Widget MakeGear(SKBitmap bmp, float size, float angle)
        => new AnimatedRotation(
            angle: angle,
            duration: TimeSpan.FromMilliseconds(SettleMs),
            curve: Curves.EaseOutBack,
            child: new Container(style: new BoxStyle { Texture = bmp, Width = size, Height = size }));
}
