using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(VisualCat.App.Tests.HeadlessTestApplication))]

namespace VisualCat.App.Tests;

public static class HeadlessTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<VisualCat.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
