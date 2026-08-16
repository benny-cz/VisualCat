using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Application.UseCases;
using VisualCat.Core.Store;
using VisualCat.Domain.Sessions;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Presentation;

public sealed class WorkspaceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly object _operationGate = new();
    private readonly Dictionary<SessionTabViewModel, SessionOperation> _operations = [];
    private readonly SemaphoreSlim _resourceGovernor = new(2, 2);
    private IDiagnosticSink? _diagnostics;
    private int _uiRefreshLimit = 30;
    private SessionTabViewModel? _selected;
    private static string? s_temporarySessionRoot;

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
            static snapshot => $"{snapshot.Stage} · {snapshot.LinesCommitted:N0} lines · {snapshot.ThroughputLinesPerSecond:N0}/s",
            operationToken);
        try
        {
            tab.Status = "Waiting for import capacity…";
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
            tab.Status = "Cancelled · partial session retained";
            throw;
        }
        catch (Exception exception)
        {
            tab.Status = $"Failed · {exception.GetBaseException().Message}";
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
        var tab = new SessionTabViewModel(snapshot.Descriptor.DisplayName, Path.GetFullPath(path));
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
            tab.Status = "Waiting for capture capacity…";
            await _resourceGovernor.WaitAsync(operationToken).ConfigureAwait(false);
            acquired = true;

            // Capture setup can include device checks and logcat format negotiation.
            // Keep that distinct from confirmed streaming so an empty workspace does
            // not claim data is flowing when the source has not produced a line yet.
            // The description also carries the scope an on-device source resolved —
            // own-app versus full-device — which the user must be able to see
            // (§4.4, §13.9).
            var scope = source.Metadata.Description;

            // READ_LOGS is signature|privileged|development, so an app cannot ask for it at
            // runtime — Android never prompts, the source quietly falls back to its own
            // records, and an idle app then produces a capture that looks broken. Naming
            // the one route out of that is the difference between a quiet capture and an
            // apparently dead one (§4.4).
            if (source.Metadata.Properties?.TryGetValue("scope", out var granted) == true &&
                string.Equals(granted, "own-app", StringComparison.Ordinal))
            {
                tab.CaptureScopeSummary = "own-app scope only";
                tab.CaptureScopeRemedy =
                    "This capture can only see this app's own log lines, so an idle app produces " +
                    "almost nothing. Android cannot prompt for wider access — READ_LOGS is not a " +
                    "runtime permission — so full-device capture has to be granted over adb, and " +
                    "again after every uninstall or reinstall:\n" +
                    "adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS";
            }
            tab.Status = source.Metadata.Kind == SourceKind.Adb
                ? $"Connecting · {scope}"
                : $"Starting capture · {scope}";
            var result = await SessionCoordinator.ImportAsync(
                source,
                sessionRoot,
                settings,
                CreateProgressiveReporter(
                    tab,
                    snapshot => tab.DescribeCaptureProgress(scope, snapshot.LinesCommitted),
                    operationToken),
                _diagnostics,
                gracefulStopToken: gracefulStop.Token,
                cancellationToken: operationToken).ConfigureAwait(false);
            var capturedEntries = result.Snapshot.Descriptor.Counters.ParsedEntries;
            result.Snapshot.Dispose();
            await tab.LoadSnapshotAsync(true, operationToken).ConfigureAwait(false);
            if (capturedEntries == 0)
            {
                tab.Status = "Stopped · no log entries were received; retry Live and generate app activity";
            }
            return tab;
        }
        catch (OperationCanceledException) when (operation.Cancellation.IsCancellationRequested)
        {
            tab.Status = "Stopped · partial session retained";
            throw;
        }
        catch (Exception exception)
        {
            tab.Status = $"Failed · {exception.GetBaseException().Message}";
            throw;
        }
        finally
        {
            tab.IsLiveCaptureActive = false;
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
        tab.Status = "Stopping · draining committed data…";
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
            tab.Status = describe(snapshot);
            if (snapshot.SnapshotGeneration > (tab.Snapshot?.Generation ?? 0))
            {
                _ = LoadProgressSnapshotAsync(tab, cancellationToken);
            }
        });
    }

    private static async Task LoadProgressSnapshotAsync(SessionTabViewModel tab, CancellationToken cancellationToken)
    {
        try
        {
            await tab.LoadSnapshotAsync(false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

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
