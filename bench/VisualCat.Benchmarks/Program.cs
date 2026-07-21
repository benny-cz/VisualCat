using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VisualCat.Application.Coordination;
using VisualCat.Core.Query;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "Usage: VisualCat.Benchmarks <logcat-file> [iterations] [--output report.json] " +
        "[--min-lines-per-second N] [--max-heat-map-ms N]");
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
        var report = JsonSerializer.Serialize(new
        {
            input,
            bytes = source.Metadata.Length,
            entries = result.Snapshot.Descriptor.Counters.TimedEntries,
            ingestSeconds = ingest.TotalSeconds,
            linesPerSecond,
            megabytesPerSecond = (source.Metadata.Length ?? 0) / ingest.TotalSeconds / (1024 * 1024),
            bytesAllocatedPerLine = lines == 0 ? 0 : allocated / (double)lines,
            heatMapIterations = options.Iterations,
            averageHeatMapMilliseconds,
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

internal sealed record BenchmarkOptions(
    int Iterations,
    string? Output,
    double? MinimumLinesPerSecond,
    double? MaximumHeatMapMilliseconds)
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
                default:
                    throw new ArgumentException($"Unknown benchmark option '{name}'.");
            }
        }

        return new BenchmarkOptions(iterations, output, minimumLines, maximumHeatMap);
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
