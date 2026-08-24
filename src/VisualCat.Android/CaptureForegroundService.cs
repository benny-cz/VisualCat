using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using VisualCat.App.Platform;

namespace VisualCat.Android;

/// <summary>
/// Keeps an explicitly started Live capture honest and runnable after the activity is hidden
/// or the screen locks.
/// </summary>
/// <remarks>
/// The service owns no log bytes, ADB keys or session writer. Those remain in the ordinary
/// capture pipeline; this is only Android's user-visible background-execution lease. Its Stop
/// action and API-35+ data-sync timeout both request the pipeline's graceful stop, so already
/// received data is drained and sealed rather than abandoning a temporary session.
/// </remarks>
[Service(
    Name = "com.barebit.visualcat.CaptureForegroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class CaptureForegroundService : Service
{
    internal const int NotificationPermissionRequestCode = 4107;

    private const string LogTag = "VisualCat.CaptureService";
    private const string ChannelId = "visualcat-live-capture";
    private const int NotificationId = 4108;
    private const string ActionStart = "com.barebit.visualcat.action.START_LIVE_CAPTURE";
    private const string ActionRefreshNotification = "com.barebit.visualcat.action.REFRESH_LIVE_CAPTURE_NOTIFICATION";
    private const string ActionStopCapture = "com.barebit.visualcat.action.STOP_AND_SAVE_LIVE_CAPTURE";
    private const string ExtraSummary = "capture-summary";

    private static readonly object Gate = new();
    private static Action<PlatformLiveCaptureStopReason>? s_requestStop;
    private static int s_generation;

    private string _summary = "Logs are being saved locally.";

    /// <summary>Starts the foreground state and returns its exactly-matching release lease.</summary>
    internal static IDisposable Begin(
        Context context,
        string scope,
        Action<PlatformLiveCaptureStopReason> requestStop)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestStop);

        var applicationContext = context.ApplicationContext
            ?? throw new ArgumentException("An Android application context is required.", nameof(context));
        int generation;
        lock (Gate)
        {
            if (s_requestStop is not null)
            {
                throw new InvalidOperationException(
                    "Android already has a VisualCat Live capture background lease.");
            }

            generation = unchecked(++s_generation);
            s_requestStop = requestStop;
        }

        var summary = scope.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                      scope.Contains("full-device", StringComparison.OrdinalIgnoreCase)
            ? "Full-device logs are being saved locally."
            : "VisualCat logs are being saved locally.";
        var intent = new Intent(applicationContext, typeof(CaptureForegroundService));
        intent.SetAction(ActionStart);
        intent.PutExtra(ExtraSummary, summary);

        try
        {
            ContextCompat.StartForegroundService(applicationContext, intent);
            global::Android.Util.Log.Info(
                LogTag,
                "Started the user-visible data-sync foreground service for Live capture.");
            return new Lease(applicationContext, generation);
        }
        catch
        {
            ClearLease(generation);
            throw;
        }
    }

    /// <summary>
    /// Reposts the already-running foreground notification after Android grants notification
    /// visibility. A notification posted while the runtime prompt is undecided is not
    /// necessarily added to an OEM notification shade retroactively.
    /// </summary>
    internal static void RepublishNotificationAfterPermissionGrant(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!HasActiveLease())
        {
            return;
        }

        var applicationContext = context.ApplicationContext ?? context;
        var intent = new Intent(applicationContext, typeof(CaptureForegroundService));
        intent.SetAction(ActionRefreshNotification);
        try
        {
            ContextCompat.StartForegroundService(applicationContext, intent);
            global::Android.Util.Log.Info(
                LogTag,
                "Requested a foreground-notification repost after notification permission was granted.");
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                LogTag,
                $"Could not repost the foreground notification after permission grant: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = GetSystemService(NotificationService) as NotificationManager;
        if (manager is null)
        {
            throw new InvalidOperationException("Android's notification service is unavailable.");
        }

        var channel = new NotificationChannel(
            ChannelId,
            "Live capture",
            NotificationImportance.Low)
        {
            Description = "Shows when VisualCat is saving a Live log capture in the background.",
            LockscreenVisibility = NotificationVisibility.Private,
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _ = flags;
        if (!HasActiveLease())
        {
            global::Android.Util.Log.Warn(
                LogTag,
                "Android delivered a capture-service command without a live in-process capture; stopping it instead of displaying a stale notification.");
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        if (string.Equals(intent?.Action, ActionStopCapture, StringComparison.Ordinal))
        {
            global::Android.Util.Log.Info(
                LogTag,
                "The reader requested Stop and save from the ongoing capture notification.");
            PublishForegroundNotification(stopping: true);
            RequestGracefulStop(PlatformLiveCaptureStopReason.NotificationAction);
            return StartCommandResult.NotSticky;
        }

        if (string.Equals(intent?.Action, ActionRefreshNotification, StringComparison.Ordinal))
        {
            global::Android.Util.Log.Info(
                LogTag,
                "Reposting the active capture notification after notification permission was granted.");
            PublishForegroundNotification(stopping: false);
            return StartCommandResult.NotSticky;
        }

        _summary = intent?.GetStringExtra(ExtraSummary) ?? _summary;
        PublishForegroundNotification(stopping: false);
        return StartCommandResult.NotSticky;
    }

    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        global::Android.Util.Log.Warn(
            LogTag,
            $"Android's foreground-service time limit ended Live capture; startId={startId}, type={fgsType}.");
        RequestGracefulStop(PlatformLiveCaptureStopReason.SystemTimeLimit);

        // API 35 gives the app only a few seconds after this callback. Session draining may
        // legitimately take longer, so comply with Android immediately; the already-running
        // graceful-stop task continues without pretending foreground status still exists.
        StopForeground(StopForegroundFlags.Remove);
        StopSelf(startId);
    }

    public override IBinder? OnBind(Intent? intent)
    {
        _ = intent;
        return null;
    }

    private void PublishForegroundNotification(bool stopping)
    {
        var openIntent = new Intent(this, typeof(MainActivity));
        openIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var openPendingIntent = PendingIntent.GetActivity(
            this,
            0,
            openIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            ?? throw new InvalidOperationException("Android could not create the capture notification's open action.");

        var stopIntent = new Intent(this, typeof(CaptureForegroundService));
        stopIntent.SetAction(ActionStopCapture);
        var stopPendingIntent = PendingIntent.GetService(
            this,
            1,
            stopIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)
            ?? throw new InvalidOperationException("Android could not create the capture notification's Stop and save action.");

        var builder = new NotificationCompat.Builder(this, ChannelId);
        _ = builder.SetSmallIcon(Resource.Drawable.ic_launcher_foreground);
        _ = builder.SetContentTitle(stopping ? "Stopping VisualCat capture" : "VisualCat live capture");
        _ = builder.SetContentText(stopping ? "Saving received logs and finalizing the session…" : _summary);
        _ = builder.SetContentIntent(openPendingIntent);
        _ = builder.SetCategory(NotificationCompat.CategoryService);
        _ = builder.SetPriority(NotificationCompat.PriorityLow);
        _ = builder.SetVisibility(NotificationCompat.VisibilityPrivate);
        _ = builder.SetOnlyAlertOnce(true);
        _ = builder.SetOngoing(true);
        _ = builder.SetSilent(true);
        _ = builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);

        if (!stopping)
        {
            var action = new NotificationCompat.Action.Builder(
                Resource.Drawable.ic_launcher_foreground,
                "Stop and save",
                stopPendingIntent).Build()
                ?? throw new InvalidOperationException("Android could not build the capture notification's Stop and save action.");
            _ = builder.AddAction(action);
        }

        var notification = builder.Build()
            ?? throw new InvalidOperationException("Android could not build the Live capture notification.");

        ServiceCompat.StartForeground(
            this,
            NotificationId,
            notification,
            (int)ForegroundService.TypeDataSync);
    }

    private static bool HasActiveLease()
    {
        lock (Gate)
        {
            return s_requestStop is not null;
        }
    }

    private static void RequestGracefulStop(PlatformLiveCaptureStopReason reason)
    {
        Action<PlatformLiveCaptureStopReason>? callback;
        lock (Gate)
        {
            callback = s_requestStop;
        }

        if (callback is null)
        {
            return;
        }

        // Cancellation callbacks can include persistence and UI dispatch. Never run them on
        // Android's service main thread, especially inside the short OnTimeout grace period.
        _ = Task.Run(() =>
        {
            try
            {
                callback(reason);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error(
                    LogTag,
                    $"The foreground-service stop callback failed: {exception.GetType().Name}: {exception.Message}");
            }
        });
    }

    private static void ClearLease(int generation)
    {
        lock (Gate)
        {
            if (generation != s_generation)
            {
                return;
            }

            s_requestStop = null;
        }
    }

    private sealed class Lease(Context context, int generation) : IDisposable
    {
        private Context? _context = context;

        public void Dispose()
        {
            var contextToStop = Interlocked.Exchange(ref _context, null);
            if (contextToStop is null)
            {
                return;
            }

            ClearLease(generation);
            contextToStop.StopService(new Intent(contextToStop, typeof(CaptureForegroundService)));
            global::Android.Util.Log.Info(
                LogTag,
                "Stopped the Live capture foreground service and removed its ongoing notification.");
        }
    }
}
