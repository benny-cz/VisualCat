using VisualCat.Application.Ports;
using VisualCat.Infrastructure.Files;

namespace VisualCat.Application.Tests;

public sealed class GrowingFileLogSourceTests
{
    private static SourceReadContext Context() =>
        new(Guid.NewGuid(), 0, Path.GetTempPath());

    /// <summary>
    /// Repeated reads use one long-lived buffer rather than allocating a chunk-sized buffer
    /// on every pass through the follow loop.
    /// </summary>
    /// <remarks>
    /// The previous allocation test sampled a process-wide GC counter. Coverage collectors
    /// and test-host background work also contribute to that counter, which made the result
    /// depend on the CI runner instead of the source. Counting the buffer factory directly
    /// pins the same regression without a timing or machine-load threshold.
    /// </remarks>
    [Fact]
    public async Task RepeatedReadsCreateOneLongLivedReadBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vcat-follow-buffer-{Guid.NewGuid():N}.log");
        var first = "05-15 14:13:37.496  1  1 D Tag: first\n";
        var second = "05-15 14:13:38.500  2  2 W Tag: second\n";
        await File.WriteAllTextAsync(path, first, TestContext.Current.CancellationToken);
        try
        {
            var bufferCreations = 0;
            await using var source = new GrowingFileLogSource(
                path,
                TimeSpan.FromMilliseconds(20),
                1024 * 1024,
                size =>
                {
                    Interlocked.Increment(ref bufferCreations);
                    return new byte[size];
                });
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var chunks = 0;
            await foreach (var chunk in source.ReadAsync(Context(), stop.Token))
            {
                _ = chunk;
                if (++chunks == 1)
                {
                    await File.AppendAllTextAsync(path, second, TestContext.Current.CancellationToken);
                    continue;
                }

                break;
            }

            Assert.Equal(2, chunks);
            Assert.Equal(1, Volatile.Read(ref bufferCreations));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A chunk handed to the consumer is its own array, sized to the bytes actually read,
    /// and survives the reads that follow it.
    /// </summary>
    /// <remarks>
    /// The read buffer is now reused across iterations, so yielding a window onto it would
    /// let the next read rewrite a chunk the pipeline had already accepted — silent
    /// corruption of captured evidence rather than a crash. This is the invariant that makes
    /// reuse safe, so it is asserted directly rather than inferred from the allocation test.
    /// </remarks>
    [Fact]
    public async Task ChunksAreIndependentOfTheReadBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vcat-follow-chunks-{Guid.NewGuid():N}.log");
        var first = "05-15 14:13:37.496  1  1 D Tag: first\n";
        var second = "05-15 14:13:38.500  2  2 W Tag: second-line-is-longer\n";
        await File.WriteAllTextAsync(path, first, TestContext.Current.CancellationToken);
        try
        {
            await using var source = new GrowingFileLogSource(path, TimeSpan.FromMilliseconds(20), 1024 * 1024);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var collected = new List<byte[]>();
            try
            {
                await foreach (var chunk in source.ReadAsync(Context(), stop.Token))
                {
                    collected.Add(chunk.Bytes.ToArray());

                    // Keep the memory itself, not a copy: if the source handed back a window
                    // onto a shared buffer, appending below would rewrite it underneath us.
                    if (collected.Count == 1)
                    {
                        Assert.Equal(first.Length, chunk.Bytes.Length);
                        await File.AppendAllTextAsync(path, second, TestContext.Current.CancellationToken);
                        continue;
                    }

                    Assert.Equal(second.Length, chunk.Bytes.Length);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
            }

            Assert.Equal(2, collected.Count);
            Assert.Equal(first, System.Text.Encoding.UTF8.GetString(collected[0]));
            Assert.Equal(second, System.Text.Encoding.UTF8.GetString(collected[1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Offsets stay contiguous across reads, so the pipeline's byte attribution is unchanged
    /// by the buffer reuse.
    /// </summary>
    [Fact]
    public async Task OffsetsRemainContiguous()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vcat-follow-offsets-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, "line one\n", TestContext.Current.CancellationToken);
        try
        {
            await using var source = new GrowingFileLogSource(path, TimeSpan.FromMilliseconds(20), 1024 * 1024);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            long expectedOffset = 0;
            var chunks = 0;
            try
            {
                await foreach (var chunk in source.ReadAsync(Context(), stop.Token))
                {
                    Assert.Equal(expectedOffset, chunk.RawOffset);
                    expectedOffset += chunk.Bytes.Length;
                    if (++chunks == 1)
                    {
                        await File.AppendAllTextAsync(path, "line two\n", TestContext.Current.CancellationToken);
                        continue;
                    }

                    break;
                }
            }
            catch (OperationCanceledException)
            {
            }

            Assert.Equal(2, chunks);
            Assert.Equal(new FileInfo(path).Length, expectedOffset);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
