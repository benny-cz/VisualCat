using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VisualCat.App.Views;

namespace VisualCat.App.Tests;

/// <summary>
/// A command that cannot run says something true about why, or says nothing.
/// </summary>
/// <remarks>
/// <para>
/// The command sheet explained every refusal it could not name as <c>needs an open session</c>.
/// With a session open whose lines all reached the timeline, <em>Lines not on the timeline…</em>
/// therefore sat greyed under a sentence that was simply false — and on the desktop the same
/// command was not greyed at all, because the overflow menu never read its availability.
/// </para>
/// <para>
/// The shell answers first for the states it owns, then the command answers for itself, and
/// what neither can name honestly stays unnamed.
/// </para>
/// </remarks>
public sealed class UnavailableCommandTests
{
    [AvaloniaFact]
    public async Task WithNoSessionTheSheetSaysSoAndTheOverflowRefusesToo()
    {
        await using var shell = await ShellFixture.CreateAsync();

        var sheetItem = Assert.Single(
            shell.View.CommandSheetForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.False(sheetItem.Enabled);
        Assert.Equal(
            "Stack-trace frames and records with no usable timestamp · needs an open session",
            sheetItem.Description);

        var overflowItem = Assert.Single(
            shell.View.OverflowMenuForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.False(overflowItem.Enabled);
        Assert.Equal("needs an open session", overflowItem.Help);

        // The commands that genuinely need a session keep saying so. Refusing to invent a
        // reason must not take away the one reason the shell can always check.
        var export = Assert.Single(
            shell.View.CommandSheetForTest(),
            static entry => entry.Label.StartsWith("Export CSV", StringComparison.Ordinal));
        Assert.False(export.Enabled);
        Assert.EndsWith("needs an open session", export.Description!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A session whose lines all reached the timeline is not a missing session.
    /// </summary>
    [AvaloniaFact]
    public async Task AnOpenSessionWithEveryLineOnTheTimelineIsNotCalledAMissingSession()
    {
        await using var shell = await ShellFixture.CreateAsync();
        await shell.OpenAsync(
            "01-01 00:00:00.000000   100   101 I Worker         : every line parses\n" +
            "01-01 00:00:01.000000   100   101 I Worker         : and reaches the timeline\n");

        var tab = shell.View.Workspace.Selected;
        Assert.NotNull(tab);
        Assert.Equal(0, tab.OffTimelineCount);

        var sheetItem = Assert.Single(
            shell.View.CommandSheetForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.False(sheetItem.Enabled);
        Assert.Equal(
            "Stack-trace frames and records with no usable timestamp · this session has none",
            sheetItem.Description);
        Assert.DoesNotContain("open session", sheetItem.Description!, StringComparison.Ordinal);

        var overflowItem = Assert.Single(
            shell.View.OverflowMenuForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.False(overflowItem.Enabled);
        Assert.Equal("this session has none", overflowItem.Help);
    }

    /// <summary>
    /// A session that does carry off-timeline lines offers the command on both surfaces.
    /// </summary>
    [AvaloniaFact]
    public async Task ASessionWithLinesOffTheTimelineOffersTheCommandWithoutAReason()
    {
        await using var shell = await ShellFixture.CreateAsync();
        await shell.OpenAsync(
            "01-01 00:00:00.000000   100   101 E Crash          : FATAL EXCEPTION: main\n" +
            "\tat com.example.Thing.method(Thing.java:42)\n");

        var tab = shell.View.Workspace.Selected;
        Assert.NotNull(tab);
        Assert.True(tab.OffTimelineCount > 0, "the fixture must carry a line the timeline does not");

        var sheetItem = Assert.Single(
            shell.View.CommandSheetForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.True(sheetItem.Enabled);
        Assert.Equal("Stack-trace frames and records with no usable timestamp", sheetItem.Description);

        var overflowItem = Assert.Single(
            shell.View.OverflowMenuForTest(),
            static entry => entry.Label.StartsWith("Lines not on the timeline", StringComparison.Ordinal));
        Assert.True(overflowItem.Enabled);
        Assert.Null(overflowItem.Help);
    }

    private sealed class ShellFixture : IAsyncDisposable
    {
        private readonly Window _window;
        private readonly string _root;

        private ShellFixture(MainView view, Window window, string root)
        {
            View = view;
            _window = window;
            _root = root;
        }

        internal MainView View { get; }

        internal static Task<ShellFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"visualcat-commands-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            VisualCat.App.Presentation.WorkspaceViewModel.ConfigureTemporarySessionRoot(root);
            var view = new MainView(null, Path.Combine(root, "settings.json"));
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            window.UpdateLayout();
            return Task.FromResult(new ShellFixture(view, window, root));
        }

        internal async Task OpenAsync(string log)
        {
            var path = Path.Combine(_root, $"{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(path, log, TestContext.Current.CancellationToken);
            await View.Workspace.ImportFileAsync(path, TestContext.Current.CancellationToken);
            PixelGestureAndTextScaleTests.PumpUntil(
                _window,
                () => View.Workspace.Selected?.Snapshot is not null);
        }

        public async ValueTask DisposeAsync()
        {
            _window.Close();
            Platform.EdgeGestureGuard.Reset();
            await View.Workspace.DisposeAsync();
            VisualCat.App.Presentation.WorkspaceViewModel.ConfigureTemporarySessionRoot(null);
            if (Directory.Exists(_root))
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
