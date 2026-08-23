using VisualCat.App.Platform;

namespace VisualCat.App.Views;

public sealed partial class MainView
{
    /// <summary>
    /// Starts Live through the strongest Android access path the reader has explicitly enabled,
    /// without pretending READ_LOGS is a normal runtime permission or granting it from the app.
    /// </summary>
    /// <remarks>
    /// If VisualCat already holds READ_LOGS — for example in a developer build or after an
    /// external ADB grant — the established direct on-device source remains the fastest path.
    /// A clean Play install instead offers full-device capture through Android's authenticated
    /// Wireless debugging service. Pairing is normally one-time, but Wireless debugging must be
    /// enabled while that capture is running. The own-app source remains an explicit fallback.
    /// </remarks>
    private async Task StartOnDeviceWithAccessSetupAsync()
    {
        // Preserve the established "Live takes me back to the one capture already running"
        // behavior. Showing a permission/setup choice before this check would make a harmless
        // navigation action look like it needed Android access again.
        if (_viewModel.ActiveLiveCapture is not null)
        {
            await StartOnDeviceAsync();
            return;
        }

        if (!OperatingSystem.IsAndroid() ||
            PlatformSourceRegistry.HasFullDeviceLogPermission?.Invoke() != false)
        {
            await StartOnDeviceAsync();
            return;
        }

        // A platform build without the optional Wireless ADB transport still keeps the proven
        // own-app path. Do not turn a missing optional dependency into an inability to capture.
        if (PlatformSourceRegistry.PairWirelessAdbAsync is null ||
            PlatformSourceRegistry.CreateWirelessAdbSource is null)
        {
            await StartOnDeviceAsync();
            return;
        }

        var choice = await ShowDialogAsync(new OnDeviceLogAccessDialog());
        switch (choice)
        {
            case OnDeviceLogAccessChoice.SetUpFullDevice:
                var reconnectImmediately =
                    PlatformSourceRegistry.HasSavedWirelessAdbIdentity?.Invoke() == true;
                var connected = await ShowDialogAsync(new WirelessAdbSetupDialog(reconnectImmediately));
                if (connected != true)
                {
                    return;
                }

                var wirelessSource = PlatformSourceRegistry.CreateWirelessAdbSource?.Invoke();
                if (wirelessSource is null)
                {
                    ShowNotice(
                        "Wireless debugging connected, but the full-device log source could not be created. " +
                        "Turn Wireless debugging off and on, then try Live again.",
                        NoticeKind.Failure);
                    return;
                }

                ShowNotice(
                    "Wireless debugging connected. Starting full-device Live capture; keep it on until you stop Live.");
                await StartOnDeviceAsync(
                    wirelessSource,
                    usesWirelessAdb: true,
                    accessContextAlreadyShown: true);
                return;

            case OnDeviceLogAccessChoice.CaptureVisualCatOnly:
                await StartOnDeviceAsync(accessContextAlreadyShown: true);
                return;

            case OnDeviceLogAccessChoice.Cancel:
            default:
                return;
        }
    }

    private async Task OpenWirelessDebuggingSettingsFromNoticeAsync()
    {
        if (PlatformSourceRegistry.OpenDeveloperOptionsAsync is not { } open)
        {
            ShowNotice(
                "Open Android Settings > Developer options and turn Wireless debugging off.",
                NoticeKind.Information);
            return;
        }

        try
        {
            await open(CancellationToken.None);
            ShowNotice(
                "VisualCat's connection is closed. Turn Wireless debugging off here if you are finished.",
                NoticeKind.Completion);
        }
        catch
        {
            ShowNotice(
                "VisualCat's connection is closed. Open Android Settings > Developer options and turn Wireless debugging off.",
                NoticeKind.Information);
        }
    }
}
