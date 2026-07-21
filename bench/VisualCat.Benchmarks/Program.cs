using System.Diagnostics;
using System.Text.Json;
using VisualCat.Application.Coordination;
using VisualCat.Core.Query;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Files;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: VisualCat.Benchmarks <logcat-file> [iterations]");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var iterations = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 100;
var root = Path.Combine(Path.GetTempPath(), $"visualcat-bench-{Guid.NewGuid():N}.vcat");

// §19.4 requires allocation per line alongside wall-clock time. Throughput alone cannot
// distinguish "slow because of I/O" from "slow because every line allocates a dozen
// short-lived objects", and only the second is fixable in this code base.
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
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        _ = SessionQueryEngine.QueryHeatMap(result.Snapshot, new Viewport(range, 2000), FilterSpec.All, iteration);
    }

    query.Stop();
    var lines = result.Snapshot.Descriptor.Counters.SourceLines;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        input,
        bytes = source.Metadata.Length,
        entries = result.Snapshot.Descriptor.Counters.TimedEntries,
        ingestSeconds = ingest.TotalSeconds,
        linesPerSecond = lines / ingest.TotalSeconds,
        megabytesPerSecond = (source.Metadata.Length ?? 0) / ingest.TotalSeconds / (1024 * 1024),
        bytesAllocatedPerLine = lines == 0 ? 0 : allocated / (double)lines,
        heatMapIterations = iterations,
        averageHeatMapMilliseconds = query.Elapsed.TotalMilliseconds / iterations,
        workingSetBytes = Environment.WorkingSet,
    }, JsonSerializerOptions.Web));
}

Directory.Delete(root, true);
return 0;
