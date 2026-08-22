namespace VisualCat.App.Views;

/// <summary>
/// How a session's name is shortened when the strip cannot show all of it.
/// </summary>
internal static class TabTitle
{
    /// <summary>How many characters a phone tab shows before a name is shortened.</summary>
    /// <remarks>
    /// It was 24. <c>On-device logcat HHhMMmSS</c> — the name this product gives every capture
    /// it makes — is 25, so every on-device capture was middle-truncated, always, and what the
    /// ellipsis ate was the word that says what the session is:
    /// <c>On-device log…t 03h45m47</c>. That is the second half of finding F-16, still true
    /// after the first half was fixed, and one character of budget was the whole difference
    /// (third device pass). The budget is that name's length plus one, so the product's own
    /// default fits whole and a genuinely long imported filename still shortens.
    /// </remarks>
    internal const int MobileBudget = 26;

    /// <summary>How many characters a desktop tab shows before a name is shortened.</summary>
    internal const int DesktopBudget = 34;

    /// <summary>
    /// Keeps both ends of a name, dropping the middle.
    /// </summary>
    /// <remarks>
    /// End-ellipsis turned <c>northlight-transit-20260812.txt</c> into
    /// <c>northlight-transit-20…</c> — cutting off the date, which is the only part that
    /// distinguishes two captures of the same app on the same device (finding 28). The head
    /// keeps a little more than the tail because a name is usually recognised from its start
    /// and disambiguated by its end.
    /// </remarks>
    internal static string Shorten(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 4);
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var keep = maximumLength - 1;
        var head = (int)Math.Ceiling(keep * 0.55);
        var tail = keep - head;
        return string.Concat(value.AsSpan(0, head), "…", value.AsSpan(value.Length - tail, tail));
    }
}
