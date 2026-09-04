using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Domain.Time;

namespace VisualCat.UiBench;

/// <summary>
/// The retained presentation-layer harness IA-06 asked for: one process, one session, the
/// same loop of settled viewport changes run against three configurations back to back.
/// </summary>
/// <remarks>
/// <para>
/// The original audit measurement lived in a temporary xUnit class that was deleted, so its
/// ~30x headless delta could not be reproduced or gated. This harness is the same experiment
/// with the pieces that matter written down: the corpus and its digest, the viewport ranges,
/// the warm-up, the settle definition, and repeated batches reported as median and p95 rather
/// than one elapsed loop.
/// </para>
/// <para>
/// It runs on the real platform backend with Skia, so a run on a physical display answers the
/// question the headless harness could not: whether a rooted workspace actually costs what
/// the null drawing backend said it did.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// A settled change is one whose queued work has drained to <see cref="DispatcherPriority.Background"/>.
    /// Layout, render and input jobs all sit above it, so nothing the change queued is still
    /// outstanding when the stopwatch stops. Both configurations use the same definition, which
    /// is what makes them comparable.
    /// </summary>
    private static readonly DispatcherPriority Settled = DispatcherPriority.Background;

    [STAThread]
    public static int Main(string[] args)
    {
        var options = BenchOptions.Parse(args);
        AppBuilder.Configure<VisualCat.App.App>()
            .UsePlatformDetect()
            .UseSkia()
            .SetupWithoutStarting();

        var exit = 0;
        using var lifetime = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await RunAsync(options).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exit = 1;
            }
            finally
            {
                lifetime.Cancel();
            }
        });

        Dispatcher.UIThread.MainLoop(lifetime.Token);
        return exit;
    }

    private static async Task RunAsync(BenchOptions options)
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-uibench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        var logPath = Path.Combine(root, "bench.txt");
        var log = BuildLog(options.Entries);
        await File.WriteAllTextAsync(logPath, log).ConfigureAwait(true);
        var corpusSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(log)));

        var workspace = new WorkspaceViewModel();
        try
        {
            var tab = await workspace.ImportFileAsync(logPath).ConfigureAwait(true);
            var range = tab.Snapshot?.TimedRange ?? throw new InvalidOperationException("The bench corpus has no timed entries.");
            var viewports = BuildViewports(range, options.ChangesPerBatch);

            var configurations = new List<ConfigurationResult>();
            configurations.Add(await MeasureAsync("A · view model only", options, viewports, tab, view: null, window: null).ConfigureAwait(true));

            var unrooted = new SessionWorkspaceView(tab);
            configurations.Add(await MeasureAsync("B · view constructed, never rooted", options, viewports, tab, unrooted, window: null).ConfigureAwait(true));

            var rooted = new SessionWorkspaceView(tab);
            var window = new Window
            {
                Content = rooted,
                Width = options.Width,
                Height = options.Height,
                Title = "VisualCat UI bench",
            };
            window.Show();
            await DrainAsync().ConfigureAwait(true);
            configurations.Add(await MeasureAsync("C · view shown in a window", options, viewports, tab, rooted, window).ConfigureAwait(true));
            window.Close();

            var report = new BenchReport(
                DateTimeOffset.Now,
                Environment.OSVersion.VersionString,
                RenderBackend(window),
                options.Entries,
                corpusSha,
                options.WarmupChanges,
                options.Batches,
                options.ChangesPerBatch,
                options.Width,
                options.Height,
                configurations);
            var json = JsonSerializer.Serialize(report, JsonOptions);
            Console.WriteLine(json);
            if (options.Output is { } output)
            {
                await File.WriteAllTextAsync(output, json).ConfigureAwait(true);
            }

            foreach (var configuration in configurations)
            {
                Console.Error.WriteLine(FormattableString.Invariant(
                    $"{configuration.Name,-36} median {configuration.MedianMsPerChange:F2} ms  p95 {configuration.P95MsPerChange:F2} ms  mean {configuration.MeanMsPerChange:F2} ms"));
            }

            await workspace.CloseAsync(tab).ConfigureAwait(true);
        }
        finally
        {
            await workspace.DisposeAsync().ConfigureAwait(true);
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            TryDelete(root);
        }
    }

    private static async Task<ConfigurationResult> MeasureAsync(
        string name,
        BenchOptions options,
        List<TimeRange> viewports,
        SessionTabViewModel tab,
        SessionWorkspaceView? view,
        Window? window)
    {
        _ = view;
        _ = window;
        for (var index = 0; index < options.WarmupChanges; index++)
        {
            await SettledChangeAsync(tab, viewports[index % viewports.Count]).ConfigureAwait(true);
        }

        var batches = new double[options.Batches];
        for (var batch = 0; batch < options.Batches; batch++)
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var viewport in viewports)
            {
                await SettledChangeAsync(tab, viewport).ConfigureAwait(true);
            }

            stopwatch.Stop();
            batches[batch] = stopwatch.Elapsed.TotalMilliseconds / viewports.Count;
        }

        var ordered = batches.Order().ToArray();
        return new ConfigurationResult(
            name,
            batches.Average(),
            Percentile(ordered, 0.5),
            Percentile(ordered, 0.95),
            ordered[0],
            ordered[^1],
            batches);
    }

    /// <summary>Issues one viewport change and waits for everything it queued.</summary>
    private static async Task SettledChangeAsync(SessionTabViewModel tab, TimeRange viewport)
    {
        await tab.SetViewportAsync(viewport).ConfigureAwait(true);
        await DrainAsync().ConfigureAwait(true);
    }

    private static Task DrainAsync() => Dispatcher.UIThread.InvokeAsync(static () => { }, Settled).GetTask();

    private static double Percentile(double[] ordered, double fraction)
    {
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        var position = fraction * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return ordered[lower] + ((ordered[upper] - ordered[lower]) * (position - lower));
    }

    /// <summary>
    /// Forty ranges that walk the session at three widths, so the loop exercises a wide fit, a
    /// mid zoom and a tight window rather than repeating one shape the caches can learn.
    /// </summary>
    private static List<TimeRange> BuildViewports(TimeRange session, int count)
    {
        var span = Math.Max(1_000L, session.EndExclusive.Value - session.StartInclusive.Value);
        var viewports = new List<TimeRange>(count);
        for (var index = 0; index < count; index++)
        {
            var width = span / ((index % 3) switch { 0 => 2, 1 => 6, _ => 20 });
            var start = session.StartInclusive.Value + (span - width) * index / Math.Max(1, count - 1);
            viewports.Add(new TimeRange(new InstantUs(start), new InstantUs(start + width)));
        }

        return viewports;
    }

    /// <summary>
    /// The audit's corpus shape: threadtime lines, 191 distinct tags, ~55-character messages.
    /// Generated here rather than read from disk so a run needs nothing but this project.
    /// </summary>
    private static string BuildLog(int entries)
    {
        var builder = new StringBuilder(entries * 96);
        for (var index = 0; index < entries; index++)
        {
            var second = index % 60;
            var micro = index % 1_000_000;
            builder.Append(FormattableString.Invariant(
                $"01-0{1 + index / 900_000} 00:{index / 60 % 60:00}:{second:00}.{micro % 1000:000}  1{index % 900:000}  2{index % 700:000} "))
                .Append("IWEDV"[index % 5])
                .Append(FormattableString.Invariant($" Tag{index % 191:000}: worker {index} finished unit of work in {index % 977} ms with status ok\n"));
        }

        return builder.ToString();
    }

    private static string RenderBackend(Window window)
    {
        try
        {
            return window.PlatformImpl?.GetType().FullName ?? "unknown";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return "unknown";
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record BenchOptions(
        int Entries,
        int WarmupChanges,
        int Batches,
        int ChangesPerBatch,
        double Width,
        double Height,
        string? Output)
    {
        public static BenchOptions Parse(string[] args)
        {
            var entries = 50_000;
            var warmup = 10;
            var batches = 5;
            var changes = 40;
            var width = 1280d;
            var height = 800d;
            string? output = null;
            for (var index = 0; index < args.Length - 1; index++)
            {
                var value = args[index + 1];
                switch (args[index])
                {
                    case "--entries": entries = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--warmup": warmup = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--batches": batches = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--changes": changes = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--width": width = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--height": height = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--output": output = value; break;
                    default: break;
                }
            }

            return new BenchOptions(entries, warmup, batches, changes, width, height, output);
        }
    }

    private sealed record ConfigurationResult(
        string Name,
        double MeanMsPerChange,
        double MedianMsPerChange,
        double P95MsPerChange,
        double FastestBatchMsPerChange,
        double SlowestBatchMsPerChange,
        IReadOnlyList<double> BatchMsPerChange);

    private sealed record BenchReport(
        DateTimeOffset RanAt,
        string OperatingSystem,
        string WindowImplementation,
        int Entries,
        string CorpusSha256,
        int WarmupChanges,
        int Batches,
        int ChangesPerBatch,
        double WindowWidth,
        double WindowHeight,
        IReadOnlyList<ConfigurationResult> Configurations);
}
