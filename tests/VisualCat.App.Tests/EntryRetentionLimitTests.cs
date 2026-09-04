using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// IA-07 — the bulk row load stops at a platform ceiling, and says so where the reader is.
/// </summary>
/// <remarks>
/// <para>
/// <c>Load all</c> bound every outstanding row into the list and kept it there, with no
/// ceiling above the one confirmation. X-13's session had 996,385 rows outstanding; on a
/// phone the end of that is process death, which cannot be caught, reported, or recovered
/// from — it ends the workspace and the capture with it.
/// </para>
/// <para>
/// The ceiling is now the product's answer, so what has to hold is not just that loading
/// stops: the reader has to be able to tell that the list is short of the session and why.
/// The phone footer is the only surface that can say so there, and it is the one that
/// disappeared with <c>Load 500 more</c> the first time this ran on a device.
/// </para>
/// </remarks>
public sealed class EntryRetentionLimitTests
{
    private const int Ceiling = 600;

    [AvaloniaFact]
    public async Task TheBulkLoadStopsAtTheCeilingAndTheFooterExplainsWhy()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        SessionTabViewModel.EntryRetentionLimitOverride = Ceiling;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog(1_500), 420, 900);
            await SettleAsync(fixture);

            var bulk = BulkLoadButton(fixture.View);
            Assert.Equal($"{Ceiling:N0}", bulk.Content as string);
            Assert.True(bulk.IsEffectivelyVisible);
            Assert.False(fixture.Tab.IsEntryRetentionLimitReached);

            await fixture.Tab.LoadAllEntriesAsync(TestContext.Current.CancellationToken);
            await SettleAsync(fixture);

            // Stopped exactly at the ceiling, with the cursor closed rather than exhausted.
            Assert.Equal(Ceiling, fixture.Tab.Entries.Count);
            Assert.True(fixture.Tab.IsEntryRetentionLimitReached);
            Assert.False(fixture.Tab.CanLoadMore);
            Assert.Equal(1_500 - Ceiling, fixture.Tab.RemainingEntryCount);

            // The band survives losing Load 500 more, because it is now holding the only
            // sentence on a phone that says the list stops short of the session.
            var footer = fixture.View.GetLogicalDescendants()
                .OfType<Border>()
                .First(static border => AutomationProperties.GetName(border) == "End of the loaded rows");
            Assert.True(footer.IsEffectivelyVisible);
            Assert.Equal("Full", bulk.Content as string);
            Assert.True(bulk.IsEffectivelyVisible);
            Assert.True(bulk.IsEnabled);

            var spoken = AutomationProperties.GetName(bulk);
            Assert.NotNull(spoken);
            Assert.Contains("safety limit reached", spoken, StringComparison.Ordinal);
            Assert.Contains($"{1_500 - Ceiling:N0}", spoken, StringComparison.Ordinal);
        }
        finally
        {
            SessionTabViewModel.EntryRetentionLimitOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>
    /// The phone's status bar is one line, and the sentence has to end inside it.
    /// </summary>
    /// <remarks>
    /// On a Samsung SM-G990B at 480 x 1040 dp the long form rendered as
    /// <c>…refine filters to narrow t…</c>. A status line cut mid-word is worse than a
    /// shorter one, so the phone gets the short sentence and the footer control's name keeps
    /// the exact remainder.
    /// </remarks>
    [AvaloniaFact]
    public async Task ThePhoneLimitStatusFitsOnOneLine()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        SessionTabViewModel.EntryRetentionLimitOverride = Ceiling;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog(1_500), 420, 900);
            await SettleAsync(fixture);
            await fixture.View.ToggleLoadAllEntriesAsync();
            await SettleAsync(fixture);

            Assert.Equal($"Showing {Ceiling:N0} rows · limit reached", fixture.Tab.Status);
            Assert.True(
                fixture.Tab.Status.Length <= 40,
                $"the phone status line is {fixture.Tab.Status.Length} characters: {fixture.Tab.Status}");
        }
        finally
        {
            SessionTabViewModel.EntryRetentionLimitOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    /// <summary>The desktop keeps the whole sentence and names the ceiling in its label.</summary>
    [AvaloniaFact]
    public async Task TheDesktopLabelAndStatusNameTheCeiling()
    {
        SessionWorkspaceView.PhoneCompositionOverride = false;
        SessionTabViewModel.EntryRetentionLimitOverride = Ceiling;
        try
        {
            await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(ManyEntryLog(1_500));
            await SettleAsync(fixture);
            var bulk = BulkLoadButton(fixture.View);
            Assert.Equal($"Load up to {Ceiling:N0}", bulk.Content as string);

            await fixture.View.ToggleLoadAllEntriesAsync();
            await SettleAsync(fixture);

            Assert.Equal($"{Ceiling:N0}-row limit", bulk.Content as string);
            Assert.Contains("refine filters", fixture.Tab.Status, StringComparison.Ordinal);
        }
        finally
        {
            SessionTabViewModel.EntryRetentionLimitOverride = null;
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    // Avalonia's logical tree reaches a ContentControl's content through the control and
    // through its presenter, so a control in the footer band is enumerated twice. Distinct
    // by reference, then assert there is exactly one bulk-load button.
    private static Button BulkLoadButton(SessionWorkspaceView view) =>
        view.GetLogicalDescendants()
            .OfType<Button>()
            .Where(static button => AutomationProperties.GetName(button) is { } name &&
                (name.Contains("matching rows in batches", StringComparison.Ordinal) ||
                 name.Contains("safety limit reached", StringComparison.Ordinal)))
            .Distinct()
            .Single();

    /// <summary>Lets the first refresh's statistics arrive before the state under test.</summary>
    private static async Task SettleAsync(LiveTestWorkspaceFixture fixture)
    {
        for (var attempt = 0; attempt < 50 && fixture.Tab.MatchesInView is null; attempt++)
        {
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        fixture.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static string ManyEntryLog(int lines)
    {
        var log = new System.Text.StringBuilder();
        for (var index = 0; index < lines; index++)
        {
            log.Append(FormattableString.Invariant(
                $"01-01 00:00:{index % 60:00}.{index:000000}   100   101 I Worker         : line {index}"));
            log.Append('\n');
        }

        return log.ToString();
    }
}
