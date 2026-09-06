using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.Ports;
using VisualCat.Core.Store;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// Deleting captures from Recent captures: selection, confirmation, results and lifetime.
/// </summary>
public sealed class RecentCaptureDeletionTests
{
    /// <summary>The panel's own floor: below this, help yields before the list does.</summary>
    private const double ListFloor = 96;

    // ---- fixtures ----------------------------------------------------------------------

    private static TemporarySessionInfo Session(string name, long size = 4096, string? identity = null, DateTimeOffset? updated = null) =>
        new(
            Path.Combine(Path.GetTempPath(), $"20260905-100000-{name}-{Guid.NewGuid():N}.vcat"),
            updated ?? DateTimeOffset.UtcNow,
            size,
            true)
        {
            Identity = identity ?? "identity-" + name,
        };

    private static RecentCaptureSnapshot Snapshot(
        IReadOnlyList<TemporarySessionInfo> sessions,
        long generation = 1,
        IReadOnlyList<TemporarySessionInfo>? capturing = null,
        IReadOnlyList<TemporarySessionInfo>? busy = null,
        IReadOnlyList<TemporarySessionInfo>? open = null,
        bool available = true,
        int issues = 0,
        int pendingCleanup = 0,
        int unresolvedCleanup = 0) =>
        new(
            new CaptureInventory(sessions, available, issues, pendingCleanup, unresolvedCleanup),
            Paths(capturing),
            Paths(busy),
            Paths(open),
            generation);

    private static HashSet<string> Paths(IReadOnlyList<TemporarySessionInfo>? sessions) =>
        new((sessions ?? []).Select(session => session.Path), SessionPath.Comparer);

    private static RecentCaptureSnapshot Snapshot(int count = 3, int busy = -1, long generation = 1)
    {
        var sessions = Enumerable.Range(0, count).Select(i => Session("capture" + i)).ToArray();
        return Snapshot(sessions, generation, busy: busy < 0 ? null : [sessions[busy]]);
    }

    private static PreparedCapture Prepared(CaptureSelection item, bool open = false) =>
        new(new CaptureDeleteTarget(item.Session.Path, item.Session.Identity!, item.Session.SizeBytes), item.Label, open);

    private static RecentCaptureActions Actions(
        RecentCaptureSnapshot snapshot,
        Action? deleting = null,
        Func<IReadOnlyList<PreparedCapture>, IReadOnlyList<CaptureDeletionResult>>? results = null,
        Func<RecentCaptureSnapshot>? refresh = null,
        IReadOnlyList<CaptureExclusion>? excluded = null) => new(
        (selected, _) => Task.FromResult(new PreparedCaptures(
            Guid.NewGuid(),
            Path.GetTempPath(),
            selected
                .Where(item => (excluded ?? []).All(drop => drop.Selection.Session.Path != item.Session.Path))
                .Select(item => Prepared(item))
                .ToArray(),
            excluded ?? [])),
        (request, _, _) =>
        {
            deleting?.Invoke();
            return Task.FromResult(results is null
                ? (IReadOnlyList<CaptureDeletionResult>)request.Captures
                    .Select(capture => new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.Deleted)))
                    .ToArray()
                : results(request.Captures));
        },
        _ => Task.FromResult(refresh?.Invoke() ?? snapshot),
        _ => Task.CompletedTask);

    private static Button Find(Control root, string text) =>
        root.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, text));

    private static bool Has(Control root, string text) =>
        root.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, text) && button.IsEffectivelyVisible);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Toggle(CheckBox box) => ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(box)!).Toggle();

    private static CheckBox SelectAll(Control root) =>
        root.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "Select all"));

    private static IEnumerable<CheckBox> RowChecks(Control root) =>
        root.GetVisualDescendants().OfType<CheckBox>().Where(box => !Equals(box.Content, "Select all"));

    private static IEnumerable<string> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty);

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 700, Height = 720 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class CapturingHost : IDialogHost
    {
        internal object? Body { get; private set; }

        internal int Shown { get; private set; }

        public Task<T?> ShowDialogAsync<T>(DialogBody<T> body)
        {
            Body = body;
            Shown++;
            return body.Completion;
        }
    }

    // ---- the feature switch ------------------------------------------------------------

    /// <summary>T-U1. Without deletion hooks nothing destructive is built at all.</summary>
    [AvaloniaFact]
    public void LegacyConstructionHasNoDestructiveControls()
    {
        var dialog = new RecentSessionsDialog([Session("only")], null);
        var window = Show(dialog);
        try
        {
            Assert.Empty(dialog.GetVisualDescendants().OfType<CheckBox>());
            Assert.False(Has(dialog, "Select"));
            Assert.False(Has(dialog, "Delete captures…"));
            Assert.Empty(dialog.DeletionResults);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U1. The initial empty state is unchanged: no result, no legend, one action.</summary>
    [AvaloniaFact]
    public void InitialEmptyStateOffersOnlyTheActionThatChangesIt()
    {
        var snapshot = Snapshot([]);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            Assert.Contains("No captures on this device yet.", Texts(dialog));
            Assert.DoesNotContain(Texts(dialog), text => text.Contains("A complete capture", StringComparison.Ordinal));
            Assert.False(Has(dialog, "Open"));
            Assert.False(Has(dialog, "Details"));

            // The capture affordance belongs to the composition that can actually start one.
            Assert.False(Has(dialog, "Capture this device's log"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The touch composition keeps the one action that changes an empty list.</summary>
    [AvaloniaFact]
    public void ThePhoneEmptyStateStillOffersToStartACapture()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot([]);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var window = Show(dialog);
            try
            {
                Assert.True(Has(dialog, "Capture this device's log"));
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>Section 9.3. One capture reads as one capture, not as "these 1 capture".</summary>
    [AvaloniaFact]
    public async Task ConfirmationCopyIsGrammaticalForASingleCapture()
    {
        var snapshot = Snapshot(1);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            try
            {
                var copy = string.Join("|", Texts(confirmation));
                Assert.Contains("This capture (about", copy, StringComparison.Ordinal);
                Assert.DoesNotContain("These 1", copy, StringComparison.Ordinal);
                Assert.Contains("Any open tab for this capture will close first.", copy, StringComparison.Ordinal);

                // Cancel reads before the destructive action and is the default.
                var buttons = confirmation.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Content is "Cancel" or "Delete permanently")
                    .Select(button => (string)button.Content!)
                    .ToArray();
                Assert.Equal(["Cancel", "Delete permanently"], buttons);
            }
            finally
            {
                child.Close();
            }
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    // ---- selection ---------------------------------------------------------------------

    /// <summary>
    /// T-U3, section 13. The one selection state Android cannot carry is said in words.
    /// </summary>
    /// <remarks>
    /// An accessibility node is checked or not; there is no third value. On the device a mixed
    /// select-all reached TalkBack as plainly unticked, so a reader who had chosen two captures
    /// out of five was told nothing was selected. The row checks report themselves correctly and
    /// need no help; only this one does.
    /// </remarks>
    [AvaloniaFact]
    public void TheMixedSelectAllSaysSoWhereItsStateCannotBeCarried()
    {
        var snapshot = Snapshot(3);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var all = SelectAll(dialog);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(all)), "nothing selected needs no explanation");

            Toggle(RowChecks(dialog).First());
            Assert.Null(all.IsChecked);
            Assert.Equal("Some captures are selected.", AutomationProperties.GetHelpText(all));

            Toggle(all);
            Assert.True(all.IsChecked);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(all)), "a full selection carries its own state");
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>T-U3, T-U4. false → mixed → true → false, and a busy capture is never in it.</summary>
    [AvaloniaFact]
    public void SelectAllHasCorrectMixedCycleAndSkipsBusyRows()
    {
        var snapshot = Snapshot(busy: 2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var all = SelectAll(dialog);
            Assert.False(all.IsChecked);
            Toggle(RowChecks(dialog).First(box => box.IsEnabled));
            Assert.Null(all.IsChecked);
            Toggle(all);
            Assert.True(all.IsChecked);
            Assert.Equal(2, RowChecks(dialog).Count(box => box.IsChecked == true));
            Toggle(all);
            Assert.False(all.IsChecked);
            Assert.All(RowChecks(dialog), box => Assert.False(box.IsChecked));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U4, section 8.6. The denominator is eligible captures, and the rest is explained.</summary>
    [AvaloniaFact]
    public void ProtectedCapturesAreExplainedAndExcludedFromTheDenominator()
    {
        var sessions = new[] { Session("a"), Session("b"), Session("c") };
        var snapshot = Snapshot(sessions, capturing: [sessions[0]], busy: [sessions[1]]);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            // The noun agrees with the denominator: one eligible capture is one capture.
            Assert.Contains("1 of 1 available capture selected", string.Join("|", Texts(dialog)), StringComparison.Ordinal);
            Assert.Contains(Texts(dialog), text => text.Contains("is being recorded and 1 capture is in use", StringComparison.Ordinal));
            Assert.Contains(Texts(dialog), text => text == "This capture is being recorded. Stop the capture first.");
            Assert.Contains(Texts(dialog), text => text == "VisualCat is still working on this capture. Wait for it to finish.");
            Assert.Equal("Delete 1 capture…", Find(dialog, "Delete 1 capture…").Content);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U4. Zero eligible captures disables select-all and Delete, and says why.</summary>
    [AvaloniaFact]
    public void EveryCaptureProtectedDisablesDeletionWithAVisibleReason()
    {
        var sessions = new[] { Session("a") };
        var snapshot = Snapshot(sessions, capturing: sessions);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            Assert.False(SelectAll(dialog).IsEnabled);
            Assert.False(Find(dialog, "Delete captures…").IsEnabled);
            Assert.Contains(Texts(dialog), text => text.Contains("is being recorded", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// T-U18. Select all draws its own box. A derived control with no style key has no control
    /// theme, and on the device the one checkbox carrying unchecked, mixed and checked drew
    /// nothing but its label while automation reported the state correctly.
    /// </summary>
    [AvaloniaFact]
    public void SelectAllDrawsItsStateAndDoesNotRelyOnAutomationAlone()
    {
        var snapshot = Snapshot(2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var all = SelectAll(dialog);
            Assert.NotNull(all.GetVisualChildren().FirstOrDefault());
            Assert.True(all.Bounds.Width > 60, $"select all is {all.Bounds.Width} wide, so it has no box");

            Toggle(all);
            window.UpdateLayout();
            Assert.True(all.IsChecked);
            Assert.Contains(all.GetVisualDescendants().OfType<Control>(), control => control.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U19. Highlight is what Open acts on; checks are what Delete acts on.</summary>
    [AvaloniaFact]
    public void DesktopHighlightAndDeleteChecksAreIndependent()
    {
        var snapshot = Snapshot();
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.False(Find(dialog, "Open").IsEnabled);
            list.SelectedIndex = 1;
            Toggle(RowChecks(dialog).First());
            Assert.Equal(1, list.SelectedIndex);
            Assert.True(Find(dialog, "Open").IsEnabled);

            // Escape clears checks first and only then ends the dialog.
            dialog.Dismiss();
            Assert.False(dialog.Completion.IsCompleted);
            dialog.Dismiss();
            Assert.True(dialog.Completion.IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U10, T-U15. Select mode, Back, and the measured touch floor.</summary>
    [AvaloniaFact]
    public void MobileSelectionBackAndTouchFloorsRemainUsable()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot();
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var window = new Window { Content = dialog, Width = 360, Height = 720 };
            window.Show();
            window.UpdateLayout();
            try
            {
                Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<CheckBox>(), box => box.IsEffectivelyVisible);
                Click(Find(dialog, "Select"));
                window.UpdateLayout();
                Assert.Contains(dialog.GetVisualDescendants().OfType<CheckBox>(), box => box.IsEffectivelyVisible);

                // A tap opens a capture here, so a highlight-driven Open could only ever be
                // permanently disabled.
                Assert.False(Has(dialog, "Open"));
                foreach (var control in dialog.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is Button or CheckBox && control.IsEffectivelyVisible))
                {
                    Assert.True(control.Bounds.Width >= 48, $"{control} width {control.Bounds.Width}");
                    Assert.True(control.Bounds.Height >= 48, $"{control} height {control.Bounds.Height}");
                }

                // Back leaves selection before it leaves the dialog.
                dialog.Dismiss();
                Assert.False(dialog.Completion.IsCompleted);
                Assert.NotNull(Find(dialog, "Select"));
                dialog.Dismiss();
                Assert.True(dialog.Completion.IsCompleted);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>Section 10. A phone row is at least a list item tall, not just a target.</summary>
    [AvaloniaFact]
    public void PhoneRowsMeetTheListItemHeight()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot();
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var window = new Window { Content = dialog, Width = 360, Height = 720 };
            window.Show();
            window.UpdateLayout();
            try
            {
                var rows = dialog.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                Assert.NotEmpty(rows);
                Assert.All(rows, row => Assert.True(row.Bounds.Height >= 56, $"row height {row.Bounds.Height}"));
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>
    /// Section 8.3. A short viewport keeps the list and the decision row. Every other row sizes
    /// to its content, so their sum took the whole card on a landscape phone and the list — the
    /// one row that should have absorbed the remainder — measured about a pixel.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1.0, 1040, 360)]
    [InlineData(1.8, 620, 394)]
    public void AShortViewportKeepsTheListAndTheActions(double scale, int width, int height)
    {
        RecentSessionsDialog.MobileOverride = true;
        var platform = TextScale.Platform;
        TextScale.Platform = scale;
        try
        {
            var snapshot = Snapshot(6);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));

            // The landscape band this phone gives the sheet, in its own logical pixels.
            // At 1.8 the phone's landscape sheet measured 620 x 394 dp, read off the card's
            // own accessibility bounds on the device. It is also where compaction used to
            // oscillate: leaving it restored more help than the fixed reserve had allowed for,
            // the list fell back under its floor, and Avalonia reported an infinite layout
            // pass — so this case fails outright, not merely by a measurement, if the reserve
            // stops scaling with the reader's type.
            var window = new Window { Content = dialog, Width = width, Height = height };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            try
            {
                // Select mode is the tallest state: it adds the selection bar above the list.
                Click(Find(dialog, "Select"));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();

                // The list never falls below the room it is promised. At 1.8 in landscape it
                // measured 37 dp on the device — less than a third of one row — while a whole
                // band below it held three reconciling buttons.
                // The list-item floor, which is what a row a thumb taps is worth. A dp figure
                // cannot be the promise here: at 1.8 one row is 119 dp, and a sheet this short
                // cannot hold a whole one alongside a selection bar, a status line and a
                // decision row. What it must never be again is the 37 dp — under a third of a
                // row — that the device drew before the reconciling actions gave up their band.
                Assert.True(list.Bounds.Height >= 56, $"list collapsed to {list.Bounds.Height}");

                // Help never keeps room the list needs: the legend is only on screen once the
                // list has its floor without it.
                var legend = dialog.GetVisualDescendants().OfType<Expander>()
                    .Any(expander => Equals(expander.Header, "Capture states") && expander.IsEffectivelyVisible);
                Assert.True(
                    !legend || list.Bounds.Height >= ListFloor,
                    $"legend kept while the list had {list.Bounds.Height}");

                // And nothing left the card to pay for it — every action is still reachable.
                foreach (var name in new[] { "Refresh", "Delete…", "Close" })
                {
                    var action = Find(dialog, name);
                    Assert.True(action.IsEffectivelyVisible, $"{name} disappeared");
                    Assert.True(action.Bounds.Bottom <= dialog.Bounds.Height + 1, $"{name} left the card");
                    Assert.True(action.Bounds.Right <= dialog.Bounds.Width + 1, $"{name} ran off the side");
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
            TextScale.Platform = platform;
        }
    }

    /// <summary>
    /// The last capture going leaves a decision row that still fits the card.
    /// </summary>
    /// <remarks>
    /// The empty sheet swaps Delete and Select for <em>Capture this device's log</em>, and on a
    /// short viewport the reconciling actions are already in that row. Five buttons at 1.8x do
    /// not fit across a phone in landscape: on the device the last one ran off the right edge,
    /// because the row's decision to stack was taken from buttons a fill had just added and had
    /// therefore never measured, and because an empty sheet stopped answering the fold at all.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.8)]
    public async Task AnEmptySheetsActionsStayInsideTheCard(double scale)
    {
        RecentSessionsDialog.MobileOverride = true;
        var platform = TextScale.Platform;
        TextScale.Platform = scale;
        try
        {
            var sessions = new[] { Session("a"), Session("b") };
            var snapshot = Snapshot(sessions, pendingCleanup: 1);
            var empty = Snapshot([], generation: 2, pendingCleanup: 1);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, refresh: () => empty));
            var host = new CapturingHost();
            dialog.Host = host;

            // The band a phone's sheet gets in landscape, where the actions are folded.
            var window = new Window { Content = dialog, Width = 620, Height = 310 };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Click(Find(dialog, "Select"));
                Toggle(SelectAll(dialog));
                Click(Find(dialog, "Delete 2…"));
                await Settle();
                var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
                var child = Show(confirmation);
                Click(Find(confirmation, "Delete permanently"));
                child.Close();
                await Settle();
                for (var i = 0; i < 6; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Contains("No captures remain in temporary storage.", Texts(dialog));

                // Every action the empty sheet offers is inside the card it is drawn on.
                var actions = dialog.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible && button.Content is string)
                    .ToArray();
                Assert.Contains(actions, button => Equals(button.Content, "Capture this device's log"));
                Assert.Contains(actions, button => Equals(button.Content, "Retry storage cleanup"));
                foreach (var button in actions)
                {
                    // Sideways is the one the card cannot recover from: the row wraps, so a
                    // button that runs off the right edge is simply unreachable. Vertical fit
                    // belongs to the state that still has a list to yield, and is asserted in
                    // AShortViewportKeepsTheListAndTheActions.
                    var origin = button.TranslatePoint(default, dialog)!.Value;
                    Assert.True(
                        origin.X >= -0.5 && origin.X + button.Bounds.Width <= dialog.Bounds.Width + 0.5,
                        $"{button.Content} ran off the side: {origin.X}+{button.Bounds.Width} in {dialog.Bounds.Width}");
                }
            }
            finally
            {
                dialog.ForceDismiss();
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
            TextScale.Platform = platform;
        }
    }

    /// <summary>
    /// The reconciling actions follow the viewport back and forth, not only the one time the
    /// list crosses its floor.
    /// </summary>
    /// <remarks>
    /// The fold was first decided inside the render pass, which does not run on every layout
    /// pass — so a sheet that changed height without the list crossing its floor kept whichever
    /// arrangement it happened to have. A rotation into a roomier viewport is exactly that.
    /// </remarks>
    [AvaloniaFact]
    public void TheReconcilingActionsFollowTheViewportInBothDirections()
    {
        RecentSessionsDialog.MobileOverride = true;
        var platform = TextScale.Platform;
        TextScale.Platform = 1.8;
        try
        {
            var snapshot = Snapshot(6);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            // Either side of the fold's own threshold (18 line boxes = 648 at this scale) and
            // on the same side of the list's floor throughout, so the only thing that changes
            // is the fold. Deciding it inside the render pass missed exactly this.
            var window = new Window { Content = dialog, Width = 620, Height = 600 };
            window.Show();
            try
            {
                Settle(window);
                Click(Find(dialog, "Select"));
                Settle(window);
                Assert.True(Folded(dialog), "a short sheet must fold the actions into the decision row");

                // Roomier: they belong beside the status line they explain.
                window.Height = 760;
                Settle(window);
                Assert.False(Folded(dialog), "a taller sheet must give them their own row back");

                // And back, without the reader having to reopen anything.
                window.Height = 600;
                Settle(window);
                Assert.True(Folded(dialog), "the fold must follow the viewport back down");

                // Wherever they are, they are reachable and on the card.
                foreach (var name in new[] { "Refresh", "Close" })
                {
                    var action = Find(dialog, name);
                    Assert.True(action.IsEffectivelyVisible, $"{name} disappeared");
                    Assert.True(action.Bounds.Bottom <= dialog.Bounds.Height + 1, $"{name} left the card");
                }
            }
            finally
            {
                dialog.ForceDismiss();
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
            TextScale.Platform = platform;
        }

        static void Settle(Window window)
        {
            for (var i = 0; i < 6; i++)
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        }

        // Folded means the button sits in the same panel as Close rather than in its own row.
        static bool Folded(Control dialog) =>
            ReferenceEquals(Find(dialog, "Refresh").Parent, Find(dialog, "Close").Parent);
    }

    /// <summary>
    /// Section 8.3. At the size the desktop dialog actually opens at, the sheet has room for
    /// its list and for the explanation of what the states mean.
    /// </summary>
    [AvaloniaFact]
    public void TheDefaultDesktopSizeHasRoomForTheListAndTheStateLegend()
    {
        var snapshot = Snapshot(4);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = new Window
        {
            Content = dialog,
            Width = dialog.PreferredSize.Width,
            Height = dialog.PreferredSize.Height,
        };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.True(list.Bounds.Height >= 200, $"list is only {list.Bounds.Height} tall");
            Assert.Contains(
                dialog.GetVisualDescendants().OfType<Expander>(),
                expander => Equals(expander.Header, "Capture states") && expander.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U12. A tap on the check never opens the capture it belongs to.</summary>
    [AvaloniaFact]
    public void CheckboxActivationNeverOpensACapture()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot();
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var window = Show(dialog);
            try
            {
                Click(Find(dialog, "Select"));
                window.UpdateLayout();
                var check = RowChecks(dialog).First();
                Toggle(check);
                check.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, new PointerEventArgs(
                    InputElement.PointerMovedEvent, check, new Pointer(0, PointerType.Touch, true),
                    check, default, 0, default, KeyModifiers.None)));
                Dispatcher.UIThread.RunJobs();
                Assert.False(dialog.Completion.IsCompleted);
                Assert.True(check.IsChecked);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>
    /// Section 8.8. A reader who deleted from the list gets the list back — the nearest
    /// survivor — rather than being dropped at the footer with the rows behind them.
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingFromTheListLeavesTheKeyboardOnTheNearestSurvivor()
    {
        var sessions = new[] { Session("a"), Session("b"), Session("c") };
        var remaining = Snapshot([sessions[0], sessions[2]], generation: 2);
        var dialog = new RecentSessionsDialog(
            Snapshot(sessions),
            Actions(
                Snapshot(sessions),
                results: captures => captures
                    .Select(capture => new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.Deleted)))
                    .ToArray(),
                refresh: () => remaining));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 1;
            list.ContainerFromIndex(1)!.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently"));
            child.Close();
            await Settle();
            window.UpdateLayout();
            await Settle();

            Assert.Equal(2, ((IEnumerable<object>)list.ItemsSource!).Count());
            Assert.True(
                Enumerable.Range(0, 2).Any(index => list.ContainerFromIndex(index)?.IsKeyboardFocusWithin == true),
                "the keyboard left the list after deleting from it");
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>T-U13. Keyboard checks, clears and confirms without leaving the list.</summary>
    [AvaloniaFact]
    public async Task KeyboardChecksClearsAndConfirmsFromTheList()
    {
        var snapshot = Snapshot();
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 0;

            // Focus lands on the realised row, which is where a keyboard reader would be and
            // where the panel's handler sees the event bubble from.
            list.ContainerFromIndex(0)!.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, RowChecks(dialog).Count(box => box.IsChecked == true));

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, RowChecks(dialog).Count(box => box.IsChecked == true));

            var container = list.ContainerFromIndex(0)!;
            Assert.True(container.IsKeyboardFocusWithin, "focus left the list after Ctrl+A");
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, RowChecks(dialog).Count(box => box.IsChecked == true));

            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            await Settle();
            Assert.IsType<CaptureDeleteConfirmation>(host.Body);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>
    /// Section 8.7. Escape works from anywhere in the dialog, including when nothing inside it
    /// has taken the focus yet.
    /// </summary>
    /// <summary>
    /// T-U13, section 8.8. The keys the list does not own, and the one the focused control does.
    /// </summary>
    /// <remarks>
    /// Navigation moves the highlight and changes no check; Enter opens what the highlight is
    /// on; and Space belongs to a focused checkbox alone — a row that also toggled would undo
    /// the checkbox's own answer in the same keystroke.
    /// </remarks>
    [AvaloniaFact]
    public async Task NavigationOpensTheHighlightAndSpaceBelongsToTheFocusedControl()
    {
        var sessions = new[] { Session("a"), Session("b"), Session("c") };
        var snapshot = Snapshot(sessions);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 0;
            list.ContainerFromIndex(0)!.Focus();
            Dispatcher.UIThread.RunJobs();

            // Arrows and Home/End move through the list and leave every check alone.
            foreach (var key in new[] { PhysicalKey.ArrowDown, PhysicalKey.ArrowDown, PhysicalKey.Home, PhysicalKey.End })
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(0, RowChecks(dialog).Count(box => box.IsChecked == true));
            Assert.Equal(2, list.SelectedIndex);

            // Space on a focused checkbox is the checkbox's: exactly one capture ends checked,
            // and the row it sits in does not answer the same keystroke as well. A button
            // completes on the release, so the release is part of the keystroke.
            var check = RowChecks(dialog).ElementAt(1);
            check.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(check.IsChecked, "the focused checkbox did not answer Space");
            Assert.Equal(1, RowChecks(dialog).Count(box => box.IsChecked == true));

            // Enter opens whatever the highlight is on, and the dialog answers with its path.
            list.ContainerFromIndex(2)!.Focus();
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Settle();
            Assert.True(dialog.Completion.IsCompleted, "Enter did not open the highlighted capture");
            Assert.Equal(sessions[2].Path, await dialog.Completion);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeWorksEvenWhenTheWindowItselfHoldsTheFocus()
    {
        var snapshot = Snapshot(2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            Toggle(RowChecks(dialog).First());
            Assert.Equal(1, RowChecks(dialog).Count(box => box.IsChecked == true));

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, RowChecks(dialog).Count(box => box.IsChecked == true));
            Assert.False(dialog.Completion.IsCompleted);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.Completion.IsCompleted);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// T-U9, T-U10. One Back press takes down one layer on the phone. Android delivers Back as
    /// an Escape key-down and then the platform callback the host answers, so a dialog that
    /// also answers the key leaves selection and closes itself for a single press.
    /// </summary>
    [AvaloniaFact]
    public void OneBackPressTakesDownOneLayerOnThePhone()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot(2);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var window = Show(dialog);
            try
            {
                Click(Find(dialog, "Select"));
                window.UpdateLayout();
                Toggle(RowChecks(dialog).First());

                // The key-down half of the gesture changes nothing.
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, RowChecks(dialog).Count(box => box.IsChecked == true));

                // The host's back callback is the half that acts, and it leaves selection first.
                dialog.Dismiss();
                window.UpdateLayout();
                Assert.False(dialog.Completion.IsCompleted);
                Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<CheckBox>(), box => box.IsEffectivelyVisible);

                dialog.Dismiss();
                Assert.True(dialog.Completion.IsCompleted);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    // ---- confirmation ------------------------------------------------------------------

    /// <summary>T-U6, T-U16. Cancel is the default and a second Delete cannot re-enter.</summary>
    [AvaloniaFact]
    public async Task ConfirmationDefaultsToCancelAndDuplicateDeleteCannotReenter()
    {
        var snapshot = Snapshot();
        var calls = 0;
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, () => calls++));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            var delete = Find(dialog, "Delete 3 captures…");
            Click(delete);
            Click(delete);
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            try
            {
                Assert.True(Find(confirmation, "Cancel").IsDefault);
                Assert.False(Find(confirmation, "Delete permanently").IsDefault);
                Assert.Contains(Texts(confirmation), text => text.Contains("cannot be undone", StringComparison.Ordinal));
                Assert.Contains(Texts(confirmation), text => text == "Any open tabs for these captures will close first.");
                Assert.Contains(Texts(confirmation), text => text.Contains("Saved copies", StringComparison.Ordinal));
                Click(Find(confirmation, "Cancel"));
            }
            finally
            {
                child.Close();
            }

            await Settle();
            Assert.Equal(1, host.Shown);
            Assert.Equal(0, calls);
            Assert.True(delete.IsEnabled);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>T-U16. Above five names, the whole frozen set is still reviewable.</summary>
    [AvaloniaFact]
    public async Task ConfirmationExposesEveryTargetBeyondTheFirstFive()
    {
        var snapshot = Snapshot(count: 12);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 12 captures…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            try
            {
                Assert.Contains(Texts(confirmation), text => text == "And 7 more captures.");
                var review = confirmation.GetVisualDescendants().OfType<Expander>().Single();
                Assert.Equal("Review all 12", review.Header);
                var all = Assert.IsType<ListBox>(review.Content);
                Assert.Equal(12, ((IEnumerable<string>)all.ItemsSource!).Count());
            }
            finally
            {
                child.Close();
            }
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>
    /// Section 8.7. Declining a confirmation puts the sheet back the way it was, including the
    /// line that says what a tap does.
    /// </summary>
    /// <remarks>
    /// Preparation writes <em>Checking selected captures…</em> over that line, and Cancel used
    /// to replace it with the result summary — which is empty before anything has been deleted.
    /// The reader was returned to select mode with their checks intact and the only sentence
    /// explaining the mode gone.
    /// </remarks>
    [AvaloniaFact]
    public async Task DecliningAConfirmationRestoresTheSelectModeLine()
    {
        RecentSessionsDialog.MobileOverride = true;
        try
        {
            var snapshot = Snapshot(count: 2);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var host = new CapturingHost();
            dialog.Host = host;
            var window = new Window { Content = dialog, Width = 360, Height = 720 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Click(Find(dialog, "Select"));
                Toggle(SelectAll(dialog));
                Click(Find(dialog, "Delete 2…"));
                await Settle();
                var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
                confirmation.Dismiss();
                await Settle();

                Assert.Contains(Texts(dialog), text => text == "Selecting captures. Tap a capture to select it.");
                Assert.Equal(2, RowChecks(dialog).Count(box => box.IsChecked == true));
            }
            finally
            {
                dialog.ForceDismiss();
                window.Close();
            }
        }
        finally
        {
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>
    /// Section 9. The overflow line counts, so exactly six captures do not read "1 more captures".
    /// </summary>
    [AvaloniaFact]
    public async Task ConfirmationOverflowLineAgreesWithItsNumber()
    {
        var snapshot = Snapshot(count: 6);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 6 captures…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            try
            {
                Assert.Contains(Texts(confirmation), text => text == "And 1 more capture.");
            }
            finally
            {
                child.Close();
            }
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>Section 7.1. Preparation drops what it cannot freeze and confirms the rest.</summary>
    [AvaloniaFact]
    public async Task PreparationDropsProtectedCapturesAndStillConfirmsTheRest()
    {
        var sessions = new[] { Session("a"), Session("b") };
        var snapshot = Snapshot(sessions);
        var excluded = new[]
        {
            new CaptureExclusion(new CaptureSelection(sessions[0], "a"), CaptureProtection.Recording, CaptureDeleteOutcome.Protected),
        };
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, excluded: excluded));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 2 captures…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            Assert.Equal("Delete 1 capture?", confirmation.DialogTitle);
            Assert.Contains(Texts(dialog), text =>
                text.Contains("removed from this deletion", StringComparison.Ordinal) &&
                text.Contains("being recorded", StringComparison.Ordinal));
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>Section 8.1. Nothing at all to freeze means no confirmation is shown.</summary>
    [AvaloniaFact]
    public async Task NothingLeftToConfirmNeverShowsAnEmptyConfirmation()
    {
        var sessions = new[] { Session("a") };
        var snapshot = Snapshot(sessions);
        var excluded = new[]
        {
            new CaptureExclusion(new CaptureSelection(sessions[0], "a"), CaptureProtection.Working, CaptureDeleteOutcome.Protected),
        };
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, excluded: excluded));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            Assert.Equal(0, host.Shown);
            Assert.Empty(dialog.DeletionResults);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EveryPreparationExclusionRemainsInDetailsAfterRefresh()
    {
        var snapshot = Snapshot(7);
        var excluded = snapshot.Inventory.Sessions.Select((session, index) =>
            new CaptureExclusion(new CaptureSelection(session, "Capture " + index),
                index % 2 == 0 ? CaptureProtection.Working : CaptureProtection.Recording,
                CaptureDeleteOutcome.Protected)).ToArray();
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, excluded: excluded));
        var host = new CapturingHost(); dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 7 captures…"));
            await Settle();
            dialog.RequestRefresh();
            await Settle();
            Assert.True(Has(dialog, "Details"));
            Click(Find(dialog, "Details"));
            await Settle();
            var details = Assert.IsType<CaptureResultDetails>(host.Body);
            var child = Show(details);
            try
            {
                var list = Assert.Single(details.GetVisualDescendants().OfType<ListBox>());
                Assert.Equal(excluded.Select(item => item.Selection.Label + "\nNot included in deletion. " + item.Reason),
                    list.ItemsSource!.Cast<string>());
            }
            finally { child.Close(); }
        }
        finally { dialog.ForceDismiss(); window.Close(); }
    }

    // ---- results -----------------------------------------------------------------------

    /// <summary>T-U7, T-U11. The last deletion keeps its result and its cleanup status.</summary>
    [AvaloniaFact]
    public async Task LastDeleteKeepsResultsAndSaysTheListIsNowEmpty()
    {
        var snapshot = Snapshot(1);
        var empty = Snapshot([], generation: 2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, refresh: () => empty));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            Click(Find(confirmation, "Delete permanently"));
            child.Close();
            await Settle();
            window.UpdateLayout();

            Assert.Single(dialog.DeletionResults);
            Assert.False(Has(dialog, "Open"));
            Assert.True(Has(dialog, "Details"));
            Assert.Contains("No captures remain in temporary storage.", Texts(dialog));
            Assert.Contains(Texts(dialog), text => text.StartsWith("Deleted 1 capture.", StringComparison.Ordinal));
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>
    /// T-U8, T-U17. Success, failure, absence and pending cleanup all survive together, the
    /// eligible failures keep their checks, and every one of them is reachable in Details.
    /// </summary>
    [AvaloniaFact]
    public async Task MixedResultsKeepEveryCategoryAndReachDetails()
    {
        var sessions = new[] { Session("a"), Session("b"), Session("c"), Session("d") };
        var snapshot = Snapshot(sessions);
        var outcomes = new[]
        {
            CaptureDeleteOutcome.Deleted,
            CaptureDeleteOutcome.Denied,
            CaptureDeleteOutcome.AlreadyMissing,
            CaptureDeleteOutcome.DeletedPendingReclaim,
        };
        var actions = Actions(snapshot, results: captures =>
            captures.Select((capture, i) => new CaptureDeletionResult(capture, new(capture.Target, outcomes[i]))).ToArray());
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 4 captures…"));
            await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently"));
            child.Close();
            await Settle();

            var status = string.Join("|", Texts(dialog));
            Assert.Contains("Deleted 2 captures.", status, StringComparison.Ordinal);
            Assert.Contains("1 capture could not be deleted.", status, StringComparison.Ordinal);
            Assert.Contains("1 capture was already missing.", status, StringComparison.Ordinal);
            Assert.Contains("Storage cleanup is pending", status, StringComparison.Ordinal);

            // The failed capture is still listed, still checked, and says why.
            Assert.Contains(Texts(dialog), text => text == "VisualCat does not have permission to remove this capture.");
            Assert.True(Has(dialog, "Retry storage cleanup"));

            Click(Find(dialog, "Details"));
            await Settle();
            var details = Assert.IsType<CaptureResultDetails>(host.Body);
            var detailsWindow = Show(details);
            try
            {
                var lines = string.Join("|", Texts(details));
                Assert.Contains("does not have permission", lines, StringComparison.Ordinal);
                Assert.Contains("already missing", lines, StringComparison.Ordinal);
            }
            finally
            {
                detailsWindow.Close();
            }
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>T-U21. An unexpected failure preserves what is known and admits the rest.</summary>
    [AvaloniaFact]
    public async Task UnexpectedFailureAfterWorkBeganPreservesKnownResults()
    {
        var snapshot = Snapshot(2);
        var actions = new RecentCaptureActions(
            (selected, _) => Task.FromResult(new PreparedCaptures(
                Guid.NewGuid(), Path.GetTempPath(), selected.Select(item => Prepared(item)).ToArray(), [])),
            (_, _, _) => throw new InvalidOperationException("injected"),
            _ => Task.FromResult(snapshot),
            _ => Task.CompletedTask);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 2 captures…"));
            await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently"));
            child.Close();
            await Settle();

            Assert.Equal(2, dialog.DeletionResults.Count);
            Assert.All(dialog.DeletionResults, result => Assert.Equal(CaptureDeleteOutcome.Unknown, result.File.Outcome));
            var status = string.Join("|", Texts(dialog));
            Assert.Contains("The deletion results for 2 captures could not be verified.", status, StringComparison.Ordinal);
            Assert.Contains("Something went wrong.", status, StringComparison.Ordinal);

            // Controls come back rather than leaving the dialog stuck busy.
            Assert.True(Find(dialog, "Close").IsEnabled);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    // ---- reconciliation ----------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACommittedResultSurvivesABrokenOrCancelledBatch(bool cancelled)
    {
        var snapshot = Snapshot(2);
        var known = new List<CaptureDeletionResult>();
        var actions = new RecentCaptureActions(
            (selected, _) => Task.FromResult(new PreparedCaptures(
                Guid.NewGuid(), Path.GetTempPath(), selected.Select(item => Prepared(item)).ToArray(), [])),
            (request, _, _) =>
            {
                var first = request.Captures[0];
                known.Add(new(first, new(first.Target, CaptureDeleteOutcome.Deleted)));
                if (cancelled) throw new OperationCanceledException();
                throw new InvalidOperationException("injected after commit");
            },
            _ => Task.FromResult(snapshot), _ => Task.CompletedTask, () => known);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost(); dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog)); Click(Find(dialog, "Delete 2 captures…")); await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently")); child.Close(); await Settle();
            Assert.Equal(2, dialog.DeletionResults.Count);
            Assert.Single(dialog.DeletionResults, result => result.File.Committed);
            Assert.Single(dialog.DeletionResults, result => result.File.Outcome == CaptureDeleteOutcome.Unknown);
            Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>().Single().Items);
            Assert.Contains(Texts(dialog), text => text.Contains("Deleted 1 capture.", StringComparison.Ordinal));
            Assert.True(Find(dialog, "Close").IsEnabled);
        }
        finally { dialog.ForceDismiss(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task QueuedProgressCannotOverwriteStoppingOrTheFinalResult()
    {
        var snapshot = Snapshot(1);
        var finished = new TaskCompletionSource<IReadOnlyList<CaptureDeletionResult>>();
        IProgress<CaptureDeletionProgress>? progress = null;
        PreparedCaptures? request = null;
        var actions = new RecentCaptureActions(
            (selected, _) => Task.FromResult(new PreparedCaptures(
                Guid.NewGuid(), Path.GetTempPath(), selected.Select(item => Prepared(item)).ToArray(), [])),
            (prepared, reporter, _) => { request = prepared; progress = reporter; return finished.Task; },
            _ => Task.FromResult(snapshot), _ => Task.CompletedTask);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost(); dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog)); Click(Find(dialog, "Delete 1 capture…")); await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently")); child.Close(); await Settle();
            Click(Find(dialog, "Stop"));
            var stale = new CaptureDeletionProgress(request!.Operation, CaptureDeletionPhase.Deleting, 1, 0, 1, "capture");
            progress!.Report(stale); await Settle();
            Assert.Contains(Texts(dialog), text => text.StartsWith("Stopping", StringComparison.Ordinal));
            Assert.False(Find(dialog, "Stopping…").IsEnabled);
            var capture = Assert.Single(request.Captures);
            finished.SetResult([new(capture, new(capture.Target, CaptureDeleteOutcome.Cancelled))]);
            await Settle(); progress.Report(stale); await Settle();
            Assert.DoesNotContain(Texts(dialog), text => text.StartsWith("Deleting capture", StringComparison.Ordinal));
            Assert.True(Find(dialog, "Close").IsEnabled);
        }
        finally { finished.TrySetResult([]); dialog.ForceDismiss(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1.0)]
    [InlineData(false, 1.8)]
    [InlineData(true, 1.8)]
    public void NarrowPhoneActionsAndRowsRemainUsableWithEnlargedText(bool dark, double scale)
    {
        var platform = TextScale.Platform; var user = TextScale.User;
        TextScale.Platform = 1; TextScale.User = scale;
        RecentSessionsDialog.MobileOverride = true;
        var snapshot = Snapshot(8, busy: 2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = new Window
        {
            Content = new Border { Padding = new Avalonia.Thickness(12), Child = dialog },
            // The content region left by a 360 dp phone's sheet frame and title.
            Width = 336,
            Height = 500,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light,
        };
        window.Show();
        try
        {
            window.UpdateLayout(); Click(Find(dialog, "Select")); Toggle(SelectAll(dialog));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(TextScale.Of(14), Find(dialog, "Delete 7…").FontSize);
            Assert.True(dialog.GetVisualDescendants().OfType<ListBox>().Single().Bounds.Height >= 64,
                string.Join("\n", dialog.GetVisualDescendants().OfType<Grid>().Take(8).Select(grid =>
                    $"{grid.Bounds}: {string.Join(',', grid.RowDefinitions.Select(row => row.ActualHeight))}")) +
                string.Join("\n", dialog.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible)
                    .Select(block => $"{block.Text}: {block.Bounds}")));
            AssertActionsFit(dialog, window);
            var prepared = snapshot.Inventory.Sessions.Select(session =>
                new PreparedCapture(new(session.Path, session.Identity!, null), new string('W', 120), false)).ToArray();
            var confirmation = new CaptureDeleteConfirmation(new(Guid.NewGuid(), Path.GetTempPath(), prepared, []), mobile: true);
            ((Border)window.Content!).Child = confirmation; window.UpdateLayout();
            Assert.Equal(TextScale.Of(14), Find(confirmation, "Cancel").FontSize);
            AssertActionsFit(confirmation, window);
            Assert.Contains(Texts(confirmation), text => text.Contains("size unavailable", StringComparison.Ordinal));
            Assert.DoesNotContain(Texts(confirmation), text => text.Contains("about 0", StringComparison.Ordinal));
            var details = new CaptureResultDetails(prepared.Select(capture => capture.Label + "\nCould not be deleted.").ToArray(), true);
            ((Border)window.Content!).Child = details; window.UpdateLayout(); AssertActionsFit(details, window);
        }
        finally { window.Close(); RecentSessionsDialog.MobileOverride = null; TextScale.Platform = platform; TextScale.User = user; }

        static void AssertActionsFit(Control body, Window host)
        {
            foreach (var button in body.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Content is string))
            {
                Assert.True(button.Bounds.Width >= 48 && button.Bounds.Height >= 48,
                    $"{button.Content}: {button.Bounds}");
                if (button.FindAncestorOfType<ScrollViewer>() is not null) continue;
                var origin = button.TranslatePoint(default, host)!.Value;
                Assert.True(origin.X >= 0 && origin.Y >= 0 &&
                    origin.X + button.Bounds.Width <= host.Bounds.Width && origin.Y + button.Bounds.Height <= host.Bounds.Height,
                    $"{button.Content}: {origin}, {button.Bounds} in {host.Bounds}");
            }
        }
    }

    /// <summary>T-U24. A stale scan cannot reinsert a removed identity or clear live checks.</summary>
    [AvaloniaFact]
    public async Task OutOfOrderScansCannotResurrectRemovedCaptures()
    {
        var sessions = new[] { Session("a"), Session("b") };
        var current = Snapshot(sessions, generation: 5);
        var stale = Snapshot(sessions, generation: 1);
        var refreshed = false;
        var actions = Actions(
            current,
            results: captures => captures
                .Select(capture => new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.Deleted)))
                .ToArray(),
            refresh: () =>
            {
                // The scan that answers after the deletion is the one that started before it.
                var answer = refreshed ? stale : current;
                refreshed = true;
                return answer;
            });
        var dialog = new RecentSessionsDialog(current, actions);
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(RowChecks(dialog).First());
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            var child = Show((CaptureDeleteConfirmation)host.Body!);
            Click(Find((Control)host.Body!, "Delete permanently"));
            child.Close();
            await Settle();
            window.UpdateLayout();

            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Single((IEnumerable<object>)list.ItemsSource!);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>T-U20. A failed refresh offers Refresh and never fakes an empty list.</summary>
    [AvaloniaFact]
    public async Task RefreshFailureKeepsTheListAndOffersRefresh()
    {
        var snapshot = Snapshot(2);
        var fail = false;
        var actions = new RecentCaptureActions(
            (selected, _) => Task.FromResult(new PreparedCaptures(Guid.NewGuid(), Path.GetTempPath(), [], [])),
            (_, _, _) => Task.FromResult<IReadOnlyList<CaptureDeletionResult>>([]),
            _ => fail ? Task.FromException<RecentCaptureSnapshot>(new IOException("injected")) : Task.FromResult(snapshot),
            _ => Task.CompletedTask);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var window = Show(dialog);
        try
        {
            fail = true;
            dialog.RequestRefresh();
            await Settle();
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Equal(2, ((IEnumerable<object>)list.ItemsSource!).Count());
            Assert.Contains("The list could not be refreshed. Try Refresh.", Texts(dialog));
            Assert.True(Find(dialog, "Refresh").IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Section 11.1. An unreadable root is not "no captures yet".</summary>
    [AvaloniaFact]
    public async Task UnreadableRootDisablesDeletionWithoutClaimingAnEmptyList()
    {
        var snapshot = Snapshot(2);
        var broken = Snapshot([], generation: 2, available: false);
        var actions = Actions(snapshot, refresh: () => broken);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var window = Show(dialog);
        try
        {
            dialog.RequestRefresh();
            await Settle();
            Assert.Contains("Temporary storage is unavailable. Try Refresh.", Texts(dialog));
            Assert.False(Find(dialog, "Delete captures…").IsEnabled);
            Assert.DoesNotContain("No captures on this device yet.", Texts(dialog));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U11. Cleanup status and its retry stay reachable with an empty list.</summary>
    [AvaloniaFact]
    public void PendingCleanupStaysReachableWhenTheListIsEmpty()
    {
        var snapshot = Snapshot([], pendingCleanup: 1);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            Assert.True(Has(dialog, "Retry storage cleanup"));
            Assert.Contains("Storage cleanup is pending. Some space is still in use.", Texts(dialog));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Section 8.11. A capture the scan could not read is not a deleted capture.</summary>
    [AvaloniaFact]
    public async Task AnUnlistableCaptureKeepsItsRowAndSaysSo()
    {
        var sessions = new[] { Session("a"), Session("b") };
        var partial = Snapshot([sessions[0]], generation: 2, issues: 1);
        var actions = Actions(Snapshot(sessions), refresh: () => partial);
        var dialog = new RecentSessionsDialog(Snapshot(sessions), actions);
        var window = Show(dialog);
        try
        {
            dialog.RequestRefresh();
            await Settle();
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.Equal(2, ((IEnumerable<object>)list.ItemsSource!).Count());
            Assert.Contains("Some captures could not be listed. Try Refresh.", Texts(dialog));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1.0, 480, 520)]
    [InlineData(1.8, 930, 420)]
    public async Task NewlyVisibleCleanupActionsWrapBeforeCloseCanLeaveTheCard(double scale, double width, double height)
    {
        var platform = TextScale.Platform;
        var user = TextScale.User;
        RecentSessionsDialog.MobileOverride = true;
        TextScale.Platform = scale; TextScale.User = 1;
        var snapshot = Snapshot(2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot,
            refresh: () => Snapshot(snapshot.Inventory.Sessions, generation: 2, unresolvedCleanup: 1)));
        var window = new Window { Content = dialog, Width = width, Height = height };
        window.Show(); window.UpdateLayout();
        try
        {
            Click(Find(dialog, "Select"));
            Toggle(SelectAll(dialog));
            await Settle();
            dialog.RequestRefresh();
            await Settle();
            window.UpdateLayout();
            Assert.True(Has(dialog, "Retry storage cleanup"));
            foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
            {
                var origin = button.TranslatePoint(default, dialog)!.Value;
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= dialog.Bounds.Width,
                    $"{button.Content} must fit horizontally after cleanup appears");
                Assert.True(origin.Y >= 0 && origin.Y + button.Bounds.Height <= dialog.Bounds.Height,
                    $"{button.Content} must fit vertically after cleanup appears");
            }
        }
        finally
        {
            window.Close(); RecentSessionsDialog.MobileOverride = null;
            TextScale.Platform = platform; TextScale.User = user;
        }
    }

    [AvaloniaFact]
    public async Task APartialInventoryReplacesAnOldIdentityWithoutKeepingAGhostRow()
    {
        var original = Session("original");
        var snapshot = Snapshot([original]);
        var replacement = original with { Identity = "replacement" };
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot,
            refresh: () => Snapshot([replacement], generation: 2, issues: 1)));
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            dialog.RequestRefresh();
            await Settle();
            var check = Assert.Single(RowChecks(dialog));
            Assert.False(check.IsChecked);
            Assert.Contains(Texts(dialog), text => text.Contains("no longer available to delete", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RefreshExplainsASelectionThatBecameBusy()
    {
        var snapshot = Snapshot(2);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot,
            refresh: () => Snapshot(snapshot.Inventory.Sessions, generation: 2, busy: [snapshot.Inventory.Sessions[0]])));
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            dialog.RequestRefresh();
            await Settle();
            Assert.Single(RowChecks(dialog), check => check.IsChecked == true);
            Assert.Contains("1 capture you had selected is no longer available to delete.", Texts(dialog));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UnavailableStorageRetainsKnownCleanupAndItsRetry()
    {
        var snapshot = Snapshot([], pendingCleanup: 1);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot,
            refresh: () => Snapshot([], generation: 2, available: false)));
        var window = Show(dialog);
        try
        {
            dialog.RequestRefresh();
            await Settle();
            Assert.True(Has(dialog, "Retry storage cleanup"));
            Assert.Contains("Temporary storage is unavailable. Try Refresh.", Texts(dialog));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancelledRefreshCannotPublishEvenIfTheReaderIgnoresCancellation()
    {
        var snapshot = Snapshot(2);
        var finished = new TaskCompletionSource<RecentCaptureSnapshot>();
        CancellationToken observed = default;
        var actions = Actions(snapshot) with { Refresh = token => { observed = token; return finished.Task; } };
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            dialog.RequestRefresh();
            await Settle();
            dialog.Dismiss();
            Assert.True(observed.IsCancellationRequested);
            finished.SetResult(Snapshot([], generation: 2));
            await Settle();
            Assert.Equal(2, RowChecks(dialog).Count(check => check.IsChecked == true));
            Assert.True(Find(dialog, "Refresh").IsEnabled);
            Assert.False(dialog.Completion.IsCompleted);
        }
        finally { dialog.ForceDismiss(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task PostDeleteRefreshCanBeStoppedWithoutLosingTheDeletionResult()
    {
        var snapshot = Snapshot(1);
        var finished = new TaskCompletionSource<RecentCaptureSnapshot>();
        CancellationToken observed = default;
        var actions = Actions(snapshot) with { Refresh = token => { observed = token; return finished.Task; } };
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost(); dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            Click(Find(confirmation, "Delete permanently"));
            child.Close();
            await Settle();
            dialog.Dismiss();
            Assert.True(observed.IsCancellationRequested);
            finished.SetResult(snapshot);
            await Settle();
            Assert.True(Assert.Single(dialog.DeletionResults).File.Committed);
            Assert.Empty(RowChecks(dialog));
            Assert.Contains("The list refresh was stopped. Try Refresh.", Texts(dialog));
            Assert.True(Find(dialog, "Refresh").IsEnabled);
        }
        finally { dialog.ForceDismiss(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task CleanupResultsReconcileIndividuallyWhenOnlyOneCaptureStillNeedsCleanup()
    {
        var snapshot = Snapshot(2);
        var next = Snapshot([], generation: 2, pendingCleanup: 2);
        var actions = Actions(snapshot,
            results: captures => captures.Select(capture => new CaptureDeletionResult(capture,
                new(capture.Target, CaptureDeleteOutcome.DeletedPendingReclaim))).ToArray(), refresh: () => next);
        var dialog = new RecentSessionsDialog(snapshot, actions);
        var host = new CapturingHost(); dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(SelectAll(dialog));
            Click(Find(dialog, "Delete 2 captures…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            Click(Find(confirmation, "Delete permanently"));
            child.Close();
            await Settle();
            next = Snapshot([], generation: 3, pendingCleanup: 1);
            next = next with
            {
                Inventory = next.Inventory with
                {
                    PendingIdentities = new HashSet<string>(StringComparer.Ordinal) { snapshot.Inventory.Sessions[1].Identity! },
                }
            };
            dialog.RequestRefresh();
            await Settle();
            Assert.Equal(CaptureDeleteOutcome.Deleted, dialog.DeletionResults[0].File.Outcome);
            Assert.Equal(CaptureDeleteOutcome.DeletedPendingReclaim, dialog.DeletionResults[1].File.Outcome);
            Assert.Contains(Texts(dialog), text => text.Contains("Storage cleanup is pending for 1 deleted capture.", StringComparison.Ordinal));
        }
        finally { dialog.ForceDismiss(); window.Close(); }
    }

    // ---- labels, speech and privacy -----------------------------------------------------

    /// <summary>T-U22, section 5.7. Names collide, so the date and size disambiguate first.</summary>
    [AvaloniaFact]
    public void DuplicateNamesAreDisambiguatedWithoutPathsOrOrdinalNoise()
    {
        var moment = new DateTimeOffset(2026, 9, 5, 9, 12, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            Session("twin", 4096, "one", moment),
            Session("twin", 8192, "two", moment),
            Session("twin", 4096, "three", moment),
            Session("unique", 4096, "four", moment),
        };
        var snapshot = Snapshot(sessions);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var labels = RowChecks(dialog).Select(box => AutomationProperties.GetName(box) ?? string.Empty).ToArray();
            Assert.Equal(4, labels.Length);

            // Size already separates the second one, so only the true collision is numbered.
            Assert.Equal(2, labels.Count(label => label.Contains("· Capture ", StringComparison.Ordinal)));
            Assert.DoesNotContain(labels, label => label.Contains("unique", StringComparison.Ordinal) &&
                label.Contains("· Capture ", StringComparison.Ordinal));
            Assert.Equal(4, labels.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U14. No private path, generated GUID or record dump reaches the interface.</summary>
    [AvaloniaFact]
    public void NothingUserVisibleCarriesAPathOrGeneratedIdentifier()
    {
        var snapshot = Snapshot(3);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = Show(dialog);
        try
        {
            var visible = Texts(dialog)
                .Concat(dialog.GetVisualDescendants().OfType<Control>()
                    .Select(control => AutomationProperties.GetName(control) ?? string.Empty))
                .Concat(dialog.GetVisualDescendants().OfType<Control>()
                    .Select(control => AutomationProperties.GetHelpText(control) ?? string.Empty))
                .ToArray();
            foreach (var session in snapshot.Inventory.Sessions)
            {
                Assert.DoesNotContain(visible, text => text.Contains(session.Path, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(visible, text => text.Contains(Path.GetFileName(session.Path), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(visible, text => text.Contains("Identity", StringComparison.Ordinal));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>T-U14. A machine-only name never becomes speech.</summary>
    [Theory]
    [InlineData("123e4567-e89b-12d3-a456-426614174000.vcat")]
    [InlineData("123e4567e89b12d3a456426614174000.vcat")]
    [InlineData("20260905-100000-123e4567e89b12d3a456426614174000.vcat")]
    [InlineData("   .vcat")]
    public void MachineOnlyNamesNeverReachSpeech(string name) =>
        Assert.Equal("Unnamed capture", SessionCacheName.Describe(Path.Combine(Path.GetTempPath(), name)));

    // ---- shell -------------------------------------------------------------------------

    private sealed class ShellHarness : IDisposable
    {
        internal ShellHarness()
        {
            Root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal List<string> Closed { get; } = [];

        internal Dictionary<string, CaptureProtection> Protection { get; } = new(SessionPath.Comparer);

        internal HashSet<string> CloseFails { get; } = new(SessionPath.Comparer);

        /// <summary>What a failing close throws. Not every tab teardown fails as I/O.</summary>
        internal Func<Exception> CloseFailure { get; set; } = () => new IOException("injected close failure");

        internal Dictionary<string, int> Tabs { get; } = new(SessionPath.Comparer);

        internal CancellationTokenSource? StopOnClose { get; set; }

        internal async Task<CaptureSelection> CaptureAsync(string name, int tabs = 0)
        {
            var path = Path.Combine(Root, name + ".vcat");
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(
                Path.Combine(path, "manifest.json"),
                "{\"updatedUtc\":\"2026-09-05T10:00:00Z\",\"finalized\":true,\"sessionSizeBytes\":4096}",
                TestContext.Current.CancellationToken);
            Tabs[path] = tabs;
            return new CaptureSelection(new TemporarySessionInfo(path, DateTimeOffset.UtcNow, 4096, true), name);
        }

        internal CaptureDeletionCoordinator Coordinator(Action<CaptureDeletionResult>? resolved = null) => new(
            Root,
            path => Protection.GetValueOrDefault(path, CaptureProtection.None),
            path => Enumerable.Range(0, Tabs.GetValueOrDefault(path)).Select(_ => (Func<Task>)(() =>
            {
                Closed.Add(path);
                StopOnClose?.Cancel();
                return CloseFails.Contains(path)
                    ? Task.FromException(CloseFailure())
                    : Task.CompletedTask;
            })).ToArray(),
            resolved ?? (_ => { }));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }

    [Fact]
    public async Task ActiveReadWorkProtectsTabsUntilItFinishes()
    {
        using var harness = new ShellHarness();
        var capture = await harness.CaptureAsync("exporting", tabs: 1);
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([capture], TestContext.Current.CancellationToken);
        using (SessionAccess.ReadForWork(capture.Session.Path))
        {
            Assert.True(SessionAccess.IsWorking(capture.Session.Path));
            var refused = Assert.Single(await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken));
            Assert.Equal(CaptureDeleteOutcome.Protected, refused.File.Outcome);
            Assert.Empty(harness.Closed);
            Assert.True(Directory.Exists(capture.Session.Path));
        }
        Assert.False(SessionAccess.IsWorking(capture.Session.Path));
        Assert.True(Assert.Single(await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken)).File.Committed);
        Assert.Single(harness.Closed);
    }

    [Fact]
    public async Task ReplacementBetweenInspectionAndReservationNeverClosesTabs()
    {
        using var harness = new ShellHarness();
        var capture = await harness.CaptureAsync("replacement", tabs: 1);
        var prepared = await harness.Coordinator().PrepareAsync([capture], TestContext.Current.CancellationToken);
        var replaced = false;
        var coordinator = new CaptureDeletionCoordinator(harness.Root, path =>
        {
            if (!replaced)
            {
                replaced = true;
                Directory.Delete(path, true);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "manifest.json"), "replacement payload");
            }
            return CaptureProtection.None;
        }, path => [() => { harness.Closed.Add(path); return Task.CompletedTask; }], _ => { });
        var result = Assert.Single(await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken));
        Assert.Equal(CaptureDeleteOutcome.Changed, result.File.Outcome);
        Assert.Empty(harness.Closed);
        Assert.Equal("replacement payload", await File.ReadAllTextAsync(Path.Combine(capture.Session.Path, "manifest.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>T-S9, T-S1. Stop leaves later captures and their tabs untouched.</summary>
    [Fact]
    public async Task StopDuringCurrentCloseNeverTouchesLaterTabs()
    {
        using var harness = new ShellHarness();
        using var stop = new CancellationTokenSource();
        harness.StopOnClose = stop;
        var first = await harness.CaptureAsync("first", tabs: 1);
        var second = await harness.CaptureAsync("second", tabs: 1);
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([first, second], TestContext.Current.CancellationToken);
        var results = await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), stop.Token);

        Assert.Equal([first.Session.Path], harness.Closed);
        Assert.Equal(CaptureDeleteOutcome.Cancelled, results[0].File.Outcome);
        Assert.Equal(CaptureDeleteOutcome.NotAttempted, results[1].File.Outcome);
        Assert.True(results[0].TabsClosed);
        Assert.False(results[1].TabsClosed);
        Assert.True(Directory.Exists(first.Session.Path));
        Assert.True(Directory.Exists(second.Session.Path));
    }

    /// <summary>T-S8. Every distinct target is answered exactly once, in request order.</summary>
    [Fact]
    public async Task EveryDistinctTargetIsAnsweredOnceInOrder()
    {
        using var harness = new ShellHarness();
        var a = await harness.CaptureAsync("a");
        var b = await harness.CaptureAsync("b");
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([a, b, a], TestContext.Current.CancellationToken);
        Assert.Equal(2, prepared.Captures.Count);
        var results = await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken);
        Assert.Equal(
            [SessionPath.Canonical(a.Session.Path), SessionPath.Canonical(b.Session.Path)],
            results.Select(result => result.Capture.Target.Path));
        Assert.All(results, result => Assert.True(result.File.Committed));
    }

    /// <summary>T-S7. A close failure is one InUse result, and the others still run.</summary>
    [Fact]
    public async Task CloseFailureGivesOneResultAndOtherCapturesContinue()
    {
        using var harness = new ShellHarness();
        var stubborn = await harness.CaptureAsync("stubborn", tabs: 2);
        var fine = await harness.CaptureAsync("fine", tabs: 1);
        harness.CloseFails.Add(stubborn.Session.Path);
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([stubborn, fine], TestContext.Current.CancellationToken);
        var results = await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken);

        Assert.Equal(CaptureProtection.CloseFailed, results[0].Protection);
        Assert.True(results[0].Failed);
        Assert.True(results[0].TabsClosed);
        Assert.Contains("could not finish closing", results[0].Reason, StringComparison.Ordinal);
        Assert.True(Directory.Exists(stubborn.Session.Path));

        // One result for the capture, even though it had two tabs, and one failed close does
        // not stop the next capture.
        Assert.Single(harness.Closed, path => SessionPath.Comparer.Equals(path, stubborn.Session.Path));
        Assert.True(results[1].File.Committed);
        Assert.False(Directory.Exists(fine.Session.Path));
    }

    /// <summary>
    /// Section 6.1. A capture the shell settled itself is a known failure, not an unknown one.
    /// </summary>
    /// <remarks>
    /// The classification of whatever a tab teardown threw decides nothing here: closing runs
    /// before the rename, so a capture whose close failed is one this workspace knows was not
    /// removed. Reported as unverified it would send the reader to refresh and check storage
    /// for an answer the sheet is already holding.
    /// </remarks>
    [Fact]
    public async Task ACloseThatFailsUnexpectedlyIsStillAKnownFailure()
    {
        using var harness = new ShellHarness();
        var capture = await harness.CaptureAsync("stubborn", tabs: 1);
        harness.CloseFails.Add(capture.Session.Path);
        harness.CloseFailure = () => new InvalidOperationException("the tab was already gone");
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([capture], TestContext.Current.CancellationToken);
        var results = await coordinator.DeleteAsync(prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal(CaptureProtection.CloseFailed, result.Protection);
        Assert.Equal(CaptureDeleteOutcome.Unknown, result.File.Outcome);
        var tally = CaptureDeletionResult.Tally.Of(results);
        Assert.Equal(1, tally.Failed);
        Assert.Equal(0, tally.Unknown);
        Assert.Contains("could not be deleted", CaptureDeletionResult.Summary(results), StringComparison.Ordinal);
        Assert.DoesNotContain("could not be verified", CaptureDeletionResult.Summary(results), StringComparison.Ordinal);

        // The detail says the close failed; it must not also say the tab was closed.
        Assert.Contains("could not finish closing", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("The tab was closed", result.Detail, StringComparison.Ordinal);
        Assert.Equal(CaptureNoticeKind.Failure, CaptureDeletionResult.FinalNotice(results).Kind);
        Assert.True(Directory.Exists(capture.Session.Path));
    }

    /// <summary>T-S2. A recording capture is neither closed nor deleted by any route.</summary>
    [Fact]
    public async Task LiveCapturesAreNeverClosedOrDeleted()
    {
        using var harness = new ShellHarness();
        var live = await harness.CaptureAsync("live", tabs: 1);
        var idle = await harness.CaptureAsync("idle");
        var coordinator = harness.Coordinator();

        // Frozen while it was idle, then the capture starts recording: the request is stale and
        // execution has to refuse it rather than trust the observation preparation made.
        var stale = await coordinator.PrepareAsync([live, idle], TestContext.Current.CancellationToken);
        Assert.Equal(2, stale.Captures.Count);
        harness.Protection[live.Session.Path] = CaptureProtection.Recording;

        var results = await coordinator.DeleteAsync(stale, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken);
        Assert.Equal(CaptureDeleteOutcome.Protected, results[0].File.Outcome);
        Assert.Equal(CaptureProtection.Recording, results[0].Protection);
        Assert.Contains("being recorded", results[0].Reason, StringComparison.Ordinal);
        Assert.Empty(harness.Closed);
        Assert.True(Directory.Exists(live.Session.Path));

        // A fresh preparation drops it with the same reason instead of failing the whole set.
        // The other capture is gone by now, so it is dropped too, with a reason of its own.
        var prepared = await coordinator.PrepareAsync([live, idle], TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Captures);
        Assert.Equal(2, prepared.Excluded.Count);
        var exclusion = prepared.Excluded[0];
        Assert.Equal(CaptureProtection.Recording, exclusion.Protection);
        Assert.Contains("being recorded", exclusion.Reason, StringComparison.Ordinal);
    }

    /// <summary>T-I12. Progress totals are stable and the resolved count only grows.</summary>
    [Fact]
    public async Task ProgressReportsAStableTotalAndMonotonicCompletion()
    {
        using var harness = new ShellHarness();
        var a = await harness.CaptureAsync("a", tabs: 1);
        var b = await harness.CaptureAsync("b");
        harness.Protection[b.Session.Path] = CaptureProtection.None;
        var coordinator = harness.Coordinator();
        var prepared = await coordinator.PrepareAsync([a, b], TestContext.Current.CancellationToken);

        // A synchronous sink, deliberately: Progress<T> posts to the captured context, and a
        // shell test with no dispatcher would be asserting on whatever had happened to arrive.
        var recorder = new RecordingProgress();
        await coordinator.DeleteAsync(prepared, recorder, TestContext.Current.CancellationToken);
        var seen = recorder.Reports;
        Assert.NotEmpty(seen);
        Assert.All(seen, value => Assert.Equal(2, value.Total));
        Assert.All(seen, value => Assert.Equal(prepared.Operation, value.Operation));
        Assert.Equal(seen.Select(value => value.Completed).Order(), seen.Select(value => value.Completed));
        Assert.Contains(seen, value => value.Phase == CaptureDeletionPhase.Closing);
        Assert.Contains(seen, value => value.Phase == CaptureDeletionPhase.Deleting);
    }

    [Fact]
    public void StoppedAndUnverifiedResultsNeverClaimMoreThanIsKnown()
    {
        var capture = Prepared(new CaptureSelection(Session("result"), "result"));
        var cancelled = new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.Cancelled), TabsClosed: true);
        var notAttempted = cancelled with { File = cancelled.File with { Outcome = CaptureDeleteOutcome.NotAttempted }, TabsClosed = false };
        var unknown = cancelled with { File = cancelled.File with { Outcome = CaptureDeleteOutcome.Unknown }, TabsClosed = false };
        var tally = CaptureDeletionResult.Tally.Of([cancelled, notAttempted, unknown]);
        Assert.Equal(1, tally.Cancelled);
        Assert.Equal(1, tally.NotAttempted);
        Assert.Equal(1, tally.Unknown);
        Assert.Equal(0, tally.Failed);
        var summary = CaptureDeletionResult.Summary([cancelled, notAttempted, unknown]);
        Assert.Contains("Stopped before deleting 1 capture.", summary, StringComparison.Ordinal);
        Assert.Contains("1 capture was not attempted.", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("No captures were deleted", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be deleted", summary, StringComparison.Ordinal);
        var unverifiedClose = unknown with { TabsClosed = true };
        Assert.Contains("The tab was closed; the deletion outcome is unverified.", unverifiedClose.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("capture was not deleted", unverifiedClose.Detail, StringComparison.Ordinal);
        var notice = CaptureDeletionResult.FinalNotice([cancelled, notAttempted]);
        Assert.Equal(CaptureNoticeKind.Information, notice.Kind);
        Assert.Contains("Tabs were closed", notice.Text, StringComparison.Ordinal);
    }

    /// <summary>T-S5. The final notice follows the ledger, and says nothing about a clean stop.</summary>
    [Fact]
    public void FinalNoticeFollowsTheLedger()
    {
        Assert.Equal(CaptureNoticeKind.None, Notice(CaptureDeleteOutcome.Cancelled, CaptureDeleteOutcome.NotAttempted).Kind);
        Assert.Equal(CaptureNoticeKind.None, CaptureDeletionResult.FinalNotice([]).Kind);

        var deleted = Notice(CaptureDeleteOutcome.Deleted, CaptureDeleteOutcome.Deleted);
        Assert.Equal(CaptureNoticeKind.Completion, deleted.Kind);
        Assert.Equal("Deleted 2 captures from temporary storage.", deleted.Text);

        var pending = Notice(CaptureDeleteOutcome.Deleted, CaptureDeleteOutcome.DeletedPendingReclaim);
        Assert.Equal(CaptureNoticeKind.Completion, pending.Kind);
        Assert.Contains("Storage cleanup is still pending.", pending.Text, StringComparison.Ordinal);

        var partial = Notice(CaptureDeleteOutcome.Deleted, CaptureDeleteOutcome.Denied);
        Assert.Equal(CaptureNoticeKind.Failure, partial.Kind);
        Assert.Equal("Deleted 1 capture. 1 capture could not be deleted.", partial.Text);

        var allFailed = Notice(CaptureDeleteOutcome.Denied);
        Assert.Equal(CaptureNoticeKind.Failure, allFailed.Kind);
        Assert.Contains("does not have permission", allFailed.Text, StringComparison.Ordinal);

        var missing = Notice(CaptureDeleteOutcome.AlreadyMissing);
        Assert.Equal(CaptureNoticeKind.Information, missing.Kind);

        var unknown = Notice(CaptureDeleteOutcome.Deleted, CaptureDeleteOutcome.Unknown);
        Assert.Equal(CaptureNoticeKind.Failure, unknown.Kind);
        Assert.Contains("could not be verified", unknown.Text, StringComparison.Ordinal);

        static (CaptureNoticeKind Kind, string Text) Notice(params CaptureDeleteOutcome[] outcomes) =>
            CaptureDeletionResult.FinalNotice(outcomes.Select((outcome, i) =>
            {
                var target = new CaptureDeleteTarget(Path.Combine(Path.GetTempPath(), $"c{i}.vcat"), "id" + i, 1024);
                return new CaptureDeletionResult(new PreparedCapture(target, "capture " + i, false), new(target, outcome));
            }).ToArray());
    }

    /// <summary>
    /// T-S5, T-U23. A retried failure counts once and stops being an outstanding failure.
    /// </summary>
    [Fact]
    public void ARetriedFailureIsNotAlsoAnOutstandingOne()
    {
        var target = new CaptureDeleteTarget(Path.Combine(Path.GetTempPath(), "c.vcat"), "id", 1024);
        var capture = new PreparedCapture(target, "capture", false);
        var ledger = new Dictionary<string, CaptureDeletionResult>(StringComparer.Ordinal);
        var key = target.Path + "|" + target.Identity;
        ledger[key] = new CaptureDeletionResult(capture, new(target, CaptureDeleteOutcome.IoFailure));
        ledger[key] = new CaptureDeletionResult(capture, new(target, CaptureDeleteOutcome.Deleted));
        var (kind, text) = CaptureDeletionResult.FinalNotice(ledger.Values);
        Assert.Equal(CaptureNoticeKind.Completion, kind);
        Assert.Equal("Deleted 1 capture from temporary storage.", text);
    }

    /// <summary>
    /// T-I17. Corrupt or absent metadata cannot turn a committed removal into an exception
    /// raised while summarising it, and no total is ever offered as space that became free.
    /// </summary>
    [Fact]
    public void ExtremeAndUnknownSizesNeitherOverflowNorPromiseFreedSpace()
    {
        // Sizes come out of manifests this product did not necessarily write. Two of these
        // sum past long.MaxValue, and a negative one is not a capture of negative size.
        Assert.Equal(long.MaxValue, RecentCapturePanel.SumSizes([long.MaxValue, long.MaxValue, 1]));
        Assert.Equal(4096, RecentCapturePanel.SumSizes([-1, 4096, long.MinValue]));

        var huge = Prepared(new CaptureSelection(Session("huge", long.MaxValue), "huge"));
        var also = Prepared(new CaptureSelection(Session("also", long.MaxValue), "also"));
        var unknown = new PreparedCapture(
            new CaptureDeleteTarget(Path.Combine(Path.GetTempPath(), "unknown.vcat"), "id-unknown", null),
            "unknown",
            false);
        var results = new[] { huge, also, unknown }
            .Select(capture => new CaptureDeletionResult(capture, new(capture.Target, CaptureDeleteOutcome.Deleted)))
            .ToArray();

        var summary = CaptureDeletionResult.Summary(results);
        Assert.Contains("Deleted 3 captures.", summary, StringComparison.Ordinal);

        // One estimate is missing, so the total is a floor rather than a measurement, and the
        // sentence says which of the two it is.
        Assert.Contains("At least about", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("freed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("free space", summary, StringComparison.OrdinalIgnoreCase);

        // Every known estimate makes it "about", which is still an estimate of what the
        // captures held and never a claim about the volume.
        var known = CaptureDeletionResult.Summary(results.Take(2));
        Assert.Contains("About", known, StringComparison.Ordinal);
        Assert.DoesNotContain("At least", known, StringComparison.Ordinal);
        Assert.DoesNotContain("freed", known, StringComparison.OrdinalIgnoreCase);

        var (kind, notice) = CaptureDeletionResult.FinalNotice(results);
        Assert.Equal(CaptureNoticeKind.Completion, kind);
        Assert.Equal("Deleted 3 captures from temporary storage.", notice);
    }

    /// <summary>
    /// T-U5, section 9.3. A confirmation whose sizes are unknown says so instead of inventing
    /// a total.
    /// </summary>
    [AvaloniaFact]
    public void ConfirmationWithNoKnownSizeSaysSoRatherThanInventingATotal()
    {
        var unknown = new PreparedCapture(
            new CaptureDeleteTarget(Path.Combine(Path.GetTempPath(), "unmeasured.vcat"), "id", null),
            "unmeasured · 5 Sep 2026, 10:00",
            false);
        var confirmation = new CaptureDeleteConfirmation(
            new PreparedCaptures(Guid.NewGuid(), Path.GetTempPath(), [unknown], []),
            mobile: false);
        var window = Show(confirmation);
        try
        {
            var copy = string.Join("|", Texts(confirmation));
            Assert.Contains("size unavailable", copy, StringComparison.Ordinal);
            Assert.Contains("Some sizes are unavailable.", copy, StringComparison.Ordinal);
            Assert.DoesNotContain("about 0", copy, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0 B", copy, StringComparison.Ordinal);
        }
        finally
        {
            confirmation.ForceDismiss();
            window.Close();
        }
    }

    // ---- host --------------------------------------------------------------------------

    /// <summary>
    /// T-S6. A failure leaves one safe classification behind, and nothing in it identifies a
    /// capture. The logger redacts the message; the classification is what travels.
    /// </summary>
    [Fact]
    public async Task AFailureIsClassifiedOnceForDiagnosticsWithoutNamingTheCapture()
    {
        using var harness = new ShellHarness();
        await using var workspace = new WorkspaceViewModel();
        var sink = new CollectingDiagnosticSink();
        workspace.ConfigureDiagnostics(sink);
        try
        {
            var stubborn = await harness.CaptureAsync("stubborn", tabs: 1);
            harness.CloseFails.Add(stubborn.Session.Path);
            var coordinator = harness.Coordinator();
            var prepared = await coordinator.PrepareAsync([stubborn], TestContext.Current.CancellationToken);
            var results = await coordinator.DeleteAsync(
                prepared, new Progress<CaptureDeletionProgress>(), TestContext.Current.CancellationToken);
            Assert.True(results[0].Failed);

            // RecordFailure writes off the caller's thread, so the assertion waits for the
            // write rather than assuming it has landed.
            var events = await sink.SettleAsync();
            var deletion = Assert.Single(events, item => item.Name.StartsWith("capture.delete.", StringComparison.Ordinal));

            // Typed context and outcome, and the exception's type. Never its English words.
            Assert.Equal("capture.delete." + results[0].File.Outcome, deletion.Name);
            Assert.Contains("exceptionType", deletion.Properties.Keys);

            // Nothing here identifies which capture it was: not its name, not its path, not
            // the identity token the operation froze.
            var payload = string.Join("|", deletion.Properties.Select(pair => pair.Key + "=" + pair.Value)) + "|" + deletion.Name;
            Assert.DoesNotContain("stubborn", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(harness.Root, payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".vcat", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            workspace.ConfigureDiagnostics(null);
        }
    }

    private sealed class CollectingDiagnosticSink : IDiagnosticSink
    {
        private readonly List<DiagnosticEvent> _events = [];

        public ValueTask WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
        {
            lock (_events)
            {
                _events.Add(diagnosticEvent);
            }

            return ValueTask.CompletedTask;
        }

        internal async Task<IReadOnlyList<DiagnosticEvent>> SettleAsync()
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                lock (_events)
                {
                    if (_events.Count > 0)
                    {
                        return [.. _events];
                    }
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            lock (_events)
            {
                return [.. _events];
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// T-S12. Visuals detaching during a relayout is not a teardown: the checks, the results
    /// and the ledger are all still there when the panel comes back.
    /// </summary>
    [AvaloniaFact]
    public async Task RelayoutDetachAndReattachPreserveSelectionAndResults()
    {
        var snapshot = Snapshot(3);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot, results: captures =>
            [
                new CaptureDeletionResult(captures[0], new(captures[0].Target, CaptureDeleteOutcome.Deleted)),
            ]));
        var host = new CapturingHost();
        dialog.Host = host;
        var window = Show(dialog);
        try
        {
            Toggle(RowChecks(dialog).First());
            Click(Find(dialog, "Delete 1 capture…"));
            await Settle();
            var confirmation = Assert.IsType<CaptureDeleteConfirmation>(host.Body);
            var child = Show(confirmation);
            Click(Find(confirmation, "Delete permanently"));
            child.Close();
            await Settle();
            Assert.Contains(Texts(dialog), text => text.StartsWith("Deleted 1 capture.", StringComparison.Ordinal));

            // Reparenting is what a rotation, a theme change or a host swapping its content
            // does. It is not the owner going away.
            var reparent = new ContentControl();
            window.Content = reparent;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            reparent.Content = dialog;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Settle();

            Assert.Contains(Texts(dialog), text => text.StartsWith("Deleted 1 capture.", StringComparison.Ordinal));
            Assert.Single(dialog.DeletionResults);
            Assert.False(dialog.Completion.IsCompleted);

            // And the dialog is still working: the two survivors can still be selected.
            Toggle(SelectAll(dialog));
            Assert.True(Find(dialog, "Delete 2 captures…").IsEnabled);
        }
        finally
        {
            dialog.ForceDismiss();
            window.Close();
        }
    }

    /// <summary>Device regression: resizing the sheet's title must resize its actual body,
    /// without replacing a selection, confirmation or results that are already on screen.</summary>
    [AvaloniaTheory]
    [InlineData("selection")]
    [InlineData("confirmation")]
    [InlineData("details")]
    public async Task AnOpenDeletionDialogFollowsPlatformTextSizeThroughTheRealHost(string kind)
    {
        var platform = VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale;
        MainView.InPageDialogOverride = true;
        RecentSessionsDialog.MobileOverride = true;
        await using var host = new MainView();
        var window = Show(host);
        try
        {
            await Settle();
            VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale = 1;
            VisualCat.App.Platform.PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            await Settle();
            var snapshot = Snapshot(2);
            DialogBody<bool>? nested = kind switch
            {
                "confirmation" => new CaptureDeleteConfirmation(new PreparedCaptures(Guid.NewGuid(), Path.GetTempPath(),
                    snapshot.Inventory.Sessions.Select(session => Prepared(new CaptureSelection(session, "Frozen " + session.Identity))).ToArray(), []), true),
                "details" => new CaptureResultDetails(["Frozen result\nDeleted from temporary storage."], true),
                _ => null,
            };
            var recent = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var presented = host.ShowDialogAsync(recent);
            await Settle();
            Click(Find(recent, "Select"));
            Toggle(SelectAll(recent));
            var inner = nested is null ? null : host.ShowDialogAsync(nested);
            await Settle();
            var before = recent.FontSize;
            var contentsBefore = nested is null ? [] : Texts(nested).ToArray();

            VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale = 1.8;
            VisualCat.App.Platform.PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            await Settle();
            window.UpdateLayout();

            Assert.True(recent.FontSize > before * 1.7);
            Assert.Equal(recent.FontSize, Find(recent, "Delete 2…").FontSize);
            Assert.Equal(2, RowChecks(recent).Count(check => check.IsChecked == true));
            Assert.False(presented.IsCompleted);
            if (nested is not null)
            {
                Assert.Equal(recent.FontSize, nested.FontSize);
                Assert.Equal(contentsBefore, Texts(nested));
                Assert.False(inner!.IsCompleted);
                nested.ForceDismiss();
                await inner;
            }
            recent.ForceDismiss();
            await presented;
        }
        finally
        {
            VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale = platform;
            VisualCat.App.Platform.PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            await Settle();
            window.Close();
            MainView.InPageDialogOverride = null;
            RecentSessionsDialog.MobileOverride = null;
        }
    }

    [AvaloniaFact]
    public async Task EnlargedLandscapeSheetKeepsItsListAndEveryCleanupAction()
    {
        var platform = VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale;
        MainView.InPageDialogOverride = true; RecentSessionsDialog.MobileOverride = true;
        await using var host = new MainView();
        var window = new Window { Content = host, Width = 948, Height = 450 };
        window.Show();
        try
        {
            await Settle();
            VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale = 1.8;
            VisualCat.App.Platform.PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            await Settle();
            var snapshot = Snapshot([Session("beta"), Session("gamma")], unresolvedCleanup: 1);
            var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
            var presented = host.ShowDialogAsync(dialog);
            await Settle();
            Click(Find(dialog, "Select"));
            Toggle(SelectAll(dialog));
            await Settle();
            window.UpdateLayout();
            var list = Assert.Single(dialog.GetVisualDescendants().OfType<ListBox>());
            Assert.True(list.Bounds.Height >= ListFloor,
                $"the capture list needs at least {ListFloor} dp; got {list.Bounds.Height} in {dialog.Bounds}, font {dialog.FontSize}");
            foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
            {
                var origin = button.TranslatePoint(default, dialog)!.Value;
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= dialog.Bounds.Width + 1, $"{button.Content} left the card horizontally");
                Assert.True(origin.Y >= 0 && origin.Y + button.Bounds.Height <= dialog.Bounds.Height + 1, $"{button.Content} left the card vertically");
            }
            dialog.ForceDismiss();
            await presented;
        }
        finally
        {
            VisualCat.App.Platform.PlatformSourceRegistry.PlatformFontScale = platform;
            VisualCat.App.Platform.PlatformSourceRegistry.PublishDisplayConfigurationChanged();
            await Settle(); window.Close();
            MainView.InPageDialogOverride = null; RecentSessionsDialog.MobileOverride = null;
        }
    }

    /// <summary>
    /// T-S10. A real desktop window close is a dismissal request the body may refuse, and a
    /// forced teardown settles it exactly once.
    /// </summary>
    [AvaloniaFact]
    public async Task DesktopWindowClosingAsksTheBodyAndForcedTeardownSettlesIt()
    {
        MainView.InPageDialogOverride = false;
        var host = new MainView();
        var window = new Window { Content = host, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var body = new RefusingBody();
            var presented = host.ShowDialogAsync(body);
            Dispatcher.UIThread.RunJobs();
            var dialogWindow = Assert.Single(window.OwnedWindows);

            // The first close is a dismissal the body declines: the window stays and the task
            // is still pending, which is exactly the state that used to hang.
            dialogWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, body.Dismissals);
            Assert.False(presented.IsCompleted);

            body.Allow = true;
            dialogWindow.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await presented.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            window.Close();
            MainView.InPageDialogOverride = null;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>T-S10. An ordinary dialog still dismisses on the first window close.</summary>
    [AvaloniaFact]
    public async Task AnOrdinaryDialogStillDismissesOnTheFirstClose()
    {
        MainView.InPageDialogOverride = false;
        var host = new MainView();
        var window = new Window { Content = host, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var body = new RefusingBody { Allow = true };
            var presented = host.ShowDialogAsync(body);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.OwnedWindows).Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await presented.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            window.Close();
            MainView.InPageDialogOverride = null;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// T-S3, T-S4. The whole shell path over real storage: the home card, the real dialog, a
    /// real staged deletion, and the home screen and notice that have to agree with the disk
    /// afterwards. Nothing here is a stub, so an ordering mistake between the ledger, the home
    /// pump and the notice lane shows up as a wrong screen rather than a passing unit test.
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingThroughTheShellAgreesWithStorageTheHomeCardAndTheNotice()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainView.InPageDialogOverride = true;
        var settings = Path.Combine(root, "settings.json");
        await new VisualCat.Infrastructure.Configuration.SettingsStore(settings).SaveAsync(
            new VisualCat.Infrastructure.Configuration.ApplicationSettings(SessionDirectory: root),
            TestContext.Current.CancellationToken);
        foreach (var name in new[] { "alpha", "beta" })
        {
            var capture = Path.Combine(root, $"20260905-10000{name.Length}-{name}-{Guid.NewGuid():N}.vcat");
            Directory.CreateDirectory(capture);
            await File.WriteAllTextAsync(
                Path.Combine(capture, "manifest.json"),
                "{\"updatedUtc\":\"2026-09-05T10:00:00Z\",\"finalized\":true,\"sessionSizeBytes\":4096}",
                TestContext.Current.CancellationToken);
        }

        var main = new MainView([], settings);
        var window = new Window { Content = main, Width = 1100, Height = 820 };
        window.Show();
        try
        {
            await main.WaitForRecentSessionsRefreshAsync();
            await Pump(window);
            Assert.Contains(VisibleTexts(main), text => text.StartsWith("RECENT CAPTURES ON THIS DEVICE", StringComparison.Ordinal));

            // The command band's own entry point, which is the one a desktop reader uses.
            Click(main.GetVisualDescendants().OfType<Button>()
                .First(button => button.IsEffectivelyVisible &&
                    (Equals(button.Content, "Recent") || AutomationProperties.GetName(button) == "Recent")));
            await Pump(window);

            var panel = Assert.Single(main.GetVisualDescendants().OfType<RecentCapturePanel>());
            Toggle(SelectAll(panel));
            await Pump(window);
            Click(Find(panel, "Delete 2 captures…"));
            await Pump(window);

            var confirmation = Assert.Single(main.GetVisualDescendants().OfType<CaptureDeleteConfirmation>());
            Click(Find(confirmation, "Delete permanently"));
            await Pump(window);

            // The disk is the oracle: both captures gone, and nothing left staged behind them.
            Assert.Empty(Directory.EnumerateDirectories(root, "*.vcat"));
            Assert.Empty(Directory.EnumerateDirectories(root, "*.vcat-deleting"));
            Assert.Contains(VisibleTexts(panel), text => text.StartsWith("Deleted 2 captures.", StringComparison.Ordinal));

            Click(Find(panel, "Close"));
            await Pump(window);
            await main.WaitForRecentSessionsRefreshAsync();
            await Pump(window);

            // The section that listed them is gone, not left showing cards for deleted captures.
            Assert.DoesNotContain(VisibleTexts(main), text => text.StartsWith("RECENT CAPTURES ON THIS DEVICE", StringComparison.Ordinal));
            Assert.Contains("Deleted 2 captures from temporary storage.", VisibleTexts(main));
        }
        finally
        {
            await main.DisposeAsync();
            window.Close();
            MainView.InPageDialogOverride = null;
            Dispatcher.UIThread.RunJobs();
            for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(50, TestContext.Current.CancellationToken);
                }
            }
        }
    }

    private static IEnumerable<string> VisibleTexts(Control root) => root.GetVisualDescendants()
        .OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Text ?? string.Empty);

    private static async Task Pump(Window window)
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// T-S11. A nested dialog belongs to the dialog that raised it, not to the main window.
    /// Parented to the shell instead, a confirmation can be left behind its own parent.
    /// </summary>
    [AvaloniaFact]
    public async Task ANestedDialogIsOwnedByTheDialogThatRaisedIt()
    {
        MainView.InPageDialogOverride = false;
        var host = new MainView();
        var window = new Window { Content = host, Width = 900, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outer = new RefusingBody { Allow = true };
            var outerPresented = host.ShowDialogAsync(outer);
            Dispatcher.UIThread.RunJobs();
            var outerWindow = Assert.Single(window.OwnedWindows);

            var inner = new RefusingBody { Allow = true };
            var innerPresented = host.ShowDialogAsync(inner);
            Dispatcher.UIThread.RunJobs();

            var innerWindow = Assert.Single(outerWindow.OwnedWindows);
            Assert.Same(outerWindow, innerWindow.Owner);
            Assert.Single(window.OwnedWindows);

            inner.ForceDismiss();
            Dispatcher.UIThread.RunJobs();
            await innerPresented.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            outer.ForceDismiss();
            Dispatcher.UIThread.RunJobs();
            await outerPresented.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            window.Close();
            MainView.InPageDialogOverride = null;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// T-U2. A recycled container is correct for the item it now holds. Every row state is
    /// bound rather than assigned once, so scrolling a long list must not carry a check, an
    /// enabled state or a label from the row a container used to show.
    /// </summary>
    [AvaloniaFact]
    public void RowStateSurvivesRecyclingAndReplacement()
    {
        var sessions = Enumerable.Range(0, 40).Select(i => Session("capture" + i)).ToArray();
        var snapshot = Snapshot(sessions, busy: [sessions[1]]);
        var dialog = new RecentSessionsDialog(snapshot, Actions(snapshot));
        var window = new Window { Content = dialog, Width = 700, Height = 640 };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var list = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            var checkedLabel = "Select " + Stamp(dialog, sessions[0]);
            var busyLabel = "Select " + Stamp(dialog, sessions[1]);
            Toggle(RowChecks(dialog).First(box => AutomationProperties.GetName(box) == checkedLabel));

            // Away far enough to recycle every realised container, then back.
            scroller.Offset = new Avalonia.Vector(0, scroller.Extent.Height);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.All(
                RowChecks(dialog).Where(box => box.IsEffectivelyVisible),
                box => Assert.False(box.IsChecked, "a recycled container kept a check from another row"));

            scroller.Offset = default;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var restored = RowChecks(dialog).Single(box => AutomationProperties.GetName(box) == checkedLabel);
            Assert.True(restored.IsChecked, "the check did not come back with its own row");
            Assert.True(restored.IsEnabled);
            var busyBox = RowChecks(dialog).Single(box => AutomationProperties.GetName(box) == busyLabel);
            Assert.False(busyBox.IsEnabled, "a busy row became selectable after recycling");
            Assert.False(busyBox.IsChecked);
            Assert.Equal("1 of 39 available captures selected · about 4 KiB", Texts(dialog).First(text => text.Contains(" of 39 ", StringComparison.Ordinal)));
        }
        finally
        {
            window.Close();
        }
    }

    private static string Stamp(Control dialog, TemporarySessionInfo session) =>
        RowChecks(dialog)
            .Select(box => AutomationProperties.GetName(box) ?? string.Empty)
            .Select(name => name.StartsWith("Select ", StringComparison.Ordinal) ? name["Select ".Length..] : name)
            .First(label => label.StartsWith(SessionCacheName.Describe(session.Path) + " ", StringComparison.Ordinal));

    /// <summary>Collects every report as it is made, with no scheduling in between.</summary>
    private sealed class RecordingProgress : IProgress<CaptureDeletionProgress>
    {
        private readonly List<CaptureDeletionProgress> _reports = [];

        internal IReadOnlyList<CaptureDeletionProgress> Reports => _reports;

        public void Report(CaptureDeletionProgress value) => _reports.Add(value);
    }

    private sealed class RefusingBody : DialogBody<string>
    {
        internal RefusingBody()
            : base("Refusing body")
        {
            Content = new TextBlock { Text = "body" };
        }

        internal bool Allow { get; set; }

        internal int Dismissals { get; private set; }

        internal override void Dismiss()
        {
            Dismissals++;
            if (Allow)
            {
                base.Dismiss();
            }
        }
    }
}
