using System;
using System.Collections.Generic;
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

    private TimerMode _pendingMode = TimerMode.InGame;
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

    public GuiDialogClockmakerNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(host, capi)
    {
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

        var bodyStyle  = new TextStyle { FontSize = body,  Color = colors.OnSurface, FontFamily = taskFont };
        var bigStyle   = new TextStyle { FontSize = big,   Color = colors.OnSurface, FontFamily = taskFont };
        var labelStyle = new TextStyle { FontSize = body,  Color = colors.OnSurface with { W = colors.OnSurface.W * 0.7f }, FontFamily = taskFont };
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
                    capi: capi);

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

        return new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { new Divider(), new Expanded(new Center(child: content)) }));
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
            onSelect: m => _pendingMode = m,
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
                modeRow,
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
    protected internal new void RefreshTimerView()
    {
        var newStatus = modSystem.MyTimer?.Status ?? TimerStatus.Idle;
        if (newStatus == _lastKnownStatus) return;
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
/// (e.g. the Stop Timer button) are never remounted while the timer is running.</summary>
internal sealed class ScribeCountdownText : StatefulWidget
{
    public readonly double InitialSeconds;
    public readonly TextStyle Style;
    public readonly ICoreClientAPI Capi;

    public ScribeCountdownText(double initialSeconds, TextStyle style, ICoreClientAPI capi)
    { InitialSeconds = initialSeconds; Style = style; Capi = capi; }

    public override State CreateState() => new ScribeCountdownTextState();
}

internal sealed class ScribeCountdownTextState : State<ScribeCountdownText>
{
    private double _remaining;
    private long _tickId;

    public override void InitState()
    {
        base.InitState();
        _remaining = Widget.InitialSeconds;
        _tickId = Widget.Capi.Event.RegisterGameTickListener(dt =>
            SetState(() => _remaining = Math.Max(0, _remaining - dt)), 250);
    }

    public override void Dispose()
    {
        if (_tickId != 0) { Widget.Capi.Event.UnregisterGameTickListener(_tickId); _tickId = 0; }
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
        => new Row(
            spacing: 16,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                new RadioButton<int>(
                    value: (int)TimerMode.InGame,
                    groupValue: _current,
                    onChanged: Select,
                    label: Lang.Get("scribe:scribe-gui-timer-mode-ingame"),
                    size: Widget.CheckboxSize),
                new RadioButton<int>(
                    value: (int)TimerMode.RealTime,
                    groupValue: _current,
                    onChanged: Select,
                    label: Lang.Get("scribe:scribe-gui-timer-mode-realtime"),
                    size: Widget.CheckboxSize),
            });
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
