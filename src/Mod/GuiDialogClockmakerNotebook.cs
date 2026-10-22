using Gui.Core.Layout;
using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Painting;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

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
