using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Threading;
using VisualCat.Application.UseCases;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain;
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

/// <summary>
/// Character range of the selected entry's own line inside the rendered raw context. The
/// context is one flat string so it stays selectable and copyable as a whole; the range is
/// what lets the inspector mark that one line and scroll to it instead of leaving the
/// reader to find a bare marker in eleven wrapped lines (§14.9).
/// </summary>
public readonly record struct RawContextMarker(int Offset, int Length);

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

    /// <summary>
    /// The narrowest window a live tail is ever given, in microseconds.
    /// </summary>
    /// <remarks>
    /// A session holding one entry has a one-microsecond span, and the plot drew it: the
    /// header read <c>DENSITY · 1 µs · 1,34 ns/px</c> and both axis labels printed the same
    /// instant to the microsecond. That is a statement of precision the data cannot support,
    /// and Follow did not widen it as more lines arrived — the window stayed as narrow as
    /// the session that produced it (audit 2, C6). Two seconds is a span a reader can read
    /// honestly and a live capture grows out of within its first breath.
    /// </remarks>
    internal const long MinimumViewportUs = 2_000_000;

    /// <remarks>
    /// Only the follow window takes this floor. A reader who deliberately zooms into a
    /// 300 µs burst has asked for exactly that and the plot's own pixel-resolution limit
    /// already governs them; a session whose whole span is 300 ms is honestly drawn at
    /// 300 ms. What is not honest is a <em>live tail</em> that inherits the span of the one
    /// entry it has so far and then never widens (audit 2, C6).
    /// </remarks>

    /// <summary>Silence after which a capture stops looking quiet and starts looking broken.</summary>
    private const int StarvedCaptureSeconds = 20;
    public const int EntryPageSize = 500;
    private const int LoadAllBatchSize = 2_000;

    /// <summary>
    /// Maximum rows retained by the bound entry collection. Android receives the smaller
    /// budget because process death under memory pressure cannot be caught or explained.
    /// </summary>
    public static int EntryRetentionLimit => EntryRetentionLimitOverride ?? (OperatingSystem.IsAndroid() ? 25_000 : 100_000);

    /// <summary>
    /// Forces a smaller row window, for tests that need to reach the ceiling.
    /// </summary>
    /// <remarks>
    /// Everything about the limit-reached state — the label, the footer band, the sentence,
    /// the stopped cursor — is only observable once a session outgrows the window, and
    /// building a hundred thousand rows to see it would make the test slower than the
    /// behaviour it checks. Null means "ask the platform", which is what every shipping
    /// build does.
    /// </remarks>
    internal static int? EntryRetentionLimitOverride { get; set; }

    /// <summary>Shortens the production timeout only for deterministic timeout tests.</summary>
    internal static TimeSpan? SearchRegexTimeoutOverride { get; set; }

    private readonly string _sessionPath;
    private readonly SessionViewStore _viewStore;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>
    /// Cancelled at the top of <see cref="DisposeAsync"/>, before it waits for
    /// <see cref="_loadLock"/>.
    /// </summary>
    /// <remarks>
    /// Every reader that can hold the load lock for an unbounded time takes a token linked
    /// to this one. Paging every row of a view is the case that mattered: it holds the lock
    /// across the whole walk, its only cancellation lived in the view that started it, and
    /// nothing cancelled that when the tab closed — so <see cref="DisposeAsync"/> queued
    /// behind a load that was still appending rows to the session it was tearing down, and
    /// the tab stayed on screen until the walk finished because
    /// <c>WorkspaceViewModel.CloseAsync</c> raises <c>TabRemoved</c> only after disposal
    /// returns. Closing the application went the same way, with no window left to explain
    /// why the process would not exit.
    /// </remarks>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>
    /// A source cancelled by <paramref name="cancellationToken"/> or by the session
    /// closing, or null when the session has already closed.
    /// </summary>
    /// <remarks>
    /// The null answer is not only the <see cref="IsDisposed"/> check: disposal can also
    /// complete between that check and this call, and
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, CancellationToken)"/>
    /// throws <see cref="ObjectDisposedException"/> on a disposed source — including one
    /// that was cancelled before it was disposed, which is the order
    /// <see cref="DisposeAsync"/> uses. Callers treat null the way they treat a session
    /// that closed underneath them, which is what it is.
    /// </remarks>
    private CancellationTokenSource? LinkToLifetime(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return null;
        }

        try
        {
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private readonly object _queryCancellationGate = new();
    private CancellationTokenSource? _queryCancellation = new();
    private SessionSnapshot? _snapshot;
    private HeatMapResult? _heatMap;
    private HeatMapResult? _overview;
    private StatisticsResult? _statistics;
    private SearchResult? _searchResult;
    private string _status = "Importing…";
    private string _searchStatus = string.Empty;
    private bool _searchInProgress;
    private SessionCompletion _completion = SessionCompletion.Complete;
    private string _durableStatus = string.Empty;
    private long _transientStatusGeneration = -1;
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
    private SessionActivity _activity = SessionActivity.Idle;
    private string? _failureReason;
    private string? _failureRemedy;
    private int _disposed;

    // True until the reader first states what they want to look at. See ViewportIsAuto.
    private bool _viewportIsAuto = true;

    // Statistics and the minimap overview depend only on the snapshot generation and
    // filter — not the viewport — so a zoom or pan must not re-run seven full facet
    // scans per wheel notch. Keys are checked before querying (§12.1, §15.2).
    private string? _statisticsCacheKey;
    private string? _overviewCacheKey;
    private string? _templatesCacheKey;
    private CancellationTokenSource? _templateDebounce;
    private CancellationTokenSource? _hoverDebounce;

    // Live-capture progress is only reported when a chunk actually arrives, and the rate it
    // carried was an average over the whole session. A capture that burst at start and then
    // went quiet therefore sat on "36/s" for minutes while nothing was arriving, which is
    // the strongest possible claim that data is streaming. These track a recent rate and
    // how long the source has been silent, and a heartbeat says so once it is.
    private DispatcherTimer? _captureHeartbeat;
    private string _captureScope = string.Empty;
    private long _captureLines;
    private long _captureCommittedLines;
    private long _captureLastAdvanceMs;
    private long _captureWindowLines;
    private long _captureWindowMs;
    private double _captureRate;
    private string? _captureHealthWarning;
    private string? _captureScopeSummary;
    private string? _captureScopeRemedy;
    private string? _lastCaptureWarning;
    private string? _captureFailure;
    private string? _refreshFailure;
    private string? _captureConnectionSummary;
    private string? _captureConnectionDetail;
    private bool _captureStreamEstablished;
    private string _captureNoRecordsSummary = "waiting for the source to log something";
    private int _liveSegmentCount;

    // A stop is not an instant: the pipeline still has to drain what it has read, compact
    // the segments, write the index, and reopen the finished session — on a four-hour phone
    // capture that is tens of seconds of work after the last line arrives. These carry the
    // stop across that window so the status line can keep describing it. See BeginStop.
    //
    // Volatile because the button presses on the UI thread and everything that answers it
    // runs off one: the pipeline's progress callbacks have no synchronization context to
    // return to, and the capture completes on a thread-pool thread. A reader that missed the
    // flag would let exactly the report through that this exists to suppress.
    private volatile bool _stopping;
    private long _stopStartedMs;
    private volatile string _stopPhase = string.Empty;

    /// <summary>
    /// Why this capture may look empty, when the reason is the scope it was granted rather
    /// than anything going wrong. Android restricts an unprivileged app to its own log
    /// records, and an idle app writes none — indistinguishable, without being told, from a
    /// capture that has broken.
    ///
    /// Two lengths because the surfaces have two widths: the status bar is one clipped line
    /// and can only carry a marker, while the empty plot and the session pane both wrap and
    /// can carry the command that actually fixes it.
    /// </summary>
    public string? CaptureScopeSummary
    {
        get => _captureScopeSummary;
        set => Set(ref _captureScopeSummary, value);
    }

    /// <summary>
    /// The session this one is waiting on for a reading slot, or null when it is not queued.
    /// </summary>
    /// <remarks>
    /// Observable because the empty plot carries it: a queued session used to say only that
    /// "another log is being read first", which named nothing and showed no progress, so with
    /// two imports on screen the reader could not tell which one was moving (audit 3, E4).
    /// </remarks>
    public string? QueuedBehind
    {
        get => _queuedBehind;
        set => Set(ref _queuedBehind, value);
    }

    /// <summary>The full explanation, including the one route out of a restricted scope.</summary>
    /// <remarks>
    /// Observable, because on Android the answer arrives several seconds into the capture
    /// rather than before it: the platform only tells the app what it can see by showing it
    /// (audit 2, C1). The empty plot and the session pane both carry this sentence, and both
    /// have to be able to acquire it late.
    /// </remarks>
    public string? CaptureScopeRemedy
    {
        get => _captureScopeRemedy;
        set => Set(ref _captureScopeRemedy, value);
    }

    private string? _queuedBehind;

    /// <summary>
    /// Trouble the capture is currently working through without losing data, in the
    /// reader's terms.
    /// </summary>
    /// <remarks>
    /// A capture that is quietly failing to write, or a view that has quietly stopped
    /// refreshing, used to look identical to a healthy quiet capture: the previous
    /// failure of this kind was invisible for fourteen minutes and then surfaced as a
    /// raw file path in a dead session. Anything the app recovers from is said out loud
    /// while it is still recoverable, and clears itself the moment it stops being true
    /// (§10.8, §15.2).
    /// </remarks>
    public string? CaptureHealthWarning
    {
        get => _captureHealthWarning;
        private set => Set(ref _captureHealthWarning, value);
    }

    /// <summary>Gets how many segments the live session currently holds, for the session pane.</summary>
    public int LiveSegmentCount
    {
        get => _liveSegmentCount;
        private set => Set(ref _liveSegmentCount, value);
    }

    public SessionTabViewModel(string title, string sessionPath)
    {
        Title = title;
        _sessionPath = sessionPath;
        _viewStore = new SessionViewStore(sessionPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SnapshotChanged;

    /// <summary>
    /// Raised on the UI thread immediately before <see cref="Entries"/> is emptied and
    /// refilled by a refresh. A list control clears its selection when the collection
    /// resets, which during a live capture silently deselected whatever the user was
    /// reading every time a snapshot landed. The pair of events lets a view tell that
    /// reset apart from a deliberate deselection.
    /// </summary>
    public event EventHandler? EntriesReloading;

    /// <summary>Raised on the UI thread once <see cref="Entries"/> has been refilled.</summary>
    public event EventHandler? EntriesReloaded;

    public string Title { get; }
    public string SessionPath => _sessionPath;
    public SessionSnapshot? Snapshot => _snapshot;

    /// <summary>
    /// Whether this session has been closed.
    /// </summary>
    /// <remarks>
    /// A view reacts to this tab through the dispatcher, so a notification raised before the
    /// tab closed can still be waiting in the queue after it has: the job then redraws from a
    /// session that is being torn down. Set before anything is released, so a queued job that
    /// has not started yet can decline to start.
    /// </remarks>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// Whether the session holds any entry whose severity could not be determined, which is
    /// what decides if the plot carries an Unknown lane. Session-stable, so panning cannot add
    /// a lane and move every other one under the pointer.
    /// </summary>
    public bool HasUnknownLevelEntries { get; private set; }
    public HeatMapResult? HeatMap { get => _heatMap; private set => Set(ref _heatMap, value); }
    public HeatMapResult? Overview { get => _overview; private set => Set(ref _overview, value); }
    public StatisticsResult? Statistics { get => _statistics; private set => Set(ref _statistics, value); }
    public SearchResult? SearchResult { get => _searchResult; private set => Set(ref _searchResult, value); }
    public ObservableCollection<NormalizedEntry> Entries { get; } = [];
    public ObservableCollection<TemplateSummary> Templates { get; } = [];
    public ObservableCollection<string> SavedViews { get; } = [];
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>
    /// Reports the outcome of something the reader just did — a copy, a cancelled load, a
    /// query that could not run — on the same line the session describes itself on.
    /// </summary>
    /// <remarks>
    /// Five view routes used to poke the status <see cref="Avalonia.Controls.TextBlock"/>
    /// directly. Both symptoms followed from that, and both were on screen at once: only the
    /// visible text was written, so a screen reader kept being told <c>Ready · 49,994 entries</c>
    /// while the line read <c>Failed · …</c>; and because the view model's own status never
    /// changed, nothing ever refreshed the line again, so the failure survived a successful
    /// query, a cleared filter and a mode switch — for the life of the tab (finding F-05).
    /// A transient message is now a view-model state like any other, so it reaches the
    /// accessible name by the same route as everything else, and it carries the query
    /// generation that produced it: when a newer query lands, the message it superseded is
    /// dropped and the session's own description comes back.
    /// </remarks>
    public void ReportTransientStatus(string text)
    {
        _transientStatusGeneration = Volatile.Read(ref _queryGeneration);
        Status = text ?? string.Empty;
    }

    /// <summary>Drops a transient message and restores what the session says about itself.</summary>
    public void ClearTransientStatus()
    {
        if (_transientStatusGeneration < 0)
        {
            return;
        }

        _transientStatusGeneration = -1;
        Status = _durableStatus;
    }

    /// <summary>Whether the status line is currently showing a transient message.</summary>
    internal bool HasTransientStatus => _transientStatusGeneration >= 0;

    /// <summary>Whether the session's own acquisition finished.</summary>
    public SessionCompletion Completion { get => _completion; private set => Set(ref _completion, value); }

    /// <summary>
    /// Reports a session that has just been opened or finished, in the tense its manifest
    /// earns rather than in the one the open path assumed.
    /// </summary>
    /// <remarks>
    /// Every finished load reported <see cref="SessionActivity.Ready"/>. A capture whose
    /// process was killed reopens with a manifest that was never finalized, and it reported
    /// <c>Ready · 1,173 entries</c> — while Recents called the same session <c>interrupted</c>
    /// and Session info called it <c>Importing</c> (finding F-19). One derived fact, one
    /// wording, everywhere.
    /// </remarks>
    /// <summary>
    /// Says the session is being brought onto the screen, before it is.
    /// </summary>
    /// <remarks>
    /// The loading tense also keeps the entries pane's own empty-result card from claiming
    /// that nothing matches during the frame before the first page is bound (finding F-06
    /// meeting finding F-18).
    /// </remarks>
    private void BeginOpening(VisualCat.Domain.Sessions.SessionDescriptor descriptor)
    {
        Completion = SessionCompletionText.Of(descriptor.Finalized, IsLiveCaptureActive);
        ReportActivity(
            SessionActivity.Opening,
            $"Opening · {Counted.Entries(descriptor.Counters.TimedEntries)}");
    }

    private void ReportOpened(VisualCat.Domain.Sessions.SessionDescriptor descriptor)
    {
        var completion = SessionCompletionText.Of(descriptor.Finalized, IsLiveCaptureActive);
        Completion = completion;
        ReportActivity(
            completion == SessionCompletion.RecoverablePartial
                ? SessionActivity.RecoverablePartial
                : SessionActivity.Ready,
            SessionCompletionText.OpenedStatus(completion, descriptor.Counters.TimedEntries));
    }

    /// <summary>
    /// What the tab is doing, for views that need to switch on it rather than read it.
    /// Always assigned before <see cref="Status"/>, so a view reacting to the status change
    /// already sees the state the new wording describes.
    /// </summary>
    public SessionActivity Activity { get => _activity; private set => Set(ref _activity, value); }

    /// <summary>Whether a capture or import is still in flight.</summary>
    public bool IsSessionWorkInFlight => Activity is
        SessionActivity.Queued or
        SessionActivity.Opening or
        SessionActivity.Importing or
        SessionActivity.Connecting or
        SessionActivity.Starting or
        SessionActivity.Capturing or
        SessionActivity.Stopping;

    /// <summary>
    /// Whether a live source is currently attached, which is what makes Follow and the
    /// new-data affordance mean anything. A finished capture is history: it cannot grow, so
    /// there is nothing to follow and nothing new to jump to (finding 27).
    /// </summary>
    public bool IsLiveSourceAttached =>
        IsLiveCaptureActive ||
        Activity is SessionActivity.Connecting
            or SessionActivity.Starting
            or SessionActivity.Capturing
            or SessionActivity.Stopping;

    /// <summary>
    /// Why this session has nothing to show, in full, and what the reader can do next on
    /// this platform.
    /// </summary>
    /// <remarks>
    /// The status bar is one clipped line, and for an import that never produced a session
    /// it was the only place the failure appeared: the workspace still built a complete set
    /// of panes over an empty store, so a failed import looked like a working session whose
    /// data had not arrived yet, and the actionable half of the message was exactly the half
    /// the ellipsis ate (finding 10). The workspace shows this instead of those panes.
    /// </remarks>
    public string? FailureReason { get => _failureReason; private set => Set(ref _failureReason, value); }

    /// <summary>The next step, when there is one this platform can actually offer.</summary>
    public string? FailureRemedy { get => _failureRemedy; private set => Set(ref _failureRemedy, value); }

    /// <summary>Records that the session ended in a failure, with the whole reason.</summary>
    public void ReportFailure(string reason, string? remedy)
    {
        FailureReason = reason;
        FailureRemedy = remedy;
        ReportCaptureFailure(remedy is { Length: > 0 } ? $"{reason} {remedy}" : reason);
        ReportActivity(SessionActivity.Failed, $"Failed · {reason}");
    }

    /// <summary>Records the state and the sentence that describes it together.</summary>
    /// <remarks>
    /// A stop, once begun, outranks anything that describes a running capture. Two things
    /// go on describing one after the reader has pressed the button — the pipeline's own
    /// progress reports, which keep arriving while the read-ahead drains, and the capture
    /// heartbeat, which fires every second the source is quiet and a stopped source is
    /// quiet by definition. Both used to win: the button sprang back from "Stopping…" to
    /// "Stop capture" within a second and the status line went back to "Capturing", so a
    /// stop that was working looked like a press that had not registered, and the only
    /// thing left to try was pressing it again. While <see cref="IsStopping"/> holds, only
    /// the stop's own wording and the states that end the session are accepted.
    /// </remarks>
    public void ReportActivity(SessionActivity activity, string status)
    {
        if (_stopping && activity is not (
                SessionActivity.Stopping or SessionActivity.Stopped or SessionActivity.Failed))
        {
            return;
        }

        Activity = activity;
        _durableStatus = status;
        _transientStatusGeneration = -1;
        Status = status;
        if (activity is SessionActivity.Ready or SessionActivity.Stopped or SessionActivity.Failed)
        {
            // Nothing is arriving any more, so an offer to jump to newly arrived data is a
            // promise about a source that has closed.
            HasNewData = false;
        }

        if (activity is SessionActivity.Stopped or SessionActivity.Failed)
        {
            // The session has landed somewhere final; the stop is over and later reports —
            // reopening this session from disk, say — describe it freely again.
            _stopping = false;
        }
    }

    /// <summary>
    /// Whether a stop is under way: the source has been told to end and the session is
    /// draining, compacting, finalizing, or being reopened.
    /// </summary>
    public bool IsStopping => _stopping;

    /// <summary>
    /// Records that this capture is ending, and starts describing the ending.
    /// </summary>
    /// <remarks>
    /// Called for every graceful ending — the reader pressing Stop and a timed capture
    /// reaching its duration alike — because both drain the same pipeline and both used to
    /// spend that drain claiming to be capturing. Idempotent: a second press, or the
    /// duration elapsing on a capture the reader has already stopped, keeps the elapsed
    /// clock the first one started rather than restarting it.
    /// </remarks>
    public void BeginStop(string? phase = null)
    {
        if (!_stopping)
        {
            // Published by the volatile write below, which is why it comes last: a thread
            // that sees the flag is guaranteed to see the clock and the phase it belongs to.
            _stopStartedMs = Environment.TickCount64;
            _stopPhase = phase ?? "saving the last of the capture";
            _stopping = true;
        }
        else if (phase is { Length: > 0 })
        {
            _stopPhase = phase;
        }

        ReportStop();
        StartCaptureHeartbeat();
    }

    /// <summary>
    /// Names the part of the ending the session has reached, for the phases the view model
    /// drives itself — reopening the finished session, which happens after the pipeline has
    /// reported everything it is going to.
    /// </summary>
    public void ReportStopPhase(string phase)
    {
        if (!_stopping)
        {
            return;
        }

        _stopPhase = phase;
        ReportStop();
    }

    /// <summary>
    /// Turns one pipeline progress report into what the reader is waiting for, while a stop
    /// is under way.
    /// </summary>
    /// <remarks>
    /// The stages are the pipeline's own (§5.5) and each one is a different answer to "what
    /// is it doing?": still writing out lines it had already read, merging the segments those
    /// lines went into, or writing the index that makes the session reopenable. Reporting a
    /// line count that is still climbing is what distinguishes a stop that is working from
    /// one that is stuck, so the backlog is named whenever there is one.
    /// </remarks>
    public void ReportStopProgress(VisualCat.Domain.Sessions.ProgressSnapshot progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!_stopping)
        {
            return;
        }

        LiveSegmentCount = progress.SegmentCount;
        _captureLines = progress.LinesRead;
        _captureCommittedLines = progress.LinesCommitted;
        UpdateCaptureHealth(progress.Warning);
        var pending = Math.Max(0, progress.LinesRead - progress.LinesCommitted);
        _stopPhase = progress.Stage switch
        {
            VisualCat.Domain.Sessions.IngestStage.Compacting => "compacting the session",
            VisualCat.Domain.Sessions.IngestStage.Finalizing => "writing the session index",
            _ when pending > 0 => $"{pending:N0} lines left to save",
            _ => "saving the last of the capture",
        };

        ReportStop();
    }

    /// <summary>
    /// What a stop says about itself. The elapsed clock comes second, before anything that
    /// can be clipped, because on a phone the status bar is one truncated line and a number
    /// that visibly moves every second is the whole answer to "is this stuck?".
    /// </summary>
    private void ReportStop()
    {
        var elapsed = TimeSpan.FromMilliseconds(
            Math.Max(0, Environment.TickCount64 - Volatile.Read(ref _stopStartedMs)));
        var health = CaptureHealthWarning is { Length: > 0 } ? " · ⚠ see session details" : string.Empty;
        ReportActivity(
            SessionActivity.Stopping,
            $"Stopping · {FormatQuiet(elapsed)} · {_stopPhase}{health}");
    }
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }

    /// <summary>
    /// Whether a search is still running, as a state rather than as the wording of a sentence.
    /// </summary>
    /// <remarks>
    /// The marker stepper carries the completed total, so the search status beside it would be
    /// saying the same number twice on a status row that is one line wide — but only once the
    /// count is final. While a search runs, the percentage is the useful half and the stepper's
    /// total is not settled yet. Views switch on this rather than on the prefix of
    /// <see cref="SearchStatus"/>, for the reason <see cref="SessionActivity"/> exists.
    /// </remarks>
    public bool SearchInProgress { get => _searchInProgress; private set => Set(ref _searchInProgress, value); }
    public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
    public FilterSpec Filter { get => _filter; private set => Set(ref _filter, value); }
    public TimeRange? Viewport { get => _viewport; private set => Set(ref _viewport, value); }
    public bool FollowLatest { get => _followLatest; set => Set(ref _followLatest, value); }
    public bool IsLiveCaptureActive
    {
        get => _isLiveCaptureActive;
        set
        {
            if (value)
            {
                IsCaptureSession = true;
            }

            Set(ref _isLiveCaptureActive, value);
        }
    }

    /// <summary>
    /// Whether this tab was opened as a live capture rather than a file import.
    /// </summary>
    /// <remarks>
    /// Sticky once set, because it is read after the capture has ended — a capture Android
    /// refused to start was told the reader it was an import that could not be read, which
    /// names an operation nobody performed and a file that was never involved.
    /// </remarks>
    public bool IsCaptureSession { get; private set; }
    public bool HasNewData { get => _hasNewData; private set => Set(ref _hasNewData, value); }
    public EntryOrder EntryOrder { get => _entryOrder; private set => Set(ref _entryOrder, value); }
    public string RawContextText { get => _rawContextText; private set => Set(ref _rawContextText, value); }

    /// <summary>
    /// Where the selected entry's own line sits inside <see cref="RawContextText"/>, or
    /// <c>null</c> when the context could not be rendered. Always assigned before
    /// <see cref="RawContextText"/>, so a view reacting to the text change never reads a
    /// range belonging to the previous entry.
    /// </summary>
    public RawContextMarker? RawContextMarker { get; private set; }
    public bool IsEntryRetentionLimitReached =>
        Entries.Count >= EntryRetentionLimit && RemainingEntryCount > 0;
    public bool CanLoadMore => _nextEntryCursor is not null && !IsEntryRetentionLimitReached;
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
        // A live capture's progress reporter republishes through the dispatcher, so a
        // republication raised before the tab closed can still arrive after it has.
        if (IsDisposed)
        {
            return;
        }

        var refreshUnchangedSnapshot = false;
        var announceOpened = (VisualCat.Domain.Sessions.SessionDescriptor?)null;
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(Path.Combine(_sessionPath, "manifest.json")))
            {
                return;
            }

            // Segments are immutable once published, so the replacement shares every
            // segment this snapshot already holds and opens only the ones that are new.
            // Opening a second complete set of mappings on each refresh is what made a
            // live capture's descriptor use grow with its duration until the process ran
            // out and killed the capture (§10.6, §12.4).
            var replacement = await SessionStore.OpenAsync(_sessionPath, _snapshot, cancellationToken).ConfigureAwait(false);
            if (_snapshot is not null &&
                (replacement.Generation < _snapshot.Generation ||
                 replacement.Generation == _snapshot.Generation &&
                 replacement.Descriptor == _snapshot.Descriptor))
            {
                replacement.Dispose();
                refreshUnchangedSnapshot = final &&
                    _snapshot.TimedRange is not null &&
                    (HeatMap is null || Overview is null || Statistics is null);
                if (final && _snapshot is { } current)
                {
                    // Two different endings on this path, and the loading tense belongs to
                    // only one of them. If a refresh is about to run, the rows are not bound
                    // yet and the session is Opening until it returns; if this call is about
                    // to return without refreshing, everything on screen is already the
                    // answer and saying "Opening" would leave the tense on for the life of
                    // the tab — which is what it did (finding F-18, first device pass).
                    var unchanged = current.Descriptor;
                    var refreshing = refreshUnchangedSnapshot;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (refreshing)
                        {
                            BeginOpening(unchanged);
                        }
                        else
                        {
                            ReportOpened(unchanged);
                        }
                    });
                    if (refreshing)
                    {
                        announceOpened = unchanged;
                    }
                }

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
                                var span = Math.Clamp(sessionRange.DurationUs, MinimumViewportUs, InitialFollowViewportUs);
                                Viewport = new TimeRange(
                                    new InstantUs(sessionRange.EndExclusive.Value - span),
                                    sessionRange.EndExclusive);
                                _growInitialFollowViewport = span < InitialFollowViewportUs;
                            }
                            else
                            {
                                Viewport = FitViewport(sessionRange);
                            }
                        }
                        else if (!FollowLatest && _viewportIsAuto)
                        {
                            // The viewport was seeded from the first progressive snapshot,
                            // when the session genuinely held one entry — and nothing ever
                            // re-fitted it, so every import finished showing one row and an
                            // empty plot beside a minimap already drawing the whole session.
                            // While the viewport is still the app's own choice it follows
                            // the session; the first zoom or pan makes it the reader's and
                            // it is never moved again (finding 1).
                            Viewport = FitViewport(sessionRange);
                            HasNewData = false;
                        }
                        else if (FollowLatest)
                        {
                            // Clamped rather than minimised: a session of a handful of
                            // microseconds must not drag the window down with it, which is
                            // what left Follow drawing a one-microsecond span that never
                            // widened as lines arrived (audit 2, C6).
                            var span = _growInitialFollowViewport
                                ? Math.Clamp(sessionRange.DurationUs, MinimumViewportUs, InitialFollowViewportUs)
                                : Math.Max(MinimumViewportUs, Math.Min(Viewport.Value.DurationUs, sessionRange.DurationUs));
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

                    // Generation numbers, "committed" and "snapshot" are column-store words:
                    // a reader watching an import wants to know how much of their log is
                    // readable, not which generation the store is on. The session pane still
                    // carries the storage view for anyone who needs it (finding 24).
                    if (final)
                    {
                        BeginOpening(replacement.Descriptor);
                        announceOpened = replacement.Descriptor;
                    }
                    else if (IsLiveCaptureActive)
                    {
                        // A published snapshot is a storage refresh, not a second status
                        // population. Replacing the source-line status with parsed entries
                        // made the live line alternate between incomparable counts on every
                        // refresh (Windows live finding F-10).
                        ReportActivity(
                            SessionActivity.Capturing,
                            DescribeCapture());
                    }
                    else
                    {
                        ReportActivity(
                            SessionActivity.Importing,
                            $"Reading · {Counted.Entries(replacement.Descriptor.Counters.TimedEntries)} ready");
                    }

                    // Answered once per published snapshot, while this method owns it, rather
                    // than by walking every segment's severity bitmaps on each redraw — which
                    // put store internals in the render path and read them from a queued job
                    // that could outlive the session it was reading.
                    HasUnknownLevelEntries = replacement.Segments.Any(
                        static segment => segment.SeverityBitmaps[LogLevel.Unknown].Cardinality > 0);
                    SnapshotChanged?.Invoke(this, EventArgs.Empty);
                });
                previous?.Dispose();
            }
        }
        finally
        {
            _loadLock.Release();
        }

        try
        {
            await RefreshAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Only after the refresh: RefreshAsync is what binds the first page of rows and
            // the plot snapshot, and until it returns there is nothing on screen for "Ready"
            // to be true of (finding F-18).
            //
            // In a finally, because the loading tense is this method's to end. Whatever
            // stops the refresh — a newer refresh superseding it, a torn-down session, an
            // I/O failure — something else is now responsible for what is on screen, and
            // none of those outcomes is a reason to leave the tab saying "Opening" for the
            // rest of its life. That is exactly what the third device pass found, and one
            // escaping exception was enough to cause it; a guarantee that does not depend on
            // reaching a particular line is the only kind that holds.
            if (announceOpened is { } opened)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ReportOpened(opened)).GetTask().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Re-runs every view query against the current snapshot, viewport, filter, and
    /// detail scope. The detail scope is deliberately not reset here: a filter, search,
    /// or level change refines what a selected cell shows rather than discarding the
    /// selection the timeline is still outlining.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // See LoadEntryPagesAsync: a queued refresh can land after the tab has closed, and
        // disposal has already taken the query cancellation apart by then.
        if (IsDisposed)
        {
            return;
        }

        var detailRange = _detailRange;
        var detailLevel = _detailLevel;
        var generation = Interlocked.Increment(ref _queryGeneration);

        // Linked to the session lifetime as well as to the caller, so a refresh that wins
        // the race against disposal is born cancelled rather than running a query set
        // against a snapshot that is about to be released.
        if (LinkToLifetime(cancellationToken) is not { } replacement)
        {
            return;
        }

        // Capture before publication: disposal is allowed to cancel and dispose a published
        // source as soon as it owns it, but an already captured token remains safe to read.
        var token = replacement.Token;
        CancellationTokenSource? previous = null;
        var published = false;
        lock (_queryCancellationGate)
        {
            // The first IsDisposed check is only a fast path. This one closes the window in
            // which disposal could pass the field before this refresh publishes its source.
            if (!IsDisposed && _queryCancellation is not null)
            {
                previous = _queryCancellation;
                _queryCancellation = replacement;
                published = true;
            }
        }

        if (!published)
        {
            replacement.Cancel();
            replacement.Dispose();
            return;
        }

        try
        {
            previous?.Cancel();
        }
        finally
        {
            previous?.Dispose();
        }

        // Queueing for the lock is part of this refresh's life, so being superseded while
        // queued has to mean what it means everywhere else in this method: stop, quietly.
        // It used to be the one moment where it did not — the body's catch is below the
        // try, the wait was above it — so a refresh that lost the race before reaching the
        // lock threw OperationCanceledException out of RefreshAsync, out of
        // LoadSnapshotAsync before it could report the session opened, and all the way to
        // the shell's blanket startup catch, which painted the framework's own
        // "The operation was canceled." in the error banner and left the tab reading
        // "Opening" for the rest of its life (finding F-18 on the startup-restore route,
        // third device pass). A caller who cancels is still answered with the cancellation
        // they asked for; only this method's own supersession is silent.
        try
        {
            await _loadLock.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return;
        }

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
                    SearchInProgress = !value.Completed;
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
            // The heat-map pass materialises the active filter bitmap. Starting the search
            // beside it made both cold consumers build that same expensive bitmap and throw
            // one result away. Search starts only after the shared cache is warm.
            var results = await queryTask;
            var searchResult = await Task.Run(RunSearchAsync, token).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _queryGeneration) ||
                results.heat.Identity.SnapshotGeneration != _snapshot?.Generation)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A failure reported by a query that has since been superseded is no longer
                // true of anything on screen (finding F-05, point 3).
                if (_transientStatusGeneration >= 0 && generation > _transientStatusGeneration)
                {
                    ClearTransientStatus();
                }

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
                    SearchInProgress = false;
                }

                EntriesReloading?.Invoke(this, EventArgs.Empty);
                Entries.Clear();
                foreach (var entry in results.details.Entries)
                {
                    Entries.Add(entry);
                }

                MatchesInView = results.details.TotalCount;
                SetNextEntryCursor(results.details.NextCursor);
                NotifyEntryLoadCounts();
                EntriesReloaded?.Invoke(this, EventArgs.Empty);
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
    /// Loads remaining rows in bounded batches until the platform's retained-row safety
    /// limit is reached. Keeping the regular refresh at 500 rows makes viewport changes
    /// cheap, while the ceiling prevents an explicit bulk load from ending the process.
    /// </summary>
    public Task LoadAllEntriesAsync(CancellationToken cancellationToken = default) =>
        LoadEntryPagesAsync(loadAll: true, cancellationToken);

    private async Task LoadEntryPagesAsync(bool loadAll, CancellationToken cancellationToken)
    {
        // A view reacts to this tab through the dispatcher, so a request raised before the
        // tab closed can still arrive after it has. LinkToLifetime below declines those too;
        // taking it here as well keeps a late arrival from claiming the in-progress flag.
        if (IsDisposed)
        {
            return;
        }

        // Button events can be delivered twice before the first query reaches the lock.
        // Treat paging as one operation rather than queuing surprise extra pages.
        if (Interlocked.CompareExchange(ref _entryLoadInProgress, 1, 0) != 0)
        {
            return;
        }

        var lockHeld = false;

        // Linked to the session lifetime so that closing the tab ends the walk instead of
        // queueing behind it. The caller's own token still means what it meant: a reader
        // who presses Stop gets the cancellation they asked for, and the rows already
        // loaded stay on screen.
        using var linked = LinkToLifetime(cancellationToken);
        if (linked is null)
        {
            Interlocked.Exchange(ref _entryLoadInProgress, 0);
            return;
        }

        var token = linked.Token;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(NotifyEntryLoadState);
            await _loadLock.WaitAsync(token).ConfigureAwait(false);
            lockHeld = true;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var snapshot = _snapshot;
                var viewport = Viewport;
                var cursor = _nextEntryCursor;
                if (snapshot is null || viewport is null || cursor is null)
                {
                    return;
                }

                var retained = await Dispatcher.UIThread.InvokeAsync(() => Entries.Count);
                var capacity = EntryRetentionLimit - retained;
                if (capacity <= 0)
                {
                    return;
                }

                var generation = Volatile.Read(ref _queryGeneration);
                var filter = DetailFilter(Filter, _detailLevel);
                var pageSize = Math.Min(loadAll ? LoadAllBatchSize : EntryPageSize, capacity);
                var page = await Task.Run(
                    () => SessionQueryEngine.GetEntries(
                        snapshot,
                        _detailRange ?? viewport.Value,
                        filter,
                        EntryOrder,
                        cursor,
                        pageSize,
                        generation,
                        token),
                    token).ConfigureAwait(false);
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
        catch (OperationCanceledException) when (IsDisposed && !cancellationToken.IsCancellationRequested)
        {
            // The session closed underneath this walk. That is not the caller's
            // cancellation and must not be reported as one: the view that started the load
            // turns an unexpected exception into a "Failed ·" status line, and there is
            // neither anything to say nor anyone left to say it to.
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

    /// <summary>
    /// Whether the viewport is still the application's choice rather than the reader's.
    /// </summary>
    /// <remarks>
    /// An auto viewport tracks the session as it grows, which is what makes an import end on
    /// the whole capture instead of on the single entry the first snapshot held. Any request
    /// to move the view — a zoom, a pan, engaging Follow, restoring a saved view — hands
    /// ownership to the reader, and from then on nothing moves the viewport but them. That
    /// is what keeps the "no surprise viewport changes" commitment: the only viewport that
    /// ever moves by itself is one nobody has touched.
    /// </remarks>
    internal bool ViewportIsAuto => _viewportIsAuto;

    public Task SetViewportAsync(TimeRange viewport, bool manual = true)
    {
        _viewportIsAuto = false;
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

    /// <summary>
    /// Applies the query the reader typed, or returns why it cannot be applied.
    /// </summary>
    /// <remarks>
    /// A pattern that does not compile used to be accepted as a filter: the chip bar showed
    /// <c>regex = (unclosed</c> as active while the list went on showing all 49,994 unfiltered
    /// rows, so the product claimed a filter it had not applied (finding F-04). Validation
    /// happens here, before the filter is replaced, so a rejected pattern changes nothing at
    /// all — the previous result stays on screen and stays true.
    /// </remarks>
    public async Task<SearchPatternProblem?> ApplySearchAsync(bool regex, bool caseSensitive)
    {
        var search = string.IsNullOrWhiteSpace(SearchText)
            ? null
            : new TextSearchSpec(
                SearchText,
                regex,
                caseSensitive,
                SearchRegexTimeoutOverride ?? TimeSpan.FromMilliseconds(250));
        if (search is { IsRegex: true } &&
            !SearchPattern.TryCompile(search, out _, out var problem))
        {
            return problem;
        }

        var previousFilter = Filter;
        var candidateFilter = previousFilter with { Search = search };
        Filter = candidateFilter;
        try
        {
            await RefreshAsync().ConfigureAwait(false);
            return null;
        }
        catch (SearchTimeoutException)
        {
            // A superseded request no longer owns the field or the chip. The current one
            // rolls back atomically: RefreshAsync publishes only after every query succeeds,
            // so the prior rows, plot, count and markers are still one consistent result.
            if (ReferenceEquals(Filter, candidateFilter))
            {
                Filter = previousFilter;
                SearchInProgress = false;
                return SearchPatternProblem.Timeout();
            }

            return null;
        }
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
        // See LoadEntryPagesAsync: a queued request can land after the tab has closed.
        using var linked = LinkToLifetime(cancellationToken);
        if (linked is null)
        {
            return;
        }

        await _loadLock.WaitAsync(linked.Token).ConfigureAwait(false);
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
            await using var raw = await VerifiedRawSource.OpenAsync(snapshot, linked.Token).ConfigureAwait(false);
            var builder = new StringBuilder();
            RawContextMarker? selectedLine = null;

            // A fixed-width gutter, so the source column starts at the same character on every
            // line and reads as the file's own bytes. It used to be a variable-length prefix
            // — the sequence, then the ParseOutcomeKind enum name in brackets — which pushed
            // each line's text to a different column and put a C# identifier in a panel
            // subtitled "exact bytes" (finding 15a).
            var sequenceWidth = SourceLineNumber(records.Max(static record => record.Sequence)).ToString(
                System.Globalization.CultureInfo.InvariantCulture).Length;
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = new byte[record.Raw.Length];
                await raw.ReadExactlyAsync(record.Raw.Offset, bytes, linked.Token).ConfigureAwait(false);
                var selected = record.Sequence == entry.SourceSequence;
                var start = builder.Length;
                builder.Append(selected ? '▶' : ' ')
                    .Append(SourceLineNumber(record.Sequence).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(sequenceWidth))
                    .Append(' ')
                    .Append(DescribeOutcome(record.Outcome))
                    .Append(" │ ")
                    .Append(Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n'))
                    .AppendLine();
                if (selected)
                {
                    selectedLine = new RawContextMarker(start, builder.Length - start);
                }
            }

            RawContextMarker = selectedLine;
            RawContextText = builder.ToString();
        }
        catch (RawEvidenceException exception)
        {
            RawContextMarker = null;
            RawContextText = exception.Message;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// One page of the source lines that did not become entries, formatted for reading.
    /// </summary>
    /// <param name="Text">The lines, in the same gutter form the source-context pane uses.</param>
    /// <param name="Count">How many lines this page holds.</param>
    /// <param name="NextSequence">Where a following page resumes.</param>
    /// <param name="Completed">Whether the scan reached the end of the session.</param>
    public readonly record struct UnparsedLinePage(
        string Text,
        int Count,
        long NextSequence,
        bool Completed);

    /// <summary>
    /// Whether this session holds physical lines that never became entries.
    /// </summary>
    /// <remarks>
    /// Continuations are counted as a defect rather than as a line class, so the answer is the
    /// sum of the three populations that are read from the source stream and not from the
    /// entry columns: unknown lines, rejected candidates, and the continuation body lines a
    /// declared grammar attached to a previous entry.
    /// </remarks>
    public long UnparsedLineCount =>
        _snapshot is { } snapshot
            ? snapshot.Descriptor.Counters.UnknownLines +
              snapshot.Descriptor.Counters.RejectedCandidates +
              snapshot.Descriptor.Counters.Continuations
            : 0;

    /// <summary>
    /// Records that parsed but carry no usable timestamp.
    /// </summary>
    /// <remarks>
    /// Counted by the filter and by nothing else: a time range cannot contain a record with no
    /// time, so they are absent from the plot, the minimap, the severity legend and the entries
    /// list, and 1,200 of them were the unexplained difference between `3,425 match` and
    /// `2,225 in session` (V2-13). They live in the source stream beside the unparsed lines and
    /// are read back by the same route.
    /// </remarks>
    public long UntimedEntryCount => _snapshot?.Descriptor.Counters.UntimedEntries ?? 0;

    /// <summary>Everything this session holds that no time-based view can show.</summary>
    public long OffTimelineCount => UnparsedLineCount + UntimedEntryCount;

    /// <summary>
    /// Reads the next page of source lines that are not parsed entries.
    /// </summary>
    /// <remarks>
    /// ADR 0009 keeps an indented stack frame under ThreadTime as an unknown line, because
    /// attaching every unmatched line to the entry before it would hide malformed evidence.
    /// That decision is not changed here and must not be: the bytes are kept, and this is the
    /// route to them. Before it existed, a 1,800-line crash log reported "600 entries" on every
    /// surface and its 1,200 stack frames could only be reached by selecting an entry, opening
    /// Source context, and recognising an undecoded <c>??</c> (V2-14).
    /// </remarks>
    public async Task<UnparsedLinePage> LoadUnparsedLinesAsync(
        long fromSequence = 0,
        int maximumLines = 500,
        CancellationToken cancellationToken = default)
    {
        using var linked = LinkToLifetime(cancellationToken);
        if (linked is null)
        {
            return new UnparsedLinePage(string.Empty, 0, fromSequence, true);
        }

        await _loadLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot;
            if (snapshot is null)
            {
                return new UnparsedLinePage(string.Empty, 0, fromSequence, true);
            }

            var page = SessionQueryEngine.ScanSourceRecords(
                snapshot,
                fromSequence,
                maximumLines,

                // A million-line session whose unparsed lines are all at the end must not turn
                // one tap into a full-file walk. The bound is generous enough to cross an
                // ordinary quiet stretch and small enough to stay interactive.
                maximumScanned: Math.Max(maximumLines, 200_000),
                // Untimed entries are here too. They parsed, so they are not "unparsed", but
                // they are excluded from every time-based view for the same practical reason
                // and were reachable by exactly the same nothing (V2-13). The gutter code
                // tells the two apart on every line: `e?` against `??`, `!!` and `..`.
                static outcome => outcome is not ParseOutcomeKind.ParsedEntry
                    and not ParseOutcomeKind.IgnoredBlank
                    and not ParseOutcomeKind.MetaRecord,
                linked.Token);

            if (page.Records.Count == 0)
            {
                return new UnparsedLinePage(string.Empty, 0, page.NextSequence, page.Completed);
            }

            await using var raw = await VerifiedRawSource.OpenAsync(snapshot, linked.Token).ConfigureAwait(false);
            var builder = new StringBuilder();
            var width = SourceLineNumber(page.Records[^1].Sequence)
                .ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
            foreach (var record in page.Records)
            {
                linked.Token.ThrowIfCancellationRequested();
                var bytes = new byte[record.Raw.Length];
                await raw.ReadExactlyAsync(record.Raw.Offset, bytes, linked.Token).ConfigureAwait(false);
                builder
                    .Append(SourceLineNumber(record.Sequence)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width))
                    .Append(' ')
                    .Append(DescribeOutcome(record.Outcome))
                    .Append(" │ ")
                    .Append(Encoding.UTF8.GetString(bytes).TrimEnd('\r', '\n'))
                    .AppendLine();
            }

            return new UnparsedLinePage(
                builder.ToString(),
                page.Records.Count,
                page.NextSequence,
                page.Completed);
        }
        catch (RawEvidenceException exception)
        {
            return new UnparsedLinePage(
                exception.Message,
                0,
                fromSequence,
                true);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// The physical line number of a source record, as every other tool counts it.
    /// </summary>
    /// <remarks>
    /// The gutter printed the store's own 0-based sequence, so the pane whose whole purpose is
    /// letting someone cross-check the app against the file disagreed with the file by exactly
    /// one: <c>sed -n 19328p</c>, <c>grep -n</c>, an editor's Go to line and <c>awk NR==</c>
    /// each landed one record early — on a neighbouring line that, in a log, looks plausible
    /// (finding F-08). Unknown lines consume sequence numbers too, so the mapping is exactly
    /// +1 and nothing else has to change; the sequence stays 0-based everywhere the store
    /// needs it.
    /// </remarks>
    internal static long SourceLineNumber(long sequence) => sequence + 1;

    /// <summary>
    /// A two-letter tag for what the parser made of a source line, for the source-context
    /// gutter. The enum's own names are C# identifiers and belong in code, not in a panel a
    /// reader compares against another tool's output (finding 15a).
    /// </summary>
    internal static string DescribeOutcome(ParseOutcomeKind outcome) => outcome switch
    {
        ParseOutcomeKind.ParsedEntry => "en",
        ParseOutcomeKind.MetaRecord => "mt",
        ParseOutcomeKind.Continuation => "..",
        ParseOutcomeKind.UntimedEntry => "e?",
        ParseOutcomeKind.IgnoredBlank => "  ",
        ParseOutcomeKind.UnknownLine => "??",
        ParseOutcomeKind.RejectedCandidate => "!!",
        _ => "??",
    };

    public async Task<string> ReadRawEntriesAsync(
        IEnumerable<NormalizedEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // See LoadEntryPagesAsync: a queued request can land after the tab has closed. The
        // caller is copying to the clipboard, and a closed session has nothing to copy — so
        // unlike the others this one says so rather than answering with nothing.
        using var linked = LinkToLifetime(cancellationToken)
            ?? throw new ObjectDisposedException(nameof(SessionTabViewModel));
        await _loadLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var snapshot = _snapshot;
            if (snapshot is null)
            {
                throw new InvalidOperationException("Raw source is unavailable.");
            }

            var selected = entries.OrderBy(static entry => entry.SourceSequence).Take(10_000).ToArray();
            if (selected.Length == 0)
            {
                return string.Empty;
            }

            await using var raw = await VerifiedRawSource.OpenAsync(snapshot, linked.Token).ConfigureAwait(false);
            var builder = new StringBuilder();
            foreach (var entry in selected)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (entry.Raw.Length > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException("A selected raw record exceeds the clipboard safety limit.");
                }

                var bytes = new byte[entry.Raw.Length];
                await raw.ReadExactlyAsync(entry.Raw.Offset, bytes, linked.Token).ConfigureAwait(false);
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

        // Following at whole-session span is not following: a second of new data lands in a
        // fraction of a pixel against the right edge, so the plot looks exactly like one
        // that has stopped receiving anything. Engaging follow from a view of the whole
        // session therefore opens the follow window instead of keeping the span — which is
        // the state a capture lands in whenever its viewport was fitted first, and the one
        // that made a live capture look frozen. A narrower span is a deliberate choice of
        // how much history to keep beside the live edge, and is preserved.
        // Compared with room to spare rather than exactly: a live session grows, so a
        // viewport fitted to it a moment ago is already narrower than the session it was
        // fitted to, and an exact test would never fire on the live captures this is for.
        var coversWholeSession = viewport.DurationUs >= session.DurationUs * 0.9;
        var span = coversWholeSession
            ? Math.Clamp(session.DurationUs, MinimumViewportUs, InitialFollowViewportUs)
            : Math.Max(MinimumViewportUs, Math.Min(viewport.DurationUs, session.DurationUs));
        return SetViewportAsync(
            new TimeRange(new InstantUs(session.EndExclusive.Value - span), session.EndExclusive),
            manual: false);
    }

    /// <summary>
    /// Records one live-capture progress report and returns the status to show for it. The
    /// rate is measured over the last second rather than averaged across the session, so a
    /// burst at connect time cannot go on describing a source that has since fallen silent.
    /// </summary>
    public string DescribeCaptureProgress(string scope, long lines)
    {
        // Callers that only have one count are describing work that is already visible; keep
        // the overload's original semantics and avoid inventing a pending backlog. The richer
        // ProgressSnapshot overload below supplies distinct source and committed counts.
        _captureCommittedLines = lines;
        return DescribeCaptureSourceProgress(scope, lines);
    }

    private string DescribeCaptureSourceProgress(string scope, long lines)
    {
        var now = Environment.TickCount64;
        _captureScope = scope;
        if (lines != _captureLines)
        {
            _captureLines = lines;
            _captureLastAdvanceMs = now;
        }
        else if (_captureLastAdvanceMs == 0)
        {
            _captureLastAdvanceMs = now;
        }

        var windowMs = now - _captureWindowMs;
        if (_captureWindowMs == 0)
        {
            _captureWindowMs = now;
            _captureWindowLines = lines;
        }
        else if (windowMs >= 1_000)
        {
            _captureRate = (lines - _captureWindowLines) * 1_000d / windowMs;
            _captureWindowMs = now;
            _captureWindowLines = lines;
        }

        StartCaptureHeartbeat();
        return DescribeCapture();
    }

    /// <summary>
    /// Records one live-capture progress report, including anything the capture is
    /// currently recovering from, and returns the status to show for it.
    /// </summary>
    public string DescribeCaptureProgress(string scope, VisualCat.Domain.Sessions.ProgressSnapshot progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        LiveSegmentCount = progress.SegmentCount;
        _captureCommittedLines = progress.LinesCommitted;
        UpdateCaptureHealth(progress.Warning);

        // Capture health is about the source, not the storage pipeline. LinesRead advances
        // as soon as logcat data reaches VisualCat, while LinesCommitted can legitimately lag
        // behind during parsing, segment sealing, compaction, or manifest publication. Using
        // the latter made an active device stream say "no new lines" even while the reader
        // was still receiving data. The pending count below separately explains any lag.
        return DescribeCaptureSourceProgress(scope, progress.LinesRead);
    }

    /// <summary>
    /// Records that refreshing the view failed, or succeeded again. The capture itself is
    /// unaffected either way — this is only about whether what is on screen is current.
    /// </summary>
    public void ReportRefreshOutcome(string? failure)
    {
        _refreshFailure = failure;
        UpdateCaptureHealth(null, keepCaptureWarning: true);
    }

    /// <summary>
    /// Records why a capture ended, so the whole explanation is readable somewhere.
    /// </summary>
    /// <remarks>
    /// The status bar is one clipped line — long enough for "Failed · Storage refused 33
    /// attempts in a row to save a…" and no further. A capture that ends after hours has
    /// earned a full sentence about what happened and what survived, and the session pane
    /// is the surface that wraps.
    /// </remarks>
    public void ReportCaptureFailure(string message)
    {
        _captureFailure = message;
        UpdateCaptureHealth(null, keepCaptureWarning: true);
    }

    /// <summary>Shows or clears a recoverable source-transport operation such as reconnecting.</summary>
    public void ReportCaptureConnectionStatus(string? summary, string? detail)
    {
        _captureConnectionSummary = summary;
        _captureConnectionDetail = detail;
        UpdateCaptureHealth(null, keepCaptureWarning: true);
        if (IsLiveCaptureActive && !_stopping)
        {
            ReportActivity(SessionActivity.Capturing, DescribeCapture());
        }
    }

    /// <summary>
    /// Says that the source transport is running even when it has not emitted a record.
    /// </summary>
    public void ReportCaptureStreamEstablished(string scope, string noRecordsSummary)
    {
        _captureStreamEstablished = true;
        _captureNoRecordsSummary = string.IsNullOrWhiteSpace(noRecordsSummary)
            ? "waiting for the source to log something"
            : noRecordsSummary;
        _captureConnectionSummary = null;
        _captureConnectionDetail = null;
        UpdateCaptureHealth(null, keepCaptureWarning: true);
        if (IsLiveCaptureActive && !_stopping)
        {
            ReportActivity(
                SessionActivity.Capturing,
                DescribeCaptureSourceProgress(scope, _captureLines));
        }
    }

    private void UpdateCaptureHealth(string? captureWarning, bool keepCaptureWarning = false)
    {
        if (!keepCaptureWarning)
        {
            _lastCaptureWarning = captureWarning;
        }

        // A capture that has already ended outranks anything it was working through on
        // the way there: that is the state the reader is now in.
        CaptureHealthWarning = _captureFailure
                               ?? _captureConnectionDetail
                               ?? _lastCaptureWarning
                               ?? (_refreshFailure is { Length: > 0 } refresh
                                   ? $"The capture is still running, but the view stopped updating: {refresh}"
                                   : null);
    }

    /// <summary>
    /// A quiet source is a normal state — an own-app capture of an idle app produces
    /// nothing for minutes — but it is indistinguishable from a broken one unless the
    /// workspace says which it is.
    /// </summary>
    /// <remarks>
    /// Ordered by how much the reader needs it, because the status bar is one clipped line
    /// and the ellipsis takes whatever is last. The rate is the most volatile and most
    /// watched number in the app and it used to be the first thing lost — the line read
    /// <c>Capturing · On-device full-device logcat · 8 312 lines · 1…</c> — behind a source
    /// description that never changes and that the session pane carries in full anyway
    /// (finding 27).
    /// </remarks>
    private string DescribeCapture()
    {
        // Trouble outranks the throughput readout: a rate is only worth reading when the
        // number it describes is still reaching disk and screen.
        var health = CaptureHealthWarning is { Length: > 0 } ? " · ⚠ see session details" : string.Empty;
        var pendingLines = Math.Max(0, _captureLines - _captureCommittedLines);
        var pending = pendingLines > 0 ? $" · {pendingLines:N0} pending" : string.Empty;
        // Two findings pull on this line in opposite directions, and both are right about
        // something. Finding 27 put the volatile numbers first because the ellipsis takes
        // whatever is last and the rate is the number a reader watches. V2-11 recorded the
        // other half: `Capturing · 37 lines received · no source lines for 18s · On-device o…`
        // dropped the scope clause, which R-02 makes load-bearing, on the one capture where
        // it is load-bearing — a restricted one, seeing almost nothing.
        //
        // The clause that leads is therefore the one that carries a limitation. A full-device
        // capture is the expected case and its scope can sit last with the numbers in front of
        // it; a restricted capture says so first, because that is the fact that explains
        // everything else on the line.
        var restricted = CaptureScopeSummary is { Length: > 0 };
        if (_captureConnectionSummary is { Length: > 0 } connection)
        {
            return restricted
                ? $"{connection}{health} · {_captureScope} · {_captureLines:N0} lines received{pending} · Stop remains available"
                : $"{connection}{health} · {_captureLines:N0} lines received{pending} · Stop remains available · {_captureScope}";
        }

        if (_captureStreamEstablished && _captureLines == 0)
        {
            var silentFor = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - _captureLastAdvanceMs));
            var silence = silentFor.TotalSeconds < 3
                ? "no records yet"
                : $"no records for {FormatQuiet(silentFor)}";
            return restricted
                ? $"Connected{health} · {_captureScope} · {silence} · {_captureNoRecordsSummary} · 0/s"
                : $"Connected{health} · {silence} · {_captureNoRecordsSummary} · 0/s · {_captureScope}";
        }

        var quiet = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - _captureLastAdvanceMs));
        if (quiet.TotalSeconds < 3)
        {
            return restricted
                ? $"Capturing{health} · {_captureScope} · {_captureLines:N0} lines received · {_captureRate:N0}/s{pending}"
                : $"Capturing{health} · {_captureLines:N0} lines received · {_captureRate:N0}/s{pending} · {_captureScope}";
        }

        // The scope only becomes worth raising once it is actually costing the reader
        // something. A restricted capture that is delivering lines is working, and saying
        // so up front would be crying wolf on every own-app session.
        var hint = quiet.TotalSeconds >= StarvedCaptureSeconds && CaptureScopeSummary is { Length: > 0 } reason
            ? $" · {reason}"
            : string.Empty;
        return restricted
            ? $"Capturing{health} · {_captureScope} · {_captureLines:N0} lines received{pending} · " +
              $"no source lines for {FormatQuiet(quiet)}{hint}"
            : $"Capturing{health} · {_captureLines:N0} lines received{pending} · " +
              $"no source lines for {FormatQuiet(quiet)}{hint} · {_captureScope}";
    }

    private static string FormatQuiet(TimeSpan quiet) =>
        quiet.TotalMinutes < 1
            ? $"{quiet.TotalSeconds:N0}s"
            : quiet.TotalHours < 1
                ? $"{(int)quiet.TotalMinutes}m {quiet.Seconds}s"
                : $"{(int)quiet.TotalHours}h {quiet.Minutes}m";

    /// <summary>
    /// Nothing reports progress while a source is silent, so without a tick of its own the
    /// status would simply freeze on whatever was last true.
    /// </summary>
    /// <remarks>
    /// A stop is the case that needs this most. Between the last line read and a reopenable
    /// session there is compaction, a manifest, and an index, and the pipeline can spend
    /// tens of seconds in any one of them without a single progress report — which is
    /// exactly the stretch the reader spends wondering whether to press the button again.
    /// The tick keeps the elapsed clock moving through all of it, so a stop that is working
    /// never looks like one that has stalled.
    /// </remarks>
    private void StartCaptureHeartbeat()
    {
        if (_captureHeartbeat is not null || !Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        _captureHeartbeat = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (Volatile.Read(ref _disposed) != 0 || !(IsLiveCaptureActive || _stopping))
                {
                    StopCaptureHeartbeat();
                    return;
                }

                if (_stopping)
                {
                    ReportStop();
                    return;
                }

                // Only speaks up once the source has gone quiet; while lines are arriving
                // the reporter is already saying something true and more precise.
                if (Environment.TickCount64 - _captureLastAdvanceMs >= 3_000)
                {
                    ReportActivity(SessionActivity.Capturing, DescribeCapture());
                }
            });
        _captureHeartbeat.Start();
    }

    private void StopCaptureHeartbeat()
    {
        _captureHeartbeat?.Stop();
        _captureHeartbeat = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            StopCaptureHeartbeat();
        }
        else
        {
            Dispatcher.UIThread.Post(StopCaptureHeartbeat);
        }

        // First, and before anything here waits on the load lock. Every reader that can
        // hold that lock for an unbounded time is linked to this, so cancelling it turns
        // the wait below from "however long a walk over the whole session takes" into a
        // wait for those readers to notice and leave.
        _lifetime.Cancel();
        CancellationTokenSource? queryCancellation;
        lock (_queryCancellationGate)
        {
            queryCancellation = _queryCancellation;
            _queryCancellation = null;
        }

        try
        {
            queryCancellation?.Cancel();
        }
        finally
        {
            queryCancellation?.Dispose();
        }
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
            // Unpublished before it is disposed, never after. Disposing first left a window
            // — two statements wide, but a whole thread switch long — in which the Snapshot
            // property handed out an object whose segments had already been released, and a
            // reader that took it threw ObjectDisposedException from deep inside a redraw.
            var snapshot = Interlocked.Exchange(ref _snapshot, null);
            snapshot?.Dispose();
        }
        finally
        {
            _loadLock.Release();
        }

        _loadLock.Dispose();

        // Last: every reader linked to this token has left the lock by now, so nothing can
        // still be building a linked source from it.
        _lifetime.Dispose();
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

    /// <summary>
    /// The reader's own position in this session, as it should be restored.
    /// </summary>
    /// <remarks>
    /// A Follow window is not a chosen viewport. Reopening a finished 4-minute capture
    /// presented <c>DENSITY · 30 s</c> pinned to the last moment of the recording — an empty
    /// plot, five zero counters, and a minimap brush parked at the far right — because
    /// Follow's live-edge window had been written into the stored view and came back with it,
    /// through a force-stop and a relaunch (V2-10). R-29 already settles the equivalent moment
    /// on the import path: an untouched viewport follows the session. Reopening from storage
    /// is the same situation, and R-23 says Follow itself belongs to a running capture.
    ///
    /// So while Follow is engaged, nothing about the viewport is persisted: the session opens
    /// at Fit, exactly as an import does, and the first zoom or pan the reader makes is the
    /// one that gets remembered.
    /// </remarks>
    /// <summary>Captures the stored view under a chosen Follow state, for tests.</summary>
    /// <remarks>
    /// V2-10 is about what a running capture writes into its session, and a headless run cannot
    /// run one. This exercises the one decision the finding is about, on a real tab.
    /// </remarks>
    internal static SessionViewState CaptureViewStateForTest(SessionTabViewModel tab, bool following)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var restore = tab.FollowLatest;
        try
        {
            tab.FollowLatest = following;
            return tab.CaptureViewState("Last view");
        }
        finally
        {
            tab.FollowLatest = restore;
        }
    }

    private SessionViewState CaptureViewState(string name) =>
        FollowLatest
            ? new SessionViewState(name, null, Filter, EntryOrder, FollowLatest: false)
            : new SessionViewState(name, Viewport, Filter, EntryOrder, FollowLatest);

    private void ApplyViewState(SessionViewState view, TimeRange? sessionRange)
    {
        ClearDetailScope();
        Filter = view.Filter;
        SearchText = view.Filter.Search?.Query ?? string.Empty;
        EntryOrder = view.EntryOrder;
        FollowLatest = view.FollowLatest;
        _growInitialFollowViewport = false;
        Viewport = ClampViewport(view.Viewport, sessionRange);

        // A restored view is the reader's own last position, so it owns the viewport from
        // here; only a view that carried no viewport leaves the session free to fit itself.
        _viewportIsAuto = view.Viewport is null;
    }

    /// <summary>
    /// The whole session, never narrower than a window the plot can actually draw.
    /// </summary>
    /// <remarks>
    /// Follow has clamped its window to <see cref="MinimumViewportUs"/> since audit 2 (C6);
    /// the fitted, non-following viewport did not, so a one-entry import opened at the raw
    /// session span — the header read <c>DENSITY · 1 µs · 1.1 ns/px</c> with the same instant
    /// printed at both ends of the axis, and a single double-tap in that state crashed the app
    /// (finding F-20). The crash itself is fixed at the zoom boundary in
    /// <c>TimelineControl.ZoomBounds</c>; this stops the app from opening in the degenerate
    /// state at all. Widening never hides a record — the session range stays inside the
    /// returned window — and the newest instant is the anchor because that is the edge a live
    /// capture grows from.
    /// </remarks>
    private static TimeRange FitViewport(TimeRange sessionRange) =>
        sessionRange.DurationUs >= MinimumViewportUs
            ? sessionRange
            : new TimeRange(
                new InstantUs(checked(sessionRange.EndExclusive.Value - MinimumViewportUs)),
                sessionRange.EndExclusive);

    private static TimeRange? ClampViewport(TimeRange? saved, TimeRange? session)
    {
        if (session is null)
        {
            return null;
        }

        if (saved is null || !saved.Value.Overlaps(session.Value))
        {
            return FitViewport(session.Value);
        }

        var clamped = saved.Value.Intersect(session.Value);
        return clamped.IsEmpty ? FitViewport(session.Value) : FitViewport(clamped);
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEntryRetentionLimitReached)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanLoadMore)));
    }
}
