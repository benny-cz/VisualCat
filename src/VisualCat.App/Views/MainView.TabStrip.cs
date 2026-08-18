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

    /// <summary>One session's chip: its title, its close button, and the parts to restyle.</summary>
    private sealed record TabChip(Border Root, Button Select, Button Close, TextBlock Title);

    /// <summary>
    /// Builds the strip and takes the built-in one out of the layout.
    /// </summary>
    private ScrollViewer BuildSessionStrip()
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

        _tabStrip = new ScrollViewer
        {
            Content = _tabChips,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 4),
            IsVisible = false,
        };
        AutomationProperties.SetName(_tabStrip, "Open sessions");
        return _tabStrip;
    }

    private void AddSessionChip(SessionTabViewModel viewModel)
    {
        var mobile = OperatingSystem.IsAndroid();
        var title = new TextBlock
        {
            Text = TabTitle.Shorten(viewModel.Title, mobile ? 24 : 34),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = mobile ? 12.5 : 12,
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

        if (_tabStrip is { } strip)
        {
            strip.IsVisible = _chips.Count > 1 ||
                              (_chips.Count == 1 && !(OperatingSystem.IsAndroid() && _mobileCompactHeight));
        }
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
    private static void BringChipIntoView(Control chip) =>
        Dispatcher.UIThread.Post(chip.BringIntoView, DispatcherPriority.Loaded);
}
