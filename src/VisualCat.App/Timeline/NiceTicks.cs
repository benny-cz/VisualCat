using VisualCat.Domain.Time;

namespace VisualCat.App.Timeline;

public static class NiceTicks
{
    public static long SelectInterval(long spanUs, double pixelWidth, double desiredPixelSpacing = 110)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spanUs);
        if (pixelWidth <= 0 || desiredPixelSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        var desiredCount = Math.Max(1, pixelWidth / desiredPixelSpacing);
        var raw = spanUs / desiredCount;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var step in new[] { 1d, 2d, 5d, 10d })
        {
            var candidate = step * magnitude;
            if (candidate >= raw)
            {
                return Math.Max(1, (long)Math.Round(candidate));
            }
        }

        return Math.Max(1, (long)magnitude * 10);
    }

    /// <summary>
    /// Interval whose aligned instants put at least <paramref name="minimumTicks"/> labels
    /// inside <paramref name="range"/>.
    ///
    /// Spacing alone only asks for a tick count; where the aligned instants actually fall
    /// decides how many land inside the viewport, and a narrow plot — a phone, or a
    /// collapsed pane — routinely ended up showing exactly one. One label states an instant
    /// but not a scale: nothing then says how much time a given width represents, which is
    /// the question an axis exists to answer. Stepping one rung down the 1–2–5 ladder until
    /// two fit costs nothing on a plot that already had enough.
    /// </summary>
    public static long SelectInterval(
        TimeRange range,
        double pixelWidth,
        double desiredPixelSpacing = 110,
        int minimumTicks = 2)
    {
        var interval = SelectInterval(range.DurationUs, pixelWidth, desiredPixelSpacing);
        while (CountTicks(range, interval) < minimumTicks)
        {
            var next = StepDown(interval);
            if (next >= interval)
            {
                break;
            }

            interval = next;
        }

        return interval;
    }

    /// <summary>Aligned instants of <paramref name="intervalUs"/> inside the range, counted
    /// without materializing them.</summary>
    private static long CountTicks(TimeRange range, long intervalUs)
    {
        var start = BucketAlignment.FloorDiv(range.StartInclusive.Value, intervalUs) * intervalUs;
        if (start < range.StartInclusive.Value)
        {
            start += intervalUs;
        }

        return start >= range.EndExclusive.Value
            ? 0
            : (range.EndExclusive.Value - 1 - start) / intervalUs + 1;
    }

    /// <summary>The next lower rung of the 1–2–5 ladder.</summary>
    private static long StepDown(long intervalUs)
    {
        if (intervalUs <= 1)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(intervalUs)));
        var normalized = intervalUs / magnitude;
        var next = normalized > 5 ? 5d : normalized > 2 ? 2d : normalized > 1 ? 1d : 5d;
        var scale = normalized > 1 ? magnitude : magnitude / 10;
        return Math.Max(1, (long)Math.Round(next * scale));
    }

    public static IEnumerable<InstantUs> Enumerate(TimeRange range, long intervalUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalUs);
        var start = BucketAlignment.FloorDiv(range.StartInclusive.Value, intervalUs) * intervalUs;
        if (start < range.StartInclusive.Value)
        {
            start += intervalUs;
        }

        for (var value = start; value < range.EndExclusive.Value; value = checked(value + intervalUs))
        {
            yield return new InstantUs(value);
        }
    }
}
