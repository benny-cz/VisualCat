using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VisualCat.Application.Coordination;
using VisualCat.Application.UseCases;
using VisualCat.Core.Query;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "Usage: VisualCat.Benchmarks <logcat-file> [iterations] [--output report.json] " +
        "[--min-lines-per-second N] [--max-heat-map-ms N] [--min-export-entries-per-second N] " +
        "[--max-manifest-bytes N] [--max-bytes-per-line N]");
    return 2;
}

var options = BenchmarkOptions.Parse(args);
var input = Path.GetFullPath(args[0]);
var root = Path.Combine(Path.GetTempPath(), $"visualcat-bench-{Guid.NewGuid():N}.vcat");
try
{
    // §19.4 requires allocation per line alongside wall-clock time. Throughput alone
    // cannot distinguish I/O variance from per-entry allocation regressions.
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var overall = Stopwatch.StartNew();
    await using var source = new FileLogSource(input);
    var result = await SessionCoordinator.ImportAsync(
        source,
        root,
        new IngestSettings(
            null,
            "utf-8",
            TimestampPolicy.ForFile(source.Metadata.ReferenceInstant),
            new TemplateSettings(),
            SegmentEntries: 100_000));
    var ingest = overall.Elapsed;
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    using (result.Snapshot)
    {
        var range = result.Snapshot.TimedRange ?? throw new InvalidDataException("Benchmark source has no timed entries.");
        var query = Stopwatch.StartNew();
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            _ = SessionQueryEngine.QueryHeatMap(
                result.Snapshot,
                new Viewport(range, 2000),
                FilterSpec.All,
                iteration);
        }

        query.Stop();
        var lines = result.Snapshot.Descriptor.Counters.SourceLines;
        var linesPerSecond = lines / ingest.TotalSeconds;
        var averageHeatMapMilliseconds = query.Elapsed.TotalMilliseconds / options.Iterations;

        // Paged entry reads were quadratic in the number of pages, which no ingest or
        // heat-map measurement can see: both touch every entry exactly once. Export walks
        // the page cursor the way the phone's "Load all" does, so it is the metric that
        // would notice that coming back.
        var entries = result.Snapshot.Descriptor.Counters.TimedEntries;
        var exportPath = Path.Combine(Path.GetTempPath(), $"visualcat-bench-{Guid.NewGuid():N}.csv");
        double exportEntriesPerSecond;
        try
        {
            var export = Stopwatch.StartNew();
            await ExportService.ExportNormalizedCsvAsync(
                result.Snapshot,
                exportPath,
                result.Snapshot.TimedRange!.Value,
                FilterSpec.All,
                EntryOrder.Chronological);
            export.Stop();
            exportEntriesPerSecond = export.Elapsed.TotalSeconds <= 0 ? 0 : entries / export.Elapsed.TotalSeconds;
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }

        // The manifest was rewritten in full on every published snapshot, so its size is
        // the number that decided whether a long capture stayed openable.
        var manifestBytes = FileLength(Path.Combine(root, "manifest.json"));
        var templateSidecarBytes = FileLength(Path.Combine(root, "templates.jsonl"));
        var report = JsonSerializer.Serialize(new
        {
            input,
            bytes = source.Metadata.Length,
            entries,
            templates = result.Snapshot.Descriptor.Counters.Templates,
            tags = result.Snapshot.Tags.Count,
            ingestSeconds = ingest.TotalSeconds,
            linesPerSecond,
            megabytesPerSecond = (source.Metadata.Length ?? 0) / ingest.TotalSeconds / (1024 * 1024),
            bytesAllocatedPerLine = lines == 0 ? 0 : allocated / (double)lines,
            heatMapIterations = options.Iterations,
            averageHeatMapMilliseconds,
            exportEntriesPerSecond,
            manifestBytes,
            templateSidecarBytes,
            workingSetBytes = Environment.WorkingSet,
        }, JsonSerializerOptions.Web);
        Console.WriteLine(report);
        if (options.Output is { } output)
        {
            var outputPath = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, report + Environment.NewLine);
        }

        var failures = new List<string>();
        if (options.MinimumLinesPerSecond is { } minimum && linesPerSecond < minimum)
        {
            failures.Add($"ingest {linesPerSecond:N0} lines/s is below the {minimum:N0} lines/s floor");
        }

        if (options.MaximumHeatMapMilliseconds is { } maximum && averageHeatMapMilliseconds > maximum)
        {
            failures.Add($"heat map {averageHeatMapMilliseconds:N2} ms exceeds the {maximum:N2} ms ceiling");
        }

        if (options.MinimumExportEntriesPerSecond is { } exportFloor && exportEntriesPerSecond < exportFloor)
        {
            failures.Add(
                $"CSV export {exportEntriesPerSecond:N0} entries/s is below the {exportFloor:N0} entries/s floor");
        }

        if (options.MaximumManifestBytes is { } manifestCeiling && manifestBytes > manifestCeiling)
        {
            failures.Add($"manifest {manifestBytes:N0} B exceeds the {manifestCeiling:N0} B ceiling");
        }

        if (options.MaximumBytesPerLine is { } allocationCeiling &&
            lines > 0 &&
            allocated / (double)lines > allocationCeiling)
        {
            failures.Add(
                $"allocation {allocated / (double)lines:N0} bytes/line exceeds the {allocationCeiling:N0} bytes/line ceiling");
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("Performance gate failed: " + string.Join("; ", failures) + ".");
            return 4;
        }
    }
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

return 0;

static long FileLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

internal sealed record BenchmarkOptions(
    int Iterations,
    string? Output,
    double? MinimumLinesPerSecond,
    double? MaximumHeatMapMilliseconds,
    double? MinimumExportEntriesPerSecond,
    long? MaximumManifestBytes,
    double? MaximumBytesPerLine)
{
    public static BenchmarkOptions Parse(IReadOnlyList<string> arguments)
    {
        var index = 1;
        var iterations = 100;
        if (index < arguments.Count && !arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            iterations = ParsePositiveInt(arguments[index++], "iterations");
        }

        string? output = null;
        double? minimumLines = null;
        double? maximumHeatMap = null;
        double? minimumExport = null;
        long? maximumManifest = null;
        double? maximumBytesPerLine = null;
        while (index < arguments.Count)
        {
            var name = arguments[index++];
            if (index >= arguments.Count)
            {
                throw new ArgumentException($"{name} requires a value.");
            }

            var value = arguments[index++];
            switch (name)
            {
                case "--output":
                    output = value;
                    break;
                case "--min-lines-per-second":
                    minimumLines = ParsePositiveDouble(value, name);
                    break;
                case "--max-heat-map-ms":
                    maximumHeatMap = ParsePositiveDouble(value, name);
                    break;
                case "--min-export-entries-per-second":
                    minimumExport = ParsePositiveDouble(value, name);
                    break;
                case "--max-manifest-bytes":
                    maximumManifest = ParsePositiveInt(value, name);
                    break;
                case "--max-bytes-per-line":
                    maximumBytesPerLine = ParsePositiveDouble(value, name);
                    break;
                default:
                    throw new ArgumentException($"Unknown benchmark option '{name}'.");
            }
        }

        return new BenchmarkOptions(
            iterations,
            output,
            minimumLines,
            maximumHeatMap,
            minimumExport,
            maximumManifest,
            maximumBytesPerLine);
    }

    private static int ParsePositiveInt(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentOutOfRangeException(name, "Value must be a positive integer.");

    private static double ParsePositiveDouble(string value, string name) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) && parsed > 0
            ? parsed
            : throw new ArgumentOutOfRangeException(name, "Value must be a positive finite number.");
}
