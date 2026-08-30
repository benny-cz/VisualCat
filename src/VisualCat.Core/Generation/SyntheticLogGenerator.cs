using System.Globalization;
using System.Text;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Time;

namespace VisualCat.Core.Generation;

public sealed record SyntheticLogOptions(
    long Lines,
    int Seed = 42,
    DateTimeOffset? Start = null,
    LogcatFormat Format = LogcatFormat.ThreadTime,
    double OutOfOrderRate = 0.001,
    double UnknownLineRate = 0.0001,
    bool IncludeBufferMarkers = true);

public static class SyntheticLogGenerator
{
    private static readonly string[] Tags = ["ActivityManager", "AndroidRuntime", "SurfaceFlinger", "VisualCat", "Network", "Camera", "chatty"];
    private static readonly string[] Messages =
    [
        "Started process {0} for package com.example.app",
        "Frame completed in {0} ms",
        "Connection {0} to 10.0.0.8 failed after {1} ms",
        "Rendering surface 0x{0:X8}",
        "FATAL EXCEPTION: main",
        "Cache contains {0} entries",
        "uid=10007(com.example) identical {0} lines",
    ];

    public static async Task GenerateAsync(
        Stream destination,
        SyntheticLogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.Lines);
        var random = new Random(options.Seed);
        var instant = options.Start ?? new DateTimeOffset(2025, 5, 15, 14, 13, 37, TimeSpan.Zero);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 1024 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
        };
        if (options.IncludeBufferMarkers)
        {
            await writer.WriteLineAsync("--------- beginning of main").ConfigureAwait(false);
        }

        for (long line = 0; line < options.Lines; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.IncludeBufferMarkers && line > 0 && line % 1_000_000 == 0)
            {
                await writer.WriteLineAsync(line % 2_000_000 == 0 ? "--------- beginning of system" : "--------- beginning of main").ConfigureAwait(false);
            }

            if (random.NextDouble() < options.UnknownLineRate)
            {
                await writer.WriteLineAsync($"malformed synthetic evidence {line}").ConfigureAwait(false);
                continue;
            }

            instant = instant.AddMilliseconds(random.Next(0, 4));
            var eventInstant = random.NextDouble() < options.OutOfOrderRate ? instant.AddMilliseconds(-random.Next(1, 5000)) : instant;
            var pid = random.Next(100, 20_000);
            var tid = random.Next(100, 30_000);
            var level = (LogLevel)random.Next(0, 6);
            var tag = Tags[random.Next(Tags.Length)];
            var pattern = Messages[random.Next(Messages.Length)];
            var message = string.Format(CultureInfo.InvariantCulture, pattern, random.Next(0, 100_000), random.Next(0, 5000));
            var formatted = Format(eventInstant, pid, tid, level, tag, message, options.Format);
            await writer.WriteLineAsync(formatted).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Format(
        DateTimeOffset instant,
        int pid,
        int tid,
        LogLevel level,
        string tag,
        string message,
        LogcatFormat format) =>
        format switch
        {
            LogcatFormat.Epoch => string.Create(
                CultureInfo.InvariantCulture,
                $"{InstantUs.FromDateTimeOffset(instant).Value / 1_000_000m:F6} {pid,5} {tid,5} {level.ToLetter()} {tag}: {message}"),
            LogcatFormat.Time => $"{instant:MM-dd HH:mm:ss.fff} {level.ToLetter()}/{tag}({pid,5}): {message}",
            LogcatFormat.Brief => $"{level.ToLetter()}/{tag}({pid,5}): {message}",

            // Long format is two lines and a blank one: a bracketed header, the message on its
            // own, then a separator. It used to fall through to the ThreadTime arm, so
            // `--format long` produced ThreadTime and the detector duly reported ThreadTime —
            // the option was accepted and silently ignored, which is a worse failure than
            // rejecting it (PLAN-01). The header shape is exactly what LogcatParser.TryLong
            // reads back: "[ MM-dd HH:mm:ss.fff  pid: tid L/Tag ]".
            LogcatFormat.LongFormat =>
                $"[ {instant:MM-dd HH:mm:ss.fff} {pid,5}:{tid,5} {level.ToLetter()}/{tag} ]\n{message}\n",
            _ => $"{instant:MM-dd HH:mm:ss.ffffff} {pid,5} {tid,5} {level.ToLetter()} {tag,-16}: {message}",
        };
}
