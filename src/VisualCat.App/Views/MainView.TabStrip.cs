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
        //
        // The trailing room is what lets the last chip's close button reach the viewport at
        // all. Measured on the device with three sessions open: the scroll host reported
        // extent 656.7 and viewport 433.8 — so a maximum offset of 222.9, which it was already
        // at — while the last chip's own bounds ran to 686.5. The content is ~30 px longer
        // than the host believes, so the selected session's close settled 84 physical px past
        // the right edge and every further scroll request clamped to no movement (A-05,
        // finding F-26). One close target plus a gutter of trailing room is both the fix and
        // the right shape: the strip now ends in a margin instead of ending in a control cut
        // off by the screen edge.
        Margin = new Thickness(0, 0, TouchTarget.MinimumWithEdgeReserve + 10, 7),
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

        // Before the offer, because holding a close button changes the chip's width and
        // therefore the answer to "is it whole?" — settle the position first, decide second.
        if (_chipScrollPending)
        {
            ScrollSelectedChipIntoView();
        }

        var selected = _viewModel.Selected;
        foreach (var (viewModel, chip) in _chips)
        {
            var whole = chip.Root.Bounds.Width > 0 && ChipIsWhole(chip.Root);

            // The selected chip is always identified by the workspace under the strip, so its
            // close never needs revealing. Every other chip's button stays enabled and says
            // which of the two things it will do, because a full-size control that silently
            // ignores a tap is worse than either (V2-16).
            //
            // Held is now reserved for a chip with no width at all — one that has not been
            // arranged yet — so the strip can no longer converge on "close held", which is how
            // the active session's own close ended up as a 19 dp sliver at the screen edge
            // (A-05, finding F-26).
            ControlSlot.Hold(chip.Close, chip.Root.Bounds.Width > 0);
            var reveal = !whole && !ReferenceEquals(viewModel, selected);
            ToolTip.SetTip(
                chip.Close,
                reveal ? $"Show {viewModel.Title} first" : $"Close {viewModel.Title}");
            AutomationProperties.SetName(
                chip.Close,
                reveal
                    ? $"Show session {viewModel.Title} before closing it"
                    : $"Close session {viewModel.Title}");
        }
    }

    /// <summary>Whether the selected chip still owes the strip a scroll.</summary>
    /// <remarks>
    /// One posted attempt is not enough. The chips are measured, the close buttons are then
    /// offered or held — which changes their widths — and the strip's own extent is only final
    /// after that; a single request at <see cref="DispatcherPriority.Loaded"/> could run
    /// against an extent of zero and clamp itself to no movement at all, which is how the
    /// selected rightmost chip ended up with 19 dp of its close target on screen (A-05,
    /// finding F-26). The request stands until it is satisfied, and it is retried from the
    /// layout pass that would otherwise have made it wrong. It clears the moment the chip is
    /// whole, so it never fights a reader who is scrolling the strip themselves.
    /// </remarks>
    private bool _chipScrollPending;

    private void ScrollSelectedChipIntoView()
    {
        if (_tabStrip is not { } strip ||
            strip.Viewport.Width <= 0 ||
            _viewModel.Selected is not { } selected ||
            !_chips.TryGetValue(selected, out var chip) ||
            chip.Root.Bounds.Width <= 0)
        {
            return;
        }

        var before = strip.Offset.X;
        ScrollChipIntoView(chip.Root);
        if (Math.Abs(strip.Offset.X - before) < 0.5 && ChipIsWhole(chip.Root))
        {
            _chipScrollPending = false;
        }
    }

    /// <summary>Whether a chip is entirely inside the strip, measured on screen.</summary>
    private bool ChipIsWhole(Control chip)
    {
        if (_tabStrip is not { } strip ||
            strip.Bounds.Width <= 0 ||
            chip.Bounds.Width <= 0 ||
            chip.TranslatePoint(default, strip) is not { } origin)
        {
            return true;
        }

        return origin.X >= -0.5 && origin.X + chip.Bounds.Width <= strip.Bounds.Width + 0.5;
    }

    private void AddSessionChip(SessionTabViewModel viewModel)
    {
        var mobile = OperatingSystem.IsAndroid();
        var title = new TextBlock
        {
            Text = TabTitle.Shorten(viewModel.Title, mobile ? TabTitle.MobileBudget : TabTitle.DesktopBudget),
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

            // Switching and closing a session are primary controls, and both measured
            // 43.7 dp against the platform's 48 dp floor (finding F-26).
            MinHeight = TouchTarget.For(mobile, 30),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ApplySessionChipSemantics(viewModel, select);
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
            // A destructive target sharing an edge with the switch target is a session
            // closed by a thumb that meant to select it. The divider is the separation the
            // report asked for, and the margin keeps the two hit rects from touching.
            BorderThickness = new Thickness(1, 0, 0, 0),
            Margin = new Thickness(mobile ? 2 : 0, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 7, 7, 0),
            Padding = new Thickness(0),
            // The close target resolves its own width from a single glyph, so it carries the
            // edge reserve rather than the bare floor; the strip's trailing margin above is
            // stated as one of these plus a gutter and follows it.
            MinWidth = TouchTarget.SelfSized(mobile, 26),
            MinHeight = TouchTarget.For(mobile, 30),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(close, $"Close {viewModel.Title}");
        AutomationProperties.SetName(close, $"Close session {viewModel.Title}");

        // A clipped chip reveals itself; a whole chip closes. The button used to be held —
        // disabled, and invisible in its own slot — whenever its chip was not entirely on
        // screen, so with four or more sessions the leftmost tab could not be closed at all
        // and nothing on screen said why: a tap at the centre of its slot did nothing, the
        // session stayed open, and no notice appeared (V2-16). The guard was right about the
        // hazard — a destructive control must not float beside a name that has scrolled away —
        // and wrong about the answer, because "nothing happens" is not a guard a reader can
        // learn from. One tap brings the name back beside the button; the next one closes it.
        close.Click += async (_, _) =>
        {
            if (!_chips.TryGetValue(viewModel, out var current))
            {
                return;
            }

            if (!ChipIsWhole(current.Root))
            {
                ScrollChipIntoView(current.Root);
                UpdateChipEdges();
                return;
            }

            // async void: a close that fails has to land in the shell's own failure lane
            // rather than escaping the handler as an unobserved exception.
            await RunAsync(() => _viewModel.CloseAsync(viewModel));
        };

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
            chip.Title.Text = TabTitle.Shorten(viewModel.Title, OperatingSystem.IsAndroid() ? TabTitle.MobileBudget : TabTitle.DesktopBudget);
            ApplySessionChipSemantics(viewModel, chip.Select);
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
    /// Names the durable completion state on the tab itself, not only in the workspace it
    /// opens. An interrupted capture must not sound identical to a complete one while a
    /// screen-reader user moves through the open-session strip (F-19).
    /// </summary>
    internal static void ApplySessionChipSemantics(SessionTabViewModel viewModel, Button select)
    {
        var outcome = viewModel.Activity switch
        {
            SessionActivity.RecoverablePartial => "interrupted",
            SessionActivity.Capturing or SessionActivity.Connecting or SessionActivity.Starting or
                SessionActivity.Queued or SessionActivity.Opening or SessionActivity.Importing or
                SessionActivity.Stopping => "in progress",
            SessionActivity.Failed => "failed",
            _ => "complete",
        };
        ToolTip.SetTip(select, $"{viewModel.Title} · {outcome}");
        AutomationProperties.SetName(select, $"Show {outcome} session {viewModel.Title}");
        AutomationProperties.SetHelpText(
            select,
            outcome == "interrupted"
                ? "This capture ended before it was finalized; open it to inspect the recovered data."
                : $"Open this {outcome} session.");
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
    /// <remarks>
    /// <c>BringIntoView(Rect)</c> was asked for a rect ten pixels wider than the
    /// chip on each side, and with three sessions open the selected rightmost chip still came
    /// to rest with its close node clipped from 124 px to 43 — 15.3 dp of the one control that
    /// closes the session the reader is looking at (A-05, compounding finding F-26). The
    /// request is a hint the scroll host may satisfy approximately; the offset is not. So the
    /// arithmetic is done here, against the strip's own viewport, and the result is a clamped
    /// assignment rather than a request.
    /// </remarks>
    private void BringChipIntoView(Control chip)
    {
        _chipScrollPending = true;
        Dispatcher.UIThread.Post(() => ScrollChipIntoView(chip), DispatcherPriority.Loaded);
    }

    private void ScrollChipIntoView(Control chip)
    {
        if (_tabStrip is not { } strip ||
            strip.Bounds.Width <= 0 ||
            chip.Bounds.Width <= 0 ||
            chip.TranslatePoint(default, strip) is not { } origin)
        {
            return;
        }

        // Measured against the strip itself rather than against its reported viewport and
        // extent. Those describe the content, and the arithmetic that goes through them has to
        // agree with the scroll host about padding, spacing and what a "viewport" includes —
        // which on the device it did not: the strip settled with the selected chip's close
        // button 78 px past the right edge and every further request clamped to no movement at
        // all. A translated point and the control's own width are facts about the screen, and
        // the correction is the difference between them.
        //
        // A gutter on both sides, because the chip's own bounds end exactly at its close
        // button and a target flush with the screen edge is a target a thumb misses.
        const double Gutter = 10;
        var overflowRight = origin.X + chip.Bounds.Width + Gutter - strip.Bounds.Width;
        var overflowLeft = Gutter - origin.X;

        // Right first, then left: a chip wider than the strip must show its start rather than
        // its end, because that is where its name is.
        var delta = overflowRight > 0 ? overflowRight : 0;
        if (overflowLeft > 0)
        {
            delta = -overflowLeft;
        }

        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        var wanted = Math.Max(0, strip.Offset.X + delta);
        if (Math.Abs(wanted - strip.Offset.X) > 0.5)
        {
            strip.Offset = new Vector(wanted, strip.Offset.Y);
        }
    }
}
