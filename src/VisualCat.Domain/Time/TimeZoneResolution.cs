namespace VisualCat.Domain.Time;

/// <summary>
/// The system time-zone database could not answer a question the session's timestamps depend on.
/// </summary>
public sealed class TimeZoneUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>
/// Resolves zone identifiers, and refuses to guess when the system cannot answer.
/// </summary>
/// <remarks>
/// <para>
/// A logcat file in <c>threadtime</c> without a year or an offset carries no zone at all, so the
/// zone <em>is</em> the interpretation of every timestamp in it. On a system with no
/// <c>tzdata</c> — a container, a minimal image, a CI runner — .NET answers
/// <see cref="TimeZoneInfo.Local"/> with UTC and drops a <c>TZ</c> request without a word, so a
/// user who asked for <c>Europe/Prague</c> got UTC and a session that looks entirely healthy with
/// every instant off by the offset (finding F-26).
/// </para>
/// <para>
/// Reporting it is the whole fix. A caller that genuinely wants UTC can ask for it by name, and
/// then nothing here objects.
/// </para>
/// </remarks>
public static class TimeZoneResolution
{
    /// <summary>
    /// The zone for an identifier, or a <see cref="TimeZoneUnavailableException"/> naming the
    /// identifier, the likely cause and what to do about it.
    /// </summary>
    public static TimeZoneInfo Resolve(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new TimeZoneUnavailableException(Explain(timeZoneId, exception), exception);
        }
    }

    /// <summary>
    /// The local zone identifier, having checked that the system could honour a <c>TZ</c> request.
    /// </summary>
    /// <exception cref="TimeZoneUnavailableException">
    /// <c>TZ</c> names a zone the runtime could not resolve, so the timestamps in any file
    /// without an explicit offset would silently be interpreted in the wrong zone.
    /// </exception>
    public static string LocalId()
    {
        var local = TimeZoneInfo.Local.Id;
        var requested = Environment.GetEnvironmentVariable("TZ");
        if (string.IsNullOrWhiteSpace(requested))
        {
            return local;
        }

        // A leading colon is allowed by POSIX and names a file or a zone; either way the zone
        // name is what follows it.
        requested = requested.TrimStart(':');
        if (requested.Length == 0 ||
            string.Equals(requested, local, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requested, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return local;
        }

        // The runtime resolved TZ to something, so it was honoured even if the spelling differs.
        if (!string.Equals(local, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return local;
        }

        throw new TimeZoneUnavailableException(Explain(requested, null));
    }

    private static string Explain(string timeZoneId, Exception? cause) =>
        $"Time zone '{timeZoneId}' could not be resolved" +
        (cause is null ? " — the request was silently answered with UTC" : string.Empty) +
        ". The system time-zone database (tzdata, /usr/share/zoneinfo) appears to be missing or " +
        "unreadable. Install it, or state the zone explicitly with --timezone UTC if UTC is what " +
        "you meant: timestamps in a logcat file without an offset are interpreted in this zone, " +
        "so guessing it would move every instant in the session.";
}
