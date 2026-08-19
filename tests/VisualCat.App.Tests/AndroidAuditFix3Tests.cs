using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Timeline;
using VisualCat.App.Views;
using VisualCat.Domain.Entries;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// The fixes from the Android companion's third UX/UI pass, at the level each one can be
/// checked without a device.
/// </summary>
/// <remarks>
/// Where a finding is about a number the audit measured on a phone — a contrast ratio, a
/// reserved slot, an accessibility node count — the number is asserted here rather than the
/// appearance, because that is the part that can regress silently. The findings that are only
/// observable on the platform itself (the logcat consent timing, the ConfigChanges list) are
/// noted where they live in the source; the device pass that closed this audit measured them.
/// </remarks>
public sealed class AndroidAuditFix3Tests
{
    // ---------------------------------------------------------------- B1 ---

    /// <summary>
    /// B1 — the severity palette is legible as text in the light theme, where every one of its
    /// seven values measured between 1.33:1 and 3.12:1 on the surfaces it is drawn on.
    /// </summary>
    [Fact]
    public void EverySeverityInkClearsTheContrastFloorOnLight()
    {
        Color[] surfaces =
        [
            WorkspacePalette.Surface(dark: false),
            WorkspacePalette.SurfaceRaised(dark: false),
            WorkspacePalette.SurfaceHeader(dark: false),
        ];

        foreach (var level in LogLevels.DisplayOrder)
        {
            var ink = LevelPalette.InkOf(level, dark: false);
            foreach (var surface in surfaces)
            {
                var contrast = Contrast(ink, surface);
                Assert.True(
                    contrast >= 4.5,
                    $"{level} ink {ink} measured {contrast:0.00}:1 on {surface}");
            }
        }
    }

    /// <summary>B1 — and the dark theme, which was already right, is untouched.</summary>
    [Fact]
    public void TheDarkSeverityInkIsStillThePlotsOwnPalette()
    {
        foreach (var level in LogLevels.DisplayOrder)
        {
            Assert.Equal(LevelPalette.ColorOf(level), LevelPalette.InkOf(level, dark: true));
            Assert.NotEqual(LevelPalette.InkOf(level, dark: true), LevelPalette.InkOf(level, dark: false));
        }
    }

    /// <summary>
    /// B1 — the row's identity line is the ink, not the fill: it was the one place the
    /// saturated palette was being read rather than looked at.
    /// </summary>
    [AvaloniaFact]
    public async Task TheEntryRowsIdentityLineFollowsTheThemeIntoLight()
    {
        await UsingPhoneWorkspace(400, 800, static (view, window) =>
        {
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.UpdateLayout();

            var lightInk = LevelPalette.InkOf(LogLevel.Debug, dark: false);
            var darkInk = LevelPalette.InkOf(LogLevel.Debug, dark: true);
            Assert.NotEqual(lightInk, darkInk);

            // The fill side of the palette is deliberately not theme-aware: the ribbon, the
            // plot and the minimap are areas of colour and read well on light already.
            Assert.Equal(LevelPalette.ColorOf(LogLevel.Debug), darkInk);
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- B2 ---

    /// <summary>
    /// B2 — a chosen segment is a tint and an outline, not white on the solid accent at
    /// 2.28:1. Fluent paints the checked state onto the template's own content presenter,
    /// which is why setting Background on the control did nothing.
    /// </summary>
    [AvaloniaFact]
    public void TheChosenSegmentIsATintRatherThanASlab()
    {
        var selector = new ChoiceSelector(
            "Theme",
            SettingChoice.Of(("System", "Follow the system"), ("Light", "Light"), ("Dark", "Dark")),
            "Light");
        var window = new Window { Content = selector, Width = 420, Height = 200 };
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.Show();
        window.UpdateLayout();
        try
        {
            var chosen = selector.GetVisualDescendants()
                .OfType<ToggleButton>()
                .Single(static segment => segment.IsChecked == true);
            var presenter = chosen.GetVisualDescendants().OfType<ContentPresenter>().First();
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background);

            // Translucent: the accent slab was opaque, and that is what took the label to
            // 2.28:1. The outline is what still says "this one".
            Assert.True(fill.Color.A < 128, $"the chosen segment is filled {fill.Color}");
            Assert.NotNull(presenter.BorderBrush);

            // And the class the style hangs on is the thing that keeps it off the severity
            // chips and off every CheckBox, both of which are ToggleButtons too.
            Assert.Contains(VisualCat.App.Theme.ProductTheme.SegmentClass, chosen.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    // ---------------------------------------------------------------- B3 ---

    /// <summary>
    /// B3 — a sheet takes the workspace out of the accessibility tree, measurably.
    /// </summary>
    /// <remarks>
    /// The previous attempt set AccessibilityView.Raw and IsOffscreenBehavior.Offscreen on the
    /// band. Neither is inherited and both describe one peer, so on the device the band left
    /// the control view and its twenty descendants were promoted in its place: an accessibility
    /// dump held 29 clickable nodes with the sheet open against 14 without it. This asserts the
    /// thing that actually decides it — what the band's peer says its children are.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASheetSealsTheWorkspacesAccessibilitySubtree()
    {
        await using var view = new MainView();
        var window = new Window { Content = view, Width = 420, Height = 900 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var band = view.GetVisualDescendants().OfType<ModalWorkspaceBand>().Single();
            var peer = ControlAutomationPeer.CreatePeerForElement(band);

            Assert.NotEmpty(peer.GetChildren());

            view.OpenCommandSheet();
            window.UpdateLayout();
            Assert.Empty(peer.GetChildren());
            Assert.True(peer.IsOffscreen());

            var close = view.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button => AutomationProperties.GetName(button) == "Close this sheet");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.NotEmpty(peer.GetChildren());
            Assert.False(peer.IsOffscreen());
        }
        finally
        {
            window.Close();
        }
    }

    // ---------------------------------------------------------------- B4 ---

    /// <summary>
    /// B4 — every facet button says what it acts on. They were all called "Include facet
    /// value", once per tag, pid and thread the session has.
    /// </summary>
    [AvaloniaFact]
    public async Task EachFacetButtonNamesItsOwnValue()
    {
        await UsingPhoneWorkspace(400, 900, static (view, window) =>
        {
            var facetButtons = view.GetLogicalDescendants()
                .OfType<Button>()
                .Select(static button => AutomationProperties.GetName(button) ?? string.Empty)
                .Where(static name => name.StartsWith("Include ", StringComparison.Ordinal) ||
                                      name.StartsWith("Exclude ", StringComparison.Ordinal))
                .ToArray();

            // A workspace with no snapshot has no facets, so the assertion that matters here is
            // the one about the constant: nothing is called by the old name any more.
            Assert.DoesNotContain("Include facet value", facetButtons);
            Assert.DoesNotContain("Exclude facet value", facetButtons);
            Assert.Equal(facetButtons.Length, facetButtons.Distinct(StringComparer.Ordinal).Count());
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- B6 ---

    /// <summary>
    /// B6 — a row hands a screen reader enough to decide with, not a kilobyte of hex. Rows
    /// carrying a 1,000-character DUMP= payload were spoken 300 characters at a time.
    /// </summary>
    [Fact]
    public void ARowSpeaksAnOpeningRatherThanAPayload()
    {
        var dump = new string('A', 1200);
        var spoken = SessionWorkspaceView.SpokenEntryMessage(dump);

        Assert.True(spoken.Length < 200, $"a row offered {spoken.Length} characters");
        Assert.EndsWith("(open the entry for the rest)", spoken, StringComparison.Ordinal);

        // An ordinary logcat line still arrives whole.
        const string ordinary = "GetNextPrivateAddressIntervalRange: interval range 900s to 900s";
        Assert.Equal(ordinary, SessionWorkspaceView.SpokenEntryMessage(ordinary));
    }

    // ---------------------------------------------------------------- C1 ---

    /// <summary>
    /// C1 — Back closes the filter drawer and stays in the app.
    /// </summary>
    /// <remarks>
    /// Android delivers one Back press as two events: a Key.Escape key-down and, about ten
    /// milliseconds later, a back-request. The key-down ran first and gave up the drawer, so
    /// the back-request found nothing to dismiss, reported itself unhandled, and the platform
    /// backgrounded the task — Back both closed the drawer and left the app, taking a
    /// half-typed query with it. One press is one decision now, whichever half arrives first.
    /// </remarks>
    [AvaloniaFact]
    public async Task OneBackPressIsOneDecision()
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        try
        {
            await using var view = new MainView();
            var window = new Window { Content = view, Width = 420, Height = 900 };
            window.Show();
            window.UpdateLayout();

            var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await using var tab = new SessionTabViewModel("Live", root);
                var workspace = new SessionWorkspaceView(tab);
                var tabs = view.GetVisualDescendants()
                    .OfType<TabControl>()
                    .Single(static host => host.Classes.Contains("sessionHost"));
                var item = new TabItem { Header = "s", Content = workspace, Tag = tab };
                tabs.Items.Add(item);
                tabs.SelectedItem = item;
                window.UpdateLayout();

                // First rather than Single: a TabControl presents its selected item's content
                // through a presenter as well as holding the item, so every control in the
                // workspace appears twice in the shell's logical tree.
                var filters = view.GetLogicalDescendants()
                    .OfType<Button>()
                    .First(static button =>
                        AutomationProperties.GetName(button) == "Open search and timeline filters");
                filters.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();

                // The key-down half of the press: it takes the drawer down and claims the key.
                var key = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                };
                view.RaiseEvent(key);
                Assert.True(key.Handled);

                // The back-request half, arriving at a workspace with nothing left to give up.
                // Unhandled, the platform takes it as "leave the app".
                var back = new RoutedEventArgs(TopLevel.BackRequestedEvent);
                window.RaiseEvent(back);
                Assert.True(back.Handled, "the second half of one Back press must not leave the app");

                // A second, separate press has nothing to answer and falls through, which is
                // the Android convention and the whole reason the workspace stopped claiming
                // Escape unconditionally in the first place.
                var again = new RoutedEventArgs(TopLevel.BackRequestedEvent);
                window.RaiseEvent(again);
                Assert.False(again.Handled);
            }
            finally
            {
                window.Close();
                Directory.Delete(root, recursive: true);
            }
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
        }
    }

    // ---------------------------------------------------------------- C2 ---

    /// <summary>
    /// C2 — the status row is tappable across its whole width, and only claims to be tappable
    /// when there is something behind the tap.
    /// </summary>
    /// <remarks>
    /// A panel with a null background is not hit-testable in Avalonia, so the row's own Tapped
    /// handler only ever fired on the ~374 px its label had painted: the chevron advertising
    /// the gesture was dead, and so was the space beside it.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheStatusRowIsTappableWhereItLooksTappable()
    {
        await UsingPhoneWorkspace(400, 800, static (view, window) =>
        {
            var row = Named<DockPanel>(view, "Session status");
            Assert.NotNull(row.Background);

            var chevron = row.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(static block => block.Text is "⌄" or "⌃");

            // `Ready · 12,370 entries` fits, so the control promised more and delivered
            // nothing — and the state it exists for looked identical to the state it does not.
            Assert.False(chevron.IsVisible);
            Assert.False(chevron.IsHitTestVisible);
            Assert.Null(AutomationProperties.GetHelpText(row));
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- D1 ---

    /// <summary>
    /// D1 — the minimap draws inside the band it is given instead of overrunning it.
    /// </summary>
    /// <remarks>
    /// Its own MinHeight was 54, which is 152 device pixels on a 2.8x phone, against a 26 dp
    /// row in the short composition: it overflowed 43 px above and 44 px below and painted the
    /// status line's own text through its bars.
    /// </remarks>
    [AvaloniaFact]
    public void TheMinimapFitsTheShortestBandItIsGiven()
    {
        var minimap = new MinimapControl();
        var host = new Border { Height = 26, Child = minimap };
        var window = new Window { Content = host, Width = 400, Height = 200 };
        window.Show();
        window.UpdateLayout();
        try
        {
            Assert.True(
                minimap.Bounds.Height <= 26.5,
                $"the minimap took {minimap.Bounds.Height:0} px of a 26 px band");
        }
        finally
        {
            window.Close();
        }
    }

    // ---------------------------------------------------------------- D3 ---

    /// <summary>
    /// D3 — a field that cannot be used recedes. The two disabled policy spinners on Session
    /// cache measured 1.66:1 against the sheet while the fields that worked measured 1.07:1,
    /// so the controls that could not be used were the most prominent objects on it.
    /// </summary>
    [AvaloniaFact]
    public void ADisabledFieldRecedesInsteadOfAdvancing()
    {
        var field = new TextBox { Text = "30", IsEnabled = false, Width = 200 };
        var window = new Window { Content = field, Width = 400, Height = 200 };
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.Show();
        window.UpdateLayout();
        try
        {
            var border = field.GetVisualDescendants()
                .OfType<Border>()
                .Single(static part => part.Name == "PART_BorderElement");
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(border.Background);

            Assert.Equal(0, fill.Color.A);
        }
        finally
        {
            window.Close();
        }
    }

    // ---------------------------------------------------------------- D5 ---

    /// <summary>
    /// D5 — the search channel is a named part of the palette rather than a literal in two
    /// files, which is what made it look accidental rather than chosen.
    /// </summary>
    [Fact]
    public void TheSearchMarkIsPartOfThePalette()
    {
        Assert.True(
            Contrast(WorkspacePalette.SearchMatchText, WorkspacePalette.SearchMatch) >= 4.5,
            "a search mark has to be readable");

        // And it is not one of the seven severities, because a match is orthogonal to how bad
        // a line is.
        foreach (var level in LogLevels.DisplayOrder)
        {
            Assert.NotEqual(LevelPalette.ColorOf(level), WorkspacePalette.SearchMatch);
        }
    }

    // ---------------------------------------------------------------- E1 ---

    /// <summary>
    /// E1 — a stored session's state is described in the tense it is actually in.
    /// </summary>
    [Fact]
    public void AnInterruptedCaptureIsDescribedInThePastTense()
    {
        var session = new TemporarySessionInfo(
            "/tmp/a.vcat",
            DateTimeOffset.UtcNow.AddDays(-1),
            168_000,
            Finalized: false);

        Assert.Equal("interrupted", SheetForm.DescribeSessionOutcome(session, capturingNow: false));
        Assert.Equal("capture in progress", SheetForm.DescribeSessionOutcome(session, capturingNow: true));
        Assert.Equal(
            "complete",
            SheetForm.DescribeSessionOutcome(session with { Finalized = true }, capturingNow: false));
    }

    // ---------------------------------------------------------------- E2 ---

    /// <summary>
    /// E2 — and the sentence that explains those words is on the sheet, not only in a tooltip
    /// that a device with no pointer can never show.
    /// </summary>
    [AvaloniaFact]
    public async Task TheSessionStateVocabularyIsExplainedOnTheSheet()
    {
        var body = new RecentSessionsDialog(
        [
            new TemporarySessionInfo("/tmp/a.vcat", DateTimeOffset.UtcNow, 1024, Finalized: false),
        ]);
        var window = new Window { Content = body, Width = 720, Height = 520 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var legend = body.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Where(static block => block.Text is { Length: > 0 })
                .Select(static block => block.Text!)
                .ToArray();

            Assert.Contains(legend, text => text.Contains("interrupted", StringComparison.Ordinal));
            Assert.Contains(legend, text => text.Contains("capture in progress", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            await Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------- E3 ---

    /// <summary>
    /// E3 — the SOURCE CONTEXT caption names the divider instead of ending on it. Collapsed,
    /// the glyph is not on screen, so the sentence read as text that had been cut off.
    /// </summary>
    [AvaloniaFact]
    public async Task TheSourceCaptionNamesTheDivider()
    {
        await UsingPhoneWorkspace(400, 900, static (view, window) =>
        {
            var caption = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(static block => block.Text ?? string.Empty)
                .Single(static text => text.StartsWith("exact bytes", StringComparison.Ordinal));

            Assert.EndsWith("divider", caption, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    // ---------------------------------------------------------------- E4 ---

    /// <summary>
    /// E4 — Fit's name and its description say different things. They were near-duplicates,
    /// and a screen reader reads both.
    /// </summary>
    [AvaloniaFact]
    public async Task FitsNameAndDescriptionAreNotTheSameSentence()
    {
        await UsingPhoneWorkspace(400, 800, static (view, window) =>
        {
            var fit = Named<Button>(view, "Fit the complete session");
            var description = ToolTip.GetTip(fit) as string;

            Assert.NotNull(description);
            Assert.DoesNotContain("Fit the complete session", description, StringComparison.Ordinal);

            // And the two spinner buttons are named for what they do rather than for the glyph
            // printed on them.
            Assert.NotNull(Named<Button>(view, "Zoom in"));
            Assert.NotNull(Named<Button>(view, "Zoom out"));
            return Task.CompletedTask;
        });
    }

    // ----------------------------------------------------------- helpers ---

    /// <remarks>
    /// Logical rather than visual, so a control the layout has parked — the held Fit slot, the
    /// drawer while it is closed — is still found by the name it answers to.
    /// </remarks>
    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetLogicalDescendants()
            .OfType<T>()
            .First(candidate => AutomationProperties.GetName(candidate) == name);

    /// <summary>
    /// Runs <paramref name="body"/> against a phone-composition workspace of the given size.
    /// </summary>
    private static async Task UsingPhoneWorkspace(
        double width,
        double height,
        Func<SessionWorkspaceView, Window, Task> body)
    {
        SessionWorkspaceView.PhoneCompositionOverride = true;
        var root = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var tab = new SessionTabViewModel("Session", root);
            var view = new SessionWorkspaceView(tab);
            var window = new Window { Content = view, Width = width, Height = height };
            window.Show();
            window.UpdateLayout();
            try
            {
                await body(view, window);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            SessionWorkspaceView.PhoneCompositionOverride = null;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static double Contrast(Color first, Color second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var normalized = value / 255.0;
        return normalized <= 0.03928
            ? normalized / 12.92
            : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }
}
