using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using VisualCat.Domain.Entries;

namespace VisualCat.Core.Store;

internal static class SegmentWriter
{
    public static SegmentManifest Write(
        string sessionRoot,
        int id,
        IReadOnlyList<NormalizedEntry> unsorted,
        Func<string, uint> internTag,
        Func<string, uint> internBuffer,
        string segmentContainer = "segments")
    {
        if (unsorted.Count == 0)
        {
            throw new ArgumentException("Cannot write an empty segment.", nameof(unsorted));
        }

        var entries = unsorted
            .Where(static entry => entry.Timestamp is not null)
            .OrderBy(static entry => entry.Timestamp!.Value.Value)
            .ThenBy(static entry => entry.SourceSequence)
            .ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("Segment contains no timed entries.", nameof(unsorted));
        }

        var relative = Path.Combine(segmentContainer, id.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
        var directory = Path.Combine(sessionRoot, relative);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "bitmaps"));

        // Columns are built in memory and written once. Emitting them field by field
        // through BinaryWriter cost one virtual call per value — nineteen per entry —
        // and hashing afterwards re-read every column back off disk (§19.3).
        var count = entries.Length;
        var timestamp = new ColumnBuffer(count, sizeof(long));
        var sequence = new ColumnBuffer(count, sizeof(long));
        var rawOffset = new ColumnBuffer(count, sizeof(long));
        var rawLength = new ColumnBuffer(count, sizeof(int));
        var pid = new ColumnBuffer(count, sizeof(int));
        var tid = new ColumnBuffer(count, sizeof(int));
        var level = new ColumnBuffer(count, sizeof(byte));
        var tag = new ColumnBuffer(count, sizeof(uint));
        var template = new ColumnBuffer(count, sizeof(uint));
        var flags = new ColumnBuffer(count, sizeof(ushort));
        var provenance = new ColumnBuffer(count, sizeof(byte));
        var confidence = new ColumnBuffer(count, sizeof(byte));
        var format = new ColumnBuffer(count, sizeof(byte));
        var buffer = new ColumnBuffer(count, sizeof(uint));
        var messageOffset = new ColumnBuffer(count, sizeof(long));
        var messageLength = new ColumnBuffer(count, sizeof(int));
        var originalOffset = new ColumnBuffer(count, sizeof(long));
        var originalLength = new ColumnBuffer(count, sizeof(int));
        var payload = new PayloadBuffer(count * 64);

        var bitWords = new Dictionary<LogLevel, ulong[]>();
        foreach (var severity in LogLevels.StorageOrder)
        {
            bitWords.Add(severity, new ulong[(count + 63) / 64]);
        }

        var minimumSequence = long.MaxValue;
        var maximumSequence = long.MinValue;
        try
        {
            for (var index = 0; index < count; index++)
            {
                var entry = entries[index];
                timestamp.WriteInt64(index, entry.Timestamp!.Value.Value);
                sequence.WriteInt64(index, entry.SourceSequence);
                rawOffset.WriteInt64(index, entry.Raw.Offset);
                rawLength.WriteInt32(index, entry.Raw.Length);
                pid.WriteInt32(index, entry.Pid);
                tid.WriteInt32(index, entry.Tid);
                level.WriteByte(index, (byte)entry.Level);
                tag.WriteUInt32(index, internTag(entry.Tag));
                template.WriteUInt32(index, entry.TemplateId);
                flags.WriteUInt16(index, (ushort)entry.Flags);
                provenance.WriteByte(index, (byte)entry.TimestampProvenance);
                confidence.WriteByte(index, (byte)Math.Clamp((int)Math.Round(entry.TimestampConfidence * 255d), 0, 255));
                format.WriteByte(index, (byte)entry.Format);
                buffer.WriteUInt32(index, internBuffer(entry.Buffer));

                var messageStart = payload.Append(entry.Message);
                messageOffset.WriteInt64(index, messageStart);
                messageLength.WriteInt32(index, payload.Length - messageStart);

                var originalStart = payload.Append(entry.OriginalTimestamp);
                originalOffset.WriteInt64(index, originalStart);
                originalLength.WriteInt32(index, payload.Length - originalStart);

                bitWords[entry.Level][index >> 6] |= 1UL << (index & 63);
                minimumSequence = Math.Min(minimumSequence, entry.SourceSequence);
                maximumSequence = Math.Max(maximumSequence, entry.SourceSequence);
            }

            var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
            Emit(directory, checksums, "timestamp.bin", timestamp);
            Emit(directory, checksums, "sequence.bin", sequence);
            Emit(directory, checksums, "raw-offset.bin", rawOffset);
            Emit(directory, checksums, "raw-length.bin", rawLength);
            Emit(directory, checksums, "pid.bin", pid);
            Emit(directory, checksums, "tid.bin", tid);
            Emit(directory, checksums, "level.bin", level);
            Emit(directory, checksums, "tag.bin", tag);
            Emit(directory, checksums, "template.bin", template);
            Emit(directory, checksums, "flags.bin", flags);
            Emit(directory, checksums, "provenance.bin", provenance);
            Emit(directory, checksums, "confidence.bin", confidence);
            Emit(directory, checksums, "format.bin", format);
            Emit(directory, checksums, "buffer.bin", buffer);
            Emit(directory, checksums, "message-offset.bin", messageOffset);
            Emit(directory, checksums, "message-length.bin", messageLength);
            Emit(directory, checksums, "original-offset.bin", originalOffset);
            Emit(directory, checksums, "original-length.bin", originalLength);
            EmitBytes(directory, checksums, "payload.bin", payload.WrittenSpan);

            foreach (var pair in bitWords)
            {
                var name = $"bitmaps/level-{(byte)pair.Key}.rbm";
                new RankBitmap(count, pair.Value).Save(Path.Combine(directory, "bitmaps", $"level-{(byte)pair.Key}.rbm"));
                checksums[name] = HashFile(Path.Combine(directory, "bitmaps", $"level-{(byte)pair.Key}.rbm"));
            }

            // The reader and verifier resolve segment files through the contract, so a
            // column added there but not emitted here must fail loudly at write time
            // rather than as a missing-file error when the session is reopened.
            foreach (var required in SegmentFileContract.RequiredRelativePaths())
            {
                if (!checksums.ContainsKey(required))
                {
                    throw new InvalidOperationException($"Segment writer did not emit '{required}'.");
                }
            }

            // Written beside the segment rather than returned into the manifest: the
            // manifest is republished in full on every snapshot, and carrying twenty-six
            // digests per segment there is what made it grow without bound during a long
            // live capture.
            SegmentChecksums.Write(directory, checksums);

            // DirectoryInfo hands back FileInfo objects already populated from the
            // directory enumeration. Enumerating names and then constructing a FileInfo
            // per name stats all twenty-seven files a second time, which measured eight
            // times slower for the same answer, on the commit thread, once per segment.
            var sizeBytes = new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(static file => file.Length);

            return new SegmentManifest(
                id,
                relative.Replace('\\', '/'),
                count,
                entries[0].Timestamp!.Value.Value,
                entries[^1].Timestamp!.Value.Value,
                minimumSequence,
                maximumSequence,
                SizeBytes: sizeBytes);
        }
        finally
        {
            timestamp.Dispose();
            sequence.Dispose();
            rawOffset.Dispose();
            rawLength.Dispose();
            pid.Dispose();
            tid.Dispose();
            level.Dispose();
            tag.Dispose();
            template.Dispose();
            flags.Dispose();
            provenance.Dispose();
            confidence.Dispose();
            format.Dispose();
            buffer.Dispose();
            messageOffset.Dispose();
            messageLength.Dispose();
            originalOffset.Dispose();
            originalLength.Dispose();
            payload.Dispose();
        }
    }

    private static void Emit(
        string directory,
        Dictionary<string, string> checksums,
        string name,
        ColumnBuffer column) =>
        EmitBytes(directory, checksums, name, column.WrittenSpan);

    private static void EmitBytes(
        string directory,
        Dictionary<string, string> checksums,
        string name,
        ReadOnlySpan<byte> bytes)
    {
        using (var stream = new FileStream(
                   Path.Combine(directory, name),
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   0,
                   FileOptions.SequentialScan))
        {
            stream.Write(bytes);
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        checksums[name] = Convert.ToHexString(hash);
    }

    internal static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Fixed-width column staged in a pooled buffer and written in one call.</summary>
    private sealed class ColumnBuffer(int count, int width) : IDisposable
    {
        private readonly byte[] _bytes = ArrayPool<byte>.Shared.Rent(count * width);
        private readonly int _width = width;
        private readonly int _length = count * width;

        public ReadOnlySpan<byte> WrittenSpan => _bytes.AsSpan(0, _length);

        public void WriteInt64(int index, long value) =>
            BinaryPrimitives.WriteInt64LittleEndian(Slot(index), value);

        public void WriteInt32(int index, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(Slot(index), value);

        public void WriteUInt32(int index, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(Slot(index), value);

        public void WriteUInt16(int index, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(Slot(index), value);

        public void WriteByte(int index, byte value) => Slot(index)[0] = value;

        public void Dispose() => ArrayPool<byte>.Shared.Return(_bytes);

        private Span<byte> Slot(int index) => _bytes.AsSpan(index * _width, _width);
    }

    /// <summary>Growable UTF-8 payload staged in a pooled buffer.</summary>
    private sealed class PayloadBuffer(int capacity) : IDisposable
    {
        private byte[] _bytes = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 1024));

        public int Length { get; private set; }

        public ReadOnlySpan<byte> WrittenSpan => _bytes.AsSpan(0, Length);

        public int Append(string value)
        {
            var start = Length;
            if (value.Length == 0)
            {
                return start;
            }

            var required = Encoding.UTF8.GetMaxByteCount(value.Length);
            if (Length + required > _bytes.Length)
            {
                Grow(Length + required);
            }

            Length += Encoding.UTF8.GetBytes(value, _bytes.AsSpan(Length));
            return start;
        }

        public void Dispose() => ArrayPool<byte>.Shared.Return(_bytes);

        private void Grow(int required)
        {
            var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(required, _bytes.Length * 2));
            _bytes.AsSpan(0, Length).CopyTo(replacement);
            ArrayPool<byte>.Shared.Return(_bytes);
            _bytes = replacement;
        }
    }
}
