namespace Scribe;

/// <summary>
/// Immutable, already-scaled sizing values for one lectern task row, shared by the read and
/// editor views so a single-line task occupies pixel-identical space in both (see
/// <see cref="GuiDialogScribeLecternLibGui"/>). Carries only sizes; colors still come from the
/// LibGUI <c>Theme</c>. Passed by value down the widget tree (read/editor content -> rows ->
/// field), which keeps each widget testable with a literal style and no config/game dependency.
/// </summary>
internal readonly record struct ScribeRowStyle(
    float FontSize,
    float RowVerticalPadding,
    float RowHorizontalPadding,
    float CheckboxTextGap,
    float CheckboxSize,
    float FieldPadX,
    float FieldPadY)
{
    /// <summary>
    /// Builds a row style from client config. THIS IS THE SINGLE PLACE where the text-size scale
    /// is applied: the scalable values are multiplied by <see cref="ScribeClientConfig.TextSizeScale"/>
    /// (default <c>1f</c>, so this is a behavioral no-op today). A future font-size / UI-scale
    /// feature hooks in here -- e.g. read the factor from a live control instead of config, or split
    /// it into font vs UI scales -- without touching any widget.
    /// </summary>
    public static ScribeRowStyle FromConfig(ScribeClientConfig c)
    {
        float s = c.TextSizeScale;
        return new ScribeRowStyle(
            FontSize: c.RowFontSize * s,
            RowVerticalPadding: c.RowVerticalPadding * s,
            RowHorizontalPadding: c.RowHorizontalPadding, // fixed left/right inset; not scaled
            CheckboxTextGap: c.RowCheckboxTextGap * s,
            CheckboxSize: c.RowCheckboxSize * s,
            FieldPadX: c.FieldInnerPaddingX * s,
            FieldPadY: c.FieldInnerPaddingY * s);
    }
}
