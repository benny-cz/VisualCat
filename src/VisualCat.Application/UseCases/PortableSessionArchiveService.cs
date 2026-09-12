using System.IO.Compression;
using VisualCat.Core.Store;

namespace VisualCat.Application.UseCases;

public static class PortableSessionArchiveService
{
    private const int MaximumEntries = 100_000;

    /// <summary>
    /// The ceiling on everything one archive may expand to.
    /// </summary>
    /// <remarks>
    /// A terabyte is not a bound: a 1.1 MB archive declaring a 1 GiB member wrote all of it into
    /// the product's own data root without objection (finding F-21). Sessions are large but not
    /// unbounded — a million-line capture with embedded raw evidence is a few gigabytes — so this
    /// is set where a real session fits comfortably and a bomb does not.
    /// </remarks>
    private const long MaximumExpandedBytes = 64L * 1024 * 1024 * 1024;

    /// <summary>How much one member may expand to beyond its compressed size.</summary>
    /// <remarks>
    /// Column files of repeated values compress extremely well, so the ratio has to be generous;
    /// what it stops is the member that is nothing but a compressible pattern. Applied alongside
    /// the member allowlist rather than instead of it: a name the session format does not define
    /// is refused whatever it would expand to.
    /// </remarks>
    private const long MaximumEntryExpansionRatio = 1_000;

    /// <summary>Below this, a member is too small for its ratio to mean anything.</summary>
    private const long RatioExemptBytes = 64 * 1024;

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

            // The archive carries the session's embedded raw evidence, so it is owner-only for
            // the same reason the evidence itself is (F-27).
            SessionFileModes.MakeFileOwnerOnly(output);
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
        SessionFileModes.CreateOwnerOnlyDirectory(temporary);
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

                // Only files the session format defines are written. A bomb payload, a
                // 200-directory-deep path and a 240-character name are all the same defect seen
                // three ways — none of them is part of a session — and naming the format's own
                // members is the bound that needs no tuning (finding F-21).
                if (!SessionLayout.IsKnownMember(entry.FullName))
                {
                    throw new InvalidDataException(
                        $"Portable archive contains an entry that is not part of a session: {entry.FullName}");
                }

                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaximumExpandedBytes)
                {
                    throw new InvalidDataException(
                        $"Portable archive expands to more than {MaximumExpandedBytes / (1024 * 1024 * 1024):N0} GiB, " +
                        "which is beyond what a session can legitimately contain.");
                }

                if (entry.Length > RatioExemptBytes &&
                    entry.CompressedLength > 0 &&
                    entry.Length / entry.CompressedLength > MaximumEntryExpansionRatio)
                {
                    throw new InvalidDataException(
                        $"Portable archive entry '{entry.FullName}' expands {entry.Length / entry.CompressedLength:N0}-fold, " +
                        $"beyond the {MaximumEntryExpansionRatio:N0}:1 limit.");
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

                SessionFileModes.CreateOwnerOnlyDirectory(Path.GetDirectoryName(path) ?? temporary);
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
