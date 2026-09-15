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
        // The cap stops at the last complete record at or below it: a byte-exact cut left
        // raw.log ending mid-line, which no parser can read back and which the manifest
        // then booked as a rejected candidate (finding F-08).
        var process = new FakeProcess("aaaa\nbbbb\ncccc\n");
        var client = new FakeClient(process);
        await using var source = new AdbLogSource(client, "ABC", ["main", "crash"], maximumCaptureBytes: 6);
        var chunks = new List<SourceChunk>();

        await foreach (var chunk in source.ReadAsync(
                           new SourceReadContext(Guid.NewGuid(), 1, Path.GetTempPath()),
                           CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var resultChunk = Assert.Single(chunks);
        Assert.Equal(0, resultChunk.RawOffset);
        Assert.Equal("aaaa\n", Encoding.UTF8.GetString(resultChunk.Bytes.Span));
        Assert.True(process.Stopped);
        Assert.Contains("threadtime,year,UTC,usec", client.StartArguments);
        Assert.Contains("main,crash", client.StartArguments);
        Assert.Equal("6", source.Metadata.Properties!["maximumCaptureBytes"]);

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

    /// <summary>
    /// F-10 — a configured ADB path is a decision, not a hint.
    /// </summary>
    /// <remarks>
    /// A path that was wrong used to be dropped by <c>File.Exists</c> and the search continued to
    /// <c>ANDROID_SDK_ROOT</c> and <c>PATH</c>, so `--adb /tmp` and `--adb /tmp/not-here` both
    /// exited 0 and captured from whatever <c>adb</c> happened to be installed. On a machine with
    /// several — Android Studio's, Homebrew's, a vendored one, the normal state of an Android
    /// developer's laptop — the operator had no way to learn which produced a capture.
    /// </remarks>
    [Fact]
    public void AnExplicitAdbPathIsRefusedByNameRatherThanReplaced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-adb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var missing = Path.Combine(root, "definitely-not-here");
            var directory = Path.Combine(root, "a-directory");
            Directory.CreateDirectory(directory);

            var forMissing = AdbLocator.Resolve(missing);
            Assert.False(forMissing.Found);
            Assert.True(forMissing.PinnedPathRejected);
            Assert.Contains(missing, forMissing.Problem!, StringComparison.Ordinal);
            Assert.Contains("does not exist", forMissing.Problem!, StringComparison.OrdinalIgnoreCase);

            var forDirectory = AdbLocator.Resolve(directory);
            Assert.False(forDirectory.Found);
            Assert.True(forDirectory.PinnedPathRejected);
            Assert.Contains("is a directory", forDirectory.Problem!, StringComparison.OrdinalIgnoreCase);

            // Both used to throw and both used to be refused as exceptions; the shell needs the
            // sentence without composing it out of one, which is what the repository's
            // "no view composes user text from a framework exception" guard requires.
            Assert.Throws<AdbLocatorException>(() => AdbLocator.Find(missing));
            Assert.Throws<AdbLocatorException>(() => AdbLocator.Find(directory));

            // A real file is accepted and returned absolute.
            var real = Path.Combine(root, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
            File.WriteAllText(real, "#!/bin/sh\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    real,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var accepted = AdbLocator.Resolve(real);
            Assert.True(accepted.Found);
            Assert.Equal(Path.GetFullPath(real), accepted.ExecutablePath);
            Assert.Null(accepted.Problem);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// F-10 — a file without an execute bit is refused with the <c>chmod</c> that fixes it,
    /// rather than reaching <c>Process.Start</c> and returning a .NET sentence that quotes the
    /// working directory.
    /// </summary>
    [Fact]
    public void AnAdbPathWithoutTheExecuteBitNamesTheChmodThatFixesIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"visualcat-adb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "adb");
            File.WriteAllText(path, "#!/bin/sh\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var resolution = AdbLocator.Resolve(path);
            Assert.False(resolution.Found);
            Assert.True(resolution.PinnedPathRejected);
            Assert.Contains($"chmod +x {path}", resolution.Problem!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// F-11, F-12 — the places the product looks are the places real machines put ADB, and the
    /// message that lists them is generated from the search itself.
    /// </summary>
    [Fact]
    public void TheSearchSummaryNamesEveryRouteThatWorks()
    {
        var summary = AdbLocator.SearchLocationSummary();
        var joined = string.Join(" | ", summary);

        Assert.Contains("--adb", joined, StringComparison.Ordinal);
        Assert.Contains("ANDROID_SDK_ROOT", joined, StringComparison.Ordinal);

        // ANDROID_HOME and the default SDK directory both work and neither was mentioned, so a
        // reader was told two of the four routes that exist (finding F-12).
        Assert.Contains("ANDROID_HOME", joined, StringComparison.Ordinal);
        Assert.Contains("on PATH", joined, StringComparison.Ordinal);

        if (OperatingSystem.IsMacOS())
        {
            // The Windows LOCALAPPDATA convention transplanted to macOS resolves to
            // ~/Library/Application Support/Android/Sdk, which exists on no ordinary Mac.
            // Android Studio installs to ~/Library/Android/sdk, and Homebrew links the
            // executable without laying down a platform-tools tree at all (finding F-11).
            Assert.Contains(Path.Combine("Library", "Android", "sdk"), joined, StringComparison.Ordinal);
            Assert.Contains("/opt/homebrew/bin/adb", joined, StringComparison.Ordinal);
            Assert.Contains("brew install", AdbLocator.InstallHint(), StringComparison.Ordinal);
        }
    }
}
