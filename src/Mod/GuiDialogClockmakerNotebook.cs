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

        var colors   = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float scale  = ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float body   = ScribeRowConstants.BaseWindowFontSize * scale;
        float big    = body * 2.0f;
        float small  = body * 0.8f;

        var bodyStyle  = new TextStyle { FontSize = body,  Color = colors.OnSurface };
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
            string timeText = status == TimerStatus.Fired
                ? "00:00"
                : FormatDuration(_localRemaining);

            Widget timeWidget = status == TimerStatus.Fired
                ? new ScribeBlinkText(timeText, bigStyle)
                : new Text(timeText, bigStyle);

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
                    Color = colors.OnSurface,
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

        Widget labelRow = new Column(
            spacing: 4,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-gui-timer-label"), smallStyle),
                new TextField(
                    labelController,
                    style: new TextFieldStyle
                    {
                        TextStyle = bodyStyle,
                        Height = fieldH,
                    },
                    onChanged: text => _pendingLabel = text),
            });

        // H / M / S steppers.
        Widget timeRow = new Row(
            spacing: 8,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.End,
            children: new Widget[]
            {
                BuildTimeUnit("th", _pendingHours,   0, 23, 1,  "h", v => _pendingHours   = v, colors, bodyStyle, smallStyle, fieldH, fieldW),
                BuildTimeUnit("tm", _pendingMinutes, 0, 59, 1,  "m", v => _pendingMinutes = v, colors, bodyStyle, smallStyle, fieldH, fieldW),
                BuildTimeUnit("ts", _pendingSecs,    0, 55, 5,  "s", v => _pendingSecs    = v, colors, bodyStyle, smallStyle, fieldH, fieldW),
            });

        // Mode toggle (In-game / Real-time).
        Widget modeRow = new Row(
            spacing: 12,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                ModeCheckbox(TimerMode.InGame,   "scribe:scribe-gui-timer-mode-ingame",   colors, bodyStyle, scale),
                ModeCheckbox(TimerMode.RealTime, "scribe:scribe-gui-timer-mode-realtime", colors, bodyStyle, scale),
            });

        bool canStart = _pendingHours > 0 || _pendingMinutes > 0 || _pendingSecs > 0;
        var btnStyle  = new TextStyle
        {
            FontSize   = ScribeRowConstants.BaseWindowFontSize * scale,
            FontFamily = ScribeTaskFont.ButtonFamily,
            Color      = canStart ? colors.OnSurface : colors.OnSurfaceVariant,
        };

        Widget startBtn = new Button(
            child: new Text(Lang.Get("scribe:scribe-gui-timer-start"), btnStyle),
            onTap: canStart ? (_ => SendStartTimer()) : null);

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
        ColorScheme colors, TextStyle bodyStyle, TextStyle smallStyle,
        float fieldH, float fieldW)
    {
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
                            ForceRebuild();
                        },
                        style: new BoxStyle { Height = fieldH, Width = fieldW },
                        focusNode: _timerFocus.NodeFor(id),
                        autoFocus: _timerFocus.ShouldFocus(id),
                        onStepped: () => _timerFocus.ArmAutoFocus(id),
                        clamp: v => Math.Clamp((int)MathF.Round(v / step) * step, min, max))),
                new Text(suffix, smallStyle),
            });
    }

    private Widget ModeCheckbox(TimerMode mode, string langKey, ColorScheme colors, TextStyle bodyStyle, float scale)
    {
        bool active = _pendingMode == mode;
        return new GestureDetector(
            onTap: _ =>
            {
                _pendingMode = mode;
                ForceRebuild();
            },
            child: new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Min,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    new Checkbox(
                        value: active,
                        onChanged: _ => { _pendingMode = mode; ForceRebuild(); },
                        size: ScribeRowConstants.RowCheckboxSize * scale),
                    new Text(Lang.Get(langKey), bodyStyle),
                }));
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

    private void OnTimerChanged()
    {
        _resyncRemaining = true;
        // RefreshTimerView is called by ScribeModSystem directly.
    }

    private void OnTimerTick(float dt)
    {
        var timer = modSystem.MyTimer;
        if (timer?.Status != TimerStatus.Running) return;
        _localRemaining = Math.Max(0, _localRemaining - dt);
        if (IsTimerView && IsOpened()) ForceRebuild();
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

// ── ScribeBlinkText ──────────────────────────────────────────────────────────

/// <summary>
/// A stateful widget that blinks its text on/off at ~500 ms intervals using a looping
/// AnimationController. Modeled on ScribeFadeText — owns its controller in InitState so it
/// survives the HUD's ForceRebuild remounts without restarting the animation.
/// </summary>
internal sealed class ScribeBlinkText : StatefulWidget
{
    public readonly string Text;
    public readonly TextStyle Style;

    public ScribeBlinkText(string text, TextStyle style) { Text = text; Style = style; }

    public override State CreateState() => new ScribeBlinkTextState();
}

internal sealed class ScribeBlinkTextState : State<ScribeBlinkText>
{
    private AnimationController? _controller;

    public override void InitState()
    {
        base.InitState();
        _controller = new AnimationController(TimeSpan.FromMilliseconds(500), Element.Owner!.GetTickerProvider());
        _controller.OnValueChanged  += _ => Element.MarkNeedsBuild();
        _controller.OnStatusChanged += s => { if (s == AnimationStatus.Completed) _controller?.Forward(); };
        _controller.Forward();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
    {
        float opacity = (float)(_controller?.Value ?? 0) < 0.5f ? 1f : 0f;
        return new Opacity(opacity, new Gui.Widgets.Basic.Text(Widget.Text, Widget.Style));
    }
}

// ── ScribeTimerIcon ──────────────────────────────────────────────────────────

/// <summary>
/// A stateful widget that continuously rotates a clock icon ±30° using a looping
/// AnimationController driven by a sine mapping.
/// </summary>
internal sealed class ScribeTimerIcon : StatefulWidget
{
    public readonly float Size;
    public readonly Vector4 Color;

    public ScribeTimerIcon(float size, Vector4 color) { Size = size; Color = color; }

    public override State CreateState() => new ScribeTimerIconState();
}

internal sealed class ScribeTimerIconState : State<ScribeTimerIcon>
{
    private AnimationController? _controller;

    public override void InitState()
    {
        base.InitState();
        _controller = new AnimationController(TimeSpan.FromMilliseconds(600), Element.Owner!.GetTickerProvider());
        _controller.OnValueChanged  += _ => Element.MarkNeedsBuild();
        _controller.OnStatusChanged += s => { if (s == AnimationStatus.Completed) _controller?.Forward(); };
        _controller.Forward();
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        base.Dispose();
    }

    public override Widget Build(BuildContext context)
    {
        float t     = (float)(_controller?.Value ?? 0);
        float angle = MathF.Sin(t * MathF.PI * 2f) * (MathF.PI / 6f); // ±30°
        return Transform.Rotate(
            new ScribeVsIconGlyph("scribetimer", Widget.Size, Widget.Color),
            angle,
            Alignment.Center);
    }
}
