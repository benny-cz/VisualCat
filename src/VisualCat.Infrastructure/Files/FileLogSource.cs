using System.Runtime.CompilerServices;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Infrastructure.Files;

public sealed class FileLogSource : ILogSource, ISourceDefectSource
{
    private const int MaximumProbeLineBytes = 1024 * 1024;
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();
    private readonly int _chunkBytes;
    private readonly long _initialLength;
    private readonly DateTime _initialWriteTime;
    private int _sourceChanged;

    /// <param name="path">The log file to read.</param>
    /// <param name="chunkBytes">How much is read at a time.</param>
    /// <param name="displayName">
    /// What to call this log, when the file on disk is not named after it.
    /// </param>
    /// <remarks>
    /// A log another app shared is copied into a private cache file whose name has to be
    /// unique, so it carries a UTC stamp and a GUID. Publishing <c>FileInfo.Name</c> as the
    /// source display name put that whole string into the session descriptor, and from there
    /// into the tab, the share archive, the exported CSV and the accessibility tree — so
    /// opening <c>tiny.txt</c> produced a session called
    /// <c>20260821-200744-8fde…-raw:_storage_emulated_0_Download_tiny.txt</c> (finding F-27).
    /// The bytes are one fact and the name is another; a caller that knows the second one
    /// passes it, and it is what gets stored.
    /// </remarks>
    public FileLogSource(string path, int chunkBytes = 1024 * 1024, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkBytes);
        _path = Path.GetFullPath(path);
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Log source was not found.", _path);
        }

        _initialLength = info.Length;
        _initialWriteTime = info.LastWriteTimeUtc;
        _chunkBytes = chunkBytes;
        Metadata = new SourceMetadata(
            SourceKind.File,
            string.IsNullOrWhiteSpace(displayName) ? info.Name : displayName,
            _path,
            _path,
            info.Length,
            info.LastWriteTimeUtc,
            true,
            true);
    }

    public SourceMetadata Metadata { get; }

    public async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(
        int maximumUsefulLines,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUsefulLines);
        var lines = new List<ReadOnlyMemory<byte>>(maximumUsefulLines);
        await using var stream = Open();
        var buffer = new byte[Math.Min(_chunkBytes, 1024 * 1024)];
        var pending = new List<byte>(1024);
        while (lines.Count < maximumUsefulLines)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var i = 0; i < read && lines.Count < maximumUsefulLines; i++)
            {
                pending.Add(buffer[i]);
                if (buffer[i] == (byte)'\n')
                {
                    lines.Add(pending.ToArray());
                    pending.Clear();
                }
                else if (pending.Count >= MaximumProbeLineBytes)
                {
                    // Format probing is advisory. Keeping an unbounded first line here
                    // would defeat the ingest line guard before the pipeline even starts.
                    lines.Add(pending.ToArray());
                    return lines;
                }
            }
        }

        if (pending.Count > 0 && lines.Count < maximumUsefulLines)
        {
            lines.Add(pending.ToArray());
        }

        return lines;
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await using var stream = Open();
        // The chunk is consumed before the iterator is advanced, so one read buffer can
        // serve the whole finite import. Line batching copies the bytes it retains.
        var buffer = GC.AllocateUninitializedArray<byte>(_chunkBytes);
        long offset = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            yield return new SourceChunk(offset, buffer.AsMemory(0, read));
            offset += read;
        }

        var final = new FileInfo(_path);
        if (final.Length != _initialLength || final.LastWriteTimeUtc != _initialWriteTime)
        {
            Interlocked.Exchange(ref _sourceChanged, 1);
            throw new IOException("The source file changed while it was being imported.");
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

    private FileStream Open() =>
        new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, _chunkBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
}
