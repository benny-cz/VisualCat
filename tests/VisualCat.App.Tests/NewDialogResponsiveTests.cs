using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.UseCases;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Tests;

/// <summary>
/// The dialogs this plan added still decide at a phone's width and at doubled text.
/// </summary>
/// <remarks>
/// The device pass ran at 1.3× OS text on one phone. The sizes that break a decision row are
/// the narrow one and the short one at 2.0×, where a form that scrolls its buttons away is a
/// dialog the reader cannot answer. Each of these composes for a thumb, at 320 dp wide and in
/// a short landscape viewport, and is asked the two questions that matter: can the decision
/// still be reached, and is anything the reader has to press below the touch floor.
/// </remarks>
public sealed class NewDialogResponsiveTests
{
    private const string ThreadTimeLog =
        "01-01 00:00:00.000000   100   101 I Worker         : first record\n" +
        "01-01 00:00:01.000000   100   101 W Worker         : second record\n";

    private static readonly string[] CancelAndChoose = ["Cancel", "Choose a file…"];
    private static readonly string[] CancelAndGo = ["Cancel", "Go"];
    private static readonly string[] CancelAndImport = ["Cancel", "Import"];
    private static readonly string[] DoneOnly = ["Done"];

    /// <summary>
    /// The plan's three phone widths in portrait, and a short landscape viewport — every one
    /// of them at doubled text, which is where a decision row runs out of room first.
    /// </summary>
    public static TheoryData<double, double> PhoneViewports() => new()
    {
        { 320, 640 },
        { 360, 600 },
        { 412, 732 },
        { 640, 340 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(PhoneViewports))]
    public void TheExportReviewKeepsItsDecisionAtPhoneSizes(double width, double height) =>
        AtPhoneSize(width, height, () =>
        {
            var dialog = new ExportReviewDialog(
                Request(),
                _ => Task.FromResult<IReadOnlyList<ResolvedExportScope>>([Scope(12), Scope(40)]),
                "UTC",
                hasOffTimelineLines: true);

            // Proves the phone override reached the dialog rather than the test asserting a
            // desktop layout under a phone-sized window.
            Assert.Equal(new Size(300, 340), dialog.MinimumSize);
            return (dialog, dialog.NotifyPresented, CancelAndChoose);
        });

    [AvaloniaTheory]
    [MemberData(nameof(PhoneViewports))]
    public void TheGoToMatchPromptKeepsItsDecisionAtPhoneSizes(double width, double height) =>
        AtPhoneSize(width, height, () =>
        {
            var dialog = new NumberPromptDialog(
                "Go to match",
                "Which of the 7,181 matches?",
                1,
                new SearchMatchPromptModel(new QueryIdentity(Guid.NewGuid(), 1, "filter", 1), 7181));
            return (dialog, dialog.NotifyPresented, CancelAndGo);
        });

    [AvaloniaTheory]
    [MemberData(nameof(PhoneViewports))]
    public async Task TheImportReviewKeepsItsDecisionAtPhoneSizes(double width, double height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualcat-responsive-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, ThreadTimeLog, TestContext.Current.CancellationToken);
        ImportPreviewSample? sample = null;
        try
        {
            await using var source = new FileLogSource(path);
            var policy = TimestampPolicy.ForFile(source.Metadata.ReferenceInstant);
            sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
            var preview = ImportSampleService.Evaluate(sample, policy);
            AtPhoneSize(width, height, () =>
            {
                var dialog = new ImportPreviewDialog("crash.txt", sample, preview, portableRawRequired: false);
                return (dialog, (Action?)null, CancelAndImport);
            });
        }
        finally
        {
            sample?.Dispose();
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(PhoneViewports))]
    public async Task TheFacetBrowserKeepsItsDecisionAtPhoneSizes(double width, double height)
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreadTimeLog);
        FacetBrowserDialog? browser = null;
        try
        {
            AtPhoneSize(width, height, () =>
            {
                var dialog = new FacetBrowserDialog(fixture.Tab, FacetDimension.Tag);
                browser = dialog;
                return (dialog, dialog.NotifyPresented, DoneOnly);
            });
        }
        finally
        {
            if (browser is not null)
            {
                await browser.DrainAsync();
                browser.Dispose();
            }
        }
    }

    /// <summary>
    /// Hosts one dialog as a phone would, at doubled text, and asks whether it can be answered.
    /// </summary>
    private static void AtPhoneSize(
        double width,
        double height,
        Func<(UserControl Dialog, Action? Present, string[] Decision)> build)
    {
        var platform = TextScale.Platform;
        var user = TextScale.User;
        DialogComposition.PhoneOverride = true;
        TextScale.Platform = 2;
        TextScale.User = 1;
        try
        {
            var (dialog, present, decision) = build();
            var host = new Window { Content = dialog, Width = width, Height = height };
            host.Show();
            try
            {
                host.UpdateLayout();
                present?.Invoke();
                host.UpdateLayout();

                foreach (var content in decision)
                {
                    var button = dialog.GetLogicalDescendants()
                        .OfType<Button>()
                        .Single(candidate => Equals(candidate.Content, content));

                    // Inside the window, and not clipped away above or below it: a decision
                    // that has scrolled off the sheet is a dialog with no answer.
                    var corner = button.TranslatePoint(default, host);
                    Assert.NotNull(corner);
                    var bounds = new Rect(corner.Value, button.Bounds.Size);
                    Assert.True(
                        bounds.Height > 0 && bounds.Width > 0,
                        $"{content} was not laid out at {width}×{height}: {bounds}");
                    Assert.True(
                        bounds.Top >= -0.5 && bounds.Bottom <= height + 0.5,
                        $"{content} is outside the {width}×{height} viewport: {bounds}");
                    Assert.True(
                        bounds.Left >= -0.5 && bounds.Right <= width + 0.5,
                        $"{content} is outside the {width}×{height} viewport: {bounds}");

                    // A thumb needs the whole target, not the glyph the theme drew.
                    Assert.True(
                        bounds.Height >= 47.5,
                        $"{content} is {bounds.Height:F1} dp tall, below the 48 dp touch floor");
                }
            }
            finally
            {
                host.Close();
                (dialog as IDisposable)?.Dispose();
            }
        }
        finally
        {
            DialogComposition.PhoneOverride = null;
            TextScale.Platform = platform;
            TextScale.User = user;
        }
    }

    private static FrozenExportRequest Request() => new(
        Guid.NewGuid(),
        Path.Combine(Path.GetTempPath(), "session"),
        "crash.txt",
        FilterSpec.All,
        new TimeRange(new InstantUs(0), new InstantUs(10_000_000)),
        null,
        null,
        null,
        EntryOrder.SourceSequence,
        DefaultIncludeUtf8Bom: false,
        CaptureContinues: true);

    private static ResolvedExportScope Scope(long rows) => new(
        rows == 12 ? ExportScopeKind.VisiblePlot : ExportScopeKind.AllTimed,
        rows == 12 ? "Visible plot range" : "All timed entries in session",
        new TimeRange(new InstantUs(0), new InstantUs(10_000_000)),
        FilterSpec.All,
        "Every timed entry the plot is showing, under the current filter.",
        Preferred: rows == 12,
        IsProvablyEmpty: false,
        rows);
}
