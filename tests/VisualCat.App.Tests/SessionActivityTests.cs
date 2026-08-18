using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;
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
        Assert.NotNull(WorkspaceViewModel.ImportRemedy(new InvalidDataException("undetectable")));
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
