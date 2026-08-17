using VisualCat.Domain.Entries;

namespace VisualCat.Core.Store;

internal static class SegmentFileContract
{
    /// <summary>
    /// Identifies one column file inside a segment directory. Values double as indices
    /// into <see cref="Specifications"/> and into a segment's mapping table, so the
    /// declaration order here is the on-disk column order and must not be reordered.
    /// </summary>
    internal enum Column
    {
        Timestamp = 0,
        Sequence,
        RawOffset,
        RawLength,
        Pid,
        Tid,
        Level,
        Tag,
        Template,
        Flags,
        Provenance,
        Confidence,
        Format,
        Buffer,
        MessageOffset,
        MessageLength,
        OriginalOffset,
        OriginalLength,
        Payload,
    }

    private static readonly (string Name, int ElementSize)[] Specifications =
    [
        ("timestamp.bin", 8),
        ("sequence.bin", 8),
        ("raw-offset.bin", 8),
        ("raw-length.bin", 4),
        ("pid.bin", 4),
        ("tid.bin", 4),
        ("level.bin", 1),
        ("tag.bin", 4),
        ("template.bin", 4),
        ("flags.bin", 2),
        ("provenance.bin", 1),
        ("confidence.bin", 1),
        ("format.bin", 1),
        ("buffer.bin", 4),
        ("message-offset.bin", 8),
        ("message-length.bin", 4),
        ("original-offset.bin", 8),
        ("original-length.bin", 4),
        // Variable length: sized by the file itself rather than by the entry count.
        ("payload.bin", 1),
    ];

    public static int ColumnCount => Specifications.Length;

    public static string NameOf(Column column) => Specifications[(int)column].Name;

    public static int ElementSizeOf(Column column) => Specifications[(int)column].ElementSize;

    public static IReadOnlyList<string> ColumnNames { get; } =
        Specifications.Select(static specification => specification.Name).ToArray();

    public static IReadOnlyList<string> RequiredRelativePaths() =>
        [.. ColumnNames, .. LogLevels.StorageOrder.ToArray().Select(static severity => $"bitmaps/level-{(byte)severity}.rbm")];
}
