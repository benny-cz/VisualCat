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
