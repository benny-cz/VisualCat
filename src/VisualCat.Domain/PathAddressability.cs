using System.Text;

namespace VisualCat.Domain;

/// <summary>
/// Whether a path this process was handed can be used to open the file it names.
/// </summary>
/// <remarks>
/// <para>
/// A Linux path is a sequence of bytes, and a name that is not valid UTF-8 — <c>latin1-name-\xE9.bin</c>
/// is the canonical example — is perfectly legal. .NET decodes process arguments and directory
/// entries as UTF-8 and replaces every invalid byte with U+FFFD, which does not encode back to the
/// byte it came from, so such a file cannot be opened by a <see cref="string"/> path at all.
/// </para>
/// <para>
/// The product reported that as <c>Log source was not found.</c> — a wrong diagnosis that sends
/// the reader looking for a missing file that is sitting right there and is readable by every
/// other tool (finding F-06). This is how the two cases are told apart, so each can say something
/// true.
/// </para>
/// </remarks>
public static class PathAddressability
{
    /// <summary>The character .NET substitutes for a byte sequence it could not decode.</summary>
    private const char ReplacementCharacter = '\uFFFD';

    /// <summary>
    /// Whether every character of the path survived decoding, so the path can be encoded back to
    /// the bytes the filesystem holds.
    /// </summary>
    public static bool IsAddressable(string? path) =>
        string.IsNullOrEmpty(path) ||
        (!path.Contains(ReplacementCharacter, StringComparison.Ordinal) && !ContainsLoneSurrogate(path));

    /// <summary>
    /// A message naming what is wrong with the path and what the reader can do about it,
    /// including the bytes as far as they are known.
    /// </summary>
    /// <param name="path">The path as this process received it.</param>
    public static string Explain(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var name = System.IO.Path.GetFileName(path);
        var directory = System.IO.Path.GetDirectoryName(path);
        var message = new StringBuilder();
        message.Append("This path is not valid UTF-8, so VisualCat cannot address the file it names: ");
        message.Append(Describe(name.Length == 0 ? path : name));
        message.Append('.');

        if (!string.IsNullOrEmpty(directory))
        {
            message.Append(" The file may well be there and readable — the name simply cannot survive ");
            message.Append("the trip through a text argument. Rename it to a UTF-8 name and try again, ");
            message.Append("for example: cd ");
            message.Append('"');
            message.Append(directory);
            message.Append('"');
            message.Append(" && mv -- *.bin renamed.bin");
        }

        return message.ToString();
    }

    /// <summary>Renders the undecodable positions visibly, so the reader can see which they are.</summary>
    private static string Describe(string name)
    {
        var described = new StringBuilder(name.Length + 16);
        foreach (var character in name)
        {
            if (character == ReplacementCharacter)
            {
                described.Append("<undecodable byte>");
            }
            else if (char.IsSurrogate(character))
            {
                described.Append("<byte 0x").Append(((int)character & 0xFF).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                described.Append(character);
            }
        }

        return described.ToString();
    }

    private static bool ContainsLoneSurrogate(string path)
    {
        for (var i = 0; i < path.Length; i++)
        {
            if (!char.IsSurrogate(path[i]))
            {
                continue;
            }

            if (char.IsHighSurrogate(path[i]) && i + 1 < path.Length && char.IsLowSurrogate(path[i + 1]))
            {
                i++;
                continue;
            }

            return true;
        }

        return false;
    }
}
