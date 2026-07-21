namespace VisualCat.App.Timeline;

/// <summary>
/// Pure device geometry for heat-map data columns.
/// <para>
/// A column is one device pixel wide by design (§12.4), which is right for reading the
/// shape of a dense row but leaves an isolated burst as a hairline that is almost
/// impossible to point at. These helpers widen a short run of occupied columns to a
/// legible minimum and snap the pointer onto the nearest occupied column, so drawing,
/// hover, and click all agree on where a bar is (R9, §14.5).
/// </para>
/// Widening only ever applies to runs narrower than the minimum: a dense row is already
/// wider than the minimum everywhere, so its shape is drawn untouched.
/// </summary>
public static class TimelineBars
{
    /// <summary>
    /// Default drawn width of an isolated bar, in logical pixels. One device pixel is
    /// under half a millimetre on a dense display — legible as a signal, but not a
    /// pointer target.
    /// </summary>
    public const double DefaultMinimumWidth = 5;

    /// <summary>Widest bar the caller may request, in logical pixels.</summary>
    public const double MaximumMinimumWidth = 12;

    /// <summary>Bars never catch the pointer from further than this many columns away.</summary>
    private const int MaximumSnapColumns = 128;

    public static double ClampMinimumWidth(double minimumWidth) =>
        double.IsFinite(minimumWidth) ? Math.Clamp(minimumWidth, 1, MaximumMinimumWidth) : DefaultMinimumWidth;

    /// <summary>
    /// Last column of the run of occupied columns that starts at <paramref name="first"/>.
    /// Returns <paramref name="first"/> for an isolated column, so a render loop can walk
    /// the row in one pass without scanning any column twice.
    /// </summary>
    public static int RunEnd(ReadOnlySpan<long> counts, int first)
    {
        var last = first;
        while (last + 1 < counts.Length && counts[last + 1] > 0)
        {
            last++;
        }

        return last;
    }

    /// <summary>
    /// Device rectangle of one column inside the run <c>[first, last]</c>. Runs at or
    /// above <paramref name="minimumWidth"/> keep their natural geometry; narrower runs
    /// are expanded around their centre and share the expanded width evenly, which keeps
    /// each column's relative position and the run's total ink proportional.
    /// </summary>
    public static (double X, double Width) BarRect(
        int first,
        int last,
        int column,
        double left,
        double totalWidth,
        int columnCount,
        double minimumWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, first);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, last);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalWidth);

        var columnWidth = totalWidth / columnCount;
        var runLength = last - first + 1;
        var runWidth = runLength * columnWidth;
        var target = Math.Min(ClampMinimumWidth(minimumWidth), totalWidth);
        if (runWidth >= target)
        {
            return (left + (first + (column - first)) * columnWidth, columnWidth);
        }

        var centre = left + (first + runLength / 2d) * columnWidth;
        var start = Math.Clamp(centre - target / 2, left, left + totalWidth - target);
        var share = target / runLength;
        return (start + (column - first) * share, share);
    }

    /// <summary>
    /// Expands a drawn interval to <paramref name="minimumWidth"/> around its centre while
    /// keeping it inside the plot. Used for the selection outline so that selecting a
    /// one-pixel cell produces a mark the user can actually see (§14.7).
    /// </summary>
    public static (double X, double Width) EnsureMinimumWidth(
        double startX,
        double endX,
        double minimumWidth,
        double left,
        double totalWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalWidth);
        var x0 = Math.Min(startX, endX);
        var x1 = Math.Max(startX, endX);
        var target = Math.Min(Math.Max(minimumWidth, 1), totalWidth);
        var width = x1 - x0;
        if (width >= target)
        {
            return (x0, width);
        }

        var start = Math.Clamp(x0 + width / 2 - target / 2, left, left + totalWidth - target);
        return (start, target);
    }

    /// <summary>
    /// Nearest occupied column within <paramref name="radiusPixels"/> of
    /// <paramref name="column"/>, preferring the earlier column on a tie so hit testing is
    /// deterministic. Returns <paramref name="column"/> unchanged when the pointer is
    /// already on data or when nothing is close enough — clicking genuine emptiness must
    /// still select that empty interval.
    /// </summary>
    public static int SnapToOccupiedColumn(
        ReadOnlySpan<long> counts,
        int column,
        double columnWidth,
        double radiusPixels)
    {
        if (counts.Length == 0)
        {
            return column;
        }

        column = Math.Clamp(column, 0, counts.Length - 1);
        if (counts[column] > 0)
        {
            return column;
        }

        if (!double.IsFinite(columnWidth) || columnWidth <= 0 || !double.IsFinite(radiusPixels) || radiusPixels <= 0)
        {
            return column;
        }

        var radius = Math.Min(MaximumSnapColumns, (int)Math.Floor(radiusPixels / columnWidth));
        for (var distance = 1; distance <= radius; distance++)
        {
            var before = column - distance;
            if (before >= 0 && counts[before] > 0)
            {
                return before;
            }

            var after = column + distance;
            if (after < counts.Length && counts[after] > 0)
            {
                return after;
            }
        }

        return column;
    }

    /// <summary>
    /// How far the pointer may sit from a bar and still catch it: half the drawn bar, so
    /// every painted pixel selects the bar it belongs to, plus a forgiveness margin for
    /// the pixel or two of aim a pointing device costs.
    /// </summary>
    public static double SnapRadiusPixels(double minimumWidth) =>
        ClampMinimumWidth(minimumWidth) / 2 + 5;

    /// <summary>
    /// Share of the row height an occupied cell fills, given its normalized intensity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Intensity is carried by opacity alone if a cell always fills its row, and §12.5
    /// and §14.14 both refuse to let color be the only signal: on a dense row every cell
    /// then renders as a near-saturated block and the shape of the traffic — the entire
    /// point of the view — disappears into a solid wall.
    /// </para>
    /// <para>
    /// Height is therefore a second, redundant encoding of the same number. The floor is
    /// what keeps it honest in the other direction: a lone Fatal in an otherwise empty
    /// row is the most important mark on the plot, and scaling it linearly from zero
    /// would draw it as a stub thinner than the grid line under it. Any occupied cell
    /// keeps a clearly visible share of its row and only the growth above that floor
    /// tracks intensity.
    /// </para>
    /// </remarks>
    public static double BarHeightFraction(double normalizedIntensity)
    {
        if (!double.IsFinite(normalizedIntensity))
        {
            return 1;
        }

        var clamped = Math.Clamp(normalizedIntensity, 0, 1);
        return OccupiedFloorFraction + clamped * (1 - OccupiedFloorFraction);
    }

    /// <summary>Row share every occupied cell fills before intensity is applied.</summary>
    public const double OccupiedFloorFraction = 0.42;
}
