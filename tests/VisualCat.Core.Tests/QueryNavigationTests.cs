using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Templates;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Tests;

public sealed class QueryNavigationTests
{
    [Fact]
    public async Task OrdinalAndAdjacentSearchResolveExactKeysAcrossOverlappingSegments()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 21_000, i => Entry(i, timestamp: BaseTimestamp, tag: "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };

        var late = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Ordinal, Ordinal: 20_500),
            1);
        Assert.Equal(SearchMatchStatus.Found, late.Status);
        Assert.Equal(20_500, late.Ordinal);
        Assert.Equal(21_000, late.TotalMatches);
        Assert.Equal(20_499, late.Key?.SourceSequence);

        var next = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Next, late.Key),
            2);
        Assert.Equal(20_500, next.Key?.SourceSequence);
        Assert.Equal(20_501, next.Ordinal);

        var last = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Last),
            3);
        Assert.Equal(20_999, last.Key?.SourceSequence);
        Assert.Equal(21_000, last.Ordinal);

        var wrapped = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Next, last.Key),
            4);
        Assert.Equal(0, wrapped.Key?.SourceSequence);
        Assert.Equal(1, wrapped.Ordinal);
    }

    [Fact]
    public async Task CancelledColdBitmapIsNotPublishedToTheSegmentCache()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 2_048, i => Entry(i, BaseTimestamp + i, $"Tag{i}"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };
        using var cancellation = new CancellationTokenSource();
        var starts = 0;
        foreach (var segment in snapshot.Segments)
        {
            segment.BitmapFactoryStartedForTests = _ =>
            {
                starts++;
                cancellation.Cancel();
            };
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SessionQueryEngine.SearchMatchAsync(
                snapshot,
                filter,
                new SearchMatchRequest(SearchMatchRequestKind.First),
                1,
                cancellation.Token));

        foreach (var segment in snapshot.Segments)
        {
            segment.BitmapFactoryStartedForTests = _ => starts++;
        }

        var result = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.First),
            2);
        Assert.Equal(SearchMatchStatus.Found, result.Status);
        Assert.True(starts >= 2, "the cancelled partial bitmap was incorrectly reused");
    }

    [Fact]
    public async Task FacetPagesAreCompleteStableAndPutAnExactSearchFirst()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 1_000, i => Entry(i, BaseTimestamp + i, $"Tag{i:0000}"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);

        var exact = SessionQueryEngine.QueryFacetValues(
            snapshot,
            FilterSpec.All,
            FacetQueryDimension.Tag,
            "tag0999",
            null,
            100,
            1);
        Assert.Equal(1, exact.NeutralMatchCount);
        Assert.Equal("Tag0999", Assert.Single(exact.NeutralValues).Key.Text);

        var first = SessionQueryEngine.QueryFacetValues(
            snapshot,
            FilterSpec.All,
            FacetQueryDimension.Tag,
            string.Empty,
            null,
            100,
            2);
        var second = SessionQueryEngine.QueryFacetValues(
            snapshot,
            FilterSpec.All,
            FacetQueryDimension.Tag,
            string.Empty,
            Assert.IsType<FacetPageCursor>(first.NextCursor),
            100,
            3);
        Assert.Equal(1_000, first.NeutralMatchCount);
        Assert.Equal(100, first.NeutralValues.Count);
        Assert.Equal(100, second.NeutralValues.Count);
        Assert.Empty(first.NeutralValues.Select(static value => value.Key).Intersect(second.NeutralValues.Select(static value => value.Key)));
        Assert.Equal("Tag0000", first.NeutralValues[0].Key.Text);
        Assert.Equal("Tag0100", second.NeutralValues[0].Key.Text);
    }

    /// <summary>
    /// Every ordinal agrees with a brute-force enumeration of the same matches.
    /// </summary>
    /// <remarks>
    /// Rank-based ordinal lookup fails silently: a wrong answer still selects a real record,
    /// just not the k-th one. Duplicate timestamps and several segments are exactly where the
    /// summed-rank arithmetic can drift, so the oracle is the enumeration itself rather than a
    /// few hand-written expectations.
    /// </remarks>
    [Fact]
    public async Task EveryOrdinalAgreesWithABruteForceEnumerationOverDuplicateTimestamps()
    {
        const int count = 3_000;
        using var session = new TemporarySession();

        // Ten records share each timestamp, and the segment size divides neither the run
        // length nor the duplicate group, so groups straddle segment boundaries.
        await WriteAsync(session.Root, count, i => Entry(i, BaseTimestamp + (i / 10 * 1_000), "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.True(snapshot.Segments.Count > 1, "the fixture must span more than one segment");
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };

        var expected = Enumerable.Range(0, count)
            .Select(i => new SearchMatchKey(snapshot.SessionId, BaseTimestamp + (i / 10 * 1_000), i))
            .OrderBy(static key => key.TimestampUs)
            .ThenBy(static key => key.SourceSequence)
            .ToArray();

        long generation = 0;
        foreach (var ordinal in new long[] { 1, 2, 10, 11, 999, 1_000, 1_001, 1_500, count - 1, count })
        {
            var result = await SessionQueryEngine.SearchMatchAsync(
                snapshot,
                filter,
                new SearchMatchRequest(SearchMatchRequestKind.Ordinal, Ordinal: ordinal),
                ++generation);
            Assert.Equal(SearchMatchStatus.Found, result.Status);
            Assert.Equal(count, result.TotalMatches);
            Assert.Equal(expected[ordinal - 1], result.Key);
            Assert.Equal(ordinal, result.Ordinal);
        }

        // Out of range is a typed answer, never the nearest real record.
        var beyond = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Ordinal, Ordinal: count + 1),
            ++generation);
        Assert.Equal(SearchMatchStatus.OutOfRange, beyond.Status);
        Assert.Null(beyond.Key);
    }

    /// <summary>
    /// Ten matches sharing one instant are ten separate places in the sequence.
    /// </summary>
    /// <remarks>
    /// Navigation used to identify a match by its timestamp alone, so a burst written in the
    /// same microsecond was one destination: Next left the reader where they already were.
    /// </remarks>
    [Fact]
    public async Task RepeatedTimestampsAdvanceThroughEveryKeyAndWrapBothWays()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 10, i => Entry(i, BaseTimestamp, "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };

        long generation = 0;
        var forward = new List<long>();
        SearchMatchKey? cursor = null;
        for (var step = 0; step < 11; step++)
        {
            var result = await SessionQueryEngine.SearchMatchAsync(
                snapshot,
                filter,
                new SearchMatchRequest(
                    cursor is null ? SearchMatchRequestKind.First : SearchMatchRequestKind.Next,
                    cursor),
                ++generation);
            Assert.Equal(SearchMatchStatus.Found, result.Status);
            cursor = result.Key;
            forward.Add(result.Key!.Value.SourceSequence);
        }

        // Ten distinct keys in order, then a wrap back to the first.
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0], forward);

        var backward = new List<long>();
        cursor = null;
        for (var step = 0; step < 11; step++)
        {
            var result = await SessionQueryEngine.SearchMatchAsync(
                snapshot,
                filter,
                new SearchMatchRequest(
                    cursor is null ? SearchMatchRequestKind.Last : SearchMatchRequestKind.Previous,
                    cursor),
                ++generation);
            cursor = result.Key;
            backward.Add(result.Key!.Value.SourceSequence);
        }

        Assert.Equal([9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 9], backward);
    }

    /// <summary>
    /// A match after twenty thousand earlier ones is still reachable and still counted.
    /// </summary>
    /// <remarks>
    /// The marker list is capped at 20,000 timestamps, and it used to be the search index as
    /// well: the total came from it, and nothing past the cap could be selected at all.
    /// </remarks>
    [Fact]
    public async Task ALateMatchBeyondTheMarkerCapIsReachableAndCounted()
    {
        const int count = 20_002;
        using var session = new TemporarySession();
        await WriteAsync(
            session.Root,
            count,
            i => Entry(i, BaseTimestamp + (i == count - 1 ? 59_000_000 : i), "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };

        var capped = await SessionQueryEngine.SearchAsync(snapshot, filter.Search!, FilterSpec.All, 1);
        Assert.True(capped.MarkersTruncated, "the fixture must exceed the marker cap");
        Assert.Equal(20_000, capped.Markers.Count);

        var last = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Last),
            2);
        Assert.Equal(count, last.TotalMatches);
        Assert.Equal(count, last.Ordinal);
        Assert.Equal(count - 1, last.Key?.SourceSequence);

        // And the same record is reachable by asking for the instant it is at.
        var nearest = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Nearest, Near: new InstantUs(BaseTimestamp + 59_000_000)),
            3);
        Assert.Equal(count - 1, nearest.Key?.SourceSequence);
    }

    /// <summary>
    /// More than twenty thousand records at one identical instant are still ordered exactly.
    /// </summary>
    /// <remarks>
    /// This is the hardest shape the rank/select path has: the timestamp binary search settles
    /// immediately and everything is then decided by ranking source sequence inside one
    /// equal-timestamp group that straddles twenty segments. It is also the shape the old
    /// timestamp-only marker list collapsed into a single destination.
    /// </remarks>
    [Fact]
    public async Task TwentyThousandRecordsAtOneInstantAreOrderedBySourceSequence()
    {
        const int count = 20_500;
        using var session = new TemporarySession();
        await WriteAsync(session.Root, count, i => Entry(i, BaseTimestamp, "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        Assert.True(snapshot.Segments.Count > 1, "the group must straddle segment boundaries");
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };

        long generation = 0;
        foreach (var ordinal in new long[] { 1, 2, 1_024, 1_025, 20_000, 20_001, count - 1, count })
        {
            var result = await SessionQueryEngine.SearchMatchAsync(
                snapshot,
                filter,
                new SearchMatchRequest(SearchMatchRequestKind.Ordinal, Ordinal: ordinal),
                ++generation);
            Assert.Equal(SearchMatchStatus.Found, result.Status);
            Assert.Equal(count, result.TotalMatches);
            Assert.Equal(ordinal, result.Ordinal);
            Assert.Equal(BaseTimestamp, result.Key?.TimestampUs);

            // Every one of them shares the instant, so the ordinal is the source sequence.
            Assert.Equal(ordinal - 1, result.Key?.SourceSequence);
        }

        // Stepping across the old cap advances by one record, not by one timestamp.
        var at20000 = new SearchMatchKey(snapshot.SessionId, BaseTimestamp, 19_999);
        var next = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Next, at20000),
            ++generation);
        Assert.Equal(20_000, next.Key?.SourceSequence);
        Assert.Equal(20_001, next.Ordinal);

        var last = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Last),
            ++generation);
        Assert.Equal(count - 1, last.Key?.SourceSequence);
        Assert.Equal(count, last.Ordinal);
    }

    /// <summary>
    /// A record that has stopped matching clears the cursor instead of moving it.
    /// </summary>
    [Fact]
    public async Task RevalidationReportsAKeyThatNoLongerMatches()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 4, i => Entry(i, BaseTimestamp + i, i == 2 ? "Other" : "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with
        {
            Search = new TextSearchSpec("needle"),
            IncludedTags = ["Worker"],
        };
        var absent = new SearchMatchKey(snapshot.SessionId, BaseTimestamp + 2, 2);

        var result = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(SearchMatchRequestKind.Revalidate, absent),
            1);

        Assert.Equal(SearchMatchStatus.NoLongerMatches, result.Status);
        Assert.Null(result.Key);
        Assert.Equal(3, result.TotalMatches);
    }

    [Fact]
    public async Task AKeyFromAnotherSessionCannotSelectACoincidentRecord()
    {
        using var session = new TemporarySession();
        await WriteAsync(session.Root, 1, i => Entry(i, BaseTimestamp, "Worker"));
        using var snapshot = await SessionStore.OpenAsync(session.Root);
        var filter = FilterSpec.All with { Search = new TextSearchSpec("needle") };
        var otherSession = Guid.NewGuid();
        Assert.NotEqual(snapshot.SessionId, otherSession);

        var result = await SessionQueryEngine.SearchMatchAsync(
            snapshot,
            filter,
            new SearchMatchRequest(
                SearchMatchRequestKind.Revalidate,
                new SearchMatchKey(otherSession, BaseTimestamp, 0)),
            1);

        Assert.Equal(SearchMatchStatus.NoLongerMatches, result.Status);
        Assert.Null(result.Key);
        Assert.Equal(1, result.TotalMatches);
    }

    private const long BaseTimestamp = 1_700_000_000_000_000;

    private static async Task WriteAsync(
        string root,
        int count,
        Func<int, NormalizedEntry> create)
    {
        await using var writer = new SessionStoreWriter(root, Settings(), Identity());
        for (var i = 0; i < count; i++)
        {
            writer.AddEntry(create(i));
        }

        writer.FlushSegment();
        await writer.FinalizeAsync(Descriptor(), [], [], CancellationToken.None);
    }

    private static NormalizedEntry Entry(long sequence, long timestamp, string tag) => new(
        Guid.Empty,
        sequence,
        sequence,
        new RawSpan(sequence * 16, 16),
        new InstantUs(timestamp),
        "01-01 00:00:00.000000",
        TimestampProvenance.ExplicitUtc,
        1,
        100,
        101,
        LogLevel.Info,
        tag,
        "main",
        $"needle {sequence}",
        LogcatFormat.ThreadTime,
        "2",
        0,
        EntryAttributes.None);

    private static IngestSettings Settings() => new(
        LogcatFormat.ThreadTime,
        "utf-8",
        new TimestampPolicy(2024, "UTC", DateTimeOffset.UnixEpoch),
        new TemplateSettings(),
        SegmentEntries: 1024);

    private static SourceIdentity Identity() => new("memory", null, 0, null, string.Empty, true);

    private static SessionDescriptor Descriptor() => new(
        Guid.NewGuid(),
        "query navigation",
        SourceKind.File,
        "test",
        DateTimeOffset.UnixEpoch,
        SessionState.Ready,
        0,
        LogcatFormat.ThreadTime,
        1,
        new TimestampPolicy(2024, "UTC", DateTimeOffset.UnixEpoch),
        new TemplateSettings(),
        new SessionCounters(),
        new DefectCounters(),
        null,
        null,
        true,
        false);

    private sealed class TemporarySession : IDisposable
    {
        internal TemporarySession()
        {
            Root = Path.Combine(Path.GetTempPath(), "VisualCat.Core.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

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
