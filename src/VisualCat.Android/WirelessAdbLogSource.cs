using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using IO.Github.Muntashirakon.Adb;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Android;

/// <summary>
/// Streams full-device logcat through Android's user-authorised local Wireless debugging service.
/// </summary>
/// <remarks>
/// The source never asks VisualCat's application process for READ_LOGS. Android executes the fixed
/// logcat command as its authenticated ADB shell, which is exactly the capability the user enabled
/// and paired in Developer options. A saved pairing removes the need to enter a code again, but
/// Wireless debugging must remain enabled while this source is running.
///
/// Unexpected transport loss is retried with the last complete logcat timestamp. The retry can
/// overlap the final record at the boundary, which is preferable to silently skipping an unknown
/// interval; every reconnect is recorded in the session defect counters.
/// </remarks>
internal sealed class WirelessAdbLogSource : ILogSource, ISourceDefectSource, ISourceConnectionStatusReporter
{
    private const string LogTag = "VisualCat.WirelessAdb";
    private const int MaximumReconnectAttempts = 5;
    private const int MaximumConsecutiveTransportGaps = 5;
    private const int PumpBufferBytes = 64 * 1024;
    private const int PumpQueueCapacity = 16;
    private const int MaximumBufferedRecordBytes = 16 * 1024 * 1024;

    private readonly WirelessAdbService _service;
    private readonly CancellationTokenSource _stop = new();
    private readonly NewlineRecordFramer _recordFramer = new(MaximumBufferedRecordBytes);
    private readonly List<byte> _timestampLineBuffer = new(512);
    private readonly object _streamSync = new();
    private AdbStream? _activeStream;
    private string? _resumeTimestamp;
    private long _reconnectGaps;
    private int _released;
    private int _disposed;

    internal WirelessAdbLogSource(WirelessAdbService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Metadata = new SourceMetadata(
            SourceKind.Android,
            SourceMetadata.NameCaptureStartedNow("Wireless logcat"),
            "Wireless debugging full-device logcat",
            null,
            null,
            DateTimeOffset.UtcNow,
            false,
            false,
            Properties: new Dictionary<string, string>
            {
                ["scope"] = "full-device",
                ["transport"] = "wireless-adb",
                ["permission"] = "ADB shell; READ_LOGS not granted by VisualCat",
                ["buffers"] = "all",
                ["start"] = "live-tail",
                [SourceMetadata.LogTimeZoneProperty] = "UTC",
            });
        global::Android.Util.Log.Info(
            LogTag,
            "Wireless ADB log source created with declared full-device scope. The fixed shell command will be opened only when the coordinator starts reading.");
    }

    public SourceMetadata Metadata { get; }

    public event Action<SourceConnectionStatus?>? ConnectionStatusChanged;

    public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(
        int maximumUsefulLines,
        CancellationToken cancellationToken)
    {
        _ = maximumUsefulLines;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ReadOnlyMemory<byte>> probe =
        [
            "2026-01-01 00:00:00.000000  1  1 I VisualCat: wireless adb live format probe\n"u8.ToArray(),
        ];
        return Task.FromResult(probe);
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        long offset = 0;
        AdbStream? stream = null;
        var consecutiveTransportGaps = 0;

        try
        {
            try
            {
                stream = await _service.OpenLogcatStreamAsync(null, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                yield break;
            }

            while (!linked.IsCancellationRequested)
            {
                if (stream is null)
                {
                    throw CaptureDisconnected();
                }

                var currentStream = stream;
                SetActiveStream(currentStream);
                global::Android.Util.Log.Info(
                    LogTag,
                    offset == 0
                        ? "Wireless ADB full-device logcat stream is active."
                        : "Wireless ADB full-device logcat stream resumed after a transport interruption.");
                ReportConnectionStatus(null);

                // LibADB 3.2.0 receives WRTE packets on its connection thread and stores them in
                // an unbounded internal queue until the caller reads them. Reading only when the
                // parser asks for another SourceChunk would therefore let a very busy device grow
                // that third-party queue without limit while VisualCat applies normal downstream
                // backpressure. A dedicated pump continuously drains LibADB into our own bounded
                // 1 MiB queue. If that queue fills, the pump closes this ADB stream immediately;
                // after the already-buffered bytes have been committed, capture reconnects from
                // the last complete timestamp. This prefers a visible reconnect gap (and logcat's
                // ring-buffer recovery) over an unbounded-memory failure.
                var pumpQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(PumpQueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });

                using var cancellationRegistration = linked.Token.Register(
                    () => CloseStreamSafely(currentStream, "capture cancellation"));
                var pumpTask = PumpStreamAsync(currentStream, pumpQueue.Writer, linked.Token);
                PumpResult pumpResult;
                try
                {
                    await foreach (var chunk in pumpQueue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        // Drain bytes that were already read from adbd even after Stop cancels the
                        // transport. LineBatching intentionally flushes pending source data on stop,
                        // and discarding this small bounded queue here would violate that guarantee.
                        foreach (var completeRecords in _recordFramer.Append(chunk))
                        {
                            TrackResumeTimestamp(completeRecords.Span);
                            yield return new SourceChunk(offset, completeRecords);
                            offset += completeRecords.Length;
                            consecutiveTransportGaps = 0;
                        }
                    }

                    pumpResult = await pumpTask.ConfigureAwait(false);
                }
                finally
                {
                    ClearActiveStream(currentStream);
                    CloseStreamSafely(currentStream, "stream iteration completion");
                    stream = null;
                }

                if (linked.IsCancellationRequested || pumpResult.Reason == PumpEndReason.Cancelled)
                {
                    var finalTail = _recordFramer.FlushPending();
                    if (finalTail.Length > 0)
                    {
                        // There will be no following transport to concatenate with this tail. Keep
                        // every byte received before Stop, matching the coordinator's graceful-tail
                        // guarantee even when adbd did not finish the final newline.
                        yield return new SourceChunk(offset, finalTail);
                    }

                    yield break;
                }

                var discardedPartialBytes = _recordFramer.DiscardPending();
                _timestampLineBuffer.Clear();
                consecutiveTransportGaps++;
                Interlocked.Increment(ref _reconnectGaps);
                global::Android.Util.Log.Warn(
                    LogTag,
                    pumpResult.Reason == PumpEndReason.Backpressure
                        ? $"Wireless ADB transport was deliberately recycled because the bounded {PumpQueueCapacity * PumpBufferBytes / 1024} KiB receive queue filled. This prevents LibADB's unbounded internal queue from growing without limit; reconnect gap={consecutiveTransportGaps}/{MaximumConsecutiveTransportGaps}, discardedPartialRecordBytes={discardedPartialBytes}, resume timestamp={_resumeTimestamp ?? "unavailable"}."
                        : $"Wireless ADB transport ended unexpectedly; reconnect gap={consecutiveTransportGaps}/{MaximumConsecutiveTransportGaps}, reason={pumpResult.Reason}, discardedPartialRecordBytes={discardedPartialBytes}, resume timestamp={_resumeTimestamp ?? "unavailable"}, detail={pumpResult.Exception?.Message ?? "none"}.");

                ReportConnectionStatus(new SourceConnectionStatus(
                    "Wireless debugging interrupted · preparing to reconnect",
                    "The local debugging connection ended unexpectedly. VisualCat is keeping the capture already saved and is reconnecting with the saved pairing."));

                if (consecutiveTransportGaps > MaximumConsecutiveTransportGaps)
                {
                    throw CaptureDisconnected(pumpResult.Exception);
                }

                Exception? lastReconnectFailure = pumpResult.Exception;
                for (var reconnectAttempt = 1;
                     reconnectAttempt <= MaximumReconnectAttempts && !linked.IsCancellationRequested;
                     reconnectAttempt++)
                {
                    var delay = TimeSpan.FromMilliseconds(
                        Math.Min(4_000, 250 * Math.Pow(2, reconnectAttempt - 1)));
                    global::Android.Util.Log.Warn(
                        LogTag,
                        $"Wireless ADB reconnect attempt {reconnectAttempt}/{MaximumReconnectAttempts} will start after {delay.TotalMilliseconds:0} ms; resume timestamp={_resumeTimestamp ?? "unavailable"}.");
                    ReportConnectionStatus(new SourceConnectionStatus(
                        $"Wireless debugging interrupted · reconnecting {reconnectAttempt}/{MaximumReconnectAttempts}",
                        $"VisualCat is looking for Android's Wireless debugging service with the saved pairing (attempt {reconnectAttempt} of {MaximumReconnectAttempts}). Keep Wireless debugging on; the captured session remains safe and Stop remains available."));
                    try
                    {
                        await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                        yield break;
                    }

                    if (!await TryPrepareReconnectAsync(reconnectAttempt, linked.Token).ConfigureAwait(false))
                    {
                        continue;
                    }

                    try
                    {
                        stream = await _service
                            .OpenLogcatStreamAsync(_resumeTimestamp, linked.Token)
                            .ConfigureAwait(false);
                        lastReconnectFailure = null;
                        ReportConnectionStatus(new SourceConnectionStatus(
                            "Wireless debugging reconnected · resuming the log",
                            "The local debugging connection recovered. VisualCat is reopening logcat from the last complete record timestamp."));
                        break;
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                        yield break;
                    }
                    catch (Exception exception)
                    {
                        lastReconnectFailure = exception;
                        global::Android.Util.Log.Warn(
                            LogTag,
                            $"Wireless ADB transport reconnected on attempt {reconnectAttempt}, but reopening logcat failed: {exception.GetType().Name}: {exception.Message}");
                    }
                }

                if (stream is null)
                {
                    ReportConnectionStatus(new SourceConnectionStatus(
                        "Wireless debugging reconnect failed",
                        "VisualCat could not restore the local debugging connection after five attempts. The capture already written will be kept."));
                    throw CaptureDisconnected(lastReconnectFailure);
                }
            }
        }
        finally
        {
            CloseCurrentStream("read loop completion");
            await ReleaseCaptureOnceAsync().ConfigureAwait(false);
        }
    }

    private static Task<PumpResult> PumpStreamAsync(
        AdbStream stream,
        ChannelWriter<byte[]> writer,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var buffer = new byte[PumpBufferBytes];
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (global::Java.IO.IOException exception)
                    {
                        return cancellationToken.IsCancellationRequested
                            ? new PumpResult(PumpEndReason.Cancelled, exception)
                            : new PumpResult(PumpEndReason.TransportClosed, exception);
                    }
                    catch (IOException exception)
                    {
                        return cancellationToken.IsCancellationRequested
                            ? new PumpResult(PumpEndReason.Cancelled, exception)
                            : new PumpResult(PumpEndReason.TransportClosed, exception);
                    }
                    catch (Exception exception)
                    {
                        return cancellationToken.IsCancellationRequested
                            ? new PumpResult(PumpEndReason.Cancelled, exception)
                            : new PumpResult(PumpEndReason.Faulted, exception);
                    }

                    if (read <= 0)
                    {
                        return cancellationToken.IsCancellationRequested
                            ? new PumpResult(PumpEndReason.Cancelled)
                            : new PumpResult(PumpEndReason.RemoteEndOfStream);
                    }

                    var chunk = GC.AllocateUninitializedArray<byte>(read);
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    if (!writer.TryWrite(chunk))
                    {
                        // Do not wait for downstream capacity here. Waiting would simply move
                        // the unbounded queue back into LibADB. Closing this stream causes adbd
                        // to stop producing for it while the consumer drains the bounded backlog.
                        CloseStreamSafely(stream, "bounded receive queue backpressure");
                        return cancellationToken.IsCancellationRequested
                            ? new PumpResult(PumpEndReason.Cancelled)
                            : new PumpResult(PumpEndReason.Backpressure);
                    }
                }

                return new PumpResult(PumpEndReason.Cancelled);
            }
            finally
            {
                writer.TryComplete();
            }
        }, CancellationToken.None);
    }

    private enum PumpEndReason
    {
        Cancelled,
        RemoteEndOfStream,
        TransportClosed,
        Backpressure,
        Faulted,
    }

    private sealed record PumpResult(PumpEndReason Reason, Exception? Exception = null);

    public DefectCounters GetDefects() =>
        new(ReconnectGaps: Interlocked.Read(ref _reconnectGaps));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!_stop.IsCancellationRequested)
        {
            global::Android.Util.Log.Info(
                LogTag,
                "Stopping Wireless ADB log source. The active logcat stream and local debugging connection will be closed.");
            _stop.Cancel();
        }

        CloseCurrentStream("explicit stop");
        await ReleaseCaptureOnceAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ConnectionStatusChanged = null;
            _stop.Dispose();
            global::Android.Util.Log.Info(LogTag, "Wireless ADB log source disposed.");
        }
    }

    private async Task<bool> TryPrepareReconnectAsync(
        int reconnectAttempt,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return await _service
            .ReconnectForCaptureAsync(reconnectAttempt, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ReleaseCaptureOnceAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            await _service.ReleaseCaptureAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                LogTag,
                $"Wireless ADB capture connection could not be released cleanly: {exception.GetType().FullName}: {exception.Message}\n{exception}");
        }
    }

    private void SetActiveStream(AdbStream stream)
    {
        lock (_streamSync)
        {
            _activeStream = stream;
        }
    }

    private void ClearActiveStream(AdbStream stream)
    {
        lock (_streamSync)
        {
            if (ReferenceEquals(_activeStream, stream))
            {
                _activeStream = null;
            }
        }
    }

    private void CloseCurrentStream(string reason)
    {
        AdbStream? stream;
        lock (_streamSync)
        {
            stream = _activeStream;
            _activeStream = null;
        }

        CloseStreamSafely(stream, reason);
    }

    private static void CloseStreamSafely(AdbStream? stream, string reason)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Close();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(
                LogTag,
                $"Wireless ADB stream close failed during {reason}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ReportConnectionStatus(SourceConnectionStatus? status)
    {
        try
        {
            ConnectionStatusChanged?.Invoke(status);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn(
                LogTag,
                $"A Wireless ADB connection-status observer failed: {exception.GetType().Name}: {exception.Message}");
        }
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
                if (TryReadTimestamp(CollectionsMarshal.AsSpan(_timestampLineBuffer), out var timestamp))
                {
                    _resumeTimestamp = timestamp;
                }

                _timestampLineBuffer.Clear();
                continue;
            }

            if (_timestampLineBuffer.Count < 512)
            {
                _timestampLineBuffer.Add(value);
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

    private static IOException CaptureDisconnected(Exception? innerException = null) =>
        new(
            "Wireless debugging disconnected repeatedly. Keep Wireless debugging on while Live is running, then start Live again. VisualCat keeps the capture already written.",
            innerException);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
