using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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
/// covers only the two controls that consume a horizontal drag — about 145 dp together —
/// and leaves Back working everywhere else on the screen, including the whole entries list.
/// </para>
/// <para>
/// Nothing here runs on a platform that has not installed
/// <see cref="PlatformSourceRegistry.SetGestureExclusions"/>, so the desktop pays one null
/// check per layout pass.
/// </para>
/// </remarks>
public static class EdgeGestureGuard
{
    private static readonly List<WeakReference<Control>> Tracked = [];
    private static IReadOnlyList<PixelRect> _published = [];
    private static bool _scheduled;

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
        PlatformSourceRegistry.SetGestureExclusions?.Invoke([]);
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
        Dispatcher.UIThread.Post(
            static () =>
            {
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

        var rectangles = new List<PixelRect>(Tracked.Count);
        for (var index = Tracked.Count - 1; index >= 0; index--)
        {
            if (!Tracked[index].TryGetTarget(out var control))
            {
                Tracked.RemoveAt(index);
                continue;
            }

            if (Measure(control) is { } rectangle)
            {
                rectangles.Add(rectangle);
            }
        }

        if (Same(rectangles, _published))
        {
            return;
        }

        _published = rectangles;
        apply(rectangles);
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

        var origin = control.TranslatePoint(default, root);
        if (origin is not { } topLeft)
        {
            return null;
        }

        // Outward, so a half-pixel of the plot is never left outside its own exclusion.
        var scale = root.RenderScaling <= 0 ? 1 : root.RenderScaling;
        var left = (int)Math.Floor(topLeft.X * scale);
        var top = (int)Math.Floor(topLeft.Y * scale);
        var right = (int)Math.Ceiling((topLeft.X + control.Bounds.Width) * scale);
        var bottom = (int)Math.Ceiling((topLeft.Y + control.Bounds.Height) * scale);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static bool Same(List<PixelRect> left, IReadOnlyList<PixelRect> right)
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
