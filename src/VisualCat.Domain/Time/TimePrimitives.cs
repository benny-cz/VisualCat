using System.Globalization;

namespace VisualCat.Domain.Time;

public readonly record struct InstantUs(long Value) : IComparable<InstantUs>
{
    public static readonly InstantUs MinValue = new(long.MinValue);
    public static readonly InstantUs MaxValue = new(long.MaxValue);

    public static InstantUs FromDateTimeOffset(DateTimeOffset value) =>
        new((value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);

    public static InstantUs FromUnixSeconds(decimal seconds) =>
        new(decimal.ToInt64(decimal.Truncate(seconds * 1_000_000m)));

    public DateTimeOffset ToDateTimeOffset() =>
        new(DateTimeOffset.UnixEpoch.UtcTicks + checked(Value * 10), TimeSpan.Zero);

    public int CompareTo(InstantUs other) => Value.CompareTo(other.Value);
    public static bool operator <(InstantUs left, InstantUs right) => left.Value < right.Value;
    public static bool operator <=(InstantUs left, InstantUs right) => left.Value <= right.Value;
    public static bool operator >(InstantUs left, InstantUs right) => left.Value > right.Value;
    public static bool operator >=(InstantUs left, InstantUs right) => left.Value >= right.Value;
    public static InstantUs operator +(InstantUs value, DurationUs duration) => new(checked(value.Value + duration.Value));
    public static InstantUs operator -(InstantUs value, DurationUs duration) => new(checked(value.Value - duration.Value));
    public static DurationUs operator -(InstantUs left, InstantUs right) => new(checked(left.Value - right.Value));
    public override string ToString() => ToDateTimeOffset().ToString("O", CultureInfo.InvariantCulture);
}

public readonly record struct DurationUs(long Value)
{
    public static DurationUs FromMilliseconds(double value) => new(checked((long)Math.Round(value * 1_000d)));
    public static DurationUs FromSeconds(double value) => new(checked((long)Math.Round(value * 1_000_000d)));
    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(checked(Value * 10));
}

public readonly record struct TimeRange
{
    public TimeRange(InstantUs startInclusive, InstantUs endExclusive)
    {
        if (endExclusive.Value < startInclusive.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "A half-open range cannot end before it starts.");
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    public InstantUs StartInclusive { get; }
    public InstantUs EndExclusive { get; }
    public long DurationUs => EndExclusive.Value - StartInclusive.Value;
    public bool IsEmpty => StartInclusive == EndExclusive;
    public bool Contains(InstantUs instant) =>
        instant.Value >= StartInclusive.Value && instant.Value < EndExclusive.Value;
    public bool Overlaps(TimeRange other) =>
        StartInclusive.Value < other.EndExclusive.Value && other.StartInclusive.Value < EndExclusive.Value;

    public TimeRange Intersect(TimeRange other) =>
        new(
            new InstantUs(Math.Max(StartInclusive.Value, other.StartInclusive.Value)),
            new InstantUs(Math.Max(
                Math.Max(StartInclusive.Value, other.StartInclusive.Value),
                Math.Min(EndExclusive.Value, other.EndExclusive.Value))));
}

public readonly record struct BucketWidth
{
    public BucketWidth(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(microseconds);

        Microseconds = microseconds;
    }

    public long Microseconds { get; }
}

public readonly record struct BucketAlignment(long OriginUs)
{
    public static BucketAlignment UnixEpoch => new(0);

    public TimeRange RangeContaining(InstantUs instant, BucketWidth width)
    {
        var relative = checked(instant.Value - OriginUs);
        var bucket = FloorDiv(relative, width.Microseconds);
        var start = checked(OriginUs + bucket * width.Microseconds);
        return new TimeRange(new InstantUs(start), new InstantUs(checked(start + width.Microseconds)));
    }

    public static long FloorDiv(long dividend, long divisor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisor);

        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}

public readonly record struct Viewport
{
    public Viewport(TimeRange range, int devicePixelWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(devicePixelWidth);

        Range = range;
        DevicePixelWidth = devicePixelWidth;
    }

    public TimeRange Range { get; }
    public int DevicePixelWidth { get; }

    public InstantUs Boundary(int index)
    {
        if ((uint)index > (uint)DevicePixelWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var duration = Range.DurationUs;
        var quotient = Math.DivRem(duration, DevicePixelWidth, out var remainder);
        return new InstantUs(checked(Range.StartInclusive.Value + quotient * index + (remainder * index) / DevicePixelWidth));
    }
}
