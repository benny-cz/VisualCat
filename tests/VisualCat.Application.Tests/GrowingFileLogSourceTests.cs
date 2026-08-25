using VisualCat.Application.Ports;
using VisualCat.Infrastructure.Files;

namespace VisualCat.Application.Tests;

/// <summary>
/// Tests that read a process-wide GC counter, and so cannot share the process with tests
/// that are busy allocating.
/// </summary>
/// <remarks>
/// <see cref="GC.GetTotalAllocatedBytes(bool)"/> reports the whole process. Run alongside
/// the ingest tests, which move hundreds of megabytes through the pipeline, it reported
/// 848 MB for a source that had allocated about one. There is no per-object allocation
/// counter to use instead — and no thread-local one either, because the source under test
/// awaits with <c>ConfigureAwait(false)</c> and resumes on whatever pool thread it likes —
/// so the measurement has to have the process to itself.
/// </remarks>
[CollectionDefinition(nameof(AllocationMeasurementGroup), DisableParallelization = true)]
public sealed class AllocationMeasurementGroup;

[Collection(nameof(AllocationMeasurementGroup))]
public sealed class GrowingFileLogSourceAllocationTests
{
    /// <summary>
    /// Following an idle file allocates a bounded amount, no matter how many times the poll
    /// loop goes round.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read buffer is a mebibyte, comfortably a large-object allocation, and it used to
    /// be allocated inside the poll loop. A follow that was waiting on a quiet file
    /// therefore spent about four mebibytes a second — roughly fifteen gibibytes an hour —
    /// and a continuous gen2 collection cadence to deliver nothing at all, on a heap that is
    /// not compacted by default. That is the cost of running, not the cost of reading, which
    /// is what made it a defect rather than churn.
    /// </para>
    /// <para>
    /// The assertion is deliberately loose. It is not measuring an allocation budget, it is
    /// separating "one buffer for the life of the read" from "one buffer per tick": at this
    /// poll interval the old shape allocated tens of mebibytes over this window and the
    /// current one allocates a little over the single buffer it keeps.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task IdleFollowDoesNotAllocatePerPollTick()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vcat-idle-follow-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, "05-15 14:13:37.496  1  1 D Tag: seed\n", TestContext.Current.CancellationToken);
        try
        {
            const int chunkBytes = 1024 * 1024;
            await using var source = new GrowingFileLogSource(path, TimeSpan.FromMilliseconds(20), chunkBytes);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var before = GC.GetTotalAllocatedBytes(precise: true);
            try
            {
                await foreach (var chunk in source.ReadAsync(
                    new SourceReadContext(Guid.NewGuid(), 0, Path.GetTempPath()),
                    stop.Token))
                {
                    _ = chunk;
                }
            }
            catch (OperationCanceledException)
            {
            }

            var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

            // Two seconds at a 20 ms poll is about a hundred ticks. Per-tick allocation of
            // the buffer would be ~100 MiB; four buffers' worth is far below that and far
            // above anything the fixed shape can reach.
            Assert.True(
                allocated < 4L * chunkBytes,
                $"Idle follow allocated {allocated:N0} bytes, which is per-tick buffer allocation rather than one buffer for the read.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class GrowingFileLogSourceTests
{
    private static SourceReadContext Context() =>
        new(Guid.NewGuid(), 0, Path.GetTempPath());

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
