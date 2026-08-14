using VisualCat.Application.Ports;

namespace VisualCat.App.Platform;

public static class PlatformSourceRegistry
{
    public static Func<ILogSource?>? CreateOnDeviceSource { get; set; }
    public static Func<string, CancellationToken, Task>? ShareFileAsync { get; set; }
    public static Func<CancellationToken, Task<IReadOnlyList<string>>>? ConsumeLaunchFilesAsync { get; set; }

    public static event Action<IReadOnlyList<string>>? LaunchFilesReceived;
    public static event Action? AppResumed;

    public static void PublishLaunchFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            LaunchFilesReceived?.Invoke(paths);
        }
    }

    public static void PublishAppResumed() => AppResumed?.Invoke();
}
