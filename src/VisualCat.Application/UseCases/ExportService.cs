using System.Globalization;
using System.Text;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Time;

namespace VisualCat.Application.UseCases;

public static class ExportService
{
    public static async Task ExportRawAsync(
        SessionSnapshot snapshot,
        string destination,
        TimeRange range,
        FilterSpec filter,
        EntryOrder order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rawPath = RequireRaw(snapshot);
        await using var source = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new AtomicDestination(destination);
        var cursor = (EntryCursor?)null;
        long generation = 1;
        do
        {
            var page = SessionQueryEngine.GetEntries(snapshot, range, filter, order, cursor, 4096, generation++, cancellationToken);
            foreach (var entry in page.Entries)
            {
                await CopySpanAsync(source, output.Stream, entry.Raw.Offset, entry.Raw.Length, cancellationToken).ConfigureAwait(false);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        await output.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task ExportRawContextAsync(
        SessionSnapshot snapshot,
        string destination,
        long sourceSequence,
        int before,
        int after,
        CancellationToken cancellationToken = default)
    {
        var rawPath = RequireRaw(snapshot);
        var records = SessionQueryEngine.GetRawContext(snapshot, sourceSequence, before, after);
        await using var source = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new AtomicDestination(destination);
        foreach (var record in records)
        {
            await CopySpanAsync(source, output.Stream, record.Raw.Offset, record.Raw.Length, cancellationToken).ConfigureAwait(false);
        }

        await output.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The number of data rows written, not counting the header.</returns>
    public static async Task<long> ExportNormalizedCsvAsync(
        SessionSnapshot snapshot,
        string destination,
        TimeRange range,
        FilterSpec filter,
        EntryOrder order,
        CancellationToken cancellationToken = default)
    {
        return await ExportNormalizedCsvAsync(
            snapshot,
            destination,
            range,
            filter,
            order,
            includeUtf8Bom: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The number of data rows written, not counting the header.</returns>
    /// <remarks>
    /// The count is returned so the caller can say how much was written. "Export CSV" is
    /// scoped to a time range, and a reader who has zoomed in has no way to tell a complete
    /// file from a truncated one by looking at it (finding 10).
    /// </remarks>
    public static async Task<long> ExportNormalizedCsvAsync(
        SessionSnapshot snapshot,
        string destination,
        TimeRange range,
        FilterSpec filter,
        EntryOrder order,
        bool includeUtf8Bom,
        CancellationToken cancellationToken = default)
    {
        var written = 0L;
        await using var output = new AtomicDestination(destination);
        await using (var writer = new StreamWriter(output.Stream, new UTF8Encoding(includeUtf8Bom), 1024 * 1024, leaveOpen: true))
        {
            await writer.WriteLineAsync("timestamp_utc,level,pid,tid,buffer,tag,template_id,message").ConfigureAwait(false);
            EntryCursor? cursor = null;
            long generation = 1;
            do
            {
                var page = SessionQueryEngine.GetEntries(snapshot, range, filter, order, cursor, 4096, generation++, cancellationToken);
                foreach (var entry in page.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written++;
                    var values = new[]
                    {
                        entry.Timestamp?.ToString() ?? string.Empty,
                        entry.Level.ToString(),
                        entry.Pid.ToString(CultureInfo.InvariantCulture),
                        entry.Tid.ToString(CultureInfo.InvariantCulture),
                        entry.Buffer,
                        entry.Tag,
                        entry.TemplateId.ToString(CultureInfo.InvariantCulture),
                        entry.Message,
                    };
                    await writer.WriteLineAsync(string.Join(',', values.Select(EscapeCsv))).ConfigureAwait(false);
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null);

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await output.CommitAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    public static async Task ExportTemplateReportAsync(
        SessionSnapshot snapshot,
        string destination,
        TimeRange range,
        FilterSpec filter,
        bool markdown,
        CancellationToken cancellationToken = default)
    {
        var templates = SessionQueryEngine.QueryTopTemplates(
            snapshot,
            range,
            filter,
            int.MaxValue,
            1,
            cancellationToken: cancellationToken);
        await using var output = new AtomicDestination(destination);
        await using (var writer = new StreamWriter(output.Stream, new UTF8Encoding(true), 64 * 1024, leaveOpen: true))
        {
            if (markdown)
            {
                await writer.WriteLineAsync("| Count | First | Last | Template |").ConfigureAwait(false);
                await writer.WriteLineAsync("|---:|---|---|---|").ConfigureAwait(false);
                foreach (var template in templates)
                {
                    await writer.WriteLineAsync(
                        $"| {template.Count} | {template.First} | {template.Last} | {template.CanonicalText.Replace("|", "\\|", StringComparison.Ordinal)} |").ConfigureAwait(false);
                }
            }
            else
            {
                await writer.WriteLineAsync("template_id,count,first,last,template").ConfigureAwait(false);
                foreach (var template in templates)
                {
                    await writer.WriteLineAsync(string.Join(',',
                        template.TemplateId.ToString(CultureInfo.InvariantCulture),
                        template.Count.ToString(CultureInfo.InvariantCulture),
                        EscapeCsv(template.First?.ToString() ?? string.Empty),
                        EscapeCsv(template.Last?.ToString() ?? string.Empty),
                        EscapeCsv(template.CanonicalText))).ConfigureAwait(false);
                }
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await output.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task ExportStatisticsAsync(
        SessionSnapshot snapshot,
        string destination,
        FilterSpec filter,
        bool markdown,
        CancellationToken cancellationToken = default)
    {
        var stats = SessionQueryEngine.QueryStatistics(snapshot, filter, 1, 100, cancellationToken);
        await using var output = new AtomicDestination(destination);
        await using (var writer = new StreamWriter(output.Stream, new UTF8Encoding(true), 64 * 1024, leaveOpen: true))
        {
            if (markdown)
            {
                await writer.WriteLineAsync($"# VisualCat statistics — {snapshot.Descriptor.DisplayName}").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync($"- Matching entries: {stats.TotalMatching:N0}").ConfigureAwait(false);
                await writer.WriteLineAsync($"- Time range: {stats.FirstInstant} — {stats.LastInstant}").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("| Level | Count |").ConfigureAwait(false);
                await writer.WriteLineAsync("|---|---:|").ConfigureAwait(false);
                foreach (var pair in stats.Levels)
                {
                    await writer.WriteLineAsync($"| {pair.Key} | {pair.Value} |").ConfigureAwait(false);
                }
            }
            else
            {
                await writer.WriteLineAsync("dimension,value,count").ConfigureAwait(false);
                foreach (var pair in stats.Levels)
                {
                    await writer.WriteLineAsync($"level,{pair.Key},{pair.Value}").ConfigureAwait(false);
                }

                foreach (var tag in stats.Tags)
                {
                    await writer.WriteLineAsync($"tag,{EscapeCsv(tag.Value)},{tag.Count}").ConfigureAwait(false);
                }
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await output.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string RequireRaw(SessionSnapshot snapshot)
    {
        if (snapshot.RawPath is not { } path || !File.Exists(path))
        {
            throw new InvalidOperationException("Raw source is unavailable; this session is open in degraded index-only mode.");
        }

        return path;
    }

    private static async Task CopySpanAsync(
        FileStream source,
        Stream destination,
        long offset,
        int length,
        CancellationToken cancellationToken)
    {
        source.Position = offset;
        var remaining = length;
        var buffer = new byte[Math.Min(1024 * 1024, Math.Max(1, length))];
        while (remaining > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Raw span extends beyond the available source.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private sealed class AtomicDestination : IAsyncDisposable
    {
        private readonly string _destination;
        private readonly string _temporary;
        private bool _committed;

        public AtomicDestination(string destination)
        {
            _destination = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(_destination) ?? ".");
            _temporary = _destination + $".tmp-{Guid.NewGuid():N}";
            Stream = new FileStream(
                _temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public FileStream Stream { get; }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Stream.Close();
            File.Move(_temporary, _destination, true);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
            if (!_committed)
            {
                File.Delete(_temporary);
            }
        }
    }
}
