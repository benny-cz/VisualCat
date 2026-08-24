using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// A masked editor whose automation value is masked too. Avalonia's TextBox paints
/// <see cref="TextBox.PasswordChar"/> but its stock automation peer still returns the plain
/// <see cref="TextBox.Text"/> value on Android (F-45).
/// </summary>
internal sealed class SensitiveTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new SensitiveTextBoxPeer(this);

    private sealed class SensitiveTextBoxPeer(SensitiveTextBox owner)
        : TextBoxAutomationPeer(owner), IValueProvider
    {
        // Re-implement the provider interface because TextBoxAutomationPeer.Value is final in
        // Avalonia 12. This preserves the stock edit-control semantics while ensuring Android's
        // accessibility bridge receives the same masked representation that is painted on screen.
        string IValueProvider.Value => new(owner.PasswordChar, owner.Text?.Length ?? 0);
    }
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
                           "Android leaves Wireless debugging enabled until you turn it off in Settings. While Live runs, " +
                           "Android shows a private ongoing notification so capture can continue with the screen off and " +
                           "you can Stop and save. Android may end background capture after its six-hour service limit; " +
                           "everything already received is kept.",
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

    private readonly SensitiveTextBox _code = new()
    {
        PlaceholderText = "6-digit pairing code",
        MaxLength = 6,
        PasswordChar = '●',
        RevealPassword = false,
    };

    private readonly TextBlock _status = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Opacity = 0.82,
    };

    private readonly TextBlock _portValidation = BuildFieldValidation("Pairing port validation");
    private readonly TextBlock _codeValidation = BuildFieldValidation("Pairing code validation");
    private readonly StackPanel _portFieldGroup;
    private readonly StackPanel _codeFieldGroup;
    private readonly Border _imeScrollReserve = new()
    {
        Height = 120,
        IsHitTestVisible = false,
        IsVisible = false,
    };

    private readonly Button _openWirelessDebugging;
    private readonly Button _connectSavedPairing;
    private readonly Button _pairAndConnect;
    private readonly Button _showNewPairing;
    private readonly Border _savedPairingPanel;
    private readonly StackPanel _newPairingPanel;
    private readonly Button _cancel;
    private readonly DispatcherTimer _validationScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };
    private Control? _validationScrollTarget;
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
        // Avalonia's abstract Pin content type maps to a full text keyboard on Pixel/API 34
        // and does not mask this TextBox in Android's accessibility bridge. Digits is the
        // platform hint that reliably produces a six-digit keypad; PasswordChar supplies the
        // explicit visible/accessibility masking contract (F-45).
        TextInputOptions.SetContentType(_code, TextInputContentType.Digits);
        TextInputOptions.SetReturnKeyType(_code, TextInputReturnKeyType.Done);
        TextInputOptions.SetIsSensitive(_code, true);
        TextInputOptions.SetShowSuggestions(_code, false);
        AutomationProperties.SetName(_port, "Wireless debugging pairing port");
        AutomationProperties.SetHelpText(_port, "Enter only the digits after the colon shown by Android.");
        AutomationProperties.SetName(_code, "Wireless debugging pairing code");
        AutomationProperties.SetHelpText(_code, "Enter the six digits shown in Android's pairing-code panel.");
        _port.TextChanged += (_, _) => ClearFieldValidation(_portValidation);
        _code.TextChanged += (_, _) => ClearFieldValidation(_codeValidation);
        _port.GotFocus += (_, _) => ScheduleFieldReveal(_port);
        _code.GotFocus += (_, _) => ScheduleFieldReveal(_code);
        _portFieldGroup = BuildFieldGroup(_portValidation, _port);
        _codeFieldGroup = BuildFieldGroup(_codeValidation, _code);
        _validationScrollTimer.Tick += ValidationScrollTimerTick;

        _openWirelessDebugging = new Button
        {
            Content = "Open Wireless debugging",
            MinHeight = touch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetHelpText(
            _openWirelessDebugging,
            "Opens Android Developer options at Wireless debugging when supported. Turn it on, then return to this sheet.");
        _openWirelessDebugging.Click += OpenWirelessDebuggingClicked;

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
                           "3. If Android closes that panel when you switch apps, try split screen if your device supports it.\n" +
                           "4. Enter the pairing port shown after the colon and the 6-digit code below, then pair before the code expires.\n" +
                           "5. If Android says “Pairing unsuccessful”, generate a fresh code. Some devices cancel codes even in split screen; " +
                           "if a fresh code also fails, cancel and use VisualCat-only capture.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.82,
                },
                new TextBlock
                {
                    Text = "Pairing port",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                _portFieldGroup,
                new TextBlock
                {
                    Text = "Pairing code",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                },
                _codeFieldGroup,
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
                _openWirelessDebugging,
                _savedPairingPanel,
                _showNewPairing,
                _newPairingPanel,
                _status,
                _imeScrollReserve,
            },
        };

        Content = SheetForm.Build(
            body,
            SheetForm.Decision(null, _cancel, _pairAndConnect),
            new Thickness(16));
    }

    /// <summary>
    /// Keeps a field error beside the field it explains. It precedes the editor so Android's
    /// focus scrolling keeps both visible even above Samsung's taller numeric keyboard. A
    /// single form-level status after the privacy note was previously hidden by the footer.
    /// </summary>
    private static TextBlock BuildFieldValidation(string accessibleName)
    {
        var validation = new TextBlock
        {
            IsVisible = false,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = TextScale.Of(11),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, -4, 0, 2),
        };
        AutomationProperties.SetName(validation, accessibleName);
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Assertive);
        return validation;
    }

    private static StackPanel BuildFieldGroup(TextBlock validation, TextBox editor) => new()
    {
        Spacing = 10,
        Children =
        {
            validation,
            editor,
        },
    };

    private static void ClearFieldValidation(TextBlock validation)
    {
        validation.Text = string.Empty;
        validation.IsVisible = false;
    }

    private void ShowFieldValidation(TextBlock validation, TextBox field, string message)
    {
        _status.Text = string.Empty;
        validation.Text = message;
        validation.IsVisible = true;

        // The explanation precedes the editor so the explicit reveal below keeps the message
        // and its field together after the shared sheet host has moved above the keyboard.
        field.Focus();
        ScheduleFieldReveal(validation.Parent as Control ?? field);
    }

    private void ScheduleFieldReveal(Control target)
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        // The overlay IME is reported separately from Avalonia's viewport on some devices.
        // MainView moves the whole sheet above it; this reserve and delayed reveal then let
        // either final field scroll clear of the pinned decision row. The delay spans the IME
        // animation and also handles port -> code while the pane is already open (F-44).
        _imeScrollReserve.IsVisible = true;
        _validationScrollTarget = target;
        _validationScrollTimer.Stop();
        _validationScrollTimer.Start();
    }

    private void ValidationScrollTimerTick(object? sender, EventArgs eventArgs)
    {
        _validationScrollTimer.Stop();
        var target = _validationScrollTarget;
        _validationScrollTarget = null;
        var scroller = target?.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (target is null || scroller is null)
        {
            return;
        }

        var viewportHeight = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : scroller.Bounds.Height;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.InputPane is { State: InputPaneState.Open } inputPane &&
            inputPane.OccludedRect.Height > 0 &&
            scroller.TranslatePoint(default, topLevel) is { } scrollerOrigin)
        {
            viewportHeight = MainView.ResolveUnoccludedScrollerHeight(
                viewportHeight,
                scrollerOrigin.Y,
                inputPane.OccludedRect.Y);
        }

        if (viewportHeight <= 0 || target.TranslatePoint(default, scroller) is not { } origin)
        {
            return;
        }

        var clearance = Math.Min(8, Math.Max(0, (viewportHeight - target.Bounds.Height) / 2));
        var top = origin.Y;
        var bottom = top + target.Bounds.Height;
        var delta = bottom > viewportHeight - clearance
            ? bottom - (viewportHeight - clearance)
            : top < clearance
                ? top - clearance
                : 0;
        var maximum = Math.Max(0, scroller.Extent.Height - viewportHeight);
        scroller.Offset = new Vector(
            scroller.Offset.X,
            Math.Clamp(scroller.Offset.Y + delta, 0, maximum));
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

    private async void OpenWirelessDebuggingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (PlatformSourceRegistry.OpenWirelessDebuggingSettingsAsync is not { } open)
        {
            _status.Text = "VisualCat cannot open Wireless debugging on this device. Open Android Settings, find Developer options, and choose Wireless debugging manually.";
            return;
        }

        try
        {
            _status.Text = "Opening Wireless debugging…";
            await open(CancellationToken.None);
            _status.Text = "Turn on Wireless debugging, then return here. Use the saved pairing button if available, or open Android's pairing-code panel for a new pairing.";
        }
        catch (Exception exception)
        {
            WorkspaceViewModel.RecordFailure("wireless-adb.open-developer-options", exception);
            _status.Text = "Wireless debugging could not be opened automatically. Open Android Settings, find Developer options, turn on Wireless debugging, then return here.";
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
            ShowFieldValidation(
                _portValidation,
                _port,
                "Enter the pairing port shown by Android: a number from 1 to 65535.");
            return;
        }

        var code = _code.Text?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(static character => character is < '0' or > '9'))
        {
            ShowFieldValidation(
                _codeValidation,
                _code,
                "Enter the 6-digit pairing code exactly as Android shows it.");
            return;
        }

        ClearFieldValidation(_portValidation);
        ClearFieldValidation(_codeValidation);
        _imeScrollReserve.IsVisible = false;

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
        _openWirelessDebugging.IsEnabled = !busy;
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
        _validationScrollTimer.Stop();
        _validationScrollTarget = null;
        CancelOperation();
        GC.SuppressFinalize(this);
    }
}
