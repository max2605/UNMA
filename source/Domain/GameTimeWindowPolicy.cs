using System;

namespace UNMA.Domain;

public enum GameTimeUnit
{
    Day = 0,
    Month = 1,
    Year = 2,
    Decade = 3,
    Century = 4,
}

public static class GameTimeWindowPolicy
{
    public const int SimTicksPerDay = 20;
    public const int SimTicksPerMonth = 600;
    public const int SimTicksPerYear = 7200;
    public const int MaximumWindowTicks = SimTicksPerYear * 100;

    public static int ToSimTicks(int amount, GameTimeUnit unit)
    {
        amount = Math.Max(1, amount);
        var ticksPerUnit = unit switch
        {
            GameTimeUnit.Day => SimTicksPerDay,
            GameTimeUnit.Month => SimTicksPerMonth,
            GameTimeUnit.Year => SimTicksPerYear,
            GameTimeUnit.Decade => SimTicksPerYear * 10,
            GameTimeUnit.Century => SimTicksPerYear * 100,
            _ => SimTicksPerMonth,
        };
        return (int)Math.Min(
            MaximumWindowTicks,
            (long)amount * ticksPerUnit);
    }

    public static void FromLegacyRealSeconds(
        int seconds,
        out int amount,
        out GameTimeUnit unit)
    {
        // COI advances one game day every two real-time seconds at 1x.
        var days = Math.Max(1, (int)Math.Round(seconds / 2d));
        if (days % 36000 == 0)
        {
            amount = Math.Max(1, days / 36000);
            unit = GameTimeUnit.Century;
        }
        else if (days % 3600 == 0)
        {
            amount = Math.Max(1, days / 3600);
            unit = GameTimeUnit.Decade;
        }
        else if (days % 360 == 0)
        {
            amount = Math.Max(1, days / 360);
            unit = GameTimeUnit.Year;
        }
        else if (days % 30 == 0)
        {
            amount = Math.Max(1, days / 30);
            unit = GameTimeUnit.Month;
        }
        else
        {
            amount = days;
            unit = GameTimeUnit.Day;
        }
    }

    public static int ClampAmount(int amount, GameTimeUnit unit)
    {
        var ticksPerUnit = ToSimTicks(1, unit);
        return Math.Max(
            1,
            Math.Min(MaximumWindowTicks / ticksPerUnit, amount));
    }
}
