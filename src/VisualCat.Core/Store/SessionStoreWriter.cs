using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Store;

public sealed class SessionStoreWriter : IAsyncDisposable
{
    private static readonly TimeSpan InitialFlushInterval = TimeSpan.FromSeconds(1);

    // A live tail should feel continuous, so the time-triggered flush ceiling is a few
    // seconds rather than half a minute: at 30s a quiet source left the plot frozen for
    // that long while entries were plainly arriving. The ceiling only governs low-volume
    // sources — a busy one fills a segment by size (SegmentEntries) and flushes far sooner,
    // so lowering it costs nothing there and only trades a bounded number of extra small
    // segments (finalization compacts them) for a far shorter visible gap (§10.6, §12.4).
    private static readonly TimeSpan MaximumFlushInterval = TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly IngestSettings _settings;
    private SourceIdentity _source;
    private readonly List<NormalizedEntry> _pending;
    private readonly List<SegmentManifest> _segments = [];
    private readonly Dictionary<string, uint> _tagIds = new(StringComparer.Ordinal);
    private readonly List<string> _tags = [];
    private readonly Dictionary<string, uint> _bufferIds = new(StringComparer.Ordinal);
    private readonly List<string> _buffers = [];
    private readonly FileStream _sourceRecordsStream;
    private readonly BinaryWriter _sourceRecords;
    private readonly FileStream _sourceIndexStream;
    private readonly BinaryWriter _sourceIndex;
    private readonly FileStream _untimedStream;
    private readonly Utf8JsonWriter _untimedWriter;
    private int _nextSegmentId = 1;
    private long _generation;
    private bool _finalized;
    private long _lastFlushTimestamp = Stopwatch.GetTimestamp();
    private TimeSpan _flushInterval = InitialFlushInterval;

    public SessionStoreWriter(string root, IngestSettings settings, SourceIdentity source)
    {
        _root = Path.GetFullPath(root);
        _settings = settings;
        _source = source;
        _pending = new List<NormalizedEntry>(settings.SegmentEntries);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "segments"));
        Directory.CreateDirectory(Path.Combine(_root, "source-order"));
        Directory.CreateDirectory(Path.Combine(_root, "diagnostics"));
        _sourceRecordsStream = new FileStream(
            Path.Combine(_root, "source-order", "records.bin"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        _sourceRecords = new BinaryWriter(_sourceRecordsStream);

        // Source records are variable length, so raw context could only be located by
        // reading the file from the start. This sidecar stores the byte offset of every
        // record, keyed by its (dense, monotonic) source sequence (§12.10).
        _sourceIndexStream = new FileStream(
            Path.Combine(_root, "source-order", "index.bin"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            256 * 1024,
            FileOptions.SequentialScan);
        _sourceIndex = new BinaryWriter(_sourceIndexStream);

        // Brief/untimed captures can contain tens of millions of entries. Stream the
        // JSON array as entries arrive instead of retaining every normalized record in
        // a List until finalization. A partial session may end with an incomplete array,
        // but its byte-faithful source records remain recoverable and the manifest
        // already marks that session as partial.
        _untimedStream = new FileStream(
            Path.Combine(_root, "source-order", "untimed.json"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            256 * 1024,
            FileOptions.SequentialScan);
        _untimedWriter = new Utf8JsonWriter(_untimedStream);
        _untimedWriter.WriteStartArray();
        InternTag(string.Empty);
        InternBuffer(string.Empty);
    }

    public string RootPath => _root;
    public long Generation => _generation;
    public IReadOnlyList<SegmentManifest> Segments => _segments;

    public SegmentManifest? AddEntry(NormalizedEntry entry)
    {
        ThrowIfFinalized();
        if (entry.Timestamp is null)
        {
            JsonSerializer.Serialize(_untimedWriter, entry, JsonOptions);
            return null;
        }

        _pending.Add(entry);
        if (_pending.Count >= _settings.SegmentEntries)
        {
            return FlushSegment();
        }

        // A live capture reaches the entry threshold only after minutes, so waiting for
        // it alone means no segment is published, no snapshot generation advances, and
        // the workspace shows nothing while data is plainly arriving (§10.6).
        //
        // The interval widens after each time-triggered flush so early segments appear
        // promptly without a long capture accumulating thousands of tiny ones — query
        // cost grows with live segment count (§12.4).
        if (Stopwatch.GetElapsedTime(_lastFlushTimestamp) < _flushInterval)
        {
            return null;
        }

        var flushed = FlushSegment();
        if (flushed is not null)
        {
            _flushInterval = _flushInterval < MaximumFlushInterval
                ? TimeSpan.FromTicks(Math.Min(_flushInterval.Ticks * 2, MaximumFlushInterval.Ticks))
                : MaximumFlushInterval;
        }

        return flushed;
    }

    public void AddOutcome(ParseOutcome outcome, long? entryId = null)
    {
        ThrowIfFinalized();
        WriteSourceRecord(new SourceRecord(
            outcome.Source.Sequence,
            outcome.Source.Raw,
            outcome.Kind,
            entryId,
            outcome.Reason));
    }

    public SegmentManifest? FlushSegment()
    {
        ThrowIfFinalized();
        if (_pending.Count == 0)
        {
            return null;
        }

        var manifest = SegmentWriter.Write(_root, _nextSegmentId++, _pending, InternTag, InternBuffer);
        _segments.Add(manifest);
        _pending.Clear();
        _generation++;
        _lastFlushTimestamp = Stopwatch.GetTimestamp();
        return manifest;
    }

    public void UpdateSourceIdentity(SourceIdentity source)
    {
        ThrowIfFinalized();
        _source = source;
    }

    public Task<SessionManifest> PublishSnapshotAsync(
        SessionDescriptor descriptor,
        IReadOnlyList<TemplateDefinition> templates,
        IReadOnlyList<ProcessNameRange> processNames,
        CancellationToken cancellationToken)
    {
        ThrowIfFinalized();
        var manifest = new SessionManifest(
            "2.0",
            descriptor with { Generation = _generation, Finalized = false },
            _source,
            _settings,
            "2",
            _settings.TemplateSettings.AlgorithmVersion,
            _generation,
            _segments.ToArray(),
            _tags.ToArray(),
            _buffers.ToArray(),
            templates,
            false,
            DateTimeOffset.UtcNow,
            processNames);
        return WriteManifestAsync(manifest, cancellationToken);
    }

    public async Task<SessionManifest> FinalizeAsync(
        SessionDescriptor descriptor,
        IReadOnlyList<TemplateDefinition> templates,
        IReadOnlyList<ProcessNameRange> processNames,
        CancellationToken cancellationToken)
    {
        ThrowIfFinalized();
        FlushSegment();
        _sourceRecords.Flush();
        await _sourceRecordsStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _sourceRecords.Dispose();
        _sourceRecordsStream.Dispose();
        _sourceIndex.Flush();
        await _sourceIndexStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _sourceIndex.Dispose();
        _sourceIndexStream.Dispose();
        _untimedWriter.WriteEndArray();
        await _untimedWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _untimedWriter.DisposeAsync().ConfigureAwait(false);
        await _untimedStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _untimedStream.DisposeAsync().ConfigureAwait(false);
        var obsoleteSegmentContainers = CompactSegments(cancellationToken);

        var manifest = new SessionManifest(
            "2.0",
            descriptor with { Generation = ++_generation, Finalized = true },
            _source,
            _settings,
            "2",
            _settings.TemplateSettings.AlgorithmVersion,
            _generation,
            _segments.ToArray(),
            _tags.ToArray(),
            _buffers.ToArray(),
            templates,
            true,
            DateTimeOffset.UtcNow,
            processNames);
        await AtomicJsonWriteAsync(Path.Combine(_root, "manifest.json"), manifest, true, cancellationToken).ConfigureAwait(false);
        _finalized = true;
        foreach (var obsolete in obsoleteSegmentContainers)
        {
            TryDeleteDirectory(obsolete);
        }

        return manifest;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finalized)
        {
            _sourceRecords.Dispose();
            await _sourceRecordsStream.DisposeAsync().ConfigureAwait(false);
            _sourceIndex.Dispose();
            await _sourceIndexStream.DisposeAsync().ConfigureAwait(false);
            await _untimedWriter.DisposeAsync().ConfigureAwait(false);
            await _untimedStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static async Task<SourceIdentity> CreateFileIdentityAsync(
        string path,
        bool embedded,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new SourceIdentity("file", embedded ? null : fullPath, info.Length, info.LastWriteTimeUtc, Convert.ToHexString(hash), embedded);
    }

    public static async Task EmbedRawAsync(string sourcePath, string sessionRoot, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(sessionRoot, "raw.log");
        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var target = new FileStream(destination + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(destination + ".tmp", destination, true);
    }

    private uint InternTag(string value) => Intern(value, _tagIds, _tags);
    private uint InternBuffer(string value) => Intern(value, _bufferIds, _buffers);

    private static uint Intern(string value, Dictionary<string, uint> ids, List<string> values)
    {
        if (ids.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var id = checked((uint)values.Count);
        ids.Add(value, id);
        values.Add(value);
        return id;
    }

    private void WriteSourceRecord(SourceRecord record)
    {
        _sourceIndex.Write(_sourceRecordsStream.Position);
        SourceRecordCodec.Write(_sourceRecords, record);
    }

    private string[] CompactSegments(CancellationToken cancellationToken)
    {
        if (_segments.Count <= 1 || AlreadyGloballySorted())
        {
            return [];
        }

        var previous = _segments.ToArray();
        var readers = previous.Select(segment => new SegmentSnapshot(_root, segment)).ToArray();
        var container = $"segments-final-{_generation + 1:D8}";
        var compacted = new List<SegmentManifest>();
        var queue = new PriorityQueue<(int Segment, int Index), (long Timestamp, long Sequence)>();
        try
        {
            for (var segment = 0; segment < readers.Length; segment++)
            {
                if (readers[segment].Count > 0)
                {
                    queue.Enqueue(
                        (segment, 0),
                        (readers[segment].TimestampAt(0), readers[segment].SequenceAt(0)));
                }
            }

            var batch = new List<NormalizedEntry>(_settings.SegmentEntries);
            while (queue.TryDequeue(out var position, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = readers[position.Segment];
                batch.Add(reader.ReadEntry(position.Index, Guid.Empty, _tags, _buffers, "2"));
                var next = position.Index + 1;
                if (next < reader.Count)
                {
                    queue.Enqueue((position.Segment, next), (reader.TimestampAt(next), reader.SequenceAt(next)));
                }

                if (batch.Count >= _settings.SegmentEntries)
                {
                    compacted.Add(SegmentWriter.Write(_root, compacted.Count + 1, batch, InternTag, InternBuffer, container));
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                compacted.Add(SegmentWriter.Write(_root, compacted.Count + 1, batch, InternTag, InternBuffer, container));
            }
        }
        catch
        {
            TryDeleteDirectory(Path.Combine(_root, container));
            throw;
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }

        _segments.Clear();
        _segments.AddRange(compacted);
        _generation++;
        return previous
            .Select(segment => segment.RelativePath.Split('/')[0])
            .Distinct(StringComparer.Ordinal)
            .Select(name => Path.Combine(_root, name))
            .ToArray();
    }

    /// <summary>
    /// A merge only earns its cost when segments overlap in time. Logs that arrive in
    /// order — the common case — already satisfy the global sort, so re-reading and
    /// re-writing every entry at finalization would double the work of the import for
    /// no change in query results (§10.4 step 6).
    /// </summary>
    private bool AlreadyGloballySorted()
    {
        for (var index = 1; index < _segments.Count; index++)
        {
            var previous = _segments[index - 1];
            var current = _segments[index];
            if (current.MinimumTimestampUs < previous.MaximumTimestampUs)
            {
                return false;
            }

            // Equal boundary timestamps still need source sequence to break the tie in
            // the same direction, or the merged order would differ from the split order.
            if (current.MinimumTimestampUs == previous.MaximumTimestampUs &&
                current.MinimumSequence < previous.MaximumSequence)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Serializes JSON to a temporary file and atomically publishes it.</summary>
    /// <param name="path">The final manifest path.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="durable">
    /// Whether to bypass the OS write cache. Only the finalized manifest needs that
    /// guarantee. A progressive manifest is explicitly marked unfinalized and is
    /// rewritten by the next published snapshot, so forcing it to physical media buys
    /// nothing a reader can observe — and it cost roughly 300 ms per publication on the
    /// commit thread, which was over half the wall time of a large import.
    /// Atomicity comes from the temporary-file rename either way (§11.7).
    /// </param>
    /// <param name="cancellationToken">Cancels serialization and publication.</param>
    private static async Task AtomicJsonWriteAsync<T>(
        string path,
        T value,
        bool durable,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        var options = FileOptions.Asynchronous | (durable ? FileOptions.WriteThrough : FileOptions.None);
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         options))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, true);
    }

    private async Task<SessionManifest> WriteManifestAsync(SessionManifest manifest, CancellationToken cancellationToken)
    {
        await AtomicJsonWriteAsync(Path.Combine(_root, "manifest.json"), manifest, false, cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private void ThrowIfFinalized()
    {
        if (_finalized)
        {
            throw new InvalidOperationException("The session store is already finalized.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
            // Active progressive snapshots can keep old mappings alive on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // The finalized manifest no longer references the obsolete container.
        }
    }
}
