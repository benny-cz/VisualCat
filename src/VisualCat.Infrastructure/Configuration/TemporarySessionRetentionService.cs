namespace VisualCat.Infrastructure.Configuration;

public sealed record TemporarySessionInfo(
    string Path,
    DateTimeOffset UpdatedUtc,
    long SizeBytes,
    bool Finalized);

public sealed record TemporaryCleanupResult(
    IReadOnlyList<TemporarySessionInfo> Sessions,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> Errors)
{
    public long TotalBytes => Sessions.Sum(static session => session.SizeBytes);
}

public static class TemporarySessionRetentionService
{
    public static async Task<IReadOnlyList<TemporarySessionInfo>> ScanAsync(
        string cacheRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(cacheRoot);
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return [];
        }

        var sessions = new List<TemporarySessionInfo>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*.vcat", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await TryInspectAsync(root, directory, cancellationToken).ConfigureAwait(false);
            if (info is not null)
            {
                sessions.Add(info);
            }
        }

        return sessions
            .OrderByDescending(static session => session.UpdatedUtc)
            .ThenBy(static session => session.Path, PathComparer)
            .ToArray();
    }

    public static async Task<TemporaryCleanupResult> CleanupAsync(
        string cacheRoot,
        bool enabled,
        TimeSpan maximumAge,
        long? maximumTotalBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumAge, TimeSpan.Zero);

        if (maximumTotalBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTotalBytes));
        }

        var root = Path.GetFullPath(cacheRoot);
        var sessions = await ScanAsync(root, cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            return new TemporaryCleanupResult(sessions, [], []);
        }

        var delete = new HashSet<string>(PathComparer);
        var cutoff = now - maximumAge;
        foreach (var session in sessions.Where(session => session.UpdatedUtc < cutoff))
        {
            delete.Add(session.Path);
        }

        if (maximumTotalBytes is { } cap)
        {
            var retainedBytes = sessions.Where(session => !delete.Contains(session.Path)).Sum(static session => session.SizeBytes);
            foreach (var session in sessions.OrderBy(static session => session.UpdatedUtc))
            {
                if (retainedBytes <= cap)
                {
                    break;
                }

                if (delete.Add(session.Path))
                {
                    retainedBytes -= session.SizeBytes;
                }
            }
        }

        var deleted = new List<string>();
        var errors = new List<string>();
        foreach (var session in sessions.Where(session => delete.Contains(session.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ValidateDeletionTarget(root, session.Path);
                Directory.Delete(session.Path, recursive: true);
                deleted.Add(session.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{session.Path}: {exception.Message}");
            }
        }

        var remaining = sessions.Where(session => !deleted.Contains(session.Path, PathComparer)).ToArray();
        return new TemporaryCleanupResult(remaining, deleted, errors);
    }

    private static async Task<TemporarySessionInfo?> TryInspectAsync(
        string root,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateDirectChild(root, directory);
            if (ContainsReparsePoint(directory))
            {
                return null;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                stream,
                new System.Text.Json.JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken).ConfigureAwait(false);
            var rootElement = document.RootElement;
            var updated = rootElement.TryGetProperty("updatedUtc", out var updatedElement) &&
                          updatedElement.TryGetDateTimeOffset(out var parsedUpdated)
                ? parsedUpdated
                : Directory.GetLastWriteTimeUtc(directory);
            var finalized = rootElement.TryGetProperty("finalized", out var finalizedElement) &&
                            finalizedElement.ValueKind is System.Text.Json.JsonValueKind.True;
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                size = checked(size + new FileInfo(file).Length);
            }

            return new TemporarySessionInfo(Path.GetFullPath(directory), updated, size, finalized);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or OverflowException)
        {
            return null;
        }
    }

    private static bool ContainsReparsePoint(string root)
    {
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        return Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Any(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint));
    }

    private static void ValidateDeletionTarget(string root, string path)
    {
        ValidateDirectChild(root, path);
        if (ContainsReparsePoint(path))
        {
            throw new IOException("Refusing to delete a session containing a symbolic link or reparse point.");
        }
    }

    private static void ValidateDirectChild(string root, string path)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(canonicalPath), canonicalRoot, PathComparison) ||
            !canonicalPath.EndsWith(".vcat", PathComparison))
        {
            throw new IOException($"Path is not a direct .vcat child of the cache root: {canonicalPath}");
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
