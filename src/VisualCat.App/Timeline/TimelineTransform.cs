using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Timeline;

public readonly record struct TimelineGeometry
{
    public TimelineGeometry(double left, double top, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Timeline geometry must have positive dimensions.");
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }
}

public sealed class TimelineTransform
{
    public static long MinimumSpanUs(double devicePixelWidth, double microsecondsPerPixel)
    {
        if (!double.IsFinite(devicePixelWidth) || devicePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(devicePixelWidth));
        }

        if (!double.IsFinite(microsecondsPerPixel) || microsecondsPerPixel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(microsecondsPerPixel));
        }

        return Math.Max(1, checked((long)Math.Ceiling(devicePixelWidth * microsecondsPerPixel)));
    }

    public TimelineTransform(TimeRange viewport, TimelineGeometry geometry, IReadOnlyList<LogLevel> displayLevels)
    {
        if (viewport.IsEmpty)
        {
            throw new ArgumentException("A timeline viewport cannot be empty.", nameof(viewport));
        }

        if (displayLevels.Count == 0)
        {
            throw new ArgumentException("At least one display level is required.", nameof(displayLevels));
        }

        Viewport = viewport;
        Geometry = geometry;
        DisplayLevels = displayLevels;
    }

    public TimeRange Viewport { get; }
    public TimelineGeometry Geometry { get; }
    public IReadOnlyList<LogLevel> DisplayLevels { get; }
    public double RowHeight => Geometry.Height / DisplayLevels.Count;

    public double InstantToX(InstantUs instant) =>
        Geometry.Left + ((instant.Value - Viewport.StartInclusive.Value) / (double)Viewport.DurationUs * Geometry.Width);

    public InstantUs XToInstant(double x)
    {
        var fraction = (x - Geometry.Left) / Geometry.Width;
        var offset = (long)Math.Round(fraction * Viewport.DurationUs, MidpointRounding.AwayFromZero);
        return new InstantUs(checked(Viewport.StartInclusive.Value + offset));
    }

    public (double StartX, double EndX) RangeToXInterval(TimeRange range) =>
        (InstantToX(range.StartInclusive), InstantToX(range.EndExclusive));

    public TimeRange XIntervalToRange(double firstX, double secondX, long minimumSpanUs = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSpanUs);
        var left = Math.Clamp(Math.Min(firstX, secondX), Geometry.Left, Geometry.Left + Geometry.Width);
        var right = Math.Clamp(Math.Max(firstX, secondX), Geometry.Left, Geometry.Left + Geometry.Width);
        var start = XToInstant(left);
        var end = XToInstant(right);
        if (end.Value - start.Value < minimumSpanUs)
        {
            end = new InstantUs(Math.Min(Viewport.EndExclusive.Value, checked(start.Value + minimumSpanUs)));
            if (end == start)
            {
                start = new InstantUs(Math.Max(Viewport.StartInclusive.Value, end.Value - minimumSpanUs));
            }
        }

        return new TimeRange(start, end);
    }

    public double LevelToY(LogLevel level)
    {
        var index = DisplayLevels.IndexOf(level);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return Geometry.Top + index * RowHeight;
    }

    public LogLevel? YToLevel(double y)
    {
        if (y < Geometry.Top || y >= Geometry.Top + Geometry.Height)
        {
            return null;
        }

        var index = Math.Min(DisplayLevels.Count - 1, (int)((y - Geometry.Top) / RowHeight));
        return DisplayLevels[index];
    }

    public TimeRange Zoom(
        double focusX,
        double factor,
        long minimumSpanUs,
        long maximumSpanUs,
        TimeRange clampRange,
        double overscrollFraction = 0.05)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSpanUs);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSpanUs, minimumSpanUs);

        var focusFraction = Math.Clamp((focusX - Geometry.Left) / Geometry.Width, 0, 1);
        var focus = Viewport.StartInclusive.Value + (long)Math.Round(focusFraction * Viewport.DurationUs);
        var span = Math.Clamp((long)Math.Round(Viewport.DurationUs * factor), minimumSpanUs, maximumSpanUs);
        var start = focus - (long)Math.Round(focusFraction * span);
        return Clamp(new TimeRange(new InstantUs(start), new InstantUs(checked(start + span))), clampRange, overscrollFraction);
    }

    public TimeRange Pan(double deltaX, TimeRange clampRange, double overscrollFraction = 0.05)
    {
        var delta = -(long)Math.Round(deltaX / Geometry.Width * Viewport.DurationUs);
        return Clamp(
            new TimeRange(
                new InstantUs(checked(Viewport.StartInclusive.Value + delta)),
                new InstantUs(checked(Viewport.EndExclusive.Value + delta))),
            clampRange,
            overscrollFraction);
    }

    /// <summary>
    /// How much of the viewport an overscroll margin may occupy at any zoom.
    /// </summary>
    /// <remarks>
    /// The margin used to be a fraction of the <em>session</em> alone, so it was a constant
    /// amount of time at every zoom: about 3.97 s at both ends of a 75 s session. At Fit that
    /// is invisible; at a 4.7 s viewport it is <b>78 % of the plot</b> — four-fifths of the
    /// screen empty, under a time axis printing an interval in which the log did not yet
    /// exist, which reads as "the data failed to draw" rather than "you are at the start". It
    /// also collapsed the minimap brush to an 8 px sliver exactly where a reader most needs to
    /// know where they are (V2-09).
    ///
    /// Bounding it by the viewport as well keeps the affordance — the reader can still see
    /// that they have reached the end — and makes it read as an edge at every zoom: at Fit
    /// nothing changes, and at the deep zoom the empty band drops from 78 % of the plot to 10 %.
    /// </remarks>
    public const double MaximumOverscrollViewportFraction = 0.10;

    public static TimeRange Clamp(TimeRange viewport, TimeRange session, double overscrollFraction)
    {
        var overscroll = (long)Math.Round(Math.Min(
            session.DurationUs * Math.Clamp(overscrollFraction, 0, 0.5),
            viewport.DurationUs * MaximumOverscrollViewportFraction));
        var minimum = session.StartInclusive.Value - overscroll;
        var maximum = session.EndExclusive.Value + overscroll;
        var start = viewport.StartInclusive.Value;
        var end = viewport.EndExclusive.Value;
        if (start < minimum)
        {
            end += minimum - start;
            start = minimum;
        }

        if (end > maximum)
        {
            start -= end - maximum;
            end = maximum;
        }

        return new TimeRange(new InstantUs(start), new InstantUs(end));
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(values[i], value))
            {
                return i;
            }
        }

        return -1;
    }
}
