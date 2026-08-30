using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VisualCat.Infrastructure.Adb;

namespace VisualCat.App.Views;

public sealed class AdbCaptureDialog : Window, IDisposable
{
    private readonly IAdbClient _client;
    private readonly ComboBox _devices = new() { MinWidth = 420 };
    private readonly TextBlock _status = new();
    private readonly CheckBox _main = new() { Content = "main", IsChecked = true };
    private readonly CheckBox _system = new() { Content = "system", IsChecked = true };
    private readonly CheckBox _crash = new() { Content = "crash", IsChecked = true };
    private readonly CheckBox _events = new() { Content = "events" };
    private readonly CheckBox _radio = new() { Content = "radio" };
    private readonly NumericUpDown _preRollSeconds = new() { Minimum = 0, Maximum = 3600, Increment = 5, Value = 0, Width = 110 };
    private readonly CheckBox _includeBufferHistory = new() { Content = "Include everything already in the buffer" };
    private readonly NumericUpDown _durationMinutes = new() { Minimum = 0, Maximum = 10_080, Increment = 1, Value = 0, Width = 110 };
    private readonly NumericUpDown _maximumMiB = new() { Minimum = 0, Maximum = 1_048_576, Increment = 128, Value = 0, Width = 130 };
    private readonly Button _start = new() { Content = "Start capture", IsEnabled = false };
    private readonly DispatcherTimer _deviceRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBlock _validation = new()
    {
        Text = "Select at least one buffer to start.",
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private bool _refreshing;
    private bool _disposed;
    private bool _updatingDeviceSelection;
    private bool _deviceListCurrent;
    private DeviceChoice? _rememberedSelection;
    private int _detectedDeviceCount;

    public AdbCaptureDialog(
        IAdbClient client,
        IReadOnlyCollection<string>? defaultBuffers = null,
        int preRollSeconds = 0)
    {
        _client = client;
        var selectedBuffers = defaultBuffers?.ToHashSet(StringComparer.Ordinal) ??
                              new HashSet<string>(["main", "system", "crash"], StringComparer.Ordinal);
        _main.IsChecked = selectedBuffers.Contains("main");
        _system.IsChecked = selectedBuffers.Contains("system");
        _crash.IsChecked = selectedBuffers.Contains("crash");
        _events.IsChecked = selectedBuffers.Contains("events");
        _radio.IsChecked = selectedBuffers.Contains("radio");
        _preRollSeconds.Value = Math.Clamp(preRollSeconds, 0, 3600);

        AutomationProperties.SetName(_devices, "Android device");
        AutomationProperties.SetHelpText(_devices, "Authorized, unauthorized, and offline Android devices reported by ADB.");
        AutomationProperties.SetName(_preRollSeconds, "Pre-roll seconds");
        AutomationProperties.SetHelpText(_preRollSeconds, "Zero starts from now. Choose a positive value to include that many seconds before Start.");
        AutomationProperties.SetName(_durationMinutes, "Stop after minutes");
        AutomationProperties.SetHelpText(_durationMinutes, "Zero keeps capturing until you stop it.");
        AutomationProperties.SetName(_maximumMiB, "Stop after megabytes");
        AutomationProperties.SetHelpText(_maximumMiB, "Zero applies no size limit.");
        AutomationProperties.SetHelpText(
            _includeBufferHistory,
            "Includes the complete existing Android logcat ring buffer. On a busy device this can add hundreds of thousands of older records.");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_validation, AutomationLiveSetting.Polite);
        SheetForm.PrepareSpinButtons(_preRollSeconds, "pre-roll seconds");
        SheetForm.PrepareSpinButtons(_durationMinutes, "stop-after minutes");
        SheetForm.PrepareSpinButtons(_maximumMiB, "stop-after megabytes");

        Title = "Live ADB capture";
        Width = 600;
        Height = 410;
        MinWidth = 600;
        MinHeight = 410;
        CanResize = true;
        Content = Build();
        _deviceRefreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        Opened += async (_, _) =>
        {
            _deviceRefreshTimer.Start();
            await RefreshDevicesAsync();
        };
        Closed += (_, _) =>
        {
            _deviceRefreshTimer.Stop();
            if (!_disposed)
            {
                _lifetime.Cancel();
            }
        };
    }

    public AdbLogSource? SelectedSource { get; private set; }
    public TimeSpan? CaptureDuration { get; private set; }

    /// <summary>Deterministic refresh seam for the headless state-transition tests.</summary>
    internal Task RefreshDevicesForTestsAsync() => RefreshDevicesAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceRefreshTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private StackPanel Build()
    {
        var root = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Select an authorized Android device and logcat buffers.",
            FontSize = TextScale.Of(16),
        });
        _devices.SelectionChanged += (_, _) =>
        {
            if (!_updatingDeviceSelection && _devices.SelectedItem is DeviceChoice { IsDisconnected: false } choice)
            {
                // A deliberate choice replaces the serial we protect. A refresh does not:
                // if that serial disappears, it remains selected as disconnected instead of
                // silently moving the pending capture to another phone (finding F-04).
                _rememberedSelection = choice;
                SetNormalDeviceStatus();
            }

            UpdateStartAvailability();
        };
        root.Children.Add(_devices);
        var buffers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buffers.Children.Add(_main);
        buffers.Children.Add(_system);
        buffers.Children.Add(_crash);
        buffers.Children.Add(_events);
        buffers.Children.Add(_radio);
        foreach (var buffer in buffers.Children.OfType<CheckBox>())
        {
            buffer.IsCheckedChanged += (_, _) => UpdateStartAvailability();
        }
        root.Children.Add(buffers);
        var preRoll = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        preRoll.Children.Add(new TextBlock { Text = "Pre-roll seconds (0 = from now)", VerticalAlignment = VerticalAlignment.Center });
        preRoll.Children.Add(_preRollSeconds);
        root.Children.Add(preRoll);
        _includeBufferHistory.IsCheckedChanged += (_, _) =>
        {
            _preRollSeconds.IsEnabled = _includeBufferHistory.IsChecked != true;
            UpdateStartAvailability();
        };
        root.Children.Add(_includeBufferHistory);
        var limits = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        limits.Children.Add(new TextBlock { Text = "Stop after minutes (0 = unlimited)", VerticalAlignment = VerticalAlignment.Center });
        limits.Children.Add(_durationMinutes);
        limits.Children.Add(new TextBlock { Text = "or MiB", VerticalAlignment = VerticalAlignment.Center });
        limits.Children.Add(_maximumMiB);
        root.Children.Add(limits);
        _status.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_status);
        root.Children.Add(_validation);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        var refresh = new Button { Content = "Refresh devices" };
        refresh.Click += async (_, _) => await RefreshDevicesAsync();
        actions.Children.Add(refresh);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        actions.Children.Add(cancel);
        _start.Click += (_, _) => StartCapture();
        actions.Children.Add(_start);
        root.Children.Add(actions);
        return root;
    }

    private async Task RefreshDevicesAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        var selected = _devices.SelectedItem as DeviceChoice ?? _rememberedSelection;
        if (_devices.ItemsSource is null)
        {
            _status.Text = "Discovering devices…";
        }

        try
        {
            using var refresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            refresh.CancelAfter(TimeSpan.FromSeconds(5));
            var devices = await _client.ListDevicesAsync(refresh.Token);
            var connectedChoices = devices.Select(static device => new DeviceChoice(device)).ToArray();
            _detectedDeviceCount = connectedChoices.Length;
            _deviceListCurrent = true;

            var selectedSerial = selected?.Device.Serial;
            var matching = connectedChoices.FirstOrDefault(choice =>
                string.Equals(choice.Device.Serial, selectedSerial, StringComparison.Ordinal));
            IReadOnlyList<DeviceChoice> choices = connectedChoices;
            DeviceChoice? nextSelection = matching;
            if (nextSelection is null && selected is not null)
            {
                var missing = selected with { IsDisconnected = true };
                choices = [missing, .. connectedChoices];
                nextSelection = missing;
            }
            else if (nextSelection is null && connectedChoices.Length > 0)
            {
                nextSelection = connectedChoices[0];
                _rememberedSelection = nextSelection;
            }

            _updatingDeviceSelection = true;
            try
            {
                _devices.ItemsSource = choices;
                _devices.SelectedItem = nextSelection;
            }
            finally
            {
                _updatingDeviceSelection = false;
            }

            if (nextSelection is { IsDisconnected: true } disconnected)
            {
                _status.Text =
                    $"Device {disconnected.Device.Serial} disconnected. Reconnect it or deliberately select another device; " +
                    $"{CountedDevices(_detectedDeviceCount)} currently available.";
            }
            else
            {
                SetNormalDeviceStatus();
            }

            UpdateStartAvailability();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Closing owns this cancellation. ProcessAdbClient disposes the command process,
            // which kills a wedged `adb devices -l` child with the dialog (finding F-05).
        }
        catch (OperationCanceledException)
        {
            _deviceListCurrent = false;
            _status.Text =
                "ADB is not responding. The device list may be stale; restart the ADB server and refresh before starting.";
            UpdateStartAvailability();
        }
        catch (Exception exception)
        {
            _deviceListCurrent = false;

            // Product sentence, never the framework's own text: a trimmed build answers with
            // a resource key instead of a message (finding F-04), and "could not list" is the
            // part a reader can act on either way.
            _status.Text =
                $"Could not refresh devices · {Presentation.WorkspaceViewModel.FriendlyMessage(exception)} " +
                "The current list is kept for reference; refresh it before starting.";
            UpdateStartAvailability();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetNormalDeviceStatus()
    {
        _status.Text = _detectedDeviceCount == 0
            ? "No devices detected. Connect a device and enable USB debugging, then refresh."
            : $"{CountedDevices(_detectedDeviceCount)} detected. Unauthorized devices must be approved on the device.";
    }

    private static string CountedDevices(int count) => count == 1 ? "1 device" : $"{count:N0} devices";

    private void UpdateStartAvailability()
    {
        var hasBuffers = _main.IsChecked == true || _system.IsChecked == true ||
                         _crash.IsChecked == true || _events.IsChecked == true || _radio.IsChecked == true;
        _validation.IsVisible = !hasBuffers;
        _start.IsEnabled = _deviceListCurrent && hasBuffers &&
                           _devices.SelectedItem is DeviceChoice
                           {
                               IsDisconnected: false,
                               Device.State: AdbDeviceState.Device,
                           };
    }

    private void StartCapture()
    {
        if (_devices.SelectedItem is not DeviceChoice { Device.State: AdbDeviceState.Device } selected)
        {
            return;
        }

        var buffers = new List<string>();
        Add(_main);
        Add(_system);
        Add(_crash);
        Add(_events);
        Add(_radio);
        if (buffers.Count == 0)
        {
            _status.Text = "Select at least one buffer.";
            return;
        }

        var maximumMiB = _maximumMiB.Value ?? 0;
        var maximumBytes = maximumMiB <= 0
            ? (long?)null
            : checked((long)maximumMiB * 1024 * 1024);
        var minutes = _durationMinutes.Value ?? 0;
        CaptureDuration = minutes <= 0 ? null : TimeSpan.FromMinutes((double)minutes);
        SelectedSource = new AdbLogSource(
            _client,
            selected.Device.Serial,
            buffers,
            maximumBytes,
            _includeBufferHistory.IsChecked == true
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds((double)(_preRollSeconds.Value ?? 0)),
            includeBufferHistory: _includeBufferHistory.IsChecked == true,
            durationLimit: CaptureDuration);
        Close(true);

        void Add(CheckBox checkBox)
        {
            if (checkBox.IsChecked == true && checkBox.Content is string name)
            {
                buffers.Add(name);
            }
        }
    }

    private sealed record DeviceChoice(AdbDevice Device, bool IsDisconnected = false)
    {
        public override string ToString() =>
            IsDisconnected
                ? $"{Device.Serial} · {Device.Model ?? Device.Product ?? "Android device"} · Not connected"
                : $"{Device.Serial} · {Device.Model ?? Device.Product ?? "Android device"} · {Device.State}";
    }
}
