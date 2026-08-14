using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Threading;
using VisualCat.Application.UseCases;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Presentation;

/// <summary>
/// The dominant mined pattern inside one hovered heat-map cell. It carries the cell it
/// was computed for so the view can reject an answer that arrived after the pointer
/// moved on (R15, §14.7).
/// </summary>
public sealed record CellPattern(TimeRange Range, LogLevel Level, string? TemplateText, long TemplateCount);

/// <summary>Filterable dimensions the facet panel exposes.</summary>
public enum FacetDimension
{
    Tag,
    Process,
    Pid,
    Tid,
    Buffer,
    Template,
}

/// <summary>How the current filter treats one facet value.</summary>
public enum FacetState
{
    Neutral,
    Included,
    Excluded,
}

/// <summary>
/// One facet value carried in whichever primitive its dimension stores, so the panel can
/// drive every dimension through a single toggle path instead of a method per dimension.
/// </summary>
public readonly record struct FacetKey
{
    private FacetKey(string text, int number, uint templateId)
    {
        Text = text;
        Number = number;
        TemplateId = templateId;
    }

    public static FacetKey OfText(string value) => new(value, 0, 0);
    public static FacetKey OfNumber(int value) => new(string.Empty, value, 0);
    public static FacetKey OfTemplate(uint value) => new(string.Empty, 0, value);

    public string Text { get; }
    public int Number { get; }
    public uint TemplateId { get; }
}

public sealed class SessionTabViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const long InitialFollowViewportUs = 30_000_000;
    public const int EntryPageSize = 500;
    private const int LoadAllBatchSize = 2_000;

    private readonly string _sessionPath;
    private readonly SessionViewStore _viewStore;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private CancellationTokenSource _queryCancellation = new();
    private SessionSnapshot? _snapshot;
    private HeatMapResult? _heatMap;
    private HeatMapResult? _overview;
    private StatisticsResult? _statistics;
    private SearchResult? _searchResult;
    private string _status = "Importing…";
    private string _searchStatus = string.Empty;
    private string _searchText = string.Empty;
    private FilterSpec _filter = FilterSpec.All;
    private TimeRange? _viewport;
    private bool _followLatest;
    private bool _isLiveCaptureActive;
    private bool _hasNewData;
    private EntryOrder _entryOrder = EntryOrder.Chronological;
    private string _rawContextText = string.Empty;
    private TimeRange? _detailRange;
    private LogLevel? _detailLevel;
    private long? _matchesInView;
    private CellPattern? _hoverPattern;
    private EntryCursor? _nextEntryCursor;
    private int _renderWidth = 1200;
    private long _queryGeneration;
    private int _entryLoadInProgress;
    private bool _viewStateLoaded;
    private bool _growInitialFollowViewport;
    private int _disposed;

    // Statistics and the minimap overview depend only on the snapshot generation and
    // filter — not the viewport — so a zoom or pan must not re-run seven full facet
    // scans per wheel notch. Keys are checked before querying (§12.1, §15.2).
    private string? _statisticsCacheKey;
    private string? _overviewCacheKey;
    private string? _templatesCacheKey;
    private CancellationTokenSource? _templateDebounce;
    private CancellationTokenSource? _hoverDebounce;

    public SessionTabViewModel(string title, string sessionPath)
    {
        Title = title;
        _sessionPath = sessionPath;
        _viewStore = new SessionViewStore(sessionPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SnapshotChanged;

    public string Title { get; }
    public string SessionPath => _sessionPath;
    public SessionSnapshot? Snapshot => _snapshot;
    public HeatMapResult? HeatMap { get => _heatMap; private set => Set(ref _heatMap, value); }
    public HeatMapResult? Overview { get => _overview; private set => Set(ref _overview, value); }
    public StatisticsResult? Statistics { get => _statistics; private set => Set(ref _statistics, value); }
    public SearchResult? SearchResult { get => _searchResult; private set => Set(ref _searchResult, value); }
    public ObservableCollection<NormalizedEntry> Entries { get; } = [];
    public ObservableCollection<TemplateSummary> Templates { get; } = [];
    public ObservableCollection<string> SavedViews { get; } = [];
    public string Status { get => _status; set => Set(ref _status, value); }
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }
    public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
    public FilterSpec Filter { get => _filter; private set => Set(ref _filter, value); }
    public TimeRange? Viewport { get => _viewport; private set => Set(ref _viewport, value); }
    public bool FollowLatest { get => _followLatest; set => Set(ref _followLatest, value); }
    public bool IsLiveCaptureActive { get => _isLiveCaptureActive; set => Set(ref _isLiveCaptureActive, value); }
    public bool HasNewData { get => _hasNewData; private set => Set(ref _hasNewData, value); }
    public EntryOrder EntryOrder { get => _entryOrder; private set => Set(ref _entryOrder, value); }
    public string RawContextText { get => _rawContextText; private set => Set(ref _rawContextText, value); }
    public bool CanLoadMore => _nextEntryCursor is not null;
    public bool IsLoadingEntries => Volatile.Read(ref _entryLoadInProgress) != 0;
    public int LoadedEntryCount => Entries.Count;
    public long RemainingEntryCount => Math.Max(0, (MatchesInView ?? Entries.Count) - Entries.Count);

    /// <summary>
    /// Time range the entry table is listing: a selected cell or range when one is
    /// active, otherwise the viewport. Kept across filter and search changes so narrowing
    /// a filter refines the selected cell instead of silently jumping back to the whole
    /// view (§14.7).
    /// </summary>
    public TimeRange? DetailRange { get => _detailRange; private set => Set(ref _detailRange, value); }
    public LogLevel? DetailLevel { get => _detailLevel; private set => Set(ref _detailLevel, value); }

    /// <summary>
    /// Entries matching the current filter inside <see cref="DetailRange"/>. Facet counts
    /// are whole-session, so this is what reconciles "8 132 in this session" with the
    /// rows the table can actually show (§14.11).
    /// </summary>
    public long? MatchesInView { get => _matchesInView; private set => Set(ref _matchesInView, value); }
    public CellPattern? HoverPattern { get => _hoverPattern; private set => Set(ref _hoverPattern, value); }

    public async Task LoadSnapshotAsync(bool final, CancellationToken cancellationToken = default)
    {
        var refreshUnchangedSnapshot = false;
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path.Combine(_sessionPath, "manifest.json")))
            {
                return;
            }

            var replacement = await SessionStore.OpenAsync(_sessionPath, cancellationToken).ConfigureAwait(false);
            if (_snapshot is not null &&
                (replacement.Generation < _snapshot.Generation ||
                 replacement.Generation == _snapshot.Generation &&
                 replacement.Descriptor == _snapshot.Descriptor))
            {
                replacement.Dispose();
                if (final && _snapshot is { } current)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Status = $"Ready · {current.Descriptor.Counters.TimedEntries:N0} entries · snapshot {current.Generation}";
                    });
                }

                refreshUnchangedSnapshot = final &&
                    _snapshot.TimedRange is not null &&
                    (HeatMap is null || Overview is null || Statistics is null);
                if (!refreshUnchangedSnapshot)
                {
                    return;
                }
            }

            if (!refreshUnchangedSnapshot)
            {
                SessionViewCatalog? restoredViews = null;
                if (!_viewStateLoaded)
                {
                    restoredViews = await _viewStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                    _viewStateLoaded = true;
                }

                SessionSnapshot? previous = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var priorRange = _snapshot?.TimedRange;
                    previous = Interlocked.Exchange(ref _snapshot, replacement);
                    if (restoredViews is not null)
                    {
                        SavedViews.Clear();
                        foreach (var view in restoredViews.Presets)
                        {
                            SavedViews.Add(view.Name);
                        }

                        if (restoredViews.Active is { } active)
                        {
                            ApplyViewState(active, replacement.TimedRange);
                        }
                    }

                    if (replacement.TimedRange is { } sessionRange)
                    {
                        if (Viewport is null)
                        {
                            if (FollowLatest)
                            {
                                var span = Math.Min(InitialFollowViewportUs, sessionRange.DurationUs);
                                Viewport = new TimeRange(
                                    new InstantUs(sessionRange.EndExclusive.Value - span),
                                    sessionRange.EndExclusive);
                                _growInitialFollowViewport = span < InitialFollowViewportUs;
                            }
                            else
                            {
                                Viewport = sessionRange;
                            }
                        }
                        else if (FollowLatest)
                        {
                            var span = _growInitialFollowViewport
                                ? Math.Min(InitialFollowViewportUs, sessionRange.DurationUs)
                                : Math.Min(Viewport.Value.DurationUs, sessionRange.DurationUs);
                            Viewport = new TimeRange(
                                new InstantUs(sessionRange.EndExclusive.Value - span),
                                sessionRange.EndExclusive);
                            _growInitialFollowViewport = span < InitialFollowViewportUs;
                            HasNewData = false;
                        }
                        else if (replacement.Descriptor.SourceKind is
                                     VisualCat.Domain.Sessions.SourceKind.Adb or
                                     VisualCat.Domain.Sessions.SourceKind.Android or
                                     VisualCat.Domain.Sessions.SourceKind.GrowingFile &&
                                 priorRange is { } oldRange &&
                                 sessionRange.EndExclusive > oldRange.EndExclusive &&
                                 Viewport is { } historical &&
                                 historical.EndExclusive < sessionRange.EndExclusive)
                        {
                            HasNewData = true;
                        }
                    }

                    Status = final
                        ? $"Ready · {replacement.Descriptor.Counters.TimedEntries:N0} entries · snapshot {replacement.Generation}"
                        : IsLiveCaptureActive
                            ? $"Capturing · {replacement.Descriptor.SourceDescription} · " +
                              $"{replacement.Descriptor.Counters.TimedEntries:N0} entries · snapshot {replacement.Generation}"
                            : $"Importing · {replacement.Descriptor.Counters.TimedEntries:N0} committed · snapshot {replacement.Generation}";
                    SnapshotChanged?.Invoke(this, EventArgs.Empty);
                });
                previous?.Dispose();
            }
        }
        finally
        {
            _loadLock.Release();
        }

        await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-runs every view query against the current snapshot, viewport, filter, and
    /// detail scope. The detail scope is deliberately not reset here: a filter, search,
    /// or level change refines what a selected cell shows rather than discarding the
    /// selection the timeline is still outlining.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var detailRange = _detailRange;
        var detailLevel = _detailLevel;
        var generation = Interlocked.Increment(ref _queryGeneration);
        var previous = Interlocked.Exchange(ref _queryCancellation, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        previous.Cancel();
        previous.Dispose();
        var token = _queryCancellation.Token;
        await _loadLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot;
            var viewport = Viewport;
            if (snapshot is null || viewport is null || viewport.Value.IsEmpty)
            {
                return;
            }

            var filter = Filter;
            var width = Math.Clamp(_renderWidth, 64, 4096);
            var fingerprint = filter.Fingerprint();
            var filterKey = $"{snapshot.Generation}|{fingerprint}";
            var refreshOverview = _overviewCacheKey != filterKey || Overview is null;
            var refreshStatistics = _statisticsCacheKey != filterKey || Statistics is null;
            var progress = new Progress<SearchProgress>(value =>
            {
                if (value.Identity.QueryGeneration == Volatile.Read(ref _queryGeneration))
                {
                    SearchStatus = value.Completed
                        ? $"{value.Matches:N0} search matches"
                        : $"Searching · {value.Progress:P0} · {value.Matches:N0} matches";
                }
            });
            var queryTask = Task.Run(() =>
            {
                var heat = SessionQueryEngine.QueryHeatMap(snapshot, new Viewport(viewport.Value, width), filter, generation, token);
                var overview = refreshOverview
                    ? SessionQueryEngine.QueryHeatMap(
                        snapshot,
                        new Viewport(snapshot.TimedRange ?? viewport.Value, 512),
                        filter,
                        generation,
                        token)
                    : null;
                var stats = refreshStatistics
                    ? SessionQueryEngine.QueryStatistics(snapshot, filter, generation, 20, token)
                    : null;
                var detailFilter = DetailFilter(filter, detailLevel);
                var details = SessionQueryEngine.GetEntries(
                    snapshot,
                    detailRange ?? viewport.Value,
                    detailFilter,
                    EntryOrder,
                    null,
                    500,
                    generation,
                    token);
                return (heat, overview, stats, details);
            }, token).ConfigureAwait(false);
            var searchTask = RunSearchAsync();
            var results = await queryTask;
            var searchResult = await searchTask.ConfigureAwait(false);
            if (generation != Volatile.Read(ref _queryGeneration) ||
                results.heat.Identity.SnapshotGeneration != _snapshot?.Generation)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HeatMap = results.heat;
                if (results.overview is { } overview)
                {
                    Overview = overview;
                    _overviewCacheKey = filterKey;
                }

                if (results.stats is { } stats)
                {
                    Statistics = stats;
                    _statisticsCacheKey = filterKey;
                }

                SearchResult = searchResult;
                if (searchResult is null)
                {
                    SearchStatus = string.Empty;
                }

                Entries.Clear();
                foreach (var entry in results.details.Entries)
                {
                    Entries.Add(entry);
                }

                MatchesInView = results.details.TotalCount;
                SetNextEntryCursor(results.details.NextCursor);
                NotifyEntryLoadCounts();
            });
            ScheduleTemplateRefresh(filterKey, viewport.Value, filter, generation, token);

            async Task<SearchResult?> RunSearchAsync()
            {
                if (filter.Search is not { } search)
                {
                    return null;
                }

                return await SessionQueryEngine.SearchAsync(
                    snapshot,
                    search,
                    filter with { Search = null },
                    generation,
                    progress,
                    cancellationToken: token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Points the entry table at one timeline cell or dragged range.</summary>
    public Task RefreshCellAsync(TimeRange range, LogLevel? level, CancellationToken cancellationToken = default)
    {
        DetailRange = range;
        DetailLevel = level;
        return RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Releases a cell or range scope so the entry table follows the viewport again.
    /// Returns whether a scope was actually released, so callers can skip a refresh.
    /// </summary>
    public bool ClearDetailScope()
    {
        if (_detailRange is null && _detailLevel is null)
        {
            return false;
        }

        DetailRange = null;
        DetailLevel = null;
        return true;
    }

    public async Task ClearDetailScopeAsync(CancellationToken cancellationToken = default)
    {
        if (ClearDetailScope())
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks for the dominant pattern inside one hovered cell. Debounced and superseded
    /// like the viewport ranking (§12.7): the pointer crosses many cells on the way to
    /// the one the user is actually reading, and only that one is worth a query.
    /// </summary>
    public void RequestCellPattern(TimeRange? range, LogLevel? level)
    {
        var debounce = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _hoverDebounce, debounce);
        previous?.Cancel();
        previous?.Dispose();
        if (range is not { } cell || level is not { } cellLevel)
        {
            HoverPattern = null;
            return;
        }

        if (HoverPattern is { } current && current.Range == cell && current.Level == cellLevel)
        {
            return;
        }

        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                await Task.Delay(140, debounce.Token).ConfigureAwait(false);
                await _loadLock.WaitAsync(debounce.Token).ConfigureAwait(false);
                IReadOnlyList<TemplateSummary> templates;
                try
                {
                    var snapshot = _snapshot;
                    if (snapshot is null)
                    {
                        return;
                    }

                    var filter = Filter;
                    templates = await Task.Run(
                        () => SessionQueryEngine.QueryTopTemplates(
                            snapshot,
                            cell,
                            filter,
                            1,
                            Volatile.Read(ref _queryGeneration),
                            cellLevel,
                            debounce.Token),
                        debounce.Token).ConfigureAwait(false);
                }
                finally
                {
                    _loadLock.Release();
                }

                var top = templates.Count > 0 ? templates[0] : null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!debounce.IsCancellationRequested)
                    {
                        HoverPattern = new CellPattern(cell, cellLevel, top?.CanonicalText, top?.Count ?? 0);
                    }
                });
            }
            catch (OperationCanceledException) when (debounce.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
                // The tab was disposed while the debounce was pending.
            }
        }
    }

    /// <summary>
    /// Filter for the entry table under a cell selection. A cell narrows to its own
    /// severity, but never re-admits a level the user has filtered out in the toolbar.
    /// </summary>
    private static FilterSpec DetailFilter(FilterSpec filter, LogLevel? detailLevel) =>
        detailLevel is { } level && (filter.IncludedLevels.Count == 0 || filter.IncludedLevels.Contains(level))
            ? filter with { IncludedLevels = ImmutableHashSet.Create(level) }
            : filter;

    /// <summary>
    /// Top templates are a scan over the viewport's index ranges, so they run only after
    /// interaction settles — debounced ~150 ms with superseded work cancelled (§12.7).
    /// The previous ranking stays visible until the new one is ready.
    /// </summary>
    private void ScheduleTemplateRefresh(
        string filterKey,
        TimeRange viewport,
        FilterSpec filter,
        long generation,
        CancellationToken cancellationToken)
    {
        var templatesKey = $"{filterKey}|{viewport.StartInclusive.Value}:{viewport.EndExclusive.Value}";
        if (_templatesCacheKey == templatesKey)
        {
            return;
        }

        var debounce = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _templateDebounce, debounce);
        previous?.Cancel();
        previous?.Dispose();
        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                await Task.Delay(150, debounce.Token).ConfigureAwait(false);
                if (generation != Volatile.Read(ref _queryGeneration))
                {
                    return;
                }

                await _loadLock.WaitAsync(debounce.Token).ConfigureAwait(false);
                IReadOnlyList<TemplateSummary> templates;
                try
                {
                    var snapshot = _snapshot;
                    if (snapshot is null || generation != Volatile.Read(ref _queryGeneration))
                    {
                        return;
                    }

                    templates = await Task.Run(
                        () => SessionQueryEngine.QueryTopTemplates(
                            snapshot,
                            viewport,
                            filter,
                            50,
                            generation,
                            cancellationToken: debounce.Token),
                        debounce.Token).ConfigureAwait(false);
                }
                finally
                {
                    _loadLock.Release();
                }

                if (generation != Volatile.Read(ref _queryGeneration))
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Templates.Clear();
                    foreach (var template in templates)
                    {
                        Templates.Add(template);
                    }

                    _templatesCacheKey = templatesKey;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Templates)));
                });
            }
            catch (OperationCanceledException) when (debounce.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
                // The tab was disposed while the debounce was pending.
            }
        }
    }

    public Task LoadNextEntryPageAsync(CancellationToken cancellationToken = default) =>
        LoadEntryPagesAsync(loadAll: false, cancellationToken);

    /// <summary>
    /// Loads every remaining row in bounded batches. Keeping the regular refresh at 500
    /// rows makes viewport changes cheap, while this explicit path gives the user full
    /// control over finite captures without turning one click into an unresponsive query.
    /// </summary>
    public Task LoadAllEntriesAsync(CancellationToken cancellationToken = default) =>
        LoadEntryPagesAsync(loadAll: true, cancellationToken);

    private async Task LoadEntryPagesAsync(bool loadAll, CancellationToken cancellationToken)
    {
        // Button events can be delivered twice before the first query reaches the lock.
        // Treat paging as one operation rather than queuing surprise extra pages.
        if (Interlocked.CompareExchange(ref _entryLoadInProgress, 1, 0) != 0)
        {
            return;
        }

        var lockHeld = false;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(NotifyEntryLoadState);
            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockHeld = true;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = _snapshot;
                var viewport = Viewport;
                var cursor = _nextEntryCursor;
                if (snapshot is null || viewport is null || cursor is null)
                {
                    return;
                }

                var generation = Volatile.Read(ref _queryGeneration);
                var filter = DetailFilter(Filter, _detailLevel);
                var pageSize = loadAll ? LoadAllBatchSize : EntryPageSize;
                var page = await Task.Run(
                    () => SessionQueryEngine.GetEntries(
                        snapshot,
                        _detailRange ?? viewport.Value,
                        filter,
                        EntryOrder,
                        cursor,
                        pageSize,
                        generation,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (generation != Volatile.Read(ref _queryGeneration) ||
                    snapshot.Generation != _snapshot?.Generation ||
                    !Equals(cursor, _nextEntryCursor))
                {
                    return;
                }

                var madeProgress = page.Entries.Count > 0 && !Equals(cursor, page.NextCursor);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var entry in page.Entries)
                    {
                        Entries.Add(entry);
                    }

                    SetNextEntryCursor(page.NextCursor);
                    NotifyEntryLoadCounts();
                });

                if (!loadAll || page.NextCursor is null || !madeProgress)
                {
                    return;
                }
            }
        }
        finally
        {
            if (lockHeld)
            {
                _loadLock.Release();
            }

            Interlocked.Exchange(ref _entryLoadInProgress, 0);
            await Dispatcher.UIThread.InvokeAsync(NotifyEntryLoadState);
        }
    }

    public Task SetViewportAsync(TimeRange viewport, bool manual = true)
    {
        if (manual)
        {
            FollowLatest = false;
            _growInitialFollowViewport = false;

            // Moving the view is a statement about what the user wants to look at, so a
            // cell selection made somewhere else is released rather than left governing
            // a table that no longer matches the plot.
            ClearDetailScope();
        }

        Viewport = viewport;
        if (_snapshot?.TimedRange is { } sessionRange &&
            viewport.EndExclusive >= sessionRange.EndExclusive)
        {
            HasNewData = false;
        }

        return RefreshAsync();
    }

    public Task SetRenderWidthAsync(int devicePixelWidth)
    {
        var width = Math.Clamp(devicePixelWidth, 64, 4096);
        if (Math.Abs(width - _renderWidth) < 24)
        {
            return Task.CompletedTask;
        }

        _renderWidth = width;
        return RefreshAsync();
    }

    public async Task ApplySearchAsync(bool regex, bool caseSensitive)
    {
        Filter = string.IsNullOrWhiteSpace(SearchText)
            ? Filter with { Search = null }
            : Filter with { Search = new TextSearchSpec(SearchText, regex, caseSensitive, TimeSpan.FromMilliseconds(250)) };
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task SetLevelAsync(LogLevel level, bool included)
    {
        var levels = Filter.IncludedLevels.Count == 0
            ? LogLevels.StorageOrder.ToArray().ToImmutableHashSet()
            : Filter.IncludedLevels;
        levels = included ? levels.Add(level) : levels.Remove(level);
        if (levels.Count == LogLevels.StorageOrder.Length)
        {
            levels = ImmutableHashSet<LogLevel>.Empty;
        }

        var filter = Filter with
        {
            IncludedLevels = levels,
        };
        if (_detailLevel is { } detailLevel &&
            filter.IncludedLevels.Count > 0 &&
            !filter.IncludedLevels.Contains(detailLevel))
        {
            ClearDetailScope();
        }

        Filter = filter;
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one facet action. Every facet control is a three-state toggle: clicking an
    /// action that is already in force removes it, and the two directions are mutually
    /// exclusive, so a value can always be returned to "not filtered" from the same
    /// control that filtered it (§14.11 — include and exclude semantics visible, and
    /// reversible).
    /// </summary>
    public Task ToggleFacetAsync(FacetDimension dimension, FacetKey value, bool exclude) =>
        UpdateFilterAsync(Apply(Filter, dimension, value, exclude));

    public FacetState StateOf(FacetDimension dimension, FacetKey value) => StateOf(Filter, dimension, value);

    /// <summary>Clears one or both directions of a facet dimension.</summary>
    /// <param name="dimension">The facet dimension to clear.</param>
    /// <param name="exclude">
    /// <c>null</c> removes both directions; <c>false</c> removes only the includes and
    /// <c>true</c> only the excludes, so a chip drops exactly what it names.
    /// </param>
    public Task ClearFacetDimensionAsync(FacetDimension dimension, bool? exclude = null) =>
        UpdateFilterAsync(Clear(Filter, dimension, exclude));

    public Task ClearLevelFilterAsync() =>
        UpdateFilterAsync(Filter with { IncludedLevels = ImmutableHashSet<LogLevel>.Empty });

    public Task IncludeTemplateAsync(uint templateId) =>
        ToggleFacetAsync(FacetDimension.Template, FacetKey.OfTemplate(templateId), exclude: false);

    public Task ExcludeTemplateAsync(uint templateId) =>
        ToggleFacetAsync(FacetDimension.Template, FacetKey.OfTemplate(templateId), exclude: true);

    public Task SetTimeRangeFilterAsync(TimeRange? range) => UpdateFilterAsync(Filter with { TimeRange = range });

    private static FacetState StateOf(FilterSpec filter, FacetDimension dimension, FacetKey value) =>
        dimension switch
        {
            FacetDimension.Tag => State(filter.IncludedTags.Contains(value.Text), filter.ExcludedTags.Contains(value.Text)),
            FacetDimension.Process => State(
                filter.IncludedProcesses.Contains(value.Text),
                filter.ExcludedProcesses.Contains(value.Text)),
            FacetDimension.Pid => State(filter.IncludedPids.Contains(value.Number), filter.ExcludedPids.Contains(value.Number)),
            FacetDimension.Tid => State(filter.IncludedTids.Contains(value.Number), filter.ExcludedTids.Contains(value.Number)),
            FacetDimension.Buffer => State(
                filter.IncludedBuffers.Contains(value.Text),
                filter.ExcludedBuffers.Contains(value.Text)),
            FacetDimension.Template => State(
                filter.IncludedTemplates.Contains(value.TemplateId),
                filter.ExcludedTemplates.Contains(value.TemplateId)),
            _ => FacetState.Neutral,
        };

    private static FacetState State(bool included, bool excluded) =>
        included ? FacetState.Included : excluded ? FacetState.Excluded : FacetState.Neutral;

    private static FilterSpec Apply(FilterSpec filter, FacetDimension dimension, FacetKey value, bool exclude)
    {
        var target = exclude ? FacetState.Excluded : FacetState.Included;
        var release = StateOf(filter, dimension, value) == target;
        return dimension switch
        {
            FacetDimension.Tag => filter with
            {
                IncludedTags = Toggle(filter.IncludedTags, value.Text, !exclude && !release),
                ExcludedTags = Toggle(filter.ExcludedTags, value.Text, exclude && !release),
            },
            FacetDimension.Process => filter with
            {
                IncludedProcesses = Toggle(filter.IncludedProcesses, value.Text, !exclude && !release),
                ExcludedProcesses = Toggle(filter.ExcludedProcesses, value.Text, exclude && !release),
            },
            FacetDimension.Pid => filter with
            {
                IncludedPids = Toggle(filter.IncludedPids, value.Number, !exclude && !release),
                ExcludedPids = Toggle(filter.ExcludedPids, value.Number, exclude && !release),
            },
            FacetDimension.Tid => filter with
            {
                IncludedTids = Toggle(filter.IncludedTids, value.Number, !exclude && !release),
                ExcludedTids = Toggle(filter.ExcludedTids, value.Number, exclude && !release),
            },
            FacetDimension.Buffer => filter with
            {
                IncludedBuffers = Toggle(filter.IncludedBuffers, value.Text, !exclude && !release),
                ExcludedBuffers = Toggle(filter.ExcludedBuffers, value.Text, exclude && !release),
            },
            FacetDimension.Template => filter with
            {
                IncludedTemplates = Toggle(filter.IncludedTemplates, value.TemplateId, !exclude && !release),
                ExcludedTemplates = Toggle(filter.ExcludedTemplates, value.TemplateId, exclude && !release),
            },
            _ => filter,
        };
    }

    private static FilterSpec Clear(FilterSpec filter, FacetDimension dimension, bool? exclude)
    {
        var includes = exclude is not true;
        var excludes = exclude is not false;
        return dimension switch
        {
            FacetDimension.Tag => filter with
            {
                IncludedTags = includes ? filter.IncludedTags.Clear() : filter.IncludedTags,
                ExcludedTags = excludes ? filter.ExcludedTags.Clear() : filter.ExcludedTags,
            },
            FacetDimension.Process => filter with
            {
                IncludedProcesses = includes ? filter.IncludedProcesses.Clear() : filter.IncludedProcesses,
                ExcludedProcesses = excludes ? filter.ExcludedProcesses.Clear() : filter.ExcludedProcesses,
            },
            FacetDimension.Pid => filter with
            {
                IncludedPids = includes ? filter.IncludedPids.Clear() : filter.IncludedPids,
                ExcludedPids = excludes ? filter.ExcludedPids.Clear() : filter.ExcludedPids,
            },
            FacetDimension.Tid => filter with
            {
                IncludedTids = includes ? filter.IncludedTids.Clear() : filter.IncludedTids,
                ExcludedTids = excludes ? filter.ExcludedTids.Clear() : filter.ExcludedTids,
            },
            FacetDimension.Buffer => filter with
            {
                IncludedBuffers = includes ? filter.IncludedBuffers.Clear() : filter.IncludedBuffers,
                ExcludedBuffers = excludes ? filter.ExcludedBuffers.Clear() : filter.ExcludedBuffers,
            },
            FacetDimension.Template => filter with
            {
                IncludedTemplates = includes ? filter.IncludedTemplates.Clear() : filter.IncludedTemplates,
                ExcludedTemplates = excludes ? filter.ExcludedTemplates.Clear() : filter.ExcludedTemplates,
            },
            _ => filter,
        };
    }

    private static ImmutableHashSet<T> Toggle<T>(ImmutableHashSet<T> values, T value, bool present) =>
        present ? values.Add(value) : values.Remove(value);

    public Task SetEntryOrderAsync(EntryOrder order)
    {
        EntryOrder = order;
        return RefreshAsync();
    }

    public async Task SaveCurrentViewAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Enter a name for the saved view.", nameof(name));
        }

        var views = await _viewStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var current = CaptureViewState(name.Trim());
        var presets = views.Presets
            .Where(view => !string.Equals(view.Name, current.Name, StringComparison.Ordinal))
            .Append(current)
            .OrderBy(static view => view.Name, StringComparer.Ordinal)
            .ToArray();
        await _viewStore.SaveAsync(CaptureViewState("Last view"), presets, cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SavedViews.Clear();
            foreach (var view in presets)
            {
                SavedViews.Add(view.Name);
            }
        });
    }

    public async Task DeleteSavedViewAsync(string name, CancellationToken cancellationToken = default)
    {
        var views = await _viewStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var remaining = views.Presets
            .Where(view => !string.Equals(view.Name, name, StringComparison.Ordinal))
            .ToArray();
        if (remaining.Length == views.Presets.Count)
        {
            return;
        }

        await _viewStore.SaveAsync(CaptureViewState("Last view"), remaining, cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SavedViews.Clear();
            foreach (var view in remaining)
            {
                SavedViews.Add(view.Name);
            }
        });
    }

    public async Task ApplySavedViewAsync(string name, CancellationToken cancellationToken = default)
    {
        var views = await _viewStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var view = views.Presets.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (view is null)
        {
            throw new InvalidOperationException($"Saved view '{name}' no longer exists.");
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyViewState(view, _snapshot?.TimedRange));
        await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task PersistViewAsync(CancellationToken cancellationToken = default) =>
        PersistViewCoreAsync(cancellationToken);

    public async Task LoadRawContextAsync(
        NormalizedEntry entry,
        int before = 5,
        int after = 5,
        CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot;
            if (snapshot is null)
            {
                return;
            }

            var records = SessionQueryEngine.GetRawContext(snapshot, entry.SourceSequence, before, after);
            if (records.Count == 0)
            {
                throw new InvalidDataException(
                    $"No published source record exists for entry {entry.SourceSequence}.");
            }
            var path = snapshot.RawPath;
            if (path is null || !File.Exists(path))
            {
                RawContextText = snapshot.Descriptor.Degraded
                    ? "Raw source is unavailable; the index remains queryable in degraded mode."
                    : "Raw source is unavailable.";
                return;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            var builder = new StringBuilder();
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = new byte[record.Raw.Length];
                stream.Position = record.Raw.Offset;
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                var marker = record.Sequence == entry.SourceSequence ? "▶" : " ";
                builder.Append(marker)
                    .Append(' ')
                    .Append(record.Sequence)
                    .Append(" [")
                    .Append(record.Outcome)
                    .Append("] ")
                    .Append(Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n'))
                    .AppendLine();
            }

            RawContextText = builder.ToString();
        }
        catch (EndOfStreamException)
        {
            RawContextText = "Raw source changed or is truncated; the indexed entry is still available.";
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string> ReadRawEntriesAsync(
        IEnumerable<NormalizedEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot;
            if (snapshot?.RawPath is not { } path || !File.Exists(path))
            {
                throw new InvalidOperationException("Raw source is unavailable.");
            }

            var selected = entries.OrderBy(static entry => entry.SourceSequence).Take(10_000).ToArray();
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            var builder = new StringBuilder();
            foreach (var entry in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Raw.Length > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException("A selected raw record exceeds the clipboard safety limit.");
                }

                var bytes = new byte[entry.Raw.Length];
                stream.Position = entry.Raw.Offset;
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                builder.Append(Encoding.UTF8.GetString(bytes));
            }

            return builder.ToString();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public Task ClearFiltersAsync()
    {
        Filter = FilterSpec.All;
        SearchText = string.Empty;
        return RefreshAsync();
    }

    public Task ToggleFollowAsync()
    {
        FollowLatest = !FollowLatest;
        _growInitialFollowViewport = false;
        if (!FollowLatest || _snapshot?.TimedRange is not { } session || Viewport is not { } viewport)
        {
            return Task.CompletedTask;
        }

        HasNewData = false;
        var span = Math.Min(viewport.DurationUs, session.DurationUs);
        return SetViewportAsync(
            new TimeRange(new InstantUs(session.EndExclusive.Value - span), session.EndExclusive),
            manual: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queryCancellation.Cancel();
        _queryCancellation.Dispose();
        var templateDebounce = Interlocked.Exchange(ref _templateDebounce, null);
        templateDebounce?.Cancel();
        templateDebounce?.Dispose();
        var hoverDebounce = Interlocked.Exchange(ref _hoverDebounce, null);
        hoverDebounce?.Cancel();
        hoverDebounce?.Dispose();
        try
        {
            await PersistViewCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _snapshot?.Dispose();
            _snapshot = null;
        }
        finally
        {
            _loadLock.Release();
        }

        _loadLock.Dispose();
    }

    private async Task UpdateFilterAsync(FilterSpec filter)
    {
        Filter = filter;
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task PersistViewCoreAsync(CancellationToken cancellationToken)
    {
        var existing = await _viewStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _viewStore.SaveAsync(CaptureViewState("Last view"), existing.Presets, cancellationToken)
            .ConfigureAwait(false);
    }

    private SessionViewState CaptureViewState(string name) =>
        new(name, Viewport, Filter, EntryOrder, FollowLatest);

    private void ApplyViewState(SessionViewState view, TimeRange? sessionRange)
    {
        ClearDetailScope();
        Filter = view.Filter;
        SearchText = view.Filter.Search?.Query ?? string.Empty;
        EntryOrder = view.EntryOrder;
        FollowLatest = view.FollowLatest;
        _growInitialFollowViewport = false;
        Viewport = ClampViewport(view.Viewport, sessionRange);
    }

    private static TimeRange? ClampViewport(TimeRange? saved, TimeRange? session)
    {
        if (session is null)
        {
            return null;
        }

        if (saved is null || !saved.Value.Overlaps(session.Value))
        {
            return session;
        }

        var clamped = saved.Value.Intersect(session.Value);
        return clamped.IsEmpty ? session : clamped;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void SetNextEntryCursor(EntryCursor? cursor)
    {
        if (Equals(_nextEntryCursor, cursor))
        {
            return;
        }

        _nextEntryCursor = cursor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoadMore)));
    }

    private void NotifyEntryLoadState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoadingEntries)));
        NotifyEntryLoadCounts();
    }

    private void NotifyEntryLoadCounts()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadedEntryCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemainingEntryCount)));
    }
}
