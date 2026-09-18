using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Core.Generation;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

/// <summary>
/// Every physical line a session read is accounted for by exactly one outcome.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SessionCounters.SourceLines"/> is counted by the reader as it frames lines.
/// The seven outcome kinds are counted by the parser as it decides what each line was.
/// The two numbers are derived independently and must agree, so a line that is read and
/// then quietly dropped — by a batch boundary, a cancelled segment, a resume cursor, a
/// reclassification that forgets to count itself — shows up as a gap that no amount of
/// staring at either number alone would reveal.
/// </para>
/// <para>
/// This does not catch a <em>misclassified</em> line: a record filed as a continuation
/// still balances, which is exactly why finding F-01 survived with both of its counters
/// reading zero. That case belongs to the round-trip properties in
/// <c>ParserPropertyTests</c>. These two guards are complementary, and the product needs
/// both: one says nothing was lost, the other says nothing was relabelled.
/// </para>
/// </remarks>
public sealed class SourceAccountingTests
{
    /// <summary>
    /// The seven outcome kinds partition the source, on a corpus with something of each.
    /// </summary>
    [Theory]
    [InlineData(LogcatFormat.ThreadTime, 1)]
    [InlineData(LogcatFormat.ThreadTime, 4)]
    [InlineData(LogcatFormat.Time, 2)]
    [InlineData(LogcatFormat.Brief, 2)]
    [InlineData(LogcatFormat.Epoch, 2)]
    [InlineData(LogcatFormat.LongFormat, 2)]
    public async Task EveryPhysicalLineIsAccountedForExactlyOnce(LogcatFormat format, int workers)
    {
        var directory = NewSessionPath();
        try
        {
            var bytes = await GenerateAsync(format, lines: 4000);
            await using var source = new MemoryLogSource(bytes, [4096, 512, 65536]);
            var result = await SessionCoordinator.ImportAsync(source, directory, Settings(format, workers));
            using var snapshot = result.Snapshot;
            var counters = snapshot.Descriptor.Counters;

            var attributed =
                counters.ParsedEntries +
                counters.MetaRecords +
                counters.Continuations +
                counters.UnknownLines +
                counters.RejectedCandidates +
                counters.IgnoredBlanks;

            Assert.Equal(counters.SourceLines, attributed);

            // ParsedEntries is the union of the two entry-producing outcomes, so it cannot
            // disagree with its own two halves either.
            Assert.Equal(counters.ParsedEntries, counters.TimedEntries + counters.UntimedEntries);

            // The corpus has to be worth counting. A generator that silently produced an
            // empty file would satisfy every equation above with zeroes.
            Assert.True(counters.SourceLines > 1000, $"Only {counters.SourceLines} lines reached the parser.");
            Assert.True(counters.ParsedEntries > 500, $"Only {counters.ParsedEntries} entries were parsed from {counters.SourceLines} lines.");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <summary>
    /// The shipped corpus generator writes what the shipped parser reads.
    /// </summary>
    /// <remarks>
    /// The generator feeds the performance workflow and <c>vcat generate-test-log</c>, so a
    /// drift between the two would be measured as a performance number and shipped as a
    /// user-facing command, while looking healthy from either side alone. Only the
    /// deliberate malformed-line rate may fail to parse; anything beyond it is the pair
    /// having come apart.
    /// </remarks>
    [Theory]
    [InlineData(LogcatFormat.ThreadTime)]
    [InlineData(LogcatFormat.Time)]
    [InlineData(LogcatFormat.Brief)]
    [InlineData(LogcatFormat.Epoch)]
    [InlineData(LogcatFormat.LongFormat)]
    public async Task TheShippedGeneratorRoundTripsThroughTheShippedParser(LogcatFormat format)
    {
        const int Lines = 4000;
        var directory = NewSessionPath();
        try
        {
            var bytes = await GenerateAsync(format, Lines);
            await using var source = new MemoryLogSource(bytes, [65536]);
            var result = await SessionCoordinator.ImportAsync(source, directory, Settings(format, 2));
            using var snapshot = result.Snapshot;
            var counters = snapshot.Descriptor.Counters;

            // The generator's default malformed rate is 0.0001, so a 4,000-line corpus is
            // expected to contain about one. Allow a handful; refuse a systematic failure.
            const long Tolerance = 10;
            Assert.True(
                counters.UnknownLines + counters.RejectedCandidates <= Tolerance,
                $"{format}: {counters.UnknownLines} unknown and {counters.RejectedCandidates} rejected lines out of " +
                $"{counters.SourceLines}. The generator and the parser disagree about this format.");

            // Every format is asked for the same number of *records*. The long format spends
            // three physical lines on each one — header, body, separator — so its SourceLines
            // is three times the others while its record count is not, which is precisely the
            // asymmetry that makes a raw line count a bad proxy for "did we read the log".
            Assert.True(
                counters.ParsedEntries >= Lines - Tolerance,
                $"{format}: asked for {Lines} records and read back {counters.ParsedEntries}.");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <summary>
    /// A long-format line with no open record to continue is an unknown line, not a body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Core.Parsing.LogcatParser"/> decides a line's kind from the line alone, so in
    /// long format every line that is not a header comes back as a continuation. Whether that is
    /// true depends on state the parser does not have: a blank separator commits the open record,
    /// and a non-header line after one continues nothing. Filing it as a continuation asserts it
    /// is the message text of an entry it is not part of — the same false assertion the malformed
    /// header of the original finding made — and the assembly walk in
    /// <see cref="SessionCoordinator"/> matches no branch for it, so it reached the end of the
    /// loop counted as a body line of a record that had already been written.
    /// </para>
    /// <para>
    /// Neither existing guard sees this. The partition test above still balances, because the
    /// line is attributed — to the wrong population. The parser round trip does not reach it,
    /// because the misclassification is the coordinator's, and the parser's answer for this line
    /// is correct for every context except the one it is in. So the contract is pinned here, on
    /// exact counts rather than on a sum.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ALongFormatLineWithNoOpenRecordIsUnknownRatherThanABody(int workers)
    {
        // Two records — the first with a two-line message, so a genuine continuation is present
        // to tell apart from the orphan — then a line after the separator that continues nothing.
        const string Corpus =
            "[ 05-15 14:13:37.001  5521:22751 I/chatty ]\nline-one\nLINE-TWO-CONTINUATION\n\n" +
            "[ 05-15 14:13:38.002  5521:22752 E/Boom ]\nsecond\n\n" +
            "ORPHAN-TRAILING-LINE\n";

        var directory = NewSessionPath();
        try
        {
            await using var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Corpus), [4096]);
            var result = await SessionCoordinator.ImportAsync(
                source,
                directory,
                Settings(LogcatFormat.LongFormat, workers));
            using var snapshot = result.Snapshot;
            var counters = snapshot.Descriptor.Counters;

            Assert.Equal(8, counters.SourceLines);
            Assert.Equal(2, counters.ParsedEntries);

            // The three real body lines, and only those.
            Assert.Equal(3, counters.Continuations);

            // The orphan. Before the fix this was 0 and Continuations was 4.
            Assert.Equal(1, counters.UnknownLines);

            Assert.Equal(2, counters.IgnoredBlanks);
            Assert.Equal(0, counters.RejectedCandidates);
            Assert.Equal(0, counters.MetaRecords);

            var attributed =
                counters.ParsedEntries +
                counters.MetaRecords +
                counters.Continuations +
                counters.UnknownLines +
                counters.RejectedCandidates +
                counters.IgnoredBlanks;
            Assert.Equal(counters.SourceLines, attributed);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static async Task<byte[]> GenerateAsync(LogcatFormat format, int lines)
    {
        using var buffer = new MemoryStream();
        await SyntheticLogGenerator.GenerateAsync(
            buffer,
            new SyntheticLogOptions(lines, Seed: 1745, Format: format));
        return buffer.ToArray();
    }

    private static string NewSessionPath() =>
        Path.Combine(Path.GetTempPath(), $"visualcat-accounting-{Guid.NewGuid():N}.vcat");

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static IngestSettings Settings(LogcatFormat format, int workers) =>
        new(
            format,
            "utf-8",
            new TimestampPolicy(2025, "UTC", new DateTimeOffset(2025, 5, 16, 0, 0, 0, TimeSpan.Zero)),
            new TemplateSettings(),
            BatchBytes: 8192,
            ChannelCapacity: 4,
            ParseWorkers: workers,
            SegmentEntries: 500,
            PortableRaw: false);
}
