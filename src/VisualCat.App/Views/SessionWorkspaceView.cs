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
using Avalonia.VisualTree;
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
    /// <summary>
    /// Forces the phone composition on or off, for tests that need to exercise it.
    /// </summary>
    /// <remarks>
    /// Everything the two Android audits are about lives in the branch this field selects,
    /// and none of it could be checked without a device: a headless run on a desktop always
    /// took the desktop branch. Read once per view, so a test sets it before constructing
    /// the workspace and restores it afterwards. Null means "ask the platform", which is
    /// what every shipping build does.
    /// </remarks>
    internal static bool? PhoneCompositionOverride { get; set; }

    private readonly bool _mobile = PhoneCompositionOverride ?? OperatingSystem.IsAndroid();
    private readonly TimelineControl _timeline = new();
    private readonly MinimapControl _minimap = new();
    private readonly StatusLine _status;
    private readonly TextBlock _searchStatus = new();

    /// <summary>
    /// The touch route to the next and previous search match.
    /// </summary>
    /// <remarks>
    /// Marker navigation was <c>F3</c>/<c>N</c> and nothing else: no control in the command
    /// band, the chip bar, the filter drawer or the More sheet, no tappable marker lane, and no
    /// node in the accessibility tree — so on a phone the product reported "7,181 search
    /// matches" and offered no way to visit one (finding F-07). B-06 is a basic scenario and
    /// U-10 wants every action reachable without a keyboard as well as without touch.
    ///
    /// It goes where the match count already was, because that is the one place on the screen
    /// a reader is already looking when they want the next match.
    /// </remarks>
    private Panel? _markerNav;
    private TextBlock? _markerPosition;
    private Button? _markerPrevious;
    private Button? _markerNext;
    private readonly TextBox _search = new() { PlaceholderText = "Search message text or regex…" };
    private TextBlock? _mobileSearchPlaceholder;
    private readonly CheckBox _regex = new() { Content = "Regex" };
    private readonly CheckBox _caseSensitive = new() { Content = "Case-sensitive" };

    /// <summary>
    /// Why the pattern in the query field cannot be used, beside the field it is about.
    /// </summary>
    /// <remarks>
    /// An uncompilable pattern used to be accepted as a filter and reported as a failure on
    /// the status line — one clipped line at the far end of the workspace, in framework
    /// language, describing a filter the chip bar was simultaneously claiming was active
    /// (finding F-04). It belongs next to the thing the reader can fix, it stays out of the
    /// way until there is something to say, and it is announced when it appears.
    /// </remarks>
    private readonly TextBlock _searchProblem = new()
    {
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        FontSize = TextScale.Of(11),
    };
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

    // The other half of the §14.9 inspector: the entry's own full message, wrapped and
    // selectable. The table can only ever show one clipped line, and until this existed a
    // long message had no reachable form anywhere in the product.
    private readonly SelectableTextBlock _inspectMessage = new()
    {
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private const int InspectorMessageLimit = 64 * 1024;
    private NormalizedEntry? _inspectedEntry;
    private long? _selectedEntryId;

    /// <summary>
    /// The filter the inspected entry was chosen under, and where it sits in time.
    /// </summary>
    /// <remarks>
    /// "Not in this viewport" is not a deselection. With Follow on, the moving 30-second
    /// window ages an entry out of the loaded page after a few seconds of reading it, and the
    /// workspace answered by clearing the selection, disabling Copy raw and Entry, removing
    /// the plot caret and changing an already-open inspector to "No entry selected" — losing
    /// the message and its source bytes for someone whose only mistake was reading
    /// (finding F-25). Clearing is right for the reasons the reader caused: an explicit
    /// deselection, a filter that now excludes the record, or the session closing. The filter
    /// fingerprint is what tells those apart from the window simply having moved.
    /// </remarks>
    private string? _selectionFilterFingerprint;
    private InstantUs? _selectedEntryInstant;
    private bool _selectedEntryOffPage;
    private Border? _entryOffPageBanner;
    private TextBlock? _entryOffPageText;
    private bool _reloadingEntries;
    private bool _pressedSelectedEntry;
    private string _presentedRawText = string.Empty;
    private Border? _inspectPill;
    private TextBlock? _inspectPillText;
    private TextBlock? _inspectTag;
    private TextBlock? _inspectMeta;
    private TextBlock? _inspectTruncated;
    private ScrollViewer? _inspectScroll;
    private Control? _inspectIdentity;
    private Button? _sourceHeader;
    private TextBlock? _sourceChevron;
    private bool _sourceExpanded;
    private Button? _copyMessage;
    private Button? _openInspector;
    private TextBlock? _sourceStatus;
    private Control? _rawSourceTools;
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
    private Button? _rawSelectToggle;
    private TextBlock? _rawPanState;
    private Button? _rawWrapToggle;
    private Button? _rawRetry;
    private Button? _rawCopySelection;
    private DispatcherTimer? _rawLoadWatchdog;
    private bool _rawLoadPending;
    private Button? _rawPanLeft;
    private Button? _rawPanRight;
    private bool _rawExpanded;
    private bool _selectingTimelineEntry;
    private bool _timelineEntryPending;
    private long _selectedTimelineCellCount;
    private long _timelineSelectionGeneration;
    private readonly object _rawLoadSync = new();
    private CancellationTokenSource? _rawLoadCancellation;
    private NormalizedEntry? _rawLoadEntry;
    private long? _rawLoadTimelineCount;
    private bool _rawLoadInterrupted;
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,Roboto Mono,monospace");
    private static readonly IBrush IncludeActive = new SolidColorBrush(Color.Parse("#1E6FA8"));
    private static readonly IBrush ExcludeActive = new SolidColorBrush(Color.Parse("#8A3B4A"));
    private readonly StackPanel _facets = new() { Spacing = 2, Margin = new Thickness(6) };
    private ScrollViewer? _facetScroll;
    private readonly Button _fitMatches = new() { Content = "Fit to matches", Margin = new Thickness(0, 0, 6, 0), IsVisible = false };
    private Border? _emptyResultsCard;
    private TextBlock? _emptyResultsTitle;
    private TextBlock? _emptyResultsDetail;
    private Button? _emptyResultsWiden;
    private Button? _emptyResultsClear;
    private readonly Button _clearScope = new() { Content = "Clear cell", Margin = new Thickness(0, 0, 6, 0), IsVisible = false };
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rangeActions = new() { Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    private readonly TextBlock _rangeText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _follow = new()
    {
        Content = "Follow: off",
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Button _newData = new()
    {
        Content = "↓ New data",
        IsVisible = false,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Button _stopCapture = new()
    {
        Content = "Stop capture",
        VerticalContentAlignment = VerticalAlignment.Center,
    };
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
    private FadingScrollHost? _mobileFilterFade;
    private Grid? _mobileFilterShell;
    private Grid? _mobileFilterBody;
    private Control? _filterHost;
    private Control? _rowSplitter;
    private Button? _mobileFit;
    private Grid? _mobileModeSelector;
    private Grid? _entryPrimaryActions;
    private Panel? _entryContextActions;
    private Border? _entryFooter;
    private TextBlock? _severityLegend;
    private TextBlock? _chipEmptyLabel;
    private Button? _clearFilters;
    private Control? _mobileQuerySection;
    private Grid? _mobileQueryRow;

    /// <summary>The QUERY caption, which a very short drawer gives up to keep its field.</summary>
    private Control? _mobileQueryCaption;

    /// <summary>The drawer's decision row, whose margins a very short drawer also gives up.</summary>
    private Grid? _mobileFilterFooter;

    private WrapPanel? _mobileQueryOptions;
    private Control? _mobileSeveritySection;
    private Control? _mobileTimeSection;
    private TextBlock? _mobileFilterCount;
    private Grid? _mobileQuickActions;
    private Grid? _mobileCaptureActions;
    private readonly Dictionary<MobileWorkspaceDisplayMode, Button> _mobileModeButtons = [];
    private Border? _minimapFrame;
    private TabControl? _mobileAnalysisTabs;

    /// <summary>The analysis tabs' header captions, so the strip can size them to the room it has.</summary>
    private readonly List<TextBlock> _mobileTabCaptions = [];
    private Grid? _mobileSummaryHost;
    private bool _summaryInTabStrip;
    private Grid? _entryHeader;
    private Grid? _entryActions;
    private Button? _copyRaw;
    private Button? _templateInclude;
    private Button? _templateExclude;
    private Button? _templateCopy;
    private DockPanel? _statusBar;
    private TextBlock? _statusChevron;
    private MobileWorkspaceMode? _mobileLayoutMode;
    private readonly MobileWorkspaceState _mobileWorkspaceState = new();
    private bool _mobileFiltersOpen;
    private bool _compactEditorActive;
    private bool _compactCommandsExternallyHosted;
    private Rect _inputPaneRect;
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

    // One-way per session: see ObserveTimestampPrecision.
    private bool _microsecondTimestamps;

    public SessionWorkspaceView(SessionTabViewModel viewModel)
    {
        _viewModel = viewModel;
        _status = new StatusLine(viewModel);
        Content = Build();
        _entries.ItemsSource = viewModel.Entries;
        _templates.ItemsSource = viewModel.Templates;
        WireInteractions();
        RefreshPresentation();

        // Avalonia surfaces help text as the Android accessibility node's content
        // description, so a phone's screen reader was reading out a desktop keyboard map —
        // including a bare "(0)" that has no meaning without a keyboard and is heard as a
        // count of zero (finding 14). Each platform is told what it can actually do.
        AutomationProperties.SetHelpText(
            _timeline,
            _mobile
                ? "Drag to pan; pinch to zoom; double-tap to zoom in; Fit shows the whole session."
                : "Arrow keys pan; plus/minus zoom; 0 fits; F follows; Ctrl+F searches; J/K move between entries.");
        AutomationProperties.SetHelpText(
            _search,
            _mobile
                ? "Search message text or regular expression. Filters the entries as you type. The keyboard's action key applies it and closes this panel."
                : "Ctrl+F focuses search; Enter applies it; F3 or N moves between matches.");
        SizeChanged += (_, eventArgs) => ApplyMobileLayout(eventArgs.NewSize);

        // The two controls whose main gesture is a horizontal drag, and the only two that
        // reach far enough across the screen to collide with a platform edge gesture. On
        // gesture navigation a pan started in the plot's outer 49 px used to go Back and
        // leave the app (finding F-28); this claims those touches for the plot.
        Platform.EdgeGestureGuard.Track(_timeline);
        Platform.EdgeGestureGuard.Track(_minimap);

        // The entries floor is computed from the pane's own arranged chrome, so it is
        // re-checked after every arrange rather than guessed before the first one. The
        // check is a handful of comparisons and only touches the layout when the answer
        // has actually moved, so it settles in one further pass instead of oscillating.
        if (_mobile)
        {
            LayoutUpdated += (_, _) => EnforceEntriesFloor();
        }
        ActualThemeVariantChanged += (_, _) => ApplyThemeSurfaces();
        ApplyThemeSurfaces();
        AttachedToVisualTree += (_, _) =>
        {
            ObserveInputPane();
            Dispatcher.UIThread.Post(() =>
            {
                // ActualThemeVariant only means anything once there is a top level to
                // inherit it from, and everything above was built before there was one, so
                // a cold start in light mode had been served dark values throughout
                // (audit 2, A1c).
                ApplyThemeSurfaces();
                if (_mobile)
                {
                    ApplyMobileLayout(Bounds.Size);
                    _root.InvalidateMeasure();
                    _root.InvalidateArrange();
                }

                ResumeInterruptedRawContextLoad();
            });
        };
        DetachedFromVisualTree += (_, _) =>
        {
            StopObservingInputPane();
            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
            _searchDebounce = null;
            _loadAllEntriesCancellation?.Cancel();
            _loadAllEntriesCancellation?.Dispose();
            _loadAllEntriesCancellation = null;
            CancelRawContextLoad(resumeOnAttach: true);

            // The read is not running any more, so neither is the clock that would have
            // called it failed; re-attaching resumes both.
            DisarmRawLoadWatchdog();
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

        if (_entryOffPageBanner is { } offPageBanner)
        {
            // Accent rather than warning ink: nothing has gone wrong, the reader has simply
            // been overtaken by the live edge (finding F-25).
            var accent = WorkspacePalette.Accent(dark);
            offPageBanner.Background = new SolidColorBrush(
                Color.FromArgb(dark ? (byte)40 : (byte)26, accent.R, accent.G, accent.B));
            offPageBanner.BorderBrush = new SolidColorBrush(
                Color.FromArgb(150, accent.R, accent.G, accent.B));
            if (_entryOffPageText is { } offPageText)
            {
                offPageText.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
            }
        }

        if (_mobileFilterPanel is { } filterPanel)
        {
            filterPanel.Background = new SolidColorBrush(WorkspacePalette.SurfaceRaised(dark));
            filterPanel.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        // A fade is made of the surface it fades into, so it changes with the surface.
        _mobileFilterFade?.ApplyTheme(dark);

        if (_minimapFrame is { } minimapFrame)
        {
            minimapFrame.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        if (_entryFooter is { } entryFooter)
        {
            entryFooter.BorderBrush = new SolidColorBrush(WorkspacePalette.BorderLine(dark));
        }

        // Both plots resolve their palette inside Render, so they need telling that the
        // answer has changed and nothing else. Without this the minimap stayed a solid
        // navy rectangle in the middle of a white page until something else moved it
        // (audit 2, A1b).
        _timeline.InvalidateVisual();
        _minimap.InvalidateVisual();

        // The dump paints its own per-run colors, so it is redrawn rather than recolored.
        ApplyRawContextText();
        _inspectMessage.Foreground = new SolidColorBrush(WorkspacePalette.TextPrimary(dark));
        if (_inspectMeta is { } inspectMeta)
        {
            inspectMeta.Foreground = muted;
        }

        // The entry pane's tag is severity ink, so it follows the theme with the rest of it.
        if (_inspectTag is { } inspectTag && _inspectedEntry is { } inspected)
        {
            inspectTag.Foreground = LevelPalette.InkBrushOf(inspected.Level, dark);
        }

        if (_inspectTruncated is { } inspectTruncated)
        {
            inspectTruncated.Foreground = muted;
        }

        if (_sourceStatus is { } sourceStatus)
        {
            sourceStatus.Foreground = muted;
        }

        // Entry rows carry theme colors resolved when the row was realized, and a theme
        // change does not re-realize them. Reinstalling the template does, exactly once.
        if (_entries.ItemTemplate is not null)
        {
            ApplyEntryTemplate();
        }

        if (_mobile)
        {
            ApplyMobileModeButtonStyles();
            if (_mobileFilterButton is { } filterButton)
            {
                ApplyMobileChoiceAppearance(filterButton, _mobileFiltersOpen);
            }

            if (_rawPanToggle is not null || _rawSelectToggle is not null)
            {
                // A two-segment selector: the lit segment is the mode a drag is currently in,
                // and the words beside it say what that means (findings 15 and 21.3).
                SetRawPanMode(_rawPanMode);
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
                     _sourceChevron,
                     _statusChevron,
                 })
        {
            if (element is { } textBlock)
            {
                textBlock.Foreground = muted;
            }
        }

        if (_severityLegend is { } severityLegend)
        {
            severityLegend.Foreground = muted;
        }

        // The severity chips paint from the level palette's ink, which is theme-dependent, so
        // they are repainted with the theme. Without this the drawer kept the dark palette's
        // saturated letters on a light plate — the whole of audit 3's B1 in one row of
        // controls, and the only place the light theme's repaint had missed.
        foreach (var (level, toggle) in _levelChecks)
        {
            ApplyLevelToggleColors(toggle, level);
        }

        ApplyFailureTheme();

        // The chip bar caches what it last drew against the filter that produced it, so a
        // repaint has to say that the cache is stale before asking for one; the summary and
        // the status line are rewritten by the same call.
        _renderedChipFilter = null;
        _renderedFacets = null;
        RefreshPresentation();

        // The session pane paints its own label/value/warn brushes, so it is rebuilt with
        // the theme rather than left in the previous variant's colors.
        UpdateSessionInfo();
    }

    public event Action<TimeRange?>? ExportRequested;
    public event Func<Task>? StopRequested;

    /// <summary>Raised when the reader picks a different phone workspace mode.</summary>
    internal event Action<string>? DisplayModeChanged;

    /// <summary>
    /// Raised while a compact-height query is being edited, so the shell can yield its shared
    /// command row to the drawer and the IME instead of leaving Reset and Done underneath it.
    /// </summary>
    internal event Action<bool>? CompactEditorChanged;

    internal bool CompactEditorActive => _compactEditorActive;

    /// <summary>
    /// Moves the compact workspace command strip into the shell's landscape command row, or
    /// restores it to row zero of this workspace. A control has one visual parent, so the move
    /// is explicit and symmetric rather than duplicated UI with drifting enabled states.
    /// </summary>
    /// <remarks>
    /// The row holds one strip — the selected workspace's — and this is where that is enforced,
    /// because the move out of the workspace is the only thing that can put one there. A
    /// workspace view is <em>replaced</em> whenever the reader changes the device's text size
    /// (every font size in it is resolved while it is built), and a replaced view has already
    /// handed its strip to the shell, so nothing it does afterwards can take it back. Each
    /// change therefore left one more strip in the row, in the same cell, at byte-identical
    /// bounds: `Filters`, `Plot`, `Split`, `Details` and `Fit` twice after one change and three
    /// times after two, the stale copies belonging to a view that had stopped answering its
    /// session (finding F-39). Adopting clears whatever else is in the row; the only strip that
    /// can be there is one no live workspace is still hosting.
    /// </remarks>
    internal void HostCompactCommands(Panel? externalHost)
    {
        if (!_mobile || _mobileFilterShell is not { } strip)
        {
            return;
        }

        var external = externalHost is not null;
        if (_compactCommandsExternallyHosted == external &&
            (!external || ReferenceEquals(strip.Parent, externalHost)))
        {
            return;
        }

        if (strip.Parent is Panel current)
        {
            current.Children.Remove(strip);
        }

        if (externalHost is not null)
        {
            externalHost.Children.Clear();
            externalHost.Children.Add(strip);
        }
        else
        {
            Grid.SetRow(strip, 0);
            Grid.SetColumn(strip, 0);
            Grid.SetColumnSpan(strip, 1);
            _root.Children.Insert(0, strip);
        }

        _compactCommandsExternallyHosted = external;
        ApplyMobileLayout(Bounds.Size);
        _root.InvalidateMeasure();
        _root.InvalidateArrange();
    }

    /// <summary>
    /// Something the reader should be told about, for the application's notice lane.
    /// </summary>
    /// <remarks>
    /// The lane was driven only from <see cref="MainView"/>, so a workspace action had no
    /// route to it: <c>Copy raw</c> wrote the clipboard and returned, with no notice, no
    /// status change, and nothing from the platform either — the reader had no way at all to
    /// know whether it had worked (audit 2, C4). The workspace does not reach into the shell;
    /// it says what happened and the shell decides where that is shown.
    /// </remarks>
    internal event Action<string, bool>? NoticeRaised;

    /// <summary>
    /// A recovered partial needs durable choices, not only a dismissible warning. The shell
    /// owns export pickers, modal confirmation, tab closure, and cache deletion, so the
    /// workspace raises the message and lets the shell present those actions (F-19).
    /// </summary>
    internal event Action<string>? PartialRecoveryRaised;

    /// <summary>Reports the result of an action whose only other evidence is off screen.</summary>
    private void Notify(string message, bool failure = false) =>
        NoticeRaised?.Invoke(message, failure);

    /// <summary>The phone workspace mode, in the form settings.json stores.</summary>
    internal string DisplayMode => _mobileWorkspaceState.Persisted;

    /// <summary>Re-adopts the mode the reader had before this process started.</summary>
    /// <summary>The workspace mode currently in force, in its stored form.</summary>
    internal string CurrentDisplayMode => _mobileWorkspaceState.Persisted;

    internal void RestoreDisplayMode(string? persisted)
    {
        if (_mobile && _mobileWorkspaceState.Restore(persisted))
        {
            ApplyMobileLayout(Bounds.Size);
        }
    }

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
            // Only claim the key when there is something to dismiss. Claiming it
            // unconditionally is what made the Android Back gesture inert everywhere in the
            // app: the press was reported handled, so the platform never got to background
            // the task and the app could not be left at all (finding 20).
            if (!TryDismissTransientState())
            {
                return false;
            }

            return true;
        }

        if (!control && !alt && !textInputFocused && eventArgs.Key is Key.J or Key.K)
        {
            MoveEntrySelection(eventArgs.Key == Key.J ? 1 : -1);
            return true;
        }

        if (!control && !alt && !textInputFocused && eventArgs.Key == Key.Enter && _inspectedEntry is not null)
        {
            ShowInspector();
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

    /// <summary>
    /// Centres the view on the search match nearest a tapped instant.
    /// </summary>
    /// <remarks>
    /// The second affordance for finding F-07: the marker lane draws where the matches are, so
    /// aiming at one is the most direct thing a reader can do with it. The nearest match wins
    /// rather than the next one, because the reader pointed at a place rather than at a
    /// direction.
    /// </remarks>
    private async Task GoToNearestMatchAsync(InstantUs instant)
    {
        if (_viewModel.SearchResult?.Markers is not { Count: > 0 } markers ||
            _viewModel.Viewport is not { } viewport ||
            _viewModel.Snapshot?.TimedRange is not { } session)
        {
            return;
        }

        var nearest = markers[0];
        var bestDistance = long.MaxValue;
        foreach (var marker in markers)
        {
            var distance = Math.Abs(marker.Value - instant.Value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = marker;
            }
        }

        var span = Math.Min(viewport.DurationUs, session.DurationUs);
        var maximumStart = session.EndExclusive.Value - span;
        var start = Math.Clamp(nearest.Value - span / 2, session.StartInclusive.Value, maximumStart);
        await _viewModel.SetViewportAsync(
            new TimeRange(new InstantUs(start), new InstantUs(start + span))).ConfigureAwait(false);
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

    /// <summary>
    /// Closes the innermost thing the reader has opened — the filter drawer, an active query,
    /// a selected timeline bar, the filter itself — and reports whether there was one.
    /// </summary>
    /// <remarks>
    /// Shared by Escape and by the Android Back gesture, and the return value is what lets
    /// Back fall through to the platform when the workspace has nothing to give up.
    /// </remarks>
    internal bool TryDismissTransientState()
    {
        if (!HasTransientState())
        {
            return false;
        }

        _ = RunUiActionAsync(HandleEscapeAsync);
        return true;
    }

    private bool HasTransientState() =>
        _mobileFiltersOpen ||
        _search.IsFocused && !string.IsNullOrWhiteSpace(_viewModel.SearchText) ||
        _viewModel.DetailRange is not null ||
        _viewModel.Filter.Fingerprint() != FilterSpec.All.Fingerprint();

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

    /// <summary>
    /// What the entries pane says when the filters match nothing.
    /// </summary>
    /// <remarks>
    /// The plot handles this state well — every severity row is labelled 0, the counts read
    /// <c>0 in view · 0 match · 49,994 in session</c> — and the entries list, the largest
    /// region on the screen, became an empty rectangle about 700 px tall with no text, no
    /// explanation and no action. Its accessibility node was an empty <c>ListBox</c> named
    /// "Filtered log entries", so a screen-reader user was told nothing at all
    /// (finding F-06).
    ///
    /// Two states, because they have different answers. Nothing in the session matches, so
    /// the filter is what has to change; or something matches but not here, so the viewport
    /// is what has to change — and that second case already had an action, hidden in a row
    /// of buttons above the empty pane rather than in the empty pane itself.
    /// </remarks>
    /// <summary>Builds the previous/position/next cluster shown beside the match count.</summary>
    private Panel BuildMarkerNavigation()
    {
        Button Step(string glyph, string name, int direction)
        {
            var button = new Button
            {
                Content = glyph,
                MinHeight = TouchTarget.For(_mobile),
                MinWidth = TouchTarget.For(_mobile, 26),
                Padding = new Thickness(_mobile ? 6 : 8, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = false,
            };
            AutomationProperties.SetName(button, name);
            ToolTip.SetTip(button, _mobile ? name : $"{name} (F3 / Shift+F3)");
            button.Click += async (_, _) => await RunUiActionAsync(() => NavigateSearchMatchAsync(direction));
            return button;
        }

        _markerPrevious = Step("◀", "Previous search match", -1);
        _markerNext = Step("▶", "Next search match", 1);
        _markerPosition = new TextBlock
        {
            FontSize = TextScale.Of(11),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0),
        };

        var nav = _markerNav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children = { _markerPrevious, _markerPosition, _markerNext },
        };
        AutomationProperties.SetName(nav, "Search match navigation");
        return nav;
    }

    /// <summary>
    /// The line that appears when the entry being read is no longer among the rows on screen.
    /// </summary>
    /// <remarks>
    /// It is a marker and a way back, not a warning: nothing has gone wrong, the live window
    /// has simply moved past the record (finding F-25). It sits above the list, where the
    /// reader is looking for the row they lost, and the action puts Follow down first —
    /// following the live edge and holding a place in the past are opposite requests.
    /// </remarks>
    private Border BuildEntryOffPageBanner()
    {
        var text = _entryOffPageText = new TextBlock
        {
            FontSize = TextScale.Of(11),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var show = new Button
        {
            Content = "Show it",
            MinHeight = TouchTarget.For(_mobile),
            Margin = new Thickness(8, 0, 0, 0),
        };
        AutomationProperties.SetName(show, "Move the plot back to the entry being read");
        show.Click += async (_, _) => await RunUiActionAsync(ShowInspectedEntryAgainAsync);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { text, show },
        };
        Grid.SetColumn(show, 1);

        var banner = _entryOffPageBanner = new Border
        {
            IsVisible = false,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 2),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = row,
        };
        AutomationProperties.SetLiveSetting(banner, AutomationLiveSetting.Polite);
        return banner;
    }

    private Border BuildEmptyResultsCard()
    {
        var title = _emptyResultsTitle = new TextBlock
        {
            FontSize = TextScale.Of(14),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var detail = _emptyResultsDetail = new TextBlock
        {
            FontSize = TextScale.Of(12),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Opacity = 0.78,
        };

        var widen = _emptyResultsWiden = new Button
        {
            Content = "Show the matching range",
            MinHeight = TouchTarget.For(_mobile),
            IsVisible = false,
        };
        AutomationProperties.SetName(widen, "Move the timeline to the range that contains the matching entries");
        widen.Click += (_, _) => FitToMatches();

        var clear = _emptyResultsClear = new Button
        {
            Content = "Clear all filters",
            MinHeight = TouchTarget.For(_mobile),
        };
        AutomationProperties.SetName(clear, "Clear every active filter");
        clear.Click += async (_, _) =>
        {
            _search.Text = string.Empty;
            _selectedRange = null;
            _rangeActions.IsVisible = false;
            await RunUiActionAsync(_viewModel.ClearFiltersAsync);
            UpdateLevelChecks();
        };

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { widen, clear },
        };

        var card = _emptyResultsCard = new Border
        {
            IsVisible = false,
            Padding = new Thickness(16),
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { title, detail, actions },
            },
        };
        AutomationProperties.SetName(card, "No matching entries");
        AutomationProperties.SetLiveSetting(card, AutomationLiveSetting.Polite);
        return card;
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

        // A "Search" button beside a field that already searches as you type promises a step
        // that does not exist. On the desktop it is at least the visible statement of what
        // Enter does; on a phone the row is narrow and what a query field actually needs
        // there is a way to empty it again with one thumb (finding 28).
        var searchAction = new Button
        {
            Content = _mobile ? "✕" : "Search",

            // 48 dp on the phone, like every other touch target. The glyph is one character
            // wide, so the button had measured to it: 30.5 dp on the third device, the one
            // control in the product still under the floor F-03 and F-26 set — because those
            // audits measured the empty state, the workspace and the tab strip, and nobody
            // had opened the filter drawer while measuring.
            MinWidth = _mobile ? 48 : 0,
        };
        AutomationProperties.SetName(searchAction, _mobile ? "Clear the query" : "Apply the query");
        ToolTip.SetTip(
            searchAction,
            _mobile
                ? "Clear the query"
                : "Apply the query now; it also applies as you type");
        searchAction.Click += async (_, _) =>
        {
            if (_mobile)
            {
                _search.Text = string.Empty;
            }

            await RunUiActionAsync(ApplySearchAsync);
        };
        _search.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Avalonia.Input.Key.Enter)
            {
                var applied = await ApplySearchGuardedAsync();

                // On a phone the IME's action key is how a query is committed, and the drawer
                // it was typed into is covering the results. A query that was refused has not
                // been committed, so the drawer stays open on the field that needs fixing
                // (finding F-04).
                if (_mobile && applied)
                {
                    SetMobileFiltersOpen(false);
                }

                eventArgs.Handled = true;
            }
        };
        _search.TextChanged += (_, _) =>
        {
            if (_mobileSearchPlaceholder is { } placeholder)
            {
                placeholder.IsVisible = string.IsNullOrEmpty(_search.Text);
            }

            QueueDebouncedSearch();
        };
        _search.GotFocus += (_, _) =>
        {
            if (_mobile &&
                _mobileFiltersOpen &&
                _mobileLayoutMode == MobileWorkspaceMode.CompactHeight)
            {
                SetCompactEditorActive(true);
            }
        };

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
                FontSize = TextScale.Of(12),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(4),
                Theme = LevelToggleTheme,
            };
            ToolTip.SetTip(
                toggle,
                _mobile
                    ? $"{level}: tap to show or hide these entries"
                    : $"{level}: click to show or hide these entries");
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

        // A control's name is what it does; the glyph on it is not one. These read as "−" and
        // "+" with the useful string filed under the description, which is the wrong way round
        // — a name has to identify the control on its own (audit 3, B6).
        var zoomOut = new Button { Content = "−", Padding = new Thickness(9, 3) };
        ToolTip.SetTip(zoomOut, "Zoom out");
        AutomationProperties.SetName(zoomOut, "Zoom out");
        zoomOut.Click += (_, _) => _timeline.ZoomAtCenter(1.8);
        var fit = new Button { Content = "Fit", Padding = new Thickness(9, 3), IsVisible = !_mobile };

        // "(0)" is a keyboard shortcut, and a touch device has no keyboard to read it
        // against — a screen reader announced it as a bare zero (finding 14). The name and the
        // description were also near-duplicates of each other, so a reader heard the same
        // sentence twice (audit 3, E4); the description now says what the name cannot.
        AutomationProperties.SetName(fit, "Fit the complete session");
        ToolTip.SetTip(fit, _mobile ? "Show the whole session in the plot" : "Show the whole session in the plot (0)");
        fit.Click += (_, _) => _timeline.FitSession();
        var zoomIn = new Button { Content = "+", Padding = new Thickness(9, 3) };
        ToolTip.SetTip(zoomIn, "Zoom in");
        AutomationProperties.SetName(zoomIn, "Zoom in");
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
                         searchAction,
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

            var queryRow = _mobileQueryRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 8,
            };
            // Avalonia's built-in placeholder is measured at its natural text width and its
            // Android automation node keeps that width after the TextBox clips the pixels. On
            // a narrow landscape column the node therefore ran through the clear button even
            // though the glyphs did not (F-11). The visual hint is a stretched, clipped child
            // of the field's exact grid cell; accessibility gets one stable name on the field
            // instead of a second, geometrically false text node.
            _search.PlaceholderText = string.Empty;
            AutomationProperties.SetName(_search, "Search message text or regular expression");
            var queryField = new Grid { ClipToBounds = true };
            queryField.Children.Add(_search);
            var searchPlaceholder = _mobileSearchPlaceholder = new TextBlock
            {
                Text = "Search message text or regex…",
                Margin = new Thickness(11, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
                IsVisible = string.IsNullOrEmpty(_search.Text),
            };
            AutomationProperties.SetAccessibilityView(searchPlaceholder, AccessibilityView.Raw);
            AutomationProperties.SetIsControlElementOverride(searchPlaceholder, false);
            queryField.Children.Add(searchPlaceholder);
            queryRow.Children.Add(queryField);
            Grid.SetColumn(searchAction, 1);
            queryRow.Children.Add(searchAction);

            var queryOptions = _mobileQueryOptions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 8,
                LineSpacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
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

            var queryCaption = _mobileQueryCaption = MobileSectionLabel("QUERY");
            var querySection = _mobileQuerySection = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8),
                Children =
                {
                    queryCaption,
                    queryRow,
                    _searchProblem,
                    queryOptions,
                },
            };
            // The chips carry a letter each and their meaning lived only in a tooltip, which
            // a touch device never shows — so "?" in particular had no legend anywhere in the
            // mobile UI even though its automation name knew what it meant (finding 28).
            var legend = new System.Text.StringBuilder();
            foreach (var level in LogLevels.DisplayOrder)
            {
                if (legend.Length > 0)
                {
                    legend.Append("  ·  ");
                }

                legend.Append(LevelPalette.Label(level))
                    .Append(' ')
                    .Append(level.ToString().ToLowerInvariant());
            }

            var severityLegend = _severityLegend = new TextBlock
            {
                Text = legend.ToString(),
                FontSize = TextScale.Of(10),
                TextWrapping = TextWrapping.Wrap,
            };
            var severitySection = _mobileSeveritySection = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(8),
                Children =
                {
                    MobileSectionLabel("SEVERITY"),
                    levelGroup,
                    severityLegend,
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
            // The query section is not in here. It is the drawer's own first band, above the
            // scroller — see the panel grid below.
            var filterBody = _mobileFilterBody = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("*"),
                Children = { severitySection, timeSection },
            };
            Grid.SetRow(timeSection, 1);
            _mobileFilterScroll = new ScrollViewer
            {
                Content = filterBody,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

                // The drawer had no padding of its own, so at rest the Time lens buttons were
                // cut by the viewport edge, and one swipe put the QUERY heading under the
                // card's top border instead: the pane looked broken at one end or the other
                // whatever the reader did with it (audit 2, D1). A scrolling surface needs a
                // margin at both ends of its travel, not only between its sections.
                Padding = new Thickness(0, 6, 0, 10),
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
            doneFilters.Click += async (_, _) =>
            {
                // Done and the keyboard action are two presentations of the same commit.
                // Closing after a refused regex hides the explanation and leaves the reader
                // looking at unchanged results with no visible way to repair the input.
                // Keep the drawer on the invalid field; a valid or empty query closes it.
                if (await ApplySearchGuardedAsync())
                {
                    SetMobileFiltersOpen(false);
                }
            };
            var filterFooter = _mobileFilterFooter = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                Margin = new Thickness(8, 2, 8, 8),
                ColumnSpacing = 6,
                Children = { filterCount, resetFilters, doneFilters },
            };
            Grid.SetColumn(resetFilters, 1);
            Grid.SetColumn(doneFilters, 2);
            _mobileFilterFade = new FadingScrollHost(
                _mobileFilterScroll,
                ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light);
            // Three bands, not two. The middle one scrolls; the outer two are the query row
            // and the decision row, and neither may ever be the thing that scrolls away.
            //
            // The query row used to be the scroller's first child, which is fine until the
            // scroller is shorter than it. A landscape keyboard on the third device leaves the
            // whole drawer 93 dp; the pinned footer took 52 of them and the query row — the
            // one control the keyboard was raised for — was left 32 dp of its 48, sliced
            // across the middle at both ends with Regex and Case-sensitive cut beside it
            // (finding F-30). Scrolling cannot answer that: the row is taller than the
            // viewport it would scroll in.
            //
            // Structural, not conditional. Moving the section into this band only while the
            // keyboard was up meant reparenting a focused TextBox, which unmounts it, which
            // drops focus, which makes Avalonia withdraw the IME it had just asked for —
            // `showSoftInput` followed by `HIDE_SOFT_INPUT_BY_INSETS_API` in the same breath,
            // and a field that could not be typed into at all. That is the same trap
            // <see cref="ObserveInputPane"/> records from finding 1, one layer down. The band
            // exists in every state, so nothing ever moves.
            var filterPanelGrid = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children = { querySection, _mobileFilterFade, filterFooter },
            };
            Grid.SetRow(_mobileFilterFade, 1);
            Grid.SetRow(filterFooter, 2);

            // The drawer is a full card in the workspace band, not a flap hanging under the
            // toolbar. Capped at 520 px it ended a fifth of the screen above the bottom in
            // portrait, leaving a blank band over a workspace it had already hidden
            // (finding 21.6) — and with the keyboard open it had nowhere to go at all
            // (finding 1). Filling the band gives the query field, the severity chips and the
            // Reset/Done footer a fixed home at every viewport height.
            _mobileFilterPanel = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#2C4361")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(6, 2, 6, 4),
                Child = filterPanelGrid,
                IsVisible = false,
                ZIndex = 5,

                // A card that cannot hold everything it was asked to hold must cut it off at
                // its own edge, not paint it over the workspace underneath.
                ClipToBounds = true,
            };
            AutomationProperties.SetName(_mobileFilterPanel, "Search and timeline filters");

            // The drawer's room depends on where it has been arranged relative to the
            // keyboard, which only a completed layout pass knows (see ApplyInputPaneRoom).
            _mobileFilterPanel.LayoutUpdated += (_, _) => ApplyInputPaneRoom();

            _mobileFilterButton = new Button
            {
                Content = "Filters",
                MinWidth = 76,
                MinHeight = 48,
                Padding = new Thickness(10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _mobileFilterButton.Click += (_, _) => SetMobileFiltersOpen(!_mobileFiltersOpen);
            AutomationProperties.SetName(_mobileFilterButton, "Open search and timeline filters");

            // No fixed width. Filters (76) + this (190) + Fit (56) + two 6 px gaps came to
            // 334 against the 324 a 1080 px portrait phone actually has, so Fit fell to a
            // second full-height row and five buttons cost 306 px of a screen where the
            // entries list was getting 173 (audit 2, A2/D6). The three segments share
            // whatever the row has left instead.
            var modeSelector = _mobileModeSelector = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                MinWidth = 168,
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
                    VerticalContentAlignment = VerticalAlignment.Center,
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

            // Zooming is not filtering, and fitting the session is the most frequent thing
            // anyone does to a plot. It lived only inside a drawer labelled "Filters", two
            // taps deep, which is the wrong place for the action a reader reaches for most
            // (finding 14). The drawer keeps its TIME LENS group for discoverability and for
            // stepped zoom; this is the one-tap route back to the whole session.
            var mobileFit = _mobileFit = new Button
            {
                Content = "Fit",
                MinHeight = 48,
                MinWidth = 56,
                Padding = new Thickness(8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            // Not "Fit the complete session in the plot" — that is the name again with four
            // words on the end, and a screen reader reads both (audit 3, E4). The description
            // says the thing the name leaves out: which surface it acts on.
            ToolTip.SetTip(mobileFit, "Show the whole session in the plot");
            AutomationProperties.SetName(mobileFit, "Fit the complete session");
            mobileFit.Click += (_, _) => _timeline.FitSession();

            // Three fixed slots that always fit, rather than a wrap panel that decides how
            // many rows the workspace loses. The mode selector takes the slack, so Fit stays
            // beside it at every phone width, and nothing the reader aims at moves.
            var quickActions = _mobileQuickActions = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 6,
                Margin = new Thickness(6, 3, 6, 3),
                VerticalAlignment = VerticalAlignment.Center,
            };
            quickActions.Children.Add(_mobileFilterButton);
            Grid.SetColumn(modeSelector, 1);
            quickActions.Children.Add(modeSelector);
            Grid.SetColumn(mobileFit, 2);
            quickActions.Children.Add(mobileFit);
            AutomationProperties.SetName(quickActions, "Filters and workspace mode");

            // The capture controls are a band of their own, and the band only exists while
            // there is a capture: a session being read back costs one 48 dp row here rather
            // than two, and a live one still gets Follow, the new-data jump and Stop at full
            // width. Keeping them off the row above is also what stops a capture ending from
            // moving the mode buttons out from under a thumb (finding 26).
            var captureActions = _mobileCaptureActions = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 6,
                Margin = new Thickness(6, 0, 6, 3),
                IsVisible = false,
            };
            _follow.HorizontalAlignment = HorizontalAlignment.Stretch;
            _follow.MinHeight = 48;
            _newData.MinHeight = 48;
            _stopCapture.MinHeight = 48;
            captureActions.Children.Add(_follow);
            Grid.SetColumn(_newData, 1);
            captureActions.Children.Add(_newData);
            Grid.SetColumn(_stopCapture, 2);
            captureActions.Children.Add(_stopCapture);
            AutomationProperties.SetName(captureActions, "Live capture controls");

            var filterShell = _mobileFilterShell = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                ColumnDefinitions = new ColumnDefinitions("*"),
            };
            filterShell.Children.Add(quickActions);
            Grid.SetRow(captureActions, 1);
            filterShell.Children.Add(captureActions);
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
            desktopFilters.Children.Add(_searchProblem);
            desktopFilters.Children.Add(_regex);
            desktopFilters.Children.Add(_caseSensitive);
            desktopFilters.Children.Add(searchAction);
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
        _filterHost = filters;
        Grid.SetRow(filters, 0);
        root.Children.Add(filters);

        var chipBar = _chipBar;
        chipBar.Margin = new Thickness(10, 0, 10, 5);
        chipBar.LastChildFill = true;
        chipBar.MinHeight = _mobile ? 40 : 0;
        var clear = _clearFilters = new Button
        {
            Content = "Clear all",
            Margin = new Thickness(6, 0, 0, 0),
            MinHeight = _mobile ? 40 : 0,
            IsVisible = false,
        };
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
        var chipEmptyLabel = _chipEmptyLabel = new TextBlock
        {
            Text = "No filters · showing everything in view",
            FontSize = TextScale.Of(11),
            Opacity = 0.62,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(chipEmptyLabel, Dock.Left);
        chipBar.Children.Add(chipEmptyLabel);
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
            _rowSplitter = rowSplitter;
            Grid.SetRow(rowSplitter, 4);
            root.Children.Add(rowSplitter);
        }

        // A pane that overruns its cell paints over the band below it, which on a short
        // viewport put entry rows through the status line (audit 2, D9).
        var analysis = _analysisGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(_mobile ? "*" : "3*,6,2*"),
            Margin = new Thickness(10, 5),
            ClipToBounds = _mobile,
        };
        ConfigureEntryList();
        var entryPanel = new Grid
        {
            RowDefinitions = new RowDefinitions(_mobile ? "Auto,*,Auto" : "Auto,Auto,*,Auto"),
            ClipToBounds = _mobile,
        };
        var entryHeader = _entryHeader = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        };
        _order.SelectionChanged += (_, _) => _ = _viewModel.SetEntryOrderAsync(
            _order.SelectedIndex == 1 ? EntryOrder.SourceSequence : EntryOrder.Chronological);
        ToolTip.SetTip(_loadMore, $"Load the next {SessionTabViewModel.EntryPageSize:N0} matching rows");
        AutomationProperties.SetName(_loadMore, $"Load next {SessionTabViewModel.EntryPageSize:N0} matching rows");
        _loadMore.Click += async (_, _) => await RunUiActionAsync(() => _viewModel.LoadNextEntryPageAsync());
        var copyRaw = _copyRaw = new Button
        {
            Content = "Copy raw",
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(copyRaw, "Copy the raw text of the selected entries");
        copyRaw.Click += async (_, _) => await RunUiActionAsync(CopySelectedRawAsync);

        // A clipped row promises text it cannot show. This is the named way to reach it,
        // beside the actions that already act on the selected row, so the route does not
        // depend on the user guessing that a second tap or another tab holds the message.
        var openInspector = _openInspector = new Button
        {
            Content = _mobile ? "Entry ⤢" : "Full entry",
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(openInspector, "Show the selected entry's full message and source bytes (Enter)");
        AutomationProperties.SetName(openInspector, "Show the full message of the selected entry");
        openInspector.Click += (_, _) => ShowInspector();

        // Filters are session-wide but this table is not: when the filter matches
        // nothing inside the current view, offer the one action that reconciles them
        // instead of leaving an empty table next to a facet promising thousands of hits.
        ToolTip.SetTip(_fitMatches, "Move the timeline to the range that contains the matching entries");
        _fitMatches.Click += (_, _) => FitToMatches();
        ToolTip.SetTip(_clearScope, "Stop listing one selected cell and follow the visible time range again");
        _clearScope.Click += async (_, _) => await RunUiActionAsync(() => _viewModel.ClearDetailScopeAsync());

        Panel entryActions;
        if (_mobile)
        {
            foreach (var touchTarget in new Control[]
                     {
                         _order,
                         _loadMore,
                         copyRaw,
                         openInspector,
                         _fitMatches,
                         _clearScope,
                     })
            {
                touchTarget.MinHeight = 48;
                touchTarget.Margin = new Thickness(0);
            }

            // Fixed slots. These three are always present, so the sort dropdown, Copy raw
            // and the inspector never move: previously they shared one wrap panel with the
            // contextual actions, and `Load next 500` appearing pushed Copy raw onto a
            // second line — two taps in the same place hit different controls (finding 26).
            var primaryActions = _entryPrimaryActions = new Grid
            {
                // The sort selector owns its measured width; the two stable actions divide
                // everything left. Auto-sized action columns can demand more than a narrow
                // large-text viewport owns and are then arranged beyond the pane's edge.
                ColumnDefinitions = new ColumnDefinitions("Auto,*,*"),
                ColumnSpacing = 6,
            };
            _order.HorizontalAlignment = HorizontalAlignment.Left;
            primaryActions.Children.Add(_order);
            copyRaw.HorizontalAlignment = HorizontalAlignment.Stretch;
            copyRaw.MinWidth = TouchTarget.Minimum;
            Grid.SetColumn(copyRaw, 1);
            primaryActions.Children.Add(copyRaw);
            openInspector.HorizontalAlignment = HorizontalAlignment.Stretch;
            openInspector.MinWidth = TouchTarget.Minimum;
            Grid.SetColumn(openInspector, 2);
            primaryActions.Children.Add(openInspector);

            // Contextual actions get their own row below, so what they push is the table
            // rather than the controls above them. Load next 500 is not one of them: it
            // means "extend the list you have reached the end of", and it sat above the
            // list, in the band that was already costing the list every row it had
            // (audit 2, A2). It is a footer now; see BuildMobileEntryFooter.
            var contextActions = _entryContextActions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemSpacing = 6,
                LineSpacing = 6,
                Margin = new Thickness(0, 6, 0, 0),
                IsVisible = false,
                Children = { _fitMatches, _clearScope },
            };
            AutomationProperties.SetName(contextActions, "Actions for the current view");

            var actionShell = _entryActions = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            actionShell.Children.Add(primaryActions);
            Grid.SetRow(contextActions, 1);
            actionShell.Children.Add(contextActions);
            entryActions = actionShell;
        }
        else
        {
            _loadAll.Click += async (_, _) => await ToggleLoadAllEntriesAsync();
            _insightsToggle.Click += (_, _) => ToggleInsights();
            var dock = new DockPanel { LastChildFill = false };
            foreach (var control in new Control[]
                     {
                         _order,
                         _loadAll,
                         _loadMore,
                         _entryLoadStatus,
                         copyRaw,
                         openInspector,
                         _fitMatches,
                         _clearScope,
                         _insightsToggle,
                     })
            {
                DockPanel.SetDock(control, Dock.Right);
                dock.Children.Add(control);
            }

            entryActions = dock;
        }

        UpdateEntryLoadControls();

        entryHeader.Children.Add(_summary);
        Grid.SetRow(entryActions, 1);
        entryHeader.Children.Add(entryActions);
        var offPage = BuildEntryOffPageBanner();
        Grid.SetRow(offPage, 2);
        entryHeader.Children.Add(offPage);
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

        // Same cell as the list, so it occupies exactly the region that was blank.
        var emptyResults = BuildEmptyResultsCard();
        Grid.SetRow(emptyResults, _mobile ? 1 : 2);
        entryPanel.Children.Add(emptyResults);
        if (_mobile)
        {
            var footer = BuildMobileEntryFooter();
            Grid.SetRow(footer, 2);
            entryPanel.Children.Add(footer);
        }

        var templatePane = BuildTemplatePane();
        var rawPane = BuildEntryInspectorPane();
        if (_mobile)
        {
            // The header is a control rather than a string so that a label too wide for the
            // tab ends in an ellipsis instead of being sliced through a glyph: at the
            // device's 1.3x text size the three tabs read `Entrie`, `Insigh` and `Entr`, and
            // `Insights` cut to `Insigh` reads as a different word (F-41). The name stays the
            // whole word, so nothing a screen reader announces is trimmed with it.
            TabItem MobileDetailTab(string header, Control content)
            {
                var caption = new TextBlock
                {
                    Text = header,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var item = new TabItem
                {
                    Header = caption,
                    Content = content,
                    MinWidth = 92,
                    MinHeight = 48,
                    Padding = new Thickness(10, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                };
                AutomationProperties.SetName(item, header);
                ToolTip.SetTip(item, header);
                _mobileTabCaptions.Add(caption);
                return item;
            }

            // "Source" named the lower half of this pane and hid the half people actually
            // come for. It is the selected entry's inspector: message first, bytes below.
            _mobileAnalysisTabs = new TabControl
            {
                FontSize = TextScale.Of(14),
                Items =
                {
                    MobileDetailTab("Entries", entryPanel),
                    MobileDetailTab("Insights", templatePane),
                    MobileDetailTab("Entry", rawPane),
                },
            };
            AutomationProperties.SetName(_mobileAnalysisTabs, "Session detail views");
            analysis.Children.Add(_mobileAnalysisTabs);
            var summaryHost = _mobileSummaryHost = new Grid
            {
                Height = 42,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                IsVisible = false,
                ZIndex = 2,
            };
            analysis.Children.Add(summaryHost);
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

        // The filter drawer owns the same band the plot and the table do, so it can take
        // whatever height the viewport has left once the soft keyboard has taken its share.
        if (_mobileFilterPanel is { } drawer)
        {
            Grid.SetRow(drawer, 2);
            Grid.SetRowSpan(drawer, 4);
            root.Children.Add(drawer);
        }

        // Occupies the whole workspace band, but only ever while the session has nothing to
        // show; the panes above are hidden rather than covered (see UpdateFailureState).
        var failureCard = BuildFailureCard();
        Grid.SetRow(failureCard, 0);
        Grid.SetRowSpan(failureCard, 6);
        root.Children.Add(failureCard);

        _searchStatus.TextTrimming = TextTrimming.CharacterEllipsis;
        var statusBar = _statusBar = new DockPanel
        {
            Margin = new Thickness(8, 4, 8, _mobile ? 12 : 4),
            ClipToBounds = true,

            // A panel with a null background is not hit-testable in Avalonia, so the row's own
            // Tapped handler only ever fired where a child had painted a glyph: the chevron
            // advertising the gesture was dead, the empty space between the label and the
            // chevron was dead, and only the ~374 px of the label's own letters worked
            // (audit 3, C2). A transparent background is the whole row.
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var markerNav = BuildMarkerNavigation();
        DockPanel.SetDock(markerNav, Dock.Right);
        statusBar.Children.Add(markerNav);
        DockPanel.SetDock(_searchStatus, Dock.Right);
        statusBar.Children.Add(_searchStatus);
        statusBar.Children.Add(_status.Control);
        if (_mobile)
        {
            // One clipped line is the right density for a status that rewrites itself several
            // times a second, and the wrong answer when it ends mid-word on the part that
            // says what went wrong — "Failed · No supported logcat format could be detected
            // i…" had no continuation anywhere in the product (finding 21.7). Tapping the row
            // gives the whole sentence; tapping again gives the row back.
            //
            // And the row says so. It was tappable and looked exactly like every other line
            // of text in the product: only the accessible help text mentioned it, so a
            // sighted reader had no way to discover the one route to the end of a clipped
            // failure message (audit 2, E7).
            //
            // Shown only while the line is actually being trimmed. `Ready · 12,370 entries`
            // fits, so a chevron beside it promised more and delivered nothing — and the one
            // state the affordance exists for, a clipped failure message, looked identical to
            // the one where it does nothing at all (audit 3, C2). It is a mark rather than a
            // control: it takes no touch of its own — the row is the target — and it stays out
            // of the accessibility tree, where it had been reading as a lone "⌄".
            var statusChevron = _statusChevron = new TextBlock
            {
                Text = "⌄",
                FontSize = TextScale.Of(13),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                IsVisible = false,
            };
            AutomationProperties.SetAccessibilityView(statusChevron, AccessibilityView.Raw);
            AutomationProperties.SetIsControlElementOverride(statusChevron, false);
            DockPanel.SetDock(statusChevron, Dock.Right);
            statusBar.Children.Insert(0, statusChevron);
            statusBar.Tapped += (_, eventArgs) =>
            {
                // The row is a disclosure gesture and now also carries two buttons. A tap that
                // began on one of them is that button's, not the row's.
                if (eventArgs.Source is Visual source &&
                    _markerNav is { } nav &&
                    (ReferenceEquals(source, nav) || source.GetVisualAncestors().Contains(nav)))
                {
                    return;
                }

                if (_statusChevron?.IsVisible == true)
                {
                    SetStatusExpanded(!_statusExpanded);
                }
            };
            AutomationProperties.SetName(statusBar, "Session status");

            // The layout pass is what knows whether the line fitted, so the affordance is
            // settled there rather than guessed when the text was written.
            _status.LayoutUpdated += (_, _) => UpdateStatusAffordance();
            UpdateStatusAffordance();
        }

        Grid.SetRow(statusBar, 6);
        root.Children.Add(statusBar);
        return root;
    }

    /// <summary>
    /// The one action that belongs under a list rather than over it.
    /// </summary>
    /// <remarks>
    /// A reader reaches Load next 500 by running out of rows, and the row it was in cost a
    /// full 144 px touch band above the table for the whole time the table had more to give
    /// — on a screen where the table itself was getting 173 px (audit 2, A2). Below the list
    /// it is where the gesture ends, it is full width so a thumb cannot miss it, and it is
    /// out of the layout entirely whenever there is nothing further to load.
    /// </remarks>
    private Border BuildMobileEntryFooter()
    {
        _loadMore.HorizontalAlignment = HorizontalAlignment.Stretch;
        _loadMore.HorizontalContentAlignment = HorizontalAlignment.Center;
        _loadMore.Margin = new Thickness(0);
        _loadMore.MinHeight = 48;
        var footer = _entryFooter = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            Margin = new Thickness(0, 2, 0, 0),
            Child = _loadMore,
            IsVisible = false,
        };
        AutomationProperties.SetName(footer, "End of the loaded rows");
        return footer;
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
