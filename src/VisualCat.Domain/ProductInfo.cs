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
}
