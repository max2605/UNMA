using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UNMA.Api;

namespace UNMA.Extensions;

/// <summary>
/// Identifies an installed provider mod without depending on Captain of
/// Industry's manifest implementation.
/// </summary>
public sealed class ExternalProviderDescriptor
{
    public string Id { get; set; } = "";
    public string RootDirectoryPath { get; set; } = "";

    public ExternalProviderDescriptor()
    {
    }

    public ExternalProviderDescriptor(string id, string rootDirectoryPath)
    {
        Id = id ?? "";
        RootDirectoryPath = rootDirectoryPath ?? "";
    }
}

/// <summary>
/// One isolated loader error or warning. A bad provider, file, or alarm never
/// prevents valid definitions from other sources from loading.
/// </summary>
public sealed class ExternalLoadDiagnostic
{
    public string Severity { get; }
    public string Code { get; }
    public string ProviderId { get; }
    public string FilePath { get; }
    public string AlarmId { get; }
    public string Message { get; }

    internal ExternalLoadDiagnostic(
        string severity,
        string code,
        string providerId,
        string filePath,
        string alarmId,
        string message)
    {
        Severity = severity;
        Code = code;
        ProviderId = providerId ?? "";
        FilePath = filePath ?? "";
        AlarmId = alarmId ?? "";
        Message = message ?? "";
    }
}

/// <summary>
/// Immutable result of one deterministic scan.
/// </summary>
public sealed class ExternalDefinitionLoadResult
{
    public IReadOnlyList<ExternalAlarmTemplateSnapshot> AlarmTemplates
    {
        get;
    }

    public IReadOnlyList<ExternalLoadDiagnostic> Diagnostics { get; }
    public int ProviderCount { get; }
    public int ScannedFileCount { get; }
    public int LoadedFileCount { get; }

    public bool HasErrors => Diagnostics.Any(item =>
        string.Equals(
            item.Severity,
            "error",
            StringComparison.OrdinalIgnoreCase));

    internal ExternalDefinitionLoadResult(
        IList<ExternalAlarmTemplateSnapshot> alarmTemplates,
        IList<ExternalLoadDiagnostic> diagnostics,
        int providerCount,
        int scannedFileCount,
        int loadedFileCount)
    {
        AlarmTemplates =
            new ReadOnlyCollection<ExternalAlarmTemplateSnapshot>(
                new List<ExternalAlarmTemplateSnapshot>(alarmTemplates));
        Diagnostics = new ReadOnlyCollection<ExternalLoadDiagnostic>(
            new List<ExternalLoadDiagnostic>(diagnostics));
        ProviderCount = providerCount;
        ScannedFileCount = scannedFileCount;
        LoadedFileCount = loadedFileCount;
    }
}

/// <summary>
/// Loads declarative V1 alarms from &lt;provider-root&gt;/UNMA/*.json.
/// </summary>
public static class ExternalDefinitionLoader
{
    public const int SchemaVersion = 1;
    public const int MaxFilesPerProvider = 64;
    public const long MaxFileSizeBytes = 1024L * 1024L;
    public const int MaxAlarmsPerFile = 256;
    public const int MaxAlarmsPerProvider = 256;
    public const int MaxConditionsPerAlarm =
        ExternalContractValidator.MaxConditionsPerAlarm;
    public const int MaxPrototypeIdsPerAlarm =
        ExternalContractValidator.MaxPrototypeIdsPerAlarm;

    public static ExternalDefinitionLoadResult Load(
        IEnumerable<ExternalProviderDescriptor> providers)
    {
        var alarms = new List<ExternalAlarmTemplateSnapshot>();
        var diagnostics = new List<ExternalLoadDiagnostic>();
        var alarmKeys = new HashSet<string>(StringComparer.Ordinal);
        var localizationOwners = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        var providerCount = 0;
        var scannedFileCount = 0;
        var loadedFileCount = 0;

        var orderedProviders = (providers ??
                Enumerable.Empty<ExternalProviderDescriptor>())
            .OrderBy(provider => provider?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(
                provider => provider?.RootDirectoryPath ?? "",
                StringComparer.Ordinal)
            .ToArray();

        // Active mods own their natural LangLib namespace even when they do
        // not ship UNMA JSON. This prevents another provider from redirecting
        // protected keys such as langlib.UNMA.* to a different root.
        foreach (var provider in orderedProviders)
        {
            var providerId = provider?.Id?.Trim() ?? "";
            if (IsLangLibNamespace(providerId) &&
                !localizationOwners.ContainsKey(providerId))
            {
                localizationOwners.Add(providerId, providerId);
            }
        }

        foreach (var provider in orderedProviders)
        {
            if (provider == null)
            {
                diagnostics.Add(Error(
                    "provider.null",
                    "",
                    "",
                    "",
                    "Null provider descriptor was ignored."));
                continue;
            }

            var originalId = provider.Id?.Trim() ?? "";
            if (!ExternalContractValidator.TryNormalizeOwner(
                    provider.Id,
                    out var providerId,
                    out var providerError))
            {
                diagnostics.Add(Error(
                    "provider.invalid_id",
                    originalId,
                    "",
                    "",
                    providerError));
                continue;
            }

            if (!TryResolveRoot(
                    provider.RootDirectoryPath,
                    out var rootPath,
                    out var rootError))
            {
                diagnostics.Add(Error(
                    "provider.invalid_root",
                    providerId,
                    provider.RootDirectoryPath,
                    "",
                    rootError));
                continue;
            }

            if (!providerIds.Add(providerId))
            {
                diagnostics.Add(Error(
                    "provider.duplicate",
                    providerId,
                    rootPath,
                    "",
                    "Duplicate provider descriptor was ignored."));
                continue;
            }

            var extensionDirectory = Path.Combine(rootPath, "UNMA");
            if (!Directory.Exists(extensionDirectory))
            {
                continue;
            }
            providerCount++;

            string[] files;
            try
            {
                files = Directory.GetFiles(
                        extensionDirectory,
                        "*.json",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception)
            {
                diagnostics.Add(Error(
                    "provider.scan_failed",
                    providerId,
                    extensionDirectory,
                    "",
                    "Could not enumerate JSON files: " + exception.Message));
                continue;
            }

            if (files.Length > MaxFilesPerProvider)
            {
                diagnostics.Add(Error(
                    "provider.file_limit",
                    providerId,
                    extensionDirectory,
                    "",
                    "Provider contains " + files.Length +
                    " JSON files; only the first " + MaxFilesPerProvider +
                    " deterministic paths are scanned."));
                files = files.Take(MaxFilesPerProvider).ToArray();
            }

            var providerAlarmCount = 0;
            foreach (var filePath in files)
            {
                if (providerAlarmCount >= MaxAlarmsPerProvider)
                {
                    diagnostics.Add(Error(
                        "provider.alarm_limit",
                        providerId,
                        extensionDirectory,
                        "",
                        "Provider alarm declaration limit of " +
                        MaxAlarmsPerProvider +
                        " was reached; remaining files were ignored."));
                    break;
                }

                scannedFileCount++;
                if (!TryReadFile(
                        providerId,
                        filePath,
                        diagnostics,
                        out var contract))
                {
                    continue;
                }

                loadedFileCount++;
                var definitions = contract.Alarms;
                var remainingAlarmCapacity = MaxAlarmsPerProvider -
                                             providerAlarmCount;
                if (definitions.Count > remainingAlarmCapacity)
                {
                    diagnostics.Add(Error(
                        "provider.alarm_limit",
                        providerId,
                        filePath,
                        "",
                        "Provider declares more than " +
                        MaxAlarmsPerProvider +
                        " alarms; only the first deterministic declarations " +
                        "were processed."));
                    definitions = definitions
                        .Take(remainingAlarmCapacity)
                        .ToList();
                }
                providerAlarmCount += definitions.Count;
                for (var index = 0; index < definitions.Count; index++)
                {
                    var definition = definitions[index];
                    var candidateId = definition?.Id?.Trim() ?? "";
                    if (!ExternalContractValidator.TryNormalizeTemplate(
                            providerId,
                            definition,
                            out var alarm,
                            out var alarmError))
                    {
                        diagnostics.Add(Error(
                            "alarm.invalid",
                            providerId,
                            filePath,
                            candidateId,
                            "Alarm index " + index + ": " + alarmError));
                        continue;
                    }

                    var alarmKey = providerId + "\u001f" + alarm.Id;
                    if (!alarmKeys.Add(alarmKey))
                    {
                        diagnostics.Add(Error(
                            "alarm.duplicate",
                            providerId,
                            filePath,
                            alarm.Id,
                            "Duplicate alarm id for this provider was " +
                            "ignored."));
                        continue;
                    }

                    if (localizationOwners.TryGetValue(
                            alarm.LocalizationNamespace,
                            out var namespaceOwner) &&
                        !string.Equals(
                            namespaceOwner,
                            providerId,
                            StringComparison.Ordinal))
                    {
                        diagnostics.Add(Error(
                            "alarm.localization_namespace_conflict",
                            providerId,
                            filePath,
                            alarm.Id,
                            "Localization namespace is already owned by " +
                            "provider '" + namespaceOwner + "'."));
                        continue;
                    }
                    localizationOwners[alarm.LocalizationNamespace] =
                        providerId;

                    alarms.Add(alarm);
                }
            }
        }

        return new ExternalDefinitionLoadResult(
            alarms,
            diagnostics,
            providerCount,
            scannedFileCount,
            loadedFileCount);
    }

    private static bool TryReadFile(
        string providerId,
        string filePath,
        ICollection<ExternalLoadDiagnostic> diagnostics,
        out ExternalAlarmFileContract contract)
    {
        contract = null;
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length > MaxFileSizeBytes)
            {
                diagnostics.Add(Error(
                    "file.too_large",
                    providerId,
                    filePath,
                    "",
                    "File exceeds the 1 MiB limit."));
                return false;
            }

            var serializer = new DataContractJsonSerializer(
                typeof(ExternalAlarmFileContract),
                new DataContractJsonSerializerSettings
                {
                    MaxItemsInObjectGraph = 100000,
                });
            contract = serializer.ReadObject(stream) as
                ExternalAlarmFileContract;
        }
        catch (Exception exception)
        {
            diagnostics.Add(Error(
                "file.invalid_json",
                providerId,
                filePath,
                "",
                "Could not read definition file: " + exception.Message));
            return false;
        }

        if (contract == null)
        {
            diagnostics.Add(Error(
                "file.empty",
                providerId,
                filePath,
                "",
                "Definition file did not contain a JSON object."));
            return false;
        }

        if (contract.SchemaVersion != SchemaVersion)
        {
            diagnostics.Add(Error(
                "file.unsupported_schema",
                providerId,
                filePath,
                "",
                "schema_version must be " + SchemaVersion + "."));
            return false;
        }

        if (!string.Equals(
                contract.ModId?.Trim(),
                providerId,
                StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "file.provider_mismatch",
                providerId,
                filePath,
                "",
                "mod_id must exactly match the containing provider mod id."));
            return false;
        }

        if (contract.Alarms == null)
        {
            diagnostics.Add(Error(
                "file.alarms_required",
                providerId,
                filePath,
                "",
                "alarms must be a JSON array and cannot be null."));
            return false;
        }

        var alarmCount = contract.Alarms.Count;
        if (alarmCount > MaxAlarmsPerFile)
        {
            diagnostics.Add(Error(
                "file.alarm_limit",
                providerId,
                filePath,
                "",
                "File contains " + alarmCount +
                " alarms; the maximum is " + MaxAlarmsPerFile + "."));
            return false;
        }

        return true;
    }

    private static bool TryResolveRoot(
        string candidate,
        out string rootPath,
        out string error)
    {
        rootPath = "";
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "Provider root directory is required.";
            return false;
        }

        try
        {
            rootPath = Path.GetFullPath(candidate.Trim());
            if (!Directory.Exists(rootPath))
            {
                error = "Provider root directory does not exist.";
                return false;
            }
        }
        catch (Exception exception)
        {
            error = "Provider root directory is invalid: " +
                    exception.Message;
            return false;
        }

        error = "";
        return true;
    }

    private static ExternalLoadDiagnostic Error(
        string code,
        string providerId,
        string filePath,
        string alarmId,
        string message)
    {
        return new ExternalLoadDiagnostic(
            "error",
            code,
            providerId,
            filePath,
            alarmId,
            message);
    }

    private static bool IsLangLibNamespace(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > 128 ||
            !IsAsciiLetterOrDigit(candidate[0]))
        {
            return false;
        }
        for (var index = 1; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (!IsAsciiLetterOrDigit(character) && character != '_' &&
                character != '-')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character >= 'a' && character <= 'z' ||
               character >= 'A' && character <= 'Z' ||
               character >= '0' && character <= '9';
    }

    [DataContract]
    private sealed class ExternalAlarmFileContract
    {
        [DataMember(Name = "$schema", Order = 1, EmitDefaultValue = false)]
        public string Schema { get; set; } = "";

        [DataMember(Name = "schema_version", Order = 2, IsRequired = true)]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "mod_id", Order = 3, IsRequired = true)]
        public string ModId { get; set; } = "";

        [DataMember(Name = "alarms", Order = 4, IsRequired = true)]
        public List<ExternalAlarmTemplateDefinition> Alarms { get; set; } =
            new();
    }
}
