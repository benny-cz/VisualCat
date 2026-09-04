using VisualCat.Application.Ports;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.Application.Tests;

public sealed class ImprovementAuditTests
{
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
