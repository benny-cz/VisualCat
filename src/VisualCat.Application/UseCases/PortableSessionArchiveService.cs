using System.IO.Compression;
using VisualCat.Core.Store;

namespace VisualCat.Application.UseCases;

public static class PortableSessionArchiveService
{
    private const int MaximumEntries = 100_000;
    private const long MaximumExpandedBytes = 1L * 1024 * 1024 * 1024 * 1024;

    public static Task CreateAsync(
        SessionSnapshot snapshot,
        string destination,
        CancellationToken cancellationToken = default) =>
        CreateAsync(snapshot, destination, progress: null, cancellationToken);

    /// <summary>Creates a portable archive while reporting exact progress per stage.</summary>
    public static async Task CreateAsync(
        SessionSnapshot snapshot,
        string destination,
        IProgress<FileWorkProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var output = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        var buildDirectory = output + $".build-{Guid.NewGuid():N}";
        var temporaryArchive = output + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await SessionSaveService.SaveAsync(snapshot, buildDirectory, portable: true, progress, cancellationToken)
                .ConfigureAwait(false);
            await using (var stream = new FileStream(
                             temporaryArchive,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                var files = Directory.EnumerateFiles(buildDirectory, "*", SearchOption.AllDirectories).ToArray();
                if (files.Length > MaximumEntries)
                {
                    throw new InvalidDataException($"Portable session contains more than {MaximumEntries:N0} files.");
                }

                progress?.Report(new FileWorkProgress(FileWorkStage.CreatingArchive, 0, files.Length, "files"));
                var packed = 0L;
                foreach (var path in files.Order(StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new IOException("Portable sessions cannot contain symbolic links or reparse points.");
                    }

                    var relative = Path.GetRelativePath(buildDirectory, path).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    await using var outputStream = entry.Open();
                    await using var input = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
                    progress?.Report(new FileWorkProgress(
                        FileWorkStage.CreatingArchive,
                        ++packed,
                        files.Length,
                        "files"));
                }
            }

            progress?.Report(new FileWorkProgress(FileWorkStage.Publishing));
            await FileSystemPublish.MoveFileAsync(temporaryArchive, output, overwrite: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            File.Delete(temporaryArchive);
            throw;
        }
        finally
        {
            DeleteBuildDirectory(buildDirectory);
        }
    }

    public static Task ExtractAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(archivePath, destinationDirectory, progress: null, cancellationToken);

    /// <summary>Extracts a portable archive while reporting exact progress per entry.</summary>
    public static async Task ExtractAsync(
        string archivePath,
        string destinationDirectory,
        IProgress<FileWorkProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var source = Path.GetFullPath(archivePath);
        var destination = Path.GetFullPath(destinationDirectory);
        using var usage = SessionAccess.Write(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Archive destination already exists: {destination}");
        }

        var temporary = destination + $".extract-{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporary);
        try
        {
            await using var stream = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > MaximumEntries)
            {
                throw new InvalidDataException("Portable archive has an invalid entry count.");
            }

            long expandedBytes = 0;
            long extracted = 0;
            progress?.Report(new FileWorkProgress(
                FileWorkStage.ExtractingArchive,
                0,
                archive.Entries.Count,
                "entries"));
            var rootPrefix = temporary.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            void ReportExtractedEntry() => progress?.Report(new FileWorkProgress(
                FileWorkStage.ExtractingArchive,
                ++extracted,
                archive.Entries.Count,
                "entries"));

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name))
                {
                    ReportExtractedEntry();
                    continue;
                }

                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("Portable archive exceeds the expanded-size safety limit.");
                }

                if (entry.ExternalAttributes != 0 &&
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                {
                    throw new InvalidDataException("Portable archives cannot contain symbolic links.");
                }

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (relative == ".capture-identity" || relative.StartsWith(".capture-identity.", StringComparison.Ordinal))
                {
                    ReportExtractedEntry();
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(temporary, relative));
                if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Portable archive entry escapes the session root: {entry.FullName}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? temporary);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                ReportExtractedEntry();
            }

            progress?.Report(new FileWorkProgress(FileWorkStage.Verifying));
            var report = await SessionVerifier.VerifyAsync(temporary, verifyRawHash: true, cancellationToken)
                .ConfigureAwait(false);
            if (!report.IsValid)
            {
                throw new InvalidDataException(
                    "Portable archive verification failed: " +
                    string.Join("; ", report.Issues.Where(static issue => issue.IsError).Select(static issue => issue.Message)));
            }

            progress?.Report(new FileWorkProgress(FileWorkStage.Publishing));
            await FileSystemPublish.MoveDirectoryAsync(temporary, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            DeleteBuildDirectory(temporary);
            throw;
        }
    }

    private static void DeleteBuildDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        if (File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint) ||
            Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
                .Any(item => File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint)))
        {
            return;
        }

        Directory.Delete(full, true);
    }
}
