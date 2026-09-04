using System.Collections.Immutable;
using System.Text.Json;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Tests;

/// <summary>
/// Covers the properties that keep a long-running session affordable: how many segments
/// it accumulates, how many mappings a reader holds open, and whether reopening it shares
/// what it already has. A capture that ran a little over an hour used to exhaust the
/// process descriptor limit and fail; each test here fails if one of the reasons returns.
/// </summary>
public sealed class SessionStoreTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CompactionBoundsSegmentCountAndPreservesEveryEntryInOrder()
    {
        using var session = new TemporarySession();

        // Two entries per flush is what a quiet source produces against a time-triggered
        // ceiling. Without compaction this is one segment per flush, forever.
        const int flushes = 160;
        const int perFlush = 2;
        await using var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 1024), Identity());
        var sequence = 0L;
        for (var flush = 0; flush < flushes; flush++)
        {
            for (var entry = 0; entry < perFlush; entry++)
            {
                writer.AddEntry(Entry(sequence++));
            }

            writer.FlushSegment();
        }

        // A base-8 counter keeps at most seven segments per level, and 320 entries in
        // two-entry units reaches three levels, so a healthy ladder settles in the low
        // tens. Without one this is 160 — one per flush, growing for as long as the
        // capture runs.
        Assert.True(
            writer.SegmentCount <= 24,
            $"Compaction left {writer.SegmentCount} segments for {flushes} flushes; the ladder is not folding them.");
        Assert.Equal(0, writer.CompactionFailures);
        Assert.True(writer.CompactedSegments > 0);

        await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);

        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var read = snapshot.Segments
            .SelectMany(segment => Enumerable
                .Range(0, segment.Count)
                .Select(index => segment.ReadEntry(index, snapshot.SessionId, snapshot.Tags, snapshot.Buffers, "2")))
            .ToArray();
        Assert.Equal(flushes * perFlush, read.Length);
        Assert.Equal(Enumerable.Range(0, flushes * perFlush).Select(static value => (long)value), read.Select(static entry => entry.SourceSequence));
        Assert.Equal(
            Enumerable.Range(0, flushes * perFlush).Select(static value => $"message {value}"),
            read.Select(static entry => entry.Message));
    }

    [Fact]
    public async Task AFailureToSealASegmentRetainsTheEntriesInsteadOfEndingTheCapture()
    {
        using var session = new TemporarySession();
        Directory.CreateDirectory(Path.Combine(session.Root, "segments"));

        // Occupy the path the next segment will want. Any condition that stops a segment
        // being written — a descriptor shortage, a locked path, a briefly unwritable
        // volume — reaches the writer the same way, and none of them may cost the user a
        // capture that is otherwise healthy.
        await File.WriteAllTextAsync(Path.Combine(session.Root, "segments", "000001"), "in the way");

        await using var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 4), Identity());
        for (var sequence = 0; sequence < 4; sequence++)
        {
            Assert.Null(writer.AddEntry(Entry(sequence)));
        }

        Assert.NotNull(writer.DeferredFlushFailure);
        Assert.Equal(4, writer.PendingEntryCount);
        Assert.Equal(0, writer.SegmentCount);

        // The obstruction is gone by the time the next segment id is used, so the retry
        // succeeds and carries every retained entry with it.
        Assert.NotNull(writer.FlushSegment());
        Assert.Equal(0, writer.PendingEntryCount);

        await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.Equal(4, snapshot.Segments.Sum(static segment => segment.Count));
    }

    [Fact]
    public async Task SegmentsAreMappedOnUseSoAnIdleSessionHoldsNoDescriptors()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 4, perSegment: 3);

        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.NotEmpty(snapshot.Segments);

        // Opening validated every segment without retaining a single mapping. This is
        // what makes the number of segments in a session stop being a descriptor cost.
        Assert.Equal(0, snapshot.MappedColumnCount);

        _ = snapshot.Segments[0].TimestampAt(0);
        Assert.Equal(1, snapshot.MappedColumnCount);

        // Reading a whole entry needs the rest of the columns; the untouched segments
        // still cost nothing.
        _ = snapshot.Segments[0].ReadEntry(0, snapshot.SessionId, snapshot.Tags, snapshot.Buffers, "2");
        Assert.InRange(snapshot.MappedColumnCount, 2, 19);
        Assert.Equal(0, snapshot.Segments[^1].MappedColumnCount);
    }

    [Fact]
    public async Task ReopeningSharesSegmentsWithThePreviousSnapshotInsteadOfRemappingThem()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 5, perSegment: 2);

        using var first = await SessionStore.OpenAsync(session.Root);
        foreach (var segment in first.Segments)
        {
            _ = segment.TimestampAt(0);
        }

        var mappedBefore = first.MappedColumnCount;
        Assert.Equal(first.Segments.Count, mappedBefore);

        var reused = await SessionStore.OpenAsync(session.Root, first);
        try
        {
            Assert.Equal(first.Segments.Count, reused.Segments.Count);
            for (var index = 0; index < first.Segments.Count; index++)
            {
                Assert.Same(first.Segments[index], reused.Segments[index]);
            }

            // The point of sharing: two live snapshots of the same session cost one set
            // of mappings, not two. Doubling here is exactly what exhausted the limit.
            Assert.Equal(mappedBefore, reused.MappedColumnCount);
        }
        finally
        {
            reused.Dispose();
        }

        // Releasing the second snapshot must not close mappings the first still owns.
        Assert.Equal(mappedBefore, first.MappedColumnCount);
        Assert.Equal(first.Segments[0].TimestampAt(0), first.Segments[0].TimestampAt(0));
    }

    /// <summary>
    /// The other half of the sharing contract, and the invariant the pointer-based
    /// <c>MappedColumn</c> depends on for its safety: reads stay valid for exactly as long as
    /// a snapshot holds the segment, and the release that closes the mappings turns a later
    /// read into a managed exception rather than a read of unmapped memory.
    /// </summary>
    [Fact]
    public async Task MappingsSurviveEveryHeldReferenceAndAReadAfterTheLastReleaseIsAManagedFailure()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 5, perSegment: 2);

        var first = await SessionStore.OpenAsync(session.Root);
        var second = await SessionStore.OpenAsync(session.Root, first);
        var segment = first.Segments[0];
        var expected = segment.TimestampAt(0);
        var expectedMessage = segment.ReadEntry(0, first.SessionId, first.Tags, first.Buffers, "2").Message;
        var mapped = first.MappedColumnCount;
        Assert.True(mapped > 0);

        // Releasing one holder of a shared segment leaves every column readable through the
        // other, payload included: the reference count, not disposal order, owns the mapping.
        second.Dispose();
        Assert.Equal(expected, segment.TimestampAt(0));
        Assert.Equal(
            expectedMessage,
            segment.ReadEntry(0, first.SessionId, first.Tags, first.Buffers, "2").Message);
        Assert.Equal(mapped, first.MappedColumnCount);

        // The last release closes them, and a caller that broke the contract by reading on
        // gets ObjectDisposedException instead of whatever the freed mapping now holds.
        first.Dispose();
        Assert.Equal(0, first.MappedColumnCount);
        Assert.Throws<ObjectDisposedException>(() => segment.TimestampAt(0));
        Assert.Throws<ObjectDisposedException>(
            () => segment.ReadEntry(0, first.SessionId, first.Tags, first.Buffers, "2"));

        // Repeated disposal cannot release the pointer a second time.
        first.Dispose();
        second.Dispose();
        Assert.Throws<ObjectDisposedException>(() => segment.TimestampAt(0));
    }

    [Fact]
    public async Task OpeningWithoutReuseProducesIndependentSegments()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 2, perSegment: 2);

        using var first = await SessionStore.OpenAsync(session.Root);
        using var second = await SessionStore.OpenAsync(session.Root);
        Assert.NotSame(first.Segments[0], second.Segments[0]);

        second.Dispose();

        // Disposing one must leave the other usable, which is only true if their
        // lifetimes are genuinely separate.
        Assert.True(first.Segments[0].TimestampAt(0) > 0);
    }

    [Fact]
    public async Task ChecksumsLiveBesideTheSegmentAndKeepTheManifestSmall()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 6, perSegment: 2);

        var manifestBytes = new FileInfo(Path.Combine(session.Root, "manifest.json")).Length;
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        foreach (var segment in snapshot.Segments)
        {
            Assert.Null(segment.Manifest.Checksums);
            Assert.True(File.Exists(Path.Combine(segment.DirectoryPath, "checksums.json")));
        }

        // Twenty-six digests per segment in the manifest is about 7 KB each, and the
        // manifest is rewritten in full on every published snapshot.
        Assert.True(
            manifestBytes < 6 * 2048,
            $"Manifest is {manifestBytes} bytes for {snapshot.Segments.Count} segments; digests have leaked back into it.");

        // The verifier must still be able to check every segment file against a digest
        // now that the digests are not in the manifest. Source-order and summary issues
        // are expected here: this session is written by hand, not by the pipeline.
        var report = await SessionVerifier.VerifyAsync(session.Root, verifyRawHash: false);
        var segmentIssues = report.Issues
            .Where(static issue => issue.Code.StartsWith("segment.", StringComparison.Ordinal) ||
                                   issue.Code.StartsWith("bitmap.", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(segmentIssues.Select(static issue => issue.Message));
    }

    [Fact]
    public async Task TemplateRevisionsLiveInALazySidecarAndLegacyEmbeddedTablesStillOpen()
    {
        using var session = new TemporarySession();
        var first = Definition("started service", 1);
        var revised = Definition("started <*>", 2);
        await using (var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 4), Identity()))
        {
            await writer.PublishSnapshotAsync(Descriptor(), [first], [], CancellationToken.None);
            await writer.FinalizeAsync(Descriptor(), [revised], [], CancellationToken.None);
        }

        var manifestPath = Path.Combine(session.Root, "manifest.json");
        var manifestBytes = new FileInfo(manifestPath).Length;
        var manifest = JsonSerializer.Deserialize<SessionManifest>(
            await File.ReadAllTextAsync(manifestPath),
            WebJson)!;
        Assert.Empty(manifest.Templates!);
        Assert.True(manifest.TemplateSidecarLength > 0);
        Assert.True(manifestBytes < 8 * 1024);

        using (var snapshot = await SessionStore.OpenAsync(session.Root))
        {
            var loaded = Assert.Single(snapshot.Templates);
            Assert.Equal(revised.CanonicalText, loaded.CanonicalText);
            Assert.Equal(2, loaded.MatchCount);
        }

        // Compatibility path: releases before the sidecar embedded the complete table.
        var legacy = manifest with { Templates = [revised], TemplateSidecarLength = null };
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(legacy, WebJson));
        // Removing every sidecar the writer can produce is what makes the assertions below
        // evidence that the embedded table is being read rather than the file.
        File.Delete(Path.Combine(session.Root, "templates.jsonl"));
        File.Delete(Path.Combine(session.Root, "templates-final.jsonl"));
        using var legacySnapshot = await SessionStore.OpenAsync(session.Root);
        var legacyDefinition = Assert.Single(legacySnapshot.Templates);
        Assert.Equal(revised.TemplateId, legacyDefinition.TemplateId);
        Assert.Equal(revised.CanonicalText, legacyDefinition.CanonicalText);
        Assert.Equal(revised.Tokens, legacyDefinition.Tokens);
        Assert.Equal(revised.MatchCount, legacyDefinition.MatchCount);
    }

    [Fact]
    public async Task ASegmentMissingFromDiskIsRefusedWhenTheSessionOpens()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 3, perSegment: 2);

        var segments = Directory.GetDirectories(Path.Combine(session.Root, "segments")).Order(StringComparer.Ordinal).ToArray();
        File.Delete(Path.Combine(segments[^1], "timestamp.bin"));

        // Mapping lazily must not turn a damaged session into one that opens cleanly and
        // then throws in the middle of a query.
        await Assert.ThrowsAnyAsync<IOException>(() => SessionStore.OpenAsync(session.Root));
    }

    [Fact]
    public async Task FinalizeWaitsForAWindowsScannerHoldingThePublishedManifest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var session = new TemporarySession();
        await using var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 4), Identity());
        writer.AddEntry(Entry(0));
        await writer.PublishSnapshotAsync(Descriptor(), [], [], CancellationToken.None);

        // Windows refuses an atomic replacement while any reader omitted delete sharing.
        // Hold the manifest longer than the old 420 ms retry budget to model an indexer or
        // antivirus scan; completing the import is preferable to losing it at the last step.
        var manifestPath = Path.Combine(session.Root, "manifest.json");
        using var heldByScanner = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var releaseScanner = Task.Run(async () =>
        {
            await Task.Delay(900);
            heldByScanner.Dispose();
        });

        await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);
        await releaseScanner;

        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.True(snapshot.Manifest.Finalized);
        Assert.Single(snapshot.Segments);
    }

    /// <summary>
    /// The filter fingerprint is a SHA-256 over the whole filter; recomputing it once per
    /// segment made a query's identity work scale with segment count instead of with the
    /// query. It is hoisted, and every segment is therefore keyed with the very same string
    /// instance — reference identity is what a per-segment recomputation could not produce.
    /// </summary>
    [Fact]
    public async Task OneQueryFingerprintsItsFilterOnceAndKeysEverySegmentWithThatSameString()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 6, perSegment: 4);

        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.True(snapshot.Segments.Count > 1, "The invariant is only observable across segments.");

        var keys = new List<string>();
        foreach (var segment in snapshot.Segments)
        {
            segment.BitmapFactoryStartedForTests = key => { lock (keys) { keys.Add(key); } };
        }

        // A constrained filter, so ActiveBitmap actually builds rather than short-circuiting
        // on FilterSpec.All.
        var filter = FilterSpec.All with { IncludedLevels = ImmutableHashSet.Create(LogLevel.Error) };
        var expected = filter.Fingerprint();
        _ = SessionQueryEngine.QueryStatistics(snapshot, filter, queryGeneration: 1);

        var activeKeys = keys.Where(key => string.Equals(key, expected, StringComparison.Ordinal)).ToArray();
        Assert.Equal(snapshot.Segments.Count, activeKeys.Length);
        Assert.All(activeKeys, key => Assert.Same(activeKeys[0], key));
    }

    private static async Task WriteSessionAsync(string root, int segments, int perSegment)
    {
        await using var writer = new SessionStoreWriter(root, Settings(segmentEntries: 1024), Identity());
        var sequence = 0L;
        for (var segment = 0; segment < segments; segment++)
        {
            for (var entry = 0; entry < perSegment; entry++)
            {
                writer.AddEntry(Entry(sequence++));
            }

            writer.FlushSegment();

            // Keep every segment its own: the merge ladder needs MergeFactor of them
            // before it folds anything, and these tests want the segments they asked for.
            Assert.True(writer.SegmentCount > 0);
        }

        await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);
    }

    private static NormalizedEntry Entry(long sequence) => new(
        Guid.Empty,
        sequence,
        sequence,
        new RawSpan(sequence * 32, 32),
        new InstantUs(1_700_000_000_000_000 + (sequence * 1_000)),
        "05-15 14:13:37.496",
        TimestampProvenance.ExplicitUtc,
        1,
        1000 + (int)(sequence % 7),
        2000 + (int)(sequence % 3),
        LogLevels.StorageOrder[(int)(sequence % LogLevels.StorageOrder.Length)],
        $"Tag{sequence % 5}",
        "main",
        $"message {sequence}",
        LogcatFormat.ThreadTime,
        "2",
        0,
        EntryAttributes.None);

    private static IngestSettings Settings(int segmentEntries) => new(
        LogcatFormat.ThreadTime,
        "utf-8",
        new TimestampPolicy(2026, "UTC", DateTimeOffset.UnixEpoch),
        new TemplateSettings(),
        SegmentEntries: segmentEntries);

    private static TemplateDefinition Definition(string canonicalText, long matchCount) => new(
        1,
        canonicalText,
        "drain",
        "drain-v2",
        canonicalText.Split(' '),
        new InstantUs(1),
        new InstantUs(matchCount),
        matchCount,
        [1],
        "hash");

    private static SourceIdentity Identity() => new("memory", null, 0, null, string.Empty, true);

    private static SessionDescriptor Descriptor() => new(
        Guid.NewGuid(),
        "test",
        SourceKind.File,
        "test source",
        DateTimeOffset.UnixEpoch,
        SessionState.Ready,
        0,
        LogcatFormat.ThreadTime,
        1,
        new TimestampPolicy(2026, "UTC", DateTimeOffset.UnixEpoch),
        new TemplateSettings(),
        new SessionCounters(),
        new DefectCounters(),
        null,
        null,
        true,
        false);

    private sealed class TemporarySession : IDisposable
    {
        public TemporarySession()
        {
            Root = Path.Combine(Path.GetTempPath(), $"visualcat-store-{Guid.NewGuid():N}.vcat");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
