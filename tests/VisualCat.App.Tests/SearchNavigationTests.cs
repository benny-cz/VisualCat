using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Core.Query;
using VisualCat.Domain;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

/// <summary>
/// Search navigation selects an exact record, and says which one out of how many.
/// </summary>
/// <remarks>
/// The stepper used to move the viewport and then describe whatever match happened to be
/// nearest its middle. At Fit that was a no-op — the view already spanned every match — and
/// the counter's total came from a marker list capped at 20,000 timestamps. These tests are
/// about the record that is selected, not about where the plot ended up.
/// </remarks>
public sealed class SearchNavigationTests
{
    /// <summary>Three matches at seconds 0, 10 and 20, which is the plan's Fit fixture.</summary>
    private const string ThreeMatchLog =
        "01-01 00:00:00.000000   100   101 I Worker         : needle one\n" +
        "01-01 00:00:10.000000   100   101 I Worker         : needle two\n" +
        "01-01 00:00:20.000000   100   101 I Worker         : needle three\n";

    [AvaloniaFact]
    public async Task AtFitTheStepperSelectsAnExactRecordAndOpensAReadableWindow()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        var session = fixture.Tab.Snapshot!.TimedRange!.Value;
        await fixture.Tab.SetViewportAsync(session);
        await ApplySearchAsync(fixture, "needle");

        // Fit: the viewport already spans every match, so recentring cannot move it. What
        // must change is which record is selected.
        Assert.Equal(session, fixture.Tab.Viewport);
        var next = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next);

        Assert.NotNull(next);
        Assert.Equal(SearchMatchStatus.Found, next.Status);
        Assert.Equal(3, next.TotalMatches);
        var arrival = fixture.Tab.Viewport!.Value;
        Assert.True(
            arrival.DurationUs < session.DurationUs,
            "arriving from a fitted plot must open a window narrower than the whole session");
        Assert.True(
            arrival.StartInclusive.Value <= next.Key!.Value.TimestampUs &&
            next.Key.Value.TimestampUs < arrival.EndExclusive.Value,
            "the selected record must be inside the window the arrival opened");
    }

    [AvaloniaFact]
    public async Task FirstAndLastSelectTheEndsAndWrapThroughThem()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");

        var first = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        Assert.Equal(0, first!.Key!.Value.SourceSequence);
        Assert.Equal(1, first.Ordinal);

        var last = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
        Assert.Equal(2, last!.Key!.Value.SourceSequence);
        Assert.Equal(3, last.Ordinal);

        var wrapped = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next);
        Assert.Equal(0, wrapped!.Key!.Value.SourceSequence);
        Assert.Equal(1, wrapped.Ordinal);
    }

    /// <summary>
    /// A single-timestamp session keeps its usable two-second window rather than an empty one.
    /// </summary>
    [AvaloniaFact]
    public async Task ASingleTimestampSessionArrivesInAUsableWindow()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
            "01-01 00:00:00.000000   100   101 I Worker         : needle one\n" +
            "01-01 00:00:00.000000   100   101 I Worker         : needle two\n");
        await ApplySearchAsync(fixture, "needle");

        var selected = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);

        Assert.Equal(2, selected!.Ordinal);
        var viewport = fixture.Tab.Viewport!.Value;
        Assert.False(viewport.IsEmpty);
        Assert.True(viewport.DurationUs >= SessionTabViewModel.MinimumViewportUs);
    }

    /// <summary>The arrival viewport is pure arithmetic, so its edges are testable directly.</summary>
    [Fact]
    public void TheArrivalWindowClampsToTheSessionWithoutOverflowing()
    {
        var session = new TimeRange(new InstantUs(0), new InstantUs(200_000_000));

        // Fitted: a twentieth of the session, centred, clamped at the start.
        var atStart = SessionTabViewModel.SearchArrivalViewport(session, session, new InstantUs(0));
        Assert.Equal(0, atStart.StartInclusive.Value);
        Assert.Equal(10_000_000, atStart.DurationUs);

        var atEnd = SessionTabViewModel.SearchArrivalViewport(
            session,
            session,
            new InstantUs(200_000_000 - 1));
        Assert.Equal(200_000_000, atEnd.EndExclusive.Value);
        Assert.Equal(10_000_000, atEnd.DurationUs);

        // A zoom the reader chose is preserved rather than replaced by the arrival span.
        var zoomed = new TimeRange(new InstantUs(50_000_000), new InstantUs(52_000_000));
        var kept = SessionTabViewModel.SearchArrivalViewport(session, zoomed, new InstantUs(120_000_000));
        Assert.Equal(zoomed.DurationUs, kept.DurationUs);
        Assert.Equal(119_000_000, kept.StartInclusive.Value);
    }

    [AvaloniaFact]
    public async Task ManualPanClearsTheCursorWithoutChangingTheSearch()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");
        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        Assert.NotNull(fixture.Tab.SelectedSearchMatch);

        var viewport = fixture.Tab.Viewport!.Value;
        await fixture.Tab.SetViewportAsync(
            new TimeRange(
                new InstantUs(viewport.StartInclusive.Value + 1_000),
                new InstantUs(viewport.EndExclusive.Value + 1_000)),
            manual: true);

        Assert.Null(fixture.Tab.SelectedSearchMatch);
        Assert.NotNull(fixture.Tab.AppliedFilter.Search);
        Assert.Equal(3, fixture.Tab.SearchResult!.Matches);
        Assert.Equal("– / 3", CounterText(fixture));
    }

    /// <summary>
    /// A match past the first loaded page is revealed, and the footer accounts for the rest.
    /// </summary>
    [AvaloniaFact]
    public async Task AMatchBeyondThePageIsSelectedAndTheFooterAccountsForBothSides()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(1_200));
        await ApplySearchAsync(fixture, "needle");

        var last = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);

        Assert.Equal(1_200, last!.Ordinal);
        Assert.True(fixture.Tab.IsEntryArrivalWindow);
        Assert.True(
            fixture.Tab.EarlierEntryCount > SessionTabViewModel.EntryPageSize,
            "the arrival window must start past the ordinary first page");
        Assert.Contains(
            fixture.Tab.Entries,
            entry => entry.SourceSequence == last.Key!.Value.SourceSequence);

        // Shown + earlier + later is the whole range, with nothing counted twice.
        Assert.Equal(
            fixture.Tab.MatchesInView,
            fixture.Tab.EarlierEntryCount + fixture.Tab.LoadedEntryCount + fixture.Tab.RemainingEntryCount);

        await fixture.Tab.ReturnToEntryRangeStartAsync();
        Assert.False(fixture.Tab.IsEntryArrivalWindow);
        Assert.Equal(0, fixture.Tab.EarlierEntryCount);
    }

    [AvaloniaFact]
    public async Task TheThreeShortcutsReachTheSameExactNavigationAsTheButtons()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");

        SearchMatchPromptModel? asked = null;
        fixture.View.AskForMatchAsync = (model, _) =>
        {
            asked = model;
            return Task.FromResult(model.TryConfirm("2", out var ordinal) ? ordinal : (long?)null);
        };

        Assert.True(fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.End, KeyModifiers.Alt)));
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SelectedSearchMatch?.Ordinal == 3);

        Assert.True(fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.Home, KeyModifiers.Alt)));
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SelectedSearchMatch?.Ordinal == 1);

        Assert.True(fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.G, KeyModifiers.Control)));
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SelectedSearchMatch?.Ordinal == 2);
        Assert.NotNull(asked);
        Assert.Equal(3, asked.Maximum);

        // The timeline's own unmodified Home and End still mean the session's ends.
        Assert.False(fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.Home, KeyModifiers.None)));
        Assert.False(fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.End, KeyModifiers.None)));
    }

    [AvaloniaFact]
    public async Task WithNoMatchesTheShortcutsAreInertAndTheCounterSaysWhy()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "haystack");
        Assert.Equal(0, fixture.Tab.SearchResult!.Matches);

        var asked = false;
        fixture.View.AskForMatchAsync = (_, _) =>
        {
            asked = true;
            return Task.FromResult<long?>(null);
        };
        fixture.View.UpdateMarkerNavigationForTest();

        // The keys are still claimed — they belong to the workspace — but nothing happens,
        // and the reason is already spoken by the controls they stand for.
        fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.G, KeyModifiers.Control));
        fixture.View.TryHandleShortcut(Key(Avalonia.Input.Key.Home, KeyModifiers.Alt));
        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => true, passes: 10);

        Assert.False(asked);
        Assert.Null(fixture.Tab.SelectedSearchMatch);
        Assert.Equal("No matches", CounterText(fixture));
    }

    /// <summary>
    /// The prompt is valid against the applied result, not against a control's clamped value.
    /// </summary>
    [Fact]
    public void ThePromptRefusesBlankFractionalAndOutOfRangeTextWithoutClamping()
    {
        var model = new SearchMatchPromptModel(Identity("filter-a"), 3);

        Assert.False(model.TryReadOrdinal(null, out _));
        Assert.False(model.TryReadOrdinal("  ", out _));
        Assert.False(model.TryReadOrdinal("1.5", out _));
        Assert.False(model.TryReadOrdinal("0", out _));
        Assert.False(model.TryReadOrdinal("4", out _));
        Assert.False(model.TryReadOrdinal("99999999999999999999", out _));
        Assert.True(model.TryReadOrdinal("3", out var ordinal));
        Assert.Equal(3, ordinal);
        Assert.Equal("Enter a match number from 1 to 3.", model.ValidationMessage);
    }

    [AvaloniaFact]
    public void ThePromptKeepsFractionalTextVisibleUntilValidationReadsIt()
    {
        using var dialog = new NumberPromptDialog(
            "Go to match",
            "Which match?",
            1,
            new SearchMatchPromptModel(Identity("filter-a"), 3));
        var host = new Window { Content = dialog, Width = 420, Height = 280 };
        host.Show();
        try
        {
            dialog.NotifyPresented();
            var input = dialog.GetLogicalDescendants().OfType<NumericUpDown>().Single();
            var go = dialog.GetLogicalDescendants().OfType<Button>().Single(static button => Equals(button.Content, "Go"));

            input.Text = "1.5";
            input.Focus();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // NumericUpDown commits before a tapped button raises Click. A plain "0" format
            // rounds 1.5 to 2 at this focus boundary, so the validator never sees the rejected
            // input. Exercise that real boundary rather than only asserting a format string.
            go.Focus();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("1.5", input.Text);

            go.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.Completion.IsCompleted);
            Assert.Equal("Enter a match number from 1 to 3.", dialog.ValidationForTest);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void APromptWhoseTotalFallsToZeroStaysOpenWithoutContradictoryBounds()
    {
        var identity = Identity("filter-a");
        var model = new SearchMatchPromptModel(identity, 7);
        Assert.True(model.CanConfirm);

        model.Apply(identity with { SnapshotGeneration = 2 }, 0, pending: false);

        Assert.False(model.CanConfirm);
        Assert.False(model.HasMatches);
        Assert.Equal("No matches in the current capture.", model.RangeText);
        Assert.False(model.TryReadOrdinal("1", out _));

        // A later positive total restores validation without rewriting the typed text.
        model.Apply(identity with { SnapshotGeneration = 3 }, 5, pending: false);
        Assert.True(model.CanConfirm);
        Assert.True(model.TryReadOrdinal("5", out _));
    }

    [Fact]
    public void AChangedSearchClosesThePromptInsteadOfRenumberingIt()
    {
        var model = new SearchMatchPromptModel(Identity("filter-a"), 7);
        string? reason = null;
        model.CloseRequested += value => reason = value;

        model.Apply(Identity("filter-b"), 12, pending: false);

        Assert.Equal("Search changed. Open Go to match again.", reason);
        Assert.Equal(7, model.Maximum);
    }

    [Fact]
    public void ConfirmingOnceCarriesTheDisplayedIdentityAndBlocksASecondDispatch()
    {
        var identity = Identity("filter-a");
        var model = new SearchMatchPromptModel(identity, 7);

        Assert.True(model.TryConfirm("4", out var ordinal));
        Assert.Equal(4, ordinal);
        Assert.Equal(new SearchMatchPromptSelection(4, identity), model.Selection);

        // Repeated Enter must not start a second navigation transaction.
        Assert.False(model.TryConfirm("5", out _));
        Assert.Equal(4, model.Selection!.Ordinal);
    }

    [Fact]
    public async Task ConfirmationPinsTheResolvedExactKeyBesideTheDisplayedIdentity()
    {
        var identity = Identity("filter-a");
        var expected = new SearchMatchKey(Guid.NewGuid(), 123_000, 47);
        SearchMatchPromptSelection? requested = null;
        var model = new SearchMatchPromptModel(
            identity,
            7,
            (selection, token) =>
            {
                token.ThrowIfCancellationRequested();
                requested = selection;
                return Task.FromResult<SearchMatchKey?>(expected);
            });

        Assert.True(model.TryConfirm("4", out _));
        Assert.True(await model.ResolveSelectionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new SearchMatchPromptSelection(4, identity), requested);
        Assert.Equal(new SearchMatchPromptSelection(4, identity, expected), model.Selection);
        Assert.False(model.TryConfirm("5", out _));
    }

    /// <summary>
    /// A query the view model published is displayed, not re-applied.
    /// </summary>
    /// <remarks>
    /// The field echoes <c>SearchText</c> so a saved view or a cleared filter shows the query
    /// in force. That echo used to queue the field's own debounced apply, carrying the field's
    /// Regex and Match case toggles: a stored regular expression came back as literal text,
    /// and a rejected pattern's rollback was undone a moment after it happened.
    /// </remarks>
    [AvaloniaFact]
    public async Task AQueryPushedFromTheViewModelIsDisplayedRatherThanReApplied()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");
        var applied = fixture.Tab.AppliedFilter;

        // A regular expression the reader never typed into this field, pushed by the model.
        fixture.Tab.SearchText = "needle (one|two)";
        await fixture.Tab.ApplySearchAsync(regex: true, caseSensitive: false);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.AppliedFilter.Search?.IsRegex == true);

        // Well past the field's own 320 ms debounce: nothing may re-apply it as literal text.
        for (var pass = 0; pass < 40; pass++)
        {
            PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, static () => true, passes: 4);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(fixture.Tab.AppliedFilter.Search?.IsRegex);
        Assert.Equal("needle (one|two)", fixture.Tab.AppliedFilter.Search?.Query);
        Assert.NotEqual(applied.Fingerprint(), fixture.Tab.AppliedFilter.Fingerprint());
    }

    /// <summary>
    /// Panning re-reads the plot, not the search. Twenty pans cost no extra whole-session work.
    /// </summary>
    /// <remarks>
    /// The search used to be rerun by every refresh, so every wheel notch rebuilt the text
    /// predicate for the whole session and rescanned it for markers. Counters rather than a
    /// stopwatch: this is about which work happens, not how long it takes on this machine.
    /// </remarks>
    [AvaloniaFact]
    public async Task TwentyPansAfterAColdSearchBuildNoFurtherWholeSessionWork()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(600));
        await ApplySearchAsync(fixture, "needle");
        var fingerprint = fixture.Tab.AppliedFilter.Fingerprint();
        var session = fixture.Tab.Snapshot!.TimedRange!.Value;

        // Counting starts only once the cold search has settled: the first build is the one
        // this cache exists to make unrepeatable, not the one it is supposed to avoid.
        var builds = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        foreach (var segment in fixture.Tab.Snapshot.Segments)
        {
            segment.BitmapFactoryStartedForTests = key =>
                builds.AddOrUpdate(key, 1, static (_, count) => count + 1);
        }

        var markers = fixture.Tab.SearchResult!.Markers;
        var span = Math.Max(SessionTabViewModel.MinimumViewportUs, session.DurationUs / 8);
        for (var pan = 0; pan < 20; pan++)
        {
            var start = session.StartInclusive.Value + (pan * 1_000);
            await fixture.Tab.SetViewportAsync(
                new TimeRange(new InstantUs(start), new InstantUs(start + span)),
                manual: true);
        }

        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => !fixture.Tab.IsQueryPending);

        Assert.Equal(0, builds.GetValueOrDefault(fingerprint));
        Assert.Equal(600, fixture.Tab.SearchResult!.Matches);

        // And the marker set the plot draws from is the one already published, not a new scan.
        Assert.Same(markers, fixture.Tab.SearchResult.Markers);
    }

    /// <summary>
    /// Stepping past the ends keeps advancing even where the viewport cannot follow.
    /// </summary>
    /// <remarks>
    /// At the first and last match the arrival window is already clamped against the session
    /// bounds, so centring it changes nothing. Selection is not the viewport: the reader must
    /// still move from match 1 to match 2 and wrap from the last back to the first.
    /// </remarks>
    [AvaloniaFact]
    public async Task SteppingAtBothSessionBoundariesStillAdvancesTheSelection()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");

        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        var atStart = fixture.Tab.Viewport;
        var second = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next);
        Assert.Equal(2, second!.Ordinal);

        // Back to the first, then previous: wrapping to the far end from a clamped start.
        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        Assert.Equal(atStart, fixture.Tab.Viewport);
        var wrappedBack = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Previous);
        Assert.Equal(3, wrappedBack!.Ordinal);

        var beforeLastStep = fixture.Tab.Viewport;
        var wrappedForward = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next);
        Assert.Equal(1, wrappedForward!.Ordinal);
        Assert.NotEqual(beforeLastStep, fixture.Tab.Viewport);
    }

    /// <summary>Source order shows the arriving record too; only the neighbours differ.</summary>
    [AvaloniaFact]
    public async Task BothEntryOrdersRevealTheSelectedRecord()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(1_200));
        await ApplySearchAsync(fixture, "needle");

        foreach (var order in new[] { EntryOrder.Chronological, EntryOrder.SourceSequence })
        {
            await fixture.Tab.SetEntryOrderAsync(order);
            var target = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Ordinal, ordinal: 900);

            Assert.Equal(900, target!.Ordinal);
            Assert.Contains(
                fixture.Tab.Entries,
                entry => entry.SourceSequence == target.Key!.Value.SourceSequence);
            Assert.True(
                fixture.Tab.IsEntryArrivalWindow,
                $"{order} did not arrive in a window around the selected record");
        }
    }

    /// <summary>One match and no match are both valid, distinct, complete states.</summary>
    [AvaloniaFact]
    public async Task OneMatchAndNoMatchAreBothCompleteStates()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);

        await ApplySearchAsync(fixture, "needle three");
        Assert.Equal(1, fixture.Tab.SearchResult!.Matches);
        var only = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        Assert.Equal(1, only!.Ordinal);
        Assert.Equal("1 / 1", CounterText(fixture));

        // Next and Previous from the only match both wrap back onto it.
        Assert.Equal(1, (await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next))!.Ordinal);
        Assert.Equal(1, (await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Previous))!.Ordinal);

        await ApplySearchAsync(fixture, "haystack");
        Assert.Equal(0, fixture.Tab.SearchResult!.Matches);
        Assert.Null(fixture.Tab.SelectedSearchMatch);
        Assert.Equal("No matches", CounterText(fixture));

        // A typed answer, not a silent one, and nothing becomes selected because of it.
        var refused = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
        Assert.Equal(SearchMatchStatus.NoMatches, refused!.Status);
        Assert.Null(refused.Key);
        Assert.Null(fixture.Tab.SelectedSearchMatch);
    }

    /// <summary>
    /// A replaced search never leaves the previous search's selection on screen.
    /// </summary>
    /// <remarks>
    /// The cursor belongs to one applied query. Carrying it across a filter change would
    /// number a record against a search it is not a match for.
    /// </remarks>
    [AvaloniaFact]
    public async Task ReplacingTheSearchDropsTheSelectionRatherThanRenumberingIt()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");
        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
        Assert.Equal(3, fixture.Tab.SelectedSearchMatch!.Ordinal);

        await ApplySearchAsync(fixture, "needle t");

        Assert.Null(fixture.Tab.SelectedSearchMatch);
        Assert.Equal(2, fixture.Tab.SearchResult!.Matches);
        Assert.Equal("– / 2", CounterText(fixture));
    }

    /// <summary>The three shortcuts belong to the workspace, including from the search field.</summary>
    [AvaloniaFact]
    public async Task TheShortcutsActWhileTheSearchFieldHasFocus()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");

        var search = fixture.View.GetLogicalDescendants()
            .OfType<TextBox>()
            .First(static box => box.PlaceholderText?.Contains("Search", StringComparison.Ordinal) == true);
        search.Focus();
        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => search.IsFocused);

        // Every one carries a modifier, which is the existing rule for a key that still fires
        // while a text field has focus.
        Assert.True(fixture.View.TryHandleShortcut(FromSearch(search, Avalonia.Input.Key.End, KeyModifiers.Alt)));
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SelectedSearchMatch?.Ordinal == 3);
        Assert.True(search.IsFocused, "the field must keep focus so typing can continue");

        Assert.True(fixture.View.TryHandleShortcut(FromSearch(search, Avalonia.Input.Key.Home, KeyModifiers.Alt)));
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SelectedSearchMatch?.Ordinal == 1);

        // A bare letter is a letter the reader is typing, not a command.
        Assert.False(fixture.View.TryHandleShortcut(FromSearch(search, Avalonia.Input.Key.N, KeyModifiers.None)));
    }

    /// <summary>
    /// A live republication of the same selection does not re-reveal it.
    /// </summary>
    /// <remarks>
    /// The arrival counter is what separates "the reader stepped here" from "the capture grew
    /// and the same record was published again". Without it a growing capture would drag the
    /// phone back to Entries, re-announce the position and re-scroll the list every few
    /// seconds at a reader who has not moved.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARepublishedSelectionIsNotAFreshArrival()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ThreeMatchLog);
        await ApplySearchAsync(fixture, "needle");

        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
        var afterArrival = fixture.Tab.SearchArrivalGeneration;
        Assert.True(afterArrival > 0);

        // A refresh that revalidates the same key is not an arrival.
        await fixture.Tab.RefreshAsync(TestContext.Current.CancellationToken);
        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => !fixture.Tab.IsQueryPending);

        Assert.Equal(afterArrival, fixture.Tab.SearchArrivalGeneration);
        Assert.Equal(3, fixture.Tab.SelectedSearchMatch!.Ordinal);

        // Stepping again is.
        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Previous);
        Assert.True(fixture.Tab.SearchArrivalGeneration > afterArrival);
    }

    /// <summary>
    /// Start of range is offered exactly while an arrival window is hiding earlier rows.
    /// </summary>
    [AvaloniaFact]
    public async Task StartOfRangeIsOfferedOnlyWhileEarlierRowsAreHidden()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(1_200));
        await ApplySearchAsync(fixture, "needle");
        Assert.Empty(StartOfRangeActions(fixture));

        await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.IsEntryArrivalWindow && StartOfRangeActions(fixture).Length > 0);

        var offered = Assert.Single(StartOfRangeActions(fixture));
        Assert.Equal("Start of range", offered.Label);
        Assert.Contains("earlier rows are not shown", offered.Description, StringComparison.Ordinal);

        await fixture.Tab.ReturnToEntryRangeStartAsync();
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => !fixture.Tab.IsEntryArrivalWindow && StartOfRangeActions(fixture).Length == 0);
        Assert.Empty(StartOfRangeActions(fixture));
    }

    /// <summary>
    /// A late match is marked and reachable even when the marker list stopped long before it.
    /// </summary>
    /// <remarks>
    /// The lane used to draw <c>SearchResult.Markers</c>, which stops at 20,000 timestamps —
    /// so on a long search everything after the cap was unmarked and unclickable. Presence now
    /// comes from the search-filtered heat map for the visible range, and a click resolves the
    /// nearest real match through the exact query rather than through the list.
    /// </remarks>
    [AvaloniaFact]
    public async Task ALateMatchIsMarkedAndClickableThoughTheMarkerListStoppedBeforeIt()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(LateMatchLog(20_050));
        await ApplySearchAsync(fixture, "needle");

        var search = fixture.Tab.SearchResult!;
        Assert.True(search.MarkersTruncated, "the fixture must exceed the marker cap");
        var session = fixture.Tab.Snapshot!.TimedRange!.Value;
        var lastTenth = new TimeRange(
            new InstantUs(session.EndExclusive.Value - (session.DurationUs / 10)),
            session.EndExclusive);
        Assert.DoesNotContain(
            search.Markers,
            marker => marker.Value >= lastTenth.StartInclusive.Value);

        // Put the last tenth on screen; the lane draws from what is visible there.
        await fixture.Tab.SetViewportAsync(lastTenth, manual: true);
        var timeline = fixture.View.GetLogicalDescendants()
            .OfType<VisualCat.App.Timeline.TimelineControl>()
            .First();
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => timeline.DrawnSearchMarkerColumns > 0);
        Assert.True(
            timeline.DrawnSearchMarkerColumns > 0,
            "a match inside the visible range must be marked however many precede it");

        // And the lane's click reaches that record, not the nearest one the list happens to hold.
        var picked = await fixture.Tab.NavigateSearchAsync(
            SearchMatchRequestKind.Nearest,
            near: lastTenth.StartInclusive);
        Assert.NotNull(picked?.Key);
        Assert.True(
            picked!.Key!.Value.TimestampUs >= lastTenth.StartInclusive.Value,
            "the nearest match to the visible range must be the late one");
        Assert.Equal(search.Matches, picked.TotalMatches);
    }

    /// <summary>
    /// A rejected pattern leaves the selected match exactly where the previous search left it.
    /// </summary>
    /// <remarks>
    /// The rollback restored the filter, the rows, the plot and the counter's total, but the
    /// cursor was dropped on the way in by the requested-filter change and never came back —
    /// so a timed-out regex silently turned `2 / 3` into `– / 3` beside rows that had not
    /// moved. Retaining the previous result means retaining which of its matches was chosen.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARejectedPatternKeepsTheMatchThePreviousSearchHadSelected()
    {
        var previousTimeout = SessionTabViewModel.SearchRegexTimeoutOverride;
        SessionTabViewModel.SearchRegexTimeoutOverride = TimeSpan.FromMilliseconds(1);
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                ThreeMatchLog +
                $"01-01 00:00:30.000000   100   101 I Worker         : {new string('a', 100_000)}!\n");
            await ApplySearchAsync(fixture, "needle");
            var selected = await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Next);
            Assert.NotNull(selected?.Key);
            var before = fixture.Tab.SelectedSearchMatch;
            var counter = $"{selected.Ordinal:N0} / 3";
            Assert.Equal(counter, CounterText(fixture));

            fixture.Tab.SearchText = "(?=a)^(a+)+$";
            var problem = await fixture.Tab.ApplySearchAsync(regex: true, caseSensitive: false);

            Assert.NotNull(problem);
            Assert.Equal(SearchPatternProblemKind.TimedOut, problem.Value.Kind);
            Assert.Same(before, fixture.Tab.SelectedSearchMatch);
            Assert.Equal(selected!.Key, fixture.Tab.SelectedSearchMatch?.Key);
            PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, static () => true, passes: 4);
            Assert.Equal(counter, CounterText(fixture));

            // And a genuinely new search still drops it, because a match number means nothing
            // across two searches.
            await ApplySearchAsync(fixture, "one");
            Assert.Null(fixture.Tab.SelectedSearchMatch);
        }
        finally
        {
            SessionTabViewModel.SearchRegexTimeoutOverride = previousTimeout;
        }
    }

    /// <summary>
    /// The phone says how the arrival window accounts for the rows it is not showing.
    /// </summary>
    /// <remarks>
    /// On the device the count line above the list read `1,424 in view` over a list holding
    /// one row, and the only place the other 1,423 were mentioned was a tooltip. The desktop
    /// had said `N shown · B earlier · A later` in its entry toolbar all along; the phone has
    /// no such toolbar, so the sentence goes in the footer band under the list.
    /// </remarks>
    [AvaloniaFact]
    public async Task ThePhoneFooterAccountsForTheRowsTheArrivalWindowHides()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(1_200), 420, 900);
            await ApplySearchAsync(fixture, "needle");
            Assert.Null(ArrivalAccounting(fixture));

            await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.IsEntryArrivalWindow && ArrivalAccounting(fixture) is not null);

            var accounting = ArrivalAccounting(fixture);
            Assert.NotNull(accounting);
            var loaded = fixture.Tab.LoadedEntryCount;
            var earlier = fixture.Tab.EarlierEntryCount;
            var later = fixture.Tab.RemainingEntryCount;
            Assert.True(earlier > 0, "the last match's window must start past the first page");
            Assert.Equal($"{loaded:N0} shown · {earlier:N0} earlier · {later:N0} later", accounting);

            // The band it lives in is the one Load 500 more vacates at the end of a range,
            // so the sentence has to earn the band on its own.
            var footer = fixture.View.GetLogicalDescendants()
                .OfType<Border>()
                .First(static border => AutomationProperties.GetName(border) == "End of the loaded rows");
            Assert.True(footer.IsEffectivelyVisible);

            await fixture.Tab.ReturnToEntryRangeStartAsync();
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => !fixture.Tab.IsEntryArrivalWindow && ArrivalAccounting(fixture) is null);
            Assert.Null(ArrivalAccounting(fixture));
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// An arrival window that hides nothing says nothing, on either platform.
    /// </summary>
    /// <remarks>
    /// The sentence exists to account for rows above the window, and the first match has none
    /// above it. The desktop showed it anyway — `500 shown · 0 earlier · 923 later` in place of
    /// the ordinary loaded-row progress — while the phone, which gates on the same count,
    /// showed nothing. Two surfaces describing one state differently is the shape §12.4 named,
    /// and the plan is explicit: disclose when B &gt; 0.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArrivingAtTheFirstMatchAccountsForNothingBecauseNothingIsHidden(bool phone)
    {
        SessionWorkspaceView.PhoneCompositionOverride = phone;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                ManyMatchLog(1_200),
                phone ? 420 : 1280,
                phone ? 900 : 800);
            await ApplySearchAsync(fixture, "needle");

            await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.First);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.IsEntryArrivalWindow && fixture.Tab.SelectedSearchMatch is not null);

            // The window is an arrival, and it begins at the start of the range.
            Assert.True(fixture.Tab.IsEntryArrivalWindow);
            Assert.Equal(0, fixture.Tab.EarlierEntryCount);

            Assert.Null(ArrivalAccounting(fixture));
            Assert.Empty(StartOfRangeActions(fixture));

            // And the last match, which does hide rows, still says so on this same surface.
            await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.EarlierEntryCount > 0 && ArrivalAccounting(fixture) is not null);
            Assert.NotNull(ArrivalAccounting(fixture));
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// The desktop keeps its account of the hidden rows when the entry toolbar folds.
    /// </summary>
    /// <remarks>
    /// The toolbar compacts below 1,100 logical pixels of pane width, which a 1,280-wide
    /// window is under once the plot and panes have taken their share — and that is one of
    /// the three desktop sizes this work is checked at. Everything else in that row moves
    /// into the More menu; this sentence had no second home, so the desktop offered
    /// <em>Start of range</em> with nothing on screen saying what there was to return from.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheDesktopStillAccountsForHiddenRowsWhenItsEntryToolbarFolds()
    {
        SessionWorkspaceView.PhoneCompositionOverride = false;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyMatchLog(1_200), 1280, 800);
            await ApplySearchAsync(fixture, "needle");

            var status = fixture.View.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Single(static text => (text.Text ?? string.Empty).EndsWith("rows loaded", StringComparison.Ordinal));
            Assert.False(status.IsVisible, "this width must fold the toolbar, or the test proves nothing");

            await fixture.Tab.NavigateSearchAsync(SearchMatchRequestKind.Last);
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => fixture.Tab.EarlierEntryCount > 0 && ArrivalAccounting(fixture) is not null);

            var loaded = fixture.Tab.LoadedEntryCount;
            var earlier = fixture.Tab.EarlierEntryCount;
            var later = fixture.Tab.RemainingEntryCount;
            Assert.True(earlier > 0, "the last match's window must start past the first page");
            Assert.Equal($"{loaded:N0} shown · {earlier:N0} earlier · {later:N0} later", ArrivalAccounting(fixture));

            // Start of range is offered from the folded menu, and now says what it returns from.
            Assert.NotEmpty(StartOfRangeActions(fixture));

            // Back at the start of the range the folded toolbar is quiet again.
            await fixture.Tab.ReturnToEntryRangeStartAsync();
            PixelGestureAndTextScaleTests.PumpUntil(
                fixture.Window,
                () => !fixture.Tab.IsEntryArrivalWindow && ArrivalAccounting(fixture) is null);
            Assert.Null(ArrivalAccounting(fixture));
            Assert.False(status.IsVisible);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>What either surface says about the hidden rows, or null while it says nothing.</summary>
    private static string? ArrivalAccounting(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(static text => text.IsVisible)
            .Select(static text => text.Text ?? string.Empty)
            .Distinct()
            .FirstOrDefault(static text => text.Contains(" shown · ", StringComparison.Ordinal));

    /// <summary>Every visible way back to the ordinary first page, whatever the platform.</summary>
    private static (string Label, string Description)[] StartOfRangeActions(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<Control>()
            .Where(static control => control is Button or MenuItem && control.IsVisible)
            .Select(static control => (
                Label: (control as Button)?.Content as string ?? (control as MenuItem)?.Header as string ?? string.Empty,
                Description: AutomationProperties.GetHelpText(control) ?? string.Empty))
            .Where(static action => action.Label.StartsWith("Start of range", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

    private static QueryIdentity Identity(string fingerprint) =>
        new(Guid.Empty, 1, fingerprint, 1);

    private static KeyEventArgs Key(Key key, KeyModifiers modifiers) =>
        new() { Key = key, KeyModifiers = modifiers };

    /// <summary>A key press raised from the search field, which is what guards bare letters.</summary>
    private static KeyEventArgs FromSearch(TextBox search, Key key, KeyModifiers modifiers) =>
        new() { Key = key, KeyModifiers = modifiers, Source = search };

    private static string CounterText(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .Select(static button => button.Content as TextBlock)
            .Where(static text => text is not null)
            .Select(static text => text!.Text ?? string.Empty)
            .First(static text => text.Contains('/', StringComparison.Ordinal) || text == "No matches");

    private static async Task ApplySearchAsync(LiveTestWorkspaceFixture fixture, string query)
    {
        fixture.Tab.SearchText = query;
        await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false);
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.SearchResult is not null && !fixture.Tab.IsQueryPending);
        fixture.View.UpdateMarkerNavigationForTest();
    }

    /// <summary>
    /// More matches than the marker cap can hold, with one of them in the last tenth of the
    /// session so the cap is guaranteed to stop before it.
    /// </summary>
    private static string LateMatchLog(int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count - 1; i++)
        {
            builder.Append("01-01 00:00:")
                .Append((i / 1_000_000 % 60).ToString("00", CultureInfo.InvariantCulture))
                .Append('.')
                .Append((i % 1_000_000).ToString("000000", CultureInfo.InvariantCulture))
                .Append("   100   101 I Worker         : needle ")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        builder.Append("01-01 00:00:59.000000   100   101 I Worker         : needle last\n");
        return builder.ToString();
    }

    /// <summary>
    /// Matches packed into a span shorter than one viewport, so the entry range holds all of
    /// them and the arrival window genuinely starts past the ordinary first page.
    /// </summary>
    private static string ManyMatchLog(int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append("01-01 00:00:00.")
                .Append(i.ToString("000000", CultureInfo.InvariantCulture))
                .Append("   100   101 I Worker         : needle ")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }
}
