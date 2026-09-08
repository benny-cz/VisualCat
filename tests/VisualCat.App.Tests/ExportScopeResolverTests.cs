using System.Collections.Immutable;
using VisualCat.App.Presentation;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

public sealed class ExportScopeResolverTests
{
    [Fact]
    public void ResolverIntersectsSessionViewportTimeFilterAndCellSeverity()
    {
        var filter = FilterSpec.All with
        {
            TimeRange = Range(10, 90),
            IncludedLevels = ImmutableHashSet.Create(LogLevel.Info, LogLevel.Error),
            IncludedTags = ImmutableHashSet.Create("Crash"),
        };
        var request = Request(filter, Range(20, 70), Range(30, 50), LogLevel.Error);

        var scopes = ExportScopeResolver.Resolve(request, Range(0, 100));

        Assert.Collection(
            scopes,
            selected =>
            {
                Assert.Equal(ExportScopeKind.SelectedCell, selected.Kind);
                Assert.Equal(Range(30, 50), selected.Range);
                Assert.Equal([LogLevel.Error], selected.Filter.IncludedLevels);
                Assert.True(selected.Preferred);
            },
            visible =>
            {
                Assert.Equal(ExportScopeKind.VisiblePlot, visible.Kind);
                Assert.Equal(Range(20, 70), visible.Range);
                Assert.Equal(filter.IncludedTags, visible.Filter.IncludedTags);
            },
            filtered =>
            {
                Assert.Equal(ExportScopeKind.AllFiltered, filtered.Kind);
                Assert.Equal(Range(10, 90), filtered.Range);
            },
            all =>
            {
                Assert.Equal(ExportScopeKind.AllTimed, all.Kind);
                Assert.Equal(Range(0, 100), all.Range);
                Assert.True(all.Filter.IsUnconstrained);
            });
    }

    [Fact]
    public void DisjointCellLevelStaysEmptyInsteadOfBroadening()
    {
        var filter = FilterSpec.All with
        {
            IncludedLevels = ImmutableHashSet.Create(LogLevel.Info),
        };

        var selected = ExportScopeResolver.Resolve(
            Request(filter, Range(0, 100), Range(20, 30), LogLevel.Error),
            Range(0, 100))[0];

        Assert.True(selected.IsProvablyEmpty);
        Assert.Equal([LogLevel.Error], selected.Filter.IncludedLevels);
    }

    [Fact]
    public void ExplicitRangeHasNoBroaderFallbackAndDropsLingeringCellLevel()
    {
        var request = Request(FilterSpec.All, Range(0, 100), Range(20, 30), LogLevel.Error) with
        {
            ExplicitRange = Range(40, 60),
        };

        var scope = Assert.Single(ExportScopeResolver.Resolve(request, Range(0, 100)));

        Assert.Equal(ExportScopeKind.SelectedRange, scope.Kind);
        Assert.Equal(Range(40, 60), scope.Range);
        Assert.Empty(scope.Filter.IncludedLevels);
    }

    [Fact]
    public void FittedUnfilteredPlotDeduplicatesToAllTimedSession()
    {
        var scope = Assert.Single(ExportScopeResolver.Resolve(
            Request(FilterSpec.All, Range(0, 100), null, null),
            Range(0, 100)));

        Assert.Equal(ExportScopeKind.AllTimed, scope.Kind);
        Assert.True(scope.Preferred);
    }

    [Fact]
    public void FittedFilteredPlotKeepsTheEarlierPreferredVisibleScope()
    {
        var filter = FilterSpec.All with { IncludedTags = ImmutableHashSet.Create("Crash") };
        var scopes = ExportScopeResolver.Resolve(
            Request(filter, Range(0, 100), null, null),
            Range(0, 100));

        Assert.Collection(
            scopes,
            visible =>
            {
                Assert.Equal(ExportScopeKind.VisiblePlot, visible.Kind);
                Assert.True(visible.Preferred);
                Assert.Equal(filter.IncludedTags, visible.Filter.IncludedTags);
            },
            all => Assert.Equal(ExportScopeKind.AllTimed, all.Kind));
    }

    private static FrozenExportRequest Request(
        FilterSpec filter,
        TimeRange viewport,
        TimeRange? detail,
        LogLevel? level) => new(
            Guid.Parse("45d407f8-2bb7-4011-8763-5cf2a0957548"),
            "session",
            "fixture.log",
            filter,
            viewport,
            detail,
            level,
            ExplicitRange: null,
            EntryOrder.SourceSequence,
            DefaultIncludeUtf8Bom: true,
            CaptureContinues: false);

    private static TimeRange Range(long start, long end) => new(new InstantUs(start), new InstantUs(end));
}
