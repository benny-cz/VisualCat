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
