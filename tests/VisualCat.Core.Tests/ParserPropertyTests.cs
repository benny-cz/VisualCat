using System.Globalization;
using System.Text;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;

namespace VisualCat.Core.Tests;

/// <summary>
/// Properties the parser must hold for every input, rather than examples it must handle.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ParserTests"/> asserts shapes captured from real devices, which is what
/// caught most of what it caught. It cannot catch a field whose <em>width</em> changes the
/// spelling, because nobody thinks to capture that sample until the bug is already known:
/// the long format prints its identity field as <c>%5d:%5d</c>, so a five-digit thread id
/// fills the column and the colon is followed by a digit rather than a space. Reading the
/// field by whitespace saw only one spelling and filed 472 of 4,000 records from a real
/// Galaxy S21 FE as continuations of the record above them (finding F-01).
/// </para>
/// <para>
/// That bug is invisible to a line-accounting check — a reclassified record still balances
/// the books, and both counters a reader would check said zero. What does see it is a
/// round trip: render a record whose fields are known by construction, parse it back, and
/// require every field to survive. These sweep the widths rather than sampling them, so
/// the case nobody thought to capture is covered by the same loop as the case everybody
/// did.
/// </para>
/// </remarks>
public sealed class ParserPropertyTests
{
    /// <summary>
    /// One digit through seven, spanning both sides of the format's five-column field:
    /// under it (the value is space-padded), exactly filling it, and over it (the column
    /// overflows and the padding disappears entirely).
    /// </summary>
    private static readonly int[] Widths = [7, 42, 926, 1073, 10503, 99999, 1234567];

    private static readonly LogLevel[] Levels =
        [LogLevel.Verbose, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];

    /// <summary>
    /// Tags that are ordinary on a real device and awkward for a parser: colons inside the
    /// tag collide with the tag/message separator, brackets collide with the long format's
    /// own delimiters, and a tag long enough to pass the threadtime column width tests that
    /// the column is a minimum rather than a maximum.
    /// </summary>
    private static readonly string[] Tags =
    [
        "A",
        "Camera",
        "binder:1854_2",
        "AF::TrackHandle",
        "WifiClientModeImpl[16509:wlan0]",
        "AVeryLongTagNameThatExceedsSixteen",
    ];

    private static readonly string[] Messages =
    [
        "",
        "x",
        "hello: world",
        "uid=10007(com.example) identical 3 lines",
        "příliš žluťoučký kůň úpěl ódy",
        "trailing spaces   ",
        "] ] ] not the end of a long header",
    ];

    private static readonly LogcatFormat[] Formats =
    [
        LogcatFormat.ThreadTime,
        LogcatFormat.Time,
        LogcatFormat.Brief,
        LogcatFormat.Epoch,
        LogcatFormat.LongFormat,
    ];

    public static TheoryData<LogcatFormat> EveryFormat
    {
        get
        {
            var data = new TheoryData<LogcatFormat>();
            foreach (var format in Formats)
            {
                data.Add(format);
            }

            return data;
        }
    }

    /// <summary>
    /// Every process and thread id width, in every format, survives the trip out and back.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFormat))]
    public void EveryIdentityWidthRoundTrips(LogcatFormat format)
    {
        var checkedCases = 0;
        var variant = 0;
        foreach (var pid in Widths)
        {
            foreach (var tid in Widths)
            {
                // The level, tag and message rotate rather than multiplying the sweep: the
                // width combination is what this test exists to cover exhaustively, and a
                // cross product of everything would trade minutes of CI for no new signal.
                var level = Levels[variant % Levels.Length];
                var tag = Tags[variant % Tags.Length];
                var message = Messages[variant % Messages.Length];
                variant++;

                var line = Render(format, pid, tid, level, tag, message);
                var outcome = Parse(line, format);

                Assert.True(
                    outcome.Kind is ParseOutcomeKind.ParsedEntry or ParseOutcomeKind.UntimedEntry,
                    $"{format} pid={pid} tid={tid} was {outcome.Kind} ({outcome.Reason}) for: {line.TrimEnd('\n')}");

                var fields = outcome.Fields;
                Assert.NotNull(fields);
                Assert.Equal(format, fields.Format);
                Assert.Equal(pid, fields.Pid);
                Assert.Equal(level, fields.Level);
                Assert.Equal(tag, fields.Tag);

                // Time and Brief carry no thread id at all, and the long format's message
                // lives on its own following line rather than in the header.
                if (format is not (LogcatFormat.Time or LogcatFormat.Brief))
                {
                    Assert.Equal(tid, fields.Tid);
                }

                Assert.Equal(format == LogcatFormat.LongFormat ? string.Empty : message, fields.Message);
                checkedCases++;
            }
        }

        // A sweep that stopped sweeping would otherwise pass in silence.
        Assert.Equal(Widths.Length * Widths.Length, checkedCases);
    }

    /// <summary>
    /// The long format's two identity spellings both parse, and to the same thread id.
    /// </summary>
    /// <remarks>
    /// Stated separately from the sweep above because this is the exact shape of F-01, and
    /// a failure here should name the bug rather than one coordinate of a loop.
    /// </remarks>
    [Fact]
    public void BothLongFormatIdentitySpellingsReadTheSameThreadId()
    {
        // "  926: 9315" — four digits, so the column pads and a space follows the colon.
        var spaced = Parse("[ 05-15 14:13:37.003   926: 9315 W/Camera ]\n", LogcatFormat.LongFormat);

        // "  926:12019" — five digits fill the column and the space disappears.
        var packed = Parse("[ 05-15 14:13:37.004   926:12019 W/Camera ]\n", LogcatFormat.LongFormat);

        Assert.Equal(ParseOutcomeKind.ParsedEntry, spaced.Kind);
        Assert.Equal(ParseOutcomeKind.ParsedEntry, packed.Kind);
        Assert.Equal(926, spaced.Fields!.Pid);
        Assert.Equal(926, packed.Fields!.Pid);
        Assert.Equal(9315, spaced.Fields.Tid);
        Assert.Equal(12019, packed.Fields.Tid);
    }

    /// <summary>
    /// A clean corpus of a format is detected as that format, and confidently.
    /// </summary>
    /// <remarks>
    /// Confidence used to measure how many fields a format carries rather than how certainly
    /// the file is that format, so a flawless <c>brief</c> capture — which has no timestamp
    /// to carry — scored 4 of 6 and sat barely above the review threshold, while a healthy
    /// <c>long</c> file could not exceed about 0.5 because two thirds of its lines are,
    /// correctly, not headers. Both are properties of the format rather than of the file, so
    /// both belong in a loop over formats.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryFormat))]
    public void ACleanCorpusIsDetectedAsItsOwnFormat(LogcatFormat format)
    {
        var samples = new List<ReadOnlyMemory<byte>>();
        for (var index = 0; index < 60; index++)
        {
            // The identity widths rotate here too. A corpus of four-digit thread ids only
            // is a corpus of one spelling, and F-01 did not merely drop those records — so
            // many long headers failed that an ordinary `-v long` dump was refused outright
            // as an unrecognised format. Detection is downstream of the same parse, so it
            // has to be shown the same spread.
            var line = Render(
                format,
                Widths[index % Widths.Length],
                Widths[(index / Widths.Length) % Widths.Length],
                Levels[index % Levels.Length],
                Tags[index % Tags.Length],
                $"detection sample {index}");
            foreach (var physical in line.TrimEnd('\n').Split('\n'))
            {
                samples.Add(Encoding.UTF8.GetBytes(physical));
            }
        }

        var detection = FormatDetector.Detect(samples);

        Assert.Equal(format, detection.PrimaryFormat);
        Assert.True(
            detection.Confidence >= 0.75,
            $"{format} scored {detection.Confidence:F3} on a flawless corpus of itself.");
    }

    /// <summary>
    /// The parser is total: any bytes at all produce an outcome rather than an exception.
    /// </summary>
    /// <remarks>
    /// The session reader has had a corruption fuzzer since §20.8, but the reader consumes
    /// what this product wrote and the parser consumes whatever a user opens — which is the
    /// side actually facing untrusted input. Seeded so a failure is reproducible from the
    /// iteration number alone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryFormat))]
    public void ArbitraryBytesProduceAnOutcomeRatherThanAnException(LogcatFormat format)
    {
        var random = new Random(0x10CCA7);
        var kinds = new HashSet<ParseOutcomeKind>();

        for (var iteration = 0; iteration < 4000; iteration++)
        {
            var bytes = NextCorruption(random, format, iteration);
            var source = new SourceLine(Guid.Empty, iteration, new RawSpan(0, bytes.Length), bytes);

            ParseOutcome outcome;
            try
            {
                outcome = LogcatParser.Parse(source, format);
            }
            catch (Exception exception)
            {
                var printable = Encoding.UTF8.GetString(bytes).Replace("\n", "<LF>", StringComparison.Ordinal);
                throw new Xunit.Sdk.XunitException(
                    $"Iteration {iteration} of {format} threw {exception.GetType().Name}: {exception.Message}{Environment.NewLine}Input: {printable}");
            }

            // Whatever it decided, it must still be describing the line it was handed.
            Assert.Equal(iteration, outcome.Source.Sequence);
            Assert.Equal(bytes.Length, outcome.Source.Raw.Length);
            if (outcome.Fields is { } fields)
            {
                Assert.True(fields.MessageByteOffset >= 0, $"Iteration {iteration} reported a negative message offset.");
                Assert.True(
                    fields.MessageByteOffset + fields.MessageByteLength <= bytes.Length,
                    $"Iteration {iteration} reported a message running {fields.MessageByteOffset + fields.MessageByteLength - bytes.Length} bytes past the end of its line.");
            }

            kinds.Add(outcome.Kind);
        }

        // Both outcomes must occur, or the corruption never reached anything interesting.
        Assert.True(
            kinds.Count >= 2,
            $"{format} answered every one of 4,000 mutations with {string.Join(", ", kinds)}; the fuzzer is not exercising the parser.");
    }

    /// <summary>
    /// Probe agrees with Parse about whether a line belongs to a format.
    /// </summary>
    /// <remarks>
    /// <see cref="LogcatParser.Probe"/> is the cheap pre-filter detection runs over a sample
    /// and <see cref="LogcatParser.Parse"/> is what the import then does with every line. If
    /// the two disagree, a file is detected as one thing and read as another — which is the
    /// mechanism behind a `long` dump being refused outright as unrecognised while its lines
    /// parsed perfectly well.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryFormat))]
    public void ProbeAcceptsWhatParseAccepts(LogcatFormat format)
    {
        var agreed = 0;
        var variant = 0;
        foreach (var pid in Widths)
        {
            foreach (var tid in Widths)
            {
                var rendered = Render(
                    format,
                    pid,
                    tid,
                    Levels[variant % Levels.Length],
                    Tags[variant % Tags.Length],
                    "probe agreement");
                variant++;

                // Only the header line of a long record is a candidate for its format; the
                // body and the separator are deliberately not.
                var header = rendered.TrimEnd('\n').Split('\n')[0];
                var bytes = Encoding.UTF8.GetBytes(header);

                var probed = LogcatParser.Probe(bytes, format);
                var parsed = LogcatParser.Parse(
                    new SourceLine(Guid.Empty, 0, new RawSpan(0, bytes.Length), bytes), format);

                if (parsed.Kind is ParseOutcomeKind.ParsedEntry or ParseOutcomeKind.UntimedEntry)
                {
                    Assert.True(
                        probed > 0,
                        $"{format} parsed pid={pid} tid={tid} as an entry but probed it as {probed}: {header}");
                    agreed++;
                }
            }
        }

        Assert.Equal(Widths.Length * Widths.Length, agreed);
    }

    /// <summary>
    /// Builds one record in the given format, exactly as Android spells it.
    /// </summary>
    /// <remarks>
    /// Written here rather than reused from the product so that the test and the parser are
    /// two independent statements of the same format. Sharing one renderer would let a
    /// parser and a corpus drift together and still agree with each other, which is the
    /// failure this whole file exists to make impossible.
    /// </remarks>
    private static string Render(
        LogcatFormat format,
        int pid,
        int tid,
        LogLevel level,
        string tag,
        string message)
    {
        var instant = new DateTimeOffset(2026, 5, 15, 14, 13, 37, 496, TimeSpan.Zero);
        return format switch
        {
            LogcatFormat.Epoch => string.Create(
                CultureInfo.InvariantCulture,
                $"{instant.ToUnixTimeMilliseconds() / 1000m:F6} {pid,5} {tid,5} {level.ToLetter()} {tag}: {message}\n"),
            LogcatFormat.Time =>
                $"{instant:MM-dd HH:mm:ss.fff} {level.ToLetter()}/{tag}({pid,5}): {message}\n",
            LogcatFormat.Brief =>
                $"{level.ToLetter()}/{tag}({pid,5}): {message}\n",
            LogcatFormat.LongFormat =>
                $"[ {instant:MM-dd HH:mm:ss.fff} {pid,5}:{tid,5} {level.ToLetter()}/{tag} ]\n{message}\n\n",
            _ =>
                $"{instant:MM-dd HH:mm:ss.ffffff} {pid,5} {tid,5} {level.ToLetter()} {tag,-16}: {message}\n",
        };
    }

    /// <summary>
    /// A valid line of the format, damaged in one of the ways real input is damaged.
    /// </summary>
    private static byte[] NextCorruption(Random random, LogcatFormat format, int iteration)
    {
        var seed = Render(
            format,
            random.Next(1, 200_000),
            random.Next(1, 200_000),
            Levels[random.Next(Levels.Length)],
            Tags[random.Next(Tags.Length)],
            Messages[random.Next(Messages.Length)]);
        var bytes = Encoding.UTF8.GetBytes(seed.TrimEnd('\n').Split('\n')[0]);

        switch (iteration % 8)
        {
            case 0:
                return bytes;
            case 1:
                // A flipped bit anywhere, including inside a multi-byte character.
                if (bytes.Length > 0) { bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8)); }
                return bytes;
            case 2:
                // Cut short, which is what a partially written or truncated log looks like.
                return bytes[..random.Next(bytes.Length + 1)];
            case 3:
                // Bytes that are not valid UTF-8 at all.
                var invalid = new byte[random.Next(1, 64)];
                random.NextBytes(invalid);
                return [.. bytes, .. invalid];
            case 4:
                // Embedded control characters, including the carriage return that made a
                // whole file read as one enormous record.
                return [.. bytes, (byte)'\r', .. bytes];
            case 5:
                // A very long line, which is what a stack trace or a base64 blob produces.
                return Encoding.UTF8.GetBytes(new string('A', random.Next(4096, 16384)));
            case 6:
                return [];
            default:
                // Random bytes with no seed at all.
                var noise = new byte[random.Next(0, 256)];
                random.NextBytes(noise);
                return noise;
        }
    }

    private static ParseOutcome Parse(string text, LogcatFormat format)
    {
        // Only the first physical line is the record's own; the long format's body and
        // separator are separate lines that the import pipeline attaches afterwards.
        var first = text.TrimEnd('\n').Split('\n')[0];
        var bytes = Encoding.UTF8.GetBytes(first);
        return LogcatParser.Parse(new SourceLine(Guid.Empty, 0, new RawSpan(0, bytes.Length), bytes), format);
    }
}
