using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Store;

public sealed class SessionSnapshot : IDisposable
{
    private bool _disposed;

    internal SessionSnapshot(string rootPath, SessionManifest manifest, IReadOnlyList<SegmentSnapshot> segments)
    {
        RootPath = rootPath;
        Manifest = manifest;
        Segments = segments;
    }

    public string RootPath { get; }
    public SessionManifest Manifest { get; }
    public Guid SessionId => Manifest.Descriptor.SessionId;
    public long Generation => Manifest.SnapshotGeneration;
    public SessionDescriptor Descriptor => Manifest.Descriptor;
    public IReadOnlyList<SegmentSnapshot> Segments { get; }

    /// <summary>
    /// Gets the number of segment column files this snapshot currently has mapped. A
    /// live capture's health surface reports it, because descriptor exhaustion is the
    /// one resource limit a long capture can still reach before its disk does.
    /// </summary>
    public int MappedColumnCount => Segments.Sum(static segment => segment.MappedColumnCount);
    public IReadOnlyList<string> Tags => Manifest.Tags;
    public IReadOnlyList<string> Buffers => Manifest.Buffers;
    public IReadOnlyList<TemplateDefinition> Templates => Manifest.Templates;
    public IReadOnlyList<ProcessNameRange> ProcessNames => Manifest.ProcessNames ?? [];
    public string? RawPath => Manifest.Source.Embedded
        ? Path.Combine(RootPath, "raw.log")
        : Manifest.Source.Path;

    public TimeRange? TimedRange => Descriptor.FirstInstant is { } first && Descriptor.LastInstant is { } last
        ? new TimeRange(first, new InstantUs(last.Value == long.MaxValue ? long.MaxValue : last.Value + 1))
        : null;

    /// <summary>
    /// Name of <paramref name="pid"/> as of <paramref name="instant"/>, or null when the
    /// session has no naming evidence for that process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A process list is sampled at an instant but describes a process that lived over an
    /// interval, so requiring the entry's timestamp to fall inside <c>[firstSeen,
    /// lastSeen]</c> resolved almost nothing: a capture shorter than the sampling interval
    /// produces one sample per process, whose range is a single microsecond, and a live
    /// capture's first entries are always older than the first sample because they come
    /// out of the device's existing ring buffer.
    /// </para>
    /// <para>
    /// Resolution therefore falls back to the observation in effect at that time — the
    /// latest one at or before the instant, or the earliest if the instant precedes every
    /// sample. What it will not do is carry a name across a known rename: ranges for one
    /// pid are disjoint and ordered, so choosing the neighbouring range never attributes
    /// an entry to a name that was already observed to have been replaced. That is the
    /// distinction §13.8 and §4.3.9 are protecting — pid reuse must not silently merge two
    /// processes — and it survives here while the common case stops returning null.
    /// </para>
    /// </remarks>
    public string? ResolveProcessName(int pid, InstantUs instant)
    {
        var byPid = _processNamesByPid ??= BuildProcessNameIndex();
        if (!byPid.TryGetValue(pid, out var ranges) || ranges.Length == 0)
        {
            return null;
        }

        // Ranges are sorted by FirstSeen, so a binary search finds the last one that
        // started at or before the instant. Linear scanning here cost one pass over every
        // range in the session per entry, which a facet count performs millions of times.
        var low = 0;
        var high = ranges.Length - 1;
        var candidate = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            if (ranges[middle].FirstSeen <= instant)
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return candidate >= 0 ? ranges[candidate].Name : ranges[0].Name;
    }

    private Dictionary<int, ProcessNameRange[]>? _processNamesByPid;

    private Dictionary<int, ProcessNameRange[]> BuildProcessNameIndex() =>
        ProcessNames
            .GroupBy(static range => range.Pid)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static range => range.FirstSeen)
                    .ThenBy(static range => range.LastSeen)
                    .ToArray());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var segment in Segments)
        {
            segment.Dispose();
        }
    }
}
