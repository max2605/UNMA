using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UNMA.Domain;
using UNMA.Localization;

namespace UNMA.Ui;

internal static class InstrumentPanelRenderer
{
    internal readonly struct RecorderArchiveColumn
    {
        public readonly float First;
        public readonly float Minimum;
        public readonly float Maximum;
        public readonly float Last;

        public RecorderArchiveColumn(
            float first,
            float minimum,
            float maximum,
            float last)
        {
            First = first;
            Minimum = minimum;
            Maximum = maximum;
            Last = last;
        }
    }

    /// <summary>
    /// A compact recorder trace that remains compatible with the archive's
    /// IReadOnlyList signature. The indexer exposes each column's last value
    /// for the live marker; the renderer additionally consumes its retained
    /// min/max envelope when available.
    /// </summary>
    internal sealed class RecorderArchiveTrace : IReadOnlyList<float>
    {
        private readonly List<RecorderArchiveColumn> m_columns = new();

        public int Count => m_columns.Count;
        public float this[int index] => m_columns[index].Last;
        public RecorderArchiveColumn GetColumn(int index) => m_columns[index];

        public void Clear() => m_columns.Clear();

        public void Add(
            float first,
            float minimum,
            float maximum,
            float last) =>
            m_columns.Add(new RecorderArchiveColumn(
                first,
                minimum,
                maximum,
                last));

        public IEnumerator<float> GetEnumerator()
        {
            foreach (var column in m_columns)
            {
                yield return column.Last;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable
            .GetEnumerator() => GetEnumerator();
    }

    private static readonly Color Bezel = CoiUiPalette.Window;
    private static readonly Color Olive = CoiUiPalette.Surface;
    private static readonly Color Cream = CoiUiPalette.Text;
    private static readonly Color Amber = CoiUiPalette.Orange;
    private static readonly Color Green = CoiUiPalette.Green;
    private static readonly Color Red = new(0.93f, 0.20f, 0.12f, 1f);

    public static void Draw(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        bool isValid,
        IReadOnlyList<float> samples,
        GUIStyle labelStyle,
        GUIStyle smallStyle,
        bool reserveActionBar = false)
    {
        Fill(rect, Bezel);
        Fill(Inset(rect, 4f), Olive);
        // Installed instruments have their actions on a dedicated second
        // header row. Keeping the title on its own row prevents long metric
        // names from being painted underneath ARCHIV / MELD. buttons.
        var titleRect = new Rect(
            rect.x + 9f,
            rect.y + 6f,
            rect.width - (reserveActionBar ? 48f : 18f),
            20f);
        var titleStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            wordWrap = false,
        };
        titleStyle.normal.textColor = Cream;
        GUI.Label(titleRect, definition.Title, titleStyle);

        var faceTop = reserveActionBar ? 56f : 28f;
        var face = new Rect(
            rect.x + 10f,
            rect.y + faceTop,
            rect.width - 20f,
            rect.height - faceTop - 33f);
        if (!isValid)
        {
            Fill(face, new Color(0.12f, 0.10f, 0.08f, 1f));
            var failureStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            failureStyle.normal.textColor = Red;
            GUI.Label(
                face,
                UnmaText.Get(
                    "ui.instrument.source_unavailable",
                    "MEASUREMENT SOURCE\nUNAVAILABLE"),
                failureStyle);
        }
        else
        {
            switch (definition.DisplayType)
            {
                case InstrumentDisplayType.EdgewiseVertical:
                    DrawEdgewise(face, definition, value, true, smallStyle);
                    break;
                case InstrumentDisplayType.EdgewiseHorizontal:
                    DrawEdgewise(face, definition, value, false, smallStyle);
                    break;
                case InstrumentDisplayType.RoundGauge:
                    DrawRoundGauge(face, definition, value, smallStyle);
                    break;
                case InstrumentDisplayType.SevenSegmentRed:
                    DrawDigital(face, definition, value, Red, false, labelStyle);
                    break;
                case InstrumentDisplayType.SevenSegmentGreen:
                    DrawDigital(face, definition, value, Green, false, labelStyle);
                    break;
                case InstrumentDisplayType.NixieTube:
                    DrawDigital(face, definition, value, Amber, true, labelStyle);
                    break;
                case InstrumentDisplayType.CrtAmber:
                    DrawCrt(face, definition, value, samples, Amber, labelStyle, smallStyle);
                    break;
                case InstrumentDisplayType.CrtGreen:
                    DrawCrt(face, definition, value, samples, Green, labelStyle, smallStyle);
                    break;
                default:
                    DrawRecorder(face, definition, value, samples, smallStyle);
                    break;
            }
        }

        var sourceStyle = new GUIStyle(smallStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            wordWrap = false,
        };
        sourceStyle.normal.textColor = CoiUiPalette.Text;
        GUI.Label(
            new Rect(rect.x + 8f, rect.yMax - 29f, rect.width - 16f, 22f),
            definition.EntityTitle + " · " + definition.MetricLabel,
            sourceStyle);
    }

    /// <summary>
    /// Draws the recorder's retained samples as a large paper archive. The
    /// supplied samples are already normalized so the archive uses exactly
    /// the same vertical scale as the compact instrument face.
    /// </summary>
    public static void DrawRecorderArchive(
        Rect rect,
        InstrumentDefinition definition,
        IReadOnlyList<float> normalizedSamples,
        double current,
        double observedMin,
        double observedMax,
        string rangeLabel,
        GUIStyle labelStyle,
        GUIStyle smallStyle,
        bool isValid = true)
    {
        if (rect.width <= 1f || rect.height <= 1f)
        {
            return;
        }

        Fill(rect, Bezel);
        var panel = Inset(rect, 4f);
        Fill(panel, CoiUiPalette.Surface);

        var headerHeight = Mathf.Clamp(panel.height * 0.09f, 38f, 58f);
        var footerHeight = Mathf.Clamp(panel.height * 0.11f, 46f, 66f);
        var header = new Rect(panel.x, panel.y, panel.width, headerHeight);
        Fill(header, CoiUiPalette.Window);

        var archiveTitleStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(headerHeight * 0.40f, 16f, 24f)),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        archiveTitleStyle.normal.textColor = CoiUiPalette.TextBright;
        var headerLabel = string.IsNullOrWhiteSpace(definition.Title)
            ? definition.MetricLabel
            : definition.Title;
        GUI.Label(
            new Rect(header.x + 16f, header.y, header.width * 0.70f - 16f, header.height),
            headerLabel,
            archiveTitleStyle);

        var rangeStyle = new GUIStyle(smallStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(headerHeight * 0.31f, 12f, 18f)),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        rangeStyle.normal.textColor = CoiUiPalette.Yellow;
        GUI.Label(
            new Rect(header.x + header.width * 0.70f, header.y, header.width * 0.30f - 16f, header.height),
            string.IsNullOrWhiteSpace(rangeLabel)
                ? UnmaText.Get("ui.recorder.full_history", "FULL HISTORY")
                : rangeLabel,
            rangeStyle);

        var chart = new Rect(
            panel.x + 16f,
            header.yMax + 10f,
            panel.width - 32f,
            Mathf.Max(1f, panel.height - headerHeight - footerHeight - 28f));
        Fill(chart, new Color(0.90f, 0.86f, 0.69f, 1f));

        var minorGrid = new Color(0.30f, 0.45f, 0.42f, 0.22f);
        var majorGrid = new Color(0.24f, 0.38f, 0.36f, 0.42f);
        const int horizontalDivisions = 10;
        const int verticalDivisions = 20;
        for (var index = 0; index <= verticalDivisions; index++)
        {
            var x = Mathf.Lerp(chart.x, chart.xMax, index / (float)verticalDivisions);
            var major = index % 5 == 0;
            Fill(
                new Rect(x, chart.y, major ? 2f : 1f, chart.height),
                major ? majorGrid : minorGrid);
        }
        for (var index = 0; index <= horizontalDivisions; index++)
        {
            var y = Mathf.Lerp(chart.y, chart.yMax, index / (float)horizontalDivisions);
            var major = index % 5 == 0;
            Fill(
                new Rect(chart.x, y, chart.width, major ? 2f : 1f),
                major ? majorGrid : minorGrid);
        }

        var plot = Inset(chart, 4f);
        if (!isValid)
        {
            Fill(chart, new Color(0.12f, 0.10f, 0.08f, 0.90f));
            var unavailableStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            unavailableStyle.normal.textColor = Red;
            GUI.Label(
                chart,
                UnmaText.Get(
                    "ui.instrument.source_unavailable",
                    "MEASUREMENT SOURCE\nUNAVAILABLE"),
                unavailableStyle);
        }
        else if (normalizedSamples is RecorderArchiveTrace archiveTrace &&
            archiveTrace.Count > 0)
        {
            if (Event.current == null ||
                Event.current.type == EventType.Repaint)
            {
                DrawArchiveTrace(
                    plot,
                    archiveTrace,
                    new Color(0.65f, 0.08f, 0.05f, 1f));

                var last = Mathf.Clamp01(
                    archiveTrace[archiveTrace.Count - 1]);
                var markerCenter = new Vector2(
                    plot.xMax,
                    plot.yMax - last * plot.height);
                Fill(
                    new Rect(
                        markerCenter.x - 4f,
                        markerCenter.y - 4f,
                        8f,
                        8f),
                    CoiUiPalette.Orange);
            }
        }
        else if (normalizedSamples != null && normalizedSamples.Count >= 2)
        {
            DrawTrace(
                plot,
                normalizedSamples,
                new Color(0.65f, 0.08f, 0.05f, 1f));

            var last = Mathf.Clamp01(normalizedSamples[normalizedSamples.Count - 1]);
            var markerCenter = new Vector2(plot.xMax, plot.yMax - last * plot.height);
            Fill(
                new Rect(markerCenter.x - 4f, markerCenter.y - 4f, 8f, 8f),
                CoiUiPalette.Orange);
        }
        else
        {
            var emptyStyle = new GUIStyle(labelStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
            };
            emptyStyle.normal.textColor = new Color(0.15f, 0.16f, 0.15f, 0.85f);
            GUI.Label(
                chart,
                UnmaText.Get("ui.recorder.no_history", "NO HISTORY YET"),
                emptyStyle);
        }

        var paperLabelStyle = new GUIStyle(smallStyle)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        paperLabelStyle.normal.textColor = new Color(0.10f, 0.11f, 0.10f, 0.92f);
        paperLabelStyle.alignment = TextAnchor.UpperLeft;
        GUI.Label(
            new Rect(chart.x + 7f, chart.y + 4f, 140f, 20f),
            UnmaText.Get("ui.recorder.oldest_sample", "OLDEST SAMPLE"),
            paperLabelStyle);
        paperLabelStyle.alignment = TextAnchor.UpperRight;
        GUI.Label(
            new Rect(chart.xMax - 147f, chart.y + 4f, 140f, 20f),
            UnmaText.Get("ui.common.now", "NOW"),
            paperLabelStyle);

        var footer = new Rect(chart.x, chart.yMax + 9f, chart.width, footerHeight);
        var gap = 8f;
        var statWidth = (footer.width - gap * 2f) / 3f;
        DrawArchiveStat(
            new Rect(footer.x, footer.y, statWidth, footer.height),
            UnmaText.Get("ui.common.minimum", "MIN"),
            observedMin,
            definition.Unit,
            labelStyle,
            smallStyle,
            hasValue: isValid);
        DrawArchiveStat(
            new Rect(footer.x + statWidth + gap, footer.y, statWidth, footer.height),
            UnmaText.Get("ui.common.maximum", "MAX"),
            observedMax,
            definition.Unit,
            labelStyle,
            smallStyle,
            hasValue: isValid);
        DrawArchiveStat(
            new Rect(footer.x + (statWidth + gap) * 2f, footer.y, statWidth, footer.height),
            UnmaText.Get("ui.common.current", "CURRENT"),
            current,
            definition.Unit,
            labelStyle,
            smallStyle,
            CoiUiPalette.Orange,
            isValid);
    }

    private static void DrawArchiveStat(
        Rect rect,
        string caption,
        double value,
        string unit,
        GUIStyle labelStyle,
        GUIStyle smallStyle,
        Color? valueColor = null,
        bool hasValue = true)
    {
        Fill(rect, CoiUiPalette.Window);
        var captionStyle = new GUIStyle(smallStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        captionStyle.normal.textColor = CoiUiPalette.Text;
        GUI.Label(
            new Rect(rect.x + 12f, rect.y, rect.width * 0.34f, rect.height),
            caption,
            captionStyle);

        var valueStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(rect.height * 0.34f, 14f, 21f)),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        valueStyle.normal.textColor = valueColor ?? CoiUiPalette.TextBright;
        GUI.Label(
            new Rect(rect.x + rect.width * 0.31f, rect.y, rect.width * 0.69f - 12f, rect.height),
            hasValue
                ? FormatValue(value) + (string.IsNullOrWhiteSpace(unit)
                    ? string.Empty
                    : " " + unit)
                : "—",
            valueStyle);
    }

    private static void DrawEdgewise(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        bool vertical,
        GUIStyle style)
    {
        Fill(rect, CoiUiPalette.TextMuted);
        var scale = vertical
            ? new Rect(rect.center.x - 28f, rect.y + 13f, 56f, rect.height - 26f)
            : new Rect(rect.x + 13f, rect.center.y - 24f, rect.width - 26f, 48f);
        Fill(scale, new Color(0.10f, 0.105f, 0.095f, 1f));
        for (var index = 0; index <= 10; index++)
        {
            var t = index / 10f;
            var tick = vertical
                ? new Rect(scale.x + 5f, scale.yMax - t * scale.height, 16f, 1f)
                : new Rect(scale.x + t * scale.width, scale.y + 5f, 1f, 14f);
            Fill(tick, Cream);
        }
        var normalized = Normalize(definition, value);
        var needle = vertical
            ? new Rect(scale.x + 4f, scale.yMax - normalized * scale.height - 2f, scale.width - 8f, 4f)
            : new Rect(scale.x + normalized * scale.width - 2f, scale.y + 4f, 4f, scale.height - 8f);
        Fill(needle, Red);
        DrawValue(rect, definition, value, style, Color.black);
    }

    private static void DrawRoundGauge(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        GUIStyle style)
    {
        Fill(rect, new Color(0.12f, 0.12f, 0.105f, 1f));
        var center = new Vector2(rect.center.x, rect.yMax - 15f);
        var radius = Mathf.Min(rect.width * 0.43f, rect.height * 0.80f);
        for (var index = 0; index <= 10; index++)
        {
            var angle = Mathf.Lerp(210f, 330f, index / 10f) * Mathf.Deg2Rad;
            var inner = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius - 9f);
            var outer = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Line(inner, outer, Cream, index % 5 == 0 ? 2f : 1f);
        }
        var needleAngle = Mathf.Lerp(210f, 330f, Normalize(definition, value)) * Mathf.Deg2Rad;
        var needleEnd = center + new Vector2(Mathf.Cos(needleAngle), Mathf.Sin(needleAngle)) * (radius - 13f);
        Line(center, needleEnd, Amber, 3f);
        Fill(
            new Rect(center.x - 5f, center.y - 5f, 10f, 10f),
            CoiUiPalette.BorderLight);
        DrawValue(new Rect(rect.x, rect.y, rect.width, 28f), definition, value, style, Cream);
    }

    private static void DrawDigital(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        Color color,
        bool nixie,
        GUIStyle labelStyle)
    {
        Fill(rect, new Color(0.025f, 0.022f, 0.018f, 1f));
        var text = FormatValue(value);
        var displayStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = nixie ? 34 : 30,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        displayStyle.normal.textColor = color;
        if (nixie)
        {
            var count = Math.Max(1, text.Length);
            var width = Mathf.Min(40f, (rect.width - 12f) / count);
            var start = rect.center.x - width * count * 0.5f;
            for (var i = 0; i < count; i++)
            {
                var tube = new Rect(start + i * width + 2f, rect.y + 10f, width - 4f, rect.height - 20f);
                Fill(tube, new Color(0.10f, 0.055f, 0.025f, 1f));
                GUI.Label(tube, text[i].ToString(), displayStyle);
            }
        }
        else
        {
            GUI.Label(new Rect(rect.x + 5f, rect.y + 4f, rect.width - 10f, rect.height - 8f), text, displayStyle);
        }
        var unitStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.LowerRight,
            fontSize = 10,
            clipping = TextClipping.Clip,
        };
        unitStyle.normal.textColor = color;
        GUI.Label(Inset(rect, 7f), definition.Unit, unitStyle);
    }

    private static void DrawCrt(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        IReadOnlyList<float> samples,
        Color phosphor,
        GUIStyle labelStyle,
        GUIStyle smallStyle)
    {
        Fill(rect, new Color(0.012f, 0.035f, 0.025f, 1f));
        for (var y = rect.y + 4f; y < rect.yMax; y += 4f)
        {
            Fill(new Rect(rect.x + 2f, y, rect.width - 4f, 1f), new Color(phosphor.r, phosphor.g, phosphor.b, 0.08f));
        }
        DrawTrace(new Rect(rect.x + 7f, rect.y + 28f, rect.width - 14f, rect.height - 36f), samples, phosphor);
        var valueStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        valueStyle.normal.textColor = phosphor;
        GUI.Label(Inset(rect, 7f), FormatValue(value) + " " + definition.Unit, valueStyle);
    }

    private static void DrawRecorder(
        Rect rect,
        InstrumentDefinition definition,
        double value,
        IReadOnlyList<float> samples,
        GUIStyle style)
    {
        Fill(rect, new Color(0.90f, 0.86f, 0.69f, 1f));
        for (var x = rect.x; x < rect.xMax; x += 12f)
        {
            Fill(new Rect(x, rect.y, 1f, rect.height), new Color(0.30f, 0.45f, 0.42f, 0.24f));
        }
        for (var y = rect.y; y < rect.yMax; y += 10f)
        {
            Fill(new Rect(rect.x, y, rect.width, 1f), new Color(0.30f, 0.45f, 0.42f, 0.24f));
        }
        DrawTrace(Inset(rect, 4f), samples, new Color(0.65f, 0.08f, 0.05f, 1f));
        DrawValue(rect, definition, value, style, Color.black);
    }

    private static void DrawTrace(Rect rect, IReadOnlyList<float> samples, Color color)
    {
        if (samples == null || samples.Count < 2)
        {
            return;
        }
        for (var index = 1; index < samples.Count; index++)
        {
            var x0 = rect.x + (index - 1f) * rect.width / (samples.Count - 1f);
            var x1 = rect.x + index * rect.width / (samples.Count - 1f);
            var y0 = rect.yMax - Mathf.Clamp01(samples[index - 1]) * rect.height;
            var y1 = rect.yMax - Mathf.Clamp01(samples[index]) * rect.height;
            Line(new Vector2(x0, y0), new Vector2(x1, y1), color, 2f);
        }
    }

    private static void DrawValue(Rect rect, InstrumentDefinition definition, double value, GUIStyle source, Color color)
    {
        var style = new GUIStyle(source)
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        style.normal.textColor = color;
        GUI.Label(Inset(rect, 4f), FormatValue(value) + " " + definition.Unit, style);
    }

    private static void DrawArchiveTrace(
        Rect rect,
        RecorderArchiveTrace trace,
        Color color)
    {
        if (trace == null || trace.Count == 0)
        {
            return;
        }

        // Each cached item represents at most one horizontal screen column.
        // Extending its vertical envelope to the previous last/current first
        // value keeps the curve uninterrupted without rasterizing a long DDA
        // segment for every retained source sample.
        var columnWidth = rect.width / trace.Count;
        var previousLast = trace.GetColumn(0).First;
        for (var index = 0; index < trace.Count; index++)
        {
            var column = trace.GetColumn(index);
            var minimum = Mathf.Clamp01(Mathf.Min(
                column.Minimum,
                Mathf.Min(previousLast, column.First)));
            var maximum = Mathf.Clamp01(Mathf.Max(
                column.Maximum,
                Mathf.Max(previousLast, column.First)));
            var top = rect.yMax - maximum * rect.height;
            var bottom = rect.yMax - minimum * rect.height;
            Fill(
                new Rect(
                    rect.x + index * columnWidth,
                    top - 1f,
                    Mathf.Max(1.5f, columnWidth + 0.5f),
                    Mathf.Max(2f, bottom - top + 2f)),
                color);
            previousLast = column.Last;
        }
    }

    private static string FormatValue(double value) =>
        value.ToString(Math.Abs(value) >= 1000d ? "0" : "0.##", CultureInfo.CurrentCulture);

    private static float Normalize(InstrumentDefinition definition, double value) =>
        Mathf.Clamp01((float)((value - definition.Minimum) / (definition.Maximum - definition.Minimum)));

    private static Rect Inset(Rect rect, float amount) =>
        new(rect.x + amount, rect.y + amount, rect.width - amount * 2f, rect.height - amount * 2f);

    private static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Line(Vector2 start, Vector2 end, Color color, float width)
    {
        // Rotating GUI.matrix around coordinates inside a GUILayout scroll
        // view escapes Unity's clip stack and leaves needle/trace fragments
        // elsewhere on screen. A short DDA stroke stays in the active clip
        // rectangle and also produces an uninterrupted recorder line.
        var distance = Vector2.Distance(start, end);
        if (distance <= 0.01f)
        {
            Fill(
                new Rect(
                    start.x - width * 0.5f,
                    start.y - width * 0.5f,
                    width,
                    width),
                color);
            return;
        }
        var steps = Math.Max(1, Mathf.CeilToInt(distance / 1.5f));
        for (var step = 0; step <= steps; step++)
        {
            var point = Vector2.Lerp(start, end, step / (float)steps);
            Fill(
                new Rect(
                    point.x - width * 0.5f,
                    point.y - width * 0.5f,
                    width + 0.5f,
                    width + 0.5f),
                color);
        }
    }
}
