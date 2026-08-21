using System.Text;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.API.Util;

namespace Scribe;

/// <summary>
/// A player-carried tablet item — the early-game "scratch tier" writing surface. One class backs all
/// tablet variants (the <c>material</c> variant axis in <c>itemtypes/scribetablet.json</c>:
/// <c>clay-red</c>/<c>clay-blue</c>/<c>clay-fire</c>/<c>wax</c> — one discrete registered item per clay
/// type, so each gets its own handbook entry and recipe); they differ only in art and recipe, not
/// behavior. Modeled on <see cref="ItemScribeNotebook"/>:
/// MaxStackSize 1, right-click to open (shift passes through to ground storage), a title tooltip line,
/// and a server-side <c>Crafted</c> history entry. NO stylus-in-offhand edit gate (explicitly deferred).
///
/// <para>The document persists on the ItemStack exactly like the notebook — pure reuse of
/// <see cref="ScribeDocumentAttributes"/> — via a <see cref="TabletHost"/> that caps the tier at 10 task
/// blocks and 1 pin. Right-click opens the bespoke <see cref="GuiDialogScribeTablet"/> (add-tablet-dialog,
/// Proposal C): an always-edit, no-tabs surface with the earthen theme, the clay/wax backdrop matching the
/// item's material, and a cuneiform title banner over the inherited editable task list.</para>
/// </summary>
public class ItemScribeTablet : Item, IScribeDocumentItem
{
    /// <summary>Held-interaction help for a WET (editable) tablet: open, quick-add, and ground-place.</summary>
    private WorldInteraction[] _wetInteractions = System.Array.Empty<WorldInteraction>();

    /// <summary>Held-interaction help for a HARD/FIRED (read-only) tablet: open + ground-place, plus a
    /// water-aim "soften" hint that is only meaningful (and only reversible) on a HARD tablet. Quick-add is
    /// omitted since a read-only tablet cannot take new text.</summary>
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

        // The Ctrl+Shift "place on ground" hint comes from the base GroundStorable behavior itself
        // (the item JSON has ctrlKey:true), so neither array adds a redundant place hint.
        _wetInteractions = ObjectCacheUtil.GetOrCreate(api, "scribeTabletWetInteractions",
            () => new[] { open, quickAdd });
        _readonlyInteractions = ObjectCacheUtil.GetOrCreate(api, "scribeTabletReadonlyInteractions",
            () => new[] { open, soften });
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        // A wet tablet advertises quick-add; a hard/fired (read-only) tablet advertises the water-soften
        // gesture instead, since it can't take new text. Ground-place (Ctrl+Shift) shows for both.
        var interactions = IsEditable(inSlot.Itemstack) ? _wetInteractions : _readonlyInteractions;
        return interactions.Append(base.GetHeldInteractionHelp(inSlot));
    }

    /// <summary>Append the stored document's title (quoted, or an untitled placeholder) to the
    /// held/inventory tooltip. A never-opened tablet carries no document attribute yet, so
    /// <see cref="ScribeDocumentAttributes.TryReadFrom"/> returns false and the placeholder shows.</summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        string? title = inSlot.Itemstack is { } stack
            && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null
            ? doc.Title
            : null;
        dsc.AppendLine(ScribeTooltip.FormatTitleLine(title));

        // A hardened (dried-but-unfired) tablet is read-only yet reversible: water softens it back to the
        // editable wet variant. Surface that on the tooltip so the reversibility is discoverable. Only the
        // hard state gets the hint — a wet tablet is already editable, and a fired one is permanent.
        if (ReadHard(inSlot.Itemstack))
            dsc.AppendLine(Vintagestory.API.Config.Lang.Get("scribe:tooltip-tablet-hard-rehydrate"));
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (!firstEvent) return;
        // Ctrl+Shift+right-click: ground placement via base CollectibleBehaviors (including GroundStorable),
        // following the vanilla spear convention (add-unified-quick-add-interaction). Checked BEFORE the
        // Shift branch because Ctrl+Shift also has ShiftKey set; taking it here keeps ground placement off
        // the plain-Shift gestures below.
        if (byEntity.Controls.CtrlKey && byEntity.Controls.ShiftKey)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }
        // Shift+right-click (no Ctrl): quench a hard tablet against a water container if aimed at one; the
        // quench takes precedence ONLY when the aimed-at block is a water-filled liquid container
        // (tablet-clay-hardening). If NOT aimed at water, fall through to the unified quick-add gesture
        // instead of ground placement (add-unified-quick-add-interaction) — aim is the discriminator.
        if (byEntity.Controls.ShiftKey && TryQuench(slot, byEntity, blockSel))
        {
            handling = EnumHandHandling.PreventDefault;
            return;
        }
        if (byEntity.Api.Side != EnumAppSide.Client) return;
        if (byEntity.Api is not ICoreClientAPI capi) return;

        // Plain right-click opens the tablet dialog; Shift+right-click off water opens it AND quick-adds a
        // fresh empty top task (a no-op quick-add on a read-only hard/fired tablet, which never enters the
        // editor — the write is simply refused there).
        handling = EnumHandHandling.PreventDefault;
        OpenTabletDialog(slot, capi, quickAdd: byEntity.Controls.ShiftKey);
    }

    public override void OnCreatedByCrafting(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        base.OnCreatedByCrafting(allInputSlots, outputSlot, byRecipe);
        if (api.Side != EnumAppSide.Server) return;
        if (outputSlot.Itemstack is null) return; // always set for a crafting output; guard is defensive

        // Resolve the crafting player from the inventory's opener set.
        var playerUid = outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault();
        var playerName = (playerUid is not null
            ? (api.World.PlayerByUid(playerUid) as IServerPlayer)?.PlayerName
            : null) ?? "Unknown";

        var sapi = (ICoreServerAPI)api;
        string date = NotebookHost.FormatDate(sapi);
        var history = HistoryStore.Deserialize(outputSlot.Itemstack.Attributes.GetBytes("scribeHistory"));
        history.TryAddEntry(new HistoryEntry
        {
            Kind       = HistoryEventKind.Crafted,
            ActorName  = playerName,
            InGameDate = date,
        });
        outputSlot.Itemstack.Attributes.SetBytes("scribeHistory", history.Serialize());
    }

    /// <summary>Parse a tablet stack's <c>material</c> variant into its base clay/wax material (the color the
    /// backdrop/theme/glow key off) and its life-cycle <see cref="TabletState"/>. State is carried by the
    /// VARIANT, not a stack attribute (wire-tablet-clay-art-and-variants): the soft variant is the bare clay
    /// code (<c>clay-red</c>), and hardening/firing swap it to a <c>-hard</c>/<c>-fired</c> sibling
    /// (<c>clay-red-hard</c>/<c>clay-red-fired</c>). An unrecognized or absent variant resolves to red + wet,
    /// so a legacy or malformed stack always yields a valid, editable state (clay-wax-tablet-item: "consumers
    /// treat it as red + soft").</summary>
    private static (string material, TabletState state) ResolveMaterialState(ItemStack? stack)
    {
        string variant = stack?.Collectible?.Variant?["material"] ?? "clay-red";
        if (variant.EndsWith("-fired")) return (variant[..^"-fired".Length], TabletState.Fired);
        if (variant.EndsWith("-hard"))  return (variant[..^"-hard".Length],  TabletState.Hard);
        return (variant, TabletState.Wet);
    }

    /// <summary>Whether a tablet stack is fired: derived from its <c>material</c> variant (a <c>-fired</c>
    /// sibling), not a stack attribute (wire-tablet-clay-art-and-variants). A fired clay tablet is permanently
    /// read-only (tablet-firing).</summary>
    public static bool ReadFired(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Fired;

    /// <summary>Whether a tablet stack is hard (dried-but-unfired): derived from its <c>material</c> variant
    /// (a <c>-hard</c> sibling). A hard clay tablet is read-only but reversible — water exposure softens it
    /// back to the wet variant (tablet-clay-hardening). Wax never hardens (no <c>-hard</c> variant).</summary>
    public static bool ReadHard(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Hard;

    /// <summary>Whether a tablet stack may be edited: editable ⇔ its variant is a SOFT clay (or wax) state —
    /// neither hard nor fired. The single resolve point the dialog and the document policy both key off
    /// (tablet-firing Decision 5).</summary>
    public static bool IsEditable(ItemStack? stack) => ResolveMaterialState(stack).state == TabletState.Wet;

    // ── Dry (wet → hard) via the native Harden transition ──────────────────────────────────────────
    // The engine ticks transitionableProps server-side against world.Calendar.TotalHours; scribetablet.json
    // declares a Harden for the clay variants only. Two overrides layer on top of that vanilla machinery:
    // suppress the transition once the tablet is already hard or fired (so it never re-hardens or ticks a
    // fired tablet), and — because the base transition rebuilds a FIXED output stack that DROPS the input's
    // custom attributes — carry the document onto the hardened output and stamp hard=true.

    /// <summary>Suppress the <c>Harden</c> transition once the tablet is already hard or fired: returning
    /// <c>null</c> makes the engine skip transition ticking for this stack entirely (VSAPI-NOTES.md). A wet
    /// clay tablet still reports the JSON-declared props via <c>base</c>; wax declares none, so this is a
    /// no-op for it.</summary>
    public override TransitionableProperties[]? GetTransitionableProperties(IWorldAccessor world, ItemStack itemstack, Entity forEntity)
    {
        if (ReadHard(itemstack) || ReadFired(itemstack)) return null;
        return base.GetTransitionableProperties(world, itemstack, forEntity);
    }

    /// <summary>Carry the document through hardening. The base builds the fixed hardened output — the
    /// <c>-hard</c> sibling variant, per <c>transitionedStack</c>, which now CARRIES the hard state itself —
    /// but does NOT copy the input stack's attributes, so a plain-JSON Harden would yield a BLANK hard tablet.
    /// We let base build the output, then copy the input's document/history bytes onto it. No attribute stamp:
    /// the <c>-hard</c> variant is the hard state (wire-tablet-clay-art-and-variants). Only acts on
    /// <see cref="EnumTransitionType.Harden"/>; any other transition passes straight through.</summary>
    public override ItemStack? OnTransitionNow(ItemSlot slot, TransitionableProperties props)
    {
        var output = base.OnTransitionNow(slot, props);
        if (props.Type != EnumTransitionType.Harden || output is null) return output;

        // Guard nulls: a blank input yields a blank-but-hard output (no document to copy).
        CarryStackData(slot.Itemstack, output);
        return output;
    }

    // ── Fire (unfired → fired) via the firepit combustible/smelt path ──────────────────────────────
    // scribetablet.json declares combustibleProps for the clay variants only (smelt → same variant). Two
    // overrides mirror the Harden pair: block re-firing a fired tablet (null combustible → CanSmelt false),
    // and carry the document onto the fired output (DoSmelt drops attributes exactly like OnTransitionNow).

    /// <summary>Block re-firing an already-fired tablet: returning <c>null</c> makes <c>CanSmelt</c> (and the
    /// firepit's heat/melt checks, which all route through this virtual — VSAPI-NOTES.md) treat it as
    /// non-combustible, so a fired tablet can't be fired into a blank. An unfired tablet — wet OR hard —
    /// still reports the JSON-declared combustible via <c>base</c>; wax declares none.</summary>
    public override CombustibleProperties? GetCombustibleProperties(IWorldAccessor world, ItemStack itemstack, Vintagestory.API.MathTools.BlockPos pos)
    {
        if (ReadFired(itemstack)) return null;
        return base.GetCombustibleProperties(world, itemstack, pos);
    }

    /// <summary>Carry the document through firing. Like <see cref="OnTransitionNow"/>, the firepit builds a
    /// fixed smelted output — the <c>-fired</c> sibling variant, per <c>smeltedStack</c>, which now CARRIES
    /// the fired state itself — that drops the input's attributes, so we capture the input's bytes BEFORE
    /// <c>base.DoSmelt</c> (which decrements/clears the input slot), let base build the fired output, then
    /// copy the bytes onto it. No attribute stamp: the <c>-fired</c> variant is the (permanent, read-only)
    /// fired state (wire-tablet-clay-art-and-variants). Firing a hard tablet smelts <c>clay-&lt;color&gt;-hard</c>
    /// → <c>clay-&lt;color&gt;-fired</c>, so the fired output is correct without any residual-state juggling.</summary>
    public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
    {
        // Capture the source document BEFORE base clears the input.
        var source = inputSlot.Itemstack;
        byte[]? docBytes = source?.Attributes.GetBytes(ScribeDocumentAttributes.DocumentAttributeKey);
        byte[]? historyBytes = source?.Attributes.GetBytes("scribeHistory");

        base.DoSmelt(world, cookingSlotsProvider, inputSlot, outputSlot);

        if (outputSlot.Itemstack is not { } fired) return;
        if (docBytes is not null) fired.Attributes.SetBytes(ScribeDocumentAttributes.DocumentAttributeKey, docBytes);
        if (historyBytes is not null) fired.Attributes.SetBytes("scribeHistory", historyBytes);
    }

    // ── Rehydrate (hard → wet) on water exposure, torch-style ──────────────────────────────────────
    // A hard clay tablet softens back to wet when exposed to water (tablet-clay-hardening Decision 3),
    // mirroring how a lit torch extinguishes: OnGroundIdle catches a dropped stack floating in water, and
    // OnHeldIdle catches the active held tablet while its holder swims. Both run server-authoritative and
    // ride the existing stack-attribute persistence — no new packet. Fired tablets never soften.

    /// <summary>Dropped-in-water case: soften a hard tablet floating in water. Runs per-tick for a dropped
    /// item entity (VSAPI-NOTES.md); gated to the server so the softened stack syncs authoritatively. On a
    /// swap the replacement stack (the soft variant) is written back onto the entity.</summary>
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

    /// <summary>Held-while-swimming case: soften a hard tablet held as the active item when its holder
    /// enters water. Runs per-tick for the active held item (VSAPI-NOTES.md); server-gated, writes the
    /// replacement stack into the slot and marks it dirty to sync the swap (mirrors <c>BlockTorch.OnHeldIdle</c>).</summary>
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

    /// <summary>Deliberate-quench case: crouch + right-click a water-filled liquid container to soften a held
    /// hard tablet, mirroring the vanilla metal-quench muscle memory (tablet-clay-hardening Decision 3/D3).
    /// Returns <c>true</c> when the gesture is consumed (a water container WAS aimed at) so the caller sets
    /// <see cref="EnumHandHandling.PreventDefault"/> — this both stops the container's own fill/pour from also
    /// firing AND suppresses the crouch ground-storage passthrough for this one gesture. When the aimed-at
    /// block is not a water container, returns <c>false</c> so the caller falls through to the existing
    /// shift-passthrough. Only a HARD tablet actually softens; a wet or fired tablet aimed at water is a
    /// no-op that still returns <c>true</c> (the water container was the interaction target either way), so
    /// the gesture never leaks into ground-storage placement when the player clearly aimed at water.
    /// Server-authoritative like the two passive paths: the swap + slot write happen server-side; both sides
    /// play the container's water sound for feedback.</summary>
    private bool TryQuench(ItemSlot slot, EntityAgent byEntity, BlockSelection? blockSel)
    {
        if (blockSel is null) return false;
        var world = byEntity.World;
        if (world.BlockAccessor.GetBlock(blockSel.Position) is not BlockLiquidContainerBase container) return false;
        if (!IsWaterContent(container, blockSel.Position)) return false;

        // Aimed at a water container: this gesture is ours regardless of state (so it never falls through to
        // ground storage). Only a hard tablet actually softens; wet/fired no-op via Soften's own guard.
        if (world.Side == EnumAppSide.Server && Soften(slot.Itemstack, world) is { } softened)
        {
            slot.Itemstack = softened;
            slot.MarkDirty();
        }
        // Feedback on both sides: reuse the container's own water fill sound so it matches whatever liquid it
        // holds, rather than hardcoding an asset path.
        var sound = container.GetContentProps(blockSel.Position)?.FillSound;
        if (sound is not null) world.PlaySoundAt(sound, blockSel.Position, 0.5, randomizePitch: true);
        return true;
    }

    /// <summary>Whether a liquid container at <paramref name="pos"/> currently holds water (any water variant
    /// — fresh/salt/boiling/lime). Detected via the shared <see cref="BlockLiquidContainerBase.GetContent"/>
    /// base API and the content stack's collectible code (all water portions end in <c>waterportion</c>), so
    /// bucket/barrel/tureen and any water kind work uniformly without per-block casts. An empty container or a
    /// non-water liquid (milk, oil, honey) returns <c>false</c>.</summary>
    private static bool IsWaterContent(BlockLiquidContainerBase container, Vintagestory.API.MathTools.BlockPos pos)
    {
        var content = container.GetContent(pos);
        string? code = content?.Collectible?.Code?.Path;
        return code is not null && code.EndsWith("waterportion");
    }

    /// <summary>Soften a hard clay tablet back to its wet variant: build a fresh stack of the SOFT sibling
    /// variant (<c>clay-&lt;color&gt;-hard</c> → <c>clay-&lt;color&gt;</c>), carry the document/history onto it, and
    /// leave it with no <c>transitionstate</c> subtree so the engine re-seeds the ~2-day dry-out clock from
    /// now on its next tick (VSAPI-NOTES.md). Because state is the item VARIANT now, softening SWAPS the item
    /// (wire-tablet-clay-art-and-variants) rather than clearing an attribute. Returns the replacement stack,
    /// or <c>null</c> (no-op) on a wet or fired stack — a fired tablet never rehydrates. The caller writes the
    /// returned stack back into its slot/entity.</summary>
    private static ItemStack? Soften(ItemStack? stack, IWorldAccessor world)
    {
        // Natural rehydration only softens a HARD tablet (a wet tablet is already editable; a fired one is
        // permanent — the guard here is what enforces that, NOT BuildStateVariant). The seam does the
        // variant swap + document carry; a freshly-built stack carries no transitionstate, so the engine
        // re-seeds the ~2-day dry-out clock from now on its next tick.
        if (ResolveMaterialState(stack).state != TabletState.Hard) return null;
        return BuildStateVariant(stack, TabletState.Wet, world);
    }

    /// <summary>Build a fresh tablet stack in the requested life-cycle <paramref name="target"/> state,
    /// carrying the source's document + history onto it — the single swap+carry seam shared by the natural
    /// <see cref="Soften"/> path and the <c>/scribe tablet</c> dev command (add-tablet-state-dev-command D2).
    /// Resolves the base clay/wax material from <paramref name="current"/>, maps <paramref name="target"/> to
    /// the sibling <c>material</c> variant (<see cref="TabletState.Wet"/> → bare <c>clay-&lt;color&gt;</c>;
    /// <see cref="TabletState.Hard"/> → <c>-hard</c>; <see cref="TabletState.Fired"/> → <c>-fired</c>), and
    /// <see cref="IWorldAccessor.GetItem"/>s it. Returns <c>null</c> when that sibling variant does not exist
    /// (wax has no <c>-hard</c>/<c>-fired</c> sibling, or an unregistered variant) — the caller reports it.
    ///
    /// <para>State-AGNOSTIC by design: it will happily build a wet/hard sibling FROM a fired stack (the
    /// normally-permanent fired state), because the permanence rule lives only in <see cref="Soften"/>'s own
    /// guard, not here. The dev command relies on this to reset a fired tablet for testing.</para></summary>
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

    /// <summary>Copy the persisted document + history bytes from <paramref name="from"/> onto
    /// <paramref name="to"/>, used by the transform overrides to carry the tablet's content across a
    /// hard/fired rebuild that would otherwise drop it. Null-safe: a source with no document simply copies
    /// nothing (a blank-but-transformed output).</summary>
    private static void CarryStackData(ItemStack? from, ItemStack to)
    {
        if (from is null) return;
        if (from.Attributes.GetBytes(ScribeDocumentAttributes.DocumentAttributeKey) is { } docBytes)
            to.Attributes.SetBytes(ScribeDocumentAttributes.DocumentAttributeKey, docBytes);
        if (from.Attributes.GetBytes("scribeHistory") is { } historyBytes)
            to.Attributes.SetBytes("scribeHistory", historyBytes);
    }

    /// <summary>Open this carried Tablet's Scribe dialog for the Handbook "Add to Scribe" fallback
    /// (add-tracker-link-tasks 3.3). Delegates to the same <see cref="OpenTabletDialog"/> the right-click
    /// path uses. A hard/fired (read-only) tablet still opens, but its editor-access request will no-op, so
    /// a Handbook append against it is a safe no-op — the resolver prefers an editable surface first.</summary>
    public ScribeDialogBase? OpenScribeDialog(ItemSlot slot, ICoreClientAPI capi)
        => OpenTabletDialog(slot, capi);

    /// <summary>A tablet can receive a Handbook append only while WET; a hardened or fired tablet is
    /// read-only, so the "Add to Scribe" resolver skips it and tries the next carried Scribe item
    /// (add-tracker-link-tasks feedback 6.2). Reuses the single <see cref="IsEditable"/> resolve point the
    /// dialog and document policy key off, so this can't drift from the actual editability.</summary>
    public bool IsSlotWriteable(ItemSlot slot) => IsEditable(slot.Itemstack);

    /// <summary>The tablet's document policy, mirroring <see cref="TabletHost.Policy"/> at the item level:
    /// a WET tablet is the capped-but-editable scratch tier (<see cref="Scribe.Core.ScribeDocumentPolicy.Tablet"/>,
    /// at most 10 task blocks); a hardened or fired tablet is read-only
    /// (<see cref="Scribe.Core.ScribeDocumentPolicy.UneditableTablet"/>). Keyed off the same
    /// <see cref="IsEditable"/> resolve point as <see cref="IsSlotWriteable"/> so the two can't drift. The
    /// Transcribe copy consults this to reject a target that's read-only or too small for the source's tasks.</summary>
    public Scribe.Core.ScribeDocumentPolicy DocumentPolicy(ItemSlot slot) =>
        IsEditable(slot.Itemstack)
            ? Scribe.Core.ScribeDocumentPolicy.Tablet
            : Scribe.Core.ScribeDocumentPolicy.UneditableTablet;

    private ScribeDialogBase OpenTabletDialog(ItemSlot slot, ICoreClientAPI capi, bool quickAdd = false)
    {
        // The backdrop/theme/glow key off the tablet's BASE clay color (clay-red/blue/fire/wax) and its
        // life-cycle state (wet/hard/fired) — BOTH now carried by the material variant, so a single parse
        // yields both (wire-tablet-clay-art-and-variants). The item and its dialog agree on the mapping
        // through ScribeBackdrops.ForTablet (add-tablet-dialog D6, add-tablet-clay-type-backdrops,
        // add-tablet-firing-mechanic). An unknown variant resolves to red + wet. Note: pass the resolved
        // BASE material, not Variant["material"] (which now includes the -hard/-fired suffix).
        var stack = slot.Itemstack;
        var (material, state) = ResolveMaterialState(stack);
        var host = new TabletHost(slot,
            ScribeBackdrops.ForTablet(material, state), material);
        var modSystem = capi.ModLoader.GetModSystem<ScribeModSystem>();
        modSystem.RegisterHost(host);
        // Tell the server we opened this tablet so it can record the one-time PickedUp entry
        // (opening the dialog is client-only; the server never sees it otherwise).
        modSystem.NotifyServerNotebookOpened(host.Document.DocId, slot);
        // Remember this as the last-opened Scribe item (add-tracker-link-tasks 3.2).
        modSystem.NoteScribeItemDialogOpened(host.Document.DocId);

        // The bespoke tablet dialog (Proposal C): earthen theme, no tabs, cuneiform title banner over the
        // task list. The state (wet/hard/fired) drives both editability (only a WET tablet edits) and the
        // read-only empty-state message (dried vs fired), so it's threaded rather than a bare editable bool.
        var dialog = new GuiDialogScribeTablet(host, capi, material, state);
        dialog.OnClosed += () => modSystem.UnregisterHost(host.Document.DocId);
        dialog.TryOpen();
        // Quick-add (Shift+right-click off water): a WET tablet is always-edit — its ctor already entered
        // the editor before TryOpen built the tree — so just drop a fresh empty top task with the caret
        // focused (add-unified-quick-add-interaction). On a HARD/FIRED (read-only) tablet the seam no-ops
        // (never in editor mode), so a stray Shift+right-click merely opens the read-only dialog.
        if (quickAdd) dialog.QuickAddTopTask();
        return dialog;
    }
}
