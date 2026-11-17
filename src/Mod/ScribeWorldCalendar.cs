using System;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Scribe;

/// <summary>
/// The one adapter between the world's calendar and Scribe's dates. It builds the calendar shape from the
/// world's days per month, turns a <c>Calendar.TotalDays</c> value into a <see cref="ScribeDate"/> with years
/// counted from 1 (Year 1 is the world's first calendar year; <c>Calendar.Year</c>, which counts from 0, is
/// never used), and formats dates through the player's Date order
/// template. Display only: server identity stamps use <see cref="NotebookHost.FormatDate(ICoreServerAPI)"/>.
/// </summary>
internal static class ScribeWorldCalendar
{
    /// <summary>The world's calendar shape: 12 months of the world's days per month.</summary>
    internal static UniformCalendarShape Shape(IWorldAccessor world) => new(world.Calendar.DaysPerMonth);

    /// <summary>Today's date in the world.</summary>
    internal static ScribeDate Today(IWorldAccessor world) => FromTotalDays(world, world.Calendar.TotalDays);

    /// <summary>The date of a <c>Calendar.TotalDays</c> value (negative values map to the world's first
    /// day). <c>TotalDays</c> counts from January 1st of the first year, so whole years elapsed + 1 is the
    /// year and the remainder gives the month and day.</summary>
    internal static ScribeDate FromTotalDays(IWorldAccessor world, double totalDays)
        => Shape(world).FromOrdinal((long)Math.Floor(Math.Max(0, totalDays)));

    /// <summary>Formats <paramref name="date"/>, with its day clamped to the month's end, through the
    /// template <paramref name="order"/> picks (<c>{0}</c> = day, <c>{1}</c> = localized month name,
    /// <c>{2}</c> = year).</summary>
    internal static string Format(IWorldAccessor world, ScribeDate date, ScribeDateOrder order)
    {
        var clamped = ((IScribeCalendarShape)Shape(world)).Clamp(date);
        return Lang.Get("scribe:" + ScribeDateText.TemplateKey(order),
            clamped.Day, MonthName(clamped.Month), clamped.Year);
    }

    /// <summary>The localized name of month 1..12, from the game's own <c>month-*</c> lang keys.</summary>
    internal static string MonthName(int month) => Lang.Get("month-" + (EnumMonth)month);
}
