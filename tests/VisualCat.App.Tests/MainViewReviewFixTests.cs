using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// The shell-level fixes from the Android UX review: how secondary commands are presented, and
/// how open sessions are listed.
/// </summary>
public sealed class MainViewReviewFixTests
{
    /// <summary>
    /// The command menu was a flyout, and a flyout is a popup: with it open and its items
    /// plainly on screen, an accessibility dump contained none of them, so with a screen reader
    /// every command except Open log and Live was unreachable (finding 8). A sheet is ordinary
    /// content — every command is a named control in the tree, and one that cannot run says so
    /// instead of being tappable and silent (finding 19).
    /// </summary>
    [AvaloniaFact]
    public async Task EverySecondaryCommandIsANamedControlInTheTree()
    {
        await using var view = new MainView();

        view.OpenCommandSheet();

        var commands = view.GetLogicalDescendants()
            .OfType<Button>()
            .Where(static button => button.Content is StackPanel)
            .ToDictionary(
                static button => AutomationProperties.GetName(button) ?? string.Empty,
                static button => button,
                StringComparer.Ordinal);

        Assert.Contains("Recent sessions…", commands.Keys);
        Assert.Contains("Open portable archive…", commands.Keys);
        Assert.Contains("Export CSV…", commands.Keys);
        Assert.Contains("Appearance & timeline…", commands.Keys);
        Assert.Contains("Session cache…", commands.Keys);
        Assert.Contains("Diagnostic bundle…", commands.Keys);

        // No session is open, so the commands that need one are disabled and explain why.
        Assert.False(commands["Export CSV…"].IsEnabled);
        Assert.Contains(
            "needs an open session",
            AutomationProperties.GetHelpText(commands["Export CSV…"]),
            StringComparison.OrdinalIgnoreCase);

        // Opening a log never needs a session, so it is never disabled.
        Assert.True(commands["Recent sessions…"].IsEnabled);
    }

    /// <summary>
    /// The sheet closes on the system Back gesture and on its own Close, because it is content
    /// rather than a popup (finding 20).
    /// </summary>
    [AvaloniaFact]
    public async Task TheCommandSheetClosesOnItsOwnAffordance()
    {
        await using var view = new MainView();

        view.OpenCommandSheet();
        var close = view.GetLogicalDescendants()
            .OfType<Button>()
            .Single(static button => AutomationProperties.GetName(button) == "Close this sheet");

        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.DoesNotContain(
            view.GetLogicalDescendants().OfType<Button>(),
            static button => AutomationProperties.GetName(button) == "Close this sheet");
    }

    /// <summary>
    /// The built-in tab strip lays its items out in a wrap panel and each item takes the whole
    /// width, so three open sessions became three full-width rows (finding 22). It is out of the
    /// layout; the product's own strip is one scrolling row.
    /// </summary>
    [AvaloniaFact]
    public async Task TheBuiltInTabStripIsOutOfTheLayout()
    {
        await using var view = new MainView();
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            var host = view.GetVisualDescendants()
                .OfType<TabControl>()
                .Single(static tabs => tabs.Classes.Contains("sessionHost"));
            var strip = host.GetVisualDescendants().OfType<ItemsPresenter>().Single();

            Assert.False(strip.IsVisible);

            // And the replacement is in the tree, ready for the first session.
            Assert.Contains(
                view.GetLogicalDescendants().OfType<ScrollViewer>(),
                static scroller => AutomationProperties.GetName(scroller) == "Open sessions");
        }
        finally
        {
            window.Close();
        }
    }
}
