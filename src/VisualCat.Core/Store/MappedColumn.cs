using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;

namespace VisualCat.Core.Store;

/// <summary>
/// One column file, mapped read-only and read through a pointer held for the object's
/// lifetime rather than through a view accessor.
/// </summary>
/// <remarks>
/// <para>
/// <c>MemoryMappedViewAccessor</c> takes and releases a ref-count on the safe handle for
/// <em>every element it reads</em>. Holding the pointer once instead measured 1.74x on a
/// million-entry literal search and 1.73x on regex, with 38% less allocation, because the
/// payload column can then decode UTF-8 straight out of the mapping with no intermediate
/// <c>byte[]</c> (see docs/PERFORMANCE.md).
/// </para>
/// <para>
/// <b>The safety boundary is layered.</b> First, the
/// constructor refuses a column whose file length is not exactly
/// <c>elementSize * expectedCount</c>. Scalar reads also retain one predictable range check:
/// query indices normally come from <see cref="SegmentSnapshot.Count"/>, but those readers
/// are public and an invalid caller must get a managed exception rather than an unsafe access.
/// Second, the offset-taking
/// <see cref="ReadBytes"/>/<see cref="ReadString"/> read <em>untrusted</em> payload offsets
/// out of column data and therefore keep an explicit span check.
/// </para>
/// <para>
/// <b>Lifetime is owned by <see cref="SegmentSnapshot"/>'s reference count, not by this
/// class.</b> Columns are opened lazily and closed only when the last snapshot holding the
/// segment releases it, so a query cannot outlive its own mappings: it reads through a
/// <see cref="SessionSnapshot"/> it holds, which holds the segment reference. The
/// <see cref="ObjectDisposedException"/> guard on each read is defence in depth against a
/// caller that breaks that contract — it converts the common ordering mistake into a managed
/// exception. It is deliberately <em>not</em> a barrier against a concurrent disposal racing
/// an in-flight read; nothing inside this class could make that safe, and the reference count
/// is what prevents it.
/// </para>
/// </remarks>
internal sealed unsafe class MappedColumn : IDisposable
{
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly long _capacity;
    private byte* _pointer;
    private int _disposed;

    public MappedColumn(string path, int elementSize, long expectedCount)
    {
        var length = new FileInfo(path).Length;
        if (length != checked((long)elementSize * expectedCount))
        {
            throw new InvalidDataException($"Column length mismatch: {path}");
        }

        Length = expectedCount;
        MemoryMappedFile? file = null;
        MemoryMappedViewAccessor? view = null;
        byte* pointer = null;
        var pointerAcquired = false;
        try
        {
            file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            pointerAcquired = true;

            _file = file;
            _view = view;
            // View capacity can be rounded to the platform's page size. The file length is
            // the format boundary; bytes in the rounded tail are not column data.
            _capacity = length;
            _pointer = pointer + view.PointerOffset;
        }
        catch
        {
            if (pointerAcquired)
            {
                view!.SafeMemoryMappedViewHandle.ReleasePointer();
            }

            view?.Dispose();
            file?.Dispose();
            throw;
        }
    }

    public long Length { get; }

    // Offsets widen to long before scaling: a segment large enough to make
    // index * sizeof(long) exceed int range would otherwise wrap to a negative
    // offset and read the wrong element rather than failing.
    public long ReadInt64(int index)
    {
        EnsureReadable(index);
        return Unsafe.ReadUnaligned<long>(_pointer + (long)index * sizeof(long));
    }

    public int ReadInt32(int index)
    {
        EnsureReadable(index);
        return Unsafe.ReadUnaligned<int>(_pointer + (long)index * sizeof(int));
    }

    public uint ReadUInt32(int index)
    {
        EnsureReadable(index);
        return Unsafe.ReadUnaligned<uint>(_pointer + (long)index * sizeof(uint));
    }

    public ushort ReadUInt16(int index)
    {
        EnsureReadable(index);
        return Unsafe.ReadUnaligned<ushort>(_pointer + (long)index * sizeof(ushort));
    }

    public byte ReadByte(int index)
    {
        EnsureReadable(index);
        return *(_pointer + index);
    }

    public byte[] ReadBytes(long offset, int length)
    {
        ValidateSpan(offset, length);
        return new ReadOnlySpan<byte>(_pointer + offset, length).ToArray();
    }

    public string ReadString(long offset, int length)
    {
        ValidateSpan(offset, length);
        return Encoding.UTF8.GetString(_pointer + offset, length);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }
        finally
        {
            try
            {
                _view.Dispose();
            }
            finally
            {
                _file.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureReadable(int index)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if ((long)(uint)index >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateSpan(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // Written as a subtraction so a corrupted offset near long.MaxValue cannot
        // overflow the bound check into passing (§18.5: validate all mapped offsets).
        if (offset < 0 || length < 0 || length > _capacity || offset > _capacity - length)
        {
            throw new InvalidDataException("Payload span is outside the mapped column.");
        }
    }
}
