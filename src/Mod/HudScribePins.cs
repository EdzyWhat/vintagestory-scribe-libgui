using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Animations;    // AnimatedOpacity, Curves
using Gui.Widgets.Basic;         // Text, Container
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, GestureDetector
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;   // Lang, RuntimeEnv

namespace Scribe;

/// <summary>
/// The on-screen pinned-task HUD, built on LibGUI (modid <c>gui</c>). It renders THIS player's own
/// pins — from the server-pushed set cached in <see cref="ScribeModSystem.MyPins"/> — as a short,
/// automatically-ordered checklist anchored to the top-right corner, legible over the world via a
/// soft text glow rather than a background plate.
///
/// <para>HUD semantics come straight from <see cref="GuiBase"/> + <c>EnumDialogType.HUD</c> (the
/// <c>GuiGlobalOverlay</c> precedent in the LibGUI source): it never steals keyboard focus, Escape
/// never closes it, and it renders behind real dialogs (so the lectern dialog sits on top). Mouse
/// events stay ON so the row checkboxes are clickable, but <see cref="ShouldReceiveKeyboardEvents"/>
/// is false so gameplay keys still flow. The window is a shrink-wrap (content-sized), non-draggable,
/// non-opaque overlay re-anchored to the corner each frame.</para>
///
/// <para><b>Visibility is self-managed</b> off <see cref="ScribeModSystem.MyPinsChanged"/>: the HUD
/// opens itself when the player has ≥1 pin and closes at zero (design D6 "hidden vs collapsed"). The
/// collapse preference (<see cref="ScribePlayerSettings.HudCollapsed"/>) minimizes it to a still-
/// clickable header instead of hiding it; it is a client-local preference, so toggling it just mutates
/// the held config (no network). <see cref="ScribeModSystem"/> constructs one of these in
/// <c>StartClientSide</c>; the object owns its own event subscription + tick listener for its full
/// lifetime and releases them in <see cref="Dispose"/>.</para>
///
/// <para><b>Completion</b> reuses the existing identity-addressed op: a row's checkbox sends
/// <see cref="ScribeCompleteTaskMessage"/> carrying the player's current completion policy (Sink/
/// Unpin/Delete); the server applies it store-first and re-pushes the set, which lands back here as a
/// rebuild. A just-completed row stays in place for a brief undo window (<see cref="UndoWindowMs"/>)
/// in which re-toggling reverts it, then sinks to the bottom (design D-Order).</para>
/// </summary>
public sealed class HudScribePins : GuiBase
{
    /// <summary>Logical-pixel margin between the HUD content and the anchored screen edge(s).</summary>
    private const float CornerMargin = 8f;

    /// <summary>Default leftward nudge applied to the <see cref="ScribeHudAnchor.TopRight"/> anchor
    /// (only when the player hasn't set their own <see cref="ScribePlayerSettings.HudOffsetX"/>) so the
    /// HUD sits left of, not under, the default top-right minimap. The vanilla minimap is a 250×250
    /// square anchored top-right with a 10px screen pad (decompiled <c>GuiDialogWorldMap</c>), so ~260px
    /// clears it. A player who has hidden/moved their minimap can zero this out via the config.</summary>
    private const float DefaultTopRightMinimapClearanceX = 260f;

    /// <summary>How long a just-completed row stays in its original (un-sunk) slot before sinking, an
    /// undo window in which re-toggling the checkbox reverts the completion (design D-Order). Client
    /// UI state only — never Core or server state.</summary>
    private const double UndoWindowMs = 2000;

    /// <summary>Low-frequency safety re-read / undo-timer cadence. The authoritative refresh is the
    /// event-driven rebuild on <see cref="ScribeModSystem.MyPinsChanged"/> (design D2); this tick only
    /// expires the undo window and re-reads defensively, so it can be cheap.</summary>
    private const int TickIntervalMs = 250;

    /// <summary>Hard cap on rendered rows regardless of the (clamped) preference, so a bad config value
    /// can never ask the HUD to draw an unbounded list. The preference is already clamped on load; this
    /// is belt-and-suspenders at the render site (design "the HUD additionally hard-caps rows").</summary>
    private const int MaxRenderedRows = ScribePlayerSettings.MaxHudMaxRows;

    private readonly ScribeModSystem modSystem;

    /// <summary>Client-local monotonic clock, accumulated from the tick delta (Core/base elapsed clocks
    /// aren't exposed), used to stamp and expire the per-pin undo windows in <see cref="sinkExpiryMs"/>.</summary>
    private double elapsedMs;

    private long tickListenerId;

    /// <summary>Optimistic done-state per pin, applied the instant a checkbox is clicked so the check
    /// mark flips without waiting for the server round-trip. Cleared for a pin once the server push
    /// agrees (see <see cref="OnMyPinsChanged"/>), after which the snapshot alone drives the row.</summary>
    private readonly Dictionary<(Guid, Guid), bool> optimisticDone = new();

    /// <summary>For each pin currently inside its undo window, the <see cref="elapsedMs"/> value at
    /// which it should sink. While present-and-unexpired, the pin is ordered as if not-done (kept in
    /// place); on expiry the tick rebuilds and it sinks. Cleared early if the player undoes.</summary>
    private readonly Dictionary<(Guid, Guid), double> sinkExpiryMs = new();

    public HudScribePins(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;

        // Own the subscription + tick for this object's whole lifetime (not per-open): the HUD must
        // react to a pin arriving even while it is closed so it can open itself. Released in Dispose().
        modSystem.MyPinsChanged += OnMyPinsChanged;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
    }

    // ---------------- HUD dialog semantics ----------------

    /// <inheritdoc />
    public override EnumDialogType DialogType => EnumDialogType.HUD;

    /// <summary>A HUD dialog is not a modal; Escape must fall through to the game (pause menu), never
    /// close the HUD. (The base already returns false for HUD type; stated explicitly for clarity.)</summary>
    public override bool OnEscapePressed() => false;

    /// <summary>Render only while open (the base default, restated for clarity).</summary>
    public override bool ShouldReceiveRenderEvents() => IsOpened();

    /// <summary>Never intercept keyboard events, so movement/hotbar/other keybinds keep working while
    /// the HUD is on screen. Mouse events are left ON (base default) so the row checkboxes are
    /// clickable; the non-opaque, content-sized window means clicks anywhere else fall through to the
    /// world (design D5 / task 5.2).</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;

    /// <inheritdoc />
    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Shrink-wrap to content (Size null) so the window is exactly as tall/wide as the rows; the
        // corner anchor is applied post-layout (see AnchorTopRight). A concrete Position suppresses the
        // base's first-frame auto-centering. Non-draggable/-resizable, and OpaqueHitTest off so a click
        // outside the (small) content rect passes through to the world instead of being swallowed.
        Size = null,
        Position = Vector2.Zero,
        Draggable = false,
        Resizable = false,
        OpaqueHitTest = false,
        Clip = false,
    };

    /// <summary>Shrink-wrap layout just settled on a (possibly new) content size — re-anchor immediately
    /// so a row-count change doesn't leave the window mis-positioned for a frame. Mirrors the
    /// <c>GuiGlobalOverlay</c> re-anchor, adapted for a configurable corner/edge rather than fullscreen.</summary>
    protected override void OnShrinkWrapLayoutCompleted() => ApplyAnchor();

    /// <inheritdoc />
    public override void OnRenderGUI(float deltaTime)
    {
        // Keep positioned every frame (handles a game-window resize, which needn't re-run layout). Uses
        // the last laid-out WindowSize; on the very first frame that is the shrink-wrap estimate,
        // self-correcting once real content lays out.
        ApplyAnchor();
        base.OnRenderGUI(deltaTime);
    }

    /// <summary>
    /// Position the shrink-wrapped window per the player's <see cref="ScribePlayerSettings.HudAnchor"/>
    /// (one of seven corners/edges) plus their <see cref="ScribePlayerSettings.HudOffsetX"/>/
    /// <see cref="ScribePlayerSettings.HudOffsetY"/> nudge (task 4.5). The offset always moves the window
    /// toward screen-center along each axis from the anchored edge — +X pulls a right anchor leftward and
    /// a left anchor rightward; +Y pulls a bottom anchor upward and a top anchor downward — so a positive
    /// value reads as "further from the edge" regardless of which edge is anchored, and a middle anchor's
    /// offset shifts from center. The <see cref="ScribeHudAnchor.TopRight"/> default, when the player
    /// hasn't set their own X offset, applies <see cref="DefaultTopRightMinimapClearanceX"/> so the HUD
    /// clears the minimap out of the box.
    /// </summary>
    private void ApplyAnchor()
    {
        float scale = RuntimeEnv.GUIScale;
        float screenW = capi.Render.FrameWidth / scale;
        float screenH = capi.Render.FrameHeight / scale;

        var settings = modSystem.MySettings;
        var anchor = ScribePlayerSettings.NormalizeAnchor(settings.HudAnchor);

        // Default the top-right X offset to the minimap clearance ONLY when the player left it at 0
        // (never touched it); any explicit value — including a deliberate 0 to sit flush — is honored.
        float offX = settings.HudOffsetX;
        if (anchor == ScribeHudAnchor.TopRight && settings.HudOffsetX == 0)
            offX = DefaultTopRightMinimapClearanceX;
        float offY = settings.HudOffsetY;

        // Left/center/right along X; top/center/bottom along Y. Left & top edges add the offset (moving
        // inward = toward center); right & bottom edges subtract it (also toward center); center anchors
        // shift by the raw offset from the midpoint.
        float x = anchor switch
        {
            ScribeHudAnchor.TopLeft or ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.BottomLeft
                => CornerMargin + offX,
            ScribeHudAnchor.TopMiddle
                => (screenW - WindowSize.X) / 2f + offX,
            _ // TopRight, MiddleRight, BottomRight
                => screenW - WindowSize.X - CornerMargin - offX,
        };

        float y = anchor switch
        {
            ScribeHudAnchor.TopLeft or ScribeHudAnchor.TopMiddle or ScribeHudAnchor.TopRight
                => CornerMargin + offY,
            ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.MiddleRight
                => (screenH - WindowSize.Y) / 2f + offY,
            _ // BottomLeft, BottomRight
                => screenH - WindowSize.Y - CornerMargin - offY,
        };

        // Never let the window escape the screen (a large offset or a huge row count could push it off).
        WindowPos = new Vector2(
            Math.Clamp(x, 0f, Math.Max(0f, screenW - WindowSize.X)),
            Math.Clamp(y, 0f, Math.Max(0f, screenH - WindowSize.Y)));
    }

    // ---------------- Refresh / visibility ----------------

    /// <summary>
    /// A fresh pin-set push arrived (added/removed/completed pin, or a snapshot refresh). Drop any
    /// optimistic done-overrides the server has now confirmed, then apply the visibility rule (design
    /// D6): open when the player has ≥1 pin, close at zero, otherwise rebuild in place.
    /// </summary>
    private void OnMyPinsChanged()
    {
        ReconcileOptimisticWithServer();

        bool hasPins = modSystem.MyPins.Count > 0;
        if (hasPins && !IsOpened())
        {
            TryOpen(); // withFocus defaults false via base; a HUD never steals focus anyway
        }
        else if (!hasPins && IsOpened())
        {
            TryClose();
        }
        else if (IsOpened())
        {
            ForceRebuild();
        }
    }

    /// <summary>Clear each optimistic override that the latest snapshot already agrees with, so the row
    /// falls back to the authoritative snapshot; keep the rest (still-in-flight clicks).</summary>
    private void ReconcileOptimisticWithServer()
    {
        if (optimisticDone.Count == 0) return;
        var snapshot = modSystem.MyPins.ToDictionary(p => (p.OwnerDocId, p.TaskId), p => p.LastKnownDone);
        foreach (var key in optimisticDone.Keys.ToList())
        {
            // Drop the override if the server matches it, or if the pin no longer exists at all.
            if (!snapshot.TryGetValue(key, out bool serverDone) || serverDone == optimisticDone[key])
            {
                optimisticDone.Remove(key);
            }
        }
    }

    /// <summary>Low-frequency tick: expire any elapsed undo windows (so completed rows sink) and act as
    /// a cheap safety re-read. Only rebuilds when something actually changed and the HUD is open.</summary>
    private void OnTick(float dt)
    {
        elapsedMs += dt * 1000.0;

        bool anyExpired = false;
        foreach (var key in sinkExpiryMs.Keys.ToList())
        {
            if (elapsedMs >= sinkExpiryMs[key])
            {
                sinkExpiryMs.Remove(key);
                anyExpired = true;
            }
        }

        if (anyExpired && IsOpened()) ForceRebuild();
    }

    // ---------------- Row state helpers ----------------

    /// <summary>The done-state to DISPLAY for a pin: the optimistic override if a click is in flight,
    /// else the authoritative snapshot.</summary>
    private bool DisplayedDone(ScribePinnedRef pin)
        => optimisticDone.TryGetValue((pin.OwnerDocId, pin.TaskId), out bool d) ? d : pin.LastKnownDone;

    /// <summary>Whether a pin is inside its (unexpired) undo window — kept in place rather than sunk.</summary>
    private bool InUndoWindow(ScribePinnedRef pin)
        => sinkExpiryMs.TryGetValue((pin.OwnerDocId, pin.TaskId), out double expiry) && elapsedMs < expiry;

    /// <summary>Whether a pin sinks to the bottom right now: done AND past its undo window. This is the
    /// HUD's undo-aware overlay on the Core <see cref="ScribePinOrdering"/> sink rule.</summary>
    private bool SunkForOrder(ScribePinnedRef pin) => DisplayedDone(pin) && !InUndoWindow(pin);

    /// <summary>
    /// A row checkbox was clicked. Flip optimistically, drive the undo window, and send the completion
    /// request carrying the player's current policy (the server applies Sink/Unpin/Delete and re-pushes).
    /// </summary>
    private void OnToggleRow(Guid docId, Guid taskId, bool currentlyDone)
    {
        var key = (docId, taskId);
        bool nowDone = !currentlyDone;

        optimisticDone[key] = nowDone;
        if (nowDone)
        {
            // Completing: hold it in place for the undo window, then let the tick sink it.
            sinkExpiryMs[key] = elapsedMs + UndoWindowMs;
        }
        else
        {
            // Un-completing (undo within the window, or unchecking a sunk row): cancel any sink.
            sinkExpiryMs.Remove(key);
        }

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)modSystem.MySettings.CompletionPolicy,
        });

        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Collapse control (on-HUD chevron; the rebindable hotkey calls the same path): flip the
    /// client-local <see cref="ScribePlayerSettings.HudCollapsed"/> preference and persist it.
    /// <see cref="ScribeModSystem.UpdateMySettings"/> fires <see cref="ScribeModSystem.MyPinsChanged"/>,
    /// so the HUD rebuilds into the collapsed/expanded form with no network round-trip (design D6).</summary>
    public void ToggleCollapsed()
        => modSystem.UpdateMySettings(s => s.HudCollapsed = !s.HudCollapsed);

    // ---------------- Build ----------------

    /// <inheritdoc />
    protected override Widget Build()
    {
        var pins = modSystem.MyPins;

        // Core ordering (not-done above done, stable) with the HUD's undo-window overlay: an in-window
        // completed pin is ordered as if not-done so it stays put until it sinks.
        var ordered = pins.Where(p => !SunkForOrder(p))
            .Concat(pins.Where(SunkForOrder))
            .ToList();

        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        var shown = ordered.Take(max)
            .Select(p => new HudPinRow(
                p.OwnerDocId, p.TaskId, p.LastKnownText, DisplayedDone(p), SunkForOrder(p)))
            .ToList();

        // Indicative "+N more" only (design "+N more affordance"): pins beyond the visible cap.
        int moreCount = Math.Max(0, pins.Count - max);

        return new HudPinsContent(
            rows: shown,
            moreCount: moreCount,
            collapsed: modSystem.MySettings.HudCollapsed,
            rowWidth: ScribePlayerSettings.ClampHudRowWidth(modSystem.MySettings.HudRowWidth),
            onToggleRow: OnToggleRow,
            onToggleCollapsed: ToggleCollapsed);
    }

    // ---------------- Lifecycle ----------------

    /// <inheritdoc />
    public override void Dispose()
    {
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        base.Dispose();
    }
}

// ============================================================================
// HUD content tree
// ============================================================================

/// <summary>A value snapshot of one HUD row: identity, last-known text, its displayed done-state, and
/// whether it is currently sunk (muted, ordered at the bottom). Carries no live pin reference.</summary>
internal readonly record struct HudPinRow(Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk);

/// <summary>
/// The HUD's widget tree: a collapse-header chevron over a column of pin rows (or, when collapsed,
/// the header alone). Split from <see cref="HudScribePins"/> so it can read the LibGUI
/// <see cref="Theme"/> off the <see cref="BuildContext"/> for its glow/mute colors — the same
/// delegation the lectern dialog uses for its content widgets. All interaction is routed back up
/// through the two callbacks; this widget holds no mutable state.
/// </summary>
internal sealed class HudPinsContent : StatelessWidget
{
    /// <summary>Row text glow radius (fraction of font size) — a soft dark halo that keeps the light
    /// text legible over any world background without a background plate (design D1).</summary>
    private const float GlowWidth = 0.6f;

    /// <summary>Font size for a HUD row's task text.</summary>
    private const float RowFontSize = 16f;

    /// <summary>Opacity of a sunk (completed, past-undo) row — muted but still readable/undoable.</summary>
    private const float SunkOpacity = 0.5f;

    private readonly IReadOnlyList<HudPinRow> rows;
    private readonly int moreCount;
    private readonly bool collapsed;
    private readonly float rowWidth;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    private readonly Action onToggleCollapsed;

    public HudPinsContent(
        IReadOnlyList<HudPinRow> rows,
        int moreCount,
        bool collapsed,
        float rowWidth,
        Action<Guid, Guid, bool> onToggleRow,
        Action onToggleCollapsed)
    {
        this.rows = rows;
        this.moreCount = moreCount;
        this.collapsed = collapsed;
        this.rowWidth = rowWidth;
        this.onToggleRow = onToggleRow;
        this.onToggleCollapsed = onToggleCollapsed;
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        Vector4 glow = new(0f, 0f, 0f, 0.9f); // dark halo

        var children = new List<Widget> { BuildHeader(colors, glow) };

        if (!collapsed)
        {
            foreach (var row in rows)
            {
                children.Add(BuildRow(row, colors, glow));
            }
            if (moreCount > 0)
            {
                children.Add(new Text(
                    Lang.Get("scribe:scribe-hud-more", moreCount),
                    new TextStyle
                    {
                        FontSize = 13,
                        Color = colors.OnSurfaceVariant,
                        GlowWidth = GlowWidth,
                        GlowColor = glow,
                    }));
            }
        }

        // Constrain the whole HUD to the configured fixed width (task 4.5) via a SizedBox, so a long
        // task wraps within that width instead of the shrink-wrap window growing arbitrarily wide. The
        // Column right-aligns its rows within that fixed width (CrossAxisAlignment.End), keeping the
        // content hugging the anchored (default top-right) edge as rows grow/shrink.
        return new SizedBox(
            width: rowWidth,
            child: new Padding(
                EdgeInsets.All(2),
                child: new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: CrossAxisAlignment.End,
                    children: children)));
    }

    /// <summary>The collapse-toggle header: a clickable chevron (▾ expanded, ▸ collapsed) plus a small
    /// title. Present in both states so a collapsed HUD stays re-expandable (design D6).</summary>
    private Widget BuildHeader(ColorScheme colors, Vector4 glow)
    {
        string chevron = collapsed ? "▸" : "▾"; // ▸ / ▾
        var titleStyle = new TextStyle
        {
            FontSize = 14,
            Color = colors.OnSurfaceVariant,
            GlowWidth = GlowWidth,
            GlowColor = glow,
        };

        return new GestureDetector(
            onTap: _ => onToggleCollapsed(),
            child: new Row(
                spacing: 4,
                mainAxisSize: MainAxisSize.Min,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    new Text(chevron, titleStyle),
                    new Text(Lang.Get("scribe:scribe-hud-title"), titleStyle),
                }));
    }

    /// <summary>One HUD row: [checkbox][text], the lectern read row minus chrome (no grip spacer, no
    /// pinned tint, no per-row buttons — design D1). A sunk row mutes via a fading
    /// <see cref="AnimatedOpacity"/> to <see cref="SunkOpacity"/>.</summary>
    private Widget BuildRow(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        var textStyle = new TextStyle
        {
            FontSize = RowFontSize,
            // A sunk (completed) row uses the muted variant; an active row uses the bright surface tone.
            Color = row.Sunk ? colors.OnSurfaceVariant : colors.OnSurface,
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        Widget rowBody = new Row(
            spacing: 6,
            // Fill the fixed-width column (task 4.5) so the Expanded text wraps within HudRowWidth minus
            // the checkbox, rather than the row sizing to its (unwrapped) content and overflowing.
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: new Widget[]
            {
                new Checkbox(
                    value: row.Done,
                    onChanged: _ => onToggleRow(row.DocId, row.TaskId, row.Done),
                    size: 20),
                // Expanded so the (SoftWrap) text wraps within the remaining fixed width. The ValueKey
                // stabilizes row identity across rebuilds for the opacity animation.
                new Expanded(child: new Text(row.Text, textStyle)),
            });

        // Fade the mute in/out rather than snapping, so completing a task reads as a gentle settle.
        return new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));
    }
}
