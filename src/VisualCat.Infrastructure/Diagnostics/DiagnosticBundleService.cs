using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VisualCat.Core.Store;

namespace VisualCat.Infrastructure.Diagnostics;

public static class DiagnosticBundleService
{
    private const long MaximumInputFileBytes = 32 * 1024 * 1024;
    private const long MaximumBundleInputBytes = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task CreateAsync(
        string diagnosticsDirectory,
        string destination,
        IEnumerable<string>? sessionDirectories = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var diagnosticsRoot = Path.GetFullPath(diagnosticsDirectory);
        var output = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        var temporary = output + $".tmp-{Guid.NewGuid():N}";
        long totalInput = 0;
        try
        {
            await using (var file = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
                await WriteTextAsync(
                    archive,
                    "SENSITIVE-DATA-WARNING.txt",
                    """
                    This bundle contains VisualCat application diagnostics and sanitized session metadata.
                    It intentionally excludes raw log messages, searches, source paths, hashes, and device serials.
                    Diagnostic metadata can still reveal timing, counts, operating-system details, and application behavior.
                    Review the archive before sharing it.
                    """,
                    cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(
                    archive,
                    "system.json",
                    new
                    {
                        createdUtc = DateTimeOffset.UtcNow,
                        os = Environment.OSVersion.VersionString,
                        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                        architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                        processorCount = Environment.ProcessorCount,
                    },
                    cancellationToken).ConfigureAwait(false);

                if (Directory.Exists(diagnosticsRoot) &&
                    !File.GetAttributes(diagnosticsRoot).HasFlag(FileAttributes.ReparsePoint))
                {
                    foreach (var path in Directory
                                 .EnumerateFiles(diagnosticsRoot, "visualcat-*.jsonl", SearchOption.TopDirectoryOnly)
                                 .Order(StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        totalInput = checked(totalInput + await AddBoundedFileAsync(
                            archive,
                            path,
                            $"logs/{Path.GetFileName(path)}",
                            cancellationToken).ConfigureAwait(false));
                        EnsureTotal(totalInput);
                    }
                }

                var manifestIndex = 0;
                foreach (var sessionDirectory in sessionDirectories ?? [])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var manifestPath = Path.Combine(Path.GetFullPath(sessionDirectory), "manifest.json");
                    if (!File.Exists(manifestPath) ||
                        File.GetAttributes(manifestPath).HasFlag(FileAttributes.ReparsePoint) ||
                        new FileInfo(manifestPath).Length > MaximumInputFileBytes)
                    {
                        continue;
                    }

                    await using var manifestStream = new FileStream(
                        manifestPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var manifest = await JsonSerializer.DeserializeAsync<SessionManifest>(
                        manifestStream,
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    if (manifest is null)
                    {
                        continue;
                    }

                    var sanitized = manifest with
                    {
                        Descriptor = manifest.Descriptor with
                        {
                            DisplayName = "<redacted>",
                            SourceDescription = "<redacted>",
                        },
                        Source = manifest.Source with
                        {
                            Path = null,
                            Sha256 = string.Empty,
                            LastWriteUtc = null,
                        },
                        Tags = manifest.Tags
                            .Select(static (_, index) => $"<redacted-tag-{index}>")
                            .ToArray(),
                        Templates = (manifest.Templates ?? [])
                            .Select(static template => template with
                            {
                                CanonicalText = "<redacted-template>",
                                Tokens = ["<redacted>"],
                                ContentHash = string.Empty,
                            })
                            .ToArray(),
                        // The bundle carries a redacted manifest and none of the session's
                        // files, so it must not go on naming a template sidecar either.
                        TemplateSidecarLength = null,
                        TemplateSidecarName = null,
                        ProcessNames = manifest.ProcessNames?
                            .Select(static process => process with { Name = "<redacted-process>" })
                            .ToArray(),
                    };
                    await WriteJsonAsync(
                        archive,
                        $"sessions/session-{manifestIndex++:D3}.json",
                        sanitized,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, output, true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static async Task<long> AddBoundedFileAsync(
        ZipArchive archive,
        string path,
        string entryName,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists ||
            info.Length > MaximumInputFileBytes ||
            info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return 0;
        }

        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var destination = entry.Open();
        await using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return info.Length;
    }

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string entryName,
        string text,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureTotal(long bytes)
    {
        if (bytes > MaximumBundleInputBytes)
        {
            throw new InvalidDataException($"Diagnostic bundle inputs exceed {MaximumBundleInputBytes:N0} bytes.");
        }
    }
}
