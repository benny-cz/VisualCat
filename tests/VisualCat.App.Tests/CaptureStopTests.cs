using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Headless.XUnit;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.App.Tests;

/// <summary>
/// What Stop capture says and does between the press and a session on disk.
/// </summary>
/// <remarks>
/// Written against a four-hour, 543,000-entry capture on a phone, where the press appeared to
/// do nothing: the button sprang back from "Stopping…" to "Stop capture" within a second, the
/// status line went on reading "Capturing · 543,767 lines received · no source lines for 7m",
/// and the session — already complete on disk — stayed that way for as long as the workspace
/// took to reopen it. Nothing about that told the reader whether the stop had registered,
/// whether their capture was safe, or whether to press again.
/// </remarks>
public sealed class CaptureStopTests
{
    /// <summary>
    /// The regression itself. Two things describe a running capture and both keep firing
    /// after the source has been told to stop: the pipeline's progress reports, which
    /// continue while the read-ahead drains, and the one-second heartbeat, which speaks up
    /// whenever the source is quiet — and a stopped source is quiet by definition.
    /// </summary>
    [AvaloniaFact]
    public void AStopSurvivesTheReportsThatUsedToUndoIt()
    {
        var tab = new SessionTabViewModel("capture", Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", "unused"));
        tab.IsLiveCaptureActive = true;
        tab.ReportActivity(SessionActivity.Capturing, "Capturing · 100 lines received");

        tab.BeginStop();
        Assert.True(tab.IsStopping);
        Assert.Equal(SessionActivity.Stopping, tab.Activity);

        // The progress reporter's path: a batch committed after the stop.
        tab.ReportActivity(SessionActivity.Capturing, "Capturing · 120 lines received");
        Assert.Equal(SessionActivity.Stopping, tab.Activity);
        Assert.StartsWith("Stopping · ", tab.Status, StringComparison.Ordinal);

        // The heartbeat's path, and the loader's: a snapshot opened mid-stop used to report
        // Ready, which would have taken the Stop button away while the stop was still under
        // way and left the capture's own ending with nothing to say.
        tab.ReportActivity(SessionActivity.Ready, "Ready · 120 entries");
        Assert.Equal(SessionActivity.Stopping, tab.Activity);

        // Only an ending ends it.
        tab.ReportActivity(SessionActivity.Stopped, "Stopped · 120 entries kept");
        Assert.False(tab.IsStopping);
        Assert.Equal(SessionActivity.Stopped, tab.Activity);

        // And once it has ended, the session describes itself freely again — reopening this
        // same session from disk must not be silenced by a stop that is long over.
        tab.ReportActivity(SessionActivity.Ready, "Ready · 120 entries");
        Assert.Equal(SessionActivity.Ready, tab.Activity);
    }

    /// <summary>
    /// A failure during the stop still reaches the reader: it is the one thing that outranks
    /// the stop's own account of itself.
    /// </summary>
    [AvaloniaFact]
    public void AFailureDuringAStopIsStillReported()
    {
        var tab = new SessionTabViewModel("capture", Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", "unused"));
        tab.BeginStop();
        tab.ReportFailure("the device disconnected", remedy: null);

        Assert.Equal(SessionActivity.Failed, tab.Activity);
        Assert.False(tab.IsStopping);
    }

    /// <summary>
    /// Each stage of the ending is a different answer to "what is it doing?", and the reader
    /// waiting on a long stop is owed the one that applies.
    /// </summary>
    [AvaloniaFact]
    public void AStopNamesTheWorkItIsWaitingOn()
    {
        var tab = new SessionTabViewModel("capture", Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", "unused"));
        tab.BeginStop();

        // Still writing out lines that were read before the stop.
        tab.ReportStopProgress(Progress(IngestStage.Committing, read: 543_767, committed: 500_000));
        Assert.Contains("43,767 lines left to save", tab.Status, StringComparison.Ordinal);

        // Everything read is committed; the store is merging what it wrote.
        tab.ReportStopProgress(Progress(IngestStage.Compacting, read: 543_767, committed: 543_767));
        Assert.Contains("compacting the session", tab.Status, StringComparison.Ordinal);

        tab.ReportStopProgress(Progress(IngestStage.Finalizing, read: 543_767, committed: 543_767));
        Assert.Contains("writing the session index", tab.Status, StringComparison.Ordinal);

        // The phase the view model drives itself, once the pipeline has said all it will.
        tab.ReportStopPhase("capture saved · opening the session");
        Assert.Contains("capture saved", tab.Status, StringComparison.Ordinal);

        // Every one of them is a stop, and every one of them leads with the elapsed clock —
        // on a phone the status bar is one truncated line, so the token that answers "is this
        // stuck?" has to come before anything that can be clipped away.
        Assert.StartsWith("Stopping · ", tab.Status, StringComparison.Ordinal);
        Assert.Matches(@"^Stopping · \d+s · ", tab.Status);
    }

    /// <summary>
    /// Nothing may describe the session as live once the pipeline has handed it back, and the
    /// reader is told what became of what they recorded. Both used to be missed: a capture the
    /// reader stopped fell through every branch that reports an ending, on the reasoning that
    /// they were looking at the button they had just pressed — but the button, the status line
    /// and Follow all read off that state, so the workspace went on offering to stop a capture
    /// that had finished minutes earlier.
    /// </summary>
    [AvaloniaFact]
    public async Task AStoppedCaptureEndsSayingWhatItKept()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            await using var workspace = new WorkspaceViewModel();
            SessionTabViewModel? opened = null;
            workspace.TabAdded += (_, tab) => opened = tab;
            await using var device = new StreamingLogSource(BuildLog(64, TimeSpan.FromSeconds(64)));
            var capture = workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);

            // The source goes on holding the capture open after this, exactly as logcat does.
            await device.WaitForFirstReadAsync();
            var tab = Assert.IsType<SessionTabViewModel>(opened);

            Assert.True(await workspace.StopAsync(tab));
            var captured = await capture;

            Assert.Same(tab, captured);
            Assert.Equal(SessionActivity.Stopped, captured.Activity);
            Assert.Contains("entries kept", captured.Status, StringComparison.Ordinal);
            Assert.False(captured.IsStopping);
            Assert.False(captured.IsLiveCaptureActive);

            // What the workspace reads to decide whether to offer Follow and Stop capture.
            Assert.False(captured.IsLiveSourceAttached);
            Assert.False(captured.IsSessionWorkInFlight);

            // Stopping a capture that has already finished is not an error and not a second
            // stop; it simply has nothing left to end.
            Assert.False(await workspace.StopAsync(captured));

            await workspace.CloseAsync(captured);
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
    /// The press is answered before any of the work it starts: draining, compacting and
    /// reopening take as long as the capture was large, and until one of them reports the
    /// reader has only the button to go on.
    /// </summary>
    [AvaloniaFact]
    public async Task AStopIsAcknowledgedBeforeItsWorkBegins()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            await using var workspace = new WorkspaceViewModel();
            SessionTabViewModel? opened = null;
            workspace.TabAdded += (_, added) => opened = added;
            await using var device = new StreamingLogSource(BuildLog(64, TimeSpan.FromSeconds(64)));
            var capture = workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);
            await device.WaitForFirstReadAsync();
            var tab = Assert.IsType<SessionTabViewModel>(opened);

            // Not awaited: this is the state the reader sees while the stop is still running.
            var stop = workspace.StopAsync(tab);
            Assert.True(tab.IsStopping);
            Assert.Equal(SessionActivity.Stopping, tab.Activity);
            Assert.StartsWith("Stopping · ", tab.Status, StringComparison.Ordinal);

            // The controls the workspace derives from that state: the capture band is still
            // there to be spoken to, and the button that is already working is not offered
            // again.
            Assert.True(tab.IsLiveSourceAttached);

            await stop;
            await capture;
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
    /// Android's notification and API-35 service timeout must use the same draining stop as
    /// the in-app button. Otherwise the foreground service can disappear while the source
    /// and an unsealed temporary session continue running behind it.
    /// </summary>
    [AvaloniaFact]
    public async Task APlatformTimeLimitStopsSavesAndReleasesItsBackgroundLease()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        var previous = PlatformSourceRegistry.BeginLiveCaptureBackgroundExecution;
        var lease = new TrackingLease();
        Action<PlatformLiveCaptureStopReason>? requestStop = null;
        string? summary = null;
        try
        {
            PlatformSourceRegistry.BeginLiveCaptureBackgroundExecution = (value, stop) =>
            {
                summary = value;
                requestStop = stop;
                return lease;
            };

            await using var workspace = new WorkspaceViewModel();
            await using var device = new StreamingLogSource(BuildLog(64, TimeSpan.FromSeconds(64)));
            var capture = workspace.CaptureAsync(device, null, TestContext.Current.CancellationToken);
            await device.WaitForFirstReadAsync();

            Assert.Equal("On-device full-device logcat", summary);
            Assert.NotNull(requestStop);
            requestStop(PlatformLiveCaptureStopReason.SystemTimeLimit);

            var captured = await capture;
            Assert.Equal(SessionActivity.Stopped, captured.Activity);
            Assert.Contains("Android's six-hour background limit", captured.Status, StringComparison.Ordinal);
            Assert.Contains("entries kept", captured.Status, StringComparison.Ordinal);
            Assert.True(lease.Disposed);

            await workspace.CloseAsync(captured);
        }
        finally
        {
            PlatformSourceRegistry.BeginLiveCaptureBackgroundExecution = previous;
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProgressSnapshot Progress(IngestStage stage, long read, long committed) =>
        new(
            Guid.Empty,
            1,
            stage,
            BytesRead: read * 96,
            BytesCommitted: committed * 96,
            LinesRead: read,
            LinesCommitted: committed,
            TotalBytes: null,
            Counters: new SessionCounters(),
            ThroughputLinesPerSecond: 0,
            Elapsed: TimeSpan.FromSeconds(1),
            EstimatedRemaining: null,
            IsIndeterminate: true,
            IsCancellable: true,
            SnapshotGeneration: 1);

    /// <summary>
    /// A source that delivers its lines and then stays open, which is what makes a capture
    /// live: it ends when it is told to, not when it runs out of bytes.
    /// </summary>
    private sealed class StreamingLogSource(string log) : ILogSource
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(log);
        private readonly TaskCompletionSource _firstRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _stop = new();

        public SourceMetadata Metadata { get; } = new(
            SourceKind.Android,
            "on-device",
            "On-device full-device logcat",
            null,
            // A live source has no length: it is not finite and cannot be replayed, and the
            // capture path branches on all three.
            null,
            DateTimeOffset.UtcNow,
            IsFinite: false,
            IsReplayable: false,
            Properties: new Dictionary<string, string> { [SourceMetadata.LogTimeZoneProperty] = "UTC" });

        /// <summary>Completes once the capture is streaming and can be stopped.</summary>
        public Task WaitForFirstReadAsync() => _firstRead.Task;

        public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(
            int maximumUsefulLines,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = new List<ReadOnlyMemory<byte>>();
            var start = 0;
            for (var index = 0; index < _bytes.Length && lines.Count < maximumUsefulLines; index++)
            {
                if (_bytes[index] != (byte)'\n')
                {
                    continue;
                }

                lines.Add(_bytes.AsMemory(start, index - start + 1));
                start = index + 1;
            }

            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<byte>>>(lines);
        }

        public async IAsyncEnumerable<SourceChunk> ReadAsync(
            SourceReadContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = context;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            yield return new SourceChunk(0, _bytes);
            _firstRead.TrySetResult();

            // Held open until the capture is stopped, which is the whole point: the stop has
            // to be what ends this, so that what the workspace does between the press and the
            // finished session is what the test is looking at.
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            _stop.Cancel();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _firstRead.TrySetResult();
            _stop.Cancel();
            _stop.Dispose();
            return ValueTask.CompletedTask;
        }
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

    private sealed class TrackingLease : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
