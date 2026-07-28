namespace Scribe.Core;

/// <summary>
/// Which screen corner/edge the pinned-task HUD is pinned to. A per-player, client-local display
/// preference (see <see cref="ScribePlayerSettings.HudAnchor"/>): the Mod layer maps the anchor to a
/// screen position and applies the per-anchor <see cref="ScribePlayerSettings.HudOffsetX"/>/
/// <see cref="ScribePlayerSettings.HudOffsetY"/> nudge so the HUD can be moved clear of other on-screen
/// overlays (the minimap, coordinate overlay, block-info overlay). Game-agnostic (pure BCL).
///
/// The default is <see cref="TopRight"/>, pre-offset left of the default top-right minimap by the Mod
/// layer so the HUD is not drawn underneath it. An in-mod settings UI to pick the anchor from a
/// dropdown is a deferred future change; today the value is hand-editable in the client JSON config.
/// </summary>
public enum ScribeHudAnchor : byte
{
    /// <summary>Top-left corner; the list grows downward.</summary>
    TopLeft = 0,

    /// <summary>Top edge, horizontally centered; the list grows downward.</summary>
    TopMiddle = 1,

    /// <summary>Top-right corner (the default); the list grows downward. Pre-offset left of the
    /// default minimap by the Mod layer.</summary>
    TopRight = 2,

    /// <summary>Left edge, vertically centered.</summary>
    MiddleLeft = 3,

    /// <summary>Right edge, vertically centered.</summary>
    MiddleRight = 4,

    /// <summary>Bottom-left corner; the list grows upward.</summary>
    BottomLeft = 5,

    /// <summary>Bottom-right corner; the list grows upward.</summary>
    BottomRight = 6,
}

/// <summary>Pure helpers for classifying a <see cref="ScribeHudAnchor"/>'s horizontal side, so the HUD's
/// text alignment (and the Mod layer's X-positioning) can be derived without duplicating the switch.
/// Game-agnostic (pure BCL), so it stays unit-testable in <c>Core.Tests</c>.</summary>
public static class ScribeHudAnchorExtensions
{
    /// <summary>True when the anchor pins the HUD to the screen's LEFT edge
    /// (<see cref="ScribeHudAnchor.TopLeft"/>/<see cref="ScribeHudAnchor.MiddleLeft"/>/
    /// <see cref="ScribeHudAnchor.BottomLeft"/>). The center and right anchors are treated as right-aligned
    /// for text purposes (the HUD hugs its anchored edge), matching the Mod layer's X-position switch where
    /// only the three Left anchors add the offset from the left margin.</summary>
    public static bool IsLeftAnchored(this ScribeHudAnchor anchor) => anchor
        is ScribeHudAnchor.TopLeft or ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.BottomLeft;
}
