using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Animations;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Painting;
using Gui.Core.Layout;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>A value snapshot of one HUD row. No "departing" flag: once a row's completion is sent its identity
/// leaves the item set (via <c>awaitingRemoval</c>) and <see cref="ScribeAnimatedList"/> collapses a frozen
/// ghost.</summary>
internal readonly record struct HudPinRow(
    Guid DocId, Guid TaskId, string Text, bool Done, bool Sunk, bool FadingOut,
    ScribeBlockKind Kind = ScribeBlockKind.Task, string? LinkTarget = null,
    // A pinned Tracker/Link carries its resolved item icon + name and have/need counts so the row renders item-shaped.
    ItemStack? DisplayStack = null, string? DisplayName = null,
    int TargetQuantity = 1, int CurrentQuantity = 0, int Depth = 0,
    bool IsStaticVsQuestObjective = false)
{
    public bool IsTracker => Kind == ScribeBlockKind.Tracker;
    public bool IsLink => Kind == ScribeBlockKind.Link;
    public bool IsCraft => Kind == ScribeBlockKind.Craft;
    public bool IsQuestObjective => Kind == ScribeBlockKind.QuestObjective;
    /// <summary>Item rows (icon + name) rather than editable text. A Craft parent renders like a Tracker, with a "Craft {0}" label.</summary>
    public bool IsItemKind => IsTracker || IsLink || IsCraft || IsQuestObjective;
    /// <summary>Kinds that DISPLAY a have/need counter. A QuestObjective's counter comes from Progression
    /// Framework, so this is deliberately not <see cref="ScribeBlock.IsCarriedCountTracked"/>.</summary>
    public bool HasCounterRow => IsTracker || IsCraft || IsQuestObjective;
    /// <summary>The label to show: the "Craft {0}" framing for a Craft parent, the plain resolved item
    /// name for a Tracker/Link, else the task text.</summary>
    public string Label => IsCraft
        ? Lang.Get("scribe:scribe-gui-craft-row-label", DisplayName ?? Text)
        : IsItemKind ? (DisplayName ?? Text) : Text;
}

/// <summary>
/// The HUD widget tree: a collapse header over a column of pin rows (header alone when collapsed). Separate
/// from <see cref="HudScribePins"/> to read the LibGUI <see cref="Theme"/> off the <see cref="BuildContext"/>;
/// stateless, with interaction routed through callbacks.
/// </summary>
internal sealed class HudPinsContent : StatelessWidget
{
    /// <summary>Row text glow radius (fraction of font size); keeps text legible over the world with no background plate.</summary>
    private const float GlowWidth = 0.45f;

    /// <summary>Opacity of a sunk (completed, past-undo) row — muted but still readable/undoable.</summary>
    private const float SunkOpacity = 0.5f;

    private readonly IReadOnlyList<HudPinRow> rows;
    private readonly int moreCount;
    /// <summary>Temporal-corruption strength (0..1); 0 = none. Applied via <see cref="Corrupt"/>.</summary>
    private readonly double corruptionStrength;
    /// <summary>Whether a temporal storm is active — drives only the title swap to "Survive the Storm".</summary>
    private readonly bool stormActive;
    /// <summary>Seed for the corruptor this build; the host advances it on the re-scramble cadence.</summary>
    private readonly int corruptionSeed;
    private readonly ScribeAnimationRegistry collapseRegistry;
    private readonly Action onDepartureSettled;
    private readonly bool collapsed;
    private readonly bool leftAligned;
    private readonly float rowWidth;
    private readonly float rowFontSize;
    private readonly float checkboxSize;
    /// <summary>Whether Tracker/Link rows render their item icon / guide-page book glyph on the HUD
    /// (<see cref="ScribePlayerSettings.HudShowIcons"/>); off gives a text-only HUD.</summary>
    private readonly bool showIcons;
    /// <summary>Whether the HUD header shows the settings gear (<see cref="ScribePlayerSettings.HudShowSettingsGear"/>).
    /// Off omits the gear; the Lectern Settings tab remains reachable.</summary>
    private readonly bool showSettingsGear;
    private readonly Action<Guid, Guid, bool> onToggleRow;
    /// <summary>Opens a pinned Link's Handbook page; wired only to a Link row's label.</summary>
    private readonly Action<string?> onOpenLink;
    private readonly Action onToggleCollapsed;
    private readonly Action onOpenSettings;
    private readonly Scribe.Core.TimerStore? timerData;
    private readonly Action? onClearTimer;
    private readonly ICoreClientAPI? capiForTimer;
    /// <summary>Pending Quest prompts; only the first renders as a banner.</summary>
    private readonly IReadOnlyList<ScribeQuestPrompt> questPrompts;
    /// <summary>Accept action for an accept-prompt banner with the resolved destination (sole candidate or picker choice); null for a completion-prompt.</summary>
    private readonly Action<ScribeQuestPrompt, ScribeAcceptCandidate?>? onAcceptPrompt;
    private readonly Action<ScribeQuestPrompt>? onDismissPrompt;
    /// <summary>This player's current Accept-placement candidates for the banner's accept-prompt
    /// (<see cref="ScribeModSystem.ComputeQuestAcceptCandidates"/>) — irrelevant for a completion-prompt.</summary>
    private readonly IReadOnlyList<ScribeAcceptCandidate> questAcceptCandidates;
    /// <summary>Whether the 2+-candidate picker is revealed (owned by <see cref="HudScribePins"/> as session state).</summary>
    private readonly bool questAcceptPickerOpen;
    private readonly int questAcceptSelectedIndex;
    private readonly Action? onOpenQuestAcceptPicker;
    private readonly Action<int>? onSelectQuestAcceptCandidate;

    public HudPinsContent(
        IReadOnlyList<HudPinRow> rows,
        int moreCount,
        double corruptionStrength,
        bool stormActive,
        int corruptionSeed,
        ScribeAnimationRegistry collapseRegistry,
        Action onDepartureSettled,
        bool collapsed,
        bool leftAligned,
        float rowWidth,
        float rowFontSize,
        float checkboxSize,
        bool showIcons,
        bool showSettingsGear,
        Action<Guid, Guid, bool> onToggleRow,
        Action<string?> onOpenLink,
        Action onToggleCollapsed,
        Action onOpenSettings,
        Scribe.Core.TimerStore? timerData = null,
        Action? onClearTimer = null,
        ICoreClientAPI? capiForTimer = null,
        IReadOnlyList<ScribeQuestPrompt>? questPrompts = null,
        Action<ScribeQuestPrompt, ScribeAcceptCandidate?>? onAcceptPrompt = null,
        Action<ScribeQuestPrompt>? onDismissPrompt = null,
        IReadOnlyList<ScribeAcceptCandidate>? questAcceptCandidates = null,
        bool questAcceptPickerOpen = false,
        int questAcceptSelectedIndex = 0,
        Action? onOpenQuestAcceptPicker = null,
        Action<int>? onSelectQuestAcceptCandidate = null)
    {
        this.rows = rows;
        this.moreCount = moreCount;
        this.corruptionStrength = corruptionStrength;
        this.stormActive = stormActive;
        this.corruptionSeed = corruptionSeed;
        this.collapseRegistry = collapseRegistry;
        this.onDepartureSettled = onDepartureSettled;
        this.collapsed = collapsed;
        this.leftAligned = leftAligned;
        this.rowWidth = rowWidth;
        this.rowFontSize = rowFontSize;
        this.checkboxSize = checkboxSize;
        this.showIcons = showIcons;
        this.showSettingsGear = showSettingsGear;
        this.onToggleRow = onToggleRow;
        this.onOpenLink = onOpenLink;
        this.onToggleCollapsed = onToggleCollapsed;
        this.onOpenSettings = onOpenSettings;
        this.timerData = timerData;
        this.onClearTimer = onClearTimer;
        this.capiForTimer = capiForTimer;
        this.questPrompts = questPrompts ?? Array.Empty<ScribeQuestPrompt>();
        this.onAcceptPrompt = onAcceptPrompt;
        this.questAcceptCandidates = questAcceptCandidates ?? Array.Empty<ScribeAcceptCandidate>();
        this.questAcceptPickerOpen = questAcceptPickerOpen;
        this.questAcceptSelectedIndex = questAcceptSelectedIndex;
        this.onOpenQuestAcceptPicker = onOpenQuestAcceptPicker;
        this.onSelectQuestAcceptCandidate = onSelectQuestAcceptCandidate;
        this.onDismissPrompt = onDismissPrompt;
    }

    /// <summary>Runs a user-visible string through the Core corruptor. <paramref name="seedOffset"/> varies the
    /// pattern per string while the frame still advances together; strength 0 returns the input unchanged.</summary>
    private string Corrupt(string text, int seedOffset = 0)
        => ScribeTextCorruptor.Corrupt(text, corruptionStrength, corruptionSeed + seedOffset);

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        // Fully opaque black glow for contrast over busy world backgrounds.
        Vector4 glow = new(0f, 0f, 0f, 1.0f);

        var children = new List<Widget> { BuildHeader(colors, glow) };

        // Quest prompt banner: shown regardless of collapse state so a collapsed HUD doesn't hide it. Wrapped in a
        // slide-in keyed by the prompt's own identity: a new prompt slides in, the same prompt across rebuilds stays settled. The "questprompt:" key prefix
        // keeps it distinct from any row's TaskId key in the shared collapseRegistry.
        if (questPrompts.Count > 0)
        {
            var bannerPrompt = questPrompts[0];
            string entryId = "questprompt:" + bannerPrompt.Source + ":" + bannerPrompt.QuestCode + ":" + bannerPrompt.IsCompletion;
            children.Add(new ScribeSlideIn(
                id: entryId,
                animating: true,
                registry: collapseRegistry,
                child: BuildQuestPromptBanner(colors, glow),
                key: new ValueKey<string>(entryId)));
        }

        if (!collapsed)
        {
            // Pin rows go through ScribeAnimatedList: it diffs the TaskId-keyed set, so a pin leaving it
            // (awaitingRemoval, filtered upstream) collapses via Immediate. Chrome (header, "+N more", timer) stays
            // outside as siblings. Live rows are keyed by TaskId so their State (fade controller, sink tween) survives
            // rebuilds. The live row is unsafe to freeze (its checkbox stays clickable mid-collapse), so each supplies a
            // static zero-opacity-text ghost matching the already-faded row. animateEntry defaults true: new or
            // newly-capped-in rows slide in.
            var items = rows
                .Select(r => new ScribeAnimatedListItem(
                    Id: r.TaskId,
                    Child: BuildRow(r, colors, glow),
                    Ghost: BuildFrozenGhost(r, colors, glow)))
                .ToList();

            children.Add(new ScribeAnimatedList(
                items: items,
                registry: collapseRegistry,
                policy: ScribeListRemovalPolicy.Immediate,
                onDepartureSettled: onDepartureSettled,
                // We own the layout: the container supplies the ordered live rows plus collapsing ghosts; same 4px
                // spacing as the outer Column so gaps read identically.
                layoutBuilder: laidOut => new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: CrossAxisAlignment.Stretch,
                    children: laidOut.ToList())));

            if (moreCount > 0)
            {
                children.Add(new Text(
                    Corrupt(Lang.Get("scribe:scribe-hud-more", moreCount), seedOffset: 101),
                    new TextStyle
                    {
                        // Desaturated 66%: the "+N more" footer and header gear share OnSurfaceVariant and read as a muted near-grey accent.
                        Color = ScribeRowConstants.ShiftBrightness(colors.OnSurfaceVariant, 0f, saturationScale: 0.34f),
                        FontSize = 13,
                        GlowWidth = GlowWidth,
                        GlowColor = glow,
                    }));
            }

            // Timer row below the pins, shown when a Notebook timer is Running or Fired.
            if (timerData is { Status: Scribe.Core.TimerStatus.Running or Scribe.Core.TimerStatus.Fired })
            {
                children.Add(new Divider());
                children.Add(BuildTimerRow(timerData, colors, glow, capiForTimer));
            }
        }

        // Fixed-width SizedBox so long tasks wrap instead of widening the shrink-wrap window. The Column aligns
        // toward the anchored edge (only the min-width header/footer visibly move; task rows fill the width).
        return ScribeTextDefaults.WrapChrome(new SizedBox(
            width: rowWidth,
            child: new Padding(
                EdgeInsets.All(2),
                child: new Column(
                    spacing: 4,
                    mainAxisSize: MainAxisSize.Min,
                    crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
                    children: children))));
    }

    /// <summary>The collapse header: clickable chevron + title, and a gear opening settings. Present collapsed
    /// too. The gear has its own GestureDetector so tapping it doesn't toggle collapse.</summary>
    private Widget BuildHeader(ColorScheme colors, Vector4 glow)
    {
        var titleStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = new Vector4(1f, 1f, 1f, 1f), // pure opaque white header
            GlowWidth = GlowWidth,
            GlowColor = glow,
        };

        // The chevron is a texture icon (same as the Inbox expand toggle), not a Unicode ▸/▾: no font on Linux
        // reliably carries those glyphs. VsIcon has no text glow, so a Container BoxShadow matches the halo (as
        // ScribeHudGearButton does).
        Widget chevronIcon = new Container(
            new BoxStyle
            {
                BoxShadows = new[]
                {
                    new BoxShadow(Color: new Vector4(0f, 0f, 0f, 0.3f), Offset: new Vector2(0f, 0f), BlurRadius: 2.25f),
                },
            },
            new ScribeVsIconGlyph(
                collapsed ? "scribetriangleright" : "scribetriangledown",
                rowFontSize * 0.6f, // scaled down ~40%: a full em reads too large next to the title
                titleStyle.Color));

        // Separate GestureDetectors so a gear tap doesn't also toggle collapse.
        var collapseToggle = new GestureDetector(
            onTap: _ => onToggleCollapsed(),
            child: new Row(
                spacing: 4,
                mainAxisSize: MainAxisSize.Min,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    chevronIcon,
                    new Text(
                        Corrupt(Lang.Get(stormActive ? "scribe:scribe-hud-title-storm" : "scribe:scribe-hud-title")),
                        titleStyle),
                }));

        var headerChildren = new List<Widget> { collapseToggle };
        if (showSettingsGear)
        {
            // Gear sized/nudged in em units off rowFontSize so it scales with the HUD text size; white resting color
            // matches the title. Self-stateful so hover survives rebuilds.
            headerChildren.Add(new ScribeHudGearButton(
                baseColor: new Vector4(1f, 1f, 1f, 1f),
                fontSize: rowFontSize,
                onTap: onOpenSettings));
        }

        return new Row(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: headerChildren);
    }

    /// <summary>The Quest prompt banner: Link/Dismiss/Settings buttons for the OLDEST banner-eligible prompt
    /// (<c>questPrompts[0]</c>; later prompts appear once it resolves). Buttons come from the shared
    /// <see cref="ScribeQuestPromptActions"/>, which <see cref="GuiDialogScribeQuestPrompt"/> also uses, so the
    /// styles can't drift.
    ///
    /// <para>An accept-prompt's Link follows the Inbox Accept 0/1/2+ rule: 0 carried documents disables it, 1
    /// accepts immediately, 2+ reveals a candidate picker on first tap and confirms on the second. A
    /// completion-prompt never shows a picker.</para></summary>
    private Widget BuildQuestPromptBanner(ColorScheme colors, Vector4 glow)
    {
        var prompt = questPrompts[0];
        var titleLabelStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = ScribeRowConstants.QuestPromptTitleColor, // warm gold — distinct from ordinary row text
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };
        // The quest name renders as ordinary HUD text below the gold label.
        var titleNameStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = ScribeRowConstants.HudStandardTextColor,
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        string titleLabel = Lang.Get(
            prompt.IsCompletion ? "scribe:scribe-hud-questprompt-complete-title" : "scribe:scribe-hud-questprompt-accept-title");
        string titleSuffix = Lang.Get(
            prompt.IsCompletion ? "scribe:scribe-hud-questprompt-complete-title-suffix" : "scribe:scribe-hud-questprompt-accept-title-suffix");
        string titleName = prompt.Title + titleSuffix;

        Widget titleBlock = new Column(
            spacing: 0,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
            children: new Widget[] { new Text(titleLabel, titleLabelStyle), new Text(titleName, titleNameStyle) });

        // Bordered, color-coded buttons (accept green, dismiss red, Settings neutral) from the shared helper. Labels
        // use the standard HUD row-text color, not the accent or pure white (TextStyle.Merge landmine; see
        // ScribeRowConstants.HudStandardTextColor).
        Vector4 promptActionTextColor = ScribeRowConstants.HudStandardTextColor;
        var (picker, linkButton) = ScribeQuestPromptActions.BuildLinkControl(
            prompt, questAcceptCandidates, questAcceptPickerOpen, questAcceptSelectedIndex,
            fontSize: rowFontSize,
            linkAccent: ScribeRowConstants.QuestPromptLinkColor,
            disabledTextColor: colors.OnSurfaceVariant,
            tooltipTextColor: colors.OnSurface,
            onAccept: (p, c) => onAcceptPrompt?.Invoke(p, c),
            onOpenPicker: () => onOpenQuestAcceptPicker?.Invoke(),
            onSelectCandidate: v => onSelectQuestAcceptCandidate?.Invoke(v),
            textColor: promptActionTextColor, glowWidth: GlowWidth, glowColor: glow);

        Widget dismissButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-dismiss-button"), ScribeRowConstants.QuestPromptDismissColor,
            rowFontSize, () => onDismissPrompt?.Invoke(prompt),
            textColor: promptActionTextColor, glowWidth: GlowWidth, glowColor: glow);

        Widget settingsButton = ScribeQuestPromptActions.AccentButton(
            Lang.Get("scribe:scribe-hud-questprompt-settings-button"), ScribeRowConstants.QuestPromptSettingsColor,
            rowFontSize, onOpenSettings);

        Widget buttonsRow = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[] { linkButton, dismissButton, settingsButton });

        var children = new List<Widget> { titleBlock };
        if (picker is not null) children.Add(picker);
        children.Add(buttonsRow);

        return new Column(
            spacing: 4,
            mainAxisSize: MainAxisSize.Min,
            crossAxisAlignment: leftAligned ? CrossAxisAlignment.Start : CrossAxisAlignment.End,
            children: children);
    }

    /// <summary>Full duration of the destructive-pending (Unpin/Delete) text fade, matched to the HUD pin
    /// window so the ramp reads as a countdown to removal.</summary>
    private const int FadeWindowMs = 1500;

    /// <summary>One LIVE HUD row: [checkbox][text]. A destructive-pending row's text fades linearly to
    /// transparent over the window (countdown); a sunk row mutes to <see cref="SunkOpacity"/>. The checkbox stays
    /// opaque and clickable so undo is available.
    ///
    /// <para>Collapse on removal is not here: the departed identity leaves the set and
    /// <see cref="ScribeAnimatedList"/> collapses a <see cref="BuildFrozenGhost"/>.</para></summary>
    private Widget BuildRow(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        // HUD text is explicit near-white rather than theme.OnSurface: it always renders over the world and needs
        // the contrast. Sunk rows are more muted.
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = row.Sunk
                ? new Vector4(0.70f, 0.70f, 0.70f, 1f)   // sunk: muted light grey
                : ScribeRowConstants.HudStandardTextColor, // active: near-white
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // Per-row seed offset keeps rows from sharing a mark pattern. Unused by the item-kind branch, which corrupts the name in BuildHudItemContent.
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        Widget rowBody;
        if (row.Kind == ScribeBlockKind.Text)
        {
            // A pinned note is text-only on the HUD: no checkbox, no unpin. Unpin from the Pin Tab.
            Widget text = new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Start,
                children: new Widget[] { new Expanded(child: text) });
        }
        else if (row.IsQuestObjective)
        {
            // Not player-completable (mirrors ScribeBlock.IsCompletable): item shape with no checkbox.
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[] { BuildHudItemContent(row, textStyle, interactive: true) });
        }
        else
        {
        var checkbox = new Checkbox(
            value: row.Done,
            onChanged: _ => onToggleRow(row.DocId, row.TaskId, row.Done),
            size: checkboxSize,
            // Grayscale, not the theme default: the stock CheckColor←Primary is the parchment theme's brown, out of
            // place on the theme-independent HUD.
            style: new CheckboxStyle
            {
                CheckColor = new Vector4(0.867f, 0.867f, 0.867f, 1f),   // #dddddd — softer than white, less blinding
                BackgroundColor = new Vector4(0.28f, 0.28f, 0.28f, 0.75f), // neutral dark-grey box at 75% opacity
                BorderColor = new Vector4(0.8f, 0.8f, 0.8f, 0.75f),  // #cccccc at 75% opacity — dimmer light-grey outline
                BorderThickness = 1.5f,
                CornerRadius = 2f,
                LabelStyle = textStyle,   // unused (no label) but required by the struct
            });

        if (row.IsItemKind)
        {
            // Tracker/Link rows show the item icon + name (+ have/need counter on the left for a Tracker); the name is
            // a Handbook hyperlink. A Quest Link's checkbox would mislead (it doesn't affect the quest), so the quest-marker
            // icon replaces it. Swapped inline, not via ScribeRowControlNudge.BuildLeadingControl, which would replace this
            // bespoke grayscale checkbox style.
            Widget leadingControl = ScribeLinkTarget.IsQuest(row.LinkTarget)
                ? new ScribeVsIconGlyph("scribequest", checkboxSize, ScribeTheme.HudQuestLinkAccent)
                : checkbox;
            rowBody = new Row(
                spacing: 6,
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Center,
                children: new Widget[]
                {
                    leadingControl,
                    new Expanded(child: BuildHudItemContent(row, textStyle, interactive: true)),
                });
        }
        else
        {
            // A plain Task's text fades linearly while destructive-pending (countdown preview). ScribeFadeText self-ticks
            // because a stock implicitly-animated widget would snap across rebuilds. Only text fades; the checkbox stays clickable.
            Widget text = new ScribeFadeText(
                fading: row.FadingOut,
                durationMs: FadeWindowMs,
                text: rowText,
                style: textStyle);

            // Link hyperlinks are handled in the item-kind branch; a plain Task's text is never a hyperlink.
            rowBody = new Row(
                spacing: 6,
                // Max width so the Expanded text wraps within HudRowWidth minus the checkbox.
                mainAxisSize: MainAxisSize.Max,
                crossAxisAlignment: CrossAxisAlignment.Start,
                children: new Widget[]
                {
                    checkbox,
                    new Expanded(child: text),
                });
        }
        }

        // A Depth-1 subtask is inset (10px + 3% of width, as on the dialog surfaces).
        if (row.Depth > 0)
        {
            rowBody = new Padding(
                EdgeInsets.Only(left: 10f + 0.03f * rowWidth),
                child: rowBody);
        }

        // A sunk row mutes the whole row, faded not snapped. The ValueKey stabilizes identity for the animation;
        // the container adds its own entry/collapse animation.
        return new AnimatedOpacity(
            opacity: row.Sunk ? SunkOpacity : 1f,
            duration: TimeSpan.FromMilliseconds(250),
            curve: Curves.EaseOut,
            child: rowBody,
            key: new ValueKey<Guid>(row.TaskId));
    }

    /// <summary>A pinned Tracker/Link row's content: item icon + name, with a have/need counter on the left for a
    /// Tracker (mirrors <c>ScribePinRowState.BuildItemContent</c>). Uses the HUD's near-white/glow ink via
    /// <paramref name="textStyle"/>. A ghost passes <paramref name="interactive"/> false (no hyperlink).</summary>
    private Widget BuildHudItemContent(HudPinRow row, TextStyle textStyle, bool interactive)
    {
        // Icons are optional (HudShowIcons); when off, rows read as text-only. The book glyph keeps the HUD's near-white ink.
        float iconSize = rowFontSize * 1.4f;
        float lineHeight = ScribeRowControlNudge.TextLineHeight(rowFontSize);
        // A Quest Link's icon renders in the leading slot and its name uses the HUD-only quest accent (the HUD has no per-material theme seam).
        bool isQuestLink = ScribeLinkTarget.IsQuest(row.LinkTarget);
        Widget? icon = showIcons && row.IsStaticVsQuestObjective
            ? ScribeLinkIcon.BuildObjective(row.DisplayStack, iconSize, textStyle.Color, lineHeight)
            : showIcons && !isQuestLink
                ? ScribeLinkIcon.Build(row.DisplayStack, row.LinkTarget, iconSize, textStyle.Color, lineHeight)
                : (Widget?)null;
        if (isQuestLink) textStyle = textStyle with { Color = ScribeTheme.HudQuestLinkAccent };

        // Name is a Handbook hyperlink when interactive and never toggles the checkbox. A Tracker's code isn't on the
        // row, so it is re-encoded from the resolved stack (keeping attribute variants, e.g. a copper lantern) to open
        // the variant's page rather than the generic one; a Link uses its LinkTarget.
        string nameText = Corrupt(row.Label, seedOffset: row.TaskId.GetHashCode());
        Widget name = new Text(nameText, textStyle);
        bool canOpen = !row.IsStaticVsQuestObjective || row.DisplayStack is not null;
        if (interactive && canOpen)
        {
            string? code = row.IsLink ? row.LinkTarget
                : row.DisplayStack is { } ds ? ScribeItemRef.Encode(ds) : null;
            name = new GestureDetector(
                onPress: e => { e.Handled = true; onOpenLink(code); },
                child: name);
        }

        var children = new List<Widget>();
        if (row.HasCounterRow)
        {
            // "have / need" counter on the left: strong (bold) while collecting, faded with a faint strikethrough once satisfied.
            bool satisfied = row.CurrentQuantity >= row.TargetQuantity;
            children.Add(ScribeTrackerCounterText.Build(
                row.CurrentQuantity, row.TargetQuantity, satisfied,
                strongColor: textStyle.Color, mutedColor: new Vector4(0.70f, 0.70f, 0.70f, 1f),
                lineHeight: lineHeight, baseStyle: textStyle,
                corrupt: s => Corrupt(s, seedOffset: 404)));
        }
        if (icon != null) children.Add(icon);
        children.Add(new Expanded(child: name));

        return new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: children);
    }

    /// <summary>A static snapshot of a HUD row for <see cref="ScribeAnimatedList"/> to collapse on departure. The
    /// live row can't be frozen (clickable checkbox, client state gone), so this twin has the text at ZERO opacity,
    /// matching the already-faded row (<see cref="ScribeFadeText"/> ramped it to ~0) so the collapse closes empty
    /// space. No ValueKey: the container owns the ghost's lifetime.</summary>
    private Widget BuildFrozenGhost(HudPinRow row, ColorScheme colors, Vector4 glow)
    {
        var textStyle = new TextStyle
        {
            FontSize = rowFontSize,
            Color = row.Sunk
                ? new Vector4(0.70f, 0.70f, 0.70f, 1f)
                : new Vector4(0.93f, 0.93f, 0.93f, 1f),
            GlowWidth = GlowWidth,
            GlowColor = glow,
            SoftWrap = true,
        };

        // Zero-opacity text; corrupted with the same seed so its metrics match the live row.
        string rowText = Corrupt(row.Text, seedOffset: row.TaskId.GetHashCode());

        Widget body = row.IsItemKind
            ? new Expanded(child: new Opacity(0f, BuildHudItemContent(row, textStyle, interactive: false)))
            : new Expanded(child: new Opacity(0f, new Text(rowText, textStyle)));

        var ghostChildren = new List<Widget>();
        if (row.Kind is not ScribeBlockKind.Text and not ScribeBlockKind.QuestObjective)
        {
            ghostChildren.Add(new Checkbox(
                value: row.Done,
                onChanged: null,
                size: checkboxSize,
                style: new CheckboxStyle
                {
                    CheckColor = new Vector4(0.867f, 0.867f, 0.867f, 1f),
                    BackgroundColor = new Vector4(0.28f, 0.28f, 0.28f, 0.75f),
                    BorderColor = new Vector4(0.8f, 0.8f, 0.8f, 0.75f),
                    BorderThickness = 1.5f,
                    CornerRadius = 2f,
                    LabelStyle = textStyle,
                }));
        }
        ghostChildren.Add(body);

        Widget ghost = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Start,
            children: ghostChildren);

        // Match the live row's Depth-1 indent so the collapsing ghost keeps identical metrics.
        if (row.Depth > 0)
            ghost = new Padding(EdgeInsets.Only(left: 10f + 0.03f * rowWidth), child: ghost);

        return ghost;
    }

    private Widget BuildTimerRow(Scribe.Core.TimerStore timer, ColorScheme colors, Vector4 glow, ICoreClientAPI? capi)
    {
        bool fired = timer.Status == Scribe.Core.TimerStatus.Fired;

        var textStyle = new TextStyle
        {
            FontSize  = rowFontSize,
            Color     = new Vector4(0.93f, 0.93f, 0.93f, 1f),
            GlowWidth = GlowWidth,
            GlowColor = glow,
        };
        // Distinct seed offsets so the label and countdown don't share a mark pattern.
        string timeText = Corrupt(
            fired ? "00:00" : FormatTimerDuration(timer.RemainingSeconds),
            seedOffset: 202);

        Widget timeWidget = (fired && capi is not null)
            ? new ScribeBlinkText(timeText, textStyle, capi)
            : new Text(timeText, textStyle);

        // Clock icon — rotates when fired.
        Widget iconWidget = (fired && capi is not null)
            ? new ScribeTimerIcon(rowFontSize * 1.1f, new Vector4(0.93f, 0.93f, 0.93f, 1f), capi)
            : new ScribeVsIconGlyph("scribetimer", rowFontSize * 1.1f, new Vector4(0.93f, 0.93f, 0.93f, 1f));

        // Label is full-strength: a live timer's label is primary information.
        string label = Corrupt(
            timer.Label.Length > 0 ? timer.Label : Lang.Get("scribe:scribe-hud-timer-label"),
            seedOffset: 303);

        Widget row = new Row(
            spacing: 6,
            mainAxisSize: MainAxisSize.Max,
            crossAxisAlignment: CrossAxisAlignment.Center,
            children: new Widget[]
            {
                iconWidget,
                new Expanded(child: new Text(label, textStyle)),
                timeWidget,
            });

        if (fired && onClearTimer is not null)
            row = new GestureDetector(onTap: _ => onClearTimer(), child: row);

        return row;
    }

    private static string FormatTimerDuration(double seconds)
    {
        int total = (int)Math.Max(0, seconds);
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int s = total % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }
}
