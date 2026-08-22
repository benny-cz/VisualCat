using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Application.Ports;
using VisualCat.Core.Query;
using VisualCat.Domain;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

/// <summary>
/// The fixes for the Android live-test report (<c>docs/ANDROID-LIVE-TEST-REPORT.md</c>),
/// at the level each one can be checked without a device.
/// </summary>
/// <remarks>
/// Findings that are only observable on the platform — an IME inset, a physical touch target
/// at 450 dpi, a logcat buffer divider — are verified on the device and recorded in §5.2 of
/// that report. What lands here is the part that can regress silently in a headless build.
/// </remarks>
public sealed partial class LiveTestRemediationTests
{
    private const string OneEntryLog =
        "01-01 00:00:00.000000   100   101 I Worker         : the only line\n";

    private const string FourEntryLog =
        "01-01 00:00:00.000000   100   101 I Worker         : one\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : two\n" +
        "01-01 00:00:20.000000   100   101 E Loader         : three\n" +
        "01-01 00:00:30.000000   100   101 I Worker         : four\n";

    // --------------------------------------------------------------- F-20 ---

    /// <summary>
    /// F-20 — a session holding one entry spans about a microsecond, which is narrower than the
    /// plot's own pixel resolution. Every zoom route but the wheel handed that raw span to
    /// <see cref="TimelineTransform.Zoom"/> as the <em>maximum</em>, below the minimum it had
    /// just been given, and the resulting <see cref="ArgumentOutOfRangeException"/> escaped the
    /// touch handler and terminated the app.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryZoomRouteSurvivesAOneEntrySession()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(OneEntryLog);
        var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
        var point = PointIn(timeline, fixture.Window);

        // Double-tap: the route the device crashed on.
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);

        // Wheel, in both directions.
        fixture.Window.MouseWheel(point, new Vector(0, 1));
        fixture.Window.MouseWheel(point, new Vector(0, -1));

        // Keyboard + / -.
        timeline.Focus();
        fixture.Window.KeyPress(Key.OemPlus, RawInputModifiers.None, PhysicalKey.Equal, "+");
        fixture.Window.KeyPress(Key.OemMinus, RawInputModifiers.None, PhysicalKey.Minus, "-");

        // The programmatic route the mobile zoom buttons use.
        timeline.ZoomAtCenter(0.5);
        timeline.ZoomAtCenter(2);
        timeline.FitSession();

        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(fixture.Tab.Viewport);
    }

    /// <summary>
    /// F-20 — the same defect reproduced without any gesture at all: a finished one-entry import
    /// opened at the raw session span, so the header read <c>1 µs</c> with the same instant at
    /// both ends of the axis. A fitted viewport is never narrower than something the plot can
    /// draw, and widening it never hides a record.
    /// </summary>
    [AvaloniaFact]
    public async Task AOneEntryImportOpensAtADrawableSpan()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(OneEntryLog);
        Dispatcher.UIThread.RunJobs();

        var viewport = fixture.Tab.Viewport;
        Assert.NotNull(viewport);
        Assert.True(
            viewport.Value.DurationUs >= 2_000_000,
            $"a one-entry session opened at {viewport.Value.DurationUs} µs");

        var session = fixture.Tab.Snapshot?.TimedRange;
        Assert.NotNull(session);
        Assert.True(
            viewport.Value.StartInclusive <= session.Value.StartInclusive &&
            viewport.Value.EndExclusive >= session.Value.EndExclusive,
            "the fitted viewport must still contain the whole session");
    }

    /// <summary>
    /// F-20 — the fit floor must not widen a session that is already wider than it, or every
    /// ordinary import would open zoomed out past its own data.
    /// </summary>
    [AvaloniaFact]
    public async Task AnOrdinaryImportStillOpensExactlyFitted()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();

        var viewport = fixture.Tab.Viewport;
        var session = fixture.Tab.Snapshot?.TimedRange;
        Assert.NotNull(viewport);
        Assert.NotNull(session);
        Assert.Equal(session.Value, viewport.Value);
    }

    // --------------------------------------------------------------- F-04 ---

    /// <summary>
    /// F-04 — a pattern that cannot compile is refused at the point of entry, so the filter it
    /// would have become is never applied. The chip bar used to show <c>regex = (unclosed</c>
    /// as an active filter while the list showed all 49,994 rows unfiltered.
    /// </summary>
    [AvaloniaFact]
    public async Task AnUncompilablePatternIsRefusedAndChangesNothing()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        var before = fixture.Tab.Filter.Fingerprint();

        fixture.Tab.SearchText = "(unclosed";
        var problem = await fixture.Tab.ApplySearchAsync(regex: true, caseSensitive: false);

        Assert.NotNull(problem);
        Assert.Equal(RegexParseError.InsufficientClosingParentheses, problem.Value.Error);
        Assert.Equal(before, fixture.Tab.Filter.Fingerprint());
        Assert.Null(fixture.Tab.Filter.Search);
    }

    /// <summary>F-04 — the same text, applied as a literal, is not a pattern and applies.</summary>
    [AvaloniaFact]
    public async Task TheSameTextAppliesAsALiteral()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        fixture.Tab.SearchText = "(unclosed";

        Assert.Null(await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false));
        Assert.NotNull(fixture.Tab.Filter.Search);
    }

    /// <summary>
    /// F-04 — the sentence is composed from the parser's structured members, which do not
    /// degrade in a Release Android build, and it names the problem rather than the parser.
    /// </summary>
    [Fact]
    public void APatternProblemReadsAsProductLanguage()
    {
        Assert.False(SearchPattern.TryCompile(
            new TextSearchSpec("(unclosed", IsRegex: true),
            out _,
            out var problem));

        Assert.Equal(9, problem.Offset);
        Assert.Equal(
            """Not a valid regular expression: there are more "(" than ")" (position 9).""",
            problem.Sentence);
    }

    /// <summary>
    /// F-04 — every parser error maps to a clause, including any the framework adds later:
    /// none of them may look like the resource key that started this.
    /// </summary>
    [Fact]
    public void NoPatternExplanationLooksLikeAResourceKey()
    {
        foreach (var error in Enum.GetValues<RegexParseError>())
        {
            var sentence = SearchPattern.Explain(error);
            Assert.NotEmpty(sentence);
            Assert.DoesNotMatch(ResourceKeyShaped(), sentence);
        }
    }

    /// <summary>
    /// F-04 — a framework message trimmed to its resource key never reaches a reader. The
    /// product sentence carries a stable code instead, and the raw text goes to diagnostics.
    /// </summary>
    [Fact]
    public void ATrimmedFrameworkMessageIsReplacedByAProductSentence()
    {
        var leaked = WorkspaceViewModel.FriendlyMessage(
            new InvalidOperationException("MakeException, (unclosed, 9, InsufficientClosingParentheses"));

        Assert.DoesNotMatch(ResourceKeyShaped(), leaked);
        Assert.Contains("InvalidOperation", leaked, StringComparison.Ordinal);

        // A message that is already a sentence is still the most useful thing to say.
        Assert.Equal(
            "the session was written by a newer VisualCat",
            WorkspaceViewModel.FriendlyMessage(
                new InvalidOperationException("the session was written by a newer VisualCat")));
    }

    // --------------------------------------------------------------- F-05 ---

    /// <summary>
    /// F-05 — a transient message and the accessible name of the status line move together,
    /// because there is no longer a text property for a view route to poke.
    /// </summary>
    [AvaloniaFact]
    public async Task ATransientStatusReachesTheScreenReaderToo()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();

        fixture.Tab.ReportTransientStatus("Copied 42 characters");
        Dispatcher.UIThread.RunJobs();

        var status = StatusBlock(fixture);
        Assert.Equal("Copied 42 characters", status.Text);
        Assert.Equal("Copied 42 characters", AutomationProperties.GetName(status));
        Assert.Equal("Copied 42 characters", AutomationProperties.GetHelpText(status));
    }

    /// <summary>
    /// F-05 — a message from a query that has since been superseded does not survive the next
    /// one. It used to survive a successful query, a cleared filter, a mode switch, and the
    /// rest of the tab's life.
    /// </summary>
    [AvaloniaFact]
    public async Task ASupersededTransientStatusClearsWhenTheNextQueryLands()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();
        var settled = fixture.Tab.Status;

        fixture.Tab.ReportTransientStatus("Failed · something that is no longer true");
        Assert.True(fixture.Tab.HasTransientStatus);

        fixture.Tab.SearchText = "Worker";
        await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false);
        Dispatcher.UIThread.RunJobs();

        Assert.False(fixture.Tab.HasTransientStatus);
        Assert.Equal(settled, fixture.Tab.Status);
        var status = StatusBlock(fixture);
        Assert.Equal(fixture.Tab.Status, status.Text);
        Assert.Equal(fixture.Tab.Status, AutomationProperties.GetName(status));
    }

    // --------------------------------------------------------------- F-21 ---

    /// <summary>F-21 — the noun agrees with the number, at zero, one, and thousands.</summary>
    [Fact]
    public void CountedNounsAgreeWithTheirNumber()
    {
        Assert.Equal("1 entry", Counted.Entries(1));
        Assert.Equal("2 entries", Counted.Entries(2));
        Assert.Equal("0 entries", Counted.Entries(0));
        Assert.Equal("no entries", Counted.EntriesOrNone(0));
        Assert.Equal("1,984 entries", Counted.Entries(1984));
        Assert.Equal("1 session", Counted.Sessions(1));
        Assert.Equal("46 sessions", Counted.Sessions(46));
    }

    // ---------------------------------------------------------- F-14, F-19 ---

    /// <summary>
    /// F-14 — Session info never renders the raw lifecycle enum, and a live capture is not
    /// described as an import.
    /// </summary>
    [Fact]
    public void NoLifecycleStateIsShownAsARawEnumName()
    {
        string[] internalWords = ["Importing", "Streaming", "SelectingSource", "Empty"];
        foreach (var state in Enum.GetValues<SessionState>())
        {
            var text = SessionCompletionText.State(state, SessionCompletion.Complete);
            Assert.NotEmpty(text);
            Assert.DoesNotContain(text, internalWords);
        }

        Assert.Equal("Capturing", SessionCompletionText.State(SessionState.Streaming, SessionCompletion.Complete));
        Assert.Equal("Reading", SessionCompletionText.State(SessionState.Importing, SessionCompletion.Complete));
        Assert.Equal("Finishing", SessionCompletionText.State(SessionState.Stopping, SessionCompletion.Complete));
    }

    /// <summary>
    /// F-19 — an unfinalized manifest is a partial recovery on every surface: the same fact
    /// produces the Recents word, the Session info row, and the status line.
    /// </summary>
    [Fact]
    public void AnUnfinishedSessionSaysSoEverywhere()
    {
        var partial = SessionCompletionText.Of(finalized: false, workInFlight: false);
        Assert.Equal(SessionCompletion.RecoverablePartial, partial);
        Assert.Equal("interrupted", SessionCompletionText.Outcome(partial));
        Assert.StartsWith("Interrupted", SessionCompletionText.State(SessionState.Importing, partial), StringComparison.Ordinal);
        Assert.StartsWith("Interrupted", SessionCompletionText.OpenedStatus(partial, 1173), StringComparison.Ordinal);
        Assert.Contains(Counted.Entries(1173), SessionCompletionText.OpenedStatus(partial, 1173), StringComparison.Ordinal);

        // A capture this process is running is neither complete nor interrupted.
        Assert.Equal(
            SessionCompletion.InProgress,
            SessionCompletionText.Of(finalized: false, workInFlight: true));
        Assert.Equal(
            SessionCompletion.Complete,
            SessionCompletionText.Of(finalized: true, workInFlight: false));
    }

    // --------------------------------------------------------------- F-01 ---

    /// <summary>
    /// F-01 — the line a screenshot carries names the build, not only the release. It used to
    /// read "VisualCat 2.0.5" on every Release build made after the 2.0.5 tag.
    /// </summary>
    [Fact]
    public void TheIdentityLineNamesTheBuild()
    {
        Assert.StartsWith(ProductInfo.DisplayVersion, ProductInfo.BuildVersion, StringComparison.Ordinal);
        if (ProductInfo.InformationalVersion.Contains('+', StringComparison.Ordinal))
        {
            var revision = ProductInfo.BuildVersion.Split('+', 2)[1];
            Assert.InRange(revision.Length, 1, 7);
            Assert.DoesNotContain('+', revision);
        }
    }

    // ----------------------------------------------- F-18 / F-04 (D-04.0) ---

    /// <summary>
    /// D-04.0 — a refresh that is superseded while it is still queued for the session lock
    /// stops quietly, like one superseded after it has the lock.
    /// </summary>
    /// <remarks>
    /// The pre-lock <c>WaitAsync</c> sat outside the try that makes supersession silent, so
    /// losing the race before reaching the lock threw <see cref="OperationCanceledException"/>
    /// out of <c>RefreshAsync</c> and out of every caller above it. Three overlapping refreshes
    /// are what it takes: the first holds the lock, the second queues behind it, and the third
    /// cancels the second while it is still waiting.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARefreshSupersededWhileQueuedDoesNotThrow()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();

        var holds = fixture.Tab.RefreshAsync();
        var queues = fixture.Tab.RefreshAsync();
        var supersedes = fixture.Tab.RefreshAsync();

        await holds;
        await queues;
        await supersedes;

        // And the tab is still usable afterwards, rather than wedged on the lock.
        await fixture.Tab.RefreshAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, fixture.Tab.MatchesInView);
    }

    /// <summary>
    /// D-04.0 — a caller who cancels is still answered with the cancellation they asked for.
    /// Silence is only for this refresh's own supersession.
    /// </summary>
    [AvaloniaFact]
    public async Task ACallerCancelledRefreshStillReportsCancellation()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Tab.RefreshAsync(cancelled.Token));
    }

    /// <summary>
    /// D-04.0 / F-18 — opening a session never leaves the tab in the loading tense, whatever
    /// the refresh it triggers does.
    /// </summary>
    /// <remarks>
    /// On the third device the startup restore reached the screen with every row bound, the
    /// plot drawn and the final count correct, and the status line still reading
    /// <c>Opening · 58,781 entries</c> — for the life of the tab, because the line that ends
    /// the tense came after a refresh that had thrown. Competing refreshes are started around
    /// the load here so the reload takes that same superseded path.
    /// </remarks>
    [AvaloniaFact]
    public async Task OpeningASessionAlwaysEndsTheOpeningTense()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(FourEntryLog);
        Dispatcher.UIThread.RunJobs();

        var reload = fixture.Tab.LoadSnapshotAsync(final: true);
        var competing = fixture.Tab.RefreshAsync();
        var supersedes = fixture.Tab.RefreshAsync();
        await reload;
        await competing;
        await supersedes;
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(SessionActivity.Opening, fixture.Tab.Activity);
        Assert.DoesNotContain("Opening", fixture.Tab.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// D-04.0 / F-04 — no view turns a framework exception into user-facing text on its own.
    /// </summary>
    /// <remarks>
    /// F-04 was fixed by adding <c>WorkspaceViewModel.FriendlyMessage</c> and using it on the
    /// routes the report named. Five other routes kept interpolating
    /// <c>GetBaseException().Message</c>, including the funnel every shell action fails
    /// through — and one of them painted "The operation was canceled." over a healthy
    /// workspace on the third device. A helper only helps where it is called, so this is the
    /// guard: the raw message may be read for a diagnostic or matched against, never composed
    /// into something a reader sees.
    /// </remarks>
    [Fact]
    public void NoViewComposesUserTextFromAFrameworkException()
    {
        var views = Path.Combine(RepositoryRoot(), "src", "VisualCat.App", "Views");
        Assert.True(Directory.Exists(views), views);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(views, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) ||
                    line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (RawExceptionMessage().IsMatch(line))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Route these through WorkspaceViewModel.FriendlyMessage (finding F-04):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>An exception's own message being read for a person to see.</summary>
    [GeneratedRegex(@"(GetBaseException\(\)|[Ee]xception|error|failure|cause)\.Message\b")]
    private static partial Regex RawExceptionMessage();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VisualCat.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    // --------------------------------------------------------------- F-28 ---

    /// <summary>
    /// F-28 — the plot and the minimap claim their own edge gestures, in the device pixels the
    /// platform speaks, and give them back when the workspace goes away.
    /// </summary>
    /// <remarks>
    /// On the third device, whose navigation is gestural, a drag-to-pan begun in the plot's
    /// outer 49 px was taken by the system's back gesture and left the app for the home
    /// screen. The platform seam is what the Android host uses to be told which rectangles the
    /// app's own gesture must win in; this is the part of that seam a headless build can hold
    /// still — that the right controls are named, and that their rectangles follow the layout.
    /// </remarks>
    [AvaloniaFact]
    public async Task ThePlotAndTheMinimapClaimTheirOwnEdgeGestures()
    {
        var published = new List<IReadOnlyList<PixelRect>>();
        Platform.PlatformSourceRegistry.SetGestureExclusions = rectangles => published.Add(rectangles);
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            // A phone-shaped viewport: 393 x 777 dp is the third device's own portrait
            // configuration, where the plot runs to 12 dp of both edges.
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 393,
                height: 777);
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(published);
            var claimed = published[^1];
            var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
            var minimap = fixture.View.GetLogicalDescendants().OfType<MinimapControl>().Single();

            foreach (var control in new Control[] { timeline, minimap })
            {
                Assert.True(control.IsEffectivelyVisible, control.GetType().Name);
                Assert.Contains(claimed, rectangle => Covers(rectangle, control, fixture.Window));
            }

            // Only those two. The exclusion budget is finite and Back has to keep working
            // everywhere else on the screen.
            Assert.Equal(2, claimed.Count);

            Platform.EdgeGestureGuard.Reset();
            Assert.Empty(published[^1]);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            Platform.EdgeGestureGuard.Reset();
            Platform.PlatformSourceRegistry.SetGestureExclusions = null;
        }
    }

    /// <summary>Whether a claimed rectangle is exactly where a control is, in device pixels.</summary>
    private static bool Covers(PixelRect rectangle, Control control, TopLevel root)
    {
        var origin = control.TranslatePoint(default, root);
        if (origin is not { } topLeft)
        {
            return false;
        }

        var scale = root.RenderScaling <= 0 ? 1 : root.RenderScaling;
        return rectangle.X <= (int)Math.Floor(topLeft.X * scale) &&
               rectangle.Y <= (int)Math.Floor(topLeft.Y * scale) &&
               rectangle.Right >= (int)Math.Ceiling((topLeft.X + control.Bounds.Width) * scale) &&
               rectangle.Bottom >= (int)Math.Ceiling((topLeft.Y + control.Bounds.Height) * scale) &&
               rectangle.Width > 0 &&
               rectangle.Height > 0;
    }

    // --------------------------------------------------------- F-29 / F-30 ---

    /// <summary>
    /// F-30 — the query row is the drawer's own band, not the scroller's first child, so no
    /// viewport can make it the thing that scrolls away or gets sliced.
    /// </summary>
    /// <remarks>
    /// A landscape keyboard on the third device leaves the whole drawer 93 dp. With the query
    /// row inside the scroller it was clipped to 32 dp of its 48, cut across the middle, with
    /// Regex and Case-sensitive sliced beside it. Moving it out only while the keyboard was up
    /// was worse still: reparenting a focused TextBox unmounts it, drops focus, and makes
    /// Avalonia withdraw the IME it had just asked for — the field could not be typed into at
    /// all. So the band is structural, and this is the test that says so.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheQueryRowIsNeverInsideTheDrawerScroller()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 393,
                height: 341);
            Filters(fixture).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var field = fixture.View.GetLogicalDescendants()
                .OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Search message text or regular expression");
            var scroller = fixture.View.GetLogicalDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault(view => view.GetLogicalDescendants().OfType<CheckBox>()
                    .Any(box => Equals(box.Content, "Regex")));

            Assert.Null(scroller);
            Assert.True(field.Bounds.Height >= 48, $"the query field is {field.Bounds.Height:0.#} dp tall");
            Assert.True(field.Bounds.Width >= 96, $"the query field is {field.Bounds.Width:0.#} dp wide");
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// F-29 — the drawer's Clear action meets the same 48 dp floor as every other touch
    /// target. It measured 30.5 dp on the third device: one glyph wide, and never audited,
    /// because F-03's and F-26's sweeps were run on panes that do not contain it.
    /// </summary>
    [AvaloniaFact]
    public async Task TheDrawerClearActionMeetsTheTouchFloor()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 393,
                height: 777);
            Filters(fixture).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var clear = fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Clear the query");

            Assert.True(clear.Bounds.Width >= 48, $"Clear is {clear.Bounds.Width:0.#} dp wide");
            Assert.True(clear.Bounds.Height >= 48, $"Clear is {clear.Bounds.Height:0.#} dp tall");
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// F-31 — a number field's spin buttons meet the same 48 dp floor as everything else a
    /// thumb lands on, on every field in the product.
    /// </summary>
    /// <remarks>
    /// <c>StretchForTouch</c> gave the <see cref="NumericUpDown"/> container its 48 dp and the
    /// container measured 48 dp, so a sweep that read the container passed. The spin buttons
    /// are template parts inside it, inset by its border: twelve of them across the Appearance
    /// and Session cache sheets measured 34.0 × 46.0 dp on the fourth device pass — the last
    /// controls in the product under the floor, and 34 dp is narrower than the 30.5 dp button
    /// F-29 fixed. The assertion is on the shared seam every numeric field already goes
    /// through, so an eighth field cannot omit it.
    /// </remarks>
    [AvaloniaFact]
    public void ANumberFieldsSpinButtonsMeetTheTouchFloor()
    {
        TouchTarget.TouchOverride = true;
        try
        {
            var field = new NumericUpDown { Minimum = 0, Maximum = 60, Value = 30, Width = 180 };
            var window = new Window { Width = 393, Height = 777, Content = field };
            window.Show();
            SheetForm.PrepareSpinButtons(field, "live UI refresh limit in hertz");

            // The buttons are three templates deep and appear on a layout pass, not on
            // TemplateApplied — which is the whole reason PrepareSpinButtons waits for one.
            for (var pass = 0; pass < 4; pass++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            var spinners = field.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => AutomationProperties.GetName(button) is { Length: > 0 } name &&
                                 name.EndsWith("live UI refresh limit in hertz", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(2, spinners.Length);
            foreach (var spinner in spinners)
            {
                var name = AutomationProperties.GetName(spinner);
                Assert.True(spinner.Bounds.Width >= 48, $"{name} is {spinner.Bounds.Width:0.#} dp wide");
                Assert.True(spinner.Bounds.Height >= 48, $"{name} is {spinner.Bounds.Height:0.#} dp tall");
            }
        }
        finally
        {
            TouchTarget.TouchOverride = null;
        }
    }

    /// <summary>
    /// F-32 - a compact-height workspace only merges the capture controls into the shell row
    /// where the width can actually hold them, so Stop capture is never laid out past the
    /// edge of a narrow screen.
    /// </summary>
    /// <remarks>
    /// Compact height is chosen by height alone, and the merge it performed assumed - in its
    /// own comment - that "a short viewport has width to spare". That is true of the 780 and
    /// 801 dp landscape viewports it was built for and false of a 360 dp portrait workspace,
    /// which reaches compact height too whenever something above it is tall: a notice, or
    /// split-screen. On the device, Stop capture measured 15.0 dp there against 97.3 dp with
    /// the notice dismissed, and two taps at its centre did nothing.
    /// </remarks>
    [AvaloniaFact]
    public async Task StopCaptureStaysOnScreenInAShortNarrowWorkspace()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await using var tab = new SessionTabViewModel("Live", root) { IsLiveCaptureActive = true };
            tab.ReportActivity(SessionActivity.Capturing, "Capturing · 218 entries");

            var view = new SessionWorkspaceView(tab);

            // Short enough to select compact height, and as narrow as a portrait phone.
            var window = new Window { Content = view, Width = 360, Height = 340 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var stop = view.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Stop capture"));

                Assert.True(stop.IsVisible);
                Assert.True(
                    stop.Bounds.Width >= 48,
                    $"Stop capture is {stop.Bounds.Width:0.#} dp wide in a 360 dp workspace");

                // And it is inside the workspace, not laid out past its right edge.
                var right = stop.TranslatePoint(new Point(stop.Bounds.Width, 0), view);
                Assert.NotNull(right);
                Assert.True(
                    right!.Value.X <= view.Bounds.Width + 0.5,
                    $"Stop capture ends at {right.Value.X:0.#} dp in a {view.Bounds.Width:0.#} dp workspace");
            }
            finally
            {
                window.Close();
                Directory.Delete(root, recursive: true);
            }
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// F-32, the other half - a short viewport that really is wide keeps the merged row that
    /// §6's compact-height work built for it, so this fix costs landscape nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task AWideShortWorkspaceStillMergesTheCaptureRow()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            await using var tab = new SessionTabViewModel("Live", root) { IsLiveCaptureActive = true };
            tab.ReportActivity(SessionActivity.Capturing, "Capturing · 218 entries");

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 801, Height = 341 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var captureRow = view.GetLogicalDescendants()
                    .OfType<Grid>()
                    .Single(grid => AutomationProperties.GetName(grid) == "Live capture controls");
                var quickActions = view.GetLogicalDescendants()
                    .OfType<Grid>()
                    .Single(grid => AutomationProperties.GetName(grid) == "Filters and workspace mode");

                // Merged means "beside", which is the same row and a later column.
                Assert.Equal(Grid.GetRow(quickActions), Grid.GetRow(captureRow));
                Assert.True(Grid.GetColumn(captureRow) > Grid.GetColumn(quickActions));

                var stop = view.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Stop capture"));
                Assert.True(stop.Bounds.Width >= 48, $"Stop capture is {stop.Bounds.Width:0.#} dp wide");
            }
            finally
            {
                window.Close();
                Directory.Delete(root, recursive: true);
            }
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// F-33 - a notice too long for its lane keeps its whole message, in a lane whose height
    /// is bounded, so what does not fit is a scroll away rather than gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lane has to stay short, because its height comes out of the workspace's (F-32). It
    /// bought that with <c>MaxLines = 6</c> and no trimming, so on a 360 dp phone the
    /// declined-consent notice was cut mid-clause and the words that never appeared were the
    /// remedy it exists to deliver - "Tap Live again and choose the option that allows
    /// access." The accessible name always carried the whole string, so a screen reader heard
    /// what the eye could not reach.
    /// </para>
    /// <para>
    /// This asserts the shape of the fix, which is the part that regressed: no line cap on
    /// the text, and a height-bounded scroller around it. The lane itself is Android-only and
    /// stays hidden in a headless desktop run, so how it looks at 360 dp is verified on the
    /// device. Forcing the lane visible in a desktop composition instead hangs the layout -
    /// worth a comment here rather than rediscovering it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task ALongNoticeKeepsItsWholeMessageInABoundedLane()
    {
        const string message =
            "Only VisualCat's own log lines are being captured - log access was not allowed." +
            "\n\nAndroid asks for permission to read the device log on every capture, and this " +
            "one was not allowed, so the capture can only see VisualCat's own log lines. " +
            "Tap Live again and choose the option that allows access.";

        await using var view = new MainView();
        view.ShowNotice(message);

        var text = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(block => AutomationProperties.GetName(block) == "Application status message");

        // The whole message, and no cap that would draw only the start of it.
        Assert.Equal(message, text.Text);
        Assert.EndsWith("allows access.", text.Text, StringComparison.Ordinal);
        Assert.Equal(0, text.MaxLines);

        // Inside a scroller, so the overflow is reachable; bounded, so the lane cannot take
        // the workspace's height in order to show it.
        var scroller = text.GetLogicalAncestors().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scroller);
        Assert.True(
            double.IsFinite(scroller!.MaxHeight) && scroller.MaxHeight > 0,
            $"the notice scroller's MaxHeight is {scroller.MaxHeight}");
        Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
    }

    /// <summary>
    /// F-32, the third half - a short workspace only lays the plot and the analysis pane out
    /// side by side where the width can hold two of them.
    /// </summary>
    /// <remarks>
    /// Two columns of a 360 dp portrait workspace left the analysis pane about 131 dp, and
    /// its actions were clipped with it: "Show the full message of the selected entry"
    /// measured 12.3 dp there against 64.0 dp in Details on the same screen and the same
    /// notice. Below the threshold the two stack, which is what an ordinary portrait
    /// workspace already does.
    /// </remarks>
    [AvaloniaFact]
    public async Task AShortNarrowWorkspaceStacksThePlotAndThePane()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var narrow = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 360,
                height: 340);
            narrow.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var narrowRoot = narrow.View.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.ColumnDefinitions.Count > 0);
            Assert.Single(narrowRoot.ColumnDefinitions);

            await using var wide = await LiveTestWorkspaceFixture.CreateAsync(
                FourEntryLog,
                width: 801,
                height: 341);
            wide.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // The landscape composition §6 and §7 built is untouched.
            var wideRoot = wide.View.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.ColumnDefinitions.Count > 0);
            Assert.Equal(2, wideRoot.ColumnDefinitions.Count);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    private static Button Filters(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => AutomationProperties.GetName(button) == "Open search and timeline filters");

    /// <summary>
    /// F-16 (second half) — the name this product gives its own captures fits a phone tab
    /// whole, so the ellipsis never eats the word that says what the session is.
    /// </summary>
    /// <remarks>
    /// F-16's first half stopped capture names reading as dates. The tab strip still cut
    /// them: at a 24-character budget the 25-character <c>On-device logcat HHhMMmSS</c>
    /// rendered as <c>On-device log…t 03h45m47</c> on every phone, every time. The report
    /// named that too ("hides the word that identifies the source") and only the naming half
    /// was fixed.
    /// </remarks>
    [Fact]
    public void APhoneTabShowsAGeneratedCaptureNameWhole()
    {
        var generated = SourceMetadata.NameCaptureStartedNow("On-device logcat");
        var shown = TabTitle.Shorten(generated, TabTitle.MobileBudget);

        Assert.Equal(generated, shown);
        Assert.DoesNotContain('…', shown);
        Assert.Contains("logcat", shown, StringComparison.Ordinal);

        // And a name that genuinely does not fit still keeps both of its ends.
        var long_ = TabTitle.Shorten("northlight-transit-20260812-evening.txt", TabTitle.MobileBudget);
        Assert.Contains('…', long_);
        Assert.StartsWith("northlight", long_, StringComparison.Ordinal);
        Assert.EndsWith(".txt", long_, StringComparison.Ordinal);
    }

    /// <summary>The signature of a trimmed framework resource string.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*,(\s|$)")]
    private static partial Regex ResourceKeyShaped();

    private static TextBlock StatusBlock(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .First(block => AutomationProperties.GetName(block) == fixture.Tab.Status);

    private static Point PointIn(Visual control, Visual root)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            root);
        Assert.NotNull(point);
        return point.Value;
    }
}
