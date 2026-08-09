using System;
using System.Collections.Generic;
using UNMA.Localization;
using System.Linq;

namespace UNMA.Runtime;

public sealed class SystemMetricDescriptor
{
    public string Id { get; }
    public string Label { get; }
    public string Unit { get; }

    public SystemMetricDescriptor(string id, string label, string unit)
    {
        Id = id;
        Label = label;
        Unit = unit;
    }
}

public static class SystemMetricCatalog
{
    private static readonly SystemMetricDescriptor[] s_metrics =
    {
        new("health.value", UnmaText.Get("auto.52dcc85d63dc"), "Punkte"),
        new("health.disease_penalty", "Krankheitsbeitrag", "Punkte"),
        new("health.disease_mortality", UnmaText.Get("auto.791877a44fa5"), "%/Monat"),
        new("health.pollution_penalty", "Pollution/Müll-Beitrag", "Punkte"),
        new("health.structural_value", UnmaText.Get("auto.560f43bd0469"), "Punkte"),
        new("health.expected_loss", UnmaText.Get("auto.9a26434dac89"), "Pops/Monat"),
        new("health.lost_last_month", UnmaText.Get("auto.d52de2e797c1"), "Pops/Monat"),
        new("health.disease_active", UnmaText.Get("auto.763c03e8c35e"), "0/1"),
        new("health.disease_months_left", "Krankheits-Restdauer", "Monate"),
        new("health.worker_buffer_months", UnmaText.Get("auto.8475ec6651d6"), "Monate"),
        new("health.worker_spiral_margin", UnmaText.Get("auto.27ded4bdf9b8"), "Monate"),
        new("workers.reserve_percent", "Arbeitsreserve", UnmaText.Get("auto.ad2e3884096f")),
        new("workers.free_or_missing", UnmaText.Get("auto.ffd0f617865d"), "Arbeiter"),
        new("workers.missing", UnmaText.Get("auto.c00320b60ba7"), "Arbeiter"),
        new("food.months", "Nahrungsvorrat", "Monate"),
        new("food.starving", UnmaText.Get("auto.68c2093510ec"), "0/1"),
        new("food.starved_last_month", "Verhungert", "Pops/Monat"),
        new("food.spiral", UnmaText.Get("auto.1d29292a2ba9"), "0/1"),
        new("population.net_change_percent", UnmaText.Get("auto.2004ff123977"), "%/Monat"),
        new("population.total", UnmaText.Get("auto.51189f33cea8"), "Pops"),
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
}
