using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Views;
using VisualCat.Domain;

namespace VisualCat.App.Tests;

/// <summary>
/// The update offer's manners in the one notice lane this product has.
/// </summary>
/// <remarks>
/// <para>
/// The lane is shared. It is where a failed export, a failed cleanup and — the one capture
/// outcome the reader cannot see for themselves — a recording that stopped without being asked
/// to are reported, and <c>ShowNotice</c> replaces whatever it is carrying, unconditionally. An
/// update offer arriving on resume and erasing an unread capture failure would be a regression,
/// not a cosmetic issue, so it is asserted here directly rather than reasoned about.
/// </para>
/// <para>
/// These run on the desktop TFM, where the lane's host is hidden — the assertions read the
/// lane's own state rather than its visibility, which is what makes the behaviour testable
/// without a device at all.
/// </para>
/// </remarks>
public sealed class MainViewUpdateNoticeTests
{
    private static AppUpdateStatus Available(long versionCode = 2010000, string? name = "2.1.0") =>
        new(AppUpdateState.Available, versionCode, name, FlexibleAllowed: true, ImmediateAllowed: true);

    /// <summary>
    /// Runs a body with the update channel forced, then puts it back.
    /// </summary>
    /// <remarks>
    /// The test assembly's own version is whatever the build stamped, which is a Development
    /// build that by design never prompts. Forcing the channel is the same seam the Android
    /// fake-update path uses, and that is precisely why it exists.
    /// </remarks>
    private static async Task WithChannel(ReleaseChannel channel, Func<MainView, Task> body)
    {
        var previous = MainView.UpdateChannel;
        MainView.UpdateChannel = channel;

        // The status cache is process-static, which is exactly what it is for in production: a
        // view rebuilt by an activity recreation renders the offer already in flight. In a test
        // assembly it is shared state between tests, and a status left behind by one of them
        // paints a banner into the next one's lane before its first assertion runs. Cleared on
        // the way in as well as the way out, so a test cannot inherit one either.
        PlatformSourceRegistry.CacheAppUpdateStatus(AppUpdateStatus.None);

        // Its own settings file, not this machine's. These tests dismiss offers, and a
        // dismissal is persisted: run against the real path they wrote a week-long snooze into
        // the developer's own settings and then failed, on that machine only, from the second
        // run onwards. Starting from a file that does not exist also means every test begins
        // with the update memory empty, which is the state the assertions describe.
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"visualcat-update-tests-{Guid.NewGuid():N}",
            "settings.json");
        var view = new MainView(null, settingsPath);
        var window = new Window { Content = view, Width = 1400, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await body(view);
        }
        finally
        {
            window.Close();
            await view.DisposeAsync();
            MainView.UpdateChannel = previous;
            PlatformSourceRegistry.CacheAppUpdateStatus(AppUpdateStatus.None);
            var directory = Path.GetDirectoryName(settingsPath)!;
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover temp file is not a test failure. Disposal drains the settings
                // writes this view started, so reaching here means something outside the view
                // still had the file open — a virus scanner, most likely.
            }
        }
    }

    private static string LaneText(MainView view) =>
        view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(static block =>
                Avalonia.Automation.AutomationProperties.GetName(block) == "Application status message")
            ?.Text ?? string.Empty;

    private static Button? LaneAction(MainView view) =>
        view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(static button => button.Name is null && button.Content is string content &&
                content is "Update" or "Install" or "Resume" or "Open Play" or "Open releases" or "Download");

    private static Button LaneDismiss(MainView view) =>
        view.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button =>
                Avalonia.Automation.AutomationProperties.GetName(button) == "Dismiss application status message");

    [AvaloniaFact]
    public async Task AnAvailableUpdateRendersAnOfferWithAnActionButton() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("VisualCat 2.1.0 is available on Google Play.", LaneText(view));
            Assert.NotNull(LaneAction(view));
            Assert.Equal("Update", (string)LaneAction(view)!.Content!);
            return Task.CompletedTask;
        });

    /// <summary>
    /// The rule the whole partial exists to hold: an unread failure keeps the lane.
    /// </summary>
    [AvaloniaFact]
    public async Task AnUpdateOfferDoesNotEraseAnUnreadCaptureFailure() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.ShowNotice("Capture stopped: the device disconnected.", MainView.NoticeKind.Failure);
            Dispatcher.UIThread.RunJobs();

            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Capture stopped: the device disconnected.", LaneText(view));
            return Task.CompletedTask;
        });

    /// <summary>An export confirmation is not collateral either.</summary>
    [AvaloniaFact]
    public async Task AnUpdateOfferDoesNotEraseAnUnreadCompletion() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.ShowNotice("Exported 12,000 entries to entries.csv", MainView.NoticeKind.Completion);
            Dispatcher.UIThread.RunJobs();

            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Exported 12,000 entries to entries.csv", LaneText(view));
            return Task.CompletedTask;
        });

    /// <summary>
    /// The offer is held rather than dropped, and goes up the moment the reader clears the lane
    /// — without waiting for a resume, a capture change, or anything else to happen to arrive.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes the withheld prompt worth keeping at all. It used to be
    /// written with a second explicit render standing in for whatever would eventually re-raise
    /// it, which passed while nothing in the product actually did: dismissing the failure left
    /// the lane empty and the offer sat in a field until an unrelated event happened to shake
    /// it loose. Nothing here calls into the update path a second time.
    /// </remarks>
    [AvaloniaFact]
    public async Task AWithheldOfferAppearsTheMomentTheReaderClearsTheLane() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.ShowNotice("Export failed.", MainView.NoticeKind.Failure);
            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Export failed.", LaneText(view));

            // The reader reads the failure and clears it. Nothing else happens.
            LaneDismiss(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("VisualCat 2.1.0 is available on Google Play.", LaneText(view));
            return Task.CompletedTask;
        });

    /// <summary>
    /// Clearing a message the update path never withheld leaves the lane empty.
    /// </summary>
    /// <remarks>
    /// The counterpart to the test above, and the reason it re-raises only a genuinely withheld
    /// prompt rather than simply re-deciding: a failure and an "installed from a file" message
    /// are not gated by any snooze, so re-deciding on every dismissal would put them straight
    /// back on screen and leave the reader with a notice they cannot get rid of.
    /// </remarks>
    [AvaloniaFact]
    public async Task DismissingAMessageNothingWasWaitingBehindLeavesTheLaneEmpty() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.RenderUpdateStatus(new AppUpdateStatus(AppUpdateState.Failed, Message: "The update did not start."));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("The update did not start.", LaneText(view));

            LaneDismiss(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, LaneText(view));
            Assert.Null(view.HoldingNoticeKind);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Dismissing a downloaded update stops it reappearing, without touching the settings file
    /// before it has been read.
    /// </summary>
    /// <remarks>
    /// A headless view never completes <c>InitializeAsync</c>, so its settings are unloaded —
    /// which is exactly the state a resume can catch on a device. Nothing here may be persisted,
    /// and the banner must still come down and stay down for this render.
    /// </remarks>
    [AvaloniaFact]
    public async Task DismissingADownloadedUpdateTakesTheBannerDown() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.RenderUpdateStatus(new AppUpdateStatus(AppUpdateState.ReadyToInstall, 2010000, "2.1.0"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Install", (string)LaneAction(view)!.Content!);

            LaneDismiss(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, LaneText(view));
            return Task.CompletedTask;
        });

    /// <summary>
    /// Download progress carries no button and is not treated as something to dismiss.
    /// </summary>
    [AvaloniaFact]
    public async Task DownloadProgressRendersWithoutAnAction() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.RenderUpdateStatus(new AppUpdateStatus(
                AppUpdateState.Downloading,
                2010000,
                "2.1.0",
                BytesDownloaded: 13_002_342,
                TotalBytesToDownload: 32_505_856));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Downloading VisualCat 2.1.0 · 12.4 MB of 31.0 MB.", LaneText(view));
            Assert.Null(LaneAction(view));
            Assert.Equal(MainView.NoticeKind.Completion, view.HoldingNoticeKind);
            return Task.CompletedTask;
        });

    /// <summary>
    /// The message the command bar shows is not a second copy of the lane's on Android, and it
    /// can never outgrow the space it has.
    /// </summary>
    /// <remarks>
    /// It sat in the brand row's trailing auto-sized column, which gives a TextBlock its full
    /// desired width, so the CharacterEllipsis it asked for could never engage: on a 1080 px
    /// phone the update offer painted through the wordmark and off the edge of the screen. This
    /// asserts the two properties that make that impossible — the flexible column, and the
    /// trimming that column finally lets work.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheCommandBarMessageIsWidthLimitedAndTrimmed() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            var message = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Single(static block => Grid.GetColumn(block) == 1 &&
                    block.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis &&
                    block.Parent is Grid { ColumnDefinitions.Count: 3 });

            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, message.HorizontalAlignment);
            Assert.Equal(Avalonia.Media.TextAlignment.Right, message.TextAlignment);
            return Task.CompletedTask;
        });

    /// <summary>An ordinary confirmation is repeatable at no cost, so the offer may take it.</summary>
    [AvaloniaFact]
    public async Task AnUpdateOfferMayReplaceAnInformationNotice() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.ShowNotice("Copied 3 entries.");
            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("VisualCat 2.1.0 is available on Google Play.", LaneText(view));
            return Task.CompletedTask;
        });

    /// <summary>
    /// A notice the reader dismisses runs its own dismissal callback; one that is superseded
    /// by an unrelated message does not. Inferring dismissal by watching revisions from
    /// outside would misfire every time something else took the lane.
    /// </summary>
    [AvaloniaFact]
    public async Task DismissalRunsOnlyForTheNoticeTheReaderCleared() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            var dismissed = 0;
            view.ShowNotice("First", MainView.NoticeKind.Completion, dismissed: () => dismissed++);
            Dispatcher.UIThread.RunJobs();

            view.ShowNotice("Second", MainView.NoticeKind.Completion);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, dismissed);

            view.DismissNotice();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, dismissed);

            view.ShowNotice("Third", MainView.NoticeKind.Completion, dismissed: () => dismissed++);
            Dispatcher.UIThread.RunJobs();
            LaneDismiss(view).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, dismissed);
            return Task.CompletedTask;
        });

    /// <summary>
    /// The lane can say what it is carrying, which is what lets anything raised without the
    /// reader asking see what it would be erasing.
    /// </summary>
    [AvaloniaFact]
    public async Task TheLaneReportsWhatItIsHolding() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            Assert.Null(view.HoldingNoticeKind);

            view.ShowNotice("Something failed.", MainView.NoticeKind.Failure);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MainView.NoticeKind.Failure, view.HoldingNoticeKind);

            view.DismissNotice();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(view.HoldingNoticeKind);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Android rebuilds the view on every activity recreation while the Play client survives,
    /// so an offer already in flight has to survive a text-size change with it.
    /// </summary>
    [AvaloniaFact]
    public async Task ARebuiltViewRendersTheCachedStatusWithoutANewCheck()
    {
        var previous = MainView.UpdateChannel;
        MainView.UpdateChannel = ReleaseChannel.Stable;
        try
        {
            PlatformSourceRegistry.PublishAppUpdateStatus(Available());

            var rebuilt = new MainView(null, Path.Combine(
                Path.GetTempPath(),
                $"visualcat-update-tests-{Guid.NewGuid():N}",
                "settings.json"));
            var window = new Window { Content = rebuilt, Width = 1400, Height = 800 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("VisualCat 2.1.0 is available on Google Play.", LaneText(rebuilt));
            }
            finally
            {
                window.Close();
                await rebuilt.DisposeAsync();
            }
        }
        finally
        {
            PlatformSourceRegistry.PublishAppUpdateStatus(AppUpdateStatus.None);
            PlatformSourceRegistry.CacheAppUpdateStatus(AppUpdateStatus.None);
            MainView.UpdateChannel = previous;
        }
    }

    /// <summary>
    /// A published status reaches the live view through the platform's static event, which is
    /// how the Android service — which outlives every view — reports progress.
    /// </summary>
    [AvaloniaFact]
    public async Task APublishedStatusReachesTheLiveView() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            PlatformSourceRegistry.PublishAppUpdateStatus(
                new AppUpdateStatus(AppUpdateState.ReadyToInstall, 2010000, "2.1.0"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("VisualCat 2.1.0 is downloaded. Installing restarts the app.", LaneText(view));
            Assert.Equal("Install", (string)LaneAction(view)!.Content!);
            PlatformSourceRegistry.PublishAppUpdateStatus(AppUpdateStatus.None);
            return Task.CompletedTask;
        });

    /// <summary>A store answer with nothing in it leaves the screen alone.</summary>
    [AvaloniaFact]
    public async Task AnUpToDateAnswerSaysNothingInTheBackground() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.RenderUpdateStatus(new AppUpdateStatus(AppUpdateState.UpToDate));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, LaneText(view));
            return Task.CompletedTask;
        });
}
