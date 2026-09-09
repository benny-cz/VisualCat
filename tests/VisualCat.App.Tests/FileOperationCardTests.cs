using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Tests;

/// <summary>
/// Long file work says it is running, says how far it has got, and can be stopped.
/// </summary>
/// <remarks>
/// <para>
/// Copying an incoming file, writing a CSV and building a portable archive all used to run
/// with no acknowledgement at all: the shell cleared its notice and awaited the work, so a
/// large copy was an application that had stopped responding to the reader's question. The
/// card is the answer, and it exists only while its work does.
/// </para>
/// <para>
/// The assertions here are about what a reader can see and press. The owner's own lifetime
/// rules — one operation at a time, cancellation as a request, an irreversible publish — are
/// covered by <see cref="FileOperationOwnerTests"/>.
/// </para>
/// </remarks>
public sealed class FileOperationCardTests
{
    [Theory]
    [InlineData("C:\\staging\\generated.bin", "investigation.vcat.zip", true)]
    [InlineData("C:\\sessions\\investigation.VCAT.ZIP", null, true)]
    [InlineData("C:\\sessions\\ordinary.zip", null, true)]
    [InlineData("C:\\staging\\generated.bin", "device.log", false)]
    public void IncomingArchiveRecognitionUsesProviderNameAndMaterializedPath(
        string path,
        string? displayName,
        bool expected)
    {
        Assert.Equal(expected, MainView.IsPortableArchive(path, displayName));
    }

    [AvaloniaFact]
    public async Task TheCardIsAbsentUntilThereIsWorkAndGoesWhenTheWorkDoes()
    {
        await using var shell = await ShellFixture.CreateAsync();
        Assert.False(shell.Band.IsVisible);

        Assert.True(shell.Operations.TryBegin(FileOperationKind.Export, "Preparing export…", out var operation));
        shell.Settle();
        Assert.True(shell.Band.IsVisible);
        Assert.Equal("Preparing export…", shell.StatusText);

        await operation!.DisposeAsync();
        shell.Settle();
        Assert.False(shell.Band.IsVisible);
    }

    [AvaloniaFact]
    public async Task StageProgressReadsAsAStageAndAKnownTotal()
    {
        await using var shell = await ShellFixture.CreateAsync();
        Assert.True(shell.Operations.TryBegin(FileOperationKind.Export, "Preparing export…", out var operation));

        operation!.Report(new FileWorkProgress(FileWorkStage.WritingRows, 4_096, 12_431, "rows"));
        shell.Settle();

        // Immediately: the stage, not a count. A copy that finishes in 40 ms must not flash
        // numbers on its way past, so the card acknowledges first and counts only if it lasts.
        Assert.Equal("Writing CSV…", shell.StatusText);
        Assert.True(shell.Progress.IsIndeterminate);

        await shell.SettleCountedProgressAsync();
        Assert.Equal("Writing CSV · 4,096 of 12,431 rows", shell.StatusText);
        Assert.False(shell.Progress.IsIndeterminate);
        Assert.Equal(12_431, shell.Progress.Maximum);
        Assert.Equal(4_096, shell.Progress.Value);

        // A stage with no total is indeterminate rather than a manufactured percentage.
        operation.Report(new FileWorkProgress(FileWorkStage.Verifying));
        shell.Settle();
        Assert.Equal("Verifying session…", shell.StatusText);
        Assert.True(shell.Progress.IsIndeterminate);

        await operation.DisposeAsync();
    }

    /// <summary>
    /// Every stage a file service can report has its own sentence on the card.
    /// </summary>
    /// <remarks>
    /// The card is kind-agnostic: it renders whatever stage the running service reports, so
    /// saving, archiving and sharing reach the reader through stages that export never
    /// produces. Those were watched on the phone but not on the desktop, and a stage with no
    /// wording of its own falls back silently to the operation's opening title — which reads
    /// as though nothing has moved since the work began.
    /// </remarks>
    [AvaloniaFact]
    public async Task EveryStageASaveArchiveOrShareReportsReadsAsItsOwnStage()
    {
        await using var shell = await ShellFixture.CreateAsync();
        Assert.True(shell.Operations.TryBegin(FileOperationKind.Share, "Preparing portable session…", out var operation));

        var expected = new Dictionary<FileWorkStage, string>
        {
            // Preparing has nothing more specific to say than the command the reader used.
            [FileWorkStage.Preparing] = "Preparing portable session…",
            [FileWorkStage.Copying] = "Copying file…",
            [FileWorkStage.WritingRows] = "Writing CSV…",
            [FileWorkStage.Verifying] = "Verifying session…",
            [FileWorkStage.CreatingArchive] = "Creating archive…",
            [FileWorkStage.ExtractingArchive] = "Extracting archive…",
            [FileWorkStage.SavingToProvider] = "Saving to chosen location…",
            [FileWorkStage.Publishing] = "Finishing…",
        };

        // Every value of the enum, so a stage added later cannot arrive without wording.
        Assert.Equal(Enum.GetValues<FileWorkStage>().Order().ToArray(), expected.Keys.Order().ToArray());

        foreach (var (stage, sentence) in expected)
        {
            operation!.Report(new FileWorkProgress(stage));
            shell.Settle();
            Assert.Equal(sentence, shell.StatusText);
            Assert.True(shell.Progress.IsIndeterminate, $"{stage} reports no total and must not invent one");
        }

        await operation!.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task CancelRequestsCancellationOnceAndThenSaysItIsCancelling()
    {
        await using var shell = await ShellFixture.CreateAsync();
        Assert.True(shell.Operations.TryBegin(FileOperationKind.Save, "Preparing saved session…", out var operation));
        operation!.Report(new FileWorkProgress(FileWorkStage.Copying, 1, 10, "files"));
        shell.Settle();
        Assert.True(shell.Cancel.IsEnabled);

        shell.Cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        shell.Settle();

        Assert.True(operation.Token.IsCancellationRequested);
        Assert.Equal("Cancelling…", shell.StatusText);

        // A cancellation request is not a terminal result: the card stays until the writer
        // actually stops, and a second press cannot start another cancellation.
        Assert.False(shell.Cancel.IsEnabled);
        Assert.True(shell.Band.IsVisible);

        await operation.DisposeAsync();
        shell.Settle();
        Assert.False(shell.Band.IsVisible);
    }

    [AvaloniaFact]
    public async Task AnIrreversibleFinishDisablesCancelWithoutClaimingSuccess()
    {
        await using var shell = await ShellFixture.CreateAsync();
        Assert.True(shell.Operations.TryBegin(FileOperationKind.Export, "Preparing export…", out var operation));

        // Provider bytes are still moving: publication has started, but this is the part a
        // reader may still stop.
        operation!.SetPublication(FilePublicationStatus.ProviderDeliveryStarted);
        operation.Report(new FileWorkProgress(FileWorkStage.SavingToProvider, 2, 10, "bytes"));
        await shell.SettleCountedProgressAsync();
        Assert.True(shell.Cancel.IsEnabled);
        Assert.StartsWith("Saving to chosen location", shell.StatusText, StringComparison.Ordinal);

        // The final boundary is the only part that cannot be interrupted.
        operation.SetPublication(FilePublicationStatus.ProviderDeliveryCompleted, finalizing: true);
        shell.Settle();
        Assert.False(shell.Cancel.IsEnabled);
        Assert.Equal("Finishing…", shell.StatusText);

        await operation.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ARunningOperationDisablesTheOtherFileCommandsAndNothingElse()
    {
        await using var shell = await ShellFixture.CreateAsync();
        var before = shell.EnabledCommandNames();

        Assert.True(shell.Operations.TryBegin(FileOperationKind.Export, "Preparing export…", out var operation));
        shell.Settle();
        var during = shell.EnabledCommandNames();

        // Reading, searching and inspecting are never among the commands taken away.
        Assert.DoesNotContain("Recent captures…", before.Except(during));
        Assert.DoesNotContain("●  ADB live", before.Except(during));

        // Every route that would claim the one shell-owned file slot is unavailable before
        // it can launch a second picker. This includes the primary action as well as a folded
        // command; dispatch still keeps the owner's duplicate-start guard as a second line.
        Assert.Contains("＋  Open log", before.Except(during));
        Assert.Contains("Open archive", before.Except(during));

        // And a command that has stopped answering says which operation is holding it, rather
        // than being merely grey — which is nothing at all to a reader who cannot see grey.
        var reasons = shell.View.GetLogicalDescendants()
            .OfType<Button>()
            .Where(static button => !button.IsEnabled)
            .Select(AutomationProperties.GetHelpText)
            .Where(static help => !string.IsNullOrEmpty(help))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("Preparing export is in progress. Try again when it finishes.", reasons);

        await operation!.DisposeAsync();
        shell.Settle();
        Assert.Equal(before, shell.EnabledCommandNames());

        // The reason goes with the state that caused it.
        Assert.DoesNotContain(
            shell.View.GetLogicalDescendants().OfType<Button>(),
            static button => AutomationProperties.GetHelpText(button)?.Contains(
                "is in progress",
                StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// The card costs the workspace one band while it exists, and gives it back exactly.
    /// </summary>
    /// <remarks>
    /// Two docked bands at the bottom of one shell is the arrangement most likely to be
    /// measured twice or written from a second place. The workspace's height is therefore
    /// the assertion: it shrinks once when the card appears, shrinks again for a notice
    /// beside it, and returns to the number it started at when both are gone.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheCardCostsTheWorkspaceOneBandAndReturnsItExactly()
    {
        await using var shell = await ShellFixture.CreateAsync();
        var unencumbered = shell.WorkspaceHeight;
        Assert.True(unencumbered > 0, "the workspace must have a measured height to compare");

        Assert.True(shell.Operations.TryBegin(FileOperationKind.Export, "Preparing export…", out var operation));
        shell.Settle();
        var withCard = shell.WorkspaceHeight;
        Assert.True(withCard < unencumbered, "the card must take its band from the workspace");

        // A notice raised beside the card must not make the card cost more. On this host the
        // notice lane is the command bar's own text, so the workspace keeps the height the
        // card left it; on a phone the lane is a second docked band, and the invariant that
        // matters on both is that neither band is subtracted twice.
        var costOfCard = unencumbered - withCard;
        shell.View.ShowNotice("Capture stopped: the device disconnected.", MainView.NoticeKind.Failure);
        shell.Settle();
        var withBoth = shell.WorkspaceHeight;
        Assert.True(withBoth <= withCard, "a notice must never give the workspace height back");
        Assert.True(
            unencumbered - withBoth < costOfCard * 2,
            "the operation band was subtracted twice while a notice was also present");

        await operation!.DisposeAsync();
        shell.Settle();
        shell.View.ShowNotice(string.Empty);
        shell.Settle();

        // Exactly back: the card borrowed the band and returned all of it.
        Assert.Equal(unencumbered, shell.WorkspaceHeight, precision: 3);
    }

    [AvaloniaFact]
    public async Task FocusReturnsToTheInitiatingCommandWhenCancellationRemovesTheCard()
    {
        await using var shell = await ShellFixture.CreateAsync();
        var invoker = shell.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button =>
                (AutomationProperties.GetName(button) ?? button.Content as string)?
                    .Contains("Open log", StringComparison.Ordinal) == true);
        invoker.Focus();
        shell.View.RememberFileOperationInvoker(invoker);
        Assert.True(shell.Operations.TryBegin(FileOperationKind.Open, "Opening log…", out var operation));
        shell.Settle();

        shell.Cancel.Focus();
        shell.Cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await operation!.DisposeAsync();
        shell.Settle();

        Assert.True(invoker.IsFocused);
    }

    /// <summary>A shell with direct access to its operation owner and card.</summary>
    private sealed class ShellFixture : IAsyncDisposable
    {
        private readonly Window _window;
        private readonly string _settingsPath;

        private ShellFixture(MainView view, Window window, string settingsPath)
        {
            View = view;
            _window = window;
            _settingsPath = settingsPath;
        }

        /// <summary>
        /// A shell over a settings file of its own, so a test never reads or writes the
        /// developer's real one.
        /// </summary>
        internal static Task<ShellFixture> CreateAsync()
        {
            var settingsPath = Path.Combine(
                Path.GetTempPath(),
                $"visualcat-operation-card-{Guid.NewGuid():N}",
                "settings.json");
            var view = new MainView(null, settingsPath);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var fixture = new ShellFixture(view, window, settingsPath);
            fixture.Settle();
            return Task.FromResult(fixture);
        }

        internal MainView View { get; }

        internal FileOperationOwner Operations => View.FileOperationsForTest;

        internal Border Band => View.FileOperationBandForTest;

        internal Button Cancel => Band.GetLogicalDescendants()
            .OfType<Button>()
            .Single(static button => (button.Content as string) == "Cancel");

        internal ProgressBar Progress => Band.GetLogicalDescendants().OfType<ProgressBar>().Single();

        internal string StatusText => Band.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(static text => text.Text ?? string.Empty)
            .First(static text => text.Length > 0);

        internal string[] EnabledCommandNames() => View.GetLogicalDescendants()
            .OfType<Button>()
            .Where(static button => button.IsEnabled)
            .Select(static button => AutomationProperties.GetName(button) ?? button.Content as string ?? string.Empty)
            .Where(static name => name.Length > 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        /// <summary>The band the workspace is left with, in logical pixels.</summary>
        internal double WorkspaceHeight => View.GetLogicalDescendants()
            .OfType<TabControl>()
            .First()
            .Bounds
            .Height;

        internal void Settle() =>
            PixelGestureAndTextScaleTests.PumpUntil(_window, static () => true, passes: 8);

        /// <summary>Waits past the card's 150 ms threshold for showing counts.</summary>
        internal async Task SettleCountedProgressAsync()
        {
            for (var pass = 0; pass < 30; pass++)
            {
                Settle();
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Settle();
        }

        public async ValueTask DisposeAsync()
        {
            _window.Close();
            await View.DisposeAsync();
            var directory = Path.GetDirectoryName(_settingsPath)!;
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
