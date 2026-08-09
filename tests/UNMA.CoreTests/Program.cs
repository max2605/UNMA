using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using UNMA.Api;
using UNMA.Audio;
using UNMA.Domain;
using UNMA.Extensions;
using UNMA.Runtime;
using UNMA.Ui;

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
        TestVanillaNotificationSuppressionPolicy();
        TestAlarmHistoryState();
        TestSystemAlarmSelection();
        TestSystemMetricMath();
        TestGlobalRuleMetricPaths();
        TestWindowResizeMath();
        TestPanelTopologyPolicy();
        TestEntityVanillaSlotPolicy();
        TestCustomRuleLifecyclePolicy();
        TestPanelSlotProjection();
        TestConfigurationRoundTrip();
        TestAlarmHistoryRoundTrip();
        TestConfigurationMigration();
        TestMechanicalSiren();
        TestExternalRegistryValidationAndSnapshots();
        TestExternalMetricPrecedenceAndIsolation();
        TestExternalAlarmTemplateNormalization();
        TestExternalPushedStateLifecycle();
        TestExternalDefinitionLoader();
        Console.WriteLine(
            $"UNMA core tests passed: {s_assertions} assertions.");
    }

    private static void TestGlobalRuleMetricPaths()
    {
        var path = SystemMetricCatalog.ToRulePath("population.total");
        AreEqual("$global:population.total", path);
        IsTrue(SystemMetricCatalog.TryParseRulePath(path, out var metricId));
        AreEqual("population.total", metricId);
        IsFalse(SystemMetricCatalog.TryParseRulePath(
            "$stored.quantity",
            out _));
        IsFalse(SystemMetricCatalog.TryParseRulePath("$global: ", out _));
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
            199d,
            ConditionValueMode.PercentOfReference,
            400d,
            out var potatoesBelowHalf));
        AreEqual(49.75d, potatoesBelowHalf);
        IsTrue(AlarmEvaluation.Compare(
            potatoesBelowHalf,
            ComparisonOperator.Less,
            50d));
        IsTrue(AlarmEvaluation.TryCalculateComparable(
            200d,
            ConditionValueMode.PercentOfReference,
            400d,
            out var potatoesAtHalf));
        AreEqual(50d, potatoesAtHalf);
        IsFalse(AlarmEvaluation.Compare(
            potatoesAtHalf,
            ComparisonOperator.Less,
            50d));

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

    private static void TestWindowResizeMath()
    {
        AreEqual(946f, WindowResizeMath.GetHandleOrigin(980f, 30f, 4f));
        AreEqual(686f, WindowResizeMath.GetHandleOrigin(720f, 30f, 4f));
        IsTrue(WindowResizeMath.IsInsideHandle(
            980f, 720f, 961f, 701f, 30f, 4f));
        IsTrue(WindowResizeMath.IsInsideHandle(
            980f, 720f, 946f, 686f, 30f, 4f));
        IsFalse(WindowResizeMath.IsInsideHandle(
            980f, 720f, 490f, 360f, 30f, 4f));
        IsFalse(WindowResizeMath.IsInsideHandle(
            980f, 720f, 945f, 701f, 30f, 4f));
        IsFalse(WindowResizeMath.IsInsideHandle(
            980f, 720f, 976f, 701f, 30f, 4f));
        IsFalse(WindowResizeMath.IsInsideHandle(
            980f, 720f, 961f, 685f, 30f, 4f));
        IsFalse(WindowResizeMath.IsInsideHandle(
            980f, 720f, 961f, 716f, 30f, 4f));

        AreEqual(1080f, WindowResizeMath.ResizeExtent(
            980f, 100f, 700f, 1908f));
        AreEqual(770f, WindowResizeMath.ResizeExtent(
            720f, 50f, 520f, 1068f));
        AreEqual(700f, WindowResizeMath.ResizeExtent(
            980f, -1000f, 700f, 1908f));
        AreEqual(1068f, WindowResizeMath.ResizeExtent(
            720f, 1000f, 520f, 1068f));
        AreEqual(1908f, WindowResizeMath.ResizeExtent(
            980f, 1000f, 700f, 1908f));
        AreEqual(700f, WindowResizeMath.ResizeExtent(
            980f, 100f, 700f, 600f));
    }

    private static void TestPanelTopologyPolicy()
    {
        var dashboard = new PanelDefinition
        {
            Id = "dashboard",
            Name = "HOME",
            IsDashboard = true,
        };
        var globalOne = new PanelDefinition
        {
            Id = "global-one",
            Name = "VERSORGUNG",
        };
        var globalTwo = new PanelDefinition
        {
            Id = "global-two",
            Name = "PRODUKTION",
        };
        var entityPanel = new PanelDefinition
        {
            Id = "entity-42",
            Name = "LAGERHAUS #42",
            OwnerEntityId = 42,
            OwnerEntityTitle = " Lagerhaus III ",
            OwnerEntityPrototypeId = " AirStorageT3 ",
            OwnerEntityType = " Mafi.Core.Buildings.Storage ",
        };
        var foreignEntityPanel = new PanelDefinition
        {
            Id = "entity-43",
            Name = "LAGERHAUS #43",
            OwnerEntityId = 43,
            OwnerEntityTitle = "Lagerhaus II",
            OwnerEntityPrototypeId = "AirStorageT2",
        };
        var panels = new[]
        {
            dashboard,
            globalOne,
            globalTwo,
            entityPanel,
            foreignEntityPanel,
        };
        var rule = new AlarmRuleDefinition
        {
            Id = "linked-storage",
            PanelId = "entity-42",
            Name = "KARTOFFELN NIEDRIG",
            LinkedPanelIds = new List<string>
            {
                " global-one ",
                "global-one",
                "entity-42",
                "entity-43",
                "dashboard",
                "missing",
                "global-two",
                " ",
            },
        };

        rule.LinkedPanelIds = PanelTopologyPolicy.NormalizeLinkedPanelIds(
            rule.PanelId,
            rule.LinkedPanelIds,
            panels);
        AreEqual(2, rule.LinkedPanelIds.Count);
        AreEqual("global-one", rule.LinkedPanelIds[0]);
        AreEqual("global-two", rule.LinkedPanelIds[1]);
        rule.LinkedPanelIds.Add("entity-43");
        var assignedPanelIds = PanelTopologyPolicy.GetRulePanelIds(
            rule,
            panels);
        AreEqual(3, assignedPanelIds.Count);
        AreEqual("entity-42", assignedPanelIds[0]);
        AreEqual("global-one", assignedPanelIds[1]);
        AreEqual("global-two", assignedPanelIds[2]);
        IsTrue(PanelTopologyPolicy.IsEntityPanel(entityPanel));
        IsFalse(PanelTopologyPolicy.IsEntityPanel(globalOne));
        IsTrue(PanelTopologyPolicy.IsRuleAssignedToPanel(
            rule,
            globalOne,
            panels));
        IsFalse(PanelTopologyPolicy.IsRuleAssignedToPanel(
            rule,
            dashboard,
            panels));
        IsTrue(PanelTopologyPolicy.TryGetRuleId(
            " rule:linked-storage ",
            out var ruleId));
        AreEqual("linked-storage", ruleId);
        IsFalse(PanelTopologyPolicy.TryGetRuleId("rule:   ", out _));

        var memory = new AlarmMemoryDefinition
        {
            Key = "rule:linked-storage",
            SlotId = "rule:linked-storage",
            Source = "custom",
            PanelId = "entity-42",
        };
        IsTrue(PanelTopologyPolicy.IsCustomMemoryEligibleForPanel(
            memory,
            entityPanel,
            new[] { rule },
            panels));
        IsTrue(PanelTopologyPolicy.IsCustomMemoryEligibleForPanel(
            memory,
            globalOne,
            new[] { rule },
            panels));
        IsFalse(PanelTopologyPolicy.IsCustomMemoryEligibleForPanel(
            memory,
            dashboard,
            new[] { rule },
            panels));
        var legacyMemory = new AlarmMemoryDefinition
        {
            Source = "custom",
            PanelId = "global-two",
        };
        IsTrue(PanelTopologyPolicy.IsCustomMemoryEligibleForPanel(
            legacyMemory,
            globalTwo,
            Array.Empty<AlarmRuleDefinition>(),
            panels));

        var configuration = new UnmaConfiguration
        {
            Panels = panels.ToList(),
            Rules = new List<AlarmRuleDefinition> { rule },
            UiScalePercent = 175,
            EditorWindowX = 210f,
            EditorWindowY = 125f,
            EditorWindowWidth = 1200f,
            EditorWindowHeight = 780f,
        };
        configuration.Normalize();
        AreEqual(13, configuration.SchemaVersion);
        AreEqual("Lagerhaus III", entityPanel.OwnerEntityTitle);
        AreEqual("AirStorageT3", entityPanel.OwnerEntityPrototypeId);
        AreEqual(
            "Mafi.Core.Buildings.Storage",
            entityPanel.OwnerEntityType);
        foreach (var panel in new[] { entityPanel, globalOne, globalTwo })
        {
            IsTrue(panel.Slots.Exists(slot =>
                slot.AlarmId == "rule:linked-storage"));
        }
        IsFalse(dashboard.Slots.Exists(slot =>
            slot.AlarmId == "rule:linked-storage"));
        IsFalse(foreignEntityPanel.Slots.Exists(slot =>
            slot.AlarmId == "rule:linked-storage"));

        rule.LinkedPanelIds = new List<string> { "global-two" };
        rule.Name = "KARTOFFELN SEHR NIEDRIG";
        rule.Conditions.Add(new ConditionDefinition());
        configuration.Normalize();
        IsTrue(entityPanel.Slots.Exists(slot =>
            slot.AlarmId == "rule:linked-storage"));
        IsFalse(globalOne.Slots.Exists(slot =>
            slot.AlarmId == "rule:linked-storage"));
        var linkedSlot = globalTwo.Slots.Find(slot =>
            slot.AlarmId == "rule:linked-storage");
        IsTrue(linkedSlot != null);
        AreEqual("KARTOFFELN SEHR NIEDRIG", linkedSlot.DisplayName);
        AreEqual("1 Bedingung(en)", linkedSlot.Detail);

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();
        AreEqual(175, restored.UiScalePercent);
        AreEqual(210f, restored.EditorWindowX);
        AreEqual(125f, restored.EditorWindowY);
        AreEqual(1200f, restored.EditorWindowWidth);
        AreEqual(780f, restored.EditorWindowHeight);
        var restoredEntityPanel = restored.Panels.Find(panel =>
            panel.Id == "entity-42");
        AreEqual(42, restoredEntityPanel.OwnerEntityId);
        AreEqual("Lagerhaus III", restoredEntityPanel.OwnerEntityTitle);
        AreEqual(
            "AirStorageT3",
            restoredEntityPanel.OwnerEntityPrototypeId);
        AreEqual(
            "Mafi.Core.Buildings.Storage",
            restoredEntityPanel.OwnerEntityType);
        AreEqual(1, restored.Rules[0].LinkedPanelIds.Count);
        AreEqual("global-two", restored.Rules[0].LinkedPanelIds[0]);
    }

    private static void TestEntityVanillaSlotPolicy()
    {
        var source = new PanelSlotDefinition
        {
            AlarmId = "vanilla:TruckCannotDeliver:entity:17",
            DisplayName = "Truck cannot deliver",
            Detail = "TruckCannotDeliver · Truck 17",
            Source = "vanilla",
            Severity = AlarmSeverity.Warning,
            ActiveColor = "#AA3300",
        };
        var panel = new PanelDefinition
        {
            Id = "entity-42",
            OwnerEntityId = 42,
            OwnerEntityTitle = "Truck 42",
            OwnerEntityPrototypeId = "TruckT2",
        };

        IsTrue(EntityVanillaSlotPolicy.Synchronize(
            panel,
            new[] { source, source }));
        AreEqual(1, panel.Slots.Count);
        AreEqual(
            "vanilla:TruckCannotDeliver:entity:42",
            panel.Slots[0].AlarmId);
        AreEqual(
            "TruckCannotDeliver · Truck 42",
            panel.Slots[0].Detail);
        AreEqual("#AA3300", panel.Slots[0].ActiveColor);
        IsTrue(EntityVanillaSlotPolicy.IsForEntity(panel.Slots[0], 42));
        IsFalse(EntityVanillaSlotPolicy.IsForEntity(panel.Slots[0], 17));
        IsFalse(EntityVanillaSlotPolicy.Synchronize(panel, new[] { source }));

        var globalPanel = new PanelDefinition { OwnerEntityId = -1 };
        IsFalse(EntityVanillaSlotPolicy.Synchronize(
            globalPanel,
            new[] { source }));
        AreEqual(0, globalPanel.Slots.Count);
    }

    private static void TestCustomRuleLifecyclePolicy()
    {
        IsTrue(CustomRuleLifecyclePolicy.ShouldDeleteForRemovedEntity(
            removedEntityIsDestroyed: true,
            hasLiveReplacement: false));
        IsFalse(CustomRuleLifecyclePolicy.ShouldDeleteForRemovedEntity(
            removedEntityIsDestroyed: false,
            hasLiveReplacement: false));
        IsFalse(CustomRuleLifecyclePolicy.ShouldDeleteForRemovedEntity(
            removedEntityIsDestroyed: true,
            hasLiveReplacement: true));
        IsFalse(CustomRuleLifecyclePolicy.ShouldDeleteForRemovedEntity(
            removedEntityIsDestroyed: false,
            hasLiveReplacement: true));
        AreEqual(
            10d,
            CustomRuleLifecyclePolicy.StaticEntityMissingGracePeriod
                .TotalSeconds);
        const long timestampFrequency = 1000;
        var graceTicks = (long)
            (CustomRuleLifecyclePolicy.StaticEntityMissingGracePeriod
                .TotalSeconds * timestampFrequency);
        IsFalse(CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
            firstMissingTimestamp: 1000,
            currentTimestamp: 1000 + graceTicks - 1,
            timestampFrequency));
        IsTrue(CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
            firstMissingTimestamp: 1000,
            currentTimestamp: 1000 + graceTicks,
            timestampFrequency));
        IsFalse(CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
            firstMissingTimestamp: 1000,
            currentTimestamp: 999,
            timestampFrequency));
        IsFalse(CustomRuleLifecyclePolicy.IsConfirmedMissingStaticEntity(
            firstMissingTimestamp: 1000,
            currentTimestamp: 1000 + graceTicks,
            timestampFrequency: 0));

        var missingTracker = new StaticEntityMissingGraceTracker();
        IsFalse(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000,
            timestampFrequency));
        IsFalse(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000 + graceTicks - 1,
            timestampFrequency));
        missingTracker.ObserveLive(42);
        IsFalse(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000 + graceTicks + 1,
            timestampFrequency));
        IsFalse(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000 + graceTicks * 2,
            timestampFrequency));
        IsTrue(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000 + graceTicks * 2 + 1,
            timestampFrequency));
        missingTracker.RetainOnly(Array.Empty<int>());
        IsFalse(missingTracker.ObserveMissing(
            entityId: 42,
            currentTimestamp: 5000 + graceTicks * 4,
            timestampFrequency));

        var allRule = new AlarmRuleDefinition
        {
            Id = "all-rule",
            Logic = AlarmLogic.All,
            Conditions = new List<ConditionDefinition>
            {
                new() { EntityId = 1 },
                new() { EntityId = 2 },
            },
        };
        var anyRule = new AlarmRuleDefinition
        {
            Id = "any-rule",
            Logic = AlarmLogic.Any,
            Conditions = new List<ConditionDefinition>
            {
                new() { EntityId = 3 },
                new() { EntityId = 4 },
            },
        };
        var disabledRule = new AlarmRuleDefinition
        {
            Id = "disabled-rule",
            Enabled = false,
            Conditions = new List<ConditionDefinition>
            {
                new() { EntityId = 1 },
            },
        };
        var unrelatedRule = new AlarmRuleDefinition
        {
            Id = "unrelated-rule",
            Conditions = new List<ConditionDefinition>
            {
                new() { EntityId = 99 },
            },
        };
        var malformedRule = new AlarmRuleDefinition
        {
            Id = "malformed-rule",
            Conditions = new List<ConditionDefinition> { null },
        };
        var blankIdRule = new AlarmRuleDefinition
        {
            Id = " ",
            Conditions = new List<ConditionDefinition>
            {
                new() { EntityId = 2 },
            },
        };

        var oneRemoved = CustomRuleLifecyclePolicy
            .FindRulesReferencingEntities(
                new[] { allRule, anyRule, disabledRule, unrelatedRule },
                new[] { 2 });
        AreEqual(1, oneRemoved.Count);
        AreEqual("all-rule", oneRemoved[0]);

        var severalRemoved = CustomRuleLifecyclePolicy
            .FindRulesReferencingEntities(
                new AlarmRuleDefinition[]
                {
                    allRule,
                    anyRule,
                    disabledRule,
                    unrelatedRule,
                    malformedRule,
                    blankIdRule,
                    allRule,
                    null,
                },
                new[] { 1, 2, 4 });
        AreEqual(3, severalRemoved.Count);
        AreEqual("all-rule", severalRemoved[0]);
        AreEqual("any-rule", severalRemoved[1]);
        AreEqual("disabled-rule", severalRemoved[2]);

        AreEqual(0, CustomRuleLifecyclePolicy
            .FindRulesReferencingEntities(
                new[] { allRule },
                Array.Empty<int>())
            .Count);
        AreEqual(0, CustomRuleLifecyclePolicy
            .FindRulesReferencingEntities(
                null,
                new[] { 1 })
            .Count);
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

    private static void TestVanillaNotificationSuppressionPolicy()
    {
        const string overrideId = "vanilla:NoRecipeSelected";
        const string entitySlotId =
            "vanilla:NoRecipeSelected:entity:17";
        const string legacySlotId =
            "vanilla:NoRecipeSelected:legacy:ABCDEF12";

        IsTrue(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId(overrideId));
        IsTrue(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId("  " + overrideId + "  "));
        IsFalse(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId(entitySlotId));
        IsFalse(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId(legacySlotId));
        IsFalse(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId("system:NoRecipeSelected"));
        IsFalse(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId("vanilla:"));
        IsFalse(VanillaNotificationSuppressionPolicy
            .IsVanillaOverrideId(null));

        AreEqual(
            overrideId,
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(overrideId));
        AreEqual(
            overrideId,
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(entitySlotId));
        AreEqual(
            overrideId,
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(legacySlotId));
        AreEqual(
            overrideId,
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId("  " + entitySlotId + "  "));
        AreEqual(
            "",
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(
                    "external:NoRecipeSelected:entity:17"));
        AreEqual(
            "",
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId(
                    "vanilla:NoRecipeSelected:entity:"));
        AreEqual(
            "",
            VanillaNotificationSuppressionPolicy
                .GetOverrideIdForSlotId("vanilla::legacy:ABCDEF12"));

        var disabled = new HashSet<string>(StringComparer.Ordinal)
        {
            overrideId,
        };
        IsTrue(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition { AlarmId = overrideId },
            disabled));
        IsTrue(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition { AlarmId = entitySlotId },
            disabled));
        IsTrue(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition { AlarmId = legacySlotId },
            disabled));
        IsFalse(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition
            {
                AlarmId = "vanilla:NoRecipe:entity:17",
            },
            disabled));
        IsFalse(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition { AlarmId = entitySlotId },
            Array.Empty<string>()));
        IsFalse(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            null,
            disabled));
        IsFalse(VanillaNotificationSuppressionPolicy.IsSlotSuppressed(
            new PanelSlotDefinition { AlarmId = entitySlotId },
            null));

        var rules = new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Hidden,
            },
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Silent,
            },
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.Entity,
                EntityId = 17,
                Behavior = VanillaNotificationBehavior.Normal,
            },
        };
        AreEqual(
            VanillaNotificationBehavior.Normal,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                rules,
                overrideId,
                17,
                "TruckT2"));
        AreEqual(
            VanillaNotificationBehavior.Silent,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                rules,
                overrideId,
                18,
                "TruckT2"));
        AreEqual(
            VanillaNotificationBehavior.Hidden,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                rules,
                overrideId,
                18,
                "TruckT3"));
        AreEqual(
            VanillaNotificationBehavior.Normal,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                rules,
                "vanilla:Other",
                17,
                "TruckT2"));
        var ignoredRules = new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliver",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Ignored,
            },
        };
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                ignoredRules,
                "vanilla:TruckCannotDeliver",
                99,
                "TruckT2"));
        AreEqual(
            VanillaNotificationBehavior.Normal,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                ignoredRules,
                "vanilla:TruckCannotDeliver",
                99,
                "TruckT3"));

        var legacyConfig = UnmaConfiguration.CreateDefault();
        legacyConfig.SchemaVersion = 12;
        legacyConfig.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = overrideId,
            SoundId = "auto",
            IsGloballyDisabled = true,
        });
        legacyConfig.Normalize();
        AreEqual(13, legacyConfig.SchemaVersion);
        IsFalse(legacyConfig.SoundOverrides.Last().IsGloballyDisabled);
        AreEqual(
            VanillaNotificationBehavior.Hidden,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                legacyConfig.VanillaNotificationRules,
                overrideId));
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

        var dashboard = PanelSlotProjection.ProjectActive(new[]
        {
            new AlarmView
            {
                Key = "active-unacknowledged",
                SlotId = "dashboard:a",
                Name = "AKTIV K",
                IsActive = true,
                Severity = AlarmSeverity.Warning,
                Sequence = 10,
            },
            new AlarmView
            {
                Key = "same-slot-acknowledged",
                SlotId = "dashboard:a",
                Name = "AKTIV KQ ZWEITZUSTAND",
                IsActive = true,
                IsAcknowledged = true,
                Severity = AlarmSeverity.Emergency,
                Sequence = 9,
            },
            new AlarmView
            {
                Key = "active-acknowledged",
                SlotId = "dashboard:b",
                Name = "AKTIV KQ",
                IsActive = true,
                IsAcknowledged = true,
                Severity = AlarmSeverity.Critical,
                Sequence = 20,
            },
            new AlarmView
            {
                Key = "gone-unacknowledged",
                SlotId = "dashboard:c",
                Name = "KG",
                IsGoneUnacknowledged = true,
                Sequence = 30,
            },
            new AlarmView
            {
                Key = "gone-acknowledged",
                SlotId = "dashboard:kgq",
                Name = "KGQ",
                IsAcknowledged = true,
                Sequence = 35,
            },
            new AlarmView
            {
                Key = "normal",
                SlotId = "dashboard:d",
                Name = "NORMAL",
                Sequence = 40,
            },
            new AlarmView
            {
                Key = "external:active-without-dashboard-slot",
                Name = "AKTIV OHNE FESTEN HOME-SCHLITZ",
                IsActive = true,
                Severity = AlarmSeverity.Warning,
                Sequence = 50,
            },
        });
        AreEqual(3, dashboard.Count);
        AreEqual("dashboard:a", dashboard[0].SlotId);
        IsTrue(dashboard[0].IsActive);
        IsFalse(dashboard[0].IsAcknowledged);
        AreEqual(AlarmSeverity.Emergency, dashboard[0].Severity);
        AreEqual(
            "external:active-without-dashboard-slot",
            dashboard[1].SlotId);
        IsTrue(dashboard[1].IsActive);
        IsFalse(dashboard[1].IsAcknowledged);
        AreEqual("dashboard:b", dashboard[2].SlotId);
        IsTrue(dashboard[2].IsActive);
        IsTrue(dashboard[2].IsAcknowledged);

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
        var fixedPanel = configuration.Panels.Find(panel =>
            !panel.IsDashboard);
        fixedPanel.Slots.Add(new PanelSlotDefinition
        {
            AlarmId = "vanilla:LowFoodSupply",
            DisplayName = "GERINGE LEBENSMITTELVERSORGUNG",
            Detail = "LowFoodSupply",
            Source = "vanilla",
            Severity = AlarmSeverity.Warning,
            ActiveColor = "#ABCDEF",
        });
        fixedPanel.ExcludedAlarmIds.Add("vanilla:Hidden");
        configuration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "system:health",
            SoundId = "siren",
            AutoAcknowledgeOnClear = true,
        });
        configuration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "vanilla:NoRecipeSelected",
            SoundId = "none",
            IsGloballyDisabled = true,
        });
        configuration.VanillaNotificationRules.Add(
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:CannotFindPath",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Hidden,
            });
        configuration.VanillaNotificationRules.Add(
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliver",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Ignored,
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
            PanelId = fixedPanel.Id,
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
                new()
                {
                    EntityId = 19,
                    EntityTitle = "Lebensmittelmarkt",
                    EntityType =
                        "Mafi.Core.Buildings.Settlements.SettlementFoodModule",
                    EntityPrototypeId = "SettlementFoodModuleT2",
                    MetricPath = "$input.product:Potato",
                    MetricLabel = "Kartoffeln · Bestand",
                    Comparison = ComparisonOperator.Less,
                    Threshold = 50,
                    ValueMode = ConditionValueMode.PercentOfReference,
                    ReferenceMetricPath = "$input.capacity:Potato",
                    ReferenceMetricLabel = "Kartoffeln · Kapazität",
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
            AutoAcknowledgeOnClear = true,
            Sequence = 73,
            EntityId = 17,
            EntityPrototypeId = "TruckT2",
            EntityTitle = "Truck 17",
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
        AreEqual(3, restored.Rules[0].Conditions.Count);
        AreEqual("LAGER UND BAND LEER", restored.Rules[0].Name);
        AreEqual(20d, restored.Rules[0].Conditions[1].Threshold);
        AreEqual(
            "AirStorageT1",
            restored.Rules[0].Conditions[0].EntityPrototypeId);
        AreEqual(2, restored.SoundOverrides.Count);
        var restoredSystemOverride = restored.SoundOverrides.Find(item =>
            item.AlarmId == "system:health");
        var restoredVanillaOverride = restored.SoundOverrides.Find(item =>
            item.AlarmId == "vanilla:NoRecipeSelected");
        AreEqual("siren", restoredSystemOverride.SoundId);
        IsTrue(restoredSystemOverride.AutoAcknowledgeOnClear);
        IsFalse(restoredSystemOverride.IsGloballyDisabled);
        AreEqual("none", restoredVanillaOverride.SoundId);
        IsTrue(restoredVanillaOverride.IsGloballyDisabled);
        AreEqual(13, restored.SchemaVersion);
        AreEqual(2, restored.VanillaNotificationRules.Count);
        AreEqual(
            VanillaNotificationBehavior.Hidden,
            restored.VanillaNotificationRules[0].Behavior);
        AreEqual(
            "TruckT2",
            restored.VanillaNotificationRules[0].EntityPrototypeId);
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            restored.VanillaNotificationRules[1].Behavior);
        IsTrue(restored.Panels[0].IsDashboard);
        IsFalse(restored.Panels[1].IsDashboard);
        AreEqual(
            ConditionValueMode.PercentOfReference,
            restored.Rules[0].Conditions[1].ValueMode);
        AreEqual(
            "$transport.capacity",
            restored.Rules[0].Conditions[1].ReferenceMetricPath);
        AreEqual(
            "Transportkapazität",
            restored.Rules[0].Conditions[1].ReferenceMetricLabel);
        AreEqual(
            "$input.product:Potato",
            restored.Rules[0].Conditions[2].MetricPath);
        AreEqual(
            "$input.capacity:Potato",
            restored.Rules[0].Conditions[2].ReferenceMetricPath);
        AreEqual(50d, restored.Rules[0].Conditions[2].Threshold);
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
        AreEqual(17, restoredMemory.EntityId);
        AreEqual("TruckT2", restoredMemory.EntityPrototypeId);
        AreEqual("Truck 17", restoredMemory.EntityTitle);
        AreEqual("vanilla:42", restoredMemory.Key);
        AreEqual("GEGANGENE MELDUNG", restoredMemory.Name);
        IsFalse(restoredMemory.IsActive);
        IsTrue(restoredMemory.IsGoneUnacknowledged);
        AreEqual("vanilla:test", restoredMemory.OccurrenceId);
        AreEqual(210, restoredMemory.OccurrencePriority);
        AreEqual(73L, restoredMemory.Sequence);
        AreEqual("vanilla:test:entity:17", restoredMemory.SlotId);
        IsTrue(restoredMemory.AutoAcknowledgeOnClear);
        AreEqual(0, restored.Panels[0].Slots.Count);
        AreEqual(5, restored.Panels[1].Slots.Count);
        AreEqual("system:health", restored.Panels[1].Slots[0].AlarmId);
        AreEqual("system:food", restored.Panels[1].Slots[1].AlarmId);
        AreEqual("system:workers", restored.Panels[1].Slots[2].AlarmId);
        AreEqual(
            "vanilla:LowFoodSupply",
            restored.Panels[1].Slots[3].AlarmId);
        AreEqual(
            "rule:" + restored.Rules[0].Id,
            restored.Panels[1].Slots[4].AlarmId);
        AreEqual(
            "vanilla:Hidden",
            restored.Panels[1].ExcludedAlarmIds[0]);
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

        AreEqual(13, restored.SchemaVersion);
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
        oldConfiguration.UiScalePercent = 175;
        oldConfiguration.EditorWindowX = 999f;
        oldConfiguration.EditorWindowY = 888f;
        oldConfiguration.EditorWindowWidth = 777f;
        oldConfiguration.EditorWindowHeight = 666f;
        oldConfiguration.Panels[1].OwnerEntityId = 44;
        oldConfiguration.Panels[1].OwnerEntityTitle = "ALT";
        oldConfiguration.Panels[1].OwnerEntityPrototypeId = "Legacy.Storage";
        oldConfiguration.Panels[1].OwnerEntityType = "Legacy.Type";
        oldConfiguration.SoundOverrides.Add(new AlarmSoundOverride
        {
            AlarmId = "system:health",
            SoundId = "horn",
        });
        oldConfiguration.Normalize();

        AreEqual(13, oldConfiguration.SchemaVersion);
        AreEqual(-1f, oldConfiguration.LauncherX);
        AreEqual(-1f, oldConfiguration.LauncherY);
        AreEqual(100, oldConfiguration.UiScalePercent);
        AreEqual(180f, oldConfiguration.EditorWindowX);
        AreEqual(110f, oldConfiguration.EditorWindowY);
        AreEqual(1080f, oldConfiguration.EditorWindowWidth);
        AreEqual(720f, oldConfiguration.EditorWindowHeight);
        AreEqual(-1, oldConfiguration.Panels[1].OwnerEntityId);
        AreEqual("", oldConfiguration.Panels[1].OwnerEntityTitle);
        AreEqual("", oldConfiguration.Panels[1].OwnerEntityPrototypeId);
        AreEqual("", oldConfiguration.Panels[1].OwnerEntityType);
        AreEqual(3, oldConfiguration.SystemAlarms.Count);

        var malformedCurrent = UnmaConfiguration.CreateDefault();
        malformedCurrent.UiScalePercent = 250;
        malformedCurrent.EditorWindowX = float.NaN;
        malformedCurrent.EditorWindowY = float.PositiveInfinity;
        malformedCurrent.EditorWindowWidth = 100f;
        malformedCurrent.EditorWindowHeight = 200f;
        malformedCurrent.Normalize();
        AreEqual(200, malformedCurrent.UiScalePercent);
        AreEqual(180f, malformedCurrent.EditorWindowX);
        AreEqual(110f, malformedCurrent.EditorWindowY);
        AreEqual(700f, malformedCurrent.EditorWindowWidth);
        AreEqual(520f, malformedCurrent.EditorWindowHeight);
        malformedCurrent.UiScalePercent = 50;
        malformedCurrent.Normalize();
        AreEqual(75, malformedCurrent.UiScalePercent);

        var health = oldConfiguration.SystemAlarms.Find(
            alarm => alarm.Id == "system:health");
        IsTrue(health.Stages.TrueForAll(stage => stage.SoundId == "horn"));
        var warning = health.Stages.Find(stage => stage.Id == "warning");
        warning.Conditions[0].Threshold = 7;
        health.Stages.RemoveAll(stage => stage.Id == "critical");
        oldConfiguration.Normalize();
        AreEqual(7d, warning.Conditions[0].Threshold);
        IsTrue(health.Stages.Exists(stage => stage.Id == "critical"));

        var schemaNine = UnmaConfiguration.CreateDefault();
        schemaNine.SchemaVersion = 9;
        schemaNine.Panels[0].Slots.Add(new PanelSlotDefinition
        {
            AlarmId = "legacy:dashboard-slot",
            DisplayName = "ALTE HOME-POSITION",
        });
        schemaNine.Normalize();
        AreEqual(1, schemaNine.Panels.Count(panel => panel.IsDashboard));
        IsTrue(schemaNine.Panels[0].IsDashboard);
        IsTrue(schemaNine.Panels[0].Slots.Exists(slot =>
            slot.AlarmId == "legacy:dashboard-slot"));

        schemaNine.Panels[1].IsDashboard = true;
        schemaNine.Normalize();
        AreEqual(1, schemaNine.Panels.Count(panel => panel.IsDashboard));
        IsTrue(schemaNine.Panels[0].IsDashboard);
        IsFalse(schemaNine.Panels[1].IsDashboard);

        var schemaSeven = UnmaConfiguration.CreateDefault();
        schemaSeven.SchemaVersion = 7;
        foreach (var panel in schemaSeven.Panels)
        {
            panel.Slots.Clear();
        }
        schemaSeven.Panels.Add(new PanelDefinition
        {
            Id = "secondary",
            Name = "ZWEITE FACHTAFEL",
            Columns = 3,
        });
        schemaSeven.Rules.Add(new AlarmRuleDefinition
        {
            Id = "fixed-rule",
            PanelId = "supply",
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
        var migratedDashboard = schemaSeven.Panels.Find(panel =>
            panel.IsDashboard);
        var migratedFixedPanel = schemaSeven.Panels.Find(panel =>
            panel.Id == "supply");
        AreEqual(13, schemaSeven.SchemaVersion);
        AreEqual(0, migratedDashboard.Slots.Count);
        AreEqual(7, migratedFixedPanel.Slots.Count);
        AreEqual("system:health", migratedFixedPanel.Slots[0].AlarmId);
        AreEqual("system:food", migratedFixedPanel.Slots[1].AlarmId);
        AreEqual("system:workers", migratedFixedPanel.Slots[2].AlarmId);
        AreEqual("rule:fixed-rule", migratedFixedPanel.Slots[3].AlarmId);
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            migratedFixedPanel.Slots[4].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            migratedFixedPanel.Slots[5].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsTrue(PanelSlotProjection.IsLegacyVanillaSlotId(
            migratedFixedPanel.Slots[6].AlarmId,
            "vanilla:NotEnoughWorkers"));
        IsFalse(string.Equals(
            schemaSeven.AlarmMemories[0].SlotId,
            schemaSeven.AlarmMemories[1].SlotId,
            StringComparison.Ordinal));
        migratedFixedPanel.Slots.Add(new PanelSlotDefinition
        {
            AlarmId = "system:food",
            DisplayName = "DUPLIKAT",
        });
        schemaSeven.Normalize();
        AreEqual(7, migratedFixedPanel.Slots.Count);
        AreEqual("system:food", migratedFixedPanel.Slots[1].AlarmId);
        var fixedLastSlot = migratedFixedPanel.Slots[6];
        migratedFixedPanel.Slots.RemoveAt(6);
        migratedFixedPanel.Slots.Insert(0, fixedLastSlot);
        schemaSeven.Normalize();
        AreEqual(fixedLastSlot.AlarmId, migratedFixedPanel.Slots[0].AlarmId);
        migratedFixedPanel.ExcludedAlarmIds.Add("system:health");
        migratedFixedPanel.Slots.RemoveAll(slot =>
            slot.AlarmId == "system:health");
        schemaSeven.Normalize();
        IsFalse(migratedFixedPanel.Slots.Exists(slot =>
            slot.AlarmId == "system:health"));
        migratedFixedPanel.ExcludedAlarmIds.Clear();
        schemaSeven.Normalize();
        AreEqual(
            "system:health",
            migratedFixedPanel.Slots[^1].AlarmId);
        var movingRule = schemaSeven.Rules.Find(rule =>
            rule.Id == "fixed-rule");
        movingRule.PanelId = "secondary";
        movingRule.Name = "VERSCHOBENE EIGENE MELDUNG";
        movingRule.Conditions.Add(new ConditionDefinition());
        schemaSeven.Normalize();
        IsFalse(migratedFixedPanel.Slots.Exists(slot =>
            slot.AlarmId == "rule:fixed-rule"));
        var secondaryPanel = schemaSeven.Panels.Find(panel =>
            panel.Id == "secondary");
        var movedSlot = secondaryPanel.Slots.Find(slot =>
            slot.AlarmId == "rule:fixed-rule");
        IsTrue(movedSlot != null);
        AreEqual("VERSCHOBENE EIGENE MELDUNG", movedSlot.DisplayName);
        AreEqual("1 Bedingung(en)", movedSlot.Detail);
        schemaSeven.Rules.Remove(movingRule);
        schemaSeven.Normalize();
        IsFalse(secondaryPanel.Slots.Exists(slot =>
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
        AreEqual(13, legacy.SchemaVersion);
        AreEqual(
            ConditionValueMode.Absolute,
            legacy.Rules[0].Conditions[0].ValueMode);
        AreEqual("value", legacy.Rules[0].Conditions[0].MetricPath);
        AreEqual("", legacy.Rules[0].Conditions[0].ReferenceMetricPath);
        AreEqual("", legacy.Rules[0].Conditions[0].ReferenceMetricLabel);
        IsFalse(legacy.Rules[0].AutoAcknowledgeOnClear);
        AreEqual(0, legacy.Rules[0].LinkedPanelIds.Count);
        IsFalse(legacy.SoundOverrides[0].AutoAcknowledgeOnClear);
        IsFalse(legacy.SoundOverrides[0].IsGloballyDisabled);
        IsTrue(legacy.SystemAlarms.TrueForAll(alarm =>
            !alarm.AutoAcknowledgeOnClear));
        AreEqual(0, legacy.AlarmMemories.Count);
        IsTrue(legacy.Panels[0].Slots != null);
        IsTrue(legacy.Panels[0].ExcludedAlarmIds != null);
        IsTrue(legacy.Panels[0].IsDashboard);
        AreEqual(-1, legacy.Panels[0].OwnerEntityId);
        AreEqual(100, legacy.UiScalePercent);
        AreEqual(180f, legacy.EditorWindowX);
        AreEqual(110f, legacy.EditorWindowY);
        AreEqual(1080f, legacy.EditorWindowWidth);
        AreEqual(720f, legacy.EditorWindowHeight);
        AreEqual(0, legacy.Panels[0].Slots.Count);

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

        AreEqual(13, versionFive.SchemaVersion);
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

        AreEqual(13, schemaEight.SchemaVersion);
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

    private static void TestExternalRegistryValidationAndSnapshots()
    {
        const string owner = "RegistryProvider";
        UnmaApi.UnregisterOwner(owner);
        try
        {
            var revisionBefore = UnmaApi.GetSnapshot().Revision;
            var candidate = new ExternalMetricDefinition
            {
                Id = " level ",
                PrototypeId = " ",
                LabelKey = "multilanglib.RegistryProvider.metric.level",
                LabelFallback = " Level ",
                Unit = " items ",
                SuggestedReferenceMetric = " capacity ",
                Reader = _ => 12.5d,
            };

            IsFalse(UnmaApi.TryRegisterMetric(
                "invalid owner",
                candidate,
                out var invalidOwnerError));
            IsTrue(invalidOwnerError.Length > 0);
            IsFalse(UnmaApi.TryRegisterMetric(
                "ÜnicodeProvider",
                candidate,
                out var unicodeOwnerError));
            IsTrue(unicodeOwnerError.Length > 0);
            IsFalse(UnmaApi.TryRegisterMetric(
                ".hidden-provider",
                candidate,
                out var leadingPunctuationError));
            IsTrue(leadingPunctuationError.Length > 0);
            IsFalse(UnmaApi.TryRegisterMetric(
                owner,
                new ExternalMetricDefinition { Id = "missing-reader" },
                out var missingReaderError));
            IsTrue(missingReaderError.Length > 0);

            IsTrue(UnmaApi.TryRegisterMetric(
                " RegistryProvider ",
                candidate,
                out var registrationError));
            AreEqual("", registrationError);
            var afterMetric = UnmaApi.GetSnapshot();
            IsTrue(afterMetric.Revision > revisionBefore);
            var registeredMetric = afterMetric.Metrics.Single(item =>
                item.OwnerModId == owner && item.Id == "level");
            AreEqual("*", registeredMetric.PrototypeId);
            AreEqual("Level", registeredMetric.LabelFallback);
            AreEqual("items", registeredMetric.Unit);
            AreEqual("capacity", registeredMetric.SuggestedReferenceMetric);

            var duplicateRevision = afterMetric.Revision;
            IsFalse(UnmaApi.TryRegisterMetric(
                owner,
                candidate,
                out var duplicateError));
            IsTrue(duplicateError.Contains("already registered"));
            AreEqual(duplicateRevision, UnmaApi.GetSnapshot().Revision);

            var templateDefinition = CreateValidExternalTemplate(
                "frozen-template");
            IsTrue(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                templateDefinition,
                out var templateError));
            AreEqual("", templateError);
            IsFalse(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                templateDefinition,
                out var duplicateTemplateError));
            IsTrue(duplicateTemplateError.Contains("already registered"));

            var heldSnapshot = UnmaApi.GetSnapshot();
            var heldMetric = heldSnapshot.Metrics.Single(item =>
                item.OwnerModId == owner && item.Id == "level");
            var heldTemplate = heldSnapshot.AlarmTemplates.Single(item =>
                item.OwnerModId == owner &&
                item.Id == "frozen-template");

            candidate.Id = "mutated";
            candidate.LabelFallback = "mutated";
            templateDefinition.PrototypeIds[0] = "Mutated.Prototype";
            templateDefinition.Conditions[0].Metric = "mutated.metric";
            templateDefinition.MessageFallback = "mutated";

            AreEqual("level", heldMetric.Id);
            AreEqual("Level", heldMetric.LabelFallback);
            AreEqual("Provider.Storage", heldTemplate.PrototypeIds[0]);
            AreEqual("$stored.amount", heldTemplate.Conditions[0].Metric);
            AreEqual("External test alarm", heldTemplate.MessageFallback);
            Throws<NotSupportedException>(() =>
                ((IList<ExternalMetricSnapshot>)heldSnapshot.Metrics).Clear());
            Throws<NotSupportedException>(() =>
                ((IList<string>)heldTemplate.PrototypeIds).Clear());

            IsTrue(UnmaApi.UnregisterOwner(owner));
            var current = UnmaApi.GetSnapshot();
            AreEqual(0, current.Metrics.Count(item =>
                item.OwnerModId == owner));
            AreEqual(0, current.AlarmTemplates.Count(item =>
                item.OwnerModId == owner));
            AreEqual(1, heldSnapshot.Metrics.Count(item =>
                item.OwnerModId == owner));
            AreEqual(1, heldSnapshot.AlarmTemplates.Count(item =>
                item.OwnerModId == owner));
        }
        finally
        {
            UnmaApi.UnregisterOwner(owner);
        }
    }

    private static void TestExternalMetricPrecedenceAndIsolation()
    {
        const string owner = "MetricProvider";
        UnmaApi.UnregisterOwner(owner);
        try
        {
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "load",
                    PrototypeId = "*",
                    Reader = _ => 10d,
                }));
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "load",
                    PrototypeId = "Provider.Exact",
                    Reader = _ => 20d,
                }));
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "throws",
                    PrototypeId = "*",
                    Reader = _ => 33d,
                }));
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "throws",
                    PrototypeId = "Provider.Exact",
                    Reader = _ => throw new InvalidOperationException(
                        "Provider callback failure"),
                }));
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "not-finite",
                    PrototypeId = "*",
                    Reader = _ => 44d,
                }));
            IsTrue(UnmaApi.RegisterMetric(
                owner,
                new ExternalMetricDefinition
                {
                    Id = "not-finite",
                    PrototypeId = "Provider.Exact",
                    Reader = _ => double.NaN,
                }));

            var snapshot = UnmaApi.GetSnapshot();
            IsTrue(snapshot.TryReadMetric(
                owner,
                "Provider.Exact",
                "load",
                new object(),
                out var exactValue));
            AreEqual(20d, exactValue);
            IsTrue(snapshot.TryReadMetric(
                owner,
                "Provider.Other",
                "load",
                new object(),
                out var wildcardValue));
            AreEqual(10d, wildcardValue);

            IsTrue(snapshot.TryReadMetric(
                owner,
                "Provider.Exact",
                "throws",
                new object(),
                out var exceptionFallback));
            AreEqual(33d, exceptionFallback);
            IsTrue(snapshot.TryReadMetric(
                owner,
                "Provider.Exact",
                "not-finite",
                new object(),
                out var nanFallback));
            AreEqual(44d, nanFallback);

            var throwingReader = snapshot.Metrics.Single(item =>
                item.OwnerModId == owner && item.Id == "throws" &&
                item.PrototypeId == "Provider.Exact");
            IsFalse(throwingReader.TryRead(new object(), out _));
            var nanReader = snapshot.Metrics.Single(item =>
                item.OwnerModId == owner && item.Id == "not-finite" &&
                item.PrototypeId == "Provider.Exact");
            IsFalse(nanReader.TryRead(new object(), out _));
            IsFalse(nanReader.TryRead(null, out _));
            IsFalse(snapshot.TryReadMetric(
                owner,
                "Provider.Exact",
                "missing",
                new object(),
                out _));
        }
        finally
        {
            UnmaApi.UnregisterOwner(owner);
        }
    }

    private static void TestExternalAlarmTemplateNormalization()
    {
        const string owner = "NormalizeProvider";
        UnmaApi.UnregisterOwner(owner);
        try
        {
            var definition = new ExternalAlarmTemplateDefinition
            {
                Id = " storage-low ",
                PrototypeIds = new List<string>
                {
                    " Provider.Storage ",
                    "Provider.Vehicle",
                },
                Scope = " PER-ENTITY ",
                PanelId = " ",
                LocalizationNamespace = " NormalizeProvider ",
                MessageKey =
                    " multilanglib.NormalizeProvider.alarm.storage_low ",
                MessageFallback = " Storage low ",
                DetailFallback = " Remaining stock ",
                Severity = " INFO ",
                SoundId = " custom.ogg ",
                ActiveColor = "#a1b2c3",
                AutoAcknowledgeOnClear = true,
                Logic = " AND ",
                Conditions = new List<ExternalAlarmConditionDefinition>
                {
                    new()
                    {
                        Metric = " fill ",
                        Operator = " less-or-equal ",
                        Threshold = 25d,
                        ValueMode = " % ",
                        ReferenceMetric = " capacity ",
                        LabelKey =
                            "multilanglib.NormalizeProvider.metric.fill",
                        ReferenceLabelKey =
                            "multilanglib.NormalizeProvider.metric.capacity",
                    },
                },
            };

            IsTrue(UnmaApi.TryRegisterAlarmTemplate(
                " NormalizeProvider ",
                definition,
                out var error));
            AreEqual("", error);
            var normalized = UnmaApi.GetSnapshot().AlarmTemplates.Single(
                item => item.OwnerModId == owner &&
                        item.Id == "storage-low");
            AreEqual("per_entity", normalized.Scope);
            AreEqual("main", normalized.PanelId);
            AreEqual(owner, normalized.LocalizationNamespace);
            AreEqual(
                "multilanglib.NormalizeProvider.alarm.storage_low",
                normalized.MessageKey);
            AreEqual("Storage low", normalized.MessageFallback);
            AreEqual("Remaining stock", normalized.DetailFallback);
            AreEqual("notice", normalized.Severity);
            AreEqual("custom.ogg", normalized.SoundId);
            AreEqual("#A1B2C3", normalized.ActiveColor);
            IsTrue(normalized.AutoAcknowledgeOnClear);
            AreEqual("all", normalized.Logic);
            AreEqual("Provider.Storage", normalized.PrototypeIds[0]);
            AreEqual("<=", normalized.Conditions[0].Operator);
            AreEqual("percent_of_reference",
                normalized.Conditions[0].ValueMode);
            AreEqual("fill", normalized.Conditions[0].Metric);
            AreEqual("capacity",
                normalized.Conditions[0].ReferenceMetric);

            definition.PrototypeIds[0] = "Mutated.Prototype";
            definition.Conditions[0].Metric = "mutated";
            AreEqual("Provider.Storage", normalized.PrototypeIds[0]);
            AreEqual("fill", normalized.Conditions[0].Metric);

            var duplicatePrototype = CreateValidExternalTemplate(
                "duplicate-prototype");
            duplicatePrototype.PrototypeIds.Add("Provider.Storage");
            IsFalse(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                duplicatePrototype,
                out var duplicatePrototypeError));
            IsTrue(duplicatePrototypeError.Contains("Duplicate prototype"));

            var tooManyPrototypes = CreateValidExternalTemplate(
                "too-many-prototypes");
            tooManyPrototypes.PrototypeIds = Enumerable.Range(
                    0,
                    ExternalDefinitionLoader.MaxPrototypeIdsPerAlarm + 1)
                .Select(index => "Provider.Prototype" + index)
                .ToList();
            IsFalse(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                tooManyPrototypes,
                out var prototypeLimitError));
            IsTrue(prototypeLimitError.Contains("at most"));

            var tooManyConditions = CreateValidExternalTemplate(
                "too-many-conditions");
            tooManyConditions.Conditions = Enumerable.Range(
                    0,
                    ExternalDefinitionLoader.MaxConditionsPerAlarm + 1)
                .Select(_ => new ExternalAlarmConditionDefinition
                {
                    Metric = "$stored.amount",
                    Operator = "<",
                    Threshold = 1d,
                })
                .ToList();
            IsFalse(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                tooManyConditions,
                out var conditionLimitError));
            IsTrue(conditionLimitError.Contains("at most"));

            var missingReference = CreateValidExternalTemplate(
                "missing-reference");
            missingReference.Conditions[0].ValueMode = "%";
            IsFalse(UnmaApi.TryRegisterAlarmTemplate(
                owner,
                missingReference,
                out var missingReferenceError));
            IsTrue(missingReferenceError.Contains("reference_metric"));
        }
        finally
        {
            UnmaApi.UnregisterOwner(owner);
        }
    }

    private static void TestExternalPushedStateLifecycle()
    {
        const string owner = "StateProvider";
        UnmaApi.UnregisterOwner(owner);
        try
        {
            var invalidUtf16 = new string((char)0xD800, 1);
            IsFalse(UnmaApi.TryPublishAlarmState(
                owner,
                new ExternalAlarmState
                {
                    Id = invalidUtf16,
                    Active = true,
                    MessageFallback = "Invalid identifier",
                },
                out var invalidIdError));
            IsTrue(invalidIdError.Contains("UTF-16"));
            IsFalse(UnmaApi.TryPublishAlarmState(
                owner,
                new ExternalAlarmState
                {
                    Id = "invalid-entity-key",
                    EntityKey = invalidUtf16,
                    Active = true,
                    MessageFallback = "Invalid entity key",
                },
                out var invalidEntityKeyError));
            IsTrue(invalidEntityKeyError.Contains("UTF-16"));
            IsFalse(UnmaApi.TryPublishAlarmStates(
                "invalid owner",
                Array.Empty<ExternalAlarmState>(),
                out var invalidBatchOwnerError));
            IsTrue(invalidBatchOwnerError.Length > 0);

            var active = new ExternalAlarmState
            {
                Id = " machine-trip ",
                InstanceId = " unit-7 ",
                Active = true,
                PanelId = " ",
                MessageFallback = " Pump trip ",
                Severity = " CRITICAL ",
                ActiveColor = "#f05c41",
                CurrentValue = 7d,
            };
            IsTrue(UnmaApi.TryPublishAlarmState(
                owner,
                active,
                out var publishError));
            AreEqual("", publishError);
            var heldSnapshot = UnmaApi.GetSnapshot();
            var heldState = heldSnapshot.AlarmStates.Single(item =>
                item.OwnerModId == owner);
            IsTrue(heldState.Active);
            AreEqual("machine-trip", heldState.Id);
            AreEqual("unit-7", heldState.InstanceId);
            AreEqual("main", heldState.PanelId);
            AreEqual("Pump trip", heldState.MessageFallback);
            AreEqual("critical", heldState.Severity);
            AreEqual("#F05C41", heldState.ActiveColor);
            AreEqual(7d, heldState.CurrentValue.Value);

            var revisionBeforeIdentical = heldSnapshot.Revision;
            IsTrue(UnmaApi.PublishAlarmState(
                owner,
                new ExternalAlarmState
                {
                    Id = "machine-trip",
                    InstanceId = "unit-7",
                    Active = true,
                    PanelId = "main",
                    MessageFallback = "Pump trip",
                    Severity = "critical",
                    ActiveColor = "#F05C41",
                    CurrentValue = 7d,
                }));
            AreEqual(
                revisionBeforeIdentical,
                UnmaApi.GetSnapshot().Revision);

            var revisionBeforeBatch = UnmaApi.GetSnapshot().Revision;
            IsTrue(UnmaApi.TryPublishAlarmStates(
                owner,
                new[]
                {
                    new ExternalAlarmState
                    {
                        Id = "batch-a",
                        Active = true,
                        MessageFallback = "Batch A",
                    },
                    new ExternalAlarmState
                    {
                        Id = "batch-b",
                        Active = false,
                        MessageFallback = "Batch B",
                    },
                },
                out var batchError));
            AreEqual("", batchError);
            AreEqual(
                revisionBeforeBatch + 1,
                UnmaApi.GetSnapshot().Revision);
            AreEqual(3, UnmaApi.GetSnapshot().AlarmStates.Count(item =>
                item.OwnerModId == owner));
            IsFalse(UnmaApi.TryPublishAlarmStates(
                owner,
                new[]
                {
                    new ExternalAlarmState
                    {
                        Id = "duplicate",
                        MessageFallback = "First",
                    },
                    new ExternalAlarmState
                    {
                        Id = "duplicate",
                        MessageFallback = "Second",
                    },
                },
                out var duplicateBatchError));
            IsTrue(duplicateBatchError.Contains("duplicate"));
            AreEqual(3, UnmaApi.GetSnapshot().AlarmStates.Count(item =>
                item.OwnerModId == owner));

            var revisionBeforeReplacement = heldSnapshot.Revision;
            IsTrue(UnmaApi.PublishAlarmState(
                owner,
                new ExternalAlarmState
                {
                    Id = "machine-trip",
                    InstanceId = "unit-7",
                    Active = false,
                    MessageFallback = "Pump restored",
                    Severity = "warning",
                    CurrentValue = 4.5d,
                }));
            var replacedSnapshot = UnmaApi.GetSnapshot();
            IsTrue(replacedSnapshot.Revision > revisionBeforeReplacement);
            AreEqual(3, replacedSnapshot.AlarmStates.Count(item =>
                item.OwnerModId == owner));
            var replaced = replacedSnapshot.AlarmStates.Single(item =>
                item.OwnerModId == owner &&
                item.Id == "machine-trip" &&
                item.InstanceId == "unit-7");
            IsFalse(replaced.Active);
            AreEqual("Pump restored", replaced.MessageFallback);
            AreEqual(4.5d, replaced.CurrentValue.Value);
            IsTrue(heldState.Active);
            AreEqual("Pump trip", heldState.MessageFallback);

            IsFalse(UnmaApi.TryPublishAlarmState(
                owner,
                new ExternalAlarmState
                {
                    Id = "invalid-value",
                    Active = true,
                    MessageFallback = "Invalid",
                    CurrentValue = double.NaN,
                },
                out var invalidValueError));
            IsTrue(invalidValueError.Contains("finite"));
            AreEqual(3, UnmaApi.GetSnapshot().AlarmStates.Count(item =>
                item.OwnerModId == owner));

            IsTrue(UnmaApi.RemoveAlarmState(
                " StateProvider ",
                " machine-trip ",
                " unit-7 "));
            IsFalse(UnmaApi.RemoveAlarmState(
                owner,
                "machine-trip",
                "unit-7"));
            AreEqual(2, UnmaApi.GetSnapshot().AlarmStates.Count(item =>
                item.OwnerModId == owner));
            AreEqual(1, heldSnapshot.AlarmStates.Count(item =>
                item.OwnerModId == owner));
        }
        finally
        {
            UnmaApi.UnregisterOwner(owner);
        }
    }

    private static void TestExternalDefinitionLoader()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "UNMA-CoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var validProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderValid");
            WriteExternalDefinition(
                validProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderValid",
                    ExternalDefinitionLoader.SchemaVersion,
                    BuildExternalAlarmJson("tank-low", 1, 1)));
            var valid = ExternalDefinitionLoader.Load(
                new[] { validProvider });
            AreEqual(1, valid.ProviderCount);
            AreEqual(1, valid.ScannedFileCount);
            AreEqual(1, valid.LoadedFileCount);
            IsFalse(valid.HasErrors);
            AreEqual(0, valid.Diagnostics.Count);
            AreEqual(1, valid.AlarmTemplates.Count);
            var loadedAlarm = valid.AlarmTemplates[0];
            AreEqual("LoaderValid", loadedAlarm.OwnerModId);
            AreEqual("tank-low", loadedAlarm.Id);
            AreEqual("Provider.Entity0", loadedAlarm.PrototypeIds[0]);
            AreEqual("External alarm", loadedAlarm.MessageFallback);
            AreEqual("metric0", loadedAlarm.Conditions[0].Metric);

            var mismatchProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderMismatch");
            WriteExternalDefinition(
                mismatchProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "DifferentProvider",
                    ExternalDefinitionLoader.SchemaVersion,
                    BuildExternalAlarmJson("mismatch", 1, 1)));
            var mismatch = ExternalDefinitionLoader.Load(
                new[] { mismatchProvider });
            AreEqual(0, mismatch.LoadedFileCount);
            AreEqual(0, mismatch.AlarmTemplates.Count);
            IsTrue(mismatch.HasErrors);
            IsTrue(HasDiagnostic(
                mismatch,
                "file.provider_mismatch"));

            var schemaProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderSchema");
            WriteExternalDefinition(
                schemaProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderSchema",
                    ExternalDefinitionLoader.SchemaVersion + 1,
                    BuildExternalAlarmJson("schema", 1, 1)));
            var invalidSchema = ExternalDefinitionLoader.Load(
                new[] { schemaProvider });
            AreEqual(0, invalidSchema.LoadedFileCount);
            AreEqual(0, invalidSchema.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(
                invalidSchema,
                "file.unsupported_schema"));

            var duplicateProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderDuplicate");
            var duplicateAlarm = BuildExternalAlarmJson(
                "duplicate",
                1,
                1);
            WriteExternalDefinition(
                duplicateProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderDuplicate",
                    ExternalDefinitionLoader.SchemaVersion,
                    duplicateAlarm,
                    duplicateAlarm));
            var duplicate = ExternalDefinitionLoader.Load(
                new[] { duplicateProvider });
            AreEqual(1, duplicate.LoadedFileCount);
            AreEqual(1, duplicate.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(duplicate, "alarm.duplicate"));

            var largeProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderLarge");
            WriteExternalDefinition(
                largeProvider,
                "large.json",
                new string(
                    'x',
                    checked((int)
                        ExternalDefinitionLoader.MaxFileSizeBytes + 1)));
            var tooLarge = ExternalDefinitionLoader.Load(
                new[] { largeProvider });
            AreEqual(0, tooLarge.LoadedFileCount);
            AreEqual(0, tooLarge.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(tooLarge, "file.too_large"));

            var conditionProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderConditions");
            WriteExternalDefinition(
                conditionProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderConditions",
                    ExternalDefinitionLoader.SchemaVersion,
                    BuildExternalAlarmJson(
                        "condition-limit",
                        1,
                        ExternalDefinitionLoader.MaxConditionsPerAlarm + 1)));
            var conditionLimit = ExternalDefinitionLoader.Load(
                new[] { conditionProvider });
            AreEqual(1, conditionLimit.LoadedFileCount);
            AreEqual(0, conditionLimit.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(conditionLimit, "alarm.invalid"));
            IsTrue(conditionLimit.Diagnostics.Any(item =>
                item.Message.Contains("conditions")));

            var prototypeProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderPrototypes");
            WriteExternalDefinition(
                prototypeProvider,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderPrototypes",
                    ExternalDefinitionLoader.SchemaVersion,
                    BuildExternalAlarmJson(
                        "prototype-limit",
                        ExternalDefinitionLoader.MaxPrototypeIdsPerAlarm + 1,
                        1)));
            var prototypeLimit = ExternalDefinitionLoader.Load(
                new[] { prototypeProvider });
            AreEqual(1, prototypeLimit.LoadedFileCount);
            AreEqual(0, prototypeLimit.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(prototypeLimit, "alarm.invalid"));
            IsTrue(prototypeLimit.Diagnostics.Any(item =>
                item.Message.Contains("prototype ids")));

            var missingRequiredProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderRequired");
            WriteExternalDefinition(
                missingRequiredProvider,
                "alarms.json",
                "{\"schema_version\":1,\"mod_id\":\"LoaderRequired\"}");
            var missingRequired = ExternalDefinitionLoader.Load(
                new[] { missingRequiredProvider });
            AreEqual(0, missingRequired.LoadedFileCount);
            IsTrue(HasDiagnostic(
                missingRequired,
                "file.invalid_json"));

            var nullAlarmsProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderNullAlarms");
            WriteExternalDefinition(
                nullAlarmsProvider,
                "alarms.json",
                "{\"schema_version\":1,\"mod_id\":\"LoaderNullAlarms\"," +
                "\"alarms\":null}");
            var nullAlarms = ExternalDefinitionLoader.Load(
                new[] { nullAlarmsProvider });
            AreEqual(0, nullAlarms.LoadedFileCount);
            AreEqual(0, nullAlarms.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(
                nullAlarms,
                "file.alarms_required"));

            var providerLimitProvider = CreateExternalProvider(
                temporaryRoot,
                "LoaderProviderLimit");
            var providerLimitAlarms = Enumerable.Range(
                    0,
                    ExternalDefinitionLoader.MaxAlarmsPerProvider)
                .Select(index => BuildExternalAlarmJson(
                    "alarm-" + index,
                    1,
                    1))
                .ToArray();
            WriteExternalDefinition(
                providerLimitProvider,
                "a.json",
                BuildExternalFileJson(
                    "LoaderProviderLimit",
                    ExternalDefinitionLoader.SchemaVersion,
                    providerLimitAlarms));
            WriteExternalDefinition(
                providerLimitProvider,
                "b.json",
                BuildExternalFileJson(
                    "LoaderProviderLimit",
                    ExternalDefinitionLoader.SchemaVersion,
                    BuildExternalAlarmJson("overflow", 1, 1)));
            var providerLimit = ExternalDefinitionLoader.Load(
                new[] { providerLimitProvider });
            AreEqual(
                ExternalDefinitionLoader.MaxAlarmsPerProvider,
                providerLimit.AlarmTemplates.Count);
            AreEqual(1, providerLimit.ScannedFileCount);
            IsTrue(HasDiagnostic(
                providerLimit,
                "provider.alarm_limit"));

            var protectedProvider = CreateExternalProvider(
                temporaryRoot,
                "UNMA");
            var aliasAttacker = CreateExternalProvider(
                temporaryRoot,
                "LoaderAliasAttacker");
            var hijackingAlarm = BuildExternalAlarmJson(
                    "hijack",
                    1,
                    1)
                .Replace(
                    "\"message_fallback\"",
                    "\"localization_namespace\":\"UNMA\"," +
                    "\"message_fallback\"");
            WriteExternalDefinition(
                aliasAttacker,
                "alarms.json",
                BuildExternalFileJson(
                    "LoaderAliasAttacker",
                    ExternalDefinitionLoader.SchemaVersion,
                    hijackingAlarm));
            var hijack = ExternalDefinitionLoader.Load(
                new[] { protectedProvider, aliasAttacker });
            AreEqual(0, hijack.AlarmTemplates.Count);
            IsTrue(HasDiagnostic(
                hijack,
                "alarm.localization_namespace_conflict"));
        }
        finally
        {
            DeleteTemporaryTestDirectory(temporaryRoot);
        }

        IsFalse(Directory.Exists(temporaryRoot));
    }

    private static ExternalProviderDescriptor CreateExternalProvider(
        string temporaryRoot,
        string providerId)
    {
        var providerRoot = Path.Combine(temporaryRoot, providerId);
        Directory.CreateDirectory(Path.Combine(providerRoot, "UNMA"));
        return new ExternalProviderDescriptor(providerId, providerRoot);
    }

    private static void WriteExternalDefinition(
        ExternalProviderDescriptor provider,
        string fileName,
        string contents)
    {
        File.WriteAllText(
            Path.Combine(provider.RootDirectoryPath, "UNMA", fileName),
            contents);
    }

    private static string BuildExternalFileJson(
        string providerId,
        int schemaVersion,
        params string[] alarms)
    {
        return "{\"schema_version\":" + schemaVersion +
               ",\"mod_id\":\"" + providerId +
               "\",\"alarms\":[" + string.Join(",", alarms) + "]}";
    }

    private static string BuildExternalAlarmJson(
        string alarmId,
        int prototypeCount,
        int conditionCount)
    {
        var prototypes = string.Join(",", Enumerable.Range(
                0,
                prototypeCount)
            .Select(index => "\"Provider.Entity" + index + "\""));
        var conditions = string.Join(",", Enumerable.Range(
                0,
                conditionCount)
            .Select(index =>
                "{\"metric\":\"metric" + index +
                "\",\"operator\":\"<\",\"threshold\":5}"));
        return "{\"id\":\"" + alarmId +
               "\",\"prototype_ids\":[" + prototypes +
               "],\"scope\":\"aggregate\"," +
               "\"message_fallback\":\"External alarm\"," +
               "\"severity\":\"warning\",\"conditions\":[" +
               conditions + "]}";
    }

    private static bool HasDiagnostic(
        ExternalDefinitionLoadResult result,
        string code)
    {
        return result.Diagnostics.Any(item => item.Code == code);
    }

    private static void DeleteTemporaryTestDirectory(string directory)
    {
        var resolved = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(
                temporaryRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith(
                "UNMA-CoreTests-",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to delete an unexpected test directory: " +
                resolved);
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static ExternalAlarmTemplateDefinition
        CreateValidExternalTemplate(string id)
    {
        return new ExternalAlarmTemplateDefinition
        {
            Id = id,
            PrototypeIds = new List<string> { "Provider.Storage" },
            Scope = "aggregate",
            PanelId = "main",
            MessageFallback = "External test alarm",
            DetailFallback = "External test detail",
            Severity = "warning",
            SoundId = "auto",
            ActiveColor = "auto",
            Logic = "all",
            Conditions = new List<ExternalAlarmConditionDefinition>
            {
                new()
                {
                    Metric = "$stored.amount",
                    Operator = "<",
                    Threshold = 5d,
                },
            },
        };
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

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        s_assertions++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            "Expected exception " + typeof(TException).Name + ".");
    }
}
