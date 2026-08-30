using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using AndroidX.Activity.Result;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia.Android;
using VisualCat.App.Platform;
using VisualCat.Application.Ports;

namespace VisualCat.Android;

[Activity(
    Label = "VisualCat",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    // The soft keyboard shrinks the viewport instead of sliding the window out from under
    // itself, which is what lets the filter drawer keep its footer reachable while a query is
    // being typed. Screen-layout and smallest-width changes are handled in place for the same
    // reason orientation is: a live capture must survive them.
    //
    // FontScale, Density, Locale and LayoutDirection are on the list for exactly that reason
    // and were missing from it. Changing the system text size — the ordinary accessibility
    // control, the one somebody who cannot read the log they are recording reaches for —
    // destroyed the activity and killed the running capture with it, silently: Follow and Stop
    // capture simply disappeared and the status line changed tense (audit 3, A2). The same
    // recreation left a million-entry session showing an empty list under the word "Ready" for
    // ten seconds while it reopened from disk (audit 3, C3). Handling them in place costs a
    // rebuild of the views at the new scale, which OnConfigurationChanged below does.
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation |
                           ConfigChanges.ScreenSize |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize |
                           ConfigChanges.KeyboardHidden |
                           ConfigChanges.UiMode |
                           ConfigChanges.FontScale |
                           ConfigChanges.Density |
                           ConfigChanges.Locale |
                           ConfigChanges.LayoutDirection)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["file", "content"],
    DataMimeType = "text/plain")]
public sealed class MainActivity : AvaloniaMainActivity
{
    private static WeakReference<MainActivity>? s_current;
    private readonly HashSet<string> _consumedUris = new(StringComparer.Ordinal);
    private WirelessAdbService? _wirelessAdbService;

    // Owned by the activity rather than by the view: Android rebuilds MainView on every
    // recreation, and a Play offer can start exactly one flow before it goes stale.
    private PlayAppUpdateService? _appUpdateService;
    private ActivityResultLauncher? _updateLauncher;

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        s_current = new WeakReference<MainActivity>(this);

        // Read before Avalonia builds anything: every font size in the product is resolved
        // against this while its view is being constructed (audit 2, B5). Android recreates
        // the activity when the reader changes the device's text size, so this is read
        // again, with the new value, for the build that replaces this one.
        PlatformSourceRegistry.PlatformFontScale = Resources?.Configuration?.FontScale;
        PlatformSourceRegistry.CreateOnDeviceSource = static () => new OnDeviceLogSource();

        // Asked fresh each time rather than cached: `pm grant` and `pm revoke` both take
        // effect against a running process (revoking one killed the app during the live test),
        // and the pre-capture explanation has to describe the state the next capture will
        // actually have (finding F-13).
#if VISUALCAT_READ_LOGS
        PlatformSourceRegistry.HasFullDeviceLogPermission = static () =>
            global::Android.App.Application.Context.CheckSelfPermission(
                global::Android.Manifest.Permission.ReadLogs) == Permission.Granted;
        var packageName = global::Android.App.Application.Context.PackageName;
        PlatformSourceRegistry.FullDeviceLogGrantCommand =
            $"adb shell pm grant {packageName} android.permission.READ_LOGS";
#else
        PlatformSourceRegistry.HasFullDeviceLogPermission = static () => false;
        PlatformSourceRegistry.FullDeviceLogGrantCommand = null;
#endif

        // A Play app cannot request READ_LOGS through Android's runtime-permission API. The
        // guided full-device path therefore leaves the permission model untouched and streams
        // the fixed logcat command through a user-authorised local Wireless debugging session.
        // The callbacks are deliberately installed without constructing the ADB stack: a
        // crypto/provider or binding problem must never make ordinary VisualCat startup fail.
        PlatformSourceRegistry.HasSavedWirelessAdbIdentity = HasSavedWirelessAdbIdentityCurrent;
        PlatformSourceRegistry.PairWirelessAdbAsync = PairWirelessAdbCurrentAsync;
        PlatformSourceRegistry.ConnectSavedWirelessAdbAsync = ConnectSavedWirelessAdbCurrentAsync;
        PlatformSourceRegistry.CreateWirelessAdbSource = CreateWirelessAdbSourceCurrent;
        PlatformSourceRegistry.BeginLiveCaptureBackgroundExecution = BeginLiveCaptureBackgroundExecutionCurrent;
        PlatformSourceRegistry.OpenWirelessDebuggingSettingsAsync = OpenWirelessDebuggingSettingsCurrentAsync;
        global::Android.Util.Log.Info(
            "VisualCat.WirelessAdb",
            "Registered guided Wireless debugging full-device transport. ADB remains disconnected until the user explicitly starts setup.");

        PlatformSourceRegistry.ShareFileAsync = ShareCurrentAsync;
        PlatformSourceRegistry.ConsumeLaunchFilesAsync = ConsumeCurrentLaunchFilesAsync;
        PlatformSourceRegistry.SetGestureExclusions = ApplyGestureExclusions;

        // AndroidX requires the launcher to be registered before the activity is STARTED, so
        // this is OnCreate or nowhere. Registration is cheap and touches no Play code; the
        // client itself is not constructed until something actually asks about an update, in
        // keeping with how the Wireless ADB stack is installed as callbacks above.
        try
        {
            _updateLauncher = PlayAppUpdateService.RegisterLauncher(this, OnUpdateFlowResult);
#if VISUALCAT_FAKE_APP_UPDATE
            // A build deployed with `adb install` is SideLoaded, which is correctly never
            // checked — so the fake manager would prove nothing without this. The other half of
            // the fake loop is the channel, which is not overridden anywhere: pass a real one at
            // build time (-p:Version=2.1.0-beta.1), because a build that lies about its channel
            // is not exercising the rules it is supposed to be demonstrating.
            PlatformSourceRegistry.GetInstallOrigin = static () => AppInstallOrigin.PlayStore;
#else
            PlatformSourceRegistry.GetInstallOrigin = ResolveInstallOriginCurrent;
#endif
            PlatformSourceRegistry.CheckForAppUpdateAsync = CheckForAppUpdateCurrentAsync;
            PlatformSourceRegistry.StartAppUpdateAsync = StartAppUpdateCurrentAsync;
            PlatformSourceRegistry.CompleteAppUpdateAsync = CompleteAppUpdateCurrentAsync;
            PlatformSourceRegistry.OpenAppStoreListingAsync = OpenAppStoreListingCurrentAsync;
        }
        catch (Exception exception)
        {
            // A binding or AndroidX problem here must cost the reader the update check and
            // nothing else. Every hook stays null, so the shared layer behaves as it does on
            // the desktop: it says nothing about updates at all.
            global::Android.Util.Log.Warn(
                "VisualCat.AppUpdate",
                $"In-app updates are unavailable on this device: {exception.GetType().Name}");
        }

        base.OnCreate(savedInstanceState);
        ConfigureEdgeToEdgeWindow();

        // Registered after Avalonia's own, so this one is asked first: AndroidX offers the
        // most recently added enabled callback the press before any earlier one. That order is
        // the whole point — the application's layer stack has to be consulted before the
        // toolkit decides the press was nobody's, whichever mechanism the platform used to
        // deliver it (V2-21).
        OnBackPressedDispatcher.AddCallback(this, new LayerAwareBackCallback(this));
    }

    /// <summary>
    /// Draws the app behind the system bars on every API level, not only the ones that enforce it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Android 15 makes edge-to-edge mandatory for a target of API 35 or later. Below that it
    /// is opt-in, and the opt-in has two halves: the window must stop fitting system windows,
    /// and the platform's own <em>contrast scrim</em> — a translucent band Android paints
    /// behind a transparent bar so icons stay legible — has to be turned off, or the app's
    /// ground never reaches the edge. On a Pixel at API 34 that scrim composited over the
    /// system's wallpaper-derived surface and produced an off-palette brown-purple band at the
    /// top 136 px and bottom 66 px of every screen, moving to the navigation edge in landscape
    /// (V2-22). The content was always safe; the shell simply stopped looking continuous.
    /// </para>
    /// <para>
    /// Avalonia already asks for edge-to-edge through its inset manager and paints the bars
    /// from the workspace palette (<c>MainView.ApplySystemBarSurface</c>), and MainView
    /// distributes the safe-area inset itself. This is the platform half of the same request,
    /// stated where the window is, and it is deliberately unconditional: on API 35 and later
    /// these calls are no-ops against behaviour the platform already enforces, so one code
    /// path serves the whole API 31-36 range rather than two that can drift.
    /// </para>
    /// </remarks>
    private void ConfigureEdgeToEdgeWindow()
    {
        if (Window is not { } window)
        {
            return;
        }

        try
        {
            // Only below API 35, all of it. From 35 the platform enforces edge-to-edge itself
            // and Avalonia's inset manager is already driving it; asking again is not free —
            // calling SetDecorFitsSystemWindows on an API-36 device pushed the notice lane 74 px
            // under the navigation bar, because the second request displaced the inset
            // dispatch the toolkit had installed. Verified on DUT-1 and reverted there.
            if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            {
                AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, false);

                // The two flags that decide whether a bar colour means anything. With
                // TRANSLUCENT_STATUS or TRANSLUCENT_NAVIGATION set, Android paints its own
                // scrim behind the bar and ignores statusBarColor/navigationBarColor
                // entirely — which is what an "off-palette brown-purple band" is (V2-22).
                // Measured on an API-33 emulator: the band was rgb(133,137,142) over an
                // app surface of #F4F7FC, i.e. the platform's ~45 % black scrim, while the
                // colour the app had asked for was #FFFFFF.
                //
                // DRAWS_SYSTEM_BAR_BACKGROUNDS is what makes the window responsible for the
                // bar area instead; clearing the translucent pair is what lets the
                // transparent colours below actually take effect.
                window.ClearFlags(
                    global::Android.Views.WindowManagerFlags.TranslucentStatus |
                    global::Android.Views.WindowManagerFlags.TranslucentNavigation);
                window.AddFlags(global::Android.Views.WindowManagerFlags.DrawsSystemBarBackgrounds);

                window.SetStatusBarColor(global::Android.Graphics.Color.Transparent);
                window.SetNavigationBarColor(global::Android.Graphics.Color.Transparent);
                window.NavigationBarContrastEnforced = false;
                window.StatusBarContrastEnforced = false;
            }
        }
        catch (global::Java.Lang.Throwable exception)
        {
            // A vendor window implementation that refuses one of these must not cost the
            // reader the app. The bars stay as the theme left them, which is the state this
            // improves on rather than depends on.
            global::Android.Util.Log.Warn(
                "VisualCat",
                $"Edge-to-edge window configuration was refused: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// The activity's own back contract: the application's layer stack first, the platform's
    /// default second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stock gesture-navigation Pixel left VisualCat for the launcher while the
    /// <em>More actions</em> sheet and the <em>Appearance</em> card were open, although a
    /// <c>KEYCODE_BACK</c> press closed exactly those layers on the same build (V2-21). The
    /// app owned no back callback at all: it read the toolkit's routed event and depended on
    /// the toolkit's callback being the one the platform consulted. Android 15 turns
    /// predictive back on by default for a target of API 35 or later and stops calling
    /// <c>Activity.onBackPressed</c> entirely, so "which mechanism fired" is not a thing an
    /// application can afford to leave implicit across an API 31-36 support range.
    /// </para>
    /// <para>
    /// The fall-through is the AndroidX idiom rather than a <c>Finish</c>: disable this
    /// callback, hand the press back to the dispatcher, and re-enable. Backgrounding the task
    /// is the platform's decision and stays the platform's decision.
    /// </para>
    /// </remarks>
    private sealed class LayerAwareBackCallback(MainActivity owner)
        : AndroidX.Activity.OnBackPressedCallback(enabled: true)
    {
        public override void HandleOnBackPressed()
        {
            if (PlatformSourceRegistry.TryNavigateBack?.Invoke() == true)
            {
                return;
            }

            Enabled = false;
            try
            {
                owner.OnBackPressedDispatcher.OnBackPressed();
            }
            finally
            {
                Enabled = true;
            }
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Restated, because Avalonia configures the window when its view attaches — after
        // OnCreate — and a translucent-bar flag added there would otherwise stand. Both calls
        // are idempotent and cost a few flag writes per resume.
        ConfigureEdgeToEdgeWindow();
        EdgeGestureGuard.Republish();
        PlatformSourceRegistry.PublishAppResumed();

        // Play's own requirement, and it is the activity's rather than the view's: a flexible
        // download may have finished while the app was backgrounded, and an immediate flow
        // interrupted by Back has to be resumed. Only a service that already exists is asked —
        // an app that has never checked has nothing in flight to rejoin.
        _appUpdateService?.OnActivityResumed();
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CaptureForegroundService.NotificationPermissionRequestCode &&
            grantResults.Any(static result => result == Permission.Granted))
        {
            CaptureForegroundService.RepublishNotificationAfterPermissionGrant(this);
        }
    }

    /// <summary>
    /// A display configuration this activity handles itself has changed.
    /// </summary>
    /// <remarks>
    /// The one the app has to act on is the text scale: every font size in the product is
    /// resolved against it while the view that uses it is being constructed, so a new value
    /// reaches the screen when the views are rebuilt and not before. The rest — density,
    /// locale, layout direction — are declared so that they, too, stop killing a running
    /// capture; nothing in the product reads them (the display culture is fixed, see
    /// <c>DisplayCulture</c>), so nothing has to answer them.
    ///
    /// The scale is republished before the notification, because the handler on the other end
    /// reads it rather than being passed it.
    /// </remarks>
    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        PlatformSourceRegistry.PlatformFontScale = newConfig?.FontScale ?? Resources?.Configuration?.FontScale;
        PlatformSourceRegistry.PublishDisplayConfigurationChanged();
    }

    /// <summary>
    /// The screen turned off, or the user left the app. A live capture keeps running —
    /// Android's user-visible foreground service owns that background allowance — but
    /// nothing on screen needs redrawing until <see cref="OnResume"/>.
    /// </summary>
    protected override void OnPause()
    {
        PlatformSourceRegistry.PublishAppPaused();
        base.OnPause();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        _ = PublishIncomingAsync(intent);
    }

    protected override void OnDestroy()
    {
        if (s_current?.TryGetTarget(out var current) == true && ReferenceEquals(current, this))
        {
            s_current = null;
            PlatformSourceRegistry.ShareFileAsync = null;
            PlatformSourceRegistry.CreateOnDeviceSource = null;
            PlatformSourceRegistry.HasFullDeviceLogPermission = null;
            PlatformSourceRegistry.FullDeviceLogGrantCommand = null;
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = null;
            PlatformSourceRegistry.PairWirelessAdbAsync = null;
            PlatformSourceRegistry.ConnectSavedWirelessAdbAsync = null;
            PlatformSourceRegistry.CreateWirelessAdbSource = null;
            PlatformSourceRegistry.BeginLiveCaptureBackgroundExecution = null;
            PlatformSourceRegistry.OpenWirelessDebuggingSettingsAsync = null;
            PlatformSourceRegistry.ConsumeLaunchFilesAsync = null;
            PlatformSourceRegistry.SetGestureExclusions = null;
            PlatformSourceRegistry.GetInstallOrigin = null;
            PlatformSourceRegistry.CheckForAppUpdateAsync = null;
            PlatformSourceRegistry.StartAppUpdateAsync = null;
            PlatformSourceRegistry.CompleteAppUpdateAsync = null;
            PlatformSourceRegistry.OpenAppStoreListingAsync = null;
            _wirelessAdbService?.Dispose();
            _wirelessAdbService = null;
        }

        // Outside the "am I still the current activity?" guard, unlike everything above it.
        // Those are static registry slots, which only the newest activity may clear; this is
        // this instance's own object. Android recreates the activity for a text-size change or
        // a low-memory kill, and a recreated one is destroyed while s_current already names its
        // replacement — so inside the guard the old service was never disposed, its Play install
        // listener stayed registered, and it went on holding the dead activity for the life of
        // the process. Disposal is idempotent and safe on an instance that never built one.
        _appUpdateService?.Dispose();
        _appUpdateService = null;
        _updateLauncher = null;

        base.OnDestroy();
    }

    /// <summary>
    /// The Play client, created on first use so a binding problem cannot break plain startup.
    /// </summary>
    private PlayAppUpdateService? GetAppUpdateService()
    {
        if (_appUpdateService is not null)
        {
            return _appUpdateService;
        }

        if (_updateLauncher is not { } launcher)
        {
            return null;
        }

        _appUpdateService = new PlayAppUpdateService(this, launcher);
        return _appUpdateService;
    }

    private static AppInstallOrigin ResolveInstallOriginCurrent() =>
        PlayAppUpdateService.ResolveInstallOrigin(global::Android.App.Application.Context);

    private static Task<AppUpdateStatus> CheckForAppUpdateCurrentAsync(CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true ||
            activity?.GetAppUpdateService() is not { } service)
        {
            return Task.FromResult(new AppUpdateStatus(AppUpdateState.Unknown));
        }

        return service.CheckAsync(cancellationToken);
    }

    private static Task<bool> StartAppUpdateCurrentAsync(AppUpdateFlow flow, CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true ||
            activity?._appUpdateService is not { } service)
        {
            // No client means nothing was ever offered. False asks the caller to check again,
            // which is the correct next move rather than an error.
            return Task.FromResult(false);
        }

        return service.StartAsync(flow, cancellationToken);
    }

    private static Task CompleteAppUpdateCurrentAsync(CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true ||
            activity?._appUpdateService is not { } service)
        {
            return Task.CompletedTask;
        }

        return service.CompleteAsync(cancellationToken);
    }

    private static async Task OpenAppStoreListingCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException("The Play Store cannot be opened because VisualCat is no longer active.");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        activity.RunOnUiThread(() =>
        {
            try
            {
                PlayAppUpdateService.OpenStoreListing(activity);
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Warn(
                    "VisualCat.AppUpdate",
                    $"Could not open the Play listing: {exception.GetType().Name}");
                completion.TrySetException(new InvalidOperationException(
                    "Google Play could not be opened on this device.", exception));
            }
        });
        await completion.Task.ConfigureAwait(false);
    }

    private void OnUpdateFlowResult(int resultCode) => _appUpdateService?.OnFlowResult(resultCode);

    private static bool HasSavedWirelessAdbIdentityCurrent()
    {
        try
        {
            return WirelessAdbService.HasSavedIdentity(global::Android.App.Application.Context);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                "VisualCat.WirelessAdb",
                $"Could not inspect saved Wireless ADB pairing state: {exception.GetType().FullName}: {exception.Message}\n{exception}");
            return false;
        }
    }

    private static Task<WirelessAdbConnectionResult> PairWirelessAdbCurrentAsync(
        WirelessAdbPairingRequest request,
        CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            global::Android.Util.Log.Warn(
                "VisualCat.WirelessAdb",
                "Wireless ADB pairing was requested while the Android activity was unavailable.");
            return Task.FromResult(new WirelessAdbConnectionResult(
                Connected: false,
                PairingSucceeded: false,
                "VisualCat is not in the foreground. Return to the app and try setup again."));
        }

        try
        {
            return activity.GetWirelessAdbService().PairAndConnectAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                "VisualCat.WirelessAdb",
                $"Could not initialise Wireless ADB pairing: {exception.GetType().FullName}: {exception.Message}\n{exception}");
            return Task.FromResult(new WirelessAdbConnectionResult(
                Connected: false,
                PairingSucceeded: false,
                "Wireless debugging setup could not be initialised on this device. You can still capture VisualCat-only logs. Developer builds that explicitly declare READ_LOGS can also use the separate host-ADB workflow documented in Support."));
        }
    }

    private static Task<WirelessAdbConnectionResult> ConnectSavedWirelessAdbCurrentAsync(
        CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            global::Android.Util.Log.Warn(
                "VisualCat.WirelessAdb",
                "Saved-pairing reconnect was requested while the Android activity was unavailable.");
            return Task.FromResult(new WirelessAdbConnectionResult(
                Connected: false,
                PairingSucceeded: false,
                "VisualCat is not in the foreground. Return to the app and try setup again."));
        }

        try
        {
            return activity.GetWirelessAdbService().ConnectSavedAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                "VisualCat.WirelessAdb",
                $"Could not initialise saved Wireless ADB connection: {exception.GetType().FullName}: {exception.Message}\n{exception}");
            return Task.FromResult(new WirelessAdbConnectionResult(
                Connected: false,
                PairingSucceeded: false,
                "The saved Wireless debugging pairing could not be opened. Turn Wireless debugging on, then pair VisualCat again if reconnect still fails."));
        }
    }

    private static ILogSource? CreateWirelessAdbSourceCurrent()
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            global::Android.Util.Log.Warn(
                "VisualCat.WirelessAdb",
                "Wireless ADB source creation was requested while the Android activity was unavailable.");
            return null;
        }

        try
        {
            return activity._wirelessAdbService?.CreateLogSource();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                "VisualCat.WirelessAdb",
                $"Could not create Wireless ADB log source: {exception.GetType().FullName}: {exception.Message}\n{exception}");
            return null;
        }
    }

    private WirelessAdbService GetWirelessAdbService()
    {
        if (_wirelessAdbService is not null)
        {
            return _wirelessAdbService;
        }

        global::Android.Util.Log.Info(
            "VisualCat.WirelessAdb",
            "Creating the Wireless ADB full-device capture service after explicit user action.");
        _wirelessAdbService = new WirelessAdbService(
            global::Android.App.Application.Context);
        return _wirelessAdbService;
    }

    private static IDisposable BeginLiveCaptureBackgroundExecutionCurrent(
        string scope,
        Action<PlatformLiveCaptureStopReason> requestStop)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException(
                "VisualCat cannot start reliable background capture while its Android activity is unavailable. Return to the app and start Live again.");
        }

        activity.RequestCaptureNotificationPermissionOnce();
        return CaptureForegroundService.Begin(
            global::Android.App.Application.Context,
            scope,
            requestStop);
    }

    /// <summary>
    /// Requests notification visibility once, at the moment the reader starts Live.
    /// </summary>
    /// <remarks>
    /// Android does not require this grant to run a foreground service: if it is denied, the
    /// service remains visible in Android's Active apps UI. Asking here still matters because
    /// the ordinary notification provides the useful Stop and save action. The one-shot flag
    /// prevents every later capture from nagging someone who declined it.
    /// </remarks>
    private void RequestCaptureNotificationPermissionOnce()
    {
        const string notificationPermission = "android.permission.POST_NOTIFICATIONS";
        if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.Tiramisu ||
            CheckSelfPermission(notificationPermission) == Permission.Granted)
        {
            return;
        }

        const string preferencesName = "visualcat-platform";
        const string requestedKey = "capture-notification-requested";
        var preferences = GetSharedPreferences(preferencesName, FileCreationMode.Private);
        if (preferences?.GetBoolean(requestedKey, false) == true)
        {
            return;
        }

        preferences?.Edit()?.PutBoolean(requestedKey, true)?.Apply();
        RunOnUiThread(() => ActivityCompat.RequestPermissions(
            this,
            [notificationPermission],
            CaptureForegroundService.NotificationPermissionRequestCode));
    }

    private static Task<bool> OpenWirelessDebuggingSettingsCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException("The Android activity is not available to open Wireless debugging settings.");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                global::Android.Util.Log.Info(
                    "VisualCat.WirelessAdb",
                    "Opening Android Developer options focused on Wireless debugging for explicit setup.");
                try
                {
                    var developerSettings = new Intent(
                        global::Android.Provider.Settings.ActionApplicationDevelopmentSettings);
                    // AOSP Settings has honored this preference-highlight hint since Wireless
                    // debugging was introduced. OEMs may ignore it, which is harmless: the
                    // public Developer-options activity still opens and the setup copy explains
                    // where the preference lives. Samsung Android 16 uses the hint to scroll the
                    // long page directly to the Wireless debugging row.
                    developerSettings.PutExtra(":settings:fragment_args_key", "toggle_adb_wireless");
                    activity.StartActivity(developerSettings);
                }
                catch (Exception exception) when (exception is ActivityNotFoundException or Java.Lang.SecurityException)
                {
                    global::Android.Util.Log.Warn(
                        "VisualCat.WirelessAdb",
                        "This Android build has no accessible Developer options activity; opening general Settings instead.");
                    activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionSettings));
                }

                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error(
                    "VisualCat.WirelessAdb",
                    $"Could not open Android settings: {exception.GetType().FullName}: {exception.Message}\n{exception}");
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static Task ShareCurrentAsync(string path, CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException("The Android activity is not available for sharing.");
        }

        return activity.ShareAsync(path, cancellationToken);
    }

    private static Task<IReadOnlyList<IncomingFile>> ConsumeCurrentLaunchFilesAsync(CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            return Task.FromResult<IReadOnlyList<IncomingFile>>([]);
        }

        return activity.MaterializeIncomingAsync(activity.Intent, cancellationToken);
    }

    private async Task PublishIncomingAsync(Intent? intent)
    {
        try
        {
            var paths = await MaterializeIncomingAsync(intent, CancellationToken.None);
            PlatformSourceRegistry.PublishLaunchFiles(paths);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("VisualCat", $"Incoming file could not be opened: {exception.Message}");
        }
    }

    private async Task<IReadOnlyList<IncomingFile>> MaterializeIncomingAsync(
        Intent? intent,
        CancellationToken cancellationToken)
    {
        var uri = intent?.Data;
        if (intent?.Action != global::Android.Content.Intent.ActionView || uri is null)
        {
            return [];
        }

        var identity = uri.ToString() ?? string.Empty;
        lock (_consumedUris)
        {
            if (!_consumedUris.Add(identity))
            {
                return [];
            }
        }

        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase) &&
            uri.Path is { } localPath &&
            File.Exists(localPath))
        {
            return [new IncomingFile(localPath, Path.GetFileName(localPath))];
        }

        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var cache = CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android cache storage is unavailable.");
        var incomingDirectory = Path.Combine(cache, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        // The provider's own name for the document, not the URI's last segment. The last
        // segment is a document id — "raw:_storage_emulated_0_Download_tiny.txt" for one
        // Downloads form of the same file, "msf:1000000323" for another — and it was becoming
        // both the cache filename and the tab title (finding F-27).
        var displayName = QueryDisplayName(uri) ?? "shared-log.txt";
        var safeName = string.Concat(
            displayName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(Path.GetExtension(safeName)))
        {
            safeName += ".txt";
        }

        var destination = Path.Combine(
            incomingDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{safeName}");
        await using var input = ContentResolver?.OpenInputStream(uri)
            ?? throw new IOException("Android did not provide a readable stream for the shared log.");
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return [new IncomingFile(destination, safeName)];
    }

    /// <summary>
    /// What the content provider calls this document, length-capped and never a path.
    /// </summary>
    /// <remarks>
    /// <c>OpenableColumns.DisplayName</c> is the documented way to ask, and a provider may
    /// decline to answer or answer with something hostile — a traversal fragment, a name
    /// thousands of characters long, an empty string. Anything that is not a plain file name
    /// is refused here and the caller falls back to a neutral one; the safe characters are
    /// filtered again by the caller, because this is untrusted text from another app.
    /// </remarks>
    private string? QueryDisplayName(global::Android.Net.Uri uri)
    {
        try
        {
            using var cursor = ContentResolver?.Query(
                uri,
                [global::Android.Provider.IOpenableColumns.DisplayName],
                null,
                null,
                null);
            if (cursor is null || !cursor.MoveToFirst())
            {
                return null;
            }

            var column = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
            var value = column >= 0 ? cursor.GetString(column) : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();
            if (value.Length > 96)
            {
                value = value[..96];
            }

            // A name, not a location: anything carrying a separator or a relative segment is
            // another app's idea of a path and is not shown or written.
            return value.Contains('/', StringComparison.Ordinal) ||
                   value.Contains('\\', StringComparison.Ordinal) ||
                   value is "." or ".."
                ? null
                : value;
        }
        catch (Exception exception) when (
            exception is global::Android.Database.SQLException or
                global::Java.Lang.SecurityException or
                global::Java.Lang.IllegalArgumentException)
        {
            global::Android.Util.Log.Warn("VisualCat", $"Provider did not answer for a display name: {exception.Message}");
            return null;
        }
    }

    private async Task ShareAsync(string path, CancellationToken cancellationToken)
    {
        var cache = CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android cache storage is unavailable.");
        var shareDirectory = Path.Combine(cache, "share");
        Directory.CreateDirectory(shareDirectory);
        foreach (var stale in Directory.EnumerateFiles(shareDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
            {
                File.Delete(stale);
            }
        }

        var sharedPath = Path.Combine(shareDirectory, Path.GetFileName(path));
        await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var target = new FileStream(sharedPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        var authority = $"{PackageName}.files";
        var uri = FileProvider.GetUriForFile(this, authority, new Java.IO.File(sharedPath));
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("application/zip");
        intent.PutExtra(Intent.ExtraStream, uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        StartActivity(Intent.CreateChooser(intent, "Share VisualCat session"));
    }

    /// <summary>
    /// Claims the touches inside these rectangles for the app, where the system's back
    /// gesture would otherwise take them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The back gesture owns a strip about 30 dp wide down both edges of a gesture-navigation
    /// screen. The heat map runs to 12 dp of both, and a drag-to-pan started in the overlap
    /// was delivered to the system instead: it went Back, and with no overlay open that means
    /// leaving the workspace for the home screen (finding F-28, third device pass). The
    /// rectangles arrive in window pixels, which is the coordinate space of the decor view, so
    /// they are set there rather than on a child whose own offset would have to be undone.
    /// </para>
    /// <para>
    /// Android honours at most 200 dp of exclusion height per edge and silently keeps the
    /// lowest rectangles past that, so the shared side is deliberate about what it sends: the
    /// plot and the minimap, and nothing else on the screen.
    /// </para>
    /// </remarks>
    private void ApplyGestureExclusions(IReadOnlyList<Avalonia.PixelRect> rectangles)
    {
        ArgumentNullException.ThrowIfNull(rectangles);
        var view = Window?.DecorView;
        if (view is null)
        {
            return;
        }

        var rects = new List<global::Android.Graphics.Rect>(rectangles.Count);
        foreach (var rectangle in rectangles)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                continue;
            }

            rects.Add(new global::Android.Graphics.Rect(
                rectangle.X,
                rectangle.Y,
                rectangle.X + rectangle.Width,
                rectangle.Y + rectangle.Height));
        }

        RunOnUiThread(() =>
        {
            try
            {
                view.SystemGestureExclusionRects = rects;
            }
            catch (global::Java.Lang.Throwable)
            {
                // A view that is being torn down refuses the call. Losing an exclusion for a
                // window that is going away costs the reader nothing.
            }
        });
    }
}

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<VisualCat.App.App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
