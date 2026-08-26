using System.Text.Json;
using VisualCat.Core.Store;

namespace VisualCat.Application.UseCases;

public static class SessionSaveService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task SaveAsync(
        SessionSnapshot snapshot,
        string destination,
        bool portable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var sourceRoot = Path.GetFullPath(snapshot.RootPath);
        var destinationRoot = Path.GetFullPath(destination);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Directory.Exists(destinationRoot) || File.Exists(destinationRoot))
        {
            throw new IOException($"Destination already exists: {destinationRoot}");
        }

        var sourcePrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        if (destinationRoot.Equals(sourceRoot, comparison) || destinationRoot.StartsWith(sourcePrefix, comparison))
        {
            throw new IOException("The destination cannot be inside the source session.");
        }

        var parent = Path.GetDirectoryName(destinationRoot) ?? ".";
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".{Path.GetFileName(destinationRoot)}.tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            await CopyDirectoryAsync(sourceRoot, temporary, cancellationToken).ConfigureAwait(false);
            var manifest = snapshot.Manifest with { UpdatedUtc = DateTimeOffset.UtcNow };
            if (portable)
            {
                if (snapshot.RawPath is not { } rawPath || !File.Exists(rawPath))
                {
                    throw new InvalidOperationException("A portable session cannot be created because raw source data is unavailable.");
                }

                var rawDestination = Path.Combine(temporary, "raw.log");
                if (!File.Exists(rawDestination) &&
                    !Path.GetFullPath(rawPath).Equals(Path.GetFullPath(rawDestination), comparison))
                {
                    await CopyFileAsync(rawPath, rawDestination, cancellationToken).ConfigureAwait(false);
                }

                manifest = manifest with
                {
                    Source = manifest.Source with { Path = null, Embedded = true },
                };
            }

            await WriteManifestAsync(temporary, manifest, cancellationToken).ConfigureAwait(false);
            var verifyRaw = portable || snapshot.RawPath is { } sourcePath && File.Exists(sourcePath);
            var report = await SessionVerifier.VerifyAsync(temporary, verifyRaw, cancellationToken).ConfigureAwait(false);
            if (!report.IsValid)
            {
                throw new InvalidDataException(
                    $"Saved session verification failed: {string.Join("; ", report.Issues.Select(static issue => issue.Message))}");
            }

            await FileSystemPublish.MoveDirectoryAsync(temporary, destinationRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }

            throw;
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                           Path.DirectorySeparatorChar;
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(directory);
            var relative = Path.GetFullPath(directory)[sourcePrefix.Length..];
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(file);
            var relative = Path.GetFullPath(file)[sourcePrefix.Length..];
            await CopyFileAsync(file, Path.Combine(destination, relative), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void RejectLink(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"Session save refuses symbolic links and reparse points: {path}");
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteManifestAsync(
        string root,
        SessionManifest manifest,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "manifest.json");
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await FileSystemPublish.MoveFileAsync(temporary, path, overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
