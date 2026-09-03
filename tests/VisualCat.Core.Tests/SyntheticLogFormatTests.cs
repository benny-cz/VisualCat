using System.Text;
using VisualCat.Core.Generation;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;
using VisualCat.Domain.Sessions;
using VisualCat.Core.Mining;

namespace VisualCat.Core.Tests;

/// <summary>
/// PLAN-01 — the generator produces every documented format, and each one round-trips through
/// the detector that has to read it.
/// </summary>
/// <remarks>
/// The live-test plan's §3.2 builds four format corpora with
/// <c>vcat generate-test-log --format &lt;fmt&gt;</c> and forbids making them by unrecorded
/// manual editing. The generator has always supported the option and the CLI rejected it as
/// unknown, so the plan commanded something the shipped tool refused and the corpora had to be
/// synthesised by hand — which is exactly what §3.2 says not to do. These assertions are what
/// stops the two drifting apart again.
/// </remarks>
public sealed class SyntheticLogFormatTests
{
    [Theory]
    [InlineData(LogcatFormat.ThreadTime)]
    [InlineData(LogcatFormat.Time)]
    [InlineData(LogcatFormat.Brief)]
    [InlineData(LogcatFormat.LongFormat)]
    [InlineData(LogcatFormat.Epoch)]
    public async Task EveryDocumentedFormatGeneratesAndDetectsAsItself(LogcatFormat format)
    {
        using var stream = new MemoryStream();
        await SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(2_000, Seed: 42, Format: format),
            TestContext.Current.CancellationToken);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 100, $"{format} produced {lines.Length} lines");

        var samples = lines
            .Take(200)
            .Select(static line => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(line.Trim()))
            .ToArray();
        var detection = FormatDetector.Detect(samples);
        Assert.Equal(format, detection.PrimaryFormat);
    }

    /// <summary>
    /// The default corpus has seven tags and seven message shapes, so it mines seventy-seven
    /// templates however long it runs, and no benchmark built on it can see a cost that scales
    /// with template diversity — which is how the template table came to be rewritten in full
    /// on every published snapshot without any gate noticing. A corpus asked for a given
    /// tag and template count has to actually produce it, or the gate built on it means nothing.
    /// </summary>
    [Theory]
    [InlineData(200, 400)]
    [InlineData(500, 2_000)]
    public async Task ARequestedTagAndTemplateCountIsWhatTheCorpusMines(int tags, int templates)
    {
        using var stream = new MemoryStream();
        await SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(
                40_000,
                Seed: 42,
                UnknownLineRate: 0,
                OutOfOrderRate: 0,
                DistinctTags: tags,
                DistinctTemplates: templates),
            TestContext.Current.CancellationToken);

        var miner = new ShardedTemplateMiner(new TemplateSettings(), 4);
        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        var entries = new List<MinedEntry>();
        var session = Guid.NewGuid();
        long entryId = 0;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            var outcome = LogcatParser.Parse(
                new SourceLine(session, entryId, new RawSpan(0, bytes.Length), bytes),
                LogcatFormat.ThreadTime);
            if (outcome.Kind is not (ParseOutcomeKind.ParsedEntry or ParseOutcomeKind.UntimedEntry) || outcome.Fields is not { } fields)
            {
                continue;
            }

            seenTags.Add(fields.Tag);
            entries.Add(new MinedEntry(fields.Tag, fields.Message, null, ++entryId));
        }

        var ids = new uint[entries.Count];
        miner.AssignBatch(entries.ToArray(), ids);
        Assert.Equal(templates, miner.TemplateCount);
        Assert.Equal(tags, seenTags.Count);
    }

    /// <summary>Both diversity counts are set together, and a tag holds at most a hundred shapes.</summary>
    [Fact]
    public async Task DiversityCountsAreValidatedTogether()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(10, DistinctTags: 4),
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(10, DistinctTags: 2, DistinctTemplates: 500),
            TestContext.Current.CancellationToken));
    }

    /// <summary>The same seed and format produce the same bytes, which is what a corpus is for.</summary>
    [Fact]
    public async Task AFormatCorpusIsDeterministic()
    {
        static async Task<byte[]> GenerateAsync(LogcatFormat format)
        {
            using var stream = new MemoryStream();
            await SyntheticLogGenerator.GenerateAsync(
                stream,
                new SyntheticLogOptions(500, Seed: 42, Format: format),
                TestContext.Current.CancellationToken);
            return stream.ToArray();
        }

        Assert.Equal(await GenerateAsync(LogcatFormat.Brief), await GenerateAsync(LogcatFormat.Brief));
        Assert.NotEqual(await GenerateAsync(LogcatFormat.Brief), await GenerateAsync(LogcatFormat.Epoch));
    }
}
