using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace UNMA.Domain;

public enum AlarmHistoryStateFilter
{
    All,
    Open,
    Completed,
    K,
    KQ,
    KG,
    KGQ,
}

public sealed class AlarmHistoryQuery
{
    public string SearchText { get; set; } = "";
    public AlarmHistoryStateFilter StateFilter { get; set; } =
        AlarmHistoryStateFilter.All;
    public AlarmSeverity? SeverityFilter { get; set; }

    public IReadOnlyList<AlarmHistoryDefinition> Apply(
        IEnumerable<AlarmHistoryDefinition> history)
    {
        var searchText = SearchText?.Trim() ?? "";
        return (history ?? Enumerable.Empty<AlarmHistoryDefinition>())
            .Where(entry => entry != null)
            .Select((entry, index) => new
            {
                Entry = entry,
                OriginalIndex = index,
            })
            .Where(item => MatchesSearch(item.Entry, searchText) &&
                           MatchesState(item.Entry, StateFilter) &&
                           (!SeverityFilter.HasValue ||
                            item.Entry.Severity == SeverityFilter.Value))
            .OrderByDescending(item => item.Entry.Sequence)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Entry)
            .ToArray();
    }

    private static bool MatchesSearch(
        AlarmHistoryDefinition entry,
        string searchText)
    {
        return searchText.Length == 0 ||
               Contains(entry.Message, searchText) ||
               Contains(entry.Detail, searchText) ||
               Contains(entry.Source, searchText) ||
               Contains(entry.PanelId, searchText) ||
               Contains(entry.AlarmKey, searchText);
    }

    private static bool Contains(string value, string searchText)
    {
        return (value ?? "").IndexOf(
            searchText,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesState(
        AlarmHistoryDefinition entry,
        AlarmHistoryStateFilter filter)
    {
        return filter switch
        {
            AlarmHistoryStateFilter.All => true,
            AlarmHistoryStateFilter.Open => !entry.CanDelete,
            AlarmHistoryStateFilter.Completed => entry.CanDelete,
            AlarmHistoryStateFilter.K =>
                !entry.IsGone && !entry.IsAcknowledged,
            AlarmHistoryStateFilter.KQ =>
                !entry.IsGone && entry.IsAcknowledged,
            AlarmHistoryStateFilter.KG =>
                entry.IsGone && !entry.IsAcknowledged,
            AlarmHistoryStateFilter.KGQ =>
                entry.IsGone && entry.IsAcknowledged,
            _ => false,
        };
    }
}

public static class AlarmHistoryExport
{
    [DataContract]
    private sealed class JsonRow
    {
        [DataMember(Name = "sequence", Order = 1)]
        public long Sequence;

        [DataMember(Name = "state", Order = 2)]
        public string State = "";

        [DataMember(Name = "severity", Order = 3)]
        public string Severity = "";

        [DataMember(Name = "raised_at_ticks", Order = 4)]
        public double RaisedAtTicks;

        [DataMember(Name = "cleared_at_ticks", Order = 5)]
        public double ClearedAtTicks;

        [DataMember(Name = "acknowledged_at_ticks", Order = 6)]
        public double AcknowledgedAtTicks;

        [DataMember(Name = "message", Order = 7)]
        public string Message = "";

        [DataMember(Name = "detail", Order = 8)]
        public string Detail = "";

        [DataMember(Name = "source", Order = 9)]
        public string Source = "";

        [DataMember(Name = "panel_id", Order = 10)]
        public string PanelId = "";

        [DataMember(Name = "alarm_key", Order = 11)]
        public string AlarmKey = "";
    }

    public static string ToCsv(
        IEnumerable<AlarmHistoryDefinition> history)
    {
        var rows = OrderForExport(history);
        var result = new StringBuilder();
        result.Append(
            "Sequence,State,Severity,RaisedAtTicks,ClearedAtTicks," +
            "AcknowledgedAtTicks,Message,Detail,Source,PanelId,AlarmKey\r\n");
        foreach (var entry in rows)
        {
            AppendCsvField(result, entry.Sequence.ToString(
                CultureInfo.InvariantCulture));
            result.Append(',');
            AppendCsvField(result, entry.StateCode);
            result.Append(',');
            AppendCsvField(result, entry.Severity.ToString());
            result.Append(',');
            AppendCsvField(result, entry.RaisedAtTicks.ToString(
                "R",
                CultureInfo.InvariantCulture));
            result.Append(',');
            AppendCsvField(result, entry.ClearedAtTicks.ToString(
                "R",
                CultureInfo.InvariantCulture));
            result.Append(',');
            AppendCsvField(result, entry.AcknowledgedAtTicks.ToString(
                "R",
                CultureInfo.InvariantCulture));
            result.Append(',');
            AppendCsvField(result, entry.Message);
            result.Append(',');
            AppendCsvField(result, entry.Detail);
            result.Append(',');
            AppendCsvField(result, entry.Source);
            result.Append(',');
            AppendCsvField(result, entry.PanelId);
            result.Append(',');
            AppendCsvField(result, entry.AlarmKey);
            result.Append("\r\n");
        }
        return result.ToString();
    }

    public static string ToJson(
        IEnumerable<AlarmHistoryDefinition> history)
    {
        var rows = OrderForExport(history)
            .Select(entry => new JsonRow
            {
                Sequence = entry.Sequence,
                State = entry.StateCode,
                Severity = entry.Severity.ToString(),
                RaisedAtTicks = entry.RaisedAtTicks,
                ClearedAtTicks = entry.ClearedAtTicks,
                AcknowledgedAtTicks = entry.AcknowledgedAtTicks,
                Message = entry.Message ?? "",
                Detail = entry.Detail ?? "",
                Source = entry.Source ?? "",
                PanelId = entry.PanelId ?? "",
                AlarmKey = entry.AlarmKey ?? "",
            })
            .ToArray();
        var serializer = new DataContractJsonSerializer(typeof(JsonRow[]));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, rows);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IReadOnlyList<AlarmHistoryDefinition> OrderForExport(
        IEnumerable<AlarmHistoryDefinition> history)
    {
        return new AlarmHistoryQuery().Apply(history);
    }

    private static void AppendCsvField(StringBuilder target, string value)
    {
        value ??= "";
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            target.Append(value);
            return;
        }
        target.Append('"');
        target.Append(value.Replace("\"", "\"\""));
        target.Append('"');
    }
}
