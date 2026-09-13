using VisualCat.Core.Parsing;

namespace VisualCat.Application.Coordination;

public enum ImportFailureReason
{
    EmptySource,
    UndetectableFormat,
    UnsupportedEncoding,
    CarriageReturnFramed,
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

    /// <summary>
    /// Refuses a source whose records are separated by carriage returns alone.
    /// </summary>
    /// <remarks>
    /// Checked before detection, and before a format override, because the framing is wrong
    /// whichever format the reader picks. Left unchecked it does not fail: the file is one line,
    /// the head of that line parses, and the import reports a single entry at full confidence
    /// with nothing counted as unaccounted (report §24).
    /// </remarks>
    public static void ThrowIfCarriageReturnFramed(IReadOnlyList<ReadOnlyMemory<byte>> samples)
    {
        if (!CarriageReturnFraming.IsCarriageReturnFramed(samples))
        {
            return;
        }

        throw new ImportSourceException(
            ImportFailureReason.CarriageReturnFramed,
            "This log separates its records with carriage returns rather than line feeds, so the " +
            "whole file is one line and only the first record would be read.");
    }
}
