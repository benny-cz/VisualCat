using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Timeline;

public sealed class MinimapControl : Control
{
    private static readonly IBrush DarkBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#101A2A"), 0),
            new GradientStop(Color.Parse("#080D16"), 1),
        },
    };
    private static readonly IBrush LightBackgroundBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#EDF2FA"), 0),
            new GradientStop(Color.Parse("#DDE7F3"), 1),
        },
    };
    private static readonly IBrush BrushFill = new SolidColorBrush(Color.Parse("#3043B4FF"));
    private static readonly Pen BrushBorder = new(new SolidColorBrush(Color.Parse("#D343B4FF")), 2);
    private HeatMapResult? _overview;
    private long[] _totals = [];
    private long _maximum = 1;
    private TimeRange? _sessionRange;
    private TimeRange? _viewport;
    private DragMode _dragMode;
    private Point _dragOrigin;
    private TimeRange _dragViewport;

    public MinimapControl()
    {
        Focusable = true;

        // Low enough to be a floor rather than a demand. It was 54, which is 152 device pixels
        // on a 2.8x phone, and the row it is given in the short/landscape composition is 26 dp:
        // the control took the 54 either way and overflowed its frame by 43 px above and 44 px
        // below, painting the status line's own text through its bars (audit 3, D1). The frame
        // decides how much room there is; Render degrades into whatever it gets.
        MinHeight = 14;
        ClipToBounds = true;
        AutomationProperties.SetName(this, "Full-session minimap and viewport brush");
        AutomationProperties.SetHelpText(this, "Drag the brush to pan; drag either edge to resize the timeline viewport.");

        // Both palettes are resolved inside Render, so a variant change is a repaint and
        // nothing more -- but nothing was asking for the repaint, so the plot kept the
        // variant it happened to be drawn in until a pan or a new snapshot moved it
        // (audit 2, A1b).
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public event EventHandler<TimeRange>? ViewportChanged;

    public void SetResult(HeatMapResult? overview, TimeRange? viewport, TimeRange? sessionRange)
    {
        if (!ReferenceEquals(_overview, overview))
        {
            RebuildDensityCache(overview);
        }

        _overview = overview;
        _viewport = viewport;
        _sessionRange = sessionRange;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var isDark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        context.FillRectangle(isDark ? DarkBackgroundBrush : LightBackgroundBrush, Bounds);
        if (_overview is null || _sessionRange is not { } session || _viewport is not { } viewport || Bounds.Width <= 2)
        {
            return;
        }

        var errors = _overview.Counts[LogLevel.Error];
        var fatals = _overview.Counts[LogLevel.Fatal];
        var columnWidth = Bounds.Width / Math.Max(1, _totals.Length);

        // Every fixed inset in this method is a share of the band instead, so a 26 dp row
        // draws the same picture as a 48 dp one, smaller — rather than the same picture,
        // overflowing. The pulse tick and the headroom above the bars were the two that could
        // consume a short band entirely.
        var pulse = Math.Clamp(Bounds.Height * 0.11, 1, 3);
        var headroom = Math.Clamp(Bounds.Height * 0.12, 1, 6);
        for (var column = 0; column < _totals.Length; column++)
        {
            var height = Math.Log2(1 + _totals[column]) / Math.Log2(1 + _maximum) * Math.Max(1, Bounds.Height - headroom);
            var dominant = DominantLevel(_overview, column);
            context.FillRectangle(
                LevelPalette.Fill(dominant, 220),
                new Rect(column * columnWidth, Bounds.Height - height, Math.Max(1, columnWidth + 0.25), height));

            // Severity pulse: a lone fatal drowned by thousands of verbose lines would
            // otherwise be invisible because the density bar takes the dominant color.
            // A tick along the top edge keeps rare severe events findable (§14.8).
            if (fatals[column] > 0)
            {
                context.FillRectangle(
                    LevelPalette.BrushOf(LogLevel.Fatal),
                    new Rect(column * columnWidth, 0, Math.Max(1, columnWidth + 0.25), pulse));
            }
            else if (errors[column] > 0)
            {
                context.FillRectangle(
                    LevelPalette.BrushOf(LogLevel.Error),
                    new Rect(column * columnWidth, 0, Math.Max(1, columnWidth + 0.25), pulse));
            }
        }

        var clipped = ClipToSession(viewport, session);
        var (left, right) = Transform(session).RangeToXInterval(clipped);
        var inset = Math.Min(1, Bounds.Height / 8);
        context.FillRectangle(BrushFill, new Rect(left, inset, Math.Max(2, right - left), Math.Max(1, Bounds.Height - (inset * 2))));
        context.DrawRectangle(null, BrushBorder, new Rect(left, inset, Math.Max(2, right - left), Math.Max(1, Bounds.Height - (inset * 2))));

        // The grip marks say the brush edges can be dragged, and a mark taller than the band
        // it is drawn in says it about a control that is not there.
        var grip = Math.Min(6, Bounds.Height / 3);
        context.DrawLine(BrushBorder, new Point(left + 4, (Bounds.Height / 2) - grip), new Point(left + 4, (Bounds.Height / 2) + grip));
        context.DrawLine(BrushBorder, new Point(right - 4, (Bounds.Height / 2) - grip), new Point(right - 4, (Bounds.Height / 2) + grip));
    }

    private void RebuildDensityCache(HeatMapResult? overview)
    {
        if (overview is null)
        {
            _totals = [];
            _maximum = 1;
            return;
        }

        _totals = new long[overview.Columns.Count];
        foreach (var values in overview.Counts.Values)
        {
            for (var column = 0; column < _totals.Length; column++)
            {
                _totals[column] += values[column];
            }
        }

        _maximum = 1;
        for (var column = 0; column < _totals.Length; column++)
        {
            _maximum = Math.Max(_maximum, _totals[column]);
        }
    }

    private static LogLevel DominantLevel(HeatMapResult result, int column)
    {
        var dominant = LogLevel.Info;
        long maximum = -1;
        foreach (var level in LogLevels.DisplayOrder)
        {
            var count = result.Counts[level][column];
            if (count > maximum)
            {
                maximum = count;
                dominant = level;
            }
        }

        return dominant;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_sessionRange is not { } session || _viewport is not { } viewport)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var (left, right) = Transform(session).RangeToXInterval(viewport);
        _dragMode = Math.Abs(point.X - left) <= 9
            ? DragMode.ResizeLeft
            : Math.Abs(point.X - right) <= 9
                ? DragMode.ResizeRight
                : point.X >= left && point.X <= right
                    ? DragMode.Move
                    : DragMode.Center;
        _dragOrigin = point;
        _dragViewport = viewport;
        e.Pointer.Capture(this);
        if (_dragMode == DragMode.Center)
        {
            RaiseCenteredViewport(point.X, session, viewport);
            _dragMode = DragMode.Move;
            _dragViewport = _viewport ?? viewport;
            _dragOrigin = point;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragMode == DragMode.None || _sessionRange is not { } session || Bounds.Width <= 0)
        {
            return;
        }

        var delta = (long)Math.Round((e.GetPosition(this).X - _dragOrigin.X) / Bounds.Width * session.DurationUs);
        var minimumSpan = Math.Max(1, (long)Math.Ceiling(session.DurationUs / Math.Max(1, Bounds.Width) * 2));
        TimeRange next;
        switch (_dragMode)
        {
            case DragMode.ResizeLeft:
                {
                    var start = Math.Clamp(
                        checked(_dragViewport.StartInclusive.Value + delta),
                        session.StartInclusive.Value,
                        _dragViewport.EndExclusive.Value - minimumSpan);
                    next = new TimeRange(new InstantUs(start), _dragViewport.EndExclusive);
                    break;
                }
            case DragMode.ResizeRight:
                {
                    var end = Math.Clamp(
                        checked(_dragViewport.EndExclusive.Value + delta),
                        _dragViewport.StartInclusive.Value + minimumSpan,
                        session.EndExclusive.Value);
                    next = new TimeRange(_dragViewport.StartInclusive, new InstantUs(end));
                    break;
                }
            default:
                {
                    var span = Math.Min(_dragViewport.DurationUs, session.DurationUs);
                    var start = Math.Clamp(
                        checked(_dragViewport.StartInclusive.Value + delta),
                        session.StartInclusive.Value,
                        session.EndExclusive.Value - span);
                    next = new TimeRange(new InstantUs(start), new InstantUs(start + span));
                    break;
                }
        }

        _viewport = next;
        ViewportChanged?.Invoke(this, next);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
    }

    private void RaiseCenteredViewport(double x, TimeRange session, TimeRange viewport)
    {
        var span = Math.Min(viewport.DurationUs, session.DurationUs);
        var center = Transform(session).XToInstant(x).Value;
        var start = Math.Clamp(center - span / 2, session.StartInclusive.Value, session.EndExclusive.Value - span);
        var next = new TimeRange(new InstantUs(start), new InstantUs(start + span));
        _viewport = next;
        ViewportChanged?.Invoke(this, next);
        InvalidateVisual();
    }

    private TimelineTransform Transform(TimeRange session) =>
        new(session, new TimelineGeometry(0, 0, Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height)), [LogLevel.Info]);

    private static TimeRange ClipToSession(TimeRange range, TimeRange session)
    {
        var start = Math.Clamp(range.StartInclusive.Value, session.StartInclusive.Value, session.EndExclusive.Value);
        var end = Math.Clamp(range.EndExclusive.Value, start, session.EndExclusive.Value);
        return new TimeRange(new InstantUs(start), new InstantUs(end));
    }

    private enum DragMode : byte
    {
        None,
        Move,
        ResizeLeft,
        ResizeRight,
        Center,
    }
}
