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

public interface IProcessNameSource
{
    Task<IReadOnlyList<ProcessNameRange>> GetProcessNamesAsync(CancellationToken cancellationToken);
}

public interface ISourceDefectSource
{
    DefectCounters GetDefects();
}
