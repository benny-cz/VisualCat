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
    bool IncludeBufferMarkers = true,

    // The default corpus has seven tags and seven message shapes, so a million lines of
    // it mine seventy-seven templates and exercise nothing that scales with template
    // diversity. A real device is nothing like that: one minute of a Galaxy S21 FE
    // produced 1,907 tags and 13,057 templates over 324,679 entries. Setting both counts
    // reproduces that shape deterministically.
    int DistinctTags = 0,
    int DistinctTemplates = 0);

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
        ValidateDiversity(options);
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
            string tag;
            string message;
            if (options.DistinctTemplates > 0)
            {
                // One shape belongs to exactly one tag, so the mined cluster count is the
                // requested template count rather than a product of the two.
                var shape = random.Next(options.DistinctTemplates);
                tag = string.Create(CultureInfo.InvariantCulture, $"Svc{shape % options.DistinctTags:0000}");
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"started {ShapeWord(shape)} for user after {random.Next(0, 100_000)} ms");
            }
            else
            {
                tag = Tags[random.Next(Tags.Length)];
                var pattern = Messages[random.Next(Messages.Length)];
                message = string.Format(CultureInfo.InvariantCulture, pattern, random.Next(0, 100_000), random.Next(0, 5000));
            }

            var formatted = Format(eventInstant, pid, tid, level, tag, message, options.Format);
            await writer.WriteLineAsync(formatted).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Both counts are set together or not at all, and a tag may carry at most a hundred
    /// shapes — the miner's default per-node fan-out limit. Beyond that the extra shapes
    /// are routed into one wildcard branch and the corpus stops producing the template
    /// count it was asked for.
    /// </summary>
    private static void ValidateDiversity(SyntheticLogOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.DistinctTags);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DistinctTemplates);
        if (options.DistinctTags == 0 && options.DistinctTemplates == 0)
        {
            return;
        }

        if (options.DistinctTags == 0 || options.DistinctTemplates == 0)
        {
            throw new ArgumentException(
                "Distinct tag and template counts are set together.",
                nameof(options));
        }

        const int shapesPerTag = 100;
        if (options.DistinctTemplates > (long)options.DistinctTags * shapesPerTag)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"{options.DistinctTemplates:N0} templates need at least " +
                $"{(options.DistinctTemplates + shapesPerTag - 1) / shapesPerTag:N0} tags.");
        }
    }

    /// <summary>
    /// A purely alphabetic, fixed-width name for one message shape. Alphabetic because
    /// the miner's masks collapse anything containing a digit, which would merge every
    /// shape into one template.
    /// </summary>
    private static string ShapeWord(int shape)
    {
        Span<char> word = stackalloc char[6];
        var value = (uint)shape;
        for (var index = word.Length - 1; index >= 0; index--)
        {
            word[index] = (char)('a' + (int)(value % 26));
            value /= 26;
        }

        return new string(word);
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
