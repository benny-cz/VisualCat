using VisualCat.Domain.Entries;

namespace VisualCat.Core.Store;

internal static class SegmentFileContract
{
    private static readonly string[] Columns =
    [
        "timestamp.bin", "sequence.bin", "raw-offset.bin", "raw-length.bin", "pid.bin", "tid.bin",
        "level.bin", "tag.bin", "template.bin", "flags.bin", "provenance.bin", "confidence.bin",
        "format.bin", "buffer.bin", "message-offset.bin", "message-length.bin",
        "original-offset.bin", "original-length.bin", "payload.bin",
    ];

    public static IReadOnlyList<string> ColumnNames => Columns;

    public static IReadOnlyList<string> RequiredRelativePaths() =>
        [.. Columns, .. LogLevels.StorageOrder.ToArray().Select(static severity => $"bitmaps/level-{(byte)severity}.rbm")];
}
