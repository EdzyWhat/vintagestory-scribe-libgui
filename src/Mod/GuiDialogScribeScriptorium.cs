using System.Collections.Generic;
using Gui.Core.Layout;          // MainAxisSize
using Gui.Widgets.Framework;
using Gui.Widgets.Inventory;    // SlotController, FlatItemSlot, ItemSlotStyle
using Gui.Widgets.Layout;       // Center, Row, Stack, Positioned, MainAxisAlignment, CrossAxisAlignment
using OpenTK.Mathematics;       // Vector4 (watermark tint)
using Vintagestory.API.Client;
using Vintagestory.API.Common;  // ItemSlot
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Scriptorium block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>,
/// mirroring <see cref="GuiDialogScribeLecternLibGui"/>. All view state, build methods, lock
/// orchestration, autosave, title editing, scroll management, and nav-button layout live in the base
/// class. For v1.2 the Scriptorium adds the Guestbook nav button (exactly like the Lectern) plus its own
/// Scribe-items-only inventory tab (add-scriptorium-inventory). This subclass is also the v1.3 attachment
/// point for the Scriptorium-only Assign &amp; History and Inbox nav buttons.
/// </summary>
public sealed class GuiDialogScribeScriptorium : ScribeDialogBase
{
    /// <summary>The owning block-entity, kept typed so the inventory tab can reach its
    /// <see cref="BlockEntityScriptorium.Inventory"/> (the base only stores the untyped
    /// <see cref="IScribeDocumentHost"/>).</summary>
    private readonly BlockEntityScriptorium scriptorium;

    /// <summary>Bridges the LibGUI slot widgets to the block-entity inventory: it turns pointer gestures on
    /// a slot into the server-bound slot-activation packets and fires its <c>ChangeNotifier</c> on every
    /// <c>SlotModified</c>. We subscribe <see cref="ScribeDialogBase.RebuildBody"/> to that notifier (in
    /// <see cref="EnsureSlotController"/>) so a slot change actually re-renders — our custom
    /// <see cref="ScribeDocumentSlot"/> is stateless and, unlike the stock <see cref="FlatItemSlot"/>, does
    /// not self-subscribe. Created lazily on the first inventory-tab build and disposed in
    /// <see cref="OnGuiClosed"/> — NOT re-created per rebuild (the reconcile architecture re-runs
    /// <see cref="BuildInventoryContent"/> every frame the tab is open, so a per-build controller would leak
    /// a <c>SlotModified</c> subscription each time).</summary>
    private SlotController? slotController;

    public GuiDialogScribeScriptorium(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // Pass the BE's inventory to the inventory-carrying GuiDialogBlockEntityBase ctor so OpenInventory /
        // CloseInventoryAndSync fire automatically on open/close. The Lectern/Notebook/Tablet dialogs keep
        // the null default, so their behavior is byte-identical to the old inventory-less ctor.
        : base(pos, host, capi, ((BlockEntityScriptorium)host).Inventory)
    {
        scriptorium = (BlockEntityScriptorium)host;
    }

    /// <summary>The Scriptorium is a shared placed block (like the Lectern): editor access requires a
    /// server lock round-trip, so the grant lands asynchronously in
    /// <see cref="ScribeDialogBase.EnterEditorMode"/>. A Handbook "Add to Scribe" click stashes its append
    /// and waits for that grant (add-tracker-link-tasks 3.4).</summary>
    protected override bool EditorAccessIsAsync => true;

    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribeguest",
            "scribe-tab-guestbook",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToVisitors,
            boxShadows: NavButtonShadow,
            activeColor: IsVisitorsView ? ScribeRowConstants.NavActiveGuestbook : null);

        // Scriptorium-only Scribe-items-only storage tab (add-scriptorium-inventory).
        yield return TitleButton(
            "scribeinventory",
            "scribe-tab-inventory",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToInventory,
            boxShadows: NavButtonShadow,
            activeColor: IsInventoryView ? ScribeRowConstants.NavActiveGuestbook : null);
    }

    /// <summary>Slot edge length in pixels — matches LibGUI's <c>SlotGrid</c>/<c>ItemSlotStyle.Default</c> so
    /// the hand-built row of slots looks identical to a stock grid.</summary>
    private const float SlotSize = 48f;

    /// <summary>Gap between the two slots, matching <c>SlotGrid</c>'s default spacing.</summary>
    private const float SlotSpacing = 4f;

    /// <summary>The book watermark's edge length as a fraction of the slot — centered within the slot.</summary>
    private const float WatermarkScale = 0.66f;

    /// <summary>Builds the inventory tab: a centered row of the Scriptorium's Scribe-item slots, wired to a
    /// dialog-lifetime <see cref="SlotController"/>. The controller watches the inventory once (lazily), so
    /// every rebuild reuses the same subscription rather than adding a new one.
    ///
    /// <para>Each slot carries a <c>scribebook</c> watermark that telegraphs "Scribe items only" (Notebooks /
    /// Tablets / picked-up Lecterns / Scriptoriums). The watermark is drawn UNDERNEATH the slot, always
    /// present: see <see cref="BuildWatermarkedSlot"/> for why layering it under (not over) fixes visibility,
    /// click-through, and the item/watermark z-order all at once.</para></summary>
    protected override Widget BuildInventoryContent()
    {
        var controller = EnsureSlotController();
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Book drawn full-opacity Primary UNDER the slot; the slot's box fill is the parchment Surface (the
        // creme that matches the page art) at ~66% alpha, veiling the book down to a faint, non-disruptive
        // watermark (feedback — a black veil read as "almost black"). Tunable polish knob (design.md D7).
        var bookColor = colors.Primary;
        var veilColor = colors.Surface with { W = 0.66f };

        var inv = scriptorium.Inventory;
        var slotWidgets = new List<Widget>(inv.Count);
        for (int i = 0; i < inv.Count; i++)
        {
            slotWidgets.Add(BuildWatermarkedSlot(inv[i], controller, colors, CurrentShade, bookColor, veilColor));
        }

        return new Center(child: new Row(
            spacing: SlotSpacing,
            mainAxisAlignment: MainAxisAlignment.Center,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: slotWidgets));
    }

    /// <summary>One inventory slot: an always-present, fully-opaque <c>scribebook</c> watermark UNDERNEATH the
    /// real <see cref="FlatItemSlot"/>, whose box fill is made transparent so the book shows through. This
    /// under-layering (feedback) fixes three problems the earlier over-layering had at once:
    /// <list type="bullet">
    /// <item>The book is the sole visible fill, at full opacity, so it actually reads (the faint on-top wash
    /// was invisible against the parchment).</item>
    /// <item>The <see cref="FlatItemSlot"/> is on TOP and owns the full-slot gesture region, so the glyph can
    /// never intercept the click that drops an item in (the over-layer version blocked the slot centre).</item>
    /// <item>A placed item is drawn by the slot's own overlay, i.e. ON TOP of the book, with no z-order race —
    /// the watermark is simply always under, "a fun visual indicator that's always there," never toggled.</item>
    /// </list>
    /// LibGUI's <c>RenderStack</c> sizes to the sole non-positioned child (the <see cref="ScribeDocumentSlot"/>
    /// at <see cref="SlotSize"/>) regardless of child order, and paints in list order — so the
    /// <see cref="Positioned"/> book (first) paints under the slot (second).
    ///
    /// <para>The slot itself is our custom <see cref="ScribeDocumentSlot"/> (refine-scribe-hover-tooltips D3)
    /// rather than the stock <see cref="FlatItemSlot"/>: it renders the same slot box + item but shows a
    /// compact Scribe document-summary card on hover (name / title / per-kind counts / never-opened), shaded
    /// by the live illumination shade at reduced hover strength, instead of LibGUI's fixed 350px item
    /// tooltip.</para></summary>
    private static Widget BuildWatermarkedSlot(ItemSlot? slot, SlotController controller, ColorScheme colors,
        ScribeAmbientLightSampler.Shade shade, Vector4 bookColor, Vector4 veilColor)
    {
        float glyph = SlotSize * WatermarkScale;
        float inset = (SlotSize - glyph) / 2f;
        // The book underneath is drawn at full Primary; the slot's own box fill sits ON TOP of it as a
        // semi-opaque parchment veil, muting the book to a faint-but-discernible watermark rather than a bold
        // icon (feedback). Border left default so the slot still reads as a slot.
        var slotStyle = ItemSlotStyle.Default with { BackgroundColor = veilColor };
        return new Stack(children: new Widget[]
        {
            new Positioned(
                left: inset, top: inset, width: glyph, height: glyph,
                child: new ScribeVsIconGlyph("scribebook", glyph, bookColor)),
            new ScribeDocumentSlot(slot, controller, slotStyle, colors, shade),
        });
    }

    /// <summary>Lazily create the slot controller and start watching the inventory. Idempotent — safe to call
    /// on every rebuild; only the first call allocates and subscribes.
    ///
    /// <para><b>Rebuild the body on every slot change.</b> The stock <c>FlatItemSlot</c> gets its
    /// "repaint when the stack changes" for free because its <c>ItemSlotGestureLayer</c> is a
    /// <c>StatefulWidget</c> that subscribes to the controller (a <c>ChangeNotifier</c>) and calls
    /// <c>SetState</c> on notify. Our <see cref="ScribeDocumentSlot"/> is a plain <c>StatelessWidget</c>
    /// (it drops that gesture layer to swap the tooltip), so nothing rebuilds it when a slot's contents
    /// change — a just-placed item stayed invisible until some UNRELATED rebuild (e.g. an illumination
    /// shade change) happened to re-run the tree, which is exactly why it only appeared when the local
    /// light shifted. Subscribing <see cref="ScribeDialogBase.RebuildBody"/> to the controller closes the
    /// gap: any <c>SlotModified</c> now marks the body for reconcile via <c>SetState</c> (deferred, so it's
    /// safe even when a click fires <c>SlotModified</c> mid-pointer-dispatch), and the reconcile reuses the
    /// rest of the tree. The listener lives on the controller and is cleared when we dispose it in
    /// <see cref="OnGuiClosed"/> (<c>ChangeNotifier.Dispose</c> clears its listeners).</para></summary>
    private SlotController EnsureSlotController()
    {
        if (slotController == null)
        {
            slotController = new SlotController(capi);
            slotController.WatchInventory(scriptorium.Inventory);
            slotController.AddListener(RebuildBody);
        }
        return slotController;
    }

    /// <summary>Tear down the slot controller when the dialog closes so its <c>SlotModified</c> subscription
    /// doesn't outlive the dialog. The base <see cref="ScribeDialogBase.OnGuiClosed"/> chains to
    /// <c>GuiDialogBlockEntityBase.OnGuiClosed</c>, which fires <c>CloseInventoryAndSync</c> (server close +
    /// packet 1001); we clean up our own watcher afterward.</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (slotController != null)
        {
            slotController.UnwatchInventory(scriptorium.Inventory);
            slotController.Dispose();
            slotController = null;
        }
    }
}
