using System.Text.RegularExpressions;
using VisualCat.Core.Query;
using VisualCat.Domain.Filters;

namespace VisualCat.App.Views;

/// <summary>A half-open [Start, Start+Length) run of a message that the active search matched.</summary>
public readonly record struct HighlightSpan(int Start, int Length);

/// <summary>
/// Locates the active search term inside a rendered message so the entry table can mark it
/// instead of leaving the user to find it by eye.
///
/// This runs per realized row of a virtualized list, so it is bounded twice: only the
/// leading <see cref="MaximumScannedCharacters"/> characters are scanned — the cell shows a
/// single ellipsized line, so a match past that is not on screen to mark — and at most
/// <see cref="MaximumSpans"/> runs are produced, which caps the inline count of one row
/// however repetitive its text is.
///
/// Regex searches reuse <see cref="SessionQueryEngine.CompileSearchRegex"/>: the highlight
/// predicate is then the same compiled instance the query used to select the row, so
/// "marked" and "matched" cannot disagree, and no row ever pays for a compile. That
/// instance carries the search timeout, so a pattern too slow to mark degrades to plain
/// text rather than to a stalled frame.
/// </summary>
public static class EntryHighlight
{
    /// <summary>Leading characters of a message considered for marking.</summary>
    public const int MaximumScannedCharacters = 512;

    /// <summary>Upper bound on marked runs in one message.</summary>
    public const int MaximumSpans = 24;

    private static readonly HighlightSpan[] None = [];

    /// <summary>
    /// Runs of <paramref name="text"/> matched by <paramref name="search"/>, in ascending
    /// order and never overlapping. Empty whenever there is nothing to mark, which is the
    /// common case and allocates nothing.
    /// </summary>
    public static IReadOnlyList<HighlightSpan> Match(string? text, TextSearchSpec? search)
    {
        if (string.IsNullOrEmpty(text) ||
            search is null ||
            string.IsNullOrEmpty(search.Query))
        {
            return None;
        }

        var window = Math.Min(text.Length, MaximumScannedCharacters);
        return search.IsRegex
            ? RegexSpans(text, window, search)
            : LiteralSpans(text, window, search.Query, search.CaseSensitive);
    }

    /// <summary>
    /// Ordinal comparison, matching the engine's own row predicate. Ordinal case folding is
    /// length-preserving, so a match is exactly as long as the query — which is what lets
    /// the scan advance without re-measuring the subject.
    /// </summary>
    private static IReadOnlyList<HighlightSpan> LiteralSpans(
        string text,
        int window,
        string query,
        bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        List<HighlightSpan>? spans = null;
        var cursor = 0;
        while (cursor < window && (spans?.Count ?? 0) < MaximumSpans)
        {
            var index = text.IndexOf(query, cursor, window - cursor, comparison);
            if (index < 0)
            {
                break;
            }

            spans ??= new List<HighlightSpan>(4);
            spans.Add(new HighlightSpan(index, Math.Min(query.Length, window - index)));
            cursor = index + query.Length;
        }

        return (IReadOnlyList<HighlightSpan>?)spans ?? None;
    }

    private static IReadOnlyList<HighlightSpan> RegexSpans(string text, int window, TextSearchSpec search)
    {
        Regex regex;
        try
        {
            regex = SessionQueryEngine.CompileSearchRegex(search);
        }
        catch (ArgumentException)
        {
            // An unparseable pattern selects no rows in the first place; the search surface
            // reports that failure, and the table just renders the message unmarked.
            return None;
        }

        List<HighlightSpan>? spans = null;
        try
        {
            // Enumerating over the span both bounds the scan and keeps the walk
            // allocation-free; zero-width matches advance the enumerator on their own.
            foreach (var match in regex.EnumerateMatches(text.AsSpan(0, window)))
            {
                if (match.Length <= 0)
                {
                    continue;
                }

                spans ??= new List<HighlightSpan>(4);
                spans.Add(new HighlightSpan(match.Index, match.Length));
                if (spans.Count >= MaximumSpans)
                {
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // The row was already selected by a completed query. Marking it is best effort,
            // so a pattern too slow on this one message keeps whatever it had found.
        }

        return (IReadOnlyList<HighlightSpan>?)spans ?? None;
    }
}
