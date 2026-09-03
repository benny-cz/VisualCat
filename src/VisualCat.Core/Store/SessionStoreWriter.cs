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
    // segments (compaction folds them away) for a far shorter visible gap (§10.6, §12.4).
    private static readonly TimeSpan MaximumFlushInterval = TimeSpan.FromSeconds(4);

    // With nobody watching there is no plot to keep continuous, so the same trade runs
    // the other way: a relaxed ceiling costs an unwatched viewer nothing and saves the
    // sealing, hashing, and manifest rewrite that a screen-off capture would otherwise
    // repeat every four seconds for hours. Returning to the foreground publishes at once
    // rather than waiting this out.
    //
    // The cost is the crash window: entries live in memory until a segment is sealed, so
    // a process killed while backgrounded loses up to this much of its tail instead of up
    // to four seconds. That is the right way round for an unattended capture, where the
    // alternative is spending the night writing segments no one will look at.
    private static readonly TimeSpan UnobservedFlushInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many segments at the same merge level trigger a merge, and therefore how many
    /// segments any one level can hold. See <see cref="CompactLiveSegments"/>.
    /// </summary>
    private const int MergeFactor = 8;

    /// <summary>Marks a segment that has grown past <see cref="IngestSettings.SegmentEntries"/> and left the merge ladder.</summary>
    private const int FullLevel = int.MaxValue;

    /// <summary>
    /// How many consecutive failed attempts to seal a segment, and how many segments'
    /// worth of entries held in memory, before a storage failure stops being treated as
    /// transient. See <see cref="TryFlushSegment"/>.
    /// </summary>
    private const int DeferredFlushAttemptCeiling = 32;
    private const int DeferredFlushEntryCeiling = 4;

    /// <summary>Flushes to skip compaction for after a failed round.</summary>
    private const int CompactionCooldownFlushes = 16;

    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    // Progressive manifests are machine-read, superseded within seconds, and rewritten in
    // full every time. Indenting one cost roughly a third of its bytes on every
    // publication for a form no one reads; the finalized manifest stays indented because
    // it is written once and is the copy a person may open.
    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private static readonly byte[] Newline = [(byte)'\n'];

    private readonly string _root;

    /// <summary>Serializes manifest rewrites; a progressive publish and the finalize can
    /// otherwise be in flight over the same file at once.</summary>
    private readonly SemaphoreSlim _manifestWriteLock = new(1, 1);
    private readonly IngestSettings _settings;
    private readonly LiveViewerPresence? _presence;
    private SourceIdentity _source;
    private readonly List<NormalizedEntry> _pending;
    private readonly List<LiveSegment> _segments = [];

    /// <summary>
    /// Segment directories compaction has folded into a larger segment, each recorded
    /// with the generation at which it left the manifest.
    /// </summary>
    /// <remarks>
    /// Deleting one is neither immediate nor demanded. It waits until a manifest that
    /// omits it has actually been published, so a reader cannot be handed a manifest
    /// naming a directory that is already gone; and a delete that fails is kept for the
    /// next round, because a reader that opened the previous manifest may still hold the
    /// directory mapped and Windows refuses the delete outright while it does.
    /// </remarks>
    private readonly Dictionary<string, long> _obsoleteSegmentDirectories = new(StringComparer.Ordinal);
    private long _publishedGeneration = -1;
    private readonly Dictionary<string, uint> _tagIds = new(StringComparer.Ordinal);
    private readonly List<string> _tags = [];
    private readonly Dictionary<string, uint> _bufferIds = new(StringComparer.Ordinal);
    private readonly List<string> _buffers = [];
    private readonly FileStream _sourceRecordsStream;
    private readonly BinaryWriter _sourceRecords;
    private readonly FileStream _sourceIndexStream;
    private readonly BinaryWriter _sourceIndex;
    private readonly FileStream _templateStream;
    private readonly Dictionary<uint, TemplateDefinition> _writtenTemplateDefinitions = [];
    private int _nextSegmentId = 1;
    private long _generation;
    private bool _finalized;
    private long _lastFlushTimestamp = Stopwatch.GetTimestamp();
    private TimeSpan _flushInterval = InitialFlushInterval;
    private bool _wasWatching = true;
    private long _compactedSegments;
    private long _compactionFailures;
    private int _compactionCooldown;
    private int _consecutiveFlushFailures;
    private string? _lastFlushFailure;

    public SessionStoreWriter(string root, IngestSettings settings, SourceIdentity source)
        : this(root, settings, source, null)
    {
    }

    public SessionStoreWriter(
        string root,
        IngestSettings settings,
        SourceIdentity source,
        LiveViewerPresence? presence)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _root = Path.GetFullPath(root);
        _settings = settings;
        _source = source;
        _presence = presence;
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

        // Definitions are mutable while their cluster keeps matching, so the sidecar is
        // a revision log rather than a one-record-per-id table. A manifest commits a byte
        // prefix; readers fold that prefix by id and ignore later live revisions.
        _templateStream = new FileStream(
            Path.Combine(_root, TemplateTable.FileName),
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            256 * 1024,
            FileOptions.SequentialScan);
        InternTag(string.Empty);
        InternBuffer(string.Empty);
    }

    public string RootPath => _root;
    public long Generation => _generation;
    public IReadOnlyList<SegmentManifest> Segments =>
        _segments.Select(static segment => segment.Manifest).ToArray();

    /// <summary>Gets how many segments the live session currently holds.</summary>
    public int SegmentCount => _segments.Count;

    /// <summary>Gets how many segments compaction has folded away over this capture.</summary>
    public long CompactedSegments => _compactedSegments;

    /// <summary>Gets how many compaction rounds failed and were skipped.</summary>
    public long CompactionFailures => _compactionFailures;

    /// <summary>
    /// Gets the message of the storage failure the writer is currently working through,
    /// or null when sealing segments is healthy. Entries are retained in memory
    /// meanwhile, so a capture reporting this has lost nothing yet.
    /// </summary>
    public string? DeferredFlushFailure => _consecutiveFlushFailures > 0 ? _lastFlushFailure : null;

    /// <summary>Gets how many entries are captured but not yet sealed into a segment.</summary>
    public int PendingEntryCount => _pending.Count;

    public SegmentManifest? AddEntry(NormalizedEntry entry)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Timestamp is null)
        {
            return null;
        }

        _pending.Add(entry);

        // While sealing is failing, every attempt costs the creation of twenty-six files
        // and fails again for the same reason. Back off to the flush interval instead of
        // retrying on each arriving entry; the entries themselves are retained either way.
        // A fast source can still fill the retention ceiling inside one backoff, and at
        // that point the attempt must be made so the failure can be raised rather than
        // absorbed into unbounded memory.
        if (_consecutiveFlushFailures > 0 &&
            _pending.Count <= checked(_settings.SegmentEntries * DeferredFlushEntryCeiling) &&
            Stopwatch.GetElapsedTime(_lastFlushTimestamp) < _flushInterval)
        {
            return null;
        }

        if (_pending.Count >= _settings.SegmentEntries)
        {
            return TryFlushSegment();
        }

        // Returning to the foreground must not make the user wait out the relaxed
        // interval that applied while they were away: publish what accumulated now, and
        // restart the ramp so the next few seconds feel as live as the first ones did.
        var watching = _presence?.IsWatching ?? true;
        if (watching != _wasWatching)
        {
            _wasWatching = watching;
            if (watching)
            {
                _flushInterval = InitialFlushInterval;
                if (TryFlushSegment() is { } resumed)
                {
                    return resumed;
                }
            }
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

        var flushed = TryFlushSegment();
        if (flushed is not null)
        {
            var ceiling = watching ? MaximumFlushInterval : UnobservedFlushInterval;
            _flushInterval = _flushInterval < ceiling
                ? TimeSpan.FromTicks(Math.Min(_flushInterval.Ticks * 2, ceiling.Ticks))
                : ceiling;
        }

        return flushed;
    }

    /// <summary>
    /// Seals a segment, treating a storage failure as something to work through rather
    /// than something to end the capture with.
    /// </summary>
    /// <remarks>
    /// The entries stay in <see cref="_pending"/> when sealing fails, so nothing is lost
    /// and the next attempt writes them. That matters because the conditions that stop a
    /// segment being written — a descriptor shortage, a handle another process is holding,
    /// a momentarily unwritable volume — usually clear within seconds, and a live capture
    /// killed by one of them takes hours of irreplaceable log with it. Persisting past
    /// <see cref="DeferredFlushAttemptCeiling"/> attempts, or past
    /// <see cref="DeferredFlushEntryCeiling"/> segments' worth of retained entries, is a
    /// different condition — a full disk, a removed volume — and is raised to the caller
    /// rather than absorbed into unbounded memory (§10.8).
    /// </remarks>
    private SegmentManifest? TryFlushSegment()
    {
        try
        {
            var flushed = FlushSegment();
            _consecutiveFlushFailures = 0;
            _lastFlushFailure = null;
            return flushed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _consecutiveFlushFailures++;
            _lastFlushFailure = exception.Message;
            _lastFlushTimestamp = Stopwatch.GetTimestamp();
            if (_consecutiveFlushFailures > DeferredFlushAttemptCeiling ||
                _pending.Count > checked(_settings.SegmentEntries * DeferredFlushEntryCeiling))
            {
                // Reported as its own condition rather than as whichever write happened
                // to be the last to fail: after a long capture the only questions worth
                // answering are how much was lost and whether the rest survived.
                throw new SegmentWriteRefusedException(
                    $"Storage refused {_consecutiveFlushFailures} attempts in a row to save a log segment, so the capture " +
                    $"stopped. The {_pending.Count:N0} entries captured since the last successful save could not be " +
                    "written; everything saved before then is intact and the session is still open.",
                    _consecutiveFlushFailures,
                    _pending.Count,
                    exception);
            }

            return null;
        }
    }

    public void AddOutcome(ParseOutcome outcome, long? entryId = null)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(outcome);
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
        _segments.Add(new LiveSegment(manifest, 0));
        _pending.Clear();
        _generation++;
        _lastFlushTimestamp = Stopwatch.GetTimestamp();
        CompactLiveSegments();
        return manifest;
    }

    public void UpdateSourceIdentity(SourceIdentity source)
    {
        ThrowIfFinalized();
        _source = source;
    }

    public async Task<SessionManifest> PublishSnapshotAsync(
        SessionDescriptor descriptor,
        IReadOnlyList<TemplateDefinition> templates,
        IReadOnlyList<ProcessNameRange> processNames,
        CancellationToken cancellationToken)
    {
        ThrowIfFinalized();

        // The manifest is the publication boundary. Everything it makes queryable must
        // be visible to concurrent readers before the atomic manifest replacement, or a
        // live entry can exist while its source record is still trapped in this writer's
        // user-space buffer.
        _sourceRecords.Flush();
        await _sourceRecordsStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _sourceIndex.Flush();
        await _sourceIndexStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var manifest = new SessionManifest(
            "2.0",
            descriptor with { Generation = _generation, Finalized = false },
            _source,
            _settings,
            "2",
            _settings.TemplateSettings.AlgorithmVersion,
            _generation,
            Segments,
            _tags.ToArray(),
            _buffers.ToArray(),
            [],
            false,
            DateTimeOffset.UtcNow,
            processNames);
        manifest = await WritePublicationAsync(
            manifest,
            templates,
            durable: false,
            indented: false,
            cancellationToken).ConfigureAwait(false);
        _publishedGeneration = _generation;
        return manifest;
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
        var obsoleteSegmentContainers = CompactSegments(cancellationToken);

        var manifest = new SessionManifest(
            "2.0",
            descriptor with { Generation = ++_generation, Finalized = true },
            _source,
            _settings,
            "2",
            _settings.TemplateSettings.AlgorithmVersion,
            _generation,
            Segments,
            _tags.ToArray(),
            _buffers.ToArray(),
            [],
            true,
            DateTimeOffset.UtcNow,
            processNames);
        manifest = await WriteFinalPublicationAsync(manifest, templates, cancellationToken).ConfigureAwait(false);
        _finalized = true;
        foreach (var obsolete in obsoleteSegmentContainers)
        {
            TryDeleteDirectory(obsolete);
        }

        // Directories compaction retired while readers still held them mapped. The
        // finalized manifest is on disk, so nothing can be sent to them any more;
        // anything still locked is left for the retention sweep rather than failing the
        // save.
        _publishedGeneration = _generation;
        DrainObsoleteSegments(force: true);
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
            await _templateStream.DisposeAsync().ConfigureAwait(false);
        }

        _manifestWriteLock.Dispose();
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

    /// <summary>
    /// Folds small live segments into larger ones so the number of segments in a session
    /// tracks how much was captured rather than how long the capture ran.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quiet source is flushed by the time ceiling, not by the entry threshold, so
    /// segments arrive at a steady rate for as long as the capture lasts — a few hundred
    /// entries an hour still produced roughly nine hundred segments an hour. Every one of
    /// them costs descriptors and mappings in each reader, a heat-map pass per query
    /// (§12.4), an entry in every republished manifest, and a directory of twenty-six
    /// mostly-empty files on disk. Left to accumulate, a capture that ran a little over an
    /// hour exhausted the process descriptor limit and killed itself.
    /// </para>
    /// <para>
    /// Merging is a base-<see cref="MergeFactor"/> counter over an explicit merge level
    /// rather than over segment size: a run of <see cref="MergeFactor"/> adjacent segments
    /// at level L becomes one segment at level L+1, which can then only merge with its own
    /// kind. That is what keeps the total work logarithmic — a size-driven rule instead
    /// re-merges the growing oldest segment with each new small one and rewrites the
    /// session over and over. Only adjacent runs merge, so the segment order the query
    /// engine relies on is preserved, and a segment that reaches
    /// <see cref="IngestSettings.SegmentEntries"/> leaves the ladder entirely so no merge
    /// ever costs more than writing one full-sized segment.
    /// </para>
    /// <para>
    /// At most <see cref="MergeFactor"/>-1 segments survive per level, so a session holds
    /// on the order of <c>MergeFactor × log(entries)</c> segments no matter how long the
    /// capture runs. Failure is not fatal: the capture keeps its data and simply carries
    /// more segments than it would have.
    /// </para>
    /// </remarks>
    private void CompactLiveSegments()
    {
        DrainObsoleteSegments();

        // A round that failed will pick the same run again and fail the same way, having
        // first read up to a full segment's worth of entries to get there. Standing down
        // for a few flushes keeps a persistent fault from turning every flush into that
        // wasted read.
        if (_compactionCooldown > 0)
        {
            _compactionCooldown--;
            return;
        }

        try
        {
            while (TrySelectMergeRun(out var start, out var length))
            {
                MergeRun(start, length);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Compaction is an optimisation. Losing a round costs a larger segment
            // count; failing the capture would cost the user their log.
            _compactionFailures++;
            _compactionCooldown = CompactionCooldownFlushes;
        }
    }

    private bool TrySelectMergeRun(out int start, out int length)
    {
        var index = 0;
        while (index < _segments.Count)
        {
            var level = _segments[index].Level;
            if (level == FullLevel)
            {
                index++;
                continue;
            }

            var runEnd = index;
            while (runEnd < _segments.Count && _segments[runEnd].Level == level)
            {
                runEnd++;
            }

            if (runEnd - index < MergeFactor)
            {
                index = runEnd;
                continue;
            }

            long total = 0;
            var take = 0;
            while (take < MergeFactor &&
                   index + take < runEnd &&
                   total + _segments[index + take].Manifest.EntryCount <= _settings.SegmentEntries)
            {
                total += _segments[index + take].Manifest.EntryCount;
                take++;
            }

            if (take >= 2)
            {
                start = index;
                length = take;
                return true;
            }

            // The oldest segment of this run already fills a segment on its own, so it
            // leaves the ladder instead of blocking the run behind it forever.
            _segments[index] = _segments[index] with { Level = FullLevel };
        }

        start = 0;
        length = 0;
        return false;
    }

    private void MergeRun(int start, int length)
    {
        var sources = _segments.GetRange(start, length);
        var capacity = sources.Sum(static segment => segment.Manifest.EntryCount);
        var merged = new List<NormalizedEntry>(capacity);
        var readers = new List<SegmentSnapshot>(length);
        try
        {
            foreach (var source in sources)
            {
                readers.Add(new SegmentSnapshot(_root, source.Manifest));
            }

            foreach (var reader in readers)
            {
                for (var index = 0; index < reader.Count; index++)
                {
                    merged.Add(reader.ReadEntry(index, Guid.Empty, _tags, _buffers, "2"));
                }
            }
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }

        var id = _nextSegmentId++;
        SegmentManifest replacement;
        try
        {
            // SegmentWriter sorts what it is handed, so a run of individually sorted
            // segments needs no merge of its own here.
            replacement = SegmentWriter.Write(_root, id, merged, InternTag, InternBuffer);
        }
        catch
        {
            TryDeleteDirectory(Path.Combine(_root, "segments", id.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)));
            throw;
        }

        _segments.RemoveRange(start, length);
        _segments.Insert(start, new LiveSegment(replacement, sources[0].Level + 1));
        _generation++;
        _compactedSegments += length;
        foreach (var source in sources)
        {
            _obsoleteSegmentDirectories[
                Path.GetFullPath(Path.Combine(_root, source.Manifest.RelativePath.Replace('/', Path.DirectorySeparatorChar)))] = _generation;
        }
    }

    /// <summary>
    /// Deletes retired segment directories that a published manifest has already stopped
    /// referencing. Anything still held open is left for a later round.
    /// </summary>
    private void DrainObsoleteSegments(bool force = false)
    {
        if (_obsoleteSegmentDirectories.Count == 0)
        {
            return;
        }

        List<string>? deleted = null;
        foreach (var (directory, retiredAt) in _obsoleteSegmentDirectories)
        {
            // Until a manifest omitting this directory has actually reached disk, a
            // reader can still be told to open it. Publication can fail and be retried,
            // so this waits for the publication rather than assuming it.
            if (!force && _publishedGeneration < retiredAt)
            {
                continue;
            }

            if (TryDeleteDirectory(directory))
            {
                (deleted ??= []).Add(directory);
            }
        }

        foreach (var directory in deleted ?? [])
        {
            _obsoleteSegmentDirectories.Remove(directory);
        }
    }

    private string[] CompactSegments(CancellationToken cancellationToken)
    {
        if (_segments.Count <= 1 || AlreadyGloballySorted())
        {
            return [];
        }

        var previous = _segments.Select(static segment => segment.Manifest).ToArray();
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
        _segments.AddRange(compacted.Select(static segment => new LiveSegment(segment, FullLevel)));
        _generation++;

        // Everything the incremental ladder retired lives in a container the final pass
        // is about to remove wholesale, so it no longer needs individual retries.
        _obsoleteSegmentDirectories.Clear();
        return previous
            .Select(static segment => segment.RelativePath.Split('/')[0])
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
            var previous = _segments[index - 1].Manifest;
            var current = _segments[index].Manifest;
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

    /// <summary>Appends template revisions and atomically commits their boundary in a manifest.</summary>
    /// <param name="manifest">The manifest to publish.</param>
    /// <param name="definitions">Template revisions since the previous successful publication.</param>
    /// <param name="durable">
    /// Whether to bypass the OS write cache. Only the finalized manifest needs that
    /// guarantee. A progressive manifest is explicitly marked unfinalized and is
    /// rewritten by the next published snapshot, so forcing it to physical media buys
    /// nothing a reader can observe — and it cost roughly 300 ms per publication on the
    /// commit thread, which was over half the wall time of a large import.
    /// Atomicity comes from the temporary-file rename either way (§11.7).
    /// </param>
    /// <param name="indented">Whether to write human-readable JSON.</param>
    /// <param name="cancellationToken">Cancels serialization and publication.</param>
    private async Task<SessionManifest> WritePublicationAsync(
        SessionManifest manifest,
        IReadOnlyList<TemplateDefinition> definitions,
        bool durable,
        bool indented,
        CancellationToken cancellationToken)
    {
        // The lock covers the sidecar append and the manifest replacement as one
        // publication operation. A finalization racing a progressive publish must not
        // commit the other operation's template boundary.
        await _manifestWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var appendStart = _templateStream.Position;
            var appended = new List<TemplateDefinition>();
            try
            {
                foreach (var definition in definitions.OrderBy(static value => value.TemplateId))
                {
                    if (_writtenTemplateDefinitions.TryGetValue(definition.TemplateId, out var previous) &&
                        ReferenceEquals(previous, definition))
                    {
                        continue;
                    }

                    var bytes = JsonSerializer.SerializeToUtf8Bytes(definition, CompactJsonOptions);
                    await _templateStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await _templateStream.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
                    appended.Add(definition);
                }

                await _templateStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (durable)
                {
                    _templateStream.Flush(flushToDisk: true);
                }
            }
            catch
            {
                _templateStream.SetLength(appendStart);
                _templateStream.Position = appendStart;
                throw;
            }

            foreach (var definition in appended)
            {
                _writtenTemplateDefinitions[definition.TemplateId] = definition;
            }

            var templateCount = _writtenTemplateDefinitions.Count;
            if (templateCount > 0 &&
                (_writtenTemplateDefinitions.Keys.Min() != 1 ||
                 _writtenTemplateDefinitions.Keys.Max() != templateCount))
            {
                throw new InvalidDataException("Template definitions must use contiguous ids beginning at one.");
            }

            manifest = manifest with
            {
                Descriptor = manifest.Descriptor with
                {
                    Counters = manifest.Descriptor.Counters with { Templates = templateCount },
                },
                TemplateSidecarLength = _templateStream.Position,
                SessionSizeBytes = EstimateSessionSize(_templateStream.Position),
            };
            await AtomicJsonWriteCoreAsync(
                Path.Combine(_root, "manifest.json"),
                manifest,
                durable,
                indented,
                cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        finally
        {
            _manifestWriteLock.Release();
        }
    }

    /// <summary>
    /// Publishes the finalized manifest against a compacted template file.
    /// </summary>
    /// <remarks>
    /// A live capture appends a record whenever a template's shape changes, so the file
    /// it grows holds superseded revisions that a reader folds by id and discards. On a
    /// real device capture that was 18,984 records for 13,057 templates. Finalization
    /// writes each template once into a file of its own and names that in the manifest,
    /// which costs a finished session neither the bytes nor the parse. The live file is
    /// removed only after the finalized manifest is durably in place, so a crash between
    /// the two leaves the previous manifest pointing at a file that is still intact.
    /// </remarks>
    private async Task<SessionManifest> WriteFinalPublicationAsync(
        SessionManifest manifest,
        IReadOnlyList<TemplateDefinition> definitions,
        CancellationToken cancellationToken)
    {
        await _manifestWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ordered = definitions.OrderBy(static value => value.TemplateId).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].TemplateId != (uint)index + 1)
                {
                    throw new InvalidDataException("Template definitions must use contiguous ids beginning at one.");
                }
            }

            // Mining can be off, and then there is nothing to compact and no reason to
            // leave an empty file named by the manifest.
            long compactLength = 0;
            string? compactName = null;
            if (ordered.Length > 0)
            {
                compactName = TemplateTable.FinalFileName;
                await using var compact = new FileStream(
                    Path.Combine(_root, compactName),
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    256 * 1024,
                    FileOptions.SequentialScan);
                foreach (var definition in ordered)
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(definition, CompactJsonOptions);
                    await compact.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await compact.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
                }

                await compact.FlushAsync(cancellationToken).ConfigureAwait(false);
                compact.Flush(flushToDisk: true);
                compactLength = compact.Position;
            }

            await _templateStream.DisposeAsync().ConfigureAwait(false);
            manifest = manifest with
            {
                Descriptor = manifest.Descriptor with
                {
                    Counters = manifest.Descriptor.Counters with { Templates = ordered.Length },
                },
                TemplateSidecarLength = compactLength,
                TemplateSidecarName = compactName,
                SessionSizeBytes = EstimateSessionSize(compactLength),
            };
            await AtomicJsonWriteCoreAsync(
                Path.Combine(_root, "manifest.json"),
                manifest,
                durable: true,
                indented: true,
                cancellationToken).ConfigureAwait(false);
            TryDeleteFile(Path.Combine(_root, TemplateTable.FileName));
            return manifest;
        }
        finally
        {
            _manifestWriteLock.Release();
        }
    }

    private long EstimateSessionSize(long templateBytes)
    {
        long size = _segments.Sum(static segment => segment.Manifest.SizeBytes ?? 0);
        foreach (var relative in new[]
                 {
                     Path.Combine("source-order", "records.bin"),
                     Path.Combine("source-order", "index.bin"),
                 })
        {
            var info = new FileInfo(Path.Combine(_root, relative));
            if (info.Exists)
            {
                size = checked(size + info.Length);
            }
        }

        size = checked(size + templateBytes);
        if (_source.Embedded)
        {
            var raw = new FileInfo(Path.Combine(_root, "raw.log"));
            if (raw.Exists)
            {
                size = checked(size + raw.Length);
            }
        }

        return size;
    }

    private static async Task AtomicJsonWriteCoreAsync<T>(
        string path,
        T value,
        bool durable,
        bool indented,
        CancellationToken cancellationToken)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = FileOptions.Asynchronous | (durable ? FileOptions.WriteThrough : FileOptions.None);
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             options))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    indented ? IndentedJsonOptions : CompactJsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await ReplaceWithRetryAsync(temporary, path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A half-written temporary is worthless and would otherwise accumulate in the
            // session directory, one per failed publication.
            TryDeleteFile(temporary);
            throw;
        }
    }

    /// <summary>
    /// Replaces <paramref name="path"/> atomically, tolerating a destination that is
    /// momentarily held.
    ///
    /// Replacing a file on Windows fails outright while any handle to it lacks delete
    /// sharing, and a live session's manifest is read by whoever is watching the capture
    /// as well as by the scanners and indexers that run over a directory being written.
    /// The window is milliseconds, but losing it failed the entire ingest, so a brief
    /// retry is worth far more than the certainty of giving up first time.
    /// </summary>
    private static async Task ReplaceWithRetryAsync(
        string temporary,
        string path,
        CancellationToken cancellationToken)
    {
        // Search indexers and real-time antivirus can retain a just-published manifest
        // noticeably longer than a normal reader. The previous 420 ms linear budget still
        // lost an otherwise complete import on a real workstation. Exponential backoff
        // keeps the common sharing violation cheap while allowing about five seconds for
        // an external scanner to release the file. Cancellation remains prompt throughout.
        const int attempts = 17;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, path, true);
                return;
            }
            catch (Exception exception) when (
                attempt < attempts && exception is UnauthorizedAccessException or IOException)
            {
                var delayMilliseconds = 25 * (1 << Math.Min(attempt - 1, 4));
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfFinalized()
    {
        if (_finalized)
        {
            throw new InvalidOperationException("The session store is already finalized.");
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }

            return true;
        }
        catch (IOException)
        {
            // Active progressive snapshots can keep old mappings alive on Windows.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The published manifest no longer references this directory.
            return false;
        }
    }

    /// <summary>A published segment together with the merge level that governs when it is folded into a larger one.</summary>
    private readonly record struct LiveSegment(SegmentManifest Manifest, int Level);
}
