using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Domain;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
using VisualCat.Infrastructure.Adb;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// The shell half of the Linux live-test remediation
/// (<c>docs/LINUX-LIVE-TEST-REPORT.md</c> §20), at the level each fix can be checked without a
/// display server. One region per finding.
/// </summary>
public sealed class LinuxLiveTestShellTests
{
    // ---------------------------------------------------------------- F-03

    [Fact]
    public void ADesktopBuildCanSayWhereItCameFrom()
    {
        // The command was gated on the host being able to answer this, and no desktop head ever
        // assigned it — so "Check for updates…" existed nowhere outside Android while
        // SUPPORT.md described it to desktop readers (finding F-03).
        var origin = AppInstallOrigin.PortableArchive;
        var prompt = AppUpdatePolicy.Decide(
            new AppUpdateStatus(AppUpdateState.Unsupported),
            ReleaseChannel.Stable,
            liveCaptureRunning: false,
            new AppUpdateMemory(),
            DateTimeOffset.UtcNow,
            manual: true,
            origin);

        Assert.NotNull(prompt);
        Assert.Equal(AppUpdatePromptAction.OpenReleases, prompt!.Action);

        // And it says so in desktop words: naming Google Play to someone who unpacked a tar.gz
        // is the same class of defect as phone vocabulary in a desktop dialog (F-14).
        Assert.DoesNotContain("Google Play", prompt.Message, StringComparison.Ordinal);
        Assert.Contains("archive", prompt.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- F-08

    [AvaloniaFact]
    public void ADialogsConfirmingActionIsDrawnAsThePrimaryOne()
    {
        // A live, clickable Import was lighter than the Cancel beside it, so the review read as
        // blocked (finding F-08). The shell's own buttons have used this palette all along.
        var button = SheetForm.PrimaryAction(new Button { Content = "Import" });
        var dark = button.ActualThemeVariant != Avalonia.Styling.ThemeVariant.Light;
        Assert.Equal(WorkspacePalette.PrimaryActionFill(dark), Assert.IsType<SolidColorBrush>(button.Background).Color);
        Assert.Equal(WorkspacePalette.PrimaryActionText(dark), Assert.IsType<SolidColorBrush>(button.Foreground).Color);
        Assert.NotEqual(button.Background, button.BorderBrush);
    }

    // ---------------------------------------------------------------- F-13

    [AvaloniaFact]
    public void TheApplicationIntroducesItselfByName()
    {
        // The accessibility bus listed it as "Avalonia Application" — the first thing a
        // screen-reader user heard about this product, and it did not say VisualCat (F-13).
        Assert.Equal("VisualCat", Avalonia.Application.Current?.Name);
    }

    // ---------------------------------------------------------------- F-14

    [AvaloniaFact]
    public void TheDesktopSettingsDialogUsesDesktopWords()
    {
        var dialog = new AppearanceDialog(new ApplicationSettings());
        var text = string.Join(
            " ",
            dialog.GetLogicalDescendants().OfType<TextBlock>().Select(static block => block.Text ?? string.Empty));
        var checkBoxes = string.Join(
            " ",
            dialog.GetLogicalDescendants().OfType<CheckBox>().Select(static box => box.Content as string ?? string.Empty));

        // Phone vocabulary in a desktop dialog is the same defect as an Android-only control
        // in one: it describes something the reader does not have (finding F-14).
        Assert.DoesNotContain("the device's own text size", text, StringComparison.Ordinal);
        Assert.DoesNotContain("device pixels", checkBoxes, StringComparison.Ordinal);
        Assert.DoesNotContain("In Split mode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Normalized CSV encoding", text, StringComparison.Ordinal);
        Assert.Contains("CSV encoding", text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheSettingsDialogOffersTheLineEndingChoiceTheExportReviewHas()
    {
        var dialog = new AppearanceDialog(new ApplicationSettings());
        var combo = dialog.GetLogicalDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(static box => AutomationProperties.GetName(box) == "CSV line endings");
        Assert.NotNull(combo);
    }

    // ---------------------------------------------------------------- F-16

    [Fact]
    public void TheExportReviewAndThePlotPresentInTheSameZone()
    {
        // An ADB capture negotiates UTC while the plot reads in the host zone, so the review
        // stated the same instants two hours from the plot it was opened from (finding F-16).
        // Through the product's own host-zone resolution: on macOS the runtime calls UTC
        // "Universal" while /etc/localtime calls it "UTC", and the two spellings are one zone.
        var adb = Descriptor(SourceKind.Adb, "UTC");
        Assert.Equal(TimeZoneResolution.HostZoneId(), DisplayZone.IdFor(adb));

        // An imported file's naive timestamps mean whatever its policy says, and that does not
        // change.
        var file = Descriptor(SourceKind.File, "Europe/Prague");
        Assert.Equal("Europe/Prague", DisplayZone.IdFor(file));
        Assert.Equal(TimeZoneInfo.Utc, DisplayZone.Resolve("Definitely/Not_A_Zone"));
    }

    private static SessionDescriptor Descriptor(SourceKind kind, string zoneId) => new(
        Guid.NewGuid(),
        "session",
        kind,
        "source",
        DateTimeOffset.UtcNow,
        SessionState.Ready,
        1,
        VisualCat.Domain.Entries.LogcatFormat.ThreadTime,
        1,
        new TimestampPolicy(null, zoneId, DateTimeOffset.UtcNow),
        new TemplateSettings(),
        new SessionCounters(),
        new DefectCounters(),
        null,
        null,
        true,
        false);

    // ---------------------------------------------------------------- U-07

    [AvaloniaFact]
    public void TheMainWindowStartsWithFocusOnItsFirstCommand()
    {
        // A window with no focused control is one a screen reader reads out whole: Orca
        // announced the frame and then the entire workspace — notice, strapline, counts,
        // template list and entry list — as a single utterance, because there was nothing
        // inside holding focus to announce instead (U-07).
        var window = new MainWindow(new MainView());
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var focused = window.FocusManager?.GetFocusedElement() as Visual;
            Assert.NotNull(focused);
            Assert.Same(window, focused!.FindAncestorOfType<Window>(includeSelf: true));

            // And it must not look different to someone who uses a pointer: the focus adorner is
            // driven by :focus-visible, which keyboard navigation raises and a programmatic focus
            // does not.
            Assert.False(
                ((IPseudoClasses)((StyledElement)focused).Classes).Contains(":focus-visible"),
                "starting focus must not draw a focus ring for a reader who never touched the keyboard");
        }
        finally
        {
            window.Close();
        }
    }

    // ------------------------------------------------- A-16, second half (F-34)

    [AvaloniaFact]
    public async Task AnEmptyDeviceListDoesNotBlameTheDeviceForTheComputerSPermissions()
    {
        // ADB omits a device whose USB node the account may not read, so "connect a device and
        // enable USB debugging" is told to a user whose phone is already connected with
        // debugging already on — the real fault being a udev rule granting a group they are not
        // in. Measured live: mode 0664 reports "no permissions", mode 0660 reports nothing.
        using var dialog = new AdbCaptureDialog(new EmptyDeviceClient());
        dialog.Show();
        try
        {
            var status = string.Empty;
            for (var attempt = 0; attempt < 200 && status.Length == 0; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                status = dialog.GetVisualDescendants().OfType<TextBlock>()
                    .Select(static block => block.Text ?? string.Empty)
                    .FirstOrDefault(static text => text.StartsWith("No devices detected", StringComparison.Ordinal))
                    ?? string.Empty;
                if (status.Length == 0)
                {
                    await Task.Delay(10);
                }
            }

            Assert.StartsWith("No devices detected.", status, StringComparison.Ordinal);
            var explanation = UsbDeviceAccess.MissingDeviceExplanation();
            if (explanation is null)
            {
                Assert.Equal(
                    "No devices detected. Connect a device and enable USB debugging, then refresh.",
                    status);
            }
            else
            {
                Assert.Contains(explanation, status, StringComparison.Ordinal);
                Assert.Contains(UsbDeviceAccess.ShortRemedy, status, StringComparison.Ordinal);
            }
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task EscapeCancelsTheLiveCaptureDialogLikeEveryOtherDialog()
    {
        // Every dialog in the shell gets Escape from the dialog host in MainView.Overlays, on
        // the tunnel and with an open dropdown as the only exception (F-12). This one is shown
        // with ShowDialog directly rather than through that host, so it was the single dialog
        // where Escape did nothing — found under kwin, true on every window manager.
        using var dialog = new AdbCaptureDialog(new EmptyDeviceClient());
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            for (var attempt = 0; attempt < 100 && !closed; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Assert.True(closed);
        }
        finally
        {
            if (!closed)
            {
                dialog.Close();
            }
        }
    }

    private sealed class EmptyDeviceClient : IAdbClient
    {
        public string ExecutablePath => "fake-adb";

        public Task<IReadOnlyList<AdbDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AdbDevice>>([]);
        }

        public Task<AdbCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAdbProcess StartProcess(IReadOnlyList<string> arguments) =>
            throw new NotSupportedException();
    }
}
