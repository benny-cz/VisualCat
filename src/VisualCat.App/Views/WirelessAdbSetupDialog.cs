using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;

namespace VisualCat.App.Views;

internal enum OnDeviceLogAccessChoice
{
    Cancel,
    SetUpFullDevice,
    CaptureVisualCatOnly,
}

/// <summary>
/// Lets the reader choose between full-device Wireless debugging capture and Android's deliberately
/// limited own-process fallback before any setup UI is shown.
/// </summary>
internal sealed class OnDeviceLogAccessDialog : DialogBody<OnDeviceLogAccessChoice>
{
    private readonly RadioButton _fullDevice;
    private readonly RadioButton _visualCatOnly;

    internal OnDeviceLogAccessDialog()
        : base("Choose what Live captures")
    {
        PreferredSize = new Size(560, 520);
        MinimumSize = new Size(360, 360);
        ScrollsInternally = true;

        var mobile = OperatingSystem.IsAndroid();
        var touch = mobile ? 48 : 0;
        var hasSavedPairing = PlatformSourceRegistry.HasSavedWirelessAdbIdentity?.Invoke() == true;
        const string fullDeviceLabel = "Full-device capture";

        _fullDevice = new RadioButton
        {
            Content = fullDeviceLabel,
            GroupName = "LogAccessScope",
            IsChecked = true,
            MinHeight = touch,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        AutomationProperties.SetName(_fullDevice, fullDeviceLabel);
        AutomationProperties.SetHelpText(
            _fullDevice,
            hasSavedPairing
                ? "Recommended. Reconnects the saved Android Wireless debugging pairing to capture the whole device log."
                : "Recommended. Uses Android Wireless debugging to capture the whole device log; pairing is usually needed only once.");

        _visualCatOnly = new RadioButton
        {
            Content = "Capture VisualCat only",
            GroupName = "LogAccessScope",
            MinHeight = touch,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        AutomationProperties.SetName(_visualCatOnly, "Capture VisualCat only");
        AutomationProperties.SetHelpText(
            _visualCatOnly,
            "Starts immediately, but Android exposes only this app's own log lines.");

        var fullDeviceDescription = Description(
            hasSavedPairing ? "Recommended · already paired" : "Recommended · setup required",
            hasSavedPairing
                ? "Turn on Wireless debugging and reconnect with the saved pairing. No new code is normally needed. " +
                  "VisualCat closes its connection when Live stops; Android leaves Wireless debugging on until you turn it off."
                : "For logs from other apps and system components, VisualCat uses Android Wireless debugging. Pairing is usually " +
                  "needed only once, no computer or root is required, and VisualCat closes its connection when Live stops.");

        var restrictedDescription = Description(
            "No setup",
            "Starts immediately, but Android exposes only VisualCat's own log lines. If VisualCat is idle, " +
            "Live may show few or no new lines; other apps and most system components are not visible.");

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Choose how Live should read this device's log.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = TextScale.Of(15),
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Nothing is uploaded. Full-device capture uses Android Wireless debugging only on this device, " +
                           "uses the connection only to read the Android log, and closes its connection when Live stops. " +
                           "Android leaves Wireless debugging enabled until you turn it off in Settings.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.82,
                },
                Choice(_fullDevice, fullDeviceDescription),
                Choice(_visualCatOnly, restrictedDescription),
            },
        };

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinHeight = touch,
        };
        cancel.Click += (_, _) => Complete(OnDeviceLogAccessChoice.Cancel);

        var continueButton = new Button
        {
            IsDefault = true,
            MinHeight = touch,
        };

        void UpdatePrimaryAction()
        {
            continueButton.Content = _visualCatOnly.IsChecked == true
                ? "Start VisualCat-only"
                : hasSavedPairing
                    ? "Connect full-device"
                    : "Set up full-device";
        }

        _fullDevice.IsCheckedChanged += (_, _) => UpdatePrimaryAction();
        _visualCatOnly.IsCheckedChanged += (_, _) => UpdatePrimaryAction();
        UpdatePrimaryAction();

        continueButton.Click += (_, _) => Complete(
            _visualCatOnly.IsChecked == true
                ? OnDeviceLogAccessChoice.CaptureVisualCatOnly
                : OnDeviceLogAccessChoice.SetUpFullDevice);

        Content = SheetForm.Build(
            body,
            SheetForm.Decision(null, cancel, continueButton),
            new Thickness(16));
    }

    private static Border Choice(RadioButton option, Control description) => new()
    {
        Padding = new Thickness(12, 8),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                option,
                description,
            },
        },
    };

    private static StackPanel Description(string heading, string text) => new StackPanel
    {
        Margin = new Thickness(30, 0, 0, 4),
        Spacing = 3,
        Children =
        {
            new TextBlock
            {
                Text = heading,
                FontSize = TextScale.Of(11),
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Opacity = 0.82,
            },
            new TextBlock
            {
                Text = text,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = TextScale.Of(12),
                Opacity = 0.82,
            },
        },
    };
}

/// <summary>
/// Guided Android Wireless debugging setup and reconnect flow for full-device logcat.
/// </summary>
internal sealed class WirelessAdbSetupDialog : DialogBody<bool>, IDisposable
{
    private readonly TextBox _port = new()
    {
        PlaceholderText = "Pairing port",
        MaxLength = 5,
    };

    private readonly TextBox _code = new()
    {
        PlaceholderText = "6-digit pairing code",
        MaxLength = 6,
    };

    private readonly TextBlock _status = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Opacity = 0.82,
    };

    private readonly Button _openDeveloperOptions;
    private readonly Button _connectSavedPairing;
    private readonly Button _pairAndConnect;
    private readonly Button _showNewPairing;
    private readonly Border _savedPairingPanel;
    private readonly StackPanel _newPairingPanel;
    private readonly Button _cancel;
    private CancellationTokenSource? _operationCancellation;
    private bool _dismissWhenOperationEnds;
    private readonly bool _connectSavedImmediately;
    private bool _automaticConnectStarted;

    internal WirelessAdbSetupDialog(bool connectSavedImmediately = false)
        : base("Connect full-device capture")
    {
        PreferredSize = new Size(580, 740);
        MinimumSize = new Size(360, 430);
        ScrollsInternally = true;

        var mobile = OperatingSystem.IsAndroid();
        var touch = mobile ? 48 : 0;
        var hasSavedPairing = PlatformSourceRegistry.HasSavedWirelessAdbIdentity?.Invoke() == true;
        _connectSavedImmediately = connectSavedImmediately && hasSavedPairing;

        _port.MinHeight = touch;
        _code.MinHeight = touch;
        TextInputOptions.SetContentType(_port, TextInputContentType.Digits);
        TextInputOptions.SetReturnKeyType(_port, TextInputReturnKeyType.Next);
        TextInputOptions.SetShowSuggestions(_port, false);
        TextInputOptions.SetContentType(_code, TextInputContentType.Pin);
        TextInputOptions.SetReturnKeyType(_code, TextInputReturnKeyType.Done);
        TextInputOptions.SetIsSensitive(_code, true);
        TextInputOptions.SetShowSuggestions(_code, false);
        AutomationProperties.SetName(_port, "Wireless debugging pairing port");
        AutomationProperties.SetHelpText(_port, "Enter only the digits after the colon shown by Android.");
        AutomationProperties.SetName(_code, "Wireless debugging pairing code");
        AutomationProperties.SetHelpText(_code, "Enter the six digits shown in Android's pairing-code panel.");

        _openDeveloperOptions = new Button
        {
            Content = "Open Developer options",
            MinHeight = touch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetHelpText(
            _openDeveloperOptions,
            "Opens Android Settings. Turn on Wireless debugging, then return to this sheet.");
        _openDeveloperOptions.Click += OpenDeveloperOptionsClicked;

        _connectSavedPairing = new Button
        {
            Content = "Connect saved pairing",
            MinHeight = touch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = hasSavedPairing,
            IsDefault = hasSavedPairing,
        };
        AutomationProperties.SetHelpText(
            _connectSavedPairing,
            "Reconnect without a new code when Android still lists VisualCat under Wireless debugging paired devices.");
        _connectSavedPairing.Click += ConnectSavedPairingClicked;

        _cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            MinHeight = touch,
        };
        _cancel.Click += (_, _) => RequestDismiss();

        _pairAndConnect = new Button
        {
            Content = "Pair & connect",
            IsDefault = !hasSavedPairing,
            IsVisible = !hasSavedPairing,
            MinHeight = touch,
        };
        _pairAndConnect.Click += PairAndConnectClicked;

        AttachedToVisualTree += AutomaticConnectOnAttach;
        DetachedFromVisualTree += (_, _) => Dispose();

        _savedPairingPanel = new Border
        {
            IsVisible = hasSavedPairing,
            Margin = new Thickness(0, 2),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Saved pairing",
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = "Turn on Wireless debugging, then connect with the saved pairing. " +
                               "You normally do not need another pairing code.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Opacity = 0.8,
                    },
                    _connectSavedPairing,
                },
            },
        };

        _newPairingPanel = new StackPanel
        {
            IsVisible = !hasSavedPairing,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Pair a new connection",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "1. In Developer options, turn on Wireless debugging.\n" +
                           "2. Tap “Pair device with pairing code” and keep that panel open.\n" +
                           "3. If Android closes that panel when you switch apps, use split screen if your device supports it.\n" +
                           "4. Enter the pairing port shown after the colon and the 6-digit code below.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.82,
                },
                new TextBlock
                {
                    Text = "Pairing port",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                _port,
                new TextBlock
                {
                    Text = "Pairing code",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                _code,
                new TextBlock
                {
                    Text = "The 6-digit pairing code is used only for this attempt and is not saved or written to logs. " +
                           "VisualCat stores only the reusable pairing identity, encrypted with Android Keystore.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = TextScale.Of(11),
                    Opacity = 0.72,
                },
            },
        };

        _showNewPairing = new Button
        {
            Content = "Pair again with a new code",
            IsVisible = hasSavedPairing,
            MinHeight = touch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetHelpText(
            _showNewPairing,
            "Show a new pairing-code form if Android no longer accepts the saved pairing.");
        _showNewPairing.Click += (_, _) =>
        {
            _showNewPairing.IsVisible = false;
            _newPairingPanel.IsVisible = true;
            _pairAndConnect.IsVisible = true;
            _connectSavedPairing.IsDefault = false;
            _pairAndConnect.IsDefault = true;
            _port.Focus();
        };

        var body = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = hasSavedPairing
                        ? "Turn on Wireless debugging, then reconnect the saved pairing."
                        : "Pair VisualCat with Android Wireless debugging. This is usually needed only once.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = TextScale.Of(14),
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Wireless debugging must stay on while Live is running. VisualCat uses the connection only for " +
                           "the device log and closes it when capture stops. Android does not switch Wireless debugging off " +
                           "for apps, so turn it off in Settings when you finish.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.86,
                },
                _openDeveloperOptions,
                _savedPairingPanel,
                _showNewPairing,
                _newPairingPanel,
                _status,
            },
        };

        Content = SheetForm.Build(
            body,
            SheetForm.Decision(null, _cancel, _pairAndConnect),
            new Thickness(16));
    }

    private async void AutomaticConnectOnAttach(object? sender, Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        if (!_connectSavedImmediately || _automaticConnectStarted)
        {
            return;
        }

        _automaticConnectStarted = true;
        if (PlatformSourceRegistry.ConnectSavedWirelessAdbAsync is not { } connect)
        {
            _status.Text = "The saved pairing connection is unavailable. Pair again with a new code.";
            return;
        }

        await RunConnectionAsync(
            "Looking for Android's Wireless debugging connection…",
            token => connect(token));
    }

    private async void OpenDeveloperOptionsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (PlatformSourceRegistry.OpenDeveloperOptionsAsync is not { } open)
        {
            _status.Text = "VisualCat cannot open Developer options on this device. Open Android Settings and find Developer options manually.";
            return;
        }

        try
        {
            _status.Text = "Opening Developer options…";
            await open(CancellationToken.None);
            _status.Text = "Turn on Wireless debugging, then return here. Use the saved pairing button if available, or open Android's pairing-code panel for a new pairing.";
        }
        catch (Exception exception)
        {
            WorkspaceViewModel.RecordFailure("wireless-adb.open-developer-options", exception);
            _status.Text = "Developer options could not be opened automatically. Open Android Settings, find Developer options, turn on Wireless debugging, then return here.";
        }
    }

    private async void ConnectSavedPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (PlatformSourceRegistry.ConnectSavedWirelessAdbAsync is not { } connect)
        {
            _status.Text = "The saved pairing is unavailable. Turn on Wireless debugging and pair again with a new code.";
            return;
        }

        await RunConnectionAsync(
            "Looking for Android's Wireless debugging connection…",
            token => connect(token));
    }

    private async void PairAndConnectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (PlatformSourceRegistry.PairWirelessAdbAsync is not { } pairAndConnect)
        {
            _status.Text = "Wireless debugging setup is unavailable on this device. Cancel and use VisualCat-only capture instead.";
            return;
        }

        if (!int.TryParse(_port.Text?.Trim(), out var port) || port is < 1 or > 65535)
        {
            _status.Text = "Enter the pairing port shown by Android: a number from 1 to 65535.";
            _port.Focus();
            return;
        }

        var code = _code.Text?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(static character => character is < '0' or > '9'))
        {
            _status.Text = "Enter the 6-digit pairing code exactly as Android shows it.";
            _code.Focus();
            return;
        }

        var request = new WirelessAdbPairingRequest(port, code);
        await RunConnectionAsync(
            "Pairing with Android and opening the local full-device log connection…",
            token => pairAndConnect(request, token));

        // Do not keep a pairing code in the control after an attempt, successful or not. Android
        // expires it quickly anyway, and retaining it would create a secret-looking value with no
        // useful purpose.
        _code.Text = string.Empty;
    }

    private async Task RunConnectionAsync(
        string initialStatus,
        Func<CancellationToken, Task<WirelessAdbConnectionResult>> operation)
    {
        if (_operationCancellation is not null)
        {
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        SetBusy(true);
        _status.Text = initialStatus;

        try
        {
            var result = await operation(_operationCancellation.Token);
            if (_dismissWhenOperationEnds)
            {
                return;
            }

            _status.Text = result.Message;
            if (result.Connected)
            {
                Complete(true);
            }
            else if (result.PairingSucceeded)
            {
                // Pairing and the subsequent authenticated connection are separate Android
                // operations. If the key was accepted but discovery/connect failed, switch the
                // sheet to the returning-user state instead of asking for another pairing code.
                _savedPairingPanel.IsVisible = true;
                _connectSavedPairing.IsVisible = true;
                _showNewPairing.IsVisible = true;
                _newPairingPanel.IsVisible = false;
                _pairAndConnect.IsVisible = false;
                _connectSavedPairing.IsDefault = true;
                _pairAndConnect.IsDefault = false;
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Connection cancelled.";
        }
        catch (Exception exception)
        {
            WorkspaceViewModel.RecordFailure("wireless-adb.connection", exception);
            _status.Text = "Connection failed. Keep Wireless debugging on and try again. If it still fails, pair again with a new code.";
        }
        finally
        {
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            SetBusy(false);
            if (_dismissWhenOperationEnds)
            {
                _dismissWhenOperationEnds = false;
                Complete(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _port.IsEnabled = !busy;
        _code.IsEnabled = !busy;
        _openDeveloperOptions.IsEnabled = !busy;
        _connectSavedPairing.IsEnabled = !busy;
        _showNewPairing.IsEnabled = !busy;
        _pairAndConnect.IsEnabled = !busy;
        _cancel.IsEnabled = true;
        _cancel.Content = busy ? "Cancel connection" : "Cancel";
    }

    internal override void Dismiss() => RequestDismiss();

    private void RequestDismiss()
    {
        if (_operationCancellation is null)
        {
            Complete(false);
            return;
        }

        _dismissWhenOperationEnds = true;
        CancelOperation();
        _status.Text = "Cancelling connection…";
        _cancel.Content = "Cancelling…";
        _cancel.IsEnabled = false;
    }

    private void CancelOperation()
    {
        try
        {
            _operationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        CancelOperation();
        GC.SuppressFinalize(this);
    }
}
