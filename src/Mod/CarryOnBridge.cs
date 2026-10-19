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
/// Zero-compile-dependency bridge to the CarryOn mod family (modid <c>carryon</c>), so a Notebook or
/// Tablet inside a block the player is carrying still participates in Death/PvpKill/TemporalStorm history.
///
/// CarryOn freezes a carried container's block-entity state, including its inventory, into a raw
/// <see cref="ITreeAttribute"/> (<c>CarriedBlock.BlockEntityData</c>) rather than a live
/// <see cref="IInventory"/>, so this needs a detection path separate from the <see cref="InventoryBasePlayer"/>
/// scan in <c>ScribeModSystem.History.cs</c>. The "no new mod dependencies" guardrail forbids a
/// compile-time reference to CarryOnLib, so CarryOn types are reached only via reflection (confined to
/// <see cref="EnsureInitialized"/>/<see cref="FindCarriedNotebooks"/>/<see cref="TrySetCarried"/>); the
/// nested tree walk uses vanilla types.
///
/// Every reflected call is wrapped in try/catch. The first failure (CarryOn missing, or an API shape that
/// moved) permanently disables the bridge for the session after one log line. It only runs on rare events
/// (death, PvP kill, storm rising edge), never per-tick.
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

    /// <summary>Whether a usable CarryOn install was found. False both when CarryOn isn't installed and
    /// when reflection failed; callers skip this detection path either way.</summary>
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

    /// <summary>Finds every Notebook/Tablet stack (recursively, including inside attached blocks) inside
    /// anything <paramref name="entity"/> is carrying via CarryOn. Never throws; a reflection failure
    /// disables the bridge and yields nothing from then on.</summary>
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

    /// <summary>Re-persists and re-syncs a mutated carried-block graph through CarryOn's SetCarried so an
    /// in-place edit to a nested tree (via <see cref="CarriedNotebookRef.FlushHistory"/>) sticks. Always
    /// passes the OUTERMOST carried block for a CarrySlot: SetCarried cannot address a nested block.</summary>
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

    /// <summary>Walks one carried block's frozen data plus every attached block's, recursively. Persistence
    /// for anything found routes back through <paramref name="rootCarriedBlock"/>, the only object
    /// SetCarried can re-save.</summary>
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

    /// <summary>Recursively searches a frozen block-entity tree for Notebook/Tablet ItemStacks. Container
    /// block entities serialize their slots under differing keys, so this recurses into every nested
    /// <see cref="ITreeAttribute"/> and checks every <see cref="ItemstackAttribute"/> leaf rather than
    /// relying on key names.</summary>
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

    /// <summary>A Notebook/Tablet found inside a CarryOn-carried block. Deliberately NOT a
    /// <see cref="NotebookHost"/>: that assumes a live <see cref="ItemSlot"/> to mark dirty and sync, but
    /// this stack lives in a frozen attribute tree. Persistence goes through
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

        /// <summary>Writes the mutated history into the stack and the stack back into its parent tree
        /// (in-place mutation of a retrieved ItemStack is not assumed to persist, as in
        /// <see cref="NotebookHost.Flush"/>), then re-persists the whole carried-block graph via SetCarried.</summary>
        public void FlushHistory()
        {
            _stack.Attributes.SetBytes("scribeHistory", History.Serialize());
            _parentTree.SetItemstack(_key, _stack);
            _bridge.TrySetCarried(_entity, _rootCarriedBlock);
        }
    }
}
