using System.Reflection;

namespace VisualCat.Domain;

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
