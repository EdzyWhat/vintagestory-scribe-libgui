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
using Gui.Widgets.Input;         // Checkbox, GestureDetector, Dropdown, DropdownItem
using Gui.Widgets.Inventory;     // ItemStackDisplay (Tracker/Link item icon)
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
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
/// The on-screen pinned-task HUD (LibGUI, modid <c>gui</c>): this player's pins from
/// <see cref="ScribeModSystem.MyPins"/> as an ordered checklist anchored to a screen corner, legible via
/// a text glow rather than a background plate.
///
/// <para>A non-modal HUD: it never takes keyboard focus, Escape never closes it, and it renders behind
/// dialogs. Mouse events stay on so row checkboxes are clickable. The window is a content-sized,
/// non-draggable, non-opaque overlay re-anchored each frame.</para>
///
/// <para>Opens itself off <see cref="ScribeModSystem.MyPinsChanged"/> when there is something to show and
/// closes when there is not. <see cref="ScribePlayerSettings.HudCollapsed"/> is client-local and minimizes
/// to the header. Owns its event subscriptions and tick listener until <see cref="Dispose"/>.</para>
///
/// <para>Completion: after an undo window (<see cref="PinHudWaitMs"/>) the HUD sends
/// <see cref="ScribeCompleteTaskMessage"/> with the completion policy; the server's re-push rebuilds.</para>
/// </summary>
public sealed class HudScribePins : GuiBase
{
    /// <summary>Logical-pixel margin between the HUD content and the anchored screen edge(s).</summary>
    private const float CornerMargin = 8f;

    /// <summary>Fallback leftward nudge for the <see cref="ScribeHudAnchor.TopRight"/> anchor when the live
    /// minimap dialog isn't resolvable (see <see cref="ResolveTopRightMinimapClearanceX"/>). Clears the 250×250
    /// vanilla minimap only at the reference GUI Scale.</summary>
    private const float DefaultTopRightMinimapClearanceX = 260f;

    /// <summary>How long a just-checked completion is held before it is sent; unchecking within it is a true
    /// undo. Client UI state only.</summary>
    private const double PinHudWaitMs = 1500;

    /// <summary>Safety re-read cadence; expires pin windows (firing deferred sends). The authoritative refresh
    /// is event-driven.</summary>
    private const int TickIntervalMs = 250;

    /// <summary>Hard render cap, independent of the (already clamped) preference.</summary>
    private const int MaxRenderedRows = ScribePlayerSettings.MaxHudMaxRows;

    private readonly ScribeModSystem modSystem;

    /// <summary>Client-local monotonic clock accumulated from tick deltas; stamps and expires
    /// <see cref="pendingCompletions"/>.</summary>
    private double elapsedMs;

    private long tickListenerId;

    /// <summary>Optimistic done-state per pin, applied on click; cleared once the server push agrees
    /// (<see cref="OnMyPinsChanged"/>).</summary>
    private readonly Dictionary<(Guid, Guid), bool> optimisticDone = new();

    /// <summary>Completions checked but not yet sent: the policy and the <see cref="elapsedMs"/> expiry. The pin
    /// is held in place while pending; unchecking removes the entry (true undo); <see cref="OnTick"/> sends on
    /// expiry.</summary>
    private readonly Dictionary<(Guid, Guid), PendingCompletion> pendingCompletions = new();

    /// <summary>A completion held on the HUD during its pin window: the player's chosen policy
    /// at click time and the clock value at which the deferred send fires.</summary>
    private readonly record struct PendingCompletion(ScribeCompletionPolicy Policy, double ExpiryMs);

    /// <summary>Pins whose destructive completion (Unpin/Delete/UnpinSink) was SENT and await the server's
    /// removal push. They are filtered OUT of the items handed to <see cref="ScribeAnimatedList"/>: an identity
    /// leaving the item set triggers the container's <c>Immediate</c> collapse of a frozen ghost, and without the
    /// filter the pin would flash back at full opacity. Reconciled on each push
    /// (<see cref="ReconcileAwaitingRemoval"/>).</summary>
    private readonly HashSet<(Guid, Guid)> awaitingRemoval = new();

    /// <summary>Quest Accept banner's 2+-candidate picker state (session state; the content widget is
    /// stateless). Reset when the pending prompt list changes.</summary>
    private bool questAcceptPickerOpen;
    private int questAcceptSelectedIndex;

    /// <summary>Per-pinned-Tracker LIVE "have" count from the viewer's carried inventory, recomputed on the HUD
    /// tick so counters are live with no dialog open. Keyed by <c>(OwnerDocId, TaskId)</c>; overrides the
    /// snapshot's <see cref="ScribePinnedRef.CurrentQuantity"/> for display (<see cref="HudTrackerHave"/>) and is
    /// the rising-edge baseline for auto-completion. Pruned to the current pinned-Tracker set.</summary>
    private readonly Dictionary<(Guid, Guid), int> liveTrackerCounts = new();

    /// <summary>Resolved ingredient per Tracker target code (registry lookups are costly); null caches
    /// "doesn't resolve, counts 0". Never invalidated: resolution is stable per session.</summary>
    private readonly Dictionary<string, CraftingRecipeIngredient?> hudTrackerIngredientCache = new();

    /// <summary>Host-owned collapse/entry controllers keyed by row identity so motion RESUMES across HUD
    /// rebuilds. The HUD only reads <c>AnyAnimating</c> (to keep hover fresh) and disposes it.</summary>
    private readonly ScribeAnimationRegistry collapseRegistry = new();

    /// <summary>Keeps the hover refresh running a few frames past the last animating frame so it lands after
    /// the rebuild's layout. See <see cref="ScribeHoverRefreshLatch"/>.</summary>
    private readonly ScribeHoverRefreshLatch hoverRefreshLatch = new();

    /// <summary>Identity of the persistent-root <see cref="ScribeDialogBody"/>. Allocated once (never in
    /// <see cref="Build"/>) so <see cref="RebuildHudBody"/> can reconcile the row list in place, reusing each
    /// row's State (fade controller, sink tween, gear hover) instead of remounting via
    /// <see cref="GuiBase.ForceRebuild"/>, which would snap animations and drop hover.</summary>
    private readonly GlobalKey bodyKey = new();

    /// <summary>Reconciles the HUD content in place (no-op when closed or unmounted), reusing rows keyed by
    /// <c>ValueKey&lt;Guid&gt;(TaskId)</c> so their animation State survives. Also arms the hover-refresh latch:
    /// a reconcile leaves <see cref="GuiBase.RootElement"/> unchanged, so
    /// <see cref="ScribeHoverRefreshLatch.ArmIfRebuilt"/> can't catch a reorder under a stationary cursor.</summary>
    private void RebuildHudBody()
    {
        if (!IsOpened()) return;
        bodyKey.CurrentState<ScribeDialogBody.BodyState>()?.Rebuild();
        hoverRefreshLatch.Arm();
    }

    /// <summary>A departing row's collapse finished. Re-arms the hover refresh because rows below slid up under
    /// a stationary cursor; the per-frame <c>AnyAnimating</c> arming covers the animating frames, this covers the
    /// settling frame.</summary>
    private void OnHudDepartureSettled() => hoverRefreshLatch.Arm();

    private record struct AnchorInputs(
        float ScreenW, float ScreenH, ScribeHudAnchor Anchor, float OffX, float OffY, bool MinimapOn,
        float MinimapClearanceX, Vector2 WindowSize);
    private AnchorInputs? _lastAnchorInputs;

    /// <summary>Interpolated remaining seconds, smooth between 1 s server pushes; resynced from
    /// <see cref="ScribeModSystem.MyTimer"/> on each push.</summary>
    private double _timerLocalRemaining;
    private bool _timerResync = true;

    /// <summary>Last timer status reacted to. <see cref="OnMyTimerChanged"/> rebuilds only on a status
    /// transition; the per-second countdown repaint belongs to <see cref="ScribeModSystem.TimerDisplayTick"/>.</summary>
    private Scribe.Core.TimerStatus _lastTimerStatus = Scribe.Core.TimerStatus.Idle;

    /// <summary>Seconds the current timer has been Fired. The server doesn't time the auto-disappear (it depends
    /// on the client-local <see cref="ScribePlayerSettings.TimerAutoDisappear"/>). Seeded from
    /// <see cref="TimerStore.FiredElapsedSeconds"/> on a Fired push so a restored timer resumes, then advanced on
    /// the 1 Hz tick.</summary>
    private double _firedElapsedLocal;

    /// <summary>Ensures only one auto-clear is sent per fired timer; reset on each fresh Fired state.</summary>
    private bool _firedAutoClearSent;

    // ---------------- Temporal-storm corruption ----------------

    /// <summary>Base strengths per storm tier, copied from vanilla's <c>stormGlitchStrength</c> bases in
    /// <see cref="SystemTemporalStability"/>. Not scaled.</summary>
    private const double StormLightStrength = 0.53;
    private const double StormMediumStrength = 0.67;
    private const double StormHeavyStrength = 0.90;

    /// <summary>Low-stability ramp: nothing at/above Upper, linear to full at/below Lower.</summary>
    private const double StabilityRampUpper = 0.50;
    private const double StabilityRampLower = 0.10;

    /// <summary>Randomized re-scramble interval range (ms) while a trigger is active.</summary>
    private const long RescrambleMinMs = 0;
    private const long RescrambleMaxMs = 5000;

    /// <summary>Corruptor seed, advanced on the re-scramble cadence; fixed within a build so the whole HUD is
    /// coherent.</summary>
    private int _corruptionSeed;

    /// <summary>World elapsed ms at which the next re-scramble is due.</summary>
    private long _nextRescrambleMs;

    /// <summary>Edge-detect state so a trigger or storm transition rebuilds immediately.</summary>
    private bool _corruptionWasActive;
    private bool _stormWasActiveHud;

    public HudScribePins(ICoreClientAPI capi, ScribeModSystem modSystem) : base(capi)
    {
        this.modSystem = modSystem;

        // Subscribed for the object's lifetime (not per-open) so a pin arriving while closed can open the HUD; released in Dispose().
        modSystem.MyPinsChanged += OnMyPinsChanged;
        modSystem.MyTimerChanged += OnMyTimerChanged;
        modSystem.TimerDisplayTick += OnTimerDisplayTick;
        modSystem.QuestPromptsChanged += OnQuestPromptsChanged;
        tickListenerId = capi.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
    }

    /// <summary>Repaints the countdown on the shared 1 Hz tick so it matches the Notebook Timer tab; the 250 ms
    /// OnTick only interpolates _timerLocalRemaining.</summary>
    private void OnTimerDisplayTick()
    {
        var timer = modSystem.MyTimer;

        // Client-driven auto-disappear while Fired. The preference is re-read each tick so turning it off
        // mid-flash takes effect live.
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

    /// <summary>Escape falls through to the game; a HUD never closes on it.</summary>
    public override bool OnEscapePressed() => false;

    public override bool ShouldReceiveRenderEvents() => IsOpened();

    /// <summary>Keyboard stays with the game; mouse stays on so checkboxes are clickable and clicks outside the
    /// content fall through.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;

    /// <inheritdoc />
    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Size null shrink-wraps to content (the anchor is applied post-layout). A concrete Position suppresses
        // the base's first-frame auto-centering; OpaqueHitTest off lets clicks outside the content reach the world.
        Size = null,
        Position = Vector2.Zero,
        Draggable = false,
        Resizable = false,
        OpaqueHitTest = false,
        Clip = false,
    };

    /// <summary>Re-anchors right after shrink-wrap layout settles so a row-count change doesn't mis-position
    /// the window for a frame.</summary>
    protected override void OnShrinkWrapLayoutCompleted() => ApplyAnchor();

    /// <inheritdoc />
    public override void OnRenderGUI(float deltaTime)
    {
        // LibGUI recomputes hover only on real mouse motion, so arm the latch to re-dispatch a synthetic
        // pointer-move for a few frames when: (1) a collapse is animating; (2) a reconcile reorders rows (armed in
        // RebuildHudBody, since RootElement is unchanged); (3) a remount swaps RootElement (self-open).
        if (collapseRegistry.AnyAnimating) hoverRefreshLatch.Arm();
        hoverRefreshLatch.ArmIfRebuilt(RootElement);
        if (hoverRefreshLatch.Tick()) RefreshHoverAtCursor();

        // Re-anchor every frame (a window resize needn't re-run layout). WindowSize is part of AnchorInputs so the
        // position self-corrects once real content lays out.
        ApplyAnchor();
        base.OnRenderGUI(deltaTime);
    }

    /// <summary>Re-dispatches a synthetic pointer-move at the cursor so LibGUI re-runs hit-testing; see
    /// <see cref="ScribeHoverRefresh"/> for the coordinate conversion.</summary>
    private void RefreshHoverAtCursor()
    {
        if (RootElement?.RenderObject == null) return;
        var local = ScribeHoverRefresh.ToWindowLocal(
            capi.Input.MouseX, capi.Input.MouseY, GetUiScale(), WindowPos);
        EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y));
    }

    /// <summary>Minimap clearance width for the <see cref="ScribeHudAnchor.TopRight"/> anchor, read from the
    /// open vanilla minimap HUD dialog's bounds, converted to this HUD's GUIScale-normalized units, plus the
    /// vanilla dialog-to-screen padding. OuterWidth is raw pixels, divided by scale to match how screenW is
    /// derived from FrameWidth. Falls back to <see cref="DefaultTopRightMinimapClearanceX"/> when the
    /// dialog isn't resolvable (not composed yet, or the full map is open).</summary>
    private float ResolveTopRightMinimapClearanceX(float scale)
    {
        var minimapBounds = capi.Gui.OpenedGuis
            .OfType<GuiDialogWorldMap>()
            .FirstOrDefault(dlg => dlg.DialogType == EnumDialogType.HUD)
            ?.SingleComposer?.Bounds;
        if (minimapBounds == null)
        {
            TraceMinimapClearance(scale, null, DefaultTopRightMinimapClearanceX);
            return DefaultTopRightMinimapClearanceX;
        }

        float result = (float)(minimapBounds.OuterWidth / scale) + (float)GuiStyle.DialogToScreenPadding;
        TraceMinimapClearance(scale, minimapBounds, result);
        return result;
    }

    private float? _lastTracedMinimapClearance;

    /// <summary>Diagnostic-only: logs the inputs behind <see cref="ResolveTopRightMinimapClearanceX"/> when the
    /// result changes. Filter <c>[scribe-hud-anchor]</c> in <c>build/scribe-log.sh --client</c>.</summary>
    [Conditional("DEBUG")]
    private void TraceMinimapClearance(float scale, ElementBounds? minimapBounds, float result)
    {
        if (_lastTracedMinimapClearance == result) return;
        _lastTracedMinimapClearance = result;

        if (minimapBounds == null)
        {
            capi.Logger.Notification(
                "[scribe-hud-anchor] scale={0} minimap=<unresolved> -> fallback clearanceX={1}", scale, result);
            return;
        }

        capi.Logger.Notification(
            "[scribe-hud-anchor] scale={0} minimap.OuterWidth={1} minimap.absX={2} minimap.absY={3} " +
            "FrameWidth={4} -> clearanceX={5}",
            scale, minimapBounds.OuterWidth, minimapBounds.absX, minimapBounds.absY, capi.Render.FrameWidth, result);
    }

    /// <summary>
    /// Positions the shrink-wrapped window per <see cref="ScribePlayerSettings.HudAnchor"/> plus the
    /// <see cref="ScribePlayerSettings.HudOffsetX"/>/<see cref="ScribePlayerSettings.HudOffsetY"/> nudge.
    /// A positive offset always moves the window toward screen center from the anchored edge. The
    /// <see cref="ScribeHudAnchor.TopRight"/> anchor adds <see cref="ResolveTopRightMinimapClearanceX"/> so the
    /// HUD clears the minimap by default.
    /// </summary>
    private void ApplyAnchor()
    {
        float scale = RuntimeEnv.GUIScale;
        float screenW = capi.Render.FrameWidth / scale;
        float screenH = capi.Render.FrameHeight / scale;

        var settings = modSystem.MySettings;
        var anchor = ScribePlayerSettings.NormalizeAnchor(settings.HudAnchor);

        bool minimapOn = !capi.Settings.Bool.Exists("showMinimapHud") || capi.Settings.Bool["showMinimapHud"];
        float minimapClearanceX = anchor == ScribeHudAnchor.TopRight && minimapOn
            ? ResolveTopRightMinimapClearanceX(scale)
            : 0f;
        var key = new AnchorInputs(
            screenW, screenH, anchor, minimapClearanceX + settings.HudOffsetX, settings.HudOffsetY, minimapOn,
            minimapClearanceX, WindowSize);
        if (_lastAnchorInputs == key) return;
        _lastAnchorInputs = key;

        // Offsets are relative to the anchor's pre-baked offset (only TopRight has one: the minimap clearance on
        // X). The clearance applies only when the minimap is on; "showMinimapHud" is written by GuiDialogWorldMap,
        // so absent means never toggled, i.e. on by default.
        float offX = key.OffX;
        float offY = key.OffY;

        // Left/top edges add the offset and right/bottom edges subtract it (both toward center); center anchors shift from the midpoint.
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
    /// A pin-set push arrived. Drops confirmed optimistic overrides, then opens the HUD when there is something
    /// to show, closes it at zero, otherwise rebuilds in place.
    /// </summary>
    private void OnMyPinsChanged()
    {
        _lastAnchorInputs = null;
        ReconcileOptimisticWithServer();
        // Drop ids the server removed (ghost collapsing or gone) or the player re-pinned (handed back to the container as a live row).
        ReconcileAwaitingRemoval();

        var rawTimer = modSystem.MyTimer;
        bool hasTimer = rawTimer is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired };
        bool hasPins = modSystem.MyPins.Count > 0;
        bool hasPrompt = HasBannerEligibleQuestPrompt;
        if ((hasPins || hasTimer || hasPrompt) && !IsOpened())
        {
            TryOpen(); // withFocus defaults false via base; a HUD never steals focus anyway
        }
        else if (!hasPins && !hasTimer && !hasPrompt && IsOpened())
        {
            TryClose();
        }
        else if (IsOpened())
        {
            RebuildHudBody();
        }

        // Refresh live Tracker counts and rebuild once more if a displayed count moved, so a new Tracker pin shows its count without waiting for the next tick.
        if (IsOpened() && RecomputeHudTrackers()) RebuildHudBody();
    }

    private void OnMyTimerChanged()
    {
        _timerResync = true;
        var timer = modSystem.MyTimer;
        var status = timer?.Status ?? Scribe.Core.TimerStatus.Idle;
        bool statusChanged = status != _lastTimerStatus;
        _lastTimerStatus = status;

        // A Fired state reseeds the auto-disappear accumulator from the pushed elapsed (so the window resumes, not
        // restarts) and re-arms the one-shot clear; any other state resets it.
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
        if (hasTimer && !IsOpened() && modSystem.MyPins.Count == 0 && !HasBannerEligibleQuestPrompt)
            TryOpen();
        else if (!hasTimer && !modSystem.MyPins.Any() && !HasBannerEligibleQuestPrompt && IsOpened())
            TryClose();
        // Rebuild only on a genuine status transition; TimerDisplayTick owns the per-second repaint.
        else if (statusChanged && IsOpened())
            RebuildHudBody();
    }

    /// <summary>A Quest prompt was queued or resolved: self-open/close as for pins/timer (a prompt alone opens
    /// the HUD) and rebuild. Uses <see cref="HasBannerEligibleQuestPrompt"/> so a PromptPopup-only accept prompt
    /// (shown by the center modal) doesn't open an empty HUD.</summary>
    private void OnQuestPromptsChanged()
    {
        questAcceptPickerOpen = false;
        questAcceptSelectedIndex = 0;
        bool hasPrompt = HasBannerEligibleQuestPrompt;
        var rawTimer = modSystem.MyTimer;
        bool hasTimer = rawTimer is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired };
        bool hasPins = modSystem.MyPins.Count > 0;

        if (hasPrompt && !IsOpened())
            TryOpen();
        else if (!hasPrompt && !hasPins && !hasTimer && IsOpened())
            TryClose();
        else if (IsOpened())
            RebuildHudBody();
    }

    /// <summary>Whether a pending prompt renders as the HUD banner: completion-prompts always; accept-prompts
    /// unless the policy is <see cref="Scribe.Core.ScribeQuestAcceptPolicy.PromptPopup"/> (then
    /// <see cref="GuiDialogScribeQuestPrompt"/> shows it). Decided at render time from the live policy.</summary>
    private bool BannerEligible(ScribeQuestPrompt p)
        => p.IsCompletion || modSystem.MySettings.QuestAcceptPolicy != Scribe.Core.ScribeQuestAcceptPolicy.PromptPopup;

    /// <summary>Whether any pending prompt is banner-eligible; drives self-open/close.</summary>
    private bool HasBannerEligibleQuestPrompt => modSystem.PendingQuestPrompts.Any(BannerEligible);

    /// <summary>Banner-eligible pending prompts, oldest first, handed to <see cref="HudPinsContent"/>.</summary>
    private List<ScribeQuestPrompt> BannerEligibleQuestPrompts()
        => modSystem.PendingQuestPrompts.Where(BannerEligible).ToList();

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

    /// <summary>Drops ids from <see cref="awaitingRemoval"/> once the server confirms the pin GONE (absent from
    /// <see cref="ScribeModSystem.MyPins"/>); the collapse itself belongs to <see cref="ScribeAnimatedList"/>.
    ///
    /// <para>Dropping only on confirmed removal (not when <see cref="IsFadingOut"/> turns false) is load-bearing:
    /// with several removals pending, an unrelated push must not un-suppress a still-present sent pin and flash it
    /// back. A destructive completion always yields a removal push before any re-pin, so re-pins need no special
    /// case.</para></summary>
    private void ReconcileAwaitingRemoval()
    {
        if (awaitingRemoval.Count == 0) return;
        var live = modSystem.MyPins.Select(p => (p.OwnerDocId, p.TaskId)).ToHashSet();
        awaitingRemoval.RemoveWhere(k => !live.Contains(k));
    }

    /// <summary>Low-frequency tick: sends completions whose window elapsed and rebuilds only if one did (the
    /// server re-push then rebuilds with the authoritative result).</summary>
    private void OnTick(float dt)
    {
        elapsedMs += dt * 1000.0;

        // Temporal-storm corruption: advance and rebuild on transitions or the 0–5 s cadence; cheap when idle.
        bool corruptionRebuilt = TickCorruption();

        // Interpolate the countdown between server pushes; InGame timers drain at the world time rate to match the server.
        var timer = modSystem.MyTimer;
        if (timer?.Status == Scribe.Core.TimerStatus.Running)
        {
            if (_timerResync) { _timerLocalRemaining = timer.RemainingSeconds; _timerResync = false; }
            else
            {
                double rate = timer.Mode == Scribe.Core.TimerMode.InGame ? ScribeTimeRate.InGamePerReal(capi) : 1.0;
                _timerLocalRemaining = Math.Max(0, _timerLocalRemaining - dt * rate);
            }
            // No repaint here: the shared 1 Hz TimerDisplayTick repaints so the HUD and Notebook Timer tab stay in step.
        }

        // Live Tracker counts; rebuild if a displayed count moved and the corruption tick didn't already rebuild.
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
                // Sink needs no bookkeeping: the server flips LastKnownDone and ForDisplay sinks the pin.
                // A destructive completion removes the pin server-side; awaitingRemoval filters it out of the animated
                // list's items so the container collapses a frozen ghost (its text already faded, so no flash).
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

    // ---------------- Live Tracker count engine ----------------

    /// <summary>Recomputes every pinned Tracker's live "have" count from carried inventory so counters are live
    /// with no dialog open. Mirrors <see cref="ScribeDialogBase"/>'s <c>RecomputeTrackers</c>, scoped to the pin
    /// set and run on the existing tick (no <c>SlotModified</c> subscription lifecycle).
    ///
    /// <para><b>Defers to an open dialog.</b> A Tracker whose document is open in any Scribe dialog
    /// (<see cref="ScribeDialogBase.OpenDocumentId"/>) is display-only here: no count send and no rising-edge
    /// completion, since the read view owns those and an external write would fight the editor's autosave. The
    /// live value is still computed so the baseline is current when the dialog closes.</para>
    ///
    /// <para>Returns true when a displayed count changed. No-op (false) when closed, no player, or no Tracker
    /// pins.</para></summary>
    private bool RecomputeHudTrackers()
    {
        if (!IsOpened()) return false;
        if (capi.World.Player is not { } player) return false;
        // Plain Trackers and Craft parents (recipe output) both count carried inventory; Craft children are plain Trackers.
        var trackerPins = modSystem.MyPins
            .Where(p => p.Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft).ToList();

        // Prune entries for gone pins, even when no Tracker pins remain.
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
            int have = Math.Max(0, counted); // raw carried count; NOT capped at the target (overflow shows)

            // Baseline for the rising edge and display diff: the last live value, else the pin snapshot.
            int baseline = liveTrackerCounts.TryGetValue(key, out var prev) ? prev : pin.CurrentQuantity;
            bool changed = have != baseline;
            liveTrackerCounts[key] = have; // always record so the display override is used consistently
            if (changed) anyDisplayChange = true;

            // Dialog has this doc open: display-only (baseline kept current for when it closes).
            if (dialogHeld.Contains(pin.OwnerDocId)) continue;
            if (!changed) continue; // nothing moved → no send, no edge

            // Persist + converge through the server-authoritative path (the same op the dialog engine uses).
            SendTrackerQuantity(pin.OwnerDocId, pin.TaskId, have);

            // Rising edge only (unmet → met): apply the tracker-completion setting once. Complete is guarded by !done,
            // so a later shortfall neither re-fires nor un-completes.
            bool wasMet = baseline >= pin.TargetQuantity;
            bool nowMet = have >= pin.TargetQuantity;
            if (nowMet && !wasMet) ApplyHudTrackerCompletion(pin);
        }

        return anyDisplayChange;
    }

    /// <summary>DocIds a Scribe dialog has open in any view; the HUD skips send and completion for Trackers in
    /// these (see <see cref="RecomputeHudTrackers"/>).</summary>
    private HashSet<Guid> DialogHeldTrackerDocs()
    {
        var set = new HashSet<Guid>();
        foreach (var dialog in capi.Gui.OpenedGuis.OfType<ScribeDialogBase>())
            if (dialog.OpenDocumentId is { } docId) set.Add(docId);
        return set;
    }

    /// <summary>The "have" count to DISPLAY for a pinned Tracker: the HUD's live carried-inventory count if it
    /// has one, else the pin snapshot's last-known value.</summary>
    private int HudTrackerHave(ScribePinnedRef pin)
        => liveTrackerCounts.TryGetValue((pin.OwnerDocId, pin.TaskId), out var have)
            ? have
            : pin.CurrentQuantity;

    /// <summary>Applies the tracker-completion setting when a pinned Tracker fills with no dialog open (sibling
    /// of <see cref="ScribeDialogBase"/>'s <c>ApplyTrackerCompletion</c>). Complete sends via
    /// <see cref="SendCompletion"/> immediately (no undo window), guarded by <see cref="DisplayedDone"/>; Delete
    /// sends <see cref="ScribeDeleteTaskMessage"/>; Nothing leaves it.</summary>
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
                    SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
                });
                break;
            case ScribeTrackerCompletion.Nothing:
                break;
        }
    }

    /// <summary>Sends a pinned Tracker's counted quantity; the server resolves the owning document, clamps and
    /// resyncs. The local <see cref="liveTrackerCounts"/> drives display without waiting.</summary>
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

    /// <summary>Whether a row renders MUTED (completed, resting at the bottom): displayed-done, past its undo
    /// window, under a sinking policy, i.e. the pins <see cref="ScribePinOrdering.ForDisplay"/> sinks. Within the
    /// window a done pin holds its place; under Keep it is not muted; Unpin/Delete pins are removed.</summary>
    private bool SunkVisual(ScribePinnedRef pin)
        => DisplayedDone(pin)
           && PendingFor(pin) is null
           && modSystem.MySettings.CompletionPolicy is ScribeCompletionPolicy.Sink
                                                     or ScribeCompletionPolicy.UnpinSink;

    /// <summary>Whether a pin is in a pending window whose text fades as a countdown: Unpin/Delete (removal) and
    /// Sink/UnpinSink (moves down). Keep doesn't fade. Rows past their window leave the set this reads, so the
    /// ghost renders them.</summary>
    private bool IsFadingOut(ScribePinnedRef pin)
        => PendingFor(pin) is { } pending
           && pending.Policy is ScribeCompletionPolicy.Unpin or ScribeCompletionPolicy.Delete
                              or ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;

    /// <summary>
    /// A row checkbox was clicked. Checking flips optimistically and records a <see cref="PendingCompletion"/>
    /// held for <see cref="PinHudWaitMs"/>; <see cref="OnTick"/> sends it on expiry. Unchecking in the window
    /// cancels it, a TRUE undo since nothing was sent (matters for Unpin/Delete).
    /// </summary>
    private void OnToggleRow(Guid docId, Guid taskId, bool currentlyDone)
    {
        var key = (docId, taskId);
        bool nowDone = !currentlyDone;

        if (nowDone)
        {
            optimisticDone[key] = true;
            pendingCompletions[key] = new PendingCompletion(
                modSystem.MySettings.CompletionPolicy, elapsedMs + PinHudWaitMs);
        }
        else
        {
            // Undo within the window: cancel the pending send; nothing reached the server.
            if (pendingCompletions.Remove(key))
            {
                optimisticDone.Remove(key);
            }
            else
            {
                // Window already elapsed (completion was sent): a genuine un-complete, sent immediately.
                optimisticDone[key] = false;
                SendCompletion(docId, taskId, modSystem.MySettings.CompletionPolicy);
            }
        }

        if (IsOpened()) RebuildHudBody();
    }

    /// <summary>Fire the deferred completion request for a pin, carrying the policy captured when the
    /// player checked it. The server applies Sink/Keep/Unpin/Delete and re-pushes.</summary>
    private void SendCompletion(Guid docId, Guid taskId, ScribeCompletionPolicy policy)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)policy,
            SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
        });
    }

    /// <summary>Collapse control (chevron or hotkey): flips and persists the client-local
    /// <see cref="ScribePlayerSettings.HudCollapsed"/>; <see cref="ScribeModSystem.UpdateMySettings"/> fires
    /// <see cref="ScribeModSystem.MyPinsChanged"/>, so there is no network round-trip.</summary>
    public void ToggleCollapsed()
        => modSystem.UpdateMySettings(s => s.HudCollapsed = !s.HudCollapsed);

    /// <summary>Opens a pinned Link's Handbook page from its snapshotted <see cref="ScribePinnedRef.LinkTarget"/>.
    /// Separate from the completion checkbox. No-op if unresolved or the survival mod isn't loaded.</summary>
    private void OpenPinnedLink(string? linkTarget) => ScribeItemRef.OpenHandbookPage(capi, linkTarget);

    private void SendClearTimer()
    {
        modSystem.DismissAlarm();
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeClearTimerMessage());
    }

    // ---------------- Temporal-storm corruption ----------------

    /// <summary>
    /// Returns the effective corruption strength (0..1) and whether a storm is active (drives only the title
    /// swap). Strength is the greater of the storm tier's vanilla glitch-strength base (while
    /// <c>StormData.nowStormActive</c>) and a linear ramp from 0 at 0.50 to 1 at 0.10 of the player's
    /// <c>temporalStability</c>. Returns <c>(0, false)</c> when the player's preference is off or the temporal
    /// system can't be resolved.
    /// </summary>
    private (double strength, bool stormActive) ComputeCorruption()
    {
        if (!modSystem.MySettings.StormCorruption) return (0.0, false);

        var stormSys = capi.ModLoader.GetModSystem<SystemTemporalStability>();
        if (stormSys is null) return (0.0, false);

        bool stormActive = stormSys.StormData.nowStormActive;
        double stormStrength = stormActive
            ? stormSys.StormData.nextStormStrength switch
            {
                EnumTempStormStrength.Heavy  => StormHeavyStrength,
                EnumTempStormStrength.Medium => StormMediumStrength,
                _                            => StormLightStrength, // Light (and any unknown) → lightest
            }
            : 0.0;

        // Linear ramp 0 at 0.50 → 1 at 0.10; default 1.0 (stable) when absent so the trigger never fires.
        double stability = capi.World.Player?.Entity?.WatchedAttributes?.GetDouble("temporalStability", 1.0) ?? 1.0;
        double stabilityStrength = Math.Clamp(
            (StabilityRampUpper - stability) / (StabilityRampUpper - StabilityRampLower), 0.0, 1.0);

        return (Math.Max(stormStrength, stabilityStrength), stormActive);
    }

    /// <summary>
    /// Corruption re-scramble tick from <see cref="OnTick"/>. While a trigger is active it advances the seed and
    /// rebuilds on a random 0–5 s cadence; trigger or storm-flag transitions rebuild immediately. Returns true if
    /// it rebuilt (so the caller skips a redundant one).
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
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { RebuildHudBody(); return true; }
            return false;
        }

        if (active && now >= _nextRescrambleMs)
        {
            _corruptionSeed++;
            _nextRescrambleMs = now + NextRescrambleInterval();
            if (IsOpened()) { RebuildHudBody(); return true; }
        }

        return false;
    }

    /// <summary>Random re-scramble interval in [0, 5000] ms from the client RNG.</summary>
    private long NextRescrambleInterval()
        => RescrambleMinMs + (long)(capi.World.Rand.NextDouble() * (RescrambleMaxMs - RescrambleMinMs));

    // ---------------- Build ----------------

    /// <summary>The live, ordered, capped rows: pins minus <see cref="awaitingRemoval"/>, ordered like the Pin Tab
    /// (<see cref="ScribePinOrdering.ForDisplay"/> under a sinking policy, else raw order). Collapsing rows are
    /// <see cref="ScribeAnimatedList"/>'s job, so only live rows are returned.</summary>
    private List<HudPinRow> BuildOrderedRows()
    {
        // Rows awaiting removal must not appear here or they'd flash back at full opacity; their identity leaving the set triggers the collapse.
        var pins = awaitingRemoval.Count == 0
            ? modSystem.MyPins
            : modSystem.MyPins.Where(p => !awaitingRemoval.Contains((p.OwnerDocId, p.TaskId))).ToList();

        // Same order as the Pin Tab (no HUD-only overlay). The in-window "hold in place" falls out because the send
        // is deferred, so LastKnownDone stays false and ForDisplay keeps the pin in the not-done group.
        bool sinkOrder = modSystem.MySettings.CompletionPolicy
            is ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;
        IReadOnlyList<ScribePinnedRef> ordered = sinkOrder ? ScribePinOrdering.ForDisplay(pins) : pins;

        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        return ordered.Take(max)
            .Select(p =>
            {
                // Tracker/Link snapshots carry an item code; resolve icon + name here (where capi lives). A plain Task gives (null, null).
                var (stack, name) = ResolveHudPinItem(p);
                // Same guide-page-scheme substitution as the dialog surfaces: a label-only QuestObjective's icon uses the book-glyph fallback. Display-only.
                bool isStaticVsQuestObjective = p.Kind == ScribeBlockKind.QuestObjective
                    && ScribeQuestCatalog.IsStaticObjectiveCode(p.LinkTarget);
                string? iconLinkTarget = p.Kind == ScribeBlockKind.QuestObjective && stack is null
                    && !isStaticVsQuestObjective
                    ? ScribeLinkTarget.ForPage(p.LinkTarget ?? "")
                    : p.LinkTarget;
                return new HudPinRow(
                    p.OwnerDocId, p.TaskId, p.LastKnownText, DisplayedDone(p), SunkVisual(p),
                    FadingOut: IsFadingOut(p), Kind: p.Kind, LinkTarget: iconLinkTarget,
                    DisplayStack: stack, DisplayName: name,
                    // Live carried count if the HUD's own engine has one, else the snapshot.
                    TargetQuantity: p.TargetQuantity, CurrentQuantity: HudTrackerHave(p),
                    // Subtask depth, so a pinned subtask indents like the other surfaces.
                    Depth: p.Depth, IsStaticVsQuestObjective: isStaticVsQuestObjective);
            })
            .ToList();
    }

    /// <summary>Resolve a pinned Tracker/Link's item icon + display name from its snapshot code, or
    /// <c>(null, null)</c> for a plain Task. Mirrors <c>ScribeDialogBase.ResolvePinItem</c> but reads the
    /// pin's own snapshot so a pinned item row renders even when its source document is unloaded.</summary>
    private (ItemStack? Stack, string? Name) ResolveHudPinItem(ScribePinnedRef p)
    {
        if (capi is null) return (null, null);
        if (p.Kind == ScribeBlockKind.QuestObjective)
        {
            // Mirrors ResolveRowItem's QuestObjective branch (ScribeDialogBase.Layout.cs).
            return p.TargetItemCode is { } objItemCode
                ? ScribeItemRef.ResolveDisplay(capi.World, objItemCode, null)
                : (null, p.LinkLabel ?? p.LinkTarget);
        }
        string? code = p.Kind switch
        {
            // A Craft parent shows its recipe OUTPUT, carried in the same TargetItemCode slot.
            ScribeBlockKind.Tracker or ScribeBlockKind.Craft => p.TargetItemCode,
            ScribeBlockKind.Link => p.LinkTarget,
            _ => null,
        };
        if (code is null) return (null, null);
        return ScribeItemRef.ResolveDisplay(capi.World, code, p.LinkLabel);
    }

    /// <inheritdoc />
    /// <remarks>Returns the persistent-root <see cref="ScribeDialogBody"/> owning the reconcilable subtree; called
    /// once per open and per <see cref="GuiBase.ForceRebuild"/>. In-place updates go through
    /// <see cref="RebuildHudBody"/>, which re-invokes <see cref="BuildHudTree"/>.</remarks>
    protected override Widget Build() => new ScribeDialogBody(bodyKey, BuildHudTree);

    /// <summary>Builds the HUD widget subtree from the current live state (pin set, timer, corruption,
    /// settings). Re-invoked on every reconcile via <see cref="ScribeDialogBody.BodyState.Build"/>, so it
    /// always reflects the latest state.</summary>
    private Widget BuildHudTree()
    {
        var shown = BuildOrderedRows();

        TraceHudRows(shown);

        // "+N more" counts only live pins beyond the cap; rows collapsing out don't count.
        int liveCount = awaitingRemoval.Count == 0
            ? modSystem.MyPins.Count
            : modSystem.MyPins.Count(p => !awaitingRemoval.Contains((p.OwnerDocId, p.TaskId)));
        int max = Math.Min(modSystem.MySettings.HudMaxRows, MaxRenderedRows);
        int moreCount = Math.Max(0, liveCount - max);

        // The HUD is not governed by Pixel-Art Display: it always uses the global LibGUI theme, so there is
        // deliberately no Theme wrap here.
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

        // Corruption strength, storm flag and seed for this build; recomputed here so any rebuild reflects live state.
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
            // Header/footer align toward the anchored edge (same classification as ApplyAnchor).
            leftAligned: ScribePlayerSettings.NormalizeAnchor(modSystem.MySettings.HudAnchor).IsLeftAnchored(),
            rowWidth: ScribePlayerSettings.ClampHudRowWidth(modSystem.MySettings.HudRowWidth),
            rowFontSize: ScribeRowConstants.BaseHudFontSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            checkboxSize: ScribeRowConstants.BaseHudCheckboxSize
                * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.HudFontScale),
            showIcons: modSystem.MySettings.HudShowIcons,
            showSettingsGear: modSystem.MySettings.HudShowSettingsGear,
            onToggleRow: OnToggleRow,
            onOpenLink: OpenPinnedLink,
            onToggleCollapsed: ToggleCollapsed,
            onOpenSettings: modSystem.OpenSettings,
            timerData: timerSnapshot,
            onClearTimer: SendClearTimer,
            capiForTimer: capi,
            questPrompts: BannerEligibleQuestPrompts(),
            onAcceptPrompt: OnAcceptQuestPrompt,
            onDismissPrompt: modSystem.DismissQuestPrompt,
            questAcceptCandidates: modSystem.ComputeQuestAcceptCandidates(),
            questAcceptPickerOpen: questAcceptPickerOpen,
            questAcceptSelectedIndex: questAcceptSelectedIndex,
            onOpenQuestAcceptPicker: OnOpenQuestAcceptPicker,
            onSelectQuestAcceptCandidate: OnSelectQuestAcceptCandidate);
    }

    /// <summary>Reveals the Quest Accept prompt banner's 2+-candidate picker (first tap of the two-step
    /// Accept, mirroring the Inbox Accept control's own picker).</summary>
    private void OnOpenQuestAcceptPicker()
    {
        questAcceptPickerOpen = true;
        RebuildHudBody();
    }

    private void OnSelectQuestAcceptCandidate(int index)
    {
        questAcceptSelectedIndex = index;
        RebuildHudBody();
    }

    private void OnAcceptQuestPrompt(ScribeQuestPrompt prompt, ScribeAcceptCandidate? candidate)
    {
        questAcceptPickerOpen = false;
        questAcceptSelectedIndex = 0;
        modSystem.AcceptQuestPrompt(prompt, candidate);
    }

    // ---------------- DEBUG: blank-checkbox diagnostics ----------------

    /// <summary>DEBUG-only (compiled out in Release) per-row trace for diagnosing a blank-checkbox row.
    /// <c>text=""</c> means the cached text was cleared (a data problem); non-empty text on a blank row is an
    /// opacity problem, where <c>Sunk</c> mutes the row to <see cref="SunkOpacity"/> and <c>FadingOut</c> drives
    /// the text-only fade (see <see cref="ScribeFadeText"/>). Also dumps <see cref="optimisticDone"/>,
    /// <see cref="pendingCompletions"/> and <see cref="awaitingRemoval"/>; a collapsing row appears only under
    /// <c>awaitingRemoval</c>. Filter <c>[scribe-hud]</c> in <c>build/scribe-log.sh --client</c>.</summary>
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
        modSystem.QuestPromptsChanged -= OnQuestPromptsChanged;
        if (tickListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        collapseRegistry.Dispose();
        base.Dispose();
    }
}

// ---------------- HUD content tree ----------------

/// <summary>A value snapshot of one HUD row. No "departing" flag: once a row's completion is sent its identity
/// leaves the item set (via <c>awaitingRemoval</c>) and <see cref="ScribeAnimatedList"/> collapses a frozen
/// ghost.</summary>
internal readonly record struct HudPinRow(
    Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk, bool FadingOut,
    ScribeBlockKind Kind = ScribeBlockKind.Task, string? LinkTarget = null,
    // A pinned Tracker/Link carries its resolved item icon + name and have/need counts so the row renders item-shaped.
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, int Depth = 0,
    bool IsStaticVsQuestObjective = false)
{
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    public bool IsCraft => Kind == ScribeBlockKind.Craft;
    public bool IsQuestObjective => Kind == ScribeBlockKind.QuestObjective;
    /// <summary>Item rows (icon + name) rather than editable text. A Craft parent renders like a Tracker, with a "Craft {0}" label.</summary>
    public bool IsItemKind => IsTracker || IsLink || IsCraft || IsQuestObjective;
    /// <summary>Kinds that DISPLAY a have/need counter. A QuestObjective's counter comes from Progression
    /// Framework, so this is deliberately not <see cref="ScribeBlock.IsCarriedCountTracked"/>.</summary>
    public bool HasCounterRow => IsTracker || IsCraft || IsQuestObjective;
    /// <summary>The label to show: the "Craft {0}" framing for a Craft parent, the plain resolved item
    /// name for a Tracker/Link, else the task text.</summary>
    public string Label => IsCraft
        ? Lang.Get("scribe:scribe-gui-craft-row-label", DisplayName ?? Text)
        : IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// The HUD widget tree: a collapse header over a column of pin rows (header alone when collapsed). Separate
/// from <see cref="HudScribePins"/> to read the LibGUI <see cref="Theme"/> off the <see cref="BuildContext"/>;
/// stateless, with interaction routed through callbacks.
/// </summary>
internal sealed class HudPinsContent : StatelessWidget
{
    /// <summary>Row text glow radius (fraction of font size); keeps text legible over the world with no background plate.</summary>
    private const float GlowWidth = 0.45f;

    /// <summary>Opacity of a sunk (completed, past-undo) row — muted but still readable/undoable.</summary>
    private const float SunkOpacity = 0.5f;

    private readonly IReadOnlyList<HudPinRow> rows;
    private readonly int moreCount;
    /// <summary>Temporal-corruption strength (0..1); 0 = none. Applied via <see cref="Corrupt"/>.</summary>
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
    /// <summary>Whether the HUD header shows the settings gear (<see cref="ScribePlayerSettings.HudShowSettingsGear"/>).
    /// Off omits the gear; the Lectern Settings tab remains reachable.</summary>
    private readonly bool showSettingsGear;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    /// <summary>Opens a pinned Link's Handbook page; wired only to a Link row's label.</summary>
    private readonly Action<string?> onOpenLink;
    private readonly Action onToggleCollapsed;
    private readonly Action onOpenSettings;
    private readonly Scribe.Core.TimerStore? timerData;
    private readonly Action? onClearTimer;
    private readonly ICoreClientAPI? capiForTimer;
    /// <summary>Pending Quest prompts; only the first renders as a banner.</summary>
    private readonly IReadOnlyList<ScribeQuestPrompt> questPrompts;
    /// <summary>Accept action for an accept-prompt banner with the resolved destination (sole candidate or picker choice); null for a completion-prompt.</summary>
    private readonly Action<ScribeQuestPrompt, ScribeAcceptCandidate?>? onAcceptPrompt;
    private readonly Action<ScribeQuestPrompt>? onDismissPrompt;
    /// <summary>This player's current Accept-placement candidates for the banner's accept-prompt
    /// (<see cref="ScribeModSystem.ComputeQuestAcceptCandidates"/>) — irrelevant for a completion-prompt.</summary>
    private readonly IReadOnlyList<ScribeAcceptCandidate> questAcceptCandidates;
    /// <summary>Whether the 2+-candidate picker is revealed (owned by <see cref="HudScribePins"/> as session state).</summary>
    private readonly bool questAcceptPickerOpen;
    private readonly int questAcceptSelectedIndex;
    private readonly Action? onOpenQuestAcceptPicker;
    private readonly Action<int>? onSelectQuestAcceptCandidate;

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
        bool showSettingsGear,
        Action<Guid, Guid, bool> onToggleRow,
        Action<string?> onOpenLink,
        Action onToggleCollapsed,
        Action onOpenSettings,
        Scribe.Core.TimerStore? timerData = null,
        Action? onClearTimer = null,
        ICoreClientAPI? capiForTimer = null,
        IReadOnlyList<ScribeQuestPrompt>? questPrompts = null,
        Action<ScribeQuestPrompt, ScribeAcceptCandidate?>? onAcceptPrompt = null,
        Action<ScribeQuestPrompt>? onDismissPrompt = null,
        IReadOnlyList<ScribeAcceptCandidate>? questAcceptCandidates = null,
        bool questAcceptPickerOpen = false,
        int questAcceptSelectedIndex = 0,
        Action? onOpenQuestAcceptPicker = null,
        Action<int>? onSelectQuestAcceptCandidate = null)
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
        this.showSettingsGear = showSettingsGear;
        this.onToggleRow = onToggleRow;
        this.onOpenLink = onOpenLink;
        this.onToggleCollapsed = onToggleCollapsed;
        this.onOpenSettings = onOpenSettings;
        this.timerData = timerData;
        this.onClearTimer = onClearTimer;
        this.capiForTimer = capiForTimer;
        this.questPrompts = questPrompts ?? Array.Empty<ScribeQuestPrompt>();
        this.onAcceptPrompt = onAcceptPrompt;
        this.questAcceptCandidates = questAcceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
        this.questAcceptPickerOpen = questAcceptPickerOpen;
        this.questAcceptSelectedIndex = questAcceptSelectedIndex;
        this.onOpenQuestAcceptPicker = onOpenQuestAcceptPicker;
        this.onSelectQuestAcceptCandidate = onSelectQuestAcceptCandidate;
        this.onDismissPrompt = onDismissPrompt;
    }

    /// <summary>Runs a user-visible string through the Core corruptor. <paramref name="seedOffset"/> varies the
    /// pattern per string while the frame still advances together; strength 0 returns the input unchanged.</summary>
    private string Corrupt(string text, int seedOffset = 0)
        => ScribeTextCorruptor.Corrupt(text, corruptionStrength, corruptionSeed + seedOffset);

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        // Fully opaque black glow for contrast over busy world backgrounds.
        Vector4 glow = new(0f, 0f, 0f, 1.0f);

        var children = new List<Widget> { BuildHeader(colors, glow) };

        // Quest prompt banner: shown regardless of collapse state so a collapsed HUD doesn't hide it. Wrapped in a
        // slide-in keyed by the prompt's own identity: a new prompt slides in, the same prompt across rebuilds stays settled. The "questprompt:" key prefix
        // keeps it distinct from any row's TaskId key in the shared collapseRegistry.
        if (questPrompts.Count > 0)
        {
            var bannerPrompt = questPrompts[0];
            string entryId = "questprompt:" + bannerPrompt.Source + ":" + bannerPrompt.QuestCode + ":" + bannerPrompt.IsCompletion;
            children.Add(new ScribeSlideIn(
                id: entryId,
                animating: true,
                registry: collapseRegistry,
                child: BuildQuestPromptBanner(colors, glow),
                key: new ValueKey<string>(entryId)));
        }

        if (!collapsed)
        {
            // Pin rows go through ScribeAnimatedList: it diffs the TaskId-keyed set, so a pin leaving it
            // (awaitingRemoval, filtered upstream) collapses via Immediate. Chrome (header, "+N more", timer) stays
            // outside as siblings. Live rows are keyed by TaskId so their State (fade controller, sink tween) survives
            // rebuilds. The live row is unsafe to freeze (its checkbox stays clickable mid-collapse), so each supplies a
            // static zero-opacity-text ghost matching the already-faded row. animateEntry defaults true: new or
            // newly-capped-in rows slide in.
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
                // We own the layout: the container supplies the ordered live rows plus collapsing ghosts; same 4px
                // spacing as the outer Column so gaps read identically.
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
                        // Desaturated 66%: the "+N more" footer and header gear share OnSurfaceVariant and read as a muted near-grey accent.
                        Color = ScribeRowConstants.ShiftBrightness(colors.OnSurfaceVariant, 0f, saturationScale: 0.34f),
                        FontSize = 13,
                        GlowWidth = GlowWidth,
                        GlowColor = glow,
                    }));
            }

            // Timer row below the pins, shown when a Notebook timer is Running or Fired.
            if (timerData is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired })
            {
                children.Add(new Divider());
                children.Add(BuildTimerRow(timerData, colors, glow, capiForTimer));
            }
        }

        // Fixed-width SizedBox so long tasks wrap instead of widening the shrink-wrap window. The Column aligns
        // toward the anchored edge (only the min-width header/footer visibly move; task rows fill the width).
        return ScribeTextDefaults.WrapChrome(new SizedBox(
            width: rowWidth,
            child: new Padding(
                EdgeInsets.All(2),
                child: new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
                    children: children))));
    }

    /// <summary>The collapse header: clickable chevron + title, and a gear opening settings. Present collapsed
    /// too. The gear has its own GestureDetector so tapping it doesn't toggle collapse.</summary>
    private Widget BuildHeader(ColorScheme colors, Vector4 glow)
    {
        var titleStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = new Vector4(1f, 1f, 1f, 1f), // pure opaque white header
            GlowWidth = GlowWidth,
            GlowColor = glow,
        };

        // The chevron is a texture icon (same as the Inbox expand toggle), not a Unicode ▸/▾: no font on Linux
        // reliably carries those glyphs. VsIcon has no text glow, so a Container BoxShadow matches the halo (as
        // ScribeHudGearButton does).
        Widget chevronIcon = new Container(
            new BoxStyle
            {
                BoxShadows = new[]
                {
                    new BoxShadow(Color: new Vector4(0f, 0f, 0f, 0.3f), Offset: new Vector2(0f, 0f), BlurRadius: 2.25f),
                },
            },
            new ScribeVsIconGlyph(
                collapsed ? "scribetriangleright" : "scribetriangledown",
                rowFontSize * 0.6f, // scaled down ~40%: a full em reads too large next to the title
                titleStyle.Color));

        // Separate GestureDetectors so a gear tap doesn't also toggle collapse.
        var collapseToggle = new GestureDetector(
            onTap: _ => onToggleCollapsed(),
            child: new Row(
                spacing: 4,
                mainAxisSize: MainAxisSize.Min,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    chevronIcon,
                    new Text(
                        Corrupt(Lang.Get(stormActive ? "scribe:scribe-hud-title-storm" : "scribe:scribe-hud-title")),
                        titleStyle),
                }));

        var headerChildren = new List<Widget> { collapseToggle };
        if (showSettingsGear)
        {
            // Gear sized/nudged in em units off rowFontSize so it scales with the HUD text size; white resting color
            // matches the title. Self-stateful so hover survives rebuilds.
            headerChildren.Add(new ScribeHudGearButton(
                baseColor: new Vector4(1f, 1f, 1f, 1f),
                fontSize: rowFontSize,
                onTap: onOpenSettings));
        }

        return new Row(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: headerChildren);
    }

    /// <summary>The Quest prompt banner: Link/Dismiss/Settings buttons for the OLDEST banner-eligible prompt
    /// (<c>questPrompts[0]</c>; later prompts appear once it resolves). Buttons come from the shared
    /// <see cref="ScribeQuestPromptActions"/>, which <see cref="GuiDialogScribeQuestPrompt"/> also uses, so the
    /// styles can't drift.
    ///
    /// <para>An accept-prompt's Link follows the Inbox Accept 0/1/2+ rule: 0 carried documents disables it, 1
    /// accepts immediately, 2+ reveals a candidate picker on first tap and confirms on the second. A
    /// completion-prompt never shows a picker.</para></summary>
    private Widget BuildQuestPromptBanner(ColorScheme colors, Vector4 glow)
    {
        var prompt = questPrompts[0];
        var titleLabelStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = ScribeRowConstants.QuestPromptTitleColor, // warm gold — distinct from ordinary row text
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };
        // The quest name renders as ordinary HUD text below the gold label.
        var titleNameStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = ScribeRowConstants.HudStandardTextColor,
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        string titleLabel = Lang.Get(
            prompt.IsCompletion ? "scribe:scribe-hud-questprompt-complete-title" : "scribe:scribe-hud-questprompt-accept-title");
        string titleSuffix = Lang.Get(
            prompt.IsCompletion ? "scribe:scribe-hud-questprompt-complete-title-suffix" : "scribe:scribe-hud-questprompt-accept-title-suffix");
        string titleName = prompt.Title + titleSuffix;

        Widget titleBlock = new Column(
            spacing: 0,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
            children: new Widget[] { new Text(titleLabel, titleLabelStyle), new Text(titleName, titleNameStyle) });

        // Bordered, color-coded buttons (accept green, dismiss red, Settings neutral) from the shared helper. Labels
        // use the standard HUD row-text color, not the accent or pure white (TextStyle.Merge landmine; see
        // ScribeRowConstants.HudStandardTextColor).
        Vector4 promptActionTextColor = ScribeRowConstants.HudStandardTextColor;
        var (picker, linkButton) = ScribeQuestPromptActions.BuildLinkControl(
            prompt, questAcceptCandidates, questAcceptPickerOpen, questAcceptSelectedIndex,
            fontSize: rowFontSize,
            linkAccent: ScribeRowConstants.QuestPromptLinkColor,
            disabledTextColor: colors.OnSurfaceVariant,
            tooltipTextColor: colors.OnSurface,
            onAccept: (p, c) => onAcceptPrompt?.Invoke(p, c),
            onOpenPicker: () => onOpenQuestAcceptPicker?.Invoke(),
            onSelectCandidate: v => onSelectQuestAcceptCandidate?.Invoke(v),
            textColor: promptActionTextColor, glowWidth: GlowWidth, glowColor: glow);

        Widget dismissButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-dismiss-button"), ScribeRowConstants.QuestPromptDismissColor,
            rowFontSize, () => onDismissPrompt?.Invoke(prompt),
            textColor: promptActionTextColor, glowWidth: GlowWidth, glowColor: glow);

        Widget settingsButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-settings-button"), ScribeRowConstants.QuestPromptSettingsColor,
            rowFontSize, onOpenSettings);

        Widget buttonsRow = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[] { linkButton, dismissButton, settingsButton });

        var children = new List<Widget> { titleBlock };
        if (picker is not null) children.Add(picker);
        children.Add(buttonsRow);

        return new Column(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
            children: children);
    }

    /// <summary>Full duration of the destructive-pending (Unpin/Delete) text fade, matched to the HUD pin
    /// window so the ramp reads as a countdown to removal.</summary>
    private const int FadeWindowMs = 1500;

    /// <summary>One LIVE HUD row: [checkbox][text]. A destructive-pending row's text fades linearly to
    /// transparent over the window (countdown); a sunk row mutes to <see cref="SunkOpacity"/>. The checkbox stays
    /// opaque and clickable so undo is available.
    ///
    /// <para>Collapse on removal is not here: the departed identity leaves the set and
    /// <see cref="ScribeAnimatedList"/> collapses a <see cref="BuildFrozenGhost"/>.</para></summary>
    private Widget BuildRow(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        // HUD text is explicit near-white rather than theme.OnSurface: it always renders over the world and needs
        // the contrast. Sunk rows are more muted.
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = row.Sunk
                ? new Vector4(0.70f, 0.70f, 0.70f, 1f)   // sunk: muted light grey
                : ScribeRowConstants.HudStandardTextColor, // active: near-white
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // Per-row seed offset keeps rows from sharing a mark pattern. Unused by the item-kind branch, which corrupts the name in BuildHudItemContent.
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        Widget rowBody;
        if (row.Kind == ScribeBlockKind.Text)
        {
            // A pinned note is text-only on the HUD: no checkbox, no unpin. Unpin from the Pin Tab.
            Widget text = new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Start,
                children: new Widget[] { new Expanded(child: text) });
        }
        else if (row.IsQuestObjective)
        {
            // Not player-completable (mirrors ScribeBlock.IsCompletable): item shape with no checkbox.
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[] { BuildHudItemContent(row, textStyle, interactive: true) });
        }
        else
        {
        var checkbox = new Checkbox(
            value: row.Done,
            onChanged: _ => onToggleRow(row.DocId, row.TaskId, row.Done),
            size: checkboxSize,
            // Grayscale, not the theme default: the stock CheckColor←Primary is the parchment theme's brown, out of
            // place on the theme-independent HUD.
            style: new CheckboxStyle
            {
                CheckColor = new Vector4(0.867f, 0.867f, 0.867f, 1f),   // #dddddd — softer than white, less blinding
                BackgroundColor = new Vector4(0.28f, 0.28f, 0.28f, 0.75f), // neutral dark-grey box at 75% opacity
                BorderColor = new Vector4(0.8f, 0.8f, 0.8f, 0.75f),  // #cccccc at 75% opacity — dimmer light-grey outline
                BorderThickness = 1.5f,
                CornerRadius = 2f,
                LabelStyle = textStyle,   // unused (no label) but required by the struct
            });

        if (row.IsItemKind)
        {
            // Tracker/Link rows show the item icon + name (+ have/need counter on the left for a Tracker); the name is
            // a Handbook hyperlink. A Quest Link's checkbox would mislead (it doesn't affect the quest), so the quest-marker
            // icon replaces it. Swapped inline, not via ScribeRowControlNudge.BuildLeadingControl, which would replace this
            // bespoke grayscale checkbox style.
            Widget leadingControl = ScribeLinkTarget.IsQuest(row.LinkTarget)
                ? new ScribeVsIconGlyph("scribequest", checkboxSize, ScribeTheme.HudQuestLinkAccent)
                : checkbox;
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    leadingControl,
                    new Expanded(child: BuildHudItemContent(row, textStyle, interactive: true)),
                });
        }
        else
        {
            // A plain Task's text fades linearly while destructive-pending (countdown preview). ScribeFadeText self-ticks
            // because a stock implicitly-animated widget would snap across rebuilds. Only text fades; the checkbox stays clickable.
            Widget text = new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);

            // Link hyperlinks are handled in the item-kind branch; a plain Task's text is never a hyperlink.
            rowBody = new Row(
                spacing: 6,
                // Max width so the Expanded text wraps within HudRowWidth minus the checkbox.
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Start,
                children: new Widget[]
                {
                    checkbox,
                    new Expanded(child: text),
                });
        }
        }

        // A Depth-1 subtask is inset (10px + 3% of width, as on the dialog surfaces).
        if (row.Depth > 0)
        {
            rowBody = new Padding(
                EdgeInsets.Only(left: 10f + 0.03f * rowWidth),
                child: rowBody);
        }

        // A sunk row mutes the whole row, faded not snapped. The ValueKey stabilizes identity for the animation;
        // the container adds its own entry/collapse animation.
        return new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));
    }

    /// <summary>A pinned Tracker/Link row's content: item icon + name, with a have/need counter on the left for a
    /// Tracker (mirrors <c>ScribePinRowState.BuildItemContent</c>). Uses the HUD's near-white/glow ink via
    /// <paramref name="textStyle"/>. A ghost passes <paramref name="interactive"/> false (no hyperlink).</summary>
    private Widget BuildHudItemContent(HudPinRow row, TextStyle textStyle, bool interactive)
    {
        // Icons are optional (HudShowIcons); when off, rows read as text-only. The book glyph keeps the HUD's near-white ink.
        float iconSize = rowFontSize * 1.4f;
        float lineHeight = ScribeRowControlNudge.TextLineHeight(rowFontSize);
        // A Quest Link's icon renders in the leading slot and its name uses the HUD-only quest accent (the HUD has no per-material theme seam).
        bool isQuestLink = ScribeLinkTarget.IsQuest(row.LinkTarget);
        Widget? icon = showIcons && row.IsStaticVsQuestObjective
            ? ScribeLinkIcon.BuildObjective(row.DisplayStack, iconSize, textStyle.Color, lineHeight)
            : showIcons && !isQuestLink
                ? ScribeLinkIcon.Build(row.DisplayStack, row.LinkTarget, iconSize, textStyle.Color, lineHeight)
                : (Widget?)null;
        if (isQuestLink) textStyle = textStyle with { Color = ScribeTheme.HudQuestLinkAccent };

        // Name is a Handbook hyperlink when interactive and never toggles the checkbox. A Tracker's code isn't on the
        // row, so it is re-encoded from the resolved stack (keeping attribute variants, e.g. a copper lantern) to open
        // the variant's page rather than the generic one; a Link uses its LinkTarget.
        string nameText = Corrupt(row.Label, seedOffset: row.TaskId.GetHashCode());
        Widget name = new Text(nameText, textStyle);
        bool canOpen = !row.IsStaticVsQuestObjective || row.DisplayStack is not null;
        if (interactive && canOpen)
        {
            string? code = row.IsLink ? row.LinkTarget
                : row.DisplayStack is { } ds ? ScribeItemRef.Encode(ds) : null;
            name = new GestureDetector(
                onPress: e => { e.Handled = true; onOpenLink(code); },
                child: name);
        }

        var children = new List<Widget>();
        if (row.HasCounterRow)
        {
            // "have / need" counter on the left: strong (bold) while collecting, faded with a faint strikethrough once satisfied.
            bool satisfied = row.CurrentQuantity >= row.TargetQuantity;
            children.Add(ScribeTrackerCounterText.Build(
                row.CurrentQuantity, row.TargetQuantity, satisfied,
                strongColor: textStyle.Color, mutedColor: new Vector4(0.70f, 0.70f, 0.70f, 1f),
                lineHeight: lineHeight, baseStyle: textStyle,
                corrupt: s => Corrupt(s, seedOffset: 404)));
        }
        if (icon != null) children.Add(icon);
        children.Add(new Expanded(child: name));

        return new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: children);
    }

    /// <summary>A static snapshot of a HUD row for <see cref="ScribeAnimatedList"/> to collapse on departure. The
    /// live row can't be frozen (clickable checkbox, client state gone), so this twin has the text at ZERO opacity,
    /// matching the already-faded row (<see cref="ScribeFadeText"/> ramped it to ~0) so the collapse closes empty
    /// space. No ValueKey: the container owns the ghost's lifetime.</summary>
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

        // Zero-opacity text; corrupted with the same seed so its metrics match the live row.
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        Widget body = row.IsItemKind
            ? new Expanded(child: new Opacity(0f, BuildHudItemContent(row, textStyle, interactive: false)))
            : new Expanded(child: new Opacity(0f, new Text(rowText, textStyle)));

        var ghostChildren = new List<Widget>();
        if (row.Kind is not ScribeBlockKind.Text and not ScribeBlockKind.QuestObjective)
        {
            ghostChildren.Add(new Checkbox(
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
                }));
        }
        ghostChildren.Add(body);

        Widget ghost = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: ghostChildren);

        // Match the live row's Depth-1 indent so the collapsing ghost keeps identical metrics.
        if (row.Depth > 0)
            ghost = new Padding(EdgeInsets.Only(left: 10f + 0.03f * rowWidth), child: ghost);

        return ghost;
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
        // Distinct seed offsets so the label and countdown don't share a mark pattern.
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

        // Label is full-strength: a live timer's label is primary information.
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
                new Expanded(child: new Text(label, textStyle)),
                timeWidget,
            });

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
/// Text that fades 1→0 over <paramref name="durationMs"/> when <c>fading</c> is set, via a self-owned
/// <see cref="AnimationController"/>.
///
/// <para>Not <see cref="AnimatedOpacity"/>: a freshly created implicitly-animated widget evaluates its tween
/// to the target instantly, so the text would snap to 0.</para>
///
/// <para><b>Reconcile-safety.</b> The HUD reuses row elements across rebuilds, so <see cref="InitState"/> does
/// not re-run when <see cref="Fading"/> flips <c>false→true</c>. The fade is therefore synced from
/// <c>SyncFadeController</c> in both <c>InitState</c> and <see cref="UpdateWidget"/>; it is idempotent, so a
/// repaint doesn't restart the ramp.</para>
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
        // Fresh mount already fading (e.g. a newly added destructive-pending row).
        SyncFadeController();
    }

    public override void UpdateWidget(ScribeFadeText oldWidget)
    {
        base.UpdateWidget(oldWidget);
        // Reused element: InitState didn't re-run, so reconcile a Fading flip here (false→true starts, true→false clears on undo).
        SyncFadeController();
    }

    /// <summary>Syncs the fade controller with <see cref="ScribeFadeText.Fading"/> in both directions. Fading
    /// starts the ramp if not already running. Not fading DISPOSES any controller so <see cref="Build"/> returns to
    /// full opacity; without this, an undo late in the window would leave a reused element's stale controller
    /// holding the text at ~0 opacity (a "checkbox with no text").</summary>
    private void SyncFadeController()
    {
        if (Widget.Fading)
        {
            if (controller != null) return; // already fading

            // Ramp 0→1 over the window (opacity = 1 − value), repainting via MarkNeedsBuild.
            controller = new AnimationController(TimeSpan.FromMilliseconds(Widget.DurationMs), Element.Owner!.GetTickerProvider());
            controller.OnValueChanged += _ => Element.MarkNeedsBuild();
            controller.Forward();
        }
        else
        {
            // Undo / un-fade: snap back to full opacity. Safe here: runs in the build phase, not inside the ticker callback.
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

/// <summary>The HUD header's settings gear: an icon button that brightens +10 HSV Value on hover and opens
/// settings on tap. Stateful so hover survives rebuilds (like <see cref="ScribeFadeText"/>). The nudge is in em
/// units off <see cref="FontSize"/>; the y-offset is a <c>Transform.Translate</c> around the GestureDetector so
/// the clickable region moves with the paint.</summary>
internal sealed class ScribeHudGearButton : StatefulWidget
{
    public ScribeHudGearButton(Vector4 baseColor, float fontSize, Action onTap, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        BaseColor = baseColor;
        FontSize = fontSize;
        OnTap = onTap;
    }

    public Vector4 BaseColor { get; }
    public float FontSize { get; }
    public Action OnTap { get; }

    public override State CreateState() => new ScribeHudGearButtonState();
}

internal sealed class ScribeHudGearButtonState : State<ScribeHudGearButton>
{
    private bool hovered;

    public override Widget Build(BuildContext context)
    {
        // Hover: +10 HSV Value, matching the lectern nav/row buttons.
        var color = hovered
            ? ScribeRowConstants.ShiftBrightness(Widget.BaseColor, +10f)
            : Widget.BaseColor;

        float em = Widget.FontSize;
        float iconSize = 0.75f * em;    // 12px at the tuned 16px baseline
        float leftPad = 0.0625f * em;   // 1px at baseline
        float offsetX = -0.0625f * em;  // 1px at baseline
        // Down nudge of 0.0625em (1px at baseline).
        float offsetY = 0.0625f * em;

        // VsIcon has no text glow, so a Container BoxShadow (black, zero-offset, blurred, as on the lectern nav buttons) halos the icon's box.
        var haloed = new Container(
            new BoxStyle
            {
                BoxShadows = new[]
                {
                    new BoxShadow(
                        Color: new Vector4(0f, 0f, 0f, 0.3f),
                        Offset: new Vector2(0f, 0f),
                        BlurRadius: 2.25f),
                },
            },
            new VsIcon("scribegearhud", iconSize, color));   // HUD-only gear-ring (dialogs keep the solid "scribegear")

        return Transform.Translate(
            new MouseRegion(
                onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
                onExit: _ => { if (hovered) SetState(() => hovered = false); },
                child: new GestureDetector(
                    onTap: _ => Widget.OnTap(),
                    child: new Padding(
                        EdgeInsets.Only(left: leftPad),
                        child: haloed))),
            new Vector2(offsetX, offsetY));
    }
}
