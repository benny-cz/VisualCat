using System.Globalization;

namespace VisualCat.Domain;

/// <summary>
/// Counted nouns, in one place, so the noun always agrees with the number.
/// </summary>
/// <remarks>
/// Every status site interpolated its own noun — <c>$"{n:N0} entries"</c> — so a capture that
/// finished with exactly one record confirmed itself as <c>Stopped · 1 entries kept</c>. Four
/// of thirty short captures in one sweep landed there, and it is ordinary quiet captures that
/// reach it, so the final word a reader gets on their capture looks unfinished (finding F-21).
/// Thousands separators follow the display culture for the same reason they always did.
/// </remarks>
public static class Counted
{
    /// <summary>"1 entry", "12,345 entries".</summary>
    public static string Entries(long count) => Of(count, "entry", "entries");

    /// <summary>"no entries", "1 entry", "12,345 entries".</summary>
    public static string EntriesOrNone(long count) => OrNone(count, "entry", "entries");

    /// <summary>"1 session", "46 sessions".</summary>
    public static string Sessions(long count) => Of(count, "session", "sessions");

    public static string Of(long count, string singular, string plural) =>
        count == 1
            ? $"1 {singular}"
            : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {plural}";

    public static string OrNone(long count, string singular, string plural) =>
        count == 0 ? $"no {plural}" : Of(count, singular, plural);
}
