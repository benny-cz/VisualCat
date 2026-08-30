using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// The three defects the Pixel 5 audit found — a gesture-navigation phone at API 34, which is
/// the configuration §§24–26 of the live-test report never ran on.
/// </summary>
/// <remarks>
/// A-02 and A-03 are behaviours a headless build can hold still exactly: which rectangles the
/// app names to the platform, and whether a changed text scale reaches the views that resolved
/// their sizes from it. A-04 is arithmetic over a measured width. Each of these fails on the
/// implementation that shipped in 2.0.10.
/// </remarks>
public sealed class PixelGestureAndTextScaleTests
{
    private const string FourEntryLog =
        "01-01 00:00:00.000000   100   101 I Worker         : one\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : two\n" +
        "01-01 00:00:20.000000   100   101 E Loader         : three\n" +
        "01-01 00:00:30.000000   100   101 I Worker         : four\n";

    // ---------------------------------------------------------------- A-02 ---

    /// <summary>
    /// A-02 — the stacked divider claims its grab band from the platform's edge gesture, and
    /// the side-by-side divider claims nothing.
    /// </summary>
    /// <remarks>
    /// On the Pixel the system's own strips are 82 px — 29.8 dp — wide and the divider reached
    /// 17.8 dp into each of them, so a drag begun near either end went Back and left the app
    /// for the launcher. The claim is deliberately the 20 dp band and not the 48 dp target:
    /// away from the centred grip that band is all <c>HitTest</c> answers, and the plot pays
    /// for anything wider out of the same 200 dp budget.
    /// </remarks>
    [AvaloniaFact]
    public void TheStackedDividerClaimsItsGrabBandAndTheColumnOneClaimsNothing()
    {
        var rows = new MobilePaneSplitter(MobilePaneAxis.Rows);
        var columns = new MobilePaneSplitter(MobilePaneAxis.Columns);
        var host = new Window
        {
            Width = 400,
            Height = 900,
            Content = new Grid { Children = { rows } },
        };
        host.Show();
        host.UpdateLayout();
        try
        {
            var claim = ((IEdgeGestureSurface)rows).EdgeGestureArea;

            Assert.True(((IEdgeGestureSurface)rows).ClaimedWhole);
            Assert.Equal(rows.Bounds.Width, claim.Width, 3);
            Assert.Equal(MobilePaneSplitter.LaneBandExtent, claim.Height, 3);
            Assert.Equal(MobilePaneSplitter.HitTargetExtent, rows.Bounds.Height, 3);

            // Centred on the boundary the divider draws, so the band the reader can grab and
            // the band the platform is told about are the same band.
            Assert.Equal(rows.Bounds.Height / 2, claim.Y + (claim.Height / 2), 3);

            // Costed against the budget the plot and the minimap are already spending: a fifth
            // of it, not half.
            Assert.True(claim.Height < MobilePaneSplitter.HitTargetExtent / 2);

            // The allocator never resolves the column divider closer than 220 dp to one edge
            // or 300 dp to the other, so it cannot reach a strip about 30 dp wide — and a
            // full-width claim as tall as the pane would take Back away from the whole
            // workspace to protect nothing.
            Assert.Equal(default, ((IEdgeGestureSurface)columns).EdgeGestureArea);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// A-02 — the divider's band is published to the platform alongside the plot and the
    /// minimap, on the Pixel's own 393 × 777 dp portrait configuration.
    /// </summary>
    [AvaloniaFact]
    public async Task TheDividerBandIsPublishedAsAnExclusionRectangle()
    {
        var published = new List<IReadOnlyList<PixelRect>>();
        PlatformSourceRegistry.SetGestureExclusions = rectangles => published.Add(rectangles);
        SessionWorkspaceView.PhoneCompositionOverride = true;

        // The guard's registry is static and this assembly runs one renderer for every test,
        // so a workspace an earlier test never closed is still tracked and still spending the
        // 200 dp budget this one is measuring. Start from nothing; the finally clears it again.
        EdgeGestureGuard.Reset();
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 393,
                height: 777);
            // The guard publishes from a Background-priority job after a layout pass, and the
            // fixture's own import settles asynchronously, so the first published list is not
            // necessarily the settled one.
            PumpUntil(fixture.Window, () => published.Count > 0 && published[^1].Count == 2);

            var splitter = fixture.View.GetLogicalDescendants()
                .OfType<MobilePaneSplitter>()
                .Single(static control => control.Axis == MobilePaneAxis.Rows);
            Assert.True(splitter.IsEffectivelyVisible);

            // At Fit the plot deliberately claims nothing: the viewport already spans the
            // session, so a pan cannot move it and the claim would take system Back away for
            // no gesture in return (V2-21). The two small whole-claim surfaces still hold
            // theirs, because an edge drag is the whole point of both.
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
            Assert.Equal(default, ((IEdgeGestureSurface)timeline).EdgeGestureArea);
            Assert.Equal(2, published[^1].Count);

            // Zoomed in, a pan is real and the plot takes its band back.
            timeline.ZoomAtCenter(0.25);
            PumpUntil(
                fixture.Window,
                () => ((IEdgeGestureSurface)timeline).EdgeGestureArea != default && published[^1].Count == 3);

            Assert.NotEqual(default, ((IEdgeGestureSurface)timeline).EdgeGestureArea);

            var scale = fixture.Window.RenderScaling <= 0 ? 1 : fixture.Window.RenderScaling;
            var claim = ((IEdgeGestureSurface)splitter).EdgeGestureArea;
            var bandTop = splitter.TranslatePoint(claim.TopLeft, fixture.Window)!.Value.Y;
            var expectedTop = (int)Math.Floor(bandTop * scale);
            var expectedBottom = (int)Math.Ceiling((bandTop + claim.Height) * scale);

            var claimed = published[^1];
            Assert.Equal(3, claimed.Count);
            Assert.Contains(
                claimed,
                rectangle => rectangle.Y == expectedTop && rectangle.Bottom == expectedBottom);

            // Every claim still runs the full width of the window, because the edge strips are
            // on both sides and width costs nothing against Android's per-edge budget.
            Assert.All(claimed, rectangle =>
            {
                Assert.Equal(0, rectangle.X);
                Assert.Equal((int)Math.Ceiling(fixture.Window.Bounds.Width * scale), rectangle.Right);
            });

            Assert.True(
                claimed.Sum(static rectangle => rectangle.Height) <=
                Math.Floor(EdgeGestureGuard.MaximumExclusionHeightDp * scale));
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            EdgeGestureGuard.Reset();
            PlatformSourceRegistry.SetGestureExclusions = null;
        }
    }

    /// <summary>
    /// Pumps the dispatcher and the layout until <paramref name="settled"/> holds.
    /// </summary>
    /// <remarks>
    /// A zoom is asynchronous: SetViewportAsync runs a query off the dispatcher and the result
    /// arrives as a property change marshalled back to it, which then invalidates layout, which
    /// is what makes EdgeGestureGuard recompute. A fixed number of passes is a guess about how
    /// long that takes, and a wrong guess reads the guard's previous answer.
    /// </remarks>
    internal static void PumpUntil(Window window, Func<bool> settled, int passes = 60)
    {
        var consecutive = 0;
        for (var pass = 0; pass < passes; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // RunJobs drains the dispatcher; it does not wait for the thread-pool work the
            // view model queues. A millisecond of real time is what lets a query finish and
            // post its result back, and it is what makes this a wait rather than a spin.
            System.Threading.Thread.Sleep(1);
            Dispatcher.UIThread.RunJobs();

            // Twice in a row. One satisfied pass can be the middle of a recompute — the guard
            // publishes from a Background-priority job, so a condition that holds now can be
            // replaced by the next queued publication before the assertions read it.
            consecutive = settled() ? consecutive + 1 : 0;
            if (consecutive >= 2)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A-02 — when the budget binds, the small claims that must be whole are served first and
    /// the timeline is the one that is trimmed.
    /// </summary>
    [Fact]
    public void TheBudgetKeepsTheSmallClaimsWholeAndTrimsTheTimeline()
    {
        const double scale = 2.75;
        var band = new PixelRect(0, 1200, 1080, 55);
        var minimap = new PixelRect(0, 1110, 1080, 88);
        var timeline = new PixelRect(0, 733, 1080, 363);

        // Preferred-first is the caller's ordering; this is what the budget then does with it.
        var limited = EdgeGestureGuard.LimitToBudget([band, minimap, timeline], scale);

        Assert.Equal(3, limited.Count);
        Assert.Equal(band, limited[0]);
        Assert.Equal(minimap, limited[1]);
        Assert.Equal(timeline.Bottom, limited[2].Bottom);
        Assert.True(
            limited.Sum(static rectangle => rectangle.Height) <=
            Math.Floor(EdgeGestureGuard.MaximumExclusionHeightDp * scale));
    }

    // ---------------------------------------------------------------- A-03 ---

    /// <summary>
    /// A-03 — the reader's own <em>Text scale</em> reaches the open session, not only the
    /// chrome around it.
    /// </summary>
    /// <remarks>
    /// Two controls change the same number. Android's own text-size setting arrives as a
    /// configuration change and rebuilt every workspace at the new scale; More → Appearance
    /// &amp; timeline → Text scale did not, so on the device the command bar, the tab titles
    /// and the status line grew by 25% and every control in the log — the mode strip, the
    /// analysis tabs, the sort control, the entry rows — stayed at exactly the size they were.
    /// The decision now lives in <c>ApplyAppearance</c>, where both routes pass through it.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheReadersOwnTextScaleReachesTheOpenSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, "session.txt");
        await File.WriteAllTextAsync(logPath, FourEntryLog, TestContext.Current.CancellationToken);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(directory);
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            // The view owns an open session under this directory, so it is disposed inside the
            // body and the directory only goes afterwards. An `await using` at method scope
            // would run the other way round and leave a live workspace reading a store that no
            // longer exists — the cross-test shape this assembly already carries a warning
            // about.
            await RaiseTheTextScaleFromTheAppearanceSheet(logPath, directory);
        }
        finally
        {
            // The view's disposal has just run; let its queued work finish before the next
            // test starts measuring a shared dispatcher.
            Dispatcher.UIThread.RunJobs();
            SessionWorkspaceView.PhoneCompositionOverride = null;
            TextScale.User = 1;
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task RaiseTheTextScaleFromTheAppearanceSheet(string logPath, string directory)
    {
        await using var view = new MainView([logPath], Path.Combine(directory, "settings.json"));
        var window = new Window { Content = view, Width = 420, Height = 900 };
        window.Show();
        try
        {
            var workspace = await WaitForWorkspace(view, window);
            var before = WorkspaceFontSizes(workspace);
            Assert.NotEmpty(before);

            view.OpenCommandSheet();
            window.UpdateLayout();
            Click(view, "Appearance & timeline…");
            await Settle(window);

            // On this TFM the sheet is a modal window of its own; on Android it is a card
            // inside the view. Either way it is the real ShowAppearanceAsync flow, which is
            // the route that used to leave the workspace behind.
            var sheet = Surfaces(view, window)
                .SelectMany(static surface => surface.GetLogicalDescendants())
                .OfType<AppearanceDialog>()
                .Single();

            // The one control in the sheet that formats itself as a multiplier.
            var textScale = sheet.GetLogicalDescendants()
                .OfType<NumericUpDown>()
                .Single(static control => control.FormatString == "0.00×");
            textScale.Value = 1.5m;
            await Settle(window);

            Click(sheet, "Apply");
            await Settle(window);

            // The view object itself is replaced, because a font size resolved while a view was
            // built reaches the screen no other way.
            var rebuilt = await WaitForWorkspace(view, window);
            var after = WorkspaceFontSizes(rebuilt);

            // The chip bar's own empty label is the witness the device measured: 13.1 dp
            // before the change, 13.1 dp after it, and 18.5 dp only once an unrelated system
            // configuration change happened to flush the workspace.
            const string chipLabel = "No filters · showing everything in view";
            Assert.True(before.ContainsKey(chipLabel), "the chip bar's empty label was not built");
            Assert.True(
                after[chipLabel] > before[chipLabel] * 1.4,
                FormattableString.Invariant(
                    $"'{chipLabel}' stayed at {after[chipLabel]} (from {before[chipLabel]}) while the reader asked for 1.5x"));

            // And it is not one label: every size the workspace resolved for itself follows.
            Assert.All(
                before.Keys.Where(after.ContainsKey),
                key => Assert.True(
                    after[key] > before[key] * 1.4,
                    FormattableString.Invariant(
                        $"'{key}' stayed at {after[key]} (from {before[key]}) while the reader asked for 1.5x")));
        }
        finally
        {
            window.Close();
        }
    }

    // ---------------------------------------------------------------- A-04 ---

    /// <summary>
    /// A-04 — the compact count line drops a whole fact rather than half of a number.
    /// </summary>
    /// <remarks>
    /// The device's own case: a 393 dp landscape workspace leaves the line 80 dp beside the
    /// three analysis tabs, and it rendered <c>50,156 view · 5…</c>. A half-drawn number is
    /// worse than a half-drawn word, because <c>5…</c> is itself a plausible count.
    /// </remarks>
    [AvaloniaTheory]
    // Room enough for everything: nothing is given up.
    [InlineData(400d, "50,156 view · 50,156")]
    // The device's own 80 dp, at roughly 6.2 px a character.
    [InlineData(80d, "50,156 view")]
    // Narrower than the words, which is the state the ellipsis is still there for.
    [InlineData(45d, "50,156")]
    public void TheCompactCountLineDropsAWholeFact(double room, string expected)
    {
        var chosen = SessionWorkspaceView.NarrowestSummaryThatFits(
            inView: 50_156,
            sessionTotal: 50_156,
            room,
            static text => text.Length * 6.2);

        Assert.Equal(expected, chosen);
    }

    /// <summary>A session whose size is unknown never invents one to drop.</summary>
    [AvaloniaFact]
    public void TheCompactCountLineNeverInventsASessionTotal()
    {
        Assert.Equal(
            "1,204 view",
            SessionWorkspaceView.NarrowestSummaryThatFits(1_204, null, 400, static text => text.Length * 6.2));
        Assert.Equal(
            "1,204",
            SessionWorkspaceView.NarrowestSummaryThatFits(1_204, null, 40, static text => text.Length * 6.2));
    }

    // ---------------------------------------------------------------- A-05 ---

    /// <summary>
    /// A-05 — the footer's load-more label gives up the remaining count whole rather than
    /// being clipped mid-glyph.
    /// </summary>
    /// <remarks>
    /// The device's own case: a 393 dp phone at a 1.55× text scale drew
    /// <c>Load 500 more · 49,656 remainir</c> across the bottom of the analysis pane, with
    /// no ellipsis and nothing to say the sentence continued. In the footer the control
    /// stretches to the pane, so a label that outgrows the pane has nowhere to go.
    /// </remarks>
    [AvaloniaTheory]
    // A full-width portrait band at an ordinary text size.
    [InlineData(320d, "Load 500 more · 49,656 remaining")]
    // The same band at 1.55×, where the sentence no longer fits.
    [InlineData(150d, "Load 500 more")]
    // Narrower than the words themselves: the header row's own compact label.
    [InlineData(60d, "+500")]
    public void TheLoadMoreLabelGivesUpAWholeFact(double room, string expected)
    {
        var chosen = SessionWorkspaceView.NarrowestThatFits(
            ["Load 500 more · 49,656 remaining", "Load 500 more", "+500"],
            room,
            static text => text.Length * 6.2);

        Assert.Equal(expected, chosen);
    }

    // ---------------------------------------------------------------- A-06 ---

    /// <summary>
    /// A-06 — a short viewport composes its panes side by side when the panes fit, not when
    /// two command groups happen to fit on one row.
    /// </summary>
    /// <remarks>
    /// The Pixel's landscape workspace is 777.5 dp wide, which seats both columns at any text
    /// size. Asking the command row's question instead — 600 dp <em>scaled by the reader's
    /// text size</em> — wanted 930 dp at 1.55×, so the panes stacked into a 143 dp band where
    /// the analysis pane resolved to nothing and Split drew a plot.
    /// </remarks>
    [Theory]
    // The Pixel's landscape workspace: side by side, and the text size does not change that.
    [InlineData(777.5, 1.0, true, true)]
    [InlineData(777.5, 1.55, true, false)]
    [InlineData(777.5, 2.0, true, false)]
    // A genuinely narrow compact viewport stacks, at every size (F-32's own geometry).
    [InlineData(434, 1.0, false, false)]
    [InlineData(434, 1.55, false, false)]
    // 360 dp portrait under a tall notice — the viewport F-32 was found in.
    [InlineData(360, 1.0, false, false)]
    public void PaneCompositionAsksThePaneQuestion(
        double width,
        double scale,
        bool fitsSideBySide,
        bool sharesARow)
    {
        Assert.Equal(
            fitsSideBySide,
            MobilePaneAllocator.FitsSideBySide(width, MobilePaneSplitter.LaneExtent));

        // Recorded so the divergence this finding is about stays visible rather than being
        // rediscovered: these are two different questions with two different answers, and
        // only one of them is about panes.
        Assert.Equal(sharesARow, MobileWorkspaceLayout.SharesARow(width, scale));
    }

    /// <summary>
    /// A-06 — the side-by-side threshold is exactly the two pane minimums plus the divider's
    /// lane, so a viewport that passes it can seat both columns.
    /// </summary>
    /// <remarks>
    /// At the threshold itself the two minimums consume the whole band, so the divider has no
    /// travel to offer and the allocator falls back to its automatic weights — which is the
    /// existing <c>MinimumUsefulTravel</c> rule, not a gap in this one. A little above it, the
    /// divider is live and both columns clear their own minimums.
    /// </remarks>
    [Fact]
    public void TheSideBySideThresholdIsTheTwoPaneMinimumsPlusTheLane()
    {
        var exact = MobilePaneAllocator.MinimumReadableTimelineWidth +
                    MobilePaneAllocator.MinimumUsableAnalysisWidth +
                    MobilePaneSplitter.LaneExtent;

        Assert.True(MobilePaneAllocator.FitsSideBySide(exact, MobilePaneSplitter.LaneExtent));
        Assert.False(MobilePaneAllocator.FitsSideBySide(exact - 1, MobilePaneSplitter.LaneExtent));

        // Both columns are seated at the threshold: neither is anywhere near the width at
        // which the heat map stops drawing at all.
        var atThreshold = Width(exact);
        var seatedPlot = (exact - MobilePaneSplitter.LaneExtent) * atThreshold.ResolvedPlotShare;
        Assert.True(
            seatedPlot > MobilePaneAllocator.TimelineRenderingWidthFloor,
            FormattableString.Invariant($"the plot column resolved to {seatedPlot} dp"));

        // And a viewport with room to move offers the divider, with both minimums reachable.
        var roomy = Width(exact + 60);
        Assert.True(roomy.SplitterEnabled);
        Assert.True(
            (exact + 60 - MobilePaneSplitter.LaneExtent) * roomy.MinimumPlotShare >=
            MobilePaneAllocator.MinimumReadableTimelineWidth - 0.01);
        Assert.True(
            (exact + 60 - MobilePaneSplitter.LaneExtent) * (1 - roomy.MaximumPlotShare) >=
            MobilePaneAllocator.MinimumUsableAnalysisWidth - 0.01);
    }

    private static MobilePaneWidthAllocation Width(double available) =>
        MobilePaneAllocator.ResolveWidth(new MobilePaneWidthRequest(
            SideBySide: true,
            AvailableWidth: available,
            PlotWeight: 21,
            AnalysisWeight: 29,
            SplitterLaneWidth: MobilePaneSplitter.LaneExtent,
            PlotMinimumWidth: MobilePaneAllocator.MinimumReadableTimelineWidth,
            AnalysisMinimumWidth: MobilePaneAllocator.MinimumUsableAnalysisWidth,
            PlotShare: null));

    // ------------------------------------------------------------- helpers ---

    private static void Click(Visual root, string name)
    {
        var button = root.GetLogicalDescendants()
            .OfType<Button>()
            .First(control =>
                AutomationProperties.GetName(control) == name ||
                (control.Content as string) == name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>The view, plus any window the dialog host opened over it.</summary>
    private static IEnumerable<Visual> Surfaces(MainView view, Window window) =>
        new Visual[] { view }.Concat(window.OwnedWindows);

    private static async Task Settle(Window window)
    {
        for (var pass = 0; pass < 8; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Yield();
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task<SessionWorkspaceView> WaitForWorkspace(MainView view, Window window)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await Settle(window);
            if (view.GetLogicalDescendants().OfType<SessionWorkspaceView>().FirstOrDefault() is { } workspace)
            {
                return workspace;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("the startup log never opened a workspace");
    }

    /// <summary>
    /// Every size the workspace resolved for itself while it was being built, keyed by the
    /// text that carries it.
    /// </summary>
    /// <remarks>
    /// Only labels that <em>state</em> a size are witnesses. Anything that merely inherits
    /// <c>FontSize</c> follows the shell live, so it grows on the broken implementation too
    /// and would let a test pass over the defect — which is exactly what a first attempt at
    /// this test did.
    /// </remarks>
    private static Dictionary<string, double> WorkspaceFontSizes(SessionWorkspaceView workspace) =>
        workspace.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(static block =>
                block.IsSet(TextBlock.FontSizeProperty) &&
                !string.IsNullOrWhiteSpace(block.Text))
            .GroupBy(static block => block.Text!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().FontSize,
                StringComparer.Ordinal);
}
