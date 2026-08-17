using System.Text.Json;

namespace VisualCat.Core.Store;

/// <summary>
/// The SHA-256 digest of every file in one segment, stored beside the segment instead of
/// inside the session manifest.
/// </summary>
/// <remarks>
/// A segment carries twenty-six digests. Held in the manifest they dominated it — about
/// 7 KB per segment — and the manifest is rewritten in full for every published snapshot,
/// so a live capture kept rewriting a file that grew with the length of the capture, many
/// times a minute. A three-hour capture wrote gigabytes of manifest for a session holding
/// megabytes of log, and every reader refresh had to parse the whole thing. Digests are
/// read only by the verifier and by session save, both of which visit one segment at a
/// time, so they live with the immutable segment they describe and are written once.
/// Sessions written before this change still carry them in the manifest and are read from
/// there (§11.7, §16.3).
/// </remarks>
internal static class SegmentChecksums
{
    public const string FileName = "checksums.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void Write(string segmentDirectory, IReadOnlyDictionary<string, string> checksums)
    {
        var path = Path.Combine(segmentDirectory, FileName);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.SequentialScan);
        JsonSerializer.Serialize(stream, checksums, Options);
    }

    /// <summary>
    /// Reads the digests for one segment, preferring those the manifest carries so a
    /// session written by an earlier version still verifies.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(SegmentManifest manifest, string segmentDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Checksums is { Count: > 0 } embedded)
        {
            return embedded;
        }

        var path = Path.Combine(segmentDirectory, FileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.SequentialScan);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, Options)
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
