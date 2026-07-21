using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Parsing;

public sealed record ResolvedTimestamp(
    InstantUs? Instant,
    TimestampProvenance Provenance,
    double Confidence,
    EntryAttributes Attributes);

public sealed class TimestampResolver
{
    private readonly TimestampPolicy _policy;
    private readonly TimeZoneInfo _zone;
    private int? _inferredYear;
    private int? _previousMonth;
    private InstantUs? _highestSeen;
    private DateTime _cachedDay = DateTime.MinValue;
    private TimeSpan _cachedDayOffset;
    private bool _cachedDayIsUniform;

    public TimestampResolver(TimestampPolicy policy)
    {
        _policy = policy;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
    }

    public ResolvedTimestamp Resolve(TimestampToken? token, InstantUs? arrival = null)
    {
        if (token is null)
        {
            if (_policy.UseArrivalTimeForUntimed && arrival is not null)
            {
                return WithOrdering(arrival, TimestampProvenance.ArrivalTime, 0.75, EntryAttributes.InferredTimestamp);
            }

            return new ResolvedTimestamp(null, TimestampProvenance.Missing, 0, EntryAttributes.None);
        }

        if (token.EpochMicroseconds is { } epoch)
        {
            return WithOrdering(new InstantUs(epoch), TimestampProvenance.Epoch, 1, EntryAttributes.None);
        }

        var attributes = EntryAttributes.None;
        var confidence = 1d;
        var year = token.Year;
        if (year is null)
        {
            attributes |= EntryAttributes.InferredTimestamp;
            confidence = 0.85;
            if (_inferredYear is null)
            {
                _inferredYear = _policy.AssumedYear ?? InferInitialYear(token);
            }
            else if (_previousMonth is { } previousMonth &&
                     previousMonth - token.Month >= _policy.RolloverBackwardMonthThreshold)
            {
                _inferredYear++;
            }

            year = _inferredYear;
            _previousMonth = token.Month;
        }

        DateTime local;
        try
        {
            local = new DateTime(year.Value, token.Month, token.Day, token.Hour, token.Minute, token.Second, DateTimeKind.Unspecified)
                .AddTicks(token.Microsecond * 10L);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new ResolvedTimestamp(null, TimestampProvenance.Missing, 0, EntryAttributes.LowTimestampConfidence);
        }

        DateTimeOffset instant;
        TimestampProvenance provenance;
        if (token.IsUtc)
        {
            instant = new DateTimeOffset(local, TimeSpan.Zero);
            provenance = TimestampProvenance.ExplicitUtc;
        }
        else if (token.ExplicitOffset is { } explicitOffset)
        {
            instant = new DateTimeOffset(local, explicitOffset);
            provenance = TimestampProvenance.ExplicitOffset;
        }
        else if (TryUniformDayOffset(local, out var uniformOffset))
        {
            // Fast path: the local day contains no UTC-offset transition, so no time in
            // it can be invalid or ambiguous and the offset is already known. Consulting
            // TimeZoneInfo three times per entry dominated timestamp resolution.
            instant = new DateTimeOffset(local, uniformOffset);
            provenance = token.Year is null ? TimestampProvenance.InferredYearAndZone : TimestampProvenance.InferredZone;
        }
        else
        {
            if (_zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
                confidence = Math.Min(confidence, 0.5);
                attributes |= EntryAttributes.LowTimestampConfidence;
            }

            var offset = ResolveOffset(local);
            instant = new DateTimeOffset(local, offset);
            provenance = token.Year is null ? TimestampProvenance.InferredYearAndZone : TimestampProvenance.InferredZone;
            if (_zone.IsAmbiguousTime(local))
            {
                confidence = Math.Min(confidence, 0.7);
                attributes |= EntryAttributes.LowTimestampConfidence;
            }
        }

        return WithOrdering(InstantUs.FromDateTimeOffset(instant), provenance, confidence, attributes);
    }

    private int InferInitialYear(TimestampToken token)
    {
        var referenceInZone = TimeZoneInfo.ConvertTime(_policy.ReferenceInstant, _zone);
        var year = referenceInZone.Year;
        try
        {
            var candidate = new DateTimeOffset(
                year,
                token.Month,
                token.Day,
                token.Hour,
                token.Minute,
                token.Second,
                ResolveOffset(new DateTime(year, token.Month, token.Day, token.Hour, token.Minute, token.Second)));
            if (candidate > _policy.ReferenceInstant.AddDays(2))
            {
                year--;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // The actual resolver reports the invalid calendar value as low confidence.
        }

        return year;
    }

    /// <summary>
    /// Reports the single UTC offset that applies for the whole local day containing
    /// <paramref name="local"/>, or false when that day carries a transition. Logs
    /// overwhelmingly cover one or two days, so this collapses per-entry zone lookups
    /// into two per distinct day while staying exact: a day with no offset change has
    /// no invalid or ambiguous local times to classify.
    /// </summary>
    private bool TryUniformDayOffset(DateTime local, out TimeSpan offset)
    {
        var day = local.Date;
        if (day == _cachedDay)
        {
            offset = _cachedDayOffset;
            return _cachedDayIsUniform;
        }

        var startOffset = _zone.GetUtcOffset(day);
        var endOffset = _zone.GetUtcOffset(day.AddDays(1).AddTicks(-1));
        _cachedDay = day;
        _cachedDayOffset = startOffset;
        _cachedDayIsUniform = startOffset == endOffset;
        offset = startOffset;
        return _cachedDayIsUniform;
    }

    private TimeSpan ResolveOffset(DateTime local)
    {
        if (!_zone.IsAmbiguousTime(local))
        {
            return _zone.GetUtcOffset(local);
        }

        var offsets = _zone.GetAmbiguousTimeOffsets(local);
        return _policy.PreferEarlierAmbiguousOffset ? offsets.Max() : offsets.Min();
    }

    private ResolvedTimestamp WithOrdering(
        InstantUs? instant,
        TimestampProvenance provenance,
        double confidence,
        EntryAttributes attributes)
    {
        if (instant is { } value)
        {
            if (_highestSeen is { } highest && value < highest)
            {
                attributes |= EntryAttributes.OutOfOrder;
            }
            else
            {
                _highestSeen = value;
            }
        }

        return new ResolvedTimestamp(instant, provenance, confidence, attributes);
    }
}
