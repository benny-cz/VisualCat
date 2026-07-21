using System.Collections.Immutable;
using System.Text.Json;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.Application.UseCases;

public sealed record SessionViewState(
    string Name,
    TimeRange? Viewport,
    FilterSpec Filter,
    EntryOrder EntryOrder,
    bool FollowLatest);

public sealed record SessionViewCatalog(
    SessionViewState? Active,
    IReadOnlyList<SessionViewState> Presets)
{
    public static SessionViewCatalog Empty { get; } = new(null, []);
}

public sealed class SessionViewStore
{
    private const int MaximumFileBytes = 4 * 1024 * 1024;
    private const int MaximumPresets = 128;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;

    public SessionViewStore(string sessionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionRoot);
        _path = Path.Combine(Path.GetFullPath(sessionRoot), "view.json");
    }

    public async Task<SessionViewCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path) ||
                File.GetAttributes(_path).HasFlag(FileAttributes.ReparsePoint) ||
                new FileInfo(_path).Length > MaximumFileBytes)
            {
                return SessionViewCatalog.Empty;
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<PersistedDocument>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            if (document is null || document.Version != 1)
            {
                return SessionViewCatalog.Empty;
            }

            var active = document.Active is null ? null : FromPersisted(document.Active);
            var presets = (document.Presets ?? [])
                .Take(MaximumPresets)
                .Select(FromPersisted)
                .GroupBy(static view => view.Name, StringComparer.Ordinal)
                .Select(static group => group.Last())
                .ToArray();
            return new SessionViewCatalog(active, presets);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return SessionViewCatalog.Empty;
        }
    }

    public async Task SaveAsync(
        SessionViewState active,
        IReadOnlyList<SessionViewState> presets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(presets);
        if (presets.Count > MaximumPresets)
        {
            throw new ArgumentOutOfRangeException(nameof(presets), $"At most {MaximumPresets} saved views are allowed.");
        }

        Validate(active);
        foreach (var preset in presets)
        {
            Validate(preset);
        }

        if (File.Exists(_path) && File.GetAttributes(_path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Saved view path is a symbolic link or reparse point.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        var document = new PersistedDocument(
            1,
            ToPersisted(active),
            presets.Select(ToPersisted).ToArray());
        var temporary = _path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, _path, true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static SessionViewState FromPersisted(PersistedView persisted)
    {
        if (persisted.Filter is null)
        {
            throw new InvalidDataException("Saved view is missing its filter.");
        }

        ValidateName(persisted.Name);
        TimeRange? viewport = persisted.ViewportStartUs is { } start && persisted.ViewportEndUs is { } end
            ? new TimeRange(new InstantUs(start), new InstantUs(end))
            : null;
        TimeRange? filterRange = persisted.Filter.TimeStartUs is { } filterStart &&
                                 persisted.Filter.TimeEndUs is { } filterEnd
            ? new TimeRange(new InstantUs(filterStart), new InstantUs(filterEnd))
            : null;
        var search = persisted.Filter.Search is null
            ? null
            : new TextSearchSpec(
                persisted.Filter.Search.Query ?? string.Empty,
                persisted.Filter.Search.IsRegex,
                persisted.Filter.Search.CaseSensitive,
                persisted.Filter.Search.TimeoutMs is { } timeout
                    ? TimeSpan.FromMilliseconds(Math.Clamp(timeout, 1, 10_000))
                    : null);
        var filter = new FilterSpec
        {
            TimeRange = filterRange,
            IncludedLevels = (persisted.Filter.Levels ?? [])
                .Where(static value => Enum.IsDefined(value))
                .Take(10_000)
                .ToImmutableHashSet(),
            IncludedTags = Strings(persisted.Filter.IncludedTags),
            ExcludedTags = Strings(persisted.Filter.ExcludedTags),
            IncludedPids = Values<int>(persisted.Filter.Pids),
            ExcludedPids = Values<int>(persisted.Filter.ExcludedPids),
            IncludedProcesses = Strings(persisted.Filter.IncludedProcesses),
            ExcludedProcesses = Strings(persisted.Filter.ExcludedProcesses),
            IncludedTids = Values<int>(persisted.Filter.Tids),
            ExcludedTids = Values<int>(persisted.Filter.ExcludedTids),
            IncludedTemplates = Values<uint>(persisted.Filter.IncludedTemplates),
            ExcludedTemplates = Values<uint>(persisted.Filter.ExcludedTemplates),
            IncludedBuffers = Strings(persisted.Filter.Buffers),
            ExcludedBuffers = Strings(persisted.Filter.ExcludedBuffers),
            IncludedOutcomes = (persisted.Filter.Outcomes ?? [])
                .Where(static value => Enum.IsDefined(value))
                .Take(10_000)
                .ToImmutableHashSet(),
            Search = search,
        };
        return new SessionViewState(
            persisted.Name,
            viewport,
            filter,
            Enum.TryParse<EntryOrder>(persisted.EntryOrder, true, out var order) ? order : EntryOrder.Chronological,
            persisted.FollowLatest);
    }

    private static PersistedView ToPersisted(SessionViewState view) =>
        new(
            view.Name,
            view.Viewport?.StartInclusive.Value,
            view.Viewport?.EndExclusive.Value,
            view.EntryOrder.ToString(),
            view.FollowLatest,
            new PersistedFilter(
                view.Filter.TimeRange?.StartInclusive.Value,
                view.Filter.TimeRange?.EndExclusive.Value,
                view.Filter.IncludedLevels.ToArray(),
                view.Filter.IncludedTags.ToArray(),
                view.Filter.ExcludedTags.ToArray(),
                view.Filter.IncludedPids.ToArray(),
                view.Filter.IncludedProcesses.ToArray(),
                view.Filter.ExcludedProcesses.ToArray(),
                view.Filter.IncludedTids.ToArray(),
                view.Filter.IncludedTemplates.ToArray(),
                view.Filter.ExcludedTemplates.ToArray(),
                view.Filter.IncludedBuffers.ToArray(),
                view.Filter.IncludedOutcomes.ToArray(),
                view.Filter.Search is { } search
                    ? new PersistedSearch(
                        search.Query,
                        search.IsRegex,
                        search.CaseSensitive,
                        search.RegexTimeout is { } timeout ? (int)timeout.TotalMilliseconds : null)
                    : null,
                view.Filter.ExcludedPids.ToArray(),
                view.Filter.ExcludedTids.ToArray(),
                view.Filter.ExcludedBuffers.ToArray()));

    private static ImmutableHashSet<T> Values<T>(T[]? values)
        where T : struct =>
        (values ?? []).Take(10_000).ToImmutableHashSet();

    private static ImmutableHashSet<string> Strings(string[]? values) =>
        (values ?? [])
        .Take(10_000)
        .Where(static value => value is not null && value.Length <= 4096)
        .ToImmutableHashSet(StringComparer.Ordinal);

    private static void Validate(SessionViewState view)
    {
        ValidateName(view.Name);
        if (view.Filter.Search?.Query.Length > 64 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(view), "Saved search text is too long.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
        {
            throw new InvalidDataException("Saved view names must contain 1–80 characters.");
        }
    }

    private sealed record PersistedDocument(int Version, PersistedView? Active, PersistedView[]? Presets);

    private sealed record PersistedView(
        string Name,
        long? ViewportStartUs,
        long? ViewportEndUs,
        string EntryOrder,
        bool FollowLatest,
        PersistedFilter Filter);

    private sealed record PersistedFilter(
        long? TimeStartUs,
        long? TimeEndUs,
        LogLevel[]? Levels,
        string[]? IncludedTags,
        string[]? ExcludedTags,
        int[]? Pids,
        string[]? IncludedProcesses,
        string[]? ExcludedProcesses,
        int[]? Tids,
        uint[]? IncludedTemplates,
        uint[]? ExcludedTemplates,
        string[]? Buffers,
        ParseOutcomeKind[]? Outcomes,
        PersistedSearch? Search,
        // Appended after v1 shipped: absent in older documents, where null means "no
        // exclusions" rather than a corrupt view (§11.7 minor migrations).
        int[]? ExcludedPids = null,
        int[]? ExcludedTids = null,
        string[]? ExcludedBuffers = null);

    private sealed record PersistedSearch(string? Query, bool IsRegex, bool CaseSensitive, int? TimeoutMs);
}
