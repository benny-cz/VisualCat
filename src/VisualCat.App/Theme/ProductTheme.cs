using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using VisualCat.App.Timeline;

namespace VisualCat.App.Theme;

/// <summary>
/// The product's own reading of Fluent.
///
/// Two things were being decided by whoever happened to be looking at the app rather than
/// by the product. First, the accent: Fluent picks the platform accent up as
/// <c>SystemAccentColor</c>, so on Android the selection highlight, the focus border and
/// every tab underline took the device's Material You color — a warm brick on the phone
/// this was found on, which reads as an error tint under every selected row and cannot be
/// tested from a screenshot taken on another device. Second, the list surfaces: Fluent's
/// <c>ChromeMediumLow</c> is a neutral grey (<c>#2B2B2B</c> in dark), so the entry table and
/// the insights list painted a grey slab into a navy workspace, at its most obvious in the
/// two states where the list is empty and the slab is all there is.
///
/// Both are palette entries rather than per-control overrides: <see cref="ColorPaletteResources"/>
/// is the theme's own extension point, Fluent derives its accent shades from the one color,
/// and a control added later inherits the product's palette instead of needing to remember
/// to opt in.
/// </summary>
internal static class ProductTheme
{
    /// <summary>
    /// Builds the themed Fluent instance the application installs. The palette is keyed by
    /// variant because both are first-class (§14.1): the accent is the workspace accent for
    /// that variant, and the two surface colors are the workspace's own.
    /// </summary>
    internal static FluentTheme CreateFluentTheme()
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = Palette(dark: true);
        fluent.Palettes[ThemeVariant.Light] = Palette(dark: false);
        return fluent;
    }

    private static ColorPaletteResources Palette(bool dark) => new()
    {
        // Selection, focus, tab indicators, and the accent fills Fluent derives from it.
        Accent = WorkspacePalette.Accent(dark),

        // The window/page ground. Fluent paints this before any view is attached, so it is
        // also what removes the black flash a cold start used to open with.
        RegionColor = WorkspacePalette.Surface(dark),

        // Every list, popup and flyout surface in Fluent resolves through ChromeMediumLow.
        ChromeMediumLow = WorkspacePalette.SurfaceRaised(dark),
        ChromeLow = WorkspacePalette.Surface(dark),
        ChromeMedium = WorkspacePalette.SurfaceHeader(dark),
    };

    /// <summary>
    /// Layout corrections the product applies on top of Fluent.
    /// </summary>
    /// <remarks>
    /// <see cref="ContentControl.VerticalContentAlignment"/> defaults to
    /// <see cref="VerticalAlignment.Stretch"/>, which in a control taller than its label —
    /// every 48 dp touch target — stretches the label's own box and leaves the text sitting
    /// in the top third. Individual controls had been fixed one at a time, so the answer
    /// depended on whether someone had noticed that particular button; these are styles, so
    /// a local value still wins where a control genuinely wants to place its own content,
    /// and the next tall button added is right by default (finding 12/13).
    /// </remarks>
    internal static IEnumerable<Style> BuildStyles()
    {
        yield return CenterContentVertically<Button>();
        yield return CenterContentVertically<ToggleButton>();
        yield return CenterContentVertically<TabItem>();
    }

    private static Style CenterContentVertically<T>()
        where T : ContentControl
    {
        var style = new Style(selector => Selectors.OfType<T>(selector));
        style.Setters.Add(new Setter(
            ContentControl.VerticalContentAlignmentProperty,
            VerticalAlignment.Center));
        return style;
    }
}
