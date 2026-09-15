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
        var local = HostZoneId();
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

    /// <summary>
    /// The zone this host is configured for, preferring the identifier the user actually chose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// macOS does not store zoneinfo as symlinked aliases the way Linux does: every zone is its
    /// own regular file, and neighbouring countries that share a rule set are byte-identical
    /// copies. .NET's Unix <see cref="TimeZoneInfo.Local"/> reads <c>/etc/localtime</c>'s
    /// <em>contents</em> and finds a matching id by scanning the zoneinfo tree — it can only take
    /// the id from the link path when the target lies under the default zoneinfo directory it was
    /// compiled with, and on macOS <c>/etc/localtime</c> points into <c>/var/db/timezone/zoneinfo</c>
    /// while that default is <c>/usr/share/zoneinfo</c>. The prefix comparison misses, the content
    /// scan returns the first alphabetical match, and a Mac set to <c>Europe/Prague</c> writes
    /// <c>Europe/Bratislava</c> into every session it creates (finding F-07).
    /// </para>
    /// <para>
    /// It is not an exotic pairing. <c>Europe/Oslo</c> wins over <c>Europe/Stockholm</c> and
    /// <c>Europe/Copenhagen</c>, <c>America/Toronto</c> over <c>America/Nassau</c>,
    /// <c>Asia/Kuala_Lumpur</c> over <c>Asia/Singapore</c>. The instants are right, because the
    /// rules are identical; the metadata names a country the user did not choose, and the same
    /// log indexed on three platforms produces three different manifests, which is what breaks
    /// the cross-platform parity assertions.
    /// </para>
    /// <para>
    /// The link target carries the identifier the user picked, so it is preferred when it names a
    /// zone the runtime can actually resolve — that last check is what keeps a path that is not a
    /// zone from ever reaching a manifest.
    /// </para>
    /// </remarks>
    public static string HostZoneId()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                var target = File.ResolveLinkTarget("/etc/localtime", returnFinalTarget: true)?.FullName;
                const string Marker = "/zoneinfo/";
                var index = target?.IndexOf(Marker, StringComparison.Ordinal) ?? -1;
                if (index >= 0 && target is not null)
                {
                    var id = target[(index + Marker.Length)..];
                    if (id.Length > 0 && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
                    {
                        return id;
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Nothing about the host's configuration is worth failing a capture over; the
                // runtime's own answer is still a correct zone, just occasionally a synonym.
            }
        }

        return TimeZoneInfo.Local.Id;
    }

    private static string Explain(string timeZoneId, Exception? cause) =>
        $"Time zone '{timeZoneId}' could not be resolved" +
        (cause is null ? ", so the runtime answered it with UTC" : string.Empty) +
        ". The system time-zone database (tzdata, /usr/share/zoneinfo) appears to be missing or " +
        "unreadable. Install it, or state the zone explicitly with --timezone UTC if UTC is what " +
        "you meant: timestamps in a logcat file without an offset are interpreted in this zone, " +
        "so guessing it would move every instant in the session.";
}
