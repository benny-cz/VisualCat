using System.Collections.Immutable;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Tests;

/// <summary>
/// Statistics and facet tallies are cached per segment, so what they answer must not depend
/// on whether the cache is warm.
/// </summary>
/// <remarks>
/// <para>
/// Every published segment is immutable, which is what makes its contribution to a query a
/// constant worth keeping. The engine used to recompute all of it — the level and timestamp
/// summary plus one pass per facet dimension — over every entry in the session on every
/// published generation, which a live capture produces every few seconds. That is the cost
/// these tests exist to let the code avoid, and the risk they exist to catch: a cached
/// contribution that survives a change it should not have.
/// </para>
/// <para>
/// The comparison is always against a snapshot opened cold, because a cold snapshot cannot
/// be wrong: it holds no cache at all.
/// </para>
/// </remarks>
public sealed class StatisticsCacheTests
{
    [Fact]
    public async Task RepeatedQueriesOnOneSnapshotAgreeWithAColdSnapshot()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 6, perSegment: 40);

        using var warm = await SessionStore.OpenAsync(session.Root);
        foreach (var filter in Filters())
        {
            // Cold every time: a fresh snapshot has no cached contribution to reuse.
            using var cold = await SessionStore.OpenAsync(session.Root);
            var expected = SessionQueryEngine.QueryStatistics(cold, filter, 1);

            // Twice on the warm snapshot: the first populates the cache, the second reads it.
            _ = SessionQueryEngine.QueryStatistics(warm, filter, 2);
            var actual = SessionQueryEngine.QueryStatistics(warm, filter, 3);
            AssertSameStatistics(expected, actual);
        }
    }

    /// <summary>
    /// Interleaving filters must not let one filter's cached contribution answer another's
    /// question.
    /// </summary>
    [Fact]
    public async Task InterleavedFiltersDoNotBorrowEachOthersCachedContributions()
    {
        using var session = new TemporarySession();
        await WriteSessionAsync(session.Root, segments: 5, perSegment: 50);

        using var warm = await SessionStore.OpenAsync(session.Root);
        var filters = Filters().ToArray();
        var expected = new StatisticsResult[filters.Length];
        for (var i = 0; i < filters.Length; i++)
        {
            using var cold = await SessionStore.OpenAsync(session.Root);
            expected[i] = SessionQueryEngine.QueryStatistics(cold, filters[i], 1);
        }

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < filters.Length; i++)
            {
                AssertSameStatistics(expected[i], SessionQueryEngine.QueryStatistics(warm, filters[i], round));
            }
        }
    }

    /// <summary>
    /// The live shape: a session that grew, reopened sharing the segments it already had.
    /// </summary>
    /// <remarks>
    /// This is the case the cache exists for and the one it could most easily get wrong. The
    /// reopened snapshot shares every segment instance with its predecessor — that sharing is
    /// what keeps a live capture's mappings flat — so it inherits their caches too, and the
    /// totals must still count the segments that were added since.
    /// </remarks>
    [Fact]
    public async Task AGrownSessionReopenedOnSharedSegmentsCountsTheNewOnesToo()
    {
        using var session = new TemporarySession();
        await using var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 1024), Identity());
        var sequence = 0L;

        SessionSnapshot? previous = null;
        try
        {
            for (var generation = 1; generation <= 6; generation++)
            {
                for (var entry = 0; entry < 40; entry++)
                {
                    writer.AddEntry(Entry(sequence++));
                }

                writer.FlushSegment();
                await writer.PublishSnapshotAsync(Descriptor(), [], [], CancellationToken.None);

                var reopened = await SessionStore.OpenAsync(session.Root, previous);
                previous?.Dispose();
                previous = reopened;

                foreach (var filter in Filters())
                {
                    using var cold = await SessionStore.OpenAsync(session.Root);
                    var expected = SessionQueryEngine.QueryStatistics(cold, filter, generation);
                    var actual = SessionQueryEngine.QueryStatistics(previous, filter, generation);
                    AssertSameStatistics(expected, actual);
                }

                // Agreeing with a cold snapshot is most of it, but both could agree on a
                // total that stopped growing. Unfiltered, the answer is every entry written
                // so far and nothing less.
                Assert.Equal(
                    sequence,
                    SessionQueryEngine.QueryStatistics(previous, FilterSpec.All, generation).TimedMatching);
            }
        }
        finally
        {
            previous?.Dispose();
        }
    }

    /// <summary>
    /// A later process-name observation revises what earlier entries are attributed to, and
    /// the cached tally must not outlive it.
    /// </summary>
    /// <remarks>
    /// The process facet is the one dimension whose value is not a property of the segment
    /// alone: it resolves a pid through the session's process-name table, and a live capture
    /// keeps adding observations to that table as it samples the device. Every other tally
    /// reads only immutable segment columns and string-table ids, which are stable once
    /// written. So this is the single case where a cached contribution could be right when it
    /// was computed and wrong afterwards, and the cache key carries the table's size to stop
    /// that. The same pid is renamed here, which is exactly what pid reuse looks like.
    /// </remarks>
    [Fact]
    public async Task RenamingAProcessRetiresTheCachedProcessTally()
    {
        using var session = new TemporarySession();
        await using var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 1024), Identity());
        for (var i = 0; i < 40; i++)
        {
            writer.AddEntry(Entry(i) with { Pid = 4242 });
        }

        writer.FlushSegment();

        var firstSeen = new InstantUs(1_700_000_000_000_000);
        var renamedAt = new InstantUs(1_700_000_000_000_000 + (20 * 1_000));
        await writer.PublishSnapshotAsync(
            Descriptor(),
            [],
            [new ProcessNameRange(4242, "com.before", firstSeen, new InstantUs(renamedAt.Value - 1))],
            CancellationToken.None);

        using var before = await SessionStore.OpenAsync(session.Root);
        var beforeStats = SessionQueryEngine.QueryStatistics(before, FilterSpec.All, 1);
        Assert.Equal("com.before", Assert.Single(beforeStats.Processes!).Value);

        // The device reports that the pid now belongs to something else. Entries after the
        // rename are that something else, and the tally the first query cached says otherwise.
        await writer.PublishSnapshotAsync(
            Descriptor(),
            [],
            [
                new ProcessNameRange(4242, "com.before", firstSeen, new InstantUs(renamedAt.Value - 1)),
                new ProcessNameRange(4242, "com.after", renamedAt, new InstantUs(renamedAt.Value + 1_000_000)),
            ],
            CancellationToken.None);

        using var after = await SessionStore.OpenAsync(session.Root, before);
        var actual = SessionQueryEngine.QueryStatistics(after, FilterSpec.All, 2);

        using var cold = await SessionStore.OpenAsync(session.Root);
        AssertSameStatistics(SessionQueryEngine.QueryStatistics(cold, FilterSpec.All, 2), actual);
        Assert.Equal(
            ["com.after", "com.before"],
            actual.Processes!.Select(static value => value.Value).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A tally over a dimension with an entry per record is used and forgotten rather than
    /// cached, and it still answers correctly.
    /// </summary>
    /// <remarks>
    /// The guard that refuses to keep an oversized tally is the one piece of the cache that
    /// changes behaviour by size rather than by content, so it is exercised directly: every
    /// entry here carries its own thread id, which is the shape that trips it.
    /// </remarks>
    [Fact]
    public async Task AnOversizedFacetTallyIsStillCountedCorrectly()
    {
        using var session = new TemporarySession();
        await using (var writer = new SessionStoreWriter(session.Root, Settings(segmentEntries: 1024), Identity()))
        {
            for (var i = 0; i < 300; i++)
            {
                writer.AddEntry(Entry(i) with { Tid = 100_000 + i });
            }

            writer.FlushSegment();
            await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);
        }

        using var cold = await SessionStore.OpenAsync(session.Root);
        var expected = SessionQueryEngine.QueryStatistics(cold, FilterSpec.All, 1, facetLimit: 500);

        using var warm = await SessionStore.OpenAsync(session.Root);
        _ = SessionQueryEngine.QueryStatistics(warm, FilterSpec.All, 1, facetLimit: 500);
        var actual = SessionQueryEngine.QueryStatistics(warm, FilterSpec.All, 2, facetLimit: 500);

        Assert.Equal(300, actual.TimedMatching);
        Assert.Equal(300, actual.Tids.Count);
        AssertSameStatistics(expected, actual);
    }

    private static IEnumerable<FilterSpec> Filters()
    {
        yield return FilterSpec.All;
        yield return FilterSpec.All with { IncludedLevels = ImmutableHashSet.Create(LogLevel.Error) };
        yield return FilterSpec.All with { IncludedLevels = ImmutableHashSet.Create(LogLevel.Warn, LogLevel.Info) };
        yield return FilterSpec.All with
        {
            IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "Tag1", "Tag3"),
        };
        yield return FilterSpec.All with { ExcludedPids = ImmutableHashSet.Create(1001, 1002) };
        yield return FilterSpec.All with
        {
            Search = new TextSearchSpec("message 1", false, false, null),
        };
        yield return FilterSpec.All with
        {
            TimeRange = new TimeRange(
                new InstantUs(1_700_000_000_000_000 + (30 * 1_000)),
                new InstantUs(1_700_000_000_000_000 + (170 * 1_000))),
        };
    }

    private static void AssertSameStatistics(StatisticsResult expected, StatisticsResult actual)
    {
        Assert.Equal(expected.TotalMatching, actual.TotalMatching);
        Assert.Equal(expected.TimedMatching, actual.TimedMatching);
        Assert.Equal(expected.UntimedMatching, actual.UntimedMatching);
        Assert.Equal(expected.FirstInstant, actual.FirstInstant);
        Assert.Equal(expected.LastInstant, actual.LastInstant);
        Assert.Equal(
            expected.Levels.OrderBy(static pair => pair.Key).ToArray(),
            actual.Levels.OrderBy(static pair => pair.Key).ToArray());
        AssertSameFacet(expected.Tags, actual.Tags);
        AssertSameFacet(expected.Processes, actual.Processes);
        AssertSameFacet(expected.Pids, actual.Pids);
        AssertSameFacet(expected.Tids, actual.Tids);
        AssertSameFacet(expected.Buffers, actual.Buffers);
        AssertSameFacet(expected.Templates, actual.Templates);
    }

    private static void AssertSameFacet<T>(IReadOnlyList<FacetValue<T>>? expected, IReadOnlyList<FacetValue<T>>? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected is null, actual is null);
            return;
        }

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Value, actual[i].Value);
            Assert.Equal(expected[i].Count, actual[i].Count);
        }
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
        (uint)(sequence % 4),
        EntryAttributes.None);

    private static IngestSettings Settings(int segmentEntries) => new(
        LogcatFormat.ThreadTime,
        "utf-8",
        new TimestampPolicy(2026, "UTC", DateTimeOffset.UnixEpoch),
        new TemplateSettings(),
        SegmentEntries: segmentEntries);

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
            Root = Path.Combine(Path.GetTempPath(), "VisualCat.Core.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
