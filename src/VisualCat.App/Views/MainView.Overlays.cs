using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using VisualCat.App.Timeline;

namespace VisualCat.App.Views;

/// <summary>
/// Everything the application shows on top of the workspace: the command sheet a phone uses
/// instead of a flyout menu, and the in-page host that gives a platform without windows a
/// place to put a dialog.
/// </summary>
public sealed partial class MainView : IDialogHost
{
    private readonly Grid _overlayHost = new() { IsVisible = false, ZIndex = 30 };
    private readonly List<OverlayEntry> _overlays = [];

    /// <summary>One overlay on the stack, and how the system Back gesture takes it down.</summary>
    private sealed record OverlayEntry(Control Root, Action Dismiss);

    /// <summary>
    /// A secondary command, independent of the control that presents it.
    /// </summary>
    /// <remarks>
    /// The desktop presents these as toolbar buttons that fold into a "More" menu; a phone
    /// presents them as a sheet. Both are generated from this list, so a command cannot exist
    /// in one presentation and be missing from the other.
    /// </remarks>
    private sealed record CommandDescriptor(
        string Label,
        string? Description,
        Func<Task> Action,
        Func<bool>? CanExecute,
        bool IsSetting,
        CommandGroup Group = CommandGroup.ThisSession);

    /// <summary>
    /// Which heading a secondary command belongs under.
    /// </summary>
    /// <remarks>
    /// "THIS SESSION" was emitted before the first non-setting command and then covered every
    /// one of them, so it sat above Recent sessions…, Open portable archive… and Open
    /// session… — three commands whose whole purpose is to open a <em>different</em> session
    /// (finding 21.1). Only Share and Export CSV act on the session the reader is looking at.
    /// </remarks>
    private enum CommandGroup
    {
        Open,
        ThisSession,
        Settings,
    }

    private readonly List<CommandDescriptor> _secondaryCommands = [];

    /// <summary>
    /// Opens the command sheet.
    /// </summary>
    /// <remarks>
    /// A phone reached these eight commands through a <see cref="Menu"/> flyout, and a
    /// flyout is a popup: with the menu open and its items plainly on screen, a
    /// <c>uiautomator</c> dump contained none of them — not the popup, not a single item — so
    /// with a screen reader none of Open session, Open portable archive, Recent sessions,
    /// Share, Export CSV, Appearance, Session cache or the diagnostic bundle was reachable at
    /// all, and synthetic taps closed the menu without activating anything under the finger
    /// (finding 8). A sheet is ordinary content in the ordinary tree: automation walks it,
    /// taps land on it, and the system Back gesture can take it down (finding 20).
    /// </remarks>
    internal void OpenCommandSheet()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var items = new StackPanel { Spacing = 2 };
        CommandGroup? heading = null;
        foreach (var command in _secondaryCommands.OrderBy(static command => command.Group))
        {
            if (heading != command.Group)
            {
                heading = command.Group;
                items.Children.Add(SheetSectionLabel(GroupLabel(command.Group), dark));
            }

            items.Children.Add(BuildSheetItem(command, dark));
        }

        Control? sheet = null;
        sheet = BuildSheet(
            "More actions",
            items,
            dark,
            () =>
            {
                if (sheet is { } root)
                {
                    RemoveOverlay(root);
                }
            });
        PushOverlay(sheet, () => RemoveOverlay(sheet));
    }

    private static string GroupLabel(CommandGroup group) => group switch
    {
        CommandGroup.Open => "OPEN ANOTHER SESSION",
        CommandGroup.ThisSession => "THIS SESSION",
        _ => "SETTINGS",
    };

    private static TextBlock SheetSectionLabel(string text, bool dark) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeight.Bold,
        Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark)),
        Margin = new Thickness(4, 10, 0, 4),
    };

    private Button BuildSheetItem(CommandDescriptor command, bool dark)
    {
        var enabled = command.CanExecute?.Invoke() ?? true;
        var label = new TextBlock
        {
            Text = command.Label,
            FontSize = 14.5,
            Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark)),
        };

        // A command that cannot run says why, in place, instead of being tappable and silent.
        var description = enabled
            ? command.Description
            : command.Description is { Length: > 0 } text
                ? $"{text} · needs an open session"
                : "Needs an open session";
        var content = new StackPanel { Spacing = 1, Children = { label } };
        if (description is { Length: > 0 })
        {
            content.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark)),
            });
        }

        var item = new Button
        {
            Content = content,
            MinHeight = 56,
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            IsEnabled = enabled,
        };
        AutomationProperties.SetName(item, command.Label);
        if (description is { Length: > 0 })
        {
            AutomationProperties.SetHelpText(item, description);
        }

        item.Click += async (_, _) =>
        {
            DismissTopOverlay();
            await command.Action();
        };
        return item;
    }

    /// <summary>
    /// A bottom sheet: a scrim that dismisses on tap and a panel anchored to the bottom edge,
    /// where a thumb is.
    /// </summary>
    /// <remarks>
    /// <c>scrolls</c> says whether the sheet should scroll the body: a body that scrolls its
    /// own content and ends in a decision row must not be put inside a second scroller, or the
    /// decision scrolls away with the list above it (finding 16). <c>showClose</c> says
    /// whether the sheet supplies its own dismissal — Appearance &amp; timeline offered Close
    /// in the header <em>and</em> Cancel and Apply at the foot, with nothing saying whether
    /// Close saved or discarded (finding 21.4), so a body that carries its own Cancel does not
    /// get a second, differently-worded one above it.
    /// </remarks>
    private Grid BuildSheet(string title, Control body, bool dark, Action dismiss, bool scrolls = true, bool showClose = true)
    {
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)170 : (byte)120, 0, 0, 0)),
        };
        scrim.PointerPressed += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            dismiss();
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(4, 0, 0, 6),
        };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark)),
        };
        header.Children.Add(heading);
        if (showClose)
        {
            var close = new Button
            {
                Content = "Close",
                MinHeight = 44,
                Padding = new Thickness(12, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(close, "Close this sheet");
            close.Click += (_, _) => dismiss();
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
        }

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        content.Children.Add(header);
        Control inner = scrolls
            ? new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            }
            : body;
        Grid.SetRow(inner, 1);
        content.Children.Add(inner);

        var panel = new Border
        {
            Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark)),
            BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark)),
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(16, 16, 0, 0),

            // MainView uses Avalonia's automatic safe-area padding on Android, so the sheet
            // is already inside the cutout/navigation-safe content rectangle. Keeping the
            // sheet padding platform-independent avoids double-applying the navigation inset.
            Padding = new Thickness(12, 12, 12, 18),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = Math.Max(240, Bounds.Height * 0.82),
            MaxWidth = 620,
            Child = content,
        };

        // The panel must not inherit the scrim's dismissal: a tap on the sheet is a tap on
        // whatever it landed on.
        panel.PointerPressed += (_, eventArgs) => eventArgs.Handled = true;
        AutomationProperties.SetName(panel, title);

        var host = new Grid();
        host.Children.Add(scrim);
        host.Children.Add(panel);
        return host;
    }

    private void PushOverlay(Control root, Action dismiss)
    {
        _overlays.Add(new OverlayEntry(root, dismiss));
        _overlayHost.Children.Add(root);
        _overlayHost.IsVisible = true;
    }

    private void RemoveOverlay(Control root)
    {
        _overlays.RemoveAll(entry => ReferenceEquals(entry.Root, root));
        _overlayHost.Children.Remove(root);
        _overlayHost.IsVisible = _overlayHost.Children.Count > 0;
    }

    /// <summary>Dismisses the topmost overlay, if there is one.</summary>
    private bool DismissTopOverlay()
    {
        if (_overlays.Count == 0)
        {
            return false;
        }

        _overlays[^1].Dismiss();
        return true;
    }

    /// <summary>
    /// Presents a dialog: a modal window where the platform has windows, and an in-page card
    /// where it does not.
    /// </summary>
    public async Task<TResult?> ShowDialogAsync<TResult>(DialogBody<TResult> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        body.Host = this;
        if (!OperatingSystem.IsAndroid() && TopLevel.GetTopLevel(this) is Window owner)
        {
            var window = new Window
            {
                Title = body.DialogTitle,
                Content = body,
                Width = body.PreferredSize.Width,
                Height = body.PreferredSize.Height,
                MinWidth = body.MinimumSize.Width,
                MinHeight = body.MinimumSize.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            // Closing the window is a dismissal, and a decided dialog closes its window.
            window.Closed += (_, _) => body.Dismiss();
            window.Opened += (_, _) => body.NotifyPresented();
            _ = window.ShowDialog(owner);
            var windowResult = await body.Completion;
            window.Close();
            return windowResult;
        }

        var dark = ActualThemeVariant != ThemeVariant.Light;

        // The body sits in a host of its own so that taking the card down releases it: a
        // control keeps one parent, and a dialog presented twice would otherwise be stuck
        // inside a card nobody can see.
        var host = new ContentControl { Content = body };
        var card = BuildSheet(
            body.DialogTitle,
            host,
            dark,
            body.Dismiss,
            scrolls: !body.ScrollsInternally,
            showClose: false);
        PushOverlay(card, body.Dismiss);
        body.NotifyPresented();
        try
        {
            return await body.Completion;
        }
        finally
        {
            host.Content = null;
            RemoveOverlay(card);
        }
    }

    /// <summary>
    /// The system Back gesture, on the platform that has one.
    /// </summary>
    /// <remarks>
    /// Back used to do nothing at all from the workspace — the app could not be left with the
    /// gesture, only with Home — because the workspace's Escape handling claimed every Back
    /// press whether or not it had anything to dismiss. What it dismisses now is the topmost
    /// thing on screen; when there is nothing, the press is left unhandled and Android does
    /// what it does everywhere else and backgrounds the task (finding 20).
    /// </remarks>
    private void OnBackRequested(object? sender, RoutedEventArgs eventArgs)
    {
        if (DismissTopOverlay())
        {
            eventArgs.Handled = true;
            return;
        }

        if ((_tabs.SelectedItem as TabItem)?.Content is SessionWorkspaceView workspace &&
            workspace.TryDismissTransientState())
        {
            eventArgs.Handled = true;
        }
    }
}
