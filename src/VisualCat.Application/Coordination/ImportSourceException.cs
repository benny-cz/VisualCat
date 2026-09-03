namespace VisualCat.Application.Coordination;

public enum ImportFailureReason
{
    EmptySource,
    UndetectableFormat,
    UnsupportedEncoding,
}

/// <summary>A source-content failure for which the import UI can offer a specific remedy.</summary>
public sealed class ImportSourceException : Exception
{
    public ImportSourceException(ImportFailureReason reason, string message)
        : base(message) => Reason = reason;

    public ImportFailureReason Reason { get; }

    public static void ThrowIfUnsupportedEncoding(IReadOnlyList<ReadOnlyMemory<byte>> samples)
    {
        if (samples.Count == 0 || samples[0].IsEmpty)
        {
            return;
        }

        var bytes = samples[0].Span;
        var unsupported = bytes.Length >= 2 &&
                          (bytes[0] == 0xFF && bytes[1] == 0xFE ||
                           bytes[0] == 0xFE && bytes[1] == 0xFF) ||
                          bytes.Length >= 4 &&
                          bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF;
        if (unsupported)
        {
            throw new ImportSourceException(
                ImportFailureReason.UnsupportedEncoding,
                "This log uses UTF-16 or UTF-32 text, which VisualCat cannot index without changing its byte offsets.");
        }
    }
}
