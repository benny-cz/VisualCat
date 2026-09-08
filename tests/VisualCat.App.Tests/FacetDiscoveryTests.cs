using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using VisualCat.App.Presentation;
using VisualCat.Core.Store;

namespace VisualCat.App.Tests;

/// <summary>
/// Every active filter value stays readable and individually removable.
/// </summary>
/// <remarks>
/// The pane lists the top twenty values by count, which is a good summary and was also the
/// only way to edit one. Sorting active values to the top could reorder only the twenty that
/// survived ranking: a rare tag the reader had already included was simply absent, and the
/// only way to remove it was to clear the whole dimension. Templates were the worst case —
/// they had no group at all, and an active one read as <c>template = 3821941</c>.
/// </remarks>
public sealed class FacetDiscoveryTests
{
    [AvaloniaFact]
    public async Task ARareIncludedTagOutsideTheTopTwentyStaysVisibleAndRemovable()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);

        // Tag20 is the least frequent of twenty-one tags, so ranking leaves it out.
        Assert.DoesNotContain("Tag20", FacetRowNames(fixture));

        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, FacetKey.OfText("Tag20"), exclude: false);
        await SettleAsync(fixture);

        var row = Assert.Single(
            FacetRowNames(fixture),
            static name => name.StartsWith("Tag Tag20,", StringComparison.Ordinal));
        Assert.Contains("2 entries", row, StringComparison.Ordinal);

        // Its own undo, not the group's Clear: pressing the row's include button again is
        // what returns this one value to neutral.
        var undo = FacetButton(fixture, "Stop including Tag Tag20,");
        RaiseClick(undo);
        await SettleAsync(fixture);

        Assert.Empty(fixture.Tab.Filter.IncludedTags);
        Assert.DoesNotContain("Tag20", FacetRowNames(fixture));
    }

    [AvaloniaFact]
    public async Task AnExcludedValueWithNoRemainingMatchesIsStillIndividuallyRemovable()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);

        // Two constraints that cannot both hold: including Tag00 leaves Tag20 with no
        // matches at all, and a zero-count value is exactly the one ranking cannot return.
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, FacetKey.OfText("Tag20"), exclude: true);
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, FacetKey.OfText("Tag00"), exclude: false);
        await SettleAsync(fixture);

        Assert.Single(
            FacetRowNames(fixture),
            static name => name.StartsWith("Tag Tag20,", StringComparison.Ordinal));
        RaiseClick(FacetButton(fixture, "Stop excluding Tag Tag20,"));
        await SettleAsync(fixture);

        Assert.Empty(fixture.Tab.Filter.ExcludedTags);
        Assert.Equal(["Tag00"], fixture.Tab.Filter.IncludedTags.Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task ManyActiveValuesKeepTwentyRowsAndOfferTheFullEditor()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        for (var i = 0; i <= 20; i++)
        {
            await fixture.Tab.ToggleFacetAsync(
                FacetDimension.Tag,
                FacetKey.OfText($"Tag{i:00}"),
                exclude: false);
        }

        await SettleAsync(fixture);

        var rows = FacetRowNames(fixture)
            .Where(static name => name.StartsWith("Tag Tag", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(20, rows.Length);

        // None of the twenty-one becomes unreachable: the pane says how many there are and
        // offers the route to all of them rather than drawing every one.
        var edit = fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .Single(static button => AutomationProperties.GetName(button) == "Edit all 21 active tag filters");
        Assert.True(edit.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task AnActiveTemplateIsReadableByItsTextAndRemovableOnItsOwn()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TemplateLog());
        await SettleAsync(fixture);

        var templates = fixture.Tab.Snapshot!.Templates;
        Assert.True(templates.Count >= 21, $"the fixture mined only {templates.Count} templates");

        // The least frequent shape, which is the one ranking drops first.
        var rare = Assert.Single(
            templates,
            static template => template.CanonicalText.Contains(RarestShapeWord, StringComparison.Ordinal));
        await fixture.Tab.ToggleFacetAsync(
            FacetDimension.Template,
            FacetKey.OfTemplate(rare.TemplateId),
            exclude: false);
        await SettleAsync(fixture);

        // Readable by its canonical text in the group and in the chip strip, not as a number.
        Assert.Contains(
            FacetRowNames(fixture),
            name => name.StartsWith($"Template {rare.CanonicalText},", StringComparison.Ordinal));
        var chip = Assert.Single(
            ChipTexts(fixture),
            static text => text.StartsWith("template = ", StringComparison.Ordinal));
        Assert.Contains(rare.CanonicalText, chip, StringComparison.Ordinal);
        Assert.False(
            uint.TryParse(chip["template = ".Length..], out _),
            $"the chip still reads as a raw template id: {chip}");

        RaiseClick(FacetButton(fixture, $"Stop including Template {rare.CanonicalText},"));
        await SettleAsync(fixture);

        // The group exists only while a template filter does.
        Assert.Empty(fixture.Tab.Filter.IncludedTemplates);
        Assert.DoesNotContain(
            FacetRowNames(fixture),
            static name => name.StartsWith("Template ", StringComparison.Ordinal));
        Assert.DoesNotContain(
            ChipTexts(fixture),
            static text => text.StartsWith("template = ", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ATemplateWithNoDefinitionDegradesToItsNumberRatherThanVanishing()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TemplateLog());
        await SettleAsync(fixture);

        const uint absent = 3821941;
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Template, FacetKey.OfTemplate(absent), exclude: false);
        await SettleAsync(fixture);

        Assert.Contains(
            FacetRowNames(fixture),
            static name => name.StartsWith("Template Template 3821941,", StringComparison.Ordinal));
        Assert.Contains(
            ChipTexts(fixture),
            static text => text.Contains("Template 3821941", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task EveryBrowsableGroupOffersFindAndNamesTheDimensionItSearches()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);

        var find = fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .Select(AutomationProperties.GetName)
            .Where(static name => name?.StartsWith("Find ", StringComparison.Ordinal) == true)
            .ToArray();

        // The button is named after the browser it opens, verbatim. Lower-casing the group
        // heading instead made it announce "Find pids" for a dialog titled "Find PIDs".
        Assert.Contains("Find tags", find);
        Assert.Contains("Find PIDs", find);
        Assert.Contains("Find threads", find);
        Assert.Contains("Find buffers", find);
        Assert.DoesNotContain("Find pids", find);

        FacetDimension? requested = null;
        fixture.View.FindFacetRequested += dimension => requested = dimension;
        RaiseClick(fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .First(static button => AutomationProperties.GetName(button) == "Find tags"));

        Assert.Equal(FacetDimension.Tag, requested);
    }

    /// <summary>
    /// The browser's own include button applies the filter it names.
    /// </summary>
    /// <remarks>
    /// Every earlier test drove the three states through <c>ToggleFacetAsync</c> directly —
    /// "the way the browser drives them" — so nothing covered the browser's button itself.
    /// On the device pressing + selected the row and left the workspace unfiltered.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheBrowsersOwnIncludeButtonAppliesTheFilter()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TwoDimensionLog());
        await SettleAsync(fixture);

        using var browser = new VisualCat.App.Views.FacetBrowserDialog(fixture.Tab, FacetDimension.Pid);
        var host = new Window { Content = browser, Width = 680, Height = 680 };
        host.Show();
        try
        {
            browser.NotifyPresented();
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => BrowserButton(browser, "Include PID 100") is not null,
                passes: 200);

            var include = BrowserButton(browser, "Include PID 100");
            Assert.NotNull(include);

            // Pressed where a finger lands — the middle of the target — not by raising Click.
            // The glyph sits left of centre, and with no background brush the rest of the
            // button was not hit-tested at all: the press fell through to the row, which
            // selected itself and left the workspace unfiltered.
            Tap(host, include);
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => fixture.Tab.StateOf(FacetDimension.Pid, FacetKey.OfNumber(100)) == FacetState.Included,
                passes: 200);

            Assert.Equal(FacetState.Included, fixture.Tab.StateOf(FacetDimension.Pid, FacetKey.OfNumber(100)));

            // And the browser then shows it where the reader can undo it on its own.
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => BrowserButton(browser, "Stop including PID 100") is not null,
                passes: 200);
            var undo = BrowserButton(browser, "Stop including PID 100");
            Assert.NotNull(undo);
            Tap(host, undo);
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => fixture.Tab.StateOf(FacetDimension.Pid, FacetKey.OfNumber(100)) == FacetState.Neutral,
                passes: 200);
            Assert.Equal(FacetState.Neutral, fixture.Tab.StateOf(FacetDimension.Pid, FacetKey.OfNumber(100)));
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// A list search that matches nothing says so, and offers the way back.
    /// </summary>
    /// <remarks>
    /// Two different empty states share one list. "Nothing is available under your other
    /// filters" is a workspace problem the chips outside this dialog answer; "nothing matches
    /// what you typed here" is this dialog's own, and the way out of it is a button rather
    /// than an instruction to clear a field the reader may not connect to the empty list.
    /// </remarks>
    [AvaloniaFact]
    public async Task AListSearchWithNoResultSaysSoAndOffersTheWayBack()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);

        using var browser = new VisualCat.App.Views.FacetBrowserDialog(fixture.Tab, FacetDimension.Tag);
        var host = new Window { Content = browser, Width = 680, Height = 680 };
        host.Show();
        try
        {
            browser.NotifyPresented();
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => browser.GetLogicalDescendants().OfType<ListBox>().Sum(static list => list.ItemCount) > 0,
                passes: 200);

            var search = browser.GetLogicalDescendants().OfType<TextBox>().First();
            search.Text = "no-such-tag";
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => BrowserTexts(browser).Any(static text => text.StartsWith("No tags match", StringComparison.Ordinal)),
                passes: 200);

            Assert.Contains(
                BrowserTexts(browser),
                static text => text == "No tags match “no-such-tag”.");
            Assert.DoesNotContain(
                BrowserTexts(browser),
                static text => text.Contains("available with the other filters", StringComparison.Ordinal));

            var clear = Assert.Single(
                browser.GetLogicalDescendants().OfType<Button>(),
                static button => (button.Content as string) == "Clear list search");
            Assert.True(clear.IsVisible);

            RaiseClick(clear);
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => browser.GetLogicalDescendants().OfType<ListBox>().Sum(static list => list.ItemCount) > 0,
                passes: 200);

            Assert.Equal(string.Empty, search.Text);
            Assert.False(clear.IsVisible);
        }
        finally
        {
            host.Close();
            await browser.DrainAsync();
        }
    }

    /// <summary>
    /// Closing the browser gives the capture back, so it can be deleted again.
    /// </summary>
    /// <remarks>
    /// The browser pins a snapshot of its own and holds a deletion work lease for as long as
    /// it is open. A leaked lease is invisible: nothing on screen would explain why a capture
    /// has stopped being deletable, which is why the close path drains its queries before it
    /// releases either resource.
    /// </remarks>
    [AvaloniaFact]
    public async Task ClosingTheBrowserReleasesTheWorkLeaseItHeld()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);
        var path = fixture.Tab.SessionPath;
        Assert.False(SessionAccess.IsWorking(path));

        var browser = new VisualCat.App.Views.FacetBrowserDialog(fixture.Tab, FacetDimension.Tag);
        var host = new Window { Content = browser, Width = 680, Height = 680 };
        host.Show();
        browser.NotifyPresented();
        PixelGestureAndTextScaleTests.PumpUntil(
            host,
            () => SessionAccess.IsWorking(path),
            passes: 200);
        Assert.True(SessionAccess.IsWorking(path), "the open browser must hold its capture");

        host.Close();
        await browser.DrainAsync();
        browser.Dispose();

        Assert.False(SessionAccess.IsWorking(path));
    }

    private static string[] BrowserTexts(Control browser) => browser.GetLogicalDescendants()
        .OfType<TextBlock>()
        .Where(static text => text.IsVisible)
        .Select(static text => text.Text ?? string.Empty)
        .Where(static text => text.Length > 0)
        .ToArray();

    /// <summary>Presses the middle of a control the way a finger does.</summary>
    private static void Tap(Window host, Visual control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            host);
        Assert.NotNull(point);
        host.MouseMove(point.Value);
        host.MouseDown(point.Value, MouseButton.Left);
        host.MouseUp(point.Value, MouseButton.Left);
    }

    private static Button? BrowserButton(Control browser, string name) =>
        browser.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == name);

    /// <summary>
    /// Every browser row carries its own sentence, including the one a screen reader reaches.
    /// </summary>
    /// <remarks>
    /// A ListBoxItem with no name of its own falls back to the item's <c>ToString()</c>, and
    /// a record's generated one is its C# declaration. On the device TalkBack read
    /// <c>FacetBrowserRow { Value = FacetQueryValue { Key = …</c> where the tag's name belonged.
    /// </remarks>
    [AvaloniaFact]
    public async Task EveryBrowserRowNamesItsValueRatherThanItsRecord()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(DecliningTagLog());
        await SettleAsync(fixture);

        using var browser = new VisualCat.App.Views.FacetBrowserDialog(fixture.Tab, FacetDimension.Tag);
        var host = new Window { Content = browser, Width = 680, Height = 680 };
        host.Show();
        try
        {
            browser.NotifyPresented();
            var values = browser.GetLogicalDescendants().OfType<ListBox>().ToArray();
            PixelGestureAndTextScaleTests.PumpUntil(
                host,
                () => values.Sum(static list => list.ItemCount) > 0,
                passes: 200);

            var rows = values
                .SelectMany(static list => list.ItemsSource?.Cast<object>() ?? [])
                .Select(static item => item.ToString() ?? string.Empty)
                .ToArray();

            Assert.NotEmpty(rows);
            Assert.DoesNotContain(rows, static row => row.Contains("FacetBrowserRow", StringComparison.Ordinal));
            Assert.All(rows, row => Assert.StartsWith("tag Tag", row, StringComparison.Ordinal));
            Assert.Contains(rows, static row => row.EndsWith(" entries", StringComparison.Ordinal));
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// A group ignores its own filters, so its other values still offer themselves.
    /// </summary>
    /// <remarks>
    /// This is the semantics the heading now discloses. Including Tag00 leaves Tag01 with a
    /// positive count even though no Tag01 row is on screen, because that count is the
    /// question "how many would I get if I added this too" — which is what the + button does.
    /// Every browsable dimension answers it the same way.
    /// </remarks>
    [AvaloniaFact]
    public async Task IncludingOneValueLeavesItsNeighboursCountedUnderTheOtherFilters()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TwoDimensionLog());
        await SettleAsync(fixture);

        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, FacetKey.OfText("Alpha"), exclude: false);
        await SettleAsync(fixture);

        // The entry list holds only Alpha rows...
        Assert.All(fixture.Tab.Entries, static entry => Assert.Equal("Alpha", entry.Tag));

        // ...while Bravo still reports what adding it would bring, in every dimension that
        // ignores its own filters.
        await SettleAsync(fixture, static rows =>
            rows.Any(static name => name.StartsWith("Tag Bravo,", StringComparison.Ordinal)));
        Assert.Contains(
            FacetRowNames(fixture),
            static name => name.StartsWith("Tag Bravo, 1 entry", StringComparison.Ordinal));

        // Omission is per dimension, not global: with Alpha included, the PID group is
        // narrowed by it and honestly shows only Alpha's PID.
        Assert.DoesNotContain(FacetRowNames(fixture), static name => name.StartsWith("Pid 200,", StringComparison.Ordinal));

        // The same rule read from the other side: a PID filter leaves the other PIDs counted.
        await fixture.Tab.ClearFiltersAsync();
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Pid, FacetKey.OfNumber(100), exclude: false);
        await SettleAsync(fixture, static rows =>
            rows.Any(static name => name.StartsWith("Pid 200,", StringComparison.Ordinal)));
        Assert.Contains(FacetRowNames(fixture), static name => name.StartsWith("Pid 200, 1 entry", StringComparison.Ordinal));
        Assert.DoesNotContain(FacetRowNames(fixture), static name => name.StartsWith("Tag Bravo,", StringComparison.Ordinal));

        await fixture.Tab.ClearFiltersAsync();
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tid, FacetKey.OfNumber(201), exclude: false);
        await SettleAsync(fixture, static rows =>
            rows.Any(static name => name.StartsWith("Thread 202,", StringComparison.Ordinal)));
        Assert.Contains(FacetRowNames(fixture), static name => name.StartsWith("Thread 202, 1 entry", StringComparison.Ordinal));
    }

    /// <summary>
    /// Include, exclude and neutral round-trip through the same three states either way.
    /// </summary>
    [AvaloniaFact]
    public async Task ThePressedValueRoundTripsThroughTheSameThreeStates()
    {
        await using var fixture = await LiveTestWorkspaceFixture.CreateAsync(TwoDimensionLog());
        await SettleAsync(fixture);
        var key = FacetKey.OfText("Alpha");

        Assert.Equal(FacetState.Neutral, fixture.Tab.StateOf(FacetDimension.Tag, key));

        RaiseClick(FacetButton(fixture, "Include Tag Alpha,"));
        await SettleAsync(fixture);
        Assert.Equal(FacetState.Included, fixture.Tab.StateOf(FacetDimension.Tag, key));

        RaiseClick(FacetButton(fixture, "Exclude Tag Alpha,"));
        await SettleAsync(fixture);
        Assert.Equal(FacetState.Excluded, fixture.Tab.StateOf(FacetDimension.Tag, key));

        RaiseClick(FacetButton(fixture, "Stop excluding Tag Alpha,"));
        await SettleAsync(fixture);
        Assert.Equal(FacetState.Neutral, fixture.Tab.StateOf(FacetDimension.Tag, key));

        // The same three transitions, driven the way the browser drives them.
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, key, exclude: false);
        Assert.Equal(FacetState.Included, fixture.Tab.StateOf(FacetDimension.Tag, key));
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, key, exclude: true);
        Assert.Equal(FacetState.Excluded, fixture.Tab.StateOf(FacetDimension.Tag, key));
        await fixture.Tab.ToggleFacetAsync(FacetDimension.Tag, key, exclude: true);
        Assert.Equal(FacetState.Neutral, fixture.Tab.StateOf(FacetDimension.Tag, key));
    }

    /// <summary>Two tags, two PIDs and two threads, one record each.</summary>
    private const string TwoDimensionLogText =
        "01-01 00:00:00.000000   100   201 I Alpha          : first record\n" +
        "01-01 00:00:01.000000   200   202 I Bravo          : second record\n";

    private static string TwoDimensionLog() => TwoDimensionLogText;

    private static string[] FacetRowNames(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<Grid>()
            .Select(AutomationProperties.GetName)
            .Where(static name => !string.IsNullOrEmpty(name))
            .Select(static name => name!)
            .ToArray();

    private static string[] ChipTexts(LiveTestWorkspaceFixture fixture) =>
        fixture.View.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(static text => text.Text ?? string.Empty)
            .Concat(fixture.View.GetLogicalDescendants()
                .OfType<Button>()
                .Select(static button => AutomationProperties.GetName(button) ?? string.Empty))
            .ToArray();

    /// <summary>
    /// One facet row's own include or exclude button, found by what it says it acts on.
    /// </summary>
    /// <remarks>
    /// The name carries the value and its count — "Stop including Tag Tag20, 2 entries" —
    /// so the prefix is the part that identifies the action without pinning the count.
    /// </remarks>
    private static Button FacetButton(LiveTestWorkspaceFixture fixture, string namePrefix) =>
        fixture.View.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button =>
                AutomationProperties.GetName(button)?.StartsWith(namePrefix, StringComparison.Ordinal) == true);

    private static void RaiseClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Waits for the query and then for the pane that draws it.
    /// </summary>
    /// <remarks>
    /// The workspace batches its redraws, so a settled query is not yet a rebuilt facet pane.
    /// Where a test depends on what the pane holds it says so, and this waits for that rather
    /// than for a fixed number of dispatcher passes that happened to be enough once.
    /// </remarks>
    private static async Task SettleAsync(
        LiveTestWorkspaceFixture fixture,
        Func<string[], bool>? until = null)
    {
        PixelGestureAndTextScaleTests.PumpUntil(
            fixture.Window,
            () => fixture.Tab.Statistics is not null && !fixture.Tab.IsQueryPending);
        await Task.Yield();
        PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, () => true, passes: 5);
        if (until is null)
        {
            return;
        }

        for (var pass = 0; pass < 40 && !until(FacetRowNames(fixture)); pass++)
        {
            PixelGestureAndTextScaleTests.PumpUntil(fixture.Window, static () => true, passes: 4);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Twenty-one tags with strictly declining counts: Tag00 has 22, Tag20 has 2.</summary>
    private static string DecliningTagLog()
    {
        var builder = new StringBuilder();
        var second = 0;
        for (var tag = 0; tag <= 20; tag++)
        {
            for (var repeat = 0; repeat < 22 - tag; repeat++)
            {
                builder.Append("01-01 00:00:")
                    .Append((second / 1_000_000 % 60).ToString("00", CultureInfo.InvariantCulture))
                    .Append('.')
                    .Append((second % 1_000_000).ToString("000000", CultureInfo.InvariantCulture))
                    .Append("   100   101 I Tag")
                    .Append(tag.ToString("00", CultureInfo.InvariantCulture))
                    .Append("          : sample record\n");
                second++;
            }
        }

        return builder.ToString();
    }

    /// <summary>The word carried by the least frequent mined shape.</summary>
    private const string RarestShapeWord = "uuuuu";

    /// <summary>
    /// Twenty-one message shapes with declining frequency, so mining produces twenty-one
    /// templates and the rarest falls outside any ranked list of twenty.
    /// </summary>
    /// <remarks>
    /// The shapes differ by a whole word rather than by a number: the miner normalises
    /// numbers into placeholders, so twenty-one numbered variants of one sentence are one
    /// template, not twenty-one.
    /// </remarks>
    private static string TemplateLog()
    {
        var builder = new StringBuilder();
        var second = 0;
        for (var shape = 0; shape <= 20; shape++)
        {
            var word = new string((char)('a' + shape), 5);
            for (var repeat = 0; repeat < 22 - shape; repeat++)
            {
                builder.Append("01-01 00:00:")
                    .Append((second / 1_000_000 % 60).ToString("00", CultureInfo.InvariantCulture))
                    .Append('.')
                    .Append((second % 1_000_000).ToString("000000", CultureInfo.InvariantCulture))
                    .Append("   100   101 I Worker         : ")
                    .Append(word)
                    .Append(" completed step ")
                    .Append(repeat.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
                second++;
            }
        }

        return builder.ToString();
    }
}
