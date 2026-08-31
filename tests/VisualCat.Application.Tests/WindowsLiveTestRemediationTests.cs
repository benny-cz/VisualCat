using System.Globalization;
using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;

namespace VisualCat.Application.Tests;

/// <summary>
/// The source-layer half of the Windows live-test remediation
/// (<c>docs/WINDOWS-LIVE-TEST-REPORT.md</c> §3). One region per finding, each test named
/// for the behaviour the run found missing rather than for the method it exercises.
/// </summary>
public sealed class WindowsLiveTestRemediationTests
{
    /// <summary>Compresses the transport's two-minute clocks into a test's patience.</summary>
    private static readonly AdbLogSource.AdbCaptureTiming FastTiming = new(
        ProbeTimeout: TimeSpan.FromSeconds(2),
        DiscoveryTimeout: TimeSpan.FromSeconds(2),
        MetadataTimeout: TimeSpan.FromSeconds(2),
        SilenceProbeInterval: TimeSpan.FromMilliseconds(25),
        SilenceProbeThreshold: TimeSpan.FromMilliseconds(50),
        DeviceReturnTimeout: TimeSpan.FromMilliseconds(300),
        DevicePollInterval: TimeSpan.FromMilliseconds(25));

    private static SourceReadContext Context() =>
        new(Guid.NewGuid(), 1, Path.GetTempPath());

    // ---------------------------------------------------------------- F-02

    [Fact]
    public async Task AFinishedCaptureCanStateWhatItWasAskedFor()
    {
        // The manifest recorded only the buffers that produced a record, so a capture that
        // selected `crash` and saw nothing from it was indistinguishable from one that never
        // asked for it (finding F-02).
        var client = new ScriptedAdbClient
        {
            AdbVersion = "Android Debug Bridge version 1.0.41",
            Properties = { ["ro.build.fingerprint"] = "samsung/r9qxeea/r9q:16/BP2A.250605.031.A3/user" },
        };
        await using var source = new AdbLogSource(
            client,
            "ABC",
            ["main", "system", "crash"],
            maximumCaptureBytes: 1024,
            preRoll: TimeSpan.FromSeconds(30),
            durationLimit: TimeSpan.FromMinutes(5))
        { Timing = FastTiming };

        await source.PrepareAsync(TestContext.Current.CancellationToken);

        var capture = source.Metadata.Capture;
        Assert.NotNull(capture);
        Assert.Equal(["main", "system", "crash"], capture.RequestedBuffers);
        Assert.Equal(30, capture.PreRollSeconds);
        Assert.False(capture.IncludesBufferHistory);
        Assert.Equal(300, capture.DurationLimitSeconds);
        Assert.Equal(1024, capture.ByteLimit);
        Assert.Equal("threadtime,year,UTC,usec", capture.NegotiatedFormat);
        Assert.Equal("UTC", capture.LogTimeZoneId);
        Assert.Equal("Android Debug Bridge version 1.0.41", capture.AdbVersion);
        Assert.Equal("Galaxy", capture.DeviceModel);
        Assert.StartsWith("samsung/", capture.DeviceFingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureSettingsSurviveTheManifestAndReopenBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-adb-metadata-{Guid.NewGuid():N}.vcat");
        var client = new ScriptedAdbClient
        {
            Output = "2026-08-30 16:00:00.000000  100  101 I Test: one record\n",
            AdbVersion = "Android Debug Bridge version 1.0.41",
            Properties = { ["ro.build.fingerprint"] = "samsung/build" },
        };
        await using var source = new AdbLogSource(
            client,
            "ABC",
            ["main", "crash"],
            preRoll: TimeSpan.FromSeconds(30),
            durationLimit: TimeSpan.FromMinutes(5))
        { Timing = FastTiming };
        try
        {
            await source.PrepareAsync(TestContext.Current.CancellationToken);
            var result = await SessionCoordinator.ImportAsync(
                source,
                root,
                new IngestSettings(
                    LogcatFormat.ThreadTime,
                    "utf-8",
                    new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                    new TemplateSettings(),
                    PortableRaw: true),
                cancellationToken: TestContext.Current.CancellationToken);
            result.Snapshot.Dispose();

            using var reopened = await VisualCat.Core.Store.SessionStore.OpenAsync(
                root,
                TestContext.Current.CancellationToken);
            var capture = reopened.Descriptor.CaptureSettings;
            Assert.NotNull(capture);
            Assert.Equal(["main", "crash"], capture.RequestedBuffers);
            Assert.Equal(30, capture.PreRollSeconds);
            Assert.Equal(300, capture.DurationLimitSeconds);
            Assert.Equal("threadtime,year,UTC,usec", capture.NegotiatedFormat);
            Assert.Equal("UTC", capture.LogTimeZoneId);
            Assert.Equal("Android Debug Bridge version 1.0.41", capture.AdbVersion);
            Assert.Equal("samsung/build", capture.DeviceFingerprint);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ADeviceThatWillNotAnswerItsOwnPropertiesStillCaptures()
    {
        // Every field in the block is a courtesy. None of them may fail a capture.
        var client = new ScriptedAdbClient { FailShellCommands = true };
        await using var source = new AdbLogSource(client, "ABC", ["main"]) { Timing = FastTiming };

        await source.PrepareAsync(TestContext.Current.CancellationToken);

        Assert.Null(source.Metadata.Capture!.AdbVersion);
        Assert.Null(source.Metadata.Capture.DeviceFingerprint);
        Assert.Equal("threadtime,year,UTC,usec", source.Metadata.Capture.NegotiatedFormat);
    }

    // ---------------------------------------------------------------- F-06

    [Fact]
    public async Task PreRollZeroStartsFromNowRatherThanDumpingTheWholeRingBuffer()
    {
        // Zero used to omit -T, which makes logcat dump everything the ring still holds:
        // 320,832 entries and 44 MiB for a twenty-second capture on the device under test,
        // beside a label that reads as "no history" (finding F-06).
        var client = new ScriptedAdbClient();
        await using var source = new AdbLogSource(client, "ABC", ["main"]) { Timing = FastTiming };

        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        await DrainAsync(source);
        var after = DateTimeOffset.UtcNow.AddSeconds(2);

        var cursor = CursorOf(client.StartArguments);
        Assert.NotNull(cursor);
        var parsed = DateTimeOffset.Parse(cursor, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.InRange(parsed, before, after);
    }

    [Fact]
    public async Task TheWholeBufferIsAnExplicitChoiceAndOmitsTheCursor()
    {
        var client = new ScriptedAdbClient();
        await using var source = new AdbLogSource(client, "ABC", ["main"], includeBufferHistory: true)
        {
            Timing = FastTiming,
        };

        await DrainAsync(source);

        Assert.DoesNotContain("-T", client.StartArguments);
        Assert.True(source.Metadata.Capture!.IncludesBufferHistory);
    }

    [Fact]
    public async Task ThePreRollCursorIsWrittenInTheZoneTheNegotiatedFormatPrints()
    {
        // logcat matches -T against the timestamps it prints. A cursor pinned to UTC is
        // correct only on the top rung of the ladder; one rung down the device prints local
        // time and the capture silently starts at the wrong instant.
        var client = new ScriptedAdbClient
        {
            RejectedModifiers = { "UTC" },
            Properties = { ["persist.sys.timezone"] = "Asia/Tokyo" },
        };
        await using var source = new AdbLogSource(client, "ABC", ["main"], preRoll: TimeSpan.FromSeconds(60))
        {
            Timing = FastTiming,
        };

        await DrainAsync(source);

        Assert.Equal("Asia/Tokyo", source.Metadata.ResolveLogTimeZoneId());
        var cursor = CursorOf(client.StartArguments);
        Assert.NotNull(cursor);
        var written = DateTime.ParseExact(cursor, "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
        var expected = TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow.AddSeconds(-60),
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        Assert.True(
            Math.Abs((written - expected.DateTime).TotalSeconds) < 30,
            $"cursor {written:O} is not the device-local instant {expected.DateTime:O}");
    }

    // ---------------------------------------------------------------- F-07, F-08

    [Fact]
    public async Task TheByteCapAdmitsTheRecordThatCrossesItRatherThanCuttingIt()
    {
        // A-18 allows the cap to be exact "within one complete-record framing allowance".
        // A partial trailing line is unreadable and was booked as a parse defect (F-08).
        var client = new ScriptedAdbClient { Output = "aaaaaaaaaa\nbb\n" };
        await using var source = new AdbLogSource(client, "ABC", ["main"], maximumCaptureBytes: 5)
        {
            Timing = FastTiming,
        };

        var captured = await DrainAsync(source);

        Assert.Equal("aaaaaaaaaa\n", captured);
        Assert.DoesNotContain(captured.TrimEnd('\n'), "\n", StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheByteCapNamesItselfInsteadOfBlamingTheLogSource()
    {
        // "the log source ended this capture" points the reader at the phone: they check the
        // cable, the device and USB debugging, all healthy (finding F-07).
        var client = new ScriptedAdbClient { Output = new string('x', 4096) + "\n" };
        await using var source = new AdbLogSource(client, "ABC", ["main"], maximumCaptureBytes: 1024)
        {
            Timing = FastTiming,
        };

        await DrainAsync(source);

        var completion = source.Completion;
        Assert.NotNull(completion);
        Assert.Contains("1 KiB", completion.Summary, StringComparison.Ordinal);
        Assert.Contains("reached", completion.Summary, StringComparison.Ordinal);
        Assert.Contains("size limit", completion.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("log source ended", completion.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ACaptureThatEndsOnItsOwnWithoutALimitNamesNoReason()
    {
        var client = new ScriptedAdbClient { Output = "one\ntwo\n" };
        await using var source = new AdbLogSource(client, "ABC", ["main"]) { Timing = FastTiming };

        await DrainAsync(source);

        Assert.Null(source.Completion);
    }

    // ---------------------------------------------------------------- F-12

    [Fact]
    public async Task AConnectedButSilentStreamSaysSoBeforeAnyRecordArrives()
    {
        // A capture of an empty buffer is a healthy stream that will never carry a record.
        // The workspace sat on "Connecting to the device…" for as long as it ran (F-12a).
        var client = new ScriptedAdbClient { BlockForever = true };
        await using var source = new AdbLogSource(client, "ABC", ["crash"]) { Timing = FastTiming };
        var established = new TaskCompletionSource();
        source.StreamEstablished += () => established.TrySetResult();

        using var stop = new CancellationTokenSource();
        var reading = DrainAsync(source, stop.Token);

        // The device check and the format negotiation both finished before the child was
        // spawned, so the transport can say "connected" while the buffer stays empty.
        await established.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Fact]
    public async Task ASilentStreamWhoseDeviceHasVanishedIsNoticedRatherThanLeftRunning()
    {
        // `adb -s <gone> logcat` blocks rather than failing, so a device that goes away mid
        // capture left the workspace reporting the last rate it happened to see, forever
        // (finding F-12b). The transport is asked, and the answer ends the capture.
        var client = new ScriptedAdbClient { BlockForever = true };
        await using var source = new AdbLogSource(client, "ABC", ["main"]) { Timing = FastTiming };
        var statuses = new List<SourceConnectionStatus?>();
        source.ConnectionStatusChanged += status =>
        {
            lock (statuses)
            {
                statuses.Add(status);
            }
        };
        var established = new TaskCompletionSource();
        source.StreamEstablished += () => established.TrySetResult();

        var reading = Assert.ThrowsAsync<AdbCaptureUnavailableException>(() => DrainAsync(source));
        await established.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        client.Devices.Clear();

        var failure = await reading.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Contains("ABC", failure.Message, StringComparison.Ordinal);
        lock (statuses)
        {
            Assert.Contains(
                statuses,
                status => status is not null &&
                          status.Summary.Contains("has not responded", StringComparison.Ordinal));
        }

        var defects = source.GetDefects();
        Assert.True(defects.ReconnectGaps >= 1, "the break was not counted");
        Assert.True(defects.ReconnectGapMilliseconds > 0, "the break has no measured duration");
    }

    [Fact]
    public async Task ADeviceThatComesBackResumesInsteadOfFailing()
    {
        // A phone that reboots mid-capture is gone for the best part of a minute and then
        // returns with the same serial. Absence is tolerated for a bounded window, is
        // visible the whole time, and only then ends the capture — so the ordinary case
        // stays a reconnect rather than becoming a failure.
        var present = new AdbDevice("ABC", AdbDeviceState.Device, "Galaxy", "r9q", "1", new Dictionary<string, string>());
        var client = new ScriptedAdbClient
        {
            Outputs = { "first\n", "second\n" },
            ExitCodes = { 1, 0 },
        };

        // Call 1 is the preflight before the first spawn; the stream then ends non-zero and
        // the device is away for two polls before it answers again.
        client.DevicesByListCall = call => call is 2 or 3 ? [] : [present];
        await using var source = new AdbLogSource(client, "ABC", ["main"])
        {
            Timing = FastTiming with { DeviceReturnTimeout = TimeSpan.FromSeconds(10) },
        };
        var statuses = new List<SourceConnectionStatus?>();
        source.ConnectionStatusChanged += status =>
        {
            lock (statuses)
            {
                statuses.Add(status);
            }
        };

        var captured = await DrainAsync(source);

        Assert.Equal("firstsecond", captured.Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.True(client.ListDeviceCalls >= 4, $"the device was polled {client.ListDeviceCalls} times");
        lock (statuses)
        {
            Assert.Contains(
                statuses,
                status => status is not null &&
                          status.Summary.Contains("has not responded", StringComparison.Ordinal));
            Assert.Contains(statuses, static status => status is null);
        }

        var defects = source.GetDefects();
        Assert.Equal(1, defects.ReconnectGaps);
        Assert.True(defects.ReconnectGapMilliseconds > 0, "the break was counted with no measured duration");
    }

    [Fact]
    public async Task ARecoveredSilentStreamClosesItsGapWithoutWaitingForARecord()
    {
        // Transport recovery is the end of a transport gap. If the selected buffer is
        // legitimately empty, tying that clock to the next record makes the persisted
        // missing-time counter grow forever after a successful reconnect.
        var client = new ScriptedAdbClient
        {
            Outputs = { "first\n" },
            ExitCodes = { 1 },
            BlockFromSpawn = 1,
        };
        await using var source = new AdbLogSource(client, "ABC", ["crash"]) { Timing = FastTiming };
        var establishedCount = 0;
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.StreamEstablished += () =>
        {
            if (Interlocked.Increment(ref establishedCount) == 2)
            {
                recovered.TrySetResult();
            }
        };
        var statuses = new List<SourceConnectionStatus?>();
        source.ConnectionStatusChanged += status => statuses.Add(status);

        using var stop = new CancellationTokenSource();
        var reading = DrainAsync(source, stop.Token);
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var settled = source.GetDefects().ReconnectGapMilliseconds;
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(settled, source.GetDefects().ReconnectGapMilliseconds);
        Assert.Contains(statuses, static status => status is null);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    // ---------------------------------------------------------------- helpers

    private static string? CursorOf(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().IndexOf("-T");
        return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
    }

    private static async Task<string> DrainAsync(AdbLogSource source, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        await foreach (var chunk in source.ReadAsync(Context(), cancellationToken).ConfigureAwait(false))
        {
            text.Append(Encoding.UTF8.GetString(chunk.Bytes.Span));
        }

        return text.ToString();
    }

    /// <summary>
    /// An ADB whose device list, shell answers, and logcat stream are all scriptable, so a
    /// transport condition the live run reached with a phone can be reached here in
    /// milliseconds.
    /// </summary>
    private sealed class ScriptedAdbClient : IAdbClient
    {
        private int _listCalls;
        private int _spawns;

        public string ExecutablePath => "fake-adb";

        public List<AdbDevice> Devices { get; } =
            [new("ABC", AdbDeviceState.Device, "Galaxy", "r9q", "1", new Dictionary<string, string>())];

        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RejectedModifiers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? AdbVersion { get; set; }

        public bool FailShellCommands { get; set; }

        public string Output { get; set; } = string.Empty;

        public int ExitCode { get; set; }

        /// <summary>One entry per spawned logcat child, for the reconnect paths.</summary>
        public List<string> Outputs { get; } = [];

        public List<int> ExitCodes { get; } = [];

        /// <summary>What `adb devices` answers on its nth call, when a test scripts it.</summary>
        public Func<int, IReadOnlyList<AdbDevice>>? DevicesByListCall { get; set; }

        public bool BlockForever { get; set; }

        /// <summary>Zero-based spawn at and after which the stream remains healthy and silent.</summary>
        public int? BlockFromSpawn { get; set; }

        public IReadOnlyList<string> StartArguments { get; private set; } = [];

        public int ListDeviceCalls => Volatile.Read(ref _listCalls);

        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments.Count == 1 && arguments[0] == "version")
            {
                return Task.FromResult(AdbVersion is null
                    ? new AdbCommandResult(1, string.Empty, "no version")
                    : new AdbCommandResult(0, AdbVersion + Environment.NewLine, string.Empty));
            }

            if (arguments.Contains("getprop"))
            {
                var name = arguments[^1];
                return Task.FromResult(!FailShellCommands && Properties.TryGetValue(name, out var value)
                    ? new AdbCommandResult(0, value + Environment.NewLine, string.Empty)
                    : new AdbCommandResult(1, string.Empty, "unknown property"));
            }

            var formatIndex = arguments.ToList().IndexOf("-v");
            if (formatIndex < 0 || formatIndex + 1 >= arguments.Count)
            {
                return Task.FromResult(new AdbCommandResult(1, string.Empty, "unexpected command"));
            }

            var format = arguments[formatIndex + 1];
            var rejected = format
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Any(RejectedModifiers.Contains);
            return Task.FromResult(rejected
                ? new AdbCommandResult(1, string.Empty, $"logcat: Invalid -v '{format}'.")
                : new AdbCommandResult(0, string.Empty, string.Empty));
        }

        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Interlocked.Increment(ref _listCalls);
            return Task.FromResult(DevicesByListCall is { } script
                ? script(count)
                : Devices.ToArray());
        }

        public IAdbProcess StartProcess(IReadOnlyList<string> arguments)
        {
            StartArguments = arguments.ToArray();
            var spawn = _spawns++;
            if (BlockForever || BlockFromSpawn is { } blockFrom && spawn >= blockFrom)
            {
                return new BlockingProcess();
            }

            return Outputs.Count == 0
                ? new ScriptedProcess(Output, ExitCode)
                : new ScriptedProcess(
                    Outputs[Math.Min(spawn, Outputs.Count - 1)],
                    ExitCodes.Count == 0 ? 0 : ExitCodes[Math.Min(spawn, ExitCodes.Count - 1)]);
        }
    }

    private sealed class ScriptedProcess(string output, int exitCode) : IAdbProcess
    {
        private readonly MemoryStream _output = new(Encoding.UTF8.GetBytes(output));
        private readonly StringReader _error = new(string.Empty);

        public Stream StandardOutput => _output;
        public TextReader StandardError => _error;
        public int ExitCode { get; private set; } = exitCode;
        public bool HasExited { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            HasExited = true;
            ExitCode = 0;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _output.Dispose();
            _error.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// The child a vanished device leaves behind: connected, silent, and never exiting. This
    /// is what made a rebooted phone go on reading "Capturing · 49/s" for as long as anyone
    /// left the window open.
    /// </summary>
    private sealed class BlockingProcess : IAdbProcess
    {
        private readonly BlockingStream _output = new();
        private readonly StringReader _error = new(string.Empty);

        public Stream StandardOutput => _output;
        public TextReader StandardError => _error;
        public int ExitCode => 137;
        public bool HasExited { get; private set; }

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            while (!HasExited)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            HasExited = true;
            _output.Release();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            HasExited = true;
            _output.Release();
            _error.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class BlockingStream : Stream
        {
            private readonly TaskCompletionSource _released =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public void Release() => _released.TrySetResult();

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                await _released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                _released.TrySetResult();
                base.Dispose(disposing);
            }
        }
    }
}
