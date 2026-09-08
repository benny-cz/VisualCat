using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Queries;

/// <summary>Chooses chronological or byte-faithful source ordering for entries.</summary>
public enum EntryOrder : byte
{
    /// <summary>Order by normalized timestamp, then stable source identity.</summary>
    Chronological,
    /// <summary>Order by original source sequence.</summary>
    SourceSequence,
}

/// <summary>Identifies the exact session, snapshot, filter, and request that produced a result.</summary>
public sealed record QueryIdentity(
    Guid SessionId,
    long SnapshotGeneration,
    string FilterFingerprint,
    long QueryGeneration);

/// <summary>Contains the count for one severity-by-time aggregation cell.</summary>
public readonly record struct AggregateCell(TimeRange Range, LogLevel Level, long Count);

/// <summary>Contains a complete severity-by-time heat-map query result.</summary>
public sealed record HeatMapResult(
    QueryIdentity Identity,
    Viewport Viewport,
    IReadOnlyList<TimeRange> Columns,
    IReadOnlyDictionary<LogLevel, long[]> Counts,
    long MaximumCount,
    bool HasUnknown);

/// <summary>Pairs a facet value with its matching entry count.</summary>
public sealed record FacetValue<T>(T Value, long Count);

/// <summary>Contains aggregate counts and leading facets for a filter.</summary>
public sealed record StatisticsResult(
    QueryIdentity Identity,
    long TotalMatching,
    long TimedMatching,
    long UntimedMatching,
    InstantUs? FirstInstant,
    InstantUs? LastInstant,
    IReadOnlyDictionary<LogLevel, long> Levels,
    IReadOnlyList<FacetValue<string>> Tags,
    IReadOnlyList<FacetValue<int>> Pids,
    IReadOnlyList<FacetValue<int>> Tids,
    IReadOnlyList<FacetValue<string>> Buffers,
    IReadOnlyList<FacetValue<uint>> Templates,
    IReadOnlyList<FacetValue<string>>? Processes = null);

/// <summary>Identifies the stable key after which the next entry page starts.</summary>
public sealed record EntryCursor(EntryOrder Order, long TimestampUs, long Sequence);

/// <summary>Contains one stable page of normalized entries.</summary>
public sealed record EntryPage(
    QueryIdentity Identity,
    IReadOnlyList<NormalizedEntry> Entries,
    EntryCursor? NextCursor,
    long? TotalCount);

/// <summary>A bounded entry page beginning at an exact key, with rows before it disclosed.</summary>
public sealed record EntryArrivalPage(EntryPage Page, long CountBeforeWindow);

/// <summary>Summarizes one mined message template within a query.</summary>
public sealed record TemplateSummary(
    uint TemplateId,
    string CanonicalText,
    long Count,
    InstantUs? First,
    InstantUs? Last,
    IReadOnlyList<long> RepresentativeEntryIds);

/// <summary>Reports bounded search progress for a query generation.</summary>
public sealed record SearchProgress(
    QueryIdentity Identity,
    long RecordsScanned,
    long Matches,
    bool Completed,
    double Progress);

/// <summary>Contains the final match count and timeline markers for a search.</summary>
public sealed record SearchResult(
    QueryIdentity Identity,
    long Matches,
    IReadOnlyList<InstantUs> Markers,
    bool MarkersTruncated);

/// <summary>Identifies one exact timed search match across snapshot generations.</summary>
public readonly record struct SearchMatchKey(Guid SessionId, long TimestampUs, long SourceSequence);

/// <summary>Chooses how an exact search match is resolved.</summary>
public enum SearchMatchRequestKind : byte
{
    First,
    Last,
    Next,
    Previous,
    Nearest,
    Ordinal,
    Revalidate,
}

/// <summary>Describes one exact, full-session search-navigation lookup.</summary>
public sealed record SearchMatchRequest(
    SearchMatchRequestKind Kind,
    SearchMatchKey? From = null,
    InstantUs? Near = null,
    long? Ordinal = null);

/// <summary>Describes whether an exact search-navigation request found a record.</summary>
public enum SearchMatchStatus : byte
{
    Found,
    NoMatches,
    OutOfRange,
    NoLongerMatches,
}

/// <summary>Contains an exact search record and its one-based rank among all matches.</summary>
public sealed record SearchMatchResult(
    QueryIdentity Identity,
    SearchMatchStatus Status,
    SearchMatchKey? Key,
    NormalizedEntry? Entry,
    long Ordinal,
    long TotalMatches);

/// <summary>Facet dimensions that support complete-value discovery.</summary>
public enum FacetQueryDimension : byte
{
    Tag,
    Process,
    Pid,
    Tid,
    Buffer,
}

/// <summary>A stable text or numeric facet key.</summary>
public readonly record struct FacetQueryKey(string? Text, int? Number)
{
    /// <summary>Creates a text facet key.</summary>
    public static FacetQueryKey OfText(string value) => new(value, null);
    /// <summary>Creates a numeric facet key.</summary>
    public static FacetQueryKey OfNumber(int value) => new(null, value);
    /// <summary>Gets invariant display text used by literal discovery search.</summary>
    public string DisplayText => Text ?? Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>One facet value and its own-dimension-omitted count.</summary>
public sealed record FacetQueryValue(FacetQueryKey Key, long Count, bool Included, bool Excluded);

/// <summary>Stable keyset cursor for a complete facet-value page.</summary>
public sealed record FacetPageCursor(
    Guid SessionId,
    long SnapshotGeneration,
    string FilterFingerprint,
    FacetQueryDimension Dimension,
    string SearchText,
    bool ExactMatch,
    long Count,
    FacetQueryKey Key);

/// <summary>One bounded page of complete facet-value discovery results.</summary>
public sealed record FacetValuesResult(
    QueryIdentity Identity,
    FacetQueryDimension Dimension,
    string SearchText,
    long NeutralMatchCount,
    IReadOnlyList<FacetQueryValue> ActiveValues,
    IReadOnlyList<FacetQueryValue> NeutralValues,
    FacetPageCursor? NextCursor);
