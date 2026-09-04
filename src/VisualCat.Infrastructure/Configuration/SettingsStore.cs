using System.Text.Json;

namespace VisualCat.Infrastructure.Configuration;

public sealed record ApplicationSettings(
    int Version = 1,
    string Theme = "System",
    bool HighContrast = false,
    double TextScale = 1,
    string? AdbPath = null,
    string[]? DefaultCaptureBuffers = null,
    int DefaultCapturePreRollSeconds = 0,
    string? SessionDirectory = null,
    int UiRefreshLimit = 30,
    string IntensityScale = "Logarithmic",
    string TimelineNormalization = "PerRow",
    double TimelineMinimumUsPerPixel = 1,
    bool TimelinePixelSnap = true,
    double TimelineMinimumBarWidth = 5,
    string ExportOrder = "SourceSequence",
    string ExportEncoding = "utf-8-bom",
    bool DiagnosticsEnabled = true,
    bool TemporaryCleanupEnabled = false,
    int TemporaryRetentionDays = 30,
    long? TemporaryRetentionMaximumBytes = null,
    double? WindowWidth = null,
    double? WindowHeight = null,
    bool WindowMaximized = false,

    // Whether the reader has already been told what an on-device capture does before any
    // platform-specific access step. Android 13+ can show a per-use consent sheet for direct
    // READ_LOGS capture, while Play builds use VisualCat's explicit Wireless-debugging setup;
    // neither surface explains where VisualCat stores the data. This records that the app has
    // explained its local-only data handling once.
    bool LiveCaptureNoticeAcknowledged = false,

    // The sessions that were open when the workspace was last on screen, and which of them
    // was in front. On Android one Back press finishes the activity, so a workspace of three
    // sessions — with their filters, viewports and selections — was gone with a gesture that
    // is easy to hit by accident, and reassembling it took several taps per tab. The captures
    // themselves are durable on disk and each session's view is already persisted; only the
    // list of what was open was not.
    string[]? OpenSessionPaths = null,
    int OpenSessionIndex = 0,

    // Which of Plot / Split / Details the phone workspace was showing. Rotation keeps it,
    // because the activity handles that configuration change in place — but a text-size or
    // display-size change is not in the manifest's list and never should be, since every
    // font size has to be re-measured. Android therefore recreates the activity, and the
    // reader's choice used to go with the view that owned it: a workspace left in Plot came
    // back in Split (audit 2, C5). It is the reader's choice, so it belongs beside the other
    // things about the workspace that survive being put down and picked up again.
    string? WorkspaceDisplayMode = null,

    // The version code the reader has already said no to. Google Play offers the same update
    // on every launch, and an app that asks again each time is a nag rather than a service. A
    // higher version code — or a release the publisher marked urgent — is a new question and
    // may ask again.
    long UpdateDismissedVersionCode = 0,

    // When the reader may next be asked. "Later" has to mean something, and on Android it has
    // to survive the process being killed, which is ordinary rather than exceptional there.
    DateTimeOffset? UpdateSnoozedUntilUtc = null,

    // When the store was last asked. A stable build asks about once a day; asking on every
    // resume would be an IPC round trip for an answer that cannot have changed.
    DateTimeOffset? UpdateLastCheckedUtc = null,

    // The aggregate plot-pane share in stacked phone Split mode. It includes the minimap and
    // excludes the divider lane. Null means automatic size-class allocation; keeping a ratio
    // rather than dp lets the choice survive rotation, density and split-screen changes.
    double? MobileTimelineShare = null,

    // The same for the side-by-side landscape composition, where the reader is moving a
    // column edge. The two axes are stored apart because their limits are unrelated: a
    // height share is about lane bands and entry rows, a width share about the plot's label
    // gutter and the message column beside it.
    double? MobileTimelineWidthShare = null);

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(path);

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new ApplicationSettings();
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            return Validate(settings);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ApplicationSettings();
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        var temporary = _path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, _path, true);
        }
        catch
        {
            DeleteTemporaryBestEffort(temporary);
            throw;
        }
    }

    /// <summary>
    /// Tries to remove an unpublished settings file without replacing the save failure that
    /// led here. The unique file is harmless if the filesystem refuses cleanup; losing the
    /// primary diagnostic is not.
    /// </summary>
    internal static void DeleteTemporaryBestEffort(string path, Action<string>? delete = null)
    {
        try
        {
            (delete ?? File.Delete)(path);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The furthest ahead an update timestamp may plausibly be before it is discarded.
    /// </summary>
    /// <remarks>
    /// A snooze is at most seven days, so anything beyond a fortnight came from a clock that
    /// was wrong when it was written, a restored backup, or an edited file. Left alone such a
    /// value silences the update prompt for as long as it says — potentially for ever — and
    /// there is nothing on screen that would explain why.
    /// </remarks>
    private static readonly TimeSpan MaximumUpdateSnooze = TimeSpan.FromDays(14);

    private static ApplicationSettings Validate(ApplicationSettings? settings)
    {
        if (settings is null || settings.Version != 1)
        {
            return new ApplicationSettings();
        }

        var theme = settings.Theme is "System" or "Light" or "Dark" ? settings.Theme : "System";
        var intensityScale = settings.IntensityScale is "Linear" or "SquareRoot" or "Logarithmic"
            ? settings.IntensityScale
            : "Logarithmic";
        var timelineNormalization = settings.TimelineNormalization is "GlobalViewport" or "PerRow"
            ? settings.TimelineNormalization
            : "PerRow";
        var exportOrder = settings.ExportOrder is "Chronological" or "SourceSequence"
            ? settings.ExportOrder
            : "SourceSequence";
        var exportEncoding = settings.ExportEncoding is "utf-8" or "utf-8-bom"
            ? settings.ExportEncoding
            : "utf-8-bom";
        var allowedBuffers = new HashSet<string>(["main", "system", "crash", "events", "radio"], StringComparer.Ordinal);
        var buffers = (settings.DefaultCaptureBuffers ?? ["main", "system", "crash"])
            .Where(allowedBuffers.Contains)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        if (buffers.Length == 0)
        {
            buffers = ["main", "system", "crash"];
        }

        var openSessions = (settings.OpenSessionPaths ?? [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return settings with
        {
            Theme = theme,
            DefaultCaptureBuffers = buffers,
            TextScale = Math.Clamp(settings.TextScale, 0.75, 2),
            UiRefreshLimit = Math.Clamp(settings.UiRefreshLimit, 1, 60),
            IntensityScale = intensityScale,
            TimelineNormalization = timelineNormalization,
            TimelineMinimumUsPerPixel = Math.Clamp(settings.TimelineMinimumUsPerPixel, 0.1, 500),
            TimelineMinimumBarWidth = double.IsFinite(settings.TimelineMinimumBarWidth)
                ? Math.Clamp(settings.TimelineMinimumBarWidth, 1, 12)
                : 5,
            ExportOrder = exportOrder,
            ExportEncoding = exportEncoding,
            DefaultCapturePreRollSeconds = Math.Clamp(settings.DefaultCapturePreRollSeconds, 0, 3600),
            TemporaryRetentionDays = Math.Clamp(settings.TemporaryRetentionDays, 1, 3650),
            TemporaryRetentionMaximumBytes = settings.TemporaryRetentionMaximumBytes is > 0
                ? Math.Max(settings.TemporaryRetentionMaximumBytes.Value, 64 * 1024 * 1024)
                : null,
            WindowWidth = settings.WindowWidth is { } width ? Math.Clamp(width, 900, 16_384) : null,
            WindowHeight = settings.WindowHeight is { } height ? Math.Clamp(height, 600, 16_384) : null,

            // A restore that reopens a hundred sessions is not a restore. The cap is the
            // number a person can plausibly have been working with, and the index has to
            // address the list that survived it.
            OpenSessionPaths = openSessions,
            OpenSessionIndex = Math.Clamp(settings.OpenSessionIndex, 0, Math.Max(0, openSessions.Length - 1)),

            // Negative codes are not version codes, and a timestamp far in the future is a
            // wrong clock rather than a preference. Both would silently suppress an update
            // prompt with nothing on screen to say why.
            UpdateDismissedVersionCode = Math.Max(settings.UpdateDismissedVersionCode, 0),
            UpdateSnoozedUntilUtc = Plausible(settings.UpdateSnoozedUntilUtc),
            UpdateLastCheckedUtc = Plausible(settings.UpdateLastCheckedUtc),
            MobileTimelineShare = StoredShare(settings.MobileTimelineShare),
            MobileTimelineWidthShare = StoredShare(settings.MobileTimelineWidthShare),
        };
    }

    /// <summary>Drops a stored update timestamp that cannot have come from this device's clock.</summary>
    private static DateTimeOffset? Plausible(DateTimeOffset? stamp) =>
        stamp is { } value && value <= DateTimeOffset.UtcNow + MaximumUpdateSnooze ? stamp : null;

    /// <summary>
    /// Keeps a pane share only inside the band it can mean anything in. Anything else becomes
    /// automatic rather than a surprising extreme; the runtime clamps again against the
    /// viewport it actually has.
    /// </summary>
    private static double? StoredShare(double? share) =>
        share is { } value && double.IsFinite(value) && value is >= 0.05 and <= 0.95
            ? value
            : null;
}
