using VisualCat.Domain.Time;

namespace VisualCat.App.Timeline;

/// <summary>
/// Which instants the time axis can actually print.
/// </summary>
/// <remarks>
/// An axis showing one label states an instant but not a scale: nothing then says how much
/// time a given width represents, which is the question an axis exists to answer. Choosing a
/// tick interval is not enough to guarantee two, for two independent reasons — the aligned
/// instants may not both fall inside the viewport, and two that do may be too close together
/// to print without overlapping — and at very narrow spans no interval can help, because a
/// one-microsecond viewport contains exactly one whole microsecond. When the ticks cannot
/// supply two labels, the viewport's own ends are labelled instead, which is a scale by
/// construction (finding 17).
/// </remarks>
internal static class TimelineAxis
{
    /// <summary>Gap kept between two axis labels.</summary>
    internal const double LabelGap = 8;

    /// <summary>
    /// How many tick labels would survive the overlap rule at this interval.
    /// </summary>
    internal static int CountDrawableTickLabels(
        TimeRange viewport,
        long intervalUs,
        double plotLeft,
        double plotWidth,
        double labelWidth)
    {
        var drawn = 0;
        var cursor = double.NegativeInfinity;
        foreach (var instant in NiceTicks.Enumerate(viewport, intervalUs))
        {
            var x = LabelX(instant, viewport, plotLeft, plotWidth, labelWidth);
            if (x < cursor)
            {
                continue;
            }

            drawn++;
            cursor = x + labelWidth + LabelGap;
        }

        return drawn;
    }

    /// <summary>
    /// Whether the axis should label the viewport's ends instead of its ticks: only when the
    /// ticks cannot supply two labels, and only when two labels genuinely fit side by side.
    /// </summary>
    internal static bool UseEndpointLabels(
        TimeRange viewport,
        long intervalUs,
        double plotLeft,
        double plotWidth,
        double labelWidth) =>
        CountDrawableTickLabels(viewport, intervalUs, plotLeft, plotWidth, labelWidth) < 2 &&
        plotWidth >= 2 * labelWidth + 2 * LabelGap;

    /// <summary>Where a tick's label starts: beside its gridline, kept inside the plot.</summary>
    internal static double LabelX(
        InstantUs instant,
        TimeRange viewport,
        double plotLeft,
        double plotWidth,
        double labelWidth)
    {
        var x = plotLeft + ((instant.Value - viewport.StartInclusive.Value) / (double)viewport.DurationUs * plotWidth);
        return Math.Clamp(x + 3, plotLeft, Math.Max(plotLeft, plotLeft + plotWidth - labelWidth));
    }
}
