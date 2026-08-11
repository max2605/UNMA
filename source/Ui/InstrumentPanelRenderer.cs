using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UNMA.Domain;
using UNMA.Localization;

namespace UNMA.Ui;

internal static class InstrumentPanelRenderer
{
    internal readonly struct HistorianColumn
    {
        public readonly float First;
        public readonly float Minimum;
        public readonly float Maximum;
        public readonly float Last;

        public HistorianColumn(
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
    /// A compact historian trace that remains compatible with the chart's
    /// IReadOnlyList signature. The indexer exposes each column's last value
    /// for the live marker; the renderer additionally consumes its retained
    /// min/max envelope when available.
    /// </summary>
    internal sealed class HistorianTrace : IReadOnlyList<float>
    {
        private readonly List<HistorianColumn> m_columns = new();

        public int Count => m_columns.Count;
        public float this[int index] => m_columns[index].Last;
        public HistorianColumn GetColumn(int index) => m_columns[index];

        public void Clear() => m_columns.Clear();

        public void Add(
            float first,
            float minimum,
            float maximum,
            float last) =>
            m_columns.Add(new HistorianColumn(
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
        NativeGUI.Label(
            titleRect,
            FitText(definition.Title, titleStyle, titleRect.width),
            titleStyle);

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
            NativeGUI.Label(
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
        NativeGUI.Label(
            new Rect(rect.x + 8f, rect.yMax - 29f, rect.width - 16f, 22f),
            FitText(
                definition.EntityTitle + " · " + definition.MetricLabel,
                sourceStyle,
                rect.width - 16f),
            sourceStyle);
    }

    /// <summary>
    /// Draws retained game-time samples and historian analysis for every
    /// instrument type. Samples are normalized to the instrument scale.
    /// </summary>
    public static void DrawHistorian(
        Rect rect,
        InstrumentDefinition definition,
        IReadOnlyList<float> normalizedSamples,
        double current,
        InstrumentForecastResult forecast,
        bool hasForecast,
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
        var footerHeight = Mathf.Clamp(panel.height * 0.24f, 112f, 148f);
        var header = new Rect(panel.x, panel.y, panel.width, headerHeight);
        Fill(header, CoiUiPalette.Window);

        var historianTitleStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(headerHeight * 0.40f, 16f, 24f)),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        historianTitleStyle.normal.textColor = CoiUiPalette.TextBright;
        var headerLabel = string.IsNullOrWhiteSpace(definition.Title)
            ? definition.MetricLabel
            : definition.Title;
        NativeGUI.Label(
            new Rect(header.x + 16f, header.y, header.width * 0.70f - 16f, header.height),
            headerLabel,
            historianTitleStyle);

        var rangeStyle = new GUIStyle(smallStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(headerHeight * 0.31f, 12f, 18f)),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        rangeStyle.normal.textColor = CoiUiPalette.Yellow;
        NativeGUI.Label(
            new Rect(header.x + header.width * 0.70f, header.y, header.width * 0.30f - 16f, header.height),
            string.IsNullOrWhiteSpace(rangeLabel)
                ? UnmaText.Get("ui.historian.full_history", "FULL HISTORY")
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
            NativeGUI.Label(
                chart,
                UnmaText.Get(
                    "ui.instrument.source_unavailable",
                    "MEASUREMENT SOURCE\nUNAVAILABLE"),
                unavailableStyle);
        }
        else if (normalizedSamples is HistorianTrace historianTrace &&
            historianTrace.Count > 0)
        {
            DrawHistorianTrace(
                plot,
                historianTrace,
                new Color(0.65f, 0.08f, 0.05f, 1f));

            var last = Mathf.Clamp01(
                historianTrace[historianTrace.Count - 1]);
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
            NativeGUI.Label(
                chart,
                UnmaText.Get("ui.historian.no_history", "NO HISTORY YET"),
                emptyStyle);
        }

        var timelineLabelStyle = new GUIStyle(smallStyle)
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
        };
        timelineLabelStyle.normal.textColor = new Color(0.10f, 0.11f, 0.10f, 0.92f);
        timelineLabelStyle.alignment = TextAnchor.UpperLeft;
        NativeGUI.Label(
            new Rect(chart.x + 7f, chart.y + 4f, 140f, 20f),
            UnmaText.Get("ui.historian.range_start", "RANGE START"),
            timelineLabelStyle);
        timelineLabelStyle.alignment = TextAnchor.UpperRight;
        NativeGUI.Label(
            new Rect(chart.xMax - 147f, chart.y + 4f, 140f, 20f),
            UnmaText.Get("ui.common.now", "NOW"),
            timelineLabelStyle);

        var footer = new Rect(chart.x, chart.yMax + 9f, chart.width, footerHeight);
        const float gap = 7f;
        var statWidth = (footer.width - gap * 3f) / 4f;
        var rowHeight = (footer.height - gap) * 0.5f;
        var firstRowY = footer.y;
        var secondRowY = footer.y + rowHeight + gap;
        var statisticsAvailable = isValid && hasForecast;
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 0, firstRowY, rowHeight),
            UnmaText.Get("ui.common.current", "CURRENT"),
            isValid ? FormatMeasurement(current, definition.Unit) : "—",
            labelStyle,
            smallStyle,
            CoiUiPalette.Orange);
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 1, firstRowY, rowHeight),
            UnmaText.Get("ui.common.minimum", "MIN"),
            statisticsAvailable
                ? FormatMeasurement(forecast.MinimumValue, definition.Unit)
                : "—",
            labelStyle,
            smallStyle);
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 2, firstRowY, rowHeight),
            UnmaText.Get("ui.historian.average", "AVERAGE"),
            statisticsAvailable
                ? FormatMeasurement(forecast.AverageValue, definition.Unit)
                : "—",
            labelStyle,
            smallStyle);
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 3, firstRowY, rowHeight),
            UnmaText.Get("ui.common.maximum", "MAX"),
            statisticsAvailable
                ? FormatMeasurement(forecast.MaximumValue, definition.Unit)
                : "—",
            labelStyle,
            smallStyle);

        var trendAvailable = statisticsAvailable &&
                             forecast.Status !=
                             InstrumentForecastStatus.InsufficientData;
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 0, secondRowY, rowHeight),
            UnmaText.Get("ui.historian.forecast", "FORECAST"),
            HistorianStatusLabel(isValid, hasForecast, forecast),
            labelStyle,
            smallStyle,
            HistorianStatusColor(isValid, hasForecast, forecast));
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 1, secondRowY, rowHeight),
            UnmaText.Get("ui.historian.rate_per_month", "RATE / MONTH"),
            trendAvailable
                ? FormatRatePerMonth(forecast.RatePerMonth, definition.Unit)
                : "—",
            labelStyle,
            smallStyle);
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 2, secondRowY, rowHeight),
            UnmaText.Get("ui.historian.r_squared", "R²"),
            trendAvailable
                ? forecast.RSquared.ToString("0.000", CultureInfo.CurrentCulture)
                : "—",
            labelStyle,
            smallStyle);
        DrawHistorianStat(
            HistorianStatRect(footer, statWidth, gap, 3, secondRowY, rowHeight),
            HistorianEtaCaption(hasForecast, forecast),
            statisticsAvailable ? HistorianEtaLabel(forecast) : "—",
            labelStyle,
            smallStyle,
            forecast.HasEta ? CoiUiPalette.Yellow : (Color?)null);
    }

    private static Rect HistorianStatRect(
        Rect footer,
        float width,
        float gap,
        int column,
        float y,
        float height) =>
        new(footer.x + column * (width + gap), y, width, height);

    private static void DrawHistorianStat(
        Rect rect,
        string caption,
        string value,
        GUIStyle labelStyle,
        GUIStyle smallStyle,
        Color? valueColor = null)
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
        NativeGUI.Label(
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
        NativeGUI.Label(
            new Rect(rect.x + rect.width * 0.31f, rect.y, rect.width * 0.69f - 12f, rect.height),
            string.IsNullOrWhiteSpace(value) ? "—" : value,
            valueStyle);
    }

    private static string HistorianStatusLabel(
        bool isValid,
        bool hasForecast,
        InstrumentForecastResult forecast)
    {
        if (!isValid)
        {
            return UnmaText.Get(
                "ui.historian.status.source_unavailable",
                "SOURCE UNAVAILABLE");
        }
        if (!hasForecast)
        {
            return UnmaText.Get(
                "ui.historian.status.forecast_unavailable",
                "FORECAST UNAVAILABLE");
        }
        return forecast.Status switch
        {
            InstrumentForecastStatus.InsufficientData => UnmaText.Get(
                "ui.historian.status.insufficient_data",
                "INSUFFICIENT DATA"),
            InstrumentForecastStatus.Stable => UnmaText.Get(
                "ui.historian.status.stable",
                "STABLE"),
            InstrumentForecastStatus.Unreliable => UnmaText.Get(
                "ui.historian.status.unreliable",
                "UNRELIABLE"),
            _ => forecast.Direction == InstrumentForecastDirection.Falling
                ? UnmaText.Get(
                    "ui.historian.status.moving_falling",
                    "MOVING ↓")
                : UnmaText.Get(
                    "ui.historian.status.moving_rising",
                    "MOVING ↑"),
        };
    }

    private static Color? HistorianStatusColor(
        bool isValid,
        bool hasForecast,
        InstrumentForecastResult forecast)
    {
        if (!isValid || !hasForecast)
        {
            return CoiUiPalette.TextMuted;
        }
        return forecast.Status switch
        {
            InstrumentForecastStatus.Stable => CoiUiPalette.Green,
            InstrumentForecastStatus.Unreliable => CoiUiPalette.Orange,
            InstrumentForecastStatus.Moving => CoiUiPalette.Yellow,
            _ => CoiUiPalette.TextMuted,
        };
    }

    private static string HistorianEtaCaption(
        bool hasForecast,
        InstrumentForecastResult forecast)
    {
        if (!hasForecast)
        {
            return UnmaText.Get("ui.historian.eta", "ETA");
        }
        return forecast.Direction switch
        {
            InstrumentForecastDirection.Rising => UnmaText.Get(
                "ui.historian.eta_to_max",
                "ETA → SCALE MAX"),
            InstrumentForecastDirection.Falling => UnmaText.Get(
                "ui.historian.eta_to_min",
                "ETA → SCALE MIN"),
            _ => UnmaText.Get("ui.historian.eta", "ETA"),
        };
    }

    private static string HistorianEtaLabel(
        InstrumentForecastResult forecast)
    {
        return forecast.EtaStatus switch
        {
            InstrumentForecastEtaStatus.Available => UnmaText.Format(
                "ui.historian.eta.available",
                "AVAILABLE · {0}",
                FormatGameDuration(forecast.EtaTicks)),
            InstrumentForecastEtaStatus.BeyondHorizon => UnmaText.Get(
                "ui.historian.eta.beyond_horizon",
                "BEYOND HORIZON · > 100 YEARS"),
            _ => UnmaText.Get("ui.historian.eta.none", "NONE"),
        };
    }

    private static string FormatGameDuration(double ticks)
    {
        if (ticks >= GameTimeWindowPolicy.SimTicksPerYear)
        {
            return UnmaText.Format(
                "ui.historian.duration.years",
                "{0} YEARS",
                FormatDurationNumber(
                    ticks / GameTimeWindowPolicy.SimTicksPerYear));
        }
        if (ticks >= GameTimeWindowPolicy.SimTicksPerMonth)
        {
            return UnmaText.Format(
                "ui.historian.duration.months",
                "{0} MONTHS",
                FormatDurationNumber(
                    ticks / GameTimeWindowPolicy.SimTicksPerMonth));
        }
        if (ticks >= GameTimeWindowPolicy.SimTicksPerDay)
        {
            return UnmaText.Format(
                "ui.historian.duration.days",
                "{0} DAYS",
                FormatDurationNumber(
                    ticks / GameTimeWindowPolicy.SimTicksPerDay));
        }
        return UnmaText.Format(
            "ui.historian.duration.ticks",
            "{0} TICKS",
            FormatDurationNumber(Math.Max(0d, ticks)));
    }

    private static string FormatDurationNumber(double value) =>
        value.ToString(
            value >= 100d ? "0" : "0.##",
            CultureInfo.CurrentCulture);

    private static string FormatMeasurement(double value, string unit) =>
        FormatValue(value) + (string.IsNullOrWhiteSpace(unit)
            ? string.Empty
            : " " + unit);

    private static string FormatRatePerMonth(double value, string unit)
    {
        var formatted = value.ToString(
            Math.Abs(value) >= 1000d ? "+0;-0;0" : "+0.###;-0.###;0",
            CultureInfo.CurrentCulture);
        return formatted + (string.IsNullOrWhiteSpace(unit)
                   ? string.Empty
                   : " " + unit) + " / " +
               UnmaText.Get("ui.historian.month_short", "MO");
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
        var valueBadge = new Rect(
            rect.center.x - Mathf.Min(82f, rect.width * 0.36f),
            rect.y + 3f,
            Mathf.Min(164f, rect.width * 0.72f),
            24f);
        Fill(valueBadge, new Color(0.08f, 0.08f, 0.07f, 0.96f));
        DrawValue(valueBadge, definition, value, style, Cream);
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
                NativeGUI.Label(tube, text[i].ToString(), displayStyle);
            }
        }
        else
        {
            NativeGUI.Label(new Rect(rect.x + 5f, rect.y + 4f, rect.width - 10f, rect.height - 8f), text, displayStyle);
        }
        var unitStyle = new GUIStyle(labelStyle)
        {
            alignment = TextAnchor.LowerRight,
            fontSize = 10,
            clipping = TextClipping.Clip,
        };
        unitStyle.normal.textColor = color;
        NativeGUI.Label(Inset(rect, 7f), definition.Unit, unitStyle);
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
        NativeGUI.Label(Inset(rect, 7f), FormatValue(value) + " " + definition.Unit, valueStyle);
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
        var labelRect = Inset(rect, 4f);
        var label = FormatValue(value) +
                    (string.IsNullOrWhiteSpace(definition.Unit)
                        ? string.Empty
                        : " " + definition.Unit);
        NativeGUI.Label(
            labelRect,
            FitText(label, style, labelRect.width),
            style);
    }

    private static string FitText(string value, GUIStyle style, float maxWidth)
    {
        value ??= string.Empty;
        if (maxWidth <= 1f || style.CalcSize(new GUIContent(value)).x <= maxWidth)
        {
            return value;
        }

        const string suffix = "...";
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidate = value.Substring(0, middle).TrimEnd() + suffix;
            if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        return value.Substring(0, low).TrimEnd() + suffix;
    }

    private static void DrawHistorianTrace(
        Rect rect,
        HistorianTrace trace,
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
        var previous = NativeGUI.color;
        NativeGUI.color = color;
        NativeGUI.DrawTexture(rect, Texture2D.whiteTexture);
        NativeGUI.color = previous;
    }

    private static void Line(Vector2 start, Vector2 end, Color color, float width)
    {
        // A short DDA stroke stays inside the UI Toolkit canvas clip and also
        // produces an uninterrupted recorder line.
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
