using Scribe.Core;

namespace Scribe;

/// <summary>
/// Immutable, already-scaled sizing values for one lectern task row, shared by the read and
/// editor views so a single-line task occupies pixel-identical space in both (see
/// <see cref="GuiDialogScribeLecternLibGui"/>). Carries only sizes; colors (including the resting
/// pinned-row tint, which is now derived from the LibGUI <c>Theme</c> at build time — add-settings-tab
/// D1b) come from the Theme at the widget. Passed by value down the widget tree (read/editor content ->
/// rows -> field), which keeps each widget testable with a literal style and no config/game dependency.
/// </summary>
internal readonly record struct ScribeRowStyle(
    float FontSize,
    float RowVerticalPadding,
    float RowHorizontalPadding,
    float CheckboxTextGap,
    float CheckboxSize,
    float FieldPadX,
    float FieldPadY,
    float ControlSize,
    string TaskFontFamily)
{
    /// <summary>
    /// Builds a row style from the player's consolidated settings. THIS IS THE SINGLE PLACE where the
    /// window font-size scale is applied: the scalable values are multiplied by
    /// <see cref="ScribePlayerSettings.WindowFontScale"/> (default <c>1f</c>, so a fresh player is a
    /// behavioral no-op), on top of the base sizes in <see cref="ScribeRowConstants"/>. The scale comes
    /// straight from the live settings, so re-deriving the style per dialog build repaints an open
    /// dialog when the player changes the scale in the settings view (add-settings-tab D4).
    /// </summary>
    public static ScribeRowStyle FromSettings(ScribePlayerSettings s)
    {
        float scale = ScribePlayerSettings.ClampFontScale(s.WindowFontScale);
        return new ScribeRowStyle(
            FontSize: ScribeRowConstants.BaseWindowFontSize * scale,
            RowVerticalPadding: ScribeRowConstants.RowVerticalPadding * scale,
            RowHorizontalPadding: ScribeRowConstants.RowHorizontalPadding, // fixed left/right inset; not scaled
            CheckboxTextGap: ScribeRowConstants.RowCheckboxTextGap * scale,
            CheckboxSize: ScribeRowConstants.RowCheckboxSize * scale,
            FieldPadX: ScribeRowConstants.FieldInnerPaddingX * scale,
            FieldPadY: ScribeRowConstants.FieldInnerPaddingY * scale,
            // Per-row control glyphs (grip/pin/delete) scale with the row like the checkbox does, so
            // they grow/shrink with the text-size preference (lectern-gui-shell "scales with text size").
            // Sized to the checkbox so the affordance column reads as a sibling of the checkbox column.
            ControlSize: ScribeRowConstants.RowCheckboxSize * scale,
            // The player's task-text font choice (v1-release-checklist §6), normalized to a known family
            // or "" (built-in body font). Read/editor rows apply it to their task/note text; re-deriving
            // the style per build repaints an open Lectern when the selector changes.
            TaskFontFamily: ScribePlayerSettings.NormalizeTaskFontFamily(s.TaskFontFamily));
    }
}
