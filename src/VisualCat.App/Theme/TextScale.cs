namespace VisualCat.App;

/// <summary>
/// The size text is actually drawn at, given the size the reader has asked for.
/// </summary>
/// <remarks>
/// Every font size in the product was a fixed logical value, so setting Android's own text
/// size to 130% — the ordinary accessibility control, the one a reader who needs larger text
/// reaches for first — produced a pixel-identical app. The product's own <em>Text scale</em>
/// setting did work, but it is the sixth control inside More → Appearance &amp; timeline, and
/// somebody who has already told the operating system they need larger text has no reason to
/// go looking for a second switch (audit 2, B5).
///
/// The platform's scale is the baseline and the in-app setting multiplies it, so a reader who
/// wants VisualCat larger than everything else can still say so, and a reader who has only
/// ever touched the system control gets what they asked for without knowing this exists.
///
/// Views read this while they are being built, which is enough: Android recreates the
/// activity when the font scale changes, so the next build is the one that carries the new
/// value. What that recreation must not cost the reader is their place in the workspace,
/// which is what <see cref="Views.MobileWorkspaceState"/> and the persisted workspace are for.
/// </remarks>
internal static class TextScale
{
    private static double s_platform = 1;
    private static double s_user = 1;

    /// <summary>
    /// The device's own text-size setting, 1.0 when the platform does not report one.
    /// </summary>
    internal static double Platform
    {
        get => s_platform;
        set => s_platform = Sane(value);
    }

    /// <summary>The product's own <em>Text scale</em> setting.</summary>
    internal static double User
    {
        get => s_user;
        set => s_user = Sane(value);
    }

    /// <summary>
    /// The two together, held inside the range the layout can still compose.
    /// </summary>
    /// <remarks>
    /// A phone will report up to 2.0 on its own, and the in-app setting reaches 2.0 as well.
    /// Four times is not a size any of these panes can lay out, and a reader who needs that
    /// much is served by the platform's own magnifier rather than by a workspace whose
    /// controls no longer fit beside each other.
    /// </remarks>
    internal static double Effective => Math.Clamp(s_platform * s_user, 0.75, 2.2);

    /// <summary>The drawn size for a design size stated in the views.</summary>
    internal static double Of(double designSize) => Math.Round(designSize * Effective, 2);

    private static double Sane(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Clamp(value, 0.5, 2.5) : 1;
}
