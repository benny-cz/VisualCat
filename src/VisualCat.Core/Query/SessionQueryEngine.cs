using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Query;

public static class SessionQueryEngine
{
    public static HeatMapResult QueryHeatMap(
        SessionSnapshot snapshot,
        Viewport viewport,
        FilterSpec filter,
        long queryGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(filter);
        var identity = Identity(snapshot, filter, queryGeneration);
        var columns = new TimeRange[viewport.DevicePixelWidth];
        var counts = new Dictionary<LogLevel, long[]>();
        foreach (var level in LogLevels.StorageOrder)
        {
            counts[level] = new long[viewport.DevicePixelWidth];
        }

        for (var column = 0; column < columns.Length; column++)
        {
            columns[column] = new TimeRange(viewport.Boundary(column), viewport.Boundary(column + 1));
        }

        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SegmentOverlaps(segment, viewport.Range))
            {
                continue;
            }

            var active = ActiveBitmap(snapshot, segment, filter);

            // Adjacent columns share a boundary, so N+1 bounds describe N cells and the
            // 2N searches the pair-at-a-time form performed are halved. Clamping each
            // boundary into the filter's time range is equivalent to intersecting every
            // column with it: a column entirely outside collapses to a zero-width cell
            // and counts zero, which is what ApplyTimeFilter returned by other means.
            var bounds = new int[columns.Length + 1];
            var previous = 0;
            for (var boundary = 0; boundary <= columns.Length; boundary++)
            {
                var instant = viewport.Boundary(boundary).Value;
                if (filter.TimeRange is { } window)
                {
                    instant = Math.Clamp(instant, window.StartInclusive.Value, window.EndExclusive.Value);
                }

                // Boundaries are non-decreasing, so each search resumes at the previous
                // result instead of bisecting the segment again (§12.2).
                previous = segment.LowerBoundFrom(instant, previous);
                bounds[boundary] = previous;
            }

            var unfiltered = IsAll(filter);
            foreach (var level in LogLevels.StorageOrder)
            {
                if (filter.IncludedLevels.Count > 0 && !filter.IncludedLevels.Contains(level))
                {
                    continue;
                }

                // §12.4: compose the active filter with the precomputed severity bitmap
                // word-wise, then answer every cell with a constant-time rank
                // subtraction. Rebuilding a per-level bitmap from an index predicate
                // instead costs one delegate call per entry per row.
                var severity = segment.SeverityBitmaps[level];
                var activeLevel = unfiltered
                    ? severity
                    : segment.GetOrCreateBitmap(
                        LevelBitmapKey(identity.FilterFingerprint, level),
                        () => active.And(severity));
                var output = counts[level];
                for (var column = 0; column < columns.Length; column++)
                {
                    var start = bounds[column];
                    var end = bounds[column + 1];
                    if (start < end)
                    {
                        output[column] += activeLevel.CountInRange(start, end);
                    }
                }
            }
        }

        var maximum = counts.Values.SelectMany(static values => values).DefaultIfEmpty().Max();
        var hasUnknown = counts[LogLevel.Unknown].Any(static count => count > 0);
        return new HeatMapResult(identity, viewport, columns, counts, maximum, hasUnknown);
    }

    public static IReadOnlyList<AggregateCell> QueryNamedBuckets(
        SessionSnapshot snapshot,
        TimeRange range,
        BucketWidth width,
        BucketAlignment alignment,
        FilterSpec filter,
        long queryGeneration,
        CancellationToken cancellationToken = default)
    {
        var identity = Identity(snapshot, filter, queryGeneration);
        var unfiltered = IsAll(filter);
        var cells = new List<AggregateCell>();
        var first = alignment.RangeContaining(range.StartInclusive, width).StartInclusive;
        for (var start = first.Value; start < range.EndExclusive.Value; start = checked(start + width.Microseconds))
        {
            var bucket = new TimeRange(new InstantUs(start), new InstantUs(checked(start + width.Microseconds)));
            var clipped = bucket.Intersect(range);
            var effective = ApplyTimeFilter(clipped, filter.TimeRange);
            foreach (var level in LogLevels.DisplayOrder)
            {
                long count = 0;
                if (effective is null)
                {
                    cells.Add(new AggregateCell(bucket, level, 0));
                    continue;
                }

                foreach (var segment in snapshot.Segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SegmentOverlaps(segment, effective.Value))
                    {
                        continue;
                    }

                    // Composed once per (segment, level) through the segment cache, not
                    // re-ANDed for every bucket in the range (§12.3).
                    var severity = segment.SeverityBitmaps[level];
                    var composed = unfiltered
                        ? severity
                        : segment.GetOrCreateBitmap(
                            LevelBitmapKey(identity.FilterFingerprint, level),
                            () => ActiveBitmap(snapshot, segment, filter).And(severity));
                    count += composed.CountInRange(
                        segment.LowerBound(effective.Value.StartInclusive.Value),
                        segment.LowerBound(effective.Value.EndExclusive.Value));
                }

                cells.Add(new AggregateCell(bucket, level, count));
            }
        }

        return cells;
    }

    public static StatisticsResult QueryStatistics(
        SessionSnapshot snapshot,
        FilterSpec filter,
        long queryGeneration,
        int facetLimit = 20,
        CancellationToken cancellationToken = default)
    {
        var identity = Identity(snapshot, filter, queryGeneration);
        var levels = LogLevels.StorageOrder.ToArray().ToDictionary(static level => level, static _ => 0L);
        InstantUs? first = null;
        InstantUs? last = null;
        long timed = 0;

        // One fingerprint for the whole pass rather than one per segment: it hashes the
        // entire filter, and with the scans below now answered from cache it would otherwise
        // be a visible share of the query's remaining cost.
        var fingerprint = filter.Fingerprint();
        var storageOrder = LogLevels.StorageOrder;
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = LevelSummary(snapshot, segment, filter, fingerprint, cancellationToken);
            if (summary.Timed == 0)
            {
                continue;
            }

            timed += summary.Timed;
            var segmentFirst = new InstantUs(summary.First);
            var segmentLast = new InstantUs(summary.Last);
            first = first is null || segmentFirst < first ? segmentFirst : first;
            last = last is null || segmentLast > last ? segmentLast : last;
            for (var position = 0; position < storageOrder.Length; position++)
            {
                levels[storageOrder[position]] += summary.Levels[position];
            }
        }

        // A facet remains useful after a value from that facet is selected. Count it
        // with only its own dimension omitted while retaining every other constraint.
        var tags = CountFacet(
            snapshot,
            filter with
            {
                IncludedTags = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                ExcludedTags = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
            },
            "tags",
            (segment, index) => snapshot.Tags[(int)segment.TagIdAt(index)],
            StringComparer.Ordinal,
            cancellationToken);
        var pids = CountFacet(
            snapshot,
            filter with { IncludedPids = ImmutableHashSet<int>.Empty, ExcludedPids = ImmutableHashSet<int>.Empty },
            "pids",
            static (segment, index) => segment.PidAt(index),
            null,
            cancellationToken);
        var processes = CountFacet(
            snapshot,
            filter with
            {
                IncludedProcesses = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                ExcludedProcesses = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
            },
            "processes",
            (segment, index) =>
            {
                var pid = segment.PidAt(index);
                return snapshot.ResolveProcessName(pid, new InstantUs(segment.TimestampAt(index))) ?? $"PID {pid}";
            },
            StringComparer.Ordinal,
            cancellationToken,
            volatileAcrossGenerations: true);
        var tids = CountFacet(
            snapshot,
            filter with { IncludedTids = ImmutableHashSet<int>.Empty, ExcludedTids = ImmutableHashSet<int>.Empty },
            "tids",
            static (segment, index) => segment.TidAt(index),
            null,
            cancellationToken);
        var buffers = CountFacet(
            snapshot,
            filter with
            {
                IncludedBuffers = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
                ExcludedBuffers = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
            },
            "buffers",
            (segment, index) => snapshot.Buffers[(int)segment.BufferIdAt(index)],
            StringComparer.Ordinal,
            cancellationToken);
        var templates = CountFacet(
            snapshot,
            filter with
            {
                IncludedTemplates = ImmutableHashSet<uint>.Empty,
                ExcludedTemplates = ImmutableHashSet<uint>.Empty,
            },
            "templates",
            static (segment, index) => segment.TemplateIdAt(index),
            null,
            cancellationToken);

        var untimed = IsUnfilteredForUntimed(filter) ? snapshot.Descriptor.Counters.UntimedEntries : 0;
        return new StatisticsResult(
            identity,
            timed + untimed,
            timed,
            untimed,
            first,
            last,
            levels,
            Top(tags, facetLimit),
            Top(pids, facetLimit),
            Top(tids, facetLimit),
            Top(buffers, facetLimit),
            Top(templates, facetLimit),
            Top(processes, facetLimit));
    }

    public static EntryPage GetEntries(
        SessionSnapshot snapshot,
        TimeRange range,
        FilterSpec filter,
        EntryOrder order,
        EntryCursor? cursor,
        int pageSize,
        long queryGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        if (pageSize > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size is capped at 10,000.");
        }

        var identity = Identity(snapshot, filter, queryGeneration);
        if (order == EntryOrder.SourceSequence)
        {
            return GetEntriesInSourceOrder(
                snapshot,
                range,
                filter,
                cursor,
                pageSize,
                identity,
                cancellationToken);
        }

        var queues = new PriorityQueue<SegmentPosition, EntryKey>();
        long total = 0;
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = ActiveBitmap(snapshot, segment, filter);
            var effectiveRange = ApplyTimeFilter(range, filter.TimeRange);
            if (effectiveRange is null)
            {
                continue;
            }

            var start = segment.LowerBound(effectiveRange.Value.StartInclusive.Value);
            var end = segment.LowerBound(effectiveRange.Value.EndExclusive.Value);
            total += active.CountInRange(start, end);
            var first = NextMatching(segment, active, start, end, cursor, order);
            if (first < end)
            {
                queues.Enqueue(new SegmentPosition(segment, active, first, end), Key(segment, first, order));
            }
        }

        var entries = new List<NormalizedEntry>(Math.Min(pageSize, checked((int)Math.Min(int.MaxValue, total))));
        EntryCursor? nextCursor = null;
        while (queues.Count > 0 && entries.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = queues.Dequeue();
            var entry = position.Segment.ReadEntry(
                position.Index,
                snapshot.SessionId,
                snapshot.Tags,
                snapshot.Buffers,
                snapshot.Manifest.ParserVersion);
            entries.Add(entry);
            nextCursor = new EntryCursor(order, entry.Timestamp?.Value ?? long.MinValue, entry.SourceSequence);
            var next = NextMatching(position.Segment, position.Active, position.Index + 1, position.End, null, order);
            if (next < position.End)
            {
                queues.Enqueue(position with { Index = next }, Key(position.Segment, next, order));
            }
        }

        return new EntryPage(identity, entries, queues.Count > 0 ? nextCursor : null, total);
    }

    /// <summary>Returns the most frequent templates in a range and optional severity row.</summary>
    /// <param name="snapshot">The immutable session snapshot to query.</param>
    /// <param name="range">The half-open time range to rank.</param>
    /// <param name="filter">The active filter constraints.</param>
    /// <param name="top">The maximum number of summaries to return.</param>
    /// <param name="queryGeneration">The caller generation stamped on query identity.</param>
    /// <param name="level">
    /// Restricts the ranking to one severity row. It composes through the same cached
    /// <c>active AND severity</c> bitmap the heat map already built for this filter, so
    /// asking "what dominates this one cell" costs a rank walk over the cell's index
    /// range and never a fresh predicate pass over the segment (§12.3, §12.7).
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public static IReadOnlyList<TemplateSummary> QueryTopTemplates(
        SessionSnapshot snapshot,
        TimeRange range,
        FilterSpec filter,
        int top,
        long queryGeneration,
        LogLevel? level = null,
        CancellationToken cancellationToken = default)
    {
        var identity = Identity(snapshot, filter, queryGeneration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(top);
        var unfiltered = IsAll(filter);
        var counts = new Dictionary<uint, (long Count, InstantUs? First, InstantUs? Last)>();
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = ActiveBitmap(snapshot, segment, filter);
            if (level is { } severityLevel)
            {
                var severity = segment.SeverityBitmaps[severityLevel];
                active = unfiltered
                    ? severity
                    : segment.GetOrCreateBitmap(
                        LevelBitmapKey(identity.FilterFingerprint, severityLevel),
                        () => active.And(severity));
            }

            var effective = ApplyTimeFilter(range, filter.TimeRange);
            if (effective is null)
            {
                continue;
            }

            var start = segment.LowerBound(effective.Value.StartInclusive.Value);
            var end = segment.LowerBound(effective.Value.EndExclusive.Value);
            for (var index = start; index < end; index++)
            {
                if (!active[index])
                {
                    continue;
                }

                var id = segment.TemplateIdAt(index);
                if (id == 0)
                {
                    continue;
                }

                var instant = new InstantUs(segment.TimestampAt(index));
                if (counts.TryGetValue(id, out var current))
                {
                    counts[id] = (
                        current.Count + 1,
                        current.First is null || instant < current.First ? instant : current.First,
                        current.Last is null || instant > current.Last ? instant : current.Last);
                }
                else
                {
                    counts[id] = (1, instant, instant);
                }
            }
        }

        var definitions = snapshot.Templates.ToDictionary(static template => template.TemplateId);
        return counts
            .OrderByDescending(static pair => pair.Value.Count)
            .ThenBy(static pair => pair.Key)
            .Take(top)
            .Select(pair =>
            {
                definitions.TryGetValue(pair.Key, out var definition);
                return new TemplateSummary(
                    pair.Key,
                    definition?.CanonicalText ?? $"Template {pair.Key}",
                    pair.Value.Count,
                    pair.Value.First,
                    pair.Value.Last,
                    definition?.RepresentativeEntryIds ?? []);
            })
            .ToArray();
    }

    public static Task<SearchResult> SearchAsync(
        SessionSnapshot snapshot,
        TextSearchSpec search,
        FilterSpec baseFilter,
        long queryGeneration,
        IProgress<SearchProgress>? progress = null,
        int markerLimit = 20_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var filter = baseFilter with { Search = search };
        var identity = Identity(snapshot, filter, queryGeneration);
        var markers = new List<InstantUs>(Math.Min(markerLimit, 20_000));
        long matches = 0;
        long scanned = 0;
        var total = snapshot.Segments.Sum(static segment => (long)segment.Count);
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = ActiveBitmap(snapshot, segment, filter);
            for (var i = 0; i < segment.Count; i++)
            {
                if (bitmap[i])
                {
                    matches++;
                    if (markers.Count < markerLimit)
                    {
                        markers.Add(new InstantUs(segment.TimestampAt(i)));
                    }
                }

                scanned++;
                if ((scanned & 0x3fff) == 0)
                {
                    progress?.Report(new SearchProgress(identity, scanned, matches, false, total == 0 ? 1 : scanned / (double)total));
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        progress?.Report(new SearchProgress(identity, scanned, matches, true, 1));
        markers.Sort();
        return Task.FromResult(new SearchResult(identity, matches, markers, matches > markers.Count));
    }

    public static IReadOnlyList<SourceRecord> GetRawContext(
        SessionSnapshot snapshot,
        long sourceSequence,
        int before,
        int after)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfNegative(after);
        var path = Path.Combine(snapshot.RootPath, "source-order", "records.bin");
        var first = Math.Max(0, sourceSequence - before);
        var result = new List<SourceRecord>(before + after + 1);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);

        // Sequences are dense and monotonic, so the sidecar turns "find record N" from a
        // scan of the whole session into one seek. Sessions written before the index
        // existed, or whose index is unusable, still work by scanning (§16.3 degraded).
        if (TryFindRecordOffset(snapshot.RootPath, first, out var startOffset) && startOffset <= stream.Length)
        {
            stream.Position = startOffset;
        }

        while (stream.Position < stream.Length)
        {
            var record = SourceRecordCodec.Read(reader);
            if (record.Sequence < first)
            {
                continue;
            }

            if (record.Sequence > sourceSequence + after)
            {
                break;
            }

            result.Add(record);
        }

        return result;
    }

    /// <summary>
    /// One page of a forward scan over the session's physical source records.
    /// </summary>
    /// <param name="Records">The matching records, in source order.</param>
    /// <param name="NextSequence">Where a following page should resume.</param>
    /// <param name="Completed">Whether the scan reached the end of the session.</param>
    public readonly record struct SourceScanPage(
        IReadOnlyList<SourceRecord> Records,
        long NextSequence,
        bool Completed);

    /// <summary>
    /// Scans source records forward from <paramref name="fromSequence"/>, keeping the ones
    /// whose parse outcome <paramref name="include"/> accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="GetRawContext"/>, which answers "what surrounds this
    /// entry". This answers "where are the lines that did not become entries" — the population
    /// <see href="../../../docs/adr/0009-continuations.md">ADR 0009</see> deliberately leaves
    /// as <see cref="ParseOutcomeKind.UnknownLine"/>, which for a ThreadTime crash log is every
    /// stack frame in the file. The decision to keep them unknown is right; leaving them
    /// unreachable is what V2-14 records.
    /// </para>
    /// <para>
    /// Both bounds are hard. <paramref name="maximumRecords"/> caps what a page returns and
    /// <paramref name="maximumScanned"/> caps how far it will look for them, so a session where
    /// the matching lines are sparse — or absent — costs a bounded read rather than a walk of
    /// a hundred million records. A page that stops on the scan bound reports
    /// <see cref="SourceScanPage.Completed"/> as <see langword="false"/> and a resume point.
    /// </para>
    /// </remarks>
    public static SourceScanPage ScanSourceRecords(
        SessionSnapshot snapshot,
        long fromSequence,
        int maximumRecords,
        int maximumScanned,
        Func<ParseOutcomeKind, bool> include,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumScanned);
        var first = Math.Max(0, fromSequence);
        var path = Path.Combine(snapshot.RootPath, "source-order", "records.bin");
        var kept = new List<SourceRecord>(Math.Min(maximumRecords, 512));
        if (!File.Exists(path))
        {
            return new SourceScanPage(kept, first, true);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        if (TryFindRecordOffset(snapshot.RootPath, first, out var startOffset) && startOffset <= stream.Length)
        {
            stream.Position = startOffset;
        }

        var scanned = 0;
        var next = first;
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = SourceRecordCodec.Read(reader);
            if (record.Sequence < first)
            {
                continue;
            }

            next = record.Sequence + 1;
            scanned++;
            if (include(record.Outcome))
            {
                kept.Add(record);
                if (kept.Count >= maximumRecords)
                {
                    return new SourceScanPage(kept, next, false);
                }
            }

            if (scanned >= maximumScanned)
            {
                return new SourceScanPage(kept, next, false);
            }
        }

        return new SourceScanPage(kept, next, true);
    }

    private static bool TryFindRecordOffset(string rootPath, long sequence, out long offset)
    {
        offset = 0;
        var indexPath = Path.Combine(rootPath, "source-order", "index.bin");
        try
        {
            using var index = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var position = sequence * sizeof(long);
            if (position < 0 || position + sizeof(long) > index.Length)
            {
                return false;
            }

            index.Position = position;
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            index.ReadExactly(buffer);
            offset = BitConverter.ToInt64(buffer);
            return offset >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static readonly Lock RegexCacheLock = new();
    private static readonly Dictionary<(string Pattern, bool CaseSensitive, TimeSpan Timeout), Regex> RegexCache = [];

    /// <summary>
    /// Compiles and memoizes a search pattern. <see cref="RegexOptions.NonBacktracking"/>
    /// bounds execution against pathological patterns over untrusted log text, but it
    /// rejects lookarounds and backreferences, so those degrade to the backtracking
    /// engine under the same timeout rather than failing the search (§12.8, §18.5).
    ///
    /// Public because the presentation layer highlights matches inside the rows this
    /// engine selected: sharing the one compiled instance keeps "highlighted" and
    /// "matched" the same predicate, and keeps row rendering off the compile path.
    /// </summary>
    public static Regex CompileSearchRegex(TextSearchSpec search)
    {
        var timeout = search.RegexTimeout ?? TimeSpan.FromMilliseconds(250);
        var key = (search.Query, search.CaseSensitive, timeout);
        lock (RegexCacheLock)
        {
            if (RegexCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var options = RegexOptions.CultureInvariant;
        if (!search.CaseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex compiled;
        try
        {
            compiled = new Regex(search.Query, options | RegexOptions.NonBacktracking, timeout);
        }
        catch (NotSupportedException)
        {
            compiled = new Regex(search.Query, options, timeout);
        }

        lock (RegexCacheLock)
        {
            if (RegexCache.Count >= 64)
            {
                RegexCache.Clear();
            }

            RegexCache[key] = compiled;
            return compiled;
        }
    }

    private static string LevelBitmapKey(string filterFingerprint, LogLevel level) =>
        string.Concat(filterFingerprint, "|level:", ((byte)level).ToString(CultureInfo.InvariantCulture));

    private static QueryIdentity Identity(SessionSnapshot snapshot, FilterSpec filter, long generation) =>
        new(snapshot.SessionId, snapshot.Generation, filter.Fingerprint(), generation);

    private static RankBitmap ActiveBitmap(SessionSnapshot snapshot, SegmentSnapshot segment, FilterSpec filter)
    {
        var key = filter.Fingerprint();
        if (ReferenceEquals(filter, FilterSpec.All) || IsAll(filter))
        {
            return segment.GetOrCreateFilter("all", static _ => true);
        }

        StringComparison comparison = filter.Search is { CaseSensitive: false }
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Compilation is memoized: this runs once per segment per query, and building a
        // Regex is orders of magnitude dearer than the dictionary lookup it replaces.
        var regex = filter.Search is { IsRegex: true } regexSearch ? CompileSearchRegex(regexSearch) : null;

        return segment.GetOrCreateFilter(key, index =>
        {
            var level = segment.LevelAt(index);
            if (filter.IncludedLevels.Count > 0 && !filter.IncludedLevels.Contains(level))
            {
                return false;
            }

            var tag = snapshot.Tags[(int)segment.TagIdAt(index)];
            if (filter.IncludedTags.Count > 0 && !filter.IncludedTags.Contains(tag) ||
                filter.ExcludedTags.Contains(tag) ||
                filter.IncludedPids.Count > 0 && !filter.IncludedPids.Contains(segment.PidAt(index)) ||
                filter.ExcludedPids.Contains(segment.PidAt(index)) ||
                filter.IncludedTids.Count > 0 && !filter.IncludedTids.Contains(segment.TidAt(index)) ||
                filter.ExcludedTids.Contains(segment.TidAt(index)) ||
                filter.IncludedTemplates.Count > 0 && !filter.IncludedTemplates.Contains(segment.TemplateIdAt(index)) ||
                filter.ExcludedTemplates.Contains(segment.TemplateIdAt(index)))
            {
                return false;
            }

            if (filter.IncludedProcesses.Count > 0 || filter.ExcludedProcesses.Count > 0)
            {
                var pid = segment.PidAt(index);
                var process = snapshot.ResolveProcessName(
                                  pid,
                                  new InstantUs(segment.TimestampAt(index)))
                              ?? $"PID {pid}";
                if (filter.IncludedProcesses.Count > 0 && !filter.IncludedProcesses.Contains(process) ||
                    filter.ExcludedProcesses.Contains(process))
                {
                    return false;
                }
            }

            var buffer = snapshot.Buffers[(int)segment.BufferIdAt(index)];
            if (filter.IncludedBuffers.Count > 0 && !filter.IncludedBuffers.Contains(buffer) ||
                filter.ExcludedBuffers.Contains(buffer))
            {
                return false;
            }

            if (filter.IncludedOutcomes.Count > 0 && !filter.IncludedOutcomes.Contains(ParseOutcomeKind.ParsedEntry))
            {
                return false;
            }

            if (filter.Search is not { } textSearch)
            {
                return true;
            }

            var message = segment.MessageAt(index);
            return textSearch.IsRegex
                ? regex!.IsMatch(message)
                : message.Contains(textSearch.Query, comparison);
        });
    }

    private static bool IsAll(FilterSpec filter) =>
        filter.TimeRange is null &&
        filter.IncludedLevels.Count == 0 &&
        filter.IncludedTags.Count == 0 &&
        filter.ExcludedTags.Count == 0 &&
        filter.IncludedPids.Count == 0 &&
        filter.ExcludedPids.Count == 0 &&
        filter.IncludedProcesses.Count == 0 &&
        filter.ExcludedProcesses.Count == 0 &&
        filter.IncludedTids.Count == 0 &&
        filter.ExcludedTids.Count == 0 &&
        filter.IncludedTemplates.Count == 0 &&
        filter.ExcludedTemplates.Count == 0 &&
        filter.IncludedBuffers.Count == 0 &&
        filter.ExcludedBuffers.Count == 0 &&
        filter.IncludedOutcomes.Count == 0 &&
        filter.Search is null;

    private static bool IsUnfilteredForUntimed(FilterSpec filter) =>
        IsAll(filter) || filter.IncludedOutcomes.Contains(ParseOutcomeKind.UntimedEntry);

    private static bool SegmentOverlaps(SegmentSnapshot segment, TimeRange range) =>
        segment.Count > 0 &&
        segment.Manifest.MinimumTimestampUs < range.EndExclusive.Value &&
        segment.Manifest.MaximumTimestampUs >= range.StartInclusive.Value;

    private static TimeRange? ApplyTimeFilter(TimeRange range, TimeRange? filterRange)
    {
        if (filterRange is null)
        {
            return range;
        }

        if (!range.Overlaps(filterRange.Value))
        {
            return null;
        }

        return range.Intersect(filterRange.Value);
    }

    private static (int Start, int End) RangeIndices(SegmentSnapshot segment, TimeRange? range) =>
        range is null
            ? (0, segment.Count)
            : (segment.LowerBound(range.Value.StartInclusive.Value), segment.LowerBound(range.Value.EndExclusive.Value));

    private static int NextMatching(
        SegmentSnapshot segment,
        RankBitmap active,
        int start,
        int end,
        EntryCursor? cursor,
        EntryOrder order)
    {
        if (cursor is not null && order == EntryOrder.Chronological)
        {
            start = segment.UpperBound(cursor.TimestampUs, cursor.Sequence, start, end);
        }

        for (var index = start; index < end; index++)
        {
            if (!active[index])
            {
                continue;
            }

            // Chronological cursors were already applied by UpperBound above. Rechecking
            // only their sequence component would discard a later timestamp whose source
            // sequence happens to be lower (the normal shape of out-of-order logcat).
            if (cursor is null || order == EntryOrder.Chronological)
            {
                return index;
            }

            var sequence = segment.SequenceAt(index);
            var after = sequence > cursor.Sequence;
            if (after)
            {
                return index;
            }
        }

        return end;
    }

    private static EntryKey Key(SegmentSnapshot segment, int index, EntryOrder order) =>
        order == EntryOrder.Chronological
            ? new EntryKey(segment.TimestampAt(index), segment.SequenceAt(index))
            : new EntryKey(segment.SequenceAt(index), segment.TimestampAt(index));

    private static FacetValue<T>[] Top<T>(Dictionary<T, long> values, int limit)
        where T : notnull =>
        values.OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .Take(limit)
            .Select(static pair => new FacetValue<T>(pair.Key, pair.Value))
            .ToArray();

    /// <summary>
    /// How many distinct values a per-segment tally may hold and still be worth keeping.
    /// </summary>
    /// <remarks>
    /// A tally is one entry per distinct value in the segment, so a high-cardinality
    /// dimension — thread ids on a busy device — can approach one entry per record. Past
    /// this point the tally costs more memory than the scan it saves, and it would evict
    /// every cheaper aggregate beside it, so it is used once and forgotten.
    /// </remarks>
    private const int MaximumCachedFacetValues = 8192;

    /// <summary>
    /// This segment's contribution to the level counts and the timestamp bounds, under one
    /// filter. A published segment is immutable, so this is a constant and is cached on the
    /// segment itself; see <see cref="SegmentSnapshot.GetOrCreateAggregate"/>.
    /// </summary>
    private sealed class SegmentLevelSummary
    {
        public required long[] Levels { get; init; }
        public required long Timed { get; init; }
        public required long First { get; init; }
        public required long Last { get; init; }
    }

    private static SegmentLevelSummary LevelSummary(
        SessionSnapshot snapshot,
        SegmentSnapshot segment,
        FilterSpec filter,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        return segment.GetOrCreateAggregate(
            "stats.levels|" + fingerprint,
            () =>
            {
                var storageOrder = LogLevels.StorageOrder;
                var counts = new long[storageOrder.Length];
                var active = ActiveBitmap(snapshot, segment, filter);
                var (start, end) = RangeIndices(segment, filter.TimeRange);
                long timed = 0;
                long first = 0;
                long last = 0;
                for (var index = start; index < end; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!active[index])
                    {
                        continue;
                    }

                    var timestamp = segment.TimestampAt(index);
                    if (timed == 0)
                    {
                        first = timestamp;
                        last = timestamp;
                    }
                    else
                    {
                        if (timestamp < first) { first = timestamp; }
                        if (timestamp > last) { last = timestamp; }
                    }

                    timed++;
                    var level = segment.LevelAt(index);
                    for (var position = 0; position < storageOrder.Length; position++)
                    {
                        if (storageOrder[position] == level)
                        {
                            counts[position]++;
                            break;
                        }
                    }
                }

                return new SegmentLevelSummary
                {
                    Levels = counts,
                    Timed = timed,
                    First = first,
                    Last = last,
                };
            });
    }

    /// <summary>
    /// Tallies one facet dimension across the session by folding each segment's own tally,
    /// which is cached on the segment because a published segment cannot change.
    /// </summary>
    /// <remarks>
    /// <c>volatileAcrossGenerations</c> is set where the selector reads something outside the
    /// segment that a later generation can revise. Only the process dimension does: it
    /// resolves a pid through the session's process-name table, and a capture keeps adding
    /// observations to that table, so its key carries the table's size and a new sample
    /// retires the cached tallies rather than leaving a stale attribution in place.
    /// </remarks>
    private static Dictionary<T, long> CountFacet<T>(
        SessionSnapshot snapshot,
        FilterSpec filter,
        string dimension,
        Func<SegmentSnapshot, int, T> selector,
        IEqualityComparer<T>? comparer,
        CancellationToken cancellationToken,
        bool volatileAcrossGenerations = false)
        where T : notnull
    {
        var key = string.Concat(
            "facet.",
            dimension,
            "|",
            filter.Fingerprint(),
            volatileAcrossGenerations
                ? "|pn=" + snapshot.ProcessNames.Count.ToString(CultureInfo.InvariantCulture)
                : string.Empty);

        var values = new Dictionary<T, long>(comparer);
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tally = segment.GetOrCreateAggregate(
                key,
                () =>
                {
                    var counts = new Dictionary<T, long>(comparer);
                    var active = ActiveBitmap(snapshot, segment, filter);
                    var (start, end) = RangeIndices(segment, filter.TimeRange);
                    for (var index = start; index < end; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (active[index])
                        {
                            Increment(counts, selector(segment, index));
                        }
                    }

                    return counts;
                },
                static counts => counts.Count <= MaximumCachedFacetValues);

            foreach (var (value, count) in tally)
            {
                values.TryGetValue(value, out var running);
                values[value] = running + count;
            }
        }

        return values;
    }

    private static EntryPage GetEntriesInSourceOrder(
        SessionSnapshot snapshot,
        TimeRange range,
        FilterSpec filter,
        EntryCursor? cursor,
        int pageSize,
        QueryIdentity identity,
        CancellationToken cancellationToken)
    {
        // Segments are timestamp-sorted, so each keeps a lazy sequence-sorted index.
        // Merge one position per segment just as chronological paging merges its native
        // order; subsequent pages then seek by cursor instead of rescanning the session.
        var queues = new PriorityQueue<SourcePosition, long>();
        long total = 0;
        foreach (var segment in snapshot.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = ActiveBitmap(snapshot, segment, filter);
            var effectiveRange = ApplyTimeFilter(range, filter.TimeRange);
            if (effectiveRange is null)
            {
                continue;
            }

            var start = segment.LowerBound(effectiveRange.Value.StartInclusive.Value);
            var end = segment.LowerBound(effectiveRange.Value.EndExclusive.Value);
            total += active.CountInRange(start, end);
            var indices = segment.SourceOrderIndices;
            var position = cursor is null ? 0 : segment.SourceOrderUpperBound(cursor.Sequence);
            position = NextSourceMatching(indices, active, position, start, end);
            if (position < indices.Count)
            {
                queues.Enqueue(
                    new SourcePosition(segment, active, indices, position, start, end),
                    segment.SequenceAt(indices[position]));
            }
        }

        var entries = new List<NormalizedEntry>(Math.Min(pageSize, checked((int)Math.Min(total, int.MaxValue))));
        EntryCursor? next = null;
        while (queues.Count > 0 && entries.Count < pageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = queues.Dequeue();
            var index = candidate.Indices[candidate.Position];
            var entry = candidate.Segment.ReadEntry(
                index,
                snapshot.SessionId,
                snapshot.Tags,
                snapshot.Buffers,
                snapshot.Manifest.ParserVersion);
            entries.Add(entry);
            next = new EntryCursor(EntryOrder.SourceSequence, entry.Timestamp?.Value ?? long.MinValue, entry.SourceSequence);

            var following = NextSourceMatching(
                candidate.Indices,
                candidate.Active,
                candidate.Position + 1,
                candidate.Start,
                candidate.End);
            if (following < candidate.Indices.Count)
            {
                queues.Enqueue(
                    candidate with { Position = following },
                    candidate.Segment.SequenceAt(candidate.Indices[following]));
            }
        }

        if (queues.Count == 0)
        {
            next = null;
        }

        return new EntryPage(identity, entries, next, total);
    }

    private static int NextSourceMatching(
        IReadOnlyList<int> indices,
        RankBitmap active,
        int position,
        int start,
        int end)
    {
        while (position < indices.Count)
        {
            var index = indices[position];
            if (index >= start && index < end && active[index])
            {
                break;
            }

            position++;
        }

        return position;
    }

    private static void Increment<T>(Dictionary<T, long> values, T key)
        where T : notnull
    {
        values.TryGetValue(key, out var count);
        values[key] = count + 1;
    }

    private readonly record struct SegmentPosition(SegmentSnapshot Segment, RankBitmap Active, int Index, int End);

    private readonly record struct SourcePosition(
        SegmentSnapshot Segment,
        RankBitmap Active,
        IReadOnlyList<int> Indices,
        int Position,
        int Start,
        int End);

    private readonly record struct EntryKey(long Primary, long Secondary) : IComparable<EntryKey>
    {
        public int CompareTo(EntryKey other)
        {
            var primary = Primary.CompareTo(other.Primary);
            return primary != 0 ? primary : Secondary.CompareTo(other.Secondary);
        }
    }
}
