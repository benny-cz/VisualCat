using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Application.UseCases;

public sealed record ImportPreview(
    FormatDetectionResult Detection,
    TimestampPolicy TimestampPolicy,
    InstantUs? FirstInstant,
    InstantUs? LastInstant,
    IReadOnlyDictionary<ParseOutcomeKind, long> OutcomeCounts,
    IReadOnlyList<string> Warnings);

public static class ImportPreviewService
{
    public static async Task<ImportPreview> PreviewAsync(
        ILogSource source,
        TimestampPolicy policy,
        LogcatFormat? formatOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var samples = await source.ProbeAsync(200, cancellationToken).ConfigureAwait(false);
        ImportSourceException.ThrowIfUnsupportedEncoding(samples);
        var detection = formatOverride is { } selected
            ? new FormatDetectionResult(selected, [], 1, [new FormatCandidate(selected, samples.Count, samples.Count * 6, 1)], samples.Count)
            : FormatDetector.Detect(samples);
        var resolver = new TimestampResolver(policy);
        var counts = Enum.GetValues<ParseOutcomeKind>().ToDictionary(static kind => kind, static _ => 0L);
        var warnings = new List<string>();
        InstantUs? first = null;
        InstantUs? last = null;
        long offset = 0;
        long sequence = 0;
        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceLine = new SourceLine(Guid.Empty, sequence++, new RawSpan(offset, sample.Length), sample);
            offset += sample.Length;
            var outcome = LogcatParser.Parse(sourceLine, detection.PrimaryFormat);
            counts[outcome.Kind]++;
            if (outcome.Fields?.Timestamp is not { } timestamp)
            {
                continue;
            }

            var resolved = resolver.Resolve(timestamp);
            if (resolved.Instant is { } instant)
            {
                first = first is null || instant < first ? instant : first;
                last = last is null || instant > last ? instant : last;
            }
        }

        if (detection.Confidence < 0.6)
        {
            warnings.Add("Format confidence is low; review the selected format.");
        }

        if (samples.Count == 0)
        {
            warnings.Add("The source is empty.");
        }

        if (counts[ParseOutcomeKind.UnknownLine] + counts[ParseOutcomeKind.RejectedCandidate] > samples.Count / 10)
        {
            warnings.Add("More than 10% of previewed lines are unknown or malformed.");
        }

        return new ImportPreview(detection, policy, first, last, counts, warnings);
    }
}
