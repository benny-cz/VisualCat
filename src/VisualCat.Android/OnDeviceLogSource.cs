using System.Runtime.CompilerServices;
using System.Text;
using Android.Content.PM;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;

namespace VisualCat.Android;

public sealed class OnDeviceLogSource : ILogSource, ISourceScopeReporter
{
    /// <summary>
    /// How long a capture may run before an unbroken diet of its own records is taken as
    /// proof that it is not seeing the device.
    /// </summary>
    /// <remarks>
    /// Long enough that a busy device's first burst has certainly arrived, and short enough
    /// that the reader is told before they have decided the app is broken. A declined
    /// capture is silent, so this timer is the whole of what turns that silence into an
    /// answer; the first foreign record ends it early and it is never started again.
    /// </remarks>
    private static readonly TimeSpan ScopeDecisionWindow = TimeSpan.FromSeconds(8);

    private const string DeclinedRemedy =
        "Android asks for permission to read the device log on every capture, and this one was " +
        "not allowed, so the capture can only see VisualCat's own log lines. Tap Live again and " +
        "choose the option that allows access.";

    private const string NotGrantedRemedy =
        "This capture can only see this app's own log lines, so an idle app produces " +
        "almost nothing. Android cannot prompt for wider access — READ_LOGS is not a " +
        "runtime permission — so full-device capture has to be granted over adb, and " +
        "again after every uninstall or reinstall:\n" +
        "adb shell pm grant com.barebit.visualcat android.permission.READ_LOGS";

    private readonly CancellationTokenSource _stop = new();
    private readonly bool _permissionHeld;
    private readonly int _ownPid = global::Android.OS.Process.MyPid();
    private readonly StringBuilder _scopeLine = new();
    private readonly Lock _scopeSync = new();
    private Timer? _scopeDeadline;
    private int _scopeResolved;
    private Java.Lang.Process? _process;

    public OnDeviceLogSource()
    {
        var context = global::Android.App.Application.Context;
        _permissionHeld = context.CheckSelfPermission(global::Android.Manifest.Permission.ReadLogs) == Permission.Granted;

        // Deliberately not "full-device" yet. Holding READ_LOGS is a necessary condition and
        // not a sufficient one: the platform still asks the reader on every capture, and a
        // declined capture is granted, running, and restricted all at once. The name says
        // what is certain until the stream says more (audit 2, C1).
        Metadata = new SourceMetadata(
            SourceKind.Android,
            SourceMetadata.NameCaptureStartedNow("On-device logcat"),
            _permissionHeld ? "On-device logcat" : "On-device own-app logcat",
            null,
            null,
            DateTimeOffset.UtcNow,
            false,
            false,
            Properties: new Dictionary<string, string>
            {
                ["scope"] = _permissionHeld ? "pending" : "own-app",
                ["permission"] = _permissionHeld ? "READ_LOGS granted" : "platform restricted",
                ["buffers"] = "all",
                ["start"] = "live-tail",

                // ReadAsync asks logcat for UTC and has no fallback, so the source can say
                // so rather than leaving the capture to assume it.
                [SourceMetadata.LogTimeZoneProperty] = "UTC",
            });
    }

    public SourceMetadata Metadata { get; }

    /// <inheritdoc/>
    public event Action<SourceScopeReport>? ScopeResolved;

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

        // A source that already knows it cannot see the device says so at once; one that
        // still might starts the clock that decides (audit 2, C1). The clock is a timer
        // rather than a check on each arriving chunk, because the state it exists to catch
        // is precisely the one where no chunk ever arrives.
        if (!_permissionHeld)
        {
            ResolveScope(fullDevice: false, declined: false);
        }
        else
        {
            StartScopeDeadline();
        }

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

                    // A stream that ended on its own, without a single foreign record, is
                    // the same answer the deadline would have given, arriving sooner. A
                    // stream that ended because the reader stopped the capture has said
                    // nothing at all, and must not be read as a refusal.
                    if (!linked.IsCancellationRequested)
                    {
                        ResolveScope(fullDevice: false, declined: _permissionHeld);
                    }
                    else
                    {
                        CancelScopeDeadline();
                    }

                    break;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                InspectScope(chunk);
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

    /// <summary>
    /// Reads the pid off each arriving record and answers the scope question with the first
    /// one that is not ours.
    /// </summary>
    /// <remarks>
    /// A full-device capture of any running phone carries system, kernel and other-app
    /// records within its first few chunks, so one foreign pid is decisive and no amount of
    /// our own is. The buffer is bounded because a single logcat record can be long and this
    /// is only ever reading the first three tokens of a line.
    /// </remarks>
    private void InspectScope(ReadOnlySpan<byte> chunk)
    {
        if (Volatile.Read(ref _scopeResolved) != 0)
        {
            return;
        }

        foreach (var value in chunk)
        {
            if (value is (byte)'\n' or (byte)'\r')
            {
                if (_scopeLine.Length > 0)
                {
                    if (IsForeignRecord(_scopeLine.ToString()))
                    {
                        ResolveScope(fullDevice: true, declined: false);
                        return;
                    }

                    _scopeLine.Clear();
                }

                continue;
            }

            // Three whitespace-separated tokens in, the threadtime format has already given
            // up the pid; anything past that is the message and cannot change the answer.
            if (_scopeLine.Length < 64)
            {
                _scopeLine.Append((char)value);
            }
        }
    }

    /// <summary>Whether a threadtime record was written by some process other than this one.</summary>
    /// <remarks>
    /// The format is <c>date time pid tid level tag: message</c>, so the pid is the third
    /// whitespace-separated token. A line that does not parse says nothing either way.
    /// </remarks>
    private bool IsForeignRecord(string line)
    {
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 3 &&
               int.TryParse(fields[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pid) &&
               pid != _ownPid;
    }

    /// <summary>
    /// Stops the clock without answering. A capture the reader ended says nothing about what
    /// it could see.
    /// </summary>
    private void CancelScopeDeadline()
    {
        lock (_scopeSync)
        {
            _scopeDeadline?.Dispose();
            _scopeDeadline = null;
        }
    }

    private void StartScopeDeadline()
    {
        lock (_scopeSync)
        {
            _scopeDeadline?.Dispose();
            _scopeDeadline = new Timer(
                static state => ((OnDeviceLogSource)state!).ResolveScope(fullDevice: false, declined: true),
                this,
                ScopeDecisionWindow,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void ResolveScope(bool fullDevice, bool declined)
    {
        if (Interlocked.Exchange(ref _scopeResolved, 1) != 0)
        {
            return;
        }

        lock (_scopeSync)
        {
            _scopeDeadline?.Dispose();
            _scopeDeadline = null;
        }

        var report = fullDevice
            ? new SourceScopeReport(true, "On-device full-device logcat", null, null)
            : new SourceScopeReport(
                false,
                "On-device own-app logcat",
                declined ? "log access was not allowed" : "own-app scope only",
                declined ? DeclinedRemedy : NotGrantedRemedy);
        global::Android.Util.Log.Info(
            "VisualCat",
            $"On-device logcat scope resolved: fullDevice={fullDevice}, declined={declined}.");
        ScopeResolved?.Invoke(report);
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

        // Before the cancellation, so the deadline cannot fire between the two and report a
        // capture the reader ended as one the platform refused.
        CancelScopeDeadline();
        _stop.Cancel();
        _process?.Destroy();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        CancelScopeDeadline();
        _stop.Cancel();
        _process?.Destroy();
        _process?.Dispose();
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }
}
