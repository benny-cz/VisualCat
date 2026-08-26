using Android.App;
using Android.Content;
using Android.Gms.Extensions;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using VisualCat.App.Platform;
using VisualCat.Domain;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace VisualCat.Android;

/// <summary>
/// Google Play's in-app update client, and the only part of VisualCat that knows Play exists.
/// </summary>
/// <remarks>
/// <para>
/// This is an IPC client for the Play Store app installed on the device. VisualCat opens no
/// socket, sends no identifier, and reaches no VisualCat-operated endpoint; the store performs
/// whatever network work is needed, under the account relationship the reader already has with
/// it. That is the whole of why the check is consistent with ADR 0017 and with the local-first
/// promise in docs/PRIVACY.md, and it is why a GitHub release feed would <em>not</em> have been.
/// </para>
/// <para>
/// The service is owned by the activity rather than by the view, because Android rebuilds
/// MainView on every recreation — a text-size change, "Don't keep activities", a low-memory
/// kill — while an <c>AppUpdateInfo</c> can start exactly one update flow before it goes stale.
/// State reaches the current view through
/// <see cref="PlatformSourceRegistry.PublishAppUpdateStatus"/>, which the newest view alone
/// answers.
/// </para>
/// <para>
/// Every entry point catches broadly and reports <see cref="AppUpdateState.Unknown"/> rather
/// than throwing. A device with no Play Store, a build Play does not own, an airplane-mode
/// device: none of these is something the reader can act on, and none of them may be allowed
/// to disturb startup.
/// </para>
/// </remarks>
internal sealed class PlayAppUpdateService : Java.Lang.Object, IInstallStateUpdatedListener
{
    private const string LogTag = "VisualCat.AppUpdate";

    private readonly ActivityResultLauncher _launcher;
#if VISUALCAT_FAKE_APP_UPDATE
    // Under the fake-update constant this field is only ever assigned a FakeAppUpdateManager,
    // and CA1859 asks for the concrete type. Keeping the interface is the point: the fake and
    // the real client must be interchangeable here, so that what the developer loop exercises
    // is the same code Release runs. The rule is correct about the devirtualisation and wrong
    // about what this field is for.
#pragma warning disable CA1859
#endif
    private readonly IAppUpdateManager _manager;
#if VISUALCAT_FAKE_APP_UPDATE
#pragma warning restore CA1859
#endif

    /// <summary>The live offer. Single-use, and it never leaves this class.</summary>
    private AppUpdateInfo? _offer;

    /// <summary>
    /// What the last answer said about the offered build, kept for the messages that cannot ask.
    /// </summary>
    /// <remarks>
    /// An <c>InstallState</c> carries a package name, a status and a byte count — no version
    /// code, and therefore no version name. Without this, the moment a download started the
    /// offer stopped being able to name itself and "Downloading VisualCat 2.1.1" became
    /// "Downloading a newer VisualCat", which is worse information at exactly the point the
    /// reader is watching a progress bar. Read from here rather than from the shared cache: the
    /// service is where the fact was learned, and the cache reflects whatever was last rendered.
    /// </remarks>
    private AppUpdateStatus _lastOffer = AppUpdateStatus.None;
#if VISUALCAT_FAKE_APP_UPDATE
    private readonly Xamarin.Google.Android.Play.Core.AppUpdate.Testing.FakeAppUpdateManager _fake;
#endif
    private bool _listening;
    private bool _disposed;

    internal PlayAppUpdateService(Activity activity, ActivityResultLauncher launcher)
    {
        _launcher = launcher;
#if VISUALCAT_FAKE_APP_UPDATE
        var fake = CreateFakeManager(activity);
        _fake = fake;
        _manager = fake;
#else
        _manager = AppUpdateManagerFactory.Create(activity);
#endif
    }

#if VISUALCAT_FAKE_APP_UPDATE
    /// <summary>
    /// Play Core's own scriptable stand-in, for the developer loop that cannot reach the real
    /// one.
    /// </summary>
    /// <remarks>
    /// Google Play never offers an update to a build installed by <c>adb install</c>, so the
    /// ordinary inner loop cannot exercise this path at all. The fake manager ships inside the
    /// production app-update.aar and drives the whole flow from the device. Opt in with
    /// <c>-p:VisualCatFakeAppUpdate=true</c>; a Release build cannot reach this line.
    /// </remarks>
    private static Xamarin.Google.Android.Play.Core.AppUpdate.Testing.FakeAppUpdateManager CreateFakeManager(
        Context context)
    {
        var fake = new Xamarin.Google.Android.Play.Core.AppUpdate.Testing.FakeAppUpdateManager(context);
        fake.SetUpdateAvailable(FakeUpdateVersionCode);
        fake.SetUpdatePriority(FakeUpdatePriority);
        fake.SetTotalBytesToDownload(FakeUpdateBytes);
        global::Android.Util.Log.Warn(
            LogTag,
            $"FAKE update manager active: offering versionCode {FakeUpdateVersionCode}. This build must never be published.");
        return fake;
    }

    /// <summary>A version code one patch above whatever this build is, so it decodes to a name.</summary>
    private static int FakeUpdateVersionCode =>
        (int)((ProductInfo.VersionCodeOf(ProductInfo.DisplayVersion) ?? 2000900) + 100);

    private const int FakeUpdatePriority = 2;
    private const long FakeUpdateBytes = 31L * 1024 * 1024;
#endif

    /// <summary>
    /// Registers the activity-result launcher Play's consent UI is started through.
    /// </summary>
    /// <remarks>
    /// AndroidX requires this before the activity is STARTED, so it is <c>OnCreate</c> or
    /// nowhere. The launcher overload is used rather than the request-code one because the
    /// latter is deprecated — which under <c>TreatWarningsAsErrors</c> would not compile — and
    /// because it does not contend for Avalonia's own single-assignment
    /// <c>AvaloniaActivity.ActivityResult</c> delegate.
    /// </remarks>
    internal static ActivityResultLauncher RegisterLauncher(
        AndroidX.Activity.ComponentActivity activity,
        Action<int> onResult)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(onResult);
        return activity.RegisterForActivityResult(
            new ActivityResultContracts.StartIntentSenderForResult(),
            new UpdateFlowResultCallback(onResult));
    }

    /// <summary>Asks Play what it has, and remembers the offer so a flow can be started from it.</summary>
    internal async Task<AppUpdateStatus> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await _manager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(true);
            if (info is null)
            {
                return new AppUpdateStatus(AppUpdateState.Unknown);
            }

            _offer = info;
            var status = Describe(info);
            Remember(status);

            // A flexible download that is already running has to be watched from wherever the
            // app rejoins it, including a cold start after the process was killed.
            if (status.State is AppUpdateState.Downloading)
            {
                StartListening();
            }

            return status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Info(LogTag, $"Update check unavailable: {exception.GetType().Name}");
            return new AppUpdateStatus(AppUpdateState.Unknown);
        }
    }

    /// <summary>
    /// Opens Play's own consent UI for one of the two flows.
    /// </summary>
    /// <returns>False when the offer had gone stale and a fresh check is needed.</returns>
    internal Task<bool> StartAsync(AppUpdateFlow flow, CancellationToken cancellationToken)
    {
        // Checked here rather than in a finally, where a throw would have discarded the return
        // value and masked anything else propagating. Starting a flow is a synchronous handoff
        // to Play; there is nothing to cancel once it has happened.
        cancellationToken.ThrowIfCancellationRequested();

        var offer = _offer;
        if (offer is null)
        {
            // Nothing to start a flow from — the check failed, or an earlier flow spent it.
            return Task.FromResult(false);
        }

        var type = flow == AppUpdateFlow.Immediate ? AppUpdateType.Immediate : AppUpdateType.Flexible;
        try
        {
            // AllowAssetPackDeletion is deliberately left at its default false. It exists to let
            // Play clear Play Asset Delivery packs when storage is tight; VisualCat ships none,
            // so setting it true would grant a permission over nothing — and plenty of published
            // snippets set it, so it is worth saying here rather than discovering in review.
            var options = AppUpdateOptions.DefaultOptions(type);
            if (!_manager.StartUpdateFlowForResult(offer, _launcher, options))
            {
                global::Android.Util.Log.Info(LogTag, "Play refused the update flow; the offer was stale.");
                _offer = null;
                return Task.FromResult(false);
            }

            // One AppUpdateInfo starts at most one flow. Anything further needs a fresh answer.
            _offer = null;
            if (flow == AppUpdateFlow.Flexible)
            {
                StartListening();
            }

#if VISUALCAT_FAKE_APP_UPDATE
            _ = DriveFakeFlowAsync(flow);
#endif
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            // True rather than false: the flow was attempted and failed, and the reader has
            // been told so. False means "the offer was stale, ask again", which would send the
            // caller round a loop that cannot succeed.
            global::Android.Util.Log.Warn(LogTag, $"Could not start the update flow: {exception.GetType().Name}");
            _offer = null;
            PlatformSourceRegistry.PublishAppUpdateStatus(new AppUpdateStatus(
                AppUpdateState.Failed,
                Message: "The update did not start. Try again from the Play Store."));
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Installs a finished flexible download. This restarts the process.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for having established that no live capture is running: the
    /// restart ends a recording without the drain-seal-reopen the in-app Stop performs, leaving
    /// an unfinalized session. Both the policy layer and the view guard this independently.
    /// </remarks>
    internal async Task CompleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _manager.CompleteUpdate().AsAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(LogTag, $"Could not complete the update: {exception.GetType().Name}");
            PlatformSourceRegistry.PublishAppUpdateStatus(new AppUpdateStatus(
                AppUpdateState.Failed,
                Message: "The update could not be installed. Try again from the Play Store."));
        }
    }

    /// <summary>
    /// The activity-lifecycle obligation Play imposes on both flows.
    /// </summary>
    /// <remarks>
    /// Flexible: a download may have finished while the app was backgrounded, and nothing else
    /// will say so. Immediate: a flow interrupted by Back leaves
    /// <c>DeveloperTriggeredUpdateInProgress</c>, which must be resumed. Driven from
    /// <c>MainActivity.OnResume</c> rather than through the view, because it is the activity's
    /// obligation and the view may be mid-rebuild.
    /// </remarks>
    internal void OnActivityResumed()
    {
        if (_disposed)
        {
            return;
        }

        _ = ResumeAsync();
    }

    private async Task ResumeAsync()
    {
        try
        {
            var info = await _manager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>().ConfigureAwait(true);
            if (info is null || _disposed)
            {
                return;
            }

            _offer = info;
            var status = Describe(info);
            Remember(status);

            // Only work the app already started is reported from here. A fresh offer goes
            // through the view's own throttle, which this must not be able to bypass.
            if (status.State is AppUpdateState.ReadyToInstall or AppUpdateState.InProgress or AppUpdateState.Downloading)
            {
                if (status.State == AppUpdateState.Downloading)
                {
                    StartListening();
                }

                PlatformSourceRegistry.PublishAppUpdateStatus(status);
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Info(LogTag, $"Resume update re-check unavailable: {exception.GetType().Name}");
        }
    }

    /// <summary>Play reported progress on a flexible download.</summary>
    public void OnStateUpdate(InstallState? state)
    {
        if (_disposed || state is null)
        {
            return;
        }

        var status = state.InstallStatus() switch
        {
            InstallStatus.Downloading => new AppUpdateStatus(
                AppUpdateState.Downloading,
                AvailableVersionCode: 0,
                AvailableVersionName: null,
                BytesDownloaded: state.BytesDownloaded(),
                TotalBytesToDownload: state.TotalBytesToDownload()),
            InstallStatus.Downloaded => new AppUpdateStatus(AppUpdateState.ReadyToInstall),
            InstallStatus.Failed => new AppUpdateStatus(
                AppUpdateState.Failed,
                Message: $"The update download did not finish (Play error {state.InstallErrorCode()}). Try again from the Play Store."),
            // Not UpToDate. The reader cancelled a download; the newer build is still out
            // there, and recording "you are on the latest version" would be a plain untruth
            // that the shared cache would then hand to the next view that asked.
            InstallStatus.Canceled => new AppUpdateStatus(AppUpdateState.Unknown),
            _ => null,
        };

        // The listener holds a reference to this service and, through it, to the activity.
        // Leaving it registered past the end of the flow leaks both.
        if (state.InstallStatus() is InstallStatus.Downloaded or InstallStatus.Installed
            or InstallStatus.Failed or InstallStatus.Canceled)
        {
            StopListening();
        }

        if (status is null)
        {
            return;
        }

        // The offered version name is not on an InstallState, so carry across what the last
        // check decoded rather than losing the version out of a message halfway through.
        PlatformSourceRegistry.PublishAppUpdateStatus(WithOfferIdentity(status));
    }

    /// <summary>Keeps the parts of an answer that later messages have no other way to learn.</summary>
    private void Remember(AppUpdateStatus status)
    {
        if (status.AvailableVersionCode > 0)
        {
            _lastOffer = status;
        }
    }

    /// <summary>Puts the offered build's identity back onto a status that could not carry it.</summary>
    private AppUpdateStatus WithOfferIdentity(AppUpdateStatus status) => status with
    {
        AvailableVersionCode = _lastOffer.AvailableVersionCode,
        AvailableVersionName = _lastOffer.AvailableVersionName,
        Priority = _lastOffer.Priority,
    };

#if VISUALCAT_FAKE_APP_UPDATE
    /// <summary>
    /// Plays the part of the reader inside Play's consent UI, and of Play inside the download.
    /// </summary>
    /// <remarks>
    /// <c>FakeAppUpdateManager</c> starts a flow and then waits to be told what happened, because
    /// in an instrumented test the test is the one that decides. On a device there is nobody to
    /// decide, so this accepts, downloads in visible steps, and finishes — which is what makes
    /// the Downloading and Ready-to-install messages, and the live-capture guard over them,
    /// something that can actually be seen on hardware. Compiled only under the fake constant.
    /// </remarks>
    private async Task DriveFakeFlowAsync(AppUpdateFlow flow)
    {
        try
        {
            _fake.UserAcceptsUpdate();
            if (flow == AppUpdateFlow.Immediate)
            {
                // Play would have taken the screen and restarted the process by now.
                _fake.DownloadStarts();
                _fake.DownloadCompletes();
                return;
            }

            _fake.DownloadStarts();
            for (var step = 1; step <= 4; step++)
            {
                await Task.Delay(700).ConfigureAwait(true);
                if (_disposed)
                {
                    return;
                }

                _fake.SetBytesDownloaded(FakeUpdateBytes * step / 4);

                // SetBytesDownloaded does not notify listeners on its own, so the progress
                // message is published here rather than arriving through OnStateUpdate.
                PlatformSourceRegistry.PublishAppUpdateStatus(WithOfferIdentity(new AppUpdateStatus(
                    AppUpdateState.Downloading,
                    BytesDownloaded: FakeUpdateBytes * step / 4,
                    TotalBytesToDownload: FakeUpdateBytes)));
            }

            await Task.Delay(400).ConfigureAwait(true);
            if (!_disposed)
            {
                _fake.DownloadCompletes();
            }
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(LogTag, $"Fake update flow stopped: {exception.GetType().Name}");
        }
    }
#endif

    /// <summary>Turns one Play answer into the snapshot the shared layer works from.</summary>
    private static AppUpdateStatus Describe(AppUpdateInfo info)
    {
        var versionCode = info.AvailableVersionCode();
        var name = ProductInfo.VersionNameOf(versionCode);

        // Java's Integer, and genuinely null much of the time: Play only reports staleness once
        // the newer build has been available to this user for a while. Unwrapping it unguarded
        // is the classic crash here, and treating null as zero would quietly turn "Play did not
        // say" into "this release is brand new", which suppresses the stale-build escalation.
        int? staleness = info.ClientVersionStalenessDays()?.IntValue();

        var state = info.UpdateAvailability() switch
        {
            UpdateAvailability.UpdateAvailable => info.InstallStatus() switch
            {
                InstallStatus.Downloaded => AppUpdateState.ReadyToInstall,
                InstallStatus.Downloading or InstallStatus.Pending => AppUpdateState.Downloading,
                _ => AppUpdateState.Available,
            },
            UpdateAvailability.DeveloperTriggeredUpdateInProgress => info.InstallStatus() switch
            {
                InstallStatus.Downloaded => AppUpdateState.ReadyToInstall,
                InstallStatus.Downloading or InstallStatus.Pending => AppUpdateState.Downloading,
                _ => AppUpdateState.InProgress,
            },
            UpdateAvailability.UpdateNotAvailable => AppUpdateState.UpToDate,

            // Not UpToDate. Play saying "unknown" is Play declining to answer, and reporting
            // that as good news is the one thing an offline device must never do.
            _ => AppUpdateState.Unknown,
        };

        return new AppUpdateStatus(
            state,
            versionCode,
            name,
            info.UpdatePriority(),
            staleness,
            info.IsUpdateTypeAllowed(AppUpdateType.Flexible),
            info.IsUpdateTypeAllowed(AppUpdateType.Immediate),
            info.BytesDownloaded(),
            info.TotalBytesToDownload());
    }

    private void StartListening()
    {
        if (_listening || _disposed)
        {
            return;
        }

        _manager.RegisterListener(this);
        _listening = true;
    }

    private void StopListening()
    {
        if (!_listening)
        {
            return;
        }

        try
        {
            _manager.UnregisterListener(this);
        }
        catch (Exception exception)
        {
            // Play can tear its binder down before the activity finishes disposing. The
            // listener is already unreachable with this service, so cleanup remains complete.
            global::Android.Util.Log.Info(LogTag, $"Update listener was already gone: {exception.GetType().Name}");
        }
        finally
        {
            _listening = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            StopListening();

            _offer = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Where this installation came from, which decides whether it can update itself at all.
    /// </summary>
    /// <remarks>
    /// <c>getInstallSourceInfo</c> is API 30 and needs no permission for the caller's own
    /// package; the minimum here is 31. A null installer means the package arrived as a file —
    /// <c>adb install</c>, a file manager, the APK attached to a GitHub release — and Play will
    /// never offer such a build an update, however new the store's copy is.
    /// </remarks>
    internal static AppInstallOrigin ResolveInstallOrigin(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var installer = context.PackageManager?
                .GetInstallSourceInfo(context.PackageName!)?
                .InstallingPackageName;
            return installer switch
            {
                "com.android.vending" => AppInstallOrigin.PlayStore,
                null or "" => AppInstallOrigin.SideLoaded,
                _ => AppInstallOrigin.OtherStore,
            };
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Info(LogTag, $"Install origin unavailable: {exception.GetType().Name}");
            return AppInstallOrigin.Unknown;
        }
    }

    /// <summary>
    /// Opens this app's Google Play listing.
    /// </summary>
    /// <remarks>
    /// The intent is launched rather than resolved. <c>ResolveActivity</c> and
    /// <c>QueryIntentActivities</c> are both subject to Android 11+ package-visibility
    /// filtering and would need a <c>&lt;queries&gt;</c> entry, which would then have to be
    /// justified against the release manifest allowlist in <c>tools/package-android.ps1</c>.
    /// Launching directly and catching the failure needs nothing, and the https form is the
    /// fallback for a device where the Store is absent or disabled.
    /// </remarks>
    internal static void OpenStoreListing(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var package = activity.PackageName;
        try
        {
            activity.StartActivity(new Intent(
                Intent.ActionView,
                global::Android.Net.Uri.Parse($"market://details?id={package}")));
        }
        catch (Exception exception) when (exception is ActivityNotFoundException or Java.Lang.SecurityException)
        {
            activity.StartActivity(new Intent(
                Intent.ActionView,
                global::Android.Net.Uri.Parse($"https://play.google.com/store/apps/details?id={package}")));
        }
    }

    /// <summary>Play's own consent UI closed, and this is what the reader chose.</summary>
    /// <remarks>
    /// A cancelled or failed flow is reported so the lane can say so; a flow the reader
    /// accepted needs no message, because the download's own progress is the next thing they
    /// will see. An Immediate flow usually never gets here at all: Play installs and restarts.
    /// </remarks>
    internal void OnFlowResult(int resultCode)
    {
        if (_disposed)
        {
            return;
        }

        switch (resultCode)
        {
            case (int)Result.Ok:
                break;

            case (int)Result.Canceled:
                // The reader said no inside Play's own sheet, so the banner they have already
                // answered comes down. Unknown rather than UpToDate: declining an update does
                // not make this the newest build, and UpToDate is cached and handed to the next
                // view that asks. "No current answer" is the true one, and the ordinary
                // throttle decides when to ask again.
                StopListening();
                PlatformSourceRegistry.PublishAppUpdateStatus(new AppUpdateStatus(AppUpdateState.Unknown));
                break;

            default:
                StopListening();
                PlatformSourceRegistry.PublishAppUpdateStatus(new AppUpdateStatus(
                    AppUpdateState.Failed,
                    Message: "The update did not start. Try again from the Play Store."));
                break;
        }
    }

    /// <summary>Bridges AndroidX's activity-result contract back to a plain result code.</summary>
    private sealed class UpdateFlowResultCallback(Action<int> onResult) : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result) =>
            onResult(result is AndroidX.Activity.Result.ActivityResult activityResult ? activityResult.ResultCode : (int)Result.Canceled);
    }
}
