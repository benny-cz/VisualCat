using System.Globalization;
using System.Text;
using VisualCat.Core.Parsing;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;

namespace VisualCat.Core.Tests;

public sealed class ParserTests
{
    public static TheoryData<string, LogcatFormat, int, int, LogLevel, string, string> Cases => new()
    {
        { "05-15 14:13:37.496  1073  1151 D rlsservice: hello: world\n", LogcatFormat.ThreadTime, 1073, 1151, LogLevel.Debug, "rlsservice", "hello: world" },
        { "2025-05-15 14:13:37.496123  1073  1151 A Runtime: crash\n", LogcatFormat.ThreadTime, 1073, 1151, LogLevel.Fatal, "Runtime", "crash" },
        { "05-15 14:13:37.496 D/rlsservice( 1073): hello\n", LogcatFormat.Time, 1073, 0, LogLevel.Debug, "rlsservice", "hello" },
        { "D/rlsservice( 1073): hello\n", LogcatFormat.Brief, 1073, 0, LogLevel.Debug, "rlsservice", "hello" },
        { "1747311217.496123 1073 1151 W Wifi: slow\n", LogcatFormat.Epoch, 1073, 1151, LogLevel.Warn, "Wifi", "slow" },
        { "[ 05-15 14:13:37.496  1073: 1151 D/rlsservice ]\n", LogcatFormat.LongFormat, 1073, 1151, LogLevel.Debug, "rlsservice", "" },

        // Android prints the long format's identity field as "%5d:%5d". A five-digit thread id
        // fills the column, so there is no space after the colon and "pid:tid" arrives as a
        // single token; a four-digit one leaves a space. Reading the field by whitespace saw
        // only the spaced spelling, so 472 of 4,000 records from an SM-G990B were dropped and
        // booked as continuations of the record above (finding F-01). All four width
        // combinations must parse.
        { "[ 05-15 14:13:37.003   926: 9315 W/Camera ]\n", LogcatFormat.LongFormat, 926, 9315, LogLevel.Warn, "Camera", "" },
        { "[ 05-15 14:13:37.004   926:12019 W/Camera ]\n", LogcatFormat.LongFormat, 926, 12019, LogLevel.Warn, "Camera", "" },
        { "[ 05-15 14:13:37.005 10503:12019 W/Camera ]\n", LogcatFormat.LongFormat, 10503, 12019, LogLevel.Warn, "Camera", "" },
        { "[ 05-15 14:13:37.006 10503: 5136 W/Camera ]\n", LogcatFormat.LongFormat, 10503, 5136, LogLevel.Warn, "Camera", "" },
        { "[ 05-15 14:13:37.007 +0000 10503:12019 W/Camera ]\n", LogcatFormat.LongFormat, 10503, 12019, LogLevel.Warn, "Camera", "" },

        // Verbatim shapes captured from a device (motorola edge 60 pro, API 35) under
        // -v threadtime[,year][,UTC|zone][,usec]. Tags containing colons are ordinary
        // on Android: binder worker threads and AudioFlinger classes both produce them.
        { "2026-07-19 21:08:42.081222 +0000  1567  1626 I HfLooper: lux = 0.0\n", LogcatFormat.ThreadTime, 1567, 1626, LogLevel.Info, "HfLooper", "lux = 0.0" },
        { "2026-07-19 23:08:42.081222 +0200  1567  1626 I HfLooper: lux = 0.0\n", LogcatFormat.ThreadTime, 1567, 1626, LogLevel.Info, "HfLooper", "lux = 0.0" },
        { "07-19 21:08:42.522 +0000  3151  3612 W BluetoothMetricsLogger: not initialized\n", LogcatFormat.ThreadTime, 3151, 3612, LogLevel.Warn, "BluetoothMetricsLogger", "not initialized" },
        { "07-19 23:08:41.843  1854  1854 W binder:1854_2: type=1400 audit(0.0:108845)\n", LogcatFormat.ThreadTime, 1854, 1854, LogLevel.Warn, "binder:1854_2", "type=1400 audit(0.0:108845)" },
        { "05-17 12:27:34.246  1108 28893 D AF::TrackHandle: \n", LogcatFormat.ThreadTime, 1108, 28893, LogLevel.Debug, "AF::TrackHandle", "" },
        { "05-17 12:14:15.906  2044  2443 I WifiClientModeImpl[16509:wlan0]: up\n", LogcatFormat.ThreadTime, 2044, 2443, LogLevel.Info, "WifiClientModeImpl[16509:wlan0]", "up" },
        { "07-19 23:08:42.851 W/binder:1854_2( 1854): denied\n", LogcatFormat.Time, 1854, 0, LogLevel.Warn, "binder:1854_2", "denied" },
        { "         1784495322.629  3151  3612 W BluetoothMetricsLogger: x\n", LogcatFormat.Epoch, 3151, 3612, LogLevel.Warn, "BluetoothMetricsLogger", "x" },
    };

    [Theory]
    [InlineData("2026-07-19 21:08:42.081222 +0000  1  2 I T: m", TimestampProvenance.ExplicitUtc, "2026-07-19T21:08:42.081222Z")]
    [InlineData("2026-07-19 23:08:42.081222 +0200  1  2 I T: m", TimestampProvenance.ExplicitOffset, "2026-07-19T21:08:42.081222Z")]
    [InlineData("2026-07-19 23:08:42.081222 -0730  1  2 I T: m", TimestampProvenance.ExplicitOffset, "2026-07-20T06:38:42.081222Z")]
    [InlineData("2026-07-19 23:08:42.081222 +02:00  1  2 I T: m", TimestampProvenance.ExplicitOffset, "2026-07-19T21:08:42.081222Z")]
    public void ZoneModifierYieldsExplicitProvenanceWithoutInference(
        string line,
        TimestampProvenance expected,
        string expectedInstant)
    {
        var outcome = Parse(line + "\n", LogcatFormat.ThreadTime);
        Assert.NotNull(outcome.Fields?.Timestamp);

        // A policy zone deliberately unrelated to the stated offset: an explicit offset
        // must win over inference, otherwise the instant silently moves.
        var resolver = new TimestampResolver(
            new TimestampPolicy(null, "America/New_York", new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)));
        var resolved = resolver.Resolve(outcome.Fields.Timestamp);

        Assert.Equal(expected, resolved.Provenance);
        Assert.Equal(1d, resolved.Confidence);
        Assert.False(resolved.Attributes.HasFlag(EntryAttributes.InferredTimestamp));
        Assert.Equal(
            DateTimeOffset.Parse(expectedInstant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            resolved.Instant!.Value.ToDateTimeOffset());
    }

    [Fact]
    public void MicrosecondPrecisionSurvivesTheZoneToken()
    {
        var outcome = Parse("2026-07-19 21:08:42.081222 +0000  1  2 I T: m\n", LogcatFormat.ThreadTime);
        Assert.Equal(81_222, outcome.Fields?.Timestamp?.Microsecond);
        Assert.Equal("2026-07-19 21:08:42.081222 +0000", outcome.Fields?.Timestamp?.OriginalText);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParsesRequiredFormats(
        string text,
        LogcatFormat format,
        int pid,
        int tid,
        LogLevel level,
        string tag,
        string message)
    {
        var outcome = Parse(text, format);
        Assert.True(outcome.Kind is ParseOutcomeKind.ParsedEntry or ParseOutcomeKind.UntimedEntry);
        Assert.NotNull(outcome.Fields);
        Assert.Equal(pid, outcome.Fields.Pid);
        Assert.Equal(tid, outcome.Fields.Tid);
        Assert.Equal(level, outcome.Fields.Level);
        Assert.Equal(tag, outcome.Fields.Tag);
        Assert.Equal(message, outcome.Fields.Message);
    }

    [Fact]
    public void PreservesUnknownSeverity()
    {
        var outcome = Parse("05-15 14:13:37.496  1  2 Q Tag: message\n", LogcatFormat.ThreadTime);
        Assert.Equal(LogLevel.Unknown, outcome.Fields?.Level);
    }

    [Fact]
    public void RecognizesMetaBlankMalformedAndInvalidEncoding()
    {
        Assert.Equal(ParseOutcomeKind.MetaRecord, Parse("--------- beginning of system\n", LogcatFormat.ThreadTime).Kind);

        // logcat -D prints this one on every crossing of the merged stream, and it is the only
        // per-record buffer signal there is: without it a -b all capture stamped whichever
        // buffer happened to be announced last on everything after it (finding F-12).
        var switched = Parse("--------- switch to radio\n", LogcatFormat.ThreadTime);
        Assert.Equal(ParseOutcomeKind.MetaRecord, switched.Kind);
        Assert.Equal("buffer:radio", switched.Reason);
        Assert.Equal(ParseOutcomeKind.IgnoredBlank, Parse("\n", LogcatFormat.ThreadTime).Kind);
        Assert.Equal(ParseOutcomeKind.UnknownLine, Parse("not a header\n", LogcatFormat.ThreadTime).Kind);
        var bytes = new byte[] { (byte)'D', (byte)'/', 0xff, (byte)'(', (byte)'1', (byte)')', (byte)':', (byte)' ', (byte)'x' };
        var source = new SourceLine(Guid.NewGuid(), 0, new RawSpan(0, bytes.Length), bytes);
        var invalid = LogcatParser.Parse(source, LogcatFormat.Brief);
        Assert.True(invalid.Fields?.Attributes.HasFlag(EntryAttributes.EncodingFallback));
    }

    [Theory]
    [InlineData(new byte[] { 0xc3 })]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0xc0, 0xaf })]
    [InlineData(new byte[] { 0xed, 0xa0, 0x80 })]
    public void EveryMalformedUtf8ShapeUsesReplacementAndMarksTheEntry(byte[] malformed)
    {
        var prefix = "D/Tag(1): "u8.ToArray();
        var bytes = prefix.Concat(malformed).ToArray();
        var source = new SourceLine(Guid.NewGuid(), 0, new RawSpan(0, bytes.Length), bytes);

        var outcome = LogcatParser.Parse(source, LogcatFormat.Brief);

        var fields = Assert.IsType<ParsedFields>(outcome.Fields);
        Assert.True(fields.Attributes.HasFlag(EntryAttributes.EncodingFallback));
        Assert.Contains('\ufffd', fields.Message);
    }

    [Fact]
    public void MultibyteUtf8StillDecodesWithoutAFallbackMarker()
    {
        var bytes = Encoding.UTF8.GetBytes("D/Tag(1): π 猫");
        var source = new SourceLine(Guid.NewGuid(), 0, new RawSpan(0, bytes.Length), bytes);

        var outcome = LogcatParser.Parse(source, LogcatFormat.Brief);

        Assert.Equal("π 猫", outcome.Fields?.Message);
        Assert.False(outcome.Fields?.Attributes.HasFlag(EntryAttributes.EncodingFallback));
    }

    [Fact]
    public void DetectsFormatUsingValidFields()
    {
        var samples = Enumerable.Range(0, 20)
            .Select(index => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes($"05-15 14:13:37.{index:D3}  1  2 D Tag: message\n"))
            .ToArray();
        var detection = FormatDetector.Detect(samples);
        Assert.Equal(LogcatFormat.ThreadTime, detection.PrimaryFormat);
        Assert.True(detection.Confidence > 0.9);
    }

    [Fact]
    public void Utf8BomDoesNotConsumeTheFirstLogRecord()
    {
        const string line = "05-15 14:13:37.496  1  2 I Tag: first\n";
        var bytes = "\uFEFF"u8.ToArray().Concat(Encoding.UTF8.GetBytes(line)).ToArray();
        var detection = FormatDetector.Detect([bytes]);
        Assert.Equal(LogcatFormat.ThreadTime, detection.PrimaryFormat);

        var outcome = LogcatParser.Parse(
            new SourceLine(Guid.NewGuid(), 0, new RawSpan(0, bytes.Length), bytes),
            LogcatFormat.ThreadTime);
        Assert.Equal(ParseOutcomeKind.ParsedEntry, outcome.Kind);
        Assert.Equal("first", outcome.Fields?.Message);
        Assert.Equal(bytes.Length, outcome.Source.Raw.Length);
    }

    [Fact]
    public void TimestampResolverInfersRolloverAndPreservesOutOfOrder()
    {
        var policy = new TimestampPolicy(null, "UTC", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var resolver = new TimestampResolver(policy);
        var december = resolver.Resolve(Token(12, 31, 23, 59, 59));
        var january = resolver.Resolve(Token(1, 1, 0, 0, 1));
        var inversion = resolver.Resolve(Token(1, 1, 0, 0, 0));
        Assert.NotNull(december.Instant);
        Assert.NotNull(january.Instant);
        Assert.True(january.Instant > december.Instant);
        Assert.True(inversion.Attributes.HasFlag(EntryAttributes.OutOfOrder));
    }

    [Fact]
    public void ArbitraryBoundedInputAlwaysProducesAnExplicitOutcome()
    {
        var random = new Random(0x51CA7);
        foreach (var format in Enum.GetValues<LogcatFormat>().Where(static value => value != LogcatFormat.Unknown))
        {
            for (var iteration = 0; iteration < 2_000; iteration++)
            {
                var bytes = new byte[random.Next(0, 2048)];
                random.NextBytes(bytes);
                var source = new SourceLine(Guid.NewGuid(), iteration, new RawSpan(0, bytes.Length), bytes);
                var outcome = LogcatParser.Parse(source, format);

                Assert.True(Enum.IsDefined(outcome.Kind));
                Assert.Equal(bytes.Length, outcome.Source.Raw.Length);
                Assert.True(outcome.Fields is null ||
                            outcome.Kind is ParseOutcomeKind.ParsedEntry or ParseOutcomeKind.UntimedEntry);
            }
        }
    }

    /// <summary>
    /// The whole point of F-01: a long-format file whose thread ids need all five columns must
    /// yield one entry per header, and nothing may be quietly reclassified as body text of the
    /// record above it. The four hand-written records are exactly as <c>logcat -v long</c>
    /// prints them.
    /// </summary>
    [Fact]
    public void EveryLongFormatIdentityWidthParsesAsItsOwnRecord()
    {
        string[] headers =
        [
            "[ 05-15 14:13:37.003   926: 9315 W/Camera ]",
            "[ 05-15 14:13:37.004   926:12019 W/Camera ]",
            "[ 05-15 14:13:37.005 10503:12019 W/Camera ]",
            "[ 05-15 14:13:37.006 10503: 5136 W/Camera ]",
        ];

        var parsed = headers.Select(header => Parse(header + "\n", LogcatFormat.LongFormat)).ToArray();
        Assert.All(parsed, outcome => Assert.Equal(ParseOutcomeKind.ParsedEntry, outcome.Kind));
        Assert.Equal([926, 926, 10503, 10503], parsed.Select(static outcome => outcome.Fields!.Pid));
        Assert.Equal([9315, 12019, 12019, 5136], parsed.Select(static outcome => outcome.Fields!.Tid));
    }

    /// <summary>
    /// The second half of F-01. A bracketed line carrying a date and a priority/tag is an
    /// attempted header; when it fails validation it must be counted as a rejected candidate,
    /// which the chip bar surfaces and "Lines not on the timeline" can reach — never as a
    /// continuation, which asserts it is message text of the entry above.
    /// </summary>
    [Theory]
    [InlineData("[ 05-15 14:13:37.008 nope:12019 W/Camera ]", ParseOutcomeKind.RejectedCandidate)]
    [InlineData("[ 05-15 14:13:37.009 10503:12019 WW/Camera ]", ParseOutcomeKind.RejectedCandidate)]
    [InlineData("[ 05-15 14:13:37.010 1050312019 W/Camera ]", ParseOutcomeKind.RejectedCandidate)]
    [InlineData("ordinary message body, not bracketed", ParseOutcomeKind.Continuation)]
    [InlineData("[ this is a bracketed message body ]", ParseOutcomeKind.Continuation)]
    public void AMalformedLongHeaderIsRejectedRatherThanFoldedIntoTheRecordAbove(string line, ParseOutcomeKind expected)
    {
        Assert.Equal(expected, Parse(line + "\n", LogcatFormat.LongFormat).Kind);
    }

    private static ParseOutcome Parse(string text, LogcatFormat format)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return LogcatParser.Parse(new SourceLine(Guid.NewGuid(), 0, new RawSpan(0, bytes.Length), bytes), format);
    }

    private static TimestampToken Token(int month, int day, int hour, int minute, int second) =>
        new($"{month:D2}-{day:D2} {hour:D2}:{minute:D2}:{second:D2}.000", null, month, day, hour, minute, second, 0);
}
