using System.Runtime.CompilerServices;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Infrastructure.Testing;

public sealed class MemoryLogSource : ILogSource
{
    private readonly byte[] _bytes;
    private readonly int[] _chunkSizes;
    private readonly TimeSpan _delay;
    private readonly long? _failAtOffset;
    private readonly CancellationTokenSource _stop = new();

    public MemoryLogSource(
        ReadOnlyMemory<byte> bytes,
        IReadOnlyList<int>? chunkSizes = null,
        TimeSpan? delay = null,
        long? failAtOffset = null,
        string name = "memory.log")
    {
        _bytes = bytes.ToArray();
        _chunkSizes = chunkSizes?.ToArray() ?? [4096];
        if (_chunkSizes.Length == 0 || _chunkSizes.Any(static size => size <= 0))
        {
            throw new ArgumentException("Chunk sizes must contain positive values.", nameof(chunkSizes));
        }

        _delay = delay ?? TimeSpan.Zero;
        _failAtOffset = failAtOffset;
        Metadata = new SourceMetadata(
            SourceKind.Memory,
            name,
            name,
            null,
            _bytes.Length,
            DateTimeOffset.UtcNow,
            true,
            true);
    }

    public SourceMetadata Metadata { get; }

    public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(int maximumUsefulLines, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lines = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        for (var i = 0; i < _bytes.Length && lines.Count < maximumUsefulLines; i++)
        {
            if (_bytes[i] != (byte)'\n')
            {
                continue;
            }

            lines.Add(_bytes.AsMemory(start, i - start + 1));
            start = i + 1;
        }

        if (start < _bytes.Length && lines.Count < maximumUsefulLines)
        {
            lines.Add(_bytes.AsMemory(start));
        }

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<byte>>>(lines);
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var offset = 0;
        var chunkIndex = 0;
        while (offset < _bytes.Length)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (_failAtOffset is { } failure && offset >= failure)
            {
                throw new IOException($"Injected source failure at byte {offset}.");
            }

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, linked.Token).ConfigureAwait(false);
            }

            var length = Math.Min(_chunkSizes[chunkIndex++ % _chunkSizes.Length], _bytes.Length - offset);
            yield return new SourceChunk(offset, _bytes.AsMemory(offset, length));
            offset += length;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _stop.Cancel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }
}
