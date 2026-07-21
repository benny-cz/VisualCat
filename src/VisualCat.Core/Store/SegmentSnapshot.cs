using System.Text;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Store;

public sealed class SegmentSnapshot : IDisposable
{
    private const int FilterCacheCapacity = 64;

    private readonly MappedColumn _timestamp;
    private readonly MappedColumn _sequence;
    private readonly MappedColumn _rawOffset;
    private readonly MappedColumn _rawLength;
    private readonly MappedColumn _pid;
    private readonly MappedColumn _tid;
    private readonly MappedColumn _level;
    private readonly MappedColumn _tag;
    private readonly MappedColumn _template;
    private readonly MappedColumn _flags;
    private readonly MappedColumn _provenance;
    private readonly MappedColumn _confidence;
    private readonly MappedColumn _format;
    private readonly MappedColumn _buffer;
    private readonly MappedColumn _messageOffset;
    private readonly MappedColumn _messageLength;
    private readonly MappedColumn _originalOffset;
    private readonly MappedColumn _originalLength;
    private readonly MappedColumn _payload;
    private readonly Dictionary<LogLevel, RankBitmap> _severity = [];
    private readonly Dictionary<string, (RankBitmap Bitmap, LinkedListNode<string> Node)> _filterCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _filterLru = [];
    private readonly object _filterLock = new();
    private bool _disposed;

    public SegmentSnapshot(string sessionRoot, SegmentManifest manifest)
    {
        Manifest = manifest;
        DirectoryPath = Path.GetFullPath(Path.Combine(sessionRoot, manifest.RelativePath));
        EnsureWithin(sessionRoot, DirectoryPath);
        EnsureNoReparsePoints(sessionRoot, DirectoryPath);
        // Nineteen mappings and seven bitmaps are opened here, any of which can fail on a
        // truncated or corrupted segment. Without this the mappings opened so far would
        // never be disposed — the instance the caller would have disposed never exists —
        // leaving the session's files locked (§10.7: no orphan mappings or locked files).
        var opened = new List<MappedColumn>(19);
        try
        {
            var count = manifest.EntryCount;
            _timestamp = Track(opened, Open("timestamp.bin", 8, count));
            _sequence = Track(opened, Open("sequence.bin", 8, count));
            _rawOffset = Track(opened, Open("raw-offset.bin", 8, count));
            _rawLength = Track(opened, Open("raw-length.bin", 4, count));
            _pid = Track(opened, Open("pid.bin", 4, count));
            _tid = Track(opened, Open("tid.bin", 4, count));
            _level = Track(opened, Open("level.bin", 1, count));
            _tag = Track(opened, Open("tag.bin", 4, count));
            _template = Track(opened, Open("template.bin", 4, count));
            _flags = Track(opened, Open("flags.bin", 2, count));
            _provenance = Track(opened, Open("provenance.bin", 1, count));
            _confidence = Track(opened, Open("confidence.bin", 1, count));
            _format = Track(opened, Open("format.bin", 1, count));
            _buffer = Track(opened, Open("buffer.bin", 4, count));
            _messageOffset = Track(opened, Open("message-offset.bin", 8, count));
            _messageLength = Track(opened, Open("message-length.bin", 4, count));
            _originalOffset = Track(opened, Open("original-offset.bin", 8, count));
            _originalLength = Track(opened, Open("original-length.bin", 4, count));
            var payloadPath = Path.Combine(DirectoryPath, "payload.bin");
            _payload = Track(opened, new MappedColumn(payloadPath, 1, new FileInfo(payloadPath).Length));
            foreach (var level in LogLevels.StorageOrder)
            {
                _severity[level] = RankBitmap.Load(Path.Combine(DirectoryPath, "bitmaps", $"level-{(byte)level}.rbm"));
            }
        }
        catch
        {
            foreach (var column in opened)
            {
                column.Dispose();
            }

            throw;
        }
    }

    private static MappedColumn Track(List<MappedColumn> opened, MappedColumn column)
    {
        opened.Add(column);
        return column;
    }

    public SegmentManifest Manifest { get; }
    public string DirectoryPath { get; }
    public int Count => Manifest.EntryCount;
    public InstantUs MinimumTimestamp => new(Manifest.MinimumTimestampUs);
    public InstantUs MaximumTimestamp => new(Manifest.MaximumTimestampUs);
    public IReadOnlyDictionary<LogLevel, RankBitmap> SeverityBitmaps => _severity;

    public long TimestampAt(int index) => _timestamp.ReadInt64(index);
    public long SequenceAt(int index) => _sequence.ReadInt64(index);
    public int PidAt(int index) => _pid.ReadInt32(index);
    public int TidAt(int index) => _tid.ReadInt32(index);
    public LogLevel LevelAt(int index) => (LogLevel)_level.ReadByte(index);
    public uint TagIdAt(int index) => _tag.ReadUInt32(index);
    public uint TemplateIdAt(int index) => _template.ReadUInt32(index);
    public uint BufferIdAt(int index) => _buffer.ReadUInt32(index);
    public EntryAttributes AttributesAt(int index) => (EntryAttributes)_flags.ReadUInt16(index);
    public string MessageAt(int index) => ReadPayload(_messageOffset.ReadInt64(index), _messageLength.ReadInt32(index));

    public int LowerBound(long timestampUs)
    {
        var low = 0;
        var high = Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_timestamp.ReadInt64(middle) < timestampUs)
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
        var low = Math.Clamp(from, 0, Count);
        if (low >= Count || _timestamp.ReadInt64(low) >= timestampUs)
        {
            return low;
        }

        // timestamp[low] < timestampUs, so the answer is strictly above low. Double the
        // stride until it overshoots, which bounds the region the bisection below scans.
        var step = 1;
        while (low + step < Count && _timestamp.ReadInt64(low + step) < timestampUs)
        {
            low += step;
            step <<= 1;
        }

        var high = Math.Min(Count, low + step);
        low++;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_timestamp.ReadInt64(middle) < timestampUs)
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
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var tagId = _tag.ReadUInt32(index);
        var bufferId = _buffer.ReadUInt32(index);
        if (tagId >= tags.Count || bufferId >= buffers.Count)
        {
            throw new InvalidDataException("Entry references an invalid string table index.");
        }

        var message = ReadPayload(_messageOffset.ReadInt64(index), _messageLength.ReadInt32(index));
        var original = ReadPayload(_originalOffset.ReadInt64(index), _originalLength.ReadInt32(index));
        var sequence = _sequence.ReadInt64(index);
        return new NormalizedEntry(
            sessionId,
            sequence,
            sequence,
            new RawSpan(_rawOffset.ReadInt64(index), _rawLength.ReadInt32(index)),
            new InstantUs(_timestamp.ReadInt64(index)),
            original,
            (TimestampProvenance)_provenance.ReadByte(index),
            _confidence.ReadByte(index) / 255d,
            _pid.ReadInt32(index),
            _tid.ReadInt32(index),
            (LogLevel)_level.ReadByte(index),
            tags[(int)tagId],
            buffers[(int)bufferId],
            message,
            (LogcatFormat)_format.ReadByte(index),
            parserVersion,
            _template.ReadUInt32(index),
            (EntryAttributes)_flags.ReadUInt16(index));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timestamp.Dispose();
        _sequence.Dispose();
        _rawOffset.Dispose();
        _rawLength.Dispose();
        _pid.Dispose();
        _tid.Dispose();
        _level.Dispose();
        _tag.Dispose();
        _template.Dispose();
        _flags.Dispose();
        _provenance.Dispose();
        _confidence.Dispose();
        _format.Dispose();
        _buffer.Dispose();
        _messageOffset.Dispose();
        _messageLength.Dispose();
        _originalOffset.Dispose();
        _originalLength.Dispose();
        _payload.Dispose();
    }

    private MappedColumn Open(string name, int size, int count) => new(Path.Combine(DirectoryPath, name), size, count);

    private string ReadPayload(long offset, int length) =>
        length == 0 ? string.Empty : Encoding.UTF8.GetString(_payload.ReadBytes(offset, length));

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
