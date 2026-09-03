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
    // Sessions written before sessionSizeBytes existed have to be measured, and a
    // finalized one is never rewritten, so that measurement would otherwise repeat on
    // the cold-start screen of every run. Persisting it beside the sessions makes the
    // second launch as cheap as the second scan within one launch.
    private const string SizeIndexFileName = ".session-sizes.json";
    private static readonly System.Text.Json.JsonSerializerOptions SizeIndexOptions = new() { MaxDepth = 8 };
    private static readonly Lock SizeCacheLock = new();
    private static readonly Dictionary<string, SizeCacheEntry> SizeCache = new(PathComparer);
    private static readonly HashSet<string> SeededRoots = new(PathComparer);

    public static Task<IReadOnlyList<TemporarySessionInfo>> ScanAsync(
        string cacheRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(cacheRoot);
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return Task.FromResult<IReadOnlyList<TemporarySessionInfo>>([]);
        }

        // Callers rebuild controls after this completes. Starting the filesystem work on
        // the pool guarantees that even a synchronously completed manifest read cannot
        // make a cache walk monopolise the dispatcher.
        return Task.Run(() => ScanCoreAsync(root, cancellationToken), cancellationToken);
    }

    private static async Task<IReadOnlyList<TemporarySessionInfo>> ScanCoreAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var sessions = new System.Collections.Concurrent.ConcurrentBag<TemporarySessionInfo>();
        var directories = Directory.EnumerateDirectories(root, "*.vcat", SearchOption.TopDirectoryOnly).ToArray();
        SeedSizeIndex(root);
        await Parallel.ForEachAsync(
            directories,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 },
            async (directory, token) =>
            {
                var info = await TryInspectAsync(root, directory, token).ConfigureAwait(false);
                if (info is not null)
                {
                    sessions.Add(info);
                }
            }).ConfigureAwait(false);

        PersistSizeIndex(root, directories);

        return sessions
            .OrderByDescending(static session => session.UpdatedUtc)
            .ThenBy(static session => session.Path, PathComparer)
            .ToArray();
    }

    /// <summary>
    /// Loads previously measured sizes for this root, once per process. Every failure is
    /// swallowed: the index is an optimisation, and a scan must still work without it.
    /// </summary>
    private static void SeedSizeIndex(string root)
    {
        lock (SizeCacheLock)
        {
            if (!SeededRoots.Add(root))
            {
                return;
            }
        }

        try
        {
            var info = new FileInfo(Path.Combine(root, SizeIndexFileName));
            if (!info.Exists ||
                info.Length > 16 * 1024 * 1024 ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }

            using var stream = new FileStream(
                info.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var entries = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, PersistedSize?>>(
                stream,
                SizeIndexOptions);
            if (entries is null)
            {
                return;
            }

            lock (SizeCacheLock)
            {
                foreach (var (name, value) in entries)
                {
                    // The index names direct children only. A separator or a traversal
                    // segment means the file was edited by something else.
                    if (value is null ||
                        value.Size < 0 ||
                        value.ManifestLength < 0 ||
                        string.IsNullOrEmpty(name) ||
                        !string.Equals(name, Path.GetFileName(name), PathComparison) ||
                        !name.EndsWith(".vcat", PathComparison))
                    {
                        continue;
                    }

                    SizeCache[Path.Combine(root, name)] = new SizeCacheEntry(
                        value.ManifestLength,
                        new DateTime(value.ManifestTicksUtc, DateTimeKind.Utc),
                        value.Size);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                System.Text.Json.JsonException or NotSupportedException)
        {
        }
    }

    /// <summary>Writes measured sizes back, pruning sessions that no longer exist.</summary>
    private static void PersistSizeIndex(string root, IReadOnlyList<string> directories)
    {
        var temporary = string.Empty;
        try
        {
            var entries = new Dictionary<string, PersistedSize>(StringComparer.Ordinal);
            lock (SizeCacheLock)
            {
                foreach (var directory in directories)
                {
                    if (SizeCache.TryGetValue(directory, out var cached))
                    {
                        entries[Path.GetFileName(directory)] = new PersistedSize(
                            cached.ManifestLength,
                            cached.ManifestLastWriteUtc.Ticks,
                            cached.SizeBytes);
                    }
                }
            }

            var path = Path.Combine(root, SizeIndexFileName);
            if (entries.Count == 0)
            {
                // Every measured session is gone. Leaving the index behind would keep
                // sizes for directories that no longer exist.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entries);

            // Sessions whose manifest declares its own size never enter the cache, so a
            // steady state rewrites nothing. Comparing before writing keeps a scan that
            // learned nothing from touching the disk at all.
            var existing = new FileInfo(path);
            if (existing.Exists && existing.Length == json.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(json))
            {
                return;
            }

            temporary = Path.Combine(root, $".session-sizes.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, json);
            File.Move(temporary, path, overwrite: true);
            temporary = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                System.Text.Json.JsonException or NotSupportedException)
        {
            if (temporary.Length > 0)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    internal sealed record PersistedSize(long ManifestLength, long ManifestTicksUtc, long Size);

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
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
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
            var manifestInfo = new FileInfo(manifestPath);
            long size;
            if (rootElement.TryGetProperty("sessionSizeBytes", out var sizeElement) &&
                sizeElement.TryGetInt64(out var declaredSize) &&
                declaredSize >= 0)
            {
                size = declaredSize;
            }
            else if (TryGetCachedSize(directory, manifestInfo, out var cachedSize))
            {
                size = cachedSize;
            }
            else
            {
                size = MeasureDirectory(directory, cancellationToken);
                CacheSize(directory, manifestInfo, size);
            }

            return new TemporarySessionInfo(Path.GetFullPath(directory), updated, size, finalized);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or OverflowException)
        {
            return null;
        }
    }

    private static long MeasureDirectory(string directory, CancellationToken cancellationToken)
    {
        long size = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
        };
        // Enumerating through DirectoryInfo yields FileInfo objects already populated
        // from the directory scan; re-statting each path by name was eight times slower
        // for the same total.
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            size = checked(size + file.Length);
        }

        return size;
    }

    private static bool TryGetCachedSize(string directory, FileInfo manifest, out long size)
    {
        lock (SizeCacheLock)
        {
            if (SizeCache.TryGetValue(directory, out var cached) &&
                cached.ManifestLength == manifest.Length &&
                cached.ManifestLastWriteUtc == manifest.LastWriteTimeUtc)
            {
                size = cached.SizeBytes;
                return true;
            }
        }

        size = 0;
        return false;
    }

    private static void CacheSize(string directory, FileInfo manifest, long size)
    {
        lock (SizeCacheLock)
        {
            if (SizeCache.Count >= 8192)
            {
                SizeCache.Clear();
            }

            SizeCache[directory] = new SizeCacheEntry(manifest.Length, manifest.LastWriteTimeUtc, size);
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

    private readonly record struct SizeCacheEntry(long ManifestLength, DateTime ManifestLastWriteUtc, long SizeBytes);
}
