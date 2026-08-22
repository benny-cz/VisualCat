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
                    : (pair.Value.Matched / (double)useful) * Math.Min(1d, pair.Value.Valid / (pair.Value.Matched * 6d));
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
