namespace VisualCat.App.Views;

/// <summary>
/// How a session's name is shortened when the strip cannot show all of it.
/// </summary>
internal static class TabTitle
{
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
