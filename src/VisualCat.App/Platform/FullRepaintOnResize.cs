using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VisualCat.App.Platform;

/// <summary>
/// Repaints a whole window after its size changes, on the platform whose backend does not.
/// </summary>
/// <remarks>
/// <para>
/// On an XWayland client, windows and dialogs are presented with unpainted and duplicated
/// regions after a layout pass that changes the content height: 4 of 5 openings of the settings
/// dialog came up with 15–31% of the window never painted, the export review's own
/// <em>Row order</em> and <em>Encoding</em> controls were hidden behind such a band, and the
/// empty state drew its hero block twice at an offset. Two independent capture paths agreed,
/// including one that reads the window's own contents, so the pixels really were never drawn
/// (finding F-10). The same measurement on a plain Xorg session is clean, which places the
/// defect in the toolkit's X11 presentation under XWayland rather than in this product's
/// drawing — the shape of the damage, a horizontal band and a second copy at an offset, says
/// the content is composed correctly and then presented with the wrong region.
/// </para>
/// <para>
/// This is the mitigation the finding asks for while that is fixed upstream: when a top-level's
/// size changes, every visual in it is invalidated, so the next frame is a full repaint instead
/// of an incremental one. It costs one frame's drawing on a resize and nothing at all otherwise.
/// </para>
/// <para>
/// Linux only, because that is where the defect is, and switchable with
/// <c>VISUALCAT_FULL_REPAINT=0</c> so the underlying behaviour can still be measured without
/// building a different binary.
/// </para>
/// </remarks>
internal static class FullRepaintOnResize
{
    /// <summary>
    /// How many visuals one repaint pass will walk.
    /// </summary>
    /// <remarks>
    /// A workspace with several open sessions runs to a few thousand; the bound exists so an
    /// unusually deep tree cannot turn a resize into a visible stall. Stopping early costs only
    /// the mitigation, never correctness.
    /// </remarks>
    private const int MaximumVisuals = 20_000;

    private static readonly bool Enabled =
        OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("VISUALCAT_FULL_REPAINT") != "0";

    /// <summary>Attaches the mitigation to a window, where the platform needs it.</summary>
    public static void Attach(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        if (!Enabled)
        {
            return;
        }

        topLevel.SizeChanged += (_, _) =>

            // After layout, not during it: invalidating mid-pass is what an incremental
            // presentation already does, and the frame that comes out wrong is the one drawn
            // once the new size has been laid out.
            Dispatcher.UIThread.Post(() => Repaint(topLevel), DispatcherPriority.Render);
    }

    private static void Repaint(Visual root)
    {
        var walked = 0;
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0 && walked < MaximumVisuals)
        {
            var visual = stack.Pop();
            walked++;
            visual.InvalidateVisual();
            foreach (var child in visual.GetVisualChildren())
            {
                stack.Push(child);
            }
        }
    }
}
