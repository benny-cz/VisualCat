namespace VisualCat.Infrastructure.Adb;

public enum AdbDeviceState
{
    Device,
    Unauthorized,
    Offline,

    /// <summary>
    /// The daemon can see the device but this account may not open it.
    /// </summary>
    /// <remarks>
    /// On Linux this is a <c>udev</c> rule or a group the account is not in, and it is the one
    /// transport state with a specific, actionable remedy. <c>adb</c> prints it as
    /// <c>no permissions (…)</c> — two words, followed by advisory prose — so a parser that
    /// takes the second whitespace token as the state reads <c>no</c> and calls it
    /// <see cref="Unknown"/>, which is the one answer that helps nobody (A-16).
    /// </remarks>
    NoPermissions,

    Unknown,
}

/// <summary>One device as the ADB daemon currently sees it.</summary>
/// <remarks>
/// <c>StateText</c> is exactly what the daemon printed, so a state this product does not model —
/// <c>recovery</c>, <c>sideload</c>, <c>bootloader</c>, <c>authorizing</c> — can still be named to
/// the reader rather than reported as "Unknown".
/// </remarks>
public sealed record AdbDevice(
    string Serial,
    AdbDeviceState State,
    string? Model,
    string? Product,
    string? TransportId,
    IReadOnlyDictionary<string, string> Properties,
    string StateText = "");

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
