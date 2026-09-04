using VisualCat.App.Platform;

namespace VisualCat.App.Tests;

/// <summary>
/// IA-12b — a foreground promotion Android refuses must stop the capture, not the process.
/// </summary>
/// <remarks>
/// <para>
/// <c>Service.OnStartCommand</c> ran three unguarded promotion paths, and Android documents
/// several ways to refuse one: a restricted background start, a missing or invalid
/// foreground-service type, a denied permission. An exception escaping that callback is a
/// process crash, which takes the capture with it and explains nothing.
/// </para>
/// <para>
/// The refused states cannot be constructed off a device, and the Android exception types do
/// not exist in a desktop test assembly — so the shims below carry their exact type names,
/// which is what <see cref="ForegroundPromotion.Classify"/> matches on. The unit proves the
/// cleanup; the device proves the platform can reach the state.
/// </para>
/// </remarks>
public sealed class ForegroundPromotionTests
{
    public static TheoryData<Exception, ForegroundPromotionFailure> DocumentedFailures() => new()
    {
        { new global::Android.App.ForegroundServiceStartNotAllowedException(), ForegroundPromotionFailure.BackgroundRestricted },
        { new global::Android.App.MissingForegroundServiceTypeException(), ForegroundPromotionFailure.ServiceTypeConfiguration },
        { new global::Android.App.InvalidForegroundServiceTypeException(), ForegroundPromotionFailure.ServiceTypeConfiguration },
        { new global::Java.Lang.IllegalArgumentException(), ForegroundPromotionFailure.ServiceTypeConfiguration },
        { new global::Java.Lang.SecurityException(), ForegroundPromotionFailure.PermissionDenied },
        { new System.Security.SecurityException("denied"), ForegroundPromotionFailure.PermissionDenied },
        { new ArgumentException("bad type"), ForegroundPromotionFailure.ServiceTypeConfiguration },
        { new InvalidOperationException("no notification service"), ForegroundPromotionFailure.PlatformUnavailable },
    };

    [Theory]
    [MemberData(nameof(DocumentedFailures))]
    public void EveryDocumentedRefusalStopsTheCaptureOnceAndReleasesTheServiceOnce(
        Exception refusal,
        ForegroundPromotionFailure expected)
    {
        var recorder = new Recorder();
        var promoted = recorder.Run(() => throw refusal, PlatformLiveCaptureStopReason.NotificationAction);

        Assert.False(promoted);
        Assert.Equal([PlatformLiveCaptureStopReason.NotificationAction], recorder.StopReasons);
        Assert.Equal(1, recorder.ForegroundReleases);
        Assert.Equal(1, recorder.ServiceReleases);
        var report = Assert.Single(recorder.Reports);
        Assert.Equal(expected, report.Failure);
        Assert.Same(refusal, report.Cause);
    }

    [Fact]
    public void ASuccessfulPromotionTouchesNothingElse()
    {
        var recorder = new Recorder();
        var promoted = recorder.Run(static () => { }, PlatformLiveCaptureStopReason.SystemTimeLimit);

        Assert.True(promoted);
        Assert.Empty(recorder.StopReasons);
        Assert.Equal(0, recorder.ForegroundReleases);
        Assert.Equal(0, recorder.ServiceReleases);
        Assert.Empty(recorder.Reports);
    }

    /// <summary>
    /// A cleanup that fails for the same reason the promotion did must not replace it, and
    /// must not stop the service from being released.
    /// </summary>
    [Fact]
    public void AFailedCleanupIsSecondaryAndStillReleasesTheService()
    {
        var refusal = new global::Android.App.ForegroundServiceStartNotAllowedException();
        var cleanupFailure = new global::Java.Lang.SecurityException();
        var recorder = new Recorder { ForegroundReleaseFailure = cleanupFailure };
        var promoted = recorder.Run(() => throw refusal, PlatformLiveCaptureStopReason.NotificationAction);

        Assert.False(promoted);
        Assert.Equal([PlatformLiveCaptureStopReason.NotificationAction], recorder.StopReasons);
        Assert.Equal(1, recorder.ServiceReleases);
        Assert.Equal(2, recorder.Reports.Count);
        Assert.Same(refusal, recorder.Reports[0].Cause);
        Assert.Same(cleanupFailure, recorder.Reports[1].Cause);

        // The category stays the promotion's own: the cleanup failure is context, not a
        // second diagnosis.
        Assert.Equal(ForegroundPromotionFailure.BackgroundRestricted, recorder.Reports[1].Failure);
    }

    /// <summary>
    /// An undocumented failure is a service bug and keeps propagating. Swallowing it would
    /// turn every defect in this file into a capture that silently declines.
    /// </summary>
    [Fact]
    public void AnUndocumentedFailureIsNotHandled()
    {
        var recorder = new Recorder();
        var thrown = Assert.Throws<NotSupportedException>(
            () => recorder.Run(static () => throw new NotSupportedException("unrelated"), PlatformLiveCaptureStopReason.NotificationAction));

        Assert.Equal("unrelated", thrown.Message);
        Assert.Empty(recorder.StopReasons);
        Assert.Equal(0, recorder.ServiceReleases);
    }

    /// <summary>
    /// Every refusal category has a sentence, and none of it is platform text.
    /// </summary>
    /// <remarks>
    /// With the foreground-start app op denied, a Samsung SM-G990B put this in front of the
    /// reader: <c>Unable to start service Intent { act=…START_LIVE_CAPTURE xflg=0x4
    /// cmp=com.barebit.visualcat/.CaptureForegroundService (has extras) }: foreground not
    /// allowed as per app op</c>. The workspace shows a failure reason verbatim, so the
    /// sentence has to be produced where the refusal is caught.
    /// </remarks>
    [Theory]
    [InlineData(ForegroundPromotionFailure.BackgroundRestricted)]
    [InlineData(ForegroundPromotionFailure.PermissionDenied)]
    [InlineData(ForegroundPromotionFailure.ServiceTypeConfiguration)]
    [InlineData(ForegroundPromotionFailure.PlatformUnavailable)]
    public void EveryCategoryExplainsItselfWithoutPlatformText(ForegroundPromotionFailure failure)
    {
        var sentence = ForegroundPromotion.Explain(failure);

        Assert.EndsWith(".", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("Intent", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("com.barebit", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("app op", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VisualCat", sentence, StringComparison.Ordinal);
    }

    /// <summary>
    /// The type a device actually raised is classified, not only the documented one.
    /// </summary>
    [Fact]
    public void TheTypeTheDeviceRaisedIsRecognised()
    {
        // Android 16 on a Samsung SM-G990B raised this for a denied foreground-start app op,
        // where the platform documentation names ForegroundServiceStartNotAllowedException.
        Assert.Equal(
            ForegroundPromotionFailure.PermissionDenied,
            ForegroundPromotion.Classify(new global::Java.Lang.SecurityException()));
        Assert.Equal(
            ForegroundPromotionFailure.BackgroundRestricted,
            ForegroundPromotion.Classify(new global::Java.Lang.IllegalStateException()));
    }

    /// <summary>The platform's own text is kept, but as the cause rather than the message.</summary>
    [Fact]
    public void TheRefusalCarriesTheProductSentenceAndKeepsThePlatformCause()
    {
        var platform = new global::Java.Lang.SecurityException();
        var refusal = new BackgroundExecutionUnavailableException(
            ForegroundPromotion.Explain(ForegroundPromotionFailure.PermissionDenied),
            platform);

        Assert.Equal(ForegroundPromotion.Explain(ForegroundPromotionFailure.PermissionDenied), refusal.Message);
        Assert.Same(platform, refusal.InnerException);
    }

    private sealed class Recorder
    {
        public List<PlatformLiveCaptureStopReason> StopReasons { get; } = [];

        public List<(ForegroundPromotionFailure Failure, Exception Cause)> Reports { get; } = [];

        public int ForegroundReleases { get; private set; }

        public int ServiceReleases { get; private set; }

        public Exception? ForegroundReleaseFailure { get; init; }

        public bool Run(Action publish, PlatformLiveCaptureStopReason reason) =>
            ForegroundPromotion.TryPublish(
                publish,
                reason,
                StopReasons.Add,
                () =>
                {
                    ForegroundReleases++;
                    if (ForegroundReleaseFailure is { } failure)
                    {
                        throw failure;
                    }
                },
                () => ServiceReleases++,
                (failure, cause) => Reports.Add((failure, cause)));
    }
}
