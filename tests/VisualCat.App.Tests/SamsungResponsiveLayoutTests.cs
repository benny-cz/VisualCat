using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using VisualCat.App.Platform;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// Regression contracts discovered on a 480-dpi Samsung phone, where naturally wrapping
/// controls had different break points from the 440-dpi reference device.
/// </summary>
public sealed class SamsungResponsiveLayoutTests
{
    [AvaloniaFact]
    public void PhoneSeverityLegendUsesTwoBalancedRows()
    {
        var legend = Assert.IsType<Grid>(MainView.BuildSeverityLegend(dark: true, mobile: true));
        var chips = legend.Children.OfType<Border>().ToArray();

        Assert.Equal(6, chips.Length);
        Assert.Equal(3, legend.ColumnDefinitions.Count);
        Assert.Equal(2, legend.RowDefinitions.Count);
        Assert.Equal(
            ["FATAL", "ERROR", "WARN", "INFO", "DEBUG", "VERBOSE"],
            chips.Select(static chip => Assert.IsType<TextBlock>(chip.Child).Text ?? string.Empty).ToArray());

        for (var index = 0; index < chips.Length; index++)
        {
            Assert.Equal(index % 3, Grid.GetColumn(chips[index]));
            Assert.Equal(index / 3, Grid.GetRow(chips[index]));
        }
    }

    [AvaloniaFact]
    public async Task PhoneHeroActionsHaveAnIntentionalTwoPlusOneLayout()
    {
        var previous = PlatformSourceRegistry.CreateOnDeviceSource;
        try
        {
            // The factory's presence is the capability flag used by the shell. It need not
            // create a transport for this layout-only contract.
            PlatformSourceRegistry.CreateOnDeviceSource = static () => null;
            await using var view = new MainView();
            var actions = Assert.IsType<Grid>(view.BuildHeroActions(dark: true, mobile: true));
            var buttons = actions.Children.OfType<Button>().ToArray();

            Assert.Equal(3, buttons.Length);
            Assert.Equal((0, 0, 1), (Grid.GetRow(buttons[0]), Grid.GetColumn(buttons[0]), Grid.GetColumnSpan(buttons[0])));
            Assert.Equal((0, 1, 1), (Grid.GetRow(buttons[1]), Grid.GetColumn(buttons[1]), Grid.GetColumnSpan(buttons[1])));
            Assert.Equal((1, 0, 2), (Grid.GetRow(buttons[2]), Grid.GetColumn(buttons[2]), Grid.GetColumnSpan(buttons[2])));
        }
        finally
        {
            PlatformSourceRegistry.CreateOnDeviceSource = previous;
        }
    }

    [AvaloniaFact]
    public async Task CompactHeightNoticePreservesWorkspaceHeightWithoutLosingItsMessage()
    {
        await using var view = new MainView();
        const string message = "Important result.\n\nThe complete explanation remains available here.";
        view.ShowNotice(message, MainView.NoticeKind.Failure);
        view.ApplyNoticeLayout(compactHeight: true);

        var text = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(block => AutomationProperties.GetName(block) == "Application status message");
        var scroller = text.GetLogicalAncestors().OfType<ScrollViewer>().Single();
        var host = view.GetLogicalDescendants()
            .OfType<Border>()
            .Single(border => AutomationProperties.GetName(border) == "Application status");

        Assert.Equal(message, text.Text);
        Assert.Equal(0, text.MaxLines);
        Assert.Equal(48, scroller.MaxHeight);
        Assert.Equal(1, host.BorderThickness.Bottom);
        Assert.Equal(0, host.Padding.Top);
        Assert.Equal(0, host.Padding.Bottom);

        view.ApplyNoticeLayout(compactHeight: false);
        Assert.Equal(108, scroller.MaxHeight);
        Assert.Equal(6, host.Padding.Top);
        Assert.Equal(6, host.Padding.Bottom);
    }

    [AvaloniaFact]
    public async Task PhoneDialogSheetsKeepACompleteVisibleFrame()
    {
        await using var view = new MainView();
        var dialog = new ConfirmationDialog("Complete popup", "The frame must remain visible.", "Continue");
        var presented = view.ShowDialogAsync(dialog);

        var panel = view.GetLogicalDescendants()
            .OfType<Border>()
            .Single(border => AutomationProperties.GetName(border) == "Complete popup");

        Assert.Equal(new Thickness(1), panel.BorderThickness);
        Assert.Equal(new CornerRadius(16), panel.CornerRadius);
        Assert.True(panel.Margin.Left > 0);
        Assert.True(panel.Margin.Right > 0);
        Assert.True(panel.Margin.Bottom > 0);

        dialog.Dismiss();
        await presented;
    }

    [AvaloniaFact]
    public async Task CompactEntryInspectorOwnsItsScrollAndClipBoundaries()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            const string log = "01-01 00:00:00.000000   100   101 I Worker         : selected message\n";
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(log, width: 780, height: 164);
            fixture.Window.UpdateLayout();

            var entries = fixture.View.GetVisualDescendants()
                .OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "Filtered log entries");
            entries.SelectedIndex = 0;
            var tabs = fixture.View.GetVisualDescendants()
                .OfType<TabControl>()
                .Single(control => AutomationProperties.GetName(control) == "Session detail views");
            tabs.SelectedIndex = 2;
            fixture.Window.UpdateLayout();

            var inspector = fixture.View.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => AutomationProperties.GetName(border) == "Selected entry inspector");
            var copyMessage = fixture.View.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Copy the whole message");

            Assert.True(inspector.ClipToBounds);
            Assert.Contains(
                copyMessage.GetVisualAncestors().OfType<ScrollViewer>(),
                static scroller => scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                                   scroller.ClipToBounds);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    [AvaloniaFact]
    public async Task LargeAndroidTextKeepsEntryActionsInsideANarrowPhonePane()
    {
        var platformScale = TextScale.Platform;
        var userScale = TextScale.User;
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            TextScale.Platform = 1.3;
            TextScale.User = 1;
            const string log = "01-01 00:00:00.000000   100   101 I Worker         : selected message\n";
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(log, width: 360, height: 780);
            fixture.Window.UpdateLayout();

            var copy = fixture.View.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ToolTip.GetTip(button) as string == "Copy the raw text of the selected entries");
            var inspector = fixture.View.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Show the full message of the selected entry");
            var primaryActions = copy.GetVisualAncestors().OfType<Grid>().First();
            var detailsMode = fixture.View.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Show details workspace");

            Assert.Equal("Copy", copy.Content);
            Assert.Equal("Entry", inspector.Content);
            Assert.Equal("Logs", detailsMode.Content);
            Assert.True(
                Math.Abs(copy.Bounds.Width - inspector.Bounds.Width) <= 0.5,
                $"Copy and Entry should share the remaining row: {copy.Bounds} / {inspector.Bounds}");
            Assert.True(
                copy.Bounds.Left >= 0 && copy.Bounds.Right <= primaryActions.Bounds.Width + 0.5,
                $"Copy {copy.Bounds} does not fit primary actions {primaryActions.Bounds}");
            Assert.True(
                inspector.Bounds.Left >= 0 && inspector.Bounds.Right <= primaryActions.Bounds.Width + 0.5,
                $"Entry {inspector.Bounds} does not fit primary actions {primaryActions.Bounds}");
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            TextScale.Platform = platformScale;
            TextScale.User = userScale;
        }
    }
}
