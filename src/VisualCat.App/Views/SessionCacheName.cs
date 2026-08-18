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
