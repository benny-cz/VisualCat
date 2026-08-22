using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using VisualCat.Infrastructure.Adb;

namespace VisualCat.App.Views;

public sealed class AdbCaptureDialog : Window
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
    private readonly NumericUpDown _durationMinutes = new() { Minimum = 0, Maximum = 10_080, Increment = 1, Value = 0, Width = 110 };
    private readonly NumericUpDown _maximumMiB = new() { Minimum = 0, Maximum = 1_048_576, Increment = 128, Value = 0, Width = 130 };
    private readonly Button _start = new() { Content = "Start capture", IsEnabled = false };
    private readonly DispatcherTimer _deviceRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _refreshing;

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
        Title = "Live ADB capture";
        Width = 560;
        Height = 360;
        CanResize = false;
        Content = Build();
        _deviceRefreshTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        Opened += async (_, _) =>
        {
            _deviceRefreshTimer.Start();
            await RefreshDevicesAsync();
        };
        Closed += (_, _) => _deviceRefreshTimer.Stop();
    }

    public AdbLogSource? SelectedSource { get; private set; }
    public TimeSpan? CaptureDuration { get; private set; }

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
            _start.IsEnabled = _devices.SelectedItem is DeviceChoice { Device.State: AdbDeviceState.Device };
        };
        root.Children.Add(_devices);
        var buffers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buffers.Children.Add(_main);
        buffers.Children.Add(_system);
        buffers.Children.Add(_crash);
        buffers.Children.Add(_events);
        buffers.Children.Add(_radio);
        root.Children.Add(buffers);
        var preRoll = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        preRoll.Children.Add(new TextBlock { Text = "Pre-roll seconds", VerticalAlignment = VerticalAlignment.Center });
        preRoll.Children.Add(_preRollSeconds);
        root.Children.Add(preRoll);
        var limits = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        limits.Children.Add(new TextBlock { Text = "Stop after minutes (0 = unlimited)", VerticalAlignment = VerticalAlignment.Center });
        limits.Children.Add(_durationMinutes);
        limits.Children.Add(new TextBlock { Text = "or MiB", VerticalAlignment = VerticalAlignment.Center });
        limits.Children.Add(_maximumMiB);
        root.Children.Add(limits);
        root.Children.Add(_status);

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
        var selectedSerial = (_devices.SelectedItem as DeviceChoice)?.Device.Serial;
        _status.Text = "Discovering devices…";
        _start.IsEnabled = false;
        try
        {
            var devices = await _client.ListDevicesAsync(CancellationToken.None);
            var choices = devices.Select(static device => new DeviceChoice(device)).ToArray();
            _devices.ItemsSource = choices;
            _devices.SelectedItem = choices.FirstOrDefault(choice =>
                string.Equals(choice.Device.Serial, selectedSerial, StringComparison.Ordinal));
            if (_devices.SelectedItem is null)
            {
                _devices.SelectedIndex = choices.Length == 0 ? -1 : 0;
            }

            _status.Text = choices.Length == 0
                ? "No devices detected. Connect a device and enable USB debugging, then refresh."
                : $"{choices.Length} device(s) detected. Unauthorized devices must be approved on the device.";
        }
        catch (Exception exception)
        {
            _devices.ItemsSource = null;

            // Product sentence, never the framework's own text: a trimmed build answers with
            // a resource key instead of a message (finding F-04), and "could not list" is the
            // part a reader can act on either way.
            _status.Text =
                $"Could not list devices · {Presentation.WorkspaceViewModel.FriendlyMessage(exception)}";
        }
        finally
        {
            _refreshing = false;
        }
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
            TimeSpan.FromSeconds((double)(_preRollSeconds.Value ?? 0)));
        Close(true);

        void Add(CheckBox checkBox)
        {
            if (checkBox.IsChecked == true && checkBox.Content is string name)
            {
                buffers.Add(name);
            }
        }
    }

    private sealed record DeviceChoice(AdbDevice Device)
    {
        public override string ToString() =>
            $"{Device.Serial} · {Device.Model ?? Device.Product ?? "Android device"} · {Device.State}";
    }
}
