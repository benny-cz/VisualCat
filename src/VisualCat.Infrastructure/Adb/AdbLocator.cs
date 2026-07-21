namespace VisualCat.Infrastructure.Adb;

public static class AdbLocator
{
    public static string? Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        foreach (var root in AndroidSdkRoots())
        {
            var candidate = Path.Combine(root, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> AndroidSdkRoots()
    {
        foreach (var name in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                yield return value;
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            var defaultPath = Path.Combine(local, "Android", "Sdk");
            if (Directory.Exists(defaultPath))
            {
                yield return defaultPath;
            }
        }
    }
}
