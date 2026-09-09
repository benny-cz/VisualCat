using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;

namespace VisualCat.App.Tests;

/// <summary>
/// An export gives the capture back on every route out of it.
/// </summary>
/// <remarks>
/// The export owns a snapshot and a deletion work lease from the moment it is prepared, and
/// there are five ways out: cancelling the review, cancelling the destination, failing to
/// prepare, finishing, and failing. A leaked lease is invisible — the capture simply stops
/// being deletable, with nothing on screen to say why — so each exit is driven here rather
/// than resting on the shape of the code that shares their disposal.
/// </remarks>
public sealed class ExportOwnershipTests
{
    private const string Log =
        "01-01 00:00:00.000000   100   101 E Worker         : needle one\n" +
        "01-01 00:00:01.000000   100   101 W Worker         : needle two\n" +
        "01-01 00:00:02.000000   100   101 I Worker         : ordinary\n";

    [AvaloniaFact]
    public async Task CancellingTheReviewReleasesTheCapture() =>
        await ExitAsync(
            async (shell, window, _) =>
            {
                var review = await ReviewAsync(shell, window);
                Press(review, "Cancel");
            },
            expectChosen: false,
            expectedNotice: null);

    [AvaloniaFact]
    public async Task CancellingTheDestinationReleasesTheCaptureWithoutAnError() =>
        await ExitAsync(
            async (shell, window, destination) =>
            {
                destination.Answer = TestDestination.Choice.Cancelled;
                await ChooseAFileAsync(shell, window);
            },
            expectedNotice: null);

    /// <summary>A request whose session moved on never asks for a destination at all.</summary>
    [AvaloniaFact]
    public async Task APreparationFailureReleasesTheCaptureBeforeAnyDestination() =>
        await ExitAsync(
            static (_, _, _) => Task.CompletedTask,
            mutate: static request => request with { SessionId = Guid.NewGuid() },
            expectChosen: false,
            expectedNotice: "Could not export session.txt");

    [AvaloniaFact]
    public async Task AFinishedExportReleasesTheCapture() =>
        await ExitAsync(
            async (shell, window, destination) =>
            {
                destination.Answer = TestDestination.Choice.Writes;
                await ChooseAFileAsync(shell, window);
            },
            expectedNotice: "Exported 3 timed rows");

    [AvaloniaFact]
    public async Task AFailedExportReleasesTheCapture() =>
        await ExitAsync(
            async (shell, window, destination) =>
            {
                destination.Answer = TestDestination.Choice.Throws;
                await ChooseAFileAsync(shell, window);
            },
            expectedNotice: "Could not export session.txt");

    /// <summary>
    /// Drives one exit and asserts the capture is deletable again on the other side of it.
    /// </summary>
    private static async Task ExitAsync(
        Func<MainView, Window, TestDestination, Task> exit,
        Func<FrozenExportRequest, FrozenExportRequest>? mutate = null,
        bool expectChosen = true,
        string? expectedNotice = null)
    {
        MainView.InPageDialogOverride = true;
        var settings = Path.Combine(Path.GetTempPath(), $"visualcat-export-{Guid.NewGuid():N}.json");
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(Log);
        var root = fixture.Tab.SessionPath;
        var shell = new MainView(null, settings);
        var window = new Window { Content = shell, Width = 900, Height = 700 };
        window.Show();
        using var destination = new TestDestination();
        try
        {
            Assert.False(SessionAccess.IsWorking(root), "nothing owns the capture before the export");
            var request = Request(fixture);
            Assert.True(shell.FileOperationsForTest.TryBegin(
                FileOperationKind.Export,
                "Preparing export · session.txt",
                out var pending));
            var operation = Assert.IsType<FileOperationHandle>(pending);
            var running = shell.RunExportForTestAsync(
                mutate is null ? request : mutate(request),
                destination.ChooseAsync,
                operation);
            shell.FileOperationsForTest.Track(operation, running);

            await exit(shell, window, destination);
            await running.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectChosen, destination.Chosen);
            Assert.False(SessionAccess.IsWorking(root), "the export must give the capture back on this exit");
            Assert.Null(shell.FileOperationsForTest.Current);
            var lane = LaneText(shell);
            if (expectedNotice is null)
            {
                // Cancelling a destination is not an error and says nothing about the export.
                Assert.DoesNotContain("export", lane, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains(expectedNotice, lane, StringComparison.Ordinal);
            }
        }
        finally
        {
            window.Close();
            await shell.DisposeAsync();
            MainView.InPageDialogOverride = null;
            File.Delete(settings);
        }
    }

    /// <summary>Waits for the review, then for the counts that enable its decision.</summary>
    private static async Task<ExportReviewDialog> ReviewAsync(MainView shell, Window window)
    {
        var review = await PumpUntilAsync(
            window,
            () => shell.GetLogicalDescendants().OfType<ExportReviewDialog>().FirstOrDefault());
        await PumpUntilAsync(window, () => Find(review, "Choose a file…").IsEnabled ? review : null);
        return review;
    }

    private static async Task ChooseAFileAsync(MainView shell, Window window)
    {
        var review = await ReviewAsync(shell, window);
        Press(review, "Choose a file…");
    }

    private static string LaneText(MainView shell) => shell.GetLogicalDescendants()
        .OfType<TextBlock>()
        .FirstOrDefault(static block =>
            Avalonia.Automation.AutomationProperties.GetName(block) == "Application status message")
        ?.Text ?? string.Empty;

    private static void Press(Control host, string content) =>
        Find(host, content).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static Button Find(Control host, string content) => host.GetLogicalDescendants()
        .OfType<Button>()
        .Single(button => Equals(button.Content, content));

    /// <summary>Runs the dispatcher until the shell has produced something, without sleeping on it.</summary>
    private static async Task<T> PumpUntilAsync<T>(Window window, Func<T?> read) where T : class
    {
        for (var pass = 0; pass < 400; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (read() is { } value)
            {
                return value;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
        return read() ?? throw new InvalidOperationException("The shell never produced what this exit needed.");
    }

    private static FrozenExportRequest Request(LiveTestWorkspaceFixture fixture)
    {
        var snapshot = fixture.Tab.Snapshot ?? throw new InvalidOperationException("The fixture has no snapshot.");
        var range = snapshot.TimedRange ?? throw new InvalidOperationException("The fixture has no timed range.");
        return new FrozenExportRequest(
            snapshot.SessionId,
            fixture.Tab.SessionPath,
            "session.txt",
            FilterSpec.All,
            range,
            null,
            null,
            null,
            EntryOrder.Chronological,
            DefaultIncludeUtf8Bom: false,
            CaptureContinues: false);
    }

    /// <summary>The destination the test chooses the behaviour of, in place of a picker.</summary>
    private sealed class TestDestination : FileDestination
    {
        internal enum Choice
        {
            Cancelled,
            Writes,
            Throws,
        }

        private readonly string _written =
            Path.Combine(Path.GetTempPath(), $"visualcat-export-out-{Guid.NewGuid():N}.csv");

        internal Choice Answer { get; set; } = Choice.Cancelled;

        internal bool Chosen { get; private set; }

        internal Task<FileDestination?> ChooseAsync(ExportDecision decision)
        {
            Chosen = true;
            return Task.FromResult<FileDestination?>(Answer == Choice.Cancelled ? null : this);
        }

        internal override string Name => Path.GetFileName(_written);

        internal override bool UsesDirectLocalPath => false;

        internal override async Task WriteAsync(
            Func<string, CancellationToken, Task> producer,
            IProgress<FileWorkProgress>? progress,
            Action<StorageWritePublication>? publication,
            CancellationToken cancellationToken)
        {
            if (Answer == Choice.Throws)
            {
                throw new IOException("The chosen location refused the file.");
            }

            await producer(_written, cancellationToken);
            publication?.Invoke(StorageWritePublication.LocalCommitted);
        }

        public override void Dispose()
        {
            base.Dispose();
            File.Delete(_written);
        }
    }
}
