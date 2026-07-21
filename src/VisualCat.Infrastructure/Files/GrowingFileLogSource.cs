using System.Runtime.CompilerServices;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Infrastructure.Files;

public sealed class GrowingFileLogSource : ILogSource, ISourceDefectSource
{
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _pollInterval;
    private readonly int _chunkBytes;
    private int _sourceChanged;

    public GrowingFileLogSource(string path, TimeSpan? pollInterval = null, int chunkBytes = 1024 * 1024)
    {
        _path = Path.GetFullPath(path);
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Growing log source was not found.", _path);
        }

        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _chunkBytes = chunkBytes;
        Metadata = new SourceMetadata(
            SourceKind.GrowingFile,
            info.Name,
            $"{_path} (follow)",
            null,
            null,
            info.LastWriteTimeUtc,
            false,
            true,
            Properties: new Dictionary<string, string> { ["path"] = _path, ["rotationPolicy"] = "stop" });
    }

    public SourceMetadata Metadata { get; }

    public async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(int maximumUsefulLines, CancellationToken cancellationToken)
    {
        await using var file = new FileLogSource(_path);
        return await file.ProbeAsync(maximumUsefulLines, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            _chunkBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var timer = new PeriodicTimer(_pollInterval);
        long offset = 0;
        while (!linked.IsCancellationRequested)
        {
            var buffer = new byte[_chunkBytes];
            var read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            if (read > 0)
            {
                if (read != buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }

                yield return new SourceChunk(offset, buffer);
                offset += read;
                continue;
            }

            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                Interlocked.Exchange(ref _sourceChanged, 1);
                throw new IOException("The followed file was removed.");
            }

            if (info.Length < offset)
            {
                Interlocked.Exchange(ref _sourceChanged, 1);
                throw new IOException("The followed file was truncated or rotated; the configured policy is to stop.");
            }

            await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false);
        }
    }

    public DefectCounters GetDefects() => new(SourceChanges: Volatile.Read(ref _sourceChanged));

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
