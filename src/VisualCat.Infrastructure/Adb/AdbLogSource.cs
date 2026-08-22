using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Infrastructure.Adb;

public sealed class AdbLogSource : ILogSource, IProcessNameSource, ISourceDefectSource
{
    private readonly IAdbClient _client;
    private readonly string _serial;
    private readonly string[] _buffers;
    private readonly long? _maximumCaptureBytes;
    private readonly string? _initialSince;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<byte> _lineBuffer = new(512);
    private IAdbProcess? _activeProcess;
    private string? _resumeTimestamp;
    private string? _negotiatedFormat;
    private long _reconnectGaps;

    public AdbLogSource(
        IAdbClient client,
        string serial,
        IReadOnlyList<string>? buffers = null,
        long? maximumCaptureBytes = null,
        TimeSpan? preRoll = null)
    {
        if (maximumCaptureBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCaptureBytes));
        }

        _client = client;
        _serial = serial;
        _buffers = buffers?.ToArray() ?? ["main", "system", "crash"];
        _maximumCaptureBytes = maximumCaptureBytes;
        if (preRoll is { } preRollValue)
        {
            if (preRollValue < TimeSpan.Zero || preRollValue > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(nameof(preRoll));
            }

            if (preRollValue > TimeSpan.Zero)
            {
                _initialSince = (DateTimeOffset.UtcNow - preRollValue)
                    .ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

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
            new Dictionary<string, string>
            {
                ["buffers"] = string.Join(',', _buffers),
                ["adb"] = _client.ExecutablePath,
                ["maximumCaptureBytes"] = _maximumCaptureBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unlimited",
                ["preRollSeconds"] = preRoll?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0",
            });
    }

    public SourceMetadata Metadata { get; private set; }

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
    /// Idempotent, and the result is reused by <see cref="ReadAsync"/> rather than probed
    /// a second time.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (_negotiatedFormat is not null)
        {
            return;
        }

        await EnsureDeviceIsCapturableAsync(cancellationToken).ConfigureAwait(false);
        var format = await NegotiateFormatAsync(cancellationToken).ConfigureAwait(false);
        var zone = format.Contains("UTC", StringComparison.Ordinal)
            ? "UTC"
            : await ReadDeviceTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        _negotiatedFormat = format;
        Metadata = Metadata with
        {
            Properties = new Dictionary<string, string>(Metadata.Properties!, StringComparer.Ordinal)
            {
                ["format"] = format,
                [SourceMetadata.LogTimeZoneProperty] = zone,
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
        try
        {
            var result = await _client
                .RunAsync(["-s", _serial, "shell", "getprop", "persist.sys.timezone"], cancellationToken)
                .ConfigureAwait(false);
            var zone = result.StandardOutput.Trim();
            if (result.ExitCode == 0 && zone.Length > 0)
            {
                TimeZoneInfo.FindSystemTimeZoneById(zone);
                return zone;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is TimeZoneNotFoundException or InvalidTimeZoneException or IOException or InvalidOperationException)
        {
        }

        return TimeZoneInfo.Local.Id;
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
        await EnsureDeviceIsCapturableAsync(linked.Token).ConfigureAwait(false);
        var format = _negotiatedFormat ?? await NegotiateFormatAsync(linked.Token).ConfigureAwait(false);
        long offset = 0;
        var reconnectAttempt = 0;
        while (!linked.IsCancellationRequested)
        {
            // -D prints a divider on every buffer crossing, so a multi-buffer capture can
            // attribute each record to the buffer it actually came from rather than to
            // whichever one was announced last (finding F-12).
            var arguments = new List<string>
            {
                "-s", _serial, "logcat", "-b", string.Join(',', _buffers), "-D", "-v", format,
            };
            if (reconnectAttempt > 0 && _resumeTimestamp is { } resumeTimestamp)
            {
                arguments.Add("-T");
                arguments.Add(resumeTimestamp);
            }
            else if (reconnectAttempt == 0 && _initialSince is { } initialSince)
            {
                arguments.Add("-T");
                arguments.Add(initialSince);
            }

            await using var process = _client.StartProcess(arguments);
            _activeProcess = process;
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

                    var accepted = _maximumCaptureBytes is { } maximum
                        ? (int)Math.Min(read, maximum - offset)
                        : read;
                    if (accepted <= 0)
                    {
                        await StopAsync(CancellationToken.None).ConfigureAwait(false);
                        yield break;
                    }

                    var chunk = new byte[accepted];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, accepted);
                    TrackResumeTimestamp(chunk);
                    yield return new SourceChunk(offset, chunk);
                    offset += accepted;
                    reconnectAttempt = 0;
                    if (_maximumCaptureBytes is { } cap && offset >= cap)
                    {
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
                Interlocked.Increment(ref _reconnectGaps);
                if (reconnectAttempt > 5)
                {
                    throw new IOException($"ADB logcat exited repeatedly with code {process.ExitCode}: {error}");
                }

                var delay = TimeSpan.FromMilliseconds(Math.Min(10_000, 250 * Math.Pow(2, reconnectAttempt - 1)));
                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                if (ReferenceEquals(_activeProcess, process))
                {
                    _activeProcess = null;
                }
            }
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

    public DefectCounters GetDefects() => new(ReconnectGaps: Interlocked.Read(ref _reconnectGaps));

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

    /// <summary>
    /// Richest format first, degrading one modifier at a time (§13.6). Scraping
    /// <c>logcat -v help</c> is not viable — it is rejected outright as an invalid
    /// format on current devices — so each candidate is probed functionally: logcat
    /// exits non-zero when it does not understand a modifier.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

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
    private async Task EnsureDeviceIsCapturableAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AdbDevice> devices;
        try
        {
            devices = await _client.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
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
                probeTimeout.CancelAfter(ProbeTimeout);
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
                lastError = $"probe timed out after {ProbeTimeout.TotalSeconds:F0}s";
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
