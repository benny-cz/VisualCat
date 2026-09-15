using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
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
using VisualCat.Infrastructure.Diagnostics;
using VisualCat.Infrastructure.Files;

return await VisualCatCli.RunAsync(args).ConfigureAwait(false);

internal static class VisualCatCli
{
    /// <summary>
    /// How long a terminating signal waits for the cooperative shutdown to publish what it has.
    /// </summary>
    /// <remarks>
    /// A <c>PosixSignal</c> handler that returns immediately lets the runtime carry on with the
    /// default disposition, so cancelling without waiting still tears the process down
    /// mid-publication. Long enough for a session to finalize its manifest; short enough that a
    /// second signal, a <c>systemd</c> stop timeout, or an impatient operator is not left
    /// waiting on a wedged writer.
    /// </remarks>
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Set once the command has finished, so a terminating signal stops waiting.</summary>
    private static readonly ManualResetEventSlim _shutdownDrained = new(false);

    // Output-only options: names instead of bare enum ordinals and ISO-8601 instants.
    // Session manifests keep their own serializer settings, so the on-disk format is
    // unaffected (§16.6 — the CLI is a human and automation surface at once).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,

        // Indentation uses the host's newline unless told otherwise, which is why the same
        // session's `templates` JSON was 602 bytes longer on Windows than on Linux and
        // identical after stripping the carriage returns (finding F-29). Machine-readable
        // output that depends on which machine produced it is not machine-readable.
        NewLine = "\n",
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            new InstantUsJsonConverter(),
        },
    };

    /// <summary>
    /// The same shape on one line each, for the streaming command.
    /// </summary>
    /// <remarks>
    /// <c>query</c> is documented as NDJSON — "one NormalizedEntry JSON object per line,
    /// suitable for streaming into tools such as jq" — and is the reference's own answer for
    /// reading a session larger than one result. Serialized with the indented options it
    /// spread each entry over 24 lines, so the file the documented example redirects to
    /// (<c>errors.ndjson</c>) could not be read by jq, by a line reader, or by anything else
    /// that trusts the name: 100 entries arrived as 2,400 lines, none of which parsed on its
    /// own (Linux live test L-04). Every other command prints one whole document a person
    /// reads, and those stay indented.
    /// </remarks>
    private static readonly JsonSerializerOptions NdjsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
            new InstantUsJsonConverter(),
        },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        // The runtime leaves one diagnostics IPC socket per process in the temporary directory
        // and never unlinks it (finding F-17). A short-lived command run in a loop is the
        // fastest way to accumulate them.
        RuntimeResidue.SweepExitedDiagnosticSockets();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => RuntimeResidue.RemoveOwnDiagnosticSocket();

        // The trailing newline of every Console.WriteLine, for the same reason as the JSON
        // indentation above: `vcat stats > stats.json` must produce the same bytes on every
        // platform. Modern Windows consoles render bare LF correctly (F-29).
        Console.Out.NewLine = "\n";

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

        // Two stages, because an interruption should not cost the reader what has already been
        // captured. The first signal asks the source to stop and lets the pipeline publish and
        // finalize what it has, so the session that survives is complete as far as it goes and
        // passes `vcat verify`. A second signal gives up on that and cancels outright.
        //
        // Before this, SIGINT was not reaching anything at all — an index signalled 2.5 s into
        // a 10 s run finished all 900,001 lines and exited 0, so Ctrl+C looked like it worked
        // by doing nothing — and SIGTERM, which is how a long index actually gets interrupted
        // on Linux (a systemd stop, a logout, a container shutdown, a bare kill), was the
        // runtime's default terminate: the process was torn down mid-publication and left a
        // session stuck in Importing with one recoverable entry (finding F-18).
        using var stop = new CancellationTokenSource();
        using var cancellation = new CancellationTokenSource();

        // Every terminating signal through one registration each, rather than SIGINT through
        // Console.CancelKeyPress and the rest through this. CancelKeyPress fires only where the
        // runtime sees a console: under `ssh host 'vcat index …'`, in a systemd unit, in a
        // container, a `kill -INT` reached nothing at all, so an index signalled 0.8 s into a
        // 30 s run read the whole file and exited 0 — Ctrl+C appeared to work by doing nothing.
        // PosixSignal is raised for Ctrl+C as well, on Unix and on Windows, so one route serves
        // both and neither can fire twice for one press.
        //
        // Cancel:true takes over from the default disposition, and the handler then waits for
        // the cooperative shutdown to publish rather than returning immediately, which would
        // let the runtime exit anyway.
        using var sigint = Terminating(PosixSignal.SIGINT, stop, cancellation);
        using var sigterm = Terminating(PosixSignal.SIGTERM, stop, cancellation);
        using var sighup = Terminating(PosixSignal.SIGHUP, stop, cancellation);

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

            // The XDG specification says a relative XDG_DATA_HOME is invalid and must be
            // ignored, and .NET duly ignores it — which meant a user who asked for one
            // directory silently got another (F-19). Ignoring it is correct; saying nothing
            // is not. Once per run, on stderr, so it cannot corrupt a piped --json result.
            if (ProductDataRoot.IgnoredDataHome is { } ignored)
            {
                Console.Error.WriteLine(
                    $"warning: XDG_DATA_HOME='{ignored}' is a relative path, which the XDG " +
                    $"specification does not allow, so it was ignored. VisualCat is using " +
                    $"'{ProductDataRoot.Path}'.");
            }

            // Same reason. An unrecognised option was silently ignored, so `--lines1000`
            // produced a million-line file instead of an error.
            options.RejectUnknown(command, KnownOptions(command));

            // And the same again for arguments that are not options. Everything after a POSIX
            // "--" is an operand, so `vcat index -- log.txt --output s.vcat` hands three
            // positionals to a command that reads one — which, silently ignored, would have
            // written the session beside the log instead of where it was asked (F-07). Extra
            // positionals were already dropped before "--" existed; naming them is what makes
            // the separator safe to use.
            options.RejectExtraPositions(command, PositionalLimit(command));
            return command switch
            {
                "index" => await IndexAsync(options, stop.Token, cancellation.Token).ConfigureAwait(false),
                "info" => await InfoAsync(options, cancellation.Token).ConfigureAwait(false),
                "stats" => await StatsAsync(options, cancellation.Token).ConfigureAwait(false),
                "query" => await QueryAsync(options, cancellation.Token).ConfigureAwait(false),
                "search" => await SearchAsync(options, cancellation.Token).ConfigureAwait(false),
                "templates" => await TemplatesAsync(options, cancellation.Token).ConfigureAwait(false),
                "export" => await ExportAsync(options, cancellation.Token).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellation.Token).ConfigureAwait(false),
                "generate-test-log" => await GenerateAsync(options, cancellation.Token).ConfigureAwait(false),
                "adb-devices" => await AdbDevicesAsync(options, cancellation.Token).ConfigureAwait(false),
                "capture-adb" => await CaptureAdbAsync(options, stop.Token, cancellation.Token).ConfigureAwait(false),
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
        catch (SearchTimeoutException exception)
        {
            // Product wording only: framework timeout text varies by runtime and may be
            // trimmed to a resource key on constrained builds.
            Console.Error.WriteLine($"error: {exception.Message}");
            return 3;
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
        finally
        {
            // However the command ended, a signal handler still parked on the drain gate must be
            // released: the work it was waiting for is over (F-18).
            _shutdownDrained.Set();
        }
    }

    /// <summary>Routes one terminating signal into the two-stage shutdown.</summary>
    private static PosixSignalRegistration Terminating(
        PosixSignal signal,
        CancellationTokenSource stop,
        CancellationTokenSource cancellation) =>
        PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            RequestShutdown(stop, cancellation);
            _shutdownDrained.Wait(ShutdownDrainTimeout);
        });

    /// <summary>
    /// Asks the running command to finish, and on a second request gives up on finishing.
    /// </summary>
    private static void RequestShutdown(CancellationTokenSource stop, CancellationTokenSource cancellation)
    {
        if (stop.IsCancellationRequested)
        {
            cancellation.Cancel();
            return;
        }

        Console.Error.WriteLine(
            "Stopping: finishing what has been read so the session can be verified. " +
            "Signal again to give up on that.");
        stop.Cancel();
    }

    private static async Task<int> IndexAsync(
        Arguments options,
        CancellationToken stopToken,
        CancellationToken cancellationToken)
    {
        var input = options.RequiredPosition(0, "index requires a log file path.");
        var output = options.GetValue("--output") ?? Path.GetFullPath(input) + ".vcat";
        RefuseNestedSession(output);
        if (Directory.Exists(output) && !options.Has("--force"))
        {
            throw new CommandException($"Output already exists: {output}. Use --force to replace it.");
        }

        // The exclusive write lease is taken before --force touches anything, and held across
        // the import. It used to be taken inside the import, after the delete, so a second
        // writer starting a moment later deleted part of the session the first one was still
        // publishing: the loser was correctly refused, and the winner exited 0 leaving a
        // session that failed verification (finding F-20). The lease nests, so the coordinator's
        // own acquisition inside is a no-op.
        using var reservation = SessionAccess.Write(output);
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
            gracefulStopToken: stopToken,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        long published;
        using (result.Snapshot)
        {
            published = result.Snapshot.Descriptor.Counters.TimedEntries;
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

        // Belt and braces for F-20: the lease now keeps two vcat processes off one session, but
        // nothing can stop a tool that does not take it. Re-reading the published manifest costs
        // one open — it is O(segments), not O(entries) — and turns "exited 0, leaves a session
        // that fails verification" into a loud failure at the moment it happens.
        await ConfirmPublishedAsync(output, published, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Re-reads a session that has just been published and refuses to call it a success if what
    /// is on disk is not what was written.
    /// </summary>
    private static async Task ConfirmPublishedAsync(string path, long expectedEntries, CancellationToken cancellationToken)
    {
        try
        {
            using var snapshot = await SessionStore.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            var actual = snapshot.Descriptor.Counters.TimedEntries;
            if (actual != expectedEntries)
            {
                throw new IOException(
                    $"The published session reports {actual:N0} timed entries where {expectedEntries:N0} " +
                    "were written. Something changed it while it was being published; run " +
                    "'vcat verify' on it and index it again.");
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or IOException))
        {
            throw new IOException(
                $"The session was written but could not be read back from '{path}': {exception.Message}. " +
                "Run 'vcat verify' on it and index it again.",
                exception);
        }
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
        var preview = await ImportPreviewService.PreviewAsync(source, policy, ParseFormat(options.GetValue("--format")), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(preview, JsonOptions));
        return 0;
    }

    private static async Task<int> StatsAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var filter = Filter(options);
        var statistics = SessionQueryEngine.QueryStatistics(snapshot, filter, 1, options.GetInt("--top", 20, 1), cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(statistics, JsonOptions));
        return 0;
    }

    private static async Task<int> QueryAsync(Arguments options, CancellationToken cancellationToken)
    {
        // Parse public option values before touching a positional path. If a value-taking
        // option consumed what the reader intended as that path, the error must name the
        // option/value pair instead of claiming the session path is simply missing.
        var order = ParseOrder(options.GetValue("--order"));
        var limit = options.GetInt("--limit", 100, 1, 10_000);
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var range = Range(options, snapshot);
        var filter = Filter(options);
        var page = SessionQueryEngine.GetEntries(snapshot, range, filter, order, null, limit, 1, cancellationToken);
        foreach (var entry in page.Entries)
        {
            Console.WriteLine(JsonSerializer.Serialize(entry, NdjsonOptions));
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
            TimeSpan.FromMilliseconds(options.GetInt("--timeout-ms", 250, 1, 60_000)));
        var filter = Filter(options);
        var result = await SessionQueryEngine.SearchAsync(snapshot, search, filter, 1, null, 20_000, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

        // Text search matches a record's message, not its tag. That is defensible and was
        // silent: searching a real capture for VCATTEST returned the three adbd lines that
        // quote it and none of the 301 records actually tagged with it (finding F-12). On
        // stderr, so a script redirecting stdout still gets clean JSON.
        if (SearchAlternatives.Find(snapshot, filter, query, result.Matches, search.IsRegex, cancellationToken)
            is { } alternative)
        {
            Console.Error.WriteLine(SearchAlternatives.Describe(alternative, query, forCommandLine: true));
        }

        return 0;
    }

    private static async Task<int> TemplatesAsync(Arguments options, CancellationToken cancellationToken)
    {
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var templates = SessionQueryEngine.QueryTopTemplates(
            snapshot,
            Range(options, snapshot),
            Filter(options),
            options.GetInt("--top", 50, 1),
            1,
            cancellationToken: cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(templates, JsonOptions));
        return 0;
    }

    private static async Task<int> ExportAsync(Arguments options, CancellationToken cancellationToken)
    {
        var type = options.GetValue("--type") ?? "raw";
        var order = ParseOrder(options.GetValue("--order"));
        var newline = ParseNewline(options.GetValue("--newline"));
        using var snapshot = await OpenRequiredAsync(options, cancellationToken).ConfigureAwait(false);
        var destination = options.RequiredPosition(1, "export requires a session path and destination.");
        var range = Range(options, snapshot);
        var filter = Filter(options);
        switch (type.ToLowerInvariant())
        {
            case "raw":
                await ExportService.ExportRawAsync(snapshot, destination, range, filter, order, cancellationToken).ConfigureAwait(false);
                break;
            case "csv":
                await ExportService.ExportNormalizedCsvAsync(
                    snapshot, destination, range, filter, order,
                    includeUtf8Bom: true, progress: null, newline, cancellationToken).ConfigureAwait(false);
                break;
            case "templates-md":
                await ExportService.ExportTemplateReportAsync(snapshot, destination, range, filter, true, newline, cancellationToken).ConfigureAwait(false);
                break;
            case "templates-csv":
                await ExportService.ExportTemplateReportAsync(snapshot, destination, range, filter, false, newline, cancellationToken).ConfigureAwait(false);
                break;
            case "stats-md":
                await ExportService.ExportStatisticsAsync(snapshot, destination, filter, true, newline, cancellationToken).ConfigureAwait(false);
                break;
            case "stats-csv":
                await ExportService.ExportStatisticsAsync(snapshot, destination, filter, false, newline, cancellationToken).ConfigureAwait(false);
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
        var checkRaw = !options.Has("--skip-raw");
        var report = await SessionVerifier.VerifyAsync(path, checkRaw, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        if (!report.IsValid)
        {
            return 3;
        }

        // "Nothing was detected as wrong" and "the evidence was checked and matches" are two
        // different answers, and only the first one has an exit code of its own. A standard
        // session whose external source has been deleted verifies clean because it never owned
        // that source — which a script reading $? cannot tell from a session whose raw evidence
        // was checked (finding F-28). --require-raw is that stronger question, asked explicitly.
        if (checkRaw && options.Has("--require-raw") && !report.RawVerified)
        {
            Console.Error.WriteLine(
                "error: the raw evidence could not be checked, so this session is unverified " +
                "rather than verified. Re-index from the original source, or export it as a " +
                "portable session so the evidence travels with it.");
            return 4;
        }

        return 0;
    }

    private static async Task<int> GenerateAsync(Arguments options, CancellationToken cancellationToken)
    {
        var output = options.GetValue("--output") ?? options.PositionOrDefault(0) ?? "synthetic-logcat.txt";
        var lines = options.GetLong("--lines", 1_000_000, 0);
        await using var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await SyntheticLogGenerator.GenerateAsync(
            stream,
            new SyntheticLogOptions(
                lines,
                options.GetInt("--seed", 42),
                Format: ParseFormat(options.GetValue("--format")) ?? LogcatFormat.ThreadTime,
                DistinctTags: options.GetInt("--tags", 0, 0),
                DistinctTemplates: options.GetInt("--templates", 0, 0)),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(Path.GetFullPath(output));
        return 0;
    }

    /// <summary>
    /// Resolves <c>adb</c>, saying what went wrong in the terms the caller can act on.
    /// </summary>
    /// <remarks>
    /// Two different failures used to share one sentence. A wrong <c>--adb</c> was silently
    /// ignored in favour of whatever was on <c>PATH</c> (finding F-10), and when there was no
    /// fallback the answer was "Set --adb, ANDROID_SDK_ROOT, or PATH" — printed to a user
    /// looking at the <c>--adb</c> they had just set, naming two of the four routes that work,
    /// and, on a Mac with no Android tooling, naming no way to obtain <c>adb</c> at all
    /// (finding F-12). The locator now refuses a pinned path by name, and the list of places
    /// searched is generated by the locator itself so the two cannot drift apart again.
    /// </remarks>
    /// <summary>
    /// Refuses an output path inside an existing session, the same way a save does.
    /// </summary>
    /// <remarks>
    /// The desktop hits this through a save panel that navigates into a session as readily as
    /// into any folder (finding F-13); the command line hits it through tab completion and
    /// through a script that builds a path. The consequence is the same either way: the outer
    /// session carries data its manifest does not describe, and its own cache retention later
    /// deletes the inner one.
    /// </remarks>
    private static void RefuseNestedSession(string output)
    {
        if (SessionSaveService.EnclosingSession(Path.GetFullPath(output)) is { } enclosing)
        {
            throw new CommandException(
                $"That output path is inside the session '{Path.GetFileName(enclosing)}'. Choose a location outside it: " +
                "a session nested inside another is deleted by the outer session's cache retention.");
        }
    }

    private static string ResolveAdb(Arguments options)
    {
        try
        {
            var resolved = AdbLocator.Find(options.GetValue("--adb"));
            if (resolved is not null)
            {
                return resolved;
            }
        }
        catch (AdbLocatorException exception)
        {
            throw new CommandException(exception.Message);
        }

        var searched = string.Join(
            Environment.NewLine,
            AdbLocator.SearchLocationSummary().Select(static line => $"         {line}"));
        throw new CommandException(
            "ADB was not found. It was looked for in, in order:" + Environment.NewLine +
            searched + Environment.NewLine +
            "       " + AdbLocator.InstallHint());
    }

    private static async Task<int> AdbDevicesAsync(Arguments options, CancellationToken cancellationToken)
    {
        var adb = ResolveAdb(options);
        var devices = await new ProcessAdbClient(adb).ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(devices, JsonOptions));
        return 0;
    }

    private static async Task<int> CaptureAdbAsync(
        Arguments options,
        CancellationToken stopToken,
        CancellationToken cancellationToken)
    {
        var serial = options.GetValue("--serial") ?? throw new CommandException("capture-adb requires --serial.");
        var output = options.GetValue("--output") ?? $"adb-{Sanitize(serial)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.vcat";
        RefuseNestedSession(output);
        var adb = ResolveAdb(options);
        var buffers = SplitValues("--buffers", options.GetValue("--buffers") ?? "main,system,crash");
        var maximumBytes = options.GetLong("--max-bytes", 0, 0);
        var seconds = options.GetInt("--duration-seconds", 0, 0);
        var preRollSeconds = options.GetInt("--pre-roll-seconds", 0, 0, 3600);
        var includeBufferHistory = options.Has("--include-buffer-history");
        var requestedFormat = options.GetValue("--format");
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
        // A duration limit and a terminating signal are the same request: stop reading and
        // publish what is there.
        using var stop = duration is { } limit
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();
        using var stopRegistration = stopToken.Register(stop.Cancel);
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
            ParseFormat(options.GetValue("--format")),
            "utf-8",
            Policy(options, reference, sourceZoneId),
            new TemplateSettings(!options.Has("--no-templates")),
            SegmentEntries: options.GetInt("--segment-entries", 100_000, 1, 5_000_000),
            ParseWorkers: options.GetInt("--workers", 0, 0, 256),
            PortableRaw: options.Has("--portable"));

    // sourceZoneId is the zone the source says its own timestamps are written in, when it
    // knows. A device capture negotiates it; a file on disk does not, and is read in the
    // local zone as it always was. An explicit --timezone still wins over both.
    private static TimestampPolicy Policy(Arguments options, DateTimeOffset reference, string? sourceZoneId = null) =>
        new(
            options.GetNullableInt("--year", 1, 9999),
            options.GetValue("--timezone") ?? sourceZoneId ?? TimeZoneResolution.LocalId(),
            reference);

    private static FilterSpec Filter(Arguments options)
    {
        var levels = ImmutableHashSet<LogLevel>.Empty;
        if (options.GetValue("--levels") is { } value)
        {
            levels = SplitValues("--levels", value)
                .Select(ParseLevel)
                .ToImmutableHashSet();
        }

        return new FilterSpec
        {
            IncludedLevels = levels,
            IncludedTags = Split(options, "--tags"),
            ExcludedTags = Split(options, "--exclude-tags"),
            IncludedPids = options.GetIntSet("--pids"),
            IncludedProcesses = Split(options, "--processes"),
            ExcludedProcesses = Split(options, "--exclude-processes"),
            IncludedTids = options.GetIntSet("--tids"),
            IncludedBuffers = Split(options, "--buffers"),
        };
    }

    private static TimeRange Range(Arguments options, SessionSnapshot snapshot)
    {
        var available = snapshot.TimedRange ?? throw new CommandException("Session has no timed entries.");
        return new TimeRange(
            options.GetValue("--from") is { } from ? ParseInstant("--from", from) : available.StartInclusive,
            options.GetValue("--to") is { } to ? ParseInstant("--to", to) : available.EndExclusive);
    }

    internal static InstantUs ParseInstant(string option, string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            return new InstantUs(microseconds);
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var instant))
        {
            return InstantUs.FromDateTimeOffset(instant);
        }

        throw new CommandException($"{option} value '{value}' must be an ISO-8601 timestamp or integer microseconds.");
    }

    /// <summary>
    /// Reads <c>--newline</c>. The default is LF on every platform, deliberately rather than by
    /// inheritance from the host, so the same session and options produce the same bytes
    /// wherever they run (F-29).
    /// </summary>
    private static NewlineStyle ParseNewline(string? value) => value?.ToLowerInvariant() switch
    {
        null or "" or "lf" => NewlineStyle.Lf,
        "crlf" => NewlineStyle.Crlf,
        _ => throw new CommandException($"Unknown --newline '{value}'. Use lf or crlf."),
    };

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

    internal static EntryOrder ParseOrder(string? value) => value?.ToLowerInvariant() switch
    {
        null or "chronological" => EntryOrder.Chronological,
        "source" => EntryOrder.SourceSequence,
        _ => throw new CommandException(
            $"--order value '{value}' must be 'chronological' or 'source'."),
    };

    private static ImmutableHashSet<string> Split(Arguments options, string name) =>
        options.GetValue(name) is { } value
            ? SplitValues(name, value).ToImmutableHashSet(StringComparer.Ordinal)
            : ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    private static string[] SplitValues(string name, string value)
    {
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Length > 0
            ? values
            : throw new CommandException($"{name} requires one or more comma-separated values.");
    }

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
            $"[--from ISO|us] [--to ISO|us] [--order chronological|source] [--newline lf|crlf] {FilterUsage}",
            "--newline applies to the text types; it defaults to lf on every platform so the " +
            "same session and options export byte for byte the same on Linux, macOS and Windows."),
        ["verify"] = new(
            "vcat verify <session.vcat> [--skip-raw] [--require-raw]",
            "--require-raw exits 4 when the raw evidence could not be checked at all, which a " +
            "clean verdict on its own does not distinguish from a session whose evidence was " +
            "checked and matched."),
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

    /// <summary>How many arguments that are not options the command reads.</summary>
    private static int PositionalLimit(string command) => command switch
    {
        "search" or "export" => 2,
        "adb-devices" or "capture-adb" => 0,
        _ => 1,
    };

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
        var optionsEnded = false;
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];

            // A bare "--" ends option parsing, as it does in every POSIX tool. Without it a file
            // whose name begins with "-" has no safe spelling on Linux, where such names are
            // legal, and every script written to the usual convention fails on the separator
            // rather than on its argument (finding F-07).
            if (!optionsEnded && value == "--")
            {
                optionsEnded = true;
                continue;
            }

            if (optionsEnded || !value.StartsWith("--", StringComparison.Ordinal))
            {
                parsed._positions.Add(value);
                continue;
            }

            var equals = value.IndexOf('=');
            if (equals > 0)
            {
                parsed._options[value[..equals]] = value[(equals + 1)..];
            }
            else if (i + 1 < args.Count && args[i + 1] != "--" && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
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

    /// <summary>
    /// Refuses arguments beyond the ones the command reads, naming them.
    /// </summary>
    public void RejectExtraPositions(string command, int limit)
    {
        if (_positions.Count <= limit)
        {
            return;
        }

        var extra = string.Join(", ", _positions.Skip(limit).Select(static value => $"'{value}'"));
        throw new CommandException(
            limit == 0
                ? $"'{command}' takes no arguments of its own, but got {extra}. " +
                  $"Run 'vcat {command} --help' to see what it does take."
                : $"'{command}' takes {limit} argument{(limit == 1 ? string.Empty : "s")} " +
                  $"before its options, but got {_positions.Count}; unexpected: {extra}. " +
                  "Everything after a bare '--' is treated as a file name, never as an option.");
    }

    public bool Has(string name) => _options.ContainsKey(name);
    public string? Get(string name) => _options.GetValueOrDefault(name);
    public string? GetValue(string name)
    {
        if (!_options.TryGetValue(name, out var value))
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CommandException($"{name} requires a value.");
    }

    public string? PositionOrDefault(int index) => index < _positions.Count ? _positions[index] : null;
    public string RequiredPosition(int index, string message) => PositionOrDefault(index) ?? throw new CommandException(message);

    public int GetInt(
        string name,
        int defaultValue,
        int minimum = int.MinValue,
        int maximum = int.MaxValue) =>
        GetValue(name) is { } value ? ParseInt(name, value, minimum, maximum) : defaultValue;

    public int? GetNullableInt(
        string name,
        int minimum = int.MinValue,
        int maximum = int.MaxValue) =>
        GetValue(name) is { } value ? ParseInt(name, value, minimum, maximum) : null;

    public long GetLong(
        string name,
        long defaultValue,
        long minimum = long.MinValue,
        long maximum = long.MaxValue) =>
        GetValue(name) is { } value ? ParseLong(name, value, minimum, maximum) : defaultValue;

    public ImmutableHashSet<int> GetIntSet(string name)
    {
        if (GetValue(name) is not { } value)
        {
            return ImmutableHashSet<int>.Empty;
        }

        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
        {
            throw new CommandException($"{name} requires one or more comma-separated integers.");
        }

        var result = ImmutableHashSet.CreateBuilder<int>();
        foreach (var item in items)
        {
            // Name the whole option's shape rather than the one item that failed to parse:
            // the reader typed a list, and int.MinValue..int.MaxValue is not the constraint
            // they broke.
            if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new CommandException(
                    $"{name} value '{value}' must be a comma-separated list of integers.");
            }

            result.Add(parsed);
        }

        return result.ToImmutable();
    }

    private static int ParseInt(string name, string value, int minimum, int maximum)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= minimum &&
            parsed <= maximum)
        {
            return parsed;
        }

        throw new CommandException(
            $"{name} value '{value}' must be an integer between " +
            $"{minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static long ParseLong(string name, string value, long minimum, long maximum)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= minimum &&
            parsed <= maximum)
        {
            return parsed;
        }

        throw new CommandException(
            $"{name} value '{value}' must be an integer between " +
            $"{minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.");
    }
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
