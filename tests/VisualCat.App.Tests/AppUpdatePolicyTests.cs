using VisualCat.App.Platform;
using VisualCat.Domain;

namespace VisualCat.App.Tests;

/// <summary>
/// The update rules, tested where they can actually be tested.
/// </summary>
/// <remarks>
/// Google Play never offers an update to a build that was not installed by Google Play, so the
/// real path cannot be exercised in the developer loop, in CI, or on a device that is not a
/// Play install. Everything that decides <em>behaviour</em> therefore lives in
/// <see cref="AppUpdatePolicy"/>, off-device, and this is where it is held to account —
/// especially the live-capture guard, which is the rule whose violation costs a reader their
/// recording.
/// </remarks>
public sealed class AppUpdatePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static AppUpdateStatus Available(
        long versionCode = 2010000,
        string? name = "2.1.0",
        int priority = 1,
        int? staleness = null,
        bool flexible = true,
        bool immediate = true) =>
        new(
            AppUpdateState.Available,
            versionCode,
            name,
            priority,
            staleness,
            flexible,
            immediate);

    [Theory]
    [InlineData(ReleaseChannel.Development, false)]
    [InlineData(ReleaseChannel.Alpha, true)]
    [InlineData(ReleaseChannel.Beta, true)]
    [InlineData(ReleaseChannel.Stable, true)]
    public void ColdStartChecksOnEveryChannelExceptDevelopment(ReleaseChannel channel, bool expected) =>
        Assert.Equal(
            expected,
            AppUpdatePolicy.ShouldCheck(
                channel,
                AppInstallOrigin.PlayStore,
                coldStart: true,
                AppUpdateMemory.Empty,
                Now));

    [Fact]
    public void AStableColdStartWaitsADayBetweenChecks()
    {
        var checkedRecently = new AppUpdateMemory(LastCheckedUtc: Now.AddHours(-3));
        Assert.False(AppUpdatePolicy.ShouldCheck(
            ReleaseChannel.Stable, AppInstallOrigin.PlayStore, coldStart: true, checkedRecently, Now));

        var checkedYesterday = new AppUpdateMemory(LastCheckedUtc: Now.AddHours(-25));
        Assert.True(AppUpdatePolicy.ShouldCheck(
            ReleaseChannel.Stable, AppInstallOrigin.PlayStore, coldStart: true, checkedYesterday, Now));
    }

    /// <summary>A tester who is not on the newest build is not testing anything.</summary>
    [Fact]
    public void AlphaColdStartAsksEvenRightAfterTheLastCheck() =>
        Assert.True(AppUpdatePolicy.ShouldCheck(
            ReleaseChannel.Alpha,
            AppInstallOrigin.PlayStore,
            coldStart: true,
            new AppUpdateMemory(LastCheckedUtc: Now.AddMinutes(-1)),
            Now));

    [Theory]
    [InlineData(ReleaseChannel.Alpha, 10, false)]
    [InlineData(ReleaseChannel.Alpha, 20, true)]
    [InlineData(ReleaseChannel.Beta, 300, false)]
    [InlineData(ReleaseChannel.Beta, 400, true)]
    [InlineData(ReleaseChannel.Stable, 1200, false)]
    [InlineData(ReleaseChannel.Stable, 1500, true)]
    public void ResumeThrottleIsPerChannel(ReleaseChannel channel, int minutesSince, bool expected) =>
        Assert.Equal(
            expected,
            AppUpdatePolicy.ShouldCheck(
                channel,
                AppInstallOrigin.PlayStore,
                coldStart: false,
                new AppUpdateMemory(LastCheckedUtc: Now.AddMinutes(-minutesSince)),
                Now));

    [Theory]
    [InlineData(AppInstallOrigin.SideLoaded)]
    [InlineData(AppInstallOrigin.OtherStore)]
    [InlineData(AppInstallOrigin.Unknown)]
    public void OnlyAPlayInstallIsEverChecked(AppInstallOrigin origin) =>
        Assert.False(AppUpdatePolicy.ShouldCheck(
            ReleaseChannel.Stable, origin, coldStart: true, AppUpdateMemory.Empty, Now));

    /// <summary>
    /// A clock that moved backwards must not be able to suppress checking for however far it
    /// went — a restored backup or a timezone edit is not a decision the reader made.
    /// </summary>
    [Fact]
    public void AFutureLastCheckDoesNotSuppressTheNextOne() =>
        Assert.True(AppUpdatePolicy.ShouldCheck(
            ReleaseChannel.Stable,
            AppInstallOrigin.PlayStore,
            coldStart: true,
            new AppUpdateMemory(LastCheckedUtc: Now.AddDays(400)),
            Now));

    [Fact]
    public void ADevelopmentBuildNeverPromptsWhateverTheStoreSays() =>
        Assert.Null(AppUpdatePolicy.Decide(
            Available(priority: 5, staleness: 400),
            ReleaseChannel.Development,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now));

    [Fact]
    public void AnOfferNamesTheVersionAndOffersUpdate()
    {
        var prompt = AppUpdatePolicy.Decide(
            Available(), ReleaseChannel.Stable, liveCaptureRunning: false, AppUpdateMemory.Empty, Now);

        Assert.NotNull(prompt);
        Assert.Equal("VisualCat 2.1.0 is available on Google Play.", prompt.Message);
        Assert.Equal("Update", prompt.ActionLabel);
        Assert.True(prompt.Persistent);
    }

    /// <summary>
    /// The app cannot learn which track an offer came from, so it names its own channel or
    /// nothing at all. Claiming "beta update available" would be a guess.
    /// </summary>
    [Theory]
    [InlineData(ReleaseChannel.Alpha, "VisualCat 2.1.0 is available on Google Play. You are on the alpha channel.")]
    [InlineData(ReleaseChannel.Beta, "VisualCat 2.1.0 is available on Google Play. You are on the beta channel.")]
    [InlineData(ReleaseChannel.Stable, "VisualCat 2.1.0 is available on Google Play.")]
    public void TheOfferNamesTheReadersOwnChannelAndNeverTheUpdates(ReleaseChannel channel, string expected) =>
        Assert.Equal(
            expected,
            AppUpdatePolicy.Decide(
                Available(), channel, liveCaptureRunning: false, AppUpdateMemory.Empty, Now)!.Message);

    /// <summary>
    /// A version code that does not decode produces a vague sentence, not a wrong one — and it
    /// is still a sentence: the unnamed fallback is an article, so it has to be capitalised
    /// where it opens one and left alone where it does not.
    /// </summary>
    [Fact]
    public void AnUndecodableVersionCodeIsNotGivenAName() =>
        Assert.Equal(
            "A newer VisualCat is available on Google Play.",
            AppUpdatePolicy.Decide(
                Available(versionCode: 77, name: null),
                ReleaseChannel.Stable,
                liveCaptureRunning: false,
                AppUpdateMemory.Empty,
                Now)!.Message);

    [Fact]
    public void TheUnnamedFallbackIsCapitalisedOnlyWhereItOpensASentence()
    {
        Assert.Equal(
            "A newer VisualCat is downloaded. Installing restarts the app.",
            AppUpdatePolicy.Decide(
                new AppUpdateStatus(AppUpdateState.ReadyToInstall),
                ReleaseChannel.Stable,
                liveCaptureRunning: false,
                AppUpdateMemory.Empty,
                Now)!.Message);

        Assert.Equal(
            "An update was interrupted. Resume it to finish installing a newer VisualCat.",
            AppUpdatePolicy.Decide(
                new AppUpdateStatus(AppUpdateState.InProgress),
                ReleaseChannel.Stable,
                liveCaptureRunning: false,
                AppUpdateMemory.Empty,
                Now)!.Message);
    }

    [Fact]
    public void DismissingAnOfferSilencesThatVersionForTheChannelsSnooze()
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 2010000,
            SnoozedUntilUtc: AppUpdatePolicy.SnoozeUntil(ReleaseChannel.Stable, Now));

        Assert.Null(AppUpdatePolicy.Decide(
            Available(), ReleaseChannel.Stable, liveCaptureRunning: false, dismissed, Now.AddDays(3)));

        // Seven days on stable, and then the same offer may be raised again.
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(), ReleaseChannel.Stable, liveCaptureRunning: false, dismissed, Now.AddDays(8)));
    }

    [Fact]
    public void AHigherVersionCodeIsANewQuestion()
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 2010000,
            SnoozedUntilUtc: Now.AddDays(6));

        Assert.Null(AppUpdatePolicy.Decide(
            Available(versionCode: 2010000), ReleaseChannel.Stable, false, dismissed, Now));

        // A genuinely newer build is not the thing that was refused, so the snooze does not
        // carry to it. This is also what stops a dismissal from outliving its release.
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(versionCode: 2010100), ReleaseChannel.Stable, false, dismissed, Now));
    }

    [Theory]
    [InlineData(ReleaseChannel.Alpha, 2, false)]
    [InlineData(ReleaseChannel.Alpha, 3, true)]
    [InlineData(ReleaseChannel.Beta, 3, false)]
    [InlineData(ReleaseChannel.Beta, 4, true)]
    [InlineData(ReleaseChannel.Stable, 3, false)]
    [InlineData(ReleaseChannel.Stable, 4, true)]
    public void PriorityOverridesADismissalAtAChannelSpecificThreshold(
        ReleaseChannel channel,
        int priority,
        bool expected)
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 2010000,
            SnoozedUntilUtc: Now.AddDays(6));

        var prompt = AppUpdatePolicy.Decide(
            Available(priority: priority), channel, liveCaptureRunning: false, dismissed, Now);
        Assert.Equal(expected, prompt is not null);
    }

    [Theory]
    [InlineData(ReleaseChannel.Alpha, 3, AppUpdatePromptAction.StartFlexible)]
    [InlineData(ReleaseChannel.Alpha, 4, AppUpdatePromptAction.StartImmediate)]
    [InlineData(ReleaseChannel.Beta, 4, AppUpdatePromptAction.StartImmediate)]
    [InlineData(ReleaseChannel.Stable, 4, AppUpdatePromptAction.StartFlexible)]
    [InlineData(ReleaseChannel.Stable, 5, AppUpdatePromptAction.StartImmediate)]
    public void OnlyAnUrgentReleaseMayTakeTheScreen(
        ReleaseChannel channel,
        int priority,
        AppUpdatePromptAction expected) =>
        Assert.Equal(
            expected,
            AppUpdatePolicy.Decide(
                Available(priority: priority),
                channel,
                liveCaptureRunning: false,
                AppUpdateMemory.Empty,
                Now)!.Action);

    /// <summary>
    /// Play reports null staleness far more often than it reports zero, and the two mean
    /// opposite things: null is "Play did not say", zero is "this became available today".
    /// Reading null as zero would silently disable the stale-build escalation.
    /// </summary>
    [Fact]
    public void UnknownStalenessIsNotTreatedAsZero()
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 2010000,
            SnoozedUntilUtc: Now.AddDays(6));

        Assert.Null(AppUpdatePolicy.Decide(
            Available(staleness: null), ReleaseChannel.Stable, false, dismissed, Now));
        Assert.Null(AppUpdatePolicy.Decide(
            Available(staleness: 29), ReleaseChannel.Stable, false, dismissed, Now));
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(staleness: 30), ReleaseChannel.Stable, false, dismissed, Now));
    }

    /// <summary>Staleness escalates on stable only; testers are nudged by priority instead.</summary>
    [Fact]
    public void StalenessDoesNotReopenADismissalOnATestingChannel()
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 2010000,
            SnoozedUntilUtc: Now.AddDays(6));

        Assert.Null(AppUpdatePolicy.Decide(
            Available(staleness: 400), ReleaseChannel.Beta, false, dismissed, Now));
    }

    // --- the live-capture guard ------------------------------------------------------------

    /// <summary>
    /// An update banner arriving over a recording somebody just started reads as an error about
    /// the recording, in the lane where capture failures are reported.
    /// </summary>
    [Fact]
    public void NoOfferIsRaisedOverARunningCapture() =>
        Assert.Null(AppUpdatePolicy.Decide(
            Available(priority: 5),
            ReleaseChannel.Stable,
            liveCaptureRunning: true,
            AppUpdateMemory.Empty,
            Now));

    /// <summary>
    /// The offer is deferred, not discarded: the same status decides differently the moment the
    /// recording ends, which is what makes "it re-offers as soon as the capture ends" true.
    /// </summary>
    [Fact]
    public void TheSameOfferIsMadeOnceTheCaptureEnds() =>
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now));

    /// <summary>
    /// Installing restarts the process, which ends a recording without the drain-seal-reopen
    /// that Stop performs. The install is withheld and the sentence says whose move it is.
    /// </summary>
    [Fact]
    public void ADownloadedUpdateIsNotInstallableDuringACapture()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.ReadyToInstall, 2010000, "2.1.0"),
            ReleaseChannel.Stable,
            liveCaptureRunning: true,
            AppUpdateMemory.Empty,
            Now);

        Assert.NotNull(prompt);
        Assert.Null(prompt.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.None, prompt.Action);
        Assert.Contains("Stop the capture to install it", prompt.Message, StringComparison.Ordinal);
        Assert.Contains("restarts the app", prompt.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADownloadedUpdateOffersInstallWhenNothingIsRecording()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.ReadyToInstall, 2010000, "2.1.0"),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now);

        Assert.Equal("Install", prompt!.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.CompleteInstall, prompt.Action);
        Assert.Contains("restarts the app", prompt.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A manual check during a capture may still offer the download — Play downloads in the
    /// background and only the install touches the process — but the button must name that
    /// much and no more.
    /// </summary>
    [Fact]
    public void AManualCheckDuringACaptureOffersOnlyTheDownload()
    {
        var prompt = AppUpdatePolicy.Decide(
            Available(priority: 5),
            ReleaseChannel.Stable,
            liveCaptureRunning: true,
            AppUpdateMemory.Empty,
            Now,
            manual: true);

        Assert.Equal("Download", prompt!.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.StartFlexible, prompt.Action);
        Assert.Contains("waits until the capture stops", prompt.Message, StringComparison.Ordinal);
    }

    /// <summary>An interrupted install is also an install, and must wait for the recording too.</summary>
    [Fact]
    public void AnInterruptedUpdateIsNotResumableDuringACapture()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.InProgress, 2010000, "2.1.0"),
            ReleaseChannel.Stable,
            liveCaptureRunning: true,
            AppUpdateMemory.Empty,
            Now);

        Assert.Null(prompt!.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.None, prompt.Action);
    }

    // --- work already in flight ------------------------------------------------------------

    /// <summary>
    /// Work the app already started is reported whatever the throttles say: the reader is
    /// waiting on it, and Play requires it to be picked up on every resume.
    /// </summary>
    [Fact]
    public void ADownloadInFlightIsReportedThroughADismissal()
    {
        var dismissed = new AppUpdateMemory(
            DismissedVersionCode: 9_999_999,
            SnoozedUntilUtc: Now.AddYears(1));

        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(
                AppUpdateState.Downloading,
                2010000,
                "2.1.0",
                BytesDownloaded: 13_002_342,
                TotalBytesToDownload: 32_505_856),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            dismissed,
            Now);

        Assert.NotNull(prompt);
        Assert.Equal("Downloading VisualCat 2.1.0 · 12.4 MB of 31.0 MB.", prompt.Message);
        Assert.Null(prompt.ActionLabel);
        Assert.False(prompt.Persistent);
    }

    [Fact]
    public void AFailedFlowSendsTheReaderToTheStore()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.Failed, Message: "The update did not start. Try again from the Play Store."),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now);

        Assert.Equal("Open Play", prompt!.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.OpenStore, prompt.Action);
    }

    /// <summary>
    /// Play knows about a newer build but will not let this app start either flow. Offering an
    /// Update button that cannot work is the defect; naming the store is the honest move.
    /// </summary>
    [Fact]
    public void AnOfferNoFlowCanStartSendsTheReaderToTheStore()
    {
        var prompt = AppUpdatePolicy.Decide(
            Available(flexible: false, immediate: false),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now);

        Assert.Equal("Open Play", prompt!.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.OpenStore, prompt.Action);
    }

    // --- background silence, manual candour ------------------------------------------------

    [Theory]
    [InlineData(AppUpdateState.UpToDate)]
    [InlineData(AppUpdateState.Unknown)]
    [InlineData(AppUpdateState.Unsupported)]
    public void ABackgroundCheckThatFindsNothingSaysNothing(AppUpdateState state) =>
        Assert.Null(AppUpdatePolicy.Decide(
            new AppUpdateStatus(state),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now));

    /// <summary>"Nothing happened" is not an answer to a question the reader typed.</summary>
    [Fact]
    public void AManualCheckStatesThatTheBuildIsCurrent()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.UpToDate),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now,
            manual: true);

        Assert.NotNull(prompt);
        Assert.Contains("newest build Google Play has for you", prompt.Message, StringComparison.Ordinal);
        Assert.Null(prompt.ActionLabel);
    }

    /// <summary>
    /// An offline device that reports itself current is giving confident wrong news about the
    /// one thing it could not find out.
    /// </summary>
    [Fact]
    public void AFailedQueryNeverMasqueradesAsGoodNews()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.Unknown),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now,
            manual: true);

        Assert.NotNull(prompt);
        Assert.Contains("could not be reached", prompt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("newest", prompt.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASideLoadedBuildIsToldTheTruthAndPointedAtGitHub()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.Unsupported),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now,
            manual: true);

        Assert.NotNull(prompt);
        Assert.Contains("installed from a file", prompt.Message, StringComparison.Ordinal);
        Assert.Equal("Open releases", prompt.ActionLabel);
        Assert.Equal(AppUpdatePromptAction.OpenReleases, prompt.Action);
    }

    /// <summary>
    /// A build another store installed was not installed from a file, and telling somebody it
    /// was sends them looking for a file they never had.
    /// </summary>
    [Fact]
    public void AnotherStoresInstallIsNotDescribedAsAFile()
    {
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.Unsupported),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now,
            manual: true,
            AppInstallOrigin.OtherStore);

        Assert.NotNull(prompt);
        Assert.Contains("installed by another store", prompt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("from a file", prompt.Message, StringComparison.Ordinal);
        Assert.Equal("Open releases", prompt.ActionLabel);
    }

    /// <summary>A development build still answers a question the reader asked out loud.</summary>
    [Fact]
    public void AManualCheckAnswersEvenOnADevelopmentBuild() =>
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(),
            ReleaseChannel.Development,
            liveCaptureRunning: false,
            AppUpdateMemory.Empty,
            Now,
            manual: true));

    /// <summary>A manual check is not silenced by an earlier Dismiss.</summary>
    [Fact]
    public void AManualCheckIgnoresAStandingDismissal() =>
        Assert.NotNull(AppUpdatePolicy.Decide(
            Available(),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            new AppUpdateMemory(DismissedVersionCode: 2010000, SnoozedUntilUtc: Now.AddYears(1)),
            Now,
            manual: true));
}
