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
        TestInstrumentForecastPolicy();
        TestBooleanLogic();
        TestAlarmLatch();
        TestAlarmTimingPolicy();
        TestAlarmEscalationPolicy();
        TestAlarmAttentionQueuePolicy();
        TestAlarmTimingModelNormalization();
        TestAlarmEscalationModelNormalization();
        TestAlarmTimingMemoryPolicy();
        TestAlarmAudioSnoozePolicy();
        TestOperatorSilenceReminderPolicy();
        TestSustainedVanillaAlarmPolicy();
        TestGroupedVanillaNotificationPolicy();
        TestGroupedVanillaNotificationNormalization();
        TestVanillaNotificationSuppressionPolicy();
        TestIgnoredVanillaPersistenceCleanup();
        TestAlarmHistoryState();
        TestAlarmHistoryQueryAndExport();
        TestSystemAlarmSelection();
        TestSystemMetricMath();
        TestGlobalRuleMetricPaths();
        TestWindowResizeMath();
        TestMetricPickerFilter();
        TestPanelTopologyPolicy();
        TestAlarmAreaPolicy();
        TestAlarmIncidentPolicy();
        TestPanelClonePolicy();
        TestEntityVanillaSlotPolicy();
        TestCustomRuleLifecyclePolicy();
        TestPanelSlotProjection();
        TestConfigurationRoundTrip();
        TestAlarmMemoryOperatorSilenceRoundTrip();
        TestAlarmHistoryRoundTrip();
        TestConfigurationMigration();
        TestReducedMotionConfigurationContract();
        TestRecommendedQuietTransferProfile();
        TestTransferProfileRoundTripAndFilter();
        TestConfigurationTransferMerge();
        TestTransferProfileSemanticValidation();
        TestTransferProfileSchemaOneSystemAlarmContract();
        TestTransferProfileStoreRoundTripAndAtomicSave();
        TestTransferProfileStoreFutureAndCorruptProtection();
        TestStateStoreFutureSchemaProtection();
        TestMechanicalSiren();
        TestAlarmUiErgonomics();
        TestAlarmUiStructuralSmokeTests();
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

    private static void TestAlarmUiErgonomics()
    {
        AreEqual(4.5d, AlarmUiErgonomics.MinimumNormalTextContrast);

        IsTrue(AlarmUiErgonomics.IsValidHtmlColor("#000000"));
        IsTrue(AlarmUiErgonomics.IsValidHtmlColor("#abcdef"));
        IsTrue(AlarmUiErgonomics.IsValidHtmlColor("  #ABCDEF  "));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor(null));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor(""));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor("   "));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor("000000"));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor("#000"));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor("#00000000"));
        IsFalse(AlarmUiErgonomics.IsValidHtmlColor("#GG0000"));

        IsTrue(AlarmUiErgonomics.ShouldUseLightText(0d, 0d, 0d));
        IsFalse(AlarmUiErgonomics.ShouldUseLightText(1d, 1d, 1d));
        IsTrue(AlarmUiErgonomics.ShouldUseLightText(
            0x2B / 255d,
            0x2D / 255d,
            0x32 / 255d));
        IsFalse(AlarmUiErgonomics.ShouldUseLightText(
            0xF0 / 255d,
            0xC5 / 255d,
            0x41 / 255d));
        IsTrue(AlarmUiErgonomics.ShouldUseLightText(
            0xE5 / 255d,
            0x1B / 255d,
            0x23 / 255d));
        AreClose(21d, AlarmUiErgonomics.BestTextContrast(0d, 0d, 0d));
        AreClose(21d, AlarmUiErgonomics.BestTextContrast(1d, 1d, 1d));
        AreClose(
            AlarmUiErgonomics.BestTextContrast(0d, 0d, 0d),
            AlarmUiErgonomics.BestTextContrast(-1d, -1d, -1d));
        AreClose(
            AlarmUiErgonomics.BestTextContrast(1d, 1d, 1d),
            AlarmUiErgonomics.BestTextContrast(2d, 2d, 2d));

        var lowestSampledContrast = double.MaxValue;
        for (var red = 0; red <= 16; red++)
        {
            for (var green = 0; green <= 16; green++)
            {
                for (var blue = 0; blue <= 16; blue++)
                {
                    lowestSampledContrast = Math.Min(
                        lowestSampledContrast,
                        AlarmUiErgonomics.BestTextContrast(
                            red / 16d,
                            green / 16d,
                            blue / 16d));
                }
            }
        }
        IsTrue(lowestSampledContrast >=
               AlarmUiErgonomics.MinimumNormalTextContrast);

        IsTrue(AlarmUiErgonomics.CanSaveRule(
            "  LOW COAL  ",
            hasTargetPanel: true,
            conditionCount: 1,
            colorIsValid: true,
            timingIsValid: true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            " ", true, 1, true, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            null, true, 1, true, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            "LOW COAL", false, 1, true, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            "LOW COAL", true, 0, true, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            "LOW COAL", true, -1, true, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            "LOW COAL", true, 1, false, true));
        IsFalse(AlarmUiErgonomics.CanSaveRule(
            "LOW COAL", true, 1, true, false));
    }

    private static void TestAlarmUiStructuralSmokeTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        var editorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Ui",
            "UnmaOverlayController.cs"));
        var editorMethod = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmRuleEditor(bool inEntityWindow)");
        var titleField = editorMethod.IndexOf(
            "DrawAlarmTitleField();",
            StringComparison.Ordinal);
        var enabledField = editorMethod.IndexOf(
            "DrawAlarmEnabledField();",
            StringComparison.Ordinal);
        var targetPanel = editorMethod.IndexOf(
            "DrawTargetPanelSelector(inEntityWindow);",
            StringComparison.Ordinal);
        IsTrue(titleField >= 0);
        IsTrue(enabledField > titleField);
        IsTrue(targetPanel > enabledField);

        var enabledMethod = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmEnabledField()");
        IsTrue(enabledMethod.Contains(
            "m_draftEnabled = NativeGUILayout.Toggle(",
            StringComparison.Ordinal));

        var alarmProperties = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmProperties()");
        IsTrue(alarmProperties.Contains(
            "DrawAlarmAdvancedSection(sounds);",
            StringComparison.Ordinal));
        IsFalse(alarmProperties.Contains(
            "DrawAlarmTimingDraft();",
            StringComparison.Ordinal));
        IsFalse(alarmProperties.Contains(
            "DrawAlarmEscalationDraft(sounds);",
            StringComparison.Ordinal));
        var advancedSection = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmAdvancedSection(");
        var collapsedGate = advancedSection.IndexOf(
            "if (!m_ruleAdvancedOpen)",
            StringComparison.Ordinal);
        var advancedTiming = advancedSection.IndexOf(
            "DrawAlarmTimingDraft();",
            StringComparison.Ordinal);
        var advancedEscalation = advancedSection.IndexOf(
            "DrawAlarmEscalationDraft(sounds);",
            StringComparison.Ordinal);
        IsTrue(collapsedGate >= 0);
        IsTrue(advancedTiming > collapsedGate);
        IsTrue(advancedEscalation > advancedTiming);
        IsTrue(advancedSection.Contains(
            "new NativeControlMetadata(",
            StringComparison.Ordinal));
        IsTrue(advancedSection.Contains(
            "\"alarm-advanced-toggle\"",
            StringComparison.Ordinal));

        var editorBody = ExtractSourceMethod(
            editorSource,
            "private void DrawEditorBodyContent()");
        var status = editorBody.IndexOf(
            "DrawStatusMessage();",
            StringComparison.Ordinal);
        var scrollStart = editorBody.IndexOf(
            "NativeGUILayout.BeginScrollView",
            StringComparison.Ordinal);
        var scrollEnd = editorBody.LastIndexOf(
            "NativeGUILayout.EndScrollView();",
            StringComparison.Ordinal);
        var actions = editorBody.IndexOf(
            "DrawRuleEditorActions(",
            StringComparison.Ordinal);
        IsTrue(status >= 0 && status < scrollStart);
        IsTrue(scrollEnd > scrollStart);
        IsTrue(actions > scrollEnd);

        var editorActions = ExtractSourceMethod(
            editorSource,
            "private void DrawRuleEditorActions(");
        IsTrue(editorActions.Contains(
            "var extremeCompact =",
            StringComparison.Ordinal));
        IsTrue(editorActions.Contains(
            "m_entityAlarmWindowRect.height / Math.Max(0.75f, UiScale) < 600f",
            StringComparison.Ordinal));
        IsTrue(editorActions.Contains(
            "\"alarm-editor-save-compact\"",
            StringComparison.Ordinal));

        var validation = ExtractSourceMethod(
            editorSource,
            "private string GetRuleDraftValidationMessage()");
        IsTrue(validation.Contains(
            "string.IsNullOrWhiteSpace(m_draftRuleName)",
            StringComparison.Ordinal));
        IsTrue(validation.Contains(
            "GetDraftTargetPanel() == null",
            StringComparison.Ordinal));
        IsTrue(validation.Contains(
            "m_draftConditions.Count == 0",
            StringComparison.Ordinal));
        IsTrue(validation.Contains(
            "AlarmUiErgonomics.IsValidHtmlColor(m_draftColor)",
            StringComparison.Ordinal));
        IsTrue(validation.Contains(
            "TryGetTimingTicks(m_draftActivationDelay",
            StringComparison.Ordinal));

        var saveDraft = ExtractSourceMethod(
            editorSource,
            "private bool SaveDraftRule(IReadOnlyList<SoundOption> sounds)");
        IsTrue(saveDraft.Contains(
            "GetRuleDraftValidationMessage()",
            StringComparison.Ordinal));
        IsTrue(saveDraft.Contains(
            "StatusSeverity.Error, true",
            StringComparison.Ordinal));
        IsTrue(saveDraft.Contains(
            "Name = m_draftRuleName.Trim()",
            StringComparison.Ordinal));
        IsTrue(saveDraft.Contains(
            "Enabled = m_draftEnabled",
            StringComparison.Ordinal));

        var beginEditing = ExtractSourceMethod(
            editorSource,
            "private void BeginEditingRule(");
        IsTrue(beginEditing.Contains(
            "m_draftEnabled = rule.Enabled;",
            StringComparison.Ordinal));
        IsTrue(beginEditing.Contains(
            "m_ruleAdvancedOpen = false;",
            StringComparison.Ordinal));
        var resetDraft = ExtractSourceMethod(
            editorSource,
            "private void ResetDraftRule()");
        IsTrue(resetDraft.Contains(
            "m_draftEnabled = true;",
            StringComparison.Ordinal));
        IsTrue(resetDraft.Contains(
            "m_ruleAdvancedOpen = false;",
            StringComparison.Ordinal));

        var mainTab = ExtractSourceMethod(
            editorSource,
            "private void DrawSelectedMainTab()");
        var silenceReminderGate = mainTab.IndexOf(
            "m_operatorSilenceReminder != null",
            StringComparison.Ordinal);
        var silenceReminderDraw = mainTab.IndexOf(
            "DrawOperatorSilenceReminder();",
            StringComparison.Ordinal);
        var mainStatus = mainTab.IndexOf(
            "DrawStatusMessage();",
            StringComparison.Ordinal);
        var mainSwitch = mainTab.IndexOf(
            "switch (m_tab)",
            StringComparison.Ordinal);
        IsTrue(silenceReminderGate >= 0);
        IsTrue(silenceReminderDraw > silenceReminderGate);
        IsTrue(mainStatus > silenceReminderDraw);
        IsTrue(mainStatus >= 0 && mainStatus < mainSwitch);
        var detachedPanel = ExtractSourceMethod(
            editorSource,
            "private void DrawDetachedPanelContent(");
        IsTrue(detachedPanel.Contains(
            "DrawStatusMessage();",
            StringComparison.Ordinal));

        var alarmTile = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmTile(");
        IsTrue(alarmTile.Contains(
            "m_runtime.Configuration.ReducedMotion",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "AlarmUiErgonomics.ShouldUseLightText(",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "alarm.IsActive && alarm.IsOperatorSilenced",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "\"alarm_tile.behavior_silent\"",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "var acknowledgementInset =",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "alarm.RequiresAcknowledgement",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "alarm.IsActive && alarm.IsAcknowledged",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "GroupedVanillaNotificationPolicy.IsGroupedOverrideId(",
            StringComparison.Ordinal));
        IsTrue(alarmTile.Contains(
            "Math.Round(alarm.LastValue)",
            StringComparison.Ordinal));

        var vanillaBehaviorControls = ExtractSourceMethod(
            editorSource,
            "private void DrawVanillaBehaviorControls(AlarmView candidate)");
        IsTrue(vanillaBehaviorControls.Contains(
            "var isGrouped = GroupedVanillaNotificationPolicy",
            StringComparison.Ordinal));
        IsTrue(vanillaBehaviorControls.Contains(
            "if (!isGrouped && candidate.EntityId >= 0)",
            StringComparison.Ordinal));
        IsTrue(vanillaBehaviorControls.Contains(
            "VanillaNotificationScope.NotificationType",
            StringComparison.Ordinal));

        var entityVanillaBehaviorButtons = ExtractSourceMethod(
            editorSource,
            "private void DrawEntityVanillaBehaviorButtons(");
        IsTrue(entityVanillaBehaviorButtons.Contains(
            "GroupedVanillaNotificationPolicy.IsGroupedOverrideId(",
            StringComparison.Ordinal));

        var alarmAcknowledgeButton = ExtractSourceMethod(
            editorSource,
            "private void DrawAlarmAcknowledgeButton(");
        var silentButtonGate = alarmAcknowledgeButton.IndexOf(
            "alarm.IsActive && alarm.IsOperatorSilenced",
            StringComparison.Ordinal);
        var silentButtonControl = alarmAcknowledgeButton.IndexOf(
            "\"alarm-operator-silent-\"",
            StringComparison.Ordinal);
        var acknowledgeGate = alarmAcknowledgeButton.IndexOf(
            "if (!alarm.RequiresAcknowledgement)",
            StringComparison.Ordinal);
        IsTrue(silentButtonGate >= 0);
        IsTrue(silentButtonControl > silentButtonGate);
        IsTrue(acknowledgeGate > silentButtonControl);
        IsTrue(alarmAcknowledgeButton.Contains(
            "m_primaryButtonStyle",
            StringComparison.Ordinal));
        IsTrue(alarmAcknowledgeButton.Contains(
            "m_runtime.AcknowledgeAlarm(panel.Id, slotId)",
            StringComparison.Ordinal));
        IsTrue(alarmAcknowledgeButton.Contains(
            "alarm.IsActive && alarm.IsAcknowledged",
            StringComparison.Ordinal));
        IsTrue(alarmAcknowledgeButton.Contains(
            "\"alarm-mark-operator-silent-\"",
            StringComparison.Ordinal));
        IsTrue(alarmAcknowledgeButton.Contains(
            "After one game month",
            StringComparison.Ordinal));
        IsTrue(alarmAcknowledgeButton.Contains(
            "notification-setting exclusions still apply.",
            StringComparison.Ordinal));

        var update = ExtractSourceMethod(
            editorSource,
            "private void Update()");
        IsTrue(update.Contains(
            "m_runtime.TryTakeOperatorSilenceReminder(",
            StringComparison.Ordinal));
        IsTrue(update.Contains(
            "HandleOperatorSilenceReminder(silenceReminder);",
            StringComparison.Ordinal));

        var handleSilenceReminder = ExtractSourceMethod(
            editorSource,
            "private void HandleOperatorSilenceReminder(");
        IsTrue(handleSilenceReminder.Contains(
            "m_operatorSilenceReminder = reminder;",
            StringComparison.Ordinal));
        IsTrue(handleSilenceReminder.Contains(
            "m_isOpen = true;",
            StringComparison.Ordinal));
        IsFalse(handleSilenceReminder.Contains(
            "m_audio.",
            StringComparison.Ordinal));

        var drawSilenceReminder = ExtractSourceMethod(
            editorSource,
            "private void DrawOperatorSilenceReminder()");
        IsTrue(drawSilenceReminder.Contains(
            "foreach (var group in reminder.Groups)",
            StringComparison.Ordinal));
        IsTrue(drawSilenceReminder.Contains(
            "group.Count.ToString(",
            StringComparison.Ordinal));
        IsTrue(drawSilenceReminder.Contains(
            "m_operatorSilenceReminder = null;",
            StringComparison.Ordinal));
        IsFalse(drawSilenceReminder.Contains(
            "m_audio.",
            StringComparison.Ordinal));

        var runtimeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Runtime",
            "UnmaRuntime.cs"));
        var getViews = ExtractSourceMethod(
            runtimeSource,
            "public IReadOnlyList<AlarmView> GetViews(PanelDefinition panel)");
        IsTrue(getViews.Contains(
            "var showGroupedPersistentSlot =",
            StringComparison.Ordinal));
        IsTrue(getViews.Contains(
            "GroupedVanillaNotificationPolicy.IsGroupedSlotId(",
            StringComparison.Ordinal));
        var setVanillaBehavior = ExtractSourceMethod(
            runtimeSource,
            "public bool SetVanillaNotificationBehavior(");
        IsTrue(setVanillaBehavior.Contains(
            "scope != VanillaNotificationScope.NotificationType",
            StringComparison.Ordinal));
        IsTrue(setVanillaBehavior.Contains(
            "ReplayCurrentVanillaNotifications(overrideId);",
            StringComparison.Ordinal));
        var initialize = ExtractSourceMethod(
            runtimeSource,
            "public void Initialize()");
        var groupedSeed = initialize.IndexOf(
            "RefreshGroupedVanillaNotificationMembers(",
            StringComparison.Ordinal);
        var notificationReplay = initialize.IndexOf(
            "OnNotificationAdded(notification);",
            StringComparison.Ordinal);
        IsTrue(groupedSeed >= 0 && groupedSeed < notificationReplay);
        var setGroupedAlarm = ExtractSourceMethod(
            runtimeSource,
            "private void SetGroupedVanillaAlarm(");
        IsTrue(setGroupedAlarm.Contains(
            "GroupedVanillaNotificationPolicy.AreAllMembersSuppressed(",
            StringComparison.Ordinal));
        IsTrue(setGroupedAlarm.Contains(
            "representative.Detail,\n                snapshot.Count)",
            StringComparison.Ordinal));
        var purgeIgnored = ExtractSourceMethod(
            runtimeSource,
            "private void PurgeIgnoredVanillaAlarms(");
        IsTrue(purgeIgnored.Contains(
            "PublishExternalDisplayAlarm(alarm, false);",
            StringComparison.Ordinal));
        var setVanillaEnabled = ExtractSourceMethod(
            runtimeSource,
            "public bool SetVanillaNotificationEnabled(");
        IsTrue(setVanillaEnabled.Contains(
            "PublishExternalDisplayAlarm(",
            StringComparison.Ordinal));
        var reconcileTransferred = ExtractSourceMethod(
            runtimeSource,
            "private void ReconcileTransferredVanillaNotifications(");
        IsTrue(reconcileTransferred.Contains(
            "PublishExternalDisplayAlarm(alarm, false);",
            StringComparison.Ordinal));
        IsTrue(reconcileTransferred.Contains(
            "disabledOverrideIds.Contains(",
            StringComparison.Ordinal));
        IsTrue(reconcileTransferred.Contains(
            "var ignoredStates = matchingStates.Where(",
            StringComparison.Ordinal));
        IsTrue(reconcileTransferred.Contains(
            "history?.SetState(",
            StringComparison.Ordinal));
        var refreshGroupedMembers = ExtractSourceMethod(
            runtimeSource,
            "private void RefreshGroupedVanillaNotificationMembers(");
        IsTrue(refreshGroupedMembers.Contains(
            ".GetNotificationKeys()",
            StringComparison.Ordinal));
        IsTrue(refreshGroupedMembers.Contains(
            ".OrderBy(",
            StringComparison.Ordinal));
        IsFalse(refreshGroupedMembers.Contains(
            "m_groupedVanillaNotifications.Clear();",
            StringComparison.Ordinal));
        IsTrue(refreshGroupedMembers.Contains(
            "m_groupedVanillaNotifications.Remove(staleKey);",
            StringComparison.Ordinal));
        var suppressChanged = ExtractSourceMethod(
            runtimeSource,
            "private void OnNotificationSuppressChanged(");
        var groupedSuppressGate = suppressChanged.IndexOf(
            "GroupedVanillaNotificationPolicy.IsGroupedPrototype(",
            StringComparison.Ordinal);
        var groupedMemberRefresh = suppressChanged.IndexOf(
            "m_groupedVanillaNotifications.Add(",
            StringComparison.Ordinal);
        var groupedAllSuppressed = suppressChanged.IndexOf(
            "GroupedVanillaNotificationPolicy\n" +
            "                    .AreAllMembersSuppressed(snapshot)",
            StringComparison.Ordinal);
        var groupedSuppressReturn = suppressChanged.IndexOf(
            "return;",
            groupedAllSuppressed,
            StringComparison.Ordinal);
        var legacyEntityScope = suppressChanged.IndexOf(
            "GetNotificationEntityScope(",
            StringComparison.Ordinal);
        IsTrue(groupedSuppressGate >= 0);
        IsTrue(groupedMemberRefresh > groupedSuppressGate);
        IsTrue(groupedAllSuppressed > groupedMemberRefresh);
        IsTrue(groupedSuppressReturn > groupedAllSuppressed);
        IsTrue(legacyEntityScope > groupedSuppressReturn);
        var historyRows = ExtractSourceMethod(
            editorSource,
            "private void DrawHistoryRows(");
        var historyRow = ExtractSourceMethod(
            editorSource,
            "private void DrawHistoryRow(");
        IsFalse(historyRows.Contains("blink", StringComparison.OrdinalIgnoreCase));
        IsFalse(historyRow.Contains("blink", StringComparison.OrdinalIgnoreCase));
        IsFalse(historyRows.Contains(
            "Time.realtimeSinceStartup",
            StringComparison.Ordinal));
        IsFalse(historyRow.Contains(
            "Time.realtimeSinceStartup",
            StringComparison.Ordinal));

        var editorShellSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Ui",
            "UnmaNativeEditorShell.cs"));
        var keyboardShortcut = ExtractSourceMethod(
            editorShellSource,
            "private void HandleKeyboardShortcut(KeyDownEvent evt)");
        IsTrue(keyboardShortcut.Contains(
            "KeyCode.Escape",
            StringComparison.Ordinal));
        IsTrue(keyboardShortcut.Contains(
            "evt.ctrlKey",
            StringComparison.Ordinal));
        IsTrue(keyboardShortcut.Contains(
            "KeyCode.Return",
            StringComparison.Ordinal));
        IsTrue(keyboardShortcut.Contains(
            "if (action?.Invoke() != true)",
            StringComparison.Ordinal));
        IsTrue(keyboardShortcut.Contains(
            "evt.StopImmediatePropagation();",
            StringComparison.Ordinal));

        var saveShortcut = ExtractSourceMethod(
            editorSource,
            "private bool SaveDraftRuleFromShortcut()");
        IsTrue(saveShortcut.Contains(
            "m_editorWindowMode != EditorWindowMode.Rule",
            StringComparison.Ordinal));
        IsTrue(saveShortcut.Contains(
            "return false;",
            StringComparison.Ordinal));
        var escapeShortcut = ExtractSourceMethod(
            editorSource,
            "private bool HandleEditorEscapeShortcut()");
        IsTrue(escapeShortcut.Contains(
            "m_editorClosePromptOpen = false;",
            StringComparison.Ordinal));

        var immediateUiSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Ui",
            "NativeImmediateUi.cs"));
        IsTrue(immediateUiSource.Contains(
            "public bool HasKeyboardFocus",
            StringComparison.Ordinal));
        IsTrue(immediateUiSource.Contains(
            "private void HandlePointerRelease(PointerUpEvent evt)",
            StringComparison.Ordinal));
        IsTrue(immediateUiSource.Contains(
            "IsTextInputElement(focused)",
            StringComparison.Ordinal));
        IsTrue(immediateUiSource.Contains(
            "m_focusSink.Focus();",
            StringComparison.Ordinal));
        IsTrue(immediateUiSource.Contains(
            "internal readonly struct NativeControlMetadata",
            StringComparison.Ordinal));

        var ensureStyles = ExtractSourceMethod(
            editorSource,
            "private void EnsureStyles()");
        var textFieldStyleStart = ensureStyles.IndexOf(
            "m_textFieldStyle = new GUIStyle()",
            StringComparison.Ordinal);
        var textFieldStyleEnd = ensureStyles.IndexOf(
            "m_historyHeaderStyle =",
            textFieldStyleStart,
            StringComparison.Ordinal);
        IsTrue(textFieldStyleStart >= 0);
        IsTrue(textFieldStyleEnd > textFieldStyleStart);
        var textFieldStyleSource = ensureStyles.Substring(
            textFieldStyleStart,
            textFieldStyleEnd - textFieldStyleStart);
        IsTrue(textFieldStyleSource.Contains(
            "fontStyle = FontStyle.Bold",
            StringComparison.Ordinal));
        IsTrue(textFieldStyleSource.Contains(
            "CoiUiPalette.InputBackground",
            StringComparison.Ordinal));
        IsTrue(textFieldStyleSource.Contains(
            "textColor = Color.white",
            StringComparison.Ordinal));
        IsTrue(textFieldStyleSource.Contains(
            "m_textFieldStyle.onFocused.background = " +
            "m_textFieldStyle.focused.background;",
            StringComparison.Ordinal));
        AreEqual(
            8,
            Regex.Matches(
                textFieldStyleSource,
                "textColor = Color\\.white",
                RegexOptions.CultureInvariant).Count);
        foreach (var expectedBackground in new[]
                 {
                     "m_textFieldStyle.hover.background = " +
                     "m_textFieldStyle.normal.background;",
                     "m_textFieldStyle.active.background = " +
                     "m_textFieldStyle.normal.background;",
                     "m_textFieldStyle.onNormal.background = " +
                     "m_textFieldStyle.normal.background;",
                     "m_textFieldStyle.onHover.background = " +
                     "m_textFieldStyle.normal.background;",
                     "m_textFieldStyle.onActive.background = " +
                     "m_textFieldStyle.normal.background;",
                     "m_textFieldStyle.onFocused.background = " +
                     "m_textFieldStyle.focused.background;",
                 })
        {
            IsTrue(textFieldStyleSource.Contains(
                expectedBackground,
                StringComparison.Ordinal));
        }

        var paletteSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Ui",
            "CoiUiPalette.cs"));
        IsTrue(paletteSource.Contains(
            "InputBackground = Rgb(0x10, 0x35, 0x22)",
            StringComparison.Ordinal));

        var textFieldCallCount = Regex.Matches(
            editorSource,
            "NativeGUILayout\\.TextField\\(",
            RegexOptions.CultureInvariant).Count;
        var styledTextFieldCallCount = Regex.Matches(
            editorSource,
            "(?=NativeGUILayout\\.TextField\\(" +
            "(?:(?!NativeGUILayout\\.TextField\\(|;)[\\s\\S])*?" +
            "m_textFieldStyle)",
            RegexOptions.CultureInvariant).Count;
        IsTrue(textFieldCallCount > 0);
        AreEqual(textFieldCallCount, styledTextFieldCallCount);

        var launcherMove = ExtractSourceMethod(
            editorSource,
            "private void HandleNativeLauncherMoved(float x, float y)");
        IsTrue(launcherMove.Contains(
            "config.LauncherX = previousX;",
            StringComparison.Ordinal));
        IsTrue(launcherMove.Contains(
            "StatusSeverity.Error",
            StringComparison.Ordinal));

        foreach (var shellFile in new[]
                 {
                     "UnmaNativeWindowShell.cs",
                     "UnmaNativeEditorShell.cs",
                     "UnmaNativeDetachedPanelShell.cs",
                 })
        {
            var shellSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "source",
                "Ui",
                shellFile));
            IsTrue(shellSource.Contains(
                "private float m_preferredWindowWidth;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "public Vector2 PreferredSize =>",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private Vector2 m_preferredFrameWorldPosition;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private Vector2 m_effectiveFrameWorldPosition;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private int m_positionRestoreVersion;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private readonly WindowDragger m_windowDragger;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "var frameShadowElement = Frame.RootElement.parent",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "new UiComponent(frameShadowElement)",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "m_windowDragger.OnMoved += HandleWindowMoved;",
                StringComparison.Ordinal));
            var createWindowDragger = ExtractSourceMethod(
                shellSource,
                "public WindowDragger CreateWindowDragger()");
            var draggerWindowArgument = createWindowDragger.IndexOf(
                "this,",
                StringComparison.Ordinal);
            var draggerFrameArgument = createWindowDragger.IndexOf(
                "new UiComponent(frameShadowElement),",
                StringComparison.Ordinal);
            var draggerHeaderArgument = createWindowDragger.IndexOf(
                "TitleBar",
                StringComparison.Ordinal);
            IsTrue(draggerWindowArgument >= 0);
            IsTrue(draggerFrameArgument > draggerWindowArgument);
            IsTrue(draggerHeaderArgument > draggerFrameArgument);
            IsTrue(shellSource.Contains(
                "private int m_viewportScreenWidth;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private int m_viewportScreenHeight;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private float m_viewportRootScale;",
                StringComparison.Ordinal));
            IsTrue(shellSource.Contains(
                "private bool m_viewportSignatureReady;",
                StringComparison.Ordinal));
            IsFalse(shellSource.Contains(
                "m_positionReady",
                StringComparison.Ordinal));

            var currentPosition = ExtractSourceMethod(
                shellSource,
                "public bool TryGetCurrentPosition(out Vector2 position)");
            IsTrue(currentPosition.Contains(
                "position = m_preferredFrameWorldPosition;",
                StringComparison.Ordinal));
            IsFalse(currentPosition.Contains(
                "m_window.FrameWorldPosition",
                StringComparison.Ordinal));
            IsFalse(currentPosition.Contains(
                "m_preferredFrameWorldPosition =",
                StringComparison.Ordinal));
            IsFalse(currentPosition.Contains(
                "m_effectiveFrameWorldPosition",
                StringComparison.Ordinal));
            IsTrue(currentPosition.Contains(
                "return !m_disposed && m_window.IsOpen;",
                StringComparison.Ordinal));

            var windowMoved = ExtractSourceMethod(
                shellSource,
                "private void HandleWindowMoved(Vector2 _)");
            IsTrue(windowMoved.Contains(
                "var moveVersion = ++m_positionRestoreVersion;",
                StringComparison.Ordinal));
            IsTrue(windowMoved.Contains(
                "m_window.IsPinned",
                StringComparison.Ordinal));
            IsTrue(windowMoved.Contains(
                "ScheduleFrameWorldPositionRead(",
                StringComparison.Ordinal));
            IsFalse(windowMoved.Contains(
                "m_window.FrameWorldPosition",
                StringComparison.Ordinal));
            IsFalse(windowMoved.Contains(
                "RestoreEffectivePositionFromPreferred(",
                StringComparison.Ordinal));

            var completeWindowMove = ExtractSourceMethod(
                shellSource,
                "private void CompleteWindowMove(");
            IsTrue(completeWindowMove.Contains(
                "moveVersion != m_positionRestoreVersion",
                StringComparison.Ordinal));
            IsTrue(completeWindowMove.Contains(
                "NormalizePreferredPosition(current)",
                StringComparison.Ordinal));
            IsTrue(completeWindowMove.Contains(
                "var effectivePosition = ClampPosition(",
                StringComparison.Ordinal));
            IsTrue(completeWindowMove.Contains(
                "if (!PositionsApproximately(current, effectivePosition))",
                StringComparison.Ordinal));
            IsTrue(completeWindowMove.Contains(
                "RestoreEffectivePositionFromPreferred(force: true);",
                StringComparison.Ordinal));

            var disposeShell = ExtractSourceMethod(
                shellSource,
                "public void Dispose()");
            IsTrue(disposeShell.Contains(
                "m_windowDragger.OnMoved -= HandleWindowMoved;",
                StringComparison.Ordinal));
            IsTrue(disposeShell.Contains(
                "m_windowDragger.Disable();",
                StringComparison.Ordinal));
            IsTrue(disposeShell.IndexOf(
                "m_windowDragger.Disable();",
                StringComparison.Ordinal) > disposeShell.LastIndexOf(
                "m_window.RemoveFromHierarchy();",
                StringComparison.Ordinal));

            var restoreFrameWorldPosition = ExtractSourceMethod(
                shellSource,
                "public void RestoreFrameWorldPosition(Vector2 worldPosition)");
            IsTrue(restoreFrameWorldPosition.Contains(
                "var frameShadowElement = Frame.RootElement.parent;",
                StringComparison.Ordinal));
            IsTrue(restoreFrameWorldPosition.Contains(
                "frameShadowElement.resolvedStyle.translate",
                StringComparison.Ordinal));
            IsFalse(restoreFrameWorldPosition.Contains(
                "Frame.RootElement.resolvedStyle.translate",
                StringComparison.Ordinal));

            var scheduledRestore = ExtractSourceMethod(
                shellSource,
                "public void ScheduleFrameWorldPositionRestore(");
            IsTrue(scheduledRestore.Contains(
                "Func<bool> shouldRestore",
                StringComparison.Ordinal));
            var scheduledGuardIndex = scheduledRestore.IndexOf(
                "if (shouldRestore?.Invoke() != true)",
                StringComparison.Ordinal);
            var scheduledMutationIndex = scheduledRestore.IndexOf(
                "RestoreFrameWorldPosition(worldPosition);",
                StringComparison.Ordinal);
            IsTrue(scheduledGuardIndex >= 0);
            IsTrue(scheduledMutationIndex > scheduledGuardIndex);

            var scheduledPositionRead = ExtractSourceMethod(
                shellSource,
                "public void ScheduleFrameWorldPositionRead(");
            IsTrue(scheduledPositionRead.Contains(
                "if (shouldRead?.Invoke() != true)",
                StringComparison.Ordinal));
            IsTrue(scheduledPositionRead.Contains(
                "onRead?.Invoke(FrameWorldPosition);",
                StringComparison.Ordinal));
            IsTrue(scheduledPositionRead.Contains(
                ".StartingIn(16);",
                StringComparison.Ordinal));

            var restorePosition = ExtractSourceMethod(
                shellSource,
                "private void RestorePosition(Vector2 requestedPosition)");
            IsTrue(restorePosition.Contains(
                "NormalizePreferredPosition(requestedPosition)",
                StringComparison.Ordinal));
            IsTrue(restorePosition.Contains(
                "RestoreEffectivePositionFromPreferred(force: true);",
                StringComparison.Ordinal));

            var restoreEffectivePosition = ExtractSourceMethod(
                shellSource,
                "private void RestoreEffectivePositionFromPreferred(bool force)");
            IsTrue(restoreEffectivePosition.Contains(
                "ClampPosition(",
                StringComparison.Ordinal));
            IsTrue(restoreEffectivePosition.Contains(
                "m_effectiveFrameWorldPosition = effectivePosition;",
                StringComparison.Ordinal));
            IsTrue(restoreEffectivePosition.Contains(
                "var restoreVersion = ++m_positionRestoreVersion;",
                StringComparison.Ordinal));
            IsTrue(restoreEffectivePosition.Contains(
                "CompleteEffectivePositionRestore(restoreVersion)",
                StringComparison.Ordinal));
            IsTrue(restoreEffectivePosition.Contains(
                "restoreVersion == m_positionRestoreVersion",
                StringComparison.Ordinal));
            IsTrue(restoreEffectivePosition.Contains(
                "m_window.IsOpen",
                StringComparison.Ordinal));

            var applyPreferredSize = ExtractSourceMethod(
                shellSource,
                "private void ApplyPreferredWindowSize()");
            IsTrue(applyPreferredSize.Contains(
                "RestoreEffectivePositionFromPreferred(force: true);",
                StringComparison.Ordinal));

            if (shellSource.Contains(
                    "public void ApplyLayout(Vector2 position, Vector2 size)",
                    StringComparison.Ordinal))
            {
                var applyLayout = ExtractSourceMethod(
                    shellSource,
                    "public void ApplyLayout(Vector2 position, Vector2 size)");
                IsTrue(applyLayout.Contains(
                    "NormalizePreferredPosition(position)",
                    StringComparison.Ordinal));
                if (string.Equals(
                        shellFile,
                        "UnmaNativeWindowShell.cs",
                        StringComparison.Ordinal))
                {
                    var fullscreenBranch = applyLayout.IndexOf(
                        "if (m_temporarilyFullscreen)",
                        StringComparison.Ordinal);
                    var applyFullscreen = applyLayout.IndexOf(
                        "ApplyFullscreenContentSize();",
                        StringComparison.Ordinal);
                    var elseBranch = applyLayout.IndexOf(
                        "else",
                        applyFullscreen,
                        StringComparison.Ordinal);
                    var applyPreferred = applyLayout.IndexOf(
                        "ApplyPreferredWindowSize();",
                        StringComparison.Ordinal);
                    IsTrue(fullscreenBranch >= 0);
                    IsTrue(applyFullscreen > fullscreenBranch);
                    IsTrue(elseBranch > applyFullscreen);
                    IsTrue(applyPreferred > elseBranch);
                }
            }

            if (string.Equals(
                    shellFile,
                    "UnmaNativeWindowShell.cs",
                    StringComparison.Ordinal))
            {
                IsFalse(currentPosition.Contains(
                    "m_temporarilyFullscreen",
                    StringComparison.Ordinal));
                var temporarySize = ExtractSourceMethod(
                    shellSource,
                    "public void SetTemporarySize(Vector2 _)");
                IsTrue(temporarySize.Contains(
                    "ApplyPreferredWindowSize();",
                    StringComparison.Ordinal));
                IsFalse(temporarySize.Contains(
                    "ApplyWindowSize(",
                    StringComparison.Ordinal));
                IsTrue(temporarySize.Contains(
                    "m_windowDragger.Enable();",
                    StringComparison.Ordinal));
                IsTrue(temporarySize.IndexOf(
                    "m_window.Fullscreen(false);",
                    StringComparison.Ordinal) < temporarySize.IndexOf(
                    "m_windowDragger.Enable();",
                    StringComparison.Ordinal));
                var maximize = ExtractSourceMethod(
                    shellSource,
                    "public Vector2 MaximizeTemporarily()");
                IsTrue(maximize.Contains(
                    "TryGetCurrentPosition(out _);",
                    StringComparison.Ordinal));
                IsTrue(maximize.Contains(
                    "m_positionRestoreVersion++;",
                    StringComparison.Ordinal));
                IsTrue(maximize.Contains(
                    "m_windowDragger.Disable();",
                    StringComparison.Ordinal));
                IsTrue(maximize.IndexOf(
                    "m_windowDragger.Disable();",
                    StringComparison.Ordinal) < maximize.IndexOf(
                    "m_window.Fullscreen();",
                    StringComparison.Ordinal));
                IsFalse(shellSource.Contains(
                    "m_preFullscreenFrameWorldPosition",
                    StringComparison.Ordinal));
                IsTrue(restoreEffectivePosition.Contains(
                    "!m_temporarilyFullscreen",
                    StringComparison.Ordinal));
                var openMainShell = ExtractSourceMethod(
                    shellSource,
                    "public void Open()");
                var openFullscreenBranch = openMainShell.IndexOf(
                    "if (m_temporarilyFullscreen)",
                    StringComparison.Ordinal);
                var openApplyFullscreen = openMainShell.IndexOf(
                    "ApplyFullscreenContentSize();",
                    StringComparison.Ordinal);
                var openElseBranch = openMainShell.IndexOf(
                    "else",
                    openApplyFullscreen,
                    StringComparison.Ordinal);
                var openRestorePosition = openMainShell.IndexOf(
                    "RestorePosition(m_preferredFrameWorldPosition);",
                    StringComparison.Ordinal);
                IsTrue(openFullscreenBranch >= 0);
                IsTrue(openApplyFullscreen > openFullscreenBranch);
                IsTrue(openElseBranch > openApplyFullscreen);
                IsTrue(openRestorePosition > openElseBranch);
            }

            var setContentScale = ExtractSourceMethod(
                shellSource,
                "public void SetContentScale(float scale)");
            IsTrue(setContentScale.Contains(
                "var contentScaleChanged =",
                StringComparison.Ordinal));
            IsTrue(setContentScale.Contains(
                "var viewportChanged = UpdateViewportSignature();",
                StringComparison.Ordinal));
            IsTrue(setContentScale.Contains(
                "if (!contentScaleChanged && !viewportChanged)",
                StringComparison.Ordinal));
            IsTrue(setContentScale.Contains(
                "ApplyPreferredWindowSize();",
                StringComparison.Ordinal));
            IsFalse(setContentScale.Contains(
                "ApplyWindowSize(m_windowWidth, m_windowHeight);",
                StringComparison.Ordinal));

            var refreshViewport = ExtractSourceMethod(
                shellSource,
                "public void RefreshViewportConstraints()");
            IsTrue(refreshViewport.Contains(
                "if (m_disposed || !UpdateViewportSignature())",
                StringComparison.Ordinal));
            var unchangedViewportReturn = refreshViewport.IndexOf(
                "return;",
                StringComparison.Ordinal);
            var refreshPreferredSize = refreshViewport.IndexOf(
                "ApplyPreferredWindowSize();",
                StringComparison.Ordinal);
            IsTrue(unchangedViewportReturn >= 0);
            IsTrue(refreshPreferredSize > unchangedViewportReturn);

            var updateViewportSignature = ExtractSourceMethod(
                shellSource,
                "private bool UpdateViewportSignature()");
            IsTrue(updateViewportSignature.Contains(
                "m_viewportScreenWidth != Screen.width",
                StringComparison.Ordinal));
            IsTrue(updateViewportSignature.Contains(
                "m_viewportScreenHeight != Screen.height",
                StringComparison.Ordinal));
            IsTrue(updateViewportSignature.Contains(
                "m_uiRoot.CurrentScale",
                StringComparison.Ordinal));
            IsTrue(updateViewportSignature.Contains(
                "m_viewportSignatureReady = true;",
                StringComparison.Ordinal));

            var openShell = ExtractSourceMethod(
                shellSource,
                "public void Open()");
            IsTrue(openShell.Contains(
                "RefreshViewportConstraints();",
                StringComparison.Ordinal));

            var resizeCompleted = ExtractSourceMethod(
                shellSource,
                "private void HandleResizeCompleted()");
            IsTrue(resizeCompleted.Contains(
                "m_preferredWindowWidth",
                StringComparison.Ordinal));
            IsTrue(resizeCompleted.Contains(
                "m_preferredWindowHeight",
                StringComparison.Ordinal));

            var resizeDelta = ExtractSourceMethod(
                shellSource,
                "private void HandleResizeDelta(Vector2 delta)");
            IsTrue(resizeDelta.Contains(
                "m_resizeStartPreferredWidth",
                StringComparison.Ordinal));
            IsTrue(resizeDelta.Contains(
                "m_resizeStartPreferredHeight",
                StringComparison.Ordinal));
        }

        var exitHistorian = ExtractSourceMethod(
            editorSource,
            "private void ExitInstrumentHistorian()");
        IsTrue(exitHistorian.Contains(
            "var preferredSize = m_nativeWindowShell.PreferredSize;",
            StringComparison.Ordinal));
        IsTrue(exitHistorian.Contains(
            "m_windowRect.width = preferredSize.x;",
            StringComparison.Ordinal));
        IsTrue(exitHistorian.Contains(
            "m_windowRect.height = preferredSize.y;",
            StringComparison.Ordinal));
        IsFalse(exitHistorian.Contains(
            "m_windowRect.width = m_historianPreviousWindowSize.x;",
            StringComparison.Ordinal));
    }

    private static string ExtractSourceMethod(
        string source,
        string signature)
    {
        var signatureIndex = source.IndexOf(
            signature,
            StringComparison.Ordinal);
        if (signatureIndex < 0)
        {
            throw new InvalidOperationException(
                "Source method not found: " + signature);
        }
        var bodyStart = source.IndexOf('{', signatureIndex);
        if (bodyStart < 0)
        {
            throw new InvalidOperationException(
                "Source method body not found: " + signature);
        }

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source.Substring(
                    signatureIndex,
                    index - signatureIndex + 1);
            }
        }

        throw new InvalidOperationException(
            "Source method body is incomplete: " + signature);
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

        AreEqual(20, legacy.SchemaVersion);
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
        AreEqual(20, new UnmaConfiguration().SchemaVersion);

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

        AreEqual(20, legacy.SchemaVersion);
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
        AreEqual(20, restored.SchemaVersion);
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

    private static void TestMetricPickerFilter()
    {
        IsTrue(MetricPickerFilter.Matches(
            "Power Generated Last Tick",
            "$PowerGeneratedLastTick",
            " generated "));
        IsTrue(MetricPickerFilter.Matches(
            "Productivity Counter History",
            "$ProductivityCounterHistory.Yearly",
            "counterhistory.year"));
        IsTrue(MetricPickerFilter.Matches(
            null,
            "$stored.quantity",
            "STORED.QUANTITY"));
        IsTrue(MetricPickerFilter.Matches(
            "Output Buffer Quantity",
            null,
            "output buffer"));
        IsTrue(MetricPickerFilter.Matches(
            null,
            null,
            "  "));
        IsFalse(MetricPickerFilter.Matches(
            "Destroyed",
            "$destroyed",
            "power"));

        var repositoryRoot = FindRepositoryRoot();
        var overlaySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Ui",
            "UnmaOverlayController.cs"));
        var drawInstruments = ExtractSourceMethod(
            overlaySource,
            "private void DrawInstruments()");
        IsTrue(drawInstruments.Contains(
            "\"instrument-metric-search\"",
            StringComparison.Ordinal));
        IsTrue(drawInstruments.Contains(
            "MetricPickerFilter.Matches(",
            StringComparison.Ordinal));
        var captureInstrumentEntity = ExtractSourceMethod(
            overlaySource,
            "private void CaptureInstrumentEntity()");
        IsTrue(captureInstrumentEntity.Contains(
            "m_instrumentMetricFilter = \"\";",
            StringComparison.Ordinal));
        IsTrue(captureInstrumentEntity.Contains(
            "m_instrumentMetricScroll = Vector2.zero;",
            StringComparison.Ordinal));
    }

    private static void TestOperatorSilenceReminderPolicy()
    {
        const long now = 1_000;
        const long month = GameTimeWindowPolicy.SimTicksPerMonth;
        var samples = new OperatorSilenceReminderSample[]
        {
            SilenceReminderSample(
                "group:b",
                "Beta",
                now - month),
            SilenceReminderSample(
                "group:a",
                "Alpha later label",
                now - month),
            SilenceReminderSample(
                "group:a",
                "Alpha",
                now - month - 1),
            SilenceReminderSample(
                "before-boundary",
                "Too new",
                now - month + 1),
            SilenceReminderSample(
                "inactive",
                "Inactive",
                now - month,
                isActive: false),
            SilenceReminderSample(
                "not-operator",
                "Not operator-silenced",
                now - month,
                isOperatorSilenced: false),
            SilenceReminderSample(
                "configured-silent",
                "Configured silent",
                now - month,
                effectiveBehavior: VanillaNotificationBehavior.Silent),
            SilenceReminderSample(
                "configured-hidden",
                "Configured hidden",
                now - month,
                effectiveBehavior: VanillaNotificationBehavior.Hidden),
            SilenceReminderSample(
                "configured-ignored",
                "Configured ignored",
                now - month,
                effectiveBehavior: VanillaNotificationBehavior.Ignored),
            SilenceReminderSample(
                "soundless",
                "Soundless",
                now - month,
                soundId: " NoNe "),
            SilenceReminderSample(
                "future",
                "Future",
                now + 1),
            SilenceReminderSample(
                "invalid-start",
                "Invalid start",
                -1),
            null,
        };

        var result = OperatorSilenceReminderPolicy.Build(
            samples,
            now,
            month);
        AreEqual(now, result.CurrentGameTick);
        AreEqual(month, result.MinimumAgeTicks);
        AreEqual(2, result.GroupCount);
        AreEqual(3, result.AlarmCount);
        AreEqual("group:a", result.Groups[0].GroupId);
        AreEqual("Alpha", result.Groups[0].Label);
        AreEqual(2, result.Groups[0].Count);
        AreEqual("group:b", result.Groups[1].StableGroupId);
        AreEqual("Beta", result.Groups[1].HumanLabel);
        AreEqual(1, result.Groups[1].AlarmCount);
        IsTrue(result.Groups is
            System.Collections.ObjectModel
                .ReadOnlyCollection<OperatorSilenceReminderGroup>);

        var reversed = OperatorSilenceReminderPolicy.Build(
            samples.Reverse(),
            now,
            month);
        AreEqual(
            string.Join("|", result.Groups.Select(group =>
                group.GroupId + ":" + group.Label + ":" + group.Count)),
            string.Join("|", reversed.Groups.Select(group =>
                group.GroupId + ":" + group.Label + ":" + group.Count)));

        var blankFallback = OperatorSilenceReminderPolicy.Build(
            new[]
            {
                SilenceReminderSample(" ", null, now - month),
                SilenceReminderSample(null, " ", now - month),
            },
            now,
            month);
        AreEqual(1, blankFallback.GroupCount);
        AreEqual(2, blankFallback.AlarmCount);
        AreEqual(
            OperatorSilenceReminderPolicy.UnknownGroupId,
            blankFallback.Groups[0].GroupId);
        AreEqual(
            OperatorSilenceReminderPolicy.UnknownLabel,
            blankFallback.Groups[0].Label);

        var exactBoundary = OperatorSilenceReminderPolicy.Build(
            new[]
            {
                SilenceReminderSample("exact", "Exact", now - month),
                SilenceReminderSample(
                    "just-before",
                    "Just before",
                    now - month + 1),
            },
            now,
            month);
        AreEqual(1, exactBoundary.AlarmCount);
        AreEqual("exact", exactBoundary.Groups[0].GroupId);

        var nextMonth = OperatorSilenceReminderPolicy.Build(
            new[]
            {
                SilenceReminderSample("still-active", "Still active", 0),
            },
            month * 2,
            month);
        AreEqual(1, nextMonth.AlarmCount);
        AreEqual("still-active", nextMonth.Groups[0].GroupId);

        foreach (var invalid in new[]
                 {
                     OperatorSilenceReminderPolicy.Build(samples, -1, month),
                     OperatorSilenceReminderPolicy.Build(samples, now, 0),
                     OperatorSilenceReminderPolicy.Build(samples, now, -1),
                     OperatorSilenceReminderPolicy.Build(null, now, month),
                 })
        {
            AreEqual(0, invalid.GroupCount);
            AreEqual(0, invalid.AlarmCount);
        }
    }

    private static OperatorSilenceReminderSample SilenceReminderSample(
        string stableGroupId,
        string humanLabel,
        long operatorSilencedAtGameTick,
        bool isActive = true,
        bool isOperatorSilenced = true,
        VanillaNotificationBehavior effectiveBehavior =
            VanillaNotificationBehavior.Normal,
        string soundId = "auto")
    {
        return new OperatorSilenceReminderSample(
            stableGroupId,
            humanLabel,
            isActive,
            isOperatorSilenced,
            effectiveBehavior,
            soundId,
            operatorSilencedAtGameTick);
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
        AreEqual(20, legacyConfiguration.SchemaVersion);
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

    private static void TestInstrumentForecastPolicy()
    {
        var risingHistory = new[]
        {
            new InstrumentValueSample(300d, 16d),
            new InstrumentValueSample(0d, 10d),
            new InstrumentValueSample(600d, 999d),
            new InstrumentValueSample(300d, 14d),
            new InstrumentValueSample(300d, 15d),
        };
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            risingHistory,
            600d,
            20d,
            0d,
            100d,
            out var rising));
        AreEqual(InstrumentForecastStatus.Moving, rising.Status);
        AreEqual(InstrumentForecastDirection.Rising, rising.Direction);
        AreEqual(InstrumentForecastEtaStatus.Available, rising.EtaStatus);
        AreEqual(3, rising.SampleCount);
        AreClose(600d, rising.DurationTicks);
        AreClose(20d, rising.CurrentValue);
        AreClose(10d, rising.MinimumValue);
        AreClose(15d, rising.AverageValue);
        AreClose(20d, rising.MaximumValue);
        AreClose(10d, rising.RatePerMonth);
        AreClose(1d, rising.RSquared);
        AreClose(100d, rising.TargetValue);
        AreClose(4800d, rising.EtaTicks);
        IsTrue(rising.HasTrend);
        IsTrue(rising.HasEta);
        IsFalse(rising.HorizonExceeded);

        // Reordering history and duplicate values must not affect analysis.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(300d, 14d),
                new InstrumentValueSample(300d, 15d),
                new InstrumentValueSample(600d, -999d),
                new InstrumentValueSample(0d, 10d),
                new InstrumentValueSample(300d, 16d),
            },
            600d,
            20d,
            0d,
            100d,
            out var reordered));
        AreEqual(rising.Status, reordered.Status);
        AreEqual(rising.Direction, reordered.Direction);
        AreEqual(rising.EtaStatus, reordered.EtaStatus);
        AreEqual(rising.SampleCount, reordered.SampleCount);
        AreClose(rising.DurationTicks, reordered.DurationTicks);
        AreClose(rising.MinimumValue, reordered.MinimumValue);
        AreClose(rising.AverageValue, reordered.AverageValue);
        AreClose(rising.MaximumValue, reordered.MaximumValue);
        AreClose(rising.RatePerMonth, reordered.RatePerMonth);
        AreClose(rising.RSquared, reordered.RSquared);
        AreClose(rising.EtaTicks, reordered.EtaTicks);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 90d),
                new InstrumentValueSample(300d, 80d),
            },
            600d,
            70d,
            0d,
            100d,
            out var falling));
        AreEqual(InstrumentForecastStatus.Moving, falling.Status);
        AreEqual(InstrumentForecastDirection.Falling, falling.Direction);
        AreEqual(InstrumentForecastEtaStatus.Available, falling.EtaStatus);
        AreClose(-20d, falling.RatePerMonth);
        AreClose(0d, falling.TargetValue);
        AreClose(2100d, falling.EtaTicks);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[] { new InstrumentValueSample(0d, 1d) },
            100d,
            2d,
            0d,
            10d,
            out var tooFew));
        AreEqual(InstrumentForecastStatus.InsufficientData, tooFew.Status);
        AreEqual(2, tooFew.SampleCount);
        IsFalse(tooFew.HasTrend);
        IsFalse(tooFew.HasEta);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(100d, 1d),
                new InstrumentValueSample(110d, 2d),
            },
            120d,
            3d,
            0d,
            10d,
            out var tooShort));
        AreEqual(InstrumentForecastStatus.InsufficientData, tooShort.Status);
        AreEqual(3, tooShort.SampleCount);
        AreClose(20d, tooShort.DurationTicks);

        // Exactly two game days is enough for a forecast.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 0d),
                new InstrumentValueSample(20d, 2d),
            },
            40d,
            4d,
            0d,
            100d,
            out var minimumWindow));
        AreEqual(InstrumentForecastStatus.Moving, minimumWindow.Status);
        AreClose(60d, minimumWindow.RatePerMonth);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 50d),
                new InstrumentValueSample(300d, 50.025d),
            },
            600d,
            50.05d,
            0d,
            100d,
            out var stable));
        AreEqual(InstrumentForecastStatus.Stable, stable.Status);
        AreEqual(InstrumentForecastDirection.None, stable.Direction);
        AreEqual(InstrumentForecastEtaStatus.None, stable.EtaStatus);
        AreClose(0.05d, stable.RatePerMonth);
        AreClose(1d, stable.RSquared);

        // Constant data has a perfect fit but remains stable and ETA-free.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 25d),
                new InstrumentValueSample(300d, 25d),
            },
            600d,
            25d,
            0d,
            100d,
            out var constant));
        AreEqual(InstrumentForecastStatus.Stable, constant.Status);
        AreClose(0d, constant.RatePerMonth);
        AreClose(1d, constant.RSquared);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 0d),
                new InstrumentValueSample(150d, 100d),
                new InstrumentValueSample(300d, 0d),
                new InstrumentValueSample(450d, 100d),
            },
            600d,
            1d,
            0d,
            100d,
            out var noisy));
        AreEqual(InstrumentForecastStatus.Unreliable, noisy.Status);
        AreEqual(InstrumentForecastDirection.Rising, noisy.Direction);
        IsTrue(noisy.RSquared <
               InstrumentForecastPolicy.MinimumReliableRSquared);
        AreEqual(InstrumentForecastEtaStatus.None, noisy.EtaStatus);
        IsFalse(noisy.HasEta);

        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 50d),
                new InstrumentValueSample(150d, 50.08d),
                new InstrumentValueSample(300d, 49.95d),
                new InstrumentValueSample(450d, 50.04d),
            },
            600d,
            50d,
            0d,
            100d,
            out var noisyAndFlat));
        AreEqual(InstrumentForecastStatus.Unreliable, noisyAndFlat.Status);
        AreEqual(InstrumentForecastDirection.None, noisyAndFlat.Direction);
        AreEqual(InstrumentForecastEtaStatus.None, noisyAndFlat.EtaStatus);

        // A projected boundary farther than 100 game years is signalled but
        // intentionally does not expose a concrete ETA.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, -1000.11d),
                new InstrumentValueSample(300d, -1000.055d),
            },
            600d,
            -1000d,
            0d,
            100d,
            out var beyondHorizon));
        AreEqual(InstrumentForecastStatus.Moving, beyondHorizon.Status);
        AreEqual(
            InstrumentForecastEtaStatus.BeyondHorizon,
            beyondHorizon.EtaStatus);
        IsTrue(beyondHorizon.HorizonExceeded);
        IsFalse(beyondHorizon.HasEta);
        AreClose(0d, beyondHorizon.EtaTicks);

        // A boundary already reached is never projected in reverse.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 90d),
                new InstrumentValueSample(300d, 100d),
            },
            600d,
            110d,
            0d,
            100d,
            out var aboveMaximum));
        AreEqual(InstrumentForecastDirection.Rising, aboveMaximum.Direction);
        AreEqual(InstrumentForecastEtaStatus.None, aboveMaximum.EtaStatus);
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 10d),
                new InstrumentValueSample(300d, 0d),
            },
            600d,
            -10d,
            0d,
            100d,
            out var belowMinimum));
        AreEqual(InstrumentForecastDirection.Falling, belowMinimum.Direction);
        AreEqual(InstrumentForecastEtaStatus.None, belowMinimum.EtaStatus);

        // Large absolute ticks must not reduce the fit precision.
        const double largeTick = 1000000000000d;
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(largeTick, 10d),
                new InstrumentValueSample(largeTick + 300d, 15d),
            },
            largeTick + 600d,
            20d,
            0d,
            100d,
            out var largeTickForecast));
        AreClose(10d, largeTickForecast.RatePerMonth);
        AreClose(1d, largeTickForecast.RSquared);

        // Scaling Y before covariance keeps very large finite values usable.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 1.00000e150d),
                new InstrumentValueSample(300d, 1.00001e150d),
            },
            600d,
            1.00002e150d,
            0d,
            2e150d,
            out var largeValueForecast));
        IsTrue(Math.Abs(
            largeValueForecast.RatePerMonth / 2e145d - 1d) < 0.000000001d);
        AreClose(1d, largeValueForecast.RSquared);

        // Historical duplicates alone do not satisfy the unique sample gate.
        IsTrue(InstrumentForecastPolicy.TryAnalyze(
            new[]
            {
                new InstrumentValueSample(0d, 1d),
                new InstrumentValueSample(0d, 2d),
                new InstrumentValueSample(0d, 3d),
            },
            40d,
            4d,
            0d,
            10d,
            out var duplicateTicks));
        AreEqual(
            InstrumentForecastStatus.InsufficientData,
            duplicateTicks.Status);
        AreEqual(2, duplicateTicks.SampleCount);
        AreClose(2d, duplicateTicks.MinimumValue);

        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            null,
            40d,
            1d,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            Array.Empty<InstrumentValueSample>(),
            double.NaN,
            1d,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            Array.Empty<InstrumentValueSample>(),
            40d,
            double.PositiveInfinity,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            Array.Empty<InstrumentValueSample>(),
            40d,
            1d,
            10d,
            0d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            Array.Empty<InstrumentValueSample>(),
            40d,
            1d,
            0d,
            double.NaN,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            new[] { new InstrumentValueSample(double.NaN, 1d) },
            40d,
            1d,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            new[] { new InstrumentValueSample(0d, double.NaN) },
            40d,
            1d,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            new[] { new InstrumentValueSample(41d, 1d) },
            40d,
            1d,
            0d,
            10d,
            out _));
        IsFalse(InstrumentForecastPolicy.TryAnalyze(
            Array.Empty<InstrumentValueSample>(),
            40d,
            1d,
            -double.MaxValue,
            double.MaxValue,
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
        AreEqual(980f, WindowResizeMath.NormalizePreferredExtent(
            980f,
            700f));
        AreEqual(700f, WindowResizeMath.NormalizePreferredExtent(
            float.NaN,
            700f));
        AreEqual(1356f, WindowResizeMath.ResolveEffectiveExtent(
            980f,
            1356f,
            1908f));
        AreEqual(980f, WindowResizeMath.ResolveEffectiveExtent(
            980f,
            700f,
            1908f));
        AreEqual(900f, WindowResizeMath.ResolveEffectiveExtent(
            980f,
            700f,
            900f));
        AreEqual(980f, WindowResizeMath.ResolveEffectiveExtent(
            980f,
            700f,
            1908f));

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
        AreEqual(20, configuration.SchemaVersion);
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

    private static void TestAlarmIncidentPolicy()
    {
        AreEqual(
            GameTimeWindowPolicy.SimTicksPerDay * 2,
            AlarmIncidentPolicy.DefaultBurstGapTicks);
        AreEqual(
            GameTimeWindowPolicy.SimTicksPerDay * 10,
            AlarmIncidentPolicy.DefaultPressureWindowTicks);
        AreEqual(4096, AlarmIncidentPolicy.MaximumActiveSamples);
        AreEqual(8192, AlarmIncidentPolicy.MaximumOccurrenceSignals);
        AreEqual(8192, AlarmIncidentPolicy.MaximumActiveInputScan);
        AreEqual(16384, AlarmIncidentPolicy.MaximumOccurrenceInputScan);

        var empty = AlarmIncidentPolicy.Analyze(null, null, 500d);
        IsTrue(empty.IsTimeValid);
        AreEqual(0, empty.ActiveAlarmCount);
        AreEqual(0, empty.ActiveUnacknowledgedCount);
        AreEqual(0, empty.RecentOccurrenceCount);
        AreEqual(0, empty.RecentDistinctAlarmCount);
        AreEqual(0, empty.AlarmPressure);
        AreEqual(AlarmStormLevel.Normal, empty.StormLevel);
        AreEqual(0, empty.Incidents.Count);

        foreach (var invalidNow in new[]
                 {
                     -1d,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                 })
        {
            var invalid = AlarmIncidentPolicy.Analyze(
                new[] { IncidentSample("future", 10d) },
                new[]
                {
                    new AlarmOccurrenceSignal(
                        "future",
                        AlarmSeverity.Emergency,
                        1,
                        10d),
                },
                invalidNow,
                int.MinValue,
                int.MaxValue);
            IsFalse(invalid.IsTimeValid);
            AreEqual(0, invalid.ActiveAlarmCount);
            AreEqual(0, invalid.RecentOccurrenceCount);
            AreEqual(
                AlarmIncidentPolicy.DefaultBurstGapTicks,
                invalid.BurstGapTicks);
            AreEqual(
                GameTimeWindowPolicy.MaximumWindowTicks,
                invalid.PressureWindowTicks);
        }

        var clusteredInput = new[]
        {
            IncidentSample(
                "b",
                140d,
                AlarmSeverity.Critical,
                acknowledged: true,
                sequence: 2),
            IncidentSample(
                "d",
                181d,
                AlarmSeverity.Emergency,
                sequence: 4),
            IncidentSample(
                "a",
                100d,
                AlarmSeverity.Warning,
                sequence: 1),
            IncidentSample(
                "c",
                181d,
                AlarmSeverity.Notice,
                sequence: 3),
        };
        var clustered = AlarmIncidentPolicy.Analyze(
            clusteredInput,
            Array.Empty<AlarmOccurrenceSignal>(),
            500d);
        AreEqual(4, clustered.ActiveAlarmCount);
        AreEqual(3, clustered.ActiveUnacknowledgedCount);
        AreEqual(2, clustered.Incidents.Count);

        // Results are newest-burst first; members and FIRST SIGNAL are
        // chronological with severity/stable-ID tie breaks.
        var newest = clustered.Incidents[0];
        AreEqual(2, newest.MemberCount);
        AreEqual(2, newest.UnacknowledgedCount);
        AreEqual(AlarmSeverity.Emergency, newest.Severity);
        AreEqual(181d, newest.FirstRaisedAtTicks);
        AreEqual(181d, newest.LastRaisedAtTicks);
        AreEqual("d", newest.FirstSignal.StableAlarmId);
        AreEqual("d", newest.Members[0].StableAlarmId);
        AreEqual("c", newest.Members[1].StableAlarmId);
        var older = clustered.Incidents[1];
        AreEqual(2, older.MemberCount);
        AreEqual(1, older.UnacknowledgedCount);
        AreEqual(100d, older.FirstRaisedAtTicks);
        AreEqual(140d, older.LastRaisedAtTicks);
        AreEqual("a", older.FirstSignal.StableAlarmId);

        var reversed = AlarmIncidentPolicy.Analyze(
            clusteredInput.Reverse().ToArray(),
            Array.Empty<AlarmOccurrenceSignal>(),
            500d);
        AreEqual(
            string.Join("|", clustered.Incidents.Select(IncidentSignature)),
            string.Join("|", reversed.Incidents.Select(IncidentSignature)));

        var splitAtBoundary = AlarmIncidentPolicy.Analyze(
            new[]
            {
                IncidentSample("one", 10d),
                IncidentSample("two", 50d),
                IncidentSample("three", 90.000001d),
            },
            null,
            100d);
        AreEqual(2, splitAtBoundary.Incidents.Count);
        AreEqual(1, splitAtBoundary.Incidents[0].MemberCount);
        AreEqual(2, splitAtBoundary.Incidents[1].MemberCount);

        var longText = new string('X',
            AlarmIncidentPolicy.MaximumTextLength + 20);
        var defensiveSamples = new AlarmIncidentActiveSample[]
        {
            IncidentSample(
                "duplicate-late",
                60d,
                AlarmSeverity.Emergency,
                acknowledged: false,
                stableAlarmId: " duplicate ",
                sequence: 20),
            IncidentSample(
                "duplicate-first",
                50d,
                AlarmSeverity.Warning,
                acknowledged: true,
                stableAlarmId: "duplicate",
                name: "FIRST",
                sequence: 10),
            IncidentSample(
                "slot-key",
                70d,
                stableAlarmId: " ",
                slotId: " slot-stable "),
            IncidentSample(
                " key-fallback ",
                80d,
                stableAlarmId: " ",
                slotId: " "),
            IncidentSample(
                "bad-severity",
                90d,
                (AlarmSeverity)999,
                sequence: -50,
                name: longText),
            IncidentSample("future", 101d),
            IncidentSample("negative", -1d),
            IncidentSample("nan", double.NaN),
            IncidentSample("infinity", double.PositiveInfinity),
            IncidentSample(" ", 20d, stableAlarmId: " ", slotId: " "),
            null,
        };
        var defensive = AlarmIncidentPolicy.Analyze(
            defensiveSamples,
            null,
            100d);
        AreEqual(4, defensive.ActiveAlarmCount);
        var flattened = defensive.Incidents
            .SelectMany(incident => incident.Members)
            .ToArray();
        var duplicate = flattened.Single(member =>
            member.StableAlarmId == "duplicate");
        AreEqual(50d, duplicate.RaisedAtTicks);
        AreEqual("FIRST", duplicate.Name);
        AreEqual(AlarmSeverity.Emergency, duplicate.Severity);
        IsFalse(duplicate.IsAcknowledged);
        AreEqual(
            "slot-stable",
            flattened.Single(member => member.Key == "slot-key")
                .StableAlarmId);
        IsTrue(flattened.Any(member =>
            member.StableAlarmId == "key-fallback"));
        var clamped = flattened.Single(member =>
            member.StableAlarmId == "bad-severity");
        AreEqual(AlarmSeverity.Emergency, clamped.Severity);
        AreEqual(0L, clamped.Sequence);
        AreEqual(AlarmIncidentPolicy.MaximumTextLength, clamped.Name.Length);

        var rolledBack = AlarmIncidentPolicy.Analyze(
            new[] { IncidentSample("not-yet", 51d) },
            new[]
            {
                new AlarmOccurrenceSignal(
                    "not-yet",
                    AlarmSeverity.Emergency,
                    1,
                    51d),
            },
            50d);
        IsTrue(rolledBack.IsTimeValid);
        AreEqual(0, rolledBack.ActiveAlarmCount);
        AreEqual(0, rolledBack.RecentOccurrenceCount);

        var pressureSignals = new AlarmOccurrenceSignal[]
        {
            new("boundary", AlarmSeverity.Notice, 1, 100d),
            new("duplicate", AlarmSeverity.Notice, 2, 150d),
            new("duplicate", AlarmSeverity.Emergency, 2, 160d),
            new("warning", AlarmSeverity.Warning, 3, 200d),
            new("warning", AlarmSeverity.Warning, 9, 190d),
            new("too-old", AlarmSeverity.Emergency, 4, 99.999d),
            new("future", AlarmSeverity.Emergency, 5, 200.001d),
            new("nan", AlarmSeverity.Emergency, 6, double.NaN),
            new("infinity", AlarmSeverity.Emergency, 7,
                double.PositiveInfinity),
            new(" ", AlarmSeverity.Emergency, 8, 180d),
            null,
        };
        var pressure = AlarmIncidentPolicy.Analyze(
            null,
            pressureSignals,
            200d,
            pressureWindowTicks: 100);
        AreEqual(4, pressure.RecentOccurrenceCount);
        AreEqual(3, pressure.RecentDistinctAlarmCount);
        AreEqual(13, pressure.AlarmPressure);
        AreEqual(AlarmStormLevel.Elevated, pressure.StormLevel);
        var reversedPressure = AlarmIncidentPolicy.Analyze(
            null,
            pressureSignals.Reverse().ToArray(),
            200d,
            pressureWindowTicks: 100);
        AreEqual(pressure.RecentOccurrenceCount,
            reversedPressure.RecentOccurrenceCount);
        AreEqual(pressure.RecentDistinctAlarmCount,
            reversedPressure.RecentDistinctAlarmCount);
        AreEqual(pressure.AlarmPressure, reversedPressure.AlarmPressure);
        foreach (var pair in new[]
                 {
                     (Pressure: -1, Level: AlarmStormLevel.Normal),
                     (Pressure: 7, Level: AlarmStormLevel.Normal),
                     (Pressure: 8, Level: AlarmStormLevel.Elevated),
                     (Pressure: 15, Level: AlarmStormLevel.Elevated),
                     (Pressure: 16, Level: AlarmStormLevel.Storm),
                     (Pressure: 31, Level: AlarmStormLevel.Storm),
                     (Pressure: 32, Level: AlarmStormLevel.Severe),
                     (Pressure: int.MaxValue, Level: AlarmStormLevel.Severe),
                 })
        {
            AreEqual(
                pair.Level,
                AlarmIncidentPolicy.ResolveStormLevel(pair.Pressure));
        }

        var stormSignals = Enumerable.Range(0, 4)
            .Select(index => new AlarmOccurrenceSignal(
                "emergency-" + index,
                AlarmSeverity.Emergency,
                index,
                200d))
            .ToArray();
        AreEqual(
            AlarmStormLevel.Elevated,
            AlarmIncidentPolicy.Analyze(
                null,
                stormSignals.Take(1).ToArray(),
                200d).StormLevel);
        AreEqual(
            AlarmStormLevel.Storm,
            AlarmIncidentPolicy.Analyze(
                null,
                stormSignals.Take(2).ToArray(),
                200d).StormLevel);
        AreEqual(
            AlarmStormLevel.Severe,
            AlarmIncidentPolicy.Analyze(
                null,
                stormSignals,
                200d).StormLevel);

        var boundedActive = Enumerable.Range(
                0,
                AlarmIncidentPolicy.MaximumActiveSamples + 1)
            .Select(index => IncidentSample(
                "bounded-active-" + index,
                100d,
                index == 0
                    ? AlarmSeverity.Emergency
                    : AlarmSeverity.Notice,
                sequence: index))
            .ToArray();
        var boundedSignals = Enumerable.Range(
                0,
                AlarmIncidentPolicy.MaximumOccurrenceSignals + 1)
            .Select(index => new AlarmOccurrenceSignal(
                "bounded-signal-" + index,
                AlarmSeverity.Emergency,
                index,
                200d))
            .ToArray();
        var bounded = AlarmIncidentPolicy.Analyze(
            boundedActive,
            boundedSignals,
            200d,
            int.MaxValue,
            int.MaxValue);
        AreEqual(
            AlarmIncidentPolicy.MaximumActiveSamples,
            bounded.ActiveAlarmCount);
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals,
            bounded.RecentOccurrenceCount);
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals,
            bounded.RecentDistinctAlarmCount);
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals * 8,
            bounded.AlarmPressure);
        AreEqual(
            GameTimeWindowPolicy.MaximumWindowTicks,
            bounded.BurstGapTicks);
        AreEqual(
            GameTimeWindowPolicy.MaximumWindowTicks,
            bounded.PressureWindowTicks);

        var scanBoundedActive = Enumerable.Range(
                0,
                AlarmIncidentPolicy.MaximumActiveInputScan)
            .Select(index => IncidentSample(
                "scan-active-" + index,
                100d,
                AlarmSeverity.Notice,
                sequence: index))
            .ToList();
        scanBoundedActive.Add(IncidentSample(
            "beyond-active-scan",
            200d,
            AlarmSeverity.Emergency));
        var scanBoundedSignals = Enumerable.Range(
                0,
                AlarmIncidentPolicy.MaximumOccurrenceInputScan)
            .Select(index => new AlarmOccurrenceSignal(
                "scan-signal-" + index,
                AlarmSeverity.Notice,
                index,
                100d))
            .ToList();
        scanBoundedSignals.Add(new AlarmOccurrenceSignal(
            "beyond-signal-scan",
            AlarmSeverity.Emergency,
            long.MaxValue,
            200d));
        var hardScanBounded = AlarmIncidentPolicy.Analyze(
            scanBoundedActive,
            scanBoundedSignals,
            200d);
        AreEqual(
            AlarmIncidentPolicy.MaximumActiveSamples,
            hardScanBounded.ActiveAlarmCount);
        IsFalse(hardScanBounded.Incidents
            .SelectMany(incident => incident.Members)
            .Any(member => member.StableAlarmId == "beyond-active-scan"));
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals,
            hardScanBounded.RecentOccurrenceCount);
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals,
            hardScanBounded.RecentDistinctAlarmCount);
        AreEqual(
            AlarmIncidentPolicy.MaximumOccurrenceSignals,
            hardScanBounded.AlarmPressure);

        var maximumTick = AlarmIncidentPolicy.Analyze(
            new[]
            {
                IncidentSample("maximum", double.MaxValue),
            },
            new[]
            {
                new AlarmOccurrenceSignal(
                    "maximum",
                    (AlarmSeverity)int.MaxValue,
                    long.MinValue,
                    double.MaxValue),
            },
            double.MaxValue);
        IsTrue(maximumTick.IsTimeValid);
        AreEqual(1, maximumTick.ActiveAlarmCount);
        AreEqual(1, maximumTick.RecentOccurrenceCount);
        AreEqual(1, maximumTick.RecentDistinctAlarmCount);
        AreEqual(8, maximumTick.AlarmPressure);
        AreEqual(AlarmStormLevel.Elevated, maximumTick.StormLevel);
        IsTrue(maximumTick.Incidents is
            System.Collections.ObjectModel.ReadOnlyCollection<AlarmIncident>);
        IsTrue(maximumTick.Incidents[0].Members is
            System.Collections.ObjectModel.ReadOnlyCollection<AlarmIncidentMember>);
    }

    private static AlarmIncidentActiveSample IncidentSample(
        string key,
        double raisedAtTicks,
        AlarmSeverity severity = AlarmSeverity.Warning,
        bool acknowledged = false,
        string stableAlarmId = null,
        string slotId = null,
        string name = null,
        long sequence = 1)
    {
        return new AlarmIncidentActiveSample(
            key,
            stableAlarmId ?? key,
            name ?? key,
            "DETAIL",
            "custom",
            "panel",
            slotId ?? stableAlarmId ?? key,
            42,
            "prototype",
            "ENTITY",
            severity,
            sequence,
            raisedAtTicks,
            acknowledged);
    }

    private static string IncidentSignature(AlarmIncident incident)
    {
        return incident.IncidentId + ":" + incident.Severity + ":" +
               incident.UnacknowledgedCount + ":" +
               string.Join(",", incident.Members.Select(member =>
                   member.StableAlarmId + "@" + member.RaisedAtTicks));
    }

    private static void TestAlarmAreaPolicy()
    {
        AreEqual(64, AlarmAreaPolicy.MaximumAreaCount);
        AreEqual(40, AlarmAreaPolicy.MaximumDraftNameLength);
        AreEqual(40, AlarmAreaPolicy.MaximumStoredNameLength);
        AreEqual(AlarmAreaFilterKind.All, AlarmAreaFilter.All.Kind);
        AreEqual("", AlarmAreaFilter.All.AreaId);
        AreEqual(
            AlarmAreaFilterKind.Unassigned,
            AlarmAreaFilter.Unassigned.Kind);
        AreEqual("", AlarmAreaFilter.Unassigned.AreaId);
        AreEqual(
            AlarmAreaFilterKind.Area,
            AlarmAreaFilter.ForArea(" north ").Kind);
        AreEqual("north", AlarmAreaFilter.ForArea(" north ").AreaId);

        var fortyCharacters = new string('X', 40);
        var generatedIds = new Queue<string>(new[]
        {
            " alpha ",
            " beta ",
            " ",
            "gamma",
        });
        var generatedIdCalls = 0;
        var malformed = new List<AlarmAreaDefinition>
        {
            null,
            new() { Id = " alpha ", Name = " North " },
            new() { Id = "alpha", Name = " north " },
            new() { Id = "ALPHA", Name = " " },
            new() { Id = "", Name = fortyCharacters + "TAIL" },
            new() { Id = "delta", Name = fortyCharacters },
            new() { Id = "epsilon", Name = null },
        };
        var normalized = AlarmAreaPolicy.Normalize(
            malformed,
            () =>
            {
                generatedIdCalls++;
                return generatedIds.Dequeue();
            });
        AreEqual(6, normalized.Count);
        AreEqual(4, generatedIdCalls);
        AreEqual("alpha", normalized[0].Id);
        AreEqual("North", normalized[0].Name);
        AreEqual("beta", normalized[1].Id);
        AreEqual("north (2)", normalized[1].Name);
        AreEqual("ALPHA", normalized[2].Id);
        AreEqual("AREA", normalized[2].Name);
        AreEqual("gamma", normalized[3].Id);
        AreEqual(fortyCharacters, normalized[3].Name);
        AreEqual("delta", normalized[4].Id);
        AreEqual(new string('X', 36) + " (2)", normalized[4].Name);
        AreEqual("epsilon", normalized[5].Id);
        AreEqual("AREA (2)", normalized[5].Name);
        IsTrue(ReferenceEquals(malformed[1], normalized[0]));
        IsTrue(ReferenceEquals(malformed[6], normalized[5]));
        var firstNormalization = string.Join(
            "|",
            normalized.Select(area => area.Id + ":" + area.Name));
        var secondGeneratorCalls = 0;
        var normalizedAgain = AlarmAreaPolicy.Normalize(
            normalized,
            () =>
            {
                secondGeneratorCalls++;
                throw new InvalidOperationException("must not be called");
            });
        AreEqual(0, secondGeneratorCalls);
        AreEqual(
            firstNormalization,
            string.Join(
                "|",
                normalizedAgain.Select(area => area.Id + ":" + area.Name)));
        for (var index = 0; index < normalized.Count; index++)
        {
            IsTrue(ReferenceEquals(normalized[index], normalizedAgain[index]));
        }

        var fallbackCalls = 0;
        var deterministicFallback = AlarmAreaPolicy.Normalize(
            new[]
            {
                new AlarmAreaDefinition { Id = "area", Name = "ONE" },
                new AlarmAreaDefinition { Id = "", Name = "TWO" },
            },
            () =>
            {
                fallbackCalls++;
                return "area";
            });
        AreEqual(128, fallbackCalls);
        AreEqual("area", deterministicFallback[0].Id);
        AreEqual("area-2", deterministicFallback[1].Id);
        var throwingGeneratorCalls = 0;
        var throwingFallback = AlarmAreaPolicy.Normalize(
            new[]
            {
                new AlarmAreaDefinition { Id = "", Name = "SAFE" },
            },
            () =>
            {
                throwingGeneratorCalls++;
                throw new InvalidOperationException("generator failed");
            });
        AreEqual(1, throwingGeneratorCalls);
        AreEqual("area", throwingFallback[0].Id);

        var oversized = new List<AlarmAreaDefinition> { null };
        oversized.AddRange(Enumerable.Range(0, 70).Select(index =>
            new AlarmAreaDefinition
            {
                Id = "area-" + index,
                Name = "AREA " + index,
            }));
        var capped = AlarmAreaPolicy.Normalize(oversized);
        AreEqual(64, capped.Count);
        AreEqual("area-0", capped[0].Id);
        AreEqual("area-63", capped[63].Id);
        AreEqual(71, oversized.Count);
        AreEqual(64, capped.Select(area => area.Id)
            .Distinct(StringComparer.Ordinal).Count());
        AreEqual(64, capped.Select(area => area.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        AreEqual(0, AlarmAreaPolicy.Normalize(null).Count);

        var north = new AlarmAreaDefinition
        {
            Id = "north",
            Name = "NORTH",
        };
        var magicAll = new AlarmAreaDefinition
        {
            Id = "all",
            Name = "LITERAL ALL AREA",
        };
        var filterAreas = new[] { north, magicAll };
        var dashboard = new PanelDefinition
        {
            Id = "home",
            IsDashboard = true,
            AreaId = "north",
        };
        var northPanel = new PanelDefinition
        {
            Id = "north-panel",
            AreaId = " north ",
        };
        var unassignedPanel = new PanelDefinition
        {
            Id = "unassigned-panel",
            AreaId = "",
        };
        var magicPanel = new PanelDefinition
        {
            Id = "magic-panel",
            AreaId = "all",
        };
        var orphanPanel = new PanelDefinition
        {
            Id = "orphan-panel",
            AreaId = "missing",
        };
        var entityPanel = new PanelDefinition
        {
            Id = "entity-panel",
            OwnerEntityId = 42,
            AreaId = "north",
        };
        var filterPanels = new PanelDefinition[]
        {
            dashboard,
            northPanel,
            unassignedPanel,
            magicPanel,
            orphanPanel,
            entityPanel,
            null,
        };
        var allPanels = AlarmAreaPolicy.SelectGlobalPanels(
            filterPanels,
            AlarmAreaFilter.All);
        AreEqual(5, allPanels.Count);
        IsTrue(ReferenceEquals(dashboard, allPanels[0]));
        IsTrue(ReferenceEquals(orphanPanel, allPanels[4]));
        IsFalse(allPanels.Contains(entityPanel));
        var unassignedPanels = AlarmAreaPolicy.SelectGlobalPanels(
            filterPanels,
            AlarmAreaFilter.Unassigned);
        AreEqual(1, unassignedPanels.Count);
        IsTrue(ReferenceEquals(unassignedPanel, unassignedPanels[0]));
        var northPanels = AlarmAreaPolicy.SelectGlobalPanels(
            filterPanels,
            AlarmAreaFilter.ForArea("north"));
        AreEqual(1, northPanels.Count);
        IsTrue(ReferenceEquals(northPanel, northPanels[0]));
        var literalAllPanels = AlarmAreaPolicy.SelectGlobalPanels(
            filterPanels,
            AlarmAreaFilter.ForArea("all"));
        AreEqual(1, literalAllPanels.Count);
        IsTrue(ReferenceEquals(magicPanel, literalAllPanels[0]));
        AreEqual(
            allPanels.Count,
            AlarmAreaPolicy.Select(filterPanels, AlarmAreaFilter.All).Count);

        var normalizedFilter = AlarmAreaPolicy.NormalizeFilter(
            AlarmAreaFilter.ForArea(" north "),
            filterAreas);
        AreEqual(AlarmAreaFilterKind.Area, normalizedFilter.Kind);
        AreEqual("north", normalizedFilter.AreaId);
        AreEqual(
            AlarmAreaFilterKind.All,
            AlarmAreaPolicy.NormalizeFilter(
                AlarmAreaFilter.ForArea("missing"),
                filterAreas).Kind);
        AreEqual(
            AlarmAreaFilterKind.All,
            AlarmAreaPolicy.NormalizeFilter(
                new AlarmAreaFilter((AlarmAreaFilterKind)99, "north"),
                filterAreas).Kind);
        AreEqual(
            AlarmAreaFilterKind.Unassigned,
            AlarmAreaPolicy.NormalizeFilter(
                new AlarmAreaFilter(
                    AlarmAreaFilterKind.Unassigned,
                    "north"),
                filterAreas).Kind);

        var assignmentOrder = filterPanels.Where(panel => panel != null)
            .Select(panel => panel.Id).ToArray();
        AlarmAreaPolicy.NormalizePanelAssignments(
            filterPanels,
            filterAreas);
        AreEqual("", dashboard.AreaId);
        AreEqual("north", northPanel.AreaId);
        AreEqual("", unassignedPanel.AreaId);
        AreEqual("all", magicPanel.AreaId);
        AreEqual("", orphanPanel.AreaId);
        AreEqual("", entityPanel.AreaId);
        IsTrue(assignmentOrder.SequenceEqual(
            filterPanels.Where(panel => panel != null)
                .Select(panel => panel.Id),
            StringComparer.Ordinal));
        AlarmAreaPolicy.NormalizePanelAssignments(
            filterPanels,
            filterAreas,
            discardAssignments: true);
        IsTrue(filterPanels.Where(panel => panel != null).All(panel =>
            panel.AreaId == ""));
        IsFalse(AlarmAreaPolicy.IsAssignablePanel(dashboard));
        IsFalse(AlarmAreaPolicy.IsAssignablePanel(entityPanel));
        IsFalse(AlarmAreaPolicy.IsAssignablePanel(null));
        IsTrue(AlarmAreaPolicy.IsAssignablePanel(northPanel));

        var replacementDraft = new List<AlarmAreaDefinition>
        {
            new() { Id = " one ", Name = " First " },
            new() { Id = "ONE", Name = " Second " },
        };
        IsTrue(AlarmAreaPolicy.ValidateReplacement(
            replacementDraft,
            out var replacement,
            out var failure));
        AreEqual(AlarmAreaMutationFailure.None, failure);
        AreEqual(2, replacement.Count);
        AreEqual("one", replacement[0].Id);
        AreEqual("First", replacement[0].Name);
        AreEqual("ONE", replacement[1].Id);
        AreEqual("Second", replacement[1].Name);
        IsFalse(ReferenceEquals(replacementDraft, replacement));
        IsFalse(ReferenceEquals(replacementDraft[0], replacement[0]));
        AreEqual(" one ", replacementDraft[0].Id);
        AreEqual(" First ", replacementDraft[0].Name);

        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            null,
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidId, failure);
        AreEqual(0, replacement.Count);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new AlarmAreaDefinition[] { null },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidId, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new[]
            {
                new AlarmAreaDefinition { Id = " ", Name = "NAME" },
            },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidId, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new[]
            {
                new AlarmAreaDefinition { Id = "same", Name = "ONE" },
                new AlarmAreaDefinition { Id = " same ", Name = "TWO" },
            },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidId, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            Enumerable.Range(0, 65).Select(index =>
                new AlarmAreaDefinition
                {
                    Id = "id-" + index,
                    Name = "NAME " + index,
                }),
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.TooManyAreas, failure);
        AreEqual(0, replacement.Count);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new[]
            {
                new AlarmAreaDefinition { Id = "empty", Name = " " },
            },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidName, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new[]
            {
                new AlarmAreaDefinition
                {
                    Id = "long",
                    Name = new string('N', 41),
                },
            },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.NameTooLong, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            new[]
            {
                new AlarmAreaDefinition { Id = "a", Name = "Alpha" },
                new AlarmAreaDefinition { Id = "b", Name = " alpha " },
            },
            out replacement,
            out failure));
        AreEqual(AlarmAreaMutationFailure.DuplicateName, failure);
        AreEqual(0, replacement.Count);

        IsTrue(AlarmAreaPolicy.ValidateReplacement(
            replacementDraft,
            "one",
            " first ",
            out var replacementName,
            out failure));
        AreEqual("first", replacementName);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            replacementDraft,
            "one",
            " second ",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.DuplicateName, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            replacementDraft,
            "one",
            " ",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidName, failure);
        IsFalse(AlarmAreaPolicy.ValidateReplacement(
            replacementDraft,
            "one",
            new string('N', 41),
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.NameTooLong, failure);

        var mutationAreas = new List<AlarmAreaDefinition>
        {
            new() { Id = "one", Name = "ONE" },
            new() { Id = "two", Name = "TWO" },
        };
        var createIds = new Queue<string>(new[] { " one ", " three " });
        IsTrue(AlarmAreaPolicy.TryCreate(
            mutationAreas,
            " THREE ",
            () => createIds.Dequeue(),
            out var created,
            out failure));
        AreEqual(AlarmAreaMutationFailure.None, failure);
        IsTrue(ReferenceEquals(created, mutationAreas[2]));
        AreEqual("three", created.Id);
        AreEqual("THREE", created.Name);
        IsFalse(AlarmAreaPolicy.TryCreate(
            mutationAreas,
            " two ",
            () => "unused",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.DuplicateName, failure);
        AreEqual(3, mutationAreas.Count);
        var fullDraft = Enumerable.Range(0, 64).Select(index =>
            new AlarmAreaDefinition
            {
                Id = "full-" + index,
                Name = "FULL " + index,
            }).ToList();
        var fullGeneratorCalls = 0;
        IsFalse(AlarmAreaPolicy.TryCreate(
            fullDraft,
            "OVERFLOW",
            () =>
            {
                fullGeneratorCalls++;
                return "overflow";
            },
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.TooManyAreas, failure);
        AreEqual(0, fullGeneratorCalls);
        AreEqual(64, fullDraft.Count);
        IsFalse(AlarmAreaPolicy.TryCreate(
            mutationAreas,
            "FOUR",
            null,
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.IdGenerationFailed, failure);

        IsTrue(AlarmAreaPolicy.TryRename(
            mutationAreas,
            " one ",
            " FIRST RENAMED ",
            out failure));
        AreEqual("FIRST RENAMED", mutationAreas[0].Name);
        IsFalse(AlarmAreaPolicy.TryRename(
            mutationAreas,
            "one",
            " two ",
            out failure));
        AreEqual(AlarmAreaMutationFailure.DuplicateName, failure);
        AreEqual("FIRST RENAMED", mutationAreas[0].Name);
        IsFalse(AlarmAreaPolicy.TryRename(
            mutationAreas,
            "missing",
            "MISSING",
            out failure));
        AreEqual(AlarmAreaMutationFailure.AreaNotFound, failure);

        IsTrue(AlarmAreaPolicy.TryMove(
            mutationAreas,
            "three",
            0,
            out failure));
        AreEqual("three", mutationAreas[0].Id);
        AreEqual("one", mutationAreas[1].Id);
        AreEqual("two", mutationAreas[2].Id);
        IsTrue(AlarmAreaPolicy.TryMove(
            mutationAreas,
            "one",
            1,
            out failure));
        IsFalse(AlarmAreaPolicy.TryMove(
            mutationAreas,
            "one",
            -1,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidTargetIndex, failure);
        IsFalse(AlarmAreaPolicy.TryMove(
            mutationAreas,
            "one",
            3,
            out failure));
        AreEqual(AlarmAreaMutationFailure.InvalidTargetIndex, failure);
        IsFalse(AlarmAreaPolicy.TryMove(
            mutationAreas,
            "missing",
            0,
            out failure));
        AreEqual(AlarmAreaMutationFailure.AreaNotFound, failure);

        var assignDashboard = new PanelDefinition
        {
            Id = "assign-home",
            IsDashboard = true,
        };
        var assignGlobal = new PanelDefinition
        {
            Id = "assign-global",
        };
        var assignEntity = new PanelDefinition
        {
            Id = "assign-entity",
            OwnerEntityId = 7,
        };
        var assignPanels = new[]
        {
            assignDashboard,
            assignGlobal,
            assignEntity,
        };
        IsTrue(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            " assign-global ",
            " one ",
            out var assignedPanel,
            out failure));
        IsTrue(ReferenceEquals(assignGlobal, assignedPanel));
        AreEqual("one", assignGlobal.AreaId);
        IsFalse(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            "assign-global",
            "missing",
            out assignedPanel,
            out failure));
        IsTrue(assignedPanel == null);
        AreEqual(AlarmAreaMutationFailure.AreaNotFound, failure);
        AreEqual("one", assignGlobal.AreaId);
        IsTrue(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            "assign-global",
            " ",
            out assignedPanel,
            out failure));
        AreEqual("", assignGlobal.AreaId);
        IsFalse(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            "assign-home",
            "one",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.PanelNotAssignable, failure);
        IsFalse(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            "assign-entity",
            "one",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.PanelNotAssignable, failure);
        IsFalse(AlarmAreaPolicy.TryAssign(
            assignPanels,
            mutationAreas,
            "missing-panel",
            "one",
            out _,
            out failure));
        AreEqual(AlarmAreaMutationFailure.PanelNotFound, failure);

        var deletePanels = new List<PanelDefinition>
        {
            new() { Id = "delete-a", AreaId = "two" },
            new()
            {
                Id = "delete-home",
                IsDashboard = true,
                AreaId = "two",
            },
            new()
            {
                Id = "delete-entity",
                OwnerEntityId = 9,
                AreaId = "two",
            },
            new() { Id = "delete-other", AreaId = "one" },
        };
        var deletePanelOrder = deletePanels.ToArray();
        IsTrue(AlarmAreaPolicy.TryDelete(
            mutationAreas,
            deletePanels,
            " two ",
            out var unassignedCount,
            out failure));
        AreEqual(3, unassignedCount);
        AreEqual(2, mutationAreas.Count);
        IsFalse(mutationAreas.Any(area => area.Id == "two"));
        AreEqual("one", deletePanels[3].AreaId);
        IsTrue(deletePanels.Take(3).All(panel => panel.AreaId == ""));
        AreEqual(deletePanelOrder.Length, deletePanels.Count);
        for (var index = 0; index < deletePanelOrder.Length; index++)
        {
            IsTrue(ReferenceEquals(deletePanelOrder[index], deletePanels[index]));
        }
        IsFalse(AlarmAreaPolicy.TryDelete(
            mutationAreas,
            deletePanels,
            "missing",
            out unassignedCount,
            out failure));
        AreEqual(0, unassignedCount);
        AreEqual(AlarmAreaMutationFailure.AreaNotFound, failure);
        AreEqual("one", deletePanels[3].AreaId);

        var cloneAreaSource = new PanelDefinition
        {
            Id = "clone-area-source",
            AreaId = " one ",
        };
        AreEqual("one", AlarmAreaPolicy.CloneAreaId(cloneAreaSource));
        AreEqual(
            "one",
            AlarmAreaPolicy.CloneAreaId(cloneAreaSource, mutationAreas));
        cloneAreaSource.AreaId = "orphan";
        AreEqual(
            "",
            AlarmAreaPolicy.CloneAreaId(cloneAreaSource, mutationAreas));
        cloneAreaSource.IsDashboard = true;
        AreEqual("", AlarmAreaPolicy.CloneAreaId(cloneAreaSource));

        var current = UnmaConfiguration.CreateDefault();
        current.AlarmAreas.Add(new AlarmAreaDefinition
        {
            Id = " production ",
            Name = " Production ",
        });
        current.Panels[1].AreaId = " production ";
        current.Normalize();
        AreEqual(20, current.SchemaVersion);
        AreEqual(1, current.AlarmAreas.Count);
        AreEqual("production", current.AlarmAreas[0].Id);
        AreEqual("Production", current.AlarmAreas[0].Name);
        AreEqual("production", current.Panels[1].AreaId);
        AreEqual("", current.Panels[0].AreaId);
        var currentSnapshot = string.Join(
            "|",
            current.AlarmAreas.Select(area => area.Id + ":" + area.Name)) +
            ";" + string.Join("|", current.Panels.Select(panel =>
                panel.Id + ":" + panel.AreaId));
        current.Normalize();
        AreEqual(
            currentSnapshot,
            string.Join(
                "|",
                current.AlarmAreas.Select(area => area.Id + ":" + area.Name)) +
            ";" + string.Join("|", current.Panels.Select(panel =>
                panel.Id + ":" + panel.AreaId)));

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaConfiguration));
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, current);
            stream.Position = 0;
            var restored =
                (UnmaConfiguration)serializer.ReadObject(stream);
            restored.Normalize();
            AreEqual(20, restored.SchemaVersion);
            AreEqual(1, restored.AlarmAreas.Count);
            AreEqual("production", restored.AlarmAreas[0].Id);
            AreEqual("Production", restored.AlarmAreas[0].Name);
            AreEqual("production", restored.Panels.Single(panel =>
                panel.Id == "supply").AreaId);
            AreEqual("", restored.Panels.Single(panel =>
                panel.IsDashboard).AreaId);
        }

        var legacy = UnmaConfiguration.CreateDefault();
        legacy.SchemaVersion = 19;
        legacy.AlarmAreas = new List<AlarmAreaDefinition>
        {
            new() { Id = "legacy-area", Name = "LEGACY" },
        };
        legacy.Panels[0].AreaId = "legacy-area";
        legacy.Panels[1].AreaId = "legacy-area";
        legacy.Panels[1].Name = "SUPPLY PRESERVED";
        legacy.Panels[1].Columns = 5;
        legacy.Panels[1].IncludeVanilla = false;
        legacy.Panels[1].NotificationFilter = "preserved-filter";
        var legacyPanelOrder = legacy.Panels.ToArray();
        legacy.Normalize();
        AreEqual(20, legacy.SchemaVersion);
        AreEqual(0, legacy.AlarmAreas.Count);
        IsTrue(legacy.Panels.All(panel => panel.AreaId == ""));
        AreEqual(legacyPanelOrder.Length, legacy.Panels.Count);
        for (var index = 0; index < legacyPanelOrder.Length; index++)
        {
            IsTrue(ReferenceEquals(legacyPanelOrder[index], legacy.Panels[index]));
        }
        AreEqual("SUPPLY PRESERVED", legacy.Panels[1].Name);
        AreEqual(5, legacy.Panels[1].Columns);
        IsFalse(legacy.Panels[1].IncludeVanilla);
        AreEqual(
            "preserved-filter",
            legacy.Panels[1].NotificationFilter);
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
            AreaId = "production-area",
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
        AreEqual("production-area", plan.Panel.AreaId);
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
            AlarmAreas = new List<AlarmAreaDefinition>
            {
                new()
                {
                    Id = "production-area",
                    Name = "PRODUCTION",
                },
            },
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
        AreEqual("production-area", normalizedClone.AreaId);
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
        AreEqual("production-area", restoredClone.AreaId);
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

    private static void TestGroupedVanillaNotificationPolicy()
    {
        const string prototypeId = "NotEnoughPowerForEntity";
        const string overrideId = "vanilla:NotEnoughPowerForEntity";
        const string groupKey =
            "vanilla:group:NotEnoughPowerForEntity";

        AreEqual(
            prototypeId,
            GroupedVanillaNotificationPolicy.PrototypeId);
        AreEqual(overrideId, GroupedVanillaNotificationPolicy.OverrideId);
        AreEqual(groupKey, GroupedVanillaNotificationPolicy.GroupKey);
        AreEqual(overrideId, GroupedVanillaNotificationPolicy.SlotId);
        IsTrue(GroupedVanillaNotificationPolicy.IsGroupedPrototype(
            prototypeId));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupedPrototype(
            "NotEnoughPower"));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupedPrototype(
            "notenoughpowerforentity"));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupedPrototype(
            " " + prototypeId + " "));
        IsTrue(GroupedVanillaNotificationPolicy.IsGroupedOverride(
            overrideId));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupedOverride(
            "vanilla:NotEnoughPower"));
        IsTrue(GroupedVanillaNotificationPolicy.IsGroupedOverrideId(
            overrideId));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupedOverrideId(
            "vanilla:NotEnoughPower"));
        IsTrue(GroupedVanillaNotificationPolicy.IsGroupKey(groupKey));
        IsFalse(GroupedVanillaNotificationPolicy.IsGroupKey(overrideId));
        AreEqual(
            groupKey,
            GroupedVanillaNotificationPolicy.AlarmKeyForNotification(
                prototypeId,
                "vanilla:notification-17"));
        AreEqual(
            "vanilla:notification-17",
            GroupedVanillaNotificationPolicy.AlarmKeyForNotification(
                "NotEnoughPower",
                "vanilla:notification-17"));
        AreEqual(
            "",
            GroupedVanillaNotificationPolicy.AlarmKeyForNotification(
                "NotEnoughPower",
                null));

        foreach (var groupedSlotId in new[]
                 {
                     overrideId,
                     " " + overrideId + " ",
                     groupKey,
                     overrideId + ":entity:17",
                     overrideId + ":legacy:ABCDEF12",
                 })
        {
            IsTrue(GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                groupedSlotId));
            AreEqual(
                overrideId,
                GroupedVanillaNotificationPolicy.CanonicalizeSlotId(
                    groupedSlotId));
        }
        foreach (var unrelatedSlotId in new[]
                 {
                     "",
                     "vanilla:NotEnoughPower",
                     overrideId + ":entity:",
                     overrideId + ":legacy:",
                     overrideId + ":other:17",
                 })
        {
            IsFalse(GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                unrelatedSlotId));
        }
        AreEqual(
            "vanilla:Other",
            GroupedVanillaNotificationPolicy.CanonicalizeSlotId(
                " vanilla:Other "));
        AreEqual(
            prototypeId,
            GroupedVanillaNotificationPolicy.FormatTitle("", 1));
        AreEqual(
            "POWER FAILURE",
            GroupedVanillaNotificationPolicy.FormatTitle(
                " POWER FAILURE ",
                1));
        AreEqual(
            "POWER FAILURE ×3",
            GroupedVanillaNotificationPolicy.FormatTitle(
                "POWER FAILURE",
                3));
        AreEqual(
            prototypeId + " ×2",
            GroupedVanillaNotificationPolicy.FormatDetail(null, 2));
        AreEqual(
            "NotEnoughPowerForEntity · Arc furnace II ×20000",
            GroupedVanillaNotificationPolicy.FormatDetail(
                " NotEnoughPowerForEntity · Arc furnace II ",
                20000));

        var invalidMember = new GroupedVanillaNotificationMemberSnapshot(
            " ",
            "ignored",
            "ignored");
        var tracker = new GroupedVanillaNotificationTracker();
        var empty = tracker.GetSnapshot();
        AreEqual(0, empty.Count);
        IsFalse(empty.HasMembers);
        IsFalse(empty.IsLastClearPending);
        IsFalse(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(null));
        IsFalse(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(empty));
        AreEqual<GroupedVanillaNotificationMemberSnapshot>(
            null,
            empty.OldestRepresentative);
        AreEqual(0, tracker.Add(null).Count);
        AreEqual(0, tracker.Add(invalidMember).Count);

        var memberA = new GroupedVanillaNotificationMemberSnapshot(
            " vanilla:notification-17 ",
            " ARC FURNACE II ",
            " NotEnoughPowerForEntity · Arc furnace II ",
            isSuppressed: false,
            entityId: 17,
            entityPrototypeId: " AirSeparatorT2 ",
            entityTitle: " Arc furnace II ");
        AreEqual("vanilla:notification-17", memberA.NotificationKey);
        AreEqual("ARC FURNACE II", memberA.Title);
        AreEqual(
            "NotEnoughPowerForEntity · Arc furnace II",
            memberA.Detail);
        IsFalse(memberA.IsSuppressed);
        AreEqual(17, memberA.EntityId);
        AreEqual("AirSeparatorT2", memberA.EntityPrototypeId);
        AreEqual("Arc furnace II", memberA.EntityTitle);

        var afterA = tracker.Add(memberA);
        AreEqual(1, afterA.Count);
        AreEqual(
            "vanilla:notification-17",
            tracker.GetNotificationKeys().Single());
        Throws<NotSupportedException>(() =>
            ((IList<string>)tracker.GetNotificationKeys()).Clear());
        IsTrue(afterA.HasMembers);
        IsFalse(afterA.IsLastClearPending);
        IsFalse(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(afterA));
        IsTrue(ReferenceEquals(memberA, afterA.OldestRepresentative));
        IsTrue(tracker.Contains(" vanilla:notification-17 "));
        IsFalse(tracker.Contains("vanilla:notification-18"));

        var refreshedA = new GroupedVanillaNotificationMemberSnapshot(
            "vanilla:notification-17",
            "ARC FURNACE II UPDATED",
            "updated",
            isSuppressed: true,
            entityId: 17,
            entityPrototypeId: "AirSeparatorT2",
            entityTitle: "Arc furnace II");
        var afterDuplicateA = tracker.Add(refreshedA);
        AreEqual(1, afterDuplicateA.Count);
        IsTrue(ReferenceEquals(
            refreshedA,
            afterDuplicateA.OldestRepresentative));
        IsTrue(afterDuplicateA.OldestRepresentative.IsSuppressed);
        IsTrue(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(afterDuplicateA));

        var memberB = new GroupedVanillaNotificationMemberSnapshot(
            "vanilla:notification-18",
            "COPPER ELECTROLYSIS",
            "NotEnoughPowerForEntity · Copper electrolysis",
            entityId: 18,
            entityPrototypeId: "AirSeparatorT3",
            entityTitle: "Copper electrolysis");
        var heldOneMemberSnapshot = afterDuplicateA;
        var afterB = tracker.Add(memberB);
        AreEqual(2, afterB.Count);
        IsFalse(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(afterB));
        AreEqual(1, heldOneMemberSnapshot.Count);
        IsTrue(ReferenceEquals(
            refreshedA,
            afterB.OldestRepresentative));
        Throws<NotSupportedException>(() =>
            ((IList<GroupedVanillaNotificationMemberSnapshot>)
                afterB.Members).Clear());

        var afterRemoveB = tracker.Remove("vanilla:notification-18");
        AreEqual(1, afterRemoveB.Count);
        IsTrue(GroupedVanillaNotificationPolicy
            .AreAllMembersSuppressed(afterRemoveB));
        IsFalse(afterRemoveB.IsLastClearPending);
        IsTrue(ReferenceEquals(
            refreshedA,
            afterRemoveB.OldestRepresentative));
        var afterDuplicateRemoveB = tracker.Remove(
            "vanilla:notification-18");
        AreEqual(1, afterDuplicateRemoveB.Count);
        IsFalse(afterDuplicateRemoveB.IsLastClearPending);

        tracker.Add(memberB);
        var afterRemoveOldest = tracker.Remove(
            "vanilla:notification-17");
        AreEqual(1, afterRemoveOldest.Count);
        IsTrue(ReferenceEquals(
            memberB,
            afterRemoveOldest.OldestRepresentative));
        var pendingLastClear = tracker.Remove(
            "vanilla:notification-18");
        AreEqual(0, pendingLastClear.Count);
        IsFalse(pendingLastClear.HasMembers);
        IsTrue(pendingLastClear.IsLastClearPending);
        IsTrue(ReferenceEquals(
            memberB,
            pendingLastClear.PendingClearRepresentative));
        AreEqual(0, tracker.GetNotificationKeys().Count);
        var duplicatePendingRemove = tracker.Remove(
            "vanilla:notification-18");
        IsTrue(duplicatePendingRemove.IsLastClearPending);
        GroupedVanillaNotificationMemberSnapshot clearRepresentative;

        var reusedB = new GroupedVanillaNotificationMemberSnapshot(
            "vanilla:notification-18",
            "COPPER ELECTROLYSIS REUSED",
            "reused notification id",
            entityId: 28);
        var readdedBeforeClear = tracker.Add(reusedB);
        AreEqual(1, readdedBeforeClear.Count);
        IsFalse(readdedBeforeClear.IsLastClearPending);
        IsFalse(tracker.TryTakePendingLastClear(out clearRepresentative));
        AreEqual<GroupedVanillaNotificationMemberSnapshot>(
            null,
            clearRepresentative);
        IsTrue(ReferenceEquals(
            reusedB,
            readdedBeforeClear.OldestRepresentative));

        tracker.Remove("vanilla:notification-18");
        IsTrue(tracker.TryTakePendingLastClear(out clearRepresentative));
        IsTrue(ReferenceEquals(reusedB, clearRepresentative));
        IsFalse(tracker.TryTakePendingLastClear(out clearRepresentative));
        AreEqual<GroupedVanillaNotificationMemberSnapshot>(
            null,
            clearRepresentative);

        // A reused NotificationId after a committed clear is a fresh member,
        // while a duplicate add during the same group remains deduplicated.
        var freshA = new GroupedVanillaNotificationMemberSnapshot(
            "vanilla:notification-17",
            "ARC FURNACE II FRESH",
            "fresh occurrence");
        AreEqual(1, tracker.Add(freshA).Count);
        AreEqual(1, tracker.Add(freshA).Count);
        tracker.Clear();
        var cleared = tracker.GetSnapshot();
        AreEqual(0, cleared.Count);
        IsFalse(cleared.IsLastClearPending);
        IsFalse(tracker.Contains("vanilla:notification-17"));
    }

    private static void TestGroupedVanillaNotificationNormalization()
    {
        const string prototypeId = "NotEnoughPowerForEntity";
        const string overrideId = "vanilla:NotEnoughPowerForEntity";
        const string groupKey =
            "vanilla:group:NotEnoughPowerForEntity";

        var slotConfiguration = UnmaConfiguration.CreateDefault();
        slotConfiguration.Panels = new List<PanelDefinition>
        {
            new()
            {
                Id = "global-group-migration",
                Name = "GLOBAL",
                IsDashboard = true,
                IncludeVanilla = false,
                IncludeSystem = false,
                Slots = new List<PanelSlotDefinition>
                {
                    new() { AlarmId = overrideId },
                    new() { AlarmId = overrideId + ":entity:17" },
                    new() { AlarmId = overrideId + ":legacy:ABCDEF12" },
                    new() { AlarmId = groupKey },
                    new()
                    {
                        AlarmId =
                            "vanilla:NotEnoughPowerForEntityExtra:entity:17",
                    },
                    new()
                    {
                        AlarmId = "vanilla:NotEnoughPower:entity:18",
                    },
                },
            },
            new()
            {
                Id = "entity-group-migration",
                Name = "ENTITY",
                OwnerEntityId = 42,
                OwnerEntityTitle = "Arc furnace II",
                OwnerEntityPrototypeId = "AirSeparatorT2",
                IncludeVanilla = false,
                IncludeSystem = false,
                Slots = new List<PanelSlotDefinition>
                {
                    new() { AlarmId = overrideId },
                    new() { AlarmId = overrideId + ":entity:42" },
                    new() { AlarmId = overrideId + ":legacy:12345678" },
                    new() { AlarmId = groupKey },
                    new()
                    {
                        AlarmId = "vanilla:NotEnoughPower:entity:42",
                    },
                },
            },
        };

        slotConfiguration.Normalize();

        var normalizedGlobalSlots = slotConfiguration.Panels.Single(panel =>
            panel.Id == "global-group-migration").Slots;
        AreEqual(
            1,
            normalizedGlobalSlots.Count(slot =>
                slot.AlarmId == overrideId));
        var normalizedGroupedSlot = normalizedGlobalSlots.Single(slot =>
            slot.AlarmId == overrideId);
        AreEqual(prototypeId, normalizedGroupedSlot.DisplayName);
        AreEqual(prototypeId, normalizedGroupedSlot.Detail);
        AreEqual("vanilla", normalizedGroupedSlot.Source);
        IsFalse(normalizedGlobalSlots.Any(slot =>
            slot.AlarmId == groupKey ||
            slot.AlarmId.StartsWith(
                overrideId + ":entity:",
                StringComparison.Ordinal) ||
            slot.AlarmId.StartsWith(
                overrideId + ":legacy:",
                StringComparison.Ordinal)));
        IsTrue(normalizedGlobalSlots.Any(slot =>
            slot.AlarmId ==
            "vanilla:NotEnoughPowerForEntityExtra:entity:17"));
        IsTrue(normalizedGlobalSlots.Any(slot =>
            slot.AlarmId == "vanilla:NotEnoughPower:entity:18"));

        var normalizedEntitySlots = slotConfiguration.Panels.Single(panel =>
            panel.Id == "entity-group-migration").Slots;
        IsFalse(normalizedEntitySlots.Any(slot =>
            GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                slot.AlarmId)));
        IsTrue(normalizedEntitySlots.Any(slot =>
            slot.AlarmId == "vanilla:NotEnoughPower:entity:42"));

        // Repeated normalization must neither recreate legacy group slots nor
        // remove a similarly named, non-target notification slot.
        slotConfiguration.Normalize();
        normalizedGlobalSlots = slotConfiguration.Panels.Single(panel =>
            panel.Id == "global-group-migration").Slots;
        normalizedEntitySlots = slotConfiguration.Panels.Single(panel =>
            panel.Id == "entity-group-migration").Slots;
        AreEqual(
            1,
            normalizedGlobalSlots.Count(slot =>
                slot.AlarmId == overrideId));
        IsFalse(normalizedEntitySlots.Any(slot =>
            GroupedVanillaNotificationPolicy.IsGroupedSlotId(
                slot.AlarmId)));
        IsTrue(normalizedGlobalSlots.Any(slot =>
            slot.AlarmId ==
            "vanilla:NotEnoughPowerForEntityExtra:entity:17"));

        var consolidated = CreateGroupedNormalizationConfiguration(
            GroupedNormalizationMemory(
                "vanilla:notification-10",
                sequence: 10,
                acknowledged: true,
                operatorSilenced: true,
                operatorSilencedAtGameTick: 100,
                entityId: 10),
            GroupedNormalizationMemory(
                "vanilla:notification-20",
                sequence: 20,
                acknowledged: true,
                operatorSilenced: true,
                operatorSilencedAtGameTick: 300,
                entityId: 20),
            GroupedNormalizationMemory(
                "vanilla:notification-30",
                sequence: 30,
                acknowledged: true,
                operatorSilenced: true,
                operatorSilencedAtGameTick: 200,
                entityId: 30),
            new AlarmMemoryDefinition
            {
                Key = "vanilla:unrelated-40",
                Source = "vanilla",
                OverrideId = "vanilla:NotEnoughWorkers",
                OccurrenceId = "vanilla:NotEnoughWorkers",
                SlotId = "vanilla:NotEnoughWorkers:entity:40",
                Sequence = 40,
                IsActive = true,
                EntityId = 40,
            });

        consolidated.Normalize();

        AreEqual(2, consolidated.AlarmMemories.Count);
        var grouped = consolidated.AlarmMemories.Single(memory =>
            memory.OverrideId == overrideId);
        AreEqual(groupKey, grouped.Key);
        AreEqual(overrideId, grouped.SlotId);
        AreEqual(overrideId, grouped.OccurrenceId);
        AreEqual(10L, grouped.Sequence);
        IsTrue(grouped.IsActive);
        IsTrue(grouped.IsAcknowledged);
        IsFalse(grouped.IsGoneUnacknowledged);
        IsTrue(grouped.IsOperatorSilenced);
        AreEqual(300L, grouped.OperatorSilencedAtGameTick);
        AreEqual(3d, grouped.LastValue);
        IsTrue(consolidated.AlarmMemories.Any(memory =>
            memory.Key == "vanilla:unrelated-40" &&
            memory.OverrideId == "vanilla:NotEnoughWorkers"));

        foreach (var supersededSequence in new long[] { 20, 30 })
        {
            IsTrue(consolidated.AlarmHistory.Single(history =>
                history.Sequence == supersededSequence).IsGone);
        }
        var groupedHistory = consolidated.AlarmHistory.Single(history =>
            history.Sequence == 10);
        AreEqual(groupKey, groupedHistory.AlarmKey);
        IsFalse(groupedHistory.IsGone);
        IsTrue(groupedHistory.IsAcknowledged);
        IsFalse(consolidated.AlarmHistory.Single(history =>
            history.Sequence == 40).IsGone);

        var firstGroupedMemoryCount = consolidated.AlarmMemories.Count;
        var firstHistoryCount = consolidated.AlarmHistory.Count;
        consolidated.Normalize();
        grouped = consolidated.AlarmMemories.Single(memory =>
            memory.OverrideId == overrideId);
        AreEqual(firstGroupedMemoryCount, consolidated.AlarmMemories.Count);
        AreEqual(firstHistoryCount, consolidated.AlarmHistory.Count);
        AreEqual(groupKey, grouped.Key);
        AreEqual(overrideId, grouped.SlotId);
        AreEqual(overrideId, grouped.OccurrenceId);
        AreEqual(10L, grouped.Sequence);
        IsTrue(grouped.IsAcknowledged);
        IsTrue(grouped.IsOperatorSilenced);
        AreEqual(300L, grouped.OperatorSilencedAtGameTick);
        AreEqual(3d, grouped.LastValue);

        // A partially migrated save can contain one canonical aggregate plus
        // a later legacy member. Preserve the aggregate's represented count.
        var canonicalMemory = GroupedNormalizationMemory(
            groupKey,
            sequence: 31,
            acknowledged: true,
            entityId: 31);
        canonicalMemory.SlotId = overrideId;
        canonicalMemory.LastValue = 3d;
        var mixedMigration = CreateGroupedNormalizationConfiguration(
            canonicalMemory,
            GroupedNormalizationMemory(
                "vanilla:notification-32",
                sequence: 32,
                acknowledged: true,
                entityId: 32));
        mixedMigration.Normalize();
        AreEqual(1, mixedMigration.AlarmMemories.Count);
        AreEqual(4d, mixedMigration.AlarmMemories[0].LastValue);
        mixedMigration.Normalize();
        AreEqual(4d, mixedMigration.AlarmMemories[0].LastValue);

        // A gone legacy occurrence is historical and must not reset the
        // acknowledgement or operator silence of the still-active group.
        var unresolved = CreateGroupedNormalizationConfiguration(
            GroupedNormalizationMemory(
                "vanilla:notification-41",
                sequence: 41,
                acknowledged: true,
                operatorSilenced: true,
                operatorSilencedAtGameTick: 410,
                entityId: 41),
            GroupedNormalizationMemory(
                "vanilla:notification-42",
                sequence: 42,
                acknowledged: true,
                operatorSilenced: true,
                operatorSilencedAtGameTick: 420,
                entityId: 42),
            GroupedNormalizationMemory(
                "vanilla:notification-43",
                sequence: 43,
                active: false,
                goneUnacknowledged: true,
                entityId: 43));
        unresolved.Normalize();
        var unresolvedGroup = unresolved.AlarmMemories.Single();
        AreEqual(41L, unresolvedGroup.Sequence);
        IsTrue(unresolvedGroup.IsActive);
        IsTrue(unresolvedGroup.IsAcknowledged);
        IsTrue(unresolvedGroup.IsOperatorSilenced);
        AreEqual(420L, unresolvedGroup.OperatorSilencedAtGameTick);
        AreEqual(2d, unresolvedGroup.LastValue);
        IsTrue(unresolved.AlarmHistory.Single(history =>
            history.Sequence == 41).IsAcknowledged);
        IsTrue(unresolved.AlarmHistory.Single(history =>
            history.Sequence == 42).IsGone);
        IsTrue(unresolved.AlarmHistory.Single(history =>
            history.Sequence == 43).IsGone);

        // Acknowledgement can be group-wide while operator silence is not.
        var partiallyOperatorSilenced =
            CreateGroupedNormalizationConfiguration(
                GroupedNormalizationMemory(
                    "vanilla:notification-51",
                    sequence: 51,
                    acknowledged: true,
                    operatorSilenced: true,
                    operatorSilencedAtGameTick: 510,
                    entityId: 51),
                GroupedNormalizationMemory(
                    "vanilla:notification-52",
                    sequence: 52,
                    acknowledged: true,
                    operatorSilenced: false,
                    entityId: 52));
        partiallyOperatorSilenced.Normalize();
        var partiallySilencedGroup =
            partiallyOperatorSilenced.AlarmMemories.Single();
        IsTrue(partiallySilencedGroup.IsAcknowledged);
        IsFalse(partiallySilencedGroup.IsOperatorSilenced);
        AreEqual(-1L, partiallySilencedGroup.OperatorSilencedAtGameTick);

        // Group behavior is type-scoped during persistence cleanup. A global
        // Ignored rule therefore wins over a legacy entity-level Normal
        // exception, including history without a surviving memory.
        var globallyIgnored = CreateGroupedNormalizationConfiguration(
            GroupedNormalizationMemory(
                "vanilla:notification-61",
                sequence: 61,
                entityId: 61),
            new AlarmMemoryDefinition
            {
                Key = "vanilla:unrelated-62",
                Source = "vanilla",
                OverrideId = "vanilla:NotEnoughWorkers",
                OccurrenceId = "vanilla:NotEnoughWorkers",
                SlotId = "vanilla:NotEnoughWorkers:entity:62",
                Sequence = 62,
                IsActive = true,
                EntityId = 62,
            });
        globallyIgnored.VanillaNotificationRules.AddRange(new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.Entity,
                EntityId = 61,
                Behavior = VanillaNotificationBehavior.Normal,
            },
        });
        globallyIgnored.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            Sequence = 63,
            AlarmKey = "vanilla:old-group-history",
            Source = "vanilla",
            Detail = prototypeId + " · Old occurrence",
        });
        AreEqual(
            VanillaNotificationBehavior.Normal,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                globallyIgnored.VanillaNotificationRules,
                overrideId,
                entityId: 61));
        globallyIgnored.Normalize();
        IsFalse(globallyIgnored.AlarmMemories.Any(memory =>
            memory.OverrideId == overrideId));
        IsFalse(globallyIgnored.AlarmHistory.Any(history =>
            history.Detail.StartsWith(
                prototypeId,
                StringComparison.Ordinal)));
        IsTrue(globallyIgnored.AlarmMemories.Any(memory =>
            memory.OverrideId == "vanilla:NotEnoughWorkers"));
        IsTrue(globallyIgnored.AlarmHistory.Any(history =>
            history.Sequence == 62));

        // Conversely, a global Normal group stays persisted even if the old
        // representative entity had a more-specific Ignored exception.
        var globallyNormal = CreateGroupedNormalizationConfiguration(
            GroupedNormalizationMemory(
                "vanilla:notification-71",
                sequence: 71,
                entityId: 71,
                entityPrototypeId: "AirSeparatorT2"));
        globallyNormal.VanillaNotificationRules.AddRange(new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new VanillaNotificationRule
            {
                AlarmId = overrideId,
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "AirSeparatorT2",
                Behavior = VanillaNotificationBehavior.Ignored,
            },
        });
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                globallyNormal.VanillaNotificationRules,
                overrideId,
                entityId: 71,
                entityPrototypeId: "AirSeparatorT2"));
        globallyNormal.Normalize();
        AreEqual(1, globallyNormal.AlarmMemories.Count);
        AreEqual(groupKey, globallyNormal.AlarmMemories[0].Key);
        AreEqual(overrideId, globallyNormal.AlarmMemories[0].SlotId);
        AreEqual(1, globallyNormal.AlarmHistory.Count);
        AreEqual(groupKey, globallyNormal.AlarmHistory[0].AlarmKey);
    }

    private static UnmaConfiguration
        CreateGroupedNormalizationConfiguration(
            params AlarmMemoryDefinition[] memories)
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.AlarmMemories = (memories ??
                Array.Empty<AlarmMemoryDefinition>())
            .Where(memory => memory != null)
            .ToList();
        configuration.AlarmHistory = configuration.AlarmMemories
            .Select(memory => new AlarmHistoryDefinition
            {
                Sequence = memory.Sequence,
                AlarmKey = memory.Key,
                Message = memory.Name,
                Detail = memory.OverrideId ==
                         GroupedVanillaNotificationPolicy.OverrideId
                    ? GroupedVanillaNotificationPolicy.PrototypeId +
                      " · Entity " + memory.EntityId
                    : memory.OverrideId.StartsWith(
                        "vanilla:",
                        StringComparison.Ordinal)
                        ? memory.OverrideId.Substring("vanilla:".Length) +
                          " · Entity " + memory.EntityId
                        : memory.Detail,
                Source = memory.Source,
                Severity = memory.Severity,
                IsGone = !memory.IsActive,
                IsAcknowledged = memory.IsAcknowledged,
            })
            .ToList();
        configuration.VanillaNotificationRules.Clear();
        return configuration;
    }

    private static AlarmMemoryDefinition GroupedNormalizationMemory(
        string key,
        long sequence,
        bool active = true,
        bool acknowledged = false,
        bool goneUnacknowledged = false,
        bool operatorSilenced = false,
        long operatorSilencedAtGameTick = -1,
        int entityId = -1,
        string entityPrototypeId = "")
    {
        return new AlarmMemoryDefinition
        {
            Key = key,
            Name = "NOT ENOUGH POWER",
            Detail = GroupedVanillaNotificationPolicy.PrototypeId +
                     " · Entity " + entityId,
            Source = "vanilla",
            OverrideId = GroupedVanillaNotificationPolicy.OverrideId,
            OccurrenceId = GroupedVanillaNotificationPolicy.OverrideId,
            SlotId = GroupedVanillaNotificationPolicy.OverrideId +
                     ":entity:" + entityId,
            Severity = AlarmSeverity.Critical,
            IsActive = active,
            IsAcknowledged = acknowledged,
            IsGoneUnacknowledged = goneUnacknowledged,
            IsOperatorSilenced = operatorSilenced,
            OperatorSilencedAtGameTick = operatorSilencedAtGameTick,
            Sequence = sequence,
            EntityId = entityId,
            EntityPrototypeId = entityPrototypeId,
            EntityTitle = "Entity " + entityId,
        };
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

        var truckHistory = new AlarmHistoryDefinition
        {
            Source = "vanilla",
            Detail = "TruckCannotDeliver · Truck 42",
        };
        IsTrue(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                truckHistory,
                "vanilla:TruckCannotDeliver"));
        truckHistory.Detail = "TruckCannotDeliver";
        IsTrue(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                truckHistory,
                "vanilla:TruckCannotDeliver"));
        truckHistory.Detail = "TruckCannotDeliverMixedCargo · Truck 42";
        IsFalse(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                truckHistory,
                "vanilla:TruckCannotDeliver"));
        IsTrue(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                truckHistory,
                "vanilla:TruckCannotDeliverMixedCargo"));
        truckHistory.Source = "external";
        IsFalse(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                truckHistory,
                "vanilla:TruckCannotDeliverMixedCargo"));
        IsFalse(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(null, overrideId));
        IsFalse(VanillaNotificationSuppressionPolicy
            .MatchesHistoryForOverride(
                new AlarmHistoryDefinition
                {
                    Source = "vanilla",
                    Detail = "NoRecipeSelected · Machine",
                },
                "system:NoRecipeSelected"));

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
        AreEqual(20, legacyConfig.SchemaVersion);
        IsFalse(legacyConfig.SoundOverrides.Last().IsGloballyDisabled);
        AreEqual(
            VanillaNotificationBehavior.Hidden,
            VanillaNotificationSuppressionPolicy.ResolveBehavior(
                legacyConfig.VanillaNotificationRules,
                overrideId));
    }

    private static void TestIgnoredVanillaPersistenceCleanup()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.VanillaNotificationRules.AddRange(new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliver",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliverMixedCargo",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Ignored,
            },
        });
        configuration.AlarmMemories.AddRange(new[]
        {
            new AlarmMemoryDefinition
            {
                Key = "truck-current",
                Sequence = 10,
                Source = "vanilla",
                OverrideId = "vanilla:TruckCannotDeliver",
                Detail = "TruckCannotDeliver · Truck 10",
                IsActive = true,
            },
            new AlarmMemoryDefinition
            {
                Key = "mixed-current",
                Sequence = 11,
                Source = "vanilla",
                OverrideId = "vanilla:TruckCannotDeliverMixedCargo",
                Detail = "TruckCannotDeliverMixedCargo · Truck 11",
                IsActive = true,
            },
            new AlarmMemoryDefinition
            {
                Key = "fuel-current",
                Sequence = 12,
                Source = "vanilla",
                OverrideId = "vanilla:VehicleNoFuel",
                Detail = "VehicleNoFuel · Truck 12",
                IsActive = true,
            },
        });
        configuration.AlarmHistory.AddRange(new[]
        {
            new AlarmHistoryDefinition
            {
                Sequence = 10,
                AlarmKey = "truck-current",
                Source = "vanilla",
                Detail = "TruckCannotDeliver · Truck 10",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 20,
                AlarmKey = "truck-old-pruned-state",
                Source = "vanilla",
                Detail = "TruckCannotDeliver · Truck 20",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 21,
                AlarmKey = "mixed-old-pruned-state",
                Source = "vanilla",
                Detail = "TruckCannotDeliverMixedCargo · Truck 21",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 12,
                AlarmKey = "fuel-current",
                Source = "vanilla",
                Detail = "VehicleNoFuel · Truck 12",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 22,
                AlarmKey = "external-same-detail",
                Source = "external",
                Detail = "TruckCannotDeliver · External",
            },
        });

        configuration.Normalize();

        AreEqual(1, configuration.AlarmMemories.Count);
        AreEqual("fuel-current", configuration.AlarmMemories[0].Key);
        AreEqual(2, configuration.AlarmHistory.Count);
        IsTrue(configuration.AlarmHistory.Any(history =>
            history.AlarmKey == "fuel-current"));
        IsTrue(configuration.AlarmHistory.Any(history =>
            history.AlarmKey == "external-same-detail"));

        var exceptionConfiguration = UnmaConfiguration.CreateDefault();
        exceptionConfiguration.VanillaNotificationRules.AddRange(new[]
        {
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliver",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new VanillaNotificationRule
            {
                AlarmId = "vanilla:TruckCannotDeliver",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Normal,
            },
        });
        exceptionConfiguration.AlarmMemories.AddRange(new[]
        {
            new AlarmMemoryDefinition
            {
                Key = "allowed-truck",
                Sequence = 30,
                Source = "vanilla",
                OverrideId = "vanilla:TruckCannotDeliver",
                EntityPrototypeId = "TruckT2",
                IsActive = true,
            },
            new AlarmMemoryDefinition
            {
                Key = "ignored-truck",
                Sequence = 31,
                Source = "vanilla",
                OverrideId = "vanilla:TruckCannotDeliver",
                EntityPrototypeId = "TruckT3",
                IsActive = true,
            },
        });
        exceptionConfiguration.AlarmHistory.AddRange(new[]
        {
            new AlarmHistoryDefinition
            {
                Sequence = 30,
                AlarmKey = "allowed-truck",
                Source = "vanilla",
                Detail = "TruckCannotDeliver · Allowed T2",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 31,
                AlarmKey = "ignored-truck",
                Source = "vanilla",
                Detail = "TruckCannotDeliver · Ignored T3",
            },
            new AlarmHistoryDefinition
            {
                Sequence = 32,
                AlarmKey = "unattributed-old-truck",
                Source = "vanilla",
                Detail = "TruckCannotDeliver · Unknown",
            },
        });

        exceptionConfiguration.Normalize();

        AreEqual(1, exceptionConfiguration.AlarmMemories.Count);
        AreEqual(
            "allowed-truck",
            exceptionConfiguration.AlarmMemories[0].Key);
        AreEqual(2, exceptionConfiguration.AlarmHistory.Count);
        IsTrue(exceptionConfiguration.AlarmHistory.Any(history =>
            history.AlarmKey == "allowed-truck"));
        IsTrue(exceptionConfiguration.AlarmHistory.Any(history =>
            history.AlarmKey == "unattributed-old-truck"));
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
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 123,
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
        IsTrue(dashboard[2].IsOperatorSilenced);
        AreEqual(123L, dashboard[2].OperatorSilencedAtGameTick);

        var allOperatorSilenced = new[]
        {
            new AlarmView
            {
                Key = "operator-silenced:one",
                SlotId = "dashboard:operator-silenced",
                Name = "OPERATOR SILENCED",
                IsActive = true,
                IsAcknowledged = true,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 100,
                Sequence = 60,
            },
            new AlarmView
            {
                Key = "operator-silenced:two",
                SlotId = "dashboard:operator-silenced",
                Name = "OPERATOR SILENCED",
                IsActive = true,
                IsAcknowledged = true,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 200,
                Sequence = 61,
            },
        };
        var operatorSilencedProjection =
            PanelSlotProjection.ProjectActive(allOperatorSilenced).Single();
        IsTrue(operatorSilencedProjection.IsAcknowledged);
        IsTrue(operatorSilencedProjection.IsOperatorSilenced);
        AreEqual(
            200L,
            operatorSilencedProjection.OperatorSilencedAtGameTick);

        allOperatorSilenced[0].IsOperatorSilenced = false;
        allOperatorSilenced[0].OperatorSilencedAtGameTick = -1;
        var mixedSilenceProjection =
            PanelSlotProjection.ProjectActive(allOperatorSilenced).Single();
        IsTrue(mixedSilenceProjection.IsAcknowledged);
        IsFalse(mixedSilenceProjection.IsOperatorSilenced);
        AreEqual(
            -1L,
            mixedSilenceProjection.OperatorSilencedAtGameTick);

        allOperatorSilenced[0].IsAcknowledged = false;
        var unacknowledgedSilenceProjection =
            PanelSlotProjection.ProjectActive(allOperatorSilenced).Single();
        IsFalse(unacknowledgedSilenceProjection.IsAcknowledged);
        IsFalse(unacknowledgedSilenceProjection.IsOperatorSilenced);
        AreEqual(
            -1L,
            unacknowledgedSilenceProjection.OperatorSilencedAtGameTick);

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

        var entityCard17 = new AlarmView
        {
            Source = "vanilla",
            OverrideId = "vanilla:NotEnoughWorkers",
            SlotId = "vanilla:NotEnoughWorkers:entity:17",
            EntityId = 17,
            EntityPrototypeId = "AirStorageT3",
        };
        var entityCard18 = new AlarmView
        {
            Source = "vanilla",
            OverrideId = "vanilla:NotEnoughWorkers",
            SlotId = "vanilla:NotEnoughWorkers:entity:18",
            EntityId = 18,
            EntityPrototypeId = "AirStorageT3",
        };
        var entityCard17Clone = new AlarmView
        {
            Source = " vanilla ",
            OverrideId = " vanilla:NotEnoughWorkers ",
            SlotId = " vanilla:NotEnoughWorkers:entity:17 ",
            EntityId = 17,
            EntityPrototypeId = " AirStorageT3 ",
        };
        var entityCard17Identity =
            PanelSlotProjection.StableViewIdentity(entityCard17);
        IsFalse(string.Equals(
            entityCard17Identity,
            PanelSlotProjection.StableViewIdentity(entityCard18),
            StringComparison.Ordinal));
        AreEqual(
            entityCard17Identity,
            PanelSlotProjection.StableViewIdentity(entityCard17Clone));

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
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 99,
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
        IsFalse(mixed.IsOperatorSilenced);
        AreEqual(-1L, mixed.OperatorSilencedAtGameTick);
        AreEqual("AKTUELL STEHEND", mixed.Name);

        mixedCandidates[0].IsGoneUnacknowledged = false;
        mixed = PanelSlotProjection.Project(mixedSlot, mixedCandidates)[0];
        IsTrue(mixed.IsActive);
        IsTrue(mixed.IsAcknowledged);
        IsFalse(mixed.IsOperatorSilenced);
        AreEqual(-1L, mixed.OperatorSilencedAtGameTick);

        mixedCandidates[0].IsActive = true;
        mixedCandidates[0].Sequence = 102;
        mixedCandidates[0].Name = "NEUESTES KOMMT";
        mixed = PanelSlotProjection.Project(mixedSlot, mixedCandidates)[0];
        IsTrue(mixed.IsActive);
        IsFalse(mixed.IsAcknowledged);
        IsFalse(mixed.IsOperatorSilenced);
        AreEqual(-1L, mixed.OperatorSilencedAtGameTick);
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
        configuration.ReducedMotion = true;
        var fixedPanel = configuration.Panels.Find(panel =>
            !panel.IsDashboard);
        configuration.DetachedPanelLayouts.Add(
            new DetachedPanelWindowLayout
            {
                PanelId = fixedPanel.Id,
                X = 234f,
                Y = 123f,
                Width = 780f,
                Height = 560f,
                IsOpen = true,
            });
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
        AreEqual(20, restored.SchemaVersion);
        IsTrue(restored.ReducedMotion);
        AreEqual(1, restored.DetachedPanelLayouts.Count);
        AreEqual(fixedPanel.Id, restored.DetachedPanelLayouts[0].PanelId);
        AreEqual(234f, restored.DetachedPanelLayouts[0].X);
        AreEqual(780f, restored.DetachedPanelLayouts[0].Width);
        IsTrue(restored.DetachedPanelLayouts[0].IsOpen);
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

    private static void TestAlarmMemoryOperatorSilenceRoundTrip()
    {
        var configuration = UnmaConfiguration.CreateDefault();
        configuration.AlarmMemories.Clear();
        configuration.AlarmMemories.AddRange(new[]
        {
            new AlarmMemoryDefinition
            {
                Key = "operator-silenced-valid",
                Source = "system",
                IsActive = true,
                IsAcknowledged = true,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 345,
                Sequence = 1,
            },
            new AlarmMemoryDefinition
            {
                Key = "operator-silenced-unacknowledged",
                Source = "system",
                IsActive = true,
                IsAcknowledged = false,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 111,
                Sequence = 2,
            },
            new AlarmMemoryDefinition
            {
                Key = "operator-silenced-gone",
                Source = "system",
                IsAcknowledged = true,
                IsGoneUnacknowledged = true,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = 222,
                Sequence = 3,
            },
            new AlarmMemoryDefinition
            {
                Key = "operator-silenced-invalid-tick",
                Source = "system",
                IsActive = true,
                IsAcknowledged = true,
                IsOperatorSilenced = true,
                OperatorSilencedAtGameTick = -1,
                Sequence = 4,
            },
            new AlarmMemoryDefinition
            {
                Key = "operator-silenced-stale-tick",
                Source = "system",
                IsActive = true,
                IsAcknowledged = true,
                IsOperatorSilenced = false,
                OperatorSilencedAtGameTick = 444,
                Sequence = 5,
            },
        });

        var json = SerializeDataContractJson(configuration);
        IsTrue(json.Contains(
            "\"IsOperatorSilenced\":true",
            StringComparison.Ordinal));
        IsTrue(json.Contains(
            "\"OperatorSilencedAtGameTick\":345",
            StringComparison.Ordinal));

        UnmaConfiguration restored;
        using (var stream = new MemoryStream(
                   System.Text.Encoding.UTF8.GetBytes(json)))
        {
            restored = (UnmaConfiguration)new DataContractJsonSerializer(
                typeof(UnmaConfiguration)).ReadObject(stream);
        }
        restored.Normalize();

        var valid = restored.AlarmMemories.Single(memory =>
            memory.Key == "operator-silenced-valid");
        IsTrue(valid.IsActive);
        IsTrue(valid.IsAcknowledged);
        IsTrue(valid.IsOperatorSilenced);
        AreEqual(345L, valid.OperatorSilencedAtGameTick);

        foreach (var invalidKey in new[]
                 {
                     "operator-silenced-unacknowledged",
                     "operator-silenced-gone",
                     "operator-silenced-invalid-tick",
                     "operator-silenced-stale-tick",
                 })
        {
            var invalid = restored.AlarmMemories.Single(memory =>
                memory.Key == invalidKey);
            IsFalse(invalid.IsOperatorSilenced);
            AreEqual(-1L, invalid.OperatorSilencedAtGameTick);
        }

        const string legacyJson =
            "{\"SchemaVersion\":20,\"AlarmMemories\":[{" +
            "\"Key\":\"legacy-active-acknowledged\"," +
            "\"Source\":\"system\",\"IsActive\":true," +
            "\"IsAcknowledged\":true,\"Sequence\":9}]}";
        UnmaConfiguration legacy;
        using (var stream = new MemoryStream(
                   System.Text.Encoding.UTF8.GetBytes(legacyJson)))
        {
            legacy = (UnmaConfiguration)new DataContractJsonSerializer(
                typeof(UnmaConfiguration)).ReadObject(stream);
        }
        legacy.Normalize();
        var legacyMemory = legacy.AlarmMemories.Single();
        IsTrue(legacyMemory.IsActive);
        IsTrue(legacyMemory.IsAcknowledged);
        IsFalse(legacyMemory.IsOperatorSilenced);
        AreEqual(-1L, legacyMemory.OperatorSilencedAtGameTick);
        AreEqual(
            UnmaConfiguration.CurrentSchemaVersion,
            legacy.SchemaVersion);
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

        AreEqual(20, restored.SchemaVersion);
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

        AreEqual(20, oldConfiguration.SchemaVersion);
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
        AreEqual(20, schemaSeven.SchemaVersion);
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
        AreEqual(20, legacy.SchemaVersion);
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

        AreEqual(20, versionFive.SchemaVersion);
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

        AreEqual(20, schemaEight.SchemaVersion);
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

    private static void TestReducedMotionConfigurationContract()
    {
        var defaults = UnmaConfiguration.CreateDefault();
        IsFalse(defaults.ReducedMotion);
        AreEqual(20, UnmaConfiguration.CurrentSchemaVersion);

        defaults.ReducedMotion = true;
        var json = SerializeDataContractJson(defaults);
        IsTrue(json.Contains(
            "\"ReducedMotion\":true",
            StringComparison.Ordinal));

        UnmaConfiguration restored;
        using (var stream = new MemoryStream(
                   System.Text.Encoding.UTF8.GetBytes(json)))
        {
            restored = (UnmaConfiguration)new DataContractJsonSerializer(
                typeof(UnmaConfiguration)).ReadObject(stream);
        }
        restored.Normalize();
        IsTrue(restored.ReducedMotion);
        AreEqual(20, restored.SchemaVersion);

        const string legacyJson = "{\"SchemaVersion\":20}";
        UnmaConfiguration legacy;
        using (var stream = new MemoryStream(
                   System.Text.Encoding.UTF8.GetBytes(legacyJson)))
        {
            legacy = (UnmaConfiguration)new DataContractJsonSerializer(
                typeof(UnmaConfiguration)).ReadObject(stream);
        }
        legacy.Normalize();
        IsFalse(legacy.ReducedMotion);
        AreEqual(20, legacy.SchemaVersion);

        var repositoryRoot = FindRepositoryRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "source",
            "Runtime",
            "UnmaRuntime.cs"));
        IsTrue(runtimeSource.Contains(
            "target.ReducedMotion = snapshot.ReducedMotion;",
            StringComparison.Ordinal));
    }

    private static void TestRecommendedQuietTransferProfile()
    {
        IsTrue(ConfigurationTransferPolicy
            .ShouldInitializeRecommendedProfile(null, "", false));
        IsTrue(ConfigurationTransferPolicy
            .ShouldInitializeRecommendedProfile(null, "  ", false));
        IsFalse(ConfigurationTransferPolicy
            .ShouldInitializeRecommendedProfile(
                new UnmaTransferProfile(),
                "",
                false));
        IsFalse(ConfigurationTransferPolicy
            .ShouldInitializeRecommendedProfile(null, "corrupt", false));
        IsFalse(ConfigurationTransferPolicy
            .ShouldInitializeRecommendedProfile(null, "", true));

        var expectedBehaviors = new Dictionary<
            string,
            VanillaNotificationBehavior>(StringComparer.Ordinal)
        {
            ["vanilla:UpgradeInProgress"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:DowngradeInProgress"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:VehicleGoalStruggling"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:VehicleNoReachableDesignations"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:NoTreesToHarvest"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:ExcavatorHasNoValidTruck"] =
                VanillaNotificationBehavior.Silent,
            ["vanilla:TruckCannotDeliver"] =
                VanillaNotificationBehavior.Ignored,
            ["vanilla:TruckCannotDeliverMixedCargo"] =
                VanillaNotificationBehavior.Ignored,
            ["vanilla:NotEnoughFuelToRefuel"] =
                VanillaNotificationBehavior.Ignored,
        };
        var profile = ConfigurationTransferPolicy
            .CreateRecommendedQuietProfile("0.10.3");

        AreEqual(
            UnmaTransferProfile.CurrentProfileSchemaVersion,
            profile.ProfileSchemaVersion);
        AreEqual("UNMA Recommended Quiet", profile.Metadata.Name);
        AreEqual("0.10.3", profile.Metadata.SourceVersion);
        IsTrue(DateTime.TryParse(
            profile.Metadata.CreatedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out _));
        AreEqual(0, profile.Metadata.SkippedItems);
        AreEqual(0, profile.Metadata.Diagnostics.Count);
        IsTrue(profile.Selection.NotificationBehaviors);
        IsFalse(profile.Selection.SoundSettings);
        IsFalse(profile.Selection.Appearance);
        IsFalse(profile.Selection.SystemAlarms);
        IsFalse(profile.Selection.WindowLayout);
        AreEqual(9, profile.NotificationRules.Count);
        IsTrue(expectedBehaviors.Keys.ToHashSet(StringComparer.Ordinal)
            .SetEquals(profile.NotificationRules.Select(rule => rule.AlarmId)));
        IsTrue(profile.NotificationRules.All(rule =>
            rule.Scope == VanillaNotificationScope.NotificationType &&
            expectedBehaviors.TryGetValue(
                rule.AlarmId,
                out var expectedBehavior) &&
            rule.Behavior == expectedBehavior &&
            string.IsNullOrEmpty(rule.EntityPrototypeId)));
        AreEqual(
            6,
            profile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Silent));
        AreEqual(
            3,
            profile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Ignored));
        AreEqual(
            0,
            profile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Hidden));
        IsTrue(profile.Selection.NotificationRuleIdentities != null);
        AreEqual(
            profile.NotificationRules.Count,
            profile.Selection.NotificationRuleIdentities.Count);
        IsTrue(new HashSet<string>(
                profile.Selection.NotificationRuleIdentities,
                StringComparer.Ordinal)
            .SetEquals(profile.NotificationRules.Select(
                ConfigurationTransferPolicy.RuleIdentity)));
        AreEqual(0, profile.SoundSettings.Count);
        IsTrue(profile.Appearance == null);
        AreEqual(0, profile.SystemAlarms.Count);
        IsTrue(profile.WindowLayout == null);

        IsFalse(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                profile,
                "0.10.3",
                out var unchangedCurrentProfile));
        IsTrue(ReferenceEquals(profile, unchangedCurrentProfile));

        var legacyProfile = ConfigurationTransferPolicy.CloneProfile(profile);
        legacyProfile.Metadata.Name = "UNMA Recommended Silent";
        legacyProfile.Metadata.SourceVersion = "0.10.1";
        legacyProfile.Metadata.CreatedUtc = "legacy-created-utc";
        legacyProfile.NotificationRules.RemoveAll(rule =>
            rule.Behavior == VanillaNotificationBehavior.Ignored);
        legacyProfile.Selection.NotificationRuleIdentities =
            legacyProfile.NotificationRules
                .Select(ConfigurationTransferPolicy.RuleIdentity)
                .ToList();
        IsTrue(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                legacyProfile,
                "0.10.3",
                out var upgradedLegacyProfile));
        IsFalse(ReferenceEquals(legacyProfile, upgradedLegacyProfile));
        AreEqual("UNMA Recommended Silent", legacyProfile.Metadata.Name);
        AreEqual(6, legacyProfile.NotificationRules.Count);
        AreEqual(
            "UNMA Recommended Quiet",
            upgradedLegacyProfile.Metadata.Name);
        AreEqual(
            "legacy-created-utc",
            upgradedLegacyProfile.Metadata.CreatedUtc);
        AreEqual(
            expectedBehaviors.Count,
            upgradedLegacyProfile.NotificationRules.Count);
        AreEqual(
            3,
            upgradedLegacyProfile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Ignored));

        var previousQuietProfile =
            ConfigurationTransferPolicy.CloneProfile(profile);
        previousQuietProfile.Metadata.SourceVersion = "0.10.2";
        previousQuietProfile.Metadata.CreatedUtc = "quiet-created-utc";
        previousQuietProfile.NotificationRules.RemoveAll(rule =>
            rule.AlarmId == "vanilla:NotEnoughFuelToRefuel");
        foreach (var rule in previousQuietProfile.NotificationRules.Where(
                     rule => rule.Behavior ==
                         VanillaNotificationBehavior.Ignored))
        {
            rule.Behavior = VanillaNotificationBehavior.Hidden;
        }
        previousQuietProfile.Selection.NotificationRuleIdentities =
            previousQuietProfile.NotificationRules
                .Select(ConfigurationTransferPolicy.RuleIdentity)
                .ToList();
        IsTrue(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                previousQuietProfile,
                "0.10.3",
                out var refreshedQuietProfile));
        AreEqual(8, previousQuietProfile.NotificationRules.Count);
        AreEqual(
            2,
            previousQuietProfile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Hidden));
        AreEqual(
            3,
            refreshedQuietProfile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Ignored));
        AreEqual(
            "quiet-created-utc",
            refreshedQuietProfile.Metadata.CreatedUtc);

        var previous103QuietProfile =
            ConfigurationTransferPolicy.CloneProfile(profile);
        previous103QuietProfile.Metadata.CreatedUtc = "0.10.3-created-utc";
        previous103QuietProfile.NotificationRules.RemoveAll(rule =>
            rule.AlarmId == "vanilla:NotEnoughFuelToRefuel");
        previous103QuietProfile.Selection.NotificationRuleIdentities =
            previous103QuietProfile.NotificationRules
                .Select(ConfigurationTransferPolicy.RuleIdentity)
                .ToList();
        IsTrue(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                previous103QuietProfile,
                "0.10.3",
                out var refreshed103QuietProfile));
        AreEqual(8, previous103QuietProfile.NotificationRules.Count);
        AreEqual(
            2,
            previous103QuietProfile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Ignored));
        AreEqual(
            expectedBehaviors.Count,
            refreshed103QuietProfile.NotificationRules.Count);
        AreEqual(
            3,
            refreshed103QuietProfile.NotificationRules.Count(rule =>
                rule.Behavior == VanillaNotificationBehavior.Ignored));
        AreEqual(
            "0.10.3-created-utc",
            refreshed103QuietProfile.Metadata.CreatedUtc);

        var customProfile = ConfigurationTransferPolicy.CloneProfile(
            previous103QuietProfile);
        customProfile.Metadata.Name = "My quiet profile";
        IsFalse(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                customProfile,
                "0.10.3",
                out var unchangedCustomProfile));
        IsTrue(ReferenceEquals(customProfile, unchangedCustomProfile));

        var divergentQuietProfile =
            ConfigurationTransferPolicy.CloneProfile(previous103QuietProfile);
        divergentQuietProfile.NotificationRules.Single(rule =>
            rule.AlarmId == "vanilla:TruckCannotDeliver").Behavior =
                VanillaNotificationBehavior.Silent;
        IsFalse(ConfigurationTransferPolicy
            .TryRefreshPreviousRecommendedProfile(
                divergentQuietProfile,
                "0.10.3",
                out var unchangedDivergentProfile));
        IsTrue(ReferenceEquals(
            divergentQuietProfile,
            unchangedDivergentProfile));

        var criticalAlarmIds = new[]
        {
            "vanilla:VehicleGoalUnreachable",
            "vanilla:VehicleNoFuel",
            "vanilla:NotEnoughPower",
            "vanilla:NotEnoughPowerForEntity",
            "vanilla:NotEnoughWorkers",
            "vanilla:LowFoodSupply",
            "vanilla:MachineIsBroken",
            "vanilla:TrainCannotFindPath",
            "vanilla:NuclearReactorInMeltdown",
            "vanilla:CannotDeliverFromMineTower",
        };
        IsFalse(profile.NotificationRules.Any(rule =>
            criticalAlarmIds.Contains(rule.AlarmId, StringComparer.Ordinal)));

        var target = UnmaConfiguration.CreateDefault();
        target.VanillaNotificationRules = new List<VanillaNotificationRule>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "LooseMaterialConveyorT3",
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new()
            {
                AlarmId = "vanilla:VehicleNoFuel",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new()
            {
                AlarmId = "vanilla:NotEnoughPower",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new()
            {
                AlarmId = "vanilla:NotEnoughPowerForEntity",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
        };
        target.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            AlarmKey = "preserved-history",
        });

        var result = ConfigurationTransferPolicy.Merge(target, profile);
        AreEqual(8, result.Preview.Added);
        AreEqual(1, result.Preview.Changed);
        AreEqual(0, result.Preview.Skipped);
        AreEqual(
            VanillaNotificationBehavior.Silent,
            result.Configuration.VanillaNotificationRules.Single(rule =>
                rule.AlarmId == "vanilla:UpgradeInProgress" &&
                rule.Scope == VanillaNotificationScope.NotificationType)
                .Behavior);
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            result.Configuration.VanillaNotificationRules.Single(rule =>
                rule.AlarmId == "vanilla:UpgradeInProgress" &&
                rule.Scope == VanillaNotificationScope.EntityPrototype)
                .Behavior);
        foreach (var protectedAlarmId in new[]
                 {
                     "vanilla:VehicleNoFuel",
                     "vanilla:NotEnoughPower",
                     "vanilla:NotEnoughPowerForEntity",
                 })
        {
            AreEqual(
                VanillaNotificationBehavior.Normal,
                result.Configuration.VanillaNotificationRules.Single(rule =>
                    rule.AlarmId == protectedAlarmId &&
                    rule.Scope ==
                        VanillaNotificationScope.NotificationType)
                    .Behavior);
        }
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            result.Configuration.VanillaNotificationRules.Single(rule =>
                rule.AlarmId == "vanilla:TruckCannotDeliver" &&
                rule.Scope == VanillaNotificationScope.NotificationType)
                .Behavior);
        AreEqual(
            VanillaNotificationBehavior.Ignored,
            result.Configuration.VanillaNotificationRules.Single(rule =>
                rule.AlarmId == "vanilla:NotEnoughFuelToRefuel" &&
                rule.Scope == VanillaNotificationScope.NotificationType)
                .Behavior);
        AreEqual("preserved-history", result.Configuration.AlarmHistory[0].AlarmKey);

        var repeated = ConfigurationTransferPolicy.PreviewImport(
            result.Configuration,
            profile);
        AreEqual(0, repeated.Added);
        AreEqual(0, repeated.Changed);
        AreEqual(expectedBehaviors.Count, repeated.Unchanged);
        AreEqual(0, repeated.Skipped);
    }

    private static void TestTransferProfileRoundTripAndFilter()
    {
        var source = UnmaConfiguration.CreateDefault();
        source.VanillaNotificationRules = new List<VanillaNotificationRule>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "LooseMaterialConveyorT3",
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.Entity,
                EntityId = 42,
                Behavior = VanillaNotificationBehavior.Hidden,
            },
            new()
            {
                AlarmId = "vanilla:CannotFindPath",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Silent,
            },
        };
        source.SoundOverrides = new List<AlarmSoundOverride>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                SoundId = "horn",
                AutoAcknowledgeOnClear = true,
                IsGloballyDisabled = true,
            },
            new()
            {
                AlarmId = "rule:world-specific-guid",
                SoundId = "must-not-transfer",
                AutoAcknowledgeOnClear = true,
            },
        };
        source.WarningColor = "#112233";
        source.CriticalColor = "#445566";
        source.EmergencyColor = "#778899";
        source.UiScalePercent = 175;
        source.ReducedMotion = true;
        source.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            AlarmKey = "must-not-transfer",
        });
        source.AlarmMemories.Add(new AlarmMemoryDefinition
        {
            Key = "must-not-transfer",
            EntityId = 99,
        });
        source.AlarmTimingMemories.Add(new AlarmTimingMemoryDefinition
        {
            OwnerKey = "must-not-transfer",
        });
        source.Instruments.Add(new InstrumentDefinition
        {
            Id = "must-not-transfer",
            MetricPath = "value",
            EntityId = 99,
        });

        var selection = new TransferProfileSelection
        {
            NotificationBehaviors = true,
            SoundSettings = true,
            Appearance = true,
            SystemAlarms = true,
            WindowLayout = false,
            NotificationRuleIdentities = new List<string>
            {
                VanillaNotificationSuppressionPolicy.RuleIdentity(
                    source.VanillaNotificationRules[0]),
                VanillaNotificationSuppressionPolicy.RuleIdentity(
                    source.VanillaNotificationRules[1]),
                VanillaNotificationSuppressionPolicy.RuleIdentity(
                    source.VanillaNotificationRules[2]),
                "vanilla:Missing|0|",
            },
        };
        var profile = ConfigurationTransferPolicy.CreateProfile(
            source,
            selection,
            "  Test profile  ",
            "0.10.2");

        AreEqual(1, profile.ProfileSchemaVersion);
        AreEqual("Test profile", profile.Metadata.Name);
        AreEqual("0.10.2", profile.Metadata.SourceVersion);
        IsTrue(DateTime.TryParse(
            profile.Metadata.CreatedUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out _));
        AreEqual(2, profile.NotificationRules.Count);
        AreEqual(2, profile.Selection.NotificationRuleIdentities.Count);
        AreEqual(
            VanillaNotificationBehavior.Normal,
            profile.NotificationRules[0].Behavior);
        AreEqual(
            VanillaNotificationScope.EntityPrototype,
            profile.NotificationRules[1].Scope);
        AreEqual(
            "LooseMaterialConveyorT3",
            profile.NotificationRules[1].EntityPrototypeId);
        AreEqual(3, profile.Metadata.SkippedItems);
        AreEqual(3, profile.Metadata.Diagnostics.Count);
        AreEqual(1, profile.SoundSettings.Count);
        AreEqual("horn", profile.SoundSettings[0].SoundId);
        IsTrue(profile.SoundSettings[0].AutoAcknowledgeOnClear);
        AreEqual("#112233", profile.Appearance.WarningColor);
        AreEqual(175, profile.Appearance.UiScalePercent);
        IsTrue(profile.Appearance.ReducedMotion);
        IsTrue(profile.WindowLayout == null);
        AreEqual(source.SystemAlarms.Count, profile.SystemAlarms.Count);

        var originalProfileMessage =
            profile.SystemAlarms[0].Stages[0].Message;
        source.SystemAlarms[0].Stages[0].Message = "SOURCE MUTATED";
        AreEqual(
            originalProfileMessage,
            profile.SystemAlarms[0].Stages[0].Message);

        var serializer = new DataContractJsonSerializer(
            typeof(UnmaTransferProfile));
        string json;
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, profile);
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        IsFalse(json.Contains("\"EntityId\"", StringComparison.Ordinal));
        IsFalse(json.Contains("|42", StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"IsGloballyDisabled\"",
            StringComparison.Ordinal));
        IsFalse(json.Contains(
            "must-not-transfer",
            StringComparison.Ordinal));
        IsFalse(json.Contains("\"Panels\"", StringComparison.Ordinal));
        IsFalse(json.Contains("\"Rules\"", StringComparison.Ordinal));
        IsFalse(json.Contains("\"Instruments\"", StringComparison.Ordinal));
        IsFalse(json.Contains("\"AlarmHistory\"", StringComparison.Ordinal));
        IsFalse(json.Contains("\"AlarmMemories\"", StringComparison.Ordinal));
        IsFalse(json.Contains(
            "\"AlarmTimingMemories\"",
            StringComparison.Ordinal));

        UnmaTransferProfile restored;
        using (var stream = new MemoryStream(
                   System.Text.Encoding.UTF8.GetBytes(json)))
        {
            restored = (UnmaTransferProfile)serializer.ReadObject(stream);
        }
        restored.Normalize();
        AreEqual(2, restored.NotificationRules.Count);
        AreEqual(
            VanillaNotificationBehavior.Normal,
            restored.NotificationRules[0].Behavior);
        AreEqual("horn", restored.SoundSettings[0].SoundId);
        AreEqual("#112233", restored.Appearance.WarningColor);
        IsTrue(restored.Appearance.ReducedMotion);
        AreEqual(source.SystemAlarms.Count, restored.SystemAlarms.Count);
    }

    private static void TestConfigurationTransferMerge()
    {
        var source = UnmaConfiguration.CreateDefault();
        source.VanillaNotificationRules = new List<VanillaNotificationRule>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Normal,
            },
            new()
            {
                AlarmId = "vanilla:CannotFindPath",
                Scope = VanillaNotificationScope.EntityPrototype,
                EntityPrototypeId = "TruckT2",
                Behavior = VanillaNotificationBehavior.Ignored,
            },
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.Entity,
                EntityId = 123,
                Behavior = VanillaNotificationBehavior.Hidden,
            },
        };
        source.SoundOverrides = new List<AlarmSoundOverride>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                SoundId = "triangle",
                AutoAcknowledgeOnClear = true,
                IsGloballyDisabled = false,
            },
            new()
            {
                AlarmId = "vanilla:CannotFindPath",
                SoundId = "horn",
                IsGloballyDisabled = true,
            },
        };
        source.WarningColor = "#010203";
        source.CriticalColor = "red";
        source.EmergencyColor = "#070809";
        source.UiScalePercent = 150;
        source.ReducedMotion = true;
        source.WindowX = 301f;
        source.WindowY = 302f;
        source.WindowWidth = 1001f;
        source.WindowHeight = 701f;
        source.LauncherX = 303f;
        source.LauncherY = 304f;
        source.EditorWindowX = 305f;
        source.EditorWindowY = 306f;
        source.EditorWindowWidth = 1101f;
        source.EditorWindowHeight = 801f;
        source.DetachedPanelLayouts.Add(new DetachedPanelWindowLayout
        {
            PanelId = "supply",
            X = 411f,
            Y = 222f,
            Width = 760f,
            Height = 540f,
            IsOpen = true,
        });
        source.SystemAlarms.Find(alarm => alarm.Id == "system:health")
            .Stages[0].Message = "TRANSFERRED HEALTH";
        source.Rules.Add(new AlarmRuleDefinition
        {
            Id = "source-only-rule",
            PanelId = "supply",
        });
        source.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            AlarmKey = "source-only-history",
        });

        var profile = ConfigurationTransferPolicy.CreateProfile(
            source,
            new TransferProfileSelection
            {
                NotificationBehaviors = true,
                SoundSettings = true,
                Appearance = true,
                SystemAlarms = true,
                WindowLayout = true,
            },
            "Merge",
            "0.10.2");
        profile.SoundSettings.Add(new TransferSoundSetting
        {
            AlarmId = "rule:world-specific-guid",
            SoundId = "must-not-import",
        });

        var target = UnmaConfiguration.CreateDefault();
        target.VanillaNotificationRules = new List<VanillaNotificationRule>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                Scope = VanillaNotificationScope.NotificationType,
                Behavior = VanillaNotificationBehavior.Silent,
            },
            new()
            {
                AlarmId = "vanilla:TargetOnly",
                Scope = VanillaNotificationScope.Entity,
                EntityId = 777,
                Behavior = VanillaNotificationBehavior.Hidden,
            },
        };
        target.SoundOverrides = new List<AlarmSoundOverride>
        {
            new()
            {
                AlarmId = "vanilla:UpgradeInProgress",
                SoundId = "beep",
                IsGloballyDisabled = true,
            },
            new()
            {
                AlarmId = "vanilla:TargetOnly",
                SoundId = "target",
            },
        };
        target.SystemAlarms.Add(new SystemAlarmDefinition
        {
            Id = "system:target-only",
            DisplayName = "TARGET ONLY",
        });
        target.Rules.Add(new AlarmRuleDefinition
        {
            Id = "target-only-rule",
            PanelId = "supply",
        });
        target.AlarmHistory.Add(new AlarmHistoryDefinition
        {
            AlarmKey = "target-only-history",
        });
        var originalTargetPanels = target.Panels;

        var preview = ConfigurationTransferPolicy.PreviewImport(
            target,
            profile);
        IsTrue(preview.Added > 0);
        IsTrue(preview.Changed > 0);
        IsTrue(preview.Unchanged > 0);
        IsTrue(preview.Skipped > 0);
        IsTrue(preview.Diagnostics.Any(item => item.Contains(
            "world-specific",
            StringComparison.Ordinal)));
        IsTrue(preview.Diagnostics.Any(item => item.Contains(
            "not stable",
            StringComparison.Ordinal)));
        AreEqual(
            TransferImportChangeKind.Changed,
            FindTransferChange(
                preview,
                TransferProfileCategory.Appearance,
                "reduced-motion").Kind);

        var result = ConfigurationTransferPolicy.Merge(target, profile);
        var merged = result.Configuration;
        IsFalse(ReferenceEquals(target, merged));
        IsFalse(ReferenceEquals(originalTargetPanels, merged.Panels));
        AreEqual(
            VanillaNotificationBehavior.Silent,
            target.VanillaNotificationRules[0].Behavior);
        IsFalse(target.ReducedMotion);
        var importedNormal = merged.VanillaNotificationRules.Find(rule =>
            rule.AlarmId == "vanilla:UpgradeInProgress" &&
            rule.Scope == VanillaNotificationScope.NotificationType);
        AreEqual(VanillaNotificationBehavior.Normal, importedNormal.Behavior);
        IsTrue(merged.VanillaNotificationRules.Any(rule =>
            rule.Scope == VanillaNotificationScope.Entity &&
            rule.EntityId == 777));
        IsFalse(merged.VanillaNotificationRules.Any(rule =>
            rule.Scope == VanillaNotificationScope.Entity &&
            rule.EntityId == 123));

        var mergedSound = merged.SoundOverrides.Find(item =>
            item.AlarmId == "vanilla:UpgradeInProgress");
        AreEqual("triangle", mergedSound.SoundId);
        IsTrue(mergedSound.AutoAcknowledgeOnClear);
        IsTrue(mergedSound.IsGloballyDisabled);
        var addedSound = merged.SoundOverrides.Find(item =>
            item.AlarmId == "vanilla:CannotFindPath");
        AreEqual("horn", addedSound.SoundId);
        IsFalse(addedSound.IsGloballyDisabled);
        IsTrue(merged.SoundOverrides.Any(item =>
            item.AlarmId == "vanilla:TargetOnly"));
        IsFalse(merged.SoundOverrides.Any(item =>
            item.AlarmId == "rule:world-specific-guid"));

        AreEqual("#010203", merged.WarningColor);
        AreEqual(150, merged.UiScalePercent);
        IsTrue(merged.ReducedMotion);
        AreEqual(301f, merged.WindowX);
        AreEqual(801f, merged.EditorWindowHeight);
        AreEqual(1, merged.DetachedPanelLayouts.Count);
        AreEqual("supply", merged.DetachedPanelLayouts[0].PanelId);
        AreEqual(411f, merged.DetachedPanelLayouts[0].X);
        IsTrue(merged.DetachedPanelLayouts[0].IsOpen);
        AreEqual(
            "TRANSFERRED HEALTH",
            merged.SystemAlarms.Find(alarm => alarm.Id == "system:health")
                .Stages[0].Message);
        IsTrue(merged.SystemAlarms.Any(alarm =>
            alarm.Id == "system:target-only"));
        IsTrue(merged.Rules.Any(rule => rule.Id == "target-only-rule"));
        IsFalse(merged.Rules.Any(rule => rule.Id == "source-only-rule"));
        IsTrue(merged.AlarmHistory.Any(item =>
            item.AlarmKey == "target-only-history"));
        IsFalse(merged.AlarmHistory.Any(item =>
            item.AlarmKey == "source-only-history"));

        var secondResult = ConfigurationTransferPolicy.Merge(merged, profile);
        AreEqual(0, secondResult.Preview.Added);
        AreEqual(0, secondResult.Preview.Changed);
        IsTrue(secondResult.Preview.Unchanged > 0);
        AreEqual(
            TransferImportChangeKind.Unchanged,
            FindTransferChange(
                secondResult.Preview,
                TransferProfileCategory.Appearance,
                "reduced-motion").Kind);
        AreEqual(
            merged.VanillaNotificationRules.Count,
            secondResult.Configuration.VanillaNotificationRules.Count);
        AreEqual(
            merged.SoundOverrides.Count,
            secondResult.Configuration.SoundOverrides.Count);
        AreEqual(
            merged.SystemAlarms.Count,
            secondResult.Configuration.SystemAlarms.Count);

        profile.NotificationRules[0].Behavior =
            VanillaNotificationBehavior.Ignored;
        AreEqual(VanillaNotificationBehavior.Normal, importedNormal.Behavior);
    }

    private static void TestTransferProfileSemanticValidation()
    {
        var target = UnmaConfiguration.CreateDefault();
        target.WarningColor = "#AABBCC";
        target.CriticalColor = "#BBCCDD";
        target.EmergencyColor = "#CCDDEE";
        target.UiScalePercent = 125;

        var source = UnmaConfiguration.CreateDefault();
        source.WarningColor = "#010203";
        source.CriticalColor = "red";
        source.EmergencyColor = "#070809";
        source.UiScalePercent = 150;
        source.WindowX = 301f;
        source.WindowY = 302f;
        source.WindowWidth = 1001f;
        source.WindowHeight = 701f;
        source.LauncherX = 303f;
        source.LauncherY = 304f;
        source.EditorWindowX = 305f;
        source.EditorWindowY = 306f;
        source.EditorWindowWidth = 1101f;
        source.EditorWindowHeight = 801f;
        var sourceFoodStage = source.SystemAlarms
            .Find(alarm => alarm.Id == "system:food").Stages[0];
        sourceFoodStage.Message = "VALID IMPORTED FOOD";
        sourceFoodStage.ActiveColor = "#abc";

        var selection = new TransferProfileSelection
        {
            NotificationBehaviors = false,
            SoundSettings = false,
            Appearance = true,
            SystemAlarms = true,
            WindowLayout = true,
        };
        var profile = ConfigurationTransferPolicy.CreateProfile(
            source,
            selection,
            "Semantic validation",
            "0.10.2");
        profile.Appearance.WarningColor = "not-a-color";
        profile.Appearance.EmergencyColor = null;
        profile.Appearance.UiScalePercent = 999;
        profile.WindowLayout.WindowX = float.NaN;
        profile.WindowLayout.WindowWidth = 699f;
        profile.WindowLayout.LauncherX = float.PositiveInfinity;
        profile.WindowLayout.EditorWindowWidth = 699f;
        profile.SystemAlarms.Find(alarm => alarm.Id == "system:health")
            .Stages[0].Conditions[0].Threshold = double.NaN;

        var preview = ConfigurationTransferPolicy.PreviewImport(
            target,
            profile);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.Appearance,
                "warning-color").Kind);
        AreEqual(
            TransferImportChangeKind.Changed,
            FindTransferChange(
                preview,
                TransferProfileCategory.Appearance,
                "critical-color").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.Appearance,
                "emergency-color").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.Appearance,
                "ui-scale-percent").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.WindowLayout,
                "window-x").Kind);
        AreEqual(
            TransferImportChangeKind.Changed,
            FindTransferChange(
                preview,
                TransferProfileCategory.WindowLayout,
                "window-y").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.WindowLayout,
                "window-width").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.WindowLayout,
                "launcher-x").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.WindowLayout,
                "editor-window-width").Kind);
        AreEqual(
            TransferImportChangeKind.Skipped,
            FindTransferChange(
                preview,
                TransferProfileCategory.SystemAlarms,
                "system:health").Kind);
        AreEqual(
            TransferImportChangeKind.Changed,
            FindTransferChange(
                preview,
                TransferProfileCategory.SystemAlarms,
                "system:food").Kind);

        var result = ConfigurationTransferPolicy.Merge(target, profile);
        var merged = result.Configuration;
        AreEqual("#AABBCC", merged.WarningColor);
        AreEqual("red", merged.CriticalColor);
        AreEqual("#CCDDEE", merged.EmergencyColor);
        AreEqual(125, merged.UiScalePercent);
        AreEqual(target.WindowX, merged.WindowX);
        AreEqual(302f, merged.WindowY);
        AreEqual(target.WindowWidth, merged.WindowWidth);
        AreEqual(701f, merged.WindowHeight);
        AreEqual(target.LauncherX, merged.LauncherX);
        AreEqual(304f, merged.LauncherY);
        AreEqual(target.EditorWindowWidth, merged.EditorWindowWidth);
        AreEqual(801f, merged.EditorWindowHeight);
        AreEqual(
            target.SystemAlarms.Find(alarm => alarm.Id == "system:health")
                .Stages[0].Conditions[0].Threshold,
            merged.SystemAlarms.Find(alarm => alarm.Id == "system:health")
                .Stages[0].Conditions[0].Threshold);
        AreEqual(
            "VALID IMPORTED FOOD",
            merged.SystemAlarms.Find(alarm => alarm.Id == "system:food")
                .Stages[0].Message);
        AreEqual(
            "#abc",
            merged.SystemAlarms.Find(alarm => alarm.Id == "system:food")
                .Stages[0].ActiveColor);

        merged.Normalize();
        AreEqual("#AABBCC", merged.WarningColor);
        AreEqual("red", merged.CriticalColor);
        AreEqual(125, merged.UiScalePercent);
        AreEqual(target.WindowX, merged.WindowX);
        AreEqual(target.WindowWidth, merged.WindowWidth);
        AreEqual(target.EditorWindowWidth, merged.EditorWindowWidth);
        AreEqual(
            "VALID IMPORTED FOOD",
            merged.SystemAlarms.Find(alarm => alarm.Id == "system:food")
                .Stages[0].Message);

        var normalizedPreview = ConfigurationTransferPolicy.PreviewImport(
            merged,
            profile);
        AreEqual(0, normalizedPreview.Added);
        AreEqual(0, normalizedPreview.Changed);
        AreEqual(preview.Skipped, normalizedPreview.Skipped);

        var invalidSystemAlarmMutations =
            new Action<SystemAlarmDefinition>[]
            {
                alarm => alarm.Stages = null,
                alarm => alarm.Stages[0].Severity = (AlarmSeverity)999,
                alarm => alarm.Stages[0].ActiveColor = "invalid",
                alarm => alarm.Stages[0].ActivationDelayTicks =
                    AlarmTimingPolicy.MaximumTimingTicks + 1,
                alarm => alarm.Stages[0].Conditions[0].Comparison =
                    (ComparisonOperator)999,
                alarm => alarm.Stages[0].Conditions[0].Threshold =
                    double.PositiveInfinity,
                alarm => alarm.Stages[0].Conditions[0].Hysteresis = -1d,
                alarm => alarm.Stages.RemoveAt(alarm.Stages.Count - 1),
            };
        var expectedHealthJson = SerializeDataContractJson(
            target.SystemAlarms.Find(alarm => alarm.Id == "system:health"));
        foreach (var mutate in invalidSystemAlarmMutations)
        {
            var invalidProfile = ConfigurationTransferPolicy.CreateProfile(
                target,
                new TransferProfileSelection
                {
                    NotificationBehaviors = false,
                    SoundSettings = false,
                    Appearance = false,
                    SystemAlarms = true,
                    WindowLayout = false,
                },
                "Invalid system alarm",
                "0.10.2");
            var invalidHealth = invalidProfile.SystemAlarms.Find(alarm =>
                alarm.Id == "system:health");
            invalidProfile.SystemAlarms =
                new List<SystemAlarmDefinition> { invalidHealth };
            mutate(invalidHealth);

            var invalidPreview = ConfigurationTransferPolicy.PreviewImport(
                target,
                invalidProfile);
            AreEqual(0, invalidPreview.Added);
            AreEqual(0, invalidPreview.Changed);
            AreEqual(0, invalidPreview.Unchanged);
            AreEqual(1, invalidPreview.Skipped);
            AreEqual(
                TransferProfileCategory.SystemAlarms,
                invalidPreview.Changes[0].Category);
            IsTrue(invalidPreview.Diagnostics[0].Contains(
                "System alarm",
                StringComparison.Ordinal));

            var invalidResult = ConfigurationTransferPolicy.Merge(
                target,
                invalidProfile);
            invalidResult.Configuration.Normalize();
            AreEqual(
                expectedHealthJson,
                SerializeDataContractJson(
                    invalidResult.Configuration.SystemAlarms.Find(alarm =>
                        alarm.Id == "system:health")));
        }
    }

    private static void TestTransferProfileSchemaOneSystemAlarmContract()
    {
        var profile = ConfigurationTransferPolicy.CreateProfile(
            UnmaConfiguration.CreateDefault(),
            new TransferProfileSelection
            {
                NotificationBehaviors = false,
                SoundSettings = false,
                Appearance = false,
                SystemAlarms = true,
                WindowLayout = false,
            },
            "Schema contract",
            "0.10.2");
        using var document = JsonDocument.Parse(
            SerializeDataContractJson(profile));
        var root = document.RootElement;
        AreEqual(
            UnmaTransferProfile.CurrentProfileSchemaVersion,
            root.GetProperty("ProfileSchemaVersion").GetInt32());
        var alarm = root.GetProperty("SystemAlarms")[0];
        AssertJsonPropertyNames(
            alarm,
            "AutoAcknowledgeOnClear",
            "DisplayName",
            "Enabled",
            "Id",
            "Stages");
        var stage = alarm.GetProperty("Stages")[0];
        AssertJsonPropertyNames(
            stage,
            "ActivationDelayTicks",
            "ActiveColor",
            "Conditions",
            "Enabled",
            "Id",
            "Logic",
            "Message",
            "MinimumActiveTicks",
            "OperatorAction",
            "Priority",
            "ResetDelayTicks",
            "Severity",
            "SoundId");
        AssertJsonPropertyNames(
            stage.GetProperty("Conditions")[0],
            "Comparison",
            "Hysteresis",
            "MetricId",
            "Threshold");
    }

    private static void TestTransferProfileStoreRoundTripAndAtomicSave()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UNMA-CoreTests-TransferStore-" + Guid.NewGuid().ToString("N"));
        var profilePath = Path.Combine(
            testDirectory,
            "nested",
            "default.json");
        try
        {
            var store = new UnmaTransferProfileStore(profilePath);
            IsTrue(store.Load(out var missingError) == null);
            AreEqual("", missingError);
            AreEqual(Path.GetFullPath(profilePath), store.Path);

            var source = UnmaConfiguration.CreateDefault();
            source.WarningColor = "#111111";
            var profile = ConfigurationTransferPolicy.CreateProfile(
                source,
                new TransferProfileSelection
                {
                    NotificationBehaviors = false,
                    SoundSettings = false,
                    Appearance = true,
                    SystemAlarms = false,
                    WindowLayout = false,
                },
                "First",
                "0.10.2");
            IsTrue(store.SaveIfMissing(
                profile,
                out var alreadyExists,
                out var saveError));
            IsFalse(alreadyExists);
            AreEqual("", saveError);
            IsTrue(File.Exists(profilePath));
            IsFalse(File.Exists(profilePath + ".tmp"));
            var firstBytes = File.ReadAllBytes(profilePath);

            var competingProfile = ConfigurationTransferPolicy.CloneProfile(
                profile);
            competingProfile.Metadata.Name = "Must not overwrite";
            IsFalse(store.SaveIfMissing(
                competingProfile,
                out alreadyExists,
                out saveError));
            IsTrue(alreadyExists);
            AreEqual("", saveError);
            IsTrue(firstBytes.SequenceEqual(File.ReadAllBytes(profilePath)));
            AreEqual(
                0,
                Directory.GetFiles(
                    Path.GetDirectoryName(profilePath),
                    "default.json.create-*.tmp").Length);

            profile.Metadata.Name = "  Second  ";
            profile.Appearance.WarningColor = "#222222";
            IsTrue(store.Save(profile, out saveError));
            AreEqual("", saveError);
            AreEqual("  Second  ", profile.Metadata.Name);
            IsTrue(File.Exists(profilePath + ".bak"));
            IsFalse(File.Exists(profilePath + ".tmp"));
            IsTrue(firstBytes.SequenceEqual(
                File.ReadAllBytes(profilePath + ".bak")));

            var restored = store.Load(out var loadError);
            AreEqual("", loadError);
            AreEqual("Second", restored.Metadata.Name);
            AreEqual("#222222", restored.Appearance.WarningColor);
            AreEqual(1, restored.ProfileSchemaVersion);

            var backupStore = new UnmaTransferProfileStore(
                profilePath + ".bak");
            var backup = backupStore.Load(out loadError);
            AreEqual("", loadError);
            AreEqual("First", backup.Metadata.Name);
            AreEqual("#111111", backup.Appearance.WarningColor);
        }
        finally
        {
            DeleteTemporaryTestDirectory(testDirectory);
        }
    }

    private static void TestTransferProfileStoreFutureAndCorruptProtection()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UNMA-CoreTests-TransferProtection-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            var futurePath = Path.Combine(testDirectory, "future.json");
            File.WriteAllText(
                futurePath,
                "{\"ProfileSchemaVersion\":2," +
                "\"FutureOnly\":\"KEEP-ME\"}");
            File.WriteAllText(futurePath + ".bak", "KEEP-BACKUP");
            File.WriteAllText(futurePath + ".tmp", "KEEP-TEMP");
            var beforeFiles = Directory.GetFiles(testDirectory)
                .ToDictionary(
                    Path.GetFileName,
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

            var futureStore = new UnmaTransferProfileStore(futurePath);
            IsTrue(futureStore.Load(out var futureError) == null);
            IsTrue(futureStore.IsWriteBlocked);
            IsTrue(futureError.Contains("schema 2", StringComparison.Ordinal));
            IsTrue(futureError.Contains("schema 1", StringComparison.Ordinal));
            var safeProfile = new UnmaTransferProfile();
            IsFalse(futureStore.Save(safeProfile, out var saveError));
            AreEqual(futureStore.WriteBlockReason, saveError);
            AssertDirectoryBytesEqual(testDirectory, beforeFiles);

            var directPath = Path.Combine(testDirectory, "direct.json");
            var directStore = new UnmaTransferProfileStore(directPath);
            var directFuture = new UnmaTransferProfile
            {
                ProfileSchemaVersion = 2,
            };
            IsFalse(directStore.Save(directFuture, out var directError));
            IsTrue(directStore.IsWriteBlocked);
            IsTrue(directError.Contains("schema 2", StringComparison.Ordinal));
            IsFalse(File.Exists(directPath));

            var corruptPath = Path.Combine(testDirectory, "corrupt.json");
            const string corruptContents = "{ definitely not json";
            File.WriteAllText(corruptPath, corruptContents);
            var corruptStore = new UnmaTransferProfileStore(corruptPath);
            IsTrue(corruptStore.Load(out var corruptError) == null);
            IsFalse(corruptStore.IsWriteBlocked);
            IsTrue(corruptError.Contains(
                "could not be loaded",
                StringComparison.Ordinal));
            var brokenFiles = Directory.GetFiles(
                testDirectory,
                "corrupt.json.broken-*");
            AreEqual(1, brokenFiles.Length);
            AreEqual(corruptContents, File.ReadAllText(brokenFiles[0]));

            var replacement = new UnmaTransferProfile
            {
                Metadata = new TransferProfileMetadata
                {
                    Name = "Recovered",
                },
                Selection = new TransferProfileSelection
                {
                    NotificationBehaviors = false,
                    SoundSettings = false,
                    Appearance = false,
                    SystemAlarms = false,
                    WindowLayout = false,
                },
            };
            IsTrue(corruptStore.Save(replacement, out saveError));
            AreEqual("", saveError);
            IsFalse(File.Exists(corruptPath + ".tmp"));
            AreEqual(corruptContents, File.ReadAllText(corruptPath + ".bak"));
            var recovered = corruptStore.Load(out var recoveredError);
            AreEqual("", recoveredError);
            AreEqual("Recovered", recovered.Metadata.Name);
        }
        finally
        {
            DeleteTemporaryTestDirectory(testDirectory);
        }
    }

    private static void TestStateStoreFutureSchemaProtection()
    {
        AreEqual(20, UnmaConfiguration.CurrentSchemaVersion);

        var futureConfiguration = UnmaConfiguration.CreateDefault();
        futureConfiguration.SchemaVersion =
            UnmaConfiguration.CurrentSchemaVersion + 1;
        futureConfiguration.UiScalePercent = 777;
        var futurePanels = futureConfiguration.Panels;
        Throws<System.Runtime.Serialization.SerializationException>(
            futureConfiguration.Normalize);
        AreEqual(
            UnmaConfiguration.CurrentSchemaVersion + 1,
            futureConfiguration.SchemaVersion);
        AreEqual(777, futureConfiguration.UiScalePercent);
        IsTrue(ReferenceEquals(futurePanels, futureConfiguration.Panels));

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UNMA-CoreTests-StateStore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            const long futureGameId = 1L;
            var futurePath = Path.Combine(
                testDirectory,
                "unma-world-0000000000000001.json");
            var futureJson =
                "{\"SchemaVersion\":21," +
                "\"FutureOnly\":{\"token\":\"KEEP-ME\"}," +
                "\"Panels\":[],\"Rules\":[]}";
            File.WriteAllText(futurePath, futureJson);
            File.WriteAllText(futurePath + ".bak", "KEEP-BACKUP");
            File.WriteAllText(futurePath + ".tmp", "KEEP-TEMP");
            File.WriteAllText(
                futurePath + ".broken-existing",
                "KEEP-BROKEN");
            var beforeFiles = Directory.GetFiles(testDirectory)
                .ToDictionary(
                    Path.GetFileName,
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

            Mafi.Log.Warnings.Clear();
            var futureStore = new UnmaStateStore(
                testDirectory,
                futureGameId);
            var safeConfiguration = futureStore.Load();
            IsTrue(futureStore.IsWriteBlocked);
            IsTrue(futureStore.WriteBlockReason.Contains(
                "schema 21",
                StringComparison.Ordinal));
            IsTrue(futureStore.WriteBlockReason.Contains(
                "schema 20",
                StringComparison.Ordinal));
            IsTrue(futureStore.WriteBlockReason.Contains(
                "left unchanged",
                StringComparison.Ordinal));
            AreEqual(
                UnmaConfiguration.CurrentSchemaVersion,
                safeConfiguration.SchemaVersion);
            IsTrue(Mafi.Log.Warnings.Any(message => message.Contains(
                futureStore.WriteBlockReason,
                StringComparison.Ordinal)));

            safeConfiguration.UiScalePercent = 777;
            IsFalse(futureStore.Save(safeConfiguration, out var firstError));
            AreEqual(futureStore.WriteBlockReason, firstError);
            AreEqual(777, safeConfiguration.UiScalePercent);
            IsFalse(futureStore.Save(safeConfiguration, out var secondError));
            AreEqual(firstError, secondError);
            AssertDirectoryBytesEqual(testDirectory, beforeFiles);

            const long directFutureGameId = 2L;
            var directFutureStore = new UnmaStateStore(
                testDirectory,
                directFutureGameId);
            var directFuture = UnmaConfiguration.CreateDefault();
            directFuture.SchemaVersion =
                UnmaConfiguration.CurrentSchemaVersion + 1;
            directFuture.UiScalePercent = 777;
            IsFalse(directFutureStore.Save(directFuture, out var directError));
            IsTrue(directFutureStore.IsWriteBlocked);
            AreEqual(directFutureStore.WriteBlockReason, directError);
            AreEqual(777, directFuture.UiScalePercent);
            IsFalse(File.Exists(Path.Combine(
                testDirectory,
                "unma-world-0000000000000002.json")));

            const long legacyGameId = 3L;
            var legacyStore = new UnmaStateStore(
                testDirectory,
                legacyGameId);
            var legacyConfiguration = UnmaConfiguration.CreateDefault();
            legacyConfiguration.SchemaVersion = 19;
            legacyConfiguration.WarningColor = "#123456";
            IsTrue(legacyStore.Save(legacyConfiguration, out var legacyError));
            AreEqual("", legacyError);
            IsFalse(legacyStore.IsWriteBlocked);
            AreEqual(
                UnmaConfiguration.CurrentSchemaVersion,
                legacyConfiguration.SchemaVersion);
            legacyConfiguration.WarningColor = "#654321";
            IsTrue(legacyStore.Save(legacyConfiguration, out legacyError));
            AreEqual("", legacyError);
            var legacyPath = Path.Combine(
                testDirectory,
                "unma-world-0000000000000003.json");
            IsTrue(File.Exists(legacyPath));
            IsTrue(File.Exists(legacyPath + ".bak"));
            var restoredLegacy = legacyStore.Load();
            IsFalse(legacyStore.IsWriteBlocked);
            AreEqual("#654321", restoredLegacy.WarningColor);
        }
        finally
        {
            DeleteTemporaryTestDirectory(testDirectory);
        }
    }

    private static void AssertDirectoryBytesEqual(
        string directory,
        IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        var actualFiles = Directory.GetFiles(directory)
            .ToDictionary(
                Path.GetFileName,
                File.ReadAllBytes,
                StringComparer.Ordinal);
        AreEqual(expectedFiles.Count, actualFiles.Count);
        foreach (var expected in expectedFiles)
        {
            IsTrue(actualFiles.TryGetValue(expected.Key, out var actual));
            IsTrue(expected.Value.SequenceEqual(actual));
        }
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

    private static TransferImportChange FindTransferChange(
        TransferImportPreview preview,
        TransferProfileCategory category,
        string key)
    {
        return preview.Changes.Single(change =>
            change.Category == category &&
            string.Equals(change.Key, key, StringComparison.Ordinal));
    }

    private static string SerializeDataContractJson<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertJsonPropertyNames(
        JsonElement element,
        params string[] expectedNames)
    {
        var expected = expectedNames
            .OrderBy(name => name, StringComparer.Ordinal);
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);
        AreEqual(string.Join("|", expected), string.Join("|", actual));
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
