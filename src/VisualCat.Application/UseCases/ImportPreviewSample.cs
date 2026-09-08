using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Application.UseCases;

/// <summary>One owned, bounded sample that can be evaluated repeatedly without rereading.</summary>
public sealed class ImportPreviewSample : IDisposable
{
    private IReadOnlyList<ReadOnlyMemory<byte>> _completeLines;
    private IReadOnlyList<ReadOnlyMemory<byte>> _detectorLines;
    private int _disposed;

    internal ImportPreviewSample(BoundedSourceProbe probe)
    {
        _completeLines = probe.CompleteLines;
        _detectorLines = probe.DetectorLines;
        SourceBytesObserved = probe.SourceBytesObserved;
        ReachedEndOfSource = probe.ReachedEndOfSource;
        Limit = probe.Limit;
        RetainedBytes = probe.RetainedBytes;
        HasClippedLine = probe.HasClippedLine;
    }

    public IReadOnlyList<ReadOnlyMemory<byte>> CompleteLines => _completeLines;
    public IReadOnlyList<ReadOnlyMemory<byte>> DetectorLines => _detectorLines;
    public bool SourceBytesObserved { get; }
    public bool ReachedEndOfSource { get; }
    public SourceProbeLimit Limit { get; }
    public int RetainedBytes { get; }
    public bool HasClippedLine { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _completeLines = [];
            _detectorLines = [];
        }
    }
}

public enum ImportPreparationDecision : byte
{
    QuickImport,
    Review,
    EmptySource,
}

/// <summary>Pure policy for deciding whether an acquired finite sample needs review.</summary>
public static class ImportPreparationPolicy
{
    public static ImportPreparationDecision Decide(
        ImportPreviewSample sample,
        ImportPreview preview,
        bool alwaysReview)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(preview);
        if (!sample.SourceBytesObserved)
        {
            return ImportPreparationDecision.EmptySource;
        }

        if (alwaysReview || sample.CompleteLines.Count == 0 || preview.Detection.Confidence < 0.6)
        {
            return ImportPreparationDecision.Review;
        }

        return ImportPreparationDecision.QuickImport;
    }
}

public static class ImportSampleService
{
    public const int MaximumLines = 200;
    public const int MaximumRetainedBytes = 4 * 1024 * 1024;
    public const int MaximumLineBytes = 1024 * 1024;

    public static async Task<ImportPreviewSample> AcquireAsync(
        ILogSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        BoundedSourceProbe probe;
        if (source is IBoundedProbeSource bounded)
        {
            probe = await bounded.ProbeAsync(
                MaximumLines,
                MaximumRetainedBytes,
                MaximumLineBytes,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var legacy = await source.ProbeAsync(MaximumLines, cancellationToken).ConfigureAwait(false);
            probe = BoundLegacy(legacy, source.Metadata.Length);
        }

        ImportSourceException.ThrowIfUnsupportedEncoding(probe.DetectorLines);
        return new ImportPreviewSample(probe);
    }

    public static ImportPreview Evaluate(
        ImportPreviewSample sample,
        TimestampPolicy policy,
        LogcatFormat? formatOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(policy);
        var detection = FormatDetector.Detect(sample.DetectorLines);
        var effectiveFormat = formatOverride ?? detection.PrimaryFormat;
        var resolver = new TimestampResolver(policy);
        var counts = Enum.GetValues<ParseOutcomeKind>().ToDictionary(static kind => kind, static _ => 0L);
        var warnings = new List<string>();
        InstantUs? first = null;
        InstantUs? last = null;
        long offset = 0;
        long sequence = 0;
        foreach (var line in sample.CompleteLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceLine = new SourceLine(Guid.Empty, sequence++, new RawSpan(offset, line.Length), line);
            offset += line.Length;
            var outcome = LogcatParser.Parse(sourceLine, effectiveFormat);
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
            warnings.Add("Sample format confidence is low. Review the format before importing.");
        }

        if (!sample.SourceBytesObserved)
        {
            warnings.Add("The source is empty.");
        }
        else if (sample.CompleteLines.Count == 0)
        {
            warnings.Add("Preview is limited before the first complete line. Review the format before importing.");
        }

        if (sample.CompleteLines.Count > 0 &&
            counts[ParseOutcomeKind.UnknownLine] + counts[ParseOutcomeKind.RejectedCandidate] >
            sample.CompleteLines.Count / 10)
        {
            warnings.Add("More than 10% of complete preview lines are unknown or malformed.");
        }

        warnings.AddRange(sample.Limit switch
        {
            SourceProbeLimit.AggregateBytes => ["Preview sample limited to 4 MiB."],
            SourceProbeLimit.ClippedLine => ["Preview stopped at a long line."],
            _ => [],
        });

        return new ImportPreview(
            detection,
            policy,
            first,
            last,
            counts,
            warnings,
            sample.CompleteLines.Count,
            sample.RetainedBytes,
            sample.Limit,
            sample.SourceBytesObserved,
            formatOverride,
            effectiveFormat);
    }

    private static BoundedSourceProbe BoundLegacy(
        IReadOnlyList<ReadOnlyMemory<byte>> lines,
        long? declaredLength)
    {
        var complete = new List<ReadOnlyMemory<byte>>(Math.Min(MaximumLines, lines.Count));
        var detector = new List<ReadOnlyMemory<byte>>(Math.Min(MaximumLines, lines.Count));
        var retained = 0;
        var sawBytes = declaredLength is > 0;
        foreach (var value in lines.Take(MaximumLines))
        {
            if (!value.IsEmpty)
            {
                sawBytes = true;
            }

            var available = MaximumRetainedBytes - retained;
            if (available <= 0)
            {
                return new BoundedSourceProbe(
                    complete, detector, sawBytes, false, SourceProbeLimit.AggregateBytes, retained, false);
            }

            if (value.Length > MaximumLineBytes || value.Length > available)
            {
                var keep = Math.Min(available, MaximumLineBytes);
                if (keep > 0)
                {
                    var prefix = value[..keep].ToArray();
                    detector.Add(prefix);
                    retained += prefix.Length;
                }

                var reason = value.Length > MaximumLineBytes
                    ? SourceProbeLimit.ClippedLine
                    : SourceProbeLimit.AggregateBytes;
                return new BoundedSourceProbe(complete, detector, sawBytes, false, reason, retained, true);
            }

            var line = value.ToArray();
            complete.Add(line);
            detector.Add(line);
            retained += line.Length;
        }

        var lineLimit = lines.Count >= MaximumLines;
        return new BoundedSourceProbe(
            complete,
            detector,
            sawBytes,
            ReachedEndOfSource: !lineLimit,
            lineLimit ? SourceProbeLimit.LineCount : SourceProbeLimit.None,
            retained,
            HasClippedLine: false);
    }
}
