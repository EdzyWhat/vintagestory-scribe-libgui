using System;
using System.Linq;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// The standalone Inbox block's placed-block entity. A thin subclass of
/// <see cref="BlockEntityScribeWritingStation"/> supplying only its identity/config. Reuses the
/// Lectern's page art as a placeholder backdrop; <see cref="PageAspect"/> is a fixed <c>W × 1.2W</c>,
/// same as <see cref="BlockEntityAssignmentDesk"/>.
/// </summary>
public sealed class BlockEntityInbox : BlockEntityScribeWritingStation
{
    protected override ScribeBackdropSpec PageBackdrop => ScribeBackdrops.LecternPage;

    protected override float PageAspect => 1.2f;

    protected override string DefaultDocumentTitleKey => "scribe:doctitle-inbox";

    protected override string MeshCacheKeyPrefix => "scribeinboxmesh";

    protected override ScribeDialogBase CreateDialog(ICoreClientAPI capi) =>
        new GuiDialogScribeInbox(Pos, this, capi);

    /// <summary>
    /// Wall-mounted facing, mirroring <see cref="BlockEntityScribeChalkboard.WallMountAngleRad"/>. Null for
    /// the ground ("up") variant, which keeps the base's player-facing angle.
    /// <para>Unlike the Chalkboard's <c>side</c> variant (the wall's own direction), the "orientation"
    /// variant is named after the CLICKED face (<see cref="BlockInbox.TryPlaceBlock"/>), pointing AWAY from
    /// the wall. So "south" means the wall is to the NORTH: the angles are the Chalkboard's mapping with
    /// north/south and east/west swapped.</para>
    /// </summary>
    protected override float? WallMountAngleRad
    {
        get
        {
            string? orientation = Block?.Variant["orientation"];
            if (orientation is null or "up") return null;

            float deg = orientation switch
            {
                "north" => 180f,
                "east"  => 90f,
                "south" => 0f,
                "west"  => 270f,
                _       => 0f,
            };
            return deg * ((float)Math.PI / 180f);
        }
    }

    /// <summary>Resolves which of the 2 Inbox box-tuning targets this instance reads from, using the same
    /// ground-vs-wall variant check as <see cref="WallMountAngleRad"/>.</summary>
    protected override ScribeBoxTuningTarget? TuningTarget
    {
        get
        {
            string? orientation = Block?.Variant["orientation"];
            return orientation is null or "up" ? ScribeBoxTuningTarget.Inbox : ScribeBoxTuningTarget.InboxWall;
        }
    }

    // ── Mixed restricted/open inventory ──────────────────────────────────────
    // The first RestrictedSlotCount slots accept only Scribe items (ItemSlotScribeDocument), the rest
    // accept anything. Lazy-init/persistence/packet-routing mirror BlockEntityScriptorium's inventory.

    /// <summary>12 slots: indices 0-7 are Scribe-items-only, 8-11 are open (see <see cref="EnsureInventory"/>).
    /// Public so <see cref="GuiDialogScribeInbox"/> and the Atlas tests can reference the split.</summary>
    public const int SlotCount = 12;

    /// <summary>Slots below this index accept only Scribe items (<see cref="ItemSlotScribeDocument"/>); the rest are open.</summary>
    public const int RestrictedSlotCount = 8;

    /// <summary>Tree sub-key for the inventory; a tree lacking it loads with empty slots.</summary>
    private const string InventoryTreeKey = "inboxInventory";

    /// <summary>Created lazily via <see cref="EnsureInventory"/> because chunk-load runs FromTreeAttributes
    /// first while a fresh place runs Initialize first.</summary>
    private InventoryGeneric? inventory;

    /// <summary>The Inbox's mixed restricted/open inventory (the Inbox Inventory tab watches this).</summary>
    public InventoryGeneric Inventory
    {
        get
        {
            EnsureInventory();
            return inventory!;
        }
    }

    private void EnsureInventory()
    {
        inventory ??= new InventoryGeneric(SlotCount, null, null,
            (slotId, self) => slotId < RestrictedSlotCount
                ? new ItemSlotScribeDocument(self)
                : new ItemSlot(self));
    }

    public override void Initialize(ICoreAPI api)
    {
        EnsureInventory();
        base.Initialize(api);

        // Bind the inventory to the block-entity packet channel. Without LateInitialize + Pos, LibGUI's
        // SlotController silently drops every slot click ("Skipped slot activation … not network-ready").
        inventory!.LateInitialize("scribeinbox-" + Pos, api);
        inventory.Pos = Pos;

        // Inbox-instance presence signal, in addition to the base class's coarse OnAssignmentParticleTick.
        if (api is ICoreClientAPI)
        {
            RegisterGameTickListener(OnInboxNoticeParticleTick, InboxNoticeParticleTickIntervalMs);
        }
    }

    /// <summary>Matches the base class's <c>AssignmentParticleTickIntervalMs</c> so the two ambient fields look consistent.</summary>
    private const int InboxNoticeParticleTickIntervalMs = 1500;

    /// <summary>True when <paramref name="slot"/> holds a sealed Task Notice addressed to
    /// <paramref name="targetUid"/> — the trigger shared by this block's particle tick and
    /// <see cref="GuiDialogScribeInbox"/>'s tab/slot shimmer. <see cref="ScribeAssignment.Seen"/> is not used:
    /// it flips true when the player opens ANY Inbox view, before the notice is resolved. Accept/Decline
    /// consumes the notice item, so this turns false on its own.</summary>
    internal static bool HoldsUndiscoveredNoticeFor(ItemSlot slot, string targetUid) =>
        slot.Itemstack?.Collectible is ItemScribeTaskNotice
        && ItemScribeTaskNotice.IsSealed(slot.Itemstack)
        && ScribeDocumentAttributes.TryReadFrom(slot.Itemstack, out var doc) && doc is not null
        && doc.Blocks.Any(b => b.Assignment?.TargetPlayerUid == targetUid);

    /// <summary>Previous tick's active state, so a fresh entry requests a seed burst.</summary>
    private bool inboxNoticeParticlesWereActive;

    /// <summary>Client-side periodic check: spawns a mote batch when one of THIS Inbox's restricted slots
    /// holds a sealed notice addressed to the local player within
    /// <see cref="ScribeAssignmentParticleEmitter.DetectionRadius"/>. Like the base class's tick, but keyed
    /// off this block's inventory rather than the player-wide unseen flag.</summary>
    private void OnInboxNoticeParticleTick(float dt)
    {
        if (Api is not ICoreClientAPI capi) return;

        var emitter = ModSystem?.ParticleEmitter;

        string? uid = capi.World.Player?.PlayerUID;
        bool active = emitter is not null && uid is not null
            && Enumerable.Range(0, RestrictedSlotCount).Any(i => HoldsUndiscoveredNoticeFor(Inventory[i], uid));
        if (active)
        {
            var player = capi.World.Player?.Entity;
            active = player is not null
                && Pos.DistanceTo(player.Pos.X, player.Pos.Y, player.Pos.Z) <= emitter!.DetectionRadius;
        }

        if (!active)
        {
            inboxNoticeParticlesWereActive = false;
            return;
        }

        emitter!.SpawnAt(capi, Pos, seedBurst: !inboxNoticeParticlesWereActive);
        inboxNoticeParticlesWereActive = true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        EnsureInventory();
        var invTree = new TreeAttribute();
        inventory!.ToTreeAttributes(invTree);
        tree[InventoryTreeKey] = invTree;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        EnsureInventory();
        base.FromTreeAttributes(tree, worldForResolving);
        // A tree without the inventory sub-tree leaves the slots empty.
        if (tree.GetTreeAttribute(InventoryTreeKey) is { } invTree)
        {
            inventory!.FromTreeAttributes(invTree);
        }
    }

    /// <summary>Standard vanilla container packet flow (mirrors <c>BlockEntityScriptorium</c>): packet id
    /// &lt; 1000 goes to the inventory's network util; 1000/1001 open/close it for the player. Rides the
    /// block-entity packet channel, NOT the mod's "scribe" channel.</summary>
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (packetid < 1000)
        {
            Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            MarkDirty(true);
            return;
        }

        if (packetid == 1000)
        {
            player.InventoryManager?.OpenInventory(Inventory);
        }
        else if (packetid == 1001)
        {
            player.InventoryManager?.CloseInventory(Inventory);
        }
    }

    /// <summary>Drops stored items when the block is broken so a stored Task Notice is never destroyed.
    /// Server-only; this BE is still alive here. The block's own document is carried onto the item by
    /// <see cref="BlockScribeWritingStation.GetDrops"/>.</summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api is ICoreServerAPI)
        {
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }
}
