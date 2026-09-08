using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using VisualCat.App.Views;
using VisualCat.Application.UseCases;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Tests;

/// <summary>
/// The review describes the sample it read, and cannot promise what the format cannot keep.
/// </summary>
/// <remarks>
/// A provider file is copied into private storage that the app deletes again, so a session
/// made from one keeps its evidence only if the raw bytes are embedded. That is a lifecycle
/// requirement, not a preference, and a form that lets it be unticked is a form that can
/// produce a session whose source is gone.
/// </remarks>
public sealed class ImportReviewTests
{
    private const string ThreadTimeLog =
        "01-01 00:00:00.000000   100   101 I Worker         : first record\n" +
        "01-01 00:00:01.000000   100   101 W Worker         : second record\n";

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RawEmbeddingIsForcedAndExplainedForATemporaryCopyOnly(bool temporary)
    {
        using var source = await SampleAsync(ThreadTimeLog);
        using var dialog = new ImportPreviewDialog(
            "shared-log.txt",
            source.Sample,
            source.Preview,
            portableRawRequired: temporary);

        var raw = dialog.GetLogicalDescendants()
            .OfType<CheckBox>()
            .Single(static box => AutomationProperties.GetName(box) == "Raw source embedding");

        Assert.Equal(temporary, raw.IsChecked);
        Assert.Equal(!temporary, raw.IsEnabled);
        Assert.Equal(
            temporary ? "Required so this session keeps its source after the temporary copy is removed." : null,
            AutomationProperties.GetHelpText(raw));
    }

    /// <summary>Every editable field says what it is, which is what an error can attach to.</summary>
    [AvaloniaFact]
    public async Task EveryImportOptionCarriesItsOwnAccessibleName()
    {
        using var source = await SampleAsync(ThreadTimeLog);
        using var dialog = new ImportPreviewDialog(
            "crash.txt",
            source.Sample,
            source.Preview,
            portableRawRequired: false);

        var named = dialog.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetName)
            .Where(static name => !string.IsNullOrEmpty(name))
            .ToArray();

        Assert.Contains("Import format", named);
        Assert.Contains("Assumed year", named);
        Assert.Contains("Time zone identifier", named);
        Assert.Contains("Known time zones", named);
        Assert.Contains("Template mining", named);
        Assert.Contains("Raw source embedding", named);
    }

    /// <summary>
    /// The dialog reports the sample's format, and says when it is parsing a forced one.
    /// </summary>
    [AvaloniaFact]
    public async Task TheSummaryDescribesTheSampleRatherThanClaimingTheWholeFile()
    {
        using var source = await SampleAsync(ThreadTimeLog);
        using var dialog = new ImportPreviewDialog(
            "crash.txt",
            source.Sample,
            source.Preview,
            portableRawRequired: false);

        var text = string.Join(
            " ",
            dialog.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(static block => block.Text ?? string.Empty));

        Assert.Contains("Preview of up to the first 200 lines", text, StringComparison.Ordinal);
        Assert.Contains("Detected sample format", text, StringComparison.Ordinal);
        Assert.Contains("Sample time span", text, StringComparison.Ordinal);

        // A sample is not a claim about the file, so nothing here says the file is anything.
        Assert.DoesNotContain("the file is", text, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<SampleFixture> SampleAsync(string log)
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualcat-review-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, log, TestContext.Current.CancellationToken);
        try
        {
            await using var source = new FileLogSource(path);
            var policy = TimestampPolicy.ForFile(source.Metadata.ReferenceInstant);
            var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
            return new SampleFixture(sample, ImportSampleService.Evaluate(sample, policy), path);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private sealed record SampleFixture(ImportPreviewSample Sample, ImportPreview Preview, string Path) : IDisposable
    {
        public void Dispose()
        {
            Sample.Dispose();
            File.Delete(Path);
        }
    }
}
