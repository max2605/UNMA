using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using UNMA.Audio;
using UNMA.Domain;
using UNMA.Runtime;

internal static class Program
{
    private static int s_assertions;

    private static void Main()
    {
        TestComparisons();
        TestBooleanLogic();
        TestAlarmLatch();
        TestSystemAlarmSelection();
        TestSystemMetricMath();
        TestConfigurationRoundTrip();
        TestConfigurationMigration();
        TestMechanicalSiren();
        Console.WriteLine(
            $"UNMA core tests passed: {s_assertions} assertions.");
    }

    private static void TestComparisons()
    {
        IsTrue(AlarmEvaluation.Compare(19, ComparisonOperator.Less, 20));
        IsTrue(AlarmEvaluation.Compare(20, ComparisonOperator.LessOrEqual, 20));
        IsTrue(AlarmEvaluation.Compare(20, ComparisonOperator.Equal, 20));
        IsTrue(AlarmEvaluation.Compare(
            20.0000001,
            ComparisonOperator.Equal,
            20));
        IsTrue(AlarmEvaluation.Compare(21, ComparisonOperator.NotEqual, 20));
        IsTrue(AlarmEvaluation.Compare(
            20,
            ComparisonOperator.GreaterOrEqual,
            20));
        IsTrue(AlarmEvaluation.Compare(21, ComparisonOperator.Greater, 20));
        IsFalse(AlarmEvaluation.Compare(20, ComparisonOperator.Less, 20));
    }

    private static void TestBooleanLogic()
    {
        IsTrue(AlarmEvaluation.Combine(
            new[] { true, true, true },
            AlarmLogic.All));
        IsFalse(AlarmEvaluation.Combine(
            new[] { true, false, true },
            AlarmLogic.All));
        IsTrue(AlarmEvaluation.Combine(
            new[] { false, true, false },
            AlarmLogic.Any));
        IsFalse(AlarmEvaluation.Combine(
            Array.Empty<bool>(),
            AlarmLogic.Any));
    }

    private static void TestAlarmLatch()
    {
        var incoming = AlarmEvaluation.Transition(
            false,
            false,
            false,
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Warning,
            false);
        IsTrue(incoming.IsActive);
        IsFalse(incoming.IsAcknowledged);
        IsFalse(incoming.IsGoneUnacknowledged);
        IsTrue(incoming.IsNewOccurrence);

        var acknowledgedStanding = AlarmEvaluation.Transition(
            true,
            true,
            false,
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Warning,
            false);
        IsTrue(acknowledgedStanding.IsActive);
        IsTrue(acknowledgedStanding.IsAcknowledged);
        IsFalse(acknowledgedStanding.IsGoneUnacknowledged);
        IsFalse(acknowledgedStanding.IsNewOccurrence);

        var escalated = AlarmEvaluation.Transition(
            true,
            true,
            false,
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Critical,
            false);
        IsFalse(escalated.IsAcknowledged);
        IsTrue(escalated.IsNewOccurrence);

        var sameSeverityStageEscalation = AlarmEvaluation.Transition(
            true,
            true,
            false,
            AlarmSeverity.Critical,
            true,
            AlarmSeverity.Critical,
            false,
            true);
        IsTrue(sameSeverityStageEscalation.IsActive);
        IsFalse(sameSeverityStageEscalation.IsAcknowledged);
        IsTrue(sameSeverityStageEscalation.IsNewOccurrence);

        var goneUnacknowledged = AlarmEvaluation.Transition(
            true,
            false,
            false,
            AlarmSeverity.Critical,
            false,
            AlarmSeverity.Critical,
            false);
        IsFalse(goneUnacknowledged.IsActive);
        IsFalse(goneUnacknowledged.IsAcknowledged);
        IsTrue(goneUnacknowledged.IsGoneUnacknowledged);

        var stillGone = AlarmEvaluation.Transition(
            false,
            false,
            true,
            AlarmSeverity.Critical,
            false,
            AlarmSeverity.Critical,
            false);
        IsTrue(stillGone.IsGoneUnacknowledged);

        var returned = AlarmEvaluation.Transition(
            false,
            false,
            true,
            AlarmSeverity.Critical,
            true,
            AlarmSeverity.Warning,
            false);
        IsTrue(returned.IsActive);
        IsFalse(returned.IsGoneUnacknowledged);
        IsTrue(returned.IsNewOccurrence);
        IsFalse(returned.IsAcknowledged);

        var automaticallyCleared = AlarmEvaluation.Transition(
            true,
            false,
            false,
            AlarmSeverity.Warning,
            false,
            AlarmSeverity.Warning,
            true);
        IsFalse(automaticallyCleared.IsActive);
        IsFalse(automaticallyCleared.IsGoneUnacknowledged);

        var acknowledgedThenCleared = AlarmEvaluation.Transition(
            true,
            true,
            false,
            AlarmSeverity.Warning,
            false,
            AlarmSeverity.Warning,
            false);
        IsFalse(acknowledgedThenCleared.IsActive);
        IsFalse(acknowledgedThenCleared.IsGoneUnacknowledged);

        var downgraded = AlarmEvaluation.Transition(
            true,
            true,
            false,
            AlarmSeverity.Emergency,
            true,
            AlarmSeverity.Warning,
            false);
        IsTrue(downgraded.IsAcknowledged);
        IsFalse(downgraded.IsNewOccurrence);
    }

    private static void TestSystemAlarmSelection()
    {
        var defaults = UnmaConfiguration.CreateDefaultSystemAlarms();
        var health = defaults.Find(alarm => alarm.Id == "system:health");
        var food = defaults.Find(alarm => alarm.Id == "system:food");
        var workers = defaults.Find(alarm => alarm.Id == "system:workers");

        var metrics = BaseSystemMetrics();
        AreEqual<SystemAlarmStageDefinition>(
            null,
            AlarmEvaluation.SelectSystemStage(health, metrics));

        metrics["health.value"] = -1;
        metrics["health.disease_penalty"] = -11;
        metrics["health.structural_value"] = 10;
        metrics["health.disease_active"] = 1;
        metrics["health.worker_buffer_months"] = 0;
        metrics["health.worker_spiral_margin"] = -2;
        AreEqual(
            "emergency.worker_spiral",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);

        metrics["health.value"] = -5;
        metrics["health.disease_penalty"] = -15;
        metrics["health.structural_value"] = 10;
        metrics["health.worker_buffer_months"] = 100;
        metrics["health.worker_spiral_margin"] = 98;
        AreEqual(
            "warning",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);

        metrics["health.disease_penalty"] = 0;
        metrics["health.pollution_penalty"] = -15;
        metrics["health.structural_value"] = -5;
        AreEqual(
            "emergency.structural_spiral",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);

        metrics = BaseSystemMetrics();
        metrics["health.value"] = -1;
        metrics["health.worker_spiral_margin"] = -2;
        AreEqual(
            "warning",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);

        metrics["health.value"] = -10;
        AreEqual(
            "critical",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);

        var warningStage = health.Stages.Find(stage => stage.Id == "warning");
        warningStage.Severity = AlarmSeverity.Emergency;
        AreEqual(
            "warning",
            AlarmEvaluation.SelectSystemStage(health, metrics).Id);
        warningStage.Severity = AlarmSeverity.Warning;

        metrics = BaseSystemMetrics();
        metrics["workers.reserve_percent"] = 4;
        AreEqual(
            AlarmSeverity.Warning,
            AlarmEvaluation.SelectSystemStage(workers, metrics).Severity);
        metrics["workers.reserve_percent"] = -1;
        AreEqual(
            AlarmSeverity.Critical,
            AlarmEvaluation.SelectSystemStage(workers, metrics).Severity);
        metrics["workers.reserve_percent"] = 0;
        AreEqual(
            AlarmSeverity.Warning,
            AlarmEvaluation.SelectSystemStage(workers, metrics).Severity);
        metrics["workers.reserve_percent"] = 5;
        AreEqual<SystemAlarmStageDefinition>(
            null,
            AlarmEvaluation.SelectSystemStage(workers, metrics));

        metrics = BaseSystemMetrics();
        metrics["food.months"] = 12;
        AreEqual(
            AlarmSeverity.Warning,
            AlarmEvaluation.SelectSystemStage(food, metrics).Severity);
        metrics["food.months"] = 3;
        AreEqual(
            AlarmSeverity.Critical,
            AlarmEvaluation.SelectSystemStage(food, metrics).Severity);
        metrics["food.starving"] = 1;
        metrics["food.spiral"] = 1;
        AreEqual(
            AlarmSeverity.Emergency,
            AlarmEvaluation.SelectSystemStage(food, metrics).Severity);
        metrics["food.starving"] = 0;
        metrics["food.spiral"] = 0;
        metrics["food.starved_last_month"] = 1;
        metrics["food.months"] = 24;
        AreEqual<SystemAlarmStageDefinition>(
            null,
            AlarmEvaluation.SelectSystemStage(food, metrics));
    }

    private static void TestSystemMetricMath()
    {
        AreClose(
            4d,
            SystemMetricCatalog.CalculateWorkerReservePercent(4, 100));
        AreClose(
            -1d,
            SystemMetricCatalog.CalculateWorkerReservePercent(-1, 100));

        var smallExpectedLoss =
            SystemMetricCatalog.CalculateExpectedPopulationLoss(100, -0.1d);
        AreClose(0.1d, smallExpectedLoss);
        AreClose(
            10d,
            SystemMetricCatalog.CalculateWorkerBufferMonths(
                1,
                0,
                smallExpectedLoss));
        AreClose(
            0d,
            SystemMetricCatalog.CalculateWorkerBufferMonths(
                0,
                0,
                smallExpectedLoss));
        AreClose(
            -2d,
            SystemMetricCatalog.CalculateWorkerSpiralMargin(0, 6));
        AreClose(
            9999d,
            SystemMetricCatalog.CalculateWorkerSpiralMargin(0, 0));
        AreEqual(
            0,
            SystemMetricCatalog.CalculateEffectiveDiseaseMonths(1));
        AreEqual(
            5,
            SystemMetricCatalog.CalculateEffectiveDiseaseMonths(6));
        IsFalse(SystemMetricCatalog.CalculateFoodSpiral(
            true,
            10000,
            1,
            10100,
            0));
        IsTrue(SystemMetricCatalog.CalculateFoodSpiral(
            true,
            0,
            0,
            100,
            0));
        IsTrue(SystemMetricCatalog.CalculateFoodSpiral(
            true,
            100,
            0,
            100,
            1));
        IsFalse(SystemMetricCatalog.CalculateFoodSpiral(
            false,
            -10,
            50,
            100,
            2));

        var bufferedLoss =
            SystemMetricCatalog.CalculateExpectedPopulationLoss(
                10100,
                -0.5d);
        AreClose(50.5d, bufferedLoss);
        IsTrue(
            SystemMetricCatalog.CalculateWorkerBufferMonths(
                10000,
                0,
                bufferedLoss) > 190d);
        AreClose(
            0d,
            SystemMetricCatalog.CalculateExpectedPopulationLoss(
                10000,
                0.25d));
        AreClose(
            10d,
            SystemMetricCatalog.CalculateExpectedPopulationLoss(
                10000,
                -0.1d));
    }

    private static void TestConfigurationRoundTrip()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "system:health",
            SoundId = "siren",
            AutoAcknowledgeOnClear = true,
        });
        var editedSystemAlarm = configuration.SystemAlarms
            .Find(alarm => alarm.Id == "system:health");
        editedSystemAlarm.AutoAcknowledgeOnClear = true;
        var editedSystemStage = editedSystemAlarm.Stages.Find(
            stage => stage.Id == "warning");
        editedSystemStage.Enabled = false;
        editedSystemStage.Message = "MEINE GESUNDHEITSMELDUNG";
        editedSystemStage.Severity = AlarmSeverity.Critical;
        editedSystemStage.Logic = AlarmLogic.Any;
        editedSystemStage.ActiveColor = "#123456";
        editedSystemStage.SoundId = "triangle";
        editedSystemStage.Conditions[0].Threshold = 7.5d;
        configuration.Rules.Add(new AlarmRuleDefinition
        {
            Name = "LAGER UND BAND LEER",
            Logic = AlarmLogic.All,
            AutoAcknowledgeOnClear = true,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    EntityId = 17,
                    EntityTitle = "Lagerhaus",
                    EntityType = "Mafi.Base.Storage",
                    EntityPrototypeId = "AirStorageT1",
                    MetricPath = "$stored.quantity",
                    MetricLabel = "Lagerinhalt",
                    Comparison = ComparisonOperator.Equal,
                    Threshold = 0,
                },
                new()
                {
                    EntityId = 18,
                    EntityTitle = "Fließband",
                    MetricPath = "TransportedProducts.Count",
                    MetricLabel = "Produkte / Count",
                    Comparison = ComparisonOperator.Less,
                    Threshold = 20,
                },
            },
        });
        configuration.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "vanilla:42",
            Name = "GEGANGENE MELDUNG",
            Detail = "Testzustand",
            Source = "vanilla",
            ActiveColor = "#F05A32",
            SoundId = "horn",
            OverrideId = "vanilla:test",
            OccurrenceId = "vanilla:test",
            OccurrencePriority = 210,
            Severity = AlarmSeverity.Critical,
            IsGoneUnacknowledged = true,
            Sequence = 73,
        });

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();

        AreEqual(2, restored.Panels.Count);
        AreEqual(1, restored.Rules.Count);
        AreEqual(2, restored.Rules[0].Conditions.Count);
        AreEqual("LAGER UND BAND LEER", restored.Rules[0].Name);
        AreEqual(20d, restored.Rules[0].Conditions[1].Threshold);
        AreEqual(
            "AirStorageT1",
            restored.Rules[0].Conditions[0].EntityPrototypeId);
        AreEqual(1, restored.SoundOverrides.Count);
        AreEqual("siren", restored.SoundOverrides[0].SoundId);
        IsTrue(restored.SoundOverrides[0].AutoAcknowledgeOnClear);
        AreEqual(5, restored.SchemaVersion);
        AreEqual(3, restored.SystemAlarms.Count);
        IsTrue(restored.Rules[0].AutoAcknowledgeOnClear);
        var restoredSystemAlarm = restored.SystemAlarms
            .Find(alarm => alarm.Id == "system:health");
        IsTrue(restoredSystemAlarm.AutoAcknowledgeOnClear);
        var restoredSystemStage = restoredSystemAlarm.Stages.Find(
            stage => stage.Id == "warning");
        IsFalse(restoredSystemStage.Enabled);
        AreEqual("MEINE GESUNDHEITSMELDUNG", restoredSystemStage.Message);
        AreEqual(AlarmSeverity.Critical, restoredSystemStage.Severity);
        AreEqual(AlarmLogic.Any, restoredSystemStage.Logic);
        AreEqual("#123456", restoredSystemStage.ActiveColor);
        AreEqual("triangle", restoredSystemStage.SoundId);
        AreEqual(7.5d, restoredSystemStage.Conditions[0].Threshold);
        AreEqual(1, restored.AlarmMemories.Count);
        var restoredMemory = restored.AlarmMemories[0];
        AreEqual("vanilla:42", restoredMemory.Key);
        AreEqual("GEGANGENE MELDUNG", restoredMemory.Name);
        IsFalse(restoredMemory.IsActive);
        IsTrue(restoredMemory.IsGoneUnacknowledged);
        AreEqual("vanilla:test", restoredMemory.OccurrenceId);
        AreEqual(210, restoredMemory.OccurrencePriority);
        AreEqual(73L, restoredMemory.Sequence);
    }

    private static void TestConfigurationMigration()
    {
        var oldConfiguration = UnmaConfiguration.CreateDefault();
        oldConfiguration.SchemaVersion = 1;
        oldConfiguration.LauncherX = 0f;
        oldConfiguration.LauncherY = 0f;
        oldConfiguration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "system:health",
            SoundId = "horn",
        });
        oldConfiguration.Normalize();

        AreEqual(5, oldConfiguration.SchemaVersion);
        AreEqual(-1f, oldConfiguration.LauncherX);
        AreEqual(-1f, oldConfiguration.LauncherY);
        AreEqual(3, oldConfiguration.SystemAlarms.Count);

        var health = oldConfiguration.SystemAlarms.Find(
            alarm => alarm.Id == "system:health");
        IsTrue(health.Stages.TrueForAll(stage => stage.SoundId == "horn"));
        var warning = health.Stages.Find(stage => stage.Id == "warning");
        warning.Conditions[0].Threshold = 7;
        health.Stages.RemoveAll(stage => stage.Id == "critical");
        oldConfiguration.Normalize();
        AreEqual(7d, warning.Conditions[0].Threshold);
        IsTrue(health.Stages.Exists(stage => stage.Id == "critical"));

        var legacyJson =
            "{\"SchemaVersion\":4," +
            "\"Panels\":[{\"Id\":\"main\",\"Name\":\"ALT\"," +
            "\"Columns\":3}]," +
            "\"Rules\":[{\"Id\":\"legacy\",\"PanelId\":\"main\"," +
            "\"Name\":\"ALTE MELDUNG\",\"Conditions\":[]}]," +
            "\"SoundOverrides\":[{\"AlarmId\":\"vanilla:test\"," +
            "\"SoundId\":\"horn\"}]}";
        using var legacyStream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(legacyJson));
        var legacy = (UnmaConfiguration)new DataContractJsonSerializer(
            typeof(UnmaConfiguration)).ReadObject(legacyStream);
        legacy.Normalize();
        AreEqual(5, legacy.SchemaVersion);
        IsFalse(legacy.Rules[0].AutoAcknowledgeOnClear);
        IsFalse(legacy.SoundOverrides[0].AutoAcknowledgeOnClear);
        IsTrue(legacy.SystemAlarms.TrueForAll(alarm =>
            !alarm.AutoAcknowledgeOnClear));
        AreEqual(0, legacy.AlarmMemories.Count);
    }

    private static void TestMechanicalSiren()
    {
        const int sampleRate = 44100;
        var samples = MechanicalSirenSynth.Generate(sampleRate);
        var repeated = MechanicalSirenSynth.Generate(sampleRate);

        AreEqual(176400, samples.Length);
        AreEqual(samples.Length, repeated.Length);
        AreClose(82.13471502590673, MechanicalSirenSynth.FrequencyAt(0d));
        AreClose(420d, MechanicalSirenSynth.FrequencyAt(2d));
        AreClose(
            82.13471502590673,
            MechanicalSirenSynth.FrequencyAt(4d));

        var sum = 0d;
        var energy = 0d;
        var peak = 0d;
        var allFinite = true;
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            allFinite &= !float.IsNaN(sample) && !float.IsInfinity(sample);
            sum += sample;
            energy += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        IsTrue(allFinite);
        IsTrue(Math.Abs(sum / samples.Length) < 0.000001d);
        IsTrue(Math.Sqrt(energy / samples.Length) > 0.25d);
        IsTrue(peak <= 0.860001d);
        IsTrue(peak >= 0.859d);
        IsTrue(Math.Abs(samples[0] - samples[^1]) < 0.05d);
        AreEqual(samples[12345], repeated[12345]);

        var previousRise = MechanicalSirenSynth.FrequencyAt(0d);
        var previousFall = MechanicalSirenSynth.FrequencyAt(2d);
        for (var step = 1; step <= 20; step++)
        {
            var rise = MechanicalSirenSynth.FrequencyAt(step / 10d);
            var fall = MechanicalSirenSynth.FrequencyAt(2d + step / 10d);
            IsTrue(rise >= previousRise);
            IsTrue(fall <= previousFall);
            previousRise = rise;
            previousFall = fall;
        }
    }

    private static Dictionary<string, double> BaseSystemMetrics()
    {
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["health.value"] = 10,
            ["health.disease_penalty"] = 0,
            ["health.disease_mortality"] = 0,
            ["health.pollution_penalty"] = 0,
            ["health.structural_value"] = 10,
            ["health.expected_loss"] = 0,
            ["health.lost_last_month"] = 0,
            ["health.disease_active"] = 0,
            ["health.disease_months_left"] = 0,
            ["health.worker_buffer_months"] = 9999,
            ["health.worker_spiral_margin"] = 9999,
            ["workers.reserve_percent"] = 20,
            ["workers.free_or_missing"] = 20,
            ["workers.missing"] = 0,
            ["food.months"] = 24,
            ["food.starving"] = 0,
            ["food.starved_last_month"] = 0,
            ["food.spiral"] = 0,
            ["population.net_change_percent"] = 0,
            ["population.total"] = 100,
        };
    }

    private static void IsTrue(bool value)
    {
        s_assertions++;
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void IsFalse(bool value)
    {
        s_assertions++;
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void AreEqual<T>(T expected, T actual)
    {
        s_assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void AreClose(double expected, double actual)
    {
        s_assertions++;
        if (Math.Abs(expected - actual) > 0.000001d)
        {
            throw new InvalidOperationException(
                $"Expected approximately '{expected}', got '{actual}'.");
        }
    }
}
