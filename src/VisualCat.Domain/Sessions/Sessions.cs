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
    long RetentionDeleted = 0,

    // How much wall-clock time the gaps above actually cost. A count of one, with no
    // duration beside it, told a reader that something was missing and nothing about how
    // much: a reboot mid-capture and a half-second cable jolt both read as
    // "reconnectGaps: 1" (finding F-12).
    long ReconnectGapMilliseconds = 0);

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

/// <summary>
/// What a live capture was asked to do, recorded so a finished session can answer for
/// itself.
/// </summary>
/// <remarks>
/// A session used to record only what arrived. <c>buffers</c> in the manifest is the
/// per-record attribution dictionary — the buffers that actually produced a record — so a
/// capture that selected <c>crash</c> and saw nothing from it was indistinguishable from
/// one that never asked for it (finding F-02). The same silence hid a timestamp policy
/// built from the wrong zone (F-11) and left a capture stopped by its own byte cap with no
/// vocabulary to name the limit that fired (F-07). Every field here is known before the
/// first byte is read, and none is more sensitive than the device serial already stored.
///
/// Entirely optional: a session written before this existed, and every source that is not
/// a device capture, carries a null and reads back as one.
/// </remarks>
/// <param name="RequestedBuffers">The buffers the capture asked for, in the order asked.</param>
/// <param name="PreRollSeconds">How much history before the start instant was requested.</param>
/// <param name="IncludesBufferHistory">Whether the capture deliberately took the whole ring buffer.</param>
/// <param name="DurationLimitSeconds">The stop-after-time the caller enforced, if any.</param>
/// <param name="ByteLimit">The stop-after-size the source enforced, if any.</param>
/// <param name="NegotiatedFormat">The rung of the logcat format ladder the device agreed to.</param>
/// <param name="LogTimeZoneId">The zone that format writes timestamps in.</param>
/// <param name="AdbVersion">The ADB build that ran the capture.</param>
/// <param name="DeviceModel">The device model as the transport reported it.</param>
/// <param name="DeviceFingerprint">The device build fingerprint, when the device answered for it.</param>
public sealed record CaptureSettings(
    IReadOnlyList<string>? RequestedBuffers = null,
    double? PreRollSeconds = null,
    bool IncludesBufferHistory = false,
    double? DurationLimitSeconds = null,
    long? ByteLimit = null,
    string? NegotiatedFormat = null,
    string? LogTimeZoneId = null,
    string? AdbVersion = null,
    string? DeviceModel = null,
    string? DeviceFingerprint = null);

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
    string StoreVersion = "2.0",

    // What the capture was asked for, as opposed to what it received. Null for every
    // source that is not a live device capture, and for sessions written before the
    // block existed (finding F-02).
    CaptureSettings? CaptureSettings = null);

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
    string? Error = null,
    // How many segments the live session currently holds. Compaction keeps this
    // roughly logarithmic in captured entries; a number that climbs with wall-clock
    // time instead means compaction is not keeping up, which is what the capture
    // health surface reports (§10.4, §12.4).
    int SegmentCount = 0,
    // Non-fatal trouble the capture recovered from and kept going through, phrased for
    // the person watching. Null while the capture is healthy.
    string? Warning = null);
