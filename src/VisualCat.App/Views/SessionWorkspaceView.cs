using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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

public sealed partial class SessionWorkspaceView : UserControl
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
        AutomationProperties.SetHelpText(
            _timeline,
            "Arrow keys pan; plus/minus zoom; 0 fits; F follows; Ctrl+F searches; J/K move between entries.");
        AutomationProperties.SetHelpText(_search, "Ctrl+F focuses search; Enter applies it; F3 or N moves between matches.");
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

    /// <summary>Handles the workspace shortcuts shared by the global host and focused panes.</summary>
    internal bool TryHandleShortcut(KeyEventArgs eventArgs)
    {
        var control = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var textInputFocused = eventArgs.Source is TextBox;

        if (control && eventArgs.Key == Key.F)
        {
            _search.Focus();
            _search.SelectAll();
            return true;
        }

        if (eventArgs.Key == Key.F3 ||
            eventArgs.Key == Key.N && !control && !alt && !textInputFocused)
        {
            _ = RunUiActionAsync(() => NavigateSearchMatchAsync(shift ? -1 : 1));
            return true;
        }

        if (eventArgs.Key == Key.Escape)
        {
            _ = RunUiActionAsync(HandleEscapeAsync);
            return true;
        }

        if (!control && !alt && !textInputFocused && eventArgs.Key is Key.J or Key.K)
        {
            MoveEntrySelection(eventArgs.Key == Key.J ? 1 : -1);
            return true;
        }

        if (alt && !control)
        {
            var focused = eventArgs.Key switch
            {
                Key.D1 or Key.NumPad1 => _timeline.Focus(),
                Key.D2 or Key.NumPad2 => _entries.Focus(),
                Key.D3 or Key.NumPad3 => _templates.Focus(),
                Key.D4 or Key.NumPad4 => FocusFirstFacet(),
                _ => false,
            };
            if (focused)
            {
                return true;
            }
        }

        return false;
    }

    internal async Task NavigateSearchMatchAsync(int direction)
    {
        if (_viewModel.SearchResult?.Markers is not { Count: > 0 } markers ||
            _viewModel.Viewport is not { } viewport ||
            _viewModel.Snapshot?.TimedRange is not { } session)
        {
            _search.Focus();
            return;
        }

        var center = viewport.StartInclusive.Value + viewport.DurationUs / 2;
        var marker = direction >= 0
            ? markers.Where(candidate => candidate.Value > center)
                .Select(static candidate => (InstantUs?)candidate)
                .FirstOrDefault() ?? markers[0]
            : markers.Where(candidate => candidate.Value < center)
                .Select(static candidate => (InstantUs?)candidate)
                .LastOrDefault() ?? markers[^1];
        var span = Math.Min(viewport.DurationUs, session.DurationUs);
        var maximumStart = session.EndExclusive.Value - span;
        var start = Math.Clamp(marker.Value - span / 2, session.StartInclusive.Value, maximumStart);
        await _viewModel.SetViewportAsync(
            new TimeRange(new InstantUs(start), new InstantUs(start + span))).ConfigureAwait(false);
    }

    private bool FocusFirstFacet() =>
        _facets.GetLogicalDescendants().OfType<Button>().FirstOrDefault()?.Focus() == true;

    private async Task HandleEscapeAsync()
    {
        if (_mobileFiltersOpen)
        {
            SetMobileFiltersOpen(false);
            return;
        }

        if (_search.IsFocused && !string.IsNullOrWhiteSpace(_viewModel.SearchText))
        {
            _search.Text = string.Empty;
            _viewModel.SearchText = string.Empty;
            await _viewModel.ApplySearchAsync(_regex.IsChecked == true, _caseSensitive.IsChecked == true).ConfigureAwait(false);
            return;
        }

        if (_viewModel.ClearDetailScope())
        {
            await _viewModel.RefreshAsync().ConfigureAwait(false);
            return;
        }

        if (_viewModel.Filter.Fingerprint() != FilterSpec.All.Fingerprint())
        {
            await _viewModel.ClearFiltersAsync().ConfigureAwait(false);
        }
    }

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

}
