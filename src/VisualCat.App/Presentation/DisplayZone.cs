using VisualCat.Domain.Sessions;

namespace VisualCat.App.Presentation;

/// <summary>
/// The one rule for which time zone a session's instants are shown in.
/// </summary>
/// <remarks>
/// <para>
/// The workspace and the export review each had their own answer, and the two disagreed: the
/// plot axis was labelled <c>Europe/Prague</c> and its header read <c>00:06:09.000</c>, while
/// the review opened from it stated <c>22:06:09.464 … UTC</c> for the same instants. Both were
/// internally consistent — an ADB capture negotiates UTC, and the plot presents in the host zone
/// — and a reader checking the export against what they could see had to do the arithmetic to
/// discover the two agreed (finding F-16).
/// </para>
/// <para>
/// The rule itself is unchanged: a capture taken over ADB or on-device carries device-clock
/// timestamps and reads naturally in the reader's own zone, while an imported file's naive
/// timestamps mean whatever its policy zone says they mean. What changes is that there is now
/// one place that says so.
/// </para>
/// </remarks>
public static class DisplayZone
{
    /// <summary>The zone identifier a session's instants are presented in.</summary>
    public static string IdFor(SessionDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return TimeZoneInfo.Utc.Id;
        }

        return descriptor.SourceKind is SourceKind.Adb or SourceKind.Android
            ? TimeZoneInfo.Local.Id
            : descriptor.TimestampPolicy.TimeZoneId;
    }

    /// <summary>That identifier as a zone, falling back to UTC where the system cannot name it.</summary>
    /// <remarks>
    /// Presentation only. A zone the system cannot resolve is reported rather than guessed where
    /// it decides what a timestamp <em>means</em> — see <c>TimeZoneResolution</c> — but a display
    /// that cannot be drawn at all is worse than one drawn in UTC and labelled UTC.
    /// </remarks>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The zone a session is presented in, resolved.</summary>
    public static TimeZoneInfo For(SessionDescriptor? descriptor) => Resolve(IdFor(descriptor));
}
