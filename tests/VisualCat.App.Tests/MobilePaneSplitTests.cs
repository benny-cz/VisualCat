using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

public sealed class MobilePaneSplitTests
{
    private static readonly double[] OverrideShares = [0.35, 0.55, 0.9];

    private static readonly string[] WorkspaceModeButtons =
    [
        "Show plot workspace",
        "Show details workspace",
        "Show split workspace",
    ];

    private const string FourEntryLog =
        "01-01 00:00:00.000000   100   101 I Worker         : one\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : two\n" +
        "01-01 00:00:20.000000   100   101 E Loader         : three\n" +
        "01-01 00:00:30.000000   100   101 I Worker         : four\n";

    [Fact]
    public void SplitStateRestoresValidValuesAndRejectsInvalidOnes()
    {
        var state = new MobilePaneSplitState();

        Assert.True(state.Restore(0.62));
        Assert.Equal(0.62, state.TimelineShare);
        Assert.True(state.HasUserOverride);

        Assert.True(state.Restore(double.NaN));
        Assert.Null(state.TimelineShare);
        Assert.False(state.HasUserOverride);
        Assert.False(state.Restore(-1));
        Assert.False(state.Restore(1.01));
    }

    [Fact]
    public void AutomaticAllocationProtectsTheTimelineItselfAndPrefersEntryRows()
    {
        var allocation = Resolve(share: null);

        Assert.True(allocation.SplitterEnabled);
        Assert.Equal(MobilePaneSplitter.LaneExtent, allocation.Splitter.Value);
        Assert.Equal(MobilePaneTrackUnit.Star, allocation.Timeline.Unit);
        Assert.Equal(MobilePaneTrackUnit.Star, allocation.Analysis.Unit);
        Assert.Equal(48, allocation.Minimap.Value);

        var resizable = 554 - MobilePaneSplitter.LaneExtent;
        var plotPane = resizable - allocation.AnalysisMaximumHeight;
        Assert.Equal(MobilePaneAllocator.MinimumReadableTimelineHeight + 48, plotPane, 3);
        Assert.True(allocation.AnalysisMinimumHeight <= allocation.AnalysisMaximumHeight);
        Assert.True(allocation.AnalysisMinimumHeight >= 173 + 64);
    }

    [Fact]
    public void UserShareIsAppliedToTheAggregatePlotAndClampedByBothHardFloors()
    {
        var requested = Resolve(0.55);
        var upperExtreme = Resolve(0.90);
        var lowerExtreme = Resolve(0.10);

        Assert.Equal(MobilePaneTrackUnit.Pixel, requested.Analysis.Unit);
        Assert.Equal(0.55, requested.ResolvedTimelineShare, 3);
        Assert.Equal((554 - 12) * 0.45, requested.Analysis.Value, 3);
        Assert.True(upperExtreme.StoredShareWasClamped);
        Assert.Equal(upperExtreme.MaximumTimelineShare, upperExtreme.ResolvedTimelineShare, 6);
        Assert.True(lowerExtreme.StoredShareWasClamped);
        Assert.Equal(lowerExtreme.MinimumTimelineShare, lowerExtreme.ResolvedTimelineShare, 6);
    }

    [Fact]
    public void TemporaryClampNeverMutatesTheStoredPreference()
    {
        var state = new MobilePaneSplitState();
        state.Restore(0.80);

        var allocation = Resolve(state.TimelineShare);

        Assert.True(allocation.StoredShareWasClamped);
        Assert.Equal(0.80, state.TimelineShare);
        Assert.NotEqual(state.TimelineShare, allocation.ResolvedTimelineShare);
    }

    [Fact]
    public void MinimapAppearanceUsesPlotSpaceWithoutChangingTheStoredShare()
    {
        var withMinimap = Resolve(0.55, minimap: 48);
        var withoutMinimap = Resolve(0.55, minimap: 0);

        Assert.Equal(0.55, withMinimap.ResolvedTimelineShare, 3);
        Assert.Equal(0.55, withoutMinimap.ResolvedTimelineShare, 3);
        Assert.Equal(withMinimap.Analysis.Value, withoutMinimap.Analysis.Value, 3);
        Assert.Equal(48, withMinimap.Minimap.Value - withoutMinimap.Minimap.Value, 3);
    }

    [Fact]
    public void InsufficientRoomUsesAStableNoninteractiveFallback()
    {
        var allocation = Resolve(0.70, band: 350);

        Assert.False(allocation.SplitterVisible);
        Assert.False(allocation.SplitterEnabled);
        Assert.Equal(0, allocation.Splitter.Value);
        Assert.Equal(MobilePaneTrackUnit.Star, allocation.Analysis.Unit);
    }

    [Fact]
    public void EveryCompositionHasOneUnambiguousRowOwner()
    {
        var filters = ResolveComposition(MobilePaneComposition.Filters);
        var plot = ResolveComposition(MobilePaneComposition.Plot);
        var details = ResolveComposition(MobilePaneComposition.Details);
        var wide = ResolveComposition(MobilePaneComposition.SplitWide);

        Assert.Equal(MobilePaneTrackUnit.Star, filters.Timeline.Unit);
        Assert.Equal(0, filters.Minimap.Value);
        Assert.Equal(0, filters.Analysis.Value);

        Assert.Equal(MobilePaneTrackUnit.Star, plot.Timeline.Unit);
        Assert.Equal(48, plot.Minimap.Value);
        Assert.Equal(0, plot.Analysis.Value);

        Assert.Equal(0, details.Timeline.Value);
        Assert.Equal(MobilePaneTrackUnit.Star, details.Analysis.Unit);
        Assert.True(details.AnalysisMinimumHeight > 0);

        Assert.Equal(MobilePaneTrackUnit.Star, wide.Timeline.Unit);
        Assert.Equal(0, wide.Minimap.Value);
        Assert.Equal(48, wide.Analysis.Value);
        Assert.All([filters, plot, details, wide], static allocation =>
        {
            Assert.False(allocation.SplitterVisible);
            Assert.False(allocation.SplitterEnabled);
        });
    }

    [Theory]
    [InlineData(350)]
    [InlineData(260)]
    [InlineData(130)]
    public void FallbackProtectsTheRenderingCliffWheneverTheBandCanHoldIt(double band)
    {
        var allocation = Resolve(0.7, band);
        var worstAnalysis = allocation.AnalysisMaximumHeight;
        var timeline = Math.Max(0, band - allocation.Minimap.Value - worstAnalysis);

        if (band >= allocation.Minimap.Value + MobilePaneAllocator.TimelineRenderingFloor)
        {
            Assert.True(
                timeline >= MobilePaneAllocator.TimelineRenderingFloor,
                $"{band:0.#} dp band resolved a {timeline:0.#} dp timeline");
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-50)]
    [InlineData(0)]
    public void InvalidBandMeasurementsAreSafe(double band)
    {
        var allocation = Resolve(0.5, band);

        Assert.False(allocation.SplitterEnabled);
        Assert.True(allocation.Timeline.Value >= 0);
        Assert.True(allocation.Analysis.Value >= 0);
        Assert.True(allocation.AnalysisMinimumHeight >= 0);
    }

    [Fact]
    public void GestureBudgetKeepsTheMinimapAndTrimsTheTimelinesTop()
    {
        const double scale = 3;
        var minimap = new PixelRect(0, 900, 1080, 144);
        var timeline = new PixelRect(0, 258, 1080, 642);

        var limited = EdgeGestureGuard.LimitToBudget([minimap, timeline], scale);

        Assert.Equal(2, limited.Count);
        Assert.Equal(minimap, limited[0]);
        Assert.Equal(600, limited.Sum(static rectangle => rectangle.Height));
        Assert.Equal(timeline.Bottom, limited[1].Bottom);
        Assert.True(limited[1].Y > timeline.Y);
    }

    /// <summary>
    /// The point of the feature, in the geometry that exposed the defect: a Samsung SM-G990B
    /// at its 360 dpi override, 480 x 1040 dp, whose workspace band is about 554 dp.
    /// </summary>
    [Fact]
    public void TheInspectedPhoneCanReachThePreferredTimelineHeight()
    {
        const double preferredTimeline = 214;
        const double band = 554;
        const double minimap = 48;
        const double chrome = 173;
        var allocation = Resolve(share: 0.95, band, minimap);
        var resizable = band - MobilePaneSplitter.LaneExtent;
        var plotPane = resizable * allocation.ResolvedTimelineShare;

        Assert.True(allocation.SplitterEnabled);
        Assert.True(
            plotPane - minimap >= preferredTimeline,
            $"the largest timeline this phone allows is {plotPane - minimap:0.#} dp");
        Assert.True(
            resizable - plotPane >= chrome + 64,
            $"only {resizable - plotPane:0.#} dp was left for the details pane");
    }

    [AvaloniaFact]
    public void AppearanceOffersAVisibleResetRouteOnlyWhenAnOverrideExists()
    {
        var automatic = new AppearanceDialog(new ApplicationSettings());
        var overridden = new AppearanceDialog(new ApplicationSettings(
            MobileTimelineShare: 0.6,
            MobileTimelineWidthShare: 0.4));
        var automaticReset = Named<Button>(automatic, "Reset plot and details split");
        var overriddenReset = Named<Button>(overridden, "Reset plot and details split");

        Assert.False(automaticReset.IsEnabled);
        Assert.True(overriddenReset.IsEnabled);
        Assert.Equal("Reset plot and details split", overriddenReset.Content);

        // A landscape-only override is still an override; the action is the route back for
        // either boundary, and it clears both.
        var landscapeOnly = new AppearanceDialog(new ApplicationSettings(MobileTimelineWidthShare: 0.4));
        Assert.True(Named<Button>(landscapeOnly, "Reset plot and details split").IsEnabled);

        overriddenReset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var apply = overridden.GetLogicalDescendants()
            .OfType<Button>()
            .Single(static button => Equals(button.Content, "Apply"));
        apply.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(overridden.Completion.IsCompletedSuccessfully);
        Assert.Null(overridden.Completion.Result!.MobileTimelineShare);
        Assert.Null(overridden.Completion.Result.MobileTimelineWidthShare);
    }

    [AvaloniaFact]
    public async Task StackedSplitHasAThinLaneAndA48DpAccessibleTarget()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();

            Assert.Equal(7, root.RowDefinitions.Count);
            Assert.True(splitter.IsVisible);
            Assert.True(splitter.IsHitTestVisible);
            Assert.True(splitter.Focusable);
            Assert.True(splitter.Bounds.Height >= MobilePaneSplitter.HitTargetExtent);
            Assert.Equal(MobilePaneSplitter.LaneExtent, root.RowDefinitions[4].ActualHeight, 1);
            Assert.Equal("Resize plot and details", AutomationProperties.GetName(splitter));
            Assert.Contains("Double tap", AutomationProperties.GetHelpText(splitter), StringComparison.Ordinal);
            Assert.Equal(0, timeline.MinHeight);
            Assert.True(
                timeline.Bounds.Height >= MobilePaneAllocator.MinimumReadableTimelineHeight - 1,
                $"timeline was {timeline.Bounds.Height:0.0} dp");
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task DraggingDownEnlargesThePlotAndPersistsOnlyOnCompletion()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            var changes = new List<double?>();
            fixture.View.SplitShareChanged += changes.Add;
            var beforePlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var beforeAnalysis = root.RowDefinitions[5].ActualHeight;
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;

            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);
            fixture.Window.MouseMove(origin.WithY(origin.Y + 80));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Assert.Empty(changes);
            fixture.Window.MouseUp(origin.WithY(origin.Y + 80), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            var afterPlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var afterAnalysis = root.RowDefinitions[5].ActualHeight;
            Assert.True(afterPlot > beforePlot, $"plot stayed at {beforePlot:0.0} -> {afterPlot:0.0}");
            Assert.True(afterAnalysis < beforeAnalysis, $"analysis stayed at {beforeAnalysis:0.0} -> {afterAnalysis:0.0}");
            Assert.Single(changes);
            Assert.NotNull(fixture.View.CurrentMobileTimelineShare);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TheDividerFollowsTheFingerExactlyAcrossManyMoves()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            double Plot() =>
                root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var start = Plot();
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;

            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);
            foreach (var offset in new double[] { 9, 21, 34, 48, 63 })
            {
                fixture.Window.MouseMove(origin.WithY(origin.Y + offset));
                Dispatcher.UIThread.RunJobs();
                fixture.Window.UpdateLayout();
                Assert.Equal(start + offset, Plot(), 1);
            }

            fixture.Window.MouseUp(origin.WithY(origin.Y + 63), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();
            Assert.Equal(start + 63, Plot(), 1);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task ADragHeldAgainstAHardStopComesStraightBackOffIt()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            double Plot() =>
                root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var start = Plot();
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;

            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);

            // Well past the maximum and held there, which is where a delta summed per event
            // used to bank travel the boundary never made and could never give back.
            double? pinned = null;
            foreach (var offset in new double[] { 120, 400, 700, 700, 700 })
            {
                fixture.Window.MouseMove(origin.WithY(origin.Y + offset));
                Dispatcher.UIThread.RunJobs();
                fixture.Window.UpdateLayout();
                pinned ??= Plot();
            }

            var atStop = Plot();
            fixture.Window.MouseMove(origin.WithY(origin.Y + 36));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();
            var afterReturn = Plot();
            fixture.Window.MouseUp(origin.WithY(origin.Y + 36), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Assert.True(atStop > start, "the drag never reached the stop");
            Assert.Equal(start + 36, afterReturn, 1);
            Assert.Equal(afterReturn, Plot(), 1);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TheWholeBoundaryIsGrabbableAndOnlyItsMiddleIsTall()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            double Plot() =>
                root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var centreY = splitter.Bounds.Height / 2;
            var lane = MobilePaneSplitter.LaneBandExtent / 2;
            var grip = MobilePaneSplitter.GripZoneLength / 2;

            // The line runs the full width of the workspace.
            Assert.Equal(root.Bounds.Width, splitter.Bounds.Width, 1);
            Assert.True(splitter.HitTest(new Point(4, centreY)));
            Assert.True(splitter.HitTest(new Point(splitter.Bounds.Width - 4, centreY)));

            // Away from the grip it is only the visible gap, so the minimap above and the
            // tab strip below keep every pixel of their own area.
            Assert.False(splitter.HitTest(new Point(4, centreY - lane - 2)));
            Assert.False(splitter.HitTest(new Point(4, centreY + lane + 2)));

            // The marked grip is where the target reaches its full accessible height.
            Assert.True(splitter.HitTest(new Point(splitter.Bounds.Width / 2, 1)));
            Assert.True(splitter.HitTest(
                new Point((splitter.Bounds.Width / 2) + grip - 1, splitter.Bounds.Height - 1)));
            Assert.True(splitter.Bounds.Height >= MobilePaneSplitter.HitTargetExtent);

            // And a real press at the far edge of the line drags, not just the grip.
            var start = Plot();
            var edge = splitter.TranslatePoint(new Point(8, centreY), fixture.Window)!.Value;
            fixture.Window.MouseMove(edge);
            fixture.Window.MouseDown(edge, MouseButton.Left);
            fixture.Window.MouseMove(edge.WithY(edge.Y + 30));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();
            fixture.Window.MouseUp(edge.WithY(edge.Y + 30), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Assert.Equal(start + 30, Plot(), 1);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task AutomationCanAdjustAndResetTheSplit()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, _) =>
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(splitter);
            var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
            range.SetValue((range.Minimum + range.Maximum) / 2);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(fixture.View.CurrentMobileTimelineShare);
            Assert.InRange(
                fixture.View.CurrentMobileTimelineShare!.Value * 100,
                range.Minimum - 0.1,
                range.Maximum + 0.1);

            fixture.View.ResetMobileTimelineShare();
            Assert.Null(fixture.View.CurrentMobileTimelineShare);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TemporaryCompositionsHideButPreserveTheDividerPreference()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, _) =>
        {
            fixture.View.RestoreMobileTimelineShare(0.55);
            fixture.Window.UpdateLayout();
            var details = Named<Button>(fixture.View, "Show details workspace");
            var split = Named<Button>(fixture.View, "Show split workspace");

            details.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Assert.False(splitter.IsVisible);
            Assert.Equal(0.55, fixture.View.CurrentMobileTimelineShare);

            split.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Assert.True(splitter.IsVisible);
            Assert.Equal(0.55, fixture.View.CurrentMobileTimelineShare);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task PlotFiltersAndWideCompositionsNeverExposeTheDivider()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            fixture.View.RestoreMobileTimelineShare(0.55);
            var plot = Named<Button>(fixture.View, "Show plot workspace");
            var split = Named<Button>(fixture.View, "Show split workspace");
            var filters = Named<Button>(fixture.View, "Open search and timeline filters");
            var drawer = Named<Border>(fixture.View, "Search and timeline filters");

            plot.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Assert.False(splitter.IsVisible);

            split.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Assert.False(splitter.IsVisible);
            Assert.True(drawer.IsVisible);
            Assert.Equal(2, Grid.GetRow(drawer));
            Assert.Equal(4, Grid.GetRowSpan(drawer));
            Assert.Equal(0.55, fixture.View.CurrentMobileTimelineShare);
            Assert.Equal(7, root.RowDefinitions.Count);
            return Task.CompletedTask;
        });

        // Side by side, the stacked divider is the wrong boundary; the column one owns it.
        await UsingPhoneWorkspace(800, 360, static (fixture, splitter, root) =>
        {
            Assert.False(splitter.IsVisible);
            Assert.Equal(3, root.ColumnDefinitions.Count);
            Assert.Equal(0, root.RowDefinitions[4].ActualHeight, 1);
            Assert.True(Splitter(fixture.View, MobilePaneAxis.Columns).IsVisible);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TheLandscapeDividerMovesTheColumnBoundaryAndPersistsOnCompletion()
    {
        await UsingPhoneWorkspace(1040, 480, static (fixture, stacked, root) =>
        {
            var splitter = Splitter(fixture.View, MobilePaneAxis.Columns);
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
            double Plot() => root.ColumnDefinitions[0].ActualWidth;
            var changes = new List<double?>();
            fixture.View.SplitWidthShareChanged += changes.Add;

            Assert.False(stacked.IsVisible);
            Assert.True(splitter.IsVisible);
            Assert.Equal(MobilePaneAxis.Columns, splitter.Axis);
            Assert.Equal(MobilePaneSplitter.LaneExtent, root.ColumnDefinitions[1].ActualWidth, 1);
            Assert.True(splitter.Bounds.Width >= MobilePaneSplitter.HitTargetExtent);
            Assert.Equal(2, Grid.GetColumn(fixture.View.GetLogicalDescendants()
                .OfType<TabControl>()
                .Single(t => AutomationProperties.GetName(t) == "Session detail views")
                .FindAncestorOfType<Grid>()!));

            var start = Plot();
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;
            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);
            foreach (var offset in new double[] { 25, 60, 95 })
            {
                fixture.Window.MouseMove(origin.WithX(origin.X + offset));
                Dispatcher.UIThread.RunJobs();
                fixture.Window.UpdateLayout();
                Assert.Equal(start + offset, Plot(), 1);
            }

            Assert.Empty(changes);
            fixture.Window.MouseUp(origin.WithX(origin.X + 95), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Assert.Single(changes);
            Assert.NotNull(fixture.View.CurrentMobileTimelineWidthShare);
            Assert.Null(fixture.View.CurrentMobileTimelineShare);
            Assert.True(
                timeline.Bounds.Width >= MobilePaneAllocator.TimelineRenderingWidthFloor,
                $"the timeline was {timeline.Bounds.Width:0.#} dp wide");
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task TheLandscapeDividerStopsAtBothColumnMinimumsAndReturns()
    {
        await UsingPhoneWorkspace(1040, 480, static (fixture, _, root) =>
        {
            var splitter = Splitter(fixture.View, MobilePaneAxis.Columns);
            double Plot() => root.ColumnDefinitions[0].ActualWidth;
            double Analysis() => root.ColumnDefinitions[2].ActualWidth;
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;

            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);
            foreach (var offset in new double[] { 400, 900, 900 })
            {
                fixture.Window.MouseMove(origin.WithX(origin.X + offset));
                Dispatcher.UIThread.RunJobs();
                fixture.Window.UpdateLayout();
            }

            Assert.True(
                Analysis() >= MobilePaneAllocator.MinimumUsableAnalysisWidth - 1,
                $"the details column fell to {Analysis():0.#} dp");

            var start = Plot();
            foreach (var offset in new double[] { -900, -1400, -1400 })
            {
                fixture.Window.MouseMove(origin.WithX(origin.X + offset));
                Dispatcher.UIThread.RunJobs();
                fixture.Window.UpdateLayout();
            }

            Assert.True(start > Plot(), "the drag never came back off the far stop");
            Assert.True(
                Plot() >= MobilePaneAllocator.MinimumReadableTimelineWidth - 1,
                $"the plot column fell to {Plot():0.#} dp");
            fixture.Window.MouseUp(origin.WithX(origin.X - 1400), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The narrowest plot the landscape divider can resolve must still draw a heat map, and
    /// its header must drop whole facts rather than run past the plot's right edge.
    /// </summary>
    [AvaloniaFact]
    public async Task TheNarrowestPlotColumnStillDrawsAndItsHeaderStaysInside()
    {
        await UsingPhoneWorkspace(1040, 480, static (fixture, _, root) =>
        {
            var splitter = Splitter(fixture.View, MobilePaneAxis.Columns);
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                fixture.Window)!.Value;

            fixture.Window.MouseMove(origin);
            fixture.Window.MouseDown(origin, MouseButton.Left);
            fixture.Window.MouseMove(origin.WithX(origin.X - 1400));
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();
            fixture.Window.MouseUp(origin.WithX(origin.X - 1400), MouseButton.Left);
            Settle(fixture);

            Assert.True(
                timeline.Bounds.Width >= MobilePaneAllocator.TimelineRenderingWidthFloor,
                $"the plot fell to {timeline.Bounds.Width:0.#} dp and would draw its empty state");
            Assert.True(
                MeasuredHeaderWidth(timeline) <= timeline.Bounds.Width - 88,
                "the plot header was wider than the band it is drawn in");
            return Task.CompletedTask;
        });
    }

    /// <summary>The width of the header the control would draw at its current size.</summary>
    private static double MeasuredHeaderWidth(TimelineControl timeline)
    {
        var method = typeof(TimelineControl).GetMethod(
            "NarrowestHeaderThatFits",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var header = Assert.IsType<string>(
            method.Invoke(timeline, [timeline.Bounds.Width - 88, 1000d]));
        var measure = typeof(TimelineControl).GetMethod(
            "MeasureTextWidth",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(measure);
        return Assert.IsType<double>(measure.Invoke(null, [header, 10d]));
    }

    [AvaloniaFact]
    public async Task TheTwoAxesKeepSeparatePreferences()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            fixture.View.RestoreMobileTimelineShare(0.52);
            fixture.View.RestoreMobileTimelineWidthShare(0.60);
            Settle(fixture);

            // Portrait applies the height share and holds the width share untouched.
            Assert.True(splitter.IsVisible);
            Assert.False(Splitter(fixture.View, MobilePaneAxis.Columns).IsVisible);
            var stackedPlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;

            fixture.Window.Width = 1040;
            fixture.Window.Height = 480;
            Settle(fixture);
            var wide = Splitter(fixture.View, MobilePaneAxis.Columns);
            var resizable = root.ColumnDefinitions[0].ActualWidth + root.ColumnDefinitions[2].ActualWidth;

            Assert.True(wide.IsVisible);
            Assert.False(splitter.IsVisible);
            Assert.Equal(0.60, root.ColumnDefinitions[0].ActualWidth / resizable, 2);
            Assert.Equal(0.52, fixture.View.CurrentMobileTimelineShare);

            fixture.Window.Width = 480;
            fixture.Window.Height = 1040;
            Settle(fixture);

            Assert.Equal(0.60, fixture.View.CurrentMobileTimelineWidthShare);
            Assert.Equal(
                stackedPlot,
                root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight,
                1);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task HomeRestoresAutomaticSizingAndReportsExactlyOneReset()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, _) =>
        {
            fixture.View.RestoreMobileTimelineShare(0.55);
            var changes = new List<double?>();
            fixture.View.SplitShareChanged += changes.Add;
            splitter.Focus();

            fixture.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Assert.Null(fixture.View.CurrentMobileTimelineShare);
            Assert.Equal([null], changes);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task ResetFromAnUnrealizedAnalysisTabUsesTheLastValidEntriesChrome()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, _, root) =>
        {
            var automaticPlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var automaticAnalysis = root.RowDefinitions[5].ActualHeight;
            fixture.View.RestoreMobileTimelineShare(0.55);
            fixture.Window.UpdateLayout();

            var tabs = Named<TabControl>(fixture.View, "Session detail views");
            tabs.SelectedIndex = 1;
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            fixture.View.ResetMobileTimelineShare();
            for (var pass = 0; pass < 3; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var resetPlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            var resetAnalysis = root.RowDefinitions[5].ActualHeight;
            Assert.Equal(automaticPlot, resetPlot, 1);
            Assert.Equal(automaticAnalysis, resetAnalysis, 1);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task RotatingAwayFromStackedSplitAndBackRestoresTheChosenShare()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            fixture.View.RestoreMobileTimelineShare(0.52);
            Settle(fixture);
            var stackedPlot = root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight;
            Assert.True(splitter.IsVisible);

            // The wide landscape composition puts the panes side by side, where a height
            // share has no meaning; it must be kept rather than recalculated or discarded.
            fixture.Window.Width = 800;
            fixture.Window.Height = 360;
            Settle(fixture);
            Assert.False(splitter.IsVisible);
            Assert.Equal(0.52, fixture.View.CurrentMobileTimelineShare);
            Assert.Equal(0, root.RowDefinitions[4].ActualHeight, 1);

            fixture.Window.Width = 480;
            fixture.Window.Height = 1040;
            Settle(fixture);

            Assert.True(splitter.IsVisible);
            Assert.Equal(0.52, fixture.View.CurrentMobileTimelineShare);
            Assert.Equal(
                stackedPlot,
                root.RowDefinitions[2].ActualHeight + root.RowDefinitions[3].ActualHeight,
                1);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Finding 18: a minimum on a star-sized row cannot enlarge the share the grid hands the
    /// control, it only makes the control arrange itself taller than its cell, and the axis
    /// labels at its bottom are then drawn under the minimap. The timeline's floor lives in
    /// the allocator, and it has to stay there while an override is in force too.
    /// </summary>
    [AvaloniaFact]
    public async Task NoCompositionEverGivesTheTimelineAMinimumHeight()
    {
        await UsingPhoneWorkspace(480, 1040, static (fixture, splitter, root) =>
        {
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
            Assert.Equal(0, timeline.MinHeight);

            foreach (var share in OverrideShares)
            {
                fixture.View.RestoreMobileTimelineShare(share);
                Settle(fixture);
                Assert.Equal(0, timeline.MinHeight);
                Assert.Equal(0, root.RowDefinitions[2].MinHeight);
                Assert.Equal(GridUnitType.Star, root.RowDefinitions[2].Height.GridUnitType);
                Assert.True(
                    timeline.Bounds.Height >= MobilePaneAllocator.TimelineRenderingFloor,
                    $"share {share} drew a {timeline.Bounds.Height:0.#} dp timeline");
            }

            foreach (var mode in WorkspaceModeButtons)
            {
                Named<Button>(fixture.View, mode)
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Settle(fixture);
                Assert.Equal(0, timeline.MinHeight);
            }

            Assert.Equal(7, root.RowDefinitions.Count);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The desktop workspace keeps its own two splitters and never grows the phone divider.
    /// </summary>
    [AvaloniaFact]
    public async Task DesktopKeepsItsOwnSplittersAndHasNoPhoneDivider()
    {
        SessionWorkspaceView.PhoneCompositionOverride = false;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var root = Assert.IsType<Grid>(fixture.View.Content);
            Assert.Empty(fixture.View.GetLogicalDescendants().OfType<MobilePaneSplitter>());
            Assert.Equal(2, fixture.View.GetLogicalDescendants().OfType<GridSplitter>().Count());
            Assert.Contains(
                fixture.View.GetLogicalDescendants().OfType<GridSplitter>(),
                splitter => Grid.GetRow(splitter) == 4);
            Assert.Equal(7, root.RowDefinitions.Count);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// A failed session hides the workspace band. A focusable, automation-visible divider
    /// left inside it is a control a screen reader can reach and nothing can move.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailedSessionLeavesNoReachableDivider()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            var sourcePath = Path.Combine(root, "not-a-logcat.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                "the quick brown fox\njumped over\nthe lazy dog\n",
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            SessionTabViewModel? tab = null;
            workspace.TabAdded += (_, added) => tab = added;
            await Assert.ThrowsAnyAsync<Exception>(() =>
                workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken));
            Assert.NotNull(tab);

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 480, Height = 1040 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                foreach (var splitter in view.GetLogicalDescendants().OfType<MobilePaneSplitter>())
                {
                    Assert.False(splitter.IsVisible);
                    Assert.False(splitter.IsEffectivelyVisible);
                    Assert.False(splitter.Focusable);
                    Assert.False(splitter.IsHitTestVisible);
                    Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(splitter));
                }

                var unused = Splitter(view, MobilePaneAxis.Rows);
                Assert.False(unused.IsVisible);
            }
            finally
            {
                window.Close();
            }

            await workspace.CloseAsync(tab);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public async Task RenderWidthDeadBandMakesVerticalLayoutChangesQueryFree()
    {
        await UsingPhoneWorkspace(480, 1040, static async (fixture, _, _) =>
        {
            var field = typeof(VisualCat.App.Presentation.SessionTabViewModel).GetField(
                "_renderWidth",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);
            var width = Assert.IsType<int>(field.GetValue(fixture.Tab));
            var heatMap = fixture.Tab.HeatMap;

            await fixture.Tab.SetRenderWidthAsync(width + 23);

            Assert.Same(heatMap, fixture.Tab.HeatMap);
            Assert.Equal(width, Assert.IsType<int>(field.GetValue(fixture.Tab)));
        });
    }

    private static MobilePaneAllocation Resolve(
        double? share,
        double band = 554,
        double minimap = 48) =>
        MobilePaneAllocator.Resolve(new MobilePaneAllocationRequest(
            MobilePaneComposition.SplitStacked,
            AvailableBandHeight: band,
            TimelineWeight: 2,
            AnalysisWeight: 3,
            MinimapHeight: minimap,
            SplitterLaneHeight: MobilePaneSplitter.LaneExtent,
            AnalysisChromeHeight: 173,
            EntryRowHeight: 64,
            PreferredEntryRows: 4,
            ManualEntryRows: 1,
            TimelineShare: share));

    private static MobilePaneAllocation ResolveComposition(MobilePaneComposition composition) =>
        MobilePaneAllocator.Resolve(new MobilePaneAllocationRequest(
            composition,
            AvailableBandHeight: 700,
            TimelineWeight: 2,
            AnalysisWeight: 3,
            MinimapHeight: 48,
            SplitterLaneHeight: MobilePaneSplitter.LaneExtent,
            AnalysisChromeHeight: 173,
            EntryRowHeight: 64,
            PreferredEntryRows: 4,
            ManualEntryRows: 1,
            TimelineShare: 0.55));

    private static async Task UsingPhoneWorkspace(
        double width,
        double height,
        Func<LiveTestWorkspaceFixture, MobilePaneSplitter, Grid, Task> body)
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog, width, height);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var splitter = Splitter(fixture.View, MobilePaneAxis.Rows);
            var root = Assert.IsType<Grid>(fixture.View.Content);
            await body(fixture, splitter, root);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    private static void Settle(LiveTestWorkspaceFixture fixture)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static MobilePaneSplitter Splitter(Visual root, MobilePaneAxis axis) =>
        root.GetLogicalDescendants().OfType<MobilePaneSplitter>().Single(s => s.Axis == axis);

    private static T Named<T>(Visual root, string name)
        where T : Visual =>
        root.GetLogicalDescendants()
            .OfType<T>()
            .First(control => AutomationProperties.GetName(control) == name);
}
