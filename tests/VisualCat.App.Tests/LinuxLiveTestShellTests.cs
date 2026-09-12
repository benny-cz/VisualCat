using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using VisualCat.App.Platform;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Domain;
using VisualCat.Domain.Sessions;
using VisualCat.Domain.Time;
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
        var adb = Descriptor(SourceKind.Adb, "UTC");
        Assert.Equal(TimeZoneInfo.Local.Id, DisplayZone.IdFor(adb));

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
}
