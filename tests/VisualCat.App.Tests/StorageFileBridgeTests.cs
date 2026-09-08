using VisualCat.App.Platform;
using VisualCat.Application.UseCases;

namespace VisualCat.App.Tests;

public sealed class StorageFileBridgeTests
{
    [Fact]
    public async Task ProviderStreamMaterializesWithSafeNameAndExactBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-storage-{Guid.NewGuid():N}");
        var expected = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        try
        {
            await using var input = new MemoryStream(expected);
            var materialized = await StorageFileBridge.CopyToTemporaryAsync(input, "../unsafe/log.txt", root);

            Assert.True(materialized.IsTemporary);
            Assert.Equal(root, Path.GetDirectoryName(materialized.Path));
            Assert.DoesNotContain("..", Path.GetFileName(materialized.Path), StringComparison.Ordinal);
            Assert.Equal(expected, await File.ReadAllBytesAsync(materialized.Path));

            materialized.DeleteIfTemporary();
            Assert.False(File.Exists(materialized.Path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PublishingToProviderStreamTruncatesPreviousContent()
    {
        var source = Path.Combine(Path.GetTempPath(), $"visualcat-storage-source-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(source, "new");
            await using var output = new MemoryStream("stale trailing content"u8.ToArray());

            await StorageFileBridge.CopyFileToStreamAsync(source, output);

            Assert.Equal("new"u8.ToArray(), output.ToArray());
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task CancellationWaitsForAStubbornProviderAndNeverReportsCompletedDelivery()
    {
        var output = new GatedWriteStream(throwAfterRelease: false);
        using var stop = new CancellationTokenSource();
        string? stagingPath = null;
        var publication = new List<StorageWritePublication>();

        var writing = StorageFileBridge.WriteToProviderAsync(
            "provider.csv",
            async (path, token) =>
            {
                stagingPath = path;
                await File.WriteAllBytesAsync(path, new byte[2 * 1024 * 1024], token);
            },
            () => Task.FromResult<Stream>(output),
            progress: new Progress<FileWorkProgress>(),
            publication.Add,
            stop.Token);

        await output.WriteStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal([StorageWritePublication.ProviderDeliveryStarted], publication);

        await stop.CancelAsync();
        Assert.False(writing.IsCompleted, "a cancellation request must wait for the native/provider call to return");
        output.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        Assert.DoesNotContain(StorageWritePublication.ProviderDeliveryCompleted, publication);
        Assert.NotNull(stagingPath);
        Assert.False(File.Exists(stagingPath));
    }

    [Fact]
    public async Task ProviderFailureAfterDeliveryStartsKeepsFailureDistinctAndCleansStaging()
    {
        var output = new GatedWriteStream(throwAfterRelease: true);
        string? stagingPath = null;
        var publication = new List<StorageWritePublication>();
        var writing = StorageFileBridge.WriteToProviderAsync(
            "provider.csv",
            async (path, token) =>
            {
                stagingPath = path;
                await File.WriteAllTextAsync(path, "provider payload", token);
            },
            () => Task.FromResult<Stream>(output),
            progress: null,
            publication.Add,
            TestContext.Current.CancellationToken);

        await output.WriteStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        output.Release.TrySetResult();

        await Assert.ThrowsAsync<IOException>(() => writing);
        Assert.Equal([StorageWritePublication.ProviderDeliveryStarted], publication);
        Assert.NotNull(stagingPath);
        Assert.False(File.Exists(stagingPath));
    }

    private sealed class GatedWriteStream(bool throwAfterRelease) : MemoryStream
    {
        private int _writes;
        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writes) == 1)
            {
                WriteStarted.TrySetResult();
                // Deliberately ignore the operation token until the simulated provider
                // returns, which is the platform limitation the product must describe.
                await Release.Task;
                await base.WriteAsync(buffer, CancellationToken.None);
                if (throwAfterRelease)
                {
                    throw new IOException("The provider disconnected after accepting bytes.");
                }

                return;
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
