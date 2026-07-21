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
            return new MobileWorkspaceLayout(
                MobileWorkspaceMode.CompactHeight,
                TimelineWeight: 2.1,
                AnalysisWeight: 2.9,
                MinimapHeight: 0,
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
