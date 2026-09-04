using System.Text;
using VisualCat.Application.Coordination;
using VisualCat.Application.Ports;
using VisualCat.Core.Query;
using VisualCat.Domain.Entries;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Configuration;
using VisualCat.Infrastructure.Testing;

namespace VisualCat.Application.Tests;

[Collection(ProcessConsoleTestGroup.Name)]
public sealed class ImprovementAuditTests
{
    /// <summary>
    /// A regex can compile cleanly and still exceed its match budget on a particular message —
    /// a lookaround forces the backtracking engine, which is what the timeout exists to bound.
    /// The CLI must answer that with a non-zero code and the product's own wording rather than
    /// letting <c>RegexMatchTimeoutException</c> escape, whose text varies by runtime and can be
    /// a bare resource key on a trimmed build.
    /// </summary>
    [Fact]
    public async Task CliReportsARegexTimeoutAsAStableFailureInsteadOfAFrameworkException()
    {
        var hostile = new string('a', 100_000) + "!";
        var log = $"01-01 00:00:00.000   100   101 I Tag: {hostile}" + Environment.NewLine;
        var root = Path.Combine(Path.GetTempPath(), $"visualcat-cli-timeout-{Guid.NewGuid():N}.vcat");
        await using (var source = new MemoryLogSource(Encoding.UTF8.GetBytes(log), [4096]))
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
            var previousError = Console.Error;
            var previousOutput = Console.Out;
            using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            Console.SetError(error);
            Console.SetOut(output);
            int exitCode;
            try
            {
                exitCode = await VisualCatCli.RunAsync(
                    ["search", root, "(?=a)^(a+)+$", "--regex", "--timeout-ms", "1"]);
            }
            finally
            {
                Console.SetError(previousError);
                Console.SetOut(previousOutput);
            }

            Assert.Equal(3, exitCode);
            Assert.Contains(SearchTimeoutException.UserMessage, error.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, output.ToString());

            // An ordinary search over the same session still works, so the timeout is a
            // per-query outcome and not a poisoned session.
            Assert.Equal(0, await VisualCatCli.RunAsync(["search", root, "aaa"]));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(2026, 9, 3, 10, 59, 59, "Capture 10h59m59")]
    [InlineData(2026, 9, 3, 11, 0, 0, "Capture 11h00m00")]
    public void CaptureNameUsesOneCoherentClockObservation(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        string expected)
    {
        var startedAt = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        Assert.Equal(expected, SourceMetadata.NameCaptureStartedAt("Capture", startedAt));
    }

    [Fact]
    public void SettingsTemporaryCleanupNeverReplacesThePrimaryFailure()
    {
        var cleanup = new IOException("cleanup failed");
        var escaped = Record.Exception(() =>
            SettingsStore.DeleteTemporaryBestEffort(
                "settings.tmp",
                _ => throw cleanup));

        Assert.Null(escaped);
    }

    [Fact]
    public void SettingsTemporaryCleanupCallsTheFilesystemOperationWhenItCan()
    {
        string? removed = null;
        SettingsStore.DeleteTemporaryBestEffort("settings.tmp", path => removed = path);
        Assert.Equal("settings.tmp", removed);
    }
}
