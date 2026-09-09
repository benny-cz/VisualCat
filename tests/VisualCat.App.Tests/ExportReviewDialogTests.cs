using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.App.Tests;

/// <summary>
/// The review the reader decides an export from, driven through its own controls.
/// </summary>
/// <remarks>
/// The scope algebra is proved by <see cref="ExportScopeOracleTests"/> and the file by
/// <see cref="ExportFidelityTests"/>. Neither presses this dialog's buttons, and a dialog
/// nothing presses is where a 48 dp target that cannot be tapped hides. These are its own
/// states: what it says while it is counting, what it does with a scope that has no rows,
/// what a failed count offers, and what the two option pickers carry into the decision.
/// </remarks>
public sealed class ExportReviewDialogTests
{
    /// <summary>Choices arrive together, with the preferred one already chosen.</summary>
    [AvaloniaFact]
    public void TheReviewCountsBeforeItOffersAnythingToDecide()
    {
        using var host = Open(
            Scope(ExportScopeKind.VisiblePlot, "Visible plot range", 12, preferred: true),
            Scope(ExportScopeKind.AllTimed, "All timed entries in session", 40));

        Assert.Equal("Calculating rows…", host.Status);
        Assert.False(host.Choose.IsEnabled);
        Assert.Empty(host.Options);

        host.Publish();

        Assert.Collection(
            host.Options,
            option => Assert.Equal("Visible plot range — 12 timed rows", Name(option)),
            option => Assert.Equal("All timed entries in session — 40 timed rows", Name(option)));
        Assert.True(host.Options[0].IsChecked);
        Assert.True(host.Choose.IsEnabled);
        Assert.Contains("12 timed rows · UTC", host.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty preferred scope chooses nothing, and never quietly widens to a bigger one.
    /// </summary>
    [AvaloniaFact]
    public void AnEmptyPreferredScopeLeavesTheDecisionToTheReader()
    {
        using var host = Open(
            Scope(ExportScopeKind.SelectedCell, "Selected cell · Error", 0, preferred: true),
            Scope(ExportScopeKind.AllTimed, "All timed entries in session", 40));
        host.Publish();

        Assert.All(host.Options, static option => Assert.NotEqual(true, option.IsChecked));
        Assert.False(host.Options[0].IsEnabled, "a scope with no rows cannot be exported");
        Assert.True(host.Options[1].IsEnabled);
        Assert.False(host.Choose.IsEnabled);
        Assert.Equal(
            "The selected scope has no timed rows. Choose another scope or close this dialog.",
            host.Status);

        host.Options[1].IsChecked = true;
        Assert.True(host.Choose.IsEnabled);
        Assert.Contains("40 timed rows", host.Status, StringComparison.Ordinal);
    }

    /// <summary>With nothing to write, the way out is Close rather than Cancel.</summary>
    [AvaloniaFact]
    public void AReviewWithNoRowsAnywhereOffersOnlyTheWayOut()
    {
        using var host = Open(
            Scope(ExportScopeKind.VisiblePlot, "Visible plot range", 0, preferred: true),
            Scope(ExportScopeKind.AllFiltered, "All timed entries matching filters", 0));
        host.Publish();

        Assert.Equal(
            "No timed entries in this scope. Change the range or filters before exporting.",
            host.Status);
        Assert.False(host.Choose.IsEnabled);
        Assert.Equal("Close", host.Cancel.Content);
    }

    /// <summary>
    /// A count that failed says so, offers the same request again, and publishes no half list.
    /// </summary>
    [AvaloniaFact]
    public void AFailedCountOffersARetryRatherThanAPartialScopeList()
    {
        using var host = Open(Scope(ExportScopeKind.AllTimed, "All timed entries in session", 40, preferred: true));
        host.Fail(new IOException("The session moved."));

        Assert.Empty(host.Options);
        Assert.Equal("Could not calculate export rows. Try again.", host.Status);
        Assert.False(host.Choose.IsEnabled);
        Assert.True(host.Retry.IsVisible);

        host.Press(host.Retry);
        Assert.Equal(2, host.Counts);
        host.Publish();

        Assert.False(host.Retry.IsVisible);
        Assert.True(host.Choose.IsEnabled);
        Assert.Equal("All timed entries in session — 40 timed rows", host.SummaryHeading);
    }

    /// <summary>
    /// One scope is stated, not offered: there is no choice to make about it.
    /// </summary>
    /// <remarks>
    /// Export range resolves its own scope, and a fitted unfiltered plot deduplicates to one.
    /// A single radio button cannot be answered wrongly and cannot be left alone, so it is a
    /// control that does nothing; the review still earns its place through Row order and
    /// Encoding beside the decision. An empty single scope keeps its explanation instead.
    /// </remarks>
    [AvaloniaFact]
    public void ASingleScopeIsStatedRatherThanOffered()
    {
        using var host = Open(Scope(ExportScopeKind.SelectedRange, "Selected range", 7, preferred: true));
        host.Publish();

        Assert.Empty(host.Options);
        Assert.Equal("Selected range — 7 timed rows", host.SummaryHeading);
        Assert.True(host.Choose.IsEnabled);
        Assert.Contains("7 timed rows · UTC", host.Status, StringComparison.Ordinal);
        Assert.Equal("Cancel", host.Cancel.Content);
    }

    /// <summary>A lone scope with no rows still explains itself rather than summarising nothing.</summary>
    [AvaloniaFact]
    public void ASingleEmptyScopeKeepsItsExplanationAndOffersOnlyTheWayOut()
    {
        using var host = Open(Scope(ExportScopeKind.SelectedRange, "Selected range", 0, preferred: true));
        host.Publish();

        Assert.Empty(host.Options);
        Assert.Equal("Selected range — 0 timed rows", host.SummaryHeading);
        Assert.False(host.Choose.IsEnabled);
        Assert.Equal(
            "No timed entries in this scope. Change the range or filters before exporting.",
            host.Status);
        Assert.Equal("Close", host.Cancel.Content);
    }

    /// <summary>The counted noun agrees with its number, here as everywhere else.</summary>
    [AvaloniaFact]
    public void ASingleRowIsOneTimedRow()
    {
        using var host = Open(Scope(ExportScopeKind.SelectedCell, "Selected cell · Error", 1, preferred: true));
        host.Publish();

        Assert.Equal("Selected cell · Error — 1 timed row", host.SummaryHeading);
        Assert.StartsWith("1 timed row · UTC", host.Status, StringComparison.Ordinal);
    }

    /// <summary>Both pickers start from the stored defaults and travel into the decision.</summary>
    [AvaloniaTheory]
    [InlineData(EntryOrder.SourceSequence, false)]
    [InlineData(EntryOrder.Chronological, true)]
    public async Task TheOptionsBesideTheDecisionAreTheOnesTheExportUses(EntryOrder order, bool bom)
    {
        using var host = Open(
            Scope(ExportScopeKind.AllTimed, "All timed entries in session", 40, preferred: true),
            defaultOrder: order,
            defaultBom: bom);
        host.Publish();

        Assert.Equal(order == EntryOrder.SourceSequence ? "Source order" : "Chronological", host.RowOrder.SelectedItem);
        Assert.Equal(bom ? "UTF-8 with byte-order mark" : "UTF-8", host.Encoding.SelectedItem);

        // Change both, to prove the decision carries what the reader chose rather than the
        // defaults it opened with.
        host.RowOrder.SelectedIndex = order == EntryOrder.SourceSequence ? 1 : 0;
        host.Encoding.SelectedIndex = bom ? 0 : 1;
        host.Press(host.Choose);

        var decision = await host.Dialog.Completion.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.NotNull(decision);
        Assert.Equal(
            order == EntryOrder.SourceSequence ? EntryOrder.Chronological : EntryOrder.SourceSequence,
            decision.Order);
        Assert.Equal(!bom, decision.IncludeUtf8Bom);
        Assert.Equal(40, decision.Scope.TimedRows);
    }

    /// <summary>
    /// An active template filter reads as the message shape it matches, here as everywhere.
    /// </summary>
    /// <remarks>
    /// The filter values are what a reader opens to check exactly which filter this export
    /// applies. A bare mined ID there is the same unreadable filter the facet package exists
    /// to correct, in the one surface that package did not reach.
    /// </remarks>
    [AvaloniaFact]
    public void AnActiveTemplateFilterReadsAsItsCanonicalText()
    {
        var filter = FilterSpec.All with
        {
            IncludedTemplates = [3821941u],
            ExcludedTemplates = [77u],
        };
        using var host = Open(
            Scope(ExportScopeKind.AllFiltered, "All timed entries matching filters", 4, preferred: true, filter),
            filter: filter,
            templateName: static id => id == 3821941u ? "Connection <*> failed after <*> ms" : $"Template {id}");
        host.Publish();

        var values = host.Dialog.GetLogicalDescendants()
            .OfType<SelectableTextBlock>()
            .Select(static block => block.Text ?? string.Empty)
            .ToArray();

        Assert.Contains(values, static text => text.Contains(
            "Template is Connection <*> failed after <*> ms",
            StringComparison.Ordinal));

        // An ID the snapshot has no definition for stays identifying rather than vanishing.
        Assert.Contains(values, static text => text.Contains("Template is not Template 77", StringComparison.Ordinal));
        Assert.DoesNotContain(values, static text => text.Contains("3821941", StringComparison.Ordinal));
    }

    /// <summary>The expanded values always describe the selected scope's actual query.</summary>
    /// <remarks>
    /// The whole-session choice intentionally ignores the workspace filter. Its compact
    /// summary already said so, but the old expanded section kept showing the captured filter,
    /// making one review contradict itself. This drives both radio choices and checks the
    /// parse-outcome dimension that used to disappear from the expanded values entirely.
    /// </remarks>
    [AvaloniaFact]
    public void FilterValuesFollowTheSelectedScopeAndNameEveryParseOutcome()
    {
        var filter = FilterSpec.All with
        {
            IncludedOutcomes =
            [
                ParseOutcomeKind.ParsedEntry,
                ParseOutcomeKind.MetaRecord,
                ParseOutcomeKind.Continuation,
                ParseOutcomeKind.UntimedEntry,
                ParseOutcomeKind.IgnoredBlank,
                ParseOutcomeKind.UnknownLine,
                ParseOutcomeKind.RejectedCandidate,
            ],
            Search = new TextSearchSpec("fatal", IsRegex: true, CaseSensitive: true),
        };
        using var host = Open(
            [
                Scope(ExportScopeKind.VisiblePlot, "Visible plot range", 4, preferred: true, filter),
                Scope(ExportScopeKind.AllTimed, "All timed entries in session", 12),
            ],
            EntryOrder.SourceSequence,
            defaultBom: false,
            filter,
            templateName: null);
        host.Publish();

        Assert.Contains("Filters: search, parse outcome", host.Status, StringComparison.Ordinal);
        Assert.Contains(
            host.FilterValues,
            static text => text.Contains(
                "Parse outcome is parsed entry, meta record, continuation line, untimed entry, " +
                "ignored blank line, unknown line, rejected candidate",
                StringComparison.Ordinal));
        Assert.Contains(
            host.FilterValues,
            static text => text.Contains(
                "Message matches regular expression fatal · Match case",
                StringComparison.Ordinal));

        host.Options[1].IsChecked = true;

        Assert.Contains("No filters", host.Status, StringComparison.Ordinal);
        Assert.Empty(host.FilterValues);
    }

    private static string Name(Control option) =>
        Avalonia.Automation.AutomationProperties.GetName(option) ?? string.Empty;

    private static ResolvedExportScope Scope(
        ExportScopeKind kind,
        string label,
        long rows,
        bool preferred = false,
        FilterSpec? filter = null) =>
        new(
            kind,
            label,
            new TimeRange(new InstantUs(0), new InstantUs(10_000_000)),
            filter ?? FilterSpec.All,
            $"{label}, over the whole session.",
            preferred,
            IsProvablyEmpty: rows == 0,
            rows);

    private static ReviewHost Open(
        params ResolvedExportScope[] scopes) =>
        Open(scopes, EntryOrder.SourceSequence, false, FilterSpec.All, null);

    private static ReviewHost Open(
        ResolvedExportScope scope,
        EntryOrder defaultOrder = EntryOrder.SourceSequence,
        bool defaultBom = false,
        FilterSpec? filter = null,
        Func<uint, string>? templateName = null) =>
        Open([scope], defaultOrder, defaultBom, filter ?? FilterSpec.All, templateName);

    private static ReviewHost Open(
        ResolvedExportScope[] scopes,
        EntryOrder defaultOrder,
        bool defaultBom,
        FilterSpec filter,
        Func<uint, string>? templateName)
    {
        var gate = new CountGate();
        var counts = 0;
        var request = new FrozenExportRequest(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), "session"),
            "crash.txt",
            filter,
            new TimeRange(new InstantUs(0), new InstantUs(10_000_000)),
            null,
            null,
            null,
            defaultOrder,
            defaultBom,
            CaptureContinues: false);
        var dialog = new ExportReviewDialog(
            request,
            _ =>
            {
                counts++;
                return gate.Current.Task;
            },
            "UTC",
            hasOffTimelineLines: false,
            templateName);
        return new ReviewHost(dialog, scopes, () => counts, gate);
    }

    /// <summary>
    /// The count the dialog is waiting on. Retry asks a second time, so the answer it waits
    /// for has to be replaceable without the test racing the click that starts it.
    /// </summary>
    private sealed class CountGate
    {
        internal TaskCompletionSource<IReadOnlyList<ResolvedExportScope>> Current { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Reset() => Current = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>The dialog on screen, with the controls the reader uses named once.</summary>
    private sealed class ReviewHost : IDisposable
    {
        private readonly Window _window;
        private readonly Func<int> _counts;
        private readonly CountGate _gate;

        internal ReviewHost(
            ExportReviewDialog dialog,
            IReadOnlyList<ResolvedExportScope> scopes,
            Func<int> counts,
            CountGate gate)
        {
            Dialog = dialog;
            Scopes = scopes;
            _counts = counts;
            _gate = gate;
            _window = new Window { Content = dialog, Width = 640, Height = 720 };
            _window.Show();
            _window.UpdateLayout();
            dialog.NotifyPresented();
            Pump();
        }

        internal ExportReviewDialog Dialog { get; }

        internal IReadOnlyList<ResolvedExportScope> Scopes { get; }

        internal int Counts => _counts();

        internal RadioButton[] Options => Dialog.GetLogicalDescendants()
            .OfType<RadioButton>()
            .ToArray();

        internal Button Choose => Find("Choose a file…");

        internal Button Cancel => Dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(static button => Equals(button.Content, "Cancel") || Equals(button.Content, "Close"));

        internal Button Retry => Find("Retry");

        /// <summary>The stated scope, when there is only one and it is not a question.</summary>
        internal string SummaryHeading => _scopeSummaryHeadings.Single();

        private string[] _scopeSummaryHeadings => Dialog.GetLogicalDescendants()
            .OfType<StackPanel>()
            .Select(static panel => Avalonia.Automation.AutomationProperties.GetName(panel) ?? string.Empty)
            .Where(static name => name.Contains(" timed row", StringComparison.Ordinal))
            .ToArray();

        internal ComboBox RowOrder => Combo("Row order");

        internal ComboBox Encoding => Combo("Encoding");

        /// <summary>The one line under the choices that says what the decision would export.</summary>
        internal string Status => Dialog.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(static block => block.Name == "ExportDecisionStatus")
            .Text ?? string.Empty;

        internal string[] FilterValues => Dialog.GetLogicalDescendants()
            .OfType<SelectableTextBlock>()
            .Select(static block => block.Text ?? string.Empty)
            .ToArray();

        internal void Publish()
        {
            _gate.Current.TrySetResult(Scopes);
            Pump();
        }

        internal void Fail(Exception reason)
        {
            _gate.Current.TrySetException(reason);
            Pump();
        }

        internal void Press(Button button)
        {
            // Retry starts the next count, so the gate is replaced before the click lands.
            if (Equals(button.Content, "Retry"))
            {
                _gate.Reset();
            }

            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        public void Dispose()
        {
            _gate.Current.TrySetCanceled();
            _window.Close();
            Dialog.Dispose();
        }

        private Button Find(string content) => Dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Content, content));

        private ComboBox Combo(string name) => Dialog.GetLogicalDescendants()
            .OfType<ComboBox>()
            .Single(box => Avalonia.Automation.AutomationProperties.GetName(box) == name);

        private void Pump()
        {
            for (var pass = 0; pass < 20; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                _window.UpdateLayout();
            }
        }
    }
}
