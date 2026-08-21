using VisualCat.Core.Parsing;

namespace VisualCat.Core.Tests;

public sealed class LogcatRecordOriginTests
{
    [Theory]
    // Verbatim shapes from devices, under the flags the on-device Android source asks for:
    // -v threadtime,year,UTC,usec. The zone token is the one that broke it.
    [InlineData("2026-08-21 08:31:36.547453 +0000  3911  3911 I adbd    : adbd service requested", 3911)]
    [InlineData("2026-07-19 21:08:42.081222 +0000  1567  1626 I HfLooper: lux = 0.0", 1567)]
    [InlineData("2026-07-19 23:08:42.081222 +0200  1567  1626 I HfLooper: lux = 0.0", 1567)]
    [InlineData("2026-07-19 23:08:42.081222 +02:00  1567  1626 I HfLooper: lux = 0.0", 1567)]

    // And the same record with each modifier dropped in turn, because the source is not the
    // only thing that chooses these and a format is not a promise.
    [InlineData("2026-07-19 21:08:42.081222  1567  1626 I HfLooper: lux = 0.0", 1567)]
    [InlineData("2025-05-15 14:13:37.496  1073  1151 A Runtime: crash", 1073)]
    [InlineData("07-19 21:08:42.522 +0000  3151  3612 W BluetoothMetricsLogger: not initialized", 3151)]
    [InlineData("05-15 14:13:37.496  1073  1151 D rlsservice: hello: world", 1073)]

    // Tags with colons and brackets are ordinary on Android, and a padded tag column is
    // ordinary on Samsung. Neither is the reader's business, but both must not confuse it.
    [InlineData("07-19 23:08:41.843  1854  1854 W binder:1854_2: type=1400 audit(0.0:108845)", 1854)]
    [InlineData("05-17 12:14:15.906  2044  2443 I WifiClientModeImpl[16509:wlan0]: up", 2044)]
    [InlineData("05-17 12:27:34.246  1108 28893 D AF::TrackHandle: ", 1108)]

    // The ceiling: pid_max on 64-bit Android is 4194304.
    [InlineData("2026-08-21 08:16:03.348977 +0000 4194304 4194304 F Tag: m", 4194304)]
    public void ReadsThePidWhateverModifiersTheFormatCarries(string record, int expected)
    {
        Assert.True(LogcatRecordOrigin.TryReadProcessId(record, out var pid));
        Assert.Equal(expected, pid);
    }

    [Fact]
    public void ZoneOffsetIsNeverMistakenForAProcessId()
    {
        // The defect this exists to prevent. The pid was taken to be the third
        // whitespace-separated token, which -v UTC makes the zone offset, and int.TryParse
        // read "+0000" back as 0 because NumberStyles.Integer allows a leading sign. Every
        // record on the phone then looked as though another process had written it, so a
        // capture that could only ever see its own log lines announced itself as full-device
        // and then delivered nothing for as long as it was left running.
        const int ownPid = 19163;
        const string ourOwnRecord =
            "2026-08-21 08:16:02.918753 +0000 19163 19196 I VisualCat: Live capture connected";

        Assert.True(LogcatRecordOrigin.TryReadProcessId(ourOwnRecord, out var pid));
        Assert.Equal(ownPid, pid);
        Assert.NotEqual(0, pid);
    }

    [Theory]
    // Buffer dividers open every stream and are not records.
    [InlineData("--------- beginning of main")]
    [InlineData("--------- beginning of radio")]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("a wrapped message line carrying no structure at all")]

    // -v epoch is a different family, and answering for a format this does not know would be
    // guessing. Refusing is the safe failure.
    [InlineData("1747311217.496123 1073 1151 W Wifi: slow")]

    // pid 0 is the kernel's idle task and writes no records, so reading one means the line
    // was misread rather than received.
    [InlineData("2026-08-21 08:16:03.348977 +0000     0     0 I Tag: m")]

    // A signed token is not a pid, however int.TryParse would read it.
    [InlineData("2026-08-21 08:16:03.348977 +0000 +1234  1626 I Tag: m")]
    [InlineData("2026-08-21 08:16:03.348977 +0000 -1234  1626 I Tag: m")]

    // Neither is a token that only starts out as a number.
    [InlineData("2026-08-21 08:16:03.348977 +0000  39a1  3911 I Tag: m")]

    // S is logcat's silent priority; no record is ever written at it.
    [InlineData("2026-08-21 08:16:03.348977 +0000  3911  3911 S Tag: m")]

    // A record that stops before the tag is not an answer: a number cut in half reads
    // perfectly well as a smaller one.
    [InlineData("2026-08-21 08:16:03.348977 +0000  3911  3911 I")]
    [InlineData("2026-08-21 08:16:03.348977 +0000  3911")]
    [InlineData("2026-08-21 08:16:03.348977")]
    public void RefusesAnythingItCannotReadWithCertainty(string record)
    {
        Assert.False(LogcatRecordOrigin.TryReadProcessId(record, out var pid));
        Assert.Equal(0, pid);
    }

    [Fact]
    public void APrefixOfTheDocumentedLengthIsAlwaysEnoughToRead()
    {
        // What a caller buffering MaximumPrefixLength characters is promised, at every part's
        // widest: the year, microseconds, an offset carrying a colon, and pids at pid_max.
        var record =
            "2026-08-21 08:16:03.348977 +02:00 4194304 4194304 I AVeryLongTagIndeed: " +
            new string('x', 4096);
        var prefix = record.AsSpan(0, LogcatRecordOrigin.MaximumPrefixLength);

        Assert.True(LogcatRecordOrigin.TryReadProcessId(prefix, out var pid));
        Assert.Equal(4194304, pid);
    }

    [Fact]
    public void ADeviceStreamIsSplitIntoOursAndTheirsCorrectly()
    {
        // The decision the on-device source makes, over a slice of a real device stream: with
        // the offset token present, exactly one of these was written by us.
        const int ownPid = 19163;
        string[] stream =
        [
            "--------- beginning of main",
            "2026-08-21 08:16:02.918753 +0000 19163 19196 I VisualCat: Live capture connected",
            "2026-08-21 08:31:36.547453 +0000  3911  3911 I adbd    : adbd service requested",
            "2026-08-21 08:31:36.811376 +0000  3957  3957 D ARGOS   : argos_monitor: UFS",
        ];

        var foreign = stream
            .Where(record => LogcatRecordOrigin.TryReadProcessId(record, out var pid) && pid != ownPid)
            .ToArray();

        Assert.Equal(2, foreign.Length);
        Assert.All(foreign, record => Assert.DoesNotContain("VisualCat", record, StringComparison.Ordinal));
    }
}
