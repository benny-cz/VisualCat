using System.Text;
using VisualCat.Domain.Entries;

namespace VisualCat.Core.Parsing;

public sealed record FormatCandidate(LogcatFormat Format, int Matched, int ValidFields, double Score);

public sealed record FormatDetectionResult(
    LogcatFormat PrimaryFormat,
    IReadOnlyList<string> Modifiers,
    double Confidence,
    IReadOnlyList<FormatCandidate> Candidates,
    int UsefulLines);

public sealed class FormatDetector
{
    public static FormatDetectionResult Detect(IEnumerable<ReadOnlyMemory<byte>> samples)
    {
        var totals = Enum.GetValues<LogcatFormat>()
            .Where(static format => format != LogcatFormat.Unknown)
            .ToDictionary(static format => format, static _ => (Matched: 0, Valid: 0));
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var useful = 0;
        var bracketed = 0;

        foreach (var memory in samples)
        {
            if (useful >= 200)
            {
                break;
            }

            var line = TrimLine(memory.Span);
            if (line.IsEmpty || LogcatParser.TryReadBufferDivider(line, out _))
            {
                continue;
            }

            useful++;
            if (line[0] == (byte)'[' && line[^1] == (byte)']')
            {
                bracketed++;
            }

            foreach (var format in totals.Keys.ToArray())
            {
                var score = LogcatParser.Probe(line, format);
                if (score > 0)
                {
                    var value = totals[format];
                    totals[format] = (value.Matched + 1, value.Valid + score);
                }
            }

            var text = Encoding.UTF8.GetString(line);
            var firstSpace = text.IndexOf(' ');
            if (firstSpace > 0)
            {
                var date = text.AsSpan(0, firstSpace);
                if (date.Length >= 10 && date.Count('-') >= 2)
                {
                    modifiers.Add("year");
                }
            }

            var dot = text.IndexOf('.');
            if (dot >= 0)
            {
                var digits = 0;
                for (var i = dot + 1; i < text.Length && char.IsAsciiDigit(text[i]); i++)
                {
                    digits++;
                }

                if (digits >= 6)
                {
                    modifiers.Add("usec");
                }
            }
        }

        var candidates = totals
            .Select(pair =>
            {
                var score = useful == 0 || pair.Value.Matched == 0
                    ? 0
                    : Coverage(pair.Key, pair.Value.Matched, bracketed, useful) *
                      Math.Min(1d, pair.Value.Valid / (pair.Value.Matched * (double)BestScore(pair.Key)));
                return new FormatCandidate(pair.Key, pair.Value.Matched, pair.Value.Valid, score);
            })
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Format)
            .ToArray();

        var primary = candidates.FirstOrDefault();
        var confidence = primary?.Score ?? 0;
        var runnerUp = candidates.Skip(1).FirstOrDefault()?.Score ?? 0;
        if (confidence > 0)
        {
            confidence = Math.Clamp(confidence * (0.75 + Math.Min(0.25, confidence - runnerUp)), 0, 1);
        }

        return new FormatDetectionResult(
            confidence >= 0.15 ? primary!.Format : LogcatFormat.Unknown,
            modifiers.Order(StringComparer.Ordinal).ToArray(),
            confidence,
            candidates,
            useful);
    }

    /// <summary>
    /// The share of the lines a format is responsible for that it actually matched.
    /// </summary>
    /// <remarks>
    /// Every format but <c>long</c> prints one record per line, so the whole sample is its
    /// responsibility. A <c>-v long</c> record is a bracketed header, its message, and a blank
    /// separator, so roughly half the non-blank lines of a perfectly healthy file can never
    /// match a header — which capped the format at ~0.5 however clean the capture was, put an
    /// unmodified device dump permanently under the 0.6 review threshold, and made the collapse
    /// caused by F-01 indistinguishable from the format's own shape. Score it against the lines
    /// it is claiming, and keep a presence factor so one stray bracketed line in an unrelated
    /// file cannot carry it.
    /// </remarks>
    private static double Coverage(LogcatFormat format, int matched, int bracketed, int useful)
    {
        if (format != LogcatFormat.LongFormat)
        {
            return matched / (double)useful;
        }

        if (bracketed == 0)
        {
            return 0;
        }

        var presence = Math.Min(1d, bracketed * 2d / useful);
        return matched / (double)bracketed * presence;
    }

    /// <summary>
    /// The highest <see cref="LogcatParser.Probe"/> score the format can reach when every field
    /// it carries is present, so confidence measures how certainly a line is this format rather
    /// than how many fields the format happens to define. <c>brief</c> has no timestamp at all,
    /// so a flawless brief capture scored 4/6 and sat barely above the review threshold.
    /// </summary>
    private static int BestScore(LogcatFormat format) => format == LogcatFormat.Brief ? 4 : 6;

    private static ReadOnlySpan<byte> TrimLine(ReadOnlySpan<byte> line)
    {
        if (!line.IsEmpty && line[^1] == (byte)'\n')
        {
            line = line[..^1];
        }

        if (!line.IsEmpty && line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        return line;
    }
}
