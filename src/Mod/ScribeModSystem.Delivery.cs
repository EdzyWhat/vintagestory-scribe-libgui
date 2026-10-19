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
/// Physical assignment delivery: the Hybrid range check round-trip and the Task Notice's own Accept/Decline handling.
/// A Task Notice creates its assignment record at Accept time, so it never goes through
/// <see cref="ScribeAssignmentStore.TryCreate"/> or <c>OnServerReceivedSendAssignmentBatch</c>.
/// </summary>
public sealed partial class ScribeModSystem
{
    /// <summary>Savegame key for the persisted last-known-position store.</summary>
    private const string PlayerLocationStoreSaveKey = "scribe:playerlocations:v1";

    /// <summary>Server-side last-known-position store, so the Hybrid range check can reach an offline target. Null on a pure client.</summary>
    private ScribePlayerLocationStore? playerLocationStore;

    /// <summary>Transient count of outstanding sealed Task Notices per recipient UID (+1 per <see cref="SendBatchViaNotice"/>,
    /// -1 per Accept/Decline). Deliberately NOT persisted: it only gates the proximity heartbeat, and losing it on restart just
    /// silences the ambient signal until the next notice is sealed.</summary>
    private readonly Dictionary<string, int> outstandingNoticeCountByTargetUid = new();

    private void AdjustOutstandingNoticeCount(string targetUid, int delta)
    {
        int next = outstandingNoticeCountByTargetUid.GetValueOrDefault(targetUid) + delta;
        if (next <= 0) outstandingNoticeCountByTargetUid.Remove(targetUid);
        else outstandingNoticeCountByTargetUid[targetUid] = next;
    }

    /// <summary>Test-support accessor for <see cref="outstandingNoticeCountByTargetUid"/>.</summary>
    public int OutstandingNoticeCount(string targetPlayerUid) =>
        outstandingNoticeCountByTargetUid.GetValueOrDefault(targetPlayerUid);

    /// <summary>Client → server: is this target in range of my Assignment Desk? Uses the live entity position if online,
    /// else the last-known position; a never-seen target reports out-of-range.</summary>
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

    /// <summary>Raised on the client when a range-check reply arrives. Carries the echoed target UID so a stale reply
    /// for a superseded selection can be ignored.</summary>
    public event Action<ScribeDeliveryRangeCheckReplyMessage>? DeliveryRangeCheckReplied;

    private void OnClientReceivedDeliveryRangeCheckReply(ScribeDeliveryRangeCheckReplyMessage message)
        => DeliveryRangeCheckReplied?.Invoke(message);

    /// <summary>Client → server: request a Hybrid range check for the desk at <paramref name="deskPos"/> against <paramref name="targetPlayerUid"/>. No-op off the client.</summary>
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

    /// <summary>Client → server: Accept or Decline a held, sealed Task Notice, addressed by its held-slot identity
    /// (an unaccepted notice has no AssignmentId yet).</summary>
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

    /// <summary>Resolves a Task Notice action packet's slot identity, requiring it to still hold an <see cref="ItemScribeTaskNotice"/>;
    /// a stale packet resolves to null and the action is a no-op.</summary>
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

    /// <summary>Client → server: Accept or Decline a sealed Task Notice. Delegates to <see cref="ApplyTaskNoticeAction"/>.</summary>
    private void OnServerReceivedTaskNoticeAction(IServerPlayer fromPlayer, ScribeTaskNoticeActionMessage message)
        => ApplyTaskNoticeAction(fromPlayer, message);

    /// <summary>Accept or Decline a sealed Task Notice. Each row's store record already exists from send time; holding the
    /// notice IS physical receipt, so every row is first moved out of <see cref="ScribeAssignmentState.Sent"/> via
    /// <see cref="ScribeAssignmentStore.TryMarkReceived"/>, then Accept/Decline goes through the same
    /// <see cref="ScribeAssignmentStore.TryApplyAction"/> path as a local-inbox assignment. Accept then places each row onto the
    /// resolved target document. Public so the integration suite can drive it directly.</summary>
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

        // The assignment is addressed to a specific player even if the item changed hands. Defensive: every genuine row carries an Assignment.
        if (noticeDoc.Blocks.Any(b => b.Assignment is null))
        {
            Trace("tasknotice-action from {0}: notice row missing its assignment — ignored", fromPlayer.PlayerName);
            return;
        }

        string date = NotebookHost.FormatDate(sapi);
        double timestamp = sapi.World.Calendar.TotalDays;
        // Holding the notice proves receipt; do not rely on the proximity heartbeat, whose chunk-crossing gate can lag a same-chunk pickup-then-Accept.
        foreach (var block in noticeDoc.Blocks)
            assignmentStore.TryMarkReceived(block.TaskId, date, timestamp);

        var action = (ScribeAssignmentAction)message.Action;
        if (action == ScribeAssignmentAction.Decline)
        {
            // Unlike Accept, Decline is not redirectable: only the addressed recipient may decline.
            if (noticeDoc.Blocks.Any(b => b.Assignment!.TargetPlayerUid != fromPlayer.PlayerUID))
            {
                Trace("tasknotice-action from {0}: notice is not addressed to them — ignored", fromPlayer.PlayerName);
                return;
            }

            var declineAssignerUids = new HashSet<string>();
            foreach (var block in noticeDoc.Blocks)
            {
                if (!assignmentStore.TryApplyAction(block.TaskId, fromPlayer.PlayerUID, ScribeAssignmentAction.Decline))
                    continue;
                declineAssignerUids.Add(block.Assignment!.AssignerUid);
                StampTransitionDate(assignmentStore.TryGet(block.TaskId)!.Assignment!, date, timestamp);
            }

            noticeSlot.Itemstack = null;
            noticeSlot.MarkDirty();
            AdjustOutstandingNoticeCount(fromPlayer.PlayerUID, -1);
            Trace("tasknotice-action from {0}: declined and consumed a {1}-row notice", fromPlayer.PlayerName, noticeDoc.Blocks.Count);

            // Passive resync only (no notification), like a local-inbox decline.
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

            // A non-recipient holder confirmed the redirect warning client-side; redirect the record's target to them so Accept resolves them as Assignee.
            if (sourceAssignment.TargetPlayerUid != fromPlayer.PlayerUID)
                assignmentStore.TryRedirectTarget(block.TaskId, fromPlayer.PlayerUID, date, timestamp);

            // The record already exists (Unaccepted); transition it through the same path as a local-inbox Accept.
            if (!assignmentStore.TryApplyAction(block.TaskId, fromPlayer.PlayerUID, ScribeAssignmentAction.Accept))
            {
                Trace("tasknotice-action from {0}: row {1} rejected (illegal transition or unknown id) — skipped", fromPlayer.PlayerName, block.TaskId);
                continue;
            }
            var record = assignmentStore.TryGet(block.TaskId);
            if (record?.Assignment is null) continue; // defensive — TryApplyAction just succeeded above

            StampTransitionDate(record.Assignment, date, timestamp);
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

    /// <summary>Persists a disconnecting player's last known position, the Hybrid range check's only way to place an offline target.</summary>
    private void OnScribePlayerDisconnect(IServerPlayer player)
    {
        if (playerLocationStore is null || player.Entity is null) return;
        var pos = player.Entity.Pos;
        playerLocationStore.SetLastKnown(player.PlayerUID, new ScribeWorldPosition(pos.X, pos.Y, pos.Z));
    }

    /// <summary>Per-player chunk coordinate at the last proximity scan; the nearby scan is skipped while it is unchanged.</summary>
    private readonly Dictionary<string, Vec3i> lastScannedChunkByPlayerUid = new();

    /// <summary>How far (blocks) the proximity scan looks for a matching Task Notice.</summary>
    private const double NoticeScanRadius = 12.0;

    /// <summary>Heartbeat for Task Notice proximity discovery and Sent → Received detection. For each online player with an
    /// outstanding notice: checks their own inventory every tick, then, gated by a chunk-boundary movement check (to bound the
    /// costlier scan), scans nearby dropped items and containers and pings that client. Registered in <c>StartServerSide</c>.</summary>
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

            var found = FindAddressedNoticePosition(pos.XYZ,
                doc => doc.Blocks.Any(b => b.Assignment?.TargetPlayerUid == player.PlayerUID));
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

    /// <summary>Own-inventory half of Sent → Received detection: scans the player's carried slots (<see cref="ScribeModSystem.EnumerateCarriedSlots"/>)
    /// for a notice addressed to them and calls <see cref="ScribeAssignmentStore.TryMarkReceived"/> per row (idempotent). Pushes an
    /// Inbox sync only if something transitioned. Public so tests can call it instead of waiting on the tick.</summary>
    public void MarkReceivedForCarriedNotices(IServerPlayer player)
    {
        string date = NotebookHost.FormatDate(sapi!);
        double timestamp = sapi!.World.Calendar.TotalDays;
        bool anyReceived = false;
        foreach (var slot in ScribeModSystem.EnumerateCarriedSlots(player))
        {
            if (slot.Itemstack?.Collectible is not ItemScribeTaskNotice) continue;
            if (!ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) || doc is null) continue;

            foreach (var block in doc.Blocks)
            {
                if (block.Assignment?.TargetPlayerUid != player.PlayerUID) continue;
                if (assignmentStore!.TryMarkReceived(block.TaskId, date, timestamp)) anyReceived = true;
            }
        }
        if (anyReceived) PushAssignmentsTo(player);
    }

    /// <summary>Locate-only entry point for the discovery ping: the nearby (dropped + container) scan with a match predicate.</summary>
    private Vec3d? FindAddressedNoticePosition(Vec3d center, System.Func<ScribeDocument, bool> predicate)
        => FindOrConsumeNearbyNotice(center, predicate, consume: false, out _);

    /// <summary>Proactive-consume: searches the player's carried slots first, then falls back to the nearby scan used by the ping,
    /// but clears/despawns the match. Returns whether anything was removed.</summary>
    private bool TryConsumeAddressedNotice(IServerPlayer targetPlayer, System.Func<ScribeDocument, bool> predicate)
    {
        if (targetPlayer.Entity is null) return false;

        foreach (var slot in ScribeModSystem.EnumerateCarriedSlots(targetPlayer))
        {
            if (slot.Itemstack is not { } stack || !NoticeMatches(stack, predicate)) continue;
            slot.Itemstack = null;
            slot.MarkDirty();
            return true;
        }

        FindOrConsumeNearbyNotice(targetPlayer.Entity.Pos.XYZ, predicate, consume: true, out var removed);
        return removed;
    }

    /// <summary>The shared scan: dropped item entities, then notices inside any block entity with an <see cref="IBlockEntityContainer"/>
    /// inventory, walked chunk-by-chunk over the scan radius. Returns the first match's position, or null. When
    /// <paramref name="consume"/> is true the match is removed (item entity despawned, container slot cleared) and
    /// <paramref name="removed"/> reports it.</summary>
    private Vec3d? FindOrConsumeNearbyNotice(Vec3d center, System.Func<ScribeDocument, bool> predicate, bool consume,
        out bool removed)
    {
        removed = false;
        if (sapi is null) return null;

        var itemEntities = sapi.World.GetEntitiesAround(center, (float)NoticeScanRadius, (float)NoticeScanRadius,
            e => e is EntityItem);
        foreach (var entity in itemEntities)
        {
            if (entity is EntityItem { Itemstack: { } stack } && NoticeMatches(stack, predicate))
            {
                var pos = entity.Pos.XYZ;
                if (consume)
                {
                    entity.Die(EnumDespawnReason.Removed);
                    removed = true;
                }
                return pos;
            }
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
                    if (slot.Itemstack is { } candidate && NoticeMatches(candidate, predicate))
                    {
                        var pos = blockPos.ToVec3d().Add(0.5, 0.5, 0.5);
                        if (consume)
                        {
                            slot.Itemstack = null;
                            slot.MarkDirty();
                            removed = true;
                        }
                        return pos;
                    }
                }
            }
        }

        return null;
    }

    private static bool NoticeMatches(ItemStack stack, System.Func<ScribeDocument, bool> predicate)
        => stack.Collectible is ItemScribeTaskNotice
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            && predicate(doc);

    /// <summary>Proactive Task Notice consumption after an Accept/Decline/Cancel/Discard goes through
    /// <see cref="ScribeModSystem.ApplyAssignmentAction"/> rather than the notice's dialog: best-effort consumes a sealed notice
    /// carrying <paramref name="taskId"/>, searching around <paramref name="targetPlayerUid"/> (the Assignee, the only party who
    /// could carry it). No-op if that player is offline.
    ///
    /// <para>A multi-row notice is removed only once EVERY row has left Sent/Unaccepted; otherwise it still carries an unresolved row.</para></summary>
    private void ConsumeMatchingTaskNotice(Guid taskId, string targetPlayerUid)
    {
        if (sapi is null || assignmentStore is null) return;
        if (sapi.World.PlayerByUid(targetPlayerUid) is not IServerPlayer { ConnectionState: EnumClientState.Playing } targetPlayer)
            return;

        bool AllRowsResolved(ScribeDocument doc) =>
            doc.Blocks.Any(b => b.TaskId == taskId) &&
            doc.Blocks.All(b => assignmentStore.TryGet(b.TaskId)?.Assignment?.State is not
                (ScribeAssignmentState.Sent or ScribeAssignmentState.Unaccepted));

        if (TryConsumeAddressedNotice(targetPlayer, AllRowsResolved))
            AdjustOutstandingNoticeCount(targetPlayerUid, -1);
    }

    /// <summary>Server → client: spawns the ambient discovery effect at the found notice's position, for the addressed player only.</summary>
    private void OnClientReceivedTaskNoticeProximityPing(ScribeTaskNoticeProximityPingMessage message)
    {
        if (capi is null) return;
        ParticleEmitter.SpawnAt(capi, new Vec3d(message.X, message.Y, message.Z), seedBurst: true);
    }
}
