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
using Vintagestory.GameContent;  // SystemTemporalStability, EnumTempStormStrength

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

    /// <summary>Pins that completed-and-settled under the Sink policy this session, by identity
    /// (scribe-settings-followups 2.1). Once a Sink window expires, the pin lands here and STAYS ordered at
    /// the bottom for the rest of the session — even if the player later unchecks it — so a completed task
    /// keeps its resting place instead of jumping back to its old slot on an uncheck. Client-local,
    /// session-only (not persisted, not synced — design D3): a relog re-derives order from done-state.</summary>
    private readonly HashSet<(Guid, Guid)> sunkOrder = new();

    /// <summary>Pins whose destructive completion (Unpin/Delete) has been SENT and that are now waiting for
    /// the server's removal push (scribe-settings-followups 1.1 flicker fix). Between the window expiring
    /// (send) and the server re-pushing the pin set, the pin is still in <see cref="ScribeModSystem.MyPins"/>
    /// but is no longer <see cref="IsFadingOut"/>, so without this it would rebuild at FULL opacity for a
    /// frame — a visible flash. Rows in this set are filtered OUT of the rendered live list; the collapsing
    /// snapshot in <see cref="departing"/> is what's shown instead. Cleared when the server push confirms the
    /// removal — but the live pin stays suppressed while it is ALSO in <see cref="departing"/> (i.e. still
    /// collapsing), so there is no post-collapse flash regardless of whether the collapse or the server push
    /// finishes first.</summary>
    private readonly HashSet<(Guid, Guid)> awaitingRemoval = new();

    /// <summary>Rows collapsing their height to zero before leaving the HUD (scribe-list-collapse). Keyed by
    /// identity, valued by a last-known <see cref="HudPinRow"/> snapshot (with the display index it held) so
    /// the row keeps rendering — and collapsing IN PLACE — even after the server's removal push drops the pin
    /// from <see cref="ScribeModSystem.MyPins"/>. A departing row renders its text at zero opacity (it already
    /// faded during the window) with its checkbox intact, so the collapse just closes the empty row. The
    /// entry is removed ONLY when its collapse completes (<see cref="OnDepartingCollapsed"/>), never on a
    /// server push — so a server round-trip faster OR slower than the collapse never truncates the animation.</summary>
    private readonly Dictionary<(Guid, Guid), DepartingRow> departing = new();

    /// <summary>A HUD row that is collapsing out of the list: its last-known <see cref="HudPinRow"/> snapshot
    /// and the display index it held when it began departing (so it collapses in place rather than jumping).</summary>
    private readonly record struct DepartingRow(HudPinRow Row, int Index);

    /// <summary>Host-owned collapse controllers for <see cref="departing"/> rows, keyed by identity so a
    /// collapse RESUMES (not restarts) across the HUD's <see cref="ForceRebuild"/> remounts
    /// (scribe-list-collapse). Disposed with the HUD.</summary>
    private readonly ScribeCollapseRegistry collapseRegistry = new();

    /// <summary>Keeps the collapse-time hover refresh running a few frames past the last animating frame so a
    /// refresh lands AFTER the completion-triggered <see cref="ForceRebuild"/> re-lays-out the fresh tree
    /// (fix-list-collapse-stale-hover). See <see cref="ScribeHoverRefreshLatch"/>.</summary>
    private readonly ScribeHoverRefreshLatch hoverRefreshLatch = new();

    /// <summary>Set when a row's collapse completes, so the row's removal + rebuild is deferred to the next
    /// <see cref="OnRenderGUI"/> — the completion callback fires from inside the ticker pump, so we must not
    /// unmount + rebuild the tree re-entrantly there.</summary>
    private bool needsCollapseCleanup;

    private record struct AnchorInputs(float ScreenW, float ScreenH, ScribeHudAnchor Anchor, float OffX, float OffY, bool MinimapOn);
    private AnchorInputs? _lastAnchorInputs;

    /// <summary>Client-side interpolated remaining seconds for the timer, kept smooth between 1-second
    /// server pushes. Resynced from <see cref="ScribeModSystem.MyTimer"/> on every push.</summary>
    private double _timerLocalRemaining;
    private bool _timerResync = true;

    /// <summary>Last timer status the HUD reacted to. The server pushes timer state every second while a
    /// timer runs (each fires <see cref="OnMyTimerChanged"/>); the steady per-second countdown repaint is
    /// the shared <see cref="ScribeModSystem.TimerDisplayTick"/>'s job, so OnMyTimerChanged only rebuilds
    /// on a genuine Idle↔Running↔Fired transition — otherwise the HUD rebuilt twice a second (push +
    /// display tick).</summary>
    private Scribe.Core.TimerStatus _lastTimerStatus = Scribe.Core.TimerStatus.Idle;

    /// <summary>Client-side accumulator for how long the current timer has been Fired, in real seconds. The
    /// server no longer times the 30 s auto-disappear (it's governed by the client-local
    /// <see cref="ScribePlayerSettings.TimerAutoDisappear"/> preference, which only the client knows), so
    /// this drives it. Seeded from the pushed <see cref="TimerStore.FiredElapsedSeconds"/> whenever a Fired
    /// state arrives — 0 at the firing transition, or the persisted value when a fired timer is restored on
    /// rejoin, so the window resumes rather than restarts — then advanced locally on the 1 Hz
    /// <see cref="ScribeModSystem.TimerDisplayTick"/> (timer-auto-disappear-setting).</summary>
    private double _firedElapsedLocal;

    /// <summary>Guards against sending more than one auto-clear for the same fired timer (the clear request
    /// and the server's Idle re-push are a round-trip apart, during which another 1 Hz tick could fire).
    /// Reset each time a fresh Fired state arrives.</summary>
    private bool _firedAutoClearSent;

    // ---------------- Temporal-storm corruption (hud-temporal-storm-corruption) ----------------

    /// <summary>Base corruption strengths per storm tier, sourced verbatim from vanilla's own
    /// <c>stormGlitchStrength</c> bases in <see cref="SystemTemporalStability"/> (decompiled) so the HUD
    /// corrupts at the same intensity the game's own storm chat would. Not scaled.</summary>
    private const double StormLightStrength = 0.53;
    private const double StormMediumStrength = 0.67;
    private const double StormHeavyStrength = 0.90;

    /// <summary>Personal-stability corruption ramp bounds: at/above <see cref="StabilityRampUpper"/> the
    /// low-stability trigger contributes nothing; it ramps linearly to full strength at (or below)
    /// <see cref="StabilityRampLower"/>.</summary>
    private const double StabilityRampUpper = 0.50;
    private const double StabilityRampLower = 0.10;

    /// <summary>Re-scramble cadence bounds (ms): while a trigger is active the corruption is recomputed on
    /// a randomized interval in this range, so the marks writhe rather than sitting static.</summary>
    private const long RescrambleMinMs = 0;
    private const long RescrambleMaxMs = 5000;

    /// <summary>Seed handed to the Core corruptor each rebuild; advanced on the re-scramble cadence so the
    /// injected marks change while the same text stays put. Deterministic per build so the whole HUD's
    /// corruption is coherent within one frame.</summary>
    private int _corruptionSeed;

    /// <summary><see cref="ICoreClientAPI.World"/>'s <c>ElapsedMilliseconds</c> at which the next
    /// re-scramble is due; scheduled to a fresh random interval each time it fires (or when a trigger
    /// first becomes active).</summary>
    private long _nextRescrambleMs;

    /// <summary>Edge-detect state for the corruption trigger and the storm-active flag, so a transition
    /// rebuilds the HUD immediately (prompt title swap + first corruption) rather than waiting for the
    /// next re-scramble tick. Mirrors the server-side <c>_stormWasActive</c> pattern.</summary>
    private bool _corruptionWasActive;
    private bool _stormWasActiveHud;


    public HudScribePins(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;

        // Own the subscription + tick for this object's whole lifetime (not per-open): the HUD must
        // react to a pin arriving even while it is closed so it can open itself. Released in Dispose().
        modSystem.MyPinsChanged += OnMyPinsChanged;
        modSystem.MyTimerChanged += OnMyTimerChanged;
        modSystem.TimerDisplayTick += OnTimerDisplayTick;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
    }

    /// <summary>Repaint the HUD timer countdown on the shared 1Hz tick so it stays in step with the
    /// Notebook Timer tab (both fire from the same dispatch). Only rebuilds while a running/fired timer
    /// is on screen and the HUD is open; the 250ms OnTick keeps _timerLocalRemaining interpolated.</summary>
    private void OnTimerDisplayTick()
    {
        var timer = modSystem.MyTimer;

        // Client-driven auto-disappear (timer-auto-disappear-setting): while a timer is Fired, advance the
        // local elapsed and — ONLY if the player's "Timer disappears" preference is on — clear it once the
        // ~30 s window elapses. Re-reading the preference every tick is what makes turning it off mid-flash
        // take effect live: the check simply stops passing, so no clear is ever sent and the fired row
        // stays until the player dismisses it. The server no longer times this.
        if (timer?.Status == Scribe.Core.TimerStatus.Fired)
        {
            _firedElapsedLocal += 1.0;
            if (!_firedAutoClearSent
                && modSystem.MySettings.TimerAutoDisappear
                && _firedElapsedLocal >= Scribe.Core.TimerStore.FiredAutoClearSeconds)
            {
                _firedAutoClearSent = true;
                SendClearTimer();
            }
        }

        if (timer?.Status == Scribe.Core.TimerStatus.Running && IsOpened())
            ForceRebuild();
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
        // A row's collapse completed (its callback fired from inside the animation pump, where unmounting the
        // tree would be re-entrant); retire it now with a rebuild (scribe-list-collapse).
        if (needsCollapseCleanup)
        {
            needsCollapseCleanup = false;
            if (IsOpened()) ForceRebuild();
        }

        // Keep hover self-healing under a STATIONARY cursor (LibGUI only recomputes hover on real mouse
        // motion). Two triggers: a collapse reflowing the list every frame, and ANY ForceRebuild mounting a
        // fresh tree where every element is hovered=false. The latter is why an UNPIN (which just rebuilds
        // with no collapse animation, so AnyAnimating never fires) previously dropped the hover controls on
        // the row that slid under the cursor — ArmIfRebuilt catches every rebuild path by RootElement
        // identity. The latch re-dispatches a synthetic pointer-move for a few frames past either trigger so
        // the rebuilt tree (laid out a frame later) regains hover without a mouse wiggle. No-op when idle.
        if (collapseRegistry.AnyAnimating) hoverRefreshLatch.Arm();
        hoverRefreshLatch.ArmIfRebuilt(RootElement);
        if (hoverRefreshLatch.Tick()) RefreshHoverAtCursor();

        // Keep positioned every frame (handles a game-window resize, which needn't re-run layout). Uses
        // the last laid-out WindowSize; on the very first frame that is the shrink-wrap estimate,
        // self-correcting once real content lays out.
        ApplyAnchor();
        base.OnRenderGUI(deltaTime);
    }

    /// <summary>Re-dispatch a synthetic pointer-move at the current cursor so LibGUI re-runs its hit-test
    /// and refreshes hover (fix-list-collapse-stale-hover) — called each frame while an unpin collapse is
    /// animating, since LibGUI otherwise only updates hover on real mouse motion. Mirrors the lectern
    /// dialog's <c>RefreshHoverAtCursor</c>; shares the raw→window-local conversion in
    /// <see cref="ScribeHoverRefresh"/>.</summary>
    private void RefreshHoverAtCursor()
    {
        if (RootElement?.RenderObject == null) return;
        var local = ScribeHoverRefresh.ToWindowLocal(
            capi.Input.MouseX, capi.Input.MouseY, GetUiScale(), WindowPos);
        EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y));
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

        bool minimapOn = !capi.Settings.Bool.Exists("showMinimapHud") || capi.Settings.Bool["showMinimapHud"];
        float prebakeX = anchor == ScribeHudAnchor.TopRight && minimapOn ? DefaultTopRightMinimapClearanceX : 0f;
        var key = new AnchorInputs(screenW, screenH, anchor, prebakeX + settings.HudOffsetX, settings.HudOffsetY, minimapOn);
        if (_lastAnchorInputs == key) return;
        _lastAnchorInputs = key;

        // Offsets are RELATIVE to the anchor's pre-baked offset (add-settings-tab D8): the player's stored
        // value is ADDED to the anchor's sensible built-in default, so a stored 0 sits at that default
        // (e.g. clear of the top-right minimap) and any value nudges further from it. This replaced the
        // old "apply the clearance only when offset==0" special-case, which made 0 ambiguous (default vs.
        // deliberately-flush). Only TopRight has a non-zero pre-bake (the minimap clearance on X).
        //
        // Minimap-aware pre-bake (v1-playtest-fixes 9.3): apply the clearance only when the minimap is on.
        // "showMinimapHud" is written by GuiDialogWorldMap — absent means minimap was never explicitly
        // toggled (i.e. on by default). Treat absent = on so a fresh install still clears the minimap.
        float offX = key.OffX;
        float offY = key.OffY;

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
        _lastAnchorInputs = null;
        ReconcileOptimisticWithServer();
        // Reconcile the collapsing/removing rows against the authoritative pin set: note removals the server
        // has now confirmed, and cancel the departure of any task the player re-pinned so it comes back at
        // full height instead of being stuck suppressed (scribe-list-collapse).
        ReconcileDeparting();

        var rawTimer = modSystem.MyTimer;
        bool hasTimer = rawTimer is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired };
        bool hasPins = modSystem.MyPins.Count > 0;
        if ((hasPins || hasTimer) && !IsOpened())
        {
            TryOpen(); // withFocus defaults false via base; a HUD never steals focus anyway
        }
        else if (!hasPins && !hasTimer && IsOpened())
        {
            TryClose();
        }
        else if (IsOpened())
        {
            ForceRebuild();
        }
    }

    private void OnMyTimerChanged()
    {
        _timerResync = true;
        var timer = modSystem.MyTimer;
        var status = timer?.Status ?? Scribe.Core.TimerStatus.Idle;
        bool statusChanged = status != _lastTimerStatus;
        _lastTimerStatus = status;

        // A fresh Fired state (the firing transition, or a fired timer restored on rejoin) reseeds the
        // client-side auto-disappear accumulator from the pushed elapsed and re-arms the one-shot clear, so
        // the ~30 s window resumes from where the server left it rather than restarting. Any non-Fired state
        // (Running/Idle/cleared) resets it so a later fire starts a clean window.
        if (status == Scribe.Core.TimerStatus.Fired)
        {
            _firedElapsedLocal = timer?.FiredElapsedSeconds ?? 0.0;
            _firedAutoClearSent = false;
        }
        else
        {
            _firedElapsedLocal = 0.0;
            _firedAutoClearSent = false;
        }

        bool hasTimer = status is Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired;
        if (hasTimer && !IsOpened() && modSystem.MyPins.Count == 0)
            TryOpen();
        else if (!hasTimer && !modSystem.MyPins.Any() && IsOpened())
            TryClose();
        // Only rebuild here on a genuine status transition (start/fire/clear). The routine per-second
        // running push does NOT rebuild — TimerDisplayTick owns the steady countdown repaint, so the HUD
        // rebuilds once per second, not twice.
        else if (statusChanged && IsOpened())
            ForceRebuild();
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

    /// <summary>Start collapsing a destructive-completed row out of the HUD (scribe-list-collapse). Snapshots
    /// the row (with its current display index, so it collapses IN PLACE) into <see cref="departing"/> and
    /// suppresses the live pin via <see cref="awaitingRemoval"/> so only the collapsing snapshot renders.
    /// Its collapse controller is lazily created (and started) by the <see cref="ScribeCollapsible"/> on its
    /// first build.</summary>
    private void BeginDeparting((Guid, Guid) key)
    {
        // Find the row's snapshot + display index from the current ordering, so the collapsing ghost sits
        // exactly where the live row was rather than jumping to the end of the list.
        var shown = BuildOrderedRows();
        int index = shown.FindIndex(r => (r.DocId, r.TaskId) == key);
        if (index < 0) return; // not currently visible (e.g. past the row cap) — nothing to animate

        departing[key] = new DepartingRow(shown[index], index);
        awaitingRemoval.Add(key);
    }

    /// <summary>Reconcile the collapsing/removing rows with a fresh authoritative pin push. A task the player
    /// re-pinned (back in <see cref="ScribeModSystem.MyPins"/>) has its departure cancelled so it reappears at
    /// full height; a task the server confirms as gone stays suppressed only while it is still collapsing (so
    /// there's no post-collapse flash) and is fully cleaned up once its collapse has finished.</summary>
    private void ReconcileDeparting()
    {
        if (awaitingRemoval.Count == 0 && departing.Count == 0) return;

        var live = modSystem.MyPins.Select(p => (p.OwnerDocId, p.TaskId)).ToHashSet();

        // A re-pinned (live again) task must not keep collapsing/suppressed: cancel its departure entirely.
        foreach (var key in departing.Keys.ToList())
        {
            if (live.Contains(key)) CancelDeparting(key);
        }

        // Stop suppressing any live pin that is no longer collapsing (defensive: a pin that somehow survived
        // its destructive send and isn't departing should show again).
        awaitingRemoval.RemoveWhere(k => live.Contains(k) && !departing.ContainsKey(k));
    }

    /// <summary>Cancel a row's departure (re-pinned before/while collapsing): drop its collapse controller,
    /// its snapshot, and its live-pin suppression so it renders normally again.</summary>
    private void CancelDeparting((Guid, Guid) key)
    {
        departing.Remove(key);
        awaitingRemoval.Remove(key);
        collapseRegistry.Release(DepartKey(key));
    }

    /// <summary>A departing row finished collapsing to zero height: retire it fully. Deferred out of the
    /// ticker/callback via <see cref="needsCollapseCleanup"/> so we never unmount + rebuild the tree
    /// re-entrantly from inside the animation pump.</summary>
    private void OnDepartingCollapsed((Guid, Guid) key)
    {
        if (!departing.ContainsKey(key)) return;
        departing.Remove(key);
        awaitingRemoval.Remove(key);
        collapseRegistry.Release(DepartKey(key));
        needsCollapseCleanup = true;
    }

    /// <summary>Stable string key for a pin identity, used to key its collapse controller in the registry.</summary>
    private static string DepartKey((Guid, Guid) key) => $"{key.Item1:N}:{key.Item2:N}";

    /// <summary>Low-frequency tick: fire any pin windows that have elapsed — sending the deferred
    /// completion to the server (design D7) — and act as a cheap safety re-read. Only rebuilds when a
    /// window actually expired and the HUD is open (the subsequent server re-push lands in
    /// <see cref="OnMyPinsChanged"/> and rebuilds again with the authoritative result).</summary>
    private void OnTick(float dt)
    {
        elapsedMs += dt * 1000.0;

        // Temporal-storm corruption (hud-temporal-storm-corruption): advance/re-scramble the corruption
        // and rebuild on trigger/storm transitions or the 0–5 s cadence. Cheap when no trigger is active
        // (a couple of null-safe reads) and only rebuilds while the HUD is open.
        bool corruptionRebuilt = TickCorruption();

        // Interpolate the timer countdown between server pushes (250ms tick, server pushes every 1s).
        // InGame-mode timers drain at the world's in-game time rate (≈30 in-game s per real s by default),
        // matching the server's decrement so the smooth local display doesn't diverge from the authoritative
        // value. RealTime timers drain one-per-real-second.
        var timer = modSystem.MyTimer;
        if (timer?.Status == Scribe.Core.TimerStatus.Running)
        {
            if (_timerResync) { _timerLocalRemaining = timer.RemainingSeconds; _timerResync = false; }
            else
            {
                double rate = timer.Mode == Scribe.Core.TimerMode.InGame ? ScribeTimeRate.InGamePerReal(capi) : 1.0;
                _timerLocalRemaining = Math.Max(0, _timerLocalRemaining - dt * rate);
            }
            // NOTE: the repaint is NOT triggered here. This 250ms tick only interpolates the local
            // remaining seconds for accuracy; the actual ForceRebuild fires from the mod system's shared
            // 1Hz TimerDisplayTick (see OnTimerDisplayTick) so the HUD and the Notebook Timer tab repaint
            // in the exact same dispatch rather than up to 250ms apart.
        }

        if (pendingCompletions.Count == 0) return;

        bool anyExpired = false;
        foreach (var key in pendingCompletions.Keys.ToList())
        {
            var pending = pendingCompletions[key];
            if (elapsedMs >= pending.ExpiryMs)
            {
                pendingCompletions.Remove(key);
                // A Sink completion settles to the bottom now and holds that place for the session
                // (scribe-settings-followups 2.1): record it so a later uncheck can't pull it back up.
                if (pending.Policy == ScribeCompletionPolicy.Sink) sunkOrder.Add(key);
                // A destructive completion (Unpin/Delete/UnpinSink) will remove the pin server-side; instead
                // of dropping its row in one frame, start collapsing its height to zero in place, then remove
                // it when the collapse completes (scribe-list-collapse). Its text already faded to ~0 over
                // the window, so the collapse just closes the empty space — no flash. Snapshot it (with its
                // current display index) so it keeps rendering even after the server push drops the live pin.
                if (pending.Policy is ScribeCompletionPolicy.Unpin or ScribeCompletionPolicy.Delete
                                   or ScribeCompletionPolicy.UnpinSink)
                    BeginDeparting(key);
                SendCompletion(key.Item1, key.Item2, pending.Policy);
                anyExpired = true;
            }
        }

        // TickCorruption may already have rebuilt this tick; don't rebuild twice.
        if (anyExpired && !corruptionRebuilt && IsOpened()) ForceRebuild();
    }

    // ---------------- Row state helpers ----------------

    /// <summary>The done-state to DISPLAY for a pin: the optimistic override if a completion is in flight
    /// (pending or just-sent), else the authoritative snapshot.</summary>
    private bool DisplayedDone(ScribePinnedRef pin)
        => optimisticDone.TryGetValue((pin.OwnerDocId, pin.TaskId), out bool d) ? d : pin.LastKnownDone;

    /// <summary>The pending (not-yet-sent) completion for a pin, or null if none is in its window.</summary>
    private PendingCompletion? PendingFor(ScribePinnedRef pin)
        => pendingCompletions.TryGetValue((pin.OwnerDocId, pin.TaskId), out var p) ? p : null;

    /// <summary>Whether a pin sinks to the bottom for ORDERING (HUD's undo-aware overlay on the Core
    /// <see cref="ScribePinOrdering"/> sink rule). True if the pin already settled under Sink this session
    /// (in <see cref="sunkOrder"/>) — which holds the bottom for the session even after an uncheck
    /// (scribe-settings-followups 2.2) — OR it is currently done under the Sink policy past its window.
    /// Under <see cref="ScribeCompletionPolicy.Keep"/> a done pin never sinks (it holds its place);
    /// Unpin/Delete pins are removed after the window so ordering is moot for them. During the window the
    /// pin is held in place (not sunk) so the sink animates as it settles on expiry.</summary>
    private bool SunkForOrder(ScribePinnedRef pin)
    {
        // A task that already settled under Sink this session keeps its bottom position for the session,
        // regardless of its current done-state (an uncheck must NOT pull it back up — 2.2).
        if (sunkOrder.Contains((pin.OwnerDocId, pin.TaskId))) return true;
        if (!DisplayedDone(pin)) return false;
        // Within its pin window a done pin is always held in place (not sunk), so the sink can animate as
        // it settles when the window expires — regardless of the pending policy.
        if (PendingFor(pin) is not null) return false;
        // Sent + confirmed done: it only remains visible under a non-removing policy (Sink/Keep — Unpin/
        // Delete/UnpinSink removed it). Sink de-prioritizes it; Keep holds its place. We don't persist
        // which policy completed it, so the player's current policy is the proxy.
        return modSystem.MySettings.CompletionPolicy is ScribeCompletionPolicy.Sink
                                                      or ScribeCompletionPolicy.UnpinSink;
    }

    /// <summary>Whether a row should render MUTED (the completed, resting-at-bottom look). Tied to actually
    /// being done — a sunk row that the player later unchecks holds its bottom position (via
    /// <see cref="SunkForOrder"/>) but reads as an active row again, not a muted-done one
    /// (scribe-settings-followups 2.2).</summary>
    private bool SunkVisual(ScribePinnedRef pin) => DisplayedDone(pin) && SunkForOrder(pin);

    /// <summary>Whether a pin is inside a pending window whose text should fade out as a countdown
    /// preview: Unpin/Delete (destructive — row departs after the window) and Sink/UnpinSink (row
    /// moves to the bottom after the window — v1-playtest-fixes 9.1). Keep doesn't fade. Departing
    /// rows are excluded here because their text is rendered at a fixed zero opacity instead.</summary>
    private bool IsFadingOut(ScribePinnedRef pin)
        => PendingFor(pin) is { } pending
           && pending.Policy is ScribeCompletionPolicy.Unpin or ScribeCompletionPolicy.Delete
                              or ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;

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

    private void SendClearTimer()
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeClearTimerMessage());
    }

    // ---------------- Temporal-storm corruption ----------------

    /// <summary>
    /// Reads the two client-local instability triggers and returns the effective corruption strength
    /// (0..1) plus whether a storm is active (which drives only the title swap). Both triggers are read
    /// off <c>capi</c> each rebuild:
    /// <list type="bullet">
    /// <item>storm tier → vanilla's own glitch-strength bases (Light 0.53 / Medium 0.67 / Heavy 0.90)
    /// when <see cref="SystemTemporalStability"/>'s <c>StormData.nowStormActive</c> is set;</item>
    /// <item>low personal stability → a linear ramp from 0 at 0.50 stability to 1 at 0.10, read off the
    /// player's <c>temporalStability</c> watched attribute.</item>
    /// </list>
    /// The effective strength is the greater of the two. When the player's storm-corruption preference is
    /// off, or the temporal system can't be resolved (e.g. a non-survival server), this returns
    /// <c>(0, false)</c> so both corruption and the title swap are suppressed — the graceful no-op.
    /// </summary>
    private (double strength, bool stormActive) ComputeCorruption()
    {
        // Player opt-out (settings toggle) fully disables the effect — no corruption, no title swap.
        if (!modSystem.MySettings.StormCorruption) return (0.0, false);

        var stormSys = capi.ModLoader.GetModSystem<SystemTemporalStability>();
        if (stormSys is null) return (0.0, false);

        // Storm trigger: tier → vanilla glitch-strength base, only while a storm is actually active.
        bool stormActive = stormSys.StormData.nowStormActive;
        double stormStrength = stormActive
            ? stormSys.StormData.nextStormStrength switch
            {
                EnumTempStormStrength.Heavy  => StormHeavyStrength,
                EnumTempStormStrength.Medium => StormMediumStrength,
                _                            => StormLightStrength, // Light (and any unknown) → lightest
            }
            : 0.0;

        // Low-stability trigger: linear ramp 0 at 0.50 → 1 at 0.10, clamped. Default 1.0 (fully stable)
        // when the attribute is absent, so the trigger simply never fires.
        double stability = capi.World.Player?.Entity?.WatchedAttributes?.GetDouble("temporalStability", 1.0) ?? 1.0;
        double stabilityStrength = Math.Clamp(
            (StabilityRampUpper - stability) / (StabilityRampUpper - StabilityRampLower), 0.0, 1.0);

        return (Math.Max(stormStrength, stabilityStrength), stormActive);
    }

    /// <summary>
    /// The corruption re-scramble tick (hud-temporal-storm-corruption 4.1/4.2), driven off the existing
    /// 250 ms <see cref="OnTick"/>. While a trigger is active it advances the seed and rebuilds on a
    /// randomized 0–5 s cadence so the marks writhe; it also edge-detects the trigger becoming active/
    /// inactive and the storm flag flipping, rebuilding immediately on either transition so the title
    /// swap and first corruption are prompt. Returns true when it triggered a rebuild (so the caller can
    /// avoid a redundant one).
    /// </summary>
    private bool TickCorruption()
    {
        var (strength, stormActive) = ComputeCorruption();
        bool active = strength > 0.0;

        bool transition = active != _corruptionWasActive || stormActive != _stormWasActiveHud;
        _corruptionWasActive = active;
        _stormWasActiveHud = stormActive;

        long now = capi.World.ElapsedMilliseconds;

        if (transition)
        {
            // A fresh trigger (or title-swap) transition: reschedule and rebuild now so the effect is prompt.
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { ForceRebuild(); return true; }
            return false;
        }

        // Steady state: re-scramble on the randomized cadence while a trigger remains active.
        if (active && now >= _nextRescrambleMs)
        {
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { ForceRebuild(); return true; }
        }

        return false;
    }

    /// <summary>A fresh randomized re-scramble interval in [0, 5000] ms. Uses the game's client RNG
    /// (Mod-layer randomness is fine — unlike Core); index-free since order doesn't matter here.</summary>
    private long NextRescrambleInterval()
        => RescrambleMinMs + (long)(capi.World.Rand.NextDouble() * (RescrambleMaxMs - RescrambleMinMs));

    // ---------------- Build ----------------

    /// <summary>The live, ordered, capped rows to display — the authoritative pin set minus any rows that are
    /// collapsing out (in <see cref="awaitingRemoval"/>), ordered by the Core rule plus the HUD's undo-window
    /// and durable-sink overlays, then capped. Shared by <see cref="Build"/> and <see cref="BeginDeparting"/>
    /// (so a departing row is snapshotted at the exact display index it held).</summary>
    private List<HudPinRow> BuildOrderedRows()
    {
        // Hide rows whose destructive completion was sent and are now collapsing out, so they don't flash
        // back to full opacity — the collapsing snapshot in `departing` is rendered in their place instead
        // (scribe-list-collapse; supersedes the scribe-settings-followups 1.1 instant-hide).
        var pins = awaitingRemoval.Count == 0
            ? modSystem.MyPins
            : modSystem.MyPins.Where(p => !awaitingRemoval.Contains((p.OwnerDocId, p.TaskId))).ToList();

        // Drop any settled-sink identities the server has since removed, so the session set can't grow
        // unbounded or resurrect a stale ordering (scribe-settings-followups 2.3).
        if (sunkOrder.Count > 0)
        {
            var live = modSystem.MyPins.Select(p => (p.OwnerDocId, p.TaskId)).ToHashSet();
            sunkOrder.RemoveWhere(k => !live.Contains(k));
        }

        // Core ordering (not-done above done, stable) with the HUD's undo-window overlay: an in-window
        // completed pin is ordered as if not-done so it stays put until it sinks; a settled-sink pin holds
        // the bottom for the session even once unchecked (scribe-settings-followups 2.2).
        var ordered = pins.Where(p => !SunkForOrder(p))
            .Concat(pins.Where(SunkForOrder))
            .ToList();

        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        return ordered.Take(max)
            .Select(p => new HudPinRow(
                p.OwnerDocId, p.TaskId, p.LastKnownText, DisplayedDone(p), SunkVisual(p),
                FadingOut: IsFadingOut(p), Departing: false))
            .ToList();
    }

    /// <inheritdoc />
    protected override Widget Build()
    {
        var shown = BuildOrderedRows();

        // Splice each collapsing row back in at the display index it held when it began departing, so it
        // collapses IN PLACE (rather than jumping to the end) even after the server push drops the live pin
        // (scribe-list-collapse). Insert in ascending index order, clamped to the current list length.
        if (departing.Count > 0)
        {
            foreach (var d in departing.Values.OrderBy(d => d.Index))
            {
                int at = Math.Clamp(d.Index, 0, shown.Count);
                shown.Insert(at, d.Row with { Departing = true });
            }
        }

        // Indicative "+N more" only (design "+N more affordance"): pins beyond the visible cap. Departing
        // rows are already-completed removals, so they don't count toward the overflow tally.
        int liveCount = awaitingRemoval.Count == 0
            ? modSystem.MyPins.Count
            : modSystem.MyPins.Count(p => !awaitingRemoval.Contains((p.OwnerDocId, p.TaskId)));
        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        int moreCount = Math.Max(0, liveCount - max);

        // The HUD is NOT governed by Pixel-Art Display (scribe-themed-toggle): only the Lectern dialog
        // toggles between Scribe's light theme and the global one. The HUD pins always render on the
        // player's global LibGUI theme, so there is deliberately NO Theme wrap here — HudPinsContent reads
        // ThemeData.Default via Theme.Of(context) like any un-wrapped widget.
        // Build a timer snapshot with interpolated remaining for smooth display.
        Scribe.Core.TimerStore? timerSnapshot = null;
        var rawTimer = modSystem.MyTimer;
        if (rawTimer is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired })
        {
            timerSnapshot = new Scribe.Core.TimerStore
            {
                Status           = rawTimer.Status,
                Mode             = rawTimer.Mode,
                Label            = rawTimer.Label,
                RemainingSeconds = rawTimer.Status == Scribe.Core.TimerStatus.Running
                    ? _timerLocalRemaining
                    : 0,
            };
        }

        // Temporal-storm corruption signal (hud-temporal-storm-corruption): the effective strength + storm
        // flag for this build, and the current seed so every corrupted string in the tree scrambles
        // coherently within one frame. Recomputed here so a plain ForceRebuild (e.g. the 1 Hz timer tick)
        // reflects the live storm/stability state, not just the re-scramble tick.
        var (corruptionStrength, stormActive) = ComputeCorruption();

        return new HudPinsContent(
            rows: shown,
            moreCount: moreCount,
            corruptionStrength: corruptionStrength,
            stormActive: stormActive,
            corruptionSeed: _corruptionSeed,
            collapseRegistry: collapseRegistry,
            onDepartingCollapsed: (docId, taskId) => OnDepartingCollapsed((docId, taskId)),
            collapsed: modSystem.MySettings.HudCollapsed,
            // Header/footer align toward the anchored edge (v1-playtest-fixes 5.3): left for a left-anchored
            // HUD, right otherwise. Same anchor classification the ApplyAnchor X-position switch uses.
            leftAligned: ScribePlayerSettings.NormalizeAnchor(modSystem.MySettings.HudAnchor).IsLeftAnchored(),
            rowWidth: ScribePlayerSettings.ClampHudRowWidth(modSystem.MySettings.HudRowWidth),
            rowFontSize: ScribeRowConstants.BaseHudFontSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            checkboxSize: ScribeRowConstants.BaseHudCheckboxSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            onToggleRow: OnToggleRow,
            onToggleCollapsed: ToggleCollapsed,
            onOpenSettings: modSystem.OpenSettings,
            timerData: timerSnapshot,
            onClearTimer: SendClearTimer,
            capiForTimer: capi);
    }

    // ---------------- Lifecycle ----------------

    /// <inheritdoc />
    public override void Dispose()
    {
        modSystem.MyPinsChanged -= OnMyPinsChanged;
        modSystem.MyTimerChanged -= OnMyTimerChanged;
        modSystem.TimerDisplayTick -= OnTimerDisplayTick;
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        collapseRegistry.Dispose();
        base.Dispose();
    }
}

// ============================================================================
// HUD content tree
// ============================================================================

/// <summary>A value snapshot of one HUD row: identity, last-known text, its displayed done-state,
/// whether it is currently sunk (muted, ordered at the bottom), whether it is fading out inside a
/// pending destructive-completion window (Unpin/Delete about to send — design D7), and whether it is
/// DEPARTING (its window elapsed and it is now collapsing its height to zero before removal —
/// scribe-list-collapse). Carries no live pin reference.</summary>
internal readonly record struct HudPinRow(
    Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk, bool FadingOut, bool Departing);

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
    /// text legible over any world background without a background plate (design D1).
    /// Tightened from 0.6 → 0.45 (v1-playtest-fixes): same protective halo, slightly crisper edge.</summary>
    private const float GlowWidth = 0.45f;

    /// <summary>Opacity of a sunk (completed, past-undo) row — muted but still readable/undoable.</summary>
    private const float SunkOpacity = 0.5f;

    private readonly IReadOnlyList<HudPinRow> rows;
    private readonly int moreCount;
    /// <summary>Effective temporal-corruption strength (0..1) for this build; 0 = no corruption
    /// (hud-temporal-storm-corruption). Applied to every user-visible string via <see cref="Corrupt"/>.</summary>
    private readonly double corruptionStrength;
    /// <summary>Whether a temporal storm is active — drives only the title swap to "Survive the Storm".</summary>
    private readonly bool stormActive;
    /// <summary>Seed for the corruptor this build; the host advances it on the re-scramble cadence.</summary>
    private readonly int corruptionSeed;
    private readonly ScribeCollapseRegistry collapseRegistry;
    private readonly Action<Guid, Guid> onDepartingCollapsed;
    private readonly bool collapsed;
    private readonly bool leftAligned;
    private readonly float rowWidth;
    private readonly float rowFontSize;
    private readonly float checkboxSize;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    private readonly Action onToggleCollapsed;
    private readonly Action onOpenSettings;
    private readonly Scribe.Core.TimerStore? timerData;
    private readonly Action? onClearTimer;
    private readonly ICoreClientAPI? capiForTimer;

    public HudPinsContent(
        IReadOnlyList<HudPinRow> rows,
        int moreCount,
        double corruptionStrength,
        bool stormActive,
        int corruptionSeed,
        ScribeCollapseRegistry collapseRegistry,
        Action<Guid, Guid> onDepartingCollapsed,
        bool collapsed,
        bool leftAligned,
        float rowWidth,
        float rowFontSize,
        float checkboxSize,
        Action<Guid, Guid, bool> onToggleRow,
        Action onToggleCollapsed,
        Action onOpenSettings,
        Scribe.Core.TimerStore? timerData = null,
        Action? onClearTimer = null,
        ICoreClientAPI? capiForTimer = null)
    {
        this.rows = rows;
        this.moreCount = moreCount;
        this.corruptionStrength = corruptionStrength;
        this.stormActive = stormActive;
        this.corruptionSeed = corruptionSeed;
        this.collapseRegistry = collapseRegistry;
        this.onDepartingCollapsed = onDepartingCollapsed;
        this.collapsed = collapsed;
        this.leftAligned = leftAligned;
        this.rowWidth = rowWidth;
        this.rowFontSize = rowFontSize;
        this.checkboxSize = checkboxSize;
        this.onToggleRow = onToggleRow;
        this.onToggleCollapsed = onToggleCollapsed;
        this.onOpenSettings = onOpenSettings;
        this.timerData = timerData;
        this.onClearTimer = onClearTimer;
        this.capiForTimer = capiForTimer;
    }

    /// <summary>Run a user-visible string through the Core corruptor at this build's
    /// <see cref="corruptionStrength"/> (hud-temporal-storm-corruption 3.3/3.4). A per-string
    /// <paramref name="seedOffset"/> is added to <see cref="corruptionSeed"/> so different strings in the
    /// same frame don't all inject the identical mark pattern, while the whole frame still advances
    /// together on the re-scramble tick. A strength of 0 returns the input unchanged (the corruptor
    /// short-circuits), so this is a no-op when no trigger is active.</summary>
    private string Corrupt(string text, int seedOffset = 0)
        => ScribeTextCorruptor.Corrupt(text, corruptionStrength, corruptionSeed + seedOffset);

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        // Slightly darker glow (1.0 vs 0.9) for more contrast over busy world backgrounds
        // (v1-playtest-fixes; was 0.9).
        Vector4 glow = new(0f, 0f, 0f, 1.0f);

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
                    Corrupt(Lang.Get("scribe:scribe-hud-more", moreCount), seedOffset: 101),
                    new TextStyle
                    {
                        // Desaturated 66% (keep 34% of the theme chroma) so the "+N more" footer and the
                        // header gear — which share OnSurfaceVariant — read as a muted, near-grey HUD accent
                        // rather than the theme's tinted variant (v1-release-checklist 11.2 follow-up).
                        Color = ScribeRowConstants.ShiftBrightness(colors.OnSurfaceVariant, 0f, saturationScale: 0.34f),
                        FontSize = 13,
                        GlowWidth = GlowWidth,
                        GlowColor = glow,
                    }));
            }

            // Timer row: below the pins (and +N more), separated by a small divider, shown when
            // a Clockmaker's Notebook timer is Running or Fired.
            if (timerData is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired })
            {
                children.Add(new Divider());
                children.Add(BuildTimerRow(timerData, colors, glow, capiForTimer));
            }
        }

        // Constrain the whole HUD to the configured fixed width (task 4.5) via a SizedBox, so a long
        // task wraps within that width instead of the shrink-wrap window growing arbitrarily wide. The
        // Column aligns its rows within that fixed width toward the anchored edge — LEFT for a
        // left-anchored HUD, RIGHT (End) otherwise (v1-playtest-fixes 5.3) — so the min-width header
        // ("Pinned" + gear) and footer ("+N more") hug the correct side. The task rows are Max-width
        // (they fill the column), so this alignment only visibly moves the header/footer.
        return new SizedBox(
            width: rowWidth,
            child: new Padding(
                EdgeInsets.All(2),
                child: new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
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
            Color = new Vector4(0.80f, 0.80f, 0.80f, 1f), // near-white header (v1-playtest-fixes)
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
                    // The chevron is a glyph affordance, not prose — leave it uncorrupted. The title text
                    // swaps to the storm call-to-action while a storm is active, then is corrupted like the
                    // rest of the HUD (hud-temporal-storm-corruption 3.3).
                    new Text(chevron, titleStyle),
                    new Text(
                        Corrupt(Lang.Get(stormActive ? "scribe:scribe-hud-title-storm" : "scribe:scribe-hud-title")),
                        titleStyle),
                }));

        // Gear sized to sit proportionally with the chevron/title beside it (scribe-settings-followups 4.2):
        // 12px reads right against the 14px title, where the prior 16px looked oversized. Its base color is
        // desaturated 66% to match the "+N more" footer (both share OnSurfaceVariant); ScribeHudGearButton
        // owns the hover-brighten (+10 V) and the up-3/left-5 nudge + tap. It is self-stateful so the hover
        // survives the HUD's ForceRebuild (mirroring ScribeFadeText).
        var gear = new ScribeHudGearButton(
            baseColor: ScribeRowConstants.ShiftBrightness(colors.OnSurfaceVariant, 0f, saturationScale: 0.34f),
            onTap: onOpenSettings);

        return new Row(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[] { collapseToggle, gear });
    }

    /// <summary>Full duration of the destructive-pending (Unpin/Delete) text fade, matched to the HUD pin
    /// window so the ramp reads as a countdown to removal (scribe-settings-followups 1.1).</summary>
    private const int FadeWindowMs = 1500;

    /// <summary>One HUD row: [checkbox][text], the lectern read row minus chrome (no grip spacer, no
    /// pinned tint, no per-row buttons — design D1). Opacity animates by state: a destructive-pending row
    /// (Unpin/Delete in its window) fades its text LINEARLY from fully opaque to fully transparent over the
    /// window (a visible countdown — scribe-settings-followups 1.1); a sunk row mutes to
    /// <see cref="SunkOpacity"/>; else full. The checkbox stays fully opaque and clickable so undo is
    /// always available (design D7) — only the TEXT fades, via a nested AnimatedOpacity.</summary>
    private Widget BuildRow(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        // HUD text is explicitly near-white rather than theme.OnSurface — the HUD always renders over
        // the live game world on the global (dark) theme, and the tester wanted more legibility contrast
        // (v1-playtest-fixes). Sunk rows use a more muted near-white; active rows are brighter.
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = row.Sunk
                ? new Vector4(0.70f, 0.70f, 0.70f, 1f)   // sunk: muted light grey
                : new Vector4(0.93f, 0.93f, 0.93f, 1f),   // active: near-white
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // The text fades LINEARLY toward full transparency over the window for a destructive-pending row (a
        // countdown preview of removal — scribe-settings-followups 1.1). ScribeFadeText owns its own ticker
        // (see its remarks): AnimatedOpacity can't be used here because the HUD rebuilds via ForceRebuild,
        // which recreates the tree and makes an implicit tween snap straight to its target (the "instant
        // jump to 0" bug). A non-fading row is fully opaque. Only the text fades — the checkbox stays
        // clickable for undo.
        //
        // A DEPARTING row has already finished its fade; render its text at fixed zero opacity rather than
        // via ScribeFadeText, whose state-owned controller would restart from full on this remount and flash
        // the text back in before the collapse (scribe-list-collapse). The row body then collapses to zero.
        // Corrupt the row text (hud-temporal-storm-corruption 3.4). A per-row seed offset (derived from the
        // task identity) keeps two rows from injecting the identical mark pattern, while the whole HUD still
        // re-scrambles together on the host's cadence. No-op when no trigger is active (strength 0).
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        Widget text = row.Departing
            ? new Opacity(0f, new Text(rowText, textStyle))
            : new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);

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
                    size: checkboxSize,
                    // Grayscale, not the theme default (v1-release-checklist 11.2). The stock CheckboxStyle
                    // maps CheckColor←Primary, which is the parchment theme's brown ochre — out of place on
                    // the HUD, which deliberately renders theme-independent near-white text over the world
                    // glow (see textStyle above). Override to a light-grey box + near-white check so the
                    // checkbox reads as part of the same grayscale HUD as its text.
                    style: new CheckboxStyle
                    {
                        CheckColor = new Vector4(0.867f, 0.867f, 0.867f, 1f),   // #dddddd — softer than white, less blinding
                        BackgroundColor = new Vector4(0.28f, 0.28f, 0.28f, 0.75f), // neutral dark-grey box at 75% opacity
                        BorderColor = new Vector4(0.8f, 0.8f, 0.8f, 0.75f),  // #cccccc at 75% opacity — dimmer light-grey outline
                        BorderThickness = 1.5f,
                        CornerRadius = 2f,
                        LabelStyle = textStyle,   // unused (no label) but required by the struct
                    }),
                // Expanded so the (SoftWrap) text wraps within the remaining fixed width.
                new Expanded(child: text),
            });

        // A sunk row mutes the WHOLE row (checkbox + text) to SunkOpacity, faded rather than snapped so a
        // completing task reads as a gentle settle. The ValueKey stabilizes row identity across rebuilds
        // for the animation.
        Widget styled = new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));

        // A departing row collapses its height to zero (rows below slide up to meet it), then is removed via
        // onDepartingCollapsed when the collapse completes (scribe-list-collapse). The collapse controller is
        // host-owned (keyed by identity) so it resumes across the HUD's ForceRebuild remounts. Wrapping keeps
        // the ValueKey OUTSIDE the collapsible so the row's identity (and its AnimatedOpacity state) is stable.
        if (!row.Departing) return styled;
        return new ScribeCollapsible(
            id: $"{row.DocId:N}:{row.TaskId:N}",
            collapsing: true,
            registry: collapseRegistry,
            onCollapsed: () => onDepartingCollapsed(row.DocId, row.TaskId),
            child: styled,
            key: new ValueKey<Guid>(row.TaskId));
    }

    private Widget BuildTimerRow(Scribe.Core.TimerStore timer, ColorScheme colors, Vector4 glow, ICoreClientAPI? capi)
    {
        bool fired = timer.Status == Scribe.Core.TimerStatus.Fired;

        var textStyle = new TextStyle
        {
            FontSize  = rowFontSize,
            Color     = new Vector4(0.93f, 0.93f, 0.93f, 1f),
            GlowWidth = GlowWidth,
            GlowColor = glow,
        };
        var muted = textStyle with { Color = new Vector4(0.70f, 0.70f, 0.70f, 1f) };

        // Countdown or blinking 00:00 — corrupted like the rest of the HUD (hud-temporal-storm-corruption
        // 3.4). Distinct per-string seed offsets so the label and countdown don't share a mark pattern.
        string timeText = Corrupt(
            fired ? "00:00" : FormatTimerDuration(timer.RemainingSeconds),
            seedOffset: 202);

        Widget timeWidget = (fired && capi is not null)
            ? new ScribeBlinkText(timeText, textStyle, capi)
            : new Text(timeText, textStyle);

        // Clock icon — rotates when fired.
        Widget iconWidget = (fired && capi is not null)
            ? new ScribeTimerIcon(rowFontSize * 1.1f, new Vector4(0.93f, 0.93f, 0.93f, 1f), capi)
            : new ScribeVsIconGlyph("scribetimer", rowFontSize * 1.1f, new Vector4(0.93f, 0.93f, 0.93f, 1f));

        // Label (if any) muted next to the icon — corrupted with its own seed offset.
        string label = Corrupt(
            timer.Label.Length > 0 ? timer.Label : Lang.Get("scribe:scribe-hud-timer-label"),
            seedOffset: 303);

        Widget row = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                iconWidget,
                new Expanded(child: new Text(label, muted)),
                timeWidget,
            });

        // Clicking the timer row when fired sends a clear.
        if (fired && onClearTimer is not null)
            row = new GestureDetector(onTap: _ => onClearTimer(), child: row);

        return row;
    }

    private static string FormatTimerDuration(double seconds)
    {
        int total = (int)Math.Max(0, seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }
}

/// <summary>
/// A text widget that fades its own opacity from 1→0 over <paramref name="durationMs"/> when
/// <c>fading</c> is set, using a self-owned <see cref="AnimationController"/> that it drives frame-by-frame
/// (scribe-settings-followups 1.1).
///
/// <para><b>Why not <see cref="AnimatedOpacity"/>?</b> The HUD's only rebuild path is
/// <see cref="GuiBase.ForceRebuild"/>, which UNMOUNTS and recreates the whole widget tree rather than
/// reconciling it. An implicitly-animated widget only animates across a reconciling <c>UpdateWidget</c>
/// (retarget tween → <c>Forward()</c>); recreated fresh, its tween inits <c>Begin=End=target</c> and
/// evaluates to the target instantly — which is exactly the "snap straight to 0" bug. This widget instead
/// starts its own controller in <see cref="InitState"/> and ticks itself, so it ramps correctly the
/// moment it is (re)mounted in the fading state, needing no per-frame rebuild from the HUD.</para>
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
        if (!Widget.Fading) return;

        // Own ticker: ramp 0→1 over the window; opacity is 1 − value so the text fades 1→0. Repaint each
        // tick via MarkNeedsBuild (SetState) — the reconciling rebuild path, so this animates itself
        // regardless of the parent using ForceRebuild.
        controller = new AnimationController(TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
        controller.OnValueChanged += _ => Element.MarkNeedsBuild();
        controller.Forward();
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

/// <summary>The HUD header's settings gear: a self-stateful icon button that brightens +10 HSV Value on
/// hover and opens the settings surface on tap. Stateful (not a bare GestureDetector) so the hover state
/// survives the HUD's <see cref="GuiBase.ForceRebuild"/> — the same reason <see cref="ScribeFadeText"/> is.
/// The up-3/left-5 nudge lives here: left comes off the leading pad, and the −3 y is a
/// <c>Transform.Translate</c> wrapping the GestureDetector so the CLICKABLE region moves with the paint
/// (<c>RenderTransform.GlobalToChild</c>), not just the drawn glyph.</summary>
internal sealed class ScribeHudGearButton : StatefulWidget
{
    public ScribeHudGearButton(Vector4 baseColor, Action onTap, Gui.Widgets.Framework.Key? key = null) : base(key)
    {
        BaseColor = baseColor;
        OnTap = onTap;
    }

    public Vector4 BaseColor { get; }
    public Action OnTap { get; }

    public override State CreateState() => new ScribeHudGearButtonState();
}

internal sealed class ScribeHudGearButtonState : State<ScribeHudGearButton>
{
    private bool hovered;

    public override Widget Build(BuildContext context)
    {
        // Hover brightens the gear +10 HSV Value (reusing ShiftBrightness, matching the lectern nav/row
        // buttons' hover feel); saturation/hue/alpha unchanged so it stays the same muted grey, just lighter.
        var color = hovered
            ? ScribeRowConstants.ShiftBrightness(Widget.BaseColor, +10f)
            : Widget.BaseColor;

        // VsIcon has no text-style glow (it's a tinted texture, not Skia text), so match the HUD text's
        // dark halo with a Container BoxShadow instead — black, zero-offset, blurred — the same mechanism
        // the lectern sidebar nav buttons use. It halos the icon's ~12px box rather than the glyph outline
        // the way text glow hugs letterforms, but reads as the same legibility halo over a busy world.
        var haloed = new Container(
            new BoxStyle
            {
                BoxShadows = new[]
                {
                    new BoxShadow(
                        Color: new Vector4(0f, 0f, 0f, 0.4f),   // softened black halo (full opacity read too harsh)
                        Offset: new Vector2(0f, 0f),
                        BlurRadius: 3f),
                },
            },
            new VsIcon("scribegear", 12f, color));

        return Transform.Translate(
            new MouseRegion(
                onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
                onExit: _ => { if (hovered) SetState(() => hovered = false); },
                child: new GestureDetector(
                    onTap: _ => Widget.OnTap(),
                    child: new Padding(
                        EdgeInsets.Only(left: 1),
                        child: haloed))),
            new Vector2(0f, -3f));
    }
}
