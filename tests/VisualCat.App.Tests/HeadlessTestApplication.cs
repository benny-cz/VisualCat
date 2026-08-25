using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(VisualCat.App.Tests.HeadlessTestApplication))]
// The UI composition root intentionally exposes process-wide platform hooks and settings.
// Tests model different devices by replacing them temporarily, so parallel classes can make
// one another write into a deleted session root or evaluate the wrong accessibility scale.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VisualCat.App.Tests;

public static class HeadlessTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<VisualCat.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
