namespace VisualCat.App.Views;

/// <summary>
/// Width and height are deliberately classified independently. A phone in landscape has
/// plenty of width but very little height; treating it as a small desktop is what used to
/// collapse the analysis pane to an unusable strip.
/// </summary>
internal enum MobileWorkspaceMode
{
    TallPortrait,
    CompactPortrait,
    CompactHeight,
}

/// <summary>
/// The mobile workspace is a three-state viewport, not a boolean "show plot" preference.
/// Keeping this independent from orientation means a rotation can recompose the same intent
/// without silently changing what the user was looking at.
/// </summary>
internal enum MobileWorkspaceDisplayMode
{
    Plot,
    Split,
    Details,
}

/// <summary>
/// Owns the user's visualization intent independently from responsive recomposition.
/// Applying another size class is deliberately idempotent after initialization; only an
/// explicit user selection may change the display mode.
/// </summary>
internal sealed class MobileWorkspaceState
{
    private bool _initialized;

    public MobileWorkspaceDisplayMode DisplayMode { get; private set; } = MobileWorkspaceDisplayMode.Split;

    public void ApplyLayout(MobileWorkspaceLayout layout)
    {
        if (_initialized)
        {
            return;
        }

        DisplayMode = layout.DefaultDisplayMode;
        _initialized = true;
    }

    public void Select(MobileWorkspaceDisplayMode mode)
    {
        DisplayMode = mode;
        _initialized = true;
    }

    /// <summary>
    /// Adopts a mode the reader chose before the process that is now running.
    /// </summary>
    /// <remarks>
    /// Restoring counts as initialisation, so a later size class cannot overwrite it — the
    /// same rule <see cref="Select"/> follows, for the same reason: only the reader changes
    /// the mode.
    /// </remarks>
    public bool Restore(string? persisted)
    {
        if (!Enum.TryParse<MobileWorkspaceDisplayMode>(persisted, ignoreCase: true, out var mode))
        {
            return false;
        }

        DisplayMode = mode;
        _initialized = true;
        return true;
    }

    /// <summary>The stored form of the current mode.</summary>
    public string Persisted => DisplayMode.ToString();
}

internal readonly record struct MobileWorkspaceLayout(
    MobileWorkspaceMode Mode,
    double TimelineWeight,
    double AnalysisWeight,
    double MinimapHeight,
    double FilterMaximumHeight,
    MobileWorkspaceDisplayMode DefaultDisplayMode)
{
    internal const double CompactWidthBreakpoint = 380;
    internal const double CompactHeightBreakpoint = 520;

    /// <summary>
    /// The width at which two command groups may share one row instead of taking one each.
    /// </summary>
    /// <remarks>
    /// Compact height is chosen by height alone, and every gate that then went on to assume
    /// width has failed the same way: the query options collapsed the editor to 64 dp
    /// (C-06.2), the capture row clipped <c>Stop capture</c> to 15 dp (F-32), the split
    /// composition left the analysis pane 131 dp (F-32's second half), and the shared command
    /// row drew five controls on top of each other with <c>Filters</c> underneath them and
    /// unreachable (F-34). They are one decision, so they are one number, named once here
    /// rather than repeated as a literal at each site.
    /// <para>
    /// 600 is what the widest of them measures: the application toolbar is about 274 dp
    /// (<c>Open log</c> 102 + <c>Live</c> 72 + <c>More</c> 70, plus spacing and the command
    /// bar's own padding) and the workspace strip about 320 dp (<c>Filters</c> 76 + three
    /// mode buttons 168 + <c>Fit</c> 56, plus spacing), which is 602 dp together. It sits
    /// well below the 780 dp and 801 dp landscape viewports §6 and §7 built the compact
    /// layout for, so landscape keeps exactly the composition it had.
    /// </para>
    /// </remarks>
    internal const double SharedRowBreakpoint = 600;

    /// <summary>
    /// Whether a viewport of <paramref name="width"/> can hold two command groups on one row.
    /// </summary>
    /// <remarks>
    /// Scaled by the reader's text size, because every control in those groups is: at 1.3×
    /// the same five controls need about 780 dp, which is a landscape phone exactly, and a
    /// fixed 600 would have gone on sharing the row until they overlapped again.
    /// </remarks>
    internal static bool SharesARow(double width) =>
        double.IsFinite(width) && width >= SharedRowBreakpoint * TextScale.Effective;

    /// <summary>
    /// A short viewport has width to spare and height to protect. Navigation moves to side
    /// rails and adjacent command groups share a row in this mode, leaving the primary data
    /// surface tall enough to scroll comfortably.
    /// </summary>
    public bool UsesWideMobileComposition => Mode == MobileWorkspaceMode.CompactHeight;

    public static MobileWorkspaceLayout ForSize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            width = 412;
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            height = 800;
        }

        if (width > height || height < CompactHeightBreakpoint)
        {
            // The minimap is 26 px here rather than absent. A short viewport is exactly
            // where the plot is at its widest and most zoomed, so dropping the one control
            // that shows where the viewport sits in the whole session — silently — took the
            // aid away at the moment it was worth most (audit 2, D9). It costs the plot
            // column alone: the analysis column beside it keeps the full band.
            return new MobileWorkspaceLayout(
                MobileWorkspaceMode.CompactHeight,
                TimelineWeight: 2.1,
                AnalysisWeight: 2.9,
                MinimapHeight: 26,
                FilterMaximumHeight: 240,
                DefaultDisplayMode: MobileWorkspaceDisplayMode.Split);
        }

        if (width < CompactWidthBreakpoint)
        {
            return new MobileWorkspaceLayout(
                MobileWorkspaceMode.CompactPortrait,
                TimelineWeight: 1.7,
                AnalysisWeight: 3.3,
                MinimapHeight: 42,
                FilterMaximumHeight: 420,
                DefaultDisplayMode: MobileWorkspaceDisplayMode.Split);
        }

        return new MobileWorkspaceLayout(
            MobileWorkspaceMode.TallPortrait,
            TimelineWeight: 2,
            AnalysisWeight: 3,
            MinimapHeight: 48,
            FilterMaximumHeight: 520,
            DefaultDisplayMode: MobileWorkspaceDisplayMode.Split);
    }
}
