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
/// The Clockmaker's Notebook dialog — extends <see cref="GuiDialogScribeNotebook"/> with a
/// Timer tab. All other tabs (Read, Edit, Pinned, History) are inherited unchanged.
/// </summary>
public sealed class GuiDialogClockmakerNotebook : GuiDialogScribeNotebook
{
    // ── Timer tab local state ──────────────────────────────────────────────────

    /// <summary>Client-side interpolated remaining seconds, updated every tick for smooth display
    /// between authoritative server pushes.</summary>
    private double _localRemaining;

    /// <summary>Whether to reseed _localRemaining from MyTimer on the next rebuild (set when a
    /// server push arrives).</summary>
    private bool _resyncRemaining = true;

    // Seeded from the player's remembered PreferredTimerMode in the ctor body (NOT here — field
    // initializers run before base(...) assigns modSystem). Defaults to RealTime for a first-time player
    // (fix-clockmaker-timer-mode-default). Persisted back on every selection so it survives close/reopen.
    private TimerMode _pendingMode;
    private int _pendingHours;
    private int _pendingMinutes;
    private int _pendingSecs;
    private string _pendingLabel = "";

    private readonly ScribeNumericFocusRegistry _timerFocus = new();
    private long _timerTickId;

    /// <summary>Last-seen timer status — used to detect transitions so we only call ForceRebuild
    /// when the status actually changes (Idle↔Running↔Fired), not on every 1-second server push.
    /// The countdown text updates itself via ScribeCountdownText's own tick.</summary>
    private TimerStatus _lastKnownStatus = TimerStatus.Idle;

    /// <summary>Monotonic-clock timestamps (ms, <c>capi.World.ElapsedMilliseconds</c>) of the last
    /// Idle→Running (engage) and →Fired (lock) transitions. Captured HERE on the host — which survives
    /// <c>ForceRebuild</c> — rather than in the gearworks widget's State (which is torn down + remounted on
    /// every transition, so a State-held timestamp would reset and the slide/lock would snap). The gearworks
    /// derives its start-slide progress and its frozen lock angle from these (design D8a / D4). 0 = "not yet".</summary>
    private long _engageStartMs;
    private long _fireLockMs;

    public GuiDialogClockmakerNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(host, capi)
    {
        // Seed the "set timer" form's mode from the player's remembered choice (default RealTime).
        _pendingMode = modSystem.MySettings.PreferredTimerMode;
        modSystem.MyTimerChanged += OnTimerChanged;
        _timerTickId = capi.Event.RegisterGameTickListener(OnTimerTick, 250);
    }

    public override void OnGuiClosed()
    {
        modSystem.MyTimerChanged -= OnTimerChanged;
        if (_timerTickId != 0)
        {
            capi.Event.UnregisterGameTickListener(_timerTickId);
            _timerTickId = 0;
        }
        _timerFocus.Dispose();
        base.OnGuiClosed();
    }

    // ── Extra nav button ───────────────────────────────────────────────────────

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        // History (inherited from GuiDialogScribeNotebook)
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

    // ── Timer tab content ──────────────────────────────────────────────────────

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

        // bodyStyle KEEPS its explicit FontFamily: it is threaded into non-Text widgets (the label
        // TextField's TextFieldStyle and the numeric steppers) which do NOT read the DefaultTextStyle
        // ancestor, so dropping it would regress those inputs to sans-serif. big/label flow only into
        // Text widgets, so their family is inherited from the wrap below. small never set a family and
        // now inherits the task font too (adopt-libgui-31-improvements — approved chrome-label change).
        var bodyStyle  = new TextStyle { FontSize = body,  Color = colors.OnSurface, FontFamily = taskFont };
        var bigStyle   = new TextStyle { FontSize = big,   Color = colors.OnSurface };
        var labelStyle = new TextStyle { FontSize = body,  Color = colors.OnSurface with { W = colors.OnSurface.W * 0.7f } };
        var smallStyle = new TextStyle { FontSize = small, Color = colors.OnSurfaceVariant };

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
            // Running or Fired: show countdown/00:00 + Stop button.
            // ScribeCountdownText self-ticks every 250ms — no ForceRebuild needed during the run.
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

        // The ambient gearworks sits ABOVE the form/countdown in its own vertical region, decoupled from
        // timer state (shown Idle/Running/Fired) — a peek into the mechanism behind the page
        // (add-timer-gearworks §2.5, spec "decoupled from timer state"). Non-interactive so it can't intercept
        // control input (design D7). A Row (not Center) centers it horizontally while consuming only the
        // gears' own height — a bare Center in this Column would expand to fill the main axis and starve the
        // Expanded(content) below it of height (the timer form/countdown would vanish). M1: motion/mesh only;
        // glass framing + fire-shudder are M2.
        // The gearworks scales off Pixel Art Size (design D9), NOT the text scale used elsewhere on the tab:
        // 540px is pegged to 100% and it grows/shrinks proportionally. The ScribeResetPaintColor wrapper kills
        // the SharedPaint DrawMaskedBox fade (design D10) — the same fix the dialog backdrops use.
        float gearScale = ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) / 540f;
        Widget gearworks = new ScribeResetPaintColor(new Row(
            mainAxisAlignment: MainAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[] { new ScribeGearworks(capi, modSystem, gearScale, status, _engageStartMs, _fireLockMs) }));

        // Root the tab subtree in the player's Task Text Font + window-scaled base size
        // (adopt-libgui-31-improvements). big/label/small Text widgets inherit the family from here;
        // bodyStyle keeps its explicit family for the non-inheriting label field + numeric steppers; the
        // Stop/Start buttons keep their explicit Caudex button font. The mode radios take the task font.
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, body, new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { new Divider(), gearworks, new Expanded(new Center(child: content)) })));
    }

    private Widget BuildSetTimerForm(ColorScheme colors, TextStyle bodyStyle, TextStyle smallStyle, float scale)
    {
        float fieldH = 34f * scale;
        float fieldW = 70f * scale;

        // Label field (plain text input).
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

        // Mode toggle: a single stateful widget that owns the visual toggle state so it can
        // call SetState on itself without remounting the sibling Button.
        Widget modeRow = new ScribeTimerModeRow(
            selected: _pendingMode,
            // Remember the choice: persist it as the player's PreferredTimerMode so the form pre-selects it
            // on the next close/reopen (fix-clockmaker-timer-mode-default).
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

        // Always provide a non-null onTap — guard inside SendStartTimer instead. A null onTap still
        // mounts ButtonState which may crash on press-sound in the shipped Gui.dll 3.1.0.
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
        // Derive a BoxStyle for the ScribeNumericField outer container matching the input fill.
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

    // ── Network sends ──────────────────────────────────────────────────────────

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
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeClearTimerMessage());
    }

    // ── Tick & event ───────────────────────────────────────────────────────────

    /// <summary>Only rebuild when the timer status changes — not on every 1-second server push.
    /// The countdown text is a self-ticking StatefulWidget, so mid-run pushes don't need a rebuild.</summary>
    internal new void RefreshTimerView()
    {
        var newStatus = modSystem.MyTimer?.Status ?? TimerStatus.Idle;
        if (newStatus == _lastKnownStatus) return;

        // Stamp the transitions the gearworks animates off (design D8a / D4). These live on the host so they
        // survive the ForceRebuild below (a widget-State timestamp would reset on remount and the slide/lock
        // would snap). Engage = anything→Running (the escape wheel slides in). Lock = →Fired (freeze angle).
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

    private void OnTimerTick(float dt)
    {
        // No per-tick logic needed in the dialog — ScribeCountdownText handles its own display.
        // We keep this registered so we can check for status transitions if needed in future.
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string FormatDuration(double seconds)
    {
        int total = (int)Math.Max(0, seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return h > 0
            ? $"{h:D2}:{m:D2}:{s:D2}"
            : $"{m:D2}:{s:D2}";
    }
}

// ── ScribeCountdownText ──────────────────────────────────────────────────────

/// <summary>Self-ticking countdown display. Seeds from <paramref name="initialSeconds"/> and
/// decrements every 250 ms without triggering a parent ForceRebuild, so sibling widgets
/// (e.g. the Stop Timer button) are never remounted while the timer is running.
///
/// <para>An InGame-mode timer drains at the world's in-game time rate (≈30 in-game s per real s by
/// default), matching the server's authoritative decrement; RealTime drains one-per-real-second. The
/// rate is read live each tick so a mid-run world time-speed change tracks.</para></summary>
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
        // Decrement AND repaint off the mod system's shared 1Hz tick — no dedicated 250ms listener. The
        // display only ever shows whole seconds, so finer interpolation was invisible; one tick per real
        // second advances the countdown by one real second's worth (rate for InGame ≈ 30). Sharing the
        // tick also keeps this countdown and the HUD timer row in the exact same dispatch (they'd drift if
        // driven by two independent listeners). See ScribeModSystem.TimerDisplayTick.
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

// ── ScribeTimerModeRow ────────────────────────────────────────────────────────

/// <summary>
/// A self-stateful row of two RadioButtons for In-game / Real-time mode selection.
/// Owns its own SetState so toggling the selection redraws only this widget, never
/// the sibling Button (which would crash ButtonState.PlaySound on remount).
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
        // Use the theme's default radio style; the mode labels take the player's Task Text Font
        // (BodyStyle already carries the resolved task font), not the fixed Caudex button face.
        var radioStyle = Theme.Of(context).RadioButtonStyle with
        {
            LabelStyle = Widget.BodyStyle,
        };

        // Stack the two options vertically. A horizontal row of both Caudex-Bold labels overflows the
        // narrow notebook page, pushing "Real time" off the right edge (behind the Settings nav button,
        // which then swallows its clicks). Vertical stacking keeps both on the page and selectable.
        // crossAxisAlignment.Start aligns the two dots; the parent form centers the whole group.
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

// ── ScribeBlinkText ──────────────────────────────────────────────────────────

/// <summary>
/// Blinks text on/off at 500 ms intervals using a VS game tick listener injected at
/// construction. Avoids AnimationController + Element.Owner so there's no mount-order
/// dependency that can silently crash in the shipped LibGUI build.
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

// ── ScribeTimerIcon ──────────────────────────────────────────────────────────

/// <summary>
/// Oscillates the timer icon ±30° using AnimatedRotation driven by a VS game tick listener.
/// Uses AnimatedRotation (implicit tween) rather than Transform.Rotate + a custom controller
/// to avoid a Skia matrix NaN/GPU crash when the icon size is zero at first layout.
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
        // Toggle every 100ms: +30° → -30° → +30° …
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

// ── ScribeGearworks ───────────────────────────────────────────────────────────

/// <summary>
/// The Timer-tab ambient clockwork gear-train (add-timer-gearworks). Two layers:
///
/// <para><b>The temporal pair (always moving).</b> A large + small interlocking toothed gear that advance
/// in a spring-wound per-tooth tick the whole time the Timer tab is open, decoupled from timer state —
/// they turn under Idle, Running, AND Fired. This is the lore hook: the temporal gears rotate eternally,
/// and that perpetual motion is what the timer mechanism <i>harnesses</i> for power. So they NEVER stop,
/// not even on fire.</para>
///
/// <para><b>The escape wheel (the regulator).</b> A big, mostly-hidden wheel that peeks up from behind the
/// pair. It is the clock's <i>escapement</i>: it engages only while a timer runs — latching into view when
/// a timer starts (powered by the always-turning pair), rotating while Running, and on Fired it briefly
/// <b>shudders then locks</b> (the escapement catching), while the pair keeps turning behind it. Cleared →
/// it slides back out. This latch-in/lock is the ONLY coupling between timer state and the gears
/// (design D6).</para>
///
/// <para><b>Rebuild-stable tick (design D4):</b> the tick phase is DERIVED from a monotonic clock
/// (<see cref="IClientWorldAccessor.ElapsedMilliseconds"/>), not accumulated in <see cref="State"/>.
/// <c>toothIndex = floor(elapsedMs / TickPeriodMs)</c>, and each gear targets
/// <c>toothIndex × stepAngle × ratio</c>. The Timer tab calls <c>ForceRebuild</c> on status
/// transitions (<c>RefreshTimerView</c>), which remounts this subtree; a phase kept only in State
/// would reset and make the gears jump. Deriving the target from the monotonic clock means the gears
/// resume at exactly the right angle no matter how often the tab rebuilds. The escape wheel's LOCK angle
/// is captured (<c>_lockedIndex</c>) at fire time so it freezes even as the monotonic clock rolls on.</para>
///
/// <para><b>Mesh via a single driver (design D3):</b> the pair is driven by the one <c>toothIndex</c>. Each
/// gear's angle = <c>direction × ratio × toothIndex × stepAngle</c>, where <c>ratio = ReferenceTeeth /
/// thisGearTeeth</c> and the direction alternates so neighbours counter-rotate. Positions are hand-tuned so
/// painted teeth interlock — a faked constant, not a physics solver (the Gearlock Firearms technique this
/// art is derived from).</para>
///
/// <para>Placeholder art note: the gear textures are the Gearlock Firearms gears as-shipped (large teal,
/// small brown), NOT re-skinned to teal and NOT glowing (material-identity art pass, tasks 1.2 / 2.4,
/// deferred by author request). The escape wheel currently REUSES the small brown gear scaled up — it wants
/// a distinct many-toothed great-wheel asset (cheapest path: generate the ring procedurally at load and dial
/// the tooth count), tracked as the immediate art follow-up. This widget renders whatever bitmaps the asset
/// locations resolve to; swapping art needs no code change.</para>
///
/// <para>Non-interactive (design D7): the gears are paint-only Containers with no gesture handlers, in their
/// own vertical region above the timer controls, so they cannot swallow a click or keystroke meant for a
/// control.</para>
/// </summary>
internal sealed class ScribeGearworks : StatefulWidget
{
    // Gearlock-derived placeholder art (see class remarks). Filed under textures/gui/ like the mod's
    // other GUI rasters (VS only scans its fixed AssetCategory folders; there is no "gui" category, but
    // "textures" is scanned — same reasoning as the backdrops / SVG icons).
    // Higher-resolution (512²) redraw of the teal temporal gear. Displayed at the same on-screen size as
    // before (the size is `largeSize` in Build, independent of source pixels), so the only change is crisper
    // pixels + downsampling instead of upscaling. The old 152×148 gear-temporal-large.png is kept in the repo
    // as a fallback/reference but no longer referenced.
    public static readonly AssetLocation LargeGearAsset = new("scribe", "textures/gui/gear-temporal-large-512.png");
    public static readonly AssetLocation SmallGearAsset = new("scribe", "textures/gui/gear-temporal-small.png");
    // The escape wheel reuses the small brown gear for now (author: "roughly in place"); wants its own
    // many-toothed asset later.
    public static readonly AssetLocation EscapeGearAsset = SmallGearAsset;

    // Authored framing art for the gearworks trim box (drawn separately from the gears). Both are optional:
    // if an asset is missing/unloadable the gearworks still renders (backdrop → nothing behind the gears;
    // border → the 1px fallback Container border). Author sizing (logical box is TrimBoxWidth×TrimBoxHeight =
    // 252×138): BACKDROP fills the trim box exactly, so author at a 2–3× multiple of 252×138 (e.g. 756×414) to
    // downsample crisply. BORDER-TRIM frames OUTSIDE the box with an even 8px margin → 268×154 logical, so
    // author at the matching multiple (e.g. 804×462) with a transparent 252×138 centre hole.
    public static readonly AssetLocation TrimBackdropAsset = new("scribe", "textures/gui/gearworks-backdrop.png");
    public static readonly AssetLocation TrimBorderAsset   = new("scribe", "textures/gui/gearworks-border.png");
    /// <summary>Very-nearly-transparent glass pane painted OVER the gears + backdrop (topmost child INSIDE the
    /// HardEdge clip), so the mechanism reads as seen through glass. Derived from vanilla clear glass
    /// (<c>glass/plain.png</c>, its flat cool-gray tint minus the leaded dark border), dropped to ~6% alpha
    /// with a soft diagonal sheen streak. Fills the trim box exactly like the backdrop → author at the same
    /// 2–3× multiple of 252×138 (756×414). Optional: absent → no glass overlay.</summary>
    public static readonly AssetLocation TrimGlassAsset    = new("scribe", "textures/gui/gearworks-glass.png");
    /// <summary>Even border margin (LOGICAL px) the trim-border art adds on EACH side of the trim box, so the
    /// border PNG is (TrimBoxWidth + 2×this) × (TrimBoxHeight + 2×this) = 268×154 at the 252×138 box. Must match
    /// how the border art is drawn.</summary>
    public const float TrimBorderMargin = 8f;

    public readonly ICoreClientAPI Capi;
    public readonly ScribeModSystem ModSystem;
    public readonly float Scale;
    public readonly TimerStatus Status;
    /// <summary>Monotonic-clock timestamp (ms) of the last Idle→Running transition, or 0 if never/Idle.
    /// The escape wheel's start-slide progress is derived from this so it survives the ForceRebuild remount
    /// (design D8a) — a State-held flag would reset on remount and the slide would snap.</summary>
    public readonly long EngageStartMs;
    /// <summary>Monotonic-clock timestamp (ms) of the last →Fired transition, or 0. The wheel freezes at the
    /// angle it had reached at this instant (design D4/D8) instead of rewinding.</summary>
    public readonly long FireLockMs;

    public ScribeGearworks(ICoreClientAPI capi, ScribeModSystem modSystem, float scale, TimerStatus status,
        long engageStartMs, long fireLockMs)
    { Capi = capi; ModSystem = modSystem; Scale = scale; Status = status; EngageStartMs = engageStartMs; FireLockMs = fireLockMs; }

    public override State CreateState() => new ScribeGearworksState();
}

internal sealed class ScribeGearworksState : State<ScribeGearworks>
{
    // Tooth counts MUST match the actual art or the faked mesh drifts: the re-skinned teal temporal gear
    // (gear-temporal-large-512.png) has 12 teeth — matching the spec's "apparent 12 teeth (a 30°/tooth step)"
    // (was 11 for the old Gearlock placeholder art; re-counted when the 512² asset landed). Each tick advances
    // BOTH gears by one of their OWN teeth, so the large steps 2π/12 (= 30°) and the small steps 2π/8 per tick
    // (see the Ratio derivation below).
    private const int ReferenceTeeth = 12;   // large teal gear (counted from gear-temporal-large-512.png)
    private const int SmallTeeth     = 8;    // small gear
    // Escape wheel: steps by the great wheel's OWN teeth so one tick advances exactly one tooth (tooth-honest,
    // task 5.5). DEV: the count is now .geartune-tunable, so the step is derived at paint time from the live
    // tuning value (GearTuning.WheelTeeth) in Build — see `escapeStep` there — rather than a compile-time
    // constant, so tuning the tooth count keeps the advance honest.

    // One discrete tooth-step every TickPeriodMs. Set to 1000 so a tooth-step lands once per REAL
    // second — matching the visible cadence of a RealTime countdown (author request: "the ticking
    // should be the same length of time as a second on the timer"). InGame-mode timers drain faster
    // (≈30 in-game s per real s), so their displayed number moves quicker than one tooth/sec — an
    // accepted placeholder mismatch (ticking one tooth/sec stays readable rather than blurring). The
    // AnimatedRotation tween settles within SettleMs (< TickPeriodMs) so each step visibly
    // snaps-and-settles before the next fires.
    private const long TickPeriodMs = 1000;
    private const int  SettleMs     = 520;
    // How often we re-check the monotonic clock for a tooth-index change. Small so the snap starts within a
    // few ms of the real-second boundary (design D11: the idle dwell + the SettleMs snap should sum to the
    // 1000 ms period and begin on the second mark). At 16 ms the perceived phase error is ~one frame.
    private const int  PollMs = 16;

    // Escape-wheel engage slide (design D8a): on Idle→Running the wheel slides from its resting peek up to its
    // live peek over this duration. Progress is derived from Widget.EngageStartMs (host-owned, survives the
    // ForceRebuild remount) rather than a State flag, so it does NOT snap on the remount that delivers Running.
    private const long EngageSlideMs = 420;
    // Escape-wheel fire retract (design D14): on Running→Fired the wheel slides its peek back DOWN from the
    // live position to the resting position over this duration — the mirror of the engage slide — so the
    // mechanism visibly disengages as the timer completes. Progress derived from Widget.FireLockMs (host-owned,
    // survives the ForceRebuild remount) for the same rebuild-stability reason as the engage slide.
    private const long FireRetractMs = 420;

    private long _tickId;
    private long _toothIndex;

    // Fire → shudder → lock. The lock ANGLE is derived from the host-owned Widget.FireLockMs timestamp (the
    // exact escape-tooth index the running wheel had reached at the fire instant), NOT re-captured in State.
    // This is what stops the wheel REWINDING on fire (task 5.1): State is torn down + remounted on the
    // Running→Fired ForceRebuild, and AnimatedRotation seeds Begin==End on a fresh mount (it only tweens
    // across a reconcile — confirmed in ImplicitlyAnimatedWidget source), so any angle that isn't exactly the
    // last running angle makes the fresh mount SNAP there — visible as a rewind. Deriving the lock angle from
    // the same clock formula the running wheel used guarantees it freezes precisely where it was.
    // The shudder is a short decaying oscillation added ON TOP via a RAW Transform.Rotate (NOT AnimatedRotation
    // — a 70ms step fed through the 520ms tween is smoothed away, why it read as invisible the first playtest).
    private float _shudderAngle;
    private int   _shudderStep;
    private long  _shudderTickId;
    // Decaying oscillation (radians) applied over ~5 steps at ShudderStepMs each, then 0 (locked).
    private static readonly float[] ShudderSequence = { 0.14f, -0.10f, 0.06f, -0.03f, 0f };
    private const int ShudderStepMs = 70;

    // Cast-shadow depth (design D13, tuned D17), painted via ScribeGearEffect (silhouette recolour of the gear
    // texture): a dark silhouette offset down-right beneath each gear → cheap 3D depth, contained by the HardEdge
    // clip. Sharpened in playtest-4 (task 7.2: blur 2→1, offset 3→1.5 → crisp near-contact shadow). Opacity is
    // PER-GEAR (task 7.3): the teal temporal gear keeps the full 0x70; the three steel gears use 2/3 of that
    // (~0x4B) so the temporal gear stays the visual focus. The teal gear's outer glow was removed (task 7.5/D18).
    private static readonly SKColor TealShadowTint  = new(0x00, 0x00, 0x00, 0x70);   // ~44% black (temporal gear)
    private static readonly SKColor SteelShadowTint = new(0x00, 0x00, 0x00, 0x4B);   // ~29% black (2/3 of 0x70)
    private const float ShadowOffset = 1.5f;   // px (×scale) down-right — halved (task 7.2)
    private const float ShadowBlur   = 1f;     // sigma (×scale) — halved (task 7.2)

    private static readonly float StepAngleLarge  = MathF.PI * 2f / ReferenceTeeth;   // one large tooth (2π/12 = 30°)
    private const float Ratio = (float)ReferenceTeeth / SmallTeeth;                    // small steps ratio× faster

    // The escape wheel is ALWAYS visible (author request). "Engaged" no longer gates its presence —
    // it only decides how far the wheel peeks up and whether it turns. Idle: a small resting peek,
    // stationary. Running/Fired: a larger peek so it slides a little further into view as the mechanism
    // "goes live". Keeping it always-visible sidesteps the fire glitch entirely: because
    // RefreshTimerView calls ForceRebuild on every status transition and stock Animated* widgets SNAP on
    // a fresh mount (they only tween across a reconcile — VSAPI-NOTES §LibGUI), an ever-hidden→visible
    // latch would re-slide-in from nothing on each remount. An always-on wheel just repaints in place.
    private bool Engaged => Widget.Status != TimerStatus.Idle;   // wheel turns + peeks further while Running/Fired
    private bool Locked  => Widget.Status == TimerStatus.Fired;

    public override void InitState()
    {
        base.InitState();
        _toothIndex = CurrentToothIndex();
        // Self-tick: repaint when the monotonic tooth index advances (the per-second snap) OR while the
        // engage slide is still in flight (so the wheel visibly glides up over EngageSlideMs). This animates
        // the widget without the host dialog rebuilding each frame (ScribeTimerIcon precedent).
        _tickId = Widget.Capi.Event.RegisterGameTickListener(_ =>
        {
            long next = CurrentToothIndex();
            bool sliding = EngageSlideProgress() < 1f || (Locked && FireRetractProgress() < 1f);
            if (next != _toothIndex)
            {
                PlayTickTock(next);            // one faint tick-tock per real second (task 7.7)
                SetState(() => _toothIndex = next);
            }
            else if (sliding)
            {
                SetState(() => _toothIndex = next);
            }
        }, PollMs);

        // DEV: rebuild live when a .geartune knob changes so layout nudges show without reopening.
        Widget.ModSystem.GearTuningChanged += OnGearTuningChanged;

        if (Locked)
        {
            // Run a one-shot shudder on top of the (timestamp-derived) frozen angle. Because RefreshTimerView
            // ForceRebuilds on the Running→Fired transition, InitState runs fresh at the moment of firing — so
            // this one-shot fires exactly once per fire, which is what we want.
            _shudderStep = 0;
            _shudderAngle = ShudderSequence[0];
            _shudderTickId = Widget.Capi.Event.RegisterGameTickListener(OnShudder, ShudderStepMs);
        }
    }

    /// <summary>Escape-tooth index the RUNNING wheel had reached at a given monotonic timestamp — the shared
    /// clock formula (D4). Used both for the live angle (now) and the frozen lock angle (Widget.FireLockMs),
    /// so the lock is exactly the last running angle → no rewind on fire (task 5.1).</summary>
    private static long ToothIndexAt(long ms) => ms / TickPeriodMs;

    /// <summary>0→1 progress of the Idle→Running engage slide, derived from the host-owned EngageStartMs so it
    /// survives the ForceRebuild remount (design D8a). 1 (fully engaged) when never started or long past.</summary>
    private float EngageSlideProgress()
    {
        if (Widget.EngageStartMs <= 0) return 1f;
        long dt = Widget.Capi.World.ElapsedMilliseconds - Widget.EngageStartMs;
        if (dt >= EngageSlideMs) return 1f;
        if (dt <= 0) return 0f;
        return (float)dt / EngageSlideMs;
    }

    /// <summary>0→1 progress of the Fired retract slide (live peek → resting peek), derived from the host-owned
    /// FireLockMs so it survives the ForceRebuild remount (design D14). Only meaningful while Locked; 0 at the
    /// fire instant (fully engaged), 1 once fully withdrawn.</summary>
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

    /// <summary>DEV: a .geartune knob changed — rebuild so Build re-reads the tuning values (live preview).</summary>
    private void OnGearTuningChanged() => SetState(() => { });

    public override void Dispose()
    {
        if (_tickId != 0)        { Widget.Capi.Event.UnregisterGameTickListener(_tickId);        _tickId = 0; }
        if (_shudderTickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_shudderTickId); _shudderTickId = 0; }
        Widget.ModSystem.GearTuningChanged -= OnGearTuningChanged;
        base.Dispose();
    }

    private long CurrentToothIndex() => Widget.Capi.World.ElapsedMilliseconds / TickPeriodMs;

    // Tick-tock (task 7.7 / D19): a faint clockwork beat once per real second, alternating two tones for the
    // tick-tock cadence. Interim asset = vanilla game:sounds/tick.ogg at two pitches (higher "tick" / lower
    // "tock") to avoid shipping+attributing new audio; a dedicated pair of samples is a later nicety. Routed
    // through the Effect (Sound) channel via EnumSoundType.Sound + RelativePosition so the effect-volume slider
    // controls it; faint via a low Volume; muted by the "Mute Scribe UI sounds" preference.
    private static readonly AssetLocation TickSound = new("game", "sounds/tick");
    private const float TickVolume = 0.49f;   // faint (0.35 × 1.4 — author bumped +40% relative)
    private const float TickPitch  = 1.15f;   // higher "tick"
    private const float TockPitch  = 0.85f;   // lower "tock"

    /// <summary>Play one tick-tock beat for the given tooth index (even → tick, odd → tock, so the two tones
    /// alternate deterministically across remounts). No-op when the player has muted Scribe UI sounds. Fire-and-
    /// forget: the loaded sound self-disposes when it finishes (SoundParams.DisposeOnFinish defaults true).</summary>
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

        // Region + gear sizing (draft/movable — final placement is an M2 tuning pass, design D7). The region
        // is tall enough to host the pair (upper band) plus the escape wheel's peek from below. The BOUNDING
        // BOX (regionW/regionH) is fixed; the gears themselves are 25% larger than the playtest-3 sizes so they
        // fill it more and mesh (task 7.4: 78→97.5, 52→65, 170→212.5).
        // DEV live-tuning: region dims + gear scales come from the .geartune window (defaults = the values
        // that were baked in here — trim box 200×130, gear scales 1.0). Fold these back into constants + delete
        // ScribeGearTuning when the layout is finalized.
        var tuning = Widget.ModSystem.GearTuning;
        float regionW   = tuning.TrimBoxWidth  * scale;   // DEV .geartune knob (default 200)
        float regionH   = tuning.TrimBoxHeight * scale;   // DEV .geartune knob (default 130)
        float largeSize = 97.5f  * scale * tuning.LargeGearScale;   // 78 × 1.25, then ×scale knob (default 1)
        float smallSize = 65f    * scale * tuning.SmallGearScale;   // 52 × 1.25, then ×scale knob (default 1)
        float escapeSize = 212.5f * scale;  // 170 × 1.25 (big wheel; only its top arc shows — rest clipped below)

        // Symmetric train (design D15, task 6.7): the teal temporal gear is the CENTERED driver, dropped LOW so
        // it overlaps the escape wheel that peeks up from below; it is flanked by TWO small steel gears — one on
        // each side — that mesh with it. The playtest-3 spacing read as too wide to be credible (teeth floated
        // apart); tightened (task 7.1) so the small-gear teeth overlap the teal gear's teeth: bigger toothKiss
        // (deeper horizontal tuck) and the smalls raised much closer to in-line with the teal gear's row.
        // DEV live-tuning: the small-gear placement + teal-gear Y knobs come from the .geartune window
        // (defaults = the values that were baked in here). Fold these back into constants + delete
        // ScribeGearTuning when the layout is finalized.
        float toothKiss = tuning.SmallGearOverlapX * scale;   // how deep each small tucks under the teal gear
        float largeLeft = (regionW - largeSize) / 2f;         // centered horizontally
        float largeTop  = tuning.LargeGearY * scale;          // DEV .geartune knob (default 30) — LOW → overlaps wheel
        float smallTop  = tuning.SmallGearY * scale;          // midpoint sits just below the teal gear's
        float leftSmallLeft  = largeLeft - smallSize + toothKiss;
        float rightSmallLeft = largeLeft + largeSize - toothKiss;

        // Escape wheel: ALWAYS visible (author request), centered horizontally, peeking up from the bottom
        // edge. The remainder extends past regionH and is clipped away (the eventual glass panel, M2, frames
        // this arc). Two peek heights: a small RESTING peek when Idle, a larger LIVE peek while the timer runs.
        // On Idle→Running the wheel SLIDES up between them over EngageSlideMs (design D8a); on Running→Fired it
        // SLIDES back DOWN live→resting over FireRetractMs (design D14, task 6.5) so it visibly disengages as
        // the timer completes. Both slide progresses are derived from host-owned timestamps (Widget.EngageStartMs
        // / Widget.FireLockMs → survive the ForceRebuild remount), NOT State flags, so they glide not snap.
        float restingPeek = tuning.WheelIdlePeek * scale;    // DEV .geartune knob (default 34)
        float livePeek    = tuning.WheelActivePeek * scale;  // DEV .geartune knob (default 60)
        float escapePeek;
        if (Locked)
        {
            // Fired: retract from the live peek back to the resting peek (mirror of the engage slide).
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
        float escapeTop  = regionH - escapePeek;         // body runs escapeTop..escapeTop+escapeSize (clipped)

        // Monotonic-derived targets. The teal driver turns one way; BOTH smalls counter-rotate against it
        // (same sign as each other, opposite the teal), ratio× faster (D3). The small gears also carry a DEV
        // .geartune resting-angle offset (degrees→radians) so their teeth can be phased to mesh at rest; it's
        // added on top of the live angle, so they still spin — it only shifts the starting phase. The offset is
        // MIRRORED: the left small gets +X, the right small gets −X, because the two flank the teal gear
        // symmetrically — the same signed offset would rotate them the same way and throw one out of mesh.
        float largeAngle    =  _toothIndex * StepAngleLarge;
        float smallSpin     = -_toothIndex * StepAngleLarge * Ratio;
        float smallRestPhase = tuning.SmallGearAngle * (MathF.PI / 180f);
        float leftSmallAngle  = smallSpin + smallRestPhase;
        float rightSmallAngle = smallSpin - smallRestPhase;

        var children = new List<Widget>();

        // Authored textured BACKDROP FIRST so it paints behind every gear, filling the trim box exactly (it is
        // inside the HardEdge clip, so any bleed is trimmed). Optional: absent → nothing behind the gears (the
        // pre-art look). Wrapped in ScribeResetPaintColor so the DrawMaskedBox SharedPaint-alpha leak can't
        // render it see-through/uneven (the tablet-backdrop bug; same fix the gears + dialog backdrops use).
        var backdropBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimBackdropAsset);
        if (backdropBmp is not null)
            children.Add(new Positioned(left: 0f, top: 0f, width: regionW, height: regionH,
                child: new ScribeResetPaintColor(
                    new Container(style: new BoxStyle { Texture = backdropBmp, Width = regionW, Height = regionH }))));

        // Escape wheel FIRST so it paints BEHIND the pair. Always present; only its motion depends on state.
        //   Idle:    stationary at tooth 0 (the wheel is disengaged — the regulator isn't being driven).
        //   Running: rotates with the monotonic clock, one escape-tooth per tick.
        //   Fired:   FROZEN at the tooth index it had reached at the fire instant (Widget.FireLockMs) — the
        //            same clock formula the running wheel used, so it locks exactly where it was (no rewind,
        //            task 5.1) — with a brief decaying shudder on top, while it retracts (peek) to idle.
        // NOTE the SIGN: +index here (task 5.6 — the wheel spins OPPOSITE the teal driver).
        long escapeIndex = Locked ? ToothIndexAt(Widget.FireLockMs)
                         : Engaged ? _toothIndex
                         : 0L;
        // Per-tick step = one of the wheel's OWN teeth so it stays tooth-honest. DEV: the tooth COUNT is
        // .geartune-tunable, so derive the step from the live count (not the compile-time StepAngleEscape) —
        // otherwise tuning the count would desync the per-tick advance from one visible tooth. + a resting-angle
        // offset (degrees→radians) so the wheel's teeth can be phased to mesh with the teal gear; added on top of
        // the live angle, so it just shifts the starting phase (still spins).
        float escapeStep = MathF.PI * 2f / (int)tuning.WheelTeeth;
        float escapeBase = escapeIndex * escapeStep + tuning.WheelAngle * (MathF.PI / 180f);
        // The escape wheel is the procedurally generated "great wheel" (blocky, many-toothed, negative-space
        // style); falls back to the reused small-gear PNG if generation is unavailable (e.g. server-side).
        var escapeBmp = Widget.ModSystem.GetProceduralGreatWheel()
                        ?? Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.EscapeGearAsset);
        if (escapeBmp is not null)
        {
            // The shudder is an INSTANT jolt (raw Transform.Rotate about center), layered over the settling
            // base rotation — feeding it through AnimatedRotation's 520ms tween would smooth it away before it
            // landed. Safe from the zero-size Skia-matrix crash (fixed non-zero child size). Only while Locked.
            System.Func<Widget> maybeShudder = () =>
            {
                Widget w = MakeGear(escapeBmp, escapeSize, escapeBase);
                if (Locked && _shudderAngle != 0f)
                    w = Transform.Rotate(child: w, radians: _shudderAngle, alignment: Alignment.Center);
                return w;
            };
            // Shadow silhouette (steel gear casts a dark, contained shadow) beneath, then the opaque wheel. The
            // wheel is wrapped in ScribeResetPaintColor so it paints through a clean opaque paint — the shadow's
            // SrcIn tint (or any prior op) would otherwise modulate it see-through (task 7.6, D16).
            AddShadow(children, maybeShudder(), escapeLeft, escapeTop, escapeSize, scale, SteelShadowTint);
            children.Add(new Positioned(left: escapeLeft, top: escapeTop, width: escapeSize, height: escapeSize,
                child: new ScribeResetPaintColor(maybeShudder())));
        }

        // Two flanking small steel gears (shadow beneath each). Below the teal gear in paint order so the
        // centered teal driver sits on top and its overlap of the escape wheel reads cleanly.
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

        // The teal temporal gear LAST (on top) — its own cast shadow beneath (the darker TealShadowTint), then
        // the opaque gear. The outer glow was removed (task 7.5/D18 — it didn't read well). Wrapped in
        // ScribeResetPaintColor so it paints fully opaque regardless of the shadow's tint left on the paint.
        var largeBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.LargeGearAsset);
        if (largeBmp is not null)
        {
            AddShadow(children, MakeGear(largeBmp, largeSize, largeAngle), largeLeft, largeTop, largeSize, scale, TealShadowTint);
            children.Add(new Positioned(left: largeLeft, top: largeTop, width: largeSize, height: largeSize,
                child: new ScribeResetPaintColor(MakeGear(largeBmp, largeSize, largeAngle))));
        }

        // Glass pane LAST (topmost) but still INSIDE the clip, so the very-nearly-transparent overlay covers the
        // whole mechanism (gears + backdrop) and its edges are trimmed to the trim box. It reads as looking into
        // the mechanism through glass. Non-interactive (a plain textured Container never hit-tests). Optional:
        // absent → bare gears. Wrapped in ScribeResetPaintColor for the same DrawMaskedBox/SharedPaint leak fix.
        var glassBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimGlassAsset);
        if (glassBmp is not null)
            children.Add(new Positioned(left: 0f, top: 0f, width: regionW, height: regionH,
                child: new ScribeResetPaintColor(
                    new Container(style: new BoxStyle { Texture = glassBmp, Width = regionW, Height = regionH }))));

        // If nothing loaded, render an empty region rather than crash (asset-miss guard).
        if (children.Count == 0)
            return new SizedBox(width: regionW, height: regionH);

        // Clip the region so the escape wheel's hidden body (below the peek) — and every shadow — never overruns
        // the form below. The authored border-trim art (added below) frames it; when that art is ABSENT we fall
        // back to a 1px parchment border on the Container so the trim box is still visible while tuning.
        var borderBmp = Widget.ModSystem.GetGuiTextureBitmap(ScribeGearworks.TrimBorderAsset);
        Widget clipped = new Container(
            style: new BoxStyle
            {
                Width           = regionW,
                Height          = regionH,
                // Fallback outline only when there's no border art to frame the box.
                BorderThickness = borderBmp is null ? scale : 0f,
                BorderColor     = new Vector4(0.55f, 0.42f, 0.2f, 0.9f),   // parchment-brown outline
            },
            child: new Clip(
                clipBehavior: ClipBehavior.HardEdge,
                child: new SizedBox(width: regionW, height: regionH, child: new Stack(children))));

        // Border-trim frame: an authored PNG that sits OUTSIDE the trim box with an even TrimBorderMargin (8px
        // logical) on every side → its logical size is (regionW + 2·margin) × (regionH + 2·margin) = 268×154 at
        // the 252×138 box. Its centre is transparent so the clipped gears/backdrop show through. Painted LAST
        // (on top) so its inner edge overlaps the box edge cleanly. When the art is absent we just render the
        // clipped region with its fallback 1px border (no frame).
        Widget region;
        if (borderBmp is not null)
        {
            float margin  = ScribeGearworks.TrimBorderMargin * scale;
            float borderW = regionW + 2f * margin;
            float borderH = regionH + 2f * margin;
            region = new SizedBox(width: borderW, height: borderH, child: new Stack(new List<Widget>
            {
                // Clipped gear region, inset by the margin so it's centred inside the frame.
                new Positioned(left: margin, top: margin, width: regionW, height: regionH, child: clipped),
                // The frame PNG over the full bordered area (transparent centre hole shows the gears).
                new Positioned(left: 0f, top: 0f, width: borderW, height: borderH,
                    child: new ScribeResetPaintColor(
                        new Container(style: new BoxStyle { Texture = borderBmp, Width = borderW, Height = borderH }))),
            }));
        }
        else
        {
            region = clipped;
        }

        // DEV .geartune "trim box Y": a PAINT-ONLY vertical nudge (Transform.Translate), NOT a margin/padding —
        // it moves only the gear region and does NOT reflow the timer form/countdown below (a top margin would
        // consume main-axis height and shrink the content). Positive = down. Skip the wrapper when 0 (no-op).
        float trimY = tuning.TrimBoxY * scale;
        return trimY == 0f ? region : Transform.Translate(child: region, offset: new Vector2(0f, trimY));
    }

    /// <summary>Push a cast-shadow silhouette of <paramref name="gear"/> into <paramref name="children"/>,
    /// offset down-right and recoloured to a dark translucent silhouette via <see cref="ScribeGearEffect"/>
    /// (design D13, tuned D17). <paramref name="tint"/> is per-gear (steel gears lighter than the teal gear,
    /// task 7.3). The offset/blur scale with the gearworks; the outer HardEdge clip keeps it contained.</summary>
    private static void AddShadow(List<Widget> children, Widget gear, float left, float top, float size, float scale, SKColor tint)
    {
        children.Add(new Positioned(
            left: left + ShadowOffset * scale,
            top:  top  + ShadowOffset * scale,
            width: size, height: size,
            child: new ScribeGearEffect(gear, tint, ShadowBlur * scale)));
    }

    /// <summary>One gear: a self-loaded raster in a fixed-size textured Container, wrapped in AnimatedRotation
    /// so per-tooth target changes spring-settle. A fresh instance per call — the shadow/glow silhouettes need
    /// their OWN rotation widget (a widget can't appear twice in the tree); identical angle inputs keep them in
    /// visual lock-step with the gear.</summary>
    private Widget MakeGear(SKBitmap bmp, float size, float angle)
        => new AnimatedRotation(
            angle: angle,
            duration: TimeSpan.FromMilliseconds(SettleMs),
            curve: Curves.EaseOutBack,
            child: new Container(style: new BoxStyle { Texture = bmp, Width = size, Height = size }));
}
