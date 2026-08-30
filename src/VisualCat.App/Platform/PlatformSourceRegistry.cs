using VisualCat.Application.Ports;

namespace VisualCat.App.Platform;

/// <summary>
/// A file another app handed to VisualCat, and the name that app calls it.
/// </summary>
/// <remarks>
/// The path is a private cache file whose name has to be unique, so it carries a UTC stamp
/// and a GUID; the display name is what the provider says the document is called. They were
/// the same string, so opening <c>tiny.txt</c> through Open with produced a tab named
/// <c>20260821-200744-8fde316a7e2447a281e7e825dfc2d0f5-raw:_storage_emulated_0_Download_tiny.txt</c>
/// and read the whole of it — timestamp, GUID, provider document id and absolute shared-storage
/// path — to a screen reader (finding F-27). Two facts, two fields.
/// </remarks>
/// <param name="Path">Where the bytes are, which only the importer needs.</param>
/// <param name="DisplayName">What to call it on screen.</param>
public sealed record IncomingFile(string Path, string DisplayName);

/// <summary>
/// The two values Android shows in its Wireless debugging pairing-code panel.
/// </summary>
/// <remarks>
/// The pairing code is deliberately carried only for the duration of one explicit setup
/// attempt. Platform implementations must not persist or log it.
/// </remarks>
/// <param name="PairingPort">The TCP port shown after the colon in Android Settings.</param>
/// <param name="PairingCode">The six ASCII digits shown by Android Settings.</param>
public sealed record WirelessAdbPairingRequest(int PairingPort, string PairingCode);

/// <summary>The result of connecting VisualCat to Android Wireless debugging.</summary>
/// <param name="Connected">Whether an authenticated ADB connection is ready for capture.</param>
/// <param name="PairingSucceeded">Whether this attempt completed a new Wireless ADB pairing.</param>
/// <param name="Message">A short, safe explanation suitable for the setup sheet.</param>
public sealed record WirelessAdbConnectionResult(
    bool Connected,
    bool PairingSucceeded,
    string Message);

/// <summary>Why the host platform asked a running Live capture to stop.</summary>
public enum PlatformLiveCaptureStopReason
{
    /// <summary>The reader pressed the platform notification's Stop and save action.</summary>
    NotificationAction,

    /// <summary>The operating system's foreground-work allowance expired.</summary>
    SystemTimeLimit,
}

/// <summary>Where this installation came from, which decides whether it can update itself.</summary>
public enum AppInstallOrigin
{
    /// <summary>The platform could not answer, so nothing may be claimed either way.</summary>
    Unknown,

    /// <summary>Installed by Google Play, which is the only origin that can update in place.</summary>
    PlayStore,

    /// <summary>Installed by another store or an enterprise installer.</summary>
    OtherStore,

    /// <summary>Installed from a file — the GitHub release APK, or a developer deploy.</summary>
    SideLoaded,
}

/// <summary>Which of the store's two update experiences to start.</summary>
public enum AppUpdateFlow
{
    /// <summary>Downloads in the background; the app keeps running until the install is completed.</summary>
    Flexible,

    /// <summary>The store takes the screen, installs, and restarts the app.</summary>
    Immediate,
}

/// <summary>What the platform's store currently knows about a newer build of this app.</summary>
public enum AppUpdateState
{
    /// <summary>Not asked yet, or the store could not answer.</summary>
    Unknown,

    /// <summary>This installation cannot update itself — side-loaded, or no store present.</summary>
    Unsupported,

    /// <summary>The store has nothing newer for this user.</summary>
    UpToDate,

    /// <summary>A newer build exists and a flow can be started.</summary>
    Available,

    /// <summary>The store is downloading in the background.</summary>
    Downloading,

    /// <summary>Downloaded and waiting to be installed, which restarts the app.</summary>
    ReadyToInstall,

    /// <summary>An update the app started earlier is still running and should be resumed.</summary>
    InProgress,

    /// <summary>The last attempt failed. <see cref="AppUpdateStatus.Message"/> says how.</summary>
    Failed,
}

/// <summary>One answer from the platform's store about updating this app.</summary>
/// <remarks>
/// Deliberately a snapshot rather than a live object: Play Core's <c>AppUpdateInfo</c> can
/// start exactly one update flow and goes stale afterwards, so the shared layer must never
/// hold one. The Android adapter keeps the live object and hands out this record.
///
/// <see cref="AvailableVersionName"/> is decoded from the version code by the app, because
/// the store supplies no version name and cannot say which track the build came from. It is
/// null when the code does not decode plausibly, and the UI then declines to name a version
/// rather than inventing one.
/// </remarks>
/// <param name="State">What the store is currently reporting.</param>
/// <param name="AvailableVersionCode">The version code the store would install, or 0.</param>
/// <param name="AvailableVersionName">That code decoded to a version name, where it decodes.</param>
/// <param name="Priority">The 0-5 urgency fixed into the release at upload time.</param>
/// <param name="StalenessDays">Days since this user could first have had the newer build, where known.</param>
/// <param name="FlexibleAllowed">Whether a background download may be started.</param>
/// <param name="ImmediateAllowed">Whether the store's take-the-screen flow may be started.</param>
/// <param name="BytesDownloaded">Progress of a download in flight.</param>
/// <param name="TotalBytesToDownload">Size of a download in flight, where the store reports it.</param>
/// <param name="Message">Why a <see cref="AppUpdateState.Failed"/> attempt failed.</param>
public sealed record AppUpdateStatus(
    AppUpdateState State,
    long AvailableVersionCode = 0,
    string? AvailableVersionName = null,
    int Priority = 0,
    int? StalenessDays = null,
    bool FlexibleAllowed = false,
    bool ImmediateAllowed = false,
    long BytesDownloaded = 0,
    long TotalBytesToDownload = 0,
    string? Message = null)
{
    /// <summary>Nothing has been asked yet.</summary>
    public static AppUpdateStatus None { get; } = new(AppUpdateState.Unknown);
}

public static class PlatformSourceRegistry
{
    /// <summary>
    /// The device's own text-size setting, as a multiplier, or null where the platform has
    /// none. Published before the first view is built; see <see cref="VisualCat.App.TextScale"/>.
    /// </summary>
    public static double? PlatformFontScale { get; set; }

    public static Func<ILogSource?>? CreateOnDeviceSource { get; set; }

    /// <summary>
    /// Whether the platform currently grants this app the whole device's log, or null where
    /// the platform has no such distinction.
    /// </summary>
    /// <remarks>
    /// The old pre-capture explanation was unconditional: it promised that Android would ask
    /// for device-log access even when the platform could not do so. On a clean install — where
    /// READ_LOGS is not held — no direct log-access sheet can appear at all, because READ_LOGS is not a runtime
    /// permission and an app cannot request it. The product then contradicted itself, since
    /// Session info correctly explained the same state and gave the exact grant command
    /// (finding F-13). The shell has to be able to ask before it writes the sentence.
    /// </remarks>
    public static Func<bool>? HasFullDeviceLogPermission { get; set; }

    /// <summary>
    /// The exact command an advanced user can run from an external ADB host to grant the
    /// development-only READ_LOGS permission, or null where nothing of the sort applies.
    /// </summary>
    /// <remarks>
    /// VisualCat never executes this command itself. Google Play builds use Wireless debugging
    /// as the log transport instead of using ADB to elevate the app's own permission state.
    /// Keeping the command available preserves the established developer/debug workflow for
    /// people who explicitly configure it from a separate trusted ADB host.
    /// </remarks>
    public static string? FullDeviceLogGrantCommand { get; set; }

    /// <summary>
    /// Whether VisualCat has a reusable identity and a successful previous explicit pairing.
    /// </summary>
    /// <remarks>
    /// The platform must not return true for an identity merely generated during a failed
    /// attempt. Android may still have forgotten the completed pairing, Wireless debugging may
    /// be off, or the current network may be unavailable.
    /// </remarks>
    public static Func<bool>? HasSavedWirelessAdbIdentity { get; set; }

    /// <summary>
    /// Pairs with Android's Wireless debugging daemon and leaves an authenticated connection
    /// ready for a full-device logcat capture. Null on platforms that cannot offer this path.
    /// </summary>
    /// <remarks>
    /// The app layer can provide only Android's pairing port and six-digit code. It cannot send
    /// an arbitrary ADB command. The Android implementation uses the resulting shell strictly as
    /// a log transport and does not grant privileged permissions to the VisualCat package.
    /// </remarks>
    public static Func<WirelessAdbPairingRequest, CancellationToken, Task<WirelessAdbConnectionResult>>?
        PairWirelessAdbAsync
    { get; set; }

    /// <summary>
    /// Reuses a Wireless debugging identity Android has already paired and leaves the authenticated
    /// connection ready for a full-device logcat capture.
    /// </summary>
    public static Func<CancellationToken, Task<WirelessAdbConnectionResult>>?
        ConnectSavedWirelessAdbAsync
    { get; set; }

    /// <summary>
    /// Creates a full-device log source from the authenticated Wireless ADB connection prepared by
    /// <see cref="PairWirelessAdbAsync"/> or <see cref="ConnectSavedWirelessAdbAsync"/>.
    /// </summary>
    public static Func<ILogSource?>? CreateWirelessAdbSource { get; set; }

    /// <summary>
    /// Makes a reader-started Live capture explicit to a mobile operating system while it is
    /// running in the background, and returns the lease that removes that state on completion.
    /// </summary>
    /// <remarks>
    /// Android implements this with a user-visible data-sync foreground service. The callback
    /// is deliberately a graceful-stop request rather than source disposal: notification Stop
    /// and Android's service timeout must drain, seal and reopen the session through the same
    /// path as the in-app Stop action. Desktop hosts leave this null.
    /// </remarks>
    public static Func<string, Action<PlatformLiveCaptureStopReason>, IDisposable>?
        BeginLiveCaptureBackgroundExecution
    { get; set; }

    /// <summary>
    /// Opens Android's Developer options focused on Wireless debugging, falling back to
    /// general Settings on platform builds that do not expose Developer options.
    /// </summary>
    public static Func<CancellationToken, Task>? OpenWirelessDebuggingSettingsAsync { get; set; }

    public static Func<string, CancellationToken, Task>? ShareFileAsync { get; set; }
    public static Func<CancellationToken, Task<IReadOnlyList<IncomingFile>>>? ConsumeLaunchFilesAsync { get; set; }

    public static event Action<IReadOnlyList<IncomingFile>>? LaunchFilesReceived;
    public static event Action? AppResumed;

    /// <summary>
    /// The device changed something about how the app is displayed — text size, display
    /// density, locale, layout direction — without the app being torn down for it.
    /// </summary>
    /// <remarks>
    /// Android's default is to destroy and recreate the activity for each of these, which on
    /// this product costs the reader a running capture and about ten seconds of blank
    /// workspace while every session is reopened from disk (audit 3, A2 and C3). The activity
    /// declares that it handles them instead, and this is how it says so to the part of the
    /// app that has to answer: the capture keeps running and the views are rebuilt at the new
    /// scale, which is all the recreation was achieving.
    /// </remarks>
    public static event Action? DisplayConfigurationChanged;

    /// <summary>
    /// Raised when the app leaves the foreground — backgrounded, or the screen turned
    /// off. A live capture keeps running; the workspace uses this to stop doing work
    /// whose only product is a picture nobody can see.
    /// </summary>
    public static event Action? AppPaused;

    /// <summary>
    /// Asks the platform to let the app have the touches inside these rectangles, even where
    /// they overlap one of the platform's own edge gestures. Coordinates are device pixels
    /// relative to the top-level's window; an empty list releases every previous claim.
    /// </summary>
    /// <remarks>
    /// Installed only by a host that has such gestures — Android's back swipe, which owns a
    /// strip about 30 dp wide down both edges of the screen and would otherwise take a
    /// drag-to-pan on the heat map and send the reader to the home screen (finding F-28).
    /// Where this is null, <see cref="EdgeGestureGuard"/> does nothing at all.
    /// </remarks>
    public static Action<IReadOnlyList<Avalonia.PixelRect>>? SetGestureExclusions { get; set; }

    /// <summary>
    /// Peels the topmost layer the reader has open, and says whether there was one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The application's answer to a platform Back, installed by the shell and called by the
    /// host's own back contract. A stock gesture-navigation Pixel left the app for the
    /// launcher while the <em>More actions</em> sheet and the <em>Appearance</em> card were
    /// open, although the same layers closed correctly for a <c>KEYCODE_BACK</c> press
    /// (V2-21): the app owned no back callback of its own and depended entirely on the
    /// toolkit's, so which mechanism the platform chose decided whether the layer stack was
    /// consulted at all.
    /// </para>
    /// <para>
    /// This is that decision, made once, in the order the layers are stacked in: dialog,
    /// sheet, drawer, then the platform's own default. <see langword="false"/> means the app
    /// has nothing open and the host should do what it does everywhere else.
    /// </para>
    /// </remarks>
    public static Func<bool>? TryNavigateBack { get; set; }

    /// <summary>Where this installation came from, or null on a platform with no such notion.</summary>
    /// <remarks>
    /// Its presence is also what decides whether the Check for updates command is offered at
    /// all, the way <see cref="ShareFileAsync"/> decides whether Share is: a host that cannot
    /// say where it was installed from cannot honestly answer the question either.
    /// </remarks>
    public static Func<AppInstallOrigin>? GetInstallOrigin { get; set; }

    /// <summary>
    /// Asks the platform's store whether a newer build is available to this user. Null on
    /// platforms with no store; must never throw.
    /// </summary>
    /// <remarks>
    /// Android implements this against Google Play's in-app update client, which is an IPC
    /// call into the Play Store app already installed on the device. VisualCat opens no
    /// socket, sends no identifier and reaches no VisualCat-operated endpoint; see
    /// docs/PRIVACY.md and docs/adr/0019-app-updates.md.
    /// </remarks>
    public static Func<CancellationToken, Task<AppUpdateStatus>>? CheckForAppUpdateAsync { get; set; }

    /// <summary>
    /// Starts the store's own update experience. Returns false when the offer went stale and
    /// the caller should check again.
    /// </summary>
    public static Func<AppUpdateFlow, CancellationToken, Task<bool>>? StartAppUpdateAsync { get; set; }

    /// <summary>
    /// Installs an update that has already been downloaded. This restarts the process, so
    /// callers must have established that no live capture is running.
    /// </summary>
    public static Func<CancellationToken, Task>? CompleteAppUpdateAsync { get; set; }

    /// <summary>
    /// Opens this app's page in the platform's own store, for the cases the API cannot serve.
    /// </summary>
    public static Func<CancellationToken, Task>? OpenAppStoreListingAsync { get; set; }

    /// <summary>The most recent status, so a rebuilt view can render without asking again.</summary>
    public static AppUpdateStatus LastAppUpdateStatus { get; private set; } = AppUpdateStatus.None;

    /// <summary>The store reported new state about an update.</summary>
    public static event Action<AppUpdateStatus>? AppUpdateStatusChanged;

    /// <summary>
    /// Reports what the store said, and remembers it for whichever view asks next.
    /// </summary>
    /// <remarks>
    /// Android rebuilds <see cref="VisualCat.App.Views.MainView"/> on every activity
    /// recreation, and the service that owns the Play client outlives those rebuilds. Caching
    /// the last answer is what lets a fresh view render an offer that is already in flight
    /// without a second IPC round trip.
    /// </remarks>
    public static void PublishAppUpdateStatus(AppUpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        LastAppUpdateStatus = status;
        AppUpdateStatusChanged?.Invoke(status);
    }

    /// <summary>
    /// Remembers a status the current view already has, without telling it again.
    /// </summary>
    /// <remarks>
    /// An answer to <see cref="CheckForAppUpdateAsync"/> is returned to the caller, so raising
    /// the event as well would render it twice — and, for a check the reader asked for, the
    /// second render would arrive on the dispatcher after the first and overwrite the more
    /// specific message with the ordinary one. The cache still has to be warmed, because it is
    /// what a view rebuilt by an activity recreation renders from.
    /// </remarks>
    public static void CacheAppUpdateStatus(AppUpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        LastAppUpdateStatus = status;
    }

    public static void PublishLaunchFiles(IReadOnlyList<IncomingFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > 0)
        {
            LaunchFilesReceived?.Invoke(files);
        }
    }

    public static void PublishAppResumed() => AppResumed?.Invoke();

    public static void PublishDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();

    public static void PublishAppPaused() => AppPaused?.Invoke();

    /// <summary>Reports a desktop window becoming visible or being minimized.</summary>
    public static void PublishWindowVisibility(bool visible)
    {
        if (visible)
        {
            AppResumed?.Invoke();
        }
        else
        {
            AppPaused?.Invoke();
        }
    }
}
