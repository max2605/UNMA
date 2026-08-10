using System;
using System.Collections.Generic;
using UNMA.Localization;
using System.Linq;

namespace UNMA.Runtime;

public sealed class SystemMetricDescriptor
{
    public string Id { get; }
    public string LabelKey { get; }
    public string LabelFallback { get; }
    public string UnitKey { get; }
    public string UnitFallback { get; }

    public string Label => string.IsNullOrWhiteSpace(LabelKey)
        ? LabelFallback
        : UnmaText.Get(LabelKey, LabelFallback);

    public string Unit => string.IsNullOrWhiteSpace(UnitKey)
        ? UnitFallback
        : UnmaText.Get(UnitKey, UnitFallback);

    public SystemMetricDescriptor(string id, string label, string unit)
    {
        Id = id;
        LabelKey = "";
        LabelFallback = label ?? "";
        UnitKey = "";
        UnitFallback = unit ?? "";
    }

    public SystemMetricDescriptor(
        string id,
        string labelKey,
        string labelFallback,
        string unitKey,
        string unitFallback)
    {
        Id = id;
        LabelKey = labelKey;
        LabelFallback = labelFallback ?? "";
        UnitKey = unitKey;
        UnitFallback = unitFallback ?? "";
    }
}

public static class SystemMetricCatalog
{
    public const string RulePathPrefix = "$global:";
    public const string ProductStoredPrefix = "product.stored.";
    public const string ProductCapacityPrefix = "product.capacity.";
    public const string ProductFillPrefix = "product.fill.";
    public const string MaintenanceQuantityPrefix = "maintenance.quantity.";
    public const string MaintenanceCapacityPrefix = "maintenance.capacity.";
    public const string MaintenanceFillPrefix = "maintenance.fill.";
    public const string MaintenanceDeltaPrefix = "maintenance.delta_month.";
    public const string MaintenanceNeededPrefix = "maintenance.needed_month.";
    public const string MaintenanceNeededMaxPrefix =
        "maintenance.needed_month_max.";

    private static readonly SystemMetricDescriptor[] s_metrics =
    {
        Metric("health.value", "system_metric.health.value.label", "Health last month", "unit.points", "points"),
        Metric("health.disease_penalty", "system_metric.health.disease_penalty.label", "Disease contribution", "unit.points", "points"),
        Metric("health.disease_mortality", "system_metric.health.disease_mortality.label", "Effective disease mortality", "unit.percent_per_month", "%/month"),
        Metric("health.pollution_penalty", "system_metric.health.pollution_penalty.label", "Pollution / waste contribution", "unit.points", "points"),
        Metric("health.structural_value", "system_metric.health.structural_value.label", "Health without disease", "unit.points", "points"),
        Metric("health.expected_loss", "system_metric.health.expected_loss.label", "Expected net population loss", "unit.population_per_month", "population/month"),
        Metric("health.lost_last_month", "system_metric.health.lost_last_month.label", "Lost population", "unit.population_per_month", "population/month"),
        Metric("health.disease_active", "system_metric.health.disease_active.label", "Disease active", "unit.boolean", "0/1"),
        Metric("health.disease_months_left", "system_metric.health.disease_months_left.label", "Remaining disease duration", "unit.months", "months"),
        Metric("health.worker_buffer_months", "system_metric.health.worker_buffer_months.label", "Worker reserve until failure", "unit.months", "months"),
        Metric("health.worker_spiral_margin", "system_metric.health.worker_spiral_margin.label", "Buffer above disease horizon", "unit.months", "months"),
        Metric("workers.reserve_percent", "system_metric.workers.reserve_percent.label", "Worker reserve", "unit.percent_of_population", "% of population"),
        Metric("workers.free_or_missing", "system_metric.workers.free_or_missing.label", "Free / missing workers", "unit.workers", "workers"),
        Metric("workers.missing", "system_metric.workers.missing.label", "Missing workers", "unit.workers", "workers"),
        Metric("food.months", "system_metric.food.months.label", "Food supply", "unit.months", "months"),
        Metric("food.starving", "system_metric.food.starving.label", "Population is starving", "unit.boolean", "0/1"),
        Metric("food.starved_last_month", "system_metric.food.starved_last_month.label", "Starved population", "unit.population_per_month", "population/month"),
        Metric("food.spiral", "system_metric.food.spiral.label", "Active starvation death spiral", "unit.boolean", "0/1"),
        Metric("population.net_change_percent", "system_metric.population.net_change_percent.label", "Net population change without starvation", "unit.percent_per_month", "%/month"),
        Metric("population.total", "system_metric.population.total.label", "Population", "unit.population", "population"),
    };

    public static IReadOnlyList<SystemMetricDescriptor> All => s_metrics;

    public static int FindIndex(string metricId)
    {
        for (var index = 0; index < s_metrics.Length; index++)
        {
            if (string.Equals(
                    s_metrics[index].Id,
                    metricId,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    public static string ToRulePath(string metricId)
    {
        return RulePathPrefix + (metricId ?? "").Trim();
    }

    public static bool TryParseRulePath(string path, out string metricId)
    {
        metricId = "";
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(RulePathPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        metricId = path.Substring(RulePathPrefix.Length).Trim();
        return metricId.Length > 0;
    }

    public static string ProductStoredId(string productId) =>
        DynamicId(ProductStoredPrefix, productId);

    public static string ProductCapacityId(string productId) =>
        DynamicId(ProductCapacityPrefix, productId);

    public static string ProductFillId(string productId) =>
        DynamicId(ProductFillPrefix, productId);

    public static string MaintenanceQuantityId(string productId) =>
        DynamicId(MaintenanceQuantityPrefix, productId);

    public static string MaintenanceCapacityId(string productId) =>
        DynamicId(MaintenanceCapacityPrefix, productId);

    public static string MaintenanceFillId(string productId) =>
        DynamicId(MaintenanceFillPrefix, productId);

    public static string MaintenanceDeltaId(string productId) =>
        DynamicId(MaintenanceDeltaPrefix, productId);

    public static string MaintenanceNeededId(string productId) =>
        DynamicId(MaintenanceNeededPrefix, productId);

    public static string MaintenanceNeededMaxId(string productId) =>
        DynamicId(MaintenanceNeededMaxPrefix, productId);

    public static double CalculateFillPercent(
        double quantity,
        double capacity)
    {
        return capacity <= 0d
            ? 0d
            : Math.Max(0d, Math.Min(100d, 100d * quantity / capacity));
    }

    public static double CalculateWorkerReservePercent(
        int freeOrMissingWorkers,
        int employablePopulation)
    {
        return employablePopulation <= 0
            ? 0d
            : 100d * freeOrMissingWorkers / employablePopulation;
    }

    public static double CalculateExpectedPopulationLoss(
        int population,
        double netPopulationChangePercent)
    {
        if (population <= 0)
        {
            return 0d;
        }
        return population *
               Math.Max(0d, -netPopulationChangePercent) / 100d;
    }

    public static double CalculateWorkerBufferMonths(
        int freeWorkers,
        int homelessPopulation,
        double expectedLossPerMonth)
    {
        return expectedLossPerMonth <= 0d
            ? 9999d
            : (Math.Max(0, freeWorkers) +
               Math.Max(0, homelessPopulation)) / expectedLossPerMonth;
    }

    public static double CalculateWorkerSpiralMargin(
        double workerBufferMonths,
        int diseaseMonthsLeft,
        double maximumHorizonMonths = 2d)
    {
        if (diseaseMonthsLeft <= 0)
        {
            return 9999d;
        }
        return workerBufferMonths -
               Math.Min(maximumHorizonMonths, diseaseMonthsLeft);
    }

    public static int CalculateEffectiveDiseaseMonths(int gameMonthsLeft)
    {
        return Math.Max(0, gameMonthsLeft - 1);
    }

    public static bool CalculateFoodSpiral(
        bool isStarving,
        int freeOrMissingWorkers,
        int workersWithheld,
        int populationWithoutHomeless,
        int starvedLastMonth)
    {
        if (!isStarving)
        {
            return false;
        }
        var withheldThreshold = Math.Max(
            1,
            (int)Math.Ceiling(populationWithoutHomeless * 0.05d));
        return freeOrMissingWorkers <= 0 ||
               workersWithheld >= withheldThreshold ||
               starvedLastMonth > 0;
    }

    public static string LabelFor(string metricId)
    {
        var metric = s_metrics.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                metricId,
                StringComparison.Ordinal));
        return metric == null
            ? metricId ?? UnmaText.Get("auto.2dac0d52e948")
            : metric.Label + " [" + metric.Unit + "]";
    }

    private static SystemMetricDescriptor Metric(
        string id,
        string labelKey,
        string labelFallback,
        string unitKey,
        string unitFallback)
    {
        return new SystemMetricDescriptor(
            id,
            labelKey,
            labelFallback,
            unitKey,
            unitFallback);
    }

    private static string DynamicId(string prefix, string productId)
    {
        return prefix + (productId ?? "").Trim();
    }
}
