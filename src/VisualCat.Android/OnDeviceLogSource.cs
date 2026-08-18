using System.Runtime.CompilerServices;
using System.Text;
using Android.Content.PM;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Android;

public sealed class OnDeviceLogSource : ILogSource
{
    private readonly CancellationTokenSource _stop = new();
    private Java.Lang.Process? _process;

    public OnDeviceLogSource()
    {
        var context = global::Android.App.Application.Context;
        var fullDevice = context.CheckSelfPermission(global::Android.Manifest.Permission.ReadLogs) == Permission.Granted;
        Metadata = new SourceMetadata(
            SourceKind.Android,
            "On-device logcat",
            fullDevice ? "On-device full-device logcat" : "On-device own-app logcat",
            null,
            null,
            DateTimeOffset.UtcNow,
            false,
            false,
            Properties: new Dictionary<string, string>
            {
                ["scope"] = fullDevice ? "full-device" : "own-app",
                ["permission"] = fullDevice ? "READ_LOGS granted" : "platform restricted",
                ["buffers"] = "all",
                ["start"] = "live-tail",

                // ReadAsync asks logcat for UTC and has no fallback, so the source can say
                // so rather than leaving the capture to assume it.
                [SourceMetadata.LogTimeZoneProperty] = "UTC",
            });
    }

    public SourceMetadata Metadata { get; }

    public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(int maximumUsefulLines, CancellationToken cancellationToken)
    {
        _ = maximumUsefulLines;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ReadOnlyMemory<byte>> seed =
        [
            "2026-01-01 00:00:00.000000  1  1 I VisualCat: on-device format probe\n"u8.ToArray(),
        ];
        return Task.FromResult(seed);
    }

    public async IAsyncEnumerable<SourceChunk> ReadAsync(
        SourceReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = context;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        // Live means the live edge, not replaying the device's existing ring buffer first.
        // Without -T the subprocess starts at the oldest retained record. On a busy device the
        // app can then spend minutes ingesting history while `adb logcat` is already showing the
        // present, which makes Follow look frozen even though the reader is working. `-T 1` keeps
        // one real record for immediate continuity and then blocks for new records.
        //
        // Use every buffer logcat makes available to this UID. The previous main/system/crash
        // subset silently omitted traffic that an operator could see with a broader adb capture
        // (events/radio and, where available, other buffers), producing another false "quiet"
        // state. Android still applies the caller's permissions to `all`; this does not bypass
        // READ_LOGS restrictions.
        var arguments = new[]
        {
            "logcat",
            "-b", "all",
            "-T", "1",
            "-v", "threadtime,year,UTC,usec",
        };
        global::Android.Util.Log.Info(
            "VisualCat",
            $"Starting on-device logcat stream: buffers=all, start=live-tail, scope={Metadata.Properties?["scope"] ?? "unknown"}.");

        _process = Java.Lang.Runtime.GetRuntime()?.Exec(arguments)
            ?? throw new InvalidOperationException("Android logcat process could not be started.");
        using var registration = linked.Token.Register(static state => ((Java.Lang.Process)state!).Destroy(), _process);
        var input = _process.InputStream ?? throw new InvalidOperationException("Android logcat stdout is unavailable.");
        var error = _process.ErrorStream;
        var errorDrain = error is null
            ? Task.CompletedTask
            : DrainErrorStreamAsync(error, linked.Token);

        // Android normally limits an unprivileged app to its own UID's log records.
        // Emit one real logcat marker after the reader starts so the user gets prompt,
        // visible proof that the stream is connected even when the app is otherwise
        // quiet. It also distinguishes a working restricted capture from a source that
        // never delivered a byte.
        global::Android.Util.Log.Info("VisualCat", "Live capture connected; waiting for app log activity.");
        var buffer = new byte[256 * 1024];
        long offset = 0;
        long chunksRead = 0;
        try
        {
            while (!linked.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await input.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    read = 0;
                }
                catch (IOException) when (linked.IsCancellationRequested)
                {
                    read = 0;
                }
                catch (Java.IO.IOException) when (linked.IsCancellationRequested)
                {
                    read = 0;
                }

                if (read <= 0)
                {
                    global::Android.Util.Log.Info(
                        "VisualCat",
                        $"On-device logcat stdout ended after {offset:N0} bytes in {chunksRead:N0} chunks; cancelled={linked.IsCancellationRequested}.");
                    break;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                yield return new SourceChunk(offset, chunk);
                offset += read;
                chunksRead++;
            }
        }
        finally
        {
            // Leaving the async enumeration means this source no longer has a consumer. Destroy
            // the subprocess unconditionally so stdout/stderr both close, including the unusual
            // case where stdout ended while the process itself had not exited yet. Always observe
            // the stderr task so a diagnostic-stream failure never becomes unobserved.
            _process?.Destroy();

            try
            {
                await errorDrain.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
            }
            catch (IOException exception)
            {
                global::Android.Util.Log.Warn("VisualCat", $"Could not finish reading logcat stderr: {exception.Message}");
            }
            catch (Java.IO.IOException exception)
            {
                global::Android.Util.Log.Warn("VisualCat", $"Could not finish reading logcat stderr: {exception.Message}");
            }
        }
    }

    private static async Task DrainErrorStreamAsync(System.IO.Stream error, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];
        var pending = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await error.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Java.IO.IOException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
            while (true)
            {
                var text = pending.ToString();
                var newline = text.IndexOf('\n', StringComparison.Ordinal);
                if (newline < 0)
                {
                    break;
                }

                var line = text[..newline].TrimEnd('\r');
                pending.Remove(0, newline + 1);
                if (line.Length > 0)
                {
                    global::Android.Util.Log.Warn("VisualCat", $"logcat stderr: {line}");
                }
            }
        }

        if (pending.Length > 0)
        {
            global::Android.Util.Log.Warn("VisualCat", $"logcat stderr: {pending.ToString().TrimEnd('\r', '\n')}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _stop.Cancel();
        _process?.Destroy();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _process?.Destroy();
        _process?.Dispose();
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }
}
