using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Converts between real time and in-game time using the world's current calendar speed.
///
/// <para>Vintage Story's <c>IGameCalendar.SpeedOfTime</c> (default 60) times <c>CalendarSpeedMul</c>
/// (default 0.5) gives how many in-game seconds pass per real second — 30 by default, i.e. a 48-minute
/// day (see VSAPI-NOTES "In-game time speed"). A world configured for longer/shorter days moves these,
/// and <c>SpeedOfTime</c> already folds in any active <c>SetTimeSpeedModifier</c>, so read it live.</para>
///
/// <para>Both the server tick (authoritative countdown) and the client-side interpolation (HUD +
/// Timer tab) use this so an InGame-mode timer drains at the same rate everywhere.</para>
/// </summary>
internal static class ScribeTimeRate
{
    /// <summary>In-game seconds that elapse per real second at the world's current time speed. Falls
    /// back to 1 (no scaling) when the calendar isn't available yet or reports a non-positive rate.</summary>
    public static double InGamePerReal(ICoreAPI? api)
    {
        var cal = api?.World?.Calendar;
        if (cal is null) return 1.0;
        double rate = cal.SpeedOfTime * cal.CalendarSpeedMul;
        return rate > 0 ? rate : 1.0;
    }
}
