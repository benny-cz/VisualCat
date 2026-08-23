namespace VisualCat.Application.Ports;

/// <summary>
/// Emits only newline-complete records from a sequence of transport chunks.
/// </summary>
/// <remarks>
/// Keeping an incomplete record separate from committed source bytes is important for reconnecting
/// transports: a tail from a dead connection must never be joined to the replay that follows it.
/// </remarks>
internal sealed class NewlineRecordFramer
{
    private readonly int _maximumPendingBytes;
    private readonly List<byte> _pending = new(4096);

    internal NewlineRecordFramer(int maximumPendingBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPendingBytes);
        _maximumPendingBytes = maximumPendingBytes;
    }

    internal IReadOnlyList<ReadOnlyMemory<byte>> Append(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
        {
            return [];
        }

        var complete = new List<ReadOnlyMemory<byte>>(2);
        var cursor = 0;
        if (_pending.Count > 0)
        {
            var newline = Array.IndexOf(chunk, (byte)'\n');
            if (newline < 0)
            {
                AppendPending(chunk, 0, chunk.Length);
                return complete;
            }

            AppendPending(chunk, 0, newline + 1);
            complete.Add(FlushPending());
            cursor = newline + 1;
        }

        if (cursor >= chunk.Length)
        {
            return complete;
        }

        var lastNewline = Array.LastIndexOf(chunk, (byte)'\n', chunk.Length - 1, chunk.Length - cursor);
        if (lastNewline >= cursor)
        {
            complete.Add(chunk.AsMemory(cursor, lastNewline - cursor + 1));
            cursor = lastNewline + 1;
        }

        if (cursor < chunk.Length)
        {
            AppendPending(chunk, cursor, chunk.Length - cursor);
        }

        return complete;
    }

    /// <summary>Returns an unterminated final record when no later transport can follow it.</summary>
    internal byte[] FlushPending()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        var result = _pending.ToArray();
        _pending.Clear();
        return result;
    }

    /// <summary>Discards a dead transport's tail so reconnect replay cannot corrupt a record.</summary>
    internal int DiscardPending()
    {
        var count = _pending.Count;
        _pending.Clear();
        return count;
    }

    private void AppendPending(byte[] bytes, int offset, int count)
    {
        if (_pending.Count > _maximumPendingBytes - count)
        {
            throw new InvalidDataException(
                $"The source produced a newline-free record larger than the {_maximumPendingBytes:N0}-byte safety limit.");
        }

        _pending.AddRange(bytes.AsSpan(offset, count));
    }
}
