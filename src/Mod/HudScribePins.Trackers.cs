using Scribe.Core;

namespace Scribe;

public sealed partial class HudScribePins
{
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
}
