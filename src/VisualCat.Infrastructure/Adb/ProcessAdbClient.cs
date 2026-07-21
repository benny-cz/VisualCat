using System.Diagnostics;
using System.Globalization;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;

namespace VisualCat.Infrastructure.Adb;

public sealed class ProcessAdbClient : IAdbClient
{
    public ProcessAdbClient(string executablePath)
    {
        ExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException("ADB executable was not found.", ExecutablePath);
        }
    }

    public string ExecutablePath { get; }

    public async Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        await using var process = StartProcess(arguments);
        using var outputReader = new StreamReader(process.StandardOutput, leaveOpen: true);
        var stdout = outputReader.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new AdbCommandResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["devices", "-l"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new IOException($"ADB device discovery failed: {result.StandardError.Trim()}");
        }

        return AdbDeviceParser.Parse(result.StandardOutput);
    }

    public IAdbProcess StartProcess(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("ADB process could not be started.");
        }

        return new ProcessAdapter(process);
    }

    private sealed class ProcessAdapter(Process process) : IAdbProcess
    {
        private int _disposed;

        public Stream StandardOutput => process.StandardOutput.BaseStream;
        public TextReader StandardError => process.StandardError;
        public int ExitCode => process.ExitCode;
        public bool HasExited => process.HasExited;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
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
                process.Dispose();
            }
        }
    }
}

public static class AdbDeviceParser
{
    public static IReadOnlyList<AdbDevice> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var devices = new List<AdbDevice>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("List of devices", StringComparison.Ordinal))
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
            {
                continue;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in tokens.Skip(2))
            {
                var colon = token.IndexOf(':');
                if (colon > 0)
                {
                    properties[token[..colon]] = token[(colon + 1)..];
                }
            }

            devices.Add(new AdbDevice(
                tokens[0],
                tokens[1] switch
                {
                    "device" => AdbDeviceState.Device,
                    "unauthorized" => AdbDeviceState.Unauthorized,
                    "offline" => AdbDeviceState.Offline,
                    _ => AdbDeviceState.Unknown,
                },
                properties.GetValueOrDefault("model"),
                properties.GetValueOrDefault("product"),
                properties.GetValueOrDefault("transport_id"),
                properties));
        }

        return devices;
    }
}

public static class AdbProcessParser
{
    public static IReadOnlyList<ProcessNameRange> Parse(string output, InstantUs observedAt)
    {
        ArgumentNullException.ThrowIfNull(output);
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pidIndex = Array.FindIndex(header, static value => value.Equals("PID", StringComparison.OrdinalIgnoreCase));
        var nameIndex = Array.FindIndex(
            header,
            static value => value.Equals("NAME", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("CMD", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("COMMAND", StringComparison.OrdinalIgnoreCase));
        var start = pidIndex >= 0 ? 1 : 0;
        var result = new List<ProcessNameRange>();
        var seen = new HashSet<(int Pid, string Name)>();
        for (var lineIndex = start; lineIndex < lines.Length; lineIndex++)
        {
            var columns = lines[lineIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var effectivePidIndex = pidIndex >= 0 ? pidIndex : Array.FindIndex(
                columns,
                static value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
            if (effectivePidIndex < 0 ||
                effectivePidIndex >= columns.Length ||
                !int.TryParse(columns[effectivePidIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
                pid <= 0)
            {
                continue;
            }

            var effectiveNameIndex = nameIndex >= 0 && nameIndex < columns.Length ? nameIndex : columns.Length - 1;
            var name = columns[effectiveNameIndex];
            if (string.IsNullOrWhiteSpace(name) || name.Length > 4096 || !seen.Add((pid, name)))
            {
                continue;
            }

            result.Add(new ProcessNameRange(pid, name, observedAt, observedAt));
        }

        return result;
    }
}
