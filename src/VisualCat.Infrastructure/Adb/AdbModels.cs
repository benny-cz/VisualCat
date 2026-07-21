namespace VisualCat.Infrastructure.Adb;

public enum AdbDeviceState
{
    Device,
    Unauthorized,
    Offline,
    Unknown,
}

public sealed record AdbDevice(
    string Serial,
    AdbDeviceState State,
    string? Model,
    string? Product,
    string? TransportId,
    IReadOnlyDictionary<string, string> Properties);

public sealed record AdbCommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// A capture could not be started at all. Carries an actionable message naming the
/// device state and the next step, per the error contract in §18.1.
/// </summary>
public sealed class AdbCaptureUnavailableException : Exception
{
    public AdbCaptureUnavailableException(string message)
        : base(message)
    {
    }

    public AdbCaptureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IAdbClient
{
    string ExecutablePath { get; }
    Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken);
    IAdbProcess StartProcess(IReadOnlyList<string> arguments);
}

public interface IAdbProcess : IAsyncDisposable
{
    Stream StandardOutput { get; }
    TextReader StandardError { get; }
    int ExitCode { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
