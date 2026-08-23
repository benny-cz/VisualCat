using System.Text;
using Android.Content;
using Android.Net.Wifi;
using IO.Github.Muntashirakon.Adb;
using VisualCat.App.Platform;
using VisualCat.Application.Ports;

namespace VisualCat.Android;

/// <summary>
/// Owns the explicitly authorised local Wireless debugging connection used for full-device
/// capture when VisualCat does not hold Android's privileged READ_LOGS permission.
/// </summary>
/// <remarks>
/// This service deliberately exposes no general-purpose shell API. The only shell stream it can
/// create is the fixed logcat transport consumed by <see cref="WirelessAdbLogSource"/>. Pairing
/// values are used only by LibADB's pairing handshake and are never interpolated into a command,
/// persisted, or written to logs.
///
/// Pairing is durable because Android remembers VisualCat's ADB public key. The transport is not:
/// Wireless debugging must be enabled while a Live capture is running, and the connection is
/// disconnected as soon as the source stops or is disposed. This preserves Android's security
/// boundary instead of trying to turn a temporary user-authorised debugging channel into a
/// privileged permission grant for the application itself.
/// </remarks>
internal sealed class WirelessAdbService : IDisposable
{
    private const string LogTag = "VisualCat.WirelessAdb";
    private const int DiscoveryTimeoutMilliseconds = 12_000;
    private const int MaximumAdbDestinationBytes = 96;

    private readonly Context _context;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateSync = new();
    private WirelessAdbConnectionManager? _manager;
    private bool _captureLeased;
    private int _disposed;

    internal WirelessAdbService(Context context)
    {
        _context = context.ApplicationContext
            ?? throw new ArgumentException("An Android application context is required.", nameof(context));
        global::Android.Util.Log.Info(
            LogTag,
            "Wireless ADB capture service created. No ADB connection has been opened and no shell command has been executed.");
    }

    internal static bool HasSavedIdentity(Context context) =>
        WirelessAdbConnectionManager.HasSavedIdentity(context);

    internal Task<WirelessAdbConnectionResult> PairAndConnectAsync(
        WirelessAdbPairingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        return ConnectCoreAsync(request, cancellationToken);
    }

    internal Task<WirelessAdbConnectionResult> ConnectSavedAsync(CancellationToken cancellationToken)
    {
        if (!HasSavedIdentity(_context))
        {
            return Task.FromResult(new WirelessAdbConnectionResult(
                Connected: false,
                PairingSucceeded: false,
                "No saved Wireless debugging pairing is available yet. Generate a pairing code in Android Settings and pair VisualCat first."));
        }

        return ConnectCoreAsync(null, cancellationToken);
    }

    internal ILogSource? CreateLogSource()
    {
        ThrowIfDisposed();
        lock (_stateSync)
        {
            if (_captureLeased)
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    "A Wireless ADB log source was requested while another source still owns the connection.");
                return null;
            }

            if (_manager is not { IsConnected: true })
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    "A Wireless ADB log source was requested without an authenticated connection.");
                return null;
            }

            _captureLeased = true;
            global::Android.Util.Log.Info(
                LogTag,
                "Wireless ADB connection leased to a full-device logcat source. The connection will remain open only for this Live capture.");
            return new WirelessAdbLogSource(this);
        }
    }

    internal async Task<AdbStream> OpenLogcatStreamAsync(
        string? resumeTimestamp,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateResumeTimestamp(resumeTimestamp);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var operationToken = linked.Token;

        var manager = _manager;
        if (manager is not { IsConnected: true })
        {
            throw new IOException(
                "Wireless debugging is not connected. Turn Wireless debugging on and start Live again.");
        }

        var destination = BuildLogcatDestination(resumeTimestamp);
        global::Android.Util.Log.Info(
            LogTag,
            resumeTimestamp is null
                ? "Opening fixed Wireless ADB logcat stream at the live tail."
                : $"Opening fixed Wireless ADB logcat stream from resume timestamp {resumeTimestamp} UTC after a transport interruption.");

        AdbStream? stream = null;
        try
        {
            operationToken.ThrowIfCancellationRequested();
            stream = await RunInterruptibleJavaOperationAsync(
                    () => manager.OpenStream(destination),
                    "opening the fixed logcat stream",
                    operationToken)
                .ConfigureAwait(false);
            if (operationToken.IsCancellationRequested)
            {
                CloseStreamSafely(stream, "because capture was cancelled while the logcat stream was opening");
                stream = null;
                operationToken.ThrowIfCancellationRequested();
            }

            return stream
                ?? throw new IOException("Android returned no Wireless debugging logcat stream.");
        }
        catch (OperationCanceledException)
        {
            CloseStreamSafely(stream, "after logcat stream opening was cancelled");
            throw;
        }
        catch (Exception exception)
        {
            CloseStreamSafely(stream, "after logcat stream opening failed");
            LogFailure("Opening the fixed Wireless ADB logcat stream failed", exception);
            throw new IOException(
                "Android closed the Wireless debugging log stream before capture could start.",
                exception);
        }
    }

    internal async Task<bool> ReconnectForCaptureAsync(
        int reconnectAttempt,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var operationToken = linked.Token;
        await _gate.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var manager = GetManager();
            DisconnectSafely(manager, $"before capture reconnect attempt {reconnectAttempt}");

            global::Android.Util.Log.Warn(
                LogTag,
                $"Wireless ADB log stream ended unexpectedly. Starting authenticated reconnect attempt {reconnectAttempt}; the saved ADB identity will be reused and no pairing code is required.");

            var multicastLock = AcquireMulticastLock();
            try
            {
                operationToken.ThrowIfCancellationRequested();
                var connected = await RunInterruptibleJavaOperationAsync(
                        () => manager.ConnectTls(_context, DiscoveryTimeoutMilliseconds),
                        $"Wireless ADB reconnect discovery attempt {reconnectAttempt}",
                        operationToken)
                    .ConfigureAwait(false);
                operationToken.ThrowIfCancellationRequested();

                if (connected && manager.IsConnected)
                {
                    global::Android.Util.Log.Info(
                        LogTag,
                        $"Wireless ADB reconnect attempt {reconnectAttempt} succeeded.");
                    return true;
                }

                global::Android.Util.Log.Warn(
                    LogTag,
                    $"Wireless ADB reconnect attempt {reconnectAttempt} completed without a connected transport; connectResult={connected}, isConnected={manager.IsConnected}.");
                return false;
            }
            catch (global::Java.Lang.InterruptedException exception)
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    $"Wireless ADB reconnect attempt {reconnectAttempt} ended during Android mDNS discovery: {exception.Message}");
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogFailure($"Wireless ADB reconnect attempt {reconnectAttempt} failed", exception);
                return false;
            }
            finally
            {
                ReleaseMulticastLock(multicastLock);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task ReleaseCaptureAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseAndDiscardManager("because the Live capture ended");

            var hadCaptureLease = false;
            lock (_stateSync)
            {
                hadCaptureLease = _captureLeased;
                _captureLeased = false;
            }

            if (hadCaptureLease)
            {
                global::Android.Util.Log.Info(
                    LogTag,
                    "Wireless ADB capture lease released and decrypted key material discarded. Android retains the pairing and leaves its Wireless debugging setting enabled until the user turns it off.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WirelessAdbConnectionResult> ConnectCoreAsync(
        WirelessAdbPairingRequest? request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var operationToken = linked.Token;
        await _gate.WaitAsync(operationToken).ConfigureAwait(false);
        var keepConnection = false;
        try
        {
            ThrowIfDisposed();
            lock (_stateSync)
            {
                if (_captureLeased)
                {
                    return new WirelessAdbConnectionResult(
                        Connected: false,
                        PairingSucceeded: false,
                        "A Wireless debugging capture is already active. Stop it before starting another connection.");
                }
            }

            var manager = GetManager();
            DisconnectSafely(manager, "before an explicit connection attempt");

            var pairedThisAttempt = false;
            if (request is not null)
            {
                global::Android.Util.Log.Info(
                    LogTag,
                    $"Starting explicit Wireless ADB pairing on loopback port {request.PairingPort}. Pairing code is intentionally not logged or persisted.");

                bool paired;
                try
                {
                    operationToken.ThrowIfCancellationRequested();
                    paired = await RunInterruptibleJavaOperationAsync(
                            () => manager.Pair("127.0.0.1", request.PairingPort, request.PairingCode),
                            "Wireless ADB pairing",
                            operationToken)
                        .ConfigureAwait(false);
                    operationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogPairingFailure(exception, request.PairingCode);
                    return Failure(
                        "Pairing failed. Keep Android's pairing-code panel open, verify the port and 6-digit code, then try again.");
                }

                if (!paired)
                {
                    global::Android.Util.Log.Warn(LogTag, "Wireless ADB pairing returned false without an exception.");
                    return Failure(
                        "Android did not accept the pairing. Generate a new pairing code and try again.");
                }

                pairedThisAttempt = true;
                try
                {
                    manager.MarkPairingSucceeded();
                }
                catch (Exception exception)
                {
                    LogFailure("Android accepted Wireless ADB pairing, but its successful state could not be saved", exception);
                    return Failure(
                        "Android accepted the code, but VisualCat could not save the completed pairing state. Generate a new code and try once more so reconnect remains reliable.");
                }

                global::Android.Util.Log.Info(
                    LogTag,
                    "Wireless ADB pairing completed successfully. Discovering Android's separate authenticated TLS connection service.");
            }
            else
            {
                global::Android.Util.Log.Info(
                    LogTag,
                    "Trying the saved Wireless ADB identity without a new pairing code. Discovering Android's authenticated TLS connection service.");
            }

            var multicastLock = AcquireMulticastLock();
            try
            {
                bool connected;
                try
                {
                    operationToken.ThrowIfCancellationRequested();
                    connected = await RunInterruptibleJavaOperationAsync(
                            () => manager.ConnectTls(_context, DiscoveryTimeoutMilliseconds),
                            "Wireless ADB TLS discovery and authentication",
                            operationToken)
                        .ConfigureAwait(false);
                    operationToken.ThrowIfCancellationRequested();
                }
                catch (global::Java.Lang.InterruptedException exception)
                {
                    global::Android.Util.Log.Warn(
                        LogTag,
                        $"Wireless ADB TLS discovery ended before a service was found: {exception.Message}");
                    return Failure(
                        pairedThisAttempt
                            ? "Pairing succeeded, but VisualCat could not find Android's Wireless debugging connection. Keep Wireless debugging on, then use the saved pairing connection."
                            : "VisualCat could not find Android's Wireless debugging connection. Turn Wireless debugging on, then try the saved pairing again or pair with a new code.",
                        pairedThisAttempt);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogFailure("Wireless ADB TLS discovery or authentication failed", exception);
                    return Failure(FriendlyConnectionFailure(exception, pairedThisAttempt), pairedThisAttempt);
                }

                if (!connected || !manager.IsConnected)
                {
                    global::Android.Util.Log.Warn(
                        LogTag,
                        $"Wireless ADB discovery finished without an established connection; connectResult={connected}, isConnected={manager.IsConnected}.");
                    return Failure(
                        pairedThisAttempt
                            ? "Pairing succeeded, but Android did not open the Wireless debugging connection. Keep Wireless debugging on and connect the saved pairing."
                            : "Android did not accept the saved Wireless debugging connection. Turn Wireless debugging on or pair VisualCat again with a new code.",
                        pairedThisAttempt);
                }

                keepConnection = true;
                global::Android.Util.Log.Info(
                    LogTag,
                    "Authenticated Wireless ADB connection established for full-device logcat. No privileged permission was granted or requested by VisualCat.");
                return new WirelessAdbConnectionResult(
                    Connected: true,
                    PairingSucceeded: pairedThisAttempt,
                    pairedThisAttempt
                        ? "Paired and connected. Keep Wireless debugging on while Live is running. VisualCat closes its connection when capture stops; Android leaves Wireless debugging enabled until you turn it off."
                        : "Connected with the saved pairing. Keep Wireless debugging on while Live is running. VisualCat closes its connection when capture stops; Android leaves Wireless debugging enabled until you turn it off.");
            }
            finally
            {
                ReleaseMulticastLock(multicastLock);
            }
        }
        finally
        {
            if (!keepConnection)
            {
                CloseAndDiscardManager("after an unsuccessful or cancelled connection attempt");
            }

            _gate.Release();
        }
    }

    private WirelessAdbConnectionManager GetManager()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        global::Android.Util.Log.Info(
            LogTag,
            "Creating the lazy Wireless ADB manager after explicit user action. The encrypted ADB identity will be loaded or created now.");
        _manager = new WirelessAdbConnectionManager(_context);
        return _manager;
    }

    private WifiManager.MulticastLock? AcquireMulticastLock()
    {
        try
        {
            var wifiManager = _context.GetSystemService(Context.WifiService) as WifiManager;
            if (wifiManager is null)
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    "WifiManager is unavailable; continuing ADB mDNS discovery without an explicit multicast lock.");
                return null;
            }

            var multicastLock = wifiManager.CreateMulticastLock("VisualCat-WirelessAdb-Discovery");
            if (multicastLock is null)
            {
                global::Android.Util.Log.Warn(
                    LogTag,
                    "Android did not create a Wi-Fi multicast lock; continuing ADB mDNS discovery without one.");
                return null;
            }

            multicastLock.SetReferenceCounted(false);
            multicastLock.Acquire();
            global::Android.Util.Log.Info(
                LogTag,
                "Acquired a short-lived Wi-Fi multicast lock for ADB mDNS discovery. It will be released immediately after discovery.");
            return multicastLock;
        }
        catch (Exception exception)
        {
            LogFailure("Could not acquire Wi-Fi multicast lock", exception);
            return null;
        }
    }

    private static void ReleaseMulticastLock(WifiManager.MulticastLock? multicastLock)
    {
        if (multicastLock is null)
        {
            return;
        }

        try
        {
            if (multicastLock.IsHeld)
            {
                multicastLock.Release();
                global::Android.Util.Log.Info(
                    LogTag,
                    "Released the short-lived Wi-Fi multicast lock after ADB mDNS discovery.");
            }
        }
        catch (Exception exception)
        {
            LogFailure("Could not release Wi-Fi multicast lock", exception);
        }
        finally
        {
            multicastLock.Dispose();
        }
    }

    /// <summary>
    /// Runs a blocking LibADB operation on a worker and maps .NET cancellation to Java thread
    /// interruption where LibADB waits on a latch/monitor. This keeps Cancel/Stop responsive
    /// during mDNS discovery and stream establishment without abandoning an unobserved Java task.
    /// </summary>
    private static async Task<T> RunInterruptibleJavaOperationAsync<T>(
        Func<T> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        var workerSync = new object();
        global::Java.Lang.Thread? workerThread = null;
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            lock (workerSync)
            {
                if (workerThread is null)
                {
                    return;
                }

                try
                {
                    global::Android.Util.Log.Info(
                        LogTag,
                        $"Cancellation requested while {operationName}; interrupting the LibADB Java worker so its blocking wait can unwind.");
                    workerThread.Interrupt();
                }
                catch (Exception exception)
                {
                    global::Android.Util.Log.Warn(
                        LogTag,
                        $"Could not interrupt LibADB Java worker while {operationName}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        });

        try
        {
            return await Task.Run(() =>
            {
                lock (workerSync)
                {
                    workerThread = global::Java.Lang.Thread.CurrentThread();
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return operation();
                }
                catch (global::Java.Lang.InterruptedException exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        $"Cancelled while {operationName}.",
                        exception,
                        cancellationToken);
                }
                finally
                {
                    lock (workerSync)
                    {
                        workerThread = null;
                    }

                    // Interrupt is a sticky flag when no interruptible Java wait consumed it.
                    // Clear it before this .NET thread-pool worker can be reused by another JNI
                    // operation. The return value is diagnostic-only and deliberately ignored.
                    _ = global::Java.Lang.Thread.Interrupted();
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    private static string BuildLogcatDestination(string? resumeTimestamp)
    {
        const string prefix = "shell:logcat -b all -D ";
        const string format = " -v threadtime,year,UTC,usec";
        var destination = resumeTimestamp is null
            ? prefix + "-T 1" + format
            : prefix + "-T \"" + resumeTimestamp + "\"" + format;

        // Keep the service string intentionally short even if LibADB changes internally. Older
        // libadb branches had an A_OPEN allocation bug for destinations around 104 bytes. The
        // current fixed command is 55 bytes at the live tail and 82 bytes when resuming; this
        // guard makes a future edit fail closed instead of silently crossing that historical
        // hazard or growing into an accidental general-purpose shell surface.
        var destinationBytes = Encoding.UTF8.GetByteCount(destination);
        if (destinationBytes > MaximumAdbDestinationBytes)
        {
            throw new InvalidOperationException(
                $"Wireless ADB logcat destination is unexpectedly long: {destinationBytes} bytes.");
        }

        return destination;
    }

    private static void ValidateResumeTimestamp(string? resumeTimestamp)
    {
        if (resumeTimestamp is null)
        {
            return;
        }

        if (resumeTimestamp.Length != 26 ||
            resumeTimestamp[4] != '-' ||
            resumeTimestamp[7] != '-' ||
            resumeTimestamp[10] != ' ' ||
            resumeTimestamp[13] != ':' ||
            resumeTimestamp[16] != ':' ||
            resumeTimestamp[19] != '.')
        {
            throw new ArgumentException("The Wireless ADB resume timestamp has an invalid shape.", nameof(resumeTimestamp));
        }

        for (var index = 0; index < resumeTimestamp.Length; index++)
        {
            if (index is 4 or 7 or 10 or 13 or 16 or 19)
            {
                continue;
            }

            if (resumeTimestamp[index] is < '0' or > '9')
            {
                throw new ArgumentException("The Wireless ADB resume timestamp contains invalid characters.", nameof(resumeTimestamp));
            }
        }
    }

    private static void ValidateRequest(WirelessAdbPairingRequest request)
    {
        if (request.PairingPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Wireless debugging pairing port must be between 1 and 65535.");
        }

        if (string.IsNullOrEmpty(request.PairingCode) ||
            request.PairingCode.Length != 6 ||
            request.PairingCode.Any(static character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "Wireless debugging pairing code must contain exactly six ASCII digits.",
                nameof(request));
        }
    }

    private static WirelessAdbConnectionResult Failure(
        string message,
        bool pairedThisAttempt = false) =>
        new(false, pairedThisAttempt, message);

    private static string FriendlyConnectionFailure(Exception exception, bool pairedThisAttempt)
    {
        var typeName = exception.GetType().Name;
        if (typeName.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("PairingRequired", StringComparison.OrdinalIgnoreCase))
        {
            return pairedThisAttempt
                ? "Pairing finished, but Android did not trust the saved debugging key for the connection. Remove any old VisualCat entry under Wireless debugging > Paired devices, then pair again."
                : "Android no longer trusts VisualCat's saved Wireless debugging key. Remove the old VisualCat entry under Wireless debugging > Paired devices, then pair again with a new code.";
        }

        if (exception is global::Java.Lang.SecurityException)
        {
            return "Android blocked local Wireless debugging discovery on this device. Keep VisualCat in the foreground, verify Wireless debugging is on, and pair again if the saved connection still fails.";
        }

        return pairedThisAttempt
            ? "Pairing succeeded, but VisualCat could not connect to Android's Wireless debugging service. Keep Wireless debugging on and use the saved pairing connection."
            : "VisualCat could not connect with Android's saved Wireless debugging pairing. Turn Wireless debugging on or pair again with a new code.";
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
            LogFailure($"Wireless ADB stream close failed {reason}", exception);
        }
    }

    private static void DisconnectSafely(WirelessAdbConnectionManager manager, string reason)
    {
        try
        {
            if (manager.IsConnected)
            {
                global::Android.Util.Log.Info(LogTag, $"Disconnecting Wireless ADB {reason}.");
            }

            manager.Disconnect();
        }
        catch (Exception exception)
        {
            LogFailure($"Wireless ADB disconnect failed {reason}", exception);
        }
    }

    /// <summary>
    /// Closes LibADB (which destroys its decrypted private-key object), releases the JNI wrapper,
    /// and forgets the unusable manager. The encrypted identity remains on disk and will be
    /// decrypted into a fresh manager for the next explicit connection.
    /// </summary>
    private void CloseAndDiscardManager(string reason)
    {
        var manager = _manager;
        _manager = null;
        if (manager is null)
        {
            return;
        }

        try
        {
            global::Android.Util.Log.Info(LogTag, $"Closing Wireless ADB {reason}; decrypted key material will be destroyed.");
            manager.Close();
        }
        catch (Exception exception)
        {
            LogFailure($"Wireless ADB close failed {reason}", exception);
            DisconnectSafely(manager, $"as fallback after close failed {reason}");
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static void LogPairingFailure(Exception exception, string pairingCode)
    {
        // The third-party pairing implementation receives the six-digit code, so treat its
        // exception text as sensitive even though VisualCat never includes the code in its own
        // messages. Preserve the exception type and stack for troubleshooting while replacing
        // any exact occurrence before it reaches logcat or a diagnostic bundle.
        var detail = exception.ToString();
        if (!string.IsNullOrEmpty(pairingCode))
        {
            detail = detail.Replace(pairingCode, "[PAIRING_CODE_REDACTED]", StringComparison.Ordinal);
        }

        global::Android.Util.Log.Error(
            LogTag,
            $"Wireless ADB pairing failed: {exception.GetType().FullName}. Sanitized detail follows.\n{detail}");
    }

    private static void LogFailure(string operation, Exception exception)
    {
        global::Android.Util.Log.Error(
            LogTag,
            $"{operation}: {exception.GetType().FullName}: {exception.Message}\n{exception}");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Cancel every in-flight LibADB operation owned by this activity before starting cleanup.
        // RunInterruptibleJavaOperationAsync maps this token to Java thread interruption where the
        // library is waiting, so Activity teardown does not leave pairing/discovery workers behind.
        // The lifetime CTS intentionally remains undisposed: a worker that is already unwinding may
        // still observe its token after this method returns.
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Never wait for LibADB or an in-flight pairing/discovery operation on Android's UI thread.
        // The worker takes the same gate as every connection operation, then closes the manager so
        // its decrypted key is destroyed before the JNI wrapper is released.
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                CloseAndDiscardManager("asynchronously because the Android activity is being destroyed");
            }
            finally
            {
                _gate.Release();
            }
        });
        lock (_stateSync)
        {
            _captureLeased = false;
        }
        // Do not dispose _gate here. A Java ADB operation that was already running when the
        // Activity began shutting down can finish on a worker and execute its finally block
        // afterward. Keeping this tiny managed semaphore alive prevents a late Release() from
        // turning orderly Android lifecycle shutdown into an ObjectDisposedException.
        global::Android.Util.Log.Info(LogTag, "Wireless ADB capture service disposed.");
    }
}
