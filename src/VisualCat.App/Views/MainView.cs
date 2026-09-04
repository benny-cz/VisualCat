using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

    /// <summary>The workspace this view presents. Exposed for tests.</summary>
    internal WorkspaceViewModel Workspace => _viewModel;

    private readonly TabControl _tabs = new();
    private readonly TextBlock _message = new();
    private readonly Border _emptyState = new();
    private readonly ModalWorkspaceBand _rootPanel = new();
    private readonly Dictionary<SessionTabViewModel, TabItem> _tabItems = [];
    private readonly string[] _startupPaths;
    private readonly SettingsStore _settingsStore;
    private ApplicationSettings _settings = new();
    private string? _reportedInvalidAdbPath;
    private RollingDiagnosticLogger? _diagnostics;
    private bool _startupOpened;
    private bool _settingsLoaded;
    private Window? _hostWindow;
    private readonly Action<IReadOnlyList<IncomingFile>> _launchFilesHandler;
    private readonly Action _appResumedHandler;
    private readonly Action _appPausedHandler;
    private readonly Action _displayConfigurationHandler;
    private readonly Action<AppUpdateStatus> _appUpdateStatusHandler;

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
    private Button? _liveButton;
    private readonly List<ToolbarCommand> _toolbarFlexible = [];
    private readonly List<MenuItem> _toolbarSettings = [];
    private readonly MenuItem _moreItem = new()
    {
        Header = "More  ▾",
        MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
    };
    private readonly Grid _commandContent = new()
    {
        RowDefinitions = new RowDefinitions("Auto,Auto"),
        RowSpacing = OperatingSystem.IsAndroid() ? 8 : 4,
    };
    private readonly Grid _compactWorkspaceCommands = new() { IsVisible = false };
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

    /// <summary>
    /// The viewport width the chrome was last composed for, so the shared-row decision can
    /// read the constraint that actually binds it rather than only the one that selected it.
    /// </summary>
    private double _mobileViewportWidth = double.PositiveInfinity;
    private StackPanel? _recentList;
    private TextBlock? _recentHeading;
    private Control? _recentSection;
    private Button? _recentShowAll;
    private readonly object _recentRefreshGate = new();
    private readonly CancellationTokenSource _recentRefreshLifetime = new();
    private Task _recentRefreshTask = Task.CompletedTask;
    private bool _recentRefreshRunning;
    private bool _recentRefreshRequested;
    private int _recentRefreshDisposed;

    private static string DiagnosticsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualCat",
        "Diagnostics");

    /// <summary>One view's registration on the platform's static event surface.</summary>
    private sealed class PlatformEventSubscription(
        Action<IReadOnlyList<IncomingFile>> launchFiles,
        Action appResumed,
        Action appPaused,
        Action displayConfiguration,
        Action<AppUpdateStatus> appUpdateStatus,
        Action superseded)
    {
        public void Attach()
        {
            PlatformSourceRegistry.LaunchFilesReceived += launchFiles;
            PlatformSourceRegistry.AppResumed += appResumed;
            PlatformSourceRegistry.AppPaused += appPaused;
            PlatformSourceRegistry.DisplayConfigurationChanged += displayConfiguration;
            PlatformSourceRegistry.AppUpdateStatusChanged += appUpdateStatus;
        }

        public void Detach()
        {
            PlatformSourceRegistry.LaunchFilesReceived -= launchFiles;
            PlatformSourceRegistry.AppResumed -= appResumed;
            PlatformSourceRegistry.AppPaused -= appPaused;
            PlatformSourceRegistry.DisplayConfigurationChanged -= displayConfiguration;
            PlatformSourceRegistry.AppUpdateStatusChanged -= appUpdateStatus;
        }

        /// <summary>Another view has taken this one's place and it will never be seen again.</summary>
        public void Supersede()
        {
            Detach();
            superseded();
        }
    }

    /// <summary>The one view currently answering the platform, or null before the first.</summary>
    /// <remarks>
    /// <para>
    /// Android builds a replacement <see cref="MainView"/> whenever it recreates the
    /// activity — for a configuration change this app does not declare, for "Don't keep
    /// activities", after a low-memory activity kill — and, unlike the desktop host, it has
    /// no window-closed moment at which to dispose the view it replaces. Those platform
    /// events are static, so the replaced view stayed subscribed to them for the life of the
    /// process. On a device that is not a theoretical leak: with two views attached, both
    /// answered <c>AppResumed</c> by resuming live views and re-running queries for a
    /// workspace nobody could see, and both answered <c>AppPaused</c> by writing their own
    /// open-workspace list to the same settings file — so the abandoned view's stale tab set
    /// could be the one that survived and got restored.
    /// </para>
    /// <para>
    /// Handing the subscriptions to the newest view makes "one view answers the platform"
    /// true by construction, on every host, without depending on a disposal hook that one of
    /// the two platforms does not have. It also stops rooting the replaced view from a static
    /// field, so the workspace it holds — and the segment mappings under it — become
    /// collectable once its own captures finish.
    /// </para>
    /// </remarks>
    private static PlatformEventSubscription? s_platformEvents;

    private readonly PlatformEventSubscription _platformEvents;

    private static void AttachToPlatform(PlatformEventSubscription subscription)
    {
        Interlocked.Exchange(ref s_platformEvents, subscription)?.Supersede();
        subscription.Attach();
    }

    /// <summary>Where the settings file lives when nobody says otherwise.</summary>
    internal static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VisualCat",
        "settings.json");

    public MainView(IEnumerable<string>? startupPaths = null)
        : this(startupPaths, DefaultSettingsPath)
    {
    }

    /// <summary>
    /// Builds a view whose settings live somewhere other than this machine's own.
    /// </summary>
    /// <remarks>
    /// For tests, and worth the seam. A headless view runs the whole of startup, including the
    /// settings load and every write that follows it, so a test that dismisses an update offer
    /// wrote a real snooze into the developer's own settings file — which then suppressed the
    /// offer in later runs and made unrelated tests fail on a machine where the suite had been
    /// run before. A test that changes the machine it runs on is not a test.
    /// </remarks>
    internal MainView(IEnumerable<string>? startupPaths, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _startupPaths = startupPaths?.ToArray() ?? [];
        _settingsStore = new SettingsStore(settingsPath);
        _launchFilesHandler = files => Dispatcher.UIThread.Post(() => _ = RunAsync(() => OpenIncomingAsync(files)));
        _appResumedHandler = () => Dispatcher.UIThread.Post(() =>
        {
            RestoreAndroidLayoutAfterResume();

            // Ordered after the layout restore so the first refreshed frame lands in the
            // layout the user left, not the one being rebuilt underneath it.
            _viewModel.ResumeLiveViews();

            // Last, and only if the channel's throttle allows it: a store answer is the least
            // urgent thing about coming back to the app, and it must not delay the frame.
            _ = CheckForUpdateAsync(coldStart: false);
        });

        // Suspension is not posted to the dispatcher: OnPause is the last moment the app
        // reliably gets before the process is frozen, and a queued message may not run.
        _appPausedHandler = () =>
        {
            _viewModel.SuspendLiveViews();
            PersistOpenWorkspaceOnPause();
        };
        _displayConfigurationHandler = () =>
        {
            // Android reports configuration changes on the UI thread. Apply those changes
            // in that same turn so an open sheet and workspace cannot render one stale frame;
            // retain the post for hosts that publish from another thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyDisplayConfigurationChange();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyDisplayConfigurationChange);
            }
        };
        _appUpdateStatusHandler = status => Dispatcher.UIThread.Post(() => RenderUpdateStatus(status));
        _platformEvents = new PlatformEventSubscription(
            _launchFilesHandler,
            _appResumedHandler,
            _appPausedHandler,
            _displayConfigurationHandler,
            _appUpdateStatusHandler,

            // A superseded view is off screen for good, and its workspace does not know
            // that: left watching, it goes on answering every progress report by reopening
            // the session — new segment mappings and a full query set — to redraw a frame
            // that has no surface to land on. Suspension is the same state the platform
            // asks for when the app is backgrounded, and this one is never lifted, because
            // nothing will ever resume this view.
            () =>
            {
                _viewModel.SuspendLiveViews();
                CancelUpdateWork();
            });
        AttachToPlatform(_platformEvents);
        Content = Build();
        SizeChanged += (_, eventArgs) =>
        {
            UpdateMobileChrome(eventArgs.NewSize);

            // A sheet's height cap was decided by the bounds it opened in, so rotating with
            // one open left it capped for the orientation it is no longer in (F-40).
            RefreshOverlays();
        };
        ActualThemeVariantChanged += (_, _) => ApplyThemeSurfaces();
        ApplyThemeSurfaces();
        _viewModel.TabAdded += (_, tab) => Dispatcher.UIThread.Post(() => AddTab(tab));
        _viewModel.TabRemoved += (_, tab) => Dispatcher.UIThread.Post(() => RemoveTab(tab));

        // A recording that stops without being asked to is the one capture outcome the reader
        // cannot see for themselves, so it goes in the lane that stays until dismissed.
        _viewModel.CaptureEndedUnprompted += (_, message) =>
            Dispatcher.UIThread.Post(() => ShowNotice(message, NoticeKind.Failure));
        _viewModel.LiveCaptureChanged += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                UpdateLiveCaptureIndicator();

                // A recording ending is the moment a deferred install becomes possible, and a
                // recording starting is the moment an offer has to get out of the way.
                ReconsiderUpdateNotice();
            });
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem is TabItem { Tag: SessionTabViewModel tab })
            {
                _viewModel.Selected = tab;
            }

            UpdateSessionStrip();
            UpdateCompactCommandComposition();
            UpdateSessionActionAvailability();
            PersistOpenWorkspace();
        };
        AttachedToVisualTree += (_, _) =>
        {
            // The gesture is raised on the top level, so the handler belongs there; a
            // descendant never sees it.
            TopLevel.GetTopLevel(this)?.AddHandler(TopLevel.BackRequestedEvent, OnBackRequested);

            // Escape belongs to the shell while a layer is open, and it has to be claimed
            // where the route actually starts. See OnShellKeyDown.
            TopLevel.GetTopLevel(this)?.AddHandler(
                KeyDownEvent,
                OnShellKeyDown,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);

            // The host's own back contract, where there is one. Avalonia raises the event
            // above from the callback it registers itself; a stock gesture-navigation Pixel
            // reached the launcher past an open sheet anyway (V2-21), so the shell installs
            // its layer stack where the platform asks about it directly rather than trusting
            // one route to be the only one.
            Platform.PlatformSourceRegistry.TryNavigateBack = TryNavigateBack;
            ObserveSafeArea();
            ObserveOverlayInputPane();

            // ActualThemeVariant is only meaningful once there is a top level to inherit it
            // from: everything built in the constructor resolved it as Default, which the
            // "is it Light?" test reads as dark, so a cold start on a phone set to light was
            // served the dark palette and no variant change ever arrived to correct it
            // (audit 2, A1c). Re-running once the tree has settled is what makes a cold
            // start in either variant produce the same result as a switch into it.
            Dispatcher.UIThread.Post(ApplyThemeSurfaces, DispatcherPriority.Loaded);

            // After the theme so a restored update banner is painted in the variant the
            // reader is actually in, and before startup so a rebuilt view shows an offer
            // that is already in flight rather than waiting for the next store answer.
            Dispatcher.UIThread.Post(RestoreCachedUpdateNotice, DispatcherPriority.Loaded);
            if (!_startupOpened)
            {
                _startupOpened = true;

                // The update check is chained after startup rather than run inside it: a
                // workspace restore and a tapped file outrank an update offer for the first
                // screen the reader sees, and InitializeAsync's catch turns anything thrown
                // inside it into "part of the startup did not finish", which a store that
                // cannot answer must never be able to produce.
                _ = InitializeAsync().ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => _ = CheckForUpdateAsync(coldStart: true)),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            TopLevel.GetTopLevel(this)?.RemoveHandler(TopLevel.BackRequestedEvent, OnBackRequested);
            TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnShellKeyDown);
            if (Platform.PlatformSourceRegistry.TryNavigateBack == TryNavigateBack)
            {
                Platform.PlatformSourceRegistry.TryNavigateBack = null;
            }

            StopObservingSafeArea();
            StopObservingOverlayInputPane();
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
        // ApplyAppearance owns the "did the effective scale move?" decision, so the platform's
        // route and the reader's own Text scale control cannot answer it differently.
        ApplyAppearance();
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

        // A sheet is a surface painted in code like any other, and it is the one the reader is
        // looking at while this runs (F-40).
        RefreshOverlays();
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

        // The flexible column, not the trailing Auto one. An Auto column gives the message its
        // full desired width, so CharacterEllipsis never engaged and a long message simply ran
        // off the edge of the screen — the first sentence of the update offer painted straight
        // through the wordmark and out of the window on a 1080 px phone. Stretched inside the
        // star column with the text right-aligned, it keeps the trailing position it had and
        // gains the width limit the trimming needs to mean anything.
        _message.HorizontalAlignment = HorizontalAlignment.Stretch;
        _message.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(_message, 1);
        brandRow.Children.Add(_message);
        commandContent.Children.Add(brandRow);

        var toolbar = BuildActionToolbar();
        Grid.SetRow(toolbar, 1);
        commandContent.Children.Add(toolbar);
        Grid.SetRow(_compactWorkspaceCommands, 1);
        commandContent.Children.Add(_compactWorkspaceCommands);

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
        _mobileViewportWidth = size.Width;
        var sessionOpen = _tabs.Items.Count > 0;
        var compositionChanged = _mobileCompactHeight != compactHeight;
        _mobileCompactHeight = compactHeight;
        ApplyNoticeLayout(compactHeight);
        // Once a session is open its tab title is the identity that matters. Removing the
        // decorative brand row recovers a full touch row in portrait without hiding any
        // command; the empty/home state still carries the complete VisualCat masthead.
        _brandRow.IsVisible = !compactHeight && !sessionOpen;
        _commandContent.RowSpacing = compactHeight || sessionOpen ? 0 : 8;
        _commandBar.Padding = CommandBarPadding(compact: compactHeight || sessionOpen);
        // Session tabs remain a compact top row. A side rail looks efficient on paper, but
        // wastes a large column when a phone has the common one-session workspace.
        _tabs.TabStripPlacement = Dock.Top;
        UpdateSessionStrip();
        UpdateCompactCommandComposition();

        if (compositionChanged)
        {
            _lastToolbarWidth = -1;
            Dispatcher.UIThread.Post(() => ReflowToolbar(_toolbar.Bounds.Width));
        }
    }

    /// <summary>
    /// Uses landscape width instead of spending a second 48 dp band on workspace commands.
    /// The selected workspace's real strip is reparented beside Open/Live/More, so labels,
    /// enabled states and screen-reader metadata have one owner. While the query has focus the
    /// whole shared row yields to the drawer; Reset and Done then remain above even an IME that
    /// overlays rather than resizes the activity (F-09/F-10).
    /// </summary>
    /// <remarks>
    /// "Landscape width" was the assumption and height alone was the test, so a short
    /// <em>portrait</em> viewport — split-screen, a short window, or a tall notice on a
    /// smaller phone — reached it too. The toolbar takes <c>Auto</c>; at 434 dp that left the
    /// strip 166 dp for about 330 dp of controls, and the strip does not clip, it overlaps:
    /// the row read <c>Plot · s · Spl · Fit · ils</c>, and <c>Filters</c> — painted first, so
    /// painted under — had no reachable touch point at all. A tap inside its own reported
    /// bounds went to <c>Split</c> instead (finding F-34). Below the threshold the strip goes
    /// back to the workspace and takes a band of its own, which is what every tall portrait
    /// phone already does; that band is the honest price of a viewport too narrow to share.
    /// </remarks>
    private void UpdateCompactCommandComposition()
    {
        if (!OperatingSystem.IsAndroid() || _commandBar is null)
        {
            return;
        }

        var active = _tabs.SelectedItem is TabItem { Content: SessionWorkspaceView workspace }
            ? workspace
            : null;
        var combine = _mobileCompactHeight
                      && active is not null
                      && MobileWorkspaceLayout.SharesARow(_mobileViewportWidth);

        foreach (var item in _tabItems.Values)
        {
            if (item.Content is not SessionWorkspaceView candidate)
            {
                continue;
            }

            candidate.HostCompactCommands(combine && ReferenceEquals(candidate, active)
                ? _compactWorkspaceCommands
                : null);
        }

        _compactWorkspaceCommands.IsVisible = combine;
        _commandBar.IsVisible = !(combine && active?.CompactEditorActive == true);

        if (combine)
        {
            _commandContent.RowDefinitions = new RowDefinitions("Auto");
            _commandContent.ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _commandContent.ColumnSpacing = 8;
            Grid.SetRow(_toolbar, 0);
            Grid.SetColumn(_toolbar, 0);
            Grid.SetRow(_compactWorkspaceCommands, 0);
            Grid.SetColumn(_compactWorkspaceCommands, 1);
        }
        else
        {
            _commandContent.RowDefinitions = new RowDefinitions("Auto,Auto");
            _commandContent.ColumnDefinitions = new ColumnDefinitions("*");
            _commandContent.ColumnSpacing = 0;
            Grid.SetRow(_toolbar, 1);
            Grid.SetColumn(_toolbar, 0);
            Grid.SetRow(_compactWorkspaceCommands, 1);
            Grid.SetColumn(_compactWorkspaceCommands, 0);
        }

        _commandContent.InvalidateMeasure();
        _commandBar.InvalidateMeasure();
    }

    /// <summary>
    /// The hero, centred when it fits and scrollable when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two defects, one cause. The hero block ended 45 % of the way down a 849 dp workspace
    /// and left 559 dp of empty ground under it — which on a phone does not read as airy, it
    /// reads as a screen that failed to finish drawing, with both primary calls to action
    /// above the part of the screen a thumb reaches most easily (V2-02). And a light-grey
    /// scrollbar thumb sat permanently at the right edge, half-clipped by the panel, on a
    /// screen that could not scroll by one pixel: 340 px of thumb in a 1 911 px track, which
    /// claims about 5.6 screens of content that does not exist (V2-01).
    /// </para>
    /// <para>
    /// <c>ScrollViewer.VerticalContentAlignment</c> does not centre on the scroll axis — the
    /// presenter measures its child against infinity and arranges it from the top — so the
    /// alignment that was already asked for could never take effect. A host that is told to be
    /// at least as tall as the viewport is the idiom that works: the content centres inside it
    /// while it fits, the host grows past the viewport when the reader's text size needs it,
    /// and the extent is then honest in both states.
    /// </para>
    /// <para>
    /// The scrollbar itself is not the affordance a touch platform uses. It is hidden here and
    /// replaced by the fade every other scrolling surface in the product already uses, which
    /// also means nothing can be drawn inside the 48 dp corner radius again.
    /// </para>
    /// </remarks>
    private FadingScrollHost BuildEmptyState(bool dark)
    {
        var mobile = OperatingSystem.IsAndroid();
        var levelLegend = BuildSeverityLegend(dark, mobile);

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
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
                    // Pixel 5 at 130% Android text needs two lines. Keeping the headline
                    // unwrapped preserved its semantic text in accessibility but clipped the
                    // visible ending after "YOUR L" on the phone.
                    TextWrapping = TextWrapping.Wrap,
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
                BuildHeroActions(dark, mobile),
                BuildRecentSection(dark),
                new TextBlock
                {
                    Text = $"VisualCat {ProductInfo.BuildVersion} · local-first · no telemetry",
                    FontSize = TextScale.Of(10),
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                    Opacity = 0.8,
                },
            },
        };

        content.VerticalAlignment = VerticalAlignment.Center;

        // Centering an oversized StackPanel directly in the host clips equal portions above
        // and below the viewport; on Pixel 5 at 130% that put the provenance behind the
        // gesture bar (F-46). The host is what keeps both behaviours: never shorter than the
        // viewport, so the hero centres; free to grow past it, so nothing is clipped.
        var centering = new Panel { Children = { content } };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = mobile
                ? ScrollBarVisibility.Hidden
                : ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = centering,
        };

        // Written from the arranged viewport rather than bound, and guarded, because this runs
        // from a layout pass: an unguarded write would invalidate the layout it was just told
        // about and never settle.
        scroller.LayoutUpdated += (_, _) =>
        {
            var target = scroller.Viewport.Height;
            if (target > 0 && Math.Abs(centering.MinHeight - target) > 0.5)
            {
                centering.MinHeight = target;
            }
        };

        // The empty state sits on the shell's own ground, not on a card, and the whole
        // block is rebuilt on a theme change, so the fade never has to be repainted in place.
        return new FadingScrollHost(scroller, dark, horizontal: false, raised: false);
    }

    /// <summary>
    /// Keeps the six-level legend balanced on phone-width layouts. A free-form wrap can fit
    /// six chips at 440 dpi but only five at 480 dpi, leaving a single orphaned chip on common
    /// Samsung displays. Two intentional rows of three are stable across phone densities,
    /// text scaling, portrait, and landscape; desktop keeps the compact single-row wrap.
    /// </summary>
    internal static Panel BuildSeverityLegend(bool dark, bool mobile)
    {
        Panel levelLegend = mobile
            ? new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnSpacing = 8,
                RowSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            }
            : new WrapPanel
            {
                ItemSpacing = 8,
                LineSpacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

        var legendIndex = 0;
        foreach (var level in VisualCat.Domain.Entries.LogLevels.DisplayOrder)
        {
            if (level == VisualCat.Domain.Entries.LogLevel.Unknown)
            {
                continue;
            }

            var parsed = VisualCat.App.Timeline.LevelPalette.ColorOf(level);
            var chip = new Border
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
            };
            levelLegend.Children.Add(chip);
            if (levelLegend is Grid mobileLegend)
            {
                Grid.SetColumn(chip, legendIndex % 3);
                Grid.SetRow(chip, legendIndex / 3);
            }

            legendIndex++;
        }

        return levelLegend;
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
        RequestRecentSessionsRefresh();
        return section;
    }

    /// <summary>
    /// Fills the recent-captures list. Failures are silent by design: this is a convenience on
    /// a screen whose other routes all still work, and a cache that cannot be scanned is not
    /// worth an error banner on the first screen of the app.
    /// </summary>
    private void RequestRecentSessionsRefresh()
    {
        lock (_recentRefreshGate)
        {
            if (_recentRefreshLifetime.IsCancellationRequested)
            {
                return;
            }

            // A close burst used to start one complete cache walk per tab. Fifty short
            // captures could therefore leave fifty growing directory scans, UI rebuilds and
            // continuations in flight at once. One running scan now absorbs every request
            // into at most one follow-up pass, so the latest state wins without creating an
            // unbounded background queue (Windows live-test finding F-13 / X-21).
            _recentRefreshRequested = true;
            if (_recentRefreshRunning)
            {
                return;
            }

            _recentRefreshRunning = true;
            _recentRefreshTask = DrainRecentSessionRefreshesAsync(_recentRefreshLifetime.Token);
        }
    }

    private async Task DrainRecentSessionRefreshesAsync(CancellationToken cancellationToken)
    {
        // Do not run the first gate-taking part inline while RequestRecentSessionsRefresh
        // still owns the gate. Keeping the captured UI context also ensures that the control
        // tree is only rebuilt on the dispatcher thread.
        await Task.Yield();

        try
        {
            while (true)
            {
                lock (_recentRefreshGate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _recentRefreshRequested = false;
                }

                await RefreshRecentSessionsCoreAsync(cancellationToken);

                lock (_recentRefreshGate)
                {
                    if (!_recentRefreshRequested)
                    {
                        _recentRefreshRunning = false;
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_recentRefreshGate)
            {
                _recentRefreshRunning = false;
                _recentRefreshRequested = false;
            }
        }
    }

    /// <summary>Waits until every recent-session refresh requested so far has settled.</summary>
    internal async Task WaitForRecentSessionsRefreshAsync()
    {
        while (true)
        {
            Task pending;
            lock (_recentRefreshGate)
            {
                pending = _recentRefreshTask;
            }

            await pending;

            lock (_recentRefreshGate)
            {
                if (!_recentRefreshRunning && ReferenceEquals(pending, _recentRefreshTask))
                {
                    return;
                }
            }
        }
    }

    private async Task RefreshRecentSessionsCoreAsync(CancellationToken cancellationToken)
    {
        if (_recentList is not { } list || _recentSection is not { } section)
        {
            return;
        }

        IReadOnlyList<TemporarySessionInfo> sessions;
        try
        {
            sessions = await TemporarySessionRetentionService.ScanAsync(
                WorkspaceViewModel.TemporarySessionRoot,
                cancellationToken);
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

        // Not the path. Avalonia surfaces a tooltip as the Android node's content description,
        // so every cold-start card was handing a screen reader
        // "/data/user/0/com.barebit.visualcat/files/VisualCat/Sessions/…-<32 hex>.vcat" —
        // a private storage location and a session GUID, both of which R-25 forbids, and
        // neither of which is needed to choose a card: the in-page Recent sessions dialog
        // lists the same three captures without them (finding F-17). The path stays in the
        // click handler, which is the only thing that needs it, and in Session info, which is
        // where someone who wants it goes.
        ToolTip.SetTip(button, $"{name} · {detail}");
        Avalonia.Automation.AutomationProperties.SetHelpText(
            button,
            "Double tap to reopen this capture.");
        button.Click += async (_, _) => await RunAsync(() => _viewModel.OpenSessionAsync(session.Path));
        return button;
    }

    /// <summary>
    /// The get-started row. These read as accent links because they are links: each is a
    /// real, keyboard-focusable control wired to the same handler as its toolbar button, so
    /// the affordance the styling promises is honoured instead of being a dead caption.
    /// </summary>
    internal Control BuildHeroActions(bool dark, bool mobile)
    {
        var links = new List<(string Label, Func<Task> Action, string Tip)>();
        if (mobile)
        {
            links.Add(("OPEN LOG", OpenLogAsync, "Open a saved logcat file"));
            if (PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                links.Add(("ON-DEVICE LIVE", StartOnDeviceWithAccessSetupAsync, "Capture this device's log live"));
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

        if (mobile)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnSpacing = 8,
                RowSpacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            for (var index = 0; index < links.Count; index++)
            {
                var button = HeroLink(links[index].Label, links[index].Action, links[index].Tip, dark);
                button.HorizontalAlignment = HorizontalAlignment.Center;
                if (index < 2)
                {
                    Grid.SetColumn(button, index);
                }
                else
                {
                    Grid.SetRow(button, 1);
                    Grid.SetColumnSpan(button, 2);
                }

                grid.Children.Add(button);
            }

            return grid;
        }

        var row = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            ItemSpacing = 8,
            LineSpacing = 4,
        };

        for (var index = 0; index < links.Count; index++)
        {
            var button = HeroLink(links[index].Label, links[index].Action, links[index].Tip, dark);
            if (index == 0)
            {
                row.Children.Add(button);
                continue;
            }

            // Keep each decorative separator with the action it introduces so a narrow desktop
            // window can never leave a bullet stranded at the end of the previous wrap line.
            row.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "·",
                        FontSize = TextScale.Of(11),
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                    },
                    button,
                },
            });
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

            // The hit rect was the text rect: 58 x 18.8 dp, about a third of the platform
            // floor, on three adjacent controls separated by a "·" — so a slightly low tap
            // landed on nothing and a slightly wide one was ambiguous (finding F-03). The
            // visible link keeps its typography and its baseline; only the target grows,
            // which is what the command band a few rows above has always done.
            Padding = new Thickness(10, 3),
            MinHeight = TouchTarget.Here(),
            VerticalContentAlignment = VerticalAlignment.Center,
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
        Button Primary(string label, Func<Task> action)
        {
            var button = ActionButton(label, action, primary: true);
            _toolbarPrimary.Add(button);
            _toolbar.Children.Add(button);
            return button;
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

        // A session command that earns no toolbar button of its own: it applies to a minority
        // of files and it is a disclosure rather than an action. It reaches the desktop More
        // menu and the phone command sheet, which is every route the reader has to a
        // secondary command.
        void Secondary(
            string menuLabel,
            Func<Task> action,
            string? description = null,
            Func<bool>? canExecute = null,
            CommandGroup group = CommandGroup.ThisSession)
        {
            _toolbarSettings.Add(MenuAction(menuLabel, action));
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
                _liveButton = Primary("●  Live", StartOnDeviceWithAccessSetupAsync);
                UpdateLiveCaptureIndicator();
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
                // Says what happens, including the case where there is only one answer. The
                // sheet used to promise a chooser unconditionally and skip it whenever the
                // plot was fitted (V2-15).
                "Save entries as CSV, choosing the scope when more than one applies",
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

        // Where the lines ADR 0009 keeps as unknown are actually readable. Offered only when
        // the open session has some, so it is never a command that opens an empty pane
        // (V2-14).
        Secondary(
            "Lines not on the timeline…",
            ShowUnparsedLinesAsync,
            "Stack-trace frames and records with no usable timestamp",
            CanShowUnparsedLines);

        Setting("Appearance & timeline…", ShowAppearanceAsync, "Theme, text size, and how the plot is drawn");
        Setting("Session cache…", ShowSessionCacheAsync, "What this device is storing, and for how long");
        Setting("Diagnostic bundle…", CreateDiagnosticBundleAsync, "A redacted zip for a bug report");

        // Offered only where the host can say where it was installed from, the same way Share
        // is offered only where a file can be handed to another app: a host that cannot answer
        // that question cannot answer this one honestly either. The description changes with
        // the origin, because promising to ask Google Play and then opening a browser is the
        // same class of defect as a notice naming a command the reader cannot run (F-13).
        if (PlatformSourceRegistry.GetInstallOrigin is { } installOrigin)
        {
            Setting(
                "Check for updates…",
                CheckForUpdatesManuallyAsync,
                installOrigin() == AppInstallOrigin.PlayStore
                    ? "Ask Google Play whether a newer VisualCat is out"
                    : "Open the GitHub releases page — this build cannot update itself");
        }

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

    private bool CanShowUnparsedLines() =>
        _viewModel.Selected is { Snapshot: not null } tab && tab.OffTimelineCount > 0;

    /// <summary>
    /// Stops the restricted capture the notice is about and reopens the scope chooser.
    /// </summary>
    /// <remarks>
    /// The notice's own instruction was "Stop this capture, tap Live again, and choose
    /// full-device access" — three steps, in a sentence that did not fit on the screen. One
    /// control does all three (V2-11).
    /// </remarks>
    /// <summary>The opening words of the restricted-scope notice, so it can be recognised.</summary>
    private const string RestrictedScopeNoticeLead = "Only VisualCat's own log lines are being captured";

    private async Task SwitchLiveScopeFromNoticeAsync()
    {
        if (_viewModel.Selected is { } capturing && capturing.IsLiveCaptureActive)
        {
            await _viewModel.StopAsync(capturing);
        }

        await StartOnDeviceWithAccessSetupAsync();
    }

    private async Task ShowUnparsedLinesAsync()
    {
        if (_viewModel.Selected is not { Snapshot: not null } tab)
        {
            return;
        }

        await ShowDialogAsync(new UnparsedLinesDialog(tab));
    }

    /// <summary>
    /// Keeps a session-dependent command enabled only while there is a session for it to act
    /// on. Each of Share, Export CSV and Save returned silently when no session was loaded,
    /// while their controls stayed fully enabled — a command that looks available and does
    /// nothing is indistinguishable from one that is broken (finding 19).
    /// </summary>
    /// <summary>
    /// Says on the command band whether this device's log is being recorded right now.
    /// </summary>
    /// <remarks>
    /// There was no global indicator of any kind: a capture left running in a tab the reader
    /// had switched away from went on recording with nothing on screen to say so, and the
    /// only tell was the process table (finding F-22). The action that would have started a
    /// second one is the natural place to put it, because it is the control a reader reaches
    /// for when they are thinking about capturing.
    /// </remarks>
    private void UpdateLiveCaptureIndicator()
    {
        if (_liveButton is not { } button)
        {
            return;
        }

        var running = _viewModel.ActiveLiveCapture;
        button.Content = running is null ? "●  Live" : "◉  Recording";
        Avalonia.Automation.AutomationProperties.SetName(
            button,
            running is null ? "Capture this device's log" : $"Go to the running capture, {running.Title}");
        ToolTip.SetTip(
            button,
            running is null
                ? "Capture this device's log"
                : $"{running.Title} is capturing. Tap to go to it; stop it there.");
    }

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

    private async Task ExportAsync(TimeRange? selectedRange = null, SessionTabViewModel? sourceTab = null)
    {
        var tab = sourceTab ?? _viewModel.Selected;
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

        // A scope that ignores the filter writes the session, not the view of it. Until this
        // existed, every export ran through the workspace's filter unconditionally and
        // "everything in this session" was not a thing the product could produce (V2-15).
        var exportFilter = scope.IgnoresFilter
            ? VisualCat.Domain.Filters.FilterSpec.All
            : tab.Filter;

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
                    exportFilter,
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

        // Three answers, in the order the reader is most likely to want them, and only the
        // ones that are actually different from each other. The More sheet promises a
        // question — "Choose which entries to write, then save a CSV" — and used to skip
        // straight to the platform picker whenever the plot happened to be fitted, which is
        // the state every import and every reopen starts in (V2-15).
        var scopes = new List<ExportScope>(3);
        var filtered = !tab.Filter.IsUnconstrained;
        var matching = tab.Statistics?.TotalMatching;
        var viewCoversAll = sessionRange is not { } covered ||
            (covered.StartInclusive >= viewportRange.StartInclusive &&
             covered.EndExclusive <= viewportRange.EndExclusive);

        if (!viewCoversAll)
        {
            scopes.Add(new ExportScope(
                viewportRange,
                "What is in view",
                tab.MatchesInView,
                "Only the entries the plot is currently showing."));
        }

        if (sessionRange is { } whole)
        {
            scopes.Add(new ExportScope(
                whole,
                filtered ? "Everything matching the current filter" : "Everything in this session",
                matching,
                filtered
                    ? "Every entry the current filter admits, across the whole session."
                    : "Every entry in the session, across its whole time range."));

            // Offered only when the filter is actually hiding something, because otherwise it
            // is the same answer twice with two different names.
            if (filtered && tab.Snapshot?.TimedRange is { } untouched)
            {
                scopes.Add(new ExportScope(
                    untouched,
                    "Everything in this session",
                    tab.Snapshot.Descriptor.Counters.TimedEntries,
                    "Every entry, ignoring the filter this workspace has on.",
                    IgnoresFilter: true));
            }
        }

        return scopes.Count switch
        {
            0 => null,

            // One answer is not a question. It is still disclosed, in the notice the export
            // writes, and in the picker's own title.
            1 => scopes[0],
            _ => await ShowDialogAsync(new ExportScopeDialog(scopes)),
        };
    }

    /// <summary>
    /// Explains an on-device capture before Android asks the reader to allow it.
    /// </summary>
    /// <remarks>
    /// Tapping Live went straight to the system's "Allow VisualCat to access all device logs?"
    /// dialog, whose only affirmative is "Allow one-time access" — a serious-sounding question
    /// with no context, arriving before the app had said anything about what it wanted the log
    /// for or where the data goes. The app's own framing lands better before that dialog than
    /// after it (finding 27). It is shown once and remembered so VisualCat does not add a
    /// repeated disclosure on top of Android's own direct-capture consent or Wireless setup.
    /// </remarks>
    private async Task<bool> ConfirmLiveCaptureAsync(
        bool? fullDeviceOverride = null,
        bool usesWirelessAdb = false,
        bool accessContextAlreadyShown = false)
    {
        if (!OperatingSystem.IsAndroid())
        {
            return true;
        }

        // The Android scope chooser already explains what Live reads, how Wireless debugging is
        // used, and that nothing is uploaded. Repeating the legacy one-time disclosure immediately
        // after that chooser/setup creates warning fatigue and makes a successful setup look as if
        // another permission decision is still pending. Record the same acknowledgement and start.
        if (accessContextAlreadyShown)
        {
            if (!_settings.LiveCaptureNoticeAcknowledged)
            {
                _settings = _settings with { LiveCaptureNoticeAcknowledged = true };
                if (_settingsLoaded)
                {
                    await RunAsync(() => SaveSettingsAsync());
                }
            }

            return true;
        }

        var fullDevice = fullDeviceOverride ??
            (PlatformSourceRegistry.HasFullDeviceLogPermission?.Invoke() ?? true);
        if (_settings.LiveCaptureNoticeAcknowledged)
        {
            // The full explanation is shown once and remembered. On Android 13+ the platform
            // can still place its own per-use log-access consent in front of a direct READ_LOGS
            // capture, so a reader who has met it before deserves a short warning. Android 12
            // has no equivalent per-use sheet, and the Wireless ADB path has its own explicit
            // pairing/connection disclosure instead.
            //
            // Unless no prompt is coming, in which case saying one is coming is simply false
            // (finding F-13): without READ_LOGS the capture is own-app-only and Android has
            // nothing to ask.
            if (usesWirelessAdb)
            {
                ShowNotice(
                    "Full-device capture uses Wireless debugging. Keep it on until you stop Live. VisualCat closes its connection afterward, but Android leaves Wireless debugging enabled until you turn it off.");
            }
            else if (fullDevice)
            {
                ShowNotice(
                    OperatingSystem.IsAndroidVersionAtLeast(33)
                        ? "Android may ask you for device-log access when this direct capture starts."
                        : "Full-device log access is already configured; this capture can start directly.");
            }
            else
            {
                ShowNoticeWithGrantCommand(
                    "This capture will contain only VisualCat's own log lines. Android will not " +
                    "ask for anything: the permission a full-device capture needs cannot be " +
                    "requested by an app.");
            }

            return true;
        }

        var confirmed = await ShowDialogAsync(new ConfirmationDialog(
            "Before Live starts",
            "VisualCat reads the Android log and stores it in this app's private storage. " +
            "Nothing is uploaded and there is no telemetry; a session leaves the device only " +
            "when you share or export it yourself.\n\n" +
            LogAccessExplanation(fullDevice, usesWirelessAdb),
            "Start Live"));
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

    /// <summary>
    /// Starts an on-device capture, or takes the reader to the one already running.
    /// </summary>
    /// <remarks>
    /// One device log, one capture of it. The global Live action had no availability logic:
    /// tapping it during a capture created a second session and a second <c>logcat</c> child,
    /// and stopping the newly selected session left the first one recording with nothing on
    /// screen to say so — only closing its tab ended it (finding F-22). A second capture of
    /// the same source is not a feature anyone asked for; it is two readers of one stream
    /// competing for the same battery. So Live becomes the way back to the capture that
    /// exists, and the Stop control in that session stays the single way to end it.
    /// </remarks>
    /// <summary>
    /// What this device will actually do when the capture starts, composed from the grant it
    /// holds rather than from an assumption.
    /// </summary>
    /// <remarks>
    /// The copy was unconditional and, on a clean install, false: it said Android would ask for
    /// access, and no sheet can appear without READ_LOGS, because READ_LOGS is not a runtime
    /// permission and an app cannot request it. Session info explained the same state correctly
    /// and gave the exact command, so the product contradicted itself two taps apart
    /// (finding F-13). The two states have nothing in common, so they get two sentences rather
    /// than one hedged one.
    /// </remarks>
    private static string LogAccessExplanation(bool fullDevice, bool usesWirelessAdb = false) =>
        usesWirelessAdb
            ? "This full-device capture uses Android Wireless debugging. VisualCat opens only a " +
              "local authenticated log stream and does not change app permissions. It closes its " +
              "connection when capture stops, but Android leaves Wireless debugging enabled until " +
              "you turn it off in Settings."
            : fullDevice
            ? OperatingSystem.IsAndroidVersionAtLeast(33)
                ? "Android may now ask you to allow access to device logs. This is a separate " +
                  "per-use system consent for a direct READ_LOGS capture and can reappear later."
                : "Full-device READ_LOGS access is already configured on this Android version, " +
                  "so capture can start without an additional system log-access sheet."
            : RestrictedLogAccessExplanation();

    private static string RestrictedLogAccessExplanation()
    {
        const string explanation =
            "This capture will contain only VisualCat's own log lines, so an idle app produces " +
            "almost nothing. Android will not ask you for anything: the permission a direct " +
            "full-device capture needs is not one an app can request. Stop this capture and tap " +
            "Live again to use the recommended Wireless debugging path.";

        return PlatformSourceRegistry.FullDeviceLogGrantCommand is { Length: > 0 } command
            ? explanation +
              " Advanced developer fallback for this build: grant READ_LOGS from a computer " +
              "with adb; repeat after reinstall:\n\n" + command
            : explanation;
    }

    /// <summary>
    /// Puts a restricted-scope message in the lane, with the exact grant command and a way to
    /// copy it.
    /// </summary>
    /// <remarks>
    /// The lane used to say "See the session details for how to widen it" and stop there —
    /// pointing at a pane that is actually called Session info, and leaving the one thing the
    /// reader has to run out of the message that told them they needed it (finding F-13).
    /// </remarks>
    private void ShowNoticeWithGrantCommand(string message)
    {
        if (PlatformSourceRegistry.FullDeviceLogGrantCommand is not { Length: > 0 } command)
        {
            ShowNotice(message, NoticeKind.Failure);
            return;
        }

        ShowNotice(
            message + "\n\n" + command,
            NoticeKind.Failure,
            new NoticeAction("Copy command", () => CopyGrantCommandAsync(command)));
    }

    private async Task CopyGrantCommandAsync(string command)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            ShowNotice("This device did not offer a clipboard.", NoticeKind.Failure);
            return;
        }

        await clipboard.SetTextAsync(command);
        ShowNotice("Grant command copied. Run it from a computer with adb.", NoticeKind.Completion);
    }

    private async Task StartOnDeviceAsync(
        ILogSource? preparedSource = null,
        bool usesWirelessAdb = false,
        bool accessContextAlreadyShown = false)
    {
        if (_viewModel.ActiveLiveCapture is { } running)
        {
            if (preparedSource is not null)
            {
                await preparedSource.DisposeAsync();
            }

            _viewModel.Selected = running;
            ShowNotice(
                $"{running.Title} is already capturing this device's log. " +
                "Stop it before starting another capture.");
            return;
        }

        if (!await ConfirmLiveCaptureAsync(
                preparedSource is null ? null : true,
                usesWirelessAdb,
                accessContextAlreadyShown))
        {
            if (preparedSource is not null)
            {
                await preparedSource.DisposeAsync();
            }

            return;
        }

        var source = preparedSource ?? PlatformSourceRegistry.CreateOnDeviceSource?.Invoke();
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
        var scopeNoticeRevision = 0L;
        if (source is ISourceScopeReporter reporter)
        {
            var scopeNotice = 0L;
            reporter.ScopeResolved += report =>
            {
                if (!report.FullDevice && report.Summary is { Length: > 0 })
                {
                    // The remedy the source composed is the whole answer — "Tap Live again and
                    // choose the option that allows access" after a decline, the exact adb
                    // command when the grant is missing — and the lane used to drop it and
                    // point at a pane by a name the pane does not have (finding F-13).
                    var grant = PlatformSourceRegistry.FullDeviceLogGrantCommand;
                    var copyable = grant is { Length: > 0 } &&
                        report.Remedy?.Contains(grant, StringComparison.Ordinal) == true;

                    // The remedy is a control, not the tail of a paragraph. It was the last
                    // sentence of a notice taller than the lane, so on a phone the one action
                    // the notice exists to offer was the part below the fold (V2-11).
                    ShowNotice(
                        RestrictedScopeNoticeLead + " — " +
                        $"{report.Summary}.\n\n{report.Remedy}",
                        NoticeKind.Failure,
                        copyable
                            ? new NoticeAction("Copy command", () => CopyGrantCommandAsync(grant!))
                            : new NoticeAction("Switch scope", SwitchLiveScopeFromNoticeAsync));
                    scopeNotice = NoticeRevision;
                    scopeNoticeRevision = scopeNotice;
                }
                else if (report.FullDevice && scopeNotice != 0)
                {
                    RetractNotice(scopeNotice);
                    scopeNotice = 0;
                    scopeNoticeRevision = 0;
                    ShowNotice(
                        "Log access was allowed after all — this capture is seeing the whole device.",
                        NoticeKind.Information);
                }
            };
        }

        try
        {
            await RunAsync(async () =>
            {
                await using (source)
                {
                    return await _viewModel.CaptureAsync(source, null);
                }
            });
        }
        finally
        {
            // The notice was written while the capture was running and stayed in the present
            // tense after it stopped: "are being captured", over a status line reading
            // `Stopped · 47 entries kept`, until somebody dismissed it by hand (V2-11). A
            // finished capture gets the past tense, and only while its own message is still
            // the one on screen.
            if (scopeNoticeRevision != 0 && IsHoldingNoticeStartingWith(RestrictedScopeNoticeLead))
            {
                ShowNotice(
                    "This capture recorded VisualCat's own log lines only.",
                    NoticeKind.Information);
            }

            if (usesWirelessAdb && _noticeKind != NoticeKind.Failure)
            {
                ShowNotice(
                    "VisualCat closed its Wireless debugging connection and discarded the decrypted key. Android still leaves Wireless debugging enabled; turn it off when you are finished.",
                    NoticeKind.Completion,
                    new NoticeAction("Open settings", OpenWirelessDebuggingSettingsFromNoticeAsync));
            }
        }
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
        viewModel.PropertyChanged += OnSessionTabPropertyChanged;
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
        // The chip in the count row is the direct route to the same card the More menu offers.
        // Presentation is the shell's, so the workspace asks rather than presents (V2-13).
        workspace.OffTimelineRequested += () => _ = RunAsync(ShowUnparsedLinesAsync);
        workspace.AskForNumberAsync = async (title, question, initial, maximum) =>
            await ShowDialogAsync(new NumberPromptDialog(title, question, initial, 1, maximum));
        workspace.PartialRecoveryRaised += message =>
            ShowNotice(
                message,
                NoticeKind.Failure,
                new NoticeAction("Review", () => ReviewRecoveredSessionAsync(viewModel)));
        workspace.RestoreDisplayMode(displayMode);
        workspace.RestoreMobileTimelineShare(_settings.MobileTimelineShare);
        workspace.RestoreMobileTimelineWidthShare(_settings.MobileTimelineWidthShare);
        workspace.DisplayModeChanged += PersistWorkspaceDisplayMode;
        workspace.SplitShareChanged += PersistMobileTimelineShare;
        workspace.SplitWidthShareChanged += PersistMobileTimelineWidthShare;
        workspace.CompactEditorChanged += _ => UpdateCompactCommandComposition();
        workspace.ExportRequested += range => _ = RunAsync(() => ExportAsync(range));
        workspace.StopRequested += () => _viewModel.StopAsync(viewModel);

        // A session that failed with no data offers the only two useful actions there are.
        workspace.CloseRequested += () => _ = RunAsync(() => _viewModel.CloseAsync(viewModel));
        workspace.OpenLogRequested += OpenLogAsync;
        return workspace;
    }

    /// <summary>
    /// Gives a recovered capture the three explicit dispositions the recovery warning names:
    /// preserve it as interrupted, export exactly what survived, or confirm permanent
    /// deletion. The view owns none of these shell-level operations (F-19).
    /// </summary>
    private async Task ReviewRecoveredSessionAsync(SessionTabViewModel tab)
    {
        if (!_viewModel.Tabs.Contains(tab) || tab.Activity != SessionActivity.RecoverablePartial)
        {
            ShowNotice("That recovered capture is no longer open.", NoticeKind.Information);
            return;
        }

        var entries = tab.Snapshot?.Descriptor.Counters.ParsedEntries ?? 0;
        var canDelete = IsDirectCachedSession(tab.SessionPath);
        var choice = await ShowDialogAsync(new RecoveredSessionDialog(tab.Title, entries, canDelete));
        switch (choice)
        {
            case RecoveredSessionAction.Keep:
                ShowNotice(
                    $"Kept {tab.Title} as an interrupted capture; {Counted.Entries(entries)} remain available.",
                    NoticeKind.Completion);
                break;

            case RecoveredSessionAction.Export:
                await ExportAsync(sourceTab: tab);
                break;

            case RecoveredSessionAction.Delete when canDelete:
                var confirmed = await ShowDialogAsync(new ConfirmationDialog(
                    "Delete recovered capture?",
                    $"{tab.Title} and its {Counted.Entries(entries)} will be permanently removed from this device. " +
                    "This cannot be undone."));
                if (confirmed != true)
                {
                    break;
                }

                var path = tab.SessionPath;
                try
                {
                    await _viewModel.CloseAsync(tab);
                    await Task.Run(() => TemporarySessionRetentionService.DeleteExactSession(
                        WorkspaceViewModel.TemporarySessionRoot,
                        path));
                    ShowNotice($"Deleted recovered capture {tab.Title}.", NoticeKind.Completion);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    ShowNotice(
                        $"The recovered capture was closed but could not be deleted: " +
                        WorkspaceViewModel.FriendlyMessage(exception),
                        NoticeKind.Failure);
                }

                break;
        }
    }

    private static bool IsDirectCachedSession(string sessionPath)
    {
        var root = Path.GetFullPath(WorkspaceViewModel.TemporarySessionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(sessionPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetDirectoryName(path), root, comparison) &&
               path.EndsWith(".vcat", comparison);
    }

    private void OnSessionSnapshotChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            UpdateSessionActionAvailability();
            UpdateSessionStrip();
            PersistOpenWorkspace();
        });

    private void OnSessionTabPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SessionTabViewModel.Activity) or nameof(SessionTabViewModel.Title))
        {
            Dispatcher.UIThread.Post(UpdateSessionStrip);
        }
    }

    private void RemoveTab(SessionTabViewModel viewModel)
    {
        if (!_tabItems.Remove(viewModel, out var item))
        {
            return;
        }

        viewModel.SnapshotChanged -= OnSessionSnapshotChanged;
        viewModel.PropertyChanged -= OnSessionTabPropertyChanged;

        // The view goes with the tab, and so does everything it is holding. A closed session's
        // workspace stayed subscribed to the session it no longer draws, and — in a
        // compact-height viewport — kept the command strip it had handed to the shell's shared
        // row, so closing three tabs left three strips stacked in one cell (finding F-39).
        // This is the same release RebuildWorkspaceViews performs for the same reason.
        if (item.Content is SessionWorkspaceView closing)
        {
            closing.DetachViewModel();
        }

        _tabs.Items.Remove(item);
        RemoveSessionChip(viewModel);
        _emptyState.IsVisible = _tabs.Items.Count == 0;
        if (_emptyState.IsVisible)
        {
            // The session just closed is the most likely one to be wanted back.
            RequestRecentSessionsRefresh();
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
        var configuredPath = string.IsNullOrWhiteSpace(_settings.AdbPath) ? null : _settings.AdbPath.Trim();
        var executable = AdbLocator.Find(_settings.AdbPath);
        if (executable is null)
        {
            ShowNotice(
                configuredPath is not null && !File.Exists(configuredPath)
                    ? $"The configured ADB path '{configuredPath}' was not found, and no other ADB installation was detected. " +
                      "Correct it in Appearance & timeline, install Android platform-tools, or set ANDROID_SDK_ROOT."
                    : "ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.",
                NoticeKind.Failure);
            return;
        }

        if (configuredPath is not null && !File.Exists(configuredPath))
        {
            if (!string.Equals(_reportedInvalidAdbPath, configuredPath, StringComparison.OrdinalIgnoreCase))
            {
                _reportedInvalidAdbPath = configuredPath;
                ShowNotice(
                    $"The configured ADB path '{configuredPath}' was not found; using '{executable}' from auto-detection. " +
                    "Correct or clear the path in Appearance & timeline.",
                    NoticeKind.Information);
            }
        }
        else
        {
            _reportedInvalidAdbPath = null;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        using var dialog = new AdbCaptureDialog(
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
        await OpenPathsAsync(_startupPaths);
        if (PlatformSourceRegistry.ConsumeLaunchFilesAsync is { } consume)
        {
            await OpenIncomingAsync(await consume(CancellationToken.None));
        }
    }

    /// <summary>Opens files another app handed over, each under the name that app gave it.</summary>
    private async Task OpenIncomingAsync(IReadOnlyList<IncomingFile> files) =>
        await OpenPathsAsync(
            files.Select(static file => file.Path),
            files.ToDictionary(
                static file => Path.GetFullPath(file.Path),
                static file => file.DisplayName,
                StringComparer.OrdinalIgnoreCase));

    private async Task OpenPathsAsync(
        IEnumerable<string> values,
        Dictionary<string, string>? displayNames = null)
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
                // The provider's display name when there is one, so the tab is called
                // "tiny.txt" rather than the private cache filename (finding F-27).
                var current = path;
                await RunAsync(() => displayNames?.TryGetValue(current, out var shown) == true
                    ? _viewModel.ImportFileAsync(current, null, shown)
                    : _viewModel.ImportFileAsync(current));
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
            _settings = ForgetSpentUpdateMemory(await _settingsStore.LoadAsync());
            _settingsLoaded = true;
            if (_hostWindow is { } window)
            {
                ApplyWindowSettings(window);
            }

            WorkspaceViewModel.ConfigureTemporarySessionRoot(_settings.SessionDirectory);
            _viewModel.ConfigureUiRefreshLimit(_settings.UiRefreshLimit);
            await ConfigureDiagnosticsAsync();
            ApplyAppearance();
            // Restore first, then anything the launch intent carried: a file the reader has
            // just tapped in another app belongs in front of the workspace they left behind.
            await RestoreWorkspaceAsync();
            await OpenStartupPathsAsync();

            // Restore before cleanup so the retention pass can protect every open tab. An
            // old session that is still part of someone's workspace is in active use even
            // when it is not a running capture; deleting it underneath that tab would make
            // the on-screen state impossible to reopen or export (F-23).
            if (_settings.TemporaryCleanupEnabled)
            {
                var result = await TemporarySessionRetentionService.CleanupAsync(
                    WorkspaceViewModel.TemporarySessionRoot,
                    enabled: true,
                    TimeSpan.FromDays(_settings.TemporaryRetentionDays),
                    _settings.TemporaryRetentionMaximumBytes,
                    DateTimeOffset.UtcNow,
                    OpenSessionPaths());
                if (result.Errors.Count > 0)
                {
                    ShowNotice($"Temporary cleanup left {result.Errors.Count:N0} session(s) in place.", NoticeKind.Failure);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Startup work that is superseded or abandoned is not a failure, and it is the
            // one thing this block used to be able to say. A restore whose view refresh was
            // superseded by the next one reached here and painted a red
            // "Startup settings: The operation was canceled." over a workspace that had in
            // fact restored perfectly (third device pass). Nothing is broken, there is
            // nothing for the reader to do, and so there is nothing to say.
        }
        catch (Exception exception)
        {
            // Not "Startup settings": the settings are the first thing this block loads and
            // usually not the thing that failed, and the reader's question is what it means
            // for the app in front of them. FriendlyMessage is also what keeps a trimmed
            // Release build from answering with a resource key (finding F-04).
            WorkspaceViewModel.RecordFailure("startup", exception);
            ShowNotice(
                $"VisualCat started, but part of the startup did not finish · {WorkspaceViewModel.FriendlyMessage(exception)}",
                NoticeKind.Failure);
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

    /// <summary>
    /// Every session the current workspace still depends on, whether complete, recovered, or
    /// actively capturing. Retention treats these paths like open documents, not cache waste.
    /// </summary>
    private HashSet<string> OpenSessionPaths() =>
        _viewModel.Tabs
            .Select(static tab => Path.GetFullPath(tab.SessionPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task OpenRecentAsync()
    {
        var sessions = await TemporarySessionRetentionService.ScanAsync(WorkspaceViewModel.TemporarySessionRoot);
        var path = await ShowDialogAsync(new RecentSessionsDialog(sessions, CapturingSessionPaths()));
        if (path is null)
        {
            return;
        }

        // The empty card offers the one action that changes what it is empty of, and the
        // shell owns that action (V2-03).
        if (string.Equals(path, RecentSessionsDialog.CaptureThisDevice, StringComparison.Ordinal))
        {
            if (OperatingSystem.IsAndroid() && PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                await StartOnDeviceWithAccessSetupAsync();
            }

            return;
        }

        await RunAsync(() => _viewModel.OpenSessionAsync(path));
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
            WorkspaceViewModel.RecordFailure("settings.apply", exception);
            ShowNotice(
                $"Could not apply the new settings · {WorkspaceViewModel.FriendlyMessage(exception)}",
                NoticeKind.Failure);
        }
    }

    private async Task ShowSessionCacheAsync()
    {
        var updated = await ShowDialogAsync(new SessionCacheDialog(
            WorkspaceViewModel.TemporarySessionRoot,
            _settings,
            CapturingSessionPaths(),
            OpenSessionPaths()));
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
        var scaleBefore = TextScale.Effective;
        TextScale.Platform = PlatformSourceRegistry.PlatformFontScale ?? 1;
        TextScale.User = _settings.TextScale;
        FontSize = TextScale.Of(14);
        ApplyTextScaleToShell();

        // A workspace resolves every font size in it while it is being built, so a changed
        // scale reaches the screen by building it again and no other way — whichever control
        // the reader used to change it. Only the platform's route used to answer that, so
        // More → Appearance & timeline → Text scale grew the command bar, the tab titles and
        // the status line and left the log those exist to frame at exactly the size it was;
        // the setting was answered by the chrome and ignored by the one surface it is raised
        // to make readable (A-03). Rebuilding is also how a sheet in front of them stops
        // being the odd one out (F-40).
        if (Math.Abs(TextScale.Effective - scaleBefore) > 0.001)
        {
            // CreateWorkspaceView applies the display settings and restores both stored
            // shares, so the loop below would only repeat what the new views already have.
            RebuildWorkspaceViews();
            RefreshOverlays();
            return;
        }

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
                workspace.RestoreMobileTimelineShare(_settings.MobileTimelineShare);
                workspace.RestoreMobileTimelineWidthShare(_settings.MobileTimelineWidthShare);
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

    /// <summary>
    /// One press, one layer — claimed at the top level, where the route begins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Android delivers a key Back as a <see cref="Key.Escape"/> key-down and then the platform
    /// back callback. A dialog whose Cancel carries <c>IsCancel</c> answers that key-down
    /// itself: Avalonia's <see cref="Button"/> registers a handler for Escape on the visual
    /// root. So the press closed the card, and the back callback then found an empty overlay
    /// stack and let the platform background the task — Back both dismissed the
    /// <em>Choose what Live captures</em> card and left the app, in one press, on the one
    /// dialog V2-18 exists to make reachable. The <em>More</em> sheet was unaffected because it
    /// has no such button, which is why the earlier pass recorded Back as passing.
    /// </para>
    /// <para>
    /// The handler is on the top level and tunnelling, for two reasons. Tunnelling runs before
    /// the bubble phase, which is where <c>Button</c>'s root hook lives. And the top level is
    /// the one element every key route passes through: with a card open and nothing focused
    /// inside it, the route's source <em>is</em> the top level, so a handler on this view would
    /// never see the key at all.
    /// </para>
    /// </remarks>
    private void OnShellKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.Key != Key.Escape || _overlays.Count == 0)
        {
            return;
        }

        DismissTopOverlay();
        NoteDismissedByEscape();
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        _ = sender;

        var control = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (control && eventArgs.Key == Key.O)
        {
            _ = RunAsync(() => shift ? OpenSessionAsync() : OpenLogAsync());
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
            _ = RunAsync(() => ExportAsync());
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
    /// <remarks>
    /// This is the funnel every <c>RunAsync</c>-wrapped action fails through, so it is also
    /// the widest way for a framework message to reach a reader. It used to interpolate
    /// <c>GetBaseException().Message</c> directly, which in a trimmed Release build is a
    /// resource key rather than a sentence — <c>MakeException, arg0, arg1</c> — the exact
    /// defect F-04 records, on a route the first remediation did not reach.
    /// </remarks>
    private void ReportFailure(Exception exception)
    {
        WorkspaceViewModel.RecordFailure("shell.action", exception);
        var message = WorkspaceViewModel.FriendlyMessage(exception);

        // Said once. A failed import already owns the whole workspace with a failure card
        // that states the reason and the remedy, and the same sentence was appearing three
        // times at once — the card, the session status line, and this lane, which is the one
        // that then has to be dismissed by hand (V2-12). The lane is for results whose only
        // other evidence is off screen; a full-page card is not off screen.
        foreach (var tab in _viewModel.Tabs)
        {
            if (string.Equals(tab.FailureReason, message, StringComparison.Ordinal))
            {
                return;
            }
        }

        ShowNotice($"Could not complete that action · {message}", NoticeKind.Failure);
    }

    public async ValueTask DisposeAsync()
    {
        // Clear the shared slot only while this view is still the one in it: a replacement
        // may already have taken the subscriptions over, and dropping its registration here
        // would leave the live view deaf to the platform. Detaching this view's own handlers
        // is unconditional and safe either way — they are this instance's delegates, and
        // removing a handler that is no longer subscribed does nothing.
        Interlocked.CompareExchange(ref s_platformEvents, null, _platformEvents);
        _platformEvents.Detach();
        DisposeUpdateWork();
        StopNoticeTimer();
        StopObservingSafeArea();

        // A cache walk must not keep this view (and its complete visual tree) alive after the
        // host closes it. ScanAsync observes this token between every session it inspects.
        var disposeRecentRefresh = Interlocked.Exchange(ref _recentRefreshDisposed, 1) == 0;
        if (disposeRecentRefresh)
        {
            _recentRefreshLifetime.Cancel();
        }

        await WaitForRecentSessionsRefreshAsync();

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

        // Drained beside the workspace writes above, and for the same reason: the settings gate
        // is disposed at the end of this method, and a dismissal recorded a moment earlier is
        // still on its way to disk.
        await DrainUpdatePersistAsync();

        await _viewModel.DisposeAsync();
        if (_diagnostics is not null)
        {
            // Unpublished before it is closed, the way ConfigureDiagnosticsAsync does it for
            // the same reason. RecordFailure reaches the sink through a static handle, so
            // leaving it published meant every later failure wrote into a disposed logger and
            // swallowed the ObjectDisposedException — and the handle went on holding the
            // logger for the life of the process.
            _viewModel.ConfigureDiagnostics(null);
            await _diagnostics.DisposeAsync();
            _diagnostics = null;
        }

        _settingsSaveGate.Dispose();
        if (disposeRecentRefresh)
        {
            _recentRefreshLifetime.Dispose();
        }
    }
}
