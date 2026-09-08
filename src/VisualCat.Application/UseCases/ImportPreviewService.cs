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
    IReadOnlyList<string> Warnings,
    int CompleteLineCount = 0,
    int RetainedBytes = 0,
    SourceProbeLimit Limit = SourceProbeLimit.None,
    bool SourceBytesObserved = false,
    LogcatFormat? FormatOverride = null,
    LogcatFormat EffectiveFormat = LogcatFormat.Unknown);

public static class ImportPreviewService
{
    public static async Task<ImportPreview> PreviewAsync(
        ILogSource source,
        TimestampPolicy policy,
        LogcatFormat? formatOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var sample = await ImportSampleService.AcquireAsync(source, cancellationToken).ConfigureAwait(false);
        return ImportSampleService.Evaluate(sample, policy, formatOverride, cancellationToken);
    }
}
