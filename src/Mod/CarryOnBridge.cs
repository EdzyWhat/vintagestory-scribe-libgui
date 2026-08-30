using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Scribe;

/// <summary>
/// Zero-compile-dependency bridge to the CarryOn mod family (modid <c>carryon</c>), so a Notebook
/// or Tablet stored inside a block the player is currently carrying (e.g. a chest on their back)
/// still participates in Death/PvpKill/TemporalStorm history recording. See
/// <c>fix-carried-notebook-detection</c>'s design.md for the full rationale.
///
/// CarryOn freezes a carried container's entire block-entity state — including its own inventory
/// — into a raw <see cref="ITreeAttribute"/> (<c>CarriedBlock.BlockEntityData</c>) rather than a
/// live <see cref="IInventory"/>, so it needs an entirely separate detection path from
/// <c>ScribeModSystem.History.cs</c>'s <see cref="InventoryBasePlayer"/> scan. Project convention
/// (see this repo's CLAUDE.md "no new mod dependencies" guardrail) forbids a compile-time reference
/// to CarryOnLib, so every CarryOn-specific type is reached only via reflection: <c>IsModEnabled</c>
/// and <c>GetModSystem(string)</c> are vanilla API, and once a <c>CarryOn.API.Common.Models.CarriedBlock</c>
/// instance is in hand its <c>ItemStack</c> and <c>BlockEntityData</c> properties are themselves
/// vanilla types (<see cref="ItemStack"/>, <see cref="ITreeAttribute"/>), so the recursive
/// notebook-finding walk below is ordinary, fully-typed vanilla code — reflection is isolated to the
/// handful of members touched in <see cref="EnsureInitialized"/>/<see cref="FindCarriedNotebooks"/>/
/// <see cref="TrySetCarried"/>.
///
/// Every reflected call is wrapped in try/catch. The first failure (CarryOn not installed, or a
/// future version whose API shape has moved) permanently disables the bridge for the rest of the
/// session after a single log line — this only runs on rare events (death, PvP kill, storm rising
/// edge), never per-tick, so there is no per-event exception risk to guard against beyond the first.
/// </summary>
internal sealed class CarryOnBridge
{
    private const string CarryOnModId = "carryon";
    private const string CarryOnLibSystemTypeName = "CarryOn.CarryOnLib.CarryOnLibSystem";

    private readonly ICoreServerAPI _sapi;
    private object? _carryManager;
    private MethodInfo? _getAllCarriedMethod;
    private MethodInfo? _setCarriedMethod;
    private bool _initialized;
    private bool _disabled;

    public CarryOnBridge(ICoreServerAPI sapi) => _sapi = sapi;

    /// <summary>Whether a usable CarryOn install was found. False both when CarryOn isn't installed
    /// and when reflection failed against an installed CarryOn — callers treat both the same way:
    /// skip this detection path entirely.</summary>
    private bool IsActive => !_disabled && _carryManager is not null;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            if (!_sapi.ModLoader.IsModEnabled(CarryOnModId)) return;

            var modSystem = _sapi.ModLoader.GetModSystem(CarryOnLibSystemTypeName);
            if (modSystem is null) { Disable("CarryOnLibSystem not found"); return; }

            var manager = modSystem.GetType().GetProperty("CarryManager")?.GetValue(modSystem);
            if (manager is null) { Disable("CarryManager property missing or null"); return; }

            var managerType = manager.GetType();
            _getAllCarriedMethod = managerType.GetMethod("GetAllCarried", new[] { typeof(Entity) });
            _setCarriedMethod    = managerType.GetMethods()
                .FirstOrDefault(m => m.Name == "SetCarried" && m.GetParameters().Length == 4);
            if (_getAllCarriedMethod is null || _setCarriedMethod is null)
            {
                Disable("GetAllCarried/SetCarried not found on ICarryManager");
                return;
            }

            _carryManager = manager;
        }
        catch (Exception ex)
        {
            Disable("initialization threw: " + ex.Message);
        }
    }

    private void Disable(string reason)
    {
        if (_disabled) return;
        _disabled = true;
        _carryManager = null;
        _sapi.Logger.Notification(
            "[scribe] CarryOn integration inactive ({0}) — notebooks inside carried containers will not record history.",
            reason);
    }

    /// <summary>Finds every Notebook/Tablet stack (recursively, including inside attached blocks —
    /// e.g. a sign on a carried chest) inside anything <paramref name="entity"/> is currently
    /// carrying via CarryOn. Never throws; a reflection failure disables the bridge and yields
    /// nothing, for this call and every one after it this session.</summary>
    public IEnumerable<CarriedNotebookRef> FindCarriedNotebooks(Entity entity)
    {
        EnsureInitialized();
        if (!IsActive) return Enumerable.Empty<CarriedNotebookRef>();

        var results = new List<CarriedNotebookRef>();
        try
        {
            if (_getAllCarriedMethod!.Invoke(_carryManager, new object[] { entity }) is not IEnumerable carriedBlocks)
                return results;

            foreach (var carriedBlockObj in carriedBlocks)
            {
                if (carriedBlockObj is null) continue;
                CollectFrom(carriedBlockObj, carriedBlockObj, entity, results);
            }
        }
        catch (Exception ex)
        {
            Disable("GetAllCarried threw: " + ex.Message);
        }
        return results;
    }

    /// <summary>Re-persists and re-syncs a mutated carried-block graph through CarryOn's own
    /// SetCarried, so an in-place edit to one of its nested trees (via
    /// <see cref="CarriedNotebookRef.FlushHistory"/>) actually sticks rather than being silently
    /// discarded on the next save/sync. Always passes the OUTERMOST carried block for a given
    /// CarrySlot — SetCarried has no way to address a nested attached block independently.</summary>
    private void TrySetCarried(Entity entity, object rootCarriedBlock)
    {
        if (!IsActive) return;
        try
        {
            _setCarriedMethod!.Invoke(_carryManager, new object?[] { entity, rootCarriedBlock, null, true });
        }
        catch (Exception ex)
        {
            Disable("SetCarried threw: " + ex.Message);
        }
    }

    /// <summary>Walks one carried block's own frozen data plus every attached block's, recursively
    /// (an attached block can itself have further attachments). All persistence for anything found
    /// anywhere in this graph routes back through <paramref name="rootCarriedBlock"/> — the single
    /// object CarryOn's SetCarried actually knows how to re-save.</summary>
    private void CollectFrom(object rootCarriedBlock, object carriedBlockObj, Entity entity, List<CarriedNotebookRef> results)
    {
        if (GetProp(carriedBlockObj, "BlockEntityData") is ITreeAttribute tree)
            WalkTree(tree, rootCarriedBlock, entity, results);

        if (GetProp(carriedBlockObj, "AttachedBlocks") is not IEnumerable attachedBlocks) return;
        foreach (var attached in attachedBlocks)
        {
            if (attached is null) continue;
            if (GetProp(attached, "CarriedBlock") is { } nestedCarriedBlock)
                CollectFrom(rootCarriedBlock, nestedCarriedBlock, entity, results);
        }
    }

    private static object? GetProp(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj);

    /// <summary>Generic recursive search for a Notebook/Tablet ItemStack inside a frozen
    /// block-entity tree — ordinary vanilla-typed code, no reflection. There is no single universal
    /// "the inventory" key (each container BlockEntity serializes its own slots), so this recurses
    /// into every nested <see cref="ITreeAttribute"/> value and checks every
    /// <see cref="ItemstackAttribute"/> leaf — matching how vanilla's own
    /// <c>InventoryBase.SlotsToTreeAttributes</c> nests slots (an inventory sub-tree, itself holding
    /// an individually-keyed <c>"slots"</c> sub-tree of per-slot <c>ItemstackAttribute</c>s), without
    /// needing to know either key name in advance.</summary>
    private void WalkTree(ITreeAttribute tree, object rootCarriedBlock, Entity entity, List<CarriedNotebookRef> results)
    {
        foreach (var kv in tree)
        {
            if (kv.Value is ItemstackAttribute { value: { } stack })
            {
                stack.ResolveBlockOrItem(entity.World);
                if (stack.Collectible is IScribeDocumentItem)
                    results.Add(new CarriedNotebookRef(this, entity, rootCarriedBlock, tree, kv.Key, stack));
            }
            else if (kv.Value is ITreeAttribute nested)
            {
                WalkTree(nested, rootCarriedBlock, entity, results);
            }
        }
    }

    /// <summary>A Notebook/Tablet found inside a CarryOn-carried block, with everything needed to
    /// record and persist a history entry on it. Deliberately NOT a <see cref="NotebookHost"/>: that
    /// class assumes a live <see cref="ItemSlot"/> to mark dirty and push a network sync through, and
    /// there is no such slot here — the stack lives inside a frozen attribute tree, not an open
    /// inventory. Persistence instead goes through CarryOn's own SetCarried via
    /// <see cref="CarryOnBridge.TrySetCarried"/>.</summary>
    public sealed class CarriedNotebookRef : IHistoryRecordable
    {
        private readonly CarryOnBridge _bridge;
        private readonly Entity _entity;
        private readonly object _rootCarriedBlock;
        private readonly ITreeAttribute _parentTree;
        private readonly string _key;
        private readonly ItemStack _stack;

        internal CarriedNotebookRef(
            CarryOnBridge bridge, Entity entity, object rootCarriedBlock,
            ITreeAttribute parentTree, string key, ItemStack stack)
        {
            _bridge = bridge;
            _entity = entity;
            _rootCarriedBlock = rootCarriedBlock;
            _parentTree = parentTree;
            _key = key;
            _stack = stack;
            History = HistoryStore.Deserialize(stack.Attributes.GetBytes("scribeHistory"));
        }

        public HistoryStore History { get; }

        /// <summary>Writes the mutated history back into the stack, writes the stack back into its
        /// parent tree (never assume in-place mutation of a retrieved ItemStack persists — the same
        /// explicit write-back discipline <see cref="NotebookHost.Flush"/> follows), then re-persists
        /// and re-syncs the whole carried-block graph through CarryOn's own SetCarried so the change
        /// actually sticks rather than being silently dropped on the next save.</summary>
        public void FlushHistory()
        {
            _stack.Attributes.SetBytes("scribeHistory", History.Serialize());
            _parentTree.SetItemstack(_key, _stack);
            _bridge.TrySetCarried(_entity, _rootCarriedBlock);
        }
    }
}
