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

    /// <summary>
    /// The sessions a cleanup with this policy would delete, without deleting anything.
    /// </summary>
    /// <remarks>
    /// The irreversible confirmation said only that "sessions older than the configured age,
    /// and oldest sessions above the size cap, will be permanently removed" — not the policy
    /// values, not how many sessions, not how many bytes, not even whether the answer was
    /// zero. It asked the reader to run the policy in their head against a long cache list,
    /// and A-15 requires a delete action to name exactly what it will remove (finding F-23).
    /// The preview and the deletion share <see cref="SelectEligible"/>, so the sentence on the
    /// confirmation and the act it confirms cannot drift apart.
    /// </remarks>
    public static async Task<IReadOnlyList<TemporarySessionInfo>> PreviewAsync(
        string cacheRoot,
        TimeSpan maximumAge,
        long? maximumTotalBytes,
        DateTimeOffset now,
        IReadOnlySet<string>? protectedPaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumAge, TimeSpan.Zero);
        var sessions = await ScanAsync(Path.GetFullPath(cacheRoot), cancellationToken).ConfigureAwait(false);
        var eligible = SelectEligible(sessions, maximumAge, maximumTotalBytes, now, protectedPaths);
        return sessions.Where(session => eligible.Contains(session.Path)).ToArray();
    }

    /// <summary>
    /// Which stored sessions the policy makes eligible — the one rule, used by preview and
    /// by deletion alike.
    /// </summary>
    private static HashSet<string> SelectEligible(
        IReadOnlyList<TemporarySessionInfo> sessions,
        TimeSpan maximumAge,
        long? maximumTotalBytes,
        DateTimeOffset now,
        IReadOnlySet<string>? protectedPaths)
    {
        var delete = new HashSet<string>(PathComparer);

        // A session this app is writing into is not a candidate at any age: deleting the
        // folder under a running capture loses the capture and leaves the writer holding
        // handles into nothing.
        bool Protected(TemporarySessionInfo session) =>
            protectedPaths is { Count: > 0 } && protectedPaths.Contains(session.Path);

        var cutoff = now - maximumAge;
        foreach (var session in sessions.Where(session => session.UpdatedUtc < cutoff && !Protected(session)))
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

                if (!Protected(session) && delete.Add(session.Path))
                {
                    retainedBytes -= session.SizeBytes;
                }
            }
        }

        return delete;
    }

    public static async Task<TemporaryCleanupResult> CleanupAsync(
        string cacheRoot,
        bool enabled,
        TimeSpan maximumAge,
        long? maximumTotalBytes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => await CleanupAsync(cacheRoot, enabled, maximumAge, maximumTotalBytes, now, null, cancellationToken)
            .ConfigureAwait(false);

    public static async Task<TemporaryCleanupResult> CleanupAsync(
        string cacheRoot,
        bool enabled,
        TimeSpan maximumAge,
        long? maximumTotalBytes,
        DateTimeOffset now,
        IReadOnlySet<string>? protectedPaths,
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

        var delete = SelectEligible(sessions, maximumAge, maximumTotalBytes, now, protectedPaths);

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

    /// <summary>
    /// Deletes one explicitly chosen cached session after its UI has closed every handle.
    /// The same direct-child and reparse-point rules as policy cleanup apply; arbitrary paths
    /// can never be turned into a recursive delete target.
    /// </summary>
    public static void DeleteExactSession(string cacheRoot, string sessionPath)
    {
        var root = Path.GetFullPath(cacheRoot);
        var path = Path.GetFullPath(sessionPath);
        ValidateDeletionTarget(root, path);
        Directory.Delete(path, recursive: true);
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
