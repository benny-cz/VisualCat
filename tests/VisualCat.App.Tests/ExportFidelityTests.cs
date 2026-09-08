using Avalonia.Headless.XUnit;
using VisualCat.App.Presentation;
using VisualCat.Application.UseCases;
using VisualCat.Core.Query;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Filters;
using VisualCat.Domain.Queries;

namespace VisualCat.App.Tests;

/// <summary>
/// The file contains what the review promised, byte for byte, in either order and encoding.
/// </summary>
/// <remarks>
/// The scope algebra is proved by <see cref="ExportScopeOracleTests"/>. This is about the
/// other half of the promise: that "N timed rows" is the number of data rows written, that
/// records the timeline never carried are not among them, and that the two per-export options
/// change the output and nothing else.
/// </remarks>
public sealed class ExportFidelityTests
{
    /// <summary>
    /// Four timed entries, a stack-trace continuation, a line no parser claims, and a
    /// candidate the parser rejects — the four outcomes a session can hold at once.
    /// </summary>
    private const string MixedLog =
        "01-01 00:00:00.000000   100   101 E Crash          : first \"quoted\" line\n" +
        "\tat com.example.Thing.method(Thing.java:42)\n" +
        "01-01 00:00:01.000000   100   101 W Crash          : second, with a comma\n" +
        "--------- beginning of crash\n" +
        "01-01 00:00:02.000000   100   101 I Crash          : third\n" +
        "01-01 00:99:99.000000   100   101 I Crash          : rejected timestamp\n" +
        "01-01 00:00:03.000000   100   101 D Crash          : fourth\n";

    [AvaloniaFact]
    public async Task TheRowCountPromisedIsTheNumberOfDataRowsWritten()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(MixedLog);
        var snapshot = fixture.Tab.Snapshot!;
        var session = snapshot.TimedRange!.Value;

        // The session genuinely holds records the timeline does not carry, which is the
        // case the disclosure beside the scope exists for.
        var counters = snapshot.Descriptor.Counters;
        Assert.True(
            counters.UntimedEntries + counters.UnknownLines + counters.RejectedCandidates > 0,
            "the fixture must contain records that are not on the timeline");

        var scope = Assert.Single(
            ExportScopeResolver.Resolve(Request(fixture, session), session),
            static candidate => candidate.Kind == ExportScopeKind.AllTimed);

        var promised = SessionQueryEngine.GetEntries(
            snapshot,
            scope.Range,
            scope.Filter,
            EntryOrder.Chronological,
            cursor: null,
            pageSize: 1,
            queryGeneration: 1).TotalCount;

        var destination = Path.Combine(Path.GetTempPath(), $"visualcat-export-{Guid.NewGuid():N}.csv");
        try
        {
            var written = await ExportService.ExportNormalizedCsvAsync(
                snapshot,
                destination,
                scope.Range,
                scope.Filter,
                EntryOrder.Chronological,
                includeUtf8Bom: false,
                TestContext.Current.CancellationToken);

            var lines = await File.ReadAllLinesAsync(destination, TestContext.Current.CancellationToken);
            Assert.Equal(promised, written);
            Assert.Equal(4, written);

            // The header is not a data row, and nothing off the timeline reached the file.
            Assert.Equal(written + 1, lines.Length);
            Assert.StartsWith("timestamp_utc,level,pid,tid,buffer,tag,template_id,message", lines[0], StringComparison.Ordinal);
            Assert.DoesNotContain(lines, static line => line.Contains("Thing.java", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, static line => line.Contains("beginning of crash", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, static line => line.Contains("rejected timestamp", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [AvaloniaFact]
    public async Task BothOrdersAndBothEncodingsChangeTheOutputAndNothingElse()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(MixedLog);
        var snapshot = fixture.Tab.Snapshot!;
        var session = snapshot.TimedRange!.Value;
        var scope = Assert.Single(
            ExportScopeResolver.Resolve(Request(fixture, session), session),
            static candidate => candidate.Kind == ExportScopeKind.AllTimed);

        var chronological = await WriteAsync(snapshot, scope, EntryOrder.Chronological, bom: false);
        var sourceOrder = await WriteAsync(snapshot, scope, EntryOrder.SourceSequence, bom: false);
        var withBom = await WriteAsync(snapshot, scope, EntryOrder.Chronological, bom: true);

        // Same rows either way; the order names which sequence they come out in.
        Assert.Equal(
            chronological.Rows.OrderBy(static row => row, StringComparer.Ordinal),
            sourceOrder.Rows.OrderBy(static row => row, StringComparer.Ordinal));

        // The encoding is the only difference the byte-order mark makes.
        Assert.Equal([0xEF, 0xBB, 0xBF], withBom.Bytes.Take(3));
        Assert.NotEqual<byte>(0xEF, chronological.Bytes[0]);
        Assert.Equal(chronological.Rows, withBom.Rows);

        // A comma and a quotation mark inside a message survive as one field, and the
        // timestamp keeps every microsecond.
        var quoted = Assert.Single(chronological.Rows, static row => row.Contains("quoted", StringComparison.Ordinal));
        Assert.EndsWith("\"first \"\"quoted\"\" line\"", quoted, StringComparison.Ordinal);
        Assert.Contains(chronological.Rows, static row => row.Contains("\"second, with a comma\"", StringComparison.Ordinal));
        Assert.All(
            chronological.Rows,
            static row => Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00,", row));
    }

    private static async Task<(string[] Rows, byte[] Bytes)> WriteAsync(
        Core.Store.SessionSnapshot snapshot,
        ResolvedExportScope scope,
        EntryOrder order,
        bool bom)
    {
        var destination = Path.Combine(Path.GetTempPath(), $"visualcat-export-{Guid.NewGuid():N}.csv");
        try
        {
            await ExportService.ExportNormalizedCsvAsync(
                snapshot,
                destination,
                scope.Range,
                scope.Filter,
                order,
                bom,
                TestContext.Current.CancellationToken);
            var bytes = await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken);
            var rows = (await File.ReadAllLinesAsync(destination, TestContext.Current.CancellationToken))
                .Skip(1)
                .Where(static line => line.Length > 0)
                .ToArray();
            return (rows, bytes);
        }
        finally
        {
            File.Delete(destination);
        }
    }

    private static FrozenExportRequest Request(LiveTestWorkspaceFixture fixture, Domain.Time.TimeRange session) =>
        new(
            fixture.Tab.Snapshot!.SessionId,
            fixture.Tab.SessionPath,
            fixture.Tab.Title,
            FilterSpec.All,
            session,
            DetailRange: null,
            DetailLevel: null,
            ExplicitRange: null,
            EntryOrder.SourceSequence,
            DefaultIncludeUtf8Bom: false,
            CaptureContinues: false);
}
