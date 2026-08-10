using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        TestInstrumentValuePolicy();
        TestBooleanLogic();
        TestAlarmLatch();
        TestAlarmTimingPolicy();
        TestAlarmEscalationPolicy();
        TestAlarmAttentionQueuePolicy();
        TestAlarmTimingModelNormalization();
        TestAlarmEscalationModelNormalization();
        TestAlarmTimingMemoryPolicy();
        TestAlarmAudioSnoozePolicy();
        TestSustainedVanillaAlarmPolicy();
        TestVanillaNotificationSuppressionPolicy();
        TestAlarmHistoryState();
        TestAlarmHistoryQueryAndExport();
        TestSystemAlarmSelection();
        TestSystemMetricMath();
        TestGlobalRuleMetricPaths();
        TestWindowResizeMath();
        TestPanelTopologyPolicy();
        TestPanelClonePolicy();
        TestEntityVanillaSlotPolicy();
        TestCustomRuleLifecyclePolicy();
        TestPanelSlotProjection();
        TestConfigurationRoundTrip();
        TestAlarmHistoryRoundTrip();
        TestConfigurationMigration();
        TestMechanicalSiren();
        TestLocalizationCoverage();
        TestExternalRegistryValidationAndSnapshots();
        TestExternalMetricPrecedenceAndIsolation();
        TestExternalAlarmTemplateNormalization();
        TestExternalPushedStateLifecycle();
        TestExternalDefinitionLoader();
        Console.WriteLine(
            $"UNMA core tests passed: {s_assertions} assertions.");
    }

    private static void TestLocalizationCoverage()
    {
        var repositoryRoot = FindRepositoryRoot();
        using (var manifest = JsonDocument.Parse(File.ReadAllText(
                   Path.Combine(repositoryRoot, "manifest.json"))))
        {
            var dependencies = manifest.RootElement
                .GetProperty("mod_dependencies")
                .EnumerateArray()
                .Select(item => item.GetString() ?? "")
                .ToArray();
            IsTrue(dependencies.Any(item => item.StartsWith(
                "MultiLangLib>=",
                StringComparison.Ordinal)));
        }
        var projectFile = File.ReadAllText(
            Path.Combine(repositoryRoot, "source", "UNMA.csproj"));
        IsTrue(Regex.IsMatch(
            projectFile,
            "<Reference Include=\"MultiLangLib\">[\\s\\S]*?" +
            "<Private>false</Private>[\\s\\S]*?</Reference>",
            RegexOptions.CultureInvariant));
        var modSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "source", "UnmaMod.cs"));
        IsTrue(modSource.Contains(
            "UnmaText.Initialize(manifest.RootDirectoryPath)",
            StringComparison.Ordinal));

        var languageDirectory = Path.Combine(repositoryRoot, "lang");
        var languageFiles = Directory.GetFiles(languageDirectory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IsTrue(languageFiles.Length >= 2);

        var languages = languageFiles.ToDictionary(
            Path.GetFileName,
            ReadLanguageFile,
            StringComparer.OrdinalIgnoreCase);
        IsTrue(languages.TryGetValue("en.json", out var english));
        IsTrue(languages.TryGetValue("de.json", out _));

        var canonicalKeys = english.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var keyPattern = new Regex(
            "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
            RegexOptions.CultureInvariant);
        var placeholderPattern = new Regex(
            "\\{([0-9]+)(?:[^}]*)\\}",
            RegexOptions.CultureInvariant);

        foreach (var language in languages)
        {
            var actualKeys = language.Value.Keys
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            AreEqual(
                string.Join("\n", canonicalKeys),
                string.Join("\n", actualKeys));

            foreach (var key in canonicalKeys)
            {
                IsTrue(keyPattern.IsMatch(key));
                IsTrue(language.Value.TryGetValue(key, out var translated));
                IsFalse(string.IsNullOrWhiteSpace(translated));
                AreEqual(
                    ExtractPlaceholderSignature(english[key], placeholderPattern),
                    ExtractPlaceholderSignature(translated, placeholderPattern));
            }
        }

        var sourceRoot = Path.Combine(repositoryRoot, "source");
        var keyUsePattern = new Regex(
            "UnmaText\\.(?:Get|Format)\\(\\s*\"([^\"]+)\"",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceFile in Directory.GetFiles(
                     sourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match match in keyUsePattern.Matches(source))
            {
                usedKeys.Add(match.Groups[1].Value);
            }
        }

        IsTrue(usedKeys.Count > 0);
        foreach (var key in usedKeys)
        {
            IsTrue(english.ContainsKey(key));
        }

        foreach (var metric in SystemMetricCatalog.All)
        {
            IsTrue(english.ContainsKey(metric.LabelKey));
            IsTrue(english.ContainsKey(metric.UnitKey));
        }

        var declaredDynamicKeyPattern = new Regex(
            "\"((?:sounds\\.builtin)\\.[A-Za-z0-9_.-]+)\"",
            RegexOptions.CultureInvariant);
        foreach (var sourceFile in Directory.GetFiles(
                     sourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (Match match in declaredDynamicKeyPattern.Matches(source))
            {
                IsTrue(english.ContainsKey(match.Groups[1].Value));
            }
        }
    }

    private static Dictionary<string, string> ReadLanguageFile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        IsTrue(document.RootElement.ValueKind == JsonValueKind.Object);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            IsTrue(property.Value.ValueKind == JsonValueKind.String);
            IsTrue(result.TryAdd(property.Name, property.Value.GetString() ?? ""));
        }
        return result;
    }

    private static string ExtractPlaceholderSignature(
        string value,
        Regex placeholderPattern) =>
        string.Join(
            ",",
            placeholderPattern.Matches(value ?? "")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal));

    private static string FindRepositoryRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "manifest.json")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "source")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "lang")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "UNMA repository root could not be located for localization tests.");
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
        AreEqual(
            "product.stored.Coal",
            SystemMetricCatalog.ProductStoredId("Coal"));
        AreEqual(
            "product.capacity.Coal",
            SystemMetricCatalog.ProductCapacityId("Coal"));
        AreEqual(
            "product.fill.Coal",
            SystemMetricCatalog.ProductFillId("Coal"));
        AreEqual(
            "maintenance.fill.MaintenanceT1",
            SystemMetricCatalog.MaintenanceFillId("MaintenanceT1"));
        AreEqual(
            "maintenance.needed_month_max.MaintenanceT1",
            SystemMetricCatalog.MaintenanceNeededMaxId("MaintenanceT1"));
        AreEqual(
            50d,
            SystemMetricCatalog.CalculateFillPercent(250d, 500d));
        AreEqual(
            100d,
            SystemMetricCatalog.CalculateFillPercent(600d, 500d));
        AreEqual(
            0d,
            SystemMetricCatalog.CalculateFillPercent(-10d, 500d));
        AreEqual(
            0d,
            SystemMetricCatalog.CalculateFillPercent(10d, 0d));
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

    private static void TestAlarmTimingPolicy()
    {
        var legacy = AlarmTimingPolicy.LegacyMigrationDefaults;
        AreEqual(0, legacy.ActivationDelayTicks);
        AreEqual(0, legacy.ResetDelayTicks);
        AreEqual(0, legacy.MinimumActiveTicks);
        AreEqual(0d, legacy.Hysteresis);
        AreEqual(0, default(AlarmTimingSettings).ActivationDelayTicks);
        AreEqual(0, default(AlarmTimingSettings).ResetDelayTicks);
        AreEqual(0, default(AlarmTimingSettings).MinimumActiveTicks);
        AreEqual(0d, default(AlarmTimingSettings).Hysteresis);
        IsFalse(default(AlarmTimingState).IsInitialized);

        var defaultStateDelay = AlarmTimingPolicy.Advance(
            default,
            conditionMet: true,
            currentGameTick: 100,
            new AlarmTimingSettings(5, 0, 0, 0d));
        IsTrue(defaultStateDelay.State.IsInitialized);
        IsFalse(defaultStateDelay.IsActive);
        AreEqual(100L, defaultStateDelay.State.ActivationPendingSinceTick);

        var normalized = AlarmTimingPolicy.Normalize(
            new AlarmTimingSettings(
                -1,
                int.MaxValue,
                -10,
                double.NaN));
        AreEqual(0, normalized.ActivationDelayTicks);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            normalized.ResetDelayTicks);
        AreEqual(0, normalized.MinimumActiveTicks);
        AreEqual(0d, normalized.Hysteresis);
        AreEqual(
            0d,
            AlarmTimingPolicy.Normalize(new AlarmTimingSettings(
                0,
                0,
                0,
                double.PositiveInfinity)).Hysteresis);

        foreach (ComparisonOperator comparison in Enum.GetValues(
                     typeof(ComparisonOperator)))
        {
            AreEqual(
                AlarmEvaluation.Compare(20d, comparison, 20d),
                AlarmTimingPolicy.CompareWithHysteresis(
                    20d,
                    comparison,
                    20d,
                    0d,
                    isCurrentlyActive: true));
        }

        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            19d,
            ComparisonOperator.Less,
            20d,
            5d,
            isCurrentlyActive: false));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            24.999d,
            ComparisonOperator.Less,
            20d,
            5d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            25d,
            ComparisonOperator.Less,
            20d,
            5d,
            isCurrentlyActive: true));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            25d,
            ComparisonOperator.LessOrEqual,
            20d,
            5d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            25.001d,
            ComparisonOperator.LessOrEqual,
            20d,
            5d,
            isCurrentlyActive: true));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            15.001d,
            ComparisonOperator.Greater,
            20d,
            5d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            15d,
            ComparisonOperator.Greater,
            20d,
            5d,
            isCurrentlyActive: true));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            15d,
            ComparisonOperator.GreaterOrEqual,
            20d,
            5d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            14.999d,
            ComparisonOperator.GreaterOrEqual,
            20d,
            5d,
            isCurrentlyActive: true));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            20.5d,
            ComparisonOperator.Equal,
            20d,
            1d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            21.00001d,
            ComparisonOperator.Equal,
            20d,
            1d,
            isCurrentlyActive: true));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            20.5d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            20d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            20.0000000001d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            20.5d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            isCurrentlyActive: false));
        IsTrue(AlarmTimingPolicy.CompareWithHysteresis(
            21.1d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            isCurrentlyActive: false));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            double.NaN,
            ComparisonOperator.Less,
            20d,
            5d,
            isCurrentlyActive: true));
        IsFalse(AlarmTimingPolicy.CompareWithHysteresis(
            19d,
            (ComparisonOperator)999,
            20d,
            5d,
            isCurrentlyActive: true));

        IsFalse(AlarmTimingPolicy.EvaluateConditionLatch(
            24d,
            ComparisonOperator.Less,
            20d,
            5d,
            hasPreviousLatch: false,
            previousLatch: true));
        IsTrue(AlarmTimingPolicy.EvaluateConditionLatch(
            24d,
            ComparisonOperator.Less,
            20d,
            5d,
            hasPreviousLatch: true,
            previousLatch: true));
        IsFalse(AlarmTimingPolicy.EvaluateConditionLatch(
            20.5d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            hasPreviousLatch: false,
            previousLatch: false));
        IsFalse(AlarmTimingPolicy.EvaluateConditionLatch(
            20.5d,
            ComparisonOperator.NotEqual,
            20d,
            1d,
            hasPreviousLatch: true,
            previousLatch: false));

        var immediate = AlarmTimingPolicy.Advance(
            AlarmTimingState.Inactive,
            conditionMet: true,
            currentGameTick: 10,
            legacy);
        AreEqual(AlarmTimingTransition.Activated, immediate.Transition);
        IsTrue(immediate.IsActive);
        var immediateClear = AlarmTimingPolicy.Advance(
            immediate.State,
            conditionMet: false,
            currentGameTick: 10,
            legacy);
        AreEqual(AlarmTimingTransition.Cleared, immediateClear.Transition);
        IsFalse(immediateClear.IsActive);

        var delayedSettings = new AlarmTimingSettings(
            activationDelayTicks: 5,
            resetDelayTicks: 4,
            minimumActiveTicks: 10,
            hysteresis: 0d);
        var delayed = AlarmTimingPolicy.Advance(
            AlarmTimingState.Inactive,
            conditionMet: true,
            currentGameTick: 100,
            delayedSettings);
        AreEqual(AlarmTimingTransition.None, delayed.Transition);
        AreEqual(100L, delayed.State.ActivationPendingSinceTick);
        delayed = AlarmTimingPolicy.Advance(
            delayed.State,
            conditionMet: true,
            currentGameTick: 104,
            delayedSettings);
        IsFalse(delayed.IsActive);
        delayed = AlarmTimingPolicy.Advance(
            delayed.State,
            conditionMet: true,
            currentGameTick: 105,
            delayedSettings);
        AreEqual(AlarmTimingTransition.Activated, delayed.Transition);
        AreEqual(105L, delayed.State.ActiveSinceTick);

        var interrupted = AlarmTimingPolicy.Advance(
            AlarmTimingState.Inactive,
            conditionMet: true,
            currentGameTick: 200,
            delayedSettings);
        interrupted = AlarmTimingPolicy.Advance(
            interrupted.State,
            conditionMet: false,
            currentGameTick: 203,
            delayedSettings);
        AreEqual(
            AlarmTimingState.NoTick,
            interrupted.State.ActivationPendingSinceTick);
        interrupted = AlarmTimingPolicy.Advance(
            interrupted.State,
            conditionMet: true,
            currentGameTick: 204,
            delayedSettings);
        interrupted = AlarmTimingPolicy.Advance(
            interrupted.State,
            conditionMet: true,
            currentGameTick: 208,
            delayedSettings);
        IsFalse(interrupted.IsActive);
        interrupted = AlarmTimingPolicy.Advance(
            interrupted.State,
            conditionMet: true,
            currentGameTick: 209,
            delayedSettings);
        IsTrue(interrupted.IsActive);

        var held = AlarmTimingPolicy.Advance(
            AlarmTimingState.ActiveAt(0),
            conditionMet: false,
            currentGameTick: 2,
            delayedSettings);
        AreEqual(2L, held.State.ResetPendingSinceTick);
        held = AlarmTimingPolicy.Advance(
            held.State,
            conditionMet: false,
            currentGameTick: 9,
            delayedSettings);
        IsTrue(held.IsActive);
        held = AlarmTimingPolicy.Advance(
            held.State,
            conditionMet: false,
            currentGameTick: 10,
            delayedSettings);
        AreEqual(AlarmTimingTransition.Cleared, held.Transition);

        var resetSettings = new AlarmTimingSettings(0, 4, 0, 0d);
        var reset = AlarmTimingPolicy.Advance(
            AlarmTimingState.ActiveAt(100),
            conditionMet: false,
            currentGameTick: 101,
            resetSettings);
        reset = AlarmTimingPolicy.Advance(
            reset.State,
            conditionMet: true,
            currentGameTick: 103,
            resetSettings);
        AreEqual(
            AlarmTimingState.NoTick,
            reset.State.ResetPendingSinceTick);
        reset = AlarmTimingPolicy.Advance(
            reset.State,
            conditionMet: false,
            currentGameTick: 104,
            resetSettings);
        reset = AlarmTimingPolicy.Advance(
            reset.State,
            conditionMet: false,
            currentGameTick: 107,
            resetSettings);
        IsTrue(reset.IsActive);
        reset = AlarmTimingPolicy.Advance(
            reset.State,
            conditionMet: false,
            currentGameTick: 108,
            resetSettings);
        IsFalse(reset.IsActive);

        var hystereticSettings = new AlarmTimingSettings(0, 3, 0, 5d);
        var hysteretic = AlarmTimingPolicy.AdvanceComparison(
            AlarmTimingState.Inactive,
            19d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 0,
            hystereticSettings);
        IsTrue(hysteretic.IsActive);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            22d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 1,
            hystereticSettings);
        AreEqual(
            AlarmTimingState.NoTick,
            hysteretic.State.ResetPendingSinceTick);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            25d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 2,
            hystereticSettings);
        AreEqual(2L, hysteretic.State.ResetPendingSinceTick);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            19d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 4,
            hystereticSettings);
        AreEqual(
            AlarmTimingState.NoTick,
            hysteretic.State.ResetPendingSinceTick);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            25d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 5,
            hystereticSettings);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            25d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 7,
            hystereticSettings);
        IsTrue(hysteretic.IsActive);
        hysteretic = AlarmTimingPolicy.AdvanceComparison(
            hysteretic.State,
            25d,
            ComparisonOperator.Less,
            20d,
            currentGameTick: 8,
            hystereticSettings);
        IsFalse(hysteretic.IsActive);

        var rebased = AlarmTimingPolicy.Advance(
            AlarmTimingState.Inactive,
            conditionMet: true,
            currentGameTick: 100,
            new AlarmTimingSettings(5, 0, 0, 0d));
        rebased = AlarmTimingPolicy.Advance(
            rebased.State,
            conditionMet: true,
            currentGameTick: 90,
            new AlarmTimingSettings(5, 0, 0, 0d));
        AreEqual(90L, rebased.State.ActivationPendingSinceTick);
        rebased = AlarmTimingPolicy.Advance(
            rebased.State,
            conditionMet: true,
            currentGameTick: 94,
            new AlarmTimingSettings(5, 0, 0, 0d));
        IsFalse(rebased.IsActive);
        rebased = AlarmTimingPolicy.Advance(
            rebased.State,
            conditionMet: true,
            currentGameTick: 95,
            new AlarmTimingSettings(5, 0, 0, 0d));
        IsTrue(rebased.IsActive);

        var rebasedActive = AlarmTimingPolicy.Advance(
            AlarmTimingState.ActiveAt(100),
            conditionMet: false,
            currentGameTick: 90,
            new AlarmTimingSettings(0, 0, 10, 0d));
        AreEqual(90L, rebasedActive.State.ActiveSinceTick);
        rebasedActive = AlarmTimingPolicy.Advance(
            rebasedActive.State,
            conditionMet: false,
            currentGameTick: 99,
            new AlarmTimingSettings(0, 0, 10, 0d));
        IsTrue(rebasedActive.IsActive);
        rebasedActive = AlarmTimingPolicy.Advance(
            rebasedActive.State,
            conditionMet: false,
            currentGameTick: 100,
            new AlarmTimingSettings(0, 0, 10, 0d));
        IsFalse(rebasedActive.IsActive);

        var clampedTick = AlarmTimingPolicy.Advance(
            AlarmTimingState.Inactive,
            conditionMet: true,
            currentGameTick: -50,
            legacy);
        AreEqual(0L, clampedTick.State.LastObservedTick);
        IsTrue(clampedTick.IsActive);
    }

    private static void TestAlarmEscalationPolicy()
    {
        var defaults = new AlarmEscalationDefinition();
        IsFalse(defaults.Enabled);
        AreEqual(0, defaults.AfterTicks);
        AreEqual(AlarmSeverity.Critical, defaults.Severity);
        AreEqual("", defaults.SoundId);
        AreEqual(AlarmOperatorAction.None, defaults.OperatorAction);

        var legacyOne = AlarmEscalationPolicy.LegacyMigrationDefaults;
        var legacyTwo = AlarmEscalationPolicy.LegacyMigrationDefaults;
        IsFalse(ReferenceEquals(legacyOne, legacyTwo));
        IsFalse(legacyOne.Enabled);

        var definition = new AlarmEscalationDefinition
        {
            Enabled = true,
            AfterTicks = 10,
            Severity = AlarmSeverity.Critical,
            SoundId = "",
            OperatorAction = AlarmOperatorAction.OpenPanel,
        };
        var clone = AlarmEscalationPolicy.Clone(definition);
        IsFalse(ReferenceEquals(definition, clone));
        AreEqual(definition.Enabled, clone.Enabled);
        AreEqual(definition.AfterTicks, clone.AfterTicks);
        AreEqual(definition.Severity, clone.Severity);
        AreEqual(definition.SoundId, clone.SoundId);
        AreEqual(definition.OperatorAction, clone.OperatorAction);
        IsFalse(AlarmEscalationPolicy.Clone(null).Enabled);

        var normalized = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = int.MaxValue,
                Severity = AlarmSeverity.Emergency,
                SoundId = " auto ",
                OperatorAction =
                    AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            },
            AlarmSeverity.Warning);
        IsTrue(normalized.Enabled);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            normalized.AfterTicks);
        AreEqual(AlarmSeverity.Emergency, normalized.Severity);
        AreEqual("auto", normalized.SoundId);
        AreEqual(
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            normalized.OperatorAction);

        var negativeDelay = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = -1,
                Severity = AlarmSeverity.Critical,
            },
            AlarmSeverity.Warning);
        IsFalse(negativeDelay.Enabled);
        AreEqual(0, negativeDelay.AfterTicks);

        var nonIncreasing = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 1,
                Severity = AlarmSeverity.Warning,
            },
            AlarmSeverity.Warning);
        IsFalse(nonIncreasing.Enabled);

        var malformedTarget = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 1,
                Severity = (AlarmSeverity)999,
            },
            AlarmSeverity.Warning);
        IsFalse(malformedTarget.Enabled);
        AreEqual(AlarmSeverity.Critical, malformedTarget.Severity);

        var emergencyBase = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 1,
                Severity = AlarmSeverity.Emergency,
            },
            AlarmSeverity.Emergency);
        IsFalse(emergencyBase.Enabled);

        var malformedAction = AlarmEscalationPolicy.Normalize(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 1,
                Severity = AlarmSeverity.Critical,
                OperatorAction = (AlarmOperatorAction)999,
            },
            AlarmSeverity.Warning);
        IsTrue(malformedAction.Enabled);
        AreEqual(AlarmOperatorAction.None, malformedAction.OperatorAction);
        AreEqual(
            AlarmOperatorAction.None,
            AlarmEscalationPolicy.NormalizeOperatorAction(
                (AlarmOperatorAction)(-1)));

        var inactive = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: false,
            isAlarmActive: false,
            activeSinceGameTick: 100,
            currentGameTick: 110);
        IsFalse(inactive.IsEscalated);
        IsFalse(inactive.JustEscalated);
        AreEqual(AlarmSeverity.Warning, inactive.Severity);
        AreEqual("horn", inactive.SoundId);
        AreEqual(AlarmOperatorAction.None, inactive.OperatorAction);

        for (var tick = 100L; tick < 110L; tick++)
        {
            var pending = AlarmEscalationPolicy.Evaluate(
                definition,
                AlarmSeverity.Warning,
                "horn",
                wasEscalated: false,
                isAlarmActive: true,
                activeSinceGameTick: 100,
                currentGameTick: tick);
            IsFalse(pending.IsEscalated);
            AreEqual(AlarmSeverity.Warning, pending.Severity);
        }

        var exact = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: 100,
            currentGameTick: 110);
        IsTrue(exact.IsEscalated);
        IsTrue(exact.JustEscalated);
        AreEqual(AlarmSeverity.Critical, exact.Severity);
        AreEqual("horn", exact.SoundId);
        AreEqual(AlarmOperatorAction.OpenPanel, exact.OperatorAction);

        var repeated = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: exact.IsEscalated,
            isAlarmActive: true,
            activeSinceGameTick: 100,
            currentGameTick: 110);
        IsTrue(repeated.IsEscalated);
        IsFalse(repeated.JustEscalated);
        AreEqual(AlarmOperatorAction.None, repeated.OperatorAction);

        var stickyAcrossRollback = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: true,
            isAlarmActive: true,
            activeSinceGameTick: 100,
            currentGameTick: 50);
        IsTrue(stickyAcrossRollback.IsEscalated);
        IsFalse(stickyAcrossRollback.JustEscalated);

        var pendingAcrossRollback = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: 100,
            currentGameTick: 50);
        IsFalse(pendingAcrossRollback.IsEscalated);
        var missingStart = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: AlarmTimingState.NoTick,
            currentGameTick: long.MaxValue);
        IsFalse(missingStart.IsEscalated);

        var nearMaximum = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "horn",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: long.MaxValue - 10,
            currentGameTick: long.MaxValue);
        IsTrue(nearMaximum.IsEscalated);

        var explicitSound = AlarmEscalationPolicy.Evaluate(
            new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 1,
                Severity = AlarmSeverity.Emergency,
                SoundId = " auto ",
                OperatorAction =
                    AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            },
            AlarmSeverity.Critical,
            "bell",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: 0,
            currentGameTick: 1);
        IsTrue(explicitSound.JustEscalated);
        AreEqual("auto", explicitSound.SoundId);
        AreEqual(
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            explicitSound.OperatorAction);

        var blankBaseSound = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            " ",
            wasEscalated: false,
            isAlarmActive: true,
            activeSinceGameTick: 0,
            currentGameTick: 0);
        AreEqual("auto", blankBaseSound.SoundId);

        var disabledClearsLatch = AlarmEscalationPolicy.Evaluate(
            new AlarmEscalationDefinition(),
            AlarmSeverity.Warning,
            "bell",
            wasEscalated: true,
            isAlarmActive: true,
            activeSinceGameTick: 0,
            currentGameTick: 100);
        IsFalse(disabledClearsLatch.IsEscalated);
        var clearResetsLatch = AlarmEscalationPolicy.Evaluate(
            definition,
            AlarmSeverity.Warning,
            "bell",
            wasEscalated: true,
            isAlarmActive: false,
            activeSinceGameTick: 0,
            currentGameTick: 100);
        IsFalse(clearResetsLatch.IsEscalated);

        AreEqual(
            "rule-7",
            AlarmEscalationPolicy.GetOccurrenceId(
                " rule-7 ",
                isEscalated: false));
        AreEqual(
            "rule-7:escalated",
            AlarmEscalationPolicy.GetOccurrenceId(
                " rule-7 ",
                isEscalated: true));
        IsTrue(AlarmEscalationPolicy.IsEscalatedOccurrenceId(
            "rule-7",
            " rule-7:escalated "));
        IsFalse(AlarmEscalationPolicy.IsEscalatedOccurrenceId(
            "rule-7",
            "rule-7"));

        var firstOccurrence = AlarmEvaluation.Transition(
            wasActive: false,
            wasAcknowledged: false,
            wasGoneUnacknowledged: false,
            previousSeverity: AlarmSeverity.Warning,
            isActive: true,
            severity: AlarmSeverity.Warning,
            autoAcknowledgeOnClear: false);
        IsTrue(firstOccurrence.IsNewOccurrence);
        var escalationOccurrence = AlarmEvaluation.Transition(
            wasActive: true,
            wasAcknowledged: true,
            wasGoneUnacknowledged: false,
            previousSeverity: AlarmSeverity.Warning,
            isActive: true,
            severity: exact.Severity,
            autoAcknowledgeOnClear: false);
        IsTrue(escalationOccurrence.IsNewOccurrence);
        IsFalse(escalationOccurrence.IsAcknowledged);
        var stableEscalation = AlarmEvaluation.Transition(
            wasActive: true,
            wasAcknowledged: false,
            wasGoneUnacknowledged: false,
            previousSeverity: AlarmSeverity.Critical,
            isActive: true,
            severity: AlarmSeverity.Critical,
            autoAcknowledgeOnClear: false);
        IsFalse(stableEscalation.IsNewOccurrence);
    }

    private static void TestAlarmAttentionQueuePolicy()
    {
        var requests = new List<AlarmAttentionRequest>();
        IsFalse(AlarmAttentionQueuePolicy.TryEnqueue(
            null,
            default));
        IsFalse(AlarmAttentionQueuePolicy.TryEnqueue(
            requests,
            default));
        IsFalse(new AlarmAttentionRequest(
            "alarm",
            0,
            "panel",
            "slot",
            AlarmSeverity.Warning,
            AlarmOperatorAction.OpenPanel).IsValid);
        IsFalse(new AlarmAttentionRequest(
            "alarm",
            1,
            "panel",
            "slot",
            AlarmSeverity.Warning,
            AlarmOperatorAction.None).IsValid);
        IsFalse(new AlarmAttentionRequest(
            "alarm",
            1,
            "panel",
            "slot",
            (AlarmSeverity)999,
            AlarmOperatorAction.OpenPanel).IsValid);

        var first = new AlarmAttentionRequest(
            " alarm-a ",
            5,
            " panel-a ",
            " slot-a ",
            AlarmSeverity.Warning,
            AlarmOperatorAction.OpenPanel);
        IsTrue(first.IsValid);
        AreEqual("alarm-a", first.AlarmKey);
        AreEqual("panel-a", first.PanelId);
        AreEqual("slot-a", first.SlotId);
        IsTrue(AlarmAttentionQueuePolicy.TryEnqueue(requests, first));
        AreEqual(1, requests.Count);
        IsFalse(AlarmAttentionQueuePolicy.TryEnqueue(
            requests,
            new AlarmAttentionRequest(
                "alarm-a",
                4,
                "panel-a",
                "slot-a",
                AlarmSeverity.Emergency,
                AlarmOperatorAction.OpenPanel)));
        AreEqual(1, requests.Count);
        IsTrue(AlarmAttentionQueuePolicy.TryEnqueue(
            requests,
            new AlarmAttentionRequest(
                "alarm-a",
                5,
                "panel-a",
                "slot-a",
                AlarmSeverity.Critical,
                AlarmOperatorAction.OpenPanel)));
        AreEqual(1, requests.Count);
        AreEqual(AlarmSeverity.Critical, requests[0].Severity);
        IsTrue(AlarmAttentionQueuePolicy.TryEnqueue(
            requests,
            new AlarmAttentionRequest(
                "alarm-a",
                6,
                "panel-a",
                "slot-a",
                AlarmSeverity.Emergency,
                AlarmOperatorAction.OpenPanel)));
        AreEqual(1, requests.Count);
        AreEqual(6L, requests[0].Sequence);

        requests.Clear();
        for (var index = 0;
             index < AlarmAttentionQueuePolicy.MaximumPendingRequests + 6;
             index++)
        {
            IsTrue(AlarmAttentionQueuePolicy.TryEnqueue(
                requests,
                new AlarmAttentionRequest(
                    "alarm-" + index,
                    index + 1L,
                    "panel",
                    "slot",
                    AlarmSeverity.Notice,
                    AlarmOperatorAction.OpenPanel)));
        }
        AreEqual(
            AlarmAttentionQueuePolicy.MaximumPendingRequests,
            requests.Count);
        IsFalse(requests.Any(item => item.AlarmKey == "alarm-0"));
        IsFalse(requests.Any(item => item.AlarmKey == "alarm-5"));
        IsTrue(requests.Any(item => item.AlarmKey == "alarm-6"));
        IsTrue(requests.Any(item => item.AlarmKey == "alarm-69"));

        requests = new List<AlarmAttentionRequest>
        {
            new(
                "stale",
                100,
                "panel",
                "slot",
                AlarmSeverity.Emergency,
                AlarmOperatorAction.OpenPanelAndCancelTemporaryMute),
            new(
                "warning-strong-action",
                999,
                "panel",
                "slot",
                AlarmSeverity.Warning,
                AlarmOperatorAction.OpenPanelAndCancelTemporaryMute),
            new(
                "emergency",
                10,
                "panel",
                "slot",
                AlarmSeverity.Emergency,
                AlarmOperatorAction.OpenPanel),
            new(
                "critical-open",
                999,
                "panel",
                "slot",
                AlarmSeverity.Critical,
                AlarmOperatorAction.OpenPanel),
            new(
                "critical-cancel-mute",
                7,
                "panel",
                "slot",
                AlarmSeverity.Critical,
                AlarmOperatorAction.OpenPanelAndCancelTemporaryMute),
            default,
        };
        IsTrue(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            candidate => candidate.AlarmKey != "stale",
            out var best));
        AreEqual("emergency", best.AlarmKey);
        IsFalse(requests.Any(item => item.AlarmKey == "stale"));
        IsTrue(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            candidate => true,
            out best));
        AreEqual("critical-cancel-mute", best.AlarmKey);
        IsTrue(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            candidate => true,
            out best));
        AreEqual("critical-open", best.AlarmKey);
        IsTrue(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            candidate => true,
            out best));
        AreEqual("warning-strong-action", best.AlarmKey);
        IsFalse(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            candidate => true,
            out _));
        IsFalse(AlarmAttentionQueuePolicy.TryTakeBest(
            null,
            candidate => true,
            out _));

        requests.Add(new AlarmAttentionRequest(
            "older",
            10,
            "",
            "",
            AlarmSeverity.Warning,
            AlarmOperatorAction.OpenPanel));
        requests.Add(new AlarmAttentionRequest(
            "newer",
            20,
            "",
            "",
            AlarmSeverity.Warning,
            AlarmOperatorAction.OpenPanel));
        IsTrue(AlarmAttentionQueuePolicy.TryTakeBest(
            requests,
            isStillRelevant: null,
            out best));
        AreEqual("newer", best.AlarmKey);
    }

    private static void TestAlarmTimingModelNormalization()
    {
        var defaultRule = new AlarmRuleDefinition();
        AreEqual(0, defaultRule.ActivationDelayTicks);
        AreEqual(0, defaultRule.ResetDelayTicks);
        AreEqual(0, defaultRule.MinimumActiveTicks);
        AreEqual(0d, new ConditionDefinition().Hysteresis);
        var defaultStage = new SystemAlarmStageDefinition();
        AreEqual(0, defaultStage.ActivationDelayTicks);
        AreEqual(0, defaultStage.ResetDelayTicks);
        AreEqual(0, defaultStage.MinimumActiveTicks);
        AreEqual(0d, new SystemConditionDefinition().Hysteresis);

        var legacy = UnmaConfiguration.CreateDefault();
        legacy.SchemaVersion = 17;
        var legacyRule = new AlarmRuleDefinition
        {
            PanelId = legacy.Panels.Find(panel => !panel.IsDashboard).Id,
            ActivationDelayTicks = 10,
            ResetDelayTicks = 20,
            MinimumActiveTicks = 30,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    MetricPath = "$global:population.total",
                    Hysteresis = 4.5d,
                },
            },
        };
        legacy.Rules.Add(legacyRule);
        var legacyStage = legacy.SystemAlarms[0].Stages[0];
        legacyStage.ActivationDelayTicks = 40;
        legacyStage.ResetDelayTicks = 50;
        legacyStage.MinimumActiveTicks = 60;
        legacyStage.Conditions[0].Hysteresis = 7.5d;
        legacy.Normalize();

        AreEqual(19, legacy.SchemaVersion);
        AreEqual(0, legacyRule.ActivationDelayTicks);
        AreEqual(0, legacyRule.ResetDelayTicks);
        AreEqual(0, legacyRule.MinimumActiveTicks);
        AreEqual(0d, legacyRule.Conditions[0].Hysteresis);
        AreEqual(0, legacyStage.ActivationDelayTicks);
        AreEqual(0, legacyStage.ResetDelayTicks);
        AreEqual(0, legacyStage.MinimumActiveTicks);
        AreEqual(0d, legacyStage.Conditions[0].Hysteresis);

        var current = UnmaConfiguration.CreateDefault();
        var currentRule = new AlarmRuleDefinition
        {
            PanelId = current.Panels.Find(panel => !panel.IsDashboard).Id,
            ActivationDelayTicks = -1,
            ResetDelayTicks = int.MaxValue,
            MinimumActiveTicks = 33,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    MetricPath = "$global:population.total",
                    Hysteresis = 2.5d,
                },
                new()
                {
                    MetricPath = "$global:population.available_workers",
                    Hysteresis = double.NaN,
                },
                new()
                {
                    MetricPath = "$global:population.needed_workers",
                    Hysteresis = -5d,
                },
            },
        };
        current.Rules.Add(currentRule);
        var currentStage = current.SystemAlarms[0].Stages[0];
        currentStage.ActivationDelayTicks = int.MaxValue;
        currentStage.ResetDelayTicks = -2;
        currentStage.MinimumActiveTicks = 44;
        currentStage.Conditions[0].Hysteresis = double.PositiveInfinity;
        current.Normalize();

        AreEqual(0, currentRule.ActivationDelayTicks);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            currentRule.ResetDelayTicks);
        AreEqual(33, currentRule.MinimumActiveTicks);
        AreEqual(2.5d, currentRule.Conditions[0].Hysteresis);
        AreEqual(0d, currentRule.Conditions[1].Hysteresis);
        AreEqual(0d, currentRule.Conditions[2].Hysteresis);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            currentStage.ActivationDelayTicks);
        AreEqual(0, currentStage.ResetDelayTicks);
        AreEqual(44, currentStage.MinimumActiveTicks);
        AreEqual(0d, currentStage.Conditions[0].Hysteresis);
    }

    private static void TestAlarmEscalationModelNormalization()
    {
        var defaultRule = new AlarmRuleDefinition();
        IsTrue(defaultRule.Escalation != null);
        IsFalse(defaultRule.Escalation.Enabled);
        AreEqual(AlarmSeverity.Critical, defaultRule.Escalation.Severity);
        AreEqual(
            AlarmOperatorAction.None,
            new SystemAlarmStageDefinition().OperatorAction);
        AreEqual(19, new UnmaConfiguration().SchemaVersion);

        var legacy = UnmaConfiguration.CreateDefault();
        legacy.SchemaVersion = 18;
        var legacyRule = new AlarmRuleDefinition
        {
            Id = "legacy-escalation-rule",
            PanelId = legacy.Panels.Find(panel => !panel.IsDashboard).Id,
            Severity = AlarmSeverity.Warning,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    MetricPath = "$global:population.total",
                    Comparison = ComparisonOperator.Less,
                    Threshold = 100d,
                },
            },
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 50,
                Severity = AlarmSeverity.Emergency,
                SoundId = "siren",
                OperatorAction =
                    AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            },
        };
        legacy.Rules.Add(legacyRule);
        var legacyStage = legacy.SystemAlarms[0].Stages[0];
        legacyStage.OperatorAction =
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute;
        var ruleOwnerKey = AlarmTimingMemoryPolicy.RuleOwnerKey(
            legacyRule.Id);
        var ruleTimingSignature = AlarmTimingMemoryPolicy
            .RuleDefinitionSignature(legacyRule);
        legacy.AlarmTimingMemories.Add(
            AlarmTimingMemoryPolicy.CreateMemory(
                ruleOwnerKey,
                ruleTimingSignature,
                AlarmTimingState.ActiveAt(25),
                new Dictionary<int, bool> { [0] = true }));
        var stageOwnerKey = AlarmTimingMemoryPolicy.SystemStageOwnerKey(
            legacy.SystemAlarms[0].Id,
            legacyStage.Id,
            0);
        var stageTimingSignature = AlarmTimingMemoryPolicy
            .SystemStageDefinitionSignature(legacyStage);
        legacy.AlarmTimingMemories.Add(
            AlarmTimingMemoryPolicy.CreateMemory(
                stageOwnerKey,
                stageTimingSignature,
                AlarmTimingState.ActiveAt(35),
                new Dictionary<int, bool> { [0] = true }));
        legacy.Normalize();

        AreEqual(19, legacy.SchemaVersion);
        IsTrue(legacyRule.Escalation != null);
        IsFalse(legacyRule.Escalation.Enabled);
        AreEqual(0, legacyRule.Escalation.AfterTicks);
        AreEqual(AlarmSeverity.Critical, legacyRule.Escalation.Severity);
        AreEqual("", legacyRule.Escalation.SoundId);
        AreEqual(
            AlarmOperatorAction.None,
            legacyRule.Escalation.OperatorAction);
        AreEqual(AlarmOperatorAction.None, legacyStage.OperatorAction);
        AreEqual(2, legacy.AlarmTimingMemories.Count);
        var preservedRuleMemory = legacy.AlarmTimingMemories.Single(memory =>
            string.Equals(
                memory.OwnerKey,
                ruleOwnerKey,
                StringComparison.Ordinal));
        AreEqual(25L, preservedRuleMemory.ActiveSinceTick);
        AreEqual(ruleTimingSignature, preservedRuleMemory
            .DefinitionSignature);
        var preservedStageMemory = legacy.AlarmTimingMemories.Single(memory =>
            string.Equals(
                memory.OwnerKey,
                stageOwnerKey,
                StringComparison.Ordinal));
        AreEqual(stageOwnerKey, preservedStageMemory.OwnerKey);
        AreEqual(stageTimingSignature, preservedStageMemory
            .DefinitionSignature);
        IsTrue(preservedStageMemory.IsActive);
        AreEqual(35L, preservedStageMemory.ActiveSinceTick);

        var current = UnmaConfiguration.CreateDefault();
        var currentPanelId = current.Panels
            .Find(panel => !panel.IsDashboard).Id;
        var validRule = new AlarmRuleDefinition
        {
            Id = "valid-escalation",
            PanelId = currentPanelId,
            Severity = AlarmSeverity.Warning,
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = int.MaxValue,
                Severity = AlarmSeverity.Emergency,
                SoundId = " auto ",
                OperatorAction =
                    AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            },
        };
        var malformedRule = new AlarmRuleDefinition
        {
            Id = "malformed-escalation",
            PanelId = currentPanelId,
            Severity = AlarmSeverity.Warning,
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = -50,
                Severity = (AlarmSeverity)999,
                SoundId = null,
                OperatorAction = (AlarmOperatorAction)999,
            },
        };
        var nullDefinitionRule = new AlarmRuleDefinition
        {
            Id = "null-escalation",
            PanelId = currentPanelId,
            Escalation = null,
        };
        var nonIncreasingRule = new AlarmRuleDefinition
        {
            Id = "non-increasing-escalation",
            PanelId = currentPanelId,
            Severity = AlarmSeverity.Critical,
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 10,
                Severity = AlarmSeverity.Warning,
                SoundId = "none",
                OperatorAction = AlarmOperatorAction.OpenPanel,
            },
        };
        current.Rules.Add(validRule);
        current.Rules.Add(malformedRule);
        current.Rules.Add(nullDefinitionRule);
        current.Rules.Add(nonIncreasingRule);
        var validStage = current.SystemAlarms[0].Stages[0];
        validStage.OperatorAction = AlarmOperatorAction.OpenPanel;
        var malformedStage = current.SystemAlarms[0].Stages[1];
        malformedStage.OperatorAction = (AlarmOperatorAction)999;
        current.Normalize();

        IsTrue(validRule.Escalation.Enabled);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            validRule.Escalation.AfterTicks);
        AreEqual(AlarmSeverity.Emergency, validRule.Escalation.Severity);
        AreEqual("auto", validRule.Escalation.SoundId);
        AreEqual(
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            validRule.Escalation.OperatorAction);
        IsFalse(malformedRule.Escalation.Enabled);
        AreEqual(0, malformedRule.Escalation.AfterTicks);
        AreEqual(AlarmSeverity.Critical, malformedRule.Escalation.Severity);
        AreEqual("", malformedRule.Escalation.SoundId);
        AreEqual(
            AlarmOperatorAction.None,
            malformedRule.Escalation.OperatorAction);
        IsTrue(nullDefinitionRule.Escalation != null);
        IsFalse(nullDefinitionRule.Escalation.Enabled);
        IsFalse(nonIncreasingRule.Escalation.Enabled);
        AreEqual("none", nonIncreasingRule.Escalation.SoundId);
        AreEqual(AlarmOperatorAction.OpenPanel, validStage.OperatorAction);
        AreEqual(AlarmOperatorAction.None, malformedStage.OperatorAction);

        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        serializer.WriteObject(stream, current);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();
        AreEqual(19, restored.SchemaVersion);
        var restoredRule = restored.Rules.Single(rule =>
            rule.Id == validRule.Id);
        IsTrue(restoredRule.Escalation.Enabled);
        AreEqual(
            AlarmTimingPolicy.MaximumTimingTicks,
            restoredRule.Escalation.AfterTicks);
        AreEqual(
            AlarmSeverity.Emergency,
            restoredRule.Escalation.Severity);
        AreEqual("auto", restoredRule.Escalation.SoundId);
        AreEqual(
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            restoredRule.Escalation.OperatorAction);
        var restoredStage = restored.SystemAlarms[0].Stages.Single(stage =>
            stage.Id == validStage.Id);
        AreEqual(
            AlarmOperatorAction.OpenPanel,
            restoredStage.OperatorAction);
    }

    private static void TestAlarmTimingMemoryPolicy()
    {
        var rule = new AlarmRuleDefinition
        {
            Id = "timed-rule",
            PanelId = "panel-a",
            Name = "TIMED RULE",
            Severity = AlarmSeverity.Warning,
            Logic = AlarmLogic.All,
            ActiveColor = "#112233",
            SoundId = "auto",
            ActivationDelayTicks = 20,
            ResetDelayTicks = 30,
            MinimumActiveTicks = 40,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    EntityId = 17,
                    EntityType = "Storage",
                    EntityPrototypeId = "AirStorageT1",
                    MetricPath = "$stored.percent",
                    Comparison = ComparisonOperator.Less,
                    Threshold = 25d,
                    Hysteresis = 3d,
                },
                new()
                {
                    MetricPath = "$global:population.available_workers",
                    Comparison = ComparisonOperator.GreaterOrEqual,
                    Threshold = 10d,
                    Hysteresis = 2d,
                },
            },
        };
        var ruleSignature =
            AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule);
        IsFalse(string.IsNullOrWhiteSpace(ruleSignature));
        rule.Name = "DISPLAY ONLY";
        rule.PanelId = "panel-b";
        rule.ActiveColor = "#FFFFFF";
        rule.SoundId = "horn";
        rule.Severity = AlarmSeverity.Emergency;
        AreEqual(
            ruleSignature,
            AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule));
        rule.Conditions[0].Threshold = 24d;
        IsFalse(string.Equals(
            ruleSignature,
            AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule),
            StringComparison.Ordinal));
        rule.Conditions[0].Threshold = 25d;
        rule.Conditions[0].EntityType = "Storage ";
        IsFalse(string.Equals(
            ruleSignature,
            AlarmTimingMemoryPolicy.RuleDefinitionSignature(rule),
            StringComparison.Ordinal));
        rule.Conditions[0].EntityType = "Storage";

        var stage = new SystemAlarmStageDefinition
        {
            Id = "warning",
            Priority = 10,
            Message = "WARNING",
            Severity = AlarmSeverity.Warning,
            Logic = AlarmLogic.Any,
            ActivationDelayTicks = 5,
            ResetDelayTicks = 6,
            MinimumActiveTicks = 7,
            Conditions = new List<SystemConditionDefinition>
            {
                new()
                {
                    MetricId = "population.health",
                    Comparison = ComparisonOperator.LessOrEqual,
                    Threshold = 90d,
                    Hysteresis = 2d,
                },
            },
        };
        var stageSignature =
            AlarmTimingMemoryPolicy.SystemStageDefinitionSignature(stage);
        stage.Message = "DISPLAY ONLY";
        stage.Priority = 999;
        stage.Severity = AlarmSeverity.Critical;
        stage.ActiveColor = "#ABCDEF";
        stage.SoundId = "siren";
        AreEqual(
            stageSignature,
            AlarmTimingMemoryPolicy.SystemStageDefinitionSignature(stage));
        stage.Conditions[0].Hysteresis = 3d;
        IsFalse(string.Equals(
            stageSignature,
            AlarmTimingMemoryPolicy.SystemStageDefinitionSignature(stage),
            StringComparison.Ordinal));
        stage.Conditions[0].Hysteresis = 2d;
        stage.Conditions[0].MetricId = "population.health ";
        IsFalse(string.Equals(
            stageSignature,
            AlarmTimingMemoryPolicy.SystemStageDefinitionSignature(stage),
            StringComparison.Ordinal));
        stage.Conditions[0].MetricId = "population.health";

        var pendingState = new AlarmTimingState(
            false,
            activationPendingSinceTick: 100,
            activeSinceTick: AlarmTimingState.NoTick,
            resetPendingSinceTick: AlarmTimingState.NoTick,
            lastObservedTick: 104);
        var memory = AlarmTimingMemoryPolicy.CreateMemory(
            AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id),
            ruleSignature,
            pendingState,
            new Dictionary<int, bool>
            {
                [0] = true,
                [1] = false,
            });
        IsTrue(memory != null);
        IsTrue(AlarmTimingMemoryPolicy.TryRestore(
            memory,
            AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id),
            ruleSignature,
            rule.Conditions.Count,
            out var restoredPendingState,
            out var restoredLatches));
        IsFalse(restoredPendingState.IsActive);
        AreEqual(100L, restoredPendingState.ActivationPendingSinceTick);
        AreEqual(104L, restoredPendingState.LastObservedTick);
        AreEqual(2, restoredLatches.Count);
        IsTrue(restoredLatches[0]);
        IsFalse(restoredLatches[1]);

        var activeResetState = new AlarmTimingState(
            true,
            activationPendingSinceTick: AlarmTimingState.NoTick,
            activeSinceTick: 80,
            resetPendingSinceTick: 110,
            lastObservedTick: 115);
        var activeMemory = AlarmTimingMemoryPolicy.CreateMemory(
            AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id),
            ruleSignature,
            activeResetState,
            restoredLatches);
        IsTrue(AlarmTimingMemoryPolicy.TryRestore(
            activeMemory,
            AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id),
            ruleSignature,
            rule.Conditions.Count,
            out var restoredActiveState,
            out _));
        IsTrue(restoredActiveState.IsActive);
        AreEqual(80L, restoredActiveState.ActiveSinceTick);
        AreEqual(110L, restoredActiveState.ResetPendingSinceTick);
        IsFalse(AlarmTimingPolicy.HasPersistentStateChanged(
            activeResetState,
            new AlarmTimingState(
                true,
                AlarmTimingState.NoTick,
                80,
                110,
                999)));
        var preservedAfterDefinitionEdit = AlarmTimingPolicy
            .PreserveActiveForDefinitionChange(activeResetState, 120);
        IsTrue(preservedAfterDefinitionEdit.IsActive);
        AreEqual(80L, preservedAfterDefinitionEdit.ActiveSinceTick);
        AreEqual(
            AlarmTimingState.NoTick,
            preservedAfterDefinitionEdit.ResetPendingSinceTick);
        AreEqual(120L, preservedAfterDefinitionEdit.LastObservedTick);
        IsFalse(AlarmTimingPolicy.PreserveActiveForDefinitionChange(
            pendingState,
            120).IsInitialized);
        var activeDefinitionEditLatches = AlarmTimingPolicy
            .CreateActiveConditionLatches(3);
        AreEqual(3, activeDefinitionEditLatches.Count);
        IsTrue(activeDefinitionEditLatches.Values.All(value => value));
        AreEqual(
            0,
            AlarmTimingPolicy.CreateActiveConditionLatches(-1).Count);
        IsTrue(AlarmTimingPolicy.HasPersistentStateChanged(
            pendingState,
            new AlarmTimingState(
                false,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                104)));
        IsTrue(AlarmTimingPolicy.HasPersistentStateChanged(
            activeResetState,
            preservedAfterDefinitionEdit));

        var invalidFutureTick = AlarmTimingMemoryPolicy.CloneMemory(memory);
        invalidFutureTick.ActivationPendingSinceTick = 200;
        IsTrue(AlarmTimingMemoryPolicy.TryRestore(
            invalidFutureTick,
            invalidFutureTick.OwnerKey,
            ruleSignature,
            rule.Conditions.Count,
            out var safelyNormalizedState,
            out _));
        AreEqual(
            AlarmTimingState.NoTick,
            safelyNormalizedState.ActivationPendingSinceTick);
        IsFalse(AlarmTimingMemoryPolicy.TryRestore(
            memory,
            memory.OwnerKey,
            "wrong-signature",
            rule.Conditions.Count,
            out _,
            out _));

        var systemAlarm = new SystemAlarmDefinition
        {
            Id = "system:test",
            Enabled = true,
            Stages = new List<SystemAlarmStageDefinition> { stage },
        };
        var systemState = AlarmTimingState.ActiveAt(70);
        var systemMemory = AlarmTimingMemoryPolicy.CreateMemory(
            AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                systemAlarm.Id,
                stage.Id,
                0),
            stageSignature,
            systemState,
            new Dictionary<int, bool> { [0] = true });
        IsFalse(string.Equals(
            systemMemory.OwnerKey,
            AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                systemAlarm.Id,
                stage.Id,
                1),
            StringComparison.Ordinal));
        var criticalStage = new SystemAlarmStageDefinition
        {
            Id = "critical",
            Enabled = true,
            Logic = AlarmLogic.All,
            ActivationDelayTicks = 9,
            Conditions = new List<SystemConditionDefinition>
            {
                new()
                {
                    MetricId = "population.health",
                    Comparison = ComparisonOperator.Less,
                    Threshold = 50d,
                    Hysteresis = 1d,
                },
            },
        };
        systemAlarm.Stages.Add(criticalStage);
        var criticalStageSignature = AlarmTimingMemoryPolicy
            .SystemStageDefinitionSignature(criticalStage);
        var criticalStageMemory = AlarmTimingMemoryPolicy.CreateMemory(
            AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                systemAlarm.Id,
                criticalStage.Id,
                1),
            criticalStageSignature,
            new AlarmTimingState(
                false,
                60,
                AlarmTimingState.NoTick,
                AlarmTimingState.NoTick,
                65),
            new Dictionary<int, bool> { [0] = true });
        var memories = new List<AlarmTimingMemoryDefinition>
        {
            memory,
            systemMemory,
            criticalStageMemory,
            new()
            {
                OwnerKey = "rule:orphan",
                DefinitionSignature = "orphan",
                LastObservedTick = 1,
            },
            new()
            {
                OwnerKey = memory.OwnerKey,
                DefinitionSignature = "stale",
                LastObservedTick = 104,
            },
            memory,
        };
        AlarmTimingMemoryPolicy.NormalizeMemories(
            memories,
            new[] { rule },
            new[] { systemAlarm },
            discardExisting: false);
        AreEqual(3, memories.Count);
        IsTrue(memories.Any(item => string.Equals(
            item.OwnerKey,
            AlarmTimingMemoryPolicy.RuleOwnerKey(rule.Id),
            StringComparison.Ordinal)));
        IsTrue(memories.Any(item => string.Equals(
            item.OwnerKey,
            AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                systemAlarm.Id,
                stage.Id,
                0),
            StringComparison.Ordinal)));
        var normalizedCriticalMemory = memories.Single(item =>
            string.Equals(
                item.OwnerKey,
                AlarmTimingMemoryPolicy.SystemStageOwnerKey(
                    systemAlarm.Id,
                    criticalStage.Id,
                    1),
                StringComparison.Ordinal));
        AreEqual(
            60L,
            normalizedCriticalMemory.ActivationPendingSinceTick);

        var disabledOccurrenceStage = new SystemAlarmStageDefinition
        {
            Id = "disabled-occurrence",
            Enabled = false,
            Priority = 999,
            Severity = AlarmSeverity.Emergency,
        };
        var restoreStages = new List<SystemAlarmStageDefinition>
        {
            stage,
            disabledOccurrenceStage,
            criticalStage,
        };
        AreEqual(
            0,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                restoreStages,
                stage.Id,
                stage.Priority,
                stage.Severity));
        AreEqual(
            -1,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                restoreStages,
                disabledOccurrenceStage.Id,
                disabledOccurrenceStage.Priority,
                disabledOccurrenceStage.Severity));
        AreEqual(
            -1,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                restoreStages,
                "removed-occurrence",
                criticalStage.Priority,
                criticalStage.Severity));
        AreEqual(
            2,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                restoreStages,
                "",
                criticalStage.Priority,
                criticalStage.Severity));
        AreEqual(
            0,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                restoreStages,
                " ",
                stage.Priority,
                stage.Severity));
        AreEqual(
            -1,
            AlarmTimingMemoryPolicy.FindRestoredSystemStageIndex(
                null,
                "",
                0,
                AlarmSeverity.Warning));

        var configuration = UnmaConfiguration.CreateDefault();
        rule.PanelId = configuration.Panels.Find(panel => !panel.IsDashboard).Id;
        configuration.Rules.Add(rule);
        configuration.AlarmTimingMemories.Add(memory);
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var roundTripped =
            (UnmaConfiguration)serializer.ReadObject(stream);
        roundTripped.Normalize();
        AreEqual(1, roundTripped.AlarmTimingMemories.Count);
        AreEqual(
            100L,
            roundTripped.AlarmTimingMemories[0]
                .ActivationPendingSinceTick);
        AreEqual(2, roundTripped.AlarmTimingMemories[0]
            .ConditionLatches.Count);

        configuration.SchemaVersion = 17;
        configuration.AlarmTimingMemories.Add(systemMemory);
        configuration.Normalize();
        AreEqual(0, configuration.AlarmTimingMemories.Count);
    }

    private static void TestAlarmAudioSnoozePolicy()
    {
        AreEqual(
            GameTimeWindowPolicy.MaximumWindowTicks,
            AlarmAudioSnoozePolicy.MaximumDurationTicks);
        var empty = default(AlarmAudioSnoozeState);
        IsFalse(empty.IsInitialized);
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            empty,
            "alarm:test",
            1,
            0,
            isActive: true));

        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            null,
            1,
            0,
            1,
            out var invalid));
        IsFalse(invalid.IsInitialized);
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "   ",
            1,
            0,
            1,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:test",
            0,
            0,
            1,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:test",
            -1,
            0,
            1,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:test",
            1,
            -1,
            1,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:test",
            1,
            10,
            10,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:test",
            1,
            10,
            9,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilGone(
            "alarm:test",
            0,
            10,
            out _));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilGone(
            "alarm:test",
            1,
            -1,
            out _));

        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            " rule:workers ",
            7,
            100,
            110,
            out var timed));
        IsTrue(timed.IsInitialized);
        AreEqual("rule:workers", timed.AlarmKey);
        AreEqual(7L, timed.Sequence);
        AreEqual(100L, timed.StartedAtGameTick);
        AreEqual(110L, timed.MutedUntilGameTick);
        IsTrue(timed.HasEndTick);
        IsFalse(timed.EndWhenGone);
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            100,
            isActive: true));
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            " rule:workers ",
            7,
            100,
            isActive: false));
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            109,
            isActive: false));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            110,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            111,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            8,
            101,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "RULE:WORKERS",
            7,
            101,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            99,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timed,
            "rule:workers",
            7,
            -1,
            isActive: true));

        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "system:food",
            12,
            50,
            75,
            endWhenGone: true,
            out var timedUntilGone));
        IsTrue(timedUntilGone.EndWhenGone);
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            timedUntilGone,
            "system:food",
            12,
            60,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            timedUntilGone,
            "system:food",
            12,
            60,
            isActive: false));

        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilGone(
            "vanilla:warning",
            21,
            200,
            out var untilGone));
        IsFalse(untilGone.HasEndTick);
        AreEqual(
            AlarmAudioSnoozeState.NoGameTick,
            untilGone.MutedUntilGameTick);
        IsTrue(untilGone.EndWhenGone);
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            untilGone,
            "vanilla:warning",
            21,
            200,
            isActive: true));
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            untilGone,
            "vanilla:warning",
            21,
            long.MaxValue,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            untilGone,
            "vanilla:warning",
            21,
            201,
            isActive: false));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            untilGone,
            "vanilla:warning",
            22,
            201,
            isActive: true));

        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:clamped",
            30,
            100,
            long.MaxValue,
            out var clamped));
        AreEqual(
            100L + AlarmAudioSnoozePolicy.MaximumDurationTicks,
            clamped.MutedUntilGameTick);
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            clamped,
            "alarm:clamped",
            30,
            clamped.MutedUntilGameTick - 1,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            clamped,
            "alarm:clamped",
            30,
            clamped.MutedUntilGameTick,
            isActive: true));

        var nearMaximum = long.MaxValue - 5;
        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:saturated",
            long.MaxValue,
            nearMaximum,
            long.MaxValue,
            out var saturated));
        AreEqual(long.MaxValue, saturated.MutedUntilGameTick);
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            saturated,
            "alarm:saturated",
            long.MaxValue,
            long.MaxValue - 1,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.IsSnoozed(
            saturated,
            "alarm:saturated",
            long.MaxValue,
            long.MaxValue,
            isActive: true));
        IsFalse(AlarmAudioSnoozePolicy.TryCreateUntilTick(
            "alarm:no-future-tick",
            1,
            long.MaxValue,
            long.MaxValue,
            out _));
        IsTrue(AlarmAudioSnoozePolicy.TryCreateUntilGone(
            "alarm:max-tick",
            long.MaxValue,
            long.MaxValue,
            out var maxTickUntilGone));
        IsTrue(AlarmAudioSnoozePolicy.IsSnoozed(
            maxTickUntilGone,
            "alarm:max-tick",
            long.MaxValue,
            long.MaxValue,
            isActive: true));
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

    private static void TestInstrumentValuePolicy()
    {
        var values = new[] { 100d, 50d, 25d };
        IsTrue(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Single,
            values,
            out var single));
        AreEqual(100d, single);
        IsTrue(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Sum,
            values,
            out var sum));
        AreEqual(175d, sum);
        IsTrue(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Average,
            values,
            out var average));
        AreEqual(175d / 3d, average);
        IsTrue(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Minimum,
            values,
            out var minimum));
        AreEqual(25d, minimum);
        IsTrue(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Maximum,
            values,
            out var maximum));
        AreEqual(100d, maximum);
        IsFalse(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Sum,
            Array.Empty<double>(),
            out _));
        IsFalse(InstrumentValuePolicy.TryAggregate(
            InstrumentAggregationMode.Sum,
            new[] { 1d, double.NaN },
            out _));

        var signatureSource = new InstrumentSourceDefinition
        {
            EntityId = 17,
            EntityPrototypeId = "AirStorageT1",
        };
        var signatureInstrument = new InstrumentDefinition
        {
            MetricPath = "$stored.quantity",
            Aggregation = InstrumentAggregationMode.Sum,
            Sources = new List<InstrumentSourceDefinition>
            {
                signatureSource,
            },
        };
        var signature = InstrumentValuePolicy.DefinitionSignature(
            signatureInstrument);
        signatureInstrument.Title = "Nur Anzeige geändert";
        AreEqual(
            signature,
            InstrumentValuePolicy.DefinitionSignature(signatureInstrument));
        signatureInstrument.Aggregation = InstrumentAggregationMode.Average;
        IsFalse(string.Equals(
            signature,
            InstrumentValuePolicy.DefinitionSignature(signatureInstrument),
            StringComparison.Ordinal));
        signatureInstrument.Aggregation = InstrumentAggregationMode.Sum;
        signatureInstrument.MetricPath = "$stored.percent";
        IsFalse(string.Equals(
            signature,
            InstrumentValuePolicy.DefinitionSignature(signatureInstrument),
            StringComparison.Ordinal));
        signatureInstrument.MetricPath = "$stored.quantity";
        signatureSource.EntityId = 18;
        IsFalse(string.Equals(
            signature,
            InstrumentValuePolicy.DefinitionSignature(signatureInstrument),
            StringComparison.Ordinal));

        var history = new[]
        {
            new InstrumentValueSample(0d, 100d),
            new InstrumentValueSample(5d, 95d),
            new InstrumentValueSample(30d, 90d),
            new InstrumentValueSample(65d, 70d),
        };
        IsTrue(InstrumentValuePolicy.TryCalculateTrend(
            history,
            65d,
            70d,
            InstrumentTrendMode.DecreaseAbsolute,
            60,
            out var absoluteDecrease));
        AreEqual(25d, absoluteDecrease);
        IsTrue(InstrumentValuePolicy.TryCalculateTrend(
            history,
            65d,
            70d,
            InstrumentTrendMode.DecreasePercent,
            60,
            out var percentDecrease));
        AreEqual(25d / 95d * 100d, percentDecrease);
        IsTrue(InstrumentValuePolicy.IsTrendTriggered(
            percentDecrease,
            25d));
        IsFalse(InstrumentValuePolicy.IsTrendTriggered(
            percentDecrease,
            27d));
        IsFalse(InstrumentValuePolicy.TryCalculateTrend(
            history,
            30d,
            90d,
            InstrumentTrendMode.DecreaseAbsolute,
            60,
            out _));
        IsFalse(InstrumentValuePolicy.TryCalculateTrend(
            new[]
            {
                new InstrumentValueSample(-40d, 100d),
                new InstrumentValueSample(65d, 70d),
            },
            65d,
            70d,
            InstrumentTrendMode.DecreaseAbsolute,
            60,
            out _));
        IsTrue(InstrumentValuePolicy.TryCalculateTrend(
            new[]
            {
                new InstrumentValueSample(0d, 50d),
                new InstrumentValueSample(60d, 75d),
            },
            60d,
            75d,
            InstrumentTrendMode.IncreaseAbsolute,
            60,
            out var absoluteIncrease));
        AreEqual(25d, absoluteIncrease);
        IsTrue(InstrumentValuePolicy.TryCalculateTrend(
            new[]
            {
                new InstrumentValueSample(0d, 50d),
                new InstrumentValueSample(60d, 75d),
            },
            60d,
            75d,
            InstrumentTrendMode.IncreasePercent,
            60,
            out var percentIncrease));
        AreEqual(50d, percentIncrease);
        IsTrue(InstrumentValuePolicy.TryEvaluateSustainedComparison(
            new[]
            {
                new InstrumentValueSample(0d, 40d),
                new InstrumentValueSample(20d, 35d),
                new InstrumentValueSample(40d, 30d),
            },
            40d,
            30d,
            40,
            ComparisonOperator.LessOrEqual,
            40d,
            out var sustained));
        IsTrue(sustained);
        IsTrue(InstrumentValuePolicy.TryEvaluateSustainedComparison(
            new[]
            {
                new InstrumentValueSample(0d, 40d),
                new InstrumentValueSample(20d, 45d),
                new InstrumentValueSample(40d, 30d),
            },
            40d,
            30d,
            40,
            ComparisonOperator.LessOrEqual,
            40d,
            out sustained));
        IsFalse(sustained);
        AreEqual(20, GameTimeWindowPolicy.ToSimTicks(1, GameTimeUnit.Day));
        AreEqual(600, GameTimeWindowPolicy.ToSimTicks(1, GameTimeUnit.Month));
        AreEqual(7200, GameTimeWindowPolicy.ToSimTicks(1, GameTimeUnit.Year));
        AreEqual(72000, GameTimeWindowPolicy.ToSimTicks(1, GameTimeUnit.Decade));
        AreEqual(720000, GameTimeWindowPolicy.ToSimTicks(1, GameTimeUnit.Century));
        IsFalse(InstrumentValuePolicy.TryCalculateTrend(
            new[] { new InstrumentValueSample(0d, 0d) },
            60d,
            0d,
            InstrumentTrendMode.DecreasePercent,
            60,
            out _));

        var legacyConfiguration = new UnmaConfiguration
        {
            SchemaVersion = 15,
            Instruments = new List<InstrumentDefinition>
            {
                new()
                {
                    Id = "legacy-recorder",
                    EntityId = 42,
                    EntityTitle = "Kohlelager",
                    EntityPrototypeId = "AirStorageT1",
                    MetricPath = "$stored.quantity",
                    HistoryDurationSeconds = 0,
                },
            },
            Rules = new List<AlarmRuleDefinition>
            {
                new()
                {
                    Conditions = new List<ConditionDefinition>
                    {
                        new()
                        {
                            InstrumentId = "legacy-recorder",
                            TrendMode =
                                InstrumentTrendMode.DecreaseAbsolute,
                            WindowSeconds = 0,
                            DeltaThreshold = 0d,
                        },
                    },
                },
            },
        };
        legacyConfiguration.Normalize();
        AreEqual(19, legacyConfiguration.SchemaVersion);
        AreEqual(1, legacyConfiguration.Instruments.Count);
        AreEqual(1, legacyConfiguration.Instruments[0].Sources.Count);
        AreEqual(42, legacyConfiguration.Instruments[0].Sources[0].EntityId);
        AreEqual(
            InstrumentAggregationMode.Single,
            legacyConfiguration.Instruments[0].Aggregation);
        AreEqual(
            3600,
            legacyConfiguration.Instruments[0].HistoryDurationSeconds);
        AreEqual(60, legacyConfiguration.Rules[0].Conditions[0].WindowSeconds);
        AreEqual(0d, legacyConfiguration.Rules[0].Conditions[0].DeltaThreshold);
        AreEqual(1, legacyConfiguration.Rules[0].Conditions[0].WindowAmount);
        AreEqual(
            GameTimeUnit.Month,
            legacyConfiguration.Rules[0].Conditions[0].WindowUnit);
        AreEqual(
            100,
            legacyConfiguration.Instruments[0].HistoryDurationAmount);
        AreEqual(
            GameTimeUnit.Year,
            legacyConfiguration.Instruments[0].HistoryDurationUnit);

        var currentMultiSourceConfiguration = new UnmaConfiguration
        {
            SchemaVersion = 17,
            Instruments = new List<InstrumentDefinition>
            {
                new()
                {
                    Id = "current-calculated",
                    EntityId = 11,
                    EntityTitle = "2 QUELLEN · SUMME",
                    EntityPrototypeId = "RemovedLegacySource",
                    MetricPath = "$stored.quantity",
                    Aggregation = InstrumentAggregationMode.Sum,
                    Sources = new List<InstrumentSourceDefinition>
                    {
                        new()
                        {
                            EntityId = 22,
                            EntityTitle = "Kohlelager West",
                            EntityPrototypeId = "AirStorageT1",
                        },
                        new()
                        {
                            EntityId = 33,
                            EntityTitle = "Kohlelager Ost",
                            EntityPrototypeId = "AirStorageT1",
                        },
                    },
                },
            },
        };
        currentMultiSourceConfiguration.Normalize();
        var normalizedCalculated =
            currentMultiSourceConfiguration.Instruments[0];
        AreEqual(2, normalizedCalculated.Sources.Count);
        AreEqual(22, normalizedCalculated.Sources[0].EntityId);
        AreEqual(33, normalizedCalculated.Sources[1].EntityId);
        AreEqual(22, normalizedCalculated.EntityId);
        AreEqual("2 QUELLEN · SUMME", normalizedCalculated.EntityTitle);
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
        AreEqual(19, configuration.SchemaVersion);
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

        var orphaned = new UnmaConfiguration
        {
            Panels = new List<PanelDefinition>
            {
                new()
                {
                    Id = "home-only",
                    Name = "HOME",
                    IsDashboard = true,
                },
            },
            Rules = new List<AlarmRuleDefinition>
            {
                new()
                {
                    Id = "orphaned-rule",
                    PanelId = "missing-panel",
                    Name = "ORPHANED",
                },
            },
        };
        orphaned.Normalize();
        AreEqual(2, orphaned.Panels.Count);
        var repairedPanel = orphaned.Panels.Single(panel =>
            !panel.IsDashboard);
        AreEqual(repairedPanel.Id, orphaned.Rules[0].PanelId);
        IsTrue(PanelTopologyPolicy.GetRulePanelIds(
            orphaned.Rules[0],
            orphaned.Panels).Count > 0);
    }

    private static void TestPanelClonePolicy()
    {
        var source = new PanelDefinition
        {
            Id = "source-panel",
            Name = " SUPPLY ",
            Columns = 5,
            IncludeVanilla = false,
            IncludeSystem = true,
            NotificationFilter = "food, workers",
            Slots = new List<PanelSlotDefinition>
            {
                new()
                {
                    AlarmId = "system:food",
                    DisplayName = "FOOD",
                    Detail = "12 months",
                    Source = "system",
                    Severity = AlarmSeverity.Warning,
                    ActiveColor = "#AA5500",
                },
                new()
                {
                    AlarmId = "rule:primary-rule",
                    DisplayName = "STALE PRIMARY SLOT",
                    Detail = "stale",
                    Source = "custom",
                    Severity = AlarmSeverity.Notice,
                    ActiveColor = "#000000",
                },
                new()
                {
                    AlarmId = "external:weather",
                    DisplayName = "WEATHER",
                    Detail = "storm",
                    Source = "external",
                    Severity = AlarmSeverity.Critical,
                    ActiveColor = "#334455",
                },
                new()
                {
                    AlarmId = "rule:linked-rule",
                    DisplayName = "STALE LINKED SLOT",
                    Source = "custom",
                },
                new()
                {
                    AlarmId = "rule:missing-rule",
                    DisplayName = "ORPHAN",
                    Source = "custom",
                },
                new()
                {
                    AlarmId = "rule:primary-rule",
                    DisplayName = "DUPLICATE",
                    Source = "custom",
                },
                new()
                {
                    AlarmId = "rule:   ",
                    DisplayName = "MALFORMED",
                    Source = "custom",
                },
                null,
            },
            ExcludedAlarmIds = new List<string>
            {
                " system:workers ",
                "system:workers",
                "rule:primary-rule",
                "rule:missing-rule",
                " ",
            },
        };
        var copyOne = new PanelDefinition
        {
            Id = "copy-one",
            Name = "SUPPLY COPY",
        };
        var copyTwo = new PanelDefinition
        {
            Id = "copy-two",
            Name = "supply copy 2",
        };
        var other = new PanelDefinition
        {
            Id = "other-panel",
            Name = "OTHER",
        };
        var primaryRule = new AlarmRuleDefinition
        {
            Id = "primary-rule",
            PanelId = source.Id,
            Name = "PRIMARY LOW",
            Severity = AlarmSeverity.Emergency,
            Logic = AlarmLogic.Any,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    EntityId = 42,
                    EntityTitle = "Storage 42",
                    EntityType = "Storage",
                    MetricPath = "products.amount",
                    MetricLabel = "Amount",
                    Comparison = ComparisonOperator.LessOrEqual,
                    Threshold = 12.5d,
                    Hysteresis = 1.75d,
                    ExpectedProductId = "AirSeparator",
                    EntityPrototypeId = "AirStorageT3",
                    ValueMode = ConditionValueMode.PercentOfReference,
                    ReferenceMetricPath = "products.capacity",
                    ReferenceMetricLabel = "Capacity",
                    InstrumentId = "instrument-7",
                    TrendMode = InstrumentTrendMode.DecreasePercent,
                    WindowSeconds = 720,
                    DeltaThreshold = 4.25d,
                    WindowAmount = 3,
                    WindowUnit = GameTimeUnit.Year,
                },
            },
            ActiveColor = "#CC2200",
            SoundId = "mechanical-siren",
            Enabled = true,
            AutoAcknowledgeOnClear = true,
            LinkedPanelIds = new List<string> { other.Id },
            ActivationDelayTicks = 120,
            ResetDelayTicks = 240,
            MinimumActiveTicks = 360,
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = false,
                AfterTicks = 720,
                Severity = AlarmSeverity.Emergency,
                SoundId = "siren",
                OperatorAction = AlarmOperatorAction.OpenPanel,
            },
        };
        var linkedRule = new AlarmRuleDefinition
        {
            Id = "linked-rule",
            PanelId = other.Id,
            Name = "LINKED HIGH",
            Severity = AlarmSeverity.Critical,
            Logic = AlarmLogic.All,
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    MetricPath = "health.value",
                    Comparison = ComparisonOperator.Greater,
                    Threshold = 90d,
                },
            },
            ActiveColor = "#FF4400",
            SoundId = "bell",
            Enabled = true,
            LinkedPanelIds = new List<string> { " source-panel " },
            Escalation = new AlarmEscalationDefinition
            {
                Enabled = true,
                AfterTicks = 480,
                Severity = AlarmSeverity.Emergency,
                SoundId = "auto",
                OperatorAction =
                    AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            },
        };
        var missingSlotRule = new AlarmRuleDefinition
        {
            Id = "missing-slot-rule",
            PanelId = source.Id,
            Name = "APPENDED RULE",
            Severity = AlarmSeverity.Notice,
            Enabled = true,
        };
        var unrelatedRule = new AlarmRuleDefinition
        {
            Id = "unrelated-rule",
            PanelId = other.Id,
            Name = "UNRELATED",
            LinkedPanelIds = new List<string>(),
        };
        var panels = new[] { source, copyOne, copyTwo, other };
        var rules = new[]
        {
            primaryRule,
            linkedRule,
            missingSlotRule,
            unrelatedRule,
        };

        IsTrue(PanelClonePolicy.CanClone(source));
        IsFalse(PanelClonePolicy.CanClone(null));
        IsFalse(PanelClonePolicy.CanClone(new PanelDefinition
        {
            Id = "dashboard",
            IsDashboard = true,
        }));
        IsFalse(PanelClonePolicy.CanClone(new PanelDefinition
        {
            Id = "entity-panel",
            OwnerEntityId = 7,
        }));
        AreEqual("SUPPLY COPY 3", PanelClonePolicy.CreateCopyName(
            source,
            panels));
        AreEqual("PANEL COPY", PanelClonePolicy.CreateCopyName(
            " ",
            Array.Empty<PanelDefinition>()));

        var generatedIds = new Queue<string>(new[]
        {
            " source-panel ",
            " ",
            " clone-panel ",
            "primary-rule",
            "clone-primary",
            "clone-linked",
            "clone-appended",
        });
        IsTrue(PanelClonePolicy.TryCreatePlan(
            source,
            panels,
            rules,
            () => generatedIds.Dequeue(),
            out var plan,
            out var failure));
        AreEqual(PanelCloneFailure.None, failure);
        IsTrue(plan != null);
        AreEqual("clone-panel", plan.Panel.Id);
        AreEqual("SUPPLY COPY 3", plan.Panel.Name);
        AreEqual(5, plan.Panel.Columns);
        IsFalse(plan.Panel.IncludeVanilla);
        IsTrue(plan.Panel.IncludeSystem);
        AreEqual("food, workers", plan.Panel.NotificationFilter);
        IsFalse(plan.Panel.IsDashboard);
        AreEqual(-1, plan.Panel.OwnerEntityId);
        AreEqual("", plan.Panel.OwnerEntityTitle);
        AreEqual("", plan.Panel.OwnerEntityPrototypeId);
        AreEqual("", plan.Panel.OwnerEntityType);
        AreEqual(3, plan.SkippedRuleSlotCount);
        AreEqual(3, plan.OrphanRuleSlotCount);
        AreEqual(3, plan.Rules.Count);
        AreEqual(3, plan.RuleIdMap.Count);
        AreEqual("clone-primary", plan.RuleIdMap["primary-rule"]);
        AreEqual("clone-linked", plan.RuleIdMap["linked-rule"]);
        AreEqual("clone-appended", plan.RuleIdMap["missing-slot-rule"]);
        IsFalse(plan.RuleIdMap.ContainsKey("unrelated-rule"));

        AreEqual(5, plan.Panel.Slots.Count);
        AreEqual("system:food", plan.Panel.Slots[0].AlarmId);
        AreEqual("rule:clone-primary", plan.Panel.Slots[1].AlarmId);
        AreEqual("PRIMARY LOW", plan.Panel.Slots[1].DisplayName);
        AreEqual(AlarmSeverity.Emergency, plan.Panel.Slots[1].Severity);
        AreEqual("#CC2200", plan.Panel.Slots[1].ActiveColor);
        AreEqual("external:weather", plan.Panel.Slots[2].AlarmId);
        AreEqual("rule:clone-linked", plan.Panel.Slots[3].AlarmId);
        AreEqual("LINKED HIGH", plan.Panel.Slots[3].DisplayName);
        AreEqual("rule:clone-appended", plan.Panel.Slots[4].AlarmId);
        AreEqual("APPENDED RULE", plan.Panel.Slots[4].DisplayName);
        IsFalse(plan.Panel.Slots.Exists(slot =>
            slot.AlarmId == "rule:missing-rule"));
        IsFalse(plan.Panel.Slots.Exists(slot =>
            string.Equals(
                slot.AlarmId?.Trim(),
                "rule:",
                StringComparison.Ordinal)));
        AreEqual(1, plan.Panel.Slots.Count(slot =>
            slot.AlarmId == "rule:clone-primary"));
        AreEqual(1, plan.Panel.ExcludedAlarmIds.Count);
        AreEqual("system:workers", plan.Panel.ExcludedAlarmIds[0]);

        var clonedPrimary = plan.Rules[0];
        AreEqual("clone-primary", clonedPrimary.Id);
        AreEqual(plan.Panel.Id, clonedPrimary.PanelId);
        AreEqual(primaryRule.Name, clonedPrimary.Name);
        AreEqual(primaryRule.Severity, clonedPrimary.Severity);
        AreEqual(primaryRule.Logic, clonedPrimary.Logic);
        AreEqual(primaryRule.ActiveColor, clonedPrimary.ActiveColor);
        AreEqual(primaryRule.SoundId, clonedPrimary.SoundId);
        IsFalse(clonedPrimary.Enabled);
        IsTrue(clonedPrimary.AutoAcknowledgeOnClear);
        AreEqual(
            primaryRule.ActivationDelayTicks,
            clonedPrimary.ActivationDelayTicks);
        AreEqual(primaryRule.ResetDelayTicks, clonedPrimary.ResetDelayTicks);
        AreEqual(
            primaryRule.MinimumActiveTicks,
            clonedPrimary.MinimumActiveTicks);
        IsFalse(ReferenceEquals(
            primaryRule.Escalation,
            clonedPrimary.Escalation));
        AreEqual(
            primaryRule.Escalation.Enabled,
            clonedPrimary.Escalation.Enabled);
        AreEqual(
            primaryRule.Escalation.AfterTicks,
            clonedPrimary.Escalation.AfterTicks);
        AreEqual(
            primaryRule.Escalation.Severity,
            clonedPrimary.Escalation.Severity);
        AreEqual(
            primaryRule.Escalation.SoundId,
            clonedPrimary.Escalation.SoundId);
        AreEqual(
            primaryRule.Escalation.OperatorAction,
            clonedPrimary.Escalation.OperatorAction);
        AreEqual(0, clonedPrimary.LinkedPanelIds.Count);
        AreEqual(1, clonedPrimary.Conditions.Count);
        IsFalse(ReferenceEquals(primaryRule, clonedPrimary));
        IsFalse(ReferenceEquals(
            primaryRule.Conditions,
            clonedPrimary.Conditions));
        IsFalse(ReferenceEquals(
            primaryRule.Conditions[0],
            clonedPrimary.Conditions[0]));
        var clonedCondition = clonedPrimary.Conditions[0];
        var sourceCondition = primaryRule.Conditions[0];
        AreEqual(sourceCondition.EntityId, clonedCondition.EntityId);
        AreEqual(sourceCondition.EntityTitle, clonedCondition.EntityTitle);
        AreEqual(sourceCondition.EntityType, clonedCondition.EntityType);
        AreEqual(sourceCondition.MetricPath, clonedCondition.MetricPath);
        AreEqual(sourceCondition.MetricLabel, clonedCondition.MetricLabel);
        AreEqual(sourceCondition.Comparison, clonedCondition.Comparison);
        AreEqual(sourceCondition.Threshold, clonedCondition.Threshold);
        AreEqual(sourceCondition.Hysteresis, clonedCondition.Hysteresis);
        AreEqual(
            sourceCondition.ExpectedProductId,
            clonedCondition.ExpectedProductId);
        AreEqual(
            sourceCondition.EntityPrototypeId,
            clonedCondition.EntityPrototypeId);
        AreEqual(sourceCondition.ValueMode, clonedCondition.ValueMode);
        AreEqual(
            sourceCondition.ReferenceMetricPath,
            clonedCondition.ReferenceMetricPath);
        AreEqual(
            sourceCondition.ReferenceMetricLabel,
            clonedCondition.ReferenceMetricLabel);
        AreEqual(sourceCondition.InstrumentId, clonedCondition.InstrumentId);
        AreEqual(sourceCondition.TrendMode, clonedCondition.TrendMode);
        AreEqual(sourceCondition.WindowSeconds, clonedCondition.WindowSeconds);
        AreEqual(sourceCondition.DeltaThreshold, clonedCondition.DeltaThreshold);
        AreEqual(sourceCondition.WindowAmount, clonedCondition.WindowAmount);
        AreEqual(sourceCondition.WindowUnit, clonedCondition.WindowUnit);
        foreach (var clonedRule in plan.Rules)
        {
            IsFalse(clonedRule.Enabled);
            AreEqual(plan.Panel.Id, clonedRule.PanelId);
            AreEqual(0, clonedRule.LinkedPanelIds.Count);
        }
        var clonedLinked = plan.Rules.Single(rule =>
            rule.Id == "clone-linked");
        IsFalse(ReferenceEquals(
            linkedRule.Escalation,
            clonedLinked.Escalation));
        IsTrue(clonedLinked.Escalation.Enabled);
        AreEqual(480, clonedLinked.Escalation.AfterTicks);
        AreEqual(
            AlarmSeverity.Emergency,
            clonedLinked.Escalation.Severity);
        AreEqual("auto", clonedLinked.Escalation.SoundId);
        AreEqual(
            AlarmOperatorAction.OpenPanelAndCancelTemporaryMute,
            clonedLinked.Escalation.OperatorAction);

        IsFalse(ReferenceEquals(source.Slots, plan.Panel.Slots));
        IsFalse(ReferenceEquals(source.Slots[0], plan.Panel.Slots[0]));
        IsFalse(ReferenceEquals(
            source.ExcludedAlarmIds,
            plan.Panel.ExcludedAlarmIds));
        plan.Panel.Slots[0].DisplayName = "CLONE FOOD";
        plan.Panel.ExcludedAlarmIds.Add("system:maintenance");
        clonedCondition.Threshold = 1d;
        clonedCondition.Hysteresis = 0.25d;
        clonedPrimary.ActivationDelayTicks = 1;
        clonedPrimary.Escalation.AfterTicks = 1;
        clonedLinked.Escalation.SoundId = "clone-only-sound";
        clonedPrimary.LinkedPanelIds.Add("clone-only-link");
        AreEqual("FOOD", source.Slots[0].DisplayName);
        AreEqual(5, source.ExcludedAlarmIds.Count);
        AreEqual(12.5d, primaryRule.Conditions[0].Threshold);
        AreEqual(1.75d, primaryRule.Conditions[0].Hysteresis);
        AreEqual(120, primaryRule.ActivationDelayTicks);
        AreEqual(720, primaryRule.Escalation.AfterTicks);
        AreEqual("auto", linkedRule.Escalation.SoundId);
        AreEqual(1, primaryRule.LinkedPanelIds.Count);

        var dashboardCalls = 0;
        IsFalse(PanelClonePolicy.TryCreatePlan(
            new PanelDefinition { Id = "home", IsDashboard = true },
            panels,
            rules,
            () =>
            {
                dashboardCalls++;
                return "unused";
            },
            out var dashboardPlan,
            out failure));
        AreEqual(PanelCloneFailure.DashboardNotSupported, failure);
        AreEqual(0, dashboardCalls);
        IsTrue(dashboardPlan == null);
        IsFalse(PanelClonePolicy.TryCreatePlan(
            new PanelDefinition { Id = "entity", OwnerEntityId = 9 },
            panels,
            rules,
            () => "unused",
            out _,
            out failure));
        AreEqual(PanelCloneFailure.EntityPanelNotSupported, failure);
        IsFalse(PanelClonePolicy.TryCreatePlan(
            null,
            panels,
            rules,
            () => "unused",
            out _,
            out failure));
        AreEqual(PanelCloneFailure.InvalidSource, failure);

        var exhaustedCalls = 0;
        IsFalse(PanelClonePolicy.TryCreatePlan(
            new PanelDefinition { Id = "collision" },
            Array.Empty<PanelDefinition>(),
            Array.Empty<AlarmRuleDefinition>(),
            () =>
            {
                exhaustedCalls++;
                return "collision";
            },
            out _,
            out failure));
        AreEqual(PanelCloneFailure.IdGenerationFailed, failure);
        AreEqual(128, exhaustedCalls);
        IsFalse(PanelClonePolicy.TryCreatePlan(
            new PanelDefinition { Id = "throwing" },
            Array.Empty<PanelDefinition>(),
            Array.Empty<AlarmRuleDefinition>(),
            () => throw new InvalidOperationException("generator failed"),
            out _,
            out failure));
        AreEqual(PanelCloneFailure.IdGenerationFailed, failure);

        var duplicateRule = new AlarmRuleDefinition
        {
            Id = primaryRule.Id,
            PanelId = source.Id,
        };
        IsFalse(PanelClonePolicy.TryCreatePlan(
            source,
            panels,
            new[] { primaryRule, duplicateRule },
            () => "unused",
            out _,
            out failure));
        AreEqual(PanelCloneFailure.InvalidSourceData, failure);

        var configuration = new UnmaConfiguration
        {
            Panels = new List<PanelDefinition>
            {
                new()
                {
                    Id = "home",
                    Name = "HOME",
                    IsDashboard = true,
                },
                source,
                copyOne,
                copyTwo,
                other,
                plan.Panel,
            },
            Rules = rules.Concat(plan.Rules).ToList(),
        };
        var schemaVersion = configuration.SchemaVersion;
        configuration.Normalize();
        AreEqual(schemaVersion, configuration.SchemaVersion);
        var normalizedClone = configuration.Panels.Single(panel =>
            panel.Id == plan.Panel.Id);
        AreEqual(3, configuration.Rules.Count(rule =>
            rule.PanelId == normalizedClone.Id));
        IsFalse(configuration.Rules.Where(rule =>
            rule.PanelId == normalizedClone.Id).Any(rule => rule.Enabled));
        IsFalse(configuration.Rules.Where(rule =>
            rule.PanelId == normalizedClone.Id).Any(rule =>
            rule.LinkedPanelIds.Count > 0));

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();
        AreEqual(schemaVersion, restored.SchemaVersion);
        var restoredClone = restored.Panels.Single(panel =>
            panel.Id == plan.Panel.Id);
        AreEqual("SUPPLY COPY 3", restoredClone.Name);
        AreEqual(3, restored.Rules.Count(rule =>
            rule.PanelId == restoredClone.Id));
        IsFalse(restored.Rules.Where(rule =>
            rule.PanelId == restoredClone.Id).Any(rule => rule.Enabled));
        IsTrue(restoredClone.Slots.Exists(slot =>
            slot.AlarmId == "system:food"));
        IsTrue(restoredClone.Slots.Exists(slot =>
            slot.AlarmId == "rule:clone-primary"));
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
        var computedRule = new AlarmRuleDefinition
        {
            Id = "computed-rule",
            Conditions = new List<ConditionDefinition>
            {
                new()
                {
                    EntityId = 2,
                    InstrumentId = "coal-total",
                },
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
                    computedRule,
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
                new[] { computedRule },
                new[] { 2 })
            .Count);

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

        IsTrue(VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            VanillaNotificationBehavior.Hidden,
            isEntityPanel: false,
            belongsToEntityPanel: false));
        IsTrue(VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            VanillaNotificationBehavior.Hidden,
            isEntityPanel: true,
            belongsToEntityPanel: false));
        IsFalse(VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            VanillaNotificationBehavior.Hidden,
            isEntityPanel: true,
            belongsToEntityPanel: true));
        IsTrue(VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            VanillaNotificationBehavior.Ignored,
            isEntityPanel: true,
            belongsToEntityPanel: true));
        IsFalse(VanillaNotificationSuppressionPolicy.IsHiddenFromPanel(
            VanillaNotificationBehavior.Silent,
            isEntityPanel: false,
            belongsToEntityPanel: false));

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
        AreEqual(19, legacyConfig.SchemaVersion);
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
                EntityId = 17,
                EntityPrototypeId = "AirStorageT3",
                EntityTitle = "Kapitansburo II",
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
        AreEqual(17, projected[2].EntityId);
        AreEqual("AirStorageT3", projected[2].EntityPrototypeId);
        AreEqual("Kapitansburo II", projected[2].EntityTitle);

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
        editedSystemStage.ActivationDelayTicks = 11;
        editedSystemStage.ResetDelayTicks = 22;
        editedSystemStage.MinimumActiveTicks = 33;
        editedSystemStage.Conditions[0].Threshold = 7.5d;
        editedSystemStage.Conditions[0].Hysteresis = 1.25d;
        configuration.Rules.Add(new AlarmRuleDefinition
        {
            PanelId = fixedPanel.Id,
            Name = "LAGER UND BAND LEER",
            Logic = AlarmLogic.All,
            AutoAcknowledgeOnClear = true,
            ActivationDelayTicks = 44,
            ResetDelayTicks = 55,
            MinimumActiveTicks = 66,
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
                    Hysteresis = 0.5d,
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
                new()
                {
                    InstrumentId = "coal-storage-17",
                    MetricLabel = "Kohlelager gesamt",
                    TrendMode = InstrumentTrendMode.DecreasePercent,
                    WindowSeconds = 300,
                    DeltaThreshold = 12.5d,
                    WindowAmount = 2,
                    WindowUnit = GameTimeUnit.Year,
                    Hysteresis = 0.75d,
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
        configuration.Instruments.Add(new InstrumentDefinition
        {
            Id = "coal-storage-17",
            Title = "KOHLE NORD",
            DisplayType = InstrumentDisplayType.EdgewiseVertical,
            EntityId = 17,
            EntityTitle = "Kohlelager Nord",
            EntityPrototypeId = "AirStorageT1",
            MetricPath = "$stored.percent",
            MetricLabel = "Füllstand",
            Unit = "%",
            Minimum = 0d,
            Maximum = 1000d,
            Aggregation = InstrumentAggregationMode.Sum,
            HistoryDurationSeconds = 7200,
            Sources = new List<InstrumentSourceDefinition>
            {
                new()
                {
                    EntityId = 17,
                    EntityTitle = "Kohlelager Nord",
                    EntityPrototypeId = "AirStorageT1",
                },
                new()
                {
                    EntityId = 27,
                    EntityTitle = "Kohlelager Süd",
                    EntityPrototypeId = "AirStorageT1",
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
        AreEqual(4, restored.Rules[0].Conditions.Count);
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
        AreEqual(19, restored.SchemaVersion);
        AreEqual(1, restored.InstrumentPanels.Count);
        AreEqual("instruments-main", restored.InstrumentPanels[0].Id);
        AreEqual(1, restored.Instruments.Count);
        AreEqual("KOHLE NORD", restored.Instruments[0].Title);
        AreEqual(
            InstrumentDisplayType.EdgewiseVertical,
            restored.Instruments[0].DisplayType);
        AreEqual("$stored.percent", restored.Instruments[0].MetricPath);
        AreEqual("instruments-main", restored.Instruments[0].PanelId);
        AreEqual(
            InstrumentAggregationMode.Sum,
            restored.Instruments[0].Aggregation);
        AreEqual(2, restored.Instruments[0].Sources.Count);
        AreEqual(27, restored.Instruments[0].Sources[1].EntityId);
        AreEqual(7200, restored.Instruments[0].HistoryDurationSeconds);
        AreEqual(
            "coal-storage-17",
            restored.Rules[0].Conditions[3].InstrumentId);
        AreEqual(
            InstrumentTrendMode.DecreasePercent,
            restored.Rules[0].Conditions[3].TrendMode);
        AreEqual(300, restored.Rules[0].Conditions[3].WindowSeconds);
        AreEqual(12.5d, restored.Rules[0].Conditions[3].DeltaThreshold);
        AreEqual(2, restored.Rules[0].Conditions[3].WindowAmount);
        AreEqual(
            GameTimeUnit.Year,
            restored.Rules[0].Conditions[3].WindowUnit);
        AreEqual(100, restored.Instruments[0].HistoryDurationAmount);
        AreEqual(
            GameTimeUnit.Year,
            restored.Instruments[0].HistoryDurationUnit);
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
        AreEqual(44, restored.Rules[0].ActivationDelayTicks);
        AreEqual(55, restored.Rules[0].ResetDelayTicks);
        AreEqual(66, restored.Rules[0].MinimumActiveTicks);
        AreEqual(0.5d, restored.Rules[0].Conditions[0].Hysteresis);
        AreEqual(0.75d, restored.Rules[0].Conditions[3].Hysteresis);
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
        AreEqual(11, restoredSystemStage.ActivationDelayTicks);
        AreEqual(22, restoredSystemStage.ResetDelayTicks);
        AreEqual(33, restoredSystemStage.MinimumActiveTicks);
        AreEqual(1.25d, restoredSystemStage.Conditions[0].Hysteresis);
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

        var timed = new AlarmHistoryDefinition
        {
            RaisedAtTicks = 20d,
        };
        IsFalse(timed.SetState(false, false, 20d));
        AreEqual(20d, timed.RaisedAtTicks);
        IsTrue(timed.SetState(false, true, 25d));
        AreEqual(25d, timed.AcknowledgedAtTicks);
        IsTrue(timed.SetState(true, false, 30d));
        AreEqual(30d, timed.ClearedAtTicks);
        IsTrue(timed.SetState(false, false, 40d));
        AreEqual(0d, timed.ClearedAtTicks);
        AreEqual("KQ", timed.StateCode);

        IsFalse(GameTimeStampPolicy.TryGetDate(0d, out _));
        IsFalse(GameTimeStampPolicy.TryGetDate(double.NaN, out _));
        IsTrue(GameTimeStampPolicy.TryGetDate(20d, out var firstDate));
        AreEqual(1, firstDate.Year);
        AreEqual(1, firstDate.Month);
        AreEqual(2, firstDate.Day);
        AreEqual(0, firstDate.TickOfDay);
        IsTrue(GameTimeStampPolicy.TryGetDate(7205d, out var secondYear));
        AreEqual(2, secondYear.Year);
        AreEqual(1, secondYear.Month);
        AreEqual(1, secondYear.Day);
        AreEqual(5, secondYear.TickOfDay);
        AreEqual(25d, GameTimeStampPolicy.LatestEventTicks(timed));
        AreEqual(0d, GameTimeStampPolicy.LatestEventTicks(null));
    }

    private static void TestAlarmHistoryQueryAndExport()
    {
        var incoming = new AlarmHistoryDefinition
        {
            Sequence = 20,
            AlarmKey = "system:food",
            Message = "Tank, \"North\"",
            Detail = "line1\r\nline2",
            Source = "system",
            PanelId = "supply",
            Severity = AlarmSeverity.Warning,
            RaisedAtTicks = 50.5d,
        };
        var acknowledged = new AlarmHistoryDefinition
        {
            Sequence = 30,
            AlarmKey = "rule:workers",
            Message = "ARBEITER \u00DCBERLASTET",
            Detail = "Schichtreserve niedrig",
            Source = "custom",
            PanelId = "labor",
            Severity = AlarmSeverity.Critical,
            IsAcknowledged = true,
            RaisedAtTicks = 100d,
            AcknowledgedAtTicks = 110.25d,
        };
        var gone = new AlarmHistoryDefinition
        {
            Sequence = 20,
            AlarmKey = "vanilla:pump",
            Message = "PUMPE GESTOPPT",
            Detail = "Storage Hall",
            Source = "vanilla",
            PanelId = "factory",
            Severity = AlarmSeverity.Emergency,
            IsGone = true,
            RaisedAtTicks = 20d,
            ClearedAtTicks = 25d,
        };
        var completed = new AlarmHistoryDefinition
        {
            Sequence = 10,
            AlarmKey = "external:done",
            Message = "ABGESCHLOSSEN",
            Detail = "Provider event",
            Source = "external",
            PanelId = "export",
            Severity = AlarmSeverity.Warning,
            IsGone = true,
            IsAcknowledged = true,
            RaisedAtTicks = 1d,
            ClearedAtTicks = 2d,
            AcknowledgedAtTicks = 3d,
        };
        var history = new AlarmHistoryDefinition[]
        {
            incoming,
            null,
            completed,
            acknowledged,
            gone,
        };

        var all = new AlarmHistoryQuery().Apply(history);
        AreEqual(4, all.Count);
        IsTrue(ReferenceEquals(acknowledged, all[0]));
        IsTrue(ReferenceEquals(incoming, all[1]));
        IsTrue(ReferenceEquals(gone, all[2]));
        IsTrue(ReferenceEquals(completed, all[3]));
        AreEqual(0, new AlarmHistoryQuery().Apply(null).Count);

        AreEqual(1, new AlarmHistoryQuery
        {
            SearchText = "tank,",
        }.Apply(history).Count);
        AreEqual(1, new AlarmHistoryQuery
        {
            SearchText = "LINE2",
        }.Apply(history).Count);
        AreEqual(1, new AlarmHistoryQuery
        {
            SearchText = "VANILLA",
        }.Apply(history).Count);
        AreEqual(1, new AlarmHistoryQuery
        {
            SearchText = "FACTORY",
        }.Apply(history).Count);
        AreEqual(1, new AlarmHistoryQuery
        {
            SearchText = "EXTERNAL:DONE",
        }.Apply(history).Count);
        AreEqual(4, new AlarmHistoryQuery
        {
            SearchText = "   ",
        }.Apply(history).Count);

        AreEqual(3, new AlarmHistoryQuery
        {
            StateFilter = AlarmHistoryStateFilter.Open,
        }.Apply(history).Count);
        AreEqual(1, new AlarmHistoryQuery
        {
            StateFilter = AlarmHistoryStateFilter.Completed,
        }.Apply(history).Count);
        foreach (var stateFilter in new[]
                 {
                     AlarmHistoryStateFilter.K,
                     AlarmHistoryStateFilter.KQ,
                     AlarmHistoryStateFilter.KG,
                     AlarmHistoryStateFilter.KGQ,
                 })
        {
            var filtered = new AlarmHistoryQuery
            {
                StateFilter = stateFilter,
            }.Apply(history);
            AreEqual(1, filtered.Count);
            AreEqual(stateFilter.ToString(), filtered[0].StateCode);
        }
        AreEqual(2, new AlarmHistoryQuery
        {
            SeverityFilter = AlarmSeverity.Warning,
        }.Apply(history).Count);
        var combined = new AlarmHistoryQuery
        {
            SearchText = "storage",
            StateFilter = AlarmHistoryStateFilter.KG,
            SeverityFilter = AlarmSeverity.Emergency,
        }.Apply(history);
        AreEqual(1, combined.Count);
        IsTrue(ReferenceEquals(gone, combined[0]));

        var csv = AlarmHistoryExport.ToCsv(history);
        IsTrue(csv.StartsWith(
            "Sequence,State,Severity,RaisedAtTicks,ClearedAtTicks," +
            "AcknowledgedAtTicks,Message,Detail,Source,PanelId,AlarmKey\r\n",
            StringComparison.Ordinal));
        IsTrue(csv.Contains(
            "20,K,Warning,50.5,0,0,\"Tank, \"\"North\"\"\"," +
            "\"line1\r\nline2\",system,supply,system:food\r\n",
            StringComparison.Ordinal));
        IsTrue(csv.IndexOf("rule:workers", StringComparison.Ordinal) <
               csv.IndexOf("system:food", StringComparison.Ordinal));
        IsTrue(csv.IndexOf("system:food", StringComparison.Ordinal) <
               csv.IndexOf("vanilla:pump", StringComparison.Ordinal));
        AreEqual(
            "Sequence,State,Severity,RaisedAtTicks,ClearedAtTicks," +
            "AcknowledgedAtTicks,Message,Detail,Source,PanelId,AlarmKey\r\n",
            AlarmHistoryExport.ToCsv(null));

        using (var json = JsonDocument.Parse(
                   AlarmHistoryExport.ToJson(history)))
        {
            var rows = json.RootElement;
            AreEqual(JsonValueKind.Array, rows.ValueKind);
            AreEqual(4, rows.GetArrayLength());
            AreEqual(30L, rows[0].GetProperty("sequence").GetInt64());
            AreEqual("KQ", rows[0].GetProperty("state").GetString());
            AreEqual(
                "Critical",
                rows[0].GetProperty("severity").GetString());
            AreClose(
                100d,
                rows[0].GetProperty("raised_at_ticks").GetDouble());
            AreClose(
                110.25d,
                rows[0].GetProperty("acknowledged_at_ticks").GetDouble());
            AreEqual(
                "ARBEITER \u00DCBERLASTET",
                rows[0].GetProperty("message").GetString());
            AreEqual(
                "factory",
                rows[2].GetProperty("panel_id").GetString());
            AreEqual(
                "external:done",
                rows[3].GetProperty("alarm_key").GetString());
        }
        using (var emptyJson = JsonDocument.Parse(
                   AlarmHistoryExport.ToJson(null)))
        {
            AreEqual(0, emptyJson.RootElement.GetArrayLength());
        }
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
            RaisedAtTicks = 120d,
            ClearedAtTicks = 180d,
            AcknowledgedAtTicks = 190d,
        });

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, configuration);
        stream.Position = 0;
        var restored = (UnmaConfiguration)serializer.ReadObject(stream);
        restored.Normalize();

        AreEqual(19, restored.SchemaVersion);
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
        AreEqual(120d, history.RaisedAtTicks);
        AreEqual(180d, history.ClearedAtTicks);
        AreEqual(190d, history.AcknowledgedAtTicks);
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

        AreEqual(19, oldConfiguration.SchemaVersion);
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
        malformedCurrent.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 1,
            AlarmKey = "test:invalid-time",
            RaisedAtTicks = double.NaN,
            ClearedAtTicks = double.PositiveInfinity,
            AcknowledgedAtTicks = -1d,
        });
        malformedCurrent.Normalize();
        AreEqual(200, malformedCurrent.UiScalePercent);
        AreEqual(180f, malformedCurrent.EditorWindowX);
        AreEqual(110f, malformedCurrent.EditorWindowY);
        AreEqual(700f, malformedCurrent.EditorWindowWidth);
        AreEqual(520f, malformedCurrent.EditorWindowHeight);
        AreEqual(0d, malformedCurrent.AlarmHistory[0].RaisedAtTicks);
        AreEqual(0d, malformedCurrent.AlarmHistory[0].ClearedAtTicks);
        AreEqual(0d, malformedCurrent.AlarmHistory[0].AcknowledgedAtTicks);
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
        AreEqual(19, schemaSeven.SchemaVersion);
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
        AreEqual(19, legacy.SchemaVersion);
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

        AreEqual(19, versionFive.SchemaVersion);
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

        AreEqual(19, schemaEight.SchemaVersion);
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
