using System;
using System.Collections.Generic;
using UNMA.Api;

namespace ExampleProvider;

/// <summary>
/// Illustrative provider-side integration. ProviderTankEntity and
/// ProviderPumpEntity stand for entity classes owned by the provider mod.
/// </summary>
public sealed class UnmaIntegration : IDisposable
{
    private const string OwnerModId = "ExampleProvider";
    private const string TankPrototypeId =
        "ExampleProvider_PressureTank";
    private const string PressureMetricId = "example.pressure_percent";
    private const string VeryHighPressureAlarmId =
        "tank-pressure-very-high";
    private const string PumpTripAlarmId = "pump-trip";

    public bool Register(out string error)
    {
        var metric = new ExternalMetricDefinition
        {
            Id = PressureMetricId,
            PrototypeId = TankPrototypeId,
            LabelKey =
                "multilanglib.ExampleProvider.metric.pressure_percent",
            LabelFallback = "Tankdruck",
            Unit = "%",
            Reader = ReadPressurePercent,
        };

        if (!UnmaApi.TryRegisterMetric(OwnerModId, metric, out error))
        {
            return false;
        }

        // The same object model used by JSON is available to code. This
        // example adds a second, more severe automatic pressure stage.
        var template = new ExternalAlarmTemplateDefinition
        {
            Id = VeryHighPressureAlarmId,
            PrototypeIds = new List<string> { TankPrototypeId },
            Scope = "per_entity",
            PanelId = "main",
            LocalizationNamespace = OwnerModId,
            MessageKey =
                "multilanglib.ExampleProvider.alarm.tank_pressure_very_high",
            MessageFallback = "TANKDRUCK NOTFALL",
            DetailKey =
                "multilanglib.ExampleProvider.alarm." +
                "tank_pressure_very_high.detail",
            DetailFallback = "Der Tankdruck liegt über 98 Prozent.",
            Severity = "emergency",
            SoundId = "siren",
            ActiveColor = "#E51B23",
            AutoAcknowledgeOnClear = false,
            Logic = "all",
            Conditions = new List<ExternalAlarmConditionDefinition>
            {
                new()
                {
                    Metric = PressureMetricId,
                    LabelKey =
                        "multilanglib.ExampleProvider.metric.pressure_percent",
                    LabelFallback = "Tankdruck",
                    Operator = ">=",
                    Threshold = 98,
                    ValueMode = "absolute",
                },
            },
        };

        if (UnmaApi.TryRegisterAlarmTemplate(
                OwnerModId,
                template,
                out error))
        {
            return true;
        }

        UnmaApi.UnregisterMetric(
            OwnerModId,
            TankPrototypeId,
            PressureMetricId);
        return false;
    }

    /// <summary>
    /// Publish the current protection state. Repeating the same state is
    /// harmless. StableId must survive save/load and must not contain spaces.
    /// </summary>
    public bool PublishPumpState(
        ProviderPumpEntity pump,
        out string error)
    {
        if (pump == null || string.IsNullOrWhiteSpace(pump.StableId))
        {
            error = "A pump with a stable ID is required.";
            return false;
        }

        return UnmaApi.TryPublishAlarmState(
            OwnerModId,
            new ExternalAlarmState
            {
                Id = PumpTripAlarmId,
                InstanceId = pump.StableId,
                Active = pump.ProtectionTripped,
                PanelId = "main",
                PrototypeId = "ExampleProvider_TransferPump",
                EntityKey = pump.StableId,
                LocalizationNamespace = OwnerModId,
                MessageKey =
                    "multilanglib.ExampleProvider.alarm.pump_trip",
                MessageFallback = "FÖRDERPUMPE STÖRUNG",
                DetailKey =
                    "multilanglib.ExampleProvider.alarm.pump_trip.detail",
                DetailFallback =
                    "Die Förderpumpe wurde durch ihre Schutzschaltung " +
                    "abgeschaltet.",
                Severity = "critical",
                SoundId = "horn",
                ActiveColor = "#F05A32",
                AutoAcknowledgeOnClear = false,
            },
            out error);
    }

    /// <summary>
    /// Call only after Active=false was published and UNMA had a chance to
    /// observe the gone transition.
    /// </summary>
    public bool RemovePumpState(string stableId)
    {
        return UnmaApi.RemoveAlarmState(
            OwnerModId,
            PumpTripAlarmId,
            stableId);
    }

    public void Dispose()
    {
        UnmaApi.UnregisterOwner(OwnerModId);
    }

    private static double? ReadPressurePercent(object entity)
    {
        var tank = entity as ProviderTankEntity;
        if (tank == null ||
            double.IsNaN(tank.PressurePercent) ||
            double.IsInfinity(tank.PressurePercent))
        {
            return null;
        }

        return tank.PressurePercent;
    }
}

// Replace these placeholders with the provider mod's real entity types.
public sealed class ProviderTankEntity
{
    public double PressurePercent { get; set; }
}

public sealed class ProviderPumpEntity
{
    public string StableId { get; set; } = "";
    public bool ProtectionTripped { get; set; }
}
