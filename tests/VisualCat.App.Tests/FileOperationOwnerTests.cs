using VisualCat.App.Presentation;
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
}
