using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Views;

public sealed class SessionWorkspaceView : UserControl
{
    private readonly SessionTabViewModel _viewModel;
    private readonly bool _mobile = OperatingSystem.IsAndroid();
    private readonly TimelineControl _timeline = new();
    private readonly MinimapControl _minimap = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _searchStatus = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search message text or regex…" };
    private readonly CheckBox _regex = new() { Content = "Regex" };
    private readonly CheckBox _caseSensitive = new() { Content = "Case-sensitive" };
    private readonly ListBox _entries = new();
    private readonly ListBox _templates = new();
    private readonly ComboBox _order = new()
    {
        ItemsSource = new[] { "Chronological", "Source order" },
        SelectedIndex = 0,
        Width = 145,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly ComboBox _savedViews = new() { MinWidth = 130, PlaceholderText = "Saved views" };
    private readonly TextBox _viewName = new() { Width = 120, PlaceholderText = "View name" };
    private readonly TextBlock _summary = new()
    {
        Margin = new Thickness(0, 0, 0, 4),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _sessionInfo = new() { Margin = new Thickness(10, 4, 10, 10) };
    private string _sessionInfoText = string.Empty;

    // Read-only by nature and copyable, so the source view reads as output instead of an
    // empty input the accent focus border invited people to type into (§14.1).
    private readonly SelectableTextBlock _rawContext = new() { TextWrapping = TextWrapping.NoWrap };
    private Border? _rawContentBorder;
    private TextBlock? _rawPlaceholder;
    private Border? _rawEmptyCard;
    private Border? _rawCodeSurface;
    private Control? _rawEmptyState;
    private Control? _rawDataSurface;
    private TextBlock? _rawEmptyTitle;
    private Button? _rawChooseEntry;
    private TextBlock? _rawHeaderLabel;
    private TextBlock? _rawHeaderHint;
    private TextBlock? _rawSelectionHint;
    private TextBlock? _rawChevron;
    private ScrollViewer? _rawScroller;
    private Button? _rawPanToggle;
    private Button? _rawWrapToggle;
    private Button? _rawCopySelection;
    private Button? _rawPanLeft;
    private Button? _rawPanRight;
    private bool _rawExpanded;
    private bool _selectingTimelineEntry;
    private bool _timelineEntryPending;
    private long _selectedTimelineCellCount;
    private long _timelineSelectionGeneration;
    private readonly object _rawLoadSync = new();
    private CancellationTokenSource? _rawLoadCancellation;
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,Roboto Mono,monospace");
    private static readonly IBrush IncludeActive = new SolidColorBrush(Color.Parse("#1E6FA8"));
    private static readonly IBrush ExcludeActive = new SolidColorBrush(Color.Parse("#8A3B4A"));
    private readonly StackPanel _facets = new() { Spacing = 2, Margin = new Thickness(6) };
    private ScrollViewer? _facetScroll;
    private readonly Button _fitMatches = new() { Content = "Fit to matches", Margin = new Thickness(0, 0, 6, 0), IsVisible = false };
    private readonly Button _clearScope = new() { Content = "Clear cell", Margin = new Thickness(0, 0, 6, 0), IsVisible = false };
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rangeActions = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    private readonly TextBlock _rangeText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _follow = new() { Content = "Follow: off" };
    private readonly Button _newData = new() { Content = "↓ New data", IsVisible = false };
    private readonly Button _stopCapture = new() { Content = "Stop capture" };
    private readonly Button _loadMore = new() { Content = "+500", Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _loadAll = new() { Content = "Load all", Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock _entryLoadStatus = new()
    {
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Button _insightsToggle = new() { Content = "Hide insights", Margin = new Thickness(0, 0, 6, 0) };
    private readonly TextBlock _zoomReadout = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<LogLevel, ToggleButton> _levelChecks = [];
    private static readonly ControlTheme LevelToggleTheme = BuildLevelToggleTheme();

    // The column header and each row are separate grids; they only line up while both use
    // the exact same track sizes, so the layout lives in one place instead of two literals.
    private const string EntryColumns = "165,32,112,56,68,96,52,*";
    private readonly Grid _root = new();
    private readonly DockPanel _chipBar = new();
    private Grid? _columnHeader;
    private Grid? _analysisGrid;
    private Control? _insightsPane;
    private GridSplitter? _insightsSplitter;
    private Button? _mobileFilterButton;
    private Border? _mobileFilterPanel;
    private ScrollViewer? _mobileFilterScroll;
    private Grid? _mobileFilterShell;
    private Grid? _mobileFilterBody;
    private Control? _mobileQuerySection;
    private Control? _mobileSeveritySection;
    private Control? _mobileTimeSection;
    private TextBlock? _mobileFilterCount;
    private WrapPanel? _mobileQuickActions;
    private readonly Dictionary<MobileWorkspaceDisplayMode, Button> _mobileModeButtons = [];
    private Border? _minimapFrame;
    private TabControl? _mobileAnalysisTabs;
    private Grid? _entryHeader;
    private WrapPanel? _entryActions;
    private Button? _copyRaw;
    private Button? _templateInclude;
    private Button? _templateExclude;
    private Button? _templateCopy;
    private DockPanel? _statusBar;
    private MobileWorkspaceMode? _mobileLayoutMode;
    private readonly MobileWorkspaceState _mobileWorkspaceState = new();
    private bool _mobileFiltersOpen;
    private bool _rawWrapPreferenceSet;
    private bool _rawPanMode;
    private bool _rawWrapEnabled;
    private bool _insightsVisible = true;
    private CancellationTokenSource? _loadAllEntriesCancellation;
    private CancellationTokenSource? _searchDebounce;
    private FilterSpec? _renderedChipFilter;
    private StatisticsResult? _renderedFacets;
    private TimeRange? _selectedRange;
    private bool _updatingLevelChecks;
    private TimeZoneInfo? _sessionZone;
    private string? _sessionZoneId;

    public SessionWorkspaceView(SessionTabViewModel viewModel)
    {
        _viewModel = viewModel;
        Content = Build();
        _entries.ItemsSource = viewModel.Entries;
        _templates.ItemsSource = viewModel.Templates;
        WireInteractions();
        RefreshPresentation();
        SizeChanged += (_, eventArgs) => ApplyMobileLayout(eventArgs.NewSize);
        ActualThemeVariantChanged += (_, _) => ApplyThemeSurfaces();
        ApplyThemeSurfaces();
        DetachedFromVisualTree += (_, _) =>
        {
            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
            _searchDebounce = null;
            _loadAllEntriesCancellation?.Cancel();
            _loadAllEntriesCancellation?.Dispose();
            _loadAllEntriesCancellation = null;
            CancelRawContextLoad();
        };
    }

    /// <summary>
    /// The workspace is built in code, so theme-dependent surface colors are applied
    /// here instead of via styled resources. Dark and light are both first-class (§14.1).
    /// </summary>
    private void ApplyThemeSurfaces()
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        _root.Background = new SolidColorBrush(WorkspacePalette.Surface(dark));
        _chipBar.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
        _zoomReadout.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        if (_columnHeader is { } header)
        {
            header.Background = new SolidColorBrush(WorkspacePalette.SurfaceHeader(dark));
            foreach (var child in header.Children)
            {
                if (child is TextBlock label)
                {
                    label.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
                }
            }
        }

        var muted = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        if (_rawContentBorder is { } rawBorder)
        {
            rawBorder.Background = _mobile
                ? Brushes.Transparent
                : new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            rawBorder.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        if (_rawCodeSurface is { } codeSurface)
        {
            codeSurface.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            codeSurface.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            emptyCard.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            emptyCard.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        if (_mobileFilterPanel is { } filterPanel)
        {
            filterPanel.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            filterPanel.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        _rawContext.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        if (_mobile)
        {
            ApplyMobileModeButtonStyles();
            if (_mobileFilterButton is { } filterButton)
            {
                ApplyMobileChoiceAppearance(filterButton, _mobileFiltersOpen);
            }

            if (_rawPanToggle is { } panToggle)
            {
                ApplyMobileChoiceAppearance(panToggle, _rawPanMode);
            }

            if (_rawWrapToggle is { } wrapToggle)
            {
                ApplyMobileChoiceAppearance(wrapToggle, _rawWrapEnabled);
            }

            UpdateFollowButton();
        }
        foreach (var element in new[]
                 {
                     _rawPlaceholder,
                     _rawHeaderLabel,
                     _rawHeaderHint,
                     _rawSelectionHint,
                     _rawChevron,
                 })
        {
            if (element is { } textBlock)
            {
                textBlock.Foreground = muted;
            }
        }

        // The session pane paints its own label/value/warn brushes, so it is rebuilt with
        // the theme rather than left in the previous variant's colors.
        UpdateSessionInfo();
    }

    public event Action<TimeRange?>? ExportRequested;
    public event Func<Task>? StopRequested;

    public void ApplyDisplaySettings(
        string intensityScale,
        string normalization,
        double minimumUsPerPixel,
        bool pixelSnap,
        double minimumBarWidth) =>
        _timeline.SetDisplayOptions(intensityScale, normalization, minimumUsPerPixel, pixelSnap, minimumBarWidth);

    private Grid Build()
    {
        var root = _root;
        // Desktop rows: filters, chips, timeline, minimap, splitter, analysis, status.
        // The timeline/analysis boundary is user-resizable (§14.1 resizable split panes).
        root.RowDefinitions = new RowDefinitions(_mobile ? "Auto,Auto,2*,48,0,3*,Auto" : "Auto,Auto,5*,62,5,4*,Auto");

        if (_mobile)
        {
            _order.ItemsSource = new[] { "Time ↑", "Source" };
            _order.Width = 126;
            _clearScope.Content = "× Cell";
        }

        _search.Width = _mobile ? double.NaN : 310;
        var searchButton = new Button { Content = "Search" };
        searchButton.Click += async (_, _) => await RunUiActionAsync(ApplySearchAsync);
        _search.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Avalonia.Input.Key.Enter)
            {
                await RunUiActionAsync(ApplySearchAsync);
                eventArgs.Handled = true;
            }
        };
        _search.TextChanged += (_, _) => QueueDebouncedSearch();

        // Severity toggles carry their row's color, so the filter bar doubles as the
        // legend for the plot above it. A row of identical grey checkboxes labelled
        // F/E/W/I/D/V made the reader map letter to color from memory on every glance,
        // and left the one control group that acts on severity as the only place in the
        // workspace that did not show what a severity looks like (§14.1, §14.11).
        Panel levelGroup = _mobile
            ? new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 4,
                LineSpacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
            };
        AutomationProperties.SetName(levelGroup, "Severity filter");
        foreach (var level in LogLevels.DisplayOrder)
        {
            var toggle = new ToggleButton
            {
                Content = LevelPalette.Label(level),
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = level,
                Width = _mobile ? 48 : 28,
                Height = _mobile ? 48 : 26,
                Padding = new Thickness(0),
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                Theme = LevelToggleTheme,
            };
            ToolTip.SetTip(toggle, $"{level}: click to show or hide these entries");
            AutomationProperties.SetName(toggle, $"{level} level");
            toggle.IsCheckedChanged += (_, _) =>
            {
                ApplyLevelToggleColors(toggle, level);
                if (!_updatingLevelChecks)
                {
                    _ = _viewModel.SetLevelAsync(level, toggle.IsChecked == true);
                }
            };
            ApplyLevelToggleColors(toggle, level);
            _levelChecks[level] = toggle;
            levelGroup.Children.Add(toggle);
        }

        var zoomOut = new Button { Content = "−", Padding = new Thickness(9, 3) };
        ToolTip.SetTip(zoomOut, "Zoom out");
        zoomOut.Click += (_, _) => _timeline.ZoomAtCenter(1.8);
        var fit = new Button { Content = "Fit", Padding = new Thickness(9, 3) };
        ToolTip.SetTip(fit, "Fit the complete session (0)");
        fit.Click += (_, _) => _timeline.FitSession();
        var zoomIn = new Button { Content = "+", Padding = new Thickness(9, 3) };
        ToolTip.SetTip(zoomIn, "Zoom in");
        zoomIn.Click += (_, _) => _timeline.ZoomAtCenter(0.5);
        _follow.Click += (_, _) => _ = _viewModel.ToggleFollowAsync();
        _newData.Click += (_, _) => _ = _viewModel.ToggleFollowAsync();
        _stopCapture.Click += async (_, _) =>
        {
            if (StopRequested is { } handler)
            {
                await RunUiActionAsync(handler);
            }
        };

        Panel filters;
        if (_mobile)
        {
            foreach (var touchTarget in new Control[]
                     {
                         _search,
                         _regex,
                         _caseSensitive,
                         searchButton,
                         zoomOut,
                         fit,
                         zoomIn,
                         _follow,
                         _newData,
                         _stopCapture,
                     })
            {
                touchTarget.MinHeight = 48;
            }

            zoomOut.MinWidth = 48;
            fit.MinWidth = 56;
            zoomIn.MinWidth = 48;

            var queryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            queryRow.Children.Add(_search);
            Grid.SetColumn(searchButton, 1);
            queryRow.Children.Add(searchButton);

            var queryOptions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 8,
                LineSpacing = 4,
            };
            queryOptions.Children.Add(_regex);
            queryOptions.Children.Add(_caseSensitive);

            var zoomControls = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            zoomControls.Children.Add(zoomOut);
            zoomControls.Children.Add(fit);
            zoomControls.Children.Add(zoomIn);
            zoomControls.Children.Add(_zoomReadout);

            var querySection = _mobileQuerySection = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8),
                Children =
                {
                    MobileSectionLabel("QUERY"),
                    queryRow,
                    queryOptions,
                },
            };
            var severitySection = _mobileSeveritySection = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8),
                Children =
                {
                    MobileSectionLabel("SEVERITY"),
                    levelGroup,
                },
            };
            var timeSection = _mobileTimeSection = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8),
                Children =
                {
                    MobileSectionLabel("TIME LENS"),
                    zoomControls,
                },
            };
            var filterBody = _mobileFilterBody = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("*"),
                Children = { querySection, severitySection, timeSection },
            };
            Grid.SetRow(severitySection, 1);
            Grid.SetRow(timeSection, 2);
            _mobileFilterScroll = new ScrollViewer
            {
                Content = filterBody,
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            var filterCount = _mobileFilterCount = new TextBlock
            {
                Text = "No active filters",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.72,
            };
            var resetFilters = new Button
            {
                Content = "Reset",
                MinHeight = 48,
                MinWidth = 64,
            };
            resetFilters.Click += async (_, _) =>
            {
                _search.Text = string.Empty;
                _selectedRange = null;
                _rangeActions.IsVisible = false;
                await RunUiActionAsync(_viewModel.ClearFiltersAsync);
                UpdateLevelChecks();
            };
            var doneFilters = new Button
            {
                Content = "Done",
                MinHeight = 48,
                MinWidth = 64,
            };
            doneFilters.Click += (_, _) => SetMobileFiltersOpen(false);
            var filterFooter = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                Margin = new Thickness(8, 2, 8, 8),
                ColumnSpacing = 6,
                Children = { filterCount, resetFilters, doneFilters },
            };
            Grid.SetColumn(resetFilters, 1);
            Grid.SetColumn(doneFilters, 2);
            var filterPanelGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children = { _mobileFilterScroll, filterFooter },
            };
            Grid.SetRow(filterFooter, 1);
            _mobileFilterPanel = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#2C4361")),
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Margin = new Thickness(6, 2, 6, 0),
                Child = filterPanelGrid,
                IsVisible = false,
            };

            _mobileFilterButton = new Button
            {
                Content = "Filters",
                MinWidth = 76,
                MinHeight = 48,
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            _mobileFilterButton.Click += (_, _) => SetMobileFiltersOpen(!_mobileFiltersOpen);
            AutomationProperties.SetName(_mobileFilterButton, "Open search and timeline filters");

            var modeSelector = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                Width = 190,
                Height = 48,
            };
            void AddModeButton(string label, MobileWorkspaceDisplayMode mode, int column)
            {
                var button = new Button
                {
                    Content = label,
                    MinHeight = 48,
                    Padding = new Thickness(5, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    CornerRadius = column switch
                    {
                        0 => new CornerRadius(7, 0, 0, 7),
                        2 => new CornerRadius(0, 7, 7, 0),
                        _ => new CornerRadius(0),
                    },
                };
                button.Click += (_, _) => SetMobileDisplayMode(mode);
                AutomationProperties.SetName(button, $"Show {label.ToLowerInvariant()} workspace");
                _mobileModeButtons[mode] = button;
                Grid.SetColumn(button, column);
                modeSelector.Children.Add(button);
            }
            AddModeButton("Plot", MobileWorkspaceDisplayMode.Plot, 0);
            AddModeButton("Split", MobileWorkspaceDisplayMode.Split, 1);
            AddModeButton("Details", MobileWorkspaceDisplayMode.Details, 2);

            var quickActions = _mobileQuickActions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6,
                Margin = new Thickness(6, 3, 6, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _mobileFilterButton,
                    modeSelector,
                    _follow,
                    _newData,
                    _stopCapture,
                },
            };
            AutomationProperties.SetName(quickActions, "Filters, workspace mode, and capture controls");

            var filterShell = _mobileFilterShell = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("*"),
            };
            filterShell.Children.Add(quickActions);
            Grid.SetRow(_mobileFilterPanel, 1);
            filterShell.Children.Add(_mobileFilterPanel);
            filters = filterShell;
        }
        else
        {
            var desktopFilters = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 7,
                LineSpacing = 6,
                Margin = new Thickness(10, 7),
            };
            desktopFilters.Children.Add(_search);
            desktopFilters.Children.Add(_regex);
            desktopFilters.Children.Add(_caseSensitive);
            desktopFilters.Children.Add(searchButton);
            desktopFilters.Children.Add(levelGroup);
            desktopFilters.Children.Add(zoomOut);
            desktopFilters.Children.Add(fit);
            desktopFilters.Children.Add(zoomIn);
            desktopFilters.Children.Add(_zoomReadout);
            desktopFilters.Children.Add(_follow);
            desktopFilters.Children.Add(_newData);
            desktopFilters.Children.Add(_stopCapture);
            filters = desktopFilters;
        }

        AutomationProperties.SetName(filters, "Session filters");
        Grid.SetRow(filters, 0);
        root.Children.Add(filters);

        var chipBar = _chipBar;
        chipBar.Margin = new Thickness(10, 0, 10, 5);
        chipBar.LastChildFill = true;
        var clear = new Button { Content = "Clear all", Margin = new Thickness(6, 0, 0, 0) };
        clear.Click += async (_, _) =>
        {
            _search.Text = string.Empty;
            _selectedRange = null;
            _rangeActions.IsVisible = false;
            await _viewModel.ClearFiltersAsync();
            UpdateLevelChecks();
        };
        DockPanel.SetDock(clear, Dock.Right);
        chipBar.Children.Add(clear);
        _rangeActions.Children.Add(_rangeText);
        var zoomRange = new Button { Content = "Zoom range" };
        zoomRange.Click += (_, _) =>
        {
            if (_selectedRange is { } range)
            {
                _ = _viewModel.SetViewportAsync(range);
            }
        };
        _rangeActions.Children.Add(zoomRange);
        var filterRange = new Button { Content = "Filter range" };
        filterRange.Click += (_, _) => _ = _viewModel.SetTimeRangeFilterAsync(_selectedRange);
        _rangeActions.Children.Add(filterRange);
        var exportRange = new Button { Content = "Export range" };
        exportRange.Click += (_, _) => ExportRequested?.Invoke(_selectedRange);
        _rangeActions.Children.Add(exportRange);
        _chips.Children.Add(_rangeActions);
        chipBar.Children.Add(_chips);
        Grid.SetRow(chipBar, 1);
        root.Children.Add(chipBar);

        Grid.SetRow(_timeline, 2);
        root.Children.Add(_timeline);

        _minimapFrame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#2C4361")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(76, 4, 12, 4),
            Child = _minimap,
        };
        Grid.SetRow(_minimapFrame, 3);
        root.Children.Add(_minimapFrame);

        if (!_mobile)
        {
            var rowSplitter = new GridSplitter
            {
                Height = 5,
                ResizeDirection = GridResizeDirection.Rows,
                Background = new SolidColorBrush(Color.Parse("#304F7199")),
                Margin = new Thickness(10, 0),
            };
            Grid.SetRow(rowSplitter, 4);
            root.Children.Add(rowSplitter);
        }

        var analysis = _analysisGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(_mobile ? "*" : "3*,6,2*"),
            Margin = new Thickness(10, 5),
        };
        ConfigureEntryList();
        var entryPanel = new Grid
        {
            RowDefinitions = new RowDefinitions(_mobile ? "Auto,*" : "Auto,Auto,*,Auto"),
        };
        var entryHeader = _entryHeader = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
        };
        Panel entryActions = _mobile
            ? _entryActions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6,
            }
            : new DockPanel { LastChildFill = false };
        _order.SelectionChanged += (_, _) => _ = _viewModel.SetEntryOrderAsync(
            _order.SelectedIndex == 1 ? EntryOrder.SourceSequence : EntryOrder.Chronological);
        DockPanel.SetDock(_order, Dock.Right);
        entryActions.Children.Add(_order);
        ToolTip.SetTip(_loadMore, $"Load the next {SessionTabViewModel.EntryPageSize:N0} matching rows");
        AutomationProperties.SetName(_loadMore, $"Load next {SessionTabViewModel.EntryPageSize:N0} matching rows");
        _loadMore.Click += async (_, _) => await RunUiActionAsync(() => _viewModel.LoadNextEntryPageAsync());
        DockPanel.SetDock(_loadMore, Dock.Right);
        if (!_mobile)
        {
            _loadAll.Click += async (_, _) => await ToggleLoadAllEntriesAsync();
            DockPanel.SetDock(_loadAll, Dock.Right);
            entryActions.Children.Add(_loadAll);
        }

        entryActions.Children.Add(_loadMore);
        if (!_mobile)
        {
            DockPanel.SetDock(_entryLoadStatus, Dock.Right);
            entryActions.Children.Add(_entryLoadStatus);
        }

        UpdateEntryLoadControls();
        var copyRaw = _copyRaw = new Button
        {
            Content = "Copy raw",
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(copyRaw, "Copy the raw text of the selected entries");
        copyRaw.Click += async (_, _) => await RunUiActionAsync(CopySelectedRawAsync);
        DockPanel.SetDock(copyRaw, Dock.Right);
        entryActions.Children.Add(copyRaw);
        if (_mobile)
        {
            foreach (var touchTarget in new Control[]
                     {
                         _order,
                         _loadMore,
                         copyRaw,
                         _fitMatches,
                         _clearScope,
                     })
            {
                touchTarget.MinHeight = 48;
            }
        }

        // Filters are session-wide but this table is not: when the filter matches
        // nothing inside the current view, offer the one action that reconciles them
        // instead of leaving an empty table next to a facet promising thousands of hits.
        ToolTip.SetTip(_fitMatches, "Move the timeline to the range that contains the matching entries");
        _fitMatches.Click += (_, _) => FitToMatches();
        DockPanel.SetDock(_fitMatches, Dock.Right);
        entryActions.Children.Add(_fitMatches);
        ToolTip.SetTip(_clearScope, "Stop listing one selected cell and follow the visible time range again");
        _clearScope.Click += async (_, _) => await RunUiActionAsync(() => _viewModel.ClearDetailScopeAsync());
        DockPanel.SetDock(_clearScope, Dock.Right);
        entryActions.Children.Add(_clearScope);
        if (!_mobile)
        {
            _insightsToggle.Click += (_, _) => ToggleInsights();
            DockPanel.SetDock(_insightsToggle, Dock.Right);
            entryActions.Children.Add(_insightsToggle);
        }

        entryHeader.Children.Add(_summary);
        Grid.SetRow(entryActions, 1);
        entryHeader.Children.Add(entryActions);
        Grid.SetRow(entryHeader, 0);
        entryPanel.Children.Add(entryHeader);
        if (!_mobile)
        {
            _columnHeader = EntryColumnHeader();
            Grid.SetRow(_columnHeader, 1);
            entryPanel.Children.Add(_columnHeader);
        }

        Grid.SetRow(_entries, _mobile ? 1 : 2);
        entryPanel.Children.Add(_entries);
        var templatePane = BuildTemplatePane();
        var rawPane = BuildRawContextPane();
        if (_mobile)
        {
            TabItem MobileDetailTab(string header, Control content) => new()
            {
                Header = header,
                Content = content,
                MinWidth = 92,
                MinHeight = 48,
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            _mobileAnalysisTabs = new TabControl
            {
                FontSize = 14,
                Items =
                {
                    MobileDetailTab("Entries", entryPanel),
                    MobileDetailTab("Insights", templatePane),
                    MobileDetailTab("Source", rawPane),
                },
            };
            AutomationProperties.SetName(_mobileAnalysisTabs, "Session detail views");
            analysis.Children.Add(_mobileAnalysisTabs);
        }
        else
        {
            Grid.SetRow(rawPane, 3);
            entryPanel.Children.Add(rawPane);
            analysis.Children.Add(entryPanel);
            var splitter = _insightsSplitter = new GridSplitter
            {
                Width = 6,
                ResizeDirection = GridResizeDirection.Columns,
                Background = new SolidColorBrush(Color.Parse("#304F7199")),
            };
            Grid.SetColumn(splitter, 1);
            analysis.Children.Add(splitter);
            _insightsPane = templatePane;
            Grid.SetColumn(templatePane, 2);
            analysis.Children.Add(templatePane);
        }

        Grid.SetRow(analysis, 5);
        root.Children.Add(analysis);

        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _searchStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        var statusBar = _statusBar = new DockPanel
        {
            Margin = new Thickness(8, 4, 8, _mobile ? 12 : 4),
            ClipToBounds = true,
        };
        DockPanel.SetDock(_searchStatus, Dock.Right);
        statusBar.Children.Add(_searchStatus);
        statusBar.Children.Add(_status);
        Grid.SetRow(statusBar, 6);
        root.Children.Add(statusBar);
        return root;
    }

    private static TextBlock MobileSectionLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Opacity = 0.62,
            Margin = new Thickness(1, 4, 0, 0),
        };

    private void SetMobileDisplayMode(MobileWorkspaceDisplayMode mode)
    {
        _mobileWorkspaceState.Select(mode);
        if (_mobileFiltersOpen)
        {
            _mobileFiltersOpen = false;
            if (_mobileFilterPanel is { } panel)
            {
                panel.IsVisible = false;
            }
        }

        ApplyMobileLayout(Bounds.Size);
    }

    private void SetMobileFiltersOpen(bool open)
    {
        _mobileFiltersOpen = open;
        if (_mobileFilterPanel is { } panel)
        {
            panel.IsVisible = open;
        }

        ApplyMobileLayout(Bounds.Size);
    }

    private void ApplyMobileModeButtonStyles()
    {
        foreach (var (mode, button) in _mobileModeButtons)
        {
            var selected = mode == _mobileWorkspaceState.DisplayMode;
            ApplyMobileChoiceAppearance(button, selected);
            button.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetHelpText(button, selected ? "Current workspace mode" : "Switch workspace mode");
        }
    }

    private void ApplyMobileChoiceAppearance(TemplatedControl control, bool selected)
    {
        var dark = ActualThemeVariant != ThemeVariant.Light;
        var accent = WorkspacePalette.Accent(dark);
        control.Background = selected
            ? new SolidColorBrush(Color.FromArgb(dark ? (byte)44 : (byte)28, accent.R, accent.G, accent.B))
            : new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
        control.BorderBrush = new SolidColorBrush(selected ? accent : WorkspacePalette.BorderLine(dark));
        control.BorderThickness = new Thickness(1);
        control.Foreground = new SolidColorBrush(
            selected ? WorkspacePalette.TextPrimary(dark) : WorkspacePalette.TextMuted(dark));
    }

    private void ApplyMobileLayout(Size size)
    {
        if (!_mobile || _root.RowDefinitions.Count < 7)
        {
            return;
        }

        var layout = MobileWorkspaceLayout.ForSize(size.Width, size.Height);
        var wideComposition = layout.UsesWideMobileComposition;
        _mobileWorkspaceState.ApplyLayout(layout);

        if (_mobileLayoutMode != layout.Mode)
        {
            _mobileLayoutMode = layout.Mode;
            // A filter workspace is deliberately transient; the visualization mode is not.
            // This keeps Plot/Split/Details stable across rotation while preventing a tall
            // portrait drawer from consuming a newly short landscape viewport.
            _mobileFiltersOpen = false;
            if (_mobileFilterPanel is { } panel)
            {
                panel.IsVisible = false;
            }

            if (!_rawWrapPreferenceSet && _rawWrapToggle is { } wrapToggle)
            {
                SetRawWrap(!wideComposition);
            }
        }

        var filtersOpen = _mobileFiltersOpen;
        var timelineVisible = !filtersOpen &&
                              _mobileWorkspaceState.DisplayMode is not MobileWorkspaceDisplayMode.Details;
        var analysisVisible = !filtersOpen &&
                              _mobileWorkspaceState.DisplayMode is not MobileWorkspaceDisplayMode.Plot;
        if (wideComposition)
        {
            _root.RowDefinitions[2].Height = filtersOpen
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            _root.RowDefinitions[3].Height = new GridLength(0);
            _root.RowDefinitions[5].Height = new GridLength(0);
        }
        else
        {
            _root.RowDefinitions[2].Height = timelineVisible
                ? new GridLength(
                    _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Plot ? 1 : layout.TimelineWeight,
                    GridUnitType.Star)
                : new GridLength(0);
            _root.RowDefinitions[3].Height = timelineVisible && layout.MinimapHeight > 0
                ? new GridLength(layout.MinimapHeight, GridUnitType.Pixel)
                : new GridLength(0);
            _root.RowDefinitions[5].Height = analysisVisible
                ? new GridLength(
                    _mobileWorkspaceState.DisplayMode == MobileWorkspaceDisplayMode.Details ? 1 : layout.AnalysisWeight,
                    GridUnitType.Star)
                : new GridLength(0);
        }

        ConfigureWideMobileComposition(wideComposition, timelineVisible, analysisVisible, size.Width);
        UpdateSummaryText();
        _analysisGrid!.IsVisible = analysisVisible;
        UpdateChipBarVisibility();
        ApplyMobileModeButtonStyles();

        _timeline.IsVisible = timelineVisible;
        _timeline.MinHeight = timelineVisible
            ? layout.Mode switch
            {
                MobileWorkspaceMode.CompactHeight => 132,
                MobileWorkspaceMode.CompactPortrait => 180,
                _ => 180,
            }
            : 0;

        if (_minimapFrame is { } minimap)
        {
            minimap.IsVisible = timelineVisible && layout.MinimapHeight > 0;
        }

        if (_mobileFilterScroll is { } filterScroll)
        {
            var maximumPanelHeight = Math.Max(160, size.Height - 58);
            filterScroll.MaxHeight = Math.Max(96, Math.Min(layout.FilterMaximumHeight, maximumPanelHeight - 58));
            if (_mobileFilterPanel is { } filterPanel)
            {
                filterPanel.MaxHeight = maximumPanelHeight;
            }
        }

        if (_mobileFilterButton is { } filterButton)
        {
            ApplyMobileChoiceAppearance(filterButton, filtersOpen);
            AutomationProperties.SetName(filterButton, filtersOpen ? "Close filters" : "Open search and timeline filters");
        }
    }

    private void ConfigureWideMobileComposition(
        bool enabled,
        bool timelineVisible,
        bool analysisVisible,
        double availableWidth)
    {
        var splitTimeline = enabled && timelineVisible && analysisVisible;
        _root.ColumnDefinitions = new ColumnDefinitions(splitTimeline ? "21*,29*" : "*");

        if (_mobileFilterShell is { } topStrip)
        {
            Grid.SetColumn(topStrip, 0);
            Grid.SetColumnSpan(topStrip, splitTimeline ? 2 : 1);
        }

        Grid.SetColumn(_chipBar, 0);
        Grid.SetColumnSpan(_chipBar, splitTimeline ? 2 : 1);
        if (_statusBar is { } workspaceStatus)
        {
            Grid.SetColumn(workspaceStatus, 0);
            Grid.SetColumnSpan(workspaceStatus, splitTimeline ? 2 : 1);
        }

        Grid.SetRow(_timeline, 2);
        Grid.SetRowSpan(_timeline, enabled ? 4 : 1);
        Grid.SetColumn(_timeline, 0);
        Grid.SetColumnSpan(_timeline, 1);

        if (_analysisGrid is { } analysis)
        {
            Grid.SetRow(analysis, enabled ? 2 : 5);
            Grid.SetRowSpan(analysis, enabled ? 4 : 1);
            Grid.SetColumn(analysis, splitTimeline ? 1 : 0);
            Grid.SetColumnSpan(analysis, 1);
        }

        if (_mobileFilterShell is { } filterShell &&
            _mobileQuickActions is { } quickActions)
        {
            filterShell.RowDefinitions = new RowDefinitions("Auto,Auto");
            filterShell.ColumnDefinitions = new ColumnDefinitions("*");
            Grid.SetRow(quickActions, 0);
            Grid.SetColumn(quickActions, 0);
            quickActions.Margin = new Thickness(6, 3);
            quickActions.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (_mobileFilterBody is { } filterBody &&
            _mobileQuerySection is { } query &&
            _mobileSeveritySection is { } severity &&
            _mobileTimeSection is { } time)
        {
            filterBody.ColumnDefinitions = new ColumnDefinitions(enabled ? "*,*" : "*");
            filterBody.RowDefinitions = new RowDefinitions(enabled ? "Auto,Auto" : "Auto,Auto,Auto");
            Grid.SetColumn(query, 0);
            Grid.SetRow(query, 0);
            Grid.SetRowSpan(query, enabled ? 2 : 1);
            Grid.SetColumn(severity, enabled ? 1 : 0);
            Grid.SetRow(severity, enabled ? 0 : 1);
            Grid.SetRowSpan(severity, 1);
            Grid.SetColumn(time, enabled ? 1 : 0);
            Grid.SetRow(time, enabled ? 1 : 2);
            Grid.SetRowSpan(time, 1);
        }

        if (_mobileAnalysisTabs is { } tabs)
        {
            var placement = enabled ? Dock.Left : Dock.Top;
            if (tabs.TabStripPlacement != placement)
            {
                // Changing placement reapplies Avalonia's TabControl template. Preserve the
                // active inspector so rotating the phone does not silently jump Source back
                // to Entries in the middle of a select/copy/pan workflow.
                var selectedIndex = tabs.SelectedIndex;
                tabs.TabStripPlacement = placement;
                Dispatcher.UIThread.Post(() => tabs.SelectedIndex = selectedIndex);
            }

            foreach (var item in tabs.Items.OfType<TabItem>())
            {
                item.MinWidth = enabled ? 78 : 92;
                item.Width = enabled ? 78 : double.NaN;
                item.FontSize = enabled ? 12.5 : 14;
                item.Padding = enabled ? new Thickness(5, 0) : new Thickness(10, 0);
            }
        }

        if (_entryHeader is { } entryHeader && _entryActions is { } entryActions)
        {
            var analysisWidth = splitTimeline ? availableWidth * 0.58 - 78 : availableWidth - (enabled ? 78 : 0);
            var sideBySide = enabled && analysisWidth >= 540;
            entryHeader.RowDefinitions = new RowDefinitions(sideBySide ? "Auto" : "Auto,Auto");
            entryHeader.ColumnDefinitions = new ColumnDefinitions(sideBySide ? "*,Auto" : "*");
            Grid.SetRow(_summary, 0);
            Grid.SetColumn(_summary, 0);
            Grid.SetRow(entryActions, sideBySide ? 0 : 1);
            Grid.SetColumn(entryActions, sideBySide ? 1 : 0);
            entryActions.HorizontalAlignment = sideBySide
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch;
            entryActions.MaxWidth = sideBySide
                ? Math.Clamp(analysisWidth * 0.58, 280, 430)
                : double.PositiveInfinity;
            _summary.TextWrapping = sideBySide ? TextWrapping.NoWrap : TextWrapping.Wrap;
            _summary.TextTrimming = sideBySide ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            _summary.Margin = sideBySide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 4);
        }

        _order.Width = enabled ? 112 : 126;
        _loadMore.Content = splitTimeline ? "More" : enabled ? "Load +500" : "Load next 500";
        _loadMore.Margin = enabled ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        if (_copyRaw is { } copyRaw)
        {
            copyRaw.Content = enabled ? "Copy" : "Copy raw";
            copyRaw.Margin = enabled ? new Thickness(0) : new Thickness(0, 0, 6, 0);
        }

        _timeline.Margin = splitTimeline ? new Thickness(6, 2, 3, 2) : new Thickness(0);
        _analysisGrid!.Margin = enabled
            ? splitTimeline ? new Thickness(3, 2, 6, 2) : new Thickness(6, 2)
            : new Thickness(8, 4);
        _chipBar.Margin = enabled ? new Thickness(6, 0, 6, 2) : new Thickness(10, 0, 10, 5);
        if (_statusBar is { } statusBar)
        {
            statusBar.Margin = enabled ? new Thickness(6, 2, 6, 4) : new Thickness(8, 4, 8, 12);
        }
    }

    private void ToggleInsights()
    {
        if (_mobile ||
            _analysisGrid is null ||
            _insightsPane is null ||
            _insightsSplitter is null)
        {
            return;
        }

        _insightsVisible = !_insightsVisible;
        _insightsPane.IsVisible = _insightsVisible;
        _insightsSplitter.IsVisible = _insightsVisible;
        _analysisGrid.ColumnDefinitions[1].Width = new GridLength(_insightsVisible ? 6 : 0, GridUnitType.Pixel);
        _analysisGrid.ColumnDefinitions[2].Width = _insightsVisible
            ? new GridLength(2, GridUnitType.Star)
            : new GridLength(0, GridUnitType.Pixel);
        _insightsToggle.Content = _insightsVisible ? "Hide insights" : "Show insights";
    }

    /// <summary>
    /// The source inspector: a labelled, collapsible panel rather than the old full-height
    /// read-only text box. Collapsed it costs one header row, so an unused inspector no
    /// longer holds a screenful of empty space under the table; selecting a row opens it,
    /// and it sizes to its content up to a modest cap (§14.1 density, §14.7).
    /// </summary>
    private Control BuildRawContextPane()
    {
        _rawContext.FontFamily = MonoFont;
        _rawContext.FontSize = 12;
        var scroller = _rawScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = _mobile ? double.PositiveInfinity : 150,
            Content = _rawContext,
        };
        _rawPlaceholder = new TextBlock
        {
            Text = "Select a row to load the exact source bytes behind it, with a few lines on each side.",
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new Grid();
        if (_mobile)
        {
            var panToggle = _rawPanToggle = new Button
            {
                MinHeight = 48,
                Width = 76,
                Padding = new Thickness(6, 0),
            };
            panToggle.Click += (_, _) => SetRawPanMode(!_rawPanMode);
            ToolTip.SetTip(panToggle, "Select mode: drag selects text. Pan mode: drag scrolls in both directions.");

            var wrapToggle = _rawWrapToggle = new Button
            {
                Content = "Wrap",
                MinHeight = 48,
                Width = 64,
                Padding = new Thickness(6, 0),
            };
            wrapToggle.Click += (_, _) =>
            {
                _rawWrapPreferenceSet = true;
                SetRawWrap(!_rawWrapEnabled);
            };
            ToolTip.SetTip(wrapToggle, "Wrap long source lines to the available width.");
            AutomationProperties.SetName(wrapToggle, "Wrap long source lines");

            var panLeft = _rawPanLeft = new Button
            {
                Content = "←",
                MinHeight = 48,
                Width = 48,
            };
            panLeft.Click += (_, _) => PanRawContext(-1);
            ToolTip.SetTip(panLeft, "Pan source left by one page");
            AutomationProperties.SetName(panLeft, "Pan source left by one page");

            var panRight = _rawPanRight = new Button
            {
                Content = "→",
                MinHeight = 48,
                Width = 48,
            };
            panRight.Click += (_, _) => PanRawContext(1);
            ToolTip.SetTip(panRight, "Pan source right by one page");
            AutomationProperties.SetName(panRight, "Pan source right by one page");

            var copySelection = _rawCopySelection = new Button
            {
                Content = "Copy",
                MinHeight = 48,
                Width = 64,
                Padding = new Thickness(6, 0),
                IsEnabled = false,
            };
            copySelection.Click += (_, _) => _rawContext.Copy();
            ToolTip.SetTip(copySelection, "Copy selected source text");
            AutomationProperties.SetName(copySelection, "Copy selected source text");

            var sourceTools = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 4,
                LineSpacing = 6,
                Margin = new Thickness(0, 0, 0, 7),
                Children =
                {
                    panToggle,
                    wrapToggle,
                    panLeft,
                    panRight,
                    copySelection,
                },
            };
            AutomationProperties.SetName(sourceTools, "Source navigation and selection controls");

            _rawSelectionHint = new TextBlock
            {
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 7),
            };
            var codeGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                Children =
                {
                    _rawSelectionHint,
                    sourceTools,
                    scroller,
                },
            };
            Grid.SetRow(sourceTools, 1);
            Grid.SetRow(scroller, 2);
            scroller.ScrollChanged += (_, _) => UpdateRawNavigationButtons();
            _rawContext.PointerReleased += (_, _) => Dispatcher.UIThread.Post(CompleteRawTextSelection);
            SetRawPanMode(false);
            SetRawWrap(false);
            _rawCodeSurface = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = codeGrid,
                IsVisible = false,
            };
            _rawDataSurface = _rawCodeSurface;
            content.Children.Add(_rawCodeSurface);

            var chooseEntry = _rawChooseEntry = new Button
            {
                Content = "Choose an entry",
                HorizontalAlignment = HorizontalAlignment.Center,
                MinHeight = 48,
            };
            chooseEntry.Click += (_, _) =>
            {
                if (_mobileAnalysisTabs is { } tabs)
                {
                    tabs.SelectedIndex = 0;
                }
            };
            AutomationProperties.SetName(chooseEntry, "Open Entries to choose a source row");

            _rawPlaceholder.Text =
                "Choose a log entry to inspect its exact source bytes and the surrounding lines.";
            _rawPlaceholder.FontStyle = FontStyle.Normal;
            _rawPlaceholder.TextAlignment = TextAlignment.Center;
            _rawPlaceholder.MaxWidth = 320;
            _rawEmptyTitle = new TextBlock
            {
                Text = "No source selected",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var emptyPanel = new StackPanel
            {
                Spacing = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "{  }",
                        FontFamily = MonoFont,
                        FontSize = 24,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    _rawEmptyTitle,
                    _rawPlaceholder,
                    chooseEntry,
                },
            };
            _rawEmptyCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18),
                Margin = new Thickness(14),
                MaxWidth = 390,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = emptyPanel,
            };
            _rawEmptyState = _rawEmptyCard;
            AutomationProperties.SetName(_rawEmptyCard, "No source selected");
            content.Children.Add(_rawEmptyCard);
        }
        else
        {
            _rawDataSurface = scroller;
            _rawEmptyState = _rawPlaceholder;
            content.Children.Add(scroller);
            content.Children.Add(_rawPlaceholder);
        }
        _rawContentBorder = new Border
        {
            BorderThickness = new Thickness(_mobile ? 0 : 1),
            CornerRadius = new CornerRadius(4),
            Padding = _mobile ? new Thickness(0) : new Thickness(8, 6),
            Child = content,
        };

        if (_mobile)
        {
            // Its own tab, so it is always open and fills the available height.
            _rawContentBorder.Margin = new Thickness(6);
            _rawExpanded = true;
            return _rawContentBorder;
        }

        _rawContentBorder.Margin = new Thickness(0, 4, 0, 0);
        _rawChevron = new TextBlock { Text = "▸", Width = 14, VerticalAlignment = VerticalAlignment.Center };
        _rawHeaderLabel = new TextBlock
        {
            Text = "SOURCE CONTEXT",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _rawHeaderHint = new TextBlock
        {
            Text = "raw bytes behind the selected row",
            FontSize = 10,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _rawChevron, _rawHeaderLabel, _rawHeaderHint },
            },
        };
        ToolTip.SetTip(header, "Show or hide the exact source bytes behind the selected row.");
        AutomationProperties.SetName(header, "Toggle source context");
        header.Click += (_, _) => SetRawExpanded(!_rawExpanded);

        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        pane.Children.Add(header);
        Grid.SetRow(_rawContentBorder, 1);
        pane.Children.Add(_rawContentBorder);
        SetRawExpanded(false);
        return pane;
    }

    private void SetRawExpanded(bool expanded)
    {
        _rawExpanded = expanded;
        if (_rawContentBorder is { } border)
        {
            border.IsVisible = expanded;
        }

        if (_rawChevron is { } chevron)
        {
            chevron.Text = expanded ? "▾" : "▸";
        }
    }

    private Control BuildTemplatePane()
    {
        // Without this the list inherits Fluent's touch-sized rows and only three or four
        // templates fit the pane; the same compact container the entry table uses shows a
        // useful ranking instead.
        _templates.Styles.Add(CompactItemStyle(_mobile ? 64 : 22));
        _templates.ItemTemplate = new FuncDataTemplate<TemplateSummary>((template, _) =>
        {
            if (template is null)
            {
                return new Grid();
            }

            // The metric gutter is fixed rather than Auto: Fluent's desktop ProgressBar has
            // a generous theme MinWidth which otherwise steals a surprising share of the
            // inspector from the canonical message. Exact counts remain in the tooltip and
            // accessibility name while the visible metric stays compact (§14.11).
            var metricTrackWidth = _mobile ? 40d : 44d;
            var metricWidth = _mobile ? 34d : 38d;
            var metricGap = 6d;
            var row = new Grid
            {
                Margin = new Thickness(2, 1),
                ColumnDefinitions = new ColumnDefinitions($"{metricTrackWidth},*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            var exactCount = template.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            var count = new TextBlock
            {
                Text = FormatTemplateCount(template.Count),
                FontFamily = MonoFont,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                Width = metricWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, metricGap, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            ToolTip.SetTip(count, $"{exactCount} matching entries");
            AutomationProperties.SetName(count, $"{exactCount} matching entries");
            row.Children.Add(count);
            var totalMatching = Math.Max(template.Count, _viewModel.Statistics?.TotalMatching ?? template.Count);
            var prevalence = new ProgressBar
            {
                Minimum = 0,
                Maximum = totalMatching,
                Value = template.Count,
                Width = metricWidth,
                MinWidth = 0,
                Height = 3,
                Margin = new Thickness(0, 4, metricGap, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(WorkspacePalette.Accent(
                    ActualThemeVariant != ThemeVariant.Light)),
            };
            ToolTip.SetTip(
                prevalence,
                $"{exactCount} entries · {template.Count / (double)Math.Max(1, totalMatching):P1} of current matches");
            Grid.SetRow(prevalence, 1);
            row.Children.Add(prevalence);
            var canonical = new TextBlock
            {
                Text = template.CanonicalText,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = _mobile ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MaxLines = _mobile ? 2 : 1,
            };
            Grid.SetColumn(canonical, 1);
            row.Children.Add(canonical);
            var span = new TextBlock
            {
                Text = $"{FormatInstant(template.First)} — {FormatInstant(template.Last)}",
                FontFamily = MonoFont,
                FontSize = 10,
                Opacity = 0.6,
            };
            Grid.SetColumn(span, 1);
            Grid.SetRow(span, 1);
            row.Children.Add(span);
            return row;
        });

        // A WrapPanel so the three actions fold onto a second line rather than clipping in
        // the narrow insights column.
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 6,
            LineSpacing = 6,
            Margin = new Thickness(4),
        };
        var include = _templateInclude = new Button
        {
            Content = _mobile ? "Filter" : "Filter to template",
            IsEnabled = false,
        };
        ToolTip.SetTip(include, "Show only entries matching the selected template");
        AutomationProperties.SetName(include, "Filter to selected template");
        include.Click += (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template)
            {
                _ = _viewModel.IncludeTemplateAsync(template.TemplateId);
            }
        };
        actions.Children.Add(include);
        var exclude = _templateExclude = new Button
        {
            Content = _mobile ? "Mute" : "Mute template",
            IsEnabled = false,
        };
        ToolTip.SetTip(exclude, "Hide entries matching the selected template");
        AutomationProperties.SetName(exclude, "Mute selected template");
        exclude.Click += (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template)
            {
                _ = _viewModel.ExcludeTemplateAsync(template.TemplateId);
            }
        };
        actions.Children.Add(exclude);
        var copy = _templateCopy = new Button
        {
            Content = _mobile ? "Copy" : "Copy template",
            IsEnabled = false,
        };
        ToolTip.SetTip(copy, "Copy the selected template text");
        AutomationProperties.SetName(copy, "Copy selected template");
        copy.Click += async (_, _) =>
        {
            if (_templates.SelectedItem is TemplateSummary template &&
                TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(template.CanonicalText);
            }
        };
        actions.Children.Add(copy);
        EnsureMobileTouch(include, exclude, copy);
        _templates.SelectionChanged += (_, _) =>
        {
            var hasSelection = _templates.SelectedItem is TemplateSummary;
            include.IsEnabled = hasSelection;
            exclude.IsEnabled = hasSelection;
            copy.IsEnabled = hasSelection;
        };

        var templateGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        templateGrid.Children.Add(actions);
        Grid.SetRow(_templates, 1);
        templateGrid.Children.Add(_templates);

        var viewsPane = BuildViewsPane();
        Control viewsDestination = viewsPane;
        if (_mobile)
        {
            viewsDestination = new ScrollViewer
            {
                Content = viewsPane,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            AutomationProperties.SetName(viewsDestination, "Saved views");
        }

        var panes = new Control[]
        {
            templateGrid,
            BuildFacetPane(),
            viewsDestination,
            BuildSessionPane(),
        };
        if (_mobile)
        {
            var destination = new ComboBox
            {
                ItemsSource = new[] { "Templates", "Facets", "Saved views", "Session info" },
                SelectedIndex = 0,
                MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(destination, "Insights destination");
            var host = new ContentControl { Content = panes[0] };
            destination.SelectionChanged += (_, _) =>
            {
                if (destination.SelectedIndex is >= 0 and < 4)
                {
                    host.Content = panes[destination.SelectedIndex];
                }
            };
            var mobilePane = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Margin = new Thickness(6, 2, 6, 4),
            };
            mobilePane.Children.Add(destination);
            Grid.SetRow(host, 1);
            mobilePane.Children.Add(host);
            return mobilePane;
        }

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "Templates", Content = panes[0] },
                new TabItem { Header = "Facets", Content = panes[1] },
                new TabItem { Header = "Views", Content = panes[2] },
                new TabItem { Header = "Session", Content = panes[3] },
            },
        };
    }

    internal static string FormatTemplateCount(long count)
    {
        if (count < 1_000)
        {
            return count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        }

        var scaled = (double)count;
        var unitIndex = 0;
        string[] units = ["", "k", "M", "B", "T"];
        while (scaled >= 999.5 && unitIndex < units.Length - 1)
        {
            scaled /= 1_000;
            unitIndex++;
        }

        var format = scaled switch
        {
            >= 100 => "0",
            >= 10 => "0.#",
            _ => "0.##",
        };
        return scaled.ToString(format, System.Globalization.CultureInfo.CurrentCulture) + units[unitIndex];
    }

    /// <summary>
    /// Saved views, split into a clearly labelled "apply an existing view" half — now with a
    /// Delete so the list is manageable — and a "save the current view" half, instead of a
    /// bare stack of controls whose two jobs were indistinguishable (§14.11).
    /// </summary>
    private StackPanel BuildViewsPane()
    {
        var views = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        _savedViews.ItemsSource = _viewModel.SavedViews;
        _savedViews.HorizontalAlignment = HorizontalAlignment.Stretch;
        views.Children.Add(SectionLabel("Saved views"));
        views.Children.Add(_savedViews);
        var savedButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var apply = new Button { Content = "Apply" };
        apply.Click += async (_, _) =>
        {
            if (_savedViews.SelectedItem is string name)
            {
                await RunUiActionAsync(() => _viewModel.ApplySavedViewAsync(name));
            }
        };
        savedButtons.Children.Add(apply);
        var delete = new Button { Content = "Delete" };
        delete.Click += async (_, _) =>
        {
            if (_savedViews.SelectedItem is string name)
            {
                await RunUiActionAsync(() => _viewModel.DeleteSavedViewAsync(name));
            }
        };
        savedButtons.Children.Add(delete);
        views.Children.Add(savedButtons);

        views.Children.Add(SectionLabel("Save current view as"));
        _viewName.Width = double.NaN;
        _viewName.HorizontalAlignment = HorizontalAlignment.Stretch;
        views.Children.Add(_viewName);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) => await RunUiActionAsync(
            () => _viewModel.SaveCurrentViewAsync(_viewName.Text ?? string.Empty));
        views.Children.Add(save);
        EnsureMobileTouch(_savedViews, apply, delete, _viewName, save);
        return views;
    }

    private Grid BuildSessionPane()
    {
        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var copyDetails = new Button
        {
            Content = "Copy details",
            Margin = new Thickness(10, 8, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        EnsureMobileTouch(copyDetails);
        copyDetails.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_sessionInfoText);
            }
        };
        pane.Children.Add(copyDetails);
        var scroll = new ScrollViewer { Content = _sessionInfo };
        Grid.SetRow(scroll, 1);
        pane.Children.Add(scroll);
        return pane;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.85,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private void EnsureMobileTouch(params Control[] controls)
    {
        if (!_mobile)
        {
            return;
        }

        foreach (var control in controls)
        {
            control.MinHeight = Math.Max(48, control.MinHeight);
        }
    }

    /// <summary>
    /// The facet panel states its own scope and semantics. Its counts are whole-session
    /// while the table below the timeline lists the current view, and several values in
    /// one group combine with OR — both are invisible in a bare "+ / −" row and both
    /// change what a click appears to do (§14.11).
    /// </summary>
    private Grid BuildFacetPane()
    {
        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        pane.Children.Add(new TextBlock
        {
            Text = "Counts are for the whole session under the current filter. " +
                   "+ keeps only matching entries (several values in one group are combined with OR), " +
                   "− hides them. Click an active + or − again to remove it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Opacity = 0.72,
            Margin = new Thickness(7, 6, 7, 2),
        });
        _facetScroll = new ScrollViewer { Content = _facets };
        AutomationProperties.SetName(_facetScroll, "Facets");
        Grid.SetRow(_facetScroll, 1);
        pane.Children.Add(_facetScroll);
        return pane;
    }

    private void ConfigureEntryList()
    {
        _entries.SelectionMode = SelectionMode.Multiple;

        // A log table earns its keep by rows on screen: monospace for scanability and
        // compact item containers instead of Fluent's touch-sized defaults.
        _entries.FontFamily = MonoFont;
        _entries.FontSize = _mobile ? 12 : 12.5;
        if (!_mobile)
        {
            // The column header is a separate grid drawn flush to the list's left edge, so
            // the rows must sit flush too — a list or item inset slides every value out
            // from under its label.
            _entries.Padding = new Thickness(0);
            _entries.Styles.Add(CompactItemStyle());
        }
        else
        {
            _entries.Styles.Add(CompactItemStyle(64));
        }
        _entries.ItemTemplate = _mobile
            ? new FuncDataTemplate<NormalizedEntry>((entry, _) =>
                entry is null
                    ? new Border()
                    : new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.Parse("#223650")),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(7, 5),
                        Child = new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = $"{entry.Level.ToLetter()}  {entry.Tag}",
                                    FontWeight = FontWeight.Bold,
                                    Foreground = LevelPalette.BrushOf(entry.Level),
                                },
                                new TextBlock
                                {
                                    Text = entry.Message.Split('\r', '\n')[0],
                                    TextWrapping = TextWrapping.Wrap,
                                    MaxLines = 1,
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                },
                                new TextBlock
                                {
                                    Text = $"{FormatInstant(entry.Timestamp)}  ·  {ProcessLabel(entry)}:{entry.Tid}  ·  {entry.Buffer}",
                                    FontSize = 10,
                                    Foreground = new SolidColorBrush(Color.Parse("#8EA2BE")),
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                },
                            },
                        },
                    })
            : new FuncDataTemplate<NormalizedEntry>((entry, _) =>
                entry is null
                    ? new Grid()
                    :
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions(EntryColumns),
                    Children =
                    {
                        Cell(FormatInstant(entry.Timestamp), 0),
                        Cell(entry.Level.ToLetter().ToString(), 1, LevelPalette.ColorOf(entry.Level)),
                        Cell(ProcessLabel(entry), 2),
                        Cell(entry.Tid.ToString(System.Globalization.CultureInfo.InvariantCulture), 3),
                        Cell(entry.Buffer, 4),
                        Cell(entry.Tag, 5),
                        Cell(entry.TemplateId.ToString(System.Globalization.CultureInfo.InvariantCulture), 6),
                        Cell(entry.Message.Split('\r', '\n')[0], 7),
                    },
                });
        AutomationProperties.SetName(_entries, "Filtered log entries");
    }

    private static Grid EntryColumnHeader()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(EntryColumns),
            Background = new SolidColorBrush(Color.Parse("#111C2D")),
        };
        foreach (var (text, column) in new[]
                 {
                     ("TIME", 0),
                     ("L", 1),
                     ("PROCESS / PID", 2),
                     ("TID", 3),
                     ("BUFFER", 4),
                     ("TAG", 5),
                     ("TPL", 6),
                     ("MESSAGE", 7),
                 })
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#8FA5C4")),
                Margin = new Thickness(4, 5),
            };
            Grid.SetColumn(label, column);
            header.Children.Add(label);
        }

        return header;
    }

    private static Avalonia.Styling.Style CompactItemStyle(double minimumHeight = 22)
    {
        var style = new Avalonia.Styling.Style(static selector => Avalonia.Styling.Selectors.OfType<ListBoxItem>(selector));
        // No horizontal padding: the per-cell 4px margin is the only inset, so a row lines
        // up with the column header, which carries the same margin. Vertical 1px keeps rows
        // tight (§14.1 density) without disturbing the column geometry.
        style.Setters.Add(new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(0, 1)));
        style.Setters.Add(new Avalonia.Styling.Setter(Layoutable.MinHeightProperty, minimumHeight));
        return style;
    }

    private TimeZoneInfo ResolveSessionZone()
    {
        var zoneId = _viewModel.Snapshot?.Descriptor.TimestampPolicy.TimeZoneId;
        if (zoneId is null)
        {
            return TimeZoneInfo.Utc;
        }

        if (_sessionZone is { } cached && string.Equals(_sessionZoneId, zoneId, StringComparison.Ordinal))
        {
            return cached;
        }

        try
        {
            _sessionZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _sessionZone = TimeZoneInfo.Utc;
        }

        _sessionZoneId = zoneId;
        return _sessionZone;
    }

    /// <summary>Session-zone "MM-dd HH:mm:ss.ffffff" — the ISO round-trip form with its
    /// offset suffix overflowed every column it appeared in. Full precision lives in the
    /// raw context and the session pane.</summary>
    private string FormatInstant(InstantUs? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value.ToDateTimeOffset(), ResolveSessionZone())
                .ToString("MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)
            : "untimed";

    private string ProcessLabel(NormalizedEntry entry)
    {
        var name = entry.Timestamp is { } instant
            ? _viewModel.Snapshot?.ResolveProcessName(entry.Pid, instant)
            : null;
        return name is null
            ? entry.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{name} ({entry.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    private void WireInteractions()
    {
        // Moving the view releases any cell scope (SetViewportAsync), which in turn
        // clears the outline through the DetailRange notification below (§14.7).
        _timeline.ViewportChanged += (_, range) => _ = _viewModel.SetViewportAsync(range);
        _timeline.CellSelected += (_, cell) => _ = SelectTimelineCellAsync(cell);
        _timeline.HoverChanged += (_, cell) => _viewModel.RequestCellPattern(cell?.Range, cell?.Level);
        _timeline.RangeSelected += (_, range) =>
        {
            _selectedRange = range;
            _rangeText.Text = $"{FormatInstant(range.StartInclusive)} — {FormatInstant(range.EndExclusive)}";
            _rangeActions.IsVisible = true;
            UpdateChipBarVisibility();
            _ = _viewModel.RefreshCellAsync(range, null);
        };
        _timeline.FollowRequested += (_, _) => _ = _viewModel.ToggleFollowAsync();
        _timeline.SearchFocusRequested += (_, _) => _search.Focus();
        _timeline.ExportRequested += (_, _) => ExportRequested?.Invoke(null);
        _timeline.EntryNavigationRequested += (_, delta) => MoveEntrySelection(delta);
        _timeline.SizeChanged += (_, eventArgs) =>
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            _ = _viewModel.SetRenderWidthAsync(
                Math.Max(64, (int)Math.Round((eventArgs.NewSize.Width - 88) * scale)));
        };
        _minimap.ViewportChanged += (_, range) => _ = _viewModel.SetViewportAsync(range);
        _entries.SelectionChanged += (_, _) =>
        {
            if (_copyRaw is { } copyRaw)
            {
                copyRaw.IsEnabled = _entries.SelectedItems?.Count > 0 || _entries.SelectedItem is NormalizedEntry;
            }

            if (_entries.SelectedItem is NormalizedEntry entry)
            {
                var timelineCount = _selectingTimelineEntry ? _selectedTimelineCellCount : (long?)null;
                _selectingTimelineEntry = false;
                BeginRawContextLoad(entry, timelineCount);
            }
        };

        _viewModel.PropertyChanged += (_, eventArgs) => Dispatcher.UIThread.Post(() =>
        {
            switch (eventArgs.PropertyName)
            {
                case nameof(SessionTabViewModel.HeatMap):
                case nameof(SessionTabViewModel.Overview):
                case nameof(SessionTabViewModel.Viewport):
                    UpdateTimelines();
                    break;
                case nameof(SessionTabViewModel.SearchText):
                    _search.Text = _viewModel.SearchText;
                    break;
                case nameof(SessionTabViewModel.EntryOrder):
                    _order.SelectedIndex = _viewModel.EntryOrder == EntryOrder.SourceSequence ? 1 : 0;
                    break;
                case nameof(SessionTabViewModel.SearchResult):
                    _timeline.SetSearchResult(_viewModel.SearchResult);
                    break;
                case nameof(SessionTabViewModel.Status):
                    _status.Text = _viewModel.Status;
                    UpdateCaptureActions();
                    break;
                case nameof(SessionTabViewModel.SearchStatus):
                    _searchStatus.Text = _viewModel.SearchStatus;
                    break;
                case nameof(SessionTabViewModel.DetailRange):
                    // The outline and the detail scope are one state: whoever releases
                    // the scope releases the outline with it, so the plot can never claim
                    // a selection the table is not listing.
                    if (_viewModel.DetailRange is null)
                    {
                        _timeline.ClearSelection();
                    }

                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.Statistics):
                case nameof(SessionTabViewModel.Filter):
                    UpdateStatistics();
                    break;
                case nameof(SessionTabViewModel.MatchesInView):
                    UpdateStatistics();
                    UpdateEntryLoadControls();
                    break;
                case nameof(SessionTabViewModel.HoverPattern):
                    _timeline.SetHoverInsight(_viewModel.HoverPattern is { } pattern
                        ? new TimelineHoverInsight(
                            pattern.Range,
                            pattern.Level,
                            pattern.TemplateText,
                            pattern.TemplateCount)
                        : null);
                    break;
                case nameof(SessionTabViewModel.RawContextText):
                    // Property changes are marshalled to this queue. A canceled source
                    // read can therefore leave a stale notification behind even after a
                    // newer timeline tap. The current request presents itself explicitly.
                    if (!_timelineEntryPending && !HasRawContextLoad())
                    {
                        PresentRawContext();
                    }

                    break;
                case nameof(SessionTabViewModel.CanLoadMore):
                case nameof(SessionTabViewModel.IsLoadingEntries):
                case nameof(SessionTabViewModel.LoadedEntryCount):
                case nameof(SessionTabViewModel.RemainingEntryCount):
                    UpdateEntryLoadControls();
                    break;
                case nameof(SessionTabViewModel.FollowLatest):
                    UpdateFollowButton();
                    break;
                case nameof(SessionTabViewModel.HasNewData):
                    UpdateCaptureActions();
                    break;
            }
        });
        _viewModel.SnapshotChanged += (_, _) => Dispatcher.UIThread.Post(UpdateTimelines);
    }

    private async Task SelectTimelineCellAsync(TimelineCellSelection cell)
    {
        var generation = Interlocked.Increment(ref _timelineSelectionGeneration);
        _timelineEntryPending = true;
        CancelRawContextLoad();
        ShowRawLoadingState(cell.Count);

        try
        {
            await _viewModel.RefreshCellAsync(cell.Range, cell.Level);
        }
        catch (OperationCanceledException)
        {
            if (generation == Volatile.Read(ref _timelineSelectionGeneration))
            {
                _timelineEntryPending = false;
            }

            return;
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == Volatile.Read(ref _timelineSelectionGeneration))
                {
                    _timelineEntryPending = false;
                    ShowRawErrorState(exception);
                }
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _timelineSelectionGeneration))
            {
                return;
            }

            if (_viewModel.Entries.Count == 0)
            {
                _timelineEntryPending = false;
                ShowRawNoMatchesState();
                return;
            }

            _selectingTimelineEntry = true;
            _selectedTimelineCellCount = _viewModel.MatchesInView ?? cell.Count;
            _entries.SelectedIndex = 0;
        });
    }

    private void BeginRawContextLoad(NormalizedEntry entry, long? timelineCount)
    {
        _timelineEntryPending = false;
        var cancellation = new CancellationTokenSource();
        lock (_rawLoadSync)
        {
            _rawLoadCancellation?.Cancel();
            _rawLoadCancellation = cancellation;
        }

        ShowRawLoadingState(timelineCount);
        _ = LoadRawContextForSelectionAsync(entry, cancellation);
    }

    private async Task LoadRawContextForSelectionAsync(
        NormalizedEntry entry,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _viewModel.LoadRawContextAsync(entry, cancellationToken: cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_rawLoadSync)
                {
                    if (ReferenceEquals(_rawLoadCancellation, cancellation))
                    {
                        PresentRawContext();
                    }
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_rawLoadSync)
                {
                    if (ReferenceEquals(_rawLoadCancellation, cancellation))
                    {
                        ShowRawErrorState(exception);
                    }
                }
            });
        }
        finally
        {
            lock (_rawLoadSync)
            {
                if (ReferenceEquals(_rawLoadCancellation, cancellation))
                {
                    _rawLoadCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelRawContextLoad()
    {
        lock (_rawLoadSync)
        {
            _rawLoadCancellation?.Cancel();
            _rawLoadCancellation = null;
        }
    }

    private bool HasRawContextLoad()
    {
        lock (_rawLoadSync)
        {
            return _rawLoadCancellation is not null;
        }
    }

    private void CompleteRawTextSelection()
    {
        var hasSelection = !string.IsNullOrEmpty(_rawContext.SelectedText);
        if (_rawCopySelection is { } copySelection)
        {
            copySelection.IsEnabled = hasSelection;
        }

        // A completed touch selection automatically releases the text surface. The next
        // drag therefore pans the ScrollViewer instead of extending a hidden selection.
        if (hasSelection)
        {
            SetRawPanMode(true);
        }
    }

    private void SetRawPanMode(bool pan)
    {
        _rawPanMode = pan;
        _rawContext.IsHitTestVisible = !pan;
        if (_rawPanToggle is not { } panToggle)
        {
            return;
        }

        panToggle.Content = pan ? "Pan" : "Select";
        ApplyMobileChoiceAppearance(panToggle, pan);
        AutomationProperties.SetName(
            panToggle,
            pan ? "Pan mode; tap to select text" : "Select mode; tap to pan source");
    }

    private void SetRawWrap(bool wrap)
    {
        _rawWrapEnabled = wrap;
        _rawContext.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (_rawScroller is { } scroller)
        {
            scroller.HorizontalScrollBarVisibility = wrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto;
            if (wrap)
            {
                scroller.Offset = new Vector(0, scroller.Offset.Y);
            }
        }

        if (_rawWrapToggle is { } wrapToggle)
        {
            wrapToggle.Content = wrap ? "Wrap ✓" : "Wrap";
            ApplyMobileChoiceAppearance(wrapToggle, wrap);
            AutomationProperties.SetName(
                wrapToggle,
                wrap ? "Line wrapping on; tap to show full lines" : "Line wrapping off; tap to wrap long lines");
        }

        UpdateRawNavigationButtons();
    }

    private void PanRawContext(int direction)
    {
        if (_rawScroller is not { } scroller || _rawWrapEnabled)
        {
            return;
        }

        var maximum = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        var page = Math.Max(96, scroller.Viewport.Width * 0.8);
        var next = Math.Clamp(scroller.Offset.X + (direction * page), 0, maximum);
        scroller.Offset = new Vector(next, scroller.Offset.Y);
        UpdateRawNavigationButtons();
    }

    private void UpdateRawNavigationButtons()
    {
        if (_rawScroller is not { } scroller)
        {
            return;
        }

        var wrapped = _rawWrapEnabled;
        var maximum = Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width);
        if (_rawPanLeft is { } panLeft)
        {
            panLeft.IsVisible = !wrapped;
            panLeft.IsEnabled = !wrapped && scroller.Offset.X > 0.5;
        }

        if (_rawPanRight is { } panRight)
        {
            panRight.IsVisible = !wrapped;
            panRight.IsEnabled = !wrapped && scroller.Offset.X < maximum - 0.5;
        }
    }

    private void PresentRawContext()
    {
        var raw = _viewModel.RawContextText;
        if (!string.Equals(_rawContext.Text, raw, StringComparison.Ordinal))
        {
            _rawContext.ClearSelection();
            _rawContext.Text = raw;
            if (_rawCopySelection is { } copySelection)
            {
                copySelection.IsEnabled = false;
            }
        }

        var hasRaw = !string.IsNullOrEmpty(raw);
        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = !hasRaw;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = hasRaw;
        }

        // Selecting a row is the request to read its source, so the panel opens
        // itself the moment content arrives; the user can still collapse it.
        if (hasRaw && !_mobile && !_rawExpanded)
        {
            SetRawExpanded(true);
        }
    }

    private void ShowRawLoadingState(long? timelineCount = null)
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = timelineCount is > 0 ? "Loading first entry…" : "Loading source…";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = timelineCount is > 0
                ? $"Reading the first of {timelineCount:N0} entries in the selected timeline bar."
                : "Reading the selected entry's exact bytes and surrounding lines.";
        }

        if (_rawSelectionHint is { } selectionHint)
        {
            selectionHint.Text = timelineCount is > 0
                ? $"First of {timelineCount:N0} in selected bar · choose another row in Entries"
                : "Selected entry · exact source bytes with surrounding lines";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = false;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(
                emptyCard,
                timelineCount is > 0
                    ? "Loading first entry from selected timeline bar"
                    : "Loading source context");
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }
    }

    private void ShowRawNoMatchesState()
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "No matching entries";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = "This timeline bar has no entries after the current filters.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "No matching entries in selected timeline bar");
        }
    }

    private void ShowRawErrorState(Exception exception)
    {
        if (_rawEmptyTitle is { } title)
        {
            title.Text = "Source unavailable";
        }

        if (_rawPlaceholder is { } description)
        {
            description.Text = "VisualCat could not read this entry's source context. Choose another entry to retry.";
        }

        if (_rawChooseEntry is { } chooseEntry)
        {
            chooseEntry.IsVisible = true;
        }

        if (_rawEmptyState is { } emptyState)
        {
            emptyState.IsVisible = true;
        }

        if (_rawDataSurface is { } dataSurface)
        {
            dataSurface.IsVisible = false;
        }

        if (_rawEmptyCard is { } emptyCard)
        {
            AutomationProperties.SetName(emptyCard, "Source context unavailable");
        }

        _status.Text = $"Source unavailable · {exception.Message}";
    }

    private async Task ApplySearchAsync()
    {
        _viewModel.SearchText = _search.Text ?? string.Empty;
        await _viewModel.ApplySearchAsync(_regex.IsChecked == true, _caseSensitive.IsChecked == true);
    }

    private void QueueDebouncedSearch()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchDebounce, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        _ = DebounceAsync(cancellation);

        async Task DebounceAsync(CancellationTokenSource source)
        {
            try
            {
                await Task.Delay(320, source.Token).ConfigureAwait(false);
                if (!source.IsCancellationRequested)
                {
                    Dispatcher.UIThread.Post(() => _ = RunUiActionAsync(ApplySearchAsync));
                }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled";
        }
        catch (Exception exception)
        {
            _status.Text = $"Failed · {exception.GetBaseException().Message}";
        }
    }

    private async Task ToggleLoadAllEntriesAsync()
    {
        if (_loadAllEntriesCancellation is { } active)
        {
            active.Cancel();
            UpdateEntryLoadControls();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _loadAllEntriesCancellation = cancellation;
        UpdateEntryLoadControls();
        try
        {
            await _viewModel.LoadAllEntriesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is an ordinary user action: the already loaded rows remain
            // useful and the exact remainder stays visible beside the controls.
        }
        catch (Exception exception)
        {
            _status.Text = $"Failed · {exception.GetBaseException().Message}";
        }
        finally
        {
            if (ReferenceEquals(_loadAllEntriesCancellation, cancellation))
            {
                _loadAllEntriesCancellation = null;
            }

            cancellation.Dispose();
            UpdateEntryLoadControls();
        }
    }

    private void UpdateEntryLoadControls()
    {
        var loaded = _viewModel.LoadedEntryCount;
        var total = _viewModel.MatchesInView;
        var remaining = _viewModel.RemainingEntryCount;
        var loading = _viewModel.IsLoadingEntries;
        var loadingAll = _loadAllEntriesCancellation is { IsCancellationRequested: false };
        var stopping = _loadAllEntriesCancellation is { IsCancellationRequested: true };

        _loadMore.IsEnabled = _viewModel.CanLoadMore && !loading && _loadAllEntriesCancellation is null;
        _loadMore.IsVisible = !_mobile || _viewModel.CanLoadMore;
        if (!_mobile)
        {
            _entryLoadStatus.Text = total is { } count
                ? loading
                    ? $"{loaded:N0} / {count:N0} rows · loading…"
                    : loaded >= count
                        ? $"All {count:N0} rows loaded"
                        : $"{loaded:N0} / {count:N0} rows loaded"
                : $"{loaded:N0} rows loaded";
            var loadDescription = total is { } knownTotal
                ? $"{loaded:N0} of {knownTotal:N0} matching rows loaded; {remaining:N0} remaining"
                : $"{loaded:N0} matching rows loaded";
            ToolTip.SetTip(_entryLoadStatus, loadDescription);
            AutomationProperties.SetName(_entryLoadStatus, loadDescription);

            _loadAll.Content = stopping ? "Stopping…" : loadingAll ? "Cancel" : "Load all";
            _loadAll.IsEnabled = stopping ? false : loadingAll || _viewModel.CanLoadMore && !loading;
            var loadAllDescription = stopping
                ? "Stopping the all-rows load"
                : loadingAll
                    ? $"Cancel loading all rows; {remaining:N0} remain"
                    : remaining > 0
                        ? $"Load all {remaining:N0} remaining matching rows in batches"
                        : "All matching rows are loaded";
            ToolTip.SetTip(_loadAll, loadAllDescription);
            AutomationProperties.SetName(_loadAll, loadAllDescription);
        }
    }

    private void RefreshPresentation()
    {
        _status.Text = _viewModel.Status;
        _searchStatus.Text = _viewModel.SearchStatus;
        UpdateFollowButton();
        _search.Text = _viewModel.SearchText;
        _order.SelectedIndex = _viewModel.EntryOrder == EntryOrder.SourceSequence ? 1 : 0;
        UpdateTimelines();
        UpdateStatistics();
        UpdateEntryLoadControls();
        UpdateCaptureActions();
    }

    private void UpdateFollowButton()
    {
        var following = _viewModel.FollowLatest;
        _follow.Content = _mobile
            ? following ? "Follow ✓" : "Follow"
            : following ? "Follow: on" : "Follow: off";
        if (_mobile)
        {
            ApplyMobileChoiceAppearance(_follow, following);
        }
    }

    private void UpdateTimelines()
    {
        if (_viewModel.Snapshot is { } snapshot)
        {
            _timeline.SetTimeZoneContext(snapshot.Descriptor.TimestampPolicy.TimeZoneId);
        }

        _timeline.SetResult(_viewModel.HeatMap, _viewModel.Snapshot?.TimedRange);
        _timeline.SetSearchResult(_viewModel.SearchResult);
        _minimap.SetResult(_viewModel.Overview, _viewModel.Viewport, _viewModel.Snapshot?.TimedRange);
        _zoomReadout.Text = _viewModel.Viewport is { } viewport
            ? $"{FormatSpan(viewport.DurationUs)} · {FormatResolution(viewport.DurationUs / Math.Max(1d, _viewModel.HeatMap?.Viewport.DevicePixelWidth ?? 1))}"
            : string.Empty;
        UpdateSessionInfo();
    }

    private void UpdateCaptureActions()
    {
        var sourceKind = _viewModel.Snapshot?.Descriptor.SourceKind;
        var live = sourceKind is SourceKind.Adb or SourceKind.Android or SourceKind.GrowingFile ||
                   _viewModel.Status.StartsWith("Capturing", StringComparison.Ordinal) ||
                   _viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _follow.IsVisible = live;
        _stopCapture.IsVisible =
            _viewModel.Status.StartsWith("Capturing", StringComparison.Ordinal) ||
            _viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _stopCapture.IsEnabled = !_viewModel.Status.StartsWith("Stopping", StringComparison.Ordinal);
        _newData.IsVisible = live && _viewModel.HasNewData;
    }

    private static string FormatSpan(long microseconds) =>
        microseconds switch
        {
            < 1_000 => $"{microseconds:N0} µs",
            < 1_000_000 => $"{microseconds / 1_000d:0.###} ms",
            < 60_000_000 => $"{microseconds / 1_000_000d:0.###} s",
            < 3_600_000_000 => $"{microseconds / 60_000_000d:0.##} min",
            _ => $"{microseconds / 3_600_000_000d:0.##} h",
        };

    private static string FormatResolution(double microsecondsPerPixel) =>
        microsecondsPerPixel switch
        {
            < 0.01 => $"{microsecondsPerPixel * 1_000:0.##} ns/px",
            < 1 => $"{microsecondsPerPixel:0.###} µs/px",
            < 1_000 => $"{microsecondsPerPixel:0.#} µs/px",
            < 1_000_000 => $"{microsecondsPerPixel / 1_000:0.##} ms/px",
            _ => $"{microsecondsPerPixel / 1_000_000:0.##} s/px",
        };

    private void UpdateStatistics()
    {
        var stats = _viewModel.Statistics;
        var inView = _viewModel.MatchesInView;
        var scoped = _viewModel.DetailRange is not null;
        UpdateSummaryText();
        _clearScope.IsVisible = scoped;
        _fitMatches.IsVisible = stats is { TotalMatching: > 0 } &&
                                inView is 0 or null &&
                                stats.FirstInstant is not null &&
                                stats.LastInstant is not null;

        // The summary above tracks the viewport, but chips and facets depend only on the
        // filter and the statistics behind them. Panning must not rebuild a hundred facet
        // rows per frame — and must not move the row under the pointer.
        var filter = _viewModel.Filter;
        if (ReferenceEquals(_renderedChipFilter, filter) && ReferenceEquals(_renderedFacets, stats))
        {
            return;
        }

        _renderedChipFilter = filter;
        _renderedFacets = stats;
        RebuildChips();
        RebuildFacets(stats);
    }

    private void UpdateSummaryText()
    {
        var stats = _viewModel.Statistics;
        if (stats is null)
        {
            _summary.Text = string.Empty;
            AutomationProperties.SetName(_summary, string.Empty);
            ToolTip.SetTip(_summary, null);
            return;
        }

        var inView = _viewModel.MatchesInView ?? 0;
        var scoped = _viewModel.DetailRange is not null;
        var full = $"{inView:N0} {(scoped ? "in selected cell" : "in view")}  ·  " +
                   $"{stats.TotalMatching:N0} in session  ·  " +
                   $"{FormatInstant(stats.FirstInstant)} — {FormatInstant(stats.LastInstant)}";
        var mobileSummary = scoped && _viewModel.DetailRange is { } range
            ? $"{inView:N0} bar · {FormatInstant(range.StartInclusive)} · {stats.TotalMatching:N0} session"
            : $"{inView:N0} view · {stats.TotalMatching:N0} session";
        _summary.Text = _mobile ? mobileSummary : full;
        AutomationProperties.SetName(_summary, full);
        ToolTip.SetTip(_summary, full);
    }

    /// <summary>Moves the viewport onto the span the current filter actually matches.</summary>
    private void FitToMatches()
    {
        if (_viewModel.Statistics is not { FirstInstant: { } first, LastInstant: { } last })
        {
            return;
        }

        var padding = Math.Max(1_000, (last.Value - first.Value) / 40);
        _ = _viewModel.SetViewportAsync(new TimeRange(
            new InstantUs(first.Value - padding),
            new InstantUs(last.Value + padding)));
    }

    private void RebuildChips()
    {
        while (_chips.Children.Count > 1)
        {
            _chips.Children.RemoveAt(_chips.Children.Count - 1);
        }

        var filter = _viewModel.Filter;
        UpdateLevelChecks();
        if (filter.IncludedLevels.Count > 0)
        {
            AddChip(
                $"levels: {string.Join(',', filter.IncludedLevels.Order().Select(static level => level.ToLetter()))}",
                () => _viewModel.ClearLevelFilterAsync());
        }

        // Several values in one dimension are OR'd — a second included tag widens the
        // result rather than narrowing it. One chip per dimension and direction says so
        // in the chip itself instead of leaving the user to infer it (§14.11).
        AddDimensionChip("tag", FacetDimension.Tag, filter.IncludedTags.Order(StringComparer.Ordinal), exclude: false);
        AddDimensionChip("tag", FacetDimension.Tag, filter.ExcludedTags.Order(StringComparer.Ordinal), exclude: true);
        AddDimensionChip(
            "process",
            FacetDimension.Process,
            filter.IncludedProcesses.Order(StringComparer.Ordinal),
            exclude: false);
        AddDimensionChip(
            "process",
            FacetDimension.Process,
            filter.ExcludedProcesses.Order(StringComparer.Ordinal),
            exclude: true);
        AddDimensionChip("pid", FacetDimension.Pid, filter.IncludedPids.Order().Select(Number), exclude: false);
        AddDimensionChip("pid", FacetDimension.Pid, filter.ExcludedPids.Order().Select(Number), exclude: true);
        AddDimensionChip("tid", FacetDimension.Tid, filter.IncludedTids.Order().Select(Number), exclude: false);
        AddDimensionChip("tid", FacetDimension.Tid, filter.ExcludedTids.Order().Select(Number), exclude: true);
        AddDimensionChip(
            "buffer",
            FacetDimension.Buffer,
            filter.IncludedBuffers.Order(StringComparer.Ordinal),
            exclude: false);
        AddDimensionChip(
            "buffer",
            FacetDimension.Buffer,
            filter.ExcludedBuffers.Order(StringComparer.Ordinal),
            exclude: true);
        AddDimensionChip(
            "template",
            FacetDimension.Template,
            filter.IncludedTemplates.Order().Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            exclude: false);
        AddDimensionChip(
            "template",
            FacetDimension.Template,
            filter.ExcludedTemplates.Order().Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            exclude: true);

        if (filter.Search is { } search)
        {
            AddChip(
                $"{(search.IsRegex ? "regex" : "text")} = {search.Query}",
                async () =>
                {
                    _search.Text = string.Empty;
                    await ApplySearchAsync();
                });
        }

        if (filter.TimeRange is { } range)
        {
            AddChip(
                $"time = {FormatInstant(range.StartInclusive)} — {FormatInstant(range.EndExclusive)}",
                () => _viewModel.SetTimeRangeFilterAsync(null));
        }

        // An empty chip strip is dead vertical space; it reappears with the first
        // active filter chip or range selection.
        UpdateMobileFilterCount(filter);
        UpdateChipBarVisibility();
    }

    private void UpdateMobileFilterCount(FilterSpec filter)
    {
        if (!_mobile)
        {
            return;
        }

        var activeGroups = 0;
        activeGroups += filter.Search is null ? 0 : 1;
        activeGroups += filter.TimeRange is null ? 0 : 1;
        activeGroups += filter.IncludedLevels.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTags.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTags.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedPids.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedPids.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedProcesses.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedProcesses.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTids.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTids.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedTemplates.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedTemplates.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedBuffers.Count == 0 ? 0 : 1;
        activeGroups += filter.ExcludedBuffers.Count == 0 ? 0 : 1;
        activeGroups += filter.IncludedOutcomes.Count == 0 ? 0 : 1;

        if (_mobileFilterButton is { } button)
        {
            button.Content = activeGroups == 0 ? "Filters" : $"Filters · {activeGroups}";
        }

        if (_mobileFilterCount is { } count)
        {
            count.Text = activeGroups == 0
                ? "No active filters"
                : $"{activeGroups} active filter {(activeGroups == 1 ? "group" : "groups")}";
        }
    }

    private void UpdateChipBarVisibility()
    {
        _chipBar.IsVisible = !(_mobile && _mobileFiltersOpen) &&
                             (_chips.Children.Count > 1 || _rangeActions.IsVisible);
    }

    /// <summary>
    /// Rebuilds the facet panel from the latest statistics. Two rules keep the panel
    /// usable while the filter changes underneath it: values the filter already acts on
    /// are pinned to the top of their group with their state visible, and the scroll
    /// offset is restored, so the row under the pointer does not move between two
    /// clicks (§14.11).
    /// </summary>
    private void RebuildFacets(StatisticsResult? statistics)
    {
        var scroll = _facetScroll?.Offset;
        _facets.Children.Clear();
        if (statistics is null)
        {
            return;
        }

        AddFacetGroup(
            "Tags",
            FacetDimension.Tag,
            statistics.Tags.Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));
        AddFacetGroup(
            "Processes",
            FacetDimension.Process,
            (statistics.Processes ?? []).Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));
        AddFacetGroup(
            "PIDs",
            FacetDimension.Pid,
            statistics.Pids.Select(facet => (FacetKey.OfNumber(facet.Value), Number(facet.Value), facet.Count)));
        AddFacetGroup(
            "Threads",
            FacetDimension.Tid,
            statistics.Tids.Select(facet => (FacetKey.OfNumber(facet.Value), Number(facet.Value), facet.Count)));
        AddFacetGroup(
            "Buffers",
            FacetDimension.Buffer,
            statistics.Buffers.Select(facet => (FacetKey.OfText(facet.Value), facet.Value, facet.Count)));

        if (scroll is { } offset)
        {
            // The panel is rebuilt inside the same layout pass that produced the click, so
            // the offset is reapplied once the new rows have been measured.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_facetScroll is { } viewer)
                    {
                        viewer.Offset = new Vector(offset.X, Math.Min(offset.Y, Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height)));
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private static string Number(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void AddFacetGroup(
        string heading,
        FacetDimension dimension,
        IEnumerable<(FacetKey Key, string Text, long Count)> values)
    {
        var rows = values
            .Select(value => (value.Key, value.Text, value.Count, State: _viewModel.StateOf(dimension, value.Key)))
            .OrderBy(static row => row.State == FacetState.Neutral ? 1 : 0)
            .ToArray();
        var active = rows.Count(static row => row.State != FacetState.Neutral);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 9, 0, 2) };
        header.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.Bold });
        if (active > 0)
        {
            var clear = new Button
            {
                Content = "Clear",
                FontSize = 10,
                Padding = new Thickness(6, 0),
                Background = Brushes.Transparent,
            };
            ToolTip.SetTip(clear, $"Remove every {heading.ToLowerInvariant()} filter");
            clear.Click += (_, _) => _ = _viewModel.ClearFacetDimensionAsync(dimension);
            Grid.SetColumn(clear, 1);
            header.Children.Add(clear);
        }

        _facets.Children.Add(header);
        foreach (var row in rows)
        {
            _facets.Children.Add(FacetRow(dimension, row.Key, row.Text, row.Count, row.State));
        }
    }

    private Grid FacetRow(FacetDimension dimension, FacetKey key, string text, long count, FacetState state)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            Margin = new Thickness(0, 1),
        };
        row.Children.Add(new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = state == FacetState.Excluded ? 0.55 : 1,
            TextDecorations = state == FacetState.Excluded ? TextDecorations.Strikethrough : null,
            FontWeight = state == FacetState.Included ? FontWeight.SemiBold : FontWeight.Normal,
        });
        var countText = new TextBlock
        {
            Text = count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            FontFamily = MonoFont,
            TextAlignment = TextAlignment.Right,
            MinWidth = 52,
            Margin = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        Grid.SetColumn(countText, 1);
        row.Children.Add(countText);
        var include = FacetButton("+", state == FacetState.Included, IncludeActive, dimension, key, exclude: false);
        Grid.SetColumn(include, 2);
        row.Children.Add(include);
        var exclude = FacetButton("−", state == FacetState.Excluded, ExcludeActive, dimension, key, exclude: true);
        Grid.SetColumn(exclude, 3);
        row.Children.Add(exclude);
        return row;
    }

    private Button FacetButton(
        string glyph,
        bool active,
        IBrush activeBrush,
        FacetDimension dimension,
        FacetKey key,
        bool exclude)
    {
        var button = new Button
        {
            Content = glyph,
            // Facet controls were a 16-pixel target; the pointer, not the eye, is the
            // constraint here (§14.14 touch/pointer targets).
            Padding = new Thickness(10, 3),
            MinWidth = _mobile ? 48 : 32,
            MinHeight = _mobile ? 48 : 26,
            Margin = new Thickness(2, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (active)
        {
            // Assigned only in the active state: a null brush is a local value that
            // overrides the theme rather than falling back to it, which renders the
            // control invisible.
            button.Background = activeBrush;
            button.Foreground = Brushes.White;
        }
        ToolTip.SetTip(
            button,
            active
                ? $"Currently {(exclude ? "excluded" : "included")} · click to remove this filter"
                : exclude
                    ? "Exclude: hide these entries"
                    : "Include: keep only entries matching a value from this group");
        AutomationProperties.SetName(button, exclude ? "Exclude facet value" : "Include facet value");
        button.Click += (_, _) => _ = _viewModel.ToggleFacetAsync(dimension, key, exclude);
        return button;
    }

    private void AddDimensionChip(string label, FacetDimension dimension, IEnumerable<string> values, bool exclude)
    {
        var listed = values.ToArray();
        if (listed.Length == 0)
        {
            return;
        }

        var operators = exclude ? "≠" : "=";
        var text = listed.Length == 1
            ? $"{label} {operators} {Shorten(listed[0])}"
            : $"{label} {operators} {string.Join(" or ", listed.Take(3).Select(Shorten))}" +
              (listed.Length > 3 ? $" +{listed.Length - 3}" : string.Empty);
        AddChip(text, () => _viewModel.ClearFacetDimensionAsync(dimension, exclude));
    }

    private static string Shorten(string value) => value.Length <= 28 ? value : value[..27] + "…";

    private void AddChip(string text, Func<Task>? remove = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        if (remove is not null)
        {
            var close = new Button
            {
                Content = "×",
                Padding = new Thickness(4, 0),
                MinWidth = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Remove this filter");
            AutomationProperties.SetName(close, $"Remove filter {text}");
            close.Click += (_, _) => _ = remove();
            content.Children.Add(close);
        }

        _chips.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#304DA3FF")),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3, 1),
            Padding = new Thickness(7, 2),
            Child = content,
        });
    }

    private void UpdateLevelChecks()
    {
        _updatingLevelChecks = true;
        try
        {
            foreach (var (level, toggle) in _levelChecks)
            {
                toggle.IsChecked = _viewModel.Filter.IncludedLevels.Count == 0 ||
                                   _viewModel.Filter.IncludedLevels.Contains(level);
            }
        }
        finally
        {
            _updatingLevelChecks = false;
        }
    }

    /// <summary>
    /// Paints one severity toggle for its current state. Included reads as the level's own
    /// color on a tinted plate; excluded drops to a flat muted outline so a hidden severity
    /// is obvious at a glance rather than requiring the reader to notice an unticked box.
    /// Shape and letter carry the state as well as color, so the distinction survives both
    /// a monochrome display and the high-contrast setting (§14.14).
    /// </summary>
    private void ApplyLevelToggleColors(ToggleButton toggle, LogLevel level)
    {
        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var color = LevelPalette.ColorOf(level);
        if (toggle.IsChecked == true)
        {
            toggle.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)56 : (byte)40, color.R, color.G, color.B));
            toggle.BorderBrush = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
            toggle.Foreground = new SolidColorBrush(dark ? color : Darken(color));
            toggle.BorderThickness = new Thickness(1);
            toggle.Opacity = 1;
        }
        else
        {
            toggle.Background = Brushes.Transparent;
            toggle.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
            toggle.Foreground = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
            toggle.BorderThickness = new Thickness(1);
            toggle.Opacity = 0.55;
        }
    }

    /// <summary>Keeps a level color legible as text on a light surface.</summary>
    private static Color Darken(Color color) =>
        Color.FromRgb((byte)(color.R * 0.62), (byte)(color.G * 0.62), (byte)(color.B * 0.62));

    /// <summary>
    /// Minimal toggle template that honours the control's own brushes. Fluent's default
    /// paints the checked state from a theme resource onto an inner content presenter,
    /// which sits above anything assigned to the control itself — so every severity
    /// toggle rendered in the same accent blue no matter which color was set on it. This
    /// template binds the visual straight to Background/BorderBrush/Foreground, leaving
    /// <see cref="ApplyLevelToggleColors"/> as the single authority on how a state looks.
    /// </summary>
    private static ControlTheme BuildLevelToggleTheme()
    {
        var template = new FuncControlTemplate<ToggleButton>((control, scope) =>
        {
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            presenter[!ContentPresenter.ContentProperty] = control[!ContentControl.ContentProperty];
            presenter[!ContentPresenter.ForegroundProperty] = control[!TemplatedControl.ForegroundProperty];
            presenter[!ContentPresenter.FontWeightProperty] = control[!TemplatedControl.FontWeightProperty];
            presenter[!ContentPresenter.FontSizeProperty] = control[!TemplatedControl.FontSizeProperty];

            var border = new Border { Child = presenter };
            border[!Border.BackgroundProperty] = control[!TemplatedControl.BackgroundProperty];
            border[!Border.BorderBrushProperty] = control[!TemplatedControl.BorderBrushProperty];
            border[!Border.BorderThicknessProperty] = control[!TemplatedControl.BorderThicknessProperty];
            border[!Border.CornerRadiusProperty] = control[!TemplatedControl.CornerRadiusProperty];

            // Only the named part joins the scope; registering the unnamed border throws.
            presenter.RegisterInNameScope(scope);
            return border;
        });

        var theme = new ControlTheme(typeof(ToggleButton));
        theme.Setters.Add(new Setter(TemplatedControl.TemplateProperty, template));

        // Pointer feedback the stripped template would otherwise lose.
        var hover = new Style(static selector => selector.Nesting().Class(":pointerover"));
        hover.Setters.Add(new Setter(OpacityProperty, 0.82));
        theme.Add(hover);
        return theme;
    }

    private void MoveEntrySelection(int delta)
    {
        if (_entries.ItemCount == 0)
        {
            return;
        }

        _entries.SelectedIndex = Math.Clamp(
            (_entries.SelectedIndex < 0 ? 0 : _entries.SelectedIndex) + delta,
            0,
            _entries.ItemCount - 1);
        _entries.ScrollIntoView(_entries.SelectedIndex);
    }

    private async Task CopySelectedRawAsync()
    {
        var selected = _entries.SelectedItems?.OfType<NormalizedEntry>().ToArray() ?? [];
        if (selected.Length == 0 && _entries.SelectedItem is NormalizedEntry entry)
        {
            selected = [entry];
        }

        if (selected.Length == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var text = await _viewModel.ReadRawEntriesAsync(selected);
        await clipboard.SetTextAsync(text);
    }

    /// <summary>
    /// Builds the session pane as a grouped label/value list rather than one monospace
    /// blob: sections give the eye anchors, the muted-label / bright-value split reads as a
    /// table, and a non-zero defect is drawn in the warn color so a health problem stands
    /// out instead of hiding in an identical wall of digits (§14.1, §14.11).
    /// </summary>
    private void UpdateSessionInfo()
    {
        _sessionInfo.Children.Clear();
        var snapshot = _viewModel.Snapshot;
        if (snapshot is null)
        {
            _sessionInfoText = string.Empty;
            _sessionInfo.Children.Add(new TextBlock
            {
                Text = "Session metadata becomes available after the first committed snapshot.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            return;
        }

        var dark = ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        var labelBrush = new SolidColorBrush(WorkspacePalette.TextMuted(dark));
        var valueBrush = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        var headBrush = new SolidColorBrush(WorkspacePalette.Accent(dark));
        var warnColor = LevelPalette.ColorOf(LogLevel.Warn);
        var warnBrush = new SolidColorBrush(dark ? warnColor : Darken(warnColor));

        var descriptor = snapshot.Descriptor;
        var counters = descriptor.Counters;
        var defects = descriptor.Defects;
        var manifest = snapshot.Manifest;
        var text = new System.Text.StringBuilder();

        void Section(string title)
        {
            text.Append('\n').Append(title).Append('\n');
            _sessionInfo.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontWeight = FontWeight.Bold,
                FontSize = 10,
                Foreground = headBrush,
                Margin = new Thickness(0, _sessionInfo.Children.Count == 0 ? 0 : 11, 0, 3),
            });
        }

        void Row(string label, string value, bool warn = false)
        {
            text.Append(label).Append(": ").Append(value).Append('\n');
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("148,*"),
                Margin = new Thickness(0, 1),
            };
            row.Children.Add(new TextBlock { Text = label, Foreground = labelBrush, FontSize = 11.5 });
            var valueText = new TextBlock
            {
                Text = value,
                Foreground = warn ? warnBrush : valueBrush,
                FontWeight = warn ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);
            _sessionInfo.Children.Add(row);
        }

        static string N(long value) => value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

        Section("Session");
        Row("Source", $"{descriptor.SourceKind} · {descriptor.DisplayName}");
        Row("Format", manifest.IngestSettings.FormatOverride?.ToString() ?? "auto-detected");
        Row("State", $"{descriptor.State}{(descriptor.Degraded ? " · degraded/index-only" : string.Empty)}", descriptor.Degraded);
        Row("Session id", descriptor.SessionId.ToString());

        Section("Entries");
        Row("Timed", N(counters.TimedEntries));
        // Untimed lines (buffer markers and the like) and inferred/continued timestamps are
        // expected in ordinary logs, so they stay neutral; highlighting them would cry wolf
        // on every healthy session and bury the counts that do signal a problem.
        Row("Untimed", N(counters.UntimedEntries));
        Row("Parsed", N(counters.ParsedEntries));
        Row("Rejected", N(counters.RejectedCandidates), counters.RejectedCandidates > 0);
        Row("Unknown", N(counters.UnknownLines), counters.UnknownLines > 0);
        Row("Process-name ranges", N(snapshot.ProcessNames.Count));

        Section("Defects");
        Row("Continuations", N(defects.Continuations));
        Row("Inferred time", N(defects.TimestampInferences));
        Row("Low confidence", N(defects.LowConfidenceTimestamps), defects.LowConfidenceTimestamps > 0);
        Row("Out-of-order", N(defects.OutOfOrderEntries), defects.OutOfOrderEntries > 0);
        Row("Late segment", N(defects.LateSegmentEntries), defects.LateSegmentEntries > 0);
        Row("Source changes", N(defects.SourceChanges));

        Section("Live loss evidence");
        Row("Chatty drops", N(defects.ChattyDeclaredDrops), defects.ChattyDeclaredDrops > 0);
        Row("Reconnect gaps", N(defects.ReconnectGaps), defects.ReconnectGaps > 0);
        Row("Duplicates", N(defects.ReconnectDuplicates), defects.ReconnectDuplicates > 0);

        Section("Safety");
        Row("Encoding fallback", N(defects.EncodingFallbacks), defects.EncodingFallbacks > 0);
        Row("Long-line overflow", N(defects.LongLineOverflows), defects.LongLineOverflows > 0);
        Row("Retention deleted", N(defects.RetentionDeleted), defects.RetentionDeleted > 0);

        Section("Build");
        Row("Snapshot", $"{snapshot.Generation} · store {manifest.FormatVersion}");
        Row("Parser", $"{manifest.ParserVersion}");
        Row("Templates", $"{manifest.TemplateAlgorithmVersion}");
        Row("Updated", manifest.UpdatedUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture));

        Section("Location");
        Row("Path", snapshot.RootPath);

        _sessionInfoText = text.ToString().TrimStart('\n');
    }

    private static TextBlock Cell(string text, int column, Color? foreground = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 2),
        };
        if (foreground is { } color)
        {
            cell.Foreground = new SolidColorBrush(color);
        }

        Grid.SetColumn(cell, column);
        return cell;
    }
}
