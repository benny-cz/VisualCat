using System.Text;
using VisualCat.Core.Generation;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;

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
