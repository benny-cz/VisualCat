using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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

    /// <summary>
    /// Every phone control that resolves its <em>own</em> width from its content reserves more
    /// than the bare 48 dp floor.
    /// </summary>
    /// <remarks>
    /// F-48 measured a severity chip at 47.6 dp on a Pixel and fixed that one control with a
    /// literal <c>+ 1</c>. The arithmetic it wrote down is general — Android rounds a node's
    /// two edges to physical pixels independently, so any self-sized control at a fractional
    /// origin can lose a pixel from each end — and a later Samsung pass duly measured the
    /// time-lens <c>Zoom in</c> at 47.6 dp while its neighbour <c>Zoom out</c>, which happened
    /// to start on a whole pixel, measured 48.0. This walks the whole family instead of the
    /// control that was measured most recently, so the next one added is caught here rather
    /// than on a device.
    /// </remarks>
    [AvaloniaFact]
    public async Task PhoneSelfSizedTouchTargetsAllReserveForPlatformEdgeRounding()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            const string log = "01-01 00:00:00.000000   100   101 I Worker         : message\n";
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(log, width: 393, height: 851);
            fixture.Window.UpdateLayout();

            var filters = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Open search and timeline filters");
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            // Named rather than discovered, because the contract is "this control sizes itself
            // to the floor", which no property exposes: a stretched control legitimately
            // reports the floor and takes its container's edges.
            string[] selfSized = ["Zoom out", "Zoom in", "Pan source left by one page", "Pan source right by one page"];
            var reserved = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Where(button => selfSized.Contains(AutomationProperties.GetName(button)))
                .ToArray();

            Assert.Equal(selfSized.Length, reserved.Length);
            Assert.All(
                reserved,
                button =>
                {
                    // Some of the family pin an exact Width and some a MinWidth; what the
                    // contract is about is the width the control resolves for itself, which is
                    // whichever of the two it actually states.
                    var reserve = double.IsNaN(button.Width) ? button.MinWidth : button.Width;
                    Assert.True(
                        reserve >= TouchTarget.MinimumWithEdgeReserve,
                        $"{AutomationProperties.GetName(button)} reserves only {reserve:0.#} dp");
                });
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    [AvaloniaFact]
    public async Task PhoneSeverityFilterTargetsReserveForPlatformEdgeRounding()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            const string log = "01-01 00:00:00.000000   100   101 I Worker         : message\n";
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(log, width: 393, height: 851);
            fixture.Window.UpdateLayout();

            var filters = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Open search and timeline filters");
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            var toggles = fixture.View.GetLogicalDescendants()
                .OfType<ToggleButton>()
                .Where(toggle => AutomationProperties.GetName(toggle)?.EndsWith(" level", StringComparison.Ordinal) == true)
                .Distinct()
                .ToArray();

            Assert.Equal(7, toggles.Length);
            Assert.All(
                toggles,
                toggle => Assert.True(
                    toggle.Width >= 49,
                    $"{AutomationProperties.GetName(toggle)} reserves only {toggle.Width:0.#} dp"));
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    [AvaloniaFact]
    public async Task HomeHeroHeadingCanWrapInsteadOfClippingAtLargeText()
    {
        await using var view = new MainView();
        var heading = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(static block => block.Text == "SEE THE SHAPE OF YOUR LOG");

        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, heading.TextWrapping);
        Assert.Equal(Avalonia.Media.TextAlignment.Center, heading.TextAlignment);
    }

    [AvaloniaFact]
    public async Task HomeHeroCanScrollWhenLargeTextExceedsAShortLandscapeViewport()
    {
        var platform = TextScale.Platform;
        try
        {
            // The viewport F-46 is about: a Samsung in landscape at a text size that makes the
            // hero taller than the screen it has to fit in.
            TextScale.Platform = 2.0;
            await using var view = new MainView();
            var window = new Window { Content = view, Width = 393, Height = 330 };
            window.Show();
            for (var pass = 0; pass < 4; pass++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            try
            {
                var heading = view.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Single(static block => block.Text == "SEE THE SHAPE OF YOUR LOG");
                var scroller = heading.GetLogicalAncestors().OfType<ScrollViewer>().Single();

                Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);

                // The behaviour, not the property. VerticalContentAlignment was asserted here
                // and never did anything: a ScrollViewer's presenter measures its child against
                // infinity and arranges it from the top, so the hero was top-aligned in a tall
                // viewport (V2-02) while still being genuinely scrollable in a short one. The
                // centring is done by a host that is never shorter than the viewport, and this
                // is the half of it F-46 was written for.
                Assert.True(
                    scroller.Extent.Height > scroller.Viewport.Height + 0.5,
                    $"hero should scroll: extent {scroller.Extent.Height}, viewport {scroller.Viewport.Height}");

                // Nothing above the first line, so the top of the hero is reachable.
                Assert.Equal(0, scroller.Offset.Y, 3);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            TextScale.Platform = platform;
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
        Assert.Equal(6, host.Padding.Top);
        Assert.Equal(6, host.Padding.Bottom);

        // The lane opens at two lines and discloses the rest, rather than spending four lines
        // of the workspace on a scroll container that still cuts the message off (V2-11). The
        // full 108 dp budget is what expanding reaches, not what the lane costs by default.
        Assert.True(
            scroller.MaxHeight < 108,
            $"the lane should open collapsed, not at {scroller.MaxHeight}");

        var expand = view.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) is
                "Show the whole message" or "Show less of the message");
        expand.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(
            scroller.MaxHeight >= 108,
            $"the expanded lane should be at least the old budget, not {scroller.MaxHeight}");

        // Every word is still in the tree throughout, which is what a screen reader reads and
        // what F-33 was written to protect.
        Assert.Equal(message, text.Text);
        Assert.Equal(0, text.MaxLines);

        // A new message starts collapsed again; carrying the expansion over would give a
        // one-line confirmation a four-line lane.
        view.ShowNotice("Copied the raw text of 1 entry.", MainView.NoticeKind.Information);
        Assert.True(scroller.MaxHeight < 108);
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
