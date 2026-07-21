using System.Text.Json;
using VisualCat.Application.Ports;

namespace VisualCat.Infrastructure.Diagnostics;

public sealed class RollingDiagnosticLogger : IDiagnosticSink
{
    private const int MaximumEventBytes = 64 * 1024;
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly int _retainedFiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public RollingDiagnosticLogger(
        string directory,
        long maximumFileBytes = 5 * 1024 * 1024,
        int retainedFiles = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maximumFileBytes < 64 * 1024 || maximumFileBytes > 1024L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        if (retainedFiles is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        }

        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        if (File.GetAttributes(_directory).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("The diagnostics directory cannot be a symbolic link or reparse point.");
        }

        _maximumFileBytes = maximumFileBytes;
        _retainedFiles = retainedFiles;
    }

    public string DirectoryPath => _directory;

    public async ValueTask WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var sanitized = Sanitize(diagnosticEvent);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(sanitized);
        if (bytes.Length > MaximumEventBytes)
        {
            throw new InvalidDataException($"A diagnostic event exceeds the {MaximumEventBytes:N0}-byte safety limit.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = SelectPath(bytes.Length + 1);
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            RetainNewestFiles();
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private string SelectPath(int incomingBytes)
    {
        var prefix = $"visualcat-{DateTime.UtcNow:yyyyMMdd}";
        for (var index = 0; index < 10_000; index++)
        {
            var path = Path.Combine(_directory, $"{prefix}-{index:D3}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= _maximumFileBytes)
            {
                return path;
            }
        }

        throw new IOException("The diagnostics roll-file limit was exhausted.");
    }

    private void RetainNewestFiles()
    {
        foreach (var file in Directory
                     .EnumerateFiles(_directory, "visualcat-*.jsonl", SearchOption.TopDirectoryOnly)
                     .Select(static path => new FileInfo(path))
                     .OrderByDescending(static file => file.LastWriteTimeUtc)
                     .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
                     .Skip(_retainedFiles))
        {
            if (!file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                file.Delete();
            }
        }
    }

    private static DiagnosticEvent Sanitize(DiagnosticEvent value)
    {
        var level = Bound(value.Level, 32);
        var subsystem = Bound(value.Subsystem, 128);
        var name = Bound(value.Name, 128);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.Properties.Take(64))
        {
            var key = Bound(pair.Key, 128);
            properties[key] = Redact(key, Bound(pair.Value, 4096));
        }
        return value with
        {
            TimestampUtc = value.TimestampUtc.ToUniversalTime(),
            Level = level,
            Subsystem = subsystem,
            Name = name,
            Properties = properties,
        };
    }

    private static string Bound(string? value, int maximumLength)
    {
        value ??= string.Empty;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string Redact(string key, string value)
    {
        if (key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("serial", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("query", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("message", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("raw", StringComparison.OrdinalIgnoreCase))
        {
            return "<redacted>";
        }

        try
        {
            if (Path.IsPathFullyQualified(value))
            {
                return "<redacted>";
            }
        }
        catch (ArgumentException)
        {
            return "<redacted>";
        }

        return value;
    }
}
