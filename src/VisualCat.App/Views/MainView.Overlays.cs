using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private IInputPane? _overlayInputPane;
    private bool _overlayInputPaneOpen;
    private Rect _overlayInputPaneRect;

    /// <summary>
    /// One overlay on the stack: how the system Back gesture takes it down, the parts of it
    /// that answer a state change, and — for a sheet whose body holds nothing the reader has
    /// half-finished — how to build that body again.
    /// </summary>
    private sealed record OverlayEntry(
        Control Root,
        Action Dismiss,
        SheetSurface? Surface = null,
        Func<bool, Control>? RebuildBody = null);

    /// <summary>
    /// The parts of a sheet that were decided by the state it opened in.
    /// </summary>
    /// <remarks>
    /// Everything <see cref="BuildSheet"/> resolves is resolved once: the scrim's alpha and
    /// the panel's surface, border and heading from the theme variant, the heading's size from
    /// <see cref="TextScale"/>, and the panel's height cap from the bounds at that instant.
    /// Nothing wrote to a sheet again, so a theme change repainted the whole shell around one
    /// that stayed dark, a rotation left it capped at the height of the orientation it opened
    /// in — 317 dp of an available 698, listing three of nine commands where all nine fit —
    /// and a text-size change left it the only surface on screen still at 1.0x (finding F-40).
    /// A sheet is on screen for as long as the reader keeps it there, which is long enough for
    /// any of the three.
    /// </remarks>
    private sealed record SheetSurface(
        Border Scrim,
        Border Panel,
        TextBlock Heading,
        Button? Close,
        FadingScrollHost? Fade,
        ContentControl BodyHost,
        Grid Host)
    {
        /// <summary>Re-resolves everything the sheet was given when it opened.</summary>
        internal void Apply(bool dark, double viewportHeight, double? inputPaneTop)
        {
            Scrim.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)170 : (byte)120, 0, 0, 0));
            Panel.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            Panel.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
            var placement = ResolveSheetInputPaneLayout(viewportHeight, inputPaneTop);
            Panel.VerticalAlignment = placement.AlignTop
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
            Panel.Margin = placement.AlignTop
                ? new Thickness(8)
                : new Thickness(8, 0, 8, 8 + placement.BottomInset);
            Panel.MaxHeight = placement.MaximumHeight;
            // In the extreme fallback, the panel's automation name still supplies the title.
            // Suppressing only the repeated visual heading reclaims enough of the 76 dp strip
            // for one complete 48 dp editor; it returns with the footer when the IME closes.
            Heading.IsVisible = !placement.AlignTop;
            Heading.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
            Heading.FontSize = TextScale.Of(15);
            if (Close is { } close)
            {
                close.MinHeight = TouchTarget.Minimum;
            }

            Fade?.ApplyTheme(dark);
        }
    }

    /// <summary>How much of the window a bottom sheet may take.</summary>
    private static double SheetHeightCap(double viewportHeight) =>
        Math.Max(240, viewportHeight * 0.82);

    /// <summary>
    /// Places an in-page sheet in the part of its overlay host the soft keyboard does not
    /// cover. The returned inset moves the pinned footer, while the height keeps the sheet's
    /// scrolling body inside the same visible region.
    /// </summary>
    /// <remarks>
    /// Some Android keyboards honor <c>AdjustResize</c> by publishing an occluded rectangle
    /// instead of reducing Avalonia's viewport. A sheet that only reads the viewport keeps
    /// its editor and decision row behind the IME (F-44). This calculation deliberately
    /// consumes host-relative geometry, so it is independent of density, navigation insets,
    /// keyboard brand and orientation.
    /// </remarks>
    internal static (double BottomInset, double MaximumHeight, bool AlignTop) ResolveSheetInputPaneLayout(
        double viewportHeight,
        double? inputPaneTop)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0 ||
            inputPaneTop is not { } top || !double.IsFinite(top) || top >= viewportHeight - 0.5)
        {
            return (0, SheetHeightCap(Math.Max(0, viewportHeight)), false);
        }

        var available = Math.Clamp(top, 0, viewportHeight);

        // A 130%-text landscape Pixel leaves about 76 logical pixels above Gboard. That is
        // smaller than a 48 dp decision row plus the heading, so shrinking the entire card to
        // the unobscured strip produces a title-only sheet with no editor (F-47). In that
        // physically impossible case, anchor the ordinary card at the top and let the IME
        // cover its deferred footer; the body scroller is then moved so the focused editor is
        // the useful 48 dp that remains. Dismissing the IME reveals the pinned actions again.
        const double minimumWholeSheetHeight = 240;
        if (available < minimumWholeSheetHeight)
        {
            return (
                0,
                Math.Max(0, Math.Min(SheetHeightCap(viewportHeight), viewportHeight - 16)),
                true);
        }

        var bottomInset = viewportHeight - available;

        // Eight units are the sheet's ordinary bottom breathing room. Spending them here
        // makes BottomInset + margin + panel height exactly the host height: no overlap and
        // no unexplained gap above the IME.
        return (bottomInset, Math.Max(0, available - 8), false);
    }

    /// <summary>Caps a scroller's usable height at the keyboard's top edge.</summary>
    internal static double ResolveUnoccludedScrollerHeight(
        double viewportHeight,
        double scrollerTop,
        double? inputPaneTop)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0 ||
            !double.IsFinite(scrollerTop) ||
            inputPaneTop is not { } top || !double.IsFinite(top))
        {
            return Math.Max(0, viewportHeight);
        }

        return Math.Clamp(top - scrollerTop, 0, viewportHeight);
    }

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
        Control? sheet = null;
        sheet = BuildSheet(
            "More actions",
            BuildCommandList(dark),
            dark,
            () =>
            {
                if (sheet is { } root)
                {
                    RemoveOverlay(root);
                }
            },
            out var surface);
        PushOverlay(sheet, () => RemoveOverlay(sheet), surface, BuildCommandList);
    }

    /// <summary>
    /// The command sheet's body: every secondary command under its heading.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="OpenCommandSheet"/> so the sheet can be given a new one while
    /// it is open. This list is derived entirely from <c>_secondaryCommands</c> and the theme —
    /// it holds no state of the reader's — so rebuilding it costs nothing and is what makes the
    /// menu answer a theme or text-size change instead of sitting through it (F-40).
    /// </remarks>
    private Control BuildCommandList(bool dark)
    {
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

        return items;
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
        FontSize = TextScale.Of(10),
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
            FontSize = TextScale.Of(14.5),
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
                FontSize = TextScale.Of(11),
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
    private Grid BuildSheet(
        string title,
        Control body,
        bool dark,
        Action dismiss,
        out SheetSurface surface,
        bool scrolls = true,
        bool showClose = true)
    {
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)170 : (byte)120, 0, 0, 0)),
        };
        // Released, not pressed. A stock gesture-navigation Back begins as an ordinary
        // ACTION_DOWN on whatever is under the finger and is only claimed by the system once
        // it has travelled — Android then sends the app a cancel. Dismissing on the press
        // meant an edge swipe over the scrim took the sheet down on touch-down, and the same
        // gesture then arrived as Back with nothing left to peel, so the platform backgrounded
        // the task: one gesture, two consumers, and the reader lands on the launcher. That is
        // V2-21's first fault, and it is why key Back and a tapped scrim always looked right.
        // A tap still dismisses; a gesture the system takes away never completes here.
        scrim.PointerPressed += (_, eventArgs) => eventArgs.Handled = true;
        scrim.PointerReleased += (_, eventArgs) =>
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

            // Wrapped, not trimmed. "Choose what Live captures" clipped horizontally on the
            // Pixel's 393 dp viewport at large text (§14.7), and a title is the one string on
            // a card that has to survive every scale.
            TextWrapping = TextWrapping.Wrap,
            FontSize = TextScale.Of(15),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark)),
        };
        header.Children.Add(heading);
        Button? closeButton = null;
        if (showClose)
        {
            var close = closeButton = new Button
            {
                Content = "Close",
                MinHeight = TouchTarget.Minimum,
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

        // A sheet ends in a pinned decision row, so its last control is the one that gets cut
        // when the body is taller than the sheet — Appearance & timeline lost the bottom third
        // of a checkbox and Session cache the whole second line of its last session, both
        // against a hard edge with nothing saying anything was below it (audit 3, D2).
        // The body is held rather than nested directly, so a sheet whose body can be built
        // again — a command list, which holds nothing half-finished — can be given a new one
        // in place when the theme or the text size changes under it (F-40).
        var bodyHost = new ContentControl { Content = body };
        FadingScrollHost? fade = null;
        Control inner = scrolls
            ? fade = new FadingScrollHost(
                new ScrollViewer
                {
                    Content = bodyHost,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                dark)
            : bodyHost;
        Grid.SetRow(inner, 1);
        content.Children.Add(inner);

        var panel = new Border
        {
            Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark)),
            BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark)),
            // Keep the whole sheet visibly framed. A flush, open lower edge reads as clipped
            // on gesture-navigation phones (confirmed on Motorola and Samsung), especially
            // when the dialog fills its height cap.
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(8, 0, 8, 8),

            // MainView uses Avalonia's automatic safe-area padding on Android, so the sheet
            // is already inside the cutout/navigation-safe content rectangle. Keeping the
            // sheet padding platform-independent avoids double-applying the navigation inset.
            Padding = new Thickness(12, 12, 12, 18),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = SheetHeightCap(Bounds.Height),
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
        surface = new SheetSurface(scrim, panel, heading, closeButton, fade, bodyHost, host);
        return host;
    }

    /// <summary>States the workspace band's accessibility view before any sheet exists.</summary>
    private void InitializeOverlayModality() => ApplyOverlayModality();

    private void PushOverlay(
        Control root,
        Action dismiss,
        SheetSurface? surface = null,
        Func<bool, Control>? rebuildBody = null)
    {
        _overlays.Add(new OverlayEntry(root, dismiss, surface, rebuildBody));
        _overlayHost.Children.Add(root);
        _overlayHost.IsVisible = true;
        ApplyOverlayModality();
    }

    /// <summary>
    /// Re-states every open sheet against the state the application is in now.
    /// </summary>
    /// <remarks>
    /// Called from the three transitions that used to walk past the overlay host: the theme
    /// change that repaints every other surface, the size change that recomposes the shell,
    /// and the text-size change that rebuilds every workspace. A sheet that can be built again
    /// is (a command list holds nothing the reader has half-typed); a dialog body is left
    /// alone, because rebuilding one would discard exactly the edit the reader opened it to
    /// make — its own controls are theme-resourced and follow the variant on their own, and
    /// what it needed from here was the panel around it (F-40).
    /// </remarks>
    private void RefreshOverlays()
    {
        if (_overlays.Count == 0)
        {
            return;
        }

        var dark = ActualThemeVariant != ThemeVariant.Light;
        var topLevel = TopLevel.GetTopLevel(this);
        if (_overlayInputPane is { } inputPane)
        {
            _overlayInputPaneOpen = inputPane.State == InputPaneState.Open;
            if (_overlayInputPaneOpen && inputPane.OccludedRect.Height > 0)
            {
                _overlayInputPaneRect = inputPane.OccludedRect;
            }
        }

        foreach (var entry in _overlays)
        {
            if (entry.Surface is not { } surface)
            {
                continue;
            }

            var height = surface.Host.Bounds.Height > 0 ? surface.Host.Bounds.Height : Bounds.Height;
            double? inputPaneTop = null;
            if (_overlayInputPaneOpen &&
                _overlayInputPaneRect.Height > 0 &&
                topLevel is not null &&
                surface.Host.TranslatePoint(default, topLevel) is { } hostOrigin)
            {
                inputPaneTop = _overlayInputPaneRect.Y - hostOrigin.Y;
            }

            surface.Apply(dark, height, inputPaneTop);
            if (entry.RebuildBody is { } rebuild)
            {
                surface.BodyHost.Content = rebuild(dark);
            }
        }
    }

    /// <summary>Watches the top-level keyboard rectangle for every in-page sheet.</summary>
    private void ObserveOverlayInputPane()
    {
        if (_overlayInputPane is not null)
        {
            return;
        }

        _overlayInputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_overlayInputPane is { } pane)
        {
            pane.StateChanged += OnOverlayInputPaneStateChanged;
            _overlayInputPaneOpen = pane.State == InputPaneState.Open;
            _overlayInputPaneRect = _overlayInputPaneOpen ? pane.OccludedRect : default;
        }
    }

    private void StopObservingOverlayInputPane()
    {
        if (_overlayInputPane is { } pane)
        {
            pane.StateChanged -= OnOverlayInputPaneStateChanged;
            _overlayInputPane = null;
        }

        _overlayInputPaneOpen = false;
        _overlayInputPaneRect = default;
    }

    private void OnOverlayInputPaneStateChanged(object? sender, InputPaneStateEventArgs eventArgs)
    {
        _overlayInputPaneOpen = eventArgs.NewState == InputPaneState.Open;
        _overlayInputPaneRect = _overlayInputPaneOpen ? eventArgs.EndRect : default;
        RefreshOverlays();
        if (_overlayInputPaneOpen)
        {
            // The placement write above triggers a new arrange. Reveal in the loaded queue,
            // after the scroller knows the viewport it actually has above the keyboard.
            Dispatcher.UIThread.Post(RevealFocusedOverlayControl, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Scrolls the editor that raised the keyboard wholly into its sheet body.</summary>
    private void RevealFocusedOverlayControl()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Control focused ||
            focused.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller ||
            focused.TranslatePoint(default, scroller) is not { } origin)
        {
            return;
        }

        var viewportHeight = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : scroller.Bounds.Height;
        var topLevel = TopLevel.GetTopLevel(this);
        if (_overlayInputPaneOpen &&
            _overlayInputPaneRect.Height > 0 &&
            topLevel is not null &&
            scroller.TranslatePoint(default, topLevel) is { } scrollerOrigin)
        {
            viewportHeight = ResolveUnoccludedScrollerHeight(
                viewportHeight,
                scrollerOrigin.Y,
                _overlayInputPaneRect.Y);
        }

        if (viewportHeight <= 0)
        {
            return;
        }

        var clearance = Math.Min(8, Math.Max(0, (viewportHeight - focused.Bounds.Height) / 2));
        var top = origin.Y;
        var bottom = top + focused.Bounds.Height;
        var delta = bottom > viewportHeight - clearance
            ? bottom - (viewportHeight - clearance)
            : top < clearance
                ? top - clearance
                : 0;
        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        var maximum = Math.Max(0, scroller.Extent.Height - viewportHeight);
        scroller.Offset = new Vector(
            scroller.Offset.X,
            Math.Clamp(scroller.Offset.Y + delta, 0, maximum));
    }

    private void RemoveOverlay(Control root)
    {
        _overlays.RemoveAll(entry => ReferenceEquals(entry.Root, root));
        _overlayHost.Children.Remove(root);
        _overlayHost.IsVisible = _overlayHost.Children.Count > 0;
        ApplyOverlayModality();
    }

    /// <summary>
    /// Takes the workspace out of the accessibility tree for as long as a sheet is over it.
    /// </summary>
    /// <remarks>
    /// The scrim catches pointer input, so touch was already safe — but nothing marked the
    /// sheet as modal, so with a sheet open every control underneath it was still reported
    /// clickable and enabled: Open log, Live, More, the mode buttons, Load next 500 and
    /// every entry row. Assistive technology walks past a scrim it cannot see and can
    /// activate what it finds there (audit 2, B4).
    ///
    /// The two attached properties were the wrong instrument for it and had no measurable
    /// effect: both describe a single peer and neither is inherited, so the band left the
    /// control view while its twenty descendants were promoted in its place and stayed
    /// clickable (audit 3, B3). <see cref="ModalWorkspaceBand"/> answers it where every
    /// platform bridge actually asks — the band reports no children while it is sealed. The
    /// attached properties are still set, because they are true, cost nothing, and are what a
    /// UIA client reads first.
    ///
    /// The overlay host itself is never made inert, so the sheet on top stays reachable, and
    /// the state is recomputed rather than toggled, so two stacked sheets closing in any
    /// order still restore the workspace exactly once.
    /// </remarks>
    private void ApplyOverlayModality()
    {
        var covered = _overlays.Count > 0;
        _rootPanel.IsSealedForModal = covered;

        // Nothing behind the scrim can be dragged, so nothing behind it has any business
        // holding the platform's edge gesture — least of all while the reader has a layer
        // open and Back is the gesture they are most likely to make (V2-21).
        Platform.EdgeGestureGuard.Suspend(covered);
        AutomationProperties.SetAccessibilityView(
            _rootPanel,
            covered ? AccessibilityView.Raw : AccessibilityView.Content);
        AutomationProperties.SetIsOffscreenBehavior(
            _rootPanel,
            covered ? IsOffscreenBehavior.Offscreen : IsOffscreenBehavior.Default);
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
    /// <summary>
    /// Forces the in-page dialog host on or off, for tests that need to exercise it.
    /// </summary>
    /// <remarks>
    /// The phone has no windows and gets a card inside the page; the desktop opens a modal
    /// <see cref="Window"/>. A headless run is neither, and it took the window path — so the
    /// overlay stack, the sheet's Back contract and the modality seal had no test coverage at
    /// all, which is how V2-23 reached a device. Null means "ask the platform", which is what
    /// every shipping build does.
    /// </remarks>
    internal static bool? InPageDialogOverride { get; set; }

    public async Task<TResult?> ShowDialogAsync<TResult>(DialogBody<TResult> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        body.Host = this;
        var inPage = InPageDialogOverride ?? OperatingSystem.IsAndroid();
        if (!inPage && TopLevel.GetTopLevel(this) is Window owner)
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
            out var surface,
            scrolls: !body.ScrollsInternally,
            showClose: false);
        PushOverlay(card, body.Dismiss, surface);
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
        _ = sender;
        if (TryNavigateBack())
        {
            eventArgs.Handled = true;
        }
    }

    /// <summary>
    /// Peels the topmost layer the reader has open, in the order they stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one implementation of Back in the application. It answers the toolkit's
    /// <see cref="TopLevel.BackRequestedEvent"/> and, on Android, the activity's own
    /// <c>OnBackPressedDispatcher</c> callback — which exists because a stock
    /// gesture-navigation Pixel walked past an open <em>More actions</em> sheet and an open
    /// <em>Appearance</em> card straight to the launcher, while the identical layers closed
    /// correctly for a <c>KEYCODE_BACK</c> press on the same build (V2-21). Two entry points
    /// and one decision is what makes the answer independent of which of them the platform
    /// used.
    /// </para>
    /// <para>
    /// The order is the order the layers are drawn in: an in-page dialog or sheet is over the
    /// workspace, and the workspace's own transient state — the filter drawer, an expanded
    /// inspector — is under it. <see langword="false"/> means there was nothing open, and the
    /// caller lets the platform do what it does everywhere else.
    /// </para>
    /// </remarks>
    internal bool TryNavigateBack()
    {
        // The same press, already answered. Android delivers a key Back as two events — a
        // Key.Escape key-down and, about ten milliseconds later, the back request — and the
        // key-down runs first. So the workspace gave up its filter drawer to the key-down,
        // and the request arrived to find nothing left to dismiss and let the platform
        // background the task: Back both closed the drawer and left the app, with a half-typed
        // query going with it (audit 3, C1). One press, one decision.
        if (TakeDismissedByEscape())
        {
            return true;
        }

        if (DismissTopOverlay())
        {
            return true;
        }

        return ActiveWorkspace is { } workspace && workspace.TryDismissTransientState();
    }

    /// <summary>
    /// When the workspace last dismissed something for an <see cref="Key.Escape"/> press.
    /// </summary>
    /// <remarks>
    /// Only ever written by the key-down path and only ever read once, by the back-request
    /// that follows the same press, so a second Back press cannot inherit the first one's
    /// answer. The window is generous against the ~10 ms the platform actually takes and far
    /// short of any interval a person could press Back twice in.
    /// </remarks>
    private long _escapeDismissedAt = long.MinValue;

    private const long EscapeEchoWindowMs = 400;

    /// <summary>Records that an Escape key-down did the work a Back press was asking for.</summary>
    internal void NoteDismissedByEscape() => _escapeDismissedAt = Environment.TickCount64;

    /// <summary>Consumes that record, if it is recent enough to belong to this press.</summary>
    private bool TakeDismissedByEscape()
    {
        var when = _escapeDismissedAt;
        _escapeDismissedAt = long.MinValue;
        return when != long.MinValue && Environment.TickCount64 - when < EscapeEchoWindowMs;
    }

    /// <summary>
    /// The workspace the reader is looking at.
    /// </summary>
    /// <remarks>
    /// Back dismissed a modal sheet correctly and left the app entirely from the open filter
    /// drawer — the surface a reader is in most often, with a half-typed query in it. Both
    /// paths run in the same handler and the sheet's does not need a workspace, which puts the
    /// fault in the one step the drawer's path adds: reading the workspace back out of the tab
    /// control (audit 3, C1).
    ///
    /// So it is not read out of the tab control any more, or not only. The selection the shell
    /// itself keeps is asked first, the tab control second, and a lone open session answers
    /// for itself — with one session open there is no ambiguity for a lookup to get wrong. A
    /// silent <c>as</c> cast deciding whether the system Back gesture works is not a thing to
    /// leave one step deep.
    /// </remarks>
    private SessionWorkspaceView? ActiveWorkspace
    {
        get
        {
            if (_viewModel.Selected is { } selected &&
                _tabItems.TryGetValue(selected, out var item) &&
                item.Content is SessionWorkspaceView selectedWorkspace)
            {
                return selectedWorkspace;
            }

            if ((_tabs.SelectedItem as TabItem)?.Content is SessionWorkspaceView tabWorkspace)
            {
                return tabWorkspace;
            }

            return _tabItems.Count == 1
                ? _tabItems.Values.First().Content as SessionWorkspaceView
                : null;
        }
    }
}
