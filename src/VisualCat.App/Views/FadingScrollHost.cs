using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VisualCat.App.Timeline;

namespace VisualCat.App.Views;

/// <summary>
/// A scrolling surface whose clipped edge reads as a boundary rather than as damage.
/// </summary>
/// <remarks>
/// In landscape the filter drawer sliced its last two 48 dp buttons a third of the way up
/// against the pinned Reset/Done row, and <em>Appearance &amp; timeline</em> and
/// <em>Session cache</em> did the same to their last control. All three of them scroll — the
/// content was reachable — but nothing on screen said so, and a row cut mid-glyph against a
/// hard edge reads as a rendering fault, not as an invitation to drag (audit 3, D2).
///
/// A fade is the smallest honest statement of "this continues": the last visible row dissolves
/// instead of being guillotined, it costs no layout, and it appears only in the state it is
/// about. The touch scroll indicator says the same thing on the other axis, and the two
/// together are what a phone gets instead of a scrollbar with arrows on it.
/// </remarks>
internal sealed class FadingScrollHost : Grid
{
    private const double FadeExtent = 22;

    private readonly ScrollViewer _scroller;
    private readonly bool _horizontal;
    private readonly Border _start;
    private readonly Border _end;

    /// <param name="scroller">The surface whose edges are being described.</param>
    /// <param name="dark">Which variant the fade has to disappear into.</param>
    /// <param name="horizontal">
    /// Fade the left and right edges instead of the top and bottom — for the session strip,
    /// which scrolls sideways.
    /// </param>
    internal FadingScrollHost(ScrollViewer scroller, bool dark, bool horizontal = false)
    {
        _scroller = scroller ?? throw new ArgumentNullException(nameof(scroller));
        _horizontal = horizontal;
        _start = EdgeBand(horizontal, atStart: true);
        _end = EdgeBand(horizontal, atStart: false);
        Children.Add(_scroller);
        Children.Add(_start);
        Children.Add(_end);
        ApplyTheme(dark);

        _scroller.ScrollChanged += (_, _) => Update();

        // The offset does not change when the content grows under a stationary scroller, and
        // that is exactly when a surface becomes clipped for the first time.
        _scroller.LayoutUpdated += (_, _) => Update();
        Update();
    }

    /// <summary>Repaints the bands for the variant in force.</summary>
    /// <remarks>
    /// A fade is the surface it fades into, so it cannot be resolved once and kept: the same
    /// gradient that vanishes into a midnight card is a grey smear on a white one.
    /// </remarks>
    internal void ApplyTheme(bool dark)
    {
        var surface = _horizontal
            ? WorkspacePalette.Surface(dark)
            : WorkspacePalette.SurfaceRaised(dark);
        _start.Background = Gradient(surface, _horizontal, atStart: true);
        _end.Background = Gradient(surface, _horizontal, atStart: false);
    }

    private static Border EdgeBand(bool horizontal, bool atStart) => new()
    {
        Width = horizontal ? FadeExtent : double.NaN,
        Height = horizontal ? double.NaN : FadeExtent,
        HorizontalAlignment = horizontal
            ? atStart ? HorizontalAlignment.Left : HorizontalAlignment.Right
            : HorizontalAlignment.Stretch,
        VerticalAlignment = horizontal
            ? VerticalAlignment.Stretch
            : atStart ? VerticalAlignment.Top : VerticalAlignment.Bottom,

        // Decoration over a scrolling surface: it must never take the drag it is describing,
        // and it is not a thing a screen reader has any use for.
        IsHitTestVisible = false,
        IsVisible = false,
        [AutomationProperties.AccessibilityViewProperty] = Avalonia.Automation.AccessibilityView.Raw,
    };

    private static LinearGradientBrush Gradient(Color surface, bool horizontal, bool atStart)
    {
        var near = atStart ? 0d : 1d;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(horizontal ? near : 0, horizontal ? 0 : near, RelativeUnit.Relative),
            EndPoint = new RelativePoint(horizontal ? 1 - near : 0, horizontal ? 0 : 1 - near, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(surface, 0),
                new GradientStop(WorkspacePalette.Tint(surface, 0), 1),
            },
        };
    }

    private void Update()
    {
        var extent = _horizontal ? _scroller.Extent.Width : _scroller.Extent.Height;
        var viewport = _horizontal ? _scroller.Viewport.Width : _scroller.Viewport.Height;
        var offset = _horizontal ? _scroller.Offset.X : _scroller.Offset.Y;
        var scrollable = extent - viewport > 0.5;
        SetBand(_start, scrollable && offset > 0.5);
        SetBand(_end, scrollable && offset < extent - viewport - 0.5);
    }

    private static void SetBand(Border band, bool visible)
    {
        // Guarded because this runs from LayoutUpdated, and an unguarded write would
        // invalidate the layout it was just told about.
        if (band.IsVisible != visible)
        {
            band.IsVisible = visible;
        }
    }
}
