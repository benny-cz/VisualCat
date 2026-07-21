using System.Text.Json;
using VisualCat.Domain.Sessions;

namespace VisualCat.Core.Store;

public static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<SessionSnapshot> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(path);
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
            FileShare.Read,
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

        var segments = new List<SegmentSnapshot>(manifest.Segments.Count);
        try
        {
            foreach (var segment in manifest.Segments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                segments.Add(new SegmentSnapshot(root, segment));
            }

            return new SessionSnapshot(root, manifest, segments);
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

        if (manifest.Segments.Count > 10_000 ||
            manifest.Tags.Count > 10_000_000 ||
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
                segment.Checksums is null ||
                segment.Checksums.Count > 256 ||
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
