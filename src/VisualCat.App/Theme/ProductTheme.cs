using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
    /// <summary>Fill under a selected list row.</summary>
    internal const string SelectionFillKey = "VisualCatSelectionFill";

    /// <summary>Outline around a selected list row.</summary>
    internal const string SelectionEdgeKey = "VisualCatSelectionEdge";

    /// <summary>Resting fill of an ordinary command button.</summary>
    internal const string ControlFillKey = "VisualCatControlFill";

    /// <summary>Resting outline of an ordinary command button.</summary>
    internal const string ControlEdgeKey = "VisualCatControlEdge";

    /// <summary>Outline of a control that cannot be used.</summary>
    internal const string ControlEdgeDisabledKey = "VisualCatControlEdgeDisabled";

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
    /// The brushes the product's own styles resolve, one set per theme variant.
    /// </summary>
    /// <remarks>
    /// A style is installed once for the whole application, so it cannot close over "the
    /// current theme". Theme dictionaries are how a single style stays correct in both:
    /// the setter binds the key, and the variant in force decides the brush.
    /// </remarks>
    internal static ResourceDictionary BuildResources()
    {
        var resources = new ResourceDictionary();
        resources.ThemeDictionaries[ThemeVariant.Dark] = VariantResources(dark: true);
        resources.ThemeDictionaries[ThemeVariant.Light] = VariantResources(dark: false);
        return resources;
    }

    private static ResourceDictionary VariantResources(bool dark)
    {
        var accent = WorkspacePalette.Accent(dark);
        var muted = WorkspacePalette.TextMuted(dark);
        return new ResourceDictionary
        {
            [SelectionFillKey] = Tint(accent, dark ? (byte)41 : (byte)36),
            [SelectionEdgeKey] = Tint(accent, dark ? (byte)190 : (byte)205),
            [ControlFillKey] = new SolidColorBrush(WorkspacePalette.SurfaceHeader(dark)),
            [ControlEdgeKey] = new SolidColorBrush(WorkspacePalette.BorderLine(dark)),
            [ControlEdgeDisabledKey] = Tint(muted, 56),
        };
    }

    private static SolidColorBrush Tint(Color color, byte alpha) =>
        new(Color.FromArgb(alpha, color.R, color.G, color.B));

    /// <summary>
    /// Layout and state corrections the product applies on top of Fluent.
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
        yield return RestingButton();
        yield return RecessiveDisabledFill<Button>();
        yield return RecessiveDisabledFill<ToggleButton>();
        yield return RecessiveDisabledFill<ComboBox>();
        yield return SelectedRowFill();
        yield return SelectedRowFill(pointerOver: true);
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

    /// <summary>
    /// Gives an ordinary command button a resting appearance of its own.
    /// </summary>
    /// <remarks>
    /// Fluent's button fill and Fluent's <em>disabled</em> button fill are the same grey, so
    /// one surface meant both "ordinary action" and "unavailable" and the only difference was
    /// text opacity — in the command sheet the strongest visual cue on screen was the block
    /// under the two commands that could not run (finding 9). This is a <see cref="Style"/>
    /// rather than a control theme override so a button that sets its own fill — the accent
    /// primaries, the mode selector, the severity chips — still wins.
    /// </remarks>
    private static Style RestingButton()
    {
        var style = new Style(static selector => Selectors.OfType<Button>(selector));
        style.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Resource(ControlFillKey)));
        style.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Resource(ControlEdgeKey)));
        style.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    /// <summary>
    /// Makes "cannot be used" recede instead of stand out: no fill, a faint outline, and the
    /// whole control held back — the treatment a disabled control is supposed to have.
    /// </summary>
    /// <remarks>
    /// Fluent paints its disabled fill onto the template's own content presenter, which
    /// outranks a local <c>Background</c> on the control, so a sheet row built as a
    /// transparent button became a raised grey block the moment it was disabled.
    /// </remarks>
    private static Style RecessiveDisabledFill<T>()
        where T : TemplatedControl
    {
        var style = new Style(static selector => Selectors
            .OfType<T>(selector)
            .Class(":disabled")
            .Template()
            .OfType<ContentPresenter>());
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Resource(ControlEdgeDisabledKey)));
        style.Setters.Add(new Setter(Visual.OpacityProperty, 0.55));
        return style;
    }

    /// <summary>
    /// Selection as a tint plus an outline rather than a solid accent slab.
    /// </summary>
    /// <remarks>
    /// Fluent fills a selected row with an accent-derived solid while the row keeps its own
    /// foregrounds, so a selected entry's metadata line measured 1.97:1 and its level letter
    /// 2.20:1 — both far under 4.5:1, and the same pattern appeared under every selected row
    /// in Recent sessions (finding 7). A low-alpha tint leaves the foregrounds the contrast
    /// they were designed against, and the outline is what still says "this row". Fatal's
    /// hot magenta clears 3.8:1 rather than 4.5:1 against the tint, because it only clears
    /// 5.1:1 against the unselected surface — that is a limit of the severity palette, not
    /// of the selection, and the row's severity ribbon and letter both remain.
    /// </remarks>
    private static Style SelectedRowFill(bool pointerOver = false)
    {
        var style = new Style(selector =>
        {
            var item = Selectors.OfType<ListBoxItem>(selector).Class(":selected");
            return (pointerOver ? item.Class(":pointerover") : item)
                .Template()
                .OfType<ContentPresenter>();
        });
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Resource(SelectionFillKey)));
        style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Resource(SelectionEdgeKey)));
        style.Setters.Add(new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    private static DynamicResourceExtension Resource(string key) => new(key);
}
