using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using VisualCat.Application.Coordination;
using VisualCat.Application.UseCases;
using VisualCat.Core.Parsing;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain.Entries;
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
        "[--min-search-entries-per-second N] [--max-manifest-bytes N] [--max-bytes-per-line N]");
    return 2;
}

var options = BenchmarkOptions.Parse(args);
var input = Path.GetFullPath(args[0]);
var inputSha256 = await Sha256Async(input);
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
        var searchIterations = Math.Clamp(options.Iterations, 1, 10);
        var literalSearch = await MeasureSearchAsync(
            root,
            new TextSearchSpec("␟visualcat-benchmark-guaranteed-miss␟", IsRegex: false, CaseSensitive: true),
            searchIterations);
        var regexSearch = await MeasureSearchAsync(
            root,
            new TextSearchSpec(".", IsRegex: true, CaseSensitive: true, TimeSpan.FromMilliseconds(250)),
            searchIterations);
        var decodeIterations = Math.Clamp(options.Iterations, 1, 10);
        var validDecode = MeasureDecode("valid", Valid: true, decodeIterations);
        var invalidDecode = MeasureDecode("invalid", Valid: false, decodeIterations);
        var commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ??
                     TryReadProcessLine("git", "rev-parse", "HEAD") ??
                     "unknown";
        var workingTreeDirty = IsWorkingTreeDirty();
        var report = JsonSerializer.Serialize(new
        {
            commit,
            workingTreeDirty,
            sourceRevision = workingTreeDirty == true ? $"{commit}+dirty" : commit,
            arguments = args,
            machine = Environment.MachineName,
            cpu = CpuDescription(),
            sdk = TryReadProcessLine("dotnet", "--version") ?? "unknown",
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            input,
            inputSha256,
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
            search = new { literal = literalSearch, regex = regexSearch },
            decode = new { valid = validDecode, invalid = invalidDecode },
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

        if (options.MinimumSearchEntriesPerSecond is { } searchFloor)
        {
            var observed = Math.Min(literalSearch.MedianEntriesPerSecond, regexSearch.MedianEntriesPerSecond);
            if (observed < searchFloor)
            {
                failures.Add(
                    $"search {observed:N0} entries/s is below the {searchFloor:N0} entries/s floor");
            }
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

static async Task<string> Sha256Async(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

static string CpuDescription()
{
    try
    {
        if (OperatingSystem.IsWindows())
        {
            return (Registry.GetValue(
                       @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                       "ProcessorNameString",
                       null) as string)?.Trim() ??
                   $"{RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical processors";
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            var model = File.ReadLines("/proc/cpuinfo")
                .FirstOrDefault(static line => line.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
            var separator = model?.IndexOf(':') ?? -1;
            if (separator >= 0)
            {
                return model![(separator + 1)..].Trim();
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryReadProcessLine("sysctl", "-n", "machdep.cpu.brand_string") ??
                   $"{RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical processors";
        }
    }
    catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
    {
        // Metadata must never prevent the benchmark itself from running.
    }

    return $"{RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical processors";
}

static string? TryReadProcessLine(string fileName, params string[] arguments)
{
    if (!TryRunProcess(fileName, arguments, out var output))
    {
        return null;
    }

    return output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();
}

static bool? IsWorkingTreeDirty()
{
    return TryRunProcess(
        "git",
        ["status", "--porcelain=v1", "--untracked-files=normal"],
        out var output)
        ? !string.IsNullOrWhiteSpace(output)
        : null;
}

static bool TryRunProcess(string fileName, IReadOnlyList<string> arguments, out string output)
{
    output = string.Empty;
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // Drain both pipes while the child runs so a verbose tool cannot fill one and
        // deadlock the benchmark's metadata probe before the timeout is observed.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
            return false;
        }

        Task.WhenAll(outputTask, errorTask).GetAwaiter().GetResult();
        output = outputTask.Result.Trim();
        return process.ExitCode == 0;
    }
    catch (Exception exception) when (
        exception is InvalidOperationException or IOException or UnauthorizedAccessException or
                     System.ComponentModel.Win32Exception)
    {
        return false;
    }
}

static async Task<SearchBenchmarkResult> MeasureSearchAsync(
    string sessionRoot,
    TextSearchSpec search,
    int iterations)
{
    var samples = new List<SearchBenchmarkSample>(iterations + 1);
    long? expectedMatches = null;
    long entries = 0;
    for (var iteration = -1; iteration < iterations; iteration++)
    {
        using var snapshot = await SessionStore.OpenAsync(sessionRoot);
        entries = snapshot.Segments.Sum(static segment => (long)segment.Count);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var result = await SessionQueryEngine.SearchAsync(snapshot, search, FilterSpec.All, iteration + 2);
        timer.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        expectedMatches ??= result.Matches;
        if (result.Matches != expectedMatches)
        {
            throw new InvalidDataException(
                $"Search benchmark match count changed from {expectedMatches:N0} to {result.Matches:N0}.");
        }

        samples.Add(new SearchBenchmarkSample(
            Warmup: iteration < 0,
            ElapsedMilliseconds: timer.Elapsed.TotalMilliseconds,
            EntriesPerSecond: timer.Elapsed.TotalSeconds <= 0 ? 0 : entries / timer.Elapsed.TotalSeconds,
            AllocatedBytes: allocated,
            Matches: result.Matches));
    }

    var measured = samples.Where(static sample => !sample.Warmup).ToArray();
    return new SearchBenchmarkResult(
        search.Query,
        search.IsRegex,
        search.CaseSensitive,
        (search.RegexTimeout ?? TimeSpan.FromMilliseconds(250)).TotalMilliseconds,
        entries,
        expectedMatches ?? 0,
        Median(measured.Select(static sample => sample.EntriesPerSecond)),
        Percentile(measured.Select(static sample => sample.ElapsedMilliseconds), 0.95),
        Median(measured.Select(static sample => (double)sample.AllocatedBytes)),
        samples);
}

// Decode cost per parsed line, split by whether the line is well-formed UTF-8. Invalid bytes
// used to be detected by throwing and catching a DecoderFallbackException per line, which is
// a cliff no corpus of valid lines can expose: a device that emits one binary record per line
// pays it on every one of them. Both shapes are reported so a change that buys the invalid
// path with the valid path is visible too.
static DecodeBenchmarkResult MeasureDecode(string shape, bool Valid, int iterations)
{
    const int lines = 50_000;
    var sources = new SourceLine[lines];
    var sessionId = Guid.NewGuid();
    for (var index = 0; index < lines; index++)
    {
        var text = Encoding.UTF8.GetBytes($"D/Tag{index % 7}({1000 + (index % 9)}): message π {index} 猫");
        var bytes = Valid ? text : [.. text, (byte)0xc3];
        sources[index] = new SourceLine(sessionId, index, new RawSpan(index * 64L, bytes.Length), bytes);
    }

    var samples = new List<DecodeBenchmarkSample>(iterations + 1);
    long fallbacks = 0;
    for (var iteration = -1; iteration < iterations; iteration++)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        long marked = 0;
        foreach (var source in sources)
        {
            var outcome = LogcatParser.Parse(source, LogcatFormat.Brief);
            if (outcome.Fields?.Attributes.HasFlag(EntryAttributes.EncodingFallback) == true)
            {
                marked++;
            }
        }

        timer.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        if (iteration < 0)
        {
            fallbacks = marked;
        }
        else if (marked != fallbacks)
        {
            throw new InvalidDataException(
                $"Decode benchmark fallback count changed from {fallbacks:N0} to {marked:N0}.");
        }

        samples.Add(new DecodeBenchmarkSample(
            Warmup: iteration < 0,
            ElapsedMilliseconds: timer.Elapsed.TotalMilliseconds,
            NanosecondsPerLine: timer.Elapsed.TotalMilliseconds * 1_000_000 / lines,
            AllocatedBytes: allocated));
    }

    // Every line of the invalid shape must be marked, or the arm is not measuring the
    // fallback path at all; none of the valid shape may be.
    var expectedFallbacks = Valid ? 0 : lines;
    if (fallbacks != expectedFallbacks)
    {
        throw new InvalidDataException(
            $"Decode benchmark '{shape}' marked {fallbacks:N0} of {lines:N0} lines, expected {expectedFallbacks:N0}.");
    }

    var measured = samples.Where(static sample => !sample.Warmup).ToArray();
    return new DecodeBenchmarkResult(
        shape,
        lines,
        fallbacks,
        Median(measured.Select(static sample => sample.NanosecondsPerLine)),
        Percentile(measured.Select(static sample => sample.NanosecondsPerLine), 0.95),
        Median(measured.Select(static sample => (double)sample.AllocatedBytes)),
        samples);
}

static double Median(IEnumerable<double> values) => Percentile(values, 0.5);

static double Percentile(IEnumerable<double> values, double percentile)
{
    var sorted = values.Order().ToArray();
    if (sorted.Length == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
}

internal sealed record SearchBenchmarkSample(
    bool Warmup,
    double ElapsedMilliseconds,
    double EntriesPerSecond,
    long AllocatedBytes,
    long Matches);

internal sealed record SearchBenchmarkResult(
    string Query,
    bool IsRegex,
    bool CaseSensitive,
    double TimeoutMilliseconds,
    long Entries,
    long Matches,
    double MedianEntriesPerSecond,
    double P95ElapsedMilliseconds,
    double MedianAllocatedBytes,
    IReadOnlyList<SearchBenchmarkSample> Samples);

internal sealed record DecodeBenchmarkSample(
    bool Warmup,
    double ElapsedMilliseconds,
    double NanosecondsPerLine,
    long AllocatedBytes);

internal sealed record DecodeBenchmarkResult(
    string Shape,
    int Lines,
    long FallbackLines,
    double MedianNanosecondsPerLine,
    double P95NanosecondsPerLine,
    double MedianAllocatedBytes,
    IReadOnlyList<DecodeBenchmarkSample> Samples);

internal sealed record BenchmarkOptions(
    int Iterations,
    string? Output,
    double? MinimumLinesPerSecond,
    double? MaximumHeatMapMilliseconds,
    double? MinimumExportEntriesPerSecond,
    double? MinimumSearchEntriesPerSecond,
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
        double? minimumSearch = null;
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
                case "--min-search-entries-per-second":
                    minimumSearch = ParsePositiveDouble(value, name);
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
            minimumSearch,
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
