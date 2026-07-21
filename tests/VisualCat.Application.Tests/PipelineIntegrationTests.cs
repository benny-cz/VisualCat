using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

public sealed class PipelineIntegrationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private const string Log =
        "--------- beginning of main\n" +
        "05-15 14:13:37.496  1073  1151 D TagA: alpha 1000\n" +
        "05-15 14:13:37.498  1073  1152 W TagB: beta 2000\n" +
        "05-15 14:13:37.497  1074  1153 Q TagA: gamma 3000\n" +
        "malformed evidence retained\n";

    [Fact]
    public async Task PipelineIsDeterministicAcrossChunkingAndVerifiesCoverage()
    {
        await using var left = await ImportAsync([1, 2, 3, 5, 8], workers: 1);
        await using var right = await ImportAsync([4096], workers: 4);
        Assert.Equal(left.Snapshot.Descriptor.Counters, right.Snapshot.Descriptor.Counters);
        Assert.Equal(3, left.Snapshot.Descriptor.Counters.TimedEntries);
        Assert.Equal(1, left.Snapshot.Descriptor.Counters.UnknownLines);

        var report = await SessionVerifier.VerifyAsync(left.Snapshot.RootPath);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(static issue => issue.Message)));
        Assert.Equal(Encoding.UTF8.GetByteCount(Log), report.SourceRecordsChecked == 5 ? left.Snapshot.Manifest.Source.Length : -1);
    }

    [Fact]
    public async Task HeatMapCountsReconcileWithDetailsUnderFilter()
    {
        await using var imported = await ImportAsync([7, 11], workers: 3);
        var snapshot = imported.Snapshot;
        var range = snapshot.TimedRange!.Value;
        var filter = new FilterSpec { IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagA") };
        var heat = SessionQueryEngine.QueryHeatMap(snapshot, new Viewport(range, 13), filter, 1);
        var heatTotal = heat.Counts.Values.Sum(static values => values.Sum());
        var details = SessionQueryEngine.GetEntries(snapshot, range, filter, EntryOrder.Chronological, null, 100, 1);
        Assert.Equal(details.Entries.Count, heatTotal);
        Assert.Equal(2, heatTotal);
        Assert.True(heat.HasUnknown);
    }

    [Fact]
    public async Task NamedBucketCountsReconcileWithDetailsAndRepeatIdentically()
    {
        // §7.5/§12.3: the named-width bucket family obeys the same count-reconciliation
        // invariant as pixel columns, and the composed-bitmap cache introduced for it
        // must return identical counts on a repeat query.
        await using var imported = await ImportAsync([9, 33], workers: 2);
        var snapshot = imported.Snapshot;
        var range = snapshot.TimedRange!.Value;
        var filter = new FilterSpec { IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagA") };
        var width = new BucketWidth(1_000);
        var first = SessionQueryEngine.QueryNamedBuckets(
            snapshot, range, width, BucketAlignment.UnixEpoch, filter, 1);
        var second = SessionQueryEngine.QueryNamedBuckets(
            snapshot, range, width, BucketAlignment.UnixEpoch, filter, 2);
        Assert.Equal(first.Select(static cell => (cell.Range, cell.Level, cell.Count)),
            second.Select(static cell => (cell.Range, cell.Level, cell.Count)));

        var bucketTotal = first.Sum(static cell => cell.Count);
        var details = SessionQueryEngine.GetEntries(snapshot, range, filter, EntryOrder.Chronological, null, 100, 1);
        Assert.Equal(details.Entries.Count, bucketTotal);
        Assert.True(bucketTotal > 0);
    }

    [Fact]
    public async Task PartitioningARangeIntoCellsPreservesTheWholeRangeCount()
    {
        // §20.3: the sum of cell counts over a partition equals the count over the whole
        // range, at every column count — no entry may be double-counted or dropped at a
        // boundary. Uses the out-of-order fixture so segment boundaries participate.
        await using var imported = await ImportAsync([5, 64], workers: 3);
        var snapshot = imported.Snapshot;
        var range = snapshot.TimedRange!.Value;
        var whole = SessionQueryEngine.GetEntries(
            snapshot, range, FilterSpec.All, EntryOrder.Chronological, null, 1000, 1).TotalCount;

        Assert.True(whole > 0);
        foreach (var columns in new[] { 1, 2, 3, 7, 16, 97, 512 })
        {
            var heat = SessionQueryEngine.QueryHeatMap(
                snapshot, new Viewport(range, columns), FilterSpec.All, columns);
            var partitioned = heat.Counts.Values.Sum(static values => values.Sum());
            Assert.Equal(whole, partitioned);

            // Adjacent columns must abut exactly: no gap and no overlap.
            for (var column = 1; column < heat.Columns.Count; column++)
            {
                Assert.Equal(heat.Columns[column - 1].EndExclusive, heat.Columns[column].StartInclusive);
            }
        }
    }

    [Fact]
    public async Task AddingAFilterDimensionNeverIncreasesTheMatchCount()
    {
        // §20.3: filter intersections never exceed their operands.
        await using var imported = await ImportAsync([23], workers: 2);
        var snapshot = imported.Snapshot;
        long Count(FilterSpec filter) => SessionQueryEngine.QueryStatistics(snapshot, filter, 1).TimedMatching;

        var unfiltered = Count(FilterSpec.All);
        var byTag = FilterSpec.All with { IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagA") };
        var byLevel = FilterSpec.All with { IncludedLevels = ImmutableHashSet.Create(LogLevel.Debug) };
        var both = byTag with { IncludedLevels = byLevel.IncludedLevels };

        Assert.True(Count(byTag) <= unfiltered);
        Assert.True(Count(byLevel) <= unfiltered);
        Assert.True(Count(both) <= Count(byTag));
        Assert.True(Count(both) <= Count(byLevel));

        // An excluded value can only remove entries the inclusive filter admitted.
        var excluded = byTag with { ExcludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagB") };
        Assert.True(Count(excluded) <= Count(byTag));
    }

    [Fact]
    public async Task IncludingAndExcludingAFacetValuePartitionTheSession()
    {
        // Every facet control the panel offers must mean the same thing: include keeps
        // exactly the matching entries, exclude keeps exactly the rest, and the two
        // partition the unfiltered population (§12.6, §14.11). Each dimension is also
        // checked against a neighbouring one so a shared filter-bitmap cache key would
        // surface here as a count that belongs to the wrong dimension.
        await using var imported = await ImportAsync([29], workers: 2);
        var snapshot = imported.Snapshot;
        long Count(FilterSpec filter) => SessionQueryEngine.QueryStatistics(snapshot, filter, 1).TimedMatching;
        var total = Count(FilterSpec.All);

        var byPid = FilterSpec.All with { IncludedPids = ImmutableHashSet.Create(1073) };
        var withoutPid = FilterSpec.All with { ExcludedPids = ImmutableHashSet.Create(1073) };
        Assert.Equal(2, Count(byPid));
        Assert.Equal(total, Count(byPid) + Count(withoutPid));

        var byTid = FilterSpec.All with { IncludedTids = ImmutableHashSet.Create(1151) };
        var withoutTid = FilterSpec.All with { ExcludedTids = ImmutableHashSet.Create(1151) };
        Assert.Equal(1, Count(byTid));
        Assert.Equal(total, Count(byTid) + Count(withoutTid));

        var byBuffer = FilterSpec.All with { IncludedBuffers = ImmutableHashSet.Create(StringComparer.Ordinal, "main") };
        var withoutBuffer = FilterSpec.All with { ExcludedBuffers = ImmutableHashSet.Create(StringComparer.Ordinal, "main") };
        Assert.Equal(total, Count(byBuffer) + Count(withoutBuffer));
        Assert.Equal(0, Count(byBuffer with { ExcludedBuffers = byBuffer.IncludedBuffers.Clear().Add("main") }));

        // Excluding a value the include set does not name leaves the include set alone,
        // and the details agree with the statistics under the same filter.
        var page = SessionQueryEngine.GetEntries(
            snapshot,
            snapshot.TimedRange!.Value,
            withoutPid,
            EntryOrder.Chronological,
            null,
            100,
            1);
        Assert.Equal(Count(withoutPid), page.Entries.Count);
        Assert.DoesNotContain(page.Entries, static entry => entry.Pid == 1073);
    }

    [Fact]
    public async Task CellPatternRankingIsRestrictedToOneSeverityRow()
    {
        // The hover readout asks for the dominant pattern of one cell at one severity;
        // that ranking must not leak entries from the other rows of the same interval
        // (§14.7).
        await using var imported = await ImportAsync([31], workers: 2);
        var snapshot = imported.Snapshot;
        var range = snapshot.TimedRange!.Value;
        var whole = new TimeRange(range.StartInclusive, new InstantUs(range.EndExclusive.Value + 1));

        var debug = SessionQueryEngine.QueryTopTemplates(snapshot, whole, FilterSpec.All, 10, 1, LogLevel.Debug);
        var warn = SessionQueryEngine.QueryTopTemplates(snapshot, whole, FilterSpec.All, 10, 1, LogLevel.Warn);
        var everything = SessionQueryEngine.QueryTopTemplates(snapshot, whole, FilterSpec.All, 10, 1);

        Assert.Equal(1, debug.Sum(static template => template.Count));
        Assert.Equal(1, warn.Sum(static template => template.Count));
        Assert.Equal(
            everything.Sum(static template => template.Count),
            debug.Concat(warn).Sum(static template => template.Count) +
            SessionQueryEngine.QueryTopTemplates(snapshot, whole, FilterSpec.All, 10, 1, LogLevel.Unknown)
                .Sum(static template => template.Count));
        Assert.DoesNotContain(warn, warnTemplate => debug.Any(other => other.TemplateId == warnTemplate.TemplateId));
    }

    [Fact]
    public async Task SegmentationDoesNotChangeTheEntryMultisetOrItsOrder()
    {
        // §20.3: sorting and compaction preserve the entry multiset and the
        // (timestamp, sequence) tie-break. Two imports of identical bytes that differ
        // only in segment size must land on the same chronological sequence.
        var log = string.Concat(Enumerable.Repeat(Log, 40));
        async Task<(string Root, IReadOnlyList<(long Ts, long Seq, string Message)> Entries, int Segments)> ReadAsync(int segmentEntries)
        {
            var root = Path.Combine(Path.GetTempPath(), $"visualcat-seg-{segmentEntries}-{Guid.NewGuid():N}.vcat");
            await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(log), [997]);
            var settings = Settings(3) with { SegmentEntries = segmentEntries };
            var result = await SessionCoordinator.ImportAsync(source, root, settings);
            using var snapshot = result.Snapshot;
            var page = SessionQueryEngine.GetEntries(
                snapshot,
                snapshot.TimedRange!.Value,
                FilterSpec.All,
                EntryOrder.Chronological,
                null,
                10_000,
                1);
            return (root, page.Entries.Select(static e => (e.Timestamp!.Value.Value, e.SourceSequence, e.Message)).ToArray(), snapshot.Segments.Count);
        }

        var fine = await ReadAsync(3);
        var coarse = await ReadAsync(100_000);
        try
        {
            Assert.True(fine.Segments >= 1);
            Assert.Equal(coarse.Entries, fine.Entries);

            // And the order itself is the declared one.
            Assert.Equal(
                fine.Entries.OrderBy(static e => e.Ts).ThenBy(static e => e.Seq).ToArray(),
                fine.Entries);
        }
        finally
        {
            Directory.Delete(fine.Root, true);
            Directory.Delete(coarse.Root, true);
        }
    }

    [Fact]
    public async Task SourceOrderPagingDoesNotAssumeTimestampSortedSequences()
    {
        await using var imported = await ImportAsync([9], workers: 2);
        var range = imported.Snapshot.TimedRange!.Value;
        var first = SessionQueryEngine.GetEntries(
            imported.Snapshot,
            range,
            FilterSpec.All,
            EntryOrder.SourceSequence,
            null,
            2,
            1);
        var second = SessionQueryEngine.GetEntries(
            imported.Snapshot,
            range,
            FilterSpec.All,
            EntryOrder.SourceSequence,
            first.NextCursor,
            2,
            2);

        Assert.Equal(["alpha 1000", "beta 2000"], first.Entries.Select(static entry => entry.Message));
        Assert.Equal(["gamma 3000"], second.Entries.Select(static entry => entry.Message));
        Assert.Equal(3, first.TotalCount);

        var exactPage = SessionQueryEngine.GetEntries(
            imported.Snapshot,
            range,
            FilterSpec.All,
            EntryOrder.SourceSequence,
            null,
            3,
            3);
        Assert.Null(exactPage.NextCursor);
    }

    [Fact]
    public async Task FacetsOmitTheirOwnSelectedDimension()
    {
        await using var imported = await ImportAsync([17], workers: 2);
        var filter = new FilterSpec
        {
            IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "TagA"),
        };
        var statistics = SessionQueryEngine.QueryStatistics(imported.Snapshot, filter, 1);

        Assert.Equal(2, statistics.TotalMatching);
        Assert.Contains(statistics.Tags, static facet => facet.Value == "TagA" && facet.Count == 2);
        Assert.Contains(statistics.Tags, static facet => facet.Value == "TagB" && facet.Count == 1);
    }

    [Fact]
    public async Task RawExportIsByteFaithfulAndSearchIsCancellable()
    {
        await using var imported = await ImportAsync([13], workers: 2);
        var destination = Path.Combine(imported.Root, "filtered.log");
        var range = imported.Snapshot.TimedRange!.Value;
        await ExportService.ExportRawAsync(
            imported.Snapshot,
            destination,
            range,
            new FilterSpec { IncludedLevels = ImmutableHashSet.Create(LogLevel.Warn) },
            EntryOrder.SourceSequence);
        var exported = await File.ReadAllTextAsync(destination);
        Assert.Equal("05-15 14:13:37.498  1073  1152 W TagB: beta 2000\n", exported);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SessionQueryEngine.SearchAsync(
                imported.Snapshot,
                new TextSearchSpec("alpha"),
                FilterSpec.All,
                1,
                cancellationToken: cancelled.Token));

        var cancelledExport = Path.Combine(imported.Root, "cancelled.csv");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExportService.ExportNormalizedCsvAsync(
                imported.Snapshot,
                cancelledExport,
                range,
                FilterSpec.All,
                EntryOrder.Chronological,
                cancelled.Token));
        Assert.False(File.Exists(cancelledExport));
        Assert.Empty(Directory.GetFiles(imported.Root, "cancelled.csv.tmp-*"));
    }

    [Fact]
    public async Task CancellationCreatesRecognizablePartialSession()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-cancel-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 1000))),
            [32],
            TimeSpan.FromMilliseconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SessionCoordinator.ImportAsync(source, root, Settings(2), cancellationToken: cancellation.Token));
        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(root, "manifest.json")) || Directory.Exists(Path.Combine(root, "source-order")));
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task GracefulStopDrainsAndFinalizesLiveSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-stop-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 1000))),
            [32],
            TimeSpan.FromMilliseconds(5));
        using var stop = new CancellationTokenSource();
        var progress = new InlineProgress<ProgressSnapshot>(value =>
        {
            if (value.LinesCommitted >= 3)
            {
                stop.Cancel();
            }
        });
        var result = await SessionCoordinator.ImportAsync(
            source,
            root,
            Settings(2),
            progress,
            gracefulStopToken: stop.Token);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(SessionState.Ready, imported.Snapshot.Descriptor.State);
        Assert.True(imported.Snapshot.Descriptor.Finalized);
        Assert.True(imported.Snapshot.Descriptor.Counters.TimedEntries > 0);
    }

    [Fact]
    public async Task RawContextSeeksByIndexAndMatchesTheScanFallback()
    {
        // 60 repetitions gives 300 source records, so a mid-session window exercises a
        // real seek rather than landing at offset zero either way.
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-rawctx-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(
                         Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 60))),
                         [4096]))
        {
            var created = await SessionCoordinator.ImportAsync(source, root, Settings(2));
            created.Snapshot.Dispose();
        }

        try
        {
            var indexPath = Path.Combine(root, "source-order", "index.bin");
            Assert.True(File.Exists(indexPath), "Ingest did not write the source-order index.");

            IReadOnlyList<SourceRecord> Indexed()
            {
                using var snapshot = SessionStore.OpenAsync(root).GetAwaiter().GetResult();
                return SessionQueryEngine.GetRawContext(snapshot, 150, 3, 4);
            }

            var withIndex = Indexed();
            Assert.Equal(8, withIndex.Count);
            Assert.Equal(147, withIndex[0].Sequence);
            Assert.Equal(154, withIndex[^1].Sequence);

            var verified = await SessionVerifier.VerifyAsync(root, verifyRawHash: false);
            Assert.True(verified.IsValid, string.Join(Environment.NewLine, verified.Issues.Select(static i => i.Message)));

            // A session written before the sidecar existed must still answer correctly.
            var saved = await File.ReadAllBytesAsync(indexPath);
            File.Delete(indexPath);
            Assert.Equal(withIndex, Indexed());

            // A corrupt sidecar is caught rather than silently returning wrong context.
            var corrupt = (byte[])saved.Clone();
            BitConverter.GetBytes(9_999_999L).CopyTo(corrupt, 8 * 150);
            await File.WriteAllBytesAsync(indexPath, corrupt);
            var report = await SessionVerifier.VerifyAsync(root, verifyRawHash: false);
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue => issue.Code == "source.index");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SlowLiveSourceCommitsWithoutWaitingToFillABatch()
    {
        // §10.6: committed data must become viewable while acquisition continues. A
        // trickling device never reaches a multi-megabyte batch boundary, so a size-only
        // trigger publishes no snapshot at all and the workspace stays empty for the
        // whole capture.
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-live-latency-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 400))),
            [200],
            TimeSpan.FromMilliseconds(2));
        using var stop = new CancellationTokenSource();
        var committedWhileRunning = 0L;
        var progress = new InlineProgress<ProgressSnapshot>(value =>
        {
            if (value.TerminalState is null && value.LinesCommitted > 0)
            {
                Interlocked.CompareExchange(ref committedWhileRunning, value.LinesCommitted, 0);
                stop.Cancel();
            }
        });

        // A batch size no realistic trickle will reach, so only the latency bound can
        // close a batch.
        var settings = Settings(2) with { BatchBytes = 8 * 1024 * 1024, BatchLatencyMilliseconds = 100 };
        var result = await SessionCoordinator.ImportAsync(source, root, settings, progress, gracefulStopToken: stop.Token);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.True(
            Volatile.Read(ref committedWhileRunning) > 0,
            "No lines were committed before the capture ended: batches only closed on size.");
    }

    [Fact]
    public async Task GracefulStopPublishesBytesBufferedSinceTheLastBatchBoundary()
    {
        // A stop interrupts the source enumerator mid-batch. Everything buffered since
        // the last boundary must still reach the store: discarding it loses up to a
        // whole batch of captured log while the session still reports success (§13.7).
        // Sized so the run ends well inside one 1 MB batch.
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-stop-tail-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 200))),
            [512],
            TimeSpan.FromMilliseconds(1));
        using var stop = new CancellationTokenSource();
        var progress = new InlineProgress<ProgressSnapshot>(value =>
        {
            if (value.LinesCommitted >= 12)
            {
                stop.Cancel();
            }
        });

        var settings = Settings(2) with { BatchBytes = 1024 * 1024, SegmentEntries = 100_000 };
        var result = await SessionCoordinator.ImportAsync(source, root, settings, progress, gracefulStopToken: stop.Token);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(SessionState.Ready, imported.Snapshot.Descriptor.State);
        Assert.True(
            imported.Snapshot.Descriptor.Counters.TimedEntries > 0,
            "A graceful stop inside the first batch discarded every buffered line.");

        // Raw capture and the index must agree about what was committed.
        var raw = await File.ReadAllBytesAsync(Path.Combine(root, "raw.log"));
        Assert.Equal(imported.Snapshot.Descriptor.Counters.SourceBytes, raw.Length);
    }

    [Fact]
    public async Task CommitFailureReclaimsPipelineInsteadOfBlockingOnFullChannels()
    {
        // A fault on the commit loop leaves parse workers parked in WriteAsync against a
        // bounded channel nobody drains. Without an abort token they never return, the
        // source stays open, and the import never completes (§10.7).
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-commit-fault-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 4000))),
            [64]);
        var reports = 0;
        var progress = new InlineProgress<ProgressSnapshot>(_ =>
        {
            if (Interlocked.Increment(ref reports) == 1)
            {
                throw new InvalidOperationException("injected commit-stage fault");
            }
        });

        var import = SessionCoordinator.ImportAsync(source, root, Settings(4), progress);
        var completed = await Task.WhenAny(import, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(import, completed);

        var failure = await Assert.ThrowsAsync<SessionPipelineException>(() => import);
        Assert.Equal("injected commit-stage fault", failure.GetBaseException().Message);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task VerifierRejectsTruncatedAndOversizedSourceRecordStrings()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), $"visualcat-corrupt-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), [4096]))
        {
            var result = await SessionCoordinator.ImportAsync(source, firstRoot, Settings(1));
            result.Snapshot.Dispose();
        }

        try
        {
            var records = Path.Combine(firstRoot, "source-order", "records.bin");
            await using (var stream = new FileStream(records, FileMode.Append, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(5L);
                writer.Write((long)Encoding.UTF8.GetByteCount(Log));
                writer.Write(0);
                writer.Write((byte)ParseOutcomeKind.MetaRecord);
                writer.Write(-1L);
                writer.Write((byte)0x81);
                writer.Write((byte)0x80);
                writer.Write((byte)0x04);
            }

            var report = await SessionVerifier.VerifyAsync(firstRoot, verifyRawHash: false);
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue => issue.Code == "session.open");
        }
        finally
        {
            Directory.Delete(firstRoot, true);
        }
    }

    [Fact]
    public async Task CorruptedSessionsFailControllablyRatherThanCrashingTheReader()
    {
        // §20.8: manifest and segment reader fuzzing, including corrupted length and
        // offset tables. Every outcome must be either a clean open or a declared
        // exception — never an out-of-bounds read, a runaway allocation, or a silently
        // wrong answer that the verifier also fails to notice.
        var pristine = Path.Combine(Path.GetTempPath(), $"visualcat-fuzz-src-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(
                         Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Log, 30))),
                         [512]))
        {
            var created = await SessionCoordinator.ImportAsync(source, pristine, Settings(2));
            created.Snapshot.Dispose();
        }

        var working = Path.Combine(Path.GetTempPath(), $"visualcat-fuzz-{Guid.NewGuid():N}.vcat");
        CopyDirectory(pristine, working);
        var targets = Directory.GetFiles(pristine, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(pristine, file))
            .ToArray();
        var random = new Random(0xC0FFEE);
        var opened = 0;
        var rejected = 0;
        try
        {
            for (var iteration = 0; iteration < 120; iteration++)
            {
                // One file is corrupted per round and restored afterwards, which keeps
                // the corpus identical without copying the whole session each time.
                var relative = targets[random.Next(targets.Length)];
                var path = Path.Combine(working, relative);
                try
                {
                    var bytes = await File.ReadAllBytesAsync(Path.Combine(pristine, relative));
                    if (bytes.Length == 0)
                    {
                        continue;
                    }

                    // Flip a byte, truncate, or extend — the three ways real corruption
                    // and partial writes present themselves.
                    switch (random.Next(3))
                    {
                        case 0:
                            bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                            await File.WriteAllBytesAsync(path, bytes);
                            break;
                        case 1:
                            await File.WriteAllBytesAsync(path, bytes[..random.Next(bytes.Length)]);
                            break;
                        default:
                            await File.WriteAllBytesAsync(path, [.. bytes, .. new byte[random.Next(1, 64)]]);
                            break;
                    }

                    try
                    {
                        using var snapshot = await SessionStore.OpenAsync(working);
                        var range = snapshot.TimedRange ?? new TimeRange(new InstantUs(0), new InstantUs(1));
                        SessionQueryEngine.QueryHeatMap(snapshot, new Viewport(range, 32), FilterSpec.All, 1);
                        SessionQueryEngine.QueryStatistics(snapshot, FilterSpec.All, 1);
                        SessionQueryEngine.GetEntries(
                            snapshot, range, FilterSpec.All, EntryOrder.Chronological, null, 200, 1);
                        SessionQueryEngine.GetRawContext(snapshot, 5, 3, 3);
                        opened++;
                    }
                    catch (Exception exception) when (
                        exception is InvalidDataException
                            or NotSupportedException
                            or EndOfStreamException
                            or JsonException
                            or IOException
                            or ArgumentException
                            or ArgumentOutOfRangeException
                            or OverflowException
                            or KeyNotFoundException
                            or FormatException
                            or UnauthorizedAccessException)
                    {
                        rejected++;
                    }
                }
                finally
                {
                    File.Copy(Path.Combine(pristine, relative), path, true);
                }
            }
        }
        finally
        {
            TryDelete(working);
            TryDelete(pristine);
        }

        // Both outcomes must occur, otherwise the corruption never reached the reader
        // or the reader accepts nothing and the assertion proves little.
        Assert.True(rejected > 0, "No corruption was rejected; the reader may not be validating.");
        Assert.True(opened > 0, "Every mutation was rejected; the fuzzer is not exercising the read path.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
        }
    }

    private static void TryDelete(string path)
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
            // Mapped segment files can linger briefly on Windows; the temp directory
            // is not the subject of this test.
        }
    }

    [Fact]
    public async Task VerifierDetectsAlteredColumnsAndReaderRefusesFutureMajorVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-column-corrupt-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), [4096]))
        {
            var result = await SessionCoordinator.ImportAsync(source, root, Settings(1));
            result.Snapshot.Dispose();
        }

        try
        {
            var column = Directory.GetFiles(root, "level.bin", SearchOption.AllDirectories).First();
            await using (var stream = new FileStream(column, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var original = stream.ReadByte();
                stream.Position = 0;
                stream.WriteByte((byte)(original ^ 0xff));
            }

            var report = await SessionVerifier.VerifyAsync(root, verifyRawHash: false);
            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, static issue => issue.Code == "segment.checksum");

            var manifestPath = Path.Combine(root, "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
            manifest["formatVersion"] = "3.0";
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            await Assert.ThrowsAsync<NotSupportedException>(() => SessionStore.OpenAsync(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ProcessNameEvidenceIsPersistedFacetedAndFilterable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-processes-{Guid.NewGuid():N}.vcat");
        await using var source = new ProcessMemorySource(Encoding.UTF8.GetBytes(Log), [17]);
        var result = await SessionCoordinator.ImportAsync(source, root, Settings(2));
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(2, imported.Snapshot.ProcessNames.Count);
        Assert.All(imported.Snapshot.ProcessNames, process => Assert.Equal("com.visualcat.sample", process.Name));
        var filter = FilterSpec.All with
        {
            IncludedProcesses = ImmutableHashSet.Create(StringComparer.Ordinal, "com.visualcat.sample"),
        };
        var statistics = SessionQueryEngine.QueryStatistics(imported.Snapshot, filter, 42);

        Assert.Equal(imported.Snapshot.Descriptor.Counters.TimedEntries, statistics.TimedMatching);
        Assert.Contains(statistics.Processes!, facet =>
            facet.Value == "com.visualcat.sample" &&
            facet.Count == imported.Snapshot.Descriptor.Counters.TimedEntries);
    }

    /// <summary>
    /// The shape a real device actually produces. <c>ps</c> reports the processes alive at
    /// the moment it runs, so every range it yields is one instant wide, and a live capture
    /// replays the device's existing ring buffer first — so the entries arrive stamped
    /// *earlier* than the only sample that describes them. Requiring the timestamp to fall
    /// inside the sampled range left every one of those entries showing a bare pid on a
    /// physical device while the suite stayed green, because the other fixture here hands
    /// out ranges spanning all of time. This asserts the realistic case instead.
    /// </summary>
    [Fact]
    public async Task ProcessNamesResolveForEntriesRecordedBeforeTheProcessListWasSampled()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-pointsample-{Guid.NewGuid():N}.vcat");
        var sampledAt = InstantUs.FromDateTimeOffset(DateTimeOffset.UtcNow.AddYears(5));
        await using var source = new ProcessMemorySource(Encoding.UTF8.GetBytes(Log), [17])
        {
            // One observation per process, zero width, taken after every log entry.
            Ranges =
            [
                new(1073, "com.visualcat.ringbuffer", sampledAt, sampledAt),
                new(1074, "com.visualcat.ringbuffer", sampledAt, sampledAt),
            ],
        };
        var result = await SessionCoordinator.ImportAsync(source, root, Settings(2));
        await using var imported = new ImportedSession(root, result.Snapshot);

        var statistics = SessionQueryEngine.QueryStatistics(imported.Snapshot, FilterSpec.All, 1);
        Assert.Contains(statistics.Processes!, static facet => facet.Value == "com.visualcat.ringbuffer");
        Assert.DoesNotContain(statistics.Processes!, static facet => facet.Value.StartsWith("PID ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The guarantee the fallback must not trade away: once a pid has been observed under
    /// two names, an entry between the observations keeps the name that was in effect then.
    /// Reusing a pid is normal on Android (§4.3.9), and merging both processes under the
    /// most recent name would silently misattribute the earlier one's entries.
    /// </summary>
    [Fact]
    public async Task AReusedPidKeepsTheNameThatWasInEffectAtEachInstant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-pidreuse-{Guid.NewGuid():N}.vcat");
        var entryInstant = InstantUs.FromDateTimeOffset(
            new DateTimeOffset(DateTime.UtcNow.Year, 5, 15, 14, 13, 37, TimeSpan.Zero));
        await using var source = new ProcessMemorySource(Encoding.UTF8.GetBytes(Log), [17])
        {
            Ranges =
            [
                new(1073, "com.first.owner", new InstantUs(entryInstant.Value - 60_000_000), new InstantUs(entryInstant.Value - 60_000_000)),
                new(1073, "com.second.owner", new InstantUs(entryInstant.Value + 60_000_000), new InstantUs(entryInstant.Value + 60_000_000)),
            ],
        };
        var result = await SessionCoordinator.ImportAsync(source, root, Settings(2));
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal("com.first.owner", imported.Snapshot.ResolveProcessName(1073, entryInstant));
        Assert.Equal(
            "com.second.owner",
            imported.Snapshot.ResolveProcessName(1073, new InstantUs(entryInstant.Value + 120_000_000)));

        // Before any observation there is no earlier name to contradict the first one.
        Assert.Equal(
            "com.first.owner",
            imported.Snapshot.ResolveProcessName(1073, new InstantUs(entryInstant.Value - 120_000_000)));
        Assert.Null(imported.Snapshot.ResolveProcessName(4242, entryInstant));
    }

    [Fact]
    public async Task MissingProcessNamesUseAFilterablePidFallback()
    {
        await using var imported = await ImportAsync([31], workers: 2);
        var statistics = SessionQueryEngine.QueryStatistics(imported.Snapshot, FilterSpec.All, 1);
        Assert.Contains(statistics.Processes!, static facet => facet.Value == "PID 1073" && facet.Count == 2);

        var filter = FilterSpec.All with
        {
            IncludedProcesses = ImmutableHashSet.Create(StringComparer.Ordinal, "PID 1073"),
        };
        var filtered = SessionQueryEngine.QueryStatistics(imported.Snapshot, filter, 2);
        Assert.Equal(2, filtered.TimedMatching);
    }

    [Fact]
    public async Task OversizedNewlineFreeInputIsSplitRejectedAndFullyCovered()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-long-line-{Guid.NewGuid():N}.vcat");
        var valid = Encoding.UTF8.GetBytes("05-15 14:13:37.496  1  1 I Seed: valid\n");
        var hostile = Enumerable.Repeat((byte)'x', 4_096).ToArray();
        var bytes = valid.Concat(hostile).ToArray();
        await using var source = new MemoryLogSource(bytes, [7, 13, 29]);
        var settings = Settings(2) with
        {
            BatchBytes = 64,
            MaximumLineBytes = 64,
            SegmentEntries = 8,
        };
        var result = await SessionCoordinator.ImportAsync(source, root, settings);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(1, imported.Snapshot.Descriptor.Counters.TimedEntries);
        Assert.True(imported.Snapshot.Descriptor.Counters.RejectedCandidates > 1);
        Assert.Equal(
            imported.Snapshot.Descriptor.Counters.RejectedCandidates,
            imported.Snapshot.Descriptor.Defects.LongLineOverflows);
        var report = await SessionVerifier.VerifyAsync(root);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(static issue => issue.Message)));
        Assert.Equal(bytes.Length, imported.Snapshot.Manifest.Source.Length);
    }

    [Fact]
    public async Task OversizedSingleChunkIsConsumedIncrementallyAndFullyCovered()
    {
        // A source is allowed to deliver a chunk much larger than MaximumLineBytes.
        // This specifically exercises the incremental fragment path rather than the
        // naturally small chunks used by the hostile-input coverage test above.
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-single-chunk-line-{Guid.NewGuid():N}.vcat");
        var bytes = Enumerable.Repeat((byte)'x', 8 * 1024).ToArray();
        await using var source = new MemoryLogSource(bytes, [bytes.Length]);
        var settings = Settings(2) with
        {
            BatchBytes = 4 * 1024,
            MaximumLineBytes = 256,
            SegmentEntries = 8,
        };
        var result = await SessionCoordinator.ImportAsync(source, root, settings);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(0, imported.Snapshot.Descriptor.Counters.TimedEntries);
        Assert.Equal(32, imported.Snapshot.Descriptor.Counters.RejectedCandidates);
        Assert.Equal(
            imported.Snapshot.Descriptor.Counters.RejectedCandidates,
            imported.Snapshot.Descriptor.Defects.LongLineOverflows);
        var report = await SessionVerifier.VerifyAsync(root);
        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(static issue => issue.Message)));
        Assert.Equal(bytes.Length, imported.Snapshot.Manifest.Source.Length);
    }

    [Fact]
    public async Task UntimedEntriesAreStreamedToACompleteReopenableSidecar()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-untimed-{Guid.NewGuid():N}.vcat");
        var text = string.Concat(Enumerable.Range(0, 5_000).Select(index => $"I/Brief( 42): message {index}\n"));
        await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(text), [137]);
        var settings = Settings(3) with
        {
            FormatOverride = LogcatFormat.Brief,
            SegmentEntries = 32,
        };
        var result = await SessionCoordinator.ImportAsync(source, root, settings);
        await using var imported = new ImportedSession(root, result.Snapshot);

        Assert.Equal(5_000, imported.Snapshot.Descriptor.Counters.UntimedEntries);
        var path = Path.Combine(root, "source-order", "untimed.json");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entries = await JsonSerializer.DeserializeAsync<NormalizedEntry[]>(
            stream,
            WebJson);
        Assert.NotNull(entries);
        Assert.Equal(5_000, entries.Length);
        Assert.All(entries, static entry => Assert.Null(entry.Timestamp));
    }

    [Fact]
    public async Task FileProbeCapsANewlineFreeFirstRecord()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualcat-probe-{Guid.NewGuid():N}.log");
        await File.WriteAllBytesAsync(path, Enumerable.Repeat((byte)'x', 2 * 1024 * 1024).ToArray());
        try
        {
            await using var source = new FileLogSource(path, 64 * 1024);
            var lines = await source.ProbeAsync(200, CancellationToken.None);
            var sample = Assert.Single(lines);
            Assert.Equal(1024 * 1024, sample.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<ImportedSession> ImportAsync(int[] chunks, int workers)
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-test-{Guid.NewGuid():N}.vcat");
        await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), chunks);
        var result = await SessionCoordinator.ImportAsync(source, root, Settings(workers));
        return new ImportedSession(root, result.Snapshot);
    }

    private static IngestSettings Settings(int workers) =>
        new(
            LogcatFormat.ThreadTime,
            "utf-8",
            new TimestampPolicy(2025, "UTC", new DateTimeOffset(2025, 5, 16, 0, 0, 0, TimeSpan.Zero)),
            new TemplateSettings(),
            BatchBytes: 64,
            ChannelCapacity: 2,
            ParseWorkers: workers,
            SegmentEntries: 2,
            PortableRaw: true);

    private sealed class ProcessMemorySource : ILogSource, IProcessNameSource
    {
        private readonly MemoryLogSource _inner;

        public ProcessMemorySource(byte[] bytes, IReadOnlyList<int> chunkSizes)
        {
            _inner = new MemoryLogSource(bytes, chunkSizes);
        }

        public SourceMetadata Metadata => _inner.Metadata;

        public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(
            int maximumUsefulLines,
            CancellationToken cancellationToken) =>
            _inner.ProbeAsync(maximumUsefulLines, cancellationToken);

        public IAsyncEnumerable<SourceChunk> ReadAsync(
            SourceReadContext context,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(context, cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            _inner.StopAsync(cancellationToken);

        /// <summary>
        /// Observations this fake reports. The default spans all of time, which is
        /// convenient but not what a device produces; tests that care about the sampling
        /// shape override it.
        /// </summary>
        public IReadOnlyList<ProcessNameRange> Ranges { get; init; } =
        [
            new(1073, "com.visualcat.sample", new InstantUs(long.MinValue + 1), new InstantUs(long.MaxValue)),
            new(1074, "com.visualcat.sample", new InstantUs(long.MinValue + 1), new InstantUs(long.MaxValue)),
        ];

        public Task<IReadOnlyList<ProcessNameRange>> GetProcessNamesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Ranges);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class ImportedSession(string root, SessionSnapshot snapshot) : IAsyncDisposable
    {
        public string Root { get; } = root;
        public SessionSnapshot Snapshot { get; } = snapshot;

        public ValueTask DisposeAsync()
        {
            Snapshot.Dispose();
            Directory.Delete(Root, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
