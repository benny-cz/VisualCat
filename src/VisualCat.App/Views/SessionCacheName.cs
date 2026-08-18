namespace VisualCat.App.Views;

/// <summary>
/// The readable name of a cached session, recovered from its folder name.
/// </summary>
/// <remarks>
/// A temporary session's folder is named <c>{yyyyMMdd-HHmmss}-{name}-{guid:N}.vcat</c> so that
/// two captures of the same file never collide. Both halves of that are for the filesystem, not
/// for a reader: showing the folder name as-is put a timestamp and thirty-two hex digits around
/// the one word — the capture's own name — that says which session this is. The timestamp is
/// shown separately, in the reader's own locale, from the session's modification time.
/// </remarks>
internal static class SessionCacheName
{
    private const int TimestampPrefixLength = 16;
    private const int GuidSuffixLength = 33;

    internal static string Describe(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var name = Path.GetFileNameWithoutExtension(fileName).AsSpan();
        if (name.Length > TimestampPrefixLength &&
            name[8] == '-' &&
            name[15] == '-' &&
            IsAsciiDigits(name[..8]) &&
            IsAsciiDigits(name[9..15]))
        {
            name = name[TimestampPrefixLength..];
        }

        if (name.Length > GuidSuffixLength &&
            name[^GuidSuffixLength] == '-' &&
            IsAsciiHex(name[^(GuidSuffixLength - 1)..]))
        {
            name = name[..^GuidSuffixLength];
        }

        return name.IsEmpty ? fileName : name.ToString();
    }

    /// <summary>
    /// The name a session should be shown under, given what its manifest claims and where it
    /// lives on disk.
    /// </summary>
    /// <remarks>
    /// Two naming paths used to disagree. The recent lists derive a name from the session
    /// folder; opening a session took <c>Descriptor.DisplayName</c> from the manifest. For a
    /// session imported before the display-name fix, that stored value is the materialised
    /// temporary file <see cref="Platform.StorageFileBridge"/> wrote — <c>{guid:N}-{original}</c>
    /// — so the same capture read <c>demo-small</c> in Recent sessions and
    /// <c>b66e69fe00aa4716a57541338b6bc29d-demo-small.txt</c> in its tab, its share archive and
    /// its exported CSV (finding 17). A stored name that still carries a materialisation prefix
    /// is not a name anybody chose, so the prefix is dropped; a stored name that is nothing but
    /// machine identity gives way to the folder entirely.
    /// </remarks>
    internal static string DescribeSession(string path, string? storedDisplayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (string.IsNullOrWhiteSpace(storedDisplayName))
        {
            return Describe(path);
        }

        var stored = StripMaterializationPrefix(storedDisplayName.Trim());
        return stored.Length == 0 || LooksMachineGenerated(stored) ? Describe(path) : stored;
    }

    /// <summary>Number of characters in <c>{Guid:N}-</c>.</summary>
    private const int MaterializationPrefixLength = 33;

    private static string StripMaterializationPrefix(string value) =>
        value.Length > MaterializationPrefixLength &&
        value[MaterializationPrefixLength - 1] == '-' &&
        IsAsciiHex(value.AsSpan(0, MaterializationPrefixLength - 1))
            ? value[MaterializationPrefixLength..]
            : value;

    /// <summary>
    /// Whether what is left after the prefix is still an identifier rather than a name: a bare
    /// guid, or a name that is only a run of hex digits.
    /// </summary>
    private static bool LooksMachineGenerated(string value)
    {
        var stem = Path.GetFileNameWithoutExtension(value.AsSpan());
        return stem.Length >= 32 && IsAsciiHex(stem);
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
