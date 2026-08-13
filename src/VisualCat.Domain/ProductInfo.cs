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
}
