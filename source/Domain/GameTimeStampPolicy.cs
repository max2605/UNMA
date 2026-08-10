using System;

namespace UNMA.Domain;

public readonly struct GameDateParts
{
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    public int TickOfDay { get; }

    public GameDateParts(int year, int month, int day, int tickOfDay)
    {
        Year = year;
        Month = month;
        Day = day;
        TickOfDay = tickOfDay;
    }
}

public static class GameTimeStampPolicy
{
    public static bool TryGetDate(
        double gameTicks,
        out GameDateParts date)
    {
        date = default;
        if (double.IsNaN(gameTicks) ||
            double.IsInfinity(gameTicks) ||
            gameTicks <= 0d)
        {
            return false;
        }

        var wholeTicks = gameTicks >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Floor(gameTicks);
        var dayIndex = wholeTicks / GameTimeWindowPolicy.SimTicksPerDay;
        var dayOfYear = dayIndex % 360L;
        var year = Math.Min(int.MaxValue, dayIndex / 360L + 1L);
        date = new GameDateParts(
            (int)year,
            (int)(dayOfYear / 30L + 1L),
            (int)(dayOfYear % 30L + 1L),
            (int)(wholeTicks % GameTimeWindowPolicy.SimTicksPerDay));
        return true;
    }

    public static double LatestEventTicks(AlarmHistoryDefinition history)
    {
        if (history == null)
        {
            return 0d;
        }
        return Math.Max(
            history.RaisedAtTicks,
            Math.Max(
                history.ClearedAtTicks,
                history.AcknowledgedAtTicks));
    }
}
