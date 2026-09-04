using System.Buffers.Binary;
using System.Text;
using VisualCat.Core.Store;

namespace VisualCat.Core.Tests;

public sealed class MappedColumnTests
{
    [Fact]
    public void ScalarReadsRemainExactForEveryColumnWidth()
    {
        WithFile(sizeof(long) * 2, path =>
        {
            var bytes = new byte[sizeof(long) * 2];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, long.MinValue + 17);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(sizeof(long)), long.MaxValue - 23);
            File.WriteAllBytes(path, bytes);
            using var column = new MappedColumn(path, sizeof(long), 2);
            Assert.Equal(long.MinValue + 17, column.ReadInt64(0));
            Assert.Equal(long.MaxValue - 23, column.ReadInt64(1));
        });

        WithFile(sizeof(int), path =>
        {
            var bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, -123_456_789);
            File.WriteAllBytes(path, bytes);
            using var column = new MappedColumn(path, sizeof(int), 1);
            Assert.Equal(-123_456_789, column.ReadInt32(0));
            Assert.Equal(unchecked((uint)-123_456_789), column.ReadUInt32(0));
        });

        WithFile(sizeof(ushort), path =>
        {
            File.WriteAllBytes(path, [0x34, 0x12]);
            using var column = new MappedColumn(path, sizeof(ushort), 1);
            Assert.Equal((ushort)0x1234, column.ReadUInt16(0));
        });

        WithFile(1, path =>
        {
            File.WriteAllBytes(path, [0xa5]);
            using var column = new MappedColumn(path, 1, 1);
            Assert.Equal((byte)0xa5, column.ReadByte(0));
        });
    }

    [Fact]
    public void PayloadReadsDecodeUnalignedUtf8WithoutAnIntermediateArray()
    {
        var payload = Encoding.UTF8.GetBytes("xπ猫z");
        WithFile(payload.Length, path =>
        {
            File.WriteAllBytes(path, payload);
            using var column = new MappedColumn(path, 1, payload.Length);
            Assert.Equal("π猫", column.ReadString(1, payload.Length - 2));
            Assert.Empty(column.ReadBytes(2, 0));
        });
    }

    [Fact]
    public void ExactLengthAndPayloadBoundsRemainTheSafetyBoundary()
    {
        WithFile(7, path =>
        {
            File.WriteAllBytes(path, new byte[7]);
            Assert.Throws<InvalidDataException>(() => new MappedColumn(path, sizeof(int), 2));

            using var column = new MappedColumn(path, 1, 7);
            Assert.Throws<InvalidDataException>(() => column.ReadString(-1, 1));
            Assert.Throws<InvalidDataException>(() => column.ReadString(6, 2));
            Assert.Throws<InvalidDataException>(() => column.ReadString(long.MaxValue, 1));
            Assert.Throws<InvalidDataException>(() => column.ReadString(0, int.MaxValue));
        });
    }

    [Fact]
    public void ScalarBoundsRemainAManagedFailureInReleaseBuilds()
    {
        WithFile(sizeof(long), path =>
        {
            using var column = new MappedColumn(path, sizeof(long), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => column.ReadInt64(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => column.ReadInt64(1));
        });
    }

    [Fact]
    public void DisposalIsIdempotentUnderContentionAndRejectsLaterReads()
    {
        WithFile(1, path =>
        {
            File.WriteAllBytes(path, [42]);
            var column = new MappedColumn(path, 1, 1);
            Parallel.For(0, 64, _ => column.Dispose());
            Assert.Throws<ObjectDisposedException>(() => column.ReadByte(0));
            Assert.Throws<ObjectDisposedException>(() => column.ReadString(0, 1));
        });
    }

    private static void WithFile(int length, Action<string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "column.bin");
        File.WriteAllBytes(path, new byte[length]);
        try
        {
            body(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
