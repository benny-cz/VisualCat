using System.Reflection;
using Avalonia.Headless.XUnit;
using VisualCat.App.Platform;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// Exactly one <see cref="MainView"/> answers the platform's static event surface.
/// </summary>
/// <remarks>
/// <para>
/// Android builds a replacement view whenever it recreates the activity and, unlike the
/// desktop host, has no window-closed moment at which to dispose the one it replaces. This
/// was confirmed on a device: forcing a recreation inside a surviving process produced a
/// second view, and <c>AppResumed</c> was then answered by both — the abandoned view
/// resuming live views and re-running queries for a workspace nobody could see, and, on
/// pause, writing its own now-stale open-workspace list into the same settings file the
/// live view writes.
/// </para>
/// <para>
/// The events are static, so the counts here are the invariant itself rather than a proxy
/// for it: the backing delegate of a field-like event is a private static field of the same
/// name, and its invocation list is what actually gets called.
/// </para>
/// </remarks>
public sealed class MainViewPlatformSubscriptionTests
{
    private static readonly string[] PlatformEvents =
    [
        "LaunchFilesReceived",
        "AppResumed",
        "AppPaused",
        "DisplayConfigurationChanged",
    ];

    [AvaloniaFact]
    public async Task AReplacementViewTakesOverTheSubscriptionsRatherThanAddingToThem()
    {
        var first = new MainView();
        try
        {
            AssertSubscriberCount(1, "the first view");

            var second = new MainView();
            try
            {
                // Not two. Before the handover this was where the abandoned view stayed.
                AssertSubscriberCount(1, "a replacement view");
            }
            finally
            {
                await second.DisposeAsync();
            }

            AssertSubscriberCount(0, "the replacement being disposed");
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    /// <summary>
    /// Disposing a view that has already been replaced leaves the live one attached.
    /// </summary>
    /// <remarks>
    /// The desktop host disposes its view when the window closes, and that path removes the
    /// view's own handlers unconditionally. It must not also clear the shared slot when a
    /// newer view is the one sitting in it, or the live view would go deaf to the platform —
    /// which on Android is every resume, pause, launch intent and text-size change.
    /// </remarks>
    [AvaloniaFact]
    public async Task DisposingASupersededViewDoesNotDetachTheLiveOne()
    {
        var superseded = new MainView();
        var live = new MainView();
        try
        {
            AssertSubscriberCount(1, "two views existing");

            await superseded.DisposeAsync();
            AssertSubscriberCount(1, "the superseded view being disposed");

            // And it is the live view that is still attached: disposing it clears the slot.
            await live.DisposeAsync();
            AssertSubscriberCount(0, "the live view being disposed");
        }
        finally
        {
            await superseded.DisposeAsync();
            await live.DisposeAsync();
        }
    }

    /// <summary>
    /// A superseded view stops redrawing a workspace that will never be on screen again.
    /// </summary>
    /// <remarks>
    /// Being replaced is permanent, and the replaced view's workspace has no way to know it:
    /// left watching, it answers every live-capture progress report by reopening the session
    /// — new segment mappings and a full query set — for a frame with no surface to land on.
    /// On the device this is an orphaned workspace doing that for as long as the process
    /// lives. It is the same suspension the platform asks for when the app is backgrounded,
    /// except that nothing will ever resume this one.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASupersededViewStopsWatchingItsLiveWorkspace()
    {
        var replaced = new MainView();
        try
        {
            Assert.True(replaced.Workspace.IsWatchingLiveViews);

            var live = new MainView();
            try
            {
                Assert.False(replaced.Workspace.IsWatchingLiveViews);
                Assert.True(live.Workspace.IsWatchingLiveViews);

                // And a resume published afterwards reaches only the live workspace, so the
                // superseded one cannot be woken back up by the platform.
                PlatformSourceRegistry.PublishAppResumed();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(replaced.Workspace.IsWatchingLiveViews);
            }
            finally
            {
                await live.DisposeAsync();
            }
        }
        finally
        {
            await replaced.DisposeAsync();
        }
    }

    /// <summary>Publishing reaches the live view and not the one it replaced.</summary>
    [AvaloniaFact]
    public async Task PublishingAfterAHandoverDoesNotReachTheReplacedView()
    {
        var scale = PlatformSourceRegistry.PlatformFontScale;
        var replaced = new MainView();
        var live = new MainView();
        try
        {
            // The abandoned view used to answer this too. Nothing here asserts on the view's
            // internals; the point is that exactly one handler runs, and the counts above say
            // which. This guards the publish path itself against throwing on a view whose
            // host is gone.
            PlatformSourceRegistry.PlatformFontScale = 1.5;
            PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            PlatformSourceRegistry.PublishAppPaused();
            PlatformSourceRegistry.PublishAppResumed();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            AssertSubscriberCount(1, "publishing to a handed-over subscription");
        }
        finally
        {
            PlatformSourceRegistry.PlatformFontScale = scale;
            PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await replaced.DisposeAsync();
            await live.DisposeAsync();
        }
    }

    private static void AssertSubscriberCount(int expected, string after)
    {
        foreach (var name in PlatformEvents)
        {
            Assert.Equal((name, expected), (name, SubscriberCount(name)));
        }

        _ = after;
    }

    private static int SubscriberCount(string eventName)
    {
        var field = typeof(PlatformSourceRegistry).GetField(
                        eventName,
                        BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException(
                        $"PlatformSourceRegistry.{eventName} is no longer a field-like event; this test reads its backing delegate.");
        return ((Delegate?)field.GetValue(null))?.GetInvocationList().Length ?? 0;
    }
}
