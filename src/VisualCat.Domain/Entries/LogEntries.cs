using VisualCat.Domain.Time;

namespace VisualCat.Domain.Entries;

/// <summary>Identifies Android log severity using the persisted storage values.</summary>
public enum LogLevel : byte
{
    /// <summary>Verbose diagnostic detail.</summary>
    Verbose = 0,
    /// <summary>Debug diagnostic detail.</summary>
    Debug = 1,
    /// <summary>Informational output.</summary>
    Info = 2,
    /// <summary>A warning condition.</summary>
    Warn = 3,
    /// <summary>An error condition.</summary>
    Error = 4,
    /// <summary>A fatal or assertion condition.</summary>
    Fatal = 5,
    /// <summary>An unrecognized severity.</summary>
    Unknown = 255,
}

/// <summary>Provides stable severity ordering and logcat-letter conversion.</summary>
public static class LogLevels
{
    /// <summary>Gets severities in their persisted bitmap order.</summary>
    public static ReadOnlySpan<LogLevel> StorageOrder =>
        [LogLevel.Verbose, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal, LogLevel.Unknown];

    /// <summary>Gets severities in descending user-facing importance.</summary>
    public static ReadOnlySpan<LogLevel> DisplayOrder =>
        [LogLevel.Fatal, LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, LogLevel.Verbose, LogLevel.Unknown];

    /// <summary>Parses a single logcat severity letter.</summary>
    public static LogLevel Parse(char value) => char.ToUpperInvariant(value) switch
    {
        'V' => LogLevel.Verbose,
        'D' => LogLevel.Debug,
        'I' => LogLevel.Info,
        'W' => LogLevel.Warn,
        'E' => LogLevel.Error,
        'F' or 'A' => LogLevel.Fatal,
        _ => LogLevel.Unknown,
    };

    /// <summary>Returns the canonical logcat severity letter.</summary>
    public static char ToLetter(this LogLevel level) => level switch
    {
        LogLevel.Verbose => 'V',
        LogLevel.Debug => 'D',
        LogLevel.Info => 'I',
        LogLevel.Warn => 'W',
        LogLevel.Error => 'E',
        LogLevel.Fatal => 'F',
        _ => '?',
    };
}

/// <summary>Identifies the supported logcat text layout.</summary>
public enum LogcatFormat : byte
{
    /// <summary>No supported format was identified.</summary>
    Unknown,
    /// <summary>Android `threadtime` format.</summary>
    ThreadTime,
    /// <summary>Android `time` format.</summary>
    Time,
    /// <summary>Android `brief` format.</summary>
    Brief,
    /// <summary>Android multi-line `long` format.</summary>
    LongFormat,
    /// <summary>Epoch-prefixed logcat format.</summary>
    Epoch,
}

/// <summary>Classifies how a physical source line contributed to parsing.</summary>
public enum ParseOutcomeKind : byte
{
    /// <summary>The line produced a timed normalized entry.</summary>
    ParsedEntry,
    /// <summary>The line was logcat metadata rather than an entry.</summary>
    MetaRecord,
    /// <summary>The line continued a preceding entry.</summary>
    Continuation,
    /// <summary>The line produced an entry without a resolved timestamp.</summary>
    UntimedEntry,
    /// <summary>The line was blank and intentionally ignored.</summary>
    IgnoredBlank,
    /// <summary>The line did not match a supported syntax.</summary>
    UnknownLine,
    /// <summary>The line resembled a format but failed its validation.</summary>
    RejectedCandidate,
}

/// <summary>Records how a normalized timestamp was established.</summary>
public enum TimestampProvenance : byte
{
    /// <summary>No timestamp was available.</summary>
    Missing,
    /// <summary>The source carried a Unix epoch value.</summary>
    Epoch,
    /// <summary>The source carried an explicit numeric UTC offset.</summary>
    ExplicitOffset,
    /// <summary>The source explicitly identified UTC.</summary>
    ExplicitUtc,
    /// <summary>Both year and time zone were inferred by policy.</summary>
    InferredYearAndZone,
    /// <summary>The time zone was inferred by policy.</summary>
    InferredZone,
    /// <summary>Arrival time was used for an otherwise untimed entry.</summary>
    ArrivalTime,
}

/// <summary>Describes parsing, ordering, and acquisition qualities of an entry.</summary>
[Flags]
public enum EntryAttributes : ushort
{
    /// <summary>No additional attributes.</summary>
    None = 0,
    /// <summary>At least part of the timestamp was inferred.</summary>
    InferredTimestamp = 1 << 0,
    /// <summary>The resolved timestamp has low confidence.</summary>
    LowTimestampConfidence = 1 << 1,
    /// <summary>The source arrived outside chronological order.</summary>
    OutOfOrder = 1 << 2,
    /// <summary>Decoding required a configured fallback.</summary>
    EncodingFallback = 1 << 3,
    /// <summary>The source line exceeded the configured bound.</summary>
    LongLineOverflow = 1 << 4,
    /// <summary>The entry represents Android chatty suppression.</summary>
    Chatty = 1 << 5,
    /// <summary>The entry owns one or more continuation lines.</summary>
    ContinuationGroup = 1 << 6,
    /// <summary>The acquisition layer identified a reconnect duplicate.</summary>
    ReconnectDuplicate = 1 << 7,
}

/// <summary>Locates an exact byte range in the original source.</summary>
public readonly record struct RawSpan
{
    /// <summary>Creates a validated non-negative source span.</summary>
    public RawSpan(long offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Offset = offset;
        Length = length;
    }

    /// <summary>Gets the zero-based source byte offset.</summary>
    public long Offset { get; }
    /// <summary>Gets the byte length.</summary>
    public int Length { get; }
}

/// <summary>Represents one byte-faithful physical line supplied to the parser.</summary>
public sealed record SourceLine(
    Guid SessionId,
    long Sequence,
    RawSpan Raw,
    ReadOnlyMemory<byte> Bytes,
    InstantUs? ArrivalInstant = null,
    string? BufferId = null);

/// <summary>Represents parsed timestamp fields before policy-based resolution.</summary>
public sealed record TimestampToken(
    string OriginalText,
    int? Year,
    int Month,
    int Day,
    int Hour,
    int Minute,
    int Second,
    int Microsecond,
    long? EpochMicroseconds = null,
    bool IsUtc = false,
    TimeSpan? ExplicitOffset = null);

/// <summary>Contains validated logcat fields before timestamp normalization and storage.</summary>
public sealed record ParsedFields(
    TimestampToken? Timestamp,
    int Pid,
    int Tid,
    LogLevel Level,
    string Tag,
    string Message,
    LogcatFormat Format,
    string? Buffer,
    int MessageByteOffset,
    int MessageByteLength,
    long ChattyDeclaredDrops = 0,
    EntryAttributes Attributes = EntryAttributes.None);

/// <summary>Describes the parser's disposition of one source line.</summary>
public sealed record ParseOutcome(
    ParseOutcomeKind Kind,
    SourceLine Source,
    ParsedFields? Fields = null,
    string? Reason = null)
{
    /// <summary>Creates a successfully parsed timed outcome.</summary>
    public static ParseOutcome Parsed(SourceLine source, ParsedFields fields) =>
        new(ParseOutcomeKind.ParsedEntry, source, fields);

    /// <summary>Creates an entry outcome whose timestamp could not be resolved.</summary>
    public static ParseOutcome Untimed(SourceLine source, ParsedFields fields) =>
        new(ParseOutcomeKind.UntimedEntry, source, fields);

    /// <summary>Creates a metadata outcome.</summary>
    public static ParseOutcome Meta(SourceLine source, string kind) =>
        new(ParseOutcomeKind.MetaRecord, source, null, kind);

    /// <summary>Creates an ignored-blank outcome.</summary>
    public static ParseOutcome Blank(SourceLine source) =>
        new(ParseOutcomeKind.IgnoredBlank, source, null, "blank");

    /// <summary>Creates an unknown-line outcome with diagnostic context.</summary>
    public static ParseOutcome Unknown(SourceLine source, string reason) =>
        new(ParseOutcomeKind.UnknownLine, source, null, reason);

    /// <summary>Creates a rejected-candidate outcome with diagnostic context.</summary>
    public static ParseOutcome Rejected(SourceLine source, string reason) =>
        new(ParseOutcomeKind.RejectedCandidate, source, null, reason);
}

/// <summary>Represents the complete immutable normalized form of one stored log entry.</summary>
public sealed record NormalizedEntry(
    Guid SessionId,
    long EntryId,
    long SourceSequence,
    RawSpan Raw,
    InstantUs? Timestamp,
    string OriginalTimestamp,
    TimestampProvenance TimestampProvenance,
    double TimestampConfidence,
    int Pid,
    int Tid,
    LogLevel Level,
    string Tag,
    string Buffer,
    string Message,
    LogcatFormat Format,
    string ParserVersion,
    uint TemplateId,
    EntryAttributes Flags,
    long? ParentEntryId = null);

/// <summary>Maps one physical source line to its parse disposition and optional entry.</summary>
public sealed record SourceRecord(
    long Sequence,
    RawSpan Raw,
    ParseOutcomeKind Outcome,
    long? EntryId,
    string? Reason);
