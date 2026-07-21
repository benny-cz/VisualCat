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
    IReadOnlyDictionary<string, string>? Properties = null);

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
