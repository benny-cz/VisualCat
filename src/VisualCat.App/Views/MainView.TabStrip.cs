using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;

namespace VisualCat.App.Views;

/// <summary>
/// The session strip: one scrolling row of chips instead of a stack of full-width rows.
/// </summary>
/// <remarks>
/// The built-in tab strip lays its items out in a <see cref="WrapPanel"/> and each item takes
/// the whole width, so three open sessions became three full-width rows — about 300 px of a
/// 2138 px phone viewport gone before any content, which squeezed the timeline enough to
/// clip its axis labels. Each close button also sat hard against the end of its title with
/// nothing tying it to the tab, so it read as an overlay floating between rows (finding 22).
/// The strip below scrolls sideways, truncates titles in the middle so both ends survive
/// (finding 28), and draws each close affordance inside its own tab's chip.
/// </remarks>
public sealed partial class MainView
{
    private readonly StackPanel _tabChips = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,

        // The strip's horizontal scrollbar is drawn over its content, not under it, so the
        // thumb cut a grey line straight through the bottom edge of every tab. Reserving the
        // thumb's own row is what stops it crossing the chrome (finding 11).
        Margin = new Thickness(0, 0, 0, 7),
    };

    private readonly Dictionary<SessionTabViewModel, TabChip> _chips = [];
    private ScrollViewer? _tabStrip;
    private FadingScrollHost? _tabStripFade;

    /// <summary>One session's chip: its title, its close button, and the parts to restyle.</summary>
    private sealed record TabChip(Border Root, Button Select, Button Close, TextBlock Title);

    /// <summary>
    /// Builds the strip and takes the built-in one out of the layout.
    /// </summary>
    private FadingScrollHost BuildSessionStrip()
    {
        // Only this TabControl's strip is hidden. The class is what keeps the selector off the
        // analysis TabControl inside a session workspace, which is a descendant of this one.
        _tabs.Classes.Add("sessionHost");
        var hideBuiltInStrip = new Style(selector => Selectors
            .OfType<TabControl>(selector)
            .Class("sessionHost")
            .Template()
            .OfType<ItemsPresenter>());
        hideBuiltInStrip.Setters.Add(new Setter(Visual.IsVisibleProperty, false));
        _tabs.Styles.Add(hideBuiltInStrip);

        var mobile = OperatingSystem.IsAndroid();
        _tabStrip = new ScrollViewer
        {
            Content = _tabChips,

            // A phone gets an edge fade instead of a bar. Fluent's horizontal one arrived as a
            // full-width 16 dp band under the tabs carrying Column-left, Page-left, Page-right
            // and Column-right — a whole row of a phone screen and four more accessibility
            // stops, for a strip a thumb scrolls by dragging it (audit 3, D4/B5).
            HorizontalScrollBarVisibility = mobile
                ? ScrollBarVisibility.Hidden
                : ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,

            // Inset to the same gutter every other band uses. The strip spanned the whole
            // 1080 px while everything above and below it stopped at 36, so the first tab's
            // rounded corner sat squarely on the screen edge and the row read as a different
            // surface from the workspace it belongs to (audit 2, D8).
            Padding = new Thickness(mobile ? 10 : 12, 0),
        };
        AutomationProperties.SetName(_tabStrip, "Open sessions");
        _tabStrip.ScrollChanged += (_, _) => UpdateChipEdges();
        _tabStrip.LayoutUpdated += (_, _) => UpdateChipEdges();
        _tabStripFade = new FadingScrollHost(
            _tabStrip,
            ActualThemeVariant != ThemeVariant.Light,
            horizontal: true);
        _tabStripFade.Margin = new Thickness(0, 0, 0, 4);
        _tabStripFade.IsVisible = false;
        return _tabStripFade;
    }

    /// <summary>
    /// Offers a session's close button only while the reader can see which session it closes.
    /// </summary>
    /// <remarks>
    /// With three sessions open the strip overflows, and the first chip's name scrolled away
    /// while its close glyph did not: the leftmost thing on the screen was a 115 px unlabelled
    /// destructive control belonging to a session the reader could no longer identify. At the
    /// other end the selected chip's own close button was clipped to 15 dp against the screen
    /// edge (audit 3, D4). A chip that is not fully on screen is a name, and nothing else.
    ///
    /// The button is held rather than removed, so a chip scrolling into view does not resize
    /// under the finger that is scrolling it.
    /// </remarks>
    private void UpdateChipEdges()
    {
        if (_tabStrip is not { } strip || strip.Viewport.Width <= 0)
        {
            return;
        }

        var left = strip.Offset.X;
        var right = left + strip.Viewport.Width;
        foreach (var chip in _chips.Values)
        {
            var bounds = chip.Root.Bounds;
            var whole = bounds.Width > 0 && bounds.Left >= left - 0.5 && bounds.Right <= right + 0.5;
            ControlSlot.Hold(chip.Close, whole);
        }
    }

    private void AddSessionChip(SessionTabViewModel viewModel)
    {
        var mobile = OperatingSystem.IsAndroid();
        var title = new TextBlock
        {
            Text = TabTitle.Shorten(viewModel.Title, mobile ? 24 : 34),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = TextScale.Of(mobile ? 12.5 : 12),
        };
        var select = new Button
        {
            Content = title,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7, 0, 0, 7),
            Padding = new Thickness(11, 0),
            MinHeight = mobile ? 44 : 30,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(select, viewModel.Title);
        AutomationProperties.SetName(select, $"Show session {viewModel.Title}");
        select.Click += (_, _) =>
        {
            if (_tabItems.TryGetValue(viewModel, out var item))
            {
                _tabs.SelectedItem = item;
            }
        };

        var close = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 7, 7, 0),
            Padding = new Thickness(0),
            MinWidth = mobile ? 44 : 26,
            MinHeight = mobile ? 44 : 30,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(close, $"Close {viewModel.Title}");
        AutomationProperties.SetName(close, $"Close session {viewModel.Title}");
        close.Click += async (_, _) => await _viewModel.CloseAsync(viewModel);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        body.Children.Add(select);
        Grid.SetColumn(close, 1);
        body.Children.Add(close);

        var chip = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = body,
            ClipToBounds = true,
        };
        _chips[viewModel] = new TabChip(chip, select, close, title);
        _tabChips.Children.Add(chip);
        UpdateSessionStrip();
    }

    private void RemoveSessionChip(SessionTabViewModel viewModel)
    {
        if (_chips.Remove(viewModel, out var chip))
        {
            _tabChips.Children.Remove(chip.Root);
        }

        UpdateSessionStrip();
    }

    /// <summary>
    /// Restyles the chips for the current selection and theme, and decides whether the strip is
    /// worth its row at all: with one session open and a viewport too short to spare a row, the
    /// session's identity is already in the workspace and the row is better spent on data
    /// (finding 3d).
    /// </summary>
    private void UpdateSessionStrip()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var accent = WorkspacePalette.Accent(dark);
        var selected = _viewModel.Selected;
        foreach (var (viewModel, chip) in _chips)
        {
            var isSelected = ReferenceEquals(viewModel, selected);
            chip.Root.Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(dark ? (byte)46 : (byte)30, accent.R, accent.G, accent.B)
                : WorkspacePalette.SurfaceRaised(dark));
            chip.Root.BorderBrush = new SolidColorBrush(isSelected ? accent : WorkspacePalette.BorderLine(dark));
            chip.Title.Foreground = new SolidColorBrush(isSelected
                ? WorkspacePalette.TextPrimary(dark)
                : WorkspacePalette.TextMuted(dark));
            chip.Title.FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal;
            chip.Title.Text = TabTitle.Shorten(viewModel.Title, OperatingSystem.IsAndroid() ? 24 : 34);
            chip.Close.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
            chip.Close.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
            if (isSelected)
            {
                BringChipIntoView(chip.Root);
            }
        }

        if (_tabStripFade is { } strip)
        {
            strip.IsVisible = _chips.Count > 1 ||
                              (_chips.Count == 1 && !(OperatingSystem.IsAndroid() && _mobileCompactHeight));
            strip.ApplyTheme(dark);
        }

        UpdateChipEdges();
    }

    /// <summary>
    /// Scrolls the selected session's chip into view once it has a position to scroll to.
    /// </summary>
    /// <remarks>
    /// This was already being asked for, in the same pass that creates the chip — before the
    /// strip had laid anything out, so the chip's bounds were still empty and the request was
    /// a no-op. Opening a third session therefore selected a tab that stayed off screen: the
    /// workspace switched to a session whose tab reported a 10 px sliver at the right edge,
    /// and nothing on screen said which session was showing (finding 11). Posted at
    /// <see cref="DispatcherPriority.Loaded"/>, the request runs after the arrange it depends
    /// on.
    /// </remarks>
    /// <remarks>
    /// A gutter is asked for on both sides, because the chip's own bounds end exactly at its
    /// close button: brought in flush, the selected session's close measured 15 dp against the
    /// screen edge (audit 3, D4), and the button that closes what you are looking at is the
    /// one that must not be a sliver.
    /// </remarks>
    private static void BringChipIntoView(Control chip) =>
        Dispatcher.UIThread.Post(
            () => chip.BringIntoView(new Rect(-10, 0, chip.Bounds.Width + 20, chip.Bounds.Height)),
            DispatcherPriority.Loaded);
}
