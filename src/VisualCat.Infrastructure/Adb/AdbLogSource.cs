using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Infrastructure.Adb;

public sealed class AdbLogSource :
    ILogSource,
    IProcessNameSource,
    ISourceDefectSource,
    ISourceConnectionStatusReporter,
    ISourceStreamStartReporter,
    ISourceCompletionReporter
{
    /// <summary>
    /// The transport's own clocks, in one value so a test can compress a two-minute wait
    /// into milliseconds without the product carrying test-only branches.
    /// </summary>
    /// <param name="ProbeTimeout">How long one format probe may take before the ladder degrades.</param>
    /// <param name="DiscoveryTimeout">How long `adb devices` may take before it counts as no answer.</param>
    /// <param name="MetadataTimeout">How long a courtesy question about the device may take.</param>
    /// <param name="SilenceProbeInterval">How often a silent stream is checked against its transport.</param>
    /// <param name="SilenceProbeThreshold">How long silence must last before the check is worth one call.</param>
    /// <param name="DeviceReturnTimeout">How long a vanished device is waited for before the capture fails.</param>
    /// <param name="DevicePollInterval">How often a vanished device is asked whether it is back.</param>
    internal sealed record AdbCaptureTiming(
        TimeSpan ProbeTimeout,
        TimeSpan DiscoveryTimeout,
        TimeSpan MetadataTimeout,
        TimeSpan SilenceProbeInterval,
        TimeSpan SilenceProbeThreshold,
        TimeSpan DeviceReturnTimeout,
        TimeSpan DevicePollInterval)
    {
        /// <summary>
        /// Shipped values. The device-return window is the one number with a story: a phone
        /// that reboots mid-capture is gone for the best part of a minute and then comes
        /// back with the same serial, so a capture that gave up on the first absent poll
        /// would be worse than the defect it replaces.
        /// </summary>
        public static readonly AdbCaptureTiming Default = new(
            ProbeTimeout: TimeSpan.FromSeconds(15),
            DiscoveryTimeout: TimeSpan.FromSeconds(10),
            MetadataTimeout: TimeSpan.FromSeconds(5),
            SilenceProbeInterval: TimeSpan.FromSeconds(5),
            SilenceProbeThreshold: TimeSpan.FromSeconds(20),
            DeviceReturnTimeout: TimeSpan.FromSeconds(120),
            DevicePollInterval: TimeSpan.FromSeconds(2));
    }

    private readonly IAdbClient _client;
    private readonly string _serial;
    private readonly string[] _buffers;
    private readonly long? _maximumCaptureBytes;
    private readonly TimeSpan _preRoll;
    private readonly bool _includeBufferHistory;
    private readonly TimeSpan? _durationLimit;
    private readonly DateTimeOffset _captureRequestedAtUtc;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<byte> _lineBuffer = new(512);
    private IAdbProcess? _activeProcess;
    private string? _resumeTimestamp;
    private string? _negotiatedFormat;
    private long _reconnectGaps;
    private long _reconnectGapMilliseconds;
    private long _gapStartedMs;
    private long _lastByteMs;

    public AdbLogSource(
        IAdbClient client,
        string serial,
        IReadOnlyList<string>? buffers = null,
        long? maximumCaptureBytes = null,
        TimeSpan? preRoll = null,
        bool includeBufferHistory = false,
        TimeSpan? durationLimit = null)
    {
        if (maximumCaptureBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCaptureBytes));
        }

        if (preRoll is { } preRollValue && (preRollValue < TimeSpan.Zero || preRollValue > TimeSpan.FromHours(1)))
        {
            throw new ArgumentOutOfRangeException(nameof(preRoll));
        }

        if (includeBufferHistory && preRoll is { } requestedPreRoll && requestedPreRoll > TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A finite pre-roll and the complete existing buffer are mutually exclusive.",
                nameof(includeBufferHistory));
        }

        if (durationLimit is { } requestedDuration && requestedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(durationLimit));
        }

        _client = client;
        _serial = serial;
        _buffers = buffers?.ToArray() ?? ["main", "system", "crash"];
        _maximumCaptureBytes = maximumCaptureBytes;
        _preRoll = preRoll ?? TimeSpan.Zero;
        _includeBufferHistory = includeBufferHistory;
        _durationLimit = durationLimit;
        // The cursor is a promise about when Start was requested, not when device checks and
        // format negotiation happened to finish. On a slow ADB server those can be seconds
        // apart; calculating later silently drops the first seconds the reader asked for.
        _captureRequestedAtUtc = DateTimeOffset.UtcNow;

        if (_buffers.Length == 0)
        {
            throw new ArgumentException("At least one logcat buffer must be selected.", nameof(buffers));
        }

        Metadata = new SourceMetadata(
            SourceKind.Adb,
            SourceMetadata.NameCaptureStartedNow($"ADB {_serial}"),
            $"ADB device {_serial}",
            null,
            null,
            DateTimeOffset.UtcNow,
            false,
            false,
            _serial,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["buffers"] = string.Join(',', _buffers),
                ["adb"] = _client.ExecutablePath,
                ["maximumCaptureBytes"] = _maximumCaptureBytes?.ToString(CultureInfo.InvariantCulture) ?? "unlimited",
                ["preRollSeconds"] = _preRoll.TotalSeconds.ToString(CultureInfo.InvariantCulture),
                ["includeBufferHistory"] = _includeBufferHistory ? "true" : "false",
            },
            new CaptureSettings(
                RequestedBuffers: _buffers,
                PreRollSeconds: _preRoll.TotalSeconds,
                IncludesBufferHistory: _includeBufferHistory,
                DurationLimitSeconds: _durationLimit?.TotalSeconds,
                ByteLimit: _maximumCaptureBytes));
    }

    public SourceMetadata Metadata { get; private set; }

    /// <summary>The clocks this source runs on; the shipped ones unless a test says otherwise.</summary>
    internal AdbCaptureTiming Timing { get; init; } = AdbCaptureTiming.Default;

    /// <inheritdoc />
    public event Action<SourceConnectionStatus?>? ConnectionStatusChanged;

    /// <inheritdoc />
    public event Action? StreamEstablished;

    /// <inheritdoc />
    public SourceCompletionReason? Completion { get; private set; }

    /// <summary>
    /// Settles the logcat format, and with it the zone the device will write timestamps in,
    /// before the session's timestamp policy has to be chosen.
    ///
    /// The ladder degrades one modifier at a time and may land on a format without the
    /// <c>UTC</c> modifier, where the device writes local time. The capture used to pin the
    /// policy to UTC regardless, so on any device that fell that far every timestamp was
    /// parsed as UTC and came out wrong by the device's offset, silently and permanently.
    /// Negotiating here lets the policy follow what the device actually agreed to.
    ///
    /// Everything the finished session will need to state its own configuration is collected
    /// here too — the rung that was agreed, the ADB build that ran the capture, and the
    /// device's model and fingerprint (finding F-02). None of it may fail a capture: a
    /// device that will not answer <c>getprop</c> still has a log worth reading.
    ///
    /// Idempotent, and the result is reused by <see cref="ReadAsync"/> rather than probed
    /// a second time.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (_negotiatedFormat is not null)
        {
            return;
        }

        var device = await EnsureDeviceIsCapturableAsync(cancellationToken).ConfigureAwait(false);
        var format = await NegotiateFormatAsync(cancellationToken).ConfigureAwait(false);
        var zone = format.Contains("UTC", StringComparison.Ordinal)
            ? "UTC"
            : await ReadDeviceTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var adbVersion = await ReadAdbVersionAsync(cancellationToken).ConfigureAwait(false);
        var fingerprint = await ReadDevicePropertyAsync("ro.build.fingerprint", cancellationToken).ConfigureAwait(false);
        _negotiatedFormat = format;
        Metadata = Metadata with
        {
            Properties = new Dictionary<string, string>(Metadata.Properties!, StringComparer.Ordinal)
            {
                ["format"] = format,
                [SourceMetadata.LogTimeZoneProperty] = zone,
            },
            Capture = (Metadata.Capture ?? new CaptureSettings()) with
            {
                NegotiatedFormat = format,
                LogTimeZoneId = zone,
                AdbVersion = adbVersion,
                DeviceModel = device.Model ?? device.Product,
                DeviceFingerprint = fingerprint,
            },
        };
    }

    /// <summary>
    /// The device's own zone, for the degraded formats that write local time. An id this
    /// machine cannot resolve is worse than no answer, so it falls back to the local zone —
    /// which a locally attached device usually shares anyway.
    /// </summary>
    private async Task<string> ReadDeviceTimeZoneAsync(CancellationToken cancellationToken)
    {
        var zone = await ReadDevicePropertyAsync("persist.sys.timezone", cancellationToken).ConfigureAwait(false);
        if (zone is { Length: > 0 })
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(zone);
                return zone;
            }
            catch (Exception exception) when (
                exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local.Id;
    }

    /// <summary>One device property, or null when the device declines to answer in time.</summary>
    private async Task<string?> ReadDevicePropertyAsync(string name, CancellationToken cancellationToken)
    {
        var result = await RunBoundedAsync(
            ["-s", _serial, "shell", "getprop", name],
            Timing.MetadataTimeout,
            cancellationToken).ConfigureAwait(false);
        var value = result?.StandardOutput.Trim();
        return result is { ExitCode: 0 } && value is { Length: > 0 } ? value : null;
    }

    /// <summary>The ADB build that ran the capture, as its own first line reports it.</summary>
    private async Task<string?> ReadAdbVersionAsync(CancellationToken cancellationToken)
    {
        var result = await RunBoundedAsync(["version"], Timing.MetadataTimeout, cancellationToken).ConfigureAwait(false);
        if (result is not { ExitCode: 0 })
        {
            return null;
        }

        var first = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    /// <summary>
    /// One ADB question with its own deadline, whose failure is an absent answer rather than
    /// an exception. Only the caller's own cancellation propagates.
    /// </summary>
    private async Task<AdbCommandResult?> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(timeout);
            return await _client.RunAsync(arguments, bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(int maximumUsefulLines, CancellationToken cancellationToken)
    {
        _ = maximumUsefulLines;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ReadOnlyMemory<byte>> probe =
        [
            "2026-01-01 00:00:00.000000  1  1 I VisualCat: live format probe\n"u8.ToArray(),
        ];
        return Task.FromResult(probe);
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await PrepareAsync(linked.Token).ConfigureAwait(false);
        var format = _negotiatedFormat!;
        var startCursor = BuildStartCursor();
        long offset = 0;
        var reconnectAttempt = 0;

        // Set when the byte cap fell inside a record: the rest of that record is taken and
        // the capture then stops, so nothing partial is ever written (finding F-08).
        var finishingRecord = false;
        Volatile.Write(ref _lastByteMs, Environment.TickCount64);
        while (!linked.IsCancellationRequested)
        {
            if (reconnectAttempt > 0)
            {
                // The presence check that guards the first spawn has to guard every later
                // one. `adb -s <gone> logcat` does not fail — it blocks waiting for the
                // device — so a re-spawn against a vanished phone looked like a successful
                // reconnect, never consumed the attempt budget, and left the workspace
                // reporting the last rate it happened to see (finding F-12b).
                await WaitForDeviceAsync(reconnectAttempt, linked.Token).ConfigureAwait(false);
            }

            // -D prints a divider on every buffer crossing, so a multi-buffer capture can
            // attribute each record to the buffer it actually came from rather than to
            // whichever one was announced last (finding F-12 of the Android run).
            var arguments = new List<string>
            {
                "-s", _serial, "logcat", "-b", string.Join(',', _buffers), "-D", "-v", format,
            };
            var cursor = reconnectAttempt > 0 ? _resumeTimestamp ?? startCursor : startCursor;
            if (cursor is not null)
            {
                arguments.Add("-T");
                arguments.Add(cursor);
            }

            await using var process = _client.StartProcess(arguments);
            _activeProcess = process;
            using var watchdog = new CancellationTokenSource();
            var silenceWatch = WatchForSilentTransportAsync(process, watchdog.Token, linked.Token);
            // A gap describes transport downtime, not time until the recovered device next
            // happens to log a record. An empty crash buffer can remain silent forever after
            // a perfectly healthy reconnect, so settle the clock when its stream exists.
            CloseReconnectGap();
            ReportStreamEstablished();
            try
            {
                var stderrTask = ReadBoundedErrorAsync(process.StandardError, linked.Token);
                var buffer = new byte[1024 * 1024];
                while (!linked.IsCancellationRequested)
                {
                    var read = await process.StandardOutput.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    Volatile.Write(ref _lastByteMs, Environment.TickCount64);
                    CloseReconnectGap();
                    var accepted = AcceptWithinCap(buffer.AsSpan(0, read), offset, ref finishingRecord, out var capReached);
                    if (accepted > 0)
                    {
                        var chunk = new byte[accepted];
                        Buffer.BlockCopy(buffer, 0, chunk, 0, accepted);
                        TrackResumeTimestamp(chunk);
                        yield return new SourceChunk(offset, chunk);
                        offset += accepted;
                        reconnectAttempt = 0;
                    }

                    if (capReached)
                    {
                        // The limit is the reader's own, so it says so rather than blaming
                        // the phone (finding F-07).
                        Completion = new SourceCompletionReason(
                            $"this capture reached its {DescribeBytes(_maximumCaptureBytes ?? offset)} limit",
                            $"The live capture reached the {DescribeBytes(_maximumCaptureBytes ?? offset)} size limit " +
                            "it was given and stopped itself. Raise or clear the size limit in Live ADB capture " +
                            "before starting again, or the next capture will stop at the same point.");
                        await StopAsync(CancellationToken.None).ConfigureAwait(false);
                        yield break;
                    }
                }

                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                var error = await stderrTask.ConfigureAwait(false);
                if (process.ExitCode == 0 || linked.IsCancellationRequested)
                {
                    yield break;
                }

                reconnectAttempt++;
                OpenReconnectGap();
                if (reconnectAttempt > 5)
                {
                    throw new IOException($"ADB logcat exited repeatedly with code {process.ExitCode}: {error}");
                }

                ReportConnectionStatus(new SourceConnectionStatus(
                    $"Reconnecting (attempt {reconnectAttempt})",
                    $"The logcat stream to {_serial} ended with code {process.ExitCode} and is being restarted. " +
                    "The capture resumes from the last record it received."));
                var delay = TimeSpan.FromMilliseconds(Math.Min(10_000, 250 * Math.Pow(2, reconnectAttempt - 1)));
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                await watchdog.CancelAsync().ConfigureAwait(false);
                try
                {
                    await silenceWatch.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                if (ReferenceEquals(_activeProcess, process))
                {
                    _activeProcess = null;
                }
            }
        }
    }

    /// <summary>
    /// How much of one read may be kept, and whether the cap has been reached.
    /// </summary>
    /// <remarks>
    /// The cap is a promise about a log, and a log is made of records. Applied to the byte
    /// stream it left <c>raw.log</c> ending mid-line — unreadable to any parser, and booked
    /// by the manifest as a rejected candidate, so the defect counters reported the reader's
    /// own limit as malformed input (finding F-08). Plan A-18 allows the cap to be exact
    /// "within one complete-record framing allowance", which is what is taken here: the last
    /// complete record at or below the cap, or, when a single record straddles it, that
    /// record whole.
    /// </remarks>
    private int AcceptWithinCap(ReadOnlySpan<byte> read, long offset, ref bool finishingRecord, out bool capReached)
    {
        capReached = false;
        if (finishingRecord)
        {
            var end = read.IndexOf((byte)'\n');
            capReached = end >= 0;
            return capReached ? end + 1 : read.Length;
        }

        if (_maximumCaptureBytes is not { } cap)
        {
            return read.Length;
        }

        var remaining = cap - offset;
        if (remaining > read.Length)
        {
            return read.Length;
        }

        var boundary = read[..(int)Math.Max(0, remaining)].LastIndexOf((byte)'\n');
        if (boundary >= 0)
        {
            capReached = true;
            return boundary + 1;
        }

        var crossing = read.IndexOf((byte)'\n');
        if (crossing >= 0)
        {
            capReached = true;
            return crossing + 1;
        }

        // A record longer than a whole read: keep taking it until its newline arrives, then
        // stop. Bounded by the parser's own maximum line length, which rejects anything
        // longer long before this can run away.
        finishingRecord = true;
        return read.Length;
    }

    /// <summary>
    /// Where the capture starts reading from, written in the zone the negotiated format
    /// prints in.
    /// </summary>
    /// <remarks>
    /// Two defects meet here. <c>logcat</c> matches <c>-T</c> against the timestamps it
    /// prints, so a cursor pinned to UTC is only correct on the top rung of the ladder; one
    /// rung down the device prints local time and the capture silently starts at the wrong
    /// instant. And a pre-roll of zero used to omit <c>-T</c> altogether, which makes logcat
    /// dump everything the ring still holds before it starts following — 320,832 entries and
    /// 44 MiB for a twenty-second capture on the device this was measured on, next to a
    /// label that reads as "no history" (finding F-06). Zero now means what it says; the
    /// whole ring is an explicit choice.
    /// </remarks>
    private string? BuildStartCursor()
    {
        if (_includeBufferHistory)
        {
            return null;
        }

        var since = _captureRequestedAtUtc - _preRoll;
        return TimeZoneInfo.ConvertTime(since, ResolveLogZone())
            .ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
    }

    private TimeZoneInfo ResolveLogZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Metadata.ResolveLogTimeZoneId());
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Waits for the device to be capturable again, and fails with the device's own reason
    /// once it has waited long enough.
    /// </summary>
    /// <remarks>
    /// A reboot is the ordinary case: the phone is gone for the best part of a minute and
    /// then comes back with the same serial, and a capture that gave up on the first absent
    /// poll would be worse than the defect this replaces. So absence is tolerated for a
    /// bounded window, is visible the whole time it lasts, and then ends the capture
    /// explicitly rather than leaving a blocked child looking like a healthy stream.
    /// </remarks>
    private async Task WaitForDeviceAsync(int reconnectAttempt, CancellationToken cancellationToken)
    {
        var startedMs = Environment.TickCount64;
        var announced = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await EnsureDeviceIsCapturableAsync(cancellationToken).ConfigureAwait(false);
                if (announced)
                {
                    ReportConnectionStatus(new SourceConnectionStatus(
                        $"Reconnecting (attempt {reconnectAttempt})",
                        $"Device {_serial} answered again and the capture is resuming from the last record it received."));
                }

                return;
            }
            catch (AdbCaptureUnavailableException exception)
            {
                var waited = TimeSpan.FromMilliseconds(Environment.TickCount64 - startedMs);
                if (waited >= Timing.DeviceReturnTimeout)
                {
                    throw;
                }

                announced = true;
                ReportConnectionStatus(new SourceConnectionStatus(
                    $"Device {_serial} has not responded for {DescribeElapsed(waited)}",
                    $"{exception.Message} The capture is waiting up to " +
                    $"{DescribeElapsed(Timing.DeviceReturnTimeout)} for the device to come back, and has waited " +
                    $"{DescribeElapsed(waited)} so far. Everything captured before the break is already saved."));
                await Task.Delay(Timing.DevicePollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Asks the transport whether a silent stream still has a device behind it.
    /// </summary>
    /// <remarks>
    /// A logcat child whose device disappears does not exit — it blocks — so silence alone
    /// cannot distinguish "the phone has nothing to say" from "the phone is gone". Quiet is
    /// the common case and an <c>adb devices</c> call is not free, so the question is only
    /// asked once the quiet has lasted long enough to be worth asking about. When the answer
    /// is that the device is gone, the blocked child is ended, which turns an invisible
    /// hang into the ordinary reconnect path and, if the device never returns, into an
    /// explicit failure (finding F-12b).
    /// </remarks>
    private async Task WatchForSilentTransportAsync(
        IAdbProcess process,
        CancellationToken watchdogToken,
        CancellationToken captureToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(watchdogToken, captureToken);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                await Task.Delay(Timing.SilenceProbeInterval, linked.Token).ConfigureAwait(false);
                var silence = TimeSpan.FromMilliseconds(
                    Environment.TickCount64 - Volatile.Read(ref _lastByteMs));
                if (silence < Timing.SilenceProbeThreshold)
                {
                    continue;
                }

                var devices = await ListDevicesBoundedAsync(linked.Token).ConfigureAwait(false);
                if (devices is null)
                {
                    continue;
                }

                var device = devices.FirstOrDefault(candidate =>
                    string.Equals(candidate.Serial, _serial, StringComparison.Ordinal));
                if (device is { State: AdbDeviceState.Device })
                {
                    continue;
                }

                ReportConnectionStatus(new SourceConnectionStatus(
                    $"Device {_serial} has not responded for {DescribeElapsed(silence)}",
                    device is null
                        ? $"Device {_serial} is no longer connected. The capture is restarting its stream and " +
                          "will wait for the device to come back before giving up. Everything captured so far is saved."
                        : $"Device {_serial} is now {device.State.ToString().ToLowerInvariant()} and cannot be read. " +
                          "The capture is restarting its stream. Everything captured so far is saved."));
                OpenReconnectGap();
                await process.StopAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IReadOnlyList<AdbDevice>?> ListDevicesBoundedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(Timing.DiscoveryTimeout);
            return await _client.ListDevicesAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ProcessNameRange>> GetProcessNamesAsync(CancellationToken cancellationToken)
    {
        var instant = InstantUs.FromDateTimeOffset(DateTimeOffset.UtcNow);
        var result = await _client.RunAsync(
            ["-s", _serial, "shell", "ps", "-A", "-o", "PID,NAME"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return AdbProcessParser.Parse(result.StandardOutput, instant);
    }

    public DefectCounters GetDefects()
    {
        var measured = Interlocked.Read(ref _reconnectGapMilliseconds);
        var open = Interlocked.Read(ref _gapStartedMs);
        if (open != 0)
        {
            measured += Math.Max(0, Environment.TickCount64 - open);
        }

        return new DefectCounters(
            ReconnectGaps: Interlocked.Read(ref _reconnectGaps),
            ReconnectGapMilliseconds: measured);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stop.Cancel();
        var process = _activeProcess;
        if (process is { HasExited: false })
        {
            await process.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _stop.Dispose();
    }

    /// <summary>Starts the clock on a break in the stream, and counts it once.</summary>
    private void OpenReconnectGap()
    {
        if (Interlocked.CompareExchange(ref _gapStartedMs, Environment.TickCount64, 0) == 0)
        {
            Interlocked.Increment(ref _reconnectGaps);
        }
    }

    /// <summary>
    /// Books how long a break actually lasted. A count with no duration beside it says that
    /// something is missing and nothing about how much (finding F-12).
    /// </summary>
    private void CloseReconnectGap()
    {
        var started = Interlocked.Exchange(ref _gapStartedMs, 0);
        if (started == 0)
        {
            return;
        }

        Interlocked.Add(ref _reconnectGapMilliseconds, Math.Max(0, Environment.TickCount64 - started));
        ReportConnectionStatus(null);
    }

    private void ReportConnectionStatus(SourceConnectionStatus? status) =>
        ConnectionStatusChanged?.Invoke(status);

    private void ReportStreamEstablished() => StreamEstablished?.Invoke();

    /// <summary>"1 MiB", "512 KiB", "700 bytes" — the unit the reader typed it in.</summary>
    private static string DescribeBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 when bytes % (1024L * 1024) == 0 => $"{bytes / (1024L * 1024):N0} MiB",
        >= 1024L * 1024 => $"{bytes / (double)(1024L * 1024):N1} MiB",
        >= 1024 when bytes % 1024 == 0 => $"{bytes / 1024:N0} KiB",
        >= 1024 => $"{bytes / 1024d:N1} KiB",
        _ => $"{bytes:N0} bytes",
    };

    private static string DescribeElapsed(TimeSpan elapsed) =>
        elapsed.TotalMinutes < 1
            ? $"{elapsed.TotalSeconds:N0}s"
            : elapsed.TotalHours < 1
                ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
                : $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";

    /// <summary>
    /// Richest format first, degrading one modifier at a time (§13.6). Scraping
    /// <c>logcat -v help</c> is not viable — it is rejected outright as an invalid
    /// format on current devices — so each candidate is probed functionally: logcat
    /// exits non-zero when it does not understand a modifier.
    /// </summary>
    private static readonly string[] CandidateFormats =
    [
        "threadtime,year,UTC,usec",
        "threadtime,year,UTC",
        "threadtime,year,usec",
        "threadtime,year",
        "threadtime",
    ];

    /// <summary>Negotiation ladder, richest first. Recorded in the session manifest.</summary>
    public static IReadOnlyList<string> FormatCandidates => CandidateFormats;

    /// <summary>
    /// Confirms the device is present and authorized before anything is spawned against
    /// it. This check is not redundant with probing: <c>adb -s &lt;unknown&gt; logcat</c>
    /// does not fail, it blocks indefinitely waiting for the device to appear, so a
    /// capture against a wrong or disconnected serial would otherwise hang until the
    /// session's own stop fired and then report an empty capture as a success.
    /// </summary>
    private async Task<AdbDevice> EnsureDeviceIsCapturableAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AdbDevice> devices;
        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(Timing.DiscoveryTimeout);
            devices = await _client.ListDevicesAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AdbCaptureUnavailableException(
                $"ADB did not answer within {Timing.DiscoveryTimeout.TotalSeconds:N0}s. " +
                "Restart the ADB server (adb kill-server) and retry.");
        }
        catch (Exception exception)
        {
            throw new AdbCaptureUnavailableException(
                $"ADB could not enumerate devices: {exception.Message}", exception);
        }

        var device = devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Serial, _serial, StringComparison.Ordinal));
        if (device is null)
        {
            var known = devices.Count == 0
                ? "no devices are connected"
                : $"connected devices: {string.Join(", ", devices.Select(static value => value.Serial))}";
            throw new AdbCaptureUnavailableException(
                $"Device '{_serial}' was not found ({known}). Connect the device and enable USB debugging.");
        }

        if (device.State != AdbDeviceState.Device)
        {
            throw new AdbCaptureUnavailableException(device.State switch
            {
                AdbDeviceState.Unauthorized =>
                    $"Device '{_serial}' has not authorized this computer. Accept the USB debugging prompt on the device and retry.",
                AdbDeviceState.Offline =>
                    $"Device '{_serial}' is offline. Reconnect it or restart the ADB server, then retry.",
                _ => $"Device '{_serial}' is not ready for capture (state: {device.State}).",
            });
        }

        return device;
    }

    private async Task<string> NegotiateFormatAsync(CancellationToken cancellationToken)
    {
        var buffers = string.Join(',', _buffers);
        var lastError = string.Empty;
        foreach (var candidate in CandidateFormats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Bounded: a device that accepts the connection but never answers must
                // degrade to the next candidate rather than stall the whole capture.
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeTimeout.CancelAfter(Timing.ProbeTimeout);
                var probe = await _client.RunAsync(
                    ["-s", _serial, "logcat", "-d", "-b", buffers, "-v", candidate, "-t", "1"],
                    probeTimeout.Token).ConfigureAwait(false);
                if (probe.ExitCode == 0)
                {
                    return candidate;
                }

                lastError = string.IsNullOrWhiteSpace(probe.StandardError) ? probe.StandardOutput : probe.StandardError;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastError = $"probe timed out after {Timing.ProbeTimeout.TotalSeconds:F0}s";
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
            }
        }

        // Every candidate failing means the device or buffer selection is unusable, or the
        // device cannot emit UTC timestamps at all. Capturing anyway would produce either
        // an empty session that still reported success, or one whose every timestamp is
        // silently off by the device's UTC offset (§13.5, §14.13, §18.1).
        throw new AdbCaptureUnavailableException(await DescribeFailureAsync(lastError, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> DescribeFailureAsync(string lastError, CancellationToken cancellationToken)
    {
        var detail = lastError.Trim();
        try
        {
            var devices = await _client.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
            var device = devices.FirstOrDefault(candidate =>
                string.Equals(candidate.Serial, _serial, StringComparison.Ordinal));
            if (device is null)
            {
                var known = devices.Count == 0
                    ? "no devices are connected"
                    : $"connected devices: {string.Join(", ", devices.Select(static value => value.Serial))}";
                return $"Device '{_serial}' was not found ({known}). Connect the device and enable USB debugging.";
            }

            return device.State switch
            {
                AdbDeviceState.Unauthorized =>
                    $"Device '{_serial}' has not authorized this computer. Accept the USB debugging prompt on the device and retry.",
                AdbDeviceState.Offline =>
                    $"Device '{_serial}' is offline. Reconnect it or restart the ADB server, then retry.",
                _ =>
                    $"Device '{_serial}' rejected every supported logcat format for buffers '{string.Join(',', _buffers)}'. " +
                    $"The buffer selection may be unavailable on this device. {detail}".TrimEnd(),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return $"Device '{_serial}' could not start a logcat capture. {detail}".TrimEnd();
        }
    }

    private static async Task<string> ReadBoundedErrorAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var text = new StringBuilder(64 * 1024);
        while (text.Length < 64 * 1024)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            text.Append(buffer, 0, Math.Min(read, 64 * 1024 - text.Length));
        }

        return text.ToString();
    }

    private void TrackResumeTimestamp(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is (byte)'\r')
            {
                continue;
            }

            if (value == (byte)'\n')
            {
                if (TryReadTimestamp(CollectionsMarshal.AsSpan(_lineBuffer), out var timestamp))
                {
                    _resumeTimestamp = timestamp;
                }

                _lineBuffer.Clear();
                continue;
            }

            if (_lineBuffer.Count < 512)
            {
                _lineBuffer.Add(value);
            }
        }
    }

    private static bool TryReadTimestamp(ReadOnlySpan<byte> line, out string timestamp)
    {
        timestamp = string.Empty;
        if (line.Length < 26 ||
            line[4] != (byte)'-' ||
            line[7] != (byte)'-' ||
            line[10] != (byte)' ' ||
            line[13] != (byte)':' ||
            line[16] != (byte)':' ||
            line[19] != (byte)'.')
        {
            return false;
        }

        for (var index = 0; index < 26; index++)
        {
            if (index is 4 or 7 or 10 or 13 or 16 or 19)
            {
                continue;
            }

            if (line[index] is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        timestamp = Encoding.ASCII.GetString(line[..26]);
        return true;
    }
}
