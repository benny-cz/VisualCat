using System.Runtime.CompilerServices;
using System.Text;
using Android.Content.PM;
using VisualCat.Application.Ports;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Sessions;

namespace VisualCat.Android;

public sealed class OnDeviceLogSource : ILogSource, ISourceScopeReporter
{
    /// <summary>
    /// How long a <em>streaming</em> capture may go on delivering nothing but its own records
    /// before that is taken as proof that it is not seeing the device.
    /// </summary>
    /// <remarks>
    /// Long enough that a busy device's first burst has certainly arrived, and short enough
    /// that the reader is told before they have decided the app is broken.
    ///
    /// The clock starts at the first byte, not when the process is spawned, and that is the
    /// whole of the difference between a right answer and a wrong one. Android holds
    /// <c>logcat</c>'s output until the reader answers its consent sheet — three paragraphs
    /// and a <em>Learn more</em> link — so a clock started at spawn is timing how long a human
    /// takes to read a dialog, not how long a stream takes to prove itself. A tester who took
    /// 27 seconds over it got a full-device capture that the product called own-app-only for
    /// the rest of its life, in the status line, in the session name, in a red notice and in
    /// the session details, while recording 4,559 lines from across the whole device
    /// (audit 3, A1). The first byte is the moment the platform stopped waiting for the human,
    /// which is the moment the question becomes about the stream.
    /// </remarks>
    private static readonly TimeSpan ScopeDecisionWindow = TimeSpan.FromSeconds(8);

    /// <summary>Nothing has been decided yet.</summary>
    private const int ScopePending = 0;

    /// <summary>
    /// Own-app only — believed, not proven, and revisable.
    /// </summary>
    /// <remarks>
    /// The absence of a foreign record is evidence and never proof: it is what a restricted
    /// capture looks like and also what a quiet device looks like. So this verdict stays open
    /// to correction for the life of the capture.
    /// </remarks>
    private const int ScopeRestricted = 1;

    /// <summary>
    /// Full-device, and final: one record from another process cannot be un-seen.
    /// </summary>
    private const int ScopeFullDevice = 2;

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
    private readonly char[] _scopePrefix = new char[LogcatRecordOrigin.MaximumPrefixLength];
    private readonly Lock _scopeSync = new();
    private int _scopePrefixLength;
    private Timer? _scopeDeadline;
    private int _scopeVerdict = ScopePending;
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

    /// <summary>
    /// What this source is, in the words the session and its manifest are written with.
    /// </summary>
    /// <remarks>
    /// Settable because the answer changes: the description a capture starts with is the most
    /// this source can honestly say before the platform has shown it anything, and
    /// <see cref="ResolveScope"/> replaces it once the stream has. Every descriptor the
    /// coordinator writes reads this afresh, so a corrected scope reaches the stored manifest
    /// too rather than only the status line (audit 3, A1). Written from the read loop or the
    /// deadline timer and read from the ingest thread; a reference assignment is atomic and
    /// the record it publishes is immutable, so a reader sees one description or the other and
    /// never half of a new one.
    /// </remarks>
    public SourceMetadata Metadata { get; private set; }

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
        //
        // -D is what makes the merged stream self-describing. Ordinary threadtime records carry
        // no buffer field, and without it logcat announces each buffer once, at the start; the
        // ingest latched the last announcement and stamped it on everything after, so a capture
        // attributed about 80% of its records to a buffer they had never been in
        // (finding F-12). With -D, logcat prints "--------- switch to <buffer>" on every
        // crossing, so each record's buffer is the last divider before it — which is exactly
        // what the ingest already assumes.
        var arguments = new[]
        {
            "logcat",
            "-b", "all",
            "-D",
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

        // A source that already knows it cannot see the device says so at once. One that
        // still might says nothing yet: the clock that decides is started by the first byte,
        // further down, because until then the reader is looking at Android's consent sheet
        // and there is no stream to judge (audit 3, A1).
        if (!_permissionHeld)
        {
            ResolveScope(fullDevice: false, declined: false);
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

                // The first byte is the platform saying it has finished asking the reader and
                // started streaming. Everything after this is evidence about the stream, which
                // is what the deadline is for.
                if (chunksRead == 0 && _permissionHeld)
                {
                    StartScopeDeadline();
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);

                // Only a capture that could have been allowed the device still has a scope
                // question open. Without READ_LOGS logd never hands over another uid's
                // records, so there is nothing in this stream to find — and every line
                // inspected is one more chance to find it wrongly.
                if (_permissionHeld)
                {
                    InspectScope(chunk);
                }

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
    ///
    /// This keeps reading after a restricted verdict, and that is deliberate: the restricted
    /// verdict is the one made from an absence, and a foreign pid arriving later is proof it
    /// was wrong. Correcting it costs one comparison per line; being wrong costs the reader
    /// the rest of the session (audit 3, A1). Only the full-device verdict stops the work,
    /// because nothing can overturn it.
    ///
    /// Called only while the app holds READ_LOGS, because only then is there anything to find.
    /// </remarks>
    private void InspectScope(ReadOnlySpan<byte> chunk)
    {
        if (Volatile.Read(ref _scopeVerdict) == ScopeFullDevice)
        {
            return;
        }

        foreach (var value in chunk)
        {
            if (value is (byte)'\n' or (byte)'\r')
            {
                if (_scopePrefixLength > 0)
                {
                    var foreign = IsForeignRecord(_scopePrefix.AsSpan(0, _scopePrefixLength));
                    _scopePrefixLength = 0;
                    if (foreign)
                    {
                        ResolveScope(fullDevice: true, declined: false);
                        return;
                    }
                }

                continue;
            }

            // Only the prefix is kept. The pid is settled well before the message begins, and
            // there is no other reason to hold on to a record — see
            // LogcatRecordOrigin.MaximumPrefixLength for why the cut falls where it does. Every
            // byte this early in a record is ASCII, so widening it to a char is exact.
            if (_scopePrefixLength < _scopePrefix.Length)
            {
                _scopePrefix[_scopePrefixLength++] = (char)value;
            }
        }
    }

    /// <summary>Whether a threadtime record was written by some process other than this one.</summary>
    /// <remarks>
    /// The reading belongs to <see cref="LogcatRecordOrigin"/> so that this source and the
    /// parser cannot come to disagree about where in a record a pid is — which is precisely how
    /// this once read the zone offset that <c>-v UTC</c> inserts as a pid of 0 and called every
    /// record on the device, its own included, foreign. A record that cannot be read says
    /// nothing either way.
    /// </remarks>
    private bool IsForeignRecord(ReadOnlySpan<char> record) =>
        LogcatRecordOrigin.TryReadProcessId(record, out var pid) && pid != _ownPid;

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

    /// <summary>
    /// Publishes what the capture can see, and revises it if the stream later says otherwise.
    /// </summary>
    /// <remarks>
    /// Full-device wins from any state and settles the question; restricted is only ever a
    /// first answer, and never overwrites a full-device one. That asymmetry is the shape of
    /// the evidence: a foreign record proves reach, while no foreign record proves nothing at
    /// all, and the old code latched both alike and could not take either back.
    /// </remarks>
    private void ResolveScope(bool fullDevice, bool declined)
    {
        // logd answers a reader according to that reader's own permissions, so an app without
        // READ_LOGS is never handed another uid's records: full-device is not a thing the
        // stream can talk this source into believing, whatever it appears to say. The rule sits
        // here rather than at the one call site so that no later reader of the stream can undo
        // it, and it is logged rather than passed over because arriving here at all would mean
        // the record reader had begun seeing things.
        if (fullDevice && !_permissionHeld)
        {
            global::Android.Util.Log.Warn(
                "VisualCat",
                "Ignoring a full-device scope claim from a capture that does not hold READ_LOGS.");
            return;
        }

        var wanted = fullDevice ? ScopeFullDevice : ScopeRestricted;
        var previous = fullDevice
            ? Interlocked.Exchange(ref _scopeVerdict, ScopeFullDevice)
            : Interlocked.CompareExchange(ref _scopeVerdict, ScopeRestricted, ScopePending);
        if (previous == wanted || previous == ScopeFullDevice)
        {
            return;
        }

        if (fullDevice)
        {
            // Nothing left to decide, so nothing left to time.
            CancelScopeDeadline();
        }

        var report = fullDevice
            ? new SourceScopeReport(true, "On-device full-device logcat", null, null)
            : new SourceScopeReport(
                false,
                "On-device own-app logcat",
                declined ? "log access was not allowed" : "own-app scope only",
                declined ? DeclinedRemedy : NotGrantedRemedy);

        // The session's own record of what it is, so a scope corrected two seconds into a
        // capture is what the manifest ends up holding.
        Metadata = Metadata with
        {
            Description = report.Description,
            Properties = WithScope(Metadata.Properties, fullDevice ? "full-device" : "own-app"),
        };
        global::Android.Util.Log.Info(
            "VisualCat",
            previous == ScopePending
                ? $"On-device logcat scope resolved: fullDevice={fullDevice}, declined={declined}."
                : $"On-device logcat scope corrected to fullDevice={fullDevice}: a record from another process arrived after the deadline.");
        ScopeResolved?.Invoke(report);
    }

    private static Dictionary<string, string>? WithScope(
        IReadOnlyDictionary<string, string>? properties,
        string scope)
    {
        if (properties is null)
        {
            return null;
        }

        return new Dictionary<string, string>(properties, StringComparer.Ordinal)
        {
            ["scope"] = scope,
        };
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
