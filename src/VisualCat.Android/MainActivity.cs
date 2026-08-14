using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using AndroidX.Core.Content;
using Avalonia.Android;
using VisualCat.App.Platform;

namespace VisualCat.Android;

[Activity(
    Label = "VisualCat",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["file", "content"],
    DataMimeType = "text/plain")]
public sealed class MainActivity : AvaloniaMainActivity
{
    private static WeakReference<MainActivity>? s_current;
    private readonly HashSet<string> _consumedUris = new(StringComparer.Ordinal);

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        s_current = new WeakReference<MainActivity>(this);
        PlatformSourceRegistry.CreateOnDeviceSource = static () => new OnDeviceLogSource();
        PlatformSourceRegistry.ShareFileAsync = ShareCurrentAsync;
        PlatformSourceRegistry.ConsumeLaunchFilesAsync = ConsumeCurrentLaunchFilesAsync;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();
        PlatformSourceRegistry.PublishAppResumed();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        _ = PublishIncomingAsync(intent);
    }

    protected override void OnDestroy()
    {
        if (s_current?.TryGetTarget(out var current) == true && ReferenceEquals(current, this))
        {
            s_current = null;
            PlatformSourceRegistry.ShareFileAsync = null;
            PlatformSourceRegistry.CreateOnDeviceSource = null;
            PlatformSourceRegistry.ConsumeLaunchFilesAsync = null;
        }

        base.OnDestroy();
    }

    private static Task ShareCurrentAsync(string path, CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            throw new InvalidOperationException("The Android activity is not available for sharing.");
        }

        return activity.ShareAsync(path, cancellationToken);
    }

    private static Task<IReadOnlyList<string>> ConsumeCurrentLaunchFilesAsync(CancellationToken cancellationToken)
    {
        if (s_current?.TryGetTarget(out var activity) != true || activity is null)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        return activity.MaterializeIncomingAsync(activity.Intent, cancellationToken);
    }

    private async Task PublishIncomingAsync(Intent? intent)
    {
        try
        {
            var paths = await MaterializeIncomingAsync(intent, CancellationToken.None);
            PlatformSourceRegistry.PublishLaunchFiles(paths);
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("VisualCat", $"Incoming file could not be opened: {exception.Message}");
        }
    }

    private async Task<IReadOnlyList<string>> MaterializeIncomingAsync(
        Intent? intent,
        CancellationToken cancellationToken)
    {
        var uri = intent?.Data;
        if (intent?.Action != global::Android.Content.Intent.ActionView || uri is null)
        {
            return [];
        }

        var identity = uri.ToString() ?? string.Empty;
        lock (_consumedUris)
        {
            if (!_consumedUris.Add(identity))
            {
                return [];
            }
        }

        if (string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase) &&
            uri.Path is { } localPath &&
            File.Exists(localPath))
        {
            return [localPath];
        }

        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var cache = CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android cache storage is unavailable.");
        var incomingDirectory = Path.Combine(cache, "incoming");
        Directory.CreateDirectory(incomingDirectory);
        var proposedName = uri.LastPathSegment ?? "shared-log.txt";
        var safeName = string.Concat(
            proposedName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(Path.GetExtension(safeName)))
        {
            safeName += ".txt";
        }

        var destination = Path.Combine(
            incomingDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{safeName}");
        await using var input = ContentResolver?.OpenInputStream(uri)
            ?? throw new IOException("Android did not provide a readable stream for the shared log.");
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            256 * 1024,
            FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return [destination];
    }

    private async Task ShareAsync(string path, CancellationToken cancellationToken)
    {
        var cache = CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android cache storage is unavailable.");
        var shareDirectory = Path.Combine(cache, "share");
        Directory.CreateDirectory(shareDirectory);
        foreach (var stale in Directory.EnumerateFiles(shareDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
            {
                File.Delete(stale);
            }
        }

        var sharedPath = Path.Combine(shareDirectory, Path.GetFileName(path));
        await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
        await using (var target = new FileStream(sharedPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
        {
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }

        var authority = $"{PackageName}.files";
        var uri = FileProvider.GetUriForFile(this, authority, new Java.IO.File(sharedPath));
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("application/zip");
        intent.PutExtra(Intent.ExtraStream, uri);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        StartActivity(Intent.CreateChooser(intent, "Share VisualCat session"));
    }
}

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<VisualCat.App.App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
