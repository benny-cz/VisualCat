using VisualCat.Core.Parsing;

namespace VisualCat.Core.Tests;

public sealed class LogcatResumeCursorTests
{
    private static readonly DateTime DeviceNow =
        new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MergedBufferReorderingCannotMoveTheResumePointBackward()
    {
        var cursor = new LogcatResumeCursor();

        Assert.True(cursor.Observe(Record("11:59:20.500000", 101), DeviceNow, out _));
        Assert.True(cursor.Observe(Record("11:59:20.494495", 102), DeviceNow, out _));
        Assert.True(cursor.Observe(Record("11:59:20.510000", 103), DeviceNow, out _));

        Assert.Equal("1787572759.510000", cursor.ResumeArgument);
        Assert.Equal("2026-08-24 11:59:19.510000", cursor.ResumeUtcTimestamp);
    }

    [Fact]
    public void TimestampLookingTextIsNotARecordCursor()
    {
        var cursor = new LogcatResumeCursor();

        Assert.False(cursor.Observe(
            "2026-08-24 08:04:23.217766 this is message text, not a threadtime header",
            DeviceNow,
            out _));
        Assert.Null(cursor.ResumeArgument);
        Assert.Null(cursor.ResumeUtcTimestamp);
    }

    [Fact]
    public void CursorUsesABoundedInclusiveOverlap()
    {
        var cursor = new LogcatResumeCursor();

        Assert.True(cursor.Observe(Record("11:59:59.123456", 101), DeviceNow, out var reset));

        Assert.False(reset);
        Assert.Equal("1787572798.123456", cursor.ResumeArgument);
        Assert.Equal("2026-08-24 11:59:58.123456", cursor.ResumeUtcTimestamp);
    }

    [Fact]
    public void RealWallClockRollbackBeginsANewEpoch()
    {
        var cursor = new LogcatResumeCursor();
        Assert.True(cursor.Observe(Record("11:59:59.000000", 101), DeviceNow, out _));

        var afterRollback = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(cursor.Observe(
            "2026-08-24 10:00:00.100000 +0000  101  102 I Clock: adjusted",
            afterRollback,
            out var reset));

        Assert.True(reset);
        Assert.Equal("1787565599.100000", cursor.ResumeArgument);
        Assert.Equal("2026-08-24 09:59:59.100000", cursor.ResumeUtcTimestamp);
    }

    [Theory]
    [InlineData("--------- switch to main")]
    [InlineData("2026-08-24 11:59:20.500000")]
    [InlineData("2026-08-24 11:59:20.500000 +0000  0  0 I Tag: impossible pid")]
    public void NonRecordsNeverChangeAValidCursor(string line)
    {
        var cursor = new LogcatResumeCursor();
        Assert.True(cursor.Observe(Record("11:59:20.500000", 101), DeviceNow, out _));

        Assert.False(cursor.Observe(line, DeviceNow, out _));
        Assert.Equal("1787572759.500000", cursor.ResumeArgument);
        Assert.Equal("2026-08-24 11:59:19.500000", cursor.ResumeUtcTimestamp);
    }

    private static string Record(string time, int pid) =>
        $"2026-08-24 {time} +0000  {pid}  {pid + 1} I VisualCat: record";
}
