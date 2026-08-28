using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// What the app remembers about update prompts between launches.
/// </summary>
/// <remarks>
/// These three fields are the whole difference between an offer and a nag, and they live in a
/// file that people do open and read. Two things therefore have to hold: a settings file
/// written by a version that predates them must still load, and a value that cannot have come
/// from this device's clock must not be able to silence the prompt for ever with nothing on
/// screen to explain why.
/// </remarks>
public sealed class AppUpdateSettingsTests
{
    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"visualcat-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task TheThreeUpdateFieldsRoundTrip()
    {
        var path = TempSettingsPath();
        var store = new SettingsStore(path);
        var snoozed = DateTimeOffset.UtcNow.AddDays(3);
        var checkedAt = DateTimeOffset.UtcNow.AddHours(-2);
        try
        {
            await store.SaveAsync(new ApplicationSettings(
                UpdateDismissedVersionCode: 2010003,
                UpdateSnoozedUntilUtc: snoozed,
                UpdateLastCheckedUtc: checkedAt));

            var loaded = await store.LoadAsync();
            Assert.Equal(2010003, loaded.UpdateDismissedVersionCode);
            Assert.Equal(snoozed, loaded.UpdateSnoozedUntilUtc);
            Assert.Equal(checkedAt, loaded.UpdateLastCheckedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A settings file written before this feature existed has none of these keys. It must load
    /// with their defaults rather than being discarded — which would take the reader's theme,
    /// their timeline settings and their open workspace with it.
    /// </summary>
    [Fact]
    public async Task ASettingsFileWithoutTheUpdateFieldsStillLoads()
    {
        var path = TempSettingsPath();
        try
        {
            await File.WriteAllTextAsync(
                path,
                """{"version":1,"theme":"Dark","textScale":1.25,"uiRefreshLimit":45}""");

            var loaded = await new SettingsStore(path).LoadAsync();
            Assert.Equal("Dark", loaded.Theme);
            Assert.Equal(1.25, loaded.TextScale);
            Assert.Equal(45, loaded.UiRefreshLimit);
            Assert.Equal(0, loaded.UpdateDismissedVersionCode);
            Assert.Null(loaded.UpdateSnoozedUntilUtc);
            Assert.Null(loaded.UpdateLastCheckedUtc);
            Assert.Null(loaded.MobileTimelineShare);
            Assert.Null(loaded.MobileTimelineWidthShare);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The longest snooze this product grants is seven days. Anything a fortnight out came from
    /// a clock that was wrong, a restored backup, or an edited file, and left alone it would
    /// suppress the update prompt for as long as it says.
    /// </summary>
    [Fact]
    public async Task AnImplausibleTimestampIsDiscardedRatherThanHonoured()
    {
        var path = TempSettingsPath();
        var store = new SettingsStore(path);
        try
        {
            await store.SaveAsync(new ApplicationSettings(
                UpdateSnoozedUntilUtc: DateTimeOffset.UtcNow.AddYears(30),
                UpdateLastCheckedUtc: DateTimeOffset.UtcNow.AddYears(30)));

            var loaded = await store.LoadAsync();
            Assert.Null(loaded.UpdateSnoozedUntilUtc);
            Assert.Null(loaded.UpdateLastCheckedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ANegativeVersionCodeIsNotAVersionCode()
    {
        var path = TempSettingsPath();
        var store = new SettingsStore(path);
        try
        {
            await store.SaveAsync(new ApplicationSettings(UpdateDismissedVersionCode: -12));
            Assert.Equal(0, (await store.LoadAsync()).UpdateDismissedVersionCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0.05, 0.05)]
    [InlineData(0.62, 0.62)]
    [InlineData(0.95, 0.95)]
    [InlineData(-1, null)]
    [InlineData(0.01, null)]
    [InlineData(0.99, null)]
    public async Task MobileTimelineShareRoundTripsOnlyInsideItsStorageRange(
        double value,
        double? expected)
    {
        var path = TempSettingsPath();
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(new ApplicationSettings(
                MobileTimelineShare: value,
                MobileTimelineWidthShare: value));

            var loaded = await store.LoadAsync();
            Assert.Equal(expected, loaded.MobileTimelineShare);
            Assert.Equal(expected, loaded.MobileTimelineWidthShare);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
