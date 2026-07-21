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
using VisualCat.Application.UseCases;
using VisualCat.Domain;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Configuration;
using VisualCat.Infrastructure.Diagnostics;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Views;

public sealed class MainView : UserControl, IAsyncDisposable
{
    private readonly WorkspaceViewModel _viewModel = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _message = new();
    private readonly Border _emptyState = new();
    private readonly DockPanel _rootPanel = new();
    private readonly Dictionary<SessionTabViewModel, TabItem> _tabItems = [];
    private readonly string[] _startupPaths;
    private readonly SettingsStore _settingsStore;
    private ApplicationSettings _settings = new();
    private RollingDiagnosticLogger? _diagnostics;
    private bool _startupOpened;
    private bool _settingsLoaded;
    private Window? _hostWindow;
    private readonly Action<IReadOnlyList<string>> _launchFilesHandler;

    // Responsive command bar: primary actions are always inline, flexible actions render as
    // buttons while they fit and fold into "More" when they do not, and settings live in
    // "More" permanently. Widths are cached from the first arrange so a resize only re-picks
    // the split, never re-measures.
    private readonly StackPanel _toolbar = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        ClipToBounds = true,
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
    private double _lastToolbarWidth = -1;
    private bool _reflowingToolbar;
    private bool _mobileCompactHeight;

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
        PlatformSourceRegistry.LaunchFilesReceived += _launchFilesHandler;
        Content = Build();
        SizeChanged += (_, eventArgs) => UpdateMobileChrome(eventArgs.NewSize);
        ActualThemeVariantChanged += (_, _) => ApplyThemeSurfaces();
        ApplyThemeSurfaces();
        _viewModel.TabAdded += (_, tab) => Dispatcher.UIThread.Post(() => AddTab(tab));
        _viewModel.TabRemoved += (_, tab) => Dispatcher.UIThread.Post(() => RemoveTab(tab));
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem is TabItem { Tag: SessionTabViewModel tab })
            {
                _viewModel.Selected = tab;
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (!_startupOpened)
            {
                _startupOpened = true;
                _ = InitializeAsync();
            }
        };
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
        await _settingsStore.SaveAsync(_settings, cancellationToken);
    }

    // The brand/command bar stays dark in both themes as the application's identity
    // band; only the workspace surface below it follows the requested theme variant.
    private void ApplyThemeSurfaces()
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        _rootPanel.Background = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.Surface(dark));
        _emptyState.Child = BuildEmptyState(dark);
    }

    private DockPanel Build()
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
                FontSize = 17,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        brand.Children.Add(new StackPanel
        {
            Spacing = -2,
            Children =
            {
                new TextBlock
                {
                    Text = "VISUALCAT",
                    FontWeight = FontWeight.Bold,
                    FontSize = 15,
                    Foreground = new SolidColorBrush(Color.Parse("#EAF2FF")),
                },
                new TextBlock
                {
                    Text = "LOGCAT SIGNAL ANALYZER",
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.Parse("#7187A6")),
                },
            },
        });
        brandRow.Children.Add(brand);
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.TextTrimming = TextTrimming.CharacterEllipsis;
        _message.Foreground = new SolidColorBrush(Color.Parse("#9EB1CB"));
        Grid.SetColumn(_message, 2);
        brandRow.Children.Add(_message);
        commandContent.Children.Add(brandRow);

        commandContent.Children.Add(BuildActionToolbar());

        var commandBar = _commandBar = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#111C2D"), 0),
                    new GradientStop(Color.Parse("#0B1220"), 1),
                },
            },
            BorderBrush = new SolidColorBrush(Color.Parse("#243753")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(OperatingSystem.IsAndroid() ? 10 : 12, 9),
            Child = commandContent,
        };
        DockPanel.SetDock(commandBar, Dock.Top);
        root.Children.Add(commandBar);
        var workspaceHost = new Grid();
        workspaceHost.Children.Add(_tabs);
        // The overlay carries the get-started links now, so it must receive clicks. It is
        // only ever visible while no session is open (see the IsVisible toggles), and its
        // Border has no background, so its empty area still passes pointer events through to
        // the tab host beneath — only the links themselves are hit targets.
        _emptyState.SetValue(Panel.ZIndexProperty, 1);
        workspaceHost.Children.Add(_emptyState);
        root.Children.Add(workspaceHost);
        return root;
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
        _commandBar.Padding = compactHeight || sessionOpen
            ? new Thickness(10, 5)
            : new Thickness(10, 9);
        // Session tabs remain a compact top row. A side rail looks efficient on paper, but
        // wastes a large column when a phone has the common one-session workspace.
        _tabs.TabStripPlacement = Dock.Top;
        foreach (var tab in _tabs.Items.OfType<TabItem>())
        {
            if (tab.Header is StackPanel { Children.Count: > 0 } header &&
                header.Children[0] is TextBlock title)
            {
                title.MaxWidth = 240;
            }
        }

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
                    FontSize = 9,
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
                    FontSize = OperatingSystem.IsAndroid() ? 22 : 28,
                    FontWeight = FontWeight.Bold,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextPrimary(dark)),
                },
                new TextBlock
                {
                    Text = "Turn raw Android logcat into a navigable severity × time signal.",
                    FontSize = OperatingSystem.IsAndroid() ? 13 : 15,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                },
                levelLegend,
                BuildHeroActions(dark),
                new TextBlock
                {
                    Text = $"VisualCat {ProductInfo.InformationalVersion} · local-first · no telemetry",
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(VisualCat.App.Timeline.WorkspacePalette.TextMuted(dark)),
                    Opacity = 0.8,
                },
            },
        };
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

            if (PlatformSourceRegistry.ShareFileAsync is not null)
            {
                links.Add(("SHARE", SharePortableAsync, "Share a portable session archive"));
            }
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
                    FontSize = 11,
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
            FontSize = 11,
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

    private static Button ActionButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse(primary ? "#174F78" : "#172235")),
            BorderBrush = new SolidColorBrush(Color.Parse(primary ? "#3CAFEF" : "#2A3B55")),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.Parse("#EDF6FF")),
            MinHeight = OperatingSystem.IsAndroid() ? 48 : 0,
        };
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

        void Flexible(string buttonLabel, string menuLabel, Func<Task> action)
        {
            var command = new ToolbarCommand(ActionButton(buttonLabel, action), MenuAction(menuLabel, action));
            _toolbarFlexible.Add(command);
            _toolbar.Children.Add(command.Button);
        }

        void Setting(string menuLabel, Func<Task> action) => _toolbarSettings.Add(MenuAction(menuLabel, action));

        Primary("＋  Open log", OpenLogAsync);
        if (OperatingSystem.IsAndroid())
        {
            if (PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                Primary("●  Live", StartOnDeviceAsync);
            }

            Flexible("Open session", "Open session…", OpenSessionAsync);
            Flexible("Open archive", "Open portable archive…", OpenArchiveAsync);
            Flexible("Recent", "Recent sessions…", OpenRecentAsync);
            if (PlatformSourceRegistry.ShareFileAsync is not null)
            {
                Flexible("Share", "Share…", SharePortableAsync);
            }

            Flexible("Export", "Export CSV…", () => ExportAsync());
        }
        else
        {
            Primary("●  ADB live", StartAdbAsync);
            Flexible("Open session", "Open session…", OpenSessionAsync);
            Flexible("Recent", "Recent sessions…", OpenRecentAsync);
            Flexible("Follow file", "Follow growing file…", FollowFileAsync);
            Flexible("Open archive", "Open portable archive…", OpenArchiveAsync);
            Flexible("Save", "Save session…", () => SaveSessionAsync(portable: false));
            Flexible("Save portable", "Save portable…", () => SaveSessionAsync(portable: true));
            Flexible("Export", "Export CSV…", () => ExportAsync());
        }

        Setting("Appearance & timeline…", ShowAppearanceAsync);
        Setting("Session cache…", ShowSessionCacheAsync);
        Setting("Diagnostic bundle…", CreateDiagnosticBundleAsync);

        _moreMenu = new Menu
        {
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#172235")),
            Items = { _moreItem },
        };
        _toolbar.Children.Add(_moreMenu);
        _toolbar.SizeChanged += (_, args) => ReflowToolbar(args.NewSize.Width);
        return _toolbar;
    }

    /// <summary>
    /// Re-picks which flexible actions render as buttons and which fold into "More" for the
    /// current width. Primaries stay inline and settings stay in the menu; everything between
    /// is shown in order until the row is full, so the toolbar uses the space it has instead
    /// of hiding actions behind "More" while the row sits half empty.
    /// </summary>
    private void ReflowToolbar(double available)
    {
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
        public ToolbarCommand(Button button, MenuItem menuItem)
        {
            Button = button;
            MenuItem = menuItem;
        }

        public Button Button { get; }
        public MenuItem MenuItem { get; }
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

        _message.Text = OperatingSystem.IsAndroid()
            ? "Folder-backed sessions are app-private on Android. Use Recent sessions or open a portable .vcat.zip archive."
            : "The selected session folder is not exposed as a filesystem path.";
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
            _message.Text = $"Saved: {destination}";
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

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export filtered entries",
            SuggestedFileName = $"{Path.GetFileNameWithoutExtension(tab.Title)}.csv",
            DefaultExtension = "csv",
        });
        if (file is not null)
        {
            using (file)
            {
                await RunAsync(() => StorageFileBridge.WriteAsync(
                    file,
                    (path, cancellationToken) => ExportService.ExportNormalizedCsvAsync(
                        tab.Snapshot,
                        path,
                        selectedRange ?? tab.Filter.TimeRange ?? tab.Viewport.Value,
                        tab.Filter,
                        _settings.ExportOrder == "Chronological"
                            ? VisualCat.Domain.Queries.EntryOrder.Chronological
                            : VisualCat.Domain.Queries.EntryOrder.SourceSequence,
                        _settings.ExportEncoding != "utf-8",
                        cancellationToken)));
            }
        }
    }

    private async Task StartOnDeviceAsync()
    {
        var source = PlatformSourceRegistry.CreateOnDeviceSource?.Invoke();
        if (source is null)
        {
            _message.Text = "On-device log access is unavailable.";
            return;
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
            _message.Text = "Portable session handed to the platform share sheet.";
        });
    }

    private void AddTab(SessionTabViewModel viewModel)
    {
        if (_tabItems.ContainsKey(viewModel))
        {
            return;
        }

        var mobile = OperatingSystem.IsAndroid();
        var title = new TextBlock
        {
            Text = viewModel.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = mobile ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            MaxWidth = mobile ? 240 : double.PositiveInfinity,
        };
        ToolTip.SetTip(title, viewModel.Title);
        var close = new Button
        {
            Content = "×",
            Padding = mobile ? new Thickness(12, 0) : new Thickness(5, 0),
            Margin = new Thickness(5, 0, 0, 0),
            MinWidth = mobile ? 48 : 0,
            MinHeight = mobile ? 48 : 0,
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { title, close },
        };
        var workspace = new SessionWorkspaceView(viewModel);
        workspace.ApplyDisplaySettings(
            _settings.IntensityScale,
            _settings.TimelineNormalization,
            _settings.TimelineMinimumUsPerPixel,
            _settings.TimelinePixelSnap,
            _settings.TimelineMinimumBarWidth);
        var item = new TabItem
        {
            Header = header,
            Content = workspace,
            Tag = viewModel,
        };
        close.Click += async (_, _) => await _viewModel.CloseAsync(viewModel);
        workspace.ExportRequested += range => _ = ExportAsync(range);
        workspace.StopRequested += () => _viewModel.StopAsync(viewModel);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SessionTabViewModel.Status))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var state = viewModel.Status.Split('·', 2)[0].Trim();
                    title.Text = mobile || state is "Ready"
                        ? viewModel.Title
                        : $"{viewModel.Title} · {state}";
                });
            }
        };
        _tabItems.Add(viewModel, item);
        _emptyState.IsVisible = false;
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        _viewModel.Selected = viewModel;
        UpdateMobileChrome(Bounds.Size);
    }

    private void RemoveTab(SessionTabViewModel viewModel)
    {
        if (!_tabItems.Remove(viewModel, out var item))
        {
            return;
        }

        _tabs.Items.Remove(item);
        _emptyState.IsVisible = _tabs.Items.Count == 0;
        UpdateMobileChrome(Bounds.Size);
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
            _message.Text = "ADB was not found. Install Android platform-tools or set ANDROID_SDK_ROOT.";
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
                _message.Text = $"Startup source not found: {path}";
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
                    _message.Text = $"Temporary cleanup left {result.Errors.Count:N0} session(s) in place.";
                }
            }

            await OpenStartupPathsAsync();
        }
        catch (Exception exception)
        {
            _message.Text = $"Startup settings: {exception.GetBaseException().Message}";
        }
    }

    private async Task OpenRecentAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var sessions = await TemporarySessionRetentionService.ScanAsync(WorkspaceViewModel.TemporarySessionRoot);
        var dialog = new RecentSessionsDialog(sessions);
        var path = await dialog.ShowDialog<string?>(owner);
        if (path is not null)
        {
            await RunAsync(() => _viewModel.OpenSessionAsync(path));
        }
    }

    private async Task ShowAppearanceAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var updated = await new AppearanceDialog(_settings).ShowDialog<ApplicationSettings?>(owner);
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
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            _message.Text = exception.GetBaseException().Message;
        }
    }

    private async Task ShowSessionCacheAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var updated = await new SessionCacheDialog(
            WorkspaceViewModel.TemporarySessionRoot,
            _settings).ShowDialog<ApplicationSettings?>(owner);
        if (updated is null)
        {
            return;
        }

        _settings = updated;
        await RunAsync(() => _settingsStore.SaveAsync(_settings));
    }

    private async Task CreateDiagnosticBundleAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var confirmed = await new ConfirmationDialog(
            "Create diagnostic bundle",
            "The bundle excludes raw log messages, source paths, hashes, searches, and device serials. It still contains timings, counts, system details, and sanitized session metadata. Review it before sharing.",
            "Create bundle").ShowDialog<bool>(owner);
        if (!confirmed)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save VisualCat diagnostic bundle",
            SuggestedFileName = $"visualcat-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
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
                    _message.Text = $"Diagnostic bundle saved: {file.Name}";
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

        FontSize = 14 * _settings.TextScale;
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
            _message.Text = string.Empty;
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
            _message.Text = string.Empty;
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
    private void ReportCancelled() => _message.Text = string.Empty;

    /// <summary>
    /// A real failure is labelled as one. A bare framework message ("The operation was
    /// canceled.", "Access to the path is denied.") does not say which action failed, and
    /// §18.1 asks a user message to state what happened and what remains usable.
    /// </summary>
    private void ReportFailure(Exception exception) =>
        _message.Text = $"Could not complete that action · {exception.GetBaseException().Message}";

    public async ValueTask DisposeAsync()
    {
        PlatformSourceRegistry.LaunchFilesReceived -= _launchFilesHandler;
        await _viewModel.DisposeAsync();
        if (_diagnostics is not null)
        {
            await _diagnostics.DisposeAsync();
            _diagnostics = null;
        }
    }
}
