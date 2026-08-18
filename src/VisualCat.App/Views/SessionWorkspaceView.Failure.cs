using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;

namespace VisualCat.App.Views;

public sealed partial class SessionWorkspaceView : UserControl
{
    private Border? _failureCard;
    private TextBlock? _failureTitle;
    private SelectableTextBlock? _failureReason;
    private TextBlock? _failureRemedy;
    private bool _failureVisible;

    /// <summary>Asks the host to close this tab — the only useful action on a dead session.</summary>
    public event Action? CloseRequested;

    /// <summary>Asks the host to open its file picker again.</summary>
    public event Func<Task>? OpenLogRequested;

    /// <summary>
    /// The workspace a failed import gets instead of a full set of inert panes.
    /// </summary>
    /// <remarks>
    /// A failed import used to build the entire workspace over an empty store — Filters,
    /// Plot/Split/Details, the analysis tab strip, a sort dropdown, Copy raw, an empty
    /// minimap frame and a large blank slab — with nothing anywhere saying the import had
    /// failed except a truncated line in the status bar, and the dead tab had to be closed by
    /// hand (finding 10). This is the state of a session that has no data and never will:
    /// the whole reason, the step that platform can actually offer, and the two actions worth
    /// taking.
    /// </remarks>
    private Border BuildFailureCard()
    {
        _failureTitle = new TextBlock
        {
            Text = "This log could not be read",
            FontSize = _mobile ? 17 : 19,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _failureReason = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = _mobile ? 13 : 13.5,
        };
        _failureRemedy = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = _mobile ? 12.5 : 13,
            IsVisible = false,
        };

        var badge = new Border
        {
            Background = LevelPalette.Fill(LogLevel.Error, 40),
            BorderBrush = LevelPalette.BrushOf(LogLevel.Error),
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = "IMPORT FAILED",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = LevelPalette.BrushOf(LogLevel.Error),
            },
        };

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var openAnother = new Button { Content = "Open another log", MinHeight = _mobile ? 48 : 0 };
        openAnother.Click += async (_, _) =>
        {
            if (OpenLogRequested is { } handler)
            {
                await RunUiActionAsync(handler);
            }
        };
        actions.Children.Add(openAnother);
        var close = new Button { Content = "Close this tab", MinHeight = _mobile ? 48 : 0 };
        close.Click += (_, _) => CloseRequested?.Invoke();
        actions.Children.Add(close);

        var body = new StackPanel
        {
            Spacing = 9,
            Children = { badge, _failureTitle, _failureReason, _failureRemedy, actions },
        };
        _failureCard = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(_mobile ? 18 : 26, _mobile ? 16 : 22),
            Margin = new Thickness(14),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = body,
            IsVisible = false,
        };
        AutomationProperties.SetName(_failureCard, "This log could not be read");
        return _failureCard;
    }

    /// <summary>
    /// Shows the failure card in place of the workspace once a session has failed with no
    /// data. A failure that arrives after some data committed leaves the workspace alone:
    /// what was read is still worth reading, and the session pane carries the reason.
    /// </summary>
    private void UpdateFailureState()
    {
        var failed = _viewModel.Activity == SessionActivity.Failed &&
                     (_viewModel.Snapshot is null ||
                      _viewModel.Snapshot.Descriptor.Counters.TimedEntries == 0);
        if (failed && _failureReason is { } reason)
        {
            reason.Text = _viewModel.FailureReason ?? "The import ended in a failure.";
        }

        if (failed && _failureRemedy is { } remedy)
        {
            remedy.Text = _viewModel.FailureRemedy ?? string.Empty;
            remedy.IsVisible = _viewModel.FailureRemedy is { Length: > 0 };
        }

        if (_failureVisible == failed)
        {
            return;
        }

        _failureVisible = failed;
        if (_failureCard is { } card)
        {
            card.IsVisible = failed;
        }

        // The panes are hidden rather than covered: a screen reader walks the tree, and an
        // overlay drawn on top of a workspace still leaves every dead control reachable.
        foreach (var suppressed in new[]
                 {
                     _filterHost,
                     _chipBar,
                     (Control)_timeline,
                     _minimapFrame,
                     _rowSplitter,
                     _analysisGrid,
                 })
        {
            if (suppressed is { } control)
            {
                control.IsVisible = !failed;
            }
        }

        if (!failed && _mobile)
        {
            ApplyMobileLayout(Bounds.Size);
        }

        ApplyFailureTheme();
    }

    private void ApplyFailureTheme()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        if (_failureCard is { } card)
        {
            card.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            card.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        if (_failureTitle is { } title)
        {
            title.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        }

        if (_failureReason is { } reason)
        {
            reason.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        }

        if (_failureRemedy is { } remedy)
        {
            remedy.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        }
    }
}
