using VisualCat.Domain.Entries;

namespace VisualCat.Core.Parsing;

/// <summary>
/// Whether a source separates its records with bare carriage returns rather than line feeds.
/// </summary>
/// <remarks>
/// No logcat implementation writes CR-only line endings; a file with them has been through a
/// conversion accident. It matters because every reader here frames on LF, so such a file arrives
/// as one enormous line — and when the head of that line happens to parse, the import *succeeds*
/// with one entry, confidence 1.000 and every counter reporting full accounting, while the rest of
/// the file sits inside that entry's message. A 40-record file became one record and said nothing
/// was missing. Refusing and naming the cause is the honest answer; framing on CR instead is not,
/// because a bare carriage return inside a message is legitimate and does occur (report §24).
/// </remarks>
public static class CarriageReturnFraming
{
    /// <summary>How many CR-separated logcat lines make this a framing problem, not a stray byte.</summary>
    private const int RecognisableLinesRequired = 3;

    private static readonly LogcatFormat[] Formats =
        [.. Enum.GetValues<LogcatFormat>().Where(static format => format != LogcatFormat.Unknown)];

    /// <summary>
    /// Whether the probed prefix is one carriage-return-framed line rather than real content.
    /// </summary>
    public static bool IsCarriageReturnFramed(IReadOnlyList<ReadOnlyMemory<byte>> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Exactly one sample means the probed prefix held no line feed at all. Anything more and
        // the source is LF-framed, so a carriage return inside one message cannot trip this — and
        // a genuinely enormous single line only trips it if three of its CR-separated parts read
        // as logcat records, which prose does not.
        if (samples.Count != 1)
        {
            return false;
        }

        var recognised = 0;
        var remaining = samples[0].Span;
        while (!remaining.IsEmpty)
        {
            var carriageReturn = remaining.IndexOf((byte)'\r');
            var line = carriageReturn < 0 ? remaining : remaining[..carriageReturn];
            if (IsLogcatLine(line) && ++recognised >= RecognisableLinesRequired)
            {
                return true;
            }

            if (carriageReturn < 0)
            {
                break;
            }

            remaining = remaining[(carriageReturn + 1)..];
        }

        return false;
    }

    private static bool IsLogcatLine(ReadOnlySpan<byte> line)
    {
        if (line.IsEmpty)
        {
            return false;
        }

        if (LogcatParser.TryReadBufferDivider(line, out _))
        {
            return true;
        }

        foreach (var format in Formats)
        {
            if (LogcatParser.Probe(line, format) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
