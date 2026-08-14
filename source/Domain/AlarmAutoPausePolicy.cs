using System;

namespace UNMA.Domain;

/// <summary>
/// Immutable global auto-pause choices. Alarm source names intentionally use
/// the same stable values persisted by <see cref="AlarmView.Source"/>.
/// </summary>
public readonly struct AlarmAutoPauseOptions
{
    public bool Enabled { get; }
    public AlarmSeverity MinimumSeverity { get; }
    public bool IncludeVanilla { get; }
    public bool IncludeSystem { get; }
    public bool IncludeCustom { get; }
    public bool IncludeExternal { get; }

    public AlarmAutoPauseOptions(
        bool enabled,
        AlarmSeverity minimumSeverity,
        bool includeVanilla,
        bool includeSystem,
        bool includeCustom,
        bool includeExternal)
    {
        Enabled = enabled;
        MinimumSeverity = AlarmAutoPausePolicy.NormalizeMinimumSeverity(
            minimumSeverity);
        IncludeVanilla = includeVanilla;
        IncludeSystem = includeSystem;
        IncludeCustom = includeCustom;
        IncludeExternal = includeExternal;
    }
}

/// <summary>
/// Pure decision policy for pausing on an alarm edge. Polling an alarm that is
/// already active can never request another pause.
/// </summary>
public static class AlarmAutoPausePolicy
{
    public static AlarmSeverity NormalizeMinimumSeverity(
        AlarmSeverity severity)
    {
        return (AlarmSeverity)Math.Max(
            (int)AlarmSeverity.Notice,
            Math.Min((int)AlarmSeverity.Emergency, (int)severity));
    }

    public static bool ShouldPause(
        AlarmAutoPauseOptions options,
        string source,
        AlarmSeverity severity,
        bool isNewOccurrence,
        bool isAcknowledged)
    {
        if (!options.Enabled || !isNewOccurrence || isAcknowledged ||
            !Enum.IsDefined(typeof(AlarmSeverity), severity) ||
            severity < options.MinimumSeverity)
        {
            return false;
        }

        return source switch
        {
            "vanilla" => options.IncludeVanilla,
            "system" => options.IncludeSystem,
            "custom" => options.IncludeCustom,
            "external" => options.IncludeExternal,
            _ => false,
        };
    }
}
