using System.IO.MemoryMappedFiles;

namespace VisualCat.Core.Store;

internal sealed class MappedColumn : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;

    public MappedColumn(string path, int elementSize, long expectedCount)
    {
        var length = new FileInfo(path).Length;
        if (length != checked((long)elementSize * expectedCount))
        {
            throw new InvalidDataException($"Column length mismatch: {path}");
        }

        Length = expectedCount;
        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
    }

    public long Length { get; }

    // Offsets widen to long before scaling: a segment large enough to make
    // index * sizeof(long) exceed int range would otherwise wrap to a negative
    // offset and read the wrong element rather than failing.
    public long ReadInt64(int index) => _view.ReadInt64((long)index * sizeof(long));
    public int ReadInt32(int index) => _view.ReadInt32((long)index * sizeof(int));
    public uint ReadUInt32(int index) => _view.ReadUInt32((long)index * sizeof(uint));
    public ushort ReadUInt16(int index) => _view.ReadUInt16((long)index * sizeof(ushort));
    public byte ReadByte(int index) => _view.ReadByte(index);

    public byte[] ReadBytes(long offset, int length)
    {
        // Written as a subtraction so a corrupted offset near long.MaxValue cannot
        // overflow the bound check into passing (§18.5: validate all mapped offsets).
        if (offset < 0 || length < 0 || offset > _view.Capacity - length)
        {
            throw new InvalidDataException("Payload span is outside the mapped column.");
        }

        var result = new byte[length];
        _view.ReadArray(offset, result, 0, length);
        return result;
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();
    }
}
