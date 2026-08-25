using Avalonia.Controls;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.Domain;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Views;

/// <summary>
/// What the platform's store says about a newer VisualCat, rendered into the notice lane.
/// </summary>
/// <remarks>
/// <para>
/// The decision itself is in <see cref="AppUpdatePolicy"/>, which knows nothing about Avalonia
/// and can therefore be tested without a device — the only way the rules that matter here can
/// be trusted, since the real Google Play path cannot be exercised outside a Play install.
/// This partial is the part that has a screen: it holds the lane's etiquette, the cancellation,
/// and the translation from a decision into a button.
/// </para>
/// <para>
/// Every hook it uses is null on the desktop, so nothing is published, so nothing renders.
/// There is no desktop code path here and there should not be one: desktop releases are updated
/// by downloading a new archive.
/// </para>
/// </remarks>
public sealed partial class MainView
{
    private readonly CancellationTokenSource _updateLifetime = new();

    /// <summary>
    /// The offer that has been decided but has not been allowed onto the screen yet.
    /// </summary>
    /// <remarks>
    /// The lane is shared, and an update must never take it from a message the reader has not
    /// read. Holding the decision rather than dropping it is what lets the offer appear the
    /// moment the lane is free, instead of waiting for the next resume to re-ask Play.
    /// </remarks>
    private AppUpdatePrompt? _pendingUpdatePrompt;

    /// <summary>The revision of the notice this view raised about an update, or 0.</summary>
    private long _updateNoticeRevision;

    /// <summary>
    /// Which audience this build was made for, as the update rules should see it.
    /// </summary>
    /// <remarks>
    /// A single accessor rather than <c>ProductInfo.Channel</c> at each call site, because the
    /// channel is the one input the rules cannot obtain for themselves in a test: this assembly's
    /// own version is whatever the build stamped, which is a <c>Development</c> build that by
    /// design never prompts. Nothing in the product writes to it. The device loop gets a real
    /// channel the honest way instead, by building with one
    /// (<c>-p:Version=2.1.0-beta.1</c>) — a build that lied about its channel would not be
    /// exercising the rules it is meant to demonstrate.
    /// </remarks>
    internal static ReleaseChannel UpdateChannel { get; set; } = ProductInfo.Channel;

    /// <summary>
    /// Renders an answer the store has already given, for a view that was built after it.
    /// </summary>
    /// <remarks>
    /// Android rebuilds this view on every activity recreation while the service that owns the
    /// Play client survives, so an offer or a download in flight is already known by the time a
    /// replacement view exists. Rendering the cached status costs no second IPC round trip and
    /// is the difference between an update banner surviving a text-size change and vanishing
    /// with the view that raised it.
    /// </remarks>
    private void RestoreCachedUpdateNotice()
    {
        var cached = PlatformSourceRegistry.LastAppUpdateStatus;
        if (cached.State != AppUpdateState.Unknown)
        {
            RenderUpdateStatus(cached);
        }
    }

    /// <summary>
    /// Abandons any store work this view started.
    /// </summary>
    /// <remarks>
    /// Called both when this view is superseded and when it is disposed. A pending Play IPC on
    /// a view nobody will ever see again is exactly the class of leak the one-subscriber
    /// invariant exists to prevent.
    /// </remarks>
    private void CancelUpdateWork()
    {
        try
        {
            if (!_updateLifetime.IsCancellationRequested)
            {
                _updateLifetime.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by DisposeAsync. A view can be superseded and then disposed, or
            // the other way round, and neither order is a problem worth reporting.
        }
    }

    /// <summary>Releases the update work's cancellation source. Called once, from disposal.</summary>
    private void DisposeUpdateWork()
    {
        CancelUpdateWork();
        _updateLifetime.Dispose();
    }

    /// <summary>
    /// Asks the store whether a newer build exists, if the channel and the throttle allow it.
    /// </summary>
    /// <remarks>
    /// Deliberately started after <c>InitializeAsync</c> has returned rather than inside it.
    /// A workspace restore and a tapped file outrank an update offer for the first screen the
    /// reader sees; and <c>InitializeAsync</c>'s catch paints "VisualCat started, but part of
    /// the startup did not finish" as a failure, which a Play Store that cannot answer must
    /// never be able to produce.
    /// </remarks>
    /// <param name="coldStart">True for the check at startup, false for one on resume.</param>
    private async Task CheckForUpdateAsync(bool coldStart)
    {
        if (PlatformSourceRegistry.CheckForAppUpdateAsync is not { } check)
        {
            return;
        }

        var origin = PlatformSourceRegistry.GetInstallOrigin?.Invoke() ?? AppInstallOrigin.Unknown;
        if (!AppUpdatePolicy.ShouldCheck(UpdateChannel, origin, coldStart, UpdateMemory(), DateTimeOffset.UtcNow))
        {
            return;
        }

        try
        {
            var status = await check(_updateLifetime.Token).ConfigureAwait(true);
            await RecordUpdateCheckedAsync().ConfigureAwait(true);
            RenderUpdateStatus(status);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The store not answering is not something the reader can act on, and startup is
            // not a place to say so. The adapter already logs one line.
            WorkspaceViewModel.RecordFailure("update.check", exception);
        }
    }

    /// <summary>
    /// The reader asked, in so many words, so this bypasses every throttle and always answers.
    /// </summary>
    /// <remarks>
    /// A side-loaded build never contacts Play at all: it is told the truth and offered the
    /// releases page. "Nothing happened" is not an answer to a question somebody typed, so the
    /// up-to-date and could-not-ask cases are stated out loud here, unlike a background check.
    /// </remarks>
    private async Task CheckForUpdatesManuallyAsync()
    {
        // An origin the platform could not resolve is asked anyway. Play is the authority on
        // whether it owns this app, and its answer — an offer, "nothing newer", or an error
        // reported as "could not be reached" — is better than the app guessing that a build it
        // cannot identify must be unsupported. The automatic check keeps the stricter rule: an
        // unproven origin is not a reason to interrupt anybody.
        var origin = PlatformSourceRegistry.GetInstallOrigin?.Invoke() ?? AppInstallOrigin.Unknown;
        var supported = origin is AppInstallOrigin.PlayStore or AppInstallOrigin.Unknown;
        if (!supported || PlatformSourceRegistry.CheckForAppUpdateAsync is not { } check)
        {
            RenderUpdatePrompt(
                AppUpdatePolicy.Decide(
                    new AppUpdateStatus(AppUpdateState.Unsupported),
                    UpdateChannel,
                    liveCaptureRunning: false,
                    UpdateMemory(),
                    DateTimeOffset.UtcNow,
                    manual: true,
                    origin),
                manual: true);
            return;
        }

        ShowNotice("Asking Google Play about updates…");
        try
        {
            var status = await check(_updateLifetime.Token).ConfigureAwait(true);
            await RecordUpdateCheckedAsync().ConfigureAwait(true);
            RenderUpdateStatus(status, manual: true);
        }
        catch (OperationCanceledException)
        {
            // The view is going away; there is nobody left to tell.
        }
        catch (Exception exception)
        {
            WorkspaceViewModel.RecordFailure("update.check", exception);
            RenderUpdatePrompt(
                AppUpdatePolicy.Decide(
                    new AppUpdateStatus(AppUpdateState.Unknown),
                    UpdateChannel,
                    liveCaptureRunning: false,
                    UpdateMemory(),
                    DateTimeOffset.UtcNow,
                    manual: true),
                manual: true);
        }
    }

    /// <summary>Turns one store answer into at most one message.</summary>
    internal void RenderUpdateStatus(AppUpdateStatus status, bool manual = false)
    {
        ArgumentNullException.ThrowIfNull(status);
        var prompt = AppUpdatePolicy.Decide(
            status,
            UpdateChannel,
            _viewModel.ActiveLiveCapture is not null,
            UpdateMemory(),
            DateTimeOffset.UtcNow,
            manual);
        _lastUpdateStatus = status;

        // So a view rebuilt by an activity recreation renders this without asking Play again.
        PlatformSourceRegistry.CacheAppUpdateStatus(status);
        RenderUpdatePrompt(prompt, manual);
    }

    private AppUpdateStatus _lastUpdateStatus = AppUpdateStatus.None;

    /// <summary>
    /// Puts a decided message into the lane, or holds it until the lane is free.
    /// </summary>
    /// <remarks>
    /// The rule this enforces is the one most likely to be got wrong, and getting it wrong is a
    /// regression rather than a cosmetic issue: <c>ShowNotice</c> replaces whatever the lane is
    /// carrying, and the lane is also where a failed export, a failed cleanup and a recording
    /// that stopped without being asked to are reported. An update offer arriving on resume
    /// would erase an unread capture failure. So an update may only take a lane that is empty
    /// or carrying an <c>Information</c> notice; anything else, it waits.
    ///
    /// A manual check is exempt, because the reader has just asked and is looking at the screen.
    /// </remarks>
    private void RenderUpdatePrompt(AppUpdatePrompt? prompt, bool manual)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RenderUpdatePrompt(prompt, manual));
            return;
        }

        if (prompt is null)
        {
            // Nothing to say. Take down an offer this view raised that no longer applies —
            // a download that finished, a capture that started — and leave everything else.
            if (_updateNoticeRevision != 0)
            {
                RetractNotice(_updateNoticeRevision);
                _updateNoticeRevision = 0;
            }

            _pendingUpdatePrompt = null;
            return;
        }

        var holding = HoldingNoticeKind;
        var lockedByReader = holding is NoticeKind.Completion or NoticeKind.Failure &&
                             _noticeRevision != _updateNoticeRevision;
        if (!manual && lockedByReader)
        {
            _pendingUpdatePrompt = prompt;
            return;
        }

        _pendingUpdatePrompt = null;
        ShowNotice(
            prompt.Message,
            prompt.Persistent ? NoticeKind.Completion : NoticeKind.Information,
            prompt.ActionLabel is null
                ? null
                : new NoticeAction(prompt.ActionLabel, () => RunUpdateActionAsync(prompt.Action)),
            prompt.Persistent ? () => RecordUpdateDismissed() : null);
        _updateNoticeRevision = prompt.Persistent ? _noticeRevision : 0;
    }

    /// <summary>
    /// The lane may have become free, or the reason an offer was withheld may have gone away.
    /// </summary>
    /// <remarks>
    /// Called when a live capture starts or ends and when the lane is cleared. A capture ending
    /// is the moment an install that was deferred becomes possible, and re-deciding from the
    /// last status is what makes "it re-offers as soon as the capture ends" true rather than
    /// aspirational.
    /// </remarks>
    private void ReconsiderUpdateNotice()
    {
        if (_lastUpdateStatus.State == AppUpdateState.Unknown && _pendingUpdatePrompt is null)
        {
            return;
        }

        if (_lastUpdateStatus.State != AppUpdateState.Unknown)
        {
            RenderUpdateStatus(_lastUpdateStatus);
            return;
        }

        if (_pendingUpdatePrompt is { } pending)
        {
            RenderUpdatePrompt(pending, manual: false);
        }
    }

    private async Task RunUpdateActionAsync(AppUpdatePromptAction action)
    {
        try
        {
            switch (action)
            {
                case AppUpdatePromptAction.StartFlexible:
                case AppUpdatePromptAction.StartImmediate:
                    await StartUpdateFlowAsync(
                        action == AppUpdatePromptAction.StartImmediate
                            ? AppUpdateFlow.Immediate
                            : AppUpdateFlow.Flexible).ConfigureAwait(true);
                    break;

                case AppUpdatePromptAction.CompleteInstall:
                    await CompleteUpdateInstallAsync().ConfigureAwait(true);
                    break;

                case AppUpdatePromptAction.OpenStore:
                    if (PlatformSourceRegistry.OpenAppStoreListingAsync is { } openStore)
                    {
                        await openStore(_updateLifetime.Token).ConfigureAwait(true);
                    }

                    break;

                case AppUpdatePromptAction.OpenReleases:
                    await OpenReleasesPageAsync().ConfigureAwait(true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            WorkspaceViewModel.RecordFailure("update.action", exception);
            ShowNotice(
                $"That update step did not run · {WorkspaceViewModel.FriendlyMessage(exception)}",
                NoticeKind.Failure);
            _updateNoticeRevision = 0;
        }
    }

    private async Task StartUpdateFlowAsync(AppUpdateFlow flow)
    {
        if (PlatformSourceRegistry.StartAppUpdateAsync is not { } start)
        {
            return;
        }

        // The store's take-the-screen flow installs and restarts the process, so it may not be
        // started over a recording however the offer was reached. The policy already withholds
        // it; this is the second, independent guard, because the cost of the rule failing is a
        // reader's unfinalized session rather than an inconvenience.
        var effective = _viewModel.ActiveLiveCapture is not null ? AppUpdateFlow.Flexible : flow;
        var started = await start(effective, _updateLifetime.Token).ConfigureAwait(true);
        if (started)
        {
            return;
        }

        // The offer went stale — Play allows exactly one flow per answer. Ask again rather
        // than retrying with an object the store has already spent.
        if (PlatformSourceRegistry.CheckForAppUpdateAsync is { } check)
        {
            RenderUpdateStatus(await check(_updateLifetime.Token).ConfigureAwait(true));
        }
    }

    private async Task CompleteUpdateInstallAsync()
    {
        if (PlatformSourceRegistry.CompleteAppUpdateAsync is not { } complete)
        {
            return;
        }

        if (_viewModel.ActiveLiveCapture is not null)
        {
            // Installing restarts the process, which would end the recording without the
            // drain-seal-reopen the in-app Stop performs. The app does not end a capture the
            // reader did not ask to end, so it says whose move this is and stops.
            ShowNotice(
                "Stop the capture first — installing the update restarts VisualCat.",
                NoticeKind.Failure);
            _updateNoticeRevision = 0;
            return;
        }

        await complete(_updateLifetime.Token).ConfigureAwait(true);
    }

    private async Task OpenReleasesPageAsync()
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            ShowNotice($"Releases are published at {AppUpdatePolicy.ReleasesUrl}", NoticeKind.Completion);
            _updateNoticeRevision = 0;
            return;
        }

        await launcher.LaunchUriAsync(new Uri(AppUpdatePolicy.ReleasesUrl)).ConfigureAwait(true);
    }

    /// <summary>What this view remembers about update prompts, from the loaded settings.</summary>
    private AppUpdateMemory UpdateMemory() => new(
        _settings.UpdateDismissedVersionCode,
        _settings.UpdateSnoozedUntilUtc,
        _settings.UpdateLastCheckedUtc);

    /// <summary>
    /// Records that the reader declined this offer, and for how long that answer stands.
    /// </summary>
    private void RecordUpdateDismissed()
    {
        _updateNoticeRevision = 0;
        if (_lastUpdateStatus.AvailableVersionCode <= 0)
        {
            return;
        }

        _settings = _settings with
        {
            UpdateDismissedVersionCode = _lastUpdateStatus.AvailableVersionCode,
            UpdateSnoozedUntilUtc = AppUpdatePolicy.SnoozeUntil(UpdateChannel, DateTimeOffset.UtcNow),
        };
        _ = PersistUpdateMemoryAsync();
    }

    private Task RecordUpdateCheckedAsync()
    {
        _settings = _settings with { UpdateLastCheckedUtc = DateTimeOffset.UtcNow };
        return PersistUpdateMemoryAsync();
    }

    /// <summary>
    /// Writes the update memory, treating a failure as not worth the reader's attention.
    /// </summary>
    /// <remarks>
    /// The cost of losing one of these three values is that the reader is asked about an update
    /// once more than they should be — which is not a sentence worth putting on screen, and
    /// certainly not one worth putting in the lane that reports capture failures. A view being
    /// disposed while a dismissal is being written reaches here too, because the settings gate
    /// goes with it.
    /// </remarks>
    private async Task PersistUpdateMemoryAsync()
    {
        try
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is ObjectDisposedException or IOException or UnauthorizedAccessException
                or OperationCanceledException)
        {
            global::System.Diagnostics.Debug.WriteLine($"VisualCat update memory not persisted: {exception}");
        }
    }

    /// <summary>
    /// Forgets a dismissal the running build has already caught up with.
    /// </summary>
    /// <remarks>
    /// After a successful update the app relaunches at the very version code it was being
    /// nagged about, and the dismissal still names it. Left alone that value can suppress a
    /// later prompt if a subsequent release ever produced a lower code, and it accumulates as
    /// confusing state in a file people do read. The default of 0 is not a dismissal, so a
    /// build that has never refused anything leaves the check throttle alone.
    /// </remarks>
    private static ApplicationSettings ForgetSpentUpdateMemory(ApplicationSettings settings)
    {
        var running = ProductInfo.VersionCodeOf(ProductInfo.DisplayVersion);
        return settings.UpdateDismissedVersionCode > 0 && running >= settings.UpdateDismissedVersionCode
            ? settings with
            {
                UpdateDismissedVersionCode = 0,
                UpdateSnoozedUntilUtc = null,
                UpdateLastCheckedUtc = null,
            }
            : settings;
    }
}
