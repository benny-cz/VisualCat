using System.Text;
using VisualCat.Core.Parsing;

namespace VisualCat.Core.Tests;

public sealed class BoundedLinePrefixScannerTests
{
    [Fact]
    public void FindsEveryCompleteLineAcrossEveryPossibleSplit()
    {
        const string text = "first\r\nsecond\nthird-is-longer-than-the-cap\nfourth\n";
        var bytes = Encoding.ASCII.GetBytes(text);
        for (var split = 0; split <= bytes.Length; split++)
        {
            var scanner = new BoundedLinePrefixScanner(8);
            var seen = new List<string>();
            scanner.Append(bytes.AsSpan(0, split), prefix => seen.Add(Encoding.ASCII.GetString(prefix)));
            scanner.Append(bytes.AsSpan(split), prefix => seen.Add(Encoding.ASCII.GetString(prefix)));

            Assert.Equal(["first", "second", "third-is", "fourth"], seen);
        }
    }

    [Fact]
    public void ALongBodyIsSkippedAndTheNextRecordStillHasItsOwnPrefix()
    {
        var scanner = new BoundedLinePrefixScanner(4);
        var seen = new List<string>();
        scanner.Append(
            Encoding.ASCII.GetBytes(new string('a', 20_000) + "\nnext\n"),
            prefix => seen.Add(Encoding.ASCII.GetString(prefix)));

        Assert.Equal(["aaaa", "next"], seen);
    }

    [Fact]
    public void UnterminatedAndDiscardedTailsAreNeverReportedAsRecords()
    {
        var scanner = new BoundedLinePrefixScanner(16);
        var seen = new List<string>();
        scanner.Append("partial"u8, prefix => seen.Add(Encoding.ASCII.GetString(prefix)));
        Assert.Empty(seen);

        scanner.Clear();
        scanner.Append("complete\n"u8, prefix => seen.Add(Encoding.ASCII.GetString(prefix)));
        Assert.Equal(["complete"], seen);
    }
}
