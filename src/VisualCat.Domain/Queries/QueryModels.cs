using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Queries;

public enum EntryOrder : byte
{
    Chronological,
    SourceSequence,
}

public sealed record QueryIdentity(
    Guid SessionId,
    long SnapshotGeneration,
    string FilterFingerprint,
    long QueryGeneration);

public readonly record struct AggregateCell(TimeRange Range, LogLevel Level, long Count);

public sealed record HeatMapResult(
    QueryIdentity Identity,
    Viewport Viewport,
    IReadOnlyList<TimeRange> Columns,
    IReadOnlyDictionary<LogLevel, long[]> Counts,
    long MaximumCount,
    bool HasUnknown);

public sealed record FacetValue<T>(T Value, long Count);

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

public sealed record EntryCursor(EntryOrder Order, long TimestampUs, long Sequence);

public sealed record EntryPage(
    QueryIdentity Identity,
    IReadOnlyList<NormalizedEntry> Entries,
    EntryCursor? NextCursor,
    long? TotalCount);

public sealed record TemplateSummary(
    uint TemplateId,
    string CanonicalText,
    long Count,
    InstantUs? First,
    InstantUs? Last,
    IReadOnlyList<long> RepresentativeEntryIds);

public sealed record SearchProgress(
    QueryIdentity Identity,
    long RecordsScanned,
    long Matches,
    bool Completed,
    double Progress);

public sealed record SearchResult(
    QueryIdentity Identity,
    long Matches,
    IReadOnlyList<InstantUs> Markers,
    bool MarkersTruncated);
