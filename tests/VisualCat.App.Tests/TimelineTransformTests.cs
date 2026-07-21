using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

public sealed class TimelineTransformTests
{
    [Fact]
    public void InstantPixelRoundTripStaysWithinOneMicrosecond()
    {
        var transform = Transform(new TimeRange(new InstantUs(10_000), new InstantUs(9_010_000)));
        for (var x = 64d; x <= 1264; x += 7.3)
        {
            var instant = transform.XToInstant(x);
            Assert.InRange(Math.Abs(transform.InstantToX(instant) - x), 0, 0.001);
        }
    }

    [Fact]
    public void PointerFocusedZoomPreservesFocusTime()
    {
        var session = new TimeRange(new InstantUs(0), new InstantUs(100_000_000));
        var random = new Random(17);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var start = random.NextInt64(0, 50_000_000);
            var span = random.NextInt64(100_000, 40_000_000);
            var viewport = new TimeRange(new InstantUs(start), new InstantUs(start + span));
            var transform = Transform(viewport);
            var x = 64 + random.NextDouble() * 1200;
            var before = transform.XToInstant(x);
            var zoomed = transform.Zoom(x, 0.8, 1000, 110_000_000, session, 0.05);
            var after = Transform(zoomed).XToInstant(x);
            Assert.InRange(Math.Abs(after.Value - before.Value), 0, 2);
        }
    }

    [Fact]
    public void ZoomingByInverseFactorsRestoresTheViewportWhenNoClampApplies()
    {
        // §20.3: zoom in then out by the reciprocal returns to the starting viewport.
        // The session range is far larger than any viewport used here so neither the
        // span limits nor the overscroll clamp can participate.
        var session = new TimeRange(new InstantUs(-500_000_000), new InstantUs(500_000_000));
        var random = new Random(4242);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var start = random.NextInt64(-10_000_000, 10_000_000);
            var span = random.NextInt64(1_000_000, 20_000_000);
            var viewport = new TimeRange(new InstantUs(start), new InstantUs(start + span));
            var x = 64 + random.NextDouble() * 1200;
            var factor = 0.25 + random.NextDouble() * 3;

            var zoomedIn = Transform(viewport).Zoom(x, factor, 1_000, 900_000_000, session, 0.05);
            var restored = Transform(zoomedIn).Zoom(x, 1 / factor, 1_000, 900_000_000, session, 0.05);

            // Two roundings of the focus offset and one of the span; a couple of
            // microseconds of slack covers them without hiding a real drift.
            Assert.InRange(Math.Abs(restored.StartInclusive.Value - viewport.StartInclusive.Value), 0, 3);
            Assert.InRange(Math.Abs(restored.EndExclusive.Value - viewport.EndExclusive.Value), 0, 3);
        }
    }

    [Fact]
    public void PanningByOppositeDeltasRestoresTheViewport()
    {
        var session = new TimeRange(new InstantUs(-500_000_000), new InstantUs(500_000_000));
        var viewport = new TimeRange(new InstantUs(0), new InstantUs(10_000_000));
        foreach (var delta in new[] { -400d, -37.5, 1, 12.25, 300 })
        {
            var panned = Transform(viewport).Pan(delta, session);
            var restored = Transform(panned).Pan(-delta, session);
            Assert.InRange(Math.Abs(restored.StartInclusive.Value - viewport.StartInclusive.Value), 0, 2);
            Assert.Equal(viewport.DurationUs, restored.DurationUs);
        }
    }

    [Fact]
    public void HitTestingCoversEveryRowExactlyOnceAcrossTheFullHeight()
    {
        // §20.7: hit testing at every margin and boundary. Each device row must map to
        // its own level, and no pixel inside the plot may fall through.
        var transform = Transform(new TimeRange(new InstantUs(0), new InstantUs(1_000_000)));
        var levels = LogLevels.DisplayOrder.ToArray();
        var seen = new Dictionary<LogLevel, int>();
        for (var y = transform.Geometry.Top; y < transform.Geometry.Top + transform.Geometry.Height; y += 0.5)
        {
            var level = transform.YToLevel(y);
            Assert.NotNull(level);
            seen[level!.Value] = seen.GetValueOrDefault(level.Value) + 1;
        }

        Assert.Equal(levels.Length, seen.Count);
        Assert.Null(transform.YToLevel(transform.Geometry.Top - 0.01));
        Assert.Null(transform.YToLevel(transform.Geometry.Top + transform.Geometry.Height));

        // Every row boundary resolves to the row it opens, never the previous one.
        foreach (var level in levels)
        {
            Assert.Equal(level, transform.YToLevel(transform.LevelToY(level)));
        }
    }

    [Fact]
    public void NiceTicksUseOneTwoFiveLadder()
    {
        foreach (var span in new[] { 1_000L, 10_000L, 1_000_000L, 86_400_000_000L })
        {
            var interval = NiceTicks.SelectInterval(span, 1000);
            var magnitude = (long)Math.Pow(10, Math.Floor(Math.Log10(interval)));
            Assert.Contains(interval / magnitude, new long[] { 1, 2, 5, 10 });
        }
    }

    [Theory]
    [InlineData(1200, 500, 600_000)]
    [InlineData(1328, 1, 1_328)]
    [InlineData(360, 0.1, 36)]
    public void MinimumZoomSpanTracksPhysicalPixelsAndConfiguredPrecision(
        double devicePixels,
        double microsecondsPerPixel,
        long expectedSpan)
    {
        Assert.Equal(
            expectedSpan,
            TimelineTransform.MinimumSpanUs(devicePixels, microsecondsPerPixel));
    }

    [Fact]
    public void MinimumZoomSpanRejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TimelineTransform.MinimumSpanUs(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimelineTransform.MinimumSpanUs(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimelineTransform.MinimumSpanUs(double.NaN, 1));
    }

    [Fact]
    public void MinimapBrushRoundTripsThroughTheSharedTransform()
    {
        var session = new TimeRange(new InstantUs(1_000), new InstantUs(10_001_000));
        var transform = new TimelineTransform(
            session,
            new TimelineGeometry(0, 0, 1000, 50),
            [LogLevel.Info]);
        var selected = new TimeRange(new InstantUs(2_001_000), new InstantUs(7_501_000));
        var interval = transform.RangeToXInterval(selected);
        var roundTrip = transform.XIntervalToRange(interval.StartX, interval.EndX);

        Assert.InRange(Math.Abs(roundTrip.StartInclusive.Value - selected.StartInclusive.Value), 0, 1);
        Assert.InRange(Math.Abs(roundTrip.EndExclusive.Value - selected.EndExclusive.Value), 0, 1);
    }

    private static TimelineTransform Transform(TimeRange range) =>
        new(
            range,
            new TimelineGeometry(64, 10, 1200, 420),
            LogLevels.DisplayOrder.ToArray());
}
