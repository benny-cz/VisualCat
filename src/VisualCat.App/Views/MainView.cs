using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Domain;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Configuration;
using VisualCat.Infrastructure.Diagnostics;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Views;

public sealed partial class MainView : UserControl, IAsyncDisposable
{
    private readonly WorkspaceViewModel _viewModel = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _message = new();
    private readonly Border _emptyState = new();
    private readonly ModalWorkspaceBand _rootPanel = new();
    private readonly Dictionary<SessionTabViewModel, TabItem> _tabItems = [];
    private readonly string[] _startupPaths;
    private readonly SettingsStore _settingsStore;
    private ApplicationSettings _settings = new();
    private RollingDiagnosticLogger? _diagnostics;
    private bool _startupOpened;
    private bool _settingsLoaded;
    private Window? _hostWindow;
    private readonly Action<IReadOnlyList<string>> _launchFilesHandler;
    private readonly Action _appResumedHandler;
    private readonly Action _appPausedHandler;
    private readonly Action _displayConfigurationHandler;

    // Responsive command bar: primary actions are always inline, flexible actions render as
    // buttons while they fit and fold into "More" when they do not, and settings live in
    // "More" permanently. Widths are cached from the first arrange so a resize only re-picks
    // the split, never re-measures.
    private readonly StackPanel _toolbar = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,

        // Clipping keeps a mid-reflow row from spilling past the command bar on the desktop,
        // where the row genuinely competes for width. On Android the row is three fixed stops
        // — Open log, Live, More — so it has nothing to clip, and clipping also clips pointer
        // input: the review found the two primary buttons reporting a 10 px hit box while
        // rendering 123 px tall, and synthetic taps landing on nothing (finding 8). This
        // removes the clip as a possible cause; the anomaly itself needs a device to confirm.
        ClipToBounds = !OperatingSystem.IsAndroid(),
    };
    private readonly List<Button> _toolbarPrimary = [];
    private readonly List<ToolbarCommand> _toolbarFlexible = [];
    private readonly List<MenuItem> _toolbarSettings = [];
    private readonly MenuItem _moreItem = new()
    {
        Header = "More  ▾",
        MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
    };
    private readonly StackPanel _commandContent = new() { Spacing = OperatingSystem.IsAndroid() ? 8 : 4 };
    private readonly Dictionary<Control, double> _toolbarWidths = [];
    private Menu? _moreMenu;
    private Grid? _brandRow;
    private Border? _commandBar;
    private TextBlock? _brandWordmark;
    private TextBlock? _brandStrapline;

    // Every shell control that paints itself rather than inheriting a themed brush, so a
    // theme change can reach all of them instead of the four ApplyThemeSurfaces used to
    // know about (audit 2, A1b).
    private readonly List<ShellButton> _shellButtons = [];

    /// <summary>A shell action and which of the two appearances it carries.</summary>
    private sealed record ShellButton(Button Button, bool Primary);
    private double _lastToolbarWidth = -1;
    private bool _reflowingToolbar;
    private bool _mobileCompactHeight;
    private StackPanel? _recentList;
    private TextBlock? _recentHeading;
    private Control? _recentSection;
    private Button? _recentShowAll;

    private static string DiagnosticsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualCat",
        "Diagnostics");

    public MainView(IEnumerable<string>? startupPaths = null)
    {
        _startupPaths = startupPaths?.ToArray() ?? [];
        _settingsStore = new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualCat",
            "settings.json"));
        _launchFilesHandler = paths => Dispatcher.UIThread.Post(() => _ = OpenPathsAsync(paths));
        _appResumedHandler = () => Dispatcher.UIThread.Post(() =>
        {
            RestoreAndroidLayoutAfterResume();

            // Ordered after the layout restore so the first refreshed frame lands in the
            // layout the user left, not the one being rebuilt underneath it.
            _viewModel.ResumeLiveViews();
        });

        // Suspension is not posted to the dispatcher: OnPause is the last moment the app
        // reliably gets before the process is frozen, and a queued message may not run.
        _appPausedHandler = () =>
        {
            _viewModel.SuspendLiveViews();
            PersistOpenWorkspaceOnPause();
        };
        _displayConfigurationHandler = () =>
            Dispatcher.UIThread.Post(ApplyDisplayConfigurationChange);
        PlatformSourceRegistry.LaunchFilesReceived += _launchFilesHandler;
        PlatformSourceRegistry.AppResumed += _appResumedHandler;
        PlatformSourceRegistry.AppPaused += _appPausedHandler;
        PlatformSourceRegistry.DisplayConfigurationChanged += _displayConfigurationHandler;
        Content = Build();
        SizeChanged += (_, eventArgs) => UpdateMobileChrome(eventArgs.NewSize);
        ActualThemeVariantChanged += (_, _) => ApplyThemeSurfaces();
        ApplyThemeSurfaces();
        _viewModel.TabAdded += (_, tab) => Dispatcher.UIThread.Post(() => AddTab(tab));
        _viewModel.TabRemoved += (_, tab) => Dispatcher.UIThread.Post(() => RemoveTab(tab));

        // A recording that stops without being asked to is the one capture outcome the reader
        // cannot see for themselves, so it goes in the lane that stays until dismissed.
        _viewModel.CaptureEndedUnprompted += (_, message) =>
            Dispatcher.UIThread.Post(() => ShowNotice(message, NoticeKind.Failure));
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem is TabItem { Tag: SessionTabViewModel tab })
            {
                _viewModel.Selected = tab;
            }

            UpdateSessionStrip();
            UpdateSessionActionAvailability();
            PersistOpenWorkspace();
        };
        AttachedToVisualTree += (_, _) =>
        {
            // The gesture is raised on the top level, so the handler belongs there; a
            // descendant never sees it.
            TopLevel.GetTopLevel(this)?.AddHandler(TopLevel.BackRequestedEvent, OnBackRequested);
            ObserveSafeArea();

            // ActualThemeVariant is only meaningful once there is a top level to inherit it
            // from: everything built in the constructor resolved it as Default, which the
            // "is it Light?" test reads as dark, so a cold start on a phone set to light was
            // served the dark palette and no variant change ever arrived to correct it
            // (audit 2, A1c). Re-running once the tree has settled is what makes a cold
            // start in either variant produce the same result as a switch into it.
            Dispatcher.UIThread.Post(ApplyThemeSurfaces, DispatcherPriority.Loaded);
            if (!_startupOpened)
            {
                _startupOpened = true;
                _ = InitializeAsync();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            TopLevel.GetTopLevel(this)?.RemoveHandler(TopLevel.BackRequestedEvent, OnBackRequested);
            StopObservingSafeArea();
        };
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The device changed how the app is displayed, and this process is still the one running.
    /// </summary>
    /// <remarks>
    /// Android used to answer a text-size change by destroying the activity. That took the
    /// live capture with it without a word, and left a large session showing an empty list
    /// under the word "Ready" for ten seconds while it was reopened from disk (audit 3, A2 and
    /// C3). The activity now handles the change, and this is the whole of what the recreation
    /// was actually achieving: the shell re-states its own sizes, and each workspace is built
    /// again at the new scale over the session it was already showing.
    ///
    /// Only a scale change rebuilds. Density, locale and layout direction are handled in place
    /// as well but nothing in the product reads them, and rebuilding four panes over a
    /// million-entry session for a change that cannot alter a single pixel would be its own
    /// small version of the defect.
    /// </remarks>
    private void ApplyDisplayConfigurationChange()
    {
        var before = TextScale.Effective;
        ApplyAppearance();
        if (Math.Abs(TextScale.Effective - before) > 0.001)
        {
            RebuildWorkspaceViews();
        }

        UpdateMobileChrome(Bounds.Size);
    }

    /// <summary>
    /// Replaces every open session's view, keeping the session itself — and its capture.
    /// </summary>
    /// <remarks>
    /// A workspace resolves every font size in it while it is being constructed, so a scale
    /// the reader has just changed reaches the screen by building the view again and no other
    /// way. What must <em>not</em> be rebuilt is the session: the tab view model owns the
    /// store, the snapshot and the running capture, and it is the same object before and
    /// after. That is the difference between this and the activity recreation it replaces —
    /// the reader keeps their recording, their million rows stay in memory, and the workspace
    /// mode they chose comes across with them.
    /// </remarks>
    private void RebuildWorkspaceViews()
    {
        foreach (var (viewModel, item) in _tabItems.ToArray())
        {
            if (item.Content is not SessionWorkspaceView previous)
            {
                continue;
            }

            var mode = previous.CurrentDisplayMode;
            previous.DetachViewModel();
            item.Content = CreateWorkspaceView(viewModel, mode);
        }

        UpdateSessionActionAvailability();
    }

    private void RestoreAndroidLayoutAfterResume()
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        // Android may recreate its drawing surface with a transient arrange slot while
        // returning from Recents. Reapply the settled bounds and invalidate the whole host
        // once the resumed UI queue is running so no stale compact/landscape arrange survives.
        UpdateMobileChrome(Bounds.Size);
        _rootPanel.InvalidateMeasure();
        _rootPanel.InvalidateArrange();
    }

    public void AttachHostWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _hostWindow = window;
        if (_settingsLoaded)
        {
            ApplyWindowSettings(window);
        }
    }

    public async Task PersistWindowStateAsync(CancellationToken cancellationToken = default)
    {
        if (!_settingsLoaded || _hostWindow is not { } window)
        {
            return;
        }

        var updated = _settings with { WindowMaximized = window.WindowState == WindowState.Maximized };
        if (window.WindowState == WindowState.Normal &&
            window.Bounds.Width >= window.MinWidth &&
            window.Bounds.Height >= window.MinHeight)
        {
            updated = updated with
            {
                WindowWidth = window.Bounds.Width,
                WindowHeight = window.Bounds.Height,
            };
        }

        _settings = updated;
        await SaveSettingsAsync(cancellationToken);
    }

    /// <summary>
    /// Repaints every shell surface that owns its own brushes for the variant now in force.
    /// </summary>
    /// <remarks>
    /// This used to touch four things — the root, the system bar, the empty state and the
    /// notice lane — and the command bar was excluded on purpose, as the application's
    /// identity band. Everything else it missed by accident: switching a running app to
    /// light left the session tabs at 1.10:1, the entry metadata at 1.11:1 and the minimap a
    /// solid navy rectangle on a white page, and only a restart put them right (audit 2, A1).
    ///
    /// The rule now is that a surface painted in code is repainted here, and the list of
    /// them is not a list this method has to keep: the tab strip, the notice lane, the empty
    /// state and each open workspace all own one entry point that this calls.
    /// </remarks>
    private void ApplyThemeSurfaces()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var workspaceSurface = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.Surface(dark));
        Background = workspaceSurface;
        _rootPanel.Background = workspaceSurface;
        ApplySystemBarSurface();
        ApplyCommandBarTheme(dark);
        _emptyState.Child = BuildEmptyState(dark);
        ApplyNoticeTheme();
        UpdateSessionStrip();
    }

    /// <summary>Paints the command bar, its wordmark and its actions for the variant.</summary>
    private void ApplyCommandBarTheme(bool dark)
    {
        if (_commandBar is { } bar)
        {
            bar.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(VisualCat.App.Timeline.WorkspacePalette.ShellTop(dark), 0),
                    new GradientStop(VisualCat.App.Timeline.WorkspacePalette.ShellBottom(dark), 1),
                },
            };
            bar.BorderBrush = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.ShellEdge(dark));
        }

        if (_brandWordmark is { } wordmark)
        {
            wordmark.Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.ShellText(dark));
        }

        if (_brandStrapline is { } strapline)
        {
            strapline.Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.ShellTextMuted(dark));
        }

        _message.Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.ShellTextMuted(dark));
        if (_moreMenu is { } menu)
        {
            menu.Background = new SolidColorBrush(
                VisualCat.App.Timeline.WorkspacePalette.SecondaryActionFill(dark));
        }

        foreach (var (button, primary) in _shellButtons)
        {
            ApplyShellButtonTheme(button, primary, dark);
        }
    }

    private static void ApplyShellButtonTheme(Button button, bool primary, bool dark)
    {
        button.Background = new SolidColorBrush(primary
            ? VisualCat.App.Timeline.WorkspacePalette.PrimaryActionFill(dark)
            : VisualCat.App.Timeline.WorkspacePalette.SecondaryActionFill(dark));
        button.BorderBrush = new SolidColorBrush(primary
            ? VisualCat.App.Timeline.WorkspacePalette.PrimaryActionEdge(dark)
            : VisualCat.App.Timeline.WorkspacePalette.SecondaryActionEdge(dark));
        button.Foreground = new SolidColorBrush(primary
            ? VisualCat.App.Timeline.WorkspacePalette.PrimaryActionText(dark)
            : VisualCat.App.Timeline.WorkspacePalette.TextPrimary(dark));
    }

    private Grid Build()
    {
        var root = _rootPanel;

        var commandContent = _commandContent;
        var brandRow = _brandRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center,
        };
        brand.Children.Add(new Border
        {
            Width = 29,
            Height = 29,
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#43B4FF"), 0),
                    new GradientStop(Color.Parse("#A78BFA"), 1),
                },
            },
            Child = new TextBlock
            {
                Text = "V",
                FontWeight = FontWeight.Black,
                FontSize = TextScale.Of(17),
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        _brandWordmark = new TextBlock
        {
            Text = "VISUALCAT",
            FontWeight = FontWeight.Bold,
            FontSize = TextScale.Of(15),
        };
        _brandStrapline = new TextBlock
        {
            Text = "LOGCAT SIGNAL ANALYZER",
            FontSize = TextScale.Of(8),
        };
        brand.Children.Add(new StackPanel
        {
            Spacing = -2,
            Children = { _brandWordmark, _brandStrapline },
        });
        brandRow.Children.Add(brand);
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(_message, 2);
        brandRow.Children.Add(_message);
        commandContent.Children.Add(brandRow);

        commandContent.Children.Add(BuildActionToolbar());

        // Both brushes are supplied by ApplyCommandBarTheme, which runs before the first
        // frame and again on every variant change.
        var commandBar = _commandBar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = CommandBarPadding(compact: false),
            Child = commandContent,
        };
        DockPanel.SetDock(commandBar, Dock.Top);
        root.Children.Add(commandBar);
        var sessionStrip = BuildSessionStrip();
        DockPanel.SetDock(sessionStrip, Dock.Top);
        root.Children.Add(sessionStrip);

        // Docked rather than floated: a message that covers the status line it was raised
        // beside answers one question by hiding another (finding 5).
        var notice = BuildNotice();
        DockPanel.SetDock(notice, Dock.Bottom);
        root.Children.Add(notice);
        var workspaceHost = new Grid();
        workspaceHost.Children.Add(_tabs);
        // The overlay carries the get-started links now, so it must receive clicks. It is
        // only ever visible while no session is open (see the IsVisible toggles), and its
        // Border has no background, so its empty area still passes pointer events through to
        // the tab host beneath — only the links themselves are hit targets.
        _emptyState.SetValue(Panel.ZIndexProperty, 1);
        workspaceHost.Children.Add(_emptyState);
        root.Children.Add(workspaceHost);

        InitializeOverlayModality();

        // Sheets and dialogs live in the ordinary tree above the workspace, so automation can
        // walk them and the system Back gesture can take them down (findings 8 and 20).
        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(_overlayHost);
        return shell;
    }

    private static Thickness CommandBarPadding(bool compact)
    {
        if (!OperatingSystem.IsAndroid())
        {
            return new Thickness(12, 8);
        }

        // Keep the three primary mobile actions comfortably separated from the shell edge,
        // but collapse vertical chrome before the log/insights viewport in compact-height
        // landscape. Touch-target height is owned by the buttons themselves, not this padding.
        return compact ? new Thickness(8, 4) : new Thickness(10, 6);
    }

    private void UpdateMobileChrome(Size size)
    {
        if (!OperatingSystem.IsAndroid() || _brandRow is null || _commandBar is null)
        {
            return;
        }

        var compactHeight = size.Width > size.Height || size.Height < MobileWorkspaceLayout.CompactHeightBreakpoint;
        var sessionOpen = _tabs.Items.Count > 0;
        var compositionChanged = _mobileCompactHeight != compactHeight;
        _mobileCompactHeight = compactHeight;
        // Once a session is open its tab title is the identity that matters. Removing the
        // decorative brand row recovers a full touch row in portrait without hiding any
        // command; the empty/home state still carries the complete VisualCat masthead.
        _brandRow.IsVisible = !compactHeight && !sessionOpen;
        _commandContent.Spacing = compactHeight || sessionOpen ? 0 : 8;
        _commandBar.Padding = CommandBarPadding(compact: compactHeight || sessionOpen);
        // Session tabs remain a compact top row. A side rail looks efficient on paper, but
        // wastes a large column when a phone has the common one-session workspace.
        _tabs.TabStripPlacement = Dock.Top;
        UpdateSessionStrip();

        if (compositionChanged)
        {
            _lastToolbarWidth = -1;
            Dispatcher.UIThread.Post(() => ReflowToolbar(_toolbar.Bounds.Width));
        }
    }

    private StackPanel BuildEmptyState(bool dark)
    {
        var levelLegend = new WrapPanel
        {
            ItemSpacing = 8,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var level in VisualCat.Domain.Entries.LogLevels.DisplayOrder)
        {
            if (level == VisualCat.Domain.Entries.LogLevel.Unknown)
            {
                continue;
            }

            var parsed = VisualCat.App.Timeline.LevelPalette.ColorOf(level);
            levelLegend.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(36, parsed.R, parsed.G, parsed.B)),
                BorderBrush = new SolidColorBrush(parsed),
                BorderThickness = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 5),
                Child = new TextBlock
                {
                    Text = level.ToString().ToUpperInvariant(),
                    FontSize = TextScale.Of(9),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(parsed),
                },
            });
        }

        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 680,
            Spacing = 15,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = "SEE THE SHAPE OF YOUR LOG",
                    FontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 22 : 28),
                    FontWeight = FontWeight.Bold,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextPrimary(dark)),
                },
                new TextBlock
                {
                    Text = "Turn raw Android logcat into a navigable severity × time signal.",
                    FontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 13 : 15),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                },
                levelLegend,
                BuildHeroActions(dark),
                BuildRecentSection(dark),
                new TextBlock
                {
                    Text = $"VisualCat {ProductInfo.DisplayVersion} · local-first · no telemetry",
                    FontSize = TextScale.Of(10),
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                    Opacity = 0.8,
                },
            },
        };
    }

    /// <summary>
    /// The captures this device already holds, on the screen a cold start opens.
    /// </summary>
    /// <remarks>
    /// A process restart drops every open tab — including a 115 MB capture that took 25 s to
    /// import — and left the app on this screen, whose only content was a static severity
    /// legend. The sessions were still on disk and reachable, but only through a menu item
    /// inside a flyout, and nothing here hinted that they existed (finding 21). Now the
    /// shortest route to yesterday's capture is the first screen of the app.
    /// </remarks>
    private Control BuildRecentSection(bool dark)
    {
        var heading = _recentHeading = new TextBlock
        {
            Text = "RECENT CAPTURES ON THIS DEVICE",
            FontSize = TextScale.Of(10),
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
        };
        var list = _recentList = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // The heading states that some captures are not listed; the way to see them has to be
        // beside the list that omits them, not three rows above it (finding 21.9).
        var showAll = _recentShowAll = HeroLink(
            "SHOW ALL CAPTURES",
            OpenRecentAsync,
            "List every capture this device holds",
            dark);
        showAll.HorizontalAlignment = HorizontalAlignment.Center;
        showAll.IsVisible = false;
        var section = _recentSection = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = false,
            Children = { heading, list, showAll },
        };
        _ = RefreshRecentSessionsAsync();
        return section;
    }

    /// <summary>
    /// Fills the recent-captures list. Failures are silent by design: this is a convenience on
    /// a screen whose other routes all still work, and a cache that cannot be scanned is not
    /// worth an error banner on the first screen of the app.
    /// </summary>
    private async Task RefreshRecentSessionsAsync()
    {
        if (_recentList is not { } list || _recentSection is not { } section)
        {
            return;
        }

        IReadOnlyList<TemporarySessionInfo> sessions;
        try
        {
            sessions = await TemporarySessionRetentionService.ScanAsync(WorkspaceViewModel.TemporarySessionRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!ReferenceEquals(_recentList, list))
        {
            // The empty state was rebuilt (a theme change) while the scan was running.
            return;
        }

        var dark = ActualThemeVariant != ThemeVariant.Light;
        var recent = sessions
            .OrderByDescending(static session => session.UpdatedUtc)
            .Take(4)
            .ToArray();
        list.Children.Clear();
        foreach (var session in recent)
        {
            list.Children.Add(BuildRecentEntry(session, dark));
        }

        section.IsVisible = recent.Length > 0;
        var hasMore = sessions.Count > recent.Length;
        if (_recentHeading is { } heading && recent.Length > 0)
        {
            heading.Text = hasMore
                ? $"RECENT CAPTURES ON THIS DEVICE · {recent.Length} OF {sessions.Count:N0}"
                : "RECENT CAPTURES ON THIS DEVICE";
        }

        if (_recentShowAll is { } showAll)
        {
            showAll.IsVisible = hasMore && recent.Length > 0;
            Avalonia.Automation.AutomationProperties.SetName(
                showAll,
                $"Show all {sessions.Count:N0} captures on this device");
        }
    }

    private Button BuildRecentEntry(TemporarySessionInfo session, bool dark)
    {
        var name = SessionCacheName.Describe(session.Path);

        // The same sentence Recent sessions and Session cache use. Three lists describing the
        // same three captures in three vocabularies was the whole of E2.
        var detail = SheetForm.DescribeSessionState(session, CapturingSessionPaths().Contains(session.Path));
        var button = new Button
        {
            MinHeight = OperatingSystem.IsAndroid() ? 56 : 0,
            Padding = new Thickness(10, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.SurfaceRaised(dark)),
            BorderBrush = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.BorderLine(dark)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = name,
                        FontSize = TextScale.Of(12.5),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextPrimary(dark)),
                    },
                    new TextBlock
                    {
                        Text = detail,
                        FontSize = TextScale.Of(10.5),
                        Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                    },
                },
            },
        };
        Avalonia.Automation.AutomationProperties.SetName(button, $"Reopen {name}, {detail}");
        ToolTip.SetTip(button, session.Path);
        button.Click += async (_, _) => await RunAsync(() => _viewModel.OpenSessionAsync(session.Path));
        return button;
    }

    /// <summary>
    /// The get-started row. These read as accent links because they are links: each is a
    /// real, keyboard-focusable control wired to the same handler as its toolbar button, so
    /// the affordance the styling promises is honoured instead of being a dead caption.
    /// </summary>
    private WrapPanel BuildHeroActions(bool dark)
    {
        var row = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemSpacing = 8,
            LineSpacing = 4,
        };

        var links = new List<(string Label, Func<Task> Action, string Tip)>();
        if (OperatingSystem.IsAndroid())
        {
            links.Add(("OPEN LOG", OpenLogAsync, "Open a saved logcat file"));
            if (PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                links.Add(("ON-DEVICE LIVE", StartOnDeviceAsync, "Capture this device's log live"));
            }

            // SHARE used to sit here and do nothing at all: by definition there is never a
            // session to share on the screen that exists because no session is open. The
            // route that does exist on this screen is the one below it (findings 19 and 21).
            links.Add(("RECENT CAPTURES", OpenRecentAsync, "Reopen a capture this device already holds"));
        }
        else
        {
            links.Add(("OPEN LOG", OpenLogAsync, "Open a saved logcat file"));
            links.Add(("ADB LIVE", StartAdbAsync, "Capture live from a device over ADB"));
            links.Add(("REOPEN SESSION", OpenRecentAsync, "Reopen a recent session"));
        }

        for (var index = 0; index < links.Count; index++)
        {
            if (index > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "·",
                    FontSize = TextScale.Of(11),
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                });
            }

            row.Children.Add(HeroLink(links[index].Label, links[index].Action, links[index].Tip, dark));
        }

        return row;
    }

    private static Button HeroLink(string text, Func<Task> action, string tip, bool dark)
    {
        var accent = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.Accent(dark));
        var hover = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextPrimary(dark));
        var label = new TextBlock
        {
            Text = text,
            FontSize = TextScale.Of(11),
            FontWeight = FontWeight.Bold,
            Foreground = accent,
        };
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(3, 3),
            MinHeight = 0,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, text);
        button.PointerEntered += (_, _) =>
        {
            label.Foreground = hover;
            label.TextDecorations = TextDecorations.Underline;
        };
        button.PointerExited += (_, _) =>
        {
            label.Foreground = accent;
            label.TextDecorations = null;
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private Button ActionButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
        };
        ApplyShellButtonTheme(button, primary, ActualThemeVariant != ThemeVariant.Light);
        _shellButtons.Add(new ShellButton(button, primary));
        button.Click += async (_, _) => await action();
        return button;
    }

    private StackPanel BuildActionToolbar()
    {
        void Primary(string label, Func<Task> action)
        {
            var button = ActionButton(label, action, primary: true);
            _toolbarPrimary.Add(button);
            _toolbar.Children.Add(button);
        }

        void Flexible(
            string buttonLabel,
            string menuLabel,
            Func<Task> action,
            string? description = null,
            Func<bool>? canExecute = null,
            CommandGroup group = CommandGroup.ThisSession)
        {
            var command = new ToolbarCommand(
                ActionButton(buttonLabel, action),
                MenuAction(menuLabel, action),
                canExecute);
            _toolbarFlexible.Add(command);
            _toolbar.Children.Add(command.Button);
            _secondaryCommands.Add(
                new CommandDescriptor(menuLabel, description, action, canExecute, IsSetting: false, group));
        }

        void Setting(string menuLabel, Func<Task> action, string? description = null)
        {
            _toolbarSettings.Add(MenuAction(menuLabel, action));
            _secondaryCommands.Add(new CommandDescriptor(
                menuLabel,
                description,
                action,
                null,
                IsSetting: true,
                CommandGroup.Settings));
        }

        Primary("＋  Open log", OpenLogAsync);
        if (OperatingSystem.IsAndroid())
        {
            if (PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                Primary("●  Live", StartOnDeviceAsync);
            }

            // These three open a different session, so they do not belong under "THIS
            // SESSION" with Share and Export CSV (finding 21.1).
            Flexible(
                "Recent",
                "Recent sessions…",
                OpenRecentAsync,
                "Reopen a capture this device already holds",
                group: CommandGroup.Open);
            Flexible(
                "Open archive",
                "Open portable archive…",
                OpenArchiveAsync,
                "Open a .vcat.zip someone shared",
                group: CommandGroup.Open);
            Flexible(
                "Open session",
                "Open session…",
                OpenSessionAsync,
                "Open a .vcat session folder",
                group: CommandGroup.Open);
            if (PlatformSourceRegistry.ShareFileAsync is not null)
            {
                Flexible(
                    "Share",
                    "Share…",
                    SharePortableAsync,
                    "Hand this session to another app as a portable archive",
                    CanSaveOrShareSelectedSession);
            }

            Flexible(
                "Export",
                "Export CSV…",
                () => ExportAsync(),
                // The sheet promised "the filtered entries" and then opened a dialog whose
                // default answer is the entries in view — a different and usually much
                // smaller set (audit 2, E4). The command opens a question; it says so.
                "Choose which entries to write, then save a CSV",
                CanExportSelectedSession);
        }
        else
        {
            Primary("●  ADB live", StartAdbAsync);
            Flexible("Open session", "Open session…", OpenSessionAsync, group: CommandGroup.Open);
            Flexible("Recent", "Recent sessions…", OpenRecentAsync, group: CommandGroup.Open);
            Flexible("Follow file", "Follow growing file…", FollowFileAsync, group: CommandGroup.Open);
            Flexible("Open archive", "Open portable archive…", OpenArchiveAsync, group: CommandGroup.Open);
            Flexible(
                "Save",
                "Save session…",
                () => SaveSessionAsync(portable: false),
                canExecute: CanSaveOrShareSelectedSession);
            Flexible(
                "Save portable",
                "Save portable…",
                () => SaveSessionAsync(portable: true),
                canExecute: CanSaveOrShareSelectedSession);
            Flexible("Export", "Export CSV…", () => ExportAsync(), canExecute: CanExportSelectedSession);
        }

        Setting("Appearance & timeline…", ShowAppearanceAsync, "Theme, text size, and how the plot is drawn");
        Setting("Session cache…", ShowSessionCacheAsync, "What this device is storing, and for how long");
        Setting("Diagnostic bundle…", CreateDiagnosticBundleAsync, "A redacted zip for a bug report");

        if (OperatingSystem.IsAndroid())
        {
            // A button and a sheet rather than a Menu and a flyout: see OpenCommandSheet.
            var more = ActionButton("More  ▾", () =>
            {
                OpenCommandSheet();
                return Task.CompletedTask;
            });
            Avalonia.Automation.AutomationProperties.SetName(more, "More actions");
            _toolbar.Children.Add(more);
        }
        else
        {
            _moreMenu = new Menu
            {
                VerticalAlignment = VerticalAlignment.Center,
                Items = { _moreItem },
            };
            _toolbar.Children.Add(_moreMenu);
        }

        _toolbar.SizeChanged += (_, args) => ReflowToolbar(args.NewSize.Width);
        UpdateSessionActionAvailability();
        return _toolbar;
    }

    private bool CanSaveOrShareSelectedSession() => _viewModel.Selected?.Snapshot is not null;

    private bool CanExportSelectedSession() =>
        _viewModel.Selected?.Snapshot is not null && _viewModel.Selected.Viewport is not null;

    /// <summary>
    /// Keeps a session-dependent command enabled only while there is a session for it to act
    /// on. Each of Share, Export CSV and Save returned silently when no session was loaded,
    /// while their controls stayed fully enabled — a command that looks available and does
    /// nothing is indistinguishable from one that is broken (finding 19).
    /// </summary>
    private void UpdateSessionActionAvailability()
    {
        foreach (var command in _toolbarFlexible)
        {
            if (command.CanExecute is not { } canExecute)
            {
                continue;
            }

            var enabled = canExecute();
            command.Button.IsEnabled = enabled;
            command.MenuItem.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// Re-picks which flexible actions render as buttons and which fold into "More" for the
    /// current width. Primaries stay inline and settings stay in the menu; everything between
    /// is shown in order until the row is full, so the toolbar uses the space it has instead
    /// of hiding actions behind "More" while the row sits half empty.
    /// </summary>
    private void ReflowToolbar(double available)
    {
        if (OperatingSystem.IsAndroid())
        {
            // A phone command bar is three stops wide — Open log, Live, More — in both
            // orientations. Every secondary command lives in the sheet, where it has a name,
            // a description, and an enabled state a screen reader can read.
            foreach (var command in _toolbarFlexible)
            {
                command.Button.IsVisible = false;
            }

            return;
        }

        if (_moreMenu is null || available <= 0 || _reflowingToolbar)
        {
            return;
        }

        CacheToolbarWidth(_moreMenu);
        foreach (var button in _toolbarPrimary)
        {
            CacheToolbarWidth(button);
        }

        foreach (var command in _toolbarFlexible)
        {
            CacheToolbarWidth(command.Button);
        }

        // Widths are only known once a control has been arranged; until then, wait for the
        // next size change rather than split the row against zero-width measurements.
        if (!_toolbarWidths.ContainsKey(_moreMenu) ||
            _toolbarPrimary.Exists(button => !_toolbarWidths.ContainsKey(button)) ||
            _toolbarFlexible.Exists(command => !_toolbarWidths.ContainsKey(command.Button)))
        {
            return;
        }

        if (Math.Abs(available - _lastToolbarWidth) < 0.5)
        {
            return;
        }

        const double spacing = 7;
        double Slot(Control control) => _toolbarWidths[control] + spacing;

        var primaryTotal = _toolbarPrimary.Sum(Slot);
        var flexibleTotal = _toolbarFlexible.Sum(command => Slot(command.Button));
        var needMore = _toolbarSettings.Count > 0 || primaryTotal + flexibleTotal > available;
        var budget = available - primaryTotal - (needMore ? Slot(_moreMenu) : 0);

        _reflowingToolbar = true;
        try
        {
            var used = 0d;
            var overflowed = false;
            foreach (var command in _toolbarFlexible)
            {
                var slot = Slot(command.Button);
                // A landscape phone benefits from a stable three-stop command bar:
                // Open, Live, More. Secondary actions remain one tap away without turning
                // the short viewport into a desktop ribbon.
                var fits = !_mobileCompactHeight && !overflowed && used + slot <= budget;
                if (fits)
                {
                    used += slot;
                }
                else
                {
                    overflowed = true;
                }

                if (command.Button.IsVisible != fits)
                {
                    command.Button.IsVisible = fits;
                }
            }

            _moreItem.Items.Clear();
            var overflowCount = 0;
            foreach (var command in _toolbarFlexible)
            {
                if (!command.Button.IsVisible)
                {
                    _moreItem.Items.Add(command.MenuItem);
                    overflowCount++;
                }
            }

            if (overflowCount > 0 && _toolbarSettings.Count > 0)
            {
                _moreItem.Items.Add(new Separator());
            }

            foreach (var item in _toolbarSettings)
            {
                _moreItem.Items.Add(item);
            }

            var showMore = _moreItem.Items.Count > 0;
            if (_moreMenu.IsVisible != showMore)
            {
                _moreMenu.IsVisible = showMore;
            }

            _lastToolbarWidth = available;
        }
        finally
        {
            _reflowingToolbar = false;
        }
    }

    private void CacheToolbarWidth(Control control)
    {
        if (_toolbarWidths.ContainsKey(control))
        {
            return;
        }

        var width = control.Bounds.Width;
        if (width <= 0)
        {
            width = control.DesiredSize.Width;
        }

        if (width > 0)
        {
            _toolbarWidths[control] = width;
        }
    }

    private sealed class ToolbarCommand
    {
        public ToolbarCommand(Button button, MenuItem menuItem, Func<bool>? canExecute)
        {
            Button = button;
            MenuItem = menuItem;
            CanExecute = canExecute;
        }

        public Button Button { get; }
        public MenuItem MenuItem { get; }
        public Func<bool>? CanExecute { get; }
    }

    private static MenuItem MenuAction(string label, Func<Task> action)
    {
        var item = new MenuItem
        {
            Header = label,
            MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
        };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task OpenLogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Android logcat file",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Text logs") { Patterns = ["*.txt", "*.log"] }],
        });
        foreach (var file in files)
        {
            using (file)
            {
                var materialized = await StorageFileBridge.MaterializeForReadAsync(file);
                var path = materialized.Path;
                try
                {
                    await using var source = new FileLogSource(path);
                    var policy = TimestampPolicy.ForFile(source.Metadata.ReferenceInstant);
                    var preview = await ImportPreviewService.PreviewAsync(source, policy);
                    if (OperatingSystem.IsAndroid())
                    {
                        var androidSettings = new IngestSettings(
                            preview.Detection.PrimaryFormat,
                            "utf-8",
                            preview.TimestampPolicy,
                            new TemplateSettings(),
                            PortableRaw: materialized.IsTemporary);
                        await RunAsync(() => _viewModel.ImportFileAsync(
                            path,
                            androidSettings,
                            file.Name));
                        continue;
                    }

                    if (TopLevel.GetTopLevel(this) is not Window owner)
                    {
                        return;
                    }

                    var dialog = new ImportPreviewDialog(Path.GetFileName(path), preview);
                    var accepted = await dialog.ShowDialog<bool>(owner);
                    if (accepted && dialog.SelectedSettings is { } settings)
                    {
                        var effectiveSettings = materialized.IsTemporary
                            ? settings with { PortableRaw = true }
                            : settings;
                        await RunAsync(() => _viewModel.ImportFileAsync(path, effectiveSettings));
                    }
                }
                finally
                {
                    materialized.DeleteIfTemporary();
                }
            }
        }
    }

    private async Task OpenSessionAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open .vcat session", AllowMultiple = false });
        if (folders.Count == 0)
        {
            return;
        }

        using var folder = folders[0];
        if (folder.TryGetLocalPath() is { } path)
        {
            await RunAsync(() => _viewModel.OpenSessionAsync(path));
            return;
        }

        ShowNotice(
            OperatingSystem.IsAndroid()
                ? "Folder-backed sessions are app-private on Android. Use Recent sessions or open a portable .vcat.zip archive."
                : "The selected session folder is not exposed as a filesystem path.",
            NoticeKind.Failure);
    }

    private async Task OpenArchiveAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open portable VisualCat archive",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("VisualCat portable archives") { Patterns = ["*.vcat.zip", "*.zip"] },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        using var file = files[0];
        var materialized = await StorageFileBridge.MaterializeForReadAsync(file);
        try
        {
            await RunAsync(() => _viewModel.OpenPortableArchiveAsync(materialized.Path));
        }
        finally
        {
            materialized.DeleteIfTemporary();
        }
    }

    private async Task SaveSessionAsync(bool portable)
    {
        var tab = _viewModel.Selected;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (tab?.Snapshot is null || storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = portable ? "Choose portable session destination" : "Choose saved session destination",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } parent)
        {
            return;
        }

        var suffix = portable ? "-portable" : string.Empty;
        var destination = Path.Combine(
            parent,
            $"{Path.GetFileNameWithoutExtension(tab.Title)}{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.vcat");
        await RunAsync(async () =>
        {
            await tab.PersistViewAsync();
            await SessionSaveService.SaveAsync(tab.Snapshot, destination, portable);
            ShowNotice($"Saved: {destination}", NoticeKind.Completion);
        });
    }

    private async Task ExportAsync(TimeRange? selectedRange = null)
    {
        var tab = _viewModel.Selected;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (tab?.Snapshot is null || tab.Viewport is null || storage is null)
        {
            return;
        }

        var scope = await ResolveExportScopeAsync(tab, selectedRange);
        if (scope is null)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {scope.Label.ToLowerInvariant()}",

            // Without the extension. Android's DocumentsUI appends the one implied by the
            // MIME type on top of whatever the suggestion carries, so a name ending in ".csv"
            // was saved as "….csv.csv"; desktop pickers append DefaultExtension themselves
            // when the typed name has none (finding 8).
            SuggestedFileName = Path.GetFileNameWithoutExtension(tab.Title),
            DefaultExtension = "csv",
        });
        if (file is null)
        {
            return;
        }

        using (file)
        {
            var written = 0L;
            var name = file.Name;
            await RunAsync(() => StorageFileBridge.WriteAsync(
                file,
                async (path, cancellationToken) => written = await ExportService.ExportNormalizedCsvAsync(
                    tab.Snapshot,
                    path,
                    scope.Range,
                    tab.Filter,
                    _settings.ExportOrder == "Chronological"
                        ? VisualCat.Domain.Queries.EntryOrder.Chronological
                        : VisualCat.Domain.Queries.EntryOrder.SourceSequence,
                    _settings.ExportEncoding != "utf-8",
                    cancellationToken)));
            if (written > 0)
            {
                ShowNotice(
                    $"Exported {written:N0} rows ({scope.Label.ToLowerInvariant()}) to {name}",
                    NoticeKind.Completion);
            }
        }
    }

    /// <summary>
    /// Settles what an export covers: the range the reader picked from the plot, or the
    /// answer to the question the scope dialog asks.
    /// </summary>
    private async Task<ExportScope?> ResolveExportScopeAsync(SessionTabViewModel tab, TimeRange? selectedRange)
    {
        // "Export range" is already an explicit scope — the reader drew it on the plot — so
        // asking again would be asking a question they have just answered.
        if (selectedRange is { } chosen)
        {
            return new ExportScope(chosen, "The selected range", null);
        }

        var filterRange = tab.Filter.TimeRange;
        var sessionRange = filterRange ?? tab.Snapshot?.TimedRange;
        var viewport = filterRange ?? tab.Viewport ?? sessionRange;
        if (viewport is not { } viewportRange)
        {
            return null;
        }

        var inView = new ExportScope(viewportRange, "Entries in view", tab.MatchesInView);
        if (sessionRange is not { } whole ||
            whole.StartInclusive >= viewportRange.StartInclusive && whole.EndExclusive <= viewportRange.EndExclusive)
        {
            // The view already is the whole matching set; there is no second answer to offer.
            return inView with { Label = "Entries matching the filter", EstimatedRows = tab.Statistics?.TotalMatching };
        }

        return await ShowDialogAsync(new ExportScopeDialog(
            inView,
            new ExportScope(whole, "All entries matching the filter", tab.Statistics?.TotalMatching)));
    }

    /// <summary>
    /// Explains an on-device capture before Android asks the reader to allow it.
    /// </summary>
    /// <remarks>
    /// Tapping Live went straight to the system's "Allow VisualCat to access all device logs?"
    /// dialog, whose only affirmative is "Allow one-time access" — a serious-sounding question
    /// with no context, arriving before the app had said anything about what it wanted the log
    /// for or where the data goes. The app's own framing lands better before that dialog than
    /// after it (finding 27). It is shown once and remembered, because the system prompt
    /// reappears on every capture and this must not become a second thing to dismiss.
    /// </remarks>
    private async Task<bool> ConfirmLiveCaptureAsync()
    {
        if (!OperatingSystem.IsAndroid())
        {
            return true;
        }

        if (_settings.LiveCaptureNoticeAcknowledged)
        {
            // The full explanation is shown once and remembered, because Android's own prompt
            // arrives on every capture and this must not become a second thing to dismiss.
            // But the prompt really does arrive every time, and a reader who has met it once
            // and forgotten still deserves a second of warning before a system dialog covers
            // the screen (audit 2, C1). The lane is not a dialog: it says so and gets out of
            // the way.
            ShowNotice("Android will ask you to allow log access. It asks on every capture.");
            return true;
        }

        var confirmed = await ShowDialogAsync(new ConfirmationDialog(
            "About to capture this device's log",
            "VisualCat reads the Android log and stores it in this app's private storage. " +
            "Nothing is uploaded and there is no telemetry; a session leaves the device only " +
            "when you share or export it yourself.\n\n" +
            "Android will now ask you to allow access to device logs. It asks every time, " +
            "because the permission it grants is one-time.",
            "Continue"));
        if (confirmed != true)
        {
            return false;
        }

        _settings = _settings with { LiveCaptureNoticeAcknowledged = true };
        if (_settingsLoaded)
        {
            await RunAsync(() => SaveSettingsAsync());
        }

        return true;
    }

    private async Task StartOnDeviceAsync()
    {
        if (!await ConfirmLiveCaptureAsync())
        {
            return;
        }

        var source = PlatformSourceRegistry.CreateOnDeviceSource?.Invoke();
        if (source is null)
        {
            ShowNotice("On-device log access is unavailable.", NoticeKind.Failure);
            return;
        }

        // The one thing the platform will not tell the app up front is whether this capture
        // was actually allowed: declining Android's prompt does not fail, it silently
        // narrows the capture to VisualCat's own records while every permission check still
        // reports success. The source works that out from what arrives and says so here, as a
        // failure — because from the reader's point of view it is one (audit 2, C1).
        //
        // And takes it back if it turns out to have been wrong. A restricted scope is inferred
        // from an absence of foreign records, so a foreign record arriving later disproves it;
        // leaving a red notice reading "only VisualCat's own log lines are being captured"
        // pinned over a capture that is plainly recording the whole device was the most
        // visible half of audit 3's A1. Only this lane's own message is retracted, and only
        // while it is still the one showing.
        if (source is ISourceScopeReporter reporter)
        {
            var scopeNotice = 0L;
            reporter.ScopeResolved += report =>
            {
                if (!report.FullDevice && report.Summary is { Length: > 0 })
                {
                    ShowNotice(
                        "Only VisualCat's own log lines are being captured — " +
                        $"{report.Summary}. See the session details for how to widen it.",
                        NoticeKind.Failure);
                    scopeNotice = NoticeRevision;
                }
                else if (report.FullDevice && scopeNotice != 0)
                {
                    RetractNotice(scopeNotice);
                    scopeNotice = 0;
                    ShowNotice(
                        "Log access was allowed after all — this capture is seeing the whole device.",
                        NoticeKind.Information);
                }
            };
        }

        await RunAsync(async () =>
        {
            await using (source)
            {
                return await _viewModel.CaptureAsync(source, null);
            }
        });
    }

    private async Task SharePortableAsync()
    {
        var tab = _viewModel.Selected;
        var share = PlatformSourceRegistry.ShareFileAsync;
        if (tab?.Snapshot is null || share is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "VisualCat", "Share");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(tab.Title)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.vcat.zip");
            await tab.PersistViewAsync();
            await PortableSessionArchiveService.CreateAsync(tab.Snapshot, path);
            await share(path, CancellationToken.None);
            ShowNotice("Portable session handed to the platform share sheet.", NoticeKind.Completion);
        });
    }

    private void AddTab(SessionTabViewModel viewModel)
    {
        if (_tabItems.ContainsKey(viewModel))
        {
            return;
        }

        viewModel.SnapshotChanged += OnSessionSnapshotChanged;
        var workspace = CreateWorkspaceView(viewModel, _settings.WorkspaceDisplayMode);

        // The header is the session's name for automation only: the strip above draws the
        // chips, and this TabControl's own strip is out of the layout (see BuildSessionStrip).
        var item = new TabItem
        {
            Header = viewModel.Title,
            Content = workspace,
            Tag = viewModel,
        };
        _tabItems.Add(viewModel, item);
        _emptyState.IsVisible = false;
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        _viewModel.Selected = viewModel;
        AddSessionChip(viewModel);
        UpdateSessionActionAvailability();
        UpdateMobileChrome(Bounds.Size);
        PersistOpenWorkspace();
    }

    /// <summary>
    /// Builds one session's workspace and connects it to the shell.
    /// </summary>
    /// <remarks>
    /// Shared by the first build and by <see cref="RebuildWorkspaceViews"/>, so a replaced
    /// view arrives wired exactly as the original was rather than as whatever the second call
    /// site remembered to repeat.
    /// </remarks>
    private SessionWorkspaceView CreateWorkspaceView(SessionTabViewModel viewModel, string? displayMode)
    {
        var workspace = new SessionWorkspaceView(viewModel);
        workspace.ApplyDisplaySettings(
            _settings.IntensityScale,
            _settings.TimelineNormalization,
            _settings.TimelineMinimumUsPerPixel,
            _settings.TimelinePixelSnap,
            _settings.TimelineMinimumBarWidth);
        workspace.NoticeRaised += (message, failure) =>
            ShowNotice(message, failure ? NoticeKind.Failure : NoticeKind.Information);
        workspace.RestoreDisplayMode(displayMode);
        workspace.DisplayModeChanged += PersistWorkspaceDisplayMode;
        workspace.ExportRequested += range => _ = ExportAsync(range);
        workspace.StopRequested += () => _viewModel.StopAsync(viewModel);

        // A session that failed with no data offers the only two useful actions there are.
        workspace.CloseRequested += () => _ = _viewModel.CloseAsync(viewModel);
        workspace.OpenLogRequested += OpenLogAsync;
        return workspace;
    }

    private void OnSessionSnapshotChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            UpdateSessionActionAvailability();
            PersistOpenWorkspace();
        });

    private void RemoveTab(SessionTabViewModel viewModel)
    {
        if (!_tabItems.Remove(viewModel, out var item))
        {
            return;
        }

        viewModel.SnapshotChanged -= OnSessionSnapshotChanged;
        _tabs.Items.Remove(item);
        RemoveSessionChip(viewModel);
        _emptyState.IsVisible = _tabs.Items.Count == 0;
        if (_emptyState.IsVisible)
        {
            // The session just closed is the most likely one to be wanted back.
            _ = RefreshRecentSessionsAsync();
        }

        UpdateSessionActionAvailability();
        UpdateMobileChrome(Bounds.Size);
        PersistOpenWorkspace();
    }

    private async Task FollowFileAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Follow a growing logcat file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Text logs") { Patterns = ["*.txt", "*.log"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using var source = new GrowingFileLogSource(path);
            return await _viewModel.CaptureAsync(source, null);
        });
    }

    private async Task StartAdbAsync()
    {
        var executable = AdbLocator.Find(_settings.AdbPath);
        if (executable is null)
        {
            ShowNotice("ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.", NoticeKind.Failure);
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new AdbCaptureDialog(
            new ProcessAdbClient(executable),
            _settings.DefaultCaptureBuffers,
            _settings.DefaultCapturePreRollSeconds);
        var accepted = await dialog.ShowDialog<bool>(owner);
        if (!accepted || dialog.SelectedSource is not { } source)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await using (source)
            {
                return await _viewModel.CaptureAsync(source, dialog.CaptureDuration);
            }
        });
    }

    private async Task OpenStartupPathsAsync()
    {
        var paths = new List<string>(_startupPaths);
        if (PlatformSourceRegistry.ConsumeLaunchFilesAsync is { } consume)
        {
            paths.AddRange(await consume(CancellationToken.None));
        }

        await OpenPathsAsync(paths);
    }

    private async Task OpenPathsAsync(IEnumerable<string> values)
    {
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetFullPath(value);
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "manifest.json")))
            {
                await RunAsync(() => _viewModel.OpenSessionAsync(path));
            }
            else if (File.Exists(path) &&
                     (path.EndsWith(".vcat.zip", StringComparison.OrdinalIgnoreCase) ||
                      path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                await RunAsync(() => _viewModel.OpenPortableArchiveAsync(path));
            }
            else if (File.Exists(path))
            {
                await RunAsync(() => _viewModel.ImportFileAsync(path));
            }
            else
            {
                ShowNotice($"Startup source not found: {path}", NoticeKind.Failure);
            }
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            _settingsLoaded = true;
            if (_hostWindow is { } window)
            {
                ApplyWindowSettings(window);
            }

            WorkspaceViewModel.ConfigureTemporarySessionRoot(_settings.SessionDirectory);
            _viewModel.ConfigureUiRefreshLimit(_settings.UiRefreshLimit);
            await ConfigureDiagnosticsAsync();
            ApplyAppearance();
            if (_settings.TemporaryCleanupEnabled)
            {
                var result = await TemporarySessionRetentionService.CleanupAsync(
                    WorkspaceViewModel.TemporarySessionRoot,
                    enabled: true,
                    TimeSpan.FromDays(_settings.TemporaryRetentionDays),
                    _settings.TemporaryRetentionMaximumBytes,
                    DateTimeOffset.UtcNow);
                if (result.Errors.Count > 0)
                {
                    ShowNotice($"Temporary cleanup left {result.Errors.Count:N0} session(s) in place.", NoticeKind.Failure);
                }
            }

            // Restore first, then anything the launch intent carried: a file the reader has
            // just tapped in another app belongs in front of the workspace they left behind.
            await RestoreWorkspaceAsync();
            await OpenStartupPathsAsync();
        }
        catch (Exception exception)
        {
            ShowNotice($"Startup settings: {exception.GetBaseException().Message}", NoticeKind.Failure);
        }
    }

    /// <summary>
    /// The sessions this app is recording into at this moment.
    /// </summary>
    /// <remarks>
    /// A stored session that is not finalized may be a capture that was interrupted months ago
    /// or one that is running right now, and on disk the two are identical — the only thing
    /// that can tell them apart is the app that is doing the recording (audit 3, E1). Path
    /// comparison is case-insensitive because these are Android and Windows file paths, and
    /// the same session can be named either way by the two sides of this comparison.
    /// </remarks>
    private HashSet<string> CapturingSessionPaths()
    {
        var capturing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in _viewModel.Tabs)
        {
            if (tab.IsLiveCaptureActive)
            {
                capturing.Add(Path.GetFullPath(tab.SessionPath));
            }
        }

        return capturing;
    }

    private async Task OpenRecentAsync()
    {
        var sessions = await TemporarySessionRetentionService.ScanAsync(WorkspaceViewModel.TemporarySessionRoot);
        var path = await ShowDialogAsync(new RecentSessionsDialog(sessions, CapturingSessionPaths()));
        if (path is not null)
        {
            await RunAsync(() => _viewModel.OpenSessionAsync(path));
        }
    }

    private async Task ShowAppearanceAsync()
    {
        var updated = await ShowDialogAsync(new AppearanceDialog(_settings));
        if (updated is null)
        {
            return;
        }

        try
        {
            WorkspaceViewModel.ConfigureTemporarySessionRoot(updated.SessionDirectory);
            _settings = updated;
            _viewModel.ConfigureUiRefreshLimit(_settings.UiRefreshLimit);
            await ConfigureDiagnosticsAsync();
            ApplyAppearance();
            await SaveSettingsAsync();
        }
        catch (Exception exception)
        {
            ShowNotice(exception.GetBaseException().Message, NoticeKind.Failure);
        }
    }

    private async Task ShowSessionCacheAsync()
    {
        var updated = await ShowDialogAsync(new SessionCacheDialog(
            WorkspaceViewModel.TemporarySessionRoot,
            _settings,
            CapturingSessionPaths()));
        if (updated is null)
        {
            return;
        }

        _settings = updated;
        await RunAsync(() => SaveSettingsAsync());
    }

    private async Task CreateDiagnosticBundleAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var confirmed = await ShowDialogAsync(new ConfirmationDialog(
            "Create diagnostic bundle",
            "The bundle excludes raw log messages, source paths, hashes, searches, and device serials. It still contains timings, counts, system details, and sanitized session metadata. Review it before sharing.",
            "Create bundle"));
        if (confirmed != true)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save VisualCat diagnostic bundle",

            // Extension in DefaultExtension only: see the note in ExportAsync (finding 8).
            SuggestedFileName = $"visualcat-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}",
            DefaultExtension = "zip",
        });
        if (file is null)
        {
            return;
        }

        using (file)
        {
            await RunAsync(() => StorageFileBridge.WriteAsync(
                file,
                async (path, cancellationToken) =>
                {
                    await DiagnosticBundleService.CreateAsync(
                        DiagnosticsDirectory,
                        path,
                        _viewModel.Tabs.Select(static tab => tab.SessionPath),
                        cancellationToken);
                    ShowNotice($"Diagnostic bundle saved: {file.Name}", NoticeKind.Completion);
                }));
        }
    }

    private async Task ConfigureDiagnosticsAsync()
    {
        _viewModel.ConfigureDiagnostics(null);
        if (_diagnostics is not null)
        {
            await _diagnostics.DisposeAsync();
            _diagnostics = null;
        }

        if (_settings.DiagnosticsEnabled)
        {
            _diagnostics = new RollingDiagnosticLogger(DiagnosticsDirectory);
            _viewModel.ConfigureDiagnostics(_diagnostics);
        }
    }

    private void ApplyAppearance()
    {
        App.HighContrastEnabled = _settings.HighContrast;
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = _settings.Theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }

        // The device's setting is the baseline and the product's own multiplies it, so a
        // reader who has only ever used Android's text-size control still gets larger text
        // (audit 2, B5). Views read the result while they are built; Android recreates the
        // activity when the scale changes, and the persisted workspace is what keeps the
        // reader's place across that (see C5).
        TextScale.Platform = PlatformSourceRegistry.PlatformFontScale ?? 1;
        TextScale.User = _settings.TextScale;
        FontSize = TextScale.Of(14);
        ApplyTextScaleToShell();
        foreach (var item in _tabItems.Values)
        {
            if (item.Content is SessionWorkspaceView workspace)
            {
                workspace.ApplyDisplaySettings(
                    _settings.IntensityScale,
                    _settings.TimelineNormalization,
                    _settings.TimelineMinimumUsPerPixel,
                    _settings.TimelinePixelSnap,
                    _settings.TimelineMinimumBarWidth);
            }
        }
    }

    /// <summary>
    /// Re-sizes the few shell labels that were built before the stored settings arrived.
    /// </summary>
    /// <remarks>
    /// Settings are read from disk, so the command bar exists before the reader's own text
    /// scale is known. Everything in the bar that does not state a size of its own already
    /// follows this view's <c>FontSize</c>; these four state one,
    /// and they are the shell's own identity rather than session content, so they are
    /// re-stated here rather than by rebuilding the bar under the reader.
    /// </remarks>
    private void ApplyTextScaleToShell()
    {
        if (_brandWordmark is { } wordmark)
        {
            wordmark.FontSize = TextScale.Of(15);
        }

        if (_brandStrapline is { } strapline)
        {
            strapline.FontSize = TextScale.Of(8);
        }

        if (_noticeText is { } notice)
        {
            notice.FontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 12.5 : 12);
        }

        foreach (var chip in _chips.Values)
        {
            chip.Title.FontSize = TextScale.Of(OperatingSystem.IsAndroid() ? 12.5 : 12);
        }

        // The empty state states several sizes and is cheap to rebuild, and it is the first
        // thing a cold start shows.
        _emptyState.Child = BuildEmptyState(ActualThemeVariant != ThemeVariant.Light);
    }

    private void ApplyWindowSettings(Window window)
    {
        if (_settings.WindowWidth is { } width)
        {
            window.Width = Math.Max(window.MinWidth, width);
        }

        if (_settings.WindowHeight is { } height)
        {
            window.Height = Math.Max(window.MinHeight, height);
        }

        if (_settings.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _ = sender;
        var control = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control && eventArgs.Key == Key.O)
        {
            _ = shift ? OpenSessionAsync() : OpenLogAsync();
            eventArgs.Handled = true;
            return;
        }

        if (ActiveWorkspace is { } workspace && workspace.TryHandleShortcut(eventArgs))
        {
            eventArgs.Handled = true;

            // Android turns one Back press into a Key.Escape key-down and a back-request, in
            // that order. This is the key-down; the back-request is about to arrive and must
            // not treat the drawer this just closed as a second thing to answer (audit 3, C1).
            if (eventArgs.Key == Key.Escape)
            {
                NoteDismissedByEscape();
            }

            return;
        }

        if (eventArgs.Key == Key.E && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = ExportAsync();
            eventArgs.Handled = true;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            ShowNotice(string.Empty);
            await action();
        }
        catch (OperationCanceledException)
        {
            ReportCancelled();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private async Task RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            ShowNotice(string.Empty);
            _ = await action();
        }
        catch (OperationCanceledException)
        {
            ReportCancelled();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    /// <summary>
    /// Cancellation is an outcome the user asked for — dismissing a picker, closing a
    /// dialog, superseding a query — not a fault. Rendering its exception text in the
    /// error banner accused the application of failing every time someone changed their
    /// mind, so it clears the banner instead.
    /// </summary>
    private void ReportCancelled() => ShowNotice(string.Empty);

    /// <summary>
    /// A real failure is labelled as one. A bare framework message ("The operation was
    /// canceled.", "Access to the path is denied.") does not say which action failed, and
    /// §18.1 asks a user message to state what happened and what remains usable.
    /// </summary>
    private void ReportFailure(Exception exception) =>
        ShowNotice($"Could not complete that action · {exception.GetBaseException().Message}", NoticeKind.Failure);

    public async ValueTask DisposeAsync()
    {
        PlatformSourceRegistry.LaunchFilesReceived -= _launchFilesHandler;
        PlatformSourceRegistry.AppResumed -= _appResumedHandler;
        PlatformSourceRegistry.AppPaused -= _appPausedHandler;
        PlatformSourceRegistry.DisplayConfigurationChanged -= _displayConfigurationHandler;
        StopNoticeTimer();
        StopObservingSafeArea();

        // No settings writer may still be waiting on the semaphore when it is disposed. The
        // newest workspace task subsumes every older queued workspace snapshot; direct settings
        // writes are awaited at their call sites.
        Interlocked.Increment(ref _workspacePersistVersion);
        try
        {
            await _lastWorkspacePersist;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            global::System.Diagnostics.Debug.WriteLine($"VisualCat final workspace persistence failed: {exception}");
        }

        await _viewModel.DisposeAsync();
        if (_diagnostics is not null)
        {
            await _diagnostics.DisposeAsync();
            _diagnostics = null;
        }

        _settingsSaveGate.Dispose();
    }
}
