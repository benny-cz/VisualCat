using System.Collections.Concurrent;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Theme;
using VisualCat.App.Views;
using VisualCat.Core.Query;

namespace VisualCat.App.Tests;

public sealed class ImprovementAuditTests
{
    private const string SearchableLog =
        "01-01 00:00:00.000000   100   101 I Worker         : ordinary needle one\n" +
        "01-01 00:00:01.000000   100   101 E Loader         : ordinary two\n" +
        "01-01 00:00:02.000000   100   101 I Worker         : ordinary needle three\n";

    [AvaloniaTheory]
    [InlineData("needle", false)]
    [InlineData("need.*", true)]
    public async Task AColdRefreshBuildsOneActiveBitmapPerSegment(string query, bool regex)
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(SearchableLog);
        var builds = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        foreach (var segment in fixture.Tab.Snapshot!.Segments)
        {
            segment.BitmapFactoryStartedForTests = key =>
                builds.AddOrUpdate(key, 1, static (_, count) => count + 1);
        }

        fixture.Tab.SearchText = query;
        Assert.Null(await fixture.Tab.ApplySearchAsync(regex, caseSensitive: false));

        Assert.Equal(
            fixture.Tab.Snapshot.Segments.Count,
            builds.GetValueOrDefault(fixture.Tab.Filter.Fingerprint()));
        Assert.False(fixture.Tab.SearchInProgress);
    }

    [AvaloniaFact]
    public async Task TimedOutRegexRollsBackEveryPublishedSearchSurfaceAndTheNextSearchWorks()
    {
        var previousTimeout = SessionTabViewModel.SearchRegexTimeoutOverride;
        SessionTabViewModel.SearchRegexTimeoutOverride = TimeSpan.FromMilliseconds(1);
        try
        {
            var hostile = new string('a', 100_000) + "!";
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(
                $"01-01 00:00:00.000000   100   101 I Worker         : {hostile}\n" +
                "01-01 00:00:01.000000   100   101 I Worker         : ordinary\n");
            var filter = fixture.Tab.Filter;
            var heat = fixture.Tab.HeatMap;
            var overview = fixture.Tab.Overview;
            var statistics = fixture.Tab.Statistics;
            var searchResult = fixture.Tab.SearchResult;
            var matches = fixture.Tab.MatchesInView;
            var entries = fixture.Tab.Entries.Select(static entry => entry.EntryId).ToArray();

            fixture.Tab.SearchText = "(?=a)^(a+)+$";
            var problem = await fixture.Tab.ApplySearchAsync(regex: true, caseSensitive: false);

            Assert.NotNull(problem);
            Assert.Equal(SearchPatternProblemKind.TimedOut, problem.Value.Kind);
            Assert.Equal(SearchTimeoutException.UserMessage, problem.Value.Sentence);
            Assert.Same(filter, fixture.Tab.Filter);
            Assert.Same(heat, fixture.Tab.HeatMap);
            Assert.Same(overview, fixture.Tab.Overview);
            Assert.Same(statistics, fixture.Tab.Statistics);
            Assert.Same(searchResult, fixture.Tab.SearchResult);
            Assert.Equal(matches, fixture.Tab.MatchesInView);
            Assert.Equal(entries, fixture.Tab.Entries.Select(static entry => entry.EntryId));
            Assert.False(fixture.Tab.SearchInProgress);

            fixture.Tab.SearchText = "ordinary";
            Assert.Null(await fixture.Tab.ApplySearchAsync(regex: false, caseSensitive: false));
            Assert.Equal(1, fixture.Tab.SearchResult?.Matches);
        }
        finally
        {
            SessionTabViewModel.SearchRegexTimeoutOverride = previousTimeout;
        }
    }

    [Fact]
    public void DesktopColumnsCollapseWholeOptionalFactsBeforeTakingMessageSpace()
    {
        var roomy = SessionWorkspaceView.EntryLayoutFor(901, textScale: 1);
        Assert.True(roomy.Process && roomy.Tid && roomy.Buffer && roomy.Template);
        Assert.Equal("165,32,112,56,68,96,52,*", roomy.Columns);

        var defaultWindowPane = SessionWorkspaceView.EntryLayoutFor(780, textScale: 1);
        Assert.True(defaultWindowPane.Process);
        Assert.False(defaultWindowPane.Tid);
        Assert.False(defaultWindowPane.Buffer);
        Assert.False(defaultWindowPane.Template);

        var minimumScanningSet = SessionWorkspaceView.EntryLayoutFor(613, textScale: 1);
        Assert.False(minimumScanningSet.Process);
        Assert.False(minimumScanningSet.Tid);
        Assert.False(minimumScanningSet.Buffer);
        Assert.False(minimumScanningSet.Template);
        Assert.Equal("165,32,0,0,0,96,0,*", minimumScanningSet.Columns);
    }

    /// <summary>
    /// A fixed 165 px TIME column ellipsizes its own timestamp once the reader enlarges the
    /// text, which is the failure the collapse order exists to avoid. Widths therefore scale
    /// with the text, and the same width buys fewer optional facts.
    /// </summary>
    [Fact]
    public void DesktopColumnWidthsFollowTheReadersTextSize()
    {
        var doubled = SessionWorkspaceView.EntryLayoutFor(1802, textScale: 2);
        Assert.True(doubled.Process && doubled.Tid && doubled.Buffer && doubled.Template);
        Assert.Equal("330,64,224,112,136,192,104,*", doubled.Columns);

        // The width that showed every fact at 100% keeps only the durable scanning set at 200%.
        var enlargedAtTheSameWidth = SessionWorkspaceView.EntryLayoutFor(901, textScale: 2);
        Assert.False(enlargedAtTheSameWidth.Process);
        Assert.False(enlargedAtTheSameWidth.Tid);
        Assert.False(enlargedAtTheSameWidth.Buffer);
        Assert.False(enlargedAtTheSameWidth.Template);
        Assert.Equal("330,64,0,0,0,192,0,*", enlargedAtTheSameWidth.Columns);
    }

    /// <summary>
    /// The audit measured 27 characters of message at a 1440 px window with insights showing.
    /// This pins the remedy at the width the finding was raised against.
    /// </summary>
    [AvaloniaFact]
    public async Task MessageKeepsItsMinimumShareAtTheDefaultWindowWidth()
    {
        var previousPhone = SessionWorkspaceView.PhoneCompositionOverride;
        SessionWorkspaceView.PhoneCompositionOverride = false;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(SearchableLog, width: 1440, height: 900);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            var header = Assert.IsType<Grid>(fixture.View.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "MESSAGE")
                .GetVisualParent());
            Assert.True(
                header.ColumnDefinitions[7].ActualWidth >= 320,
                $"MESSAGE got {header.ColumnDefinitions[7].ActualWidth:F0} px at a 1440 px window.");
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = previousPhone;
        }
    }

    [AvaloniaFact]
    public async Task DesktopRecoveryActionsAndOverflowRemainReachableAtTheMinimumWidth()
    {
        var previousPhone = SessionWorkspaceView.PhoneCompositionOverride;
        var previousScale = TextScale.User;
        SessionWorkspaceView.PhoneCompositionOverride = false;
        TextScale.User = 2;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(SearchableLog, width: 900, height: 600);
            Dispatcher.UIThread.RunJobs();
            fixture.Window.UpdateLayout();

            Button Button(string label) => fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, label));

            var fullEntry = Button("Full entry");
            var insights = Button("Hide insights");
            var more = fixture.View.GetLogicalDescendants()
                .OfType<MenuItem>()
                .Single(item => AutomationProperties.GetName(item) == "More entry actions");

            Assert.True(fullEntry.IsEffectivelyVisible);
            Assert.True(insights.IsEffectivelyVisible);
            Assert.True(more.IsEffectivelyVisible);
            var sort = Assert.IsType<MenuItem>(more.Items
                .OfType<MenuItem>()
                .Single(item => Equals(item.Header, "Sort entries")));
            var sortChoices = sort.Items.OfType<MenuItem>().ToArray();
            Assert.Collection(
                sortChoices,
                chronological =>
                {
                    Assert.Equal("Chronological", chronological.Header);
                    Assert.Equal(MenuItemToggleType.Radio, chronological.ToggleType);
                    Assert.True(chronological.IsChecked);
                },
                sourceOrder =>
                {
                    Assert.Equal("Source order", sourceOrder.Header);
                    Assert.Equal(MenuItemToggleType.Radio, sourceOrder.ToggleType);
                    Assert.False(sourceOrder.IsChecked);
                });
            sortChoices[1].RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(sortChoices[0].IsChecked);
            Assert.True(sortChoices[1].IsChecked);
            var recovery = Assert.IsType<StackPanel>(fullEntry.GetVisualParent());
            Assert.True(recovery.DesiredSize.Width <= recovery.Bounds.Width + 0.5);

            var messageLabel = fixture.View.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text == "MESSAGE");
            Assert.True(messageLabel.IsEffectivelyVisible);
            var header = Assert.IsType<Grid>(messageLabel.GetVisualParent());
            // 524 px of table at 200% text: every optional column is already gone, so TIME
            // yields its microsecond tail rather than the message column reaching zero.
            Assert.True(
                header.ColumnDefinitions[7].ActualWidth > 0,
                $"MESSAGE got no width at a 900 px window and 200% text. Tracks: " +
                string.Join(',', header.ColumnDefinitions.Select(
                    c => c.ActualWidth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.True(header.ColumnDefinitions[0].ActualWidth >= 104 * TextScale.Effective);
        }
        finally
        {
            TextScale.User = previousScale;
            SessionWorkspaceView.PhoneCompositionOverride = previousPhone;
        }
    }

    [Fact]
    public void MobileDividerUsesTheSharedSelfSizedTouchToken()
    {
        Assert.Equal(TouchTarget.MinimumWithEdgeReserve, MobilePaneSplitter.HitTargetExtent);
        Assert.Equal(49, MobilePaneSplitter.HitTargetExtent);
        Assert.Equal(12, MobilePaneSplitter.LaneExtent);
        Assert.Equal(20, MobilePaneSplitter.LaneBandExtent);
    }
}
