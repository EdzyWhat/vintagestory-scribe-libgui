using Vintagestory.API.Common;

namespace Scribe;

/// <summary>Formats an assignment calendar date in the viewing player's locale when a real in-game
/// timestamp is present; otherwise returns the stored identity string (a blob from before timestamps
/// existed has nothing to rebuild from). Mirrors Guestbook's display rule.</summary>
internal static class AssignmentDisplay
{
    internal static string Date(string baked, double? timestamp, IWorldAccessor world)
        => timestamp is { } ts
            ? NotebookHost.FormatDateFromTimestamp(world, ts)
            : baked;
}
