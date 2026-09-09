using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
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
/// Every control a reader can reach in the dialogs this plan added says what it is.
/// </summary>
/// <remarks>
/// The live pass could not run TalkBack or Narrator, and reading names by hand is what found
/// a row announcing its record's generated <c>ToString()</c>. This asks the same question of
/// every focusable control at once, through the peer a screen reader actually reads, so a
/// new field cannot arrive unnamed and a control cannot fall back to a type dump.
/// </remarks>
public sealed class NewDialogAccessibilityTests
{
    private const string ThreadTimeLog =
        "01-01 00:00:00.000000   100   101 I Worker         : first record\n" +
        "01-01 00:00:01.000000   100   101 W Worker         : second record\n";

    [AvaloniaFact]
    public void EveryExportReviewControlSaysWhatItIs()
    {
        using var dialog = new ExportReviewDialog(
            Request(),
            _ => Task.FromResult<IReadOnlyList<ResolvedExportScope>>([Scope("Visible plot range", 12)]),
            "UTC",
            hasOffTimelineLines: true);

        AssertEveryControlIsNamed(dialog, static body => body.NotifyPresented());
    }

    [AvaloniaFact]
    public async Task EveryImportReviewControlSaysWhatItIs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualcat-a11y-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, ThreadTimeLog, TestContext.Current.CancellationToken);
        try
        {
            await using var source = new FileLogSource(path);
            var policy = TimestampPolicy.ForFile(source.Metadata.ReferenceInstant);
            using var sample = await ImportSampleService.AcquireAsync(source, TestContext.Current.CancellationToken);
            using var dialog = new ImportPreviewDialog(
                "crash.txt",
                sample,
                ImportSampleService.Evaluate(sample, policy),
                portableRawRequired: false);

            AssertEveryControlIsNamed(dialog);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task EveryFacetBrowserControlSaysWhatItIs()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreadTimeLog);
        var browser = new FacetBrowserDialog(fixture.Tab, FacetDimension.Tag);
        try
        {
            AssertEveryControlIsNamed(browser, static body => body.NotifyPresented());
        }
        finally
        {
            await browser.DrainAsync();
            browser.Dispose();
        }
    }

    [AvaloniaFact]
    public void EveryGoToMatchControlSaysWhatItIs()
    {
        using var dialog = new NumberPromptDialog(
            "Go to match",
            "Which match?",
            1,
            new SearchMatchPromptModel(
                new QueryIdentity(Guid.NewGuid(), 1, "filter", 1),
                3));

        AssertEveryControlIsNamed(dialog, static body => body.NotifyPresented());
    }

    /// <summary>Hosts the body, lays it out and asks every focusable control for its name.</summary>
    private static void AssertEveryControlIsNamed<TResult>(
        DialogBody<TResult> dialog,
        Action<DialogBody<TResult>>? present = null)
    {
        var host = new Window { Content = dialog, Width = 640, Height = 720 };
        host.Show();
        try
        {
            host.UpdateLayout();
            present?.Invoke(dialog);
            host.UpdateLayout();

            var unnamed = new StringBuilder();
            foreach (var control in dialog.GetVisualDescendants().OfType<Control>())
            {
                if (!IsReaderReachable(control))
                {
                    continue;
                }

                var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();
                if (string.IsNullOrWhiteSpace(name) || LooksLikeATypeDump(name))
                {
                    unnamed.Append(control.GetType().Name)
                        .Append(" → ")
                        .Append(string.IsNullOrWhiteSpace(name) ? "<no name>" : name)
                        .AppendLine();
                }
            }

            Assert.True(unnamed.Length == 0, $"controls a screen reader cannot name:{Environment.NewLine}{unnamed}");
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// The controls a reader can land on: everything focusable, plus list rows, which are
    /// reached by arrow key rather than by tab and are read the same way.
    /// </summary>
    private static bool IsReaderReachable(Control control) =>
        control is ListBoxItem ||
        (control.Focusable && control.IsEffectivelyEnabled && control.IsEffectivelyVisible &&
         control is not TextBlock);

    /// <summary>A generated record/type name is a name nobody wrote for a reader.</summary>
    private static bool LooksLikeATypeDump(string name) =>
        name.Contains(" { ", StringComparison.Ordinal) ||
        name.StartsWith("VisualCat.", StringComparison.Ordinal) ||
        name.StartsWith("Avalonia.", StringComparison.Ordinal);

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

    private static ResolvedExportScope Scope(string label, long rows) => new(
        ExportScopeKind.VisiblePlot,
        label,
        new TimeRange(new InstantUs(0), new InstantUs(10_000_000)),
        FilterSpec.All,
        $"{label} summary",
        Preferred: true,
        IsProvablyEmpty: false,
        rows);
}
