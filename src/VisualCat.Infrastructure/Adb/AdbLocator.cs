namespace VisualCat.Infrastructure.Adb;

/// <summary>
/// An explicitly configured ADB path that cannot be used, named with the reason.
/// </summary>
/// <remarks>
/// Distinct from "no ADB anywhere", because the two need opposite answers: the first is a
/// correction to one value the caller supplied, the second is a list of places to put one.
/// </remarks>
public sealed class AdbLocatorException(string message) : Exception(message);

/// <summary>
/// What the locator found, or the sentence explaining why it found nothing.
/// </summary>
/// <param name="ExecutablePath">The resolved <c>adb</c>, or null.</param>
/// <param name="Problem">
/// A reader-facing sentence when there is nothing to run. Product prose, not a framework
/// exception message: the shell shows it verbatim, which is why the locator returns it rather
/// than making a view compose one out of an exception.
/// </param>
public readonly record struct AdbResolution(string? ExecutablePath, string? Problem)
{
    /// <summary>Whether an executable was resolved.</summary>
    public bool Found => ExecutablePath is not null;

    /// <summary>
    /// Whether the failure is about a path the caller pinned, rather than about this machine
    /// having no ADB at all. The two need opposite answers, so callers distinguish them.
    /// </summary>
    public bool PinnedPathRejected { get; init; }
}

public static class AdbLocator
{
    /// <summary>
    /// Resolves the <c>adb</c> executable, preferring an explicitly configured path, and says
    /// why when it cannot.
    /// </summary>
    /// <remarks>
    /// The result-returning form exists so no view has to turn an exception into user-facing
    /// text. <see cref="Find"/> is the throwing form the command line uses.
    /// </remarks>
    public static AdbResolution Resolve(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var pinned = explicitPath.Trim();
            var rejection = RejectPinned(pinned);
            return rejection is null
                ? new AdbResolution(Path.GetFullPath(pinned), null)
                : new AdbResolution(null, rejection) { PinnedPathRejected = true };
        }

        foreach (var candidate in AmbientCandidates())
        {
            if (File.Exists(candidate))
            {
                return new AdbResolution(Path.GetFullPath(candidate), null);
            }
        }

        return new AdbResolution(null, NotFoundSentence());
    }

    /// <summary>The "there is no ADB here" sentence, with the places looked and the remedy.</summary>
    public static string NotFoundSentence() =>
        "ADB was not found. It was looked for in: " +
        string.Join("; ", SearchLocationSummary()) + ". " + InstallHint();

    /// <summary>
    /// Resolves the <c>adb</c> executable, preferring an explicitly configured path.
    /// </summary>
    /// <param name="explicitPath">
    /// A path the caller pinned — <c>--adb</c> on the command line, or the desktop's ADB path
    /// setting. When present it is authoritative: it is either used or refused by name.
    /// </param>
    /// <exception cref="AdbLocatorException">
    /// <paramref name="explicitPath"/> is a directory, does not exist, or is not executable.
    /// </exception>
    /// <remarks>
    /// An explicit path used to be treated as a hint: <c>File.Exists</c> false — a typo, a moved
    /// SDK, or a directory, for which it is false by definition — fell through to the ambient
    /// probes, so the run <em>succeeded</em> with a different <c>adb</c> and exit code 0, and on
    /// a machine with several installations nothing said which one ran. Worse, with no fallback
    /// available the product answered "ADB was not found. Set --adb…" to a user looking at the
    /// <c>--adb</c> they had just set (finding F-10).
    /// </remarks>
    public static string? Find(string? explicitPath = null)
    {
        var resolution = Resolve(explicitPath);
        return resolution.PinnedPathRejected
            ? throw new AdbLocatorException(resolution.Problem!)
            : resolution.ExecutablePath;
    }

    /// <summary>
    /// Where <see cref="Find"/> looks when nothing is pinned, in the order it looks, as prose a
    /// message can print.
    /// </summary>
    /// <remarks>
    /// Built from the same code that does the searching so a message cannot drift from the
    /// behaviour again: the "ADB was not found" text named two of the four routes that work and,
    /// on the platform where it is most likely to appear, no way to obtain <c>adb</c> at all
    /// (finding F-12).
    /// </remarks>
    public static IReadOnlyList<string> SearchLocationSummary()
    {
        var lines = new List<string>
        {
            "--adb <path> on the command line",
            "the ADB path in Appearance & timeline (desktop)",
            "ANDROID_SDK_ROOT or ANDROID_HOME pointing at an SDK directory",
        };

        foreach (var root in AndroidSdkRoots(includeMissing: true))
        {
            lines.Add(Path.Combine(root, "platform-tools", ExecutableName));
        }

        foreach (var direct in DirectExecutables())
        {
            lines.Add(direct);
        }

        lines.Add($"{ExecutableName} on PATH");
        return lines;
    }

    /// <summary>The one-paragraph remedy for "there is no ADB on this machine".</summary>
    public static string InstallHint() =>
        OperatingSystem.IsMacOS()
            ? "Install it with 'brew install --cask android-platform-tools', or from Android Studio, " +
              "which puts it in ~/Library/Android/sdk/platform-tools."
            : OperatingSystem.IsLinux()
                ? "Install your distribution's android-sdk-platform-tools package, or unpack Google's " +
                  "platform-tools archive and put it on PATH."
                : "Install Android Studio's platform-tools, or unpack Google's platform-tools archive " +
                  "and put it on PATH.";

    private static string ExecutableName => OperatingSystem.IsWindows() ? "adb.exe" : "adb";

    /// <summary>Why a pinned path cannot be used, or null when it can.</summary>
    private static string? RejectPinned(string explicitPath)
    {
        if (Directory.Exists(explicitPath))
        {
            return $"The configured ADB path '{explicitPath}' is a directory, not the adb executable. " +
                   $"Point it at the file, usually '{Path.Combine(explicitPath, "platform-tools", ExecutableName)}'.";
        }

        if (!File.Exists(explicitPath))
        {
            return $"The configured ADB path '{explicitPath}' does not exist. Correct it, or clear it to " +
                   "search the Android SDK locations and PATH instead.";
        }

        if (!OperatingSystem.IsWindows() && !IsExecutable(explicitPath))
        {
            return $"The configured ADB path '{explicitPath}' is not executable. Run 'chmod +x {explicitPath}'.";
        }

        return null;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool IsExecutable(string path)
    {
        try
        {
            const UnixFileMode AnyExecute =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & AnyExecute) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A filesystem that cannot report a mode is not evidence that the file is unusable.
            // Let Process.Start be the judge rather than refusing a path that may work.
            return true;
        }
    }

    private static IEnumerable<string> AmbientCandidates()
    {
        foreach (var root in AndroidSdkRoots(includeMissing: false))
        {
            yield return Path.Combine(root, "platform-tools", ExecutableName);
        }

        foreach (var direct in DirectExecutables())
        {
            yield return direct;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, ExecutableName);
        }
    }

    /// <summary>
    /// Package managers that link the executable rather than laying down a
    /// <c>platform-tools</c> tree, so the SDK-root probe above cannot see them.
    /// </summary>
    /// <remarks>
    /// Homebrew is reached today only because it also puts these directories on <c>PATH</c> —
    /// and a GUI process launched from Finder inherits the login session's environment, not a
    /// shell's, so a <c>PATH</c> entry that <c>.zprofile</c> adds is invisible to it. A Mac user
    /// who installs platform-tools with Homebrew and double-clicks VisualCat found no devices,
    /// while the same build run from Terminal found them at once (finding F-11).
    /// </remarks>
    private static IEnumerable<string> DirectExecutables()
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin/adb";   // Apple silicon
            yield return "/usr/local/bin/adb";      // Intel
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "/usr/lib/android-sdk/platform-tools/adb";
            yield return "/usr/local/bin/adb";
        }
    }

    /// <summary>
    /// SDK roots to probe, in order, after the two environment variables.
    /// </summary>
    /// <param name="includeMissing">
    /// True when the caller is describing where the product looks rather than searching, so a
    /// location the user has not created yet is still worth naming.
    /// </param>
    /// <remarks>
    /// The only default probed used to be <c>LocalApplicationData/Android/Sdk</c> — the
    /// <em>Windows</em> Android Studio convention, which on macOS resolves to
    /// <c>~/Library/Application Support/Android/Sdk</c> and exists on no ordinary Mac. Android
    /// Studio on macOS installs to <c>~/Library/Android/sdk</c>, and on Linux to
    /// <c>~/Android/Sdk</c>; neither was ever looked at (finding F-11). Both spellings of the
    /// last segment are probed, because <c>Sdk</c> and <c>sdk</c> differ only in case — which
    /// resolves on a case-insensitive APFS or NTFS volume and does not on a case-sensitive one,
    /// the hardest kind of "works on my machine" to report.
    /// </remarks>
    private static IEnumerable<string> AndroidSdkRoots(bool includeMissing)
    {
        foreach (var name in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && (includeMissing || Directory.Exists(value)))
            {
                yield return value;
            }
        }

        foreach (var root in DefaultSdkRoots())
        {
            if (includeMissing || Directory.Exists(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> DefaultSdkRoots()
    {
        // DoNotVerify on both: on Unix the verifying overload answers with an empty string when
        // the directory does not exist, so a profile whose home has not been created yet — or a
        // service account — silently dropped every default root from both the search and the
        // message that lists where the search looked.
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(home))
        {
            if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(home, "Library", "Android", "sdk");
                yield return Path.Combine(home, "Library", "Android", "Sdk");
            }
            else if (OperatingSystem.IsLinux())
            {
                yield return Path.Combine(home, "Android", "Sdk");
                yield return Path.Combine(home, "Android", "sdk");
                yield return Path.Combine(home, ".local", "share", "Android", "Sdk");
            }
        }

        // Kept last and unconditional: it is where Windows Android Studio installs, and it is
        // the location earlier versions probed, so a profile that relies on it keeps working.
        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(local))
        {
            yield return Path.Combine(local, "Android", "Sdk");
        }
    }
}
