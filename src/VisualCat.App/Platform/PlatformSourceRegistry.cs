using VisualCat.Application.Ports;

namespace VisualCat.App.Platform;

public static class PlatformSourceRegistry
{
    /// <summary>
    /// The device's own text-size setting, as a multiplier, or null where the platform has
    /// none. Published before the first view is built; see <see cref="VisualCat.App.TextScale"/>.
    /// </summary>
    public static double? PlatformFontScale { get; set; }

    public static Func<ILogSource?>? CreateOnDeviceSource { get; set; }
    public static Func<string, CancellationToken, Task>? ShareFileAsync { get; set; }
    public static Func<CancellationToken, Task<IReadOnlyList<string>>>? ConsumeLaunchFilesAsync { get; set; }

    public static event Action<IReadOnlyList<string>>? LaunchFilesReceived;
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

    public static void PublishLaunchFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            LaunchFilesReceived?.Invoke(paths);
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
