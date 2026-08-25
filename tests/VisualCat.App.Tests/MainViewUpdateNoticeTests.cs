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
        var view = new MainView();
        var window = new Window { Content = view, Width = 1400, Height = 800 };
        window.Show();
        try
        {
            await body(view);
        }
        finally
        {
            window.Close();
            await view.DisposeAsync();
            MainView.UpdateChannel = previous;
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
    /// The offer is held rather than dropped, so it appears the moment the lane is free
    /// instead of waiting for the next resume to re-ask Play.
    /// </summary>
    [AvaloniaFact]
    public async Task AWithheldOfferAppearsOnceTheLaneIsFree() =>
        await WithChannel(ReleaseChannel.Stable, view =>
        {
            view.ShowNotice("Export failed.", MainView.NoticeKind.Failure);
            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Export failed.", LaneText(view));

            // The reader reads the failure and clears it. The offer is still pending.
            view.DismissNotice();
            Dispatcher.UIThread.RunJobs();
            view.RenderUpdateStatus(Available());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("VisualCat 2.1.0 is available on Google Play.", LaneText(view));
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

            var rebuilt = new MainView();
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
