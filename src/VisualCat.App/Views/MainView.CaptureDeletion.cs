using System.ComponentModel;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.Core.Store;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

/// <summary>
/// The shell half of deleting captures: protection, tab lifetime, the ledger and home.
/// </summary>
/// <remarks>
/// The dialog asks and the shell coordinates. Nothing here reaches into the dialog's controls,
/// and nothing in the dialog closes a tab, touches storage or refreshes the home screen.
/// </remarks>
public sealed partial class MainView
{
    private RecentSessionsDialog? _recentDialog;
    private bool _recentDialogOpening;
    private long _recentMutationVersion;
    private IReadOnlyList<TemporarySessionInfo> _recentHomeSnapshot = [];
    private Task? _captureDeletionTask;
    private DispatcherTimer? _recentActivityDebounce;

    /// <summary>
    /// Whether this workspace is doing something to a capture that must not be interrupted.
    /// </summary>
    /// <remarks>
    /// A tab's own state answers for this process; <see cref="SessionAccess.IsWorking"/> answers
    /// for a writer that has no tab — a save, an archive extraction or a view-preset write. An
    /// observation is not authority to skip the execution-time reservation, only a reason to
    /// refuse before getting there.
    /// </remarks>
    private CaptureProtection CaptureProtectionFor(string path)
    {
        var canonical = SessionPath.Canonical(path);
        var tabs = _viewModel.Tabs
            .Where(tab => SessionPath.Comparer.Equals(SessionPath.Canonical(tab.SessionPath), canonical))
            .ToArray();
        if (tabs.Any(tab => tab.IsLiveCaptureActive))
        {
            return CaptureProtection.Recording;
        }

        if (tabs.Any(tab => tab.IsSessionWorkInFlight) || SessionAccess.IsWorking(canonical))
        {
            return CaptureProtection.Working;
        }

        return CaptureProtection.None;
    }

    private Func<Task>[] CloseTabsFor(string path)
    {
        var canonical = SessionPath.Canonical(path);
        return _viewModel.Tabs
            .Where(tab => SessionPath.Comparer.Equals(SessionPath.Canonical(tab.SessionPath), canonical))
            .Select(tab => (Func<Task>)(() => _viewModel.CloseAsync(tab)))
            .ToArray();
    }

    private CaptureDeletionCoordinator CreateCaptureDeletionCoordinator(string root, Action<CaptureDeletionResult> resolved) =>
        new(root, CaptureProtectionFor, CloseTabsFor, result =>
        {
            resolved(result);
            if (!result.File.Removed)
            {
                return;
            }

            // A removal is applied to the home screen immediately and stamped, so a scan that
            // started before it cannot republish the card it removed. Reconciliation follows.
            _recentMutationVersion++;
            _recentHomeSnapshot = _recentHomeSnapshot
                .Where(session => !SessionPath.Comparer.Equals(session.Path, result.Capture.Target.Path))
                .ToArray();
            ApplyRecentHomeSnapshot(_recentHomeSnapshot);
            RequestRecentSessionsRefresh();
        });

    private async Task<RecentCaptureSnapshot> ReadRecentCaptureSnapshotAsync(string root, long generation, CancellationToken token)
    {
        var inventory = await CaptureDeletionService.InventoryAsync(root, token);
        if (inventory.Error is { } error)
        {
            WorkspaceViewModel.RecordFailure("capture.inventory." + error.GetType().Name, error);
        }

        return new RecentCaptureSnapshot(
            inventory,
            CapturingSessionPaths(),
            inventory.Sessions.Where(session => SessionAccess.IsWorking(session.Path)).Select(session => session.Path)
                .Concat(_viewModel.Tabs
                .Where(tab => tab.IsSessionWorkInFlight)
                .Select(tab => SessionPath.Canonical(tab.SessionPath)))
                .ToHashSet(SessionPath.Comparer),
            OpenSessionPaths(),
            generation);
    }

    /// <summary>Recent captures, with selection and deletion wired to this workspace.</summary>
    private async Task OpenRecentWithDeletionAsync()
    {
        // The guard is taken before the first await: two rapid activations of the same command
        // must not both reach a presentation.
        if (_recentDialog is not null || _recentDialogOpening)
        {
            return;
        }

        _recentDialogOpening = true;
        var root = SessionPath.Canonical(WorkspaceViewModel.TemporarySessionRoot);
        var generation = 0L;
        var ledger = new Dictionary<string, CaptureDeletionResult>(SessionPath.Comparer);
        var coordinator = CreateCaptureDeletionCoordinator(
            root,
            result => ledger[result.Capture.Target.Path + "|" + result.Capture.Target.Identity] = result);
        var actions = new RecentCaptureActions(
            coordinator.PrepareAsync,
            async (request, progress, token) =>
            {
                var task = coordinator.DeleteAsync(request, progress, token);
                _captureDeletionTask = task;
                try
                {
                    return await task;
                }
                finally
                {
                    _captureDeletionTask = null;
                }
            },
            token => ReadRecentCaptureSnapshotAsync(root, Interlocked.Increment(ref generation), token),
            token => CaptureDeletionService.RetryCleanupAsync(root, token),
            () => ledger.Values.ToArray());

        RecentSessionsDialog dialog;
        try
        {
            var initial = await actions.Refresh(_recentRefreshLifetime.Token);
            _recentRefreshLifetime.Token.ThrowIfCancellationRequested();
            dialog = _recentDialog = new RecentSessionsDialog(initial, actions);
        }
        finally
        {
            _recentDialogOpening = false;
        }

        ObserveWorkspaceForRecentCaptures(dialog);
        string? path;
        try
        {
            path = await ShowDialogAsync(dialog);
        }
        finally
        {
            StopObservingWorkspaceForRecentCaptures();
            _recentDialog = null;
        }

        foreach (var result in dialog.DeletionResults)
        {
            ledger[result.Capture.Target.Path + "|" + result.Capture.Target.Identity] = result;
        }

        var noticeBeforeOpening = _noticeRevision;
        if (string.Equals(path, RecentSessionsDialog.CaptureThisDevice, StringComparison.Ordinal))
        {
            if (OperatingSystem.IsAndroid() && Platform.PlatformSourceRegistry.CreateOnDeviceSource is not null)
            {
                await StartOnDeviceWithAccessSetupAsync();
            }
        }
        else if (path is not null)
        {
            await RunAsync(() => _viewModel.OpenSessionAsync(path));
        }

        PublishCaptureDeletionNotice(ledger.Values, _noticeRevision != noticeBeforeOpening && _noticeKind == NoticeKind.Failure);
    }

    /// <summary>
    /// Raises at most one notice for the whole visit, after any opening the reader asked for.
    /// </summary>
    /// <remarks>
    /// <c>RunAsync</c> clears the lane on entry, so a deletion notice published before an open
    /// would be erased by it. An opening failure keeps its priority: it is the thing the reader
    /// just tried to do.
    /// </remarks>
    private void PublishCaptureDeletionNotice(IEnumerable<CaptureDeletionResult> results, bool preserveOpeningFailure = false)
    {
        var (kind, text) = CaptureDeletionResult.FinalNotice(results);
        if (kind == CaptureNoticeKind.None || preserveOpeningFailure)
        {
            return;
        }

        var notice = kind switch
        {
            CaptureNoticeKind.Failure => NoticeKind.Failure,
            CaptureNoticeKind.Information => NoticeKind.Information,
            _ => NoticeKind.Completion,
        };
        ShowNotice(text, notice, kind == CaptureNoticeKind.Completion && !text.Contains("pending", StringComparison.Ordinal)
            ? null
            : new NoticeAction("Recent captures", OpenRecentAsync));
    }

    /// <summary>
    /// Refreshes the open dialog when this workspace's capture state actually changes.
    /// </summary>
    /// <remarks>
    /// Polling would rescan storage on a timer whether or not anything had happened. The events
    /// that matter are a tab arriving or leaving and a tab's activity changing; a short debounce
    /// coalesces a burst of them into one scan, and status text — which changes several times a
    /// second during a live capture — is deliberately not one of them.
    /// </remarks>
    private void ObserveWorkspaceForRecentCaptures(RecentSessionsDialog dialog)
    {
        _recentActivityDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _recentActivityDebounce.Tick += (_, _) =>
        {
            _recentActivityDebounce?.Stop();
            dialog.RequestRefresh();
        };
        _viewModel.TabAdded += OnRecentCaptureTabAdded;
        _viewModel.TabRemoved += OnRecentCaptureTabRemoved;
        _viewModel.LiveCaptureChanged += OnRecentCaptureActivity;
        SessionAccess.WorkChanged += OnRecentStorageWorkChanged;
        foreach (var tab in _viewModel.Tabs)
        {
            tab.PropertyChanged += OnRecentCaptureTabChanged;
        }
    }

    private void StopObservingWorkspaceForRecentCaptures()
    {
        _viewModel.TabAdded -= OnRecentCaptureTabAdded;
        _viewModel.TabRemoved -= OnRecentCaptureTabRemoved;
        _viewModel.LiveCaptureChanged -= OnRecentCaptureActivity;
        SessionAccess.WorkChanged -= OnRecentStorageWorkChanged;
        foreach (var tab in _viewModel.Tabs)
        {
            tab.PropertyChanged -= OnRecentCaptureTabChanged;
        }

        _recentActivityDebounce?.Stop();
        _recentActivityDebounce = null;
    }

    private void OnRecentCaptureTabAdded(object? sender, SessionTabViewModel tab)
    {
        tab.PropertyChanged += OnRecentCaptureTabChanged;
        OnRecentCaptureActivity(sender, EventArgs.Empty);
    }

    private void OnRecentCaptureTabRemoved(object? sender, SessionTabViewModel tab)
    {
        tab.PropertyChanged -= OnRecentCaptureTabChanged;
        OnRecentCaptureActivity(sender, EventArgs.Empty);
    }

    private void OnRecentCaptureTabChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SessionTabViewModel.Activity) or nameof(SessionTabViewModel.IsLiveCaptureActive))
        {
            OnRecentCaptureActivity(sender, EventArgs.Empty);
        }
    }

    private void OnRecentStorageWorkChanged(string path) =>
        Dispatcher.UIThread.Post(() => OnRecentCaptureActivity(null, EventArgs.Empty));

    private void OnRecentCaptureActivity(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (_recentActivityDebounce is { } debounce)
        {
            debounce.Stop();
            debounce.Start();
        }
    }

    /// <summary>Deleting the recovered capture the reader is reviewing, with a real report.</summary>
    private async Task DeleteRecoveredCaptureAsync(SessionTabViewModel tab)
    {
        var root = SessionPath.Canonical(WorkspaceViewModel.TemporarySessionRoot);
        var coordinator = CreateCaptureDeletionCoordinator(root, _ => { });
        try
        {
            var inventory = await CaptureDeletionService.InventoryAsync(root, _recentRefreshLifetime.Token);
            var session = inventory.Sessions.FirstOrDefault(item =>
                SessionPath.Comparer.Equals(item.Path, SessionPath.Canonical(tab.SessionPath)));
            if (session is null)
            {
                throw new CaptureChangedException();
            }

            var request = await coordinator.PrepareAsync(
                [new CaptureSelection(session, SheetForm.DescribeSessionRow(session))],
                _recentRefreshLifetime.Token);
            if (request.Captures.Count == 0)
            {
                ShowNotice(
                    request.Excluded.Count > 0 ? request.Excluded[0].Reason : "This capture could not be deleted.",
                    NoticeKind.Failure);
                return;
            }

            if (!await ShowDialogAsync(new CaptureDeleteConfirmation(request, OperatingSystem.IsAndroid())))
            {
                return;
            }

            var task = coordinator.DeleteAsync(request, new Progress<CaptureDeletionProgress>(), _recentRefreshLifetime.Token);
            _captureDeletionTask = task;
            PublishCaptureDeletionNotice(await task);
        }
        catch (Exception error)
        {
            WorkspaceViewModel.RecordFailure("capture.delete.recovered", error);
            ShowNotice(
                "This capture could not be deleted. Open Recent captures to refresh and try again.",
                NoticeKind.Failure,
                new NoticeAction("Recent captures", OpenRecentAsync));
        }
        finally
        {
            _captureDeletionTask = null;
        }
    }
}
