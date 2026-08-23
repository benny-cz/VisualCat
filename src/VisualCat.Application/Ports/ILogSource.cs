using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Application.Ports;

public sealed record SourceMetadata(
    SourceKind Kind,
    string DisplayName,
    string Description,
    string? SourcePath,
    long? Length,
    DateTimeOffset ReferenceInstant,
    bool IsFinite,
    bool IsReplayable,
    string? DeviceSerial = null,
    IReadOnlyDictionary<string, string>? Properties = null)
{
    /// <summary>
    /// <see cref="Properties"/> key naming the time zone the source's own timestamps are
    /// written in. A caller cannot know this — a device capture asks logcat for UTC while a
    /// file on disk carries whatever the machine that wrote it used — and guessing produced
    /// timestamps that were wrong by a whole UTC offset without ever saying so. Sources that
    /// know declare it; a source that does not is read in the local zone, which is what an
    /// imported file already assumes.
    /// </summary>
    public const string LogTimeZoneProperty = "logTimeZone";

    /// <summary>
    /// A capture's name, made to tell it apart from the last one.
    /// </summary>
    /// <remarks>
    /// Every on-device capture was called "On-device logcat" — in the tab strip, in Recent
    /// sessions, in the empty state, in Session cache and in the suggested export filename.
    /// Two tabs open at once were two identical chips with nothing whatever to choose
    /// between them (audit 2, C2). The start time is the discriminator, because it is the
    /// one thing that differs and the one the reader already remembers.
    ///
    /// Local time, because it is read by a person standing next to the device. Not colons:
    /// this becomes a folder name and a suggested filename, and a sanitised name in one list
    /// beside an unsanitised one in another is the inconsistency that started all this; a
    /// period would be worse, since
    /// <see cref="System.IO.Path.GetFileNameWithoutExtension(string)"/> would eat the seconds.
    ///
    /// Not hyphens either, which is what they were. A capture started at 20:09:12 was called
    /// "On-device logcat 20-09-12", and hyphenated triples of two digits are what dates look
    /// like: the first thing that name says is "12 September 2020" (finding F-16). The
    /// h/m separators are unambiguous, filesystem-safe on every platform the product runs on,
    /// and one character narrower than nothing else would be.
    /// </remarks>
    public static string NameCaptureStartedNow(string sourceName) =>
        $"{sourceName} {DateTimeOffset.Now:HH}h{DateTimeOffset.Now:mm}m{DateTimeOffset.Now:ss}";

    /// <summary>The declared timestamp zone, or the local zone when the source is silent.</summary>
    public string ResolveLogTimeZoneId() =>
        Properties?.TryGetValue(LogTimeZoneProperty, out var zone) == true && !string.IsNullOrWhiteSpace(zone)
            ? zone
            : TimeZoneInfo.Local.Id;
}

public sealed record SourceChunk(long RawOffset, ReadOnlyMemory<byte> Bytes);

public sealed record SourceReadContext(Guid SessionId, long CoordinatorGeneration, string SessionDirectory);

public interface ILogSource : IAsyncDisposable
{
    SourceMetadata Metadata { get; }

    Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(
        int maximumUsefulLines,
        CancellationToken cancellationToken);

    IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What a source turned out to be able to see, once it started seeing it.
/// </summary>
/// <param name="FullDevice">Whether records from other processes are reaching the reader.</param>
/// <param name="Description">The source description to show from now on.</param>
/// <param name="Summary">A few words for the status line, or null when nothing is wrong.</param>
/// <param name="Remedy">The whole explanation and the route out, or null when nothing is wrong.</param>
public sealed record SourceScopeReport(
    bool FullDevice,
    string Description,
    string? Summary,
    string? Remedy);

/// <summary>
/// A source whose real reach cannot be known before it starts producing data.
/// </summary>
/// <remarks>
/// Android 13 and later can put a per-use consent dialog in front of direct device-log access
/// even for an app that holds <c>READ_LOGS</c>. Declining that consent does not necessarily fail
/// <c>logcat</c>: the app can receive its own process's records and nothing else while every
/// permission check the app can make still says <c>READ_LOGS</c> is held. The platform may cache
/// a recent consent briefly, so the dialog must never be treated as a guaranteed every-capture
/// signal. A capture that had been declined therefore went
/// on describing itself as full-device while delivering 24 lines in 40 seconds
/// (audit 2, C1).
///
/// A separate probe process would answer the question, but on this platform running
/// <c>logcat</c> is what raises the consent dialog, so probing would ask the reader twice. The
/// source reports what it can see from the stream it already has.
/// </remarks>
public interface ISourceScopeReporter
{
    /// <summary>
    /// Raised on a background thread when the source can say what it is actually seeing. A
    /// source that never resolves is reporting nothing, not reporting success.
    /// </summary>
    /// <remarks>
    /// It may be raised more than once, and a later report replaces an earlier one entirely —
    /// description, summary and remedy alike. A restricted scope is inferred from an absence
    /// (no record from another process has arrived yet) and an absence can be disproved, so a
    /// source that later sees the device says so rather than living with the first answer it
    /// gave. A handler that treats the first report as final will therefore go on describing a
    /// full-device capture as own-app-only, which is the defect this replaces (audit 3, A1).
    /// </remarks>
    event Action<SourceScopeReport>? ScopeResolved;
}

/// <summary>A temporary, recoverable transport condition that should be visible during capture.</summary>
/// <param name="Summary">Short status-line wording, such as a numbered reconnect attempt.</param>
/// <param name="Detail">A complete explanation suitable for the session health pane.</param>
public sealed record SourceConnectionStatus(string Summary, string Detail);

/// <summary>
/// A live source that can temporarily stop delivering bytes while it repairs its transport.
/// </summary>
public interface ISourceConnectionStatusReporter
{
    /// <summary>
    /// Raised from any thread. A non-null value replaces the current connection status; null
    /// clears it as soon as the transport is healthy again.
    /// </summary>
    event Action<SourceConnectionStatus?>? ConnectionStatusChanged;
}

public interface IProcessNameSource
{
    Task<IReadOnlyList<ProcessNameRange>> GetProcessNamesAsync(CancellationToken cancellationToken);
}

public interface ISourceDefectSource
{
    DefectCounters GetDefects();
}
