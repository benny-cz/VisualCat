using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VisualCat.App.Platform;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// Headless UX contracts for Android's optional Wireless debugging full-device path.
/// </summary>
public sealed class WirelessAdbSetupTests
{
    [AvaloniaFact]
    public void FirstUseExplainsPairingWithoutClaimingAReadLogsGrant()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var dialog = new OnDeviceLogAccessDialog();
            var text = string.Join(
                "\n",
                dialog.GetLogicalDescendants().OfType<TextBlock>().Select(static block => block.Text ?? string.Empty));

            Assert.Contains("setup required", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("READ_LOGS", text, StringComparison.Ordinal);
            Assert.Contains("Nothing is uploaded", text, StringComparison.Ordinal);
            Assert.Contains("Wireless debugging", text, StringComparison.Ordinal);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void SavedIdentityMakesReconnectThePrimaryMentalModel()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => true;
            var dialog = new OnDeviceLogAccessDialog();
            var radios = dialog.GetLogicalDescendants().OfType<RadioButton>().ToArray();

            Assert.Contains(
                radios,
                static radio => string.Equals(radio.Content?.ToString(), "Full-device capture", StringComparison.Ordinal));
            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<Button>(),
                static button => string.Equals(button.Content?.ToString(), "Connect full-device", StringComparison.Ordinal));
            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                static block => (block.Text ?? string.Empty).Contains("already paired", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void SavedPairingButtonIsHiddenUntilAnIdentityExists()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var firstUse = new WirelessAdbSetupDialog();
            var hiddenReconnect = firstUse.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Connect saved pairing", StringComparison.Ordinal));
            Assert.False(hiddenReconnect.IsVisible);

            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => true;
            var returning = new WirelessAdbSetupDialog();
            var visibleReconnect = returning.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Connect saved pairing", StringComparison.Ordinal));
            Assert.True(visibleReconnect.IsVisible);
            Assert.True(visibleReconnect.IsDefault);
            Assert.Contains(
                returning.GetLogicalDescendants().OfType<TextBlock>(),
                static block => (block.Text ?? string.Empty).Contains("normally do not need another pairing code", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }


    [AvaloniaFact]
    public void InvalidPairingPortIsRejectedBeforePlatformPairing()
    {
        var previousIdentity = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        var previousPair = PlatformSourceRegistry.PairWirelessAdbAsync;
        try
        {
            var calls = 0;
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            PlatformSourceRegistry.PairWirelessAdbAsync = (request, cancellationToken) =>
            {
                _ = request;
                _ = cancellationToken;
                calls++;
                return Task.FromResult(new WirelessAdbConnectionResult(true, true, "Connected."));
            };

            var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing port").Text = "0";
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing code").Text = "123456";

            dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0, calls);
            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                static block => (block.Text ?? string.Empty).Contains("1 to 65535", StringComparison.Ordinal));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previousIdentity;
            PlatformSourceRegistry.PairWirelessAdbAsync = previousPair;
        }
    }

    [AvaloniaFact]
    public void FieldValidationStaysBesideTheFieldAndClearsWhenEdited()
    {
        var previousIdentity = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        var previousPair = PlatformSourceRegistry.PairWirelessAdbAsync;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            PlatformSourceRegistry.PairWirelessAdbAsync = static (_, _) =>
                Task.FromResult(new WirelessAdbConnectionResult(true, true, "Connected."));

            using var dialog = new WirelessAdbSetupDialog();
            var port = dialog.GetLogicalDescendants().OfType<TextBox>().Single(static input =>
                AutomationProperties.GetName(input) == "Wireless debugging pairing port");
            var code = dialog.GetLogicalDescendants().OfType<TextBox>().Single(static input =>
                AutomationProperties.GetName(input) == "Wireless debugging pairing code");
            var portValidation = dialog.GetLogicalDescendants().OfType<TextBlock>().Single(static block =>
                AutomationProperties.GetName(block) == "Pairing port validation");
            var codeValidation = dialog.GetLogicalDescendants().OfType<TextBlock>().Single(static block =>
                AutomationProperties.GetName(block) == "Pairing code validation");
            var submit = dialog.GetLogicalDescendants().OfType<Button>().Single(static button =>
                string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal));

            submit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            var portPanel = Assert.IsType<StackPanel>(port.Parent);
            Assert.Same(portPanel, portValidation.Parent);
            Assert.Equal(2, portPanel.Children.Count);
            Assert.Equal(portPanel.Children.IndexOf(port) - 1, portPanel.Children.IndexOf(portValidation));
            Assert.True(portValidation.IsVisible);
            Assert.Contains("1 to 65535", portValidation.Text, StringComparison.Ordinal);
            Assert.False(codeValidation.IsVisible);

            port.Text = "37123";
            Dispatcher.UIThread.RunJobs();
            Assert.False(portValidation.IsVisible);
            code.Text = "12345x";
            submit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            var codePanel = Assert.IsType<StackPanel>(code.Parent);
            Assert.Same(codePanel, codeValidation.Parent);
            Assert.Equal(2, codePanel.Children.Count);
            Assert.Equal(codePanel.Children.IndexOf(code) - 1, codePanel.Children.IndexOf(codeValidation));
            Assert.True(codeValidation.IsVisible);
            Assert.Contains("exactly as Android shows", codeValidation.Text, StringComparison.Ordinal);

            code.Text = "123456";
            Dispatcher.UIThread.RunJobs();
            Assert.False(codeValidation.IsVisible);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previousIdentity;
            PlatformSourceRegistry.PairWirelessAdbAsync = previousPair;
        }
    }

    [AvaloniaFact]
    public void NonNumericPairingCodeIsRejectedBeforePlatformPairing()
    {
        var previousIdentity = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        var previousPair = PlatformSourceRegistry.PairWirelessAdbAsync;
        try
        {
            var calls = 0;
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            PlatformSourceRegistry.PairWirelessAdbAsync = (request, cancellationToken) =>
            {
                _ = request;
                _ = cancellationToken;
                calls++;
                return Task.FromResult(new WirelessAdbConnectionResult(true, true, "Connected."));
            };

            var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing port").Text = "37123";
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing code").Text = "12345x";

            dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(0, calls);
            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                static block => (block.Text ?? string.Empty).Contains("6-digit pairing code", StringComparison.Ordinal));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previousIdentity;
            PlatformSourceRegistry.PairWirelessAdbAsync = previousPair;
        }
    }

    [AvaloniaFact]
    public void PairingInputsHaveExplicitAccessibleNames()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();

            Assert.Contains(
                inputs,
                static input => AutomationProperties.GetName(input) == "Wireless debugging pairing port");
            Assert.Contains(
                inputs,
                static input => AutomationProperties.GetName(input) == "Wireless debugging pairing code");
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void PairingInputsRequestNumericSensitiveMobileKeyboards()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            using var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            var port = inputs.Single(static input =>
                AutomationProperties.GetName(input) == "Wireless debugging pairing port");
            var code = inputs.Single(static input =>
                AutomationProperties.GetName(input) == "Wireless debugging pairing code");

            Assert.Equal("Pairing port", port.PlaceholderText);
            Assert.Equal("6-digit pairing code", code.PlaceholderText);
            Assert.Equal(Avalonia.Input.TextInput.TextInputContentType.Digits, Avalonia.Input.TextInput.TextInputOptions.GetContentType(port));
            Assert.Equal(Avalonia.Input.TextInput.TextInputContentType.Pin, Avalonia.Input.TextInput.TextInputOptions.GetContentType(code));
            Assert.True(Avalonia.Input.TextInput.TextInputOptions.GetIsSensitive(code));
            Assert.False(Avalonia.Input.TextInput.TextInputOptions.GetShowSuggestions(port));
            Assert.False(Avalonia.Input.TextInput.TextInputOptions.GetShowSuggestions(code));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void SetupExplainsThatAndroidKeepsWirelessDebuggingEnabledAfterCapture()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => true;
            using var dialog = new WirelessAdbSetupDialog();
            var text = string.Join(
                "\n",
                dialog.GetLogicalDescendants().OfType<TextBlock>().Select(static block => block.Text ?? string.Empty));

            Assert.Contains("Android does not switch Wireless debugging off", text, StringComparison.Ordinal);
            Assert.Contains("turn it off in Settings", text, StringComparison.Ordinal);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void ScopeChoiceUsesActionSpecificPrimaryLabels()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            var dialog = new OnDeviceLogAccessDialog();
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            var radios = dialog.GetLogicalDescendants().OfType<RadioButton>().ToArray();

            Assert.Contains(buttons, static button => string.Equals(button.Content?.ToString(), "Set up full-device", StringComparison.Ordinal));

            radios.Single(static radio => string.Equals(radio.Content?.ToString(), "Capture VisualCat only", StringComparison.Ordinal)).IsChecked = true;

            Assert.Contains(buttons, static button => string.Equals(button.Content?.ToString(), "Start VisualCat-only", StringComparison.Ordinal));
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void SavedPairingKeepsNewPairingAsARecoveryAction()
    {
        var previous = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => true;
            var dialog = new WirelessAdbSetupDialog();
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            Assert.True(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Connect saved pairing", StringComparison.Ordinal)).IsVisible);
            Assert.True(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Pair again with a new code", StringComparison.Ordinal)).IsVisible);
            Assert.False(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal)).IsVisible);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previous;
        }
    }

    [AvaloniaFact]
    public void SuccessfulPairingWithFailedDiscoverySwitchesToSavedReconnectUx()
    {
        var previousIdentity = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        var previousPair = PlatformSourceRegistry.PairWirelessAdbAsync;
        try
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            PlatformSourceRegistry.PairWirelessAdbAsync = static (request, cancellationToken) =>
            {
                _ = request;
                _ = cancellationToken;
                return Task.FromResult(new WirelessAdbConnectionResult(
                    Connected: false,
                    PairingSucceeded: true,
                    "Pairing succeeded; reconnect with the saved pairing."));
            };

            var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing port").Text = "37123";
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing code").Text = "123456";

            dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            Assert.True(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Connect saved pairing", StringComparison.Ordinal)).IsVisible);
            Assert.True(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Pair again with a new code", StringComparison.Ordinal)).IsVisible);
            Assert.False(buttons.Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal)).IsVisible);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previousIdentity;
            PlatformSourceRegistry.PairWirelessAdbAsync = previousPair;
        }
    }

    [AvaloniaFact]
    public async Task DismissWaitsForAnInFlightPairingAttemptToReturn()
    {
        var previousIdentity = PlatformSourceRegistry.HasSavedWirelessAdbIdentity;
        var previousPair = PlatformSourceRegistry.PairWirelessAdbAsync;
        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<WirelessAdbConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = static () => false;
            PlatformSourceRegistry.PairWirelessAdbAsync = async (request, cancellationToken) =>
            {
                _ = request;
                using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                return await release.Task;
            };

            var dialog = new WirelessAdbSetupDialog();
            var inputs = dialog.GetLogicalDescendants().OfType<TextBox>().ToArray();
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing port").Text = "37123";
            inputs.Single(static input => AutomationProperties.GetName(input) == "Wireless debugging pairing code").Text = "123456";

            dialog.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => string.Equals(button.Content?.ToString(), "Pair & connect", StringComparison.Ordinal))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            await started.Task;
            dialog.Dismiss();
            await cancelled.Task;

            Assert.False(dialog.Completion.IsCompleted);
            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                static block => string.Equals(block.Text, "Cancelling connection…", StringComparison.Ordinal));

            release.SetResult(new WirelessAdbConnectionResult(false, true, "Pairing returned after cancellation."));
            Assert.False(await dialog.Completion);
        }
        finally
        {
            PlatformSourceRegistry.HasSavedWirelessAdbIdentity = previousIdentity;
            PlatformSourceRegistry.PairWirelessAdbAsync = previousPair;
        }
    }

}
