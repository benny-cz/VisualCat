using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain.Sessions;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Presentation;

public sealed partial class WorkspaceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly object _operationGate = new();
    private readonly Dictionary<SessionTabViewModel, SessionOperation> _operations = [];
    private readonly SemaphoreSlim _resourceGovernor = new(2, 2);
    private IDiagnosticSink? _diagnostics;
    private int _uiRefreshLimit = 30;
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

    public void ConfigureDiagnostics(IDiagnosticSink? diagnostics) => _diagnostics = diagnostics;

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
            _ = LoadProgressSnapshotAsync(tab, CancellationToken.None);
        }
    }

    public void ConfigureUiRefreshLimit(int refreshesPerSecond) =>
        _uiRefreshLimit = Math.Clamp(refreshesPerSecond, 1, 60);

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
        await using var source = new FileLogSource(path);
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
            tab.ReportActivity(SessionActivity.Queued, "Queued · waiting for another log to finish reading");
            await _resourceGovernor.WaitAsync(operationToken).ConfigureAwait(false);
            acquired = true;
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

        // A source that only learns its own reach by exercising it says so when it knows.
        // Until then the status line carries the neutral name the source chose, rather than
        // promising a device-wide capture that may already have been declined.
        if (source is ISourceScopeReporter reporter)
        {
            reporter.ScopeResolved += OnScopeResolved;
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
        try
        {
            tab.ReportActivity(SessionActivity.Queued, "Queued · waiting for another log to finish reading");
            await _resourceGovernor.WaitAsync(operationToken).ConfigureAwait(false);
            acquired = true;

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
            await tab.LoadSnapshotAsync(true, operationToken).ConfigureAwait(false);
            if (capturedEntries == 0)
            {
                tab.ReportActivity(
                    SessionActivity.Stopped,
                    "Stopped · no log entries were received; retry Live and generate app activity");
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
            if (source is ISourceScopeReporter finished)
            {
                finished.ScopeResolved -= OnScopeResolved;
            }

            if (acquired)
            {
                _resourceGovernor.Release();
            }

            CompleteOperation(tab, operation);
        }
    }

    public async Task<bool> StopAsync(SessionTabViewModel tab)
    {
        SessionOperation? operation;
        lock (_operationGate)
        {
            _operations.TryGetValue(tab, out operation);
        }

        if (operation is null)
        {
            return false;
        }

        operation.GracefulStop.Cancel();
        tab.ReportActivity(SessionActivity.Stopping, "Stopping · saving the last of the capture…");
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
            tab.ReportActivity(activity, describe(snapshot));

            // With the workspace off screen there is nothing to redraw, and re-opening
            // the snapshot only to run four queries against it and throw the answers
            // away is the most expensive thing a backgrounded capture can do.
            // ResumeLiveViews brings the tab straight up to date when it returns.
            if (snapshot.SnapshotGeneration > (tab.Snapshot?.Generation ?? 0) && _presence.IsWatching)
            {
                _ = LoadProgressSnapshotAsync(tab, cancellationToken);
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
                $"VisualCat is not allowed to read or write part of this session: {Shorten(cause.Message)}",
            FileNotFoundException or DirectoryNotFoundException =>
                "Part of the session is missing. It may have been moved or deleted while it was open.",
            IOException =>
                $"VisualCat could not read or write the session: {Shorten(cause.Message)}",
            _ => Shorten(cause.Message),
        };
    }

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
