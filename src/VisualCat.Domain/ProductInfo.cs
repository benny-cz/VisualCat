using System.Globalization;
using System.Reflection;

namespace VisualCat.Domain;

/// <summary>Which audience a build was made for.</summary>
/// <remarks>
/// Derived from the version string rather than from a separate build property, so it cannot
/// drift from the tag that produced the artifact: <c>Directory.Build.props</c> stamps
/// <c>-dev</c> on anything the release pipeline did not build with
/// <c>-p:ReleaseChannel=stable</c>, and the release workflow passes the tag's own version
/// through <c>-p:Version=</c>. Anything unrecognised is <see cref="Development"/>, which is
/// the conservative answer: a build nobody can identify does not nag.
/// </remarks>
public enum ReleaseChannel
{
    /// <summary>Not a release: a developer or CI build. Never prompts for updates.</summary>
    Development,

    /// <summary>Closed testing. Testers are here to run the newest build, so nudging is firm.</summary>
    Alpha,

    /// <summary>Open testing.</summary>
    Beta,

    /// <summary>Production.</summary>
    Stable,
}

/// <summary>Exposes build identity shared by the desktop app and command line.</summary>
public static class ProductInfo
{
    /// <summary>Gets the assembly informational version, including source metadata when available.</summary>
    public static string InformationalVersion { get; } =
        typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "2.0.0";

    /// <summary>
    /// Gets the release version without SemVer build metadata, for places that identify the
    /// product to a person rather than to a bug report.
    /// </summary>
    /// <remarks>
    /// SourceLink appends "+&lt;commit&gt;" to the informational version. Those 40 characters are
    /// what makes a crash report actionable, and they are meaningless in a phone-width footer
    /// that they overflow. Diagnostics keep <see cref="InformationalVersion"/>.
    /// </remarks>
    public static string DisplayVersion { get; } =
        InformationalVersion.Split('+', 2)[0];

    /// <summary>
    /// Gets the release version and the short source revision, for the one line that has to
    /// name the build a screenshot was taken from.
    /// </summary>
    /// <remarks>
    /// A bug report with a screenshot is the main channel by which defects arrive, and
    /// "VisualCat 2.0.5" could not answer "which build?" — the version was identical on the
    /// shipped release and on every Release build made after it (finding F-01). Seven
    /// characters of commit is what `git show` wants and what a phone-width footer can hold;
    /// the full 40 stay in <see cref="InformationalVersion"/>, where diagnostics read them.
    /// </remarks>
    public static string BuildVersion { get; } = Compose(InformationalVersion);

    /// <summary>Which audience this build was made for.</summary>
    public static ReleaseChannel Channel { get; } = ChannelOf(DisplayVersion);

    /// <summary>
    /// Reads the release channel out of a SemVer version string.
    /// </summary>
    /// <remarks>
    /// Only the prerelease label matters, and only its first dot-separated identifier: Play
    /// releases are tagged <c>v2.1.0-alpha.2</c> and <c>v2.1.0-beta.1</c>, while an untagged
    /// workflow run produces <c>2.1.0-preview.7</c>. Exposed rather than private because it is
    /// the seam the channel tests drive; the running build's own answer is
    /// <see cref="Channel"/>.
    /// </remarks>
    /// <param name="version">A version string, with or without SemVer build metadata.</param>
    public static ReleaseChannel ChannelOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ReleaseChannel.Development;
        }

        var release = version.Split('+', 2)[0];
        var separator = release.IndexOf('-', StringComparison.Ordinal);
        if (separator < 0)
        {
            // No prerelease label at all. It is only a stable release if the part in front of
            // it actually parses as one: "" and "nightly" are not versions.
            return Version.TryParse(release, out _) ? ReleaseChannel.Stable : ReleaseChannel.Development;
        }

        var label = release[(separator + 1)..].Split('.', 2)[0];
        return label.ToLowerInvariant() switch
        {
            "alpha" => ReleaseChannel.Alpha,
            "beta" => ReleaseChannel.Beta,
            _ => ReleaseChannel.Development,
        };
    }

    /// <summary>
    /// The Android version code a release version maps to, or null where it has none.
    /// </summary>
    /// <remarks>
    /// The mirror of the formula in <c>src/VisualCat.Android/VisualCat.Android.csproj</c>:
    /// <c>major * 1000000 + minor * 10000 + patch * 100 + build</c>. It lives here so the app
    /// can compare the build it is running against the code Google Play is offering — Play
    /// reports a version code and never a version name — and so the arithmetic can be tested
    /// off-device. The build counter is not recoverable from a version string, which carries
    /// only the SemVer prerelease label, so this answers for build 0.
    /// </remarks>
    /// <param name="version">A release version, with or without a prerelease label.</param>
    public static long? VersionCodeOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var release = version.Split('+', 2)[0].Split('-', 2)[0];
        if (!Version.TryParse(release, out var parsed))
        {
            return null;
        }

        var patch = Math.Max(parsed.Build, 0);
        if (parsed.Major is < 0 or > 2000 || parsed.Minor is < 0 or > 99 || patch > 99)
        {
            return null;
        }

        return ((long)parsed.Major * 1000000) + ((long)parsed.Minor * 10000) + (patch * 100L);
    }

    /// <summary>
    /// The version name an Android version code was built from, or null when it does not
    /// decode plausibly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Google Play's in-app update API reports the version code of the build it would install
    /// and nothing else — no version name, no release notes, no track. This is the only way an
    /// update offer can be named to the reader at all, and it works only because this
    /// repository's version-code scheme is deterministic and invertible.
    /// </para>
    /// <para>
    /// Codes below 1000000 came from the scheme used up to and including 2.0.9
    /// (<c>major * 10000 + minor * 100 + patch</c>), which every installed build in the wild
    /// still carries; they have to keep decoding or the first offer this feature ever makes
    /// would be unnamed. A code that decodes to nothing sensible returns null and the caller
    /// says "a newer VisualCat" rather than inventing a version.
    /// </para>
    /// </remarks>
    /// <param name="versionCode">A version code as Google Play reports it.</param>
    public static string? VersionNameOf(long versionCode)
    {
        if (versionCode <= 0)
        {
            return null;
        }

        if (versionCode < 1000000)
        {
            // The legacy scheme. Its fields are the same width, one decade narrower.
            var legacyMajor = versionCode / 10000;
            var legacyMinor = versionCode / 100 % 100;
            var legacyPatch = versionCode % 100;
            return legacyMajor is >= 1 and <= 99
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{legacyMajor}.{legacyMinor}.{legacyPatch}")
                : null;
        }

        var major = versionCode / 1000000;
        var minor = versionCode / 10000 % 100;
        var patch = versionCode / 100 % 100;
        return major is >= 1 and <= 2000
            ? string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}")
            : null;
    }

    private static string Compose(string informational)
    {
        var parts = informational.Split('+', 2);
        if (parts.Length < 2 || parts[1].Length == 0)
        {
            return parts[0];
        }

        var revision = parts[1];
        return $"{parts[0]}+{revision[..Math.Min(7, revision.Length)]}";
    }
}
