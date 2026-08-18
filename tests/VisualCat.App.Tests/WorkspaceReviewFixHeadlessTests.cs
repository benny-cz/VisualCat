using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Domain.Entries;

namespace VisualCat.App.Tests;

/// <summary>
/// The workspace-level fixes from the Android UX review, exercised through the real view.
/// </summary>
public sealed class WorkspaceReviewFixHeadlessTests
{
    /// <summary>
    /// A one-line budget under word wrapping draws the text up to the last break opportunity
    /// that fits rather than the text that fits, so rows ellipsised at a third of their width
    /// with two thirds empty beside them — arbitrarily, because it depended on where the break
    /// opportunities fell (finding 2). A single-line cell does not wrap.
    /// </summary>
    [AvaloniaFact]
    public async Task ACollapsedRowFillsItsWidthAndOnlyTheSelectedRowWraps()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100000   100   101 I ProbeTag       : A onRebind: Intent { act=android.intent.action.SCREEN_ON flg=0x50000010 }\n" +
            "01-01 00:00:00.200000   100   101 I ProbeTag       : B onRebind: Zntent Z act=android.intent.action.SCREEN_ON flg=0x50000010 Z\n" +
            "01-01 00:00:00.300000   100   101 I ProbeTag       : H Broadcasting: Intent { act=android.intent.action.TIME_TICK flg=0x50200014 }\n");

        var entries = fixture.Entries;
        for (var index = 0; index < 3; index++)
        {
            var message = MessageBlock(entries, index);
            Assert.Equal(TextWrapping.NoWrap, message.TextWrapping);
            Assert.Equal(1, message.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, message.TextTrimming);
        }

        entries.SelectedIndex = 1;
        var selected = MessageBlock(entries, 1);
        Assert.Equal(TextWrapping.Wrap, selected.TextWrapping);
        Assert.True(selected.MaxLines > 1);

        // Every row that is not the reader's stays a single scannable line.
        Assert.Equal(TextWrapping.NoWrap, MessageBlock(entries, 0).TextWrapping);
    }

    /// <summary>
    /// A row with no automation name of its own falls back to its content's ToString(), and the
    /// content is a record whose generated ToString() is a field-by-field dump including the
    /// session guid and the raw span. A screen reader read that for every row (finding 9).
    /// </summary>
    [AvaloniaFact]
    public async Task ARowIsSpokenAsAnEntryRatherThanAsARecordDump()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100000   100   101 E Loader         : request 1 failed\n");

        var container = fixture.Entries.ContainerFromIndex(0);
        Assert.NotNull(container);
        var name = AutomationProperties.GetName(container);

        Assert.StartsWith("Error Loader at ", name, StringComparison.Ordinal);
        Assert.EndsWith("request 1 failed", name, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionId", name, StringComparison.Ordinal);
        Assert.DoesNotContain("RawSpan", name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every logcat format prints milliseconds, so a fixed six digits rendered three constant
    /// zeros on every row of most captures (finding 25).
    /// </summary>
    [AvaloniaFact]
    public async Task AMillisecondCaptureRendersMillisecondTimestamps()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100   100   101 I Worker         : one\n" +
            "01-01 00:00:00.200   100   101 I Worker         : two\n");

        var container = fixture.Entries.ContainerFromIndex(0);
        Assert.NotNull(container);
        var rendered = container.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(static block => block.Text)
            .Where(static text => text is { Length: > 0 })
            .ToArray();

        Assert.Contains(rendered, static text => text!.EndsWith("00:00:00.100", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, static text => text!.Contains("00:00:00.100000", StringComparison.Ordinal));
    }

    /// <summary>
    /// The chip strip appeared with the first chip and pushed everything below it down by a
    /// touch row, so applying a filter moved every control the reader was about to use
    /// (finding 26). Its row is reserved for any session that can be filtered.
    /// </summary>
    [AvaloniaFact]
    public async Task TheFilterChipRowKeepsItsHeightWhileNothingIsFiltered()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100000   100   101 I Worker         : one\n" +
            "01-01 00:00:00.200000   100   101 E Loader         : two\n");

        var emptyLabel = fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(static block => block.Text == "No filters · showing everything in view");
        Assert.True(emptyLabel.IsVisible);

        await fixture.Tab.SetLevelAsync(LogLevel.Error, included: false);

        Assert.False(emptyLabel.IsVisible);
        Assert.Contains(
            fixture.View.GetLogicalDescendants().OfType<Button>(),
            static button => Equals(button.Content, "Clear all") && button.IsVisible);
    }

    /// <summary>
    /// The workspace's Escape handling claimed every key press whether or not it had anything
    /// to dismiss, and on Android the Back gesture arrives the same way — so the press was
    /// reported handled, the platform never backgrounded the task, and the app could not be
    /// left with the gesture at all (finding 20).
    /// </summary>
    [AvaloniaFact]
    public async Task DismissingNothingLeavesTheKeyForThePlatform()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100000   100   101 I Worker         : one\n");

        Assert.False(fixture.View.TryHandleShortcut(new KeyEventArgs { Key = Key.Escape }));

        await fixture.Tab.SetLevelAsync(LogLevel.Info, included: false);

        // With a filter to give up, the workspace takes the key.
        Assert.True(fixture.View.TryHandleShortcut(new KeyEventArgs { Key = Key.Escape }));
    }

    /// <summary>
    /// The inspector's context line was written once, from the cell count the load carried, so
    /// it went on quoting a bar that had already been released (finding 6).
    /// </summary>
    [AvaloniaFact]
    public async Task TheInspectorContextLineDescribesTheSelectionAsItStands()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.100000   100   101 I Worker         : one\n" +
            "01-01 00:00:00.200000   100   101 I Worker         : two\n");

        fixture.Entries.SelectedIndex = 1;
        var hint = fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(static block => AutomationProperties.GetAutomationId(block) ==
                                    SessionWorkspaceView.InspectorSelectionHintId);

        Assert.Equal("Selected entry", hint.Text);
    }

    /// <summary>
    /// A failed import built a complete workspace over an empty store: filters, mode buttons, a
    /// sort dropdown and a blank slab, with nothing saying the import had failed except a
    /// truncated line in the status bar (finding 10).
    /// </summary>
    [AvaloniaFact]
    public async Task AFailedImportShowsTheReasonInsteadOfAnInertWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
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
            Assert.Equal(SessionActivity.Failed, tab.Activity);
            Assert.NotNull(tab.FailureReason);
            Assert.DoesNotContain("format override", tab.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("Failed · ", tab.Status, StringComparison.Ordinal);

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            try
            {
                var card = view.GetLogicalDescendants()
                    .OfType<Border>()
                    .Single(static border =>
                        AutomationProperties.GetName(border) == "This log could not be read");
                Assert.True(card.IsVisible);

                // The inert panes are out of the tree's reach, not merely covered: automation
                // skips a subtree whose ancestor is not visible, so a screen reader cannot walk
                // into a workspace that has nothing to show.
                Assert.False(view.GetLogicalDescendants().OfType<TimelineControl>().Single().IsEffectivelyVisible);
                Assert.False(view.GetLogicalDescendants()
                    .OfType<ListBox>()
                    .Single(static list => AutomationProperties.GetName(list) == "Filtered log entries")
                    .IsEffectivelyVisible);

                var reason = card.GetLogicalDescendants().OfType<SelectableTextBlock>().Single();
                Assert.Equal(tab.FailureReason, reason.Text);
                Assert.Contains(
                    card.GetLogicalDescendants().OfType<Button>(),
                    static button => Equals(button.Content, "Close this tab"));
            }
            finally
            {
                window.Close();
            }

            await workspace.CloseAsync(tab);
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

    /// <summary>
    /// Zooming and scoping were the same gesture: the press that zoomed returned early without
    /// recording a drag origin, so the release that ended it looked like a stationary click and
    /// selected a cell — a double-tap zoomed and silently replaced the entry list with the
    /// contents of one bar, complete with a chip the reader never asked for (finding 4).
    /// </summary>
    [AvaloniaFact]
    public async Task DoubleTappingThePlotZoomsWithoutSelectingASecondCell()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(
            "01-01 00:00:00.000000   100   101 I Worker         : one\n" +
            "01-01 00:00:10.000000   100   101 I Worker         : two\n" +
            "01-01 00:00:20.000000   100   101 E Loader         : three\n" +
            "01-01 00:00:30.000000   100   101 I Worker         : four\n");

        var timeline = fixture.View.GetLogicalDescendants().OfType<TimelineControl>().Single();
        var selections = 0;
        var zooms = 0;
        timeline.CellSelected += (_, _) => selections++;
        timeline.ViewportChanged += (_, _) => zooms++;

        var inside = timeline.TranslatePoint(
            new Point(timeline.Bounds.Width / 2, timeline.Bounds.Height / 2),
            fixture.Window);
        Assert.NotNull(inside);
        var point = inside.Value;

        // One tap on a bar is a request to list it, and stays one.
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);
        Assert.Equal(1, selections);

        // The second tap of a double-tap zooms. Its release must not also scope the list.
        fixture.Window.MouseDown(point, MouseButton.Left);
        fixture.Window.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, selections);
        Assert.True(zooms > 0, "the double-tap should have zoomed");
    }

    /// <summary>
    /// A view answers its session through the dispatcher, so a change raised while the tab was
    /// alive can arrive after it has been closed. The redraw then read a session that was being
    /// torn down and threw <see cref="ObjectDisposedException"/> from inside the plot — which
    /// on a real device is an unhandled exception on the UI thread, and in the test run showed
    /// up as a cleanup failure. Closing a session must be safe at any moment.
    /// </summary>
    [AvaloniaFact]
    public async Task AClosedSessionStopsDrivingItsWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "closing.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                "01-01 00:00:00.100000   100   101 I Worker         : one\n" +
                "01-01 00:00:01.200000   100   101 E Loader         : two\n",
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();
            var snapshot = tab.Snapshot;
            Assert.NotNull(snapshot);

            await workspace.CloseAsync(tab);

            // The snapshot is unpublished before it is released, so no reader can be handed a
            // disposed one — the object itself really is dead.
            Assert.True(tab.IsDisposed);
            Assert.Null(tab.Snapshot);
            Assert.NotEmpty(snapshot.Segments);
            Assert.Throws<ObjectDisposedException>(() => _ = snapshot.Segments[0].SeverityBitmaps);

            // And a notification that arrives after the close redraws nothing.
            tab.Status = "Ready · 2 entries";
            tab.FollowLatest = true;
            tab.IsLiveCaptureActive = false;
            Dispatcher.UIThread.RunJobs();
            window.Close();
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

    private static TextBlock MessageBlock(ListBox entries, int index)
    {
        var container = entries.ContainerFromIndex(index);
        Assert.NotNull(container);
        return container.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(static block => block.Name == "EntryMessage");
    }

    /// <summary>An imported session with a live workspace over it, torn down together.</summary>
    private sealed class WorkspaceFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly WorkspaceViewModel _workspace;
        private readonly Window _window;

        private WorkspaceFixture(
            string root,
            WorkspaceViewModel workspace,
            SessionTabViewModel tab,
            SessionWorkspaceView view,
            Window window)
        {
            _root = root;
            _workspace = workspace;
            _window = window;
            Tab = tab;
            View = view;
        }

        public SessionTabViewModel Tab { get; }

        public Window Window => _window;

        public SessionWorkspaceView View { get; }

        public ListBox Entries => View.GetLogicalDescendants()
            .OfType<ListBox>()
            .Single(static list => AutomationProperties.GetName(list) == "Filtered log entries");

        public static async Task<WorkspaceFixture> CreateAsync(string log)
        {
            var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
            var sourcePath = Path.Combine(root, "session.txt");
            await File.WriteAllTextAsync(sourcePath, log, TestContext.Current.CancellationToken);
            var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            return new WorkspaceFixture(root, workspace, tab, view, window);
        }

        public async ValueTask DisposeAsync()
        {
            _window.Close();
            await _workspace.CloseAsync(Tab);
            await _workspace.DisposeAsync();
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
