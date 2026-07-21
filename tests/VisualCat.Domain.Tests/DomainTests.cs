using System.Collections.Immutable;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Domain.Tests;

public sealed class DomainTests
{
    [Fact]
    public void SeverityMappingsAreCanonicalAndUnknownIsFirstClass()
    {
        Assert.Equal(
            [LogLevel.Verbose, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal, LogLevel.Unknown],
            LogLevels.StorageOrder.ToArray());
        Assert.Equal(
            [LogLevel.Fatal, LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, LogLevel.Verbose, LogLevel.Unknown],
            LogLevels.DisplayOrder.ToArray());
        Assert.Equal(LogLevel.Fatal, LogLevels.Parse('A'));
        Assert.Equal(LogLevel.Unknown, LogLevels.Parse('Q'));
    }

    [Theory]
    [InlineData(-1, 1000, -1)]
    [InlineData(-1000, 1000, -1)]
    [InlineData(-1001, 1000, -2)]
    [InlineData(0, 1000, 0)]
    [InlineData(1001, 1000, 1)]
    public void FloorDivisionIsMathematical(long value, long divisor, long expected) =>
        Assert.Equal(expected, BucketAlignment.FloorDiv(value, divisor));

    [Fact]
    public void HalfOpenRangesAssignBoundariesExactlyOnce()
    {
        var alignment = BucketAlignment.UnixEpoch;
        var width = new BucketWidth(1000);
        Assert.Equal(new TimeRange(new InstantUs(0), new InstantUs(1000)), alignment.RangeContaining(new InstantUs(0), width));
        Assert.Equal(new TimeRange(new InstantUs(1000), new InstantUs(2000)), alignment.RangeContaining(new InstantUs(1000), width));
        Assert.False(alignment.RangeContaining(new InstantUs(0), width).Contains(new InstantUs(1000)));
    }

    [Fact]
    public void PixelViewportBoundariesHaveNoGaps()
    {
        var viewport = new Viewport(new TimeRange(new InstantUs(-17), new InstantUs(103)), 17);
        Assert.Equal(viewport.Range.StartInclusive, viewport.Boundary(0));
        Assert.Equal(viewport.Range.EndExclusive, viewport.Boundary(17));
        for (var index = 0; index < viewport.DevicePixelWidth; index++)
        {
            Assert.True(viewport.Boundary(index) <= viewport.Boundary(index + 1));
        }
    }

    [Fact]
    public void FilterFingerprintIsIndependentOfSetInsertionOrder()
    {
        var left = new FilterSpec
        {
            IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "z", "a"),
            IncludedPids = ImmutableHashSet.Create(9, 1),
        };
        var right = new FilterSpec
        {
            IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "a", "z"),
            IncludedPids = ImmutableHashSet.Create(1, 9),
        };
        Assert.Equal(left.Fingerprint(), right.Fingerprint());
    }

    [Fact]
    public void EveryFilterDimensionChangesTheFingerprint()
    {
        // The fingerprint is the cache key for per-segment filter bitmaps (§12.3), so two
        // filters that select different entries must never share one. A dimension left out
        // of the fingerprint shows up as a filter that appears to select an arbitrary
        // earlier result.
        var variants = new Dictionary<string, FilterSpec>(StringComparer.Ordinal)
        {
            ["all"] = FilterSpec.All,
            ["levels"] = FilterSpec.All with { IncludedLevels = ImmutableHashSet.Create(LogLevel.Warn) },
            ["tag+"] = FilterSpec.All with { IncludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "T") },
            ["tag-"] = FilterSpec.All with { ExcludedTags = ImmutableHashSet.Create(StringComparer.Ordinal, "T") },
            ["pid+"] = FilterSpec.All with { IncludedPids = ImmutableHashSet.Create(7) },
            ["pid-"] = FilterSpec.All with { ExcludedPids = ImmutableHashSet.Create(7) },
            ["process+"] = FilterSpec.All with { IncludedProcesses = ImmutableHashSet.Create(StringComparer.Ordinal, "p") },
            ["process-"] = FilterSpec.All with { ExcludedProcesses = ImmutableHashSet.Create(StringComparer.Ordinal, "p") },
            ["tid+"] = FilterSpec.All with { IncludedTids = ImmutableHashSet.Create(7) },
            ["tid-"] = FilterSpec.All with { ExcludedTids = ImmutableHashSet.Create(7) },
            ["template+"] = FilterSpec.All with { IncludedTemplates = ImmutableHashSet.Create(7u) },
            ["template-"] = FilterSpec.All with { ExcludedTemplates = ImmutableHashSet.Create(7u) },
            ["buffer+"] = FilterSpec.All with { IncludedBuffers = ImmutableHashSet.Create(StringComparer.Ordinal, "main") },
            ["buffer-"] = FilterSpec.All with { ExcludedBuffers = ImmutableHashSet.Create(StringComparer.Ordinal, "main") },
            ["outcome"] = FilterSpec.All with { IncludedOutcomes = ImmutableHashSet.Create(ParseOutcomeKind.UnknownLine) },
            ["time"] = FilterSpec.All with { TimeRange = new TimeRange(new InstantUs(0), new InstantUs(1)) },
            ["search"] = FilterSpec.All with { Search = new TextSearchSpec("x") },
        };

        var fingerprints = variants.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Fingerprint(),
            StringComparer.Ordinal);
        Assert.Equal(variants.Count, fingerprints.Values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void StateMachineRejectsInvalidTransitions()
    {
        var machine = new SessionStateMachine();
        machine.TransitionTo(SessionState.SelectingSource);
        machine.TransitionTo(SessionState.Importing);
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(SessionState.Streaming));
        machine.TransitionTo(SessionState.Ready);
        Assert.Equal(SessionState.Ready, machine.State);
    }
}
