using System.Text;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Infrastructure.Files;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

public sealed class ImportPreviewSampleTests
{
    [Fact]
    public async Task FinalUnterminatedLineIsCompleteAndEvaluated()
    {
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes("05-15 14:13:37.496  1073  1151 D Tag: final"));
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);

        var preview = ImportSampleService.Evaluate(sample, Policy(2024));

        Assert.Single(sample.CompleteLines);
        Assert.True(sample.ReachedEndOfSource);
        Assert.Equal(1, preview.OutcomeCounts[ParseOutcomeKind.ParsedEntry]);
    }

    [Fact]
    public async Task FileProbeEnforcesAggregateBudgetBeforeRetainingAnotherLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualcat-preview-{Guid.NewGuid():N}.log");
        try
        {
            var line = Enumerable.Repeat((byte)'x', 900_000).Append((byte)'\n').ToArray();
            await using (var output = File.Create(path))
            {
                for (var index = 0; index < 5; index++)
                {
                    await output.WriteAsync(line, TestContext.Current.CancellationToken);
                }
            }

            await using var source = new FileLogSource(path, chunkBytes: 32 * 1024);
            using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);

            Assert.Equal(SourceProbeLimit.AggregateBytes, sample.Limit);
            Assert.Equal(4, sample.CompleteLines.Count);
            Assert.True(sample.HasClippedLine);
            Assert.Equal(ImportSampleService.MaximumRetainedBytes, sample.RetainedBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LongFirstLineIsNotMisclassifiedAsAnEmptySource()
    {
        var bytes = Enumerable.Repeat((byte)'x', ImportSampleService.MaximumLineBytes + 100).ToArray();
        await using var source = new MemoryLogSource(bytes);
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
        var preview = ImportSampleService.Evaluate(sample, Policy(2024));

        Assert.True(sample.SourceBytesObserved);
        Assert.Empty(sample.CompleteLines);
        Assert.Equal(SourceProbeLimit.ClippedLine, sample.Limit);
        Assert.Equal(ImportPreparationDecision.Review, ImportPreparationPolicy.Decide(sample, preview, false));
    }

    [Fact]
    public async Task OneSampleReevaluatesYearAndFormatWithoutInventingConfidence()
    {
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes("05-15 14:13:37.496  1073  1151 D Tag: message\n"));
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);

        var first = ImportSampleService.Evaluate(sample, Policy(2024));
        var second = ImportSampleService.Evaluate(sample, Policy(2025), LogcatFormat.Brief);

        Assert.NotEqual(first.FirstInstant, second.FirstInstant);
        Assert.Equal(LogcatFormat.Brief, second.FormatOverride);
        Assert.Equal(first.Detection.Confidence, second.Detection.Confidence);
        Assert.Equal(sample.RetainedBytes, second.RetainedBytes);
    }

    [Fact]
    public async Task PolicyKeepsConfidentQuickImportAndAlwaysReviewDistinct()
    {
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
                "05-15 14:13:37.496  1073  1151 D Tag: message\n",
                10))));
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
        var preview = ImportSampleService.Evaluate(sample, Policy(2024));

        Assert.Equal(ImportPreparationDecision.QuickImport, ImportPreparationPolicy.Decide(sample, preview, false));
        Assert.Equal(ImportPreparationDecision.Review, ImportPreparationPolicy.Decide(sample, preview, true));
    }

    /// <summary>
    /// A crash log carries dozens of lines no parser claims, and still imports without asking.
    /// </summary>
    /// <remarks>
    /// The existing warning for more than 10% unknown-or-rejected input stays visible wherever
    /// a review opens, but it is not by itself a reason to interrupt a quick import: every
    /// Android crash is a run of stack-trace frames inside ordinary traffic, and stopping to
    /// ask about each of those would make the phone's default path the exception rather than
    /// the rule. Detection reads the records around the block, and stays confident.
    /// </remarks>
    [Fact]
    public async Task ACrashShapedSampleStillQuickImportsDespiteItsUnknownLines()
    {
        // Ordinary traffic with a crash block spliced into it, which is what a crash log is:
        // the frames are well over the 10% unknown-or-rejected warning threshold, and the
        // records around them are what detection actually reads.
        var builder = new StringBuilder();
        for (var record = 0; record < 30; record++)
        {
            builder.Append("05-15 14:13:37.")
                .Append(record.ToString("000", System.Globalization.CultureInfo.InvariantCulture))
                .Append("  1073  1151 D Tag: ordinary record\n");
        }

        builder.Append("05-15 14:13:38.000  1073  1151 E AndroidRuntime: FATAL EXCEPTION: main\n");
        for (var frame = 0; frame < 40; frame++)
        {
            builder.Append("\tat com.example.Thing.method(Thing.java:").Append(frame).Append(")\n");
        }

        for (var record = 0; record < 30; record++)
        {
            builder.Append("05-15 14:13:39.")
                .Append(record.ToString("000", System.Globalization.CultureInfo.InvariantCulture))
                .Append("  1073  1151 D Tag: ordinary record\n");
        }

        await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(builder.ToString()));
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
        var preview = ImportSampleService.Evaluate(sample, Policy(2024));

        var unclaimed = sample.CompleteLines.Count - preview.OutcomeCounts[ParseOutcomeKind.ParsedEntry];
        Assert.True(
            unclaimed * 10 > sample.CompleteLines.Count,
            $"the fixture must exceed the 10% warning threshold; {unclaimed} of {sample.CompleteLines.Count}");
        Assert.Equal(
            ImportPreparationDecision.QuickImport,
            ImportPreparationPolicy.Decide(sample, preview, alwaysReview: false));
    }

    /// <summary>
    /// Input the detector is unsure about opens the review rather than importing on a guess.
    /// </summary>
    [Fact]
    public async Task ALowConfidenceSampleOpensTheReviewOnItsOwn()
    {
        await using var source = new MemoryLogSource(
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("nothing here resembles a logcat record\n", 10))));
        using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
        var preview = ImportSampleService.Evaluate(sample, Policy(2024));

        Assert.True(sample.SourceBytesObserved);
        Assert.True(preview.Detection.Confidence < 0.6, $"confidence was {preview.Detection.Confidence}");
        Assert.Equal(
            ImportPreparationDecision.Review,
            ImportPreparationPolicy.Decide(sample, preview, alwaysReview: false));
    }

    private static TimestampPolicy Policy(int year) =>
        new(year, "UTC", new DateTimeOffset(year, 5, 16, 0, 0, 0, TimeSpan.Zero));
}
