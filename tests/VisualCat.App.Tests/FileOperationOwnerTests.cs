using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Tests;

public sealed class FileOperationOwnerTests
{
    [Fact]
    public async Task OwnerSerializesCancellationAndReleasesTheSlot()
    {
        await using var owner = new FileOperationOwner();
        Assert.True(owner.TryBegin(FileOperationKind.Export, "Preparing export…", out var first));
        Assert.NotNull(first);
        Assert.False(owner.TryBegin(FileOperationKind.Open, "Opening file…", out _));

        first!.Report(new FileWorkProgress(FileWorkStage.WritingRows, 10, 100, "rows"));
        Assert.Equal(FileOperationPhase.Running, owner.Current?.Phase);
        owner.Cancel();
        Assert.True(first.Token.IsCancellationRequested);
        Assert.Equal(FileOperationPhase.Cancelling, owner.Current?.Phase);

        await first.DisposeAsync();
        Assert.Null(owner.Current);
        Assert.True(owner.TryBegin(FileOperationKind.Open, "Opening file…", out var second));
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task PublishingMakesCancellationIrreversible()
    {
        await using var owner = new FileOperationOwner();
        Assert.True(owner.TryBegin(FileOperationKind.Save, "Saving…", out var operation));

        operation!.BeginPublishing();
        owner.Cancel();

        Assert.False(operation.Token.IsCancellationRequested);
        Assert.False(owner.Current?.CanCancel);
        Assert.Equal(FileOperationPhase.Publishing, owner.Current?.Phase);
        Assert.Equal(FilePublicationStatus.NotPublished, operation.Publication);

        operation.SetPublication(FilePublicationStatus.LocalCommitted, finalizing: true);
        Assert.Equal(FilePublicationStatus.LocalCommitted, operation.Publication);
        await operation.DisposeAsync();
    }

    [Fact]
    public async Task ACommitReportedAfterCancellationDoesNotReverseTheInterimPhase()
    {
        await using var owner = new FileOperationOwner();
        Assert.True(owner.TryBegin(FileOperationKind.Save, "Saving…", out var operation));

        owner.Cancel();
        operation!.SetPublication(FilePublicationStatus.LocalCommitted, finalizing: true);

        Assert.True(operation.Token.IsCancellationRequested);
        Assert.Equal(FileOperationPhase.Cancelling, owner.Current?.Phase);
        Assert.Equal(FilePublicationStatus.LocalCommitted, operation.Publication);
        await operation.DisposeAsync();
    }

    /// <summary>
    /// O1-F — an app-owned review is part of its file operation, so teardown must dismiss the
    /// review before it waits for the operation that is awaiting that review.
    /// </summary>
    [AvaloniaFact]
    public async Task ShellDisposalDismissesAFileReviewBeforeDrainingItsOperation()
    {
        var settingsPath = TemporarySettingsPath();
        MainView.InPageDialogOverride = true;
        var shell = new MainView(null, settingsPath);
        var review = new BlockingFileReview();
        Task? disposal = null;
        try
        {
            Assert.True(shell.FileOperationsForTest.TryBegin(
                FileOperationKind.Export,
                "Preparing export…",
                out var pending));
            var operation = Assert.IsType<FileOperationHandle>(pending);
            var cancellationObserved = false;
            var running = AwaitReviewAsync();
            shell.FileOperationsForTest.Track(operation, running);

            Assert.False(review.Completion.IsCompleted);
            disposal = shell.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(review.Completion.IsCompleted);
            Assert.True(cancellationObserved);
            Assert.Null(shell.FileOperationsForTest.Current);

            async Task AwaitReviewAsync()
            {
                await using (operation)
                {
                    await shell.ShowDialogAsync(review);
                    cancellationObserved = operation.Token.IsCancellationRequested;
                }
            }
        }
        finally
        {
            // If an assertion failed before disposal reached the dialog, release the same
            // dependency so the test cannot leave a shell task behind.
            review.ForceDismiss();
            if (disposal is not null)
            {
                await disposal;
            }
            else
            {
                await shell.DisposeAsync();
            }

            MainView.InPageDialogOverride = null;
            File.Delete(settingsPath);
        }
    }

    /// <summary>
    /// O1-F — Android can replace the view without a window-close disposal callback. The
    /// permanently hidden shell must still cancel and dismiss the file review it owned.
    /// </summary>
    [AvaloniaFact]
    public async Task AReplacementShellCancelsTheFileReviewOwnedByTheAbandonedShell()
    {
        var replacedSettings = TemporarySettingsPath();
        var liveSettings = TemporarySettingsPath();
        MainView.InPageDialogOverride = true;
        var replaced = new MainView(null, replacedSettings);
        MainView? live = null;
        var review = new BlockingFileReview();
        try
        {
            Assert.True(replaced.FileOperationsForTest.TryBegin(
                FileOperationKind.Open,
                "Opening log…",
                out var pending));
            var operation = Assert.IsType<FileOperationHandle>(pending);
            var cancellationObserved = false;
            var running = AwaitReviewAsync();
            replaced.FileOperationsForTest.Track(operation, running);

            live = new MainView(null, liveSettings);
            await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(review.Completion.IsCompleted);
            Assert.True(cancellationObserved);
            Assert.Null(replaced.FileOperationsForTest.Current);

            async Task AwaitReviewAsync()
            {
                await using (operation)
                {
                    await replaced.ShowDialogAsync(review);
                    cancellationObserved = operation.Token.IsCancellationRequested;
                }
            }
        }
        finally
        {
            review.ForceDismiss();
            if (live is not null)
            {
                await live.DisposeAsync();
            }

            await replaced.DisposeAsync();
            MainView.InPageDialogOverride = null;
            File.Delete(replacedSettings);
            File.Delete(liveSettings);
        }
    }

    private static string TemporarySettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"visualcat-file-operation-{Guid.NewGuid():N}.json");

    private sealed class BlockingFileReview() : DialogBody<bool>("File review");
}
