using System.Text;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Store;

/// <summary>
/// A read view over one immutable segment directory.
/// </summary>
/// <remarks>
/// <para>
/// Instances are reference counted so consecutive snapshots of the same session can share
/// them. A live capture republishes its manifest every few seconds; re-opening every
/// segment for each republication meant a session briefly held two complete sets of
/// mappings, which is what exhausted the process descriptor limit during a long capture
/// (§10.6, §12.4). <see cref="TryAddReference"/> is taken by each session snapshot that
/// lists the segment, and the mappings close when the last one is released.
/// </para>
/// <para>
/// Columns are mapped on first use rather than in the constructor. The heat map — the one
/// query that visits every segment in a session — reads timestamps and the severity
/// bitmaps and nothing else, and it skips segments outside the viewport entirely, so a
/// segment nobody is looking at costs no descriptor at all. The constructor still
/// validates that the segment's timestamp column exists and has the length its manifest
/// entry declares, so a missing or truncated segment is still refused when the session is
/// opened rather than mid-query.
/// </para>
/// </remarks>
public sealed class SegmentSnapshot : IDisposable
{
    private const int FilterCacheCapacity = 64;

    private readonly MappedColumn?[] _columns = new MappedColumn?[SegmentFileContract.ColumnCount];
    private readonly Lock _columnLock = new();
    private readonly Dictionary<string, (RankBitmap Bitmap, LinkedListNode<string> Node)> _filterCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _filterLru = [];
    private readonly Lock _filterLock = new();
    private Dictionary<LogLevel, RankBitmap>? _severity;
    private int _references = 1;
    private bool _disposed;

    public SegmentSnapshot(string sessionRoot, SegmentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Manifest = manifest;
        DirectoryPath = Path.GetFullPath(Path.Combine(sessionRoot, manifest.RelativePath));
        EnsureWithin(sessionRoot, DirectoryPath);
        EnsureNoReparsePoints(sessionRoot, DirectoryPath);

        // Opening a session must still fail on a segment that is absent or the wrong
        // size, and it must do so without retaining a descriptor for every segment in
        // the session. A stat of the column every query needs answers both.
        var timestampName = SegmentFileContract.NameOf(SegmentFileContract.Column.Timestamp);
        var timestamp = new FileInfo(Path.Combine(DirectoryPath, timestampName));
        if (!timestamp.Exists)
        {
            throw new FileNotFoundException($"Segment column is missing: {manifest.RelativePath}/{timestampName}", timestamp.FullName);
        }

        if (timestamp.Length != checked((long)SegmentFileContract.ElementSizeOf(SegmentFileContract.Column.Timestamp) * manifest.EntryCount))
        {
            throw new InvalidDataException($"Column length mismatch: {timestamp.FullName}");
        }
    }

    public SegmentManifest Manifest { get; }
    public string DirectoryPath { get; }
    public int Count => Manifest.EntryCount;
    public InstantUs MinimumTimestamp => new(Manifest.MinimumTimestampUs);
    public InstantUs MaximumTimestamp => new(Manifest.MaximumTimestampUs);

    /// <summary>Gets the number of column files currently mapped, for diagnostics.</summary>
    internal int MappedColumnCount
    {
        get
        {
            var mapped = 0;
            for (var index = 0; index < _columns.Length; index++)
            {
                if (Volatile.Read(ref _columns[index]) is not null)
                {
                    mapped++;
                }
            }

            return mapped;
        }
    }

    public IReadOnlyDictionary<LogLevel, RankBitmap> SeverityBitmaps
    {
        get
        {
            var existing = Volatile.Read(ref _severity);
            if (existing is not null)
            {
                return existing;
            }

            lock (_columnLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_severity is { } raced)
                {
                    return raced;
                }

                var loaded = new Dictionary<LogLevel, RankBitmap>();
                foreach (var level in LogLevels.StorageOrder)
                {
                    loaded[level] = RankBitmap.Load(Path.Combine(DirectoryPath, "bitmaps", $"level-{(byte)level}.rbm"));
                }

                Volatile.Write(ref _severity, loaded);
                return loaded;
            }
        }
    }

    /// <summary>
    /// Claims a share of this segment for another session snapshot, or reports that the
    /// segment has already been released and the caller must open its own.
    /// </summary>
    internal bool TryAddReference()
    {
        var current = Volatile.Read(ref _references);
        while (current > 0)
        {
            var seen = Interlocked.CompareExchange(ref _references, current + 1, current);
            if (seen == current)
            {
                return true;
            }

            current = seen;
        }

        return false;
    }

    public long TimestampAt(int index) => Column(SegmentFileContract.Column.Timestamp).ReadInt64(index);
    public long SequenceAt(int index) => Column(SegmentFileContract.Column.Sequence).ReadInt64(index);
    public int PidAt(int index) => Column(SegmentFileContract.Column.Pid).ReadInt32(index);
    public int TidAt(int index) => Column(SegmentFileContract.Column.Tid).ReadInt32(index);
    public LogLevel LevelAt(int index) => (LogLevel)Column(SegmentFileContract.Column.Level).ReadByte(index);
    public uint TagIdAt(int index) => Column(SegmentFileContract.Column.Tag).ReadUInt32(index);
    public uint TemplateIdAt(int index) => Column(SegmentFileContract.Column.Template).ReadUInt32(index);
    public uint BufferIdAt(int index) => Column(SegmentFileContract.Column.Buffer).ReadUInt32(index);
    public EntryAttributes AttributesAt(int index) => (EntryAttributes)Column(SegmentFileContract.Column.Flags).ReadUInt16(index);

    public string MessageAt(int index) => ReadPayload(
        Column(SegmentFileContract.Column.MessageOffset).ReadInt64(index),
        Column(SegmentFileContract.Column.MessageLength).ReadInt32(index));

    public int LowerBound(long timestampUs)
    {
        var timestamps = Column(SegmentFileContract.Column.Timestamp);
        var low = 0;
        var high = Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (timestamps.ReadInt64(middle) < timestampUs)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Lower bound restricted to <c>[from, Count)</c>, located by galloping outward from
    /// <paramref name="from"/> instead of bisecting the whole segment.
    /// </summary>
    /// <param name="timestampUs">The first timestamp value the result may equal or exceed.</param>
    /// <param name="from">
    /// An index known to satisfy <c>timestamp[j] &lt; timestampUs</c> for every
    /// <c>j &lt; from</c> — in practice the bound returned for the previous, smaller
    /// boundary. Passing 0 is always sound and degrades to a plain binary search.
    /// </param>
    /// <remarks>
    /// The heat map asks each segment for thousands of strictly increasing boundaries,
    /// and consecutive boundaries land a few entries apart. Bisecting <c>[0, Count)</c>
    /// every time costs log2(Count) scattered reads over a memory-mapped column per
    /// boundary; resuming where the previous boundary ended costs a handful of
    /// sequential ones, which is what makes the per-column cost of §12.4 hold as the
    /// segment grows.
    /// </remarks>
    public int LowerBoundFrom(long timestampUs, int from)
    {
        var timestamps = Column(SegmentFileContract.Column.Timestamp);
        var low = Math.Clamp(from, 0, Count);
        if (low >= Count || timestamps.ReadInt64(low) >= timestampUs)
        {
            return low;
        }

        // timestamp[low] < timestampUs, so the answer is strictly above low. Double the
        // stride until it overshoots, which bounds the region the bisection below scans.
        var step = 1;
        while (low + step < Count && timestamps.ReadInt64(low + step) < timestampUs)
        {
            low += step;
            step <<= 1;
        }

        var high = Math.Min(Count, low + step);
        low++;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (timestamps.ReadInt64(middle) < timestampUs)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    public RankBitmap GetOrCreateFilter(string key, Func<int, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return GetOrCreateBitmap(key, () => RankBitmap.FromPredicate(Count, predicate));
    }

    /// <summary>
    /// Caches a derived bitmap. Composed bitmaps (active AND severity) go through here
    /// so they are built with word-wise Boolean operations rather than an index
    /// predicate, which is the difference between one pass per 64 entries and one
    /// delegate call per entry on the interactive heat-map path (§12.3, §12.4).
    /// </summary>
    public RankBitmap GetOrCreateBitmap(string key, Func<RankBitmap> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_filterLock)
        {
            if (_filterCache.TryGetValue(key, out var existing))
            {
                _filterLru.Remove(existing.Node);
                _filterLru.AddFirst(existing.Node);
                return existing.Bitmap;
            }
        }

        var created = factory();
        lock (_filterLock)
        {
            if (_filterCache.TryGetValue(key, out var raced))
            {
                return raced.Bitmap;
            }

            var node = _filterLru.AddFirst(key);
            _filterCache.Add(key, (created, node));
            // One filter occupies eight entries: its active bitmap plus one composed
            // bitmap per severity row. A bound of 16 let two filters evict each other
            // on every alternation, so hold a handful of recent filters instead.
            while (_filterCache.Count > FilterCacheCapacity && _filterLru.Last is { } last)
            {
                _filterCache.Remove(last.Value);
                _filterLru.RemoveLast();
            }

            return created;
        }
    }

    public NormalizedEntry ReadEntry(
        int index,
        Guid sessionId,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> buffers,
        string parserVersion)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(buffers);
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var tagId = Column(SegmentFileContract.Column.Tag).ReadUInt32(index);
        var bufferId = Column(SegmentFileContract.Column.Buffer).ReadUInt32(index);
        if (tagId >= tags.Count || bufferId >= buffers.Count)
        {
            throw new InvalidDataException("Entry references an invalid string table index.");
        }

        var message = ReadPayload(
            Column(SegmentFileContract.Column.MessageOffset).ReadInt64(index),
            Column(SegmentFileContract.Column.MessageLength).ReadInt32(index));
        var original = ReadPayload(
            Column(SegmentFileContract.Column.OriginalOffset).ReadInt64(index),
            Column(SegmentFileContract.Column.OriginalLength).ReadInt32(index));
        var sequence = Column(SegmentFileContract.Column.Sequence).ReadInt64(index);
        return new NormalizedEntry(
            sessionId,
            sequence,
            sequence,
            new RawSpan(
                Column(SegmentFileContract.Column.RawOffset).ReadInt64(index),
                Column(SegmentFileContract.Column.RawLength).ReadInt32(index)),
            new InstantUs(Column(SegmentFileContract.Column.Timestamp).ReadInt64(index)),
            original,
            (TimestampProvenance)Column(SegmentFileContract.Column.Provenance).ReadByte(index),
            Column(SegmentFileContract.Column.Confidence).ReadByte(index) / 255d,
            Column(SegmentFileContract.Column.Pid).ReadInt32(index),
            Column(SegmentFileContract.Column.Tid).ReadInt32(index),
            (LogLevel)Column(SegmentFileContract.Column.Level).ReadByte(index),
            tags[(int)tagId],
            buffers[(int)bufferId],
            message,
            (LogcatFormat)Column(SegmentFileContract.Column.Format).ReadByte(index),
            parserVersion,
            Column(SegmentFileContract.Column.Template).ReadUInt32(index),
            (EntryAttributes)Column(SegmentFileContract.Column.Flags).ReadUInt16(index));
    }

    /// <summary>
    /// Releases this holder's share. The mappings close once every snapshot that listed
    /// the segment has released it.
    /// </summary>
    public void Dispose()
    {
        var remaining = Interlocked.Decrement(ref _references);
        if (remaining > 0)
        {
            return;
        }

        if (remaining < 0)
        {
            // Disposed more often than referenced. Restore the floor so the extra
            // release cannot make a later one free the mappings a second time.
            Interlocked.Exchange(ref _references, 0);
            return;
        }

        lock (_columnLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (var index = 0; index < _columns.Length; index++)
            {
                _columns[index]?.Dispose();
                _columns[index] = null;
            }

            _severity = null;
        }

        lock (_filterLock)
        {
            _filterCache.Clear();
            _filterLru.Clear();
        }
    }

    private MappedColumn Column(SegmentFileContract.Column column)
    {
        var existing = Volatile.Read(ref _columns[(int)column]);
        if (existing is not null)
        {
            return existing;
        }

        lock (_columnLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_columns[(int)column] is { } raced)
            {
                return raced;
            }

            var path = Path.Combine(DirectoryPath, SegmentFileContract.NameOf(column));
            var opened = column == SegmentFileContract.Column.Payload
                ? new MappedColumn(path, 1, new FileInfo(path).Length)
                : new MappedColumn(path, SegmentFileContract.ElementSizeOf(column), Count);
            Volatile.Write(ref _columns[(int)column], opened);
            return opened;
        }
    }

    private string ReadPayload(long offset, int length) =>
        length == 0
            ? string.Empty
            : Encoding.UTF8.GetString(Column(SegmentFileContract.Column.Payload).ReadBytes(offset, length));

    private static void EnsureWithin(string root, string candidate)
    {
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.StartsWith(rootPath, comparison))
        {
            throw new InvalidDataException("Session manifest contains a path outside the session directory.");
        }
    }

    private static void EnsureNoReparsePoints(string root, string candidate)
    {
        var current = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(current, candidate);
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("Session paths may not traverse symbolic links or reparse points.");
                }
            }
        }
    }
}
