using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Application.Coordination;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.App.Tests;

public sealed class SessionWorkspaceHeadlessTests
{
    [AvaloniaFact]
    public async Task ActiveCaptureKeepsStopActionVisibleDuringSnapshotRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Live", root)
        {
            IsLiveCaptureActive = true,
        };

        // A progressive snapshot used to overwrite Capturing with Importing and
        // accidentally hide the only graceful-stop action.
        tab.ReportActivity(SessionActivity.Importing, "Importing · 1 committed · snapshot 1");
        var view = new SessionWorkspaceView(tab);
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        try
        {
            var stop = view.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Stop capture"));
            Assert.True(stop.IsVisible);
            Assert.True(stop.IsEnabled);
        }
        finally
        {
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ImportQueryFilterPagePersistAndComposeWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "headless-logcat.txt");
            await File.WriteAllTextAsync(sourcePath, BuildLog(650), TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var added = 0;
            var removed = 0;
            workspace.TabAdded += (_, _) => added++;
            workspace.TabRemoved += (_, _) => removed++;

            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);

            Assert.Equal(1, added);
            Assert.Same(tab, workspace.Selected);
            Assert.NotNull(tab.Snapshot);
            Assert.NotNull(tab.HeatMap);
            Assert.NotNull(tab.Overview);
            Assert.NotNull(tab.Statistics);
            Assert.Equal(650, tab.Statistics.TotalMatching);
            Assert.Equal(SessionTabViewModel.EntryPageSize, tab.Entries.Count);
            Assert.True(tab.CanLoadMore);

            await tab.LoadNextEntryPageAsync(TestContext.Current.CancellationToken);
            Assert.Equal(650, tab.Entries.Count);
            Assert.False(tab.CanLoadMore);

            var errorTag = FacetKey.OfText("ErrorTag");
            await tab.ToggleFacetAsync(FacetDimension.Tag, errorTag, exclude: false);
            Assert.Equal(FacetState.Included, tab.StateOf(FacetDimension.Tag, errorTag));
            Assert.Equal(130, tab.Entries.Count);
            Assert.All(tab.Entries, static entry => Assert.Equal("ErrorTag", entry.Tag));

            tab.SearchText = "failure";
            await tab.ApplySearchAsync(regex: false, caseSensitive: false);
            Assert.Equal(130, tab.SearchResult?.Matches);
            Assert.All(tab.Entries, static entry => Assert.Contains("failure", entry.Message));

            await tab.SaveCurrentViewAsync("Failures", TestContext.Current.CancellationToken);
            Assert.Contains("Failures", tab.SavedViews);
            await tab.ClearFiltersAsync();
            Assert.Equal(SessionTabViewModel.EntryPageSize, tab.Entries.Count);
            await tab.ApplySavedViewAsync("Failures", TestContext.Current.CancellationToken);
            Assert.Equal(FacetState.Included, tab.StateOf(FacetDimension.Tag, errorTag));
            Assert.Equal("failure", tab.SearchText);

            await tab.SetEntryOrderAsync(EntryOrder.SourceSequence);
            Assert.Equal(EntryOrder.SourceSequence, tab.EntryOrder);
            Assert.Equal(tab.Entries.OrderBy(static entry => entry.SourceSequence), tab.Entries);

            var selected = Assert.Single(tab.Entries.Take(1));
            await tab.LoadRawContextAsync(selected, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Contains("failure", tab.RawContextText);
            var raw = await tab.ReadRawEntriesAsync([selected], TestContext.Current.CancellationToken);
            Assert.Contains("failure", raw);

            var sessionRange = Assert.IsType<TimeRange>(tab.Snapshot.TimedRange);
            var midpoint = new InstantUs(sessionRange.StartInclusive.Value + sessionRange.DurationUs / 2);
            var firstHalf = new TimeRange(sessionRange.StartInclusive, midpoint);
            var secondHalf = new TimeRange(midpoint, sessionRange.EndExclusive);
            await Task.WhenAll(tab.SetViewportAsync(firstHalf), tab.SetViewportAsync(secondHalf));
            Assert.Equal(secondHalf, tab.Viewport);
            Assert.Equal(secondHalf, tab.HeatMap?.Viewport.Range);

            await tab.RefreshCellAsync(secondHalf, LogLevel.Error, TestContext.Current.CancellationToken);
            Assert.Equal(LogLevel.Error, tab.DetailLevel);
            await tab.SetLevelAsync(LogLevel.Error, included: false);
            Assert.Null(tab.DetailLevel);
            Assert.Null(tab.DetailRange);
            await tab.SetLevelAsync(LogLevel.Error, included: true);

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            try
            {
                var accessibleNames = view.GetLogicalDescendants()
                    .OfType<Control>()
                    .Select(AutomationProperties.GetName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                Assert.Contains("Session filters", accessibleNames);
                Assert.Contains("Filtered log entries", accessibleNames);
                Assert.Contains("Facets", accessibleNames);
                Assert.Contains(
                    accessibleNames,
                    static name => name?.StartsWith("COUNTS · THIS VIEW.", StringComparison.Ordinal) == true);
                // The scope qualifier is in the visible label, not only in a tooltip a touch
                // device never shows (finding 11).
                Assert.Contains(
                    accessibleNames,
                    static name =>
                        name?.StartsWith("COUNTS · WHOLE SESSION · CURRENT FILTER.", StringComparison.Ordinal) == true);

                var search = view.GetLogicalDescendants()
                    .OfType<TextBox>()
                    .Single(textBox => textBox.PlaceholderText?.Contains("Search", StringComparison.Ordinal) == true);
                Assert.True(view.TryHandleShortcut(new KeyEventArgs
                {
                    Key = Key.F,
                    KeyModifiers = KeyModifiers.Control,
                }));
                Assert.True(search.IsFocused);

                var entries = view.GetLogicalDescendants()
                    .OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Filtered log entries");
                entries.SelectedIndex = 0;
                Assert.True(view.TryHandleShortcut(new KeyEventArgs { Key = Key.J }));
                Assert.Equal(1, entries.SelectedIndex);

                // The plot marks the row the table has selected, so scrolling the table
                // cannot silently cost the user their place in the plot (§14.9).
                var timeline = view.GetLogicalDescendants().OfType<TimelineControl>().Single();
                var selectedEntry = Assert.IsType<NormalizedEntry>(entries.SelectedItem);
                Assert.Equal(selectedEntry.Timestamp, timeline.SelectedEntryInstant);
                Assert.Equal(selectedEntry.Level, timeline.SelectedEntryLevel);
                entries.SelectedIndex = -1;
                Assert.Null(timeline.SelectedEntryInstant);

                // The active search term is marked inside the message rather than left for
                // the reader to find in every one of the matching rows.
                entries.SelectedIndex = 0;
                var marked = view.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .SelectMany(static block => block.Inlines ?? [])
                    .OfType<Run>()
                    .Where(static run => run.Background is not null)
                    .ToArray();
                Assert.NotEmpty(marked);
                Assert.All(marked, static run => Assert.Equal("failure", run.Text));
            }
            finally
            {
                window.Close();
            }

            await workspace.CloseAsync(tab);
            Assert.Empty(workspace.Tabs);
            Assert.Equal(1, removed);
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
    /// A tap on a bar used to end at a clipped line with no way forward: the row did not
    /// grow, and the whole message existed nowhere the reader could reach. The row now
    /// opens for the entry the reader picked, and the inspector holds all of it.
    /// </summary>
    [AvaloniaFact]
    public async Task SelectingARowOpensItAndTheInspectorHoldsTheWholeMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var novel = "dlopen failed: library not found in " + new string('p', 400);
            var sourcePath = Path.Combine(root, "wide-messages.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                "01-01 00:00:00.000000   100   101 I Worker         : short\n" +
                $"01-01 00:00:01.000000   100   101 E Loader         : {novel}\n",
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            try
            {
                var entries = view.GetLogicalDescendants()
                    .OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Filtered log entries");
                var longIndex = tab.Entries.Count - 1;
                Assert.Contains(novel, tab.Entries[longIndex].Message, StringComparison.Ordinal);

                var openEntry = view.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button => Equals(button.Content, "Full entry"));
                Assert.False(openEntry.IsEnabled);

                entries.SelectedIndex = longIndex;

                // Exactly one row differs, so the table stays scannable while the row the
                // reader picked shows more of its message.
                Assert.Equal(2, MessageLines(entries, longIndex));
                Assert.Equal(1, MessageLines(entries, 0));
                Assert.True(openEntry.IsEnabled);

                var inspector = view.GetLogicalDescendants()
                    .OfType<SelectableTextBlock>()
                    .Single(block => AutomationProperties.GetAutomationId(block) ==
                                     SessionWorkspaceView.InspectorMessageId);
                Assert.Equal(tab.Entries[longIndex].Message, inspector.Text);
                Assert.True(inspector.IsVisible);

                // A live snapshot replaces every row with an equal instance. The reader's
                // entry — and the plot's mark for it — has to survive that.
                var timeline = view.GetLogicalDescendants().OfType<TimelineControl>().Single();
                var before = Assert.IsType<NormalizedEntry>(entries.SelectedItem);
                await tab.RefreshAsync(TestContext.Current.CancellationToken);
                var after = Assert.IsType<NormalizedEntry>(entries.SelectedItem);
                Assert.Equal(before.EntryId, after.EntryId);
                Assert.Equal(before.Timestamp, timeline.SelectedEntryInstant);

                // Releasing the selection still releases the mark and the inspector.
                entries.SelectedIndex = -1;
                Assert.Null(timeline.SelectedEntryInstant);
                Assert.False(openEntry.IsEnabled);
                Assert.Equal(string.Empty, inspector.Text);
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
    /// A device capture is parsed in UTC because that is what logcat is asked to emit, and
    /// reading it back in UTC put the newest entry a whole offset in the past — on a
    /// UTC+2 device a running capture with Follow engaged looked like it had stopped.
    /// The clock the workspace reads in is the reader's; the clock it stores in is not.
    /// </summary>
    [AvaloniaFact]
    public async Task ADeviceCaptureIsReadInTheReadersOwnClock()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var utcLog = Encoding.UTF8.GetBytes(
                "2026-05-15 12:13:00.000000  6642 19876 I Worker         : request 1 completed\n" +
                "2026-05-15 12:13:01.000000  6642 19876 E Loader         : request 2 failed\n");

            await using var workspace = new WorkspaceViewModel();

            // Goes through the live capture path, progress reporter and all. That reporter
            // opens snapshots while the capture is still writing, which used to collide
            // with the atomic manifest replace and fail a short capture outright.
            await using var device = new MemoryLogSource(
                utcLog,
                name: "on-device",
                kind: SourceKind.Android,
                logTimeZoneId: "UTC");
            var captured = await workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);
            var deviceView = new SessionWorkspaceView(captured);

            // Parsed in the zone the source declares, read in the reader's own clock.
            Assert.Equal(SourceKind.Android, captured.Snapshot?.Descriptor.SourceKind);
            Assert.Equal("UTC", captured.Snapshot?.Descriptor.TimestampPolicy.TimeZoneId);
            Assert.Equal(TimeZoneInfo.Local.Id, deviceView.DisplayZoneId());

            // An imported file keeps its policy zone, which is what makes a rendered row
            // agree with the raw line behind it.
            var filePath = Path.Combine(root, "imported.txt");
            await File.WriteAllTextAsync(
                filePath,
                "05-15 12:13:00.000000  6642 19876 I Worker         : request 1 completed\n",
                TestContext.Current.CancellationToken);
            var imported = await workspace.ImportFileAsync(filePath, TestContext.Current.CancellationToken);
            var importedView = new SessionWorkspaceView(imported);
            Assert.Equal(
                imported.Snapshot?.Descriptor.TimestampPolicy.TimeZoneId,
                importedView.DisplayZoneId());

            await workspace.CloseAsync(captured);
            await workspace.CloseAsync(imported);
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
    /// A logcat ring buffer can hold hours of history with very few entries in it, so a
    /// capture opens on a session whose range is wide and whose contents are sparse.
    /// Following the latest data has to mean a window on the end of it: at whole-session
    /// span a second of new data is a fraction of a pixel, which is indistinguishable from
    /// a capture that has stopped.
    /// </summary>
    [AvaloniaFact]
    public async Task FollowingASparseCaptureOpensOnAWindowNotTheWholeSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            // Two entries, two and a half hours apart: the shape a stale ring buffer has.
            var sparse = Encoding.UTF8.GetBytes(
                "2026-05-15 09:00:00.000000  6642 19876 I Worker         : oldest buffered line\n" +
                "2026-05-15 11:30:00.000000  6642 19876 I Worker         : newest buffered line\n");

            await using var workspace = new WorkspaceViewModel();
            await using var device = new MemoryLogSource(
                sparse,
                name: "on-device",
                kind: SourceKind.Android,
                logTimeZoneId: "UTC");
            var tab = await workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);

            Assert.True(tab.FollowLatest);
            var session = Assert.IsType<TimeRange>(tab.Snapshot?.TimedRange);
            Assert.True(session.DurationUs > TimeSpan.FromHours(2).Ticks / 10, "the session should span hours");

            var viewport = Assert.IsType<TimeRange>(tab.Viewport);
            Assert.Equal(session.EndExclusive, viewport.EndExclusive);
            Assert.True(
                viewport.DurationUs <= TimeSpan.FromSeconds(30).Ticks / 10,
                $"following opened on {viewport.DurationUs / 1_000_000d:N0}s of a {session.DurationUs / 1_000_000d:N0}s session");

            // Fitting the whole session releases follow, which is right. Re-engaging it
            // used to keep the whole-session span, leaving the capture "following" at a
            // resolution where new data cannot be seen arriving at all.
            //
            // Fitted to very slightly less than the session, which is the only shape this
            // ever has on a live capture: the session has already grown by the time the
            // reader reaches for the button.
            var fitted = new TimeRange(
                new InstantUs(session.StartInclusive.Value + session.DurationUs / 50),
                session.EndExclusive);
            await tab.SetViewportAsync(fitted);
            Assert.False(tab.FollowLatest);
            Assert.Equal(fitted.DurationUs, Assert.IsType<TimeRange>(tab.Viewport).DurationUs);

            await tab.ToggleFollowAsync();
            Assert.True(tab.FollowLatest);
            var refollowed = Assert.IsType<TimeRange>(tab.Viewport);
            Assert.Equal(session.EndExclusive, refollowed.EndExclusive);
            Assert.True(
                refollowed.DurationUs <= TimeSpan.FromSeconds(30).Ticks / 10,
                $"re-engaging follow kept {refollowed.DurationUs / 1_000_000d:N0}s of a {session.DurationUs / 1_000_000d:N0}s session");

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
    /// F-25 — a busy live window can have another page even after it has moved past the entry
    /// being read. That page cursor must not suppress the off-screen explanation or discard
    /// the inspector.
    /// </summary>
    [AvaloniaFact]
    public async Task AFollowRefreshKeepsAndExplainsAnEntryThatAgesOutWithAnotherPageAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "busy-live-window.txt");
            var builder = new StringBuilder(650 * 96);
            var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 650; index++)
            {
                var instant = first.AddMilliseconds(index * 50);
                builder.Append(instant.ToString("MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture))
                    .Append("   100   101 I Worker         : live row ")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            await File.WriteAllTextAsync(sourcePath, builder.ToString(), TestContext.Current.CancellationToken);
            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            var session = Assert.IsType<TimeRange>(tab.Snapshot?.TimedRange);
            var initial = new TimeRange(
                new InstantUs(session.EndExclusive.Value - 30_000_000),
                session.EndExclusive);
            await tab.SetViewportAsync(initial, manual: false);
            Assert.True(tab.CanLoadMore);

            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = 900, Height = 700 };
            window.Show();
            try
            {
                var entries = view.GetLogicalDescendants()
                    .OfType<ListBox>()
                    .Single(list => AutomationProperties.GetName(list) == "Filtered log entries");
                entries.SelectedIndex = 0;
                var inspected = Assert.IsType<NormalizedEntry>(entries.SelectedItem);

                tab.IsLiveCaptureActive = true;
                tab.FollowLatest = true;
                var advanced = new TimeRange(
                    new InstantUs(initial.StartInclusive.Value + 1_000_000),
                    initial.EndExclusive);
                await tab.SetViewportAsync(advanced, manual: false);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.True(tab.CanLoadMore);
                Assert.DoesNotContain(tab.Entries, entry => entry.EntryId == inspected.EntryId);
                var explanation = view.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Single(block => block.Text?.StartsWith("This entry has scrolled out", StringComparison.Ordinal) == true);
                Assert.True(explanation.IsVisible);
                var openInspector = view.GetLogicalDescendants()
                    .OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Show the full message of the selected entry");
                Assert.True(openInspector.IsEnabled);
            }
            finally
            {
                window.Close();
                await workspace.CloseAsync(tab);
            }
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
    /// The capture status used to carry a session-long average that kept claiming lines
    /// were arriving long after the source fell silent.
    /// </summary>
    [AvaloniaFact]
    public async Task CaptureStatusReportsARecentRateRatherThanASessionAverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Live", root) { IsLiveCaptureActive = true };
        try
        {
            var first = tab.DescribeCaptureProgress("On-device own-app logcat", 143);
            Assert.Contains("143 lines", first, StringComparison.Ordinal);
            Assert.Contains("On-device own-app logcat", first, StringComparison.Ordinal);

            // No window has elapsed, so no rate has been measured yet — and crucially the
            // status never inherits one from the burst that opened the capture.
            Assert.Contains("0/s", first, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Source activity and committed visibility are different clocks. A busy Android reader
    /// must not be described as quiet merely because parsing or publication is behind it.
    /// </summary>
    [AvaloniaFact]
    public async Task CaptureStatusUsesSourceLinesAndReportsCommitBacklogSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Live", root) { IsLiveCaptureActive = true };
        try
        {
            var progress = Progress(linesRead: 250, linesCommitted: 175);
            var status = tab.DescribeCaptureProgress("On-device full-device logcat", progress);

            Assert.Contains("250 lines received", status, StringComparison.Ordinal);
            Assert.Contains("75 pending", status, StringComparison.Ordinal);
            Assert.DoesNotContain("175 lines received", status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A capture that is quietly failing to write, or a view that has quietly stopped
    /// refreshing, must not keep looking like a healthy quiet capture.
    /// </summary>
    [AvaloniaFact]
    public async Task CaptureStatusRaisesTroubleTheCaptureIsWorkingThrough()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Live", root) { IsLiveCaptureActive = true };
        try
        {
            Assert.Null(tab.CaptureHealthWarning);
            Assert.DoesNotContain("⚠", tab.DescribeCaptureProgress("On-device logcat", 10), StringComparison.Ordinal);

            var troubled = tab.DescribeCaptureProgress("On-device logcat", Progress(12, warning: "Storage is not accepting writes"));
            Assert.Contains("⚠", troubled, StringComparison.Ordinal);
            Assert.Contains("Storage is not accepting writes", tab.CaptureHealthWarning!, StringComparison.Ordinal);

            // A refresh failure is a different claim from a capture failure: the capture
            // is fine, the picture is not, and the wording has to say which.
            _ = tab.DescribeCaptureProgress("On-device logcat", Progress(14));
            Assert.Null(tab.CaptureHealthWarning);
            tab.ReportRefreshOutcome("ran out of open files");
            Assert.Contains("capture is still running", tab.CaptureHealthWarning!, StringComparison.OrdinalIgnoreCase);

            tab.ReportRefreshOutcome(null);
            Assert.Null(tab.CaptureHealthWarning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The framework's text for a descriptor limit is an errno phrase and whichever path
    /// happened to be unlucky. That is what a user saw when a long capture died, and it
    /// named neither the problem nor a way out of it.
    /// </summary>
    [Fact]
    public void ResourceFailuresAreTranslatedIntoSomethingActionable()
    {
        var exhausted = WorkspaceViewModel.FriendlyMessage(
            new IOException("Too many open files : '/data/user/0/com.barebit.visualcat/files/x/segments/001058/flags.bin'"));
        Assert.Contains("ran out of open files", exhausted, StringComparison.Ordinal);
        Assert.DoesNotContain("flags.bin", exhausted, StringComparison.Ordinal);

        Assert.Contains("full", WorkspaceViewModel.FriendlyMessage(new IOException("There is not enough space on the disk.")), StringComparison.Ordinal);
        Assert.Contains("missing", WorkspaceViewModel.FriendlyMessage(new FileNotFoundException("nope")), StringComparison.Ordinal);

        // A condition is recognised through the layers that wrapped it on the way here.
        var wrapped = WorkspaceViewModel.FriendlyMessage(
            new AggregateException(new IOException("Too many open files : '/a/b/c.bin'")));
        Assert.Contains("ran out of open files", wrapped, StringComparison.Ordinal);

        // A capture that gave up says how much it could not save and what survived,
        // instead of naming whichever write happened to be last.
        var refused = WorkspaceViewModel.FriendlyMessage(new SegmentWriteRefusedException(
            "Storage refused 33 attempts in a row to save a log segment, so the capture stopped. The 4,120 entries " +
            "captured since the last successful save could not be written; everything saved before then is intact " +
            "and the session is still open.",
            33,
            4120,
            new IOException("The file '/data/user/0/com.x/files/VisualCat/Sessions/s.vcat/segments/000342' already exists.")));
        Assert.Contains("everything saved before then is intact", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("000342", refused, StringComparison.Ordinal);

        // An ordinary write failure keeps its meaning but loses the 130-character path
        // that would otherwise fill the whole status line.
        var io = WorkspaceViewModel.FriendlyMessage(new IOException(
            "The file '/data/user/0/com.barebit.visualcat/files/VisualCat/Sessions/20260817-171031-On-device logcat-48529e2a.vcat/segments/000342' already exists."));
        Assert.Contains("already exists", io, StringComparison.Ordinal);
        Assert.Contains("…/segments/000342", io, StringComparison.Ordinal);
        Assert.DoesNotContain("/data/user/0/", io, StringComparison.Ordinal);
        Assert.True(io.Length < 120, $"Status text is still {io.Length} characters: {io}");

        // Anything without a better translation keeps its own message rather than being
        // flattened into a generic apology.
        Assert.Equal("something specific", WorkspaceViewModel.FriendlyMessage(new InvalidOperationException("something specific")));
    }

    private static ProgressSnapshot Progress(long lines, string? warning = null) =>
        Progress(lines, lines, warning);

    private static ProgressSnapshot Progress(long linesRead, long linesCommitted, string? warning = null) => new(
        Guid.NewGuid(),
        1,
        IngestStage.Committing,
        0,
        0,
        linesRead,
        linesCommitted,
        null,
        new SessionCounters(),
        0,
        TimeSpan.Zero,
        null,
        true,
        true,
        1,
        null,
        null,
        SegmentCount: 3,
        Warning: warning);

    /// <summary>Line budget the row at <paramref name="index"/> currently draws.</summary>
    private static int MessageLines(ListBox entries, int index)
    {
        var container = entries.ContainerFromIndex(index);
        Assert.NotNull(container);
        return container.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(block => block.Name == "EntryMessage")
            .MaxLines;
    }

    private static string BuildLog(int lines)
    {
        var builder = new StringBuilder(lines * 96);
        for (var index = 0; index < lines; index++)
        {
            var level = index % 5 == 0 ? 'E' : 'I';
            var tag = level == 'E' ? "ErrorTag" : "Worker";
            var message = level == 'E' ? $"request {index} failure code 500" : $"request {index} completed";
            builder.Append("01-01 00:00:00.")
                .Append((index * 1_000).ToString("000000", CultureInfo.InvariantCulture))
                .Append("   100   101 ")
                .Append(level)
                .Append(' ')
                .Append(tag.PadRight(15))
                .Append(": ")
                .AppendLine(message);
        }

        return builder.ToString();
    }
}
