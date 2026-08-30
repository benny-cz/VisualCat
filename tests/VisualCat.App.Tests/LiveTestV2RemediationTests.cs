using Avalonia;
using Avalonia.Automation;
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
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// The fixes for the second Android live-test report
/// (<c>docs/ANDROID-LIVE-TEST-REPORT-V2.md</c>), at the level each one can be checked without
/// a device.
/// </summary>
/// <remarks>
/// Every assertion here fails against the implementation that shipped at commit
/// <c>0c9dd02</c>. What a headless run cannot settle — a physical touch at 2.25 px/dp, a stock
/// edge-Back gesture, the pixels of a system bar — is verified on the connected device and
/// recorded in §20 of that report.
/// </remarks>
public sealed class LiveTestV2RemediationTests
{
    private const string FourEntryLog =
        "01-01 00:00:00.000000   100   101 I Worker         : one\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : two\n" +
        "01-01 00:00:20.000000   100   101 E Loader         : three\n" +
        "01-01 00:00:30.000000   100   101 I Worker         : four\n";

    /// <summary>A ThreadTime crash block: three records and four indented stack frames.</summary>
    private const string CrashLog =
        "01-01 00:00:00.000000   100   101 E AndroidRuntime : FATAL EXCEPTION: main\n" +
        "01-01 00:00:00.000000   100   101 E AndroidRuntime : Process: com.example.app, PID: 100\n" +
        "01-01 00:00:00.000000   100   101 E AndroidRuntime : java.lang.IllegalStateException: boom\n" +
        "\tat com.example.app.Boom.explode(Boom.java:42)\n" +
        "\tat com.example.app.Boom.access$000(Boom.java:11)\n" +
        "\tat android.os.Handler.dispatchMessage(Handler.java:106)\n" +
        "\t... 12 more\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : after\n";

    // ---------------------------------------------------------------- V2-18 ---

    /// <summary>
    /// V2-18 — both scope choices are realised, painted, and inside the card at every text
    /// scale the platform can produce.
    /// </summary>
    /// <remarks>
    /// At <c>font_scale</c> 1.8 the restricted radio left the accessibility tree entirely and
    /// at 2.0 neither radio was painted at all, because a ninety-word disclosure paragraph sat
    /// above both of them in a card with no scroll affordance. A reader running large text was
    /// left with cancel or a Wireless-debugging pairing flow, and the zero-setup path B-10
    /// exists to protect was unreachable. Reproduced independently on a Pixel at 393 dp and a
    /// Motorola at 434 dp, so it is a composition failure and not a device one.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.3)]
    [InlineData(1.5)]
    [InlineData(1.8)]
    [InlineData(2.0)]
    public void BothLiveScopeChoicesStayInsideTheCardAtEveryTextScale(double scale)
    {
        var platform = TextScale.Platform;
        var saved = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            TextScale.Platform = scale;
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var dialog = new OnDeviceLogAccessDialog();

            // The Samsung's own portrait viewport at the override density it ships with, and
            // the sheet's height cap, which is what the card is actually given.
            var host = new Window
            {
                Width = 480,
                Height = 1040,
                Content = new Border
                {
                    Width = 464,
                    Height = 853,
                    Child = dialog,
                },
            };
            host.Show();
            for (var pass = 0; pass < 3; pass++)
            {
                host.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            try
            {
                var radios = dialog.GetLogicalDescendants().OfType<RadioButton>().ToArray();
                Assert.Equal(2, radios.Length);

                var scroller = dialog.GetLogicalDescendants().OfType<ScrollViewer>().First();
                foreach (var radio in radios)
                {
                    Assert.True(radio.IsEffectivelyVisible, $"radio not realised at {scale}");
                    var origin = radio.TranslatePoint(default, scroller);
                    Assert.NotNull(origin);

                    // Inside the viewport it was given, without the reader having to guess
                    // that a card with no fade and no chevron scrolls.
                    Assert.True(
                        origin!.Value.Y >= -0.5,
                        $"radio above the card viewport at {scale}: {origin.Value.Y}");
                    Assert.True(
                        origin.Value.Y + radio.Bounds.Height <= scroller.Viewport.Height + 0.5,
                        $"radio below the card viewport at {scale}: " +
                        $"{origin.Value.Y + radio.Bounds.Height} > {scroller.Viewport.Height}");
                }
            }
            finally
            {
                host.Close();
            }
        }
        finally
        {
            TextScale.Platform = platform;
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = saved;
        }
    }

    /// <summary>
    /// V2-18 — the disclosure is still reachable, and it is one labelled control away rather
    /// than ninety words in front of the decision.
    /// </summary>
    [AvaloniaFact]
    public void TheLiveScopeDisclosureIsCollapsedButNamedAndReachable()
    {
        var saved = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var dialog = new OnDeviceLogAccessDialog();
            var blocks = dialog.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            var disclosure = Assert.Single(
                blocks,
                static block => block.Text?.Contains("six-hour service limit", StringComparison.Ordinal) == true);

            Assert.False(disclosure.IsVisible);

            var toggle = Assert.Single(
                dialog.GetLogicalDescendants().OfType<Button>().ToArray(),
                static button => AutomationProperties.GetName(button) == "How full-device capture works");

            // The claim a reader is entitled to without asking stays on the card.
            Assert.Contains(
                blocks,
                static block => block.IsVisible &&
                                block.Text?.Contains("Nothing is uploaded", StringComparison.Ordinal) == true);

            toggle.Command?.Execute(null);
            RaiseClick(toggle);
            Assert.True(disclosure.IsVisible);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = saved;
        }
    }

    // ---------------------------------------------------------------- V2-05 ---

    /// <summary>
    /// V2-05 — every enabled control in the chip bar clears the touch floor.
    /// </summary>
    /// <remarks>
    /// The chip's remove button measured 15.6 × 16.4 dp and <em>Clear all</em> 40.0 dp on the
    /// device: the only two interactive nodes under the floor anywhere in the run, next to
    /// 48 and 49 dp neighbours. Both were literals — <c>MinWidth = 0</c> and a chip-bar-local
    /// <c>40</c> — that predate the <c>TouchTarget</c> seam.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheChipBarClearsTheTouchFloorOnAPhone()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        TouchTarget.TouchOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 480,
                height: 1040);
            fixture.Tab.SearchText = "Worker";
            await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var chips = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Where(static button =>
                    AutomationProperties.GetName(button)?.StartsWith("Remove filter", StringComparison.Ordinal) == true)
                .ToArray();
            Assert.NotEmpty(chips);
            foreach (var chip in chips)
            {
                Assert.True(
                    chip.Bounds.Height >= TouchTarget.Minimum,
                    $"chip target {chip.Bounds.Height} dp");
                Assert.True(chip.Bounds.Width >= TouchTarget.Minimum);
            }

            var clear = Assert.Single(
                fixture.View.GetLogicalDescendants().OfType<Button>().ToArray(),
                static button => button.Content as string == "Clear all");
            Assert.True(clear.Bounds.Height >= TouchTarget.Minimum, $"Clear all {clear.Bounds.Height} dp");
        }
        finally
        {
            TouchTarget.TouchOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;

            // A phone workspace tracks its plot, minimap and divider with the edge guard, and
            // the guard's registry is static. Leaving three closed controls in it makes the
            // next test's first publication a stale-geometry one.
            EdgeGestureGuard.Reset();
        }
    }

    // ---------------------------------------------------------------- V2-17 ---

    /// <summary>
    /// V2-17 — the allocator budgets the row height the list actually draws, not the design
    /// constant it was written with.
    /// </summary>
    /// <remarks>
    /// The container measured exactly 558 px at <c>font_scale</c> 1.0, 1.3 and 2.0 while the
    /// row grew 144 → 156 → 218 px underneath it, because <c>preferredAnalysis</c> was
    /// <c>chrome + 4 × 64</c> at every scale. The four-row floor silently became three rows,
    /// then two and a half.
    /// </remarks>
    [Fact]
    public void TheEntriesFloorIsBudgetedFromTheDrawnRowHeight()
    {
        static double AnalysisMinimum(double rowHeight) =>
            MobilePaneAllocator.Resolve(new MobilePaneAllocationRequest(
                MobilePaneComposition.Details,
                AvailableBandHeight: 900,
                TimelineWeight: 2,
                AnalysisWeight: 3,
                MinimapHeight: 0,
                SplitterLaneHeight: 0,
                AnalysisChromeHeight: 120,
                EntryRowHeight: rowHeight,
                PreferredEntryRows: 4,
                ManualEntryRows: 1,
                TimelineShare: null)).AnalysisMinimumHeight;

        Assert.Equal(120 + (4 * 64), AnalysisMinimum(64), 3);

        // The same request at the row a 2.0 text scale actually draws has to ask for more, or
        // the floor is a floor in name only.
        Assert.Equal(120 + (4 * 96.9), AnalysisMinimum(96.9), 3);
        Assert.True(AnalysisMinimum(96.9) > AnalysisMinimum(64));
    }

    /// <summary>
    /// V2-17 — the number the allocator multiplies by four is the row the list draws, at the
    /// reader's own text size.
    /// </summary>
    /// <remarks>
    /// This is the half of V2-17 the pure allocator test above cannot see. The allocator
    /// always multiplied whatever it was handed; what it was handed was
    /// <c>_entryRowMinimumHeight</c>, a constant 64, while the row is content-sized and reached
    /// 96.9 dp at <c>font_scale</c> 2.0. So the pane asked for four <em>design</em> rows and
    /// got two and a half real ones.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheBudgetedRowHeightFollowsTheDrawnRowAtLargeText()
    {
        var platform = TextScale.Platform;
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            TextScale.Platform = 2.0;
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 480,
                height: 1040);
            for (var pass = 0; pass < 5; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var entries = fixture.View.GetLogicalDescendants()
                .OfType<ListBox>()
                .First(static list => AutomationProperties.GetName(list)?
                    .StartsWith("Filtered log entries", StringComparison.Ordinal) == true);
            var drawn = entries.GetRealizedContainers()
                .Select(static container => container.Bounds.Height)
                .DefaultIfEmpty(0)
                .Max();
            Assert.True(drawn > 0, "no entry row was realised");

            // The row genuinely grows with the reader's text size; if it did not, this test
            // would prove nothing about the budget.
            Assert.True(drawn > 64, $"row height at 2.0 was {drawn} dp");
            Assert.True(
                fixture.View.BudgetedEntryRowHeight >= drawn - 0.5,
                $"budgeted {fixture.View.BudgetedEntryRowHeight} dp against a drawn {drawn} dp row");

            // Either the pane is given its four whole rows, or Split has honestly given up and
            // composed Details — never a lit Split button above two and a half rows.
            Assert.True(
                fixture.View.SplitStarvedByTextScale ||
                fixture.View.LastMobilePaneAllocation.AnalysisMaximumHeight >= 4 * drawn ||
                fixture.View.LastMobilePaneAllocation.AnalysisMinimumHeight >= 4 * drawn);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            TextScale.Platform = platform;
            EdgeGestureGuard.Reset();
        }
    }

    // ---------------------------------------------------------------- V2-21 ---

    /// <summary>
    /// V2-21 — the platform's edge gesture is released while a modal layer is over the
    /// workspace, and taken back when it closes.
    /// </summary>
    /// <remarks>
    /// The plot's claim is honest while the plot is what the finger is on. Under a sheet it
    /// suppressed system Back across a 205 dp band of a Pixel — at exactly the moments there
    /// was a layer to peel — so a gesture-only reader got no Back at all below the header and
    /// too much Back above it.
    /// </remarks>
    [AvaloniaFact]
    public void SuspendingTheEdgeGuardReleasesEveryClaimAndResumeTakesThemBack()
    {
        var published = new List<IReadOnlyList<PixelRect>>();
        PlatformSourceRegistry.SetGestureExclusions = rectangles => published.Add(rectangles);
        try
        {
            EdgeGestureGuard.Suspend(true);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(published);
            Assert.Empty(published[^1]);

            var releasedAt = published.Count;
            EdgeGestureGuard.Suspend(true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(releasedAt, published.Count);

            EdgeGestureGuard.Suspend(false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(published.Count > releasedAt);
        }
        finally
        {
            EdgeGestureGuard.Reset();
            PlatformSourceRegistry.SetGestureExclusions = null;
        }
    }

    // ---------------------------------------------------------------- V2-23 ---

    /// <summary>
    /// V2-23 — one Back press takes down exactly one layer, even when the layer contains a
    /// button that listens for Escape itself.
    /// </summary>
    /// <remarks>
    /// Found on the device while verifying V2-18. Android delivers a key Back as a
    /// <see cref="Key.Escape"/> key-down and then the platform back callback. A dialog whose
    /// Cancel carries <c>IsCancel</c> answers that key-down through Avalonia's own root hook,
    /// so the card closed <em>and</em> the callback then found an empty overlay stack and let
    /// the platform background the task. Back dismissed the Live scope chooser and left the
    /// app in one press. The More sheet was unaffected, because it has no such button — which
    /// is why the earlier pass recorded Back as passing.
    /// </remarks>
    [AvaloniaFact]
    public async Task OneBackPressTakesDownOneLayerEvenWithAnIsCancelButtonInIt()
    {
        MainView.InPageDialogOverride = true;
        var host = new MainView();
        var window = new Window { Content = host, Width = 480, Height = 1040 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var dialog = new OnDeviceLogAccessDialog();
            var presented = host.ShowDialogAsync(dialog);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            Dispatcher.UIThread.RunJobs();

            // The card is gone…
            Assert.Equal(OnDeviceLogAccessChoice.Cancel, await presented);

            // …and the press that closed it is not also available to the platform, which is
            // what would background the task.
            Assert.True(host.TryNavigateBack());

            // A second, unrelated press has nothing to answer and is left to the platform.
            Assert.False(host.TryNavigateBack());
        }
        finally
        {
            window.Close();
            MainView.InPageDialogOverride = null;

            // Pushing an overlay suspends the edge guard and taking it down resumes it, and
            // the resume is scheduled. Drain it here rather than leaving a queued empty
            // publication to land inside the next test's assertions.
            Dispatcher.UIThread.RunJobs();
            EdgeGestureGuard.Reset();
        }
    }

    // ------------------------------------------------------- V2-13 and V2-14 ---

    /// <summary>
    /// V2-14 — the lines ADR 0009 keeps as unknown are counted and readable.
    /// </summary>
    /// <remarks>
    /// Nothing about the parse changes: the four indented frames stay unknown lines, which is
    /// what the ADR decides and why the source pane can prove nothing was dropped. What
    /// changes is that the reader is told they exist and can open them.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACrashLogCountsAndSurfacesItsStackFrames()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(CrashLog);
        Dispatcher.UIThread.RunJobs();

        var counters = fixture.Tab.Snapshot!.Descriptor.Counters;
        Assert.True(counters.UnknownLines > 0, "the corpus must produce unknown lines");
        Assert.True(fixture.Tab.UnparsedLineCount >= counters.UnknownLines);

        var page = await fixture.Tab.LoadUnparsedLinesAsync();
        Assert.True(page.Count > 0);
        Assert.Contains("Boom.java:42", page.Text, StringComparison.Ordinal);

        // The gutter form is the source pane's, so the two surfaces read as one view of one
        // file — and the codes in it are decoded on screen rather than in a tooltip (V2-04).
        Assert.Contains("??", page.Text, StringComparison.Ordinal);
        Assert.True(ParseOutcomeLegend.AppliesTo(page.Text));
        Assert.False(ParseOutcomeLegend.AppliesTo("  1 en │ nothing unusual here\n"));
    }

    /// <summary>
    /// V2-13 — the count line never lets <c>match</c> exceed <c>in session</c> without saying
    /// which populations the two numbers count.
    /// </summary>
    [AvaloniaFact]
    public async Task TheCountLineNamesTheTimedPopulationWhenSomethingIsOutsideIt()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(CrashLog);
        for (var pass = 0; pass < 3; pass++)
        {
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        var summary = fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(AutomationProperties.GetName)
            .FirstOrDefault(static name => name?.Contains("in view", StringComparison.Ordinal) == true);
        Assert.NotNull(summary);
        Assert.Contains("timed in session", summary!, StringComparison.Ordinal);
        Assert.Contains("unparsed lines", summary, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- V2-19 ---

    /// <summary>
    /// V2-19 — the phone footer offers a cancellable Load all beside Load 500 more.
    /// </summary>
    /// <remarks>
    /// The implementation only ever wrote <c>_loadAll</c> inside <c>if (!_mobile)</c>, so the
    /// phone's footer held <c>Load 500 more</c> alone: after 3,500 rows of a 999,885-entry
    /// session it still said <c>996,385 remaining</c>, and reaching the end took 1,999 taps.
    /// X-13 requires either completion or an explicit bound; the phone offered neither the
    /// action nor an explanation.
    /// </remarks>
    [AvaloniaFact]
    public async Task ThePhoneFooterOffersLoadAll()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        TouchTarget.TouchOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                ManyEntryLog(),
                width: 480,
                height: 1040);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            // Scoped to the footer band: the phone and desktop compositions are both built,
            // so an unscoped sweep finds each paging control twice.
            var footer = fixture.View.GetLogicalDescendants()
                .OfType<Border>()
                .First(static border => AutomationProperties.GetName(border) == "End of the loaded rows");
            var buttons = footer.GetLogicalDescendants().OfType<Button>().ToArray();
            var loadAll = Assert.Single(
                buttons,
                static button => button.Content as string == "All");

            Assert.True(loadAll.IsEffectivelyVisible);
            Assert.True(loadAll.IsEnabled);
            Assert.True(loadAll.Bounds.Height >= TouchTarget.Minimum, $"height {loadAll.Bounds.Height} dp");
            Assert.True(loadAll.Bounds.Width >= TouchTarget.Minimum, $"width {loadAll.Bounds.Width} dp");

            // The label is three characters wide on a phone; the number lives in the name a
            // screen reader speaks and a tooltip shows.
            Assert.Contains(
                "remaining matching rows",
                AutomationProperties.GetName(loadAll)!,
                StringComparison.Ordinal);

            // Load 500 more keeps its own full-width target beside it.
            var loadMore = Assert.Single(
                buttons,
                static button => button.Content as string != "All");
            Assert.True(loadMore.Bounds.Width > loadAll.Bounds.Width);
        }
        finally
        {
            TouchTarget.TouchOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
            EdgeGestureGuard.Reset();
        }
    }

    // ---------------------------------------------------------------- V2-20 ---

    /// <summary>
    /// V2-20 — the primary action row does not move when the notice lane enters or leaves.
    /// </summary>
    /// <remarks>
    /// A successful copy raised a notice, the notice took 140 px out of the workspace, both
    /// panes gave up their share of it, and <c>Copy raw</c> arrived 140 px higher up the
    /// screen. A second tap at the same coordinate then landed inside the selected list row
    /// and opened the Entry tab instead — a successful action changing what the reader's
    /// finger will do next, with no pointer movement, which is the class of failure R-34
    /// exists to prevent.
    ///
    /// The lane's height comes out of the plot now, and the entries list gives up the rows.
    /// Everything above the list keeps its coordinates.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheEntryActionRowHoldsItsPlaceWhenTheNoticeLaneAppears()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        TouchTarget.TouchOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                ManyEntryLog(),
                width: 480,
                height: 1040);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            // Both compositions are built; the phone one is the effectively visible one.
            var copy = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .First(static button =>
                    button.IsEffectivelyVisible &&
                    (AutomationProperties.GetName(button) == "Copy raw" ||
                     button.Content as string == "Copy raw"));
            var before = copy.TranslatePoint(default, fixture.Window)!.Value.Y;

            // The lane the shell docks under this view, at the height the device measured: it
            // takes that height out of the workspace and tells the workspace how much.
            fixture.Window.Height -= 62;
            fixture.View.SetNoticeReserve(62);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var after = copy.TranslatePoint(default, fixture.Window)!.Value.Y;
            Assert.True(
                Math.Abs(after - before) <= 1.5,
                $"Copy raw moved {after - before} dp when the notice lane appeared");

            fixture.Window.Height += 62;
            fixture.View.SetNoticeReserve(0);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var restored = copy.TranslatePoint(default, fixture.Window)!.Value.Y;
            Assert.True(
                Math.Abs(restored - before) <= 1.5,
                $"Copy raw moved {restored - before} dp when the notice lane left");
        }
        finally
        {
            TouchTarget.TouchOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
            EdgeGestureGuard.Reset();
        }
    }

    // -------------------------------------------------------- V2-01 and V2-02 ---

    /// <summary>
    /// V2-01 and V2-02 — the empty state centres its hero and reports an honest extent.
    /// </summary>
    /// <remarks>
    /// One cause, two findings. <c>ScrollViewer.VerticalContentAlignment</c> does not centre on
    /// the scroll axis, so the hero was arranged from the top of an 849 dp workspace and ended
    /// 45 % down it, leaving 559 dp of empty ground below both primary actions — and the same
    /// presenter reported an extent of about 5.6 screens, which painted a permanent 340 px
    /// scrollbar thumb, half-clipped by the panel edge, on a screen that could not scroll.
    /// </remarks>
    [AvaloniaFact]
    public void TheEmptyStateCentresItsHeroAndClaimsNoScrollableExtent()
    {
        MainView.InPageDialogOverride = true;
        var host = new MainView();
        var window = new Window { Content = host, Width = 480, Height = 1040 };
        window.Show();
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        try
        {
            var scroller = host.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .First(static viewer => viewer.Bounds.Height > 0);

            // Nothing scrolls, so nothing may claim it does.
            Assert.True(
                scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"extent {scroller.Extent.Height} > viewport {scroller.Viewport.Height}");

            var hero = Assert.IsType<Panel>(scroller.Content).Children[0];
            var top = hero.TranslatePoint(default, scroller)!.Value.Y;
            var bottom = top + hero.Bounds.Height;
            var below = scroller.Viewport.Height - bottom;

            // Centred: the ground above and below the hero is the same, within a pixel.
            Assert.True(
                Math.Abs(top - below) <= 1.5,
                $"hero not centred: {top} above, {below} below");
        }
        finally
        {
            window.Close();
            MainView.InPageDialogOverride = null;
            Dispatcher.UIThread.RunJobs();
            EdgeGestureGuard.Reset();
        }
    }

    // ---------------------------------------------------------------- V2-03 ---

    /// <summary>
    /// V2-03 — with nothing stored, the recent-captures card says so and offers the action
    /// that changes it, instead of explaining a taxonomy and showing a disabled Open.
    /// </summary>
    [AvaloniaFact]
    public void TheRecentCapturesCardHasAnEmptyStateOfItsOwn()
    {
        var dialog = new RecentSessionsDialog([]);
        var text = string.Join(
            "\n",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(static block => block.Text ?? string.Empty));

        Assert.Contains("No captures on this device yet.", text, StringComparison.Ordinal);

        // The complete/interrupted/in-progress explanation is help text for a populated list.
        Assert.DoesNotContain("A complete capture was stopped", text, StringComparison.Ordinal);

        var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
        Assert.DoesNotContain(buttons, static button => button.Content as string == "Open");
        var capture = Assert.Single(
            buttons,
            static button => button.Content as string == "Capture this device's log");
        Assert.True(capture.IsEnabled);

        // And with a session, the list, the legend and Open all come back.
        var populated = new RecentSessionsDialog(
        [
            new TemporarySessionInfo(
                Path.Combine(Path.GetTempPath(), "vc", "session-a"),
                DateTimeOffset.UtcNow,
                1024,
                true),
        ]);
        var populatedText = string.Join(
            "\n",
            populated.GetLogicalDescendants().OfType<TextBlock>().Select(static block => block.Text ?? string.Empty));
        Assert.Contains("A complete capture was stopped", populatedText, StringComparison.Ordinal);
        Assert.Contains(
            populated.GetLogicalDescendants().OfType<Button>(),
            static button => button.Content as string == "Open");
    }

    // ---------------------------------------------------------------- V2-07 ---

    /// <summary>
    /// V2-07 — the first and the last match are one tap away.
    /// </summary>
    /// <remarks>
    /// The only navigation was one match at a time, and the counter opened at the caret's
    /// position rather than at match 1 — so on a 7,181-match search, reaching the first
    /// occurrence took 3,578 taps and the last another 3,602. Finding the first occurrence of
    /// something is the most common reason anyone searches a log.
    /// </remarks>
    [AvaloniaFact]
    public async Task SearchCanReachItsFirstAndLastMatch()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog());
        fixture.Tab.SearchText = "line 1";
        await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false);

        // The search reports its markers back through the dispatcher after the await returns,
        // so this waits for them rather than assuming three layout passes are enough.
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SearchResult?.Markers is { Count: > 2 });

        var markers = fixture.Tab.SearchResult?.Markers;
        Assert.NotNull(markers);
        Assert.True(markers!.Count > 2);

        var first = fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button => AutomationProperties.GetName(button) == "First search match");
        var last = fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button => AutomationProperties.GetName(button) == "Last search match");
        Assert.True(first.IsEnabled);
        Assert.True(last.IsEnabled);

        var fitted = fixture.Tab.Viewport!.Value;

        await fixture.View.NavigateToSearchEdgeAsync(last: false);

        // The jump runs a query off the dispatcher and reports back through it, so waiting for
        // the viewport to actually move is the wait; a fixed pass count is a guess.
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.Viewport is { } current && current.DurationUs < fitted.DurationUs);
        var atFirst = fixture.Tab.Viewport!.Value;
        Assert.True(
            atFirst.StartInclusive.Value <= markers[0].Value && markers[0].Value <= atFirst.EndExclusive.Value,
            "the first match is not inside the viewport after jumping to it");

        // Arriving somewhere. From Fit the jump has to narrow, or it clamps back to the
        // viewport it started in and the button reads as doing nothing (V2-07).
        Assert.True(
            atFirst.DurationUs < fitted.DurationUs,
            $"the viewport did not move from Fit: {atFirst.DurationUs} vs {fitted.DurationUs}");
        Assert.True(atFirst.EndExclusive.Value < fitted.EndExclusive.Value);

        await fixture.View.NavigateToSearchEdgeAsync(last: true);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.Viewport is { } current &&
                  current.StartInclusive.Value > atFirst.StartInclusive.Value);
        var atLast = fixture.Tab.Viewport!.Value;
        Assert.True(
            atLast.StartInclusive.Value <= markers[^1].Value && markers[^1].Value <= atLast.EndExclusive.Value,
            "the last match is not inside the viewport after jumping to it");
        Assert.True(atLast.StartInclusive.Value > atFirst.StartInclusive.Value);

        // B-06's "navigation wraps at both ends" was untestable through the UI while the ends
        // could not be reached at all (V2-07 records it as a coverage gap). With the last
        // match one tap away it can finally be asserted: from a window whose centre is past
        // the final marker, Next comes back to the first.
        var tail = new TimeRange(
            new InstantUs(markers[^1].Value),
            fixture.Tab.Snapshot!.TimedRange!.Value.EndExclusive);
        if (tail.DurationUs > 0)
        {
            await fixture.Tab.SetViewportAsync(tail);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.Viewport is { } current &&
                      current.StartInclusive.Value >= markers[^1].Value);
            await fixture.View.NavigateSearchMatchAsync(1);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.Viewport is { } current &&
                      current.StartInclusive.Value <= markers[0].Value);
            var wrapped = fixture.Tab.Viewport!.Value;
            Assert.True(
                wrapped.StartInclusive.Value <= markers[0].Value &&
                markers[0].Value <= wrapped.EndExclusive.Value,
                "Next did not wrap to the first match from beyond the last one");
        }
    }

    // ---------------------------------------------------------------- V2-06 ---

    /// <summary>
    /// V2-06 — the two controls named for the open entry agree about whether it is in scope,
    /// and the pane says so when it is not.
    /// </summary>
    /// <remarks>
    /// <c>Copy raw</c> was driven by the list selection and <c>Entry</c> by the inspected
    /// entry, so on the cell-selection route one was disabled and the other opened an Error
    /// record under a Fatal-only filter, with nothing on screen admitting it. A reader
    /// triaging a crash could copy a message believing it came from the filtered set.
    /// </remarks>
    [AvaloniaFact]
    public async Task BothEntryActionsAgreeAndTheOutOfFilterEntrySaysSo()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        for (var pass = 0; pass < 3; pass++)
        {
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        var entries = fixture.View.GetLogicalDescendants()
            .OfType<ListBox>()
            .First(static list => AutomationProperties.GetName(list)?
                .StartsWith("Filtered log entries", StringComparison.Ordinal) == true);

        // The Error row, opened the way the cell route opens one: the reader is reading it,
        // and the list holds no selection of its own. The plain select-then-filter path does
        // not reach this state, which is why V2-06 needed a cell tap to reproduce.
        var error = fixture.Tab.Entries.First(static entry => entry.Level == LogLevel.Error);
        entries.SelectedItem = error;
        Dispatcher.UIThread.RunJobs();
        fixture.View.InspectEntryForTest(error);
        Dispatcher.UIThread.RunJobs();

        Button Find(string name) => fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => AutomationProperties.GetName(button) == name ||
                             button.Content as string == name);

        var copy = Find("Copy raw");
        var open = Find("Show the full message of the selected entry");
        Assert.True(copy.IsEnabled);
        Assert.True(open.IsEnabled);

        // Now admit only Fatal, which this session has none of.
        foreach (var level in new[] { LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, LogLevel.Verbose, LogLevel.Unknown })
        {
            await fixture.Tab.SetLevelAsync(level, included: false);
        }
        for (var pass = 0; pass < 3; pass++)
        {
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        // One predicate: whatever the answer is, it is the same answer for both.
        Assert.Equal(open.IsEnabled, copy.IsEnabled);

        // And when the entry is kept, the pane says it is out of scope and offers the way back.
        if (open.IsEnabled)
        {
            var banner = fixture.View.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(static block => block.Text ?? string.Empty)
                .FirstOrDefault(static text => text.Contains("not in the current filter", StringComparison.Ordinal));
            Assert.False(
                string.IsNullOrEmpty(banner),
                "an entry kept outside the filter has to say so");
            Assert.Contains(
                fixture.View.GetLogicalDescendants().OfType<Button>(),
                static button => button.Content as string == "Clear filters" && button.IsVisible);
        }
    }

    // ---------------------------------------------------------------- V2-09 ---

    /// <summary>
    /// V2-09 — the overscroll margin is bounded by the viewport, so it reads as an edge at
    /// every zoom instead of as a screen that failed to draw.
    /// </summary>
    [AvaloniaFact]
    public void OverscrollIsBoundedByTheViewportAsWellAsTheSession()
    {
        var session = new TimeRange(new InstantUs(0), new InstantUs(75_186_000));

        // At Fit the margin is unchanged: it is already smaller than a tenth of the viewport.
        var fitted = TimelineTransform.Clamp(
            new TimeRange(new InstantUs(-1_000_000_000), new InstantUs(-1_000_000_000 + session.DurationUs)),
            session,
            0.05);
        Assert.Equal(
            session.StartInclusive.Value - (long)(session.DurationUs * 0.05),
            fitted.StartInclusive.Value);

        // Zoomed to 4.699 s — the state the device measured — the empty band is at most a
        // tenth of the plot rather than 78 % of it.
        const long span = 4_699_000;
        var zoomed = TimelineTransform.Clamp(
            new TimeRange(new InstantUs(-1_000_000_000), new InstantUs(-1_000_000_000 + span)),
            session,
            0.05);
        var margin = session.StartInclusive.Value - zoomed.StartInclusive.Value;
        Assert.True(margin > 0, "the affordance is kept");
        Assert.True(
            margin <= (long)(span * TimelineTransform.MaximumOverscrollViewportFraction) + 1,
            $"overscroll {margin} us is more than a tenth of a {span} us viewport");

        // The original behaviour, for the record: a constant 5 % of the session, which at this
        // zoom is 3.76 s of a 4.70 s plot.
        Assert.True(margin < (long)(session.DurationUs * 0.05));
    }

    // ---------------------------------------------------------------- V2-10 ---

    /// <summary>
    /// V2-10 — a Follow window is never written into the stored view, so reopening a finished
    /// capture starts at Fit rather than on an empty 30-second live edge.
    /// </summary>
    [AvaloniaFact]
    public async Task AFollowWindowIsNotPersistedAsTheReadersViewport()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog());
        Dispatcher.UIThread.RunJobs();

        var captured = SessionTabViewModel.CaptureViewStateForTest(fixture.Tab, following: true);
        Assert.Null(captured.Viewport);
        Assert.False(captured.FollowLatest);

        // A viewport the reader chose is still theirs.
        var chosen = SessionTabViewModel.CaptureViewStateForTest(fixture.Tab, following: false);
        Assert.NotNull(chosen.Viewport);
    }

    // ---------------------------------------------------------------- V2-12 ---

    /// <summary>
    /// V2-12 — an empty file is reported as empty, not as an undetectable format.
    /// </summary>
    /// <remarks>
    /// A zero-byte file produced the byte-for-byte identical presentation to 10 MiB of random
    /// noise, down to the paragraph advising the reader to check that it is a logcat capture
    /// and not a bug report. It is not a detection failure; there is nothing to detect.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnEmptyFileSaysItIsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var empty = Path.Combine(root, "empty.txt");
            await File.WriteAllTextAsync(empty, string.Empty, TestContext.Current.CancellationToken);
            await using var workspace = new WorkspaceViewModel();
            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => workspace.ImportFileAsync(empty, cancellationToken: TestContext.Current.CancellationToken));

            var message = WorkspaceViewModel.FriendlyMessage(failure);
            Assert.Contains("empty", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("format", message, StringComparison.OrdinalIgnoreCase);

            var remedy = WorkspaceViewModel.ImportRemedy(failure);
            Assert.NotNull(remedy);
            Assert.DoesNotContain("bug report", remedy!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // -------------------------------------- V2-13 and V2-14, second pass ---

    /// <summary>
    /// V2-13 — the records no time-based view can show have a home in the count row, and it is
    /// not mistaken for a filter.
    /// </summary>
    /// <remarks>
    /// The first pass named untimed records on the count line and stopped there. The finding
    /// asks for somewhere to <em>go</em>: they are counted by the filter and drawn by nothing,
    /// so the only way to reach them was to already know they existed.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheOffTimelineChipNamesItsRecordsWithoutClaimingAFilter()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        TouchTarget.TouchOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                CrashLog,
                width: 480,
                height: 1040);
            for (var pass = 0; pass < 4; pass++)
            {
                fixture.Window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            Assert.True(fixture.Tab.OffTimelineCount > 0);

            var chip = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .First(static button => AutomationProperties.GetName(button)?
                    .Contains("not on the timeline", StringComparison.Ordinal) == true);
            Assert.True(chip.IsEffectivelyVisible);
            Assert.True(chip.Bounds.Height >= TouchTarget.Minimum, $"{chip.Bounds.Height} dp");

            // It is not a filter, so the strip must go on saying nothing is filtered and must
            // not offer Clear all for something it cannot clear.
            Assert.Contains(
                fixture.View.GetLogicalDescendants().OfType<TextBlock>(),
                static block => block.IsVisible &&
                                block.Text == "No filters · showing everything in view");
            Assert.DoesNotContain(
                fixture.View.GetLogicalDescendants().OfType<Button>(),
                static button => button.Content as string == "Clear all" && button.IsVisible);

            // And it asks the shell for the card rather than presenting one itself.
            var asked = 0;
            fixture.View.OffTimelineRequested += () => asked++;
            RaiseClick(chip);
            Assert.Equal(1, asked);
        }
        finally
        {
            TouchTarget.TouchOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
            EdgeGestureGuard.Reset();
        }
    }

    /// <summary>
    /// V2-13 — untimed records are listed by the same read path the unparsed lines use.
    /// </summary>
    [AvaloniaFact]
    public async Task UntimedRecordsAreListedBesideTheUnparsedLines()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(MixedFormatLog());
        Dispatcher.UIThread.RunJobs();

        Assert.True(fixture.Tab.UntimedEntryCount > 0, "the corpus must produce untimed records");

        var page = await fixture.Tab.LoadUnparsedLinesAsync();
        Assert.True(page.Count > 0);

        // `e?` is the gutter's code for an untimed entry, and the legend on the card decodes it.
        Assert.Contains("e?", page.Text, StringComparison.Ordinal);
    }

    // ------------------------------------------------ V2-07, second pass ---

    /// <summary>
    /// V2-07 — the counter is a control that reaches a match by number.
    /// </summary>
    [AvaloniaFact]
    public async Task TheMatchCounterJumpsToAChosenMatch()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog());
        fixture.Tab.SearchText = "line 1";
        await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SearchResult?.Markers is { Count: > 2 });

        var markers = fixture.Tab.SearchResult!.Markers;

        // Without a host to ask, the counter is inert rather than blocking on a dialog nobody
        // can see — and it says so by being disabled.
        var counter = fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button => button.Content is TextBlock text &&
                                    text.Text?.Contains(" / ", StringComparison.Ordinal) == true);
        Assert.False(counter.IsEnabled);

        long? asked = null;
        fixture.View.AskForNumberAsync = (_, _, _, maximum) =>
        {
            asked = maximum;
            return Task.FromResult<long?>(3);
        };
        fixture.View.UpdateMarkerNavigationForTest();
        Assert.True(counter.IsEnabled);

        RaiseClick(counter);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => asked is not null && fixture.Tab.Viewport is { } current &&
                  current.StartInclusive.Value <= markers[2].Value &&
                  markers[2].Value <= current.EndExclusive.Value);

        Assert.Equal(markers.Count, asked);
        var viewport = fixture.Tab.Viewport!.Value;
        Assert.True(
            viewport.StartInclusive.Value <= markers[2].Value &&
            markers[2].Value <= viewport.EndExclusive.Value,
            "the third match is not inside the viewport after asking for it by number");
    }

    /// <summary>200 KB of ThreadTime followed by Brief records, which parse as untimed.</summary>
    private static string MixedFormatLog()
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < 200; index++)
        {
            builder.Append("01-01 00:00:")
                .Append((index % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(".000000   100   101 I Worker         : timed ")
                .Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        for (var index = 0; index < 200; index++)
        {
            builder.Append("D/BriefTag ( 1001): brief record ")
                .Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>A session with enough rows that paging is a real operation.</summary>
    private static string ManyEntryLog()
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < 1200; index++)
        {
            builder.Append("01-01 00:00:")
                .Append((index % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture))
                .Append('.')
                .Append((index % 1000).ToString("000", System.Globalization.CultureInfo.InvariantCulture))
                .Append("000   100   101 I Worker         : line ")
                .Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static void RaiseClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
