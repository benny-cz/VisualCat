using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;
using VisualCat.Core.Query;
using VisualCat.Domain;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

/// <summary>
/// The export writes the scope it disclosed, counted against a real session.
/// </summary>
/// <remarks>
/// <para>
/// The resolver's own unit tests prove the range algebra. This proves the other half: that
/// the CSV query, run with the resolved range and filter, returns exactly the records the
/// label promised. The fixture's duplicate timestamps and its record on the filter's
/// exclusive end are the two places the two halves could disagree without either looking
/// wrong on its own.
/// </para>
/// <para>
/// Every expectation below is the source sequence of a record, not a count, so a scope that
/// returns the right number of the wrong rows still fails.
/// </para>
/// </remarks>
public sealed class ExportScopeOracleTests
{
    /// <summary>Eight timed records; seconds 2 and 2 are deliberately the same instant.</summary>
    private const string OracleLog =
        "01-01 00:00:00.000000   100   101 E A              : needle\n" +
        "01-01 00:00:01.000000   100   101 W A              : needle\n" +
        "01-01 00:00:02.000000   100   101 E A              : needle\n" +
        "01-01 00:00:02.000000   100   101 W A              : needle\n" +
        "01-01 00:00:03.000000   100   101 E B              : needle\n" +
        "01-01 00:00:04.000000   100   101 I A              : ordinary\n" +
        "01-01 00:00:05.000000   100   101 E A              : needle\n" +
        "01-01 00:00:06.000000   100   101 F A              : needle\n";

    [AvaloniaFact]
    public async Task EachOfferedScopeWritesExactlyTheRecordsItsLabelPromises()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(OracleLog);
        var snapshot = fixture.Tab.Snapshot!;
        var session = snapshot.TimedRange!.Value;
        var origin = session.StartInclusive.Value;
        var filter = FilterSpec.All with
        {
            Search = new TextSearchSpec("needle"),
            IncludedTags = ImmutableHashSet.Create("A"),
            TimeRange = At(origin, 1, 6),
        };

        var scopes = ExportScopeResolver.Resolve(
            new FrozenExportRequest(
                snapshot.SessionId,
                fixture.Tab.SessionPath,
                fixture.Tab.Title,
                filter,
                At(origin, 2, 5),
                At(origin, 2, 3),
                LogLevel.Error,
                ExplicitRange: null,
                EntryOrder.SourceSequence,
                DefaultIncludeUtf8Bom: false,
                CaptureContinues: false),
            session);

        Assert.Equal(
            [
                ExportScopeKind.SelectedCell,
                ExportScopeKind.VisiblePlot,
                ExportScopeKind.AllFiltered,
                ExportScopeKind.AllTimed,
            ],
            scopes.Select(static scope => scope.Kind));

        // The selected cell narrows severity as well as time; the visible plot admits every
        // level the filter admits over the same window.
        Assert.Equal([2], Sequences(fixture, scopes[0]));
        Assert.Equal([2, 3], Sequences(fixture, scopes[1]));

        // Second 6 is outside the filter's exclusive end, and second 0 is before its start.
        Assert.Equal([1, 2, 3, 6], Sequences(fixture, scopes[2]));

        // The whole session ignores every filter dimension, including the search.
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], Sequences(fixture, scopes[3]));
    }

    [AvaloniaFact]
    public async Task AnEmptySelectedCellIsOfferedAsZeroRowsAndNeverBroadened()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(OracleLog);
        var snapshot = fixture.Tab.Snapshot!;
        var session = snapshot.TimedRange!.Value;
        var origin = session.StartInclusive.Value;
        var filter = FilterSpec.All with
        {
            Search = new TextSearchSpec("needle"),
            IncludedTags = ImmutableHashSet.Create("A"),
            TimeRange = At(origin, 1, 6),
        };

        // Second 3 holds only tag B, so an Error cell there matches nothing under this filter.
        var scopes = ExportScopeResolver.Resolve(
            new FrozenExportRequest(
                snapshot.SessionId,
                fixture.Tab.SessionPath,
                fixture.Tab.Title,
                filter,
                At(origin, 2, 5),
                At(origin, 3, 4),
                LogLevel.Error,
                ExplicitRange: null,
                EntryOrder.SourceSequence,
                DefaultIncludeUtf8Bom: false,
                CaptureContinues: false),
            session);

        Assert.Empty(Sequences(fixture, scopes[0]));
        Assert.True(scopes[0].Preferred, "the empty cell is still the offered default");
        Assert.Equal([2, 3], Sequences(fixture, scopes[1]));
        Assert.Equal(8, Sequences(fixture, scopes[^1]).Length);
    }

    [AvaloniaFact]
    public async Task AnExplicitRangeIsTheOnlyChoiceAndDropsTheCellSeverity()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(OracleLog);
        var snapshot = fixture.Tab.Snapshot!;
        var session = snapshot.TimedRange!.Value;
        var origin = session.StartInclusive.Value;
        var filter = FilterSpec.All with
        {
            Search = new TextSearchSpec("needle"),
            IncludedTags = ImmutableHashSet.Create("A"),
            TimeRange = At(origin, 1, 6),
        };

        var scopes = ExportScopeResolver.Resolve(
            new FrozenExportRequest(
                snapshot.SessionId,
                fixture.Tab.SessionPath,
                fixture.Tab.Title,
                filter,
                At(origin, 2, 5),
                At(origin, 2, 3),
                LogLevel.Error,
                ExplicitRange: At(origin, 3, 6),
                EntryOrder.SourceSequence,
                DefaultIncludeUtf8Bom: false,
                CaptureContinues: false),
            session);

        var only = Assert.Single(scopes);
        Assert.Equal(ExportScopeKind.SelectedRange, only.Kind);

        // The lingering Error cell does not narrow an explicitly chosen range, and no broader
        // fallback is offered beside it.
        Assert.Equal([6], Sequences(fixture, only));
    }

    /// <summary>Runs the actual CSV query for one scope and reports the records it selects.</summary>
    private static long[] Sequences(LiveTestWorkspaceFixture fixture, ResolvedExportScope scope) =>
        SessionQueryEngine.GetEntries(
                fixture.Tab.Snapshot!,
                scope.Range,
                scope.Filter,
                EntryOrder.SourceSequence,
                cursor: null,
                pageSize: 100,
                queryGeneration: 1)
            .Entries
            .Select(static entry => entry.SourceSequence)
            .ToArray();

    private static TimeRange At(long origin, long fromSecond, long toSecond) => new(
        new InstantUs(origin + (fromSecond * 1_000_000)),
        new InstantUs(origin + (toSecond * 1_000_000)));
}
