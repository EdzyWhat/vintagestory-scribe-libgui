using Vintagestory.API.Common;

namespace Scribe;

/// <summary>
/// Converts between real time and in-game time using the world's current calendar speed.
///
/// <para><c>IGameCalendar.SpeedOfTime</c> (default 60) times <c>CalendarSpeedMul</c> (default 0.5) gives
/// in-game seconds per real second (see VSAPI-NOTES "In-game time speed"). <c>SpeedOfTime</c> already folds
/// in any active <c>SetTimeSpeedModifier</c>, so read it live.</para>
///
/// <para>The server tick and client interpolation both use this so an InGame-mode timer drains at the same
/// rate everywhere.</para>
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
