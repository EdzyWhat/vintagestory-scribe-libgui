using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried tablet item, the early-game scratch-tier writing surface. One class backs all tablet
/// variants (the <c>material</c> axis in <c>itemtypes/scribetablet.json</c>); they differ in art and recipe, not
/// behavior. Modeled on <see cref="ItemScribeNotebook"/>: MaxStackSize 1, right-click opens, shift passes
/// through to ground storage, and a server-side <c>Crafted</c> history entry. The document persists on the
/// ItemStack via <see cref="ScribeDocumentAttributes"/> and a <see cref="TabletHost"/> that caps the tier at
/// 10 task blocks and 1 pin; right-click opens <see cref="GuiDialogScribeTablet"/>.
/// </summary>
public class ItemScribeTablet : Item, IScribeDocumentItem
{
    /// <summary>Held-interaction help for a WET (editable) tablet: open, quick-add, and ground-place.</summary>
    private WorldInteraction[] _wetInteractions = System.Array.Empty<WorldInteraction>();

    /// <summary>Held-interaction help for a HARD/FIRED (read-only) tablet: open, plus a water-aim "soften" hint
    /// (no quick-add, since it cannot take text).</summary>
    private WorldInteraction[] _readonlyInteractions = System.Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        MaxStackSize = 1;

        if (api.Side != EnumAppSide.Client) return;

        var open = new WorldInteraction
        {
            ActionLangCode = "scribe:itemhelp-scribetablet-open",
            MouseButton = EnumMouseButton.Right,
        };
        var quickAdd = new WorldInteraction
        {
            ActionLangCode = "scribe:itemhelp-scribetablet-quickadd",
            HotKeyCode = "shift",
            MouseButton = EnumMouseButton.Right,
        };
        var soften = new WorldInteraction
        {
            ActionLangCode = "scribe:itemhelp-scribetablet-quench",
            HotKeyCode = "shift",
            MouseButton = EnumMouseButton.Right,
        };

        // The Ctrl+Shift "place on ground" hint comes from the base GroundStorable behavior (ctrlKey:true in the item JSON).
        _wetInteractions = ObjectCacheUtil.GetOrCreate(api, "scribeTabletWetInteractions",
            () => new[] { open, quickAdd });
        _readonlyInteractions = ObjectCacheUtil.GetOrCreate(api, "scribeTabletReadonlyInteractions",
            () => new[] { open, soften });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        // Wet advertises quick-add; hard/fired advertises water-soften instead.
        var interactions = IsEditable(inSlot.Itemstack) ? _wetInteractions : _readonlyInteractions;
        return interactions.Append(base.GetHeldInteractionHelp(inSlot));
    }

    /// <summary>Appends the stored document's title (or an untitled placeholder) to the tooltip. A never-opened
    /// tablet has no document attribute, so the placeholder shows.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        string? title = inSlot.Itemstack is { } stack
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            ? doc.Title
            : null;
        dsc.AppendLine(ScribeTooltip.FormatTitleLine(title));

        // Only a hard tablet gets the rehydrate hint: wet is already editable, fired is permanent.
        if (ReadHard(inSlot.Itemstack))
            dsc.AppendLine(Vintagestory.API.Config.Lang.Get("scribe:tooltip-tablet-hard-rehydrate"));
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        // Ctrl+Shift+right-click: ground placement via the base behaviors. Checked BEFORE the Shift branch
        // because Ctrl+Shift also sets ShiftKey.
        if (byEntity.Controls.CtrlKey && byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        // Shift+right-click (no Ctrl): quench if aimed at a water container; otherwise fall through to quick-add.
        if (byEntity.Controls.ShiftKey && TryQuench(slot, byEntity, blockSel))
        {
            handling = EnumHandHandling.PreventDefault;
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        // Plain right-click opens the dialog; Shift+right-click off water also quick-adds (refused on a read-only tablet).
        handling = EnumHandHandling.PreventDefault;
        OpenTabletDialog(slot, capi, quickAdd: byEntity.Controls.ShiftKey);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;
        if (outputSlot.Itemstack is null) return; // always set for a crafting output; guard is defensive

        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (api.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

        var sapi = (ICoreServerAPI)api;
        string date = NotebookHost.FormatDate(sapi);
        var history = HistoryStore.Deserialize(outputSlot.Itemstack.Attributes.GetBytes("scribeHistory"));
        history.TryAddEntry(new HistoryEntry
        {
            Kind            = HistoryEventKind.Crafted,
            ActorName       = playerName,
            InGameDate      = date,
            InGameTimestamp = sapi.World.Calendar.TotalDays,
        });
        outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", history.Serialize());
    }

    /// <summary>Parses the <c>material</c> variant into the base clay/wax material and its <see cref="TabletState"/>.
    /// State lives in the VARIANT (bare <c>clay-red</c> = wet; <c>-hard</c>/<c>-fired</c> siblings), not a stack
    /// attribute. An unrecognized or absent variant resolves to red + wet.</summary>
    private static (string material, TabletState state) ResolveMaterialState(ItemStack? stack)
    {
        string variant = stack?.Collectible?.Variant?["material"] ?? "clay-red";
        if (variant.EndsWith("-fired")) return (variant[..^"-fired".Length], TabletState.Fired);
        if (variant.EndsWith("-hard"))  return (variant[..^"-hard".Length],  TabletState.Hard);
        return (variant, TabletState.Wet);
    }

    /// <summary>Whether the tablet is fired (a <c>-fired</c> variant): permanently read-only.</summary>
    public static bool ReadFired(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Fired;

    /// <summary>Whether the tablet is hard (a <c>-hard</c> variant): read-only but reversible by water. Wax never hardens.</summary>
    public static bool ReadHard(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Hard;

    /// <summary>Whether the tablet may be edited (neither hard nor fired). The single resolve point the dialog and document policy key off.</summary>
    public static bool IsEditable(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Wet;

    // Dry (wet → hard) via the native Harden transition
    // scribetablet.json declares a Harden for clay variants only. The overrides below suppress it once hard or
    // fired, and carry the document onto the output (the base rebuilds a fixed stack that drops attributes).

    /// <summary>Returns <c>null</c> once hard or fired, which makes the engine skip transition ticking for this stack (VSAPI-NOTES.md).</summary>
    public override TransitionableProperties[]? GetTransitionableProperties(IWorldAccessor world, ItemStack itemstack, Entity forEntity)
    {
        if (ReadHard(itemstack) || ReadFired(itemstack)) return null;
        return base.GetTransitionableProperties(world, itemstack, forEntity);
    }

    /// <summary>Carries the document through hardening: the base builds the <c>-hard</c> output but drops the
    /// input's attributes, so the document/history bytes are copied on. Other transitions pass through.</summary>
    public override ItemStack? OnTransitionNow(ItemSlot slot, TransitionableProperties props)
    {
        var output = base.OnTransitionNow(slot, props);
        if (props.Type != EnumTransitionType.Harden || output is null) return output;

        CarryStackData(slot.Itemstack, output);
        return output;
    }

    // Fire (unfired → fired) via the firepit combustible/smelt path

    /// <summary>Returns <c>null</c> for a fired tablet so <c>CanSmelt</c> and the firepit heat checks (all routed here; VSAPI-NOTES.md) treat it as non-combustible.</summary>
    public override CombustibleProperties? GetCombustibleProperties(IWorldAccessor world, ItemStack itemstack, Vintagestory.API.MathTools.BlockPos pos)
    {
        if (ReadFired(itemstack)) return null;
        return base.GetCombustibleProperties(world, itemstack, pos);
    }

    /// <summary>Carries the document through firing. The input's bytes are captured BEFORE <c>base.DoSmelt</c>
    /// (which clears the input slot), then copied onto the <c>-fired</c> output the base builds.</summary>
    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        // Capture BEFORE base clears the input.
        var source = inputSlot.Itemstack;
        byte[]? docBytes = source?.Attributes.GetBytes(ScribeDocumentAttributes.DocumentAttributeKey);
        byte[]? historyBytes = source?.Attributes.GetBytes("scribeHistory");

        base.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot);

        if (outputSlot.Itemstack is not { } fired) return;
        if (docBytes is not null) fired.Attributes.SetBytes(ScribeDocumentAttributes.DocumentAttributeKey, docBytes);
        if (historyBytes is not null) fired.Attributes.SetBytes("scribeHistory", historyBytes);
    }

    // Rehydrate (hard → wet) on water exposure, torch-style. Both paths run server-side and ride the existing
    // stack-attribute persistence. Fired tablets never soften.

    /// <summary>Dropped-in-water case: softens a hard tablet floating in water (server-only; VSAPI-NOTES.md).</summary>
    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);
        if (api.Side != EnumAppSide.Server) return;
        if (entityItem.Swimming && Soften(entityItem.Itemstack, api.World) is { } softened)
        {
            entityItem.Itemstack = softened;
            entityItem.WatchedAttributes.MarkPathDirty("itemstack");
        }
    }

    /// <summary>Held-while-swimming case: softens the active held tablet (server-only; VSAPI-NOTES.md; mirrors <c>BlockTorch.OnHeldIdle</c>).</summary>
    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {
        base.OnHeldIdle(slot, byEntity);
        if (byEntity.World.Side != EnumAppSide.Server) return;
        if (byEntity.Swimming && Soften(slot.Itemstack, byEntity.World) is { } softened)
        {
            slot.Itemstack = softened;
            slot.MarkDirty();
        }
    }

    /// <summary>Deliberate-quench case: shift+right-click a water-filled liquid container to soften a held tablet.
    /// Returns <c>true</c> when a water container was aimed at (so the caller sets PreventDefault, stopping the
    /// container's own fill/pour and ground-storage passthrough), even for a fired no-op; <c>false</c> otherwise.
    /// The swap happens server-side; both sides play the container's water sound.</summary>
    private bool TryQuench(ItemSlot slot, EntityAgent byEntity, BlockSelection? blockSel)
    {
        if (blockSel is null) return false;
        var world = byEntity.World;
        if (world.BlockAccessor.GetBlock(blockSel.Position) is not BlockLiquidContainerBase container) return false;
        if (!IsWaterContent(container, blockSel.Position)) return false;

        // Aimed at a water container: the gesture is ours regardless of state.
        if (world.Side == EnumAppSide.Server && Soften(slot.Itemstack, world) is { } softened)
        {
            slot.Itemstack = softened;
            slot.MarkDirty();
        }
        // Reuse the container's own fill sound so it matches whatever liquid it holds.
        var sound = container.GetContentProps(blockSel.Position)?.FillSound;
        if (sound is not null) world.PlaySoundAt(sound, blockSel.Position, 0.5, randomizePitch: true);
        return true;
    }

    /// <summary>Whether a liquid container currently holds any water variant (content code ends in <c>waterportion</c>).</summary>
    private static bool IsWaterContent(BlockLiquidContainerBase container, Vintagestory.API.MathTools.BlockPos pos)
    {
        var content = container.GetContent(pos);
        string? code = content?.Collectible?.Code?.Path;
        return code is not null && code.EndsWith("waterportion");
    }

    /// <summary>Rehydrate on water: hard swaps to its wet sibling (document/history carried; the fresh stack
    /// re-seeds the clock); wet drops <c>transitionstate</c> so the harden clock restarts (VSAPI-NOTES.md);
    /// fired never changes. Returns the stack to write back, or <c>null</c> when nothing changed.</summary>
    private static ItemStack? Soften(ItemStack? stack, IWorldAccessor world)
    {
        if (stack is null) return null;
        var state = ResolveMaterialState(stack).state;
        if (state == TabletState.Fired) return null;
        if (state == TabletState.Hard) return BuildStateVariant(stack, TabletState.Wet, world);

        // Already wet: reset the clock in place if it progressed. Skip near-zero clocks (engine advance threshold 0.05h) so idle-in-water does not dirty the slot every tick.
        return TryResetHardenClock(stack) ? stack : null;
    }

    private static float PeekTransitionedHours(ItemStack? stack)
    {
        if (stack?.Attributes["transitionstate"] is ITreeAttribute tree
            && tree["transitionedHours"] is FloatArrayAttribute arr
            && arr.value.Length > 0)
            return arr.value[0];
        return 0f;
    }

    /// <summary>Drops <c>transitionstate</c> so the next tick re-seeds the Harden clock. False when there is
    /// nothing to reset (no tree, or under the engine's 0.05h threshold), avoiding per-tick MarkDirty.</summary>
    private static bool TryResetHardenClock(ItemStack stack)
    {
        if (stack.Attributes["transitionstate"] is not ITreeAttribute) return false;
        if (PeekTransitionedHours(stack) < 0.05f) return false;
        stack.Attributes.RemoveAttribute("transitionstate");
        return true;
    }

    /// <summary>Builds a fresh tablet stack in the <paramref name="target"/> state with the source's document
    /// and history carried over; shared by <see cref="Soften"/> and the <c>/scribe tablet</c> dev command.
    /// Returns <c>null</c> when the sibling variant does not exist (e.g. wax has no <c>-hard</c>/<c>-fired</c>).
    /// State-AGNOSTIC: it builds wet/hard FROM a fired stack too; the permanence rule lives only in
    /// <see cref="Soften"/>.</summary>
    public static ItemStack? BuildStateVariant(ItemStack? current, TabletState target, IWorldAccessor world)
    {
        if (current?.Collectible is null) return null;
        var (material, _) = ResolveMaterialState(current);
        string variant = target switch
        {
            TabletState.Hard  => material + "-hard",
            TabletState.Fired => material + "-fired",
            _                 => material, // Wet = the bare clay/wax variant
        };

        var item = world.GetItem(current.Collectible.CodeWithVariant("material", variant));
        if (item is null) return null;

        var stack = new ItemStack(item);
        CarryStackData(current, stack);
        return stack;
    }

    /// <summary>Copies the document + history bytes onto <paramref name="to"/>; null-safe (no document copies nothing).</summary>
    private static void CarryStackData(ItemStack? from, ItemStack to)
    {
        if (from is null) return;
        if (from.Attributes.GetBytes(ScribeDocumentAttributes.DocumentAttributeKey) is { } docBytes)
            to.Attributes.SetBytes(ScribeDocumentAttributes.DocumentAttributeKey, docBytes);
        if (from.Attributes.GetBytes("scribeHistory") is { } historyBytes)
            to.Attributes.SetBytes("scribeHistory", historyBytes);
    }

    /// <summary>Opens the tablet dialog for the Handbook "Add to Scribe" fallback. A read-only tablet still
    /// opens, but its edit request no-ops.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi)
        => OpenTabletDialog(slot, capi);

    /// <summary>Writeable only while WET; keyed off <see cref="IsEditable"/> so it cannot drift from real editability.</summary>
    public bool IsSlotWriteable(ItemSlot slot) => IsEditable(slot.Itemstack);

    /// <summary>The item-level policy: <see cref="Scribe.Core.ScribeDocumentPolicy.Tablet"/> (10 task blocks) when
    /// WET, <see cref="Scribe.Core.ScribeDocumentPolicy.UneditableTablet"/> otherwise. Mirrors
    /// <see cref="TabletHost.Policy"/>.</summary>
    public Scribe.Core.ScribeDocumentPolicy DocumentPolicy(ItemSlot slot) =>
        IsEditable(slot.Itemstack)
            ? Scribe.Core.ScribeDocumentPolicy.Tablet
            : Scribe.Core.ScribeDocumentPolicy.UneditableTablet;

    private ScribeDialogBase OpenTabletDialog(ItemSlot slot, ICoreClientAPI capi, bool quickAdd = false)
    {
        // Backdrop/theme/glow key off the BASE material and the state, both parsed from the variant. Pass the
        // resolved base material, not Variant["material"] (which includes the -hard/-fired suffix).
        var stack = slot.Itemstack;
        var (material, state) = ResolveMaterialState(stack);
        var host = new TabletHost(slot,
            ScribeBackdrops.ForTablet(material, state), material);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this tablet (it never sees client-only dialog opens) for the one-time PickedUp entry.
        modSystem.NotifyServerNotebookOpened(host.Document.DocId, slot);
        // Remember this as the last-opened Scribe item.
        modSystem.NoteScribeItemDialogOpened(host.Document.DocId);

        // The state drives editability (only WET edits) and the read-only empty-state message.
        var dialog = new GuiDialogScribeTablet(host, capi, material, state);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
        // Quick-add: a WET tablet's ctor already entered the editor; the seam no-ops on a read-only tablet.
        if (quickAdd) dialog.QuickAddTopTask();
        return dialog;
    }
}
