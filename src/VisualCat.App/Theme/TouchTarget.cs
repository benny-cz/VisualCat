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

    /// <summary>The floor on this platform: <see cref="Minimum"/> on touch, or <paramref name="desktop"/>.</summary>
    internal static double For(bool touch, double desktop = 0) => touch ? Minimum : desktop;

    /// <summary>The floor on the platform the app is actually running on.</summary>
    internal static double Here(double desktop = 0) => For(OperatingSystem.IsAndroid(), desktop);
}
