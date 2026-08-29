using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualCat.App;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Application.Ports;
using VisualCat.Domain;
using VisualCat.Infrastructure.Configuration;
using VisualCat.Infrastructure.Files;

namespace VisualCat.App.Tests;

/// <summary>
/// The fixes from the Android companion's second UX/UI pass (docs/ANDROID-UX-AUDIT-2.md), at
/// the level each one can be checked without a device.
/// </summary>
/// <remarks>
/// The phone composition is selected by <see cref="SessionWorkspaceView.PhoneCompositionOverride"/>
/// where a test needs it, because everything both audits are about lives in that branch and a
/// headless run on a desktop otherwise takes the desktop one.
/// </remarks>
public sealed class AndroidAuditFix2Tests
{
    // ---------------------------------------------------------------- A1 ---

    /// <summary>
    /// A1c — the entry row's metadata line resolves its foreground from the theme rather than
    /// from whatever variant happened to be in force when the list was built.
    /// </summary>
    [AvaloniaFact]
    public void TheMetadataLineFollowsTheThemeIntoLight()
    {
        var window = new Window { Width = 400, Height = 300 };
        window.Show();

        window.RequestedThemeVariant = ThemeVariant.Dark;
        var dark = Resolve(window, VisualCat.App.Theme.ProductTheme.TextMutedKey);
        window.RequestedThemeVariant = ThemeVariant.Light;
        var light = Resolve(window, VisualCat.App.Theme.ProductTheme.TextMutedKey);

        Assert.Equal(WorkspacePalette.TextMuted(dark: true), dark);
        Assert.Equal(WorkspacePalette.TextMuted(dark: false), light);
        Assert.NotEqual(dark, light);
    }

    /// <summary>
    /// A1c — and the light value it resolves to is one a reader can actually read: the row's
    /// secondary line measured 2.17:1 against the light list surface.
    /// </summary>
    [Fact]
    public void TheLightMetadataLineClearsTheContrastFloor()
    {
        var contrast = Contrast(
            WorkspacePalette.TextMuted(dark: false),
            WorkspacePalette.SurfaceRaised(dark: false));

        Assert.True(contrast >= 4.5, $"metadata on a light list surface measured {contrast:0.00}:1");
    }

    /// <summary>A1a — the command bar has a light appearance, not one fixed dark band.</summary>
    [Fact]
    public void TheCommandBarHasBothVariants()
    {
        Assert.NotEqual(WorkspacePalette.ShellTop(dark: true), WorkspacePalette.ShellTop(dark: false));
        Assert.NotEqual(WorkspacePalette.ShellBottom(dark: true), WorkspacePalette.ShellBottom(dark: false));
        Assert.NotEqual(WorkspacePalette.SystemBar(dark: true), WorkspacePalette.SystemBar(dark: false));

        // And its wordmark reads against it in both.
        Assert.True(Contrast(WorkspacePalette.ShellText(dark: false), WorkspacePalette.ShellTop(dark: false)) >= 4.5);
        Assert.True(Contrast(WorkspacePalette.ShellText(dark: true), WorkspacePalette.ShellTop(dark: true)) >= 4.5);
        Assert.True(Contrast(WorkspacePalette.ShellTextMuted(dark: false), WorkspacePalette.ShellTop(dark: false)) >= 4.5);
        Assert.True(Contrast(WorkspacePalette.ShellTextMuted(dark: true), WorkspacePalette.ShellTop(dark: true)) >= 4.5);
    }

    /// <summary>A1b — a variant change repaints the shell rather than only four of its parts.</summary>
    [AvaloniaFact]
    public void SwitchingToLightRepaintsTheCommandBar()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.Show();
        window.UpdateLayout();
        var darkBar = CommandBarTop(view);

        window.RequestedThemeVariant = ThemeVariant.Light;
        window.UpdateLayout();

        Assert.Equal(WorkspacePalette.ShellTop(dark: true), darkBar);
        Assert.Equal(WorkspacePalette.ShellTop(dark: false), CommandBarTop(view));
    }

    // ---------------------------------------------------------------- A3 ---

    /// <summary>
    /// A3 — a scrolling surface lays its content out beside the scrollbar, not under it.
    /// </summary>
    [AvaloniaFact]
    public void ScrolledContentDoesNotReachUnderTheScrollBar()
    {
        var body = new StackPanel();
        for (var index = 0; index < 60; index++)
        {
            body.Children.Add(new Button
            {
                Content = $"row {index}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }

        var scroller = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var window = new Window { Content = scroller, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();

        var bar = scroller.GetVisualDescendants()
            .OfType<ScrollBar>()
            .First(candidate => candidate.Orientation == Orientation.Vertical);
        var row = (Control)body.Children[0];
        var barLeft = bar.TranslatePoint(new Point(0, 0), scroller)!.Value.X;
        var rowRight = row.TranslatePoint(new Point(row.Bounds.Width, 0), scroller)!.Value.X;

        Assert.True(rowRight <= barLeft + 0.5, $"content ends at {rowRight}, the bar starts at {barLeft}");
    }

    /// <summary>
    /// A3 — and the lane reserved for the bar is at least as wide as the bar Fluent draws, so
    /// a theme update that widened it would fail here rather than on a device.
    /// </summary>
    [AvaloniaFact]
    public void TheScrollBarLaneIsWideEnoughForTheBar()
    {
        var body = new StackPanel();
        for (var index = 0; index < 60; index++)
        {
            body.Children.Add(new TextBlock { Text = $"row {index}" });
        }

        var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Content = scroller, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();

        var bar = scroller.GetVisualDescendants()
            .OfType<ScrollBar>()
            .First(candidate => candidate.Orientation == Orientation.Vertical);

        Assert.True(
            bar.Bounds.Width <= VisualCat.App.Theme.ProductTheme.ScrollBarLane,
            $"the bar measures {bar.Bounds.Width} against a reserved lane of {VisualCat.App.Theme.ProductTheme.ScrollBarLane}");
    }

    // ---------------------------------------------------------------- A4 ---

    /// <summary>
    /// A4 — a settings choice with a handful of options is presented in place instead of in a
    /// popup that has to be positioned over a scrolling sheet.
    /// </summary>
    [AvaloniaFact]
    public void AChoiceSelectorPicksWithoutAPopup()
    {
        var choices = SettingChoice.Of(("A", "First"), ("B", "Second"), ("C", "Third"));
        var selector = new ChoiceSelector("Test choice", choices, "B");
        var window = new Window { Content = selector, Width = 360, Height = 200 };
        window.Show();
        window.UpdateLayout();

        var segments = selector.GetVisualDescendants().OfType<ToggleButton>().ToArray();
        Assert.Equal(3, segments.Length);
        Assert.Equal("B", selector.Value);
        Assert.Equal("Test choice: Second", AutomationProperties.GetName(segments[1]));

        segments[2].IsChecked = true;

        Assert.Equal("C", selector.Value);
        Assert.False(segments[1].IsChecked);

        // A choice cannot be turned off, only replaced.
        segments[2].IsChecked = false;
        Assert.Equal("C", selector.Value);
        Assert.True(segments[2].IsChecked);
    }

    // ---------------------------------------------------------------- B1 ---

    /// <summary>B1 — a stored session is announced as its two visible lines, not as a record.</summary>
    [Fact]
    public void AStoredSessionIsSpokenAsItsOwnRow()
    {
        var session = new TemporarySessionInfo(
            Path.Combine(Path.GetTempPath(), "20260818-185535-On-device logcat 18-55-35-edd2cb30f9f144c5b36304f05ad55b13.vcat"),
            new DateTimeOffset(2026, 8, 18, 18, 57, 5, TimeSpan.Zero),
            35_460,
            Finalized: true);

        var spoken = SheetForm.DescribeSessionRow(session);

        Assert.StartsWith("On-device logcat 18-55-35,", spoken, StringComparison.Ordinal);
        Assert.Contains("complete", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporarySessionInfo", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain("edd2cb30", spoken, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), spoken, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- B5 ---

    /// <summary>B5 — the device's text size is the baseline and the app's setting multiplies it.</summary>
    [Fact]
    public void TheAppMultipliesTheDeviceTextScale()
    {
        var platform = TextScale.Platform;
        var user = TextScale.User;
        try
        {
            TextScale.Platform = 1.3;
            TextScale.User = 1;
            Assert.Equal(13d, TextScale.Of(10), 3);

            TextScale.User = 1.5;
            Assert.Equal(19.5, TextScale.Of(10), 3);

            // Beyond what the panes can compose, the product stops rather than laying out
            // controls that no longer fit beside each other.
            TextScale.Platform = 2;
            TextScale.User = 2;
            Assert.Equal(22d, TextScale.Of(10), 3);
        }
        finally
        {
            TextScale.Platform = platform;
            TextScale.User = user;
        }
    }

    // ---------------------------------------------------------------- C2 ---

    /// <summary>C2 — two captures started a second apart are not called the same thing.</summary>
    [Fact]
    public void ACaptureIsNamedForWhenItStarted()
    {
        var name = SourceMetadata.NameCaptureStartedNow("On-device logcat");

        Assert.StartsWith("On-device logcat ", name, StringComparison.Ordinal);

        // h/m rather than hyphens: "20-09-12" is what a date looks like, and the first thing
        // that name said was "12 September 2020" (finding F-16).
        Assert.Matches(@"^On-device logcat \d{2}h\d{2}m\d{2}$", name);

        // It survives being used as a filename stem, which is where it ends up.
        Assert.Equal(name, Path.GetFileNameWithoutExtension(name));
        Assert.DoesNotContain(name, Path.GetInvalidFileNameChars().Select(c => c.ToString()));
    }

    // ---------------------------------------------------------------- C3 ---

    /// <summary>C3 — Open cannot be tapped before there is a session for it to open.</summary>
    [AvaloniaFact]
    public void RecentSessionsCannotOpenNothing()
    {
        var sessions = new[]
        {
            new TemporarySessionInfo(
                Path.Combine(Path.GetTempPath(), "20260818-185535-first-edd2cb30f9f144c5b36304f05ad55b13.vcat"),
                DateTimeOffset.UtcNow,
                1024,
                Finalized: true),
        };
        var dialog = new RecentSessionsDialog(sessions);
        var window = new Window { Content = dialog, Width = 600, Height = 420 };
        window.Show();
        window.UpdateLayout();

        var open = dialog.GetVisualDescendants()
            .OfType<Button>()
            .First(button => Equals(button.Content, "Open"));
        Assert.False(open.IsEnabled);

        var list = dialog.GetVisualDescendants().OfType<ListBox>().First();
        list.SelectedIndex = 0;

        Assert.True(open.IsEnabled);
    }

    // ---------------------------------------------------------------- C5 ---

    /// <summary>
    /// C5 — the workspace mode is the reader's choice, so it survives the activity being
    /// recreated by a text-size or display-size change.
    /// </summary>
    [Fact]
    public void TheWorkspaceModeCanBeRestored()
    {
        var state = new MobileWorkspaceState();
        Assert.True(state.Restore("Plot"));
        Assert.Equal(MobileWorkspaceDisplayMode.Plot, state.DisplayMode);
        Assert.Equal("Plot", state.Persisted);

        // Restoring counts as initialisation: a later size class cannot overwrite the choice.
        state.ApplyLayout(MobileWorkspaceLayout.ForSize(412, 900));
        Assert.Equal(MobileWorkspaceDisplayMode.Plot, state.DisplayMode);

        Assert.False(new MobileWorkspaceState().Restore(null));
        Assert.False(new MobileWorkspaceState().Restore("not-a-mode"));
    }

    /// <summary>C5 — and there is somewhere to keep it between processes.</summary>
    [Fact]
    public void TheWorkspaceModeHasAPlaceInSettings()
    {
        var settings = new ApplicationSettings() with { WorkspaceDisplayMode = "Details" };

        Assert.Equal("Details", settings.WorkspaceDisplayMode);
        Assert.Null(new ApplicationSettings().WorkspaceDisplayMode);
    }

    // ---------------------------------------------------------------- E1 ---

    /// <summary>
    /// E1 — numbers and dates are formatted in the culture the interface is written in,
    /// rather than in the device's, which produced two conventions in one line.
    /// </summary>
    [Fact]
    public void EveryNumberIsFormattedInTheInterfacesOwnCulture()
    {
        var culture = DisplayCulture.Current;

        Assert.Equal("59,640", 59_640.ToString("N0", culture));
        Assert.Equal("34.63", 34.63.ToString("0.##", culture));

        // ISO dates, because that is already what the timeline axis and the entry rows draw.
        var instant = new DateTime(2026, 8, 18, 20, 57, 5, DateTimeKind.Utc);
        Assert.Equal("2026-08-18 20:57", instant.ToString("g", culture));
    }

    /// <summary>E1 — and the compact metrics follow it too.</summary>
    [Fact]
    public void CompactCountsUseTheInterfacesOwnSeparator()
    {
        Assert.Equal("12.3k", SessionWorkspaceView.FormatTemplateCount(12_345));
    }

    // ---------------------------------------------------------------- E2 ---

    /// <summary>
    /// E2/E3 — every list describes a stored session with the same words, and the words say
    /// what they mean rather than assuming the reader knows "ready" from "partial".
    /// </summary>
    [Fact]
    public void EveryListDescribesAStoredSessionTheSameWay()
    {
        var finalized = new TemporarySessionInfo("/tmp/a.vcat", DateTimeOffset.UtcNow, 1024, Finalized: true);
        var partial = finalized with { Finalized = false };

        Assert.EndsWith("complete", SheetForm.DescribeSessionState(finalized), StringComparison.Ordinal);
        Assert.DoesNotContain("ready", SheetForm.DescribeSessionState(finalized), StringComparison.Ordinal);
        Assert.DoesNotContain("partial", SheetForm.DescribeSessionState(partial), StringComparison.Ordinal);

        // Audit 3's E1: "still being written" said a process was writing to the file at that
        // moment, and every capture that ends other than through Stop capture wore it
        // permanently — a session from the previous day, untouched for 26 hours, described
        // itself as one being recorded. A past state gets a past tense.
        Assert.EndsWith("interrupted", SheetForm.DescribeSessionState(partial), StringComparison.Ordinal);
        Assert.DoesNotContain("still being written", SheetForm.DescribeSessionState(partial), StringComparison.Ordinal);

        // And the present tense is kept for the one session that is actually present tense.
        Assert.EndsWith(
            "capture in progress",
            SheetForm.DescribeSessionState(partial, capturingNow: true),
            StringComparison.Ordinal);

        // And the vocabulary is explained where a reader meets it.
        Assert.Contains("interrupted", SheetForm.SessionStateHelp, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ A2 / C7 ---

    /// <summary>
    /// A2/D6 — Filters, the mode selector and Fit share one touch row instead of taking two,
    /// and C7 — Fit leaves with the plot it acts on.
    /// </summary>
    [AvaloniaFact]
    public async Task TheModeRowFitsOnOneLineAndFitFollowsThePlot()
    {
        await UsingPhoneWorkspace(360, 780, static (view, window) =>
        {
            var plot = Named<Button>(view, "Show plot workspace");
            var split = Named<Button>(view, "Show split workspace");
            var details = Named<Button>(view, "Show details workspace");
            var filters = Named<Button>(view, "Open search and timeline filters");
            var fit = Named<Button>(view, "Fit the complete session");

            var row = Top(filters);
            Assert.Equal(row, Top(plot), 1);
            Assert.Equal(row, Top(split), 1);
            Assert.Equal(row, Top(details), 1);
            Assert.Equal(row, Top(fit), 1);
            Assert.True(fit.IsEnabled);
            Assert.Equal(1, fit.Opacity);

            var detailsEdges = (details.Bounds.Left, details.Bounds.Right);
            details.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            // Details hides the plot, so the control that moves the plot goes with it rather
            // than staying enabled over a surface nobody can see.
            Assert.False(fit.IsEnabled);
            Assert.Equal(0, fit.Opacity);

            // But it keeps its slot: audit 3's C4 found the three mode segments spreading into
            // the space Fit left, so a second tap where Details had just been hit Split.
            Assert.Equal(detailsEdges.Left, details.Bounds.Left, 1);
            Assert.Equal(detailsEdges.Right, details.Bounds.Right, 1);

            plot.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.True(fit.IsEnabled);
            Assert.Equal(1, fit.Opacity);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A2 — the entries list clears its floor of four rows in Split, taking the difference
    /// from the plot rather than from itself.
    /// </summary>
    [AvaloniaFact]
    public async Task TheEntriesListClearsItsFloorInSplit()
    {
        await UsingPhoneWorkspace(360, 780, static (view, window) =>
        {
            var entries = Named<ListBox>(view, "Filtered log entries");
            var rows = entries.Bounds.Height / 64;

            Assert.True(rows >= 4, $"the entries list was {entries.Bounds.Height:0} px, {rows:0.0} rows");
            return Task.CompletedTask;
        });
    }

    /// <summary>A2 — and it keeps a floor even where the whole viewport is short.</summary>
    [AvaloniaFact]
    public async Task TheEntriesListKeepsAFloorOnAShortViewport()
    {
        await UsingPhoneWorkspace(360, 560, static (view, window) =>
        {
            var entries = Named<ListBox>(view, "Filtered log entries");

            Assert.True(entries.Bounds.Height >= 3 * 64, $"the entries list was {entries.Bounds.Height:0} px");
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// F-09 — even below the preferred row floor, a realized row belongs to the list and can
    /// never paint into the status band beneath it.
    /// </summary>
    [AvaloniaFact]
    public async Task TheEntryListOwnsItsCompactHeightClipBoundary()
    {
        await UsingPhoneWorkspace(800, 360, static (view, window) =>
        {
            var entries = Named<ListBox>(view, "Filtered log entries");
            Assert.True(entries.ClipToBounds);
            Assert.IsType<Grid>(entries.Parent);
            Assert.True(((Grid)entries.Parent!).ClipToBounds);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// F-09 — the real workspace strip can share the shell row and returns to this workspace
    /// symmetrically; there is no duplicate set of controls whose states can drift.
    /// </summary>
    [AvaloniaFact]
    public async Task CompactCommandsHaveOneOwnerWhenMovedIntoTheShell()
    {
        await UsingPhoneWorkspace(800, 360, static (view, window) =>
        {
            var host = new Grid();
            view.HostCompactCommands(host);
            Assert.Single(host.Children);
            Assert.Same(host, host.Children[0].Parent);

            view.HostCompactCommands(null);
            Assert.Empty(host.Children);
            Assert.NotNull(Named<Button>(view, "Open search and timeline filters"));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// F-11 — the visible hint is constrained by the field's grid cell and removed from the
    /// automation tree; the TextBox itself owns the stable, complete accessible name.
    /// </summary>
    [AvaloniaFact]
    public async Task TheCompactSearchHintCannotOverflowItsFieldInAutomation()
    {
        await UsingPhoneWorkspace(800, 360, static (view, window) =>
        {
            var filters = Named<Button>(view, "Open search and timeline filters");
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            var field = Named<TextBox>(view, "Search message text or regular expression");
            Assert.Equal(string.Empty, field.PlaceholderText);
            Assert.StartsWith(
                "Search message text or regular expression.",
                AutomationProperties.GetHelpText(field),
                StringComparison.Ordinal);
            var hint = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Text is "Search or regex…");
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(hint));
            Assert.False(AutomationProperties.GetIsControlElementOverride(hint));
            var parent = (Visual)hint.Parent!;
            Assert.IsType<Grid>(parent);
            Assert.True(((Grid)parent).ClipToBounds);
            Assert.Equal(HorizontalAlignment.Stretch, hint.HorizontalAlignment);
            Assert.Equal(TextTrimming.CharacterEllipsis, hint.TextTrimming);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// F-11 follow-up — a short portrait viewport still needs a real editor. A notice,
    /// split-screen, or an IME transition can make height select the compact composition
    /// without making the phone wide; option controls must not consume the field's column.
    /// </summary>
    [AvaloniaFact]
    public async Task AShortPortraitCompactDrawerKeepsAUsableQueryField()
    {
        await UsingPhoneWorkspace(360, 480, static (view, window) =>
        {
            var filters = Named<Button>(view, "Open search and timeline filters");
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            var field = Named<TextBox>(view, "Search message text or regular expression");
            Assert.True(
                field.Bounds.Width >= 96,
                $"the short-portrait query field is only {field.Bounds.Width:0.#} dp wide");
            Assert.True(field.IsVisible);
            Assert.True(field.IsEffectivelyEnabled);
            return Task.CompletedTask;
        });
    }

    /// <summary>F-04 — Done must not hide the field whose regex it just refused.</summary>
    [AvaloniaFact]
    public async Task DoneKeepsAnInvalidRegexVisibleForRepair()
    {
        await UsingPhoneWorkspace(360, 480, static async (view, window) =>
        {
            var filters = Named<Button>(view, "Open search and timeline filters");
            filters.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Named<TextBox>(view, "Search message text or regular expression").Text = "(unclosed";
            view.GetVisualDescendants()
                .OfType<CheckBox>()
                .Single(box => Equals(box.Content, "Regex"))
                .IsChecked = true;

            var done = view.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Done"));
            done.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal("Close filters", AutomationProperties.GetName(filters));
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                block => block.IsVisible &&
                         block.Text?.StartsWith("Not a valid regular expression:", StringComparison.Ordinal) == true);
        });
    }

    /// <summary>
    /// A2 — Load next 500 is under the list it extends, and is out of the layout entirely
    /// while there is nothing further to load.
    /// </summary>
    [AvaloniaFact]
    public async Task LoadingMoreRowsIsAFooter()
    {
        await UsingPhoneWorkspace(360, 780, static (view, window) =>
        {
            var entries = Named<ListBox>(view, "Filtered log entries");
            var footer = Named<Border>(view, "End of the loaded rows");

            // Out of the layout entirely while there is nothing further to load, and below
            // the list when there is — asserted on the grid rather than on arranged bounds,
            // because a hidden control has none.
            Assert.False(footer.IsVisible);
            Assert.Same(entries.Parent, footer.Parent);
            Assert.True(
                Grid.GetRow(footer) > Grid.GetRow(entries),
                "the footer must sit below the list it extends, not above it");
            return Task.CompletedTask;
        });
    }

    // ------------------------------------------------------------- B2 -------

    /// <summary>
    /// B2 — the status row's accessible description tracks the status instead of keeping
    /// whatever it was first given.
    /// </summary>
    [AvaloniaFact]
    public async Task TheStatusDescriptionFollowsTheStatus()
    {
        await UsingPhoneWorkspace(360, 780, static (view, window, tab) =>
        {
            tab.ReportActivity(SessionActivity.Capturing, "Capturing · 23 lines received · 1/s · On-device logcat");
            Dispatcher.UIThread.RunJobs();

            var status = view.GetVisualDescendants()
                .OfType<TextBlock>()
                .First(block => AutomationProperties.GetName(block) == tab.Status);
            Assert.Equal(tab.Status, AutomationProperties.GetHelpText(status));

            // The platform node kept whatever description it read first, so a finished
            // session went on being announced as "Starting capture" (audit 2, B2).
            tab.ReportActivity(SessionActivity.Ready, "Ready · 59 640 entries");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(tab.Status, AutomationProperties.GetName(status));
            Assert.Equal(tab.Status, AutomationProperties.GetHelpText(status));
            return Task.CompletedTask;
        });
    }

    // ------------------------------------------------------------- B4 -------

    /// <summary>
    /// B4 — while a sheet is up, the workspace behind it is out of the accessibility tree,
    /// and it comes back when the last one is gone.
    /// </summary>
    [AvaloniaFact]
    public void ASheetTakesTheWorkspaceOutOfTheAccessibilityTree()
    {
        var view = new MainView();
        var window = new Window { Content = view, Width = 420, Height = 780 };
        window.Show();
        window.UpdateLayout();

        var host = view.GetVisualDescendants().OfType<DockPanel>().First();
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(host));

        view.OpenCommandSheet();
        window.UpdateLayout();

        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(host));
        Assert.Equal(IsOffscreenBehavior.Offscreen, AutomationProperties.GetIsOffscreenBehavior(host));

        // The band is not disabled: a workspace greyed out behind a bottom sheet would be a
        // worse lie than the one being fixed.
        Assert.True(host.IsEnabled);
    }

    // ------------------------------------------------------------- C4 -------

    /// <summary>
    /// C4 — an action whose only other evidence is off screen reports itself, through a route
    /// the workspace has rather than by reaching into the shell.
    /// </summary>
    [AvaloniaFact]
    public async Task CopyingWithNothingSelectedSaysSo()
    {
        await UsingPhoneWorkspace(360, 780, static async (view, window) =>
        {
            var raised = new List<(string Message, bool Failure)>();
            view.NoticeRaised += (message, failure) => raised.Add((message, failure));

            var copy = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => Equals(button.Content, "Copy raw"));
            copy.IsEnabled = true;
            copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();

            var notice = Assert.Single(raised);
            Assert.True(notice.Failure);
            Assert.Contains("Copy raw", notice.Message, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------- D5 -------

    /// <summary>D5 — the Entry tab's empty state keeps its button inside its own card.</summary>
    [AvaloniaFact]
    public async Task TheEmptyEntryCardContainsItsButton()
    {
        await UsingPhoneWorkspace(360, 780, static (view, window) =>
        {
            var tabs = Named<TabControl>(view, "Session detail views");
            tabs.SelectedIndex = 2;
            window.UpdateLayout();

            var card = Named<Border>(view, "No entry selected");
            var choose = Named<Button>(view, "Open Entries to choose a row");
            if (!card.IsVisible || card.Bounds.Height <= 0)
            {
                return Task.CompletedTask;
            }

            var cardTop = card.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            var buttonBottom = choose.TranslatePoint(new Point(0, choose.Bounds.Height), view)!.Value.Y;

            Assert.True(
                buttonBottom <= cardTop + card.Bounds.Height + 0.5,
                $"the button ends at {buttonBottom:0} and the card at {cardTop + card.Bounds.Height:0}");
            return Task.CompletedTask;
        });
    }

    // ------------------------------------------------------------- D3/D7 ---

    /// <summary>
    /// D3 — the cache sheet's decision row keeps Cancel and Save on one line, and D7 — the
    /// policy fields recede while the switch that governs them is off.
    /// </summary>
    [AvaloniaFact]
    public void TheCacheSheetDecidesOnOneLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dialog = new SessionCacheDialog(root, new ApplicationSettings());
        var window = new Window { Content = dialog, Width = 380, Height = 620 };
        window.Show();
        window.UpdateLayout();

        var cancel = dialog.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Cancel"));
        var save = dialog.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Save policy"));
        Assert.Equal(Top(cancel), Top(save), 1);

        var days = Named<NumericUpDown>(dialog, "Maximum age (days)");
        var size = Named<NumericUpDown>(dialog, "Maximum total size (GiB, 0 = unlimited)");
        var enabled = dialog.GetVisualDescendants()
            .OfType<CheckBox>()
            .First(box => Equals(box.Content, "Enable automatic temporary-session cleanup"));

        Assert.False(enabled.IsChecked);
        Assert.False(days.IsEnabled);
        Assert.False(size.IsEnabled);

        enabled.IsChecked = true;
        Assert.True(days.IsEnabled);
        Assert.True(size.IsEnabled);
    }

    /// <summary>F-19 — recovery offers all three explicit dispositions with safe copy.</summary>
    [AvaloniaFact]
    public void ARecoveredCaptureOffersKeepExportAndConfirmedDelete()
    {
        var dialog = new RecoveredSessionDialog("On-device logcat 01h29m04", 947, canDelete: true);
        var window = new Window { Content = dialog, Width = 380, Height = 620 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            var keep = Assert.Single(buttons, button => AutomationProperties.GetName(button) == "Keep this recovered capture");
            var export = Assert.Single(buttons, button => AutomationProperties.GetName(button) == "Export recovered data");
            var delete = Assert.Single(buttons, button => AutomationProperties.GetName(button) == "Delete this recovered capture");
            Assert.True(keep.IsEnabled);
            Assert.True(export.IsEnabled);
            Assert.True(delete.IsEnabled);
            Assert.Contains("asks for confirmation", AutomationProperties.GetHelpText(delete), StringComparison.Ordinal);
            Assert.Contains(
                dialog.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text?.Contains(Counted.Entries(947), StringComparison.Ordinal) == true);

            var external = new RecoveredSessionDialog("External", 3, canDelete: false);
            var externalWindow = new Window { Content = external, Width = 380, Height = 620 };
            externalWindow.Show();
            externalWindow.UpdateLayout();
            try
            {
                Assert.False(external.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Delete this recovered capture")
                    .IsEnabled);
            }
            finally
            {
                externalWindow.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task AnInterruptedOpenSessionNamesItsStateOnTheTab()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("Recovered", root);
        tab.ReportActivity(SessionActivity.RecoverablePartial, "Interrupted");
        var button = new Button();

        MainView.ApplySessionChipSemantics(tab, button);

        Assert.Equal("Show interrupted session Recovered", AutomationProperties.GetName(button));
        Assert.Contains("ended before", AutomationProperties.GetHelpText(button), StringComparison.Ordinal);
        Directory.Delete(root, recursive: true);
    }

    /// <summary>B3 — a numeric field's spin buttons say which is which.</summary>
    [AvaloniaFact]
    public void NumericSpinnersHaveNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dialog = new SessionCacheDialog(root, new ApplicationSettings());
        var window = new Window { Content = dialog, Width = 380, Height = 620 };
        window.Show();
        window.UpdateLayout();

        var days = Named<NumericUpDown>(dialog, "Maximum age (days)");
        var spinners = days.GetVisualDescendants()
            .OfType<Button>()
            .Select(AutomationProperties.GetName)
            .Where(static name => !string.IsNullOrEmpty(name))
            .ToArray();

        Assert.Contains("Increase Maximum age (days)", spinners);
        Assert.Contains("Decrease Maximum age (days)", spinners);
        Assert.DoesNotContain(spinners, static name => name!.Contains("PathIcon", StringComparison.Ordinal));
    }

    /// <summary>
    /// A spin button resolves its own box, so on touch it reserves above the floor rather
    /// than sitting exactly on it.
    /// </summary>
    /// <remarks>
    /// F-31 gave these the floor after they measured 34 x 46 dp. The floor alone is not
    /// enough for a control that sizes itself: Android rounds a node's two edges to physical
    /// pixels independently, and elsewhere in the product that turned an exact 48 into an
    /// exported 47.6 dp. These measured 48.0 on the device, so this is the rule being applied
    /// where the shape is, not a defect being chased.
    /// </remarks>
    [AvaloniaFact]
    public void NumericSpinnersReserveAboveTheTouchFloorOnTouch()
    {
        var previous = TouchTarget.TouchOverride;
        TouchTarget.TouchOverride = true;
        try
        {
            var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var dialog = new SessionCacheDialog(root, new ApplicationSettings());
            var window = new Window { Content = dialog, Width = 380, Height = 620 };
            window.Show();
            window.UpdateLayout();

            var days = Named<NumericUpDown>(dialog, "Maximum age (days)");
            var spinners = days.GetVisualDescendants()
                .OfType<Button>()
                .Where(static button => !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                .ToArray();

            Assert.Equal(2, spinners.Length);
            Assert.All(
                spinners,
                button =>
                {
                    Assert.True(
                        button.MinWidth >= TouchTarget.MinimumWithEdgeReserve,
                        $"{AutomationProperties.GetName(button)} is {button.MinWidth:0.#} dp wide");
                    Assert.True(
                        button.MinHeight >= TouchTarget.MinimumWithEdgeReserve,
                        $"{AutomationProperties.GetName(button)} is {button.MinHeight:0.#} dp tall");
                });
        }
        finally
        {
            TouchTarget.TouchOverride = previous;
        }
    }

    // ---------------------------------------------------------------- ---

    private static Task UsingPhoneWorkspace(
        double width,
        double height,
        Func<SessionWorkspaceView, Window, Task> body) =>
        UsingPhoneWorkspace(width, height, (view, window, _) => body(view, window));

    private static Task UsingPhoneWorkspace(
        double width,
        double height,
        Func<SessionWorkspaceView, Window, SessionTabViewModel, Task> body) =>
        UsingPhoneWorkspaceCore(width, height, body);

    private static async Task UsingPhoneWorkspaceCore(
        double width,
        double height,
        Func<SessionWorkspaceView, Window, SessionTabViewModel, Task> body)
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var tab = new SessionTabViewModel("On-device logcat 21-04-33", root);
        try
        {
            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = width, Height = height };
            window.Show();

            // Two passes: the entries floor is measured from the first arrange and applied to
            // the second, which is the whole point of measuring it rather than assuming it.
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            await body(view, window, tab);
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static T Named<T>(Visual scope, string name)
        where T : Visual =>
        scope.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => AutomationProperties.GetName(candidate) == name)
        ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}'.");

    private static double Top(Visual control) =>
        control.TranslatePoint(new Point(0, 0), Root(control))!.Value.Y;

    private static Visual Root(Visual control)
    {
        var root = control;
        while (root.GetVisualParent() is { } parent)
        {
            root = parent;
        }

        return root;
    }

    private static Color Resolve(Window window, string key) =>
        window.TryFindResource(key, window.ActualThemeVariant, out var value) &&
        value is ISolidColorBrush brush
            ? brush.Color
            : throw new InvalidOperationException($"'{key}' did not resolve to a brush.");

    private static Color CommandBarTop(MainView view) =>
        view.GetVisualDescendants()
            .OfType<Border>()
            .Select(static border => border.Background as LinearGradientBrush)
            .First(static brush => brush is { GradientStops.Count: 2 })!
            .GradientStops[0]
            .Color;

    /// <summary>WCAG 2.x contrast ratio between two opaque colors.</summary>
    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var scaled = value / 255d;
        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}
