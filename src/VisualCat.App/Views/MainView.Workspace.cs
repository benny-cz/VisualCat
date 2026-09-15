using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.Application.Ports;
using VisualCat.Domain;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

/// <summary>
/// Durable Android workspace restoration and serialized application-settings persistence.
/// </summary>
public sealed partial class MainView
{
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private long _workspacePersistVersion;
    private Task _lastWorkspacePersist = Task.CompletedTask;
    private bool _restoringWorkspace;

    /// <summary>
    /// Captures the open-session set and selected index into the normal settings record, then
    /// writes only the newest queued snapshot. This is intentionally cheap enough to call on
    /// every tab/selection change, which means Android Back does not need a warning dialog just
    /// to preserve the workspace the reader was already using.
    /// </summary>
    private void PersistOpenWorkspace()
    {
        if (!_settingsLoaded || _restoringWorkspace)
        {
            return;
        }

        var paths = _viewModel.Tabs
            .Select(static tab => tab.SessionPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var selectedPath = _viewModel.Selected?.SessionPath;
        var selectedIndex = selectedPath is null
            ? 0
            : Array.FindIndex(paths, path => string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        if (SequenceEqual(_settings.OpenSessionPaths, paths) && _settings.OpenSessionIndex == selectedIndex)
        {
            return;
        }

        _settings = _settings with
        {
            OpenSessionPaths = paths,
            OpenSessionIndex = selectedIndex,
        };

        var version = Interlocked.Increment(ref _workspacePersistVersion);
        var snapshot = _settings;
        _lastWorkspacePersist = PersistOpenWorkspaceAsync(version, snapshot);
    }

    /// <summary>
    /// Android can freeze the process immediately after OnPause. The normal coalesced writer is
    /// sufficient during interaction; this path synchronously commits the already-small JSON
    /// record before returning control to the platform.
    /// </summary>
    private void PersistOpenWorkspaceOnPause()
    {
        if (!_settingsLoaded)
        {
            return;
        }

        ApplicationSettings snapshot;
        SessionTabViewModel[] tabs;
        if (Dispatcher.UIThread.CheckAccess())
        {
            PersistOpenWorkspace();
            snapshot = _settings;
            tabs = _viewModel.Tabs.ToArray();
        }
        else
        {
            // Lifecycle adapters are expected to call on the main thread, but preserve a
            // current workspace even if a host invokes pause elsewhere. Capture both the
            // settings record and observed collection in one dispatcher-owned operation.
            var captured = Dispatcher.UIThread.InvokeAsync(() =>
            {
                PersistOpenWorkspace();
                return (Settings: _settings, Tabs: _viewModel.Tabs.ToArray());
            }).GetAwaiter().GetResult();
            snapshot = captured.Settings;
            tabs = captured.Tabs;
        }

        Interlocked.Increment(ref _workspacePersistVersion);
        var persistedViews = 0;
        var failedViews = 0;
        try
        {
            foreach (var tab in tabs)
            {
                try
                {
                    tab.PersistViewAsync(CancellationToken.None).GetAwaiter().GetResult();
                    persistedViews++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    failedViews++;
                    global::System.Diagnostics.Debug.WriteLine(
                        $"VisualCat view persistence failed during pause for '{tab.Title}': {exception}");
                }
            }

            SaveSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            _ = WriteMainViewDiagnosticAsync(
                "workspace.persisted.on-pause",
                failedViews > 0 ? "warning" : "information",
                new Dictionary<string, string>
                {
                    ["openSessionCount"] = (snapshot.OpenSessionPaths?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["selectedIndex"] = snapshot.OpenSessionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["persistedViewCount"] = persistedViews.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["failedViewCount"] = failedViews.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            global::System.Diagnostics.Debug.WriteLine($"VisualCat workspace persistence failed during pause: {exception}");
        }
    }

    /// <summary>
    /// Records the phone workspace mode the reader has just chosen.
    /// </summary>
    /// <remarks>
    /// One value for the workspace rather than one per session: the mode is how this reader
    /// wants to look at a log, not a property of any particular log, and a phone shows one
    /// session at a time anyway.
    /// </remarks>
    private void PersistWorkspaceDisplayMode(string mode)
    {
        if (!_settingsLoaded ||
            _restoringWorkspace ||
            string.Equals(_settings.WorkspaceDisplayMode, mode, StringComparison.Ordinal))
        {
            return;
        }

        _settings = _settings with { WorkspaceDisplayMode = mode };
        var version = Interlocked.Increment(ref _workspacePersistVersion);
        _lastWorkspacePersist = PersistOpenWorkspaceAsync(version, _settings);
    }

    /// <summary>Records a completed phone plot/details resize or an explicit reset.</summary>
    private void PersistMobileTimelineShare(double? share)
    {
        if (!_settingsLoaded ||
            _restoringWorkspace ||
            SharesEqual(_settings.MobileTimelineShare, share))
        {
            return;
        }

        _settings = _settings with { MobileTimelineShare = share };
        var version = Interlocked.Increment(ref _workspacePersistVersion);
        _lastWorkspacePersist = PersistOpenWorkspaceAsync(version, _settings);
    }

    /// <summary>Records a completed landscape plot/details resize or an explicit reset.</summary>
    private void PersistMobileTimelineWidthShare(double? share)
    {
        if (!_settingsLoaded ||
            _restoringWorkspace ||
            SharesEqual(_settings.MobileTimelineWidthShare, share))
        {
            return;
        }

        _settings = _settings with { MobileTimelineWidthShare = share };
        var version = Interlocked.Increment(ref _workspacePersistVersion);
        _lastWorkspacePersist = PersistOpenWorkspaceAsync(version, _settings);
    }

    private static bool SharesEqual(double? left, double? right) =>
        left is null && right is null ||
        left is { } l && right is { } r && Math.Abs(l - r) < 0.0001;

    private async Task PersistOpenWorkspaceAsync(long version, ApplicationSettings snapshot)
    {
        // One dispatcher turn coalesces AddTab + SelectionChanged + chip updates into one disk
        // write while preserving an immediate in-memory settings snapshot for OnPause.
        await Task.Yield();
        if (version != Volatile.Read(ref _workspacePersistVersion))
        {
            return;
        }

        try
        {
            await SaveSettingsAsync(CancellationToken.None).ConfigureAwait(false);
            await WriteMainViewDiagnosticAsync(
                "workspace.persisted",
                "information",
                new Dictionary<string, string>
                {
                    ["openSessionCount"] = (snapshot.OpenSessionPaths?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["selectedIndex"] = snapshot.OpenSessionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await WriteMainViewDiagnosticAsync(
                "workspace.persist.failed",
                "warning",
                new Dictionary<string, string>
                {
                    ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                }).ConfigureAwait(false);
        }
    }

    /// <summary>Serializes every settings file replacement so independent UI actions cannot race.</summary>
    private async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsSaveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read the settings only after acquiring the writer gate. A workspace save can be
            // queued just before an Appearance/cache edit; persisting the latest record here
            // prevents that older queued operation from overwriting the newer preference.
            var latest = Volatile.Read(ref _settings);
            await _settingsStore.SaveAsync(latest, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    /// <summary>
    /// Reopens the Android workspace that was visible before the activity was finished. The
    /// pause checkpoint persists each session's filter, viewport, and selection first; this
    /// restores the remaining layer — which session paths were open and which tab was in front.
    /// </summary>
    private async Task RestoreWorkspaceAsync()
    {
        // Android restores without being asked; every other platform is offered the choice by
        // OfferWorkspaceRestoreAsync, which calls the overload below with the paths it checked.
        if (!OperatingSystem.IsAndroid() || _settings.OpenSessionPaths is not { Length: > 0 } remembered)
        {
            return;
        }

        await RestoreWorkspaceAsync(remembered);
    }

    private async Task RestoreWorkspaceAsync(string[] remembered)
    {
        var paths = remembered
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var requestedIndex = Math.Clamp(_settings.OpenSessionIndex, 0, paths.Length - 1);
        var requestedPath = paths[requestedIndex];
        var restored = new List<SessionTabViewModel>(paths.Length);
        var failed = 0;
        _restoringWorkspace = true;
        try
        {
            foreach (var path in paths)
            {
                if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "manifest.json")))
                {
                    failed++;
                    await WriteMainViewDiagnosticAsync(
                        "workspace.restore.skipped",
                        "information",
                        new Dictionary<string, string>
                        {
                            ["reason"] = "session-not-found",
                        });
                    continue;
                }

                try
                {
                    restored.Add(await _viewModel.OpenSessionAsync(path).ConfigureAwait(true));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failed++;
                    await WriteMainViewDiagnosticAsync(
                        "workspace.restore.failed",
                        "warning",
                        new Dictionary<string, string>
                        {
                            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                        });
                }
            }

            // TabAdded is marshalled through the Avalonia dispatcher. Queue selection after
            // those callbacks so the selected chip can be brought into view reliably.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var selected = restored.FirstOrDefault(tab =>
                                   string.Equals(tab.SessionPath, requestedPath, StringComparison.OrdinalIgnoreCase))
                               ?? restored.ElementAtOrDefault(Math.Min(requestedIndex, Math.Max(0, restored.Count - 1)));
                if (selected is null)
                {
                    return;
                }

                _viewModel.Selected = selected;
                if (_tabItems.TryGetValue(selected, out var item))
                {
                    _tabs.SelectedItem = item;
                }

                if (_chips.TryGetValue(selected, out var chip))
                {
                    BringChipIntoView(chip.Root);
                }
            }, DispatcherPriority.Loaded);
        }
        finally
        {
            _restoringWorkspace = false;
        }

        PersistOpenWorkspace();
        await WriteMainViewDiagnosticAsync(
            "workspace.restore.completed",
            failed > 0 ? "warning" : "information",
            new Dictionary<string, string>
            {
                ["requestedCount"] = paths.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["restoredCount"] = restored.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["failedCount"] = failed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        if (failed > 0 && restored.Count > 0)
        {
            ShowNotice($"Restored {restored.Count:N0} session(s); {failed:N0} saved session(s) are no longer available.");
        }
        else if (failed > 0)
        {
            ShowNotice("The previously open sessions are no longer available on this device.", NoticeKind.Failure);
        }
    }

    private async Task WriteMainViewDiagnosticAsync(
        string name,
        string level,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        if (_diagnostics is null)
        {
            return;
        }

        try
        {
            await _diagnostics.WriteAsync(new DiagnosticEvent(
                DateTimeOffset.UtcNow,
                level,
                "main-view",
                name,
                _viewModel.Selected?.Snapshot?.SessionId ?? Guid.Empty,
                _viewModel.Selected?.Snapshot?.Generation ?? 0,
                properties ?? new Dictionary<string, string>())).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            global::System.Diagnostics.Debug.WriteLine($"VisualCat diagnostic write failed for {name}: {exception}");
        }
    }

    private static bool SequenceEqual(string[]? left, string[] right) =>
        (left ?? []).SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file whose presence means the last run of this product did not end cleanly.
    /// </summary>
    /// <remarks>
    /// Written when the workspace is first persisted and removed on an orderly exit, so its
    /// presence at start-up is exactly "the previous process died without closing". It carries
    /// no content: what was open is already in <c>settings.json</c>, and a marker that holds
    /// data is a marker that can disagree with it.
    /// </remarks>
    private static string CleanExitMarkerPath => System.IO.Path.Combine(ProductDataRoot.Path, "workspace-open.marker");

    /// <summary>
    /// Offers to reopen the workspace the previous run left behind, and says so plainly when
    /// that run ended in a crash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The desktop wrote <c>openSessionPaths</c> on every exit and never read it back: after a
    /// crash — and, it turned out, after an ordinary ⌘Q with three tabs open — the next launch
    /// showed the empty start page, with nothing saying that the captures were safe or where
    /// they were (finding F-17). The sessions were always intact and always reachable through
    /// <em>Recent captures</em>; only the handoff was missing.
    /// </para>
    /// <para>
    /// An offer rather than an automatic restore, because reopening a 17 MB capture unasked is
    /// its own annoyance, and because a reader whose last session ended in a crash may want to
    /// start somewhere else entirely. Android keeps restoring automatically: one stray Back
    /// press finishes the activity there, so the workspace goes away by accident rather than by
    /// decision, and re-assembling it costs several taps per tab.
    /// </para>
    /// </remarks>
    private async Task OfferWorkspaceRestoreAsync()
    {
        if (OperatingSystem.IsAndroid())
        {
            return;
        }

        var crashed = MarkWorkspaceOpen();
        if (_settings.OpenSessionPaths is not { Length: > 0 } remembered)
        {
            return;
        }

        var available = remembered
            .Where(static path =>
                !string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path) &&
                File.Exists(System.IO.Path.Combine(path, "manifest.json")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        if (available.Length == 0)
        {
            return;
        }

        var count = Counted.Of(available.Length, "capture", "captures");
        var one = available.Length == 1;
        ShowNotice(
            crashed
                ? one
                    ? $"VisualCat closed unexpectedly. Your {count} is safe — reopen it, or find it under Recent captures."
                    : $"VisualCat closed unexpectedly. Your {count} are safe — reopen them, or find them under Recent captures."
                : $"{count} {(one ? "was" : "were")} open when you last closed VisualCat.",
            NoticeKind.Information,
            new NoticeAction(
                "Reopen",
                async () =>
                {
                    ShowNotice(string.Empty);
                    await RestoreWorkspaceAsync(available);
                }));

        await WriteMainViewDiagnosticAsync(
            "workspace.restore.offered",
            crashed ? "warning" : "information",
            new Dictionary<string, string>
            {
                ["rememberedCount"] = remembered.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["availableCount"] = available.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["previousExitWasClean"] = crashed ? "false" : "true",
            });
    }

    /// <summary>
    /// Records that a workspace is open, and reports whether the previous run left one behind.
    /// </summary>
    /// <returns><see langword="true"/> when the last run ended without an orderly exit.</returns>
    private static bool MarkWorkspaceOpen()
    {
        try
        {
            var path = CleanExitMarkerPath;
            var crashed = File.Exists(path);
            File.WriteAllBytes(path, []);
            Core.Store.SessionFileModes.MakeFileOwnerOnly(path);
            return crashed;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ProductDataRootException)
        {
            // A marker that cannot be written is a missing nicety, never a reason to refuse a
            // launch: the offer below still works, it simply cannot say "unexpectedly".
            return false;
        }
    }

    /// <summary>Records an orderly exit, so the next launch does not report a crash.</summary>
    internal static void MarkWorkspaceClosed()
    {
        try
        {
            File.Delete(CleanExitMarkerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ProductDataRootException)
        {
        }
    }
}
