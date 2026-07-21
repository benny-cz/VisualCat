using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Sessions;

/// <summary>Identifies the acquisition mechanism that produced a session.</summary>
public enum SourceKind : byte
{
    /// <summary>A finite file.</summary>
    File,
    /// <summary>A file followed while it grows.</summary>
    GrowingFile,
    /// <summary>A host-side ADB capture.</summary>
    Adb,
    /// <summary>An Android on-device capture.</summary>
    Android,
    /// <summary>An in-memory source, primarily for tests.</summary>
    Memory,
}

/// <summary>Represents user-visible session lifecycle state.</summary>
public enum SessionState : byte
{
    /// <summary>No source has been selected.</summary>
    Empty,
    /// <summary>Source selection is active.</summary>
    SelectingSource,
    /// <summary>A finite source is being imported.</summary>
    Importing,
    /// <summary>A live source is connecting.</summary>
    Connecting,
    /// <summary>A live source is streaming.</summary>
    Streaming,
    /// <summary>A live source is paused.</summary>
    Paused,
    /// <summary>A graceful live stop is draining.</summary>
    Stopping,
    /// <summary>Live acquisition stopped successfully.</summary>
    Stopped,
    /// <summary>The session is ready for querying.</summary>
    Ready,
    /// <summary>Cancellation is draining in-flight work.</summary>
    Cancelling,
    /// <summary>The operation was cancelled.</summary>
    Cancelled,
    /// <summary>The operation failed.</summary>
    Failed,
}

/// <summary>Identifies the current ingest pipeline stage.</summary>
public enum IngestStage : byte
{
    /// <summary>Selecting a source.</summary>
    Selecting,
    /// <summary>Reading source bytes.</summary>
    Reading,
    /// <summary>Parsing physical lines.</summary>
    Parsing,
    /// <summary>Restoring deterministic source order.</summary>
    Sequencing,
    /// <summary>Mining message templates.</summary>
    Mining,
    /// <summary>Committing immutable columns.</summary>
    Committing,
    /// <summary>Compacting published segments.</summary>
    Compacting,
    /// <summary>Finalizing manifests and checksums.</summary>
    Finalizing,
    /// <summary>Ingest completed successfully.</summary>
    Ready,
    /// <summary>Ingest was cancelled.</summary>
    Cancelled,
    /// <summary>Ingest failed.</summary>
    Failed,
}

/// <summary>Enforces valid transitions between session lifecycle states.</summary>
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

    /// <summary>Creates a state machine at the requested initial state.</summary>
    public SessionStateMachine(SessionState initial = SessionState.Empty) => State = initial;
    /// <summary>Gets the current lifecycle state.</summary>
    public SessionState State { get; private set; }

    /// <summary>Moves to a permitted state or throws for an invalid transition.</summary>
    public void TransitionTo(SessionState next)
    {
        if (!Allowed[State].Contains(next))
        {
            throw new InvalidOperationException($"Invalid session transition {State} -> {next}.");
        }

        State = next;
    }
}

/// <summary>Defines deterministic rules for resolving incomplete logcat timestamps.</summary>
public sealed record TimestampPolicy(
    int? AssumedYear,
    string TimeZoneId,
    DateTimeOffset ReferenceInstant,
    int RolloverBackwardMonthThreshold = 6,
    bool PreferEarlierAmbiguousOffset = true,
    bool UseArrivalTimeForUntimed = false)
{
    /// <summary>Creates the default policy for a finite file using its modification time.</summary>
    public static TimestampPolicy ForFile(DateTimeOffset fileModified, string? timeZoneId = null) =>
        new(null, timeZoneId ?? TimeZoneInfo.Local.Id, fileModified);
}

/// <summary>Configures bounded, deterministic Drain-style template mining.</summary>
public sealed record TemplateSettings(
    bool Enabled = true,
    int Depth = 4,
    double SimilarityThreshold = 0.4,
    int MaximumChildren = 100,
    int MaximumClustersPerTag = 10_000,
    int RepresentativeExamples = 3,
    string AlgorithmVersion = "drain-v2");

/// <summary>Configures bounded parsing, ordering, segmentation, and raw-data retention.</summary>
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
    /// <summary>Gets explicit parser parallelism or a processor-derived default.</summary>
    public int EffectiveParseWorkers => ParseWorkers > 0 ? ParseWorkers : Math.Max(1, Environment.ProcessorCount - 1);
}

/// <summary>Counts non-fatal evidence-quality and acquisition defects.</summary>
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

/// <summary>Associates a PID with a resolved process name over a time interval.</summary>
public sealed record ProcessNameRange(
    int Pid,
    string Name,
    InstantUs FirstSeen,
    InstantUs LastSeen);

/// <summary>Counts physical source input and normalized session output.</summary>
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

/// <summary>Describes immutable session identity, policy, state, counts, and time bounds.</summary>
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

/// <summary>Captures one monotonic progress observation from the ingest coordinator.</summary>
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
