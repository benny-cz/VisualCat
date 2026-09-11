using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.Coordination;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.App.Tests;

/// <summary>
/// What a session says about itself while it is being read, and what it does with the viewport
/// nobody has touched yet.
/// </summary>
public sealed class SessionActivityTests
{
    /// <summary>
    /// The viewport was seeded from the first progressive snapshot, when the session genuinely
    /// held a handful of entries, and nothing re-fitted it as the rest arrived — so every import
    /// finished showing one row and an empty plot beside a minimap already drawing the whole
    /// session (finding 1).
    /// </summary>
    [AvaloniaFact]
    public async Task AnImportEndsShowingTheWholeSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            // Long enough, and spread widely enough in time, that a partial snapshot's range is
            // obviously narrower than the finished session's.
            var sourcePath = Path.Combine(root, "wide-session.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                BuildLog(20_000, TimeSpan.FromHours(2)),
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);

            Assert.Equal(SessionActivity.Ready, tab.Activity);
            Assert.NotNull(tab.Snapshot?.TimedRange);
            Assert.Equal(tab.Snapshot.TimedRange, tab.Viewport);

            // Nobody has stated what they want to look at yet, so the session still owns it.
            Assert.True(tab.ViewportIsAuto);

            var session = tab.Snapshot.TimedRange.Value;
            var half = new TimeRange(
                session.StartInclusive,
                new InstantUs(session.StartInclusive.Value + session.DurationUs / 2));
            await tab.SetViewportAsync(half);

            // From the first zoom or pan the viewport is the reader's, and nothing moves it.
            Assert.False(tab.ViewportIsAuto);
            Assert.Equal(half, tab.Viewport);

            await tab.LoadSnapshotAsync(true, TestContext.Current.CancellationToken);
            Assert.Equal(half, tab.Viewport);

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
    /// The same promise for an import large enough to be committed in more than one segment,
    /// which is the only size at which it was ever broken (Linux live test L-01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A session is committed in 100,000-entry segments, and the progressive reporter
    /// republishes the tab's snapshot as each new generation appears. Below one segment an
    /// import publishes its whole content in the generations the view consumes, so every
    /// existing test of this promise — 20,000 lines, 4,000 lines — passes whatever the
    /// refresh path does. Above it the finished session arrives in generations the view was
    /// never told about, and the workspace was left rendering a prefix of the log: 100,001 of
    /// 199,990 entries on one run, 800,001 of 999,892 on another, with the missing tail simply
    /// absent from the plot and no indication that anything was missing.
    /// </para>
    /// <para>
    /// The assertions are deliberately about the descriptor rather than the plot. Every
    /// visible surface — heat map, severity totals, time axis, entry list, templates, and the
    /// summary counters — is derived from the snapshot the tab holds, so the snapshot's own
    /// counters are what makes all of them right or all of them wrong at once.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task AMultiSegmentImportEndsShowingTheWholeSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            // Two and a half segments: enough that the last committed generation cannot be
            // the one the progressive reporter last published.
            const int Lines = 250_000;
            var sourcePath = Path.Combine(root, "multi-segment.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                BuildLog(Lines, TimeSpan.FromHours(2)),
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();

            // The workspace is on screen while the import runs, as it is in the product: the
            // tab's view is built the moment the tab appears and then redraws on every
            // progress refresh. That contention on the UI thread is what lets one refresh
            // still be in flight when the next generation lands.
            Window? window = null;
            workspace.TabAdded += (_, added) =>
            {
                window = new Window
                {
                    Content = new SessionWorkspaceView(added),
                    Width = 1280,
                    Height = 800,
                };
                window.Show();
            };

            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SessionActivity.Ready, tab.Activity);
            var snapshot = Assert.IsType<VisualCat.Core.Store.SessionSnapshot>(tab.Snapshot);

            // The session the reader is looking at is the session that was read.
            Assert.Equal(Lines, snapshot.Descriptor.Counters.TimedEntries);

            // And the plot is showing all of it, not the prefix that existed when the last
            // progress refresh happened to land.
            Assert.NotNull(snapshot.TimedRange);
            Assert.True(tab.ViewportIsAuto);
            Assert.Equal(snapshot.TimedRange, tab.Viewport);

            // The invariant underneath all of that, and the one the defect actually broke:
            // what is on screen was computed from the snapshot the tab holds. Every visible
            // number is derived from this query, so when it lags the store the tab reports
            // Ready over a session it is not showing — and nothing on screen says so.
            var applied = Assert.IsType<QueryIdentity>(tab.AppliedQueryIdentity);
            Assert.Equal(snapshot.Generation, applied.SnapshotGeneration);
            Assert.Equal(Lines, tab.Statistics?.TimedMatching);
            Assert.Equal(Lines, tab.MatchesInView);

            window?.Close();
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
    /// The same promise while the session is still growing: a viewport nobody has touched
    /// follows the session, so a partial first snapshot cannot leave the plot showing a sliver
    /// of a capture that has since grown past it (finding 1).
    /// </summary>
    [AvaloniaFact]
    public async Task AnUntouchedViewportFollowsAGrowingSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var log = Encoding.UTF8.GetBytes(BuildLog(4_000, TimeSpan.FromHours(1)));
            await using var workspace = new WorkspaceViewModel();
            SessionTabViewModel? captured = null;
            workspace.TabAdded += (_, tab) =>
            {
                captured = tab;

                // Assigned directly rather than through ToggleFollowAsync, which is a
                // statement about what to look at and would hand the viewport to the reader.
                tab.FollowLatest = false;
            };

            // Delivered in pieces, so the session is committed and published more than once and
            // the viewport is seeded from a range that is not the final one.
            await using var device = new MemoryLogSource(
                log,
                chunkSizes: [8 * 1024],
                delay: TimeSpan.FromMilliseconds(5),
                name: "on-device",
                kind: SourceKind.Android,
                logTimeZoneId: "UTC");
            var tab = await workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);

            Assert.Same(captured, tab);
            Assert.False(tab.FollowLatest);
            Assert.True(tab.ViewportIsAuto);
            var session = Assert.IsType<TimeRange>(tab.Snapshot?.TimedRange);
            Assert.True(session.DurationUs > TimeSpan.FromMinutes(30).TotalMilliseconds * 1_000);
            Assert.Equal(session, tab.Viewport);

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
    /// "Snapshot N", "committing" and "import capacity" are column-store words; a reader
    /// watching an import wants to know how much of their log is readable (finding 24).
    /// </summary>
    [AvaloniaFact]
    public async Task AFinishedImportSpeaksOfEntriesRatherThanSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "small.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                BuildLog(24, TimeSpan.FromSeconds(24)),
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);

            Assert.StartsWith("Ready · ", tab.Status, StringComparison.Ordinal);
            Assert.DoesNotContain("snapshot", tab.Status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("committ", tab.Status, StringComparison.OrdinalIgnoreCase);
            Assert.False(tab.IsSessionWorkInFlight);
            Assert.False(tab.IsLiveSourceAttached);

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
    /// The status bar is one clipped line and the ellipsis takes whatever is last, so the rate —
    /// the most volatile and most watched number in the app — used to be the first thing lost,
    /// behind a source description that never changes (finding 27).
    /// </summary>
    [AvaloniaFact]
    public async Task TheCaptureStatusPutsTheChangingNumbersBeforeTheScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var tab = new SessionTabViewModel("live", root) { IsLiveCaptureActive = true };
            const string scope = "On-device full-device logcat";

            var status = tab.DescribeCaptureProgress(scope, 8_312);

            Assert.StartsWith("Capturing", status, StringComparison.Ordinal);
            Assert.EndsWith(scope, status, StringComparison.Ordinal);
            var lines = 8_312.ToString("N0", CultureInfo.CurrentCulture);
            Assert.True(
                status.IndexOf(lines, StringComparison.Ordinal) < status.IndexOf(scope, StringComparison.Ordinal),
                $"The line count should precede the scope in '{status}'.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Follow and the new-data jump only mean something while a source can still add data. A
    /// finished capture cannot grow, and both used to stay on screen offering to follow a source
    /// that had closed (finding 27).
    /// </summary>
    [AvaloniaFact]
    public async Task AFinishedCaptureHasNoLiveSourceToFollow()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var tab = new SessionTabViewModel("live", root) { IsLiveCaptureActive = true };
            tab.ReportActivity(SessionActivity.Capturing, "Capturing · 12 lines · 4/s · scope");
            Assert.True(tab.IsLiveSourceAttached);
            Assert.True(tab.IsSessionWorkInFlight);

            tab.IsLiveCaptureActive = false;
            tab.ReportActivity(SessionActivity.Ready, "Ready · 12 entries");

            Assert.False(tab.IsLiveSourceAttached);
            Assert.False(tab.IsSessionWorkInFlight);
            Assert.False(tab.HasNewData);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Whether the plot carries an Unknown lane is a fact about the session, answered once when
    /// a snapshot is published. The workspace used to answer it by walking every segment's
    /// severity bitmaps on each redraw, which put store internals in the render path and read
    /// them from a queued job that could outlive the session it was reading.
    /// </summary>
    [AvaloniaTheory]
    [InlineData('X', true)]
    [InlineData('W', false)]
    public async Task TheUnknownLaneIsASessionFactAnsweredOncePerSnapshot(char level, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            var sourcePath = Path.Combine(root, "levels.txt");
            await File.WriteAllTextAsync(
                sourcePath,
                "01-01 00:00:00.100000   100   101 I Worker         : one\n" +
                $"01-01 00:00:01.200000   100   101 {level} Worker         : two\n",
                TestContext.Current.CancellationToken);

            await using var workspace = new WorkspaceViewModel();
            var tab = await workspace.ImportFileAsync(sourcePath, TestContext.Current.CancellationToken);

            Assert.Equal(expected, tab.HasUnknownLevelEntries);

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
    /// A failure keeps the whole reason for the workspace to show, and the platform-specific
    /// next step separate from it (finding 10).
    /// </summary>
    [AvaloniaFact]
    public async Task AFailureKeepsItsWholeReasonAndItsRemedy()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var tab = new SessionTabViewModel("broken", root);

            tab.ReportFailure("No supported logcat format could be detected in this file.", "Try a format override.");

            Assert.Equal(SessionActivity.Failed, tab.Activity);
            Assert.Equal("Failed · No supported logcat format could be detected in this file.", tab.Status);
            Assert.Equal("No supported logcat format could be detected in this file.", tab.FailureReason);
            Assert.Equal("Try a format override.", tab.FailureRemedy);
            Assert.NotNull(tab.CaptureHealthWarning);
            Assert.Contains("Try a format override.", tab.CaptureHealthWarning, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// A phone has no format override, so telling a phone user to select one named a control
    /// that does not exist there (finding 10).
    /// </summary>
    [Fact]
    public void OnlyAnUndetectableFormatGetsAnImportRemedy()
    {
        Assert.NotNull(WorkspaceViewModel.ImportRemedy(new ImportSourceException(
            ImportFailureReason.UndetectableFormat,
            "undetectable")));
        Assert.Null(WorkspaceViewModel.ImportRemedy(new InvalidDataException("damaged session")));
        Assert.Null(WorkspaceViewModel.ImportRemedy(new IOException("disk")));
    }

    private static string BuildLog(int lines, TimeSpan span)
    {
        var builder = new StringBuilder(lines * 96);
        var stepUs = Math.Max(1_000, (long)(span.TotalMilliseconds * 1_000 / Math.Max(1, lines)));
        for (var index = 0; index < lines; index++)
        {
            var instant = TimeSpan.FromMicroseconds(index * stepUs);
            builder.Append("01-01 ")
                .Append(instant.Hours.ToString("00", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(instant.Minutes.ToString("00", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(instant.Seconds.ToString("00", CultureInfo.InvariantCulture))
                .Append('.')
                .Append((instant.Milliseconds * 1_000 + instant.Microseconds)
                    .ToString("000000", CultureInfo.InvariantCulture))
                .Append("   100   101 I Worker         : request ")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" completed");
        }

        return builder.ToString();
    }
}
