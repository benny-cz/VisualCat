using VisualCat.Application.Ports;

namespace VisualCat.App.Platform;

/// <summary>
/// A file another app handed to VisualCat, and the name that app calls it.
/// </summary>
/// <remarks>
/// The path is a private cache file whose name has to be unique, so it carries a UTC stamp
/// and a GUID; the display name is what the provider says the document is called. They were
/// the same string, so opening <c>tiny.txt</c> through Open with produced a tab named
/// <c>20260821-200744-8fde316a7e2447a281e7e825dfc2d0f5-raw:_storage_emulated_0_Download_tiny.txt</c>
/// and read the whole of it — timestamp, GUID, provider document id and absolute shared-storage
/// path — to a screen reader (finding F-27). Two facts, two fields.
/// </remarks>
/// <param name="Path">Where the bytes are, which only the importer needs.</param>
/// <param name="DisplayName">What to call it on screen.</param>
public sealed record IncomingFile(string Path, string DisplayName);

public static class PlatformSourceRegistry
{
    /// <summary>
    /// The device's own text-size setting, as a multiplier, or null where the platform has
    /// none. Published before the first view is built; see <see cref="VisualCat.App.TextScale"/>.
    /// </summary>
    public static double? PlatformFontScale { get; set; }

    public static Func<ILogSource?>? CreateOnDeviceSource { get; set; }

    /// <summary>
    /// Whether the platform currently grants this app the whole device's log, or null where
    /// the platform has no such distinction.
    /// </summary>
    /// <remarks>
    /// The pre-capture explanation was unconditional: it promised that "Android will now ask
    /// you to allow access to device logs" on every capture, and on a clean install — where
    /// READ_LOGS is not held — no sheet can appear at all, because READ_LOGS is not a runtime
    /// permission and an app cannot request it. The product then contradicted itself, since
    /// Session info correctly explained the same state and gave the exact grant command
    /// (finding F-13). The shell has to be able to ask before it writes the sentence.
    /// </remarks>
    public static Func<bool>? HasFullDeviceLogPermission { get; set; }

    /// <summary>
    /// The exact command a person runs to grant full-device log access on this platform, or
    /// null where nothing of the sort applies.
    /// </summary>
    public static string? FullDeviceLogGrantCommand { get; set; }
    public static Func<string, CancellationToken, Task>? ShareFileAsync { get; set; }
    public static Func<CancellationToken, Task<IReadOnlyList<IncomingFile>>>? ConsumeLaunchFilesAsync { get; set; }

    public static event Action<IReadOnlyList<IncomingFile>>? LaunchFilesReceived;
    public static event Action? AppResumed;

    /// <summary>
    /// The device changed something about how the app is displayed — text size, display
    /// density, locale, layout direction — without the app being torn down for it.
    /// </summary>
    /// <remarks>
    /// Android's default is to destroy and recreate the activity for each of these, which on
    /// this product costs the reader a running capture and about ten seconds of blank
    /// workspace while every session is reopened from disk (audit 3, A2 and C3). The activity
    /// declares that it handles them instead, and this is how it says so to the part of the
    /// app that has to answer: the capture keeps running and the views are rebuilt at the new
    /// scale, which is all the recreation was achieving.
    /// </remarks>
    public static event Action? DisplayConfigurationChanged;

    /// <summary>
    /// Raised when the app leaves the foreground — backgrounded, or the screen turned
    /// off. A live capture keeps running; the workspace uses this to stop doing work
    /// whose only product is a picture nobody can see.
    /// </summary>
    public static event Action? AppPaused;

    /// <summary>
    /// Asks the platform to let the app have the touches inside these rectangles, even where
    /// they overlap one of the platform's own edge gestures. Coordinates are device pixels
    /// relative to the top-level's window; an empty list releases every previous claim.
    /// </summary>
    /// <remarks>
    /// Installed only by a host that has such gestures — Android's back swipe, which owns a
    /// strip about 30 dp wide down both edges of the screen and would otherwise take a
    /// drag-to-pan on the heat map and send the reader to the home screen (finding F-28).
    /// Where this is null, <see cref="EdgeGestureGuard"/> does nothing at all.
    /// </remarks>
    public static Action<IReadOnlyList<Avalonia.PixelRect>>? SetGestureExclusions { get; set; }

    public static void PublishLaunchFiles(IReadOnlyList<IncomingFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > 0)
        {
            LaunchFilesReceived?.Invoke(files);
        }
    }

    public static void PublishAppResumed() => AppResumed?.Invoke();

    public static void PublishDisplayConfigurationChanged() => DisplayConfigurationChanged?.Invoke();

    public static void PublishAppPaused() => AppPaused?.Invoke();

    /// <summary>Reports a desktop window becoming visible or being minimized.</summary>
    public static void PublishWindowVisibility(bool visible)
    {
        if (visible)
        {
            AppResumed?.Invoke();
        }
        else
        {
            AppPaused?.Invoke();
        }
    }
}
