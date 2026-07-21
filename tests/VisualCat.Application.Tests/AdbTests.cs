using System.Text;
using VisualCat.Application.Ports;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;

namespace VisualCat.Application.Tests;

public sealed class AdbTests
{
    [Fact]
    public void DeviceParserPreservesActionableStatesAndProperties()
    {
        const string output =
            "List of devices attached\n" +
            "ABC device product:panther model:Pixel_7 transport_id:1\n" +
            "DEF unauthorized usb:2-1\n" +
            "GHI offline\n" +
            "JKL recovery\n";

        var devices = AdbDeviceParser.Parse(output);

        Assert.Collection(
            devices,
            device =>
            {
                Assert.Equal(AdbDeviceState.Device, device.State);
                Assert.Equal("Pixel_7", device.Model);
                Assert.Equal("1", device.TransportId);
            },
            device => Assert.Equal(AdbDeviceState.Unauthorized, device.State),
            device => Assert.Equal(AdbDeviceState.Offline, device.State),
            device => Assert.Equal(AdbDeviceState.Unknown, device.State));
    }

    [Fact]
    public async Task AdbSourceNegotiatesBestFormatAndHonorsByteCap()
    {
        var process = new FakeProcess("abcdefghij");
        var client = new FakeClient(process);
        await using var source = new AdbLogSource(client, "ABC", ["main", "crash"], maximumCaptureBytes: 5);
        var chunks = new List<SourceChunk>();

        await foreach (var chunk in source.ReadAsync(
                           new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                           CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var resultChunk = Assert.Single(chunks);
        Assert.Equal(0, resultChunk.RawOffset);
        Assert.Equal("abcde", Encoding.UTF8.GetString(resultChunk.Bytes.Span));
        Assert.True(process.Stopped);
        Assert.Contains("threadtime,year,UTC,usec", client.StartArguments);
        Assert.Contains("main,crash", client.StartArguments);
        Assert.Equal("5", source.Metadata.Properties!["maximumCaptureBytes"]);

        // The richest candidate succeeded, so no degradation was attempted.
        Assert.Equal(["threadtime,year,UTC,usec"], client.ProbedFormats);
    }

    [Theory]
    [InlineData(new[] { "year" }, "threadtime,year")]
    [InlineData(new[] { "year", "usec" }, "threadtime,year,usec")]
    [InlineData(new string[0], "threadtime")]
    public async Task AdbSourceDegradesOneModifierAtATimeOnOlderDevices(
        string[] supported,
        string expectedFormat)
    {
        // Devices reject an unknown modifier outright, so capture must fall back rather
        // than assume support or give up (§13.6).
        var process = new FakeProcess("x");
        var client = new FakeClient(process, supported.Length == 0 ? ["none"] : supported);
        await using var source = new AdbLogSource(client, "ABC", ["main"]);

        await foreach (var _ in source.ReadAsync(
                           new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                           CancellationToken.None))
        {
        }

        Assert.Contains(expectedFormat, client.StartArguments);
        Assert.Equal(AdbLogSource.FormatCandidates[0], client.ProbedFormats[0]);
    }

    [Fact]
    public async Task CaptureAgainstAnAbsentDeviceFailsWithAnActionableMessage()
    {
        // `adb -s <unknown> logcat` blocks waiting for the device instead of failing, so
        // without an up-front check the capture runs until its own stop fires and then
        // reports an empty session as a success (§13.5, §18.1).
        var client = new FakeClient(new FakeProcess("x"));
        client.Devices.Clear();
        client.Devices.Add(new AdbDevice("OTHER", AdbDeviceState.Device, null, null, "1", new Dictionary<string, string>()));
        await using var source = new AdbLogSource(client, "MISSING", ["main"]);

        var failure = await Assert.ThrowsAsync<AdbCaptureUnavailableException>(async () =>
        {
            await foreach (var _ in source.ReadAsync(
                               new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains("MISSING", failure.Message, StringComparison.Ordinal);
        Assert.Contains("OTHER", failure.Message, StringComparison.Ordinal);
        Assert.Empty(client.ProbedFormats);
    }

    [Theory]
    [InlineData(AdbDeviceState.Unauthorized, "authorize")]
    [InlineData(AdbDeviceState.Offline, "offline")]
    public async Task CaptureReportsUnusableDeviceStatesBeforeSpawningLogcat(
        AdbDeviceState state,
        string expectedHint)
    {
        var client = new FakeClient(new FakeProcess("x"));
        client.Devices.Clear();
        client.Devices.Add(new AdbDevice("ABC", state, null, null, "1", new Dictionary<string, string>()));
        await using var source = new AdbLogSource(client, "ABC", ["main"]);

        var failure = await Assert.ThrowsAsync<AdbCaptureUnavailableException>(async () =>
        {
            await foreach (var _ in source.ReadAsync(
                               new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains(expectedHint, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.ProbedFormats);
    }

    [Fact]
    public async Task CaptureFailsWhenTheDeviceRejectsEveryFormatRatherThanCapturingNothing()
    {
        // The plainest candidate is universally supported, so every probe failing means
        // the buffer selection is unusable — not that the device is merely old.
        var client = new FakeClient(new FakeProcess("x"));
        await using var source = new AdbLogSource(client, "ABC", ["nosuchbuffer"]);

        var failure = await Assert.ThrowsAsync<AdbCaptureUnavailableException>(async () =>
        {
            await foreach (var _ in source.ReadAsync(
                               new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains("nosuchbuffer", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessParserHandlesModernAndFallbackPsLayouts()
    {
        var observed = new InstantUs(123);
        var modern = AdbProcessParser.Parse(
            "PID NAME\n1 init\n234 com.example.app\n234 com.example.app\n",
            observed);
        var fallback = AdbProcessParser.Parse(
            "u0_a12 345 1 0 0 0 S com.example.worker\n",
            observed);

        Assert.Collection(
            modern,
            process =>
            {
                Assert.Equal(1, process.Pid);
                Assert.Equal("init", process.Name);
            },
            process =>
            {
                Assert.Equal(234, process.Pid);
                Assert.Equal("com.example.app", process.Name);
            });
        var worker = Assert.Single(fallback);
        Assert.Equal(345, worker.Pid);
        Assert.Equal("com.example.worker", worker.Name);
    }

    /// <summary>
    /// Models a device that accepts a <c>-v</c> probe only when it understands every
    /// requested modifier, which is how logcat actually reports capability: it exits
    /// non-zero on an unknown modifier and has no machine-readable capability list.
    /// </summary>
    private sealed class FakeClient(FakeProcess process, params string[] supportedModifiers) : IAdbClient
    {
        private readonly HashSet<string> _supported =
            new(supportedModifiers.Length == 0 ? ["year", "UTC", "usec"] : supportedModifiers, StringComparer.OrdinalIgnoreCase);

        public string ExecutablePath => "fake-adb";
        public IReadOnlyList<string> StartArguments { get; private set; } = [];
        public List<string> ProbedFormats { get; } = [];

        public HashSet<string> SupportedBuffers { get; } =
            new(["main", "system", "crash", "events", "radio", "kernel"], StringComparer.Ordinal);

        public Task<AdbCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var formatIndex = arguments.ToList().IndexOf("-v");
            if (formatIndex < 0 || formatIndex + 1 >= arguments.Count)
            {
                return Task.FromResult(new AdbCommandResult(1, string.Empty, "unexpected command"));
            }

            var format = arguments[formatIndex + 1];
            ProbedFormats.Add(format);

            // Real logcat rejects an unknown buffer for every format, including the
            // plainest one, which is what distinguishes an unusable selection from a
            // merely old device.
            var bufferIndex = arguments.ToList().IndexOf("-b");
            if (bufferIndex >= 0 && bufferIndex + 1 < arguments.Count)
            {
                var unknown = arguments[bufferIndex + 1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(buffer => !SupportedBuffers.Contains(buffer));
                if (unknown is not null)
                {
                    return Task.FromResult(new AdbCommandResult(1, string.Empty, $"logcat: Unknown -b buffer '{unknown}'."));
                }
            }

            var modifiers = format.Split(',', StringSplitOptions.RemoveEmptyEntries).Skip(1);
            return Task.FromResult(modifiers.All(_supported.Contains)
                ? new AdbCommandResult(0, string.Empty, string.Empty)
                : new AdbCommandResult(1, string.Empty, $"logcat: Invalid -v '{format}'."));
        }

        /// <summary>Devices this fake ADB reports; defaults to one healthy "ABC".</summary>
        public List<AdbDevice> Devices { get; } =
            [new("ABC", AdbDeviceState.Device, "model", "product", "1", new Dictionary<string, string>())];

        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AdbDevice>>(Devices);
        }

        public IAdbProcess StartProcess(IReadOnlyList<string> arguments)
        {
            StartArguments = arguments.ToArray();
            return process;
        }
    }

    private sealed class FakeProcess(string output) : IAdbProcess
    {
        private readonly MemoryStream _output = new(Encoding.UTF8.GetBytes(output));
        private readonly StringReader _error = new(string.Empty);

        public Stream StandardOutput => _output;
        public TextReader StandardError => _error;
        public int ExitCode => 0;
        public bool HasExited { get; private set; }
        public bool Stopped { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HasExited = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stopped = true;
            HasExited = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _output.Dispose();
            _error.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
