using System.Collections.Immutable;
using System.Globalization;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Presentation;

public enum ExportScopeKind : byte
{
    SelectedCell,
    SelectedRange,
    VisiblePlot,
    AllFiltered,
    AllTimed,
}

/// <summary>All mutable workspace inputs captured before export crosses an async boundary.</summary>
public sealed record FrozenExportRequest(
    Guid SessionId,
    string SessionRoot,
    string SourceTitle,
    FilterSpec AppliedFilter,
    TimeRange Viewport,
    TimeRange? DetailRange,
    LogLevel? DetailLevel,
    TimeRange? ExplicitRange,
    EntryOrder DefaultOrder,
    bool DefaultIncludeUtf8Bom,
    bool CaptureContinues);

/// <summary>One deduplicated export query. Its range and filter are complete and immutable.</summary>
public sealed record ResolvedExportScope(
    ExportScopeKind Kind,
    string Label,
    TimeRange Range,
    FilterSpec Filter,
    string Summary,
    bool Preferred,
    bool IsProvablyEmpty,
    long? TimedRows = null)
{
    public string Identity =>
        $"{Range.StartInclusive.Value}:{Range.EndExclusive.Value}:{(Filter with { TimeRange = null }).Fingerprint()}";

    /// <summary>The label as it reads inside a sentence, with only its opening capital lowered.</summary>
    /// <remarks>
    /// "Exported 4,231 timed rows · selected cell · crash.csv" is one sentence, so the label's
    /// sentence-initial capital goes with it. Only that one: lower-casing the whole label turned
    /// the severity in "Selected cell · Error" into "error", which is not what this product calls
    /// that level anywhere else — the same defect as a dialog titled "Find PIDs" announcing
    /// itself as "Find pids".
    /// </remarks>
    public string SentenceLabel =>
        Label.Length == 0 ? Label : char.ToLowerInvariant(Label[0]) + Label[1..];
}

public sealed record ExportDecision(
    ResolvedExportScope Scope,
    EntryOrder Order,
    bool IncludeUtf8Bom);

/// <summary>Pure scope algebra shared by presentation and deterministic tests.</summary>
public static class ExportScopeResolver
{
    public static IReadOnlyList<ResolvedExportScope> Resolve(
        FrozenExportRequest request,
        TimeRange? sessionRange) =>
        Resolve(request, sessionRange, TimeZoneInfo.Utc);

    /// <summary>Resolves the offered scopes, describing their ranges in one display zone.</summary>
    public static IReadOnlyList<ResolvedExportScope> Resolve(
        FrozenExportRequest request,
        TimeRange? sessionRange,
        TimeZoneInfo displayZone)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(displayZone);
        if (sessionRange is not { } session || session.IsEmpty)
        {
            return [];
        }

        var scopes = new List<ResolvedExportScope>(4);
        if (request.ExplicitRange is { } explicitRange)
        {
            Add(
                scopes,
                Create(
                    ExportScopeKind.SelectedRange,
                    "Selected range",
                    session.Intersect(explicitRange),
                    request.AppliedFilter,
                    preferred: true,
                    level: null,
                    Describe(explicitRange, displayZone)));
            return scopes;
        }

        if (request.DetailRange is { } detail)
        {
            var kind = request.DetailLevel is null ? ExportScopeKind.SelectedRange : ExportScopeKind.SelectedCell;
            var label = request.DetailLevel is { } level ? $"Selected cell · {level}" : "Selected range";
            Add(
                scopes,
                Create(
                    kind,
                    label,
                    session.Intersect(detail),
                    request.AppliedFilter,
                    preferred: true,
                    request.DetailLevel,
                    Describe(detail, displayZone)));
        }

        var visible = Create(
            ExportScopeKind.VisiblePlot,
            "Visible plot range",
            session.Intersect(request.Viewport),
            request.AppliedFilter,
            preferred: scopes.Count == 0,
            level: null,
            Describe(request.Viewport, displayZone));
        var fullFiltered = Create(
            ExportScopeKind.AllFiltered,
            "All timed entries matching filters",
            session,
            request.AppliedFilter,
            preferred: false,
            level: null,
            "The full session time range with every applied filter.");
        var wholeSession = new ResolvedExportScope(
            ExportScopeKind.AllTimed,
            "All timed entries in session",
            session,
            FilterSpec.All,
            "Every parsed entry on the timeline; workspace filters are ignored.",
            Preferred: scopes.Count == 0,
            IsProvablyEmpty: false);
        // A fitted, wholly unfiltered plot is most truthfully the one available
        // "All timed entries in session" choice. With an active filter, however, Visible is
        // the earlier applicable label and the preferred default; Add then deduplicates the
        // later all-filtered description of the same effective request.
        if (!request.AppliedFilter.IsUnconstrained ||
            !string.Equals(visible.Identity, wholeSession.Identity, StringComparison.Ordinal))
        {
            Add(scopes, visible);
        }

        if (!request.AppliedFilter.IsUnconstrained)
        {
            Add(scopes, fullFiltered);
        }

        Add(scopes, wholeSession with { Preferred = scopes.Count == 0 });
        return scopes;
    }

    private static ResolvedExportScope Create(
        ExportScopeKind kind,
        string label,
        TimeRange requestedRange,
        FilterSpec sourceFilter,
        bool preferred,
        LogLevel? level,
        string summary)
    {
        var effectiveRange = sourceFilter.TimeRange is { } timeFilter
            ? requestedRange.Intersect(timeFilter)
            : requestedRange;
        var empty = effectiveRange.IsEmpty;
        var filter = sourceFilter with { TimeRange = effectiveRange };
        if (level is { } selectedLevel)
        {
            if (sourceFilter.IncludedLevels.Count > 0 && !sourceFilter.IncludedLevels.Contains(selectedLevel))
            {
                empty = true;
            }

            filter = filter with
            {
                IncludedLevels = ImmutableHashSet.Create(selectedLevel),
            };
        }

        return new ResolvedExportScope(
            kind,
            label,
            effectiveRange,
            filter,
            summary,
            preferred,
            empty);
    }

    private static void Add(List<ResolvedExportScope> scopes, ResolvedExportScope scope)
    {
        if (scopes.Any(existing => string.Equals(existing.Identity, scope.Identity, StringComparison.Ordinal)))
        {
            return;
        }

        scopes.Add(scope);
    }

    /// <summary>
    /// A range boundary as the plot draws it, in the reader's display zone.
    /// </summary>
    /// <remarks>
    /// The raw <see cref="InstantUs"/> renders as
    /// <c>2025-12-31T23:26:50.0629250+00:00</c> — seven fractional digits, a UTC offset and a
    /// date the reader is not asking about. The plot's own axis already answers "when is
    /// this", so the summary answers it the same way, and states that the end is exclusive.
    /// </remarks>
    private static string Describe(TimeRange range, TimeZoneInfo zone) =>
        $"{FormatBoundary(range.StartInclusive, range.DurationUs, zone)} to " +
        $"{FormatBoundary(range.EndExclusive, range.DurationUs, zone)} (end excluded)";

    private static string FormatBoundary(InstantUs instant, long spanUs, TimeZoneInfo zone)
    {
        var time = TimeZoneInfo.ConvertTime(instant.ToDateTimeOffset(), zone);
        return spanUs switch
        {
            < 1_000_000 => time.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            < 60_000_000 => time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            < 86_400_000_000 => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            _ => time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
    }
}
