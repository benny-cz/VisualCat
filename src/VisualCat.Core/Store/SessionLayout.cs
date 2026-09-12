using System.Globalization;

namespace VisualCat.Core.Store;

/// <summary>
/// The complete set of files a stored session is made of.
/// </summary>
/// <remarks>
/// <para>
/// The portable-archive extractor wrote every member of the archive it was given, so a
/// 1.1 MB <c>.vcat.zip</c> declaring a 1 GiB <c>bomb.bin</c> produced a 1.1 GiB directory
/// inside the product's own data root — where it also counted against the session cache. A
/// 200-directory-deep path and a 240-character file name were extracted verbatim for the same
/// reason (finding F-21). None of those are part of a session.
/// </para>
/// <para>
/// The security boundary held throughout — nothing escaped the root, no link or special file was
/// written, modes were correct — so what was missing was a resource bound. Naming the format's
/// own members is the bound that needs no tuning: an entry that is not part of a session is
/// refused whatever its size, depth, or name.
/// </para>
/// </remarks>
public static class SessionLayout
{
    /// <summary>How many segments a path may name, matching the store's own ceiling.</summary>
    private const int MaximumSegmentId = 999_999;

    /// <summary>Files that sit directly in the session root.</summary>
    private static readonly HashSet<string> RootFiles = new(StringComparer.Ordinal)
    {
        "manifest.json",
        "raw.log",
        "view.json",
        TemplateTable.FinalFileName,
    };

    /// <summary>Files under <c>source-order/</c>.</summary>
    private static readonly HashSet<string> SourceOrderFiles = new(StringComparer.Ordinal)
    {
        "records.bin",
        "index.bin",
    };

    /// <summary>
    /// Whether an archive member names a file a session is made of.
    /// </summary>
    /// <param name="entryName">
    /// The member's name as the archive spells it, with forward slashes.
    /// </param>
    public static bool IsKnownMember(string? entryName)
    {
        if (string.IsNullOrEmpty(entryName) || entryName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = entryName.Split('/');
        if (Array.Exists(parts, static part => part.Length == 0 || part is "." or ".."))
        {
            return false;
        }

        return parts switch
        {
            [var file] => RootFiles.Contains(file),
            ["source-order", var file] => SourceOrderFiles.Contains(file),
            ["diagnostics", var file] => IsDiagnosticFile(file),
            [var container, var id, var file] =>
                IsSegmentContainer(container) && IsSegmentId(id) && IsSegmentFile(file),
            [var container, var id, "bitmaps", var file] =>
                IsSegmentContainer(container) && IsSegmentId(id) && IsBitmapFile(file),
            _ => false,
        };
    }

    /// <summary>
    /// The directory segments live in: <c>segments</c> while a capture is live, and
    /// <c>segments-final-NNNNNNNN</c> once it has been compacted, which is the container a saved
    /// or exported session actually carries.
    /// </summary>
    private static bool IsSegmentContainer(string name) =>
        name == "segments" ||
        (name.StartsWith("segments-final-", StringComparison.Ordinal) &&
         name.Length == "segments-final-".Length + 8 &&
         long.TryParse(name["segments-final-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out _));

    /// <summary>A segment directory is six digits, which is how the writer names them.</summary>
    private static bool IsSegmentId(string name) =>
        name.Length == 6 &&
        int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
        id is >= 0 and <= MaximumSegmentId;

    private static bool IsSegmentFile(string name) =>
        name == SegmentChecksums.FileName || SegmentFileContract.ColumnNames.Contains(name, StringComparer.Ordinal);

    private static bool IsBitmapFile(string name) =>
        name.StartsWith("level-", StringComparison.Ordinal) &&
        name.EndsWith(".rbm", StringComparison.Ordinal) &&
        byte.TryParse(name[6..^4], NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsDiagnosticFile(string name) =>
        name.EndsWith(".jsonl", StringComparison.Ordinal) ||
        name.EndsWith(".json", StringComparison.Ordinal) ||
        name.EndsWith(".log", StringComparison.Ordinal);
}
