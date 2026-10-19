using System.Collections.Generic;
using Gui.Core.Layout;          // MainAxisSize
using Gui.Rendering;            // EdgeInsets
using Gui.Rendering.Text;       // TextStyle, FontWeight
using Gui.Widgets.Basic;        // Text, Container, Divider, Button, ButtonVariant, VsIcon
using Gui.Widgets.Framework;
using Gui.Widgets.Input;        // RadioButton
using Gui.Widgets.Inventory;    // SlotController, FlatItemSlot, ItemSlotStyle
using Gui.Widgets.Layout;       // Center, Row, Column, Stack, Positioned, Padding, SizedBox, Expanded, MainAxisAlignment, CrossAxisAlignment
using Gui.Widgets.Painting;     // BoxStyle
using OpenTK.Mathematics;       // Vector4 (watermark tint)
using Scribe.Core;              // ScribeArrowDigraph, ScribePlayerSettings, ScribeDocumentPolicy
using Vintagestory.API.Client;
using Vintagestory.API.Common;  // ItemSlot
using Vintagestory.API.Config;  // Lang
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Scriptorium block's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>, mirroring
/// <see cref="GuiDialogScribeLecternLibGui"/>. Adds the Guestbook and Inbox nav buttons plus its own
/// Scribe-items-only Transcribe (inventory) tab.
/// </summary>
public sealed class GuiDialogScribeScriptorium : ScribeDialogBase
{
    /// <summary>The owning block-entity, kept typed (the base only stores the untyped
    /// <see cref="IScribeDocumentHost"/>) to reach <see cref="BlockEntityScriptorium.Inventory"/>.</summary>
    private readonly BlockEntityScriptorium scriptorium;

    /// <summary>Bridges LibGUI slot widgets to the inventory (pointer gestures to slot-activation packets;
    /// notifies on every <c>SlotModified</c>). <see cref="EnsureSlotController"/> subscribes
    /// <see cref="ScribeDialogBase.RebuildBody"/> because the stateless <see cref="ScribeDocumentSlot"/>
    /// doesn't self-subscribe like <see cref="FlatItemSlot"/>. Created lazily, disposed in
    /// <see cref="OnGuiClosed"/>, never per rebuild (<see cref="BuildInventoryContent"/> re-runs every frame,
    /// which would leak a subscription each time).</summary>
    private SlotController? slotController;

    /// <summary>Inventory slot indices: 0 = copy Original (source), 1 = copy Duplicate (target),
    /// 2 = Import/Export source-and-target.</summary>
    private const int SourceSlotIndex = 0;
    private const int TargetSlotIndex = 1;
    private const int ImportExportSlotIndex = 2;

    /// <summary>The seal button's two-press overwrite-confirm state (client-only UX; the server re-checks).
    /// Reset to Idle whenever a slot's contents change.</summary>
    private enum TranscribeConfirm { Idle, ConfirmOverwrite }
    private TranscribeConfirm confirmState = TranscribeConfirm.Idle;

    /// <summary>The Import button's two-press confirm, separate from <see cref="confirmState"/> because the
    /// buttons target different slots. Only meaningful in <see cref="CopyMode.Overwrite"/> against a
    /// non-empty target.</summary>
    private TranscribeConfirm importConfirmState = TranscribeConfirm.Idle;

    /// <summary>The copy behavior chosen by the radio. <see cref="CopyMode.Overwrite"/> (default) replaces
    /// the target, with the two-press confirm when it is non-empty; <see cref="CopyMode.Append"/> is
    /// non-destructive and needs no confirm. The server re-checks capacity and applies the mode
    /// (<see cref="ScribeTranscribeCopyMessage.Append"/>).</summary>
    private enum CopyMode { Overwrite, Append }
    private CopyMode copyMode = CopyMode.Overwrite;

    /// <summary>The wooden rubber-stamp flourish; purely cosmetic (the marked action is already done
    /// server-side). <see cref="stampRegistry"/> owns the animation controller so it survives the per-frame
    /// reconcile; <see cref="stampGeneration"/> bumps per play so a re-play remounts a fresh
    /// <see cref="ScribeStamp"/> (new key + id) instead of reusing the completed controller.
    ///
    /// <para><see cref="stampTargetSlot"/> is the slot the overlay sits on (−1 = none) and
    /// <see cref="stampLabel"/> the imprint text; <see cref="PlayStamp"/> takes both.</para></summary>
    private readonly ScribeAnimationRegistry stampRegistry = new();
    private int stampGeneration;
    /// <summary>Slot index the flourish plays over, or −1.</summary>
    private int stampTargetSlot = -1;
    /// <summary>The imprint text (e.g. "COPIED"); meaningful while <see cref="stampTargetSlot"/> is not −1.</summary>
    private string stampLabel = "";

    /// <summary>The pixel-art stamp PNG (baked by <c>build/gen-copy-stamp.py</c>), scaled up nearest-neighbour.</summary>
    private static readonly AssetLocation StampAsset = new("scribe", "textures/gui/scribe-copy-stamp.png");

    public GuiDialogScribeScriptorium(BlockPos pos, IScribeDocumentHost host, ICoreClientAPI capi)
        // The inventory-carrying base ctor fires OpenInventory / CloseInventoryAndSync on open/close.
        : base(pos, host, capi, ((BlockEntityScriptorium)host).Inventory)
    {
        scriptorium = (BlockEntityScriptorium)host;
        // Transcribe is what a plain right-click opens (see EnterGrantedView).
        DefaultToInventoryView();
    }

    /// <summary>Shared placed block: the editor grant lands asynchronously in
    /// <see cref="ScribeDialogBase.EnterEditorMode"/>, so a Handbook "Add to Scribe" click stashes its
    /// append and waits.</summary>
    protected override bool EditorAccessIsAsync => true;

    /// <summary>Transcribe comes before Read/Edit/Pinned in the nav order.</summary>
    protected override IEnumerable<Widget> GetLeadingNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribebook",
            "scribe-tab-transcribe",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToInventory,
            boxShadows: NavButtonShadow,
            activeColor: IsInventoryView ? ScribeRowConstants.NavActiveTranscribe : null);
    }

    /// <summary>Right-click lands on Transcribe rather than the base's Read view. Crouch+right-click
    /// (quick-add) is handled upstream in <see cref="BlockScribeWritingStation.OnBlockInteractStart"/>.</summary>
    public override void EnterGrantedView() => OnClickSwitchToInventory();

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

        // Players who have never received an assignment get no Inbox nav button here.
        if (modSystem.MyReceivedAssignments.Count > 0)
        {
            yield return TitleButton(
                "scribeinboxarrow",
                "scribe-tab-inbox",
                colors.OnSurfaceVariant,
                NavButtonSize,
                OnClickSwitchToInbox,
                boxShadows: NavButtonShadow,
                activeColor: IsInboxView ? ScribeRowConstants.NavActiveGuestbook : null,
                shimmer: ShowInboxShimmer);
        }
    }

    /// <summary>Slot edge length in pixels, matching LibGUI's <c>SlotGrid</c>/<c>ItemSlotStyle.Default</c>.</summary>
    private const float SlotSize = 48f;

    /// <summary>Gap between slots (<c>SlotGrid</c>'s default).</summary>
    private const float SlotSpacing = 4f;

    /// <summary>Fixed width of the "→" cell between the copy slots (see <see cref="BuildCopySection"/>).</summary>
    private const float ArrowCellWidth = 34f;

    /// <summary>Fixed width of the import/export controls column; without it a
    /// <see cref="CrossAxisAlignment.Stretch"/> Column balloons to the page width.</summary>
    private const float IoControlsWidth = 140f;

    /// <summary>Width the import/export slot caption wraps within.</summary>
    private const float IoCaptionWidth = 96f;

    /// <summary>The book watermark's edge length as a fraction of the slot.</summary>
    private const float WatermarkScale = 0.66f;

    /// <summary>Builds the Transcribe tab: the copy zone on top and the import/export zone below, wired to
    /// the dialog-lifetime <see cref="SlotController"/>. Each slot carries a <c>scribebook</c> watermark
    /// telegraphing "Scribe items only" (see <see cref="BuildWatermarkedSlot"/>).</summary>
    protected override Widget BuildInventoryContent()
    {
        var controller = EnsureSlotController();
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        // Book in full Primary under the slot; the slot's parchment fill at ~66% alpha veils it to a watermark.
        var bookColor = colors.Primary;
        var veilColor = colors.Surface with { W = 0.66f };

        // Headings, buttons, captions, and arrow use Caudex (ScribeTaskFont.ButtonFamily).
        var headingStyle = new TextStyle
        {
            FontSize = 16, Weight = FontWeight.Bold, Color = colors.OnSurface, FontFamily = ScribeTaskFont.ButtonFamily,
        };

        // Vertical 50/50 split (two Expanded halves), each zone centred; no scroll because the content
        // fits at every supported size.
        var topZone = new Center(child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(
            spacing: 10,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-transcribe-heading"), headingStyle),
                BuildCopySection(controller, colors, bookColor, veilColor),
                BuildSealButton(colors),
                BuildCopyModeRadio(colors),
            })));

        var bottomZone = new Center(child: new Column(
            spacing: 10,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-transcribe-io-heading"), headingStyle),
                BuildImportExportSection(controller, colors, bookColor, veilColor),
            }));

        // Neither zone is Row 3, so the header divider follows the subtitle; the Divider between the
        // zones is an internal separator.
        Widget header = ScribeTabHeader.Build(colors, RowStyle,
            "scribe:scribe-tab-transcribe", "scribe:scribe-tab-subtitle-transcribe",
            modSystem.VisualTuning.ShowSubtitleRow);
        var content = new Padding(EdgeInsets.Ltrb(10f, 4f, 10f, 10f), new Column(
            spacing: 0,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                header,
                new Expanded(child: topZone),
                new Divider(),
                new Expanded(child: bottomZone),
            }));

        // Info button stacked over the corner so it doesn't disturb the split; inset by 4px plus 1% of the
        // Pixel Art Size so it clears the page edge at every art size.
        float cornerInset = 4f + ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) * 0.01f;
        return new Stack(children: new Widget[]
        {
            content,
            new Positioned(right: cornerInset, bottom: cornerInset, child: BuildFeaturesHelpButton(colors)),
        });
    }

    /// <summary>The bottom-right ⓘ button, peer of the editor footer's features button; toggles the tab's
    /// handbook guide page via <see cref="ScribeDialogBase.ToggleHandbookPage"/>. The style comes from
    /// <see cref="ScribeTheme.For"/> because content builders have no <see cref="BuildContext"/>.</summary>
    private Widget BuildFeaturesHelpButton(ColorScheme colors)
    {
        var buttonStyle = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ButtonStyle with
        {
            Padding = EdgeInsets.All(7),
        };
        return WithTooltip(
            "scribe-transcribe-features-tooltip",
            new Button(
                child: new ScribeVsIconGlyph("scribeinfo", 17f, colors.OnPrimary),
                style: buttonStyle,
                onTap: _ => ToggleHandbookPage("craftinginfo-scribe-transcribe")));
    }

    /// <summary>The copy pair: Original slot, "→" arrow (<see cref="ScribeArrowDigraph.RightArrow"/>), and
    /// Duplicate slot, each captioned. The arrow is boxed to the slot height so it lines up with the slots,
    /// not their captions.</summary>
    private Widget BuildCopySection(SlotController controller, ColorScheme colors, Vector4 bookColor, Vector4 veilColor)
    {
        var inv = scriptorium.Inventory;
        // Fixed width too: a Center grows to a finite max width (which the Row passes), which would
        // balloon to the full page and shove the slots off the edges.
        Widget arrow = new SizedBox(width: ArrowCellWidth, height: SlotSize, child: new Center(child: new Text(
            ScribeArrowDigraph.RightArrow.ToString(),
            new TextStyle { FontSize = 28f, Color = colors.OnSurfaceVariant, FontFamily = ScribeTaskFont.ButtonFamily })));
        return new Row(
            spacing: 14f,
            mainAxisAlignment: MainAxisAlignment.Center,
            crossAxisAlignment: CrossAxisAlignment.Start,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                LabeledSlot(inv[SourceSlotIndex], "scribe-transcribe-copyfrom", controller, colors, bookColor, veilColor),
                arrow,
                LabeledSlot(inv[TargetSlotIndex], "scribe-transcribe-pasteinto", controller, colors, bookColor, veilColor,
                    overlay: BuildStampOverlay(TargetSlotIndex)),
            });
    }

    /// <summary>A watermarked slot above its caption. <paramref name="overlay"/> (the stamp flourish) is
    /// stacked on the slot, sharing its footprint but free to overflow (paint-only).</summary>
    private Widget LabeledSlot(ItemSlot slot, string labelKey, SlotController controller, ColorScheme colors,
        Vector4 bookColor, Vector4 veilColor, Widget? overlay = null, float? captionWidth = null)
    {
        Widget slotWidget = BuildWatermarkedSlot(slot, controller, colors, CurrentShade, bookColor, veilColor);
        if (overlay != null)
        {
            slotWidget = new Stack(children: new Widget[]
            {
                slotWidget,
                new Positioned(left: 0f, top: 0f, width: SlotSize, height: SlotSize, child: overlay),
            });
        }
        // A longer caption is bounded to captionWidth so it wraps instead of stretching the Row.
        Widget caption = captionWidth is float w
            ? new SizedBox(width: w, child: new Text(Lang.Get("scribe:" + labelKey),
                new TextStyle { FontSize = 11, Color = colors.OnSurfaceVariant, SoftWrap = true, Align = TextAlignment.Center }))
            : new Text(Lang.Get("scribe:" + labelKey),
                new TextStyle { FontSize = 12, Color = colors.OnSurfaceVariant });
        return new Column(
            spacing: 4f,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                slotWidget,
                caption,
            });
    }

    /// <summary>The stamp flourish for <paramref name="slotIndex"/>, or null unless it matches
    /// <see cref="stampTargetSlot"/>. The <see cref="ScribeStamp"/> key + id carry
    /// <see cref="stampGeneration"/> so a re-play remounts. A missing bitmap drops the wooden image but
    /// keeps the imprint.</summary>
    private Widget? BuildStampOverlay(int slotIndex)
    {
        if (slotIndex != stampTargetSlot) return null;
        string id = StampId(stampGeneration);
        // Width scales with Pixel Art Size (× 0.2; 600 → 120px), spilling past the 48px slot.
        float artWidth = ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) * 0.2f;
        // Imprint glow: parchment Surface at 0.6 alpha, so it doesn't read as a solid plate.
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new ScribeStamp(
            id: id,
            registry: stampRegistry,
            stampBitmap: modSystem.GetGuiTextureBitmap(StampAsset),
            copyLabel: stampLabel,
            imprintColor: ScribeRowConstants.StampImprintInk,
            glowColor: colors.Surface with { W = 0.6f },
            slotSize: SlotSize,
            artWidth: artWidth,
            onEnd: () => OnStampEnded(id),
            onDescend: PlayStampSound,
            key: new ValueKey<string>(id));
    }

    /// <summary>Plays the stamp sound at the contact frame (wired to <see cref="ScribeStamp.OnDescend"/>,
    /// so only when the flourish is mounted). Shared with the Assignment Desk via <see cref="ScribeStampSound"/>.</summary>
    private void PlayStampSound() => ScribeStampSound.Play(capi);

    /// <summary>The Copy button. Disabled with an explainer tooltip until both slots hold a Scribe item and
    /// the Duplicate is writeable with room. A target that already has tasks needs a second press on the red
    /// confirm; an empty target copies on one press.</summary>
    private Widget BuildSealButton(ColorScheme colors)
    {
        var inv = scriptorium.Inventory;
        bool bothFilled = inv[SourceSlotIndex].Itemstack != null && inv[TargetSlotIndex].Itemstack != null;

        // A real disabled Button (0.45 opacity, no press sound) plus a tooltip, not a hand-rolled box.
        Widget Disabled(string tooltipKey, params object[] tooltipArgs) => WithTooltip(
            tooltipKey,
            LabelButton(Lang.Get("scribe:scribe-transcribe-stamp"), colors, enabled: false, ButtonVariant.Primary),
            tooltipArgs);

        if (!bothFilled)
        {
            confirmState = TranscribeConfirm.Idle; // can't be mid-confirm without both slots filled
            return Disabled("scribe-transcribe-stamp-disabled");
        }

        // Valid-target gate (writeable and has room) from the target's document policy; UX only, the
        // server re-checks in OnServerReceivedTranscribeCopy.
        var targetPolicy = TargetPolicy();
        if (targetPolicy.ReadOnly)
        {
            confirmState = TranscribeConfirm.Idle;
            return Disabled("scribe-transcribe-stamp-readonly");
        }
        // Overwrite replaces (result = source's blocks); Append adds (target's + source's).
        int resultingBlocks = copyMode == CopyMode.Append
            ? TargetBlockCount() + SourceBlockCount()
            : SourceBlockCount();
        if (!targetPolicy.CanHold(resultingBlocks))
        {
            confirmState = TranscribeConfirm.Idle;
            return Disabled("scribe-transcribe-stamp-toobig", targetPolicy.MaxBlocks ?? 0);
        }

        // Only Overwrite onto a target with completable tasks confirms.
        int targetTasks = TargetTaskCount();
        bool confirming = copyMode == CopyMode.Overwrite
            && confirmState == TranscribeConfirm.ConfirmOverwrite && targetTasks > 0;
        if (copyMode == CopyMode.Append) confirmState = TranscribeConfirm.Idle;

        string label = confirming
            ? Lang.Get("scribe:scribe-transcribe-stamp-confirm")
            : Lang.Get("scribe:scribe-transcribe-stamp");

        // Not wrapped in Expanded, so it hugs its text; Danger signals the destructive overwrite.
        return LabelButton(label, colors, enabled: true, confirming ? ButtonVariant.Danger : ButtonVariant.Primary,
            onTap: OnSealPressed);
    }

    /// <summary>The Overwrite / Append radio under the Copy button. <c>RadioButton</c> requires
    /// <c>IEquatable&lt;T&gt;</c>, so the group value is the enum cast to <c>int</c>. Labels use the player's
    /// task font, not Caudex. Switching modes clears any armed confirm.
    ///
    /// <para>Each option is in a fixed-width <see cref="SizedBox"/> because a <c>RadioButton</c> lays out in
    /// an internal <see cref="MainAxisSize.Max"/> Row and would fill the page, pushing "Append" off the
    /// edge. The widths are sized to the labels; widen one if a font clips it.</para></summary>
    private Widget BuildCopyModeRadio(ColorScheme colors)
    {
        var labelStyle = new TextStyle
        {
            FontSize = ScribeTaskFont.LayoutSize(modSystem.MySettings.TaskFontFamily, 14),
            Color = colors.OnSurface,
            FontFamily = ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily),
        };
        var style = new RadioButtonStyle
        {
            DotColor = colors.Primary,
            BackgroundColor = colors.SurfaceHigh,
            BorderColor = colors.Border,
            BorderThickness = 1.5f,
            LabelStyle = labelStyle,
        };

        int selected = (int)copyMode;
        void Choose(int v)
        {
            var mode = (CopyMode)v;
            if (mode == copyMode) return;
            copyMode = mode;
            confirmState = TranscribeConfirm.Idle;
            importConfirmState = TranscribeConfirm.Idle;
            RebuildBody();
        }

        Widget Radio(CopyMode mode, string labelKey, float width) => new SizedBox(
            width: width,
            child: new RadioButton<int>(
                value: (int)mode,
                groupValue: selected,
                onChanged: Choose,
                label: Lang.Get("scribe:" + labelKey),
                size: 18f,
                style: style));

        return new Row(
            spacing: 10f,
            mainAxisAlignment: MainAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                Radio(CopyMode.Overwrite, "scribe-transcribe-mode-overwrite", 120f),
                Radio(CopyMode.Append, "scribe-transcribe-mode-append", 95f),
            });
    }

    /// <summary>A label Button on the default theme style with a Caudex label, matching the editor's footer
    /// buttons; hugs its label. A disabled button renders at 0.45 opacity and never fires.
    ///
    /// <para><paramref name="center"/> centres the label in a button whose width is fixed by an outer
    /// Stretch, using a full-width <see cref="Row"/> rather than <see cref="Gui.Widgets.Layout.Center"/>:
    /// a <c>Center</c> fills both finite axes and would balloon the button to the full zone height.</para></summary>
    private static Widget LabelButton(string label, ColorScheme colors, bool enabled, ButtonVariant variant,
        System.Action? onTap = null, bool center = false)
    {
        Vector4 fg = variant == ButtonVariant.Danger ? colors.OnError : colors.OnPrimary;
        Widget child = new Text(label, new TextStyle { FontSize = 14, Color = fg, FontFamily = ScribeTaskFont.ButtonFamily });
        if (center)
            child = new Row(
                mainAxisAlignment: MainAxisAlignment.Center,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { child });
        return new Button(
            child: child,
            variant: variant,
            enabled: enabled,
            onTap: onTap == null ? null : _ => onTap());
    }

    /// <summary>Block count (of any kind) on the Original item — the capacity measure the tier cap counts
    /// (<see cref="Scribe.Core.ScribeDocument.BlockCount"/>). 0 when empty or without a document.</summary>
    private int SourceBlockCount()
    {
        var stack = scriptorium.Inventory[SourceSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.BlockCount;
        return 0;
    }

    /// <summary>The Duplicate item's document policy (capacity + editability); unlimited when empty or not
    /// an <see cref="IScribeDocumentItem"/>.</summary>
    private ScribeDocumentPolicy TargetPolicy()
    {
        var slot = scriptorium.Inventory[TargetSlotIndex];
        if (slot.Itemstack?.Collectible is IScribeDocumentItem item)
            return item.DocumentPolicy(slot);
        return ScribeDocumentPolicy.Unlimited;
    }

    /// <summary>Completable-task count on the Duplicate item; 0 (copies without confirm) when empty or
    /// without a document.</summary>
    private int TargetTaskCount()
    {
        var stack = scriptorium.Inventory[TargetSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.CompletableCount;
        return 0;
    }

    /// <summary>Block count already on the Duplicate (distinct from <see cref="TargetTaskCount"/>), used
    /// for the Append capacity gate. 0 when empty or without a document.</summary>
    private int TargetBlockCount()
    {
        var stack = scriptorium.Inventory[TargetSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.BlockCount;
        return 0;
    }

    /// <summary>Append always sends on one press. Overwrite copies at once onto an empty target; a
    /// non-empty target arms the red confirm first and commits on the second press.</summary>
    private void OnSealPressed()
    {
        if (copyMode == CopyMode.Append)
        {
            SendTranscribeCopy(append: true, allowOverwrite: false);
            confirmState = TranscribeConfirm.Idle;
            StampCopy();
        }
        else if (TargetTaskCount() == 0)
        {
            SendTranscribeCopy(append: false, allowOverwrite: false);
            confirmState = TranscribeConfirm.Idle;
            StampCopy();
        }
        else if (confirmState == TranscribeConfirm.Idle)
        {
            confirmState = TranscribeConfirm.ConfirmOverwrite;
            RebuildBody();
        }
        else
        {
            SendTranscribeCopy(append: false, allowOverwrite: true);
            confirmState = TranscribeConfirm.Idle;
            StampCopy();
        }
    }

    /// <summary>Plays the "COPIED" flourish over the Duplicate slot (both copy modes).</summary>
    private void StampCopy() => PlayStamp(TargetSlotIndex, Lang.Get("scribe:scribe-transcribe-stamp-imprint"));

    /// <summary>This block's position; the key for matching a watcher-stamp broadcast to its open dialog.</summary>
    public BlockPos BlockPosition => scriptorium.Pos;

    /// <summary>Replays the flourish for a copy/import by another player on this block (driven by
    /// <see cref="ScribeTranscribeStampMessage"/>). <paramref name="imported"/> selects COPIED vs IMPORTED.</summary>
    public void PlayWatcherStamp(int slot, bool imported) =>
        PlayStamp(slot, Lang.Get(imported
            ? "scribe:scribe-transcribe-stamp-imprint-imported"
            : "scribe:scribe-transcribe-stamp-imprint"));

    /// <summary>Starts (or restarts) the flourish over <paramref name="targetSlot"/> stamping
    /// <paramref name="label"/>. Called only for actions the server will accept. The generation bump gives
    /// the next <see cref="ScribeStamp"/> a fresh key+id so it replays from the start.</summary>
    private void PlayStamp(int targetSlot, string label)
    {
        stampGeneration++;
        stampTargetSlot = targetSlot;
        stampLabel = label;
        RebuildBody();
    }

    /// <summary>Releases the finished stamp's controller by its own id and drops the overlay, unless a
    /// newer generation is already running.</summary>
    private void OnStampEnded(string id)
    {
        stampRegistry.Release(id);
        if (id == StampId(stampGeneration))
        {
            stampTargetSlot = -1;
            RebuildBody();
        }
    }

    private static string StampId(int generation) => $"transcribe-stamp:{generation}";

    /// <summary>Sends the copy request; the server clones the source with a fresh identity onto the target.</summary>
    private void SendTranscribeCopy(bool append, bool allowOverwrite)
    {
        var pos = scriptorium.Pos;
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeTranscribeCopyMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            SourceSlot = SourceSlotIndex,
            TargetSlot = TargetSlotIndex,
            Append = append,
            AllowOverwrite = allowOverwrite,
        });
    }

    /// <summary>The Import/Export section: the watermarked slot (<see cref="ImportExportSlotIndex"/>, with
    /// the IMPORTED/EXPORTED stamp overlay) beside a column of <b>Copy as JSON</b>, <b>Copy as TSV</b>, and
    /// <b>Import</b> buttons. Exports copy the slotted document to the clipboard; Import reads the clipboard
    /// and sends a server-authoritative import. Buttons enable from the live slot contents.</summary>
    private Widget BuildImportExportSection(SlotController controller, ColorScheme colors, Vector4 bookColor, Vector4 veilColor)
    {
        var slotStack = scriptorium.Inventory[ImportExportSlotIndex].Itemstack;
        bool hasDoc = slotStack != null
            && ScribeDocumentAttributes.TryReadFrom(slotStack, out var doc) && doc is not null;
        bool hasItem = slotStack != null;
        bool writeable = ImportExportSlotWriteable();

        // Enabled only when the slot holds a readable document; otherwise a tooltip explains why.
        Widget ExportButton(string labelKey, System.Action onTap) => hasDoc
            ? LabelButton(Lang.Get("scribe:" + labelKey), colors, enabled: true, ButtonVariant.Primary, onTap, center: true)
            : WithTooltip("scribe-transcribe-export-empty",
                LabelButton(Lang.Get("scribe:" + labelKey), colors, enabled: false, ButtonVariant.Primary, center: true));

        var controls = new SizedBox(width: IoControlsWidth, child: new Column(
            spacing: 6f,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                ExportButton("scribe-transcribe-export-json", OnClickExportJson),
                ExportButton("scribe-transcribe-export-tsv", OnClickExportTsv),
                BuildImportButton(colors, hasItem, writeable),
            }));

        return new Row(
            spacing: 14f,
            mainAxisAlignment: MainAxisAlignment.Center,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                LabeledSlot(scriptorium.Inventory[ImportExportSlotIndex], "scribe-transcribe-io-slot",
                    controller, colors, bookColor, veilColor, overlay: BuildStampOverlay(ImportExportSlotIndex),
                    captionWidth: IoCaptionWidth),
                controls,
            });
    }

    /// <summary>The Import button: one press in Append mode or onto an empty target; otherwise the same red
    /// two-press confirm as Copy (<see cref="importConfirmState"/>). Disabled with a tooltip when the slot is
    /// empty or read-only. Whether the clipboard payload fits is re-checked server-side.</summary>
    private Widget BuildImportButton(ColorScheme colors, bool hasItem, bool writeable)
    {
        if (!hasItem)
        {
            importConfirmState = TranscribeConfirm.Idle;
            return WithTooltip("scribe-transcribe-import-empty",
                LabelButton(Lang.Get("scribe:scribe-transcribe-import"), colors, enabled: false, ButtonVariant.Primary, center: true));
        }
        if (!writeable)
        {
            importConfirmState = TranscribeConfirm.Idle;
            return WithTooltip("scribe-transcribe-import-readonly",
                LabelButton(Lang.Get("scribe:scribe-transcribe-import"), colors, enabled: false, ButtonVariant.Primary, center: true));
        }

        bool confirming = copyMode == CopyMode.Overwrite
            && importConfirmState == TranscribeConfirm.ConfirmOverwrite
            && ImportExportSlotTaskCount() > 0;
        if (copyMode == CopyMode.Append) importConfirmState = TranscribeConfirm.Idle;

        string label = confirming
            ? Lang.Get("scribe:scribe-transcribe-import-confirm")
            : Lang.Get("scribe:scribe-transcribe-import");
        return LabelButton(label, colors, enabled: true, confirming ? ButtonVariant.Danger : ButtonVariant.Primary,
            OnImportPressed, center: true);
    }

    /// <summary>Whether the Import/Export slot's item can be imported onto; empty counts as writeable, a
    /// hard or fired tablet does not.</summary>
    private bool ImportExportSlotWriteable()
    {
        var slot = scriptorium.Inventory[ImportExportSlotIndex];
        if (slot.Itemstack?.Collectible is IScribeDocumentItem item)
            return !item.DocumentPolicy(slot).ReadOnly;
        return true;
    }

    /// <summary>Completable-task count on the slot's document (0 if none); gates the overwrite confirm.</summary>
    private int ImportExportSlotTaskCount()
    {
        var stack = scriptorium.Inventory[ImportExportSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.CompletableCount;
        return 0;
    }

    /// <summary>The document on the slot's item, or null; read fresh from the synced stack (no packet).</summary>
    private ScribeDocument? ImportExportSlotDoc()
    {
        var stack = scriptorium.Inventory[ImportExportSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc;
        return null;
    }

    /// <summary>Copy the slotted document to the clipboard as lossless JSON and stamp EXPORTED.</summary>
    private void OnClickExportJson()
    {
        if (ImportExportSlotDoc() is not { } doc) return;
        BuildOwner.GetClipboard()?.SetText(ScribeDocumentJsonCodec.Serialize(doc));
        PlayStamp(ImportExportSlotIndex, Lang.Get("scribe:scribe-transcribe-stamp-imprint-exported"));
        capi.TriggerIngameDiscovery(this, "scribe-transcribe-export", Lang.Get("scribe:scribe-transcribe-export-done", "JSON"));
    }

    /// <summary>Copy the slotted document to the clipboard as a spreadsheet-friendly TSV table and stamp EXPORTED.</summary>
    private void OnClickExportTsv()
    {
        if (ImportExportSlotDoc() is not { } doc) return;
        BuildOwner.GetClipboard()?.SetText(ScribeDocumentTsvCodec.Serialize(doc));
        PlayStamp(ImportExportSlotIndex, Lang.Get("scribe:scribe-transcribe-stamp-imprint-exported"));
        capi.TriggerIngameDiscovery(this, "scribe-transcribe-export", Lang.Get("scribe:scribe-transcribe-export-done", "TSV"));
    }

    /// <summary>Reads the clipboard, auto-detects JSON vs TSV (a trimmed payload starting with <c>{</c> is
    /// JSON), parses it with the Core codec, and validates item references
    /// (<see cref="ScribeImportValidator"/>). An unparseable payload shows an error and does nothing.
    /// Confirmation follows the Copy button. The clipboard is re-read on the confirming press, so a changed
    /// payload imports its current content.</summary>
    private void OnImportPressed()
    {
        string? clip = BuildOwner.GetClipboard()?.GetText();
        if (string.IsNullOrWhiteSpace(clip))
        {
            capi.TriggerIngameError(this, "scribe-transcribe-import", Lang.Get("scribe:scribe-transcribe-import-invalid"));
            return;
        }

        bool isJson = clip.TrimStart().StartsWith('{');
        bool parsed = isJson
            ? ScribeDocumentJsonCodec.TryDeserialize(clip, out var doc)
            : ScribeDocumentTsvCodec.TryDeserialize(clip, out doc);
        if (!parsed || doc is null)
        {
            capi.TriggerIngameError(this, "scribe-transcribe-import", Lang.Get("scribe:scribe-transcribe-import-invalid"));
            return;
        }

        // Best-effort reconstruction: unresolved item/link references degrade to plain tasks.
        var result = ScribeImportValidator.Validate(capi.World, doc);

        if (copyMode == CopyMode.Overwrite && ImportExportSlotTaskCount() > 0
            && importConfirmState == TranscribeConfirm.Idle)
        {
            importConfirmState = TranscribeConfirm.ConfirmOverwrite;
            RebuildBody();
            return;
        }

        bool append = copyMode == CopyMode.Append;
        SendTranscribeImport(result.Document, append: append, allowOverwrite: !append);
        importConfirmState = TranscribeConfirm.Idle;
        PlayStamp(ImportExportSlotIndex, Lang.Get("scribe:scribe-transcribe-stamp-imprint-imported"));

        int tasks = result.Document.CompletableCount;
        string message = result.Degraded > 0
            ? Lang.Get("scribe:scribe-transcribe-import-result-degraded", tasks, result.Degraded)
            : Lang.Get("scribe:scribe-transcribe-import-result", tasks);
        capi.TriggerIngameDiscovery(this, "scribe-transcribe-import", message);
    }

    /// <summary>Sends the import request as JSON (the one wire format the server parses); the server
    /// re-deserializes it (minting fresh ids) and re-checks capacity.</summary>
    private void SendTranscribeImport(ScribeDocument document, bool append, bool allowOverwrite)
    {
        var pos = scriptorium.Pos;
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeTranscribeImportMessage
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            TargetSlot = ImportExportSlotIndex,
            DocumentJson = ScribeDocumentJsonCodec.Serialize(document),
            Append = append,
            AllowOverwrite = allowOverwrite,
        });
    }

    /// <summary>One slot with an always-present <c>scribebook</c> watermark layered UNDER the slot, whose
    /// fill is a translucent veil. Under-layering keeps the slot on top owning the full-slot gesture region
    /// (an over-layered glyph would block drops at the centre) and draws placed items above the book with no
    /// z-order race. <c>RenderStack</c> sizes to the sole non-positioned child (the slot) and paints in list
    /// order, so the <see cref="Positioned"/> book (first) paints under it.
    ///
    /// <para>The slot is our <see cref="ScribeDocumentSlot"/> rather than <see cref="FlatItemSlot"/>: same box
    /// + item, but hover shows a compact Scribe document-summary card instead of LibGUI's fixed 350px tooltip.</para></summary>
    private static Widget BuildWatermarkedSlot(ItemSlot? slot, SlotController controller, ColorScheme colors,
        ScribeAmbientLightSampler.Shade shade, Vector4 bookColor, Vector4 veilColor)
    {
        float glyph = SlotSize * WatermarkScale;
        float inset = (SlotSize - glyph) / 2f;
        // The slot's fill veils the book; the default border keeps it reading as a slot.
        var slotStyle = ItemSlotStyle.Default with { BackgroundColor = veilColor };
        return new Stack(children: new Widget[]
        {
            new Positioned(
                left: inset, top: inset, width: glyph, height: glyph,
                child: new ScribeVsIconGlyph("scribebook", glyph, bookColor)),
            new ScribeDocumentSlot(slot, controller, slotStyle, colors, shade),
        });
    }

    /// <summary>Idempotently creates the slot controller and watches the inventory.
    ///
    /// <para>The body must rebuild on every slot change: <c>FlatItemSlot</c> repaints itself via its
    /// stateful gesture layer, but our stateless <see cref="ScribeDocumentSlot"/> drops that layer, so a
    /// just-placed item would stay invisible until an unrelated rebuild. The deferred <c>SetState</c> is
    /// safe even when <c>SlotModified</c> fires mid-pointer-dispatch. Disposing the controller in
    /// <see cref="OnGuiClosed"/> clears the listener.</para></summary>
    private SlotController EnsureSlotController()
    {
        if (slotController == null)
        {
            slotController = new SlotController(capi);
            slotController.WatchInventory(scriptorium.Inventory);
            // Any slot change resets the confirms so a stale "overwrite N tasks" never stays armed
            // against different contents.
            slotController.AddListener(OnSlotsChanged);
        }
        return slotController;
    }

    /// <summary>Slot-change hook: cancels any armed overwrite confirm, then rebuilds the body.</summary>
    private void OnSlotsChanged()
    {
        confirmState = TranscribeConfirm.Idle;
        importConfirmState = TranscribeConfirm.Idle;
        RebuildBody();
    }

    /// <summary>Disposes the slot controller so its <c>SlotModified</c> subscription doesn't outlive the
    /// dialog. The base chain fires <c>CloseInventoryAndSync</c> first.</summary>
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (slotController != null)
        {
            slotController.UnwatchInventory(scriptorium.Inventory);
            slotController.Dispose();
            slotController = null;
        }
        // Stops in-flight stamp tickers.
        stampRegistry.Dispose();
        stampTargetSlot = -1;
    }
}
