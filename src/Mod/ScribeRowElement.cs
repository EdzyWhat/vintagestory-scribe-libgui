using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Scribe.Core;

namespace Scribe;

/// <summary>
/// One task/note row, drawn entirely by the mod in the interactive render pass.
///
/// This is the core of the row-list rework (S1). Unlike the old read view -- which stacked
/// <c>AddStaticText</c> + <c>AddInset</c> dividers that the engine bakes into a single, always-
/// unclipped static texture (so overflow could only be hidden by *culling* whole rows, never
/// clipped; see VSAPI-NOTES.md) -- this element bakes its own visuals into a PRIVATE
/// <see cref="LoadedTexture"/> in <see cref="ComposeElements"/> and blits it every frame in
/// <see cref="RenderInteractiveElements"/>. Because that blit happens in the interactive pass,
/// inside the dialog's <c>BeginClip</c> scissor, the engine clips it natively -- a row straddling
/// the scroll boundary is drawn partially, and the list scrolls by an ordinary parent
/// <c>fixedY</c> shift (the same model <c>GuiElementFlatList</c> uses). Read view has no
/// <c>GuiElementTextInput</c>, so the <c>GlScissorFlag(false)</c> clobber that defeated clipping
/// for the mixed editor list does not apply here.
///
/// S1 wires only the read <see cref="ScribeRowMode.Read"/> path. The mode flag and the edit branch
/// exist so S2 can add edit-in-place (a single live text field floating onto the focused row,
/// aligned via the shared <see cref="RowTextLayout"/>) without reshaping this element.
/// </summary>
public sealed class ScribeRowElement : GuiElement
{
    private readonly ScribeRowMode mode;
    private readonly int blockIndex;
    private readonly bool isTask;
    private readonly bool done;
    private readonly string text;
    private readonly CairoFont font;
    private readonly ScribeClientConfig config;

    /// <summary>Invoked with this row's block index when its checkbox is clicked. Read view sends
    /// the lock-free toggle; editor view toggles the scratch document (the dialog decides which).
    /// Null for a note (no checkbox).</summary>
    private readonly System.Action<int>? onToggleClicked;

    /// <summary>Editor view only: invoked with this row's block index when the player clicks the
    /// row's text area, asking the dialog to float the single live edit input onto this row. Null
    /// in read view (nothing but the checkbox is interactive there).</summary>
    private readonly System.Action<int>? onRequestEdit;

    /// <summary>Editor view only: when <c>true</c>, this row bakes its checkbox + ruling but SKIPS
    /// drawing its text pixels for this compose -- because the single floating <c>ScribeRowTextInput</c>
    /// is positioned over this row and is painting the text instead, and drawing both would
    /// double-draw (design.md Decision 1). "Suppress" is a draw-time skip only; <c>text</c>/the
    /// underlying block data is untouched. The element re-bakes (unsuppressed) on the next compose
    /// once focus moves off this row.</summary>
    private readonly bool suppressText;

    // Not readonly: generateTexture(surface, ref rowTexture) takes it by ref (loads/updates the
    // GL texture in place). The instance is created once in the ctor and never reassigned by us.
    private LoadedTexture rowTexture;

    public ScribeRowElement(
        ICoreClientAPI capi,
        ElementBounds bounds,
        ScribeRowMode mode,
        int blockIndex,
        bool isTask,
        bool done,
        string text,
        CairoFont font,
        ScribeClientConfig config,
        System.Action<int>? onToggleClicked,
        System.Action<int>? onRequestEdit = null,
        bool suppressText = false)
        : base(capi, bounds)
    {
        this.mode = mode;
        this.blockIndex = blockIndex;
        this.isTask = isTask;
        this.done = done;
        this.text = text;
        this.font = font;
        this.config = config;
        this.onToggleClicked = onToggleClicked;
        this.onRequestEdit = onRequestEdit;
        this.suppressText = suppressText;
        rowTexture = new LoadedTexture(capi);
    }

    /// <summary>The unscaled top padding above a row's text -- scaled by the text-size preference
    /// so it tracks font size. The single definition of this gap, shared by the height math below
    /// and the element's own drawing, so they can never disagree.</summary>
    private static double TopPadFixed(ScribeClientConfig config) => config.RulingPadding * config.TextSizeScale;

    /// <summary>The unscaled space below a row's text: bottom padding + the ruling line itself.</summary>
    private static double BottomOverheadFixed(ScribeClientConfig config) =>
        config.RulingPadding * config.TextSizeScale + config.RulingThickness * config.TextSizeScale;

    /// <summary>
    /// Computes a row's full height in UNSCALED fixed units (the space <c>ElementBounds.Fixed</c>
    /// expects), floored at the row's minimum. This is the SINGLE source of row height, shared by
    /// the dialog's layout pass and this element -- so the surface the row bakes onto is always
    /// exactly tall enough for the text it draws.
    ///
    /// The subtlety that caused a clipped last line before centralizing this: the engine's text
    /// measurement (<c>GetMultilineTextHeight</c>) and text drawing both work in SCALED (absolute)
    /// pixels -- <c>CairoFont.SetupContext</c> applies <c>GuiElement.scaled()</c> to the font size,
    /// and line count depends on the box width the text is measured against. So we must (a) measure
    /// against the SCALED text width the text is actually drawn at, then (b) divide the scaled text
    /// height back down by GUIScale to express it in fixed units before adding it to the fixed-unit
    /// paddings and handing the total to <c>ElementBounds.Fixed</c> (which re-applies GUIScale).
    /// Measuring at the unscaled width and skipping the divide double-applied the scale and left the
    /// row slightly too short at any non-1.0 GUIScale (Retina) -- clipping the final wrapped line.
    /// </summary>
    public static double RowHeightFixed(ICoreClientAPI capi, ScribeBlock block, double rowWidthFixed, CairoFont font, ScribeClientConfig config)
    {
        var layout = RowTextLayout.For(rowWidthFixed, block.IsTask, font, config);
        double textHeightFixed = MeasureWrappedTextHeightFixed(capi, block.Text, font, layout.TextWidth);

        double minHeight = ScribeBlockRowCell.RowHeight(block, config);
        double contentHeight = TopPadFixed(config) + textHeightFixed + BottomOverheadFixed(config);
        return System.Math.Max(minHeight, contentHeight);
    }

    /// <summary>
    /// Height in FIXED (layout) units of <paramref name="text"/> wrapped to a text column of
    /// <paramref name="fixedWidth"/> fixed units. The single wrapped-text-height primitive shared by
    /// row measurement and any other caller that needs to size a text block (e.g. the empty-list
    /// edit hint), so there is one correct measure rather than several divergent ones.
    ///
    /// Owns the scaled-vs-fixed unit handling so callers never touch GUIScale: the engine measures
    /// text in SCALED (absolute) pixels -- <c>CairoFont.SetupContext</c> applies <c>scaled()</c> to
    /// the font, and line count depends on the box width the text is measured against -- so we
    /// measure against the SCALED width and divide the scaled height back down by GUIScale before
    /// returning it in fixed units. Measuring at the unscaled width and skipping the divide
    /// double-applied the scale and left content slightly too short at any non-1.0 GUIScale (Retina),
    /// clipping the final wrapped line.
    /// </summary>
    internal static double MeasureWrappedTextHeightFixed(ICoreClientAPI capi, string text, CairoFont font, double fixedWidth) =>
        MeasureWrappedTextHeightScaled(capi, text, font, scaled(fixedWidth)) / RuntimeEnv.GUIScale;

    /// <summary>
    /// Scaled pixel height of <paramref name="text"/> wrapped to <paramref name="scaledWidth"/>,
    /// measured PER newline-delimited segment so that trailing/blank lines are counted.
    ///
    /// The engine's <c>GetMultilineTextHeight</c> drops a trailing newline's empty line from its
    /// count (decompile + in-game measurement 2026-07-22: <c>"a\n"</c> measured the same height as
    /// <c>"a"</c>). That made a row not grow the instant Shift+Enter added an empty last line, and
    /// left the caret on that empty line rendering below the input box (task 4.6). Splitting on
    /// <c>'\n'</c> and summing <c>max(1, wrappedLineCount(segment))</c> counts every visual line --
    /// including an empty trailing segment (which measures 0 lines, floored to 1) -- while still
    /// wrapping long segments correctly. Verified against the in-game log: multi-segment texts with
    /// no trailing newline sum to the same count the engine reported (e.g. 4 lines for
    /// <c>"New task\nsdf\ndsd\ndsad"</c>), and a trailing newline now adds exactly one line.
    /// </summary>
    private static double MeasureWrappedTextHeightScaled(ICoreClientAPI capi, string text, CairoFont font, double scaledWidth)
    {
        string[] segments = text.Split('\n');
        int totalLines = 0;
        foreach (string segment in segments)
        {
            // Empty segment (blank line / trailing newline) measures 0 lines -> floor at 1 so the
            // empty line still reserves height.
            totalLines += System.Math.Max(1, capi.Gui.Text.GetQuantityTextLines(font, segment, scaledWidth));
        }
        return totalLines * capi.Gui.Text.GetLineHeight(font);
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        // Deliberately ignore the shared static ctx/surface -- drawing there is exactly what made
        // the old rows unclippable. Bake onto our OWN surface instead (as GuiElementFlatList does
        // for its hover overlay), then blit it in the interactive pass where the clip applies.
        Bounds.CalcWorldBounds();

        int width = (int)Bounds.InnerWidth;
        int height = (int)Bounds.InnerHeight;
        if (width <= 0 || height <= 0) return;

        var surface = new ImageSurface(Format.Argb32, width, height);
        var ctx = new Context(surface);

        var layout = RowTextLayout.For(Bounds.fixedWidth, isTask, font, config);

        double topPad = scaled(TopPadFixed(config));

        if (isTask)
        {
            DrawCheckboxGlyph(ctx, done, scaled(layout.CheckboxX), topPad, scaled(layout.CheckboxSize));
        }

        // Text sits below the top padding, in the reserved text column. Read-view text wraps to the
        // available text width, matching the old AddStaticText behavior (tasks are typically single
        // line; a long note wraps and the row was measured tall enough to hold it). In edit mode a
        // focused row suppresses its own text draw (the floating input paints it instead) -- the
        // checkbox + ruling below still draw so only the text pixels are skipped (design.md Dec. 1).
        if (!suppressText)
        {
            font.SetupContext(ctx);
            api.Gui.Text.AutobreakAndDrawMultilineTextAt(
                ctx, font, text, scaled(layout.TextX), topPad, scaled(layout.TextWidth));
        }

        DrawRuling(ctx, width, height);

        generateTexture(surface, ref rowTexture);
        ctx.Dispose();
        surface.Dispose();
    }

    /// <summary>Draws the lined-paper hairline along the row's bottom edge, spanning the full row
    /// width. A structural part of the row (baked into the row's own texture, so it scrolls with
    /// the row). Authored as its own routine so its visual could later be swapped for an image
    /// (design.md Decision 3) without touching layout math.</summary>
    private void DrawRuling(Context ctx, int width, int height)
    {
        double thickness = scaled(config.RulingThickness);
        double y = height - thickness;

        ctx.SetSourceRGBA(config.RulingColorR, config.RulingColorG, config.RulingColorB, config.RulingColorA);
        ctx.LineWidth = thickness;
        ctx.NewPath();
        ctx.MoveTo(0, y + thickness / 2);
        ctx.LineTo(width, y + thickness / 2);
        ctx.Stroke();
    }

    /// <summary>Draws the custom checkbox glyph (a rounded square, filled with a check when done),
    /// replacing the engine's gamey <c>GuiElementSwitch</c>. Vertically centered against the top
    /// padding band so it lines up with the first text line.
    ///
    /// S4 HOOK (stamp/erase animation): this is the single seam where the checkbox visual is
    /// produced. The later stamp-on-check / erase-on-uncheck animation + sound (see ROADMAP) should
    /// replace/augment this draw only -- hit-testing (<see cref="OnMouseUpOnElement"/>) and layout
    /// (<see cref="RowTextLayout"/>) are intentionally independent of it and should not need to
    /// change.</summary>
    private void DrawCheckboxGlyph(Context ctx, bool isDone, double x, double y, double size)
    {
        if (size <= 0) return;

        // The glyph fills ReadCheckboxGlyphFill of the column, centered -- the leftover is split as
        // an inset on each side.
        double bs = size * config.ReadCheckboxGlyphFill;
        double inset = (size - bs) / 2;
        double bx = x + inset;
        double by = y + inset;
        double radius = bs * 0.2;

        // Box outline (near-ink, low alpha to match the ruling's paper feel).
        ctx.SetSourceRGBA(config.RulingColorR, config.RulingColorG, config.RulingColorB, System.Math.Min(1.0, config.RulingColorA * 2));
        ctx.LineWidth = System.Math.Max(1.0, scaled(config.RulingThickness));
        RoundedRect(ctx, bx, by, bs, bs, radius);
        ctx.Stroke();

        if (isDone)
        {
            // A simple check mark inside the box.
            ctx.SetSourceRGBA(config.RulingColorR, config.RulingColorG, config.RulingColorB, 1.0);
            ctx.LineWidth = System.Math.Max(1.5, scaled(config.RulingThickness) * 1.5);
            ctx.NewPath();
            ctx.MoveTo(bx + bs * 0.22, by + bs * 0.55);
            ctx.LineTo(bx + bs * 0.42, by + bs * 0.75);
            ctx.LineTo(bx + bs * 0.80, by + bs * 0.28);
            ctx.Stroke();
        }
    }

    private static void RoundedRect(Context ctx, double x, double y, double w, double h, double r)
    {
        ctx.NewPath();
        ctx.Arc(x + w - r, y + r, r, -System.Math.PI / 2, 0);
        ctx.Arc(x + w - r, y + h - r, r, 0, System.Math.PI / 2);
        ctx.Arc(x + r, y + h - r, r, System.Math.PI / 2, System.Math.PI);
        ctx.Arc(x + r, y + r, r, System.Math.PI, System.Math.PI * 1.5);
        ctx.ClosePath();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (rowTexture.TextureId == 0) return;

        // Blit at renderX/renderY -- which pick up the content parent's scroll fixedY shift (the
        // interactive-pass coordinate; see VSAPI-NOTES.md "TWO passes with TWO Y coordinates").
        // The dialog's BeginClip scissor is active here, so a row past the viewport edge is clipped
        // rather than bleeding out.
        api.Render.Render2DTexturePremultipliedAlpha(
            rowTexture.TextureId, Bounds.renderX, Bounds.renderY, Bounds.InnerWidth, Bounds.InnerHeight);
    }

    /// <summary>
    /// True if <paramref name="args"/> falls on this row's checkbox target. Reconstructs the drawn
    /// glyph's on-screen rect (matching <see cref="DrawCheckboxGlyph"/>'s math), then expands it by
    /// <c>ReadCheckboxHitboxScale</c> on BOTH axes so the clickable target is ~20% larger than the
    /// drawn glyph -- a forgiving target without accepting a click anywhere on a tall note row.
    /// Clamped to the row bounds so it never leaves the element. Notes (no checkbox) never hit.
    /// </summary>
    private bool IsCheckboxHit(MouseEvent args)
    {
        if (!isTask || onToggleClicked is null) return false;

        var layout = RowTextLayout.For(Bounds.fixedWidth, isTask, font, config);
        double colX = Bounds.absX + scaled(layout.CheckboxX);
        double colSize = scaled(layout.CheckboxSize);
        double glyphSize = colSize * config.ReadCheckboxGlyphFill;
        double glyphInset = (colSize - glyphSize) / 2;
        double glyphX = colX + glyphInset;
        double glyphY = Bounds.absY + scaled(TopPadFixed(config)) + glyphInset;

        double expand = glyphSize * (config.ReadCheckboxHitboxScale - 1) / 2;
        double hitLeft = System.Math.Max(Bounds.absX, glyphX - expand);
        double hitRight = System.Math.Min(Bounds.absX + Bounds.InnerWidth, glyphX + glyphSize + expand);
        double hitTop = System.Math.Max(Bounds.absY, glyphY - expand);
        double hitBottom = System.Math.Min(Bounds.absY + Bounds.InnerHeight, glyphY + glyphSize + expand);

        return args.X >= hitLeft && args.X <= hitRight && args.Y >= hitTop && args.Y <= hitBottom;
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        // On the focused editor row (suppressText == true, so a floating ScribeRowTextInput is
        // composed over this row's text column), a click in the text column must FALL THROUGH to
        // that input rather than be consumed here. The input is added to the composer AFTER this
        // row, so GuiComposer.OnMouseDown reaches this row first: if this (non-focusable) row
        // consumes the down, the composer's dispatch loop then BLURS the still-focused input --
        // the re-click-loses-focus bug (decompile-confirmed; see VSAPI-NOTES). By NOT setting
        // args.Handled, the down continues to the input, which keeps focus AND places the caret at
        // the click (GuiElementEditableTextBase.OnMouseDownOnElement -> SetCaretPos). A checkbox
        // hit on the focused row is still consumed normally (it toggles + recomposes). Every other
        // row (not yet focused, so no input overlaps) is consumed as before -- its mouse-up asks
        // the dialog to float the input onto it.
        if (mode == ScribeRowMode.Edit && suppressText && !IsCheckboxHit(args))
        {
            return; // leave args.Handled false: the overlapping input wins this mouse-down
        }

        base.OnMouseDownOnElement(api, args); // sets args.Handled = true
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUpOnElement(api, args);

        // The composer only dispatches here when IsPositionInside passes, which already ANDs
        // InsideClipBounds -- so a row scrolled outside the clip region rejects the hit for free.

        // Checkbox (task rows, both views): toggle done.
        if (IsCheckboxHit(args))
        {
            onToggleClicked!(blockIndex);
            args.Handled = true;
            return;
        }

        // Read view: nothing but the checkbox is interactive; a text click is inert.
        if (mode != ScribeRowMode.Edit) return;

        // Editor view, text-column click: ask the dialog to float the single live edit input onto
        // this row. For the ALREADY-focused row this is a no-op (the dialog early-returns and the
        // input already handled the mouse-down above), so it only does real work when focusing a
        // different row.
        if (onRequestEdit is not null)
        {
            onRequestEdit(blockIndex);
            args.Handled = true;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        rowTexture.Dispose();
    }
}

/// <summary>Which interaction set a <see cref="ScribeRowElement"/> exposes. S1 wires only
/// <see cref="Read"/>; <see cref="Edit"/> is reserved for S2's edit-in-place work.</summary>
public enum ScribeRowMode
{
    /// <summary>Read view: the checkbox toggles done (lock-free); nothing else is interactive.</summary>
    Read,

    /// <summary>Editor view: the checkbox toggles done (scratch edit) and clicking the text column
    /// floats the single live <see cref="ScribeRowTextInput"/> onto the row for edit-in-place.</summary>
    Edit,
}
