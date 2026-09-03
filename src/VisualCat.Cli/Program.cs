using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using VisualCat.Application.Coordination;
using VisualCat.Application.UseCases;
using VisualCat.Core.Generation;
using VisualCat.Core.Query;
using VisualCat.Core.Store;
using VisualCat.Domain;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Files;

return await VisualCatCli.RunAsync(args).ConfigureAwait(false);

internal static class VisualCatCli
{
    // Output-only options: names instead of bare enum ordinals and ISO-8601 instants.
    // Session manifests keep their own serializer settings, so the on-disk format is
    // unaffected (§16.6 — the CLI is a human and automation surface at once).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            new InstantUsJsonConverter(),
        },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "-v" or "--version" or "version")
        {
            Console.WriteLine($"vcat {ProductInfo.InformationalVersion}");
            return 0;
        }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var command = args[0].ToLowerInvariant();

            // Asking a command to explain itself must never be the same thing as running it.
            // "--help" was neither recognised nor rejected: it fell through to the command's
            // defaults, so `vcat generate-test-log --help` wrote a 90 MB file into the working
            // directory and said nothing (finding F-02). Handled here, before any command
            // runs, so it is true of every command rather than of the ones somebody
            // remembered.
            if (args[1..].Any(static value => value is "-h" or "--help" or "-?" or "/?"))
            {
                PrintCommandHelp(command);
                return 0;
            }

            var options = Arguments.Parse(args[1..]);

            // Same reason. An unrecognised option was silently ignored, so `--lines1000`
            // produced a million-line file instead of an error.
            options.RejectUnknown(command, KnownOptions(command));
            return command switch
            {
                "index" => await IndexAsync(options, cancellation.Token).ConfigureAwait(false),
                "info" => await InfoAsync(options, cancellation.Token).ConfigureAwait(false),
                "stats" => await StatsAsync(options, cancellation.Token).ConfigureAwait(false),
                "query" => await QueryAsync(options, cancellation.Token).ConfigureAwait(false),
                "search" => await SearchAsync(options, cancellation.Token).ConfigureAwait(false),
                "templates" => await TemplatesAsync(options, cancellation.Token).ConfigureAwait(false),
                "export" => await ExportAsync(options, cancellation.Token).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellation.Token).ConfigureAwait(false),
                "generate-test-log" => await GenerateAsync(options, cancellation.Token).ConfigureAwait(false),
                "adb-devices" => await AdbDevicesAsync(options, cancellation.Token).ConfigureAwait(false),
                "capture-adb" => await CaptureAdbAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => throw new CommandException($"Unknown command '{args[0]}'."),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (CommandException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            if (!ReferenceEquals(exception, exception.GetBaseException()))
            {
                Console.Error.WriteLine($"cause: {exception.GetBaseException().Message}");
            }

            if (Environment.GetEnvironmentVariable("VISUALCAT_DEBUG") == "1")
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static async Task<int> IndexAsync(Arguments options, CancellationToken cancellationToken)
    {
        var input = options.RequiredPosition(0, "index requires a log file path.");
        var output = options.Get("--output") ?? Path.GetFullPath(input) + ".vcat";
        if (Directory.Exists(output) && !options.Has("--force"))
        {
            throw new CommandException($"Output already exists: {output}. Use --force to replace it.");
        }

        if (Directory.Exists(output))
        {
            var full = Path.GetFullPath(output);
            if (!full.EndsWith(".vcat", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandException("--force only removes directories whose name ends in .vcat.");
            }

            if (Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true ||
                Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
                    .Prepend(full)
                    .Any(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)))
            {
                throw new CommandException("--force refuses filesystem roots and sessions containing symbolic links or reparse points.");
            }

            Directory.Delete(full, true);
        }

        await using var source = new FileLogSource(input);
        var settings = Settings(options, source.Metadata.ReferenceInstant);
        var progress = new Progress<ProgressSnapshot>(value =>
        {
            if (!Console.IsErrorRedirected)
            {
                Console.Error.Write(
                    $"\r{value.Stage,-11} {value.LinesCommitted,12:N0} lines {value.ThroughputLinesPerSecond,12:N0} lines/s");
            }
        });
        var result = await SessionCoordinator.ImportAsync(
            source,
            output,
            settings,
            progress,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        using (result.Snapshot)
        {
            if (!Console.IsErrorRedirected)
            {
                Console.Error.WriteLine();
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                session = result.Snapshot.SessionId,
                path = result.Snapshot.RootPath,
                format = result.Detection.PrimaryFormat,
                confidence = result.Detection.Confidence,
                entries = result.Snapshot.Descriptor.Counters.TimedEntries,
                untimed = result.Snapshot.Descriptor.Counters.UntimedEntries,
                unknown = result.Snapshot.Descriptor.Counters.UnknownLines,
                templates = result.Snapshot.Descriptor.Counters.Templates,
                elapsedSeconds = result.Elapsed.TotalSeconds,
            }, JsonOptions));
        }

        return 0;
    }

    private static async Task<int> InfoAsync(Arguments options, CancellationToken cancellationToken)
    {
        var path = options.RequiredPosition(0, "info requires a file or .vcat session path.");
        if (Directory.Exists(path))
        {
            using var snapshot = await SessionStore.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(snapshot.Manifest, JsonOptions));
            return 0;
        }

        await using var source = new FileLogSource(path);
        var policy = Policy(options, source.Metadata.ReferenceInstant);
        var preview = await ImportPreviewService.PreviewAsync(source, policy, ParseFormat(options.Get("--format")), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(preview, JsonOptions));
        return 0;
    }

    private static async Task<int> StatsAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var filter = Filter(options);
        var statistics = SessionQueryEngine.QueryStatistics(snapshot, filter, 1, options.GetInt("--top", 20), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(statistics, JsonOptions));
        return 0;
    }

    private static async Task<int> QueryAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var range = Range(options, snapshot);
        var filter = Filter(options);
        var order = ParseOrder(options.Get("--order"));
        var page = SessionQueryEngine.GetEntries(snapshot, range, filter, order, null, options.GetInt("--limit", 100), 1, cancellationToken);
        foreach (var entry in page.Entries)
        {
            Console.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
        }

        return 0;
    }

    private static async Task<int> SearchAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var query = options.RequiredPosition(1, "search requires a session path and query.");
        var search = new TextSearchSpec(
            query,
            options.Has("--regex"),
            options.Has("--case-sensitive"),
            TimeSpan.FromMilliseconds(options.GetInt("--timeout-ms", 250)));
        var result = await SessionQueryEngine.SearchAsync(snapshot, search, Filter(options), 1, null, 20_000, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static async Task<int> TemplatesAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var templates = SessionQueryEngine.QueryTopTemplates(
            snapshot,
            Range(options, snapshot),
            Filter(options),
            options.GetInt("--top", 50),
            1,
            cancellationToken: cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(templates, JsonOptions));
        return 0;
    }

    private static async Task<int> ExportAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var destination = options.RequiredPosition(1, "export requires a session path and destination.");
        var type = options.Get("--type") ?? "raw";
        var range = Range(options, snapshot);
        var filter = Filter(options);
        var order = ParseOrder(options.Get("--order"));
        switch (type.ToLowerInvariant())
        {
            case "raw":
                await ExportService.ExportRawAsync(snapshot, destination, range, filter, order, cancellationToken).ConfigureAwait(false);
                break;
            case "csv":
                await ExportService.ExportNormalizedCsvAsync(snapshot, destination, range, filter, order, cancellationToken).ConfigureAwait(false);
                break;
            case "templates-md":
                await ExportService.ExportTemplateReportAsync(snapshot, destination, range, filter, true, cancellationToken).ConfigureAwait(false);
                break;
            case "templates-csv":
                await ExportService.ExportTemplateReportAsync(snapshot, destination, range, filter, false, cancellationToken).ConfigureAwait(false);
                break;
            case "stats-md":
                await ExportService.ExportStatisticsAsync(snapshot, destination, filter, true, cancellationToken).ConfigureAwait(false);
                break;
            case "stats-csv":
                await ExportService.ExportStatisticsAsync(snapshot, destination, filter, false, cancellationToken).ConfigureAwait(false);
                break;
            case "portable":
                await PortableSessionService.SavePortableAsync(snapshot, destination, cancellationToken).ConfigureAwait(false);
                break;
            case "portable-zip":
                await PortableSessionArchiveService.CreateAsync(snapshot, destination, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new CommandException($"Unsupported export type '{type}'.");
        }

        Console.WriteLine(Path.GetFullPath(destination));
        return 0;
    }

    private static async Task<int> VerifyAsync(Arguments options, CancellationToken cancellationToken)
    {
        var path = options.RequiredPosition(0, "verify requires a .vcat session path.");
        var report = await SessionVerifier.VerifyAsync(path, !options.Has("--skip-raw"), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.IsValid ? 0 : 3;
    }

    private static async Task<int> GenerateAsync(Arguments options, CancellationToken cancellationToken)
    {
        var output = options.Get("--output") ?? options.PositionOrDefault(0) ?? "synthetic-logcat.txt";
        var lines = options.GetLong("--lines", 1_000_000);
        await using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(
                lines,
                options.GetInt("--seed", 42),
                Format: ParseFormat(options.Get("--format")) ?? LogcatFormat.ThreadTime,
                DistinctTags: options.GetInt("--tags", 0),
                DistinctTemplates: options.GetInt("--templates", 0)),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static async Task<int> AdbDevicesAsync(Arguments options, CancellationToken cancellationToken)
    {
        var adb = AdbLocator.Find(options.Get("--adb")) ?? throw new CommandException("ADB was not found. Set --adb, ANDROID_SDK_ROOT, or PATH.");
        var devices = await new ProcessAdbClient(adb).ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(devices, JsonOptions));
        return 0;
    }

    private static async Task<int> CaptureAdbAsync(Arguments options, CancellationToken cancellationToken)
    {
        var serial = options.Get("--serial") ?? throw new CommandException("capture-adb requires --serial.");
        var output = options.Get("--output") ?? $"adb-{Sanitize(serial)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.vcat";
        var adb = AdbLocator.Find(options.Get("--adb")) ?? throw new CommandException("ADB was not found. Set --adb, ANDROID_SDK_ROOT, or PATH.");
        var buffers = (options.Get("--buffers") ?? "main,system,crash").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maximumBytes = options.GetLong("--max-bytes", 0);
        var seconds = options.GetInt("--duration-seconds", 0);
        var preRollSeconds = options.GetInt("--pre-roll-seconds", 0);
        var includeBufferHistory = options.Has("--include-buffer-history");
        var requestedFormat = options.Get("--format");
        if (buffers.Length == 0)
        {
            throw new CommandException("capture-adb requires at least one buffer in --buffers.");
        }
        if (maximumBytes < 0)
        {
            throw new CommandException("--max-bytes must be zero (unlimited) or a positive byte count.");
        }
        if (seconds < 0)
        {
            throw new CommandException("--duration-seconds must be zero (unlimited) or a positive duration.");
        }
        if (preRollSeconds is < 0 or > 3600)
        {
            throw new CommandException("--pre-roll-seconds must be between 0 and 3600.");
        }
        if (includeBufferHistory && preRollSeconds > 0)
        {
            throw new CommandException(
                "--include-buffer-history cannot be combined with a positive --pre-roll-seconds value.");
        }
        if (requestedFormat is not null &&
            !requestedFormat.Equals("threadtime", StringComparison.OrdinalIgnoreCase))
        {
            throw new CommandException(
                "Live ADB capture uses threadtime format; omit --format or set it to threadtime.");
        }

        var duration = seconds > 0 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        await using var source = new AdbLogSource(
            new ProcessAdbClient(adb),
            serial,
            buffers,
            maximumBytes > 0 ? maximumBytes : null,
            TimeSpan.FromSeconds(preRollSeconds),
            includeBufferHistory,
            duration);

        // Settling the format first is what lets the policy below follow the zone the device
        // actually agreed to write in. Without it the CLI built its policy from the host,
        // so a capture from a phone whose logcat cannot emit the UTC modifier had every
        // instant moved by the host-to-device offset — silently, with isValid: true and no
        // defect counter raised (finding F-11). The desktop has always asked; this is the
        // same question, asked from the other surface.
        await source.PrepareAsync(cancellationToken).ConfigureAwait(false);
        var settings = Settings(options, DateTimeOffset.UtcNow, source.Metadata.ResolveLogTimeZoneId()) with
        {
            FormatOverride = LogcatFormat.ThreadTime,
            PortableRaw = true,
        };
        using var stop = duration is { } limit
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();
        var result = await SessionCoordinator.ImportAsync(
            source,
            output,
            settings,
            gracefulStopToken: stop.Token,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        result.Snapshot.Dispose();

        // stdout stays the session path, which is what a script reads. Why the capture ended
        // goes beside it, because "the log source ended it" sent readers to check a cable
        // when their own byte cap had fired (finding F-07).
        if (source.Completion is { } completion)
        {
            Console.Error.WriteLine($"capture ended: {completion.Summary}");
        }
        else if (stop.IsCancellationRequested)
        {
            Console.Error.WriteLine($"capture ended: it ran its full {seconds}s duration");
        }

        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    private static async Task<SessionSnapshot> OpenRequiredAsync(Arguments options, CancellationToken cancellationToken) =>
        await SessionStore.OpenAsync(options.RequiredPosition(0, "A .vcat session path is required."), cancellationToken).ConfigureAwait(false);

    private static IngestSettings Settings(Arguments options, DateTimeOffset reference, string? sourceZoneId = null) =>
        new(
            ParseFormat(options.Get("--format")),
            "utf-8",
            Policy(options, reference, sourceZoneId),
            new TemplateSettings(!options.Has("--no-templates")),
            SegmentEntries: options.GetInt("--segment-entries", 100_000),
            ParseWorkers: options.GetInt("--workers", 0),
            PortableRaw: options.Has("--portable"));

    // sourceZoneId is the zone the source says its own timestamps are written in, when it
    // knows. A device capture negotiates it; a file on disk does not, and is read in the
    // local zone as it always was. An explicit --timezone still wins over both.
    private static TimestampPolicy Policy(Arguments options, DateTimeOffset reference, string? sourceZoneId = null) =>
        new(
            options.GetNullableInt("--year"),
            options.Get("--timezone") ?? sourceZoneId ?? TimeZoneInfo.Local.Id,
            reference);

    private static FilterSpec Filter(Arguments options)
    {
        var levels = ImmutableHashSet<LogLevel>.Empty;
        if (options.Get("--levels") is { } value)
        {
            levels = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseLevel)
                .ToImmutableHashSet();
        }

        return new FilterSpec
        {
            IncludedLevels = levels,
            IncludedTags = Split(options.Get("--tags")),
            ExcludedTags = Split(options.Get("--exclude-tags")),
            IncludedPids = SplitInts(options.Get("--pids")),
            IncludedProcesses = Split(options.Get("--processes")),
            ExcludedProcesses = Split(options.Get("--exclude-processes")),
            IncludedTids = SplitInts(options.Get("--tids")),
            IncludedBuffers = Split(options.Get("--buffers")),
        };
    }

    private static TimeRange Range(Arguments options, SessionSnapshot snapshot)
    {
        var available = snapshot.TimedRange ?? throw new CommandException("Session has no timed entries.");
        return new TimeRange(
            options.Get("--from") is { } from ? ParseInstant(from) : available.StartInclusive,
            options.Get("--to") is { } to ? ParseInstant(to) : available.EndExclusive);
    }

    private static InstantUs ParseInstant(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
            ? new InstantUs(microseconds)
            : InstantUs.FromDateTimeOffset(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    private static LogcatFormat? ParseFormat(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "threadtime" => LogcatFormat.ThreadTime,
        "time" => LogcatFormat.Time,
        "brief" => LogcatFormat.Brief,
        "long" => LogcatFormat.LongFormat,
        "epoch" => LogcatFormat.Epoch,
        _ => throw new CommandException($"Unknown logcat format '{value}'."),
    };

    private static LogLevel ParseLevel(string value) => value.Trim().ToUpperInvariant() switch
    {
        "V" or "VERBOSE" => LogLevel.Verbose,
        "D" or "DEBUG" => LogLevel.Debug,
        "I" or "INFO" => LogLevel.Info,
        "W" or "WARN" => LogLevel.Warn,
        "E" or "ERROR" => LogLevel.Error,
        "F" or "FATAL" or "A" or "ASSERT" => LogLevel.Fatal,
        "?" or "UNKNOWN" => LogLevel.Unknown,
        _ => throw new CommandException($"Unknown level '{value}'."),
    };

    private static EntryOrder ParseOrder(string? value) =>
        value?.Equals("source", StringComparison.OrdinalIgnoreCase) == true
            ? EntryOrder.SourceSequence
            : EntryOrder.Chronological;

    private static ImmutableHashSet<string> Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToImmutableHashSet(StringComparer.Ordinal) ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    private static ImmutableHashSet<int> SplitInts(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static item => int.Parse(item, CultureInfo.InvariantCulture))
            .ToImmutableHashSet() ?? ImmutableHashSet<int>.Empty;

    private static string Sanitize(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    /// <summary>The filter options every query-shaped command shares.</summary>
    private const string FilterUsage =
        "[--levels W,E,F] [--tags TAG,...] [--exclude-tags TAG,...] [--pids 1,2] " +
        "[--processes NAME,...] [--exclude-processes NAME,...] [--tids 1,2] [--buffers main,system]";

    /// <summary>The ingest options `index` and `capture-adb` both take.</summary>
    private const string IngestUsage =
        "[--format threadtime|time|brief|long|epoch] [--year 2026] [--timezone UTC] " +
        "[--no-templates] [--segment-entries 100000] [--workers 0]";

    /// <summary>Ingest policy accepted by live ADB, whose wire format is always threadtime.</summary>
    private const string CaptureIngestUsage =
        "[--format threadtime] [--year 2026] [--timezone UTC] " +
        "[--no-templates] [--segment-entries 100000] [--workers 0]";

    /// <summary>
    /// Every command, with the usage text that is also its option list.
    /// </summary>
    /// <remarks>
    /// The options a command accepted and the usage line it printed used to be two lists
    /// kept by hand, and they had drifted in both directions: `vcat index --timezone UTC` —
    /// an option `CLI.md` documents and <see cref="Settings"/> reads — was refused as
    /// unknown, `vcat verify --skip-raw` was refused although `VerifyAsync` reads it, and
    /// `vcat search --limit 5` was accepted and silently ignored (report §4.3). There is one
    /// list now and the accepted set is read out of the printed text, so a command cannot
    /// accept an option it does not print, or print one it will not accept.
    /// </remarks>
    private static readonly Dictionary<string, CommandHelp> Commands = new(StringComparer.Ordinal)
    {
        ["index"] = new(
            $"vcat index <log.txt> [--output session.vcat] [--force] [--portable] {IngestUsage}",
            "--force replaces an existing .vcat directory; it refuses filesystem roots and trees containing links."),
        ["info"] = new("vcat info <log.txt|session.vcat> [--format threadtime] [--year 2026] [--timezone UTC]"),
        ["stats"] = new($"vcat stats <session.vcat> [--top 20] {FilterUsage}"),
        ["query"] = new(
            $"vcat query <session.vcat> [--from ISO|us] [--to ISO|us] [--limit 100] " +
            $"[--order chronological|source] {FilterUsage}",
            "--limit is capped at 10,000; read a whole session by paging with --from and --to."),
        ["search"] = new(
            $"vcat search <session.vcat> <text> [--regex] [--case-sensitive] [--timeout-ms 250] {FilterUsage}"),
        ["templates"] = new(
            $"vcat templates <session.vcat> [--top 50] [--from ISO|us] [--to ISO|us] {FilterUsage}"),
        ["export"] = new(
            "vcat export <session.vcat> <output> " +
            "[--type raw|csv|templates-md|templates-csv|stats-md|stats-csv|portable|portable-zip] " +
            $"[--from ISO|us] [--to ISO|us] [--order chronological|source] {FilterUsage}"),
        ["verify"] = new("vcat verify <session.vcat> [--skip-raw]"),
        // --format was parsed by GenerateAsync and rejected here, so the one option the live
        // test plan's §3.2 asks for by name could not be passed at all: `vcat
        // generate-test-log --format brief` failed as an unknown option while the code behind
        // it worked. A plan must not command a CLI option the shipped CLI rejects (PLAN-01).
        ["generate-test-log"] = new(
            "vcat generate-test-log [--output log.txt] [--lines 1000000] [--seed 42] " +
            "[--format threadtime|time|brief|long|epoch] [--tags 1900] [--templates 13000]",
            "--tags and --templates are set together and produce a corpus with that much " +
            "tag and template diversity, which the default seven-tag corpus does not have."),
        ["adb-devices"] = new("vcat adb-devices [--adb path]"),
        ["capture-adb"] = new(
            "vcat capture-adb --serial SERIAL [--output session.vcat] [--duration-seconds N] " +
            "[--max-bytes N] [--buffers main,system,crash] [--pre-roll-seconds 0] " +
            $"[--include-buffer-history] [--adb path] {CaptureIngestUsage}",
            "--pre-roll-seconds 0 starts from now; --include-buffer-history takes everything the " +
            "ring buffer already holds, which on a busy device is hundreds of thousands of records."),
    };

    /// <summary>One command's complete usage text, and anything a reader needs beside it.</summary>
    private sealed record CommandHelp(string Usage, string? Note = null);

    private static HashSet<string>? KnownOptions(string command) =>
        Commands.TryGetValue(command, out var help) ? OptionsIn($"{help.Usage} {help.Note}") : null;

    /// <summary>Every <c>--option</c> token in a usage text, which is what the command accepts.</summary>
    private static HashSet<string> OptionsIn(string usage)
    {
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchFrom = 0;
        while (searchFrom < usage.Length)
        {
            var index = usage.IndexOf("--", searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            var end = index + 2;
            while (end < usage.Length && (char.IsAsciiLetterOrDigit(usage[end]) || usage[end] == '-'))
            {
                end++;
            }

            if (end > index + 2)
            {
                options.Add(usage[index..end]);
            }

            searchFrom = end;
        }

        return options;
    }

    /// <summary>The usage text for one command, or the whole map when the command is unknown.</summary>
    private static void PrintCommandHelp(string command)
    {
        if (!Commands.TryGetValue(command, out var help))
        {
            PrintHelp();
            return;
        }

        Console.WriteLine(help.Usage);
        if (help.Note is { Length: > 0 } note)
        {
            Console.WriteLine();
            Console.WriteLine(note);
        }
    }

    private static void PrintHelp() => Console.WriteLine(
        $"""
        VisualCat v2 command line ({ProductInfo.InformationalVersion})

          vcat --version
          vcat index <log.txt> [--output session.vcat] [--portable] [--format threadtime]
          vcat info <log.txt|session.vcat>
          vcat stats <session.vcat> [--levels W,E,F] [--top 20]
          vcat query <session.vcat> [--from ISO|us] [--to ISO|us] [--limit 100] [--processes NAME]
          vcat search <session.vcat> <text> [--regex] [--case-sensitive]
          vcat templates <session.vcat> [--top 50]
          vcat export <session.vcat> <output> [--type raw|csv|templates-md|templates-csv|stats-md|stats-csv|portable|portable-zip]
          vcat verify <session.vcat>
          vcat generate-test-log [--output log.txt] [--lines 1000000] [--seed 42] [--format threadtime|time|brief|long|epoch]
          vcat adb-devices [--adb path]
          vcat capture-adb --serial SERIAL [--output session.vcat] [--duration-seconds N] [--max-bytes N]

        Run 'vcat <command> --help' for one command's complete option list.
        """);
}

internal sealed class Arguments
{
    private readonly List<string> _positions = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    private Arguments()
    {
    }

    public static Arguments Parse(IReadOnlyList<string> args)
    {
        var parsed = new Arguments();
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                parsed._positions.Add(value);
                continue;
            }

            var equals = value.IndexOf('=');
            if (equals > 0)
            {
                parsed._options[value[..equals]] = value[(equals + 1)..];
            }
            else if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed._options[value] = args[++i];
            }
            else
            {
                parsed._options[value] = null;
            }
        }

        return parsed;
    }

    /// <summary>
    /// Refuses an option the command does not have.
    /// </summary>
    /// <remarks>
    /// Unknown options were dropped on the floor, so a typo silently produced a run with the
    /// defaults — <c>--lines1000</c> asked for a thousand lines and got a million
    /// (finding F-02). Exit code 2 is what the rest of the CLI already uses for "you asked for
    /// something that is not a thing", and the message names the option so the typo is visible.
    /// </remarks>
    public void RejectUnknown(string command, IReadOnlySet<string>? known)
    {
        if (known is null)
        {
            return;
        }

        foreach (var name in _options.Keys)
        {
            if (!known.Contains(name))
            {
                throw new CommandException(
                    $"'{command}' does not take '{name}'. Run 'vcat {command} --help' to see what it does take.");
            }
        }
    }

    public bool Has(string name) => _options.ContainsKey(name);
    public string? Get(string name) => _options.GetValueOrDefault(name);
    public string? PositionOrDefault(int index) => index < _positions.Count ? _positions[index] : null;
    public string RequiredPosition(int index, string message) => PositionOrDefault(index) ?? throw new CommandException(message);
    public int GetInt(string name, int defaultValue) => Get(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : defaultValue;
    public int? GetNullableInt(string name) => Get(name) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;
    public long GetLong(string name, long defaultValue) => Get(name) is { } value ? long.Parse(value, CultureInfo.InvariantCulture) : defaultValue;
}

internal sealed class CommandException(string message) : Exception(message);

/// <summary>
/// Serializes <see cref="InstantUs"/> as an ISO-8601 UTC string in CLI output instead of
/// the raw wrapper record. Reading accepts both shapes so scripted round trips keep
/// working against older captured output.
/// </summary>
internal sealed class InstantUsJsonConverter : System.Text.Json.Serialization.JsonConverter<InstantUs>
{
    public override InstantUs Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            long value = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.GetString() is "value" or "Value")
                {
                    reader.Read();
                    value = reader.GetInt64();
                }
            }

            return new InstantUs(value);
        }

        return InstantUs.FromDateTimeOffset(reader.GetDateTimeOffset());
    }

    public override void Write(Utf8JsonWriter writer, InstantUs value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToDateTimeOffset().ToString("O", CultureInfo.InvariantCulture));
}
