namespace VisualCat.App;

/// <summary>
/// The one minimum size an actionable control has on a touch platform.
/// </summary>
/// <remarks>
/// The product had two sizes and a lot of zeroes. Toolbar buttons, workspace modes, Fit and
/// the analysis tabs all measured 135 px = 48 dp at the test device's 450 dpi, and were
/// right; session chips, their close targets, the notice dismiss action and the source-section
/// header were written as a literal <c>44</c> and measured 43.7 dp (finding F-26); the empty
/// state's three hero links had no minimum at all and measured <b>18.8 dp</b>, on the one
/// screen a first-time reader meets (finding F-03).
///
/// 48 dp is the platform's stated floor and the live-test plan's U-08 gate. The visible glyph
/// does not have to grow to meet it — a hit area does — so the token is applied as
/// <c>MinHeight</c>/<c>MinWidth</c> on the control, leaving typography alone.
/// </remarks>
internal static class TouchTarget
{
    /// <summary>The floor, in logical pixels, on a platform where a finger is the pointer.</summary>
    internal const double Minimum = 48;

    /// <summary>
    /// The size a control must reserve when it sizes <em>itself</em> to the floor.
    /// </summary>
    /// <remarks>
    /// A control laid out at exactly <see cref="Minimum"/> can still export an accessibility
    /// node below it. Android rounds a node's two edges to physical pixels
    /// <em>independently</em>, so a control whose logical origin is fractional loses part of a
    /// pixel at each end: a severity chip measured 47.6 dp on a Pixel at 2.75 px/dp (F-48),
    /// and the time-lens <c>Zoom in</c> button measured 47.6 dp on a Samsung at 2.25 px/dp
    /// while its neighbour <c>Zoom out</c>, which happened to start on a whole pixel, measured
    /// 48.0. The defect is the arithmetic, not the device, so the answer cannot be a literal
    /// written at whichever call site was measured last.
    ///
    /// One logical dp is the reserve. It is under half a physical pixel at every density the
    /// product ships on — so it cannot be seen, and it changes no wrap, spacing or rhythm —
    /// and it is more than the most two inward-rounded edges can take.
    ///
    /// Only a control that resolves its own width or height needs it. One stretched to a row
    /// or a column takes that container's edges, and those are measured against the container.
    /// </remarks>
    internal const double MinimumWithEdgeReserve = Minimum + 1;

    /// <summary>The floor on this platform: <see cref="Minimum"/> on touch, or <paramref name="desktop"/>.</summary>
    internal static double For(bool touch, double desktop = 0) => touch ? Minimum : desktop;

    /// <summary>
    /// The self-sizing floor on this platform: <see cref="MinimumWithEdgeReserve"/> on touch,
    /// or <paramref name="desktop"/>.
    /// </summary>
    internal static double SelfSized(bool touch, double desktop = 0) =>
        touch ? MinimumWithEdgeReserve : desktop;

    /// <summary>
    /// Forces the touch floor on or off, for tests that need to exercise it.
    /// </summary>
    /// <remarks>
    /// The floor only exists on a touch platform, so a headless desktop run measures every
    /// control against zero and proves nothing. That is how twelve spin buttons stayed
    /// 34 dp wide through three device passes (finding F-31): the only thing that had ever
    /// measured them was a phone. Null means "ask the platform", which is what every
    /// shipping build does.
    /// </remarks>
    internal static bool? TouchOverride { get; set; }

    /// <summary>The floor on the platform the app is actually running on.</summary>
    internal static double Here(double desktop = 0) =>
        For(TouchOverride ?? OperatingSystem.IsAndroid(), desktop);

    /// <summary>
    /// The self-sizing floor on the platform the app is actually running on.
    /// </summary>
    internal static double SelfSizedHere(double desktop = 0) =>
        SelfSized(TouchOverride ?? OperatingSystem.IsAndroid(), desktop);
}
