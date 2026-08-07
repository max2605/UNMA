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
        TestComparableValues();
        TestBooleanLogic();
        TestAlarmLatch();
        TestSustainedVanillaAlarmPolicy();
        TestAlarmHistoryState();
        TestSystemAlarmSelection();
        TestSystemMetricMath();
        TestPanelSlotProjection();
        TestConfigurationRoundTrip();
        TestAlarmHistoryRoundTrip();
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

    private static void TestComparableValues()
    {
        IsTrue(AlarmEvaluation.TryCalculateComparable(
            42d,
            ConditionValueMode.Absolute,
            double.NaN,
            out var absolute));
        AreEqual(42d, absolute);

        IsTrue(AlarmEvaluation.TryCalculateComparable(
            25d,
            ConditionValueMode.PercentOfReference,
            100d,
            out var percent));
        AreEqual(25d, percent);
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.Less,
            30d));
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.LessOrEqual,
            25d));
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.Equal,
            25d));
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.NotEqual,
            24d));
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.GreaterOrEqual,
            25d));
        IsTrue(AlarmEvaluation.Compare(
            percent,
            ComparisonOperator.Greater,
            20d));

        IsTrue(AlarmEvaluation.TryCalculateComparable(
            150d,
            ConditionValueMode.PercentOfReference,
            100d,
            out var overCapacity));
        AreEqual(150d, overCapacity);

        IsFalse(AlarmEvaluation.TryCalculateComparable(
            10d,
            ConditionValueMode.PercentOfReference,
            0d,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            10d,
            ConditionValueMode.PercentOfReference,
            -1d,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            double.NaN,
            ConditionValueMode.PercentOfReference,
            100d,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            double.PositiveInfinity,
            ConditionValueMode.PercentOfReference,
            100d,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            10d,
            ConditionValueMode.PercentOfReference,
            double.NaN,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            10d,
            ConditionValueMode.PercentOfReference,
            double.PositiveInfinity,
            out _));
        IsFalse(AlarmEvaluation.TryCalculateComparable(
            10d,
            (ConditionValueMode)999,
            100d,
            out _));
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

    private static void TestSustainedVanillaAlarmPolicy()
    {
        const string prototypeId = "HomelessLeft";
        const string overrideId = "vanilla:HomelessLeft";
        const string stableKey = "vanilla:sustained:HomelessLeft";

        AreEqual(
            stableKey,
            SustainedVanillaAlarmPolicy.AlarmKeyForNotification(
                prototypeId,
                "vanilla:10"));
        AreEqual(
            stableKey,
            SustainedVanillaAlarmPolicy.AlarmKeyForNotification(
                prototypeId,
                "vanilla:11"));
        AreEqual(
            "vanilla:99",
            SustainedVanillaAlarmPolicy.AlarmKeyForNotification(
                "NotEnoughWorkers",
                "vanilla:99"));
        AreEqual(
            stableKey,
            SustainedVanillaAlarmPolicy.AlarmKeyForOverrideId(overrideId));
        IsTrue(SustainedVanillaAlarmPolicy.IsSustainedPrototype(
            prototypeId));
        IsTrue(SustainedVanillaAlarmPolicy.IsSustainedOverrideId(
            overrideId));
        IsTrue(SustainedVanillaAlarmPolicy.IgnoresNotificationRemoval(
            prototypeId));
        IsFalse(SustainedVanillaAlarmPolicy.IgnoresNotificationRemoval(
            "NotEnoughWorkers"));
        IsTrue(SustainedVanillaAlarmPolicy.MatchesHistory(
            prototypeId,
            "vanilla:12",
            prototypeId));
        IsFalse(SustainedVanillaAlarmPolicy.MatchesHistory(
            prototypeId,
            "vanilla:12",
            "NotEnoughWorkers"));
        IsFalse(SustainedVanillaAlarmPolicy.ShouldClear(prototypeId, -1d));
        IsFalse(SustainedVanillaAlarmPolicy.ShouldClear(
            prototypeId,
            -double.Epsilon));
        IsTrue(SustainedVanillaAlarmPolicy.ShouldClear(prototypeId, 0d));
        IsTrue(SustainedVanillaAlarmPolicy.ShouldClear(prototypeId, 1d));
        IsTrue(SustainedVanillaAlarmPolicy.ShouldProcessNotification(
            prototypeId,
            -1d));
        IsFalse(SustainedVanillaAlarmPolicy.ShouldProcessNotification(
            prototypeId,
            0d));
        IsTrue(SustainedVanillaAlarmPolicy.ShouldProcessNotification(
            "NotEnoughWorkers",
            0d));

        var firstMonth = AlarmEvaluation.Transition(
            false,
            false,
            false,
            AlarmSeverity.Critical,
            true,
            AlarmSeverity.Critical,
            false);
        IsTrue(firstMonth.IsNewOccurrence);
        IsFalse(firstMonth.IsAcknowledged);

        var acknowledgedNextMonth = AlarmEvaluation.Transition(
            firstMonth.IsActive,
            true,
            firstMonth.IsGoneUnacknowledged,
            AlarmSeverity.Critical,
            true,
            AlarmSeverity.Critical,
            false);
        IsFalse(acknowledgedNextMonth.IsNewOccurrence);
        IsTrue(acknowledgedNextMonth.IsAcknowledged);

        var clearedAtZero = AlarmEvaluation.Transition(
            acknowledgedNextMonth.IsActive,
            acknowledgedNextMonth.IsAcknowledged,
            acknowledgedNextMonth.IsGoneUnacknowledged,
            AlarmSeverity.Critical,
            false,
            AlarmSeverity.Critical,
            false);
        IsFalse(clearedAtZero.IsActive);
        IsFalse(clearedAtZero.IsGoneUnacknowledged);

        var genuinelyReturned = AlarmEvaluation.Transition(
            clearedAtZero.IsActive,
            clearedAtZero.IsAcknowledged,
            clearedAtZero.IsGoneUnacknowledged,
            AlarmSeverity.Critical,
            true,
            AlarmSeverity.Critical,
            false);
        IsTrue(genuinelyReturned.IsNewOccurrence);
        IsFalse(genuinelyReturned.IsAcknowledged);
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

    private static void TestPanelSlotProjection()
    {
        var slots = new List<PanelSlotDefinition>
        {
            new()
            {
                AlarmId = "system:food",
                DisplayName = "NAHRUNGSVERSORGUNG",
                Source = "system",
                Severity = AlarmSeverity.Warning,
            },
            new()
            {
                AlarmId = "system:workers",
                DisplayName = "ARBEITERRESERVE",
                Source = "system",
                Severity = AlarmSeverity.Warning,
            },
            new()
            {
                AlarmId = "vanilla:NotEnoughWorkers:entity:17",
                DisplayName = "NICHT GENUG ARBEITER",
                Source = "vanilla",
                Severity = AlarmSeverity.Critical,
            },
        };
        var candidates = new List<AlarmView>
        {
            new()
            {
                Key = "vanilla:41",
                SlotId = "vanilla:NotEnoughWorkers:entity:17",
                OverrideId = "vanilla:NotEnoughWorkers",
                Name = "Nicht genug Arbeiter",
                Source = "vanilla",
                Severity = AlarmSeverity.Critical,
                Sequence = 41,
            },
            new()
            {
                Key = "system:workers",
                SlotId = "system:workers",
                Name = "ARBEITER FEHLEN",
                Source = "system",
                Severity = AlarmSeverity.Critical,
                IsActive = true,
                IsAcknowledged = true,
                Sequence = 50,
            },
            new()
            {
                Key = "vanilla:42",
                SlotId = "vanilla:NotEnoughWorkers:entity:17",
                OverrideId = "vanilla:NotEnoughWorkers",
                Name = "Nicht genug Arbeiter",
                Detail = "NotEnoughWorkers · Kapitänsbüro II",
                Source = "vanilla",
                Severity = AlarmSeverity.Critical,
                IsActive = true,
                Sequence = 42,
            },
        };

        var projected = PanelSlotProjection.Project(slots, candidates);
        AreEqual(3, projected.Count);
        AreEqual("system:food", projected[0].SlotId);
        AreEqual("system:food", projected[0].Key);
        AreEqual("NAHRUNGSVERSORGUNG", projected[0].Name);
        AreEqual("system", projected[0].Source);
        AreEqual(AlarmSeverity.Warning, projected[0].Severity);
        IsFalse(projected[0].IsActive);
        IsFalse(projected[0].IsMissingSource);
        AreEqual("system:workers", projected[1].SlotId);
        IsTrue(projected[1].IsActive);
        IsTrue(projected[1].IsAcknowledged);
        AreEqual(
            "vanilla:NotEnoughWorkers:entity:17",
            projected[2].SlotId);
        AreEqual("vanilla:42", projected[2].Key);
        IsTrue(projected[2].RequiresAcknowledgement);

        candidates[2].IsActive = false;
        candidates[2].IsGoneUnacknowledged = true;
        var gone = PanelSlotProjection.Project(slots, candidates);
        AreEqual("vanilla:42", gone[2].Key);
        IsTrue(gone[2].IsGoneUnacknowledged);

        candidates[2].IsGoneUnacknowledged = false;
        var normal = PanelSlotProjection.Project(slots, candidates);
        AreEqual(
            "vanilla:NotEnoughWorkers:entity:17",
            normal[2].Key);
        AreEqual("NICHT GENUG ARBEITER", normal[2].Name);
        IsFalse(normal[2].IsLatched);

        var legacy = new AlarmView
        {
            Key = "vanilla:99",
            OverrideId = "vanilla:NotEnoughWorkers",
            OccurrenceId = "legacy-occurrence",
            Source = "vanilla",
        };
        AreEqual(
            "vanilla:NotEnoughWorkers",
            PanelSlotProjection.StableAlarmId(legacy));
        legacy.OverrideId = "";
        AreEqual(
            "legacy-occurrence",
            PanelSlotProjection.StableAlarmId(legacy));
        legacy.OccurrenceId = "";
        AreEqual("vanilla:99", PanelSlotProjection.StableAlarmId(legacy));

        var entity18 = PanelSlotProjection.CreateSlot(new AlarmView
        {
            Key = "vanilla:43",
            SlotId = "vanilla:NotEnoughWorkers:entity:18",
            OverrideId = "vanilla:NotEnoughWorkers",
            Name = "Nicht genug Arbeiter",
            Source = "vanilla",
        });
        AreEqual(
            "vanilla:NotEnoughWorkers:entity:18",
            entity18.AlarmId);
        IsFalse(string.Equals(
            entity18.AlarmId,
            slots[2].AlarmId,
            StringComparison.Ordinal));

        var sameNameDifferentId = PanelSlotProjection.Project(
            new[]
            {
                new PanelSlotDefinition
                {
                    AlarmId = "custom:a",
                    DisplayName = "GLEICHER TEXT",
                },
                new PanelSlotDefinition
                {
                    AlarmId = "custom:b",
                    DisplayName = "GLEICHER TEXT",
                },
            },
            Array.Empty<AlarmView>());
        AreEqual(2, sameNameDifferentId.Count);
        AreEqual("custom:a", sameNameDifferentId[0].SlotId);
        AreEqual("custom:b", sameNameDifferentId[1].SlotId);

        var mixedSlot = new[]
        {
            new PanelSlotDefinition
            {
                AlarmId = "vanilla:mixed:entity:1",
                DisplayName = "GEMISCHTER ZUSTAND",
            },
        };
        var mixedCandidates = new[]
        {
            new AlarmView
            {
                Key = "vanilla:old-gone",
                SlotId = "vanilla:mixed:entity:1",
                Name = "ALTES KG",
                IsGoneUnacknowledged = true,
                Sequence = 100,
            },
            new AlarmView
            {
                Key = "vanilla:standing-ack",
                SlotId = "vanilla:mixed:entity:1",
                Name = "AKTUELL STEHEND",
                IsActive = true,
                IsAcknowledged = true,
                Sequence = 101,
            },
        };
        var mixed = PanelSlotProjection.Project(
            mixedSlot,
            mixedCandidates)[0];
        IsTrue(mixed.IsActive);
        IsFalse(mixed.IsGoneUnacknowledged);
        IsFalse(mixed.IsAcknowledged);
        AreEqual("AKTUELL STEHEND", mixed.Name);

        mixedCandidates[0].IsGoneUnacknowledged = false;
        mixed = PanelSlotProjection.Project(mixedSlot, mixedCandidates)[0];
        IsTrue(mixed.IsActive);
        IsTrue(mixed.IsAcknowledged);

        mixedCandidates[0].IsActive = true;
        mixedCandidates[0].Sequence = 102;
        mixedCandidates[0].Name = "NEUESTES KOMMT";
        mixed = PanelSlotProjection.Project(mixedSlot, mixedCandidates)[0];
        IsTrue(mixed.IsActive);
        IsFalse(mixed.IsAcknowledged);
        AreEqual("NEUESTES KOMMT", mixed.Name);

        var legacyOne = PanelSlotProjection.LegacyVanillaSlotId(
            "vanilla:NotEnoughWorkers",
            "NotEnoughWorkers · Büro II");
        var legacyTwo = PanelSlotProjection.LegacyVanillaSlotId(
            "vanilla:NotEnoughWorkers",
            "NotEnoughWorkers · Büro III");
        AreEqual(
            legacyOne,
            PanelSlotProjection.LegacyVanillaSlotId(
                "vanilla:NotEnoughWorkers",
                "NotEnoughWorkers · Büro II"));
        IsFalse(string.Equals(legacyOne, legacyTwo, StringComparison.Ordinal));
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            legacyOne,
            "vanilla:NotEnoughWorkers"));

        var placementPanel = new PanelDefinition
        {
            Id = "placement",
            Slots = new List<PanelSlotDefinition>
            {
                new()
                {
                    AlarmId = "system:food",
                    DisplayName = "NAHRUNG",
                },
                new()
                {
                    AlarmId = "system:workers",
                    DisplayName = "ARBEITER",
                },
            },
        };
        var linkedRule = new AlarmRuleDefinition
        {
            Id = " linked-rule ",
            PanelId = "placement",
            Name = "LAGER UND BAND",
            Severity = AlarmSeverity.Critical,
            ActiveColor = "#123456",
            Conditions = new List<ConditionDefinition>
            {
                new(),
                new(),
            },
        };
        IsTrue(PanelSlotProjection.InsertRuleSlot(
            placementPanel,
            linkedRule,
            1));
        AreEqual(3, placementPanel.Slots.Count);
        AreEqual("system:food", placementPanel.Slots[0].AlarmId);
        AreEqual("rule:linked-rule", placementPanel.Slots[1].AlarmId);
        AreEqual("system:workers", placementPanel.Slots[2].AlarmId);
        AreEqual("LAGER UND BAND", placementPanel.Slots[1].DisplayName);
        AreEqual("2 Bedingung(en)", placementPanel.Slots[1].Detail);
        AreEqual("custom", placementPanel.Slots[1].Source);
        AreEqual(AlarmSeverity.Critical, placementPanel.Slots[1].Severity);
        AreEqual("#123456", placementPanel.Slots[1].ActiveColor);
        IsFalse(PanelSlotProjection.InsertRuleSlot(
            placementPanel,
            linkedRule,
            0));
        AreEqual(3, placementPanel.Slots.Count);

        IsTrue(PanelSlotProjection.TryGetCustomRuleId(
            new AlarmView
            {
                Key = "custom-occurrence:entity:77",
                SlotId = "rule:linked-rule",
                Source = "custom",
            },
            out var linkedRuleId));
        AreEqual("linked-rule", linkedRuleId);
        IsFalse(PanelSlotProjection.TryGetCustomRuleId(
            new AlarmView
            {
                Key = "system:food",
                SlotId = "system:food",
                Source = "system",
            },
            out _));

        IsFalse(PanelSlotProjection.TryGetCustomRuleId(
            new AlarmView
            {
                Key = "rule:",
                Source = "custom",
            },
            out _));
        IsFalse(PanelSlotProjection.TryGetCustomRuleId(
            new AlarmView
            {
                SlotId = "rule:   ",
                Source = "custom",
            },
            out _));
        IsFalse(PanelSlotProjection.TryGetCustomRuleId(
            new AlarmView
            {
                SlotId = "rule:linked-rule",
                Source = "system",
            },
            out _));

        var boundaryPanel = new PanelDefinition
        {
            Id = "boundaries",
            Slots = new List<PanelSlotDefinition>
            {
                new()
                {
                    AlarmId = "system:health",
                    DisplayName = "GESUNDHEIT",
                },
            },
        };
        IsTrue(PanelSlotProjection.InsertRuleSlot(
            boundaryPanel,
            new AlarmRuleDefinition
            {
                Id = "at-start",
                Name = "ANFANG",
            },
            -100));
        IsTrue(PanelSlotProjection.InsertRuleSlot(
            boundaryPanel,
            new AlarmRuleDefinition
            {
                Id = "at-end",
                Name = "ENDE",
            },
            int.MaxValue));
        AreEqual("rule:at-start", boundaryPanel.Slots[0].AlarmId);
        AreEqual("system:health", boundaryPanel.Slots[1].AlarmId);
        AreEqual("rule:at-end", boundaryPanel.Slots[2].AlarmId);

        var positionedConfiguration = UnmaConfiguration.CreateDefault();
        positionedConfiguration.Panels.Clear();
        positionedConfiguration.Panels.Add(placementPanel);
        positionedConfiguration.Rules.Clear();
        positionedConfiguration.Rules.Add(linkedRule);
        positionedConfiguration.Normalize();
        AreEqual("linked-rule", positionedConfiguration.Rules[0].Id);
        AreEqual("rule:linked-rule", placementPanel.Slots[1].AlarmId);

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, positionedConfiguration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();
        AreEqual("rule:linked-rule", restored.Panels[0].Slots[1].AlarmId);
    }

    private static void TestConfigurationRoundTrip()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.Panels[0].Slots.Add(new PanelSlotDefinition
        {
            AlarmId = "vanilla:LowFoodSupply",
            DisplayName = "GERINGE LEBENSMITTELVERSORGUNG",
            Detail = "LowFoodSupply",
            Source = "vanilla",
            Severity = AlarmSeverity.Warning,
            ActiveColor = "#ABCDEF",
        });
        configuration.Panels[0].ExcludedAlarmIds.Add("vanilla:Hidden");
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
                    ValueMode = ConditionValueMode.PercentOfReference,
                    ReferenceMetricPath = "$transport.capacity",
                    ReferenceMetricLabel = "Transportkapazität",
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
            SlotId = "vanilla:test:entity:17",
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
        AreEqual(9, restored.SchemaVersion);
        AreEqual(
            ConditionValueMode.PercentOfReference,
            restored.Rules[0].Conditions[1].ValueMode);
        AreEqual(
            "$transport.capacity",
            restored.Rules[0].Conditions[1].ReferenceMetricPath);
        AreEqual(
            "Transportkapazität",
            restored.Rules[0].Conditions[1].ReferenceMetricLabel);
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
        AreEqual("vanilla:test:entity:17", restoredMemory.SlotId);
        AreEqual(5, restored.Panels[0].Slots.Count);
        AreEqual("system:health", restored.Panels[0].Slots[0].AlarmId);
        AreEqual("system:food", restored.Panels[0].Slots[1].AlarmId);
        AreEqual("system:workers", restored.Panels[0].Slots[2].AlarmId);
        AreEqual(
            "vanilla:LowFoodSupply",
            restored.Panels[0].Slots[3].AlarmId);
        AreEqual(
            "rule:" + restored.Rules[0].Id,
            restored.Panels[0].Slots[4].AlarmId);
        AreEqual(
            "vanilla:Hidden",
            restored.Panels[0].ExcludedAlarmIds[0]);
    }

    private static void TestAlarmHistoryState()
    {
        var incomingAcknowledged = new AlarmHistoryDefinition();
        AreEqual("K", incomingAcknowledged.StateCode);
        IsFalse(incomingAcknowledged.CanDelete);
        IsTrue(incomingAcknowledged.SetState(false, true));
        AreEqual("KQ", incomingAcknowledged.StateCode);
        IsFalse(incomingAcknowledged.CanDelete);
        IsFalse(incomingAcknowledged.SetState(false, false));
        AreEqual("KQ", incomingAcknowledged.StateCode);

        var goneAcknowledged = new AlarmHistoryDefinition();
        AreEqual("K", goneAcknowledged.StateCode);
        IsTrue(goneAcknowledged.SetState(true, false));
        AreEqual("KG", goneAcknowledged.StateCode);
        IsFalse(goneAcknowledged.CanDelete);
        IsTrue(goneAcknowledged.SetState(true, true));
        AreEqual("KGQ", goneAcknowledged.StateCode);
        IsTrue(goneAcknowledged.CanDelete);
        IsFalse(goneAcknowledged.SetState(true, false));
        AreEqual("KGQ", goneAcknowledged.StateCode);

        var acknowledgedThenGone = new AlarmHistoryDefinition();
        IsTrue(acknowledgedThenGone.SetState(false, true));
        AreEqual("KQ", acknowledgedThenGone.StateCode);
        IsTrue(acknowledgedThenGone.SetState(true, false));
        AreEqual("KGQ", acknowledgedThenGone.StateCode);
        IsTrue(acknowledgedThenGone.CanDelete);
    }

    private static void TestAlarmHistoryRoundTrip()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 91,
            AlarmKey = "system:food",
            Message = "NAHRUNGSVORRAT KRITISCH",
            Detail = "Nahrung 0 Monate",
            Source = "system",
            PanelId = "supply",
            Severity = AlarmSeverity.Emergency,
            IsGone = true,
            IsAcknowledged = true,
        });

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();

        AreEqual(9, restored.SchemaVersion);
        AreEqual(1, restored.AlarmHistory.Count);
        var history = restored.AlarmHistory[0];
        AreEqual(91L, history.Sequence);
        AreEqual("system:food", history.AlarmKey);
        AreEqual("NAHRUNGSVORRAT KRITISCH", history.Message);
        AreEqual("Nahrung 0 Monate", history.Detail);
        AreEqual("system", history.Source);
        AreEqual("supply", history.PanelId);
        AreEqual(AlarmSeverity.Emergency, history.Severity);
        AreEqual("KGQ", history.StateCode);
        IsTrue(history.CanDelete);
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

        AreEqual(9, oldConfiguration.SchemaVersion);
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

        var schemaSeven = UnmaConfiguration.CreateDefault();
        schemaSeven.SchemaVersion = 7;
        foreach (var panel in schemaSeven.Panels)
        {
            panel.Slots.Clear();
        }
        schemaSeven.Rules.Add(new AlarmRuleDefinition
        {
            Id = "fixed-rule",
            PanelId = "main",
            Name = "FESTE EIGENE MELDUNG",
        });
        schemaSeven.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "vanilla:77",
            OverrideId = "vanilla:NotEnoughWorkers",
            Name = "NICHT GENUG ARBEITER",
            Detail = "NotEnoughWorkers · Kapitänsbüro II",
            Source = "vanilla",
            Severity = AlarmSeverity.Critical,
            IsGoneUnacknowledged = true,
            Sequence = 77,
        });
        schemaSeven.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "vanilla:78",
            OverrideId = "vanilla:NotEnoughWorkers",
            Name = "NICHT GENUG ARBEITER",
            Detail = "NotEnoughWorkers · Kapitänsbüro III",
            Source = "vanilla",
            Severity = AlarmSeverity.Critical,
            IsGoneUnacknowledged = true,
            Sequence = 78,
        });
        schemaSeven.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 79,
            AlarmKey = "vanilla:79",
            Message = "NICHT GENUG ARBEITER",
            Detail = "NotEnoughWorkers · Kapitänsbüro IV",
            Source = "vanilla",
            Severity = AlarmSeverity.Critical,
            IsGone = true,
            IsAcknowledged = true,
        });
        schemaSeven.Normalize();
        AreEqual(9, schemaSeven.SchemaVersion);
        AreEqual(7, schemaSeven.Panels[0].Slots.Count);
        AreEqual("system:health", schemaSeven.Panels[0].Slots[0].AlarmId);
        AreEqual("system:food", schemaSeven.Panels[0].Slots[1].AlarmId);
        AreEqual("system:workers", schemaSeven.Panels[0].Slots[2].AlarmId);
        AreEqual("rule:fixed-rule", schemaSeven.Panels[0].Slots[3].AlarmId);
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            schemaSeven.Panels[0].Slots[4].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            schemaSeven.Panels[0].Slots[5].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            schemaSeven.Panels[0].Slots[6].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsFalse(string.Equals(
            schemaSeven.AlarmMemories[0].SlotId,
            schemaSeven.AlarmMemories[1].SlotId,
            StringComparison.Ordinal));
        schemaSeven.Panels[0].Slots.Add(new PanelSlotDefinition
        {
            AlarmId = "system:food",
            DisplayName = "DUPLIKAT",
        });
        schemaSeven.Normalize();
        AreEqual(7, schemaSeven.Panels[0].Slots.Count);
        AreEqual("system:food", schemaSeven.Panels[0].Slots[1].AlarmId);
        var fixedLastSlot = schemaSeven.Panels[0].Slots[6];
        schemaSeven.Panels[0].Slots.RemoveAt(6);
        schemaSeven.Panels[0].Slots.Insert(0, fixedLastSlot);
        schemaSeven.Normalize();
        AreEqual(fixedLastSlot.AlarmId, schemaSeven.Panels[0].Slots[0].AlarmId);
        schemaSeven.Panels[0].ExcludedAlarmIds.Add("system:health");
        schemaSeven.Panels[0].Slots.RemoveAll(slot =>
            slot.AlarmId == "system:health");
        schemaSeven.Normalize();
        IsFalse(schemaSeven.Panels[0].Slots.Exists(slot =>
            slot.AlarmId == "system:health"));
        schemaSeven.Panels[0].ExcludedAlarmIds.Clear();
        schemaSeven.Normalize();
        AreEqual(
            "system:health",
            schemaSeven.Panels[0].Slots[^1].AlarmId);
        var movingRule = schemaSeven.Rules.Find(rule =>
            rule.Id == "fixed-rule");
        movingRule.PanelId = "supply";
        movingRule.Name = "VERSCHOBENE EIGENE MELDUNG";
        movingRule.Conditions.Add(new ConditionDefinition());
        schemaSeven.Normalize();
        IsFalse(schemaSeven.Panels[0].Slots.Exists(slot =>
            slot.AlarmId == "rule:fixed-rule"));
        var movedSlot = schemaSeven.Panels[1].Slots.Find(slot =>
            slot.AlarmId == "rule:fixed-rule");
        IsTrue(movedSlot != null);
        AreEqual("VERSCHOBENE EIGENE MELDUNG", movedSlot.DisplayName);
        AreEqual("1 Bedingung(en)", movedSlot.Detail);
        schemaSeven.Rules.Remove(movingRule);
        schemaSeven.Normalize();
        IsFalse(schemaSeven.Panels[1].Slots.Exists(slot =>
            slot.AlarmId == "rule:fixed-rule"));

        var legacyJson =
            "{\"SchemaVersion\":4," +
            "\"Panels\":[{\"Id\":\"main\",\"Name\":\"ALT\"," +
            "\"Columns\":3}]," +
            "\"Rules\":[{\"Id\":\"legacy\",\"PanelId\":\"main\"," +
            "\"Name\":\"ALTE MELDUNG\",\"Conditions\":[{" +
            "\"EntityId\":42,\"MetricPath\":\" value \"}]}]," +
            "\"SoundOverrides\":[{\"AlarmId\":\"vanilla:test\"," +
            "\"SoundId\":\"horn\"}]}";
        using var legacyStream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(legacyJson));
        var legacy = (UnmaConfiguration)new DataContractJsonSerializer(
            typeof(UnmaConfiguration)).ReadObject(legacyStream);
        legacy.Normalize();
        AreEqual(9, legacy.SchemaVersion);
        AreEqual(
            ConditionValueMode.Absolute,
            legacy.Rules[0].Conditions[0].ValueMode);
        AreEqual("value", legacy.Rules[0].Conditions[0].MetricPath);
        AreEqual("", legacy.Rules[0].Conditions[0].ReferenceMetricPath);
        AreEqual("", legacy.Rules[0].Conditions[0].ReferenceMetricLabel);
        IsFalse(legacy.Rules[0].AutoAcknowledgeOnClear);
        IsFalse(legacy.SoundOverrides[0].AutoAcknowledgeOnClear);
        IsTrue(legacy.SystemAlarms.TrueForAll(alarm =>
            !alarm.AutoAcknowledgeOnClear));
        AreEqual(0, legacy.AlarmMemories.Count);
        IsTrue(legacy.Panels[0].Slots != null);
        IsTrue(legacy.Panels[0].ExcludedAlarmIds != null);
        AreEqual(1, legacy.Panels[0].Slots.Count);
        AreEqual("rule:legacy", legacy.Panels[0].Slots[0].AlarmId);

        var versionFive = UnmaConfiguration.CreateDefault();
        versionFive.SchemaVersion = 5;
        versionFive.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "migration:k",
            Name = "KOMMEN",
            IsActive = true,
            Sequence = 101,
        });
        versionFive.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "migration:kq",
            Name = "KOMMEN QUITTIERT",
            IsActive = true,
            IsAcknowledged = true,
            Sequence = 102,
        });
        versionFive.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "migration:kg",
            Name = "KOMMEN GEGANGEN",
            IsGoneUnacknowledged = true,
            Sequence = 103,
        });

        versionFive.Normalize();

        AreEqual(9, versionFive.SchemaVersion);
        AreEqual(3, versionFive.AlarmHistory.Count);
        AreEqual(
            "K",
            versionFive.AlarmHistory.Find(item =>
                item.AlarmKey == "migration:k").StateCode);
        AreEqual(
            "KQ",
            versionFive.AlarmHistory.Find(item =>
                item.AlarmKey == "migration:kq").StateCode);
        AreEqual(
            "KG",
            versionFive.AlarmHistory.Find(item =>
                item.AlarmKey == "migration:kg").StateCode);

        versionFive.Normalize();
        AreEqual(3, versionFive.AlarmHistory.Count);
        AreEqual(1, versionFive.AlarmHistory.FindAll(item =>
            item.AlarmKey == "migration:k").Count);
        AreEqual(1, versionFive.AlarmHistory.FindAll(item =>
            item.AlarmKey == "migration:kq").Count);
        AreEqual(1, versionFive.AlarmHistory.FindAll(item =>
            item.AlarmKey == "migration:kg").Count);

        var schemaEight = UnmaConfiguration.CreateDefault();
        schemaEight.SchemaVersion = 8;
        schemaEight.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "vanilla:10",
            Name = "3 OBDACHLOSE VERLIESSEN DIE INSEL",
            Detail = "HomelessLeft",
            Source = "vanilla",
            OverrideId = "vanilla:HomelessLeft",
            OccurrenceId = "vanilla:HomelessLeft",
            SlotId = "vanilla:HomelessLeft",
            Severity = AlarmSeverity.Critical,
            IsActive = true,
            IsAcknowledged = true,
            Sequence = 201,
        });
        schemaEight.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "vanilla:11",
            Name = "1 OBDACHLOSER VERLIESS DIE INSEL",
            Detail = "HomelessLeft",
            Source = "vanilla",
            OverrideId = "vanilla:HomelessLeft",
            OccurrenceId = "vanilla:HomelessLeft",
            SlotId = "vanilla:HomelessLeft",
            Severity = AlarmSeverity.Critical,
            IsActive = true,
            IsAcknowledged = true,
            Sequence = 202,
        });
        schemaEight.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 201,
            AlarmKey = "vanilla:10",
            Message = "3 OBDACHLOSE VERLIESSEN DIE INSEL",
            Detail = "HomelessLeft",
            Source = "vanilla",
            Severity = AlarmSeverity.Critical,
            IsAcknowledged = true,
        });
        schemaEight.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 202,
            AlarmKey = "vanilla:11",
            Message = "1 OBDACHLOSER VERLIESS DIE INSEL",
            Detail = "HomelessLeft",
            Source = "vanilla",
            Severity = AlarmSeverity.Critical,
            IsAcknowledged = true,
        });

        schemaEight.Normalize();

        AreEqual(9, schemaEight.SchemaVersion);
        IsTrue(schemaEight.LegacySustainedAlarmReconciliationPending);
        AreEqual(1, schemaEight.AlarmMemories.Count);
        var sustainedMemory = schemaEight.AlarmMemories[0];
        AreEqual(
            "vanilla:sustained:HomelessLeft",
            sustainedMemory.Key);
        IsTrue(sustainedMemory.IsActive);
        IsTrue(sustainedMemory.IsAcknowledged);
        AreEqual("vanilla:HomelessLeft", sustainedMemory.SlotId);
        AreEqual(2, schemaEight.AlarmHistory.Count);
        AreEqual(1, schemaEight.AlarmHistory.FindAll(item =>
            !item.IsGone).Count);
        AreEqual(
            "vanilla:sustained:HomelessLeft",
            schemaEight.AlarmHistory.Find(item =>
                !item.IsGone).AlarmKey);

        schemaEight.Normalize();
        AreEqual(1, schemaEight.AlarmMemories.Count);
        AreEqual(1, schemaEight.AlarmHistory.FindAll(item =>
            !item.IsGone).Count);

        var historyOnlySchemaEight = UnmaConfiguration.CreateDefault();
        historyOnlySchemaEight.SchemaVersion = 8;
        historyOnlySchemaEight.AlarmHistory.Add(
            new AlarmHistoryDefinition
            {
                Sequence = 301,
                AlarmKey = "vanilla:12",
                Message = "1 OBDACHLOSER VERLIESS DIE INSEL",
                Detail = "HomelessLeft",
                Source = "vanilla",
                Severity = AlarmSeverity.Critical,
                IsGone = true,
                IsAcknowledged = true,
            });
        historyOnlySchemaEight.Normalize();
        AreEqual(0, historyOnlySchemaEight.AlarmMemories.Count);
        IsTrue(historyOnlySchemaEight
            .LegacySustainedAlarmReconciliationPending);

        using var migratedStream = new MemoryStream();
        new DataContractJsonSerializer(typeof(UnmaConfiguration))
            .WriteObject(migratedStream, historyOnlySchemaEight);
        migratedStream.Position = 0;
        var persistedSchemaNine = (UnmaConfiguration)
            new DataContractJsonSerializer(typeof(UnmaConfiguration))
                .ReadObject(migratedStream);
        persistedSchemaNine.Normalize();
        IsTrue(persistedSchemaNine
            .LegacySustainedAlarmReconciliationPending);
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
