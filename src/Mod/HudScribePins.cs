using System;
using System.Collections.Generic;
using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Animations;    // AnimatedOpacity, Curves
using Gui.Widgets.Basic;         // Text, Container, VsIcon
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

    /// <summary>The shared "pin window" (design D7): how long a just-checked completion is held on the
    /// HUD before it is SENT to the server, during which unchecking the row is a true undo (nothing was
    /// sent). All completion policies share this one duration (the user's <c>pinHudWaitTime</c>). Client
    /// UI state only — never Core or server state.</summary>
    private const double PinHudWaitMs = 1500;

    /// <summary>Low-frequency safety re-read / window cadence. The authoritative refresh is the
    /// event-driven rebuild on <see cref="ScribeModSystem.MyPinsChanged"/> (design D2); this tick expires
    /// the pin windows (firing the deferred sends) and re-reads defensively, so it can be cheap.</summary>
    private const int TickIntervalMs = 250;

    /// <summary>Hard cap on rendered rows regardless of the (clamped) preference, so a bad config value
    /// can never ask the HUD to draw an unbounded list. The preference is already clamped on load; this
    /// is belt-and-suspenders at the render site (design "the HUD additionally hard-caps rows").</summary>
    private const int MaxRenderedRows = ScribePlayerSettings.MaxHudMaxRows;

    private readonly ScribeModSystem modSystem;

    /// <summary>Client-local monotonic clock, accumulated from the tick delta (Core/base elapsed clocks
    /// aren't exposed), used to stamp and expire the per-pin windows in <see cref="pendingCompletions"/>.</summary>
    private double elapsedMs;

    private long tickListenerId;

    /// <summary>Optimistic done-state per pin, applied the instant a checkbox is clicked so the check
    /// mark flips without waiting for the (deferred) server round-trip. Cleared for a pin once the server
    /// push agrees (see <see cref="OnMyPinsChanged"/>), after which the snapshot alone drives the row.</summary>
    private readonly Dictionary<(Guid, Guid), bool> optimisticDone = new();

    /// <summary>A completion the player checked off but that has NOT yet been sent to the server (design
    /// D7 deferred send): the policy to apply and the <see cref="elapsedMs"/> value at which the window
    /// expires and the send fires. While present-and-unexpired the pin is held in place (not sunk) and
    /// animates its pending outcome; unchecking within the window removes the entry (a true undo — nothing
    /// was sent); on expiry <see cref="OnTick"/> sends the completion and removes the entry.</summary>
    private readonly Dictionary<(Guid, Guid), PendingCompletion> pendingCompletions = new();

    /// <summary>A completion held on the HUD during its pin window (design D7): the player's chosen policy
    /// at click time and the clock value at which the deferred send fires.</summary>
    private readonly record struct PendingCompletion(ScribeCompletionPolicy Policy, double ExpiryMs);

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

        // Offsets are RELATIVE to the anchor's pre-baked offset (add-settings-tab D8): the player's stored
        // value is ADDED to the anchor's sensible built-in default, so a stored 0 sits at that default
        // (e.g. clear of the top-right minimap) and any value nudges further from it. This replaced the
        // old "apply the clearance only when offset==0" special-case, which made 0 ambiguous (default vs.
        // deliberately-flush). Only TopRight has a non-zero pre-bake (the minimap clearance on X).
        float prebakeX = anchor == ScribeHudAnchor.TopRight ? DefaultTopRightMinimapClearanceX : 0f;
        float offX = prebakeX + settings.HudOffsetX;
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

    /// <summary>Low-frequency tick: fire any pin windows that have elapsed — sending the deferred
    /// completion to the server (design D7) — and act as a cheap safety re-read. Only rebuilds when a
    /// window actually expired and the HUD is open (the subsequent server re-push lands in
    /// <see cref="OnMyPinsChanged"/> and rebuilds again with the authoritative result).</summary>
    private void OnTick(float dt)
    {
        elapsedMs += dt * 1000.0;

        bool anyExpired = false;
        foreach (var key in pendingCompletions.Keys.ToList())
        {
            var pending = pendingCompletions[key];
            if (elapsedMs >= pending.ExpiryMs)
            {
                pendingCompletions.Remove(key);
                SendCompletion(key.Item1, key.Item2, pending.Policy);
                anyExpired = true;
            }
        }

        if (anyExpired && IsOpened()) ForceRebuild();
    }

    // ---------------- Row state helpers ----------------

    /// <summary>The done-state to DISPLAY for a pin: the optimistic override if a completion is in flight
    /// (pending or just-sent), else the authoritative snapshot.</summary>
    private bool DisplayedDone(ScribePinnedRef pin)
        => optimisticDone.TryGetValue((pin.OwnerDocId, pin.TaskId), out bool d) ? d : pin.LastKnownDone;

    /// <summary>The pending (not-yet-sent) completion for a pin, or null if none is in its window.</summary>
    private PendingCompletion? PendingFor(ScribePinnedRef pin)
        => pendingCompletions.TryGetValue((pin.OwnerDocId, pin.TaskId), out var p) ? p : null;

    /// <summary>Whether a pin sinks to the bottom right now (HUD's undo-aware overlay on the Core
    /// <see cref="ScribePinOrdering"/> sink rule). A pin sinks only when it is done AND its policy is
    /// <see cref="ScribeCompletionPolicy.Sink"/> AND it is past its pin window. Under
    /// <see cref="ScribeCompletionPolicy.Keep"/> a done pin never sinks (it holds its place); Unpin/Delete
    /// pins are removed after the window so ordering is moot for them. During the window the pin is held
    /// in place (not sunk) so the sink animates as it settles on expiry.</summary>
    private bool SunkForOrder(ScribePinnedRef pin)
    {
        if (!DisplayedDone(pin)) return false;
        // Within its pin window a done pin is always held in place (not sunk), so the sink can animate as
        // it settles when the window expires — regardless of the pending policy.
        if (PendingFor(pin) is not null) return false;
        // Sent + confirmed done: it only remains visible under a non-removing policy (Sink/Keep — Unpin/
        // Delete removed it). Sink de-prioritizes it; Keep holds its place. We don't persist which policy
        // completed it, so the player's current policy is the proxy (matches the resting-order intent).
        return modSystem.MySettings.CompletionPolicy == ScribeCompletionPolicy.Sink;
    }

    /// <summary>Whether a pin is inside a pending window whose policy will REMOVE it (Unpin/Delete), so
    /// the row should fade its text out over the window as a preview of the destructive outcome (design
    /// D7). Sink/Keep don't fade (they persist); they get the mute/settle treatment instead.</summary>
    private bool IsFadingOut(ScribePinnedRef pin)
        => PendingFor(pin) is { } pending
           && pending.Policy is ScribeCompletionPolicy.Unpin or ScribeCompletionPolicy.Delete;

    /// <summary>
    /// A row checkbox was clicked. Deferred-send model (design D7): checking a row does NOT send the
    /// completion immediately — it flips optimistically and records a <see cref="PendingCompletion"/> with
    /// the player's current policy, held for <see cref="PinHudWaitMs"/>. <see cref="OnTick"/> fires the
    /// actual <see cref="ScribeCompleteTaskMessage"/> when the window expires. Unchecking within the
    /// window removes the pending entry and clears the optimistic flag — a TRUE undo, because nothing was
    /// sent to the server (important for the destructive Unpin/Delete policies).
    /// </summary>
    private void OnToggleRow(Guid docId, Guid taskId, bool currentlyDone)
    {
        var key = (docId, taskId);
        bool nowDone = !currentlyDone;

        if (nowDone)
        {
            // Complete: flip optimistically and hold the send until the window expires.
            optimisticDone[key] = true;
            pendingCompletions[key] = new PendingCompletion(
                modSystem.MySettings.CompletionPolicy, elapsedMs + PinHudWaitMs);
        }
        else
        {
            // Undo within the window (or unchecking an already-completed row). If a send was still
            // pending, cancel it outright — nothing reached the server, so this fully reverts.
            if (pendingCompletions.Remove(key))
            {
                optimisticDone.Remove(key);
            }
            else
            {
                // No pending send (the window already elapsed and the completion was sent): this is a
                // genuine un-complete. Flip optimistically and send it immediately (there's no window to
                // hold — the row is already done server-side).
                optimisticDone[key] = false;
                SendCompletion(docId, taskId, modSystem.MySettings.CompletionPolicy);
            }
        }

        if (IsOpened()) ForceRebuild();
    }

    /// <summary>Fire the deferred completion request for a pin, carrying the policy captured when the
    /// player checked it (design D7). The server applies Sink/Keep/Unpin/Delete and re-pushes.</summary>
    private void SendCompletion(Guid docId, Guid taskId, ScribeCompletionPolicy policy)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)policy,
        });
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
                p.OwnerDocId, p.TaskId, p.LastKnownText, DisplayedDone(p), SunkForOrder(p),
                FadingOut: IsFadingOut(p)))
            .ToList();

        // Indicative "+N more" only (design "+N more affordance"): pins beyond the visible cap.
        int moreCount = Math.Max(0, pins.Count - max);

        return new HudPinsContent(
            rows: shown,
            moreCount: moreCount,
            collapsed: modSystem.MySettings.HudCollapsed,
            rowWidth: ScribePlayerSettings.ClampHudRowWidth(modSystem.MySettings.HudRowWidth),
            rowFontSize: ScribeRowConstants.BaseHudFontSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            checkboxSize: ScribeRowConstants.BaseHudCheckboxSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            onToggleRow: OnToggleRow,
            onToggleCollapsed: ToggleCollapsed,
            onOpenSettings: OpenSettings);
    }

    /// <summary>The HUD gear (add-settings-tab 5.4): open the minimal standalone settings dialog hosting
    /// the shared <see cref="ScribeSettingsContent"/> form (design D2's HUD-gear target — the HUD is an
    /// always-on overlay with no central region to swap, so it opens a small window instead of an
    /// in-place swap). Reuses one instance so repeated gear taps toggle it rather than stacking.</summary>
    private ScribeSettingsDialog? settingsDialog;

    private void OpenSettings()
    {
        settingsDialog ??= new ScribeSettingsDialog(capi, modSystem);
        if (!settingsDialog.IsOpened()) settingsDialog.TryOpen();
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
        settingsDialog?.Dispose();
        settingsDialog = null;
        base.Dispose();
    }
}

// ============================================================================
// HUD content tree
// ============================================================================

/// <summary>A value snapshot of one HUD row: identity, last-known text, its displayed done-state,
/// whether it is currently sunk (muted, ordered at the bottom), and whether it is fading out inside a
/// pending destructive-completion window (Unpin/Delete about to send — design D7). Carries no live pin
/// reference.</summary>
internal readonly record struct HudPinRow(Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk, bool FadingOut);

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

    /// <summary>Opacity of a sunk (completed, past-undo) row — muted but still readable/undoable.</summary>
    private const float SunkOpacity = 0.5f;

    private readonly IReadOnlyList<HudPinRow> rows;
    private readonly int moreCount;
    private readonly bool collapsed;
    private readonly float rowWidth;
    private readonly float rowFontSize;
    private readonly float checkboxSize;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    private readonly Action onToggleCollapsed;
    private readonly Action onOpenSettings;

    public HudPinsContent(
        IReadOnlyList<HudPinRow> rows,
        int moreCount,
        bool collapsed,
        float rowWidth,
        float rowFontSize,
        float checkboxSize,
        Action<Guid, Guid, bool> onToggleRow,
        Action onToggleCollapsed,
        Action onOpenSettings)
    {
        this.rows = rows;
        this.moreCount = moreCount;
        this.collapsed = collapsed;
        this.rowWidth = rowWidth;
        this.rowFontSize = rowFontSize;
        this.checkboxSize = checkboxSize;
        this.onToggleRow = onToggleRow;
        this.onToggleCollapsed = onToggleCollapsed;
        this.onOpenSettings = onOpenSettings;
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
    /// title, and a gear that opens the settings surface (add-settings-tab 5.4). Present in both states
    /// so a collapsed HUD stays re-expandable AND its settings stay reachable (design D6). The gear sits
    /// in its own GestureDetector so tapping it opens settings without also toggling the collapse.</summary>
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

        // Chevron + title toggle collapse; the trailing gear opens settings. Separate GestureDetectors
        // so the two targets don't overlap (a gear tap must not also flip the collapse state).
        var collapseToggle = new GestureDetector(
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

        var gear = new GestureDetector(
            onTap: _ => onOpenSettings(),
            child: new Padding(
                EdgeInsets.Only(left: 6),
                child: new VsIcon("scribegear", 16f, colors.OnSurfaceVariant)));

        return new Row(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[] { collapseToggle, gear });
    }

    /// <summary>Target opacity a destructive-pending (Unpin/Delete) row fades toward over its window, as
    /// a preview of removal (design D7). Not fully 0 so the row (and its still-clickable checkbox for
    /// undo) stays visible until the send actually removes it.</summary>
    private const float FadingOutOpacity = 0.15f;

    /// <summary>One HUD row: [checkbox][text], the lectern read row minus chrome (no grip spacer, no
    /// pinned tint, no per-row buttons — design D1). Opacity animates by state: a destructive-pending row
    /// (Unpin/Delete in its window) fades toward <see cref="FadingOutOpacity"/> over the window; a sunk
    /// row mutes to <see cref="SunkOpacity"/>; else full. The checkbox stays fully opaque and clickable so
    /// undo is always available (design D7) — only the TEXT fades, via a nested AnimatedOpacity.</summary>
    private Widget BuildRow(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            // A sunk (completed) row uses the muted variant; an active row uses the bright surface tone.
            Color = row.Sunk ? colors.OnSurfaceVariant : colors.OnSurface,
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // The text fades toward FadingOutOpacity over the window for a destructive-pending row (preview of
        // removal). The animation duration matches the pin window so the fade tracks the countdown; a
        // non-fading row is fully opaque. Only the text fades — the checkbox stays clickable for undo.
        Widget text = new AnimatedOpacity(
            opacity: row.FadingOut ? FadingOutOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(row.FadingOut ? 1500 : 200),
            curve: Curves.EaseOut,
            child: new Text(row.Text, textStyle));

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
                    size: checkboxSize),
                // Expanded so the (SoftWrap) text wraps within the remaining fixed width.
                new Expanded(child: text),
            });

        // A sunk row mutes the WHOLE row (checkbox + text) to SunkOpacity, faded rather than snapped so a
        // completing task reads as a gentle settle. The ValueKey stabilizes row identity across rebuilds
        // for the animation.
        return new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));
    }
}
