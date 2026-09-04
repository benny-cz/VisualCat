using VisualCat.Domain.Queries;
using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

public sealed class CliValidationTests
{
    [Fact]
    public void IntegerAccessorsDistinguishAbsentMissingEmptyMalformedOverflowAndRange()
    {
        Assert.Equal(100, Arguments.Parse([]).GetInt("--limit", 100, 1, 10_000));

        AssertOptionError(["--limit"], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        AssertOptionError(["--limit="], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        AssertOptionError(["--limit", "abc"], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        AssertOptionError(["--limit", "99999999999999999999"], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        AssertOptionError(["--limit", "0"], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        AssertOptionError(["--limit", "10001"], "--limit", options => options.GetInt("--limit", 100, 1, 10_000));
        Assert.Equal(10_000, Arguments.Parse(["--limit", "10000"]).GetInt("--limit", 100, 1, 10_000));
    }

    [Fact]
    public void LongAndIntegerListErrorsNameThePublicOption()
    {
        AssertOptionError(["--lines", "overflow-overflow"], "--lines", options => options.GetLong("--lines", 10, 0));
        AssertOptionError(["--pids", "1,two,3"], "--pids", options => options.GetIntSet("--pids"));
        Assert.Equal([1, 2, 3], Arguments.Parse(["--pids=1,2,3"]).GetIntSet("--pids").Order().ToArray());
    }

    [Theory]
    [InlineData(null, EntryOrder.Chronological)]
    [InlineData("chronological", EntryOrder.Chronological)]
    [InlineData("CHRONOLOGICAL", EntryOrder.Chronological)]
    [InlineData("source", EntryOrder.SourceSequence)]
    [InlineData("SOURCE", EntryOrder.SourceSequence)]
    public void OrderAcceptsOnlyTheTwoDocumentedValues(string? value, EntryOrder expected)
    {
        Assert.Equal(expected, VisualCatCli.ParseOrder(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("src")]
    [InlineData("time")]
    public void InvalidOrderNamesTheOptionAndAllowedValues(string value)
    {
        var exception = Assert.Throws<CommandException>(() => VisualCatCli.ParseOrder(value));
        Assert.Contains("--order", exception.Message, StringComparison.Ordinal);
        Assert.Contains("chronological", exception.Message, StringComparison.Ordinal);
        Assert.Contains("source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidInstantNamesTheOptionAndAcceptedForms()
    {
        var exception = Assert.Throws<CommandException>(() => VisualCatCli.ParseInstant("--from", "not-a-date"));
        Assert.Contains("--from", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ISO-8601", exception.Message, StringComparison.Ordinal);
        Assert.Contains("microseconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliInvalidValuesExitTwoAndNameTheOptionAcrossCommandShapes()
    {
        const string Log =
            "01-01 00:00:03.000   100   101 I Tag: third written first\n" +
            "01-01 00:00:01.000   100   101 I Tag: first in time\n" +
            "01-01 00:00:02.000   100   101 I Tag: second in time\n";
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-cli-values-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(Encoding.UTF8.GetBytes(Log), [4096]))
        {
            var result = await SessionCoordinator.ImportAsync(
                source,
                root,
                new IngestSettings(
                    LogcatFormat.ThreadTime,
                    "utf-8",
                    new TimestampPolicy(2026, "UTC", DateTimeOffset.UtcNow),
                    new TemplateSettings(),
                    PortableRaw: true));
            result.Snapshot.Dispose();
        }

        try
        {
            foreach (var (arguments, option) in new (string[] Arguments, string Option)[]
                     {
                         (["query", root, "--limit", "abc"], "--limit"),
                         (["query", root, "--from", "not-a-date"], "--from"),
                         (["query", root, "--pids", "1,two"], "--pids"),
                         (["query", root, "--order", "src"], "--order"),
                         (["export", root, Path.Combine(root, "bad.log"), "--order=src"], "--order"),
                         (["query", "--limit", root], "--limit"),
                     })
            {
                var (exitCode, error) = await RunWithErrorAsync(arguments);
                Assert.Equal(2, exitCode);
                Assert.Contains(option, error, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertOptionError<T>(
        IReadOnlyList<string> input,
        string option,
        Func<Arguments, T> read)
    {
        var exception = Assert.Throws<CommandException>(() => read(Arguments.Parse(input)));
        Assert.Contains(option, exception.Message, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Error)> RunWithErrorAsync(string[] arguments)
    {
        var previous = Console.Error;
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Console.SetError(error);
        try
        {
            return (await VisualCatCli.RunAsync(arguments), error.ToString());
        }
        finally
        {
            Console.SetError(previous);
        }
    }
}
