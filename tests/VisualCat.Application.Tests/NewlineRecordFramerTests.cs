using System.Text;
using VisualCat.Application.Ports;

namespace VisualCat.Application.Tests;

public sealed class NewlineRecordFramerTests
{
    [Fact]
    public void FragmentedRecordsAreEmittedOnlyAfterTheirNewline()
    {
        var framer = new NewlineRecordFramer(1024);

        Assert.Empty(framer.Append("first"u8.ToArray()));
        var complete = framer.Append(" line\nsecond\nthird"u8.ToArray());

        Assert.Equal(["first line\n", "second\n"], Decode(complete));
        Assert.Equal("third", Encoding.UTF8.GetString(framer.FlushPending()));
    }

    [Fact]
    public void ReconnectDiscardPreventsCrossTransportRecordCorruption()
    {
        var framer = new NewlineRecordFramer(1024);

        Assert.Empty(framer.Append("old transport partial"u8.ToArray()));
        Assert.Equal(21, framer.DiscardPending());

        var replay = framer.Append("2026-08-23 complete replay\n"u8.ToArray());
        Assert.Equal(["2026-08-23 complete replay\n"], Decode(replay));
        Assert.Empty(framer.FlushPending());
    }

    [Fact]
    public void PendingRecordHasABoundedMemoryLimit()
    {
        var framer = new NewlineRecordFramer(8);

        Assert.Empty(framer.Append("12345678"u8.ToArray()));
        var exception = Assert.Throws<InvalidDataException>(() => framer.Append("9"u8.ToArray()));

        Assert.Contains("8-byte safety limit", exception.Message, StringComparison.Ordinal);
    }

    private static string[] Decode(IReadOnlyList<ReadOnlyMemory<byte>> records) =>
        records.Select(static record => Encoding.UTF8.GetString(record.Span)).ToArray();
}
