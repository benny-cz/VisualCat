using VisualCat.Core.Mining;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Tests;

public sealed class DrainTests
{
    [Fact]
    public void MasksParametersAndKeepsTagsIsolated()
    {
        var miner = new DrainTemplateMiner(new TemplateSettings());
        var first = miner.Assign(Entry(1, "Network", "Connection 42 to 10.0.0.8 failed after 315 ms"));
        var second = miner.Assign(Entry(2, "Network", "Connection 99 to 10.0.0.9 failed after 120 ms"));
        var otherTag = miner.Assign(Entry(3, "Database", "Connection 99 to 10.0.0.9 failed after 120 ms"));
        Assert.Equal(first.TemplateId, second.TemplateId);
        Assert.NotEqual(first.TemplateId, otherTag.TemplateId);
        Assert.Contains("<*>", second.CanonicalText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedReplayIsDeterministic()
    {
        var messages = Enumerable.Range(0, 200).Select(index => $"request {index:D6} completed in {index % 50} ms").ToArray();
        var left = Mine(messages);
        var right = Mine(messages);
        Assert.Equal(left, right);
    }

    [Theory]
    [InlineData("Connection 42 to 10.0.0.8:8080 failed after 315 ms")]
    [InlineData("session 3f8a1c2d-4b5e-11ec-8f3a-0242ac130002 opened")]
    [InlineData("peer 00:1A:2B:3C:4D:5E at 0xdeadBEEF")]
    [InlineData("started 2026-07-19T21:08:42.081222+02:00 elapsed 1.5 sec")]
    [InlineData("read /proc/1234/status and /sys/class/net/12345")]
    [InlineData("no digits here at all")]
    [InlineData("small 1 2 3 numbers stay")]
    [InlineData("boundary 999 1000 -1000 -999")]
    [InlineData("")]
    [InlineData("0x0")]
    [InlineData("1.2.3.4.5.6")]
    [InlineData("12:34:56:78:9a:bc:de")]
    [InlineData("took 250ms and 3 s and 4 hours")]
    public void CombinedMaskMatchesTheRuleAtATimeOracle(string message)
    {
        Assert.Equal(DrainTemplateMiner.ApplyMasksSequentially(message), DrainTemplateMiner.ApplyMasks(message));
    }

    [Fact]
    public void CombinedMaskMatchesTheOracleAcrossTheSampleCorpus()
    {
        // Synthetic cases cannot cover the shapes real devices emit. Replaying the
        // checked-in captures through both maskers is what caught the one genuine
        // divergence in this rewrite: the cascade between the two path rules.
        var repository = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var corpora = new[]
        {
            Path.Combine(repository, "test-data", "golden-formats.txt"),
            Path.Combine(repository, "samples", "logcat_small.txt"),
            Path.Combine(repository, "samples", "logcat_supersmall.txt"),
        }.Where(File.Exists).ToArray();

        Assert.NotEmpty(corpora);
        var compared = 0;
        foreach (var line in corpora.SelectMany(static path => File.ReadLines(path)))
        {
            Assert.Equal(DrainTemplateMiner.ApplyMasksSequentially(line), DrainTemplateMiner.ApplyMasks(line));
            compared++;
        }

        Assert.True(compared > 900, $"Corpus comparison covered only {compared} lines.");
    }

    /// <summary>
    /// §9.4's central claim: sharding is an execution detail, so the shard count must not
    /// be observable in the output. Both halves matter — the ids each entry receives and
    /// the template table those ids index — because a scheme that numbered clusters as
    /// shards created them would still pass an assignment-only check while renumbering
    /// the table underneath it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(16)]
    public void ShardCountDoesNotChangeTemplateOutput(int shardCount)
    {
        var entries = SkewedCorpus();
        var (expectedIds, expectedTemplates) = MineSharded(entries, 1);
        var (actualIds, actualTemplates) = MineSharded(entries, shardCount);

        Assert.Equal(expectedIds, actualIds);
        Assert.Equal(expectedTemplates, actualTemplates);
    }

    /// <summary>
    /// Batching is the other axis the caller controls: the coordinator hands the miner
    /// whatever a 4 MB read produced, so identical input split differently must still
    /// cluster and number identically.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(4096)]
    public void BatchSizeDoesNotChangeTemplateOutput(int batchSize)
    {
        var entries = SkewedCorpus();
        var (expectedIds, expectedTemplates) = MineSharded(entries, 4);

        var miner = new ShardedTemplateMiner(new TemplateSettings(), 4);
        var ids = new List<uint>(entries.Length);
        for (var offset = 0; offset < entries.Length; offset += batchSize)
        {
            var slice = entries.AsSpan(offset, Math.Min(batchSize, entries.Length - offset));
            var buffer = new uint[slice.Length];
            miner.AssignBatch(slice, buffer);
            ids.AddRange(buffer);
        }

        Assert.Equal(expectedIds, ids.ToArray());
        Assert.Equal(expectedTemplates, miner.GetDefinitions().Select(static t => t.CanonicalText).ToArray());
    }

    [Fact]
    public void UnminedOutcomesKeepTheReservedZeroTemplate()
    {
        var miner = new ShardedTemplateMiner(new TemplateSettings(), 4);
        MinedEntry[] entries =
        [
            new("Tag", "value 1234 seen", null, 0),
            default, // a meta or unknown line carries no tag and is not mined
            new("Tag", "value 5678 seen", null, 2),
        ];
        var ids = new uint[entries.Length];
        miner.AssignBatch(entries, ids);

        Assert.Equal(0u, ids[1]);
        Assert.NotEqual(0u, ids[0]);
        Assert.Equal(ids[0], ids[2]);
    }

    [Fact]
    public void SessionWideClusterBudgetIsDeterministicAndCountsUnassignedEntries()
    {
        var miner = new ShardedTemplateMiner(new TemplateSettings(Depth: 0, MaximumClusters: 2), 4);
        MinedEntry[] first =
        [
            new("A", "alpha", null, 0),
            new("B", "bravo", null, 1),
            new("C", "charlie", null, 2),
            new("D", "delta", null, 3),
        ];
        var ids = new uint[first.Length];
        miner.AssignBatch(first, ids);

        Assert.Equal([1u, 2u, 0u, 0u], ids);
        Assert.Equal(2, miner.TemplateCount);
        Assert.Equal(2, miner.OverflowAssignments);

        MinedEntry[] second =
        [
            new("A", "alpha", null, 4),
            new("E", "echo", null, 5),
        ];
        var laterIds = new uint[second.Length];
        miner.AssignBatch(second, laterIds);
        Assert.Equal([1u, 0u], laterIds);
        Assert.Equal(3, miner.OverflowAssignments);

        var changed = miner.GetChangedDefinitions();
        Assert.Equal(2, changed.Count);
        miner.MarkDefinitionsPublished();
        Assert.Empty(miner.GetChangedDefinitions());

        miner.AssignOne(new MinedEntry("A", "alpha", null, 6));
        Assert.Empty(miner.GetChangedDefinitions());

        miner.AssignOne(new MinedEntry("A", "bravo", null, 7));
        Assert.Equal(1u, Assert.Single(miner.GetChangedDefinitions()).TemplateId);
    }

    /// <summary>
    /// Deliberately skewed: one very hot tag plus a long tail, which is the shape that
    /// puts many tags on one shard and exercises cross-shard interleaving.
    /// </summary>
    private static MinedEntry[] SkewedCorpus()
    {
        var random = new Random(20260720);
        var entries = new List<MinedEntry>(4000);
        for (var index = 0; index < 4000; index++)
        {
            var tag = random.Next(100) < 60
                ? "ActivityManager"
                : $"Tag{random.Next(40):D2}";
            var message = random.Next(4) switch
            {
                0 => $"Connection {random.Next(100000)} to 10.0.{random.Next(255)}.{random.Next(255)} failed after {random.Next(9999)} ms",
                1 => $"window {random.Next(50)} state changed to {(random.Next(2) == 0 ? "RESUMED" : "PAUSED")}",
                2 => $"alloc 0x{random.Next():x} size {random.Next(1000000)}",
                _ => "steady heartbeat with no parameters",
            };
            entries.Add(new MinedEntry(tag, message, new InstantUs(index), index));
        }

        return entries.ToArray();
    }

    private static (uint[] Ids, string[] Templates) MineSharded(MinedEntry[] entries, int shardCount)
    {
        var miner = new ShardedTemplateMiner(new TemplateSettings(), shardCount);
        var ids = new uint[entries.Length];
        miner.AssignBatch(entries, ids);
        return (ids, miner.GetDefinitions().Select(static template => template.CanonicalText).ToArray());
    }

    private static uint[] Mine(IReadOnlyList<string> messages)
    {
        var miner = new DrainTemplateMiner(new TemplateSettings());
        return messages.Select((message, index) => miner.Assign(Entry(index, "Tag", message)).TemplateId).ToArray();
    }

    private static NormalizedEntry Entry(long sequence, string tag, string message) =>
        new(
            Guid.Empty,
            sequence,
            sequence,
            new RawSpan(sequence, 1),
            new InstantUs(sequence),
            sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TimestampProvenance.Epoch,
            1,
            1,
            1,
            LogLevel.Info,
            tag,
            "main",
            message,
            LogcatFormat.ThreadTime,
            "2",
            0,
            EntryAttributes.None);
}
