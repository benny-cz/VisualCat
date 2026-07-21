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
