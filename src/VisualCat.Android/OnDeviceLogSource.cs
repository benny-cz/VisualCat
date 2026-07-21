using System.Runtime.CompilerServices;
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
        var arguments = new[] { "logcat", "-b", "main,system,crash", "-v", "threadtime,year,UTC,usec" };
        _process = Java.Lang.Runtime.GetRuntime()?.Exec(arguments)
            ?? throw new InvalidOperationException("Android logcat process could not be started.");
        using var registration = linked.Token.Register(static state => ((Java.Lang.Process)state!).Destroy(), _process);
        var input = _process.InputStream ?? throw new InvalidOperationException("Android logcat stdout is unavailable.");
        var buffer = new byte[256 * 1024];
        long offset = 0;
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
                break;
            }

            var chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
            yield return new SourceChunk(offset, chunk);
            offset += read;
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
