using VisualCat.Domain.Time;

namespace VisualCat.Domain.Entries;

public enum LogLevel : byte
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
    Unknown = 255,
}

public static class LogLevels
{
    public static ReadOnlySpan<LogLevel> StorageOrder =>
        [LogLevel.Verbose, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal, LogLevel.Unknown];

    public static ReadOnlySpan<LogLevel> DisplayOrder =>
        [LogLevel.Fatal, LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, LogLevel.Verbose, LogLevel.Unknown];

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

public enum LogcatFormat : byte
{
    Unknown,
    ThreadTime,
    Time,
    Brief,
    LongFormat,
    Epoch,
}

public enum ParseOutcomeKind : byte
{
    ParsedEntry,
    MetaRecord,
    Continuation,
    UntimedEntry,
    IgnoredBlank,
    UnknownLine,
    RejectedCandidate,
}

public enum TimestampProvenance : byte
{
    Missing,
    Epoch,
    ExplicitOffset,
    ExplicitUtc,
    InferredYearAndZone,
    InferredZone,
    ArrivalTime,
}

[Flags]
public enum EntryAttributes : ushort
{
    None = 0,
    InferredTimestamp = 1 << 0,
    LowTimestampConfidence = 1 << 1,
    OutOfOrder = 1 << 2,
    EncodingFallback = 1 << 3,
    LongLineOverflow = 1 << 4,
    Chatty = 1 << 5,
    ContinuationGroup = 1 << 6,
    ReconnectDuplicate = 1 << 7,
}

public readonly record struct RawSpan
{
    public RawSpan(long offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Offset = offset;
        Length = length;
    }

    public long Offset { get; }
    public int Length { get; }
}

public sealed record SourceLine(
    Guid SessionId,
    long Sequence,
    RawSpan Raw,
    ReadOnlyMemory<byte> Bytes,
    InstantUs? ArrivalInstant = null,
    string? BufferId = null);

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

public sealed record ParseOutcome(
    ParseOutcomeKind Kind,
    SourceLine Source,
    ParsedFields? Fields = null,
    string? Reason = null)
{
    public static ParseOutcome Parsed(SourceLine source, ParsedFields fields) =>
        new(ParseOutcomeKind.ParsedEntry, source, fields);

    public static ParseOutcome Untimed(SourceLine source, ParsedFields fields) =>
        new(ParseOutcomeKind.UntimedEntry, source, fields);

    public static ParseOutcome Meta(SourceLine source, string kind) =>
        new(ParseOutcomeKind.MetaRecord, source, null, kind);

    public static ParseOutcome Blank(SourceLine source) =>
        new(ParseOutcomeKind.IgnoredBlank, source, null, "blank");

    public static ParseOutcome Unknown(SourceLine source, string reason) =>
        new(ParseOutcomeKind.UnknownLine, source, null, reason);

    public static ParseOutcome Rejected(SourceLine source, string reason) =>
        new(ParseOutcomeKind.RejectedCandidate, source, null, reason);
}

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

public sealed record SourceRecord(
    long Sequence,
    RawSpan Raw,
    ParseOutcomeKind Outcome,
    long? EntryId,
    string? Reason);
