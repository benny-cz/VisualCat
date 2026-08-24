using System.Globalization;

namespace VisualCat.Core.Parsing;

/// <summary>
/// Tracks a loss-averse, monotonic <c>logcat -T</c> resume point across merged Android buffers.
/// </summary>
/// <remarks>
/// <c>logcat -b all</c> can deliver adjacent records a few milliseconds out of timestamp order.
/// Using the last record therefore moves a reconnect cursor backward and can replay a large
/// ring-buffer suffix repeatedly under backpressure. This keeps the greatest genuine record
/// timestamp and applies a one-second overlap: small ordering differences cannot move it
/// backward, while the overlap prefers bounded duplicate boundary records over loss.
///
/// A real wall-clock rollback is different. When the previous high-water mark is now more than
/// five seconds in the device's future, the cursor starts a new clock epoch instead of waiting
/// for wall time to catch up. The caller supplies the same device wall clock represented by the
/// UTC log format, which also makes that rare branch deterministic in tests.
/// </remarks>
public sealed class LogcatResumeCursor
{
    private static readonly TimeSpan ResumeOverlap = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromSeconds(5);
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.ffffff";

    private DateTime? _highWaterUtc;

    /// <summary>
    /// The inclusive timezone-independent Unix-epoch argument for the fixed <c>logcat -T</c>
    /// command.
    /// </summary>
    /// <remarks>
    /// Android parses a zone-less wall-clock <c>-T</c> value in the device's local timezone,
    /// even when logcat output uses the <c>UTC</c> presentation modifier. Passing UTC text on a
    /// non-UTC device can therefore replay hours of the ring buffer on every reconnect. The
    /// numeric seconds form is explicitly supported by logcat and has no timezone ambiguity.
    /// </remarks>
    public string? ResumeArgument => _highWaterUtc is { } value
        ? FormatEpoch(value - ResumeOverlap)
        : null;

    /// <summary>The same inclusive resume point in human-readable UTC for diagnostics.</summary>
    public string? ResumeUtcTimestamp => _highWaterUtc is { } value
        ? (value - ResumeOverlap).ToString(TimestampFormat, CultureInfo.InvariantCulture)
        : null;

    /// <summary>
    /// Observes one complete output line and advances the cursor only when it is a genuine
    /// year/usec threadtime record.
    /// </summary>
    /// <param name="record">A complete line or its documented 96-character prefix.</param>
    /// <param name="deviceUtcNow">The device wall clock corresponding to the stream's UTC format.</param>
    /// <param name="clockEpochReset">True when a real wall-clock rollback began a new epoch.</param>
    /// <returns>True when <paramref name="record"/> was a valid record considered by the cursor.</returns>
    public bool Observe(
        ReadOnlySpan<char> record,
        DateTime deviceUtcNow,
        out bool clockEpochReset)
    {
        clockEpochReset = false;
        if (record.Length < TimestampFormat.Length ||
            !LogcatRecordOrigin.TryReadProcessId(record, out _) ||
            !DateTime.TryParseExact(
                record[..TimestampFormat.Length],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var recordUtc))
        {
            return false;
        }

        var utcNow = deviceUtcNow.Kind == DateTimeKind.Utc
            ? deviceUtcNow
            : deviceUtcNow.ToUniversalTime();
        if (_highWaterUtc is { } previous && previous - utcNow > ClockRollbackTolerance)
        {
            _highWaterUtc = null;
            clockEpochReset = true;
        }

        if (_highWaterUtc is null || recordUtc > _highWaterUtc)
        {
            _highWaterUtc = recordUtc;
        }

        return true;
    }

    private static string FormatEpoch(DateTime utc)
    {
        var ticksSinceEpoch = utc.Ticks - DateTime.UnixEpoch.Ticks;
        var seconds = Math.DivRem(ticksSinceEpoch, TimeSpan.TicksPerSecond, out var remainingTicks);
        var microseconds = remainingTicks / 10;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{seconds}.{microseconds:D6}");
    }
}
