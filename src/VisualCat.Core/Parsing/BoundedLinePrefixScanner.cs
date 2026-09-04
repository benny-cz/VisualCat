namespace VisualCat.Core.Parsing;

/// <summary>
/// Walks arbitrary byte chunks one line at a time while retaining only the bounded prefix
/// a caller needs. The scan jumps between newlines; long message bodies are never copied.
/// </summary>
internal sealed class BoundedLinePrefixScanner
{
    private readonly byte[] _prefix;
    private int _length;

    public BoundedLinePrefixScanner(int maximumPrefixLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPrefixLength);
        _prefix = new byte[maximumPrefixLength];
    }

    public void Append(ReadOnlySpan<byte> bytes, LinePrefixHandler observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        while (!bytes.IsEmpty)
        {
            var newline = bytes.IndexOf((byte)'\n');
            var part = newline < 0 ? bytes : bytes[..newline];
            AppendPrefix(part);
            if (newline < 0)
            {
                return;
            }

            observe(_prefix.AsSpan(0, _length));
            _length = 0;
            bytes = bytes[(newline + 1)..];
        }
    }

    public void Clear() => _length = 0;

    private void AppendPrefix(ReadOnlySpan<byte> bytes)
    {
        var room = _prefix.Length - _length;
        while (room > 0 && !bytes.IsEmpty)
        {
            var carriageReturn = bytes.IndexOf((byte)'\r');
            var clean = carriageReturn < 0 ? bytes : bytes[..carriageReturn];
            var copy = Math.Min(room, clean.Length);
            clean[..copy].CopyTo(_prefix.AsSpan(_length));
            _length += copy;
            room -= copy;
            if (copy < clean.Length || carriageReturn < 0)
            {
                return;
            }

            bytes = bytes[(carriageReturn + 1)..];
        }
    }
}

internal delegate void LinePrefixHandler(ReadOnlySpan<byte> prefix);
