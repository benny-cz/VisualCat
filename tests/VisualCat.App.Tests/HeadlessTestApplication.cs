using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(VisualCat.App.Tests.HeadlessTestApplication))]
// Avalonia 12 defaults to tearing the headless compositor down and rebuilding it for every
// test. In a long xUnit v3 run that reset can race the next dispatcher work item and construct
// the compositor on a worker that does not own it. Per-assembly isolation is Avalonia's
// documented remedy: one renderer, one dispatcher thread, and no teardown/re-init boundary
// between tests. The test-owned windows and process-wide VisualCat seams are still reset by
// their fixtures below and by the serialized collection policy.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
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
