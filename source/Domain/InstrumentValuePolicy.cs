using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UNMA.Domain;

public readonly struct InstrumentValueSample
{
    public double TimestampSeconds { get; }
    public double Value { get; }

    public InstrumentValueSample(double timestampSeconds, double value)
    {
        TimestampSeconds = timestampSeconds;
        Value = value;
    }
}

public static class InstrumentValuePolicy
{
    private const double ZeroTolerance = 0.000001d;
    private const double MaximumBaselineSkewTicks =
        GameTimeWindowPolicy.SimTicksPerDay * 2d;

    public static string DefinitionSignature(InstrumentDefinition instrument)
    {
        if (instrument == null)
        {
            return "";
        }

        var builder = new StringBuilder();
        AppendSignatureToken(builder, instrument.MetricPath);
        builder.Append(((int)instrument.Aggregation).ToString(
            CultureInfo.InvariantCulture));
        builder.Append('|');

        var hasSources = false;
        if (instrument.Sources != null)
        {
            foreach (var source in instrument.Sources)
            {
                if (source == null || source.EntityId <= 0)
                {
                    continue;
                }
                hasSources = true;
                builder.Append(source.EntityId.ToString(
                    CultureInfo.InvariantCulture));
                builder.Append(':');
                AppendSignatureToken(builder, source.EntityPrototypeId);
            }
        }
        if (!hasSources && instrument.EntityId > 0)
        {
            builder.Append(instrument.EntityId.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            AppendSignatureToken(builder, instrument.EntityPrototypeId);
        }
        return builder.ToString();
    }

    public static bool TryAggregate(
        InstrumentAggregationMode aggregation,
        IReadOnlyList<double> values,
        out double result)
    {
        result = 0d;
        if (values == null || values.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (!IsFinite(values[index]))
            {
                return false;
            }
        }

        switch (aggregation)
        {
            case InstrumentAggregationMode.Single:
                result = values[0];
                break;
            case InstrumentAggregationMode.Sum:
            case InstrumentAggregationMode.Average:
                for (var index = 0; index < values.Count; index++)
                {
                    result += values[index];
                }
                if (aggregation == InstrumentAggregationMode.Average)
                {
                    result /= values.Count;
                }
                break;
            case InstrumentAggregationMode.Minimum:
                result = values[0];
                for (var index = 1; index < values.Count; index++)
                {
                    result = Math.Min(result, values[index]);
                }
                break;
            case InstrumentAggregationMode.Maximum:
                result = values[0];
                for (var index = 1; index < values.Count; index++)
                {
                    result = Math.Max(result, values[index]);
                }
                break;
            default:
                return false;
        }
        return IsFinite(result);
    }

    public static bool TryCalculateTrend(
        IReadOnlyList<InstrumentValueSample> history,
        double currentTimestampSeconds,
        double currentValue,
        InstrumentTrendMode trendMode,
        int windowTicks,
        out double change)
    {
        change = 0d;
        if (history == null || history.Count == 0 ||
            !IsFinite(currentTimestampSeconds) ||
            !IsFinite(currentValue) ||
            windowTicks <= 0 ||
            trendMode == InstrumentTrendMode.None)
        {
            return false;
        }

        var cutoff = currentTimestampSeconds - windowTicks;
        var hasBaseline = false;
        var baselineTimestamp = double.MinValue;
        var baselineValue = 0d;
        for (var index = 0; index < history.Count; index++)
        {
            var sample = history[index];
            if (!IsFinite(sample.TimestampSeconds) ||
                !IsFinite(sample.Value) ||
                sample.TimestampSeconds > cutoff ||
                sample.TimestampSeconds < baselineTimestamp)
            {
                continue;
            }
            hasBaseline = true;
            baselineTimestamp = sample.TimestampSeconds;
            baselineValue = sample.Value;
        }
        if (!hasBaseline ||
            cutoff - baselineTimestamp > MaximumBaselineSkewTicks)
        {
            return false;
        }

        switch (trendMode)
        {
            case InstrumentTrendMode.DecreaseAbsolute:
                change = baselineValue - currentValue;
                break;
            case InstrumentTrendMode.DecreasePercent:
                if (Math.Abs(baselineValue) <= ZeroTolerance)
                {
                    return false;
                }
                change = (baselineValue - currentValue) /
                           Math.Abs(baselineValue) * 100d;
                break;
            case InstrumentTrendMode.IncreaseAbsolute:
                change = currentValue - baselineValue;
                break;
            case InstrumentTrendMode.IncreasePercent:
                if (Math.Abs(baselineValue) <= ZeroTolerance)
                {
                    return false;
                }
                change = (currentValue - baselineValue) /
                         Math.Abs(baselineValue) * 100d;
                break;
            default:
                return false;
        }
        return IsFinite(change);
    }

    public static bool TryEvaluateSustainedComparison(
        IReadOnlyList<InstrumentValueSample> history,
        double currentTimestampTicks,
        double currentValue,
        int windowTicks,
        ComparisonOperator comparison,
        double threshold,
        out bool sustained)
    {
        sustained = false;
        if (history == null || history.Count == 0 ||
            !IsFinite(currentTimestampTicks) ||
            !IsFinite(currentValue) ||
            !IsFinite(threshold) ||
            windowTicks <= 0)
        {
            return false;
        }

        var cutoff = currentTimestampTicks - windowTicks;
        var baselineIndex = -1;
        var baselineTimestamp = double.MinValue;
        for (var index = 0; index < history.Count; index++)
        {
            var sample = history[index];
            if (!IsFinite(sample.TimestampSeconds) ||
                !IsFinite(sample.Value) ||
                sample.TimestampSeconds > cutoff ||
                sample.TimestampSeconds < baselineTimestamp)
            {
                continue;
            }
            baselineIndex = index;
            baselineTimestamp = sample.TimestampSeconds;
        }
        if (baselineIndex < 0 ||
            cutoff - baselineTimestamp > MaximumBaselineSkewTicks)
        {
            return false;
        }

        for (var index = baselineIndex; index < history.Count; index++)
        {
            var sample = history[index];
            if (sample.TimestampSeconds > currentTimestampTicks)
            {
                break;
            }
            if (!AlarmEvaluation.Compare(
                    sample.Value,
                    comparison,
                    threshold))
            {
                sustained = false;
                return true;
            }
        }
        sustained = AlarmEvaluation.Compare(
            currentValue,
            comparison,
            threshold);
        return true;
    }

    public static bool IsTrendTriggered(
        double decrease,
        double deltaThreshold)
    {
        return IsFinite(decrease) &&
               IsFinite(deltaThreshold) &&
               deltaThreshold >= 0d &&
               decrease + ZeroTolerance >= deltaThreshold;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static void AppendSignatureToken(
        StringBuilder builder,
        string value)
    {
        value ??= "";
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }
}
