using System.Globalization;

namespace VisualCat.Domain.Time;

/// <summary>Represents a UTC instant as signed microseconds since the Unix epoch.</summary>
public readonly record struct InstantUs(long Value) : IComparable<InstantUs>
{
    /// <summary>Gets the earliest representable instant.</summary>
    public static readonly InstantUs MinValue = new(long.MinValue);
    /// <summary>Gets the latest representable instant.</summary>
    public static readonly InstantUs MaxValue = new(long.MaxValue);

    /// <summary>Converts a UTC-aware platform timestamp to microseconds.</summary>
    public static InstantUs FromDateTimeOffset(DateTimeOffset value) =>
        new((value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);

    /// <summary>Converts fractional Unix seconds to microseconds, truncating sub-microsecond precision.</summary>
    public static InstantUs FromUnixSeconds(decimal seconds) =>
        new(decimal.ToInt64(decimal.Truncate(seconds * 1_000_000m)));

    /// <summary>Converts this instant to a UTC platform timestamp.</summary>
    public DateTimeOffset ToDateTimeOffset() =>
        new(DateTimeOffset.UnixEpoch.UtcTicks + checked(Value * 10), TimeSpan.Zero);

    /// <inheritdoc />
    public int CompareTo(InstantUs other) => Value.CompareTo(other.Value);
    /// <summary>Tests whether the left instant precedes the right instant.</summary>
    public static bool operator <(InstantUs left, InstantUs right) => left.Value < right.Value;
    /// <summary>Tests whether the left instant does not follow the right instant.</summary>
    public static bool operator <=(InstantUs left, InstantUs right) => left.Value <= right.Value;
    /// <summary>Tests whether the left instant follows the right instant.</summary>
    public static bool operator >(InstantUs left, InstantUs right) => left.Value > right.Value;
    /// <summary>Tests whether the left instant does not precede the right instant.</summary>
    public static bool operator >=(InstantUs left, InstantUs right) => left.Value >= right.Value;
    /// <summary>Offsets an instant by a duration.</summary>
    public static InstantUs operator +(InstantUs value, DurationUs duration) => new(checked(value.Value + duration.Value));
    /// <summary>Offsets an instant backwards by a duration.</summary>
    public static InstantUs operator -(InstantUs value, DurationUs duration) => new(checked(value.Value - duration.Value));
    /// <summary>Measures the signed duration between two instants.</summary>
    public static DurationUs operator -(InstantUs left, InstantUs right) => new(checked(left.Value - right.Value));
    /// <summary>Formats the instant as an invariant ISO-8601 UTC timestamp.</summary>
    public override string ToString() => ToDateTimeOffset().ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>Represents a signed duration measured in microseconds.</summary>
public readonly record struct DurationUs(long Value)
{
    /// <summary>Creates a duration from milliseconds.</summary>
    public static DurationUs FromMilliseconds(double value) => new(checked((long)Math.Round(value * 1_000d)));
    /// <summary>Creates a duration from seconds.</summary>
    public static DurationUs FromSeconds(double value) => new(checked((long)Math.Round(value * 1_000_000d)));
    /// <summary>Converts the duration to a platform time span.</summary>
    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(checked(Value * 10));
}

/// <summary>Represents a validated half-open time interval.</summary>
public readonly record struct TimeRange
{
    /// <summary>Creates a range whose start is included and end is excluded.</summary>
    public TimeRange(InstantUs startInclusive, InstantUs endExclusive)
    {
        if (endExclusive.Value < startInclusive.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "A half-open range cannot end before it starts.");
        }

        StartInclusive = startInclusive;
        EndExclusive = endExclusive;
    }

    /// <summary>Gets the included lower boundary.</summary>
    public InstantUs StartInclusive { get; }
    /// <summary>Gets the excluded upper boundary.</summary>
    public InstantUs EndExclusive { get; }
    /// <summary>Gets the range duration in microseconds.</summary>
    public long DurationUs => EndExclusive.Value - StartInclusive.Value;
    /// <summary>Gets whether both boundaries are equal.</summary>
    public bool IsEmpty => StartInclusive == EndExclusive;
    /// <summary>Tests whether an instant falls inside the half-open range.</summary>
    public bool Contains(InstantUs instant) =>
        instant.Value >= StartInclusive.Value && instant.Value < EndExclusive.Value;
    /// <summary>Tests whether this range and another range share any instant.</summary>
    public bool Overlaps(TimeRange other) =>
        StartInclusive.Value < other.EndExclusive.Value && other.StartInclusive.Value < EndExclusive.Value;

    /// <summary>Returns the intersection, using an empty range when the inputs do not overlap.</summary>
    public TimeRange Intersect(TimeRange other) =>
        new(
            new InstantUs(Math.Max(StartInclusive.Value, other.StartInclusive.Value)),
            new InstantUs(Math.Max(
                Math.Max(StartInclusive.Value, other.StartInclusive.Value),
                Math.Min(EndExclusive.Value, other.EndExclusive.Value))));
}

/// <summary>Represents a strictly positive aggregation bucket width.</summary>
public readonly record struct BucketWidth
{
    /// <summary>Creates a bucket width in microseconds.</summary>
    public BucketWidth(long microseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(microseconds);

        Microseconds = microseconds;
    }

    /// <summary>Gets the width in microseconds.</summary>
    public long Microseconds { get; }
}

/// <summary>Defines the origin used to align fixed-width time buckets.</summary>
public readonly record struct BucketAlignment(long OriginUs)
{
    /// <summary>Gets an alignment anchored at the Unix epoch.</summary>
    public static BucketAlignment UnixEpoch => new(0);

    /// <summary>Returns the aligned bucket containing an instant.</summary>
    public TimeRange RangeContaining(InstantUs instant, BucketWidth width)
    {
        var relative = checked(instant.Value - OriginUs);
        var bucket = FloorDiv(relative, width.Microseconds);
        var start = checked(OriginUs + bucket * width.Microseconds);
        return new TimeRange(new InstantUs(start), new InstantUs(checked(start + width.Microseconds)));
    }

    /// <summary>Divides integers while rounding the quotient toward negative infinity.</summary>
    public static long FloorDiv(long dividend, long divisor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisor);

        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}

/// <summary>Maps a time range onto a positive number of device-pixel columns.</summary>
public readonly record struct Viewport
{
    /// <summary>Creates a viewport over a time range.</summary>
    public Viewport(TimeRange range, int devicePixelWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(devicePixelWidth);

        Range = range;
        DevicePixelWidth = devicePixelWidth;
    }

    /// <summary>Gets the visible time range.</summary>
    public TimeRange Range { get; }
    /// <summary>Gets the number of output columns.</summary>
    public int DevicePixelWidth { get; }

    /// <summary>Returns one of the width-plus-one exact column boundaries.</summary>
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
