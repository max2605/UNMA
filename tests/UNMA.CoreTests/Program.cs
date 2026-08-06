using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using UNMA.Domain;

internal static class Program
{
    private static int s_assertions;

    private static void Main()
    {
        TestComparisons();
        TestBooleanLogic();
        TestAlarmLatch();
        TestConfigurationRoundTrip();
        TestConfigurationMigration();
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
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Warning);
        IsTrue(incoming.IsActive);
        IsFalse(incoming.IsAcknowledged);
        IsTrue(incoming.IsNewOccurrence);

        var acknowledgedStanding = AlarmEvaluation.Transition(
            true,
            true,
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Warning);
        IsTrue(acknowledgedStanding.IsActive);
        IsTrue(acknowledgedStanding.IsAcknowledged);
        IsFalse(acknowledgedStanding.IsNewOccurrence);

        var escalated = AlarmEvaluation.Transition(
            true,
            true,
            AlarmSeverity.Warning,
            true,
            AlarmSeverity.Critical);
        IsFalse(escalated.IsAcknowledged);
        IsTrue(escalated.IsNewOccurrence);

        var normal = AlarmEvaluation.Transition(
            true,
            true,
            AlarmSeverity.Critical,
            false,
            AlarmSeverity.Notice);
        IsFalse(normal.IsActive);
        IsFalse(normal.IsAcknowledged);

        var returned = AlarmEvaluation.Transition(
            false,
            false,
            AlarmSeverity.Notice,
            true,
            AlarmSeverity.Warning);
        IsTrue(returned.IsNewOccurrence);
        IsFalse(returned.IsAcknowledged);
    }

    private static void TestConfigurationRoundTrip()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "system:health",
            SoundId = "siren",
        });
        configuration.Rules.Add(new AlarmRuleDefinition
        {
            Name = "LAGER UND BAND LEER",
            Logic = AlarmLogic.All,
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
    }

    private static void TestConfigurationMigration()
    {
        var oldConfiguration = UnmaConfiguration.CreateDefault();
        oldConfiguration.SchemaVersion = 1;
        oldConfiguration.LauncherX = 0f;
        oldConfiguration.LauncherY = 0f;
        oldConfiguration.Normalize();

        AreEqual(3, oldConfiguration.SchemaVersion);
        AreEqual(-1f, oldConfiguration.LauncherX);
        AreEqual(-1f, oldConfiguration.LauncherY);
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
}
