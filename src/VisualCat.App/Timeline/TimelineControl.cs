using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Timeline;

public sealed record TimelineCellSelection(TimeRange Range, LogLevel Level, long Count);

/// <summary>
/// The debounced per-cell answer behind the hover readout: which pattern dominates the
/// hovered half-open interval at the hovered severity (§14.7). It carries the cell it
/// describes so a late result for a cell the pointer has already left is discarded
/// rather than drawn against the wrong cell (R15).
/// </summary>
public sealed record TimelineHoverInsight(TimeRange Range, LogLevel Level, string? TemplateText, long TemplateCount);

public sealed class TimelineControl : Control
{
    // Immutable, cached drawing resources: Render touches thousands of cells per frame,
    // so nothing in it may allocate per cell (R11, §15.2, §19.3).
    private static readonly Typeface MonoTypeface = new(
        new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,Roboto Mono,monospace"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush DarkForeground = new(Color.Parse("#EAF2FF"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush LightForeground = new(Color.Parse("#172033"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush DarkMuted = new(Color.Parse("#93A4BD"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush LightMuted = new(Color.Parse("#54647A"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush DarkBackground = new(Color.Parse("#080D16"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush LightBackground = new(Color.Parse("#F4F7FC"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush AccentBrush = new(Color.Parse("#4FC3F7"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush HoverBoxFill = new(Color.Parse("#F3111B2B"));
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush HoverBoxMuted = new(Color.Parse("#AFC4DE"));
    private static readonly Avalonia.Media.Immutable.ImmutablePen DarkGridPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#26364D")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen LightGridPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#D9E2EF")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen DarkMinorGridPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#17253A")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen LightMinorGridPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#E8EEF6")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen DarkCrosshairPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#E8FFFFFF")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen LightCrosshairPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#D8172033")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen SearchMarkerPen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(WorkspacePalette.SearchMatch), 1);
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush RangeFill = new(Color.Parse("#304DA3FF"));
    private static readonly Avalonia.Media.Immutable.ImmutablePen RangePen = new(new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#B84DA3FF")), 1);
    private static readonly Avalonia.Media.Immutable.ImmutablePen SelectionPen = new(Brushes.White, 2);
    private static readonly Avalonia.Media.Immutable.ImmutablePen SelectionPenHighContrast = new(Brushes.Yellow, 3);

    /// <summary>Down-pointing marker with its base on the origin, built once and placed by
    /// transform: the selected-entry mark must not allocate geometry per frame.</summary>
    private static readonly StreamGeometry EntryMarkGlyph = BuildEntryMarkGlyph();

    private LogLevel[] _displayLevels = TimelineLevelLayout.Resolve(new HashSet<LogLevel>(), sessionHasUnknown: false);
    private HeatMapResult? _result;
    private TimeRange? _sessionRange;
    private Point? _dragOrigin;
    private TimeRange? _dragViewport;
    private TimelineCellSelection? _selection;
    private Point? _rangeOrigin;
    private bool _suppressCellSelection;
    private TimeRange? _rangeSelection;
    private TimeRange? _pinchViewport;
    private int? _hoverColumn;
    private LogLevel? _hoverLevel;
    private TimelineHoverInsight? _hoverInsight;
    private InstantUs? _entryMarkInstant;
    private LogLevel? _entryMarkLevel;
    private SearchResult? _searchResult;
    private string _intensityScale = "Logarithmic";
    private string _normalization = "PerRow";
    private double _minimumUsPerPixel = 1;
    private double _minimumBarWidth = TimelineBars.DefaultMinimumWidth;
    private bool _pixelSnap = true;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;
    private string _emptyTitle = "Open a logcat file or start a live capture.";
    private string _emptyDetail = "The severity × time signal will appear here.";

    /// <summary>Band above the lanes: the header line and the selected-entry marker.</summary>
    private const double HeaderBandHeight = 36;

    /// <summary>
    /// Band below the lanes: tick labels and search markers.
    /// </summary>
    /// <remarks>
    /// Reserved inside the control's own geometry so the plot shrinks instead of the labels
    /// being overdrawn. The control used to demand a minimum height instead, which a star-sized
    /// row cannot refuse: with three session tabs open, or a capture row added, the arranged
    /// height exceeded the row and the bottom of the axis labels was drawn underneath the
    /// minimap panel that follows it in the grid (finding 18).
    /// </remarks>
    private const double AxisBandHeight = 34;

    /// <summary>Lane band below which there is no plot worth drawing.</summary>
    private const double MinimumLaneBandHeight = 36;

    public TimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetName(this, "Severity by time heat map");
        AutomationProperties.SetHelpText(this, "Mouse wheel zooms, drag pans, right-drag selects a range, and arrow keys pan.");

        // Both palettes are resolved inside Render, so a variant change is a repaint and
        // nothing more -- but nothing was asking for the repaint, so the plot kept the
        // variant it happened to be drawn in until a pan or a new snapshot moved it
        // (audit 2, A1b).
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(InputElement.PinchEvent, (_, eventArgs) =>
        {
            if (_result is null ||
                _sessionRange is not { } session ||
                Geometry() is not { } geometry ||
                !double.IsFinite(eventArgs.Scale) ||
                eventArgs.Scale <= 0)
            {
                return;
            }

            _pinchViewport ??= _result.Viewport.Range;
            ViewportChanged?.Invoke(
                this,
                ZoomViewport(_pinchViewport.Value, geometry, session, eventArgs.ScaleOrigin.X, 1 / eventArgs.Scale));
            eventArgs.Handled = true;
        });
        AddHandler(InputElement.PinchEndedEvent, (_, eventArgs) =>
        {
            _pinchViewport = null;
            eventArgs.Handled = true;
        });
    }

    public event EventHandler<TimeRange>? ViewportChanged;
    public event EventHandler<TimelineCellSelection>? CellSelected;
    public event EventHandler<TimelineCellSelection?>? HoverChanged;
    public event EventHandler<TimeRange>? RangeSelected;
    public event EventHandler? FollowRequested;
    public event EventHandler? SearchFocusRequested;

    /// <summary>
    /// The reader tapped the search-marker lane at this instant.
    /// </summary>
    /// <remarks>
    /// The magenta lane under the plot draws every match in view and was not a control: a tap
    /// inside it changed nothing, and there was no marker-navigation node in the accessibility
    /// tree either, so on a phone the product could say "7,181 search matches" and offer no way
    /// to step through them (finding F-07). The stepper in the status bar is the named route;
    /// this is the direct one, and it costs one hit test.
    /// </remarks>
    public event EventHandler<InstantUs>? SearchMarkerPicked;
    public event EventHandler? ExportRequested;
    public event EventHandler<int>? EntryNavigationRequested;

    /// <summary>
    /// Makes lane geometry follow the active severity filter. Unknown is session-stable:
    /// panning into one viewport cannot add a row and move every existing row under the
    /// pointer. Changing the layout also invalidates hover state because its y-to-level
    /// mapping no longer describes the same lane.
    /// </summary>
    public void SetDisplayLevels(IReadOnlySet<LogLevel> includedLevels, bool sessionHasUnknown)
    {
        var next = TimelineLevelLayout.Resolve(includedLevels, sessionHasUnknown);
        if (_displayLevels.AsSpan().SequenceEqual(next))
        {
            return;
        }

        _displayLevels = next;
        if (_selection is { } selection && !next.Contains(selection.Level))
        {
            _selection = null;
        }

        _hoverColumn = null;
        _hoverLevel = null;
        _hoverInsight = null;
        HoverChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    public void SetResult(HeatMapResult? result, TimeRange? sessionRange)
    {
        // A selection that has scrolled out of the viewport would otherwise keep drawing
        // an outline over an unrelated part of the plot; it stays while it is still
        // visible, including while live data advances (§14.7).
        if (result is not null &&
            _selection is { } selection &&
            !selection.Range.Overlaps(result.Viewport.Range))
        {
            _selection = null;
        }

        _result = result;
        _sessionRange = sessionRange;
        InvalidateVisual();
    }

    /// <summary>Explains why a session has no drawable data yet.</summary>
    public void SetEmptyState(string title, string detail)
    {
        _emptyTitle = title;
        _emptyDetail = detail;
        if (_result is null)
        {
            InvalidateVisual();
        }
    }

    /// <summary>Drops the cell outline when the detail scope it represents is released.</summary>
    public void ClearSelection()
    {
        if (_selection is null)
        {
            return;
        }

        _selection = null;
        InvalidateVisual();
    }

    public void SetHoverInsight(TimelineHoverInsight? insight)
    {
        _hoverInsight = insight;
        InvalidateVisual();
    }

    /// <summary>
    /// Marks where the entry the table has selected sits in time (§14.9). Scrolling the
    /// table used to cost the user their place in the plot; the caret is what keeps the
    /// two surfaces one workspace. An untimed entry marks nothing rather than guessing a
    /// position.
    /// </summary>
    public void SetSelectedEntry(InstantUs? instant, LogLevel? level)
    {
        if (_entryMarkInstant == instant && _entryMarkLevel == level)
        {
            return;
        }

        _entryMarkInstant = instant;
        _entryMarkLevel = level;
        InvalidateVisual();
    }

    internal InstantUs? SelectedEntryInstant => _entryMarkInstant;

    internal LogLevel? SelectedEntryLevel => _entryMarkLevel;

    public void SetSearchResult(SearchResult? result)
    {
        _searchResult = result;
        InvalidateVisual();
    }

    public void SetDisplayOptions(
        string intensityScale,
        string normalization,
        double minimumUsPerPixel,
        bool pixelSnap,
        double minimumBarWidth)
    {
        _minimumBarWidth = TimelineBars.ClampMinimumWidth(minimumBarWidth);
        _intensityScale = intensityScale is "Linear" or "SquareRoot" or "Logarithmic"
            ? intensityScale
            : "Logarithmic";
        _normalization = normalization is "GlobalViewport" or "PerRow"
            ? normalization
            : "PerRow";
        _minimumUsPerPixel = double.IsFinite(minimumUsPerPixel)
            ? Math.Clamp(minimumUsPerPixel, 0.1, 500)
            : 1;
        _pixelSnap = pixelSnap;
        InvalidateVisual();
    }

    public void ZoomAtCenter(double factor)
    {
        if (_result is null ||
            _sessionRange is not { } session ||
            Geometry() is not { } geometry)
        {
            return;
        }

        ViewportChanged?.Invoke(
            this,
            ZoomViewport(_result.Viewport.Range, geometry, session, geometry.Left + geometry.Width / 2, factor));
    }

    /// <summary>
    /// Shows the whole session, at no more resolution than the plot has pixels for.
    /// </summary>
    /// <remarks>
    /// A session holding one entry spans one microsecond, and Fit handed that straight to
    /// the viewport: the header read <c>DENSITY · 1 µs · 1,34 ns/px</c> and both axis labels
    /// printed the same instant (audit 2, C6). Zooming has always been clamped by
    /// <see cref="MinimumSpan"/> for exactly this reason — a window narrower than the plot
    /// can resolve states a precision the drawing does not have — and fitting is a zoom.
    /// The session's newest instant is the one kept, because it is the end a live capture is
    /// growing from.
    /// </remarks>
    public void FitSession()
    {
        if (_sessionRange is not { } session)
        {
            return;
        }

        var minimum = Geometry() is { } geometry ? MinimumSpan(geometry) : 1;
        ViewportChanged?.Invoke(
            this,
            session.DurationUs >= minimum
                ? session
                : new TimeRange(
                    new InstantUs(checked(session.EndExclusive.Value - minimum)),
                    session.EndExclusive));
    }

    public void SetTimeZoneContext(string timeZoneId)
    {
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _timeZone = TimeZoneInfo.Utc;
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var isDark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var background = isDark ? DarkBackground : LightBackground;
        var foreground = isDark ? DarkForeground : LightForeground;
        var muted = isDark ? DarkMuted : LightMuted;
        context.FillRectangle(background, Bounds);
        if (_result is null || Geometry() is not { } plotGeometry)
        {
            // Having data and no room to draw it is a different situation from having no data,
            // and saying "open a logcat file" to someone who has one open would be absurd.
            if (_result is not null)
            {
                DrawText(context, "EVENT DENSITY", new Point(22, 14), 11, AccentBrush);
                DrawText(context, "Not enough height to draw the plot.", new Point(22, 34), 12, foreground);
                DrawText(context, "Make this pane taller, or show the plot on its own.", new Point(22, 52), 11, muted);
                return;
            }

            DrawEmptyState(context, foreground, muted);
            return;
        }

        var levels = _displayLevels;
        var geometry = plotGeometry;
        var transform = new TimelineTransform(_result.Viewport.Range, geometry, levels);
        var gridPen = isDark ? DarkGridPen : LightGridPen;
        var minorGridPen = isDark ? DarkMinorGridPen : LightMinorGridPen;

        var resolution = _result.Viewport.Range.DurationUs / Math.Max(1d, _result.Viewport.DevicePixelWidth);
        var normalizationLabel = _normalization == "PerRow" ? "PER-ROW" : "GLOBAL";
        var compactHeader = geometry.Width < 700;
        var header = compactHeader
            ? $"DENSITY  ·  {FormatDuration(_result.Viewport.Range.DurationUs)}  ·  {FormatResolution(resolution)}"
            : $"EVENT DENSITY  ·  {FormatDuration(_result.Viewport.Range.DurationUs)}  ·  {FormatResolution(resolution)}  ·  " +
              $"{normalizationLabel} {_intensityScale.ToUpperInvariant()}";
        DrawText(context, header, new Point(geometry.Left, 9), 10, muted);
        if (!compactHeader)
        {
            var zoneLabel = Shorten(_timeZone.Id, 30);
            var zoneWidth = MeasureTextWidth(zoneLabel, 10);
            DrawText(context, zoneLabel, new Point(Math.Max(geometry.Left, geometry.Left + geometry.Width - zoneWidth), 9), 10, muted);
        }

        for (var row = 0; row <= levels.Length; row++)
        {
            var y = geometry.Top + row * transform.RowHeight;
            context.DrawLine(gridPen, new Point(geometry.Left, y), new Point(geometry.Left + geometry.Width, y));
        }

        for (var row = 0; row < levels.Length; row++)
        {
            var level = levels[row];
            var rowRect = new Rect(
                geometry.Left,
                geometry.Top + row * transform.RowHeight + 1,
                geometry.Width,
                Math.Max(1, transform.RowHeight - 2));
            context.FillRectangle(LevelPalette.Fill(level, isDark ? (byte)15 : (byte)10), rowRect);
            context.DrawLine(
                minorGridPen,
                new Point(geometry.Left, rowRect.Y + rowRect.Height / 2),
                new Point(geometry.Left + geometry.Width, rowRect.Y + rowRect.Height / 2));
        }

        var columnWidth = geometry.Width / _result.Columns.Count;
        var globalMaximum = Math.Max(1, _result.MaximumCount);
        for (var row = 0; row < levels.Length; row++)
        {
            var level = levels[row];
            var values = _result.Counts[level];
            long rowTotal = 0;
            long rowMaximum = 1;
            for (var column = 0; column < values.Length; column++)
            {
                rowTotal += values[column];
                rowMaximum = Math.Max(rowMaximum, values[column]);
            }

            var maximum = _normalization == "PerRow" ? rowMaximum : globalMaximum;
            var baselinePen = LevelPalette.BaselinePen(level);
            var minimumAlpha = App.HighContrastEnabled ? 180 : 112;

            // Walk occupied runs rather than bare columns: a run wider than the minimum
            // draws at its natural width, a narrower one is widened to stay visible and
            // clickable. Each column is visited exactly once (§19.3 — no per-cell
            // allocation, no rescanning).
            for (var column = 0; column < values.Length; column++)
            {
                if (values[column] == 0)
                {
                    continue;
                }

                var last = TimelineBars.RunEnd(values, column);
                for (var index = column; index <= last; index++)
                {
                    var count = values[index];
                    var normalized = _intensityScale switch
                    {
                        "Linear" => count / (double)maximum,
                        "SquareRoot" => Math.Sqrt(count) / Math.Sqrt(maximum),
                        _ => Math.Log2(1 + count) / Math.Log2(1 + maximum),
                    };
                    var alpha = (byte)Math.Clamp(minimumAlpha + normalized * (255 - minimumAlpha), minimumAlpha, 255);
                    var (barX, barWidth) = TimelineBars.BarRect(
                        column,
                        last,
                        index,
                        geometry.Left,
                        geometry.Width,
                        values.Length,
                        _minimumBarWidth);
                    var x = barX;
                    var width = Math.Max(1, barWidth + (last > column ? 0.25 : 0));
                    if (_pixelSnap)
                    {
                        var right = Math.Round(x + width);
                        x = Math.Round(x);
                        width = Math.Max(1, right - x);
                    }

                    // Intensity is drawn twice: as opacity and as height above the row
                    // baseline. The second encoding is what makes a dense row read as a
                    // profile instead of a solid block, and it is what carries the value
                    // when color cannot (§12.5, §14.14).
                    var bandTop = geometry.Top + row * transform.RowHeight + 1;
                    var bandHeight = Math.Max(1, transform.RowHeight - 2);
                    var barHeight = Math.Max(1, bandHeight * TimelineBars.BarHeightFraction(normalized));
                    var rect = new Rect(x, bandTop + bandHeight - barHeight, width, barHeight);
                    context.FillRectangle(LevelPalette.Fill(level, alpha), rect);
                    context.DrawLine(
                        baselinePen,
                        new Point(rect.X, rect.Bottom - 1),
                        new Point(rect.Right, rect.Bottom - 1));
                }

                column = last;
            }

            // Sized from the lane it labels. A landscape phone gives each of six lanes about
            // ten pixels, and a fixed 12-pixel glyph in an 18-pixel chip then overflowed its
            // own lane, bled across the boundary, and sat half a row away from the stripe it
            // named — so the count beside the `E` could belong to either lane (finding 3b).
            var laneHeight = transform.RowHeight;
            var laneTop = geometry.Top + row * laneHeight;
            var labelSize = Math.Clamp(laneHeight - 3, 7, 12);
            var showCount = laneHeight >= 20;
            var chipHeight = Math.Max(5, laneHeight - 4);
            var chipTop = laneTop + (laneHeight - chipHeight) / 2;
            context.FillRectangle(
                LevelPalette.Fill(level, isDark ? (byte)54 : (byte)36),
                new Rect(10, chipTop, showCount ? 58 : 24, chipHeight));
            context.DrawLine(
                LevelPalette.AccentPen(level),
                new Point(10, chipTop),
                new Point(10, chipTop + chipHeight));
            DrawTextCentered(context, LevelPalette.Label(level), 19, laneTop, laneHeight, labelSize, foreground);
            if (showCount)
            {
                DrawTextCentered(context, CompactCount(rowTotal), 36, laneTop, laneHeight, 9, muted);
            }
        }

        // Spacing has to follow how wide the labels actually are: a whole-session view
        // prints a date and is three times the width of a time, so a fixed target packed
        // date labels into each other on a narrow plot. Measuring one sample is enough —
        // every label at a given span uses the same format.
        var sampleWidth = MeasureTextWidth(
            FormatTick(_result.Viewport.Range.StartInclusive, _result.Viewport.Range.DurationUs),
            10);
        var interval = NiceTicks.SelectInterval(
            _result.Viewport.Range,
            geometry.Width,
            Math.Max(145, sampleWidth + 24));

        // Every label at a given span uses the same fixed-width format, so one measurement
        // describes all of them and the overlap rule can be decided before anything is drawn.
        var labelY = geometry.Top + geometry.Height + 6;
        var labelEndpoints = TimelineAxis.UseEndpointLabels(
            _result.Viewport.Range,
            interval,
            geometry.Left,
            geometry.Width,
            sampleWidth);
        var lastLabelRight = double.NegativeInfinity;
        foreach (var instant in NiceTicks.Enumerate(_result.Viewport.Range, interval))
        {
            // Not while the axis is labelling its own ends: a gridline whose instant is
            // printed nowhere states that something regular is happening without saying what,
            // and a narrow portrait plot drew two to four of them under two end labels
            // (finding 21.8). Where tick labels are drawn, an occasional line whose text was
            // dropped for overlap still sits between two printed neighbours and reads.
            if (labelEndpoints)
            {
                continue;
            }

            var x = transform.InstantToX(instant);
            context.DrawLine(gridPen, new Point(x, geometry.Top), new Point(x, geometry.Top + geometry.Height + 4));

            var label = FormatTick(instant, _result.Viewport.Range.DurationUs);
            var labelX = TimelineAxis.LabelX(instant, _result.Viewport.Range, geometry.Left, geometry.Width, sampleWidth);

            // The gridline still marks every tick; only the text is dropped, because two
            // labels printed over each other read as neither.
            if (labelX < lastLabelRight)
            {
                continue;
            }

            DrawText(context, label, new Point(labelX, labelY), 10, foreground);
            lastLabelRight = labelX + sampleWidth + TimelineAxis.LabelGap;
        }

        if (labelEndpoints)
        {
            var span = _result.Viewport.Range;
            DrawText(context, FormatTick(span.StartInclusive, span.DurationUs), new Point(geometry.Left, labelY), 10, foreground);
            DrawText(
                context,
                FormatTick(span.EndExclusive, span.DurationUs),
                new Point(geometry.Left + geometry.Width - sampleWidth, labelY),
                10,
                foreground);
        }

        if (_searchResult is { } search)
        {
            var lastPixel = int.MinValue;
            foreach (var marker in search.Markers)
            {
                if (marker < _result.Viewport.Range.StartInclusive || marker >= _result.Viewport.Range.EndExclusive)
                {
                    continue;
                }

                // Markers are sorted, so deduplicating per device pixel needs only the
                // previously drawn position, not a per-frame hash set.
                var x = transform.InstantToX(marker);
                var pixel = (int)Math.Round(x);
                if (pixel != lastPixel)
                {
                    lastPixel = pixel;
                    context.DrawLine(SearchMarkerPen, new Point(x, geometry.Top + geometry.Height + 1), new Point(x, geometry.Top + geometry.Height + 6));
                }
            }
        }

        if (_rangeSelection is { } rangeSelection)
        {
            var x0 = transform.InstantToX(rangeSelection.StartInclusive);
            var x1 = transform.InstantToX(rangeSelection.EndExclusive);
            context.FillRectangle(RangeFill, new Rect(x0, geometry.Top, Math.Max(1, x1 - x0), geometry.Height));
            context.DrawRectangle(null, RangePen, new Rect(x0, geometry.Top, Math.Max(1, x1 - x0), geometry.Height));
        }

        if (_selection is { } selected && levels.Contains(selected.Level))
        {
            // A selected cell is often one device pixel wide; outlining it at that width
            // makes the selection invisible, so the mark is widened the same way the bars
            // themselves are.
            var (x0, selectionWidth) = TimelineBars.EnsureMinimumWidth(
                transform.InstantToX(selected.Range.StartInclusive),
                transform.InstantToX(selected.Range.EndExclusive),
                _minimumBarWidth + 2,
                geometry.Left,
                geometry.Width);
            var y = transform.LevelToY(selected.Level);
            context.DrawRectangle(
                null,
                App.HighContrastEnabled ? SelectionPenHighContrast : SelectionPen,
                new Rect(x0, y, selectionWidth, transform.RowHeight));
        }

        DrawSelectedEntryMark(context, transform, geometry, levels);

        if (_hoverColumn is { } hover && hover >= 0 && hover < _result.Columns.Count)
        {
            DrawHoverReadout(context, transform, geometry, levels, hover, columnWidth, normalizationLabel, isDark);
        }
    }

    /// <summary>
    /// Why there is nothing to plot. Both lines wrap: an import failure states its reason
    /// here, and a reason that runs off the right edge of the plot is no better than no
    /// reason at all (finding 10).
    /// </summary>
    private void DrawEmptyState(DrawingContext context, IBrush foreground, IBrush muted)
    {
        var width = Math.Max(80, Bounds.Width - 44);
        DrawText(context, "EVENT DENSITY", new Point(22, 20), 11, AccentBrush);
        var title = FormatWrapped(_emptyTitle, Math.Min(17, Math.Max(12, Bounds.Height / 9)), foreground, width);
        context.DrawText(title, new Point(22, 48));
        var detail = FormatWrapped(_emptyDetail, 12, muted, width);
        context.DrawText(detail, new Point(22, 52 + title.Height));
    }

    private static FormattedText FormatWrapped(string text, double size, IBrush brush, double maximumWidth) =>
        new(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            brush)
        {
            MaxTextWidth = maximumWidth,
            MaxLineCount = 6,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    /// <summary>
    /// Where the table's selected entry sits in time: a caret spanning every lane, a solid
    /// stub in the entry's own lane, and a marker in the gutter above the plot (§14.9). It
    /// is drawn in the entry's severity color, so it cannot be mistaken for the magenta
    /// search ticks or the white cell outline, and it names the lane to look in even when
    /// that lane is filtered away.
    ///
    /// An out-of-viewport indicator is deliberately absent: the entry table is queried over
    /// the viewport, so a listed — and therefore selectable — row is always inside it, and
    /// any viewport change re-queries the list and drops the selection with it. An arrow
    /// for that case would be unreachable chrome.
    /// </summary>
    private void DrawSelectedEntryMark(
        DrawingContext context,
        TimelineTransform transform,
        TimelineGeometry geometry,
        LogLevel[] levels)
    {
        if (_entryMarkInstant is not { } instant ||
            _result is null ||
            instant < _result.Viewport.Range.StartInclusive ||
            instant >= _result.Viewport.Range.EndExclusive)
        {
            return;
        }

        // A lane the severity filter has removed cannot hold a selectable row, so a caret
        // pointing into it marks an entry the table is not listing (finding 28).
        if (_entryMarkLevel is { } markLevel && Array.IndexOf(levels, markLevel) < 0)
        {
            return;
        }

        var level = _entryMarkLevel ?? LogLevel.Unknown;
        var brush = LevelPalette.BrushOf(level);
        var x = transform.InstantToX(instant);
        context.DrawLine(
            LevelPalette.CaretPen(level),
            new Point(x, geometry.Top),
            new Point(x, geometry.Top + geometry.Height));
        if (Array.IndexOf(levels, level) >= 0)
        {
            var laneTop = transform.LevelToY(level);
            context.FillRectangle(brush, new Rect(x - 1, laneTop + 1, 2, Math.Max(1, transform.RowHeight - 2)));
        }

        using (context.PushTransform(Matrix.CreateTranslation(x, geometry.Top - 7)))
        {
            context.DrawGeometry(brush, null, EntryMarkGlyph);
        }
    }

    /// <summary>
    /// Crosshair plus readout box for the hovered cell: exact half-open interval,
    /// total, per-level counts rendered in their own level colors, and — on the second
    /// line — what is actually under the pointer: the hovered row, its share of the cell,
    /// and the dominant pattern inside that one cell once the debounced query answers
    /// (§14.7). The second line is derived from the hovered cell, never from a
    /// viewport-wide ranking that would read identically for every bar.
    /// </summary>
    private void DrawHoverReadout(
        DrawingContext context,
        TimelineTransform transform,
        TimelineGeometry geometry,
        LogLevel[] levels,
        int hover,
        double columnWidth,
        string normalizationLabel,
        bool isDark)
    {
        var result = _result!;
        var range = result.Columns[hover];
        var x = geometry.Left + (hover + 0.5) * columnWidth;
        context.DrawLine(
            isDark ? DarkCrosshairPen : LightCrosshairPen,
            new Point(x, geometry.Top),
            new Point(x, geometry.Top + geometry.Height));

        long total = 0;
        LogLevel dominant = levels[0];
        long dominantCount = -1;
        foreach (var level in LogLevels.StorageOrder)
        {
            var count = result.Counts[level][hover];
            total += count;
            if (count > dominantCount)
            {
                dominantCount = count;
                dominant = level;
            }
        }

        var intervalText =
            $"{FormatTick(range.StartInclusive, range.DurationUs)} → {FormatTick(range.EndExclusive, range.DurationUs)}  ·  Σ {total:N0}  ";
        var templateText = HoverDetailText(range, total, normalizationLabel);

        // Per-level counts are drawn as individually colored runs, so measure them
        // per segment to know the total width first.
        var countWidth = 0d;
        Span<double> segmentWidths = stackalloc double[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            segmentWidths[i] = MeasureTextWidth(CountSegment(levels[i], result.Counts[levels[i]][hover]), 10);
            countWidth += segmentWidths[i];
        }

        var firstLineWidth = MeasureTextWidth(intervalText, 10) + countWidth;
        var boxWidth = Math.Min(
            Bounds.Width - 16,
            Math.Max(320, Math.Max(firstLineWidth, MeasureTextWidth(templateText, 10)) + 20));
        var boxX = Math.Clamp(x - boxWidth / 2, 8, Bounds.Width - boxWidth - 8);
        context.FillRectangle(HoverBoxFill, new Rect(boxX, 3, boxWidth, 46));
        context.DrawRectangle(null, LevelPalette.BaselinePen(total > 0 ? dominant : LogLevel.Info), new Rect(boxX, 3, boxWidth, 46));
        DrawText(context, intervalText, new Point(boxX + 10, 9), 10, Brushes.White);
        var runX = boxX + 10 + MeasureTextWidth(intervalText, 10);
        for (var i = 0; i < levels.Length; i++)
        {
            if (runX + segmentWidths[i] > boxX + boxWidth - 6)
            {
                break;
            }

            DrawText(
                context,
                CountSegment(levels[i], result.Counts[levels[i]][hover]),
                new Point(runX, 9),
                10,
                LevelPalette.BrushOf(levels[i]));
            runX += segmentWidths[i];
        }

        DrawText(context, templateText, new Point(boxX + 10, 26), 10, HoverBoxMuted);
    }

    private static string CountSegment(LogLevel level, long count) =>
        $"{LevelPalette.Label(level)}:{count:N0}  ";

    /// <summary>
    /// Second readout line. Everything before the pattern is computed from the hovered
    /// cell itself, so the line always changes as the pointer moves even while the
    /// pattern query is still in flight or the session has no mined templates.
    /// </summary>
    private string HoverDetailText(TimeRange range, long total, string normalizationLabel)
    {
        if (_result is null || _hoverColumn is not { } column || _hoverLevel is not { } level)
        {
            return $"{normalizationLabel.ToLowerInvariant()} {_intensityScale.ToLowerInvariant()} intensity";
        }

        var label = LevelPalette.Label(level);
        var count = _result.Counts[level][column];
        if (count == 0)
        {
            return $"{label} · no entries in this cell · click to inspect the interval";
        }

        var share = total == 0 ? 0 : count / (double)total;
        var head = $"{label} · {count:N0} of {total:N0} in cell · {share:P0}";
        if (_hoverInsight is not { } insight || insight.Range != range || insight.Level != level)
        {
            return $"{head} · reading cell pattern…";
        }

        return insight.TemplateText is { Length: > 0 } text
            ? $"{head} · top {insight.TemplateCount:N0}× {Shorten(text, 62)}"
            : $"{head} · no mined pattern in this cell";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_result is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed &&
            _searchResult is { Markers.Count: > 0 } &&
            Geometry() is { } markerGeometry &&
            IsInMarkerLane(point, markerGeometry))
        {
            var transform = new TimelineTransform(_result.Viewport.Range, markerGeometry, _displayLevels);
            SearchMarkerPicked?.Invoke(this, transform.XToInstant(point.X));
            _suppressCellSelection = true;
            e.Handled = true;
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            _rangeOrigin = point;
            _rangeSelection = null;
            e.Pointer.Capture(this);
        }
        else if (properties.IsLeftButtonPressed)
        {
            if (TryHandleDoubleTap(point, e.ClickCount))
            {
                e.Handled = true;
                return;
            }

            _dragOrigin = point;
            _dragViewport = _result.Viewport.Range;
            e.Pointer.Capture(this);
        }
    }

    /// <summary>
    /// Applies the double-tap gesture independently of the platform's wall-clock click detector.
    /// Keeping the state transition here lets headless tests supply the recognized click count
    /// directly; a busy test host must not turn the gesture contract into a timing lottery.
    /// </summary>
    internal bool TryHandleDoubleTap(Point point, int clickCount)
    {
        if (clickCount < 2 ||
            _result is null ||
            _sessionRange is not { } session ||
            Geometry() is not { } geometry)
        {
            return false;
        }

        ViewportChanged?.Invoke(
            this,
            ZoomViewport(_result.Viewport.Range, geometry, session, point.X, 0.5));

        // The release that ends this tap still arrives, and a release that did not
        // move used to be read as "select this cell" — so one double-tap both zoomed
        // and silently re-scoped the entry list to a single bar, complete with a
        // "× Cell" chip the reader never asked for. One gesture, one meaning
        // (finding 4).
        _suppressCellSelection = true;
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var current = e.GetPosition(this);
        if (_rangeOrigin is { } rangeOrigin && _result is not null && Geometry() is { } rangeGeometry)
        {
            var rangeTransform = new TimelineTransform(_result.Viewport.Range, rangeGeometry, _displayLevels);
            var first = rangeTransform.XToInstant(Math.Clamp(rangeOrigin.X, rangeGeometry.Left, rangeGeometry.Left + rangeGeometry.Width));
            var second = rangeTransform.XToInstant(Math.Clamp(current.X, rangeGeometry.Left, rangeGeometry.Left + rangeGeometry.Width));
            var start = first <= second ? first : second;
            var end = first <= second ? second : first;
            if (end == start)
            {
                end = new InstantUs(start.Value + 1);
            }

            _rangeSelection = new TimeRange(start, end);
            InvalidateVisual();
            return;
        }

        if (_dragOrigin is not { } origin || _dragViewport is not { } viewport || _sessionRange is not { } session)
        {
            UpdateHover(current);
            return;
        }

        var geometry = Geometry();
        if (geometry is null)
        {
            return;
        }

        var transform = new TimelineTransform(viewport, geometry.Value, _displayLevels);
        ViewportChanged?.Invoke(this, transform.Pan(current.X - origin.X, session));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_rangeOrigin is not null)
        {
            _rangeOrigin = null;
            e.Pointer.Capture(null);
            if (_rangeSelection is { } selected)
            {
                RangeSelected?.Invoke(this, selected);
            }

            return;
        }

        var moved = _dragOrigin is { } origin && Math.Abs(e.GetPosition(this).X - origin.X) > 3;
        var consumedByZoom = _suppressCellSelection;
        _suppressCellSelection = false;
        _dragOrigin = null;
        _dragViewport = null;
        e.Pointer.Capture(null);
        if (!moved && !consumedByZoom)
        {
            SelectCell(e.GetPosition(this));
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragOrigin is null && _rangeOrigin is null)
        {
            ClearHover();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_result is null || _sessionRange is not { } session || Geometry() is not { } geometry)
        {
            return;
        }

        var factor = Math.Pow(1.18, -e.Delta.Y);
        ViewportChanged?.Invoke(this, ZoomViewport(_result.Viewport.Range, geometry, session, e.GetPosition(this).X, factor));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_result is null || _sessionRange is not { } session || Geometry() is not { } geometry)
        {
            return;
        }

        var transform = new TimelineTransform(_result.Viewport.Range, geometry, _displayLevels);
        if (e.Key == Key.F && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            FollowRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchFocusRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ExportRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.J or Key.K)
        {
            EntryNavigationRequested?.Invoke(this, e.Key == Key.J ? 1 : -1);
            e.Handled = true;
            return;
        }

        TimeRange? next = e.Key switch
        {
            Key.Left => transform.Pan(geometry.Width * 0.1, session),
            Key.Right => transform.Pan(-geometry.Width * 0.1, session),
            Key.Add or Key.OemPlus => ZoomViewport(_result.Viewport.Range, geometry, session, geometry.Left + geometry.Width / 2, 0.8),
            Key.Subtract or Key.OemMinus => ZoomViewport(_result.Viewport.Range, geometry, session, geometry.Left + geometry.Width / 2, 1.25),
            Key.D0 or Key.NumPad0 => session,
            Key.Home => new TimeRange(session.StartInclusive, new InstantUs(session.StartInclusive.Value + _result.Viewport.Range.DurationUs)),
            Key.End => new TimeRange(new InstantUs(session.EndExclusive.Value - _result.Viewport.Range.DurationUs), session.EndExclusive),
            _ => null,
        };
        if (next is { } range)
        {
            ViewportChanged?.Invoke(this, range);
            e.Handled = true;
        }
    }

    private void UpdateHover(Point point)
    {
        if (_result is null || Geometry() is not { } geometry ||
            point.X < geometry.Left || point.X >= geometry.Left + geometry.Width ||
            point.Y < geometry.Top || point.Y >= geometry.Top + geometry.Height)
        {
            ClearHover();
            return;
        }

        var transform = new TimelineTransform(_result.Viewport.Range, geometry, _displayLevels);
        var level = transform.YToLevel(point.Y);
        if (level is null)
        {
            ClearHover();
            return;
        }

        var column = ColumnAt(point.X, geometry, level.Value);
        if (_hoverColumn == column && _hoverLevel == level)
        {
            return;
        }

        _hoverColumn = column;
        _hoverLevel = level;
        HoverChanged?.Invoke(
            this,
            new TimelineCellSelection(_result.Columns[column], level.Value, _result.Counts[level.Value][column]));
        InvalidateVisual();
    }

    private void ClearHover()
    {
        if (_hoverColumn is null && _hoverLevel is null)
        {
            return;
        }

        _hoverColumn = null;
        _hoverLevel = null;
        _hoverInsight = null;
        HoverChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    /// <summary>
    /// Column under a device x, snapped onto the nearest occupied column of
    /// <paramref name="level"/>'s row. Hover and click share this so the readout always
    /// describes the cell a click would select, and a one-pixel bar stays reachable
    /// without the user hunting for its exact pixel (§14.5).
    /// </summary>
    private int ColumnAt(double x, TimelineGeometry geometry, LogLevel level)
    {
        var result = _result!;
        var columns = result.Columns.Count;
        var raw = Math.Clamp((int)((x - geometry.Left) / geometry.Width * columns), 0, columns - 1);
        return TimelineBars.SnapToOccupiedColumn(
            result.Counts[level],
            raw,
            geometry.Width / columns,
            TimelineBars.SnapRadiusPixels(_minimumBarWidth));
    }

    private void SelectCell(Point point)
    {
        if (_result is null || Geometry() is not { } geometry)
        {
            return;
        }

        var transform = new TimelineTransform(_result.Viewport.Range, geometry, _displayLevels);
        var level = transform.YToLevel(point.Y);
        if (level is null || point.X < geometry.Left || point.X >= geometry.Left + geometry.Width)
        {
            return;
        }

        var column = ColumnAt(point.X, geometry, level.Value);
        _selection = new TimelineCellSelection(_result.Columns[column], level.Value, _result.Counts[level.Value][column]);
        CellSelected?.Invoke(this, _selection);
        InvalidateVisual();
    }

    /// <summary>
    /// The height the plot would like: both reserved bands plus a lane band tall enough to
    /// read. A star-sized row is free to give it less — and then the plot, not the axis, is
    /// what shrinks.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        _ = base.MeasureOverride(availableSize);
        return new Size(0, HeaderBandHeight + AxisBandHeight + 144);
    }

    /// <summary>
    /// Splits the control into the header band, the lane band, and the axis band. The two
    /// bands give way proportionally in a short viewport so that whatever height there is,
    /// the labels stay inside the control instead of spilling over whatever is drawn next.
    /// </summary>
    private TimelineGeometry? Geometry()
    {
        if (Bounds.Width < 120)
        {
            return null;
        }

        var header = Math.Min(HeaderBandHeight, Bounds.Height * 0.22);
        var axis = Math.Min(AxisBandHeight, Bounds.Height * 0.26);
        var lanes = Bounds.Height - header - axis;
        return lanes < MinimumLaneBandHeight
            ? null
            : new TimelineGeometry(76, header, Math.Max(1, Bounds.Width - 88), lanes);
    }

    /// <summary>
    /// Whether a press landed in the band the search markers are drawn in.
    /// </summary>
    /// <remarks>
    /// The drawn ticks are five pixels tall, which is not a target; the band is the target,
    /// and it is the full width of the plot because the reader is aiming at a time, not at a
    /// hairline. The <see cref="TouchTarget"/> floor is applied downward from the lane so the
    /// axis labels below it keep their own room.
    /// </remarks>
    private static bool IsInMarkerLane(Point point, TimelineGeometry geometry)
    {
        var top = geometry.Top + geometry.Height;
        return point.Y >= top && point.Y < top + MarkerLaneTargetHeight &&
               point.X >= geometry.Left && point.X <= geometry.Left + geometry.Width;
    }

    /// <summary>How far below the lanes a tap still counts as aiming at a marker.</summary>
    private const double MarkerLaneTargetHeight = 22;

    private long MinimumSpan(TimelineGeometry geometry) =>
        TimelineTransform.MinimumSpanUs(
            geometry.Width * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1),
            _minimumUsPerPixel);

    /// <summary>
    /// The one place effective zoom bounds are built, for every gesture and key that zooms.
    /// </summary>
    /// <remarks>
    /// A session holding a single entry spans about two microseconds, while the plot's own
    /// pixel resolution puts the narrowest useful window near a millisecond — so the largest
    /// allowed span was smaller than the smallest allowed one.
    /// <see cref="TimelineTransform.Zoom"/> is right to refuse that inverted contract, and it
    /// refuses it by throwing; thrown from a touch handler, that ends the process. The wheel
    /// route guarded the relationship for itself, and double-tap, pinch, keyboard +/- and
    /// <see cref="ZoomAtCenter"/> each passed the raw session span straight through, so one
    /// double-tap during the first progressive entry killed the app (finding F-20). The floor
    /// wins when the two disagree: a session shorter than one physically meaningful span keeps
    /// the Fit-expanded window and zooming in becomes a bounded no-op. Building the pair here
    /// rather than at five call sites is the point — a sixth zoom route cannot omit the guard.
    /// </remarks>
    private (long Minimum, long Maximum) ZoomBounds(TimelineGeometry geometry, TimeRange session)
    {
        var minimum = MinimumSpan(geometry);
        return (minimum, Math.Max(minimum, MaximumSpan(session)));
    }

    private TimeRange ZoomViewport(
        TimeRange viewport,
        TimelineGeometry geometry,
        TimeRange session,
        double focusX,
        double factor)
    {
        var (minimum, maximum) = ZoomBounds(geometry, session);
        var effective = viewport.DurationUs >= minimum
            ? viewport
            : new TimeRange(
                new InstantUs(checked(viewport.EndExclusive.Value - minimum)),
                viewport.EndExclusive);
        return new TimelineTransform(effective, geometry, _displayLevels)
            .Zoom(focusX, factor, minimum, maximum, session);
    }

    private static long MaximumSpan(TimeRange session) =>
        Math.Max(1, checked((long)Math.Ceiling(session.DurationUs * 1.1)));

    /// <summary>
    /// One label of the time axis, at the precision the span can actually distinguish.
    /// </summary>
    /// <remarks>
    /// Milliseconds were printed for every span up to a day, so a twenty-minute view drew
    /// <c>.000</c> under every tick — three characters of nothing, on the axis where width is
    /// most expensive (audit 2, E6). The precision follows the span because the labels have
    /// to stay one fixed width: the axis measures one of them and lays out all of them from
    /// that, and a format that varied per label would break the overlap rule that keeps them
    /// from being drawn over each other.
    /// </remarks>
    private string FormatTick(InstantUs instant, long spanUs)
    {
        var time = TimeZoneInfo.ConvertTime(instant.ToDateTimeOffset(), _timeZone);
        return spanUs switch
        {
            < 1_000_000 => time.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            < 60_000_000 => time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            < 86_400_000_000 => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            _ => time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
    }

    private static string FormatDuration(long durationUs) =>
        durationUs switch
        {
            < 1_000 => $"{durationUs:N0} µs",
            < 1_000_000 => $"{durationUs / 1_000d:0.###} ms",
            < 60_000_000 => $"{durationUs / 1_000_000d:0.###} s",
            < 3_600_000_000 => $"{durationUs / 60_000_000d:0.##} min",
            < 86_400_000_000 => $"{durationUs / 3_600_000_000d:0.##} h",
            _ => $"{durationUs / 86_400_000_000d:0.##} d",
        };

    private static string FormatResolution(double microsecondsPerPixel) =>
        microsecondsPerPixel switch
        {
            < 0.01 => $"{microsecondsPerPixel * 1_000:0.##} ns/px",
            < 1 => $"{microsecondsPerPixel:0.###} µs/px",
            < 1_000 => $"{microsecondsPerPixel:0.#} µs/px",
            < 1_000_000 => $"{microsecondsPerPixel / 1_000:0.##} ms/px",
            _ => $"{microsecondsPerPixel / 1_000_000:0.##} s/px",
        };

    private static string CompactCount(long count) =>
        count switch
        {
            >= 1_000_000_000 => $"{count / 1_000_000_000d:0.#}b",
            >= 1_000_000 => $"{count / 1_000_000d:0.#}m",
            >= 1_000 => $"{count / 1_000d:0.#}k",
            _ => count.ToString(CultureInfo.InvariantCulture),
        };

    private static StreamGeometry BuildEntryMarkGlyph()
    {
        var glyph = new StreamGeometry();
        using (var sink = glyph.Open())
        {
            sink.BeginFigure(new Point(-5, 0), true);
            sink.LineTo(new Point(5, 0));
            sink.LineTo(new Point(0, 7));
            sink.EndFigure(true);
        }

        return glyph;
    }

    private static string Shorten(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";

    /// <summary>Draws one label vertically centred in the band it belongs to.</summary>
    private static void DrawTextCentered(
        DrawingContext context,
        string text,
        double x,
        double bandTop,
        double bandHeight,
        double size,
        IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            brush);
        context.DrawText(formatted, new Point(x, bandTop + Math.Max(0, (bandHeight - formatted.Height) / 2)));
    }

    private static void DrawText(DrawingContext context, string text, Point point, double size, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            brush);
        context.DrawText(formatted, point);
    }

    private static double MeasureTextWidth(string text, double size)
    {
        var formatted = new FormattedText(
            text,
            DisplayCulture.Current,
            FlowDirection.LeftToRight,
            MonoTypeface,
            size,
            Brushes.White);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
