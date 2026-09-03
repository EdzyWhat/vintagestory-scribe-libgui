using System;
using System.Collections.Generic;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// Physical assignment delivery (`assignment-delivery-mode` / `task-notice-item` capabilities,
/// add-assignment-physical-delivery-mode): the Hybrid range check round-trip, and the Task Notice's
/// own Accept/Decline handling. Kept as its own partial rather than folded into
/// <c>ScribeModSystem.Assignment.cs</c> because a Task Notice creates its assignment record itself, at
/// Accept time — it never goes through <see cref="ScribeAssignmentStore.TryCreate"/> or
/// <c>OnServerReceivedSendAssignmentBatch</c> at all.
/// </summary>
public sealed partial class ScribeModSystem
{
    /// <summary>Savegame key for the persisted last-known-position store (<see cref="ScribePlayerLocationStore"/>).</summary>
    private const string PlayerLocationStoreSaveKey = "scribe:playerlocations:v1";

    /// <summary>Server-side last-known-position store, used by the Hybrid range check to reach an
    /// offline target (Core's range-check function needs SOME position for them). Null on a pure client.</summary>
    private ScribePlayerLocationStore? playerLocationStore;

    /// <summary>In-memory, transient count of outstanding sealed Task Notices addressed to each recipient
    /// UID (task-notice-proximity-signal tasks.md 5.1) — incremented once per notice sealed
    /// (<see cref="SendBatchViaNotice"/>), decremented once per notice consumed
    /// (<see cref="OnServerReceivedTaskNoticeAction"/>'s Accept/Decline). Deliberately NOT persisted: it is
    /// only a cheap "does this player have anything to scan for at all" gate for the proximity heartbeat —
    /// losing it across a server restart just means the ambient signal goes quiet until the next notice is
    /// sealed, never a loss of the notice item itself (which the player's own inventory/a container already
    /// persists like any other item).</summary>
    private readonly Dictionary<string, int> outstandingNoticeCountByTargetUid = new();

    private void AdjustOutstandingNoticeCount(string targetUid, int delta)
    {
        int next = outstandingNoticeCountByTargetUid.GetValueOrDefault(targetUid) + delta;
        if (next <= 0) outstandingNoticeCountByTargetUid.Remove(targetUid);
        else outstandingNoticeCountByTargetUid[targetUid] = next;
    }

    /// <summary>Client → server: "is this target in range of my Assignment Desk?" (Hybrid mode). Resolves
    /// the target's position from their live entity if online, or their last-known position if not; a
    /// target this server has NEVER seen (no last-known position at all) reports out-of-range, since there
    /// is nothing to check against — the safer default for a Hybrid server (see design.md).</summary>
    private void OnServerReceivedDeliveryRangeCheckRequest(IServerPlayer fromPlayer, ScribeDeliveryRangeCheckRequestMessage message)
    {
        if (sapi is null) return;
        string? targetUid = message.TargetPlayerUid;
        if (string.IsNullOrWhiteSpace(targetUid)) return;

        var deskPos = new ScribeWorldPosition(message.X, message.Y, message.Z);
        bool inRange;
        if (sapi.World.PlayerByUid(targetUid) is IServerPlayer { ConnectionState: EnumClientState.Playing } target
            && target.Entity is not null)
        {
            var pos = target.Entity.Pos;
            inRange = ScribeDeliveryPolicy.IsInRange(deskPos, new ScribeWorldPosition(pos.X, pos.Y, pos.Z), ScribeDeliveryConfig.ReadRadius(sapi));
        }
        else if (playerLocationStore is not null && playerLocationStore.TryGetLastKnown(targetUid, out var lastKnown))
        {
            inRange = ScribeDeliveryPolicy.IsInRange(deskPos, lastKnown, ScribeDeliveryConfig.ReadRadius(sapi));
        }
        else
        {
            inRange = false;
        }

        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeDeliveryRangeCheckReplyMessage
        {
            TargetPlayerUid = targetUid,
            InRange = inRange,
        }, fromPlayer);
    }

    /// <summary>Raised on the client whenever a range-check reply arrives, so the Create Assignments tab
    /// can pre-select the delivery toggle for the currently selected target. Carries the reply directly
    /// (including its echoed <see cref="ScribeDeliveryRangeCheckReplyMessage.TargetPlayerUid"/>) so a stale
    /// reply for a since-superseded target selection is recognizable and ignorable by the subscriber.</summary>
    public event Action<ScribeDeliveryRangeCheckReplyMessage>? DeliveryRangeCheckReplied;

    private void OnClientReceivedDeliveryRangeCheckReply(ScribeDeliveryRangeCheckReplyMessage message)
        => DeliveryRangeCheckReplied?.Invoke(message);

    /// <summary>Client → server: request a Hybrid range check for the desk at <paramref name="deskPos"/>
    /// against <paramref name="targetPlayerUid"/> (`assignment-delivery-mode` tasks.md 4.2). No-op on a
    /// pure server/off the client.</summary>
    public void RequestDeliveryRangeCheck(Vintagestory.API.MathTools.BlockPos deskPos, string targetPlayerUid)
    {
        capi?.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeDeliveryRangeCheckRequestMessage
        {
            X = deskPos.X,
            Y = deskPos.Y,
            Z = deskPos.Z,
            TargetPlayerUid = targetPlayerUid,
        });
    }

    /// <summary>Client → server: Accept or Decline a held, sealed Task Notice (`task-notice-item`
    /// capability). Send helper for <see cref="GuiDialogTaskNotice"/> — mirrors
    /// <c>ScribeDialogBase.SendAssignmentAction</c>'s shape but addresses the notice by its own held-slot
    /// identity (an unaccepted notice has no AssignmentId yet).</summary>
    internal void SendTaskNoticeAction(ItemSlot slot, ScribeAssignmentAction action, ScribeAcceptCandidate? target)
    {
        capi?.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeTaskNoticeActionMessage
        {
            SourceInventoryId = slot.Inventory?.InventoryID,
            SourceSlotId = slot.Inventory?.GetSlotId(slot) ?? -1,
            Action = (byte)action,
            TargetInventoryId = target?.InventoryId,
            TargetSlotId = target?.SlotId ?? -1,
            NewTaskInsert = (byte)ScribePlayerSettings.NormalizeNewTaskInsert(MySettings.NewTaskInsert),
        });
    }

    /// <summary>Resolves a Task Notice action packet's held-slot identity to the exact slot, requiring it
    /// to still hold an <see cref="ItemScribeTaskNotice"/> — a stale packet for an item the player no
    /// longer holds (already consumed, dropped, etc.) resolves to null and the action is a no-op.</summary>
    private static ItemSlot? ResolveTaskNoticeSlot(IServerPlayer fromPlayer, string? inventoryId, int slotId)
    {
        ItemSlot? slot = null;
        if (inventoryId is not null)
        {
            var inv = fromPlayer.InventoryManager?.GetInventory(inventoryId);
            if (inv is not null && slotId >= 0 && slotId < inv.Count)
                slot = inv[slotId];
        }
        return slot?.Itemstack?.Collectible is ItemScribeTaskNotice ? slot : null;
    }

    /// <summary>Client → server: Accept or Decline a sealed Task Notice. Thin network shell — see
    /// <see cref="ApplyTaskNoticeAction"/> for the actual logic, which this delegates to unchanged.</summary>
    private void OnServerReceivedTaskNoticeAction(IServerPlayer fromPlayer, ScribeTaskNoticeActionMessage message)
        => ApplyTaskNoticeAction(fromPlayer, message);

    /// <summary>Accept or Decline a sealed Task Notice (`task-notice-item` capability, refine-task-notice-ux
    /// Decision 2). Each row's <see cref="ScribeAssignmentStore"/> record already exists from send time
    /// (<see cref="ScribeAssignmentStore.TryCreateSent"/>) — holding a sealed notice IS physical receipt,
    /// so this first ensures every row has transitioned out of <see cref="ScribeAssignmentState.Sent"/> via
    /// <see cref="ScribeAssignmentStore.TryMarkReceived"/> (a no-op if the proximity heartbeat already
    /// caught up — see <see cref="OnTaskNoticeProximityTick"/>), then applies Accept/Decline through the
    /// SAME <see cref="ScribeAssignmentStore.TryApplyAction"/> path a local-inbox assignment uses, instead
    /// of creating a fresh record. Accept then places each row onto the resolved target document,
    /// mirroring <c>TryPlaceAcceptedAssignment</c>'s own placement shape.
    ///
    /// <para>Public — matches the <see cref="ScribeModSystem.PinOperations"/>-file precedent
    /// (<c>SetPinForPlayer</c>/<c>CompleteTaskForPlayer</c>) of a domain-named method the network handler
    /// delegates to and the integration suite drives directly (refine-task-notice-ux 2.5).</para></summary>
    public void ApplyTaskNoticeAction(IServerPlayer fromPlayer, ScribeTaskNoticeActionMessage message)
    {
        if (sapi is null || assignmentStore is null) return;

        var noticeSlot = ResolveTaskNoticeSlot(fromPlayer, message.SourceInventoryId, message.SourceSlotId);
        if (noticeSlot?.Itemstack is not { } noticeStack
            || !ScribeDocumentAttributes.TryReadFrom(noticeStack, out var noticeDoc)
            || noticeDoc is null || noticeDoc.Blocks.Count == 0)
        {
            Trace("tasknotice-action from {0}: held item is not a sealed Task Notice — ignored", fromPlayer.PlayerName);
            return;
        }

        // Only the addressed recipient may Accept/Decline — a physical item can change hands, but the
        // assignment it carries is still addressed to a specific player.
        if (noticeDoc.Blocks.Any(b => b.Assignment is null || b.Assignment.TargetPlayerUid != fromPlayer.PlayerUID))
        {
            Trace("tasknotice-action from {0}: notice is not addressed to them — ignored", fromPlayer.PlayerName);
            return;
        }

        string date = NotebookHost.FormatDate(sapi);
        // Holding the sealed notice at all already proves physical receipt — ensure every row's store
        // record is out of Sent regardless of whether the proximity heartbeat's own inventory scan has
        // caught up yet (its per-chunk-crossing gate can lag a same-chunk pickup-then-immediate-Accept).
        foreach (var block in noticeDoc.Blocks)
            assignmentStore.TryMarkReceived(block.TaskId, date);

        var action = (ScribeAssignmentAction)message.Action;
        if (action == ScribeAssignmentAction.Decline)
        {
            var declineAssignerUids = new HashSet<string>();
            foreach (var block in noticeDoc.Blocks)
            {
                if (!assignmentStore.TryApplyAction(block.TaskId, fromPlayer.PlayerUID, ScribeAssignmentAction.Decline))
                    continue;
                declineAssignerUids.Add(block.Assignment!.AssignerUid);
                StampTransitionDate(assignmentStore.TryGet(block.TaskId)!.Assignment!, date);
            }

            noticeSlot.Itemstack = null;
            noticeSlot.MarkDirty();
            AdjustOutstandingNoticeCount(fromPlayer.PlayerUID, -1);
            Trace("tasknotice-action from {0}: declined and consumed a {1}-row notice", fromPlayer.PlayerName, noticeDoc.Blocks.Count);

            // Passive resync only (design.md: "no active notification, no toast, no highlight") — the
            // Assigner's next Sent History view simply reflects Declined, exactly like a local-inbox decline.
            PushAssignmentsTo(fromPlayer);
            foreach (var assignerUid in declineAssignerUids)
                if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);
            return;
        }
        if (action != ScribeAssignmentAction.Accept) return;

        var placementSlot = ResolveItemPacketSlot(fromPlayer, message.TargetInventoryId, message.TargetSlotId);
        if (placementSlot?.Itemstack?.Collectible is not IScribeDocumentItem targetItem || !targetItem.IsSlotWriteable(placementSlot))
        {
            Trace("tasknotice-action from {0}: Accept placement target unresolvable/read-only — notice stays held", fromPlayer.PlayerName);
            return;
        }

        if (!ScribeDocumentAttributes.TryReadFrom(placementSlot.Itemstack!, out var targetDoc) || targetDoc is null)
            targetDoc = new ScribeDocument();

        if (!targetItem.DocumentPolicy(placementSlot).CanHold(targetDoc.BlockCount + noticeDoc.Blocks.Count))
        {
            Trace("tasknotice-action from {0}: Accept placement target has no capacity for {1} rows — notice stays held",
                fromPlayer.PlayerName, noticeDoc.Blocks.Count);
            return;
        }

        string destinationLabel = ScribeAssignmentDestinationLabel.Format(placementSlot.Itemstack!);
        var assignerUids = new HashSet<string>();

        foreach (var block in noticeDoc.Blocks)
        {
            var sourceAssignment = block.Assignment!;
            assignerUids.Add(sourceAssignment.AssignerUid);

            // The record already exists (Unaccepted, from the TryMarkReceived pass above) — transition it
            // through the same actor-validated path a local-inbox Accept uses, instead of creating a fresh
            // one (refine-task-notice-ux).
            if (!assignmentStore.TryApplyAction(block.TaskId, fromPlayer.PlayerUID, ScribeAssignmentAction.Accept))
            {
                Trace("tasknotice-action from {0}: row {1} rejected (illegal transition or unknown id) — skipped", fromPlayer.PlayerName, block.TaskId);
                continue;
            }
            var record = assignmentStore.TryGet(block.TaskId);
            if (record?.Assignment is null) continue; // defensive — TryApplyAction just succeeded above

            StampTransitionDate(record.Assignment, date);
            record.Assignment!.AcceptedIntoLabel = destinationLabel;
            var placed = new ScribeBlock(record.Kind, record.Text, depth: record.Depth, taskId: record.TaskId,
                targetItemCode: record.TargetItemCode, targetQuantity: record.TargetQuantity,
                currentQuantity: record.CurrentQuantity, linkTarget: record.LinkTarget, linkLabel: record.LinkLabel,
                recipeSignature: record.RecipeSignature, linkDescription: record.LinkDescription,
                assignment: record.Assignment!.Clone());
            targetDoc.InsertAssignedBlock(
                targetDoc.InsertIndexForBatch(placed.Assignment!.BatchId, (ScribeNewTaskInsert)message.NewTaskInsert),
                placed);
        }

        ScribeDocumentAttributes.WriteTo(placementSlot.Itemstack!, targetDoc);
        placementSlot.MarkDirty();
        noticeSlot.Itemstack = null;
        noticeSlot.MarkDirty();
        AdjustOutstandingNoticeCount(fromPlayer.PlayerUID, -1);

        Trace("tasknotice-action from {0}: accepted {1} row(s) onto {2}", fromPlayer.PlayerName, noticeDoc.Blocks.Count,
            placementSlot.Itemstack?.Collectible.Code);

        PushAssignmentsTo(fromPlayer);
        foreach (var assignerUid in assignerUids)
            if (sapi.World.PlayerByUid(assignerUid) is IServerPlayer assigner) PushAssignmentsTo(assigner);

        sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeNotebookSaveMessage
        {
            DocIdBytes = targetDoc.DocId.ToByteArray(),
            DocumentBytes = ScribeDocumentCodec.Serialize(targetDoc),
        }, fromPlayer);
    }

    /// <summary>Persists a disconnecting player's last known position (Core 1.5) — the Hybrid range
    /// check's only way to place an offline target, since the server otherwise has no live entity
    /// position for them at all. Mirrors the Sign block's <c>ToTreeAttributes</c>/<c>MarkDirty</c>
    /// persistence pattern in spirit (a value captured on a lifecycle event, held until explicitly
    /// re-saved) even though the actual storage is <see cref="ScribePlayerLocationStore"/>'s own savegame
    /// key rather than a block entity's tree.</summary>
    private void OnScribePlayerDisconnect(IServerPlayer player)
    {
        if (playerLocationStore is null || player.Entity is null) return;
        var pos = player.Entity.Pos;
        playerLocationStore.SetLastKnown(player.PlayerUID, new ScribeWorldPosition(pos.X, pos.Y, pos.Z));
    }

    /// <summary>Per-player chunk coordinate at the time of that player's last proximity scan
    /// (task-notice-proximity-signal tasks.md 5.2) — the scan below is skipped whenever a player's
    /// current chunk still matches this, so a stationary player costs nothing between chunk crossings.</summary>
    private readonly Dictionary<string, Vec3i> lastScannedChunkByPlayerUid = new();

    /// <summary>How far (blocks) the proximity scan looks for a matching Task Notice (design.md: "a
    /// ~10-15 block scan").</summary>
    private const double NoticeScanRadius = 12.0;

    /// <summary>The <c>OnStormTick</c>-style heartbeat for Task Notice proximity discovery
    /// (task-notice-proximity-signal tasks.md 5.1-5.3) AND Sent → Received detection (refine-task-notice-ux
    /// Decision 2): for every online player with at least one outstanding sealed notice addressed to them
    /// (the cheap <see cref="outstandingNoticeCountByTargetUid"/> gate), first checks their OWN inventory
    /// for a carried notice (cheap slot iteration, run every tick — not gated by the chunk-boundary check
    /// below, which exists only to bound the more expensive nearby-scan), then — gated by the chunk-boundary
    /// movement check (5.2) — scans nearby dropped items and block-entity containers (5.3) for a matching
    /// stack and pings that one client to spawn the ambient discovery effect (5.4). Registered alongside
    /// <c>OnStormTick</c> in <c>StartServerSide</c>.</summary>
    private void OnTaskNoticeProximityTick(float _)
    {
        if (sapi is null || outstandingNoticeCountByTargetUid.Count == 0) return;

        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (outstandingNoticeCountByTargetUid.GetValueOrDefault(player.PlayerUID) <= 0) continue;

            if (assignmentStore is not null) MarkReceivedForCarriedNotices(player);

            if (player.Entity is null) continue;

            var pos = player.Entity.Pos;
            var chunk = new Vec3i((int)pos.X / GlobalConstants.ChunkSize, (int)pos.Y / GlobalConstants.ChunkSize,
                (int)pos.Z / GlobalConstants.ChunkSize);
            if (lastScannedChunkByPlayerUid.TryGetValue(player.PlayerUID, out var lastChunk) && lastChunk.Equals(chunk))
                continue;
            lastScannedChunkByPlayerUid[player.PlayerUID] = chunk;

            var found = FindAddressedNoticePosition(player.PlayerUID, pos.XYZ);
            if (found is not null)
            {
                sapi.Network.GetChannel(NetworkChannelName).SendPacket(new ScribeTaskNoticeProximityPingMessage
                {
                    X = found.X,
                    Y = found.Y,
                    Z = found.Z,
                }, player);
            }
        }
    }

    /// <summary>Own-inventory half of Sent → Received detection (refine-task-notice-ux Decision 2): scans
    /// <paramref name="player"/>'s own hotbar + backpack (the same carried-only inventories
    /// <see cref="ScribeModSystem.EnumerateCarriedSlots"/> already defines for the Accept-placement picker)
    /// for a sealed Task Notice addressed to them, and calls <see cref="ScribeAssignmentStore.TryMarkReceived"/>
    /// for every row it carries — a no-op for any row not currently in the Sent state, so re-running this
    /// every tick for a player who still has the notice carried is harmless. Pushes a fresh Inbox sync only
    /// if anything actually transitioned, sparing an idle re-sync on every tick.
    ///
    /// <para>Public — the same "network-driven, but also directly integration-suite-drivable" seam as
    /// <see cref="SendAssignmentBatch"/>/<see cref="ApplyTaskNoticeAction"/>: a test stands in for the
    /// proximity heartbeat's own-inventory scan by moving a notice into a player's inventory and calling
    /// this directly, rather than waiting on <see cref="OnTaskNoticeProximityTick"/>'s real tick cadence
    /// (refine-task-notice-ux 2.5).</para></summary>
    public void MarkReceivedForCarriedNotices(IServerPlayer player)
    {
        string date = NotebookHost.FormatDate(sapi!);
        bool anyReceived = false;
        foreach (var slot in ScribeModSystem.EnumerateCarriedSlots(player))
        {
            if (slot.Itemstack?.Collectible is not ItemScribeTaskNotice) continue;
            if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) continue;

            foreach (var block in doc.Blocks)
            {
                if (block.Assignment?.TargetPlayerUid != player.PlayerUID) continue;
                if (assignmentStore!.TryMarkReceived(block.TaskId, date)) anyReceived = true;
            }
        }
        if (anyReceived) PushAssignmentsTo(player);
    }

    /// <summary>The scan itself (tasks.md 5.3): dropped/thrown notices via
    /// <see cref="IWorldAccessor.GetEntitiesAround"/>, then notices sitting inside any block entity
    /// exposing an <see cref="IBlockEntityContainer"/> inventory, walked chunk-by-chunk
    /// (<see cref="IWorldChunk.BlockEntities"/>) across the scan radius's bounding box — a generic
    /// at-rest scan with no coupling to any specific container type (design.md). Returns the first
    /// match's world position, or null.</summary>
    private Vec3d? FindAddressedNoticePosition(string targetUid, Vec3d center)
    {
        if (sapi is null) return null;

        var itemEntities = sapi.World.GetEntitiesAround(center, (float)NoticeScanRadius, (float)NoticeScanRadius,
            e => e is EntityItem);
        foreach (var entity in itemEntities)
        {
            if (entity is EntityItem { Itemstack: { } stack } && NoticeAddressedTo(stack, targetUid))
                return entity.Pos.XYZ;
        }

        int minCx = (int)Math.Floor((center.X - NoticeScanRadius) / GlobalConstants.ChunkSize);
        int maxCx = (int)Math.Floor((center.X + NoticeScanRadius) / GlobalConstants.ChunkSize);
        int minCy = (int)Math.Floor((center.Y - NoticeScanRadius) / GlobalConstants.ChunkSize);
        int maxCy = (int)Math.Floor((center.Y + NoticeScanRadius) / GlobalConstants.ChunkSize);
        int minCz = (int)Math.Floor((center.Z - NoticeScanRadius) / GlobalConstants.ChunkSize);
        int maxCz = (int)Math.Floor((center.Z + NoticeScanRadius) / GlobalConstants.ChunkSize);

        for (int cx = minCx; cx <= maxCx; cx++)
        for (int cy = minCy; cy <= maxCy; cy++)
        for (int cz = minCz; cz <= maxCz; cz++)
        {
            var chunk = sapi.World.BlockAccessor.GetChunk(cx, cy, cz);
            if (chunk?.BlockEntities is null) continue;

            foreach (var (blockPos, blockEntity) in chunk.BlockEntities)
            {
                if (blockEntity is not IBlockEntityContainer container) continue;
                if (blockPos.DistanceTo(center.X, center.Y, center.Z) > NoticeScanRadius) continue;

                foreach (var slot in container.Inventory)
                {
                    if (slot.Itemstack is { } candidate && NoticeAddressedTo(candidate, targetUid))
                        return blockPos.ToVec3d().Add(0.5, 0.5, 0.5);
                }
            }
        }

        return null;
    }

    private static bool NoticeAddressedTo(ItemStack stack, string targetUid)
        => stack.Collectible is ItemScribeTaskNotice
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            && doc.Blocks.Any(b => b.Assignment?.TargetPlayerUid == targetUid);

    /// <summary>Server → client: spawns the existing ambient discovery effect at the found notice's
    /// position, client-local to the one player this ping was addressed to (tasks.md 5.4). Reuses
    /// <see cref="ScribeAssignmentParticleEmitter"/>'s Vec3d-centered overload rather than inventing a
    /// second effect.</summary>
    private void OnClientReceivedTaskNoticeProximityPing(ScribeTaskNoticeProximityPingMessage message)
    {
        if (capi is null) return;
        ScribeAssignmentParticleEmitter.SpawnAt(capi, new Vec3d(message.X, message.Y, message.Z), seedBurst: true);
    }
}
