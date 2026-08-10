using System;
using System.Collections.Generic;

namespace UNMA.Domain;

public enum InstrumentForecastStatus
{
    InsufficientData = 0,
    Stable = 1,
    Unreliable = 2,
    Moving = 3,
}

public enum InstrumentForecastDirection
{
    None = 0,
    Rising = 1,
    Falling = 2,
}

public enum InstrumentForecastEtaStatus
{
    None = 0,
    Available = 1,
    BeyondHorizon = 2,
}

/// <summary>
/// Immutable historian statistics and a directed linear forecast for one
/// instrument. All durations are expressed in simulation ticks.
/// </summary>
public readonly struct InstrumentForecastResult
{
    public InstrumentForecastStatus Status { get; }
    public InstrumentForecastDirection Direction { get; }
    public InstrumentForecastEtaStatus EtaStatus { get; }
    public int SampleCount { get; }
    public double DurationTicks { get; }
    public double CurrentValue { get; }
    public double MinimumValue { get; }
    public double AverageValue { get; }
    public double MaximumValue { get; }
    public double RatePerMonth { get; }
    public double RSquared { get; }
    public double TargetValue { get; }
    public double EtaTicks { get; }

    public bool HasTrend =>
        Status != InstrumentForecastStatus.InsufficientData;

    public bool HasEta =>
        EtaStatus == InstrumentForecastEtaStatus.Available;

    public bool HorizonExceeded =>
        EtaStatus == InstrumentForecastEtaStatus.BeyondHorizon;

    internal InstrumentForecastResult(
        InstrumentForecastStatus status,
        InstrumentForecastDirection direction,
        InstrumentForecastEtaStatus etaStatus,
        int sampleCount,
        double durationTicks,
        double currentValue,
        double minimumValue,
        double averageValue,
        double maximumValue,
        double ratePerMonth,
        double rSquared,
        double targetValue,
        double etaTicks)
    {
        Status = status;
        Direction = direction;
        EtaStatus = etaStatus;
        SampleCount = sampleCount;
        DurationTicks = durationTicks;
        CurrentValue = currentValue;
        MinimumValue = minimumValue;
        AverageValue = averageValue;
        MaximumValue = maximumValue;
        RatePerMonth = ratePerMonth;
        RSquared = rSquared;
        TargetValue = targetValue;
        EtaTicks = etaTicks;
    }
}

/// <summary>
/// Computes finite-only historian statistics and a least-squares forecast.
/// The explicit current sample is authoritative when history already contains
/// the same tick. Other duplicate ticks are merged deterministically so that
/// neither input ordering nor duplicate capture can bias the regression.
/// </summary>
public static class InstrumentForecastPolicy
{
    public const int MinimumSampleCount = 3;
    public const double MinimumDurationTicks =
        GameTimeWindowPolicy.SimTicksPerDay * 2d;
    public const double MinimumReliableRSquared = 0.35d;
    public const double MinimumStableRatePerMonth = 0.000001d;
    public const double StableScaleFractionPerMonth = 0.001d;
    public const double MaximumEtaTicks =
        GameTimeWindowPolicy.SimTicksPerYear * 100d;

    public static bool TryAnalyze(
        IReadOnlyList<InstrumentValueSample> history,
        double currentTimestampTicks,
        double currentValue,
        double scaleMinimum,
        double scaleMaximum,
        out InstrumentForecastResult result)
    {
        result = default;
        if (history == null ||
            !IsFinite(currentTimestampTicks) ||
            !IsFinite(currentValue) ||
            !IsFinite(scaleMinimum) ||
            !IsFinite(scaleMaximum) ||
            scaleMaximum <= scaleMinimum)
        {
            return false;
        }

        var scaleWidth = scaleMaximum - scaleMinimum;
        if (!IsFinite(scaleWidth))
        {
            return false;
        }

        var samples = new List<SamplePoint>(history.Count + 1);
        var maximumAbsoluteValue = Math.Abs(currentValue);
        for (var index = 0; index < history.Count; index++)
        {
            var sample = history[index];
            if (!IsFinite(sample.TimestampSeconds) ||
                !IsFinite(sample.Value) ||
                sample.TimestampSeconds > currentTimestampTicks)
            {
                return false;
            }

            // The separately captured current value wins at its exact tick.
            if (sample.TimestampSeconds == currentTimestampTicks)
            {
                continue;
            }

            samples.Add(new SamplePoint(
                sample.TimestampSeconds,
                sample.Value));
            maximumAbsoluteValue = Math.Max(
                maximumAbsoluteValue,
                Math.Abs(sample.Value));
        }
        samples.Add(new SamplePoint(currentTimestampTicks, currentValue));
        samples.Sort(SamplePointComparer.Instance);

        var valueScale = maximumAbsoluteValue > 0d
            ? maximumAbsoluteValue
            : 1d;
        var uniqueSamples = MergeDuplicateTicks(samples, valueScale);
        if (uniqueSamples.Count == 0)
        {
            return false;
        }

        var minimumValue = uniqueSamples[0].Value;
        var maximumValue = uniqueSamples[0].Value;
        var normalizedAverage = 0d;
        for (var index = 0; index < uniqueSamples.Count; index++)
        {
            var value = uniqueSamples[index].Value;
            minimumValue = Math.Min(minimumValue, value);
            maximumValue = Math.Max(maximumValue, value);
            normalizedAverage +=
                (value / valueScale - normalizedAverage) / (index + 1d);
        }
        var averageValue = normalizedAverage * valueScale;
        if (!IsFinite(averageValue))
        {
            return false;
        }

        var durationTicks = currentTimestampTicks -
                            uniqueSamples[0].TimestampTicks;
        if (!IsFinite(durationTicks) || durationTicks < 0d)
        {
            return false;
        }

        if (uniqueSamples.Count < MinimumSampleCount ||
            durationTicks < MinimumDurationTicks)
        {
            result = CreateResult(
                InstrumentForecastStatus.InsufficientData,
                InstrumentForecastDirection.None,
                InstrumentForecastEtaStatus.None,
                uniqueSamples.Count,
                durationTicks,
                currentValue,
                minimumValue,
                averageValue,
                maximumValue,
                0d,
                0d,
                currentValue,
                0d);
            return true;
        }

        if (!TryCalculateRegression(
                uniqueSamples,
                durationTicks,
                valueScale,
                out var ratePerMonth,
                out var rSquared))
        {
            return false;
        }

        var stableRate = Math.Max(
            MinimumStableRatePerMonth,
            scaleWidth * StableScaleFractionPerMonth);
        var isStable = Math.Abs(ratePerMonth) <= stableRate;
        var direction = isStable
            ? InstrumentForecastDirection.None
            : ratePerMonth > 0d
                ? InstrumentForecastDirection.Rising
                : InstrumentForecastDirection.Falling;
        var targetValue = direction switch
        {
            InstrumentForecastDirection.Rising => scaleMaximum,
            InstrumentForecastDirection.Falling => scaleMinimum,
            _ => currentValue,
        };
        if (rSquared < MinimumReliableRSquared)
        {
            result = CreateResult(
                InstrumentForecastStatus.Unreliable,
                direction,
                InstrumentForecastEtaStatus.None,
                uniqueSamples.Count,
                durationTicks,
                currentValue,
                minimumValue,
                averageValue,
                maximumValue,
                ratePerMonth,
                rSquared,
                targetValue,
                0d);
            return true;
        }

        if (isStable)
        {
            result = CreateResult(
                InstrumentForecastStatus.Stable,
                InstrumentForecastDirection.None,
                InstrumentForecastEtaStatus.None,
                uniqueSamples.Count,
                durationTicks,
                currentValue,
                minimumValue,
                averageValue,
                maximumValue,
                ratePerMonth,
                rSquared,
                currentValue,
                0d);
            return true;
        }

        var etaStatus = InstrumentForecastEtaStatus.None;
        var etaTicks = 0d;
        var distance = direction == InstrumentForecastDirection.Rising
            ? targetValue - currentValue
            : currentValue - targetValue;
        if (IsFinite(distance) && distance > 0d)
        {
            var etaMonths = distance / Math.Abs(ratePerMonth);
            var maximumEtaMonths = MaximumEtaTicks /
                                   GameTimeWindowPolicy.SimTicksPerMonth;
            if (double.IsPositiveInfinity(etaMonths) ||
                etaMonths > maximumEtaMonths)
            {
                etaStatus = InstrumentForecastEtaStatus.BeyondHorizon;
            }
            else if (IsFinite(etaMonths) && etaMonths > 0d)
            {
                var candidateEtaTicks =
                    etaMonths * GameTimeWindowPolicy.SimTicksPerMonth;
                if (IsFinite(candidateEtaTicks) && candidateEtaTicks > 0d)
                {
                    etaStatus = InstrumentForecastEtaStatus.Available;
                    etaTicks = candidateEtaTicks;
                }
            }
        }

        result = CreateResult(
            InstrumentForecastStatus.Moving,
            direction,
            etaStatus,
            uniqueSamples.Count,
            durationTicks,
            currentValue,
            minimumValue,
            averageValue,
            maximumValue,
            ratePerMonth,
            rSquared,
            targetValue,
            etaTicks);
        return true;
    }

    private static List<SamplePoint> MergeDuplicateTicks(
        IReadOnlyList<SamplePoint> sortedSamples,
        double valueScale)
    {
        var merged = new List<SamplePoint>(sortedSamples.Count);
        var index = 0;
        while (index < sortedSamples.Count)
        {
            var timestamp = sortedSamples[index].TimestampTicks;
            var normalizedAverage = 0d;
            var duplicateCount = 0;
            while (index < sortedSamples.Count &&
                   sortedSamples[index].TimestampTicks == timestamp)
            {
                duplicateCount++;
                normalizedAverage +=
                    (sortedSamples[index].Value / valueScale -
                     normalizedAverage) / duplicateCount;
                index++;
            }
            merged.Add(new SamplePoint(
                timestamp,
                normalizedAverage * valueScale));
        }
        return merged;
    }

    private static bool TryCalculateRegression(
        IReadOnlyList<SamplePoint> samples,
        double durationTicks,
        double valueScale,
        out double ratePerMonth,
        out double rSquared)
    {
        ratePerMonth = 0d;
        rSquared = 0d;
        if (samples == null || samples.Count < MinimumSampleCount ||
            durationTicks <= 0d || valueScale <= 0d)
        {
            return false;
        }

        // X is normalized to [0, 1] and Y by its largest magnitude. This
        // avoids loss of precision from large absolute game ticks and keeps
        // all covariance sums safely bounded for realistic history sizes.
        var firstTimestamp = samples[0].TimestampTicks;
        var meanX = 0d;
        var meanY = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var x = (samples[index].TimestampTicks - firstTimestamp) /
                    durationTicks;
            var y = samples[index].Value / valueScale;
            meanX += (x - meanX) / (index + 1d);
            meanY += (y - meanY) / (index + 1d);
        }

        var sumXX = 0d;
        var sumXY = 0d;
        var sumYY = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            var x = (samples[index].TimestampTicks - firstTimestamp) /
                    durationTicks;
            var y = samples[index].Value / valueScale;
            var deltaX = x - meanX;
            var deltaY = y - meanY;
            sumXX += deltaX * deltaX;
            sumXY += deltaX * deltaY;
            sumYY += deltaY * deltaY;
        }
        if (!IsFinite(sumXX) || !IsFinite(sumXY) ||
            !IsFinite(sumYY) || sumXX <= 0d)
        {
            return false;
        }

        var normalizedSlope = sumXY / sumXX;
        ratePerMonth = normalizedSlope *
                       (GameTimeWindowPolicy.SimTicksPerMonth /
                        durationTicks) *
                       valueScale;
        if (!IsFinite(ratePerMonth))
        {
            return false;
        }

        if (sumYY <= 0d)
        {
            rSquared = 1d;
            return true;
        }

        var correlation = sumXY /
                          Math.Sqrt(sumXX) /
                          Math.Sqrt(sumYY);
        rSquared = correlation * correlation;
        if (!IsFinite(rSquared))
        {
            return false;
        }
        rSquared = Math.Max(0d, Math.Min(1d, rSquared));
        return true;
    }

    private static InstrumentForecastResult CreateResult(
        InstrumentForecastStatus status,
        InstrumentForecastDirection direction,
        InstrumentForecastEtaStatus etaStatus,
        int sampleCount,
        double durationTicks,
        double currentValue,
        double minimumValue,
        double averageValue,
        double maximumValue,
        double ratePerMonth,
        double rSquared,
        double targetValue,
        double etaTicks)
    {
        return new InstrumentForecastResult(
            status,
            direction,
            etaStatus,
            sampleCount,
            durationTicks,
            currentValue,
            minimumValue,
            averageValue,
            maximumValue,
            ratePerMonth,
            rSquared,
            targetValue,
            etaTicks);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private readonly struct SamplePoint
    {
        public double TimestampTicks { get; }
        public double Value { get; }

        public SamplePoint(double timestampTicks, double value)
        {
            TimestampTicks = timestampTicks;
            Value = value;
        }
    }

    private sealed class SamplePointComparer : IComparer<SamplePoint>
    {
        public static readonly SamplePointComparer Instance = new();

        public int Compare(SamplePoint left, SamplePoint right)
        {
            var timestampComparison = left.TimestampTicks.CompareTo(
                right.TimestampTicks);
            return timestampComparison != 0
                ? timestampComparison
                : left.Value.CompareTo(right.Value);
        }
    }
}
