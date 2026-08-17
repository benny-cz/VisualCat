using System.Text.Json;
using VisualCat.Domain.Sessions;

namespace VisualCat.Core.Store;

public static class SessionStore
{
    /// <summary>
    /// Upper bound on segments in one manifest. Live compaction keeps a real session far
    /// below this (§10.4, §12.4); the limit exists so a corrupt or hostile manifest
    /// cannot make opening a session allocate without bound.
    /// </summary>
    private const int MaximumSegments = 100_000;

    /// <summary>
    /// How many times to re-read the manifest when a segment it lists has already been
    /// removed. Live compaction publishes a manifest that no longer references the
    /// segments it merged and then deletes them, so a reader that read the previous
    /// manifest a moment earlier can ask for a directory that is already gone. Re-reading
    /// resolves it; the alternative is a spurious failure during an otherwise healthy
    /// capture.
    /// </summary>
    private const int LiveReopenAttempts = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task<SessionSnapshot> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        OpenAsync(path, null, cancellationToken);

    /// <summary>
    /// Opens a session as an immutable snapshot.
    /// </summary>
    /// <param name="path">The session directory.</param>
    /// <param name="reuseFrom">
    /// An open snapshot of the same session whose segments may be shared with the new
    /// one. Segments are immutable once published, so a republished manifest usually
    /// differs from its predecessor by a handful of segments; sharing the rest is what
    /// keeps a live capture's descriptor count flat instead of doubling it on every
    /// refresh. The caller keeps ownership of <paramref name="reuseFrom"/> and must
    /// still dispose it.
    /// </param>
    /// <param name="cancellationToken">Cancels the open.</param>
    public static async Task<SessionSnapshot> OpenAsync(
        string path,
        SessionSnapshot? reuseFrom,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(path);
        var reusable = BuildReuseIndex(root, reuseFrom);
        for (var attempt = 1; ; attempt++)
        {
            var manifest = await ReadManifestAsync(root, cancellationToken).ConfigureAwait(false);
            var segments = new List<SegmentSnapshot>(manifest.Segments.Count);
            try
            {
                foreach (var segment in manifest.Segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    segments.Add(Acquire(root, segment, reusable));
                }

                return new SessionSnapshot(root, manifest, segments);
            }
            catch (Exception exception) when (
                !manifest.Finalized &&
                attempt < LiveReopenAttempts &&
                exception is FileNotFoundException or DirectoryNotFoundException)
            {
                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                await Task.Delay(20 * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                foreach (var segment in segments)
                {
                    segment.Dispose();
                }

                throw;
            }
        }
    }

    private static SegmentSnapshot Acquire(
        string root,
        SegmentManifest segment,
        Dictionary<string, SegmentSnapshot>? reusable)
    {
        if (reusable is not null &&
            reusable.TryGetValue(segment.RelativePath, out var existing) &&
            existing.Manifest.EntryCount == segment.EntryCount &&
            existing.TryAddReference())
        {
            return existing;
        }

        return new SegmentSnapshot(root, segment);
    }

    private static Dictionary<string, SegmentSnapshot>? BuildReuseIndex(string root, SessionSnapshot? reuseFrom)
    {
        if (reuseFrom is null ||
            !string.Equals(
                Path.TrimEndingDirectorySeparator(reuseFrom.RootPath),
                Path.TrimEndingDirectorySeparator(root),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var index = new Dictionary<string, SegmentSnapshot>(reuseFrom.Segments.Count, StringComparer.Ordinal);
        foreach (var segment in reuseFrom.Segments)
        {
            index[segment.Manifest.RelativePath] = segment;
        }

        return index;
    }

    private static async Task<SessionManifest> ReadManifestAsync(string root, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifestInfo = new FileInfo(manifestPath);
        if (!manifestInfo.Exists)
        {
            throw new FileNotFoundException("Session manifest was not found.", manifestPath);
        }

        if (manifestInfo.Length > 128 * 1024 * 1024)
        {
            throw new InvalidDataException("Session manifest exceeds the 128 MB safety limit.");
        }

        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            // A live capture republishes this manifest by atomic replace while readers are
            // open on it. Without FileShare.Delete a Windows reader blocks that replace
            // outright, so a progress snapshot opened at the wrong moment made the capture
            // itself fail to finalize with UnauthorizedAccessException — reliably on a
            // short capture, where the two coincide. Sharing delete lets the replace
            // proceed; this handle goes on reading the version it opened.
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<SessionManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Session manifest is empty.");
        ValidateManifest(manifest);
        if (!Version.TryParse(manifest.FormatVersion, out var version) || version.Major != 2)
        {
            throw new NotSupportedException($"Session format {manifest.FormatVersion} is not supported.");
        }

        if (!manifest.Source.Embedded && manifest.Source.Path is { } externalPath)
        {
            var external = new FileInfo(externalPath);
            var degraded = !external.Exists ||
                           external.Length != manifest.Source.Length ||
                           manifest.Source.LastWriteUtc is { } expected &&
                           external.LastWriteTimeUtc != expected.UtcDateTime;
            if (degraded)
            {
                manifest = manifest with { Descriptor = manifest.Descriptor with { Degraded = true } };
            }
        }

        return manifest;
    }

    private static void ValidateManifest(SessionManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.FormatVersion) ||
            manifest.Descriptor is null ||
            manifest.Source is null ||
            manifest.IngestSettings is null ||
            manifest.IngestSettings.TimestampPolicy is null ||
            manifest.IngestSettings.TemplateSettings is null ||
            manifest.Segments is null ||
            manifest.Tags is null ||
            manifest.Buffers is null ||
            manifest.Templates is null)
        {
            throw new InvalidDataException("Session manifest is missing required fields.");
        }

        if (manifest.Descriptor.Counters is null ||
            manifest.Descriptor.Defects is null ||
            manifest.Descriptor.TimestampPolicy is null ||
            manifest.Descriptor.TemplateSettings is null)
        {
            throw new InvalidDataException("Session descriptor is missing required fields.");
        }

        // Named separately from the other dimension checks: this is the one a real
        // session could once approach on its own, and "unreasonable dimensions" told a
        // user nothing about a capture that had simply run for a very long time.
        if (manifest.Segments.Count > MaximumSegments)
        {
            throw new InvalidDataException(
                $"This session declares {manifest.Segments.Count:N0} segments, more than the {MaximumSegments:N0} " +
                "VisualCat can open. The session directory is most likely damaged or was not written by VisualCat.");
        }

        if (manifest.Tags.Count > 10_000_000 ||
            manifest.Buffers.Count > 1_000_000 ||
            manifest.Templates.Count > 10_000_000 ||
            manifest.ProcessNames is { Count: > 10_000_000 })
        {
            throw new InvalidDataException("Session manifest declares unreasonable collection dimensions.");
        }

        if (manifest.Source.Length < 0 ||
            manifest.SnapshotGeneration < 0 ||
            manifest.Descriptor.Counters.TimedEntries < 0)
        {
            throw new InvalidDataException("Session manifest contains negative dimensions.");
        }

        var ids = new HashSet<int>();
        long totalEntries = 0;
        foreach (var segment in manifest.Segments)
        {
            if (segment is null ||
                segment.Id <= 0 ||
                !ids.Add(segment.Id) ||
                segment.EntryCount <= 0 ||
                segment.EntryCount > 50_000_000 ||
                string.IsNullOrWhiteSpace(segment.RelativePath) ||
                segment.MaximumTimestampUs < segment.MinimumTimestampUs ||
                segment.MaximumSequence < segment.MinimumSequence)
            {
                throw new InvalidDataException("Session manifest contains an invalid segment descriptor.");
            }

            totalEntries = checked(totalEntries + segment.EntryCount);
            if (totalEntries > 1_000_000_000)
            {
                throw new InvalidDataException("Session manifest exceeds the supported entry safety limit.");
            }
        }

        if (manifest.Tags.Any(static value => value is null) ||
            manifest.Buffers.Any(static value => value is null) ||
            manifest.Templates.Any(static value => value is null) ||
            manifest.ProcessNames?.Any(static value =>
                value is null ||
                value.Pid < 0 ||
                string.IsNullOrWhiteSpace(value.Name) ||
                value.Name.Length > 4096 ||
                value.LastSeen < value.FirstSeen) == true)
        {
            throw new InvalidDataException("Session manifest contains null table values.");
        }

        foreach (var ranges in (manifest.ProcessNames ?? [])
                     .GroupBy(static range => range.Pid))
        {
            ProcessNameRange? previous = null;
            foreach (var range in ranges.OrderBy(static range => range.FirstSeen))
            {
                if (previous is not null && range.FirstSeen <= previous.LastSeen)
                {
                    throw new InvalidDataException($"Session manifest contains overlapping process-name ranges for PID {range.Pid}.");
                }

                previous = range;
            }
        }
    }
}
