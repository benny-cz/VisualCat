using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
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
        PlatformSourceRegistry.OpenDeveloperOptionsAsync = OpenDeveloperOptionsCurrentAsync;
        global::Android.Util.Log.Info(
            "VisualCat.WirelessAdb",
            "Registered guided Wireless debugging full-device transport. ADB remains disconnected until the user explicitly starts setup.");

        PlatformSourceRegistry.ShareFileAsync = ShareCurrentAsync;
        PlatformSourceRegistry.ConsumeLaunchFilesAsync = ConsumeCurrentLaunchFilesAsync;
        PlatformSourceRegistry.SetGestureExclusions = ApplyGestureExclusions;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();
        PlatformSourceRegistry.PublishAppResumed();
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
    /// that is the point of leaving one going overnight — but nothing on screen needs
    /// redrawing until <see cref="OnResume"/>.
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
            PlatformSourceRegistry.OpenDeveloperOptionsAsync = null;
            PlatformSourceRegistry.ConsumeLaunchFilesAsync = null;
            PlatformSourceRegistry.SetGestureExclusions = null;
            _wirelessAdbService?.Dispose();
            _wirelessAdbService = null;
        }

        base.OnDestroy();
    }

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

    private static Task<bool> OpenDeveloperOptionsCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException("The Android activity is not available to open Developer options.");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                global::Android.Util.Log.Info(
                    "VisualCat.WirelessAdb",
                    "Opening Android Developer options for explicit Wireless debugging setup.");
                try
                {
                    activity.StartActivity(new Intent(global::Android.Provider.Settings.ActionApplicationDevelopmentSettings));
                }
                catch (ActivityNotFoundException)
                {
                    global::Android.Util.Log.Warn(
                        "VisualCat.WirelessAdb",
                        "This Android build has no direct Developer options activity; opening general Settings instead.");
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
