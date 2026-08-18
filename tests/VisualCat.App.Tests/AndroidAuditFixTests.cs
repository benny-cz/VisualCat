using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using VisualCat.App.Presentation;
using VisualCat.App.Views;
using VisualCat.Domain.Entries;
using VisualCat.Infrastructure.Configuration;

namespace VisualCat.App.Tests;

/// <summary>
/// The fixes from the Android companion UX/UI audit (docs/ANDROID-UX-AUDIT.md), at the level
/// each one can be checked without a device.
/// </summary>
public sealed class AndroidAuditFixTests
{
    /// <summary>
    /// F17 — a session imported by an older build stored the materialised temporary file's
    /// name in its manifest, so its tab, its shared archive and its exported CSV all carried
    /// a 32-hex guid that the same session's row in Recent sessions never showed.
    /// </summary>
    [Theory]
    [InlineData("b66e69fe00aa4716a57541338b6bc29d-demo-small.txt", "demo-small.txt")]
    [InlineData("demo-small.txt", "demo-small.txt")]
    [InlineData("", "demo-small")]
    [InlineData(null, "demo-small")]
    [InlineData("b66e69fe00aa4716a57541338b6bc29d.txt", "demo-small")]
    public void AStoredNameGivesUpItsMaterializationPrefix(string? stored, string expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "20260812-051531-demo-small-b66e69fe00aa4716a57541338b6bc29d.vcat");

        Assert.Equal(expected, SessionCacheName.DescribeSession(folder, stored));
    }

    /// <summary>
    /// F17 — a name a person chose is left exactly as it is, guid-shaped characters included,
    /// as long as it is not the whole name.
    /// </summary>
    [Fact]
    public void ARealNameSurvivesUnchanged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "20260812-051531-anything-b66e69fe00aa4716a57541338b6bc29d.vcat");

        Assert.Equal("deadbeef-run.txt", SessionCacheName.DescribeSession(folder, "deadbeef-run.txt"));
    }

    /// <summary>
    /// F15a — the source-context gutter says what the parser made of a line in words a reader
    /// can use, instead of printing the <c>ParseOutcomeKind</c> enum name into a panel
    /// subtitled "exact bytes".
    /// </summary>
    [Fact]
    public void EveryParseOutcomeHasAFixedWidthGutterTag()
    {
        foreach (var outcome in Enum.GetValues<ParseOutcomeKind>())
        {
            var tag = SessionTabViewModel.DescribeOutcome(outcome);

            Assert.Equal(2, tag.Length);
            Assert.DoesNotContain(outcome.ToString(), tag, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// F3 — the open-session list survives a settings round trip, so a workspace discarded by
    /// one accidental Back press can be put back.
    /// </summary>
    [Fact]
    public async Task TheOpenWorkspaceIsRememberedAcrossARestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new ApplicationSettings
            {
                OpenSessionPaths = ["/sessions/a.vcat", "/sessions/b.vcat", "/sessions/b.vcat"],
                OpenSessionIndex = 1,
            });

            var reloaded = await store.LoadAsync();

            // Duplicates are dropped, so the index has to address what survived.
            Assert.NotNull(reloaded.OpenSessionPaths);
            Assert.Equal(["/sessions/a.vcat", "/sessions/b.vcat"], reloaded.OpenSessionPaths!);
            Assert.Equal(1, reloaded.OpenSessionIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// F3 — an index left pointing past the end of a truncated list selects a session that
    /// exists rather than throwing during startup.
    /// </summary>
    [Fact]
    public async Task ARestoredSelectionIsClampedToTheSessionsThatSurvived()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VisualCat.App.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new ApplicationSettings
            {
                OpenSessionPaths = ["/sessions/a.vcat"],
                OpenSessionIndex = 9,
            });

            Assert.Equal(0, (await store.LoadAsync()).OpenSessionIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// F5 — every confirmation and every failure the shell reports went into the brand row,
    /// and the brand row is hidden on Android whenever a session is open. A failure now has a
    /// surface of its own, announced to a screen reader as soon as it arrives.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailureIsShownOnASurfaceOfItsOwn()
    {
        await using var view = new MainView();
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            var notice = FindNotice(view);
            Assert.NotNull(notice);
            Assert.False(notice.IsVisible);

            view.ShowNotice("Could not complete that action · disk full", MainView.NoticeKind.Failure);

            // The lane is Android's, because only Android hides the brand row that carries the
            // same message on the desktop. What is platform-independent is that the message
            // reaches a surface of its own and interrupts a screen reader rather than waiting.
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(notice));
            Assert.Contains(
                view.GetLogicalDescendants().OfType<TextBlock>(),
                static block => (block.Text ?? string.Empty).Contains("disk full", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F5 — a message can always be taken down, and taking it down leaves nothing behind for
    /// the next one to be confused with.
    /// </summary>
    [AvaloniaFact]
    public async Task AMessageCanBeDismissed()
    {
        await using var view = new MainView();
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            view.ShowNotice("Something went wrong", MainView.NoticeKind.Failure);
            Assert.Contains(
                view.GetLogicalDescendants().OfType<TextBlock>(),
                static block => block.Text == "Something went wrong");

            var dismiss = view.GetLogicalDescendants()
                .OfType<Button>()
                .Single(static button =>
                    AutomationProperties.GetName(button) == "Dismiss application status message");
            dismiss.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            // Nothing is left for the next message to be read as part of.
            Assert.False(FindNotice(view)!.IsVisible);
            Assert.DoesNotContain(
                view.GetLogicalDescendants().OfType<TextBlock>(),
                static block => block.Text == "Something went wrong");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F21.1 — "THIS SESSION" sat above Recent sessions…, Open portable archive… and Open
    /// session…, three commands that open a <em>different</em> session. Only Share and Export
    /// CSV act on the one the reader is looking at.
    /// </summary>
    [AvaloniaFact]
    public async Task TheCommandSheetGroupsOpeningApartFromActingOnThisSession()
    {
        await using var view = new MainView();

        view.OpenCommandSheet();

        var labels = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(static block => block.Text ?? string.Empty)
            .ToArray();
        var openHeading = Array.IndexOf(labels, "OPEN ANOTHER SESSION");
        var settingsHeading = Array.IndexOf(labels, "SETTINGS");

        Assert.True(openHeading >= 0, "The sheet names the group that opens another session.");
        Assert.True(settingsHeading > openHeading, "Settings come after the commands.");
        Assert.True(
            Array.IndexOf(labels, "Recent sessions…") > openHeading,
            "Recent sessions… is under the opening group.");
        Assert.True(
            Array.IndexOf(labels, "Appearance & timeline…") > settingsHeading,
            "Appearance & timeline… is under settings.");
    }

    /// <summary>
    /// F21.4 — Appearance &amp; timeline offered Close in the sheet header plus Cancel and
    /// Apply at the foot, with nothing saying whether Close saved or discarded. A dialog that
    /// carries its own decision row does not get a second, differently-worded dismissal.
    /// </summary>
    [AvaloniaFact]
    public async Task ADialogSheetDoesNotAddASecondDismissal()
    {
        // No window: with no TopLevel to own a modal, MainView presents the in-page card, which
        // is the presentation Android always gets.
        await using var view = new MainView();
        var dialog = new AppearanceDialog(new ApplicationSettings());
        var presented = view.ShowDialogAsync(dialog);

        Assert.DoesNotContain(
            view.GetLogicalDescendants().OfType<Button>(),
            static button => AutomationProperties.GetName(button) == "Close this sheet");
        Assert.Contains(
            view.GetLogicalDescendants().OfType<Button>(),
            static button => Equals(button.Content, "Cancel"));

        dialog.Dismiss();
        await presented;
    }

    /// <summary>
    /// F16 — the in-page host wrapped every dialog in a scroller, so Recent sessions' own
    /// Cancel/Open row scrolled away below nine sessions. A body that scrolls internally is
    /// given the sheet's height directly.
    /// </summary>
    [AvaloniaFact]
    public async Task ADialogThatScrollsItselfIsNotPutInsideASecondScroller()
    {
        await using var view = new MainView();
        var dialog = new RecentSessionsDialog([]);
        Assert.True(dialog.ScrollsInternally);

        var presented = view.ShowDialogAsync(dialog);

        // The dialog sits under the sheet's own content grid, with no ScrollViewer interposed
        // between it and the sheet.
        Assert.DoesNotContain(dialog.GetLogicalAncestors().OfType<ScrollViewer>(), static _ => true);

        dialog.Dismiss();
        await presented;
    }

    /// <summary>
    /// F7 — Fluent fills a selected row with a solid accent-derived brush while the row keeps
    /// its own foregrounds, which measured 1.97:1 on the metadata line. The product replaces
    /// it with a low-alpha tint and an outline, resolved per theme variant.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void SelectionIsATintRatherThanASlab(string variant)
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two" } };
        var window = new Window
        {
            Content = list,
            Width = 400,
            Height = 300,
            RequestedThemeVariant = variant == "Dark"
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light,
        };
        window.Show();
        try
        {
            list.SelectedIndex = 0;
            window.UpdateLayout();

            var presenter = list.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .First(static item => item.IsSelected)
                .GetVisualDescendants()
                .OfType<ContentPresenter>()
                .First();

            var fill = Assert.IsType<SolidColorBrush>(presenter.Background);
            Assert.InRange(fill.Color.A, 1, 80);
            Assert.NotNull(presenter.BorderBrush);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F9 — Fluent's disabled fill is the same grey as its ordinary fill, so the command sheet
    /// drew its two unusable rows as raised blocks among flat ones: the strongest cue on the
    /// sheet pointed at what could not be done. Disabled recedes now.
    /// </summary>
    [AvaloniaFact]
    public void ADisabledControlRecedes()
    {
        var enabled = new Button { Content = "Fit" };
        var disabled = new Button { Content = "Copy raw", IsEnabled = false };
        var window = new Window
        {
            Content = new StackPanel { Children = { enabled, disabled } },
            Width = 400,
            Height = 300,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var disabledFill = Presenter(disabled).Background;
            var enabledFill = Presenter(enabled).Background;

            Assert.Equal(Brushes.Transparent, disabledFill);
            Assert.True(Presenter(disabled).Opacity < 1);

            // And the enabled control still has a fill of its own, so "flat" no longer means
            // "usable" and "filled" no longer means "unavailable".
            Assert.NotNull(enabledFill);
            Assert.NotEqual(disabledFill, enabledFill);
        }
        finally
        {
            window.Close();
        }

        static ContentPresenter Presenter(Button button) =>
            button.GetVisualDescendants().OfType<ContentPresenter>().First();
    }

    private static Border? FindNotice(MainView view) =>
        view.GetLogicalDescendants()
            .OfType<Border>()
            .FirstOrDefault(static border =>
                AutomationProperties.GetLiveSetting(border) != AutomationLiveSetting.Off);
}
