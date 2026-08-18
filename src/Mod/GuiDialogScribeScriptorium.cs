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

    /// <summary>The Scriptorium inventory slot indices. Slot 0 is the copy Original (source), slot 1 the copy
    /// Duplicate (target), and slot 2 the Import/Export source-and-target — the item a JSON/TSV export reads
    /// from and an import writes onto (add-scriptorium-import-export). All three are real
    /// <see cref="BlockEntityScriptorium"/> slots watched by the same <see cref="slotController"/>.</summary>
    private const int SourceSlotIndex = 0;
    private const int TargetSlotIndex = 1;
    private const int ImportExportSlotIndex = 2;

    /// <summary>The seal button's two-press overwrite-confirm state (add-transcribe-copy-paste D3). Held on
    /// the dialog (client-only UX); the server re-checks the overwrite gate regardless. Reset to
    /// <see cref="TranscribeConfirm.Idle"/> whenever a slot's contents change (see
    /// <see cref="EnsureSlotController"/>).</summary>
    private enum TranscribeConfirm { Idle, ConfirmOverwrite }
    private TranscribeConfirm confirmState = TranscribeConfirm.Idle;

    /// <summary>The Import button's own two-press overwrite-confirm state — the peer of <see cref="confirmState"/>
    /// for the Import/Export slot (add-scriptorium-import-export). Kept separate from the copy confirm because the
    /// two buttons target different slots; both reset to Idle whenever any slot's contents change (see
    /// <see cref="OnSlotsChanged"/>). Only meaningful in <see cref="CopyMode.Overwrite"/> against a non-empty
    /// target; Append and an empty target import on a single press.</summary>
    private TranscribeConfirm importConfirmState = TranscribeConfirm.Idle;

    /// <summary>The copy BEHAVIOR selected by the radio under the Copy button (2026-08-17).
    /// <see cref="CopyMode.Overwrite"/> (default) REPLACES the target document — the original behavior, with the
    /// two-press <see cref="confirmState"/> guard when the target is non-empty. <see cref="CopyMode.Append"/> adds
    /// the source's tasks onto the target's existing document without deleting anything, so it needs no confirm
    /// and copies on a single press. Client-only UX; the server re-checks capacity and applies the chosen mode
    /// (<see cref="ScribeTranscribeCopyMessage.Append"/>).</summary>
    private enum CopyMode { Overwrite, Append }
    private CopyMode copyMode = CopyMode.Overwrite;

    /// <summary>The wooden rubber-stamp flourish (add-transcribe-copy-paste D4). Non-load-bearing: whatever it
    /// marks (a copy today; an import/export in the next section) is already done — server-authoritative — by the
    /// time this plays. <see cref="stampRegistry"/> owns the animation controller so the motion survives the
    /// per-frame body reconcile; <see cref="stampGeneration"/> bumps on each play so a re-play remounts a fresh
    /// <see cref="ScribeStamp"/> (new key + id) and replays rather than reusing the completed controller.
    ///
    /// <para><b>Generalized (2026-08-17):</b> the flourish is no longer wired to a single hard-coded slot + fixed
    /// "COPIED" text. <see cref="stampTargetSlot"/> names the inventory slot the overlay sits on (−1 = nothing
    /// playing), and <see cref="stampLabel"/> is the imprint text. <see cref="PlayStamp"/> takes both, so any
    /// slot can be stamped with any word ("COPIED", "Imported", "Exported") — see <see cref="BuildStampOverlay"/>,
    /// which mounts the overlay only on the slot whose index matches <see cref="stampTargetSlot"/>.</para></summary>
    private readonly ScribeAnimationRegistry stampRegistry = new();
    private int stampGeneration;
    /// <summary>Inventory slot index the flourish is currently playing over, or −1 when nothing is playing.</summary>
    private int stampTargetSlot = -1;
    /// <summary>The imprint text the current flourish stamps (e.g. "COPIED"); only meaningful while
    /// <see cref="stampTargetSlot"/> is not −1.</summary>
    private string stampLabel = "";

    /// <summary>The pixel-art wooden stamp PNG (baked by <c>build/gen-copy-stamp.py</c>), scaled up
    /// nearest-neighbour by <see cref="ScribeStamp"/>. Swappable art — same path, no code change to repaint.</summary>
    private static readonly AssetLocation StampAsset = new("scribe", "textures/gui/scribe-copy-stamp.png");

    /// <summary>Ink-red for the "COPY" imprint, matched to the rubber face in the stamp PNG.</summary>
    private static readonly Vector4 ImprintInk = new(0.66f, 0.18f, 0.16f, 1f);

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

        // Scriptorium-only Transcribe tab: the two-slot document-copy surface (add-transcribe-copy-paste).
        yield return TitleButton(
            "scribeinventory",
            "scribe-tab-transcribe",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToInventory,
            boxShadows: NavButtonShadow,
            activeColor: IsInventoryView ? ScribeRowConstants.NavActiveTranscribe : null);
    }

    /// <summary>Slot edge length in pixels — matches LibGUI's <c>SlotGrid</c>/<c>ItemSlotStyle.Default</c> so
    /// the hand-built row of slots looks identical to a stock grid.</summary>
    private const float SlotSize = 48f;

    /// <summary>Gap between the two slots, matching <c>SlotGrid</c>'s default spacing.</summary>
    private const float SlotSpacing = 4f;

    /// <summary>Fixed width of the "→" glyph cell between the copy slots. Bounded (not Center-auto-sized) so the
    /// arrow can't balloon to the full page width — see <see cref="BuildCopySection"/>.</summary>
    private const float ArrowCellWidth = 34f;

    /// <summary>Fixed width of the stacked import/export controls column. Bounded so the buttons size to a tidy
    /// column instead of a <see cref="CrossAxisAlignment.Stretch"/> Column ballooning to the full page width.
    /// Narrowed 200 → 140 (70%, refinement) after the buttons read as too wide for their short labels; their
    /// text is centred within the stretched button (see <see cref="LabelButton"/>'s <c>center</c> option).</summary>
    private const float IoControlsWidth = 140f;

    /// <summary>Width the import/export slot caption wraps within (under its placeholder slot), so the long
    /// "Note to export from…" text stacks into a few short lines instead of stretching the section wide.</summary>
    private const float IoCaptionWidth = 96f;

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

        // All heading + button + caption + arrow text on this tab renders in Caudex (refinement #7), the same
        // bundled face as the dialog title and the editor's text buttons (ScribeTaskFont.ButtonFamily).
        var headingStyle = new TextStyle
        {
            FontSize = 16, Weight = FontWeight.Bold, Color = colors.OnSurface, FontFamily = ScribeTaskFont.ButtonFamily,
        };

        // Vertical 50/50 split (refinement #6): the top half is the copy mechanic, the bottom half the
        // (unwired) import/export placeholder, each vertically CENTRED within its zone. Two Expanded halves
        // divide the fixed central region (given height InnerH by the SectionInnerBox's stretched Row), so the
        // split is exact and needs no scroll — this fixed layout supersedes task 3.4's scroll mitigation now
        // that the button sits below the slots and the content fits the region at every supported size.
        var topZone = new Center(child: new Column(
            spacing: 10,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-transcribe-heading"), headingStyle),
                BuildCopySection(controller, colors, bookColor, veilColor),
                BuildSealButton(colors),   // the Copy button sits BELOW the slot pair (refinement #4)
                BuildCopyModeRadio(colors),// Overwrite / Append behavior radio, directly under the Copy button
            }));

        var bottomZone = new Center(child: new Column(
            spacing: 10,
            crossAxisAlignment: CrossAxisAlignment.Center,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Text(Lang.Get("scribe:scribe-transcribe-io-heading"), headingStyle),
                BuildImportExportSection(controller, colors, bookColor, veilColor),
            }));

        // A theme-border Divider as the FIRST element, right under the dialog title bar — the same
        // separator the read view puts atop its scrolling section (ScribeReadContent), so the Transcribe
        // tab's title reads as distinct from its two content sections (refinement round 3). A second Divider
        // still splits the copy zone from the import/export zone below.
        var content = new Padding(EdgeInsets.All(10f), new Column(
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Max,
            children: new Widget[]
            {
                new Divider(),
                new Expanded(child: topZone),
                new Divider(),
                new Expanded(child: bottomZone),
            }));

        // A bottom-right "Show / hide Transcribe features" info button, overlaid on the tab (refinement round
        // 3) — the peer of the editor's "Show / hide Editor Features" button, toggling this tab's own handbook
        // guide page. Stacked so it pins to the corner without disturbing the 50/50 split above it. Nudged up
        // and left off the corner by 3% of the Pixel Art Size (on top of the 4px base inset) so it clears the
        // page edge at every art size (refinement round 3).
        float cornerInset = 4f + ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) * 0.01f;
        return new Stack(children: new Widget[]
        {
            content,
            new Positioned(right: cornerInset, bottom: cornerInset, child: BuildFeaturesHelpButton(colors)),
        });
    }

    /// <summary>The bottom-right "Show / hide Transcribe features" info button (refinement round 3): the
    /// Transcribe-tab peer of the editor footer's "Show / hide Editor Features" button. Same LibGUI
    /// <see cref="Button"/> on the theme's <c>ButtonStyle</c> (tight <c>All(7)</c> padding so it's a small
    /// square, matching the editor's icon button) with the same scaled ⓘ glyph in the button foreground, and
    /// the same toggle behaviour — <see cref="ScribeDialogBase.ToggleHandbookPage"/> opens (or, if already
    /// open, closes) the tab's own handbook guide page. An explainer tooltip labels it.
    ///
    /// <para>The <c>ButtonStyle</c> comes from <see cref="ScribeTheme.For"/> rather than
    /// <c>Theme.Of(context)</c> because these content builders don't receive a <see cref="BuildContext"/>;
    /// the resolved style is the same one the ambient <c>Theme</c> would hand a null-styled button.</para></summary>
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

    /// <summary>The copy pair: the Original slot, a Caudex "→" arrow, and the Duplicate slot, in a centered
    /// row (the Copy button now sits BELOW this row — refinement #4). Each slot carries a caption underneath.
    /// The arrow uses the same U+2192 glyph the editors substitute for a typed <c>-&gt;</c>
    /// (<see cref="ScribeArrowDigraph.RightArrow"/>) so the "Original → Duplicate" direction reads at a glance
    /// (refinement #8); it is boxed to the slot height and centred so it lines up with the slots, not their
    /// captions.</summary>
    private Widget BuildCopySection(SlotController controller, ColorScheme colors, Vector4 bookColor, Vector4 veilColor)
    {
        var inv = scriptorium.Inventory;
        // The arrow is boxed to a FIXED width as well as the slot height: a Center (RenderPositionedBox) only
        // shrink-wraps when its incoming width is unbounded — given a finite max (which the copy Row passes it),
        // it grows to that max. An unbounded-width Center here ballooned to the full page, shoving the Original
        // slot off the left edge and the Duplicate off the right (the "stretched to full width" bug). Bounding
        // both axes pins it to a small glyph cell that the Row can hug.
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

    /// <summary>One captioned copy slot: the watermarked <see cref="ScribeDocumentSlot"/> above its role label.
    /// When <paramref name="overlay"/> is supplied (the Duplicate slot's copy flourish) it is stacked ON TOP of
    /// the slot, sharing the slot's footprint but free to overflow (paint-only) as the stamp descends.</summary>
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
        // A short role label sits on one line; a longer caption (the Import/Export slot's) is bounded to
        // captionWidth so it wraps into a few centred lines under the slot instead of stretching the Row wide.
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

    /// <summary>The stamp flourish overlaid on <paramref name="slotIndex"/>, or <c>null</c> when nothing is
    /// playing over THAT slot (D4). The dialog asks each captioned slot for its overlay; only the slot whose index
    /// matches the active <see cref="stampTargetSlot"/> gets one, so the same call site serves the copy's Duplicate
    /// slot today and the import/export slot in the next section. A fresh <see cref="ScribeStamp"/> is created each
    /// rebuild while a play is active; its <c>ValueKey</c> + id carry the current <see cref="stampGeneration"/> so
    /// a re-play remounts and replays. The wooden-stamp bitmap is loaded once and cached by the mod system
    /// (null-safe: a missing asset just drops the wooden image, keeping the imprint).</summary>
    private Widget? BuildStampOverlay(int slotIndex)
    {
        if (slotIndex != stampTargetSlot) return null;
        string id = StampId(stampGeneration);
        // Peg the stamp + imprint width to the player's Pixel Art Size setting (× 0.2, so the 600 default →
        // 120px) — larger than the 48px slot, spilling over its sides (refinement #2).
        float artWidth = ScribePlayerSettings.ClampPixelArtSize(modSystem.MySettings.PixelArtSize) * 0.2f;
        // Page-background colour for the imprint's outer glow: the theme's parchment Surface at 0.6 alpha
        // (refinement 2026-08-17), so the dark-red "COPIED" reads over whatever sits under the slot without the
        // glow reading as a solid plate (and so the dark landing shadow above it still contrasts).
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        return new ScribeStamp(
            id: id,
            registry: stampRegistry,
            stampBitmap: modSystem.GetGuiTextureBitmap(StampAsset),
            copyLabel: stampLabel,
            imprintColor: ImprintInk,
            glowColor: colors.Surface with { W = 0.6f },
            slotSize: SlotSize,
            artWidth: artWidth,
            onEnd: () => OnStampEnded(id),
            key: new ValueKey<string>(id));
    }

    /// <summary>The Copy button (D3/D4), sitting below the slot pair (refinement #4). Disabled (greyed, with an
    /// explainer tooltip) until both copy slots hold a Scribe item AND the Duplicate is a valid target —
    /// writeable and with room for the source's tasks (refinement #1). When the target already has tasks, the
    /// first press flips the button to a red "overwrite N tasks" confirm; the second press sends the copy. An
    /// empty target copies on a single press. Label/variant derive from the live slot contents +
    /// <see cref="confirmState"/>; styled like the editor's "Done editing" button (thematic Button, Caudex
    /// label) but sized to its text.</summary>
    private Widget BuildSealButton(ColorScheme colors)
    {
        var inv = scriptorium.Inventory;
        bool bothFilled = inv[SourceSlotIndex].Itemstack != null && inv[TargetSlotIndex].Itemstack != null;

        // The can't-copy affordance: a REAL disabled Button (enabled:false) wrapped in an explainer tooltip —
        // NOT a hand-rolled Container. A disabled Button renders in the exact theme as the editor's "Done
        // editing" button (default ButtonStyle, Caudex label) at 0.45 opacity, so it reads as a proper greyed
        // button instead of an off-theme box; enabled:false skips ButtonState's press-sound path entirely, so
        // the earlier "avoid a live Button here" caution doesn't apply. It hugs its label like the live button
        // (refinement #5).
        Widget Disabled(string tooltipKey, params object[] tooltipArgs) => WithTooltip(
            tooltipKey,
            LabelButton(Lang.Get("scribe:scribe-transcribe-stamp"), colors, enabled: false, ButtonVariant.Primary),
            tooltipArgs);

        if (!bothFilled)
        {
            confirmState = TranscribeConfirm.Idle; // can't be mid-confirm without both slots filled
            return Disabled("scribe-transcribe-stamp-disabled");
        }

        // Valid-target gate (refinement #1): the Duplicate must be WRITEABLE (not hardened/fired) AND have
        // ROOM for the source's tasks. Both facts come from the target item's document policy — a read-only
        // tablet reports ReadOnly; a wet tablet caps at 10 task blocks. The server re-checks the same policy
        // in OnServerReceivedTranscribeCopy, so this gate is UX, not the authority.
        var targetPolicy = TargetPolicy();
        if (targetPolicy.ReadOnly)
        {
            confirmState = TranscribeConfirm.Idle;
            return Disabled("scribe-transcribe-stamp-readonly");
        }
        // Resulting block count depends on the mode: Overwrite REPLACES (result = source's blocks), Append ADDS
        // onto the target's existing blocks (result = target's + source's). The server re-checks this same sum.
        int resultingBlocks = copyMode == CopyMode.Append
            ? TargetTaskBlockCount() + SourceTaskCount()
            : SourceTaskCount();
        if (!targetPolicy.CanHold(resultingBlocks))
        {
            confirmState = TranscribeConfirm.Idle;
            // A finite cap is implied here: a non-read-only policy fails CanHold only when MaxBlocks is set.
            return Disabled("scribe-transcribe-stamp-toobig", targetPolicy.MaxBlocks ?? 0);
        }

        // Append is non-destructive, so it never arms the overwrite confirm — a single press always sends. Only
        // Overwrite mode confirms, and only when the target already has completable tasks to clobber.
        int targetTasks = TargetTaskCount();
        bool confirming = copyMode == CopyMode.Overwrite
            && confirmState == TranscribeConfirm.ConfirmOverwrite && targetTasks > 0;
        if (copyMode == CopyMode.Append) confirmState = TranscribeConfirm.Idle;

        string label = confirming
            ? Lang.Get("scribe:scribe-transcribe-stamp-confirm")
            : Lang.Get("scribe:scribe-transcribe-stamp");

        // Same styling as the editor's "Done editing" button — a plain thematic LibGUI Button on the default
        // theme ButtonStyle (no explicit style), its label in Caudex (#7) — but NOT wrapped in Expanded, so it
        // hugs its text (#5). Being a live Button it inherits the standard hover-grow / press-shrink feedback
        // the rest of the Scribe UI has. The confirming state flips to Danger to signal the destructive overwrite.
        return LabelButton(label, colors, enabled: true, confirming ? ButtonVariant.Danger : ButtonVariant.Primary,
            onTap: OnSealPressed);
    }

    /// <summary>The copy-behavior radio set, sitting directly under the Copy button (2026-08-17): a
    /// <see cref="CopyMode.Overwrite"/> / <see cref="CopyMode.Append"/> choice built from LibGUI's
    /// <see cref="RadioButton{T}"/>. <c>RadioButton</c> is constrained to <c>IEquatable&lt;T&gt;</c>, which a bare
    /// enum does not satisfy, so the group value is the enum cast to <c>int</c> (mirroring the framework's own
    /// examples). The label uses the PLAYER'S chosen body font (<see cref="ScribeTaskFont.Resolve"/> of the
    /// task-font setting), NOT Caudex — on this tab only the buttons are Caudex; everything else follows the
    /// player's text preference. Circle/dot/border colours come from the active theme so the set matches the
    /// dialog's parchment palette. Switching modes clears any armed overwrite confirm.
    ///
    /// <para>The two options sit side-by-side in one Row, but each is wrapped in a fixed-width <see cref="SizedBox"/>:
    /// a <c>RadioButton</c> lays its indicator+label out in an internal <see cref="MainAxisSize.Max"/> Row, so given
    /// unbounded width it balloons to fill the page — placed raw side-by-side, the first filled the width and pushed
    /// "Append" off the right edge (the observed bug). Bounding each to a width that hugs its own label keeps both on
    /// one line, and the Row centres as a unit under the Copy button. The per-option widths are sized to their
    /// labels (Overwrite is the longer word); if a font makes a label clip, widen its box here.</para></summary>
    private Widget BuildCopyModeRadio(ColorScheme colors)
    {
        var labelStyle = new TextStyle
        {
            FontSize = 14,
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
            confirmState = TranscribeConfirm.Idle;       // a mode switch cancels any armed copy overwrite confirm
            importConfirmState = TranscribeConfirm.Idle;  // …and the import one
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

    /// <summary>A thematic label Button matching the editor's "Done editing"/"Add task" footer buttons: the
    /// default theme <c>ButtonStyle</c> (no explicit style override) with a Caudex label in the variant's
    /// foreground colour, so it inherits the identical background, border, padding, and hover-grow/press-shrink
    /// feedback. Non-<c>Expanded</c>, so it hugs its label (refinement #5). A disabled button (<paramref
    /// name="enabled"/> false) renders the same shape at 0.45 opacity and never fires its tap/press-sound path.
    ///
    /// <para>When <paramref name="center"/> is set the label is centred within a button whose WIDTH is fixed by
    /// an outer stretch (the import/export column's <see cref="CrossAxisAlignment.Stretch"/>). Centring uses a
    /// full-width <see cref="Row"/> (<see cref="MainAxisSize.Max"/> + <see cref="MainAxisAlignment.Center"/>),
    /// NOT a <see cref="Gui.Widgets.Layout.Center"/>: a <c>Center</c> (RenderPositionedBox) grows to fill BOTH
    /// finite axes it's given, so wrapping the label in one ballooned the button to the full height of its
    /// <see cref="Expanded"/> zone (the "IO button fills 100% of the container" bug). A Row only fills its main
    /// (horizontal) axis to the bounded button width and hugs its children vertically, so the label centres
    /// without the button growing tall. Left off for the hugging Copy button, whose child already fills its own
    /// width.</para></summary>
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

    /// <summary>The number of TASK blocks on the Original (source) item — what a copy would place onto the
    /// Duplicate (a copy REPLACES the target document, so the result's task count equals the source's). 0 when
    /// the source is empty or carries no document, so an empty source always fits any writeable target (the
    /// source may be any Scribe object, even an empty one — refinement #1).</summary>
    private int SourceTaskCount()
    {
        var stack = scriptorium.Inventory[SourceSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.TaskCount;
        return 0;
    }

    /// <summary>The Duplicate (target) item's document policy — capacity + editability. Uncapped and writeable
    /// when the target is empty or isn't an <see cref="IScribeDocumentItem"/>; a Tablet reports its live
    /// wet/hard/fired policy. Drives the client-side valid-target gate; the server re-checks the same policy.</summary>
    private ScribeDocumentPolicy TargetPolicy()
    {
        var slot = scriptorium.Inventory[TargetSlotIndex];
        if (slot.Itemstack?.Collectible is IScribeDocumentItem item)
            return item.DocumentPolicy(slot);
        return ScribeDocumentPolicy.Unlimited;
    }

    /// <summary>Number of completable tasks on the Duplicate (target) item, read from its synced document
    /// (Core's <see cref="Scribe.Core.ScribeDocument.CompletableCount"/>). 0 when the target is empty or has
    /// no document — the "empty target" case that copies without a confirm.</summary>
    private int TargetTaskCount()
    {
        var stack = scriptorium.Inventory[TargetSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.CompletableCount;
        return 0;
    }

    /// <summary>Number of TASK BLOCKS already on the Duplicate (target) — the capacity measure the policy cap
    /// counts (<see cref="Scribe.Core.ScribeDocument.TaskCount"/>), distinct from <see cref="TargetTaskCount"/>'s
    /// completable count. Used only for the Append-mode capacity gate (existing blocks + source's blocks must
    /// fit). 0 when the target is empty or carries no document.</summary>
    private int TargetTaskBlockCount()
    {
        var stack = scriptorium.Inventory[TargetSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.TaskCount;
        return 0;
    }

    /// <summary>Handle a press of the (enabled) seal button. In <see cref="CopyMode.Append"/> a single press
    /// always sends (non-destructive, no confirm). In <see cref="CopyMode.Overwrite"/> an empty target copies
    /// immediately; a non-empty target arms the red confirm on the first press and commits on the second (D3).</summary>
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

    /// <summary>Play the "COPIED" flourish over the Duplicate slot — the copy path's use of the now-generalized
    /// <see cref="PlayStamp"/>. Both copy modes stamp the same word onto the same (target) slot; only the
    /// underlying document write differs.</summary>
    private void StampCopy() => PlayStamp(TargetSlotIndex, Lang.Get("scribe:scribe-transcribe-stamp-imprint"));

    /// <summary>Start (or restart) the wooden-stamp flourish over <paramref name="targetSlot"/>, stamping
    /// <paramref name="label"/>. Called only on an action that will succeed (a copy's empty target / confirming
    /// press today) — the client gates never fire a play the server would reject, so a played stamp always
    /// corresponds to a real change. Bumping the generation gives the next <see cref="ScribeStamp"/> a fresh
    /// key+id so the reconciler replays it from the start instead of reusing the just-completed controller (D4).</summary>
    private void PlayStamp(int targetSlot, string label)
    {
        stampGeneration++;
        stampTargetSlot = targetSlot;
        stampLabel = label;
        RebuildBody();
    }

    /// <summary>Fired once when a stamp play completes: release its controller and drop the overlay so the
    /// stamped item is shown unobstructed. Releases by the exact id the finished stamp used, so a play that
    /// re-armed a newer generation mid-flight isn't torn down by an older stamp's end.</summary>
    private void OnStampEnded(string id)
    {
        stampRegistry.Release(id);
        // Only clear the target if this was the CURRENT generation's stamp; a newer play may already be running.
        if (id == StampId(stampGeneration))
        {
            stampTargetSlot = -1;
            RebuildBody();
        }
    }

    private static string StampId(int generation) => $"transcribe-stamp:{generation}";

    /// <summary>Send the server-authoritative copy request for this Scriptorium's copy pair (D2). The server
    /// clones the source document with a fresh identity and writes it onto the target, syncing the result
    /// back through the inventory channel.</summary>
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

    /// <summary>The Import/Export section, now live (add-scriptorium-import-export D7): a real watermarked slot
    /// (index <see cref="ImportExportSlotIndex"/>, bound to the shared <paramref name="controller"/> like the copy
    /// slots and carrying the IMPORTED/EXPORTED stamp overlay) beside a fixed-width column of three Caudex
    /// buttons — <b>Copy as JSON</b>, <b>Copy as TSV</b>, and <b>Import</b>. Exports read the slotted item's
    /// document, serialize it to the clipboard, and stamp EXPORTED; Import reads the clipboard, auto-detects the
    /// format, validates against the game, and sends a server-authoritative import (stamping IMPORTED on success).
    /// The buttons enable/disable from the live slot contents (there is nothing to export from an empty slot, and
    /// nothing to import onto a read-only/empty one); the fixed <see cref="IoControlsWidth"/> keeps the column
    /// from ballooning to the page width (the same Stretch-Column bound the copy section uses).</summary>
    private Widget BuildImportExportSection(SlotController controller, ColorScheme colors, Vector4 bookColor, Vector4 veilColor)
    {
        var slotStack = scriptorium.Inventory[ImportExportSlotIndex].Itemstack;
        bool hasDoc = slotStack != null
            && ScribeDocumentAttributes.TryReadFrom(slotStack, out var doc) && doc is not null;
        bool hasItem = slotStack != null;
        bool writeable = ImportExportSlotWriteable();

        // A live export button, enabled only when the slot holds a readable document. Disabled → an explainer
        // tooltip, exactly like the copy button's disabled affordance.
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

    /// <summary>The Import button (D6): a single press imports in Append mode or onto an empty target; in
    /// Overwrite mode against a non-empty target it arms the same red two-press confirm the Copy button uses
    /// (<see cref="importConfirmState"/>). Disabled — with an explainer tooltip — when the slot is empty
    /// (nothing to import onto) or holds a read-only (hardened/fired) item. The confirming label mirrors the copy
    /// confirm wording. Whether the incoming clipboard actually fits is re-checked server-side (the client can't
    /// know the payload's task count until the button is pressed and the clipboard is read).</summary>
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

        // Append never arms the confirm; only Overwrite against a target that already has tasks does.
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

    /// <summary>Whether the Import/Export slot's item can be written (imported onto). Empty or a non-Scribe item
    /// (which can't reach this Scribe-only slot anyway) counts as writeable; a Tablet reports its live
    /// wet/hard/fired policy, so a hardened/fired one is read-only. Mirrors the copy path's target-writeability
    /// check.</summary>
    private bool ImportExportSlotWriteable()
    {
        var slot = scriptorium.Inventory[ImportExportSlotIndex];
        if (slot.Itemstack?.Collectible is IScribeDocumentItem item)
            return !item.DocumentPolicy(slot).ReadOnly;
        return true;
    }

    /// <summary>Completable-task count on the Import/Export slot's document — the "empty vs non-empty target"
    /// measure the overwrite confirm gates on (0 when the slot is empty or holds no document).</summary>
    private int ImportExportSlotTaskCount()
    {
        var stack = scriptorium.Inventory[ImportExportSlotIndex].Itemstack;
        if (stack != null && ScribeDocumentAttributes.TryReadFrom(stack, out var doc) && doc is not null)
            return doc.CompletableCount;
        return 0;
    }

    /// <summary>The document currently on the Import/Export slot's item, or null when the slot is empty or its
    /// item carries no Scribe document. Read fresh from the synced item stack each call (exports are a pure
    /// client-side read of already-synced state — no packet).</summary>
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
        if (ImportExportSlotDoc() is not { } doc) return; // button is disabled without a doc; belt-and-suspenders
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

    /// <summary>Handle a press of the (enabled) Import button (D4/D6). Reads the clipboard, auto-detects JSON vs
    /// TSV (a trimmed payload starting with <c>{</c> is JSON, otherwise TSV), parses it with the Core codec, and
    /// validates item references against the game (<see cref="ScribeImportValidator"/>). A payload that parses as
    /// neither surfaces the "not a valid Scribe export" error and does nothing. In Append mode, or Overwrite onto
    /// an empty target, a single press sends. In Overwrite onto a non-empty target the first press arms the red
    /// confirm and the second sends (mirroring the Copy button). The clipboard is re-read on the confirming press,
    /// so a payload that changed between presses imports its current content.</summary>
    private void OnImportPressed()
    {
        string? clip = BuildOwner.GetClipboard()?.GetText();
        if (string.IsNullOrWhiteSpace(clip))
        {
            capi.TriggerIngameError(this, "scribe-transcribe-import", Lang.Get("scribe:scribe-transcribe-import-invalid"));
            return;
        }

        // Auto-detect the format and parse via the API-free Core codec.
        bool isJson = clip.TrimStart().StartsWith('{');
        bool parsed = isJson
            ? ScribeDocumentJsonCodec.TryDeserialize(clip, out var doc)
            : ScribeDocumentTsvCodec.TryDeserialize(clip, out doc);
        if (!parsed || doc is null)
        {
            capi.TriggerIngameError(this, "scribe-transcribe-import", Lang.Get("scribe:scribe-transcribe-import-invalid"));
            return;
        }

        // Best-effort reconstruction: unresolved item/link references degrade to plain tasks (D5).
        var result = ScribeImportValidator.Validate(capi.World, doc);

        // Overwrite onto a non-empty target arms the two-press confirm; Append and empty targets send at once.
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

        // Report the outcome — task count, and any degraded references — as a positive discovery toast.
        int tasks = result.Document.CompletableCount;
        string message = result.Degraded > 0
            ? Lang.Get("scribe:scribe-transcribe-import-result-degraded", tasks, result.Degraded)
            : Lang.Get("scribe:scribe-transcribe-import-result", tasks);
        capi.TriggerIngameDiscovery(this, "scribe-transcribe-import", message);
    }

    /// <summary>Send the server-authoritative import request for the Import/Export slot (D6). The validated
    /// document is serialized to JSON (the one wire format the server parses); the server re-deserializes it
    /// (minting fresh ids), re-checks capacity, and writes onto the slot's item, syncing the result back through
    /// the inventory channel.</summary>
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
            // Any slot-content change resets the overwrite confirm to Idle (D3: "confirmation resets if the
            // slots change") and then re-renders — a copy landing on the target, or a player pulling/swapping
            // an item mid-confirm, must not leave a stale "overwrite N tasks" armed against different contents.
            slotController.AddListener(OnSlotsChanged);
        }
        return slotController;
    }

    /// <summary>Slot-change hook: cancel any armed overwrite confirm, then rebuild the body. See the listener
    /// registration in <see cref="EnsureSlotController"/> for why the reset lives here (D3).</summary>
    private void OnSlotsChanged()
    {
        confirmState = TranscribeConfirm.Idle;
        importConfirmState = TranscribeConfirm.Idle; // the import overwrite-confirm resets on any slot change too
        RebuildBody();
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
        // Dispose any in-flight stamp controllers so their tickers don't outlive the dialog.
        stampRegistry.Dispose();
        stampTargetSlot = -1;
    }
}
