using System.Diagnostics;
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
    private readonly Func<int, byte[]> _readBufferFactory;
    private int _sourceChanged;

    public GrowingFileLogSource(string path, TimeSpan? pollInterval = null, int chunkBytes = 1024 * 1024)
        : this(path, pollInterval, chunkBytes, static size => new byte[size])
    {
    }

    internal GrowingFileLogSource(
        string path,
        TimeSpan? pollInterval,
        int chunkBytes,
        Func<int, byte[]> readBufferFactory)
    {
        ArgumentNullException.ThrowIfNull(readBufferFactory);
        _path = Path.GetFullPath(path);
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Growing log source was not found.", _path);
        }

        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _chunkBytes = chunkBytes;
        _readBufferFactory = readBufferFactory;
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

        // Hoisted out of the poll loop. Following a file means running this loop every
        // _pollInterval for as long as the capture lasts, and the read buffer is a
        // megabyte — comfortably a large-object allocation. Allocating it per iteration
        // cost about four mebibytes a second, fifteen gibibytes an hour, and a continuous
        // gen2 collection cadence while the followed file was idle and the loop was
        // delivering nothing at all. The LOH is not compacted by default, so that is
        // fragmentation as well as churn. One buffer for the life of the read costs one
        // allocation instead.
        var buffer = _readBufferFactory(_chunkBytes);
        if (buffer.Length < _chunkBytes)
        {
            throw new InvalidOperationException("The growing-file read buffer is smaller than the configured chunk size.");
        }

        var identity = FollowedFileIdentity.Of(stream);
        var lastIdentityCheck = Stopwatch.GetTimestamp();
        long offset = 0;
        while (!linked.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            if (read > 0)
            {
                // The consumer keeps what it is handed past the next read, so the chunk
                // has to be its own array rather than a window onto the shared buffer.
                // Sized to the bytes actually read: a partial read used to allocate a
                // full-sized array and then Array.Resize it, which is two allocations
                // and a copy where one copy will do.
                var chunk = GC.AllocateUninitializedArray<byte>(read);
                buffer.AsSpan(0, read).CopyTo(chunk);
                yield return new SourceChunk(offset, chunk);
                offset += read;

                // A successful read is not evidence that the file being read is still the file
                // at the followed path: on Linux an open descriptor keeps an unlinked inode
                // alive and readable, so a writer that keeps appending to the rotated-away file
                // makes reads succeed forever while everything written to the live path is
                // invisible (finding F-31, case 7). Checked no more often than one poll
                // interval, so a busy stream does not stat per chunk.
                if (Stopwatch.GetElapsedTime(lastIdentityCheck) < _pollInterval)
                {
                    continue;
                }

                lastIdentityCheck = Stopwatch.GetTimestamp();
                RaiseIfSourceChanged(identity, offset);
                continue;
            }

            lastIdentityCheck = Stopwatch.GetTimestamp();
            RaiseIfSourceChanged(identity, offset);
            await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends the follow when the file at the followed path is no longer the file being read.
    /// </summary>
    /// <remarks>
    /// Identity first, length second. Comparing lengths can see only a replacement that is
    /// <em>shorter</em> than what has already been delivered, and an ordinary <c>logrotate</c>
    /// without <c>copytruncate</c> — the default on most distributions — usually leaves a
    /// replacement that is longer, so 93 records written to the file the product named as its
    /// source never appeared and no source change was recorded at all (finding F-31).
    /// </remarks>
    private void RaiseIfSourceChanged(FollowedFileIdentity identity, long delivered)
    {
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            Interlocked.Exchange(ref _sourceChanged, 1);
            throw new FollowedSourceChangedException("The followed file was removed.");
        }

        if (identity.HasMoved(_path))
        {
            Interlocked.Exchange(ref _sourceChanged, 1);
            throw new FollowedSourceChangedException(
                "The followed file was replaced (rotated); the configured policy is to stop. " +
                "Everything read before the rotation is kept.");
        }

        if (info.Length < delivered)
        {
            Interlocked.Exchange(ref _sourceChanged, 1);
            throw new FollowedSourceChangedException(
                "The followed file was truncated or rotated; the configured policy is to stop. " +
                "Everything read before the change is kept.");
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
