using System.Diagnostics;
using Gui;
using Gui.Widgets.Framework;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Common;

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
public sealed partial class HudScribePins : GuiBase
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

