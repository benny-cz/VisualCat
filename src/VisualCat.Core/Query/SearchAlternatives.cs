using VisualCat.Core.Store;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;

namespace VisualCat.Core.Query;

/// <summary>
/// A facet the reader's search text names exactly, when searching the message text found little.
/// </summary>
/// <param name="Dimension">Which facet the text matched — a tag, or a process name.</param>
/// <param name="Value">The facet value, as it is spelled in the session.</param>
/// <param name="Count">How many entries carry it under the current filter.</param>
public sealed record SearchAlternative(FacetQueryDimension Dimension, string Value, long Count)
{
    /// <summary>The facet's name in a sentence — "tag" or "process".</summary>
    public string DimensionLabel => Dimension == FacetQueryDimension.Process ? "process" : "tag";

    /// <summary>The command-line option that would search it.</summary>
    public string CommandLineOption => Dimension == FacetQueryDimension.Process ? "--processes" : "--tags";
}

/// <summary>
/// Answers "you searched for something that is not in any message, but it <em>is</em> a tag".
/// </summary>
/// <remarks>
/// <para>
/// Text search matches a record's message, not its tag — which is defensible, and was silent.
/// Searching a real capture for <c>VCATTEST</c> returned three matches, the <c>adbd</c> lines
/// that happen to quote the tag in their own text, while the 301 records actually tagged
/// <c>VCATTEST</c> were not matched and nothing said so. The reader's reasonable conclusion is
/// that the product cannot find their own log lines (finding F-12).
/// </para>
/// <para>
/// The condition is deliberately narrow: the query has to match a facet value <em>exactly</em>,
/// and the text search has to have found little or nothing. A fuzzy "did you mean" on every
/// search would be noise, and a hint offered beside a thousand good matches would be worse than
/// none.
/// </para>
/// </remarks>
public static class SearchAlternatives
{
    /// <summary>
    /// The most numerous facet whose value equals <paramref name="query"/> exactly, or null.
    /// </summary>
    /// <param name="snapshot">The session being searched.</param>
    /// <param name="filter">The filter the search ran under, so the count is the one on screen.</param>
    /// <param name="query">The reader's search text.</param>
    /// <param name="textMatches">How many records the text search matched.</param>
    /// <param name="isRegex">
    /// Whether the query was a regular expression. A pattern that happens to equal a tag is a
    /// coincidence, not an intention, so no hint is offered for one.
    /// </param>
    /// <param name="cancellationToken">Cancels the facet tallies.</param>
    public static SearchAlternative? Find(
        SessionSnapshot snapshot,
        FilterSpec filter,
        string? query,
        long textMatches,
        bool isRegex = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(filter);

        // Offered only when the search all but failed. Three stray matches out of 301 is the
        // shape that prompted this; three hundred good ones is not.
        if (isRegex || textMatches > MatchesWorthOverriding || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return null;
        }

        SearchAlternative? best = null;
        foreach (var dimension in new[] { FacetQueryDimension.Tag, FacetQueryDimension.Process })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Match(snapshot, filter, dimension, needle, cancellationToken);
            if (candidate is not null && (best is null || candidate.Count > best.Count))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// The most text matches a search may have and still be offered a facet instead.
    /// </summary>
    /// <remarks>
    /// Above a handful the reader has results to read and a hint is an interruption. The live
    /// case was three incidental matches against 301 tagged records.
    /// </remarks>
    private const long MatchesWorthOverriding = 5;

    private static SearchAlternative? Match(
        SessionSnapshot snapshot,
        FilterSpec filter,
        FacetQueryDimension dimension,
        string needle,
        CancellationToken cancellationToken)
    {
        // The facet page is already filtered by text and ordered by count, so one page of it is
        // enough to find an exact spelling without tallying the whole dimension twice.
        var page = SessionQueryEngine.QueryFacetValues(
            snapshot,
            filter,
            dimension,
            needle,
            cursor: null,
            limit: 20,
            queryGeneration: 0,
            cancellationToken);

        foreach (var value in page.NeutralValues.Concat(page.ActiveValues))
        {
            if (value.Key.Text is { Length: > 0 } text &&
                string.Equals(text, needle, StringComparison.OrdinalIgnoreCase) &&
                value.Count > 0)
            {
                return new SearchAlternative(dimension, text, value.Count);
            }
        }

        return null;
    }

    /// <summary>The whole hint as one sentence, for a status line or a command's output.</summary>
    public static string Describe(SearchAlternative alternative, string query, bool forCommandLine)
    {
        ArgumentNullException.ThrowIfNull(alternative);
        var opening = $"No message text matched \"{query}\". " +
                      $"{alternative.Count:N0} {(alternative.Count == 1 ? "entry carries" : "entries carry")} " +
                      $"that {alternative.DimensionLabel}";
        return forCommandLine
            ? $"{opening} — search it with {alternative.CommandLineOption} {alternative.Value}."
            : $"{opening}.";
    }
}
