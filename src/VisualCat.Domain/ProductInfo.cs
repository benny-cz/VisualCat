using System.Reflection;

namespace VisualCat.Domain;

public static class ProductInfo
{
    public static string InformationalVersion { get; } =
        typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "2.0.0";
}
