namespace Scribe.Core;

/// <summary>
/// Which screen corner/edge the pinned-task HUD is pinned to (see <see cref="ScribePlayerSettings.HudAnchor"/>).
/// The Mod layer maps it to a screen position and applies the per-anchor offset nudge. The default is
/// <see cref="TopRight"/>, pre-offset left of the default minimap by the Mod layer. There is no in-mod
/// picker yet; the value is hand-editable in the client JSON config.
/// </summary>
public enum ScribeHudAnchor : byte
{
    /// <summary>Top-left corner; the list grows downward.</summary>
    TopLeft = 0,

    /// <summary>Top edge, horizontally centered; the list grows downward.</summary>
    TopMiddle = 1,

    /// <summary>Top-right corner (the default); the list grows downward.</summary>
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

/// <summary>Helpers for classifying a <see cref="ScribeHudAnchor"/>'s horizontal side.</summary>
public static class ScribeHudAnchorExtensions
{
    /// <summary>True for the three Left anchors. Center and right anchors count as right-aligned for text,
    /// matching the Mod layer's X-position switch where only Left anchors offset from the left margin.</summary>
    public static bool IsLeftAnchored(this ScribeHudAnchor anchor) => anchor
        is ScribeHudAnchor.TopLeft or ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.BottomLeft;
}
