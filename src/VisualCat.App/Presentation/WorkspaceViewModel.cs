using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Domain.Sessions;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Presentation;

public sealed partial class WorkspaceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly object _operationGate = new();
    private readonly Dictionary<SessionTabViewModel, SessionOperation> _operations = [];
    private readonly SemaphoreSlim _resourceGovernor = new(2, 2);
    private IDiagnosticSink? _diagnostics;
    private static IDiagnosticSink? s_diagnostics;
    /// <summary>
    /// How often, at most, a running capture is allowed to rewrite the workspace.
    /// </summary>
    /// <remarks>
    /// It was 30 per second on every platform. On a phone that is a ceiling nothing below it
    /// can use: the device's own logcat reported this app's SurfaceView at about 4 fps during
    /// a capture, with <c>Surface::disconnect</c> about five times a second, and an untouched
    /// P0 capture of roughly five lines a second sampled 17.8–25.0 % CPU against a 0.0 %
    /// idle control (finding F-15). Each tick rewrites the status line — a layout pass — and,
    /// whenever the committer has advanced, reopens the snapshot and re-runs the heat map, the
    /// overview, statistics and the entry page.
    ///
    /// Four per second is above the rate this renderer actually presents at and well above
    /// what a person reads a moving number at; the elapsed-time heartbeat only needs one. The
    /// desktop keeps 30, where the frames are cheap and real.
    /// </remarks>
    private int _uiRefreshLimit = OperatingSystem.IsAndroid() ? 6 : 30;

    /// <summary>Whether a snapshot refresh raised by progress is already in flight.</summary>
    /// <remarks>
    /// Reports arrive faster than a refresh completes on a busy capture, and each one used to
    /// start another: they then queued on the session's load lock, so the work outlived the
    /// arrivals that asked for it and the queue only ever grew. A refresh reads the newest
    /// generation on disk, so one that is already running will pick up whatever arrived while
    /// it ran — dropping the request is not dropping the data.
    /// </remarks>
    private readonly HashSet<SessionTabViewModel> _refreshing = [];
    private SessionTabViewModel? _selected;
    private static string? s_temporarySessionRoot;

    /// <summary>
    /// Whether the workspace is on screen. Live captures keep running when it is not;
    /// only the rate at which they publish and the view redraws is relaxed.
    /// </summary>
    private readonly LiveViewerPresence _presence = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<SessionTabViewModel>? TabAdded;
    public event EventHandler<SessionTabViewModel>? TabRemoved;

    public ObservableCollection<SessionTabViewModel> Tabs { get; } = [];
    public static string TemporarySessionRoot => s_temporarySessionRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualCat",
            "Sessions");

    public static void ConfigureTemporarySessionRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            s_temporarySessionRoot = null;
            return;
        }

        var root = Path.GetFullPath(path);
        Directory.CreateDirectory(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("The temporary session root cannot be a symbolic link or reparse point.");
        }

        s_temporarySessionRoot = root;
    }

    public void ConfigureDiagnostics(IDiagnosticSink? diagnostics)
    {
        _diagnostics = diagnostics;
        s_diagnostics = diagnostics;
    }

    /// <summary>
    /// Keeps the raw text of a failure a user was shown a product sentence for.
    /// </summary>
    /// <remarks>
    /// <see cref="FriendlyMessage"/> stopped putting framework exception text in front of a
    /// reader (finding F-04), which would otherwise throw the only detailed record of the
    /// failure away. The composition root configures exactly one sink per process, so a static
    /// handle is honest here; it is only ever read to write a diagnostic line, and a null sink
    /// means diagnostics are switched off rather than that something went wrong.
    /// </remarks>
    internal static void RecordFailure(string context, Exception exception)
    {
        if (s_diagnostics is not { } sink)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await sink.WriteAsync(new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    "warning",
                    "workspace",
                    context,
                    Guid.Empty,
                    0,
                    new Dictionary<string, string>
                    {
                        ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["message"] = exception.Message,
                    })).ConfigureAwait(false);
            }
            catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
            }
        });
    }

    /// <summary>
    /// Reports that the workspace has left the screen — the window was hidden, or on a
    /// phone the activity was backgrounded or the display turned off.
    /// </summary>
    /// <remarks>
    /// Captures continue; what stops is work whose only product is a picture. Refreshing
    /// a snapshot re-runs the heat map, the overview, statistics and any active search,
    /// and a live capture did all of that every few seconds for as long as it ran,
    /// whether or not anyone could see the result. Over a night with the screen off that
    /// is hours of CPU, and on a phone it is battery and flash wear too.
    /// </remarks>
    public void SuspendLiveViews() => _presence.IsWatching = false;

    /// <summary>
    /// Reports that the workspace is back on screen and brings every live tab up to date
    /// at once, so returning never shows a stale plot while the ordinary cadence catches
    /// up.
    /// </summary>
    public void ResumeLiveViews()
    {
        if (_presence.IsWatching)
        {
            return;
        }

        _presence.IsWatching = true;
        foreach (var tab in Tabs.ToArray())
        {
            _ = RefreshProgressSnapshotAsync(tab, CancellationToken.None);
        }
    }

    /// <summary>
    /// Applies the configured refresh ceiling, never above what this platform can present.
    /// </summary>
    /// <remarks>
    /// The stored setting defaults to 30 and is shared by both platforms, so configuring it
    /// used to put the phone straight back to 30 whatever the field above says. A rate above
    /// what the renderer presents at buys the reader nothing and costs a phone battery and
    /// heat for hours (finding F-15), so the platform ceiling wins; a reader who lowers the
    /// setting is still obeyed.
    /// </remarks>
    public void ConfigureUiRefreshLimit(int refreshesPerSecond) =>
        _uiRefreshLimit = Math.Min(Math.Clamp(refreshesPerSecond, 1, 60), PlatformRefreshCeiling);

    /// <summary>The most useful refreshes per second this platform can actually show.</summary>
    private static int PlatformRefreshCeiling => OperatingSystem.IsAndroid() ? 6 : 60;

    public SessionTabViewModel? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Task<SessionTabViewModel> ImportFileAsync(string path, CancellationToken cancellationToken = default) =>
        ImportFileAsync(path, null, cancellationToken);

    public Task<SessionTabViewModel> ImportFileAsync(
        string path,
        IngestSettings? ingestSettings,
        CancellationToken cancellationToken = default) =>
        ImportFileCoreAsync(path, ingestSettings, null, cancellationToken);

    public Task<SessionTabViewModel> ImportFileAsync(
        string path,
        IngestSettings? ingestSettings,
        string displayName,
        CancellationToken cancellationToken = default) =>
        ImportFileCoreAsync(path, ingestSettings, displayName, cancellationToken);

    private async Task<SessionTabViewModel> ImportFileCoreAsync(
        string path,
        IngestSettings? ingestSettings,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var titleSource = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(path)
            : Path.GetFileName(displayName);
        var title = string.IsNullOrWhiteSpace(titleSource) ? "logcat.txt" : titleSource;
        var sessionRoot = CreateTemporarySessionPath(Path.GetFileNameWithoutExtension(title));
        var tab = new SessionTabViewModel(title, sessionRoot);
        Add(tab);
        var operation = RegisterOperation(tab, cancellationToken);
        var operationToken = operation.Cancellation.Token;
        var acquired = false;
        // The title travels into the session, not only onto the tab: a session reopened from
        // the cache reads its name from the stored descriptor, and without this it read the
        // private cache filename back out again (finding F-27).
        await using var source = new FileLogSource(path, displayName: title);
        var settings = ingestSettings ?? new IngestSettings(
            null,
            "utf-8",
            TimestampPolicy.ForFile(source.Metadata.ReferenceInstant),
            new TemplateSettings(),
            PortableRaw: false);
        var progress = CreateProgressiveReporter(
            tab,
            SessionActivity.Importing,
            // "Committing", generations and "capacity" are column-store words. What a reader
            // watching an import wants is how much of their log is already readable and how
            // fast the rest is arriving (finding 24).
            static snapshot =>
                $"Reading · {snapshot.LinesCommitted:N0} lines read · {snapshot.ThroughputLinesPerSecond:N0}/s",
            operationToken);
        try
        {
            EnterQueue(tab);
            await _resourceGovernor.WaitAsync(operationToken).ConfigureAwait(false);
            acquired = true;
            tab.QueuedBehind = null;
            var result = await SessionCoordinator.ImportAsync(
                source,
                sessionRoot,
                settings,
                progress,
                _diagnostics,
                cancellationToken: operationToken).ConfigureAwait(false);
            result.Snapshot.Dispose();
            await tab.LoadSnapshotAsync(true, operationToken).ConfigureAwait(false);
            return tab;
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            tab.ReportActivity(SessionActivity.Stopped, "Stopped · what was read so far is kept");
            throw;
        }
        catch (Exception exception)
        {
            var reason = FriendlyMessage(exception);
            tab.ReportFailure(reason, ImportRemedy(exception));
            throw;
        }
        finally
        {
            if (acquired)
            {
                _resourceGovernor.Release();
            }

            CompleteOperation(tab, operation);
        }
    }

    public async Task<SessionTabViewModel> OpenSessionAsync(string path, CancellationToken cancellationToken = default)
    {
        using var snapshot = await SessionStore.OpenAsync(path, cancellationToken).ConfigureAwait(false);
        var fullPath = Path.GetFullPath(path);

        // Not the manifest's stored name as-is: sessions imported by older builds recorded the
        // materialised temporary file's name, so the tab, the shared archive and the exported
        // CSV all inherited a 32-hex guid that the same session's row in Recent sessions never
        // showed (finding 17).
        var tab = new SessionTabViewModel(
            Views.SessionCacheName.DescribeSession(fullPath, snapshot.Descriptor.DisplayName),
            fullPath);
        Add(tab);
        await tab.LoadSnapshotAsync(true, cancellationToken).ConfigureAwait(false);
        return tab;
    }

    public async Task<SessionTabViewModel> OpenPortableArchiveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = CreateTemporarySessionPath(Path.GetFileNameWithoutExtension(path), create: false);
        await PortableSessionArchiveService.ExtractAsync(path, sessionRoot, cancellationToken).ConfigureAwait(false);
        return await OpenSessionAsync(sessionRoot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The words a running capture uses for its own source, which the source may revise.
    /// </summary>
    /// <remarks>
    /// Volatile because the writer is the source's reading thread and the reader is the
    /// progress reporter: the point of the revision is that the status line stops saying
    /// "full-device" the moment the platform makes clear that it is not.
    /// </remarks>
    private sealed class CaptureScopeLabel(string initial)
    {
        private volatile string _value = initial;

        public string Value
        {
            get => _value;
            set => _value = value;
        }
    }

    /// <summary>
    /// A live capture ended without the reader stopping it, and with what to tell them.
    /// </summary>
    /// <remarks>
    /// Raised on whatever thread the capture finished on. The shell is the only thing that
    /// owns a place to say it, so the model says what happened and leaves the presentation to
    /// the shell — the same division the workspace's own notices follow.
    /// </remarks>
    public event Action<SessionTabViewModel, string>? CaptureEndedUnprompted;

    /// <summary>
    /// Puts a session in the queue and says what it is queued behind.
    /// </summary>
    /// <remarks>
    /// "Waiting for another log to finish reading" is true and useless: with one import running
    /// and another queued, the reader could not tell which was which, whether the one in front
    /// was making progress, or how long they were waiting for (audit 3, E4). The session that
    /// holds the slot has a name and a status line of its own, so this says which one it is
    /// and where to look at it.
    /// </remarks>
    private void EnterQueue(SessionTabViewModel tab)
    {
        var ahead = Tabs.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, tab) &&
            candidate.Activity is SessionActivity.Importing or SessionActivity.Capturing
                or SessionActivity.Connecting or SessionActivity.Starting);
        tab.QueuedBehind = ahead?.Title;
        tab.ReportActivity(
            SessionActivity.Queued,
            ahead is null
                ? "Queued · waiting for a free reading slot"
                : $"Queued · waiting for {ahead.Title} to finish");
    }

    /// <summary>
    /// The session a live capture is running in, if one is.
    /// </summary>
    /// <remarks>
    /// The shell used to have no way to ask. The global Live action was registered with no
    /// availability logic at all, so tapping it during a capture silently created a second
    /// session and a second <c>logcat</c> child; stopping the newly selected one left the
    /// first recording with no global indicator anywhere, and only closing its tab ended it
    /// (finding F-22). A capture that goes on recording after the reader believes they stopped
    /// it costs battery, storage, and — since it is a device log — their trust.
    /// </remarks>
    public SessionTabViewModel? ActiveLiveCapture =>
        Tabs.FirstOrDefault(static tab =>
            tab.IsLiveCaptureActive ||
            tab.Activity is SessionActivity.Connecting or SessionActivity.Starting
                or SessionActivity.Capturing or SessionActivity.Stopping);

    /// <summary>Raised whenever a live capture starts or ends, so the shell can re-ask.</summary>
    public event EventHandler? LiveCaptureChanged;

    private void RaiseLiveCaptureChanged() =>
        LiveCaptureChanged?.Invoke(this, EventArgs.Empty);

    public async Task<SessionTabViewModel> CaptureAsync(
        ILogSource source,
        TimeSpan? duration,
        CancellationToken cancellationToken = default)
    {
        var sessionRoot = CreateTemporarySessionPath(source.Metadata.DisplayName);
        var tab = new SessionTabViewModel(source.Metadata.DisplayName, sessionRoot);
        tab.FollowLatest = true;
        tab.IsLiveCaptureActive = true;
        Add(tab);
        RaiseLiveCaptureChanged();
        var operation = RegisterOperation(tab, cancellationToken);
        var operationToken = operation.Cancellation.Token;
        var acquired = false;

        // Held in a cell rather than a local because it is rewritten from the source's own
        // thread the moment the platform reveals what the capture can actually see, and read
        // from the progress path that describes it (audit 2, C1).
        var scope = new CaptureScopeLabel(source.Metadata.Description);
        void OnScopeResolved(SourceScopeReport report)
        {
            scope.Value = report.Description;
            tab.CaptureScopeSummary = report.Summary;
            tab.CaptureScopeRemedy = report.Remedy;
        }

        void OnConnectionStatusChanged(SourceConnectionStatus? status) =>
            Dispatcher.UIThread.Post(() =>
                tab.ReportCaptureConnectionStatus(status?.Summary, status?.Detail));

        // A source that only learns its own reach by exercising it says so when it knows.
        // Until then the status line carries the neutral name the source chose, rather than
        // promising a device-wide capture that may already have been declined.
        if (source is ISourceScopeReporter reporter)
        {
            reporter.ScopeResolved += OnScopeResolved;
        }

        if (source is ISourceConnectionStatusReporter connectionReporter)
        {
            connectionReporter.ConnectionStatusChanged += OnConnectionStatusChanged;
        }


        // Settling the logcat format first is what lets the policy below follow the zone the
        // device actually agreed to write, instead of assuming one it may not be using.
        if (source is VisualCat.Infrastructure.Adb.AdbLogSource adbSource)
        {
            await adbSource.PrepareAsync(operationToken).ConfigureAwait(false);
        }

        var settings = new IngestSettings(
            VisualCat.Domain.Entries.LogcatFormat.ThreadTime,
            "utf-8",
            // Taken from the source rather than assumed: a device capture asks logcat for
            // UTC, but a followed file on disk carries whatever wrote it, and pinning UTC
            // for both parsed every local timestamp in a followed file as if it were UTC.
            TimestampPolicy.ForFile(source.Metadata.ReferenceInstant, source.Metadata.ResolveLogTimeZoneId()),
            new TemplateSettings(),
            PortableRaw: true);
        using var timed = duration is { } value ? new CancellationTokenSource(value) : new CancellationTokenSource();
        using var gracefulStop = CancellationTokenSource.CreateLinkedTokenSource(timed.Token, operation.GracefulStop.Token);

        // Every graceful ending drains the same pipeline, so every one of them gets the same
        // account of itself. Stop capture already said so from the button; this is what gives
        // a capture that reached its own duration the same "Stopping · 4s · …" line instead of
        // a status frozen on "Capturing" for the whole drain. Registered rather than checked
        // after the fact because the drain begins the moment the token trips, which is tens of
        // seconds before ImportAsync returns to tell anyone about it.
        using var stopAnnouncement = gracefulStop.Token.Register(() =>
            Dispatcher.UIThread.Post(() => tab.BeginStop()));
        try
        {
            EnterQueue(tab);
            await _resourceGovernor.WaitAsync(operationToken).ConfigureAwait(false);
            acquired = true;
            tab.QueuedBehind = null;

            // Capture setup can include device checks and logcat format negotiation.
            // Keep that distinct from confirmed streaming so an empty workspace does
            // not claim data is flowing when the source has not produced a line yet.
            // The description also carries the scope an on-device source resolved —
            // own-app versus full-device — which the user must be able to see
            // (§4.4, §13.9).
            if (source.Metadata.Kind == SourceKind.Adb)
            {
                tab.ReportActivity(SessionActivity.Connecting, $"Connecting · {scope.Value}");
            }
            else
            {
                tab.ReportActivity(SessionActivity.Starting, $"Starting capture · {scope.Value}");
            }

            var result = await SessionCoordinator.ImportAsync(
                source,
                sessionRoot,
                settings,
                CreateProgressiveReporter(
                    tab,
                    SessionActivity.Capturing,
                    snapshot => tab.DescribeCaptureProgress(scope.Value, snapshot),
                    operationToken),
                _diagnostics,
                _presence,
                gracefulStopToken: gracefulStop.Token,
                cancellationToken: operationToken).ConfigureAwait(false);
            var capturedEntries = result.Snapshot.Descriptor.Counters.ParsedEntries;
            result.Snapshot.Dispose();

            // The session is complete and reopenable from here on, so nothing may go on
            // describing a live source. Reopening it and running the first queries over it is
            // real work — a minute of it, on the four-hour phone capture this was written for
            // — and it used to happen with the capture flag still set and the heartbeat still
            // asserting "Capturing · no source lines for 7m", over a Stop capture button that
            // by then did nothing at all.
            tab.IsLiveCaptureActive = false;
            RaiseLiveCaptureChanged();

            // Says the capture is safe before saying what is left to do. By this point the
            // manifest is written and the session would reopen from disk even if the app died
            // here, and "will I lose what I recorded?" is the question behind pressing the
            // button a second time — so it is answered in the line itself rather than left to
            // be inferred from a phase name.
            tab.ReportStopPhase("capture saved · opening the session");
            try
            {
                await tab.LoadSnapshotAsync(true, operationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!operationToken.IsCancellationRequested)
            {
                // A view query this one was superseded by, not a capture that failed: any
                // refresh started while this one waits for the snapshot lock cancels it. The
                // session is complete on disk and the refresh that took over is drawing it,
                // so this reports the ending it actually had instead of "Failed".
            }

            // Who ended it decides what the reader is owed. A capture the reader stopped is
            // told plainly that it stopped and what it kept — leaving the last word to
            // whatever the snapshot loader happened to say let a stop land back on the
            // capture's own wording — and one that ran its stated duration ended as agreed.
            // A capture that ended on its own is the case where a recording the reader
            // believes is running has quietly stopped, and it used to be told by nothing at
            // all: the status line changed tense and Follow and Stop capture disappeared from
            // the layout (audit 3, A2).
            var readerStopped = operation.GracefulStop.IsCancellationRequested;
            var durationElapsed = timed.IsCancellationRequested;
            if (capturedEntries == 0)
            {
                tab.ReportActivity(
                    SessionActivity.Stopped,
                    "Stopped · no log entries were received; retry Live and generate app activity");
            }
            else if (readerStopped)
            {
                tab.ReportActivity(
                    SessionActivity.Stopped,
                    $"Stopped · {Counted.Entries(capturedEntries)} kept");
            }
            else if (durationElapsed)
            {
                tab.ReportActivity(
                    SessionActivity.Stopped,
                    $"Stopped · this capture ran its full duration · {Counted.Entries(capturedEntries)} kept");
            }
            else
            {
                tab.ReportActivity(
                    SessionActivity.Stopped,
                    $"Stopped · the log source ended this capture · {Counted.Entries(capturedEntries)} kept");
            }

            if (!readerStopped && !durationElapsed)
            {
                CaptureEndedUnprompted?.Invoke(
                    tab,
                    capturedEntries == 0
                        ? "The live capture stopped on its own before any log line arrived. Nothing was recorded."
                        : $"The live capture stopped on its own — the log source ended it. " +
                          $"{Counted.Entries(capturedEntries)} {(capturedEntries == 1 ? "was" : "were")} kept; start Live again to carry on.");
            }

            return tab;
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            tab.ReportActivity(SessionActivity.Stopped, "Stopped · what was captured is kept");
            throw;
        }
        catch (Exception exception)
        {
            tab.ReportFailure(FriendlyMessage(exception), remedy: null);
            throw;
        }
        finally
        {
            tab.IsLiveCaptureActive = false;
            RaiseLiveCaptureChanged();
            if (source is ISourceScopeReporter finished)
            {
                finished.ScopeResolved -= OnScopeResolved;
            }
            if (source is ISourceConnectionStatusReporter finishedConnectionReporter)
            {
                finishedConnectionReporter.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }

            if (acquired)
            {
                _resourceGovernor.Release();
            }

            CompleteOperation(tab, operation);
        }
    }

    /// <summary>
    /// Ends a running capture and waits for the session to be finished and reopened.
    /// </summary>
    /// <remarks>
    /// The acknowledgement is the first thing that happens and it happens on the caller's
    /// thread, before the token is even cancelled: everything after this point — draining the
    /// read-ahead, compacting, writing the index, reopening the session — takes as long as the
    /// capture was large, and on a phone capture of half a million lines that is tens of
    /// seconds during which the reader has only the button to go on. Idempotent, because a
    /// stop that looks like it did nothing is a stop that gets pressed again.
    /// </remarks>
    public async Task<bool> StopAsync(SessionTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        SessionOperation? operation;
        lock (_operationGate)
        {
            _operations.TryGetValue(tab, out operation);
        }

        if (operation is null)
        {
            return false;
        }

        tab.BeginStop();
        operation.GracefulStop.Cancel();
        await operation.Completion.Task.ConfigureAwait(false);
        return true;
    }

    public async Task CloseAsync(SessionTabViewModel tab)
    {
        SessionOperation? operation;
        lock (_operationGate)
        {
            _operations.TryGetValue(tab, out operation);
        }

        if (operation is not null)
        {
            operation.Cancellation.Cancel();
            await operation.Completion.Task.ConfigureAwait(false);
        }

        if (!Tabs.Remove(tab))
        {
            return;
        }

        if (ReferenceEquals(Selected, tab))
        {
            Selected = Tabs.LastOrDefault();
        }

        await tab.DisposeAsync().ConfigureAwait(false);
        TabRemoved?.Invoke(this, tab);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tab in Tabs.ToArray())
        {
            await CloseAsync(tab).ConfigureAwait(false);
        }

        _resourceGovernor.Dispose();
    }

    private void Add(SessionTabViewModel tab)
    {
        Tabs.Add(tab);
        Selected = tab;
        TabAdded?.Invoke(this, tab);
    }

    private SessionOperation RegisterOperation(SessionTabViewModel tab, CancellationToken cancellationToken)
    {
        var operation = new SessionOperation(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
        lock (_operationGate)
        {
            _operations.Add(tab, operation);
        }

        return operation;
    }

    private void CompleteOperation(SessionTabViewModel tab, SessionOperation operation)
    {
        lock (_operationGate)
        {
            _operations.Remove(tab);
        }

        operation.Completion.TrySetResult();
        operation.Cancellation.Dispose();
        operation.GracefulStop.Dispose();
    }

    /// <summary>
    /// Throttled progress reporter that also republishes the tab's snapshot whenever the
    /// committer advances a generation. §10.6 requires committed data to become viewable
    /// while acquisition continues — for a live capture, which only ends when the user
    /// stops it, waiting for completion would mean showing nothing at all.
    /// </summary>
    private Progress<ProgressSnapshot> CreateProgressiveReporter(
        SessionTabViewModel tab,
        SessionActivity activity,
        Func<ProgressSnapshot, string> describe,
        CancellationToken cancellationToken)
    {
        long lastUiProgress = 0;
        return new Progress<ProgressSnapshot>(snapshot =>
        {
            var now = Stopwatch.GetTimestamp();
            var minimumTicks = Math.Max(1, Stopwatch.Frequency / Volatile.Read(ref _uiRefreshLimit));
            if (snapshot.TerminalState is null &&
                now - Volatile.Read(ref lastUiProgress) < minimumTicks)
            {
                return;
            }

            Volatile.Write(ref lastUiProgress, now);
            if (tab.IsStopping)
            {
                // The reports keep coming after the stop — the pipeline still has read-ahead
                // to commit — and each one is progress the reader is waiting on, so it is
                // shown as what remains of the stop rather than as a capture still running.
                tab.ReportStopProgress(snapshot);
            }
            else
            {
                tab.ReportActivity(activity, describe(snapshot));
            }

            // With the workspace off screen there is nothing to redraw, and re-opening
            // the snapshot only to run four queries against it and throw the answers
            // away is the most expensive thing a backgrounded capture can do.
            // ResumeLiveViews brings the tab straight up to date when it returns.
            if (snapshot.SnapshotGeneration > (tab.Snapshot?.Generation ?? 0) && _presence.IsWatching)
            {
                _ = RefreshProgressSnapshotAsync(tab, cancellationToken);
            }
        });
    }

    /// <summary>
    /// Refreshes one tab from the session on disk, reporting the outcome to the tab
    /// rather than discarding it.
    /// </summary>
    /// <remarks>
    /// This runs detached from the progress callback, so an exception here has nowhere to
    /// propagate to. Dropping it meant that when refreshing began failing — for fourteen
    /// minutes, in the failure this was written for — the workspace showed a frozen
    /// timeline and a status line that went on claiming the capture was healthy. The
    /// capture genuinely is unaffected, so this is reported as a view-freshness problem
    /// and clears itself as soon as a refresh succeeds.
    /// </remarks>
    /// <summary>Runs one progress refresh per tab at a time, dropping the overlap.</summary>
    private async Task RefreshProgressSnapshotAsync(SessionTabViewModel tab, CancellationToken cancellationToken)
    {
        lock (_refreshing)
        {
            if (!_refreshing.Add(tab))
            {
                return;
            }
        }

        try
        {
            await LoadProgressSnapshotAsync(tab, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_refreshing)
            {
                _refreshing.Remove(tab);
            }
        }
    }

    private async Task LoadProgressSnapshotAsync(SessionTabViewModel tab, CancellationToken cancellationToken)
    {
        try
        {
            await tab.LoadSnapshotAsync(false, cancellationToken).ConfigureAwait(false);
            tab.ReportRefreshOutcome(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            tab.ReportRefreshOutcome(FriendlyMessage(exception));
            await WriteRefreshDiagnosticAsync(tab, exception).ConfigureAwait(false);
        }
    }

    private async Task WriteRefreshDiagnosticAsync(SessionTabViewModel tab, Exception exception)
    {
        if (_diagnostics is null)
        {
            return;
        }

        try
        {
            await _diagnostics.WriteAsync(new DiagnosticEvent(
                DateTimeOffset.UtcNow,
                "warning",
                "workspace",
                "snapshot.refresh.failed",
                tab.Snapshot?.SessionId ?? Guid.Empty,
                tab.Snapshot?.Generation ?? 0,
                new Dictionary<string, string>
                {
                    ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                })).ConfigureAwait(false);
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Turns an exception into something a user can act on.
    /// </summary>
    /// <remarks>
    /// The framework's own text for a resource limit is a bare errno phrase followed by
    /// the path that happened to be unlucky — "Too many open files :
    /// '…/segments/001058/flags.bin'" — which names neither what went wrong nor what to
    /// do. These are the failures a long-running capture can actually reach, so they are
    /// the ones worth translating; anything else keeps its own message.
    /// </remarks>
    internal static string FriendlyMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Conditions are recognised anywhere in the chain, because the cause is usually
        // wrapped by the stage that noticed it, and it is the cause that decides what the
        // user can do about it.
        if (Chain(exception).Any(IsResourceExhaustion))
        {
            return "VisualCat ran out of open files. Close some session tabs and try again; " +
                   "if this keeps happening during a long capture, stop and reopen the capture to compact it.";
        }

        if (Chain(exception).Any(IsDiskFull))
        {
            return "The disk holding the session is full. Free some space, then continue.";
        }

        var cause = Unwrap(exception);
        return cause switch
        {
            // Already phrased for a person, including what survived.
            SegmentWriteRefusedException => cause.Message,
            UnauthorizedAccessException =>
                $"VisualCat is not allowed to read or write part of this session: {Detail(cause)}",
            FileNotFoundException or DirectoryNotFoundException =>
                "Part of the session is missing. It may have been moved or deleted while it was open.",
            IOException =>
                $"VisualCat could not read or write the session: {Detail(cause)}",
            _ => Presentable(cause),
        };
    }

    /// <summary>
    /// The cause's own message, but only while it is still a sentence a person can act on.
    /// </summary>
    /// <remarks>
    /// A Release Android build sets <c>System.Resources.UseSystemResourceKeys=true</c> — the
    /// .NET Android SDK's default, which trims framework resource strings — and every framework
    /// message then arrives as <c>ResourceKey, arg0, arg1</c> instead of a sentence. That is
    /// how <c>Failed · MakeException, (unclosed, 9, InsufficientClosingParentheses</c> reached
    /// a status line (finding F-04), and it degrades that way only in Release, which is why no
    /// test saw it. The property is also turned off for the Android build as a safety net, but
    /// this is the fix: a message that still looks like a key is replaced by a product sentence
    /// and a stable code, and the raw text goes to the diagnostic bundle.
    /// </remarks>
    private static string Presentable(Exception cause)
    {
        var message = Shorten(cause.Message);
        if (!IsPresentable(message))
        {
            RecordFailure("message.not.presentable", cause);
            return $"VisualCat could not finish that ({ErrorCode(cause)}). The details are in the diagnostic bundle.";
        }

        return message;
    }

    /// <summary>The trailing half of a sentence that already names what failed.</summary>
    private static string Detail(Exception cause)
    {
        var message = Shorten(cause.Message);
        if (!IsPresentable(message))
        {
            RecordFailure("message.not.presentable", cause);
            return $"error {ErrorCode(cause)}";
        }

        return message;
    }

    private static bool IsPresentable(string message) =>
        message.Length > 0 && !ResourceKeyLeak().IsMatch(message);

    /// <summary>A short, stable, greppable name for an exception type.</summary>
    private static string ErrorCode(Exception cause)
    {
        var name = cause.GetType().Name;
        return name.EndsWith("Exception", StringComparison.Ordinal) && name.Length > 9
            ? name[..^9]
            : name;
    }

    /// <summary>
    /// The signature of a trimmed framework resource string: an identifier, a comma, a space.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*,(\s|$)")]
    private static partial Regex ResourceKeyLeak();

    /// <summary>
    /// The next step for a failed import, when this platform has one to offer.
    /// </summary>
    /// <remarks>
    /// A phone has no format override — the Android import path constructs its settings from
    /// the detected format with no dialog — so telling a phone user to "select a format
    /// override" named a control that does not exist there. Both platforms get the same
    /// first sentence and only the actionable half differs (finding 10).
    /// </remarks>
    internal static string? ImportRemedy(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Unwrap(exception) is not InvalidDataException)
        {
            return null;
        }

        // No Markdown: this is read by a plain TextBlock, so backticks around logcat and .vcat
        // rendered as literal backticks in the failure card (finding 20).
        return OperatingSystem.IsAndroid()
            ? "VisualCat reads Android logcat text — the output of logcat, or a .vcat " +
              "session or portable archive. Check that this file is a logcat capture and " +
              "not, say, a bug report or an application log in another format."
            : "Open it again and choose a format override in the import preview if you know " +
              "which logcat format it is.";
    }

    /// <summary>Unwraps the reporting layers a failure passes through on its way here.</summary>
    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current.InnerException is { } inner &&
               current is SessionPipelineException or AggregateException or TargetInvocationException)
        {
            current = inner;
        }

        return current;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Collapses absolute paths inside a framework message to their last two components.
    /// </summary>
    /// <remarks>
    /// A session path on Android runs to about 130 characters, and the status bar is one
    /// clipped line: left whole, the path pushes out every word that says what went wrong.
    /// The session pane carries the full path for anyone who needs it.
    /// </remarks>
    private static string Shorten(string message) =>
        QuotedPath().Replace(message, static match =>
        {
            var parts = match.Value.Trim('\'').Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 2 ? match.Value : $"'…/{parts[^2]}/{parts[^1]}'";
        });

    [GeneratedRegex(@"'[^']*[/\\][^']*'")]
    private static partial Regex QuotedPath();

    private static bool IsResourceExhaustion(Exception exception) =>
        exception.Message.Contains("Too many open files", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("insufficient system resources", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("not enough memory resources", StringComparison.OrdinalIgnoreCase);

    private static bool IsDiskFull(Exception exception) =>
        exception.Message.Contains("no space left", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("disk is full", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase);

    private static string CreateTemporarySessionPath(string name, bool create = true)
    {
        var safe = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var root = Path.Combine(
            TemporarySessionRoot,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safe}-{Guid.NewGuid():N}.vcat");
        if (create)
        {
            Directory.CreateDirectory(root);
        }

        return root;
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

    private sealed record SessionOperation(
        CancellationTokenSource Cancellation,
        CancellationTokenSource GracefulStop,
        TaskCompletionSource Completion)
    {
        public SessionOperation(CancellationTokenSource cancellation)
            : this(
                cancellation,
                new CancellationTokenSource(),
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
        {
        }
    }
}
