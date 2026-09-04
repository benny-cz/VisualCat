namespace VisualCat.App.Platform;

/// <summary>
/// Why Android refused to put a service into the user-visible foreground state.
/// </summary>
/// <remarks>
/// The categories are kept apart because they need different answers from a reader: a
/// background restriction is a state the device is in, a missing or wrong service type is a
/// packaging defect, and a denied permission is a grant the app does not hold. Collapsing
/// them into "could not start" would make the diagnostic bundle useless for telling them
/// apart after the fact.
/// </remarks>
public enum ForegroundPromotionFailure
{
    /// <summary>Android would not allow a foreground start from the app's current state.</summary>
    BackgroundRestricted,

    /// <summary>The declared or requested foreground-service type is missing or invalid.</summary>
    ServiceTypeConfiguration,

    /// <summary>A permission the promotion requires is not held.</summary>
    PermissionDenied,

    /// <summary>A platform object the notification needs could not be produced.</summary>
    PlatformUnavailable,
}

/// <summary>
/// Android refused the user-visible background-execution lease a capture needs.
/// </summary>
/// <remarks>
/// Carries a product sentence because the platform's own is an intent dump: on a Samsung
/// SM-G990B with the foreground-start app op denied, the reader was shown
/// <c>Unable to start service Intent { act=com.barebit.visualcat.action.START_LIVE_CAPTURE
/// xflg=0x4 cmp=com.barebit.visualcat/.CaptureForegroundService (has extras) }: foreground
/// not allowed as per app op</c>. The platform text is kept as the inner exception, which is
/// where the diagnostic bundle reads it from.
/// </remarks>
public sealed class BackgroundExecutionUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// The one place a foreground promotion's failure is decided and answered.
/// </summary>
/// <remarks>
/// <para>
/// <c>Service.OnStartCommand</c> is not a place an exception may escape: an escaping one is a
/// process crash rather than a capture that declines cleanly, and Android documents several
/// ways a promotion can be refused — a restricted background start, a missing or invalid
/// foreground-service type, a denied permission (IA-12b). Every branch of the Android service
/// promotes through here so all of them recover the same way and none of them can forget a
/// step.
/// </para>
/// <para>
/// It lives in the cross-platform layer on purpose. The Android type it serves cannot be
/// constructed off a device, so the policy — which failures are handled, and exactly what
/// running once each means — is written where a unit test can inject every documented
/// category and observe the whole recovery.
/// </para>
/// </remarks>
public static class ForegroundPromotion
{
    /// <summary>
    /// Runs <paramref name="publish"/>, and on a documented Android refusal performs the
    /// complete recovery exactly once: request the ordinary drain-and-seal stop, release the
    /// foreground state, and release the service.
    /// </summary>
    /// <returns><see langword="true"/> when the service is in the foreground state.</returns>
    /// <remarks>
    /// Undocumented failures are not caught. A blanket catch here would turn an ordinary
    /// service bug into a capture that quietly stops, which is the harder defect to find.
    /// </remarks>
    public static bool TryPublish(
        Action publish,
        PlatformLiveCaptureStopReason stopReason,
        Action<PlatformLiveCaptureStopReason> requestStop,
        Action releaseForeground,
        Action releaseService,
        Action<ForegroundPromotionFailure, Exception> report)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(requestStop);
        ArgumentNullException.ThrowIfNull(releaseForeground);
        ArgumentNullException.ThrowIfNull(releaseService);
        ArgumentNullException.ThrowIfNull(report);

        ForegroundPromotionFailure failure;
        Exception cause;
        try
        {
            publish();
            return true;
        }
        catch (Exception exception) when (Classify(exception) is { } classified)
        {
            failure = classified;
            cause = exception;
        }

        report(failure, cause);
        requestStop(stopReason);

        // Best effort, and deliberately after the stop request. The capture's own shutdown is
        // the part a reader loses if it does not happen; tidying a foreground state Android
        // has already refused is not, and its failure must not replace the original cause.
        try
        {
            releaseForeground();
        }
        catch (Exception exception) when (Classify(exception) is not null)
        {
            report(failure, exception);
        }

        releaseService();
        return false;
    }

    /// <summary>
    /// The sentence a reader gets when the lease is refused, for each documented category.
    /// </summary>
    /// <remarks>
    /// Each one names what Android would not do, and the one thing the reader can change.
    /// A configuration failure is the app's own defect and says so rather than sending
    /// somebody into their settings after a fix that is not theirs to make.
    /// </remarks>
    public static string Explain(ForegroundPromotionFailure failure) => failure switch
    {
        ForegroundPromotionFailure.BackgroundRestricted =>
            "Android would not let VisualCat keep a capture running in the background. " +
            "Allow background activity for VisualCat in Android's app settings, then start Live again.",
        ForegroundPromotionFailure.PermissionDenied =>
            "Android refused VisualCat the permission a background capture needs. " +
            "Check VisualCat's permissions in Android's app settings, then start Live again.",
        ForegroundPromotionFailure.ServiceTypeConfiguration =>
            "This VisualCat build cannot register the background capture service with this " +
            "version of Android. Live capture is unavailable until the app is updated.",
        _ =>
            "Android could not start the background capture service. Restart VisualCat and try Live again.",
    };

    /// <summary>
    /// Names the documented refusal an exception represents, or <see langword="null"/> when it
    /// is not one and must not be handled here.
    /// </summary>
    /// <remarks>
    /// Matched by type name because the Android exception types exist only in the Android
    /// framework assembly, which the cross-platform layer does not reference — and because a
    /// device may raise either the managed binding's type or the underlying Java one. A
    /// Samsung SM-G990B on Android 16 raised a plain <c>Java.Lang.SecurityException</c> for a
    /// denied foreground-start app op, not the documented
    /// <c>ForegroundServiceStartNotAllowedException</c>, which is why the list is by name and
    /// wider than the documentation alone would make it.
    /// </remarks>
    public static ForegroundPromotionFailure? Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetType().FullName switch
        {
            "Android.App.ForegroundServiceStartNotAllowedException" or
            "Android.App.BackgroundServiceStartNotAllowedException" or
            "Java.Lang.IllegalStateException" => ForegroundPromotionFailure.BackgroundRestricted,
            "Android.App.MissingForegroundServiceTypeException" or
            "Android.App.InvalidForegroundServiceTypeException" or
            "Java.Lang.IllegalArgumentException" => ForegroundPromotionFailure.ServiceTypeConfiguration,
            "Java.Lang.SecurityException" => ForegroundPromotionFailure.PermissionDenied,
            _ => exception switch
            {
                System.Security.SecurityException => ForegroundPromotionFailure.PermissionDenied,
                ArgumentException => ForegroundPromotionFailure.ServiceTypeConfiguration,
                InvalidOperationException => ForegroundPromotionFailure.PlatformUnavailable,
                _ => null,
            },
        };
    }
}
