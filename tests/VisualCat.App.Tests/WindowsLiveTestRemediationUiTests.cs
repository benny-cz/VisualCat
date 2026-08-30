using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Application.Ports;
using VisualCat.Domain.Sessions;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>The desktop/application half of the Windows live-test remediation.</summary>
public sealed class WindowsLiveTestRemediationUiTests
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    [AvaloniaFact]
    public async Task LiveAdbFieldsAndSpinnerButtonsTellAutomationWhatTheyDo()
    {
        var client = new DeviceClient(Device("A"));
        using var dialog = new AdbCaptureDialog(client);
        dialog.Show();
        try
        {
            await WaitUntilAsync(() => dialog.GetVisualDescendants().OfType<NumericUpDown>()
                .SelectMany(static field => field.GetVisualDescendants().OfType<Button>()).Count() >= 6);
            dialog.UpdateLayout();

            var device = Assert.Single(dialog.GetVisualDescendants().OfType<ComboBox>());
            Assert.Equal("Android device", AutomationProperties.GetName(device));

            var fields = dialog.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Assert.Collection(
                fields,
                field => Assert.Equal("Pre-roll seconds", AutomationProperties.GetName(field)),
                field => Assert.Equal("Stop after minutes", AutomationProperties.GetName(field)),
                field => Assert.Equal("Stop after megabytes", AutomationProperties.GetName(field)));
            var buttons = fields.SelectMany(static field => field.GetVisualDescendants().OfType<Button>()).ToArray();
            Assert.True(buttons.Length >= 6);
            Assert.All(buttons, button =>
            {
                var name = AutomationProperties.GetName(button);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.False(name!.StartsWith("Avalonia.", StringComparison.Ordinal));
            });

            Assert.Contains(
                dialog.GetVisualDescendants().OfType<TextBlock>(),
                static block => block.Text == "Pre-roll seconds (0 = from now)");
            Assert.Contains(
                dialog.GetVisualDescendants().OfType<CheckBox>(),
                static box => Equals(box.Content, "Include everything already in the buffer"));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task RefreshKeepsAVanishedSerialSelectedUntilTheReaderChoosesAnother()
    {
        var client = new DeviceClient(Device("A"), Device("B"));
        using var dialog = new AdbCaptureDialog(client);
        dialog.Show();
        try
        {
            await WaitUntilAsync(() => dialog.GetVisualDescendants().OfType<ComboBox>()
                .SingleOrDefault()?.Items.Count == 2);
            var devices = Assert.Single(dialog.GetVisualDescendants().OfType<ComboBox>());
            devices.SelectedIndex = 1;
            Assert.StartsWith("B ·", devices.SelectedItem?.ToString(), StringComparison.Ordinal);

            client.Devices = [Device("A")];
            await dialog.RefreshDevicesForTestsAsync();

            Assert.StartsWith("B ·", devices.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.Contains("Not connected", devices.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.Contains(
                dialog.GetVisualDescendants().OfType<TextBlock>(),
                static block => block.Text?.Contains("Device B disconnected", StringComparison.Ordinal) == true);
            Assert.False(dialog.GetVisualDescendants().OfType<Button>()
                .Single(static button => Equals(button.Content, "Start capture")).IsEnabled);

            client.Devices = [Device("A"), Device("B")];
            await dialog.RefreshDevicesForTestsAsync();
            Assert.StartsWith("B ·", devices.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Not connected", devices.SelectedItem?.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingDiscoveryCancelsTheCommandAndSingularCountIsHuman()
    {
        var healthy = new DeviceClient(Device("A"));
        using (var dialog = new AdbCaptureDialog(healthy))
        {
            dialog.Show();
            await WaitUntilAsync(() => dialog.GetVisualDescendants().OfType<TextBlock>()
                .Any(static block => block.Text?.StartsWith("1 device detected.", StringComparison.Ordinal) == true));
            dialog.Close();
        }

        var blocked = new BlockingDiscoveryClient();
        using var blockedDialog = new AdbCaptureDialog(blocked);
        blockedDialog.Show();
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        blockedDialog.Close();
        await blocked.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public void AnInvalidConfiguredAdbPathIsMarkedWhereItWasEntered()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "adb.exe");
        var dialog = new AppearanceDialog(new ApplicationSettings(AdbPath: missing));
        var window = new Window { Content = dialog, Width = 600, Height = 900 };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var field = dialog.GetVisualDescendants().OfType<TextBox>()
                .Single(static box => AutomationProperties.GetName(box) == "ADB executable");
            var warning = dialog.GetVisualDescendants().OfType<TextBlock>()
                .Single(static block => AutomationProperties.GetName(block) == "ADB executable validation");

            Assert.True(warning.IsVisible);
            Assert.Contains("not found", warning.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("correct", AutomationProperties.GetHelpText(field), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AHealthySilentStreamAndLaterTransportTroubleAreDifferentStates()
    {
        var tab = new SessionTabViewModel("ADB A", Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", "unused"))
        {
            IsLiveCaptureActive = true,
        };

        tab.ReportActivity(SessionActivity.Connecting, "Connecting · ADB device A");
        tab.ReportCaptureStreamEstablished("ADB device A", "the crash buffer is empty");
        Assert.Equal(SessionActivity.Capturing, tab.Activity);
        Assert.Contains("Connected", tab.Status, StringComparison.Ordinal);
        Assert.Contains("no records yet", tab.Status, StringComparison.Ordinal);
        Assert.Contains("crash buffer is empty", tab.Status, StringComparison.Ordinal);
        Assert.Contains("0/s", tab.Status, StringComparison.Ordinal);

        tab.ReportCaptureConnectionStatus(
            "Device A has not responded for 20s",
            "Device A is no longer connected. Everything captured so far is saved.");
        Assert.Contains("has not responded", tab.Status, StringComparison.Ordinal);
        Assert.Contains("Everything captured so far is saved", tab.CaptureHealthWarning, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASourceOwnedSizeLimitReachesTheDesktopStatusAndNotice()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        try
        {
            await using var workspace = new WorkspaceViewModel();
            string? notice = null;
            workspace.CaptureEndedUnprompted += (_, message) => notice = message;
            await using var source = new CompletingSource();

            var tab = await workspace.CaptureAsync(source, null, TestContext.Current.CancellationToken);

            Assert.Contains("reached its 1 MiB limit", tab.Status, StringComparison.Ordinal);
            Assert.NotNull(notice);
            Assert.Contains("size limit", notice, StringComparison.Ordinal);
            Assert.DoesNotContain("log source ended", notice, StringComparison.OrdinalIgnoreCase);
            await workspace.CloseAsync(tab);
        }
        finally
        {
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// F-13's decisive repetition pass: a full shell creates, renders, closes and releases
    /// fifty capture workspaces. Private bytes alone are recorded rather than asserted — the
    /// CLR may reserve collected heap — while the objects that would prove a product leak
    /// are required to lose every root.
    /// </summary>
    [AvaloniaFact]
    public async Task FiftyCaptureCyclesReleaseEveryClosedTabAndWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
        var main = new MainView();
        var window = new Window { Content = main, Width = 1280, Height = 800 };
        var tabs = new List<WeakReference>(50);
        var views = new List<WeakReference>(50);
        var samples = new List<X21Sample>(10);
        var latencies = new List<double>(50);
        window.Show();
        try
        {
            for (var cycle = 1; cycle <= 50; cycle++)
            {
                var started = Stopwatch.GetTimestamp();
                var released = await RunCaptureCycleAsync(main, window);
                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                tabs.Add(released.Tab);
                views.Add(released.View);
                released = default;

                if (cycle % 5 == 0)
                {
                    await main.WaitForRecentSessionsRefreshAsync();
                    CollectAndPump(window);
                    samples.Add(Sample(cycle));
                }
            }

            // A JIT may keep the final async method result in a hidden state-machine local.
            // Run one unmeasured sentinel cycle so that conservative last-result root cannot
            // make the fiftieth product object look leaked. The fifty measured weak references
            // are still created, rendered and closed through the complete shell path.
            _ = await RunCaptureCycleAsync(main, window);
            await main.WaitForRecentSessionsRefreshAsync();
            CollectAndPump(window);
            Assert.Empty(main.Workspace.Tabs);
            Assert.Null(main.Workspace.Selected);

            var resultPath = Environment.GetEnvironmentVariable("VISUALCAT_X21_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(resultPath));
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(
                    resultPath,
                    JsonSerializer.Serialize(
                        new
                        {
                            samples,
                            latencyMilliseconds = new
                            {
                                median = Percentile(latencies, 0.5),
                                p95 = Percentile(latencies, 0.95),
                                minimum = latencies.Min(),
                                maximum = latencies.Max(),
                            },
                            aliveTabs = tabs.Count(static reference => reference.IsAlive),
                            aliveViews = views.Count(static reference => reference.IsAlive),
                        },
                        ReportJson),
                    TestContext.Current.CancellationToken);
            }

            Assert.All(tabs, static reference => Assert.False(reference.IsAlive, "a closed capture tab is still rooted"));
            Assert.All(views, static reference => Assert.False(reference.IsAlive, "a closed capture workspace is still rooted"));
        }
        finally
        {
            window.Close();
            await main.DisposeAsync();
            WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static AdbDevice Device(string serial) =>
        new(serial, AdbDeviceState.Device, $"Model-{serial}", "product", "1", new Dictionary<string, string>());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected headless UI state did not arrive.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Tab, WeakReference View)> RunCaptureCycleAsync(
        MainView main,
        Window window)
    {
        await using var source = new CompletingSource();
        var tab = await main.Workspace.CaptureAsync(source, null, TestContext.Current.CancellationToken);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var view = main.GetVisualDescendants().OfType<SessionWorkspaceView>().Single();
        var tabReference = new WeakReference(tab);
        var viewReference = new WeakReference(view);

        await main.Workspace.CloseAsync(tab);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab = null!;
        view = null!;
        return (tabReference, viewReference);
    }

    private static void CollectAndPump(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static X21Sample Sample(int cycle)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new X21Sample(
            cycle,
            process.PrivateMemorySize64,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            process.HandleCount,
            process.Threads.Count);
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var ordered = values.Order().ToArray();
        return ordered[(int)Math.Clamp(Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1)];
    }

    private sealed record X21Sample(
        int Cycle,
        long PrivateBytes,
        long WorkingSetBytes,
        long ManagedHeapBytes,
        int Handles,
        int Threads);

    private sealed class DeviceClient(params AdbDevice[] devices) : IAdbClient
    {
        public string ExecutablePath => "fake-adb";
        public IReadOnlyList<AdbDevice> Devices { get; set; } = devices;
        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Devices);
        }
        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public IAdbProcess StartProcess(IReadOnlyList<string> arguments) => throw new NotSupportedException();
    }

    private sealed class BlockingDiscoveryClient : IAdbClient
    {
        public string ExecutablePath => "fake-adb";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
        public Task<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public IAdbProcess StartProcess(IReadOnlyList<string> arguments) => throw new NotSupportedException();
    }

    private sealed class CompletingSource : ILogSource, ISourceStreamStartReporter, ISourceCompletionReporter
    {
        private static readonly byte[] Bytes = Encoding.UTF8.GetBytes(
            "2026-08-30 16:00:00.000000  100  101 I Test: complete record\n");

        public SourceMetadata Metadata { get; } = new(
            SourceKind.Adb,
            "ADB A",
            "ADB device A",
            null,
            null,
            DateTimeOffset.UtcNow,
            IsFinite: false,
            IsReplayable: false,
            DeviceSerial: "A",
            Properties: new Dictionary<string, string> { [SourceMetadata.LogTimeZoneProperty] = "UTC" });
        public SourceCompletionReason? Completion { get; } = new(
            "this capture reached its 1 MiB limit",
            "The live capture reached the 1 MiB size limit it was given and stopped itself.");
        public event Action? StreamEstablished;
        public Task<IReadOnlyList<ReadOnlyMemory<byte>>> ProbeAsync(int maximumUsefulLines, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<byte>>>([Bytes]);
        public async IAsyncEnumerable<SourceChunk> ReadAsync(
            SourceReadContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            StreamEstablished?.Invoke();
            yield return new SourceChunk(0, Bytes);
            await Task.Yield();
        }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
