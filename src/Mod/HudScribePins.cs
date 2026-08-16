using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only HUD row trace)
using System.Linq;
using Gui;                       // GuiBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle
using Gui.Widgets.Animations;    // AnimatedOpacity, Curves
using Gui.Widgets.Basic;         // Text, Container, VsIcon
using Gui.Widgets.Events;        // PointerEvent
using Gui.Widgets.Framework;     // Widget, StatelessWidget, BuildContext, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, GestureDetector
using Gui.Widgets.Inventory;     // ItemStackDisplay (Tracker/Link item icon)
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2, Vector4
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
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

    /// <summary>Pins whose destructive completion (Unpin/Delete/UnpinSink) has been SENT and that are now
    /// waiting for the server's removal push (scribe-settings-followups 1.1 flicker fix). Between the window
    /// expiring (send) and the server re-pushing the pin set, the pin is still in
    /// <see cref="ScribeModSystem.MyPins"/> but is no longer <see cref="IsFadingOut"/>, so without this it
    /// would rebuild at FULL opacity for a frame — a visible flash. Pins in this set are filtered OUT of the
    /// item set handed to <see cref="ScribeAnimatedList"/> (migrate-hud-onto-animated-list): the pin's
    /// identity leaving the container's item set is exactly what triggers the container's <c>Immediate</c>
    /// collapse — it splices a frozen ghost at the row's slot and animates it closed, then self-retires it.
    /// Reconciled against the live set on each server push (<see cref="ReconcileAwaitingRemoval"/>): an id the
    /// server removed OR that the player re-pinned is dropped (the container's own reappear-cancels-departure
    /// revives a re-pinned row mid-collapse).</summary>
    private readonly HashSet<(Guid, Guid)> awaitingRemoval = new();

    /// <summary>Per-pinned-Tracker LIVE "have" count, recomputed from the viewer's carried inventory on the
    /// HUD's own 250ms tick (add-tracker-link-tasks 7.10), so a pinned Tracker's counter is live even with no
    /// Scribe dialog open — the dialog-bound count engine (<see cref="ScribeDialogBase"/>'s
    /// <c>RecomputeTrackers</c>) only runs while a surface is in its read view, so before this the HUD counter
    /// froze at the last snapshot. Keyed by pin identity <c>(OwnerDocId, TaskId)</c>. Overrides the pin
    /// snapshot's <see cref="ScribePinnedRef.CurrentQuantity"/> for display (<see cref="HudTrackerHave"/>) and
    /// is the rising-edge baseline for the HUD-driven auto-completion. Pruned to the current pinned-Tracker
    /// set each recompute so it can't leak entries for unpinned/removed tasks.</summary>
    private readonly Dictionary<(Guid, Guid), int> liveTrackerCounts = new();

    /// <summary>Resolved <see cref="CraftingRecipeIngredient"/> per Tracker target code, cached because
    /// resolving touches the item/block registries; the frequent recompute then re-runs only the cheap
    /// carried-stack sum. A null value caches "this code doesn't resolve → counts as 0". Mirrors the dialog
    /// engine's own <c>trackerIngredientCache</c>; an item code's resolution is stable for the session, so it
    /// is never invalidated.</summary>
    private readonly Dictionary<string, CraftingRecipeIngredient?> hudTrackerIngredientCache = new();

    /// <summary>Host-owned collapse (and entry) controllers, keyed by row identity, passed into
    /// <see cref="ScribeAnimatedList"/> so a motion RESUMES (not restarts) across the HUD's tree rebuilds
    /// (gui-row-animation-harness). The container drives it now; the HUD only reads <c>AnyAnimating</c> (to
    /// pin hover under a stationary cursor while the list reflows) and disposes it with the HUD.</summary>
    private readonly ScribeAnimationRegistry collapseRegistry = new();

    /// <summary>Keeps the collapse-time hover refresh running a few frames past the last animating frame so a
    /// refresh lands AFTER the completion-triggered <see cref="ForceRebuild"/> re-lays-out the fresh tree
    /// (fix-list-collapse-stale-hover). See <see cref="ScribeHoverRefreshLatch"/>.</summary>
    private readonly ScribeHoverRefreshLatch hoverRefreshLatch = new();

    /// <summary>Stable identity of the single persistent-root <see cref="ScribeDialogBody"/> that wraps the
    /// HUD content (reconcile-animating-surfaces §4.1). Allocated ONCE here (never in <see cref="Build"/>,
    /// per the <see cref="GlobalKey"/> contract) so <see cref="RebuildHudBody"/> can reach the live body
    /// State and reconcile the row list in place — REUSING each row's live State (its <see cref="ScribeFadeText"/>
    /// fade controller, <see cref="AnimatedOpacity"/> sink tween, <see cref="ScribeHudGearButton"/> hover) —
    /// instead of tearing the tree down with <see cref="GuiBase.ForceRebuild"/> (which remounts everything,
    /// snapping those animations and dropping hover). The 0⇄1 self-open/close stays a host concern
    /// (<c>TryOpen</c>/<c>TryClose</c> — §4.2); this only replaces the in-place repaint path.</summary>
    private readonly GlobalKey bodyKey = new();

    /// <summary>Reconcile the HUD content in place (reconcile-animating-surfaces §4.1): the replacement for
    /// <see cref="GuiBase.ForceRebuild"/> at every in-place update site — a pin push, a completion toggle, a
    /// tick-expiry, the collapse/corruption/timer repaints. A no-op <c>SetState</c> on the persistent body
    /// State reconciles the subtree, REUSING matching rows (same type + <c>ValueKey&lt;Guid&gt;(TaskId)</c>)
    /// so their animation State survives, rather than remounting them. A no-op before the body has mounted
    /// (defensive) or while the HUD is closed. Also arms the hover-refresh latch: unlike a
    /// <c>ForceRebuild</c> (which swaps <see cref="GuiBase.RootElement"/>, caught by
    /// <see cref="ScribeHoverRefreshLatch.ArmIfRebuilt"/>), a reconcile leaves RootElement unchanged, so a
    /// row-list reorder that slides a different row under a stationary cursor would otherwise leave stale
    /// hover — arming here re-dispatches a synthetic pointer-move over the next few frames
    /// (fix-list-collapse-stale-hover).</summary>
    private void RebuildHudBody()
    {
        if (!IsOpened()) return;
        bodyKey.CurrentState<ScribeDialogBody.BodyState>()?.Rebuild();
        hoverRefreshLatch.Arm();
    }

    /// <summary>A departing pin row's collapse finished and the list has shrunk (the container self-retired
    /// its ghost — migrate-hud-onto-animated-list). The HUD is shrink-wrapped (no scroll to clamp, unlike the
    /// Pin Tab), so the only concern is hover: a row below the departed one slid up under a stationary cursor
    /// while the window shrank, so re-arm the hover-refresh latch to re-dispatch a synthetic pointer-move over
    /// the next few frames. The per-frame <c>AnyAnimating</c> arming in <see cref="OnRenderGUI"/> already
    /// covers the animating frames; this is the belt-and-suspenders clamp for the settling frame.</summary>
    private void OnHudDepartureSettled() => hoverRefreshLatch.Arm();

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
            RebuildHudBody();
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
        // Keep hover self-healing under a STATIONARY cursor (LibGUI only recomputes hover on real mouse
        // motion). Three arming paths, now that in-place updates reconcile instead of ForceRebuild
        // (reconcile-animating-surfaces §4.1):
        //  1. A collapse reflowing the list every frame (AnyAnimating).
        //  2. Any reconcile that reorders rows — armed INSIDE RebuildHudBody, because a reconcile does NOT
        //     swap RootElement, so ArmIfRebuilt below can't catch it. This is the unpin/complete case: a
        //     row leaving slides a different row under the stationary cursor. (Under reconcile the reused
        //     elements KEEP their hovered flag — unlike a ForceRebuild's fresh hovered=false tree — so only
        //     a row that genuinely CHANGED slot needs the synthetic re-dispatch; arming is harmless when it
        //     didn't.)
        //  3. A genuine tree remount that DOES swap RootElement — the self-open (TryOpen) path — still
        //     caught here by identity. (There are no in-place ForceRebuilds left; this now only fires on open.)
        // The latch re-dispatches a synthetic pointer-move for a few frames past any trigger so the
        // reconciled/rebuilt tree (laid out a frame later) regains hover without a mouse wiggle. No-op idle.
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
        // Reconcile the sent-but-not-yet-removed set against the authoritative pin push: drop any id the
        // server has now removed (its ghost is collapsing or already gone in the container) or that the
        // player re-pinned (so it's handed back to the container as a live row and the container's own
        // reappear-cancels-departure revives it mid-collapse). migrate-hud-onto-animated-list.
        ReconcileAwaitingRemoval();

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
            RebuildHudBody();
        }

        // Refresh the live Tracker counts against the new pin set and rebuild once more if a displayed count
        // moved, so a newly-pushed (or newly-opened) Tracker pin shows its live carried count at once rather
        // than waiting for the next 250ms tick (add-tracker-link-tasks 7.10). No-op unless the HUD is open.
        if (IsOpened() && RecomputeHudTrackers()) RebuildHudBody();
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
            RebuildHudBody();
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

    /// <summary>Reconcile the sent-but-not-yet-removed set (<see cref="awaitingRemoval"/>) with a fresh
    /// authoritative pin push (migrate-hud-onto-animated-list). The collapse itself is owned by
    /// <see cref="ScribeAnimatedList"/> now: a pin held in this set is filtered OUT of the container's item
    /// set, and the container splices a frozen ghost at its slot and animates it closed. This method only
    /// keeps the suppression set from leaking: an id is dropped once the server confirms the pin GONE (no
    /// longer in <see cref="ScribeModSystem.MyPins"/>).
    ///
    /// <para>Dropping only on server-confirmed removal — not merely on "no longer <see cref="IsFadingOut"/>"
    /// — is load-bearing for the rapid multi-complete case: several destructive completions can be pending
    /// removal at once, and an intervening push (an unrelated pin change) must not un-suppress a still-present
    /// sent pin and flash it back to full opacity. A destructive completion is guaranteed to produce a removal
    /// push, so the removal always precedes any later re-pin of the same task — which is why a re-pin needs no
    /// special case here: by the time the task can reappear it has already left this set.</para></summary>
    private void ReconcileAwaitingRemoval()
    {
        if (awaitingRemoval.Count == 0) return;
        var live = modSystem.MyPins.Select(p => (p.OwnerDocId, p.TaskId)).ToHashSet();
        awaitingRemoval.RemoveWhere(k => !live.Contains(k));
    }

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

        // Live Tracker counts (add-tracker-link-tasks 7.10): recompute every pinned Tracker's carried-inventory
        // "have" on this tick so the HUD counter is live with no Scribe dialog open. Rebuild if a displayed
        // count actually moved (and the corruption tick didn't already rebuild this frame).
        bool trackersRebuilt = false;
        if (RecomputeHudTrackers() && !corruptionRebuilt && IsOpened())
        {
            RebuildHudBody();
            trackersRebuilt = true;
        }

        if (pendingCompletions.Count == 0) return;

        bool anyExpired = false;
        foreach (var key in pendingCompletions.Keys.ToList())
        {
            var pending = pendingCompletions[key];
            if (elapsedMs >= pending.ExpiryMs)
            {
                pendingCompletions.Remove(key);
                // A Sink completion needs no client-side bookkeeping on expiry: the deferred send fires below,
                // the server flips LastKnownDone and re-pushes, and the shared ForDisplay partition sinks the
                // now-done pin — the HUD and Pin Tab agree by construction (sync-pinned-order-per-player D1/D3).
                // A destructive completion (Unpin/Delete/UnpinSink) removes the pin server-side. Add it to
                // awaitingRemoval so it is filtered OUT of the item set handed to ScribeAnimatedList: the
                // pin's identity leaving the container's set is exactly what triggers the container's
                // Immediate collapse — the container splices a frozen ghost (the already-faded row) at the
                // slot it left and animates it closed, then self-retires it (migrate-hud-onto-animated-list).
                // Its text faded to ~0 over the window, so the ghost closes empty space — no flash. No manual
                // snapshot/splice/cleanup: the container captures the ghost from the row's last live frame.
                if (pending.Policy is ScribeCompletionPolicy.Unpin or ScribeCompletionPolicy.Delete
                                   or ScribeCompletionPolicy.UnpinSink)
                    awaitingRemoval.Add(key);
                SendCompletion(key.Item1, key.Item2, pending.Policy);
                anyExpired = true;
            }
        }

        // TickCorruption / the tracker recompute may already have rebuilt this tick; don't rebuild twice.
        if (anyExpired && !corruptionRebuilt && !trackersRebuilt && IsOpened()) RebuildHudBody();
    }

    // ---------------- Live Tracker count engine (add-tracker-link-tasks 7.10) ----------------

    /// <summary>Recompute every pinned Tracker's live "have" count from the viewer's carried inventory and
    /// reconcile — the HUD's own count engine, so a pinned Tracker's counter is LIVE with no Scribe dialog
    /// open. Mirrors <see cref="ScribeDialogBase"/>'s <c>RecomputeTrackers</c> but scoped to the pin set and
    /// running off the HUD's existing 250ms <see cref="OnTick"/> (rather than a separate <c>SlotModified</c>
    /// subscription): folding it into the tick the HUD already runs for its pin windows costs nothing and
    /// avoids the subscribe/unsubscribe lifecycle the dialog engine needs. 250ms latency is imperceptible for
    /// a HUD counter.
    ///
    /// <para><b>Defer to an open dialog.</b> A pinned Tracker whose owning document is open in ANY Scribe
    /// dialog (<see cref="ScribeDialogBase.OpenDocumentId"/>) is DISPLAY-ONLY here — the HUD does not send its
    /// count or fire the rising-edge completion for it. That covers both hazards the dialog engine already
    /// reasons about: in the read view the dialog itself owns the send + completion (double-driving would
    /// double-send a quantity and double-fire the completion), and in the editor view an external count write
    /// would fight the editor's scratch autosave flush. The live override is still computed while deferring so
    /// the baseline stays current for the moment the dialog closes and the HUD becomes the sole driver.</para>
    ///
    /// <para>Returns true when a DISPLAYED count changed, so <see cref="OnTick"/> rebuilds. No-op (returns
    /// false) when the HUD is closed, the player is unavailable, or no pinned task is a Tracker.</para></summary>
    private bool RecomputeHudTrackers()
    {
        if (!IsOpened()) return false;
        if (capi.World.Player is not { } player) return false;

        var trackerPins = modSystem.MyPins.Where(p => p.Kind == ScribeBlockKind.Tracker).ToList();

        // Prune live-count entries for pins that are gone (unpinned/removed) so the map can't leak. Done even
        // when there are no Tracker pins left (clears the last one out).
        if (liveTrackerCounts.Count > 0)
        {
            var liveKeys = trackerPins.Select(p => (p.OwnerDocId, p.TaskId)).ToHashSet();
            foreach (var key in liveTrackerCounts.Keys.ToList())
                if (!liveKeys.Contains(key)) liveTrackerCounts.Remove(key);
        }
        if (trackerPins.Count == 0) return false;

        var dialogHeld = DialogHeldTrackerDocs();
        bool anyDisplayChange = false;

        foreach (var pin in trackerPins)
        {
            var key = (pin.OwnerDocId, pin.TaskId);

            string codeKey = pin.TargetItemCode ?? "";
            if (!hudTrackerIngredientCache.TryGetValue(codeKey, out var ingredient))
            {
                ScribeTrackerCounter.TryResolveIngredient(capi.World, pin.TargetItemCode, out ingredient);
                hudTrackerIngredientCache[codeKey] = ingredient; // may cache null ("unresolvable → 0")
            }

            int counted = ingredient is null ? 0 : ScribeTrackerCounter.CountCarried(player, ingredient);
            int have = Math.Max(0, counted); // raw carried count; NOT capped at the target (overflow shows, 7.14)

            // Baseline for the rising edge AND the display diff: the last live value we computed for this pin,
            // else the pin snapshot (first sight this tick / this session).
            int baseline = liveTrackerCounts.TryGetValue(key, out var prev) ? prev : pin.CurrentQuantity;
            bool changed = have != baseline;
            liveTrackerCounts[key] = have; // always record so the display override is used consistently
            if (changed) anyDisplayChange = true;

            // A dialog has this doc open → defer entirely (read view drives it; editor must not be fought).
            // Display-only here; the baseline above is kept current for when the dialog closes.
            if (dialogHeld.Contains(pin.OwnerDocId)) continue;
            if (!changed) continue; // nothing moved → no send, no edge

            // Persist + converge through the server-authoritative path (the same op the dialog engine uses).
            SendTrackerQuantity(pin.OwnerDocId, pin.TaskId, have);

            // Rising edge only (unmet → met): apply the tracker-completion setting exactly once. Because we
            // skip unchanged counts and only fire on the rising edge — and Complete is guarded by !done below —
            // a later shortfall neither re-fires nor un-completes, mirroring the dialog engine (D6/4.4).
            bool wasMet = baseline >= pin.TargetQuantity;
            bool nowMet = have >= pin.TargetQuantity;
            if (nowMet && !wasMet) ApplyHudTrackerCompletion(pin);
        }

        return anyDisplayChange;
    }

    /// <summary>The set of DocIds a Scribe dialog currently has open (in any view). The HUD skips the server
    /// send + rising-edge completion for pinned Trackers in these docs so it never double-drives a doc a
    /// dialog's read view owns, nor writes an external count into a doc being edited (see
    /// <see cref="RecomputeHudTrackers"/> and <see cref="ScribeDialogBase.OpenDocumentId"/>).</summary>
    private HashSet<Guid> DialogHeldTrackerDocs()
    {
        var set = new HashSet<Guid>();
        foreach (var dialog in capi.Gui.OpenedGuis.OfType<ScribeDialogBase>())
            if (dialog.OpenDocumentId is { } docId) set.Add(docId);
        return set;
    }

    /// <summary>The "have" count to DISPLAY for a pinned Tracker: the HUD's live carried-inventory count if it
    /// has one, else the pin snapshot's last-known value (add-tracker-link-tasks 7.10).</summary>
    private int HudTrackerHave(ScribePinnedRef pin)
        => liveTrackerCounts.TryGetValue((pin.OwnerDocId, pin.TaskId), out var have)
            ? have
            : pin.CurrentQuantity;

    /// <summary>Apply the player's tracker-completion setting when a pinned Tracker fills up with NO dialog
    /// open (add-tracker-link-tasks 7.10) — the HUD-driven sibling of <see cref="ScribeDialogBase"/>'s
    /// <c>ApplyTrackerCompletion</c>:
    /// <list type="bullet">
    /// <item><b>Complete</b> — mark the task done through the same identity-addressed op a checkbox uses,
    /// honoring the player's completion policy (<see cref="SendCompletion"/>). Guarded by
    /// <see cref="DisplayedDone"/> so re-collecting after a drop can't un-complete an already-done Tracker; the
    /// optimistic flag flips the check mark at once. Sent immediately (not via the undo window) exactly like
    /// the dialog's auto-complete.</item>
    /// <item><b>Delete</b> — remove the task via the standalone <see cref="ScribeDeleteTaskMessage"/>.</item>
    /// <item><b>Nothing</b> — leave it satisfied.</item>
    /// </list></summary>
    private void ApplyHudTrackerCompletion(ScribePinnedRef pin)
    {
        switch (modSystem.MySettings.TrackerCompletion)
        {
            case ScribeTrackerCompletion.Complete:
                if (!DisplayedDone(pin))
                {
                    optimisticDone[(pin.OwnerDocId, pin.TaskId)] = true;
                    SendCompletion(pin.OwnerDocId, pin.TaskId, modSystem.MySettings.CompletionPolicy);
                }
                break;
            case ScribeTrackerCompletion.Delete:
                capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
                {
                    DocId = pin.OwnerDocId.ToByteArray(),
                    TaskId = pin.TaskId.ToByteArray(),
                });
                break;
            case ScribeTrackerCompletion.Nothing:
                break;
        }
    }

    /// <summary>Send a pinned Tracker's freshly-counted quantity to the server (add-tracker-link-tasks 7.10).
    /// The server resolves the owning document (registry for a Lectern, or by scanning the acting player's
    /// inventory for a Notebook/Tablet — the HUD viewer is that player), writes the clamped count lock-free,
    /// and resyncs. Independent of any open dialog. The HUD's own <see cref="liveTrackerCounts"/> override
    /// drives the display, so the counter is correct immediately without waiting for this round-trip.</summary>
    private void SendTrackerQuantity(Guid docId, Guid taskId, int quantity)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetTrackerQuantityMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Quantity = quantity,
        });
    }

    // ---------------- Row state helpers ----------------

    /// <summary>The done-state to DISPLAY for a pin: the optimistic override if a completion is in flight
    /// (pending or just-sent), else the authoritative snapshot.</summary>
    private bool DisplayedDone(ScribePinnedRef pin)
        => optimisticDone.TryGetValue((pin.OwnerDocId, pin.TaskId), out bool d) ? d : pin.LastKnownDone;

    /// <summary>The pending (not-yet-sent) completion for a pin, or null if none is in its window.</summary>
    private PendingCompletion? PendingFor(ScribePinnedRef pin)
        => pendingCompletions.TryGetValue((pin.OwnerDocId, pin.TaskId), out var p) ? p : null;

    /// <summary>Whether a row should render MUTED (the completed, resting-at-bottom look): displayed-done,
    /// past its undo window (no pending send), under a sinking policy — exactly the pins the shared
    /// <see cref="ScribePinOrdering.ForDisplay"/> partition has sunk to the bottom. Un-completing a sunk pin
    /// flips <see cref="DisplayedDone"/> false, so it reads as an active row again and returns to its prior
    /// position via that same partition (sync-pinned-order-per-player D2) — there is no session overlay
    /// holding it at the bottom. Within its window a done pin is held in place (not muted) so the sink can
    /// animate as it settles on expiry; under <see cref="ScribeCompletionPolicy.Keep"/> a done pin holds its
    /// place and is not muted; Unpin/Delete pins are removed after the window.</summary>
    private bool SunkVisual(ScribePinnedRef pin)
        => DisplayedDone(pin)
           && PendingFor(pin) is null
           && modSystem.MySettings.CompletionPolicy is ScribeCompletionPolicy.Sink
                                                     or ScribeCompletionPolicy.UnpinSink;

    /// <summary>Whether a pin is inside a pending window whose text should fade out as a countdown
    /// preview: Unpin/Delete (destructive — row leaves the item set after the window and the container
    /// collapses its ghost) and Sink/UnpinSink (row moves to the bottom after the window — v1-playtest-fixes
    /// 9.1). Keep doesn't fade. A row that has already left the set (past its window, in awaitingRemoval) is
    /// not in the pin set this reads, so it never reports fading — the container's zero-opacity-text frozen
    /// ghost renders the already-faded row as it collapses.</summary>
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

        if (IsOpened()) RebuildHudBody();
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

    /// <summary>Open a pinned Link's referenced Handbook page (add-tracker-link-tasks 5.5): parse the
    /// snapshotted <see cref="ScribePinnedRef.LinkTarget"/> and hand it to the same handbook-open helper the
    /// Read/editor Link rows use. This is the row-click plumbing gated on kind == Link (design D3c); it is
    /// entirely separate from the row's completion checkbox, so opening the page never toggles done-state. A
    /// no-op when the code doesn't resolve or the survival mod (handbook protocol) isn't loaded.</summary>
    private void OpenPinnedLink(string? linkTarget) => ScribeItemRef.OpenHandbookPage(capi, linkTarget);

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
            if (IsOpened()) { RebuildHudBody(); return true; }
            return false;
        }

        // Steady state: re-scramble on the randomized cadence while a trigger remains active.
        if (active && now >= _nextRescrambleMs)
        {
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { RebuildHudBody(); return true; }
        }

        return false;
    }

    /// <summary>A fresh randomized re-scramble interval in [0, 5000] ms. Uses the game's client RNG
    /// (Mod-layer randomness is fine — unlike Core); index-free since order doesn't matter here.</summary>
    private long NextRescrambleInterval()
        => RescrambleMinMs + (long)(capi.World.Rand.NextDouble() * (RescrambleMaxMs - RescrambleMinMs));

    // ---------------- Build ----------------

    /// <summary>The live, ordered, capped rows to display — the authoritative pin set minus any rows whose
    /// destructive completion was sent and are now collapsing out (in <see cref="awaitingRemoval"/>), ordered
    /// by the SAME shared rule the Pin Tab uses (<see cref="ScribePinOrdering.ForDisplay"/> under a sinking
    /// policy, else raw pin order) with no HUD-only overlay, then capped (sync-pinned-order-per-player D1). The
    /// collapsing rows themselves are the <see cref="ScribeAnimatedList"/>'s job (it splices a frozen ghost at
    /// the slot a departed id left), so this returns ONLY the live rows the container diffs against.</summary>
    private List<HudPinRow> BuildOrderedRows()
    {
        // Filter out rows whose destructive completion was sent and are now collapsing out. Their identity
        // leaving this set is exactly what makes the container collapse them (migrate-hud-onto-animated-list),
        // so they must not appear here or they'd flash back at full opacity as a live row.
        var pins = awaitingRemoval.Count == 0
            ? modSystem.MyPins
            : modSystem.MyPins.Where(p => !awaitingRemoval.Contains((p.OwnerDocId, p.TaskId))).ToList();

        // Order matches the Pin Tab exactly (sync-pinned-order-per-player D1): mirror OrderedPinsForDisplay —
        // under a sinking policy done pins sink below not-done via the shared Core rule
        // (ScribePinOrdering.ForDisplay); otherwise raw pin order. There is NO HUD-only ordering overlay: the
        // two surfaces render one and the same per-player order, agreeing by construction. The in-window
        // "hold in place" falls out for free (D3) — the HUD defers its send, so the server's LastKnownDone
        // stays false during the window and ForDisplay keeps the just-checked pin in the not-done group.
        bool sinkOrder = modSystem.MySettings.CompletionPolicy
            is ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;
        IReadOnlyList<ScribePinnedRef> ordered = sinkOrder ? ScribePinOrdering.ForDisplay(pins) : pins;

        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        return ordered.Take(max)
            .Select(p =>
            {
                // A pinned Tracker/Link carries its item code in the snapshot (Tracker→TargetItemCode,
                // Link→LinkTarget); resolve the icon + name here (where capi lives) so the row renders
                // item-shaped, mirroring the Pin Tab / read view (add-tracker-link-tasks 7.8). A plain
                // Task resolves to (null, null) and keeps its text.
                var (stack, name) = ResolveHudPinItem(p);
                return new HudPinRow(
                    p.OwnerDocId, p.TaskId, p.LastKnownText, DisplayedDone(p), SunkVisual(p),
                    FadingOut: IsFadingOut(p), Kind: p.Kind, LinkTarget: p.LinkTarget,
                    DisplayStack: stack, DisplayName: name,
                    // Live carried count if the HUD's own engine has one, else the snapshot (7.10).
                    TargetQuantity: p.TargetQuantity, CurrentQuantity: HudTrackerHave(p));
            })
            .ToList();
    }

    /// <summary>Resolve a pinned Tracker/Link's item icon + display name from its snapshot code, or
    /// <c>(null, null)</c> for a plain Task. Mirrors <c>ScribeDialogBase.ResolvePinItem</c> but reads the
    /// pin's own snapshot so a pinned item row renders even when its source document is unloaded
    /// (add-tracker-link-tasks 7.8).</summary>
    private (ItemStack? Stack, string? Name) ResolveHudPinItem(ScribePinnedRef p)
    {
        string? code = p.Kind switch
        {
            ScribeBlockKind.Tracker => p.TargetItemCode,
            ScribeBlockKind.Link => p.LinkTarget,
            _ => null,
        };
        if (code is null || capi is null) return (null, null);
        return ScribeItemRef.ResolveDisplay(capi.World, code, p.LinkLabel);
    }

    /// <inheritdoc />
    /// <remarks>Returns the ONE persistent-root <see cref="ScribeDialogBody"/> that owns the reconcilable
    /// HUD subtree (reconcile-animating-surfaces §4.1). <see cref="GuiBase"/> calls this once per open (and
    /// once per <see cref="GuiBase.ForceRebuild"/>); the body then persists, and every in-place update goes
    /// through <see cref="RebuildHudBody"/> (a reconciling <c>SetState</c>) which re-invokes
    /// <see cref="BuildHudTree"/> to re-read the HUD's live state. The self-open/close (§4.2) still rides
    /// <c>TryOpen</c>/<c>TryClose</c>, and the genuinely-new-tree cases keep <see cref="GuiBase.ForceRebuild"/>.</remarks>
    protected override Widget Build() => new ScribeDialogBody(bodyKey, BuildHudTree);

    /// <summary>Builds the HUD widget subtree from the current live state (pin set, timer, corruption,
    /// settings). Re-invoked on every reconcile via <see cref="ScribeDialogBody.BodyState.Build"/>, so it
    /// always reflects the latest state — this is the body of what used to be <see cref="Build"/> directly.</summary>
    private Widget BuildHudTree()
    {
        var shown = BuildOrderedRows();

        TraceHudRows(shown);

        // Indicative "+N more" only (design "+N more affordance"): pins beyond the visible cap. Rows
        // collapsing out (in awaitingRemoval) are already-completed removals, so — like the live-row filter
        // in BuildOrderedRows — they don't count toward the overflow tally.
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
            onDepartureSettled: OnHudDepartureSettled,
            collapsed: modSystem.MySettings.HudCollapsed,
            // Header/footer align toward the anchored edge (v1-playtest-fixes 5.3): left for a left-anchored
            // HUD, right otherwise. Same anchor classification the ApplyAnchor X-position switch uses.
            leftAligned: ScribePlayerSettings.NormalizeAnchor(modSystem.MySettings.HudAnchor).IsLeftAnchored(),
            rowWidth: ScribePlayerSettings.ClampHudRowWidth(modSystem.MySettings.HudRowWidth),
            rowFontSize: ScribeRowConstants.BaseHudFontSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            checkboxSize: ScribeRowConstants.BaseHudCheckboxSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            showIcons: modSystem.MySettings.HudShowIcons,
            onToggleRow: OnToggleRow,
            onOpenLink: OpenPinnedLink,
            onToggleCollapsed: ToggleCollapsed,
            onOpenSettings: modSystem.OpenSettings,
            timerData: timerSnapshot,
            onClearTimer: SendClearTimer,
            capiForTimer: capi);
    }

    // ---------------- DEBUG: blank-checkbox diagnostics ----------------

    /// <summary>DEBUG-only HUD row trace for the "blank checkbox / no text" bug
    /// (<c>hud-fade-text-stale-controller-bug</c>; recurred 2026-08-10 via a Read-view Sink completion —
    /// TESTING.md <c>f2d0a7e5</c>). Compiled out entirely in Release (<see cref="ConditionalAttribute"/>),
    /// so the call site in <see cref="BuildHudTree"/> vanishes too. Emits, per rendered row, everything
    /// needed to tell the two failure modes APART without guessing:
    /// <list type="bullet">
    /// <item><b>text=""</b> (empty <c>LastKnownText</c>) → the pin's cached text was genuinely cleared —
    /// a data/push problem, NOT a fade bug.</item>
    /// <item><b>text non-empty but the row still renders blank</b> → an OPACITY problem: cross-reference
    /// the flags — <c>Sunk</c> mutes the whole row to <see cref="SunkOpacity"/>; <c>FadingOut</c> drives the
    /// text-only fade (the stale <see cref="ScribeFadeText"/> controller signature). The blank row's flags
    /// say which mechanism is stuck. (A row past its window is gone from this trace — it left via
    /// <c>awaitingRemoval</c> and the container renders its collapsing ghost.)</item>
    /// </list>
    /// Also dumps the per-identity client state (<see cref="optimisticDone"/>, <see cref="pendingCompletions"/>,
    /// <see cref="awaitingRemoval"/>) so a row that a Read-view completion drove into
    /// a state the HUD's own toggle path never set up is visible directly. A row collapsing out is no longer
    /// in this set (it left via <c>awaitingRemoval</c> and the container renders its ghost), so its identity
    /// shows up under <c>awaitingRemoval</c> rather than as a traced row. Watch with
    /// <c>build/scribe-log.sh --client</c>, filter <c>[scribe-hud]</c>. Reproduce: complete a PINNED task from
    /// the Read view under Sink, then read the last frame's block.</summary>
    [Conditional("DEBUG")]
    private void TraceHudRows(List<HudPinRow> shown)
    {
        capi.Logger.Notification(
            "[scribe-hud] --- rebuild: {0} rows, policy={1}, pending={2}, awaitingRemoval={3} ---",
            shown.Count, modSystem.MySettings.CompletionPolicy, pendingCompletions.Count,
            awaitingRemoval.Count);

        for (int i = 0; i < shown.Count; i++)
        {
            var r = shown[i];
            var key = (r.DocId, r.TaskId);
            string textShown = r.Text ?? "<null>";
            bool blank = string.IsNullOrEmpty(r.Text);
            capi.Logger.Notification(
                "[scribe-hud]  [{0}] {1}task={2} done={3} sunk={4} fadeOut={5} | opt={6} pend={7} awaitRm={8} | text=\"{9}\"",
                i,
                blank ? "BLANK " : "",
                r.TaskId.ToString("N").Substring(0, 8),
                r.Done, r.Sunk, r.FadingOut,
                optimisticDone.TryGetValue(key, out var od) ? od.ToString() : "-",
                pendingCompletions.TryGetValue(key, out var pc) ? pc.Policy.ToString() : "-",
                awaitingRemoval.Contains(key),
                textShown);
        }
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
/// whether it is currently sunk (muted, ordered at the bottom), and whether it is fading out inside a
/// pending destructive-completion window (Unpin/Delete about to send — design D7). Carries no live pin
/// reference. There is no "departing" flag: once a row's window expires and its completion is sent, its
/// identity leaves the item set (via <c>awaitingRemoval</c>) and the <see cref="ScribeAnimatedList"/>
/// collapses it from a frozen ghost it captured — the HUD row itself is simply gone from this set
/// (migrate-hud-onto-animated-list).</summary>
internal readonly record struct HudPinRow(
    Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk, bool FadingOut,
    ScribeBlockKind Kind = ScribeBlockKind.Task, string? LinkTarget = null,
    // A pinned Tracker/Link carries its item's resolved icon + display name (snapshotted server-side and
    // resolved client-side in BuildOrderedRows), plus a Tracker's have/need counts, so the HUD row can
    // render the item icon + name instead of the (empty) task text (add-tracker-link-tasks 7.8).
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0)
{
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    /// <summary>A Tracker/Link renders as an item row (icon + name), not the editable-text shape.</summary>
    public bool IsItemKind => IsTracker || IsLink;
    /// <summary>The label to show: the resolved item name for a Tracker/Link, else the task text.</summary>
    public string Label => IsItemKind ? (DisplayName ?? Text) : Text;
}

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
    private readonly ScribeAnimationRegistry collapseRegistry;
    private readonly Action onDepartureSettled;
    private readonly bool collapsed;
    private readonly bool leftAligned;
    private readonly float rowWidth;
    private readonly float rowFontSize;
    private readonly float checkboxSize;
    /// <summary>Whether Tracker/Link rows render their item icon / guide-page book glyph on the HUD
    /// (<see cref="ScribePlayerSettings.HudShowIcons"/>); off gives a text-only HUD.</summary>
    private readonly bool showIcons;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    /// <summary>Open a pinned Link's Handbook page by its snapshotted link-target (add-tracker-link-tasks
    /// 5.5). Wired only for a Link row's label; null-safe target.</summary>
    private readonly Action<string?> onOpenLink;
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
        ScribeAnimationRegistry collapseRegistry,
        Action onDepartureSettled,
        bool collapsed,
        bool leftAligned,
        float rowWidth,
        float rowFontSize,
        float checkboxSize,
        bool showIcons,
        Action<Guid, Guid, bool> onToggleRow,
        Action<string?> onOpenLink,
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
        this.onDepartureSettled = onDepartureSettled;
        this.collapsed = collapsed;
        this.leftAligned = leftAligned;
        this.rowWidth = rowWidth;
        this.rowFontSize = rowFontSize;
        this.checkboxSize = checkboxSize;
        this.showIcons = showIcons;
        this.onToggleRow = onToggleRow;
        this.onOpenLink = onOpenLink;
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
            // Route the pin rows through ScribeAnimatedList (migrate-hud-onto-animated-list): the container
            // diffs the TaskId-keyed set frame-to-frame, so a pin that leaves the set (its destructive
            // completion sent → held in the HUD's awaitingRemoval → filtered out of `rows` upstream)
            // collapses out via the Immediate policy — the same one path the editor / Read / Pin Tab use.
            // The container abstracts MOTION + ORDER only; the HUD keeps its own chrome (header, "+N more",
            // timer) OUTSIDE the animated set, as siblings in the outer Column below.
            //
            // Each live row is the interactive HudPinRow widget (BuildRow), keyed by TaskId so its live State
            // — the ScribeFadeText countdown controller, the AnimatedOpacity sink tween — reconciles across
            // the HUD's rebuilds instead of remounting (which would snap those animations). Each departing row
            // supplies an explicit FROZEN ghost: the live row is unsafe to freeze in place (its checkbox stays
            // clickable mid-collapse), so BuildFrozenGhost snapshots it as a static, zero-opacity-text twin of
            // the HUD row shape — matching the already-faded row the window left behind, so the collapse just
            // closes empty space (design D2/Risks). animateEntry defaults true (D6): a newly-pinned row, or one
            // crossing into the capped window because another collapsed out, SLIDES in like every other surface.
            var items = rows
                .Select(r => new ScribeAnimatedListItem(
                    Id: r.TaskId,
                    Child: BuildRow(r, colors, glow),
                    Ghost: BuildFrozenGhost(r, colors, glow)))
                .ToList();

            children.Add(new ScribeAnimatedList(
                items: items,
                registry: collapseRegistry,
                policy: ScribeListRemovalPolicy.Immediate,
                onDepartureSettled: onDepartureSettled,
                // The layout wrapper is ours (D6 seam): the container hands us the ordered widget list (live
                // rows + any collapsing ghosts spliced at their old slots) and we lay them out as the HUD's
                // own column. Same 4px inter-row spacing the outer Column uses, so the gaps read identically
                // whether a row is live or collapsing. Rows are Max-width, so Stretch just guarantees full
                // width; an empty list renders nothing (the header/timer are outside this).
                layoutBuilder: laidOut => new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    children: laidOut.ToList())));

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

    /// <summary>One LIVE HUD row: [checkbox][text], the lectern read row minus chrome (no grip spacer, no
    /// pinned tint, no per-row buttons — design D1). Opacity animates by state: a destructive-pending row
    /// (Unpin/Delete in its window) fades its text LINEARLY from fully opaque to fully transparent over the
    /// window (a visible countdown — scribe-settings-followups 1.1); a sunk row mutes to
    /// <see cref="SunkOpacity"/>; else full. The checkbox stays fully opaque and clickable so undo is
    /// always available (design D7) — only the TEXT fades, via a nested AnimatedOpacity.
    ///
    /// <para>The row's height-collapse-on-removal is NOT wired here anymore: when the window expires and the
    /// completion is sent, the row's identity leaves the item set and <see cref="ScribeAnimatedList"/>
    /// collapses a frozen ghost (<see cref="BuildFrozenGhost"/>) in its place (migrate-hud-onto-animated-list).
    /// This method only ever builds the interactive, present-in-the-set row.</para></summary>
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

        // Corrupt the row text (hud-temporal-storm-corruption 3.4). A per-row seed offset (derived from the
        // task identity) keeps two rows from injecting the identical mark pattern, while the whole HUD still
        // re-scrambles together on the host's cadence. No-op when no trigger is active (strength 0). Unused
        // by the item-kind branch (it corrupts the resolved name in BuildHudItemContent instead).
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        var checkbox = new Checkbox(
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
            });

        Widget rowBody;
        if (row.IsItemKind)
        {
            // A pinned Tracker/Link renders the referenced item's icon + name (+ a have/need counter on the
            // LEFT for a Tracker) instead of the editable-text shape, mirroring the Pin Tab / read view
            // (add-tracker-link-tasks 7.8). The name is a Handbook hyperlink; the counter is not.
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    checkbox,
                    new Expanded(child: BuildHudItemContent(row, textStyle, interactive: true)),
                });
        }
        else
        {
            // A plain Task's text fades LINEARLY toward full transparency over the window for a
            // destructive-pending row (a countdown preview of removal — scribe-settings-followups 1.1).
            // ScribeFadeText owns its own ticker (see its remarks): a stock implicitly-animated widget would
            // snap to its target across the HUD's rebuilds, so this self-ticks instead. A non-fading row is
            // fully opaque. Only the text fades — the checkbox stays clickable for undo.
            Widget text = new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);

            // A pinned Link's label is a Handbook hyperlink (add-tracker-link-tasks 5.5): tapping it opens
            // the referenced page and NEVER toggles the row's checkbox (opening the page is separate from
            // completion — design D3c). (Handled here for the item-kind branch above; a plain Task's text is
            // never a hyperlink.)
            rowBody = new Row(
                spacing: 6,
                // Fill the fixed-width column (task 4.5) so the Expanded text wraps within HudRowWidth minus
                // the checkbox, rather than the row sizing to its (unwrapped) content and overflowing.
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Start,
                children: new Widget[]
                {
                    checkbox,
                    // Expanded so the (SoftWrap) text wraps within the remaining fixed width.
                    new Expanded(child: text),
                });
        }

        // A sunk row mutes the WHOLE row (checkbox + text) to SunkOpacity, faded rather than snapped so a
        // completing task reads as a gentle settle. The ValueKey stabilizes row identity across rebuilds
        // for the animation. The container wraps this in its own entry/collapse animation (keyed by the same
        // TaskId) as needed; this returns just the styled interactive row.
        return new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));
    }

    /// <summary>The content of a pinned Tracker/Link HUD row: the referenced item's icon + name, with a
    /// have/need counter on the LEFT for a Tracker (add-tracker-link-tasks 7.8). Mirrors the Pin Tab / read
    /// view (<c>ScribePinRowState.BuildItemContent</c>) so all surfaces render item rows identically —
    /// counter-left, name as a Handbook hyperlink — but in the HUD's theme-independent near-white/glow ink
    /// (the passed <paramref name="textStyle"/>) rather than the parchment <c>ColorScheme</c>, matching the
    /// rest of the HUD. A non-interactive ghost passes <paramref name="interactive"/> false (no hyperlink).</summary>
    private Widget BuildHudItemContent(HudPinRow row, TextStyle textStyle, bool interactive)
    {
        // The item's 3D icon — theme-independent, so it renders identically here and on every other surface.
        // The book glyph keeps the HUD's near-white ink (not the parchment Primary the notebook uses — 7.11d);
        // both the item icon (grown) and the book glyph (shrunk) render row-height-neutral (7.11e/7.11f).
        // Icons are optional on the HUD (client-local preference): when HudShowIcons is off, Tracker/Link
        // rows drop the item icon / book glyph and read as text-only, for a leaner HUD.
        float iconSize = rowFontSize * 1.4f;
        float lineHeight = ScribeRowControlNudge.TextLineHeight(rowFontSize);
        Widget? icon = showIcons
            ? ScribeLinkIcon.Build(row.DisplayStack, row.LinkTarget, iconSize, textStyle.Color, lineHeight)
            : (Widget?)null;

        // Name (corrupted like every HUD string). A Handbook hyperlink when interactive: tapping opens the
        // item's page and NEVER toggles the checkbox (design D3c) — same open path as the live Link row. The
        // Tracker's code isn't carried on the row, so derive it from the resolved stack (a Link uses its
        // snapshotted LinkTarget directly); either resolves the same page the Pin Tab / read view open.
        string nameText = Corrupt(row.Label, seedOffset: row.TaskId.GetHashCode());
        Widget name = new Text(nameText, textStyle);
        if (interactive)
        {
            string? code = row.IsLink ? row.LinkTarget : row.DisplayStack?.Collectible?.Code?.ToString();
            name = new GestureDetector(
                onPress: e => { e.Handled = true; onOpenLink(code); },
                child: name);
        }

        var children = new List<Widget>();
        if (row.IsTracker)
        {
            // A "have / need" counter on the LEFT (future Crafting tasks inherit this). Emphasis INVERTED
            // (7.11g): an in-progress count reads STRONG (the row's bright near-white, bold — still collecting);
            // a satisfied count reads FADED (muted grey) with a faint strikethrough over the number (7.11h).
            // Shared helper so the HUD matches the read/Pin counters (in the HUD's near-white ink, and corrupted).
            bool satisfied = row.CurrentQuantity >= row.TargetQuantity;
            children.Add(ScribeTrackerCounterText.Build(
                row.CurrentQuantity, row.TargetQuantity, satisfied,
                strongColor: textStyle.Color, mutedColor: new Vector4(0.70f, 0.70f, 0.70f, 1f),
                lineHeight: lineHeight, baseStyle: textStyle,
                corrupt: s => Corrupt(s, seedOffset: 404)));
        }
        if (icon != null) children.Add(icon);
        // Expanded so the (SoftWrap) name wraps within the remaining fixed width, matching the Task row.
        children.Add(new Expanded(child: name));

        return new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: children);
    }

    /// <summary>A static, non-interactive snapshot of a HUD row for <see cref="ScribeAnimatedList"/> to
    /// collapse when the row departs (migrate-hud-onto-animated-list). The live row is unsafe to freeze in
    /// place — its checkbox would stay clickable mid-collapse and its per-identity client state is gone once
    /// the pin leaves the set — so this renders a frozen twin of the [checkbox][text] shape with the text at
    /// ZERO opacity. That matches the already-faded row the destructive-completion window left behind (its
    /// <see cref="ScribeFadeText"/> ramped the text to ~0 over the window), so the collapse closes empty space
    /// with no flash-back of the text (design D2/Risks). No <see cref="AnimatedOpacity"/> and no ValueKey: the
    /// container owns this ghost's lifetime and keys it by the row id.</summary>
    private Widget BuildFrozenGhost(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = row.Sunk
                ? new Vector4(0.70f, 0.70f, 0.70f, 1f)
                : new Vector4(0.93f, 0.93f, 0.93f, 1f),
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // Text at fixed zero opacity — the window already faded it out; a departing row collapses an empty
        // space. Corrupt with the same per-row seed so the (invisible) text still matches the live row's
        // metrics exactly as it collapses.
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        return new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: new Widget[]
            {
                // A frozen (disabled) checkbox mirroring the row's last done-state — no onChanged, so it
                // can't be toggled while it collapses. Same grayscale HUD style as the live row's checkbox.
                new Checkbox(
                    value: row.Done,
                    onChanged: null,
                    size: checkboxSize,
                    style: new CheckboxStyle
                    {
                        CheckColor = new Vector4(0.867f, 0.867f, 0.867f, 1f),
                        BackgroundColor = new Vector4(0.28f, 0.28f, 0.28f, 0.75f),
                        BorderColor = new Vector4(0.8f, 0.8f, 0.8f, 0.75f),
                        BorderThickness = 1.5f,
                        CornerRadius = 2f,
                        LabelStyle = textStyle,
                    }),
                // For an item-kind row, freeze the SAME item content (icon + name + Tracker counter) at zero
                // opacity so the collapsing ghost matches the live row's metrics exactly; else the plain text.
                // Non-interactive (no hyperlink) — the ghost is inert while it collapses.
                row.IsItemKind
                    ? new Expanded(child: new Opacity(0f, BuildHudItemContent(row, textStyle, interactive: false)))
                    : new Expanded(child: new Opacity(0f, new Text(rowText, textStyle))),
            });
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
/// <para><b>Why not <see cref="AnimatedOpacity"/>?</b> Originally the HUD's only rebuild path was
/// <see cref="GuiBase.ForceRebuild"/>, which UNMOUNTS and recreates the whole widget tree rather than
/// reconciling it. A stock implicitly-animated widget only animates across a reconciling <c>UpdateWidget</c>
/// (retarget tween → <c>Forward()</c>); recreated fresh, its tween inits <c>Begin=End=target</c> and
/// evaluates to the target instantly — which is exactly the "snap straight to 0" bug. This widget instead
/// owns its controller and ticks itself, so it ramps correctly regardless of the host's rebuild strategy.</para>
///
/// <para><b>Reconcile-safety (reconcile-animating-surfaces §4.1).</b> Now that the HUD rebuilds via a
/// reconciling <c>SetState</c> (not <c>ForceRebuild</c>), a row element is REUSED across a rebuild rather
/// than remounted — so <see cref="InitState"/> does NOT re-run when a row's <see cref="Fading"/> flips
/// <c>false→true</c> (the checkbox-click → destructive-pending transition). Starting the fade only in
/// <c>InitState</c> would therefore silently never fire on a reused row. So the fade is (re)started from a
/// single <c>EnsureFading</c> helper called from BOTH <c>InitState</c> (fresh mount — e.g. a row that
/// mounts already-pending) AND <see cref="UpdateWidget"/> (reused element whose prop just flipped). Idempotent:
/// it no-ops if a controller is already running, so a plain repaint doesn't restart the ramp.</para>
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
        // Fresh mount: if this row mounts already in the fading state (e.g. reconcile added a new row that
        // is already destructive-pending), start the fade now.
        SyncFadeController();
    }

    public override void UpdateWidget(ScribeFadeText oldWidget)
    {
        base.UpdateWidget(oldWidget);
        // Reused element (reconcile): InitState did NOT re-run, so a Fading flip must be reconciled here —
        // false→true (re)starts the fade, true→false (an undo within the window) clears it so the text
        // reappears. Idempotent in both directions.
        SyncFadeController();
    }

    /// <summary>Reconcile the self-owned fade controller with the widget's current <see cref="ScribeFadeText.Fading"/>
    /// flag, in BOTH directions. Called from <see cref="InitState"/> (mount) and <see cref="UpdateWidget"/>
    /// (reconcile reuse):
    /// <list type="bullet">
    /// <item><b>Fading</b>: start the ramp if one isn't already running (idempotent — a plain repaint won't
    /// restart it).</item>
    /// <item><b>Not fading</b>: DISPOSE any controller so <see cref="Build"/> returns to full opacity. This is
    /// the undo path (reconcile-animating-surfaces §4.5 HUD regression): a destructive-pending row whose window
    /// the player cancels late has a nearly-complete fade controller (<c>Value ≈ 1</c>). Because the reconcile
    /// path REUSES the element rather than remounting it, without this the stale controller would keep the text
    /// at ~0 opacity forever — a "checkbox with no text". (The old <see cref="GuiBase.ForceRebuild"/> path hid
    /// this by remounting to a fresh <c>controller == null</c> state; reconcile made element-reuse the norm.)</item>
    /// </list></summary>
    private void SyncFadeController()
    {
        if (Widget.Fading)
        {
            if (controller != null) return; // already fading

            // Own ticker: ramp 0→1 over the window; opacity is 1 − value so the text fades 1→0. Repaint each
            // tick via MarkNeedsBuild (SetState) — the reconciling rebuild path, so this animates itself
            // regardless of the host's rebuild strategy.
            controller = new AnimationController(TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
            controller.OnValueChanged += _ => Element.MarkNeedsBuild();
            controller.Forward();
        }
        else
        {
            // Undo / un-fade: drop the (possibly completed) fade so opacity snaps back to 1 and the text is
            // visible again. Safe to dispose here — UpdateWidget/InitState run during the build phase, not
            // from inside the ticker callback.
            controller?.Dispose();
            controller = null;
        }
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
