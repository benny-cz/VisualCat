using VisualCat.App.Platform;

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
}
