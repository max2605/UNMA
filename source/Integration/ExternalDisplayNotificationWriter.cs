using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UNMA.Domain;
using UNMA.Localization;

namespace UNMA.Integration;

/// <summary>
/// Fault-tolerant JSON-lines bridge to the optional UNMA external display.
/// File-system failures must never interrupt the game simulation.
/// </summary>
internal sealed class ExternalDisplayNotificationWriter
{
    private readonly object m_gate = new();
    private readonly string m_path;
    private readonly string m_panelStatePath;
    private string m_lastPanelStateJson = string.Empty;

    internal string Path => m_path;
    internal string PanelStatePath => m_panelStatePath;

    internal ExternalDisplayNotificationWriter(string path = null)
    {
        m_path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UNMA",
            "notifications.jsonl");
        m_panelStatePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(m_path),
            "panels.json");
    }

    internal bool TryPublishPanelState(
        IReadOnlyList<PanelDefinition> panels,
        Func<PanelDefinition, IReadOnlyList<AlarmView>> getViews,
        out bool changed,
        out string error)
    {
        changed = false;
        try
        {
            var state = new PanelStateDto
            {
                // Keep the serialized snapshot stable so unchanged panels do
                // not cause disk writes on every polling interval.
                UpdatedAt = string.Empty,
                Panels = (panels ?? Array.Empty<PanelDefinition>())
                    .Where(panel => panel != null)
                    .Select((panel, index) => new PanelDto
                    {
                        Id = panel.Id ?? string.Empty,
                        Name = panel.Name ?? UnmaText.Get(
                            "default.panel",
                            "PANEL"),
                        Columns = Math.Max(1, Math.Min(8, panel.Columns)),
                        Order = index,
                        IsDashboard = panel.IsDashboard,
                        OwnerEntityId = panel.OwnerEntityId,
                        OwnerEntityTitle = panel.OwnerEntityTitle ?? string.Empty,
                        Alarms = (getViews(panel) ?? Array.Empty<AlarmView>())
                            .Where(alarm => alarm != null)
                            .Select((alarm, slotIndex) => new AlarmDto
                            {
                                Key = alarm.Key ?? string.Empty,
                                SlotId = alarm.SlotId ?? string.Empty,
                                Title = alarm.Name ?? UnmaText.Get(
                                    "default.notification",
                                    "NOTIFICATION"),
                                Detail = alarm.Detail ?? string.Empty,
                                Source = alarm.Source ?? string.Empty,
                                Severity = SeverityName(alarm.Severity),
                                ActiveColor = alarm.ActiveColor ?? string.Empty,
                                IsActive = alarm.IsActive,
                                IsAcknowledged = alarm.IsAcknowledged,
                                IsGoneUnacknowledged = alarm.IsGoneUnacknowledged,
                                IsMissingSource = alarm.IsMissingSource,
                                EntityTitle = alarm.EntityTitle ?? string.Empty,
                                Sequence = alarm.Sequence,
                                SlotIndex = slotIndex,
                            })
                            .ToArray(),
                    })
                    .ToArray(),
            };

            string json;
            var serializer = new DataContractJsonSerializer(
                typeof(PanelStateDto));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, state);
                json = Encoding.UTF8.GetString(stream.ToArray());
            }

            lock (m_gate)
            {
                if (string.Equals(
                        json,
                        m_lastPanelStateJson,
                        StringComparison.Ordinal))
                {
                    error = string.Empty;
                    return true;
                }
                Directory.CreateDirectory(
                    System.IO.Path.GetDirectoryName(m_panelStatePath));
                var temporaryPath = m_panelStatePath + ".tmp";
                File.WriteAllText(
                    temporaryPath,
                    json,
                    new UTF8Encoding(false));
                File.Copy(temporaryPath, m_panelStatePath, true);
                File.Delete(temporaryPath);
                m_lastPanelStateJson = json;
                changed = true;
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal bool TryReset(out string error)
    {
        lock (m_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(m_path));
                File.WriteAllText(m_path, string.Empty, new UTF8Encoding(false));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    internal bool TryPublish(
        string key,
        string title,
        string message,
        string severity,
        string source,
        bool active,
        out string error)
    {
        var line = "{\"timestamp\":\"" + DateTimeOffset.Now.ToString("O")
            + "\",\"key\":\"" + Escape(key)
            + "\",\"active\":" + (active ? "true" : "false")
            + ",\"severity\":\"" + Escape(severity)
            + "\",\"title\":\"" + Escape(title)
            + "\",\"message\":\"" + Escape(message)
            + "\",\"source\":\"" + Escape(source) + "\"}";

        lock (m_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(m_path));
                File.AppendAllText(
                    m_path,
                    line + Environment.NewLine,
                    new UTF8Encoding(false));
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    private static string Escape(string value) => (value ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");

    private static string SeverityName(AlarmSeverity severity) => severity switch
    {
        AlarmSeverity.Emergency => "emergency",
        AlarmSeverity.Critical => "critical",
        AlarmSeverity.Warning => "warning",
        _ => "notice",
    };

    [DataContract]
    private sealed class PanelStateDto
    {
        [DataMember(Name = "updatedAt", Order = 1)]
        public string UpdatedAt = string.Empty;

        [DataMember(Name = "panels", Order = 2)]
        public PanelDto[] Panels = Array.Empty<PanelDto>();
    }

    [DataContract]
    private sealed class PanelDto
    {
        [DataMember(Name = "id", Order = 1)] public string Id = string.Empty;
        [DataMember(Name = "name", Order = 2)] public string Name = string.Empty;
        [DataMember(Name = "columns", Order = 3)] public int Columns;
        [DataMember(Name = "order", Order = 4)] public int Order;
        [DataMember(Name = "isDashboard", Order = 5)] public bool IsDashboard;
        [DataMember(Name = "ownerEntityId", Order = 6)] public int OwnerEntityId;
        [DataMember(Name = "ownerEntityTitle", Order = 7)] public string OwnerEntityTitle = string.Empty;
        [DataMember(Name = "alarms", Order = 8)] public AlarmDto[] Alarms = Array.Empty<AlarmDto>();
    }

    [DataContract]
    private sealed class AlarmDto
    {
        [DataMember(Name = "key", Order = 1)] public string Key = string.Empty;
        [DataMember(Name = "slotId", Order = 2)] public string SlotId = string.Empty;
        [DataMember(Name = "title", Order = 3)] public string Title = string.Empty;
        [DataMember(Name = "detail", Order = 4)] public string Detail = string.Empty;
        [DataMember(Name = "source", Order = 5)] public string Source = string.Empty;
        [DataMember(Name = "severity", Order = 6)] public string Severity = string.Empty;
        [DataMember(Name = "activeColor", Order = 7)] public string ActiveColor = string.Empty;
        [DataMember(Name = "isActive", Order = 8)] public bool IsActive;
        [DataMember(Name = "isAcknowledged", Order = 9)] public bool IsAcknowledged;
        [DataMember(Name = "isGoneUnacknowledged", Order = 10)] public bool IsGoneUnacknowledged;
        [DataMember(Name = "isMissingSource", Order = 11)] public bool IsMissingSource;
        [DataMember(Name = "entityTitle", Order = 12)] public string EntityTitle = string.Empty;
        [DataMember(Name = "sequence", Order = 13)] public long Sequence;
        [DataMember(Name = "slotIndex", Order = 14)] public int SlotIndex;
    }
}
