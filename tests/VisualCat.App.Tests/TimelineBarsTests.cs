using VisualCat.App.Timeline;

namespace VisualCat.App.Tests;

public sealed class TimelineBarsTests
{
    private const double Left = 76;
    private const double Width = 1200;

    /// <summary>
    /// The height encoding must stay monotonic in intensity, or it would contradict the
    /// opacity encoding of the same number instead of reinforcing it.
    /// </summary>
    [Fact]
    public void BarHeightRisesWithIntensityAndNeverFallsBelowTheVisibleFloor()
    {
        Assert.Equal(TimelineBars.OccupiedFloorFraction, TimelineBars.BarHeightFraction(0), 6);
        Assert.Equal(1, TimelineBars.BarHeightFraction(1), 6);

        var previous = 0d;
        for (var step = 0; step <= 100; step++)
        {
            var fraction = TimelineBars.BarHeightFraction(step / 100d);

            // A single rare event stays plainly visible: the floor is what stops the most
            // important mark on the plot from rendering as a hairline.
            Assert.InRange(fraction, TimelineBars.OccupiedFloorFraction, 1);
            Assert.True(fraction >= previous, $"Height fell at intensity {step / 100d}.");
            previous = fraction;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-5)]
    [InlineData(17)]
    public void BarHeightClampsHostileIntensities(double intensity)
    {
        Assert.InRange(TimelineBars.BarHeightFraction(intensity), TimelineBars.OccupiedFloorFraction, 1);
    }

    [Fact]
    public void AnIsolatedColumnIsWidenedToTheMinimumWidth()
    {
        var counts = new long[Columns];
        counts[400] = 5;
        var (x, width) = Rect(counts, 400, minimumWidth: 3);

        Assert.Equal(3, width, 3);

        // The widened bar stays centred on the column it represents, so the mark still
        // sits at the time it describes.
        var columnWidth = Width / Columns;
        var centre = Left + 400.5 * columnWidth;
        Assert.InRange(Math.Abs(x + width / 2 - centre), 0, 0.001);
    }

    [Fact]
    public void ARunWiderThanTheMinimumKeepsItsNaturalGeometry()
    {
        var counts = new long[Columns];
        for (var column = 100; column < 140; column++)
        {
            counts[column] = column;
        }

        var columnWidth = Width / Columns;
        for (var column = 100; column < 140; column++)
        {
            var (x, width) = Rect(counts, column, minimumWidth: 3);
            Assert.Equal(columnWidth, width, 6);
            Assert.Equal(Left + column * columnWidth, x, 6);
        }
    }

    [Fact]
    public void WidenedBarsStayInsideThePlotAndInColumnOrder()
    {
        var counts = new long[Columns];
        counts[0] = 1;
        counts[Columns - 1] = 1;
        counts[Columns / 2] = 1;

        var previous = double.NegativeInfinity;
        foreach (var column in new[] { 0, Columns / 2, Columns - 1 })
        {
            var (x, width) = Rect(counts, column, minimumWidth: 5);
            Assert.True(x >= Left, $"column {column} starts left of the plot");
            Assert.True(x + width <= Left + Width + 0.001, $"column {column} runs past the plot");
            Assert.True(x > previous, "bars must stay in column order");
            previous = x;
        }
    }

    [Fact]
    public void EveryDrawnBarCanBeClicked()
    {
        // The defect this guards: a one-device-pixel bar is drawn wider than the column
        // it stands for, so the pointer lands on an empty neighbouring column and the
        // click selects nothing. Pointing at the middle of anything visible must select
        // an occupied cell.
        var random = new Random(31);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var counts = new long[Columns];
            var occupied = new List<int>();
            for (var column = 0; column < Columns; column++)
            {
                if (random.NextDouble() < 0.02)
                {
                    counts[column] = random.NextInt64(1, 500);
                    occupied.Add(column);
                }
            }

            var columnWidth = Width / Columns;
            var minimumWidth = 3d;
            foreach (var column in occupied)
            {
                var (x, width) = Rect(counts, column, minimumWidth);
                var pointer = x + width / 2;
                var raw = Math.Clamp((int)((pointer - Left) / Width * Columns), 0, Columns - 1);
                var snapped = TimelineBars.SnapToOccupiedColumn(
                    counts,
                    raw,
                    columnWidth,
                    TimelineBars.SnapRadiusPixels(minimumWidth));
                Assert.True(counts[snapped] > 0, $"pointing at the middle of column {column} selected an empty cell");
            }
        }
    }

    [Fact]
    public void SnappingPrefersTheNearerColumnAndKeepsGenuineEmptinessSelectable()
    {
        long[] counts = [0, 0, 7, 0, 0, 0, 0, 0, 0, 3];

        // Already on data: never moved.
        Assert.Equal(2, TimelineBars.SnapToOccupiedColumn(counts, 2, 1, 4));

        // One column away on the left, two on the right: the nearer one wins.
        Assert.Equal(2, TimelineBars.SnapToOccupiedColumn(counts, 3, 1, 4));

        // Equidistant: resolved to the earlier column so hit testing is deterministic.
        long[] tie = [1, 0, 1];
        Assert.Equal(0, TimelineBars.SnapToOccupiedColumn(tie, 1, 1, 4));

        // Far from anything: the empty interval itself stays selectable.
        Assert.Equal(6, TimelineBars.SnapToOccupiedColumn(counts, 6, 1, 2));

        // Wide columns leave nothing to snap to.
        Assert.Equal(5, TimelineBars.SnapToOccupiedColumn(counts, 5, 20, 4));
    }

    [Fact]
    public void SelectionOutlineIsWidenedWithoutLeavingThePlot()
    {
        var (x, width) = TimelineBars.EnsureMinimumWidth(Left, Left + 0.4, 5, Left, Width);
        Assert.Equal(5, width, 6);
        Assert.True(x >= Left);

        var (rightX, rightWidth) = TimelineBars.EnsureMinimumWidth(
            Left + Width - 0.2,
            Left + Width,
            5,
            Left,
            Width);
        Assert.Equal(5, rightWidth, 6);
        Assert.True(rightX + rightWidth <= Left + Width + 0.001);

        // An interval already wider than the minimum is returned untouched.
        var (wideX, wideWidth) = TimelineBars.EnsureMinimumWidth(Left + 10, Left + 40, 5, Left, Width);
        Assert.Equal(Left + 10, wideX, 6);
        Assert.Equal(30, wideWidth, 6);
    }

    [Fact]
    public void MinimumWidthIsClampedToASaneRange()
    {
        Assert.Equal(1, TimelineBars.ClampMinimumWidth(0));
        Assert.Equal(1, TimelineBars.ClampMinimumWidth(-4));
        Assert.Equal(TimelineBars.MaximumMinimumWidth, TimelineBars.ClampMinimumWidth(1000));
        Assert.Equal(TimelineBars.DefaultMinimumWidth, TimelineBars.ClampMinimumWidth(double.NaN));
        Assert.Equal(4.5, TimelineBars.ClampMinimumWidth(4.5));
    }

    private const int Columns = 1200;

    private static (double X, double Width) Rect(long[] counts, int column, double minimumWidth)
    {
        var first = column;
        while (first > 0 && counts[first - 1] > 0)
        {
            first--;
        }

        return TimelineBars.BarRect(
            first,
            TimelineBars.RunEnd(counts, first),
            column,
            Left,
            Width,
            counts.Length,
            minimumWidth);
    }
}
