using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Sessions;

public enum SourceKind : byte
{
    File,
    GrowingFile,
    Adb,
    Android,
    Memory,
}

public enum SessionState : byte
{
    Empty,
    SelectingSource,
    Importing,
    Connecting,
    Streaming,
    Paused,
    Stopping,
    Stopped,
    Ready,
    Cancelling,
    Cancelled,
    Failed,
}

public enum IngestStage : byte
{
    Selecting,
    Reading,
    Parsing,
    Sequencing,
    Mining,
    Committing,
    Compacting,
    Finalizing,
    Ready,
    Cancelled,
    Failed,
}

public sealed class SessionStateMachine
{
    private static readonly Dictionary<SessionState, SessionState[]> Allowed =
        new Dictionary<SessionState, SessionState[]>
        {
            [SessionState.Empty] = [SessionState.SelectingSource],
            [SessionState.SelectingSource] = [SessionState.Importing, SessionState.Connecting, SessionState.Cancelled, SessionState.Failed],
            [SessionState.Importing] = [SessionState.Ready, SessionState.Cancelling, SessionState.Failed],
            [SessionState.Connecting] = [SessionState.Streaming, SessionState.Cancelling, SessionState.Failed],
            [SessionState.Streaming] = [SessionState.Paused, SessionState.Stopping, SessionState.Cancelling, SessionState.Failed],
            [SessionState.Paused] = [SessionState.Streaming, SessionState.Stopping, SessionState.Cancelling, SessionState.Failed],
            [SessionState.Stopping] = [SessionState.Stopped, SessionState.Failed],
            [SessionState.Stopped] = [SessionState.Ready],
            [SessionState.Cancelling] = [SessionState.Cancelled],
            [SessionState.Ready] = [],
            [SessionState.Cancelled] = [],
            [SessionState.Failed] = [],
        };

    public SessionStateMachine(SessionState initial = SessionState.Empty) => State = initial;
    public SessionState State { get; private set; }

    public void TransitionTo(SessionState next)
    {
        if (!Allowed[State].Contains(next))
        {
            throw new InvalidOperationException($"Invalid session transition {State} -> {next}.");
        }

        State = next;
    }
}

public sealed record TimestampPolicy(
    int? AssumedYear,
    string TimeZoneId,
    DateTimeOffset ReferenceInstant,
    int RolloverBackwardMonthThreshold = 6,
    bool PreferEarlierAmbiguousOffset = true,
    bool UseArrivalTimeForUntimed = false)
{
    public static TimestampPolicy ForFile(DateTimeOffset fileModified, string? timeZoneId = null) =>
        new(null, timeZoneId ?? TimeZoneInfo.Local.Id, fileModified);
}

public sealed record TemplateSettings(
    bool Enabled = true,
    int Depth = 4,
    double SimilarityThreshold = 0.4,
    int MaximumChildren = 100,
    int MaximumClustersPerTag = 10_000,
    int RepresentativeExamples = 3,
    string AlgorithmVersion = "drain-v2");

public sealed record IngestSettings(
    LogcatFormat? FormatOverride,
    string EncodingName,
    TimestampPolicy TimestampPolicy,
    TemplateSettings TemplateSettings,
    int BatchBytes = 4 * 1024 * 1024,
    int BatchLatencyMilliseconds = 250,
    int ChannelCapacity = 8,
    int ParseWorkers = 0,
    int SegmentEntries = 100_000,
    long ReorderHorizonUs = 5_000_000,
    int MaximumLineBytes = 16 * 1024 * 1024,
    bool PortableRaw = false)
{
    public int EffectiveParseWorkers => ParseWorkers > 0 ? ParseWorkers : Math.Max(1, Environment.ProcessorCount - 1);
}

public sealed record DefectCounters(
    long UnknownLines = 0,
    long RejectedCandidates = 0,
    long Continuations = 0,
    long UntimedEntries = 0,
    long TimestampInferences = 0,
    long LowConfidenceTimestamps = 0,
    long OutOfOrderEntries = 0,
    long LateSegmentEntries = 0,
    long EncodingFallbacks = 0,
    long LongLineOverflows = 0,
    long ChattyDeclaredDrops = 0,
    long ReconnectGaps = 0,
    long ReconnectDuplicates = 0,
    long SourceChanges = 0,
    long RetentionDeleted = 0);

public sealed record ProcessNameRange(
    int Pid,
    string Name,
    InstantUs FirstSeen,
    InstantUs LastSeen);

public sealed record SessionCounters(
    long SourceBytes = 0,
    long SourceLines = 0,
    long ParsedEntries = 0,
    long TimedEntries = 0,
    long MetaRecords = 0,
    long UnknownLines = 0,
    long RejectedCandidates = 0,
    long Continuations = 0,
    long UntimedEntries = 0,
    long IgnoredBlanks = 0,
    long Templates = 0);

public sealed record SessionDescriptor(
    Guid SessionId,
    string DisplayName,
    SourceKind SourceKind,
    string SourceDescription,
    DateTimeOffset CreatedUtc,
    SessionState State,
    long Generation,
    LogcatFormat DetectedFormat,
    double FormatConfidence,
    TimestampPolicy TimestampPolicy,
    TemplateSettings TemplateSettings,
    SessionCounters Counters,
    DefectCounters Defects,
    InstantUs? FirstInstant,
    InstantUs? LastInstant,
    bool Finalized,
    bool Degraded,
    string StoreVersion = "2.0");

public sealed record ProgressSnapshot(
    Guid SessionId,
    long CoordinatorGeneration,
    IngestStage Stage,
    long BytesRead,
    long BytesCommitted,
    long LinesRead,
    long LinesCommitted,
    long? TotalBytes,
    SessionCounters Counters,
    double ThroughputLinesPerSecond,
    TimeSpan Elapsed,
    TimeSpan? EstimatedRemaining,
    bool IsIndeterminate,
    bool IsCancellable,
    long SnapshotGeneration,
    SessionState? TerminalState = null,
    string? Error = null);
