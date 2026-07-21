using System.Text;
using VisualCat.Domain.Entries;

namespace VisualCat.Core.Store;

internal static class SourceRecordCodec
{
    private const int MaximumReasonBytes = 64 * 1024;

    public static void Write(BinaryWriter writer, SourceRecord record)
    {
        writer.Write(record.Sequence);
        writer.Write(record.Raw.Offset);
        writer.Write(record.Raw.Length);
        writer.Write((byte)record.Outcome);
        writer.Write(record.EntryId ?? -1);
        writer.Write(record.Reason ?? string.Empty);
    }

    public static SourceRecord Read(BinaryReader reader)
    {
        EnsureRemaining(reader, sizeof(long) * 3 + sizeof(int) + sizeof(byte));
        var sequence = reader.ReadInt64();
        var offset = reader.ReadInt64();
        var length = reader.ReadInt32();
        var outcome = (ParseOutcomeKind)reader.ReadByte();
        var entryId = reader.ReadInt64();
        var reasonLength = Read7BitEncodedNonNegativeInt(reader);
        if (reasonLength > MaximumReasonBytes)
        {
            throw new InvalidDataException($"Source-record reason length {reasonLength} exceeds the safety limit.");
        }

        EnsureRemaining(reader, reasonLength);
        var reasonBytes = reader.ReadBytes(reasonLength);
        if (reasonBytes.Length != reasonLength)
        {
            throw new EndOfStreamException("Source-record reason is truncated.");
        }

        var reason = Encoding.UTF8.GetString(reasonBytes);
        return new SourceRecord(
            sequence,
            new RawSpan(offset, length),
            outcome,
            entryId < 0 ? null : entryId,
            reason.Length == 0 ? null : reason);
    }

    private static int Read7BitEncodedNonNegativeInt(BinaryReader reader)
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            EnsureRemaining(reader, 1);
            var value = reader.ReadByte();
            if (shift == 28 && (value & 0xf0) != 0)
            {
                throw new InvalidDataException("Invalid source-record string length.");
            }

            result |= (uint)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
            {
                return checked((int)result);
            }
        }

        throw new InvalidDataException("Invalid source-record string length.");
    }

    private static void EnsureRemaining(BinaryReader reader, long required)
    {
        var stream = reader.BaseStream;
        if (!stream.CanSeek || required < 0 || stream.Length - stream.Position < required)
        {
            throw new EndOfStreamException("Source-record stream is truncated.");
        }
    }
}
