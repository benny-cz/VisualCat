using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VisualCat.App.Timeline;

namespace VisualCat.App.Platform;

/// <summary>
/// Keeps the platform's own edge gestures out of the controls whose main gesture is a
/// horizontal drag.
/// </summary>
/// <remarks>
/// <para>
/// On a phone using gesture navigation, a swipe that begins within about 30 dp of the left or
/// right edge is the system's Back gesture, and the app never sees it. The heat map runs to
/// 12 dp of both edges and its documented gesture is "drag to pan", so on the third device a
/// pan started anywhere in the plot's outer 49 px did not pan — it went Back, and with no
/// overlay open that left the workspace for the home screen (finding F-28). The minimap
/// brush, whose description explicitly invites an edge drag, had the same problem on the
/// right.
/// </para>
/// <para>
/// Insetting the plot away from the strips would cost 60 dp of a 393 dp screen on the one
/// axis the plot exists to show. Android's answer is
/// <c>View.setSystemGestureExclusionRects</c>: the app names the rectangles where its own
/// gesture wins. The budget is 200 dp of exclusion height per edge, so this deliberately
/// covers only the surfaces a drag is the whole point of, and leaves Back working everywhere
/// else on the screen, including the whole entries list.
/// </para>
/// <para>
/// The phone divider joined them once its target became the whole boundary rather than a
/// pill in the middle (§24.3). On the Pixel the system's own strips are 82 px — 29.8 dp —
/// wide, and the divider reaches 17.8 dp into each of them, so grabbing the line near either
/// end and pulling it down with any sideways component went Back and left the app entirely:
/// F-28 again, on the control built after it was fixed. Neither device §§24–26 ran on could
/// see it, because both use three-button navigation. The divider claims only its grab band —
/// 20 dp, not its 48 dp target — because that band is the only part of it a drag can start
/// in away from the centred grip.
/// </para>
/// <para>
/// Nothing here runs on a platform that has not installed
/// <see cref="PlatformSourceRegistry.SetGestureExclusions"/>, so the desktop pays one null
/// check per layout pass.
/// </para>
/// </remarks>
/// <summary>
/// A tracked control that claims less than its whole rectangle, or claims it ahead of the
/// larger surfaces.
/// </summary>
/// <remarks>
/// The plot and the minimap are draggable everywhere inside themselves, so their whole
/// rectangle is the honest claim. The phone divider is not: only a thin band along the
/// boundary is grabbable away from its centred grip, and that band is all that needs to win
/// against the platform. Claiming the divider's whole 48 dp would spend nearly two and a half
/// times the budget on area no finger can start a drag in — budget the plot then loses.
/// </remarks>
internal interface IEdgeGestureSurface
{
    /// <summary>
    /// The part of the control, in its own coordinates, whose drag must beat the platform's
    /// edge gesture. An empty rectangle claims nothing.
    /// </summary>
    Rect EdgeGestureArea { get; }

    /// <summary>
    /// Whether the claim is small enough — and the consequence of losing it severe enough —
    /// that it must be granted whole before the larger surfaces are served.
    /// </summary>
    bool ClaimedWhole { get; }
}

public static class EdgeGestureGuard
{
    internal const double MaximumExclusionHeightDp = 200;
    private static readonly List<WeakReference<Control>> Tracked = [];
    private static IReadOnlyList<PixelRect> _published = [];
    private static bool _scheduled;
    private static bool _forcePublish;

    /// <summary>
    /// Declares that this control's horizontal drag matters more than the platform's edge
    /// gesture wherever the two overlap.
    /// </summary>
    public static void Track(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (PlatformSourceRegistry.SetGestureExclusions is null)
        {
            return;
        }

        Tracked.Add(new WeakReference<Control>(control));

        // Layout, not just size: the plot keeps its size and moves when the notice lane
        // appears, when the mode strip changes, and when the drawer opens, and an exclusion
        // rectangle left at the old place is worse than none — it would take the gesture away
        // from a strip of screen that no longer holds the plot.
        control.LayoutUpdated += (_, _) => Schedule();
        control.AttachedToVisualTree += (_, _) => Schedule();
        control.DetachedFromVisualTree += (_, _) => Schedule();
        Schedule();
    }

    /// <summary>Forgets every tracked control and clears what the platform is holding.</summary>
    /// <remarks>Tests share one process; a rectangle from a closed window must not outlive it.</remarks>
    internal static void Reset()
    {
        Tracked.Clear();
        _published = [];
        _forcePublish = false;
        _suspended = false;

        // A recompute is queued at Background priority, so one can still be in flight for a
        // window that has just gone away. Retiring the generation makes the queued job a
        // no-op instead of a publication of stale geometry.
        _generation++;
        _scheduled = false;
        PlatformSourceRegistry.SetGestureExclusions?.Invoke([]);
    }

    private static int _generation;

    private static bool _suspended;

    /// <summary>
    /// Releases every claim while a modal layer is over the workspace, and takes them back
    /// when it closes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plot's claim is honest while the plot is the thing under the reader's finger. It is
    /// not honest while a sheet or a dialog is over it: the surfaces being protected cannot be
    /// dragged, nothing behind the scrim can be, and the one gesture the reader is most likely
    /// to want at that moment is the one this suppresses. On a gesture-navigation Pixel that
    /// combination is half of V2-21 — Back was unavailable across a 205 dp band of the screen,
    /// decided purely by where on the y-axis the swipe began, at exactly the moments there was
    /// a layer to peel.
    /// </para>
    /// <para>
    /// Suspension is recomputed rather than toggled, so two stacked sheets closing in any
    /// order restore the claims exactly once, and the republish is forced because the measured
    /// geometry has not moved — only the app's willingness to claim it has.
    /// </para>
    /// </remarks>
    public static void Suspend(bool suspended)
    {
        if (_suspended == suspended)
        {
            return;
        }

        _suspended = suspended;
        _forcePublish = true;
        Schedule();
    }

    /// <summary>
    /// Re-applies the current geometry after Android resumes or replaces its decor view.
    /// </summary>
    /// <remarks>
    /// Android clears a window's exclusions when Back finishes the activity and can also
    /// replace the decor view during configuration work. The Avalonia controls may retain
    /// identical bounds, so geometry equality alone cannot tell that the platform target is
    /// new. Calling this from Activity.OnResume makes the idempotent publication explicit.
    /// </remarks>
    public static void Republish()
    {
        _forcePublish = true;
        Schedule();
    }

    private static void Schedule()
    {
        if (_scheduled || PlatformSourceRegistry.SetGestureExclusions is null)
        {
            return;
        }

        // One recompute per dispatcher turn. A layout pass raises LayoutUpdated on every
        // tracked control, and a mode switch raises several passes; the answer is the same
        // each time and only the last one is worth publishing.
        _scheduled = true;
        var generation = _generation;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (generation != _generation)
                {
                    return;
                }

                _scheduled = false;
                Publish();
            },
            DispatcherPriority.Background);
    }

    private static void Publish()
    {
        if (PlatformSourceRegistry.SetGestureExclusions is not { } apply)
        {
            return;
        }

        if (_suspended)
        {
            if (_forcePublish || _published.Count > 0)
            {
                _forcePublish = false;
                _published = [];
                apply([]);
            }

            return;
        }

        var measured = new List<(PixelRect Rectangle, bool Preferred, double Scale)>(Tracked.Count);
        for (var index = Tracked.Count - 1; index >= 0; index--)
        {
            if (!Tracked[index].TryGetTarget(out var control))
            {
                Tracked.RemoveAt(index);
                continue;
            }

            if (Measure(control) is { } rectangle)
            {
                var scale = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
                var whole = control is MinimapControl or IEdgeGestureSurface { ClaimedWhole: true };
                measured.Add((rectangle, whole, scale > 0 ? scale : 1));
            }
        }

        // Android silently keeps the lowest rectangles once the 200 dp per-edge budget is
        // exceeded. Make that policy explicit: the small, entirely draggable surfaces — the
        // minimap, and the divider's grab band — stay whole, then the timeline gets what
        // remains and is trimmed from its label-heavy top.
        var ordered = measured
            .OrderByDescending(static item => item.Preferred)
            .ThenByDescending(static item => item.Rectangle.Bottom)
            .ToArray();
        var scaleForBudget = ordered.Length > 0 ? ordered[0].Scale : 1;
        var rectangles = LimitToBudget(
            ordered.Select(static item => item.Rectangle).ToArray(),
            scaleForBudget);

        if (!_forcePublish && Same(rectangles, _published))
        {
            return;
        }

        _forcePublish = false;
        _published = rectangles;
        apply(rectangles);
    }

    /// <summary>
    /// Caps preferred-first rectangles to Android's per-edge budget, trimming overflow from
    /// each rectangle's top so the lower interaction surface remains protected.
    /// </summary>
    internal static IReadOnlyList<PixelRect> LimitToBudget(
        IReadOnlyList<PixelRect> preferredFirst,
        double renderScaling)
    {
        renderScaling = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1;
        var remainingPixels = (int)Math.Floor(MaximumExclusionHeightDp * renderScaling);
        var limited = new List<PixelRect>(preferredFirst.Count);
        foreach (var rectangle in preferredFirst)
        {
            if (remainingPixels <= 0 || rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                continue;
            }

            var keptHeight = Math.Min(rectangle.Height, remainingPixels);
            limited.Add(new PixelRect(
                rectangle.X,
                rectangle.Bottom - keptHeight,
                rectangle.Width,
                keptHeight));
            remainingPixels -= keptHeight;
        }

        return limited;
    }

    /// <summary>Where a control is on the window, in the device pixels the platform speaks.</summary>
    private static PixelRect? Measure(Control control)
    {
        if (!control.IsEffectivelyVisible ||
            control.Bounds.Width <= 0 ||
            control.Bounds.Height <= 0 ||
            TopLevel.GetTopLevel(control) is not { } root)
        {
            return null;
        }

        // A control that grabs only part of itself says so; everything else claims all of it.
        var claim = control is IEdgeGestureSurface surface
            ? surface.EdgeGestureArea
            : new Rect(control.Bounds.Size);
        if (claim.Width <= 0 || claim.Height <= 0)
        {
            return null;
        }

        var origin = control.TranslatePoint(claim.TopLeft, root);
        if (origin is not { } topLeft)
        {
            return null;
        }

        // Outward, so a half-pixel of the plot is never left outside its own exclusion.
        var scale = root.RenderScaling <= 0 ? 1 : root.RenderScaling;
        // Claim the edge margins in the same vertical band too. The plot deliberately sits
        // only 12 dp from each edge; a rectangle ending at the plot's last pixel overlaps a
        // wide Samsung Back strip by just a few pixels and was not retained reliably. Width
        // does not spend Android's exclusion budget (height does), and this still leaves Back
        // available everywhere above and below the two horizontal-drag surfaces.
        var left = 0;
        var top = (int)Math.Floor(topLeft.Y * scale);
        var right = (int)Math.Ceiling(root.Bounds.Width * scale);
        var bottom = (int)Math.Ceiling((topLeft.Y + claim.Height) * scale);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static bool Same(IReadOnlyList<PixelRect> left, IReadOnlyList<PixelRect> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}
