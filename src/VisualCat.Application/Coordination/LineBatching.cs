using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using VisualCat.Application.Ports;
using VisualCat.Domain.Entries;

namespace VisualCat.Application.Coordination;

/// <summary>
/// A value type on purpose: one of these exists per source line, so a record class here
/// put an object header and a GC reference on every line in the session — millions of
/// short-lived allocations that the surrounding <see cref="List{T}"/> then chased through
/// the heap. As a struct the list is one flat array (§19.3).
/// </summary>
internal readonly record struct LineSlice(int Offset, int Length, long RawOffset, bool ExceededLimit = false);

internal sealed class LineBatch(long batchId, long firstSequence, byte[] bytes, IReadOnlyList<LineSlice> lines)
{
    public long BatchId { get; } = batchId;

    /// <summary>
    /// Session sequence of <c>Lines[0]</c>. The reader is the only stage that sees lines
    /// in source order, so stamping the number here lets a parse worker build each
    /// <see cref="SourceLine"/> with its final identity instead of the committer
    /// re-creating both the outcome and its source line to add it later.
    /// </summary>
    public long FirstSequence { get; } = firstSequence;

    public byte[] Bytes { get; } = bytes;
    public IReadOnlyList<LineSlice> Lines { get; } = lines;
}

internal sealed record ParsedBatch(long BatchId, IReadOnlyList<ParseOutcome> Outcomes);

internal static class LineBatching
{
    public static async IAsyncEnumerable<LineBatch> ReadBatchesAsync(
        ILogSource source,
        SourceReadContext context,
        int targetBytes,
        int maximumLineBytes,
        TimeSpan maximumLatency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLineBytes);

        // A size-only trigger starves live sources: a device emitting tens of kilobytes
        // per second would buffer for a minute before the first batch, so nothing is
        // committed, no snapshot is published, and the workspace stays empty. Batches
        // therefore also close on elapsed time (§10.6, §15.2). The timeout is raced
        // against MoveNext below; checking elapsed time only after another chunk arrives
        // does not bound latency when a source emits one chunk and then goes quiet.
        var lastYield = Stopwatch.GetTimestamp();
        var latencyEnabled = maximumLatency > TimeSpan.Zero;

        var writer = new ArrayBufferWriter<byte>(Math.Min(targetBytes, 1024 * 1024));
        var lines = new List<LineSlice>();
        long batchId = 0;
        long rawOffset = 0;
        long nextSequence = 0;
        var lineStart = 0;
        var oversizedLine = false;

        // A stop or cancellation surfaces as an exception out of the source enumerator.
        // Enumerating by hand keeps that exception out of the yield path so the bytes
        // buffered since the last batch boundary are still published: dropping them
        // would silently lose up to `targetBytes` of captured log on every stop (§13.7).
        await using var chunks = source.ReadAsync(context, cancellationToken).GetAsyncEnumerator(cancellationToken);
        ExceptionDispatchInfo? interrupted = null;
        Task<bool>? pendingRead = null;
        while (true)
        {
            try
            {
                pendingRead ??= chunks.MoveNextAsync().AsTask();
            }
            catch (OperationCanceledException exception)
            {
                interrupted = ExceptionDispatchInfo.Capture(exception);
                break;
            }

            if (latencyEnabled && lines.Count > 0 && !pendingRead.IsCompleted)
            {
                var remaining = maximumLatency - Stopwatch.GetElapsedTime(lastYield);
                if (remaining > TimeSpan.Zero)
                {
                    var timeout = Task.Delay(remaining, CancellationToken.None);
                    if (await Task.WhenAny(pendingRead, timeout).ConfigureAwait(false) != pendingRead)
                    {
                        yield return CompleteReadyBatch(
                            ref writer,
                            lines,
                            batchId++,
                            nextSequence,
                            ref lineStart);
                        nextSequence += lines.Count;
                        lines = [];
                        lastYield = Stopwatch.GetTimestamp();
                        continue;
                    }
                }
                else
                {
                    yield return CompleteReadyBatch(
                        ref writer,
                        lines,
                        batchId++,
                        nextSequence,
                        ref lineStart);
                    nextSequence += lines.Count;
                    lines = [];
                    lastYield = Stopwatch.GetTimestamp();
                    continue;
                }
            }

            bool hasNext;
            try
            {
                hasNext = await pendingRead.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                interrupted = ExceptionDispatchInfo.Capture(exception);
                break;
            }

            if (!hasNext)
            {
                break;
            }

            pendingRead = null;
            var chunk = chunks.Current;

            if (chunk.RawOffset != rawOffset)
            {
                throw new InvalidDataException($"Source chunk offset {chunk.RawOffset} does not follow {rawOffset}.");
            }

            // Held as memory, not span: a span cannot survive the yield below.
            var chunkBytes = chunk.Bytes;
            var consumed = 0;
            while (consumed < chunkBytes.Length)
            {
                var newlineIndex = chunkBytes.Span[consumed..].IndexOf((byte)'\n');
                var segmentLength = newlineIndex < 0 ? chunkBytes.Length - consumed : newlineIndex + 1;
                var segmentEnd = checked(consumed + segmentLength);

                // The parser cannot enforce a line-size limit until batching hands it a
                // line. Without this guard, newline-free hostile input grows `writer`
                // to the size of the entire source and can exhaust memory first. Feed
                // at most one rejected fragment at a time: besides bounding the carried
                // line, this avoids repeatedly copying the remainder of a large source
                // chunk when an unusually small safety limit is configured.
                while (consumed < segmentEnd)
                {
                    var currentLineBytes = writer.WrittenCount - lineStart;
                    var fragmentCapacity = checked(maximumLineBytes + 1 - currentLineBytes);
                    var take = Math.Min(segmentEnd - consumed, fragmentCapacity);
                    writer.Write(chunkBytes.Span.Slice(consumed, take));
                    consumed += take;
                    rawOffset += take;

                    if (writer.WrittenCount - lineStart > maximumLineBytes)
                    {
                        var fragmentLength = checked(maximumLineBytes + 1);
                        var fragmentRawOffset = rawOffset - fragmentLength;
                        lines.Add(new LineSlice(lineStart, fragmentLength, fragmentRawOffset, true));
                        yield return new LineBatch(batchId++, nextSequence, writer.WrittenMemory.ToArray(), lines);
                        nextSequence += lines.Count;

                        writer = new ArrayBufferWriter<byte>(Math.Max(1024, Math.Min(targetBytes, 1024 * 1024)));
                        lines = [];
                        lineStart = 0;
                        oversizedLine = true;
                        lastYield = Stopwatch.GetTimestamp();
                    }
                }

                if (newlineIndex >= 0)
                {
                    var lineLength = writer.WrittenCount - lineStart;
                    if (lineLength > 0)
                    {
                        lines.Add(new LineSlice(
                            lineStart,
                            lineLength,
                            rawOffset - lineLength,
                            oversizedLine));
                    }

                    lineStart = writer.WrittenCount;
                    oversizedLine = false;
                    if (writer.WrittenCount >= targetBytes)
                    {
                        yield return CompleteBatch(ref writer, lines, batchId++, nextSequence);
                        nextSequence += lines.Count;
                        lines = [];
                        lineStart = 0;
                        lastYield = Stopwatch.GetTimestamp();
                    }
                }
            }

            // Fast-path a timeout already reached while this chunk was being consumed.
            // CompleteReadyBatch keeps a trailing partial line in the new writer, so
            // completed lines do not have to wait for that line's next chunk.
            if (latencyEnabled &&
                lines.Count > 0 &&
                Stopwatch.GetElapsedTime(lastYield) >= maximumLatency)
            {
                yield return CompleteReadyBatch(
                    ref writer,
                    lines,
                    batchId++,
                    nextSequence,
                    ref lineStart);
                nextSequence += lines.Count;
                lines = [];
                lastYield = Stopwatch.GetTimestamp();
            }
        }

        if (lineStart < writer.WrittenCount)
        {
            lines.Add(new LineSlice(
                lineStart,
                writer.WrittenCount - lineStart,
                rawOffset - (writer.WrittenCount - lineStart),
                oversizedLine));
        }

        if (lines.Count > 0)
        {
            yield return CompleteBatch(ref writer, lines, batchId, nextSequence);
        }

        // Only now that the tail is published does the stop become observable, so the
        // coordinator still classifies it as a graceful stop or a hard cancellation.
        interrupted?.Throw();
    }

    private static LineBatch CompleteBatch(
        ref ArrayBufferWriter<byte> writer,
        IReadOnlyList<LineSlice> lines,
        long batchId,
        long firstSequence)
    {
        var bytes = writer.WrittenMemory.ToArray();
        writer = new ArrayBufferWriter<byte>(Math.Max(1024, bytes.Length));
        return new LineBatch(batchId, firstSequence, bytes, lines);
    }

    /// <summary>
    /// Closes the completed prefix while retaining any newline-free tail for the next
    /// chunk. Line offsets are relative to the completed prefix and therefore remain
    /// valid; raw offsets are absolute and need no adjustment.
    /// </summary>
    private static LineBatch CompleteReadyBatch(
        ref ArrayBufferWriter<byte> writer,
        IReadOnlyList<LineSlice> lines,
        long batchId,
        long firstSequence,
        ref int lineStart)
    {
        var completedLength = lineStart;
        var bytes = writer.WrittenMemory[..completedLength].ToArray();
        var tail = writer.WrittenMemory[completedLength..].ToArray();
        writer = new ArrayBufferWriter<byte>(Math.Max(1024, Math.Max(bytes.Length, tail.Length)));
        writer.Write(tail);
        lineStart = 0;
        return new LineBatch(batchId, firstSequence, bytes, lines);
    }
}
