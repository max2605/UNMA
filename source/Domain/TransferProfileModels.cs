using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace UNMA.Domain;

public enum TransferProfileCategory
{
    NotificationBehaviors = 0,
    SoundSettings = 1,
    Appearance = 2,
    SystemAlarms = 3,
    WindowLayout = 4,
}

public enum TransferImportChangeKind
{
    Added = 0,
    Changed = 1,
    Unchanged = 2,
    Skipped = 3,
}

[DataContract]
public sealed class TransferProfileSelection
{
    [DataMember(Order = 1)] public bool NotificationBehaviors = true;
    [DataMember(Order = 2)] public bool SoundSettings = true;
    [DataMember(Order = 3)] public bool Appearance = true;
    [DataMember(Order = 4)] public bool SystemAlarms = true;
    [DataMember(Order = 5)] public bool WindowLayout;

    // Null means all transferable notification rules. A non-null list is an
    // exact allow-list of VanillaNotificationSuppressionPolicy.RuleIdentity
    // values. Entity-scoped identities are never transferable.
    [DataMember(Order = 6, EmitDefaultValue = false)]
    public List<string> NotificationRuleIdentities;
}

[DataContract]
public sealed class TransferProfileMetadata
{
    [DataMember(Order = 1)] public string Name = "Default";
    [DataMember(Order = 2)] public string CreatedUtc = "";
    [DataMember(Order = 3)] public string SourceVersion = "";
    [DataMember(Order = 4)] public int SkippedItems;
    [DataMember(Order = 5)] public List<string> Diagnostics = new();
}

[DataContract]
public sealed class TransferSoundSetting
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public string SoundId = "auto";
    [DataMember(Order = 3)] public bool AutoAcknowledgeOnClear;
}

[DataContract]
public sealed class TransferNotificationRule
{
    [DataMember(Order = 1)] public string AlarmId = "";
    [DataMember(Order = 2)] public VanillaNotificationScope Scope;
    [DataMember(Order = 3)] public VanillaNotificationBehavior Behavior;
    [DataMember(Order = 4)] public string EntityPrototypeId = "";
}

[DataContract]
public sealed class TransferAppearanceSettings
{
    [DataMember(Order = 1)] public string WarningColor = "#F0C541";
    [DataMember(Order = 2)] public string CriticalColor = "#F05A32";
    [DataMember(Order = 3)] public string EmergencyColor = "#E51B23";
    [DataMember(Order = 4)] public int UiScalePercent = 100;
    [DataMember(Order = 5)] public bool ReducedMotion;
}

[DataContract]
public sealed class TransferWindowLayout
{
    [DataMember(Order = 1)] public float WindowX = 120f;
    [DataMember(Order = 2)] public float WindowY = 80f;
    [DataMember(Order = 3)] public float WindowWidth = 980f;
    [DataMember(Order = 4)] public float WindowHeight = 720f;
    [DataMember(Order = 5)] public float LauncherX = -1f;
    [DataMember(Order = 6)] public float LauncherY = -1f;
    [DataMember(Order = 7)] public float EditorWindowX = 180f;
    [DataMember(Order = 8)] public float EditorWindowY = 110f;
    [DataMember(Order = 9)] public float EditorWindowWidth = 1080f;
    [DataMember(Order = 10)] public float EditorWindowHeight = 720f;
    [DataMember(Order = 11, EmitDefaultValue = false)]
    public List<TransferDetachedPanelWindowLayout> DetachedPanels = new();
}

[DataContract]
public sealed class TransferDetachedPanelWindowLayout
{
    [DataMember(Order = 1)] public string PanelId = "";
    [DataMember(Order = 2)] public float X = 40f;
    [DataMember(Order = 3)] public float Y = 60f;
    [DataMember(Order = 4)] public float Width = 620f;
    [DataMember(Order = 5)] public float Height = 460f;
    [DataMember(Order = 6)] public bool IsOpen;
}

[DataContract]
public sealed class UnmaTransferProfile
{
    public const int CurrentProfileSchemaVersion = 1;

    [DataMember(Order = 1)]
    public int ProfileSchemaVersion = CurrentProfileSchemaVersion;
    [DataMember(Order = 2)] public TransferProfileMetadata Metadata = new();
    [DataMember(Order = 3)] public TransferProfileSelection Selection = new();
    [DataMember(Order = 4)]
    public List<TransferNotificationRule> NotificationRules = new();
    [DataMember(Order = 5)]
    public List<TransferSoundSetting> SoundSettings = new();
    [DataMember(Order = 6, EmitDefaultValue = false)]
    public TransferAppearanceSettings Appearance;
    [DataMember(Order = 7)]
    public List<SystemAlarmDefinition> SystemAlarms = new();
    [DataMember(Order = 8, EmitDefaultValue = false)]
    public TransferWindowLayout WindowLayout;

    public void Normalize()
    {
        if (ProfileSchemaVersion > CurrentProfileSchemaVersion)
        {
            throw new SerializationException(
                "UNMA transfer profile schema " + ProfileSchemaVersion +
                " is newer than supported schema " +
                CurrentProfileSchemaVersion + ".");
        }
        if (ProfileSchemaVersion <= 0)
        {
            ProfileSchemaVersion = CurrentProfileSchemaVersion;
        }

        Metadata ??= new TransferProfileMetadata();
        Metadata.Name = string.IsNullOrWhiteSpace(Metadata.Name)
            ? "Default"
            : Metadata.Name.Trim();
        Metadata.CreatedUtc = Metadata.CreatedUtc?.Trim() ?? "";
        Metadata.SourceVersion = Metadata.SourceVersion?.Trim() ?? "";
        Metadata.SkippedItems = Math.Max(0, Metadata.SkippedItems);
        Metadata.Diagnostics ??= new List<string>();
        Metadata.Diagnostics.RemoveAll(string.IsNullOrWhiteSpace);
        for (var index = 0; index < Metadata.Diagnostics.Count; index++)
        {
            Metadata.Diagnostics[index] = Metadata.Diagnostics[index].Trim();
        }

        Selection ??= new TransferProfileSelection();
        if (Selection.NotificationRuleIdentities != null)
        {
            var identities = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var identity in Selection.NotificationRuleIdentities)
            {
                var normalized = identity?.Trim() ?? "";
                if (normalized.Length > 0 && seen.Add(normalized))
                {
                    identities.Add(normalized);
                }
            }
            Selection.NotificationRuleIdentities = identities;
        }

        NotificationRules ??= new List<TransferNotificationRule>();
        NotificationRules.RemoveAll(rule => rule == null);
        SoundSettings ??= new List<TransferSoundSetting>();
        SoundSettings.RemoveAll(setting => setting == null);
        foreach (var setting in SoundSettings)
        {
            setting.AlarmId = setting.AlarmId?.Trim() ?? "";
            setting.SoundId = string.IsNullOrWhiteSpace(setting.SoundId)
                ? "auto"
                : setting.SoundId.Trim();
        }

        SystemAlarms ??= new List<SystemAlarmDefinition>();
        SystemAlarms.RemoveAll(alarm => alarm == null);
        if (WindowLayout != null)
        {
            WindowLayout.DetachedPanels ??=
                new List<TransferDetachedPanelWindowLayout>();
            WindowLayout.DetachedPanels.RemoveAll(layout =>
                layout == null || string.IsNullOrWhiteSpace(layout.PanelId));
            var seenPanelIds = new HashSet<string>(StringComparer.Ordinal);
            WindowLayout.DetachedPanels = WindowLayout.DetachedPanels
                .Where(layout =>
                {
                    layout.PanelId = layout.PanelId.Trim();
                    return seenPanelIds.Add(layout.PanelId);
                })
                .ToList();
        }
    }
}

public sealed class TransferImportChange
{
    public TransferProfileCategory Category;
    public string Key = "";
    public TransferImportChangeKind Kind;
    public string Diagnostic = "";
}

public sealed class TransferImportPreview
{
    public int Added;
    public int Changed;
    public int Unchanged;
    public int Skipped;
    public List<TransferImportChange> Changes = new();
    public List<string> Diagnostics = new();

    internal void Add(
        TransferProfileCategory category,
        string key,
        TransferImportChangeKind kind,
        string diagnostic = "")
    {
        var change = new TransferImportChange
        {
            Category = category,
            Key = key ?? "",
            Kind = kind,
            Diagnostic = diagnostic ?? "",
        };
        Changes.Add(change);
        switch (kind)
        {
            case TransferImportChangeKind.Added:
                Added++;
                break;
            case TransferImportChangeKind.Changed:
                Changed++;
                break;
            case TransferImportChangeKind.Unchanged:
                Unchanged++;
                break;
            case TransferImportChangeKind.Skipped:
                Skipped++;
                if (!string.IsNullOrWhiteSpace(diagnostic))
                {
                    Diagnostics.Add(diagnostic);
                }
                break;
        }
    }
}

public sealed class TransferImportResult
{
    public UnmaConfiguration Configuration;
    public TransferImportPreview Preview;
}
